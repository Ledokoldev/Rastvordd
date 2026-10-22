using System;
using System.Collections.Generic;

namespace Rastvordd.Sim
{
    /// <summary>Станция прогрессии (D54): одна запись — один вид установленного. Добавить/поменять станцию — одна строка в Prog.Stations.</summary>
    public sealed class StationDef
    {
        public string Item;                     // id предмета (deploy_*)
        public DeployKind Kind;
        public string Name, Desc, Sprite;       // спрайт в мире: art/props/<Sprite>.png (null — рисуется кодом)
        public Footprint Size; public float W, D;   // место в клетке и хитбокс (метры, W — длинная сторона)
        public int Bench;                       // какой верстак нужен для крафта (0 — в руках)
        public (string id, int n)[] Cost = Array.Empty<(string, int)>();
        public float Time = 10;                 // крафт, с
        public int Scrap;                       // изучение на верстаке Bench (0 — известно сразу)
        public string[] Requires;               // узлы дерева, которые нужны раньше
        public bool Registered;                 // предмет уже заведён в другом месте (токарный станок)
        public float Power;                     // потребление тока (0 — не нужен)
        public bool NeedsPower;                 // без тока не работает (электропечь, лампы, колонна)
        public float PowerSpeed = 1;            // под током работает/крафтит быстрее во столько раз
        public float PowerOut, PowerRadius;     // генератор: сколько тока даёт и на сколько метров
        public string Burn; public float BurnPerMin;   // что жжёт, пока работает (дерево — шт., жидкость — литры)
        public string Machine;                  // окно «материалы → выход» по рецептам Prog.Machines с этим ключом
        public int Slots = 12;                  // ячеек хранилища (станок, ящик)
        public string[] Boost;                  // рядом с ним эти рецепты (id или префикс*) быстрее/дешевле
        public float BoostTime = 1, BoostCost = 1;
        public float Light;                     // свет, м (лампы — только под током, костёр — пока горит)
        public bool Smoke, Warm;                // дымит (видно базу), греет как огонь
        public float Noise;                     // шум работы, м
        public bool OnNode;                     // буровая: ставится на землю у рудного узла
        public float Tank;                      // бочка: литров
        public string Repair;                   // что чинит рядом: "gun" — стволы целиком, "plate" — пластины
    }

    /// <summary>Рецепт станка: материалы в его ячейках → выход (жидкость — литры в ёмкостях внутри станка).</summary>
    public sealed class MachineRecipe
    {
        public string Machine, Output;
        public int Count = 1;
        public (string id, int n)[] In;
        public float Time;
        public bool Bench3;
    }

    /// <summary>Инструмент добычи: урон, интервал, дальность, добыча за удар (дерево/камень/руда/сера), топливо.</summary>
    public sealed class ToolDef
    {
        public string Id, Name, Desc;
        public float Dmg, Interval, Range, Tree, Stone, Metal, Sulfur;
        public string Fuel; public float FuelPerHit;   // без топлива бьёт как булыжник
        public int Bench, Scrap; public (string id, int n)[] Cost; public float Time = 8;
        public float Noise = 12;
    }

    /// <summary>Строка лута: шанс, предмет, сколько; Liquid — ёмкость с жидкостью (min..max литров).</summary>
    public readonly record struct LootLine(double Chance, string Id, int Min, int Max, string Liquid = null);

    /// <summary>
    /// ВСЕ ЧИСЛА ПРОГРЕССИИ D54 (docs/design/15-economy.md, «Реализация D54»): легированная сталь, станции, ток, топливо и ёмкости,
    /// вода, медицина, инструменты, бронеуровень построек, лут. Поменять баланс — правка здесь; логика — Sim/Stations.cs, Sim/Liquids.cs.
    /// </summary>
    public static class Prog
    {
        // =====================================================================================================
        // Легированная сталь: только лут высоких тиров и переработка; не плавится, не добывается
        // =====================================================================================================
        public const string Alloy = "alloy";

