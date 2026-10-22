using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Жадность и злопамятность (D45; пользователь: «лут — буквально самое главное. Убить, залутать… кемпишь, убиваешь, лутаешь»;
    /// «нужно злопамятность прям жёстко»). Всё честно — только увиденное, услышанное и запомненное:
    /// - «полный рюкзак»: видел, как он вскрывал ящики/обирал труп; пришёл на РТ — ящики пустые, а оттуда уходил он;
    ///   по этому — оценка его груза (сколько лома несёт), по ней — нападаю, перехватываю на отходе, караулю у РТ;
    /// - убил — залутай: своё убийство обыскиваю первым делом (с проверкой «не стреляют ли»), чужой, обобравший мой труп, — обида;
    /// - обиды на весь вайп: убил меня, обобрал моё убийство, рейдил, напал первым — копятся и растят желание мстить:
    ///   ищу там, где видел, у его базы (если знаю), у РТ, где его встречал; отомстил — счёт помнится, повтор — снова.
    /// </summary>
    public sealed partial class PlayerBot
    {
        // ---------- оценка чужого груза ----------

        sealed class Carrier
        {
            public float Est, At, SeenAt = -999;
            public Vector2 Pos, Vel;
            public string Why = "";
            public string Rt;          // где грабил
        }

        readonly Dictionary<int, Carrier> _carriers = new();

        /// <summary>Жадность к чужому луту 0..1: у всех высокая (как в Расте), у бродяг и жадных — выше.</summary>
        float Greed => Math.Clamp(0.45f + 0.35f * _greed + 0.25f * Aggression, 0, 1);

        void NoteCarrier(Actor a, float value, string why, string rt = null)
        {
            if (!IsPerson(a) || value <= 0) return;
            if (!_carriers.TryGetValue(a.Id, out var c)) _carriers[a.Id] = c = new Carrier();
            bool first = c.Est < 60 && c.Est + value >= 60;
            c.Est = MathF.Min(c.Est + value, 900); c.At = _w.Time; c.Pos = a.Pos; c.Vel = a.Vel; c.SeenAt = _w.Time; c.Why = why;
            if (rt != null) c.Rt = rt;
            if (first) { Thought($"у {a.Name} рюкзак с лутом (≈{c.Est:0} лома): {why}"); LStat("жадность: заметил груз"); }
        }

        Carrier CarrierOf(Actor a) => a != null && _carriers.TryGetValue(a.Id, out var c) && _w.Time - c.At < 600 ? c : null;

        // мои убийства: обыскать труп
        readonly List<(Vector2 pos, float at, int victim, string name)> _kills = new();

        /// <summary>Из восприятия: кто что лутал у меня на глазах, кого я убил, кто обобрал моё убийство.</summary>
        void PerceiveGreed()
        {
            float view = _w.ViewRange(_me);
            foreach (var e in _w.Events)
            {
                if (e.Kind == SimEventKind.Death && e.OtherId == _me.Id && e.ActorId != _me.Id && _w.Find(e.ActorId) is Actor victim)
                {
                    _kills.Add((e.Pos, _w.Time, victim.Id, victim.Name));
                    if (_kills.Count > 6) _kills.RemoveAt(0);
                    if (IsPerson(victim)) { LStat("убил игрока"); Thought($"убил {victim.Name} — теперь залутать"); if (_w.Time - _campHitAt < 45) { LStat("кемп: убил из засады"); _w.Milestone(_me, $"убил {victim.Name} из засады"); } }
                }
                if (e.Kind != SimEventKind.Loot || e.ActorId == _me.Id || _w.Find(e.ActorId) is not Actor looter || !IsPerson(looter)) continue;
                float dist = Vector2.Distance(e.Pos, _me.Pos);
                bool seen = dist < 8f || (dist < view && _w.HasLineOfSight(_me.Pos, e.Pos));
                // обобрал моё убийство
                foreach (var k in _kills)
                    if (_w.Time - k.at < 240 && Vector2.Distance(k.pos, e.Pos) < 3f && k.victim != looter.Id && (seen || dist < 25f))
                    {
                        Offend(looter, "LootedKill", $"обобрал моё убийство ({k.name})");
                        NoteCarrier(looter, 90, $"обобрал {k.name}, которого убил я");
                    }
                if (!seen) continue;
                var bag = _w.Bags.Find(b => Vector2.DistanceSquared(b.Pos, e.Pos) < 0.04f);
                float v = bag == null ? 70 : bag.IsCrate ? CrateGuess(bag) : bag.Corpse ? 80 : 25;   // сумка исчезла целиком — скорее всего труп
                var m = _w.MonumentAt(e.Pos);
                NoteCarrier(looter, v, m != null ? $"видел, как он лутал на «{m.Name}»" : "видел, как он обирал сумку", m?.Name);
            }
            // кого вижу — обновить, где он и куда идёт (для перехвата)
            if (_carriers.Count > 0)
                foreach (var (id, c) in _carriers)
                    if (_enc.TryGetValue(id, out var en) && _w.Time - en.LastSeen < 0.5f) { c.Pos = en.SeenPos; c.Vel = en.A.Vel; c.SeenAt = _w.Time; }
        }

        // ---------- цель «лут убитого» ----------

        float _killLootAt = -99;

        (Vector2 pos, float at, int victim, string name)? KillToLoot()
        {
            for (int i = _kills.Count - 1; i >= 0; i--)
            {
                var k = _kills[i];
                if (_w.Time - k.at > 150) continue;
                if (KillBag(k.pos) != null || Vector2.Distance(k.pos, _me.Pos) > 6f) return k;
            }
            return null;
        }

        LootBag KillBag(Vector2 at)
        {
            foreach (var b in _w.Bags) if (b.Corpse && !b.Empty && Vector2.DistanceSquared(b.Pos, at) < 2.5f * 2.5f) return b;
            return null;
        }

        float KillLootScore()
        {
            if (_me.Hp < 30 || KillToLoot() == null) return 0;
            return 86;   // ниже боя и бегства (безопасность — первой), выше всего остального: убил — залутай
        }

        bool KillLootAct(ref InputCmd cmd)
        {
            if (KillToLoot() is not var (pos, at, victim, name)) return false;
            var bag = KillBag(pos);
            Doing = $"лутает убитого {name}";
            if (bag == null)
            {
                // сумки нет: подойти и посмотреть; пусто — значит, обобрали (обидчик — кого видел рядом)
                if (Vector2.Distance(_me.Pos, pos) > 5f || !_w.HasLineOfSight(_me.Pos, pos)) { MoveTo(pos, 3f, ref cmd); return true; }
                Actor near = null;
                foreach (var en in _enc.Values) if (en.A.Id != victim && _w.Time - en.LastSeen < 20 && Vector2.Distance(en.SeenPos, pos) < 12) { near = en.A; break; }
                if (near != null) Offend(near, "LootedKill", $"обобрал моё убийство ({name})");
                Thought($"труп {name} уже пуст{(near != null ? $" — его обобрал {near.Name}" : "")}");
                LStat("лут убитого: опоздал");
                _kills.RemoveAll(k => k.at == at);
                return true;
            }
            if (Ranged() is string g && Vector2.Distance(_me.Pos, bag.Pos) > 3f) Hold(g, ref cmd);
            if (!Reach(bag.Pos, ref cmd)) { if (NoReach(bag.Pos)) _kills.RemoveAll(k => k.at == at); return true; }
            if (MakeRoom(80, ref cmd)) return true;
            if (_w.Time - _killLootAt < 0.45f) return true;
            _killLootAt = _w.Time;
            float before = CarriedValue();
            int n = bag.Items.Count;
            _w.BotUse(_me, bag);
            float got = CarriedValue() - before;
            if (bag.Empty || bag.Items.Count == n)
            {
                _kills.RemoveAll(k => k.at == at);
                Thought($"залутал убитого {name}{(got > 0 ? $": ≈{got:0} лома" : "")}{(!bag.Empty ? " (всё не влезло)" : "")}");
                LStat("лут убитого: залутал");
                if (got > 0) _w.Milestone(_me, $"убил и залутал {name}");
                if (_rng.NextDouble() < 0.5) TryCamp("corpse", pos, _w.MonumentAt(pos), $"труп {name}");   // к трупу придут обирать
            }
            return true;
        }

        // ---------- перехват того, кто несёт лут ----------

        int _icptId;
        Carrier _icpt;
        float _icptStart = -99;
        readonly Dictionary<int, float> _icptCooldown = new();

        bool ArmedForGreed => Ranged() != null || Has("weapon_spear");

        float InterceptScore()
        {
            if (_icpt != null && _w.Time - _icptStart < 150) return 74;
            _icpt = null;
            if (!ArmedForGreed || _me.Hp < 60 || _load > LoadLimit || _raid != null || _rtPhase == RtPhase.Prep) return 0;
            float best = 0;
            foreach (var (id, c) in _carriers)
            {
                if (c.Est < 50 || _w.Time - c.SeenAt > 90 || _icptCooldown.GetValueOrDefault(id) > _w.Time) continue;
                var a = _w.Find(id);
                if (a == null || a.Dead || !IsPerson(a)) continue;
                float kit = _enc.TryGetValue(id, out var en) ? KitPower(en.A) : 0.5f;
                if (kit > MyPower + 0.15f + 0.15f * Greed) continue;   // не по силам — не лезу
                if (_enc.TryGetValue(id, out var en2) && _w.Time - en2.LastSeen < 1f) continue;   // на виду — решает встреча
                float s = (50 + MathF.Min(30, c.Est / 15)) * Greed * MathF.Max(0.4f, 1 - Vector2.Distance(c.Pos, _me.Pos) / 250f);
                if (s > best) { best = s; _icptId = id; }
            }
            if (best < 36) return 0;
            return best + 30;   // ≈66–90: выше похода на РТ и хозяйства
        }

        bool InterceptAct(ref InputCmd cmd)
        {
            if (_icpt == null)
            {
                if (!_carriers.TryGetValue(_icptId, out var c)) return false;
                _icpt = c; _icptStart = _w.Time;
                var who = _w.Find(_icptId);
                Thought($"перехват: {who?.Name} несёт ≈{c.Est:0} лома ({c.Why}) — иду наперерез");
                LStat("жадность: перехват");
            }
            var a = _w.Find(_icptId);
            if (a == null || a.Dead) { EndIntercept("его уже нет"); return false; }
            // на виду — встреча («нападаю: у него рюкзак»); иначе — туда, где он будет: последнее место + его ход, или к его базе
            var c2 = _icpt;
            float since = _w.Time - c2.SeenAt;
            var dest = c2.Pos + (c2.Vel.LengthSquared() > 1 ? Vector2.Normalize(c2.Vel) * MathF.Min(45, since * 3.5f) : Vector2.Zero);
            foreach (var kb in _bases.Values) if (kb.OwnerId == _icptId) { dest = Vector2.Lerp(dest, kb.Pos, since > 20 ? 0.6f : 0.3f); break; }
            Doing = $"перехватывает {a.Name} (≈{c2.Est:0} лома)";
            if (Ranged() is string g) Hold(g, ref cmd); else if (Has("weapon_spear")) Hold("weapon_spear", ref cmd);
            if (since > 75) { EndIntercept("упустил — след простыл"); return false; }
            dest = OpenGroundNear(dest);
            if ((MoveTo(dest, 5f, ref cmd) || NoReach(dest)) && since > 8) { EndIntercept("на месте пусто — упустил"); return false; }
            return true;
        }

        void EndIntercept(string why)
        {
            if (_icpt == null) return;
            Thought($"перехват окончен: {why}");
            _icptCooldown[_icptId] = _w.Time + 180;
            _icpt = null;
            _redecide = true;
        }

        /// <summary>Пришёл на РТ, а она слутана: перехватить того, кто её обобрал; иначе — кемп у выхода; иначе — уйти с причиной.</summary>
        string OnLootedRt()
        {
            if (_rt == null) return "на РТ больше нечего брать";
            Actor taker = null; float tv = 0;
            foreach (var (id, c) in _carriers) if (c.Rt == _rt.M.Name && _w.Time - c.At < 240 && c.Est > tv && _w.Find(id) is Actor ta && !ta.Dead) { tv = c.Est; taker = ta; }
            if (taker == null)
                foreach (var en in _enc.Values)
                    if (!en.A.Dead && _w.Time - en.LastSeen < 120 && _rt.M.Contains(en.SeenPos, 25))
                    {
                        float v = 0;
                        foreach (var cr in _rt.Crates) if (cr.Empty) v += CrateGuess(cr);
                        NoteCarrier(en.A, MathF.Max(40, v), $"РТ «{_rt.Short}» пустая, а отсюда уходил он", _rt.M.Name);
                        taker = en.A;
                        break;
                    }
            LStat("пустая РТ");
            if (taker != null && ArmedForGreed && CarrierOf(taker) is Carrier cc && _icptCooldown.GetValueOrDefault(taker.Id) < _w.Time)
            {
                _icptId = taker.Id; _icpt = null;
                LStat("пустая РТ: перехват");
                return $"РТ пустая — её обобрал {taker.Name} (≈{cc.Est:0} лома), иду перехватывать";
            }
            if (TryCamp("rt-exit", ExitToward(_rt.M, Home), _rt.M, $"выход «{_rt.Short}»"))
                return "РТ пустая — кемплю выход: кто придёт или выйдет с лутом — мой";
            CampReady(out string no);
            LStat("пустая РТ: ушёл");
            return $"РТ пустая, кемпить не стану ({no ?? "не мой стиль"}) — ухожу";
        }

        // ---------- кемп: засада там, где будет лут, — но не «по-крысиному» ----------
        //
        // Места: выход с РТ (слутанная РТ; на РТ шумят перфокарта/тайник/сигналка — кто-то вынесет лут), точка сброса или
        // закрытого ящика, свой свежий труп (жду того, кто придёт обобрать), дорога от РТ к известной базе. Место — с укрытием
        // рядом, в 12–25 м от точки и с прямой видимостью на неё; присел, тихо. Ограничения: не чаще раза в 8–10 мин,
        // 60–150 с, уходит при голоде/жажде/ранении, с грузом ≥ 250 лома, когда заметили или подстрелили; не у спавна и не
        // у чужой двери; у одной РТ не больше двух кемперов; характер: агрессивные — чаще, осторожные — редко, голые — никогда.
        // Бью только того, кто выходит с лутом, или слабее; сильного пропускаю («пропускаю: слишком силён»).

        sealed class CampJob
        {
            public string Kind, Label, Key;
            public Vector2 Target, Spot;
            public float Start, Until;
            public Monument M;
            public bool Struck;
        }

        CampJob _camp;
        float _nextCampAt = 420, _campHitAt = -99;
        public float CampTime;                                   // секунд в кемпе (доля времени — в отчёте)
        readonly Dictionary<string, float> _rtNoiseAt = new();
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, Dictionary<string, int>> _campers = new();

        /// <summary>Готов ли я кемпить сейчас (характер, кит, нужды, груз, кулдаун) — шанс по характеру бросается на каждую возможность.</summary>
        bool CampReady(out string why)
        {
            why = null;
            if (_w.Time < _nextCampAt) { why = "недавно кемпил"; return false; }
            if (_stage5 < WipeStage.Tools) { why = "я ещё голый"; return false; }
            if (Ranged() == null && !(Has("weapon_spear") && Aggression > 0.45f)) { why = "нечем бить из засады"; return false; }
            if (_me.Hp < 65 || NeedsUrgency() > 0 || _me.Food < 30 || _me.Water < 30) { why = "сначала поесть/подлечиться"; return false; }
            if (_load >= 250) { why = $"несу ≈{_load:0} лома — сначала домой"; return false; }
            if (_raid != null || _rj != null) { why = "занят"; return false; }
            return true;
        }

        float CampChance => Math.Clamp(0.1f + 0.75f * Aggression - 0.35f * _caution + 0.2f * _greed, 0.05f, 0.85f);

        /// <summary>Возможность кемпа (kind: rt-exit, rt-noise, drop, corpse, road). true — начал.</summary>
        bool TryCamp(string kind, Vector2 target, Monument m, string label, bool roll = true)
        {
            if (_camp != null || !CampReady(out _)) return false;
            if (roll && _rng.NextDouble() > CampChance) { _nextCampAt = _w.Time + 60; return false; }   // не мой стиль сейчас — минута до следующей мысли
            foreach (var sp in _w.PlayerSpawns) if (Vector2.Distance(sp, target) < 45) return false;     // не у спавна голых
            foreach (var kb in _bases.Values) if (kb.OwnerId != _me.Id && Vector2.Distance(kb.Pos, target) < 22) return false;   // не у чужой двери
            string key = m?.Name ?? $"{(int)(target.X / 30)}:{(int)(target.Y / 30)}";
            var reg = _campers.GetValue(_w, _ => new Dictionary<string, int>());
            if (reg.GetValueOrDefault(key) >= 2) { Say("кемп-занят", $"у «{label}» уже кемпят двое — не полезу", 60); return false; }
            if (CampSpot(target, m) is not Vector2 spot) return false;
            reg[key] = reg.GetValueOrDefault(key) + 1;
            _camp = new CampJob { Kind = kind, Label = label, Key = key, Target = target, Spot = spot, M = m, Start = _w.Time, Until = _w.Time + 60 + (float)_rng.NextDouble() * 90 };
            Thought($"кемплю {label}: {(kind switch { "rt-exit" => "кто придёт или выйдет с лутом — мой", "rt-noise" => "там шумят — выйдет с лутом", "drop" => "к грузу придут", "corpse" => "к трупу придут обирать", _ => "тут ходят с РТ домой" })}");
            LStat("кемп: начат");
            return true;
        }

        void EndCamp(string why)
        {
            if (_camp == null) return;
            var reg = _campers.GetValue(_w, _ => new Dictionary<string, int>());
            reg[_camp.Key] = Math.Max(0, reg.GetValueOrDefault(_camp.Key) - 1);
            Thought($"кемп ({_camp.Label}) окончен: {why}");
            LStat("кемп: секунд", (int)(_w.Time - _camp.Start));
            LStat(why.StartsWith("дождался") ? "кемп: дождался" : why.StartsWith("надоело") ? "кемп: надоело" : "кемп: ушёл раньше");
            _nextCampAt = _w.Time + 480 + 120 * (float)_rng.NextDouble();
            _camp = null;
            _redecide = true;
        }

        /// <summary>Место кемпа: 12–25 м от точки, видно её, рядом укрытие (стена, дерево, глыба, пропс), сам не в препятствии.</summary>
        Vector2? CampSpot(Vector2 target, Monument m)
        {
            Vector2? best = null; float bs = float.MaxValue;
            var toMe = _me.Pos - target;
            float baseAng = MathF.Atan2(toMe.Y, toMe.X);
            for (int k = 0; k < 24; k++)
            {
                float a = baseAng + (k % 2 == 0 ? 1 : -1) * (k / 2) * 0.26f;
                foreach (float r in new[] { 16f, 21f, 13f, 25f })
                {
                    var p = target + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                    if (m != null && m.Contains(p, 2)) continue;   // снаружи РТ
                    if (!_w.Terrain.IsLand(p) || _w.WalkBlocked(p, _me.Radius + 0.1f) || !_w.HasLineOfSight(p, target)) continue;
                    bool cover = _w.WalkBlocked(p, 1.4f);   // укрытие вплотную
                    float s = Vector2.Distance(p, _me.Pos) * 0.05f + (cover ? 0 : 3f) + MathF.Abs(r - 17) * 0.1f;
                    if (s < bs) { bs = s; best = p; }
                }
                if (best != null && bs < 2f) break;
            }
            return best;
        }

        /// <summary>Возможности кемпа из восприятия: шум на РТ (перфокарта, тайник, сигналка) и дорога у РТ к известной базе.</summary>
        void PerceiveCamp()
        {
            foreach (var n in _w.Noises)
            {
                if (n.SourceId == _me.Id || _w.Time - n.Time > 0.1f || Vector2.Distance(n.Pos, _me.Pos) > n.Radius) continue;
                var m = _w.MonumentAt(n.Pos);
                if (m == null || (_rt != null && _rt.M == m)) continue;
                if (_rtNoiseAt.TryGetValue(m.Name, out float at) && _w.Time - at < 120) continue;
                _rtNoiseAt[m.Name] = _w.Time;
                if (Vector2.Distance(_me.Pos, m.Center) < 220 && _camp == null && CampReady(out _))
                    TryCamp("rt-noise", ExitToward(m, _me.Pos), m, $"выход «{Short(m)}»");
            }
            if (_w.Director != null && _camp == null)
                foreach (var r in _w.Director.Rumors)
                    if ((r.Kind == "airdrop" || r.Kind == "locked_crate") && _w.Time - r.Time < 0.1f && Vector2.Distance(r.Pos, _me.Pos) < 300 && JudgeRumor(r, out _) < 40)
                        TryCamp("drop", r.Pos, _w.MonumentAt(r.Pos), r.Kind == "airdrop" ? "место сброса груза" : "закрытый ящик");
            // кемп кончается: время, нужды, груз, ранен, заметили
            if (_camp != null)
            {
                CampTime += World.Dt;
                if (_w.Time > _camp.Until) EndCamp("надоело, ухожу");
                else if (_me.Hp < 60 || _w.Time - _me.LastDamagedTime < 1f) EndCamp("меня подстрелили — засада раскрыта");
                else if (NeedsUrgency() > 0.6f) EndCamp("голод/жажда — ухожу");
                else if (_load >= 250) EndCamp($"несу ≈{_load:0} лома — домой");
            }
        }

        static string Short(Monument m) => m.Name.Contains('«') ? m.Name.Split('«')[0].Trim() : m.Name;

        /// <summary>Выход с РТ в сторону точки: край прямоугольника РТ на линии от центра к ней.</summary>
        static Vector2 ExitToward(Monument m, Vector2 from)
        {
            var c = m.Center; var d = from - c;
            if (d.LengthSquared() < 1) d = Vector2.UnitX;
            d = Vector2.Normalize(d);
            var half = (m.Max - m.Min) * 0.5f;
            float t = MathF.Min(MathF.Abs(d.X) > 1e-3f ? half.X / MathF.Abs(d.X) : 1e6f, MathF.Abs(d.Y) > 1e-3f ? half.Y / MathF.Abs(d.Y) : 1e6f);
            return c + d * t;
        }

        float LootCampScore()
        {
            if (_camp == null)
            {
                // дорога: без дел (хозяйство, бродить) недалеко от РТ без охраны — посидеть у выхода, откуда несут лут
                // (к известной чужой базе, иначе — в мою сторону); раз в 8–10 мин, шанс по характеру
                if (Aggression > 0.25f && _w.Time >= _nextCampAt && _rtPhase == RtPhase.None && (_w.Tick + _me.Id) % 300 == 0
                    && (_goal == null || _goal.Name is "хозяйство" or "бродить" or "снаряжение") && CampReady(out _))
                {
                    RtInfo near = null; float nd = 170;
                    foreach (var r in Rts()) { float dd = Vector2.Distance(r.M.Center, _me.Pos); if (r.Guards == 0 && dd < nd) { nd = dd; near = r; } }
                    if (near != null)
                    {
                        Vector2 toward = _me.Pos; string to = "моей стороне";
                        foreach (var kb in _bases.Values) if (kb.OwnerId != _me.Id && Vector2.Distance(kb.Pos, near.M.Center) < 260) { toward = kb.Pos; to = "чужой базе"; break; }
                        var exit = ExitToward(near.M, toward);
                        var road = exit + Vector2.Normalize(toward - exit + new Vector2(0.01f, 0)) * 25f;
                        if (!Guarded(road)) TryCamp("road", road, null, $"дорогу от «{near.Short}» к {to}");
                    }
                }
                return _camp != null ? 71 : 0;
            }
            return 71;   // выше хозяйства и похода на РТ, ниже боя, бегства, нужд при голоде (кемп сам кончается)
        }

        bool CampGoalAct(ref InputCmd cmd)
        {
            if (_camp == null) return false;
            var c = _camp;
            Doing = $"кемпит {c.Label} ({c.Until - _w.Time:0} с)";
            if (Ranged() is string g) Hold(g, ref cmd); else if (Has("weapon_spear")) Hold("weapon_spear", ref cmd);
            if (!MoveTo(c.Spot, 1.2f, ref cmd)) return true;
            cmd.Crouch = true;   // присел, тихо
            cmd.Aim = c.Target;
            return true;
        }

        /// <summary>
        /// Встреча из засады: выходит с лутом (видел груз) или слабее — бью («дождался — бью»); сильнее — пропускаю.
        /// null — кемп ни при чём (решает обычная встреча).
        /// </summary>
        (Stance, string)? CampJudge(Actor a, float edge)
        {
            if (_camp == null || Vector2.Distance(a.Pos, _camp.Target) > 30) return null;
            var car = CarrierOf(a);
            bool loot = car != null && car.Est >= 40;
            bool weaker = edge >= 0.08f;
            if (loot || weaker)
            {
                string why = loot ? $"дождался — бью: {a.Name} несёт ≈{car.Est:0} лома" : $"дождался — бью: {a.Name} слабее ({Items.Name(a.Held?.Id ?? "tool_rock")})";
                _camp.Struck = true; _campHitAt = _w.Time;
                EndCamp(why);
                return (Stance.Fight, why);
            }
            Say("кемп-пропуск" + a.Id, $"пропускаю {a.Name}: слишком силён ({Items.Name(a.Held?.Id ?? "tool_rock")})", 60);
            LStat("кемп: пропустил");
            return (Stance.Watch, "из засады пропускаю: слишком силён");
        }

        // ---------- обиды на весь вайп ----------

        /// <summary>Обида: kind — Kill, LootedKill, Raid, ShotFirst. Копится, растит желание мстить.</summary>
        void Offend(Actor a, string kind, string why)
        {
            if (!IsPerson(a)) return;
            if (!_grudge.TryGetValue(a.Id, out var g)) _grudge[a.Id] = g = new Grudge();
            if (kind == "LootedKill" && _w.Time - g.LootedAt < 60) return;   // один и тот же труп — одна обида
            if (kind == "Raid" && _w.Time - g.RaidAt < 300) return;
            switch (kind)
            {
                case "Kill": g.Kills++; g.KilledMe = true; break;
                case "LootedKill": g.LootedKills++; g.LootedAt = _w.Time; break;
                case "Raid": g.Raids++; g.RaidAt = _w.Time; break;
                case "ShotFirst": g.ShotFirst++; break;
            }
            g.At = g.OffendAt = _w.Time; g.LastPos = a.Pos; g.LastSeen = _w.Time; g.Kit = KitPower(a);
            Thought($"обида на {a.Name}: {why} — {GrudgeLine(g)}");
            LStat("обида: " + kind switch { "Kill" => "убил меня", "LootedKill" => "обобрал моё убийство", "Raid" => "рейдил", _ => "напал первым" });
        }

        static string GrudgeLine(Grudge g)
        {
            var p = new List<string>();
            if (g.Kills > 0) p.Add($"убил меня {g.Kills}");
            if (g.LootedKills > 0) p.Add($"обобрал моё {g.LootedKills}");
            if (g.Raids > 0) p.Add($"рейдил {g.Raids}");
            if (g.ShotFirst > 0) p.Add($"нападал {g.ShotFirst}");
            if (g.Revenged > 0) p.Add($"отомстил {g.Revenged}");
            return $"счёт: {string.Join(", ", p)} (злость {g.Severity:0.#})";
        }

        /// <summary>Сводка обид для наблюдателя и летописи: «Гена 7,5; Жека 2».</summary>
        public string GrudgeSummary
        {
            get
            {
                var p = new List<string>();
                foreach (var (id, g) in _grudge) if (g.Severity >= 1) p.Add($"{_w.Find(id)?.Name ?? "?"} {g.Severity:0.#}");
                return string.Join("; ", p);
            }
        }
    }
}
