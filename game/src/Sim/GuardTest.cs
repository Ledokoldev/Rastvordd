using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Замер меткости охраны РТ (отладка: --simtest=N --guardtest): охранник с оружием стреляет по манекену на 8/15/25 м,
    /// стоящему и бегущему боком. Печатает долю попаданий и время до смерти голого игрока (100 HP). Сейв не трогает.
    /// </summary>
    public sealed partial class World
    {
        public void GuardSelfTest(Action<string> log)
        {
            foreach (var gun in new[] { "weapon_pps43", "weapon_akm" })
                foreach (float dist in new[] { 8f, 15f, 25f })
                    foreach (bool run in new[] { false, true })
                    {
                        var g = new Actor { Id = NextId(), Name = "Тест-охранник", Team = Team.Npc };
                        var t = new Actor { Id = NextId(), Name = "Тест-манекен", Team = Team.Player };
                        Actors.Add(g); Actors.Add(t);
                        var at = PlayerSpawns[0];
                        var toC = Vector2.Normalize(-at);   // к центру острова — суша
                        NpcHomes[g.Id] = at;
                        g.Pos = at; t.Pos = at + toC * dist; g.Facing = MathF.Atan2(toC.Y, toC.X);
                        var wd = Items.Get(gun).Weapon;
                        g.Belt[0] = Items.NewSlot(gun, wd.Magazine); g.Current = 0; g.Add(wd.AmmoId, 500);
                        var brain = new NpcBrain(g, this, 777 + (int)dist + (run ? 1 : 0));
                        int shots = 0, hits = 0; float dmg = 0, deathAt = -1;
                        int ticks = (int)(20f / Dt);
                        for (int k = 0; k < ticks; k++)
                        {
                            var cmd = brain.Think();
                            // манекен бегает боком туда-сюда по 3 м
                            var tc = InputCmd.Empty;
                            var side = new Vector2(-toC.Y, toC.X);
                            if (run) tc.Move = side * ((k / (int)(1.2f / Dt)) % 2 == 0 ? 1 : -1);
                            tc.Aim = g.Pos;
                            int before = PelletsFired;
                            float hp0 = t.Hp;
                            Step(new Dictionary<int, InputCmd> { [g.Id] = cmd, [t.Id] = tc });
                            g.Pos = at; var off = t.Pos - at; float lat = Vector2.Dot(off, side); t.Pos = at + toC * dist + side * Math.Clamp(lat, -3f, 3f);   // держим дистанцию, бег — вбок
                            shots += PelletsFired - before;
                            if (t.Hp < hp0) { hits++; dmg += hp0 - t.Hp; if (deathAt < 0 && dmg >= 100) deathAt = k * Dt; }
                            t.Hp = t.MaxHp; t.Dead = false; t.Food = t.Water = 100;
                            g.Hp = g.MaxHp;
                        }
                        log($"ОХРАНА-ТЕСТ {Items.Name(gun)} {dist:0} м {(run ? "бег" : "стоит")}: выстрелов {shots}, попаданий {hits} ({(shots > 0 ? 100f * hits / shots : 0):0}%), урон за 20 с {dmg:0}, голый умер бы через {(deathAt < 0 ? ">20" : deathAt.ToString("0.0"))} с");
                        Actors.Remove(g); Actors.Remove(t); NpcHomes.Remove(g.Id);
                    }
        }
    }
}
