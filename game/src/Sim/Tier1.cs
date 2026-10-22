using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace Rastvordd.Sim
{
    /// <summary>
    /// РТ тира 1 по макетам (scripts/mock-tier1.py → Tier1Layouts): Гаражи, АТС, Депо, Карьер (docs/design/11-monuments.md).
    /// Механика ставится по меткам макета «MECH:…». Везде в конце — крейт под перфокарту I: 30 с громкого гула и открыт.
    /// Локальные предметы (брелок, кабели) живут только на своей РТ: вынес за забор — пропали и вернулись на место.
    /// </summary>
    public sealed class Tier1Rt
    {
        public const float CrateDelay = 30f, RefreshTime = 900f;
        public string Key;
        public Monument M;
        public Vector2 O;
        public readonly List<Stash> Stashes = new();
        public LootBag Crate;
        public Vector2 CratePos;
        public bool Ready;              // крейт можно открыть перфокартой (ворота открыты, вагон у рампы, контейнер на площадке)
        public float CrateTimer, RefreshLeft, _noiseT;
        public string NotReadyText = "Крейт пока не достать";

        // Гаражи
        public Stash Alarm;
        public float AlarmLeft;
        public readonly List<(MapWall wall, Stash use)> Gates = new();
        public MapWall TargetGate;
        // АТС
        public readonly List<Pickup> Cables = new();
        public readonly List<Vector2> CableSpots = new();
        public int CablesIn;
        public readonly List<Vector2> Capsules = new(), Pipe = new();
        public float CapsuleLeft;
        public int CapsuleTo = -1;
        // Депо
        public readonly List<Stash> Levers = new();
        public bool[] LeverOn = new bool[3];
        public MapDecor Wagon;
        public Vector2 WagonStart, WagonEnd;
        public float WagonT;
        public bool WagonMoving;
        // Карьер
        public Stash Cabin;
        public MapDecor Boom, Container;
        public Vector2 Pivot, ContStart, ContEnd;

        public Vector2 At(float x, float y) => O + new Vector2(x, y);
    }

    public sealed partial class World
    {
        public readonly List<Tier1Rt> Tier1 = new();
        /// <summary>Подвижные украшения РТ (вагон, стрела крана, контейнер) — клиент двигает их по снапшоту.</summary>
        public readonly List<MapDecor> MovingDecors = new();

        void PlaceTier1()
        {
            foreach (var (key, name, json, seed) in new[]
            {
                ("garages", "Гаражи «Ракушки»", Tier1Layouts.Garages, 51),
                ("ats", "АТС «Занято»", Tier1Layouts.Ats, 53),
                ("depot", "Депо «Тупик»", Tier1Layouts.Depot, 57),
                ("quarry", "Карьер «Ковш»", Tier1Layouts.Quarry, 59),
            })
            {
                if (string.IsNullOrEmpty(json)) continue;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                float w = root.GetProperty("W").GetSingle(), h = root.GetProperty("H").GetSingle(), margin = root.GetProperty("M").GetSingle();
                var rng = new Random(Seed * 7919 + seed);
                var spot = FindPrefabSpot(rng, new Vector2(w, h) + new Vector2(margin * 2), new[] { Biome.Grassland, Biome.Forest, Biome.Arid, Biome.Tundra }, HalfSize * 0.2f, HalfSize * 0.8f, 50);
                if (spot == null) continue;
                var rt = new Tier1Rt { Key = key, O = spot.Value + new Vector2(margin) };
                rt.M = new Monument { Name = name, Min = rt.O, Max = rt.O + new Vector2(w, h) };
                Monuments.Add(rt.M);
                Prefabs.Add(rt.M);
                ClearPrefabArea(new Monument { Min = rt.M.Min - new Vector2(margin), Max = rt.M.Max + new Vector2(margin) });
                _prefabMode = true;
                BuildTier1(rt, root, rng);
                _prefabMode = false;
                RegisterDress(rt.M, key, rt.O, root);
                Tier1.Add(rt);
            }
        }

        void BuildTier1(Tier1Rt rt, JsonElement root, Random rng)
        {
            // земля, стены, крыши, пропсы, лут — общим строителем тира 0 (там же метки перфокарты не трогаются: ключ «t1»)
            var tmp = new Tier0Rt { O = rt.O, M = rt.M };
            BuildFromLayout(tmp, "t1", root, rng);
            Vector2 P(JsonElement mk) => rt.O + new Vector2(mk[0].GetSingle(), mk[1].GetSingle());
            var marks = new List<(string label, Vector2 p)>();
            foreach (var mk in root.GetProperty("marks").EnumerateArray())
            {
                string label = mk[2].GetString();
                if (label != null && label.StartsWith("MECH:")) marks.Add((label[5..], P(mk)));
            }
            Vector2? Mark(string k) { var m = marks.Find(x => x.label == k); return m.label != null ? m.p : null; }
            List<Vector2> Marks(string prefix) => marks.FindAll(x => x.label.StartsWith(prefix)).ConvertAll(x => x.p);
            MapDecor DecorNear(Vector2 p, string spritePart)
            {
                MapDecor best = null; float bd = 12;
                foreach (var d in Decors)
                    if (!d.Flat && d.Sprite.Contains(spritePart) && Vector2.Distance(d.Pos, p) < bd) { bd = Vector2.Distance(d.Pos, p); best = d; }
                return best;
            }
            MapWall WallNear(Vector2 p, string tex)
            {
                MapWall best = null; float bd = 4;
                foreach (var w in Walls)
                {
                    if (!w.Prefab || (tex != null && w.Tex != tex)) continue;
                    float d = Vector2.Distance((w.Wall.Min + w.Wall.Max) * 0.5f, p);
                    if (d < bd) { bd = d; best = w; }
                }
                return best;
            }

            // охрана по меткам: MECH:guard:<оружие>
            foreach (var (label, p) in marks)
                if (label.StartsWith("guard"))
                {
                    NpcHomes[-NpcHomes.Count - 1] = p;
                    NpcKit[p] = label.Length > 6 ? label[6..] : "pipe";
                }

            rt.CratePos = Mark("crate") ?? rt.M.Center;
            rt.Crate = new LootBag { Id = NextId(), Pos = rt.CratePos, IsCrate = true, Tier = 1, Punch = true };
            FillPunchCrate(rt.Crate, rt.Key == "ats");
            Bags.Add(rt.Crate);

            switch (rt.Key)
            {
                case "garages":
                {
                    rt.NotReadyText = "Крейт в запертом гараже — нужен брелок от ворот";
                    var car = Mark("alarm_car") ?? rt.M.Center;
                    rt.Alarm = new Stash { Pos = car, Verb = "Вскрыть машину (сработает сигнализация)", Sound = "pry", Seconds = 3, Noise = 30 };
                    rt.Alarm.OnDone = a =>
                    {
                        a.Add("rt_keyfob", 1);
                        rt.AlarmLeft = 15;
                        rt.RefreshLeft = Tier1Rt.RefreshTime;
                        Info(a, "Брелок от ворот у тебя! Машина орёт — сейчас сюда сбегутся. Жми брелок у гаражей: нужные ворота запищат");
                    };
                    rt.Stashes.Add(rt.Alarm);
                    float bestD = float.MaxValue;
                    foreach (var gp in Marks("gate"))
                    {
                        var wall = WallNear(gp, "walls/wallface_garage");
                        if (wall == null) continue;
                        RemovableWalls.Add(wall);
                        var use = new Stash { Pos = gp + new Vector2(0, 0.9f), Verb = "Нажать брелок у ворот", Sound = "beep", Seconds = 0.6f, Noise = 15, Repeatable = true };
                        use.Require = a => a.Count("rt_keyfob") > 0 ? null : "Ворота заперты. Брелок — в бардачке машины с сигнализацией";
                        var w = wall;
                        use.OnDone = a =>
                        {
                            if (w == rt.TargetGate)
                            {
                                if (Walls.Remove(w)) _wallGrid.Remove(w);
                                rt.Ready = true;
                                PrefabVersion++;
                                Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = a.Id, Pos = use.Pos, Text = "gate" });
                                Info(a, "Ворота поехали вверх — крейт внутри");
                            }
                            else
                            {
                                var tg = (rt.TargetGate.Wall.Min + rt.TargetGate.Wall.Max) * 0.5f;
                                Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = a.Id, Pos = tg, Text = "beep" });
                                Noises.Add(new Noise { Pos = tg, Radius = 25, SourceId = a.Id, Time = Time });
                                Info(a, "Не эти ворота — слышишь, где пищит?");
                            }
                        };
                        rt.Gates.Add((wall, use));
                        rt.Stashes.Add(use);
                        float d = Vector2.Distance(gp, rt.CratePos);
                        if (d < bestD) { bestD = d; rt.TargetGate = wall; }
                    }
                    if (rt.TargetGate == null) rt.Ready = true;
                    break;
                }
                case "ats":
                {
                    rt.NotReadyText = "Крейт придёт пневмопочтой — сначала кабели в кросс и перфокарта в коммутатор";
                    rt.CableSpots.AddRange(Marks("cable"));
                    foreach (var cp in rt.CableSpots)
                    {
                        AddPickup(PickupKind.LocalItem, cp);
                        var pk = Pickups[^1];
                        pk.Item = "rt_cable";
                        rt.Cables.Add(pk);
                    }
                    rt.Capsules.AddRange(Marks("capsule"));
                    rt.Pipe.AddRange(Marks("pipe"));
                    var patch = new Stash { Pos = Mark("patch") ?? rt.M.Center, Verb = "Воткнуть кабель в кросс", Sound = "fuse", Seconds = 1.5f, Noise = 15, Repeatable = true };
                    patch.Require = a => rt.CablesIn >= 3 ? "Кросс собран — теперь перфокарта в коммутатор" : a.Count("rt_cable") > 0 ? null : $"В кроссе не хватает перемычек ({rt.CablesIn}/3) — кабели лежат по зданию";
                    patch.OnDone = a =>
                    {
                        a.Take("rt_cable", 1);
                        rt.CablesIn++;
                        Info(a, rt.CablesIn < 3 ? $"Перемычка на месте ({rt.CablesIn}/3)" : "Кросс собран — коммутатор ожил");
                        if (rt.CablesIn >= 3) Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = a.Id, Pos = patch.Pos, Text = "power" });
                    };
                    rt.Stashes.Add(patch);
                    var sw = new Stash { Pos = Mark("switch") ?? rt.M.Center, Verb = "Перфокарту в коммутатор", Sound = "reader", Seconds = 2, Noise = 30 };
                    sw.Require = a => rt.CablesIn < 3 ? "Коммутатор мёртвый — сначала собери кросс (3 кабеля)" : a.Count("punchcard_1") > 0 ? null : "Нужна перфокарта I (лежит на РТ тира 0)";
                    sw.OnDone = a =>
                    {
                        a.Take("punchcard_1", 1);
                        rt.CapsuleTo = rt.Capsules.Count > 0 ? Rng.Next(rt.Capsules.Count) : -1;
                        rt.CapsuleLeft = 20;
                        rt.RefreshLeft = Tier1Rt.RefreshTime;
                        Info(a, "Перфокарта принята — капсула пошла по трубам. Слушай, куда свистит");
                    };
                    rt.Stashes.Add(sw);
                    Bags.Remove(rt.Crate);   // крейт приходит капсулой
                    break;
                }
                case "depot":
                {
                    rt.NotReadyText = "Вагон с крейтом стоит в тупике — переведи три стрелки, чтобы он скатился к рампе";
                    var levers = Marks("lever");
                    for (int i = 0; i < levers.Count && i < 3; i++)
                    {
                        int k = i;
                        var lv = new Stash { Pos = levers[i] + new Vector2(0, 0.8f), Verb = "Перевести стрелку", Sound = "pry", Seconds = 2, Noise = 30, Repeatable = true };
                        lv.Require = a => rt.WagonMoving || rt.Ready ? "Вагон уже пошёл" : null;
                        lv.OnDone = a =>
                        {
                            rt.LeverOn[k] = !rt.LeverOn[k];
                            int on = 0; foreach (var b in rt.LeverOn) if (b) on++;
                            Info(a, $"Стрелка {(rt.LeverOn[k] ? "переведена" : "возвращена")} ({on}/3)");
                            if (on >= Math.Min(3, levers.Count)) { rt.WagonMoving = true; rt.RefreshLeft = Tier1Rt.RefreshTime; Info(a, "Вагон тронулся — к рампе!"); }
                        };
                        rt.Levers.Add(lv);
                        rt.Stashes.Add(lv);
                    }
                    rt.WagonStart = Mark("wagon_start") ?? rt.CratePos;
                    rt.WagonEnd = Mark("wagon_end") ?? rt.CratePos;
                    rt.Wagon = DecorNear(rt.WagonStart, "wagon");
                    if (rt.Wagon != null) MovingDecors.Add(rt.Wagon);
                    rt.Crate.Pos = rt.CratePos;
                    break;
                }
                case "quarry":
                {
                    rt.NotReadyText = "Контейнер с крейтом завален — перенеси его краном на площадку (кабина крана)";
                    rt.Pivot = Mark("crane_pivot") ?? rt.M.Center;
                    rt.ContStart = Mark("container_start") ?? rt.CratePos;
                    rt.ContEnd = Mark("container_end") ?? rt.CratePos;
                    rt.Boom = DecorNear(rt.Pivot, "crane_boom");
                    rt.Container = DecorNear(rt.ContStart, "container");
                    if (rt.Boom != null) { rt.Boom.Pivot = true; rt.Boom.Lift = 9f; MovingDecors.Add(rt.Boom); }
                    if (rt.Container != null) MovingDecors.Add(rt.Container);
                    rt.Cabin = new Stash { Pos = Mark("crane_cabin") ?? rt.Pivot, Verb = "Управлять краном (сиди в кабине)", Sound = "crane", Seconds = 40, Noise = 90, KeepProgress = true, Range = 2.2f };
                    rt.Cabin.OnDone = a => { rt.Ready = true; rt.RefreshLeft = Tier1Rt.RefreshTime; Info(a, "Контейнер на площадке — крейт можно открыть перфокартой"); };
                    rt.Stashes.Add(rt.Cabin);
                    rt.Crate.Pos = rt.ContStart;
                    break;
                }
            }
            SetCrane(rt, 0);
        }

        /// <summary>Положение стрелы и контейнера по доле пути крана 0..1.</summary>
        void SetCrane(Tier1Rt rt, float k)
        {
            if (rt.Key != "quarry") return;
            float a0 = MathF.Atan2(rt.ContStart.Y - rt.Pivot.Y, rt.ContStart.X - rt.Pivot.X), a1 = MathF.Atan2(rt.ContEnd.Y - rt.Pivot.Y, rt.ContEnd.X - rt.Pivot.X);
            float da = a1 - a0;
            while (da > MathF.PI) da -= MathF.Tau;
            while (da < -MathF.PI) da += MathF.Tau;
            float ang = a0 + da * k, r = Vector2.Distance(rt.Pivot, rt.ContStart) * (1 - k) + Vector2.Distance(rt.Pivot, rt.ContEnd) * k;
            var p = rt.Pivot + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r;
            if (rt.Boom != null) { rt.Boom.Pos = rt.Pivot; rt.Boom.Rot = ang * 180f / MathF.PI; }
            if (rt.Container != null) rt.Container.Pos = p;
            if (!rt.Crate.Unlocked) rt.Crate.Pos = p;
        }

        void UseTier1Crate(Actor a, Tier1Rt rt)
        {
            if (!rt.Ready) { Info(a, rt.NotReadyText); return; }
            if (rt.CrateTimer > 0) { Info(a, $"Крейт открывается: {MathF.Ceiling(rt.CrateTimer)} с"); return; }
            if (rt.Key == "ats") { rt.Crate.Unlocked = true; return; }   // перфокарта уже ушла в коммутатор
            if (a.Take("punchcard_1", 1) == 0) { Info(a, "Крейт заперт: нужна перфокарта I (лежит на РТ тира 0)"); return; }
            rt.CrateTimer = Tier1Rt.CrateDelay;
            rt.RefreshLeft = Tier1Rt.RefreshTime;
            Info(a, $"Перфокарта принята — крейт откроется через {Tier1Rt.CrateDelay:0} с");
        }

        void StepTier1()
        {
            foreach (var rt in Tier1)
            {
                foreach (var s in rt.Stashes) StepStash(s);
                rt._noiseT -= Dt;
                bool tick = rt._noiseT <= 0;
                if (tick) rt._noiseT = 1f;
                if (rt.AlarmLeft > 0)
                {
                    rt.AlarmLeft -= Dt;
                    if (tick) { Noises.Add(new Noise { Pos = rt.Alarm.Pos, Radius = 70, SourceId = -1, Time = Time }); Events.Add(new SimEvent { Kind = SimEventKind.Machine, Pos = rt.Alarm.Pos, Text = "alarm" }); }
                }
                if (rt.CapsuleLeft > 0)
                {
                    rt.CapsuleLeft -= Dt;
                    if (tick)
                    {
                        // свист в трубах: звук идёт по меткам трубы, к концу — у станции прихода
                        var at = rt.Pipe.Count > 0 ? rt.Pipe[Math.Clamp((int)((1 - rt.CapsuleLeft / 20f) * rt.Pipe.Count), 0, rt.Pipe.Count - 1)] : rt.M.Center;
                        if (rt.CapsuleLeft < 6 && rt.CapsuleTo >= 0) at = rt.Capsules[rt.CapsuleTo];
                        Noises.Add(new Noise { Pos = at, Radius = 45, SourceId = -1, Time = Time });
                        Events.Add(new SimEvent { Kind = SimEventKind.Machine, Pos = at, Text = "pipe" });
                    }
                    if (rt.CapsuleLeft <= 0)
                    {
                        rt.Crate.Pos = rt.CapsuleTo >= 0 ? rt.Capsules[rt.CapsuleTo] + new Vector2(0, 0.9f) : rt.M.Center;
                        FillPunchCrate(rt.Crate, rt.Key == "ats");
                        rt.Crate.Unlocked = true;
                        if (!Bags.Contains(rt.Crate)) Bags.Add(rt.Crate);
                        rt.Ready = true;
                        Events.Add(new SimEvent { Kind = SimEventKind.Machine, Pos = rt.Crate.Pos, Text = "crate" });
                    }
                }
                if (rt.WagonMoving)
                {
                    rt.WagonT = MathF.Min(1, rt.WagonT + Dt / 25f);
                    var p = Vector2.Lerp(rt.WagonStart, rt.WagonEnd, rt.WagonT);
                    if (rt.Wagon != null) rt.Wagon.Pos = p;
                    rt.Crate.Pos = p;
                    if (tick) { Noises.Add(new Noise { Pos = p, Radius = 55, SourceId = -1, Time = Time }); Events.Add(new SimEvent { Kind = SimEventKind.Machine, Pos = p, Text = "rail" }); }
                    if (rt.WagonT >= 1) { rt.WagonMoving = false; rt.Ready = true; }
                }
                if (rt.Key == "quarry" && !rt.Ready) SetCrane(rt, Math.Clamp(rt.Cabin.Progress / rt.Cabin.Seconds, 0, 1));
                if (rt.CrateTimer > 0)
                {
                    rt.CrateTimer -= Dt;
                    if (tick) { Noises.Add(new Noise { Pos = rt.Crate.Pos, Radius = 120, SourceId = -1, Time = Time }); Events.Add(new SimEvent { Kind = SimEventKind.Machine, Pos = rt.Crate.Pos, Text = "reader" }); }
                    if (rt.CrateTimer <= 0) { rt.Crate.Unlocked = true; Events.Add(new SimEvent { Kind = SimEventKind.Machine, Pos = rt.Crate.Pos, Text = "crate" }); }
                }
                // локальные предметы — только здесь
                if (Tick % 30 == 0)
                    foreach (var a in Actors)
                    {
                        if (a.Dead || rt.M.Contains(a.Pos, 2)) continue;
                        if (a.Count("rt_keyfob") > 0 && rt.Key == "garages") { a.Take("rt_keyfob", 99); rt.Alarm.Done = false; Info(a, "Брелок остался в Гаражах — с ним не уйти"); }
                        if (a.Count("rt_cable") > 0 && rt.Key == "ats")
                        {
                            int n = a.Take("rt_cable", 99);
                            foreach (var c in rt.Cables) if (n > 0 && !c.Available) { c.RespawnLeft = 0; n--; }
                            Info(a, "Кабели остались на АТС — вынести нельзя");
                        }
                    }
                if (rt.RefreshLeft > 0 && (rt.RefreshLeft -= Dt) <= 0)
                {
                    if (Actors.Exists(a => !a.Dead && a.Team == Team.Player && rt.M.Contains(a.Pos, 4))) { rt.RefreshLeft = 60; continue; }
                    ResetTier1(rt);
                }
            }
            if (MovingDecors.Count > 0 && Tick % 2 == 0) PrefabVersion++;
        }

        void ResetTier1(Tier1Rt rt)
        {
            foreach (var s in rt.Stashes) { s.Done = false; s.ActorId = 0; s.Progress = 0; }
            rt.Ready = false;
            rt.CrateTimer = 0;
            rt.AlarmLeft = 0;
            foreach (var (w, _) in rt.Gates) if (!Walls.Contains(w)) { Walls.Add(w); _wallGrid.Insert(w, w.Wall.Min, w.Wall.Max); }
            if (rt.Key == "garages" && rt.TargetGate == null) rt.Ready = true;
            rt.CablesIn = 0;
            foreach (var c in rt.Cables) c.RespawnLeft = 0;
            rt.CapsuleLeft = 0;
            for (int i = 0; i < rt.LeverOn.Length; i++) rt.LeverOn[i] = false;
            rt.WagonT = 0;
            rt.WagonMoving = false;
            if (rt.Wagon != null) rt.Wagon.Pos = rt.WagonStart;
            FillPunchCrate(rt.Crate, rt.Key == "ats");
            rt.Crate.Pos = rt.Key == "depot" ? rt.WagonStart : rt.CratePos;
            if (rt.Key == "ats") Bags.Remove(rt.Crate);
            else if (!Bags.Contains(rt.Crate)) Bags.Add(rt.Crate);
            SetCrane(rt, 0);
            PrefabVersion++;
        }
    }
}
