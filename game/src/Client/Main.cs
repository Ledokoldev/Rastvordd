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
        BuildMenu _buildMenu, _hammerMenu;   // колёса Плана и киянки (ПКМ)
        BuildGhost2D _hammerMark;            // подсветка постройки, над которой открыто колесо киянки
        BuildGhost2D _doorMark;              // мягкая подсветка двери, которую откроет E
        InteractMark _useMark;               // мягкая рамка остальной цели E (верстак, ящик, печь…)
        int _hammerFocus, _hammerOp, _hammerTarget;
        int _hammerMode = 1;                 // режим киянки (колёсико): индекс в HammerModes
        static readonly int[] HammerModes = { 1, 2, 3, 4, World.HammerDemolish };   // дерево, камень, металл, сталь, снос
        static string HammerModeName(int m) => m == World.HammerDemolish ? "снос" : Building.TierNames[m];
        BuildBar _buildBar;
        StructTip _structTip;
        PickupFeed _feed;
        BuildGuidePanel _guide;
        int _setPiece = -1;
        // наблюдатель (песочница ботов)
        bool _spectate;
        int _followId, _timeScale = 1;
        bool _autoCam;       // наблюдатель: камера сама переезжает к самой горячей точке (--autocam, C — вкл/выкл)
        float _camTimer;
        float _effSpeed = 1;   // сколько секунд мира проходит за секунду на деле (песочница может не успевать)
        float _specRefresh;
        SpectatorPanel _specPanel;
        string _researchReq;      // изучить узел дерева (id)
        (UiOp op, string item, int index)? _uiReq;
        readonly Queue<(UiOp op, string item, int index)> _uiQ = new();   // ховер-лут за кадр проходит несколько ячеек - действия идут по очереди, по одному за такт
        bool UiShown => _inv.Visible;
        bool _uiWasOpen, _modalWas;

        bool AnyModal => _inv.Visible || _hud.CraftOpen || _hud.AdminOpen || _hud.CodeOpen;
        /// <summary>Курсор: виден, пока открыто любое окно; меняется только на переходе (Esc-«свободная мышь» не затирается).</summary>
        void SyncMouse()
        {
            bool m = AnyModal;
            if (m == _modalWas) return;
            _modalWas = m;
            Input.MouseMode = m ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Hidden;
        }
        /// <summary>Закрыть инвентарь/контейнер (и сказать серверу).</summary>
        void CloseInv() { _invOpen = false; _uiQ.Clear(); if (_cur?.Self?.Ui != null) _uiReq = (UiOp.Close, null, 0); }
        /// <summary>Окна на весь экран взаимоисключающи: открывая одно, закрываем остальные.</summary>
        void CloseBigExcept(bool inv, bool craft, bool map)
        {
            if (!inv) CloseInv();
            if (!craft && _hud.CraftOpen) _hud.CloseCraftAll();
            if (!map) _mapView.Visible = false;
        }
        string _lastPlace;
        CanvasModulate _daylight;
        Ambient _ambient;
        bool _overview, _perf, _timeEdge, _forceCrouch, _forceSprint;
        SVec _forceWalk;
        SVec _autoRun;       // «=» — закрепить текущее направление бега до следующего нажатия WASD
        bool _autoSprint;
        int _pressNAt = -1;
        double _perfTime, _simTime;
        int _perfFrames;
        // --perf: рывки — по секундам худший кадр (реальное время между кадрами) и из чего он состоял
        ulong _hitchLast, _hitchStart, _procT0, _procEnd, _drawUs, _renderUs, _preUs;
        double _hitchWorst, _hitchAllWorst, _decalMs;
        int _hitchFrames, _hitchBad, _hitchSec, _gc0, _gc2;
        string _hitchWhat = "";
        float? _forcedDay;
        int _myId;
        Snapshot _cur;
        readonly Dictionary<int, ActorSnap> _prevActors = new();
        readonly Dictionary<int, ProjSnap> _prevProjs = new();
        double _acc;

        Node2D _world;
        Camera2D _cam;
        LoadingScreen _loading;
        double _loadDoneMs = -1;   // короткая загрузка после _Ready: декор вокруг, текстуры, прогрев шейдеров
        /// <summary>Стены и пропсы карты (РТ) — для прозрачности заслонов.</summary>
        readonly List<WallView2D> _mapWalls = new();
        PrefabLayer _prefab;
        LightingLayer _lighting;
        DirectorLayer _dirLayer;
        readonly System.Collections.Generic.List<(SVec pos, float t)> _flares = new();   // сигнальные ракеты «Маяка» (DirectorLayer)
        RadioFeed _radio;
        /// <summary>Отладка (--occl-off): прозрачность только над своим персонажем — как было до D39, для сравнения.</summary>
        bool _occlSelfOnly;
        float _zoom = 2.0f;
        Hud _hud;

        readonly Dictionary<int, ActorView2D> _actorViews = new();
        readonly Dictionary<int, TracerView2D> _projViews = new();
        readonly Dictionary<int, BagView2D> _bagViews = new();
        readonly Dictionary<Node, float> _hiddenFor = new();

        /// <summary>
        /// Кого нет в снапшоте — спрятать, а через 20 с совсем удалить: иначе в песочнице с десятками ботов копились
        /// сотни спрятанных персонажей (у каждого свой SubViewport, перерисовка каждый кадр) — утечка памяти.
        /// </summary>
        void ReapViews<T>(Dictionary<int, T> views, HashSet<int> seen, float dt) where T : Node2D
        {
            List<int> dead = null;
            foreach (var (id, v) in views)
            {
                if (seen.Contains(id)) { _hiddenFor.Remove(v); continue; }
                v.Visible = false;
                float t = (_hiddenFor.TryGetValue(v, out var h) ? h : 0) + dt;
                _hiddenFor[v] = t;
                if (t > 20) (dead ??= new()).Add(id);
            }
            if (dead == null) return;
            foreach (var id in dead) { var v = views[id]; _hiddenFor.Remove(v); v.QueueFree(); views.Remove(id); }
        }
        readonly Dictionary<int, NodeView2D> _nodeViews = new();
        readonly Dictionary<int, PickupView2D> _pickupViews = new();
        readonly Dictionary<int, DeployView2D> _deployViews = new();
        readonly Dictionary<int, ThrownView2D> _thrownViews = new();
        readonly Dictionary<int, FireView2D> _fireViews = new();
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
        bool _scriptCodeUi;
        int _scriptCode = -1;   // отладка: код, который сценарий «вводит» у запертой двери

        void RunScript()
        {
            if (!_script.TryGetValue(_frame, out var actions)) return;
            foreach (var a in actions)
            {
                if (a.StartsWith("sb:")) SandboxScript(a[3..]);   // полигон (SandboxSound.cs)
                else if (a == "kit") _debugEdge = true;
                else if (a == "interact") _interactEdge = true;
                else if (a == "eat") _eatEdge = true;
                else if (a == "reload") _reloadEdge = true;
                else if (a == "fire") _autoFireFrames = 4;
                else if (a == "hold") _autoFireFrames = 100000;
                else if (a == "release") _autoFireFrames = 0;
                else if (a == "craftmenu") _hud.ToggleCraft();
                else if (a == "tech") _hud.OpenTech();
                else if (a.StartsWith("wb")) _hud.WorkbenchDebug(a[2..]);   // отладка: wb<вкладка 1–4>[f<верстак рядом>][d — демо-избранное]
                else if (a.StartsWith("scroll") && int.TryParse(a.Substring(6), out var sy)) ScrollAll(this, sy);
                else if (a == "drawdump") DrawDump();
                else if (a == "cullfar") { int n = 0; foreach (var ch in _world.GetChildren()) if (ch is Node2D c && c.Position.DistanceTo(_cam.Position) > 60 * Iso.PPM) { c.Visible = false; n++; } GD.Print($"cullfar: скрыто {n}"); }   // отладка: замер — что будет, если не обрабатывать далёкое
                else if (a.StartsWith("hide")) { var tn = a.Substring(4); void H(Node n) { if (n.GetType().Name == tn && n is CanvasItem c) c.Visible = false; foreach (var ch in n.GetChildren()) H(ch); } H(GetTree().Root); }   // отладка: скрыть все узлы типа (замер, кто ест кадр)
                else if (a == "nolights") { void Off(Node n) { if (n is Light2D l) l.Enabled = false; foreach (var ch in n.GetChildren()) Off(ch); } Off(GetTree().Root); }   // отладка: выключить весь 2D-свет   // отладка: кто рисуется в кадре (по типам, с учётом вложенности)   // отладка: прокрутить все видимые списки (снимки длинных меню)
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
                else if (a.StartsWith("bpick")) _buildMenu.PointAt(int.Parse(a.Substring(5)));   // отладка: навести на сектор колеса (с 0)
                else if (a == "hammer") { var me = _server.World.Find(_myId); me.Belt[5] = Items.NewSlot("tool_hammer"); me.Current = 5; }   // отладка: киянку в руки
                else if (a == "hmenu") OpenHammerWheel();     // отладка: ПКМ с киянкой
                else if (a.StartsWith("aimst"))
                {
                    // отладка: aimst<вид> — встать у своей постройки этого вида (стена — 1) и навести на неё
                    var w = _server.World; var me = w.Find(_myId);
                    var kind = (StructKind)int.Parse(a.Substring(5));
                    Structure best = null;
                    foreach (var st in w.Structures) if (st.Kind == kind && st.OwnerId == _myId && (best == null || SVec.Distance(st.Center, me.Pos) < SVec.Distance(best.Center, me.Pos))) best = st;
                    if (best != null) { me.Pos = best.Center + (best.IsFloor ? new SVec(-1.6f, 0.4f) : new SVec(0.2f, 1.1f)); _autoAim = best.Center; }
                }
                else if (a == "hdone") _hammerMenu.Release();
                else if (a.StartsWith("snowc")) _server.World.SnowCover = float.Parse(a.Substring(5), System.Globalization.CultureInfo.InvariantCulture);   // отладка: сколько снега лежит
                else if (a.StartsWith("wetc")) _server.World.Wetness = float.Parse(a.Substring(4), System.Globalization.CultureInfo.InvariantCulture);
                else if (a.StartsWith("sky")) _server.World.SetSky((Rastvordd.Sim.Sky)int.Parse(a.Substring(3)));   // отладка: погода 0–7
                else if (a.StartsWith("sbsub")) SbSetSub(int.Parse(a.Substring(5)));   // отладка: полигон — мишени/боты/дуэль
                else if (a.StartsWith("hmode")) _hammerMode = int.Parse(a.Substring(5));   // отладка: режим киянки (1–5, 9 — снос)
                else if (a.StartsWith("hpick")) _hammerMenu.PointAt(int.Parse(a.Substring(5)));
                else if (a.StartsWith("dose")) _server.World.Find(_myId).RadDose = float.Parse(a.Substring(4));   // отладка (D47): dose<n> — доза радиации
                else if (a.StartsWith("dress:"))
                {
                    // отладка (D47): dress:army+wear_ozk_gp5+plate_steel — надеть комплект(ы) и вещи по id; dress:none — раздеть
                    var w = _server.World; var me = w.Find(_myId);
                    foreach (var k in a.Substring(6).Split('+'))
                    {
                        if (k == "none") { System.Array.Clear(me.Worn); System.Array.Clear(me.Plates); continue; }
                        var set = Clothing.Sets.Find(x => x.Key == k);
                        w.Dress(me, set != null ? set.Items.ToArray() : new[] { k });
                    }
                }
                else if (a.StartsWith("give")) { var g = a.Substring(4).Split('x'); _server.World.Find(_myId).Add(g[0], g.Length > 1 ? int.Parse(g[1]) : 1); }   // отладка: give<id>x<n>
                else if (a.StartsWith("open:")) { _server.World.DebugOpenUi(_server.World.Find(_myId), a[5..]); _invOpen = true; }   // отладка: окна для скриншотов
                else if (a.StartsWith("ui")) _uiReq = ((UiOp)int.Parse(a.Substring(2).Split('_')[0]), a.Contains('_') ? a.Substring(a.IndexOf('_') + 1).Replace('~', '#').Replace('^', '>') : null, 0);   // отладка: ui<op>_<арг> (# → ~, > → ^)
                else if (a == "inv") _invOpen = !_invOpen;
                else if (a.StartsWith("key+:") || a.StartsWith("key-:"))   // отладка: key+:W / key-:W — нажать/отпустить клавишу
                    Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = OS.FindKeycodeFromString(a[5..]), Keycode = OS.FindKeycodeFromString(a[5..]), Pressed = a[3] == '+' });
                else if (a.StartsWith("click:"))   // отладка: click:x;y — нажать и отпустить ЛКМ в точке экрана
                {
                    var xy = a[6..].Split(';'); var at = new Vector2(float.Parse(xy[0]), float.Parse(xy[1]));
                    GetViewport().WarpMouse(at);
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
                }
                else if (a.StartsWith("invhover")) _inv.DebugHover(a.Substring(8));   // отладка: invhover<inv>~<i> - навести курсор на ячейку (подсказка)
                else if (a.StartsWith("invsel")) _inv.DebugSelect(a.Substring(6));   // отладка: invsel<pack|belt>~<i> — карточка этой ячейки
                else if (a == "admin") _hud.ToggleAdmin();
                else if (a.StartsWith("drop")) { var w0 = _server.World; (w0.Director ??= new Director(w0.Seed)).DebugAirdrop(w0, w0.Find(_myId).Pos + new SVec(3, 5), a.Length > 4 ? float.Parse(a[4..]) : 30); }   // отладка: сброс груза рядом
                else if (a.StartsWith("vfx")) DebugVfx(a[3..]);   // отладка эффектов: vfxall, vfxflare, vfxboom, vfxfire…
                else if (a == "code") _hud.ShowCode(0);
                else if (a == "codeui") _scriptCodeUi = true;   // отладка: у замка открывать окно, а не вводить --code сразу
                else if (a == "die") _server.World.DebugKill(_server.World.Find(_myId));
                else if (a == "steps") GD.Print("steps: " + string.Join(", ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(Sfx.Played, kv => kv.Key.StartsWith("steps/")), kv => kv.Key[6..] + "×" + kv.Value)));   // отладка: какие шаги звучали
                else if (a == "info") GD.Print($"info: {_server.World.LastInfo.GetValueOrDefault(_myId)} | structs {_server.World.Structures.Count} | aim {_autoAim} | me {_server.World.Find(_myId).Pos}" + string.Concat(_server.World.Tier2.ConvertAll(r => $" | тир2 {r.Key}: ток {r.PowerLeft:0} перезарядка {r.CooldownLeft:0} кодов {r.Codes.Count}")) + (_server.World.Substations.Count > 0 ? $" | тир1: {string.Join(", ", _server.World.Tier1.ConvertAll(r => $"{r.Key} готов={r.Ready} кабели={r.CablesIn} вагон={r.WagonT:0.00} кран={(r.Cabin != null ? r.Cabin.Progress : 0):0} крейт={(r.Crate.Unlocked ? "открыт" : "закрыт")}@{r.Crate.Pos.X:0};{r.Crate.Pos.Y:0}"))} | подстанция: питание {_server.World.Substations[0].Powered}, крейт открыт {_server.World.Substations[0].Crate.Unlocked}, таймер {_server.World.Substations[0].CrateTimer:0}" : ""));   // отладка: последнее сообщение
                else if (a == "map") { _mapView.Visible = !_mapView.Visible; _mapView.QueueRedraw(); }
                else if (a == "crouch") _forceCrouch = true;
                else if (a == "sprint") _forceSprint = true;
                else if (a == "walk") _forceSprint = false;
                else if (a == "gait") { if (_gaitLog) GaitReport(); _gaitLog = true; _gaitTimes.Clear(); }   // отладка: темп шагов
                else if (a == "save") SaveWorld();
                else if (a == "stand") _forceCrouch = false;
                else if (a.StartsWith("code")) _scriptCode = int.Parse(a.Substring(4));
                else if (a.StartsWith("go"))
                {
                    var p = a.Substring(2).Split(';');
                    _forceWalk = new SVec(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (a.StartsWith("tpb") || a.StartsWith("tpe"))
                {
                    // отладка: перенестись в глубину биома (tpbForest, tpbGrassland…): точка, где вокруг 12 м тот же биом;
                    // tpe<Биом> — на его край (опушку): в 6 м от точки начинается другой биом
                    bool edge = a.StartsWith("tpe");
                    var want = System.Enum.Parse<Biome>(a.Substring(3));
                    var me = _server.World.Find(_myId);
                    var rng = new System.Random(int.TryParse(_scriptCode.ToString(), out var sd) ? sd : 1);
                    for (int t = 0; t < 20000; t++)
                    {
                        var q = new SVec(rng.Next(-400, 400), rng.Next(-400, 400));
                        bool ok = true;
                        if (edge) { ok = false; for (int k = 0; k < 8 && !ok; k++) ok = TerrainView.BiomeAt(_map, q + new SVec(Mathf.Cos(k * 0.785f) * 6, Mathf.Sin(k * 0.785f) * 6)) == Biome.Grassland; }
                        else for (int k = 0; k < 8 && ok; k++) ok = TerrainView.BiomeAt(_map, q + new SVec(Mathf.Cos(k * 0.785f) * 12, Mathf.Sin(k * 0.785f) * 12)) == want;
                        if (ok && TerrainView.BiomeAt(_map, q) == want) { me.Pos = q; break; }
                    }
                }
                else if (a.StartsWith("t2tp"))
                {
                    // отладка: перенестись на РТ тира 2: t2tp<номер>;x;y от её угла
                    var p = a.Substring(4).Split(';');
                    _server.World.Find(_myId).Pos = _server.World.Tier2[int.Parse(p[0])].At(float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (a == "t2power") { foreach (var r in _server.World.Tier2) r.PowerLeft = Rastvordd.Sim.Tier2Rt.PowerTime; }   // отладка: ток на Радиоцентре без дизеля
                else if (a == "t2call")
                {
                    // отладка: вызвать борт на Радиоцентр без шагов (проверка сброса на площадку)
                    foreach (var r in _server.World.Tier2) _server.World.DebugCallRadio(r, _server.World.Find(_myId));
                }
                else if (a.StartsWith("t1tp"))
                {
                    // отладка: перенестись на РТ тира 1: t1tp<номер>;x;y от её угла
                    var p = a.Substring(4).Split(';');
                    _server.World.Find(_myId).Pos = _server.World.Tier1[int.Parse(p[0])].At(float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture));
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

        public override void _EnterTree()
        {
            // отладочные скриншоты (--shot/--frames): окно за краем экрана и без фокуса — не мешает пользователю
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--shot=") || arg.StartsWith("--frames="))
                {
                    var w = GetWindow();
                    w.Unfocusable = true;
                    w.Position = new Vector2I(-32000, -32000);
                    break;
                }
        }

        public override void _Ready()
        {
            ulong readyT0 = Time.GetTicksUsec();
            DisplayServer.WindowSetTitle("Раствордд — прототип 0 (2D 3/4)");
            // отладка: --seed=N — другой мир для прогонов ботов (обычная игра — 1337; сохранение держит свой)
            var seedArg = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--seed="));
            _server = new LocalServer(seedArg != null && int.TryParse(seedArg.Substring(7), out int seedN) ? seedN : 1337);
            // сохранение мира игрока — только при обычном запуске: любой отладочный флаг (прогоны ботов, скриншоты,
            // наблюдатель) работает со свежим миром и НЕ трогает сохранение (иначе тест перезапишет мир игрока)
            _saveEnabled = OS.GetCmdlineUserArgs().All(a => a.StartsWith("--bots="));   // своя игра (в т. ч. с --bots=N) сохраняется, отладочные запуски — нет
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
            _autoCam = _spectate && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--autocam") >= 0;
            var spd = System.Array.Find(OS.GetCmdlineUserArgs(), x => x.StartsWith("--speed="));
            if (spd != null && int.TryParse(spd[8..], out int ts)) _timeScale = System.Math.Clamp(ts, 1, 60);   // скорость мира с самого старта
            _myId = _spectate ? _server.ConnectSpectator() : _server.Connect("Игрок");
            if (!_spectate && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--rtkit") >= 0 && _server.World.Find(_myId) is Rastvordd.Sim.Actor kit)
            {
                // отладка: набор для пробежки по РТ — АКМ и наган с патронами, перфокарты I/II, дизель для «Маяка», хил, карты доступа
                kit.AddSlot(Rastvordd.Sim.Items.NewSlot("weapon_akm", 30));
                kit.AddSlot(Rastvordd.Sim.Items.NewSlot("weapon_nagant", 7));
                kit.Add("ammo_762", 240); kit.Add("ammo_9mm", 80);
                kit.Add("punchcard_1", 4); kit.Add("punchcard_2", 2); kit.Add("keycard", 3);
                kit.AddSlot(Rastvordd.Sim.Liquids.Make("canister_20", "diesel", 20));
                kit.Add("medkit", 4); kit.Add("syringe", 4); kit.Add("bandage", 6);
                kit.Add("expl_satchel", 4);
            }
            // летопись вайпа — всегда: обычная игра и песочница пишут в user://chronicle, отладка — куда скажут (--chronicle=путь)
            try
            {
                var custom = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--chronicle="));
                bool debugRun = !_saveEnabled && !_spectate;
                if (custom != null || !debugRun)
                    _server.StartChronicle(custom?.Substring(12) ?? ProjectSettings.GlobalizePath($"user://chronicle/{(_spectate ? "sandbox" : "wipe")}_{System.DateTime.Now:yyyy-MM-dd_HH-mm}.jsonl"));
            }
            catch (System.Exception ex) { GD.PrintErr($"летопись не начата: {ex.Message}"); }
            // отладка: --recipedump=путь — все рецепты (верстак|станция|выход|цена|изучение) в файл и выход
            var rd = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--recipedump="));
            if (rd != null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var r in Rastvordd.Sim.Recipes.All)
                    sb.AppendLine($"{r.Bench}|{r.Station}|{Rastvordd.Sim.Items.Name(r.Output)}{(r.Count > 1 ? " ×" + r.Count : "")}|{string.Join(", ", System.Linq.Enumerable.Select(r.Cost, c => $"{Rastvordd.Sim.Items.Name(c.id)} {c.count}"))}|{(Rastvordd.Sim.Tech.Get(r.Output) != null ? "изуч" : "")}");
                System.IO.File.WriteAllText(rd.Substring(13), sb.ToString());
                GetTree().Quit();
                return;
            }
            // отладка: --progdump=путь — вся прогрессия и экономика (рецепты, дерево, лут по источникам, переработка, добыча) в JSON и выход
            var pd = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--progdump="));
            if (pd != null)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(pd.Substring(11))));
                System.IO.File.WriteAllText(pd.Substring(11), Rastvordd.Sim.ProgDump.Build(_server.World), new System.Text.UTF8Encoding(false));
                GetTree().Quit();
                return;
            }
            // отладка: --simtest=600[,бот] — прокрутить 600 с мира без отрисовки и встать рядом с ботом
            var st = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--simtest="));
            if (st != null)
            {
                var p = st.Substring(10).Split(',');
                var rk = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--raidkit="));
                if (rk != null) _server.RaidKitAt = float.Parse(rk.Substring(10), System.Globalization.CultureInfo.InvariantCulture);
                var gk = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--gunkit="));   // D54: набор на оружейный стол боту
                if (gk != null) _server.GunKitAt = float.Parse(gk.Substring(9), System.Globalization.CultureInfo.InvariantCulture);
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
            SandboxSetup();   // полигон (--sandbox=aim|sound|build): площадка и укрытия — до того, как клиент возьмёт карту

            VisionFx.Register();
            _map = _server.GetMap();
            BuildGround(_map);
            _world = new Node2D { YSortEnabled = true };
            AddChild(_world);
            BuildMap(_map);
            var flatDecals = new Node2D { ZIndex = -9 };
            AddChild(flatDecals);
            _decals = new DecalLayer(_map, flatDecals, _world);
            Vfx.Init(_world, flatDecals);
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
            if (_teleports != null) _hud.SetTeleports(_teleports);
            // админка: надеть любой комплект (снятое — в рюкзак), антирадин
            var gear = new List<(string, System.Action)>();
            foreach (var set in Clothing.Sets)
            {
                if (set.Key == "watch") continue;
                var s = set;
                gear.Add(($"{s.Name} ({s.Tier})", () =>
                {
                    var me = _server.World.Find(_myId);
                    for (int i = 0; i < me.Worn.Length; i++)
                        if (me.Worn[i] != null && me.Worn[i].Id != "wear_watch") { me.Add(me.Worn[i].Id, 1); me.Worn[i] = null; }
                    _server.World.Dress(me, s.Items.ToArray());
                    _hud.Toast($"Надет комплект «{s.Name}»");
                }));
            }
            gear.Add(("Антирадин ×5", () => { _server.World.Find(_myId).Add("antirad", 5); _hud.Toast("Антирадин ×5"); }));
            _hud.SetGear(gear);
            var monList = new List<(string, SVec, SVec)>();
            for (int i = 0; i < _map.Yards.Count; i++) monList.Add((_map.YardNames[i], _map.Yards[i].min, _map.Yards[i].max));
            _mapView = new MapView(_map, monList);
            _mapView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _hud.AddChild(_mapView);
            _miniMap = new MiniMap(_mapView);
            _miniMap.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            _miniMap.OffsetLeft = -MiniMap.Side - 16; _miniMap.OffsetRight = -16; _miniMap.OffsetTop = 14; _miniMap.OffsetBottom = 14 + MiniMap.Side + 34;
            _hud.AddChild(_miniMap);
            // счётчик кадров (F10 — скрыть/показать): FPS, худший кадр за полсекунды, вызовы отрисовки
            _fpsLabel = new Label { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -MiniMap.Side - 16 - 60, OffsetRight = -16, OffsetTop = 14 + MiniMap.Side + 40, HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = Control.MouseFilterEnum.Ignore };
            _fpsLabel.AddThemeFontSizeOverride("font_size", 13);
            _fpsLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            _fpsLabel.AddThemeConstantOverride("outline_size", 4);
            _hud.AddChild(_fpsLabel);
            _inv = new InventoryWindow();
            _inv.Op += (op, arg) => _uiQ.Enqueue((op, arg, 0));
            _inv.ClickedOutside += () => { _invOpen = false; _uiQ.Clear(); if (_cur?.Self.Ui != null) _uiReq = (UiOp.Close, null, 0); };
            _hud.AddChild(_inv);
            _buildMenu = new BuildMenu();
            _buildMenu.Picked += k => _setPiece = k;
            _hud.AddChild(_buildMenu);
            _hammerMenu = new BuildMenu();
            _hammerMenu.Picked += op => { _hammerOp = op; _hammerTarget = _hammerFocus; };
            _hammerMenu.Rejected += why => _hud.Toast(why);
            _hud.AddChild(_hammerMenu);
            _buildBar = new BuildBar { Visible = false };
            _buildBar.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
            _buildBar.OffsetLeft = -_buildBar.CustomMinimumSize.X / 2; _buildBar.OffsetRight = _buildBar.CustomMinimumSize.X / 2;
            _buildBar.OffsetTop = -300; _buildBar.OffsetBottom = -234;   // над подсказкой действия
            _hud.AddChild(_buildBar);
            _structTip = new StructTip();
            _hud.AddChild(_structTip);
            _feed = new PickupFeed();
            // лента подборов — справа от пояса, низом по низу ячеек (пояс ровно по центру, см. Hotbar)
            _feed.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
            _feed.OffsetLeft = UiTheme.Layout == 3 ? -_feed.CustomMinimumSize.X - 20 : Hotbar.BeltWidth / 2 + Hotbar.Gap; _feed.OffsetRight = _feed.OffsetLeft + _feed.CustomMinimumSize.X;
            _feed.OffsetBottom = -28; _feed.OffsetTop = -28 - _feed.CustomMinimumSize.Y;
            _hud.AddChild(_feed);
            _guide = new BuildGuidePanel();
            _hud.AddChild(_guide);
            _hud.CraftRequested += (i, n) => { _craftPendIdx = i; _craftPendLeft = n; };
            _hud.ResearchRequested += id => _researchReq = id;
            _hud.CodeEntered += (door, code) => _codeReq = (door, code);
            _hud.MenuClosed += SyncMouse;
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
            SandboxReady();
            StartLoading();
            if (_perf)
            {
                // где кадр: _Draw узлов (между концом _Process и началом отрисовки) или сам рендер (шейдеры, текстуры)
                RenderingServer.FramePreDraw += () => { _preUs = Time.GetTicksUsec(); _drawUs = _preUs - _procEnd; };
                RenderingServer.FramePostDraw += () => _renderUs = Time.GetTicksUsec() - _preUs;
            }
            if (_perf) GD.Print(_server.World.TreeStats());
            if (_perf) GD.Print($"hitch: загрузка сцены (_Ready) {(Time.GetTicksUsec() - readyT0) / 1000.0:F0} мс");
        }

        void ParseArgs()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--shot=")) _shotPath = arg.Substring(7);
                else if (arg.StartsWith("--sandbox=")) _sbMode = arg[10..];   // полигон: aim, sound, build (SandboxMode.cs)
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
                else if (arg.StartsWith("--att1="))
                {
                    // отладка: встать на РТ тира 1 номер N в точке (x, y) от её угла: --att1=0,40,30
                    var p = arg.Substring(7).Split(',');
                    var rt = _server.World.Tier1[int.Parse(p[0])];
                    _server.World.Find(_myId).Pos = rt.At(float.Parse(p[1], inv), float.Parse(p[2], inv));
                }
                else if (arg.StartsWith("--att2="))
                {
                    // отладка: встать на РТ тира 2 номер N в точке (x, y) от её угла: --att2=0,92,80 (Радиоцентр: площадка ~92,71)
                    var p = arg.Substring(7).Split(',');
                    var rt = _server.World.Tier2[int.Parse(p[0])];
                    _server.World.Find(_myId).Pos = rt.At(float.Parse(p[1], inv), float.Parse(p[2], inv));
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
                else if (arg.StartsWith("--gridshow"))
                {
                    string mode = arg.Contains('=') ? arg.Split('=')[1] : "half";
                    // отладка сетки клеток D52: база со всеми размерами установленного и призрак верстака со слотами
                    var w = _server.World;
                    var me = w.Find(_myId);
                    for (int r = 0; r < 200; r += 2)
                    {
                        bool found = false;
                        for (int k = 0; k < 16 && !found; k++)
                        {
                            var c = new SVec(60, 60) + new SVec(Mathf.Cos(k * 0.39f), Mathf.Sin(k * 0.39f)) * r;
                            var (ci, cj) = Building.CellOf(c);
                            bool ok = w.CanPlaceClaim(Building.CellCenter(ci, cj), out _);
                            for (int di = -2; di <= 4 && ok; di++) for (int dj = -2; dj <= 4 && ok; dj++) ok = w.FoundationGeoOk(ci + di, cj + dj, out _);
                            if (ok) { _autoAim = w.DebugGridDemo(me, ci, cj, mode); found = true; }
                        }
                        if (found) break;
                    }
                    me.Belt[0] = Items.NewSlot(mode switch { "tri" or "door" => "deploy_box", "lathe" => "deploy_lathe", _ => "deploy_workbench" });
                    me.Current = 0;
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
                else if (arg == "--dressreport") foreach (var line in _server.World.DressReport) GD.Print("DRESS " + line);   // обстановка РТ: плотность до/после
                else if (arg == "--overview") _overview = true;
                else if (arg == "--perf") _perf = true;
                else if (arg == "--nocover") DecalLayer.CoverOn = false;   // без густой травы (замер)
                else if (arg == "--hero") ActorView2D.HeroSprites = true;
                else if (arg.StartsWith("--rigset=")) ActorView2D.RigName = arg.Substring(9);   // игрок из своего набора частей рига   // пробный нарисованный персонаж вместо рига
                else if (arg == "--nohrtf") Tuning.SteamHrtf = false;   // отладка: звук без Steam Audio (сравнить память)
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
                        var kv = step.Split(':', 2);
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

        CanvasLayer _rigTuneLayer;
        /// <summary>F6 (Ctrl+6): окно ручной настройки рига игрока (набор --rigset или «naked»).</summary>
        void ToggleRigTune()
        {
            if (_rigTuneLayer != null) { _rigTuneLayer.QueueFree(); _rigTuneLayer = null; return; }
            _rigTuneLayer = new CanvasLayer { Layer = 50 };
            _rigTuneLayer.AddChild(new RigTuneWindow(ActorView2D.RigName ?? "naked"));
            AddChild(_rigTuneLayer);
        }

        System.Collections.Generic.List<(string, System.Action)> _teleports;

        void BuildMap(MapInfo map)
        {
            _prefab = new PrefabLayer(map, _world, this);
            {
                var tps = new System.Collections.Generic.List<(string, System.Action)>();
                foreach (var m in _server.World.Monuments)
                {
                    var mon = m;
                    tps.Add((mon.Name, () => { var me = _server.World.Find(_myId); if (me != null && !me.Dead) me.Pos = new SVec(mon.Center.X, mon.Max.Y + 5); }));
                }
                _teleports = tps;
            }
            _lighting = new LightingLayer(map, _world);
            _dirLayer = new DirectorLayer { Flares = _flares };
            AddChild(_dirLayer);
            foreach (var w in map.Walls)
            {
                if (w.Hidden) continue;   // хитбокс пропса РТ — рисуется спрайтом
                var v = new WallView2D(w, TerrainView.BiomeAt(map, (w.Min + w.Max) * 0.5f));
                if (w.Tex != null) v.Paint = DressPaint.For(map, w);
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
                if (_buildMenu.PickByKey(k.PhysicalKeycode) || _hammerMenu.PickByKey(k.PhysicalKeycode)) return;
                if (k.PhysicalKeycode == Key.Escape && (_buildMenu.Visible || _hammerMenu.Visible)) { _buildMenu.Close(); _hammerMenu.Close(); return; }
                if (k.PhysicalKeycode == Key.F7) { _artMode = !_artMode; _hud.Toast(_artMode ? "Масштаб ассетов: наведи на объект, [ / ] — меньше/больше, обратная косая — сброс, F7 — выход" : "Масштаб ассетов выключен"); return; }
                if (_artMode && _artHover != null && k.PhysicalKeycode is Key.Bracketleft or Key.Bracketright or Key.Backslash)
                {
                    string key = ArtScale.KeyOf(_artHover);
                    float v = k.PhysicalKeycode == Key.Backslash ? 1f : Mathf.Clamp(ArtScale.Get(key) * (k.PhysicalKeycode == Key.Bracketright ? 1.05f : 1f / 1.05f), 0.2f, 5f);
                    ArtScale.Set(GetTree(), key, v);
                    _hud.Toast($"{System.IO.Path.GetFileNameWithoutExtension(key)}: ×{v:0.00} (для всех таких)");
                    return;
                }
                if (SandboxKey(k.PhysicalKeycode)) return;
                if (_spectate && SpectatorKey(k.PhysicalKeycode)) return;
                if (_hud.CraftOpen && k.PhysicalKeycode is Key.Key1 or Key.Key2 or Key.Key3 or Key.Key4) { _hud.WorkbenchTab((int)(k.PhysicalKeycode - Key.Key1)); return; }   // окно верстака: 1–4 — вкладки
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
                        break;
                    case Key.F9: _debugEdge = true; break;
                    case Key.F8: _server.World.GiveBuilderKit(_server.World.Find(_myId)); break;   // набор строителя
                    case Key.Equal: case Key.KpAdd:
                        if (_autoRun != SVec.Zero) { _autoRun = SVec.Zero; _hud.Toast("Автобег выключен"); break; }
                        _autoRun = WasdVector();
                        _autoSprint = Input.IsPhysicalKeyPressed(Key.Shift);
                        _hud.Toast(_autoRun != SVec.Zero ? "Автобег: любая из WASD — стоп" : "Автобег: зажми направление и нажми «=»");
                        break;
                    case Key.W: case Key.A: case Key.S: case Key.D: _autoRun = SVec.Zero; break;
                    case Key.Q:
                        if (!_hud.CraftOpen) CloseBigExcept(false, true, false);
                        _hud.ToggleCraft();
                        break;
                    case Key.Tab:
                        if (_inv.Visible) CloseInv();
                        else { CloseBigExcept(true, false, false); _invOpen = true; }
                        break;
                    case Key.G:
                        if (!_mapView.Visible) CloseBigExcept(false, false, true);
                        _mapView.Visible = !_mapView.Visible; _mapView.QueueRedraw();
                        break;
                    case Key.F10: if (_fpsLabel != null) _fpsLabel.Visible = !_fpsLabel.Visible; break;
                    case Key.F5: _hud.Toast(SaveWorld() ? "Мир сохранён" : _saveEnabled ? "Сохранение НЕ записано: " + _saveError : "Сохранение выключено в этом запуске"); break;
                    case Key.F6: ToggleRigTune(); break;   // ручная настройка рига (размеры и сдвиги частей)
                    case Key.F12: SaveScreenshot($"user://shot_{Time.GetTicksMsec()}.png"); break;
                    case Key.Escape when _hud.AdminOpen:
                        _hud.ToggleAdmin();
                        break;
                    case Key.Escape when _mapView.Visible:
                        _mapView.Visible = false;
                        break;
                    case Key.Escape when _inv.Visible:
                        CloseInv();
                        break;
                    case Key.Escape when _hud.CraftOpen:
                        _hud.CloseCraftAll();
                        break;
                    case Key.Escape:
                        Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Hidden ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Hidden;
                        break;
                }
            }
            else if (e is InputEventMouseButton rb && rb.ButtonIndex == MouseButton.Right && !rb.Pressed) { _buildMenu.Release(); _hammerMenu.Release(); }
            else if (e is InputEventMouseButton mb && mb.Pressed)
            {
                // ПКМ с Планом — колесо элементов (держишь, наводишь, отпускаешь) вместо прицела
                if (mb.ButtonIndex == MouseButton.Right && _cur?.Self != null && _cur.Self.HoldingPlan && !UiShown) _buildMenu.Open();
                // ПКМ с киянкой по своей постройке — колесо: уровни и снос
                // с Планом колесо листает элементы стройки (Ctrl+колесо — зум), иначе — зум
                bool plan = _cur?.Self != null && _cur.Self.HoldingPlan && !mb.CtrlPressed;
                bool hammer = _cur?.Self != null && _cur.Self.HoldingHammer && !mb.CtrlPressed;
                if (hammer && mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
                {
                    // колёсико с киянкой — режим: дерево → камень → металл → сталь → снос
                    int i = System.Array.IndexOf(HammerModes, _hammerMode), n = HammerModes.Length;
                    _hammerMode = HammerModes[((i + (mb.ButtonIndex == MouseButton.WheelDown ? 1 : -1)) % n + n) % n];
                    _hud.Toast($"Киянка: {HammerModeName(_hammerMode)}");
                }
                else if (plan && mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
                    _setPiece = (int)Building.NextPiece(_cur.Self.BuildPiece, mb.ButtonIndex == MouseButton.WheelDown ? 1 : -1);
                else if (mb.ButtonIndex == MouseButton.WheelUp) _zoom = Mathf.Min(2.6f, _zoom * 1.1f);
                else if (mb.ButtonIndex == MouseButton.WheelDown) _zoom = Mathf.Max(0.9f, _zoom / 1.1f);
            }
        }

        /// <summary>ПКМ с киянкой: колесо над постройкой под прицелом (её id запоминается — колесо держит цель, пока открыто).</summary>
        void OpenHammerWheel()
        {
            if (_cur?.Self == null || !_cur.Self.HoldingHammer || _cur.Self.HammerTarget == 0 || _cur.Self.Dead) return;
            _hammerFocus = _cur.Self.HammerTarget;
            _hammerMenu.Fill(_cur.Self.HammerWheel);
            _hammerMenu.Open();
        }

        /// <summary>Колесо киянки: пункты с сервера, подсветка цели; цель пропала или киянку убрали — закрыть.</summary>
        void UpdateHammerWheel()
        {
            var self = _cur.Self;
            bool open = _hammerMenu.Visible && self.HoldingHammer && !self.Dead && self.HammerTarget == _hammerFocus;
            if (_hammerMenu.Visible && !open) _hammerMenu.Close();
            StructSnap? target = null;
            int focus = open ? _hammerFocus : self.HoldingHammer && !self.Dead && !UiShown ? self.HammerTarget : 0;   // киянка в руках — цель под ней видна всегда
            if (focus != 0) foreach (var st in _cur.Structures) if (st.Id == focus) { target = st; break; }
            if (open)
            {
                _hammerMenu.Fill(self.HammerWheel);
                _hammerMenu.Title = target is StructSnap t ? $"{Building.KindName(t.Kind)} · {Building.TierNames[t.Tier]} · {Mathf.CeilToInt(t.Hp * Building.MaxHp(t.Kind, t.Tier))}/{(int)Building.MaxHp(t.Kind, t.Tier)}" : "";
            }
            if (_hammerMark == null) { _hammerMark = new BuildGhost2D { Tint = UiTheme.Accent }; _world.AddChild(_hammerMark); }
            _hammerMark.Visible = target != null;
            Color tint = _hammerMode == World.HammerDemolish ? UiTheme.Bad : self.HammerActionOk ? UiTheme.Accent : UiTheme.Dim;   // снос — красным
            if (_hammerMark.Tint != tint) { _hammerMark.Tint = tint; _hammerMark.QueueRedraw(); }
            _hammerMark.Modulate = new Color(1, 1, 1, 0.65f + 0.35f * Mathf.Sin(Time.GetTicksMsec() / 1000f * 6f));   // пульсирует
            // дверь под E — чуть заметная подсветка (слабее киянки)
            if (_doorMark == null) { _doorMark = new BuildGhost2D { Tint = new Color(1f, 0.95f, 0.8f), Modulate = new Color(1, 1, 1, 0.4f) }; _world.AddChild(_doorMark); }
            StructSnap? door = null;
            if (self.InteractDoor != 0 && !self.Dead && !UiShown) foreach (var st in _cur.Structures) if (st.Id == self.InteractDoor) { door = st; break; }
            _doorMark.Visible = door != null && target == null;
            if (door is StructSnap ds) _doorMark.Set(ds.Kind, null, ds.A, ds.B, true);
            if (_useMark == null) { _useMark = new InteractMark(); _world.AddChild(_useMark); }
            _useMark.Visible = door == null && target == null && !self.Dead && !UiShown && self.InteractHalf != default;
            if (_useMark.Visible) _useMark.Set(self.InteractAt, self.InteractHalf);
            if (target is StructSnap ts) _hammerMark.Set(ts.Kind, Building.IsFloor(ts.Kind) ? ts.Poly : null, ts.A, ts.B, true);
        }

        /// <summary>Наблюдатель: Tab — следующий бот, F — свободная камера, 1–4 — скорость ×1/×2/×4/×8.</summary>
        void DrawDump()
        {
            var cam = GetViewport().GetCamera2D();
            var screen = GetViewport().GetVisibleRect();
            var byType = new Dictionary<string, int>(); var byTop = new Dictionary<string, int>();
            int total = 0;
            void Walk(Node n, string top, int depth)
            {
                if (n is CanvasItem ci && !ci.Visible) return;
                if (n is CanvasItem c2)
                {
                    total++;
                    var k = n.GetType().Name; byType[k] = byType.GetValueOrDefault(k) + 1;
                    byTop[top] = byTop.GetValueOrDefault(top) + 1;
                }
                foreach (var ch in n.GetChildren()) Walk(ch, depth == 0 ? ch.GetType().Name + ":" + ch.Name : top, depth + 1);
            }
            Walk(GetTree().Root, "root", 0);
            GD.Print($"drawdump: всего видимых CanvasItem {total}, камера {cam?.GlobalPosition}, экран {screen.Size}");
            int lon = 0, lsh = 0, occ = 0; var lsrc = new Dictionary<string, int>();
            void WL(Node n) { if (n is PointLight2D pl && pl.Enabled && pl.Energy > 0 && pl.IsVisibleInTree()) { lon++; if (pl.ShadowEnabled) lsh++; var k = pl.GetParent()?.GetType().Name ?? "?"; lsrc[k] = lsrc.GetValueOrDefault(k) + 1; } if (n is LightOccluder2D o && o.IsVisibleInTree()) occ++; foreach (var ch in n.GetChildren()) WL(ch); }
            WL(GetTree().Root);
            GD.Print($"  свет: включено {lon}, с тенями {lsh}, окклюдеров {occ}; по родителям: {string.Join(", ", lsrc.Select(x => x.Key + " " + x.Value))}");
            var procBy = new Dictionary<string, int>(); int procAll = 0;
            void WP(Node n) { if (n.IsProcessing() || n.IsPhysicsProcessing()) { procAll++; var k = n.GetType().Name; procBy[k] = procBy.GetValueOrDefault(k) + 1; } foreach (var ch in n.GetChildren()) WP(ch); }
            WP(GetTree().Root);
            GD.Print($"  с _Process: {procAll}; " + string.Join(", ", procBy.OrderByDescending(x => x.Value).Take(10).Select(x => x.Key + " " + x.Value)));
            GD.Print($"  кадр: calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0} objs {Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame):0}");
            foreach (var kv in byType.OrderByDescending(x => x.Value).Take(15)) GD.Print($"  тип {kv.Key}: {kv.Value}");
            var top2 = new Dictionary<string, int>();
            void Walk2(Node n, string path, int depth)
            {
                if (n is CanvasItem ci && !ci.Visible) return;
                if (depth == 3) { top2[path] = top2.GetValueOrDefault(path) + Count(n); return; }
                foreach (var ch in n.GetChildren()) Walk2(ch, path + "/" + ch.GetType().Name, depth + 1);
            }
            int Count(Node n) { int s = n is CanvasItem ? 1 : 0; foreach (var ch in n.GetChildren()) if (!(ch is CanvasItem cc && !cc.Visible)) s += Count(ch); return s; }
            Walk2(GetTree().Root, "", 0);
            foreach (var kv in top2.OrderByDescending(x => x.Value).Take(12)) GD.Print($"  ветка {kv.Key}: {kv.Value}");
        }

        static void ScrollAll(Node n, int y)
        {
            if (n is ScrollContainer sc && sc.IsVisibleInTree()) sc.ScrollVertical = y;
            foreach (var c in n.GetChildren()) ScrollAll(c, y);
        }

        bool SpectatorKey(Key key)
        {
            switch (key)
            {
                case Key.Tab:
                    var order = BotOrder();   // строго по постоянным номерам
                    if (order.Count == 0) return true;
                    _followId = order[(order.IndexOf(_followId) + 1) % order.Count];
                    _autoCam = false;
                    _hud.Toast($"Бот №{BotNum(_followId)}");
                    return true;
                case Key.F: _followId = 0; _autoCam = false; return true;
                case Key.C: _autoCam = !_autoCam; _camTimer = 0; _hud.Toast(_autoCam ? "Автокамера: к перестрелкам" : "Автокамера выключена"); return true;
                case Key.Key1: _timeScale = 1; return true;
                case Key.Key2: _timeScale = 2; return true;
                case Key.Key3: _timeScale = 4; return true;
                case Key.Key4: _timeScale = 8; return true;
                case Key.Key5: _timeScale = 10; return true;
                case Key.Key6: _timeScale = 20; return true;
                // плюс/минус — шаг скорости по лестнице (×1 … ×60)
                case Key.Equal: case Key.KpAdd: StepTimeScale(1); return true;
                case Key.Minus: case Key.KpSubtract: StepTimeScale(-1); return true;
            }
            return false;
        }

        /// <summary>Постоянные номера ботов: по порядку появления (id), 1..N — для Tab и Ctrl+Shift+номер.</summary>
        System.Collections.Generic.List<int> BotOrder()
        {
            var ids = _server.BotLines().ConvertAll(b => b.id);
            ids.Sort();
            return ids;
        }

        int BotNum(int id) => BotOrder().IndexOf(id) + 1;

        string _botTyped = ""; double _botTypedAt;
        bool _artMode; Node2D _artHover;   // дев-масштаб ассетов (F7)

        static readonly int[] TimeSteps = { 1, 2, 3, 4, 6, 8, 10, 15, 20, 30, 40, 60 };

        void StepTimeScale(int dir)
        {
            int i = System.Array.FindIndex(TimeSteps, v => v >= _timeScale);
            if (i < 0) i = TimeSteps.Length - 1;
            i = System.Math.Clamp(i + dir, 0, TimeSteps.Length - 1);
            _timeScale = TimeSteps[i];
            _hud.Toast($"Скорость ×{_timeScale}");
        }

        /// <summary>
        /// Автокамера: раз в 12 с (или когда наблюдаемый умер) — к живому боту у самой горячей точки (стрельба, рейд, сброс);
        /// горячих нет — к тому, кто дольше всех не показывался.
        /// </summary>
        void AutoCam(float dt)
        {
            var cur = _server.World.Find(_followId);
            if ((_camTimer -= dt) > 0 && cur != null && !cur.Dead) return;
            _camTimer = 12f;
            var bots = _server.BotLines();
            if (bots.Count == 0) return;
            int pick = -1; float bestD = 70f;
            if (_cur.Hot.Count > 0)
                foreach (var (id, _) in bots)
                    if (_server.World.Find(id) is Sim.Actor a && !a.Dead && SVec.Distance(a.Pos, _cur.Hot[0].pos) < bestD) { bestD = SVec.Distance(a.Pos, _cur.Hot[0].pos); pick = id; }
            if (pick < 0)
            {
                int i = bots.FindIndex(b => b.id == _followId);
                for (int k = 1; k <= bots.Count && pick < 0; k++)
                    if (_server.World.Find(bots[(i + k) % bots.Count].id) is Sim.Actor a && !a.Dead) pick = a.Id;
            }
            if (pick >= 0) _followId = pick;
        }

        void UpdateSpectatorPanel(float dt)
        {
            if (_specPanel == null)
            {
                _hud.SpectatorMode();
                _specPanel = new SpectatorPanel { Position = new Vector2(16, 14) };
                _hud.AddChild(_specPanel);
            }
            if (_botTyped != "" && Time.GetTicksMsec() / 1000.0 - _botTypedAt > 0.7)
            {
                var order = BotOrder();
                if (int.TryParse(_botTyped, out int n) && n >= 1 && n <= order.Count) { _followId = order[n - 1]; _autoCam = false; _hud.Toast($"Слежу за ботом №{n}"); }
                else _hud.Toast($"Нет бота №{_botTyped} (всего {order.Count})");
                _botTyped = "";
            }
            if (_autoCam) AutoCam(dt);
            if ((_specRefresh -= dt) > 0) return;
            _specRefresh = 0.4f;
            string speed = $"×{_timeScale}{(_timeScale > 1 && _effSpeed < _timeScale * 0.85f ? $" (факт ×{_effSpeed:0.#})" : "")}";
            string time = $"{(int)(_server.World.Time / 60):00}:{(int)(_server.World.Time % 60):00}";
            var f = _server.BotLines().Find(l => l.id == _followId);
            var rows = _server.BotRows().ConvertAll(r => new SpectatorPanel.Row { Id = r.id, Num = BotNum(r.id), Name = r.name, Roamer = r.roamer, Bench = r.bench, Known = r.known, Stash = r.stash, Dead = r.dead, Goal = r.goal });
            string phase = _cur.Phase == 0 ? "ранняя" : _cur.Phase == 1 ? "средняя" : "поздняя";
            _specPanel.Set(speed, time, f.line != null ? "▶ " + f.line : null, rows, _followId, phase,
                _cur.Markers.ConvertAll(m => (m.Name, m.Open)), _cur.Hot.Count > 0 ? _cur.Hot[0].heat : 0, _server.World.Milestones);
        }

        // ---------- отдача: тянет сам курсор вверх по экрану ----------
        int _recoilStreak; double _recoilLastShot; float _recoilDebt;   // сколько градусов «утянуло» за очередь — часть вернётся сама

        /// <summary>
        /// Выстрел своим стволом: курсор поворачивается вокруг персонажа вверх по экрану (смотришь вправо — против часовой,
        /// влево — по часовой) на RecoilDeg + RecoilGrowDeg × номер в очереди. Компенсируется мышью. Автоматы тянут по ходу
        /// очереди; одиночные — короткий рывок. Пауза > 0,35 с — новая очередь, половина увода плавно возвращается.
        /// </summary>
        void RecoilKick(string weaponId)
        {
            var wd = Items.Get(weaponId)?.Weapon;
            if (wd == null || wd.RecoilDeg <= 0 || Input.MouseMode == Input.MouseModeEnum.Visible && UiShown) return;
            double now = Time.GetTicksMsec() / 1000.0;
            if (now - _recoilLastShot > 0.35) _recoilStreak = 0;
            _recoilLastShot = now;
            var me = _cur?.Self; if (me == null) return;
            bool crouch = Input.IsPhysicalKeyPressed(Key.Alt);
            float deg = Mathf.Min(wd.RecoilDeg + wd.RecoilGrowDeg * _recoilStreak, wd.RecoilDeg * 1.6f) * (crouch ? 0.7f : 1f) * RecoilFeel;   // рост за очередь ограничен
            // целишься вверх или вниз — «вверх по экрану» превращается в увод вбок, его неудобно ловить мышью:
            // там увод слабее (до 30%), точность держит разброс
            var vp = GetViewport();
            var aimV = vp.GetMousePosition() - vp.GetCanvasTransform() * _selfPx;
            if (aimV.LengthSquared() > 4) deg *= 0.3f + 0.7f * Mathf.Abs(aimV.X) / aimV.Length();
            _recoilStreak++;
            _recoilDebt += deg;
            RotateCursor(-deg);
        }

        const float RecoilFeel = 0.45f;   // общая сила увода курсора (подкрутить по ощущениям)

        /// <summary>Повернуть курсор вокруг своего персонажа: «+» — вниз по экрану, «−» — вверх.</summary>
        void RotateCursor(float upDeg)
        {
            var vp = GetViewport();
            var mouse = vp.GetMousePosition();
            var center = vp.GetCanvasTransform() * _selfPx;
            var v = mouse - center;
            if (v.LengthSquared() < 4) return;
            float sign = v.X >= 0 ? 1f : -1f;   // вправо: вверх = против часовой (угол уменьшается); влево — наоборот
            vp.WarpMouse(center + v.Rotated(Mathf.DegToRad(upDeg) * sign));
        }

        /// <summary>Каждый кадр: после паузы в стрельбе прицел сам опускается на половину увода.</summary>
        void RecoilRecover(double delta)
        {
            if (_recoilDebt <= 0.01f) { _recoilDebt = 0; return; }
            if (Time.GetTicksMsec() / 1000.0 - _recoilLastShot < 0.2) return;
            float step = Mathf.Min(_recoilDebt, (float)delta * 25f);
            _recoilDebt -= step;
            RotateCursor(step * 0.7f);   // возвращаем 70% — остальное тянешь сам
            if (_recoilDebt < 0.05f) _recoilDebt = 0;
        }

        static SVec WasdVector()
        {
            var v = SVec.Zero;
            // физические коды (русская раскладка не мешает); удалённый доступ часто шлёт нажатия без скан-кода —
            // тогда по обычной клавише: латиница, кириллица (Ц Ф Ы В) и стрелки
            static bool K(Key phys, Key alt1, Key alt2) => Input.IsPhysicalKeyPressed(phys) || Input.IsKeyPressed(phys) || Input.IsKeyPressed(alt1) || Input.IsKeyPressed(alt2);
            if (K(Key.W, (Key)0x0426, Key.Up)) v.Y -= 1;      // Ц
            if (K(Key.S, (Key)0x042B, Key.Down)) v.Y += 1;    // Ы
            if (K(Key.A, (Key)0x0424, Key.Left)) v.X -= 1;    // Ф
            if (K(Key.D, (Key)0x0412, Key.Right)) v.X += 1;   // В
            v.X = Mathf.Clamp(v.X, -1, 1); v.Y = Mathf.Clamp(v.Y, -1, 1);
            return v;
        }

        /// <summary>Клик мимо админки закрывает её (окна на весь экран ловят клик сами).</summary>
        public override void _Input(InputEvent e)
        {
            if (_hud.CodeKey(e)) { GetViewport().SetInputAsHandled(); return; }   // окно кодового замка — модальное
            // ноутбук: Ctrl+1…Ctrl+0 = F1…F10, Ctrl+− = F11, Ctrl+= = F12 (у многих F-ряд только через Fn)
            // наблюдатель: Ctrl+Shift+цифры — номер бота (можно двузначный), переход через 0,7 с после последней цифры
            if (_spectate && e is InputEventKey nk && nk.Pressed && !nk.Echo && nk.CtrlPressed && nk.ShiftPressed
                && nk.PhysicalKeycode >= Key.Key0 && nk.PhysicalKeycode <= Key.Key9)
            {
                _botTyped += (char)('0' + (nk.PhysicalKeycode - Key.Key0));
                _botTypedAt = Time.GetTicksMsec() / 1000.0;
                _hud.Toast($"Бот №{_botTyped}…");
                GetViewport().SetInputAsHandled();
                return;
            }
            if (e is InputEventKey ck && ck.CtrlPressed && !ck.ShiftPressed && !ck.Echo && LaptopF(ck.PhysicalKeycode) is Key fk)
            {
                GetViewport().SetInputAsHandled();
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = fk, Keycode = fk, Pressed = ck.Pressed });
                return;
            }
            if (e is InputEventMouseButton sbm && sbm.Pressed && SandboxClick(sbm)) { GetViewport().SetInputAsHandled(); return; }   // полигон: свернуть панель / ответ викторины
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex is MouseButton.Left or MouseButton.Right && _hud != null && _hud.CloseAdminIfOutside(mb.GlobalPosition))
                GetViewport().SetInputAsHandled();
        }

        static Key? LaptopF(Key k) => k switch
        {
            Key.Key1 => Key.F1, Key.Key2 => Key.F2, Key.Key3 => Key.F3, Key.Key4 => Key.F4, Key.Key5 => Key.F5,
            Key.Key6 => Key.F6, Key.Key7 => Key.F7, Key.Key8 => Key.F8, Key.Key9 => Key.F9, Key.Key0 => Key.F10,
            Key.Minus => Key.F11, Key.Equal => Key.F12, _ => null,
        };

        InputCmd BuildCmd()
        {
            bool typing = _hud.CodeOpen;
            var move = typing ? SVec.Zero : WasdVector();
            if (_cur?.Self != null && _cur.Self.Dead) _autoRun = SVec.Zero;
            bool auto = move == SVec.Zero && _autoRun != SVec.Zero;
            if (auto) move = _autoRun;
            move += _forceWalk;
            return new InputCmd
            {
                Follow = _followId,
                Move = move,
                Aim = _aim,
                Fire = _autoFireFrames > 0 || (!_hud.CraftOpen && !_hud.CodeOpen && !_hud.AdminOpen && !UiShown && !_buildMenu.Visible && !_hammerMenu.Visible && !SbBlocksFire && _rigTuneLayer == null && Input.IsMouseButtonPressed(MouseButton.Left)),   // при настройке рига (F6) не стреляем   // клики по меню крафта не стреляют
                Eat = _eatEdge,
                DebugKit = _debugEdge,
                Craft = _craftReq,
                Code = _codeReq?.code ?? -1,
                UiOp = (int)(_uiReq?.op ?? UiOp.None),
                SetPiece = _setPiece,
                HammerFocus = _hammerMenu.Visible ? _hammerFocus : 0,
                HammerOp = _hammerOp,
                HammerTarget = _hammerTarget,
                HammerMode = _cur?.Self?.HoldingHammer == true ? _hammerMode : 0,
                Research = _researchReq,
                UiItem = _uiReq?.item,
                UiIndex = _uiReq?.index ?? 0,
                CodeDoor = _codeReq?.door ?? 0,
                Sprint = _forceSprint || Input.IsPhysicalKeyPressed(Key.Shift) || (auto && _autoSprint),
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
            float peekTarget = _forcedPeek ?? (_rigTuneLayer != null ? (RigTuneWindow.ForceAim ? 1f : 0f) : 0f) + (_rigTuneLayer == null && Input.IsMouseButtonPressed(MouseButton.Right) && !(_cur?.Self?.HoldingPlan ?? false) && !(_hammerMenu?.Visible ?? false) ? 1f : 0f);   // с Планом ПКМ — меню
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

        string _saveError = "";
        bool SaveWorld()
        {
            if (!_saveEnabled) return false;
            string path = ProjectSettings.GlobalizePath(SavePath);
            try
            {
                System.IO.File.WriteAllText(path + ".tmp", _server.SaveJson());
                System.IO.File.Move(path + ".tmp", path, true);   // атомарно: не оставим полуфайл при вылете
                return true;
            }
            catch (System.Exception ex)
            {
                // ошибка видна в тосте F5 и остаётся в user://save_errors.log (логи Godot перетираются прогонами)
                _saveError = ex.Message;
                GD.PrintErr($"сохранение не записано: {ex}");
                try { System.IO.File.AppendAllText(ProjectSettings.GlobalizePath("user://save_errors.log"), $"{System.DateTime.Now:u} {ex}" + System.Environment.NewLine); } catch { }
                return false;
            }
        }

        public override void _Notification(int what)
        {
            if (what == NotificationWMCloseRequest) SaveWorld();
        }

        // ---------- цикл ----------

        /// <summary>Загрузка перед игрой: текстуры растительности, декор вокруг точки появления, прогрев шейдеров.
        /// Раньше всё это происходило лениво в первые секунды — рывками, пока мир «подгружался».</summary>
        void StartLoading()
        {
            int mi = _cur.Actors.FindIndex(a => a.Id == _myId);
            var at = mi >= 0 ? Iso.Px(_cur.Actors[mi].Pos) : Vector2.Zero;
            _loading = new LoadingScreen();
            AddChild(_loading);
            var warm = LoadingScreen.WarmUp(at);
            _world.AddChild(warm);
            foreach (var id in LoadingScreen.ArtIn("grass", "plants", "trees", "nodes", "clutter", "decals"))
                _loading.Add(() => { Art.Get(id); });
            for (int i = 0; i < 49; i++) _loading.Add(() => { _decals.Prebuild(at, 0); });
            _loading.Add(() => _decals.Prebuild(at, 0));
            for (int i = 0; i < 3; i++) _loading.NextFrame();   // прогрев и новый декор успевают отрисоваться под экраном
            _loading.Add(() => { warm.QueueFree(); });
        }

        // солнце сдвинулось — перерисовать тени тысяч узлов. Разом это ~0,3 с одного кадра (регулярный фриз раз в несколько
        // игровых минут), поэтому порциями: ближние к камере первыми, по ShadeBatch узлов за кадр (~5 мс _Draw)
        const int ShadeBatch = 150;
        readonly List<CanvasItem> _shadeQueue = new();
        int _shadeAt;

        void QueueShadeRedraw()
        {
            _shadeQueue.Clear(); _shadeAt = 0;
            var cam = _cam.Position;
            var keys = new List<float>();
            foreach (var n in GetTree().GetNodesInGroup(Shade.Group))
                if (n is CanvasItem ci) { _shadeQueue.Add(ci); keys.Add(ci is Node2D n2 ? n2.Position.DistanceSquaredTo(cam) : float.MaxValue); }
            var ka = keys.ToArray(); var qa = _shadeQueue.ToArray();
            System.Array.Sort(ka, qa);
            _shadeQueue.Clear(); _shadeQueue.AddRange(qa);
        }

        void ShadeRedrawStep()
        {
            for (int k = 0; k < ShadeBatch && _shadeAt < _shadeQueue.Count; k++, _shadeAt++)
                if (IsInstanceValid(_shadeQueue[_shadeAt])) _shadeQueue[_shadeAt].QueueRedraw();
            if (_shadeAt >= _shadeQueue.Count && _shadeQueue.Count > 0) { _shadeQueue.Clear(); _shadeAt = 0; }
        }

        /// <summary>--perf: первые 30 с после загрузки — по секундам худший кадр (мс), сколько кадров дольше 50 мс, худшее время декора.</summary>
        /// <summary>--perf: первые 30 с — каждую секунду худший кадр и его состав (логика, декор, _Draw узлов, рендер, сборки мусора);
        /// дальше — только секунды с кадром дольше 40 мс.</summary>
        void PerfHitches()
        {
            ulong now = Time.GetTicksUsec();
            int g0 = System.GC.CollectionCount(0), g2 = System.GC.CollectionCount(2);
            if (_hitchStart == 0) { _hitchStart = _hitchLast = now; _gc0 = g0; _gc2 = g2; GD.Print($"hitch: первый кадр, логика {(now - _procT0) / 1000.0:F0} мс"); return; }
            double ms = (now - _hitchLast) / 1000.0;   // отрисовка прошлого кадра + логика этого
            _hitchLast = now;
            _hitchFrames++;
            if (ms > 50) _hitchBad++;
            if (ms > _hitchWorst)
            {
                _hitchWorst = ms;
                _hitchWhat = (_loading is { Done: false } ? "под экраном загрузки: " : "") + $"логика {(now - _procT0) / 1000.0:F1}, декор {_decalMs:F1}, Draw {_drawUs / 1000.0:F1}, рендер {_renderUs / 1000.0:F1}, GC {g0 - _gc0}/{g2 - _gc2}";
            }
            _gc0 = g0; _gc2 = g2;
            _hitchAllWorst = System.Math.Max(_hitchAllWorst, ms);
            int sec = (int)((now - _hitchStart) / 1000000.0);
            if (sec <= _hitchSec) return;
            if (sec <= 30 || _hitchWorst > 40)
                GD.Print($"hitch: {sec,3} с, кадров {_hitchFrames}, худший {_hitchWorst:F0} мс ({_hitchWhat}), >50 мс: {_hitchBad}");
            if (_hitchSec < 10 && sec >= 10) GD.Print($"hitch: итог — худший кадр за первые 10 с {_hitchAllWorst:F0} мс");
            _hitchSec = sec;
            _hitchFrames = _hitchBad = 0; _hitchWorst = 0;
        }

        // --perf: средняя стоимость частей кадра (мс), печатается вместе с perf
        static readonly string[] ProfNames = { "вход", "сим", "виды", "камера", "декор", "звук", "погода", "окружение", "тени", "свет", "HUD" };
        readonly double[] _prof = new double[11];
        ulong _profT; int _profFrames;
        void P(int i) { var n = Time.GetTicksUsec(); _prof[i] += (n - _profT) / 1000.0; _profT = n; }

        long _raysFrame = -100; SVec _raysAt; bool _raysCrouch; float _raysRange;

        Label _fpsLabel;
        float _fpsT, _fpsWorst;
        int _fpsN;

        void UpdateFps(double delta)
        {
            if (_fpsLabel == null || !_fpsLabel.Visible) return;
            _fpsT += (float)delta; _fpsN++; _fpsWorst = Mathf.Max(_fpsWorst, (float)delta * 1000f);
            if (_fpsT < 0.5f) return;
            float fps = _fpsN / _fpsT;
            _fpsLabel.Text = $"{fps:0} FPS · худший {_fpsWorst:0} мс · draw {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0}";
            _fpsLabel.Modulate = fps >= 55 ? new Color(0.7f, 1f, 0.7f) : fps >= 35 ? new Color(1f, 0.9f, 0.55f) : new Color(1f, 0.55f, 0.5f);
            _fpsT = 0; _fpsN = 0; _fpsWorst = 0;
        }

        public override void _Process(double delta)
        {
            _procT0 = Time.GetTicksUsec();
            _frame++;
            UpdateFps(delta);
            _profT = Time.GetTicksUsec(); _profFrames++;
            RecoilRecover(delta);
            if (_frame == _pressNAt) _timeEdge = true;
            _saveTimer += delta;
            if (_saveTimer >= 60) { _saveTimer = 0; SaveWorld(); }   // автосохранение раз в минуту
            RunScript();
            UpdateAim((float)delta);

            _acc += delta * _timeScale;
            if (_loading != null)
            {
                _loading.Step(12, (float)delta);
                if (!_loading.Done) _acc = 0;   // пока грузимся, мир стоит
                else if (_perf && _loadDoneMs < 0) { _loadDoneMs = (Time.GetTicksUsec() - _hitchStart) / 1000.0; GD.Print($"hitch: экран загрузки снят через {_loadDoneMs:F0} мс после первого кадра"); }
                else if (_loading.IsQueuedForDeletion()) _loading = null;
            }
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
                    SandboxPreTick();
                    if (_uiReq == null && _uiQ.Count > 0) _uiReq = _uiQ.Dequeue();
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
                SandboxPreTick();
                if (_uiReq == null && _uiQ.Count > 0) _uiReq = _uiQ.Dequeue();   // действия инвентаря — по одному за такт (и в обычной игре)
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
                _hammerOp = 0;
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
                _hammerOp = 0;
                _researchReq = null;
                HandleEvents();
            }
            _effSpeed = Mathf.Lerp(_effSpeed, (float)(steps * World.Dt / System.Math.Max(delta, 1e-4)), 0.05f);
            if (steps == maxSteps || _acc > World.Dt * maxSteps) _acc = 0;
            if (_spectate) UpdateSpectatorPanel((float)delta);
            P(1);

            float alpha = (float)(_acc / World.Dt);
            float dt = (float)delta;
            Render(alpha, dt);
            P(2);
            UpdateCamera(dt);
            P(3);
            ulong decT0 = Time.GetTicksUsec();
            if (!_overview && _loading is not { Done: false }) _decals.Update(_cam.Position);
            P(4);
            _decalMs = (Time.GetTicksUsec() - decT0) / 1000.0;
            {
                var me = new SVec(_selfPx.X / Iso.PPM, _selfPx.Y / Iso.PPM);
                bool yard = InYard(me);
                Acoustics.Update(me, _cur.Nodes, TerrainView.BiomeAt(_map, me), yard, delta);
                if (!_overview)
                    _weather.Update(_cam.Position, GetViewport().GetVisibleRect().Size / 2 / _cam.Zoom.X, TerrainView.BiomeAt(_map, me),
                                    World.NightFactorAt(_forcedDay ?? _cur.DayTime), _cur.Sky, dt, _cur.WindAngle, _cur.WindStrength);
                VisionFx.SetWeather(_cur.SnowCover, _cur.Wetness);
                {
                    // тени облаков: ясно — заметные, в дождь и снег небо сплошное — почти нет; ночью не видно
                    float ca = _cur.Sky switch { Rastvordd.Sim.Sky.Clear => 0.55f, Rastvordd.Sim.Sky.Wind => 0.6f, Rastvordd.Sim.Sky.Rain or Rastvordd.Sim.Sky.Snow => 0.22f, _ => 0.06f };
                    ca *= 1f - World.NightFactorAt(_forcedDay ?? _cur.DayTime);
                    var wv = new Vector2(Mathf.Cos(_cur.WindAngle), Mathf.Sin(_cur.WindAngle)) * (1.5f + 6f * _cur.WindStrength);
                    VisionFx.SetClouds(wv, ca, dt); P(6);
                    NightLights.Set(World.NightFactorAt(_forcedDay ?? _cur.DayTime));   // фонари и лампы ночью
                }
            }
            _ambient.Update(new SVec(_selfPx.X / Iso.PPM, _selfPx.Y / Iso.PPM), _forcedDay ?? _cur.DayTime, dt);
            _daylight.Color = _overview ? Colors.White : DayColor(_forcedDay ?? _cur.DayTime) * Weather.Grade(_cur.Sky);   // + цветокор погоды
            if (Shade.SetSun(_forcedDay ?? _cur.DayTime)) QueueShadeRedraw();   // тени по солнцу
            ShadeRedrawStep();
            P(8);
            if (_artMode)
            {
                // дев-масштаб: ближайший к курсору объект (по основанию), подсветка мерцанием
                var mouse = _world.GetGlobalMousePosition(); Node2D best = null; float bd = 48f;
                foreach (var n in GetTree().GetNodesInGroup(Shade.Group))
                    if (n is Node2D n2 && n2.IsVisibleInTree() && n2.GlobalPosition.DistanceTo(mouse) is float dd && dd < bd) { bd = dd; best = n2; }
                if (_artHover != best && _artHover != null && IsInstanceValid(_artHover)) _artHover.Modulate = Colors.White;
                _artHover = best;
                if (best != null) best.Modulate = new Color(1.3f, 1.3f, 1f) * (0.85f + 0.15f * Mathf.Sin(Time.GetTicksMsec() / 120f));
            }
            else if (_artHover != null) { if (IsInstanceValid(_artHover)) _artHover.Modulate = Colors.White; _artHover = null; }
            {
                // свет РТ: фонари ночью, лампы корпуса Подстанции — при включённом щитке
                bool powered = _cur.DecorSprites.Exists(d => d.sprite == "props/subst_main_panel_on");
                var halfV = GetViewport().GetVisibleRect().Size / 2 / _cam.Zoom.X;
                _lighting.Update(_overview ? 0 : World.NightFactorAt(_forcedDay ?? _cur.DayTime), powered, new Rect2(_cam.Position - halfV, halfV * 2), dt, _cur.RadioPower);
            }
            UpdateHud(dt);
            P(10);
            SandboxFrame(dt);

            if (_hud.AdminOpen && _frame % 15 == 0) _hud.SetAdminSounds(PlayingSounds());
            if (_perf) PerfHitches();
            if (_perf)
            {
                _perfTime += delta; _perfFrames++;
                if (_perfTime >= 3)
                {
                    GD.Print(PlayingSounds() + $" buses {AudioServer.BusCount}");
                    GD.Print($"amb: biome {TerrainView.BiomeAt(_map, new SVec(_selfPx.X / Iso.PPM, _selfPx.Y / Iso.PPM))} | {_ambient.Debug}");
                    GD.Print($"perf: {_perfFrames / _perfTime:F0} fps, sim {_simTime / (_perfTime * 30):F2} ms/tick, nodes {_server.World.Nodes.Count}, views {_world.GetChildCount()}, объектов {Performance.GetMonitor(Performance.Monitor.ObjectCount):0}, узлов {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0}, ресурсов {Performance.GetMonitor(Performance.Monitor.ObjectResourceCount):0}, куча .NET {System.GC.GetTotalMemory(false) / (1024 * 1024)} МБ, видеопамять {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024 * 1024):0} МБ");
                    {
                        var cnt = new Dictionary<string, (int all, int near, int lit)>();
                        var vr = GetViewport().GetVisibleRect().Grow(80);
                        foreach (var ch in _world.GetChildren())
                        {
                            var k = ch.GetType().Name; cnt.TryGetValue(k, out var c); c.all++;
                            if (ch is Node2D n2 && vr.HasPoint(n2.GetGlobalTransformWithCanvas().Origin)) { c.near++; if (n2.Material != null) c.lit++; }
                            cnt[k] = c;
                        }
                        var sb = new System.Text.StringBuilder("perf-views(all/near):");
                        foreach (var kv in cnt) if (kv.Value.all > 20) sb.Append($" {kv.Key} {kv.Value.all}/{kv.Value.near}");
                        GD.Print(sb.ToString());
                        var vrid = GetViewport().GetViewportRid();
                        RenderingServer.ViewportSetMeasureRenderTime(vrid, true);
                        GD.Print("perf-кадр: " + string.Join(", ", System.Linq.Enumerable.Range(0, 11).Select(i => $"{ProfNames[i]} {_prof[i] / System.Math.Max(1, _profFrames):F2}"))); System.Array.Clear(_prof); _profFrames = 0;
                        GD.Print($"perf-time: process {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:F2} мс, physics {Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000:F2} мс, render cpu {RenderingServer.ViewportGetMeasuredRenderTimeCpu(vrid) + RenderingServer.GetFrameSetupTimeCpu():F2} мс, gpu {RenderingServer.ViewportGetMeasuredRenderTimeGpu(vrid):F2} мс");
                        GD.Print($"perf-draw: calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0} objs {Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame):0} prims {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame):0} lights {GetTree().GetNodesInGroup("night_light").Count}");
                    }
                    _perfTime = 0; _perfFrames = 0; _simTime = 0;
                }
            }
            _procEnd = Time.GetTicksUsec();   // --perf: отсюда до начала отрисовки — _Draw узлов (печать замеров выше в него не попадает)
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
                SandboxEvent(e);
                switch (e.Kind)
                {
                    case SimEventKind.Shot:
                        // вспышка у дула по типу ствола (ПБС — без вспышки, D46), свет выдаёт стрелка ночью
                        if (e.Text is not ("weapon_bow" or "weapon_crossbow"))
                        {
                            // вспышка — из дула нарисованного оружия (риг), иначе — из точки выстрела сервера
                            if (_actorViews.TryGetValue(e.ActorId, out var sv) && sv.TryMuzzle(out var mAt, out var mDir)) Vfx.Muzzle(mAt + new Vector2(0, Iso.GunH), mDir, e.Text, e.Value > 0);   // Muzzle ждёт точку на земле и сам поднимает на высоту ствола
                            else Vfx.Muzzle(Iso.Px(e.Pos + e.Dir * 0.15f), new Vector2(e.Dir.X, e.Dir.Y), e.Text, e.Value > 0);
                        }
                        if (_actorViews.TryGetValue(e.ActorId, out var shooter)) shooter.Kick();
                        if (e.ActorId == _myId && !_spectate) RecoilKick(e.Text);
                        if (e.ActorId == _myId && e.Text != "weapon_bow") _shake = Mathf.Min(1.5f, _shake + 0.6f);
                        if (e.Text != "weapon_bow") SpawnCasing(e.Pos, e.Dir);
                        PlayShotSound(e.Text, e.Pos, e.OtherId > 0 ? Mathf.Clamp(e.OtherId / 100f, 0.2f, 1f) : 1f);
                        break;
                    case SimEventKind.Reload:
                        if (_actorViews.TryGetValue(e.ActorId, out var reloader)) reloader.StartReload(Items.Get(e.Text)?.Weapon?.ReloadTime ?? 2f);
                        {
                            string wn = (e.Text ?? "").Replace("weapon_", "");
                            Sfx.Play(Sfx.Pick("reload_" + wn, "reload_ak"), Iso.Px(e.Pos), -2, 25, 0.02f);   // reload_akm… → заглушка reload_ak
                            // затвор в конце перезарядки (свои записи: bolt_akm, bolt_svd)
                            if (Sfx.Get("bolt_" + wn) != null)
                            {
                                var bp = Iso.Px(e.Pos); float rt = Items.Get(e.Text)?.Weapon?.ReloadTime ?? 2f;
                                GetTree().CreateTimer(rt * 0.85f).Timeout += () => Sfx.Play("bolt_" + wn, bp, -2, 25, 0.02f);
                            }
                        }
                        break;
                    case SimEventKind.Swing:
                        if (_actorViews.TryGetValue(e.ActorId, out var swinger)) swinger.StartSwing();
                        Sfx.Play("swing", Iso.Px(e.Pos), -6, 15, 0.15f);
                        break;
                    case SimEventKind.HitNode:
                        if (_nodeViews.TryGetValue(e.OtherId, out var nv)) nv.Hit();
                        Sfx.Play(NodeHitSound(e.OtherId), Iso.Px(e.Pos), -2, 30, 0.1f);
                        SpawnChips(e.OtherId, e.Pos, e.ActorId);
                        Vfx.Hit(Iso.Px(e.Pos), Iso.H(0.9f), NodeHitMat(e.OtherId));
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
                        // свои записи стройки: установка каркаса (тир 0) и улучшение до дерева/камня/металла/стали (броня — как сталь)
                        if (e.Text == "ремонт") Sfx.Play(e.Value >= 2 ? "hit_stone" : "hit_wood", Iso.Px(e.Pos), -3, 25, 0.1f);
                        else Sfx.Play(Sfx.Pick(e.Value <= 0 ? "build_place_0" : $"build_upgrade_{System.Math.Clamp((int)e.Value, 1, 4)}", e.Value >= 2 ? "hit_stone" : "hit_wood"), Iso.Px(e.Pos), -2, 30, 0.04f);
                        SpawnChipsAt(e.Pos, TierChips[System.Math.Clamp((int)e.Value, 0, 4)], e.ActorId, 5);
                        Vfx.Dust(Iso.Px(e.Pos), true);
                        if (e.ActorId == _myId && e.Value > 0) _hud.Toast($"{e.Text}: улучшено");
                        break;
                    case SimEventKind.Destroyed:
                        Sfx.Play(Sfx.Pick($"build_destroy_{System.Math.Clamp((int)e.Value, 0, 4)}", (int)e.Value >= 2 ? "hit_stone" : "hit_wood"), Iso.Px(e.Pos), 2, 40, 0.05f);
                        SpawnChipsAt(e.Pos, TierChips[System.Math.Clamp((int)e.Value, 0, 4)], -1, 18);
                        Vfx.Collapse(Iso.Px(e.Pos), 7);   // обрушение: кольцо пыли и дым
                        _shake = Mathf.Min(1.2f, _shake + 0.3f);
                        break;
                    case SimEventKind.Demolished:
                        // снос киянкой: разбор, а не обрушение — пыль, щепа/крошка, глухой стук (своего звука нет — заглушки)
                        Sfx.Play(Sfx.Pick($"build_destroy_{System.Math.Clamp((int)e.Value, 0, 4)}", (int)e.Value >= 2 ? "hit_stone" : "hit_wood"), Iso.Px(e.Pos), 0, 30, 0.08f);
                        SpawnChipsAt(e.Pos, TierChips[System.Math.Clamp((int)e.Value, 0, 4)], e.ActorId, 12);
                        Vfx.Dust(Iso.Px(e.Pos));
                        Vfx.Collapse(Iso.Px(e.Pos), 4);
                        if (e.ActorId == _myId) _shake = Mathf.Min(1.2f, _shake + 0.15f);
                        break;
                    case SimEventKind.Revealed:
                        // ячейка раскрылась: звоночек по редкости (Arc Raiders)
                        Sfx.Play($"reveal_{System.Math.Clamp((int)e.Value, 0, 4)}", _selfPx, -4, 10, 0.02f);
                        Sfx.Play("rummage", _selfPx, -10, 10, 0.1f);
                        break;
                    case SimEventKind.Wear:
                        // одежда (D47): надел — шорох/лязг/резина; пластина разбита — хруст (заглушки — rummage, hit_metal_ore, glass_break)
                        if (e.Text == "plate_break") { Sfx.Play(Sfx.Pick("plate_break", "glass_break"), Iso.Px(e.Pos), -2, 30); if (e.ActorId == _myId) _hud.Toast("Пластина разбита"); }
                        else if (e.ActorId == _myId) Sfx.Play(Sfx.Pick(e.Text, e.Text == "wear_metal" ? "hit_metal_ore" : "rummage"), _selfPx, -8, 8);
                        break;
                    case SimEventKind.Machine:
                        // механизмы готовых РТ: пробка, питание, роллета, считыватель крейта
                        if (e.Text != null && e.Text.StartsWith("станок:")) { Sfx.Play(Sfx.Pick("lathe_done", "craft_done"), Iso.Px(e.Pos), -2, 30, 0.05f); break; }   // деталь готова (D46)
                        if (e.Text != null && e.Text.StartsWith("станция:")) { Sfx.Play(Sfx.Pick("station_done", "craft_done"), Iso.Px(e.Pos), -8, 15, 0.05f); break; }   // станция D54 сделала порцию
                        if (e.Text == "pump") { Sfx.Play(Sfx.Pick("hand_pump", "hit_metal_ore"), Iso.Px(e.Pos), -4, 40, 0.15f); break; }   // насос Заправки (D54)
                        switch (e.Text)
                        {
                            case "fuse": Sfx.Play("lock_click", Iso.Px(e.Pos), 0, 15, 0f); Vfx.Electric(Iso.Px(e.Pos), Iso.H(1.3f), false); break;
                            case "power": Sfx.Play("fuse_hiss", Iso.Px(e.Pos), 2, 60, 0f); Vfx.Electric(Iso.Px(e.Pos), Iso.H(1.3f), true); _shake = Mathf.Min(1f, _shake + 0.3f); break;
                            case "shutter": Sfx.Play("hit_metal_ore", Iso.Px(e.Pos), 0, 40, 0.1f); Vfx.Dust(Iso.Px(e.Pos)); break;
                            case "reader": Sfx.Play("c4_beep", Iso.Px(e.Pos), 0, 60, 0f); Vfx.Blink(Iso.Px(e.Pos), Iso.H(1.1f), new Color(0.4f, 1f, 0.5f), 2.5f, 0.25f); break;
                            case "crate": Sfx.Play("craft_done", Iso.Px(e.Pos), 2, 40, 0f); Vfx.Blink(Iso.Px(e.Pos), Iso.H(0.8f), new Color(0.4f, 1f, 0.5f), 5f, 0.5f); Vfx.SmokeBurst("smoke_green", Iso.Px(e.Pos), 6); break;
                            case "pry": Sfx.Play("hit_metal_ore", Iso.Px(e.Pos), -2, 25, 0.15f); Vfx.Hit(Iso.Px(e.Pos), Iso.H(1f), HitMat.Metal); break;
                            case "glass": Sfx.Play("glass_break", Iso.Px(e.Pos), -4, 30, 0.12f); Vfx.Hit(Iso.Px(e.Pos), Iso.H(1.2f), HitMat.Glass); break;
                            case "beep": Sfx.Play("c4_beep", Iso.Px(e.Pos), 4, 30, 0f); Vfx.Blink(Iso.Px(e.Pos), Iso.H(0.4f), new Color(1f, 0.3f, 0.2f), 2.5f, 0.2f); break;
                            case "gate": Sfx.Play("hit_metal_ore", Iso.Px(e.Pos), 2, 40, 0.05f); Vfx.Dust(Iso.Px(e.Pos)); break;
                            case "alarm": Sfx.Play("c4_beep", Iso.Px(e.Pos), 8, 70, 0.3f); Vfx.Blink(Iso.Px(e.Pos), Iso.H(2f), (int)(_server.World.Time * 2) % 2 == 0 ? new Color(1f, 0.6f, 0.1f) : new Color(1f, 0.2f, 0.1f), 6f, 0.5f); break;
                            case "pipe": Sfx.Play("fuse_hiss", Iso.Px(e.Pos), 0, 45, 0.15f); Vfx.Steam(Iso.Px(e.Pos), Iso.H(0.8f)); break;
                            case "rail": Sfx.Play("hit_metal_ore", Iso.Px(e.Pos), 0, 55, 0.2f); Vfx.Hit(Iso.Px(e.Pos), 4, HitMat.Metal); Vfx.Dust(Iso.Px(e.Pos)); break;
                            case "crane": Sfx.Play("hit_metal_ore", Iso.Px(e.Pos), -6, 90, 0.3f, 0.6f); Vfx.Dust(Iso.Px(e.Pos)); break;
                            case "bang": Sfx.Play("hit_wood", Iso.Px(e.Pos), 2, 40, 0.1f); Vfx.Hit(Iso.Px(e.Pos), Iso.H(1f), HitMat.Wood); Vfx.Dust(Iso.Px(e.Pos)); _shake = Mathf.Min(0.6f, _shake + 0.15f); break;
                            case "drop": Vfx.Collapse(Iso.Px(e.Pos), 8); break;   // груз сброса приземлился: кольцо пыли
                            case "flare": Sfx.Play(Sfx.Pick("flare_launch", "fire_whoosh"), Iso.Px(e.Pos), 6, 400, 0.05f); Vfx.Flare(Iso.Px(e.Pos)); break;   // сигнальная ракета «Маяка» (слышно далеко)
                            case "diesel": Sfx.Play(Sfx.Pick("diesel_loop", "hit_metal_ore"), Iso.Px(e.Pos), -6, 60, 0.2f, 0.5f); Vfx.SmokeBurst("smoke_black", Iso.Px(e.Pos), 3); break;
                            case "power_off": Sfx.Play(Sfx.Pick("diesel_stop", "fuse_hiss"), Iso.Px(e.Pos), -2, 60, 0f); break;
                            case "paper": Sfx.Play(Sfx.Pick("paper_flip", "rummage"), Iso.Px(e.Pos), -8, 8, 0.1f); break;
                        }
                        break;
                    case SimEventKind.Locked:
                    {
                        // Value: 0 — ввести код, 1 — задать (владелец), 2 — принят, -2 — неверный (личное событие)
                        int mode = (int)e.Value;
                        Sfx.Play("lock_click", Iso.Px(e.Pos), mode == -2 ? 0 : -4, 10, 0f, mode == -2 ? 0.7f : mode == 2 ? 1.25f : 1f);
                        if (e.ActorId != _myId) break;
                        if (mode is 2 or -2) _hud.CodeResult(e.OtherId, mode == 2);
                        else if (_scriptCode >= 0 && !_scriptCodeUi) _codeReq = (e.OtherId, _scriptCode);
                        else _hud.ShowCode(e.OtherId, mode);
                        break;
                    }
                    case SimEventKind.Explosion when e.Text == "expl_fire":
                        Sfx.Play("glass_break", Iso.Px(e.Pos), -2, 40, 0.08f);
                        Sfx.Play("fire_whoosh", Iso.Px(e.Pos), -4, 40, 0.08f);
                        Vfx.Molotov(Iso.Px(e.Pos));
                        break;
                    case SimEventKind.Explosion:
                    {
                        bool big = e.Text == "expl_c4";
                        Vfx.Explosion(Iso.Px(e.Pos), big);
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
                        Vfx.Hit(Iso.Px(e.Pos), Iso.H(1f), e.Text is "каркас" or "дерево" ? HitMat.Wood : e.Text == "камень" ? HitMat.Stone : HitMat.Metal);
                        if (_actorViews.TryGetValue(e.ActorId, out var sh)) sh.HitStop(0.07f);
                        if (e.ActorId == _myId) _shake = Mathf.Min(1.2f, _shake + 0.4f);
                        break;
                    case SimEventKind.Placed:
                        _hud.Toast("Установлено: " + e.Text);
                        Sfx.Play("hit_wood", Iso.Px(e.Pos), -4, 20);
                        break;
                    case SimEventKind.HitActor:
                        // шлем/пластина — искры, тело — стилизованные капли
                        Vfx.Hit(Iso.Px(e.Pos), Iso.H(e.Text == "head" || e.Text == "helmet" ? 1.5f : 1.05f), e.Text is "helmet" or "plate" ? HitMat.Armor : HitMat.Flesh,
                            _actorViews.TryGetValue(e.OtherId, out var shooterV) && _actorViews.TryGetValue(e.ActorId, out var hitV) ? hitV.Position - shooterV.Position : Vector2.Zero);
                        // зона попадания (D47): хедшот — щелчок, шлем — «дзынь», пластина — тупой удар, конечность — глуше
                        Sfx.Play(e.Text switch
                        {
                            "head" => Sfx.Pick("hit_head", "hit_flesh"),
                            "helmet" => Sfx.Pick("hit_helmet", "hit_metal_ore"),
                            "plate" => Sfx.Pick("hit_plate", "hit_metal_ore"),
                            "limb" => Sfx.Pick("hit_limb", "hit_flesh"),
                            _ => "hit_flesh",
                        }, Iso.Px(e.Pos), e.Text == "limb" ? -5 : -2, 30, 0.12f);
                        if (e.OtherId == _myId) _hud.Crosshair.HitFlash = e.Text is "head" or "helmet" ? 0.3f : 0.15f;   // хедшот — маркер ярче и дольше
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
                        Vfx.Hit(Iso.Px(e.Pos), Iso.GunH, HitMaterialAt(e.Pos), new Vector2(-e.Dir.X, -e.Dir.Y));
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

        /// <summary>
        /// Звук выстрела (и полигон звука — тот же путь): настоящий сэмпл для всего огнестрела (самопал — чуть выше и тише),
        /// каждый доигрывает до конца. D46: свой звук shot_<ствол> (shot_akm, shot_nagant…), пока файла нет — сэмпл калаша
        /// (громкие — как есть, тихие — выше и тише). Дальность слышимости — по типу ствола (Weaponry.ShotHearM),
        /// глушитель (muf &lt; 1) сокращает её и громкость.
        /// </summary>
        /// <returns>id звука, который сыграл.</returns>
        static string PlayShotSound(string weapon, SVec pos, float muf = 1f)
        {
            if (weapon == "weapon_bow") { Sfx.Play("shot_bow", Iso.Px(pos), -4, 25, 0.06f); return "shot_bow"; }
            string own = "shot_" + (weapon ?? "").Replace("weapon_", "");
            bool loud = (Items.Get(weapon)?.Weapon?.NoiseRadius ?? 0) >= 70;
            float hear = Weaponry.ShotHearM(weapon) * muf, supDb = muf < 0.95f ? -8f : 0f;
            if (Sfx.Get(own) != null) { Sfx.Play(own, Iso.Px(pos), supDb, hear, 0.03f); return own; }
            Sfx.Play("shot_ak", Iso.Px(pos), (loud ? 0 : -3) + supDb, hear, 0.03f, loud ? 1f : 1.12f);
            return "shot_ak";
        }

        /// <summary>Отладка: эффекты рядом с игроком (только клиентские, мир не меняется).</summary>
        void DebugVfx(string what)
        {
            var me = _selfPx;
            var right = new Vector2(1, 0);
            switch (what)
            {
                case "all":
                    // ряд эффектов слева направо в 2 м друг от друга, второй ряд — ниже
                    Vfx.Muzzle(me + new Vector2(-160, -40), right, "weapon_nagant", false);
                    Vfx.Muzzle(me + new Vector2(-110, -40), right, "weapon_akm", false);
                    Vfx.Muzzle(me + new Vector2(-50, -40), right, "weapon_mp133", false);
                    Vfx.Muzzle(me + new Vector2(20, -40), right, "weapon_akm", true);
                    Vfx.Hit(me + new Vector2(60, -40), Iso.GunH, HitMat.Metal);
                    Vfx.Hit(me + new Vector2(100, -40), Iso.GunH, HitMat.Wood);
                    Vfx.Hit(me + new Vector2(140, -40), Iso.GunH, HitMat.Stone);
                    Vfx.Hit(me + new Vector2(180, -40), Iso.GunH, HitMat.Flesh);
                    Vfx.Hit(me + new Vector2(220, -40), Iso.GunH, HitMat.Glass);
                    Vfx.Splash(me + new Vector2(-160, 60));
                    Vfx.Dust(me + new Vector2(-110, 60));
                    Vfx.Electric(me + new Vector2(-60, 60), Iso.H(1.2f), true);
                    Vfx.Steam(me + new Vector2(-10, 60), Iso.H(0.6f));
                    Vfx.SmokeBurst("smoke_green", me + new Vector2(50, 60), 4);
                    Vfx.SmokeBurst("smoke_yellow", me + new Vector2(110, 60), 4);
                    Vfx.Collapse(me + new Vector2(180, 60), 6);
                    break;
                case "boom": Vfx.Explosion(me + new Vector2(90, 0), false); Vfx.Explosion(me + new Vector2(-110, 10), true); break;
                case "molotov": Vfx.Molotov(me + new Vector2(80, 0)); break;
                case "flare": Vfx.Flare(me + new Vector2(60, 40)); break;
                case "fire":
                {
                    var fv = new FireView2D(1.5f, 7) { Position = me + new Vector2(70, 10), Left = 20 };
                    _world.AddChild(fv);
                    _debugFires.Add(fv);
                    break;
                }
            }
        }

        readonly List<FireView2D> _debugFires = new();

        HitMat NodeHitMat(int nodeId)
        {
            foreach (var n in _cur.Nodes)
                if (n.Id == nodeId)
                    return n.Kind switch
                    {
                        NodeKind.Tree => HitMat.Wood, NodeKind.Metal or NodeKind.Barrel or NodeKind.Junk => HitMat.Metal,
                        NodeKind.Tire or NodeKind.TireStack => HitMat.Dust, _ => HitMat.Stone,
                    };
            return HitMat.Stone;
        }

        /// <summary>Во что попала пуля: ресурс рядом (дерево — щепки, руда — крошка), стена карты по виду, иначе бетон.</summary>
        HitMat HitMaterialAt(SVec p)
        {
            foreach (var n in _cur.Nodes)
                if (SVec.DistanceSquared(n.Pos, p) < 1.2f) return NodeHitMat(n.Id);
            foreach (var w in _map.Walls)
            {
                if (p.X < w.Min.X - 0.12f || p.X > w.Max.X + 0.12f || p.Y < w.Min.Y - 0.12f || p.Y > w.Max.Y + 0.12f) continue;
                return w.Kind switch
                {
                    WallKind.Crate or WallKind.WoodCrate => HitMat.Wood,
                    WallKind.Car or WallKind.Barrel or WallKind.Barrier or WallKind.Junk => HitMat.Metal,
                    _ => HitMat.Stone,
                };
            }
            return HitMat.Stone;
        }

        string NodeHitSound(int nodeId)
        {
            if (nodeId == -2) return Sfx.Pick("lathe_work", "hit_metal_ore");   // токарный станок (D46)
            if (nodeId == -3) return Sfx.Pick("drill_work", "hit_stone");       // буровая (D54)
            foreach (var n in _cur.Nodes)
                if (n.Id == nodeId)
                    return n.Kind == NodeKind.Tree ? "hit_wood" : n.Kind == NodeKind.Stone ? "hit_stone" : "hit_metal_ore";
            return "hit_stone";
        }

        /// <summary>
        /// Шаги: ровно один звук на каждое касание стопы в анимации (view.StepContacts) — звук и ноги всегда в такт;
        /// по траве мягко, по гравию/песку/снегу/бетону жёстче.
        /// </summary>
        void Footstep(int id, SVec pos, float speed, int contacts, bool crouch = false)
        {
            for (int i = 0; i < contacts; i++)
            {
                bool sprint = speed > Sim.World.WalkSpeed * 1.3f;
                float vol = (id == _myId ? Tuning.StepsVolumeDb : -2) + (sprint ? 3 : 0) - (crouch ? 6 : 0);
                var surf = SurfaceAt(pos);
                // слышимость чужих шагов от радиуса обзора (50 м): бег — почти весь обзор, шаг — 3/5, присед — четверть
                Footsteps.Play(surf, Iso.Px(pos), vol, crouch ? Footsteps.HearCrouch : sprint ? Footsteps.HearRun : Footsteps.HearWalk);
                // пыль из-под ног: только песок/грунт и снег, едва заметно; в приседе не пылит
                if (!crouch && surf is Surface.Sand or Surface.Snow && (sprint || (_frame + id) % 2 == 0))
                    Vfx.Dust(Iso.Px(pos), true, sprint ? 0.55f : 0.35f);
                if (_gaitLog && id == _myId)
                {
                    float t = Time.GetTicksMsec() / 1000f;
                    _gaitTimes.Add(t);
                    GD.Print($"gait: шаг t={t:0.000} скорость {speed:0.00} м/с темп {Footsteps.Cadence(speed, crouch):0.00} шаг/с{(crouch ? " присед" : sprint ? " бег" : "")}");
                }
            }
        }

        bool _gaitLog;
        readonly List<float> _gaitTimes = new();
        /// <summary>Отладка (--do=N:gait): сколько касаний (= звуков шага) в секунду за последний отрезок.</summary>
        void GaitReport()
        {
            if (_gaitTimes.Count >= 3)
            {
                int n = _gaitTimes.Count - 1;
                GD.Print($"gait: {_gaitTimes.Count} касаний за {_gaitTimes[^1] - _gaitTimes[0]:0.00} с → {n / (_gaitTimes[^1] - _gaitTimes[0]):0.00} шаг/с");
            }
            else GD.Print($"gait: касаний {_gaitTimes.Count}");
            _gaitTimes.Clear();
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
                view.SetWorn(a.Worn);   // одежда слоями на риге (D47)
                view.SetState(Iso.Px(pos), facing, a.Dead, a.Reloading, speed, dt, a.HeldId, a.Crouching);
                if (!a.Dead && !a.Swimming) Footstep(a.Id, pos, speed, view.StepContacts, a.Crouching);
            }
            ReapViews(_actorViews, seen, dt);
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
                    v = new BagView2D { IsCrate = b.IsCrate, Safe = b.Safe, Punch = b.Punch, Id = b.Id, Tier = b.Tier, Look = b.Look, Position = Iso.Px(b.Pos) };
                    _world.AddChild(v);
                    _bagViews[b.Id] = v;
                }
                v.Visible = true;
                if (v.Empty != b.Empty || v.Unlocked != b.Unlocked) { v.Empty = b.Empty; v.Unlocked = b.Unlocked; v.QueueRedraw(); }
                v.Position = Iso.Px(b.Pos);   // крейт едет в вагоне, висит на кране, приходит капсулой
            }
            ReapViews(_bagViews, seen, dt);

            RenderWorldObjects(dt);

            for (int i = _texts.Count - 1; i >= 0; i--)
                if (!_texts[i].Tick(dt)) { _texts[i].QueueFree(); _texts.RemoveAt(i); }

            for (int i = _debris.Count - 1; i >= 0; i--)
                if (!_debris[i].Tick(dt)) { _debris[i].QueueFree(); _debris.RemoveAt(i); }
            {
                // эффекты (Vfx.cs): живут и анимируются, короткие вне кадра не создаются
                var halfV = GetViewport().GetVisibleRect().Size / 2 / _cam.Zoom.X;
                Vfx.Night = _overview ? 0 : World.NightFactorAt(_forcedDay ?? _cur.DayTime);
                Vfx.Update(dt, new Rect2(_cam.Position - halfV, halfV * 2));
                foreach (var df in _debugFires) df.Tick(dt);
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
                        ? new TreeView2D(Iso.Px(n.Pos), n.Radius, n.Id, TerrainView.BiomeAt(_map, n.Pos), n.Look)
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
                    v = new PickupView2D(p.Kind, TerrainView.BiomeAt(_map, p.Pos)) { Position = Iso.Px(p.Pos), Item = p.Item };
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
                    var half = d.Half == SVec.Zero ? Footprints.HalfOf(d.Kind, d.Rot) : d.Half;
                    v = new DeployView2D(d.Kind, d.Level, d.Rot, half) { Position = Iso.Px(d.Pos + new SVec(0, half.Y)) };   // начало — передний край следа
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
            _feed.Visible = !_hud.CraftOpen && !_mapView.Visible && !_inv.Visible;   // лента у пояса; пояс спрятан под крафтом и картой
            bool hammerTip = _cur.Self.HoldingHammer && _cur.Self.HammerTarget != 0;
            _structTip.Action = _cur.Self.HoldingHammer ? $"Режим: {HammerModeName(_hammerMode)} · " + (hammerTip ? _cur.Self.HammerAction : "наведи на постройку") : null;
            _structTip.ActionOk = _cur.Self.HammerActionOk;
            StructSnap? tipFor = hammerTip ? _cur.Structures.Find(st => st.Id == _cur.Self.HammerTarget) : StructTip.Pick(_cur.Structures, _aim);
            if (hammerTip && tipFor?.Id != _cur.Self.HammerTarget) tipFor = null;
            _structTip.Set(_cur.Self.Dead || UiShown || _buildMenu.Visible || _hammerMenu.Visible || _mapView.Visible ? null : tipFor);
            _buildBar.Visible = _cur.Self.HoldingPlan && !_cur.Self.Dead && !_buildMenu.Visible && !UiShown;
            if (_buildBar.Visible) _buildBar.Set((int)_cur.Self.BuildPiece, (int)_cur.Self.GhostPiece);
            if (_cur.Self.HoldingPlan) _buildMenu.Fill(_cur.Self.BuildPieces);
            else _buildMenu.Close();
            UpdateHammerWheel();
            _guide.SetText(UiShown || _mapView.Visible ? null : _cur.Self.BuildGuide);
            _guide.Position = new Vector2(GetViewport().GetVisibleRect().Size.X - _guide.GetCombinedMinimumSize().X - 16, MiniMap.Side + 60);
            _hud.MapOpen = _mapView.Visible;

            // окно по E (ящик, печь, столб) и крафт у верстака
            bool showInv = !_cur.Self.Dead && (_invOpen || _cur.Self.Ui != null);
            if (_cur.Self.OpenCraft) CloseBigExcept(false, true, false);   // E у верстака: инвентарь/контейнер уходят
            if (_cur.Self.Dead) { CloseBigExcept(false, false, false); _hud.CloseCraftAll(); }
            showInv &= !_hud.CraftOpen;
            _inv.Visible = showInv;
            _hud.InventoryOpen = showInv;
            if (showInv) _inv.Refresh(_cur.Self, _cur.Self.Ui, dt);
            if (_inv.Visible != _uiWasOpen)
            {
                _uiWasOpen = _inv.Visible;
                if (!_inv.Visible) _invOpen = false;
            }
            if (_cur.Self.OpenCraft) { if (_cur.Self.OpenCraftList) _hud.OpenCraftList(); else _hud.OpenTech(); }
            SyncMouse();   // у станции D54 — сразу список крафта   // E у верстака — дерево изучения

            // вход на РТ — название
            if (_cur.Self.Place != _lastPlace) { _lastPlace = _cur.Self.Place; if (_lastPlace != null) _hud.Toast(_lastPlace); }

            // постройки: виды, тени обзора, прозрачность стен перед персонажем, призрак плана
            _build ??= new BuildLayer(_world);
if (_build.Sync(_cur.Structures, _cur.StructVersion))
            {
                if (_rays != null) { _rays.Dynamic.Clear(); _rays.Dynamic.AddRange(_build.Occluders); }
                _decals.Blocked ??= _build.OnFloor;
                _decals.RefreshBlocked(_build.FloorRects());
            }
            _build.UpdateFade(_selfPx, dt);
            foreach (var w in _mapWalls) w.UpdateOcclusion(dt);
            _prefab.Sync(_cur);
            _prefab.Update(_selfPx, dt, Vfx.Night);
            _dirLayer.Sync(_cur.Markers, _cur.Time);
            if (_radio == null)
            {
                _radio = new RadioFeed { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 0, OffsetLeft = -400, OffsetRight = 400, OffsetTop = 116, OffsetBottom = 230 };   // вверху по центру, под сообщениями
                _hud.AddChild(_radio);
            }
            _radio.Sync(_cur.Radio, dt);
            _mapView.Markers = _cur.Markers;
            _mapView.Hot = _cur.Hot.ConvertAll(h => (h.pos, h.heat));
            _mapView.Time = _cur.Time;
            if (_bghost == null) { _bghost = new BuildGhost2D(); _world.AddChild(_bghost); }
            _bghost.Visible = _cur.Self.HoldingPlan && !_cur.Self.Dead && !_hud.CraftOpen;
            if (_bghost.Visible) { _bghost.Set(_cur.Self.GhostPiece, _cur.Self.GhostPoly, _cur.Self.GhostA, _cur.Self.GhostB, _cur.Self.BuildOk); _bghost.SetZone(_cur.Self.HasZone, _cur.Self.ZoneCell); }

            // призрак установки
            var self = _cur.Self;
            _ghost.Visible = self.HeldDeploy && !self.Dead && !_hud.CraftOpen;
            if (_ghost.Visible) _ghost.Set(self.DeployOk, self.DeployPos, self.DeployHalf, self.DeploySnapped, self.DeploySlots);   // как посчитал сервер (сетка клеток D52)
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

        readonly WearSfx _wearSfx = new();

        void UpdateHud(float dt)
        {
            var s = _cur.Self;
            float day = _forcedDay ?? _cur.DayTime;
            // часы (D47): время, воздух и радиация — только с часами «Электроника» на запястье (ConditionView над шкалами)
            _hud.DayTime = day;
            _hud.UpdateHud(s, dt, _peek > 0.05f ? "прицел" : "");
            _wearSfx.Update(s, dt, _selfPx);

            var canvas = GetViewport().GetCanvasTransform();
            float zoom = _cam.Zoom.X;
            // обзор «вилкой» из ног персонажа; за стенами и стволами — серо (лучи как на сервере)
            var feetPx = canvas * _selfPx;
            var selfM = new SVec(_selfPx.X / Iso.PPM, _selfPx.Y / Iso.PPM);
            var dir = new Vector2(_aim.X - selfM.X, _aim.Y - selfM.Y);
            float range = (50 + 30 * _peek) * (1f - 0.4f * World.NightFactorAt(_forcedDay ?? _cur.DayTime)) * SkyInfo.ViewMul(_cur.Sky);   // ливень, снегопад, буран — ближе
            if (_rays == null) { _rays = new VisionRays(_map); if (_build != null) _rays.Dynamic.AddRange(_build.Occluders); }
            bool crouched = _cur.Actors.Find(x => x.Id == _myId).Crouching;
            // лучи обзора — только когда что-то поменялось (сдвиг, присед, дальность) или раз в 6 кадров (двери, постройки): в центре острова это ~0,5 мс за кадр
            if (_frame - _raysFrame >= 6 || SVec.DistanceSquared(selfM, _raysAt) > 0.0025f || crouched != _raysCrouch || Mathf.Abs(range - _raysRange) > 0.5f)
            {
                _rays.Update(selfM, range, _cur.Nodes, crouched);   // в приседе низкое укрытие вплотную закрывает обзор
                _raysFrame = _frame; _raysAt = selfM; _raysCrouch = crouched; _raysRange = range;
            }
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
