using System;
using System.Collections.Generic;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Жидкости в ёмкостях (D54): фляги, канистры (ItemSlot.Liquid/Liters), бочка 200 л (Deployable.Store["liq:<id>"]).
    /// Рецепты и счётчики работают с жидкостью «в литрах»: Actor.Count("fuel") — сумма по ёмкостям, Take — сливает.
    /// Числа — Data/Progression.cs (Prog.Containers, Prog.LiquidDefs).
    /// </summary>
    public static class Liquids
    {
        static readonly HashSet<string> Ids = new() { "crude", "fuel", "diesel", "water_raw", "water_boiled" };
        public static bool Is(string id) => id != null && Ids.Contains(id);
        public static bool IsWater(string id) => id is "water_raw" or "water_boiled";
        const float Eps = 1e-3f;

        public static float Room(ItemSlot s) => s?.Def is ItemDef d && d.Capacity > 0 ? Math.Max(0, d.Capacity - s.Liters) : 0;

        /// <summary>Можно ли налить эту жидкость в эту ёмкость (пустая или та же; фляга — только вода).</summary>
        public static bool Fits(ItemSlot s, string liquid)
        {
            if (s?.Def is not ItemDef d || d.Capacity <= 0 || !Is(liquid)) return false;
            if (d.WaterOnly && !IsWater(liquid)) return false;
            return s.Liters <= Eps || s.Liquid == liquid;
        }

        public static float Total(IList<ItemSlot> slots, string liquid)
        {
            float n = 0;
            foreach (var s in slots) if (s != null && s.Liquid == liquid) n += s.Liters;
            return n;
        }

        /// <summary>Слить до liters жидкости из ёмкостей (сначала из почти пустых). Возвращает слитое.</summary>
        public static float Drain(IList<ItemSlot> slots, string liquid, float liters)
        {
            float got = 0;
            while (got < liters - Eps)
            {
                ItemSlot best = null;
                foreach (var s in slots) if (s != null && s.Liquid == liquid && s.Liters > Eps && (best == null || s.Liters < best.Liters)) best = s;
                if (best == null) break;
                float t = Math.Min(best.Liters, liters - got);
                best.Liters -= t; got += t;
                if (best.Liters <= Eps) { best.Liters = 0; best.Liquid = null; }
            }
            return got;
        }

        /// <summary>Налить до liters в ёмкости (сначала в начатые с той же жидкостью). Возвращает налитое.</summary>
        public static float Pour(IList<ItemSlot> slots, string liquid, float liters)
        {
            float put = 0;
            for (int pass = 0; pass < 2 && put < liters - Eps; pass++)
                foreach (var s in slots)
                {
                    if (put >= liters - Eps) break;
                    if (s == null || !Fits(s, liquid) || (pass == 0 && s.Liquid != liquid)) continue;
                    float t = Math.Min(Room(s), liters - put);
                    if (t <= Eps) continue;
                    s.Liquid = liquid; s.Liters += t; put += t;
                }
            return put;
        }

        public static float RoomFor(IList<ItemSlot> slots, string liquid)
        {
            float r = 0;
            foreach (var s in slots) if (Fits(s, liquid)) r += Room(s);
            return r;
        }

        /// <summary>Перелить из одной ёмкости в другую сколько влезет.</summary>
        public static float Transfer(ItemSlot src, ItemSlot dst)
        {
            if (src?.Liquid == null || src.Liters <= Eps || !Fits(dst, src.Liquid)) return 0;
            float t = Math.Min(src.Liters, Room(dst));
            if (t <= Eps) return 0;
            dst.Liquid = src.Liquid; dst.Liters += t;
            src.Liters -= t;
            if (src.Liters <= Eps) { src.Liters = 0; src.Liquid = null; }
            return t;
        }

        /// <summary>Новая ёмкость с жидкостью (лут, «give»).</summary>
        public static ItemSlot Make(string container, string liquid, float liters)
        {
            var s = new ItemSlot(container, 1);
            if (liquid != null && Fits(s, liquid)) { s.Liquid = liquid; s.Liters = Math.Min(liters, s.Def.Capacity); }
            return s;
        }

        /// <summary>Ёмкость под столько литров: канистра 5 или 20 л.</summary>
        public static string ContainerFor(float liters) => liters <= 5 ? "canister_5" : "canister_20";

        public static int TakeFromActor(Actor a, string liquid, int n)
        {
            float got = Drain(a.Pack, liquid, n);
            if (got < n - Eps) got += Drain(a.Belt, liquid, n - got);
            return (int)(got + Eps);
        }

        /// <summary>Жидкость игроку: в свои ёмкости; не влезло — в новую канистру (как будто нашёл с ней).</summary>
        public static int AddToActor(Actor a, string liquid, int n)
        {
            float left = n - Pour(a.Pack, liquid, n);
            if (left > Eps) left -= Pour(a.Belt, liquid, left);
            while (left > Eps)
            {
                var c = Make(ContainerFor(left), liquid, left);
                left -= c.Liters;
                if (!a.AddSlot(c)) a.Overflow.Add(c);
            }
            return n;
        }

        // ---------- бочка 200 л: жидкость в Store ----------
        public static (string liquid, float liters) Tank(Deployable d)
        {
            foreach (var kv in d.Store) if (kv.Key.StartsWith("liq:") && kv.Value > Eps) return (kv.Key.Substring(4), kv.Value);
            return (null, 0);
        }
        public static float TankCap(Deployable d) => StationDefs.Get(d.Kind)?.Tank ?? 0;

        public static float TankPour(Deployable d, string liquid, float liters)
        {
            var (cur, have) = Tank(d);
            if (cur != null && cur != liquid) return 0;
            float t = Math.Min(liters, TankCap(d) - have);
            if (t <= Eps) return 0;
            d.Store["liq:" + liquid] = have + t;
            return t;
        }

        public static float TankDrain(Deployable d, string liquid, float liters)
        {
            var (cur, have) = Tank(d);
            if (cur != liquid) return 0;
            float t = Math.Min(liters, have);
            d.Store["liq:" + liquid] = have - t;
            if (have - t <= Eps) d.Store.Remove("liq:" + liquid);
            return t;
        }

        /// <summary>Подпись для ячейки: «Топляк 3,5/5 л».</summary>
        public static string Label(ItemSlot s)
        {
            if (s?.Def is not ItemDef d || d.Capacity <= 0) return null;
            return s.Liquid == null || s.Liters <= Eps ? $"пусто · {d.Capacity:0.#} л" : $"{Items.Name(s.Liquid)} {s.Liters:0.#}/{d.Capacity:0.#} л";
        }
    }
}
