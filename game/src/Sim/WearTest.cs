using System;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Самопроверка одежды D47 (отладка: --simtest=N --weartest): зоны попадания, потолки, пластина спереди/сзади,
    /// разрушение пластины, доза радиации ниже/выше порога, порог холода, потолок скорости, ближний бой, надеть/снять,
    /// сейв пластины. Работает на отладочном мире, сейв игрока не трогает.
    /// </summary>
    public sealed partial class World
    {
        public void WearSelfTest(Action<string> log)
        {
            int ok = 0, bad = 0;
            void Check(bool cond, string what) { if (cond) ok++; else bad++; log($"ОДЕЖДА-ТЕСТ {(cond ? "OK " : "ПРОВАЛ")} {what}"); }
            bool Near(float a, float b, float eps = 0.002f) => MathF.Abs(a - b) < eps;

            var t = new Actor { Id = NextId(), Name = "Тест-манекен", Team = Team.Player, Pos = PlayerSpawns[0], Facing = 0 };   // смотрит по +X
            Actors.Add(t);
            try
            {
                // 1. зоны: пороги 0,12/0,28 м для тела 0,40 м → ×0,35/0,40 = 0,105/0,245 для нашего радиуса
                float r = t.Radius;
                Check(ZoneOf(0.0f, r) == HitZone.Head && ZoneOf(0.104f, r) == HitZone.Head, $"голова: смещение < {Clothing.HeadR * r / Clothing.DocBodyRadius:0.000} м");
                Check(ZoneOf(0.106f, r) == HitZone.Torso && ZoneOf(0.244f, r) == HitZone.Torso, $"корпус: {Clothing.HeadR * r / Clothing.DocBodyRadius:0.000}–{Clothing.TorsoR * r / Clothing.DocBodyRadius:0.000} м");
                Check(ZoneOf(0.246f, r) == HitZone.Limbs && ZoneOf(0.34f, r) == HitZone.Limbs, "конечности: дальше");

                // попадание по голому: спереди, по центру — голова ×1,8; сбоку 0,3 м — конечности ×0,6
                void Shoot(float offset, Vector2 dir, float dmg = 30)
                {
                    var n = new Vector2(-dir.Y, dir.X);
                    var from = t.Pos - dir * 5 + n * offset;
                    BulletHit(t, from, dir, dmg, -1, t.Pos);
                }
                t.Hp = 100; Shoot(0, -Vector2.UnitX);
                Check(Near(t.Hp, 100 - 54), $"голый, в голову 30 → {100 - t.Hp:0.0} (ждём 54)");
                t.Hp = 100; Shoot(0.3f, -Vector2.UnitX);
                Check(Near(t.Hp, 100 - 18), $"голый, в руку/ногу 30 → {100 - t.Hp:0.0} (ждём 18)");

                // 2. защита по зонам и потолки
                Dress(t, "wear_sobr_sfera", "wear_sobr_nomex", "wear_sobr_kora", "wear_sobr_suit", "wear_sobr_elbowpads", "wear_sobr_kneepads", "wear_sobr_pants", "wear_sobr_boots", "wear_sobr_gloves");
                float head = Clothing.Zone(t, HitZone.Head), limbs = Clothing.Zone(t, HitZone.Limbs);
                Check(Near(head, 1 - 0.55f * 0.92f), $"СОБР голова = 1 − 0,55×0,92 = {head:0.000}");
                Check(Near(limbs, 1 - 0.8f * 0.94f * 0.9f * 0.8f * 0.92f), $"СОБР конечности = {limbs:0.000}");
                Check(Near(Clothing.CapZone(HitZone.Head, 0.9f), 0.70f) && Near(Clothing.CapZone(HitZone.Torso, 0.9f), 0.75f) && Near(Clothing.CapZone(HitZone.Limbs, 0.9f), 0.60f),
                      "потолки: голова 70%, корпус 75%, конечности 60%");
                Check(Near(Clothing.SpeedPenalty(t), -0.07f), $"СОБР скорость {Clothing.SpeedPenalty(t) * 100:0}% (ждём −7)");
                Check(Near(Clothing.CapSpeed(-0.25f), -0.15f) && Near(SpeedMul(t), 0.93f), "потолок скорости −15%");

                // 3. пластина спереди / сзади: смотрит по +X; пуля летит по −X — спереди, по +X — в спину
                t.ClearAll();
                Dress(t, "wear_army_6b2", "plate_homemade");   // одна пластина — в слот «перед»
                float front = Clothing.Zone(t, HitZone.Torso, SideOf(t, -Vector2.UnitX));
                float back = Clothing.Zone(t, HitZone.Torso, SideOf(t, Vector2.UnitX));
                Check(SideOf(t, -Vector2.UnitX) == 0 && SideOf(t, Vector2.UnitX) == 1, "сторона: навстречу взгляду — перед, вдогонку — спина");
                Check(Near(front, 1 - 0.7f * 0.8f) && Near(back, 0.30f), $"6Б2 + кустарная спереди: перед {front * 100:0}% (44), спина {back * 100:0}% (30)");
                Check(Near(ArmorReduction(t), front), "HUD «Броня» = корпус спереди");

                // 4. износ и разрушение пластины: 150 урона; АКМ 34 в корпус — пятый выстрел её ломает
                int hits = 0;
                for (; hits < 10 && t.Plates[0] != null; hits++) { t.Hp = 100; Shoot(0.15f, -Vector2.UnitX, 34); }
                bool broke = Events.Exists(e => e.Kind == SimEventKind.Wear && e.Text == "plate_break" && e.ActorId == t.Id);
                Check(hits == 5 && broke, $"кустарная пластина разбита на {hits}-м попадании (ждём 5), событие plate_break: {broke}");
                t.Hp = 100; Shoot(0.15f, -Vector2.UnitX, 34);
                Check(Near(t.Hp, 100 - 34 * 0.7f), $"без пластины корпус 30%: урон {100 - t.Hp:0.0} (ждём 23,8)");
                t.Hp = 100;
                Check(Near(MeleeAfterArmor(t, new Actor { Pos = t.Pos + new Vector2(1, 0) }, 40), 40 * (1 - 0.15f)), "ближний бой: защита корпуса ×0,5");

                // 5. радиация: Карьер 25, тряпьё (рад 5) — доза 2/с; ОЗК (рад 67) — не идёт и выводится
                t.ClearAll(); t.Hp = 100;
                Dress(t, "wear_rags_bandana", "wear_rags_shirt", "wear_rags_pants", "wear_rags_wraps", "wear_rags_mittens");
                Check(Near(Clothing.RadProt(t), 5) && Near(Clothing.Warmth(t), 10), $"тряпьё: рад {Clothing.RadProt(t):0} (5), тепло {Clothing.Warmth(t):0} (10)");
                t.RadHere = 25; t.FeltTemp = 30;
                for (int i = 0; i < 300; i++) EnvTick(t, Dt);   // 10 с
                Check(Near(t.RadDose, 20, 0.05f), $"тряпьё на Карьере: доза за 10 с = {t.RadDose:0.0} (ждём 20)");
                t.RadDose = 260; float hp0 = t.Hp; EnvTick(t, 1f);
                Check(Near(hp0 - t.Hp, 1, 0.01f), "доза ≥ 250 → 1 hp/с");
                t.RadDose = 520; hp0 = t.Hp; EnvTick(t, 1f);
                Check(Near(hp0 - t.Hp, 3, 0.01f), "доза ≥ 500 → 3 hp/с");
                Check(HealMul(t) < 1, "доза ≥ 100 → тошнота (лечение ×0,5)");
                t.ClearAll(); t.Hp = 100;
                Dress(t, "wear_ozk_hood", "wear_ozk_gp5", "wear_ozk_cape", "wear_ozk_gloves", "wear_ozk_stockings");
                t.RadHere = 25; t.FeltTemp = 30; t.RadDose = 10;
                for (int i = 0; i < 300; i++) EnvTick(t, Dt);
                Check(Near(Clothing.RadProt(t), 67) && t.RadDose < 10 && Near(t.RadDose, 5, 0.05f), $"ОЗК (рад {Clothing.RadProt(t):0}) на Карьере: доза не идёт, выводится 0,5/с → {t.RadDose:0.0}");

                // 6. холод: воздух + тепло ≥ +15 — комфортно
                t.ClearAll(); t.Hp = 100; t.RadHere = 0;
                t.FeltTemp = 15; for (int i = 0; i < 300; i++) EnvTick(t, Dt);
                Check(t.Cold == 0, "+15 °C — переохлаждения нет");
                t.FeltTemp = 14; for (int i = 0; i < 300; i++) EnvTick(t, Dt);
                Check(Near(t.Cold, 0.2f, 0.01f), $"+14 °C — копится: {t.Cold:0.00} за 10 с (ждём 0,2)");
                t.Cold = 100; t.FeltTemp = 5; hp0 = t.Hp; EnvTick(t, 1f);
                Check(Near(hp0 - t.Hp, 0.15f + 10 * 0.02f, 0.01f), $"полное переохлаждение при +5 °C: −{hp0 - t.Hp:0.00} hp/с (0,35)");
                Dress(t, "wear_vatnik_ushanka", "wear_vatnik_scarf", "wear_vatnik_telogreika", "wear_vatnik_pants", "wear_vatnik_valenki", "wear_vatnik_mittens");
                Check(Near(Clothing.Comfort - Clothing.Warmth(t), -30), $"ватник держит холод до {Clothing.Comfort - Clothing.Warmth(t):0} °C (−30)");

                // 7. надеть / снять через окно: ПКМ по жилету в рюкзаке, по пластине, снять жилет — пластины в рюкзак
                t.ClearAll();
                t.Pack[0] = new ItemSlot("wear_scrap_signvest", 1);
                t.Pack[1] = new ItemSlot("plate_steel", 1) { Dur = 123 };
                t.Pack[2] = new ItemSlot("wear_rags_shirt", 1);
                QuickCell(t, "pack", 0);
                QuickCell(t, "pack", 1);
                QuickCell(t, "pack", 2);
                Check(t.Worn[(int)WearSlot.Armor]?.Id == "wear_scrap_signvest" && t.Plates[0]?.Id == "plate_steel" && t.Worn[(int)WearSlot.Kofta]?.Id == "wear_rags_shirt", "ПКМ — надеть жилет, вставить пластину, надеть рубаху");
                MoveCell(t, "pack", 3, "wear", 0);   // пусто — ничего
                t.Pack[4] = new ItemSlot("wear_army_ssh68", 1);
                MoveCell(t, "pack", 4, "wear", (int)WearSlot.Feet);
                Check(t.Worn[(int)WearSlot.Feet] == null && t.Pack[4] != null, "шлем в слот обуви не лезет");
                QuickCell(t, "wear", (int)WearSlot.Armor); FixPlates(t);
                bool plateBack = Array.Exists(t.Pack, s => s?.Id == "plate_steel" && Near(s.Dur, 123));
                Check(t.Worn[(int)WearSlot.Armor] == null && t.Plates[0] == null && plateBack, "снял жилет — пластина с прочностью 123 в рюкзаке");

                // 8. сейв пластины и одежды
                var ps = SaveSlot(new ItemSlot("plate_ceramic", 1) { Dur = 77 });
                Check(Near(LoadSlot(ps).Dur, 77), "сейв: прочность пластины сохраняется");
                Check(Tech.Knows(t, "wear_rags_shirt") && Tech.Knows(t, "wear_vatnik_telogreika") && !Tech.Knows(t, "wear_spec_r2") && !Tech.Knows(t, "plate_homemade"),
                      "изучение: тир 0–1 известны, спецовка и кустарная пластина — после узла комплекта");
            }
            finally
            {
                Actors.Remove(t);
            }
            log($"ОДЕЖДА-ТЕСТ итог: {ok} OK, {bad} ПРОВАЛ");
        }
    }
}
