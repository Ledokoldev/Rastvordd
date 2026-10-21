using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Частицы вокруг камеры по биому: листья (лес, поле), снег (тайга), пыль (пустошь, пляж), светлячки ночью.
    /// Частицы — по 2–3 пикселя, как и весь пиксель-арт. Светлячки на своём слое, чтобы ночь их не гасила.
    /// </summary>
    public sealed class Weather
    {
        readonly GpuParticles2D _leaves, _snow, _dust, _fireflies;

        public Weather(Node world, Node root)
        {
            _leaves = Make(world, 28, 7f, new Color(0.78f, 0.68f, 0.3f), new Vector3(10, 16, 0), new Vector3(1, 1, 0), 12, 22, 1f, 1.6f, 0.9f);
            _snow = Make(world, 260, 6f, new Color(0.93f, 0.95f, 1f), new Vector3(5, 26, 0), new Vector3(0.3f, 1, 0), 18, 32, 1f, 1.5f, 0.9f);
            _dust = Make(world, 70, 4f, new Color(0.78f, 0.68f, 0.5f), new Vector3(26, 2, 0), new Vector3(1, 0.1f, 0), 25, 45, 1f, 2f, 0.45f);

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

        static GpuParticles2D Make(Node parent, int amount, float life, Color color, Vector3 gravity, Vector3 dir, float vMin, float vMax, float sMin, float sMax, float alpha)
        {
            var img = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
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
        public void Update(Vector2 cameraPx, Vector2 viewHalfPx, Biome biome, float night)
        {
            var ext = new Vector3(viewHalfPx.X + 60, viewHalfPx.Y + 60, 0);
            foreach (var p in new[] { _leaves, _snow, _dust, _fireflies })
            {
                p.GlobalPosition = cameraPx;
                ((ParticleProcessMaterial)p.ProcessMaterial).EmissionBoxExtents = ext;
                p.VisibilityRect = new Rect2(-ext.X - 200, -ext.Y - 200, ext.X * 2 + 400, ext.Y * 2 + 400);
            }
            Set(_leaves, biome is Biome.Forest or Biome.Grassland or Biome.Swamp);
            Set(_snow, biome == Biome.Tundra);
            Set(_dust, biome is Biome.Arid or Biome.Beach);
            Set(_fireflies, night > 0.6f && biome is Biome.Grassland or Biome.Swamp or Biome.Forest);
        }

        static void Set(GpuParticles2D p, bool on)
        {
            if (p.Emitting != on) p.Emitting = on;
        }
    }
}
