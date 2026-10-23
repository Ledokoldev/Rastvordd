using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Rastvordd.Sim
{
    /// <summary>
    /// ТТК-симуляция (отладка: --simtest=1 --ttktest): каждый ствол против реальных сетов (тиры 0–3 и смеси соседних),
    /// на 10/25/50 м, с шансом головы 5/7/9/11/14/17%. Промахи — по разбросу, блуму и отдаче ствола (как в FireShot),
    /// зоны — шансом (D55), урон — через настоящую броню и пластины (Clothing.Zone, износ пластин). Стрелок в прицеле, стоя.
    /// Итог: CSV в user://ttk/ и сводка в лог.
    /// </summary>
    public sealed partial class World
    {
        // --ttkquick: быстрый прогон для подгонки баланса (300 дуэлей, голова 9%, только дистанция класса)
        static bool TtkQuick => Array.IndexOf(Environment.GetCommandLineArgs(), "--ttkquick") >= 0;
        static int TtkRuns => TtkQuick ? 300 : 1500;
        static float[] TtkDists => TtkQuick ? new float[] { 10, 25, 50 } : new float[] { 10, 25, 50 };
        static float[] TtkHeads => TtkQuick ? new float[] { 9 } : new float[] { 5, 7, 9, 11, 14, 17 };

        static (string name, int tier, string[] dress)[] TtkTargets() => new[]
        {
            ("голый", 0, new string[0]),
            ("тряпьё", 0, Set("rags")),
            ("тряпьё+кожаный шлем и нагрудник", 0, Concat(Set("rags"), new[] { "wear_leather_helmet", "wear_leather_chestplate" })),
            ("кожа и дерево", 1, Set("leather")),
            ("ватник", 1, Set("vatnik")),
            ("кожа + каска-кастрюля + знаки", 1, Concat(Set("leather"), new[] { "wear_scrap_pot", "wear_scrap_signvest" })),
            ("кустарная + кустарная пластина", 2, Concat(Set("scrap"), new[] { "plate_homemade" })),
            ("спецовка", 2, Set("spec")),
            ("кустарная + СШ-68 + 6Б2 + кустарные пл.", 2, Concat(Set("scrap"), new[] { "wear_army_ssh68", "wear_army_6b2", "plate_homemade", "plate_homemade" })),
            ("армейская без пластин", 3, Set("army")),
            ("армейская + 2 стальные пл.", 3, Concat(Set("army"), new[] { "plate_steel", "plate_steel" })),
            ("СОБР + 2 керамики", 3, Concat(Set("sobr"), new[] { "plate_ceramic", "plate_ceramic" })),
            ("ОЗК", 3, Set("ozk")),
        };
        static string[] Set(string k) => Clothing.Sets.Find(s => s.Key == k).Items.ToArray();
        static string[] Concat(string[] a, string[] b) => a.Concat(b).ToArray();

        /// <summary>Целевая дистанция класса: пистолеты/ПП/дробовики — 10 м, автоматы/винтовки — 25, марксманки — 50.</summary>
        static float ClassDist(WeaponDef w) => w.Pellets > 1 || w.Range <= 40 ? 10 : Headshots.Marksman(w) && w.Range >= 90 ? 50 : 25;

        public void TtkSelfTest(Action<string> log)
        {
            var rng = new Random(4242);
            var shooter = new Actor { Id = NextId(), Name = "Тест-стрелок", Team = Team.Player, Peek = 1 };
            var target = new Actor { Id = NextId(), Name = "Тест-цель", Team = Team.Player };
            var csv = new StringBuilder("gun;tier;target;target_tier;dist;head;ttk_med;ttk_p90;shots_med;hits_med;kill_in_mag;ttk_p5;heads_avg;pct_2row;pct_3row\n");
            var guns = Weaponry.GunsByPower.Where(g => Items.Get(g)?.Weapon is WeaponDef d && !d.Melee).ToList();
            var targets = TtkTargets();
            var summary = new List<(string gun, string tgt, int tt, float ttk, float shots)>();

            foreach (var g in guns)
            {
                var wd = Items.Get(g).Weapon;
                foreach (var (tname, ttier, dress) in targets)
                    foreach (float dist in TtkDists)
                        foreach (float head in TtkHeads)
                        {
                            var ttks = new List<float>(); var shotsL = new List<float>(); var hitsL = new List<float>(); int inMag = 0, sumHeads = 0, row2 = 0, row3 = 0;
                            for (int run = 0; run < TtkRuns; run++)
                            {
                                target.ClearAll(); Dress(target, dress);
                                var r = Duel(rng, wd, target, dist, head);
                                ttks.Add(r.t); shotsL.Add(r.shots); hitsL.Add(r.hits); if (r.inMag) inMag++; sumHeads += r.heads; if (r.headRun >= 2) row2++; if (r.headRun >= 3) row3++;
                            }
                            ttks.Sort(); shotsL.Sort(); hitsL.Sort();
                            float med = ttks[ttks.Count / 2], p90 = ttks[(int)(ttks.Count * 0.9f)];
                            csv.Append($"{g};{wd.Tier};{tname};{ttier};{dist};{head};{med:0.00};{p90:0.00};{shotsL[shotsL.Count / 2]};{hitsL[hitsL.Count / 2]};{100f * inMag / TtkRuns:0};{ttks[(int)(ttks.Count * 0.05f)]:0.00};{(float)sumHeads / TtkRuns:0.00};{100f * row2 / TtkRuns:0.0};{100f * row3 / TtkRuns:0.00}\n");
                            if (dist == ClassDist(wd) && head == 9) summary.Add((g, tname, ttier, med, shotsL[shotsL.Count / 2]));
                        }
            }
            string dir = System.IO.Path.Combine(Godot.ProjectSettings.GlobalizePath("user://"), "ttk");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, $"ttk_{DateTime.Now:yyyy-MM-dd_HH-mm}.csv");
            System.IO.File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
            log($"ТТК-ТЕСТ CSV: {path}");
            // сводка: ствол × тир цели (худший сет тира — максимум ТТК), голова 9%, на дистанции класса
            foreach (var g in guns)
            {
                var wd = Items.Get(g).Weapon;
                var cells = Enumerable.Range(0, 4).Select(t =>
                {
                    var row = summary.Where(s => s.gun == g && s.tt == t).ToList();
                    if (row.Count == 0) return "   -   ";
                    var worst = row.OrderByDescending(s => s.ttk).First();
                    return $"{worst.ttk,5:0.00}с/{worst.shots,2:0}";
                });
                log($"ТТК-ТЕСТ {Items.Name(g),-16} т{wd.Tier} {ClassDist(wd),2:0}м | {string.Join(" | ", cells)}");
            }
        }

        /// <summary>Одна дуэль: стрелок без остановки стреляет в цель до смерти (100 HP). Возврат: секунды, выстрелы, попадания.</summary>
        (float t, int shots, int hits, bool inMag, int heads, int headRun) Duel(Random rng, WeaponDef wd, Actor target, float dist, float headPct)
        {
            float hp = 100, time = 0, bloom = 0, recoil = 0;
            int ammo = wd.Magazine, shots = 0, hits = 0, streak = 0, burstLeft = 0;
            bool inMag = true; int heads = 0, run = 0, headRun = 0;
            const float BodyR = 0.35f;
            float interval = wd.Mode == FireMode.Semi ? MathF.Max(wd.FireInterval, 0.12f) : wd.FireInterval;   // полуавтомат — не быстрее кликов
            for (int guard = 0; guard < 400 && hp > 0; guard++)
            {
                if (ammo <= 0) { time += wd.ReloadTime; ammo = wd.Magazine; bloom = 0; recoil = 0; streak = 0; inMag = false; }
                // выстрел: разброс в прицеле (как CurrentSpreadDeg: ×0,65 в прицеле), блум, отдача
                float spread = (wd.BaseSpreadDeg + bloom) * Gunplay.AdsK;
                float rr = ((float)rng.NextDouble() + (float)rng.NextDouble() - 1f) * 0.5f;
                float baseAng = recoil + rr * spread;
                int pellets = Math.Max(1, wd.Pellets);
                for (int p = 0; p < pellets; p++)
                {
                    float ang = baseAng;
                    if (pellets > 1) ang += ((float)rng.NextDouble() + (float)rng.NextDouble() - 1f) * 0.5f * wd.PelletSpreadDeg;
                    float off = MathF.Abs(dist * MathF.Tan(ang * MathF.PI / 180f));
                    if (off > BodyR || dist > wd.Range) continue;
                    hits++;
                    float rz = (float)rng.NextDouble() * 100f;
                    var zone = rz < headPct ? HitZone.Head : rz < headPct + Headshots.LimbsBase + Headshots.LimbsAim ? HitZone.Limbs : HitZone.Torso;
                    if (zone == HitZone.Head) { heads++; run++; headRun = Math.Max(headRun, run); } else run = 0;
                    float raw = wd.Damage * Gunplay.Falloff(wd.EffRange, wd.Range, dist) * Armor_ZoneMul(zone);
                    float prot = Clothing.Zone(target, zone, 0);
                    if (zone == HitZone.Torso && Clothing.ActivePlate(target, 0) is ItemSlot plate)
                    {
                        plate.Dur = Clothing.PlateDur(plate) - raw;
                        if (plate.Dur <= 0) target.Plates[0] = null;
                    }
                    hp -= raw * (1f - prot);
                }
                shots++; ammo--; streak++;
                bloom = MathF.Min(wd.MaxBloomDeg, bloom + wd.BloomPerShotDeg);
                if (wd.RecoilDeg > 0)
                {
                    int sign = wd.RecoilPattern != null ? wd.RecoilPattern[streak % wd.RecoilPattern.Length] : (rng.Next(2) == 0 ? -1 : 1);
                    float step = (wd.RecoilDeg + wd.RecoilGrowDeg * streak) * sign + ((float)rng.NextDouble() - 0.5f) * 0.8f;
                    recoil = Math.Clamp(recoil + step, -wd.RecoilMaxDeg, wd.RecoilMaxDeg);
                }
                if (hp <= 0) break;
                // пауза до следующего выстрела: отсечка — внутри очереди быстро
                float dt = interval;
                if (wd.Burst > 1) { if (burstLeft <= 0) burstLeft = wd.Burst; burstLeft--; dt = burstLeft > 0 ? wd.BurstInterval : wd.FireInterval; }
                time += dt;
                bloom = MathF.Max(0, bloom - wd.BloomRecoverDeg * dt);
                recoil = MathF.Sign(recoil) * MathF.Max(0, MathF.Abs(recoil) - wd.RecoilRecoverDeg * dt);
            }
            return (hp > 0 ? 99 : time, shots, hits, inMag, heads, headRun);
        }

        static float Armor_ZoneMul(HitZone z) => z switch { HitZone.Head => Clothing.HeadMul, HitZone.Torso => Clothing.TorsoMul, _ => Clothing.LimbMul };
    }
}
