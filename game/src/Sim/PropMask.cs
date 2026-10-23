using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Хитбокс укрытия-пропа (ящик, машина, хлам, бочка) по пикселям его PNG: пуля попадает, если на экране её точка
    /// (на высоте ствола) ложится на непрозрачный пиксель картинки. Картинка стоит как у клиента: по центру «следа»,
    /// низом на передний край, в родном размере. Нет картинки (headless) — старая коробка.
    /// </summary>
    public static class PropMask
    {
        public const float PPM = 32f, HS = 0.75f;               // = Client Iso
        public const float Lift = Hitbox.GunH * HS;              // м по Y: точка пули на экране выше её «следа»
        const int Sink = 2;                                      // картинка утоплена на 2 px (как рисует клиент)

        public sealed class Mask { public int W, H; public bool[] A; public float S = 1f; }   // S — масштаб, как рисует клиент (машина ×1,4)
        static readonly Dictionary<string, Mask> _cache = new();

        public static string Prop(WallKind k) => k switch
        {
            WallKind.Car => "props/car_wreck", WallKind.Barrier => "props/concrete_barrier", WallKind.Junk => "props/junk_pile",
            WallKind.Barrel => "props/barrel_oil", WallKind.WoodCrate => "props/crate_wood", _ => null,
        };

        public static string Styled(string basePath, Biome biome)
        {
            string suffix = biome switch { Biome.Tundra => "_snow", Biome.Arid or Biome.Beach => "_sand", _ => null };
            return suffix != null && Get(basePath + suffix) != null ? basePath + suffix : basePath;
        }

        public static Mask Get(string path)
        {
            if (_cache.TryGetValue(path, out var m)) return m;
            m = null;
            try
            {
                string res = $"res://art/{path}.png";
                if (Godot.ResourceLoader.Exists(res) && Godot.GD.Load<Godot.Texture2D>(res)?.GetImage() is Godot.Image img)
                {
                    if (img.IsCompressed()) img.Decompress();
                    m = new Mask { W = img.GetWidth(), H = img.GetHeight(), A = new bool[img.GetWidth() * img.GetHeight()], S = path.Contains("car_wreck") ? 1.4f : 1f };
                    for (int y = 0; y < m.H; y++) for (int x = 0; x < m.W; x++) m.A[y * m.W + x] = img.GetPixel(x, y).A > 0.4f;
                }
            }
            catch { m = null; }
            return _cache[path] = m;
        }

        /// <summary>Область «следов» пули (м, на земле), где она может задеть картинку.</summary>
        public static (Vector2 min, Vector2 max) Region(Vector2 wMin, Vector2 wMax, Mask m)
        {
            float cx = (wMin.X + wMax.X) * 0.5f, bottom = wMax.Y + Sink * m.S / PPM;
            return (new Vector2(cx - m.W * m.S / 2f / PPM, bottom - m.H * m.S / PPM + Lift), new Vector2(cx + m.W * m.S / 2f / PPM, bottom + Lift));
        }

        /// <summary>Пуля a→b задевает картинку: доля пути t (0..1) первого непрозрачного пикселя.</summary>
        public static bool Hit(Vector2 wMin, Vector2 wMax, Mask m, Vector2 a, Vector2 b, out float t)
        {
            t = 2;
            var (min, max) = Region(wMin, wMax, m);
            if (!Geo.SegmentAabb(a, b, min, max, out float t0)) return false;
            float t1 = Geo.SegmentAabb(b, a, min, max, out float r) ? 1f - r : 1f;   // выход из области
            float len = Vector2.Distance(a, b);
            if (len < 1e-5f) return false;
            float step = 0.5f / PPM / len;                       // полпикселя
            for (float s = t0; s <= t1 + step; s += step)
            {
                var p = a + (b - a) * System.MathF.Min(s, 1f);
                int px = (int)((p.X - min.X) * PPM / m.S), py = (int)((p.Y - min.Y) * PPM / m.S);
                if (px < 0 || py < 0 || px >= m.W || py >= m.H) continue;
                if (m.A[py * m.W + px]) { t = s; return true; }
            }
            return false;
        }
    }
}
