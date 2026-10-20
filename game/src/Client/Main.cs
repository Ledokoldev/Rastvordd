using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>
    /// Клиент прототипа: собирает ввод, отправляет его локальному серверу и рисует снапшоты.
    /// В мир напрямую не лезет — только через LocalServer (как будет по сети).
    /// </summary>
    public partial class Main : Node3D
    {
        const float AimHeight = 1.2f;

        LocalServer _server;
        int _myId;
        Snapshot _cur;
        readonly Dictionary<int, ActorSnap> _prevActors = new();
        readonly Dictionary<int, ProjSnap> _prevProjs = new();
        double _acc;

        Camera3D _cam;
        Vector3 _camTarget;
        bool _steepCam;
        float _zoom = 24;
        ShaderMaterial _visionMat;

        readonly Dictionary<int, ActorView> _actorViews = new();
        readonly Dictionary<int, MeshInstance3D> _projViews = new();
        readonly Dictionary<int, MeshInstance3D> _bagViews = new();
        readonly List<(Node3D node, float life, float max)> _fx = new();

        // ввод
        bool _reloadEdge, _interactEdge, _healEdge, _respawnEdge;
        int _slot = -1;
        SVec _aim;

        // интерфейс
        Label _hpLabel, _ammoLabel, _hintLabel, _toastLabel, _promptLabel, _invLabel, _deathLabel, _camLabel;
        ColorRect _hpFill, _damageRect, _deathRect;
        PanelContainer _invPanel;
        Crosshair _crosshair;
        float _toastTime, _damageFlash;

        // отладка / скриншоты
        string _shotPath;
        int _shotFrame = -1, _frame;

        static Vector3 V3(SVec v, float y = 0) => new Vector3(v.X, y, v.Y);

        public override void _Ready()
        {
            DisplayServer.WindowSetTitle("Раствордд — прототип 0");
            _server = new LocalServer(1337);
            _myId = _server.Connect("Игрок");
            ParseArgs();

            BuildEnvironment();
            BuildMap(_server.GetMap());
            BuildUi();

            _cur = _server.BuildSnapshot(_myId);
            Input.MouseMode = Input.MouseModeEnum.Hidden;
            Toast("Вайп начался. Ты на пляже с самопалом. РТ — на юго-востоке.");
        }

        void ParseArgs()
        {
            foreach (var arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--shot=")) _shotPath = arg.Substring(7);
                else if (arg.StartsWith("--frames=")) _shotFrame = int.Parse(arg.Substring(9));
                else if (arg == "--steep") _steepCam = true;
                else if (arg.StartsWith("--spawn="))
                {
                    var p = arg.Substring(8).Split(',');
                    var me = _server.World.Find(_myId);
                    me.Pos = new SVec(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                                      float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        // ---------- построение сцены ----------

        void BuildEnvironment()
        {
            var env = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.16f, 0.2f, 0.24f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.6f, 0.68f),
                AmbientLightEnergy = 0.55f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                SsaoEnabled = true,
                GlowEnabled = true,
            };
            AddChild(new WorldEnvironment { Environment = env });

            var sun = new DirectionalLight3D
            {
                ShadowEnabled = true,
                LightEnergy = 1.15f,
                LightColor = new Color(1f, 0.93f, 0.82f),
                RotationDegrees = new Vector3(-58, 30, 0),
            };
            AddChild(sun);

            _cam = new Camera3D { Fov = 42, Far = 300 };
            AddChild(_cam);
        }

        void BuildMap(MapInfo map)
        {
            float size = map.HalfSize * 2;

            var ocean = new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(500, 500) },
                MaterialOverride = Visuals.Mat(new Color(0.12f, 0.27f, 0.35f), 0.3f),
                Position = new Vector3(0, -0.15f, 0),
            };
            AddChild(ocean);

            var noise = new NoiseTexture2D
            {
                Width = 512, Height = 512, Seamless = true,
                Noise = new FastNoiseLite { Frequency = 0.012f, FractalOctaves = 4 },
                ColorRamp = new Gradient { Colors = new[] { new Color(0.78f, 0.8f, 0.74f), Colors.White } },
            };
            var groundMat = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.52f, 0.56f, 0.36f),
                AlbedoTexture = noise,
                Uv1Scale = new Vector3(6, 6, 1),
                Roughness = 1,
            };
            AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(size, size) }, MaterialOverride = groundMat });

            // пляж вдоль края
            var sandMat = Visuals.Mat(new Color(0.78f, 0.72f, 0.55f));
            float b = 4;
            foreach (var (pos, sz) in new[]
            {
                (new Vector3(0, 0.01f, -map.HalfSize + b / 2), new Vector3(size, 0.02f, b)),
                (new Vector3(0, 0.01f, map.HalfSize - b / 2), new Vector3(size, 0.02f, b)),
                (new Vector3(-map.HalfSize + b / 2, 0.01f, 0), new Vector3(b, 0.02f, size)),
                (new Vector3(map.HalfSize - b / 2, 0.01f, 0), new Vector3(b, 0.02f, size)),
            })
            {
                var m = Visuals.Box(sz, sandMat);
                m.Position = pos;
                AddChild(m);
            }

            // площадка РТ
            var pad = Visuals.Box(new Vector3(30, 0.03f, 30), Visuals.Mat(new Color(0.42f, 0.42f, 0.4f)));
            pad.Position = new Vector3(20, 0.015f, -20);
            AddChild(pad);

            var concrete = Visuals.Mat(new Color(0.58f, 0.58f, 0.56f));
            var crate = Visuals.Mat(new Color(0.5f, 0.36f, 0.2f));
            var rock = Visuals.Mat(new Color(0.48f, 0.47f, 0.45f));
            var trunk = Visuals.Mat(new Color(0.36f, 0.26f, 0.17f));
            var leaves = Visuals.Mat(new Color(0.22f, 0.38f, 0.2f));

            foreach (var w in map.Walls)
            {
                var c = (w.Min + w.Max) * 0.5f;
                var s = w.Max - w.Min;
                switch (w.Kind)
                {
                    case WallKind.Tree:
                        var t = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = s.X * 0.4f, BottomRadius = s.X * 0.5f, Height = 2.6f }, MaterialOverride = trunk };
                        t.Position = new Vector3(c.X, 1.3f, c.Y);
                        AddChild(t);
                        var crown = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1.25f, Height = 2.2f }, MaterialOverride = leaves };
                        crown.Position = new Vector3(c.X, 3.2f, c.Y);
                        AddChild(crown);
                        break;
                    default:
                        var mat = w.Kind == WallKind.Crate ? crate : w.Kind == WallKind.Rock ? rock : concrete;
                        var box = Visuals.Box(new Vector3(s.X, w.Height, s.Y), mat);
                        box.Position = new Vector3(c.X, w.Height / 2, c.Y);
                        AddChild(box);
                        break;
                }
            }

            // затемнение вне обзора
            _visionMat = new ShaderMaterial { Shader = new Shader { Code = VisionShader } };
            var vision = new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(500, 500) },
                MaterialOverride = _visionMat,
                Position = new Vector3(0, 0.05f, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(vision);
        }

        const string VisionShader = @"
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;
uniform vec2 player_pos;
uniform vec2 facing_dir = vec2(1.0, 0.0);
uniform float cos_half = 0.42;
uniform float near_r = 2.5;
uniform float far_r = 50.0;
uniform float darkness = 0.6;
varying vec3 wpos;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    vec2 d = wpos.xz - player_pos;
    float dist = length(d);
    float c = dot(d / max(dist, 0.001), facing_dir);
    float cone = smoothstep(cos_half - 0.06, cos_half + 0.03, c) * (1.0 - smoothstep(far_r - 6.0, far_r, dist));
    float nearv = 1.0 - smoothstep(near_r - 0.4, near_r + 0.6, dist);
    float vis = max(cone, nearv);
    ALBEDO = vec3(0.01, 0.02, 0.05);
    ALPHA = (1.0 - vis) * darkness;
}";

        void BuildUi()
        {
            var ui = new CanvasLayer();
            AddChild(ui);

            _damageRect = new ColorRect { Color = new Color(0.8f, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
            _damageRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            ui.AddChild(_damageRect);

            _hintLabel = MakeLabel(ui, new Vector2(16, 12), 15, new Color(1, 1, 1, 0.75f));
            _hintLabel.Text = "WASD — ходьба · Shift — бег · ЛКМ — стрелять · R — перезарядка · E — лутать · H — бинт\n1/2/3 — оружие · Tab — инвентарь · V — угол камеры · колесо — зум · Esc — курсор";
            _camLabel = MakeLabel(ui, new Vector2(16, 58), 15, new Color(1, 0.9f, 0.6f, 0.85f));

            var hpBack = new ColorRect { Color = new Color(0, 0, 0, 0.55f), Size = new Vector2(260, 26), MouseFilter = Control.MouseFilterEnum.Ignore };
            hpBack.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
            hpBack.Position = new Vector2(20, -50);
            ui.AddChild(hpBack);
            _hpFill = new ColorRect { Color = new Color(0.45f, 0.75f, 0.3f), Size = new Vector2(260, 26), MouseFilter = Control.MouseFilterEnum.Ignore };
            hpBack.AddChild(_hpFill);
            _hpLabel = MakeLabel(hpBack, new Vector2(8, 2), 16, Colors.White);

            _ammoLabel = MakeLabel(ui, Vector2.Zero, 22, Colors.White);
            _ammoLabel.HorizontalAlignment = HorizontalAlignment.Right;

            _promptLabel = MakeLabel(ui, Vector2.Zero, 20, new Color(1, 0.9f, 0.5f));
            _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;

            _toastLabel = MakeLabel(ui, Vector2.Zero, 20, Colors.White);
            _toastLabel.HorizontalAlignment = HorizontalAlignment.Center;

            _invPanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
            _invPanel.Position = new Vector2(-40, -150);
            ui.AddChild(_invPanel);
            _invLabel = new Label();
            _invLabel.AddThemeFontSizeOverride("font_size", 17);
            _invPanel.AddChild(_invLabel);

            _deathRect = new ColorRect { Color = new Color(0.25f, 0, 0, 0.55f), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
            _deathRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            ui.AddChild(_deathRect);
            _deathLabel = MakeLabel(_deathRect, Vector2.Zero, 30, Colors.White);
            _deathLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _deathLabel.Text = "ТЕБЯ УБИЛИ\nвесь лут остался на трупе\n\nEnter — проснуться на пляже";

            _crosshair = new Crosshair { MouseFilter = Control.MouseFilterEnum.Ignore };
            ui.AddChild(_crosshair);
        }

        static Label MakeLabel(Node parent, Vector2 pos, int size, Color color)
        {
            var l = new Label { Position = pos, MouseFilter = Control.MouseFilterEnum.Ignore };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", color);
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            l.AddThemeConstantOverride("outline_size", 5);
            parent.AddChild(l);
            return l;
        }

        /// <summary>Ставит элемент по долям экрана (0 — лево/верх, 1 — право/низ) с отступом.</summary>
        static void Place(Control c, Vector2 screen, float ax, float ay, Vector2 offset)
        {
            c.Size = c.GetCombinedMinimumSize();
            c.Position = new Vector2((screen.X - c.Size.X) * ax, (screen.Y - c.Size.Y) * ay) + offset;
        }

        void Toast(string text)
        {
            _toastLabel.Text = text;
            _toastTime = 3.5f;
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
                    case Key.Tab: _invPanel.Visible = !_invPanel.Visible; break;
                    case Key.V: _steepCam = !_steepCam; break;
                    case Key.F12: SaveScreenshot($"user://shot_{Time.GetTicksMsec()}.png"); break;
                    case Key.Escape:
                        Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Hidden ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Hidden;
                        break;
                }
            }
            else if (e is InputEventMouseButton mb && mb.Pressed)
            {
                if (mb.ButtonIndex == MouseButton.WheelUp) _zoom = Mathf.Max(14, _zoom - 1.5f);
                else if (mb.ButtonIndex == MouseButton.WheelDown) _zoom = Mathf.Min(40, _zoom + 1.5f);
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
            };
        }

        void UpdateAim()
        {
            var mouse = GetViewport().GetMousePosition();
            var o = _cam.ProjectRayOrigin(mouse);
            var n = _cam.ProjectRayNormal(mouse);
            if (Mathf.Abs(n.Y) < 1e-4f) return;
            float t = (AimHeight - o.Y) / n.Y;
            var p = o + n * t;
            _aim = new SVec(p.X, p.Z);
        }

        // ---------- цикл ----------

        public override void _Process(double delta)
        {
            _frame++;
            UpdateAim();

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
            Render(alpha, (float)delta);
            UpdateUi((float)delta);

            if (_shotPath != null && _frame == _shotFrame)
            {
                SaveScreenshot(_shotPath);
                GetTree().Quit();
            }
        }

        void SaveScreenshot(string path)
        {
            var img = GetViewport().GetTexture().GetImage();
            img.SavePng(path);
            GD.Print($"screenshot: {ProjectSettings.GlobalizePath(path)}");
        }

        void HandleEvents()
        {
            foreach (var e in _cur.Events)
            {
                switch (e.Kind)
                {
                    case SimEventKind.Shot:
                        var light = new OmniLight3D { LightColor = new Color(1, 0.75f, 0.35f), LightEnergy = 4, OmniRange = 5, Position = V3(e.Pos, AimHeight) };
                        AddChild(light);
                        _fx.Add((light, 0.06f, 0.06f));
                        break;
                    case SimEventKind.HitActor:
                        Spark(e.Pos, new Color(0.9f, 0.1f, 0.08f), 0.18f);
                        if (e.OtherId == _myId) _crosshair.HitFlash = 0.15f;
                        if (e.ActorId == _myId) _damageFlash = 0.45f;
                        break;
                    case SimEventKind.HitWall:
                        Spark(e.Pos, new Color(1, 0.85f, 0.5f), 0.1f);
                        break;
                    case SimEventKind.Death:
                        if (e.ActorId == _myId) Toast("Тебя убили.");
                        else if (e.OtherId == _myId) Toast("Убит. Лут на трупе — подойди и нажми E.");
                        break;
                    case SimEventKind.Loot:
                        if (e.ActorId == _myId) Toast("Залутано: " + e.Text);
                        break;
                    case SimEventKind.Respawn:
                        if (e.ActorId == _myId) Toast("Проснулся на пляже. Голый, но с самопалом.");
                        break;
                }
            }
        }

        void Spark(SVec pos, Color c, float size)
        {
            var m = new MeshInstance3D { Mesh = new SphereMesh { Radius = size, Height = size * 2 }, MaterialOverride = Visuals.Glow(c, 4) };
            m.Position = V3(pos, AimHeight);
            AddChild(m);
            _fx.Add((m, 0.12f, 0.12f));
        }

        void Render(float alpha, float dt)
        {
            SVec selfPos = SVec.Zero;
            var seen = new HashSet<int>();
            foreach (var a in _cur.Actors)
            {
                seen.Add(a.Id);
                if (!_actorViews.TryGetValue(a.Id, out var view))
                {
                    view = new ActorView(a.IsNpc, a.Id == _myId);
                    AddChild(view);
                    _actorViews[a.Id] = view;
                }
                view.Visible = true;
                var pos = a.Pos;
                float facing = a.Facing;
                if (_prevActors.TryGetValue(a.Id, out var prev) && SVec.Distance(prev.Pos, a.Pos) < 3)
                {
                    pos = SVec.Lerp(prev.Pos, a.Pos, alpha);
                    facing = Mathf.LerpAngle(prev.Facing, a.Facing, alpha);
                }
                if (a.Id == _myId)
                {
                    selfPos = pos;
                    var d = _aim - pos;
                    if (d.LengthSquared() > 0.04f) facing = Mathf.Atan2(d.Y, d.X);
                }
                view.SetState(V3(pos), Visuals.YawFor(Mathf.Cos(facing), Mathf.Sin(facing)), a.Dead, a.Reloading);
            }
            foreach (var kv in _actorViews)
                if (!seen.Contains(kv.Key)) kv.Value.Visible = false;

            // пули
            seen.Clear();
            foreach (var p in _cur.Projectiles)
            {
                seen.Add(p.Id);
                if (!_projViews.TryGetValue(p.Id, out var m))
                {
                    m = Visuals.Box(new Vector3(0.05f, 0.05f, 1.4f), Visuals.Glow(new Color(1, 0.85f, 0.4f), 5));
                    m.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                    AddChild(m);
                    _projViews[p.Id] = m;
                }
                var pos = _prevProjs.TryGetValue(p.Id, out var prev) ? SVec.Lerp(prev.Pos, p.Pos, alpha) : p.Pos;
                m.Position = V3(pos, AimHeight);
                m.Rotation = new Vector3(0, Visuals.YawFor(p.Dir.X, p.Dir.Y), 0);
            }
            var dead = new List<int>();
            foreach (var kv in _projViews)
                if (!seen.Contains(kv.Key)) { kv.Value.QueueFree(); dead.Add(kv.Key); }
            foreach (var id in dead) _projViews.Remove(id);

            // сумки и ящики
            seen.Clear();
            foreach (var b in _cur.Bags)
            {
                seen.Add(b.Id);
                if (!_bagViews.TryGetValue(b.Id, out var m))
                {
                    m = b.IsCrate
                        ? Visuals.Box(new Vector3(1.1f, 0.6f, 0.65f), Visuals.Mat(new Color(0.3f, 0.38f, 0.22f)))
                        : Visuals.Box(new Vector3(0.55f, 0.35f, 0.45f), Visuals.Mat(new Color(0.45f, 0.32f, 0.18f)));
                    m.Position = V3(b.Pos, b.IsCrate ? 0.3f : 0.18f);
                    AddChild(m);
                    _bagViews[b.Id] = m;
                }
                m.Visible = true;
                ((StandardMaterial3D)m.MaterialOverride).AlbedoColor = b.IsCrate
                    ? (b.Empty ? new Color(0.18f, 0.2f, 0.15f) : new Color(0.3f, 0.42f, 0.22f))
                    : new Color(0.45f, 0.32f, 0.18f);
            }
            foreach (var kv in _bagViews)
                if (!seen.Contains(kv.Key)) kv.Value.Visible = false;

            // эффекты
            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                var (node, life, max) = _fx[i];
                life -= dt;
                if (life <= 0) { node.QueueFree(); _fx.RemoveAt(i); continue; }
                if (node is OmniLight3D l) l.LightEnergy = 4 * life / max;
                _fx[i] = (node, life, max);
            }

            // камера: следует за игроком и чуть выглядывает в сторону прицела
            var look = _aim - selfPos;
            if (look.Length() > 7) look = look / look.Length() * 7;
            var target = V3(selfPos + look * 0.35f, 0);
            _camTarget = _camTarget == Vector3.Zero ? target : _camTarget.Lerp(target, 1 - Mathf.Exp(-8 * dt));
            if (_steepCam)
            {
                _cam.Position = _camTarget + new Vector3(0, _zoom, 0.01f);
                _cam.LookAt(_camTarget, Vector3.Forward);
            }
            else
            {
                float pitch = Mathf.DegToRad(60);
                _cam.Position = _camTarget + new Vector3(0, _zoom * Mathf.Sin(pitch), _zoom * Mathf.Cos(pitch));
                _cam.LookAt(_camTarget, Vector3.Up);
            }

            // обзор
            var self = _cur.Self;
            var dir = _aim - selfPos;
            if (dir.LengthSquared() > 0.01f) dir = SVec.Normalize(dir);
            _visionMat.SetShaderParameter("player_pos", new Vector2(selfPos.X, selfPos.Y));
            _visionMat.SetShaderParameter("facing_dir", new Vector2(dir.X, dir.Y));
            _visionMat.SetShaderParameter("darkness", self.Dead ? 0.0f : 0.6f);
        }

        void UpdateUi(float dt)
        {
            var s = _cur.Self;
            _hpFill.Size = new Vector2(260 * Mathf.Clamp(s.Hp / s.MaxHp, 0, 1), 26);
            _hpFill.Color = s.Hp > 50 ? new Color(0.45f, 0.75f, 0.3f) : s.Hp > 25 ? new Color(0.85f, 0.65f, 0.2f) : new Color(0.85f, 0.2f, 0.15f);
            _hpLabel.Text = $"HP {Mathf.CeilToInt(s.Hp)}   · бинты: {s.Bandages}";

            var bar = new System.Text.StringBuilder();
            for (int i = 0; i < s.Hotbar.Length; i++)
                bar.Append(i == s.Current ? $"[{i + 1} {s.Hotbar[i]}]  " : $"{i + 1} {s.Hotbar[i]}  ");
            _ammoLabel.Text = $"{bar}\n{s.WeaponName}   {s.Ammo} / {s.Magazine}   ·  запас {s.Reserve}";

            _camLabel.Text = _steepCam ? "Камера: строго сверху (V)" : "Камера: под углом 60° (V)";
            _promptLabel.Text = s.NearLoot && !s.Dead ? "[E] Залутать" : "";

            var inv = new System.Text.StringBuilder("ИНВЕНТАРЬ\n\n");
            if (s.Inventory.Count == 0) inv.Append("пусто");
            foreach (var (name, count) in s.Inventory) inv.Append($"{name} ×{count}\n");
            _invLabel.Text = inv.ToString();

            _deathRect.Visible = s.Dead;

            _toastTime -= dt;
            _toastLabel.Modulate = new Color(1, 1, 1, Mathf.Clamp(_toastTime, 0, 1));

            var vs = GetViewport().GetVisibleRect().Size;
            Place(_ammoLabel, vs, 1, 1, new Vector2(-20, -20));
            Place(_promptLabel, vs, 0.5f, 1, new Vector2(0, -130));
            Place(_toastLabel, vs, 0.5f, 0, new Vector2(0, 90));
            Place(_invPanel, vs, 1, 0.5f, new Vector2(-30, 0));
            Place(_deathLabel, vs, 0.5f, 0.5f, Vector2.Zero);

            _damageFlash = Mathf.Max(0, _damageFlash - dt);
            _damageRect.Color = new Color(0.8f, 0, 0, _damageFlash * 0.5f);

            // прицел: радиус круга = реальный разброс на дистанции до курсора
            var mouse = GetViewport().GetMousePosition();
            _crosshair.Position = mouse;
            _crosshair.HitFlash = Mathf.Max(0, _crosshair.HitFlash - dt);
            _crosshair.ReloadProgress = s.ReloadProgress;
            var me = _actorViews.TryGetValue(_myId, out var mv) ? mv.Position : Vector3.Zero;
            var aim3 = new Vector3(_aim.X, AimHeight, _aim.Y);
            float dist = new Vector2(aim3.X - me.X, aim3.Z - me.Z).Length();
            float rWorld = Mathf.Tan(Mathf.DegToRad(s.SpreadDeg * 0.5f)) * dist;
            var right = _cam.GlobalTransform.Basis.X;
            _crosshair.RadiusPx = _cam.UnprojectPosition(aim3 + right * rWorld).DistanceTo(_cam.UnprojectPosition(aim3));
            _crosshair.QueueRedraw();
        }
    }
}
