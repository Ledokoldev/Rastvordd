using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Мозг v3 (docs/design/bot-brain.md, раздел 8; D43): план вайпа — лестница этапов с условием выхода, начатые задачи
    /// по слухам режиссёра (сброс груза, закрытый ящик, «слабая база»), после смерти — домой за набором, карта опасных мест.
    /// Реакции (бой, бегство, антирейд) — прерывания: после них бот возвращается к своей задаче (директор целей).
    /// Слухи — честно «по радио»: место примерное (в пределах радиуса слуха), что там — бот видит только сам, когда дойдёт.
    /// </summary>
    public sealed partial class PlayerBot
    {
        // ---------- план вайпа: этапы ----------

        enum WipeStage { Naked, Shack, Tools, Main, Scrap, Arms, Raids, Final }

        static readonly string[] StageNames = { "голый старт", "кибитка", "инструменты и запас", "мейн с верстаком", "лом и перфокарты", "оружие (верстак 2)", "РТ тира 2 и рейды", "верстак 3" };
        WipeStage _stage5 = WipeStage.Naked;
        float _stageAt, _stageCheckAt = -99;
        public string StageName => StageNames[(int)_stage5];

        /// <summary>Где я на лестнице: первый невыполненный этап. Бродяга идёт мимо кибитки сразу к РТ.</summary>
        WipeStage CurrentStage()
        {
            bool roamer = Archetype == BotArchetype.Roamer;
            if (!Has("tool_hatchet") || !Has("tool_pickaxe")) return WipeStage.Naked;
            var claim = MyClaim();
            if (claim == null || MyBox() == null) return WipeStage.Shack;
            int bench = MyBenchLevel();
            if (bench < 1) return roamer ? WipeStage.Scrap : (MyFurnace() == null ? WipeStage.Tools : WipeStage.Main);
            if (bench < 2) return WipeStage.Scrap;
            if (bench < 3) return Ranged() is string g && AmmoLeft(g) >= 20 ? WipeStage.Raids : WipeStage.Arms;
            return WipeStage.Final;
        }

        bool _hadBag;

        void StepStage()
        {
            if (_w.Time - _stageCheckAt < 5) return;
            _stageCheckAt = _w.Time;
            if (!_hadBag && _w.ReadyBag(_me) != null) { _hadBag = true; Thought("спальник постелен — теперь просыпаюсь дома"); LStat("спальник постелен"); _w.Milestone(_me, "постелил спальник"); }
            var s = CurrentStage();
            if (s == _stage5) return;
            Thought($"этап вайпа: {StageNames[(int)_stage5]} → {StageNames[(int)s]} (за {(_w.Time - _stageAt) / 60:0} мин)");
            if (s > _stage5) _w.Milestone(_me, $"этап: {StageNames[(int)s]}");
            _stage5 = s; _stageAt = _w.Time;
        }

        // ---------- слухи по радио → задачи ----------

        sealed class RumorJob
        {
            public Rumor R;
            public DirectorEvent Ev;   // ивент, к которому слух (сброс, закрытый ящик) — что там на месте, бот видит сам
            public Vector2 Guess;      // куда идти: примерное место из слуха
            public string Kind, Why;
            public float StartAt, ArriveAt = -1;
            public bool Started;       // взлом запущен мной
            public bool Looting;       // уже лутаю ящик: домой не тянет, пока не выгреб
            public float Got;          // сколько унёс из ящика (в ломах)
            public int Idle;           // заходов подряд, когда ничего не влезло
            public bool Spotted;       // увидел сам по пути, а не по радио
        }

        /// <summary>Сброс, который уже пробовал взять по пути (не дёргаться к нему снова каждые полсекунды): до какого времени.</summary>
        readonly Dictionary<DirectorEvent, float> _spotTried = new();

        /// <summary>
        /// 2026-10-27 (напарник): «сброс лежит, мимо ходят боты — и им всё равно». Увидел сброс сам (≤ 45 м, глазами) и ничего ценного
        /// при себе (лишнего меньше «несу ценное») — бросаю текущее дело и иду лутать. Не в бою, не ранен, не лутаю РТ и не в рейде.
        /// Закрытый ящик на РТ — только уже открытый (ждать взлом 3 мин по пути никто не станет).
        /// </summary>
        void SpotDrop()
        {
            if (_rj != null || _w.Director == null || _me.Hp < 50 || _threat != null || _raid != null || _rtPhase == RtPhase.Loot) return;
            if (_surplus >= CarryAvoid) return;
            foreach (var ev in _w.Director.Events)
            {
                if (ev.Crate == null || !_w.Bags.Contains(ev.Crate) || (ev.Open && ev.Crate.Empty)) continue;
                if (ev.Kind == "locked_crate" ? !ev.Open : ev.Kind != "airdrop") continue;
                if (_spotTried.TryGetValue(ev, out float until) && _w.Time < until) continue;
                if (Vector2.Distance(ev.Crate.Pos, _me.Pos) > 45 || !SeePoint(ev.Crate.Pos, 45)) continue;
                _spotTried[ev] = _w.Time + 240;
                var r = new Rumor { Pos = ev.Crate.Pos, Radius = 10, Kind = ev.Kind, Time = _w.Time, Until = _w.Time + 600, Text = "увидел сам" };
                string what = ev.Kind == "airdrop" ? "сброс груза" : "открытый закрытый ящик";
                _rj = new RumorJob { R = r, Ev = ev, Kind = ev.Kind, Guess = ev.Crate.Pos, StartAt = _w.Time, Spotted = true, Why = $"вижу {what} в {Vector2.Distance(ev.Crate.Pos, _me.Pos):0} м, при мне ценного ≈{_surplus:0} лома — почему бы не залутать" };
                _rjPick = null;
                if (_rtPhase == RtPhase.Travel) EndRun("по пути увидел сброс — РТ подождёт");
                Thought($"по пути: {_rj.Why}");
                LStat("сброс: заметил по пути");
                _w.Milestone(_me, $"заметил по пути: {what}");
                _redecide = true;
                return;
            }
        }

        readonly HashSet<Rumor> _rumorsJudged = new();
        RumorJob _rj, _rjPick;
        float _rjScore, _rjScoreAt = -99, _rjCooldown;

        /// <summary>Как я отношусь к слуху: идти ли (по этапу, характеру, снаряжению, грузу, дороге). Решение пишется в мысли.</summary>
        float JudgeRumor(Rumor r, out string why)
        {
            float d = Vector2.Distance(r.Pos, _me.Pos);
            var gun = Ranged();
            int ammo = gun != null ? AmmoLeft(gun) : 0;
            why = null;
            if (_me.Hp < 70) { why = $"ранен ({_me.Hp:0} HP)"; return 0; }
            if (_surplus > LoadLimit) { why = $"несу ≈{_surplus:0} лома — сначала домой"; return 0; }
            switch (r.Kind)
            {
                case "airdrop":
                case "locked_crate":
                {
                    if (_stage5 < WipeStage.Tools) { why = "я ещё голый — там будет мясорубка"; return 0; }
                    if (gun == null && Aggression < 0.6f) { why = "нечем стрелять — за грузом пойдут со стволами"; return 0; }
                    if (gun != null && ammo < 12) { why = "мало патронов"; return 0; }
                    if (d > 320 + 200 * Aggression) { why = $"далеко ({d:0} м)"; return 0; }
                    float s = (r.Kind == "airdrop" ? 80 : 74) * (0.55f + Aggression * 0.7f + (gun != null ? GunPower(gun) * 0.3f : 0)) * MathF.Max(0.35f, 1 - d / 600f);
                    why = $"{(r.Kind == "airdrop" ? "сброс груза" : "закрытый ящик")} в {d:0} м, у меня {(gun != null ? $"{Items.Name(gun)} ({ammo})" : "только " + Items.Name(Has("weapon_spear") ? "weapon_spear" : "tool_rock"))}";
                    return s;
                }
                case "weak_base":
                case "rich_loot":
                {
                    if (_stage5 < WipeStage.Arms && Bombs == 0) { why = "рейдить пока нечем"; return 0; }
                    if (d > 450) { why = "далеко"; return 0; }
                    foreach (var kb in _bases.Values) if (Vector2.Distance(kb.Pos, r.Pos) < r.Radius + 20 && _w.Time - kb.SeenAt < 600) { why = "эту базу я уже знаю"; return 0; }
                    why = r.Kind == "weak_base" ? "база с деревянными дверями — схожу посмотреть" : "база с полными ящиками — схожу посмотреть";
                    return 52 * (0.6f + Aggression) * MathF.Max(0.4f, 1 - d / 700f);
                }
                case "hot":
                case "fight":
                {
                    if (gun == null || ammo < 20 || Aggression < 0.55f) { why = gun == null ? "нечем стрелять" : "не моя драка"; return 0; }
                    if (d > 260) { why = "далеко"; return 0; }
                    why = r.Kind == "fight" ? "там кто-то с полным рюкзаком — поживлюсь" : "там стреляют — подберу, что останется";
                    return 48 * Aggression * MathF.Max(0.4f, 1 - d / 400f);
                }
                case "substation_power":
                {
                    if (gun == null || ammo < 20 || Aggression < 0.5f) { why = "не полезу: там будут со стволами"; return 0; }
                    why = "кто-то включил Подстанцию — встречу его у крейта";
                    return d < 300 ? 46 * Aggression : 0;
                }
            }
            return 0;
        }

        /// <summary>Очки «слуха»: начатая задача держится (76), пока не сделана или не отменена с причиной.</summary>
        float RumorScore()
        {
            SpotDrop();
            if (_rj != null) return _rj.Spotted ? 88 : 76;   // увидел сам — перебивает хозяйство, походы и сдачу лута
            if (_w.Director == null || _rtPhase != RtPhase.None || _raid != null || _w.Time < _rjCooldown) return 0;
            if (_w.Time - _rjScoreAt < 2) return _rjScore;
            _rjScoreAt = _w.Time; _rjScore = 0; _rjPick = null;
            foreach (var r in _w.Director.Rumors)
            {
                if (r.Until < _w.Time) continue;
                float s = JudgeRumor(r, out string why);
                if (_rumorsJudged.Add(r)) { Thought($"по радио: «{r.Text}» → {(s >= 40 ? "иду" : "не иду")}: {why ?? "не для меня"}"); LStat(s >= 40 ? "слухов: иду" : "слухов: не иду"); }
                if (s >= 40 && s > _rjScore)
                {
                    _rjScore = s;
                    var ang = (float)(_rng.NextDouble() * MathF.Tau);
                    var ev = _w.Director.Events.Find(e => Vector2.Distance(e.Pos, r.Pos) < r.Radius + 5 && (e.Kind == r.Kind));
                    _rjPick = new RumorJob { R = r, Ev = ev, Kind = r.Kind, Why = why, Guess = r.Pos + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r.Radius * 0.5f * (float)_rng.NextDouble() };
                }
            }
            if (_rumorsJudged.Count > 200) _rumorsJudged.RemoveWhere(x => x.Until < _w.Time);
            return _rjScore;
        }

        void EndRumorJob(string why, float cooldown = 120)
        {
            if (_rj == null) return;
            Thought($"задача «{RumorJobName(_rj)}» окончена: {why}");
            _rj = null;
            _rjCooldown = _w.Time + cooldown;
            _redecide = true;
        }

        /// <summary>Чужой рядом с точкой (видел за последние 5 с) — у ящика лучше не задерживаться.</summary>
        bool RivalNear(Vector2 p, float r)
        {
            foreach (var en in _enc.Values)
                if (!en.A.Dead && _w.Time - en.LastSeen < 5f && Vector2.Distance(en.SeenPos, p) < r) return true;
            return false;
        }

        static string RumorJobName(RumorJob j) => j.Kind switch
        {
            "airdrop" => "сброс груза", "locked_crate" => "закрытый ящик", "weak_base" or "rich_loot" => "посмотреть базу из слуха",
            "substation_power" => "засада у Подстанции", _ => "на шум",
        };

        /// <summary>Видно ли мне ящик ивента (рядом и без стены) — что там на месте, бот узнаёт только глазами.</summary>
        bool SeesCrate(LootBag c) => c != null && _w.Bags.Contains(c) && SeePoint(c.Pos, 30);

        bool RumorAct(ref InputCmd cmd)
        {
            if (_rj == null)
            {
                if (_rjPick == null) return false;
                _rj = _rjPick; _rjPick = null;
                _rj.StartAt = _w.Time;
                Thought($"беру задачу «{RumorJobName(_rj)}»: {_rj.Why}");
                LStat("задач по слухам");
                _w.Milestone(_me, $"идёт по слуху: {RumorJobName(_rj)}");
            }
            var j = _rj;
            if (_w.Time - j.StartAt > 600) { EndRumorJob("затянулось (10 мин)"); return false; }
            if (!j.Looting && _surplus > LoadLimit + 150) { EndRumorJob($"набрал ≈{_surplus:0} лома — несу домой", 60); return false; }
            if (_me.Hp < 40) { EndRumorJob($"ранен ({_me.Hp:0} HP)"); return false; }
            var gun = Ranged();
            if (gun != null) Hold(gun, ref cmd); else if (Has("weapon_spear")) Hold("weapon_spear", ref cmd);
            float d = Vector2.Distance(_me.Pos, j.Guess);

            if (j.Kind is "weak_base" or "rich_loot")
            {
                Doing = $"идёт посмотреть базу из слуха ({d:0} м)";
                foreach (var kb in _bases.Values) if (Vector2.Distance(kb.Pos, j.R.Pos) < j.R.Radius + 25 && _w.Time - kb.SeenAt < 5) { EndRumorJob($"увидел базу ({_w.Find(kb.OwnerId)?.Name ?? "?"}) — запомнил для рейда", 300); LStat("баз из слухов"); return false; }
                if (MoveTo(j.Guess, 6f, ref cmd) || (d < 15 && NoReach(j.Guess))) { if (j.ArriveAt < 0) j.ArriveAt = _w.Time; j.Guess = j.R.Pos + Geo.Rotate(Vector2.UnitX, (float)(_rng.NextDouble() * MathF.Tau)) * j.R.Radius * 0.8f; }
                else if (d < 15 && j.ArriveAt < 0 && _w.Time - j.StartAt > 180) j.ArriveAt = _w.Time;   // рядом, а точно не подойти (за стенами) — считать, что дошёл
                if (j.ArriveAt > 0 && _w.Time - j.ArriveAt > 60) EndRumorJob("обошёл место — базы не нашёл", 300);
                return true;
            }
            if (j.Kind is "hot" or "fight" or "substation_power")
            {
                Doing = $"идёт на слух: {RumorJobName(j)} ({d:0} м)";
                if (d < 30) cmd.Crouch = true;
                if (MoveTo(j.Guess, 5f, ref cmd)) { if (j.ArriveAt < 0) j.ArriveAt = _w.Time; cmd.Crouch = true; }
                if (j.ArriveAt > 0 && _w.Time - j.ArriveAt > 45) EndRumorJob("на месте тихо — ухожу");
                return true;
            }

            // сброс груза и закрытый ящик: дойти, найти глазами, ждать/взломать/охранять, вынести
            var ev = j.Ev;
            if (ev == null || !_w.Director.Events.Contains(ev))
            {
                if (d < 25) { EndRumorJob("на месте пусто — опоздал"); return false; }
            }
            var crate = ev?.Crate;
            bool sees = SeesCrate(crate) || (ev?.Start != null && SeePoint(ev.Start.Pos, 25));
            if (!sees || d > 40)
            {
                Doing = $"идёт к {(j.Kind == "airdrop" ? "сбросу груза" : "закрытому ящику")} ({d:0} м)";
                if (d < 35) cmd.Crouch = _rng.NextDouble() < 0.5;
                var target = sees ? (crate?.Pos ?? ev.Pos) : j.Guess;
                if (MoveTo(target, 3f, ref cmd))
                {
                    if (j.ArriveAt < 0) j.ArriveAt = _w.Time;
                    // на месте не вижу — обойти округу слуха
                    j.Guess = j.R.Pos + Geo.Rotate(Vector2.UnitX, (float)(_rng.NextDouble() * MathF.Tau)) * j.R.Radius * 0.7f;
                    if (_w.Time - j.ArriveAt > 90) EndRumorJob("обошёл место — ящика не нашёл");
                }
                if (sees) j.Guess = crate?.Pos ?? ev.Pos;
                return true;
            }
            if (j.ArriveAt < 0) { j.ArriveAt = _w.Time; Thought($"вижу {(j.Kind == "airdrop" ? "сброс груза" : "закрытый ящик")} — {(ev.Open ? "открыт" : "ещё закрыт")}"); }
            // закрытый ящик ещё не взламывают — запустить взлом (держи E, шумно), потом охранять
            if (j.Kind == "locked_crate" && ev.StartsAt <= 0 && ev.Start != null && !ev.Start.Done)
            {
                Doing = "запускает взлом закрытого ящика";
                if (!Reach(ev.Start.Pos, ref cmd)) return true;
                if (_w.BotUseStash(_me, ev.Start) && !j.Started) { j.Started = true; Thought("запускаю взлом закрытого ящика — 3 мин, гул на всю округу; охраняю"); LStat("взломов запущено"); }
                return true;
            }
            // 2026-10-27 (напарник): сброс груза — замок остыл, но сам не откроется: вскрыть (держи E 10 с, громко). Раньше все ждали вокруг вечно.
            // Занял другой — жду рядом (встречи решает «бой/бегство»); бросил или убит — пробую сам.
            if (j.Kind == "airdrop" && !ev.Open && ev.Start != null && !ev.Start.Done && (ev.Start.ActorId == 0 || ev.Start.ActorId == _me.Id))
            {
                Doing = "вскрывает контейнер сброса";
                if (!Reach(ev.Start.Pos, ref cmd)) return true;
                if (_w.BotUseStash(_me, ev.Start) && !j.Started) { j.Started = true; Thought("вскрываю контейнер сброса — 10 с, слышно далеко; не отхожу"); LStat("сбросов: вскрытие начато"); }
                return true;
            }
            if (!ev.Open || crate == null || !crate.Unlocked)
            {
                // ждать открытия: присесть у ящика, смотреть по сторонам (встречи решает «бой/бегство»)
                Doing = $"ждёт, когда откроется {(j.Kind == "airdrop" ? "груз" : "ящик")} ({MathF.Max(0, ev.StartsAt - _w.Time):0} с)";
                var spot = (crate?.Pos ?? ev.Pos) + Vector2.Normalize(_me.Pos - (crate?.Pos ?? ev.Pos) + new Vector2(0.01f, 0)) * 6f;
                if (MoveTo(spot, 2f, ref cmd)) { cmd.Crouch = true; cmd.Aim = _me.Pos + Geo.Rotate(Vector2.UnitX, _w.Time * 0.6f + _me.Id) * 5; }
                if (ev.StartsAt > 0 && ev.StartsAt - _w.Time > 240) { EndRumorJob("ждать слишком долго"); return false; }
                return true;
            }
            if (crate.Empty) { EndRumorJob("пусто — забрали до меня"); return false; }
            Doing = $"лутает {(j.Kind == "airdrop" ? "сброс груза" : "закрытый ящик")}";
            if (!Reach(crate.Pos, ref cmd)) return true;
            // 2026-10-27 (напарник): выгрести всё, что влезет, а не схватить первое и уйти. Лишний хлам — на землю (MakeRoom).
            // Рядом соперник — забрал что успел и ухожу; никого — лутаю до конца.
            j.Looting = true;
            if (MakeRoom(CrateGuess(crate), ref cmd)) return true;
            if (_w.Time - _lastUse <= 0.4f) return true;
            _lastUse = _w.Time;
            float before = CarriedValue();
            _w.BotUse(_me, crate);
            float got = CarriedValue() - before;
            j.Got += MathF.Max(0, got);
            j.Idle = got > 0 ? 0 : j.Idle + 1;
            string done = crate.Empty ? "выгреб всё" : j.Got > 0 && RivalNear(crate.Pos, 30) ? "рядом чужой — забрал что успел" : j.Idle >= 3 ? "больше не влезает" : null;
            if (done != null)
            {
                if (j.Got > 0)
                {
                    LStat(j.Kind == "airdrop" ? "сбросов вскрыто" : "закрытых ящиков вскрыто");
                    _w.Milestone(_me, $"забрал {(j.Kind == "airdrop" ? "сброс груза" : "закрытый ящик")}");
                    DepositSoon();
                    _stashBlock = 0;
                }
                EndRumorJob($"{done} (≈{j.Got:0} лома) — несу домой", 240);
            }
            return true;
        }

        // ---------- после смерти: домой за набором; карта опасных мест ----------

        readonly List<(Vector2 pos, float at)> _deathSpots = new();
        float _respawnAt = -999;
        bool _wasDead;

        /// <summary>Смерть и возрождение: место смерти — в карту опасности (10 мин), после возрождения — за набором домой.</summary>
        void StepDeath()
        {
            if (_me.Dead) { if (!_wasDead) { _deathSpots.Add((_me.Pos, _w.Time)); if (_deathSpots.Count > 12) _deathSpots.RemoveAt(0); if (_rj != null) { _rj = null; _rjCooldown = _w.Time + 300; } } _wasDead = true; return; }
            if (_wasDead)
            {
                _wasDead = false; _respawnAt = _w.Time;
                bool inBag = _w.ReadyBag(_me) is Deployable bg && Vector2.Distance(bg.Pos, _me.Pos) < 2f;
                Thought(inBag ? "проснулся в своём спальнике" : "проснулся на пляже");
                LStat(inBag ? "респаун: спальник" : "респаун: пляж");
            }
        }

        /// <summary>Опасно ли здесь по памяти (умирал недалеко за последние 10 мин).</summary>
        bool DangerSpot(Vector2 p, float r = 40)
        {
            foreach (var (pos, at) in _deathSpots) if (_w.Time - at < 600 && Vector2.Distance(pos, p) < r) return true;
            return false;
        }

        static readonly string[] KitIds = { "weapon_spear", "bandage", "tool_hatchet", "tool_pickaxe" };

        static int KitWant(string id) => id switch { "ammo_762" or "ammo_545" => 60, "arrow" or "ammo_bolt" => 20, "ammo_12g" => 24, "bandage" => 3, _ when id.StartsWith("ammo_") => 40, _ => 1 };

        /// <summary>Что забрать из ящика после смерти: лучший ствол, патроны к своим стволам, копьё, бинты, инструменты.</summary>
        string KitWanted(Deployable box)
        {
            if (box?.Storage == null) return null;
            string own = Ranged();
            float have = own != null ? Weaponry.Power(own) : 0;
            foreach (var g in Weaponry.GunsByPower)
            {
                if (Weaponry.Power(g) <= have) break;
                if (InBox(box, g) == 0 || Has(g)) continue;
                if (BoxWith(g, true) != null) return g;
            }
            // патроны к стволам, что при мне или в ящике
            foreach (var g in Weaponry.GunsByPower)
            {
                if (!Has(g) && InBox(box, g) == 0) continue;
                string ammo = WeaponAmmo(g);
                if (_me.Count(ammo) < KitWant(ammo) && InBox(box, ammo) > 0) return ammo;
            }
            foreach (var id in KitIds)
            {
                if (id == "weapon_spear" && Ranged() != null) continue;
                if (id.StartsWith("weapon_") && Has(id)) continue;
                if (_me.Count(id) < KitWant(id) && InBox(box, id) > 0) return id;
            }
            // 2026-10-27 (напарник): броня и одежда — лучшая из ящика на себя (раньше после смерти брали ствол и бинты, а броню бросали дома)
            return BetterWearInBox(box);
        }

        /// <summary>Вещь из своих ящиков, которая лучше надетой в том же слоте (Clothing.BotScore); null — нет.</summary>
        string BetterWearInBox(Deployable box)
        {
            ScanMine();
            string best = null; float gain = 0;
            foreach (var b in _mBoxes)
                foreach (var s in b.Storage.Items)
                {
                    if (Clothing.Def(s.Id) is not WearDef d || Has(s.Id)) continue;
                    var cur = _me.Worn[(int)d.Slot];
                    float g = Clothing.BotScore(d) - (cur == null ? 0 : Clothing.BotScore(Clothing.Def(cur.Id)));
                    if (g > gain + 0.01f) { gain = g; best = s.Id; }
                }
            return best;
        }

        float AfterDeathScore()
        {
            if (MyBox() is not Deployable box) return 0;
            // после смерти — 5 мин; потом — когда и так рядом с домом (≤ 20 м): проходил мимо — переоделся и взял лучший ствол
            if (_w.Time - _respawnAt > 300 && Vector2.Distance(box.Pos, _me.Pos) > 20) return 0;
            return KitWanted(box) != null ? 64 : 0;
        }

        bool AfterDeathAct(ref InputCmd cmd)
        {
            var box = MyBox();
            var id = KitWanted(box);
            if (id == null) { _respawnAt = -999; return false; }
            int want = KitWant(id);
            if (_w.Time - _respawnAt < 1) Thought("после смерти: домой за набором (оружие, патроны, бинты, броня)");
            Doing = $"{(_w.Time - _respawnAt < 300 ? "после смерти — " : "")}берёт из ящика {Items.Name(id)}";
            if (Clothing.Def(id) != null)
            {
                if (!TakeFromBox(box, id, 1, ref cmd)) { _respawnAt = -999; return false; }
                Say("броня-из-ящика" + id, $"беру из ящика {Items.Name(id)} — лучше того, что на мне", 120);
                return true;
            }
            // ствол в ящике лежит целиком (с износом и магазином) — BotTake его не берёт, забрать ячейку (раньше бот стоял у ящика 5 мин)
            if (BoxWith(id, true) is Deployable gb) { if (!TakeGunFromBox(gb, id, ref cmd)) { _respawnAt = -999; return false; } return true; }
            if (!TakeFromBox(box, id, Math.Min(want, _me.Count(id) + InBox(box, id)), ref cmd)) { _respawnAt = -999; return false; }
            return true;
        }
    }
}