        /// <summary>Переписанные рецепты существующих вещей (весь рецепт целиком): выход → цена, верстак, станция.</summary>
        public static readonly (string output, int bench, DeployKind station, (string, int)[] cost)[] RecipeOverride =
        {
            ("deploy_workbench3", 2, DeployKind.None, new[] { ("metal_fragments", 600), ("scrap", 400), ("comp_circuit", 3), ("comp_gears", 5), (Alloy, 25) }),
            // стволы тира 3 — только на оружейном столе, с легированной сталью
            ("weapon_akm", 3, DeployKind.GunTable, new[] { ("metal_fragments", 250), ("wood", 100), ("comp_receiver_ak", 1), ("comp_bolt_carrier", 1), ("comp_gas_block", 1), ("comp_spring", 4), (Alloy, 25) }),
            ("weapon_an94", 3, DeployKind.GunTable, new[] { ("metal_fragments", 300), ("comp_receiver_ak", 1), ("comp_barrel_chrome", 1), ("comp_bolt_carrier", 1), ("comp_gas_block", 1), ("comp_gears", 3), (Alloy, 30) }),
            ("weapon_ppsh", 3, DeployKind.GunTable, new[] { ("metal_fragments", 200), ("wood", 150), ("comp_body_smg", 1), ("comp_barrel_chrome", 1), ("comp_bolt_carrier", 1), ("comp_spring", 4), (Alloy, 20) }),
            ("weapon_svd", 3, DeployKind.GunTable, new[] { ("metal_fragments", 250), ("wood", 100), ("comp_body_rifle", 1), ("comp_barrel_chrome", 1), ("comp_gas_block", 1), ("comp_trigger", 1), (Alloy, 25) }),
            // армейская одежда: +20 стали на комплект
            ("wear_army_ssh68", 3, DeployKind.None, new[] { ("metal_fragments", 200), ("leather", 5), (Alloy, 5) }),
            ("wear_army_6b2", 3, DeployKind.None, new[] { ("metal_fragments", 250), ("cloth", 30), ("comp_tape", 3), (Alloy, 10) }),
            ("wear_army_elbowpads", 3, DeployKind.None, new[] { ("metal_fragments", 50), ("rubber", 6), (Alloy, 2) }),
            ("wear_army_kneepads", 3, DeployKind.None, new[] { ("metal_fragments", 50), ("rubber", 6), (Alloy, 3) }),
            ("expl_c4", 3, DeployKind.None, new[] { ("gunpowder", 200), ("metal_fragments", 100), ("scrap", 20), ("cloth", 5), (Alloy, 5) }),
            // антирадин — на химстоле из медспирта (D51)
            ("antirad", 2, DeployKind.Chem, new[] { ("medspirt", 2), ("charcoal", 10) }),
            // коктейль Молотова — на топляке (D51)
            ("expl_fire", 0, DeployKind.None, new[] { ("cloth", 10), ("fuel", 5) }),
        };

        /// <summary>Сталь при переработке (сверх обычного выхода): детали тира 2–3, дорогой ценный лом, пластины, элита.</summary>
        public static readonly Dictionary<string, int> RecycleAlloy = new()
        {
            ["comp_trigger"] = 2, ["comp_bolt_carrier"] = 2, ["comp_barrel_rifled"] = 2, ["comp_frame_pistol"] = 2, ["comp_body_smg"] = 2, ["comp_body_rifle"] = 2,
            ["val_motor"] = 1,
            ["comp_receiver_ak"] = 4, ["comp_barrel_chrome"] = 3, ["comp_gas_block"] = 3,
            ["val_radio_tubes"] = 2, ["val_gold_contacts"] = 3, ["val_computer_block"] = 8,
            ["plate_steel"] = 4, ["plate_ceramic"] = 6,
            ["weapon_saiga12"] = 12, ["weapon_mosin"] = 12, ["weapon_aps"] = 10, ["weapon_mp133"] = 4, ["weapon_sawnoff"] = 2,
            ["att_scope4x"] = 3, ["att_suppressor_pbs"] = 4, ["att_compensator_army"] = 3, ["att_mag_ext_army"] = 3,
        };

