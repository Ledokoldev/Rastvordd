using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Сеть (D73): хост — этот ПК считает мир с ботами и раздаёт снимки друзьям; клиент — играет в чужом мире.
    /// Состояние переживает перезапуск сцены (static): «Подключиться» перезапускает сцену клиентом, «Отключиться» — своей игрой.
    /// </summary>
    public partial class Main
    {
        /// <summary>Куда подключиться при старте сцены (адрес:порт), null — своя игра.</summary>
        static string NetConnectTo;
        static string NetName;
        /// <summary>Хост был открыт — после перезапуска сцены (вайп) открыть снова.</summary>
        static int NetHostPort;
        static NetHost _liveHost;
        static NetClient _liveClient;

        NetClient _net;
        NetHost _host;
        string _netError;
        float _netStatusT;
        double _lostT = -1;

        static (string host, int port) SplitAddr(string a)
        {
            a = a.Trim();
            int i = a.LastIndexOf(':');
            if (i > 0 && int.TryParse(a[(i + 1)..], out int p)) return (a[..i], p);
            return (a, NetProto.DefaultPort);
        }

        void StartHost(int port)
        {
            _liveHost?.Stop();
            _host = new NetHost(_server) { HostActorId = _myId };
            var err = _host.Start(port);
            if (err != null) { _hud.Toast($"Сервер не открылся на порту {port}: {err}"); _host = null; NetHostPort = 0; return; }
            _liveHost = _host;
            NetHostPort = port;
            _host.Notice += s => _hud.Toast(s);
            _hud.Toast($"Сервер открыт: TCP {port}. Друзья — Esc → «Сеть» → адрес → «Подключиться»");
            NetStatus();
        }

        void StopHost()
        {
            _host?.Stop();
            _host = null; _liveHost = null; NetHostPort = 0;
            _hud.Toast("Сервер закрыт");
            NetStatus();
        }

        void NetMenuSetup()
        {
            if (_net != null) { _liveClient?.Close(); _liveClient = _net; _net.SendReady(); }
            _settings.HostToggled += () => { if (_host != null) StopHost(); else StartHost(NetProto.DefaultPort); };
            _settings.JoinRequested += (addr, name) =>
            {
                if (addr == "") { _hud.Toast("Впиши адрес сервера"); return; }
                if (name.Length < 2) { _hud.Toast("Впиши имя (от 2 букв)"); return; }
                SaveWorld();   // свой мир — на диск, пока гуляем по чужому
                _liveHost?.Stop(); _liveHost = null; NetHostPort = 0;
                NetConnectTo = addr; NetName = name;
                Input.MouseMode = Input.MouseModeEnum.Hidden;
                GetTree().ReloadCurrentScene();
            };
            _settings.LeaveRequested += LeaveNet;
            NetStatus();
        }

        void LeaveNet()
        {
            _net?.Close();
            _liveClient = null;
            NetConnectTo = null;
            Input.MouseMode = Input.MouseModeEnum.Hidden;
            GetTree().ReloadCurrentScene();   // обратно в свой мир (сохранение читается заново)
        }

        void NetStatus()
        {
            string s = _net != null ? $"В игре у {NetConnectTo} · принято {_net.BytesIn / 1024} КБ"
                : _host != null ? $"Сервер открыт на TCP {_host.Port} · друзей: {_host.Players}" + (_host.Players > 0 ? " — " + string.Join(", ", _host.Names()) : "") + $" · отдано {_host.BytesOut / 1024} КБ"
                : "Своя игра. Хочешь играть с друзьями — открой сервер (порт 7777) или подключись к другу.";
            _settings?.SetNet(_host != null, _net != null, s);
        }

        /// <summary>Раз в кадр: приём пакетов, обрыв связи.</summary>
        void NetPoll()
        {
            _host?.Poll();
            if (_net != null)
            {
                _net.Poll();
                if (_net.Lost && _lostT < 0) { _lostT = 0; _hud.Toast("Связь с сервером потеряна" + (_net.Error != null ? ": " + _net.Error : "") + " — возвращаемся в свой мир"); }
                if (_lostT >= 0 && (_lostT += GetProcessDeltaTime()) > 3) LeaveNet();
            }
            if ((_netStatusT += (float)GetProcessDeltaTime()) > 1)
            {
                _netStatusT = 0; NetStatus();
                // --netlog: для проверки без картинки — где я, кого вижу, сколько трафика
                if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--netlog") >= 0 && _cur?.Actors != null)
                {
                    var me = _cur.Actors.Find(a => a.Id == _myId);
                    int people = _cur.Actors.FindAll(a => a.Id != _myId && !a.IsNpc).Count, npc = _cur.Actors.FindAll(a => a.IsNpc).Count;
                    var names = string.Join(", ", _cur.Actors.FindAll(a => a.Id != _myId && a.Label != null).ConvertAll(a => a.Label.Split(':')[0]));
                    GD.Print($"сеть-лог: {(_net != null ? "клиент" : "хост")} тик {_cur.Tick} · я {me.Pos.X:0.0};{me.Pos.Y:0.0} hp {_cur.Self?.Hp:0} · людей видно {people} [{names}] · охраны {npc} · событий {_cur.Events.Count}"
                        + (_net != null ? $" · принято {_net.BytesIn / 1024} КБ" : _host != null ? $" · друзей {_host.Players} · отдано {_host.BytesOut / 1024} КБ" : ""));
                }
            }
        }
    }
}
