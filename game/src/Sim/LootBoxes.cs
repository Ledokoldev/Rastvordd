using System;
using System.Collections.Generic;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Тематические ящики (docs/design/loot-tables.md): вид ящика (LootBag.Box) задаёт и спрайт (art/loot/loot_crate_&lt;вид&gt;), и пул лута;
    /// какие виды стоят на РТ — по теме РТ (LootBag.Rt) и тиру. Вид выбирается при первом наполнении и дальше не меняется.
    /// Строка пула: шанс по тирам 0/1/2 и количество на тире 0 (тир 1 — ×1,6, тир 2 — ×2,2).
    /// </summary>
    public sealed partial class World
    {
        public static readonly string[] BoxKinds = { "wood", "food", "medical", "tools", "electric", "ammo", "army", "weapon", "elite" };

        public static string BoxName(string box) => box switch
        {
            "wood" => "Бытовой ящик", "food" => "Продуктовый ящик", "medical" => "Медицинский ящик", "tools" => "Ящик с инструментом",
            "electric" => "Ящик электрики", "ammo" => "Ящик патронов", "army" => "Армейский ящик", "weapon" => "Оружейный ящик", "elite" => "Ящик с ценностями",
            _ => "Ящик",
        };

        /// <summary>Какие ящики стоят на РТ (ключ темы → вес вида). Армейский и оружейный — только тир 1+, ценности — только тир 1+.</summary>
        public static readonly Dictionary<string, (string box, int w)[]> RtBoxes = new()
        {
            ["t0"] = new[] { ("wood", 5), ("food", 2), ("medical", 1), ("tools", 2), ("electric", 1) },          // без темы, тир 0 (придорожные)
            ["t1"] = new[] { ("wood", 2), ("tools", 3), ("ammo", 2), ("medical", 1), ("army", 1), ("electric", 1) },
            ["t2"] = new[] { ("army", 4), ("weapon", 2), ("ammo", 2), ("elite", 1), ("medical", 1) },
            ["gai"] = new[] { ("wood", 3), ("ammo", 2), ("tools", 1), ("medical", 1) },                          // Пост ГАИ
            ["shop"] = new[] { ("food", 5), ("wood", 2), ("medical", 1) },                                       // Сельмаг
            ["fuel"] = new[] { ("tools", 4), ("wood", 3), ("food", 1) },                                         // Заправка
            ["weigh"] = new[] { ("wood", 3), ("tools", 3) },                                                     // Весовая
            ["ration"] = new[] { ("food", 4), ("medical", 3), ("wood", 1), ("ammo", 1) },                        // Раздаточная «Сухой паёк»
            ["post"] = new[] { ("wood", 3), ("electric", 2), ("elite", 1), ("medical", 1) },                     // Почтовый узел
            ["boiler"] = new[] { ("tools", 4), ("wood", 2), ("electric", 1) },                                   // Котельная
            ["seeds"] = new[] { ("food", 3), ("medical", 3), ("tools", 1) },                                     // Станция семян
            ["garages"] = new[] { ("tools", 5), ("wood", 2), ("ammo", 1) },                                      // Гаражи
            ["ats"] = new[] { ("electric", 5), ("tools", 1), ("elite", 1) },                                     // АТС
            ["depot"] = new[] { ("tools", 4), ("wood", 2), ("ammo", 1), ("army", 1) },                           // Депо
            ["quarry"] = new[] { ("ammo", 4), ("tools", 3), ("wood", 1) },                                       // Карьер
            ["substation"] = new[] { ("electric", 5), ("tools", 2) },                                            // Подстанция
            ["warehouse"] = new[] { ("army", 4), ("weapon", 3), ("ammo", 2), ("elite", 1), ("medical", 1) },     // Склад «Центральный»
            ["radio"] = new[] { ("army", 3), ("electric", 2), ("weapon", 2), ("elite", 2), ("ammo", 1) },        // Радиоцентр «Маяк»
        };

        /// <summary>Ключ темы малого РТ по названию (Monuments.SmallMonuments).</summary>
        public static string SmallRtKey(string name) =>
            name.StartsWith("Весовая") ? "weigh" : name.StartsWith("Раздаточная") ? "ration" : name.StartsWith("Почтовый") ? "post"
            : name.StartsWith("Котельная") ? "boiler" : name.StartsWith("Станция семян") ? "seeds" : null;

        static bool BoxAllowed(string box, int tier) => tier >= 1 || box is not ("army" or "weapon" or "elite");

        /// <summary>Веса видов ящика для РТ и тира (тема РТ, иначе общий набор тира), только разрешённые на этом тире.</summary>
        public static List<(string box, int w)> BoxWeights(string rt, int tier)
        {
            var table = rt != null && RtBoxes.TryGetValue(rt, out var t) ? t : RtBoxes["t" + Math.Clamp(tier, 0, 2)];
            var list = new List<(string, int)>();
            foreach (var (b, w) in table) if (BoxAllowed(b, tier)) list.Add((b, w));
            if (list.Count == 0) foreach (var (b, w) in RtBoxes["t" + Math.Clamp(tier, 0, 2)]) if (BoxAllowed(b, tier)) list.Add((b, w));
            return list;
        }

        readonly HashSet<string> _rtMainBox = new();   // РТ, где уже стоит ящик основной темы

        string PickBox(LootBag bag)
        {
            var list = BoxWeights(bag.Rt, bag.Tier);
            // первый ящик РТ — всегда основной темы (самый весомый вид), остальные — по весам: тема читается даже при 2–3 ящиках
            if (bag.Rt != null && RtBoxes.ContainsKey(bag.Rt) && _rtMainBox.Add(bag.Rt + "/" + bag.Tier))
            {
                var top = list[0];
                foreach (var x in list) if (x.w > top.w) top = x;
                return top.box;
            }
            int sum = 0; foreach (var (_, w) in list) sum += w;
            int r = Rng.Next(sum);
            foreach (var (b, w) in list) { if (r < w) return b; r -= w; }
            return list[0].box;
        }

        /// <summary>Пул вида ящика: (id или @-жетон, шанс тир 0, тир 1, тир 2, мин, макс на тире 0).</summary>
        static readonly Dictionary<string, (string id, double c0, double c1, double c2, int min, int max)[]> BoxPools = new()
        {
            ["wood"] = new[]
            {
                ("scrap", .85, .85, .85, 3, 8), ("cloth", .5, .45, .4, 10, 25), ("metal_fragments", .35, .5, .5, 10, 30),
                ("comp_spring", .35, .4, .4, 1, 1), ("comp_tape", .3, .35, .35, 1, 1), ("comp_pipe", .3, .3, .3, 1, 1), ("comp_gears", .12, .25, .3, 1, 1),
                ("canned", .35, .3, .25, 1, 2), ("water_bottle", .35, .3, .25, 1, 2), ("bandage", .25, .3, .3, 1, 2),
                ("@val0", .45, .4, .3, 1, 2), ("@val1", .1, .3, .4, 1, 1), ("ammo_9mm", .15, .25, .3, 5, 12), ("ammo_12g", .1, .15, .2, 3, 6),
                ("@part2", .05, .15, .2, 1, 1), ("@crudeatt", .04, .04, .03, 1, 1), ("@gun0", .03, .04, .04, 1, 1),
            },
            ["food"] = new[]
            {
                ("canned", .75, .75, .75, 1, 3), ("water_bottle", .7, .7, .7, 1, 3), ("berries", .3, .25, .2, 2, 5), ("cloth", .3, .3, .3, 5, 15),
                ("bandage", .25, .3, .3, 1, 2), ("antirad", .05, .1, .15, 1, 1), ("medspirt", .05, .1, .1, 1, 1), ("scrap", .4, .45, .5, 2, 5),
                ("@val0", .15, .15, .1, 1, 1),
            },
            ["medical"] = new[]
            {
                ("bandage", .85, .85, .85, 1, 3), ("medkit", .2, .35, .45, 1, 1), ("syringe", .1, .2, .3, 1, 1), ("antirad", .15, .3, .4, 1, 2),
                ("medspirt", .3, .4, .4, 1, 2), ("cloth", .4, .4, .4, 10, 20), ("scrap", .3, .35, .4, 2, 5),
            },
            ["tools"] = new[]
            {
                ("comp_spring", .5, .55, .55, 1, 2), ("comp_gears", .3, .45, .5, 1, 2), ("comp_tape", .45, .45, .45, 1, 2), ("comp_pipe", .45, .45, .45, 1, 2),
                ("comp_oil", .2, .3, .35, 1, 1), ("metal_fragments", .5, .6, .6, 15, 40), ("rubber", .15, .15, .15, 2, 4), ("scrap", .7, .75, .8, 4, 10),
                ("@val1", .1, .25, .3, 1, 1), ("@part2", .04, .12, .15, 1, 1), ("@lockcode", 0.0, .12, .12, 1, 1), ("tool_hatchet", .03, .03, .02, 1, 1),
                ("tool_pickaxe", .03, .03, .02, 1, 1),
            },
            ["electric"] = new[]
            {
                ("val_copper_wire", .4, .5, .5, 1, 3), ("comp_circuit", .1, .3, .4, 1, 1), ("comp_lens", .08, .2, .3, 1, 1), ("@val1", .4, .6, .6, 1, 2),
                ("@val2", .05, .25, .45, 1, 1), ("@high", 0.0, .03, .1, 1, 1), ("comp_tape", .3, .3, .3, 1, 2), ("scrap", .5, .55, .6, 3, 8),
                ("att_flashlight", .02, .05, .06, 1, 1), ("att_laser", 0.0, .02, .04, 1, 1),
            },
            ["ammo"] = new[]
            {
                ("ammo_9mm", .6, .6, .5, 8, 20), ("ammo_12g", .4, .45, .4, 4, 10), ("ammo_762", .05, .3, .55, 8, 16), ("ammo_545", 0.0, .05, .2, 10, 20),
                ("gunpowder", .3, .5, .6, 10, 25), ("sulfur", .15, .2, .2, 10, 25), ("arrow", .2, .1, 0, 4, 10), ("scrap", .4, .45, .5, 3, 6),
            },
            ["army"] = new[]
            {
                ("ammo_762", 0.0, .5, .5, 8, 18), ("bandage", 0.0, .5, .5, 1, 2), ("gunpowder", 0.0, .45, .45, 12, 25), ("scrap", 0.0, .8, .85, 8, 16),
                ("metal_fragments", 0.0, .5, .5, 20, 40), ("canned", 0.0, .3, .3, 1, 2), ("@part2", 0.0, .35, .4, 1, 1), ("@val2", 0.0, .4, .6, 1, 1),
                ("@factatt", 0.0, .08, .1, 1, 1), ("@armyatt", 0.0, .03, .05, 1, 1), ("@satchel", 0.0, .03, .06, 1, 1), ("@tier3", 0.0, 0, .25, 1, 1),
            },
            ["weapon"] = new[]
            {
                ("@part2", 0.0, 1, 1, 1, 1), ("@factatt", 0.0, .35, .4, 1, 1), ("@ammo", 0.0, .5, .55, 6, 14), ("comp_oil", 0.0, .3, .3, 1, 1),
                ("comp_spring", 0.0, .4, .4, 1, 2), ("@crudeatt", 0.0, .05, 0, 1, 1), ("@gun1", 0.0, .06, .08, 1, 1), ("@tier3", 0.0, 0, .25, 1, 1),
                ("scrap", 0.0, .5, .55, 4, 8),
            },
            ["elite"] = new[]
            {
                ("@val2", 0.0, .9, .9, 1, 1), ("@high", 0.0, .25, .35, 1, 1), ("alloy", 0.0, .1, .25, 1, 1), ("comp_circuit", 0.0, .3, .3, 1, 1),
                ("comp_lens", 0.0, .3, .3, 1, 1), ("@gun2", 0.0, .05, .08, 1, 1), ("@tier3", 0.0, .1, .3, 1, 1), ("scrap", 0.0, .8, .8, 8, 15),
            },
        };

        /// <summary>Наполнить тематический ящик (не сейф): пул вида × тир, профиль РТ, одежда, карта доступа на охраняемых.</summary>
        void FillBox(LootBag bag, string[] prof)
        {
            bag.Box ??= PickBox(bag);
            int tier = Math.Clamp(bag.Tier, 0, 2);
            double k = tier == 0 ? 1 : tier == 1 ? 1.6 : 2.2;
            var rolled = new HashSet<string>();
            foreach (var (id, c0, c1, c2, min, max) in BoxPools[bag.Box])
            {
                double ch = tier == 0 ? c0 : tier == 1 ? c1 : c2;
                if (ch <= 0) continue;
                rolled.Add(id);
                if (prof != null && Array.IndexOf(prof, id) >= 0) ch = Math.Min(0.95, ch * ProfileBoost);   // профиль РТ: свои детали чаще
                if (Rng.NextDouble() >= ch) continue;
                int lo = Math.Max(1, (int)Math.Round(min * (min > 1 ? k : 1))), hi = Math.Max(lo, (int)Math.Round(max * (max > 1 ? k : 1)));
                int n = Rng.Next(lo, hi + 1);
                switch (id)
                {
                    case "@val0": bag.Items.Add(new ItemSlot(Economy.Tier0(Rng), n)); break;
                    case "@val1": bag.Items.Add(new ItemSlot(Economy.Tier1(Rng), n)); break;
                    case "@val2": bag.Items.Add(new ItemSlot(Economy.Tier2(Rng), n)); break;
                    case "@high": bag.Items.Add(new ItemSlot(Economy.High(Rng), 1)); break;
                    case "@part2": bag.Items.Add(new ItemSlot(Weaponry.Tier2Part(Rng), 1)); break;
                    case "@crudeatt": bag.Items.Add(new ItemSlot(Pick(new[] { "att_grip_wood", "att_suppressor_can", "att_mag_twin_crude", "att_torch" }), 1)); break;
                    case "@factatt": bag.Items.Add(new ItemSlot(Pick(Weaponry.FactoryAtts), 1)); break;
                    case "@armyatt": bag.Items.Add(new ItemSlot(Pick(Weaponry.ArmyAtts), 1)); break;
                    case "@gun0": bag.Items.Add(Items.NewSlot("weapon_pipeshotgun")); break;
                    case "@gun1": bag.Items.Add(Items.NewSlot(Rng.Next(2) == 0 ? "weapon_pipeshotgun" : "weapon_sawnoff")); break;
                    case "@gun2": bag.Items.Add(Items.NewSlot(Pick(new[] { "weapon_sawnoff", "weapon_mp133", "weapon_grach" }))); break;
                    case "@satchel": bag.Items.Add(Items.NewSlot("expl_satchel")); break;
                    case "@lockcode": bag.Items.Add(Items.NewSlot("lock_code")); break;
                    case "@tier3": TryTier3(bag, 1.0); break;   // шанс уже выпал; режиссёр всё равно дозирует (фаза, лимит в час)
                    case "@ammo": bag.Items.Add(new ItemSlot(Pick(tier >= 2 ? new[] { "ammo_762", "ammo_762", "ammo_9mm", "ammo_12g" } : new[] { "ammo_9mm", "ammo_12g", "ammo_762" }), n)); break;
                    default:
                        if (Items.Get(id)?.Holdable == true && Items.MaxStack(id) <= 1) bag.Items.Add(Items.NewSlot(id));
                        else bag.Items.Add(new ItemSlot(id, n));
                        break;
                }
            }
            // карта доступа — в охраняемых (на продуктах и медицине реже); 0,25 — прогон 28.10: при 0,18 карт почти не было (5 на 20 ботов за 5,5 ч)
            if (tier >= 1 && Rng.NextDouble() < (bag.Box is "food" or "medical" ? 0.1 : 0.25)) bag.Items.Add(new ItemSlot("keycard", 1));
            if (prof != null)
                foreach (var id in prof)
                    if (!rolled.Contains(id)) Maybe(0.14, id, 1, id is "rubber" or "val_copper_wire" ? 3 : 2);   // у предмета профиля нет строки в пуле — своя
            Clothing.Loot(WearRng, bag, $"crate{tier}");   // одежда, резина, кожа, антирадин (D47)
            // продукты и медицина — без железа общего слоя: сплав, резина, кожа, пластины там не к месту (тема ящика читается)
            if (bag.Box is "food" or "medical") bag.Items.RemoveAll(s => s.Id is "alloy" or "rubber" or "leather" || s.Id.StartsWith("plate_"));
            if (bag.Empty) bag.Items.Add(new ItemSlot("scrap", 5 + 5 * tier));

            void Maybe(double chance, string id, int min, int max) { if (Rng.NextDouble() < chance) bag.Items.Add(new ItemSlot(id, Rng.Next(min, max + 1))); }
        }
    }
}