        // =====================================================================================================
        // Лут прогрессии (вызывается из Clothing.Loot — все источники уже размечены ключом)
        // =====================================================================================================
        public static readonly Dictionary<string, LootLine[]> Loot = new()
        {
            ["barrel"] = new LootLine[] { new(0.03, "flask_05", 1, 1), new(0.03, "canister_5", 1, 3, "crude") },
            ["barrel_rt"] = new LootLine[] { new(0.04, "flask_1", 1, 1), new(0.06, "canister_5", 2, 4, "crude"), new(0.03, "medspirt", 1, 1) },
            ["crate0"] = new LootLine[] { new(0.08, "flask_05", 1, 1), new(0.04, "flask_1", 1, 1), new(0.06, "canister_5", 2, 5, "fuel"), new(0.08, "medspirt", 1, 2) },
            ["crate1"] = new LootLine[] { new(0.5, Alloy, 2, 4), new(0.15, "canister_5", 3, 5, "fuel"), new(0.05, "canister_20", 6, 12, "fuel"), new(0.15, "medspirt", 1, 3), new(0.05, "syringe", 1, 1) },
            ["crate2"] = new LootLine[] { new(0.5, Alloy, 4, 8), new(0.15, "canister_20", 10, 20, "fuel"), new(0.08, "canister_5", 3, 5, "diesel"), new(0.15, "syringe", 1, 2), new(0.08, "medkit", 1, 1), new(0.03, "tool_jackhammer", 1, 1) },
            ["safe"] = new LootLine[] { new(0.6, Alloy, 3, 6), new(0.2, "syringe", 1, 2), new(0.1, "medkit", 1, 1), new(0.1, "canister_5", 3, 5, "diesel") },
            ["punch"] = new LootLine[] { new(1.0, Alloy, 3, 6), new(0.2, "canister_5", 3, 5, "fuel"), new(0.1, "syringe", 1, 1) },
            ["event"] = new LootLine[] { new(1.0, Alloy, 8, 15), new(0.3, "canister_20", 8, 15, "diesel"), new(0.3, "medkit", 1, 2), new(0.3, "syringe", 1, 2) },
        };

        // =====================================================================================================
        // Жидкости и ёмкости (литры). Одна ёмкость — одна жидкость.
        // =====================================================================================================
        public static readonly (string id, string name, string desc, int rarity, float value)[] LiquidDefs =
        {
            ("crude", "Сырая нефть", "Мазут из бочек и ржавых канистр. Самогонный аппарат перегоняет 2 л в 1 л топляка", 1, 1.5f),
            ("fuel", "Топляк", "Низкосортное топливо: генератор, бензопила, коктейль Молотова, аптечки и шприцы. Колонна перегоняет в дизель", 2, 3f),
            ("diesel", "Дизель", "Чистое топливо: буровая, отбойник, запуск РТ. Ректификационная колонна: топляк 2 л → дизель 1 л", 3, 8f),
            ("water_raw", "Сырая вода", $"Из бочки или водоёма. Пить можно, но ~30% отравиться — вскипяти на костре или в самогонном аппарате", 0, 0.3f),
            ("water_boiled", "Кипячёная вода", "Безопасная вода: F — выпить (пол-литра за раз)", 0, 1f),
        };

        /// <summary>Ёмкости: id, название, литров, только вода, верстак, цена, редкость, описание.</summary>
        public static readonly (string id, string name, float liters, bool waterOnly, int bench, (string, int)[] cost, int rarity, string desc)[] Containers =
        {
            ("flask_05", "Фляга 0,5 л", 0.5f, true, 0, new[] { ("metal_fragments", 15) }, 0, "Только вода. Пьётся с пояса или из рюкзака (F)"),
            ("flask_1", "Фляга 1 л", 1f, true, 1, new[] { ("metal_fragments", 25), ("cloth", 5) }, 1, "Только вода"),
            ("canister_5", "Канистра 5 л", 5f, false, 1, new[] { ("metal_fragments", 40), ("rubber", 2) }, 1, "Вода или топливо"),
            ("canister_20", "Канистра 20 л", 20f, false, 2, new[] { ("metal_fragments", 100), ("rubber", 4) }, 2, "Вода или топливо. Тяжёлая"),
        };

