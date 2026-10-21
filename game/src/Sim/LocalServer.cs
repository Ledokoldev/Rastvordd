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
        public bool Swimming;
        public string Label;         // подпись над головой (боты: имя и занятие)
    }

    public struct ProjSnap { public int Id; public Vector2 Pos; public Vector2 Dir; public bool Arrow; }
    public struct BagSnap { public int Id; public Vector2 Pos; public bool IsCrate; public bool Empty; }
    public struct NodeSnap { public int Id; public NodeKind Kind; public Vector2 Pos; public float Radius; public float Fill; }
    public struct PickupSnap { public int Id; public PickupKind Kind; public Vector2 Pos; public bool Available; }
    public struct DeploySnap { public int Id; public DeployKind Kind; public Vector2 Pos; public bool Burning; }
    public struct FireSnap { public int Id; public Vector2 Pos; public float Radius, Left; }
    public struct ThrownSnap { public int Id; public string ItemId; public Vector2 Pos; public float Z; public float Fuse; }
    public struct StructSnap { public int Id; public StructKind Kind; public int I, J, Dir, Tier; public float Hp; public bool Open; public bool Locked; }

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
        public bool HoldingPlan;        // план постройки: призрак элемента под курсором
        public StructKind BuildPiece;
        public bool BuildOk;
        public string Place;            // РТ, на котором стоишь
        public Vector2? Home;           // твой межевой столб (для карты)
        public bool HasZone;            // зона столба под курсором (клетка столба, радиус World.ClaimRadius)
        public (int i, int j) ZoneCell;
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
        public List<StructSnap> Structures = new();
        public List<ThrownSnap> Throwns = new();
        public List<FireSnap> Fires = new();
        public int StructVersion;         // меняется при любой стройке/поломке/двери
        public List<SimEvent> Events = new();
    }

    public struct WallInfo { public Vector2 Min, Max; public float Height; public WallKind Kind; public int Tier; public bool Door; public bool Locked; public int StructId; }

    public sealed class MapInfo
    {
        public float HalfSize;
        public List<WallInfo> Walls = new();
        public List<(Vector2 min, Vector2 max)> Yards = new();   // площадки РТ: бетон под ногами
        public List<string> YardNames = new();
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
        readonly List<IBrain> _brains = new();
        readonly Dictionary<int, PlayerBot> _bots = new();
        bool _botsStarted;
        readonly int _seed;
        /// <summary>Сколько ботов-игроков живёт на острове (позже — от размера карты и настроек вайпа).</summary>
        public int BotCount = 3;   // 2 фермера + роумер
        static readonly string[] BotNames = { "Сёма", "Жека", "Толян", "Вован", "Лёха", "Димон" };
        readonly Dictionary<int, InputCmd> _inputs = new();
        readonly List<SimEvent> _eventsThisTick = new();

        public LocalServer(int seed)
        {
            World = new World(seed) { Seed = seed };
            _seed = seed;
            int i = 0;
            foreach (var npc in World.SpawnNpcs())
                _brains.Add(new NpcBrain(npc, World, seed * 31 + i++));
        }

        public MapInfo GetMap()
        {
            var t = World.Terrain;
            var m = new MapInfo { HalfSize = World.HalfSize, N = t.N, Cell = TerrainMap.Cell, Biomes = t.Biomes, Heights = t.Height };
            foreach (var mon in World.Monuments) { m.Yards.Add((mon.Min, mon.Max)); m.YardNames.Add(mon.Name); }
            foreach (var w in World.Walls)
                m.Walls.Add(new WallInfo { Min = w.Wall.Min, Max = w.Wall.Max, Height = w.Wall.Height, Kind = w.Kind });
            return m;
        }

        /// <summary>Подключение: если в сохранении есть персонаж с этим именем — продолжаем им.</summary>
        public int Connect(string name) => (World.FindPlayer(name) ?? World.SpawnPlayer(name)).Id;

        /// <summary>Загрузить сохранение (до Connect). false — файл от другого зерна/версии.</summary>
        public bool LoadSave(string json, out string report)
        {
            report = null;
            var save = WorldSave.FromJson(json);
            if (save == null || save.Version != 2 || save.Seed != World.Seed) return false;
            World.Load(save);
            // мир жил, пока игра была закрыта (не больше суток за раз)
            float away = (float)System.Math.Min(24 * 3600, (System.DateTime.UtcNow - save.SavedAtUtc).TotalSeconds);
            var lines = World.CatchUp(away);
            if (away >= 60)
                report = $"Тебя не было {(away >= 3600 ? $"{away / 3600:0.0} ч" : $"{away / 60:0} мин")}" + (lines.Count > 0 ? ": " + string.Join("; ", lines) : "");
            return true;
        }

        public string SaveJson() => World.Save().ToJson();

        /// <summary>Боты-игроки: после загрузки продолжают своими персонажами (по имени), иначе — новые с пляжа.</summary>
        void StartBots()
        {
            _botsStarted = true;
            for (int i = 0; i < BotCount && i < BotNames.Length; i++)
            {
                string name = BotNames[i] + " (бот)";
                var a = World.FindPlayer(name) ?? World.SpawnPlayer(name);
                var bot = new PlayerBot(a, World, _seed * 17 + i) { Archetype = i == 2 ? BotArchetype.Roamer : BotArchetype.Farmer };
                _bots[a.Id] = bot;
                _brains.Add(bot);
            }
        }

        /// <summary>Отладка: прокрутить мир на seconds без клиента, печатая занятия ботов.</summary>
        public void FastForward(float seconds, System.Action<string> log, int focusId = -1)
        {
            int ticks = (int)(seconds / World.Dt);
            for (int t = 0; t < ticks; t++)
            {
                if (focusId >= 0) _pending[focusId] = InputCmd.Empty;
                Tick();
                foreach (var e in World.Events)
                    if (e.Kind == SimEventKind.Death)
                        log($"[{World.Time,6:0}s] смерть: {World.Find(e.ActorId)?.Name} ← {World.Find(e.OtherId)?.Name ?? "?"}");
                if (t % (int)(30 / World.Dt) == 0)
                    foreach (var b in _bots.Values)
                        log($"[{World.Time,6:0}s] {b.Me.Name} @({b.Me.Pos.X:0.0};{b.Me.Pos.Y:0.0}) {b.Debug}: {b.Doing} · HP {b.Me.Hp:0} · дерево {b.Me.Count("wood")} камень {b.Me.Count("stone")} · построек {World.Structures.FindAll(s => s.OwnerId == b.Me.Id).Count} · {World.LastInfo.GetValueOrDefault(b.Me.Id)}");
            }
        }

        public Vector2 BotHome(int index)
        {
            int i = 0;
            foreach (var b in _bots.Values)
                if (i++ == index)
                    foreach (var d in World.Deployables) if (d.Kind == DeployKind.Claim && d.OwnerId == b.Me.Id) return d.Pos;
            return BotPos(index);
        }

        public Vector2 BotPos(int index) { int i = 0; foreach (var b in _bots.Values) if (i++ == index) return b.Me.Pos; return Vector2.Zero; }

        public string BotLabel(int actorId) => _bots.TryGetValue(actorId, out var b) ? $"{b.Me.Name}: {b.Doing}" : null;

        public void Submit(int clientId, InputCmd cmd) => _pending[clientId] = cmd;

        public void Tick()
        {
            _inputs.Clear();
            foreach (var kv in _pending) _inputs[kv.Key] = kv.Value;
            _pending.Clear();
            if (!_botsStarted) StartBots();
            foreach (var b in _brains) _inputs[b.Me.Id] = b.Think();
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
            snap.Self.Place = World.MonumentAt(me.Pos)?.Name;
            foreach (var d in World.Deployables) if (d.Kind == DeployKind.Claim && d.Auth.Contains(me.Id)) { snap.Self.Home = d.Pos; break; }
            if (me.Held?.Def?.Build == true && World.ClaimAt(Building.CellOf(me.LastAim)) is Deployable zc) { snap.Self.ZoneCell = Building.CellOf(zc.Pos); snap.Self.HasZone = true; }
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
                    Swimming = a.Swimming,
                    Label = BotLabel(a.Id),
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

            foreach (var t in World.Throwns)
                if (Vector2.DistanceSquared(t.Pos, me.Pos) < EventRadius * EventRadius)
                    snap.Throwns.Add(new ThrownSnap { Id = t.Id, ItemId = t.ItemId, Pos = t.Pos, Z = t.Z, Fuse = t.Landed ? t.FuseLeft : -1 });
            foreach (var f in World.Fires)
                if (Vector2.DistanceSquared(f.Pos, me.Pos) < EventRadius * EventRadius)
                    snap.Fires.Add(new FireSnap { Id = f.Id, Pos = f.Pos, Radius = f.Radius, Left = f.Left });
            snap.StructVersion = World.StructVersion;
            foreach (var st in World.Structures)
                if (Vector2.DistanceSquared(st.Center, me.Pos) < r2)
                    snap.Structures.Add(new StructSnap { Id = st.Id, Kind = st.Kind, I = st.I, J = st.J, Dir = st.Dir, Tier = st.Tier, Hp = st.Hp / st.MaxHp, Open = st.Open, Locked = st.Locked });

            foreach (var e in _eventsThisTick)
            {
                bool mine = e.ActorId == me.Id || e.OtherId == me.Id;
                bool personal = e.Kind is SimEventKind.Loot or SimEventKind.Gather or SimEventKind.Info
                                       or SimEventKind.Crafted or SimEventKind.Placed or SimEventKind.Locked;   // Built видят все рядом: слышно стройку
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
            if (!me.Dead && held?.Def?.Build == true)
            {
                s.HoldingPlan = true;
                s.BuildPiece = me.BuildPiece;
                s.BuildOk = World.CanBuild(me, me.BuildPiece, me.LastAim, out string why);
                var cost = Building.Cost(me.BuildPiece, me.BuildPiece == StructKind.Door ? 1 : 0);
                string price = string.Join(", ", System.Array.ConvertAll(cost, c => $"{Items.Name(c.id)} {c.n}"));
                if (string.IsNullOrEmpty(s.InteractHint))
                    s.InteractHint = $"{Building.KindName(me.BuildPiece)} · {price} · R — другой элемент{(s.BuildOk ? "" : " · " + why)}";
            }
            else if (!me.Dead && held?.Id == "tool_hammer" && World.TargetStructure(me, 2f) is Structure t && string.IsNullOrEmpty(s.InteractHint))
            {
                string state = $"{Building.KindName(t.Kind)} ({Building.TierNames[t.Tier]}) {(int)t.Hp}/{(int)t.MaxHp}";
                if (t.Tier < Building.MaxTier)
                {
                    var up = Building.Cost(t.Kind, t.Tier + 1);
                    s.InteractHint = $"{state} · ЛКМ — {Building.TierNames[t.Tier + 1]}: " + string.Join(", ", System.Array.ConvertAll(up, c => $"{Items.Name(c.id)} {c.n}"));
                }
                else s.InteractHint = state + (t.Hp < t.MaxHp ? " · ЛКМ — починить" : "");
            }
            return s;
        }

        string Hint((World.InteractKind kind, object target) it) => it.kind switch
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
            World.InteractKind.Box => ((Deployable)it.target).Storage.Empty ? "[E] Ящик (пуст) · Alt+E — сложить" : $"[E] Забрать из ящика ({((Deployable)it.target).Storage.Items.Count} шт.) · Alt+E — сложить",
            World.InteractKind.Claim => ClaimHint(World, (Deployable)it.target),
            World.InteractKind.Door => (((Structure)it.target).Open ? "[E] Закрыть дверь" : "[E] Открыть дверь") + (((Structure)it.target).Locked ? " · замок" : ""),
            _ => "",
        };

        static string ClaimHint(World w, Deployable c)
        {
            var (b, found, doors) = w.ClaimLoad(c);
            float h = w.ClaimHoursLeft(c);
            string upkeep = b <= 0 ? "" : h > 0 ? $" · запас {h:0.0} ч" : c.Grace > 0 ? $" · ЗАПАСА НЕТ, гниение через {c.Grace / 60:0} мин" : " · ГНИЁТ";
            return $"[E] Межевой столб · нагрузка {b:0}/{World.BudgetMax} · фундаменты {found}/{World.FoundMax} · двери {doors}/{World.DoorMax}{upkeep}";
        }

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
