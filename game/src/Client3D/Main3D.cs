using System.Collections.Generic;
using System.Linq;
using Godot;
using Rastvordd.Client;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client3D
{
    /// <summary>
    /// 3D-клиент: та же симуляция (LocalServer), тот же интерфейс (HUD, инвентарь, крафт, изучение, колёса стройки,
    /// карта, миникарта, настройки, админка), но мир — в 3D с камерой как в Project Zomboid.
    /// Запуск: Main3D.tscn («Играть 3D.cmd», наблюдатель — «Наблюдать за ботами 3D.cmd»).
    /// В мир напрямую не лезет — только InputCmd → Submit и Snapshot ← BuildSnapshot, как 2D-клиент.
    ///
    /// Сохранение — по тем же правилам, что у 2D: своя игра (без аргументов или только --bots=N / --near=…)
    /// читает и пишет world_save.json (раз в минуту, F5, при закрытии окна); любой отладочный запуск — свежий мир
    /// и ничего не пишет. Не запускай 2D и 3D одновременно — у них одно сохранение.
    /// </summary>
    public partial class Main3D : Node3D
    {
        LocalServer _server;
        MapInfo _map;
        int _myId;
        Snapshot _cur;
        readonly Dictionary<int, ActorSnap> _prevActors = new();
        readonly Dictionary<int, ProjSnap> _prevProjs = new();
        double _acc;

        // мир
        Cam3D _cam;
        DirectionalLight3D _sun;
        Godot.Environment _env;
        OmniLight3D _selfLight;
        MapStatic3D _static;
        Decor3D _decor;
        Veg3D _veg;
        Struct3D _structs;
        Ghost3D _ghost;
        Fx3D _fx;
        Node3D _actorsRoot, _nodesRoot, _deployRoot, _bagsRoot, _pickRoot, _projRoot, _thrownRoot, _fireRoot;
        Pool3D _actors, _nodes, _deploys, _bags, _picks, _projs, _throwns, _fires;

        Terrain3D _terrain;
        Weather3D _weather;
        Ambient3D _ambient;
        Lamps3D _lamps;
        Director3D _director;
        Decals3D _decals;
        Vision3D _vision;
        VisionRays _rays;
        int _raysVersion = -1, _frame, _raysFrame = -100;
        SVec _raysAt; bool _raysCrouch; float _raysRange;

        // интерфейс (тот же, что в 2D)
        Hud _hud;
        InventoryWindow _inv;
        BuildMenu _buildMenu, _hammerMenu;
        BuildBar _buildBar;
        BuildGuidePanel _guide;
        StructTip _structTip;
        PickupFeed _feed;
        RadioFeed _radio;
        MapView _mapView;
        MiniMap _miniMap;
        SettingsWindow _settings;
        Label _fpsLabel, _specLabel;
        double _fpsAcc; int _fpsFrames; float _worstFrame;
        string _lastPlace;
        bool _invOpen, _modalWas = true, _uiWasOpen;

        // сохранение (как у 2D) и наблюдатель
        const string SavePath = "user://world_save.json";
        bool _saveEnabled;
        double _saveTimer;
        string _saveError = "";
        bool _spectate;
        int _followId, _timeScale = 1;

        // ввод
        SVec _aim;
        Vector3 _aimWorld;
        float _peek;
        bool _reloadEdge, _interactEdge, _healEdge, _respawnEdge, _eatEdge, _debugEdge, _timeEdge, _bagEdge;
        int _slot = -1, _craftReq = -1, _craftPendIdx = -1, _craftPendLeft, _setPiece = -1;
        string _researchReq;
        (int door, int code)? _codeReq;
        (UiOp op, string item, int index)? _uiReq;
        readonly Queue<(UiOp op, string item, int index)> _uiQ = new();
        int _hammerFocus, _hammerOp, _hammerTarget, _hammerMode = 1;
        static readonly int[] HammerModes = { 1, 2, 3, 4, World.HammerDemolish, World.HammerTake };

        const float AimH = 1.15f;   // курсор указывает на высоту груди/оружия, как в 2D (Iso.GunH)

        bool AnyModal => _inv.Visible || _hud.CraftOpen || _hud.AdminOpen || _hud.CodeOpen || (_settings?.Visible ?? false);
        static bool Arg(string a) => System.Array.IndexOf(OS.GetCmdlineUserArgs(), a) >= 0;

        public override void _Ready()
        {
            DisplayServer.WindowSetTitle("Раствордд — 3D");
            if (FileAccess.FileExists("res://data/balance.json"))
                try { BalanceOverrides.Apply(FileAccess.GetFileAsString("res://data/balance.json")); } catch (System.Exception ex) { GD.PrintErr($"balance.json: {ex.Message}"); }

            var args = OS.GetCmdlineUserArgs();
            _spectate = Arg("--spectate");
            var seedArg = System.Array.Find(args, a => a.StartsWith("--seed="));
            _server = new LocalServer(seedArg != null && int.TryParse(seedArg.Substring(7), out int seedN) ? seedN : 1337);
            // правило проекта (AGENTS.md): сохранение читается и пишется только в своей игре — без отладочных аргументов
            _saveEnabled = !_spectate && args.All(a => a.StartsWith("--bots=") || a.StartsWith("--near="));
            if (_saveEnabled && FileAccess.FileExists(SavePath))
                try { _server.LoadSave(FileAccess.GetFileAsString(SavePath), out _); }
                catch (System.Exception ex) { GD.PrintErr($"сохранение не прочитано: {ex.Message}"); }
            _myId = _spectate ? _server.ConnectSpectator() : _server.Connect("Игрок");
            _map = _server.GetMap();

            BuildWorld();
            BuildUi();

            Sfx.Init(this);
            Acoustics.Init(this, _map);
            SteamHrtf.Init(this, _map);
            Tuning.Load();
            GameSettings.Load();
            GameSettings.CmdOverrides();
            GameSettings.ApplyVideoAudio();   // окно, vsync, FPS, громкость — те же настройки, что у 2D
            ApplyQuality(GameSettings.Quality);
            _fpsLabel.Visible = GameSettings.ShowFps;

            _cur = _server.BuildSnapshot(_myId);
            Input.MouseMode = Input.MouseModeEnum.Hidden;
            _modalWas = false;
            if (_spectate)
            {
                _hud.SpectatorMode();
                _hud.Toast("Наблюдатель: Tab — следить за ботом, F — свободная камера (WASD), 1–6 — скорость, Z/X — поворот");
            }
            else _hud.Toast(_saveEnabled ? "Z / X или средняя кнопка — повернуть камеру, колёсико — зум, G — карта, Esc — настройки"
                                         : "Отладочный запуск: свежий мир, сохранение выключено");
        }

        /// <summary>Качество графики (Esc → «Графика»): тени, SSAO, свечение, дальность травы и декора.</summary>
        void ApplyQuality(int q)
        {
            _sun.ShadowEnabled = q >= 1;
            _sun.DirectionalShadowMaxDistance = q >= 3 ? 130f : 90f;
            _env.SsaoEnabled = q >= 2;
            _env.GlowEnabled = q >= 1;
            _veg.Radius = q switch { 0 => 0f, 1 => 32f, 2 => 56f, _ => 80f };
            _decor.LoadRadius = q switch { 0 => 70f, 1 => 95f, 2 => 120f, _ => 160f };
            _decor.UnloadRadius = _decor.LoadRadius + 30f;
        }

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
                _saveError = ex.Message;
                GD.PrintErr($"сохранение не записано: {ex}");
                return false;
            }
        }

        public override void _Notification(int what)
        {
            if (what == NotificationWMCloseRequest) SaveWorld();
        }

        void BuildWorld()
        {
            _env = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.55f, 0.65f, 0.72f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.62f, 0.68f, 0.75f),
                AmbientLightEnergy = 0.55f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                SsaoEnabled = true,
                GlowEnabled = true,
                FogEnabled = true,
                FogLightColor = new Color(0.60f, 0.66f, 0.72f),
                FogDensity = 0.004f,
            };
            AddChild(new WorldEnvironment { Environment = _env });

            _sun = new DirectionalLight3D
            {
                ShadowEnabled = true,
                DirectionalShadowMaxDistance = 90f,
                LightEnergy = 1.1f,
                RotationDegrees = new Vector3(-55, 30, 0),
            };
            AddChild(_sun);

            _terrain = new Terrain3D();
            AddChild(_terrain);
            _terrain.Build(_map);

            _static = new MapStatic3D();
            AddChild(_static);
            _static.Build(_map);

            _decor = new Decor3D();     // декор РТ: машины, техника, мебель, заборы… — клетками вокруг игрока
            AddChild(_decor);
            _decor.Build(_map, _static);

            _veg = new Veg3D();         // трава и цветы (после земли: нужна маска троп и площадок)
            AddChild(_veg);
            _veg.Build(_map);

            _structs = new Struct3D(); AddChild(_structs);
            _ghost = new Ghost3D(); AddChild(_ghost);
            _fx = new Fx3D(); AddChild(_fx);
            _decals = new Decals3D(); AddChild(_decals);
            _weather = new Weather3D(); AddChild(_weather);
            _ambient = new Ambient3D(); AddChild(_ambient); _ambient.Build(_map, _fx);
            _lamps = new Lamps3D(); AddChild(_lamps); _lamps.Build(_map);
            _director = new Director3D(); AddChild(_director); _director.Init(_fx);
            _vision = new Vision3D(); AddChild(_vision);

            Node3D Root(out Pool3D pool) { var r = new Node3D(); AddChild(r); pool = new Pool3D(r); return r; }
            _actorsRoot = Root(out _actors);
            _nodesRoot = Root(out _nodes);
            _deployRoot = Root(out _deploys);
            _bagsRoot = Root(out _bags);
            _pickRoot = Root(out _picks);
            _projRoot = Root(out _projs);
            _thrownRoot = Root(out _throwns);
            _fireRoot = Root(out _fires);

            _selfLight = new OmniLight3D { LightColor = new Color(1f, 0.9f, 0.75f), OmniRange = 7f, LightEnergy = 0f, ShadowEnabled = false };
            AddChild(_selfLight);

            _cam = new Cam3D();
            AddChild(_cam);
        }

        void BuildUi()
        {
            _hud = new Hud();
            AddChild(_hud);
            _inv = new InventoryWindow();
            _inv.Op += (op, arg) => _uiQ.Enqueue((op, arg, 0));
            _inv.ClickedOutside += CloseInv;
            _hud.AddChild(_inv);
            _buildMenu = new BuildMenu();
            _buildMenu.Picked += k => _setPiece = k;
            _buildMenu.Rejected += why => _hud.Toast(why);
            _hud.AddChild(_buildMenu);
            _hammerMenu = new BuildMenu();
            _hammerMenu.Picked += op => { _hammerOp = op; _hammerTarget = _hammerFocus; };
            _hammerMenu.Rejected += why => _hud.Toast(why);
            _hud.AddChild(_hammerMenu);
            _hud.CraftRequested += (i, n) => { _craftPendIdx = i; _craftPendLeft = n; };
            _hud.ResearchRequested += id => _researchReq = id;
            _hud.CodeEntered += (door, code) => _codeReq = (door, code);
            _hud.MenuClosed += SyncMouse;

            // карта (G) и миникарта — те же, что в 2D
            var monList = new List<(string, SVec, SVec)>();
            for (int i = 0; i < _map.Yards.Count; i++) monList.Add((_map.YardNames[i], _map.Yards[i].min, _map.Yards[i].max));
            _mapView = new MapView(_map, monList) { Visible = false };
            _mapView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _hud.AddChild(_mapView);
            _miniMap = new MiniMap(_mapView);
            _miniMap.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            _miniMap.OffsetLeft = -MiniMap.Side - 16; _miniMap.OffsetRight = -16; _miniMap.OffsetTop = 14; _miniMap.OffsetBottom = 14 + MiniMap.Side + 34;
            _hud.AddChild(_miniMap);

            // счётчик кадров (F10)
            _fpsLabel = new Label { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -MiniMap.Side - 16 - 60, OffsetRight = -16, OffsetTop = 14 + MiniMap.Side + 40, HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = Control.MouseFilterEnum.Ignore };
            _fpsLabel.AddThemeFontSizeOverride("font_size", 13);
            _fpsLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            _fpsLabel.AddThemeConstantOverride("outline_size", 4);
            _hud.AddChild(_fpsLabel);

            // панель Плана, подсказка по постройке, лента подборов, шпаргалка «как построить дом», радио
            _buildBar = new BuildBar { Visible = false };
            _buildBar.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
            _buildBar.OffsetLeft = -_buildBar.CustomMinimumSize.X / 2; _buildBar.OffsetRight = _buildBar.CustomMinimumSize.X / 2;
            _buildBar.OffsetTop = -300; _buildBar.OffsetBottom = -234;
            _hud.AddChild(_buildBar);
            _structTip = new StructTip();
            _hud.AddChild(_structTip);
            _feed = new PickupFeed();
            _feed.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
            _feed.OffsetLeft = UiTheme.Layout == 3 ? -_feed.CustomMinimumSize.X - 20 : Hotbar.BeltWidth / 2 + Hotbar.Gap; _feed.OffsetRight = _feed.OffsetLeft + _feed.CustomMinimumSize.X;
            _feed.OffsetBottom = -28; _feed.OffsetTop = -28 - _feed.CustomMinimumSize.Y;
            _hud.AddChild(_feed);
            _guide = new BuildGuidePanel();
            _hud.AddChild(_guide);
            _radio = new RadioFeed { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 0, OffsetLeft = -400, OffsetRight = 400, OffsetTop = 116, OffsetBottom = 230 };
            _hud.AddChild(_radio);
            _specLabel = new Label { Position = new Vector2(16, 14), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
            _specLabel.AddThemeFontSizeOverride("font_size", 15);
            _specLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            _specLabel.AddThemeConstantOverride("outline_size", 5);
            _hud.AddChild(_specLabel);

            // админка (F1): телепорты на РТ и комплекты одежды — как в 2D
            var tps = new List<(string, System.Action)>();
            foreach (var m in _server.World.Monuments)
            {
                var mon = m;
                tps.Add((mon.Name, () => { var me = _server.World.Find(_myId); if (me != null && !me.Dead) me.Pos = new SVec(mon.Center.X, mon.Max.Y + 5); }));
            }
            _hud.SetTeleports(tps);
            var gear = new List<(string, System.Action)>();
            foreach (var set in Clothing.Sets)
            {
                if (set.Key == "watch") continue;
                var st = set;
                gear.Add(($"{st.Name} ({st.Tier})", () =>
                {
                    var me = _server.World.Find(_myId);
                    for (int i = 0; i < me.Worn.Length; i++)
                        if (me.Worn[i] != null && me.Worn[i].Id != "wear_watch") { me.Add(me.Worn[i].Id, 1); me.Worn[i] = null; }
                    _server.World.Dress(me, st.Items.ToArray());
                    _hud.Toast($"Надет комплект «{st.Name}»");
                }));
            }
            gear.Add(("Антирадин ×5", () => { _server.World.Find(_myId).Add("antirad", 5); _hud.Toast("Антирадин ×5"); }));
            _hud.SetGear(gear);
            _hud.WipeRequested += () =>
            {
                // новый вайп по просьбе игрока: стираем сохранение и перезапускаем сцену
                _saveEnabled = false;
                string path = ProjectSettings.GlobalizePath(SavePath);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                Input.MouseMode = Input.MouseModeEnum.Hidden;
                GetTree().ReloadCurrentScene();
            };

            // настройки (Esc)
            _settings = new SettingsWindow { Visible = false };
            _hud.AddChild(_settings);
            _settings.Closed += SyncMouse;
            _settings.SaveRequested += () => _hud.Toast(SaveWorld() ? "Мир сохранён" : _saveEnabled ? "Сохранение НЕ записано: " + _saveError : "Сохранение выключено в этом запуске");
            _settings.WipeRequested += () => _hud.Wipe();
            _settings.FpsToggled += on => _fpsLabel.Visible = on;
            _settings.QualityChanged += ApplyQuality;
        }

        void SyncMouse()
        {
            bool m = AnyModal;
            if (m == _modalWas) return;
            _modalWas = m;
            Input.MouseMode = m ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Hidden;
        }

        void CloseInv() { _invOpen = false; _uiQ.Clear(); if (_cur?.Self?.Ui != null) _uiReq = (UiOp.Close, null, 0); }

        // ---------- цикл ----------

        public override void _Process(double delta)
        {
            float dt = (float)delta;
            UpdateAim(dt);

            _acc += delta * _timeScale;
            int steps = 0, maxSteps = 5 * _timeScale;
            while (_acc >= World.Dt && steps < maxSteps)
            {
                _prevActors.Clear();
                foreach (var a in _cur.Actors) _prevActors[a.Id] = a;
                _prevProjs.Clear();
                foreach (var p in _cur.Projectiles) _prevProjs[p.Id] = p;

                if (_craftReq < 0 && _craftPendLeft > 0) { _craftReq = _craftPendIdx; _craftPendLeft--; }
                if (_uiReq == null && _uiQ.Count > 0) _uiReq = _uiQ.Dequeue();
                _server.Submit(_myId, BuildCmd());
                _server.Tick();
                _cur = _server.BuildSnapshot(_myId);
                _decor.Sync(_cur);
                _reloadEdge = _interactEdge = _healEdge = _respawnEdge = _eatEdge = _debugEdge = _timeEdge = _bagEdge = false;
                _slot = _craftReq = -1;
                _codeReq = null;
                _uiReq = null;
                _setPiece = -1;
                _hammerOp = 0;
                _researchReq = null;
                HandleEvents();
                _acc -= World.Dt;
                steps++;
            }
            if (_acc > World.Dt * maxSteps) _acc = 0;

            _saveTimer += delta;
            if (_saveTimer >= 60) { _saveTimer = 0; SaveWorld(); }   // автосохранение раз в минуту (как в 2D)

            _frame++;
            float alpha = (float)(_acc / World.Dt);
            Render(alpha, dt);
            UpdateUi(dt);
            UpdateFps(dt);
        }

        void UpdateFps(float dt)
        {
            if (!_fpsLabel.Visible) return;
            _fpsAcc += dt; _fpsFrames++; _worstFrame = Mathf.Max(_worstFrame, dt);
            if (_fpsAcc < 0.5) return;
            _fpsLabel.Text = $"{_fpsFrames / _fpsAcc:0} FPS · худший {_worstFrame * 1000:0} мс · {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0} вызовов";
            _fpsAcc = 0; _fpsFrames = 0; _worstFrame = 0;
        }

        SVec SelfPos(float alpha)
        {
            foreach (var a in _cur.Actors)
                if (a.Id == _myId)
                    return _prevActors.TryGetValue(a.Id, out var p) ? SVec.Lerp(p.Pos, a.Pos, alpha) : a.Pos;
            return SVec.Zero;
        }

        void Render(float alpha, float dt)
        {
            var me = SelfPos(alpha);
            var me3 = W3.V(me);

            // свет: солнце по времени суток, ночью — личный свет у игрока (как фонарик у Zomboid)
            float night = World.NightFactorAt(_cur.DayTime);
            float day = 1f - night;
            _sun.RotationDegrees = new Vector3(-(12f + 50f * day), (_cur.DayTime - 0.5f) * 300f, 0);
            _sun.LightEnergy = 0.08f + 1.05f * day;
            _sun.LightColor = new Color(1f, 0.88f + 0.1f * day, 0.75f + 0.2f * day);
            _env.AmbientLightEnergy = 0.18f + 0.4f * day;
            _env.AmbientLightColor = new Color(0.62f, 0.68f, 0.75f).Lerp(new Color(0.25f, 0.3f, 0.45f), night);
            _env.BackgroundColor = new Color(0.55f, 0.65f, 0.72f).Lerp(new Color(0.04f, 0.05f, 0.08f), night);
            _env.FogLightColor = _env.BackgroundColor;
            _selfLight.LightEnergy = _spectate ? 0 : 0.9f * night;
            _selfLight.Position = me3 + new Vector3(0, 2.2f, 0);

            // погода: туман гуще в ливень, буран и туман; гроза — вспышки; снег ложится на землю
            float fog = _weather.Step(_cur, me3, dt);
            _env.FogDensity = 0.004f + fog;
            if (_weather.Flash > 0)
            {
                _env.AmbientLightEnergy += _weather.Flash * 2.5f;
                _sun.LightEnergy += _weather.Flash * 2f;
            }
            _terrain.SetWeather(_cur.SnowCover, _cur.Wetness);
            var wind = new Vector3(Mathf.Cos(_cur.WindAngle), 0, Mathf.Sin(_cur.WindAngle)) * _cur.WindStrength;
            _ambient.Step(me, night, _cur.Time, wind, dt);
            bool powered = _cur.DecorSprites.Exists(d => d.sprite == "props/subst_main_panel_on");
            _lamps.Step(night, powered, _cur.RadioPower, me, _cur.Time);
            _director.Sync(_cur.Markers, _cur.Time, dt);

            _structs.Sync(_cur);
            _static.SetGone(_cur.GoneWalls);
            _static.UpdateRoofs(me);
            _decor.Step(me);
            _veg.Step(me);
            SyncEntities(alpha, dt);
            SyncGhost();

            _cam.Step(me3, _aimWorld, _peek, dt);
            bool alive = _cur.Self != null && !_cur.Self.Dead;
            Mats3D.UpdateCut(_cam.Cam.GlobalPosition, me3 + new Vector3(0, 1.0f, 0), alive ? 3.2f : 0f);

            Acoustics.Update(me, _cur.Nodes, TerrainView.BiomeAt(_map, me), InYard(me), dt);
            UpdateVision(me, night);
        }

        /// <summary>Обзор: конус к курсору и лучи до стен (VisionRays — те же, что у 2D), наблюдатель видит всё.</summary>
        void UpdateVision(SVec me, float night)
        {
            var s = _cur.Self;
            bool on = s != null && !s.Dead && !_spectate;
            if (!on) { _vision.Set(me, SVec.UnitX, 0, null, false, night); return; }
            _rays ??= new VisionRays(_map);
            if (_structs.Version != _raysVersion) { _raysVersion = _structs.Version; _rays.Dynamic.Clear(); _rays.Dynamic.AddRange(_structs.Occluders); _raysFrame = -100; }
            float range = (50 + 30 * _peek) * (1f - 0.4f * night) * Mathf.Lerp(SkyInfo.ViewMul(_cur.WxPrev), SkyInfo.ViewMul(_cur.Sky), SkyInfo.CloudW(_cur.WxBlend));
            bool crouched = false;
            foreach (var a in _cur.Actors) if (a.Id == _myId) { crouched = a.Crouching; break; }
            if (_frame - _raysFrame >= 6 || SVec.DistanceSquared(me, _raysAt) > 0.0025f || crouched != _raysCrouch || Mathf.Abs(range - _raysRange) > 0.5f)
            {
                _rays.Update(me, range, _cur.Nodes, crouched);
                _raysFrame = _frame; _raysAt = me; _raysCrouch = crouched; _raysRange = range;
            }
            var dir = _aim - me;
            dir = dir.LengthSquared() > 0.01f ? SVec.Normalize(dir) : SVec.UnitX;
            _vision.Set(me, dir, Mathf.Cos(Mathf.DegToRad((130 - 70 * _peek) * 0.5f)), _rays.Texture, true, night);
        }

        bool InYard(SVec p)
        {
            foreach (var (mn, mx) in _map.Yards)
                if (p.X > mn.X + 2 && p.X < mx.X - 2 && p.Y > mn.Y + 2 && p.Y < mx.Y - 2) return true;
            return false;
        }

        void SyncEntities(float alpha, float dt)
        {
            _actors.Begin();
            foreach (var a in _cur.Actors)
            {
                bool me = a.Id == _myId;
                bool dressed = a.Worn != null && System.Array.Exists(a.Worn, w => !string.IsNullOrEmpty(w));
                var view = (Actor3D)_actors.Get(a.Id, (me ? "me" : a.IsNpc ? "npc" : "bot") + (dressed ? "+" : ""), () => Actor3D.Make(me, a.IsNpc, dressed));
                view.Visible = !(_spectate && me);   // наблюдателя в мире нет
                var pos = a.Pos; float facing = a.Facing;
                if (_prevActors.TryGetValue(a.Id, out var p))
                {
                    pos = SVec.Lerp(p.Pos, a.Pos, alpha);
                    facing = Mathf.LerpAngle(p.Facing, a.Facing, alpha);
                }
                view.Apply(a, pos, facing, dt, !me);
                if (view.Steps > 0 && view.Visible && !a.Dead && !a.Swimming) Footstep(a.Id, pos, a.Sprinting, a.Crouching, view.Steps);
            }
            _actors.End();

            _nodes.Begin();
            foreach (var n in _cur.Nodes)
            {
                var v = _nodes.Get(n.Id, $"{(int)n.Kind}:{n.Look}:{(int)(n.Fill * 4)}", () => Props3D.NodeView(n, TerrainView.BiomeAt(_map, n.Pos)));
                v.Position = W3.V(n.Pos);
            }
            _nodes.End();

            _deploys.Begin();
            foreach (var d in _cur.Deployables)
            {
                var v = _deploys.Get(d.Id, $"{(int)d.Kind}:{d.Burning}:{d.Seen}:{d.Rot}:{d.Half}", () => Props3D.Deploy(d));
                v.Position = W3.V(d.Pos, Pieces3D.FloorTop);
            }
            _deploys.End();

            _bags.Begin();
            foreach (var b in _cur.Bags)
            {
                var v = _bags.Get(b.Id, $"{b.IsCrate}:{b.Empty}:{b.Tier}:{b.Safe}:{b.Look}", () => Props3D.Bag(b));
                v.Position = W3.V(b.Pos);
            }
            _bags.End();

            _picks.Begin();
            foreach (var p in _cur.Pickups)
            {
                var v = _picks.Get(p.Id, $"{(int)p.Kind}", () => Props3D.Pickup(p));
                v.Position = W3.V(p.Pos);
                v.Visible = p.Available;
            }
            _picks.End();

            _projs.Begin();
            foreach (var p in _cur.Projectiles)
            {
                var v = _projs.Get(p.Id, p.Arrow ? "a" : "b", () => Props3D.Tracer(p.Arrow));
                var pos = _prevProjs.TryGetValue(p.Id, out var pp) ? SVec.Lerp(pp.Pos, p.Pos, alpha) : p.Pos;
                v.Position = W3.V(pos, 1.25f);
                v.Rotation = new Vector3(0, W3.Yaw(p.Dir.X, p.Dir.Y), 0);
            }
            _projs.End();

            _throwns.Begin();
            foreach (var t in _cur.Throwns)
            {
                var v = _throwns.Get(t.Id, t.ItemId ?? "", () => Props3D.Thrown(t.ItemId));
                v.Position = W3.V(t.Pos, t.Z + 0.1f);
            }
            _throwns.End();

            _fires.Begin();
            foreach (var f in _cur.Fires)
            {
                var v = _fires.Get(f.Id, $"{f.Radius:0.0}", () => Props3D.Fire(f.Radius));
                v.Position = W3.V(f.Pos);
            }
            _fires.End();
        }

        /// <summary>Шаг: звук по поверхности под ногой (пол постройки, дорога, площадка РТ, биом) — как у 2D.</summary>
        void Footstep(int id, SVec pos, bool sprint, bool crouch, int contacts)
        {
            if (SVec.Distance(pos, SelfPos(1f)) > Footsteps.HearRun + 5) return;
            var surf = SurfaceAt(pos);
            float vol = (id == _myId ? Tuning.StepsVolumeDb : -2) + (sprint ? 3 : 0) - (crouch ? 6 : 0);
            for (int i = 0; i < System.Math.Min(contacts, 2); i++)
                Footsteps.Play(surf, Px(pos), vol, crouch ? Footsteps.HearCrouch : sprint ? Footsteps.HearRun : Footsteps.HearWalk);
            if (!crouch && sprint && surf is Surface.Sand or Surface.Snow) _fx.Puff(W3.V(pos, 0.1f), new Color(0.75f, 0.7f, 0.6f, 0.35f), 0.4f, 0.8f, 0.3f, Vector3.Zero);
        }

        Surface SurfaceAt(SVec p)
        {
            int tier = _structs.FloorTier(p);
            if (tier >= 0) return tier <= 1 ? Surface.Wood : tier == 2 ? Surface.Concrete : Surface.Metal;
            for (int i = _map.Patches.Count - 1; i >= 0; i--)
            {
                var pt = _map.Patches[i];
                if (p.X < pt.Min.X || p.X > pt.Max.X || p.Y < pt.Min.Y || p.Y > pt.Max.Y) continue;
                return pt.Tile.Contains("gravel") || pt.Tile.Contains("dirt") ? Surface.Sand : Surface.Concrete;
            }
            if (InYard(p)) return Surface.Concrete;
            return TerrainView.BiomeAt(_map, p) switch
            {
                Biome.Tundra => Surface.Snow,
                Biome.Beach or Biome.Arid => Surface.Sand,
                Biome.Swamp => Surface.Mud,
                Biome.Water => Surface.None,
                _ => Surface.Grass,
            };
        }

        void SyncGhost()
        {
            var self = _cur.Self;
            if (self == null) return;
            StructSnap? target = null, door = null;
            int focus = _hammerMenu.Visible ? _hammerFocus : self.HoldingHammer && !self.Dead && !_inv.Visible ? self.HammerTarget : 0;
            if (focus != 0 && _structs.ById.TryGetValue(focus, out var t)) target = t;
            if (self.InteractDoor != 0 && !self.Dead && !_inv.Visible && _structs.ById.TryGetValue(self.InteractDoor, out var d)) door = d;
            _ghost.Sync(self, target, _hammerMode == World.HammerDemolish, self.HammerActionOk, target == null ? door : null);
        }

        // ---------- интерфейс ----------

        void UpdateUi(float dt)
        {
            var s = _cur.Self;
            if (s == null) return;
            _hud.DayTime = _cur.DayTime;
            _hud.MapOpen = _mapView.Visible;

            // карта и миникарта
            SVec mePos = SelfPos(1f); float meFacing = 0;
            foreach (var a in _cur.Actors) if (a.Id == _myId) { meFacing = a.Facing; break; }
            _mapView.SetState(mePos, meFacing, s.Home);
            _mapView.Markers = _cur.Markers;
            _mapView.Hot = _cur.Hot.ConvertAll(h => (h.pos, h.heat));
            _mapView.Time = _cur.Time;
            _miniMap.Visible = !_mapView.Visible && !s.Dead;
            if (_miniMap.Visible) _miniMap.SetState(mePos, meFacing, s.Home, s.Place, _cur.Structures, _cur.Deployables);
            _radio.Sync(_cur.Radio, dt);
            if (s.Place != _lastPlace) { _lastPlace = s.Place; if (_lastPlace != null) _hud.Toast(_lastPlace); }
            if (_spectate) UpdateSpectator();

            if (s.HoldingPlan) _buildMenu.Fill(s.BuildPieces);
            else _buildMenu.Close();
            bool hammerOpen = _hammerMenu.Visible && s.HoldingHammer && !s.Dead && s.HammerTarget == _hammerFocus;
            if (_hammerMenu.Visible && !hammerOpen) _hammerMenu.Close();
            if (hammerOpen && _structs.ById.TryGetValue(_hammerFocus, out var ht))
            {
                _hammerMenu.Fill(s.HammerWheel);
                _hammerMenu.Title = $"{Building.KindName(ht.Kind)} · {Building.TierNames[ht.Tier]} · {Mathf.CeilToInt(ht.Hp * Building.MaxHp(ht.Kind, ht.Tier))}/{(int)Building.MaxHp(ht.Kind, ht.Tier)}";
            }

            bool showInv = !s.Dead && (_invOpen || s.Ui != null);
            // E у верстака / станции — окно изучения или список рецептов (как в 2D)
            if (s.OpenCraft) { CloseInv(); showInv = false; if (s.OpenCraftList) _hud.OpenCraftList(); else _hud.OpenTech(); }
            if (s.Dead) { CloseInv(); _hud.CloseCraftAll(); showInv = false; }
            showInv &= !_hud.CraftOpen;
            _inv.Visible = showInv;
            _hud.InventoryOpen = showInv;
            if (showInv) _inv.Refresh(s, s.Ui, dt);
            if (_inv.Visible != _uiWasOpen) { _uiWasOpen = _inv.Visible; if (!_inv.Visible) _invOpen = false; }

            // лента подборов, подсказка по постройке (киянка — режим и что сделает ЛКМ), панель Плана, шпаргалка
            bool ui = AnyModal || _mapView.Visible;
            _feed.Update(s, dt);
            _feed.Visible = !_hud.CraftOpen && !_mapView.Visible && !_inv.Visible;
            bool hammerTip = s.HoldingHammer && s.HammerTarget != 0;
            _structTip.Action = s.HoldingHammer ? $"Режим: {HammerModeName(_hammerMode)} · " + (hammerTip || _hammerMode == World.HammerTake ? s.HammerAction : "наведи на постройку") : null;
            _structTip.ActionOk = s.HammerActionOk;
            StructSnap? tipFor = hammerTip ? (_structs.ById.TryGetValue(s.HammerTarget, out var tt) ? (StructSnap?)tt : null) : StructTip.Pick(_cur.Structures, _aim);
            _structTip.Set(s.Dead || ui || _buildMenu.Visible || _hammerMenu.Visible ? null : tipFor);
            _buildBar.Visible = s.HoldingPlan && !s.Dead && !_buildMenu.Visible && !ui;
            if (_buildBar.Visible) _buildBar.Set((int)s.BuildPiece, (int)s.GhostPiece);
            _guide.SetText(ui ? null : s.BuildGuide);
            _guide.Position = new Vector2(GetViewport().GetVisibleRect().Size.X - _guide.GetCombinedMinimumSize().X - 16, MiniMap.Side + 60);

            _hud.UpdateHud(s, dt, _peek > 0.05f ? "прицел" : "");

            // прицел: круг = реальный разброс на дистанции до курсора (как в 2D)
            var mouse = GetViewport().GetMousePosition();
            _hud.Crosshair.Position = mouse;
            var me3 = W3.V(SelfPos(1f), AimH);
            float dist = new Vector2(_aimWorld.X - me3.X, _aimWorld.Z - me3.Z).Length();
            float rM = Mathf.Tan(Mathf.DegToRad(s.SpreadDeg * 0.5f)) * dist;
            var p0 = _cam.Cam.UnprojectPosition(_aimWorld);
            var p1 = _cam.Cam.UnprojectPosition(_aimWorld + _cam.Right * rM);
            _hud.Crosshair.RadiusPx = (p1 - p0).Length();
            SyncMouse();
        }

        static string HammerModeName(int m) => m == World.HammerDemolish ? "снос" : m == World.HammerTake ? "забрать" : Building.TierNames[System.Math.Clamp(m, 0, Building.TierNames.Length - 1)];

        void UpdateSpectator()
        {
            _specLabel.Visible = true;
            string who = "свободная камера (WASD)";
            if (_followId != 0)
                foreach (var (id, line) in _server.BotLines())
                    if (id == _followId) { who = $"бот №{BotNum(id)}: {line}"; break; }
            _specLabel.Text = $"Наблюдатель · ×{_timeScale} · {who}";
        }

        List<int> BotOrder()
        {
            var ids = _server.BotLines().ConvertAll(b => b.id);
            ids.Sort();
            return ids;
        }

        int BotNum(int id) => BotOrder().IndexOf(id) + 1;

        bool SpectatorKey(Key key)
        {
            switch (key)
            {
                case Key.Tab:
                    var order = BotOrder();
                    if (order.Count == 0) return true;
                    _followId = order[(order.IndexOf(_followId) + 1) % order.Count];
                    _hud.Toast($"Бот №{BotNum(_followId)}");
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

        // ---------- ввод ----------

        void UpdateAim(float dt)
        {
            bool rmb = Input.IsMouseButtonPressed(MouseButton.Right);
            bool menu = (_cur?.Self?.HoldingPlan ?? false) || _hammerMenu.Visible || (_cur?.Self?.HoldingHammer ?? false);
            _peek = Mathf.MoveToward(_peek, rmb && !menu && !AnyModal ? 1f : 0f, dt * 5f);
            var hit = _cam.Cam != null ? _cam.GroundAt(GetViewport().GetMousePosition(), AimH) : null;
            if (hit is Vector3 h) { _aimWorld = h; _aim = new SVec(h.X, h.Z); }
        }

        SVec MoveVector()
        {
            if (_hud.CodeOpen) return SVec.Zero;
            static bool K(Key phys, Key alt1, Key alt2) => Input.IsPhysicalKeyPressed(phys) || Input.IsKeyPressed(alt1) || Input.IsKeyPressed(alt2);
            float x = 0, y = 0;
            if (K(Key.W, (Key)0x0426, Key.Up)) y += 1;
            if (K(Key.S, (Key)0x042B, Key.Down)) y -= 1;
            if (K(Key.A, (Key)0x0424, Key.Left)) x -= 1;
            if (K(Key.D, (Key)0x0412, Key.Right)) x += 1;
            if (x == 0 && y == 0) return SVec.Zero;
            // экранные «вверх/вправо» → мир с учётом поворота камеры
            var w = _cam.Right * x + _cam.Forward * y;
            var v = new SVec(w.X, w.Z);
            return v.LengthSquared() > 1f ? SVec.Normalize(v) : v;
        }

        InputCmd BuildCmd()
        {
            var self = _cur?.Self;
            bool blocked = AnyModal || _buildMenu.Visible || _hammerMenu.Visible || _mapView.Visible || _spectate;
            return new InputCmd
            {
                Move = MoveVector(),
                Aim = _aim,
                Fire = !blocked && Input.IsMouseButtonPressed(MouseButton.Left),
                Sprint = Input.IsPhysicalKeyPressed(Key.Shift),
                Crouch = Input.IsPhysicalKeyPressed(Key.Alt),
                Peek = _peek,
                Reload = _reloadEdge,
                Interact = _interactEdge,
                Heal = _healEdge,
                Eat = _eatEdge,
                Respawn = _respawnEdge,
                RespawnBag = _bagEdge,
                DebugKit = _debugEdge,
                ToggleTime = _timeEdge,
                SelectSlot = _slot,
                Craft = _craftReq,
                Research = _researchReq,
                Code = _codeReq?.code ?? -1,
                CodeDoor = _codeReq?.door ?? 0,
                UiOp = (int)(_uiReq?.op ?? UiOp.None),
                UiItem = _uiReq?.item,
                UiIndex = _uiReq?.index ?? 0,
                SetPiece = _setPiece,
                HammerFocus = _hammerMenu.Visible ? _hammerFocus : 0,
                HammerOp = _hammerOp,
                HammerTarget = _hammerTarget,
                HammerMode = self?.HoldingHammer == true ? _hammerMode : 0,
                Follow = _followId,
            };
        }

        public override void _UnhandledInput(InputEvent e)
        {
            if (e is InputEventKey k && k.Pressed && !k.Echo)
            {
                if (_buildMenu.PickByKey(k.PhysicalKeycode) || _hammerMenu.PickByKey(k.PhysicalKeycode)) return;
                if (k.PhysicalKeycode == Key.Escape && (_buildMenu.Visible || _hammerMenu.Visible)) { _buildMenu.Close(); _hammerMenu.Close(); return; }
                if (_hud.CodeOpen) return;   // вводим код замка — клавиши не наши
                if (_spectate && SpectatorKey(k.PhysicalKeycode)) return;
                if (_hud.CraftOpen && k.PhysicalKeycode is Key.Key1 or Key.Key2 or Key.Key3 or Key.Key4) { _hud.WorkbenchTab((int)(k.PhysicalKeycode - Key.Key1)); return; }
                switch (k.PhysicalKeycode)
                {
                    case Key.R: _reloadEdge = true; break;
                    case Key.E: _interactEdge = true; break;
                    case Key.H: _healEdge = true; break;
                    case Key.F: _eatEdge = true; break;
                    case Key.B: _bagEdge = true; break;
                    case Key.N: _timeEdge = true; break;
                    case Key.Enter: case Key.KpEnter: _respawnEdge = true; break;
                    case Key.Key1: _slot = 0; break;
                    case Key.Key2: _slot = 1; break;
                    case Key.Key3: _slot = 2; break;
                    case Key.Key4: _slot = 3; break;
                    case Key.Key5: _slot = 4; break;
                    case Key.Key6: _slot = 5; break;
                    case Key.Z: _cam.RotateBy(-1); break;
                    case Key.X: _cam.RotateBy(1); break;
                    case Key.F8: _server.World.GiveBuilderKit(_server.World.Find(_myId)); _hud.Toast("Набор строителя"); break;
                    case Key.F9: _debugEdge = true; break;
                    case Key.Q:
                        if (!_hud.CraftOpen) CloseInv();
                        _hud.ToggleCraft();
                        break;
                    case Key.Tab:
                        if (_inv.Visible) CloseInv();
                        else { if (_hud.CraftOpen) _hud.CloseCraftAll(); _invOpen = true; }
                        break;
                    case Key.G:
                        _mapView.Visible = !_mapView.Visible;
                        _mapView.QueueRedraw();
                        break;
                    case Key.F1: _hud.ToggleAdmin(); SyncMouse(); break;
                    case Key.F5: _hud.Toast(SaveWorld() ? "Мир сохранён" : _saveEnabled ? "Сохранение НЕ записано: " + _saveError : "Сохранение выключено в этом запуске"); break;
                    case Key.F10:
                        GameSettings.ShowFps = !GameSettings.ShowFps;
                        _fpsLabel.Visible = GameSettings.ShowFps;
                        GameSettings.Save();
                        break;
                    case Key.Escape when _mapView.Visible: _mapView.Visible = false; break;
                    case Key.Escape when _inv.Visible: CloseInv(); break;
                    case Key.Escape when _hud.CraftOpen: _hud.CloseCraftAll(); break;
                    case Key.Escape when _hud.AdminOpen: _hud.ToggleAdmin(); SyncMouse(); break;
                    case Key.Escape when _settings.Visible: _settings.Close(); break;
                    case Key.Escape:
                        _settings.Open();   // ничего не открыто — «Настройки»
                        SyncMouse();
                        break;
                }
            }
            else if (e is InputEventMouseButton rb && rb.ButtonIndex == MouseButton.Right && !rb.Pressed) { _buildMenu.Release(); _hammerMenu.Release(); }
            else if (e is InputEventMouseButton mb && mb.Pressed)
            {
                var self = _cur?.Self;
                if (mb.ButtonIndex == MouseButton.Right && self != null && !AnyModal)
                {
                    if (self.HoldingPlan) _buildMenu.Open();
                    else if (self.HoldingHammer && self.HammerTarget != 0 && !self.Dead)
                    {
                        _hammerFocus = self.HammerTarget;
                        _hammerMenu.Fill(self.HammerWheel);
                        _hammerMenu.Open();
                    }
                }
                bool wheel = mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown;
                int dir = mb.ButtonIndex == MouseButton.WheelDown ? 1 : -1;
                if (!wheel) return;
                if (self != null && self.HoldingHammer && !mb.CtrlPressed)
                {
                    int i = System.Array.IndexOf(HammerModes, _hammerMode), n = HammerModes.Length;
                    _hammerMode = HammerModes[((i + dir) % n + n) % n];
                    _hud.Toast("Киянка: " + (_hammerMode == World.HammerDemolish ? "снос" : _hammerMode == World.HammerTake ? "забрать (верстаки, печи, ящики)" : Building.TierNames[_hammerMode]));
                }
                else if (self != null && self.HoldingPlan && !mb.CtrlPressed)
                    _setPiece = (int)Building.NextPiece(self.BuildPiece, dir);
                else _cam.ZoomBy(dir > 0 ? 1.1f : 1f / 1.1f);
            }
            else if (e is InputEventMouseMotion mm && Input.IsMouseButtonPressed(MouseButton.Middle))
                _cam.DragRotate(mm.Relative.X);
        }

        // ---------- события симуляции: звук, вспышки, подсказки ----------

        void HandleEvents()
        {
            foreach (var e in _cur.Events)
            {
                var at = W3.V(e.Pos, 1.2f);
                switch (e.Kind)
                {
                    case SimEventKind.Shot:
                        if (e.Text is not ("weapon_bow" or "weapon_crossbow"))
                        {
                            var mz = W3.V(e.Pos + e.Dir * 0.7f, 1.25f);
                            _fx.Muzzle(mz);
                        }
                        if (_actors.TryGet(e.ActorId, out var sv) && sv is Actor3D sa) sa.Kick();
                        if (e.ActorId == _myId && e.Text != "weapon_bow") _cam.Shake = Mathf.Min(1.5f, _cam.Shake + 0.6f);
                        PlayShotSound(e.Text, e.Pos, e.OtherId > 0 ? Mathf.Clamp(e.OtherId / 100f, 0.2f, 1f) : 1f);
                        break;
                    case SimEventKind.HitActor:
                        _fx.Hit(W3.V(e.Pos, e.Text is "head" or "helmet" ? 1.6f : 1.1f), e.Text is not ("helmet" or "plate"));
                        Sfx.Play(e.Text switch { "head" => Sfx.Pick("hit_head", "hit_flesh"), "helmet" => Sfx.Pick("hit_helmet", "hit_metal_ore"), _ => "hit_flesh" }, Px(e.Pos), -2, 30, 0.1f);
                        if (e.OtherId == _myId) _hud.Crosshair.HitFlash = e.Text is "head" or "helmet" ? 0.3f : 0.15f;
                        if (e.ActorId == _myId) _hud.FlashDamage();
                        if (e.Text is not ("helmet" or "plate")) _decals.Blood(e.Pos);
                        break;
                    case SimEventKind.HitWall:
                    case SimEventKind.HitStruct:
                        _fx.Hit(at, false);
                        _decals.BulletHole(e.Pos, e.Dir, 1.2f);
                        Sfx.Play(e.Text is "камень" or "металл" or "сталь" ? "hit_metal_ore" : "hit_wood", Px(e.Pos), -2, 30, 0.1f);
                        break;
                    case SimEventKind.HitNode:
                    case SimEventKind.Gather:
                        Sfx.Play(NodeSound(e.OtherId), Px(e.Pos), -2, 30, 0.1f);
                        break;
                    case SimEventKind.Explosion:
                    {
                        bool big = e.Text == "expl_c4";
                        _fx.Explosion(W3.V(e.Pos), big);
                        _decals.Scorch(e.Pos, big);
                        _fx.Puff(W3.V(e.Pos, 1f), new Color(0.2f, 0.19f, 0.18f, 0.7f), big ? 3f : 2f, 4f, 1.2f, Vector3.Zero);
                        Sfx.Play(big ? "explosion_big" : "explosion_small", Px(e.Pos), 4, 300, 0.06f);
                        float d = SVec.Distance(e.Pos, SelfPos(1f));
                        _cam.Shake = Mathf.Min(2.5f, _cam.Shake + (big ? 2.2f : 1.4f) * Mathf.Clamp(1f - d / 40f, 0.15f, 1f));
                        break;
                    }
                    case SimEventKind.Demolished:
                        _fx.Dust(W3.V(e.Pos));
                        Sfx.Play("hit_wood", Px(e.Pos), -2, 30, 0.1f);
                        break;
                    case SimEventKind.Door:
                        Sfx.Play("hit_wood", Px(e.Pos), -8, 20, 0.2f);
                        break;
                    case SimEventKind.Built:
                        Sfx.Play(Sfx.Pick(e.Value <= 0 ? "build_place_0" : $"build_upgrade_{System.Math.Clamp((int)e.Value, 1, 4)}", e.Value >= 2 ? "hit_stone" : "hit_wood"), Px(e.Pos), -2, 30, 0.04f);
                        break;
                    case SimEventKind.Info:
                        _hud.Toast(e.Text);
                        break;
                    case SimEventKind.Crafted:
                        _hud.Toast("Скрафчено: " + e.Text);
                        Sfx.Play("craft_done", Px(e.Pos), -6, 10, 0f);
                        break;
                    case SimEventKind.Locked:
                    {
                        int mode = (int)e.Value;
                        Sfx.Play("lock_click", Px(e.Pos), mode == -2 ? 0 : -4, 10, 0f, mode == -2 ? 0.7f : mode == 2 ? 1.25f : 1f);
                        if (e.ActorId != _myId) break;
                        if (mode is 2 or -2) _hud.CodeResult(e.OtherId, mode == 2);
                        else { _hud.ShowCode(e.OtherId, mode); SyncMouse(); }
                        break;
                    }
                    case SimEventKind.Reload:
                    {
                        string wn = (e.Text ?? "").Replace("weapon_", "");
                        Sfx.Play(Sfx.Pick("reload_" + wn, "reload_ak"), Px(e.Pos), -2, 25, 0.02f);
                        if (Sfx.Get("bolt_" + wn) != null)
                        {
                            var bp = Px(e.Pos); float rt = Items.Get(e.Text)?.Weapon?.ReloadTime ?? 2f;
                            GetTree().CreateTimer(rt * 0.85f).Timeout += () => Sfx.Play("bolt_" + wn, bp, -2, 25, 0.02f);
                        }
                        break;
                    }
                    case SimEventKind.Swing:
                        if (_actors.TryGet(e.ActorId, out var swv) && swv is Actor3D swa) swa.Kick();
                        Sfx.Play("swing", Px(e.Pos), -6, 15, 0.15f);
                        break;
                    case SimEventKind.Destroyed:
                        _fx.Dust(W3.V(e.Pos));
                        _fx.Puff(W3.V(e.Pos, 1f), new Color(0.6f, 0.55f, 0.5f, 0.5f), 2.5f, 3f, 0.6f, Vector3.Zero);
                        Sfx.Play(Sfx.Pick($"build_destroy_{System.Math.Clamp((int)e.Value, 0, 4)}", (int)e.Value >= 2 ? "hit_stone" : "hit_wood"), Px(e.Pos), 2, 40, 0.05f);
                        break;
                    case SimEventKind.Wear:
                        if (e.Text == "plate_break") { Sfx.Play(Sfx.Pick("plate_break", "glass_break"), Px(e.Pos), -2, 30); if (e.ActorId == _myId) _hud.Toast("Пластина разбита"); }
                        else if (e.ActorId == _myId) Sfx.Play(Sfx.Pick(e.Text, e.Text == "wear_metal" ? "hit_metal_ore" : "rummage"), Px(e.Pos), -8, 8);
                        break;
                    case SimEventKind.Placed:
                        Sfx.Play("hit_wood", Px(e.Pos), -4, 20);
                        break;
                    case SimEventKind.Loot:
                        if (e.ActorId == _myId) { _hud.Toast("Залутано: " + e.Text); Sfx.Play("loot", Px(e.Pos), -4, 10); }
                        break;
                    case SimEventKind.Machine:
                        Machine(e);
                        break;
                    case SimEventKind.Death:
                        if (e.ActorId == _myId) _hud.Toast("Тебя убили. Enter — заново, B — в спальнике.");
                        else if (e.OtherId == _myId) _hud.Toast("Убит. Лут на трупе — подойди и нажми E.");
                        break;
                }
            }
        }

        /// <summary>Машины РТ: щиток, рубильник, роллеты, кардридер, ворота, тревога, трубы, кран, дизель, ракеты — звук и эффект, как в 2D.</summary>
        void Machine(SimEvent e)
        {
            var p = Px(e.Pos);
            var at = W3.V(e.Pos, 1.2f);
            if (e.Text != null && e.Text.StartsWith("станок:")) { Sfx.Play(Sfx.Pick("lathe_done", "craft_done"), p, -2, 30, 0.05f); return; }
            if (e.Text != null && e.Text.StartsWith("станция:")) { Sfx.Play(Sfx.Pick("station_done", "craft_done"), p, -8, 15, 0.05f); return; }
            if (e.Text == "pump") { Sfx.Play(Sfx.Pick("hand_pump", "hit_metal_ore"), p, -4, 40, 0.15f); return; }
            switch (e.Text)
            {
                case "fuse": Sfx.Play("lock_click", p, 0, 15, 0f); _fx.Hit(at, false); break;
                case "power": Sfx.Play("fuse_hiss", p, 2, 60, 0f); _fx.Muzzle(at); _cam.Shake = Mathf.Min(1f, _cam.Shake + 0.3f); break;
                case "shutter": Sfx.Play("hit_metal_ore", p, 0, 40, 0.1f); _fx.Dust(W3.V(e.Pos)); break;
                case "reader": Sfx.Play("c4_beep", p, 0, 60, 0f); _fx.Hit(W3.V(e.Pos, 1.1f), false); break;
                case "crate": Sfx.Play("craft_done", p, 2, 40, 0f); for (int i = 0; i < 6; i++) _fx.Puff(W3.V(e.Pos, 0.5f), new Color(0.4f, 0.9f, 0.45f, 0.55f), 1f, 3f, 1.5f, new Vector3(Mathf.Cos(i), 0, Mathf.Sin(i)) * 0.6f); break;
                case "pry": Sfx.Play("hit_metal_ore", p, -2, 25, 0.15f); _fx.Hit(at, false); break;
                case "glass": Sfx.Play("glass_break", p, -4, 30, 0.12f); _fx.Hit(at, false); break;
                case "beep": Sfx.Play("c4_beep", p, 4, 30, 0f); _fx.Hit(W3.V(e.Pos, 0.4f), true); break;
                case "gate": Sfx.Play("hit_metal_ore", p, 2, 40, 0.05f); _fx.Dust(W3.V(e.Pos)); break;
                case "alarm": Sfx.Play("c4_beep", p, 8, 70, 0.3f); _fx.Muzzle(W3.V(e.Pos, 2f)); break;
                case "pipe": Sfx.Play("fuse_hiss", p, 0, 45, 0.15f); for (int i = 0; i < 4; i++) _fx.Puff(W3.V(e.Pos, 0.8f), new Color(0.92f, 0.92f, 0.95f, 0.5f), 0.6f, 2f, 1.8f, Vector3.Zero); break;
                case "rail": Sfx.Play("hit_metal_ore", p, 0, 55, 0.2f); _fx.Dust(W3.V(e.Pos)); break;
                case "crane": Sfx.Play("hit_metal_ore", p, -6, 90, 0.3f, 0.6f); _fx.Dust(W3.V(e.Pos)); break;
                case "bang": Sfx.Play("hit_wood", p, 2, 40, 0.1f); _fx.Dust(W3.V(e.Pos)); _cam.Shake = Mathf.Min(0.6f, _cam.Shake + 0.15f); break;
                case "drop": _fx.Dust(W3.V(e.Pos)); _fx.Puff(W3.V(e.Pos, 0.3f), new Color(0.7f, 0.65f, 0.55f, 0.5f), 3f, 2f, 0.4f, Vector3.Zero); break;   // груз сброса приземлился
                case "flare": Sfx.Play(Sfx.Pick("flare_launch", "fire_whoosh"), p, 6, 400, 0.05f); _director.Flares.Add((e.Pos, _cur.Time)); break;
                case "diesel": Sfx.Play(Sfx.Pick("diesel_loop", "hit_metal_ore"), p, -6, 60, 0.2f, 0.5f); for (int i = 0; i < 3; i++) _fx.Puff(W3.V(e.Pos, 2f), new Color(0.12f, 0.12f, 0.12f, 0.6f), 1f, 3f, 1.5f, Vector3.Zero); break;
                case "power_off": Sfx.Play(Sfx.Pick("diesel_stop", "fuse_hiss"), p, -2, 60, 0f); break;
                case "paper": Sfx.Play(Sfx.Pick("paper_flip", "rummage"), p, -8, 8, 0.1f); break;
            }
        }

        /// <summary>Звук мира принимает пиксели 2D-проекции (Iso) — акустика сама переводит в метры.</summary>
        static Vector2 Px(SVec p) => Iso.Px(p);

        string NodeSound(int nodeId)
        {
            foreach (var n in _cur.Nodes)
                if (n.Id == nodeId)
                    return n.Kind switch { NodeKind.Tree => "hit_wood", NodeKind.Stone => "hit_stone", NodeKind.Barrel or NodeKind.Junk => "hit_metal_ore", _ => "hit_metal_ore" };
            return "hit_wood";
        }

        /// <summary>Копия Main.PlayShotSound (там она private): свой звук ствола, иначе — АК с поправкой на громкость.</summary>
        static void PlayShotSound(string weapon, SVec pos, float muf)
        {
            if (weapon == "weapon_bow") { Sfx.Play("shot_bow", Px(pos), -4, 25, 0.06f); return; }
            string own = "shot_" + (weapon ?? "").Replace("weapon_", "");
            bool loud = (Items.Get(weapon)?.Weapon?.NoiseRadius ?? 0) >= 70;
            float hear = Weaponry.ShotHearM(weapon) * muf, supDb = muf < 0.95f ? -8f : 0f;
            if (Sfx.Get(own) != null) { Sfx.Play(own, Px(pos), supDb, hear, 0.03f); return; }
            Sfx.Play("shot_ak", Px(pos), (loud ? 0 : -3) + supDb, hear, 0.03f, loud ? 1f : 1.12f);
        }
    }
}