        public const float HeavyContainerLiters = 20f, HeavyContainerSpeed = 0.93f;   // полная канистра 20 л (больше половины) — −7% скорости

        // =====================================================================================================
        // Вода и отравление
        // =====================================================================================================
        public const float SipLiters = 0.5f;          // за одно F
        public const float WaterPerSip = 35f;         // как бутылка воды
        public const float RawPoisonChance = 0.3f;    // сырая вода: шанс отравиться
        public const float PoisonTime = 60f;          // с
        public const float PoisonWaterPerSec = 0.4f, PoisonHpPerSec = 0.15f;
        public const float BarrelDrink = 40f;         // бочка с водой — пить на месте
        public const float FillFromBarrel = 5f;       // бочка с водой: налить литров (Alt+E или «Налить»), потом пустеет
        public const float ShoreFillRange = 2.5f;     // водоём: налить, если вода ближе

        // =====================================================================================================
        // Нефть и топливо в мире
        // =====================================================================================================
        public const int OilBarrelsWorld = 40, CanisterPilesWorld = 30;   // раскладка по острову (свой генератор)
        public static readonly (int min, int max, float respawn) OilBarrel = (3, 8, 900f);     // бочка с мазутом: литров нефти
        public static readonly (int min, int max, float respawn) CanisterPile = (2, 5, 1200f); // ржавые канистры
        public static readonly (int min, int max) FuelPump = (20, 40);                          // насос Заправки: литров топляка за раз (держать E, шумно)
        public const float FuelPumpSeconds = 8f, FuelPumpNoise = 60f;

        // =====================================================================================================
        // Медицина
        // =====================================================================================================
        public static readonly (string id, string name, float heal, float cooldown, bool cure, int rarity, int stack, string desc)[] Meds =
        {
            ("medspirt", "Медспирт", 0, 0, false, 1, 20, "Спирт из ягод (самогонный аппарат). Аптечки, шприцы, антирадин"),
            ("medkit", "Аптечка", 60, 5, true, 3, 5, "Лечит 60 и снимает отравление. H — лечиться (сильное ранение — сначала аптечка)"),
            ("syringe", "Шприц", 35, 1, true, 3, 10, "Быстро: +35 за секунду, снимает отравление"),
        };

        // =====================================================================================================
        // Инструменты: добыча за удар (каменный топор: дерево 16, каменная кирка: камень 14, руда 10, сера 9)
        // =====================================================================================================
        public static readonly ToolDef[] Tools =
        {
            new() { Id = "tool_metal_hatchet", Name = "Металлический топор", Desc = "Рубит вдвое быстрее каменного", Dmg = 20, Interval = 0.75f, Range = 1.8f, Tree = 32, Stone = 4, Metal = 3, Sulfur = 3,
                    Bench = 1, Scrap = 30, Cost = new[] { ("metal_fragments", 75), ("wood", 50) } },
            new() { Id = "tool_metal_pickaxe", Name = "Металлическая кирка", Desc = "Камень и руда — вдвое быстрее каменной", Dmg = 18, Interval = 0.85f, Range = 1.8f, Tree = 4, Stone = 28, Metal = 20, Sulfur = 18,
                    Bench = 1, Scrap = 30, Cost = new[] { ("metal_fragments", 100), ("wood", 50) } },
            new() { Id = "tool_chainsaw", Name = "Бензопила «Дружба»", Desc = "Дерево ×4. Жрёт топляк (из канистры в рюкзаке), громкая", Dmg = 30, Interval = 0.35f, Range = 1.7f, Tree = 32, Stone = 1, Metal = 1, Sulfur = 1,
                    Fuel = "fuel", FuelPerHit = 0.05f, Noise = 60, Bench = 2, Scrap = 100, Time = 15,
                    Cost = new[] { ("metal_fragments", 150), ("comp_gears", 2), ("comp_spring", 1), ("val_motor", 1) } },
            new() { Id = "tool_jackhammer", Name = "Отбойный молоток", Desc = "Камень и руда ×4. Работает на дизеле, слышно издалека", Dmg = 25, Interval = 0.4f, Range = 1.7f, Tree = 1, Stone = 26, Metal = 19, Sulfur = 17,
                    Fuel = "diesel", FuelPerHit = 0.04f, Noise = 70, Bench = 3, Scrap = 150, Time = 20,
                    Cost = new[] { ("metal_fragments", 200), ("comp_gears", 3), ("val_motor", 1) } },
        };
        // бензопила и отбойник бьют чаще (0,35–0,4 с против 0,8–0,9) — за секунду ≈ ×4 к каменным

