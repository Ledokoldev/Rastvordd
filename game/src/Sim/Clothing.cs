using System;
using System.Collections.Generic;

namespace Rastvordd.Sim
{
    /// <summary>10 слотов одежды (docs/design/14, «Одежда — версия 2»). В каждый слот — одна вещь.</summary>
    public enum WearSlot { Head = 0, Face = 1, Kofta = 2, Armor = 3, Arms = 4, Gloves = 5, Legs = 6, Knees = 7, Feet = 8, Wrist = 9 }

    /// <summary>Зона попадания: по расстоянию пули от центра персонажа (см. World.BulletHit).</summary>
    public enum HitZone { Head = 0, Torso = 1, Limbs = 2 }

    /// <summary>Вещь: пули (доля 0..1), радиация (ед., сумма по всем вещам), тепло (°C, сумма), скорость (доля, ≤ 0), пластин.</summary>
    public sealed class WearDef
    {
        public string Id, Set;
        public WearSlot Slot;
        public float Bullet, Rad, Warm, Speed;
        public int Plates, Tier;
        public bool Metal;       // шумит, звук «лязг» при надевании
        public bool Rubber;      // ОЗК, противогаз — скрип резины
    }

    /// <summary>Бронепластина: + к защите корпуса со своей стороны, прочность — урон до разрушения.</summary>
    public sealed class PlateDef { public string Id; public float Bonus, Durability, Rad; }

    /// <summary>
    /// Одежда, броня, пластины, часы, радиация и холод — данные (docs/design/14-armor-radiation-heli.md, версии 2–3).
    /// Формулы — здесь же (защита по зонам, скорость), применение — World (Armor.cs), окна — Containers.cs.
    /// Все числа из «Вещи: пули % / радиация (ед.) / тепло (°C)» (версия 3) — они же в интерактивном конструкторе комплекта.
    /// </summary>
    public static class Clothing
    {
        public const int Slots = 10;
        static Clothing() => _ = Items.Get("wood");   // справочник заполняется из Items (Register) — гарантируем порядок
        public const int PlateSlots = 2;              // перед, спина
        public static readonly string[] SlotNames = { "Шлем / шапка", "Лицо", "Кофта", "Броня", "Руки", "Перчатки", "Штаны", "Наколенники", "Обувь", "Запястье" };
        public static readonly HitZone?[] SlotZone = { HitZone.Head, HitZone.Head, HitZone.Torso, HitZone.Torso, HitZone.Limbs, HitZone.Limbs, HitZone.Limbs, HitZone.Limbs, HitZone.Limbs, null };

        // ---------- константы (зоны, потолки, скорость) ----------
        /// <summary>Зоны попадания для тела радиусом 0,40 м; у нас радиус Actor.Radius (0,35) — пороги масштабируются (×R/0,40).</summary>
        public const float DocBodyRadius = 0.40f, HeadR = 0.12f, TorsoR = 0.28f;
        public const float HeadMul = 1.8f, TorsoMul = 1f, LimbMul = 0.6f;
        public static readonly float[] ZoneCap = { 0.55f, 0.60f, 0.45f };   // голова, корпус, конечности (27.10: было 0.70/0.75/0.60)
        public const float SpeedCap = -0.15f;
        public const float MeleeFactor = 0.5f;          // ближний бой: защита корпуса ×0,5 (пластины не изнашивает)

        // радиация (ед.) и холод (°C)
        public const float RadRate = 0.1f;              // доза/с = (уровень − защита) × 0,1
        public const float RadDecay = 0.5f;             // доза/с выводится, когда не облучает
        public const float RadNausea = 100, RadHurt1 = 250, RadHurt3 = 500, RadMax = 1000;
        public const float AntiradDose = 120;           // антирадин: −120 дозы сразу
        // фон РТ по тиру (D53): боевой сет тира N держит РТ тиров 0…N, защищённый — 0…N+1, две свинцовые пластины — любые
        public static readonly float[] RtRad = { 0, 10, 20, 30, 40 };
        public const float Comfort = 15f;               // воздух + тепло ≥ +15 °C — комфортно
        public const float FireWarm = 15f, IndoorWarm = 8f, WetCold = -10f, NightCold = -10f, RainCold = -4f, WinterCold = -15f, UndergroundAir = 6f;
        public const float ColdRise = 0.02f;            // переохлаждение/с = дефицит × 0,02 (до 100): голым ночью в поле (+4 °C) — ≈ 7,5 мин
        public const float ColdRecover = 2f;
        public const float ColdDmgBase = 0.15f, ColdDmgPerDeg = 0.02f;  // урон/с на полном переохлаждении: 0,15 + дефицит × 0,02 (тундра ночью голым ≈ 0,8)

