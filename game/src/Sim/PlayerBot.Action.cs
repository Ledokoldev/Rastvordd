using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Движуха (D63, пользователь: «должна быть движуха вечно»): вооружённый бот время от времени идёт искать драку — как роумер в Rust:
    /// в горячие точки режиссёра (где недавно стреляли и умирали), к РТ с людьми, к чужим базам на виду. Встречу решает «бой» (Decide).
    /// </summary>
    public sealed partial class PlayerBot
    {
        Vector2 _pvpTo;
        float _pvpUntil = -1, _pvpNextAt = 180, _pvpArrived = -1;
        string _pvpWhy = "";

        /// <summary>Готов ли к роуму: ствол с патронами (или копьё у задиры), здоров, не несу ценное.</summary>
        bool PvpReady()
        {
            if (_me.Hp < 65 || _surplus > LoadLimit || _raid != null || _rtPhase == RtPhase.Loot || _rj != null) return false;
            if (Ranged() is string g) return AmmoLeft(g) >= (g is "weapon_bow" or "weapon_crossbow" ? 10 : 20);
            return Aggression > 0.6f && Has("weapon_spear");
        }

        float PvpRoamScore()
        {
            if (_w.Time < _pvpUntil) return PvpReady() ? 70 : 0;   // начатый роум держится
            if (_w.Time < _pvpNextAt || !PvpReady()) return 0;
            float want = 0.35f + Aggression * 0.6f + (Archetype == BotArchetype.Roamer ? 0.2f : 0) - (Night ? 0.2f : 0);
            return 50 * want;   // бродяга (≈0,9) — 45–55: перебивает хозяйство (40) и сборы; фермер (≈0,5) — урывками
        }

        bool PvpRoamAct(ref InputCmd cmd)
        {
            if (_w.Time >= _pvpUntil)
            {
                // новая цель: горячая точка → людная РТ → случайная РТ
                _pvpTo = default; _pvpWhy = "";
                if (_w.Director != null)
                    foreach (var (pos, heat) in _w.Director.HotSpots(6))
                    {
                        float d = Vector2.Distance(pos, _me.Pos);
                        if (d > 30 && d < 450) { _pvpTo = pos; _pvpWhy = $"там стреляли (жар {heat:0})"; break; }
                    }
                if (_pvpTo == default)
                {
                    Monument best = null; float bs = float.MinValue;
                    foreach (var m in _w.Monuments)
                    {
                        float d = Vector2.Distance(m.Center, _me.Pos);
                        if (d > 500 || d < 20) continue;
                        int people = 0;
                        foreach (var a in _w.Actors) if (IsPerson(a) && !a.Dead && m.Contains(a.Pos, 20)) people++;   // «на РТ кто-то есть» — как слух и звуки
                        float s = people * 40 - d * 0.1f + (float)_rng.NextDouble() * 20;
                        if (s > bs) { bs = s; best = m; }
                    }
                    if (best != null) { _pvpTo = best.Center; _pvpWhy = $"иду на «{best.Name}» искать людей"; }
                }
                if (_pvpTo == default) { _pvpNextAt = _w.Time + 60; return false; }
                _pvpUntil = _w.Time + 150;
                _pvpArrived = -1;
                Thought($"роум: {_pvpWhy}");
                LStat("роум: вышел");
            }
            var gun = Ranged();
            if (gun != null) Hold(gun, ref cmd); else if (Has("weapon_spear")) Hold("weapon_spear", ref cmd);
            float dd = Vector2.Distance(_me.Pos, _pvpTo);
            Doing = $"роум: {_pvpWhy} ({dd:0} м)";
            if (MoveTo(_pvpTo, 8f, ref cmd) || NoReach(_pvpTo))
            {
                if (_pvpArrived < 0) _pvpArrived = _w.Time;
                // на месте: пройтись вокруг, присесть, послушать
                if (_w.Time - _pvpArrived > 25) { EndPvpRoam("никого — дальше"); return false; }
                _pvpTo += Geo.Rotate(Vector2.UnitX, (float)(_rng.NextDouble() * MathF.Tau)) * 6f;
            }
            return true;
        }

        void EndPvpRoam(string why)
        {
            _pvpUntil = -1;
            _pvpNextAt = _w.Time + 90 + (float)_rng.NextDouble() * (240 - 150 * Aggression);
            Thought($"роум окончен: {why}");
        }
    }
}
