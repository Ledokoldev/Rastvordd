using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>Таблица шансов в голову (отладка: --simtest=1 --headtest): типовые стволы, обвесы, позы, дистанции.</summary>
    public sealed partial class World
    {
        public void HeadshotSelfTest(Action<string> log)
        {
            var a = new Actor { Id = NextId(), Name = "Тест-стрелок", Team = Team.Player };
            void Row(string gun, string[] atts, bool aim, bool crouch, float dist)
            {
                var slot = Items.NewSlot(gun);
                foreach (var at in atts) Attach(a, slot.State, at, out _);
                a.Peek = aim ? 1 : 0; a.Crouching = crouch; a.Vel = Vector2.Zero;
                log($"ХЕДШОТ {Items.Name(gun),-16} {(atts.Length == 0 ? "без обвесов" : string.Join("+", atts)),-48} {(aim ? "прицел" : "бедро "),-6} {(crouch ? "присед" : "стоя  ")} {dist,4:0} м → голова {Headshots.HeadChance(a, slot.State, dist):0.0}%, конечности {Headshots.LimbChance(a):0}%");
            }
            var none = new string[0];
            Row("weapon_nagant", none, false, false, 15); Row("weapon_nagant", none, true, true, 15);
            Row("weapon_pps43", none, false, false, 15); Row("weapon_pps43", new[] { "att_reddot" }, true, true, 20);
            Row("weapon_akm", none, false, false, 20); Row("weapon_akm", new[] { "att_reddot", "att_grip", "att_stock_tac", "att_compensator_army" }, true, true, 25);
            Row("weapon_akm", new[] { "att_scope4x" }, true, false, 5);
            Row("weapon_svd", none, false, false, 40); Row("weapon_svd", new[] { "att_scope4x" }, true, false, 60);
            Row("weapon_svd", new[] { "att_scope4x", "att_compensator_army" }, true, true, 70);
            Row("weapon_mosin", new[] { "att_scope4x" }, true, true, 80); Row("weapon_mosin", new[] { "att_scope4x" }, true, false, 6);
        }
    }
}