        // =====================================================================================================
        // Ток: генератор питает станции в радиусе (ближние первыми), пока хватает мощности
        // =====================================================================================================
        public const float PowerTick = 1f;            // пересчёт сети, с

        // =====================================================================================================
        // Станции (D51/D52/D54). Size/W/D — 16-building-grid.md; Bench, Cost, Scrap — дерево прогрессии (progression.html)
        // =====================================================================================================
        public static readonly StationDef[] Stations =
        {
            new() { Item = "deploy_still", Kind = DeployKind.Still, Name = "Самогонный аппарат", Sprite = "still", Size = Footprint.Half, W = 1.7f, D = 0.85f,
                    Bench = 1, Cost = new[] { ("metal_fragments", 50), ("comp_pipe", 2) }, Time = 12, Scrap = 30,
                    Burn = "wood", BurnPerMin = 6, Machine = "still", Smoke = true, Light = 3,
                    Desc = "Перегонка на дровах: нефть → топляк, ягоды → медспирт, сырая вода → кипячёная. Дымит — базу видно издалека" },
            new() { Item = "deploy_sewing", Kind = DeployKind.Sewing, Name = "Швейный стол", Sprite = "sewing", Size = Footprint.Half, W = 1.7f, D = 0.85f,
                    Bench = 1, Cost = new[] { ("wood", 100), ("cloth", 20) }, Time = 10, Scrap = 20,
                    Boost = new[] { "wear_rags_*", "wear_vatnik_*", "wear_leather_*", "bandage" }, BoostTime = 0.4f, Repair = "plate",
                    Desc = "Одежда тиров 0–1 вдвое быстрее, починка бронепластин (изолента + металл). E — крафт" },
            new() { Item = "deploy_chem", Kind = DeployKind.Chem, Name = "Химстол", Sprite = "chem", Size = Footprint.Half, W = 1.7f, D = 0.85f,
                    Bench = 2, Cost = new[] { ("metal_fragments", 150), ("comp_pipe", 1) }, Time = 15, Scrap = 60,
                    Power = 1, PowerSpeed = 1.33f, Boost = new[] { "gunpowder", "ammo_*", "expl_satchel", "expl_c4", "expl_fire" }, BoostTime = 0.5f, BoostCost = 0.75f,
                    Desc = "Порох, патроны и взрывчатка вдвое быстрее и на четверть дешевле; антирадин — только здесь. Под током — ещё быстрее" },
            new() { Item = "deploy_generator", Kind = DeployKind.Generator, Name = "Дизель-генератор", Sprite = "generator", Size = Footprint.Full, W = 1.85f, D = 1.85f,
                    Bench = 2, Cost = new[] { ("metal_fragments", 200), ("comp_gears", 3), ("comp_pipe", 2) }, Time = 20, Scrap = 60,
                    PowerOut = 6, PowerRadius = 15, Burn = "fuel", BurnPerMin = 0.2f, Slots = 6, Noise = 40,
                    Desc = "Топляк → ток на 15 м: лампы, электропечь, колонна, станки быстрее. Мощность 6. Канистру — в ячейки или бочку рядом" },
            new() { Item = "deploy_lamp", Kind = DeployKind.Lamp, Name = "Лампа", Sprite = "lamp", Size = Footprint.Quarter, W = 0.6f, D = 0.6f,
                    Bench = 2, Cost = new[] { ("metal_fragments", 20), ("val_copper_wire", 1) }, Time = 4, Scrap = 20,
                    Power = 1, NeedsPower = true, Light = 9, Desc = "Свет 9 м от генератора (ток 1)" },
            new() { Item = "deploy_floodlight", Kind = DeployKind.Floodlight, Name = "Прожектор", Sprite = "door_lamp_post", Size = Footprint.Quarter, W = 0.6f, D = 0.6f,
                    Bench = 2, Cost = new[] { ("metal_fragments", 50), ("val_copper_wire", 2), ("comp_lens", 1) }, Time = 6, Scrap = 0, Requires = new[] { "deploy_lamp" },
                    Power = 2, NeedsPower = true, Light = 18, Desc = "Яркий свет 18 м от генератора (ток 2)" },
            new() { Item = "deploy_efurnace", Kind = DeployKind.EFurnace, Name = "Электропечь", Sprite = "efurnace", Size = Footprint.Full, W = 1.85f, D = 1.85f,
                    Bench = 3, Cost = new[] { ("metal_fragments", 300), ("val_motor", 1), ("val_copper_wire", 2) }, Time = 20, Scrap = 100,
                    Power = 3, NeedsPower = true, Machine = "efurnace", Light = 2,
                    Desc = "Плавит руду вдвое быстрее печи, без дерева — только на токе (3)" },
            new() { Item = "deploy_guntable", Kind = DeployKind.GunTable, Name = "Оружейный стол", Sprite = "guntable", Size = Footprint.Full, W = 1.85f, D = 1.85f,
                    Bench = 3, Cost = new[] { ("metal_fragments", 300), (Alloy, 20), ("comp_gears", 4), ("comp_spring", 4) }, Time = 25, Scrap = 150,
                    Power = 1, PowerSpeed = 1.33f, Boost = new[] { "att_*", "weapon_*" }, BoostTime = 0.6f, Repair = "gun",
                    Desc = "Стволы тира 3 и армейские обвесы — только здесь; починка ствола маслом — до 100%. E — крафт" },
            new() { Item = "deploy_medtable", Kind = DeployKind.MedTable, Name = "Медстол", Sprite = "medtable", Size = Footprint.Half, W = 1.7f, D = 0.85f,
                    Bench = 3, Cost = new[] { ("metal_fragments", 150), ("cloth", 50) }, Time = 15, Scrap = 80,
                    Power = 1, PowerSpeed = 1.33f, Boost = new[] { "bandage" }, BoostTime = 0.5f,
                    Desc = "Аптечки и шприцы (медспирт, ткань, топляк). E — крафт" },
            new() { Item = "deploy_conveyor", Kind = DeployKind.Conveyor, Name = "Конвейер-сортировщик", Sprite = "conveyor_sorter", Size = Footprint.Quarter, W = 0.85f, D = 0.85f,
                    Bench = 3, Cost = new[] { ("metal_fragments", 100), ("val_motor", 1), ("comp_circuit", 1) }, Time = 15, Scrap = 120,
                    Power = 1, NeedsPower = true, Slots = 6,
                    Desc = "Под током раз в 2 с перекладывает стопку из соседнего хранилища «сзади» в соседнее «спереди» (R — поворот). Вещи в его ячейках — фильтр" },
            new() { Item = "deploy_rectifier", Kind = DeployKind.Rectifier, Name = "Ректификационная колонна", Sprite = "rectifier", Size = Footprint.Full, W = 1.85f, D = 1.85f,
                    Bench = 3, Cost = new[] { ("metal_fragments", 250), ("comp_pipe", 4), ("comp_gears", 2), ("val_motor", 1) }, Time = 25, Scrap = 100,
                    Power = 2, NeedsPower = true, Machine = "rectifier", Noise = 20,
                    Desc = "Топляк 2 л → дизель 1 л. Нужен ток (2)" },
            new() { Item = "deploy_campfire", Kind = DeployKind.Campfire, Name = "Костёр", Sprite = null, Size = Footprint.Quarter, W = 0.8f, D = 0.8f,
                    Bench = 0, Cost = new[] { ("wood", 30), ("stone", 10) }, Time = 3,
                    Burn = "wood", BurnPerMin = 4, Machine = "campfire", Slots = 6, Light = 7, Warm = true, Smoke = true,
                    Desc = "Греет и светит. Кипятит сырую воду во фляге или канистре: положи ёмкость и дрова" },
            new() { Item = "deploy_barrel", Kind = DeployKind.Barrel, Name = "Бочка 200 л", Sprite = "barrel_200", Size = Footprint.Full, W = 1.2f, D = 1.2f,
                    Bench = 2, Cost = new[] { ("metal_fragments", 150) }, Time = 10, Tank = 200,
                    Desc = "Склад жидкости на базе. Генератор, станки и буровая в 3 м берут топливо из бочки" },
            new() { Item = "deploy_box_large", Kind = DeployKind.BoxLarge, Name = "Большой ящик", Sprite = "box_large", Size = Footprint.Half, W = 1.7f, D = 0.85f,
                    Bench = 1, Cost = new[] { ("wood", 250), ("metal_fragments", 50) }, Time = 8, Scrap = 20, Slots = 48,
                    Desc = "Хранилище на 48 ячеек" },
            new() { Item = "deploy_drill", Kind = DeployKind.Drill, Name = "Буровая установка", Sprite = "drill_rig", Size = Footprint.Full, W = 1.85f, D = 1.85f,
                    Bench = 3, Cost = new[] { ("metal_fragments", 400), ("comp_gears", 6), ("val_motor", 2), ("comp_pipe", 4) }, Time = 30, Scrap = 200,
                    Burn = "diesel", BurnPerMin = 0.5f, OnNode = true, Slots = 12, Noise = 80, Smoke = true,
                    Desc = "Ставится на землю вплотную к рудному/серному/каменному узлу; на дизеле сама добывает 10 руды за 10 с. Видна и слышна издалека" },
            // токарный станок (D46) — предмет заведён в Weaponry; здесь только ток: под током точит в 1,5 раза быстрее
            new() { Item = "deploy_lathe", Kind = DeployKind.Lathe, Name = "Токарный станок", Sprite = "lathe", Size = Footprint.Full, W = 1.85f, D = 1.85f,
                    Registered = true, Power = 2, PowerSpeed = 1.5f, Noise = 45 },
        };

