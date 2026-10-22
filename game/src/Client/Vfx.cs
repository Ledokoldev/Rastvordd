using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>Материал попадания: какие крошки/искры летят.</summary>
    public enum HitMat { Stone, Wood, Metal, Flesh, Armor, Glass, Dust, Water }

    /// <summary>
    /// Визуальные эффекты из листов анимаций game/art/fx/&lt;id&gt;.png (кадры в один ряд, квадратные; docs/design/vfx.md).
    /// Пул спрайтов в мире с y-sort: точка узла — на земле (по ней сортировка и затенение обзором), высота — смещением вверх.
    /// Дым и пыль затеняются обзором и ночью (их освещает огонь); огонь, вспышки и искры светятся сами (unshaded)
    /// и ставят PointLight2D — ночью выстрел и взрыв видно. Короткие эффекты вне кадра не создаются.
    /// </summary>
    public static class Vfx
    {
        const int MaxLive = 600;
        static readonly Vector2 Wind = new(7, 0);   // общий снос дыма, px/с

        sealed class P
        {
            public Sprite2D S;
            public PointLight2D L;
            public int Frames, Start;
            public float Fps, T, Life, Fade, Alpha, H, Rise, LightE, Drag;
            public bool Loop;
            public Vector2 Pos, Vel;
            public System.Action<P, float> Custom;   // особое поведение (ракета)
        }

        static Node2D _world, _flat;
        static readonly List<P> Live = new();
        static readonly Stack<P> Free = new();
        static readonly List<(Sprite2D s, float life, float max)> Scorches = new();
        static Rect2 _view = new(-1e6f, -1e6f, 2e6f, 2e6f);
        static readonly RandomNumberGenerator Rng = new();
        static CanvasItemMaterial _glow, _emberMat;
        static int _spawned;

        /// <summary>Свечение: не затеняется обзором и ночной тонировкой (светит само).</summary>
        static CanvasItemMaterial Glow => _glow ??= new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };

        static readonly HashSet<string> Bottom = new() { "dust_puff", "dust_step", "smoke_wisp", "steam_puff", "fire_small", "fire_torch", "fire_medium", "fire_big", "embers", "water_splash" };

        /// <summary>Ночь 0..1 (Main): днём вспышки света слабее, иначе аддитивный свет выжигает картинку.</summary>
        public static float Night;
        static float LightK => Mathf.Lerp(0.3f, 1f, Night);

        public static int LiveCount => Live.Count;
        public static int SpawnedTotal => _spawned;

        public static void Init(Node2D world, Node2D flat)
        {
            _world = world; _flat = flat;
            Live.Clear(); Free.Clear(); Scorches.Clear();
        }

        public static Texture2D Tex(string id) => Art.Get("fx/" + id);
        public static int FrameCount(Texture2D t) => t == null ? 1 : Mathf.Max(1, t.GetWidth() / Mathf.Max(1, t.GetHeight()));

        /// <summary>Кадр листа для самостоятельной отрисовки (DrawTextureRectRegion): номер кадра по времени.</summary>
        public static Rect2 Region(Texture2D t, int frame)
        {
            int h = t.GetHeight(), n = FrameCount(t);
            return new Rect2(((frame % n) + n) % n * h, 0, h, h);
        }

        /// <summary>Нарисовать кадр анимации (низ кадра — в точке at) из любого CanvasItem.</summary>
        public static void DrawFrame(CanvasItem ci, string id, Vector2 at, float time, float fps, int scale = 1, Color? mod = null, int phase = 0)
        {
            var t = Tex(id);
            if (t == null) return;
            int h = t.GetHeight() * scale;
            var r = Region(t, (int)(time * fps) + phase);
            ci.DrawTextureRectRegion(t, new Rect2((at - new Vector2(h / 2, h)).Round(), new Vector2(h, h)), r, mod ?? Colors.White);
        }

        /// <summary>Каждый кадр: продвинуть живые эффекты. view — видимая часть мира (px).</summary>
        public static void Update(float dt, Rect2 view)
        {
            _view = view;
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var p = Live[i];
                p.T += dt;
                if (p.Custom != null) p.Custom(p, dt);
                if (p.T >= p.Life) { Release(i); continue; }
                p.S.Visible = p.T >= 0;   // отложенный старт (клубы столба дыма по очереди)
                if (p.T < 0) continue;
                p.Vel *= Mathf.Max(0, 1 - p.Drag * dt);
                p.Pos += p.Vel * dt;
                p.H += p.Rise * dt;
                int f = p.Loop ? (p.Start + (int)(p.T * p.Fps)) % p.Frames : Mathf.Min(p.Frames - 1, p.Start + (int)(p.T * p.Fps));
                p.S.Frame = f;
                p.S.Position = p.Pos.Round();
                p.S.Offset = new Vector2(p.S.Offset.X, -Mathf.Round(p.H) + BaseOffsetY(p));
                float left = p.Life - p.T;
                float a = p.Fade > 0 && left < p.Fade ? left / p.Fade : 1f;
                p.S.SelfModulate = new Color(1, 1, 1, p.Alpha * a);
                if (p.L != null && p.L.Enabled)
                {
                    // вспышка света короче анимации: огненный шар гаснет, дым остаётся тёмным
                    float k = Mathf.Max(0, 1f - p.T / Mathf.Min(p.Life, 0.4f));
                    p.L.Energy = p.LightE * LightK * (p.Loop ? a : k * k);
                }
            }
            for (int i = Scorches.Count - 1; i >= 0; i--)
            {
                var (s, life, max) = Scorches[i];
                life -= dt;
                if (life <= 0) { s.QueueFree(); Scorches.RemoveAt(i); continue; }
                s.SelfModulate = new Color(1, 1, 1, Mathf.Min(0.6f, life / 8f));
                Scorches[i] = (s, life, max);
            }
        }

        static float BaseOffsetY(P p) => p.S.GetMeta("by", 0f).AsSingle();

        static void Release(int i)
        {
            var p = Live[i];
            Live.RemoveAt(i);
            p.S.Visible = false;
            if (p.L != null) p.L.Enabled = false;
            p.Custom = null;
            Free.Push(p);
        }

        static bool InView(Vector2 px, float margin) => _view.Grow(margin).HasPoint(px);

        /// <summary>
        /// Запустить анимацию: ground — точка на земле (px мира), h — высота над землёй (px экрана).
        /// life = 0 — один проход по кадрам; loop — крутить кадры всё время жизни.
        /// </summary>
        static P Spawn(string id, Vector2 ground, float h = 0, float fps = 12, float life = 0, bool loop = false, bool glow = false,
                       float alpha = 1, int z = 0, float margin = 160, int start = 0)
        {
            if (_world == null || Live.Count >= MaxLive || !InView(ground, margin)) return null;
            var tex = Tex(id);
            if (tex == null) return null;
            P p = Free.Count > 0 ? Free.Pop() : NewP();
            int n = FrameCount(tex);
            p.S.Texture = tex;
            p.S.Hframes = n;
            p.S.Frame = 0;
            p.S.Rotation = 0;
            p.S.FlipH = false;
            p.S.Scale = Vector2.One;
            p.S.Modulate = Colors.White;
            p.S.ZIndex = z;
            p.S.Material = glow ? Glow : VisionFx.ObjectMat;
            float half = tex.GetHeight() / 2f;
            p.S.Offset = Vector2.Zero;
            p.S.SetMeta("by", Bottom.Contains(id) ? -half : 0f);
            p.S.Visible = true;
            p.Frames = n; p.Start = start % n; p.Fps = fps; p.T = 0; p.Loop = loop;
            p.Life = life > 0 ? life : (n - p.Start) / fps;
            p.Fade = loop ? Mathf.Min(0.6f, p.Life * 0.4f) : 0;
            p.Alpha = alpha; p.H = h; p.Rise = 0; p.Vel = Vector2.Zero; p.Drag = 0; p.LightE = 0;
            p.Pos = ground;
            p.S.Position = ground.Round();
            p.S.Offset = new Vector2(0, -Mathf.Round(h) + BaseOffsetY(p));
            p.S.SelfModulate = new Color(1, 1, 1, alpha);
            if (p.L != null) p.L.Enabled = false;
            Live.Add(p);
            _spawned++;
            return p;
        }

        static P NewP()
        {
            var s = new Sprite2D { TextureFilter = CanvasItem.TextureFilterEnum.Nearest, Centered = true };
            _world.AddChild(s);
            return new P { S = s };
        }

        static void Light(P p, float sizeM, float energy, Color c, bool shadows = true)
        {
            if (p == null) return;
            // свет с тенями дорог: одновременно не больше 8 вспышек, тени — только у первых 3
            int lit = 0;
            foreach (var q in Live) if (q.L != null && q.L.Enabled) lit++;
            if (lit >= 8) return;
            if (lit >= 3) shadows = false;
            if (p.L == null)
            {
                p.L = Art.FireLight(sizeM, energy);
                p.S.AddChild(p.L);
            }
            p.L.TextureScale = sizeM * Iso.PPM * 2 / 256f;
            p.L.Color = c;
            p.L.ShadowEnabled = shadows;
            p.L.Position = new Vector2(0, p.S.Offset.Y);
            p.LightE = energy;
            p.L.Energy = energy * LightK;
            p.L.Enabled = true;
        }

        // ---------------- готовые эффекты ----------------

        /// <summary>Клуб дыма: поднимается и сносится ветром.</summary>
        public static void Puff(string id, Vector2 ground, float h, float rise = 18, float alpha = 0.85f, float fps = 6, int z = 3, Vector2 drift = default, float margin = 260)
        {
            var p = Spawn(id, ground + new Vector2(Rng.RandfRange(-3, 3), 0), h, fps * Rng.RandfRange(0.85f, 1.15f), alpha: alpha, z: z, margin: margin);
            if (p == null) return;
            p.Rise = rise;
            p.Vel = Wind * Rng.RandfRange(0.6f, 1.4f) + drift;
            p.Fade = p.Life * 0.35f;
            p.S.FlipH = Rng.Randf() < 0.5f;
        }

        /// <summary>Вспышка выстрела: лист по типу ствола, повёрнут по направлению выстрела; свет выдаёт стрелка ночью.</summary>
        public static void Muzzle(Vector2 muzzleGround, Vector2 dir, string weapon, bool noFlash)
        {
            string id = noFlash ? "muzzle_suppressed"
                : weapon is "weapon_pipeshotgun" or "weapon_sawnoff" or "weapon_mp133" or "weapon_saiga12" ? "muzzle_shotgun"
                : weapon is "weapon_nagant" or "weapon_grach" ? "muzzle_pistol" : "muzzle_rifle";
            // кадры 1–4: пик и затухание за ~70 мс
            var p = Spawn(id, muzzleGround, 0, 55, start: 1, glow: true, z: 2, margin: 64);
            if (p == null) return;
            p.Life = 4 / 55f;
            p.S.Position = (muzzleGround - new Vector2(0, Iso.GunH)).Round();
            p.Pos = muzzleGround - new Vector2(0, Iso.GunH);
            p.S.SetMeta("by", 0f);
            p.S.Offset = new Vector2(p.S.Texture.GetHeight() / 2f - 1, 0);   // левый край кадра — у дула
            p.S.Rotation = dir.Angle();
            if (!noFlash) Light(p, id == "muzzle_shotgun" ? 8f : 7f, 1.8f, new Color(1f, 0.75f, 0.4f));
            // пороховой дымок у дула
            if (id != "muzzle_pistol" || Rng.Randf() < 0.5f)
                Puff("smoke_puff", muzzleGround + dir * 6, Iso.GunH, 10, id == "muzzle_shotgun" ? 0.55f : 0.3f, 14, 2, dir * 12, 64);
        }

        /// <summary>Попадание: искры/щепки/крошка/кровь по материалу.</summary>
        public static void Hit(Vector2 ground, float h, HitMat m, Vector2 dir = default)
        {
            string id = m switch
            {
                HitMat.Metal or HitMat.Armor => "hit_sparks", HitMat.Wood => "hit_wood", HitMat.Flesh => "hit_blood",
                HitMat.Glass => "glass_shards", HitMat.Water => "water_splash", HitMat.Dust => "dust_step", _ => "hit_stone",
            };
            bool glow = id == "hit_sparks";
            var p = Spawn(id, ground, h, id == "water_splash" ? 16 : 22, glow: glow, z: 1, margin: 64);
            if (p == null) return;
            p.S.FlipH = dir.X < 0 || (dir == Vector2.Zero && Rng.Randf() < 0.5f);
            if (glow) Light(p, 2.5f, 1.2f, new Color(1f, 0.85f, 0.5f), false);
            if (m is HitMat.Stone or HitMat.Wood) Puff("dust_step", ground, Mathf.Max(0, h - 8), 6, 0.6f, 12, 1, default, 64);
        }

        /// <summary>Взрыв сумки (small) или C4 (big): огненный шар, кольцо по земле, пыль, столб чёрного дыма, подпалина.</summary>
        public static void Explosion(Vector2 ground, bool big)
        {
            var e = Spawn(big ? "explosion_big" : "explosion_small", ground, Iso.H(big ? 1.2f : 0.8f), 14, glow: true, z: 4, margin: 400);
            if (e != null)
            {
                Light(e, big ? 16f : 11f, 2.6f, new Color(1f, 0.7f, 0.35f));
                e.S.FlipH = Rng.Randf() < 0.5f;
            }
            var ring = Spawn("shockwave", ground, 0, 18, z: -1, alpha: 0.7f, margin: 400);
            if (ring != null) { ring.S.Scale = new Vector2(big ? 2 : 1, big ? 1 : 0.5f); ring.S.ZIndex = -1; }
            int dust = big ? 10 : 6;
            for (int i = 0; i < dust; i++)
            {
                float a = Mathf.Tau * i / dust + Rng.RandfRange(-0.2f, 0.2f);
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.5f);
                var p = Spawn("dust_puff", ground + d * (big ? 20 : 12), 0, 9, alpha: 0.85f, z: 2, margin: 400);
                if (p == null) continue;
                p.Vel = d * (big ? 90 : 60); p.Drag = 2.5f; p.Fade = p.Life * 0.4f;
            }
            // столб дыма догорает ещё несколько секунд
            int smoke = big ? 9 : 5;
            for (int i = 0; i < smoke; i++)
            {
                var p = Spawn("smoke_black", ground + new Vector2(Rng.RandfRange(-14, 14), Rng.RandfRange(-4, 4)), Iso.H(0.6f + i * 0.35f), 2.2f,
                              alpha: 0.8f, z: 4, margin: 400, start: 0);
                if (p == null) continue;
                p.T = -0.3f - 0.15f * i;   // появляются по очереди, когда огненный шар уже раскрылся
                p.Rise = 14 + i * 2; p.Vel = Wind * 1.3f; p.Fade = p.Life * 0.4f;
                p.S.FlipH = Rng.Randf() < 0.5f;
            }
            Scorch(ground, big ? 1.4f : 1f);
        }

        /// <summary>Молотов разбился: вспышка огня, осколки бутылки, клуб дыма.</summary>
        public static void Molotov(Vector2 ground)
        {
            var f = Spawn("fire_medium", ground, 0, 16, life: 0.5f, loop: true, glow: true, z: 2, margin: 300);
            if (f != null) Light(f, 7f, 2f, new Color(1f, 0.6f, 0.25f));
            Spawn("glass_shards", ground, 6, 18, glow: false, z: 2, margin: 300);
            Puff("smoke_black", ground, Iso.H(0.8f), 16, 0.7f, 3, 4);
        }

        /// <summary>Тёмное пятно копоти на земле после взрыва; тает за минуту.</summary>
        static void Scorch(Vector2 ground, float scale)
        {
            if (_flat == null) return;
            var t = Art.Get("decals/decal_burnt_spot");
            if (t == null) return;
            if (Scorches.Count > 24) { Scorches[0].s.QueueFree(); Scorches.RemoveAt(0); }
            var s = new Sprite2D { Texture = t, Position = ground.Round(), TextureFilter = CanvasItem.TextureFilterEnum.Nearest, Material = VisionFx.GroundMat };
            s.FlipH = Rng.Randf() < 0.5f;
            _flat.AddChild(s);
            Scorches.Add((s, 60f, 60f));
        }

        /// <summary>Пыль: удар о землю, приземление, обрушение, шаги (small).</summary>
        public static void Dust(Vector2 ground, bool small = false, float alpha = 0.8f)
        {
            var p = Spawn(small ? "dust_step" : "dust_puff", ground + new Vector2(Rng.RandfRange(-2, 2), 0), 0, small ? 12 : 10, alpha: alpha, z: small ? 0 : 2, margin: 64);
            if (p == null) return;
            p.S.FlipH = Rng.Randf() < 0.5f;
            p.Vel = Wind * 0.5f;
        }

        /// <summary>Обрушение постройки: кольцо пыли и серый дым.</summary>
        public static void Collapse(Vector2 ground, int size)
        {
            for (int i = 0; i < size; i++)
            {
                float a = Mathf.Tau * i / size;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.5f);
                var p = Spawn("dust_puff", ground + d * 14, Rng.RandfRange(0, 10), 8, alpha: 0.9f, z: 3, margin: 300);
                if (p == null) continue;
                p.Vel = d * 45; p.Drag = 2f; p.Fade = p.Life * 0.4f;
            }
            Puff("smoke_puff", ground, Iso.H(1f), 14, 0.7f, 5, 3, default, 300);
        }

        public static void Splash(Vector2 ground, bool small = false)
        {
            var p = Spawn("water_splash", ground + new Vector2(0, 2), 0, small ? 18 : 13, alpha: small ? 0.7f : 1f, z: 1, margin: 64);
            if (p != null) p.S.FlipH = Rng.Randf() < 0.5f;
        }

        /// <summary>Искры короткого замыкания (пробки, щиток).</summary>
        public static void Electric(Vector2 ground, float h, bool big)
        {
            var p = Spawn("spark_electric", ground, h, big ? 14 : 20, glow: true, z: 3, margin: 160);
            if (p == null) return;
            Light(p, big ? 9f : 4f, big ? 2f : 1.3f, new Color(0.6f, 0.8f, 1f), big);
            if (big) Puff("smoke_puff", ground, h, 12, 0.5f, 8, 3);
        }

        /// <summary>Пар/газ из трубы.</summary>
        public static void Steam(Vector2 ground, float h, int count = 3)
        {
            for (int i = 0; i < count; i++)
            {
                var p = Spawn("steam_puff", ground + new Vector2(i * 4, 0), h, 10, alpha: 0.8f, z: 3, margin: 160);
                if (p == null) continue;
                p.T = -0.12f * i; p.Rise = 10; p.Vel = Wind; p.Fade = p.Life * 0.4f;
            }
        }

        /// <summary>Цветной дым-маркер (зелёный/жёлтый/красный): облако на месте ивента.</summary>
        public static void SmokeBurst(string id, Vector2 ground, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var p = Spawn(id, ground + new Vector2(Rng.RandfRange(-10, 10), Rng.RandfRange(-4, 4)), Rng.RandfRange(4, 16), 4, alpha: 0.85f, z: 3, margin: 300);
                if (p == null) continue;
                p.T = -0.1f * i; p.Rise = 12; p.Vel = Wind + new Vector2(Rng.RandfRange(-10, 10), 0); p.Fade = p.Life * 0.4f;
                p.S.FlipH = Rng.Randf() < 0.5f;
            }
        }

        /// <summary>Мигание индикатора (считыватель, пищалка, сирена): маленький светящийся огонёк без круга.</summary>
        public static void Blink(Vector2 ground, float h, Color c, float sizeM, float life)
        {
            var p = Spawn("spark_electric", ground, h, 30, life: life, glow: true, z: 3, margin: 160, start: 6);
            if (p == null) return;
            p.Alpha = 0.9f;
            p.S.Modulate = c;
            p.Loop = true;
            Light(p, sizeM, 1.5f, c, false);
        }

        /// <summary>
        /// Сигнальная ракета «Маяка»: взлетает с красным дымным хвостом, вспыхивает и висит, медленно опускаясь,
        /// заливая округу красным светом (виден издалека).
        /// </summary>
        public static void Flare(Vector2 ground)
        {
            if (_world == null) return;
            var tex = Tex("flare_burn");
            if (tex == null) return;
            // ракете не нужна проверка кадра: висит 30 с, камера может подойти
            var p = Spawn("flare_burn", ground, Iso.H(1.2f), 12, life: 30, loop: true, glow: true, z: 6, margin: 1e6f);
            if (p == null) return;
            p.Fade = 3f;
            Light(p, 18f, 0f, new Color(1f, 0.25f, 0.18f), false);
            float top = Iso.H(10), trail = 0;
            p.Custom = (q, dt) =>
            {
                if (q.T < 1.6f)
                {
                    // подъём: быстро вверх, лёгкий снос
                    float k = q.T / 1.6f;
                    q.H = Mathf.Lerp(Iso.H(1.2f), top, 1 - (1 - k) * (1 - k));
                    q.Rise = 0;
                    q.Pos += new Vector2(6 * dt, 0);
                    q.S.Scale = Vector2.One;
                    q.LightE = 0.6f + 0.8f * k;
                    trail += dt;
                    while (trail > 0.08f) { trail -= 0.08f; Puff("smoke_red", q.Pos, q.H - 6, 4, 0.6f, 5, 5, default, 600); }
                }
                else
                {
                    // висит и медленно опускается, мерцая
                    q.Rise = -Iso.H(0.22f);
                    q.LightE = 1.5f + 0.3f * Mathf.Sin(q.T * 17) * Mathf.Sin(q.T * 6.1f);
                    trail += dt;
                    while (trail > 0.4f) { trail -= 0.4f; Puff("smoke_red", q.Pos, q.H - 4, 6, 0.45f, 3, 5, default, 600); }
                }
                if (q.L != null) q.L.Position = new Vector2(0, -q.H + BaseOffsetY(q));
            };
        }

        // ---------------- частицы ----------------

        /// <summary>Угольки над огнём: CpuParticles2D с листом embers (анимация частиц по кадрам).</summary>
        public static CpuParticles2D Embers(float widthPx, int amount)
        {
            var tex = Tex("embers");
            _emberMat ??= new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded, ParticlesAnimation = true, ParticlesAnimHFrames = FrameCount(tex), ParticlesAnimVFrames = 1, ParticlesAnimLoop = false };
            return new CpuParticles2D
            {
                Texture = tex, Material = _emberMat, Amount = amount, Lifetime = 1.4f, Explosiveness = 0, Randomness = 0.5f,
                EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle, EmissionRectExtents = new Vector2(widthPx / 2, 2),
                Direction = new Vector2(0, -1), Spread = 18, Gravity = new Vector2(6, -16), InitialVelocityMin = 14, InitialVelocityMax = 30,
                AnimSpeedMin = 1, AnimSpeedMax = 1, TextureFilter = CanvasItem.TextureFilterEnum.Nearest, LocalCoords = false,
            };
        }
    }
}