        static readonly Dictionary<string, WearDef> Defs = new();
        static readonly Dictionary<string, PlateDef> PlateDefs = new();
        public static WearDef Def(string id) => id != null && Defs.TryGetValue(id, out var d) ? d : null;
        public static PlateDef Plate(string id) => id != null && PlateDefs.TryGetValue(id, out var p) ? p : null;
        public static IEnumerable<WearDef> All => Defs.Values;

        /// <summary>Наборы: id → (название, тир, «от чего», где взять). Порядок вещей — как в документе.</summary>
        public sealed class SetInfo { public string Key, Name, Kind, Source; public int Tier; public readonly List<string> Items = new(); }
        public static readonly List<SetInfo> Sets = new();

        // ---------- справка для карточек и ботов ----------
        static readonly Dictionary<string, Weaponry.Meta> Meta = new();
        public static bool TryMeta(string id, out Weaponry.Meta m) => Meta.TryGetValue(id, out m);

        internal static void Register(Func<string, string, ItemCategory, ItemDef> add)
        {
            // материалы, которых раньше не было (D47): резина — покрышки и бочки на РТ, кожа — бочки и ящики (зверей в игре нет)
            add("rubber", "Резина", ItemCategory.Component);   // компонент: режется из покрышек (узлы Tire / TireStack)
            Meta["rubber"] = new Weaponry.Meta { Desc = "Резина: режешь из покрышек (топор или нож быстрее). Спецовка, ОЗК, сапоги, рукояти, уплотнители", Rarity = 1, Value = 1.5f, Stack = 200 };
            add("leather", "Кожа", ItemCategory.Resource);
            Meta["leather"] = new Weaponry.Meta { Desc = "Старые куртки и ремни на лоскуты. Кожаный сет, кирзачи, ремни брони. Бочки и ящики", Rarity = 1, Value = 1.5f, Stack = 200 };
            var anti = add("antirad", "Антирадин", ItemCategory.Medical);
            Meta["antirad"] = new Weaponry.Meta { Desc = $"Выводит радиацию: −{AntiradDose:0} дозы. F — принять, когда облучён. Ящики РТ; крафт — химстол (медспирт 2 + уголь 10)", Rarity = 2, Value = 12f, Stack = 10 };

            void W(string set, string key, string name, WearSlot slot, float b, float rad, float warm, float spd = 0, int plates = 0, string desc = null, bool metal = false, bool rubber = false)
            {
                var si = Sets.Find(s => s.Key == set);
                string id = key == "" ? $"wear_{set}" : $"wear_{set}_{key}";
                var wd = new WearDef { Id = id, Set = set, Slot = slot, Bullet = b / 100f, Rad = rad, Warm = warm, Speed = spd / 100f, Plates = plates, Tier = si.Tier, Metal = metal, Rubber = rubber };
                Defs[id] = wd;
                si.Items.Add(id);
                add(id, name, ItemCategory.Clothing).Wear = wd;
                string d = desc ?? "";
                Meta[id] = new Weaponry.Meta { Desc = d, Rarity = Math.Min(4, si.Tier), Value = ValueOf(si.Tier, slot, plates), Stack = 1 };
            }
            void S(string key, string name, int tier, string kind, string source) => Sets.Add(new SetInfo { Key = key, Name = name, Tier = tier, Kind = kind, Source = source });

            S("rags", "Тряпьё", 0, "base", "в руках, ткань");
            W("rags", "bandana", "Бандана", WearSlot.Face, 0, 0, 1, desc: "Тряпка на лицо. Почти ничего, но лучше, чем ничего");
            W("rags", "shirt", "Рубаха из мешковины", WearSlot.Kofta, 3, 0, 5, desc: "Первая одежда: колется, но греет");
            W("rags", "pants", "Штаны из мешковины", WearSlot.Legs, 3, 0, 5);
            W("rags", "wraps", "Обмотки", WearSlot.Feet, 1, 0, 2, desc: "Тряпки на ноги вместо обуви");
            W("rags", "mittens", "Рукавицы", WearSlot.Gloves, 1, 0, 2);

            S("vatnik", "Ватник", 1, "env", "верстак 1");
            W("vatnik", "ushanka", "Ушанка", WearSlot.Head, 3, 3, 7, desc: "Тёплая шапка с ушами");
            W("vatnik", "scarf", "Шарф", WearSlot.Face, 0, 4, 4);
            W("vatnik", "telogreika", "Телогрейка", WearSlot.Kofta, 8, 5, 10, desc: "Стёганый ватник: держит холод ночью");
            W("vatnik", "pants", "Ватные штаны", WearSlot.Legs, 6, 4, 7);
            W("vatnik", "valenki", "Валенки", WearSlot.Feet, 2, 2, 4);
            W("vatnik", "mittens", "Ватные рукавицы", WearSlot.Gloves, 1, 2, 2);

            S("leather", "Кожа и дерево", 1, "people", "верстак 1");
            W("leather", "helmet", "Кожаный шлем", WearSlot.Head, 18, 2, 3);
            W("leather", "chestplate", "Деревянный нагрудник", WearSlot.Armor, 12, 3, 2, -3, desc: "Доски на ремнях: держит нож и картечь на излёте");
            W("leather", "bracers", "Кожаные наручи", WearSlot.Arms, 14, 1, 2);
            W("leather", "pants", "Кожаные штаны", WearSlot.Legs, 8, 3, 6);
            W("leather", "shinguards", "Деревянные поножи", WearSlot.Knees, 14, 1, 2, -2);

            S("spec", "Спецовка", 2, "env", "верстак 2");
            W("spec", "hood", "Прорезиненный капюшон", WearSlot.Head, 2, 5, 8, rubber: true);
            W("spec", "r2", "Респиратор Р-2", WearSlot.Face, 0, 8, 2, desc: "Фильтрует пыль и радиоактивную гарь", rubber: true);
            W("spec", "robe", "Роба-спецовка", WearSlot.Kofta, 6, 7, 16, desc: "Плотная рабочая роба: от пыли и радиации");
            W("spec", "pants", "Прорезиненные штаны", WearSlot.Legs, 4, 5, 12, rubber: true);
            W("spec", "boots", "Резиновые сапоги", WearSlot.Feet, 2, 3, 8, rubber: true);
            W("spec", "gloves", "Резиновые перчатки", WearSlot.Gloves, 1, 2, 6, rubber: true);

            S("scrap", "Кустарная броня", 2, "people", "верстак 2");
            W("scrap", "pot", "Каска-кастрюля", WearSlot.Head, 30, 3, 1, desc: "Кастрюля на ремне. Звенит от каждой пули", metal: true);
            W("scrap", "balaclava", "Балаклава", WearSlot.Face, 5, 4, 5);
            W("scrap", "signvest", "Жилет из дорожных знаков", WearSlot.Armor, 20, 5, 0, -4, 1, "Дорожные знаки на проволоке. Карман под 1 пластину (спереди). Гремит на бегу", metal: true);
            W("scrap", "bracers", "Жестяные наручи", WearSlot.Arms, 18, 2, 1, metal: true);
            W("scrap", "kneepads", "Наколенники из покрышек", WearSlot.Knees, 6, 2, 2, -2, rubber: true);
            W("scrap", "kirza", "Кирзачи", WearSlot.Feet, 8, 4, 6);

            S("ozk", "ОЗК", 3, "env", "верстак 3 или лут");   // защищённый тир 3: на будущие РТ уровня 4 («Объект 42»)
            W("ozk", "hood", "Капюшон ОЗК", WearSlot.Head, 2, 7, 8, rubber: true);
            W("ozk", "gp5", "Противогаз ГП-5", WearSlot.Face, 2, 12, 3, desc: "Резиновая маска с фильтром. Дышишь тяжело, но живёшь", rubber: true);
            W("ozk", "cape", "Плащ ОЗК", WearSlot.Armor, 4, 12, 22, -3, desc: "Общевойсковой защитный комплект: от радиации, не от пуль. Виден издалека", rubber: true);
            W("ozk", "gloves", "Перчатки ОЗК", WearSlot.Gloves, 1, 4, 8, rubber: true);
            W("ozk", "stockings", "Чулки ОЗК", WearSlot.Feet, 1, 5, 11, rubber: true);

            S("army", "Армейская", 3, "people", "верстак 3");
            W("army", "ssh68", "Шлем СШ-68", WearSlot.Head, 35, 4, 4, desc: "Стальной армейский шлем", metal: true);
            W("army", "bushlat", "Бушлат", WearSlot.Kofta, 5, 6, 18, desc: "Армейский ватный бушлат: тепло и немного от осколков");
            W("army", "6b2", "Бронежилет 6Б2", WearSlot.Armor, 38, 4, 2, -5, 2, "Армейский бронежилет: 2 пластины — спереди и сзади", metal: true);
            W("army", "elbowpads", "Армейские налокотники", WearSlot.Arms, 20, 2, 2);
            W("army", "kneepads", "Армейские наколенники", WearSlot.Knees, 10, 2, 2);
            W("army", "pants", "Армейские штаны", WearSlot.Legs, 8, 5, 10);
            W("army", "bertsy", "Берцы", WearSlot.Feet, 8, 4, 8);
            W("army", "gloves", "Тактические перчатки", WearSlot.Gloves, 4, 3, 6);


            S("sobr", "СОБР", 3, "people", "только лут");   // элита тира 3: тиров 0–3, четвёртого нет
            W("sobr", "sfera", "Шлем «Сфера»", WearSlot.Head, 45, 4, 4, desc: "Титановый шлем спецназа", metal: true);
            W("sobr", "nomex", "Балаклава Nomex", WearSlot.Face, 8, 4, 6);
            W("sobr", "kora", "Бронежилет «Кора»", WearSlot.Armor, 42, 4, 2, -7, 2, "Штурмовой бронежилет: 2 пластины — спереди и сзади", metal: true);
            W("sobr", "suit", "Тактический костюм", WearSlot.Kofta, 10, 6, 14);
            W("sobr", "elbowpads", "Налокотники СОБР", WearSlot.Arms, 20, 2, 2);
            W("sobr", "kneepads", "Наколенники СОБР", WearSlot.Knees, 10, 2, 2);
            W("sobr", "pants", "Тактические брюки", WearSlot.Legs, 10, 4, 10);
            W("sobr", "boots", "Штурмовые ботинки", WearSlot.Feet, 8, 2, 8);
            W("sobr", "gloves", "Штурмовые перчатки", WearSlot.Gloves, 6, 2, 4);

            // часы — одни (упрощено по просьбе пользователя): время, температура, радиация в HUD
            S("watch", "Часы", 1, "base", "бочки и ящики РТ 0–1");
            W("watch", "", "Часы «Электроника»", WearSlot.Wrist, 0, 0, 0, desc: "Наденешь на запястье — в HUD время, температура воздуха и радиация там, где стоишь");
            Meta["wear_watch"].Rarity = 2; Meta["wear_watch"].Value = 25;

            void P(string id, string name, float bonus, float dur, int rarity, float value, string desc, float rad = 0)
            {
                PlateDefs[id] = new PlateDef { Id = id, Bonus = bonus / 100f, Durability = dur, Rad = rad };
                add(id, name, ItemCategory.Clothing).Plate = PlateDefs[id];
                Meta[id] = new Weaponry.Meta { Desc = desc, Rarity = rarity, Value = value, Stack = 1 };
            }
            P("plate_homemade", "Кустарная пластина", 10, 150, 2, 30, "Жесть и дорожный знак: +10% к корпусу со своей стороны, держит 150 урона. Вставляется в жилет из знаков, 6Б2, «Кору»");
            P("plate_steel", "Армейская стальная пластина", 20, 400, 3, 80, "+20% к корпусу со своей стороны, держит 400 урона. Только лут РТ тира 2");
            P("plate_ceramic", "Керамическая пластина", 30, 300, 4, 150, "+30% к корпусу со своей стороны, держит 300 урона. Только вертолёт и «Объект 42»");
            P("plate_lead", "Свинцовая пластина", 10, 250, 2, 40, "Антирад: +5 к защите от радиации и +10% к корпусу, держит 250 урона. Две в бронике (6Б2, «Кора») — пустит куда угодно", rad: 5);
        }

