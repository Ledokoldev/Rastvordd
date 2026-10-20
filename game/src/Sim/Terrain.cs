using System;
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

        public TerrainMap(float halfSize, int seed)
        {
            HalfSize = halfSize;
            N = (int)(halfSize * 2 / Cell);
            Height = new float[N * N];
            Biomes = new Biome[N * N];
            _shape = new Noise2(seed);
            _height = new Noise2(seed + 1);
            _forest = new Noise2(seed + 2);
            _climate = new Noise2(seed + 3);
            _wet = new Noise2(seed + 4);
            Generate();
        }

        public Vector2 CellCenter(int x, int y) => new Vector2((x + 0.5f) * Cell - HalfSize, (y + 0.5f) * Cell - HalfSize);

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

            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                var p = CellCenter(x, y);
                float h = Height[y * N + x];
                Biome b;
                if (h < 0) b = Biome.Water;
                else if (h < 0.09f) b = Biome.Beach;
                else
                {
                    // широта с шумом: север (y < 0) холодный, юг сухой
                    float lat = p.Y / HalfSize + _climate.Fbm(p / 110f, 3) * 0.35f;
                    if (lat < -0.42f) b = Biome.Tundra;
                    else if (lat > 0.45f) b = Biome.Arid;
                    else if (_wet.Fbm(p / 45f, 3) > 0.33f && h < 0.45f) b = Biome.Swamp;
                    else b = _forest.Fbm(p / 55f, 3) > 0.05f ? Biome.Forest : Biome.Grassland;
                }
                Biomes[y * N + x] = b;
            }
        }

        int Index(Vector2 p)
        {
            int x = (int)((p.X + HalfSize) / Cell), y = (int)((p.Y + HalfSize) / Cell);
            if (x < 0 || y < 0 || x >= N || y >= N) return -1;
            return y * N + x;
        }

        public Biome BiomeAt(Vector2 p) { int i = Index(p); return i < 0 ? Biome.Water : Biomes[i]; }
        public bool IsLand(Vector2 p) => BiomeAt(p) != Biome.Water;
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
