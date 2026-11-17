using System;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Пополнение по ходу игры (D72, пользователь: «пусть пополняют патроны и хил по мере игры»): патронов к своему стволу
    /// меньше половины кита или хила меньше двух — домой (до 400 м): взять из ящика, а нет — скрафтить (патроны с порохом,
    /// стрелы, бинты из ткани, шприц/аптечка на медстоле). Попытка — до 2 мин, не вышло — пауза 5 мин.
    /// </summary>
    public sealed partial class PlayerBot
    {
        /// <summary>Чем лечиться при себе: бинты, шприцы, аптечки (лечение берёт любое — Stations.Heal).</summary>
        int Heals => _me.Count("bandage") + _me.Count("syringe") + _me.Count("medkit");

        float _resupStart = -999, _resupBlock = -999, _resupCheckAt = -99;
        string _resupWhy;

        /// <summary>Что пополнить (null — всё в порядке).</summary>
        string ResupplyNeed()
        {
            if (Ranged() is string g)
            {
                string ammo = WeaponAmmo(g);
                if (AmmoLeft(g) < KitWant(ammo) / 2) return ammo;
            }
            if (Heals < 2) return "bandage";
            return null;
        }

        float ResupplyScore()
        {
            if (_resupStart > 0)
            {
                if (_w.Time - _resupStart < 120 && ResupplyNeed() != null) return _resupScore;
                if (ResupplyNeed() != null) { _resupBlock = _w.Time + 300; Thought($"пополнить {_resupWhy} не вышло за 2 мин — позже"); LStat("пополнение: не вышло"); }
                else LStat("пополнение: готово");
                _resupStart = -999;
                return 0;
            }
            if (_w.Time < _resupBlock || _threat != null || _raid != null || _w.Time - _resupCheckAt < 2f) return 0;
            _resupCheckAt = _w.Time;
            if (MyBox() is not Deployable box || Vector2.Distance(_me.Pos, box.Pos) > 400) return 0;
            var need = ResupplyNeed();
            if (need == null) return 0;
            bool gunEmpty = Ranged() is string g && AmmoLeft(g) < 6;
            // со стволом без патронов — не боец: домой выше любых выходов; хил — как сдача добычи
            _resupScore = gunEmpty ? 90 : need == "bandage" ? 76 : 72;
            _resupStart = _w.Time;
            _resupWhy = need == "bandage" ? "хил" : Items.Name(need);
            Thought($"пополнение: {(_resupWhy == "хил" ? $"хила {Heals}" : $"{_resupWhy} мало")} — домой за запасом");
            LStat("пополнение: иду");
            return _resupScore;
        }
        float _resupScore;

        bool ResupplyAct(ref InputCmd cmd)
        {
            if (ResupplyStep(ref cmd)) return true;
            if (ResupplyNeed() != null) { _resupBlock = _w.Time + 300; LStat("пополнение: нечем"); }   // не из чего — не держать цель
            _resupStart = -999;
            return false;
        }

        bool ResupplyStep(ref InputCmd cmd)
        {
            if (MyBox() is not Deployable box) return false;
            var need = ResupplyNeed();
            if (need == null) return false;
            Doing = $"пополняет {_resupWhy}";
            if (need == "bandage")
            {
                foreach (var h in new[] { "medkit", "syringe", "bandage" })
                    if (TakeFromBox(box, h, Math.Min(_me.Count(h) + InBox(box, h), h == "bandage" ? 4 : 2), ref cmd)) return true;
                if (Heals >= 2) return false;
                // медстол (верстак 3): шприц из медспирта, если изучен
                if (Tech.Knows(_me, "syringe") && Stock("medspirt") >= 2 && Stock("fuel") >= 1 && MyBenchLevel() >= 3) return CraftAtBench("syringe", box, ref cmd);
                if (_me.Count("cloth") >= 6) return Craft("bandage", ref cmd) || true;
                if (TakeFromBox(box, "cloth", 24, ref cmd)) return true;
                Doing = "пополняет хил: ищет коноплю на бинты";
                return UsePickup(PickupKind.Hemp, ref cmd);
            }
            string want = need;
            int n = KitWant(want);
            if (TakeFromBox(box, want, n, ref cmd)) return true;
            if (want == "arrow")
            {
                if (Craft("arrow", ref cmd)) return true;
                if (TakeFromBox(box, "wood", 150, ref cmd) || TakeFromBox(box, "stone", 60, ref cmd)) return true;
                return !Need(_me.Count("wood") < 25 ? "wood" : "stone", 100, ref cmd) || true;
            }
            int ri = Recipe(want);
            if (ri < 0 || Recipes.All[ri].Bench > MyBenchLevel() || (Tech.Get(want) != null && !_me.Known.Contains(want))) return false;   // не скрафтить — только лут
            int powder = 0;
            foreach (var (id, k) in Recipes.All[ri].Cost) if (id == "gunpowder") powder = k;
            if (powder > 0 && _me.Count("gunpowder") < powder)
            {
                if (TakeFromBox(box, "gunpowder", powder * 4, ref cmd)) return true;
                return MakeGunpowder(powder, box, MyFurnace(), ref cmd);
            }
            return CraftAtBench(want, box, ref cmd);
        }
    }
}