        static float ValueOf(int tier, WearSlot slot, int plates) =>
            (tier switch { 0 => 3f, 1 => 8f, 2 => 20f, 3 => 45f, _ => 90f }) * (slot is WearSlot.Armor or WearSlot.Head ? 2f : 1f) + plates * 10;

        /// <summary>Рецепты одежды (D47). СОБР, армейская/керамическая пластины, часы — только лут; ОЗК — верстак 3 и лут.</summary>
        internal static void AddRecipes(List<Recipe> all)
        {
            Recipe R(string output, float time, int bench, params (string, int)[] cost) => new Recipe { Output = output, Time = time, Bench = bench, Count = 1, Cost = cost };
            all.AddRange(new[]
            {
                // тир 0 — в руках, из ткани (конопля)
                R("wear_rags_bandana", 2, 0, ("cloth", 5)),
                R("wear_rags_shirt", 4, 0, ("cloth", 15)),
                R("wear_rags_pants", 4, 0, ("cloth", 15)),
                R("wear_rags_wraps", 2, 0, ("cloth", 8)),
                R("wear_rags_mittens", 2, 0, ("cloth", 6)),
                // тир 1 — верстак 1
                R("wear_vatnik_ushanka", 5, 1, ("cloth", 20), ("leather", 3)),
                R("wear_vatnik_scarf", 3, 1, ("cloth", 12)),
                R("wear_vatnik_telogreika", 8, 1, ("cloth", 40)),
                R("wear_vatnik_pants", 6, 1, ("cloth", 30)),
                R("wear_vatnik_valenki", 6, 1, ("cloth", 25)),
                R("wear_vatnik_mittens", 3, 1, ("cloth", 12)),
                R("wear_leather_helmet", 5, 1, ("leather", 8), ("cloth", 5)),
                R("wear_leather_chestplate", 8, 1, ("wood", 200), ("leather", 4)),
                R("wear_leather_bracers", 4, 1, ("leather", 6)),
                R("wear_leather_pants", 6, 1, ("leather", 12), ("cloth", 10)),
                R("wear_leather_shinguards", 5, 1, ("wood", 120), ("leather", 3)),
                // тир 2 — верстак 2
                R("wear_spec_hood", 5, 2, ("rubber", 6), ("cloth", 10)),
                R("wear_spec_r2", 6, 2, ("rubber", 4), ("charcoal", 20), ("comp_tape", 1)),
                R("wear_spec_robe", 8, 2, ("cloth", 35), ("rubber", 6)),
                R("wear_spec_pants", 6, 2, ("rubber", 10), ("cloth", 15)),
                R("wear_spec_boots", 6, 2, ("rubber", 12)),
                R("wear_spec_gloves", 4, 2, ("rubber", 6)),
                R("wear_scrap_pot", 6, 2, ("metal_fragments", 75), ("leather", 3)),
                R("wear_scrap_balaclava", 3, 2, ("cloth", 15)),
                R("wear_scrap_signvest", 10, 2, ("metal_fragments", 150), ("comp_tape", 2), ("leather", 4)),
                R("wear_scrap_bracers", 5, 2, ("metal_fragments", 60), ("leather", 2)),
                R("wear_scrap_kneepads", 5, 2, ("rubber", 10), ("comp_tape", 1)),
                R("wear_scrap_kirza", 6, 2, ("leather", 12), ("rubber", 4)),
                R("plate_homemade", 8, 2, ("metal_fragments", 100), ("comp_tape", 1)),
                R("plate_lead", 10, 2, ("metal_fragments", 150), ("comp_tape", 1)),
                R("antirad", 4, 2, ("charcoal", 20), ("cloth", 2)),
                // тир 3 — верстак 3
                R("wear_ozk_hood", 6, 3, ("rubber", 15), ("cloth", 10)),
                R("wear_ozk_gp5", 10, 3, ("rubber", 10), ("charcoal", 30), ("comp_lens", 1)),
                R("wear_ozk_cape", 12, 3, ("rubber", 30), ("cloth", 20)),
                R("wear_ozk_gloves", 5, 3, ("rubber", 10)),
                R("wear_ozk_stockings", 6, 3, ("rubber", 15)),
                R("wear_army_ssh68", 10, 3, ("metal_fragments", 200), ("leather", 5)),
                R("wear_army_bushlat", 10, 3, ("cloth", 60), ("leather", 6)),
                R("wear_army_6b2", 15, 3, ("metal_fragments", 250), ("cloth", 30), ("comp_tape", 3)),
                R("wear_army_elbowpads", 5, 3, ("metal_fragments", 50), ("rubber", 6)),
                R("wear_army_kneepads", 5, 3, ("metal_fragments", 50), ("rubber", 6)),
                R("wear_army_pants", 6, 3, ("cloth", 40)),
                R("wear_army_bertsy", 8, 3, ("leather", 15), ("rubber", 6)),
                R("wear_army_gloves", 4, 3, ("leather", 6), ("cloth", 5)),
            });
        }

