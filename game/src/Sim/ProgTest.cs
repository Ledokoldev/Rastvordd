using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Самопроверка прогрессии D54 (отладка: --simtest=N --progtest): сталь и оружейный стол, перегонка, ток и электропечь,
    /// ёмкости и вода, бензопила, буровая, конвейер, бронеуровень, сохранение жидкостей. Отладочный мир, сейв не трогает.
    /// </summary>
    public sealed partial class World
    {
        public void ProgSelfTest(Action<string> log)
        {
            var a = new Actor { Id = NextId(), Name = "Тест-прогрессия", Team = Team.Player };
            Actors.Add(a);
            a.Pos = PlayerSpawns[0];
            int ok = 0, bad = 0;
            void Check(bool cond, string what) { if (cond) ok++; else bad++; log($"ПРОГ-ТЕСТ {(cond ? "OK " : "ПРОВАЛ")} {what}"); }
            void Run(float seconds) { var none = new Dictionary<int, InputCmd>(); for (int t = 0; t < (int)(seconds / Dt); t++) { a.Hp = a.MaxHp; a.Food = a.Water = 80; Step(none); } }
            Deployable Place(DeployKind k, Vector2 at)
            {
                var d = new Deployable { Id = NextId(), Kind = k, Pos = at, OwnerId = a.Id, Hp = 1e9f, Half = Footprints.HalfOf(k, 0) };
                d.Storage = new LootBag { Id = NextId(), Pos = at };
                d.Auth.Add(a.Id);
                AddDeployable(d);
                return d;
            }
            var o = a.Pos + new Vector2(0, 40);   // площадка в стороне от спавна
            a.Pos = o;

            // 1. предметы и рецепты
            Check(Items.Get(Prog.Alloy) != null && Items.Get("deploy_guntable")?.Deploy == DeployKind.GunTable && Items.Get("canister_20")?.Capacity == 20, "предметы D54 заведены (сталь, оружейный стол, канистра 20 л)");
            var akm = Recipes.All.Find(r => r.Output == "weapon_akm");
            Check(akm.Station == DeployKind.GunTable && Array.Exists(akm.Cost, c => c.id == Prog.Alloy), "АКМ — на оружейном столе, со сталью");
            Check(Array.Exists(Recipes.All.Find(r => r.Output == "deploy_workbench3").Cost, c => c.id == Prog.Alloy && c.count == 25), "верстак 3: 25 стали");

            // 2. оружейный стол
            a.ClearAll();
            a.Known.Add("weapon_akm");
            foreach (var (id, n) in akm.Cost) a.Add(id, n);
            Check(!CanCraft(a, akm), "АКМ без оружейного стола — нельзя");
            var gt = Place(DeployKind.GunTable, o + new Vector2(2, 0));
            Check(CanCraft(a, akm), "АКМ у оружейного стола (без верстака 3) — можно");
            QueueCraft(a, akm);
            Run(akm.Time + 1);
            Check(a.Count("weapon_akm") == 0 && FindBelt(a, "weapon_akm") != null || HasSlot(a, "weapon_akm"), "АКМ скрафчен на столе");
            RemoveDeploy(gt);

            // 3. жидкости в рюкзаке
            a.ClearAll();
            a.Add("fuel", 7);
            Check(a.Count("fuel") == 7, $"топляк 7 л → в канистре, Count = {a.Count("fuel")}");
            Check(a.Take("fuel", 3) == 3 && a.Count("fuel") == 4, "слито 3 л, осталось 4");
            var flask = Liquids.Make("flask_1", null, 0);
            a.AddSlot(flask);
            Check(!Liquids.Fits(flask, "fuel"), "фляга не берёт топливо");

            // 4. самогонный аппарат: нефть → топляк на дровах
            var still = Place(DeployKind.Still, o + new Vector2(-3, 0));
            still.Storage.Items.Add(Liquids.Make("canister_20", "crude", 6));
            still.Storage.Items.Add(Liquids.Make("canister_5", null, 0));
            still.Storage.Items.Add(new ItemSlot("wood", 20));
            Run(50);
            float fuel = Liquids.Total(still.Storage.Items, "fuel"), crude = Liquids.Total(still.Storage.Items, "crude");
            Check(fuel >= 3 - 0.01f && crude <= 0.01f, $"аппарат: нефть 6 л → топляк {fuel:0.#} л (нефти осталось {crude:0.#}), дров {CountIn(still.Storage.Items, "wood")}");

            // 5. ток: генератор → электропечь плавит руду
            var ef = Place(DeployKind.EFurnace, o + new Vector2(0, 6));
            ef.Storage.Items.Add(new ItemSlot("metal_ore", 50));
            Run(3);
            Check(CountIn(ef.Storage.Items, "metal_fragments") == 0, "электропечь без тока не плавит");
            var gen = Place(DeployKind.Generator, o + new Vector2(4, 6));
            gen.Storage.Items.Add(Liquids.Make("canister_5", "fuel", 5));
            Run(12);
            int metal = CountIn(ef.Storage.Items, "metal_fragments");
            Check(ef.Powered && metal >= 40, $"под током электропечь: руда → металл {metal} за ~10 с");
            Check(Liquids.Total(gen.Storage.Items, "fuel") < 5, $"генератор жжёт топляк: осталось {Liquids.Total(gen.Storage.Items, "fuel"):0.##} л");

            // 6. вода: сырая — шанс отравиться; кипячёная — нет
            a.ClearAll();
            var raw = Liquids.Make("canister_5", "water_raw", 5);
            a.AddSlot(raw);
            int poisoned = 0;
            for (int k = 0; k < 10; k++) { a.Poison = 0; a.Water = 10; DrinkFrom(a, raw); if (a.Poison > 0) poisoned++; }
            Check(poisoned > 0 && poisoned < 10, $"сырая вода: отравился {poisoned} из 10 (≈30%)");
            var fire = Place(DeployKind.Campfire, o + new Vector2(-6, 6));
            fire.Storage.Items.Add(Liquids.Make("flask_1", "water_raw", 1));
            fire.Storage.Items.Add(new ItemSlot("wood", 10));
            Run(12);
            Check(Liquids.Total(fire.Storage.Items, "water_boiled") >= 0.99f, "костёр: фляга сырой воды → кипячёная");
            var boiled = Liquids.Make("flask_1", "water_boiled", 1);
            a.Poison = 0; a.Water = 10;
            DrinkFrom(a, boiled);
            Check(a.Poison <= 0 && a.Water > 40, $"кипячёная вода: вода {a.Water:0}, без отравления");
            a.Add("syringe", 1); a.Poison = 30; a.Hp = 50; a.HealCooldown = 0;
            Heal(a);
            Check(a.Poison <= 0 && a.Hp > 50, "шприц снял отравление и подлечил");

            // 7. бензопила: топляк за удар
            a.ClearAll();
            a.Belt[0] = Items.NewSlot("tool_chainsaw"); a.Current = 0;
            a.Add("fuel", 2);
            float m1 = ToolFuelMul(a);
            Check(m1 == 1f && a.Count("fuel") == 1, $"бензопила с топливом: ×{m1}, топляка {Liquids.Total(a.Pack, "fuel"):0.##} л");
            Liquids.Drain(a.Pack, "fuel", 10);
            Check(ToolFuelMul(a) < 0.5f, "бензопила без топлива — еле берёт");

            // 8. буровая на узле
            ResourceNode node = null;
            foreach (var n in Nodes) if (n.Kind == NodeKind.Metal) { node = n; break; }
            if (node != null)
            {
                var half = Footprints.HalfOf(DeployKind.Drill, 0);
                var drill = Place(DeployKind.Drill, node.Pos + new Vector2(node.Radius + half.X + 0.3f, 0));
                drill.Storage.Items.Add(Liquids.Make("canister_5", "diesel", 5));
                Run(Prog.DrillPeriod * 2 + 0.5f);
                Check(CountIn(drill.Storage.Items, "metal_ore") >= Prog.DrillYield, $"буровая: руды {CountIn(drill.Storage.Items, "metal_ore")} за {Prog.DrillPeriod * 2:0} с");
                RemoveDeploy(drill);
            }

            // 9. конвейер: ящик → ящик под током
            var b1 = Place(DeployKind.Box, o + new Vector2(8.5f, 0));
            var conv = Place(DeployKind.Conveyor, o + new Vector2(9.5f, 0));
            var b2 = Place(DeployKind.Box, o + new Vector2(10.5f, 0));
            b1.Storage.Items.Add(new ItemSlot("stone", 50));
            var gen2 = Place(DeployKind.Generator, o + new Vector2(9.5f, 4));
            gen2.Storage.Items.Add(Liquids.Make("canister_5", "fuel", 5));
            Run(5);
            Check(CountIn(b2.Storage.Items, "stone") > 0, $"конвейер перенёс камень: {CountIn(b2.Storage.Items, "stone")}");

            // 10. переработка и лут
            Check(Items.RecycleYield("comp_receiver_ak").Exists(y => y.id == Prog.Alloy), "переработка ствольной коробки даёт сталь");
            var ev = new LootBag();
            StationDefs.Loot(new Random(1), ev, "event");
            Check(ev.Items.Exists(s => s.Id == Prog.Alloy), "ящик ивента: сталь гарантированно");

            // 11. бронеуровень
            Check(Building.MaxTier == 5 && Array.Exists(Building.Cost(StructKind.Wall, 5), c => c.id == Prog.Alloy), "стена: 5-й уровень «броня» за сталь");
            Check(!Array.Exists(Building.UpkeepCost(StructKind.Wall, 5), c => c.id == Prog.Alloy), "содержание брони — без стали (металл, лом)");

            // 11б. ставни: закрытое окно — стена во всю высоту, пули не проходят
            var closed = Building.Spans(StructKind.Window, true).Find(x => x.t0 > 0.2f && x.t1 < 0.8f);
            var openW = Building.Spans(StructKind.Window, false).Find(x => x.t0 > 0.2f && x.t1 < 0.8f);
            Check(closed.h >= Building.WallH && !closed.pass && openW.pass, "ставни: закрытое окно — как стена, открытое — подоконник");

            // 12. сохранение жидкостей и станций
            still.On = false;
            var sv = Save();
            var w2 = new World(Seed);
            w2.Load(WorldSave.FromJson(sv.ToJson()));
            var still2 = w2.Deployables.Find(d => d.Id == still.Id);
            Check(still2 != null && !still2.On && Math.Abs(Liquids.Total(still2.Storage.Items, "fuel") - fuel) < 0.01f, "сейв: топляк в канистре и «выключено» у станции сохранились");

            RemoveDeploy(still); RemoveDeploy(ef); RemoveDeploy(gen); RemoveDeploy(fire); RemoveDeploy(b1); RemoveDeploy(conv); RemoveDeploy(b2); RemoveDeploy(gen2);
            Actors.Remove(a);
            log($"ПРОГ-ТЕСТ итог: {ok} OK, {bad} провалов");
        }

        static bool HasSlot(Actor a, string id)
        {
            foreach (var s in a.Belt) if (s?.Id == id) return true;
            foreach (var s in a.Pack) if (s?.Id == id) return true;
            return false;
        }

        void RemoveDeploy(Deployable d) => RemoveDeployable(d);
    }
}
