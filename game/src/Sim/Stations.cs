using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Станции прогрессии D54: регистрация предметов, рецептов и узлов изучения из таблиц Data/Progression.cs.
    /// Логика (ток, станки, буровая, конвейер, вода, бочка) — partial World ниже.
    /// </summary>
    public static class StationDefs
    {
        static readonly Dictionary<DeployKind, StationDef> ByKind = new();
        static StationDefs() { foreach (var s in Prog.Stations) ByKind[s.Kind] = s; }
        public static StationDef Get(DeployKind k) => ByKind.TryGetValue(k, out var s) ? s : null;
        public static StationDef ByItem(string id) => Array.Find(Prog.Stations, s => s.Item == id);

        /// <summary>Рецепты станка по ключу (токарный станок — из Weaponry.Lathe).</summary>
        public static List<MachineRecipe> RecipesFor(string machine)
        {
            var l = new List<MachineRecipe>();
            if (machine == "lathe")
            {
                foreach (var r in Weaponry.Lathe) l.Add(new MachineRecipe { Machine = "lathe", Output = r.Output, In = r.In, Time = r.Time, Bench3 = r.Bench3 });
                return l;
            }
            foreach (var r in Prog.Machines) if (r.Machine == machine) l.Add(r);
            return l;
        }

        public static bool BoostMatches(StationDef s, string output)
        {
            if (s.Boost == null || output == null) return false;
            foreach (var b in s.Boost)
                if (b.EndsWith("*") ? output.StartsWith(b.Substring(0, b.Length - 1)) : output == b) return true;
            return false;
        }

        static void M(string id, string desc, int rarity, float value, int stack = 0)
            => Weaponry.Info[id] = new Weaponry.Meta { Desc = desc, Rarity = rarity, Value = value, Stack = stack };

        /// <summary>Лут D54 по ключу источника (barrel, crate0–2, safe, punch, event): строки Prog.Loot.</summary>
        public static void Loot(Random rng, LootBag bag, string source)
        {
            if (source == null || !Prog.Loot.TryGetValue(source, out var lines)) return;
            foreach (var l in lines)
            {
                if (rng.NextDouble() >= l.Chance) continue;
                int n = rng.Next(l.Min, l.Max + 1);
                if (l.Liquid != null) bag.Items.Add(Liquids.Make(l.Id, l.Liquid, n));
                else if (Items.Get(l.Id)?.Weapon != null) bag.Items.Add(Items.NewSlot(l.Id));
                else bag.Items.Add(new ItemSlot(l.Id, n));
            }
        }

        /// <summary>Предметы D54 (зовётся из Items).</summary>
        internal static void Register(Func<string, string, ItemCategory, ItemDef> add)
        {
            add(Prog.Alloy, "Легированная сталь", ItemCategory.Resource);
            M(Prog.Alloy, "Слитки легированной стали. Не плавится и не добывается: аирдропы, закрытые ящики, крейты РТ 1–2, переработка деталей тира 2–3, " +
                          "дорогого ценного лома и пластин. Верстак 3, оружейный стол, стволы тира 3, армейская одежда, C4, броня построек", 3, Prog.Value(Prog.Alloy), 100);
            foreach (var (id, name, desc, rarity, value) in Prog.LiquidDefs)
            {
                add(id, name, ItemCategory.Resource).IsLiquid = true;
                M(id, desc + ". Живёт только в ёмкостях (фляга, канистра, бочка 200 л)", rarity, value);
            }
            foreach (var (id, name, liters, water, _, _, rarity, desc) in Prog.Containers)
            {
                var d = add(id, name, ItemCategory.Tool);
                d.Capacity = liters; d.WaterOnly = water;
                M(id, $"{desc}. Объём {liters:0.#} л. Карточка: «Налить» у бочки, колонки, водоёма, «Вылить», «Выпить»; перетащи на другую ёмкость — перелить", rarity, Prog.Value(id), 1);
            }
            foreach (var (id, name, heal, _, cure, rarity, stack, desc) in Prog.Meds)
            {
                var d = add(id, name, heal > 0 ? ItemCategory.Medical : ItemCategory.Component);
                d.Heal = heal; d.CuresPoison = cure;
                M(id, desc, rarity, Prog.Value(id), stack);
            }
            foreach (var t in Prog.Tools)
            {
                var w = WeaponDef.MeleeTool(t.Id, t.Dmg, t.Interval, t.Range, t.Tree, t.Stone, t.Metal, t.Sulfur);
                w.NoiseRadius = t.Noise;
                var d = add(t.Id, t.Name, ItemCategory.Tool);
                d.Weapon = w; d.FuelId = t.Fuel; d.FuelPerHit = t.FuelPerHit;
                M(t.Id, t.Desc, Math.Min(4, t.Bench + 1), 10 + 30 * t.Bench);
            }
            var bars = add("bars", "Решётки и ставни", ItemCategory.Tool);
            bars.Bars = true;
            M("bars", "ЛКМ по окну своей базы — решётка со ставнями. E у окна: ставни закрыты — окно как стена (не видно, пули не летят), открыты — обычное окно", 1, Prog.Value("bars"), 5);
            foreach (var s in Prog.Stations)
            {
                if (s.Registered) continue;
                add(s.Item, s.Name, ItemCategory.Deployable).Deploy = s.Kind;
                M(s.Item, $"{s.Desc}. Место: {Footprints.SizeName(s.Size)}", s.Bench >= 3 ? 3 : s.Bench + 1, 20 + 40 * s.Bench, 1);
            }
        }

        /// <summary>Рецепты D54: переписанные (сталь, станции) и новые. Зовётся в конце Recipes.</summary>
        internal static void AddRecipes(List<Recipe> all)
        {
            foreach (var (output, bench, station, cost) in Prog.RecipeOverride)
            {
                var r = all.Find(x => x.Output == output);
                if (r == null) { r = new Recipe { Output = output, Time = 10 }; all.Add(r); }
                r.Bench = bench; r.Station = station; r.Cost = cost;
            }
            foreach (var (output, count, time, bench, station, cost) in Prog.Recipes)
                all.Add(new Recipe { Output = output, Count = count, Time = time, Bench = bench, Station = station, Cost = cost });
            foreach (var (id, _, _, _, bench, cost, _, _) in Prog.Containers)
                all.Add(new Recipe { Output = id, Time = 4, Bench = bench, Cost = cost });
            foreach (var t in Prog.Tools)
                all.Add(new Recipe { Output = t.Id, Time = t.Time, Bench = t.Bench, Cost = t.Cost });
            foreach (var s in Prog.Stations)
                if (!s.Registered) all.Add(new Recipe { Output = s.Item, Time = s.Time, Bench = s.Bench, Cost = s.Cost });
        }

        /// <summary>Узлы изучения D54 (в конец столбца своего верстака).</summary>
        internal static void AddTech(List<TechNode> all)
        {
            foreach (var s in Prog.Stations)
                if (!s.Registered && s.Scrap > 0) all.Add(new TechNode { Id = s.Item, Bench = Math.Max(1, s.Bench), Scrap = s.Scrap, Col = Math.Max(1, s.Bench) - 1, Requires = s.Requires ?? Array.Empty<string>() });
            foreach (var s in Prog.Stations)   // прожектор и т. п.: без своей цены — открывается вместе с предком
                if (!s.Registered && s.Scrap == 0 && s.Requires != null) all.Add(new TechNode { Id = s.Item, Bench = Math.Max(1, s.Bench), Scrap = 10, Col = Math.Max(1, s.Bench) - 1, Requires = s.Requires });
            foreach (var t in Prog.Tools)
                if (t.Scrap > 0) all.Add(new TechNode { Id = t.Id, Bench = Math.Max(1, t.Bench), Scrap = t.Scrap, Col = Math.Max(1, t.Bench) - 1 });
            foreach (var (id, bench, scrap, req) in Prog.Tech)
                all.Add(new TechNode { Id = id, Bench = bench, Scrap = scrap, Col = bench - 1, Requires = req ?? Array.Empty<string>() });
        }
    }

    public sealed partial class World
    {
        // ======================================================================== станции рядом, крафт

        /// <summary>Станция этого вида рядом (до края хитбокса ≤ Prog.StationRange).</summary>
        public Deployable StationNear(Vector2 p, DeployKind k)
        {
            Deployable best = null; float bd = float.MaxValue;
            foreach (var d in DeploysNear(p, Prog.StationRange + 2.5f))
            {
                if (d.Kind != k) continue;
                float dist = Vector2.Distance(p, Vector2.Clamp(p, d.Min, d.Max));
                if (dist <= Prog.StationRange && dist < bd) { bd = dist; best = d; }
            }
            return best;
        }

        /// <summary>Множители времени и цены рецепта у станций рядом (ускорение, удешевление, ток).</summary>
        (float time, float cost) CraftMul(Vector2 p, Recipe r)
        {
            float time = 1, cost = 1;
            foreach (var s in Prog.Stations)
            {
                bool need = r.Station == s.Kind;
                if (!need && !StationDefs.BoostMatches(s, r.Output)) continue;
                var d = StationNear(p, s.Kind);
                if (d == null) continue;
                float t = need ? 1 : s.BoostTime;
                if (d.Powered && s.PowerSpeed > 1) t /= s.PowerSpeed;
                time = MathF.Min(time, t);
                if (!need) cost = MathF.Min(cost, s.BoostCost);
            }
            return (time, cost);
        }

        public (string id, int n)[] CraftCost(Actor a, Recipe r)
        {
            var (_, cm) = CraftMul(a.Pos, r);
            if (cm >= 1) return r.Cost;
            return Array.ConvertAll(r.Cost, c => (c.id, Math.Max(1, (int)MathF.Ceiling(c.count * cm))));
        }

        public float CraftTime(Actor a, Recipe r) => r.Time * CraftMul(a.Pos, r).time;

        public bool StationOk(Actor a, Recipe r) => r.Station == DeployKind.None || StationNear(a.Pos, r.Station) != null;

        public static string StationName(DeployKind k) => StationDefs.Get(k)?.Name ?? k.ToString();

        // ======================================================================== хранилища станций

        /// <summary>Сколько ячеек у хранилища установленного (ящик, станок, станция).</summary>
        public static int StorageCap(Deployable d) => d.Kind switch
        {
            DeployKind.Box => BoxSlots,
            DeployKind.Recycler or DeployKind.Lathe => RecyclerSlots,
            _ => StationDefs.Get(d.Kind)?.Slots ?? BoxSlots,
        };

        static int CountIn(List<ItemSlot> items, string id)
        {
            if (Liquids.Is(id)) return (int)(Liquids.Total(items, id) + 1e-3f);
            int n = 0;
            foreach (var s in items) if (s.Id == id && s.State == null) n += s.Count;
            return n;
        }

        static void TakeIn(List<ItemSlot> items, string id, int n)
        {
            if (Liquids.Is(id)) { Liquids.Drain(items, id, n); return; }
            for (int i = items.Count - 1; i >= 0 && n > 0; i--)
            {
                var s = items[i];
                if (s.Id != id || s.State != null) continue;
                int t = Math.Min(n, s.Count);
                s.Count -= t; n -= t;
                if (s.Count <= 0) items.RemoveAt(i);
            }
        }

        /// <summary>Положить выход в хранилище: вещь — в стопку/свободную ячейку, жидкость — в ёмкости. false — некуда.</summary>
        static bool PutIn(List<ItemSlot> items, int cap, string id, int n)
        {
            if (Liquids.Is(id))
            {
                if (Liquids.RoomFor(items, id) < n - 1e-3f) return false;
                Liquids.Pour(items, id, n);
                return true;
            }
            int max = Items.MaxStack(id), room = 0;
            foreach (var s in items) if (s.Id == id && s.State == null) room += Math.Max(0, max - s.Count);
            room += Math.Max(0, cap - items.Count) * max;
            if (room < n) return false;
            foreach (var s in items)
            {
                if (n <= 0) break;
                if (s.Id != id || s.State != null || s.Count >= max) continue;
                int t = Math.Min(n, max - s.Count); s.Count += t; n -= t;
            }
            while (n > 0) { int t = Math.Min(n, max); items.Add(new ItemSlot(id, t)); n -= t; }
            return true;
        }

        /// <summary>Бочка 200 л рядом со станцией (для топлива).</summary>
        Deployable TankNear(Vector2 p, string liquid)
        {
            foreach (var d in DeploysNear(p, Prog.TankRange + 1.5f))
                if (d.Kind == DeployKind.Barrel && Vector2.Distance(d.Pos, p) <= Prog.TankRange + 1f && Liquids.Tank(d).liquid == liquid) return d;
            return null;
        }

        /// <summary>Сжечь топливо станции за dt: дерево — поштучно из ячеек, жидкость — литры из ёмкостей или бочки рядом. false — нечем.</summary>
        bool Burn(Deployable d, StationDef s, float dt)
        {
            if (s.Burn == null || s.BurnPerMin <= 0) return true;
            float need = s.BurnPerMin / 60f * dt;
            if (d.Fuel >= need) { d.Fuel -= need; return true; }
            var items = d.Storage?.Items;
            if (Liquids.Is(s.Burn))
            {
                float got = items != null ? Liquids.Drain(items, s.Burn, 0.25f) : 0;
                if (got <= 0 && TankNear(d.Pos, s.Burn) is Deployable tank) got = Liquids.TankDrain(tank, s.Burn, 0.25f);
                d.Fuel += got;
            }
            else if (items != null && CountIn(items, s.Burn) > 0) { TakeIn(items, s.Burn, 1); d.Fuel += 1; }
            if (d.Fuel < need) return false;
            d.Fuel -= need;
            return true;
        }

        bool HasBurn(Deployable d, StationDef s)
        {
            if (s.Burn == null) return true;
            if (d.Fuel > 0.001f) return true;
            if (d.Storage != null && (Liquids.Is(s.Burn) ? Liquids.Total(d.Storage.Items, s.Burn) > 0.001f : CountIn(d.Storage.Items, s.Burn) > 0)) return true;
            return Liquids.Is(s.Burn) && TankNear(d.Pos, s.Burn) != null;
        }

        /// <summary>Что станок сделает из того, что лежит: первый рецепт по таблице, на который хватает (и выход есть куда класть).</summary>
        MachineRecipe MachineJob(Deployable d, StationDef s)
        {
            if (s.Machine == null || d.Storage == null) return null;
            var items = d.Storage.Items;
            foreach (var r in Prog.Machines)
            {
                if (r.Machine != s.Machine) continue;
                bool ok = true;
                foreach (var (id, n) in r.In) if (CountIn(items, id) < n + (id == s.Burn ? 1 : 0)) { ok = false; break; }
                if (!ok) continue;
                if (Liquids.Is(r.Output))
                {
                    // выход в ёмкость: свободное место + ёмкости, которые опустеют после слива входа
                    float room = Liquids.RoomFor(items, r.Output);
                    foreach (var (id, n) in r.In)
                        if (Liquids.Is(id)) foreach (var c in items) if (c.Liquid == id && c.Liters <= n + 1e-3f && !(c.Def.WaterOnly && !Liquids.IsWater(r.Output))) room += c.Def.Capacity;
                    if (room < r.Count - 1e-3f) continue;
                }
                else
                {
                    int max = Items.MaxStack(r.Output);
                    if (items.Count >= StorageCap(d) && !items.Exists(x => x.Id == r.Output && x.State == null && x.Count + r.Count <= max)) continue;
                }
                return r;
            }
            return null;
        }

        void CompleteJob(Deployable d, MachineRecipe r)
        {
            var items = d.Storage.Items;
            foreach (var (id, n) in r.In) TakeIn(items, id, n);
            if (!PutIn(items, StorageCap(d), r.Output, r.Count))
            {
                foreach (var (id, n) in r.In) PutIn(items, StorageCap(d) + 4, id, n);   // вернуть как было
                return;
            }
            Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = d.OwnerId, Pos = d.Pos, Text = $"станция: {Items.Name(r.Output)}" });
        }

        // ======================================================================== шаг

        float _powerTimer;

        /// <summary>Ток: каждый работающий генератор питает станции в радиусе, ближние первыми, пока хватает мощности.</summary>
        void StepPower()
        {
            if ((_powerTimer -= Dt) > 0) return;
            _powerTimer = Prog.PowerTick;
            foreach (var d in Deployables) d.Powered = false;
            foreach (var g in Deployables)
            {
                if (g.Kind != DeployKind.Generator || !g.Active) continue;
                var gs = StationDefs.Get(g.Kind);
                float budget = gs.PowerOut;
                var near = DeploysInRadius(g.Pos, gs.PowerRadius + 1f, new List<Deployable>());
                near.Sort((x, y) => Vector2.DistanceSquared(x.Pos, g.Pos).CompareTo(Vector2.DistanceSquared(y.Pos, g.Pos)));
                foreach (var c in near)
                {
                    if (c.Powered || c.OwnerId != g.OwnerId && !g.Auth.Overlaps(c.Auth)) continue;   // чужую базу не питаем
                    var cs = StationDefs.Get(c.Kind);
                    if (cs == null || cs.Power <= 0 || cs.Power > budget || Vector2.Distance(c.Pos, g.Pos) > gs.PowerRadius) continue;
                    if (!c.On && cs.Machine == null && c.Kind is not (DeployKind.Lamp or DeployKind.Floodlight or DeployKind.Lathe)) continue;
                    c.Powered = true;
                    budget -= cs.Power;
                }
            }
        }

        void StepStations()
        {
            StepPower();
            foreach (var d in Deployables)
            {
                var s = StationDefs.Get(d.Kind);
                if (s == null || s.Registered) continue;
                switch (d.Kind)
                {
                    case DeployKind.Generator:
                        d.Active = d.On && Burn(d, s, Dt);
                        if (d.Active && Tick % 90 == 0) Noises.Add(new Noise { Pos = d.Pos, Radius = s.Noise, SourceId = d.OwnerId, Time = Time });
                        break;
                    case DeployKind.Lamp or DeployKind.Floodlight:
                        d.Active = d.On && d.Powered;
                        break;
                    case DeployKind.Drill:
                        StepDrill(d, s);
                        break;
                    case DeployKind.Conveyor:
                        StepConveyor(d, s);
                        break;
                    default:
                        if (s.Machine != null) StepMachine(d, s);
                        break;
                }
            }
        }

        void StepMachine(Deployable d, StationDef s)
        {
            if (!d.On || (s.NeedsPower && !d.Powered)) { d.Active = false; return; }
            var job = MachineJob(d, s);
            bool fire = d.Kind == DeployKind.Campfire;   // костёр горит и без работы — греет и светит
            if (job == null && !fire) { d.Active = false; d.Work = 0; return; }
            if (!Burn(d, s, Dt)) { d.Active = false; return; }
            d.Active = true;
            if (job == null) { d.Work = 0; return; }
            d.Work += Dt * (d.Powered ? s.PowerSpeed : 1f);
            int guard = 0;
            while (job != null && d.Work >= job.Time && guard++ < 50)
            {
                d.Work -= job.Time;
                CompleteJob(d, job);
                job = MachineJob(d, s);
            }
            if (job == null) d.Work = 0;
            if (s.Noise > 0 && Tick % 60 == 0) Noises.Add(new Noise { Pos = d.Pos, Radius = s.Noise, SourceId = d.OwnerId, Time = Time });
        }

        /// <summary>Узел, на котором стоит буровая.</summary>
        public ResourceNode DrillNode(Vector2 p, Vector2 min, Vector2 max)
        {
            ResourceNode best = null; float bd = float.MaxValue;
            foreach (var n in NodesNear(p, Prog.DrillNodeRange + 2f))
            {
                if (n.Kind is not (NodeKind.Stone or NodeKind.Metal or NodeKind.Sulfur)) continue;
                float dist = Vector2.Distance(n.Pos, Vector2.Clamp(n.Pos, min, max)) - n.Radius;
                if (dist <= Prog.DrillNodeRange && dist < bd) { bd = dist; best = n; }
            }
            return best;
        }

        void StepDrill(Deployable d, StationDef s)
        {
            var node = d.On ? DrillNode(d.Pos, d.Min, d.Max) : null;
            if (node == null || !Burn(d, s, Dt)) { d.Active = false; return; }
            d.Active = true;
            if ((d.Work += Dt) < Prog.DrillPeriod) return;
            d.Work = 0;
            if (!PutIn(d.Storage.Items, StorageCap(d), node.ResourceId, Prog.DrillYield)) { d.On = false; d.Active = false; return; }
            Noises.Add(new Noise { Pos = d.Pos, Radius = s.Noise, SourceId = d.OwnerId, Time = Time });
            Events.Add(new SimEvent { Kind = SimEventKind.HitNode, Pos = d.Pos, OtherId = -3 });   // −3 — звук буровой (клиент: drill_work)
        }

        /// <summary>Куда «смотрит» конвейер: поворот 0 — +X, 1 — +Y, 2 — −X, 3 — −Y.</summary>
        public static Vector2 ConveyorDir(int rot) => (rot & 3) switch { 0 => Vector2.UnitX, 1 => Vector2.UnitY, 2 => -Vector2.UnitX, _ => -Vector2.UnitY };

        Deployable StorageAt(Deployable self, Vector2 p)
        {
            Deployable best = null; float bd = float.MaxValue;
            foreach (var d in DeploysNear(p, Prog.ConveyorReach + 1.5f))
            {
                if (d == self || d.Kind is DeployKind.Conveyor or DeployKind.Recycler) continue;
                if (d.Storage == null && d.Kind != DeployKind.Furnace) continue;
                float dist = Vector2.Distance(p, Vector2.Clamp(p, d.Min, d.Max));
                if (dist <= Prog.ConveyorReach && dist < bd) { bd = dist; best = d; }
            }
            return best;
        }

        void StepConveyor(Deployable d, StationDef s)
        {
            d.Active = d.On && d.Powered;
            if (!d.Active || (d.Work += Dt) < Prog.ConveyorPeriod) return;
            d.Work = 0;
            var dir = ConveyorDir(d.Rot);
            var src = StorageAt(d, d.Pos - dir * 0.6f);
            var dst = StorageAt(d, d.Pos + dir * 0.6f);
            if (src == null || dst == null || src == dst) return;
            var filter = new HashSet<string>();
            foreach (var f in d.Storage.Items) filter.Add(f.Id);
            // что взять: из печи — готовое, из станка/ящика — первую подходящую стопку
            string id = null; int n = 0;
            if (src.Kind == DeployKind.Furnace)
            {
                foreach (var (fid, have) in new[] { ("metal_fragments", src.OutMetal), ("sulfur", src.OutSulfur), ("charcoal", src.OutCharcoal) })
                    if (have > 0 && (filter.Count == 0 || filter.Contains(fid))) { id = fid; n = Math.Min(have, Prog.ConveyorStack); break; }
            }
            else
            {
                var job = StationDefs.Get(src.Kind) is StationDef ss ? MachineJob(src, ss) : null;
                foreach (var it in src.Storage.Items)
                {
                    if (it.State != null || it.IsContainer || (filter.Count > 0 && !filter.Contains(it.Id))) continue;
                    if (job != null && Array.Exists(job.In, x => x.id == it.Id)) continue;   // сырьё станка не уносим
                    id = it.Id; n = Math.Min(it.Count, Prog.ConveyorStack); break;
                }
            }
            if (id == null || n <= 0) return;
            bool ok;
            if (dst.Kind == DeployKind.Furnace)
            {
                ok = true;
                if (id == "metal_ore") dst.Ore += n; else if (id == "sulfur_ore") dst.SulfurOre += n; else if (id == "wood") dst.Fuel += n; else ok = false;
            }
            else ok = PutIn(dst.Storage.Items, StorageCap(dst), id, n);
            if (!ok) return;
            if (src.Kind == DeployKind.Furnace)
            {
                if (id == "metal_fragments") src.OutMetal -= n; else if (id == "sulfur") src.OutSulfur -= n; else src.OutCharcoal -= n;
            }
            else TakeIn(src.Storage.Items, id, n);
        }

        // ======================================================================== взаимодействие

        /// <summary>E по станции: окно станка / ящика / бочки, крафт у столов, свет — вкл/выкл.</summary>
        void UseStation(Actor a, Deployable d)
        {
            var s = StationDefs.Get(d.Kind);
            if (s == null) return;
            switch (d.Kind)
            {
                case DeployKind.BoxLarge:
                    if (!a.IsBot) OpenUi(a, UiKind.Box, d); else UseBox(a, d);
                    return;
                case DeployKind.Barrel:
                    if (!a.IsBot) OpenUi(a, UiKind.Tank, d);
                    return;
                case DeployKind.Lamp or DeployKind.Floodlight:
                    d.On = !d.On;
                    Info(a, $"{s.Name}: {(d.On ? (d.Powered ? "горит" : "включена, но нет тока — нужен генератор рядом") : "выключена")}");
                    return;
                case DeployKind.Sewing or DeployKind.Chem or DeployKind.GunTable or DeployKind.MedTable:
                    if (!a.IsBot) { a.WantsCraftMenu = true; a.WantsCraftList = true; }
                    else Info(a, $"{s.Name}: рядом доступны его рецепты");
                    return;
            }
            if (!a.IsBot) OpenUi(a, UiKind.Recycler, d);
        }

        /// <summary>Подсказка при наведении.</summary>
        public string StationHint(Deployable d)
        {
            var s = StationDefs.Get(d.Kind);
            if (s == null) return null;
            string power = s.Power > 0 ? (d.Powered ? " · ток есть" : s.NeedsPower ? " · нет тока" : "") : "";
            return d.Kind switch
            {
                DeployKind.Barrel => Liquids.Tank(d) is var (lq, l) && lq != null ? $"[E] {s.Name}: {Items.Name(lq)} {l:0} л" : $"[E] {s.Name}: пусто",
                DeployKind.Lamp or DeployKind.Floodlight => $"[E] {s.Name} — {(d.On ? "выключить" : "включить")}{power}",
                DeployKind.Sewing or DeployKind.Chem or DeployKind.GunTable or DeployKind.MedTable => $"[E] {s.Name} — крафт{power}",
                _ => $"[E] {s.Name}{(d.Active ? " — работает" : "")}{power}",
            };
        }

        // ======================================================================== окно станции (переработчик-подобное)

        void BuildStationUi(Actor a, Deployable d, StationDef s, UiView v)
        {
            v.Title = s.Name.ToUpper();
            v.Machine = s.Machine ?? d.Kind.ToString().ToLower();
            int cap = StorageCap(d);
            for (int i = 0; i < cap; i++) v.Cells.Add(i < d.Storage.Items.Count ? Cell(d.Storage.Items[i]) : new CellView());
            var job = MachineJob(d, s);
            v.On = d.On; v.Burning = d.Active;
            v.Making = d.Active ? job?.Output : null;
            if (v.Making != null) v.Progress = Math.Clamp(d.Work / job.Time, 0, 1);
            foreach (var r in StationDefs.RecipesFor(s.Machine))
            {
                var inp = new List<string>();
                foreach (var (id, n) in r.In) inp.Add($"{Items.Name(id)} {n}");
                v.Makes.Add((r.Output, string.Join(" + ", inp), true));
            }
            string fuel = s.Burn == null ? "" : $" Топливо: {Items.Name(s.Burn)} ({s.BurnPerMin:0.##}{(Liquids.Is(s.Burn) ? " л" : " шт.")}/мин) — в ячейки{(Liquids.Is(s.Burn) ? " (канистрой) или бочка 200 л рядом" : "")}.";
            string power = s.NeedsPower ? (d.Powered ? " Ток есть." : " НЕТ ТОКА — нужен дизель-генератор рядом (15 м).") : s.Power > 0 ? (d.Powered ? $" Под током — быстрее ×{s.PowerSpeed:0.##}." : "") : "";
            v.Info = d.Kind switch
            {
                DeployKind.Generator => (d.Active ? "Работает: даёт ток станциям в 15 м." : "Стоит: положи канистру с топляком (или поставь бочку 200 л рядом) и нажми «Запустить».") + fuel,
                DeployKind.Drill => (DrillNode(d.Pos, d.Min, d.Max) is ResourceNode n ? $"Бурит: {Items.Name(n.ResourceId)} {Prog.DrillYield} за {Prog.DrillPeriod:0} с." : "Рядом нет рудного/серного/каменного узла — поставь вплотную.") + fuel,
                DeployKind.Conveyor => "Под током раз в 2 с переносит стопку из хранилища сзади в хранилище спереди (стрелка — R при установке). Положи сюда образцы — будет носить только их." + power,
                _ => (d.Active ? (job != null ? $"Работает: {Items.Name(job.Output)}." : "Горит.") : d.On ? "Ждёт материалов." : "Остановлена.") + fuel + power,
            };
        }

        // ======================================================================== вода, топливо у источников

        /// <summary>Источник жидкости рядом для «Налить»: бочка 200 л, бочка с водой, бочка с мазутом, ржавые канистры, водоём.</summary>
        (string liquid, float max, object src) LiquidSource(Actor a)
        {
            foreach (var d in DeploysNear(a.Pos, InteractRange + 1.5f))
                if (d.Kind == DeployKind.Barrel && Vector2.Distance(a.Pos, Vector2.Clamp(a.Pos, d.Min, d.Max)) < InteractRange && Liquids.Tank(d) is var (lq, l) && lq != null) return (lq, l, d);
            foreach (var p in Pickups)
            {
                if (!p.Available || Vector2.Distance(a.Pos, p.Pos) > InteractRange + 0.4f) continue;
                if (p.Kind == PickupKind.WaterBarrel) return ("water_raw", 1000, p);
                if (p.Kind == PickupKind.OilBarrel) return ("crude", Rng.Next(Prog.OilBarrel.min, Prog.OilBarrel.max + 1), p);
                if (p.Kind == PickupKind.CanisterPile) return ("crude", Rng.Next(Prog.CanisterPile.min, Prog.CanisterPile.max + 1), p);
            }
            for (int k = 0; k < 12; k++)
            {
                float ang = k * MathF.PI / 6;
                var q = a.Pos + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * Prog.ShoreFillRange;
                if (!Terrain.IsLand(q) || !Terrain.IsLand(a.Pos)) return ("water_raw", 1000, null);
            }
            return (null, 0, null);
        }

        /// <summary>Налить в ёмкость из источника рядом.</summary>
        void FillContainer(Actor a, ItemSlot c)
        {
            if (c == null || !c.IsContainer) return;
            var (liquid, max, src) = LiquidSource(a);
            if (liquid == null) { Info(a, "Налить неоткуда: подойди к бочке с водой, водоёму, бочке с мазутом, ржавым канистрам или своей бочке 200 л"); return; }
            if (!Liquids.Fits(c, liquid)) { Info(a, c.Def.WaterOnly ? $"{c.Def.Name}: только для воды" : $"{c.Def.Name}: уже налито {Items.Name(c.Liquid)} — сначала вылей"); return; }
            float put = Math.Min(Liquids.Room(c), max);
            if (put <= 0.001f) { Info(a, $"{c.Def.Name}: полна"); return; }
            if (src is Deployable tank) put = Liquids.TankDrain(tank, liquid, put);
            c.Liquid = liquid; c.Liters += put;
            if (src is Pickup p && p.Kind is PickupKind.OilBarrel or PickupKind.CanisterPile)
                p.RespawnLeft = p.Kind == PickupKind.OilBarrel ? Prog.OilBarrel.respawn : Prog.CanisterPile.respawn;
            Events.Add(new SimEvent { Kind = SimEventKind.Gather, ActorId = a.Id, Pos = a.Pos, Value = put, Text = Items.Name(liquid) });
            Info(a, $"Налито: {Items.Name(liquid)} {put:0.#} л → {Liquids.Label(c)}");
        }

        /// <summary>E по бочке с мазутом / ржавым канистрам: налить нефть в первую подходящую ёмкость.</summary>
        void CollectCrude(Actor a, Pickup p)
        {
            ItemSlot c = null;
            foreach (var s in a.Pack) if (Liquids.Fits(s, "crude") && Liquids.Room(s) > 0.01f && (c == null || s.Liquid == "crude")) c = s;
            if (c == null) foreach (var s in a.Belt) if (Liquids.Fits(s, "crude") && Liquids.Room(s) > 0.01f) { c = s; break; }
            if (c == null) { Info(a, "Нефть не во что набрать: нужна пустая канистра (верстак 1: металл 40, резина 2)"); return; }
            FillContainer(a, c);
        }

        /// <summary>Выпить из ёмкости (поллитра). Сырая вода — шанс отравиться.</summary>
        bool DrinkFrom(Actor a, ItemSlot c)
        {
            if (c?.Liquid == null || !Liquids.IsWater(c.Liquid) || c.Liters <= 0.01f) { Info(a, "Пить нечего: налей воды"); return false; }
            float l = Math.Min(Prog.SipLiters, c.Liters);
            bool raw = c.Liquid == "water_raw";
            c.Liters -= l;
            if (c.Liters <= 0.01f) { c.Liters = 0; c.Liquid = null; }
            a.Water = MathF.Min(100, a.Water + Prog.WaterPerSip * l / Prog.SipLiters);
            if (raw && Rng.NextDouble() < Prog.RawPoisonChance)
            {
                a.Poison = Prog.PoisonTime;
                Info(a, "Сырая вода: тебя мутит — отравление (аптечка или шприц снимут)");
            }
            else Info(a, raw ? "Выпил сырой воды — повезло" : "Выпил кипячёной воды");
            Events.Add(new SimEvent { Kind = SimEventKind.Wear, ActorId = a.Id, Pos = a.Pos, Text = "drink" });   // звук (клиент: drink → rummage)
            return true;
        }

        /// <summary>Ёмкость с водой для F: кипячёная, потом сырая (если пить больше нечего).</summary>
        ItemSlot WaterToDrink(Actor a, bool allowRaw)
        {
            ItemSlot best = null;
            for (int k = 0; k < Actor.PackSize + Actor.BeltSize; k++)
            {
                var s = k < Actor.PackSize ? a.Pack[k] : a.Belt[k - Actor.PackSize];
                if (s?.Liquid == null || s.Liters <= 0.01f) continue;
                if (s.Liquid == "water_boiled") return s;
                if (s.Liquid == "water_raw" && allowRaw) best ??= s;
            }
            return best;
        }

        /// <summary>Отравление: вода и здоровье уходят.</summary>
        void StepPoison(Actor a)
        {
            if (a.Poison <= 0) return;
            a.Poison -= Dt;
            a.Water = MathF.Max(0, a.Water - Prog.PoisonWaterPerSec * Dt);
            a.Hp -= Prog.PoisonHpPerSec * Dt;
            if (a.Hp <= 0) Kill(a, -1);
        }

        /// <summary>Инструмент на топливе: за удар сливает литры из ёмкостей; без топлива бьёт вчетверо слабее.</summary>
        float ToolFuelMul(Actor a)
        {
            var d = a.Held?.Def;
            if (d?.FuelId == null || d.FuelPerHit <= 0) return 1f;
            float got = Liquids.Drain(a.Pack, d.FuelId, d.FuelPerHit);
            if (got < d.FuelPerHit - 1e-4f) got += Liquids.Drain(a.Belt, d.FuelId, d.FuelPerHit - got);
            if (got >= d.FuelPerHit - 1e-4f) return 1f;
            Info(a, $"{d.Name}: нет топлива ({Items.Name(d.FuelId)} в канистре) — еле берёт");
            return 0.25f;
        }

        /// <summary>H: лечиться лучшим подходящим — аптечка при сильном ранении, шприц при среднем, бинт при лёгком (что есть).</summary>
        void Heal(Actor a)
        {
            if (a.HealCooldown > 0) return;
            float missing = a.MaxHp - a.Hp;
            if (missing <= 0.5f && a.Poison <= 0) return;
            string[] order = missing >= 50 ? new[] { "medkit", "syringe", "bandage" } : missing >= 25 || a.Poison > 0 ? new[] { "syringe", "medkit", "bandage" } : new[] { "bandage", "syringe", "medkit" };
            foreach (var id in order)
            {
                if (a.Count(id) <= 0) continue;
                var def = Items.Get(id);
                a.Take(id, 1);
                a.Hp = MathF.Min(a.MaxHp, a.Hp + def.Heal * HealMul(a));
                if (def.CuresPoison) a.Poison = 0;
                float cd = 2f;
                foreach (var m in Prog.Meds) if (m.id == id) cd = m.cooldown;
                a.HealCooldown = cd;
                if (id != "bandage") Info(a, $"{def.Name}: +{def.Heal:0}");
                return;
            }
        }

        // ======================================================================== нефть в мире, насос Заправки

        /// <summary>Бочки с мазутом и ржавые канистры по острову (свой генератор — раскладка мира не сдвигается). Зовётся в самом конце BuildMap.</summary>
        void PlaceFuelSpots()
        {
            var rng = new Random(Seed * 977 + 41);
            int oil = 0, cans = 0, oilN = (int)(Prog.OilBarrelsWorld * AreaScale), cansN = (int)(Prog.CanisterPilesWorld * AreaScale);
            for (int tries = 0; tries < 14000 && (oil < oilN || cans < cansN); tries++)
            {
                var p = new Vector2((float)(rng.NextDouble() * 2 - 1) * HalfSize * 0.95f, (float)(rng.NextDouble() * 2 - 1) * HalfSize * 0.95f);
                if (!Terrain.IsLand(p) || NearMonument(p, 20) || !IsFree(p, 1.0f)) continue;
                var b = Terrain.BiomeAt(p);
                if (b is not (Biome.Beach or Biome.Arid or Biome.Grassland)) continue;
                if (oil < oilN && (cans >= cansN || rng.NextDouble() < 0.57)) { AddPickup(PickupKind.OilBarrel, p); oil++; }
                else { AddPickup(PickupKind.CanisterPile, p); cans++; }
            }
            // у каждой РТ — пара бочек с мазутом за оградой
            foreach (var m in Monuments)
                for (int k = 0, put = 0; k < 40 && put < 2; k++)
                {
                    var c = (m.Min + m.Max) * 0.5f;
                    float ang = (float)(rng.NextDouble() * MathF.Tau);
                    float r = MathF.Max(m.Max.X - m.Min.X, m.Max.Y - m.Min.Y) * 0.5f + 4 + (float)rng.NextDouble() * 6;
                    var p = c + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r;
                    if (!Terrain.IsLand(p) || NearMonument(p, 2) || !IsFree(p, 1.0f)) continue;
                    AddPickup(put == 0 ? PickupKind.OilBarrel : PickupKind.CanisterPile, p);
                    put++;
                }
        }

        /// <summary>Насос подземной цистерны Заправки: держать E — топляк в ёмкости; дальше пусто до обновления РТ.</summary>
        void AddFuelPump(Tier0Rt rt, Vector2 at)
        {
            Decor("props/fuel_hand_pump", at);   // картинка без хитбокса: стены РТ и их номера не сдвигаются
            rt.Stashes.Add(new Stash
            {
                Pos = at + new Vector2(0, 0.8f), Verb = "Качать топляк насосом подземной цистерны", Sound = "pump", Seconds = Prog.FuelPumpSeconds, Noise = Prog.FuelPumpNoise,
                Require = a => Liquids.RoomFor(a.Pack, "fuel") + Liquids.RoomFor(a.Belt, "fuel") < 1 ? "Топляк не во что качать: нужна пустая канистра (верстак 1)" : null,
                OnDone = a =>
                {
                    float want = Rng.Next(Prog.FuelPump.min, Prog.FuelPump.max + 1);
                    float put = Liquids.Pour(a.Pack, "fuel", want);
                    if (put < want) put += Liquids.Pour(a.Belt, "fuel", want - put);
                    Info(a, $"Накачал топляка: {put:0.#} л. Цистерна пуста до обновления РТ");
                    Events.Add(new SimEvent { Kind = SimEventKind.Gather, ActorId = a.Id, Pos = at, Value = put, Text = "Топляк" });
                    rt.RefreshLeft = Tier0Rt.RefreshTime;
                },
            });
        }

        // ======================================================================== ёмкости в инвентаре

        /// <summary>Перетаскивание ёмкостей: на другую ёмкость — перелить, в бочку — вылить в неё, из бочки — налить. true — обработано.</summary>
        bool LiquidMove(Actor a, string src, int si, ItemSlot s, string dst, int di)
        {
            if (dst == "tank")
            {
                if (s == null || !s.IsContainer || s.Liquid == null || a.OpenTarget is not Deployable tank) return src != "tank";
                float t = Liquids.TankPour(tank, s.Liquid, s.Liters);
                if (t <= 0) { Info(a, "В бочке другая жидкость или она полна"); return true; }
                s.Liters -= t;
                if (s.Liters <= 0.001f) { s.Liters = 0; s.Liquid = null; }
                Info(a, $"В бочку: {Items.Name(Liquids.Tank(tank).liquid)} {t:0.#} л");
                Events.Add(new SimEvent { Kind = SimEventKind.Wear, ActorId = a.Id, Pos = a.Pos, Text = "pour_liquid" });
                return true;
            }
            if (src == "tank")
            {
                var d = GetCell(a, dst, di);
                if (a.OpenTarget is not Deployable tank || s == null) return true;
                if (d == null || !Liquids.Fits(d, s.Id)) { Info(a, "Налить можно только в ёмкость (фляга — только вода)"); return true; }
                float t = Liquids.TankDrain(tank, s.Id, Liquids.Room(d));
                if (t > 0) { d.Liquid = s.Id; d.Liters += t; Info(a, $"Налито из бочки: {t:0.#} л → {Liquids.Label(d)}"); }
                return true;
            }
            if (s == null || !s.IsContainer || s.Liquid == null || src is not ("pack" or "belt" or "cont") || dst is not ("pack" or "belt" or "cont")) return false;
            var to = GetCell(a, dst, di);
            if (to == null || to == s || !to.IsContainer || !Liquids.Fits(to, s.Liquid)) return false;
            float moved = Liquids.Transfer(s, to);
            Info(a, moved > 0 ? $"Перелито {moved:0.#} л → {Liquids.Label(to)}" : $"{to.Def.Name}: полна");
            return true;
        }

        /// <summary>Кнопки карточки ёмкости: налить у источника, вылить, выпить.</summary>
        void ContainerOp(Actor a, UiOp op, string arg)
        {
            var (inv, i) = ParseCell(arg);
            if (inv is not ("pack" or "belt")) return;
            var c = GetCell(a, inv, i);
            if (c == null || !c.IsContainer) return;
            switch (op)
            {
                case UiOp.Fill: FillContainer(a, c); break;
                case UiOp.Drink: DrinkFrom(a, c); break;
                case UiOp.Empty:
                    if (c.Liquid == null) { Info(a, "Уже пусто"); break; }
                    Info(a, $"Вылито: {Items.Name(c.Liquid)} {c.Liters:0.#} л");
                    c.Liquid = null; c.Liters = 0;
                    break;
            }
        }

        /// <summary>Починка из карточки: ствол — маслом у верстака/оружейного стола, бронепластина — у швейного стола.</summary>
        void RepairCell(Actor a, ItemSlot s)
        {
            if (s == null) return;
            if (s.State != null) { Repair(a, s.State); return; }
            if (Clothing.Plate(s.Id) is not PlateDef pd) return;
            if (StationNear(a.Pos, DeployKind.Sewing) == null) { Info(a, "Пластину чинят у швейного стола"); return; }
            if (s.Dur < 0 || s.Dur >= pd.Durability - 0.5f) { Info(a, $"{Items.Name(s.Id)} целая"); return; }
            foreach (var (id, n) in Prog.PlateRepairCost) if (a.Count(id) < n) { Info(a, $"На починку нужно: {string.Join(", ", Array.ConvertAll(Prog.PlateRepairCost, c => $"{Items.Name(c.Item1)} {c.Item2}"))}"); return; }
            foreach (var (id, n) in Prog.PlateRepairCost) a.Take(id, n);
            s.Dur = pd.Durability;
            Info(a, $"{Items.Name(s.Id)} починена");
        }

        // ======================================================================== решётки и ставни

        Structure NearestShutter(Vector2 p, float range)
        {
            Structure best = null; float bd = range;
            foreach (var s in Structures)
            {
                if (s.Kind != StructKind.Window || !s.Shutters) continue;
                float d = Building.SegDist(p, s.A, s.B);
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        /// <summary>ЛКМ решёткой по окну своей базы: решётка со ставнями (ставни сразу закрыты).</summary>
        void InstallBars(Actor a)
        {
            var w = TargetStructure(a, 2f);
            if (w == null || w.Kind != StructKind.Window) { Info(a, "Решётку — на окно: наведи на окно своей базы"); return; }
            if (w.Shutters) { Info(a, "На окне уже стоят ставни"); return; }
            if (ClaimAt(Building.CellOf(w.Center)) is Deployable c && !c.Auth.Contains(a.Id)) { Info(a, "Чужая база — нужен доступ к шкафу"); return; }
            w.Shutters = true;
            w.Open = true;
            RebuildBoxes(w);
            StructVersion++;
            var held = a.Held;
            if (--held.Count <= 0) a.Belt[a.Current] = null;
            Events.Add(new SimEvent { Kind = SimEventKind.Built, ActorId = a.Id, OtherId = w.Id, Pos = w.Center, Text = "ставни" });
            Info(a, "Решётка со ставнями: E у окна — открыть/закрыть");
        }

        void ToggleShutters(Actor a, Structure w)
        {
            if (ClaimAt(Building.CellOf(w.Center)) is Deployable c && !c.Auth.Contains(a.Id)) { Info(a, "Ставни заперты изнутри"); return; }
            w.Open = !w.Open;
            RebuildBoxes(w);
            StructVersion++;
            Events.Add(new SimEvent { Kind = SimEventKind.Door, ActorId = a.Id, OtherId = w.Id, Pos = w.Center, Value = w.Open ? 0 : 1 });
        }

        // ======================================================================== бочка 200 л: окно

        UiView BuildTankUi(Deployable d)
        {
            var v = new UiView { Kind = UiKind.Tank, Title = "БОЧКА 200 Л" };
            var (lq, l) = Liquids.Tank(d);
            v.Cells.Add(lq == null ? new CellView { Label = "пусто" } : new CellView { Id = lq, Name = Items.Name(lq), Count = (int)l, Rarity = Items.Rarity(lq), Label = $"{l:0} / {Liquids.TankCap(d):0} л", Durability = l / Liquids.TankCap(d) });
            v.Info = lq == null ? "Пусто. Перетащи сюда канистру — перельётся; бочка держит одну жидкость" : $"{Items.Name(lq)}: {l:0} из {Liquids.TankCap(d):0} л. Перетащи канистру сюда — долить; отсюда на ёмкость в рюкзаке — налить. Станции в 3 м берут топливо отсюда";
            return v;
        }
    }
}