        /// <summary>
        /// Изучение одежды: тиры 0–1 известны с начала, тиры 2–3 — один дешёвый узел на комплект (лом).
        /// Узел открывает весь комплект (id узла — главная вещь комплекта, см. TechKey). Итого 4 узла, 200 лома (D49).
        /// </summary>
        internal static void AddTech(List<TechNode> all)
        {
            all.Add(new TechNode { Id = "wear_spec_robe", Bench = 2, Scrap = 25, Col = 1, Row = 15 });
            all.Add(new TechNode { Id = "wear_scrap_signvest", Bench = 2, Scrap = 35, Col = 1, Row = 16 });
            all.Add(new TechNode { Id = "wear_ozk_cape", Bench = 3, Scrap = 60, Col = 2, Row = 6, Requires = new[] { "wear_spec_robe" } });
            all.Add(new TechNode { Id = "wear_army_6b2", Bench = 3, Scrap = 80, Col = 2, Row = 7, Requires = new[] { "wear_scrap_signvest" } });
        }

        /// <summary>
        /// Переработка лутовой одежды и пластин (D49): у крафтовых — половина рецепта (Items.RecycleYield), у лутовых — лом по тиру
        /// (тир 2 — 6, тир 3 — 12, тир 4 — 25; голова и броня ×1,5) и немного ткани или металла; пластины — лом и металл.
        /// </summary>
        public static (string, int)[] LootRecycle(string id)
        {
            if (id == null) return null;
            switch (id)
            {
                case "plate_steel": return new[] { ("scrap", 20), ("metal_fragments", 60) };
                case "plate_ceramic": return new[] { ("scrap", 35) };
                case "wear_watch": return new[] { ("scrap", 10), ("val_radio_parts", 1) };
                case "rubber": return new[] { ("scrap", 1) };   // резина — почти ничего: её режут на крафт, а не на лом
            }
            if (Def(id) is not WearDef d || Recipes.All.Exists(r => r.Output == id)) return null;
            int scrap = d.Tier switch { <= 1 => 2, 2 => 6, 3 => 12, _ => 25 };
            if (d.Slot is WearSlot.Armor or WearSlot.Head) scrap = scrap * 3 / 2;
            return d.Metal ? new[] { ("scrap", scrap), ("metal_fragments", 10 * d.Tier) } : new[] { ("scrap", scrap), ("cloth", 5 * Math.Max(1, d.Tier)) };
        }

