using System.Collections.Generic;
using Godot;
using Rastvordd.Client;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;
using Sky = Rastvordd.Sim.Sky;   // погода симуляции (у Godot есть свой Sky — небо)

namespace Rastvordd.Client3D
{
    /// <summary>
    /// Погода: дождь/ливень/гроза и снег/метель частицами вокруг камеры, ветер их сносит, туман по погоде,
    /// молнии в грозу, снег ложится на землю (SnowCover). Плавная смена — по WxPrev/WxBlend, как в 2D.
    /// </summary>
    public partial class Weather3D : Node3D
    {
        GpuParticles3D _rain, _snow;
        Sky _rainFor = (Sky)(-1), _snowFor = (Sky)(-1);
        float _flash, _nextBolt = 6f;
        readonly RandomNumberGenerator _rng = new();
        public float Flash => _flash;

        public override void _Ready()
        {
            _rain = Emitter(new QuadMesh { Size = new Vector2(0.025f, 0.7f) }, new Color(0.75f, 0.82f, 0.9f, 0.45f), 0.9f, 22f);
            _snow = Emitter(new QuadMesh { Size = new Vector2(0.09f, 0.09f) }, new Color(1f, 1f, 1f, 0.9f), 6f, 1.4f);
        }

        GpuParticles3D Emitter(PrimitiveMesh mesh, Color c, float life, float fall)
        {
            var mat = new StandardMaterial3D
            {
                AlbedoColor = c,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            mesh.Material = mat;
            var p = new GpuParticles3D
            {
                Amount = 1,
                Lifetime = life,
                Emitting = false,
                LocalCoords = false,
                DrawPass1 = mesh,
                VisibilityAabb = new Aabb(new Vector3(-40, -30, -40), new Vector3(80, 60, 80)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                ProcessMaterial = new ParticleProcessMaterial
                {
                    EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                    EmissionBoxExtents = new Vector3(34, 1, 34),
                    Direction = Vector3.Down,
                    Spread = 4f,
                    InitialVelocityMin = fall * 0.85f,
                    InitialVelocityMax = fall * 1.15f,
                    Gravity = Vector3.Zero,
                },
            };
            AddChild(p);
            return p;
        }

        static int RainAmount(Sky s) => s switch { Sky.Rain => 1600, Sky.Downpour => 4200, Sky.Storm => 5200, _ => 0 };
        static int SnowAmount(Sky s) => s switch { Sky.Snow => 1400, Sky.HeavySnow => 3200, Sky.Blizzard => 6000, _ => 0 };

        /// <summary>Каждый кадр: центр — точка взгляда камеры; возвращает добавку к плотности тумана.</summary>
        public float Step(Snapshot s, Vector3 focus, float dt)
        {
            Position = focus + new Vector3(0, 16, 0);
            var sky = s.WxBlend < 0.5f ? s.WxPrev : s.Sky;
            int ra = RainAmount(sky), sa = SnowAmount(sky);
            if (sky != _rainFor) { _rainFor = sky; _rain.Emitting = ra > 0; if (ra > 0) _rain.Amount = ra; }
            if (sky != _snowFor) { _snowFor = sky; _snow.Emitting = sa > 0; if (sa > 0) _snow.Amount = sa; }
            // ветер сносит осадки: по углу и силе ветра из симуляции
            var wind = new Vector3(Mathf.Cos(s.WindAngle), 0, Mathf.Sin(s.WindAngle)) * s.WindStrength;
            if (_rain.ProcessMaterial is ParticleProcessMaterial rp) rp.Gravity = wind * 4f;
            if (_snow.ProcessMaterial is ParticleProcessMaterial sp) sp.Gravity = wind * (sky == Sky.Blizzard ? 9f : 3f) + new Vector3(0, -0.2f, 0);

            // гроза: вспышка неба раз в несколько секунд
            _flash = Mathf.MoveToward(_flash, 0, dt * 4f);
            if (sky == Sky.Storm)
            {
                _nextBolt -= dt;
                if (_nextBolt <= 0) { _flash = 1f; _nextBolt = _rng.RandfRange(4f, 14f); }
            }
            return sky switch { Sky.Fog => 0.03f, Sky.Blizzard => 0.035f, Sky.HeavySnow => 0.015f, Sky.Downpour or Sky.Storm => 0.012f, Sky.Rain or Sky.Snow => 0.006f, _ => 0f };
        }
    }

    /// <summary>
    /// Живое на РТ (MapAmbient): дым и пар — клубами, флаг на флагштоке колышется, вороны кружат,
    /// бумажки кувыркаются по ветру, свечи и лампы горят ночью (с мерцанием у «lamp_flicker»).
    /// </summary>
    public partial class Ambient3D : Node3D
    {
        struct Emit { public string Kind; public Vector3 At; public float Next; public Node3D Node; public float Phase; public OmniLight3D Light; }
        readonly List<Emit> _items = new();
        readonly RandomNumberGenerator _rng = new();
        Fx3D _fx;

        public void Build(MapInfo map, Fx3D fx)
        {
            _fx = fx;
            foreach (var a in map.Ambients)
            {
                var at = W3.V(a.Pos, a.H);
                var e = new Emit { Kind = a.Kind, At = at, Next = _rng.RandfRange(0, 2), Phase = _rng.RandfRange(0, 6) };
                switch (a.Kind)
                {
                    case "flag":
                    {
                        var root = new Node3D { Position = W3.V(a.Pos) };
                        float h = Mathf.Max(4f, a.H);
                        MapStatic3D.Segment(root, Vector3.Zero, new Vector3(0, h, 0), 0.04f, Mats3D.Solid(new Color(0.5f, 0.5f, 0.5f), 0.5f, 0.6f));
                        var cloth = new Node3D { Position = new Vector3(0, h - 0.45f, 0) };
                        W3.AddBox(cloth, new Vector3(0.6f, 0, 0), new Vector3(1.2f, 0.8f, 0.02f), 0, Mats3D.Solid(new Color(0.72f, 0.18f, 0.15f), 0.9f));
                        root.AddChild(cloth);
                        AddChild(root);
                        e.Node = cloth;
                        break;
                    }
                    case "crows":
                    {
                        var root = new Node3D { Position = at + new Vector3(0, 8, 0) };
                        for (int i = 0; i < 5; i++)
                        {
                            var bird = new Node3D();
                            W3.AddBox(bird, Vector3.Zero, new Vector3(0.35f, 0.04f, 0.12f), 0, Mats3D.Solid(new Color(0.06f, 0.06f, 0.07f), 0.9f), false);
                            W3.AddBox(bird, Vector3.Zero, new Vector3(0.1f, 0.04f, 0.6f), 0, Mats3D.Solid(new Color(0.06f, 0.06f, 0.07f), 0.9f), false);
                            root.AddChild(bird);
                        }
                        AddChild(root);
                        e.Node = root;
                        break;
                    }
                    case "papers":
                    {
                        var root = new Node3D { Position = at };
                        for (int i = 0; i < 4; i++) W3.AddBox(root, new Vector3(_rng.RandfRange(-1, 1), 0.05f, _rng.RandfRange(-1, 1)), new Vector3(0.22f, 0.005f, 0.3f), _rng.RandfRange(0, 3), Mats3D.Solid(new Color(0.88f, 0.86f, 0.8f), 0.9f), false);
                        AddChild(root);
                        e.Node = root;
                        break;
                    }
                    case "candle":
                    case "lamp":
                    case "lamp_flicker":
                    {
                        var c = a.Kind == "candle" ? new Color(1f, 0.7f, 0.35f) : new Color(1f, 0.85f, 0.6f);
                        e.Light = new OmniLight3D { Position = at + new Vector3(0, 0.1f, 0), LightColor = c, OmniRange = a.Kind == "candle" ? 3f : 6f, LightEnergy = 0, ShadowEnabled = false };
                        AddChild(e.Light);
                        W3.AddMesh(this, W3.Sphere, at, Vector3.One * (a.Kind == "candle" ? 0.05f : 0.16f), 0, Mats3D.Glow(c, 2.5f), false);
                        break;
                    }
                }
                _items.Add(e);
            }
        }

        public void Step(SVec me, float night, float time, Vector3 wind, float dt)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var e = _items[i];
                // дальние — не трогаем (дым и частицы только рядом)
                float d = new Vector2(e.At.X - me.X, e.At.Z - me.Y).Length();
                switch (e.Kind)
                {
                    case "smoke":
                    case "steam":
                        if (d > 120) break;
                        e.Next -= dt;
                        if (e.Next <= 0)
                        {
                            bool steam = e.Kind == "steam";
                            _fx.Puff(e.At, steam ? new Color(0.92f, 0.92f, 0.95f, 0.5f) : new Color(0.35f, 0.34f, 0.33f, 0.55f), steam ? 0.6f : 0.9f, steam ? 2.2f : 4.5f, steam ? 1.6f : 1.1f, wind * 0.4f);
                            e.Next = _rng.RandfRange(0.25f, 0.6f);
                        }
                        break;
                    case "flag":
                        if (e.Node != null) e.Node.Rotation = new Vector3(0, Mathf.Atan2(-wind.Z, wind.X) + Mathf.Sin(time * 3f + e.Phase) * 0.25f, Mathf.Sin(time * 5f + e.Phase) * 0.06f);
                        break;
                    case "crows":
                        if (e.Node == null) break;
                        int k = 0;
                        foreach (var c in e.Node.GetChildren())
                        {
                            if (c is not Node3D b) continue;
                            float a = time * 0.5f + k * 1.25f + e.Phase;
                            float r = 6f + k * 1.3f;
                            b.Position = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(time * 1.3f + k) * 0.8f + k * 0.4f, Mathf.Sin(a) * r);
                            b.Rotation = new Vector3(Mathf.Sin(time * 9f + k) * 0.4f, -a - Mathf.Pi / 2, 0);
                            k++;
                        }
                        break;
                    case "papers":
                        if (e.Node != null) e.Node.Position = e.At + new Vector3(Mathf.Sin(time * 0.7f + e.Phase), Mathf.Max(0, Mathf.Sin(time * 1.7f + e.Phase)) * 0.4f, Mathf.Cos(time * 0.5f + e.Phase)) * 0.6f;
                        break;
                    default:
                        if (e.Light != null)
                        {
                            float flick = e.Kind == "lamp_flicker" ? (Mathf.Sin(time * 23f + e.Phase) > 0.6f ? 0.2f : 1f) : e.Kind == "candle" ? 0.85f + 0.15f * Mathf.Sin(time * 11f + e.Phase) : 1f;
                            e.Light.LightEnergy = Mathf.Clamp(night * 1.6f + 0.25f, 0, 2) * flick;
                            e.Light.Visible = d < 90;
                        }
                        break;
                }
                _items[i] = e;
            }
        }
    }

    /// <summary>
    /// Фонари РТ — те же места и цвета, что в 2D (LightingLayer): фонари Подстанции, у КПП, под навесом АЗС, прожектор на вышке,
    /// аппаратура Радиоцентра (при дизеле), трансформатор и лампы корпуса (при включённом щитке). Горят ночью, часть мигает.
    /// </summary>
    public partial class Lamps3D : Node3D
    {
        struct Lamp { public Light3D L; public float Base; public bool Power, Radio, Broken; public float Phase; }
        readonly List<Lamp> _lamps = new();
        readonly RandomNumberGenerator _rng = new() { Seed = 4242 };

        public void Build(MapInfo map)
        {
            const float P = Client.Iso.PPM;
            foreach (var d in map.Decors)
            {
                if (d.Flat) continue;
                var p = d.Pos;
                switch (d.Sprite)
                {
                    case "props/subst_lamp": Add(p + new SVec(d.Flip ? 26 / P : -26 / P, 6 / P), 6.2f, 10f, new Color(1f, 0.72f, 0.42f), 1.35f, true); break;
                    case "props/door_lamp_post": Add(p + new SVec(0, 4 / P), 1.9f, 5.5f, new Color(1f, 0.86f, 0.62f), 1.1f, false); break;
                    case "props/fuel_canopy":
                        Add(p + new SVec(-120 / P, -40 / P), 3.6f, 6.5f, new Color(0.9f, 0.95f, 1f), 1.0f, true);
                        Add(p + new SVec(120 / P, -40 / P), 3.6f, 6.5f, new Color(0.9f, 0.95f, 1f), 1.0f, true);
                        break;
                    case "props/gai_post":
                    case "props/subst_kpp": Add(p + new SVec(0, 14 / P), 1.6f, 4.5f, new Color(1f, 0.8f, 0.5f), 0.9f, false); break;
                    case "props/phone_booth": Add(p + new SVec(0, 6 / P), 2.0f, 3f, new Color(0.85f, 0.95f, 1f), 0.8f, false); break;
                    case "props/radio_watchtower": Add(d.Pos + new SVec(3f, 6f), 7.5f, 13f, new Color(0.85f, 0.92f, 1f), 1.5f, true, broken: false); break;
                    case "props/radio_console":
                    case "props/radio_transmitter_big":
                        Add(p + new SVec(0, -16 / P), 1.4f, 5f, d.Sprite.EndsWith("console") ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.75f, 0.35f), 0.9f, false, radio: true, broken: false);
                        break;
                    case "props/subst_transformer": Add(p + new SVec(0, -10 / P), 2.5f, 7f, new Color(0.55f, 0.75f, 1f), 0.9f, false, power: true, broken: true); break;
                }
            }
            foreach (var r in map.Roofs)
            {
                if (r.Tile != "tiles/roof_tar") continue;
                Add((r.Min + r.Max) * 0.5f, 2.6f, Mathf.Min(9f, (r.Max - r.Min).Length() * 0.45f), new Color(0.82f, 0.92f, 1f), 1.2f, false, power: true);
            }
        }

        void Add(SVec at, float h, float range, Color c, float energy, bool cone, bool power = false, bool radio = false, bool? broken = null)
        {
            Light3D l;
            if (cone)
            {
                var s = new SpotLight3D { SpotRange = range * 1.4f, SpotAngle = 55f, Position = W3.V(at, h), RotationDegrees = new Vector3(-90, 0, 0) };
                l = s;
            }
            else l = new OmniLight3D { OmniRange = range, Position = W3.V(at, h) };
            l.LightColor = c;
            l.LightEnergy = 0;
            l.ShadowEnabled = cone;
            l.Visible = false;
            AddChild(l);
            if (cone) W3.AddMesh(this, W3.Sphere, W3.V(at, h + 0.05f), new Vector3(0.35f, 0.12f, 0.35f), 0, Mats3D.Glow(c, 2f), false);
            _lamps.Add(new Lamp { L = l, Base = energy * (cone ? 2.2f : 1.4f), Power = power, Radio = radio, Broken = broken ?? _rng.Randf() < 0.18f, Phase = _rng.Randf() * 50 });
        }

        public void Step(float night, bool substPowered, bool radioPowered, SVec me, float time)
        {
            foreach (var lp in _lamps)
            {
                bool on = lp.Radio ? radioPowered : lp.Power ? substPowered : night > 0.15f;
                var pos = lp.L.Position;
                bool near = new Vector2(pos.X - me.X, pos.Z - me.Y).Length() < 110;
                lp.L.Visible = on && near;
                if (!lp.L.Visible) continue;
                float flick = lp.Broken ? (Mathf.Sin(time * 17f + lp.Phase) + Mathf.Sin(time * 5.3f + lp.Phase * 2) > 1.2f ? 0.15f : 1f) : 1f;
                float k = lp.Radio || lp.Power ? Mathf.Max(0.5f, night) : night;
                lp.L.LightEnergy = lp.Base * k * flick;
            }
        }
    }

    /// <summary>
    /// События директора: Ан-2 пролетает и сбрасывает груз на парашюте, у груза — цветной дым (красный — ещё закрыт,
    /// зелёный — открыт), у прочих ивентов — жёлтый столб света и дым; ракеты ракетницы взлетают и горят красным.
    /// Время — как в 2D: StartsAt, сглаженное до плавного полёта.
    /// </summary>
    public partial class Director3D : Node3D
    {
        const float PlaneAlt = 45f;
        Node3D _plane, _chute;
        readonly Dictionary<int, Node3D> _beams = new();
        public readonly List<(SVec pos, float t)> Flares = new();
        readonly List<(Node3D node, OmniLight3D light)> _flareViews = new();
        float _vis = -1, _smokeAcc;
        int _tick;
        Fx3D _fx;

        public void Init(Fx3D fx)
        {
            _fx = fx;
            _plane = Plane();
            _plane.Visible = false;
            AddChild(_plane);
            _chute = new Node3D { Visible = false };
            W3.AddMesh(_chute, W3.Sphere, new Vector3(0, 4.2f, 0), new Vector3(5f, 2.4f, 5f), 0, Mats3D.Solid(new Color(0.85f, 0.35f, 0.2f), 0.9f));
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.Pi / 2 + Mathf.Pi / 4;
                MapStatic3D.Segment(_chute, new Vector3(0, 0.6f, 0), new Vector3(Mathf.Cos(a) * 2.2f, 3.6f, Mathf.Sin(a) * 2.2f), 0.015f, Mats3D.Solid(new Color(0.85f, 0.85f, 0.8f), 0.9f), false);
            }
            W3.AddBox(_chute, new Vector3(0, 0.3f, 0), new Vector3(1f, 0.7f, 1f), 0, Mats3D.Solid(new Color(0.35f, 0.45f, 0.3f), 0.8f));
            AddChild(_chute);
        }

        static Node3D Plane()
        {
            var root = new Node3D();
            var body = Mats3D.Solid(new Color(0.55f, 0.6f, 0.5f), 0.7f, 0.2f);
            W3.AddBox(root, Vector3.Zero, new Vector3(12.5f, 1.6f, 1.6f), 0, body);
            W3.AddBox(root, new Vector3(1.5f, 1.2f, 0), new Vector3(2.4f, 0.12f, 18f), 0, body);   // верхнее крыло
            W3.AddBox(root, new Vector3(1.5f, -0.6f, 0), new Vector3(2.0f, 0.12f, 14f), 0, body);  // нижнее
            W3.AddBox(root, new Vector3(-5.8f, 0.3f, 0), new Vector3(1.4f, 0.1f, 5f), 0, body);    // хвост
            W3.AddBox(root, new Vector3(-5.8f, 1.2f, 0), new Vector3(1.4f, 1.8f, 0.1f), 0, body);
            W3.AddBox(root, new Vector3(6.4f, 0, 0), new Vector3(0.1f, 2.6f, 0.2f), 0, Mats3D.Solid(new Color(0.15f, 0.15f, 0.15f), 0.6f));   // винт
            return root;
        }

        public void Sync(List<DirectorMarker> markers, float time, float dt)
        {
            if (_vis < 0 || Mathf.Abs(_vis - time) > 1.5f) _vis = time;
            else { _vis += dt; _vis += (time - _vis) * Mathf.Min(1f, dt * 3f); }
            _plane.Visible = false;
            _chute.Visible = false;
            var seen = new HashSet<int>();
            foreach (var m in markers)
            {
                if (m.Kind == "airdrop" && !m.Started)
                {
                    float k = (_vis - (m.StartsAt - 45)) / 40f;
                    if (k > 0 && k < 1)
                    {
                        var p = SVec.Lerp(m.PlaneFrom, m.PlaneTo, k);
                        var dir = m.PlaneTo - m.PlaneFrom;
                        _plane.Visible = true;
                        _plane.Position = W3.V(p, PlaneAlt);
                        _plane.Rotation = new Vector3(Mathf.Sin(_vis * 0.6f) * 0.04f, W3.Yaw(dir.X, dir.Y), 0);
                    }
                    float fall = (_vis - (m.StartsAt - 25)) / 25f;
                    if (fall > 0 && fall < 1)
                    {
                        float e = 1 - Mathf.Pow(1 - fall, 1.8f);
                        _chute.Visible = true;
                        _chute.Position = W3.V(m.Pos, PlaneAlt * (1 - e));
                        _chute.Rotation = new Vector3(Mathf.Sin(_vis * 1.3f) * 0.08f, 0, Mathf.Cos(_vis * 1.1f) * 0.06f);
                    }
                    continue;
                }
                seen.Add(m.Id);
                if (!_beams.TryGetValue(m.Id, out var beam))
                {
                    beam = new Node3D { Position = W3.V(m.Pos) };
                    AddChild(beam);
                    _beams[m.Id] = beam;
                }
                // столб света: цвет по состоянию (зелёный — открыт, красный — груз, жёлтый — ивент), пульсирует
                var col = m.Open ? new Color(0.4f, 1f, 0.45f) : m.Kind == "airdrop" ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 0.85f, 0.25f);
                float pulse = 0.55f + 0.45f * Mathf.Sin(time * (m.Started && !m.Open ? 6f : 2f));
                string key = $"{col.ToHtml()}";
                if ((string)beam.GetMeta("k", "") != key)
                {
                    W3.ClearChildren(beam);
                    var mat = Mats3D.Glow(new Color(col.R, col.G, col.B, 0.22f), 1.5f, true);
                    W3.AddMesh(beam, W3.Cylinder, new Vector3(0, 20, 0), new Vector3(1.4f, 40f, 1.4f), 0, mat, false);
                    beam.SetMeta("k", key);
                }
                beam.Scale = new Vector3(0.7f + 0.3f * pulse, 1, 0.7f + 0.3f * pulse);
            }
            var drop = new List<int>();
            foreach (var id in _beams.Keys) if (!seen.Contains(id)) drop.Add(id);
            foreach (var id in drop) { RemoveChild(_beams[id]); _beams[id].QueueFree(); _beams.Remove(id); }

            // дым у меток
            _smokeAcc += dt;
            while (_smokeAcc >= 0.1f)
            {
                _smokeAcc -= 0.1f;
                _tick++;
                foreach (var m in markers)
                {
                    if (m.Kind == "airdrop" && !m.Started) continue;
                    var at = W3.V(m.Pos, 0.4f);
                    if (m.Kind == "airdrop" && !m.Open) { if (_tick % 2 == 0) _fx.Puff(at, new Color(0.9f, 0.25f, 0.2f, 0.6f), 1.0f, 5f, 2.2f, new Vector3(0.6f, 0, 0)); }
                    else if (!m.Open) { if (_tick % 8 == 0) _fx.Puff(at, new Color(0.95f, 0.85f, 0.3f, 0.5f), 0.9f, 5f, 2.6f, Vector3.Zero); }
                    else if (_tick % 15 == 0) _fx.Puff(at, new Color(0.4f, 0.9f, 0.45f, 0.45f), 0.8f, 5f, 3f, Vector3.Zero);
                }
            }

            // ракеты: взлёт до 60 м за 2,5 с, медленно опускаются, гаснут за 25 с
            while (_flareViews.Count < Flares.Count)
            {
                var n = new Node3D();
                W3.AddMesh(n, W3.Sphere, Vector3.Zero, Vector3.One * 0.5f, 0, Mats3D.Glow(new Color(1f, 0.25f, 0.15f), 5f), false);
                var l = new OmniLight3D { LightColor = new Color(1f, 0.3f, 0.2f), OmniRange = 70f, LightEnergy = 2f, ShadowEnabled = false };
                n.AddChild(l);
                AddChild(n);
                _flareViews.Add((n, l));
            }
            for (int i = 0; i < _flareViews.Count; i++)
            {
                var (n, l) = _flareViews[i];
                if (i >= Flares.Count) { n.Visible = false; continue; }
                var (fp, ft) = Flares[i];
                float age = time - ft;
                n.Visible = age >= 0 && age <= 25;
                if (!n.Visible) continue;
                float hM = age < 2.5f ? 60 * (age / 2.5f) : 60 - (age - 2.5f) * 1.6f;
                n.Position = W3.V(fp, hM) + new Vector3(Mathf.Sin(age * 0.7f) * 2f * Mathf.Min(1, age / 3), 0, 0);
                l.LightEnergy = 2.5f * (age > 20 ? (25 - age) / 5 : 1);
            }
        }
    }

    /// <summary>Следы на мире: кровь под попаданием, выбоины от пуль на стенах, копоть от взрывов. Не больше 160, старые уходят первыми.</summary>
    public partial class Decals3D : Node3D
    {
        const int Max = 160;
        readonly Queue<Decal> _q = new();
        readonly RandomNumberGenerator _rng = new();
        static Texture2D _hole;

        static Texture2D Hole()
        {
            if (_hole != null) return _hole;
            var g = new Gradient();
            g.SetColor(0, new Color(0.05f, 0.05f, 0.05f, 0.95f));
            g.SetColor(1, new Color(0.15f, 0.14f, 0.13f, 0f));
            _hole = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 32, Height = 32 };
            return _hole;
        }

        void Push(Decal d)
        {
            AddChild(d);
            _q.Enqueue(d);
            while (_q.Count > Max) { var old = _q.Dequeue(); RemoveChild(old); old.QueueFree(); }
        }

        public void Blood(SVec at)
        {
            var tex = Mats3D.Tex(_rng.Randf() < 0.5f ? "decals/floor_blood" : "decals/decal_blood_trail");
            if (tex == null) return;
            float s = _rng.RandfRange(0.6f, 1.2f);
            Push(new Decal
            {
                TextureAlbedo = tex,
                Size = new Vector3(s, 1.2f, s),
                Position = W3.V(at + new SVec(_rng.RandfRange(-0.3f, 0.3f), _rng.RandfRange(-0.3f, 0.3f)), 0.3f),
                RotationDegrees = new Vector3(0, _rng.RandfRange(0, 360), 0),
                Modulate = new Color(0.7f, 0.1f, 0.1f, 0.9f),
            });
        }

        /// <summary>Выбоина: декаль смотрит против полёта пули и проецируется на стену.</summary>
        public void BulletHole(SVec at, SVec dir, float h)
        {
            var d3 = new Vector3(dir.X, 0, dir.Y);
            if (d3.LengthSquared() < 1e-4f) d3 = Vector3.Forward;
            d3 = d3.Normalized();
            // ось −Y декали — вдоль пули (в стену)
            var y = -d3;
            var x = y.Cross(Vector3.Up).Normalized();
            var z = x.Cross(y).Normalized();
            Push(new Decal
            {
                TextureAlbedo = Hole(),
                Size = new Vector3(0.14f, 0.6f, 0.14f),
                Transform = new Transform3D(new Basis(x, y, z), W3.V(at, h) - d3 * 0.1f),
            });
        }

        public void Scorch(SVec at, bool big)
        {
            var tex = Mats3D.Tex("decals/decal_burnt_spot");
            if (tex == null) return;
            float s = big ? 4.5f : 3f;
            Push(new Decal { TextureAlbedo = tex, Size = new Vector3(s, 2.5f, s), Position = W3.V(at, 0.5f), RotationDegrees = new Vector3(0, _rng.RandfRange(0, 360), 0) });
        }
    }

    /// <summary>
    /// Обзор, как в 2D (VisionFx): вне конуса взгляда и за стенами мир затемнён. Конус — по направлению на курсор,
    /// уже при прицеливании; дальность — из тех же лучей VisionRays (стены, деревья, присед), что у 2D.
    /// Рисуется полноэкранным квадом: по глубине восстанавливаем точку мира и сравниваем с лучом.
    /// </summary>
    public partial class Vision3D : MeshInstance3D
    {
        const string Code = @"
shader_type spatial;
render_mode unshaded, depth_test_disabled, depth_draw_never, cull_disabled, blend_mix, fog_disabled;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform sampler2D rays : filter_linear, repeat_enable;
uniform vec2 me = vec2(0.0);
uniform vec2 dir = vec2(1.0, 0.0);
uniform float cos_half = 0.4;
uniform float near_r = 3.0;
uniform float strength = 0.6;
uniform float rays_count = 512.0;

void vertex() { POSITION = vec4(VERTEX.xy, 1.0, 1.0); }

void fragment() {
    float depth = texture(depth_tex, SCREEN_UV).x;
    if (depth <= 0.00001) discard;   // небо (обратный Z: дальняя плоскость — 0)
    vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, depth);
    vec4 v = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
    v.xyz /= v.w;
    vec3 w = (INV_VIEW_MATRIX * vec4(v.xyz, 1.0)).xyz;
    vec2 d = w.xz - me;
    float dist = length(d);
    float dark = 0.0;
    if (dist > near_r) {
        vec2 nd = d / max(dist, 0.001);
        float c = dot(nd, dir);
        float cone = smoothstep(cos_half - 0.08, cos_half + 0.02, c);
        float a = atan(d.y, d.x);
        float u = (a + 3.14159265) / 6.2831853;
        float reach = texture(rays, vec2(u, 0.5)).r;
        float seen = cone * (1.0 - smoothstep(reach - 0.5, reach + 1.5, dist));
        dark = 1.0 - seen;
    }
    if (w.y > 2.8) dark *= 0.35;   // крыши и кроны выше — светлее: читается силуэт
    ALBEDO = vec3(0.02, 0.03, 0.05);
    ALPHA = dark * strength;
}
";
        ShaderMaterial _mat;

        public override void _Ready()
        {
            Mesh = new QuadMesh { Size = new Vector2(2, 2) };
            _mat = new ShaderMaterial { Shader = new Shader { Code = Code } };
            MaterialOverride = _mat;
            ExtraCullMargin = 16384f;
            CastShadow = ShadowCastingSetting.Off;
        }

        public void Set(SVec me, SVec dir, float cosHalf, Texture2D rays, bool on, float night)
        {
            Visible = on;
            if (!on) return;
            _mat.SetShaderParameter("me", new Vector2(me.X, me.Y));
            _mat.SetShaderParameter("dir", new Vector2(dir.X, dir.Y));
            _mat.SetShaderParameter("cos_half", cosHalf);
            _mat.SetShaderParameter("rays", rays);
            _mat.SetShaderParameter("strength", 0.5f + 0.3f * night);
        }
    }
}
