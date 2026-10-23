using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Бой ботов (docs/design/bot-brain.md, этап 3): дистанция по оружию, стрейф, укрытия (камни, деревья, стены, постройки)
    /// с проверкой «пуля не пройдёт», пики из-за укрытия (вышел — выстрелил — назад), перезарядка и бинт в укрытии,
    /// кайт против ближнего боя, отход с разрывом линии обзора, дожим, когда противник перезаряжается или слаб.
    /// Честно: бот знает о противнике только то, что видел (позиция, оружие в руках, перезарядка) и слышал
    /// (выстрелы, звук перезарядки, свои попадания) — без всевидения.
    /// </summary>
    public sealed partial class PlayerBot
    {
        enum Fight { None, Open, ToCover, InCover, Peek, Kite, Push }

        // ---------- память о противнике ----------

        Actor _foe;
        Vector2 _foeSeenPos;
        float _foeSeenAt = -100, _foeHeardAt = -100, _foeReloadAt = -100;
        float _foeDamage, _foeHitAt = -100;   // сколько я по нему попал — оценка его HP
        float _foeSince;                       // когда начал с ним бой
        float _foeKit = 0.1f;      // сила того, что видел у него в руках
        string _foeKitId = "tool_rock";

        Fight _fight;
        Vector2 _coverPos, _peekPos;
        string _coverName = "";
        bool _hasCover, _hasPeek, _trig;
        float _coverCheck, _phaseTime, _phaseLen, _peekUnseen;
        int _peekShots;
        float _lastShotAt = -100, _coverSince, _pushUntil = -100, _chaseStart, _chaseTick = -99;
        float _adT, _adSign = 1, _crT;   // открытый бой: смена стрейфа (A-D) и присед-встал — неровный ритм, как у людей
        bool _crOn;
        int _burstShots; float _burstPause;
        public float RecoilControl = 0.7f;   // доля отдачи, которую бот гасит прицелом (человек мышью гасит почти всю)

        /// <summary>В разгаре перестрелки: недавно стрелял или по мне попали, он рядом и на виду — дерусь до конца, не бегу спиной.</summary>
        bool Committed() => _w.Time - _foeSeenAt < 3f && (_w.Time - _lastShotAt < 3f || _w.Time - _me.LastDamagedTime < 3f)
                            && _foe != null && Vector2.Distance(KnownPos(_foe), _me.Pos) < 28f;
        Vector2 _chaseFrom;
        Vector2 _flankFrom;
        Actor _chaseFoe;
        readonly Dictionary<int, float> _preyIgnore = new();   // брошенная добыча: id → до какого времени не трогать

        Vector2 _escapePos;
        bool _hasEscape, _retreating;
        float _escapeCheck, _lastFleeAt = -100;
        string _loseWhy;

        readonly List<ResourceNode> _coverNodes = new();
        readonly List<MapWall> _coverWalls = new();
        readonly List<Deployable> _coverDeps = new();
        readonly Dictionary<string, float> _combatSaid = new();

        /// <summary>Счётчики боя для прогонов: выстрелы из укрытия и в открытую, перезарядки, отходы, пики.</summary>
        public readonly Dictionary<string, int> CombatStats = new();
        void Stat(string k) => CombatStats[k] = CombatStats.GetValueOrDefault(k) + 1;

        /// <summary>Мысль о бое — не чаще раза в every секунд на ключ (иначе журнал тонет в повторах).</summary>
        void Say(string key, string text, float every = 8f)
        {
            if (_w.Time - _combatSaid.GetValueOrDefault(key, -999) < every) return;
            _combatSaid[key] = _w.Time;
            Thought(text);
        }

        static string WeaponAmmo(string gun) => Items.Get(gun)?.Weapon?.AmmoId ?? "arrow";

        /// <summary>Сила оружия на глаз (как и KitPower): калаш 1, самопал 0,8, лук 0,6, копьё 0,4.</summary>
        static float GunPower(string id) => Weaponry.IsGun(id) ? Weaponry.Power(id) : id switch { "weapon_spear" => 0.4f, "tool_hatchet" or "tool_pickaxe" => 0.25f, _ => 0.1f };

        /// <summary>Удобная дистанция боя: лук и самопал — средняя, калаш — дальняя.</summary>
        static float PrefDist(string gun) => Weaponry.PrefDist(gun);

        WeaponState GunState(string gun) { int s = SlotOf(gun); return s >= 0 ? _me.Belt[s].State : null; }

        int AmmoLeft(string gun) => (GunState(gun)?.Ammo ?? (PackOf(gun) is int p && p >= 0 ? _me.Pack[p].State?.Ammo ?? 0 : 0)) + _me.Count(WeaponAmmo(gun));

        float FoeHp(Actor t) => t == _foe ? Math.Clamp(100 - _foeDamage, 5, 100) : 100;

        /// <summary>Вижу ли его сейчас: дальность обзора, стены и стволы; присевшего за низким укрытием — нет.</summary>
        bool SeesFoe(Actor t) => !t.Dead && _w.CanSeeActor(_me, t, _w.BotSightRange(_me, t), World.BotSightCone);

        /// <summary>Где он, насколько я знаю: вижу — где стоит, нет — где видел или слышал последний раз.</summary>
        Vector2 KnownPos(Actor t) => t != _foe || SeesFoe(t) ? t.Pos : _foeSeenPos;

        float FoeKit(Actor t) => t == _foe ? _foeKit : KitPower(t);

        void NewFoe(Actor t)
        {
            _foe = t;
            _foeDamage = 0; _foeHitAt = -100;
            _foeSince = _w.Time;
            _foeSeenPos = t.Pos;
            _foeSeenAt = _w.Time;
            _foeKit = KitPower(t);
            _foeKitId = t.Held?.Id ?? "tool_rock";
            _hasCover = false; _hasEscape = false;
            _fight = Fight.None;
        }

        /// <summary>Восприятие боя (из Perceive): обновить память о противнике — увиденное и услышанное.</summary>
        void PerceiveFoe()
        {
            if (_threat != null && _threat != _foe) NewFoe(_threat);
            if (_foe == null) return;
            if (_foe.Dead)
            {
                if (_fight != Fight.None || _retreating) Thought($"{_foe.Name} убит — бой окончен");
                ForgetFoe();
                return;
            }
            if (SeesFoe(_foe))
            {
                _foeSeenPos = _foe.Pos;
                _foeSeenAt = _w.Time;
                _foeKit = KitPower(_foe);
                _foeKitId = _foe.Held?.Id ?? "tool_rock";
                if (_foe.Weapon != null && _foe.Weapon.ReloadLeft > 0) _foeReloadAt = _w.Time;   // видно, как он перезаряжается
            }
            foreach (var e in _w.Events)
            {
                if (e.ActorId == _foe.Id && e.Kind == SimEventKind.Shot && Vector2.Distance(e.Pos, _me.Pos) < HearRange(e.Text, false))
                { _foeHeardAt = _w.Time; if (_w.Time - _foeSeenAt > 0.5f) _foeSeenPos = e.Pos; }   // по звуку — примерно где он
                if (e.ActorId == _foe.Id && e.Kind == SimEventKind.Reload && Vector2.Distance(e.Pos, _me.Pos) < 25) _foeReloadAt = _w.Time;   // слышно перезарядку
                if (e.Kind == SimEventKind.HitActor && e.ActorId == _foe.Id && e.OtherId == _me.Id) { _foeDamage += e.Value; _foeHitAt = _w.Time; }
            }
            // давно не попадал — он мог забинтоваться: оценку его HP понемногу возвращаем
            if (_w.Time - _foeHitAt > 4f) _foeDamage = MathF.Max(0, _foeDamage - 6f * World.Dt);
            // долго ни видно, ни слышно — забыть
            if (_w.Time - MathF.Max(_foeSeenAt, _foeHeardAt) > 14f)
            {
                if (_fight != Fight.None) Thought($"потерял {_foe.Name} из виду — бой окончен");
                ForgetFoe();
            }
        }

        void ForgetFoe() { _foe = null; _fight = Fight.None; _hasCover = false; _hasEscape = false; _retreating = false; }

        /// <summary>Угроза из памяти: спрятался за камень — не значит, что ушёл.</summary>
        Actor RememberedFoe() => _foe != null && !_foe.Dead && _w.Time - MathF.Max(_foeSeenAt, _foeHeardAt) < 10f && Vector2.Distance(_foeSeenPos, _me.Pos) < 45f ? _foe : null;

        // ---------- драться или отходить ----------

        /// <summary>Причина не драться (null — драться): HP, патроны, его оружие сильнее, проигрываю размен.</summary>
        string LosingReason(Actor t)
        {
            var known = KnownPos(t);
            float d = Vector2.Distance(known, _me.Pos);
            bool hitMe = _me.LastAttacker == t.Id && _w.Time - _me.LastDamagedTime < 4f;
            float foeKit = FoeKit(t), foeHp = FoeHp(t);
            float edge = MyPower - foeKit + (Archetype == BotArchetype.Roamer ? 0.1f : 0) + (_me.Hp - foeHp) / 250f;
            var gun = Ranged();
            if (gun == null)
            {
                // ближник против ближника не сильнее — драться (раньше нужно было «заметно сильнее»: двое с камнями разбегались оба)
                if (foeKit < 0.5f && edge > -0.08f && _me.Hp >= 40) return null;
                if (foeKit < 0.5f && hitMe && edge > -0.2f && _me.Hp >= 30) return null;   // напал — отмахиваюсь
                return foeKit >= 0.6f ? $"у него {Items.Name(_foeKitId)}, а мне нечем стрелять" : "нечем драться";
            }
            if (AmmoLeft(gun) < (gun == "weapon_bow" ? 2 : 4)) return "кончаются патроны";
            if (Committed() && t == _foe)
            {
                // уже сцепились: бежать спиной под огнём — верная смерть; отход только почти мёртвым против целого
                if (_me.Hp < 18 && foeHp > _me.Hp + 25) return $"почти мёртв ({_me.Hp:0} HP)";
                return null;
            }
            if (_me.Hp < 30 && foeHp > _me.Hp) return $"мало HP ({_me.Hp:0})";
            if (_me.Hp < 45 && foeKit > MyPower) return $"ранен ({_me.Hp:0} HP), а у него {Items.Name(_foeKitId)}";
            if (AmmoLeft(gun) < (gun == "weapon_bow" ? 2 : 4)) return "кончаются патроны";
            // охрана РТ стоит на посту за стеной: минута перестрелки без попаданий — пат, тратить патроны и HP незачем (этап 4)
            if (t.Team == Team.Npc && _w.Time - _foeSince > 60 && _w.Time - MathF.Max(_foeHitAt, _foeSince) > 35)
            {
                if (_rtPhase is RtPhase.Loot or RtPhase.Travel) LeaveRt("пат с охраной — ухожу");
                return "пат с охраной: минуту не попадаю";
            }
            if (edge < -0.3f) return $"у него {Items.Name(_foeKitId)} — сильнее моего {Items.Name(gun)}";
            return null;
        }

        // ---------- отход: разорвать линию обзора, подлечиться, уйти домой ----------

        /// <summary>Точка отхода: подальше от него, чтобы он меня не видел и не мог прострелить; ближе к дому.</summary>
        bool FindEscape(Vector2 foePos)
        {
            var away = _me.Pos - foePos;
            if (away.LengthSquared() < 0.01f) away = Vector2.UnitX;
            away = Vector2.Normalize(away);
            var home = MyClaim()?.Pos;
            float best = float.MaxValue;
            for (int k = 0; k < 16; k++)
            {
                float a = k * MathF.PI / 8;
                var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                if (Vector2.Dot(dir, away) < -0.2f) continue;   // не к нему
                foreach (float dist in new[] { 9f, 15f, 22f })
                {
                    var p = _me.Pos + dir * dist;
                    if (!_w.IsFree(p, _me.Radius + 0.1f)) continue;
                    bool hidden = !_w.HasLineOfSight(foePos, p) || !_w.BulletClear(foePos, p);
                    float s = (hidden ? 0 : 25) - Vector2.Distance(p, foePos) * 0.5f + dist * 0.2f
                              + (home is Vector2 h ? Vector2.Distance(p, h) * 0.04f : 0)
                              + (_w.SegmentWalkable(_me.Pos, p, _me.Radius + 0.05f) ? 0 : 12);
                    if (s < best) { best = s; _escapePos = p; }
                }
            }
            _hasEscape = best < float.MaxValue;
            return _hasEscape;
        }

        /// <summary>Отход (цель «бегство»): за укрытие от его взгляда, бегом; оторвался — бинт, перезарядка, домой.</summary>
        bool Retreat(ref InputCmd cmd)
        {
            if (!Fleeing) { _redecide = true; return false; }   // отход окончен (по таймеру) — к своей задаче
            if (FleeCheck()) { _redecide = true; return false; }   // разошлись или дошёл — назад к своей задаче
            var t = _fleeFrom ?? _threat ?? RememberedFoe();
            if (!_retreating || _w.Time - _lastFleeAt > 2f)
            {
                _retreating = true;
                _hasEscape = false;
                if (t != null) { Thought($"отхожу от {t.Name} на {_fleeEnd - _w.Time:0} с: {(_enc.TryGetValue(t.Id, out var en) && en.S == Stance.Flee ? en.Why : _loseWhy ?? LosingReason(t) ?? "опасно")}"); Stat("отход"); }
            }
            _lastFleeAt = _w.Time;
            Doing = "отходит";
            // далеко от него и не видит — к точке отхода (дом или подальше), а не кружить у камня
            if (_fleeFrom != null && t != null && !SeesFoe(t) && Vector2.Distance(t.Pos, _me.Pos) > 20 && Vector2.Distance(_me.Pos, _fleeDest) > 4f)
            {
                Doing = "отходит к безопасному месту";
                GoTo(_fleeDest, 3f, ref cmd);
                cmd.Sprint = true;
                cmd.Aim = _me.Pos + (cmd.Move.LengthSquared() > 0.01f ? cmd.Move : Vector2.UnitX) * 3;
                return true;
            }
            var from = t != null ? KnownPos(t) : _me.Pos - _me.FacingDir;
            bool seen = t != null && SeesFoe(t);
            if (!_hasEscape || (_escapeCheck -= World.Dt) <= 0 || Vector2.Distance(_me.Pos, _escapePos) < 1.5f && seen)
            {
                _escapeCheck = 1.5f;
                FindEscape(from);
            }
            bool arrived = !_hasEscape || Vector2.Distance(_me.Pos, _escapePos) < 1.5f;
            if (!arrived)
            {
                MoveTo(_escapePos, 1.2f, ref cmd);
                cmd.Sprint = true;
                cmd.Aim = _me.Pos + (cmd.Move.LengthSquared() > 0.01f ? cmd.Move : Vector2.Normalize(_escapePos - _me.Pos)) * 3;   // спринт — только вперёд
                // догоняет вплотную с копьём — отмахнуться
                if (t != null && Vector2.Distance(t.Pos, _me.Pos) < 2.2f && seen && Ranged() is string g0) { cmd.Sprint = false; cmd.Aim = t.Pos; if (Hold(g0, ref cmd)) cmd.Fire = _trig = !_trig; }
                return true;
            }
            // оторвался: укрыт — присесть, перезарядиться и забинтоваться
            cmd.Crouch = true;
            if (t != null) cmd.Aim = from;
            // спрятаться не вышло, он видит меня — отстреливаться, пока ищу новую точку
            if (seen && Ranged() is string g1 && GunState(g1) is WeaponState st1 && Hold(g1, ref cmd))
                Shoot(t, g1, st1, Vector2.Distance(t.Pos, _me.Pos), true, ref cmd, "в открытую");
            if (!seen)
            {
                Say("оторвался", "оторвался — укрылся, перевожу дух", 15);
                if (_me.Hp < 75 && _me.Count("bandage") > 0 && _w.Time - _me.LastDamagedTime > 2f) { cmd.Heal = true; if (_me.HealCooldown <= 0) { Say("бинт-отход", $"бинтуюсь в укрытии ({_me.Hp:0} HP)", 10); Stat("бинт в укрытии"); } }
                if (Ranged() is string g && GunState(g) is WeaponState st && st.Ammo < st.MagSize && st.ReloadLeft <= 0 && _me.Count(WeaponAmmo(g)) > 0 && Hold(g, ref cmd))
                { cmd.Reload = true; if (st.MagSize > 1) { Stat("перезарядка в укрытии"); Say("перезарядка-отход", "перезаряжаюсь, пока он меня не видит", 10); } }
                // и уходим домой, когда он потерян
                if (t == null && MyClaim() is Deployable c && Vector2.Distance(c.Pos, _me.Pos) > 12) { cmd.Crouch = false; Doing = "отходит домой"; GoTo(c.Pos + new Vector2(0.6f, 0.6f), 3f, ref cmd); }
            }
            return true;
        }

        // ---------- укрытия ----------

        static string CoverLabel(MapWall w) => w.Owner != null ? "стеной" : w.Kind switch
        {
            WallKind.Rock => "скалой", WallKind.Car => "машиной", WallKind.Concrete => "бетонной стеной",
            WallKind.Crate or WallKind.WoodCrate => "ящиком", WallKind.Barrier => "блоком", WallKind.Barrel => "бочкой", _ => "укрытием",
        };

        /// <summary>
        /// Ищет укрытие от точки foePos: место за камнем / деревом / стеной / установленным, куда его пуля не пройдёт,
        /// недалеко от меня, на удобной дистанции, и с местом для пика сбоку (откуда пуля до него проходит).
        /// </summary>
        bool FindCover(Vector2 foePos, float pref)
        {
            float best = float.MaxValue;
            const float R = 15f;

            void Consider(Vector2 p, string name, float extra)
            {
                float dm = Vector2.Distance(_me.Pos, p);
                if (dm > 14f) return;
                if (_flankFrom != default && Vector2.Distance(p, _flankFrom) < 3f && _w.Time - _coverSince < 1f) return;   // обход — не на старое место
                float df = Vector2.Distance(p, foePos);
                if (df < 5f || df > pref + 22f) return;
                // грубая оценка до проверок мира — дешёвая отсечка
                float pre = dm * 1.2f + MathF.Abs(df - pref) * 0.3f + extra;
                if (pre >= best) return;
                if (!_w.IsFree(p, _me.Radius + 0.05f) || _w.BulletClear(foePos, p)) return;
                // пик: шаг вбок, откуда пуля до него проходит и его видно
                var side = Vector2.Normalize(new Vector2(-(p - foePos).Y, (p - foePos).X));
                Vector2 peek = default; bool ok = false;
                foreach (float off in new[] { 1.1f, -1.1f, 1.7f, -1.7f, 2.4f, -2.4f })
                {
                    var q = p + side * off;
                    if (!_w.IsFree(q, _me.Radius + 0.05f)) continue;
                    var muzzle = q + Vector2.Normalize(foePos - q) * 0.75f;
                    if (_w.BulletClear(muzzle, foePos) && _w.HasLineOfSight(q, foePos)) { peek = q; ok = true; break; }
                }
                float s = pre + (ok ? 0 : 7f);
                if (s >= best) return;
                best = s; _coverPos = p; _peekPos = ok ? peek : p; _hasPeek = ok; _coverName = name;
            }

            foreach (var n in _w.NodesInRadius(_me.Pos, R, _coverNodes))
            {
                if (n.Kind == NodeKind.Barrel) continue;   // бочка разлетается от пуль
                var dir = n.Pos - foePos;
                if (dir.LengthSquared() < 0.01f) continue;
                dir = Vector2.Normalize(dir);
                Consider(n.Pos + dir * (n.Radius + _me.Radius + 0.2f), n.Kind == NodeKind.Tree ? "деревом" : n.Kind == NodeKind.Junk ? "кучей хлама" : "камнем", n.Kind == NodeKind.Tree ? 1.5f : 0);
            }
            foreach (var w in _w.WallsInRadius(_me.Pos, R, _coverWalls))
            {
                if (w.PassBullets) continue;
                var min = w.Wall.Min; var max = w.Wall.Max;
                var c = (min + max) * 0.5f;
                float gap = _me.Radius + 0.25f;
                // каждая грань: ближайшая ко мне точка грани, вынесенная наружу
                for (int f = 0; f < 4; f++)
                {
                    var normal = f switch { 0 => -Vector2.UnitX, 1 => Vector2.UnitX, 2 => -Vector2.UnitY, _ => Vector2.UnitY };
                    if (Vector2.Dot(normal, c - foePos) <= 0) continue;   // грань, смотрящая на него, — не укрытие
                    float inset = 0.3f;
                    var q = new Vector2(
                        f == 0 ? min.X : f == 1 ? max.X : Math.Clamp(_me.Pos.X, MathF.Min(min.X + inset, c.X), MathF.Max(max.X - inset, c.X)),
                        f == 2 ? min.Y : f == 3 ? max.Y : Math.Clamp(_me.Pos.Y, MathF.Min(min.Y + inset, c.Y), MathF.Max(max.Y - inset, c.Y)));
                    Consider(q + normal * gap, CoverLabel(w), w.Wall.BlocksVision ? 0 : 0.5f);
                }
            }
            foreach (var dp in _w.DeploysInRadius(_me.Pos, R, _coverDeps))
            {
                if (dp.Kind is DeployKind.SleepBag) continue;
                var dir = dp.Pos - foePos;
                if (dir.LengthSquared() < 0.01f) continue;
                Consider(dp.Pos + Vector2.Normalize(dir) * (dp.Radius + _me.Radius + 0.2f), dp.Kind switch { DeployKind.Furnace => "печью", DeployKind.Workbench => "верстаком", DeployKind.Claim => "шкафом", DeployKind.Recycler => "переработчиком", _ => "ящиком" }, 1f);
            }
            return best < float.MaxValue;
        }

        // ---------- бой ----------

        /// <summary>Дальний бой с противником t (цели «бой» и «охота»). Ближний — как раньше (Hunt).</summary>
        void Combat(Actor t, ref InputCmd cmd)
        {
            if (t != _foe) NewFoe(t);
            // погоня без толку (не догнать, не попасть) — бросить добычу
            if (t == _prey)
            {
                if (_chaseFoe != t || _w.Time - _chaseTick > 5f) { _chaseFoe = t; _chaseStart = _w.Time; _chaseFrom = _me.Pos; }
                _chaseTick = _w.Time;
                // уходит от меня, а я 12 с не попадаю (или 25 с вообще) — не гоняться: разошлись на 2 мин, если он сам не нападёт
                var rel = t.Pos - _me.Pos;
                bool leaving = t.Vel.LengthSquared() > 1f && Vector2.Dot(t.Vel, rel) > 0.4f * t.Vel.Length() * rel.Length();
                bool hitMe = _me.LastAttacker == t.Id && _w.Time - _me.LastDamagedTime < 8f;
                float noHit = _w.Time - MathF.Max(_foeHitAt, _chaseStart);
                // раздел 10: погоня не дольше ~35 с и 60 м от места, где начал, — если он не несёт много лута
                bool rich = (CarrierOf(t)?.Est ?? 0) >= 150;
                bool tooLong = !rich && (_w.Time - _chaseStart > 35f || Vector2.Distance(_me.Pos, _chaseFrom) > 60f) && _w.Time - _foeHitAt > 3f;
                if (!hitMe && (noHit > 25f || tooLong || (leaving && noHit > 12f && rel.Length() > 12f)))
                {
                    if (tooLong) LStat("погоня: предел 35 с / 60 м");
                    Thought($"бросаю погоню за {t.Name}: {(tooLong ? "гоняюсь слишком долго — не стоит того" : noHit > 25f ? "25 с ни одного попадания" : "он уходит, не догнать")}");
                    Stat("брошена погоня");
                    PartWith(t, "бросил погоню");
                    _prey = null; _chaseFoe = null;
                    ForgetFoe();
                    return;
                }
            }
            var gun = Ranged();
            var known = KnownPos(t);
            float d = Vector2.Distance(known, _me.Pos);
            if (gun == null || d < 2.2f && FoeKit(t) < 0.5f) { MeleeFight(t, ref cmd); return; }
            if (!Hold(gun, ref cmd)) return;
            var st = GunState(gun);
            if (st == null) return;
            bool sees = SeesFoe(t);
            bool hitRecently = _w.Time - _me.LastDamagedTime < 1.5f;
            float pref = PrefDist(gun);
            if (Weaponry.Power(_foeKitId) >= 1f && GunPower(gun) < 0.95f) pref = MathF.Min(pref, 12f);   // с самопалом и луком против калаша — ближе не страшнее, но дальше бесполезно
            Doing = $"{(Archetype == BotArchetype.Roamer ? "охотится на" : "дерётся с")} {t.Name}";

            // 1. он ближник, а у меня ствол — кайт: держать дистанцию и стрелять на отходе
            if (_foeKit < 0.5f)
            {
                if (_fight != Fight.Kite) { Say("кайт", $"кайчу {t.Name}: у него {Items.Name(_foeKitId)}, держу дистанцию"); Stat("кайт"); }
                _fight = Fight.Kite;
                Kite(t, gun, st, d, sees, ref cmd);
                return;
            }

            // 2. дожать: он перезаряжается, почти мёртв или заметно слабее
            string push = _w.Time - _foeReloadAt < 1.6f && d < 30 ? "он перезаряжается"
                        : FoeHp(t) < 35 && _me.Hp > 50 ? $"он едва жив (≈{FoeHp(t):0} HP)"
                        : MyPower - _foeKit >= 0.35f && _me.Hp > 60 ? $"у него только {Items.Name(_foeKitId)}"
                        : _w.Time < _pushUntil ? "выкуриваю его из-за укрытия" : null;
            if (push != null && st.Ammo > 0 && st.ReloadLeft <= 0 && (sees || _w.Time - _foeSeenAt < 3f))
            {
                if (_fight != Fight.Push) { Say("дожим", $"давлю {t.Name}: {push}", 5); Stat("дожим"); }
                _fight = Fight.Push;
                var to = known - _me.Pos;
                var dir = to.LengthSquared() > 0.01f ? Vector2.Normalize(to) : Vector2.UnitX;
                var sideP = new Vector2(-dir.Y, dir.X) * (MathF.Sin(_w.Time * 1.3f + _me.Id) > 0 ? 1 : -1);
                cmd.Move = Avoid(Vector2.Normalize(d > pref * 0.6f ? dir + sideP * 0.4f : sideP));
                Shoot(t, gun, st, d, sees, ref cmd, "в открытую");
                return;
            }

            // 3. укрытие: выбрать, сменить, если обошли
            // пат: оба за укрытиями, давно ни одного выстрела, а он на виду — зайти сбоку или выкурить
            if (_hasCover && _fight is (Fight.InCover or Fight.Peek) && _w.Time - MathF.Max(_lastShotAt, _coverSince) > 8f && _w.Time - _foeSeenAt < 2f)
            {
                _flankFrom = _coverPos;
                _hasCover = false; _coverCheck = 0; _coverSince = _w.Time;
                Stat("обход");
                if (!FindCover(known, pref))
                {
                    _hasCover = false;
                    if (_me.Hp >= 60 && Aggression > 0.25f) { _pushUntil = _w.Time + 4f; Say("выкурить", $"{t.Name} засел за укрытием — иду выкуривать", 8); }
                    else { _hasCover = true; _coverPos = _flankFrom; Say("пат", $"{t.Name} засел за укрытием — держу угол", 12); }
                }
                else { _hasCover = true; _fight = Fight.ToCover; Say("обход-сбоку", $"{t.Name} засел за укрытием — захожу сбоку, за {_coverName}", 8); }
            }
            if ((_coverCheck -= World.Dt) <= 0)
            {
                _coverCheck = 1.2f;
                if (_hasCover && _w.BulletClear(known, _coverPos))
                {
                    _hasCover = false;
                    Say("обошли", $"{t.Name} обошёл моё укрытие — меняю", 6);
                    Stat("смена укрытия");
                }
                if (!_hasCover || _fight == Fight.Open)
                {
                    if (FindCover(known, pref))
                    {
                        _hasCover = true;
                        _coverSince = _w.Time;
                        _fight = Fight.ToCover;
                        Say("укрытие", $"укрываюсь за {_coverName} ({Vector2.Distance(_me.Pos, _coverPos):0} м){(_hasPeek ? ", буду пикать" : "")}", 6);
                    }
                    else if (_fight != Fight.Open)
                    {
                        _fight = Fight.Open;
                        Say("открыто", "укрыться негде — стреляю со стрейфом", 10);
                    }
                }
            }

            if (!_hasCover) { OpenFight(t, gun, st, d, pref, sees, ref cmd); return; }

            // 4. дойти до укрытия (бегом, если по мне попадают или далеко; вблизи — отстреливаясь)
            float dc = Vector2.Distance(_me.Pos, _coverPos);
            if (_fight is not (Fight.InCover or Fight.Peek)) _fight = Fight.ToCover;
            if (_fight == Fight.ToCover || (_fight == Fight.InCover && dc > 0.8f))
            {
                if (dc > 0.5f)
                {
                    _fight = Fight.ToCover;
                    MoveTo(_coverPos, 0.4f, ref cmd);
                    // бегом (спиной) — только если укрытие далеко и он меня не видит, или стрелять нечем; иначе — к укрытию с огнём
                    bool run = (dc > 6f && !sees) || (dc > 3f && st.Ammo == 0 && st.ReloadLeft <= 0 && _me.Count(WeaponAmmo(gun)) == 0) || dc > 14f;
                    if (run) { cmd.Sprint = true; cmd.Aim = _me.Pos + (cmd.Move.LengthSquared() > 0.01f ? cmd.Move : Vector2.UnitX) * 3; }
                    else Shoot(t, gun, st, d, sees, ref cmd, "в открытую");
                    return;
                }
                _fight = Fight.InCover;
                _phaseTime = 0;
                _phaseLen = 0.4f + (float)_rng.NextDouble() * 0.8f;
            }

            // 5. в укрытии: перезарядка, бинт, потом пик
            if (_fight == Fight.InCover)
            {
                cmd.Crouch = true;
                cmd.Aim = known;
                cmd.Move = Vector2.Distance(_me.Pos, _coverPos) > 0.25f ? Vector2.Normalize(_coverPos - _me.Pos) * 0.6f : Vector2.Zero;
                _phaseTime += World.Dt;
                int mag = st.MagSize;
                bool needReload = st.ReloadLeft <= 0 && _me.Count(WeaponAmmo(gun)) > 0 && (st.Ammo == 0 || (mag > 1 && st.Ammo < mag * 0.5f));
                if (st.ReloadLeft > 0) { Doing = $"перезаряжается за {_coverName}"; return; }
                if (needReload)
                {
                    cmd.Reload = true;
                    if (mag > 1) { Stat("перезарядка в укрытии"); Say("перезарядка", $"перезаряжаюсь за {_coverName}", 6); }
                    return;
                }
                if (_me.Hp < 65 && _me.Count("bandage") > 0 && _w.Time - _me.LastDamagedTime > 2f && _me.HealCooldown <= 0)
                {
                    cmd.Heal = true;
                    Stat("бинт в укрытии");
                    Say("бинт", $"бинтуюсь за {_coverName} ({_me.Hp:0} HP)", 6);
                    return;
                }
                if (!_hasPeek)
                {
                    // пикнуть неоткуда — укрытие глухое: подождать и поискать другое
                    if (_phaseTime > 2.5f) { _hasCover = false; _coverCheck = 0; _fight = Fight.Open; }
                    return;
                }
                if (_phaseTime >= _phaseLen)
                {
                    _fight = Fight.Peek;
                    _phaseTime = 0; _peekShots = 0; _peekUnseen = 0;
                    _phaseLen = Items.Get(gun)?.Weapon?.Auto == true ? 1.4f + (float)_rng.NextDouble() * 0.6f : Weaponry.Tier(gun) >= 1 ? 2.2f : 2.5f;   // держит угол: ждёт, когда он высунется
                    Say("пик", $"пикаю из-за {CoverFrom(_coverName)}", 12);
                }
                return;
            }

            // 6. пик: шаг в сторону, очередь / выстрел, назад
            if (_fight == Fight.Peek)
            {
                _phaseTime += World.Dt;
                var step = _peekPos - _me.Pos;
                cmd.Move = step.Length() > 0.2f ? Vector2.Normalize(step) : Vector2.Zero;
                cmd.Crouch = st.Def.Auto && !_w.LowCoverNear(_me.Pos, t.Pos);   // калаш — из приседа, точнее; но не из-за низкого укрытия — оттуда не видно
                int before = st.Ammo;
                if (step.Length() < 0.6f) Shoot(t, gun, st, d, sees, ref cmd, "из укрытия");
                else { cmd.Aim = known; cmd.Peek = 1; }
                if (st.Ammo < before) _peekShots++;
                if (!sees) _peekUnseen += World.Dt; else _peekUnseen = 0;
                bool back = _phaseTime > _phaseLen || st.Ammo == 0 || (gun == "weapon_bow" && _peekShots > 0)
                            || (hitRecently && _phaseTime > 0.3f) || _peekUnseen > 2f;
                if (back)
                {
                    if (_peekUnseen > 2f && _w.Time - _foeSeenAt > 4f) { _coverCheck = 0; _hasCover = false; Say("не вижу", $"из-за {CoverFrom(_coverName)} его не видно — ищу позицию", 8); }
                    _fight = Fight.ToCover;
                }
            }
        }

        /// <summary>«за камнем» → «из-за камня»: падеж укрытия для мыслей.</summary>
        static string CoverFrom(string name) => name switch
        {
            "камнем" => "камня", "деревом" => "дерева", "скалой" => "скалы", "машиной" => "машины", "стеной" => "стены",
            "бетонной стеной" => "бетонной стены", "ящиком" => "ящика", "блоком" => "блока", "бочкой" => "бочки", "кучей хлама" => "кучи хлама",
            _ => "укрытия",
        };

        /// <summary>Выстрел по нему: упреждение, разброс руки, пики (ПКМ); калаш — очередями, остальное — щелчками.</summary>
        void Shoot(Actor t, string gun, WeaponState st, float d, bool sees, ref InputCmd cmd, string where)
        {
            var known = KnownPos(t);
            var dir = d > 0.01f ? (known - _me.Pos) / d : Vector2.UnitX;
            _aimDrift = _aimDrift * 0.95f + ((float)_rng.NextDouble() - 0.5f) * 0.25f;
            float lead = d / MathF.Max(40f, Items.Get(gun)?.Weapon?.BulletSpeed ?? 110f);
            cmd.Aim = sees ? t.Pos + t.Vel * lead + new Vector2(-dir.Y, dir.X) * _aimDrift * d * 0.06f : known;
            // гасит отдачу, как человек мышью: прицел против увода ствола (не полностью)
            if (sees && st.Recoil != 0)
            {
                var rel = cmd.Aim - _me.Pos;
                float ang = MathF.Atan2(rel.Y, rel.X) - st.Recoil * RecoilControl * MathF.PI / 180f;
                cmd.Aim = _me.Pos + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * rel.Length();
            }
            cmd.Peek = 1;
            if (!sees || d > st.Def.Range * 0.95f) return;
            var muzzle = _me.Pos + dir * (_me.Radius + 0.35f);
            if (!_w.BulletClear(muzzle, t.Pos)) return;   // своё укрытие на линии — не стрелять в камень
            if (st.Ammo <= 0) { if (st.ReloadLeft <= 0) { cmd.Reload = true; if (st.MagSize > 1) { Stat(where == "из укрытия" ? "перезарядка в укрытии" : "перезарядка в открытую"); } } return; }
            // автомат — короткими очередями (3–6 выстрелов, дальше короче) с паузой, чтобы ствол вернулся
            bool fire;
            if (st.Def.Auto)
            {
                if (_burstPause > 0) { _burstPause -= World.Dt; fire = false; }
                else
                {
                    fire = true;
                    if (st.Cooldown <= 0 && st.ReloadLeft <= 0 && ++_burstShots >= (d > 25 ? 3 : d > 12 ? 4 : 6) + _rng.Next(2))
                    { _burstShots = 0; _burstPause = 0.18f + (float)_rng.NextDouble() * (d > 25 ? 0.3f : 0.15f); }
                }
            }
            else fire = _trig = !_trig;
            if (fire && st.Cooldown <= 0 && st.ReloadLeft <= 0) _lastShotAt = _w.Time;
            if (fire && st.Cooldown <= 0 && st.ReloadLeft <= 0) Stat(where == "из укрытия" ? "выстрел из укрытия" : "выстрел в открытую");
            cmd.Fire = fire;
        }

        /// <summary>Укрыться негде: держать дистанцию, стрейфиться; пустой магазин — отойти боком и перезарядиться.</summary>
        void OpenFight(Actor t, string gun, WeaponState st, float d, float pref, bool sees, ref InputCmd cmd)
        {
            var known = KnownPos(t);
            var dir = d > 0.01f ? (known - _me.Pos) / d : Vector2.UnitX;
            // A-D стрейф неровным ритмом (0,35–0,9 с), как у людей: так сложнее попасть
            if ((_adT -= World.Dt) <= 0) { _adT = 0.35f + (float)_rng.NextDouble() * 0.55f; if (_rng.NextDouble() < 0.75) _adSign = -_adSign; }
            var side = new Vector2(-dir.Y, dir.X) * _adSign;
            bool reloading = st.ReloadLeft > 0 || st.Ammo == 0;
            // присел-встал: в приседе точнее и ниже голова; но не за низким укрытием — оттуда не видно и пули в него
            if ((_crT -= World.Dt) <= 0) { _crOn = !_crOn && _rng.NextDouble() < 0.6; _crT = _crOn ? 0.4f + (float)_rng.NextDouble() * 0.6f : 0.5f + (float)_rng.NextDouble() * 0.9f; }
            cmd.Crouch = _crOn && !reloading && sees && !_w.LowCoverNear(_me.Pos, known);
            var move = side * 0.7f + (reloading ? -dir * 0.6f : d > pref + 4 ? dir : d < pref - 4 ? -dir * 0.7f : Vector2.Zero);
            if (!sees && _w.Time - _foeSeenAt > 2f) move = dir;   // пропал — подойти к месту, где видел
            cmd.Move = move.LengthSquared() > 0.01f ? Avoid(Vector2.Normalize(move)) : Vector2.Zero;
            if (reloading)
            {
                cmd.Aim = known;
                if (st.ReloadLeft <= 0 && _me.Count(WeaponAmmo(gun)) > 0) { cmd.Reload = true; if (st.MagSize > 1) { Stat("перезарядка в открытую"); Say("перезарядка-открыто", "перезаряжаюсь в открытую — укрыться негде", 10); } }
                return;
            }
            Shoot(t, gun, st, d, sees, ref cmd, "в открытую");
        }

        /// <summary>Кайт: он с копьём/топором — держать 8–14 м, пятиться и стрелять; догнал вплотную — оторваться бегом.</summary>
        void Kite(Actor t, string gun, WeaponState st, float d, bool sees, ref InputCmd cmd)
        {
            var known = KnownPos(t);
            var dir = d > 0.01f ? (known - _me.Pos) / d : Vector2.UnitX;
            var side = new Vector2(-dir.Y, dir.X) * (MathF.Sin(_w.Time * 0.7f + _me.Id) > 0 ? 1 : -1);
            if (d < 5f)
            {
                // слишком близко — бегом в сторону от него, без стрельбы
                cmd.Move = Avoid(Vector2.Normalize(-dir + side * 0.5f));
                cmd.Sprint = true;
                cmd.Aim = _me.Pos + cmd.Move * 3;
                return;
            }
            var move = side * 0.5f + (d < 9 ? -dir : d > 15 ? dir * 0.8f : Vector2.Zero);
            cmd.Move = Avoid(Vector2.Normalize(move));
            if (st.Ammo == 0 && st.ReloadLeft <= 0 && _me.Count(WeaponAmmo(gun)) > 0) { cmd.Reload = true; cmd.Aim = known; return; }
            Shoot(t, gun, st, d, sees, ref cmd, "в открытую");
        }

        /// <summary>Ближний бой: копьё, топор, камень — вплотную.</summary>
        void MeleeFight(Actor t, ref InputCmd cmd)
        {
            var to = t.Pos - _me.Pos;
            float d = to.Length();
            var dir = d > 0.01f ? to / d : Vector2.UnitX;
            string melee = Has("weapon_spear") ? "weapon_spear" : Has("tool_hatchet") ? "tool_hatchet" : "tool_rock";
            Doing = $"{(Archetype == BotArchetype.Roamer ? "охотится на" : "отбивается от")} {t.Name}";
            if (!Hold(melee, ref cmd)) return;
            cmd.Aim = t.Pos;
            if (d > 1.8f) { cmd.Move = Avoid(dir); cmd.Sprint = d > 6; }
            else cmd.Fire = true;
        }
    }
}