        /// <summary>Узел дерева, который открывает рецепт вещи (комплект целиком); null — своего узла нет.</summary>
        public static string TechKey(string id) => id switch
        {
            null => null,
            "antirad" => null,
            "plate_homemade" or "plate_lead" => "wear_scrap_signvest",
            _ when id.StartsWith("wear_spec_") => "wear_spec_robe",
            _ when id.StartsWith("wear_scrap_") => "wear_scrap_signvest",
            _ when id.StartsWith("wear_ozk_") => "wear_ozk_cape",
            _ when id.StartsWith("wear_army_") => "wear_army_6b2",
            _ => null,
        };

        /// <summary>Название узла в окне изучения: комплект, а не одна вещь.</summary>
        public static string TechName(string nodeId) => nodeId switch
        {
            "wear_spec_robe" => "Спецовка (комплект)",
            "wear_scrap_signvest" => "Кустарная броня (комплект + пластина)",
            "wear_ozk_cape" => "ОЗК (комплект)",
            "wear_army_6b2" => "Армейская (комплект)",
            _ => null,
        };

        // ---------- формулы ----------

        public static float Combine(float a, float r) => 1f - (1f - a) * (1f - r);

        /// <summary>Сколько пластин держит надетая броня (0–2).</summary>
        public static int PlateCap(Actor a) => Def(a.Worn[(int)WearSlot.Armor]?.Id)?.Plates ?? 0;

