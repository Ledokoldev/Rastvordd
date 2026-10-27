using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Самопроверка ветки оружия D46 (отладка: --simtest=N --weapontest): дробь 8 дробин, отсечка АН-94, обвесы меняют
    /// характеристики, токарный станок точит детали, старые id сейва переводятся. Работает на отладочном мире, сейв не трогает.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>Сколько пуль/дробин выпущено за всё время (для самопроверки).</summary>
        public int PelletsFired;

        public void WeaponSelfTest(Action<string> log)
        {
            var a = new Actor { Id = NextId(), Name = "Тест-стрелок", Team = Team.Player };
            Actors.Add(a);
            a.Pos = PlayerSpawns[0];
            int ok = 0, bad = 0;
            void Check(bool cond, string what) { if (cond) ok++; else bad++; log($"ОРУЖИЕ-ТЕСТ {(cond ? "OK " : "ПРОВАЛ")} {what}"); }

            var noInput = new Dictionary<int, InputCmd>();
            InputCmd Cmd(bool fire) { var c = InputCmd.Empty; c.Aim = a.Pos + new Vector2(10, 0); c.Fire = fire; return c; }
            void Equip(string gun)
            {
                a.ClearAll();
                a.Dead = false; a.Hp = a.MaxHp;
                var wd = Items.Get(gun).Weapon;
                a.Belt[0] = Items.NewSlot(gun, wd.Magazine);
                a.Current = 0;
                a.Add(wd.AmmoId, 100);
            }
            List<float> Shoot(int ticks, int holdTicks)
            {
                var times = new List<float>();
                for (int t = 0; t < ticks; t++)
                {
                    int before = PelletsFired;
                    var input = new Dictionary<int, InputCmd> { [a.Id] = Cmd(t < holdTicks) };
                    a.Hp = a.MaxHp; a.Food = a.Water = 100;
                    Step(input);
                    for (int k = before; k < PelletsFired; k++) times.Add(t * Dt);
                }
                return times;
            }

            // 1. дробовик: 8 дробин за выстрел
            Equip("weapon_sawnoff");
            var sh = Shoot(3, 1);
            Check(sh.Count == 8, $"Обрез: дробин за выстрел {sh.Count} (ждём 8)");
            Equip("weapon_mp133");
            sh = Shoot(3, 1);
            Check(sh.Count == 8, $"МР-133: дробин {sh.Count}, урон дробины {Items.Get("weapon_mp133").Weapon.Damage} (8×14)");

            // 2. АН-94: отсечка по 2 (0,03 с), между очередями 0,25 с
            Equip("weapon_an94");
            var bt = Shoot(18, 18);   // 0,6 с с зажатым курком
            log($"ОРУЖИЕ-ТЕСТ АН-94 выстрелы, с: {string.Join(", ", bt.ConvertAll(x => x.ToString("0.000")))}");
            bool pairs = bt.Count >= 4 && bt[1] - bt[0] < 0.05f && bt[2] - bt[1] >= 0.24f && bt[3] - bt[2] < 0.05f;
            Check(pairs, $"АН-94: очереди по 2 ({bt.Count} выстрелов за 0,6 с)");
            Equip("weapon_an94");
            bt = Shoot(18, 1);       // один клик — одна очередь из 2
            Check(bt.Count == 2, $"АН-94: один клик → {bt.Count} выстрела (ждём 2)");

            // 3. обвесы
            Equip("weapon_akm");
            var w = a.Weapon;
            int mag0 = w.MagSize; float noise0 = w.Noise, rec0 = w.RecoilMul, rel0 = w.Reload;
            float spread0 = CurrentSpreadDeg(a);
            Attach(a, w, "att_mag_ext", out _);
            Attach(a, w, "att_suppressor", out _);
            Attach(a, w, "att_grip", out _);
            Check(MathF.Abs(w.RecoilMul - 0.8f) < 0.001f, $"отдача ×{rec0:0.00} → ×{w.RecoilMul:0.00} (рукоять −20%)");
            Attach(a, w, "att_laser", out string laserMsg);
            Attach(a, w, "att_scope4x", out _);
            Check(w.MagSize == 45 && mag0 == 30, $"АКМ магазин {mag0} → {w.MagSize} (увеличенный +50%)");
            Check(MathF.Abs(w.Noise - noise0 * 0.5f) < 0.01f, $"шум {noise0:0} → {w.Noise:0} (глушитель −50%)");
            log($"ОРУЖИЕ-ТЕСТ ЛЦУ: {laserMsg} (цевьё занято рукоятью — ЛЦУ её сменил, рукоять в рюкзаке: {a.Count("att_grip")})");
            float spread1 = CurrentSpreadDeg(a);
            Check(spread1 < spread0 * 0.8f, $"разброс от бедра {spread0:0.00}° → {spread1:0.00}° (ЛЦУ −25%)");
            Check(MathF.Abs(w.SightZoom - 2.2f) < 0.01f, $"ПСО: SightZoom = {w.SightZoom}");
            Attach(a, w, "att_mag_twin", out _);
            Check(w.MagSize == 30 && w.Reload < rel0 * 0.7f, $"спарка вместо увеличенного: магазин {w.MagSize}, перезарядка {rel0:0.0} → {w.Reload:0.00} с");
            Equip("weapon_nagant");
            bool refused = !Attach(a, a.Weapon, "att_stock_tac", out string why);
            Check(refused, $"приклад на Наган не встаёт: {why}");
            // кустарный глушитель живёт 40 выстрелов
            Equip("weapon_homesmg");
            Attach(a, a.Weapon, "att_suppressor_can", out _);
            a.Add("ammo_9mm", 200);
            var hs = Shoot(300, 300);
            log($"ОРУЖИЕ-ТЕСТ Самоделка: выстрелов {hs.Count}, в магазине {a.Weapon.Ammo}, перезарядка {a.Weapon.ReloadLeft:0.00}, патронов {a.Count("ammo_9mm")}");
            Check(a.Weapon.Att[(int)AttSlot.Muzzle] == null, $"глушитель-банка развалился после 40 выстрелов (износ ствола {a.Weapon.Durability:0}/{a.Weapon.Def.MaxDurability:0})");

            // 4. износ и ремонт
            a.Weapon.Durability = 0;
            int pf = PelletsFired;
            Shoot(5, 5);
            Check(PelletsFired == pf, "сломанный ствол не стреляет");
            var bench = new Deployable { Id = NextId(), Kind = DeployKind.Workbench, Pos = a.Pos + new Vector2(0, 1.2f), Level = 1, OwnerId = a.Id };
            AddDeployable(bench);
            a.Add("comp_oil", 1); a.Add("comp_spring", 1);
            bool fixedOk = Repair(a, a.Weapon);
            Check(fixedOk && a.Weapon.Durability >= a.Weapon.Def.MaxDurability * 0.49f, $"ремонт маслом + пружиной: {a.Weapon.Durability:0}/{a.Weapon.Def.MaxDurability:0}");

            // 5. токарный станок
            var lathe = new Deployable { Id = NextId(), Kind = DeployKind.Lathe, Pos = a.Pos + new Vector2(2, 0), OwnerId = a.Id, Radius = 0.7f };
            lathe.Storage = new LootBag { Id = NextId(), Pos = lathe.Pos };
            AddDeployable(lathe);
            lathe.Storage.Items.Add(new ItemSlot("metal_fragments", 200));
            lathe.Storage.Items.Add(new ItemSlot("scrap", 50));
            lathe.On = true;
            for (int t = 0; t < (int)(32 / Dt); t++) Step(noInput);
            string Has(string id) { int n = 0; foreach (var s in lathe.Storage.Items) if (s.Id == id) n += s.Count; return $"{Items.Name(id)} {n}"; }
            int Cnt(string id) { int n = 0; foreach (var s in lathe.Storage.Items) if (s.Id == id) n += s.Count; return n; }
            Check(Cnt("comp_pipe") == 2 && Cnt("scrap") == 0, $"станок: металл 200 + лом 50 → {Has("comp_pipe")}, {Has("scrap")}");
            lathe.Storage.Items.Clear();
            lathe.Storage.Items.Add(new ItemSlot("comp_spring", 3));
            lathe.Storage.Items.Add(new ItemSlot("comp_gears", 2));
            lathe.Storage.Items.Add(new ItemSlot("metal_fragments", 80));
            lathe.Storage.Items.Add(new ItemSlot("scrap", 80));
            lathe.On = true;
            for (int t = 0; t < (int)(42 / Dt); t++) Step(noInput);
            Check(Cnt("comp_bolt_carrier") == 1, $"станок: пружины 3 + шестерни 2 + металл 80 → {Has("comp_bolt_carrier")}");
            lathe.Storage.Items.Clear();
            lathe.Storage.Items.Add(new ItemSlot("comp_barrel_rifled", 2));
            lathe.Storage.Items.Add(new ItemSlot("comp_spring", 2));
            lathe.Storage.Items.Add(new ItemSlot("metal_fragments", 200));
            Check(LatheJob(lathe)?.Output != "comp_barrel_chrome", $"без верстака 3 хромированный ствол не точится (выбрано: {Items.Name(LatheJob(lathe)?.Output ?? "-")})");
            bench.Level = 3;
            Check(LatheJob(lathe)?.Output == "comp_barrel_chrome", "рядом верстак 3 — хромированный ствол точится");

            // 6. старые id сейва
            var old = LoadSlot(new WorldSave.SlotS { Id = "weapon_ak", Count = 1, Ammo = 30 });
            var oldAmmo = LoadSlot(new WorldSave.SlotS { Id = "ammo_pistol", Count = 40 });
            var oldPipe = LoadSlot(new WorldSave.SlotS { Id = "weapon_pipe", Count = 1, Ammo = 10 });
            Check(old?.Id == "weapon_akm" && oldAmmo?.Id == "ammo_9mm" && oldAmmo.Count == 40 && oldPipe?.Id == "weapon_nagant" && oldPipe.State.Ammo == 7,
                  $"сейв: weapon_ak → {old?.Id}, ammo_pistol → {oldAmmo?.Id}, weapon_pipe → {oldPipe?.Id} (патронов {oldPipe?.State?.Ammo})");
            var saved = SaveSlot(Items.NewSlot("weapon_akm", 30));
            saved.Att = new List<string> { "att_reddot", null, null, "att_mag_ext", null };
            var back = LoadSlot(saved);
            Check(back.State.SightZoom == 1f && back.State.MagSize == 45, "сейв: обвесы и износ сохраняются");

            // 7. рецепты и изучение
            int path = 0;
            foreach (var id in new[] { "gunpowder", "ammo_9mm", "weapon_nagant", "weapon_homesmg", "weapon_grach", "ammo_762", "weapon_akm" }) path += Tech.Get(id).Scrap;
            log($"ОРУЖИЕ-ТЕСТ путь до АКМ через Грач: {path} лома");
            Check(Recipes.All.Find(r => r.Output is "weapon_sawnoff" or "weapon_mp133" or "weapon_saiga12" or "weapon_pipeshotgun" or "weapon_mosin" or "weapon_aps") == null, "дробовики и элита не крафтятся");

            RemoveDeployable(lathe);
            RemoveDeployable(bench);
            Actors.Remove(a);
            log($"ОРУЖИЕ-ТЕСТ итог: {ok} OK, {bad} провалов");
        }
    }
}
