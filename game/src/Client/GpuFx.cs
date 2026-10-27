using Godot;
using System.Collections.Generic;

namespace Rastvordd.Client
{
    /// <summary>
    /// Боевые эффекты на видеокарте: искры, обломки, угольки, оседающий дым (GPUParticles2D) и искажение воздуха —
    /// жар над огнём и ударная волна взрыва (короткоживущий квад, читающий экран). Всё в пулах, созданных один раз:
    /// выстрел/взрыв только переставляет готовый узел и перезапускает его — без выделений памяти. Цвета — из листов
    /// fx/hit_sparks, fx/embers, fx/explosion_*, fx/smoke_black.
    /// </summary>
    public static class GpuFx
    {
        public enum Kind { Sparks, Debris, Embers, Smoke }

        sealed class Pool { public GpuParticles2D[] E; public int Next; }
        static readonly Pool[] Pools = new Pool[4];

        sealed class Warp { public Sprite2D S; public ShaderMaterial M; public float Age, Life; }
        static readonly List<Warp> Warps = new();
        static int _warpNext;

        static Vector2 _wind = new(7, 0);
        static Node2D _world;

        const string WarpCode = @"
shader_type canvas_item;
render_mode unshaded;
uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform float age = 0.0;
uniform float life = 1.0;
uniform float mode = 0.0;     // 0 — жар (дрожание), 1 — ударная волна (кольцо)
uniform float strength = 3.0; // сдвиг, экранных пикселей
void fragment() {
    vec2 p = UV * 2.0 - 1.0;
    float r = length(p);
    float k = clamp(age / life, 0.0, 1.0);
    vec2 off = vec2(0.0);
    vec3 add = vec3(0.0);
    if (mode < 0.5) {
        float fall = (1.0 - smoothstep(0.25, 1.0, r)) * (1.0 - k * k) * smoothstep(0.0, 0.08, age);
        // жар поднимается: волна бежит вверх по экрану
        off = vec2(sin(UV.y * 26.0 + TIME * 13.0) , cos(UV.x * 19.0 + UV.y * 9.0 + TIME * 10.0) * 0.5) * strength * fall;
    } else {
        float R = sqrt(k);
        float band = (1.0 - smoothstep(0.0, 0.16, abs(r - R))) * (1.0 - k);
        off = normalize(p + vec2(0.0001)) * band * strength;
        add = vec3(1.0, 0.92, 0.8) * step(0.55, band) * 0.22;   // светлая кромка волны (как fx/shockwave)
    }
    off = floor(off + 0.5);                                 // целые пиксели — без размытия арта
    vec3 c = texture(screen_tex, SCREEN_UV - off * SCREEN_PIXEL_SIZE).rgb + add;
    COLOR = vec4(c, step(r, 1.0));
}";

        public static void Init(Node2D world)
        {
            _world = world;
            Warps.Clear(); _warpNext = 0;
            for (int i = 0; i < Pools.Length; i++) Pools[i] = null;
            if (world == null) return;
            Pools[(int)Kind.Sparks] = Make(10, () => Emitter(28, 0.5f, Px(2, 1), true, m =>
            {
                m.Spread = 55; m.InitialVelocityMin = 70; m.InitialVelocityMax = 230; m.Gravity = new Vector3(0, 340, 0);
                m.DampingMin = 40; m.DampingMax = 90;
                m.ColorRamp = Ramp(C(0xfeef8a), C(0xffbd12), C(0xfe9903), new Color(C(0xff3401), 0));
            }));
            Pools[(int)Kind.Debris] = Make(6, () => Emitter(22, 1.0f, Px(2, 2), false, m =>
            {
                m.Spread = 180; m.InitialVelocityMin = 50; m.InitialVelocityMax = 170; m.Gravity = new Vector3(0, 300, 0);
                m.Direction = new Vector3(0, -1, 0); m.DampingMin = 30; m.DampingMax = 60;
                m.ScaleMin = 0.8f; m.ScaleMax = 1.6f;
                m.ColorRamp = Ramp(C(0x6d6565), C(0x4c4244), C(0x3c3336), new Color(C(0x32292c), 0));
            }));
            Pools[(int)Kind.Embers] = Make(8, () => Emitter(26, 1.8f, Px(1, 1), true, m =>
            {
                m.Spread = 70; m.Direction = new Vector3(0, -1, 0); m.InitialVelocityMin = 18; m.InitialVelocityMax = 70;
                m.Gravity = new Vector3(_wind.X, -18, 0); m.DampingMin = 8; m.DampingMax = 20;
                m.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere; m.EmissionSphereRadius = 10;
                m.ScaleMin = 1; m.ScaleMax = 2;
                m.ColorRamp = Ramp(C(0xffff5f), C(0xf4b708), C(0xff8701), new Color(C(0xff3401), 0));
            }));
            Pools[(int)Kind.Smoke] = Make(6, () => Emitter(12, 6.5f, Disk(10), false, m =>
            {
                m.Spread = 180; m.InitialVelocityMin = 6; m.InitialVelocityMax = 22; m.Gravity = new Vector3(_wind.X, -6, 0);
                m.DampingMin = 4; m.DampingMax = 8;
                m.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere; m.EmissionSphereRadius = 14;
                m.ScaleMin = 1.2f; m.ScaleMax = 2.0f;
                var sc = new Curve(); sc.AddPoint(new Vector2(0, 0.6f)); sc.AddPoint(new Vector2(1, 2.6f));
                m.ScaleCurve = new CurveTexture { Curve = sc };
                m.ColorRamp = Ramp(new Color(C(0x433b36), 0), new Color(C(0x3f3531), 0.55f), new Color(C(0x53463f), 0.35f), new Color(C(0x53463f), 0));
            }, explosive: 0.75f));
            var white = Px(1, 1);
            for (int i = 0; i < 6; i++)
            {
                var m = new ShaderMaterial { Shader = Shared ??= new Shader { Code = WarpCode } };
                var s = new Sprite2D { Texture = white, Material = m, Visible = false, ZIndex = 6, Centered = true };
                world.AddChild(s);
                Warps.Add(new Warp { S = s, M = m });
            }
        }
        static Shader Shared;