        public static float PlateDur(ItemSlot s) => s == null ? 0 : s.Dur >= 0 ? s.Dur : Plate(s.Id)?.Durability ?? 0;
        public static float PlateFrac(ItemSlot s) => Plate(s?.Id) is PlateDef p ? Math.Clamp(PlateDur(s) / p.Durability, 0, 1) : -1;

        /// <summary>Активная пластина со стороны: 0 — перед, 1 — спина (null — нет).</summary>
        public static ItemSlot ActivePlate(Actor a, int side)
        {
            if (side < 0 || side >= PlateSlots || side >= PlateCap(a)) return null;
            var p = a.Plates[side];
            return p != null && Plate(p.Id) != null && PlateDur(p) > 0 ? p : null;
        }

        /// <summary>
        /// Защита зоны от пуль: 1 − Π(1 − rᵢ) по вещам этой зоны; корпус — плюс пластина со стороны выстрела
        /// (side: 0 — спереди, 1 — сзади, −1 — без пластины). Потолок: голова 70%, корпус 75%, конечности 60%.
        /// </summary>
        public static float Zone(Actor a, HitZone z, int side = 0)
        {
            float p = 0;
            for (int i = 0; i < Slots; i++)
                if (SlotZone[i] == z && Def(a.Worn[i]?.Id) is WearDef d) p = Combine(p, d.Bullet);
            if (z == HitZone.Torso && ActivePlate(a, side) is ItemSlot pl) p = Combine(p, Plate(pl.Id).Bonus);
            return CapZone(z, p);
        }

