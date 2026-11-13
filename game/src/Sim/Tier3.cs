using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// РТ тира 3 «РЛС „Дуга“» (D61, docs/design/30-tier3-rt.md): загоризонтная станция на острове — корпус управления
    /// в 2 этажа с атриумом машинного зала, галерея антенны (3-й уровень), трансформаторный двор, КПП, кабельная траншея к болоту.
    /// Корпус собран из неразрушимых построек (Structure.Static) на этажах D60. Главный ящик — цепочка: 2 рубильника (ток) →
    /// щиток на галерее → прогрев пульта 120 с → крейт с лутом тира 3.
    /// </summary>
    public sealed class Tier3Rt
    {
        public const float RefreshTime = 1800f, WarmTime = 120f;
        public Monument M;
        public Vector2 O;                  // угол РТ (кратен клетке 2 м)
        public int BI, BJ;                 // клетка северо-западного угла корпуса
        public const int BW = 12, BH = 8;  // корпус 24 × 16 м
        public readonly List<Stash> Stashes = new();
        public Stash Switch1, Switch2, Shield, Console;
        public LootBag Crate;
        public Vector2 CratePos, ConsolePos;
        public bool Power1, Power2, ShieldOn;
        public float WarmLeft = -1;        // идёт прогрев (> 0), −1 — не запущен
        public float RefreshLeft;
        public float _noiseT;
        public bool Powered => Power1 && Power2;
        public Vector2 Cell(float i, float j) => new((BI + i + 0.5f) * Building.Cell, (BJ + j + 0.5f) * Building.Cell);
        public Vector2 At(float x, float y) => O + new Vector2(x, y);
    }

    public sealed partial class World
    {
        public Tier3Rt Duga;
        /// <summary>Этаж дома охранника (по точке дома): охрана на 2-м этаже и галерее (D61).</summary>
        readonly Dictionary<Vector2, int> NpcHomeFloor = new();

        void PlaceTier3()
        {
            const float W = 100, H = 80, margin = 10;
            var rng = new Random(Seed * 7919 + 313);
            var spot = Tier2Spot(rng, new Vector2(W, H) + new Vector2(margin * 2));
            if (spot == null) { Milestones.Add("РТ «РЛС „Дуга“» не поместилась на остров"); return; }
            var o = spot.Value + new Vector2(margin);
            o = new Vector2(MathF.Floor(o.X / Building.Cell) * Building.Cell, MathF.Floor(o.Y / Building.Cell) * Building.Cell);
            var rt = new Tier3Rt { O = o };
            rt.M = new Monument { Name = "РЛС «Дуга»", Key = "duga", Min = o, Max = o + new Vector2(W, H) };
            rt.BI = (int)MathF.Round((o.X + 38) / Building.Cell);
            rt.BJ = (int)MathF.Round((o.Y + 30) / Building.Cell);
            // площадка: убрать лес, узлы, чужие стены и находки (РТ ставится после дорог и ориентиров)
            var area = new Monument { Min = rt.M.Min - new Vector2(margin), Max = rt.M.Max + new Vector2(margin) };
            ClearPrefabArea(area);
            Decors.RemoveAll(d => area.Contains(d.Pos));
            Monuments.Add(rt.M);
            Prefabs.Add(rt.M);
            _prefabMode = true;
            BuildDuga(rt, rng);
            _prefabMode = false;
            Duga = rt;
        }

        // ---------- неразрушимые постройки РТ ----------

        Structure StaticPlate(int i, int j, int floor, StructKind kind = StructKind.Foundation, int tier = 2, int dir = -1)
        {
            using var ctx = Ctx(floor);
            var s = new Structure { Id = NextId(), Kind = kind, I = i, J = j, Dir = dir, Tier = tier, OwnerId = -1, Floor = floor, Static = true };
            s.Hp = s.MaxHp;
            _foundations[(i, j)] = s;
            Structures.Add(s);
            CountFloorEdges(s, +1, false);
            RebuildBoxes(s);
            return s;
        }

        Structure StaticEdge(Vector2 a, Vector2 b, StructKind kind, int floor, int tier = 2)
        {
            using var ctx = Ctx(floor);
            long key = Building.EdgeKey(a, b);
            if (_edges.ContainsKey(key)) return _edges[key];
            var s = new Structure { Id = NextId(), Kind = kind, Dir = -1, Tier = tier, OwnerId = -1, Floor = floor, Static = true, A = a, B = b };
            s.Hp = s.MaxHp;
            _edges[key] = s;
            Structures.Add(s);
            RebuildBoxes(s);
            return s;
        }

        void BuildDuga(Tier3Rt rt, Random rng)
        {
            const float C = Building.Cell;
            int bi = rt.BI, bj = rt.BJ, W = Tier3Rt.BW, H = Tier3Rt.BH;
            // рёбра клетки (i, j) корпуса: север/юг/запад/восток
            (Vector2, Vector2) N(int i, int j) => (new Vector2((bi + i) * C, (bj + j) * C), new Vector2((bi + i + 1) * C, (bj + j) * C));
            (Vector2, Vector2) S(int i, int j) => N(i, j + 1);
            (Vector2, Vector2) Wv(int i, int j) => (new Vector2((bi + i) * C, (bj + j) * C), new Vector2((bi + i) * C, (bj + j + 1) * C));
            (Vector2, Vector2) E(int i, int j) => Wv(i + 1, j);
            void Edge((Vector2 a, Vector2 b) e, StructKind k, int f, int tier = 2) => StaticEdge(e.a, e.b, k, f, tier);

            // площадка: гравий, двор КПП — грунт
            Patches.Add(new MapPatch { Tile = "ground_gravel", Min = rt.M.Min, Max = rt.M.Max });
            Patches.Add(new MapPatch { Tile = "ground_dirt", Min = rt.At(40, 62), Max = rt.At(60, 80) });

            // ---- земля (этаж 0) ----
            for (int i = 0; i < W; i++) for (int j = 0; j < H; j++) StaticPlate(bi + i, bj + j, 0);
            int door = 5, sideJ = 3, westJ = 4;   // парадный (юг), боковой (восток), к траншее (запад)
            for (int i = 0; i < W; i++)
            {
                Edge(N(i, 0), StructKind.Wall, 0);
                Edge(S(i, H - 1), i == door ? StructKind.Doorway : i is 2 or 9 ? StructKind.Window : StructKind.Wall, 0);
            }
            for (int j = 0; j < H; j++)
            {
                Edge(Wv(0, j), j == westJ ? StructKind.Doorway : StructKind.Wall, 0);
                Edge(E(W - 1, j), j == sideJ ? StructKind.Doorway : j == 6 ? StructKind.Window : StructKind.Wall, 0);
                Edge(Wv(4, j), j is 2 or 5 ? StructKind.Doorway : StructKind.Wall, 0);   // перегородка: коридор | машинный зал
            }

            // ---- 2-й этаж (этаж 1): перекрытие, кроме атриума машзала и пролёта лестницы А ----
            bool Atrium(int i, int j) => i >= 6 && i <= 9 && j >= 2 && j <= 5;
            (int i, int j) stairA = (1, 5), stairB = (8, 0), hatch = (4, 1);
            for (int i = 0; i < W; i++)
                for (int j = 0; j < H; j++)
                {
                    if (Atrium(i, j) || (i, j) == stairA) continue;
                    StaticPlate(bi + i, bj + j, 1, (i, j) == hatch ? StructKind.Hatch : StructKind.Foundation);
                }
            StaticPlate(bi + stairA.i, bj + stairA.j, 1, StructKind.Stairs, 2, 3);   // лестница А: с земли на 2-й, подъём на север
            for (int i = 0; i < W; i++)
            {
                Edge(N(i, 0), i is 2 or 10 ? StructKind.Window : StructKind.Wall, 1);
                Edge(S(i, H - 1), i is 1 or 3 or 6 or 8 or 10 ? StructKind.Window : StructKind.Wall, 1);
            }
            for (int j = 0; j < H; j++)
            {
                Edge(Wv(0, j), j == 2 ? StructKind.Window : StructKind.Wall, 1);
                Edge(E(W - 1, j), j is 1 or 5 ? StructKind.Window : StructKind.Wall, 1);
                if (!Atrium(4, j)) Edge(Wv(4, j), j == 6 ? StructKind.Doorway : StructKind.Wall, 1);   // аппаратная | остальное
            }
            // пульт ДО (север, i 4..11, j 0..1): с юга — стена с проёмами, над атриумом — парапет (смотреть вниз в зал)
            for (int i = 4; i < W; i++)
                Edge(N(i, 2), i is 6 or 7 or 8 or 9 ? StructKind.LowWall : i is 5 or 10 ? StructKind.Doorway : StructKind.Wall, 1);
            // ограждение атриума с остальных сторон
            for (int j = 2; j <= 5; j++) { Edge(Wv(6, j), StructKind.LowWall, 1); Edge(E(9, j), StructKind.LowWall, 1); }
            for (int i = 6; i <= 9; i++) Edge(S(i, 5), StructKind.LowWall, 1);
            // кабинет начальника (юго-восток, i 10..11, j 5..7)
            for (int j = 5; j < H; j++) Edge(Wv(10, j), j == 6 ? StructKind.Doorway : StructKind.Wall, 1);
            for (int i = 10; i < W; i++) Edge(N(i, 5), StructKind.Wall, 1);

            // ---- галерея антенны (этаж 2): полоса над северным краем корпуса, лестница Б из пульта ----
            StaticPlate(bi + stairB.i, bj + stairB.j, 2, StructKind.Stairs, 2, 3);
            for (int i = 0; i < W; i++) StaticPlate(bi + i, bj - 1, 2, StructKind.Foundation, 3);
            for (int i = 0; i < W; i++)
            {
                Edge(N(i, -1), StructKind.LowWall, 2, 3);
                if (i != stairB.i) Edge(S(i, -1), StructKind.LowWall, 2, 3);
            }
            Edge(Wv(0, -1), StructKind.LowWall, 2, 3);
            Edge(E(W - 1, -1), StructKind.LowWall, 2, 3);

            // ---- стена антенн (картинка Codex, D61): секции 13,4 м к северу от корпуса; твёрдые — бетонные опоры ----
            for (float x = 4; x + 13.4f <= 98; x += 13.4f)
            {
                var p = rt.At(x + 6.7f, 22);
                Decor("props/duga_antenna_wall", p);
                for (int k = 0; k < 5; k++) { float fx = p.X - 6.2f + k * 3.1f; AddHiddenWall(fx - 0.5f, p.Y - 0.8f, fx + 0.5f, p.Y, 6f); }
            }

            // ---- забор: бетон, КПП на юге, пролом на западе (к болоту) ----
            float t = 0.35f, fh = 2.6f;
            var o = rt.O;
            AddWall(o.X, o.Y, o.X + 100, o.Y + t, fh, WallKind.Concrete);                       // север
            AddWall(o.X, o.Y + 80 - t, o.X + 44, o.Y + 80, fh, WallKind.Concrete);              // юг до КПП
            AddWall(o.X + 56, o.Y + 80 - t, o.X + 100, o.Y + 80, fh, WallKind.Concrete);        // юг после КПП
            AddWall(o.X, o.Y, o.X + t, o.Y + 36, fh, WallKind.Concrete);                        // запад до пролома
            AddWall(o.X, o.Y + 42, o.X + t, o.Y + 80, fh, WallKind.Concrete);                   // запад после пролома
            AddWall(o.X + 100 - t, o.Y, o.X + 100, o.Y + 80, fh, WallKind.Concrete);            // восток
            Prop("props/subst_kpp", rt.At(43, 80));
            Decor("props/fence_panels_fallen", rt.At(2, 39));
            // кабельная траншея: низкие стенки от пролома к западному входу корпуса
            float ty0 = (bj + westJ) * C - 0.4f, ty1 = (bj + westJ + 1) * C + 0.4f;
            AddWall(o.X + 1, ty0 - 0.3f, bi * C - 0.1f, ty0, 1.0f, WallKind.Concrete);
            AddWall(o.X + 1, ty1, bi * C - 0.1f, ty1 + 0.3f, 1.0f, WallKind.Concrete);

            // ---- трансформаторный двор (восток) ----
            Prop("props/subst_transformer", rt.At(72, 36));
            Prop("props/subst_transformer", rt.At(84, 36));
            Prop("props/subst_transformer", rt.At(78, 50));
            Prop("props/subst_switchgear", rt.At(90, 44));
            for (float y = 28; y < 58; y += 1.3f) Prop("props/concrete_barrier", rt.At(66, y + 0.5f));

            // ---- лут ----
            // машинный зал (земля): военные ящики; коридор — деревянный
            foreach (var (ci, cj) in new[] { (7, 1), (10, 2), (9, 6) }) { AddCrate(rt.Cell(ci, cj), 2, "duga"); Bags[^1].Floor = 0; }
            AddCrate(rt.Cell(1, 1), 1, "duga");
            // аппаратная (2-й эт.): стойки — «разобрать» 4 с
            foreach (var (ci, cj) in new[] { (0.5f, 0.6f), (2.5f, 0.6f), (0.5f, 3f), (2.5f, 3f), (2.5f, 6.5f) })
            {
                var rp = rt.Cell(ci, cj);
                var st = new Stash { Pos = rp, Floor = 1, Verb = "Разобрать стойку аппаратуры", Sound = "pry", Seconds = 4, Noise = 20 };
                st.OnDone = a =>
                {
                    var got = new List<string>();
                    void Give(string id, int n) { if (n <= 0) return; a.Add(id, n); got.Add($"{Items.Name(id)} ×{n}"); Economy.BookTake("стойка «Дуги»", id, n); }
                    Give("val_radio_parts", Rng.Next(2, 5));
                    if (Rng.NextDouble() < 0.7) Give("val_radio_tubes", Rng.Next(1, 3));
                    if (Rng.NextDouble() < 0.3) Give("val_gold_contacts", 1);
                    if (Rng.NextDouble() < 0.12) Give("val_computer_block", 1);
                    Events.Add(new SimEvent { Kind = SimEventKind.Gather, ActorId = a.Id, Pos = rp, Z = Building.FloorZ(1), Value = 1, Text = "Стойка" });
                    Info(a, "Из стойки: " + string.Join(", ", got));
                    if (rt.RefreshLeft <= 0) rt.RefreshLeft = Tier3Rt.RefreshTime;
                };
                Decor("props/duga_rack", rp + new Vector2(0, 0.3f)).Floor = 1;
                rt.Stashes.Add(st);
            }
            // кабинет начальника: сейф (карта доступа)
            AddSafe(rt.Cell(11, 7)); Bags[^1].Floor = 1;

            // ---- цепочка главного ящика ----
            rt.Switch1 = DugaSwitch(rt, rt.At(70, 31), 1);
            rt.Switch2 = DugaSwitch(rt, rt.At(88, 54), 2);
            rt.Shield = new Stash { Pos = rt.Cell(11, -1), Floor = 2, Verb = "Перекинуть щиток антенны", Sound = "lever", Seconds = 4, Noise = 35, Repeatable = true };
            rt.Shield.Require = a => rt.ShieldOn ? "Щиток уже перекинут — антенна на пульте" : !rt.Powered ? "Щиток мёртв — во дворе выключены рубильники (2 шт., восток)" : null;
            rt.Shield.OnDone = a =>
            {
                rt.ShieldOn = true;
                PrefabVersion++;
                Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = a.Id, Pos = rt.Shield.Pos, Z = Building.FloorZ(2), Text = "power" });
                Info(a, "Антенна подключена к пульту — запускай прогрев на 2-м этаже");
            };
            rt.Stashes.Add(rt.Shield);
            rt.ConsolePos = rt.Cell(5, 0);
            Decor("props/duga_console", rt.ConsolePos + new Vector2(0, -0.4f)).Floor = 1;
            Decor("props/duga_shield", rt.Shield.Pos + new Vector2(0, -0.5f)).Floor = 2;
            rt.Console = new Stash { Pos = rt.ConsolePos, Floor = 1, Verb = "Запустить прогрев пульта дальнего обнаружения", Sound = "reader", Seconds = 3, Noise = 30, Repeatable = true };
            rt.Console.Require = a =>
            {
                if (rt.WarmLeft > 0) return $"Пульт греется — ещё {MathF.Ceiling(rt.WarmLeft)} с";
                if (rt.Crate.Unlocked) return "Ящик пульта уже открыт";
                if (!rt.Powered) return "Пульт мёртв — нет тока: включи 2 рубильника в трансформаторном дворе";
                if (!rt.ShieldOn) return "Антенна не подключена — перекинь щиток на галерее (лестница из пультовой)";
                return null;
            };
            rt.Console.OnDone = a =>
            {
                rt.WarmLeft = Tier3Rt.WarmTime;
                Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = a.Id, Pos = rt.ConsolePos, Z = Building.FloorZ(1), Text = "alarm" });
                Info(a, $"Прогрев пошёл — {Tier3Rt.WarmTime:0} с. Треск слышно на всю РТ, охрана идёт");
                Noises.Add(new Noise { Pos = rt.ConsolePos, Radius = 120, SourceId = a.Id, Time = Time });
            };
            rt.Stashes.Add(rt.Console);
            rt.CratePos = rt.Cell(10.5f, 0.5f);
            var crate = new LootBag { Id = NextId(), Pos = rt.CratePos, IsCrate = true, Punch = true, Tier = 3, Rt = "duga", Floor = 1, Look = "military", Src = "пульт «Дуги»" };
            Bags.Add(crate);
            rt.Crate = crate;

            // ---- охрана (тир 3): земля, 2-й этаж, галерея ----
            void Guard(Vector2 p, int floor, string kit)
            {
                NpcHomes[-NpcHomes.Count - 1] = p;
                NpcKit[p] = kit;
                if (floor > 0) NpcHomeFloor[p] = floor;
            }
            Guard(rt.At(46, 76), 0, "akm"); Guard(rt.At(55, 76), 0, "akm");                 // КПП
            Guard(rt.At(30, 58), 0, "akm"); Guard(rt.At(64, 60), 0, "pps");                 // двор
            Guard(rt.Cell(8, 4), 0, "akm");                                                // машинный зал
            Guard(rt.Cell(2, 2), 1, "pps");                                                // аппаратная
            Guard(rt.Cell(7, 1), 1, "akm");                                                // пультовая
            Guard(rt.Cell(11, 6), 1, "pps");                                               // кабинет
            Guard(rt.Cell(3, -1), 2, "svd");                                               // снайпер на галерее
        }

        Stash DugaSwitch(Tier3Rt rt, Vector2 p, int n)
        {
            Decor("props/duga_breaker", p + new Vector2(0, -0.6f));
            AddHiddenWall(p.X - 0.35f, p.Y - 0.9f, p.X + 0.35f, p.Y - 0.6f, 2.2f);
            var s = new Stash { Pos = p, Verb = $"Включить рубильник {n}", Sound = "lever", Seconds = 5, Noise = 70, Repeatable = true };
            s.Require = a => (n == 1 ? rt.Power1 : rt.Power2) ? "Рубильник уже включён" : null;
            s.OnDone = a =>
            {
                if (n == 1) rt.Power1 = true; else rt.Power2 = true;
                PrefabVersion++;
                Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = a.Id, Pos = p, Text = "power" });
                Noises.Add(new Noise { Pos = p, Radius = 90, SourceId = a.Id, Time = Time });   // сирена: охрана идёт на звук
                Info(a, rt.Powered ? "Оба рубильника включены — ток пошёл, гудит сирена. Дальше — щиток на галерее антенны" : "Рубильник включён — нужен второй (во дворе)");
            };
            rt.Stashes.Add(s);
            return s;
        }

        /// <summary>Лут пульта «Дуги» (тир 3): детали тира 3, сталь, ценный лом, шанс элиты и перфокарты III (D49 — через бюджет режиссёра).</summary>
        void FillDugaCrate(LootBag bag)
        {
            bag.Items.Clear();
            bag.Search.Clear();
            bag.Items.Add(new ItemSlot("scrap", Rng.Next(40, 71)));
            bag.Items.Add(new ItemSlot(Prog.Alloy, Rng.Next(20, 31)));
            for (int i = Rng.Next(2, 4); i > 0; i--) bag.Items.Add(new ItemSlot(Weaponry.Tier2Part(Rng), 1));
            TryTier3(bag, 0.9);
            TryTier3(bag, 0.4);
            TryElite(bag, Director?.Phase ?? 0, 0.3);
            bag.Items.Add(new ItemSlot("val_radio_tubes", Rng.Next(2, 5)));
            bag.Items.Add(new ItemSlot("val_gold_contacts", Rng.Next(1, 3)));
            if (Rng.NextDouble() < 0.35) bag.Items.Add(new ItemSlot("val_computer_block", 1));
            if (Rng.NextDouble() < 0.35) bag.Items.Add(new ItemSlot("punchcard_3", 1));
            if (Rng.NextDouble() < 0.4) bag.Items.Add(new ItemSlot(Pick(Weaponry.ArmyAtts), 1));
            bag.Items.Add(new ItemSlot("ammo_762", Rng.Next(30, 61)));
            if ((Director?.Phase ?? 0) >= 2 && Rng.NextDouble() < 0.3) bag.Items.Add(Items.NewSlot("expl_c4"));
            Clothing.Loot(WearRng, bag, "event");
        }

        void StepTier3()
        {
            var rt = Duga;
            if (rt == null) return;
            foreach (var s in rt.Stashes) StepStash(s);
            rt._noiseT -= Dt;
            bool tick = rt._noiseT <= 0;
            if (tick) rt._noiseT = 1f;
            if (rt.WarmLeft > 0)
            {
                rt.WarmLeft -= Dt;
                if (tick)
                {
                    Noises.Add(new Noise { Pos = rt.ConsolePos, Radius = 70, SourceId = -1, Time = Time });
                    if ((int)Time % 3 == 0) Events.Add(new SimEvent { Kind = SimEventKind.Machine, Pos = rt.ConsolePos, Z = Building.FloorZ(1), Text = "reader" });
                }
                if (rt.WarmLeft <= 0)
                {
                    rt.WarmLeft = -1;
                    FillDugaCrate(rt.Crate);
                    rt.Crate.Unlocked = true;
                    rt.RefreshLeft = Tier3Rt.RefreshTime;
        
                    Events.Add(new SimEvent { Kind = SimEventKind.Machine, Pos = rt.CratePos, Z = Building.FloorZ(1), Text = "power" });
                    Events.Add(new SimEvent { Kind = SimEventKind.Info, Pos = rt.CratePos, Text = "Пульт «Дуги» прогрет — ящик открыт" });
                }
            }
            if (rt.RefreshLeft > 0 && (rt.RefreshLeft -= Dt) <= 0)
            {
                if (Actors.Exists(a => !a.Dead && a.Team == Team.Player && rt.M.Contains(a.Pos, 4))) { rt.RefreshLeft = 60; return; }
                ResetTier3(rt);
            }
        }

        void ResetTier3(Tier3Rt rt)
        {
            foreach (var s in rt.Stashes) { s.Done = false; s.ActorId = 0; s.Progress = 0; }
            rt.Power1 = rt.Power2 = rt.ShieldOn = false;
            rt.WarmLeft = -1;
            // крейт пульта: из сохранения он — обычная сумка; убрать копии, вернуть свой, закрытым и пустым
            Bags.RemoveAll(b => b != rt.Crate && b.IsCrate && b.Floor == 1 && Vector2.Distance(b.Pos, rt.CratePos) < 0.6f);
            if (!Bags.Contains(rt.Crate)) Bags.Add(rt.Crate);
            rt.Crate.Items.Clear();
            rt.Crate.Search.Clear();
            rt.Crate.Unlocked = false;
            PrefabVersion++;
        }

        /// <summary>Этот ящик — крейт пульта «Дуги», ещё закрытый: что сказать игроку.</summary>
        string DugaCrateWhy(LootBag b)
        {
            var rt = Duga;
            if (rt == null || b != rt.Crate || b.Unlocked) return null;
            if (rt.WarmLeft > 0) return $"Пульт греется — ящик откроется через {MathF.Ceiling(rt.WarmLeft)} с";
            return !rt.Powered ? "Ящик пульта заперт: включи 2 рубильника во дворе, перекинь щиток на галерее и запусти прогрев пульта"
                 : !rt.ShieldOn ? "Ток есть. Дальше — щиток на галерее антенны, потом прогрев пульта"
                 : "Всё готово — запусти прогрев на пульте (рядом)";
        }

        /// <summary>Отладка (--do=…:dugachain): включить рубильники и щиток сразу.</summary>
        public void DebugDugaChain() { if (Duga != null) { Duga.Power1 = Duga.Power2 = Duga.ShieldOn = true; PrefabVersion++; } }
    
        /// <summary>
        /// Самопроверка «Дуги» (D61, --simtest=N --dugatest): корпус на этажах, лестницы А и Б проходимы, охрана на 2-м этаже
        /// и галерее, цепочка рубильники → щиток → прогрев → крейт, постройки РТ не ломаются и к ним не пристроиться.
        /// </summary>
        public void DugaSelfTest(Action<string> log)
        {
            int fails = 0;
            void Check(string what, bool ok) { if (!ok) fails++; log($"ДУГА-ТЕСТ {what}: {(ok ? "OK" : "ПРОВАЛ")}"); }
            var rt = Duga;
            Check("РТ поставлена на остров", rt != null);
            if (rt == null) { log("ДУГА-ТЕСТ итог: провалов 1"); return; }
            log($"ДУГА-ТЕСТ место: {rt.O.X:0},{rt.O.Y:0}");
            int up1 = 0, up2 = 0; foreach (var s in Structures) if (s.Static) { if (s.Floor == 1) up1++; if (s.Floor == 2) up2++; }
            Check($"корпус: 2-й этаж {up1}, галерея {up2}", up1 > 60 && up2 > 12);
            int g1 = 0, g2 = 0; foreach (var a in Actors) if (a.Team == Team.Npc && rt.M.Contains(a.Pos)) { if (a.Floor == 1) g1++; if (a.Floor == 2) g2++; }
            Check($"охрана наверху: 2-й этаж {g1}, галерея {g2}", g1 >= 3 && g2 >= 1);
            var p = new Actor { Id = NextId(), Name = "Тест-рейдер", Team = Team.Player };
            Actors.Add(p);
            void Walk(Vector2 dir, float sec) { for (int k = 0; k < (int)(sec / Dt); k++) { var c = InputCmd.Empty; c.Move = dir; c.Aim = p.Pos + dir * 5; Step(new Dictionary<int, InputCmd> { [p.Id] = c }); p.Hp = p.MaxHp; } }
            p.Pos = rt.Cell(1, 6.4f);
            Walk(new Vector2(0, -1), 1.6f);
            Check($"лестница А: этаж {p.Floor + 1}", p.Floor == 1);
            p.Pos = rt.Cell(8, 1.3f); p.Floor = 1; p.Z = Building.FloorZ(1);
            Walk(new Vector2(0, -1), 1.8f);
            Check($"лестница Б на галерею: этаж {p.Floor + 1}", p.Floor == 2);
            // цепочка
            Check("ящик пульта закрыт", !rt.Crate.Unlocked && DugaCrateWhy(rt.Crate) != null);
            p.Floor = 0; p.Z = 0;
            foreach (var sw in new[] { rt.Switch1, rt.Switch2 }) { p.Pos = sw.Pos; UseStash(p, sw); for (int k = 0; k < (int)(6 / Dt); k++) { StepStash(sw); p.Pos = sw.Pos; } }
            Check("рубильники: ток", rt.Powered);
            p.Floor = 1; p.Z = Building.FloorZ(1); p.Pos = rt.ConsolePos;
            Check("пульт без щитка — отказ", rt.Console.Require(p) != null);
            p.Floor = 2; p.Z = Building.FloorZ(2); p.Pos = rt.Shield.Pos;
            UseStash(p, rt.Shield); for (int k = 0; k < (int)(5 / Dt); k++) { StepStash(rt.Shield); p.Pos = rt.Shield.Pos; }
            Check("щиток перекинут", rt.ShieldOn);
            p.Floor = 1; p.Z = Building.FloorZ(1); p.Pos = rt.ConsolePos;
            UseStash(p, rt.Console); for (int k = 0; k < (int)(4 / Dt); k++) { StepStash(rt.Console); p.Pos = rt.ConsolePos; }
            Check("прогрев запущен", rt.WarmLeft > 0);
            for (int k = 0; k < (int)((Tier3Rt.WarmTime + 1) / Dt); k++) StepTier3();
            Check($"крейт открыт, вещей {rt.Crate.Items.Count}", rt.Crate.Unlocked && rt.Crate.Items.Count >= 5);
            // постройки РТ
            Structure wall = Structures.Find(s => s.Static && s.Kind == StructKind.Wall);
            float hp = wall.Hp; DamageStructure(wall, 1e6f, p.Id);
            Check("стена РТ не ломается", Structures.Contains(wall) && wall.Hp == hp);
            p.Floor = 0; p.Z = 0; p.Pos = rt.Cell(-2, 3); p.BuildPiece = StructKind.Foundation;
            Check("рядом строить нельзя", !CanBuild(p, StructKind.Foundation, rt.Cell(-1, 3), out _));
            Actors.Remove(p);
            ResetTier3(rt);
            log($"ДУГА-ТЕСТ итог: {(fails == 0 ? "всё OK" : $"провалов {fails}")}");
        }
}
}
