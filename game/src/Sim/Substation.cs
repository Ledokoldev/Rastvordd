using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Подстанция «Тихая» — РТ тира 1 (docs/design/11-monuments.md): две пробки (лежат в 2 из 6 мест) → главный щиток →
    /// питание: гудят трансформаторы, поднимается роллета склада → перфокарта I в крейт → 30 с гула → крейт открыт.
    /// Пробки — предмет этой РТ: за забором пропадают и возвращаются на место. Через 15 мин после открытия крейта,
    /// когда на территории никого нет, РТ обновляется.
    /// </summary>
    public sealed class Substation
    {
        public const float CrateDelay = 30f, RefreshTime = 900f, HumRadius = 60f, CrateNoise = 120f;
        public Monument M;
        public Vector2 O;                       // угол РТ (северо-запад)
        public MapDecor Panel;
        public Vector2 PanelPos;                // где стоять, чтобы вставить пробку
        public MapWall Shutter;
        public Vector2 CratePos;
        public LootBag Crate;
        public readonly List<Pickup> FuseSpots = new();
        public int FusesIn;
        public bool Powered;
        public float CrateTimer, RefreshLeft, HumLeft, _noiseT;

        public Vector2 At(float x, float y) => O + new Vector2(x, y);

        /// <summary>После загрузки сохранения сумки заменены сохранёнными: найти свой крейт по месту или вернуть новый.</summary>
        public void Relink(World w)
        {
            Crate = w.Bags.Find(b => b.IsCrate && Vector2.Distance(b.Pos, CratePos) < 0.3f);
            if (Crate == null) { Crate = w.NewPunchCrate(CratePos); }
            Crate.Punch = true;
        }
    }

    public sealed partial class World
    {
        public readonly List<Substation> Substations = new();
        /// <summary>Чем вооружена охрана на конкретной точке (по умолчанию — калаш, как на Складе).</summary>
        readonly Dictionary<Vector2, string> NpcKit = new();
        bool _prefabMode;
        int _wallSeq;
        /// <summary>Меняется при любом изменении состояния готовых РТ (роллета, щиток) — клиент перечитывает.</summary>
        public int PrefabVersion;

        internal LootBag NewPunchCrate(Vector2 p)
        {
            var bag = new LootBag { Id = NextId(), Pos = p, IsCrate = true, Tier = 1, Punch = true };
            FillPunchCrate(bag, true);
            Bags.Add(bag);
            return bag;
        }

        /// <summary>
        /// Главная награда тира 1 (D46): перфокарта II, лом, корпуса и детали тира 2 (УСМ, затворная группа, нарезной ствол),
        /// линза, плата (АТС и Подстанция — электроника — чаще), патроны; с малым шансом Обрез или МР-133.
        /// </summary>
        void FillPunchCrate(LootBag bag, bool electronics = false)
        {
            bag.Items.Clear();
            bag.Search.Clear();
            bag.Unlocked = false;
            bag.Items.Add(new ItemSlot("punchcard_2", 1));
            bag.Items.Add(new ItemSlot("scrap", Rng.Next(30, 61)));
            bag.Items.Add(new ItemSlot(Rng.Next(2) == 0 ? "comp_gears" : "comp_spring", Rng.Next(1, 3)));
            bag.Items.Add(new ItemSlot(Pick(Weaponry.Tier2Parts), 1));
            if (Rng.NextDouble() < 0.45) bag.Items.Add(new ItemSlot(Pick(Weaponry.Tier2Parts), 1));
            if (Rng.NextDouble() < (electronics ? 0.75 : 0.25)) bag.Items.Add(new ItemSlot("comp_circuit", 1));
            if (Rng.NextDouble() < (electronics ? 0.35 : 0.3)) bag.Items.Add(new ItemSlot("comp_lens", 1));
            if (Rng.NextDouble() < 0.5) bag.Items.Add(new ItemSlot("comp_pipe", 1));
            if (Rng.NextDouble() < 0.5) bag.Items.Add(new ItemSlot("comp_spring", Rng.Next(1, 3)));
            if (Rng.NextDouble() < 0.6) bag.Items.Add(new ItemSlot("ammo_9mm", Rng.Next(10, 25)));
            if (Rng.NextDouble() < 0.3) bag.Items.Add(new ItemSlot("ammo_12g", Rng.Next(5, 12)));
            if (Rng.NextDouble() < 0.3) bag.Items.Add(new ItemSlot("comp_oil", 1));
            if (Rng.NextDouble() < 0.5) bag.Items.Add(new ItemSlot("bandage", Rng.Next(1, 3)));
            if (Rng.NextDouble() < 0.1) bag.Items.Add(new ItemSlot(Pick(Weaponry.FactoryAtts), 1));
            if (Rng.NextDouble() < 0.06) bag.Items.Add(Items.NewSlot(Rng.Next(4) == 0 ? "weapon_mp133" : "weapon_sawnoff"));
        }

        void PlaceSubstation()
        {
            var rng = new Random(Seed * 7919 + 101);
            var size = new Vector2(92, 72);
            var spot = FindPrefabSpot(rng, size, new[] { Biome.Grassland, Biome.Forest, Biome.Arid }, HalfSize * 0.25f, HalfSize * 0.7f);
            if (spot == null) return;
            var s = new Substation { O = spot.Value };
            s.M = new Monument { Name = "Подстанция «Тихая»", Min = s.O, Max = s.O + size };
            Monuments.Add(s.M);
            Prefabs.Add(s.M);
            ClearPrefabArea(s.M);
            _prefabMode = true;
            BuildSubstation(s, rng);
            _prefabMode = false;
            Substations.Add(s);
        }

        void BuildSubstation(Substation s, Random rng)
        {
            Vector2 P(float x, float y) => s.At(x, y);
            const string F = "walls/wallface_fence_po2", B = "walls/wallface_brick", SH = "walls/wallface_shutter", G = "walls/wallface_garage";

            // земля: двор — грунт, ОРУ — гравий, подъезд — асфальт, полы корпусов
            Patch("tiles/ground_dirt", P(0, 14), P(92, 72));
            Patch("tiles/ground_dirt", P(50, 0), P(92, 14));
            Patch("tiles/ground_gravel", P(50, 3), P(90, 45));
            Patch("tiles/ground_gravel", P(42, 16), P(50, 45));
            Patch("tiles/ground_gravel", P(40, 50), P(51, 70));
            Patch("tiles/ground_asphalt", P(10, 48), P(40, 70));
            Patch("tiles/ground_asphalt", P(9, 70), P(19, 82));
            Patch("tiles/ground_concrete", P(52, 48), P(78, 70));
            Patch("tiles/ground_concrete", P(44, 25.5f), P(90, 26.5f));
            foreach (var bx in new[] { 52f, 66f, 80f })
            {
                Patch("tiles/ground_concrete", P(bx + 3.5f, 12), P(bx + 4.5f, 44));      // кабельные лотки
                Patch("tiles/ground_concrete", P(bx - 3.5f, 35), P(bx + 3.5f, 40));      // фундамент трансформатора
                Patch("tiles/ground_concrete", P(bx - 2, 20), P(bx + 2, 23));
            }
            Patch("tiles/floor_tile", P(6, 20), P(34, 34));
            Patch("tiles/floor_tile", P(6, 34), P(18, 46));
            Patch("tiles/ground_concrete", P(54, 52), P(76, 64));

            // забор ПО-2: ворота (юг), калитка (восток), пролом под упавшей опорой (север)
            foreach (var (a, b) in new[] { ((0f, 14f), (50f, 14f)), ((50f, 0f), (50f, 14f)), ((50f, 0f), (70f, 0f)), ((77f, 0f), (92f, 0f)), ((92f, 0f), (92f, 57f)),
                                           ((92f, 61f), (92f, 72f)), ((0f, 72f), (9f, 72f)), ((19f, 72f), (92f, 72f)), ((0f, 14f), (0f, 72f)) })
                WallT(P(a.Item1, a.Item2), P(b.Item1, b.Item2), 2.2f, F);
            // ОПУ: коридор, щитовая, склад (вход только через роллету), дежурка
            foreach (var (a, b) in new[] { ((6f, 20f), (34f, 20f)), ((6f, 20f), (6f, 46f)), ((34f, 20f), (34f, 28f)), ((34f, 33f), (34f, 34f)), ((18f, 34f), (34f, 34f)),
                                           ((18f, 34f), (18f, 41f)), ((18f, 43f), (18f, 46f)), ((6f, 46f), (11.4f, 46f)), ((12.6f, 46f), (18f, 46f)),
                                           ((18f, 20f), (18f, 22.4f)), ((18f, 23.6f), (18f, 34f)), ((18f, 27f), (34f, 27f)), ((6f, 34f), (11.4f, 34f)), ((12.6f, 34f), (18f, 34f)) })
                WallT(P(a.Item1, a.Item2), P(b.Item1, b.Item2), 2.8f, B);
            s.Shutter = WallT(P(34, 28), P(34, 33), 2.8f, SH, 0.35f);
            RemovableWalls.Add(s.Shutter);
            // мастерская с гаражными воротами
            foreach (var (a, b) in new[] { ((54f, 52f), (76f, 52f)), ((54f, 52f), (54f, 56f)), ((54f, 57.2f), (54f, 64f)), ((76f, 52f), (76f, 64f)),
                                           ((54f, 64f), (57f, 64f)), ((63f, 64f), (66f, 64f)), ((72f, 64f), (76f, 64f)) })
                WallT(P(a.Item1, a.Item2), P(b.Item1, b.Item2), 3.0f, B);
            WallT(P(57, 64), P(63, 64), 3.0f, G);
            WallT(P(66, 64), P(72, 64), 3.0f, G);
            foreach (var x in new[] { 59f, 73f }) WallT(P(x, 30), P(x, 42), 3.4f, B, 1.0f);   // огнестены
            int roofFrom = Roofs.Count;
            Roof("tiles/roof_tar", P(6, 20), P(34, 34), rng);
            Roof("tiles/roof_tar", P(6, 34), P(18, 46), rng);
            Roof("tiles/roof_metal", P(54, 52), P(76, 64), rng);

            // входы: крыльцо и пандусы — по проёмам (Entrances.cs); створки калитки и фонари у дверей
            AddEntrances(roofFrom);
            Prop("props/door_lamp_post", P(52.6f, 55.4f));
            Prop("props/door_lamp_post", P(35.2f, 27.4f));
            Prop("props/gate_leaf_open", P(93.4f, 57.2f));             // калитка (восток)
            Prop("props/gate_leaf_open", P(93.4f, 61.4f), true);
            Prop("props/door_lamp_post", P(93, 56.2f));
            Prop("props/door_lamp_post", P(8.4f, 73));                  // ворота (юг)
            Prop("props/door_lamp_post", P(19.6f, 73));

            // ОРУ: три ячейки портал → разъединитель → выключатель → разъединитель → трансформатор
            foreach (var bx in new[] { 52f, 66f, 80f })
            {
                Prop("props/subst_portal", P(bx, 10));
                Prop("props/subst_disconnector", P(bx, 17));
                Prop("props/subst_breaker", P(bx, 22.5f));
                Prop("props/subst_relay_cabinet", P(bx - 5, 25));
                Prop("props/subst_disconnector", P(bx, 30.5f));
                Prop("props/subst_transformer", P(bx, 39));
                Prop("props/subst_relay_cabinet", P(bx + 5, 43.5f));
                for (int k = -1; k <= 1; k++)
                {
                    Wire(P(bx + k * 1.3f, 10), 5.5f, P(bx + k * 0.65f, 17), 2.5f, 0.6f);
                    Wire(P(bx + k * 0.65f, 17), 2.5f, P(bx + k * 0.78f, 22.5f), 2.8f, 0.2f);
                    Wire(P(bx + k * 0.78f, 22.5f), 2.8f, P(bx + k * 0.65f, 30.5f), 2.5f, 0.3f);
                    Wire(P(bx + k * 0.65f, 30.5f), 2.5f, P(bx + k * 0.78f, 39), 3.8f, 0.3f);
                }
            }
            for (int i = 0; i < 3; i++)
            {
                Wire(P(45, 9.6f + i * 0.5f), 5.9f, P(92, 9.6f + i * 0.5f), 5.9f, 1.0f);
                Wire(P(73 + i * 0.6f, -10), 7f, P(73 + i * 0.6f, 9.6f), 5.9f, 0.8f);
            }
            Prop("props/subst_ktp", P(88, 24));
            Prop("props/subst_insulator_pallet", P(88, 36));
            Prop("props/subst_oil_tank", P(45, 44));
            Prop("props/subst_pylon_fallen", P(73, 6));
            Prop("props/subst_lamp", P(53, 4));
            Prop("props/subst_lamp", P(90, 44));
            // двор
            foreach (var (x, y) in new[] { (42f, 53f), (46f, 55.5f), (43f, 59f), (48f, 62f), (44.5f, 66f) }) Prop("props/subst_cable_drum", P(x, y), rng.Next(2) == 0);
            Prop("props/subst_reel_stack", P(49, 68));
            Prop("props/subst_insulator_pallet", P(41, 69.5f));
            Prop("props/subst_insulator_pallet", P(50, 52));
            Prop("props/subst_truck", P(82, 68.5f));
            Prop("props/subst_ktp", P(86, 56));
            Prop("props/subst_oil_tank", P(84, 49));
            Prop("props/subst_kpp", P(5, 69.5f));
            Prop("props/subst_lamp", P(21, 49));
            foreach (var (x, y) in new[] { (41f, 47f), (90f, 30f), (52f, 46f), (8f, 16.5f) }) Decor("clutter/subst_sign", P(x, y));
            Prop("props/car_wreck", P(15, 62), true);
            Prop("props/car_wreck", P(30, 57));
            foreach (var (x, y) in new[] { (24f, 66f), (27f, 66f), (35f, 60f), (13f, 55f), (17f, 55f) }) Prop("props/concrete_barrier", P(x, y));
            foreach (var (x, y) in new[] { (2f, 16f), (90f, 2f), (90f, 70f), (36f, 70f), (2f, 50f), (64f, 70f), (3f, 30f), (3f, 63f), (4f, 64.2f) }) Prop("props/barrel_oil", P(x, y));
            Prop("props/subst_reel_stack", P(33, 69));
            Prop("props/subst_insulator_pallet", P(22, 60));
            // мастерская
            Prop("props/workbench", P(60, 54.2f));
            Prop("props/int_shelf", P(66, 52.6f));
            Prop("props/int_shelf", P(70, 52.6f));
            Prop("props/subst_reel_stack", P(73, 58));
            Prop("props/barrel_oil", P(56, 55));
            // ОПУ внутри
            Prop("props/subst_switchgear", P(22, 20.6f));
            Prop("props/subst_switchgear", P(27.2f, 20.6f));
            s.Panel = Prop("props/subst_main_panel", P(32, 20.6f));
            s.PanelPos = P(32, 21.4f);
            Prop("props/int_shelf", P(21, 27.6f));
            Prop("props/subst_insulator_pallet", P(31, 33.4f));
            Prop("props/int_lockers", P(9, 20.6f));
            Prop("props/int_lockers", P(11, 20.6f));
            Prop("props/int_lockers", P(13, 20.6f));
            Prop("props/int_desk", P(11, 26));
            Prop("props/int_desk", P(15, 26));
            Prop("props/int_table", P(14, 30.5f));
            Prop("props/int_shelf", P(7.2f, 31));
            Prop("props/int_cot", P(8, 38));
            Prop("props/int_burzhuika", P(16, 35.2f));
            Prop("props/int_table", P(12, 42));
            Prop("props/int_desk", P(9, 45));

            // лут: крейт под перфокарту (за роллетой), обычные ящики, бочки и хлам с ломом
            s.CratePos = P(26, 31.8f);
            s.Crate = NewPunchCrate(s.CratePos);
            AddCrate(P(28, 54), 1);
            AddCrate(P(60, 47), 1);
            AddCrate(P(88, 63), 1);
            foreach (var (x, y) in new[] { (36f, 40f), (80f, 49f), (24f, 62f) }) AddScrapSpot(NodeKind.Junk, P(x, y));
            foreach (var (x, y) in new[] { (2f, 40f), (38f, 52f), (90f, 12f), (56f, 70f), (2f, 25f), (88f, 50f) }) AddScrapSpot(NodeKind.Barrel, P(x, y));
            AddPickup(PickupKind.WaterBarrel, P(16.5f, 44.5f));

            // пробки: 6 мест, доступны 2 случайных
            foreach (var (x, y) in new[] { (5f, 67.5f), (9f, 40.5f), (66f, 43.5f), (45f, 58.5f), (77.2f, 67.5f), (24.5f, 22f) })
            {
                AddPickup(PickupKind.Fuse, P(x, y));
                var p = Pickups[^1];
                p.RespawnLeft = 1e9f;
                s.FuseSpots.Add(p);
            }
            ScatterFuses(s, 2);

            // охрана с самопалами: ОРУ (обход вокруг трансформаторов) и коридор ОПУ
            foreach (var home in new[] { P(66, 30), P(12, 28) })
            {
                NpcHomes[-NpcHomes.Count - 1] = home;
                NpcKit[home] = "pipe";
            }
        }

        /// <summary>Сделать доступными n пробок в случайных свободных местах.</summary>
        void ScatterFuses(Substation s, int n)
        {
            var free = s.FuseSpots.FindAll(p => !p.Available);
            for (int i = 0; i < n && free.Count > 0; i++)
            {
                int k = Rng.Next(free.Count);
                free[k].RespawnLeft = 0;
                free.RemoveAt(k);
            }
        }

        public Substation SubstationAt(Vector2 p)
        {
            foreach (var s in Substations) if (s.M.Contains(p)) return s;
            return null;
        }

        /// <summary>Пробка в щиток: две — питание.</summary>
        void UsePanel(Actor a, Substation s)
        {
            if (s.Powered) { Info(a, "Щиток включён — трансформаторы гудят"); return; }
            if (a.Take("fuse", 1) == 0) { Info(a, $"Щиток обесточен: нужны 2 пробки ({s.FusesIn}/2). Они лежат на территории Подстанции"); return; }
            s.FusesIn++;
            Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = a.Id, Pos = s.PanelPos, Text = "fuse" });
            if (s.FusesIn < 2) { Info(a, "Пробка вставлена (1/2)"); return; }
            s.Powered = true;
            s.HumLeft = 60;
            s.Panel.Sprite = "props/subst_main_panel_on";
            if (Walls.Remove(s.Shutter)) _wallGrid.Remove(s.Shutter);
            PrefabVersion++;
            Info(a, "Питание есть: гудят трансформаторы, роллета склада поднялась");
            Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = a.Id, Pos = s.PanelPos, Text = "power" });
            Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = a.Id, Pos = s.Shutter.Wall.Min, Text = "shutter" });
            Noises.Add(new Noise { Pos = s.PanelPos, Radius = Substation.HumRadius, SourceId = a.Id, Time = Time });
        }

        /// <summary>Перфокарта I в считыватель крейта: 30 с гула, потом крейт открыт.</summary>
        void UsePunchCrate(Actor a, LootBag bag)
        {
            var s = Substations.Find(x => x.Crate == bag);
            if (s != null && !s.Powered) { Info(a, "Считыватель без питания — сначала включи главный щиток"); return; }
            if (s != null && s.CrateTimer > 0) { Info(a, $"Крейт открывается: {MathF.Ceiling(s.CrateTimer)} с"); return; }
            if (a.Take("punchcard_1", 1) == 0) { Info(a, "Крейт заперт: нужна перфокарта I (лежит на РТ тира 0)"); return; }
            if (s == null) { bag.Unlocked = true; return; }
            s.CrateTimer = Substation.CrateDelay;
            Info(a, $"Перфокарта принята — крейт откроется через {Substation.CrateDelay:0} с");
        }

        /// <summary>Каждый тик: гул, таймер крейта, пробки за забором, обновление РТ.</summary>
        void StepSubstations()
        {
            foreach (var s in Substations)
            {
                s._noiseT -= Dt;
                bool noisy = s._noiseT <= 0;
                if (noisy) s._noiseT = 2f;
                if (s.HumLeft > 0)
                {
                    s.HumLeft -= Dt;
                    if (noisy) Noises.Add(new Noise { Pos = s.At(66, 35), Radius = Substation.HumRadius, SourceId = -1, Time = Time });
                }
                if (s.CrateTimer > 0)
                {
                    s.CrateTimer -= Dt;
                    if (noisy) { Noises.Add(new Noise { Pos = s.CratePos, Radius = Substation.CrateNoise, SourceId = -1, Time = Time }); Events.Add(new SimEvent { Kind = SimEventKind.Machine, Pos = s.CratePos, Text = "reader" }); }
                    if (s.CrateTimer <= 0)
                    {
                        s.Crate.Unlocked = true;
                        Events.Add(new SimEvent { Kind = SimEventKind.Machine, Pos = s.CratePos, Text = "crate" });
                        s.RefreshLeft = Substation.RefreshTime;
                        PrefabVersion++;
                    }
                }
                // пробки — только здесь: вынес за забор — пропала и вернулась на место
                if (Tick % 30 == 0)
                    foreach (var a in Actors)
                    {
                        if (a.Fuses == 0) continue;
                        if (!a.Dead && (s.M.Contains(a.Pos) || SubstationAt(a.Pos) != null)) continue;
                        int n = a.Take("fuse", 99);
                        ScatterFuses(s, n);
                        if (!a.Dead) Info(a, "Пробки остались на Подстанции — вынести их нельзя");
                    }
                if (s.RefreshLeft > 0 && (s.RefreshLeft -= Dt) <= 0)
                {
                    bool busy = Actors.Exists(a => !a.Dead && a.Team == Team.Player && s.M.Contains(a.Pos, 4));
                    if (busy) { s.RefreshLeft = 60; continue; }
                    ResetSubstation(s);
                }
            }
        }

        void ResetSubstation(Substation s)
        {
            s.Powered = false;
            s.FusesIn = 0;
            s.CrateTimer = 0;
            s.Panel.Sprite = "props/subst_main_panel";
            if (!Walls.Contains(s.Shutter)) { Walls.Add(s.Shutter); _wallGrid.Insert(s.Shutter, s.Shutter.Wall.Min, s.Shutter.Wall.Max); }
            if (!Bags.Contains(s.Crate)) Bags.Add(s.Crate);
            FillPunchCrate(s.Crate, true);
            foreach (var p in s.FuseSpots) p.RespawnLeft = 1e9f;
            ScatterFuses(s, 2);
            PrefabVersion++;
        }

        /// <summary>Убранные сейчас стены готовых РТ (поднятая роллета) — клиент их не рисует.</summary>
        public void CollectGoneWalls(List<int> into)
        {
            foreach (var w in RemovableWalls) if (!Walls.Contains(w)) into.Add(w.Id);
        }
    }
}