        public const float StationRange = 2.5f;       // от края станции: рядом — её рецепты и ускорение
        public const float DrillNodeRange = 2.6f;     // буровая: до ближайшего узла
        public const float DrillPeriod = 10f; public const int DrillYield = 10;
        public const float TankRange = 3f;            // станок берёт топливо из бочки в 3 м
        public const float ConveyorPeriod = 2f; public const int ConveyorStack = 20; public const float ConveyorReach = 1.6f;

        /// <summary>Рецепты станков (материалы в ячейках станка → выход). Жидкость — литры в ёмкостях внутри станка.</summary>
        public static readonly MachineRecipe[] Machines =
        {
            new() { Machine = "still", Output = "fuel", In = new[] { ("crude", 2) }, Time = 15 },
            new() { Machine = "still", Output = "medspirt", In = new[] { ("berries", 10) }, Time = 20 },
            new() { Machine = "still", Output = "water_boiled", In = new[] { ("water_raw", 1) }, Time = 6 },
            new() { Machine = "campfire", Output = "water_boiled", In = new[] { ("water_raw", 1) }, Time = 10 },
            new() { Machine = "efurnace", Output = "metal_fragments", In = new[] { ("metal_ore", 1) }, Time = 0.2f },
            new() { Machine = "efurnace", Output = "sulfur", In = new[] { ("sulfur_ore", 1) }, Time = 0.2f },
            new() { Machine = "rectifier", Output = "diesel", In = new[] { ("fuel", 2) }, Time = 25 },
        };

