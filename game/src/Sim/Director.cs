using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>Слух по радио: примерное место (в радиусе Radius), что там, до какого времени актуально.</summary>
    public sealed class Rumor
    {
        public Vector2 Pos;
        public float Radius, Time, Until;
        public string Kind, Text, Source;
    }

    /// <summary>Ивент режиссёра: сброс груза, закрытый ящик. Виден всей карте (маркер), крейт — внутри.</summary>
    public sealed class DirectorEvent
    {
        public int Id;
        public string Kind, Name;
        public Vector2 Pos;
        public float StartsAt, EndsAt;     // сброс: когда приземлится; закрытый ящик: когда откроется (0 — ещё не запущен)
        public bool Open;
        public LootBag Crate;
        public Stash Start;                 // закрытый ящик: «держи E» запускает взлом
        public Vector2 PlaneFrom, PlaneTo;  // самолёт сброса
    }

    /// <summary>
    /// Режиссёр вайпа (docs/design/07-director.md): фаза вайпа, «напряжение», ивенты по расписанию со случайностью
    /// (сброс груза, закрытый ящик на РТ) и слухи по радио — наводки и игроку, и ботам. Сам никого не ведёт за руку:
    /// создаёт поводы, а решают боты по характеру.
    /// </summary>
    public sealed class Director
    {
        public readonly List<Rumor> Rumors = new();
        public readonly List<DirectorEvent> Events = new();
        /// <summary>Сообщения по радио для клиента (последние), с номером — чтобы клиент показывал только новые.</summary>
        public readonly List<(int n, float t, string text)> Radio = new();
        public int Phase;                   // 0 ранний, 1 средний, 2 поздний
        /// <summary>
        /// Тепловая карта «где мясорубка»: клетки по 50 м, выстрелы, попадания, смерти и взрывы греют, со временем остывает
        /// (половина за ~4 мин). Боты читают её как «молву»: осторожные обходят горячее, бешеные идут туда.
        /// </summary>
        public const float HeatCell = 50f;
        public readonly Dictionary<(int, int), float> Heat = new();
        float _heatDecayT, _nextHotRumor = 5 * 60;

        public static (int, int) HeatKey(Vector2 p) => ((int)MathF.Floor(p.X / HeatCell), (int)MathF.Floor(p.Y / HeatCell));
        public float HeatAt(Vector2 p) => Heat.TryGetValue(HeatKey(p), out var h) ? h : 0;

        /// <summary>Самые горячие места сейчас (центр клетки, жар), по убыванию.</summary>
        public List<(Vector2 pos, float heat)> HotSpots(int max = 5)
        {
            var list = new List<(Vector2, float)>();
            foreach (var (k, h) in Heat) if (h > 1) list.Add((new Vector2((k.Item1 + 0.5f) * HeatCell, (k.Item2 + 0.5f) * HeatCell), h));
            list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }

        void AddHeat(Vector2 p, float v)
        {
            var k = HeatKey(p);
            Heat[k] = (Heat.TryGetValue(k, out var h) ? h : 0) + v;
        }
        public float Tension;               // 0..1: что-то недавно происходило
        float _nextAirdrop = 11 * 60, _nextCrate = 24 * 60, _nextRumor = 4 * 60, _quiet;
        int _radioN, _eventId;
        bool _substationPowered;
        readonly Random _rng;
        public Director(int seed) => _rng = new Random(seed * 131 + 7);

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        public void Say(World w, string text)
        {
            Radio.Add((++_radioN, w.Time, text));
            if (Radio.Count > 30) Radio.RemoveAt(0);
            w.Milestones.Add($"[{(int)(w.Time / 60):00}:{(int)(w.Time % 60):00}] Радио — {text}");
        }

        void AddRumor(World w, string kind, Vector2 pos, float radius, float life, string text, string source = null)
        {
            Rumors.Add(new Rumor { Kind = kind, Pos = pos, Radius = radius, Time = w.Time, Until = w.Time + life, Text = text, Source = source });
            Say(w, text);
        }

        public void Step(World w)
        {
            float t = w.Time;
            Rumors.RemoveAll(r => r.Until < t);

            // фаза — по времени и среднему верстаку игроков
            int benches = 0, players = 0;
            foreach (var a in w.Actors)
            {
                if (a.Team != Team.Player || a.Spectator) continue;
                players++;
                foreach (var d in w.Deployables) if (d.OwnerId == a.Id && d.Kind == DeployKind.Workbench) { benches += Math.Max(1, d.Level); break; }
            }
            float avg = players > 0 ? benches / (float)players : 0;
            Phase = t > 60 * 60 && avg >= 1.5f ? 2 : t > 18 * 60 || avg >= 0.8f ? 1 : 0;

            // тепловая карта: выстрелы, попадания, смерти, взрывы
            foreach (var e in w.Events)
                switch (e.Kind)
                {
                    case SimEventKind.Shot: AddHeat(e.Pos, 0.3f); break;
                    case SimEventKind.HitActor: AddHeat(e.Pos, 1f); break;
                    case SimEventKind.Death: AddHeat(e.Pos, 6f); break;
                    case SimEventKind.Explosion: AddHeat(e.Pos, 4f); break;
                }
            if ((_heatDecayT -= World.Dt) <= 0)
            {
                _heatDecayT = 5;
                var keys = new List<(int, int)>(Heat.Keys);
                foreach (var k in keys) { float h = Heat[k] * 0.986f; if (h < 0.05f) Heat.Remove(k); else Heat[k] = h; }
            }
            if ((_nextHotRumor -= World.Dt) <= 0)
            {
                _nextHotRumor = R(3, 5) * 60;
                var hot = HotSpots(1);
                if (hot.Count > 0 && hot[0].heat > 12)
                {
                    var m = w.MonumentAt(hot[0].pos);
                    AddRumor(w, "hot", hot[0].pos, HeatCell * 0.7f, 300, m != null ? $"у РТ «{m.Name}» мясорубка — стреляют без остановки" : "в одном месте карты идёт жёсткая перестрелка (отмечено)");
                }
            }

            // напряжение: громкие звуки и смерти поднимают, тишина опускает
            bool loud = false;
            foreach (var n in w.Noises) if (n.Radius >= 60 && t - n.Time < 0.1f) { loud = true; break; }
            foreach (var e in w.Events) if (e.Kind is SimEventKind.Death or SimEventKind.Explosion) loud = true;
            Tension = Math.Clamp(Tension + (loud ? 0.02f : -World.Dt / 300f), 0, 1);
            _quiet = loud ? 0 : _quiet + World.Dt;

            // Подстанция: щиток включили — слух всем
            bool powered = w.Substations.Exists(s => s.Powered);
            if (powered && !_substationPowered)
            {
                var s = w.Substations.Find(x => x.Powered);
                AddRumor(w, "substation_power", s.M.Center, 30, 240, "на Подстанции кто-то включил щиток — трансформаторы гудят");
            }
            _substationPowered = powered;

            StepEvents(w);

            // расписание: сброс груза, закрытый ящик (со средней фазы), слухи; тишина ускоряет
            float hurry = _quiet > 6 * 60 ? 3f : 1f;
            if ((_nextAirdrop -= World.Dt * hurry) <= 0) { _nextAirdrop = R(15, 24) * 60; StartAirdrop(w); }
            if (Phase >= 1 && (_nextCrate -= World.Dt * hurry) <= 0) { _nextCrate = R(25, 35) * 60; StartLockedCrate(w); }
            if ((_nextRumor -= World.Dt * hurry) <= 0) { _nextRumor = R(3, 6) * 60; MakeRumor(w); }
        }

        // ---------- сброс груза ----------

        Vector2? OpenGround(World w, Vector2 near, float spread)
        {
            for (int i = 0; i < 300; i++)
            {
                float a = R(0, MathF.Tau), r = R(0, spread);
                var p = near + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                if (MathF.Abs(p.X) > World.HalfSize - 30 || MathF.Abs(p.Y) > World.HalfSize - 30) continue;
                if (!w.Terrain.IsLand(p) || w.NearMonument(p, 15) || !w.IsFree(p, 4)) continue;
                bool nearBase = false;
                foreach (var s in w.Structures) if (Vector2.DistanceSquared(s.Center, p) < 60 * 60) { nearBase = true; break; }
                if (!nearBase) return p;
            }
            return null;
        }

        /// <summary>Центр активности: среднее живых игроков и ботов (сброс падает «где все», но не у баз).</summary>
        Vector2 Activity(World w)
        {
            var sum = Vector2.Zero; int n = 0;
            foreach (var a in w.Actors) if (a.Team == Team.Player && !a.Dead && !a.Spectator) { sum += a.Pos; n++; }
            return n > 0 ? sum / n : Vector2.Zero;
        }

        /// <summary>Отладка: сброс груза рядом с точкой, приземлится через secs секунд.</summary>
        public void DebugAirdrop(World w, Vector2 at, float secs)
        {
            float ang = R(0, MathF.Tau);
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            Events.Add(new DirectorEvent { Id = ++_eventId, Kind = "airdrop", Name = "Сброс груза", Pos = at, StartsAt = w.Time + secs, EndsAt = w.Time + secs + 600, PlaneFrom = at - dir * 700, PlaneTo = at + dir * 700 });
        }

        void StartAirdrop(World w)
        {
            var p = OpenGround(w, Activity(w), 220) ?? OpenGround(w, Vector2.Zero, World.HalfSize * 0.7f);
            if (p == null) return;
            float ang = R(0, MathF.Tau);
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            var ev = new DirectorEvent
            {
                Id = ++_eventId, Kind = "airdrop", Name = "Сброс груза", Pos = p.Value, StartsAt = w.Time + 45, EndsAt = w.Time + 45 + 60,
                PlaneFrom = p.Value - dir * 700, PlaneTo = p.Value + dir * 700,
            };
            Events.Add(ev);
            AddRumor(w, "airdrop", p.Value, 25, 600, "слышен самолёт — с него сбросили груз, парашют идёт вниз (на карте отмечено)");
        }

        // ---------- закрытый ящик ----------

        void StartLockedCrate(World w)
        {
            var mons = w.Monuments.FindAll(m => !w.Prefabs.Contains(m) || w.Tier0.Exists(t => t.M == m));
            if (mons.Count == 0) mons = w.Monuments;
            var m = mons[_rng.Next(mons.Count)];
            var p = m.Center;
            for (int i = 0; i < 80; i++)
            {
                var q = new Vector2(R(m.Min.X + 4, m.Max.X - 4), R(m.Min.Y + 4, m.Max.Y - 4));
                if (w.IsFree(q, 1.5f)) { p = q; break; }
            }
            var crate = new LootBag { Id = w.NextId(), Pos = p, IsCrate = true, Tier = 2, Punch = true, Look = "military" };
            w.FillEventCrate(crate, Phase);
            w.Bags.Add(crate);
            var ev = new DirectorEvent { Id = ++_eventId, Kind = "locked_crate", Name = $"Закрытый ящик — {m.Name}", Pos = p, Crate = crate, EndsAt = w.Time + 15 * 60 };
            ev.Start = new Stash { Pos = p + new Vector2(0, 0.9f), Verb = "Запустить взлом закрытого ящика (3 мин)", Sound = "pry", Seconds = 3, Noise = 40 };
            ev.Start.OnDone = a =>
            {
                ev.StartsAt = w.Time + 180;
                ev.EndsAt = w.Time + 180 + 10 * 60;
                AddRumor(w, "locked_crate", p, 15, 400, $"на РТ «{m.Name}» начали ломать закрытый ящик — 3 минуты до открытия");
            };
            w.DirectorStashes.Add(ev.Start);
            Events.Add(ev);
            AddRumor(w, "locked_crate", p, 30, 900, $"на РТ «{m.Name}» привезли закрытый ящик (на карте отмечено)");
        }

        void StepEvents(World w)
        {
            for (int i = Events.Count - 1; i >= 0; i--)
            {
                var ev = Events[i];
                if (ev.Kind == "airdrop")
                {
                    if (ev.Crate == null && w.Time >= ev.StartsAt)
                    {
                        // приземлился: контейнер на месте, но заперт 1,5 мин (горит дым) — сбегутся все; потом вскрывать 10 с, шумно
                        ev.Crate = new LootBag { Id = w.NextId(), Pos = ev.Pos, IsCrate = true, Tier = 2, Punch = true, Look = "airdrop" };
                        w.FillEventCrate(ev.Crate, Phase);
                        w.Bags.Add(ev.Crate);
                        w.Noises.Add(new Noise { Pos = ev.Pos, Radius = 150, SourceId = -1, Time = w.Time });
                        w.Events.Add(new SimEvent { Kind = SimEventKind.Machine, Pos = ev.Pos, Text = "drop" });
                        ev.StartsAt = w.Time + 90;
                        AddRumor(w, "airdrop", ev.Pos, 25, 600, "груз приземлился — контейнер заперт, через полторы минуты можно вскрывать");
                    }
                    else if (ev.Crate != null && !ev.Open && ev.Start == null && w.Time >= ev.StartsAt)
                    {
                        // замок «остыл»: теперь вскрыть — держать E 10 с, слышно далеко
                        var drop = ev;
                        drop.Start = new Stash { Pos = drop.Pos + new Vector2(0, 0.9f), Verb = "Вскрыть контейнер сброса (10 с, громко)", Sound = "pry", Seconds = 10, Noise = 60 };
                        drop.Start.OnDone = a =>
                        {
                            drop.Open = true;
                            drop.Crate.Unlocked = true;
                            drop.EndsAt = w.Time + 8 * 60;
                            w.Events.Add(new SimEvent { Kind = SimEventKind.Machine, Pos = drop.Pos, Text = "crate" });
                        };
                        w.DirectorStashes.Add(drop.Start);
                        ev.EndsAt = w.Time + 10 * 60;
                    }
                }
                else if (ev.Kind == "locked_crate")
                {
                    if (ev.StartsAt > 0 && !ev.Open)
                    {
                        if ((int)(w.Time * 2) % 4 == 0) w.Noises.Add(new Noise { Pos = ev.Pos, Radius = 100, SourceId = -1, Time = w.Time });
                        if (w.Time >= ev.StartsAt)
                        {
                            ev.Open = true;
                            ev.Crate.Unlocked = true;
                            w.Events.Add(new SimEvent { Kind = SimEventKind.Machine, Pos = ev.Pos, Text = "crate" });
                            Say(w, $"{ev.Name}: открыт!");
                        }
                    }
                }
                bool emptied = ev.Crate != null && ev.Open && ev.Crate.Empty;
                if (w.Time > ev.EndsAt || emptied)
                {
                    if (ev.Crate != null) w.Bags.Remove(ev.Crate);
                    if (ev.Start != null) w.DirectorStashes.Remove(ev.Start);
                    Events.RemoveAt(i);
                }
            }
        }

        // ---------- слухи ----------

        void MakeRumor(World w)
        {
            var options = new List<Action>();
            // богатая база: самый полный ящик (видно по «молве»)
            Deployable rich = null; int richN = 0;
            foreach (var d in w.Deployables)
                if (d.Kind == DeployKind.Box && d.Storage != null && d.Storage.Items.Count > richN) { richN = d.Storage.Items.Count; rich = d; }
            if (rich != null && richN >= 6)
                options.Add(() => AddRumor(w, "rich_loot", rich.Pos, 45, 900, "говорят, у одной базы ящики ломятся от лута (примерно отмечено)"));
            // слабая база: дверь из дерева или каркаса
            var weak = w.Structures.FindAll(s => s.Kind == StructKind.Door && s.Tier <= 1);
            if (weak.Count > 0 && Phase >= 1)
            {
                var d = weak[_rng.Next(weak.Count)];
                options.Add(() => AddRumor(w, "weak_base", d.Center, 40, 900, "говорят, есть база с деревянными дверями — легко вскрыть"));
            }
            // кто-то с полным рюкзаком у РТ
            foreach (var a in w.Actors)
            {
                if (a.Team != Team.Player || a.Dead || a.Spectator || a.Count("scrap") < 150) continue;
                var m = w.MonumentAt(a.Pos);
                if (m == null) continue;
                var who = a;
                options.Add(() => AddRumor(w, "fight", who.Pos, 35, 240, $"у РТ «{m.Name}» кто-то ходит с полным рюкзаком"));
                break;
            }
            // перфокарты: подсказка новичкам
            if (Phase == 0 && w.Tier0.Count > 0)
            {
                var t0 = w.Tier0[_rng.Next(w.Tier0.Count)];
                options.Add(() => AddRumor(w, "punchcard", t0.M.Center, 40, 600, $"на «{t0.M.Name}» ещё можно найти перфокарту"));
            }
            if (options.Count > 0) options[_rng.Next(options.Count)]();
        }
    }
}
