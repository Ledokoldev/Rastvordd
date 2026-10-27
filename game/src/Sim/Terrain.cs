using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    public enum Biome : byte { Water, Beach, Grassland, Forest, Arid, Tundra, Swamp }

    /// <summary>
    /// Остров: высоты и биомы на сетке (клетка Cell метров). Черновой генератор:
    /// береговая линия по шуму, север холоднее (тундра), юг суше (пустошь), посередине поля и леса.
    /// </summary>
    public sealed class TerrainMap
    {
        public const float Cell = 2f;

        public readonly float HalfSize;
        public readonly int N;
        public readonly float[] Height;    // < 0 — вода
        public readonly Biome[] Biomes;

        readonly Noise2 _shape, _height, _forest, _climate, _wet;
        readonly int Seed;

        public TerrainMap(float halfSize, int seed)
        {
            HalfSize = halfSize;
            N = (int)(halfSize * 2 / Cell);
            Height = new float[N * N];
            Biomes = new Biome[N * N];
            River = new byte[N * N];
            Bridge = new bool[N * N];
            RiverNearM = new byte[N * N];
            Seed = seed;
            _shape = new Noise2(seed);
            _height = new Noise2(seed + 1);
            _forest = new Noise2(seed + 2);
            _climate = new Noise2(seed + 3);
            _wet = new Noise2(seed + 4);
            Generate();
        }

        public Vector2 CellCenter(int x, int y) => new Vector2((x + 0.5f) * Cell - HalfSize, (y + 0.5f) * Cell - HalfSize);

        /// <summary>Река: 1 — русло (мелкая вода, вброд), 3 — озеро (тихая вода), 2 — берег (песок/галька). Мосты делают русло сушей (IsLand).</summary>
        public readonly byte[] River;
        public readonly bool[] Bridge;
        readonly byte[] RiverNearM;
        /// <summary>До русла реки, м (≤ 40; 255 — далеко).</summary>
        public float RiverNear(Vector2 p) { int i = Index(p); return i < 0 ? 255 : RiverNearM[i]; }
        /// <summary>Осевые линии рек (главная и приток): от истока к устью, точки через ~2 м, с шириной.</summary>
        public readonly List<(Vector2 p, float w)[]> RiverLines = new();

        /// <summary>Сдвиг координат шумом (domain warp): границы биомов органичные, без прямых «полос».</summary>
        Vector2 Warp(Vector2 p, float scale, float amp) =>
            p + new Vector2(_shape.Fbm(p / scale + new Vector2(11, 3), 3), _shape.Fbm(p / scale + new Vector2(-7, 29), 3)) * amp;

        void Generate()
        {
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                var p = CellCenter(x, y);
                float r = p.Length() / (HalfSize * 0.86f);
                // неровный берег: радиальный спад + крупный шум
                float coast = 1f - r + _shape.Fbm(p / 90f, 4) * 0.35f;
                float h = coast * 1.4f + _height.Fbm(p / 60f, 4) * 0.35f;
                // центр (РТ) всегда суша
                if (p.Length() < 70f) h = MathF.Max(h, 0.3f);
                Height[y * N + x] = h;
            }

            // биомы: крупные цельные области по сдвинутому шуму (сотни метров), без тонких «пальцев»
            var raw = new Biome[N * N];
            // пороги климата по долям суши: ~17 % тундры и ~15 % пустоши при любом сиде
            var cl = new List<float>();
            for (int y = 0; y < N; y += 4)
            for (int x = 0; x < N; x += 4)
                if (Height[y * N + x] >= 0.09f) cl.Add(_climate.Fbm(Warp(CellCenter(x, y), 160f, 70f) / 300f, 3));
            cl.Sort();
            float tCold = cl.Count > 0 ? cl[(int)(cl.Count * 0.17f)] : -0.14f, tDry = cl.Count > 0 ? cl[(int)(cl.Count * 0.85f)] : 0.15f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                var p = CellCenter(x, y);
                float h = Height[i];
                if (h < 0) { raw[i] = Biome.Water; continue; }
                if (h < 0.09f) { raw[i] = Biome.Beach; continue; }
                var q = Warp(p, 160f, 70f);
                float clim = _climate.Fbm(q / 300f, 3);
                Biome b;
                if (clim < tCold) b = Biome.Tundra;
                else if (clim > tDry) b = Biome.Arid;
                else if (_wet.Fbm(Warp(p, 120f, 45f) / 150f, 3) > 0.1f && h < 0.75f) b = Biome.Swamp;
                else if (ForestNoise(p) > 0.0f) b = Biome.Forest;
                else b = Biome.Grassland;
                raw[i] = b;
            }
            Smooth(raw, 7); Smooth(raw, 5);
            DropSmall(raw, 900);    // области < 3600 м² — в соседний биом
            // поляны в лесу: округлые луговые окна 15–35 м в глубине массива
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                if (raw[i] != Biome.Forest) continue;
                var p = CellCenter(x, y);
                if (_forest.Fbm(Warp(p, 40f, 10f) / 30f + new Vector2(57, -23), 2) > 0.33f && ForestNoise(p) > 0.12f) raw[i] = Biome.Grassland;
            }
            Array.Copy(raw, Biomes, raw.Length);
            CarveRivers();
        }

        static readonly Biome[] Smoothable = { Biome.Grassland, Biome.Forest, Biome.Arid, Biome.Tundra, Biome.Swamp };

        /// <summary>Сглаживание: в каждой клетке суши — биом, преобладающий в квадрате (2r+1)² (суммы по таблице). Снимает «пальцы» и рваные края.</summary>
        void Smooth(Biome[] b, int r)
        {
            int n = N, n1 = n + 1;
            var tabs = new int[Smoothable.Length][];
            for (int k = 0; k < Smoothable.Length; k++)
            {
                var t = new int[n1 * n1]; var bk = Smoothable[k];
                for (int y = 0; y < n; y++)
                {
                    int row = 0;
                    for (int x = 0; x < n; x++) { if (b[y * n + x] == bk) row++; t[(y + 1) * n1 + x + 1] = t[y * n1 + x + 1] + row; }
                }
                tabs[k] = t;
            }
            var res = (Biome[])b.Clone();
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                if (b[i] is Biome.Water or Biome.Beach) continue;
                int x0 = Math.Max(0, x - r), y0 = Math.Max(0, y - r), x1 = Math.Min(n, x + r + 1), y1 = Math.Min(n, y + r + 1);
                int best = -1, bc = -1;
                for (int k = 0; k < Smoothable.Length; k++)
                {
                    var t = tabs[k];
                    int c = t[y1 * n1 + x1] - t[y0 * n1 + x1] - t[y1 * n1 + x0] + t[y0 * n1 + x0];
                    if (c > bc) { bc = c; best = k; }
                }
                if (bc > 0) res[i] = Smoothable[best];
            }
            Array.Copy(res, b, b.Length);
        }

        /// <summary>Мелкие области биома (меньше minCells) — в самый частый соседний биом.</summary>
        void DropSmall(Biome[] b, int minCells)
        {
            int n = N; var seen = new bool[n * n]; var q = new List<int>(); var cnt = new int[8];
            for (int s = 0; s < n * n; s++)
            {
                if (seen[s] || b[s] is Biome.Water or Biome.Beach) continue;
                q.Clear(); q.Add(s); seen[s] = true; var bk = b[s]; Array.Clear(cnt);
                for (int h = 0; h < q.Count; h++)
                {
                    int i = q[h], x = i % n, y = i / n;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                        int j = ny * n + nx;
                        if (b[j] == bk) { if (!seen[j]) { seen[j] = true; q.Add(j); } }
                        else cnt[(int)b[j]]++;
                    }
                }
                if (q.Count >= minCells) continue;
                int best = -1;
                foreach (var c in Smoothable) if (cnt[(int)c] > 0 && (best < 0 || cnt[(int)c] > cnt[best])) best = (int)c;
                if (best < 0) continue;
                foreach (int i in q) b[i] = (Biome)best;
            }
        }

        // ---------- река ----------

        /// <summary>
        /// Главная река от глубины острова к морю (виляет, 6 → 12 м к устью) и приток. Русло — мелкая вода (Biome.Water, высота чуть ниже 0:
        /// тот же шейдер воды и пена берега), берега — галька/песок (Beach). Обходит центральную РТ (r &lt; 95 м).
        /// </summary>
        float[] RiverDepth;

        void CarveRivers()
        {
            RiverDepth = new float[N * N];
            var rng = new Random(Seed * 613 + 5);
            var mn = new Noise2(Seed * 97 + 13);
            // большая река через весь остров: устье A ← исток-озеро у берега A → обход центра → развилка → два устья (дельта)
            float a0 = (float)(rng.NextDouble() * MathF.Tau), side = rng.Next(2) == 0 ? 1 : -1;
            Vector2 Dir(float a) => new Vector2(MathF.Cos(a), MathF.Sin(a));
            Vector2 Coast(float a) { var d = Dir(a); float L = 100; while (L < HalfSize && HeightAt(d * L) >= 0) L += 3; return d * (L + 10); }
            float ks = HalfSize / 500f;   // масштаб острова
            var lake0 = Dir(a0) * 150f * ks;
            var up = TraceTo(Coast(a0 + 0.15f * side), lake0, mn, 11f, 13f);
            var mid = Dir(a0 + MathF.PI * 0.5f * side) * 125f * ks;
            var fork = Dir(a0 + MathF.PI) * 165f * ks;
            var m1 = TraceTo(lake0, mid, mn, 14f, 16f);
            var m2 = TraceTo(mid, fork, mn, 16f, 18f);
            if (up != null) RiverLines.Add(up);
            if (m1 != null && m2 != null)
            {
                RiverLines.Add(m1); RiverLines.Add(m2);
                var flow = Dir(a0 + MathF.PI);
                foreach (float t in new[] { 0.55f, -0.55f })
                {
                    var br = Trace(fork, Rotate(flow, t), mn, 9f, 12f);
                    if (br != null) RiverLines.Add(br);
                }
            }
            foreach (var line in RiverLines) Stamp(line);
            // озёра: исток реки + 2–3 отдельных, органичная форма (радиус по шуму от угла)
            var lakes = new List<(Vector2 c, float r)> { (lake0, 26f + (float)rng.NextDouble() * 12f) };
            int want = 3 + rng.Next(2);   // 3–4 озера вместе с истоком
            for (int t = 0; t < 400 && lakes.Count < want; t++)
            {
                float ang = (float)(rng.NextDouble() * MathF.Tau), rr = (120f + (float)rng.NextDouble() * 220f) * ks;
                var c = Dir(ang) * rr; float R = 15f + (float)rng.NextDouble() * 30f;
                if (HeightAt(c) < 0.25f || RiverDist(c) < R + 30f) continue;
                bool ok = true; foreach (var l in lakes) ok &= Vector2.Distance(l.c, c) > l.r + R + 60f;
                for (int k = 0; k < 16 && ok; k++) ok = HeightAt(c + Dir(k / 16f * MathF.Tau) * (R + 12f)) >= 0.12f;
                if (ok) lakes.Add((c, R));
            }
            Lakes.AddRange(lakes);
            for (int li = 0; li < lakes.Count; li++) StampLake(lakes[li].c, lakes[li].r, mn, li);
            // расстояние до русла (м, до 40) — для прибрежных рощ ив и тополей
            var qq = new Queue<int>();
            for (int i = 0; i < N * N; i++) { bool w = River[i] is 1 or 3; RiverNearM[i] = w ? (byte)0 : (byte)255; if (w) qq.Enqueue(i); }
            while (qq.Count > 0)
            {
                int i = qq.Dequeue(), x = i % N, y = i / N; int nd = RiverNearM[i] + 2;
                if (nd > 40) continue;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= N || ny >= N) continue;
                    int j = ny * N + nx; if (RiverNearM[j] <= nd) continue;
                    RiverNearM[j] = (byte)nd; qq.Enqueue(j);
                }
            }
            // по маске: русло — вода, берег — песок/галька
            for (int i = 0; i < N * N; i++)
            {
                if (River[i] is 1 or 3) { Biomes[i] = Biome.Water; Height[i] = MathF.Min(Height[i], -0.03f - RiverDepth[i] * 0.22f); }
                else if (River[i] == 2 && Biomes[i] != Biome.Water) { Biomes[i] = Biome.Beach; Height[i] = Math.Clamp(Height[i], 0.02f, 0.08f); }
            }
        }

        /// <summary>Озёра (центр, средний радиус), м.</summary>
        public readonly List<(Vector2 c, float r)> Lakes = new();

        /// <summary>Озеро: River = 3 (тихая вода, как река), кольцо берега = 2. Форма — радиус, меняющийся по шуму от угла.</summary>
        void StampLake(Vector2 c, float R, Noise2 mn, int k)
        {
            int r = (int)MathF.Ceiling(R * 1.6f / Cell) + 2;
            int cx = (int)((c.X + HalfSize) / Cell), cy = (int)((c.Y + HalfSize) / Cell);
            for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || y < 0 || x >= N || y >= N) continue;
                var d = CellCenter(x, y) - c; float dist = d.Length(), th = MathF.Atan2(d.Y, d.X);
                float rad = R * (1f + mn.Fbm(new Vector2(MathF.Cos(th) * 1.3f + k * 17f, MathF.Sin(th) * 1.3f - k * 5f), 3) * 0.7f);
                rad = MathF.Max(rad, R * 0.45f);
                int i = y * N + x;
                if (dist <= rad) { River[i] = 3; RiverDepth[i] = MathF.Max(RiverDepth[i], MathF.Min(1, (1 - dist / rad) * 2.5f)); }
                else if (dist <= rad + 3f + (_wet.Sample(CellCenter(x, y) / 9f) + 0.5f) * 1.5f && River[i] == 0) River[i] = 2;
            }
        }

        (Vector2 p, float w)[] Trace(Vector2 src, Vector2 dir, Noise2 mn, float w0, float w1)
        {
            var pts = new List<Vector2>();
            var p = src; var d = dir; float s = 0;
            for (int k = 0; k < 600; k++)
            {
                pts.Add(p);
                if (HeightAt(p) < 0 && k > 10) { for (int e = 0; e < 8; e++) { p += d * 2f; pts.Add(p); } break; }
                var outward = Vector2.Normalize(p + new Vector2(0.01f, 0));
                // меандры: угол от шума вдоль пути + тяга наружу (к морю)
                float ang = mn.Fbm(new Vector2(s / 48f, 3.7f), 2) * 2.2f + MathF.Sin(s / 34f) * 0.75f;
                var want = Rotate(Vector2.Normalize(dir * 0.4f + outward * 0.6f), ang);
                if (p.Length() < 95f) want = Vector2.Normalize(want + outward * 2f);
                d = Vector2.Normalize(d * 0.8f + want * 0.2f);
                p += d * 2f; s += 2f;
                if (MathF.Abs(p.X) > HalfSize - 4 || MathF.Abs(p.Y) > HalfSize - 4) break;
            }
            if (pts.Count < 40) return null;
            var res = new (Vector2 p, float w)[pts.Count];
            for (int i = 0; i < pts.Count; i++) { float t = (float)i / (pts.Count - 1); res[i] = (pts[i], w0 + (w1 - w0) * t * t + mn.Sample(new Vector2(i / 20f, 9.1f)) * 1.2f); }
            return res;
        }

        (Vector2 p, float w)[] TraceTo(Vector2 src, Vector2 dst, Noise2 mn, float w0, float w1)
        {
            var pts = new List<Vector2>();
            var p = src; var d = Vector2.Normalize(dst - src); float s = 0;
            for (int k = 0; k < 400 && Vector2.Distance(p, dst) > 3f; k++)
            {
                pts.Add(p);
                var to = Vector2.Normalize(dst - p);
                float ang = mn.Fbm(new Vector2(s / 55f + 40f, 1.3f), 2) * 1.4f * MathF.Min(1, Vector2.Distance(p, dst) / 60f);
                var want = Rotate(to, ang);
                if (p.Length() < 95f) want = Vector2.Normalize(want + Vector2.Normalize(p) * 2f);
                d = Vector2.Normalize(d * 0.8f + want * 0.2f);
                p += d * 2f; s += 2f;
            }
            pts.Add(dst);
            if (pts.Count < 20) return null;
            var res = new (Vector2 p, float w)[pts.Count];
            for (int i = 0; i < pts.Count; i++) { float t = (float)i / (pts.Count - 1); res[i] = (pts[i], w0 + (w1 - w0) * t); }
            return res;
        }

        static Vector2 Rotate(Vector2 v, float a) { float c = MathF.Cos(a), s = MathF.Sin(a); return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c); }

        void Stamp((Vector2 p, float w)[] line)
        {
            foreach (var (p, w) in line)
            {
                float half = w * 0.5f, bank = half + 2.5f + (_wet.Sample(p / 9f) + 0.5f) * 1.5f;
                int r = (int)MathF.Ceiling(bank / Cell) + 1;
                int cx = (int)((p.X + HalfSize) / Cell), cy = (int)((p.Y + HalfSize) / Cell);
                for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || y < 0 || x >= N || y >= N) continue;
                    float dist = Vector2.Distance(CellCenter(x, y), p);
                    int i = y * N + x;
                    if (dist <= half) { River[i] = 1; RiverDepth[i] = MathF.Max(RiverDepth[i], 1 - dist / half); }
                    else if (dist <= bank && River[i] == 0) River[i] = 2;
                }
            }
        }

        /// <summary>До русла реки, м (по осевым линиям, минус полширины); 999 — реки нет. Только для генерации.</summary>
        public float RiverDist(Vector2 p)
        {
            float best = 999;
            foreach (var l in RiverLines)
                for (int i = 0; i < l.Length; i += 2)
                {
                    float d = Vector2.Distance(l[i].p, p) - l[i].w * 0.5f;
                    if (d < best) best = d;
                }
            return best;
        }

        /// <summary>Мост: клетки русла рядом с точкой становятся сушей (ходьба без плавания).</summary>
        public void MarkBridge(Vector2 p, float r)
        {
            int cr = (int)MathF.Ceiling(r / Cell);
            int cx = (int)((p.X + HalfSize) / Cell), cy = (int)((p.Y + HalfSize) / Cell);
            for (int y = cy - cr; y <= cy + cr; y++)
            for (int x = cx - cr; x <= cx + cr; x++)
            {
                if (x < 0 || y < 0 || x >= N || y >= N) continue;
                int i = y * N + x;
                if (River[i] == 1 && Vector2.Distance(CellCenter(x, y), p) <= r) Bridge[i] = true;
            }
        }

        /// <summary>Тип древостоя 0..1 (низкочастотный сдвинутый шум): меняется на сотни метров, граница пород размыта.</summary>
        public float StandAt(Vector2 p) => Math.Clamp(_forest.Fbm(Warp(p, 110f, 40f) / 170f + new Vector2(201, -77), 3) * 1.8f + 0.5f, 0, 1);
        /// <summary>Густота древостоя ~0.4..1.4: внутри массива редины и куртины.</summary>
        public float StandDensity(Vector2 p) => 0.9f + _height.Fbm(Warp(p, 50f, 15f) / 45f + new Vector2(-311, 57), 2) * 1.0f;

        float ForestNoise(Vector2 p) => _forest.Fbm(p / 150f, 3) + _forest.Fbm(p / 40f + new Vector2(-41, 9), 2) * 0.15f;

        /// <summary>Хвойный (&gt;0) или лиственный (&lt;0) лес: свои массивы ельника/сосняка и березняка/осинника, примерно -1..1.</summary>
        public float ConiferAt(Vector2 p) => _climate.Fbm(p / 120f + new Vector2(-77, 140), 3) * 1.6f - _climate.Fbm(p / 210f, 3) * 1.2f;

        int Index(Vector2 p)
        {
            int x = (int)((p.X + HalfSize) / Cell), y = (int)((p.Y + HalfSize) / Cell);
            if (x < 0 || y < 0 || x >= N || y >= N) return -1;
            return y * N + x;
        }

        public Biome BiomeAt(Vector2 p) { int i = Index(p); return i < 0 ? Biome.Water : Biomes[i]; }
        public bool IsLand(Vector2 p) { int i = Index(p); return i >= 0 && (Biomes[i] != Biome.Water || Bridge[i]); }
        public bool IsRiver(Vector2 p) { int i = Index(p); return i >= 0 && River[i] == 1; }
        public float HeightAt(Vector2 p) { int i = Index(p); return i < 0 ? -1 : Height[i]; }
    }

    /// <summary>Простой градиентный шум (без зависимостей от движка) и fbm на нём. Значения примерно -1..1.</summary>
    public sealed class Noise2
    {
        readonly int[] _perm = new int[512];

        public Noise2(int seed)
        {
            var rng = new Random(seed);
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            for (int i = 255; i > 0; i--) { int j = rng.Next(i + 1); (p[i], p[j]) = (p[j], p[i]); }
            for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
        }

        static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

        float Grad(int h, float x, float y)
        {
            switch (h & 7)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }

        public float Sample(Vector2 v)
        {
            int xi = (int)MathF.Floor(v.X), yi = (int)MathF.Floor(v.Y);
            float xf = v.X - xi, yf = v.Y - yi;
            int X = xi & 255, Y = yi & 255;
            float u = Fade(xf), w = Fade(yf);
            int aa = _perm[_perm[X] + Y], ab = _perm[_perm[X] + Y + 1];
            int ba = _perm[_perm[X + 1] + Y], bb = _perm[_perm[X + 1] + Y + 1];
            float x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
            float x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);
            return Lerp(x1, x2, w) * 0.7f;
        }

        public float Fbm(Vector2 v, int octaves)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += Sample(v) * amp;
                norm += amp;
                amp *= 0.5f;
                v *= 2.03f;
            }
            return sum / norm;
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
