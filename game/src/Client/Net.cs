using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Хост (D73): игра этого ПК — сервер. Мир с ботами считает только он; друзья шлют ввод и получают снимки.
    /// Живёт поверх LocalServer: PreTick — ввод друзей в очередь мира, PostTick — каждому его снимок.
    /// </summary>
    public sealed class NetHost
    {
        sealed class Remote
        {
            public int Peer, ActorId;
            public string Name;
            public readonly List<InputCmd> Queue = new();
            public InputCmd Last = InputCmd.Empty;
            public readonly NetProto.SnapEncoder Enc = new();
        }

        readonly LocalServer _server;
        readonly ENetMultiplayerPeer _peer = new();
        readonly Dictionary<int, Remote> _remotes = new();
        public int Port { get; private set; }
        public int Players { get { int n = 0; foreach (var r in _remotes.Values) if (r.ActorId != 0) n++; return n; } }
        public event System.Action<string> Notice;   // «Вася зашёл» — тост хосту
        public long BytesOut;
        public int HostActorId;   // персонаж самого хоста (для отладки --netnear)
        public const int LongTimeoutMs = 60000;   // ENet: сколько молчания терпеть (клиент строит мир ~десятки секунд)

        public NetHost(LocalServer server) => _server = server;

        public Error Start(int port)
        {
            Port = port;
            var err = _peer.CreateServer(port, NetProto.MaxPlayers);
            if (err != Error.Ok) return err;
            _peer.PeerConnected += id =>
            {
                _remotes[(int)id] = new Remote { Peer = (int)id };
                _peer.GetPeer((int)id)?.SetTimeout(32, LongTimeoutMs, LongTimeoutMs * 2);   // клиент долго строит карту и не отвечает — не рвём
            };
            _peer.PeerDisconnected += id =>
            {
                if (_remotes.Remove((int)id, out var r) && r.ActorId != 0) { Notice?.Invoke($"{r.Name} вышел"); GD.Print($"сеть: {r.Name} отключился"); }
            };
            GD.Print($"сеть: сервер слушает UDP {port}");
            return Error.Ok;
        }

        public void Stop() { _peer.Close(); _remotes.Clear(); }

        public IEnumerable<string> Names() { foreach (var r in _remotes.Values) if (r.ActorId != 0) yield return r.Name; }

        /// <summary>Раз в кадр: приём пакетов.</summary>
        public void Poll()
        {
            _peer.Poll();
            while (_peer.GetAvailablePacketCount() > 0)
            {
                int from = _peer.GetPacketPeer();
                var pkt = _peer.GetPacket();
                if (pkt == null || pkt.Length < 1 || !_remotes.TryGetValue(from, out var r)) continue;
                try
                {
                    if (pkt[0] == NetProto.Hello && r.ActorId == 0) OnHello(r, NetProto.Decode<NetProto.HelloMsg>(pkt));
                    else if (pkt[0] == NetProto.Input && r.ActorId != 0)
                    {
                        var c = NetProto.DecodeInput(pkt);
                        c.DebugKit = c.ToggleTime = c.DebugFly = false; c.Follow = 0;   // читы — только у хоста
                        r.Queue.Add(c);
                    }
                }
                catch (System.Exception ex) { GD.PrintErr($"сеть: плохой пакет от {from}: {ex.Message}"); }
            }
        }

        void OnHello(Remote r, NetProto.HelloMsg h)
        {
            string name = (h.Name ?? "").Trim();
            string why = h.Version != NetProto.Version ? "другая версия игры — обновитесь"
                : name.Length < 2 || name.Length > 20 ? "имя — от 2 до 20 букв"
                : name == "Игрок" || name.EndsWith("(бот)") ? "это имя занято"
                : System.Linq.Enumerable.Contains(Names(), name) ? "игрок с таким именем уже в игре" : null;
            if (why != null) { Send(r.Peer, NetProto.Pack(NetProto.Reject, why)); _peer.DisconnectPeer(r.Peer); return; }
            r.Name = name;
            r.ActorId = _server.Connect(name);
            // отладка: --netnear — друг появляется в 3 м от хоста (проверка «видят друг друга»)
            if (HostActorId != 0 && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--netnear") >= 0 && _server.World.Find(HostActorId) is Actor ha && _server.World.Find(r.ActorId) is Actor f)
                f.Pos = ha.Pos + new System.Numerics.Vector2(3, 0);
            Send(r.Peer, NetProto.EncodeWelcome(new NetProto.WelcomeMsg { Version = NetProto.Version, Id = r.ActorId, Seed = _server.World.Seed, NoScatter = World.NoScatter, MapHash = NetProto.MapHash(_server.World) }));
            Send(r.Peer, r.Enc.Encode(_server.BuildSnapshot(r.ActorId)));   // сразу — даже если мир хоста стоит (экран загрузки)
            Notice?.Invoke($"{name} зашёл");
            GD.Print($"сеть: {name} (peer {r.Peer}) → персонаж {r.ActorId}");
        }

        /// <summary>Перед тиком мира: по команде от каждого друга (очередь сглаживает рывки сети).</summary>
        public void PreTick()
        {
            foreach (var r in _remotes.Values)
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
            foreach (var r in _remotes.Values)
            {
                if (r.ActorId == 0) continue;
                var bytes = r.Enc.Encode(_server.BuildSnapshot(r.ActorId));
                BytesOut += bytes.Length;
                Send(r.Peer, bytes);
            }
        }

        void Send(int peer, byte[] data)
        {
            _peer.TransferMode = MultiplayerPeer.TransferModeEnum.Reliable;
            _peer.TransferChannel = 0;
            _peer.SetTargetPeer(peer);
            _peer.PutPacket(data);
        }
    }

    /// <summary>
    /// Клиент (D73): мир не считает — шлёт ввод, проигрывает снимки сервера с небольшим запасом (буфер ~2 тика).
    /// </summary>
    public sealed class NetClient
    {
        readonly ENetMultiplayerPeer _peer = new();
        readonly NetProto.SnapDecoder _dec = new();
        readonly Queue<byte[]> _snaps = new();
        public NetProto.WelcomeMsg Info;
        public string Error;
        public bool Lost;
        public long BytesIn;
        bool _waitFill = true;

        /// <summary>Подключиться и дождаться приветствия и первого снимка (блокирует до timeoutMs).</summary>
        public Snapshot ConnectBlocking(string host, int port, string name, int timeoutMs = 30000)
        {
            var err = _peer.CreateClient(host, port);
            if (err != Godot.Error.Ok) { Error = $"не удалось открыть соединение ({err})"; return null; }
            ulong t0 = Time.GetTicksMsec();
            bool hello = false;
            while (Time.GetTicksMsec() - t0 < (ulong)timeoutMs)
            {
                _peer.Poll();
                var st = _peer.GetConnectionStatus();
                if (st == MultiplayerPeer.ConnectionStatus.Connected && !hello)
                {
                    _peer.GetPeer(1)?.SetTimeout(32, NetHost.LongTimeoutMs, NetHost.LongTimeoutMs * 2);
                    Send(NetProto.EncodeHello(name)); hello = true;
                }
                if (st == MultiplayerPeer.ConnectionStatus.Disconnected) { Error ??= "сервер не отвечает"; return null; }
                while (_peer.GetAvailablePacketCount() > 0)
                {
                    var pkt = _peer.GetPacket();
                    if (pkt.Length < 1) continue;
                    if (pkt[0] == NetProto.Reject) { Error = NetProto.Text(pkt); _peer.Close(); return null; }
                    if (pkt[0] == NetProto.Welcome) Info = NetProto.Decode<NetProto.WelcomeMsg>(pkt);
                    else if (pkt[0] == NetProto.Snap && Info != null) { BytesIn += pkt.Length; return _dec.Decode(pkt); }
                }
                OS.DelayMsec(5);
            }
            Error = hello ? "сервер не прислал мир" : "нет ответа от сервера (адрес, порт, проброс?)";
            _peer.Close();
            return null;
        }

        public void Close() => _peer.Close();

        /// <summary>Раз в кадр: приём снимков.</summary>
        public void Poll()
        {
            _peer.Poll();
            if (_peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected) Lost = true;
            while (_peer.GetAvailablePacketCount() > 0)
            {
                var pkt = _peer.GetPacket();
                if (pkt.Length < 1) continue;
                if (pkt[0] == NetProto.Snap) { _snaps.Enqueue(pkt); BytesIn += pkt.Length; }
                else if (pkt[0] == NetProto.Reject) { Error = NetProto.Text(pkt); Lost = true; }
            }
        }

        public void SendInput(InputCmd c) => Send(NetProto.EncodeInput(c));

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
            }
            var s = _dec.Decode(_snaps.Dequeue());
            if (skipped != null) { skipped.AddRange(s.Events); s.Events = skipped; }
            return s;
        }

        void Send(byte[] data)
        {
            _peer.TransferMode = MultiplayerPeer.TransferModeEnum.Reliable;
            _peer.TransferChannel = 0;
            _peer.SetTargetPeer(1);
            _peer.PutPacket(data);
        }
    }
}
