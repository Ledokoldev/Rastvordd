using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Боты по этажам (D62): цель на другом этаже — к ближайшей лестнице, пролёт, выход. Лестница поднимает бота, только пока он
    /// сам идёт по ней (Actor.StairsOk) — на чужую лестницу случайно не зайдёт. Наверху поиск пути — только по полу (World.WalkBlocked),
    /// мозг думает в контексте своего этажа (LocalServer).
    /// </summary>
    public sealed partial class PlayerBot
    {
        /// <summary>На каком этаже цель текущего дела (каждый тик — 0; поход на РТ ставит этаж ящика или механизма).</summary>
        int _wantFloor;
        Structure _stair;
        bool _stairMove;   // MoveTo вызван переходом по лестнице — не перехватывать
        float _stairSince;

        /// <summary>Этаж цели: ящик, сумка, «держи E», установленное — их этаж, остальное — земля.</summary>
        static int FloorOf(object o) => o switch { LootBag b => b.Floor, Stash s => s.Floor, Deployable d => d.Floor, Tier3Rt => 1, _ => 0 };
        bool _dugaMine;   // прогрев пульта «Дуги» запустил я — жду у крейта

        bool InStairCell(Structure s) => s != null && Building.CellOf(_me.Pos) == (s.I, s.J);

        /// <summary>Шаг к нужному этажу. true — уже на нём и не в пролёте: можно идти к цели как обычно.</summary>
        bool StairsTo(int floor, Vector2 near, ref InputCmd cmd)
        {
            bool inCell = InStairCell(_stair);
            if (_me.Floor == floor && !inCell) { _me.StairsOk = false; _stair = null; return true; }
            // в пролёте этаж переключается на середине марша — направление держим по самой лестнице, иначе качели вверх-вниз
            bool up = inCell && _stair != null ? floor >= _stair.Floor : floor > _me.Floor;
            if (_stair == null || !_w.Structures.Contains(_stair) || (!inCell && (up ? _stair.Floor - 1 : _stair.Floor) != _me.Floor))
            {
                _stair = null;
                float best = float.MaxValue;
                foreach (var s in _w.Structures)
                {
                    if (s.Kind != StructKind.Stairs || (up ? s.Floor - 1 : s.Floor) != _me.Floor) continue;
                    var c = Building.CellCenter(s.I, s.J);
                    float cost = Vector2.Distance(_me.Pos, c) + Vector2.Distance(c, near) * 0.5f;
                    if (cost < best && Vector2.Distance(_me.Pos, c) < 120) { best = cost; _stair = s; }
                }
                _stairSince = _w.Time;
                if (_stair == null)
                {
                    if (!NoReach(near)) { MarkNoReach(near, 120); Say("нет-лестницы", "на тот этаж лестницы не вижу — беру другую цель", 20); }
                    return false;
                }
            }
            var center = Building.CellCenter(_stair.I, _stair.J);
            var u = World.StairDir(_stair.Dir);
            var go = up ? u : -u;
            Doing = up ? "поднимается по лестнице" : "спускается по лестнице";
            if (_w.Time - _stairSince > 25) { MarkNoReach(near, 90); _stair = null; _me.StairsOk = false; return false; }   // не вышло за 25 с — бросить
            if (inCell || (_me.StairsOk && Vector2.Distance(_me.Pos, center) < 2.3f))
            {
                // в пролёте: прямо вдоль марша, по середине ступеней
                _me.StairsOk = true;
                var lat = center - _me.Pos;
                lat -= go * Vector2.Dot(lat, go);
                cmd.Move = Vector2.Normalize(go + lat * 1.5f);
                cmd.Aim = _me.Pos + go * 4;
                return false;
            }
            // к входу в пролёт со своей стороны
            var entry = center - go * 1.5f;
            _stairMove = true;
            bool at = MoveTo(entry, 0.35f, ref cmd);
            _stairMove = false;
            if (at) { _me.StairsOk = true; cmd.Move = go; cmd.Aim = _me.Pos + go * 4; }
            return false;
        }

        /// <summary>Отладка (самопроверка D62): сразу начать лутать РТ m, как будто дошёл до неё.</summary>
        public void DebugRunRt(Monument m)
        {
            _rtsAt = -999;
            _rt = Rts().Find(r => r.M == m);
            if (_rt == null) return;
            _rtPhase = RtPhase.Loot;
            _rtStartAt = _rtLootAt = _w.Time;
            _rtValue0 = CarriedValue();
            _rtHad.Clear();
            _rtSkip.Clear();
        }
        public string DebugDoing => Doing;
        public string DebugTrace => $"цель {(_rtT is { } t ? $"{t.Item1} {t.Item2?.GetType().Name} эт.{FloorOf(t.Item2) + 1} @{t.Item3.X:0.0};{t.Item3.Y:0.0}" : "-")} хочу эт.{_wantFloor + 1} лестница {(_stair != null ? $"{_stair.I},{_stair.J}" : "-")} StairsOk {_me.StairsOk}";
        public bool DebugOnRt => _rtPhase == RtPhase.Loot;

        /// <summary>Перехват MoveTo: цель на другом этаже — сначала лестница. true — ход уже задан.</summary>
        bool FloorDetour(Vector2 p, ref InputCmd cmd)
        {
            if (_stairMove) return false;
            if (_me.Floor == _wantFloor && !InStairCell(_stair)) { if (_me.StairsOk) { _me.StairsOk = false; _stair = null; } return false; }
            StairsTo(_wantFloor, p, ref cmd);
            return true;
        }
    }
}
