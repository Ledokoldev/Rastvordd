using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    public struct ActorSnap
    {
        public int Id;
        public Vector2 Pos;
        public float Facing;
        public float RecoilDeg;      // увод ствола — клиент поворачивает оружие в руках
        public float Peek;           // прицеливание (ПКМ) — оружие у головы
        public bool Dead;
        public bool IsNpc;
        public string HeldId;
        public bool Reloading;
        public bool Sprinting;
        public bool Crouching;
    }

    public struct ProjSnap { public int Id; public Vector2 Pos; public Vector2 Dir; public bool Arrow; }
    public struct BagSnap { public int Id; public Vector2 Pos; public bool IsCrate; public bool Empty; }
    public struct NodeSnap { public int Id; public NodeKind Kind; public Vector2 Pos; public float Radius; public float Fill; }
    public struct PickupSnap { public int Id; public PickupKind Kind; public Vector2 Pos; public bool Available; }
    public struct DeploySnap { public int Id; public DeployKind Kind; public Vector2 Pos; public bool Burning; }

    public struct BeltSlotInfo
    {
        public string Id;
        public string Name;
        public int Count;
        public bool IsGun;          // огнестрел или лук — показываем патроны
        public int Ammo, Magazine;
        public bool IsDeployable;
    }

    public struct RecipeInfo
    {
        public int Index;
        public string Output;      // id предмета (для иконки)
        public string Name;
        public string CostText;
        public bool CanCraft;
        public bool NeedsBench;
        public float Time;
    }

    public sealed class SelfInfo
    {
        public int Id;
        public bool Dead;
        public float Hp, MaxHp, Food, Water;
        public string WeaponName;
        public bool HoldingGun;
        public int Ammo, Magazine, Reserve;
        public float ReloadProgress;    // 0..1, 0 — не перезаряжается
        public float SpreadDeg;
        public int Bandages;
        public string InteractHint;     // пусто — рядом ничего нет
        public bool HeldDeploy;
        public bool NearBench;
        public BeltSlotInfo?[] Belt = new BeltSlotInfo?[Actor.BeltSize];
        public int Current;
        public List<(string name, int count)> Inventory = new();
        public List<(string name, float progress)> CraftQueue = new();
        public List<RecipeInfo> Recipes = new();
    }

    /// <summary>То, что сервер отправляет клиенту за тик. Только видимое этому клиенту.</summary>
    public sealed class Snapshot
    {
        public uint Tick;
        public float Time;
        public float DayTime;      // 0..1, см. World.TimeOfDay
        public SelfInfo Self;
        public List<ActorSnap> Actors = new();
        public List<ProjSnap> Projectiles = new();
        public List<BagSnap> Bags = new();
        public List<NodeSnap> Nodes = new();
        public List<PickupSnap> Pickups = new();
        public List<DeploySnap> Deployables = new();
        public List<SimEvent> Events = new();
    }

    public struct WallInfo { public Vector2 Min, Max; public float Height; public WallKind Kind; }

    public sealed class MapInfo
    {
        public float HalfSize;
        public List<WallInfo> Walls = new();
        // рельеф и биомы (сетка N×N, клетка Cell метров) — отправляются один раз при подключении
        public int N;
        public float Cell;
        public Biome[] Biomes;
        public float[] Heights;
    }

    /// <summary>
    /// Локальный сервер. Сейчас живёт в одном процессе с клиентом; позже тот же класс
    /// будет работать за сетевым транспортом. Клиент общается с ним только через
    /// InputCmd → Submit и Snapshot ← BuildSnapshot.
    /// </summary>
    public sealed class LocalServer
    {
        const float WorldViewRadius = 70f;   // ресурсы, пикапы и постройки видны без учёта конуса
        const float EventRadius = 80f;

        public readonly World World;
        readonly Dictionary<int, InputCmd> _pending = new();
        readonly List<NpcBrain> _brains = new();
        readonly Dictionary<int, InputCmd> _inputs = new();
        readonly List<SimEvent> _eventsThisTick = new();

        public LocalServer(int seed)
        {
            World = new World(seed);
            int i = 0;
            foreach (var npc in World.SpawnNpcs())
                _brains.Add(new NpcBrain(npc, World, seed * 31 + i++));
        }

        public MapInfo GetMap()
        {
            var t = World.Terrain;
            var m = new MapInfo { HalfSize = World.HalfSize, N = t.N, Cell = TerrainMap.Cell, Biomes = t.Biomes, Heights = t.Height };
            foreach (var w in World.Walls)
                m.Walls.Add(new WallInfo { Min = w.Wall.Min, Max = w.Wall.Max, Height = w.Wall.Height, Kind = w.Kind });
            return m;
        }

        public int Connect(string name) => World.SpawnPlayer(name).Id;

        public void Submit(int clientId, InputCmd cmd) => _pending[clientId] = cmd;

        public void Tick()
        {
            _inputs.Clear();
            foreach (var kv in _pending) _inputs[kv.Key] = kv.Value;
            _pending.Clear();
            int bi = 0;
            foreach (var a in World.Actors)
                if (a.Team == Team.Npc) _inputs[a.Id] = _brains[bi++].Think();
            World.Step(_inputs);
            _eventsThisTick.Clear();
            _eventsThisTick.AddRange(World.Events);
        }

        readonly List<ResourceNode> _snapNodes = new();
        readonly List<Deployable> _snapDeploys = new();

        public Snapshot BuildSnapshot(int clientId)
        {
            var me = World.Find(clientId);
            var snap = new Snapshot { Tick = World.Tick, Time = World.Time, DayTime = World.TimeOfDay };
            snap.Self = BuildSelf(me);
            float r2 = WorldViewRadius * WorldViewRadius;

            foreach (var a in World.Actors)
            {
                bool visible = a.Id == me.Id || IsVisibleTo(me, a);
                if (!visible) continue;
                var w = a.Weapon;
                snap.Actors.Add(new ActorSnap
                {
                    Id = a.Id, Pos = a.Pos, Facing = a.Facing, RecoilDeg = a.Weapon?.Recoil ?? 0, Peek = a.Peek, Dead = a.Dead, IsNpc = a.Team == Team.Npc,
                    HeldId = a.Held?.Id, Reloading = w != null && w.ReloadLeft > 0,
                    Sprinting = a.Vel.Length() > World.WalkSpeed + 0.3f,
                    Crouching = a.Crouching,
                });
            }

            foreach (var p in World.Projectiles)
                if (Vector2.DistanceSquared(p.Pos, me.Pos) < EventRadius * EventRadius)
                    snap.Projectiles.Add(new ProjSnap { Id = p.Id, Pos = p.Pos, Dir = p.Dir, Arrow = p.Arrow });

            foreach (var b in World.Bags)
                if (Vector2.DistanceSquared(b.Pos, me.Pos) < 60 * 60 && (b.IsCrate || IsPointVisible(me, b.Pos)))
                    snap.Bags.Add(new BagSnap { Id = b.Id, Pos = b.Pos, IsCrate = b.IsCrate, Empty = b.Empty });

            foreach (var n in World.NodesInRadius(me.Pos, WorldViewRadius, _snapNodes))
                if (Vector2.DistanceSquared(n.Pos, me.Pos) < r2)
                    snap.Nodes.Add(new NodeSnap { Id = n.Id, Kind = n.Kind, Pos = n.Pos, Radius = n.Radius, Fill = n.Amount / n.MaxAmount });

            foreach (var p in World.Pickups)
                if (Vector2.DistanceSquared(p.Pos, me.Pos) < r2)
                    snap.Pickups.Add(new PickupSnap { Id = p.Id, Kind = p.Kind, Pos = p.Pos, Available = p.Available });

            foreach (var d in World.DeploysInRadius(me.Pos, WorldViewRadius, _snapDeploys))
                if (Vector2.DistanceSquared(d.Pos, me.Pos) < r2)
                    snap.Deployables.Add(new DeploySnap { Id = d.Id, Kind = d.Kind, Pos = d.Pos, Burning = d.Burning });

            foreach (var e in _eventsThisTick)
            {
                bool mine = e.ActorId == me.Id || e.OtherId == me.Id;
                bool personal = e.Kind is SimEventKind.Loot or SimEventKind.Gather or SimEventKind.Info
                                       or SimEventKind.Crafted or SimEventKind.Placed;
                if (personal && e.ActorId != me.Id) continue;
                if (!mine && Vector2.DistanceSquared(e.Pos, me.Pos) > EventRadius * EventRadius) continue;
                snap.Events.Add(e);
            }
            return snap;
        }

        SelfInfo BuildSelf(Actor me)
        {
            var held = me.Held;
            var w = me.Weapon;
            bool gun = w != null && !w.Def.Melee;
            var s = new SelfInfo
            {
                Id = me.Id, Dead = me.Dead, Hp = me.Hp, MaxHp = me.MaxHp, Food = me.Food, Water = me.Water,
                WeaponName = held != null ? Items.Name(held.Id) : "пустые руки",
                HoldingGun = gun,
                Ammo = gun ? w.Ammo : 0, Magazine = gun ? w.Def.Magazine : 0,
                Reserve = gun ? me.Count(w.Def.AmmoId) : 0,
                ReloadProgress = gun && w.ReloadLeft > 0 ? 1f - w.ReloadLeft / w.Def.ReloadTime : 0,
                SpreadDeg = World.CurrentSpreadDeg(me),
                Bandages = me.Count("bandage"),
                Current = me.Current,
                HeldDeploy = held?.Def?.Deploy is DeployKind.Workbench or DeployKind.Furnace,
                NearBench = World.NearBench(me.Pos),
            };

            for (int i = 0; i < Actor.BeltSize; i++)
            {
                var slot = me.Belt[i];
                if (slot == null) continue;
                var st = slot.State;
                bool isGun = st != null && !st.Def.Melee;
                s.Belt[i] = new BeltSlotInfo
                {
                    Id = slot.Id, Name = Items.Name(slot.Id), Count = slot.Count,
                    IsGun = isGun, Ammo = isGun ? st.Ammo : 0, Magazine = isGun ? st.Def.Magazine : 0,
                    IsDeployable = slot.Def?.Deploy != DeployKind.None,
                };
            }

            foreach (var it in me.Inventory) s.Inventory.Add((Items.Name(it.Id), it.Count));
            foreach (var job in me.CraftQueue)
                s.CraftQueue.Add((Items.Name(job.Recipe.Output), 1f - job.Left / job.Recipe.Time));

            for (int i = 0; i < Recipes.All.Count; i++)
            {
                var r = Recipes.All[i];
                var cost = new List<string>();
                foreach (var (id, n) in r.Cost) cost.Add($"{Items.Name(id)} {n}");
                s.Recipes.Add(new RecipeInfo
                {
                    Index = i,
                    Output = r.Output,
                    Name = Items.Name(r.Output) + (r.Count > 1 ? $" ×{r.Count}" : ""),
                    CostText = string.Join(", ", cost),
                    CanCraft = World.CanCraft(me, r),
                    NeedsBench = r.Bench > 0,
                    Time = r.Time,
                });
            }

            if (!me.Dead) s.InteractHint = Hint(World.NearestInteract(me));
            return s;
        }

        static string Hint((World.InteractKind kind, object target) it) => it.kind switch
        {
            World.InteractKind.Bag => ((LootBag)it.target).IsCrate ? "[E] Залутать ящик" : "[E] Залутать",
            World.InteractKind.Pickup => ((Pickup)it.target).Kind switch
            {
                PickupKind.Hemp => "[E] Собрать коноплю",
                PickupKind.Berries => "[E] Собрать ягоды",
                _ => "[E] Попить",
            },
            World.InteractKind.Furnace => FurnaceHint((Deployable)it.target),
            World.InteractKind.Workbench => "[E] Верстак",
            _ => "",
        };

        static string FurnaceHint(Deployable f)
        {
            string state = f.Burning ? $"горит · руда {f.Ore + f.SulfurOre}, дерево {(int)f.Fuel}" : "не горит";
            int ready = f.OutMetal + f.OutSulfur + f.OutCharcoal;
            return $"[E] Печь ({state}{(ready > 0 ? $" · готово {ready}" : "")})";
        }

        /// <summary>Серверный туман войны: клиент не получает тех, кого не видит (защита от wallhack).</summary>
        bool IsVisibleTo(Actor viewer, Actor target)
        {
            if (viewer.Dead) return Vector2.Distance(viewer.Pos, target.Pos) < 30;
            if (World.CanSeeActor(viewer, target, World.ViewRange(viewer), World.ViewCone(viewer))) return true;
            // выстрел выдаёт стрелка, если между нами нет стен
            bool shotRecently = World.Time - target.LastShotTime < 0.6f;
            return shotRecently && Vector2.Distance(viewer.Pos, target.Pos) < 60 && World.HasLineOfSight(viewer.Pos, target.Pos);
        }

        bool IsPointVisible(Actor viewer, Vector2 p) =>
            Vector2.Distance(viewer.Pos, p) < 3 || World.CanSee(viewer, p, World.ViewRange(viewer), World.ViewCone(viewer));
    }
}
