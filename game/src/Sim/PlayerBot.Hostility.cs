using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Встречи с людьми (docs/design/bot-brain.md, этап 5; D42): любой другой игрок или бот — возможный враг, мира «по умолчанию» нет.
    /// Решение на встречу — по силам и характеру: сильнее — нападаю, слабее — ухожу, несу ценное — обхожу, но отвечаю;
    /// на одной РТ двое не лутают рядом — первый отбивает, новичок дерётся, караулит выход или уходит.
    /// Против метания (пользователь: «трое болванов бегают друг от друга по пляжу»): решение держится 15–20 с,
    /// у бегства есть точка и таймер, взаимный отход — «разошлись» на 2 мин, брошенную погоню не начинаем снова 2 мин.
    /// Обиды помнятся: кто бил или убил — того бьём при встрече и ищем там, где видели последний раз.
    /// Честно: только то, что вижу (конус, дальность, стены) и слышу (шаги вплотную, попадания по мне).
    /// </summary>
    public sealed partial class PlayerBot
    {
        enum Stance { None, Fight, Flee, Watch, Camp, Parted }

        sealed class Enc
        {
            public Actor A;
            public Stance S;
            public float Until, FirstSeen = -999, LastSeen = -999;
            public Vector2 SeenPos;
            public string Why = "";
            public bool Rt;            // спор за РТ
        }

        sealed class Grudge
        {
            public float Damage, At, LastSeen = -999, Kit;
            public bool KilledMe;
            public Vector2 LastPos;
        }

        readonly Dictionary<int, Enc> _enc = new();
        readonly Dictionary<int, Grudge> _grudge = new();
        Actor _encFight, _encAvoid;
        float _encScanAt = -99;

        float LoadLimit => 120 + 280 * Aggression;
        bool IsPerson(Actor a) => a != null && a != _me && !a.Spectator && a.Team != Team.Npc;

        // ---------- восприятие людей ----------

        /// <summary>Каждый тик — попадания и смерти (обиды); раз в 0,3 с — кого вижу и что о нём решил.</summary>
        void PerceivePeople()
        {
            foreach (var e in _w.Events)
            {
                if (e.Kind == SimEventKind.HitActor && e.ActorId == _me.Id && _w.Find(e.OtherId) is Actor src && IsPerson(src))
                {
                    if (!_grudge.TryGetValue(src.Id, out var g)) { _grudge[src.Id] = g = new Grudge(); Thought($"{src.Name} напал на меня — запомню"); }
                    g.Damage += e.Value; g.At = _w.Time; g.LastPos = src.Pos; g.LastSeen = _w.Time; g.Kit = KitPower(src);
                }
                if (e.Kind != SimEventKind.Death) continue;
                if (e.ActorId == _me.Id && _w.Find(e.OtherId) is Actor killer && IsPerson(killer))
                {
                    if (!_grudge.TryGetValue(killer.Id, out var g)) _grudge[killer.Id] = g = new Grudge();
                    g.KilledMe = true; g.At = _w.Time; g.LastPos = killer.Pos; g.LastSeen = _w.Time; g.Kit = KitPower(killer);
                    Thought($"меня убил {killer.Name} ({Items.Name(killer.Held?.Id ?? "tool_rock")}) — {(g.Kit <= MyPower + 0.1f ? "найду и отомщу" : "сильнее меня — буду обходить")}");
                }
                if (e.OtherId == _me.Id && _grudge.Remove(e.ActorId) && _w.Find(e.ActorId) is Actor dead) { Thought($"отомстил {dead.Name}"); Stat("месть: отомстил"); }
            }
            // забыть старые обиды (20 мин)
            if (_grudge.Count > 0)
                foreach (var id in new List<int>(_grudge.Keys)) if (_w.Time - _grudge[id].At > 1200) _grudge.Remove(id);

            if (_w.Time - _encScanAt < 0.3f) return;
            _encScanAt = _w.Time;
            float range = _w.ViewRange(_me) + 10;
            _encFight = null; _encAvoid = null;
            float bf = float.MaxValue, ba = float.MaxValue;
            foreach (var a in _w.Actors)
            {
                if (!IsPerson(a)) continue;
                if (a.Dead) { _enc.Remove(a.Id); continue; }
                float d = Vector2.Distance(a.Pos, _me.Pos);
                bool hitMe = _me.LastAttacker == a.Id && _w.Time - _me.LastDamagedTime < 2f;
                bool sees = hitMe || d < 6f || (d < range && _w.CanSeeActor(_me, a, range, 260f));   // вплотную слышно шаги
                _enc.TryGetValue(a.Id, out var en);
                if (sees)
                {
                    if (en == null) _enc[a.Id] = en = new Enc { A = a, FirstSeen = _w.Time };
                    if (_w.Time - en.LastSeen > 60) en.FirstSeen = _w.Time;   // новая встреча
                    en.LastSeen = _w.Time; en.SeenPos = a.Pos;
                    if (_grudge.TryGetValue(a.Id, out var g)) { g.LastSeen = _w.Time; g.LastPos = a.Pos; g.Kit = KitPower(a); }
                    Judge(en, d, hitMe);
                }
                if (en == null) continue;
                bool recent = _w.Time - en.LastSeen < 8f;   // спрятался — не значит, что ушёл
                float dk = Vector2.Distance(en.SeenPos, _me.Pos);
                if (en.S == Stance.Fight && recent && dk < 60 && dk < bf) { bf = dk; _encFight = a; }
                if (en.S == Stance.Flee && recent && dk < 25 && dk < ba) { ba = dk; _encAvoid = a; }
            }
            if (_enc.Count > 60) foreach (var id in new List<int>(_enc.Keys)) if (_w.Time - _enc[id].LastSeen > 300) _enc.Remove(id);
        }

        /// <summary>
        /// Решение по встрече: принимается один раз и держится 15–20 с. Раньше срока меняется только при явном поводе:
        /// он напал (отвечаю, если могу), я при смерти или без патронов (ухожу).
        /// </summary>
        void Judge(Enc en, float d, bool hitMe)
        {
            var a = en.A;
            bool expired = _w.Time >= en.Until || en.S == Stance.None;
            if (!expired)
            {
                if (hitMe && en.S is Stance.Watch or Stance.Parted or Stance.Camp && LosingReason(a) == null) Set(en, Stance.Fight, "он напал — отвечаю");
                else if (hitMe && en.S is Stance.Watch or Stance.Parted or Stance.Camp) Set(en, Stance.Flee, $"он напал, а мне не выстоять: {LosingReason(a)}");
                else if (en.S == Stance.Fight && Critical() is string c) Set(en, Stance.Flee, c);
                return;
            }
            var (s, why) = Decide(en, d, hitMe);
            Set(en, s, why);
        }

        void Set(Enc en, Stance s, string why)
        {
            bool changed = en.S != s;
            en.S = s;
            en.Why = why;
            en.Until = _w.Time + 15f + (float)_rng.NextDouble() * 5f;
            if (s == Stance.Parted) en.Until = _w.Time + 120f;
            if (!changed) return;
            string what = s switch { Stance.Fight => "нападаю", Stance.Flee => "ухожу", Stance.Camp => "караулю выход с РТ", Stance.Parted => "разошлись", _ => "держу на виду" };
            Thought($"встреча: {en.A.Name} ({Items.Name(en.A.Held?.Id ?? "tool_rock")}, {Vector2.Distance(en.A.Pos, _me.Pos):0} м) → {what}: {why}");
            Stat(s switch { Stance.Fight => "встреча: нападаю", Stance.Flee => "встреча: ухожу", Stance.Camp => "РТ: караулю", Stance.Parted => "встреча: разошлись", _ => "встреча: слежу" });
            if (en.Rt && s == Stance.Fight) Stat("РТ: спор");
            if (s == Stance.Flee && en.Rt && _rtPhase is RtPhase.Travel or RtPhase.Loot) { LeaveRt($"РТ занята: {en.A.Name} — {why}"); Stat("РТ: уступил"); }
        }

        /// <summary>Повод бросить бой немедленно (не ждать конца решения): при смерти, без патронов.</summary>
        string Critical()
        {
            if (_me.Hp < 30) return $"мало HP ({_me.Hp:0})";
            if (Ranged() is string g && AmmoLeft(g) < (g == "weapon_bow" ? 2 : 4)) return "кончаются патроны";
            return null;
        }

        (Stance, string) Decide(Enc en, float d, bool hitMe)
        {
            var a = en.A;
            float edge = MyPower - KitPower(a) + (Archetype == BotArchetype.Roamer ? 0.1f : 0) + (_me.Hp - 100) / 250f;
            string lose = LosingReason(a);
            bool armedClose = a.Weapon != null && !a.Weapon.Def.Melee && d < 18 + 14 * _caution;
            bool grudge = _grudge.TryGetValue(a.Id, out var g);
            bool loaded = _load > LoadLimit;
            bool home = MyClaim() is Deployable c && Vector2.Distance(c.Pos, a.Pos) < 30 && Vector2.Distance(c.Pos, _me.Pos) < 45;
            en.Rt = _rt != null && (_rtPhase == RtPhase.Loot || (_rtPhase == RtPhase.Travel && Vector2.Distance(_me.Pos, _rt.M.Center) < _rt.Radius + 45)) && _rt.M.Contains(a.Pos, 6);
            string kit = Items.Name(a.Held?.Id ?? "tool_rock");

            if (lose != null) return hitMe || en.Rt || d < 25 || armedClose ? (Stance.Flee, lose) : (Stance.Watch, lose);
            if (hitMe) return (Stance.Fight, "он напал первым");
            if (loaded) return d < 20 || armedClose ? (Stance.Flee, $"несу ≈{_load:0} лома — не до драки (отвечу, если нападёт)") : (Stance.Watch, $"несу ≈{_load:0} лома — обхожу");
            if (grudge) return (Stance.Fight, g.KilledMe ? "он меня убил — мщу" : $"он меня бил ({g.Damage:0} урона) — мщу");
            if (home) return (Stance.Fight, "чужой у моего дома");
            if (en.Rt)
            {
                bool first = _rtPhase == RtPhase.Loot && _rtLootAt < en.FirstSeen - 1;
                if (first) return (Stance.Fight, $"РТ «{_rt.Short}» — я тут первый, выбиваю");
                if (edge >= -0.05f || Aggression > 0.5f) return (Stance.Fight, $"РТ «{_rt.Short}» занята {a.Name}, у него {kit} — отбиваю");
                return (Stance.Camp, $"РТ «{_rt.Short}» занята {a.Name} ({kit}) — жду его на выходе");
            }
            if (_preyIgnore.TryGetValue(a.Id, out float until) && _w.Time < until) return (Stance.Parted, "недавно бросил за ним погоню");
            float need = 0.22f - 0.35f * Aggression;
            if (edge >= need && d < 18 + 30 * Aggression) return (Stance.Fight, $"я не слабее ({Items.Name(Ranged() ?? (Has("weapon_spear") ? "weapon_spear" : "tool_rock"))} против {kit})");
            if (armedClose) return edge >= -0.1f ? (Stance.Fight, $"он со стволом рядом ({kit}) — бью первым") : (Stance.Flee, $"он со стволом рядом ({kit})");
            if (d < 6 && edge >= -0.05f) return (Stance.Fight, "подошёл вплотную");
            if (edge < -0.1f && d < 15) return (Stance.Flee, $"он сильнее ({kit})");
            return (Stance.Watch, "держу на виду");
        }

        /// <summary>Что делать с угрозой: 1 — драться, −1 — уходить, 0 — не трогать (разошлись, слежу). Охрана РТ — по-старому (CanFight).</summary>
        int ThreatMode(Actor t)
        {
            if (!IsPerson(t)) return CanFight(t) ? 1 : -1;
            if (!_enc.TryGetValue(t.Id, out var en)) _enc[t.Id] = en = new Enc { A = t, FirstSeen = _w.Time, LastSeen = _w.Time, SeenPos = t.Pos };
            bool hitMe = _me.LastAttacker == t.Id && _w.Time - _me.LastDamagedTime < 2f;
            Judge(en, Vector2.Distance(t.Pos, _me.Pos), hitMe);
            return en.S == Stance.Fight ? 1 : en.S == Stance.Flee ? -1 : 0;
        }

        // ---------- бегство с точкой и таймером ----------

        Actor _fleeFrom;
        float _fleeEnd = -99;
        Vector2 _fleeDest;

        /// <summary>Начать отход: куда (дом, если не через него; иначе подальше и за укрытие) и сколько (10–16 с).</summary>
        void StartFlee(Actor from)
        {
            _fleeFrom = from;
            _fleeEnd = _w.Time + 10f + 6f * _caution;
            var away = _me.Pos - from.Pos;
            away = away.LengthSquared() > 0.01f ? Vector2.Normalize(away) : Vector2.UnitX;
            var home = MyClaim()?.Pos;
            if (home is Vector2 h && Vector2.Distance(h, _me.Pos) < 160 && Vector2.Dot(Vector2.Normalize(h - _me.Pos + new Vector2(1e-3f, 0)), away) > -0.2f)
                _fleeDest = h + new Vector2(0.6f, 0.6f);
            else
            {
                _fleeDest = _me.Pos + away * 40;
                if (!_w.Terrain.IsLand(_fleeDest)) _fleeDest = _me.Pos + Geo.Rotate(away, 1.2f) * 40;
            }
        }

        /// <summary>Отход окончен: дошёл, вышло время или он тоже уходит — «разошлись» на 2 мин, назад к своей задаче.</summary>
        void EndFlee(string why)
        {
            if (_fleeFrom != null && _enc.TryGetValue(_fleeFrom.Id, out var en) && en.S == Stance.Flee) Set(en, Stance.Parted, why);
            else Thought($"отход окончен: {why}");
            if (_encAvoid == _fleeFrom) _encAvoid = null;
            _fleeFrom = null; _fleeEnd = -99;
            _retreating = false; _hasEscape = false;
        }

        bool Fleeing => _fleeFrom != null && _w.Time < _fleeEnd;

        /// <summary>Очки «бегства»: идёт отход с таймером, угроза не по силам или решено уходить от встречного.</summary>
        float FleeScore()
        {
            if (Fleeing) return 100;
            if (_fleeFrom != null) EndFlee("время отхода вышло");
            if (_threat != null && ThreatMode(_threat) < 0) { StartFlee(_threat); return 100; }
            if (_encAvoid != null && _enc.TryGetValue(_encAvoid.Id, out var ea) && ea.S == Stance.Flee) { StartFlee(_encAvoid); return 100; }
            return 0;
        }

        /// <summary>Проверки в отходе: он тоже уходит — разошлись; дошёл до точки и он не видит — хватит.</summary>
        bool FleeCheck()
        {
            var t = _fleeFrom;
            if (t == null) return false;
            if (t.Dead) { EndFlee($"{t.Name} мёртв"); return true; }
            float d = Vector2.Distance(t.Pos, _me.Pos);
            bool hitMe = _me.LastAttacker == t.Id && _w.Time - _me.LastDamagedTime < 3f;
            var rel = t.Pos - _me.Pos;
            bool leaving = t.Vel.LengthSquared() > 1f && Vector2.Dot(t.Vel, rel) > 0.4f * t.Vel.Length() * d;
            if (!hitMe && d > 10 && leaving && _w.Time - (_fleeEnd - 10f - 6f * _caution) > 2f) { EndFlee("он тоже уходит — разошлись"); Stat("разошлись (оба ушли)"); return true; }
            if (!hitMe && d > 30 && Vector2.Distance(_me.Pos, _fleeDest) < 4f) { EndFlee("дошёл до безопасного места"); return true; }
            return false;
        }

        // ---------- охота на встречного и месть ----------

        float EncFightScore() => _encFight != null && _encFight != _threat && _me.Hp >= 35 ? 88 : 0;

        bool EncFightAct(ref InputCmd cmd)
        {
            if (_encFight == null || _encFight.Dead) return false;
            _prey = _encFight;
            Combat(_encFight, ref cmd);
            return true;
        }

        /// <summary>Погоня за уходящим без толку — разошлись (не гоняться снова 2 мин, если он сам не нападёт).</summary>
        void PartWith(Actor a, string why)
        {
            _preyIgnore[a.Id] = _w.Time + 120f;
            if (_enc.TryGetValue(a.Id, out var en)) Set(en, Stance.Parted, why);
            if (_encFight == a) _encFight = null;
        }

        Grudge _hunt;
        int _huntId;

        /// <summary>Месть: обидчик не сильнее меня, видел его недавно — иду туда, где видел. Не нашёл — след потерян.</summary>
        float RevengeScore()
        {
            _hunt = null;
            if (_load > LoadLimit || _me.Hp < 60 || _rtPhase != RtPhase.None || _raid != null) return 0;
            float best = 0;
            foreach (var (id, g) in _grudge)
            {
                if (_w.Time - g.LastSeen > 180 || g.Kit > MyPower + 0.05f || (_enc.TryGetValue(id, out var en) && _w.Time - en.LastSeen < 3)) continue;
                float s = (g.KilledMe ? 60 : 45) * (0.5f + Aggression) * MathF.Max(0.3f, 1 - Vector2.Distance(g.LastPos, _me.Pos) / 300f);
                if (s > best) { best = s; _hunt = g; _huntId = id; }
            }
            return best;
        }

        bool RevengeAct(ref InputCmd cmd)
        {
            if (_hunt == null) return false;
            var who = _w.Find(_huntId);
            Doing = $"ищет {who?.Name ?? "обидчика"} (месть)";
            if (Ranged() is string gun) Hold(gun, ref cmd); else if (Has("weapon_spear")) Hold("weapon_spear", ref cmd);
            if (MoveTo(_hunt.LastPos, 4f, ref cmd))
            {
                _hunt.LastSeen = -999;
                Thought($"на месте, где видел {who?.Name ?? "обидчика"}, пусто — след потерян");
            }
            if (Doing.StartsWith("ищет")) Stat("месть: поиск (тики)");
            return true;
        }

        // ---------- засада у занятой РТ ----------

        Enc CampTarget()
        {
            if (_rt == null || _rtPhase is not (RtPhase.Travel or RtPhase.Loot)) return null;
            foreach (var en in _enc.Values) if (en.S == Stance.Camp && en.Rt && !en.A.Dead) return en;
            return null;
        }

        float _campSince = -1;

        float CampScore() => CampTarget() != null ? 72 : (_campSince = -1) * 0;

        /// <summary>Жду занявшего РТ на выходе: присел за краем, ствол наготове. Вышел — бью; ушёл из виду — захожу; долго — ухожу.</summary>
        bool CampAct(ref InputCmd cmd)
        {
            var en = CampTarget();
            if (en == null) return false;
            if (_campSince < 0) _campSince = _w.Time;
            var a = en.A;
            if (_w.Time - _campSince > 120) { _campSince = -1; en.S = Stance.Parted; en.Until = _w.Time + 120; LeaveRt($"РТ занята {a.Name} — не дождался"); Stat("РТ: уступил"); return true; }
            bool seen = _w.Time - en.LastSeen < 1f;
            if (seen && !_rt.M.Contains(a.Pos, 2)) { Set(en, Stance.Fight, $"{a.Name} вышел с РТ — бью из засады"); _campSince = -1; return true; }
            if (_w.Time - en.LastSeen > 40) { en.S = Stance.Watch; en.Until = _w.Time + 15; Thought($"{a.Name} давно не видно — захожу на РТ"); _campSince = -1; return false; }
            var dir = _me.Pos - _rt.M.Center;
            dir = dir.LengthSquared() > 0.01f ? Vector2.Normalize(dir) : Vector2.UnitX;
            var spot = _rt.M.Center + dir * (_rt.Radius + 14);
            Doing = $"караулит {a.Name} у РТ «{_rt.Short}»";
            if (Ranged() is string gun) Hold(gun, ref cmd);
            if (!MoveTo(spot, 2.5f, ref cmd)) return true;
            cmd.Crouch = true;
            cmd.Aim = en.SeenPos;
            return true;
        }

        // ---------- сторож прогресса и здоровье мозга ----------

        float _progAt, _progCheckAt, _forceUntil = -1;
        long _progSig = -1;
        string _forceJob;

        long ProgressSig()
        {
            int structs = 0;
            foreach (var s in _w.Structures) if (s.OwnerId == _me.Id) structs++;
            int res = _me.Count("wood") + _me.Count("stone") + _me.Count("metal_fragments") * 3 + _me.Count("scrap") * 5 + _me.Count("metal_ore") + _me.Count("sulfur_ore");
            return structs * 1_000_000L + _me.Known.Count * 100_000L + MyBenchLevel() * 10_000_000L + (long)(_stash / 25) * 1000 + res / 50;
        }

        /// <summary>5 мин без сдвига (ресурсы, постройки, изучения, ящик) — минута базовой задачи: сложить добычу или фарм.</summary>
        float WatchdogScore()
        {
            if (_w.Time - _progCheckAt >= 10)
            {
                _progCheckAt = _w.Time;
                long sig = ProgressSig();
                if (sig != _progSig) { _progSig = sig; _progAt = _w.Time; }
                else if (_w.Time - _progAt > 300 && _w.Time > _forceUntil)
                {
                    _forceJob = _surplus >= 25 && MyBox() != null ? "домой и сложить добычу" : "фарм дерева и камня";
                    _forceUntil = _w.Time + 60;
                    _progAt = _w.Time;
                    Thought($"сторож прогресса: 5 мин без сдвига (цель «{GoalName}», {Doing}) → беру базовую задачу: {_forceJob}");
                    Stat("сторож прогресса");
                    if (_rtPhase != RtPhase.None) EndRun("сторож прогресса: поход застрял");   // иначе «несёт домой» держал цель дальше
                    _path = null; _node = null;
                }
            }
            return _w.Time < _forceUntil ? 92 : 0;
        }

        bool WatchdogAct(ref InputCmd cmd)
        {
            if (_forceJob == "домой и сложить добычу" && _surplus >= 10 && StashAct(ref cmd)) return true;
            Gather(_me.Count("wood") <= _me.Count("stone") ? NodeKind.Tree : NodeKind.Stone, ref cmd);
            return true;
        }

        // доля времени в бегстве и в бою/охоте (метрика здоровья мозга; > 30 % за 10 мин — тревога)
        float _hFlee, _hFight, _hAll;
        public float TotFlee, TotFight, TotAll;

        void CountHealth()
        {
            if (_me.Dead || _goal == null) return;
            _hAll += World.Dt; TotAll += World.Dt;
            if (_goal.Name == "бегство") { _hFlee += World.Dt; TotFlee += World.Dt; }
            else if (_goal.Name is "бой" or "охота" or "месть" or "засада у РТ") { _hFight += World.Dt; TotFight += World.Dt; }
        }

        /// <summary>Строка «ЗДОРОВЬЕ» за прошедшее окно; сбрасывает окно.</summary>
        public string HealthReport()
        {
            float all = MathF.Max(1, _hAll);
            float f = _hFlee / all, h = _hFight / all;
            string s = $"бегство {f:P0}, бой/охота {h:P0}{(f + h > 0.3f ? " — ТРЕВОГА: больше 30 %" : "")}";
            _hFlee = _hFight = _hAll = 0;
            return s;
        }
    }
}
