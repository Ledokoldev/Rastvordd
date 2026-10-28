using System;
using System.Collections.Generic;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Экономика лома (D49, docs/design/15-economy.md): «ценный лом» — вещи только на переработку, раскладка по тирам РТ,
    /// учёт дохода лома по источникам (для прогонов ботов: летопись пишет строки "econ").
    /// Лом — в основном из переработчика: бочки и ящики дают мало голого лома, зато компоненты и ценный лом.
    /// </summary>
    public static class Economy
    {
        /// <summary>Ценный лом по возрастанию цены: id, название, описание, редкость, стопка, выход переработки.</summary>
        public static readonly (string id, string name, string desc, int rarity, int stack, (string, int)[] recycle)[] Valuables =
        {
            ("val_copper_wire", "Медный провод", "Моток медного провода из стен и щитков. Только в переработчик: 4 лома и 5 металла", 1, 20, new[] { ("scrap", 4), ("metal_fragments", 5) }),
            ("val_radio_parts", "Радиодетали", "Россыпь конденсаторов, резисторов и транзисторов. Только в переработчик: 8 лома", 2, 20, new[] { ("scrap", 8) }),
            ("val_battery", "Аккумулятор", "Тяжёлый автомобильный аккумулятор. Только в переработчик: 12 лома и 10 металла", 2, 5, new[] { ("scrap", 12), ("metal_fragments", 10) }),
            ("val_motor", "Электромотор", "Мотор с медной обмоткой — с Подстанции и из депо. Только в переработчик: 20 лома и 30 металла", 3, 5, new[] { ("scrap", 20), ("metal_fragments", 30) }),
            ("val_radio_tubes", "Радиолампы", "Армейские радиолампы в вате. Только в переработчик: 40 лома", 3, 10, new[] { ("scrap", 40) }),
            ("val_gold_contacts", "Золотые контакты", "Позолоченные разъёмы с военной техники. Только в переработчик: 60 лома", 4, 10, new[] { ("scrap", 60) }),
            ("val_computer_block", "Блок ЭВМ", "Блок военной ЭВМ — платы с золотом и редкими металлами. Только в переработчик: 120 лома", 4, 3, new[] { ("scrap", 120) }),
        };

        public static bool IsValuable(string id) => id != null && id.StartsWith("val_");

        /// <summary>Сколько лома даёт переработка одной штуки (для ботов и учёта).</summary>
        public static int ScrapYield(string id)
        {
            int n = 0;
            foreach (var (cid, c) in Items.RecycleYield(id)) if (cid == "scrap") n += c;
            return n;
        }

        internal static void Register(Func<string, string, ItemCategory, ItemDef> add)
        {
            foreach (var (id, name, desc, rarity, stack, rec) in Valuables)
            {
                add(id, name, ItemCategory.Component);
                // ценность для ботов ≈ лом с переработки (+ металл по 0,3)
                float value = 0;
                foreach (var (cid, c) in rec) value += cid == "scrap" ? c : c * 0.3f;
                Weaponry.Info[id] = new Weaponry.Meta { Desc = desc, Rarity = rarity, Value = value, Stack = stack, Recycle = rec };
            }
        }

        // ---------- раскладка по источникам ----------

        static string Pick(Random rng, params (string id, int w)[] table)
        {
            int sum = 0;
            foreach (var (_, w) in table) sum += w;
            int r = rng.Next(sum);
            foreach (var (id, w) in table) { if (r < w) return id; r -= w; }
            return table[0].id;
        }

        /// <summary>Ценный лом тира 0 (бочки, ящики РТ 0): провод, радиодетали, редко аккумулятор.</summary>
        public static string Tier0(Random rng) => Pick(rng, ("val_copper_wire", 60), ("val_radio_parts", 30), ("val_battery", 10));
        /// <summary>Ценный лом тира 1 (ящики РТ 0 получше, РТ с охраной, крейты): радиодетали, аккумулятор, мотор.</summary>
        public static string Tier1(Random rng) => Pick(rng, ("val_copper_wire", 20), ("val_radio_parts", 35), ("val_battery", 30), ("val_motor", 15));
        /// <summary>Ценный лом тира 2 (склад, сейфы, крейты перфокарт): аккумулятор, мотор.</summary>
        public static string Tier2(Random rng) => Pick(rng, ("val_radio_parts", 25), ("val_battery", 35), ("val_motor", 40));
        /// <summary>Высокий лом (пока только сброс и закрытый ящик; позже РТ 2, ПВО, «Объект 42», вертолёт).</summary>
        public static string High(Random rng) => Pick(rng, ("val_radio_tubes", 60), ("val_gold_contacts", 30), ("val_computer_block", 10));

        // ---------- учёт дохода (прогоны ботов) ----------

        /// <summary>Лом, полученный игроками и ботами, по источникам ("сырой: бочка", "переработчик" …). Только для отчёта.</summary>
        public static readonly Dictionary<string, double> Ledger = new();
        static readonly object Lock = new();

        public static void Book(string source, double scrap)
        {
            if (scrap <= 0) return;
            lock (Lock) Ledger[source] = Ledger.GetValueOrDefault(source) + scrap;
        }

        /// <summary>Взяли предмет из источника: голый лом — как есть, детали и ценный лом — «лом в переработке» (сколько дадут).</summary>
        public static void BookTake(string source, string id, int n)
        {
            if (n <= 0 || source == null) return;
            In(source.StartsWith("ящик РТ") || source.StartsWith("ящик ") ? "ящики РТ" : source, id, n);
            if (id == "scrap") Book("сырой: " + source, n);
            else if (IsValuable(id)) Book("ценный лом: " + source, ScrapYield(id) * n);
            else if (id.StartsWith("comp_")) Book("детали: " + source, ScrapYield(id) * n);
        }

        public static Dictionary<string, double> Snapshot() { lock (Lock) return new Dictionary<string, double>(Ledger); }

        /// <summary>Поток предметов у игроков и ботов (прогоны, scripts/item-flow.py): приход и расход по источнику, ключ «источник|id».</summary>
        public static readonly Dictionary<string, double> ItemIn = new(), ItemOut = new();
        public static void In(string src, string id, double n) { if (n > 0 && id != null) lock (Lock) ItemIn[src + "|" + id] = ItemIn.GetValueOrDefault(src + "|" + id) + n; }
        public static void Out(string src, string id, double n) { if (n > 0 && id != null) lock (Lock) ItemOut[src + "|" + id] = ItemOut.GetValueOrDefault(src + "|" + id) + n; }
        public static (Dictionary<string, double> i, Dictionary<string, double> o) FlowSnapshot() { lock (Lock) return (new(ItemIn), new(ItemOut)); }
    }
}
