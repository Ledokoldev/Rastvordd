using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Одно TCP-соединение с кадрами «длина (4 байта) + пакет», неблокирующее (D73).
    /// TCP, а не ENet/UDP: через VPS и VPN большие UDP-датаграммы режутся (MTU), а TCP сам подбирает размер.
    /// Канал у нас и так надёжный и упорядоченный — по сути то же, что было.
    /// </summary>
    public sealed class NetLink
    {
        readonly Socket _s;
        readonly byte[] _buf = new byte[64 * 1024];
        byte[] _rx = new byte[64 * 1024];
        int _rxLen;
        readonly Queue<byte[]> _tx = new();
        int _txOff;
        public long Pending;          // байт ждёт отправки (друг завис — копится)
        public bool Closed { get; private set; }
        public string Remote;

        public NetLink(Socket s)
        {
            _s = s;
            _s.Blocking = false;
            _s.NoDelay = true;   // без задержки Нейгла: ввод и снимки — сразу
            Remote = (s.RemoteEndPoint as IPEndPoint)?.ToString();
        }

        public void Send(byte[] data)
        {
            if (Closed) return;
            var f = new byte[data.Length + 4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(f, data.Length);
            System.Buffer.BlockCopy(data, 0, f, 4, data.Length);
            _tx.Enqueue(f);
            Pending += f.Length;
            Flush();
        }

        void Flush()
        {
            try
            {
                while (_tx.Count > 0)
                {
                    var f = _tx.Peek();
                    int n = _s.Send(f, _txOff, f.Length - _txOff, SocketFlags.None, out var err);
                    if (err == SocketError.WouldBlock) return;
                    if (err != SocketError.Success) { Close(); return; }
                    _txOff += n; Pending -= n;
                    if (_txOff >= f.Length) { _tx.Dequeue(); _txOff = 0; }
                }
            }
            catch { Close(); }
        }

        /// <summary>Раз в кадр: дослать, принять, разрезать на пакеты.</summary>
        public void Poll(List<byte[]> into)
        {
            if (Closed) return;
            Flush();
            try
            {
                while (true)
                {
                    int n = _s.Receive(_buf, 0, _buf.Length, SocketFlags.None, out var err);
                    if (err == SocketError.WouldBlock) break;
                    if (err != SocketError.Success || n == 0) { Close(); break; }
                    if (_rxLen + n > _rx.Length) System.Array.Resize(ref _rx, System.Math.Max(_rx.Length * 2, _rxLen + n));
                    System.Buffer.BlockCopy(_buf, 0, _rx, _rxLen, n);
                    _rxLen += n;
                }
            }
            catch { Close(); }
            int off = 0;
            while (_rxLen - off >= 4)
            {
                int len = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(new System.ReadOnlySpan<byte>(_rx, off, 4));
                if (len < 0 || len > 64 * 1024 * 1024) { Close(); return; }
                if (_rxLen - off - 4 < len) break;
                var p = new byte[len];
                System.Buffer.BlockCopy(_rx, off + 4, p, 0, len);
                into.Add(p);
                off += 4 + len;
            }
            if (off > 0) { System.Buffer.BlockCopy(_rx, off, _rx, 0, _rxLen - off); _rxLen -= off; }
        }

        public void Close()
        {
            if (Closed) return;
            Closed = true;
            try { _s.Shutdown(SocketShutdown.Both); } catch { }
            try { _s.Close(); } catch { }
        }
    }

    /// <summary>
    /// Хост (D73): игра этого ПК — сервер. Мир с ботами считает только он; друзья шлют ввод и получают снимки.
    /// Живёт поверх LocalServer: PreTick — ввод друзей в очередь мира, PostTick — каждому его снимок.
    /// </summary>
    public sealed class NetHost
    {
        sealed class Remote
        {
            public NetLink Link;
            public int ActorId;
            public string Name;
            public readonly List<InputCmd> Queue = new();
            public InputCmd Last = InputCmd.Empty;
            public NetProto.SnapEncoder Enc = new();
            public bool Ready;   // клиент построил карту — шлём снимки
        }

        const long MaxBacklog = 16 * 1024 * 1024;   // друг не принимает снимки (завис) — отключаем

        readonly LocalServer _server;
        TcpListener _listen;
        readonly List<Remote> _remotes = new();
        readonly List<byte[]> _in = new();
        public int Port { get; private set; }
        public int Players { get { int n = 0; foreach (var r in _remotes) if (r.ActorId != 0) n++; return n; } }
        public event System.Action<string> Notice;   // «Вася зашёл» — тост хосту
        public long BytesOut;
        public int HostActorId;   // персонаж самого хоста (для отладки --netnear)

        public NetHost(LocalServer server) => _server = server;

        public string Start(int port)
        {
            Port = port;
            try
            {
                _listen = new TcpListener(IPAddress.Any, port);
                _listen.Start();
            }
            catch (System.Exception ex) { _listen = null; return ex.Message; }
            GD.Print($"сеть: сервер слушает TCP {port}");
            return null;
        }

        public void Stop()
        {
            foreach (var r in _remotes) r.Link.Close();
            _remotes.Clear();
            try { _listen?.Stop(); } catch { }
            _listen = null;
        }

        public IEnumerable<string> Names() { foreach (var r in _remotes) if (r.ActorId != 0) yield return r.Name; }

        /// <summary>Раз в кадр: новые подключения, приём пакетов, отвалившиеся.</summary>
        public void Poll()
        {
            if (_listen == null) return;
            while (_listen.Pending())
            {
                var s = _listen.AcceptSocket();
                if (_remotes.Count >= NetProto.MaxPlayers) { s.Close(); continue; }
                _remotes.Add(new Remote { Link = new NetLink(s) });
            }
            for (int i = _remotes.Count - 1; i >= 0; i--)
            {
                var r = _remotes[i];
                _in.Clear();
                r.Link.Poll(_in);
                foreach (var pkt in _in)
                {
                    if (pkt.Length < 1) continue;
                    try
                    {
                        if (pkt[0] == NetProto.Hello && r.ActorId == 0) OnHello(r, NetProto.Decode<NetProto.HelloMsg>(pkt));
                        else if (pkt[0] == NetProto.Ready && r.ActorId != 0 && !r.Ready) { r.Ready = true; r.Enc = new NetProto.SnapEncoder(); }   // заново с полного снимка
                        else if (pkt[0] == NetProto.Input && r.ActorId != 0)
                        {
                            var c = NetProto.DecodeInput(pkt);
                            c.DebugKit = c.ToggleTime = c.DebugFly = false; c.Follow = 0;   // читы — только у хоста
                            r.Queue.Add(c);
                        }
                    }
                    catch (System.Exception ex) { GD.PrintErr($"сеть: плохой пакет от {r.Link.Remote}: {ex.Message}"); }
                }
                if (r.Link.Pending > MaxBacklog) r.Link.Close();
                if (r.Link.Closed)
                {
                    _remotes.RemoveAt(i);
                    if (r.ActorId != 0) { Notice?.Invoke($"{r.Name} вышел"); GD.Print($"сеть: {r.Name} отключился"); }
                }
            }
        }

        void OnHello(Remote r, NetProto.HelloMsg h)
        {
            string name = (h.Name ?? "").Trim();
            string why = h.Version != NetProto.Version ? "другая версия игры — обновитесь"
                : name.Length < 2 || name.Length > 20 ? "имя — от 2 до 20 букв"
                : name == "Игрок" || name.EndsWith("(бот)") ? "это имя занято"
                : System.Linq.Enumerable.Contains(Names(), name) ? "игрок с таким именем уже в игре" : null;
            if (why != null) { r.Link.Send(NetProto.Pack(NetProto.Reject, why)); return; }
            r.Name = name;
            r.ActorId = _server.Connect(name);
            // отладка: --netnear — друг появляется в 3 м от хоста (проверка «видят друг друга»)
            if (HostActorId != 0 && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--netnear") >= 0 && _server.World.Find(HostActorId) is Actor ha && _server.World.Find(r.ActorId) is Actor f)
                f.Pos = ha.Pos + new System.Numerics.Vector2(3, 0);
            r.Link.Send(NetProto.EncodeWelcome(new NetProto.WelcomeMsg { Version = NetProto.Version, Id = r.ActorId, Seed = _server.World.Seed, NoScatter = World.NoScatter, MapHash = NetProto.MapHash(_server.World) }));
            r.Link.Send(r.Enc.Encode(_server.BuildSnapshot(r.ActorId)));   // сразу — даже если мир хоста стоит (экран загрузки)
            Notice?.Invoke($"{name} зашёл");
            GD.Print($"сеть: {name} ({r.Link.Remote}) → персонаж {r.ActorId}");
        }

        /// <summary>Перед тиком мира: по команде от каждого друга (очередь сглаживает рывки сети).</summary>
        public void PreTick()
        {
            foreach (var r in _remotes)
            {
                if (r.ActorId == 0) continue;
                // отстали — догоняем: лишние команды сливаем, разовые нажатия не теряем
                while (r.Queue.Count > 4)
                {
                    var a = r.Queue[0]; r.Queue.RemoveAt(0);
                    r.Queue[0] = Merge(a, r.Queue[0]);
                }
                InputCmd c;
                if (r.Queue.Count > 0) { c = r.Queue[0]; r.Queue.RemoveAt(0); r.Last = c; }
                else
                {
                    // команда не пришла вовремя — держим прежние клавиши, без разовых действий
                    var l = r.Last;
                    c = InputCmd.Empty;
                    c.Move = l.Move; c.Aim = l.Aim; c.Fire = l.Fire; c.Sprint = l.Sprint; c.Crouch = l.Crouch; c.Peek = l.Peek;
                    c.HammerFocus = l.HammerFocus; c.HammerMode = l.HammerMode; c.HasAimZ = l.HasAimZ; c.AimZ = l.AimZ;
                }
                _server.Submit(r.ActorId, c);
            }
        }

        static InputCmd Merge(InputCmd a, InputCmd b)
        {
            b.Reload |= a.Reload; b.Interact |= a.Interact; b.Heal |= a.Heal; b.Eat |= a.Eat; b.Respawn |= a.Respawn; b.RespawnBag |= a.RespawnBag;
            if (b.SelectSlot < 0) b.SelectSlot = a.SelectSlot;
            if (b.Craft < 0) b.Craft = a.Craft;
            if (b.Code < 0) { b.Code = a.Code; b.CodeDoor = a.CodeDoor; }
            if (b.UiOp == 0) { b.UiOp = a.UiOp; b.UiItem = a.UiItem; b.UiIndex = a.UiIndex; }
            if (b.SetPiece < 0) b.SetPiece = a.SetPiece;
            b.Research ??= a.Research;
            if (b.HammerOp == 0) { b.HammerOp = a.HammerOp; b.HammerTarget = a.HammerTarget; }
            return b;
        }

        /// <summary>После тика мира: каждому другу — его снимок.</summary>
        public void PostTick()
        {
            foreach (var r in _remotes)
            {
                if (r.ActorId == 0 || !r.Ready || r.Link.Closed) continue;
                var bytes = r.Enc.Encode(_server.BuildSnapshot(r.ActorId));
                BytesOut += bytes.Length;
                r.Link.Send(bytes);
            }
        }
    }

    /// <summary>
    /// Клиент (D73): мир не считает — шлёт ввод, проигрывает снимки сервера с небольшим запасом (буфер ~2 тика).
    /// </summary>
    public sealed class NetClient
    {
        NetLink _link;
        readonly NetProto.SnapDecoder _dec = new();
        readonly Queue<byte[]> _snaps = new();
        readonly List<byte[]> _in = new();
        public NetProto.WelcomeMsg Info;
        public string Error;
        public bool Lost;
        public long BytesIn;
        bool _waitFill = true;

        /// <summary>Подключиться и дождаться приветствия и первого снимка (блокирует до timeoutMs).</summary>
        public Snapshot ConnectBlocking(string host, int port, string name, int timeoutMs = 30000)
        {
            ulong t0 = Time.GetTicksMsec();
            Socket s;
            try
            {
                s = new Socket(SocketType.Stream, ProtocolType.Tcp);
                var task = s.ConnectAsync(host, port);
                if (!task.Wait(8000) || !s.Connected) { s.Close(); Error = "нет ответа от сервера (адрес, порт, проброс?)"; return null; }
            }
            catch (System.Exception ex) { Error = "нет связи: " + (ex.InnerException ?? ex).Message; return null; }
            _link = new NetLink(s);
            _link.Send(NetProto.EncodeHello(name));
            while (Time.GetTicksMsec() - t0 < (ulong)timeoutMs)
            {
                _in.Clear();
                _link.Poll(_in);
                foreach (var pkt in _in)
                {
                    if (pkt.Length < 1) continue;
                    if (pkt[0] == NetProto.Reject) { Error = NetProto.Text(pkt); _link.Close(); return null; }
                    if (pkt[0] == NetProto.Welcome) Info = NetProto.Decode<NetProto.WelcomeMsg>(pkt);
                    else if (pkt[0] == NetProto.Snap && Info != null)
                    {
                        BytesIn += pkt.Length;
                        var first = _dec.Decode(pkt);
                        GD.Print($"сеть: мир получен за {Time.GetTicksMsec() - t0} мс ({pkt.Length / 1024} КБ)");
                        return first;
                    }
                }
                if (_link.Closed) { Error ??= "сервер закрыл соединение"; return null; }
                OS.DelayMsec(5);
            }
            Error = "сервер не прислал мир";
            _link.Close();
            return null;
        }

        public void Close() => _link?.Close();

        /// <summary>Раз в кадр: приём снимков.</summary>
        public void Poll()
        {
            if (_link == null) return;
            _in.Clear();
            _link.Poll(_in);
            foreach (var pkt in _in)
            {
                if (pkt.Length < 1) continue;
                if (pkt[0] == NetProto.Snap) { _snaps.Enqueue(pkt); BytesIn += pkt.Length; }
                else if (pkt[0] == NetProto.Reject) { Error = NetProto.Text(pkt); Lost = true; }
            }
            if (_link.Closed) Lost = true;
        }

        public void SendInput(InputCmd c) => _link?.Send(NetProto.EncodeInput(c));

        /// <summary>Карта построена: сервер начинает слать снимки (первый — полный).</summary>
        public void SendReady() { _link?.Send(new[] { NetProto.Ready }); }

        /// <summary>Тик клиента: следующий снимок (null — нового нет, держим прежний). Отстали — проматываем, события копим.</summary>
        public Snapshot Take()
        {
            if (_snaps.Count == 0) { _waitFill = true; return null; }
            if (_waitFill && _snaps.Count < 2) return null;   // опустел буфер — ждём запас в 2 снимка, иначе дёргает
            _waitFill = false;
            List<SimEvent> skipped = null;
            while (_snaps.Count > 3)
            {
                var old = _dec.Decode(_snaps.Dequeue());
                (skipped ??= new()).AddRange(old.Events);
                if (skipped.Count > 300) skipped.Clear();   // долго стояли (клиент строил мир) — старые выстрелы и шаги не проигрываем
            }
            var s = _dec.Decode(_snaps.Dequeue());
            if (skipped != null) { skipped.AddRange(s.Events); s.Events = skipped; }
            return s;
        }
    }
}
