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
        readonly Dictionary<int, float> _stepAcc = new();
        CanvasModulate _daylight;
        bool _overview;
        float? _forcedDay;
        int _myId;
        Snapshot _cur;
        readonly Dictionary<int, ActorSnap> _prevActors = new();
        readonly Dictionary<int, ProjSnap> _prevProjs = new();
        double _acc;

        Node2D _world;
        Camera2D _cam;
        float _zoom = 2.0f;
        Hud _hud;

        readonly Dictionary<int, ActorView2D> _actorViews = new();
        readonly Dictionary<int, TracerView2D> _projViews = new();
        readonly Dictionary<int, BagView2D> _bagViews = new();
        readonly Dictionary<int, NodeView2D> _nodeViews = new();
        readonly Dictionary<int, PickupView2D> _pickupViews = new();
        readonly Dictionary<int, DeployView2D> _deployViews = new();
        readonly List<FlashView2D> _fx = new();
        readonly List<FloatText2D> _texts = new();
        GhostView2D _ghost;

        bool _reloadEdge, _interactEdge, _healEdge, _respawnEdge, _eatEdge, _debugEdge;
        int _slot = -1, _craftReq = -1;
        SVec _aim;
        Vector2 _screenDir;
        float _peek;
        float? _forcedPeek;
        Vector2 _autoScreen = new Vector2(800, 450);
        Vector2 _selfPx;

        string _shotPath;
        int _shotFrame = -1, _frame;
        SVec? _autoAim;
        readonly Dictionary<int, List<string>> _script = new();
        int _autoFireFrames;

        void RunScript()
        {
            if (!_script.TryGetValue(_frame, out var actions)) return;
            foreach (var a in actions)
            {
                if (a == "kit") _debugEdge = true;
                else if (a == "interact") _interactEdge = true;
                else if (a == "eat") _eatEdge = true;
                else if (a == "fire") _autoFireFrames = 4;
                else if (a == "hold") _autoFireFrames = 100000;
                else if (a == "release") _autoFireFrames = 0;
                else if (a == "craftmenu") _hud.ToggleCraft();
                else if (a.StartsWith("slot")) _slot = int.Parse(a.Substring(4)) - 1;
                else if (a.StartsWith("craft")) _craftReq = int.Parse(a.Substring(5));
            }
        }

        public override void _Ready()
        {
            DisplayServer.WindowSetTitle("Раствордд — прототип 0 (2D 3/4)");
            _server = new LocalServer(1337);
            _myId = _server.Connect("Игрок");
            ParseArgs();

            _map = _server.GetMap();
            BuildGround(_map);
            _world = new Node2D { YSortEnabled = true };
            AddChild(_world);
            BuildMap(_map);
            var flatDecals = new Node2D { ZIndex = -9 };
            AddChild(flatDecals);
            _decals = new DecalLayer(_map, flatDecals, _world);
            Sfx.Init(_world);
            _daylight = new CanvasModulate();
            AddChild(_daylight);

            _cam = new Camera2D { Zoom = new Vector2(_zoom, _zoom) };
            AddChild(_cam);
            _cam.MakeCurrent();

            _hud = new Hud();
            AddChild(_hud);
            _hud.CraftRequested += i => _craftReq = i;

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
                else if (arg.StartsWith("--spawn="))
                {
                    var p = arg.Substring(8).Split(',');
                    _server.World.Find(_myId).Pos = new SVec(float.Parse(p[0], inv), float.Parse(p[1], inv));
                }
                else if (arg == "--overview") _overview = true;
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
            foreach (var w in map.Walls) _world.AddChild(new WallView2D(w));
        }

        // ---------- ввод ----------

        public override void _UnhandledInput(InputEvent e)
        {
            if (e is InputEventKey k && k.Pressed && !k.Echo)
            {
                switch (k.PhysicalKeycode)
                {
                    case Key.R: _reloadEdge = true; break;
                    case Key.E: _interactEdge = true; break;
                    case Key.H: _healEdge = true; break;
                    case Key.Enter: case Key.KpEnter: _respawnEdge = true; break;
                    case Key.Key1: _slot = 0; break;
                    case Key.Key2: _slot = 1; break;
                    case Key.Key3: _slot = 2; break;
                    case Key.Key4: _slot = 3; break;
                    case Key.Key5: _slot = 4; break;
                    case Key.Key6: _slot = 5; break;
                    case Key.F: _eatEdge = true; break;
                    case Key.F9: _debugEdge = true; break;
                    case Key.Q:
                        _hud.ToggleCraft();
                        Input.MouseMode = _hud.CraftOpen ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Hidden;
                        break;
                    case Key.Tab: _hud.ToggleInventory(); break;
                    case Key.F12: SaveScreenshot($"user://shot_{Time.GetTicksMsec()}.png"); break;
                    case Key.Escape:
                        Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Hidden ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Hidden;
                        break;
                }
            }
            else if (e is InputEventMouseButton mb && mb.Pressed)
            {
                if (mb.ButtonIndex == MouseButton.WheelUp) _zoom = Mathf.Min(2.6f, _zoom * 1.1f);
                else if (mb.ButtonIndex == MouseButton.WheelDown) _zoom = Mathf.Max(0.9f, _zoom / 1.1f);
            }
        }

        InputCmd BuildCmd()
        {
            var move = SVec.Zero;
            if (Input.IsPhysicalKeyPressed(Key.W)) move.Y -= 1;
            if (Input.IsPhysicalKeyPressed(Key.S)) move.Y += 1;
            if (Input.IsPhysicalKeyPressed(Key.A)) move.X -= 1;
            if (Input.IsPhysicalKeyPressed(Key.D)) move.X += 1;
            return new InputCmd
            {
                Move = move,
                Aim = _aim,
                Fire = _autoFireFrames > 0 || (!_hud.CraftOpen && Input.IsMouseButtonPressed(MouseButton.Left)),   // клики по меню крафта не стреляют
                Eat = _eatEdge,
                DebugKit = _debugEdge,
                Craft = _craftReq,
                Sprint = Input.IsPhysicalKeyPressed(Key.Shift),
                Reload = _reloadEdge,
                Interact = _interactEdge,
                Heal = _healEdge,
                Respawn = _respawnEdge,
                SelectSlot = _slot,
                Peek = _peek,
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
            float peekTarget = _forcedPeek ?? (Input.IsMouseButtonPressed(MouseButton.Right) ? 1f : 0f);
            _peek = Mathf.MoveToward(_peek, peekTarget, dt * 5f);

            if (_autoAim.HasValue) { _aim = _autoAim.Value; return; }
            var m = GetGlobalMousePosition();
            _aim = new SVec(m.X / Iso.PPM, (m.Y + Iso.GunH) / Iso.PPM);
        }

        // ---------- цикл ----------

        public override void _Process(double delta)
        {
            _frame++;
            RunScript();
            UpdateAim((float)delta);

            _acc += delta;
            int steps = 0;
            while (_acc >= World.Dt && steps < 5)
            {
                _prevActors.Clear();
                foreach (var a in _cur.Actors) _prevActors[a.Id] = a;
                _prevProjs.Clear();
                foreach (var p in _cur.Projectiles) _prevProjs[p.Id] = p;

                _server.Submit(_myId, BuildCmd());
                _server.Tick();
                _cur = _server.BuildSnapshot(_myId);
                _reloadEdge = _interactEdge = _healEdge = _respawnEdge = _eatEdge = _debugEdge = false;
                _slot = _craftReq = -1;
                if (_autoFireFrames > 0) _autoFireFrames--;
                HandleEvents();
                _acc -= World.Dt;
                steps++;
            }
            if (steps == 5) _acc = 0;

            float alpha = (float)(_acc / World.Dt);
            float dt = (float)delta;
            Render(alpha, dt);
            UpdateCamera(dt);
            if (!_overview) _decals.Update(_cam.Position);
            _daylight.Color = _overview ? Colors.White : DayColor(_forcedDay ?? _cur.DayTime);
            UpdateHud(dt);

            if (_shotPath != null && _frame == _shotFrame)
            {
                SaveScreenshot(_shotPath);
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
                        Sfx.Play(e.Text == "weapon_bow" ? "shot_bow" : e.Text == "weapon_ak" ? "shot_ak" : "shot_pipe", Iso.Px(e.Pos),
                                 e.Text == "weapon_bow" ? -4 : 0, e.Text == "weapon_bow" ? 25 : 90, 0.06f);
                        break;
                    case SimEventKind.Swing:
                        if (_actorViews.TryGetValue(e.ActorId, out var swinger)) swinger.StartSwing();
                        Sfx.Play("swing", Iso.Px(e.Pos), -6, 15, 0.15f);
                        break;
                    case SimEventKind.HitNode:
                        if (_nodeViews.TryGetValue(e.OtherId, out var nv)) nv.Hit();
                        Sfx.Play(NodeHitSound(e.OtherId), Iso.Px(e.Pos), -2, 30, 0.1f);
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
                    case SimEventKind.Placed:
                        _hud.Toast("Установлено: " + e.Text);
                        Sfx.Play("hit_wood", Iso.Px(e.Pos), -4, 20);
                        break;
                    case SimEventKind.HitActor:
                        Flash(e.Pos, new Color(0.85f, 0.1f, 0.08f), 5, 0.15f);
                        Sfx.Play("hit_flesh", Iso.Px(e.Pos), -2, 30, 0.12f);
                        if (e.OtherId == _myId) _hud.Crosshair.HitFlash = 0.15f;
                        if (e.ActorId == _myId) _hud.FlashDamage();
                        break;
                    case SimEventKind.HitWall:
                        Flash(e.Pos, new Color(1, 0.85f, 0.5f), 3, 0.1f);
                        break;
                    case SimEventKind.Death:
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
        void Footstep(int id, SVec pos, float speed, float dt)
        {
            if (speed < 0.5f) return;
            _stepAcc.TryGetValue(id, out var acc);
            acc += speed * dt;
            if (acc >= 0.8f)
            {
                acc = 0;
                var b = TerrainView.BiomeAt(_map, pos);
                bool hard = b is Biome.Arid or Biome.Tundra or Biome.Beach || (pos.X > 4 && pos.X < 36 && pos.Y > -36 && pos.Y < -4);
                Sfx.Play(hard ? "step_gravel" : "step_grass", Iso.Px(pos), id == _myId ? -10 : -6, 18, 0.15f);
            }
            _stepAcc[id] = acc;
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
                    view = new ActorView2D(a.IsNpc, a.Id == _myId);
                    _world.AddChild(view);
                    _actorViews[a.Id] = view;
                }
                view.Visible = true;
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
                view.SetState(Iso.Px(pos), facing, a.Dead, a.Reloading, speed, dt, a.HeldId);
                if (!a.Dead) Footstep(a.Id, pos, speed, dt);
            }
            foreach (var kv in _actorViews)
                if (!seen.Contains(kv.Key)) kv.Value.Visible = false;

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
                    v = new BagView2D { IsCrate = b.IsCrate, Position = Iso.Px(b.Pos) };
                    _world.AddChild(v);
                    _bagViews[b.Id] = v;
                }
                v.Visible = true;
                if (v.Empty != b.Empty) { v.Empty = b.Empty; v.QueueRedraw(); }
            }
            foreach (var kv in _bagViews)
                if (!seen.Contains(kv.Key)) kv.Value.Visible = false;

            RenderWorldObjects(dt);

            for (int i = _texts.Count - 1; i >= 0; i--)
                if (!_texts[i].Tick(dt)) { _texts[i].QueueFree(); _texts.RemoveAt(i); }

            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                var f = _fx[i];
                f.Life -= dt;
                if (f.Life <= 0) { f.QueueFree(); _fx.RemoveAt(i); continue; }
                f.QueueRedraw();
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
                        : new OreView2D(Iso.Px(n.Pos), n.Kind, n.Radius, n.Id);
                    _world.AddChild(v);
                    _nodeViews[n.Id] = v;
                }
                if (Mathf.Abs(v.Fill - n.Fill) > 0.01f) { v.Fill = n.Fill; v.QueueRedraw(); }
                v.Tick(dt);
                // кроны над игроком становятся прозрачными
                if (v is TreeView2D t)
                    t.SetFade(head.DistanceSquaredTo(t.CanopyCenter) < t.CanopyR * t.CanopyR * 1.4f && _selfPx.Y < t.Base.Y + 4, dt);
            }
            RemoveMissing(_nodeViews, seen);

            seen.Clear();
            foreach (var p in _cur.Pickups)
            {
                seen.Add(p.Id);
                if (!_pickupViews.TryGetValue(p.Id, out var v))
                {
                    v = new PickupView2D(p.Kind) { Position = Iso.Px(p.Pos) };
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
                    v = new DeployView2D(d.Kind) { Position = Iso.Px(d.Pos) };
                    _world.AddChild(v);
                    _deployViews[d.Id] = v;
                }
                if (v.Burning != d.Burning) { v.Burning = d.Burning; v.QueueRedraw(); }
                v.Tick(dt);
            }
            RemoveMissing(_deployViews, seen);

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
            var chestPx = canvas * (_selfPx + new Vector2(0, -Iso.GunH));
            var dir = new Vector2(_aim.X, _aim.Y) - new Vector2(_selfPx.X / Iso.PPM, _selfPx.Y / Iso.PPM);
            _hud.SetVision(chestPx, dir.LengthSquared() > 0.01f ? dir.Normalized() : Vector2.Right,
                           2.5f * Iso.PPM * zoom, (50 + 30 * _peek) * (1f - 0.4f * World.NightFactorAt(_forcedDay ?? _cur.DayTime)) * Iso.PPM * zoom,
                           Mathf.Cos(Mathf.DegToRad((130 - 70 * _peek) * 0.5f)), !s.Dead);

            // прицел: круг = реальный разброс на дистанции до курсора
            var mouse = _autoAim.HasValue ? canvas * (Iso.Px(_aim) - new Vector2(0, Iso.GunH)) : GetViewport().GetMousePosition();
            _hud.Crosshair.Position = mouse;
            float dist = dir.Length();
            _hud.Crosshair.RadiusPx = Mathf.Tan(Mathf.DegToRad(s.SpreadDeg * 0.5f)) * dist * Iso.PPM * zoom;
        }
    }
}
