using System;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Простой NPC класса A (охрана РТ): патруль → бой → поиск → возврат.
    /// Управляет персонажем через тот же InputCmd, что и игрок.
    /// </summary>
    public sealed class NpcBrain
    {
        enum State { Patrol, Combat, Search }

        readonly Actor _me;
        readonly World _w;
        readonly Random _rng;
        readonly float _skill;          // 0..1: реакция и точность

        State _state = State.Patrol;
        int _targetId = -1;
        Vector2 _lastKnown;
        float _lostTime, _reaction, _strafeTimer, _strafeSign = 1, _burstTimer, _pauseTimer;
        Vector2 _patrolTarget, _aimError, _lastPos;
        float _patrolWait, _stuckTime, _avoidAngle, _avoidTimer;
        float _notice;       // 0..1: насколько NPC уверен, что видит врага. В бой — только при 1
        float _trackTime;    // сколько секунд ведёт цель: первые выстрелы неточные
        bool _provoked;      // в него стреляли — реагирует сразу

        const float SightRange = 26f, SightCone = 110f, MaxFireRange = 30f;

        public NpcBrain(Actor me, World w, int seed)
        {
            _me = me;
            _w = w;
            _rng = new Random(seed);
            _skill = 0.3f + (float)_rng.NextDouble() * 0.5f;
            _patrolTarget = me.Pos;
        }

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        public InputCmd Think()
        {
            var cmd = InputCmd.Empty;
            if (_me.Dead)
            {
                _state = State.Patrol;
                _targetId = -1;
                return cmd;
            }

            var home = _w.NpcHomes[_me.Id];
            Actor target = Perceive();
            if (target == null) _notice = MathF.Max(0, _notice - World.Dt * 0.4f);

            switch (_state)
            {
                case State.Patrol:
                    if (target != null) { Notice(target, ref cmd); break; }
                    if (HeardNoise(out var noisePos)) { _lastKnown = noisePos; _state = State.Search; _lostTime = 0; break; }
                    if (Vector2.Distance(_me.Pos, _patrolTarget) < 0.8f)
                    {
                        _patrolWait -= World.Dt;
                        if (_patrolWait <= 0)
                        {
                            _patrolTarget = home + new Vector2(R(-5, 5), R(-5, 5));
                            _patrolWait = R(1.5f, 4f);
                        }
                    }
                    else cmd.Move = Steer(_patrolTarget) * 0.5f;
                    cmd.Aim = _me.Pos + (cmd.Move.LengthSquared() > 0.01f ? Vector2.Normalize(cmd.Move) : _me.FacingDir) * 5;
                    break;

                case State.Combat:
                    target ??= _w.Find(_targetId);
                    bool visible = target != null && !target.Dead && _w.CanSee(_me, target.Pos, 50, 200);
                    if (!visible)
                    {
                        _state = State.Search;
                        _lostTime = 0;
                        break;
                    }
                    _lastKnown = target.Pos;
                    Fight(target, ref cmd);
                    break;

                case State.Search:
                    if (target != null) { Notice(target, ref cmd); break; }
                    _lostTime += World.Dt;
                    if (_lostTime > 8f || Vector2.Distance(_me.Pos, home) > 30f)
                    {
                        _state = State.Patrol;
                        _patrolTarget = home;
                        break;
                    }
                    if (Vector2.Distance(_me.Pos, _lastKnown) > 1.5f) cmd.Move = Steer(_lastKnown) * 0.8f;
                    cmd.Aim = _lastKnown;
                    if (_me.Weapon != null && _me.Weapon.Ammo < _me.Weapon.Def.Magazine / 2) cmd.Reload = true;
                    break;
            }
            return cmd;
        }

        /// <summary>Заметил кого-то: сначала присматривается, в бой — когда уверен или в него стреляли.</summary>
        void Notice(Actor target, ref InputCmd cmd)
        {
            float dist = Vector2.Distance(_me.Pos, target.Pos);
            float rate = dist < 8 ? 2.2f : dist < 16 ? 1.1f : 0.6f;
            if (target.Vel.Length() > World.WalkSpeed + 0.3f) rate *= 1.5f;   // бегущего заметить проще
            _notice += World.Dt * rate;
            if (_provoked || _notice >= 1f) { EnterCombat(target); return; }
            cmd.Aim = target.Pos;   // поворачивается и присматривается
        }

        void EnterCombat(Actor target)
        {
            if (_state != State.Combat)
            {
                _reaction = R(0.5f, 1.0f) * (1.4f - _skill);
                _trackTime = 0;
            }
            _state = State.Combat;
            _targetId = target.Id;
            _lastKnown = target.Pos;
        }

        Actor Perceive()
        {
            Actor best = null;
            float bestD = float.MaxValue;
            _provoked = false;
            foreach (var a in _w.Actors)
            {
                if (a.Dead || a.Team == Team.Npc) continue;
                bool shotAtMe = _me.LastAttacker == a.Id && _w.Time - _me.LastDamagedTime < 1.5f;
                if (shotAtMe) _provoked = true;
                if (!shotAtMe && !_w.CanSee(_me, a.Pos, SightRange, SightCone)) continue;
                if (shotAtMe && !_w.HasLineOfSight(_me.Pos, a.Pos)) { _lastKnown = a.Pos; continue; }
                float d = Vector2.Distance(_me.Pos, a.Pos);
                if (d < bestD) { bestD = d; best = a; }
            }
            if (best == null && _me.LastAttacker >= 0 && _w.Time - _me.LastDamagedTime < 0.1f && _state != State.Combat)
            {
                _state = State.Search;   // ранили из темноты — идём проверять
                _lostTime = 0;
            }
            return best;
        }

        bool HeardNoise(out Vector2 pos)
        {
            pos = default;
            foreach (var n in _w.Noises)
            {
                var src = _w.Find(n.SourceId);
                if (src == null || src.Team == Team.Npc) continue;
                if (Vector2.Distance(n.Pos, _me.Pos) < n.Radius * 0.6f) { pos = n.Pos; return true; }
            }
            return false;
        }

        void Fight(Actor target, ref InputCmd cmd)
        {
            var toT = target.Pos - _me.Pos;
            float dist = toT.Length();
            var dir = dist > 0.01f ? toT / dist : Vector2.UnitX;

            // держим дистанцию и стрейфимся
            _strafeTimer -= World.Dt;
            if (_strafeTimer <= 0) { _strafeSign = _rng.Next(2) == 0 ? -1 : 1; _strafeTimer = R(0.6f, 1.8f); }
            var side = new Vector2(-dir.Y, dir.X) * _strafeSign;
            Vector2 move = side * 0.7f;
            if (dist > 18) move += dir;
            else if (dist < 8) move -= dir * 0.8f;
            if (_me.Hp < 35 && dist < 20) move -= dir * 0.6f;
            cmd.Move = AvoidWalls(move);

            // ошибка прицела «плывёт»: тем меньше, чем выше скилл и чем дольше NPC ведёт цель
            _trackTime += World.Dt;
            float warmup = 1f + 2.5f * MathF.Exp(-_trackTime / 1.2f);
            var errTarget = new Vector2(R(-1, 1), R(-1, 1)) * (1.8f - _skill) * warmup * Math.Clamp(dist / 10f, 0.6f, 1.8f);
            _aimError = Vector2.Lerp(_aimError, errTarget, 0.08f);
            var lead = _skill > 0.6f ? target.Vel * (dist / 150f) : Vector2.Zero;
            cmd.Aim = target.Pos + lead + _aimError;

            _reaction -= World.Dt;
            if (_reaction > 0 || dist > MaxFireRange) return;

            // очереди с паузами
            if (_pauseTimer > 0) { _pauseTimer -= World.Dt; return; }
            _burstTimer += World.Dt;
            cmd.Fire = true;
            if (_burstTimer > R(0.25f, 0.5f)) { _burstTimer = 0; _pauseTimer = R(0.5f, 1.0f); }
        }

        Vector2 Steer(Vector2 to)
        {
            var d = to - _me.Pos;
            if (d.LengthSquared() < 0.01f) return Vector2.Zero;
            return AvoidWalls(Vector2.Normalize(d));
        }

        /// <summary>Грубый обход препятствий: если застрял — поворачиваем направление.</summary>
        Vector2 AvoidWalls(Vector2 move)
        {
            if (move.LengthSquared() < 0.01f) return move;
            float moved = Vector2.Distance(_me.Pos, _lastPos);
            _lastPos = _me.Pos;
            if (moved < 0.03f) _stuckTime += World.Dt; else _stuckTime = MathF.Max(0, _stuckTime - World.Dt);
            if (_stuckTime > 0.3f && _avoidTimer <= 0)
            {
                _avoidAngle = (_rng.Next(2) == 0 ? -1 : 1) * R(1.0f, 1.8f);
                _avoidTimer = R(0.5f, 1.2f);
                _stuckTime = 0;
            }
            if (_avoidTimer > 0)
            {
                _avoidTimer -= World.Dt;
                return Geo.Rotate(move, _avoidAngle);
            }
            return move;
        }
    }
}