        /// <summary>Ветер (Main, из погоды): дым и угольки сносит.</summary>
        public static void SetWind(float angle, float strength)
        {
            _wind = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.5f) * (4 + strength * 5);
        }

        /// <summary>Выпустить частицы вида kind в точке экрана (с высотой уже вычтенной), направление dir, доля от полного залпа.</summary>
        public static void Burst(Kind kind, Vector2 at, Vector2 dir, float ratio = 1f)
        {
            var pool = Pools[(int)kind];
            if (pool == null) return;
            var e = pool.E[pool.Next]; pool.Next = (pool.Next + 1) % pool.E.Length;
            e.Position = at;
            e.Rotation = dir == Vector2.Zero ? 0 : dir.Angle();
            e.AmountRatio = Mathf.Clamp(ratio, 0.05f, 1f);
            if (kind is Kind.Smoke or Kind.Embers && e.ProcessMaterial is ParticleProcessMaterial pm)
                pm.Gravity = new Vector3(_wind.X, kind == Kind.Smoke ? -6 + _wind.Y : -18, 0);
            e.Restart();
        }

        /// <summary>Жар: дрожание воздуха над огнём/после взрыва (radiusPx — по экрану).</summary>
        public static void Haze(Vector2 at, float radiusPx, float life, float strength = 2f) => StartWarp(at, radiusPx, life, 0, strength);

        /// <summary>Ударная волна: кольцо искажения, расходится за life (эллипс 3/4).</summary>
        public static void Shock(Vector2 at, float radiusPx, float life = 0.45f, float strength = 4f) => StartWarp(at, radiusPx, life, 1, strength);

        static void StartWarp(Vector2 at, float r, float life, float mode, float strength)
        {
            if (Warps.Count == 0) return;
            var w = Warps[_warpNext]; _warpNext = (_warpNext + 1) % Warps.Count;
            w.Age = 0; w.Life = life;
            w.S.Position = at.Round();
            w.S.Scale = new Vector2(r * 2, r * (mode > 0.5f ? 1f : 2.4f));   // волна — плоский эллипс по земле, жар — вытянут вверх
            if (mode < 0.5f) w.S.Position -= new Vector2(0, r * 0.9f);
            w.M.SetShaderParameter("mode", mode);
            w.M.SetShaderParameter("life", life);
            w.M.SetShaderParameter("strength", strength);
            w.M.SetShaderParameter("age", 0f);
            w.S.Visible = true;
        }

        /// <summary>Каждый кадр из Vfx.Update: только возраст активных квадов искажения (≤6).</summary>
        public static void Update(float dt)
        {
            for (int i = 0; i < Warps.Count; i++)
            {
                var w = Warps[i];
                if (!w.S.Visible) continue;
                w.Age += dt;
                if (w.Age >= w.Life) { w.S.Visible = false; continue; }
                w.M.SetShaderParameter("age", w.Age);
            }
        }

        // ---------------- сборка ----------------

        static Pool Make(int n, System.Func<GpuParticles2D> f)
        {
            var p = new Pool { E = new GpuParticles2D[n] };
            for (int i = 0; i < n; i++) { p.E[i] = f(); _world.AddChild(p.E[i]); }
            return p;
        }

        static GpuParticles2D Emitter(int amount, float lifetime, Texture2D tex, bool glow, System.Action<ParticleProcessMaterial> setup, float explosive = 1f)
        {
            var m = new ParticleProcessMaterial
            {
                ParticleFlagDisableZ = true, Direction = new Vector3(1, 0, 0), Gravity = Vector3.Zero,
                LifetimeRandomness = 0.4f,
            };
            setup(m);
            return new GpuParticles2D
            {
                Amount = amount, Lifetime = lifetime, OneShot = true, Explosiveness = explosive, Randomness = 0.5f,
                Emitting = false, LocalCoords = false, Texture = tex, ProcessMaterial = m, ZIndex = 4,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                Material = glow ? new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded, BlendMode = CanvasItemMaterial.BlendModeEnum.Add } : null,
                VisibilityRect = new Rect2(-400, -400, 800, 800),
            };
        }

        static Color C(int hex) => new(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);

        static GradientTexture1D Ramp(params Color[] c)
        {
            var g = new Gradient();
            var off = new float[c.Length];
            for (int i = 0; i < c.Length; i++) off[i] = i / (float)(c.Length - 1);
            g.Offsets = off; g.Colors = c;
            return new GradientTexture1D { Gradient = g, Width = 32 };
        }

        static ImageTexture Px(int w, int h)
        {
            var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
            img.Fill(Colors.White);
            return ImageTexture.CreateFromImage(img);
        }

        /// <summary>Пиксельный клуб дыма: диск с «рваным» шахматным краем (как края fx/smoke_black).</summary>
        static ImageTexture Disk(int d)
        {
            var img = Image.CreateEmpty(d, d, false, Image.Format.Rgba8);
            float r = d / 2f;
            for (int y = 0; y < d; y++)
            for (int x = 0; x < d; x++)
            {
                float q = new Vector2(x + 0.5f - r, y + 0.5f - r).Length() / r;
                float a = q < 0.7f ? 1f : q < 1f && ((x + y) & 1) == 0 ? 1f : 0f;
                img.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            return ImageTexture.CreateFromImage(img);
        }
    }
}