        /// <summary>Новые рецепты (крафт у верстака/станции): выход, штук, время, верстак, станция, цена.</summary>
        public static readonly (string output, int count, float time, int bench, DeployKind station, (string, int)[] cost)[] Recipes =
        {
            ("medkit", 1, 8, 3, DeployKind.MedTable, new[] { ("medspirt", 4), ("cloth", 10), ("fuel", 2) }),
            ("syringe", 1, 5, 3, DeployKind.MedTable, new[] { ("medspirt", 2), ("fuel", 1) }),
            ("bars", 1, 5, 1, DeployKind.None, new[] { ("metal_fragments", 50) }),
            // армейские обвесы — только на оружейном столе (раньше только лут)
            ("att_scope4x", 1, 15, 3, DeployKind.GunTable, new[] { ("metal_fragments", 150), ("comp_lens", 3), ("comp_gears", 2), (Alloy, 5) }),
            ("att_suppressor_pbs", 1, 15, 3, DeployKind.GunTable, new[] { ("metal_fragments", 150), ("comp_barrel_rifled", 1), ("comp_tape", 2), (Alloy, 8) }),
            ("att_compensator_army", 1, 10, 3, DeployKind.GunTable, new[] { ("metal_fragments", 120), ("comp_gears", 2), (Alloy, 5) }),
            ("att_mag_ext_army", 1, 10, 3, DeployKind.GunTable, new[] { ("metal_fragments", 120), ("comp_spring", 3), (Alloy, 5) }),
        };

