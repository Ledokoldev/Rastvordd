using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Главное меню (D74): своя игра, сервер для друзей (+ проброс frpc, если лежит рядом), подключиться к другу.
    /// Любой запуск с аргументами (отладка, cmd-файлы) идёт мимо меню — сразу в игру.
    /// Адрес по умолчанию — из server.txt рядом с exe (или в корне проекта): репозиторий публичный, адрес в git не кладём.
    /// </summary>
    public partial class MainMenu : Control
    {
        public const string GameScene = "res://Main.tscn";
        public const string MenuScene = "res://MainMenu.tscn";
        /// <summary>Что показать в меню после возврата (ошибка подключения, «связь потеряна»).</summary>
        public static string Message;
        /// <summary>Процесс frpc (проброс через VPS), запущенный меню — гасим при выходе из игры.</summary>
        public static int FrpcPid;

        LineEdit _addr, _name;
        string _shot; int _frames;

        public override void _Process(double delta)
        {
            if (_shot == null || ++_frames != 40) return;
            GetViewport().GetTexture().GetImage().SavePng(_shot);
            GetTree().Quit();
        }
        Label _msg;

        public override void _Ready()
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length > 0 && System.Array.IndexOf(args, "--menu") < 0) { CallDeferred(MethodName.Go); return; }   // отладка и cmd — как раньше (--menu — показать меню, --shot=путь — снимок)
            _shot = System.Array.Find(args, a => a.StartsWith("--shot="))?.Substring(7);
            DisplayServer.WindowSetTitle("Раствордд");
            GameSettings.Load();
            GameSettings.ApplyVideoAudio();
            Theme = UiTheme.Theme;
            SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(new ColorRect { Color = new Color(0.05f, 0.06f, 0.07f), AnchorRight = 1, AnchorBottom = 1 });
            var bg = ResourceLoader.Exists("res://art/ui/menu_bg.png") ? GD.Load<Texture2D>("res://art/ui/menu_bg.png") : null;
            if (bg != null) AddChild(new TextureRect { Texture = bg, AnchorRight = 1, AnchorBottom = 1, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, Modulate = new Color(1, 1, 1, 0.5f) });

            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", UiTheme.Glass(28, 0.9f, 22));
            panel.SetAnchorsPreset(LayoutPreset.Center);
            panel.GrowHorizontal = GrowDirection.Both; panel.GrowVertical = GrowDirection.Both;
            panel.CustomMinimumSize = new Vector2(520, 0);
            AddChild(panel);
            var box = UiTheme.VBox(12);
            panel.AddChild(box);
            box.AddChild(UiTheme.Strong("РАСТВОРДД", 40));
            box.AddChild(UiTheme.Small("остров, боты, вайп"));
            box.AddChild(UiTheme.Sep());

            var solo = UiTheme.Btn("Своя игра", true, 0, 46);
            solo.Pressed += () => Start(host: false);
            box.AddChild(solo);
            var host = UiTheme.Btn("Сервер для друзей", false, 0, 42);
            host.Pressed += () => Start(host: true);
            box.AddChild(host);
            box.AddChild(UiTheme.Small("Твой мир с ботами открыт друзьям (порт 7777). Проброс через VPS — если рядом лежит frp."));

            box.AddChild(UiTheme.Sep());
            box.AddChild(UiTheme.Caption("Подключиться к другу"));
            var cfg = new ConfigFile();
            cfg.Load("user://net.cfg");
            string addr = (string)cfg.GetValue("net", "addr", "");
            if (addr == "") addr = DefaultServer() ?? "";
            var row = UiTheme.HBox(10);
            _addr = new LineEdit { PlaceholderText = "адрес:порт", Text = addr, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _name = new LineEdit { PlaceholderText = "твоё имя", Text = (string)cfg.GetValue("net", "name", ""), CustomMinimumSize = new Vector2(160, 0) };
            row.AddChild(_addr); row.AddChild(_name);
            box.AddChild(row);
            var join = UiTheme.Btn("Подключиться", false, 0, 42);
            join.Pressed += Join;
            box.AddChild(join);
            _name.TextSubmitted += _ => Join();

            _msg = UiTheme.Small(Message ?? "", UiTheme.Bad);
            _msg.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _msg.Visible = Message != null;
            box.AddChild(_msg);
            Message = null;

            box.AddChild(UiTheme.Sep());
            var quit = UiTheme.Btn("Выход", false, 0, 38);
            quit.Pressed += () => GetTree().Quit();
            box.AddChild(quit);
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }

        /// <summary>Адрес сервера по умолчанию: первая строка server.txt рядом с exe или в корне проекта.</summary>
        static string DefaultServer()
        {
            foreach (var dir in new[] { OS.GetExecutablePath().GetBaseDir(), ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\').GetBaseDir(), ProjectSettings.GlobalizePath("res://") })
            {
                var p = System.IO.Path.Combine(dir, "server.txt");
                if (System.IO.File.Exists(p)) { var s = System.IO.File.ReadAllText(p).Trim(); if (s.Length > 0) return s.Split('\n')[0].Trim(); }
            }
            return null;
        }

        void Show(string s) { _msg.Text = s; _msg.Visible = true; }

        void Join()
        {
            string a = _addr.Text.Trim(), n = _name.Text.Trim();
            if (a == "") { Show("Впиши адрес сервера"); return; }
            if (n.Length < 2) { Show("Впиши имя (от 2 букв)"); return; }
            var cfg = new ConfigFile();
            cfg.Load("user://net.cfg");
            cfg.SetValue("net", "addr", a); cfg.SetValue("net", "name", n);
            cfg.Save("user://net.cfg");
            Main.NetConnectTo = a; Main.NetName = n; Main.NetHostPort = 0;
            Go();
        }

        void Start(bool host)
        {
            Main.NetConnectTo = null;
            Main.MenuBots = 20;
            Main.NetHostPort = host ? NetProto.DefaultPort : 0;
            if (host) StartFrpc();
            Go();
        }

        /// <summary>Проброс через VPS: frp\frpc.exe + frpc.toml рядом с exe (сборка) или tools\frp (репозиторий).</summary>
        static void StartFrpc()
        {
            if (FrpcPid > 0) return;
            foreach (var dir in new[] { System.IO.Path.Combine(OS.GetExecutablePath().GetBaseDir(), "frp"), System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\').GetBaseDir(), "tools", "frp") })
            {
                string exe = System.IO.Path.Combine(dir, "frpc.exe"), toml = System.IO.Path.Combine(dir, "frpc.toml");
                if (!System.IO.File.Exists(exe) || !System.IO.File.Exists(toml)) continue;
                FrpcPid = OS.CreateProcess(exe, new[] { "-c", toml });
                GD.Print($"сеть: проброс frpc запущен (pid {FrpcPid})");
                return;
            }
        }

        public static void StopFrpc()
        {
            if (FrpcPid > 0) { OS.Kill(FrpcPid); FrpcPid = 0; }
        }

        void Go()
        {
            Input.MouseMode = Input.MouseModeEnum.Hidden;
            GetTree().ChangeSceneToFile(GameScene);
        }
    }
}
