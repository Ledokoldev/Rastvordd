using System;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>Порода дерева (вид спрайта). 0 — «как раньше»: вид по биому и id (старые сохранения).</summary>
    public enum TreeSpecies : byte { Auto, Pine, Spruce, Birch, Oak, Aspen, Poplar, Rowan, Willow, Apple, Dead, Coastal }

    /// <summary>Возраст дерева: подрост, молодое, взрослое, старое. 0 — неизвестен (старые сохранения).</summary>
    public enum TreeAge : byte { Unknown, Sapling, Young, Mature, Old }

    /// <summary>
    /// Условия места для растений — поля, которые считаются из рельефа и биомов (одинаково на сервере и у клиента,
    /// у клиента — по сетке MapInfo): влажность, свет, высота, крутизна, близость воды и опушки.
    /// </summary>
    public struct Site
    {
        public Biome Biome;
        public float Depth;      // глубина внутри своего биома, м (0 — граница, 30 — глушь): опушка или чаща
        public float WaterDist;  // до воды, м (до 30)
        public float Height;     // высота рельефа (0 — берег, ~1 — возвышенность)
        public float Slope;      // крутизна: перепад высоты на метр ×100
        public float SouthSun;   // южный склон (+) — суше и светлее, северный (−) — сырее
        /// <summary>Влажность 0..1: болото, у воды, в низине, на северном склоне — сырее; на возвышенности и южном склоне — суше.</summary>
        public float Wet;
        /// <summary>Тень 0..1: под сомкнутым пологом леса (глубь леса); опушка, поляна, поле — свет.</summary>
        public float Shade;
        public bool ForestNear;  // лес в пределах ~8 м (опушка со стороны поля)
        /// <summary>Круча 0..1: верхние ~15 % крутизны острова (склоны, у скал).</summary>
        public readonly float Steep => Math.Clamp((Slope - 0.9f) / 0.6f, 0, 1);
    }

    /// <summary>Чтение условий места по сетке биомов и высот (TerrainMap на сервере, MapInfo у клиента).</summary>
    public sealed class SiteMap
    {
        readonly int _n; readonly float _cell, _half; readonly Biome[] _b; readonly float[] _h;

        public SiteMap(int n, float cell, float halfSize, Biome[] biomes, float[] heights) { _n = n; _cell = cell; _half = halfSize; _b = biomes; _h = heights; }

        int Index(Vector2 p)
        {
            int x = (int)((p.X + _half) / _cell), y = (int)((p.Y + _half) / _cell);
            return x < 0 || y < 0 || x >= _n || y >= _n ? -1 : y * _n + x;
        }
        public Biome BiomeAt(Vector2 p) { int i = Index(p); return i < 0 ? Biome.Water : _b[i]; }
        public float HeightAt(Vector2 p) { int i = Index(p); return i < 0 || _h == null ? -1 : _h[i]; }

        static readonly Vector2[] Dirs = MakeDirs(12);
        static Vector2[] MakeDirs(int n) { var d = new Vector2[n]; for (int i = 0; i < n; i++) d[i] = new Vector2(MathF.Cos(i * MathF.Tau / n), MathF.Sin(i * MathF.Tau / n)); return d; }
        static readonly float[] Rings = { 2.5f, 5f, 8f, 12f, 17f, 24f };

        /// <summary>Глубина внутри своего биома: до ближайшей клетки другого биома (кольца до 24 м; дальше — 30).</summary>
        public float EdgeDepth(Vector2 p)
        {
            var b = BiomeAt(p);
            foreach (float r in Rings)
                foreach (var d in Dirs)
                    if (BiomeAt(p + d * r) != b) return r;
            return 30f;
        }

        public Site At(Vector2 p)
        {
            var s = new Site { Biome = BiomeAt(p), Height = MathF.Max(0, HeightAt(p)), WaterDist = 30, Depth = 30 };
            // одним обходом колец: граница биома, вода, лес рядом
            bool edgeFound = false;
            foreach (float r in Rings)
                foreach (var d in Dirs)
                {
                    var q = BiomeAt(p + d * r);
                    if (!edgeFound && q != s.Biome) { s.Depth = r; edgeFound = true; }
                    if (q == Biome.Water && r < s.WaterDist) s.WaterDist = r;
                    if (q == Biome.Forest && r <= 8) s.ForestNear = true;
                }
            // рельеф: разности высот на ±3 м (Y растёт на юг — экран вниз)
            float hx = HeightAt(p + new Vector2(3, 0)) - HeightAt(p - new Vector2(3, 0));
            float hy = HeightAt(p + new Vector2(0, 3)) - HeightAt(p - new Vector2(0, 3));
            s.Slope = MathF.Sqrt(hx * hx + hy * hy) / 6f * 100f;
            s.SouthSun = -hy / 6f * 100f;   // высота падает к югу — склон смотрит на юг, на солнце
            // влажность: правило 1 — у воды и в низинах сыро, на возвышенности и южном склоне сухо
            float wet = s.Biome switch { Biome.Swamp => 0.85f, Biome.Arid => 0.1f, Biome.Beach => 0.35f, Biome.Tundra => 0.45f, Biome.Forest => 0.5f, _ => 0.4f };
            wet += MathF.Max(0, 1 - s.WaterDist / 14f) * 0.35f;              // у воды
            wet += (0.45f - MathF.Min(s.Height, 1f)) * 0.3f;                  // низина / возвышенность
            wet -= Math.Clamp(s.SouthSun, -1, 1) * 0.12f;                      // южный склон суше
            s.Wet = Math.Clamp(wet, 0, 1);
            // свет: правило 2 — в глуби леса тень (полог сомкнут), опушка и поляны светлые
            s.Shade = s.Biome == Biome.Forest ? Math.Clamp((s.Depth - 3f) / 14f, 0, 1) : 0;
            return s;
        }
    }

    /// <summary>
    /// Пригодность места для породы — явные правила, почему дерево растёт здесь (0 — не растёт, ~1 — обычно, &gt;1 — любит).
    /// Множители перемножаются: влажность × свет × рельеф × климат биома.
    /// </summary>
    public static class Ecology
    {
        /// <summary>Колокол предпочтения: 1 в оптимуме, падает к краям допуска.</summary>
        static float Pref(float v, float best, float tol) { float d = (v - best) / tol; return MathF.Max(0.05f, 1 - d * d); }

        public static float TreeSuit(TreeSpecies sp, in Site s, float conifer)
        {
            float steep = s.Steep;   // крутой склон: деревьям тяжело, кроме сосны
            float k = sp switch
            {
                // ива (ольха): только сырое — у воды, в низинах, на болоте
                TreeSpecies.Willow => Pref(s.Wet, 0.95f, 0.35f) * (s.WaterDist < 10 || s.Biome == Biome.Swamp ? 1.6f : 0.4f),
                // сосна: сухие возвышенности, песок, южные склоны, держится и на круче
                TreeSpecies.Pine => Pref(s.Wet, 0.25f, 0.4f) * (1 + Math.Clamp(s.SouthSun, 0, 1) * 0.5f) * (1 + MathF.Max(0, conifer) * 1.2f),
                // ель: теневынослива, свежая влажная почва, глубь хвойного леса
                TreeSpecies.Spruce => Pref(s.Wet, 0.6f, 0.35f) * (0.4f + s.Shade) * (1 + MathF.Max(0, conifer) * 1.6f) * (1 - steep * 0.6f),
                // берёза: пионер — свет, опушки, поляны, гари; сырость терпит
                TreeSpecies.Birch => Pref(s.Wet, 0.5f, 0.5f) * (1.3f - s.Shade * 0.8f) * (1 + MathF.Max(0, -conifer) * 1.2f),
                // осина: пионер на свежих местах, опушки
                TreeSpecies.Aspen => Pref(s.Wet, 0.6f, 0.4f) * (1.2f - s.Shade * 0.7f) * (1 + MathF.Max(0, -conifer)),
                // дуб: плодородные ровные места средней влажности, любит свет — в лесу реже, в поле одинокий великан
                TreeSpecies.Oak => Pref(s.Wet, 0.45f, 0.3f) * (1 - steep * 0.7f) * (1 + MathF.Max(0, -conifer) * 0.8f) * (1.1f - s.Shade * 0.4f),
                // рябина: подлесок и опушка
                TreeSpecies.Rowan => Pref(s.Wet, 0.5f, 0.4f) * (s.Depth < 8 ? 1.4f : 0.5f),
                TreeSpecies.Poplar => Pref(s.Wet, 0.65f, 0.3f) * (s.WaterDist < 20 ? 1.2f : 0.6f),
                TreeSpecies.Apple => Pref(s.Wet, 0.45f, 0.25f) * (s.Shade < 0.2f ? 1 : 0.1f),   // одичавшая яблоня — только на свету
                // сухостой: пересохшая пустошь или подтопленное болото
                TreeSpecies.Dead => s.Biome == Biome.Arid ? 1.5f : s.Biome == Biome.Swamp ? 0.8f + s.Wet * 0.5f : 0.05f,
                TreeSpecies.Coastal => s.Biome == Biome.Beach ? 1.5f : 0.05f,
                _ => 0,
            };
            // климат биома: что вообще встречается здесь
            float clim = (s.Biome, sp) switch
            {
                (Biome.Tundra, TreeSpecies.Spruce or TreeSpecies.Pine) => 1.5f,
                (Biome.Tundra, TreeSpecies.Birch) => 0.6f,
                (Biome.Tundra, _) => 0,
                (Biome.Arid, TreeSpecies.Dead) => 1,
                (Biome.Arid, TreeSpecies.Pine) => 0.4f,
                (Biome.Arid, _) => 0,
                (Biome.Swamp, TreeSpecies.Willow or TreeSpecies.Dead or TreeSpecies.Birch) => 1,
                (Biome.Swamp, _) => 0.05f,
                (Biome.Beach, TreeSpecies.Coastal) => 1,
                (Biome.Beach, TreeSpecies.Pine) => 0.2f,
                (Biome.Beach, _) => 0,
                (_, TreeSpecies.Coastal) => 0,
                _ => 1,
            };
            return k * clim;
        }

        static readonly TreeSpecies[] All =
            { TreeSpecies.Pine, TreeSpecies.Spruce, TreeSpecies.Birch, TreeSpecies.Oak, TreeSpecies.Aspen, TreeSpecies.Poplar, TreeSpecies.Rowan, TreeSpecies.Willow, TreeSpecies.Apple, TreeSpecies.Dead, TreeSpecies.Coastal };
        /// <summary>Базовая встречаемость породы (редкие — реже, даже если место подходит).</summary>
        static float Base(TreeSpecies sp) => sp switch { TreeSpecies.Poplar => 0.25f, TreeSpecies.Apple => 0.15f, TreeSpecies.Rowan => 0.5f, TreeSpecies.Dead => 0.4f, _ => 1 };

        /// <summary>Порода по пригодности места: взвешенный выбор из всех пород.</summary>
        public static TreeSpecies PickTree(Random r, in Site s, float conifer)
        {
            Span<float> w = stackalloc float[All.Length];
            float sum = 0;
            for (int i = 0; i < All.Length; i++) { w[i] = TreeSuit(All[i], s, conifer) * Base(All[i]); sum += w[i]; }
            if (sum <= 0) return TreeSpecies.Pine;
            float x = (float)r.NextDouble() * sum;
            for (int i = 0; i < All.Length; i++) { if (x < w[i]) return All[i]; x -= w[i]; }
            return All[^1];
        }
    }
}
