using Godot;
using Rastvordd.Sim;
using Sky = Rastvordd.Sim.Sky;

namespace Rastvordd.Client
{
    /// <summary>
    /// Частицы вокруг камеры по биому: листья (лес, поле), снег (тайга), пыль (пустошь, пляж), светлячки ночью.
    /// Частицы — по 2–3 пикселя, как и весь пиксель-арт. Светлячки на своём слое, чтобы ночь их не гасила.
    /// </summary>
    public sealed class Weather
    {
        readonly GpuParticles2D _leaves, _snow, _dust, _fireflies;
        // погода (Sky): осадки, брызги, полосы ветра, сорванные листья; экран — тонировка/дымка и вспышки молний
        readonly GpuParticles2D _rain, _splash, _flakes, _streaks, _debris;
        readonly ColorRect _tint, _flash;
        float _flashA, _nextBolt = 6f, _boltT = -1;
        float _storm, _blizz;   // доли грозы и бурана в текущей (смешанной) погоде
        GpuParticles2D[] _all;   // без выделений памяти каждый кадр
        readonly RandomNumberGenerator _rng = new();

        public Weather(Node world, Node root)
        {
            _leaves = Make(world, 28, 7f, new Color(0.78f, 0.68f, 0.3f), new Vector3(10, 16, 0), new Vector3(1, 1, 0), 12, 22, 1f, 1.6f, 0.9f);
            _snow = Make(world, 260, 6f, new Color(0.93f, 0.95f, 1f), new Vector3(5, 26, 0), new Vector3(0.3f, 1, 0), 18, 32, 1f, 1.5f, 0.9f);
            _dust = Make(world, 70, 4f, new Color(0.78f, 0.68f, 0.5f), new Vector3(26, 2, 0), new Vector3(1, 0.1f, 0), 25, 45, 1f, 2f, 0.45f);

            _rain = Make(world, 1, 0.55f, new Color(0.78f, 0.84f, 0.95f), Vector3.Zero, new Vector3(0, 1, 0), 480, 560, 1f, 1f, 0.55f, new Vector2I(1, 7));
            _splash = Make(world, 1, 0.22f, new Color(0.85f, 0.9f, 1f), Vector3.Zero, new Vector3(0, -1, 0), 4, 10, 1f, 1.5f, 0.6f);
            _flakes = Make(world, 1, 7f, new Color(0.95f, 0.97f, 1f), Vector3.Zero, new Vector3(0, 1, 0), 30, 50, 1f, 2f, 0.9f);
            _streaks = Make(world, 1, 0.9f, new Color(0.9f, 0.92f, 0.95f), Vector3.Zero, new Vector3(1, 0, 0), 520, 720, 1f, 1f, 0.4f, new Vector2I(1, 18));   // полоса вдоль скорости (AlignY)
            _debris = Make(world, 1, 2.2f, new Color(0.55f, 0.5f, 0.32f), new Vector3(0, 30, 0), new Vector3(1, 0.15f, 0), 260, 380, 1f, 2f, 0.85f);
            ((ParticleProcessMaterial)_rain.ProcessMaterial).ParticleFlagAlignY = true;
            ((ParticleProcessMaterial)_streaks.ProcessMaterial).ParticleFlagAlignY = true;
            ((ParticleProcessMaterial)_rain.ProcessMaterial).Spread = 3;
            ((ParticleProcessMaterial)_streaks.ProcessMaterial).Spread = 4;
            ((ParticleProcessMaterial)_debris.ProcessMaterial).TurbulenceEnabled = true;
            ((ParticleProcessMaterial)_flakes.ProcessMaterial).TurbulenceEnabled = true;
            ((ParticleProcessMaterial)_flakes.ProcessMaterial).TurbulenceNoiseStrength = 2f;
            _splash.ZIndex = 5;
            // плавность: Amount задан один раз по максимуму (смена Amount перезапускает систему), густоту меняет AmountRatio
            foreach (var (p, n) in new[] { (_rain, MaxRain), (_splash, MaxRain / 3), (_flakes, MaxSnow), (_streaks, MaxStreaks), (_debris, MaxDebris) })
            { p.Amount = n; p.AmountRatio = 0f; p.Lifetime = p == _flakes ? 3.5f : p.Lifetime; p.Preprocess = 0; }
            // экран: тонировка (сумрак дождя / белая мгла снега) и вспышка молнии — между миром и HUD
            var scr = new CanvasLayer { Layer = 0 };
            root.AddChild(scr);
            _tint = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Color = new Color(0, 0, 0, 0) };
            _tint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _flash = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Color = new Color(0.9f, 0.93f, 1f, 0) };
            _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            scr.AddChild(_tint); scr.AddChild(_flash);

