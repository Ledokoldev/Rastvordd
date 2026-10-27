using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Одиночные ориентиры мира (только декор: не используются игроком, но укрывают и мешают проходу, как пропсы).
    /// Каждый — маленькая сцена, поставленная по правилу места (таблица ниже), не случайный разброс.
    /// Свой генератор случайных чисел — остальная раскладка мира и старые сохранения не сдвигаются.
    ///
    /// ПРАВИЛА МЕСТА (что — где — почему):
    ///  железная дорога   — одна линия запад→восток, через реку (мост) и дорогу (переезд), в обход РТ; полилиния Railway — под будущую дрезину
    ///  трубопровод       — 300–450 м запад→восток по полям, пересекает реку по трубному мосту
    ///  вертолёт          — глубокий лес (густой древостой), далеко от дорог: упал там, где не нашли
    ///  горелый лес       — лес, далеко от дорог (пожар после падения/грозы)
    ///  дерево через ручей — узкое место реки, течение с севера на юг (ствол лежит поперёк)
    ///  водонапорная башня — у дороги рядом с посёлком (РТ)
    ///  вышка связи       — возвышенность у края поля; РЛС — самая высокая точка; метеостанция — открытая высота
    ///  упавшая ЛЭП       — поле недалеко от дороги; стоячие столбы с проводами — вдоль второстепенных дорог
    ///  лагерь выживших   — опушка у воды (река/озеро)
    ///  охотничья вышка   — опушка: лес за спиной, поле перед глазами
    ///  ДОТы с окопом     — высоты; у части — сгоревший танк
    ///  блокпост          — дорога на подъезде к РТ (ежи, бетонные блоки по обочинам)
    ///  баржа             — устье реки на берегу моря; лодка — берег озера
    ///  изба-развалюха    — лесная поляна; колодец-журавль — у сельской дороги
    ///  качалка           — засушливая степь (Arid), иначе дальнее поле
    ///  заправка          — развилка дорог
    ///  дуб-патриарх      — одинокий холм/луг вдали от леса
    ///  хутор (стога, пасека, пугало), детская площадка — поле у посёлка
    /// </summary>
    public sealed partial class World
    {
        /// <summary>Ориентиры: вид и место (для отладки --landmarks).</summary>
        public readonly List<(string Kind, Vector2 Pos)> Landmarks = new();
        /// <summary>Ось железной дороги (полилиния, шаг ~6 м) — для будущей дрезины.</summary>
        public readonly List<Vector2> Railway = new();
        readonly List<(Vector2 c, float r)> _lmAreas = new();
        readonly List<(Vector2 c, float r)> _lmClear = new();
        Random _lr;
        float[] _rdGrid; int _rdN; const float RdCell = 4f;

        float Rf(float a, float b) => a + (float)_lr.NextDouble() * (b - a);

        void PlaceLandmarks()
        {
            _lr = new Random(Seed * 104729 + 911);
            BuildRoadDistance();
            BuildRailway();
            BuildPipeline();
            DressSideRoads();

            Place("heli", 2, 500, p => Terrain.BiomeAt(p) == Biome.Forest && RoadDist(p) > 60 ? Terrain.StandAt(p) * 2 + Rf(0, .3f) : -1, 14, Heli);
            Place("burnt_forest", 1, 400, p => Terrain.BiomeAt(p) == Biome.Forest && RoadDist(p) > 50 ? Terrain.StandAt(p) + Rf(0, .5f) : -1, 20, BurntForest);
            Place("water_tower", 3, 300, p => { float d = RoadDist(p), m = MonDist(p); return d is > 6 and < 18 && m is > 25 and < 140 ? 1 - m / 200 + Rf(0, .3f) : -1; }, 5, WaterTower);
            Place("radar", 1, 900, p => Open(p) ? Hill(p) * 3 + Rf(0, .1f) : -1, 8, p => Scene(p, "vehicles/radar_p18", 6, 3, 5));
            Place("cell_tower", 2, 500, p => Open(p) && FieldEdge(p) ? Hill(p) * 2 + Rf(0, .3f) : -1, 5, CellTower);
            Place("weather", 1, 600, p => Open(p) ? Hill(p) * 2 + Rf(0, .3f) : -1, 5, Weather);
            Place("fallen_line", 3, 300, p => { float d = RoadDist(p); return Open(p) && d is > 25 and < 120 ? 1 + Rf(0, .5f) : -1; }, 12, FallenLine);
            Place("camp", 3, 350, p => { float w = WaterDist(p); return Terrain.BiomeAt(p) is Biome.Grassland or Biome.Forest && ForestEdge(p) && w is > 12 and < 70 && RoadDist(p) > 30 ? 1 + Rf(0, .5f) : -1; }, 9, Camp);
            Place("hunting_tower", 8, 160, p => Terrain.BiomeAt(p) == Biome.Grassland && ForestEdge(p) && RoadDist(p) > 20 ? 1 + Rf(0, .5f) : -1, 3, p => Scene(p, "props/lm_hunting_tower", 2.2f, 2.0f, 5));
            Place("bunker", 4, 260, p => Terrain.BiomeAt(p) is Biome.Grassland or Biome.Tundra or Biome.Arid && RoadDist(p) > 15 ? Hill(p) * 3 + Rf(0, .2f) : -1, 12, Bunker);
            Place("log_hut", 2, 400, p => Terrain.BiomeAt(p) == Biome.Forest && RoadDist(p) > 35 ? Terrain.StandAt(p) + Rf(0, .5f) : -1, 8, LogHut);
            Place("shadoof", 2, 400, p => { float d = RoadDist(p), m = MonDist(p); return d is > 4 and < 9 && m is > 30 and < 220 && Open(p) ? 1 + Rf(0, .5f) : -1; }, 3, p => Scene(p, "outdoor/well_crane_side", 1.5f, 1.2f, 2));
            Place("pumpjack", 3, 140, p => Terrain.BiomeAt(p) == Biome.Arid ? 2 + Rf(0, .5f) : Open(p) && MonDist(p) > 150 ? Rf(0, .5f) : -1, 6, Pumpjack);
            Place("oak", 2, 500, p => Terrain.BiomeAt(p) == Biome.Grassland && Terrain.StandAt(p) < .35f && !ForestNear(p, 30) ? Hill(p) * 2 + Rf(0, .3f) : -1, 7, Oak);
            Place("farm", 2, 400, p => { float m = MonDist(p); return Open(p) && m is > 25 and < 90 && RoadDist(p) is > 8 and < 60 ? 1 + Rf(0, .5f) : -1; }, 13, Farm);
            Place("playground", 1, 400, p => { float m = MonDist(p); return Open(p) && m is > 18 and < 60 ? 1 + Rf(0, .5f) : -1; }, 9, Playground);
            Checkpoints(2);
            FuelStation();
            RiverScenes();
            LakeBoats(2);
            _rdGrid = null;
        }

        // ---------- поиск места ----------

        /// <summary>Лучшее по score место из случайной выборки, свободное (r) и не ближе spacing к своим.</summary>
        void Place(string kind, int count, float spacing, Func<Vector2, float> score, float r, Action<Vector2> build)
        {
            for (int n = 0; n < count; n++)
            {
                Vector2 best = default; float bs = 0;
                for (int t = 0; t < 2500; t++)
                {
                    var p = new Vector2(Rf(-HalfSize + 40, HalfSize - 40), Rf(-HalfSize + 40, HalfSize - 40));
                    if (!Clear(p, r)) continue;
                    bool near = false;
                    foreach (var l in Landmarks) if (l.Kind == kind && Vector2.Distance(l.Pos, p) < spacing) { near = true; break; }
                    if (near) continue;
                    float s = score(p);
                    if (s > bs) { bs = s; best = p; }
                }
                if (bs <= 0) return;
                Take(kind, best, r);
                build(best);
            }
        }

        bool Clear(Vector2 p, float r)
        {
            if (Terrain.BiomeAt(p) is Biome.Water or Biome.Beach or Biome.Swamp || NearMonument(p, r + 18) || RoadDist(p) < r + 2) return false;
            if (Terrain.RiverNear(p) < r + 6) return false;
            foreach (var a in _lmAreas) if (Vector2.Distance(a.c, p) < a.r + r + 6) return false;
            foreach (var s in PlayerSpawns) if (Vector2.Distance(s, p) < r + 10) return false;
            for (int k = 0; k < 8; k++)
            {
                var q = p + new Vector2(MathF.Cos(k * .785f), MathF.Sin(k * .785f)) * r;
                if (!Terrain.IsLand(q) || Terrain.BiomeAt(q) == Biome.Water) return false;
            }
            return Slope(p, r) < 0.06f;
        }

        void Take(string kind, Vector2 c, float r)
        {
            Landmarks.Add((kind, c));
            _lmAreas.Add((c, r));
            Strip(c, r);
        }

        /// <summary>Убрать деревья, камни и пропсы мира с площадки (и после загрузки сохранения — тоже).</summary>
        void Strip(Vector2 c, float r)
        {
            _lmClear.Add((c, r));
            foreach (var nd in NodesNear(c, r).ToArray())
                if (Vector2.Distance(nd.Pos, c) < r) { _nodeGrid.Remove(nd); Nodes.Remove(nd); }
            foreach (var w in WallsNear(c, r).ToArray())
                if (w.Owner == null && !w.Prefab && RoadsideClearKind(w.Kind)) { _wallGrid.Remove(w); Walls.Remove(w); }
        }

        void ClearLandmarksAfterLoad()
        {
            foreach (var (c, r) in _lmClear)
                foreach (var nd in NodesNear(c, r).ToArray())
                    if (Vector2.Distance(nd.Pos, c) < r && nd.Kind != NodeKind.Barrel) { _nodeGrid.Remove(nd); Nodes.Remove(nd); }
        }

        float Slope(Vector2 p, float r)
        {
            float lo = 9, hi = -9;
            for (int k = 0; k < 5; k++)
            {
                var q = p + (k == 0 ? Vector2.Zero : new Vector2(MathF.Cos(k * 1.57f), MathF.Sin(k * 1.57f)) * MathF.Max(r, 4));
                float h = Terrain.HeightAt(q); lo = MathF.Min(lo, h); hi = MathF.Max(hi, h);
            }
            return hi - lo;
        }

        /// <summary>Насколько точка выше окрестности (холм) — 0..~1.</summary>
        float Hill(Vector2 p)
        {
            float h = Terrain.HeightAt(p), sum = 0;
            for (int k = 0; k < 8; k++) sum += Terrain.HeightAt(p + new Vector2(MathF.Cos(k * .785f), MathF.Sin(k * .785f)) * 70);
            return MathF.Max(0, h + (h - sum / 8) * 4);
        }

        bool Open(Vector2 p) => Terrain.BiomeAt(p) is Biome.Grassland or Biome.Tundra or Biome.Arid && !ForestNear(p, 10);
        bool ForestNear(Vector2 p, float r)
        {
            foreach (var n in NodesNear(p, r)) if (n.Kind == NodeKind.Tree) return true;
            return false;
        }
        bool FieldEdge(Vector2 p) => ForestNear(p, 45);
        /// <summary>Опушка: деревья с одной стороны, открытое место — с другой.</summary>
        bool ForestEdge(Vector2 p)
        {
            int f = 0;
            for (int k = 0; k < 8; k++) if (Terrain.BiomeAt(p + new Vector2(MathF.Cos(k * .785f), MathF.Sin(k * .785f)) * 22) == Biome.Forest) f++;
            return f is >= 2 and <= 5 && !ForestNear(p, 4);
        }
        float MonDist(Vector2 p)
        {
            float best = 9999;
            foreach (var m in Monuments)
            {
                var q = Vector2.Clamp(p, m.Min, m.Max);
                best = MathF.Min(best, Vector2.Distance(p, q));
            }
            return best;
        }
        float WaterDist(Vector2 p)
        {
            float d = Terrain.RiverNear(p);
            foreach (var (c, r) in Terrain.Lakes) d = MathF.Min(d, Vector2.Distance(c, p) - r);
            return d;
        }

        /// <summary>Расстояние до полотна дорог: сетка 4 м, двухпроходная фаска (один раз при генерации).</summary>
        void BuildRoadDistance()
        {
            _rdN = (int)(HalfSize * 2 / RdCell);
            _rdGrid = new float[_rdN * _rdN];
            Array.Fill(_rdGrid, 9999f);
            foreach (var l in RoadLines)
                foreach (var q in l.P)
                {
                    int x = (int)((q.X + HalfSize) / RdCell), y = (int)((q.Y + HalfSize) / RdCell);
                    if (x >= 0 && y >= 0 && x < _rdN && y < _rdN) _rdGrid[y * _rdN + x] = MathF.Min(_rdGrid[y * _rdN + x], -l.Half);
                }
            const float D = RdCell, G = RdCell * 1.414f;
            for (int y = 0; y < _rdN; y++)
                for (int x = 0; x < _rdN; x++)
                {
                    int i = y * _rdN + x; float v = _rdGrid[i];
                    if (x > 0) v = MathF.Min(v, _rdGrid[i - 1] + D);
                    if (y > 0) { v = MathF.Min(v, _rdGrid[i - _rdN] + D); if (x > 0) v = MathF.Min(v, _rdGrid[i - _rdN - 1] + G); if (x < _rdN - 1) v = MathF.Min(v, _rdGrid[i - _rdN + 1] + G); }
                    _rdGrid[i] = v;
                }
            for (int y = _rdN - 1; y >= 0; y--)
                for (int x = _rdN - 1; x >= 0; x--)
                {
                    int i = y * _rdN + x; float v = _rdGrid[i];
                    if (x < _rdN - 1) v = MathF.Min(v, _rdGrid[i + 1] + D);
                    if (y < _rdN - 1) { v = MathF.Min(v, _rdGrid[i + _rdN] + D); if (x < _rdN - 1) v = MathF.Min(v, _rdGrid[i + _rdN + 1] + G); if (x > 0) v = MathF.Min(v, _rdGrid[i + _rdN - 1] + G); }
                    _rdGrid[i] = v;
                }
        }
        float RoadDist(Vector2 p)
        {
            int x = (int)((p.X + HalfSize) / RdCell), y = (int)((p.Y + HalfSize) / RdCell);
            if (_rdGrid == null || x < 0 || y < 0 || x >= _rdN || y >= _rdN) return 9999;
            return _rdGrid[y * _rdN + x] + RdCell * 0.7f;
        }

        // ---------- кирпичики сцен ----------

        MapDecor Flat(string sprite, Vector2 p, float scale = 1, float rot = 0)
        {
            var d = Decor(sprite, p);
            d.Flat = true; d.Scale = scale; d.Rot = rot;
            return d;
        }
        /// <summary>Спрайт с хитбоксом w×d (основание — нижняя кромка) и высотой h.</summary>
        void Solid(string sprite, Vector2 p, float w, float d, float h, bool flip = false)
        {
            Decor(sprite, p, flip);
            AddHiddenWall(p.X - w / 2, p.Y - d, p.X + w / 2, p.Y, h);
        }
        void Scene(Vector2 p, string sprite, float w, float d, float h) => Solid(sprite, p, w, d, h, _lr.Next(2) == 0);
        Vector2 Around(Vector2 c, float r0, float r1) { float a = Rf(0, 6.283f), r = Rf(r0, r1); return c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r; }
        void Barrels(Vector2 c, float chance)
        {
            if (_lr.NextDouble() > chance) return;
            var p = c;
            for (int t = 0; t < 20; t++) { p = Around(c, 1.5f, 3f); if (IsFree(p, 0.6f) && Terrain.IsLand(p)) break; }
            AddScrapSpot(NodeKind.Barrel, p);   // стандартная бочка лута мира (та же, что везде)
        }

        // ---------- сцены ----------

        void Heli(Vector2 c)
        {
            Flat("decals/decal_burnt_spot", c + new Vector2(0, -4), 2.2f, Rf(0, 360)); Flat("decals/decal_burnt_spot", c + new Vector2(-4, -3), 1.8f, Rf(0, 360)); Flat("decals/decal_burnt_spot", c + new Vector2(4, -4), 1.8f, Rf(0, 360));
            Flat("decals/decal_ash", c + new Vector2(-3, -2), 1.6f, Rf(0, 360));
            Flat("decals/decal_ash_pile", c + new Vector2(4, -5), 1.6f);
            Decor("vehicles/heli_mi8_wreck_top", c, _lr.Next(2) == 0);
            AddHiddenWall(c.X - 6, c.Y - 7, c.X + 5, c.Y - 2, 2.5f);
            Decor("vehicles/heli_mi8_debris_top", c + new Vector2(Rf(7, 10), Rf(3, 6)));
            Decor("props/heli_rotor_blade_broken", c + new Vector2(Rf(-12, -9), Rf(2, 5)));
            Decor("props/heli_mi8_door_panel", c + new Vector2(Rf(2, 5), Rf(3, 5)));
            // сломанные деревья вокруг: пни и упавшие стволы по направлению падения
            for (int k = 0; k < 7; k++)
            {
                var p = Around(c + new Vector2(0, -3), 9, 14);
                Decor(k % 2 == 0 ? "outdoor/stump" : (k % 3 == 0 ? "props/log_2" : "props/log_1"), p, _lr.Next(2) == 0);
            }
            Decor("props/lm_tree_fallen", c + new Vector2(-11, -9));
            AddHiddenWall(c.X - 14, c.Y - 9.4f, c.X - 8, c.Y - 9, 0.8f);
            Barrels(c + new Vector2(6, 2), 0.6f);
        }

        void BurntForest(Vector2 c)
        {
            for (int k = 0; k < 12; k++) Flat(k % 2 == 0 ? "decals/decal_ash" : "decals/decal_burnt_spot", Around(c, 0, 15), Rf(1.4f, 2.2f), Rf(0, 360));
            for (int k = 0; k < 9; k++)
            {
                var p = Around(c, 2, 18);
                if (k < 5) { Decor("props/chem_dead_tree", p, _lr.Next(2) == 0); AddHiddenWall(p.X - .3f, p.Y - .4f, p.X + .3f, p.Y, 4); }
                else Decor(k % 2 == 0 ? "outdoor/stump" : "props/log_1", p, _lr.Next(2) == 0);
            }
        }

        void WaterTower(Vector2 c)
        {
            Flat("decals/decal_gravel_patch", c + new Vector2(0, -1), 2.2f);
            Solid("outdoor/water_tower", c, 3, 3, 12);
            Decor("outdoor/electric_cabinet", c + new Vector2(3, .5f));
            Flat("decals/decal_puddle", c + new Vector2(-2.5f, .8f), 1.4f);
            Barrels(c, 0.35f);
        }

        void CellTower(Vector2 c)
        {
            Flat("decals/decal_gravel_patch", c + new Vector2(0, -1), 2.5f);
            Solid("props/obj_s_antenna_array", c, 2.5f, 2.5f, 14);
            Decor("outdoor/electric_cabinet", c + new Vector2(2.6f, .4f));
            for (int i = -2; i <= 2; i++) Decor("outdoor/fence_chainlink_torn", c + new Vector2(i * 3, 2.5f));
        }

        void Weather(Vector2 c)
        {
            Scene(c, "props/lm_weather_station", 2, 1.4f, 3);
            Decor("props/lm_windsock", c + new Vector2(4, -1));
            Flat("decals/decal_gravel_patch", c + new Vector2(0, -.5f), 1.6f);
        }

        void FallenLine(Vector2 c)
        {
            // стоячая опора, от неё провода к упавшей; воронка и подпалина там, где упала
            var stand = c + new Vector2(-9, 0);
            Solid("outdoor/power_pole_concrete", stand, .4f, .4f, 9);
            Flat("decals/decal_burnt_spot", c + new Vector2(2, -1), 2f, Rf(0, 360));
            Flat("decals/decal_debris", c + new Vector2(5, 1), 1.5f);
            Flat("decals/decal_mud", c + new Vector2(-1, 0), 1.8f);
            Decor("props/lm_pylon_fallen", c + new Vector2(3, 0));
            AddHiddenWall(c.X - 3, c.Y - 1.2f, c.X + 9, c.Y, 1.2f);
            Wire(stand, 8.5f, c + new Vector2(-2, -.5f), 0.3f, 0.2f);
            Wire(stand, 8.3f, c + new Vector2(0, .4f), 0.1f, 0.3f);
            Wire(stand + new Vector2(-30, Rf(-3, 3)), 8.5f, stand, 8.5f, 0.8f);
            var far = stand + new Vector2(-30, 0);
            if (Terrain.IsLand(far) && IsFree(far, 1)) Solid("outdoor/power_pole_concrete", far, .4f, .4f, 9);
        }

        void Camp(Vector2 c)
        {
            Flat("decals/floor_dirt", c, 3f, Rf(0, 360));
            Flat("decals/decal_ash", c, 0.9f);
            Flat("decals/floor_campfire", c, 1.6f);
            for (int k = 0; k < 7; k++) Flat("decals/decal_pebbles", c + new Vector2(MathF.Cos(k * .9f), MathF.Sin(k * .9f)) * 1.0f, .35f, k * 50);
            Solid("props/army_tent", c + new Vector2(-4, -2.5f), 4.2f, 2.5f, 2.4f);
            Solid("props/lm_tarp_shelter", c + new Vector2(4, -2.5f), 3f, 1.5f, 1.8f, _lr.Next(2) == 0);
            Decor("building/bsleep_bag", c + new Vector2(3.6f, -1.2f));
            Decor("building/bsleep_bag", c + new Vector2(-1.8f, 2.6f));
            Decor("outdoor/clothesline", c + new Vector2(0, 4.5f));
            Decor("outdoor/firewood_pile", c + new Vector2(2.2f, 1.6f));
            Decor("props/log_1", c + new Vector2(-1.6f, .6f));
            Barrels(c + new Vector2(-4, 2), 0.5f);
        }

        void Bunker(Vector2 c)
        {
            Solid("props/lm_pillbox", c, 4.4f, 3.2f, 2.2f);
            // окоп дугой за ДОТом: полоса траншеи, бруствер мешками, колючка перед фронтом
            for (int k = -3; k <= 3; k++)
            {
                var p = c + new Vector2(k * 2.6f, -6 - MathF.Abs(k) * .5f);
                Flat("decals/decal_mud", p, 1.4f, Rf(0, 360));
                if (k % 2 == 0) Solid("outdoor/sandbags_wall", p + new Vector2(0, 1.2f), 2.5f, .45f, .9f);
            }
            Patches.Add(new MapPatch { Tile = "tiles/lm_trench", Min = c + new Vector2(-9, -6.6f), Max = c + new Vector2(9, -5.2f) });
            Solid("outdoor/sandbags_curve_angled", c + new Vector2(-4.2f, .6f), 2.4f, 1.2f, .9f);
            for (int k = -3; k <= 3; k++) Solid("building/bwire_barbed", c + new Vector2(k * 3, 4.5f), 3, .3f, 1);
            if (!Landmarks.Exists(l => l.Kind == "tank") || _lr.Next(3) == 0)
            {
                var t = c + new Vector2(Rf(9, 12), Rf(4, 7));
                Flat("decals/decal_burnt_spot", t + new Vector2(0, -1.5f), 2f, Rf(0, 360));
                Solid("vehicles/tank_t55_wreck_angled", t, 6, 3, 2.5f, _lr.Next(2) == 0);
                Landmarks.Add(("tank", t));
            }
            Barrels(c + new Vector2(5, -3), 0.4f);
        }

        void LogHut(Vector2 c)
        {
            Flat("decals/decal_leaves_pile", c + new Vector2(0, 1), 2f);
            Solid("props/lm_log_hut_ruined", c, 5, 4, 3, _lr.Next(2) == 0);
            Decor("outdoor/firewood_stack_side", c + new Vector2(3.6f, 1));
            Decor("outdoor/stump", c + new Vector2(-3.5f, 2));
        }

        void Pumpjack(Vector2 c)
        {
            Flat("decals/decal_oil_stain_big", c + new Vector2(0, -.5f), 2.2f);
            Solid("props/lm_pumpjack", c, 5, 1.6f, 4, _lr.Next(2) == 0);
            Decor("props/barrel_mazut", c + new Vector2(3.5f, 1.4f));
            AddHiddenWall(c.X + 3.2f, c.Y + .9f, c.X + 3.8f, c.Y + 1.4f, 1);
            Barrels(c + new Vector2(-3, 2), 0.35f);
        }

        void Oak(Vector2 c)
        {
            Solid("trees/lm_oak_patriarch", c, 1.6f, 1.2f, 11);
            Flat("decals/decal_leaves_scatter", c + new Vector2(0, -1), 2.5f);
            Flat("decals/decal_flowers", c + new Vector2(3, 2), 1.2f);
        }

        void Farm(Vector2 c)
        {
            for (int k = 0; k < 3; k++) Solid("outdoor/haystack", c + new Vector2(-8 + k * 4.5f, -4 + Rf(-1, 1)), 3, 2, 2.5f);
            Solid("outdoor/hay_bales_angled", c + new Vector2(5, -4), 3, 1.5f, 1.2f);
            Flat("decals/decal_straw_scatter", c + new Vector2(-4, -3), 2.5f);
            for (int k = 0; k < 4; k++) Solid("outdoor/beehive", c + new Vector2(-6 + k * 2f, 4), .6f, .6f, 1);
            Decor("props/lm_scarecrow", c + new Vector2(7, 3));
            Flat("decals/decal_flowers", c + new Vector2(-3, 5.5f), 2f);
        }

        void Playground(Vector2 c)
        {
            Flat("decals/decal_sand_drift", c, 3f);
            Solid("outdoor/playground_swing", c + new Vector2(-4, 0), 3, .6f, 2.5f);
            Solid("outdoor/playground_slide_side", c + new Vector2(3, -1), 3, 1, 2);
            Solid("outdoor/playground_carousel", c + new Vector2(0, 4), 2, 1.5f, 1);
            Decor("outdoor/sandbox_mushroom", c + new Vector2(5, 4));
            for (int k = 0; k < 5; k++) Flat("decals/decal_grass_tuft_" + (k % 2 + 1), Around(c, 1, 6), 1.5f);
        }

        // ---------- линейные ----------

        /// <summary>Железная дорога: прямая запад→восток, насыпь, шпалы и рельсы, мост через реку, переезд, вагоны, будка стрелочника.</summary>
        void BuildRailway()
        {
            const float step = 6f;
            float bestS = 0, bestY = 0, bestX0 = 0;
            for (int t = 0; t < 400; t++)
            {
                float y = Rf(-HalfSize + 150, HalfSize - 150), x0 = Rf(-HalfSize + 60, 0), len = Rf(900, 1300);
                int land = 0, water = 0, bad = 0, roads = 0; bool wasWater = false, crossed = false;
                for (float x = x0; x < x0 + len && x < HalfSize - 40; x += step)
                {
                    var p = new Vector2(x, y);
                    if (NearMonument(p, 12)) bad++;
                    bool river = Terrain.IsRiver(p);
                    if (river) water++; else if (!Terrain.IsLand(p) || Terrain.BiomeAt(p) == Biome.Water) bad += 3; else land++;
                    if (river && !wasWater) crossed = true;
                    wasWater = river;
                    if (RoadDist(p) < 1) roads++;
                }
                if (!crossed || water > 14 || bad > 0 || roads == 0) continue;
                float s = land + Rf(0, 20);
                if (s > bestS) { bestS = s; bestY = y; bestX0 = x0; }
            }
            if (bestS <= 0) return;
            var line = new List<Vector2>();
            for (float x = bestX0; ; x += step)
            {
                var p = new Vector2(x, bestY);
                if (x > HalfSize - 40 || (!Terrain.IsLand(p) && !Terrain.IsRiver(p)) || NearMonument(p, 12)) break;
                line.Add(p);
                if (line.Count > 230) break;
            }
            Railway.AddRange(line);
            Landmarks.Add(("railway", line[line.Count / 2]));
            int lastCross = -99;
            for (int i = 0; i < line.Count; i++)
            {
                var p = line[i];
                bool river = Terrain.IsRiver(p) || Terrain.IsRiver(p + new Vector2(step / 2, 0));
                bool road = RoadDist(p) < 2;
                _lmAreas.Add((p, 4));
                if (!road) Strip(p + new Vector2(step / 2, 0), 4.2f);
                if (river)
                {
                    for (float dx = 0; dx < step; dx += 1.5f) Terrain.MarkBridge(p + new Vector2(dx, 0), 3);
                    Patches.Add(new MapPatch { Tile = "tiles/bfloor_wood", Min = p + new Vector2(0, -2.4f), Max = p + new Vector2(step, 1.0f) });
                    Decor("outdoor/concrete_rings", p + new Vector2(step / 2, 1.6f));   // опоры моста
                }
                else if (!road) Patches.Add(new MapPatch { Tile = "tiles/ground_gravel", Min = p + new Vector2(0, -2.6f), Max = p + new Vector2(step, 1.4f) });
                Flat("decals/rail_track", p + new Vector2(step / 2, 0));
                if (road && i - lastCross > 4)
                {
                    lastCross = i;
                    Landmarks.Add(("level_crossing", p));
                    Decor("props/checkpoint_barrier_angled", p + new Vector2(-4, -3));
                    Decor("props/checkpoint_barrier_angled", p + new Vector2(8, 3.5f), true);
                    var booth = p + new Vector2(-9, -4.5f);
                    if (RoadDist(booth) > 1.5f) { Solid("outdoor/kpp_booth", booth, 2.2f, 2, 2.6f); Landmarks.Add(("switchman", booth)); }
                }
            }
            // брошенный состав: 3 вагона подряд на прямом участке суши
            string[] wagons = { "vehicles/wagon_boxcar_green_side", "vehicles/wagon_flatcar_empty_side", "vehicles/wagon_gondola_coal_side" };
            for (int tries = 0, placed = 0; tries < 60 && placed == 0; tries++)
            {
                int i0 = _lr.Next(4, Math.Max(5, line.Count - 12));
                bool ok = true;
                for (int i = i0; i < i0 + 8 && ok; i++) if (i >= line.Count || Terrain.IsRiver(line[i]) || RoadDist(line[i]) < 6) ok = false;
                if (!ok) continue;
                for (int k = 0; k < 3; k++)
                {
                    var p = line[i0] + new Vector2(3 + k * 14f, 0.3f);
                    Solid(wagons[k], p, 13, 1.2f, 3.5f);
                }
                Landmarks.Add(("wagons", line[i0]));
                placed = 1;
            }
        }

        /// <summary>Наземный трубопровод на опорах через поля; через реку — по трубному мосту.</summary>
        void BuildPipeline()
        {
            float bestS = 0, bestY = 0, bestX0 = 0, bestLen = 0;
            foreach (var rl in Terrain.RiverLines)
                for (int t = 0; t < 200; t++)
                {
                    var rp = rl[_lr.Next(rl.Length)].p;
                    float len = Rf(300, 450), x0 = rp.X - Rf(.3f, .7f) * len, y = rp.Y;
                    if (Railway.Count > 0 && MathF.Abs(y - Railway[0].Y) < 60) continue;
                    int bad = 0, river = 0;
                    for (float x = x0; x < x0 + len; x += 5)
                    {
                        var p = new Vector2(x, y);
                        if (MathF.Abs(x) > HalfSize - 40 || NearMonument(p, 10)) bad++;
                        else if (Terrain.IsRiver(p)) river++;
                        else if (!Terrain.IsLand(p) || Terrain.BiomeAt(p) is Biome.Water or Biome.Forest) bad++;
                    }
                    if (bad > 3 || river == 0 || river > 8) continue;
                    float s = len + Rf(0, 50) - bad * 40;
                    if (s > bestS) { bestS = s; bestY = y; bestX0 = x0; bestLen = len; }
                }
            if (bestS <= 0) return;
            Landmarks.Add(("pipeline", new Vector2(bestX0 + bestLen / 2, bestY)));
            bool wasRiver = false;
            for (float x = bestX0; x < bestX0 + bestLen; x += 5)
            {
                var p = new Vector2(x + 2.5f, bestY);
                bool river = Terrain.IsRiver(p) || Terrain.IsRiver(p + new Vector2(4, 0)) || Terrain.IsRiver(p - new Vector2(4, 0));
                if (river)
                {
                    if (!wasRiver) Landmarks.Add(("pipe_bridge", p));
                    Decor("props/chem_pipe_bridge", p + new Vector2(1.5f, 0));
                    x += 3;
                }
                else
                {
                    if (NearMonument(p, 8) || !Terrain.IsLand(p) || RoadDist(p) < 2) { wasRiver = false; continue; }   // под дорогой — в земле
                    Strip(p, 2.6f);
                    Decor("outdoor/gas_pipe_side", p);
                    AddHiddenWall(p.X - 2.5f, p.Y - .3f, p.X + 2.5f, p.Y, .6f);
                }
                wasRiver = river;
                _lmAreas.Add((p, 3));
            }
        }

        /// <summary>Столбы с провисшими проводами вдоль второстепенных дорог (главные одеты в RoadNet.DressRoads).</summary>
        void DressSideRoads()
        {
            int lines = 0;
            foreach (var l in RoadLines)
            {
                if (l.Main || l.P.Count < 60 || lines >= 3) continue;
                lines++;
                Vector2? prev = null;
                int side = _lr.Next(2) == 0 ? 1 : -1;
                for (int i = 8; i < l.P.Count - 8; i += 22)
                {
                    var tg = Vector2.Normalize(l.P[Math.Min(i + 2, l.P.Count - 1)] - l.P[Math.Max(i - 2, 0)]);
                    var p = l.P[i] + new Vector2(-tg.Y, tg.X) * side * (l.Half + 1.6f);
                    if (NearMonument(p, 4) || !Terrain.IsLand(p) || !IsFree(p, .8f) || RoadDist(p) < 0.8f) { prev = null; continue; }
                    Decor(_lr.Next(4) == 0 ? "outdoor/power_pole_wood_angled" : "outdoor/power_pole_wood", p, _lr.Next(2) == 0);
                    AddHiddenWall(p.X - .2f, p.Y - .4f, p.X + .2f, p.Y, 8);
                    if (prev is Vector2 q && Vector2.Distance(q, p) < 60) Wire(q, 7.3f, p, 7.3f, 1.1f);
                    prev = p;
                }
                Landmarks.Add(("pole_line", l.P[l.P.Count / 2]));
            }
        }

        /// <summary>Блокпост на дороге у въезда в РТ: ежи и бетонные блоки по обочинам, полотно свободно.</summary>
        void Checkpoints(int count)
        {
            int n = 0;
            foreach (var l in RoadLines)
            {
                if (n >= count || l.A == null || l.P.Count < 30 || _lr.Next(2) == 0) continue;
                for (int i = 0; i < l.P.Count; i++)
                {
                    var p = l.P[i];
                    if (MonDist(p) < 30 || MonDist(p) > 40) continue;
                    var tg = Vector2.Normalize(l.P[Math.Min(i + 2, l.P.Count - 1)] - l.P[Math.Max(i - 2, 0)]);
                    var nr = new Vector2(-tg.Y, tg.X);
                    bool clash = false;
                    foreach (var a in _lmAreas) if (Vector2.Distance(a.c, p) < a.r + 10) clash = true;
                    if (clash) break;
                    Landmarks.Add(("checkpoint", p));
                    _lmAreas.Add((p, 8));
                    foreach (int s in new[] { 1, -1 })
                    {
                        var e = p + nr * s * (l.Half + 1.4f);
                        if (!Terrain.IsLand(e)) continue;
                        Solid("props/concrete_blocks_road", e, 2.2f, .8f, 1);
                        Solid("props/hedgehog_antitank", e + tg * 3.5f, 1.2f, 1, 1.3f);
                        Solid("props/hedgehog_antitank", e - tg * 3.5f, 1.2f, 1, 1.3f);
                    }
                    var post = p + nr * (l.Half + 5);
                    if (Terrain.IsLand(post) && IsFree(post, 1.5f)) Solid("outdoor/sandbags_curve_angled", post, 2.4f, 1.2f, .9f);
                    n++;
                    break;
                }
            }
        }

        /// <summary>Заправка у развилки: две дороги сходятся — навес и колонки рядом, не на полотне.</summary>
        void FuelStation()
        {
            for (int a = 0; a < RoadLines.Count; a++)
                foreach (var p in RoadLines[a].P)
                {
                    if (MonDist(p) < 50) continue;
                    bool fork = false;
                    for (int b = 0; b < RoadLines.Count && !fork; b++)
                        if (b != a && RoadLines[b].P.Exists(q => Vector2.DistanceSquared(p, q) < 9)) fork = true;
                    if (!fork) continue;
                    for (int k = 0; k < 8; k++)
                    {
                        var c = p + new Vector2(MathF.Cos(k * .785f), MathF.Sin(k * .785f)) * 16;
                        if (!Clear(c, 7)) continue;
                        Take("fuel_station", c, 7);
                        Patches.Add(new MapPatch { Tile = "tiles/ground_asphalt", Min = c + new Vector2(-6, -5), Max = c + new Vector2(6, 3) });
                        Decor("props/fuel_canopy", c);
                        Solid("props/fuel_pump", c + new Vector2(-2, -1), .8f, .6f, 1.6f);
                        Solid("props/fuel_pump", c + new Vector2(2, -1), .8f, .6f, 1.6f);
                        Solid("outdoor/kiosk_soyuzpechat", c + new Vector2(0, -4.5f), 2.4f, 1.6f, 2.5f);
                        Flat("decals/decal_oil_stain", c + new Vector2(0, 1.5f), 1.5f);
                        Barrels(c + new Vector2(5, -3), 0.6f);
                        return;
                    }
                }
        }

        /// <summary>Баржа на мели в устье; дерево поперёк узкого места реки (течение север–юг).</summary>
        void RiverScenes()
        {
            bool barge = false;
            foreach (var rl in Terrain.RiverLines)
            {
                if (barge || rl.Length < 4) break;
                foreach (var end in new[] { (rl[0].p, rl[3].p), (rl[^1].p, rl[^4].p) })
                {
                    var dir = Vector2.Normalize(end.Item2 - end.Item1);
                    var side = new Vector2(-dir.Y, dir.X);
                    for (float s = 6; s < 40 && !barge; s += 3)
                        for (float along = 0; along < 60 && !barge; along += 6)
                        {
                            var c = end.Item1 + dir * along + side * s;
                            if (Terrain.BiomeAt(c) != Biome.Beach || NearMonument(c, 20)) continue;
                            Landmarks.Add(("barge", c));
                            _lmAreas.Add((c, 9));
                            Strip(c, 9);
                            Solid("props/barge_rusted_side", c, 12, 2.5f, 3);
                            barge = true;
                        }
                    if (barge) break;
                }
            }
            // дерево через ручей
            float bw = 99; Vector2 bp = default;
            foreach (var rl in Terrain.RiverLines)
                for (int i = 2; i < rl.Length - 2; i++)
                {
                    var tg = Vector2.Normalize(rl[i + 2].p - rl[i - 2].p);
                    if (MathF.Abs(tg.Y) < .9f || rl[i].w > 9 || rl[i].w > bw || NearMonument(rl[i].p, 30) || RoadDist(rl[i].p) < 30) continue;
                    bool forest = Terrain.BiomeAt(rl[i].p + new Vector2(rl[i].w, 0)) == Biome.Forest || Terrain.BiomeAt(rl[i].p - new Vector2(rl[i].w, 0)) == Biome.Forest;
                    if (!forest && bw < 99) continue;
                    bw = rl[i].w; bp = rl[i].p;
                }
            if (bw < 99)
            {
                Landmarks.Add(("tree_across", bp));
                _lmAreas.Add((bp, 6));
                Decor("props/lm_tree_fallen", bp + new Vector2(0, .5f), _lr.Next(2) == 0);
                Decor("outdoor/stump", bp + new Vector2(-bw / 2 - 2.5f, 0));
            }
        }

        void LakeBoats(int count)
        {
            int n = 0;
            foreach (var (lc, lr) in Terrain.Lakes)
            {
                if (n >= count) break;
                for (int k = 0; k < 16; k++)
                {
                    var dir = new Vector2(MathF.Cos(k * .39f), MathF.Sin(k * .39f));
                    Vector2? at = null;
                    for (float s = lr * .6f; s < lr + 25; s += 1.5f)
                    {
                        var p = lc + dir * s;
                        if (Terrain.IsLand(p) && Terrain.BiomeAt(p) != Biome.Water) { at = p + dir * 1.5f; break; }
                    }
                    if (at is not Vector2 c || NearMonument(c, 15) || !IsFree(c, 2) || RoadDist(c) < 4) continue;
                    Landmarks.Add(("boat", c));
                    _lmAreas.Add((c, 4));
                    Strip(c, 3);
                    Solid(_lr.Next(2) == 0 ? "props/boat_lodka_red_side" : "props/boat_kazanka_side", c, 4, 1.2f, 1, dir.X < 0);
                    Decor("props/log_2", c + new Vector2(2.5f, 1.5f));
                    n++;
                    break;
                }
            }
        }
    }
}
