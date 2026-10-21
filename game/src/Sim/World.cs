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
            var w = new MapWall { Wall = new Wall(min, max, h), Kind = kind };
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

            foreach (var p in new[] { new Vector2(15, -24), new Vector2(28, -30), new Vector2(29, -10),
                                      new Vector2(10, -15), new Vector2(22, -20), new Vector2(20, 2) })
                NpcHomes[-NpcHomes.Count - 1] = p;   // ключи временные, заменяются при спавне

            // малые РТ по острову
            PlaceSmallMonuments();

            // раскладка по биомам: плотность и что встречается
            ScatterByBiome();

            // спавны — на пляжах
            for (int tries = 0; tries < 20000 && PlayerSpawns.Count < 16; tries++)
            {
                var p = new Vector2(Range(-HalfSize, HalfSize), Range(-HalfSize, HalfSize));
                if (Terrain.BiomeAt(p) == Biome.Beach && IsFree(p, 1.5f)) PlayerSpawns.Add(p);
            }
            if (PlayerSpawns.Count == 0) PlayerSpawns.Add(new Vector2(0, 40));
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
                    int r7 = Rng.Next(3);
                    AddProp(r7 == 0 ? WallKind.WoodCrate : r7 == 1 ? WallKind.Barrel : WallKind.Junk, p);
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
                _ => 60,
            };
            var node = new ResourceNode
            {
                Id = NextId(), Kind = kind, Pos = p,
                Radius = kind == NodeKind.Tree ? Range(0.35f, 0.5f) : 0.7f,
                Amount = amount, MaxAmount = amount,
            };
            Nodes.Add(node);
            _nodeGrid.Insert(node, p - new Vector2(node.Radius), p + new Vector2(node.Radius));
        }

        void AddPickup(PickupKind kind, Vector2 p) => Pickups.Add(new Pickup { Id = NextId(), Kind = kind, Pos = p });

        void AddCrate(Vector2 pos)
        {
            var bag = new LootBag { Id = NextId(), Pos = pos, IsCrate = true };
            FillCrate(bag);
            Bags.Add(bag);
        }

        void FillCrate(LootBag bag)
        {
            bag.Items.Clear();
            void Maybe(double chance, string id, int min, int max)
            {
                if (Rng.NextDouble() < chance) bag.Items.Add(new ItemSlot(id, Rng.Next(min, max + 1)));
            }
            Maybe(0.4, "ammo_rifle", 15, 40);
            Maybe(0.5, "ammo_pistol", 10, 30);
            Maybe(0.8, "scrap", 10, 30);
            Maybe(0.4, "bandage", 1, 3);
            Maybe(0.5, "metal_fragments", 20, 60);
            Maybe(0.35, "gunpowder", 10, 30);
            Maybe(0.4, "canned", 1, 2);
            Maybe(0.4, "water_bottle", 1, 2);
            Maybe(0.3, "cloth", 10, 30);
            if (Rng.NextDouble() < 0.12) bag.Items.Add(Items.NewSlot("weapon_pipe"));
            if (Rng.NextDouble() < 0.08) bag.Items.Add(Items.NewSlot("weapon_ak"));
            if (bag.Empty) bag.Items.Add(new ItemSlot("scrap", 10));
        }

        // ---------- актёры ----------

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

        public void RespawnPlayer(Actor a, bool inBag = false)
        {
            var bag = inBag ? ReadyBag(a) : null;
            if (bag != null && Time < bag.ReadyAt) bag = null;
            a.Pos = bag != null ? bag.Pos + new Vector2(0.9f, 0) : PlayerSpawns[Rng.Next(PlayerSpawns.Count)];
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
            a.AddSlot(Items.NewSlot("weapon_ak", 30));
            a.Add("ammo_rifle", Rng.Next(30, 75));
            a.Add("scrap", Rng.Next(5, 20));
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
            a.AddSlot(Items.NewSlot("weapon_ak", 30));
            a.AddSlot(Items.NewSlot("build_plan"));
            a.AddSlot(Items.NewSlot("tool_hammer"));
            a.AddSlot(Items.NewSlot("deploy_claim"));
            a.AddSlot(Items.NewSlot("weapon_torch"));
            a.Add("expl_c4", 6);
            a.Add("expl_satchel", 10);
            a.Add("ammo_rifle", 120);
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
            foreach (var w in WallsAlong(a, b))
                if (w.Wall.BlocksVision && Geo.SegmentAabb(a, b, w.Wall.Min, w.Wall.Max, out _)) return false;
            foreach (var n in NodesAlong(a, b))
                if (n.BlocksVision && Geo.SegmentCircle(a, b, n.Pos, n.Radius * 0.9f, out _)) return false;
            return true;
        }

        /// <summary>Обзор персонажа: прицеливание увеличивает дальность и сужает конус.</summary>
        public float ViewRange(Actor a) => (50f + 30f * a.Peek) * (1f - 0.4f * NightFactorAt(TimeOfDay) * (a.Held?.Id == "weapon_torch" ? 0.35f : 1f));   // факел почти снимает ночной штраф

        // ---------- время суток ----------
        public const float DayLength = 1200f;   // 20 минут на сутки
        /// <summary>0..1: 0 — полночь, 0.25 — рассвет, 0.5 — полдень, 0.75 — закат. Вайп начинается утром.</summary>
        public float TimeOfDay => ((Time + TimeOffset) / DayLength + 0.36f) % 1f;
        public float TimeOffset;

        /// <summary>Отладка (клавиша N): день ↔ ночь — прыжок к полудню или полуночи.</summary>
        void ToggleDayNight()
        {
            float target = NightFactorAt(TimeOfDay) > 0.5f ? 0.5f : 0.0f;
            TimeOffset += ((target - TimeOfDay + 1f) % 1f) * DayLength;
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

        public bool NearBench(Vector2 p)
        {
            foreach (var d in DeploysNear(p, BenchRange))
                if (d.Kind == DeployKind.Workbench && Vector2.Distance(d.Pos, p) < BenchRange) return true;
            return false;
        }

        public bool CanCraft(Actor a, Recipe r)
        {
            if (r.Bench > 0 && !NearBench(a.Pos)) return false;
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

            foreach (var a in Actors)
            {
                if (!inputs.TryGetValue(a.Id, out var cmd)) cmd = InputCmd.Empty;
                if (a.Dead)
                {
                    if (a.Team == Team.Npc && Time - a.DeadTime > NpcRespawnTime) RespawnNpc(a);
                    else if (a.Team == Team.Player && (cmd.Respawn || cmd.RespawnBag)) RespawnPlayer(a, cmd.RespawnBag);
                    continue;
                }
                StepActor(a, cmd);
            }

            SeparateActors();
            StepProjectiles();
            StepBags();
            StepPickupsAndNodes();
            StepFurnaces();
            StepClaims();
            StepThrowns();
            StepFires();
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
            a.Vel = Geo.MoveToward(a.Vel, move * speed, Accel * Dt);
            var before = a.Pos;
            a.Pos += a.Vel * Dt;
            ResolveCollisions(a);
            a.Swimming = !Terrain.IsLand(a.Pos);

            // взгляд
            if (toAimDir.LengthSquared() > 0.04f) a.Facing = MathF.Atan2(toAimDir.Y, toAimDir.X);

            StepSurvival(a, sprint);

            // лечение и еда
            a.HealCooldown -= Dt;
            if (cmd.Heal && a.HealCooldown <= 0 && a.Hp < a.MaxHp && a.Take("bandage", 1) > 0)
            {
                a.Hp = MathF.Min(a.MaxHp, a.Hp + 25);
                a.HealCooldown = 2f;
            }
            if (cmd.Eat) Eat(a);
            if (cmd.DebugKit && a.Team == Team.Player) GiveDebugKit(a);
            if (cmd.ToggleTime && a.Team == Team.Player) ToggleDayNight();

            if (cmd.Craft >= 0 && cmd.Craft < Recipes.All.Count) QueueCraft(a, Recipes.All[cmd.Craft]);
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
            if (a.Food > 60 && a.Water > 60) a.Hp = MathF.Min(a.MaxHp, a.Hp + 0.25f * Dt);
            float starving = (a.Food <= 0 ? 0.35f : 0) + (a.Water <= 0 ? 0.35f : 0);
            if (starving > 0)
            {
                a.Hp -= starving * Dt;
                if (a.Hp <= 0) Kill(a, -1);
            }
        }

        void Eat(Actor a)
        {
            // выбираем еду, которая лучше всего закрывает текущий голод и жажду
            string best = null;
            float bestScore = 0;
            foreach (var s in a.Inventory)
            {
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
            if (r.Bench > 0 && !NearBench(a.Pos)) { Info(a, "Нужен верстак рядом"); return; }
            if (!CanCraft(a, r)) { Info(a, "Не хватает ресурсов"); return; }
            foreach (var (id, n) in r.Cost) a.Take(id, n);   // как в Расте: ресурсы списываются сразу
            a.CraftQueue.Add(new CraftJob { Recipe = r, Left = r.Time });
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
                if (cmd.Reload) a.BuildPiece = (StructKind)(((int)a.BuildPiece + 1) % 5);
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
                    w.Ammo += a.Take(wd.AmmoId, wd.Magazine - w.Ammo);
                }
            }
            else if (cmd.Reload && w.Ammo < wd.Magazine && a.Count(wd.AmmoId) > 0)
            {
                w.ReloadLeft = wd.ReloadTime;
                Events.Add(new SimEvent { Kind = SimEventKind.Reload, ActorId = a.Id, Pos = a.Pos, Text = wd.Id });
            }

            trigger = wd.Auto ? cmd.Fire : cmd.Fire && !a.FireHeldLastTick;
            a.FireHeldLastTick = cmd.Fire;
            if (!trigger || sprint || w.ReloadLeft > 0 || w.Cooldown > 0) return;

            if (w.Ammo <= 0)
            {
                if (a.Count(wd.AmmoId) > 0)
                {
                    w.ReloadLeft = wd.ReloadTime;
                    Events.Add(new SimEvent { Kind = SimEventKind.Reload, ActorId = a.Id, Pos = a.Pos, Text = wd.Id });
                }
                return;
            }

            w.Ammo--;
            w.Cooldown = wd.FireInterval;
            float spreadDeg = CurrentSpreadDeg(a);
            w.Bloom = MathF.Min(wd.MaxBloomDeg, w.Bloom + wd.BloomPerShotDeg);
            // разброс: сумма двух случайных — чаще ближе к центру
            float r = ((float)Rng.NextDouble() + (float)Rng.NextDouble() - 1f) * 0.5f;
            float ang = a.Facing + (w.Recoil + r * spreadDeg) * MathF.PI / 180f;
            // увод ствола следующим выстрелам: по узору ствола, с нарастанием; в приседе меньше, на ходу больше
            if (wd.RecoilDeg > 0)
            {
                int sign = wd.RecoilPattern != null ? wd.RecoilPattern[w.Streak % wd.RecoilPattern.Length] : (Rng.Next(2) == 0 ? -1 : 1);
                float step = (wd.RecoilDeg + wd.RecoilGrowDeg * w.Streak) * sign + ((float)Rng.NextDouble() - 0.5f) * 0.8f;
                step *= (a.Crouching ? 0.7f : 1f) * (a.Vel.Length() > 0.5f ? 1.3f : 1f);
                w.Recoil = Math.Clamp(w.Recoil + step, -wd.RecoilMaxDeg, wd.RecoilMaxDeg);
            }
            w.Streak++;
            w.SinceShot = 0;
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            var muzzle = a.Pos + dir * (a.Radius + 0.35f);
            Projectiles.Add(new Projectile
            {
                Id = NextId(), OwnerId = a.Id, Pos = muzzle, Dir = dir,
                Speed = wd.BulletSpeed, RangeLeft = wd.Range, Arrow = wd.AmmoId == "arrow",
                Damage = wd.Damage * (a.Team == Team.Npc ? NpcDamageScale : 1f),
            });
            a.LastShotTime = Time;
            Noises.Add(new Noise { Pos = a.Pos, Radius = wd.NoiseRadius, SourceId = a.Id, Time = Time });
            Events.Add(new SimEvent { Kind = SimEventKind.Shot, ActorId = a.Id, Pos = muzzle, Dir = dir, Text = wd.Id });
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
                if (o == a || o.Dead || (a.Team == Team.Npc && o.Team == Team.Npc)) continue;
                float d = Vector2.Distance(o.Pos, a.Pos);
                if (d < best && InArc(o.Pos, o.Radius)) { best = d; victim = o; }
            }
            if (victim != null)
            {
                Damage(victim, wd.Damage, a.Id, victim.Pos);
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

            float yield = MathF.Min(node.Amount, wd.Gather(node.Kind));
            int got = (int)MathF.Round(yield);
            node.Amount -= yield;
            Events.Add(new SimEvent { Kind = SimEventKind.HitNode, ActorId = a.Id, OtherId = node.Id, Pos = node.Pos });
            if (got > 0)
            {
                a.Add(node.ResourceId, got);
                // при добыче металла и серы попадается немного камня
                if (node.Kind is NodeKind.Metal or NodeKind.Sulfur) a.Add("stone", Math.Max(1, got / 3));
                Events.Add(new SimEvent { Kind = SimEventKind.Gather, ActorId = a.Id, OtherId = node.Id, Pos = node.Pos, Value = got, Text = Items.Name(node.ResourceId) });
            }
            if (node.Amount <= 0.5f)
            {
                Nodes.Remove(node);
                _nodeGrid.Remove(node);
                _nodeRespawns.Add((node.Kind, Range(90, 180)));
            }
        }

        void TryPlace(Actor a, Vector2 at, ItemDef def)
        {
            if (Vector2.Distance(a.Pos, at) > PlaceRange) { Info(a, "Слишком далеко"); return; }
            if (!IsFree(at, 0.65f)) { Info(a, "Здесь не поставить"); return; }
            if (def.Deploy == DeployKind.Claim && !CanPlaceClaim(at, out string why)) { Info(a, $"Столб: {why}"); return; }
            var dep = new Deployable { Id = NextId(), Kind = def.Deploy, Pos = at, OwnerId = a.Id, Radius = def.Deploy is DeployKind.Claim or DeployKind.SleepBag ? 0.3f : 0.6f };   // столб тонкий, спальник плоский
            if (def.Deploy == DeployKind.Box) dep.Storage = new LootBag { Id = NextId(), Pos = at };
            dep.Auth.Add(a.Id);
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
            float spread = w.Def.BaseSpreadDeg + w.Bloom + MathF.Min(1.5f, moving) * 5f;   // на ходу заметно хуже
            return spread * (1f - 0.35f * a.Peek) * (a.Crouching ? 0.75f : 1f);   // в приседе точнее
        }

        // ---------- взаимодействие (E) ----------

        public enum InteractKind { None, Bag, Pickup, Furnace, Workbench, Door, Claim, Box }

        /// <summary>Что ближе всего под рукой. Используется и сервером, и подсказкой на экране.</summary>
        public (InteractKind kind, object target) NearestInteract(Actor a)
        {
            (InteractKind, object) best = (InteractKind.None, null);
            float bestD = InteractRange;
            foreach (var b in Bags)
            {
                if (b.Empty) continue;
                float d = Vector2.Distance(a.Pos, b.Pos);
                if (d < bestD) { bestD = d; best = (InteractKind.Bag, b); }
            }
            foreach (var p in Pickups)
            {
                if (!p.Available) continue;
                float d = Vector2.Distance(a.Pos, p.Pos);
                if (d < bestD) { bestD = d; best = (InteractKind.Pickup, p); }
            }
            foreach (var dp in DeploysNear(a.Pos, InteractRange))
            {
                float d = Vector2.Distance(a.Pos, dp.Pos) - dp.Radius + 0.3f;
                if (d < bestD) { bestD = d; best = (dp.Kind switch { DeployKind.Furnace => InteractKind.Furnace, DeployKind.Claim => InteractKind.Claim, DeployKind.Box => InteractKind.Box, _ => InteractKind.Workbench }, dp); }
            }
            var door = NearestDoor(a.Pos, best.Item1 == InteractKind.None ? InteractRange + 0.4f : bestD);   // дверь — если ближе остального
            if (door != null) best = (InteractKind.Door, door);
            return best;
        }

        void Interact(Actor a)
        {
            var (kind, target) = NearestInteract(a);
            switch (kind)
            {
                case InteractKind.Bag: LootBagInto(a, (LootBag)target); break;
                case InteractKind.Pickup: Collect(a, (Pickup)target); break;
                case InteractKind.Furnace: UseFurnace(a, (Deployable)target); break;
                case InteractKind.Door: ToggleDoor(a, (Structure)target); break;
                case InteractKind.Claim: UseClaim(a, (Deployable)target); break;
                case InteractKind.Box: UseBox(a, (Deployable)target); break;
                case InteractKind.Workbench: Info(a, "Верстак: рядом с ним доступны рецепты уровня 1 (Q)"); break;
            }
        }

        /// <summary>Ящик: E — забрать всё, Alt+E (присед) — сложить весь инвентарь (пояс остаётся при себе).</summary>
        void UseBox(Actor a, Deployable box)
        {
            if (a.Crouching)
            {
                if (a.Inventory.Count == 0) { Info(a, "Инвентарь пуст — класть нечего"); return; }
                var parts = new List<string>();
                foreach (var st in a.Inventory)
                {
                    if (st.Id.StartsWith("key_")) continue;   // ключи остаются при себе
                    var same = box.Storage.Items.Find(s => s.Id == st.Id && s.State == null);
                    if (same != null) same.Count += st.Count; else box.Storage.Items.Add(new ItemSlot(st.Id, st.Count));
                    parts.Add($"{Items.Name(st.Id)} ×{st.Count}");
                }
                a.Inventory.RemoveAll(s => !s.Id.StartsWith("key_"));
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
                    a.Add(s.Id, s.Count);
                    parts.Add($"{Items.Name(s.Id)} ×{s.Count}");
                }
                bag.Items.RemoveAt(i);
            }
            if (bag.IsCrate && bag.Empty) bag.RespawnLeft = 90f;
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
            }
        }

        /// <summary>Печь: забираешь готовое, потом загружаешь руду и дерево из инвентаря.</summary>
        void UseFurnace(Actor a, Deployable f)
        {
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

        void SeparateActors()
        {
            for (int i = 0; i < Actors.Count; i++)
            {
                var a = Actors[i];
                if (a.Dead) continue;
                for (int j = i + 1; j < Actors.Count; j++)
                {
                    var b = Actors[j];
                    if (b.Dead) continue;
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
                foreach (var w in WallsAlong(a, b))
                    if (!w.PassBullets && Geo.SegmentAabb(a, b, w.Wall.Min, w.Wall.Max, out float t) && t < bestT) { bestT = t; hitWall = w; }
                foreach (var n in NodesAlong(a, b))
                    if (Geo.SegmentCircle(a, b, n.Pos, n.Radius, out float t) && t < bestT) { bestT = t; hitWall = null; }
                foreach (var d in DeploysAlong(a, b))
                    if (Geo.SegmentCircle(a, b, d.Pos, d.Radius, out float t) && t < bestT) { bestT = t; hitWall = null; }
                foreach (var act in Actors)
                {
                    if (act.Dead || act.Id == p.OwnerId) continue;
                    if (owner != null && owner.Team == Team.Npc && act.Team == Team.Npc) continue;
                    if (Geo.SegmentCircle(a, b, act.Pos, act.Radius, out float t) && t < bestT)
                    {
                        bestT = t;
                        hitActor = act;
                    }
                }

                if (bestT <= 1)
                {
                    var hitPos = a + (b - a) * bestT;
                    if (hitActor != null) Damage(hitActor, p.Damage, p.OwnerId, hitPos);
                    else
                    {
                        // пули ломают двери и разметку (D22), стены — нет
                        if (hitWall?.Owner is Structure st) DamageStructure(st, p.Damage * Building.BulletMult(st.Kind, st.Tier), p.OwnerId);
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

        public void Damage(Actor target, float dmg, int attackerId, Vector2 pos)
        {
            target.Hp -= dmg;
            target.LastAttacker = attackerId;
            target.LastDamagedTime = Time;
            Events.Add(new SimEvent { Kind = SimEventKind.HitActor, ActorId = target.Id, OtherId = attackerId, Pos = pos, Value = dmg });
            if (target.Hp <= 0) Kill(target, attackerId);
        }

        void Kill(Actor a, int killerId)
        {
            a.Hp = 0;
            a.Dead = true;
            a.DeadTime = Time;
            var bag = new LootBag { Id = NextId(), Pos = a.Pos, Despawn = 300f };
            foreach (var s in a.Inventory) bag.Items.Add(new ItemSlot(s.Id, s.Count));
            foreach (var s in a.Belt)
                if (s != null)
                {
                    if (s.State != null) s.State.ReloadLeft = 0;
                    bag.Items.Add(s);
                }
            a.ClearAll();
            if (!bag.Empty) Bags.Add(bag);
            Events.Add(new SimEvent { Kind = SimEventKind.Death, ActorId = a.Id, OtherId = killerId, Pos = a.Pos });
        }

        void StepBags()
        {
            for (int i = Bags.Count - 1; i >= 0; i--)
            {
                var b = Bags[i];
                if (b.IsCrate)
                {
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