        /// <summary>Узлы дерева изучения для новых вещей (лом; станции — из StationDef.Scrap, инструменты — из ToolDef.Scrap).</summary>
        public static readonly (string id, int bench, int scrap, string[] req)[] Tech =
        {
            ("medkit", 3, 80, new[] { "deploy_medtable" }),
            ("syringe", 3, 80, new[] { "deploy_medtable" }),
            ("bars", 1, 20, null),
            ("att_scope4x", 3, 60, new[] { "deploy_guntable" }),
            ("att_suppressor_pbs", 3, 60, new[] { "deploy_guntable" }),
            ("att_compensator_army", 3, 40, new[] { "deploy_guntable" }),
            ("att_mag_ext_army", 3, 40, new[] { "deploy_guntable" }),
        };

        // =====================================================================================================
        // Починка на станциях
        // =====================================================================================================
        public const float GunRepairBench = 0.5f, GunRepairTable = 1f;   // доля износа за одно масло: верстак / оружейный стол
        public static readonly (string, int)[] PlateRepairCost = { ("comp_tape", 1), ("metal_fragments", 30) };

        // =====================================================================================================
        // Постройки: бронеуровень (5) поверх стали и решётки/ставни на окна
        // =====================================================================================================
        public const string ArmorTierName = "броня";
        public const float ArmorTierHp = 3500;
        public static readonly (string, int)[] ArmorWallCost = { (Alloy, 25), ("metal_fragments", 50) };            // стена; фундамент ×1,5, окно/проём ×0,7
        public static readonly (string, int)[] ArmorDoorCost = { ("metal_fragments", 200), (Alloy, 40), ("comp_gears", 4) };
        public const float ArmorDoorBulletMult = 0.03f;

        // =====================================================================================================
        // Ценность для ботов (в ломах) и редкость
        // =====================================================================================================
        public static float Value(string id) => id switch
        {
            Alloy => 8f, "medspirt" => 3f, "medkit" => 25f, "syringe" => 15f, "bars" => 15f,
            "flask_05" => 3f, "flask_1" => 5f, "canister_5" => 10f, "canister_20" => 25f,
            _ => -1f,
        };
    }
}
