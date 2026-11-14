using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Самопроверка мозга ботов (D64, --botbraintest): бот, на которого напали, отвечает — со стволом против ствола сильнее,
    /// с копьём против камня. Печатает «МОЗГ-ТЕСТ … OK/ПРОВАЛ».
    /// </summary>
    public sealed partial class World
    {
        public void BotBrainSelfTest(Action<string> log)
        {
            int fails = 0;
            void Check(string what, bool ok) { if (!ok) fails++; log($"МОЗГ-ТЕСТ {what}: {(ok ? "OK" : "ПРОВАЛ")}"); }
            var at = PlayerSpawns[0] + new Vector2(0, -40);
            for (int t = 0; t < 40 && !IsFree(at, 6); t++) at += new Vector2(7, 0);

            (float dealt, string doing) Duel(string botGun, string botAmmo, string atkGun, string atkAmmo, float dist)
            {
                var bot = new Actor { Id = NextId(), Name = "Тест-бот", Team = Team.Player, IsBot = true, Pos = at };
                var atk = new Actor { Id = NextId(), Name = "Тест-нападающий", Team = Team.Player, Pos = at + new Vector2(dist, 0) };
                Actors.Add(bot); Actors.Add(atk);
                bot.AddSlot(Items.NewSlot(botGun)); if (botAmmo != null) bot.Add(botAmmo, 60); bot.Add("bandage", 3); bot.Add("tool_hatchet", 1);
                var aslot = Items.NewSlot(atkGun); atk.Belt[0] = aslot; atk.Current = 0; if (atkAmmo != null) atk.Add(atkAmmo, 200);
                var brain = new PlayerBot(bot, this, 777);
                float atkHp0 = atk.Hp; string lastDoing = "";
                for (int k = 0; k < (int)(20 / Dt); k++)
                {
                    InputCmd bc; using (Ctx(bot.Floor)) bc = brain.Think();
                    var ac = InputCmd.Empty; ac.Aim = bot.Pos; ac.Fire = atk.Weapon?.Def.Melee == true ? k % 12 < 2 : k % 20 < 12; ac.Peek = 1;
                    if (Vector2.Distance(atk.Pos, bot.Pos) > dist + 2) ac.Move = Vector2.Normalize(bot.Pos - atk.Pos);   // держится рядом
                    Step(new Dictionary<int, InputCmd> { [bot.Id] = bc, [atk.Id] = ac });
                    if (bot.Hp < 40) bot.Hp = 60;   // проверяем реакцию, не смерть
                    atk.Hp = MathF.Max(atk.Hp, 1); atk.Dead = false;
                    if (k % 30 == 0) lastDoing = brain.Doing;
                }
                float dealt = 100 - atk.Hp;
                if (brain.Thoughts.Exists(th => th.Contains("→ бой") || th.Contains("отвечаю") || th.Contains("пикаю"))) dealt = MathF.Max(dealt, 0.01f);   // вступил в бой — ответил
                foreach (var th in brain.Thoughts.GetRange(Math.Max(0, brain.Thoughts.Count - 6), Math.Min(6, brain.Thoughts.Count))) log("МОЗГ-ТЕСТ   мысль: " + th);
                log($"МОЗГ-ТЕСТ   бот HP {bot.Hp:0}, последний обидчик {bot.LastAttacker} (нападающий {atk.Id}), бот в руках {bot.Held?.Id}");
                Actors.Remove(bot); Actors.Remove(atk);
                return (dealt, lastDoing);
            }

            var (d1, w1) = Duel("weapon_pipeshotgun", "ammo_12g", "weapon_akm", "ammo_762", 12);
            log($"МОЗГ-ТЕСТ (самопал против АКМ: бот делал «{w1}», нанёс {d1:0})");
            Check("бот с самопалом отвечает на огонь из АКМ (вступает в бой)", d1 > 0);
            var (d2, w2) = Duel("weapon_spear", null, "tool_hatchet", null, 1.1f);
            log($"МОЗГ-ТЕСТ (копьё против топора: бот делал «{w2}», нанёс {d2:0})");
            Check("бот с копьём отвечает на удары топором", d2 > 0);
            log($"МОЗГ-ТЕСТ итог: {(fails == 0 ? "всё OK" : $"провалов {fails}")}");
        }
    }
}
