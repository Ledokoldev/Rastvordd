using System.Collections.Generic;
using System.Linq;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>
    /// Клиент прототипа (2D, вид 3/4): собирает ввод, отправляет его локальному серверу и рисует снапшоты.
    /// В мир напрямую не лезет — только через LocalServer (как будет по сети).
    /// </summary>
    public partial class Main : Node2D
    {
        LocalServer _server;
        MapInfo _map;
        DecalLayer _decals;
        float _shake;   // тряска камеры от своих выстрелов
        Weather _weather;
        VisionRays _rays;
        BuildLayer _build;
        BuildGhost2D _bghost;
        MapView _mapView;
        MiniMap _miniMap;
        InventoryWindow _inv;
        bool _invOpen;
        BuildMenu _buildMenu;
        BuildBar _buildBar;
        StructTip _structTip;
        PickupFeed _feed;
        Label _guide;
        int _setPiece = -1;
        // наблюдатель (песочница ботов)
        bool _spectate;
        int _followId, _timeScale = 1;
        float _effSpeed = 1;   // сколько секунд мира проходит за секунду на деле (песочница может не успевать)
        float _specRefresh;
        Label _specPanel;
        string _researchReq;      // изучить узел дерева (id)
        (UiOp op, string item, int index)? _uiReq;
        bool UiShown => _inv.Visible;
        bool _uiWasOpen;
        string _lastPlace;
        readonly Dictionary<int, float> _stepAcc = new();
        CanvasModulate _daylight;
        Ambient _ambient;
        bool _overview, _perf, _timeEdge, _forceCrouch, _forceSprint;
        SVec _forceWalk;
        int _pressNAt = -1;
        double _perfTime, _simTime;
        int _perfFrames;
        float? _forcedDay;
        int _myId;
        Snapshot _cur;
        readonly Dictionary<int, ActorSnap> _prevActors = new();
        readonly Dictionary<int, ProjSnap> _prevProjs = new();
        double _acc;

        Node2D _world;
        Camera2D _cam;
        /// <summary>Стены и пропсы карты (РТ) — для прозрачности заслонов.</summary>
        readonly List<WallView2D> _mapWalls = new();
        PrefabLayer _prefab;
        LightingLayer _lighting;
        /// <summary>Отладка (--occl-off): прозрачность только над своим персонажем — как было до D39, для сравнения.</summary>
        bool _occlSelfOnly;
        float _zoom = 2.0f;
        Hud _hud;

        readonly Dictionary<int, ActorView2D> _actorViews = new();
        readonly Dictionary<int, TracerView2D> _projViews = new();
        readonly Dictionary<int, BagView2D> _bagViews = new();
        readonly Dictionary<int, NodeView2D> _nodeViews = new();
        readonly Dictionary<int, PickupView2D> _pickupViews = new();
        readonly Dictionary<int, DeployView2D> _deployViews = new();
        readonly Dictionary<int, ThrownView2D> _thrownViews = new();
        readonly Dictionary<int, FireView2D> _fireViews = new();
        readonly List<FlashView2D> _fx = new();
        readonly List<FloatText2D> _texts = new();
        GhostView2D _ghost;

        bool _reloadEdge, _interactEdge, _healEdge, _respawnEdge, _eatEdge, _debugEdge, _bagEdge;
        int _slot = -1, _craftReq = -1;
        int _craftPendIdx = -1, _craftPendLeft;   // «скрафтить ×N»: по одному заказу за тик
        (int door, int code)? _codeReq;
        SVec _aim;
        Vector2 _screenDir;
        float _peek;
        float? _forcedPeek;
        Vector2 _autoScreen = new Vector2(800, 450);
        Vector2 _selfPx;

        string _shotPath;
        int _shotFrame = -1, _frame;
        (int start, int count, int step) _burst;
        SVec? _autoAim;
        readonly Dictionary<int, List<string>> _script = new();
        int _autoFireFrames;
        int _scriptCode = -1;   // отладка: код, который сценарий «вводит» у запертой двери

        void RunScript()
        {
            if (!_script.TryGetValue(_frame, out var actions)) return;
            foreach (var a in actions)
            {
                if (a == "kit") _debugEdge = true;
                else if (a == "interact") _interactEdge = true;
                else if (a == "eat") _eatEdge = true;
                else if (a == "reload") _reloadEdge = true;
                else if (a == "fire") _autoFireFrames = 4;
                else if (a == "hold") _autoFireFrames = 100000;
                else if (a == "release") _autoFireFrames = 0;
                else if (a == "craftmenu") _hud.ToggleCraft();
                else if (a == "tech") _hud.OpenTech();
                else if (a == "pathtest")
                {
                    // отладка: маршрут от игрока до ближайшего чужого шкафа, двери проходимы
                    var w = _server.World; var me = w.Find(_myId);
                    Deployable claim = null; float bd = 1e9f;
                    foreach (var d in w.Deployables) if (d.Kind == DeployKind.Claim && SVec.Distance(d.Pos, me.Pos) < bd) { bd = SVec.Distance(d.Pos, me.Pos); claim = d; }
                    if (claim != null)
                    {
                        System.Func<MapWall, bool> doors = mw => mw.Owner is Structure st && st.Kind == StructKind.Door;
                        var path = Pathfinder.Find(w, me.Pos, claim.Pos, me.Radius + 0.05f, 1.0f, doors);
                        GD.Print($"pathtest: от {me.Pos} к шкафу {claim.Pos}: {(path == null ? "НЕТ ПУТИ" : string.Join(" ", path.ConvertAll(q => $"({q.X:0.0};{q.Y:0.0})")))}");
                        foreach (var st in w.Structures) if (st.Kind is StructKind.Door or StructKind.Doorway && SVec.Distance(st.Center, claim.Pos) < 6)
                            GD.Print($"  {st.Kind} {st.A}-{st.B} open={st.Open} центр свободен={!w.WalkBlocked(st.Center, me.Radius + 0.05f, doors)}");
                    }
                }
                else if (a.StartsWith("piece")) _setPiece = int.Parse(a.Substring(5));   // отладка: элемент плана (5 — треугольник)
                else if (a == "rec") foreach (var d in _server.World.Deployables) { if (d.Kind == DeployKind.Recycler) GD.Print($"рециклер {d.Pos}: on={d.On} [{string.Join(", ", d.Storage.Items.ConvertAll(x => x.Id + "×" + x.Count))}] · у меня шестерни {_server.World.Find(_myId).Count("comp_gears")}"); }   // отладка
                else if (a == "peace") { foreach (var n in _server.World.Actors) if (n.Team == Team.Npc) { n.Dead = true; n.DeadTime = _server.World.Time; } }   // отладка: охрана РТ «спит»
                else if (a.StartsWith("speckey")) SpectatorKey(a.Substring(7) switch { "tab" => Key.Tab, "f" => Key.F, "2" => Key.Key2, "3" => Key.Key3, "4" => Key.Key4, "5" => Key.Key5, "6" => Key.Key6, _ => Key.Key1 });   // отладка: клавиши наблюдателя
                else if (a.StartsWith("learn")) _researchReq = a.Substring(5);   // отладка: learn<id>
                else if (a.StartsWith("slot")) _slot = int.Parse(a.Substring(4)) - 1;
                else if (a.StartsWith("craft")) _craftReq = int.Parse(a.Substring(5));
                else if (a == "stop") _forceWalk = SVec.Zero;
                else if (a == "bmenu") _buildMenu.Open();
                else if (a == "bdone") _buildMenu.Release();   // отладка: отпустить ПКМ
                else if (a.StartsWith("bpick")) { GetViewport().WarpMouse(GetViewport().GetVisibleRect().Size / 2 + new Vector2(0, -1).Rotated(int.Parse(a.Substring(5)) * Mathf.Tau / 5) * 110); }   // отладка: навести на сектор колеса
                else if (a.StartsWith("give")) { var g = a.Substring(4).Split('x'); _server.World.Find(_myId).Add(g[0], g.Length > 1 ? int.Parse(g[1]) : 1); }   // отладка: give<id>x<n>
                else if (a.StartsWith("ui")) _uiReq = ((UiOp)int.Parse(a.Substring(2).Split('_')[0]), a.Contains('_') ? a.Substring(a.IndexOf('_') + 1).Replace('~', '#').Replace('^', '>') : null, 0);   // отладка: ui<op>_<арг> (# → ~, > → ^)
                else if (a == "inv") _invOpen = !_invOpen;
                else if (a == "steps") GD.Print("steps: " + string.Join(", ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(Sfx.Played, kv => kv.Key.StartsWith("steps/")), kv => kv.Key[6..] + "×" + kv.Value)));   // отладка: какие шаги звучали
                else if (a == "info") GD.Print($"info: {_server.World.LastInfo.GetValueOrDefault(_myId)} | structs {_server.World.Structures.Count} | aim {_autoAim} | me {_server.World.Find(_myId).Pos}" + (_server.World.Substations.Count > 0 ? $" | подстанция: питание {_server.World.Substations[0].Powered}, крейт открыт {_server.World.Substations[0].Crate.Unlocked}, таймер {_server.World.Substations[0].CrateTimer:0}" : ""));   // отладка: последнее сообщение
                else if (a == "map") { _mapView.Visible = !_mapView.Visible; _mapView.QueueRedraw(); }
                else if (a == "crouch") _forceCrouch = true;
                else if (a == "save") SaveWorld();
                else if (a == "stand") _forceCrouch = false;
                else if (a.StartsWith("code")) _scriptCode = int.Parse(a.Substring(4));
                else if (a.StartsWith("go"))
                {
                    var p = a.Substring(2).Split(';');
                    _forceWalk = new SVec(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (a.StartsWith("subtp"))
                {
                    // отладка: перенестись на Подстанции в точку (x;y) от её угла
                    var p = a.Substring(5).Split(';');
                    _server.World.Find(_myId).Pos = _server.World.Substations[0].At(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (a.StartsWith("at"))
                {
                    // прицел относительно себя, метры: at1.5;-2
                    var p = a.Substring(2).Split(';');
                    var me = _server.World.Find(_myId).Pos;
                    _autoAim = me + new SVec(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        public override void _Ready()
        {
            DisplayServer.WindowSetTitle("Раствордд — прототип 0 (2D 3/4)");
            _server = new LocalServer(1337);
            // сохранение мира игрока — только при обычном запуске: любой отладочный флаг (прогоны ботов, скриншоты,
            // наблюдатель) работает со свежим миром и НЕ трогает сохранение (иначе тест перезапишет мир игрока)
            _saveEnabled = OS.GetCmdlineUserArgs().Length == 0;
            // отладка сохранений: --savefile=путь — отдельный файл, работает и в прогонах со скриншотами
            var sf = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--savefile="));
            if (sf != null) { SavePath = sf.Substring(11); _saveEnabled = true; }
            bool loaded = false;
            if (_saveEnabled && FileAccess.FileExists(SavePath))
            {
                try { loaded = _server.LoadSave(FileAccess.GetFileAsString(SavePath), out _awayReport); }
                catch (System.Exception ex) { GD.PrintErr($"сохранение не прочитано: {ex.Message}"); }
            }
            // песочница: --spectate — без своего персонажа, смотреть на ботов
            _spectate = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--spectate") >= 0;
            _myId = _spectate ? _server.ConnectSpectator() : _server.Connect("Игрок");
            // летопись вайпа — всегда: обычная игра и песочница пишут в user://chronicle, отладка — куда скажут (--chronicle=путь)
            try
            {
                var custom = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--chronicle="));
                bool debugRun = !_saveEnabled && !_spectate;
                if (custom != null || !debugRun)
                    _server.StartChronicle(custom?.Substring(12) ?? ProjectSettings.GlobalizePath($"user://chronicle/{(_spectate ? "sandbox" : "wipe")}_{System.DateTime.Now:yyyy-MM-dd_HH-mm}.jsonl"));
            }
            catch (System.Exception ex) { GD.PrintErr($"летопись не начата: {ex.Message}"); }
            // отладка: --simtest=600[,бот] — прокрутить 600 с мира без отрисовки и встать рядом с ботом
            var st = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--simtest="));
            if (st != null)
            {
                var p = st.Substring(10).Split(',');
                var rk = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--raidkit="));
                if (rk != null) _server.RaidKitAt = float.Parse(rk.Substring(10), System.Globalization.CultureInfo.InvariantCulture);
                _server.FastForward(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), s => GD.Print(s), _myId);
                var me = _server.World.Find(_myId);
int bi = p.Length > 1 ? int.Parse(p[1]) : 0;
                if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--raidtest") >= 0)
                {
                    GD.Print($"рейд-тест: повреждено построек {_server.DamageBotBase(bi, 0.5f, _myId)}");
                    _server.FastForward(240, s => GD.Print(s), _myId);
                }
                me.Pos = (p.Length > 2 && p[2] == "home" ? _server.BotHome(bi) + new SVec(1, 6) : p.Length > 2 && p[2] == "base" ? _server.BotHome(bi) + new SVec(-2.5f, 0.5f) : _server.BotPos(bi) + new SVec(-3, 2));   // base — камера на сам дом
                me.Dead = false; me.Hp = me.MaxHp;
            }
            if (loaded) CallDeferred(nameof(ToastLoaded));
            ParseArgs();

            VisionFx.Register();
            _map = _server.GetMap();
            BuildGround(_map);
            _world = new Node2D { YSortEnabled = true };
            AddChild(_world);
            BuildMap(_map);
            var flatDecals = new Node2D { ZIndex = -9 };
            AddChild(flatDecals);
            _decals = new DecalLayer(_map, flatDecals, _world);
            Sfx.Init(_world);
            Acoustics.Init(this, _map);
            SteamHrtf.Init(this, _map);
            _weather = new Weather(_world, this);
            Tuning.Load();
            _ambient = new Ambient(this, _map);
            _daylight = new CanvasModulate();
            AddChild(_daylight);

            _cam = new Camera2D { Zoom = new Vector2(_zoom, _zoom) };
            AddChild(_cam);
            _cam.MakeCurrent();

            _hud = new Hud();
            AddChild(_hud);
            var monList = new List<(string, SVec, SVec)>();
            for (int i = 0; i < _map.Yards.Count; i++) monList.Add((_map.YardNames[i], _map.Yards[i].min, _map.Yards[i].max));
            _mapView = new MapView(_map, monList);
            _mapView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _hud.AddChild(_mapView);
            _miniMap = new MiniMap(_mapView);
            _miniMap.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            _miniMap.OffsetLeft = -MiniMap.Side - 14; _miniMap.OffsetRight = -14; _miniMap.OffsetTop = 12; _miniMap.OffsetBottom = 12 + MiniMap.Side + 18;
            _hud.AddChild(_miniMap);
            _inv = new InventoryWindow();
            _inv.Op += (op, arg) => _uiReq = (op, arg, 0);
            _hud.AddChild(_inv);
            _buildMenu = new BuildMenu();
            _buildMenu.Picked += k => _setPiece = k;
            _hud.AddChild(_buildMenu);
            _buildBar = new BuildBar { Visible = false };
            _buildBar.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
            _buildBar.OffsetLeft = -_buildBar.CustomMinimumSize.X / 2; _buildBar.OffsetRight = _buildBar.CustomMinimumSize.X / 2;
            _buildBar.OffsetTop = -205; _buildBar.OffsetBottom = -145;
            _hud.AddChild(_buildBar);
            _structTip = new StructTip();
            _hud.AddChild(_structTip);
            _feed = new PickupFeed();
            _feed.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
            _feed.OffsetRight = -16; _feed.OffsetLeft = -16 - _feed.CustomMinimumSize.X;
            _feed.OffsetBottom = -62; _feed.OffsetTop = -62 - _feed.CustomMinimumSize.Y;
            _hud.AddChild(_feed);
            _guide = new Label { Position = new Vector2(0, 120), MouseFilter = Control.MouseFilterEnum.Ignore };
            _guide.AddThemeFontSizeOverride("font_size", 15);
            _guide.AddThemeColorOverride("font_color", new Color(0.85f, 0.92f, 1f));
            _guide.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            _guide.AddThemeConstantOverride("outline_size", 5);
            _hud.AddChild(_guide);
            _hud.CraftRequested += (i, n) => { _craftPendIdx = i; _craftPendLeft = n; };
            _hud.ResearchRequested += id => _researchReq = id;
            _hud.CodeEntered += (door, code) => _codeReq = (door, code);
            _hud.WipeRequested += () =>
            {
                // игрок сам попросил новый вайп: стираем сохранение и перезапускаем сцену
                _saveEnabled = false;
                string path = ProjectSettings.GlobalizePath(SavePath);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                Input.MouseMode = Input.MouseModeEnum.Hidden;
                GetTree().ReloadCurrentScene();
            };

            _ghost = new GhostView2D { Visible = false };
            _world.AddChild(_ghost);

            _cur = _server.BuildSnapshot(_myId);
            Input.MouseMode = Input.MouseModeEnum.Hidden;
            _hud.Toast("Вайп начался. Ты голый на пляже с булыжником. Бей деревья и камни, Q — крафт.");
            if (OS.GetCmdlineUserArgs().Contains("--kit")) _debugEdge = true;
            if (OS.GetCmdlineUserArgs().Contains("--craft")) _hud.ToggleCraft();
        }

        void ParseArgs()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--shot=")) _shotPath = arg.Substring(7);
                else if (arg.StartsWith("--frames=")) _shotFrame = int.Parse(arg.Substring(9));
                else if (arg.StartsWith("--burst="))
                {
                    var p = arg.Substring(8).Split(',');
                    _burst = (int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]));
                }
                else if (arg.StartsWith("--spawn="))
                {
                    var p = arg.Substring(8).Split(',');
                    _server.World.Find(_myId).Pos = new SVec(float.Parse(p[0], inv), float.Parse(p[1], inv));
                }
                else if (arg.StartsWith("--atsub="))
                {
                    // отладка: встать на Подстанции в точке (x, y) от её угла
                    var p = arg.Substring(8).Split(',');
                    var sub = _server.World.Substations[0];
                    _server.World.Find(_myId).Pos = sub.At(float.Parse(p[0], inv), float.Parse(p[1], inv));
                }
                else if (arg.StartsWith("--att0="))
                {
                    // отладка: встать на РТ тира 0 номер N в точке (x, y) от её угла: --att0=0,40,30
                    var p = arg.Substring(7).Split(',');
                    var rt = _server.World.Tier0[int.Parse(p[0])];
                    _server.World.Find(_myId).Pos = rt.At(float.Parse(p[1], inv), float.Parse(p[2], inv));
                }
                else if (arg.StartsWith("--atmon="))
                {
                    // отладка: встать у южных ворот РТ номер N
                    var mons = _server.World.Monuments;
                    var m = mons[System.Math.Clamp(int.Parse(arg.Substring(8)), 0, mons.Count - 1)];
                    _server.World.Find(_myId).Pos = new SVec(m.Center.X, m.Max.Y + 4);
                }
                else if (arg == "--buildspot")
                {
                    // отладка: встать на ближайшее к (60;60) место, где можно строить дом 3×3 (лицом на восток)
                    var w = _server.World;
                    for (int r = 0; r < 200; r += 2)
                    {
                        bool found = false;
                        for (int k = 0; k < 16 && !found; k++)
                        {
                            var c = new SVec(60, 60) + new SVec(Mathf.Cos(k * 0.39f), Mathf.Sin(k * 0.39f)) * r;
                            var (ci, cj) = Building.CellOf(c);
                            bool ok = true;
                            for (int di = -1; di <= 3 && ok; di++) for (int dj = -1; dj <= 3 && ok; dj++) ok = w.FoundationGeoOk(ci + di, cj + dj, out _);
                            if (ok) { w.Find(_myId).Pos = Building.CellCenter(ci, cj) + new SVec(-1, 0); found = true; }
                        }
                        if (found) break;
                    }
                }
else if (arg == "--atcrate")
                {
                    // отладка: встать у ближайшего к центру ящика РТ без охраны
                    var w = _server.World;
                    LootBag best = null;
                    foreach (var b in w.Bags) if (b.IsCrate && b.Tier == 0 && (best == null || b.Pos.Length() < best.Pos.Length())) best = b;
                    best ??= w.Bags.Find(b => b.IsCrate);
                    if (best != null) { w.Find(_myId).Pos = best.Pos + new SVec(-1.2f, 0.4f); _autoAim = best.Pos; }
                }
                else if (arg == "--overview") _overview = true;
                else if (arg == "--perf") _perf = true;
                else if (arg.StartsWith("--riggallery")) AddChild(new RigGallery(arg.Contains('=') ? arg.Split('=')[1] : "naked"));
                else if (arg == "--crouch") _forceCrouch = true;
                else if (arg == "--sprint") _forceSprint = true;
                else if (arg.StartsWith("--walk="))
                {
                    var p = arg.Substring(7).Split(",");
                    _forceWalk = new SVec(float.Parse(p[0], inv), float.Parse(p[1], inv));
                }
                else if (arg == "--press-n") _pressNAt = 5;
                else if (arg.StartsWith("--day=")) _forcedDay = float.Parse(arg.Substring(6), inv);
                else if (arg.StartsWith("--zoom=")) _zoom = float.Parse(arg.Substring(7), inv);
                else if (arg.StartsWith("--peek=")) _forcedPeek = float.Parse(arg.Substring(7), inv);
                else if (arg.StartsWith("--screen="))
                {
                    var p = arg.Substring(9).Split(',');
                    _autoScreen = new Vector2(float.Parse(p[0], inv), float.Parse(p[1], inv));
                }
                else if (arg.StartsWith("--do="))
                {
                    // отладочный сценарий: --do=10:kit,20:slot5,30:fire,60:interact,70:craft3
                    foreach (var step in arg.Substring(5).Split(','))
                    {
                        var kv = step.Split(':');
                        int frame = int.Parse(kv[0]);
                        if (!_script.TryGetValue(frame, out var list)) _script[frame] = list = new List<string>();
                        list.Add(kv[1]);
                    }
                }
                else if (arg.StartsWith("--aim="))
                {
                    var p = arg.Substring(6).Split(',');
                    _autoAim = new SVec(float.Parse(p[0], inv), float.Parse(p[1], inv));
                }
                else if (arg == "--occl-off") _occlSelfOnly = true;
                else if (arg.StartsWith("--occl="))
                {
                    // отладка перекрытий 3/4: --occl=tree — противник за кроной дерева, --occl=wall — за бетонной стеной склада у ворот.
                    // В обоих случаях он честно в прямой видимости (луч идёт мимо ствола / через проход), но рисунок его закрывает.
                    var w = _server.World;
                    var me = w.Find(_myId);
                    var foe = w.Actors.Find(a => a.Id != _myId && !a.Spectator && a.Team == Team.Npc) ?? w.Actors.Find(a => a.Id != _myId && !a.Spectator);
                    SVec foePos;
                    if (arg.EndsWith("wall")) { foePos = new SVec(16.8f, -5.8f); me.Pos = new SVec(21f, -1.5f); }
                    else
                    {
                        // лиственная роща у склада; противник чуть сбоку от ствола — луч обзора идёт мимо ствола
                        var at = new SVec(8, 5);
                        ResourceNode tree = null;
                        foreach (var n in w.Nodes)
                            if (n.Kind == NodeKind.Tree && (tree == null || SVec.Distance(n.Pos, at) < SVec.Distance(tree.Pos, at))) tree = n;
                        foePos = tree.Pos + new SVec(tree.Radius * 0.9f + 0.6f, -0.8f);
                        me.Pos = tree.Pos + new SVec(0, 3.5f);
                    }
                    if (foe != null) { foe.Pos = foePos; foe.Dead = false; }
                    _autoAim = foePos;
                    GD.Print($"occl: я {me.Pos}, противник {foe?.Id} {foe?.Name} {foePos}");
                }
                else if (arg.StartsWith("--near="))
                {
                    // встать рядом с ближайшим узлом вида (tree/stone/metal/sulfur) и смотреть на него: --near=tree[,угол°]
                    var p = arg.Substring(7).Split(',');
                    var kind = System.Enum.Parse<NodeKind>(p[0], true);
                    float ang = p.Length > 1 ? Mathf.DegToRad(float.Parse(p[1], inv)) : Mathf.Pi;
                    var me = _server.World.Find(_myId);
                    ResourceNode best = null;
                    foreach (var n in _server.World.Nodes)
                        if (n.Kind == kind && (best == null || SVec.Distance(n.Pos, me.Pos) < SVec.Distance(best.Pos, me.Pos))) best = n;
                    if (best != null)
                    {
                        me.Pos = best.Pos + new SVec(Mathf.Cos(ang), Mathf.Sin(ang)) * (best.Radius + 0.9f);
                        _autoAim = best.Pos;
                    }
                }
            }
        }

        // ---------- построение ----------

        void BuildGround(MapInfo map)
        {
            float half = map.HalfSize * Iso.PPM, size = half * 2;
            var ground = new Node2D { ZIndex = -10 };
            AddChild(ground);

            ground.AddChild(new ColorRect { Color = new Color(0.07f, 0.15f, 0.21f), Position = new Vector2(-half - 3000, -half - 3000), Size = new Vector2(size + 6000, size + 6000) });

            // вся суша — один шейдер с переходами и рельефом светотенью
            ground.AddChild(new TerrainView(map, 1337));
        }

        void BuildMap(MapInfo map)
        {
            _prefab = new PrefabLayer(map, _world, this);
            _lighting = new LightingLayer(map, _world);
            foreach (var w in map.Walls)
            {
                if (w.Hidden) continue;   // хитбокс пропса РТ — рисуется спрайтом
                var v = new WallView2D(w, TerrainView.BiomeAt(map, (w.Min + w.Max) * 0.5f));
                _world.AddChild(v);
                _mapWalls.Add(v);
                if (w.Tex != null) _prefab.WallsById[w.Id] = v;
            }
        }

        // ---------- ввод ----------

        public override void _UnhandledInput(InputEvent e)
        {
            if (e is InputEventKey k && k.Pressed && !k.Echo)
            {
                if (_buildMenu.PickByKey(k.PhysicalKeycode)) return;
                if (_spectate && SpectatorKey(k.PhysicalKeycode)) return;
                switch (k.PhysicalKeycode)
                {
                    case Key.R: _reloadEdge = true; break;
                    case Key.E: _interactEdge = true; break;
                    case Key.H: _healEdge = true; break;
                    case Key.Enter: case Key.KpEnter: _respawnEdge = true; break;
                    case Key.B: _bagEdge = true; break;
                    case Key.Key1: _slot = 0; break;
                    case Key.Key2: _slot = 1; break;
                    case Key.Key3: _slot = 2; break;
                    case Key.Key4: _slot = 3; break;
                    case Key.Key5: _slot = 4; break;
                    case Key.Key6: _slot = 5; break;
                    case Key.F: _eatEdge = true; break;
                    case Key.N: _timeEdge = true; break;
                    case Key.F1:
                        _hud.ToggleAdmin();
                        Input.MouseMode = _hud.AdminOpen || _hud.CraftOpen ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Hidden;
                        break;
                    case Key.F9: _debugEdge = true; break;
                    case Key.Q:
                        _hud.ToggleCraft();
                        Input.MouseMode = _hud.CraftOpen ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Hidden;
                        break;
                    case Key.Tab: _invOpen = !_invOpen; if (!_invOpen && _cur.Self.Ui != null) _uiReq = (UiOp.Close, null, 0); break;
                    case Key.G: _mapView.Visible = !_mapView.Visible; _mapView.QueueRedraw(); break;
                    case Key.F5: SaveWorld(); _hud.Toast("Мир сохранён"); break;
                    case Key.F12: SaveScreenshot($"user://shot_{Time.GetTicksMsec()}.png"); break;
                    case Key.Escape when _mapView.Visible:
                        _mapView.Visible = false;
                        break;
                    case Key.Escape when _inv.Visible:
                        _invOpen = false;
                        if (_cur.Self.Ui != null) _uiReq = (UiOp.Close, null, 0);
                        break;
                    case Key.Escape when _hud.CraftOpen:
                        _hud.ToggleCraft();
                        Input.MouseMode = Input.MouseModeEnum.Hidden;
                        break;
                    case Key.Escape:
                        Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Hidden ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Hidden;
                        break;
                }
            }
            else if (e is InputEventMouseButton rb && rb.ButtonIndex == MouseButton.Right && !rb.Pressed) _buildMenu.Release();
            else if (e is InputEventMouseButton mb && mb.Pressed)
            {
                // ПКМ с Планом — колесо элементов (держишь, наводишь, отпускаешь) вместо прицела
                if (mb.ButtonIndex == MouseButton.Right && _cur?.Self != null && _cur.Self.HoldingPlan && !UiShown) _buildMenu.Open();
                // с Планом колесо листает элементы стройки (Ctrl+колесо — зум), иначе — зум
                bool plan = _cur?.Self != null && _cur.Self.HoldingPlan && !mb.CtrlPressed;
                if (plan && mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
                    _setPiece = (int)Building.NextPiece(_cur.Self.BuildPiece, mb.ButtonIndex == MouseButton.WheelDown ? 1 : -1);
                else if (mb.ButtonIndex == MouseButton.WheelUp) _zoom = Mathf.Min(2.6f, _zoom * 1.1f);
                else if (mb.ButtonIndex == MouseButton.WheelDown) _zoom = Mathf.Max(0.9f, _zoom / 1.1f);
            }
        }

        /// <summary>Наблюдатель: Tab — следующий бот, F — свободная камера, 1–4 — скорость ×1/×2/×4/×8.</summary>
        bool SpectatorKey(Key key)
        {
            switch (key)
            {
                case Key.Tab:
                    var bots = _server.BotLines();
                    if (bots.Count == 0) return true;
                    int i = bots.FindIndex(b => b.id == _followId);
                    _followId = bots[(i + 1) % bots.Count].id;
                    return true;
                case Key.F: _followId = 0; return true;
                case Key.Key1: _timeScale = 1; return true;
                case Key.Key2: _timeScale = 2; return true;
                case Key.Key3: _timeScale = 4; return true;
                case Key.Key4: _timeScale = 8; return true;
                case Key.Key5: _timeScale = 10; return true;
                case Key.Key6: _timeScale = 20; return true;
            }
            return false;
        }

        void UpdateSpectatorPanel(float dt)
        {
            if (_specPanel == null)
            {
                _hud.SpectatorMode();
                _specPanel = new Label { Position = new Vector2(16, 14), MouseFilter = Control.MouseFilterEnum.Ignore };
                _specPanel.AddThemeFontSizeOverride("font_size", 14);
                _specPanel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
                _specPanel.AddThemeConstantOverride("outline_size", 5);
                _hud.AddChild(_specPanel);
            }
            if ((_specRefresh -= dt) > 0) return;
            _specRefresh = 0.4f;
            var sb = new System.Text.StringBuilder();
            sb.Append($"НАБЛЮДАТЕЛЬ · скорость ×{_timeScale}{(_timeScale > 1 && _effSpeed < _timeScale * 0.85f ? $" (факт ×{_effSpeed:0.#})" : "")} · время {(int)(_server.World.Time / 60):00}:{(int)(_server.World.Time % 60):00}\n");
            sb.Append("Tab — следующий бот · F — свободная камера · WASD/Shift — лететь · 1–6 — скорость ×1/×2/×4/×8/×10/×20\n\n");
            var lines = _server.BotLines();
            if (lines.Count <= 5)
                foreach (var (id, line) in lines) sb.Append(id == _followId ? "▶ " : "   ").Append(line).Append('\n');
            else
            {
                // много ботов: подробно — тот, за кем следим; остальные — таблица гонки (кто впереди)
                var f = lines.Find(l => l.id == _followId);
                if (f.line != null) sb.Append("▶ ").Append(f.line).Append("\n\n");
                sb.Append("ГОНКА\n");
                int n = 0;
                foreach (var (id, _, line) in _server.BotShortLines())
                    if (n++ < 24) sb.Append(id == _followId ? " ▶ " : "    ").Append(line).Append('\n');
            }
            var ms = _server.World.Milestones;
            if (ms.Count > 0)
            {
                sb.Append("\nВЕХИ\n");
                for (int i = System.Math.Max(0, ms.Count - 12); i < ms.Count; i++) sb.Append("   ").Append(ms[i]).Append('\n');
            }
            _specPanel.Text = sb.ToString();
        }

        InputCmd BuildCmd()
        {
            var move = SVec.Zero;
            bool typing = _hud.CodeOpen;
            if (!typing && Input.IsPhysicalKeyPressed(Key.W)) move.Y -= 1;
            if (!typing && Input.IsPhysicalKeyPressed(Key.S)) move.Y += 1;
            if (!typing && Input.IsPhysicalKeyPressed(Key.A)) move.X -= 1;
            if (!typing && Input.IsPhysicalKeyPressed(Key.D)) move.X += 1;
            move += _forceWalk;
            return new InputCmd
            {
                Follow = _followId,
                Move = move,
                Aim = _aim,
                Fire = _autoFireFrames > 0 || (!_hud.CraftOpen && !_hud.AdminOpen && !UiShown && !_buildMenu.Visible && Input.IsMouseButtonPressed(MouseButton.Left)),   // клики по меню крафта не стреляют
                Eat = _eatEdge,
                DebugKit = _debugEdge,
                Craft = _craftReq,
                Code = _codeReq?.code ?? -1,
                UiOp = (int)(_uiReq?.op ?? UiOp.None),
                SetPiece = _setPiece,
                Research = _researchReq,
                UiItem = _uiReq?.item,
                UiIndex = _uiReq?.index ?? 0,
                CodeDoor = _codeReq?.door ?? 0,
                Sprint = _forceSprint || Input.IsPhysicalKeyPressed(Key.Shift),
                Reload = _reloadEdge,
                Interact = _interactEdge,
                Heal = _healEdge,
                Respawn = _respawnEdge,
                RespawnBag = _bagEdge,
                SelectSlot = _slot,
                Peek = _peek,
                ToggleTime = _timeEdge,
                Crouch = _forceCrouch || Input.IsPhysicalKeyPressed(Key.Alt),
            };
        }

        /// <summary>Курсор указывает на высоту груди/оружия, поэтому точка на земле — чуть ниже курсора.</summary>
        void UpdateAim(float dt)
        {
            // положение курсора относительно центра экрана: -1..1 по каждой оси
            var vs = GetViewport().GetVisibleRect().Size;
            var screen = _autoAim.HasValue ? _autoScreen : GetViewport().GetMousePosition();
            _screenDir = ((screen - vs / 2) / (vs / 2)).Clamp(-Vector2.One, Vector2.One);

            // прицеливание на ПКМ: конус уже, видно дальше, камеру можно протянуть к курсору
            float peekTarget = _forcedPeek ?? (Input.IsMouseButtonPressed(MouseButton.Right) && !(_cur?.Self?.HoldingPlan ?? false) ? 1f : 0f);   // с Планом ПКМ — меню
            _peek = Mathf.MoveToward(_peek, peekTarget, dt * 5f);

            if (_autoAim.HasValue) { _aim = _autoAim.Value; return; }
            var m = GetGlobalMousePosition();
            _aim = new SVec(m.X / Iso.PPM, (m.Y + Iso.GunH) / Iso.PPM);
        }

        bool InYard(SVec p)
        {
            foreach (var (mn, mx) in _map.Yards)
                if (p.X > mn.X + 2 && p.X < mx.X - 2 && p.Y > mn.Y + 2 && p.Y < mx.Y - 2) return true;
            return false;
        }

        // ---------- сохранение мира ----------
        string SavePath = "user://world_save.json";
        bool _saveEnabled;
        double _saveTimer;

        string _awayReport;
        void ToastLoaded() => _hud.Toast(_awayReport ?? "Мир загружен — продолжаем с того же места");

        void SaveWorld()
        {
            if (!_saveEnabled) return;
            string path = ProjectSettings.GlobalizePath(SavePath);
            try
            {
                System.IO.File.WriteAllText(path + ".tmp", _server.SaveJson());
                System.IO.File.Move(path + ".tmp", path, true);   // атомарно: не оставим полуфайл при вылете
            }
            catch (System.Exception ex) { GD.PrintErr($"сохранение не записано: {ex.Message}"); }
        }

        public override void _Notification(int what)
        {
            if (what == NotificationWMCloseRequest) SaveWorld();
        }

        // ---------- цикл ----------

        public override void _Process(double delta)
        {
            _frame++;
            if (_frame == _pressNAt) _timeEdge = true;
            _saveTimer += delta;
            if (_saveTimer >= 60) { _saveTimer = 0; SaveWorld(); }   // автосохранение раз в минуту
            RunScript();
            UpdateAim((float)delta);

            _acc += delta * _timeScale;
            int steps = 0, maxSteps = 5 * _timeScale;
            // ускорение (песочница): на шаги мира — не больше ~25 мс за кадр, снапшот — один раз за кадр;
            // не успеваем — честно отстаём (скорость «факт» в панели), а картинка остаётся плавной
            bool fast = _timeScale > 2;
            ulong frameT0 = Time.GetTicksUsec();
            while (_acc >= World.Dt && steps < maxSteps && (!fast || Time.GetTicksUsec() - frameT0 < 25000))
            {
                if (fast)
                {
                    if (_craftReq < 0 && _craftPendLeft > 0) { _craftReq = _craftPendIdx; _craftPendLeft--; }
                    _server.Submit(_myId, BuildCmd());
                    var tf = Time.GetTicksUsec();
                    _server.Tick();
                    _simTime += (Time.GetTicksUsec() - tf) / 1000.0;
                    _acc -= World.Dt;
                    steps++;
                    continue;
                }
                _prevActors.Clear();
                foreach (var a in _cur.Actors) _prevActors[a.Id] = a;
                _prevProjs.Clear();
                foreach (var p in _cur.Projectiles) _prevProjs[p.Id] = p;

                if (_craftReq < 0 && _craftPendLeft > 0) { _craftReq = _craftPendIdx; _craftPendLeft--; }
                _server.Submit(_myId, BuildCmd());
                var t0 = Time.GetTicksUsec();
                _server.Tick();
                _simTime += (Time.GetTicksUsec() - t0) / 1000.0;
                _cur = _server.BuildSnapshot(_myId);
                _reloadEdge = _interactEdge = _healEdge = _respawnEdge = _eatEdge = _debugEdge = _timeEdge = _bagEdge = false;
                _slot = _craftReq = -1;
                _codeReq = null;
                _uiReq = null;
                _setPiece = -1;
                _researchReq = null;
                if (_autoFireFrames > 0) _autoFireFrames--;
                HandleEvents();
                _acc -= World.Dt;
                steps++;
            }
            if (fast && steps > 0)
            {
                _prevActors.Clear();
                foreach (var a in _cur.Actors) _prevActors[a.Id] = a;
                _prevProjs.Clear();
                foreach (var p in _cur.Projectiles) _prevProjs[p.Id] = p;
                _cur = _server.BuildSnapshot(_myId);
                _reloadEdge = _interactEdge = _healEdge = _respawnEdge = _eatEdge = _debugEdge = _timeEdge = _bagEdge = false;
                _slot = _craftReq = -1;
                _codeReq = null;
                _uiReq = null;
                _setPiece = -1;
                _researchReq = null;
                HandleEvents();
            }
            _effSpeed = Mathf.Lerp(_effSpeed, (float)(steps * World.Dt / System.Math.Max(delta, 1e-4)), 0.05f);
            if (steps == maxSteps || _acc > World.Dt * maxSteps) _acc = 0;
            if (_spectate) UpdateSpectatorPanel((float)delta);

            float alpha = (float)(_acc / World.Dt);
            float dt = (float)delta;
            Render(alpha, dt);
            UpdateCamera(dt);
            if (!_overview) _decals.Update(_cam.Position);
            {
                var me = new SVec(_selfPx.X / Iso.PPM, _selfPx.Y / Iso.PPM);
                bool yard = InYard(me);
                Acoustics.Update(me, _cur.Nodes, TerrainView.BiomeAt(_map, me), yard, delta);
                if (!_overview)
                    _weather.Update(_cam.Position, GetViewport().GetVisibleRect().Size / 2 / _cam.Zoom.X, TerrainView.BiomeAt(_map, me),
                                    World.NightFactorAt(_forcedDay ?? _cur.DayTime));
            }
            _ambient.Update(new SVec(_selfPx.X / Iso.PPM, _selfPx.Y / Iso.PPM), _forcedDay ?? _cur.DayTime, dt);
            _daylight.Color = _overview ? Colors.White : DayColor(_forcedDay ?? _cur.DayTime);
            {
                // свет РТ: фонари ночью, лампы корпуса Подстанции — при включённом щитке
                bool powered = _cur.DecorSprites.Exists(d => d.sprite == "props/subst_main_panel_on");
                var halfV = GetViewport().GetVisibleRect().Size / 2 / _cam.Zoom.X;
                _lighting.Update(_overview ? 0 : World.NightFactorAt(_forcedDay ?? _cur.DayTime), powered, new Rect2(_cam.Position - halfV, halfV * 2), dt);
            }
            UpdateHud(dt);

            if (_hud.AdminOpen && _frame % 15 == 0) _hud.SetAdminSounds(PlayingSounds());
            if (_perf)
            {
                _perfTime += delta; _perfFrames++;
                if (_perfTime >= 3)
                {
                    GD.Print(PlayingSounds() + $" buses {AudioServer.BusCount}");
                    GD.Print($"amb: biome {TerrainView.BiomeAt(_map, new SVec(_selfPx.X / Iso.PPM, _selfPx.Y / Iso.PPM))} | {_ambient.Debug}");
                    GD.Print($"perf: {_perfFrames / _perfTime:F0} fps, sim {_simTime / (_perfTime * 30):F2} ms/tick, nodes {_server.World.Nodes.Count}, views {_world.GetChildCount()}");
                    _perfTime = 0; _perfFrames = 0; _simTime = 0;
                }
            }
            // серия кадров для разбора анимаций: --burst=начало,сколько,шаг → <shot>_N.png
            if (_shotPath != null && _burst.count > 0 && _frame >= _burst.start && (_frame - _burst.start) % _burst.step == 0
                && (_frame - _burst.start) / _burst.step < _burst.count)
                SaveScreenshot(_shotPath.Replace(".png", $"_{(_frame - _burst.start) / _burst.step:00}.png"));
            if (_shotPath != null && _frame == _shotFrame)
            {
                SaveScreenshot(_shotPath);
                if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--sfxlog") >= 0)
                    foreach (var kv in Sfx.Played) GD.Print($"sfx {kv.Key}: {kv.Value}");
                GetTree().Quit();
            }
        }

        void SaveScreenshot(string path)
        {
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"screenshot: {ProjectSettings.GlobalizePath(path)}");
        }

        void HandleEvents()
        {
            foreach (var e in _cur.Events)
            {
                switch (e.Kind)
                {
                    case SimEventKind.Shot:
                        if (e.Text != "weapon_bow") Flash(e.Pos + e.Dir * 0.3f, new Color(1, 0.8f, 0.4f), 7, 0.06f, true);
                        if (_actorViews.TryGetValue(e.ActorId, out var shooter)) shooter.Kick();
                        if (e.ActorId == _myId && e.Text != "weapon_bow") _shake = Mathf.Min(1.5f, _shake + 0.6f);
                        if (e.Text != "weapon_bow") SpawnCasing(e.Pos, e.Dir);
                        // настоящий сэмпл выстрела для всего огнестрела (самопал — чуть выше и тише); каждый доигрывает до конца
                        if (e.Text == "weapon_bow") Sfx.Play("shot_bow", Iso.Px(e.Pos), -4, 25, 0.06f);
                        else Sfx.Play("shot_ak", Iso.Px(e.Pos), e.Text == "weapon_ak" ? 0 : -3, 120, 0.03f, e.Text == "weapon_ak" ? 1f : 1.12f);
                        break;
                    case SimEventKind.Reload:
                        if (_actorViews.TryGetValue(e.ActorId, out var reloader)) reloader.StartReload(Items.Get(e.Text)?.Weapon?.ReloadTime ?? 2f);
                        Sfx.Play("reload_ak", Iso.Px(e.Pos), -2, 25, 0.02f);
                        break;
                    case SimEventKind.Swing:
                        if (_actorViews.TryGetValue(e.ActorId, out var swinger)) swinger.StartSwing();
                        Sfx.Play("swing", Iso.Px(e.Pos), -6, 15, 0.15f);
                        break;
                    case SimEventKind.HitNode:
                        if (_nodeViews.TryGetValue(e.OtherId, out var nv)) nv.Hit();
                        Sfx.Play(NodeHitSound(e.OtherId), Iso.Px(e.Pos), -2, 30, 0.1f);
                        SpawnChips(e.OtherId, e.Pos, e.ActorId);
                        if (_actorViews.TryGetValue(e.ActorId, out var hitter)) hitter.HitStop(0.07f);   // стоп-кадр: инструмент «вязнет» в цели
                        if (e.ActorId == _myId) _shake = Mathf.Min(1.2f, _shake + 0.45f);
                        break;
                    case SimEventKind.Gather:
                        FloatText(e.Pos, $"+{(int)e.Value} {e.Text}", new Color(1, 0.95f, 0.75f));
                        if (e.OtherId == 0) Sfx.Play("loot", Iso.Px(e.Pos), -8, 10);
                        break;
                    case SimEventKind.Info:
                        _hud.Toast(e.Text);
                        break;
                    case SimEventKind.Crafted:
                        _hud.Toast("Скрафчено: " + e.Text);
                        Sfx.Play("craft_done", _selfPx, -6, 10, 0f);
                        break;
                    case SimEventKind.Built:
                        Sfx.Play(e.Value >= 2 ? "hit_stone" : "hit_wood", Iso.Px(e.Pos), -3, 25, 0.1f);
                        SpawnChipsAt(e.Pos, TierChips[System.Math.Clamp((int)e.Value, 0, 4)], e.ActorId, 5);
                        if (e.ActorId == _myId && e.Value > 0) _hud.Toast($"{e.Text}: улучшено");
                        break;
                    case SimEventKind.Destroyed:
                        Sfx.Play((int)e.Value >= 2 ? "hit_stone" : "hit_wood", Iso.Px(e.Pos), 2, 40, 0.05f);
                        SpawnChipsAt(e.Pos, TierChips[System.Math.Clamp((int)e.Value, 0, 4)], -1, 18);
                        _shake = Mathf.Min(1.2f, _shake + 0.3f);
                        break;
                    case SimEventKind.Revealed:
                        // ячейка раскрылась: звоночек по редкости (Arc Raiders)
                        Sfx.Play($"reveal_{System.Math.Clamp((int)e.Value, 0, 4)}", _selfPx, -4, 10, 0.02f);
                        Sfx.Play("rummage", _selfPx, -10, 10, 0.1f);
                        break;
                    case SimEventKind.Machine:
                        // механизмы готовых РТ: пробка, питание, роллета, считыватель крейта
                        switch (e.Text)
                        {
                            case "fuse": Sfx.Play("lock_click", Iso.Px(e.Pos), 0, 15, 0f); Flash(e.Pos, new Color(1f, 0.9f, 0.5f), 6, 0.15f, true); break;
                            case "power": Sfx.Play("fuse_hiss", Iso.Px(e.Pos), 2, 60, 0f); Flash(e.Pos, new Color(0.6f, 0.8f, 1f), 18, 0.4f, true); _shake = Mathf.Min(1f, _shake + 0.3f); break;
                            case "shutter": Sfx.Play("hit_metal_ore", Iso.Px(e.Pos), 0, 40, 0.1f); break;
                            case "reader": Sfx.Play("c4_beep", Iso.Px(e.Pos), 0, 60, 0f); Flash(e.Pos, new Color(0.4f, 1f, 0.5f), 5, 0.2f, true); break;
                            case "crate": Sfx.Play("craft_done", Iso.Px(e.Pos), 2, 40, 0f); Flash(e.Pos, new Color(0.4f, 1f, 0.5f), 14, 0.5f, true); break;
                            case "pry": Sfx.Play("hit_metal_ore", Iso.Px(e.Pos), -2, 25, 0.15f); break;
                            case "glass": Sfx.Play("glass_break", Iso.Px(e.Pos), -4, 30, 0.12f); break;
                            case "bang": Sfx.Play("hit_wood", Iso.Px(e.Pos), 2, 40, 0.1f); _shake = Mathf.Min(0.6f, _shake + 0.15f); break;
                        }
                        break;
                    case SimEventKind.Locked:
                        Sfx.Play("lock_click", Iso.Px(e.Pos), -4, 10, 0f);
                        if (e.ActorId == _myId) { if (_scriptCode >= 0) _codeReq = (e.OtherId, _scriptCode); else _hud.ShowCode(e.OtherId); }
                        break;
                    case SimEventKind.Explosion when e.Text == "expl_fire":
                        Sfx.Play("glass_break", Iso.Px(e.Pos), -2, 40, 0.08f);
                        Sfx.Play("fire_whoosh", Iso.Px(e.Pos), -4, 40, 0.08f);
                        Flash(e.Pos, new Color(1f, 0.6f, 0.25f), 30, 0.35f, true);
                        break;
                    case SimEventKind.Explosion:
                    {
                        bool big = e.Text == "expl_c4";
                        Flash(e.Pos, new Color(1f, 0.75f, 0.4f), big ? 70 : 50, 0.3f, true);
                        Flash(e.Pos, new Color(0.3f, 0.28f, 0.26f, 0.8f), big ? 55 : 40, 1.2f);
                        Sfx.Play(big ? "explosion_big" : "explosion_small", Iso.Px(e.Pos), 4, 300, 0.06f);
                        float d = SVec.Distance(e.Pos, new SVec(_selfPx.X / Iso.PPM, _selfPx.Y / Iso.PPM));
                        _shake = Mathf.Min(2.5f, _shake + (big ? 2.2f : 1.4f) * Mathf.Clamp(1f - d / 40f, 0.15f, 1f));
                        SpawnChipsAt(e.Pos, new[] { new Color(0.35f, 0.33f, 0.3f), new Color(0.55f, 0.5f, 0.45f), new Color(0.2f, 0.19f, 0.18f), new Color(0.9f, 0.6f, 0.3f) }, -1, big ? 34 : 22);
                        break;
                    }
                    case SimEventKind.Door:
                        Sfx.Play("hit_wood", Iso.Px(e.Pos), -8, 20, 0.2f);
                        break;
                    case SimEventKind.HitStruct:
                        Sfx.Play(e.Text is "камень" or "металл" or "сталь" ? "hit_metal_ore" : "hit_wood", Iso.Px(e.Pos), -2, 30, 0.1f);
                        SpawnChipsAt(e.Pos, TierChips[e.Text == "каркас" ? 0 : e.Text == "дерево" ? 1 : e.Text == "камень" ? 2 : 3], e.ActorId, e.Value > 0 ? 6 : 3);
                        if (_actorViews.TryGetValue(e.ActorId, out var sh)) sh.HitStop(0.07f);
                        if (e.ActorId == _myId) _shake = Mathf.Min(1.2f, _shake + 0.4f);
                        break;
                    case SimEventKind.Placed:
                        _hud.Toast("Установлено: " + e.Text);
                        Sfx.Play("hit_wood", Iso.Px(e.Pos), -4, 20);
                        break;
                    case SimEventKind.HitActor:
                        Flash(e.Pos, new Color(0.85f, 0.1f, 0.08f), 5, 0.15f);
                        Sfx.Play("hit_flesh", Iso.Px(e.Pos), -2, 30, 0.12f);
                        if (e.OtherId == _myId) _hud.Crosshair.HitFlash = 0.15f;
                        if (e.ActorId == _myId) _hud.FlashDamage();
                        if (_actorViews.TryGetValue(e.ActorId, out var victim))
                        {
                            var vf = _cur.Actors.Find(x => x.Id == e.ActorId).Facing;
                            var from = _actorViews.TryGetValue(e.OtherId, out var att) ? att.Position : Iso.Px(e.Pos);
                            var toAtt = from - victim.Position;
                            victim.Flinch(toAtt.LengthSquared() < 1 || toAtt.Dot(new Vector2(Mathf.Cos(vf), Mathf.Sin(vf))) >= 0);
                        }
                        break;
                    case SimEventKind.HitWall:
                        Flash(e.Pos, new Color(1, 0.85f, 0.5f), 3, 0.1f);
                        break;
                    case SimEventKind.Death:
                        GD.Print($"death: {e.ActorId} by {e.OtherId} at {e.Pos}");
                        if (e.ActorId == _myId) _hud.Toast("Тебя убили.");
                        else if (e.OtherId == _myId) _hud.Toast("Убит. Лут на трупе — подойди и нажми E.");
                        break;
                    case SimEventKind.Loot:
                        if (e.ActorId == _myId) { _hud.Toast("Залутано: " + e.Text); Sfx.Play("loot", _selfPx, -4, 10); }
                        break;
                    case SimEventKind.Respawn:
                        if (e.ActorId == _myId) _hud.Toast("Проснулся на пляже. Голый, с булыжником.");
                        break;
                }
            }
        }

        string NodeHitSound(int nodeId)
        {
            foreach (var n in _cur.Nodes)
                if (n.Id == nodeId)
                    return n.Kind == NodeKind.Tree ? "hit_wood" : n.Kind == NodeKind.Stone ? "hit_stone" : "hit_metal_ore";
            return "hit_stone";
        }

        /// <summary>Шаги: звук каждые ~0.8 м пути; по траве мягко, по гравию/песку/снегу/бетону жёстче.</summary>
        void Footstep(int id, SVec pos, float speed, float dt, bool crouch = false)
        {
            if (speed < 0.5f) return;
            _stepAcc.TryGetValue(id, out var acc);
            acc += speed * dt;
            if (acc >= Footsteps.Stride(speed, crouch))
            {
                acc = 0;
                bool sprint = speed > Sim.World.WalkSpeed * 1.3f;
                float vol = (id == _myId ? Tuning.StepsVolumeDb : -4) + (sprint ? 3 : 0) - (crouch ? 10 : 0);
                Footsteps.Play(SurfaceAt(pos), Iso.Px(pos), vol, crouch ? 7 : sprint ? 24 : 18);
            }
            _stepAcc[id] = acc;
        }

        /// <summary>Для админки: что сейчас реально звучит (имя файла и громкость).</summary>
        string PlayingSounds()
        {
            var sb = new System.Text.StringBuilder("Сейчас играет:\n");
            foreach (var n in FindChildren("*", "", true, false))
            {
                if (n is AudioStreamPlayer ap && ap.Playing && !ap.StreamPaused && ap.VolumeDb > -60)
                    sb.Append($"  {System.IO.Path.GetFileName(ap.Stream?.ResourcePath)}  {ap.VolumeDb:F0} дБ  [{ap.Bus}]\n");
                else if (n is AudioStreamPlayer2D p2 && p2.Playing && p2.VolumeDb > -60)
                    sb.Append($"  {System.IO.Path.GetFileName(p2.Stream?.ResourcePath)}  (в мире) {p2.VolumeDb:F0} дБ\n");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Что под ногами: пол постройки (каркас и дерево — дерево, камень — бетон, металл — металл),
        /// участки готовых РТ (кафель, бетон, асфальт — бетон; гравий и грунт — песок), площадки старых РТ — бетон,
        /// иначе биом: тундра — снег, пляж и пустошь — песок, болото — грязь, остальное — трава.
        /// </summary>
        Surface SurfaceAt(SVec p)
        {
            int tier = _build?.FloorTier(p) ?? -1;
            if (tier >= 0) return tier <= 1 ? Surface.Wood : tier == 2 ? Surface.Concrete : Surface.Metal;
            for (int i = _map.Patches.Count - 1; i >= 0; i--)
            {
                var pt = _map.Patches[i];
                if (p.X < pt.Min.X || p.X > pt.Max.X || p.Y < pt.Min.Y || p.Y > pt.Max.Y) continue;
                return pt.Tile.Contains("gravel") || pt.Tile.Contains("dirt") ? Surface.Sand : Surface.Concrete;
            }
            for (int i = 0; i < _map.Yards.Count; i++)
            {
                if (i < _map.YardPrefab.Count && _map.YardPrefab[i]) continue;
                var (mn, mx) = _map.Yards[i];
                if (p.X > mn.X + 2 && p.X < mx.X - 2 && p.Y > mn.Y + 2 && p.Y < mx.Y - 2) return Surface.Concrete;
            }
            return TerrainView.BiomeAt(_map, p) switch
            {
                Biome.Tundra => Surface.Snow,
                Biome.Beach or Biome.Arid => Surface.Sand,
                Biome.Swamp => Surface.Mud,
                Biome.Water => Surface.None,
                _ => Surface.Grass,
            };
        }

        readonly List<Debris2D> _debris = new();
        readonly RandomNumberGenerator _fxRng = new();

        /// <summary>Гильза вылетает вбок от ствола, падает и отскакивает.</summary>
        void SpawnCasing(SVec muzzle, SVec dir)
        {
            var d = new Vector2(dir.X, dir.Y);
            var perp = new Vector2(-d.Y, d.X);
            var g = new Debris2D
            {
                Position = Iso.Px(muzzle - dir * 0.45f), Z = Iso.GunH, VZ = _fxRng.RandfRange(70, 110),
                Vel = perp * _fxRng.RandfRange(45, 80) + d * _fxRng.RandfRange(-25, 10), Spin = _fxRng.RandfRange(-25, 25), Life = 2.5f,
            };
            _world.AddChild(g);
            _debris.Add(g);
        }

        /// <summary>Щепки/крошка при ударе по ресурсу: цвет по типу узла, разлетаются от удара.</summary>
        void SpawnChips(int nodeId, SVec nodePos, int actorId)
        {
            var kind = NodeKind.Stone;
            foreach (var n in _cur.Nodes) if (n.Id == nodeId) { kind = n.Kind; break; }
            Color[] cols = kind switch
            {
                NodeKind.Tree => new[] { new Color(0.62f, 0.45f, 0.26f), new Color(0.8f, 0.66f, 0.42f), new Color(0.45f, 0.32f, 0.18f) },
                NodeKind.Metal => new[] { new Color(0.55f, 0.52f, 0.5f), new Color(0.75f, 0.42f, 0.2f), new Color(0.4f, 0.38f, 0.37f) },
                NodeKind.Sulfur => new[] { new Color(0.6f, 0.6f, 0.55f), new Color(0.92f, 0.85f, 0.3f), new Color(0.8f, 0.72f, 0.2f) },
                _ => new[] { new Color(0.6f, 0.59f, 0.56f), new Color(0.42f, 0.41f, 0.4f), new Color(0.75f, 0.74f, 0.7f) },
            };
            SpawnChipsAt(nodePos, cols, actorId, _fxRng.RandiRange(4, 7));
        }

        static readonly Color[][] TierChips =
        {
            new[] { new Color(0.55f, 0.45f, 0.3f), new Color(0.7f, 0.6f, 0.4f) },
            new[] { new Color(0.62f, 0.45f, 0.26f), new Color(0.8f, 0.66f, 0.42f), new Color(0.45f, 0.32f, 0.18f) },
            new[] { new Color(0.6f, 0.59f, 0.56f), new Color(0.42f, 0.41f, 0.4f), new Color(0.75f, 0.74f, 0.7f) },
            new[] { new Color(0.55f, 0.52f, 0.5f), new Color(0.75f, 0.42f, 0.2f), new Color(0.9f, 0.85f, 0.6f) },
            new[] { new Color(0.45f, 0.47f, 0.52f), new Color(0.95f, 0.9f, 0.7f) },
        };

        /// <summary>Разлёт крошки от точки удара в сторону от бьющего.</summary>
        void SpawnChipsAt(SVec nodePos, Color[] cols, int actorId, int count)
        {
            var from = _actorViews.TryGetValue(actorId, out var av) ? av.Position : Iso.Px(nodePos) + new Vector2(0, 10);
            var away = (Iso.Px(nodePos) - from).Normalized();
            for (int i = 0; i < count; i++)
            {
                var dir = (-away).Rotated(_fxRng.RandfRange(-1.2f, 1.2f));
                var g = new Debris2D
                {
                    Position = Iso.Px(nodePos) - away * 6, Z = _fxRng.RandfRange(6, 16), VZ = _fxRng.RandfRange(40, 90),
                    Vel = dir * _fxRng.RandfRange(30, 70), Spin = _fxRng.RandfRange(-15, 15), Life = _fxRng.RandfRange(0.8f, 1.6f),
                    Color = cols[_fxRng.RandiRange(0, cols.Length - 1)], Size = new Vector2(_fxRng.RandiRange(1, 2), _fxRng.RandiRange(1, 2)),
                };
                _world.AddChild(g);
                _debris.Add(g);
            }
        }

        /// <summary>Пустой магазин падает из оружия при перезарядке.</summary>
        void SpawnMag(Vector2 heldPx, float facing)
        {
            float h = Iso.H(1.05f);
            var g = new Debris2D
            {
                Position = heldPx + new Vector2(Mathf.Cos(facing) * 6, h), Z = h - 4, VZ = 15,
                Vel = new Vector2(_fxRng.RandfRange(-12, 12), _fxRng.RandfRange(-4, 8)), Spin = _fxRng.RandfRange(-6, 6),
                Life = 6f, Color = new Color(0.16f, 0.16f, 0.17f), Size = new Vector2(3, 5),
            };
            _world.AddChild(g);
            _debris.Add(g);
        }

        void FloatText(SVec pos, string text, Color c)
        {
            var t = new FloatText2D { Position = Iso.Px(pos), Text = text, Color = c, ZIndex = 5 };
            _world.AddChild(t);
            _texts.Add(t);
        }

        void Flash(SVec pos, Color c, float radius, float life, bool light = false)
        {
            var f = new FlashView2D { Position = Iso.Px(pos), Color = c, Radius = radius, Life = life, Max = life };
            if (light)
            {
                // вспышка выстрела освещает округу — ночью это выдаёт стрелка
                var l = Art.FireLight(7f, 1.8f);
                l.Position = new Vector2(0, -Iso.GunH);
                f.AddChild(l);
            }
            _world.AddChild(f);
            _fx.Add(f);
        }

        void Render(float alpha, float dt)
        {
            var seen = new HashSet<int>();
            foreach (var a in _cur.Actors)
            {
                seen.Add(a.Id);
                if (!_actorViews.TryGetValue(a.Id, out var view))
                {
                    view = new ActorView2D(a.IsNpc, a.Id == _myId && !_spectate);
                    view.MagDropped += SpawnMag;
                    _world.AddChild(view);
                    _actorViews[a.Id] = view;
                }
                view.Visible = !(_spectate && a.Id == _myId);   // наблюдателя в мире нет
                var pos = a.Pos;
                float facing = a.Facing, speed = 0;
                if (_prevActors.TryGetValue(a.Id, out var prev) && SVec.Distance(prev.Pos, a.Pos) < 3)
                {
                    pos = SVec.Lerp(prev.Pos, a.Pos, alpha);
                    facing = Mathf.LerpAngle(prev.Facing, a.Facing, alpha);
                    speed = SVec.Distance(prev.Pos, a.Pos) / World.Dt;
                }
                if (a.Id == _myId)
                {
                    var d = _aim - pos;
                    if (d.LengthSquared() > 0.04f) facing = Mathf.Atan2(d.Y, d.X);
                    _selfPx = Iso.Px(pos);
                }
                view.RecoilRad = Mathf.DegToRad(a.RecoilDeg);
                view.AimDown = a.Id == _myId ? _peek : a.Peek;
                view.SetLabel(a.Label);
                view.Swimming = a.Swimming;
                view.SetState(Iso.Px(pos), facing, a.Dead, a.Reloading, speed, dt, a.HeldId, a.Crouching);
                if (!a.Dead && !a.Swimming) Footstep(a.Id, pos, speed, dt, a.Crouching);
            }
            foreach (var kv in _actorViews)
                if (!seen.Contains(kv.Key)) kv.Value.Visible = false;
            CollectOcclusionFocus();

            // пули
            seen.Clear();
            foreach (var p in _cur.Projectiles)
            {
                seen.Add(p.Id);
                if (!_projViews.TryGetValue(p.Id, out var v))
                {
                    v = new TracerView2D { Dir = new Vector2(p.Dir.X, p.Dir.Y), Arrow = p.Arrow };
                    _world.AddChild(v);
                    _projViews[p.Id] = v;
                }
                var pos = _prevProjs.TryGetValue(p.Id, out var prev) ? SVec.Lerp(prev.Pos, p.Pos, alpha) : p.Pos;
                v.Position = Iso.Px(pos);
            }
            var gone = new List<int>();
            foreach (var kv in _projViews)
                if (!seen.Contains(kv.Key)) { kv.Value.QueueFree(); gone.Add(kv.Key); }
            foreach (var id in gone) _projViews.Remove(id);

            // сумки и ящики
            seen.Clear();
            foreach (var b in _cur.Bags)
            {
                seen.Add(b.Id);
                if (!_bagViews.TryGetValue(b.Id, out var v))
                {
                    v = new BagView2D { IsCrate = b.IsCrate, Safe = b.Safe, Punch = b.Punch, Id = b.Id, Tier = b.Tier, Position = Iso.Px(b.Pos) };
                    _world.AddChild(v);
                    _bagViews[b.Id] = v;
                }
                v.Visible = true;
                if (v.Empty != b.Empty || v.Unlocked != b.Unlocked) { v.Empty = b.Empty; v.Unlocked = b.Unlocked; v.QueueRedraw(); }
            }
            foreach (var kv in _bagViews)
                if (!seen.Contains(kv.Key)) kv.Value.Visible = false;

            RenderWorldObjects(dt);

            for (int i = _texts.Count - 1; i >= 0; i--)
                if (!_texts[i].Tick(dt)) { _texts[i].QueueFree(); _texts.RemoveAt(i); }

            for (int i = _debris.Count - 1; i >= 0; i--)
                if (!_debris[i].Tick(dt)) { _debris[i].QueueFree(); _debris.RemoveAt(i); }
            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                var f = _fx[i];
                f.Life -= dt;
                if (f.Life <= 0) { f.QueueFree(); _fx.RemoveAt(i); continue; }
                f.QueueRedraw();
            }
        }

        /// <summary>
        /// Фокусы прозрачности заслонов (Occlusion): свой персонаж, все персонажи из снапшота и пятно у курсора.
        /// Снапшот содержит только тех, кого игрок честно видит (серверный туман, D32), — спрятанных это не выдаёт.
        /// </summary>
        void CollectOcclusionFocus()
        {
            var half = GetViewport().GetVisibleRect().Size / 2 / _cam.Zoom.X + new Vector2(200, 300);   // запас: высокие спрайты выше кадра
            Occlusion.Begin(new Rect2(_cam.Position - half, half * 2));
            foreach (var a in _cur.Actors)
            {
                if (!_actorViews.TryGetValue(a.Id, out var view) || !view.Visible) continue;
                if (_occlSelfOnly && a.Id != _myId) continue;
                Occlusion.AddBody(view.Position, a.Dead);
            }
            if (!_cur.Self.Dead && !UiShown && !_occlSelfOnly)
            {
                var ground = Iso.Px(_aim);
                Occlusion.AddCursor(ground - new Vector2(0, Iso.GunH), ground);
            }
        }

        /// <summary>Ресурсы, пикапы и постройки из снапшота + призрак установки.</summary>
        void RenderWorldObjects(float dt)
        {
            var head = _selfPx + new Vector2(0, -Iso.H(1.4f));
            var seen = new HashSet<int>();
            foreach (var n in _cur.Nodes)
            {
                seen.Add(n.Id);
                if (!_nodeViews.TryGetValue(n.Id, out var v))
                {
                    v = n.Kind == NodeKind.Tree
                        ? new TreeView2D(Iso.Px(n.Pos), n.Radius, n.Id, TerrainView.BiomeAt(_map, n.Pos))
                        : new OreView2D(Iso.Px(n.Pos), n.Kind, n.Radius, n.Id, TerrainView.BiomeAt(_map, n.Pos));
                    _world.AddChild(v);
                    _nodeViews[n.Id] = v;
                }
                if (Mathf.Abs(v.Fill - n.Fill) > 0.01f) { v.Fill = n.Fill; v.QueueRedraw(); }
                v.Tick(dt);
                // кроны, за которыми игрок, видимый ему персонаж или курсор, становятся прозрачными
                if (v is TreeView2D t)
                    t.UpdateOcclusion(dt);
            }
            RemoveMissing(_nodeViews, seen);

            seen.Clear();
            foreach (var p in _cur.Pickups)
            {
                seen.Add(p.Id);
                if (!_pickupViews.TryGetValue(p.Id, out var v))
                {
                    v = new PickupView2D(p.Kind, TerrainView.BiomeAt(_map, p.Pos)) { Position = Iso.Px(p.Pos) };
                    _world.AddChild(v);
                    _pickupViews[p.Id] = v;
                }
                if (v.Available != p.Available) { v.Available = p.Available; v.QueueRedraw(); }
            }
            RemoveMissing(_pickupViews, seen);

            seen.Clear();
            foreach (var d in _cur.Deployables)
            {
                seen.Add(d.Id);
                if (!_deployViews.TryGetValue(d.Id, out var v))
                {
                    v = new DeployView2D(d.Kind, d.Level) { Position = Iso.Px(d.Pos) };
                    _world.AddChild(v);
                    _deployViews[d.Id] = v;
                }
                if (v.Burning != d.Burning) { v.Burning = d.Burning; v.QueueRedraw(); }
                v.Modulate = d.Seen ? Colors.White : BuildLayer.FogTint;   // не в обзоре — как запомнил
                v.Tick(dt);
            }
            RemoveMissing(_deployViews, seen);

            seen.Clear();
            foreach (var t in _cur.Throwns)
            {
                seen.Add(t.Id);
                if (!_thrownViews.TryGetValue(t.Id, out var tv)) { tv = new ThrownView2D(t.ItemId); _world.AddChild(tv); _thrownViews[t.Id] = tv; }
                tv.Position = Iso.Px(t.Pos);
                tv.Z = t.Z;
                tv.Fuse = t.Fuse;
                tv.Tick(dt);
            }
            RemoveMissing(_thrownViews, seen);

            seen.Clear();
            foreach (var f in _cur.Fires)
            {
                seen.Add(f.Id);
                if (!_fireViews.TryGetValue(f.Id, out var fv)) { fv = new FireView2D(f.Radius, f.Id) { Position = Iso.Px(f.Pos) }; _world.AddChild(fv); _fireViews[f.Id] = fv; }
                fv.Left = f.Left;
                fv.Tick(dt);
            }
            RemoveMissing(_fireViews, seen);

            _mapView.SetState(new SVec(_selfPx.X / Iso.PPM, _selfPx.Y / Iso.PPM), _cur.Actors.Find(x => x.Id == _myId).Facing, _cur.Self.Home);
            _miniMap.Visible = !_mapView.Visible && !_cur.Self.Dead;
            if (_miniMap.Visible && _cur.Actors.Find(x => x.Id == _myId) is var meSnap) _miniMap.SetState(meSnap.Pos, meSnap.Facing, _cur.Self.Home, _cur.Self.Place, _cur.Structures, _cur.Deployables);

            // меню плана и шпаргалка стройки
            _feed.Update(_cur.Self, dt);
            _structTip.Set(_cur.Self.Dead || UiShown || _buildMenu.Visible || _mapView.Visible ? null : StructTip.Pick(_cur.Structures, _aim));
            _buildBar.Visible = _cur.Self.HoldingPlan && !_cur.Self.Dead && !_buildMenu.Visible && !UiShown;
            if (_buildBar.Visible) _buildBar.Set((int)_cur.Self.BuildPiece, (int)_cur.Self.GhostPiece);
            if (_cur.Self.HoldingPlan) _buildMenu.Fill(_cur.Self.BuildPieces, (int)_cur.Self.BuildPiece);
            else if (_buildMenu.Visible) { _buildMenu.Visible = false; Input.MouseMode = Input.MouseModeEnum.Hidden; }
            _guide.Text = _cur.Self.BuildGuide ?? "";
            _guide.Position = new Vector2(GetViewport().GetVisibleRect().Size.X - _guide.GetCombinedMinimumSize().X - 16, MiniMap.Side + 48);

            // окно по E (ящик, печь, столб) и крафт у верстака
            bool showInv = !_cur.Self.Dead && (_invOpen || _cur.Self.Ui != null);
            _inv.Visible = showInv;
            if (showInv) _inv.Refresh(_cur.Self, _cur.Self.Ui, dt);
            if (_inv.Visible != _uiWasOpen)
            {
                _uiWasOpen = _inv.Visible;
                if (!_inv.Visible) _invOpen = false;
                Input.MouseMode = _inv.Visible || _hud.CraftOpen ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Hidden;
            }
            if (_cur.Self.OpenCraft) { _hud.OpenTech(); Input.MouseMode = Input.MouseModeEnum.Visible; }   // E у верстака — дерево изучения

            // вход на РТ — название
            if (_cur.Self.Place != _lastPlace) { _lastPlace = _cur.Self.Place; if (_lastPlace != null) _hud.Toast(_lastPlace); }

            // постройки: виды, тени обзора, прозрачность стен перед персонажем, призрак плана
            _build ??= new BuildLayer(_world);
if (_build.Sync(_cur.Structures, _cur.StructVersion))
            {
                if (_rays != null) { _rays.Dynamic.Clear(); _rays.Dynamic.AddRange(_build.Occluders); }
                _decals.Blocked ??= _build.OnFloor;
                _decals.RefreshBlocked();
            }
            _build.UpdateFade(_selfPx, dt);
            foreach (var w in _mapWalls) w.UpdateOcclusion(dt);
            _prefab.Sync(_cur);
            _prefab.Update(_selfPx, dt);
            if (_bghost == null) { _bghost = new BuildGhost2D(); _world.AddChild(_bghost); }
            _bghost.Visible = _cur.Self.HoldingPlan && !_cur.Self.Dead && !_hud.CraftOpen;
            if (_bghost.Visible) { _bghost.Set(_cur.Self.GhostPiece, _cur.Self.GhostPoly, _cur.Self.GhostA, _cur.Self.GhostB, _cur.Self.BuildOk); _bghost.SetZone(_cur.Self.HasZone, _cur.Self.ZoneCell); }

            // призрак установки
            var self = _cur.Self;
            _ghost.Visible = self.HeldDeploy && !self.Dead && !_hud.CraftOpen;
            if (_ghost.Visible)
            {
                var me = new SVec(_selfPx.X / Iso.PPM, _selfPx.Y / Iso.PPM);
                _ghost.Position = Iso.Px(_aim);
                bool ok = SVec.Distance(me, _aim) <= World.PlaceRange;
                if (_ghost.Ok != ok) { _ghost.Ok = ok; _ghost.QueueRedraw(); }
            }
        }

        static void RemoveMissing<T>(Dictionary<int, T> views, HashSet<int> seen) where T : Node
        {
            List<int> gone = null;
            foreach (var kv in views)
                if (!seen.Contains(kv.Key)) (gone ??= new List<int>()).Add(kv.Key);
            if (gone == null) return;
            foreach (var id in gone) { views[id].QueueFree(); views.Remove(id); }
        }

        /// <summary>Цвет освещения по времени суток: ночь синяя, рассвет и закат тёплые.</summary>
        static Color DayColor(float t)
        {
            var night = new Color(0.2f, 0.24f, 0.4f);
            (float at, Color c)[] keys =
            {
                (0f, night), (0.2f, night), (0.27f, new Color(0.9f, 0.66f, 0.58f)), (0.34f, new Color(0.97f, 0.94f, 0.9f)),
                (0.5f, new Color(1f, 1f, 0.98f)), (0.68f, new Color(1f, 0.95f, 0.88f)), (0.76f, new Color(0.92f, 0.6f, 0.5f)),
                (0.84f, night), (1f, night),
            };
            for (int i = 0; i < keys.Length - 1; i++)
                if (t <= keys[i + 1].at)
                    return keys[i].c.Lerp(keys[i + 1].c, (t - keys[i].at) / (keys[i + 1].at - keys[i].at));
            return night;
        }

        void UpdateCamera(float dt)
        {
            if (_overview)
            {
                // отладка: вся карта в кадре
                var vs = GetViewport().GetVisibleRect().Size;
                float oz = Mathf.Min(vs.X, vs.Y) / (_map.HalfSize * 2 * Iso.PPM);
                _cam.Position = Vector2.Zero;
                _cam.Zoom = new Vector2(oz, oz);
                return;
            }
            // смещение зависит от положения курсора на экране (не в мире), поэтому камера не «убегает»
            // сдвиг — доля половины экрана, чтобы персонаж всегда оставался в кадре (не ближе 20% к краю)
            var halfWorld = GetViewport().GetVisibleRect().Size / 2 / _cam.Zoom.X;
            var target = _selfPx + _screenDir * halfWorld * (0.35f * _peek) + new Vector2(0, -Iso.H(1f));
            _cam.Position = _frame <= 2 ? target : _cam.Position.Lerp(target, 1 - Mathf.Exp(-8 * dt));
            _shake = Mathf.MoveToward(_shake, 0, dt * 6f);
            if (_shake > 0.01f) _cam.Offset = new Vector2(_fxRng.RandfRange(-1, 1), _fxRng.RandfRange(-1, 1)) * 2.5f * _shake;
            else _cam.Offset = Vector2.Zero;
            float zoomTarget = _zoom * (1f - 0.1f * _peek);   // в прицеливании камера чуть отдаляется
            var z = _cam.Zoom.X + (zoomTarget - _cam.Zoom.X) * (1 - Mathf.Exp(-10 * dt));
            _cam.Zoom = new Vector2(z, z);
        }

        void UpdateHud(float dt)
        {
            var s = _cur.Self;
            float day = _forcedDay ?? _cur.DayTime;
            int mins = (int)(day * 24 * 60);
            string clock = $"{mins / 60:00}:{mins % 60:00}" + (World.NightFactorAt(day) > 0.5f ? " · ночь, обзор короче" : "");
            _hud.UpdateHud(s, dt, _peek > 0.05f
                ? $"{clock} · прицеливание: обзор уже, видно дальше, разброс меньше"
                : $"{clock} · ПКМ — прицелиться");

            var canvas = GetViewport().GetCanvasTransform();
            float zoom = _cam.Zoom.X;
            // обзор «вилкой» из ног персонажа; за стенами и стволами — серо (лучи как на сервере)
            var feetPx = canvas * _selfPx;
            var selfM = new SVec(_selfPx.X / Iso.PPM, _selfPx.Y / Iso.PPM);
            var dir = new Vector2(_aim.X - selfM.X, _aim.Y - selfM.Y);
            float range = (50 + 30 * _peek) * (1f - 0.4f * World.NightFactorAt(_forcedDay ?? _cur.DayTime));
            if (_rays == null) { _rays = new VisionRays(_map); if (_build != null) _rays.Dynamic.AddRange(_build.Occluders); }
            _rays.Update(selfM, range, _cur.Nodes);
            VisionFx.Update(_selfPx, dir.LengthSquared() > 0.01f ? dir.Normalized() : Vector2.Right,
                            Mathf.Cos(Mathf.DegToRad((130 - 70 * _peek) * 0.5f)), range, _rays.Texture, !s.Dead && !_spectate);   // наблюдатель видит всё

            // прицел: круг = реальный разброс на дистанции до курсора
            var mouse = _autoAim.HasValue ? canvas * (Iso.Px(_aim) - new Vector2(0, Iso.GunH)) : GetViewport().GetMousePosition();
            _hud.Crosshair.Position = mouse;
            float dist = dir.Length();
            _hud.Crosshair.RadiusPx = Mathf.Tan(Mathf.DegToRad(s.SpreadDeg * 0.5f)) * dist * Iso.PPM * zoom;
        }
    }
}