            // светлячки: свой слой поверх мира (ночная тонировка мира их не гасит), мигают
            var layer = new CanvasLayer { Layer = 0, FollowViewportEnabled = true };
            root.AddChild(layer);
            _fireflies = Make(layer, 40, 5f, new Color(0.95f, 1f, 0.55f), Vector3.Zero, new Vector3(0, -1, 0), 3, 8, 1f, 1.5f, 1f);
            var pm = (ParticleProcessMaterial)_fireflies.ProcessMaterial;
            pm.Spread = 180;
            pm.TurbulenceEnabled = true;
            pm.TurbulenceNoiseStrength = 3f;
            pm.TurbulenceNoiseScale = 2f;
            pm.ColorRamp = new GradientTexture1D
            {
                Gradient = new Gradient
                {
                    Offsets = new[] { 0f, 0.2f, 0.35f, 0.5f, 0.7f, 0.85f, 1f },
                    Colors = new[] { new Color(1, 1, 1, 0), Colors.White, new Color(1, 1, 1, 0.2f), Colors.White, new Color(1, 1, 1, 0.3f), Colors.White, new Color(1, 1, 1, 0) },
                },
            };
        }

        static GpuParticles2D Make(Node parent, int amount, float life, Color color, Vector3 gravity, Vector3 dir, float vMin, float vMax, float sMin, float sMax, float alpha, Vector2I? size = null)
        {
            var sz = size ?? new Vector2I(2, 2);
            var img = Image.CreateEmpty(sz.X, sz.Y, false, Image.Format.Rgba8);
            img.Fill(Colors.White);
            var pm = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = new Vector3(600, 400, 0),
                Gravity = gravity,
                Direction = dir,
                Spread = 25,
                InitialVelocityMin = vMin, InitialVelocityMax = vMax,
                ScaleMin = sMin, ScaleMax = sMax,
                Color = new Color(color, alpha),
            };
            var p = new GpuParticles2D
            {
                Amount = amount, Lifetime = life, Preprocess = life, Emitting = false,
                LocalCoords = false, ProcessMaterial = pm, Texture = ImageTexture.CreateFromImage(img),
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest, ZIndex = 20,
            };
            parent.AddChild(p);
            return p;
        }

        /// <param name="viewHalfPx">Половина видимой области в пикселях мира.</param>
        public void Update(Vector2 cameraPx, Vector2 viewHalfPx, Biome biome, float night, Sky sky = Sky.Clear, float dt = 0.016f, float windAngle = 0, float windStrength = 0, Sky prev = Sky.Clear, float blend = 1f)
        {
            var ext = new Vector3(viewHalfPx.X + 60, viewHalfPx.Y + 60, 0);
            SetSky(prev, sky, blend);
            Wind(windAngle, windStrength);
            UpdateSky(dt);
            _all ??= new[] { _leaves, _snow, _dust, _fireflies, _rain, _splash, _flakes, _streaks, _debris };
            foreach (var p in _all)
            {
                p.GlobalPosition = cameraPx;
                ((ParticleProcessMaterial)p.ProcessMaterial).EmissionBoxExtents = ext;
                p.VisibilityRect = new Rect2(-ext.X - 200, -ext.Y - 200, ext.X * 2 + 400, ext.Y * 2 + 400);
            }
            Set(_leaves, biome is Biome.Forest or Biome.Grassland or Biome.Swamp);
            Set(_snow, biome == Biome.Tundra && !SkyInfo.Snowy(sky) && !SkyInfo.Snowy(prev));
            Set(_dust, biome is Biome.Arid or Biome.Beach);
            Set(_fireflies, night > 0.6f && biome is Biome.Grassland or Biome.Swamp or Biome.Forest);
        }

        const int MaxRain = 1700, MaxSnow = 2200, MaxStreaks = 260, MaxDebris = 160;
        static float RainN(Sky s) => s switch { Sky.Rain => 500, Sky.Downpour => 1400, Sky.Storm => 1700, _ => 0 };
        static float SnowN(Sky s) => s switch { Sky.Snow => 380, Sky.HeavySnow => 1100, Sky.Blizzard => 2200, _ => 0 };
        static float StreakN(Sky s) => s switch { Sky.Wind => 220, Sky.Storm => 120, Sky.HeavySnow => 60, Sky.Blizzard => 260, _ => 0 };
        static float DebrisN(Sky s) => s switch { Sky.Wind => 160, Sky.Storm => 90, _ => 0 };
        static float SnowV(Sky s, bool hi) => s == Sky.Blizzard ? (hi ? 640 : 460) : s == Sky.HeavySnow ? (hi ? 190 : 130) : (hi ? 115 : 75);
        static Color Tint(Sky s) => s switch
        {
            Sky.Rain => new Color(0.08f, 0.1f, 0.16f, 0.12f), Sky.Downpour => new Color(0.06f, 0.08f, 0.13f, 0.26f),
            Sky.Storm => new Color(0.04f, 0.05f, 0.1f, 0.34f), Sky.HeavySnow => new Color(0.86f, 0.9f, 0.96f, 0.16f),
            Sky.Blizzard => new Color(0.88f, 0.91f, 0.96f, 0.42f), Sky.Snow => new Color(0.86f, 0.9f, 0.96f, 0.06f),
            Sky.Wind => new Color(0.5f, 0.48f, 0.4f, 0.05f), _ => new Color(0, 0, 0, 0),
        };
        static Color TintMix(Color a, Color b, float t)   // прозрачный конец не тянет цвет к чёрному
        {
            var c = a.A <= 0.001f ? new Color(b, 0) : a; var d = b.A <= 0.001f ? new Color(a, 0) : b;
            return c.Lerp(d, t);
        }

        /// <summary>Погода каждый кадр, смешанно: старые осадки стихают, тучи/цвет приходят, затем новые осадки (без перезапуска систем).</summary>
        void SetSky(Sky prev, Sky sky, float blend)
        {
            float cw = SkyInfo.CloudW(blend), pi = SkyInfo.PrecipIn(blend), po = SkyInfo.PrecipOut(blend);
            float wind = Mathf.Lerp(SkyInfo.Wind(prev), SkyInfo.Wind(sky), cw);
            float rain = RainN(prev) * po + RainN(sky) * pi, snow = SnowN(prev) * po + SnowN(sky) * pi;
            float streaks = StreakN(prev) * (1 - cw) + StreakN(sky) * cw, debris = DebrisN(prev) * (1 - cw) + DebrisN(sky) * cw;
            _storm = (prev == Sky.Storm ? po : 0) + (sky == Sky.Storm ? pi : 0);
            _blizz = (prev == Sky.Blizzard ? po : 0) + (sky == Sky.Blizzard ? pi : 0);
            float sw = snow > 1 ? SnowN(sky) * pi / snow : 0;   // доля новой погоды в снегопаде
            Conf(_rain, rain / MaxRain, 480 + 120 * wind, 600 + 160 * wind);
            Conf(_splash, rain / MaxRain, 4, 10);
            ((ParticleProcessMaterial)_flakes.ProcessMaterial).ScaleMax = Mathf.Lerp(prev == Sky.Snow ? 1.6f : 2.4f, sky == Sky.Snow ? 1.6f : 2.4f, sw);
            Conf(_flakes, snow / MaxSnow, Mathf.Lerp(SnowV(prev, false), SnowV(sky, false), sw), Mathf.Lerp(SnowV(prev, true), SnowV(sky, true), sw));
            Conf(_streaks, streaks / MaxStreaks, 520 + 200 * wind, 720 + 260 * wind);
            Conf(_debris, debris / MaxDebris, 260, 380);
            _tint.Color = TintMix(Tint(prev), Tint(sky), cw);
        }

        /// <summary>Частицы — по ветру: дождь косит, снег сносит (в буране — почти горизонтально), полосы и мусор летят по ветру.</summary>
        void Wind(float angle, float strength)
        {
            var w = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * 0.6f, 0) * strength;   // глубина в виде 3/4 сжата
            void Dir(GpuParticles2D p, Vector3 d) => ((ParticleProcessMaterial)p.ProcessMaterial).Direction = d.Normalized();
            Dir(_rain, new Vector3(w.X * 0.7f, 1 + w.Y * 0.4f, 0));
            Dir(_flakes, new Vector3(w.X * 1.6f, 1 + w.Y, 0).Lerp(new Vector3(w.X * 4f, 1 + w.Y * 3f, 0), _blizz));
            if (strength > 0.05f) { Dir(_streaks, w); Dir(_debris, w + new Vector3(0, 0.1f, 0)); }
        }

        /// <summary>Цветокор по погоде: умножается на цвет времени суток (дождь — серо-синий, снег — холодный, ветер — пыльный).</summary>
        public static Color Grade(Sky prev, Sky sky, float blend) => Grade(prev).Lerp(Grade(sky), SkyInfo.CloudW(blend));
        public static Color Grade(Sky sky) => sky switch
        {
            Sky.Rain => new Color(0.86f, 0.89f, 0.95f), Sky.Downpour => new Color(0.78f, 0.82f, 0.9f), Sky.Storm => new Color(0.7f, 0.74f, 0.84f),
            Sky.Snow => new Color(0.94f, 0.97f, 1f), Sky.HeavySnow => new Color(0.9f, 0.94f, 1f), Sky.Blizzard => new Color(0.86f, 0.9f, 0.98f),
            Sky.Wind => new Color(0.97f, 0.96f, 0.92f), _ => Colors.White,
        };

        static void Conf(GpuParticles2D p, float ratio, float vMin, float vMax)
        {
            var pm = (ParticleProcessMaterial)p.ProcessMaterial;
            pm.InitialVelocityMin = vMin; pm.InitialVelocityMax = vMax;
            ratio = Mathf.Clamp(ratio, 0f, 1f);
            if (Mathf.Abs(p.AmountRatio - ratio) > 0.002f || (ratio == 0 && p.AmountRatio != 0)) p.AmountRatio = ratio;
            bool on = ratio > 0.002f;
            if (p.Emitting != on) p.Emitting = on;   // выключается только на нуле густоты — без щелчка
        }

        /// <summary>Гроза: молния раз в 5–14 с — двойная вспышка экрана.</summary>
        void UpdateSky(float dt)
        {
            _flashA = 0;
            if (_storm > 0.5f)
            {
                if ((_nextBolt -= dt) <= 0) { _nextBolt = _rng.RandfRange(5f, 14f); _boltT = 0; }
                if (_boltT >= 0)
                {
                    _boltT += dt;
                    _flashA = _boltT < 0.06f ? 0.75f : _boltT < 0.14f ? 0.1f : _boltT < 0.22f ? 0.55f : Mathf.Max(0, 0.55f - (_boltT - 0.22f) * 1.6f);
                    if (_boltT > 0.6f) _boltT = -1;
                }
            }
            _flash.Color = new Color(_flash.Color, _flashA);
        }

        static void Set(GpuParticles2D p, bool on)
        {
            if (p.Emitting != on) p.Emitting = on;
        }
    }
}
