using System.Collections.Generic;
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
        readonly List<TreeView2D> _trees = new();
        readonly List<FlashView2D> _fx = new();

        bool _reloadEdge, _interactEdge, _healEdge, _respawnEdge;
        int _slot = -1;
        SVec _aim;
        Vector2 _screenDir;
        float _peek;
        float? _forcedPeek;
        Vector2 _autoScreen = new Vector2(800, 450);
        Vector2 _selfPx;

        string _shotPath;
        int _shotFrame = -1, _frame;
        SVec? _autoAim;

        public override void _Ready()
        {
            DisplayServer.WindowSetTitle("Раствордд — прототип 0 (2D 3/4)");
            _server = new LocalServer(1337);
            _myId = _server.Connect("Игрок");
            ParseArgs();

            BuildGround(_server.GetMap());
            _world = new Node2D { YSortEnabled = true };
            AddChild(_world);
            BuildMap(_server.GetMap());

            _cam = new Camera2D { Zoom = new Vector2(_zoom, _zoom) };
            AddChild(_cam);
            _cam.MakeCurrent();

            _hud = new Hud();
            AddChild(_hud);

            _cur = _server.BuildSnapshot(_myId);
            Input.MouseMode = Input.MouseModeEnum.Hidden;
            _hud.Toast("Вайп начался. Ты на пляже с самопалом. РТ — на юго-востоке.");
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
                else if (arg.StartsWith("--zoom=")) _zoom = float.Parse(arg.Substring(7), inv);
                else if (arg.StartsWith("--peek=")) _forcedPeek = float.Parse(arg.Substring(7), inv);
                else if (arg.StartsWith("--screen="))
                {
                    var p = arg.Substring(9).Split(',');
                    _autoScreen = new Vector2(float.Parse(p[0], inv), float.Parse(p[1], inv));
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

            ground.AddChild(new ColorRect { Color = new Color(0.13f, 0.28f, 0.36f), Position = new Vector2(-half - 3000, -half - 3000), Size = new Vector2(size + 6000, size + 6000) });

            var noise = new NoiseTexture2D
            {
                Width = 512, Height = 512, Seamless = true,
                Noise = new FastNoiseLite { Frequency = 0.01f, FractalOctaves = 4 },
                ColorRamp = new Gradient { Colors = new[] { new Color(0.44f, 0.5f, 0.3f), new Color(0.56f, 0.6f, 0.37f) } },
            };
            ground.AddChild(new TextureRect
            {
                Texture = noise, StretchMode = TextureRect.StretchModeEnum.Tile,
                Position = new Vector2(-half, -half), Size = new Vector2(size, size),
            });

            var sand = new Color(0.78f, 0.72f, 0.55f);
            float b = 4 * Iso.PPM;
            ground.AddChild(new ColorRect { Color = sand, Position = new Vector2(-half, -half), Size = new Vector2(size, b) });
            ground.AddChild(new ColorRect { Color = sand, Position = new Vector2(-half, half - b), Size = new Vector2(size, b) });
            ground.AddChild(new ColorRect { Color = sand, Position = new Vector2(-half, -half), Size = new Vector2(b, size) });
            ground.AddChild(new ColorRect { Color = sand, Position = new Vector2(half - b, -half), Size = new Vector2(b, size) });

            // площадка РТ
            ground.AddChild(new ColorRect { Color = new Color(0.45f, 0.45f, 0.43f), Position = Iso.Px(new SVec(5, -35)), Size = new Vector2(30, 30) * Iso.PPM });
        }

        void BuildMap(MapInfo map)
        {
            foreach (var w in map.Walls)
            {
                if (w.Kind == WallKind.Tree)
                {
                    var t = new TreeView2D(w);
                    _world.AddChild(t);
                    _trees.Add(t);
                }
                else _world.AddChild(new WallView2D(w));
            }
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
                Fire = Input.IsMouseButtonPressed(MouseButton.Left),
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
                _reloadEdge = _interactEdge = _healEdge = _respawnEdge = false;
                _slot = -1;
                HandleEvents();
                _acc -= World.Dt;
                steps++;
            }
            if (steps == 5) _acc = 0;

            float alpha = (float)(_acc / World.Dt);
            float dt = (float)delta;
            Render(alpha, dt);
            UpdateCamera(dt);
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
                        Flash(e.Pos + e.Dir * 0.3f, new Color(1, 0.8f, 0.4f), 7, 0.05f);
                        break;
                    case SimEventKind.HitActor:
                        Flash(e.Pos, new Color(0.85f, 0.1f, 0.08f), 5, 0.15f);
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
                        if (e.ActorId == _myId) _hud.Toast("Залутано: " + e.Text);
                        break;
                    case SimEventKind.Respawn:
                        if (e.ActorId == _myId) _hud.Toast("Проснулся на пляже. Голый, но с самопалом.");
                        break;
                }
            }
        }

        void Flash(SVec pos, Color c, float radius, float life)
        {
            var f = new FlashView2D { Position = Iso.Px(pos), Color = c, Radius = radius, Life = life, Max = life };
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
                view.SetState(Iso.Px(pos), facing, a.Dead, a.Reloading, speed, dt);
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
                    v = new TracerView2D { Dir = new Vector2(p.Dir.X, p.Dir.Y) };
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

            // кроны над игроком становятся прозрачными
            var head = _selfPx + new Vector2(0, -Iso.H(1.4f));
            foreach (var t in _trees)
                t.SetFade(head.DistanceSquaredTo(t.CanopyCenter) < t.CanopyR * t.CanopyR * 1.4f && _selfPx.Y < t.Position.Y + 4, dt);

            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                var f = _fx[i];
                f.Life -= dt;
                if (f.Life <= 0) { f.QueueFree(); _fx.RemoveAt(i); continue; }
                f.QueueRedraw();
            }
        }

        void UpdateCamera(float dt)
        {
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
            _hud.UpdateHud(s, dt, _peek > 0.05f
                ? "Прицеливание · обзор уже, видно дальше, разброс меньше, шаг медленнее"
                : "Вид 3/4 · 2D · заглушки · ПКМ — прицелиться");

            var canvas = GetViewport().GetCanvasTransform();
            float zoom = _cam.Zoom.X;
            var chestPx = canvas * (_selfPx + new Vector2(0, -Iso.GunH));
            var dir = new Vector2(_aim.X, _aim.Y) - new Vector2(_selfPx.X / Iso.PPM, _selfPx.Y / Iso.PPM);
            _hud.SetVision(chestPx, dir.LengthSquared() > 0.01f ? dir.Normalized() : Vector2.Right,
                           2.5f * Iso.PPM * zoom, (50 + 30 * _peek) * Iso.PPM * zoom,
                           Mathf.Cos(Mathf.DegToRad((130 - 70 * _peek) * 0.5f)), !s.Dead);

            // прицел: круг = реальный разброс на дистанции до курсора
            var mouse = _autoAim.HasValue ? canvas * (Iso.Px(_aim) - new Vector2(0, Iso.GunH)) : GetViewport().GetMousePosition();
            _hud.Crosshair.Position = mouse;
            float dist = dir.Length();
            _hud.Crosshair.RadiusPx = Mathf.Tan(Mathf.DegToRad(s.SpreadDeg * 0.5f)) * dist * Iso.PPM * zoom;
        }
    }
}
