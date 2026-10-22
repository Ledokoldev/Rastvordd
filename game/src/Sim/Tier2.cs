using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace Rastvordd.Sim
{
    /// <summary>
    /// РТ тира 2 по макетам (scripts/mock-tier2.py → Tier2Layouts): Радиоцентр «Маяк» (docs/design/11-monuments.md, D50).
    /// Вызов груза: журнал дежурного (личный код) → дизель (ток на 10 мин) → пульт: код + перфокарта II →
    /// красная ракета у площадки, слух по радио → через 90 с Ан-2 сбрасывает контейнер РОВНО на площадку (заперт 30 с, вскрыть 10 с).
    /// Перезарядка РТ — 20 мин. Радиация 30 по всей территории.
    /// </summary>
    public sealed class Tier2Rt
    {
        public const float RefreshTime = 1200f, PowerTime = 600f, CallDelay = 90f, DropLock = 30f, Radiation = 30f;
        public string Key;
        public Monument M;
        public Vector2 O;
        public int ConsoleId;                       // «дверь» для окна ввода кода (отрицательный id, Hud.ShowCode)
        public readonly List<Stash> Stashes = new();
        public readonly List<Stash> Racks = new();
        public Stash Journal, Diesel, Console;
        public readonly List<Stash> GateStashes = new();
        public MapWall SideGate;
        public Vector2 PadPos, FlarePos, DieselPos;
        public float PowerLeft, CooldownLeft, RefreshLeft, _noiseT;
        /// <summary>Личные коды из журнала: кто прочёл — свой код (код подходит любой выданный: можно продиктовать товарищу).</summary>
        public readonly Dictionary<int, int> Codes = new();
        public readonly HashSet<int> CodeOk = new();
        public DirectorEvent Called;
        public bool Powered => PowerLeft > 0;
        public Vector2 At(float x, float y) => O + new Vector2(x, y);
    }

    public sealed partial class World
    {
        public readonly List<Tier2Rt> Tier2 = new();

        void PlaceTier2()
        {
            foreach (var (key, name, json, seed) in new[]
            {
                ("radio", "Радиоцентр «Маяк»", Tier2Layouts.Radio, 71),
            })
            {
                if (string.IsNullOrEmpty(json)) continue;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                float w = root.GetProperty("W").GetSingle(), h = root.GetProperty("H").GetSingle(), margin = root.GetProperty("M").GetSingle();
                var rng = new Random(Seed * 7919 + seed);
                var size = new Vector2(w, h) + new Vector2(margin * 2);
                var spot = Tier2Spot(rng, size);
                if (spot == null) { Milestones.Add($"РТ «{name}» не поместилась на остров ({w}×{h} м)"); continue; }
                var rt = new Tier2Rt { Key = key, O = spot.Value + new Vector2(margin), ConsoleId = -1000 - Tier2.Count };
                rt.M = new Monument { Name = name, Min = rt.O, Max = rt.O + new Vector2(w, h) };
                Monuments.Add(rt.M);
                Prefabs.Add(rt.M);
                ClearPrefabArea(new Monument { Min = rt.M.Min - new Vector2(margin), Max = rt.M.Max + new Vector2(margin) });
                _prefabMode = true;
                BuildTier2(rt, root, rng);
                _prefabMode = false;
                Tier2.Add(rt);
            }
        }

        /// <summary>
        /// Место под большую РТ: сначала как у остальных (биом, отступы от РТ), потом с меньшими отступами, потом — лучшее
        /// место «сколько суши» по сетке острова (остров 1×1 км мал для 190×146 м; на 2×2 км хватит первого шага).
        /// </summary>
        Vector2? Tier2Spot(Random rng, Vector2 size)
        {
            var biomes = new[] { Biome.Grassland, Biome.Forest, Biome.Arid, Biome.Tundra };
            float half = HalfSize - size.Length() * 0.5f - 10;
            if (half > 40)
                foreach (float gap in new[] { 110f, 60f, 25f })
                {
                    var s = FindPrefabSpot(rng, size, biomes, MathF.Min(HalfSize * 0.15f, half * 0.5f), half, gap);
                    if (s != null) return s;
                }
            // запасной путь: перебор по сетке — больше суши без пляжа и дальше от других РТ
            Vector2? best = null;
            float bestScore = float.MinValue;
            for (float cx = -HalfSize + size.X * 0.5f; cx <= HalfSize - size.X * 0.5f; cx += 20)
                for (float cy = -HalfSize + size.Y * 0.5f; cy <= HalfSize - size.Y * 0.5f; cy += 20)
                {
                    var o = new Vector2(cx, cy) - size * 0.5f;
                    int land = 0, n = 0;
                    for (float x = 0; x <= size.X; x += 8)
                        for (float y = 0; y <= size.Y; y += 8)
                        {
                            var p = o + new Vector2(x, y);
                            n++;
                            if (Terrain.IsLand(p) && Terrain.BiomeAt(p) != Biome.Beach) land++;
                        }
                    float score = land / (float)n * 100;
                    var c = o + size * 0.5f;
                    foreach (var m in Monuments)
                    {
                        var mh = (m.Max - m.Min) * 0.5f + size * 0.5f;
                        var d = Vector2.Abs(m.Center - c);
                        if (d.X < mh.X + 5 && d.Y < mh.Y + 5) score -= 200;   // наезд на другую РТ
                        else score += MathF.Min(Vector2.Distance(m.Center, c), 150) * 0.02f;
                    }
                    if (score > bestScore) { bestScore = score; best = o; }
                }
            if (best != null) Milestones.Add($"Радиоцентр: запасное место на острове (оценка {bestScore:0})");
            return best;
        }

        void BuildTier2(Tier2Rt rt, JsonElement root, Random rng)
        {
            // земля, стены, крыши, пропсы, лут — общим строителем тира 0
            int bagsFrom = Bags.Count;
            var tmp = new Tier0Rt { O = rt.O, M = rt.M };
            BuildFromLayout(tmp, "t2", root, rng);
            // ящики РТ: военные — тир 2, деревянные — тир 1
            var military = new List<Vector2>();
            foreach (var pr in root.GetProperty("props").EnumerateArray())
                if (pr[0].GetString().EndsWith("crate_military")) military.Add(rt.O + V(pr, 1));
            for (int i = bagsFrom; i < Bags.Count; i++)
            {
                var b = Bags[i];
                if (!b.IsCrate || b.Safe || b.Punch) continue;
                b.Tier = military.Exists(q => Vector2.DistanceSquared(q, b.Pos) < 0.25f) ? 2 : 1;
                FillCrate(b);
            }
            Vector2 P(JsonElement mk) => rt.O + new Vector2(mk[0].GetSingle(), mk[1].GetSingle());
            var marks = new List<(string label, Vector2 p)>();
            foreach (var mk in root.GetProperty("marks").EnumerateArray())
            {
                string label = mk[2].GetString();
                if (label != null && label.StartsWith("MECH:")) marks.Add((label[5..], P(mk)));
            }
            Vector2? Mark(string k) { var m = marks.Find(x => x.label == k); return m.label != null ? m.p : null; }
            List<Vector2> Marks(string prefix) => marks.FindAll(x => x.label.StartsWith(prefix)).ConvertAll(x => x.p);

            // охрана: MECH:guard:<pps|svt|grach>
            foreach (var (label, p) in marks)
                if (label.StartsWith("guard"))
                {
                    NpcHomes[-NpcHomes.Count - 1] = p;
                    NpcKit[p] = label.Length > 6 ? label[6..] : "pps";
                }

            rt.PadPos = Mark("pad") ?? rt.M.Center;
            rt.FlarePos = Mark("flare") ?? rt.PadPos;

            // упавшая мачта: поворотное украшение от основания к вершине, твёрдые только куски (под ней — пролом в заборе)
            if (Mark("fallen_base") is Vector2 fb && Mark("fallen_top") is Vector2 ft)
            {
                var d = Decor("props/radio_mast_fallen", fb);
                d.Pivot = true;
                d.Rot = MathF.Atan2(ft.Y - fb.Y, ft.X - fb.X) * 180f / MathF.PI;
                foreach (var (t0, t1) in new[] { (0f, 0.1f), (0.28f, 0.36f), (0.52f, 0.6f) })
                {
                    var a = Vector2.Lerp(fb, ft, t0); var b = Vector2.Lerp(fb, ft, t1);
                    AddHiddenWall(MathF.Min(a.X, b.X) - 0.5f, MathF.Min(a.Y, b.Y) - 0.5f, MathF.Max(a.X, b.X) + 0.5f, MathF.Max(a.Y, b.Y) + 0.5f, 1.4f);
                }
            }
            // растяжки мачт и фидеры диполей — провода (только картинка)
            foreach (var mp in Marks("mast"))
                foreach (var off in new[] { new Vector2(-11, 6), new Vector2(11, 6), new Vector2(0, -12) })
                {
                    var q = mp + off;
                    if (Decors.Exists(x => x.Sprite.EndsWith("concrete_barrier") && Vector2.Distance(x.Pos, q) < 1.5f)) Wire(mp, 19f, q, 0.4f, 0.3f);   // только к анкерам-блокам
                }
            var dip = Decors.FindAll(x => x.Sprite == "props/radio_dipole" && rt.M.Contains(x.Pos));
            dip.Sort((a, b) => a.Pos.Y != b.Pos.Y ? a.Pos.Y.CompareTo(b.Pos.Y) : a.Pos.X.CompareTo(b.Pos.X));
            for (int i = 1; i < dip.Count; i++)
                if (MathF.Abs(dip[i].Pos.Y - dip[i - 1].Pos.Y) < 0.5f) Wire(dip[i - 1].Pos, 3.6f, dip[i].Pos, 3.6f, 0.6f);

            // сейф начальника — карта доступа (как на складе)
            if (Mark("safe") is Vector2 sp) AddSafe(sp);

            // 1. журнал дежурного: личный код
            rt.Journal = new Stash { Pos = Mark("journal") ?? rt.M.Center, Verb = "Листать журнал дежурного", Sound = "paper", Seconds = 3, Noise = 5, Repeatable = true };
            rt.Journal.OnDone = a =>
            {
                if (!rt.Codes.TryGetValue(a.Id, out int code)) rt.Codes[a.Id] = code = Rng.Next(1000, 10000);
                Info(a, $"В журнале дежурного — код вызова борта: {code}. Нужен ток (дизель) и перфокарта II у пульта");
            };
            rt.Stashes.Add(rt.Journal);

            // 2. дизель: ток корпусу на 10 минут, громко
            rt.DieselPos = Mark("diesel") ?? rt.M.Center;
            rt.Diesel = new Stash { Pos = rt.DieselPos, Verb = "Запустить дизель", Sound = "diesel", Seconds = 5, Noise = 60, Repeatable = true };
            rt.Diesel.Require = a => rt.Powered ? $"Дизель уже молотит — тока ещё на {MathF.Ceiling(rt.PowerLeft / 60)} мин" : null;
            rt.Diesel.OnDone = a =>
            {
                rt.PowerLeft = Tier2Rt.PowerTime;
                rt.RefreshLeft = MathF.Max(rt.RefreshLeft, Tier2Rt.PowerTime);
                PrefabVersion++;
                Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = a.Id, Pos = rt.DieselPos, Text = "power" });
                Info(a, "Дизель завёлся — корпус под током 10 минут. Его слышно далеко");
            };
            rt.Stashes.Add(rt.Diesel);

            // 3. пульт вызова: ток + код (окно ввода) + перфокарта II
            rt.Console = new Stash { Pos = Mark("console") ?? rt.M.Center, Verb = "Провести перфокарту II через пульт", Sound = "reader", Seconds = 2, Noise = 25, Repeatable = true };
            rt.Console.Require = a =>
            {
                if (rt.CooldownLeft > 0) return rt.Called != null && Director?.Events.Contains(rt.Called) == true
                    ? "Борт уже вызван — груз летит на площадку" : $"Пульт перегрелся — вызвать снова через {MathF.Ceiling(rt.CooldownLeft / 60)} мин";
                if (!rt.Powered) return "Пульт мёртвый — нет тока. Запусти дизель (юго-восток)";
                if (a.Count("punchcard_2") <= 0) return "Пульт просит перфокарту II (крейты РТ тира 1)";
                if (!rt.CodeOk.Contains(a.Id))
                {
                    Events.Add(new SimEvent { Kind = SimEventKind.Locked, ActorId = a.Id, OtherId = rt.ConsoleId, Pos = rt.Console.Pos });
                    return "Пульт просит код дежурного (он в журнале дежурки)";
                }
                return null;
            };
            rt.Console.OnDone = a =>
            {
                if (a.Take("punchcard_2", 1) == 0) { Info(a, "Перфокарта II пропала из рук"); return; }
                CallRadioDrop(rt, a);
            };
            rt.Stashes.Add(rt.Console);

            // стойки передатчика: разобрать — радиолампы, радиодетали, иногда золотые контакты (ценный лом тира 2, D49)
            foreach (var rp in Marks("rack"))
            {
                var st = new Stash { Pos = rp, Verb = "Разобрать стойку передатчика", Sound = "pry", Seconds = 4, Noise = 20 };
                st.OnDone = a =>
                {
                    var got = new List<string>();
                    void Give(string id, int n) { if (n <= 0) return; a.Add(id, n); got.Add($"{Items.Name(id)} ×{n}"); Economy.BookTake("стойка передатчика", id, n); }
                    Give("val_radio_parts", Rng.Next(1, 4));
                    if (Rng.NextDouble() < 0.6) Give("val_radio_tubes", Rng.Next(1, 3));
                    if (Rng.NextDouble() < 0.15) Give("val_gold_contacts", 1);
                    Events.Add(new SimEvent { Kind = SimEventKind.Gather, ActorId = a.Id, Pos = rp, Value = 1, Text = "Стойка" });
                    Info(a, "Из стойки: " + string.Join(", ", got));
                    if (rt.RefreshLeft <= 0) rt.RefreshLeft = Tier2Rt.RefreshTime;
                };
                rt.Racks.Add(st);
                rt.Stashes.Add(st);
            }

            // калитка у дизельной: заперта, выбить 4 с (громко), с обеих сторон
            if (Mark("sidegate") is Vector2 gp)
            {
                MapWall gate = null; float bd = 4;
                foreach (var w in Walls)
                {
                    if (!w.Prefab || w.Tex != "walls/wallface_door_metal") continue;
                    float d = Vector2.Distance((w.Wall.Min + w.Wall.Max) * 0.5f, gp);
                    if (d < bd) { bd = d; gate = w; }
                }
                if (gate != null)
                {
                    rt.SideGate = gate;
                    RemovableWalls.Add(gate);
                    var c = (gate.Wall.Min + gate.Wall.Max) * 0.5f;
                    foreach (var side in new[] { -0.9f, 0.9f })
                    {
                        var st = new Stash { Pos = c + new Vector2(side, 0), Verb = "Выбить запертую калитку", Sound = "bang", Seconds = 4, Noise = 50 };
                        st.OnDone = a =>
                        {
                            if (Walls.Remove(gate)) _wallGrid.Remove(gate);
                            foreach (var o in rt.GateStashes) o.Done = true;
                            PrefabVersion++;
                            if (rt.RefreshLeft <= 0) rt.RefreshLeft = Tier2Rt.RefreshTime;
                            Info(a, "Калитка выбита");
                        };
                        rt.GateStashes.Add(st);
                        rt.Stashes.Add(st);
                    }
                }
            }
        }

        /// <summary>Пульт: введён код (Hud.ShowCode с отрицательным id). Верно — сразу проводим перфокарту.</summary>
        void EnterConsoleCode(Actor a, int consoleId, int code)
        {
            var rt = Tier2.Find(r => r.ConsoleId == consoleId);
            if (rt?.Console == null || Vector2.Distance(a.Pos, rt.Console.Pos) > 3f) return;
            if (rt.Codes.ContainsValue(code))
            {
                rt.CodeOk.Add(a.Id);
                Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = a.Id, Pos = rt.Console.Pos, Text = "beep" });
                Info(a, "Код принят — проводи перфокарту II");
                UseStash(a, rt.Console);
            }
            else
            {
                Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = a.Id, Pos = rt.Console.Pos, Text = "beep" });
                Noises.Add(new Noise { Pos = rt.Console.Pos, Radius = 20, SourceId = a.Id, Time = Time });
                Info(a, "Неверный код — пульт пищит. Код в журнале дежурного");
            }
        }

        /// <summary>Вызов борта: ракета над площадкой, слух всей карте, через 90 с Ан-2 бросает контейнер на площадку.</summary>
        void CallRadioDrop(Tier2Rt rt, Actor a)
        {
            rt.CooldownLeft = Tier2Rt.RefreshTime;
            rt.RefreshLeft = Tier2Rt.RefreshTime;
            Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = a.Id, Pos = rt.FlarePos, Text = "flare" });
            Noises.Add(new Noise { Pos = rt.FlarePos, Radius = 200, SourceId = a.Id, Time = Time });
            Milestone(a, $"вызвал борт на «{rt.M.Name}»");
            Info(a, "Борт вызван! Ракета ушла — теперь все знают. Груз на площадку через 90 секунд");
            rt.Called = Director?.CallDrop(this, rt.PadPos, Tier2Rt.CallDelay, Tier2Rt.DropLock, FillRadioDrop,
                $"«{rt.M.Name}» вызвал борт — груз через 90 секунд (красная ракета, на карте отмечено)");
        }

        /// <summary>Контейнер «Маяка»: детали тира 2–3, шанс ствола тира 2 (редко 3), радиолампы и контакты, малый шанс перфокарты III.</summary>
        internal void FillRadioDrop(LootBag bag)
        {
            bag.Items.Clear();
            bag.Search.Clear();
            bag.Unlocked = false;
            bag.Src = "вызов «Маяка»";
            bag.Items.Add(new ItemSlot("scrap", Rng.Next(25, 46)));
            bag.Items.Add(new ItemSlot(Weaponry.Tier2Part(Rng), 1));
            if (Rng.NextDouble() < 0.6) bag.Items.Add(new ItemSlot(Weaponry.Tier2Part(Rng), 1));
            TryTier3(bag, 0.6);
            bag.Items.Add(new ItemSlot("val_radio_tubes", Rng.Next(1, 4)));
            if (Rng.NextDouble() < 0.45) bag.Items.Add(new ItemSlot("val_gold_contacts", Rng.Next(1, 3)));
            if (Rng.NextDouble() < 0.4) bag.Items.Add(Items.NewSlot(Pick(new[] { "weapon_pps43", "weapon_svt40", "weapon_grach", "weapon_mp133" })));
            else if (Rng.NextDouble() < 0.06) bag.Items.Add(Items.NewSlot(Pick(new[] { "weapon_akm", "weapon_ppsh", "weapon_svd" })));
            bag.Items.Add(new ItemSlot(Rng.Next(2) == 0 ? "ammo_762" : "ammo_9mm", Rng.Next(20, 50)));
            if (Rng.NextDouble() < 0.5) bag.Items.Add(new ItemSlot("comp_oil", Rng.Next(1, 3)));
            if (Rng.NextDouble() < 0.35) bag.Items.Add(new ItemSlot(Rng.Next(2) == 0 ? "comp_lens" : "comp_circuit", 1));
            if (Rng.NextDouble() < 0.3) bag.Items.Add(new ItemSlot(Pick(Weaponry.ArmyAtts), 1));
            if (Rng.NextDouble() < 0.07) bag.Items.Add(new ItemSlot("punchcard_3", 1));
            Clothing.Loot(WearRng, bag, "event");
        }

        void StepTier2()
        {
            foreach (var rt in Tier2)
            {
                foreach (var s in rt.Stashes) StepStash(s);
                rt._noiseT -= Dt;
                bool tick = rt._noiseT <= 0;
                if (tick) rt._noiseT = 1f;
                if (rt.PowerLeft > 0)
                {
                    rt.PowerLeft -= Dt;
                    if (tick)
                    {
                        Noises.Add(new Noise { Pos = rt.DieselPos, Radius = 60, SourceId = -1, Time = Time });
                        if ((int)Time % 2 == 0) Events.Add(new SimEvent { Kind = SimEventKind.Machine, Pos = rt.DieselPos, Text = "diesel" });
                    }
                    if (rt.PowerLeft <= 0) { rt.PowerLeft = 0; PrefabVersion++; Events.Add(new SimEvent { Kind = SimEventKind.Machine, Pos = rt.DieselPos, Text = "power_off" }); }
                }
                if (rt.CooldownLeft > 0) rt.CooldownLeft -= Dt;
                if (rt.RefreshLeft > 0 && (rt.RefreshLeft -= Dt) <= 0)
                {
                    if (Actors.Exists(a => !a.Dead && a.Team == Team.Player && rt.M.Contains(a.Pos, 4))) { rt.RefreshLeft = 60; continue; }
                    ResetTier2(rt);
                }
            }
        }

        void ResetTier2(Tier2Rt rt)
        {
            foreach (var s in rt.Stashes) { s.Done = false; s.ActorId = 0; s.Progress = 0; }
            rt.Codes.Clear();
            rt.CodeOk.Clear();
            rt.PowerLeft = 0;
            if (rt.SideGate != null && !Walls.Contains(rt.SideGate)) { Walls.Add(rt.SideGate); _wallGrid.Insert(rt.SideGate, rt.SideGate.Wall.Min, rt.SideGate.Wall.Max); }
            PrefabVersion++;
        }

        public bool AnyTier2Powered => Tier2.Exists(r => r.Powered);

        /// <summary>Отладка (--do=…:t2call): вызвать борт сразу, без журнала, дизеля и перфокарты.</summary>
        public void DebugCallRadio(Tier2Rt rt, Actor a) { if (a != null) CallRadioDrop(rt, a); }
    }
}
