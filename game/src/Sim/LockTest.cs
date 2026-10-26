using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Самопроверка (отладка: --simtest=N --locktest): ближний удар и пуля не проходят сквозь деревянную стену и закрытую дверь;
    /// замки: ключевой — владельцу и держателю ключа, кодовый — владелец задаёт код, верный код даёт доступ, смена кода его сбрасывает.
    /// Печатает «ЗАМОК-ТЕСТ … OK/ПРОВАЛ». Сейв не трогает.
    /// </summary>
    public sealed partial class World
    {
        public void LockSelfTest(Action<string> log)
        {
            int fails = 0;
            void Check(string what, bool ok) { if (!ok) fails++; log($"ЗАМОК-ТЕСТ {what}: {(ok ? "OK" : "ПРОВАЛ")}"); }

            var (ci, cj) = Building.CellOf(PlayerSpawns[0]);
            var (ea, eb) = Building.EdgeEnds(ci, cj, 1);   // вертикальное ребро x = ci·Cell
            float x = ea.X, y = (ea.Y + eb.Y) * 0.5f;
            Actor Mk(string name) { var t = new Actor { Id = NextId(), Name = name, Team = Team.Player }; Actors.Add(t); return t; }
            var atk = Mk("Тест-рубака");
            var vic = Mk("Тест-хозяин");
            Structure Edge(StructKind k)
            {
                var s = new Structure { Id = NextId(), Kind = k, Dir = 1, Tier = 1, OwnerId = vic.Id, A = ea, B = eb };
                s.Hp = s.MaxHp;
                Structures.Add(s);
                if (k == StructKind.Door) _doors[s.EdgeKey] = s; else _edges[s.EdgeKey] = s;
                RebuildBoxes(s);
                return s;
            }
            void Drop(Structure s) { RemoveStructure(s, true); _doors.Remove(s.EdgeKey); _edges.Remove(s.EdgeKey); }

            // ---- ближний бой: топор через стену ----
            float MeleeDealt()
            {
                atk.Pos = new Vector2(x - 0.6f, y); vic.Pos = new Vector2(x + 0.6f, y);
                atk.Facing = 0; vic.Hp = vic.MaxHp; vic.Dead = false;
                atk.Belt[0] = Items.NewSlot("tool_hatchet", 0); atk.Current = 0;
                Swing(atk, atk.Weapon);
                return vic.MaxHp - vic.Hp;
            }
            Check("топор без стены попадает (контроль)", MeleeDealt() > 0);
            var wall = Edge(StructKind.Wall);
            Check("топор сквозь деревянную стену — нет", MeleeDealt() == 0);
            Drop(wall);
            var door = Edge(StructKind.Door);
            Check("топор сквозь закрытую дверь — нет", MeleeDealt() == 0);

            // ---- пуля через стену ----
            float ShotDealt()
            {
                atk.Pos = new Vector2(x - 4f, y); vic.Pos = new Vector2(x + 1f, y);
                vic.Hp = vic.MaxHp; vic.Dead = false;
                var wd = Items.Get("weapon_pps43").Weapon;
                atk.Belt[0] = Items.NewSlot("weapon_pps43", wd.Magazine); atk.Current = 0; atk.Add(wd.AmmoId, 200);
                float dealt = 0;
                for (int k = 0; k < (int)(1.5f / Dt); k++)
                {
                    var c = InputCmd.Empty; c.Aim = vic.Pos; c.Fire = true;
                    var vc = InputCmd.Empty; vc.Aim = atk.Pos;
                    float hp0 = vic.Hp;
                    Step(new Dictionary<int, InputCmd> { [atk.Id] = c, [vic.Id] = vc });
                    atk.Pos = new Vector2(x - 4f, y); vic.Pos = new Vector2(x + 1f, y);
                    dealt += MathF.Max(0, hp0 - vic.Hp);
                    vic.Hp = vic.MaxHp; vic.Dead = false;
                }
                return dealt;
            }
            Check("пуля сквозь закрытую дверь — нет", ShotDealt() == 0);
            Drop(door);
            wall = Edge(StructKind.Wall);
            Check("пуля сквозь деревянную стену — нет", ShotDealt() == 0);
            Drop(wall);
            Check("пуля без стены попадает (контроль)", ShotDealt() > 0);

            // ---- замки ----
            door = Edge(StructKind.Door);
            var own = vic; var other = atk;
            own.Pos = new Vector2(x + 0.8f, y); other.Pos = new Vector2(x - 0.8f, y);
            door.Locked = true; door.KeyLock = true; door.LockOwner = own.Id;
            Check("ключевой: владелец без ключа открывает", CanOpenDoor(own, door));
            Check("ключевой: чужой без ключа — нет", !CanOpenDoor(other, door));
            other.Add("key_" + door.Id, 1);
            Check("ключевой: чужой с ключом — да", CanOpenDoor(other, door));
            other.Take("key_" + door.Id, 1);

            door.KeyLock = false; door.Code = -1; door.LockAuth.Clear(); door.LockAuth.Add(own.Id);
            int ev = Events.Count;
            ToggleDoor(own, door);
            Check("кодовый: код не задан — владельцу окно «задать», дверь закрыта", !door.Open && Events.FindIndex(ev, e => e.Kind == SimEventKind.Locked && e.Value == 1) >= 0);
            EnterCode(own, door.Id, 1234);
            Check("кодовый: владелец задал 1234", door.Code == 1234 && door.LockAuth.Contains(own.Id));
            other.Hp = other.MaxHp;
            EnterCode(other, door.Id, 1111);
            Check("кодовый: неверный код — нет доступа, бьёт током", !door.LockAuth.Contains(other.Id) && other.Hp < other.MaxHp && !door.Open);
            EnterCode(other, door.Id, 1234);
            Check("кодовый: верный код — доступ и дверь открыта", door.LockAuth.Contains(other.Id) && door.Open);
            ToggleDoor(other, door);
            ToggleDoor(other, door);
            Check("кодовый: допущенный открывает без кода", door.Open);
            EnterCode(own, door.Id, 4321);
            Check("кодовый: смена кода сбрасывает допуск", door.Code == 4321 && !door.LockAuth.Contains(other.Id) && door.LockAuth.Contains(own.Id));

            Drop(door);
            Actors.Remove(atk); Actors.Remove(vic);
            log($"ЗАМОК-ТЕСТ итог: {(fails == 0 ? "всё OK" : $"провалов {fails}")}");
        }
    }
}
