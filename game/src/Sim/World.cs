using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>Concrete/Crate/Rock — стены РТ и укрытия-блоки; остальное — пропсы-спрайты (машины, блоки, хлам), тоже укрытия.</summary>
    public enum WallKind { Concrete, Crate, Rock, Car, Barrier, Junk, Barrel, WoodCrate, Built }

    public sealed class MapWall
    {
        public Wall Wall;
        public WallKind Kind;
        public Structure Owner;        // часть постройки игрока (иначе — карта)
        public bool PassBullets;       // проём окна: пули и взгляд проходят
        public int Id;                 // номер стены карты (для клиента: какие убраны)
        public string Tex;             // текстура лица (стены готовых РТ), иначе по виду
        public bool Hidden;            // только хитбокс пропса: не рисуется
        public bool Prefab;            // часть готовой РТ
    }

    public struct Noise
    {
        public Vector2 Pos;
        public float Radius;
        public int SourceId;
        public float Time;
    }

    /// <summary>Авторитарная симуляция мира. Ничего не знает про Godot.</summary>
    public sealed partial class World
    {
        public const float Dt = 1f / 30f;
        public const float HalfSize = 500f;   // остров 1×1 км (шаг к 2×2 км, D25)
        public const float WalkSpeed = 4.5f;
        public const float SprintSpeed = 6.8f;
        public const float Accel = 40f;
        public const float NpcRespawnTime = 45f;
        public const float NpcDamageScale = 0.75f;
        public const float InteractRange = 1.9f;
        public const float PlaceRange = 3.5f;
        public const float BenchRange = 4f;

        // выживание: полная сытость кончается примерно за 40 минут, вода — за 30
        public const float FoodDecay = 100f / 2400f;
        public const float WaterDecay = 100f / 1800f;

        public float Time;
        public uint Tick;
        public readonly Random Rng;
        public readonly List<Actor> Actors = new();
        public readonly List<MapWall> Walls = new();
        public readonly List<ResourceNode> Nodes = new();
        public readonly List<Pickup> Pickups = new();
        public readonly List<Deployable> Deployables = new();
        public readonly List<Projectile> Projectiles = new();
        public readonly List<LootBag> Bags = new();
        public readonly List<SimEvent> Events = new();
        public readonly List<Noise> Noises = new();
        public readonly Dictionary<int, Vector2> NpcHomes = new();
        public readonly List<Vector2> PlayerSpawns = new();
        public readonly TerrainMap Terrain;
        readonly List<(NodeKind kind, float left)> _nodeRespawns = new();

        // пространственные сетки: проверки идут по соседним клеткам, а не по всей карте
        const float GridCell = 8f;
        readonly SpatialHash<MapWall> _wallGrid = new(GridCell);
        readonly SpatialHash<ResourceNode> _nodeGrid = new(GridCell);
        readonly SpatialHash<Deployable> _deployGrid = new(GridCell);
        readonly List<MapWall> _qWalls = new();
        readonly List<ResourceNode> _qNodes = new();
        readonly List<Deployable> _qDeploys = new();

        List<MapWall> WallsNear(Vector2 c, float r) => _wallGrid.QueryCircle(c, r, _qWalls);
        List<ResourceNode> NodesNear(Vector2 c, float r) => _nodeGrid.QueryCircle(c, r + 1f, _qNodes);
        List<Deployable> DeploysNear(Vector2 c, float r) => _deployGrid.QueryCircle(c, r + 1f, _qDeploys);
        List<MapWall> WallsAlong(Vector2 a, Vector2 b) => _wallGrid.QuerySegment(a, b, _qWalls);
        List<ResourceNode> NodesAlong(Vector2 a, Vector2 b) => _nodeGrid.QuerySegment(a, b, _qNodes);
        List<Deployable> DeploysAlong(Vector2 a, Vector2 b) => _deployGrid.QuerySegment(a, b, _qDeploys);

        /// <summary>Узлы и постройки в радиусе (для снапшотов) — через сетку, в свой список.</summary>
        public List<ResourceNode> NodesInRadius(Vector2 c, float r, List<ResourceNode> into) => _nodeGrid.QueryCircle(c, r, into);
        public List<Deployable> DeploysInRadius(Vector2 c, float r, List<Deployable> into) => _deployGrid.QueryCircle(c, r, into);

        void AddDeployable(Deployable d)
        {
            Deployables.Add(d);
            _deployGrid.Insert(d, d.Pos - new Vector2(d.Radius), d.Pos + new Vector2(d.Radius));
        }

        int _nextId = 1;

        public World(int seed)
        {
            Rng = new Random(seed);
            Seed = seed;
            Terrain = new TerrainMap(HalfSize, seed);
            BuildMap();
        }

        public int NextId() => _nextId++;
        float Range(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

        // ---------- карта ----------

        void AddWall(float x1, float y1, float x2, float y2, float h, WallKind kind)
        {
            var min = new Vector2(MathF.Min(x1, x2), MathF.Min(y1, y2));
            var max = new Vector2(MathF.Max(x1, x2), MathF.Max(y1, y2));
            var w = new MapWall { Wall = new Wall(min, max, h), Kind = kind, Id = _wallSeq++, Prefab = _prefabMode };
            Walls.Add(w);
            _wallGrid.Insert(w, min, max);
        }

        bool InMonument(Vector2 p) => NearMonument(p, 2);

        void BuildMap()
        {
            const float t = 0.35f, h = 2.6f;
            Monuments.Add(new Monument { Name = "Склад «Центральный»", Min = new Vector2(2, -38), Max = new Vector2(38, 2) });
            // РТ: огороженный склад (x 5..35, y -35..-5) с проходами
            AddWall(5, -35 - t, 18, -35 + t, h, WallKind.Concrete);
            AddWall(22, -35 - t, 35, -35 + t, h, WallKind.Concrete);
            AddWall(5, -5 - t, 17, -5 + t, h, WallKind.Concrete);
            AddWall(23, -5 - t, 35, -5 + t, h, WallKind.Concrete);
            AddWall(5 - t, -35, 5 + t, -22, h, WallKind.Concrete);
            AddWall(5 - t, -18, 5 + t, -5, h, WallKind.Concrete);
            AddWall(35 - t, -35, 35 + t, -5, h, WallKind.Concrete);
            // здание внутри
            AddWall(11, -29, 19, -28.4f, h, WallKind.Concrete);
            AddWall(11, -29, 11.6f, -21, h, WallKind.Concrete);
            AddWall(18.4f, -29, 19, -24, h, WallKind.Concrete);
            AddWall(11, -21.6f, 16, -21, h, WallKind.Concrete);
            // укрытия внутри
            AddWall(24, -14, 26, -12.8f, 1.0f, WallKind.Crate);
            AddWall(28, -26, 29.2f, -24, 1.0f, WallKind.Crate);
            AddWall(21, -31, 23, -30, 1.0f, WallKind.Crate);
            AddWall(8, -12, 10, -11, 1.0f, WallKind.Crate);

            AddCrate(new Vector2(14, -25));
            AddCrate(new Vector2(31, -31));
            AddCrate(new Vector2(30, -9));
            AddPickup(PickupKind.WaterBarrel, new Vector2(8, -31));
            foreach (var p in new[] { new Vector2(4, -36), new Vector2(36, -36), new Vector2(36, -2.5f), new Vector2(4, -20), new Vector2(18, -36), new Vector2(36, -18) })
                AddScrapSpot(NodeKind.Barrel, p);
            AddScrapSpot(NodeKind.Junk, new Vector2(33, -22));
            AddRecycler(new Vector2(20, -6));
            AddSafe(new Vector2(31, -35.5f));
            AddScrapSpot(NodeKind.Junk, new Vector2(7, -5));

            foreach (var p in new[] { new Vector2(15, -24), new Vector2(28, -30), new Vector2(29, -10),
                                      new Vector2(10, -15), new Vector2(22, -20), new Vector2(20, 2) })
                NpcHomes[-NpcHomes.Count - 1] = p;   // ключи временные, заменяются при спавне

            // малые РТ по острову
            // старые малые РТ-«коробки» убраны (пользователь: скучные «квадрат в квадрате») — их место заняли готовые РТ тира 0 и 1

            // раскладка по биомам: плотность и что встречается
            ScatterByBiome();

            // спавны — на пляжах
            for (int tries = 0; tries < 20000 && PlayerSpawns.Count < 16; tries++)
            {
                var p = new Vector2(Range(-HalfSize, HalfSize), Range(-HalfSize, HalfSize));
                if (Terrain.BiomeAt(p) == Biome.Beach && IsFree(p, 1.5f)) PlayerSpawns.Add(p);
            }
            if (PlayerSpawns.Count == 0) PlayerSpawns.Add(new Vector2(0, 40));

            // готовые РТ — в самом конце и своим генератором: остальная раскладка мира (и старые сохранения) не сдвигается
            PlaceSubstation();
            PlaceTier0();
            PlaceTier1();
            PlaceTier2();   // тир 2 (D50): Радиоцентр «Маяк» — большой, свой генератор
            PlaceWorldTires();   // покрышки — резина (после РТ, своим генератором)
        }

        // площадь на один объект (м²) и веса: дерево, камень, металл, сера, конопля, ягоды, бочка воды, ящик-укрытие, руины
        static readonly Dictionary<Biome, (float area, int[] w)> BiomeFill = new()
        {
            [Biome.Forest] = (40, new[] { 75, 6, 4, 1, 5, 7, 1, 0, 1 }),
            [Biome.Grassland] = (120, new[] { 22, 15, 8, 4, 20, 10, 3, 8, 10 }),
            [Biome.Arid] = (110, new[] { 8, 25, 15, 25, 3, 2, 3, 8, 11 }),
            [Biome.Tundra] = (80, new[] { 40, 20, 20, 8, 2, 3, 2, 2, 3 }),
            [Biome.Beach] = (220, new[] { 4, 30, 5, 0, 10, 0, 10, 25, 16 }),
            [Biome.Swamp] = (100, new[] { 35, 5, 5, 0, 20, 30, 2, 0, 3 }),
        };

        void ScatterByBiome()
        {
            const float probeArea = 30f;
            int probes = (int)(HalfSize * HalfSize * 4 / probeArea);
            for (int i = 0; i < probes; i++)
            {
                var p = new Vector2(Range(-HalfSize, HalfSize), Range(-HalfSize, HalfSize));
                var b = Terrain.BiomeAt(p);
                if (!BiomeFill.TryGetValue(b, out var fill) || InMonument(p)) continue;
                if (Rng.NextDouble() > probeArea / fill.area) continue;
                if (!IsFree(p, 1.5f)) continue;
                PlaceObject(PickWeighted(fill.w), p);
            }
        }

        int PickWeighted(int[] w)
        {
            int sum = 0;
            foreach (var x in w) sum += x;
            int r = Rng.Next(sum);
            for (int i = 0; i < w.Length; i++) { if (r < w[i]) return i; r -= w[i]; }
            return 0;
        }

        void PlaceObject(int cat, Vector2 p)
        {
            switch (cat)
            {
                case 0: AddNode(NodeKind.Tree, p); break;
                case 1: AddNode(NodeKind.Stone, p); break;
                case 2: AddNode(NodeKind.Metal, p); break;
                case 3: AddNode(NodeKind.Sulfur, p); break;
                case 4: AddPickup(PickupKind.Hemp, p); break;
                case 5: AddPickup(PickupKind.Berries, p); break;
                case 6: AddPickup(PickupKind.WaterBarrel, p); break;
                case 7:
                    // мелкие укрытия: ящик, бочка, хлам
                    int r7 = Rng.Next(5);
                    if (r7 == 3) AddScrapSpot(NodeKind.Barrel, p);
                    else if (r7 == 4) AddScrapSpot(NodeKind.Junk, p);
                    else AddProp(r7 == 0 ? WallKind.WoodCrate : r7 == 1 ? WallKind.Barrel : WallKind.Junk, p);
                    break;
                default:
                    // крупные укрытия: сгоревшая машина, бетонный блок, хлам
                    int r8 = Rng.Next(10);
                    AddProp(r8 < 3 ? WallKind.Car : r8 < 8 ? WallKind.Barrier : WallKind.Junk, p);
                    break;
            }
        }

        /// <summary>Пропс-укрытие: размер «следа» на земле под спрайт, высота ниже 2 м — обзор не закрывает.</summary>
        void AddProp(WallKind kind, Vector2 p)
        {
            var (w, d, ht) = kind switch
            {
                WallKind.Car => (3.3f, 1.5f, 1.5f),
                WallKind.Barrier => (1.2f, 0.5f, 0.9f),
                WallKind.Barrel => (0.55f, 0.5f, 1.0f),
                WallKind.WoodCrate => (0.75f, 0.65f, 0.8f),
                _ => (0.9f, 0.6f, 0.8f),
            };
            AddWall(p.X - w / 2, p.Y - d, p.X + w / 2, p.Y, ht, kind);
        }

        static int NodeCategory(NodeKind k) => k switch { NodeKind.Tree => 0, NodeKind.Stone => 1, NodeKind.Metal => 2, _ => 3 };

        /// <summary>Свободная точка на суше; для респавна узла — в биоме, где такой узел встречается.</summary>
        Vector2 RandomFreePoint(float clearance, NodeKind? kind = null)
        {
            for (int tries = 0; tries < 200; tries++)
            {
                var p = new Vector2(Range(-HalfSize + 5, HalfSize - 5), Range(-HalfSize + 5, HalfSize - 5));
                if (InMonument(p) || !BiomeFill.TryGetValue(Terrain.BiomeAt(p), out var fill)) continue;
                if (kind.HasValue && fill.w[NodeCategory(kind.Value)] == 0) continue;
                if (IsFree(p, clearance)) return p;
            }
            return new Vector2(0, 40);
        }
        /// <summary>Свободна ли точка (ни стен, ни узлов, ни установленного, ни людей).</summary>
        public bool IsFree(Vector2 p, float r)
        {
            if (!Terrain.IsLand(p)) return false;
            foreach (var w in WallsNear(p, r))
                if (Vector2.Distance(Vector2.Clamp(p, w.Wall.Min, w.Wall.Max), p) < r) return false;
            foreach (var n in NodesNear(p, r)) if (Vector2.Distance(n.Pos, p) < n.Radius + r) return false;
            foreach (var d in DeploysNear(p, r)) if (Vector2.Distance(d.Pos, p) < d.Radius + r) return false;
            foreach (var pk in Pickups) if (Vector2.Distance(pk.Pos, p) < r) return false;
            foreach (var a in Actors) if (!a.Dead && Vector2.Distance(a.Pos, p) < a.Radius + r) return false;
            return true;
        }

        void AddNode(NodeKind kind, Vector2 p)
        {
            float amount = kind switch
            {
                NodeKind.Tree => Range(120, 200),
                NodeKind.Stone => 150,
                NodeKind.Metal => 90,
                NodeKind.Barrel => 30,               // прочность бочки
                NodeKind.Junk => Rng.Next(1, 3),     // лома в куче (D49: голого лома мало — 1–2)
                NodeKind.Tire => Rng.Next(2, 5),     // резины в покрышке
                NodeKind.TireStack => Rng.Next(6, 11),
                _ => 60,
            };
            var node = new ResourceNode
            {
                Id = NextId(), Kind = kind, Pos = p,
                Radius = kind switch { NodeKind.Tree => Range(0.35f, 0.5f), NodeKind.Barrel => 0.35f, NodeKind.Junk => 0.6f, NodeKind.Tire => 0.4f, NodeKind.TireStack => 0.55f, _ => 0.7f },
                Amount = amount, MaxAmount = amount,
            };
            Nodes.Add(node);
            _nodeGrid.Insert(node, p - new Vector2(node.Radius), p + new Vector2(node.Radius));
        }

        void AddPickup(PickupKind kind, Vector2 p) => Pickups.Add(new Pickup { Id = NextId(), Kind = kind, Pos = p });

        /// <summary>
        /// Через сколько секунд пустой ящик РТ наполнится снова: тир 0 — 5–7 мин, тир 1 — 15–21, тир 2 — 20–30.
        /// Было 90 с — пользователь: «ящики откатываются очень быстро», лутать РТ без риска можно было по кругу.
        /// </summary>
        T Pick<T>(T[] arr) => arr[Rng.Next(arr.Length)];

        // ---------- детали тира 3: режиссёр выпускает с середины вайпа и дозирует в час (D46 «темп вайпа») ----------
        /// <summary>С какой секунды вайпа в лут попадают детали тира 3 (ствольная коробка АК, хромированный ствол, газоотвод).</summary>
        public const float Tier3From = 5f * 3600f;
        /// <summary>Сколько деталей тира 3 на весь мир за час (ящики, сейфы, ивенты).</summary>
        public const int Tier3PerHour = 4;
        readonly Dictionary<int, int> _tier3ByHour = new();

        /// <summary>Можно ли сейчас выпустить деталь тира 3 (фаза режиссёра, время, лимит в час).</summary>
        public bool Tier3Allowed() => Time >= Tier3From && (Director?.Phase ?? 0) >= 1 && _tier3ByHour.GetValueOrDefault((int)(Time / 3600)) < Tier3PerHour;

        /// <summary>Элита (Сайга-12, АПС, Мосинка) — только ивенты режиссёра, с 3 ч вайпа, поздняя фаза и не чаще раза в 3,5 ч на мир (D49).</summary>
        public const float EliteFrom = 3f * 3600f, EliteEvery = 3.5f * 3600f;
        float _eliteAt = -1e9f;

        /// <summary>С шансом положить элитный ствол в ящик ивента (режиссёр: фаза ≥ 2, лимит на мир).</summary>
        public bool TryElite(LootBag bag, int phase, double chance)
        {
            if (phase < 2 || Time < EliteFrom || Time - _eliteAt < EliteEvery || Rng.NextDouble() >= chance) return false;
            _eliteAt = Time;
            bag.Items.Add(Items.NewSlot(Pick(new[] { "weapon_saiga12", "weapon_aps", "weapon_mosin" })));
            return true;
        }

        /// <summary>С шансом положить деталь тира 3 (если режиссёр разрешает).</summary>
        public bool TryTier3(LootBag bag, double chance)
        {
            if (Rng.NextDouble() >= chance || !Tier3Allowed()) return false;
            int h = (int)(Time / 3600);
            _tier3ByHour[h] = _tier3ByHour.GetValueOrDefault(h) + 1;
            bag.Items.Add(new ItemSlot(Rng.Next(2) == 0 ? "comp_receiver_ak" : Pick(Weaponry.Tier3Parts), 1));   // ствольная коробка — только лут, она чаще
            return true;
        }

        public float CrateRespawnTime(LootBag bag) => bag.Tier switch { 0 => Range(420, 600), 1 => Range(900, 1260), _ => Range(1200, 1800) };   // тир 0 — 7–10 мин (D49: петля «своя РТ + переработчик» не должна кормить ломом без конца)

        void AddCrate(Vector2 pos, int tier = 2)
        {
            var bag = new LootBag { Id = NextId(), Pos = pos, IsCrate = true, Tier = tier };
            FillCrate(bag);
            Bags.Add(bag);
        }

        void FillCrate(LootBag bag)
        {
            bag.Items.Clear();
            bag.Search.Clear();   // новый лут — обыскивать заново
            void Maybe(double chance, string id, int min, int max)
            {
                if (Rng.NextDouble() < chance) bag.Items.Add(new ItemSlot(id, Rng.Next(min, max + 1)));
            }
            // уровень по карточкам Астры: без охраны — бытовое, малая охрана — компоненты и патроны, склад (РТ 2) — детали тира 2–3 (D46)
            bag.Unlocked = false;
            if (bag.Safe)
            {
                // сейф: гарантированно лом и детали, шанс на ствол тира 2, деталь тира 3 (по режиссёру) и взрывчатку
                bag.Items.Add(new ItemSlot("scrap", Rng.Next(15, 31)));
                bag.Items.Add(new ItemSlot(Pick(Weaponry.CommonParts), Rng.Next(1, 3)));
                bag.Items.Add(new ItemSlot(Economy.Tier2(Rng), Rng.Next(1, 3)));   // ценный лом (D49)
                Maybe(0.5, Economy.Tier1(Rng), 1, 2);
                bag.Items.Add(new ItemSlot(Pick(Weaponry.Tier2Parts), 1));
                Maybe(0.5, "metal_fragments", 50, 120);
                Maybe(0.4, "gunpowder", 30, 60);
                Maybe(0.4, "ammo_762", 10, 30);
                if (Rng.NextDouble() < 0.15) bag.Items.Add(Items.NewSlot(Pick(new[] { "weapon_grach", "weapon_pps43", "weapon_svt40" })));
                if (Rng.NextDouble() < 0.12) bag.Items.Add(new ItemSlot(Pick(Weaponry.FactoryAtts), 1));
                TryTier3(bag, 0.3);
                if (Rng.NextDouble() < 0.2) bag.Items.Add(Items.NewSlot("expl_satchel"));
                if (Rng.NextDouble() < 0.06) bag.Items.Add(Items.NewSlot("expl_c4"));
                Clothing.Loot(WearRng, bag, "safe");
                return;
            }
            // общие детали — во всех ящиках; карта доступа — в охраняемых
            // D49: в ящиках мало голого лома — компоненты и ценный лом (в переработчик или на крафт)
            if (bag.Tier == 0)
            {
                Maybe(0.75, Weaponry.CommonPart(Rng), 1, 1);
                Maybe(0.3, Weaponry.CommonPart(Rng), 1, 1);
            }
            else
            {
                Maybe(0.35, "comp_tape", 1, 2);
                Maybe(0.35, "comp_gears", 1, 2);
                Maybe(0.3, "comp_spring", 1, 2);
                Maybe(0.3, "comp_pipe", 1, 2);
            }
            if (bag.Tier >= 1) Maybe(0.18, "keycard", 1, 1);
            switch (bag.Tier)
            {
                case 0:
                    Maybe(0.85, "scrap", 3, 8);
                    Maybe(0.5, Economy.Tier0(Rng), 1, 2);
                    Maybe(0.2, Economy.Tier1(Rng), 1, 1);
                    if (Rng.NextDouble() < 0.1) bag.Items.Add(new ItemSlot(Weaponry.Tier2Part(Rng), 1));   // редкая деталь тира 2 (корпуса чаще)
                    Maybe(0.5, "cloth", 10, 25);
                    Maybe(0.5, "canned", 1, 2);
                    Maybe(0.5, "water_bottle", 1, 2);
                    Maybe(0.35, "bandage", 1, 2);
                    Maybe(0.3, "metal_fragments", 10, 30);
                    Maybe(0.2, "ammo_9mm", 5, 15);
                    Maybe(0.12, "ammo_12g", 3, 8);
                    Maybe(0.12, "comp_oil", 1, 1);
                    Maybe(0.05, "comp_lens", 1, 1);
                    Maybe(0.05, "comp_circuit", 1, 1);
                    if (Rng.NextDouble() < 0.04) bag.Items.Add(Items.NewSlot("weapon_pipeshotgun"));
                    if (Rng.NextDouble() < 0.05) bag.Items.Add(new ItemSlot(Pick(new[] { "att_grip_wood", "att_suppressor_can", "att_mag_twin_crude", "att_torch" }), 1));
                    break;
                case 1:
                    Maybe(0.85, "scrap", 10, 20);
                    Maybe(0.7, Economy.Tier1(Rng), 1, 2);
                    Maybe(0.3, Economy.Tier2(Rng), 1, 1);
                    Maybe(0.6, "metal_fragments", 25, 70);
                    Maybe(0.45, "gunpowder", 15, 35);
                    Maybe(0.45, "ammo_9mm", 10, 30);
                    Maybe(0.25, "ammo_12g", 5, 12);
                    Maybe(0.15, "ammo_762", 5, 15);
                    Maybe(0.4, "bandage", 1, 3);
                    Maybe(0.3, "canned", 1, 2);
                    Maybe(0.2, "comp_oil", 1, 2);
                    Maybe(0.12, "comp_lens", 1, 1);
                    Maybe(0.12, "comp_circuit", 1, 1);
                    if (Rng.NextDouble() < 0.3) bag.Items.Add(new ItemSlot(Weaponry.Tier2Part(Rng), 1));
                    if (Rng.NextDouble() < 0.06) bag.Items.Add(Items.NewSlot(Rng.Next(2) == 0 ? "weapon_pipeshotgun" : "weapon_sawnoff"));
                    if (Rng.NextDouble() < 0.12) bag.Items.Add(Items.NewSlot("lock_code"));
                    break;
                default:
                    // РТ 2 (склад с калашами): детали тира 2, редко — тира 3 (режиссёр дозирует), армейские обвесы
                    Maybe(0.4, "ammo_762", 15, 35);
                    Maybe(0.5, "ammo_9mm", 10, 30);
                    Maybe(0.25, "ammo_12g", 6, 14);
                    Maybe(0.85, "scrap", 20, 40);
                    Maybe(0.8, Economy.Tier2(Rng), 1, 2);
                    Maybe(0.4, Economy.Tier1(Rng), 1, 2);
                    Maybe(0.5, "bandage", 2, 4);
                    Maybe(0.55, "metal_fragments", 40, 90);
                    Maybe(0.45, "gunpowder", 25, 50);
                    Maybe(0.3, "comp_oil", 1, 2);
                    Maybe(0.15, "comp_lens", 1, 1);
                    Maybe(0.15, "comp_circuit", 1, 1);
                    if (Rng.NextDouble() < 0.35) bag.Items.Add(new ItemSlot(Weaponry.Tier2Part(Rng), 1));
                    if (Rng.NextDouble() < 0.08) bag.Items.Add(new ItemSlot(Pick(Weaponry.FactoryAtts), 1));
                    if (Rng.NextDouble() < 0.03) bag.Items.Add(new ItemSlot(Pick(Weaponry.ArmyAtts), 1));
                    if (Rng.NextDouble() < 0.05) bag.Items.Add(Items.NewSlot(Pick(new[] { "weapon_sawnoff", "weapon_mp133", "weapon_grach" })));
                    TryTier3(bag, 0.12);
                    if (Rng.NextDouble() < 0.06) bag.Items.Add(Items.NewSlot("expl_satchel"));
                    break;
            }
            Clothing.Loot(WearRng, bag, $"crate{Math.Clamp(bag.Tier, 0, 2)}");   // одежда, резина, кожа, антирадин (D47)
            if (bag.Empty) bag.Items.Add(new ItemSlot("scrap", 10));
        }

        // ---------- актёры ----------

        /// <summary>Наблюдатель для песочницы ботов: в мире его нет.</summary>
        public Actor SpawnSpectator()
        {
            var a = new Actor { Id = NextId(), Name = "Наблюдатель", Team = Team.Player, Spectator = true, Pos = Vector2.Zero };
            Actors.Add(a);
            return a;
        }

        public Actor SpawnPlayer(string name)
        {
            var a = new Actor { Id = NextId(), Name = name, Team = Team.Player };
            Actors.Add(a);
            RespawnPlayer(a);
            return a;
        }

        /// <summary>Как в Расте: просыпаешься голым на пляже с булыжником.</summary>
        public const float BagCooldown = 300f;

        /// <summary>Свой спальник, в котором можно проснуться сейчас (null — нет или перезаряжается).</summary>
        public Deployable ReadyBag(Actor a)
        {
            Deployable best = null;
            foreach (var d in Deployables)
                if (d.Kind == DeployKind.SleepBag && d.OwnerId == a.Id && (best == null || d.ReadyAt < best.ReadyAt)) best = d;
            return best;
        }

        // точки на пляже по всему берегу (лениво, свой генератор — раскладка мира не сдвигается) и где каждый просыпался последний раз
        List<Vector2> _beach;
        readonly Dictionary<int, Vector2> _lastBeach = new();
        Random _spawnRng;

        /// <summary>
        /// Пляж для голого старта (как в Расте): случайная точка вдоль всего берега, не ближе 60 м к живому игроку или боту
        /// и не там же, где этот игрок просыпался в прошлый раз (раньше 16 точек на всех — боты просыпались в одной точке и сразу дрались).
        /// </summary>
        Vector2 BeachSpawn(Actor a)
        {
            if (_beach == null)
            {
                _beach = new List<Vector2>();
                _spawnRng = new Random(Seed * 7919 + 17);
                for (int tries = 0; tries < 60000 && _beach.Count < 400; tries++)
                {
                    var p = new Vector2((float)(_spawnRng.NextDouble() * 2 - 1) * HalfSize, (float)(_spawnRng.NextDouble() * 2 - 1) * HalfSize);
                    if (Terrain.BiomeAt(p) == Biome.Beach && IsFree(p, 1.5f)) _beach.Add(p);
                }
                _beach.AddRange(PlayerSpawns);
            }
            Vector2 best = PlayerSpawns[_spawnRng.Next(PlayerSpawns.Count)];
            float bestScore = float.MinValue;
            bool hasLast = _lastBeach.TryGetValue(a.Id, out var last);
            for (int k = 0; k < 40; k++)
            {
                var p = _beach[_spawnRng.Next(_beach.Count)];
                if (!IsFree(p, 1.0f)) continue;
                float near = float.MaxValue;
                foreach (var o in Actors)
                    if (o != a && !o.Dead && !o.Spectator && o.Team == Team.Player) near = MathF.Min(near, Vector2.Distance(o.Pos, p));
                float fromLast = hasLast ? Vector2.Distance(last, p) : 999;
                if (near >= 60 && fromLast >= 80) { best = p; break; }
                float s = MathF.Min(near, 200) + MathF.Min(fromLast, 80);
                if (s > bestScore) { bestScore = s; best = p; }
            }
            _lastBeach[a.Id] = best;
            return best;
        }

        public void RespawnPlayer(Actor a, bool inBag = false)
        {
            var bag = inBag ? ReadyBag(a) : null;
            if (bag != null && Time < bag.ReadyAt) bag = null;
            a.Pos = bag != null ? bag.Pos + new Vector2(0.9f, 0) : BeachSpawn(a);
            if (bag != null) bag.ReadyAt = Time + BagCooldown;
            a.Vel = Vector2.Zero;
            a.Hp = a.MaxHp;
            a.Food = 60;
            a.Water = 60;
            a.Dead = false;
            a.ClearAll();
            a.AddSlot(Items.NewSlot("tool_rock"));
            a.Add("bandage", 1);
            Events.Add(new SimEvent { Kind = SimEventKind.Respawn, ActorId = a.Id, Pos = a.Pos });
        }

        public List<Actor> SpawnNpcs()
        {
            var homes = new List<Vector2>(NpcHomes.Values);
            NpcHomes.Clear();
            var list = new List<Actor>();
            foreach (var home in homes)
            {
                var a = new Actor { Id = NextId(), Name = "Охранник", Team = Team.Npc };
                NpcHomes[a.Id] = home;
                Actors.Add(a);
                RespawnNpc(a);
                list.Add(a);
            }
            return list;
        }

        void RespawnNpc(Actor a)
        {
            a.Pos = NpcHomes[a.Id];
            a.Vel = Vector2.Zero;
            a.Hp = a.MaxHp;
            a.Dead = false;
            a.ClearAll();
            if (NpcKit.TryGetValue(NpcHomes[a.Id], out var kit) && kit is "club" or "bow")
            {
                // тир 1: сторож с дубиной (копьё) или лучник
                a.AddSlot(Items.NewSlot(kit == "bow" ? "weapon_bow" : "weapon_spear"));
                if (kit == "bow") a.Add("arrow", Rng.Next(10, 20));
            }
            else if (kit == "pipe")
            {
                // тир 1: сторож с самопалом (12 к) или наганом, не калаш
                bool shotgun = Rng.NextDouble() < 0.4;
                a.AddSlot(Items.NewSlot(shotgun ? "weapon_pipeshotgun" : "weapon_nagant", shotgun ? 1 : 7));
                a.Add(shotgun ? "ammo_12g" : "ammo_9mm", Rng.Next(10, 25));
            }
            else if (kit is "pps" or "grach" or "svt")
            {
                // тир 2 (Радиоцентр): ППС-43, Грач, снайпер с СВТ-40 на вышке
                string gun = kit == "pps" ? "weapon_pps43" : kit == "svt" ? "weapon_svt40" : "weapon_grach";
                a.AddSlot(Items.NewSlot(gun, kit == "pps" ? 35 : kit == "svt" ? 10 : 17));
                a.Add(kit == "svt" ? "ammo_762" : "ammo_9mm", Rng.Next(30, 70));
                if (Rng.NextDouble() < 0.3) a.Add(Economy.Tier2(Rng), 1);
            }
            else
            {
                a.AddSlot(Items.NewSlot("weapon_akm", 30));
                a.Add("ammo_762", Rng.Next(30, 75));
            }
            // одежда по тиру РТ (D47): тир 1 (дубина, лук, самопал) — ватник или кожа, склад с калашами — армейская
            Dress(a, Clothing.GuardKit(WearRng, kit is "club" or "bow" or "pipe" ? 1 : 2));
            a.Add("scrap", Rng.Next(2, 9));
            if (Rng.NextDouble() < 0.3) a.Add(Economy.Tier1(Rng), 1);
            if (Rng.NextDouble() < 0.12) a.Add("keycard", 1);
            if (Rng.NextDouble() < 0.4) a.Add("bandage", 1);
            if (Rng.NextDouble() < 0.3) a.Add("canned", 1);
        }

        void GiveDebugKit(Actor a)
        {
            // повторное нажатие — набор для рейда: столб и факел меняются на взрывчатку
            for (int i = 0; i < Actor.BeltSize; i++)
                if (a.Belt[i]?.Id == "build_plan")
                {
                    for (int k = 0; k < Actor.BeltSize; k++)
                        if (a.Belt[k]?.Id is "deploy_claim" or "weapon_torch" or "tool_rock" or "tool_hammer") a.Belt[k] = null;
                    a.Add("expl_c4", 10);
                    a.Add("expl_fire", 5);
                    a.Add("expl_satchel", 10);
                    a.Add("lock_code", 1);
                    a.Add("deploy_box", 1);
                    Info(a, "Отладочный набор: взрывчатка");
                    return;
                }
            a.AddSlot(Items.NewSlot("weapon_akm", 30));
            a.AddSlot(Items.NewSlot("build_plan"));
            a.AddSlot(Items.NewSlot("tool_hammer"));
            a.AddSlot(Items.NewSlot("deploy_claim"));
            a.AddSlot(Items.NewSlot("weapon_torch"));
            a.Add("expl_c4", 6);
            a.Add("expl_satchel", 10);
            a.Add("ammo_762", 120);
            a.Add("comp_oil", 3);
            foreach (var at in new[] { "att_reddot", "att_mag_ext" }) a.Add(at, 1);
            a.Add("punchcard_1", 2);   // проверить Подстанцию, пока РТ тира 0 нет в игре
            a.Add("metal_ore", 60);
            a.Add("sulfur_ore", 30);
            foreach (var (id, n) in new[] { ("wood", 5000), ("stone", 5000), ("metal_fragments", 3000), ("cloth", 100), ("scrap", 500), ("charcoal", 100), ("sulfur", 100), ("bandage", 5) })
                a.Add(id, n);
            Info(a, "Отладочный набор выдан");
        }

        public Actor Find(int id)
        {
            foreach (var a in Actors) if (a.Id == id) return a;
            return null;
        }

        /// <summary>Последнее сообщение каждому (боты читают причины отказа).</summary>
        public readonly Dictionary<int, string> LastInfo = new();

        void Info(Actor a, string text) { LastInfo[a.Id] = text; Events.Add(new SimEvent { Kind = SimEventKind.Info, ActorId = a.Id, Pos = a.Pos, Text = text }); }

        // ---------- восприятие ----------

        public bool HasLineOfSight(Vector2 a, Vector2 b)
        {
            SimPerf.Count("обзор (LOS)");
            foreach (var w in WallsAlong(a, b))
                if (w.Wall.BlocksVision && Geo.SegmentAabb(a, b, w.Wall.Min, w.Wall.Max, out _)) return false;
            foreach (var n in NodesAlong(a, b))
                if (n.BlocksVision && Geo.SegmentCircle(a, b, n.Pos, n.Radius * 0.9f, out _)) return false;
            return true;
        }

        /// <summary>Обзор персонажа: прицеливание увеличивает дальность и сужает конус.</summary>
        public float ViewRange(Actor a) => (50f + 30f * a.Peek) * (1f - 0.4f * NightFactorAt(TimeOfDay) * (a.Held?.Id == "weapon_torch" || a.Weapon?.Flashlight == true ? 0.35f : 1f));   // факел (и фонарь на стволе) почти снимает ночной штраф

        // ---------- время суток ----------
        public const float DayLength = 1500f;   // сутки 25 минут: день ~20, ночь ~5 (по просьбе пользователя)
        const float NightShare = 0.2f;           // доля реального времени суток на ночь
        const float DayFrom = 0.25f, DayTo = 0.80f;   // TimeOfDay середины рассвета и заката (ночной фактор 0,5)
        /// <summary>Фаза суток по реальному времени 0..1 (равномерно). Вайп начинается утром.</summary>
        public float DayPhase => ((Time + TimeOffset) / DayLength + 0.16f) % 1f;
        /// <summary>0..1: 0 — полночь, 0.25 — рассвет, 0.5 — полдень, 0.75 — закат. Часы ночью идут быстрее: ночь короче дня.</summary>
        public float TimeOfDay => PhaseToTod(DayPhase);
        public float TimeOffset;

        static float PhaseToTod(float p) => p < 1f - NightShare
            ? DayFrom + p / (1f - NightShare) * (DayTo - DayFrom)
            : (DayTo + (p - (1f - NightShare)) / NightShare * (1f + DayFrom - DayTo)) % 1f;
        static float TodToPhase(float t)
        {
            if (t >= DayFrom && t < DayTo) return (t - DayFrom) / (DayTo - DayFrom) * (1f - NightShare);
            float n = (t - DayTo + 1f) % 1f;   // сколько ночи прошло после заката
            return 1f - NightShare + n / (1f + DayFrom - DayTo) * NightShare;
        }

        /// <summary>Отладка (клавиша N): день ↔ ночь — прыжок к полудню или полуночи.</summary>
        void ToggleDayNight()
        {
            float target = TodToPhase(NightFactorAt(TimeOfDay) > 0.5f ? 0.5f : 0.0f);
            TimeOffset += ((target - DayPhase + 1f) % 1f) * DayLength;
        }
        /// <summary>0 днём, 1 глубокой ночью, плавно на рассвете и закате.</summary>
        public static float NightFactorAt(float t)
        {
            float dawn = Math.Clamp((t - 0.2f) / 0.1f, 0f, 1f);
            float dusk = Math.Clamp((t - 0.75f) / 0.1f, 0f, 1f);
            return 1f - dawn + dusk;
        }
        public static float ViewCone(Actor a) => 130f - 70f * a.Peek;

        /// <summary>Видит ли наблюдатель человека. Присевшего прячут и низкие укрытия (машины, блоки, ящики).</summary>
        public bool CanSeeActor(Actor viewer, Actor target, float range, float coneDeg)
        {
            if (!CanSee(viewer, target.Pos, range, coneDeg)) return false;
            if (!target.Crouching || Vector2.Distance(viewer.Pos, target.Pos) < 2.5f) return true;
            foreach (var w in WallsAlong(viewer.Pos, target.Pos))
                if (!w.Wall.BlocksVision && Geo.SegmentAabb(viewer.Pos, target.Pos, w.Wall.Min, w.Wall.Max, out _)) return false;
            return true;
        }

        /// <summary>Видит ли наблюдатель цель: конус обзора, дальность, стены. Вплотную чувствуется всегда.</summary>
        public bool CanSee(Actor viewer, Vector2 target, float range = 45f, float coneDeg = 120f)
        {
            var d = target - viewer.Pos;
            float dist = d.Length();
            if (dist > range) return false;
            if (dist < 2.5f) return true;
            float ang = MathF.Atan2(d.Y, d.X);
            if (MathF.Abs(Geo.AngleDiff(viewer.Facing, ang)) > coneDeg * 0.5f * MathF.PI / 180f) return false;
            return HasLineOfSight(viewer.Pos, target);
        }

        public bool NearBench(Vector2 p) => BenchLevel(p) > 0;

        /// <summary>Самый высокий уровень верстака рядом (0 — нет).</summary>
        public int BenchLevel(Vector2 p)
        {
            int best = 0;
            foreach (var d in DeploysNear(p, BenchRange))
                if (d.Kind == DeployKind.Workbench && Vector2.Distance(d.Pos, p) < BenchRange) best = Math.Max(best, Math.Max(1, d.Level));
            return best;
        }

        public bool CanCraft(Actor a, Recipe r)
        {
            if (!Tech.Knows(a, r.Output)) return false;
            if (r.Bench > 0 && BenchLevel(a.Pos) < r.Bench) return false;
            foreach (var (id, n) in r.Cost) if (a.Count(id) < n) return false;
            return true;
        }

        // ---------- шаг ----------

        public void Step(Dictionary<int, InputCmd> inputs)
        {
            Events.Clear();
            Time += Dt;
            Tick++;
            Noises.RemoveAll(n => Time - n.Time > 3f);
            long t0 = SimPerf.Begin();

            foreach (var a in Actors)
            {
                if (!inputs.TryGetValue(a.Id, out var cmd)) cmd = InputCmd.Empty;
                if (a.Spectator) { StepSpectator(a, cmd); continue; }
                if (a.Dead)
                {
                    if (a.Team == Team.Npc && Time - a.DeadTime > NpcRespawnTime) RespawnNpc(a);
                    else if (a.Team == Team.Player && (cmd.Respawn || cmd.RespawnBag)) RespawnPlayer(a, cmd.RespawnBag);
                    continue;
                }
                StepActor(a, cmd);
            }
            SimPerf.End("шаг: персонажи", t0);

            t0 = SimPerf.Begin();
            SeparateActors();
            SimPerf.End("шаг: раздвижка", t0);
            t0 = SimPerf.Begin();
            StepProjectiles();
            SimPerf.End("шаг: пули", t0);
            t0 = SimPerf.Begin();
            StepBags();
            StepPickupsAndNodes();
            StepFurnaces();
            StepRecyclers();
            StepLathes();
            if (Tick % 30 == 0) TrackArms();
            StepClaims();
            StepThrowns();
            StepFires();
            StepSubstations();
            StepTier0();
            StepTier1();
            StepTier2();
            foreach (var s in DirectorStashes.ToArray()) StepStash(s);
            (Director ??= new Director(Seed)).Step(this);
            SimPerf.End("шаг: прочее (печи, РТ, сумки)", t0);
        }

        void StepActor(Actor a, InputCmd cmd)
        {
            // смена слота пояса
            if (cmd.SelectSlot >= 0 && cmd.SelectSlot < Actor.BeltSize && cmd.SelectSlot != a.Current)
            {
                if (a.Weapon != null) a.Weapon.ReloadLeft = 0;
                a.Current = cmd.SelectSlot;
            }

            // движение
            var move = cmd.Move;
            if (move.LengthSquared() > 1) move = Vector2.Normalize(move);
            a.Peek = Math.Clamp(cmd.Peek, 0f, 1f);
            a.Swimming = !Terrain.IsLand(a.Pos);
            a.Crouching = cmd.Crouch && !a.Swimming;
            float speed = WalkSpeed * (1f - 0.45f * a.Peek) * (a.Crouching ? 0.5f : 1f);   // прицел и присед — медленнее
            if (a.Swimming) { speed = WalkSpeed * 0.45f; cmd.Sprint = false; }              // плавание (D25)
            bool sprint = false;
            var toAimDir = cmd.Aim - a.Pos;
            a.LastAim = cmd.Aim;
            if (cmd.Code >= 0) EnterCode(a, cmd.CodeDoor, cmd.Code);
            if (cmd.Sprint && !a.Crouching && move.LengthSquared() > 0.01f && a.Peek < 0.3f && toAimDir.LengthSquared() > 0.01f)
            {
                // спринт только вперёд: чем сильнее смотришь в сторону от бега, тем ближе к шагу
                float off = MathF.Abs(Geo.AngleDiff(MathF.Atan2(move.Y, move.X), MathF.Atan2(toAimDir.Y, toAimDir.X))) * 180f / MathF.PI;
                float k = 1f - Math.Clamp((off - 30f) / 30f, 0f, 1f);
                speed = WalkSpeed + (SprintSpeed - WalkSpeed) * k;
                sprint = k > 0.5f;
            }
            speed *= SpeedMul(a);   // тяжёлая броня: до −15% (D47)
            a.Vel = Geo.MoveToward(a.Vel, move * speed, Accel * Dt);
            var before = a.Pos;
            a.Pos += a.Vel * Dt;
            ResolveCollisions(a);
            a.Swimming = !Terrain.IsLand(a.Pos);

            // взгляд
            if (toAimDir.LengthSquared() > 0.04f) a.Facing = MathF.Atan2(toAimDir.Y, toAimDir.X);

            StepSurvival(a, sprint);
            StepEnvironment(a);   // радиация и холод (D47)
            if (a.Dead) return;
            ArmorRattle(a, sprint);

            // лечение и еда
            a.HealCooldown -= Dt;
            if (cmd.Heal && a.HealCooldown <= 0 && a.Hp < a.MaxHp && a.Take("bandage", 1) > 0)
            {
                a.Hp = MathF.Min(a.MaxHp, a.Hp + 25 * HealMul(a));   // тошнота от радиации — хуже лечит
                a.HealCooldown = 2f;
            }
            if (cmd.Eat) Eat(a);
            if (cmd.DebugKit && a.Team == Team.Player) GiveDebugKit(a);
            if (cmd.ToggleTime && a.Team == Team.Player) ToggleDayNight();

            if (cmd.Craft >= 0 && cmd.Craft < Recipes.All.Count) QueueCraft(a, Recipes.All[cmd.Craft]);
            if (!string.IsNullOrEmpty(cmd.Research)) Research(a, cmd.Research);
            StepCrafting(a);

            if (a.PendingSwingAt > 0 && Time >= a.PendingSwingAt)
            {
                a.PendingSwingAt = -1;
                if (a.PendingSwing != null && a.Weapon == a.PendingSwing) Swing(a, a.PendingSwing);   // сменил предмет — удар отменён
                a.PendingSwing = null;
            }
            if (!a.Swimming) StepHeld(a, cmd, sprint);   // в воде руки заняты плаванием
            else a.FireHeldLastTick = cmd.Fire;

            if (cmd.Interact) Interact(a);
            StepUi(a, cmd);
            DropOverflow(a);
            if (cmd.SetPiece >= 0 && cmd.SetPiece <= (int)StructKind.TriFoundation) a.BuildPiece = (StructKind)cmd.SetPiece;
        }

        void ResolveCollisions(Actor a)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var w in WallsNear(a.Pos, a.Radius + 0.5f))
                    a.Pos = Geo.PushOut(a.Pos, a.Radius, w.Wall.Min, w.Wall.Max);
                foreach (var n in NodesNear(a.Pos, a.Radius)) a.Pos = PushOutCircle(a.Pos, a.Radius, n.Pos, n.Radius);
                foreach (var d in DeploysNear(a.Pos, a.Radius)) a.Pos = PushOutCircle(a.Pos, a.Radius, d.Pos, d.Radius);
            }
            a.Pos = Vector2.Clamp(a.Pos, new Vector2(-HalfSize + a.Radius), new Vector2(HalfSize - a.Radius));
        }


        static Vector2 PushOutCircle(Vector2 p, float r, Vector2 c, float cr)
        {
            var d = p - c;
            float dist = d.Length(), min = r + cr;
            if (dist >= min) return p;
            if (dist < 1e-5f) return c + new Vector2(min, 0);
            return c + d / dist * min;
        }

        // ---------- выживание ----------

        void StepSurvival(Actor a, bool sprint)
        {
            if (a.Team == Team.Npc) return;   // охране РТ голод не нужен
            a.Food = MathF.Max(0, a.Food - FoodDecay * (sprint ? 1.5f : 1f) * Dt);
            a.Water = MathF.Max(0, a.Water - WaterDecay * (sprint ? 1.5f : 1f) * Dt);
            // сытый — понемногу лечится, на нуле — понемногу теряет здоровье
            if (a.Food > 40 && a.Water > 40) a.Hp = MathF.Min(a.MaxHp, a.Hp + 0.25f * HealMul(a) * Dt);
            float starving = (a.Food <= 0 ? 0.35f : 0) + (a.Water <= 0 ? 0.35f : 0);
            if (starving > 0)
            {
                a.Hp -= starving * Dt;
                if (a.Hp <= 0) Kill(a, -1);
            }
        }

        void Eat(Actor a)
        {
            if (TakeAntirad(a)) return;   // облучён и есть антирадин — сначала он (D47)
            // выбираем еду, которая лучше всего закрывает текущий голод и жажду
            string best = null;
            float bestScore = 0;
            foreach (var s in a.Pack)
            {
                if (s == null) continue;
                var d = Items.Get(s.Id);
                if (d == null || d.Category != ItemCategory.Food) continue;
                float score = MathF.Min(d.Food, 100 - a.Food) + MathF.Min(d.Water, 100 - a.Water);
                if (score > bestScore) { bestScore = score; best = s.Id; }
            }
            if (best == null) { Info(a, "Нечего есть"); return; }
            var def = Items.Get(best);
            a.Take(best, 1);
            a.Food = MathF.Min(100, a.Food + def.Food);
            a.Water = MathF.Min(100, a.Water + def.Water);
            Info(a, $"Съедено: {def.Name}");
        }

        // ---------- крафт ----------

        void QueueCraft(Actor a, Recipe r)
        {
            if (a.CraftQueue.Count >= 8) { Info(a, "Очередь крафта заполнена"); return; }
            if (!Tech.Knows(a, r.Output)) { Info(a, $"{Items.Name(r.Output)}: сначала изучи на верстаке"); return; }
            if (r.Bench > 0 && BenchLevel(a.Pos) < r.Bench) { Info(a, $"Нужен верстак {r.Bench} рядом"); return; }
            if (!CanCraft(a, r)) { Info(a, "Не хватает ресурсов"); return; }
            foreach (var (id, n) in r.Cost) a.Take(id, n);   // как в Расте: ресурсы списываются сразу
            a.CraftQueue.Add(new CraftJob { Recipe = r, Left = r.Time });
        }

        /// <summary>Можно ли изучить узел прямо сейчас; причина — для подсказки.</summary>
        public bool CanResearch(Actor a, TechNode n, out string why)
        {
            why = "";
            if (a.Known.Contains(n.Id)) { why = "уже изучено"; return false; }
            foreach (var r in n.Requires) if (!a.Known.Contains(r)) { why = $"сначала: {Items.Name(r)}"; return false; }
            if (n.RequiresAny.Length > 0 && !System.Array.Exists(n.RequiresAny, a.Known.Contains)) { why = $"сначала: {string.Join(" или ", System.Array.ConvertAll(n.RequiresAny, Items.Name))}"; return false; }
            int lvl = BenchLevel(a.Pos);
            if (lvl == 0) { why = "подойди к верстаку"; return false; }
            if (lvl < n.Bench) { why = $"нужен верстак {n.Bench}"; return false; }
            if (a.Count("scrap") < n.Scrap) { why = $"нужно {n.Scrap} металлолома"; return false; }
            return true;
        }

        /// <summary>Вехи прогресса (для гонки ботов и отчёта): «[мм:сс] Сёма — верстак 2».</summary>
        public readonly List<string> Milestones = new();
        readonly HashSet<string> _milestoneKeys = new();

        public void Milestone(Actor a, string what)
        {
            if (!_milestoneKeys.Add(a.Id + "|" + what)) return;
            int m = (int)(Time / 60), s = (int)(Time % 60);
            Milestones.Add($"[{m:00}:{s:00}] {a.Name} — {what}");
        }

        readonly HashSet<(int id, int tier)> _armsSeen = new();
        readonly HashSet<int> _armsWorld = new();

        /// <summary>
        /// Летопись темпа вайпа (D46): первый ствол каждого тира у каждого игрока/бота и впервые в мире —
        /// неважно, скрафтил, нашёл или снял с трупа. Тир 4 — элита.
        /// </summary>
        void TrackArms()
        {
            foreach (var a in Actors)
            {
                if (a.Team != Team.Player || a.Spectator || a.Dead) continue;
                for (int k = 0; k < Actor.BeltSize + Actor.PackSize; k++)
                {
                    var s = k < Actor.BeltSize ? a.Belt[k] : a.Pack[k - Actor.BeltSize];
                    if (s?.State?.Def is not WeaponDef wd || wd.Melee || wd.Tier < 1) continue;
                    if (!_armsSeen.Add((a.Id, wd.Tier))) continue;
                    string tier = wd.Tier >= 4 ? "элита" : $"тир {wd.Tier}";
                    int m = (int)(Time / 60), sec = (int)(Time % 60);
                    Milestones.Add($"[{m:00}:{sec:00}] {a.Name} — первый ствол ({tier}): {Items.Name(wd.Id)}");
                    if (_armsWorld.Add(wd.Tier)) Milestones.Add($"[{m:00}:{sec:00}] МИР — впервые ствол ({tier}): {Items.Name(wd.Id)} у {a.Name}");
                }
            }
        }

        void Research(Actor a, string id)
        {
            var n = Tech.Get(id);
            if (n == null) return;
            if (!CanResearch(a, n, out string why)) { Info(a, $"{Items.Name(id)}: {why}"); return; }
            a.Take("scrap", n.Scrap);
            a.Known.Add(id);
            Info(a, $"Изучено: {Items.Name(id)}");
            Milestone(a, $"изучил: {Items.Name(id)}");
        }

        void StepCrafting(Actor a)
        {
            if (a.CraftQueue.Count == 0) return;
            var job = a.CraftQueue[0];
            job.Left -= Dt;
            if (job.Left > 0) return;
            a.CraftQueue.RemoveAt(0);
            var r = job.Recipe;
            if (!a.Add(r.Output, r.Count))
            {
                // пояс полон — кладём к ногам
                var bag = new LootBag { Id = NextId(), Pos = a.Pos, Despawn = 300 };
                bag.Items.Add(Items.NewSlot(r.Output));
                Bags.Add(bag);
                Info(a, "Пояс полон — предмет у ног");
            }
            Events.Add(new SimEvent { Kind = SimEventKind.Crafted, ActorId = a.Id, Pos = a.Pos, Text = $"{Items.Name(r.Output)}{(r.Count > 1 ? " ×" + r.Count : "")}" });
            if (Items.Get(r.Output)?.Weapon is WeaponDef wd && !wd.Melee) Milestone(a, $"скрафтил: {Items.Name(r.Output)}");
        }

        // ---------- предмет в руках ----------

        void StepHeld(Actor a, InputCmd cmd, bool sprint)
        {
            var held = a.Held;
            bool trigger;
            if (held == null) { a.FireHeldLastTick = cmd.Fire; return; }
            var def = held.Def;

            if (def.Lock)
            {
                trigger = cmd.Fire && !a.FireHeldLastTick;
                a.FireHeldLastTick = cmd.Fire;
                if (trigger) InstallLock(a);
                return;
            }

            if (def.Explosive != null)
            {
                trigger = cmd.Fire && !a.FireHeldLastTick;
                a.FireHeldLastTick = cmd.Fire;
                if (trigger && !sprint) Throw(a, cmd.Aim);
                return;
            }

            if (def.Build)
            {
                // план постройки: R — следующий элемент, ЛКМ — поставить
                if (cmd.Reload) a.BuildPiece = Building.NextPiece(a.BuildPiece);
                trigger = cmd.Fire && !a.FireHeldLastTick;
                a.FireHeldLastTick = cmd.Fire;
                if (trigger) TryBuild(a, cmd.Aim);
                return;
            }

            if (def.Deploy != DeployKind.None)
            {
                trigger = cmd.Fire && !a.FireHeldLastTick;
                a.FireHeldLastTick = cmd.Fire;
                if (trigger) TryPlace(a, cmd.Aim, def);
                return;
            }

            var w = held.State;
            var wd = w.Def;
            w.Cooldown -= Dt;
            w.Bloom = MathF.Max(0, w.Bloom - wd.BloomRecoverDeg * Dt);
            // отпустил курок — ствол возвращается, очередь сбрасывается
            w.SinceShot += Dt;
            if (w.SinceShot > wd.FireInterval * 1.6f)
            {
                w.Recoil = MathF.Sign(w.Recoil) * MathF.Max(0, MathF.Abs(w.Recoil) - wd.RecoilRecoverDeg * Dt);
                if (w.SinceShot > 0.35f) w.Streak = 0;
            }

            if (wd.Melee)
            {
                trigger = cmd.Fire && w.Cooldown <= 0 && !sprint;
                a.FireHeldLastTick = cmd.Fire;
                if (trigger) StartSwing(a, w);
                return;
            }

            if (w.ReloadLeft > 0)
            {
                w.ReloadLeft -= Dt;
                if (w.ReloadLeft <= 0)
                {
                    w.ReloadLeft = 0;
                    w.Ammo += a.Take(wd.AmmoId, w.MagSize - w.Ammo);
                }
            }
            else if (cmd.Reload && w.Ammo < w.MagSize && a.Count(wd.AmmoId) > 0)
            {
                w.BurstLeft = 0;
                w.ReloadLeft = w.Reload;
                Events.Add(new SimEvent { Kind = SimEventKind.Reload, ActorId = a.Id, Pos = a.Pos, Text = wd.Id });
            }

            bool edge = cmd.Fire && !a.FireHeldLastTick;
            a.FireHeldLastTick = cmd.Fire;
            // отсечка (АН-94): начатая очередь доигрывается сама
            if (w.BurstLeft > 0)
            {
                if (w.Cooldown > 0 || w.ReloadLeft > 0) return;
                if (w.Ammo <= 0 || sprint || w.Broken) { w.BurstLeft = 0; w.Cooldown = wd.FireInterval; return; }
                w.BurstLeft--;
                FireShot(a, w, wd);
                w.Cooldown = w.BurstLeft > 0 ? wd.BurstInterval : wd.FireInterval;
                return;
            }

            trigger = wd.Auto ? cmd.Fire : edge;
            if (!trigger || sprint || w.ReloadLeft > 0 || w.Cooldown > 0) return;
            if (w.Broken)
            {
                if (edge) Info(a, $"{Items.Name(wd.Id)} сломан: почини маслом у верстака (Alt+E со стволом в руках)");
                return;
            }

            if (w.Ammo <= 0)
            {
                if (a.Count(wd.AmmoId) > 0)
                {
                    w.ReloadLeft = w.Reload;
                    Events.Add(new SimEvent { Kind = SimEventKind.Reload, ActorId = a.Id, Pos = a.Pos, Text = wd.Id });
                }
                return;
            }

            FireShot(a, w, wd);
            if (wd.Burst > 1) { w.BurstLeft = wd.Burst - 1; w.Cooldown = wd.BurstInterval; }
            else w.Cooldown = wd.FireInterval;
        }

        /// <summary>Один выстрел: патрон, износ, разброс и отдача (с обвесами), дробь — несколько пуль конусом.</summary>
        void FireShot(Actor a, WeaponState w, WeaponDef wd)
        {
            w.Ammo--;
            if (wd.MaxDurability > 0) w.Durability = MathF.Max(0, w.Durability - 1);
            // обвесы с ресурсом (кустарный глушитель — 40 выстрелов) изнашиваются и отваливаются
            for (int k = 0; k < w.Att.Length; k++)
                if (Weaponry.Att(w.Att[k]) is AttachmentDef ad && ad.MaxShots > 0 && (w.AttDur[k] -= 1) <= 0)
                {
                    Info(a, $"{Items.Name(w.Att[k])} развалился");
                    w.Att[k] = null;
                }
            float spreadDeg = CurrentSpreadDeg(a);
            w.Bloom = MathF.Min(wd.MaxBloomDeg, w.Bloom + wd.BloomPerShotDeg);
            // разброс: сумма двух случайных — чаще ближе к центру
            float r = ((float)Rng.NextDouble() + (float)Rng.NextDouble() - 1f) * 0.5f;
            float baseAng = a.Facing + (w.Recoil + r * spreadDeg) * MathF.PI / 180f;
            // увод ствола следующим выстрелам: по узору ствола, с нарастанием; в приседе меньше, на ходу больше; обвесы гасят
            if (wd.RecoilDeg > 0)
            {
                int sign = wd.RecoilPattern != null ? wd.RecoilPattern[w.Streak % wd.RecoilPattern.Length] : (Rng.Next(2) == 0 ? -1 : 1);
                float step = (wd.RecoilDeg + wd.RecoilGrowDeg * w.Streak) * sign + ((float)Rng.NextDouble() - 0.5f) * 0.8f;
                step *= (a.Crouching ? 0.7f : 1f) * (a.Vel.Length() > 0.5f ? 1.3f : 1f) * w.RecoilMul;
                w.Recoil = Math.Clamp(w.Recoil + step, -wd.RecoilMaxDeg, wd.RecoilMaxDeg);
            }
            w.Streak++;
            w.SinceShot = 0;
            var dir0 = new Vector2(MathF.Cos(baseAng), MathF.Sin(baseAng));
            var muzzle = a.Pos + dir0 * (a.Radius + 0.35f);
            bool arrow = wd.AmmoId is "arrow" or "ammo_bolt";
            PelletsFired += Math.Max(1, wd.Pellets);
            for (int p = 0; p < Math.Max(1, wd.Pellets); p++)
            {
                float ang = baseAng;
                if (wd.Pellets > 1) ang += ((float)Rng.NextDouble() + (float)Rng.NextDouble() - 1f) * 0.5f * wd.PelletSpreadDeg * MathF.PI / 180f;
                var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
                Projectiles.Add(new Projectile
                {
                    Id = NextId(), OwnerId = a.Id, Pos = muzzle, Dir = dir,
                    Speed = wd.BulletSpeed * (wd.Pellets > 1 ? 0.92f + 0.16f * (float)Rng.NextDouble() : 1f),
                    RangeLeft = wd.Range, Arrow = arrow,
                    Damage = wd.Damage * (a.Team == Team.Npc ? NpcDamageScale : 1f),
                });
            }
            a.LastShotTime = Time;
            Noises.Add(new Noise { Pos = a.Pos, Radius = w.Noise, SourceId = a.Id, Time = Time });
            Events.Add(new SimEvent { Kind = SimEventKind.Shot, ActorId = a.Id, Pos = muzzle, Dir = dir0, Text = wd.Id, Value = w.NoFlash ? 1 : 0 });
        }

        /// <summary>Удар инструментом: сначала люди в дуге перед собой, потом ресурсы.</summary>
        public const float SwingImpactDelay = 0.2f;

        /// <summary>Начало удара: анимация замаха; попадание — через SwingImpactDelay (когда инструмент опускается).</summary>
        void StartSwing(Actor a, WeaponState w)
        {
            w.Cooldown = w.Def.FireInterval;
            a.PendingSwingAt = Time + SwingImpactDelay;
            a.PendingSwing = w;
            Events.Add(new SimEvent { Kind = SimEventKind.Swing, ActorId = a.Id, Pos = a.Pos, Dir = a.FacingDir, Text = w.Def.Id });
        }

        void Swing(Actor a, WeaponState w)
        {
            var wd = w.Def;
            float halfArc = wd.MeleeArcDeg * 0.5f * MathF.PI / 180f;

            bool InArc(Vector2 p, float r)
            {
                var d = p - a.Pos;
                float dist = d.Length();
                if (dist > wd.Range + r) return false;
                if (dist < r + 0.1f) return true;
                return MathF.Abs(Geo.AngleDiff(a.Facing, MathF.Atan2(d.Y, d.X))) <= halfArc;
            }

            if (wd.Id == "tool_hammer")
            {
                var target = TargetStructure(a, wd.Range);
                if (target != null) { HammerHit(a, target); return; }
            }

            Actor victim = null;
            float best = float.MaxValue;
            foreach (var o in Actors)
            {
                if (o == a || o.Dead || o.Spectator || (a.Team == Team.Npc && o.Team == Team.Npc)) continue;
                float d = Vector2.Distance(o.Pos, a.Pos);
                if (d < best && InArc(o.Pos, o.Radius)) { best = d; victim = o; }
            }
            if (victim != null)
            {
                Damage(victim, MeleeAfterArmor(victim, a, wd.Damage), a.Id, victim.Pos, "torso");   // броня корпуса ×0,5 (D47)
                return;
            }

            ResourceNode node = null;
            best = float.MaxValue;
            foreach (var n in NodesNear(a.Pos, wd.Range + 1f))
            {
                float d = Vector2.Distance(n.Pos, a.Pos) - n.Radius;
                if (d < best && InArc(n.Pos, n.Radius)) { best = d; node = n; }
            }
            if (node == null)
            {
                // по постройке: инструментом ломаются только разметка и дерево
                var st = TargetStructure(a, wd.Range);
                if (st != null)
                {
                    float dealt = DamageStructure(st, wd.Damage * Building.MeleeMult(st.Kind, st.Tier) * 2f, a.Id);
                    Events.Add(new SimEvent { Kind = SimEventKind.HitStruct, ActorId = a.Id, OtherId = st.Id, Pos = st.Center, Value = dealt, Text = Building.TierNames[st.Tier] });
                    if (dealt <= 0) Info(a, $"{Building.KindName(st.Kind)} ({Building.TierNames[st.Tier]}) — инструментом не взять");
                }
                return;
            }

            if (node.Kind == NodeKind.Barrel)
            {
                Events.Add(new SimEvent { Kind = SimEventKind.HitNode, ActorId = a.Id, OtherId = node.Id, Pos = node.Pos });
                HitBarrel(node, wd.Damage, a.Id);
                return;
            }
            float yield = node.Kind is NodeKind.Tire or NodeKind.TireStack
                ? MathF.Min(node.Amount, wd.Id == "tool_hatchet" || wd.Id == "weapon_spear" ? 2 : 1)   // покрышку режут: топором быстрее
                : node.Kind == NodeKind.Junk
                ? MathF.Min(node.Amount, wd.Id == "tool_pickaxe" || wd.Id == "tool_hatchet" ? 2 : 1)   // хлам ковыряют: инструментом быстрее
                : MathF.Min(node.Amount, wd.Gather(node.Kind));
            int got = (int)MathF.Round(yield);
            node.Amount -= yield;
            Events.Add(new SimEvent { Kind = SimEventKind.HitNode, ActorId = a.Id, OtherId = node.Id, Pos = node.Pos });
            if (got > 0)
            {
                a.Add(node.ResourceId, got);
                // при добыче металла и серы попадается немного камня
                if (node.Kind is NodeKind.Metal or NodeKind.Sulfur) a.Add("stone", Math.Max(1, got / 3));
                if (node.Kind == NodeKind.Junk && Rng.NextDouble() < 0.18) a.Add("metal_fragments", Rng.Next(3, 8));
                if (node.Kind == NodeKind.Junk && a.Team == Team.Player) Economy.Book("сырой: куча хлама", got);
                Events.Add(new SimEvent { Kind = SimEventKind.Gather, ActorId = a.Id, OtherId = node.Id, Pos = node.Pos, Value = got, Text = Items.Name(node.ResourceId) });
            }
            if (node.Amount <= 0.5f) RemoveNode(node);
        }

        // ---------- металлолом: бочки и кучи хлама (D31) ----------

        readonly List<(NodeKind kind, Vector2 pos, float left)> _spotRespawns = new();

        /// <summary>Бочка или куча хлама на постоянном месте: после разграбления появляется там же.</summary>
        void AddScrapSpot(NodeKind kind, Vector2 p) => AddNode(kind, p);

        /// <summary>Места покрышек (РТ и мир) — чтобы вернуть их в мир из старого сохранения, где их ещё не было.</summary>
        readonly List<(NodeKind kind, Vector2 pos)> _tireSpots = new();

        void AddTireSpot(NodeKind kind, Vector2 p) { _tireSpots.Add((kind, p)); AddNode(kind, p); }

        /// <summary>
        /// Покрышки по миру (резина — отдельный компонент): у дорог и на пляже, в пустоши, на окраинах поля. Свой генератор —
        /// раскладка остального мира не сдвигается.
        /// </summary>
        void PlaceWorldTires()
        {
            var rng = new Random(Seed * 613 + 29);
            int placed = 0;
            for (int tries = 0; tries < 4000 && placed < 90; tries++)
            {
                var p = new Vector2((float)(rng.NextDouble() * 2 - 1) * HalfSize * 0.95f, (float)(rng.NextDouble() * 2 - 1) * HalfSize * 0.95f);
                if (!Terrain.IsLand(p) || NearMonument(p, 25) || !IsFree(p, 1.2f)) continue;
                var b = Terrain.BiomeAt(p);
                if (b is not (Biome.Beach or Biome.Arid or Biome.Grassland)) continue;
                AddTireSpot(rng.NextDouble() < 0.15 ? NodeKind.TireStack : NodeKind.Tire, p);
                placed++;
            }
        }

        /// <summary>Старое сохранение без покрышек: вернуть их на места.</summary>
        void EnsureTires()
        {
            foreach (var n in Nodes) if (n.Kind is NodeKind.Tire or NodeKind.TireStack) return;
            foreach (var (k, p) in _tireSpots) if (IsFree(p, 0.5f)) AddNode(k, p);
        }

        /// <summary>Переработчик на РТ: ничей, не ломается, открыт всем.</summary>
        void AddRecycler(Vector2 p)
        {
            var d = new Deployable { Id = NextId(), Kind = DeployKind.Recycler, Pos = p, Radius = 0.9f, Hp = 1e9f, On = false };
            d.Storage = new LootBag { Id = NextId(), Pos = p };
            AddDeployable(d);
        }

        /// <summary>Сейф: ящик высшего уровня под карту доступа.</summary>
        void AddSafe(Vector2 p)
        {
            AddCrate(p, 2);
            var safe = Bags[^1];
            safe.Safe = true;
            FillCrate(safe);
        }

        void RemoveNode(ResourceNode node)
        {
            Nodes.Remove(node);
            _nodeGrid.Remove(node);
            if (node.Kind is NodeKind.Barrel or NodeKind.Junk) _spotRespawns.Add((node.Kind, node.Pos, Range(180, 300)));
            else if (node.Kind is NodeKind.Tire or NodeKind.TireStack) _spotRespawns.Add((node.Kind, node.Pos, NearMonument(node.Pos, 2) ? Range(900, 1260) : Range(1200, 1800)));   // покрышки: на РТ — с обновлением РТ, в мире — 20–30 мин
            else _nodeRespawns.Add((node.Kind, Range(90, 180)));
        }

        /// <summary>Удар или пуля по бочке; разбита — под ней сумка с хламом (открывается сразу).</summary>
        void HitBarrel(ResourceNode node, float dmg, int byId)
        {
            node.Amount -= dmg;
            if (node.Amount > 0) return;
            var bag = new LootBag { Id = NextId(), Pos = node.Pos, Despawn = 120f, Src = "бочка" };
            void Put(string id, int n) { if (n > 0) bag.Items.Add(new ItemSlot(id, n)); }
            bool rich = NearMonument(node.Pos, 2);
            Put("scrap", Rng.Next(1, 3));   // D49: голого лома мало — 1–2
            if (Rng.NextDouble() < 0.35) Put(Weaponry.CommonPart(Rng), 1);                       // общий компонент
            if (Rng.NextDouble() < (rich ? 0.25 : 0.2)) Put(Economy.Tier0(Rng), 1);                // ценный лом тира 0
            if (Rng.NextDouble() < 0.35) Put("metal_fragments", Rng.Next(10, 26));
            if (Rng.NextDouble() < 0.25) Put("cloth", Rng.Next(4, 9));
            if (Rng.NextDouble() < (rich ? 0.2 : 0.1)) Put("ammo_9mm", Rng.Next(4, 9));
            if (Rng.NextDouble() < (rich ? 0.1 : 0.04)) Put("ammo_12g", Rng.Next(2, 6));
            if (Rng.NextDouble() < 0.08) Put("comp_oil", 1);
            if (Rng.NextDouble() < (rich ? 0.04 : 0.015)) Put(Rng.Next(2) == 0 ? "comp_lens" : "comp_circuit", 1);
            if (rich && Rng.NextDouble() < 0.01) bag.Items.Add(Items.NewSlot("weapon_pipeshotgun"));
            if (Rng.NextDouble() < 0.08) Put("gunpowder", Rng.Next(5, 11));
            if (Rng.NextDouble() < 0.05) Put("bandage", 1);
            Clothing.Loot(WearRng, bag, rich ? "barrel_rt" : "barrel");   // резина (покрышки), кожа, редко часы (D47)
            Bags.Add(bag);
            Events.Add(new SimEvent { Kind = SimEventKind.Destroyed, ActorId = byId, Pos = node.Pos, Text = "Бочка" });
            RemoveNode(node);
        }

        void TryPlace(Actor a, Vector2 at, ItemDef def)
        {
            if (Vector2.Distance(a.Pos, at) > PlaceRange) { Info(a, "Слишком далеко"); return; }
            float rad = def.Deploy switch { DeployKind.Claim or DeployKind.SleepBag => 0.3f, DeployKind.Box => 0.45f, _ => 0.6f };
            if (def.Deploy == DeployKind.Claim && !CanPlaceClaim(at, out string why)) { Info(a, $"Шкаф: {why}"); return; }
            var dep = new Deployable { Id = NextId(), Kind = def.Deploy, Pos = at, OwnerId = a.Id, Radius = rad, Level = Items.BenchLevel(def.Id) };
            if (def.Deploy is DeployKind.Workbench or DeployKind.Claim or DeployKind.Furnace or DeployKind.Lathe) Milestone(a, $"поставил: {def.Name}");   // столб тонкий, спальник плоский, ящик компактный
            if (def.Deploy is DeployKind.Box or DeployKind.Lathe) dep.Storage = new LootBag { Id = NextId(), Pos = at };
            if (def.Deploy == DeployKind.Lathe) { dep.On = false; dep.Radius = 0.7f; }
            dep.Auth.Add(a.Id);
            if (def.Deploy == DeployKind.Furnace && !a.IsBot) dep.On = false;   // игрок зажигает сам в окне печи
            AddDeployable(dep);
            // фундаменты без столба в его зоне — привязываются к нему
            if (def.Deploy == DeployKind.Claim)
                foreach (var s in Structures)
                    if (s.ClaimId == 0 && ClaimAt(Building.CellOf(s.Center)) == dep) s.ClaimId = dep.Id;
            var held = a.Held;
            held.Count--;
            if (held.Count <= 0) a.Belt[a.Current] = null;
            Events.Add(new SimEvent { Kind = SimEventKind.Placed, ActorId = a.Id, Pos = at, Text = def.Name });
        }

        /// <summary>Полный разброс в градусах: база + отдача + штраф за движение, в прицеливании меньше.</summary>
        public float CurrentSpreadDeg(Actor a)
        {
            var w = a.Weapon;
            if (w == null || w.Def.Melee) return 0;
            float moving = a.Vel.Length() / WalkSpeed;
            // обвесы: ЛЦУ — от бедра, приклад — на ходу, прицел — в прицеливании (D46)
            float hip = 1f + (w.HipSpreadMul - 1f) * (1f - a.Peek), ads = 1f + (w.AdsSpreadMul - 1f) * a.Peek;
            float spread = (w.Def.BaseSpreadDeg + w.Bloom) * hip + MathF.Min(1.5f, moving) * 5f * w.MoveSpreadMul;   // на ходу заметно хуже
            if (w.Def.MaxDurability > 0 && w.Durability < w.Def.MaxDurability * 0.2f) spread *= 1.25f;   // изношенный ствол бьёт хуже
            return spread * (1f - 0.35f * a.Peek) * ads * (a.Crouching ? 0.75f : 1f);   // в приседе точнее
        }

        // ---------- взаимодействие (E) ----------

        public enum InteractKind { None, Bag, Pickup, Furnace, Workbench, Door, Claim, Box, Recycler, Panel, Stash, Lathe }

        /// <summary>Что ближе всего под рукой. Используется и сервером, и подсказкой на экране.</summary>
        public (InteractKind kind, object target) NearestInteract(Actor a)
        {
            (InteractKind, object) best = (InteractKind.None, null);
            float bestD = InteractRange;
            foreach (var b in Bags)
            {
                if (b.Empty || !ClearLine(a.Pos, b.Pos, null, true)) continue;   // сквозь стены и двери не достать
                float d = Vector2.Distance(a.Pos, b.Pos);
                if (d < bestD) { bestD = d; best = (InteractKind.Bag, b); }
            }
            foreach (var p in Pickups)
            {
                if (!p.Available || !ClearLine(a.Pos, p.Pos, null, true)) continue;
                float d = Vector2.Distance(a.Pos, p.Pos);
                if (d < bestD) { bestD = d; best = (InteractKind.Pickup, p); }
            }
            foreach (var dp in DeploysNear(a.Pos, InteractRange))
            {
                if (dp.Kind == DeployKind.SleepBag || !ClearLine(a.Pos, dp.Pos, null, true)) continue;   // спальник — не для E
                float d = Vector2.Distance(a.Pos, dp.Pos) - dp.Radius + 0.3f;
                if (d < bestD) { bestD = d; best = (dp.Kind switch { DeployKind.Furnace => InteractKind.Furnace, DeployKind.Claim => InteractKind.Claim, DeployKind.Box => InteractKind.Box, DeployKind.Recycler => InteractKind.Recycler, DeployKind.Lathe => InteractKind.Lathe, _ => InteractKind.Workbench }, dp); }
            }
            foreach (var st in AllStashes())
            {
                float d = Vector2.Distance(a.Pos, st.Pos);
                if (!st.Done && d < bestD && ClearLine(a.Pos, st.Pos, null, true)) { bestD = d; best = (InteractKind.Stash, st); }
            }
            foreach (var s in Substations)
            {
                float d = Vector2.Distance(a.Pos, s.PanelPos);
                if (d < bestD && ClearLine(a.Pos, s.PanelPos, null, true)) { bestD = d; best = (InteractKind.Panel, s); }
            }
            var door = NearestDoor(a.Pos, best.Item1 == InteractKind.None ? InteractRange + 0.4f : bestD);   // дверь — если ближе остального
            if (door != null && ClearLine(a.Pos, door.Center, door, true)) best = (InteractKind.Door, door);
            return best;
        }

        /// <summary>
        /// Бот использует конкретную цель (а не «ближайшее под рукой»): шкаф, печь, ящик (deposit — сложить всё), мешок, находку.
        /// Проверка «рукой»: рядом и без стены между.
        /// </summary>
        public bool BotUse(Actor a, object target, bool deposit = false)
        {
            Vector2 pos; float r;
            switch (target)
            {
                case Deployable d when Deployables.Contains(d): pos = d.Pos; r = d.Radius; break;
                case LootBag b when Bags.Contains(b) && !b.Empty: pos = b.Pos; r = 0.3f; break;
                case Pickup p when p.Available: pos = p.Pos; r = 0.3f; break;
                default: return false;
            }
            if (Vector2.Distance(a.Pos, pos) > InteractRange + r || !ClearLine(a.Pos, pos, null, true)) return false;
            switch (target)
            {
                case Deployable d when d.Kind == DeployKind.Claim: UseClaim(a, d); break;
                case Deployable d when d.Kind == DeployKind.Furnace: UseFurnace(a, d); break;
                case Deployable d when d.Kind == DeployKind.Box:
                    bool was = a.Crouching;
                    a.Crouching = deposit;   // присед + E — сложить всё
                    UseBox(a, d);
                    a.Crouching = was;
                    break;
                case LootBag b when !b.Safe || b.Unlocked: LootBagInto(a, b); break;
                case Pickup p: Collect(a, p); break;
                default: return false;
            }
            return true;
        }

        void Interact(Actor a)
        {
            var (kind, target) = NearestInteract(a);
            switch (kind)
            {
                // у живого игрока — окна, боты пользуются быстрыми действиями
                case InteractKind.Panel: UsePanel(a, (Substation)target); break;
                case InteractKind.Stash: UseStash(a, (Stash)target); break;
                case InteractKind.Bag when ((LootBag)target).Punch && !((LootBag)target).Unlocked && Director?.Events.Find(e => e.Crate == target) is { } dev:
                    Info(a, dev.Kind == "airdrop" ? $"Груз ещё закрыт — {MathF.Ceiling(dev.StartsAt - Time)} с" : dev.StartsAt > 0 ? $"Идёт взлом — {MathF.Ceiling(dev.StartsAt - Time)} с" : "Закрытый ящик: запусти взлом (рядом, держи E)");
                    break;
                case InteractKind.Bag when ((LootBag)target).Punch && !((LootBag)target).Unlocked && Tier1.Find(r => r.Crate == target) is { } t1: UseTier1Crate(a, t1); break;
                case InteractKind.Bag when ((LootBag)target).Punch && !((LootBag)target).Unlocked: UsePunchCrate(a, (LootBag)target); break;
                case InteractKind.Bag when ((LootBag)target).Safe && !((LootBag)target).Unlocked:
                    if (a.Take("keycard", 1) > 0) { ((LootBag)target).Unlocked = true; Info(a, "Сейф открыт картой доступа"); if (!a.IsBot) OpenUi(a, UiKind.Bag, target); }
                    else Info(a, "Сейф заперт: нужна карта доступа (бывает в ящиках и у охраны)");
                    break;
                case InteractKind.Bag when !a.IsBot: OpenUi(a, UiKind.Bag, target); break;
                case InteractKind.Recycler when !a.IsBot: OpenUi(a, UiKind.Recycler, target); break;
                case InteractKind.Lathe when !a.IsBot: OpenUi(a, UiKind.Recycler, target); break;   // окно переработчика, данные станка (UiView.Machine = "lathe")
                case InteractKind.Workbench when a.Crouching && a.Weapon is WeaponState rw && !rw.Def.Melee: Repair(a, rw); break;   // Alt+E у верстака со стволом — починка маслом
                case InteractKind.Furnace when !a.IsBot: OpenUi(a, UiKind.Furnace, target); break;
                case InteractKind.Claim when !a.IsBot: OpenUi(a, UiKind.Claim, target); break;
                case InteractKind.Box when !a.IsBot: OpenUi(a, UiKind.Box, target); break;
                case InteractKind.Workbench when !a.IsBot: a.WantsCraftMenu = true; break;
                case InteractKind.Bag: LootBagInto(a, (LootBag)target); break;
                case InteractKind.Pickup: Collect(a, (Pickup)target); break;
                case InteractKind.Furnace: UseFurnace(a, (Deployable)target); break;
                case InteractKind.Door: ToggleDoor(a, (Structure)target); break;
                case InteractKind.Claim: UseClaim(a, (Deployable)target); break;
                case InteractKind.Box: UseBox(a, (Deployable)target); break;
                case InteractKind.Workbench: Info(a, "Верстак: рядом с ним доступны рецепты уровня 1 (Q)"); break;
            }
        }

        /// <summary>Бот берёт из своего ящика только нужное (рукой: рядом и без стены между).</summary>
        public bool BotTake(Actor a, Deployable box, string id, int n)
        {
            if (box.Storage == null || Vector2.Distance(a.Pos, box.Pos) > InteractRange) { LastInfo[a.Id] = $"ящик далеко {Vector2.Distance(a.Pos, box.Pos):0.0} м"; return false; }
            if (!ClearLine(a.Pos, box.Pos, null, true)) { LastInfo[a.Id] = "ящик за стеной"; return false; }
            int freePack = 0;
            foreach (var p in a.Pack) if (p == null) freePack++;
            if (freePack == 0 && a.Count(id) == 0)
            {
                var kinds = new List<string>();
                foreach (var p in a.Pack) if (p != null && !kinds.Contains(p.Id)) kinds.Add(p.Id);
                LastInfo[a.Id] = $"рюкзак полон: {string.Join(",", kinds)} · в ящике {box.Storage.Items.Count}/{BoxSlots}";
            }
            int got = 0;
            for (int i = box.Storage.Items.Count - 1; i >= 0 && got < n; i--)
            {
                var s = box.Storage.Items[i];
                if (s.Id != id || s.State != null) continue;
                int take = Math.Min(n - got, s.Count);
                int put = a.AddUpTo(id, take);
                got += put;
                s.Count -= put;
                if (s.Count <= 0) box.Storage.Items.RemoveAt(i);
                if (put < take) break;
            }
            return got > 0;
        }

        /// <summary>Ящик: E — забрать всё, Alt+E (присед) — сложить весь инвентарь (пояс остаётся при себе).</summary>
        void UseBox(Actor a, Deployable box)
        {
            if (a.Crouching)
            {
                var parts = new List<string>();
                for (int i = 0; i < Actor.PackSize; i++)
                {
                    var st = a.Pack[i];
                    if (st == null || st.Id.StartsWith("key_")) continue;   // ключи остаются при себе
                    var same = box.Storage.Items.Find(s => s.Id == st.Id && s.State == null);
                    if (same != null && st.State == null) same.Count += st.Count; else box.Storage.Items.Add(st);
                    parts.Add($"{Items.Name(st.Id)} ×{st.Count}");
                    a.Pack[i] = null;
                }
                if (parts.Count == 0) { Info(a, "Рюкзак пуст — класть нечего"); return; }
                Info(a, parts.Count <= 3 ? "В ящик: " + string.Join(", ", parts) : $"В ящик: {string.Join(", ", parts.GetRange(0, 3))} и ещё {parts.Count - 3}");
                return;
            }
            if (box.Storage.Empty) { Info(a, "Ящик пуст · Alt+E — сложить инвентарь"); return; }
            LootBagInto(a, box.Storage);
        }

        void LootBagInto(Actor a, LootBag bag)
        {
            var parts = new List<string>();
            for (int i = bag.Items.Count - 1; i >= 0; i--)
            {
                var s = bag.Items[i];
                var def = s.Def;
                if (def != null && def.Holdable)
                {
                    // такое же оружие уже есть — забираем только патроны из магазина
                    if (s.State != null && FindBelt(a, s.Id) != null)
                    {
                        if (s.State.Ammo > 0) { a.Add(s.State.Def.AmmoId, s.State.Ammo); parts.Add($"{Items.Name(s.State.Def.AmmoId)} ×{s.State.Ammo}"); }
                    }
                    else if (!a.AddSlot(s)) continue;     // пояс полон — остаётся в сумке
                    else parts.Add(def.Name);
                }
                else
                {
                    // сколько влезет; остальное остаётся в сумке (иначе бот бесконечно перекладывал бы сумки)
                    int put = a.AddUpTo(s.Id, s.Count);
                    if (put <= 0) continue;
                    if (a.Team == Team.Player) Economy.BookTake(bag.Source, s.Id, put);
                    parts.Add($"{Items.Name(s.Id)} ×{put}");
                    s.Count -= put;
                    if (s.Count > 0) continue;
                }
                bag.Items.RemoveAt(i);
            }
            if (bag.IsCrate && bag.Empty) bag.RespawnLeft = CrateRespawnTime(bag);
            Events.Add(new SimEvent { Kind = SimEventKind.Loot, ActorId = a.Id, Pos = bag.Pos, Text = parts.Count > 0 ? string.Join(", ", parts) : "пояс полон" });
        }

        static ItemSlot FindBelt(Actor a, string id)
        {
            foreach (var s in a.Belt) if (s != null && s.Id == id) return s;
            return null;
        }

        void Collect(Actor a, Pickup p)
        {
            switch (p.Kind)
            {
                case PickupKind.Hemp:
                    a.Add("cloth", 10);
                    p.RespawnLeft = 120;
                    Events.Add(new SimEvent { Kind = SimEventKind.Gather, ActorId = a.Id, Pos = p.Pos, Value = 10, Text = "Ткань" });
                    break;
                case PickupKind.Berries:
                    a.Add("berries", 3);
                    p.RespawnLeft = 90;
                    Events.Add(new SimEvent { Kind = SimEventKind.Gather, ActorId = a.Id, Pos = p.Pos, Value = 3, Text = "Ягоды" });
                    break;
                case PickupKind.WaterBarrel:
                    a.Water = MathF.Min(100, a.Water + 40);
                    Info(a, "Напился воды");
                    break;
                case PickupKind.LocalItem:
                    a.Add(p.Item, 1);
                    p.RespawnLeft = 1e9f;
                    Events.Add(new SimEvent { Kind = SimEventKind.Gather, ActorId = a.Id, Pos = p.Pos, Value = 1, Text = Items.Name(p.Item) });
                    Info(a, $"{Items.Name(p.Item)} у тебя ({a.Count(p.Item)})");
                    break;
                case PickupKind.PunchCard:
                    a.Add("punchcard_1", 1);
                    p.RespawnLeft = 1e9f;   // вернётся при обновлении РТ
                    Events.Add(new SimEvent { Kind = SimEventKind.Gather, ActorId = a.Id, Pos = p.Pos, Value = 1, Text = "Перфокарта I" });
                    Info(a, "Перфокарта I у тебя — ей открывается крейт Подстанции");
                    break;
                case PickupKind.Fuse:
                    a.Add("fuse", 1);
                    p.RespawnLeft = 1e9f;   // вернётся при обновлении РТ или если пробку вынесут за забор
                    Events.Add(new SimEvent { Kind = SimEventKind.Gather, ActorId = a.Id, Pos = p.Pos, Value = 1, Text = "Пробка" });
                    Info(a, $"Пробка у тебя ({a.Count("fuse")}). Вставь в главный щиток в щитовой");
                    break;
            }
        }

        /// <summary>Печь: забираешь готовое, потом загружаешь руду и дерево из инвентаря.</summary>
        void UseFurnace(Actor a, Deployable f)
        {
            f.On = true;
            var got = new List<string>();
            if (f.OutMetal > 0) { a.Add("metal_fragments", f.OutMetal); got.Add($"Металл ×{f.OutMetal}"); f.OutMetal = 0; }
            if (f.OutSulfur > 0) { a.Add("sulfur", f.OutSulfur); got.Add($"Сера ×{f.OutSulfur}"); f.OutSulfur = 0; }
            if (f.OutCharcoal > 0) { a.Add("charcoal", f.OutCharcoal); got.Add($"Уголь ×{f.OutCharcoal}"); f.OutCharcoal = 0; }

            int ore = a.Take("metal_ore", int.MaxValue);
            int sulfur = a.Take("sulfur_ore", int.MaxValue);
            f.Ore += ore;
            f.SulfurOre += sulfur;
            // дерева берём столько, сколько нужно на загруженную руду (+ запас), а не всё
            float need = (f.Ore + f.SulfurOre) * 0.5f + 10 - f.Fuel;
            int wood = need > 0 ? a.Take("wood", (int)MathF.Ceiling(need)) : 0;
            f.Fuel += wood;

            var put = new List<string>();
            if (ore > 0) put.Add($"руда ×{ore}");
            if (sulfur > 0) put.Add($"сера ×{sulfur}");
            if (wood > 0) put.Add($"дерево ×{wood}");
            string text = got.Count > 0 ? "Забрано: " + string.Join(", ", got) : "";
            if (put.Count > 0) text += (text.Length > 0 ? " · " : "") + "В печь: " + string.Join(", ", put);
            if (text.Length == 0) text = f.Burning ? "Печь работает" : "Печь пуста: нужна руда и дерево";
            Info(a, text);
        }

        void StepFurnaces()
        {
            foreach (var f in Deployables)
            {
                if (f.Kind != DeployKind.Furnace || !f.Burning) continue;
                f.SmeltTimer += Dt;
                while (f.SmeltTimer >= 0.4f && f.Burning)
                {
                    f.SmeltTimer -= 0.4f;
                    if (f.Ore > 0) { f.Ore--; f.OutMetal++; }
                    else { f.SulfurOre--; f.OutSulfur++; }
                    f.Fuel -= 0.5f;
                    f.CharcoalAcc += 0.5f;
                    if (f.CharcoalAcc >= 1) { f.CharcoalAcc -= 1; f.OutCharcoal++; }
                }
                if (f.Fuel < 0) f.Fuel = 0;
            }
        }

        void StepPickupsAndNodes()
        {
            foreach (var p in Pickups)
                if (p.RespawnLeft > 0) p.RespawnLeft -= Dt;
            for (int i = _spotRespawns.Count - 1; i >= 0; i--)
            {
                var (kind, pos, left) = _spotRespawns[i];
                left -= Dt;
                if (left <= 0 && IsFree(pos, 0.8f)) { AddNode(kind, pos); _spotRespawns.RemoveAt(i); }
                else _spotRespawns[i] = (kind, pos, left);
            }
            for (int i = _nodeRespawns.Count - 1; i >= 0; i--)
            {
                var (kind, left) = _nodeRespawns[i];
                left -= Dt;
                if (left <= 0)
                {
                    AddNode(kind, RandomFreePoint(1.5f, kind));
                    _nodeRespawns.RemoveAt(i);
                }
                else _nodeRespawns[i] = (kind, left);
            }
        }

        // ---------- бой ----------

        /// <summary>Наблюдатель: летит по WASD (Shift — быстрее) или висит над тем, за кем следит.</summary>
        void StepSpectator(Actor a, InputCmd cmd)
        {
            a.Dead = false; a.Hp = a.MaxHp; a.Food = a.Water = 100;
            if (cmd.Follow != 0 && Find(cmd.Follow) is Actor t) { a.Pos = t.Pos; a.Facing = t.Facing; return; }
            var move = cmd.Move;
            if (move.LengthSquared() > 1) move = Vector2.Normalize(move);
            a.Pos += move * (cmd.Sprint ? 70f : 28f) * Dt;
            a.Pos = Vector2.Clamp(a.Pos, new Vector2(-HalfSize), new Vector2(HalfSize));
        }

        void SeparateActors()
        {
            for (int i = 0; i < Actors.Count; i++)
            {
                var a = Actors[i];
                if (a.Dead || a.Spectator) continue;
                for (int j = i + 1; j < Actors.Count; j++)
                {
                    var b = Actors[j];
                    if (b.Dead || b.Spectator) continue;
                    var d = b.Pos - a.Pos;
                    float min = a.Radius + b.Radius;
                    float dist = d.Length();
                    if (dist >= min || dist < 1e-5f) continue;
                    var push = d / dist * (min - dist) * 0.5f;
                    a.Pos -= push;
                    b.Pos += push;
                }
            }
        }

        void StepProjectiles()
        {
            for (int i = Projectiles.Count - 1; i >= 0; i--)
            {
                var p = Projectiles[i];
                float stepLen = MathF.Min(p.Speed * Dt, p.RangeLeft);
                var a = p.Pos;
                var b = p.Pos + p.Dir * stepLen;
                var owner = Find(p.OwnerId);

                float bestT = 2;
                Actor hitActor = null;
                MapWall hitWall = null;
                ResourceNode hitNode = null;
                foreach (var w in WallsAlong(a, b))
                    if (!w.PassBullets && Geo.SegmentAabb(a, b, w.Wall.Min, w.Wall.Max, out float t) && t < bestT) { bestT = t; hitWall = w; }
                foreach (var n in NodesAlong(a, b))
                    if (Geo.SegmentCircle(a, b, n.Pos, n.Radius, out float t) && t < bestT) { bestT = t; hitWall = null; hitNode = n; }
                foreach (var d in DeploysAlong(a, b))
                    if (Geo.SegmentCircle(a, b, d.Pos, d.Radius, out float t) && t < bestT) { bestT = t; hitWall = null; hitNode = null; }
                foreach (var act in Actors)
                {
                    if (act.Dead || act.Spectator || act.Id == p.OwnerId) continue;
                    if (owner != null && owner.Team == Team.Npc && act.Team == Team.Npc) continue;
                    if (Geo.SegmentCircle(a, b, act.Pos, act.Radius, out float t) && t < bestT)
                    {
                        bestT = t;
                        hitActor = act;
                        hitNode = null;
                    }
                }

                if (bestT <= 1)
                {
                    var hitPos = a + (b - a) * bestT;
                    if (hitActor != null) BulletHit(hitActor, p.Pos, p.Dir, p.Damage, p.OwnerId, hitPos);   // зоны и броня (D47, Armor.cs)
                    else
                    {
                        // пули ломают двери и разметку (D22), стены — нет
                        if (hitWall?.Owner is Structure st) DamageStructure(st, p.Damage * Building.BulletMult(st.Kind, st.Tier), p.OwnerId);
                        if (hitNode != null && hitNode.Kind == NodeKind.Barrel && Nodes.Contains(hitNode)) HitBarrel(hitNode, p.Damage, p.OwnerId);
                        Events.Add(new SimEvent { Kind = SimEventKind.HitWall, Pos = hitPos, Dir = p.Dir, OtherId = p.OwnerId });
                    }
                    Projectiles.RemoveAt(i);
                    continue;
                }

                p.Pos = b;
                p.RangeLeft -= stepLen;
                if (p.RangeLeft <= 0) Projectiles.RemoveAt(i);
            }
        }

        /// <param name="zone">куда попали (для звука и маркера): head, helmet, torso, plate, limb; null — без зоны (взрыв, огонь)</param>
        public void Damage(Actor target, float dmg, int attackerId, Vector2 pos, string zone = null)
        {
            if (target.Spectator) return;
            target.Hp -= dmg;
            target.LastAttacker = attackerId;
            target.LastDamagedTime = Time;
            Events.Add(new SimEvent { Kind = SimEventKind.HitActor, ActorId = target.Id, OtherId = attackerId, Pos = pos, Value = dmg, Text = zone });
            if (target.Hp <= 0) Kill(target, attackerId);
        }

        void Kill(Actor a, int killerId)
        {
            a.Hp = 0;
            a.Dead = true;
            a.DeadTime = Time;
            var bag = new LootBag { Id = NextId(), Pos = a.Pos, Despawn = 300f, Corpse = true };
            if (a.Team == Team.Npc) NpcDrop(a, bag);   // охрана РТ — не фулл-лут
            else
            {
                foreach (var s in a.Pack) if (s != null) bag.Items.Add(s);
                foreach (var s in a.Belt)
                    if (s != null)
                    {
                        if (s.State != null) s.State.ReloadLeft = 0;
                        bag.Items.Add(s);
                    }
                foreach (var s in a.Worn) if (s != null) bag.Items.Add(s);     // одежда и пластины — в рюкзак трупа (D47)
                foreach (var s in a.Plates) if (s != null) bag.Items.Add(s);
            }
            a.ClearAll();
            if (!bag.Empty) Bags.Add(bag);
            Events.Add(new SimEvent { Kind = SimEventKind.Death, ActorId = a.Id, OtherId = killerId, Pos = a.Pos });
        }

        /// <summary>
        /// Что падает с охраны РТ (D35): не весь обвес. Оружие — никогда, патроны — часть, лом и карта — как есть,
        /// бинт и еда — иногда. Иначе с одного РТ выносят по 4–5 калашей.
        /// </summary>
        void NpcDrop(Actor a, LootBag bag)
        {
            void Put(string id, int n) { if (n > 0) bag.Items.Add(new ItemSlot(id, n)); }
            // ствол — редко и только не выше тира 2 (самопал/наган у охраны тира 1); калаши со склада не падают
            foreach (var s in a.Belt)
                if (s?.State is WeaponState ws && !ws.Def.Melee && ws.Def.Tier is >= 1 and <= 2 && Rng.NextDouble() < 0.1)
                {
                    ws.Durability = ws.Def.MaxDurability * (0.2f + 0.4f * (float)Rng.NextDouble());   // потрёпанный
                    ws.ReloadLeft = 0;
                    bag.Items.Add(s);
                    break;
                }
            foreach (var ammo in new[] { "ammo_762", "ammo_9mm", "ammo_12g" })
                if (a.Count(ammo) > 0 && Rng.NextDouble() < 0.6) Put(ammo, Math.Min(a.Count(ammo), Rng.Next(5, 16)));
            Put("scrap", a.Count("scrap"));
            Put("keycard", a.Count("keycard"));
            if (Rng.NextDouble() < 0.5) Put("bandage", a.Count("bandage"));
            if (Rng.NextDouble() < 0.5) Put("canned", a.Count("canned"));
            // одежда охраны: каждая вещь — с шансом 25% (свой ГСЧ: основной поток не сдвигается)
            foreach (var s in a.Worn) if (s != null && WearRng.NextDouble() < 0.25) bag.Items.Add(new ItemSlot(s.Id, 1));
        }

        void StepBags()
        {
            for (int i = Bags.Count - 1; i >= 0; i--)
            {
                var b = Bags[i];
                if (b.IsCrate)
                {
                    if (b.Punch) continue;   // наполняется при обновлении своей РТ
                    if (b.Empty)
                    {
                        b.RespawnLeft -= Dt;
                        if (b.RespawnLeft <= 0) FillCrate(b);
                    }
                }
                else
                {
                    b.Despawn -= Dt;
                    if (b.Despawn <= 0 || b.Empty) Bags.RemoveAt(i);
                }
            }
        }
    }
}