        public static float CapZone(HitZone z, float p) => MathF.Min(p, ZoneCap[(int)z]);
        public static float CapSpeed(float sum) => MathF.Max(SpeedCap, sum);

        /// <summary>Защита от радиации (ед.): сумма по всем надетым вещам.</summary>
        public static float RadProt(Actor a)
        {
            float s = 0;
            foreach (var w in a.Worn) if (Def(w?.Id) is WearDef d) s += d.Rad;
            for (int i = 0; i < PlateSlots && i < PlateCap(a); i++) if (Plate(a.Plates[i]?.Id) is PlateDef p && PlateDur(a.Plates[i]) > 0) s += p.Rad;   // свинцовые пластины
            return s;
        }
        /// <summary>Тепло одежды (°C): сумма. Держит холод до 15 − тепло.</summary>
        public static float Warmth(Actor a) { float s = 0; foreach (var w in a.Worn) if (Def(w?.Id) is WearDef d) s += d.Warm; return s; }
        /// <summary>Штраф скорости: сумма, но не больше −15%.</summary>
        public static float SpeedPenalty(Actor a) { float s = 0; foreach (var w in a.Worn) if (Def(w?.Id) is WearDef d) s += d.Speed; return CapSpeed(s); }
        public static bool HasWatch(Actor a) => a.Worn[(int)WearSlot.Wrist]?.Id == "wear_watch";
        public static bool Noisy(Actor a) { foreach (var w in a.Worn) if (Def(w?.Id)?.Metal == true) return true; return false; }

        /// <summary>Есть ли на голове жёсткий шлем (для звука «дзынь»).</summary>
        public static bool HardHelmet(Actor a) => a.Worn[(int)WearSlot.Head]?.Id is "wear_scrap_pot" or "wear_army_ssh68" or "wear_sobr_sfera" or "wear_leather_helmet";

        /// <summary>Звук надевания: ткань / металл / резина (docs/sound-list.md).</summary>
        public static string EquipSound(string id) => Def(id) is WearDef d ? (d.Metal ? "wear_metal" : d.Rubber ? "wear_rubber" : "wear_cloth") : Plate(id) != null ? "wear_metal" : "wear_cloth";

        /// <summary>Очки вещи для ботов: пули важнее всего, потом радиация и тепло.</summary>
        public static float BotScore(WearDef d) => d == null ? 0 : d.Bullet * 100 + d.Rad * 0.6f + d.Warm * 0.4f + d.Speed * 60 + (d.Slot == WearSlot.Wrist ? 5 : 0);

        // ---------- лут ----------
        static readonly string[] T0Bits = { "wear_rags_bandana", "wear_rags_shirt", "wear_rags_pants", "wear_rags_wraps", "wear_rags_mittens" };
        static string[] _t1, _t2, _ozk, _army, _sobr;
        static string[] SetItems(string key) => Sets.Find(s => s.Key == key).Items.ToArray();
        static string[] T1 => _t1 ??= Concat(SetItems("vatnik"), SetItems("leather"));
        static string[] T2 => _t2 ??= Concat(SetItems("spec"), SetItems("scrap"));
        static string[] Ozk => _ozk ??= SetItems("ozk");
        static string[] Army => _army ??= SetItems("army");
        static string[] Sobr => _sobr ??= SetItems("sobr");
        static string[] Concat(string[] a, string[] b) { var r = new string[a.Length + b.Length]; a.CopyTo(r, 0); b.CopyTo(r, a.Length); return r; }

