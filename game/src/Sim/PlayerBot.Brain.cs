using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Мозг бота v2, каркас (docs/design/bot-brain.md): восприятие → доска знаний → директор целей → инструменты.
    /// Цели сами считают, насколько они нужны сейчас (0..100); директор берёт лучшую с «упрямством» к текущей,
    /// чтобы не метаться. Решения с причинами пишутся в журнал мыслей (видно в наблюдателе и в логе прогона).
    /// Инструменты — нынешние действия бота: ходьба (A*), добыча, крафт, стройка, бой, лут.
    /// </summary>
    public sealed partial class PlayerBot
    {
        // ---------- доска знаний ----------

        /// <summary>Услышанный бой: где, когда, чем стреляли (по звуку), кто (для памяти).</summary>
        public struct Heard
        {
            public Vector2 Pos;
            public float Time;
            public string Weapon;
            public int SourceId;
            public bool Explosion;
            public bool Judged;     // решение по нему уже принято (и записано в мысли)
        }

        readonly List<Heard> _heard = new();
        Actor _threat;
        float _load, _stash;
        public float Aggression;   // 0..1: тяга к драке (по архетипу и характеру)

        /// <summary>Ценность того, что несу (в «ломах»).</summary>
        float CarriedValue()
        {
            float v = 0;
            foreach (var s in _me.Pack) if (s != null) v += Items.Value(s.Id) * s.Count;
            foreach (var s in _me.Belt) if (s != null && s.Def?.Weapon == null && s.Def?.Deploy == DeployKind.None) v += Items.Value(s.Id) * s.Count;
            return v;
        }

        /// <summary>Ценность ящика дома.</summary>
        float StashValue()
        {
            var box = MyBox();
            if (box?.Storage == null) return 0;
            float v = 0;
            foreach (var s in box.Storage.Items) v += Items.Value(s.Id) * s.Count;
            return v;
        }

        /// <summary>Насколько опасно оружие на слух.</summary>
        static float WeaponPower(string id) => id switch
        {
            "weapon_ak" => 1f,
            "weapon_pipe" => 0.75f,
            "weapon_bow" => 0.6f,
            "expl_satchel" or "expl_c4" => 1.1f,
            _ => 0.4f,
        };

        /// <summary>Слышимость: огнестрел — далеко, лук — близко, взрывы — на полкарты.</summary>
        static float HearRange(string id, bool explosion) => explosion ? 260f : id switch { "weapon_ak" => 180f, "weapon_pipe" => 120f, "weapon_bow" => 35f, _ => 25f };

        void Perceive()
        {
            _threat = Threat();
            _load = CarriedValue();
            _stash = StashValue();
            Aggression = Archetype == BotArchetype.Roamer ? 0.6f + 0.4f * (1 - _caution) : 0.2f + 0.3f * (1 - _caution);
            // слух: выстрелы и взрывы прошлого тика
            foreach (var e in _w.Events)
            {
                if (e.ActorId == _me.Id) continue;
                bool expl = e.Kind == SimEventKind.Explosion;
                if (e.Kind != SimEventKind.Shot && !expl) continue;
                if (Vector2.Distance(e.Pos, _me.Pos) > HearRange(e.Text, expl)) continue;
                // одна запись на место боя: очередь выстрелов не плодит новые
                int i = _heard.FindIndex(h => Vector2.DistanceSquared(h.Pos, e.Pos) < 30 * 30);
                if (i >= 0) { var h = _heard[i]; h.Time = _w.Time; h.Pos = e.Pos; if (WeaponPower(e.Text) > WeaponPower(h.Weapon)) h.Weapon = e.Text; _heard[i] = h; }
                else _heard.Add(new Heard { Pos = e.Pos, Time = _w.Time, Weapon = e.Text, SourceId = e.ActorId, Explosion = expl });
            }
            _heard.RemoveAll(h => _w.Time - h.Time > 90);
        }

        // ---------- журнал мыслей ----------

        public readonly List<string> Thoughts = new();
        public int ThoughtTotal;   // всего мыслей (журнал хранит последние 40)
        public string LastThought => Thoughts.Count > 0 ? Thoughts[^1] : "";

        void Thought(string text)
        {
            int m = (int)(_w.Time / 60), s = (int)(_w.Time % 60);
            Thoughts.Add($"[{m:00}:{s:00}] {text}");
            ThoughtTotal++;
            if (Thoughts.Count > 40) Thoughts.RemoveAt(0);
        }

        static string Dir(Vector2 from, Vector2 to)
        {
            var d = to - from;
            float a = MathF.Atan2(-d.Y, d.X) * 180 / MathF.PI;
            string[] names = { "восток", "северо-восток", "север", "северо-запад", "запад", "юго-запад", "юг", "юго-восток" };
            return names[(int)MathF.Round((a + 360) % 360 / 45) % 8];
        }

        // ---------- цели ----------

        delegate bool GoalAct(ref InputCmd cmd);

        sealed class Goal
        {
            public string Name;
            public Func<float> Score;
            public GoalAct Act;
        }

        List<Goal> _goals;
        readonly Dictionary<string, float> _lastSwitchLog = new();
        Goal _goal;
        public string GoalName => _goal?.Name ?? "-";

        /// <summary>Вес цели по архетипу: одна логика, разные характеры.</summary>
        float W(string goal) => (Archetype, goal) switch
        {
            (BotArchetype.Roamer, "охота") => 1.2f,
            (BotArchetype.Roamer, "проверить бой") => 1.2f,
            (BotArchetype.Roamer, "хозяйство") => 0.3f,
            (BotArchetype.Farmer, "охота") => 0f,
            (BotArchetype.Farmer, "бродить") => 0f,
            (BotArchetype.Farmer, "снаряжение") => 0f,
            (BotArchetype.Farmer, "антирейд") => 1.2f,
            (BotArchetype.Roamer, "антирейд") => 0f,
            _ => 1f,
        };

        void BuildGoals()
        {
            _goals = new List<Goal>
            {
                new() { Name = "бегство", Score = () => _fleeTime > 0 || (_threat != null && !CanFight(_threat)) ? 100 : 0, Act = Flee },
                new() { Name = "бой", Score = () => _threat != null && CanFight(_threat) ? 95 * Math.Max(W("бой"), 0.5f) : 0, Act = (ref InputCmd c) => { Hunt(_threat, ref c); return true; } },
                new() { Name = "антирейд", Score = () => AntiRaidScore() * W("антирейд"), Act = AntiRaid },
                new() { Name = "охота", Score = () => (W("охота") > 0 && PickPrey() != null ? 85 : 0) * W("охота"), Act = (ref InputCmd c) => { if (_prey == null) return false; Hunt(_prey, ref c); return true; } },
                new() { Name = "нужды", Score = () => NeedsUrgency() * 70, Act = Needs },
                new() { Name = "проверить бой", Score = () => InvestigateScore() * W("проверить бой"), Act = Investigate },
                new() { Name = "лут трупа", Score = () => CorpseNear() != null && _me.Hp > 35 ? 60 * W("лут трупа") : 0, Act = LootCorpse },
                new() { Name = "снаряжение", Score = () => GearMissing() ? 50 * W("снаряжение") : 0, Act = GearUp },
                new() { Name = "хозяйство", Score = () => 40 * W("хозяйство"), Act = (ref InputCmd c) => { Plan(ref c); return true; } },
                new() { Name = "бродить", Score = () => 20 * W("бродить"), Act = RoamMove },
            };
        }

        /// <summary>Директор: лучшая цель; текущей — надбавка «упрямства» (×1,25), чтобы не метаться.</summary>
        void Decide(ref InputCmd cmd)
        {
            if (_goals == null) BuildGoals();
            Goal best = null;
            float bestScore = 0;
            foreach (var g in _goals)
            {
                float s = g.Score();
                if (g == _goal) s *= 1.25f;
                if (s > bestScore) { bestScore = s; best = g; }
            }
            best ??= _goals[^2];
            if (best != _goal)
            {
                bool fresh = _w.Time - _lastSwitchLog.GetValueOrDefault(best.Name, -999) > 30;   // повтор той же смены — не чаще раза в 30 с
                if (fresh) _lastSwitchLog[best.Name] = _w.Time;
                if (fresh && _goal != null && best.Name is "бегство" or "бой" or "антирейд" or "проверить бой" or "охота")
                    Thought($"{_goal.Name} → {best.Name}{(best.Name == "бой" || best.Name == "бегство" ? $": {_threat?.Name ?? "угроза"}, у него {Items.Name(_threat?.Held?.Id ?? "tool_rock")}" : "")}");
                _goal = best;
            }
            if (!best.Act(ref cmd)) Plan(ref cmd);
        }

        // ---------- бой и бегство ----------

        /// <summary>Драться или бежать: дальнобойное, не слабее, не ранен; роумер смелее.</summary>
        bool CanFight(Actor t)
        {
            float d = Vector2.Distance(t.Pos, _me.Pos);
            bool hitMe = _me.LastAttacker == t.Id && _w.Time - _me.LastDamagedTime < 4f;
            float edge = MyPower - KitPower(t) + (Archetype == BotArchetype.Roamer ? 0.1f : 0) + (_me.Hp - t.Hp) / 400f;
            if (_me.Hp < 40) return false;
            if (Ranged() != null) return edge >= 0 && d < 35;
            return hitMe && edge > 0.1f && d < 6;   // с копьём — только если сам полез и он слабее
        }

        bool Flee(ref InputCmd cmd)
        {
            if (_threat != null) _fleeTime = 3f + 4f * _caution;
            _fleeTime -= World.Dt;
            Doing = "убегает";
            var from = _threat?.Pos ?? _me.Pos - _me.FacingDir;
            var away = _me.Pos - from;
            if (away.LengthSquared() < 0.01f) away = Vector2.UnitX;
            cmd.Move = Avoid(Vector2.Normalize(away));
            cmd.Sprint = true;
            cmd.Aim = _me.Pos + Vector2.Normalize(away) * 3;
            if (_me.Hp < 60) cmd.Heal = true;
            return true;
        }

        // ---------- нужды ----------

        float NeedsUrgency()
        {
            bool hurt = _me.Hp < _me.MaxHp - 5;
            float u = 0;
            if (_me.Hp < 60 && _me.Count("bandage") == 0) u = MathF.Max(u, 0.9f);
            if (_me.Water < (hurt ? 50 : 30)) u = MathF.Max(u, _me.Water < 15 ? 1f : 0.8f);
            if (_me.Food < (hurt ? 45 : 30) && _me.Count("berries") == 0) u = MathF.Max(u, _me.Food < 15 ? 1f : 0.7f);
            if (_me.Hp < 70 && _me.Count("bandage") > 0) u = MathF.Max(u, 0.6f);
            return u;
        }

        // ---------- слух: идти на выстрелы или нет ----------

        Heard? _target;

        /// <summary>
        /// Стоит ли идти на бой: тяга к драке × близость × (1 − риск) × (1 − груз) × здоровье.
        /// Риск — услышанное оружие против моего. Решение по каждому новому бою пишется в мысли с причиной.
        /// </summary>
        float InvestigateScore()
        {
            float best = 0;
            Heard? pick = null;
            for (int i = 0; i < _heard.Count; i++)
            {
                var h = _heard[i];
                if (h.Explosion && NearMyBase(h.Pos)) continue;   // взрывы у дома — это антирейд
                float d = Vector2.Distance(h.Pos, _me.Pos);
                float power = WeaponPower(h.Weapon), mine = MyPower;
                float risk = Math.Clamp((power - mine + 0.25f) / 0.9f, 0, 1);
                float near = Math.Clamp(1 - d / 220f, 0, 1);
                float burden = Math.Clamp((_load + (Archetype == BotArchetype.Farmer ? _stash * 0.3f : 0)) / 600f, 0, 1);
                float hp = _me.Hp / _me.MaxHp;
                float score = mine < 0.35f ? 0 : 75 * Aggression * near * (1 - risk) * (1 - 0.7f * burden) * hp * (_w.Time - h.Time < 45 ? 1 : 0.5f);
                if (!h.Judged)
                {
                    h.Judged = true;
                    _heard[i] = h;
                    string what = h.Explosion ? $"взрыв ({Items.Name(h.Weapon)})" : $"выстрелы ({Items.Name(h.Weapon)})";
                    string why = mine < 0.35f ? "нечем драться"
                        : risk > 0.7f ? $"у них оружие сильнее ({Items.Name(h.Weapon)} против {Items.Name(Ranged() ?? "weapon_spear")})"
                        : burden > 0.6f ? $"несу ценное (≈{_load:0} лома) — не рискую"
                        : hp < 0.5f ? "ранен"
                        : near < 0.3f ? "далеко"
                        : score > 25 ? "можно поживиться" : "не стоит";
                    Thought($"слышу {what} в {d:0} м на {Dir(_me.Pos, h.Pos)} → {(score > 25 ? "иду" : "не иду")}: {why}");
                }
                if (score > best) { best = score; pick = h; }
            }
            _target = pick;
            return best > 25 ? best : 0;
        }

        bool Investigate(ref InputCmd cmd)
        {
            if (_target is not Heard h) return false;
            float d = Vector2.Distance(h.Pos, _me.Pos);
            Doing = $"идёт на звук боя ({Items.Name(h.Weapon)}, {d:0} м)";
            var gun = Ranged();
            if (gun != null) Hold(gun, ref cmd);
            if (d < 25) cmd.Crouch = true;   // последние метры — тихо
            if (MoveTo(h.Pos, 4f, ref cmd))
            {
                // пришёл, никого — бой кончился: забыть это место
                _heard.RemoveAll(x => Vector2.DistanceSquared(x.Pos, h.Pos) < 30 * 30);
                Thought("на месте боя пусто — ухожу");
            }
            return true;
        }

        // ---------- антирейд ----------

        bool NearMyBase(Vector2 p) => MyClaim() is Deployable c && Vector2.Distance(c.Pos, p) < 40;

        float AntiRaidScore()
        {
            var claim = MyClaim();
            if (claim == null || _w.Time - claim.RaidedAt > 90) { _raidAlarm = false; return 0; }
            var raider = _w.Find(claim.RaiderId);
            float worth = Math.Clamp(_stash / 300f, 0.3f, 1f);
            bool stronger = raider == null || raider.Dead || MyPower >= KitPower(raider);
            if (!_raidAlarm)
            {
                _raidAlarm = true;
                Thought($"мой дом рейдят{(raider != null ? $" ({raider.Name}, {Items.Name(raider.Held?.Id ?? "tool_rock")})" : "")} → " +
                        (stronger ? $"иду защищать: в ящике ≈{_stash:0} лома" : "пережидаю в стороне: он сильнее"));
            }
            return 90 * worth;
        }
        bool _raidAlarm;

        bool AntiRaid(ref InputCmd cmd)
        {
            var claim = MyClaim();
            if (claim == null) return false;
            var raider = _w.Find(claim.RaiderId);
            if (raider != null && !raider.Dead && KitPower(raider) > MyPower)
            {
                Doing = "дом рейдят — пережидает в стороне";
                var away = _me.Pos - raider.Pos;
                if (away.LengthSquared() < 0.01f) away = Vector2.UnitX;
                if (Vector2.Distance(_me.Pos, raider.Pos) < 55) { cmd.Move = Avoid(Vector2.Normalize(away)); cmd.Sprint = true; cmd.Aim = _me.Pos + Vector2.Normalize(away) * 3; }
                return true;
            }
            Doing = "бежит защищать дом";
            var gun = Ranged();
            if (gun != null) Hold(gun, ref cmd);
            GoTo(claim.Pos + new Vector2(0.6f, 0.6f), 1.5f, ref cmd);
            return true;
        }

        // ---------- роумер: охота, лут трупов, снаряжение, бродить ----------

        Actor PickPrey()
        {
            int load = _me.Count("wood") + _me.Count("stone") + _me.Count("metal_fragments") * 3 + _me.Count("scrap") * 5;
            bool loaded = load > 900;
            if (_me.Hp < 35) { _prey = null; return null; }
            if (_prey != null && !_prey.Dead && (_preyTimer -= World.Dt) > 0) return _prey;
            _prey = null;
            float best = float.MaxValue;
            foreach (var a in _w.Actors)
            {
                if (a == _me || a.Dead || a.Spectator || a.Team == Team.Npc) continue;
                float d = Vector2.Distance(a.Pos, _me.Pos);
                bool hitMe = _me.LastAttacker == a.Id && _w.Time - _me.LastDamagedTime < 5f;
                if (!hitMe && (loaded || d > 45 || MyPower < 0.4f || KitPower(a) > MyPower + 0.05f)) continue;   // без копья/лука первым не лезет
                if (!_w.HasLineOfSight(_me.Pos, a.Pos)) continue;
                if (d < best) { best = d; _prey = a; }
            }
            _preyTimer = 2f;
            if (_prey != null && _prey != _lastPrey) { Thought($"вижу {_prey.Name} ({Items.Name(_prey.Held?.Id ?? "tool_rock")}) — слабее меня, охочусь"); _lastPrey = _prey; }
            return _prey;
        }
        Actor _lastPrey;

        LootBag CorpseNear()
        {
            LootBag bag = null;
            float bd = 35;
            foreach (var b in _w.Bags)
            {
                if (b.Empty || b.IsCrate) continue;
                float d = Vector2.Distance(b.Pos, _me.Pos);
                if (d < bd) { bd = d; bag = b; }
            }
            return bag;
        }

        bool LootCorpse(ref InputCmd cmd)
        {
            var bag = CorpseNear();
            if (bag == null) return false;
            Doing = bag.Corpse ? "лутает труп" : "подбирает вещи";
            if (MoveTo(bag.Pos, 1.0f, ref cmd)) cmd.Interact = Click2();
            return true;
        }

        bool GearMissing() => Has("tool_hatchet") && (!Has("weapon_spear") || (!Has("weapon_bow") && (_me.Count("cloth") >= 50 || NearestPickup(PickupKind.Hemp) != null)) || (Has("weapon_bow") && _me.Count("arrow") < 12));

        bool GearUp(ref InputCmd cmd)
        {
            if (!Has("weapon_spear")) { if (!Craft("weapon_spear", ref cmd)) Need("wood", 150, ref cmd); return true; }
            if (!Has("weapon_bow"))
            {
                if (_me.Count("cloth") < 50) { Doing = "собирает коноплю"; UsePickup(PickupKind.Hemp, ref cmd); return true; }
                if (!Craft("weapon_bow", ref cmd)) Need("wood", 100, ref cmd);
                return true;
            }
            if (Craft("arrow", ref cmd)) return true;
            if (_me.Count("wood") < 25) { Need("wood", 150, ref cmd); return true; }
            Need("stone", 80, ref cmd);
            return true;
        }

        bool RoamMove(ref InputCmd cmd)
        {
            if (!Has("tool_hatchet")) return false;   // без инструментов — общий план (камень → топор)
            float rd = Vector2.Distance(_roamTo, _me.Pos);
            if (rd < _roamBest - 0.5f) { _roamBest = rd; _roamStuck = 0; }
            else if ((_roamStuck += World.Dt) > 8f) _roamTo = default;   // упёрся — другая цель
            if (_roamTo == default || Vector2.Distance(_roamTo, _me.Pos) < 6)
            {
                _roamBest = float.MaxValue; _roamStuck = 0;
                // чередует: окрестности РТ и пляжи, где просыпаются голые
                if (_rng.NextDouble() < 0.5 && _w.PlayerSpawns.Count > 0)
                    _roamTo = _w.PlayerSpawns[_rng.Next(_w.PlayerSpawns.Count)] * 0.97f;
                else
                {
                    var m = _w.Monuments[_rng.Next(_w.Monuments.Count)];
                    var ang = (float)(_rng.NextDouble() * Math.PI * 2);
                    _roamTo = m.Center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * (70 + (m.Max - m.Min).Length() * 0.5f);   // дальше от охраны
                }
                if (!_w.Terrain.IsLand(_roamTo)) _roamTo *= 0.85f;
            }
            Doing = "бродит";
            var gun = Ranged();
            Hold(gun ?? (Has("weapon_spear") ? "weapon_spear" : "tool_hatchet"), ref cmd);
            MoveTo(_roamTo, 4, ref cmd);
            return true;
        }
    }
}
