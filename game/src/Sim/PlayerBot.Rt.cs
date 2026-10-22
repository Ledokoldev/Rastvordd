using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Этап 4 мозга: походы на РТ (docs/design/bot-brain.md). Инструмент данных: любая РТ из World.Monuments описывается
    /// таблицей «риск / награда» из того, что на ней стоит (охрана, ящики, сейф, бочки и хлам, переработчик, крейт под перфокарту),
    /// поэтому новые готовые РТ (тир 0) подхватываются сами. Поход: собраться (оставить ценное дома, взять ствол, патроны, бинты
    /// и то, что нужно на этой РТ) → дойти → обойти цели (ящики, сейф картой, бочки и хлам, сумки, переработчик; на Подстанции —
    /// пробки → щиток → перфокарта в крейт → 30 с охраны крейта) → уйти (ранен, набрал, слышу опасность, пусто, долго) → домой и в ящик.
    /// Бот знает только карту (где РТ, что там стоит и сколько постов охраны) и то, что сам увидел.
    /// </summary>
    public sealed partial class PlayerBot
    {
        // ---------- таблица РТ ----------

        public sealed class RtInfo
        {
            public Monument M;
            public Substation Sub;
            public Tier0Rt T0;          // РТ тира 0: перфокарта I лежит или за тайником «держи E»
            public int Tier, Guards, ScrapSpots;
            public float Kit;           // чем вооружена охрана (сила на глаз)
            public float Risk, Reward;  // для выбора базы и похода
            public readonly List<LootBag> Crates = new(), Safes = new();
            public Deployable Recycler;
            public string Short => M.Name.Contains('«') ? M.Name.Split('«')[0].Trim() : M.Name;
            public float Radius => (M.Max - M.Min).Length() * 0.5f;
        }

        /// <summary>Ручные правки для отдельных РТ (по началу имени): уровень, множители риска и награды. Новые РТ — сюда же при нужде.</summary>
        static readonly (string prefix, int tier, float risk, float reward)[] RtOverrides =
        {
            ("Склад", 2, 1.2f, 1.2f),            // крупный РТ: шесть калашей в упор, лучший лут
        };

        List<RtInfo> _rts;
        float _rtsAt = -999;

        List<RtInfo> Rts()
        {
            if (_rts != null && _w.Time - _rtsAt < 600) return _rts;
            _rtsAt = _w.Time;
            _rts = new List<RtInfo>();
            foreach (var m in _w.Monuments)
            {
                var r = new RtInfo { M = m, Sub = _w.Substations.Find(s => s.M == m), T0 = _w.Tier0.Find(t => t.M == m) };
                foreach (var b in _w.Bags)
                    if (b.IsCrate && m.Contains(b.Pos, 1)) { if (b.Safe) r.Safes.Add(b); else if (!b.Punch) r.Crates.Add(b); }
                foreach (var d in _w.Deployables) if (d.Kind == DeployKind.Recycler && m.Contains(d.Pos, 1)) r.Recycler = d;
                foreach (var h in _w.NpcHomes.Values) if (m.Contains(h, 2)) r.Guards++;
                foreach (var n in _w.NodesInRadius(m.Center, r.Radius + 2, _nodeBuf)) if (n.Kind is NodeKind.Barrel or NodeKind.Junk && m.Contains(n.Pos, 2)) r.ScrapSpots++;
                r.Kit = r.Guards == 0 ? 0 : r.Sub != null ? 0.8f : 1f;
                int maxTier = 0;
                float reward = r.ScrapSpots * 5;
                foreach (var c in r.Crates) { reward += CrateGuess(c); maxTier = Math.Max(maxTier, c.Tier); }
                reward += r.Safes.Count * 70 + (r.Sub != null ? 120 : 0) + (r.Recycler != null ? 15 : 0) + (r.T0 != null ? 30 : 0);
                r.Tier = r.Sub != null ? 1 : r.Guards == 0 ? 0 : Math.Max(1, maxTier);
                r.Risk = r.Guards * r.Kit;
                r.Reward = reward;
                foreach (var (prefix, tier, risk, rew) in RtOverrides)
                    if (m.Name.StartsWith(prefix)) { r.Tier = tier; r.Risk *= risk; r.Reward *= rew; }
                _rts.Add(r);
            }
            return _rts;
        }

        /// <summary>Аппетит к риску 0..1 из характера: осторожность против жадности и тяги к драке.</summary>
        float Appetite => Math.Clamp(0.1f + (0.9f - _caution) * 1.3f + (_greed - 0.3f) * 0.6f + (Archetype == BotArchetype.Roamer ? 0.3f : 0f), 0.05f, 0.95f);

        RtInfo _fav, _leaveFor;
        Vector2 _leaveTo;
        int _favCrowd;

        /// <summary>«Своя» РТ, у которой строиться: осторожный — тихая и бедная, рисковый — опасная и богатая.</summary>
        RtInfo PickFavourite(out string why)
        {
            why = "";
            var rts = Rts();
            if (rts.Count == 0) return null;
            float maxRew = 1, riskUnit = 2f;   // риск в «двух калашах»: два охранника — уже серьёзно для осторожного
            foreach (var r in rts) maxRew = MathF.Max(maxRew, r.Reward);
            float a = Appetite, best = float.MinValue;
            RtInfo pick = null;
            foreach (var r in rts)
            {
                // тесно: у этой РТ уже строятся другие — каждая чужая база минус 0,22 (все 12 ботов садились у двух РТ)
                int crowd = 0;
                lock (Sites) foreach (var (w, q) in Sites) if (w == _w && Vector2.Distance(q, r.M.Center) < r.Radius + 130) crowd++;
                float s = a * r.Reward / maxRew - (1 - a) * 1.4f * r.Risk / riskUnit - Vector2.Distance(_me.Pos, r.M.Center) / 3000f + (float)_rng.NextDouble() * 0.08f - 0.22f * crowd;
                if (s > best) { best = s; pick = r; _favCrowd = crowd; }
            }
            why = (_favCrowd > 0 ? $"рядом уже {_favCrowd} баз, но " : "") + (pick.Guards == 0 ? "тихо, охраны нет — мне хватит"
                : a > 0.6f ? $"рискую: {pick.Guards} охр., зато лут жирный"
                : $"охраны немного ({pick.Guards}), лут приличный");
            return pick;
        }

        // ---------- состояние похода ----------

        enum RtPhase { None, Prep, Travel, Loot, Leave }
        RtPhase _rtPhase;
        RtInfo _rt;
        float _rtNextAt = 240, _rtStartAt, _rtLootAt, _rtScoreAt = -999, _rtScore, _rtValue0;
        string _rtWhy;
        readonly Dictionary<string, int> _rtHad = new(), _rtStashed = new();
        readonly Dictionary<object, float> _rtSkip = new();
        readonly Dictionary<string, int> _rtDeaths = new();
        float _scrapWant, _scrapWantAt = -999;   // прогрессия упёрлась в лом: сколько не хватает

        static readonly string[] RunItems = { "scrap", "comp_gears", "comp_spring", "comp_pipe", "comp_tape", "comp_trigger", "comp_bolt_carrier", "comp_barrel_rifled", "comp_frame_pistol", "comp_body_smg", "comp_body_rifle", "comp_receiver_ak", "comp_circuit", "comp_lens", "keycard", "punchcard_1", "punchcard_2", "ammo_9mm", "ammo_12g", "ammo_762", "metal_fragments", "bandage", "gunpowder" };

        /// <summary>Сколько общих деталей бот держит под крафт стволов (D46): лишнее — в переработчик.</summary>
        public static int CompKeep(string id) => id switch
        {
            "comp_spring" => 12, "comp_pipe" => 4, "comp_tape" => 4, "comp_gears" => 8,   // пружины не точатся — их держат больше
            _ when Economy.IsValuable(id) => 0,                                  // ценный лом — только в переработчик (D49)
            _ when Array.IndexOf(Weaponry.Tier2Parts, id) >= 0 => 2,             // детали тира 2 — по две на стволы, лишнее в лом
            _ => int.MaxValue,
        };
        /// <summary>Сколько лома дадут лишние детали и ценный лом (при себе и дома) — повод сходить к переработчику.</summary>
        int RecycleScrapStock() => World.BotRecycleScrap(Stock, CompKeep);
        int CompSurplus(string id) => CompKeep(id) == int.MaxValue ? 0 : Math.Max(0, Stock(id) - CompKeep(id));
        int CompKeepPocket(string id) => Math.Max(0, CompKeep(id) - (MyBox() is Deployable b ? InBox(b, id) : 0));

        /// <summary>Что взять из ящика в этот поход (и не сейчить, пока он идёт).</summary>
        int TripNeeds(string id)
        {
            if (_rt == null) return 0;
            return id switch
            {
                "punchcard_1" => _rt.Sub != null ? 1 : 0,
                "keycard" => _rt.Safes.Count > 0 ? 1 : 0,
                "comp_gears" or "comp_spring" or "comp_pipe" or "comp_tape" => _rt.Recycler != null ? CompSurplus(id) : 0,
                _ when Array.IndexOf(World.BotRecycleIds, id) >= 0 => _rt.Recycler != null ? CompSurplus(id) : 0,   // ценный лом и лишние детали тира 2
                _ when id.StartsWith("ammo_") => Ranged() is string g && WeaponAmmo(g) == id ? (id is "ammo_762" or "ammo_545" ? 120 : 60) : 0,
                "bandage" => 3,
                _ => 0,
            };
        }

        int Stock(string id) => _me.Count(id) + (MyBox() is Deployable b ? InBox(b, id) : 0);

        /// <summary>Что брать из ящика в поход: карты, патроны, бинты и всё, что пойдёт в переработчик.</summary>
        static readonly string[] TripIds = BuildTripIds();
        static string[] BuildTripIds()
        {
            var l = new List<string> { "punchcard_1", "keycard", "ammo_9mm", "ammo_12g", "ammo_762", "bandage" };
            foreach (var id in World.BotRecycleIds) if (!l.Contains(id)) l.Add(id);
            return l.ToArray();
        }

        /// <summary>Могу ли я идти на эту РТ: охрана против моего ствола, патроны, смерти там.</summary>
        bool RtFeasible(RtInfo r, out string why)
        {
            why = null;
            var gun = Ranged();
            int deaths = _rtDeaths.GetValueOrDefault(r.M.Name);
            // без оружия на РТ не ходят: копьё минимум, лук и самопал — лучше (пользователь: «так неинтересно»)
            if (gun == null && !Has("weapon_spear")) { why = "без оружия на РТ не хожу — сначала копьё"; return false; }
            // тир 0 — «сходить буквально один раз»: за перфокартой (если её нет) или первый раз за ломом; дальше — только кемпить там
            if (r.Tier == 0 && r.Guards == 0)
            {
                int runs = _rtRuns.GetValueOrDefault(r.M.Name);
                bool needCard = Stock("punchcard_1") == 0 && _w.Substations.Count > 0 && r.T0 != null;
                int t0Runs = 0;
                foreach (var (name, n) in _rtRuns) if (Rts().Exists(x => x.M.Name == name && x.Tier == 0 && x.Guards == 0)) t0Runs += n;
                if (runs == 0 && (needCard || (t0Runs < 2 && _stage5 < WipeStage.Arms))) { }   // каждую тир 0 — один раз; за ломом — не больше двух всего
                else if (r.Recycler != null && RecycleScrapStock() >= 40) { }                     // переработчик — главный источник лома (D49): к нему ходят сколько надо
                else { why = runs > 0 ? $"тир 0 уже обобрал ({runs} раз) — там нечего ловить" : "тир 0 мне уже не нужен"; return false; }
            }
            if (r.Guards == 0) return deaths < 3 || (why = "там уже умирал") == null;
            if (gun == null) { why = "охрана, а у меня нет ствола"; return false; }
            if (AmmoLeft(gun) < 20) { why = "мало патронов"; return false; }
            if (MyPower < r.Kit - 0.25f) { why = $"у охраны {(r.Kit >= 1 ? "калаши" : "самопалы")} — мой {Items.Name(gun)} слабее"; return false; }
            // с перфокартой Подстанция — главный приз этапа: терплю на 3 охранника больше (раньше перфокарты копились, а на Подстанцию не шёл никто)
            int extra = r.Sub != null && Stock("punchcard_1") > 0 ? 3 : 0;
            if (r.Guards > 2 + extra + (int)(Aggression * 4 + Appetite * 2)) { why = $"слишком много охраны ({r.Guards})"; return false; }
            if (deaths >= 2 + (int)(Appetite * 3)) { why = $"там уже {deaths} раза умирал"; return false; }
            return true;
        }

        /// <summary>Цель «поход на РТ»: нужда (лом на прогрессию, перфокарта, детали, давно не ходил) × готовность × вес архетипа.</summary>
        float RtScore()
        {
            if (_rtPhase != RtPhase.None) return (_rtPhase == RtPhase.Prep ? 58 : 66) * MathF.Max(W("поход на РТ"), 0.9f);
            if (_w.Time < _rtNextAt || _w.Time < _rebuildUntil || _w.Time - _alarmAt < 120 || _raid != null || _rj != null || !Has("tool_hatchet")) return 0;
            if (Night && Aggression < 0.75f) return 0;   // раздел 10: ночью на РТ не ходят (кроме самых отчаянных) — крафт, сортировка, фарм у дома            if (_w.Time - _rtScoreAt < 3) return _rtScore;
            _rtScoreAt = _w.Time;
            _rtScore = 0;
            if (_me.Hp < 75 || _me.Food < 15 || _me.Water < 15) return 0;   // поесть и попить — на сборах
            if (Archetype == BotArchetype.Farmer && MyBenchLevel() < 1) return 0;   // фермер сначала отстраивает мейн с верстаком
            float need = 0;
            var why = new List<string>();
            if (_w.Time - _scrapWantAt < 20 && _scrapWant > 0) { need += Math.Clamp(_scrapWant / 120f, 0.15f, 0.6f); why.Add($"не хватает {_scrapWant:0} лома на прогрессию"); }
            bool card = Stock("punchcard_1") > 0;
            if (card && _w.Substations.Count > 0) { need += 0.5f; why.Add("есть перфокарта I — Подстанция"); }
            bool wantCard = !card && _w.Substations.Count > 0 && _w.Tier0.Count > 0 && Ranged() != null;
            if (wantCard) { need += 0.2f; why.Add("нужна перфокарта I (на тире 0) — для крейта Подстанции"); }
            int recScrap = RecycleScrapStock();
            int comps = recScrap >= 25 ? recScrap : 0;
            if (recScrap >= 25) { need += Math.Clamp(recScrap / 150f, 0.2f, 0.6f); why.Add($"ценный лом и лишние детали ≈{recScrap} лома — в переработчик"); }
            if (_partsWant.Count > 0 && _w.Time - _partsWantAt < 60) { need += 0.3f; why.Add($"на ствол не хватает: {string.Join(", ", new List<string>(_partsWant.Keys).ConvertAll(Items.Name))}"); }
            float since = _w.Time - LastRunEnd;
            if (since > 1200) { need += 0.35f; why.Add("давно не был на РТ"); }
            if (Archetype == BotArchetype.Roamer) { need += 0.3f; why.Add("лут РТ — мой хлеб"); }
            if (need < 0.3f) return 0;

            // лучшая РТ: ценность для меня против риска (с поправкой на мой аппетит) и дороги
            RtInfo pick = null;
            float best = float.MinValue;
            string no = null;
            foreach (var r in Rts())
            {
                if (!RtFeasible(r, out string w))
                {
                    if (r == _fav) no = $"«{r.Short}»: {w}";
                    if (card && r.Sub != null) Say("подстанция-нет", $"с перфокартой на «{r.Short}» не иду: {w}", 300);
                    continue;
                }
                float value = r.Reward * (r == _fav ? 1.3f : 1f) + (card && r.Sub != null ? 150 : 0) + (comps > 0 && r.Recycler != null ? 40 + comps : 0)
                              + (Stock("keycard") > 0 ? r.Safes.Count * 60 : 0) + (wantCard && r.T0 != null && !Skipped(r.T0) ? 90 : 0);
                float risk = r.Risk * (1 - Appetite) * 25 + Vector2.Distance(Home, r.M.Center) / 8f + (DangerSpot(r.M.Center, r.Radius + 20) ? 60 * (1 - Appetite) : 0);   // недавно там умирал
                // слух по радио «на РТ ещё есть перфокарта» — туда, если перфокарта нужна
                if (wantCard && _w.Director != null) foreach (var ru in _w.Director.Rumors) if (ru.Kind == "punchcard" && r.M.Contains(ru.Pos, 5)) value += 60;
                float s = value - risk;
                if (s > best) { best = s; pick = r; }
            }
            if (pick == null) { Say("рт-нет", $"на РТ не иду: {no ?? "ни одна мне не по силам"}", 120); return 0; }
            _rtPick = pick;
            _rtWhy = string.Join(", ", why);
            _rtScore = (36 + 45 * MathF.Min(need, 1)) * W("поход на РТ");
            return _rtScore;
        }
        RtInfo _rtPick;
        float LastRunEnd = 0;

        Vector2 Home => MyClaim()?.Pos ?? _me.Pos;

        bool RtRun(ref InputCmd cmd)
        {
            if (_rtPhase == RtPhase.None)
            {
                // выбор устаревает: между пересчётами целей поход мог только что закончиться (раньше — сотни «иду на РТ» за секунду)
                if (_rtPick == null || _w.Time < _rtNextAt || _w.Time - _rtScoreAt > 3.5f) return false;
                _rt = _rtPick;
                _rtPhase = RtPhase.Prep;
                _rtStartAt = _rtLootAt = _w.Time;
                _rtSkip.Clear();
                _guardSaid = false;
                Thought($"иду на РТ «{_rt.Short}»{(_rt == _fav ? " (моя)" : "")}: {_rtWhy}; охрана {_rt.Guards}");
                LStat("походов на РТ");
            }
            if (_w.Time - _rtStartAt > 1000 && _rtPhase != RtPhase.Leave) { LeaveRt("поход затянулся"); }
            switch (_rtPhase)
            {
                case RtPhase.Prep: return RtPrep(ref cmd);
                case RtPhase.Travel: return RtTravel(ref cmd);
                case RtPhase.Loot: return RtLoot(ref cmd);
                case RtPhase.Leave: return RtLeave(ref cmd);
            }
            return false;
        }

        // ---------- сборы ----------

        bool RtPrep(ref InputCmd cmd)
        {
            var box = MyBox();
            // дома лежит нужное в поход (перфокарта, карта, детали, патроны) или несу ценное — сначала домой
            bool wantHome = false;
            if (box != null)
            {
                foreach (var id in TripIds)
                    if (TripNeeds(id) > _me.Count(id) && InBox(box, id) > 0) wantHome = true;
                wantHome |= _surplus >= 40;
            }
            bool home = box != null && Vector2.Distance(_me.Pos, box.Pos) < 60;
            if (box != null && wantHome && !home && Vector2.Distance(_me.Pos, box.Pos) < 350 && _w.Time - _rtStartAt < 240)
            {
                Doing = $"собирается на РТ «{_rt.Short}»: идёт домой за снаряжением";
                GoTo(box.Pos, 3f, ref cmd);
                return true;
            }
            if (box != null && home && _w.Time - _rtStartAt < 240)
            {
                // ценное — дома (кроме нужного в поход)
                if (_surplus >= 10) { if (StashAct(ref cmd)) { Doing = "собирается на РТ: оставляет ценное дома"; return true; } }
                // взять из ящика нужное в поход
                foreach (var id in TripIds)
                {
                    int want = TripNeeds(id);
                    if (want <= 0 || _me.Count(id) >= Math.Min(want, _me.Count(id) + InBox(box, id)) || InBox(box, id) == 0) continue;
                    if (TakeFromBox(box, id, Math.Min(want, _me.Count(id) + InBox(box, id)), ref cmd)) { Doing = $"собирается на РТ: берёт {Items.Name(id)}"; return true; }
                }
                if (_me.Count("bandage") < 2 && _me.Count("cloth") >= 4 && Craft("bandage", ref cmd)) return true;
            }
            // поесть и попить перед дорогой
            if (_w.Time - _rtStartAt < 240)
            {
                if (_me.Food < 50 && (_me.Count("berries") > 0 || _me.Count("canned") > 0)) cmd.Eat = true;
                if (_me.Water < 45) { Doing = "пьёт перед походом"; if (UsePickup(PickupKind.WaterBarrel, ref cmd)) return true; }
                if (_me.Food < 40 && _me.Count("berries") == 0 && _me.Count("canned") == 0) { Doing = "ищет еду перед походом"; if (UsePickup(PickupKind.Berries, ref cmd)) return true; }
            }
            var gun = Ranged();
            int ammo = gun != null ? AmmoLeft(gun) : 0;
            Thought($"собрался на «{_rt.Short}»: {(gun != null ? $"{Items.Name(gun)}, {ammo} патр." : "без ствола")}, бинтов {_me.Count("bandage")}" +
                    $"{(_me.Count("punchcard_1") > 0 && _rt.Sub != null ? ", перфокарта I" : "")}{(_me.Count("keycard") > 0 ? ", карта доступа" : "")}; дома ≈{StashValue():0} лома");
            _rtPhase = RtPhase.Travel;
            return true;
        }

        // ---------- дорога ----------

        bool RtTravel(ref InputCmd cmd)
        {
            if (RtDanger(out string why)) { LeaveRt(why); return true; }
            float d = Vector2.Distance(_me.Pos, _rt.M.Center);
            Doing = $"идёт на РТ «{_rt.Short}» ({d:0} м)";
            if (Ranged() is string g) Hold(g, ref cmd);
            if (_rt.M.Contains(_me.Pos, 6))
            {
                _rtPhase = RtPhase.Loot;
                _rtLootAt = _w.Time;
                _rtValue0 = CarriedValue();
                _rtHad.Clear();
                _rtStashed.Clear();
                _rtProg.Clear();
                _rtMine = 0;
                foreach (var id in RunItems) _rtHad[id] = _me.Count(id);
                Thought($"на РТ «{_rt.Short}» — обхожу");
                return true;
            }
            var target = RtEntry();
            if (_rtTravelStuck(target)) { LeaveRt("не могу дойти до РТ"); return true; }
            GoTo(target, 2.5f, ref cmd);
            return true;
        }

        /// <summary>Точка входа: ближайшая к дороге цель внутри РТ (ящик, бочка), а не центр (там стена).</summary>
        Vector2 RtEntry()
        {
            var best = _rt.M.Center;
            float bd = float.MaxValue;
            foreach (var c in _rt.Crates) { float d = Vector2.Distance(c.Pos, _me.Pos); if (d < bd) { bd = d; best = c.Pos; } }
            return best;
        }

        float _travelBest = float.MaxValue, _travelStuck;
        Vector2 _travelFor;
        bool _rtTravelStuck(Vector2 t)
        {
            float d = Vector2.Distance(_me.Pos, t);
            if (Vector2.Distance(_travelFor, t) > 1) { _travelFor = t; _travelBest = d; _travelStuck = 0; }
            if (d < _travelBest - 1) { _travelBest = d; _travelStuck = 0; } else _travelStuck += World.Dt;
            if (_travelStuck > 40) { _travelStuck = 0; return true; }
            return false;
        }

        // ---------- на РТ: цели ----------

        enum RtTk { Crate, Safe, Node, Bag, Fuse, Panel, Punch, Guard, Recycler, Stash }
        (RtTk kind, object obj, Vector2 pos)? _rtT;
        float _rtTSince, _rtTBest, _rtTPick, _recycleSince;
        bool _guardSaid;

        bool Skipped(object o) => _rtSkip.TryGetValue(o, out float until) && until > _w.Time;
        void Skip(object o, float sec) => _rtSkip[o] = _w.Time + sec;

        /// <summary>Вижу ли точку (для «ящик пуст», «пробки нет»): рядом и без стены.</summary>
        bool SeePoint(Vector2 p, float r = 18f) => Vector2.Distance(p, _me.Pos) < 2.5f || (Vector2.Distance(p, _me.Pos) < r && _w.HasLineOfSight(_me.Pos, p));

        (RtTk, object, Vector2)? PickRtTarget()
        {
            var s = _rt.Sub;
            // Подстанция: пробки → щиток → перфокарта → охрана крейта → лут крейта
            if (s != null)
            {
                if (s.Crate != null && s.Crate.Unlocked && !s.Crate.Empty && !Skipped(s.Crate)) return (RtTk.Crate, s.Crate, s.Crate.Pos);
                if (s.CrateTimer > 0 && _punchedByMe) return (RtTk.Guard, s, s.CratePos);
                bool card = _me.Count("punchcard_1") > 0;
                if (card && !s.Powered)
                {
                    if (s.FusesIn + _me.Count("fuse") >= 2 || (_me.Count("fuse") > 0 && s.FusesIn < 2 && FuseSpotsLeft(s) == 0)) { if (_me.Count("fuse") > 0) return (RtTk.Panel, s, s.PanelPos); }
                    Pickup bestF = null; float bd = float.MaxValue;
                    foreach (var p in s.FuseSpots)
                    {
                        if (Skipped(p)) continue;
                        float d = Vector2.Distance(p.Pos, _me.Pos);
                        if (d < bd) { bd = d; bestF = p; }
                    }
                    if (bestF != null) return (RtTk.Fuse, bestF, bestF.Pos);
                }
                if (card && s.Powered && s.Crate != null && !s.Crate.Unlocked && s.CrateTimer <= 0 && !Skipped(s.Crate)) return (RtTk.Punch, s.Crate, s.Crate.Pos);
            }
            // тир 0: перфокарта лежит — взять; тайник «держи E» — делать, стоя рядом (шумно); начатый — не бросать
            if (_rt.T0 is Tier0Rt t0)
            {
                foreach (var st in t0.Stashes) if (st.ActorId == _me.Id && !st.Done) return (RtTk.Stash, st, st.Pos);
                if (t0.Card != null && !Skipped(t0.Card) && !(SeePoint(t0.Card.Pos, 10) && !t0.Card.Available)) return (RtTk.Fuse, t0.Card, t0.Card.Pos);
            }
            (RtTk, object, Vector2)? best = null;
            float bestCost = float.MaxValue;
            void Consider(RtTk k, object o, Vector2 p, float bonus)
            {
                if (Skipped(o) || NoReach(p)) return;
                float c = Vector2.Distance(p, _me.Pos) - bonus;
                if (c < bestCost) { bestCost = c; best = (k, o, p); }
            }
            foreach (var c in _rt.Crates) if (!(c.Empty && SeePoint(c.Pos))) Consider(RtTk.Crate, c, c.Pos, 8);
            if (_rt.T0 != null)
                foreach (var st in _rt.T0.Stashes)
                    if (!st.Done && (st.ActorId == 0 || st.ActorId == _me.Id)) Consider(RtTk.Stash, st, st.Pos, st.GivesCard ? 30 : 12);
            foreach (var sf in _rt.Safes) if ((sf.Unlocked && !sf.Empty) || (!sf.Unlocked && _me.Count("keycard") > 0)) Consider(RtTk.Safe, sf, sf.Pos, 20);
            foreach (var b in _w.Bags)
                if (!b.IsCrate && !b.Empty && _rt.M.Contains(b.Pos, 4) && !MyDrop(b)) Consider(RtTk.Bag, b, b.Pos, 10);
            foreach (var n in _w.NodesInRadius(_rt.M.Center, _rt.Radius + 3, _nodeBuf))
                if (n.Kind is NodeKind.Barrel or NodeKind.Junk && _rt.M.Contains(n.Pos, 3)) Consider(RtTk.Node, n, n.Pos, n.Kind == NodeKind.Junk ? 4 : 0);
            if (_rt.Recycler != null && (World.BotRecyclable(_me, CompKeepPocket) > 0 || _recycleSince > 0)) Consider(RtTk.Recycler, _rt.Recycler, _rt.Recycler.Pos, 15);
            return best;
        }
        bool _punchedByMe;
        readonly Dictionary<object, (float best, float since)> _rtProg = new();
        int _rtMine;                                                   // ящиков РТ вскрыл я сам в этот поход
        readonly Dictionary<string, int> _rtRuns = new();              // сколько раз ходил на РТ (тир 0 — «один раз»)
        readonly Dictionary<string, float> _rtLast = new();

        /// <summary>
        /// Склад Подстанции (крейт) — только через роллету с улицы: снаружи сначала к роллете, потом внутрь
        /// (A* ищет путь в окне вокруг отрезка и вокруг корпуса ОПУ его не находит). true — иду к роллете.
        /// </summary>
        bool ViaShutter(Substation s, ref InputCmd cmd)
        {
            if (s == null) return false;
            var w = s.Shutter.Wall;
            var mid = (w.Min + w.Max) * 0.5f;
            bool inside = Building.PointInPoly(_me.Pos, new[] { s.At(18, 27), s.At(34.4f, 27), s.At(34.4f, 34), s.At(18, 34) });
            if (inside) return false;
            var front = mid + new Vector2(2.2f, 0);
            if (Vector2.Distance(_me.Pos, front) < 1.2f) { cmd.Move = Vector2.Normalize(mid - new Vector2(1.5f, 0) - _me.Pos); cmd.Aim = mid - new Vector2(3, 0); return true; }
            MoveTo(front, 1.0f, ref cmd);
            return true;
        }

        int FuseSpotsLeft(Substation s) { int n = 0; foreach (var p in s.FuseSpots) if (!Skipped(p)) n++; return n; }

        /// <summary>Уходить ли с РТ / не идти дальше: ранен, кончаются патроны, рядом стреляют сильнее моего.</summary>
        bool RtDanger(out string why)
        {
            why = null;
            if (_me.Hp < 30 || (_me.Hp < 50 && _me.Count("bandage") == 0)) why = $"ранен ({_me.Hp:0} HP)";
            else if (_rt.Guards > 0 && Ranged() is string g && AmmoLeft(g) < 6) why = "кончаются патроны";
            else
                foreach (var h in _heard)
                    if (!h.Explosion && _w.Time - h.Time < 8 && h.SourceId != _foe?.Id && _w.Find(h.SourceId) is Actor src && src.Team != Team.Npc
                        && Vector2.Distance(h.Pos, _me.Pos) < 70 && WeaponPower(h.Weapon) > MyPower + 0.05f)
                    { why = $"рядом стреляют из {Items.Name(h.Weapon)} — сильнее моего"; break; }
            return why != null;
        }

        bool RtLoot(ref InputCmd cmd)
        {
            // уходить?
            float got = CarriedValue() - _rtValue0;
            float limit = 120 + 260 * Appetite + (_rt.Sub != null && (_me.Count("punchcard_1") > 0 || _punchedByMe) ? 300 : 0);   // главный приз — крейт: ради него терплю груз
            if (RtDanger(out string why)) { LeaveRt(why); return true; }
            if (got >= limit) { LeaveRt($"набрал ≈{got:0} лома — несу домой, пока не отобрали"); return true; }
            if (FreePack() == 0) { LeaveRt("рюкзак полон"); return true; }
            if (_w.Time - _rtLootAt > (_rt.Sub != null && _me.Count("punchcard_1") > 0 ? 480 : 300) && !(_rt.Sub?.CrateTimer > 0)) { LeaveRt("долго торчу на РТ"); return true; }

            if (_rtT == null || _w.Time - _rtTPick > 1.5f)
            {
                var t = PickRtTarget();
                if (t == null) { LeaveRt(_rtMine > 0 || _rtValue0 + 30 < CarriedValue() ? "на РТ больше нечего брать" : OnLootedRt()); return true; }
                if (_rtT == null || t.Value.Item2 != _rtT.Value.obj) { _rtTSince = _w.Time; _rtTBest = float.MaxValue; }
                _rtT = t;
                _rtTPick = _w.Time;
            }
            var (kind, obj, pos) = _rtT.Value;
            // не подойти 12 с — пропустить цель. Прогресс — по каждой цели отдельно: раньше две бочки за стеной чередовались
            // каждые 1,5 с, таймер сбрасывался, и бот упирался в стену заправки по 2 минуты
            float d = Vector2.Distance(_me.Pos, pos);
            bool special = kind == RtTk.Guard || kind == RtTk.Recycler || (obj is Stash hs && hs.ActorId == _me.Id);
            if (!special && NoReach(pos)) { Skip(obj, 180); _rtT = null; Say("рт-недост", "до цели на РТ не дойти (за стеной без прохода) — пропускаю", 20); return true; }
            var pr = _rtProg.TryGetValue(obj, out var pv) ? pv : (best: float.MaxValue, since: _w.Time);
            if (d < pr.best - 0.3f) pr = (d, _w.Time);
            _rtProg[obj] = pr;
            if (_w.Time - pr.since > 12 && !special) { Skip(obj, 120); _rtProg.Remove(obj); _rtT = null; LStat("цель РТ: не подошёл 12 с"); return true; }
            var gun = Ranged();
            switch (kind)
            {
                case RtTk.Crate:
                case RtTk.Bag:
                case RtTk.Safe:
                {
                    var bag = (LootBag)obj;
                    if (bag.Punch && ViaShutter(_rt.Sub, ref cmd)) return true;
                    if (bag.Empty) { Skip(bag, bag.IsCrate ? 120 : 600); _rtT = null; return true; }
                    if (bag.Punch && !bag.Unlocked) { Skip(bag, 30); _rtT = null; return true; }   // запертый крейт — только перфокартой
                    Doing = kind == RtTk.Safe ? "открывает сейф" : kind == RtTk.Bag ? "обирает сумку на РТ" : "обыскивает ящик на РТ";
                    if (!Reach(bag.Pos, ref cmd)) return true;
                    if (MakeRoom(bag.Safe || bag.Punch ? CrateGuess(bag) : 20, ref cmd)) return true;
                    if (bag.Safe && !bag.Unlocked)
                    {
                        if (_w.BotOpenSafe(_me, bag)) { Thought("открыл сейф картой доступа"); LStat("сейфов открыто"); }
                        return true;
                    }
                    int before = bag.Items.Count;
                    if (_w.Time - _lastUse > 0.4f) { _lastUse = _w.Time; _w.BotUse(_me, bag); }
                    if (bag.Punch && bag.Empty) { _w.Milestone(_me, $"вскрыл крейт РТ «{_rt.Short}» перфокартой"); LStat("крейтов вскрыто"); }
                    if (!bag.Empty && bag.Items.Count == before) Skip(bag, 60);   // не влезло
                    if (bag.IsCrate && !bag.Punch) { LStat("ящиков РТ"); _rtMine++; }
                    _rtT = null;
                    return true;
                }
                case RtTk.Node:
                {
                    var n = (ResourceNode)obj;
                    if (!_w.Nodes.Contains(n)) { _rtT = null; return true; }
                    Doing = n.Kind == NodeKind.Junk ? "ковыряет хлам на РТ" : "бьёт бочку на РТ";
                    if (!Reach(n.Pos, ref cmd, n.Radius + 0.55f)) return true;   // место рукой, без стены до бочки (раньше — «в радиусе», хоть из-за стены)
                    cmd.Aim = n.Pos;
                    string tool = n.Kind == NodeKind.Junk ? (Has("tool_pickaxe") ? "tool_pickaxe" : "tool_rock")
                                                         : (Has("weapon_spear") ? "weapon_spear" : Has("tool_hatchet") ? "tool_hatchet" : "tool_rock");
                    if (Hold(tool, ref cmd)) cmd.Fire = true;
                    return true;
                }
                case RtTk.Fuse:
                {
                    var p = (Pickup)obj;
                    bool cardPick = p.Kind == PickupKind.PunchCard;
                    if (SeePoint(p.Pos, 12) && !p.Available) { Skip(p, 900); _rtT = null; return true; }   // пробки (перфокарты) тут нет
                    Doing = cardPick ? "берёт перфокарту I" : "ищет пробки на Подстанции";
                    if (!Reach(p.Pos, ref cmd)) { if (gun != null) Hold(gun, ref cmd); return true; }
                    if (_w.BotUse(_me, p))
                    {
                        if (cardPick) { Thought("взял перфокарту I — теперь крейт Подстанции"); LStat("перфокарт I найдено"); _w.Milestone(_me, $"нашёл перфокарту I на РТ «{_rt.Short}»"); }
                        else Thought($"нашёл пробку ({_me.Count("fuse")} при себе, в щитке {_rt.Sub.FusesIn})");
                        Skip(p, 900);
                    }
                    _rtT = null;
                    return true;
                }
                case RtTk.Stash:
                {
                    var st = (Stash)obj;
                    if (st.Done) { Skip(st, 900); _rtT = null; return true; }
                    Doing = $"{st.Verb.ToLower()} ({st.Progress:0}/{st.Seconds:0} с)";
                    if (st.ActorId != _me.Id)
                    {
                        if (!Reach(st.Pos, ref cmd, 0.8f)) return true;
                        if (_w.Time - _lastUse > 0.5f)
                        {
                            _lastUse = _w.Time;
                            if (_w.BotUseStash(_me, st)) Thought($"{st.Verb.ToLower()} — {st.Seconds:0} с шума, не отхожу");
                            else Skip(st, 60);
                        }
                        return true;
                    }
                    // держу E: стою рядом, смотрю на вход
                    cmd.Aim = st.Pos;
                    _rtTSince = _w.Time;
                    if (st.Done || st.Progress + World.Dt >= st.Seconds) { if (st.GivesCard) { LStat("перфокарт I найдено"); _w.Milestone(_me, $"добыл перфокарту I на РТ «{_rt.Short}»"); } _rtT = null; }
                    return true;
                }
                case RtTk.Panel:
                {
                    var s = (Substation)obj;
                    Doing = $"вставляет пробки в щиток ({s.FusesIn}/2)";
                    if (!Reach(s.PanelPos, ref cmd, 0.6f)) return true;
                    if (_w.Time - _lastUse > 0.6f && _me.Count("fuse") > 0)
                    {
                        _lastUse = _w.Time;
                        _w.BotUsePanel(_me, s);
                        if (s.Powered) { Thought("щиток включён — гудят трансформаторы (слышно на 60 м), роллета поднялась"); _w.Milestone(_me, $"запитал щиток РТ «{_rt.Short}»"); }
                    }
                    return true;
                }
                case RtTk.Punch:
                {
                    var bag = (LootBag)obj;
                    Doing = "несёт перфокарту к крейту";
                    if (ViaShutter(_rt.Sub, ref cmd)) return true;
                    if (!Reach(bag.Pos, ref cmd)) return true;
                    if (_w.Time - _lastUse > 0.6f)
                    {
                        _lastUse = _w.Time;
                        if (_w.BotUsePunchCrate(_me, bag) && _rt.Sub.CrateTimer > 0)
                        {
                            _punchedByMe = true;
                            Thought($"перфокарта в считывателе — крейт откроется через {_rt.Sub.CrateTimer:0} с; гул слышно на всю округу, охраняю");
                        }
                        else Skip(bag, 20);
                    }
                    return true;
                }
                case RtTk.Guard:
                {
                    var s = (Substation)obj;
                    // охрана крейта: в стороне от него, лицом ко входу (роллета), присев; бой — директор поднимет «бой»
                    var door = s.Shutter.Wall.Min + (s.Shutter.Wall.Max - s.Shutter.Wall.Min) * 0.5f;
                    var spot = s.CratePos + Vector2.Normalize(door - s.CratePos + new Vector2(0.01f, 0)) * 2.2f + new Vector2(0, -1.2f);
                    Doing = $"охраняет крейт: {s.CrateTimer:0} с";
                    if (!_guardSaid) { _guardSaid = true; LStat("охрана крейта"); }
                    if (!MoveTo(spot, 0.8f, ref cmd)) return true;
                    cmd.Crouch = true;
                    cmd.Aim = door + Vector2.Normalize(door - s.CratePos + new Vector2(0.01f, 0)) * 4;
                    if (gun != null) Hold(gun, ref cmd);
                    return true;
                }
                case RtTk.Recycler:
                {
                    var rc = (Deployable)obj;
                    Doing = "перерабатывает детали в лом";
                    if (!Reach(rc.Pos, ref cmd, 1.2f)) return true;
                    if (_recycleSince <= 0)
                    {
                        int n = _w.BotRecyclerPut(_me, rc, CompKeepPocket);
                        if (n > 0) { _recycleSince = _w.Time; Thought($"детали ×{n} в переработчик — лязг слышно, жду с оружием"); LStat("деталей в переработчик", n); }
                        else { Skip(rc, 300); _rtT = null; }
                        return true;
                    }
                    if (gun != null) Hold(gun, ref cmd);
                    cmd.Crouch = true;
                    bool done = !rc.Storage.Items.Exists(x => Items.RecycleYield(x.Id).Count > 0);
                    if (done || _w.Time - _recycleSince > 40)
                    {
                        int got2 = _w.BotRecyclerTake(_me, rc);
                        if (got2 > 0) Thought($"забрал из переработчика: лом и металл ×{got2}");
                        _recycleSince = 0;
                        Skip(rc, 300);
                        _rtT = null;
                    }
                    return true;
                }
            }
            return true;
        }

        void LeaveRt(string why)
        {
            if (_rtPhase == RtPhase.Leave || _rt == null) return;
            if (_rt.Sub != null && _rt.Sub.CrateTimer > 0 && _punchedByMe && _me.Hp >= 30 && !why.StartsWith("ранен")) return;   // крейт вот-вот откроется — дожидаюсь
            _rtPhase = RtPhase.Leave;
            Thought($"ухожу с РТ «{_rt.Short}»: {why}");
        }

        bool RtLeave(ref InputCmd cmd)
        {
            var claim = MyClaim();
            Doing = $"несёт лут с РТ «{_rt.Short}» домой";
            if (Ranged() is string g) Hold(g, ref cmd);
            if (claim == null)
            {
                if (Vector2.Distance(_me.Pos, _rt.M.Center) > _rt.Radius + 50) { EndRun("отошёл от РТ"); return false; }
                // точка отхода считается один раз: раньше она «ехала» вместе с ботом (от центра РТ через меня), счётчик застревания
                // каждый тик сбрасывался, маршрут не строился — бродяга без базы стоял у забора по 25 мин
                if (_leaveFor != _rt) { _leaveFor = _rt; var away = _me.Pos - _rt.M.Center; _leaveTo = _rt.M.Center + Vector2.Normalize(away + new Vector2(0.01f, 0)) * (_rt.Radius + 60); if (!_w.Terrain.IsLand(_leaveTo)) _leaveTo = _rt.M.Center + Geo.Rotate(Vector2.Normalize(away + new Vector2(0.01f, 0)), 1.6f) * (_rt.Radius + 60); }
                var to = _leaveTo;
                GoTo(to, 4, ref cmd);
                if (_rtTravelStuck(to)) { EndRun("застрял на выходе с РТ"); return false; }   // без базы: раньше стоял у забора бесконечно
                return true;
            }
            if (Vector2.Distance(_me.Pos, claim.Pos) < 14) { EndRun("дома"); return false; }
            GoTo(claim.Pos + new Vector2(0.6f, 0.6f), 3f, ref cmd);
            if (_rtTravelStuck(claim.Pos)) { EndRun("застрял по дороге домой"); return false; }
            return true;
        }

        /// <summary>Итог похода: что принёс (по сравнению с началом обхода) — в мысли и счётчики.</summary>
        void EndRun(string why, bool died = false)
        {
            if (_rt == null) { _rtPhase = RtPhase.None; return; }
            var parts = new List<string>();
            if (!died && _rtHad.Count > 0)
                foreach (var id in RunItems)
                {
                    int delta = _me.Count(id) + _rtStashed.GetValueOrDefault(id) - _rtHad.GetValueOrDefault(id);
                    if (delta > 0) { parts.Add($"{Items.Name(id)} +{delta}"); LStat("принесено: " + Items.Name(id), delta); }
                }
            if (_rtPhase is RtPhase.Loot or RtPhase.Leave) { _rtRuns[_rt.M.Name] = _rtRuns.GetValueOrDefault(_rt.M.Name) + 1; _rtLast[_rt.M.Name] = _w.Time; if (_rt.Tier == 0) LStat("походов на тир 0"); }
            if (died) { _rtDeaths[_rt.M.Name] = _rtDeaths.GetValueOrDefault(_rt.M.Name) + 1; LStat("смертей в походе"); }
            else LStat("вернулся с РТ");
            Thought(died ? $"погиб в походе на «{_rt.Short}» — запомню ({_rtDeaths[_rt.M.Name]})" : $"поход на «{_rt.Short}» окончен ({why}): {(parts.Count > 0 ? string.Join(", ", parts) : "пусто")}");
            if (!died && parts.Count > 0) { _w.Milestone(_me, $"принёс с РТ «{_rt.Short}»: {string.Join(", ", parts.GetRange(0, Math.Min(3, parts.Count)))}"); DepositSoon(); }
            LastRunEnd = _w.Time;
            _rtNextAt = _w.Time + (Archetype == BotArchetype.Roamer ? 150 : 240 + 240 * _caution);
            _rtPhase = RtPhase.None;
            _rt = null;
            _rtT = null;
            _punchedByMe = false;
            _recycleSince = 0;
        }
    }
}