        /// <summary>
        /// Одежда и материалы в контейнер. kind: "barrel", "barrel_rt", "crate0/1/2", "safe", "punch", "event".
        /// Свой ГСЧ (World.WearRng): основной поток мира не сдвигается — старые сейвы и карта по зерну те же.
        /// </summary>
        public static void Loot(Random rng, LootBag bag, string kind, bool army = true)
        {
            void Maybe(double ch, string id, int min = 1, int max = 1) { if (rng.NextDouble() < ch) bag.Items.Add(new ItemSlot(id, rng.Next(min, max + 1))); }
            void One(double ch, string[] from) { if (rng.NextDouble() < ch) bag.Items.Add(new ItemSlot(from[rng.Next(from.Length)], 1)); }
            StationDefs.Loot(rng, bag, kind);   // легированная сталь, ёмкости с топливом, медицина (D54, Prog.Loot)
            switch (kind)
            {
                case "barrel":
                    Maybe(0.08, "rubber", 1, 3); Maybe(0.06, "leather", 1, 3); Maybe(0.008, "wear_watch");
                    break;
                case "barrel_rt":   // бочки на РТ: покрышки, камеры, старые куртки
                    Maybe(0.25, "rubber", 2, 5); Maybe(0.12, "leather", 2, 4); Maybe(0.02, "wear_watch"); One(0.03, T0Bits);
                    break;
                case "crate0":
                    Maybe(0.3, "rubber", 2, 6); Maybe(0.25, "leather", 2, 5); Maybe(0.04, "wear_watch"); Maybe(0.06, "antirad");
                    One(0.12, T0Bits); One(0.05, T1);
                    break;
                case "crate1":
                    Maybe(0.3, "rubber", 3, 8); Maybe(0.25, "leather", 3, 6); Maybe(0.03, "wear_watch"); Maybe(0.2, "antirad", 1, 2);
                    One(0.12, T1); One(0.05, T2); One(0.04, Ozk); Maybe(0.05, "plate_homemade");
                    break;
                case "crate2":
                case "safe":
                    Maybe(0.3, "rubber", 4, 10); Maybe(0.3, "antirad", 1, 3);
                    One(0.12, Ozk); if (army) One(kind == "safe" ? 0.2 : 0.1, Army);   // (27.10) армейская — только тир 2
                     Maybe(kind == "safe" ? 0.2 : 0.12, "plate_steel");
                    One(0.02, Sobr); Maybe(0.05, "plate_lead"); Maybe(0.008, "plate_ceramic");
                    break;
                case "punch":   // крейт РТ тира 1 под перфокарту
                    Maybe(0.35, "antirad", 1, 2); One(0.15, T2); One(0.08, Ozk); Maybe(0.08, "plate_homemade"); Maybe(0.03, "plate_steel"); Maybe(0.03, "wear_watch");
                    break;
                case "event":   // аирдроп и закрытый ящик: тир 4 — только отсюда (и будущие вертолёт / «Объект 42»)
                    Maybe(0.5, "antirad", 1, 3); One(0.25, Army); Maybe(0.25, "plate_steel");
                    One(0.12, Sobr); One(0.1, Ozk); Maybe(0.15, "plate_lead"); Maybe(0.06, "plate_ceramic");
                    break;
            }
        }

        /// <summary>Что надеть охране РТ по тиру: тир 1 — ватник или кожа, тир 2 — армейская; СОБР — крушение вертолёта (хук).</summary>
        public static string[] GuardKit(Random rng, int tier) => tier switch
        {
            >= 4 => new[] { "wear_sobr_sfera", "wear_sobr_nomex", "wear_sobr_kora", "wear_sobr_suit", "wear_sobr_kneepads", "wear_sobr_pants", "wear_sobr_boots", "wear_sobr_gloves" },
            >= 2 => new[] { "wear_army_ssh68", "wear_army_bushlat", "wear_army_6b2", "wear_army_pants", "wear_army_bertsy", rng.Next(2) == 0 ? "wear_army_gloves" : "wear_scrap_balaclava" },
            _ => rng.Next(2) == 0
                ? new[] { "wear_vatnik_ushanka", "wear_vatnik_telogreika", "wear_vatnik_pants", "wear_vatnik_valenki", rng.Next(2) == 0 ? "wear_vatnik_scarf" : "wear_vatnik_mittens" }
                : new[] { "wear_leather_helmet", "wear_leather_chestplate", "wear_leather_bracers", "wear_leather_pants", rng.Next(2) == 0 ? "wear_leather_shinguards" : "wear_rags_shirt" },
        };
    }
}
