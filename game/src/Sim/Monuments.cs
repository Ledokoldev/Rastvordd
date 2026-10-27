using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>РТ на карте: имя для подсказки, занятый прямоугольник (рядом нельзя строить), охрана.</summary>
    public sealed class Monument
    {
        public string Name;
        public string Key;   // id РТ тира 1 для профиля лута (garages/depot/quarry/ats/substation), иначе null
        public Vector2 Min, Max;
        public Vector2 Center => (Min + Max) * 0.5f;
        public bool Contains(Vector2 p, float margin = 0) =>
            p.X > Min.X - margin && p.X < Max.X + margin && p.Y > Min.Y - margin && p.Y < Max.Y + margin;
    }

    public sealed partial class World
    {
        public readonly List<Monument> Monuments = new();

        /// <summary>Точка внутри РТ или ближе margin к нему (стройка запрещена, боты обходят).</summary>
        public bool NearMonument(Vector2 p, float margin = 0)
        {
            foreach (var m in Monuments) if (m.Contains(p, margin)) return true;
            return false;
        }

        public Monument MonumentAt(Vector2 p)
        {
            foreach (var m in Monuments) if (m.Contains(p)) return m;
            return null;
        }

        /// <summary>
        /// Малые РТ по карточкам Астры (docs/gpt/01-monuments.md): размер, биом, охрана, число ящиков.
        /// Планировка общая: забор с 2–3 проходами, кирпичная постройка с проёмом, укрытия, ящики, бочка воды.
        /// </summary>
        static readonly (string name, float w, float h, Biome[] biomes, int guards, int crates)[] SmallMonuments =
        {
            ("Весовая «Последний километр»", 34, 26, new[] { Biome.Grassland, Biome.Arid }, 0, 2),
            ("Раздаточная «Сухой паёк»", 40, 30, new[] { Biome.Forest, Biome.Grassland }, 1, 3),
            ("Почтовый узел «Невручённое»", 38, 32, new[] { Biome.Grassland, Biome.Forest }, 2, 3),
            ("Котельная «Остывший квартал»", 46, 38, new[] { Biome.Tundra, Biome.Forest }, 2, 4),
            ("Станция семян «Мёртвый посев»", 50, 42, new[] { Biome.Grassland, Biome.Swamp }, 2, 4),
        };

        void PlaceSmallMonuments()
        {
            foreach (var (name, w, h, biomes, guards, crates) in SmallMonuments)
            {
                for (int tries = 0; tries < 400; tries++)
                {
                    float ang = Range(0, MathF.Tau), r = Range(HalfSize * 0.3f, HalfSize * 0.68f);
                    var c = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r;
                    if (Array.IndexOf(biomes, Terrain.BiomeAt(c)) < 0) continue;
                    var min = c - new Vector2(w, h) * 0.5f;
                    var max = c + new Vector2(w, h) * 0.5f;
                    // целиком на суше и не ближе 60 м к другим РТ
                    bool ok = true;
                    for (float x = min.X; x <= max.X && ok; x += 4)
                        for (float y = min.Y; y <= max.Y && ok; y += 4)
                            ok = Terrain.IsLand(new Vector2(x, y)) && Terrain.BiomeAt(new Vector2(x, y)) != Biome.Beach;
                    foreach (var m in Monuments) ok &= Vector2.Distance(m.Center, c) > 130 + (m.Max - m.Min).Length() * 0.5f;   // РТ не кучкуются
                    if (!ok) continue;
                    BuildSmallMonument(name, min, max, guards, crates);
                    break;
                }
            }
        }

        void BuildSmallMonument(string name, Vector2 min, Vector2 max, int guards, int crates)
        {
            Monuments.Add(new Monument { Name = name, Min = min, Max = max });
            const float t = 0.3f, fh = 2.2f, bh = 2.6f;
            var c = (min + max) * 0.5f;
            float w = max.X - min.X, h = max.Y - min.Y;

            // забор с проходами (по 4 м): на юге всегда, на севере или востоке — случайно
            float gapS = Range(min.X + 6, max.X - 10), gapN = Range(min.X + 6, max.X - 10), gapE = Range(min.Y + 6, max.Y - 10);
            bool northGate = Rng.NextDouble() < 0.6;
            AddWall(min.X, max.Y - t, gapS, max.Y + t, fh, WallKind.Concrete);
            AddWall(gapS + 4, max.Y - t, max.X, max.Y + t, fh, WallKind.Concrete);
            if (northGate)
            {
                AddWall(min.X, min.Y - t, gapN, min.Y + t, fh, WallKind.Concrete);
                AddWall(gapN + 4, min.Y - t, max.X, min.Y + t, fh, WallKind.Concrete);
            }
            else AddWall(min.X, min.Y - t, max.X, min.Y + t, fh, WallKind.Concrete);
            AddWall(min.X - t, min.Y, min.X + t, max.Y, fh, WallKind.Concrete);
            if (!northGate)
            {
                AddWall(max.X - t, min.Y, max.X + t, gapE, fh, WallKind.Concrete);
                AddWall(max.X - t, gapE + 4, max.X + t, max.Y, fh, WallKind.Concrete);
            }
            else AddWall(max.X - t, min.Y, max.X + t, max.Y, fh, WallKind.Concrete);

            // постройка: прямоугольник в северо-западной части, проём на юг
            var b0 = min + new Vector2(w * 0.12f, h * 0.15f);
            var b1 = b0 + new Vector2(w * 0.38f, h * 0.38f);
            float door = (b0.X + b1.X) * 0.5f;
            AddWall(b0.X, b0.Y - t, b1.X, b0.Y + t, bh, WallKind.Concrete);
            AddWall(b0.X - t, b0.Y, b0.X + t, b1.Y, bh, WallKind.Concrete);
            AddWall(b1.X - t, b0.Y, b1.X + t, b1.Y, bh, WallKind.Concrete);
            AddWall(b0.X, b1.Y - t, door - 1.1f, b1.Y + t, bh, WallKind.Concrete);
            AddWall(door + 1.1f, b1.Y - t, b1.X, b1.Y + t, bh, WallKind.Concrete);

            // укрытия во дворе
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector2(Range(c.X, max.X - 4), Range(c.Y - h * 0.1f, max.Y - 4));
                var kind = i % 2 == 0 ? WallKind.Crate : WallKind.Barrel;
                AddWall(p.X, p.Y, p.X + (kind == WallKind.Crate ? 2 : 1), p.Y + 1, 1.0f, kind);
            }

            // ящики: первый всегда в постройке, остальные во дворе
            int tier = guards == 0 ? 0 : 1;   // без охраны — бытовой лут, с охраной — получше
            AddCrate((b0 + b1) * 0.5f, tier);
            if (guards > 0) AddSafe(b0 + new Vector2(1.2f, 1.2f));                 // сейф в углу постройки — под карту доступа
            if (name.StartsWith("Котельная")) AddRecycler(new Vector2(b1.X + 3, c.Y));
            for (int i = 1; i < crates; i++)
                AddCrate(new Vector2(Range(min.X + 3, max.X - 3), Range(c.Y + 2, max.Y - 3)), tier);
            if (Rng.NextDouble() < 0.7) AddPickup(PickupKind.WaterBarrel, new Vector2(max.X - 3, min.Y + 3));

            // металлолом без боя: бочки вдоль забора и кучи хлама во дворе (D31)
            int barrels = 3 + guards * 2;
            for (int i = 0, tries = 0; i < barrels && tries < 60; tries++)
            {
                var p = Rng.Next(2) == 0
                    ? new Vector2(Range(min.X + 1.5f, max.X - 1.5f), Rng.Next(2) == 0 ? min.Y + 1.2f : max.Y - 1.2f)
                    : new Vector2(Rng.Next(2) == 0 ? min.X + 1.2f : max.X - 1.2f, Range(min.Y + 1.5f, max.Y - 1.5f));
                if (IsFree(p, 0.7f)) { AddScrapSpot(NodeKind.Barrel, p); i++; }
            }
            for (int i = 0, tries = 0; i < 1 + guards && tries < 40; tries++)
            {
                var p = new Vector2(Range(c.X, max.X - 3), Range(min.Y + 3, max.Y - 3));
                if (IsFree(p, 1.2f)) { AddScrapSpot(NodeKind.Junk, p); i++; }
            }

            // охрана: в постройке и во дворе
            for (int g = 0; g < guards; g++)
                NpcHomes[-NpcHomes.Count - 1] = g == 0 ? (b0 + b1) * 0.5f + new Vector2(1, 1) : new Vector2(Range(c.X, max.X - 3), Range(c.Y, max.Y - 3));
        }
    }
}
