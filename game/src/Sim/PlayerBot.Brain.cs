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
        float _valueAt = -99;
        Actor _threat;
        float _load, _stash;
        public float Load => _load;
        public float Stash => _stash;
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
        static float WeaponPower(string id) => id is "expl_satchel" or "expl_c4" ? 1.1f : Weaponry.IsGun(id) ? Weaponry.Power(id) : 0.4f;

        /// <summary>Слышимость: огнестрел — далеко, лук — близко, взрывы — на полкарты.</summary>
        static float HearRange(string id, bool explosion) => explosion ? 260f : Weaponry.HearRange(id);

        void Perceive()
        {
            _threat = Threat() ?? RememberedFoe();   // спрятался — не значит, что ушёл
            PerceiveFoe();
            // ценность груза и ящика — раз в 0,5 с (перебор рюкзака и ящика каждый тик у 20 ботов заметен)
            if (_w.Time - _valueAt >= 0.5f) { _valueAt = _w.Time; _load = CarriedValue(); _stash = StashValue(); _surplus = SurplusValue(); }
            Aggression = Archetype == BotArchetype.Roamer ? 0.6f + 0.4f * (1 - _caution) : 0.2f + 0.3f * (1 - _caution);
            // слух: выстрелы и взрывы прошлого тика
            foreach (var e in _w.Events)
            {
                if (e.ActorId == _me.Id || e.ActorId == _foe?.Id) continue;   // выстрелы того, с кем дерусь, — не «чужой бой»
                bool expl = e.Kind == SimEventKind.Explosion;
                if (e.Kind != SimEventKind.Shot && !expl) continue;
                if (Vector2.Distance(e.Pos, _me.Pos) > HearRange(e.Text, expl)) continue;
                // одна запись на место боя: очередь выстрелов не плодит новые
                int i = _heard.FindIndex(h => Vector2.DistanceSquared(h.Pos, e.Pos) < 30 * 30);
                if (i >= 0) { var h = _heard[i]; h.Time = _w.Time; h.Pos = e.Pos; if (WeaponPower(e.Text) > WeaponPower(h.Weapon)) h.Weapon = e.Text; _heard[i] = h; }
                else _heard.Add(new Heard { Pos = e.Pos, Time = _w.Time, Weapon = e.Text, SourceId = e.ActorId, Explosion = expl });
            }
            _heard.RemoveAll(h => _w.Time - h.Time > 90);
            // этап 4: опасность рядом, чужие базы, рейд по моей базе, что лишнего несу
            if (_threat != null || _w.Time - _me.LastDamagedTime < 5) _lastDanger = _w.Time;
            foreach (var h in _heard) if (!h.Explosion && _w.Time - h.Time < 2 && Vector2.Distance(h.Pos, _me.Pos) < 80) _lastDanger = _w.Time;
            PerceiveBases();
            PerceiveDeaths();
            PerceiveRaid();
            PerceivePeople();
            PerceiveGreed();
            PerceiveCamp();
            StepStage();
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
        Actor _lastThreatDecided;
        bool _redecide;
        public string GoalName => _goal?.Name ?? "-";

        /// <summary>Вес цели по архетипу: одна логика, разные характеры.</summary>
        /// <summary>
        /// Вес цели по архетипу: одна логика и одна цель вайпа у всех (верстак 3, изучение, лут РТ, своя безопасность),
        /// архетип — только порядок и вес. Нулевых весов нет: фермер тоже ходит на РТ и дерётся, бродяга тоже строит.
        /// </summary>
        float W(string goal)
        {
            bool farmer = Archetype == BotArchetype.Farmer;
            int bench = farmer ? MyBenchLevel() : 0;
            return goal switch
            {
                "охота" => farmer ? (bench >= 2 ? 0.65f : 0.3f) : 1.2f,             // фермер дерётся, когда отстроился
                "проверить бой" => farmer ? 1f : 1.2f,
                "бродить" => farmer ? 0.2f : 1f,
                "снаряжение" => farmer ? 0.35f : 1f,
                "антирейд" => farmer ? 1.2f : 0.8f,
                "хозяйство" => farmer ? 1f : RoamerHousework(),
                "поход на РТ" => farmer ? (bench >= 1 ? 1f : 0.4f) : 1.3f,         // фермер — после мейна с верстаком, бродяга — сразу
                "рейд" => farmer ? 0.8f : 1.2f,
                "сейч" => 1f,
                _ => 1f,
            };
        }

        /// <summary>Бродяга строит минимум: кибитку — в первую очередь, потом хозяйство урывками (окно 4 мин из 10).</summary>
        float RoamerHousework()
        {
            if (MyClaim() == null || MyBox() == null) return 1.0f;
            return (_w.Time % 600f) < 240f ? 0.6f : 0.35f;
        }

        void BuildGoals()
        {
            _goals = new List<Goal>
            {
                new() { Name = "бегство", Score = FleeScore, Act = Retreat },
                new() { Name = "бой", Score = () => _threat != null && ThreatMode(_threat) > 0 ? 95 * Math.Max(W("бой"), 0.5f) : 0, Act = (ref InputCmd c) => { Combat(_threat, ref c); return true; } },
                new() { Name = "лут убитого", Score = KillLootScore, Act = KillLootAct },
                new() { Name = "сторож", Score = WatchdogScore, Act = WatchdogAct },
                new() { Name = "перехват", Score = InterceptScore, Act = InterceptAct },
                new() { Name = "кемп", Score = LootCampScore, Act = CampGoalAct },
                new() { Name = "засада у РТ", Score = CampScore, Act = CampAct },
                new() { Name = "антирейд", Score = () => AntiRaidScore() * W("антирейд"), Act = AntiRaid },
                new() { Name = "слух", Score = RumorScore, Act = RumorAct },
                new() { Name = "после смерти", Score = AfterDeathScore, Act = AfterDeathAct },
                new() { Name = "охота", Score = EncFightScore, Act = EncFightAct },
                new() { Name = "месть", Score = RevengeScore, Act = RevengeAct },
                new() { Name = "нужды", Score = () => NeedsUrgency() * 70, Act = Needs },
                new() { Name = "проверить бой", Score = () => InvestigateScore() * W("проверить бой"), Act = Investigate },
                new() { Name = "лут трупа", Score = () => CorpseNear() != null && _me.Hp > 35 ? (ArmedForGreed && _load < LoadLimit ? 74 : 60) * W("лут трупа") : 0, Act = LootCorpse },   // лут — главное: каждый труп обыскать
                new() { Name = "рейд", Score = RaidScore, Act = RaidAct },
                new() { Name = "поход на РТ", Score = RtScore, Act = RtRun },
                new() { Name = "сейч", Score = StashScore, Act = StashAct },
                new() { Name = "разведка", Score = ScoutScore, Act = Scout },
                new() { Name = "снаряжение", Score = () => !GearMissing() ? 0 : !Has("weapon_spear") ? 55 : 50 * W("снаряжение"), Act = GearUp },
                new() { Name = "хозяйство", Score = () => 40 * W("хозяйство"), Act = (ref InputCmd c) => { Plan(ref c); return true; } },
                new() { Name = "бродить", Score = () => 20 * W("бродить"), Act = RoamMove },
            };
        }

        /// <summary>Директор: лучшая цель; текущей — надбавка «упрямства» (×1,25), чтобы не метаться.</summary>
        void Decide(ref InputCmd cmd)
        {
            if (_goals == null) BuildGoals();
            CountHealth();
            // цели пересчитываются раз в 0,2 с (вразнобой у разных ботов), действие — каждый тик;
            // сразу — если по мне попали или появилась/сменилась угроза
            bool urgent = _w.Time - _me.LastDamagedTime < 0.05f || _threat != _lastThreatDecided || _redecide;
            _redecide = false;
            if (_goal != null && !urgent && (_w.Tick + _me.Id) % 6 != 0)
            {
                long tk = SimPerf.Begin();
                if (!_goal.Act(ref cmd)) { Plan(ref cmd); _redecide = true; }   // цель кончилась — пересчитать в следующий тик
                SimPerf.End("действие: " + _goal.Name, tk);
                return;
            }
            _lastThreatDecided = _threat;
            Goal best = null;
            float bestScore = 0;
            foreach (var g in _goals)
            {
                long tq = SimPerf.Begin();
                float s = g.Score();
                SimPerf.End("очки: " + g.Name, tq);
                if (g == _goal) s *= 1.25f;
                if (s > bestScore) { bestScore = s; best = g; }
            }
            best ??= _goals[^2];
            if (best != _goal)
            {
                bool fresh = _w.Time - _lastSwitchLog.GetValueOrDefault(best.Name, -999) > 30;   // повтор той же смены — не чаще раза в 30 с
                if (fresh) _lastSwitchLog[best.Name] = _w.Time;
                if (fresh && _goal != null && best.Name is "бегство" or "бой" or "антирейд" or "проверить бой" or "охота" or "рейд" or "поход на РТ" or "разведка" or "слух" or "месть" or "перехват" or "кемп" or "лут убитого")
                    Thought($"{_goal.Name} → {best.Name}{(best.Name == "бой" || best.Name == "бегство" ? $": {_threat?.Name ?? "угроза"}, у него {Items.Name(_threat?.Held?.Id ?? "tool_rock")}" : "")}");
                _goal = best;
            }
            long ta = SimPerf.Begin();
            if (!best.Act(ref cmd)) Plan(ref cmd);
            SimPerf.End("действие: " + best.Name, ta);
        }

        // ---------- бой и бегство ----------

        /// <summary>Драться или отходить: причина отхода (HP, патроны, его оружие, проигранный размен) — в _loseWhy.</summary>
        bool CanFight(Actor t)
        {
            _loseWhy = LosingReason(t);
            return _loseWhy == null;
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
            if (_warming || (_me.Cold >= 60 && _me.FeltTemp < Clothing.Comfort && _w.Time >= _warmNoTry)) u = MathF.Max(u, 0.75f);   // замёрз (D47)
            if (NeedClothesCloth) u = MathF.Max(u, 0.5f);                                                                              // мёрзнет голым — конопля на рубаху
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
                float score = mine < 0.35f ? 0 : 75 * MathF.Min(1f, Aggression + 0.2f * Greed) * near * (1 - risk) * (1 - 0.7f * burden) * hp * (_w.Time - h.Time < 45 ? 1 : 0.5f);
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

        // ---------- роумер: охота, лут трупов, снаряжение, бродить ----------


        readonly Dictionary<LootBag, float> _badLoot = new();
        LootBag _lootBag;
        float _lootSince;

        LootBag CorpseNear()
        {
            LootBag bag = null;
            float bd = ArmedForGreed ? 55 : 35;
            foreach (var b in _w.Bags)
            {
                if (b.Empty || b.IsCrate || MyDrop(b) || (_badLoot.TryGetValue(b, out float until) && _w.Time < until)) continue;
                if (_w.FloorAt(b.Pos) is Structure f && f.OwnerId == _me.Id && !InsideBase && RecoverPending()) continue;   // за своей запертой дверью без ключа
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
            // не дойти за 30 с (за стеной, в чужом доме) — на 3 минуты забыть про эту сумку
            if (bag != _lootBag) { _lootBag = bag; _lootSince = _w.Time; }
            else if (_w.Time - _lootSince > 30 || NoReach(bag.Pos)) { _badLoot[bag] = _w.Time + 180; _lootBag = null; return false; }
            Use(bag, bag.Pos, ref cmd);
            return true;
        }

        /// <summary>Без копья на РТ и к людям не ходят (пользователь: «хотя бы с копьём минимум, с луком шансов больше»).</summary>
        bool GearMissing()
        {
            if (_w.Time - _gearAt < 1f) return _gearMiss;   // перебор конопли на карте — раз в секунду
            _gearAt = _w.Time;
            return _gearMiss = GearMissingNow();
        }
        float _gearAt = -99; bool _gearMiss;
        bool GearMissingNow() => Has("tool_hatchet") && (!Has("weapon_spear") || (!Has("weapon_bow") && (_me.Count("cloth") >= 50 || NearestPickup(PickupKind.Hemp) != null)) || (Has("weapon_bow") && _me.Count("arrow") < 12));

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
