using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Чертежи баз (docs/design/bot-brain.md, этап 2): база — данные по стадиям (кибитка → мейн → расширение → финал),
    /// квадраты и треугольники; исполнитель строит недостающее (и восстанавливает снесённое), ставит ящики и спальник,
    /// вешает замки, улучшает до уровня стадии, чинит, переходит к следующей стадии по условию.
    /// Координаты чертежа — клетки от угла базы (север — строка 0, вход — с юга).
    /// </summary>
    public sealed partial class PlayerBot
    {
        // ---------- данные чертежа ----------

        /// <summary>
        /// Элемент: пол-квадрат (I, J); треугольник — к ребру сетки (I, J, Dir) со стороны Side (±1);
        /// стена/проём/окно/дверь — на ребре сетки (I, J, Dir) или на ребре треугольника (TriEdge 0: A–C, 1: C–B).
        /// </summary>
        sealed class Piece
        {
            public StructKind Kind;
            public int I, J, Dir = -1, Side, TriEdge = -1;
            public bool Lock;   // повесить замок (дверь наружу)
        }

        sealed class Stage
        {
            public string Name;
            public int Tier;                         // до какого материала улучшить всё построенное
            public readonly List<Piece> Pieces = new();
            public readonly List<(string item, Vector2 at)> Deploys = new();   // ящики, спальник
            public Func<PlayerBot, bool> Gate = _ => true;   // когда можно начинать стадию
        }

        sealed class BasePlan
        {
            public string Name;
            public Vector2 Claim;                    // где шкаф (метры от угла базы)
            public Vector2 Furnace, BenchStand;
            public Vector2[] Benches;
            public readonly List<Stage> Stages = new();
        }

        // конструктор чертежей: короткие записи
        static Piece F(int i, int j) => new() { Kind = StructKind.Foundation, I = i, J = j };
        static Piece T(int i, int j, int dir, int side) => new() { Kind = StructKind.TriFoundation, I = i, J = j, Dir = dir, Side = side };
        static Piece E(StructKind k, int i, int j, int dir, bool lk = false) => new() { Kind = k, I = i, J = j, Dir = dir, Lock = lk };
        static Piece TE(StructKind k, int i, int j, int dir, int side, int edge) => new() { Kind = k, I = i, J = j, Dir = dir, Side = side, TriEdge = edge };
        static IEnumerable<Piece> TriWalls(int i, int j, int dir, int side) { yield return TE(StructKind.Wall, i, j, dir, side, 0); yield return TE(StructKind.Wall, i, j, dir, side, 1); }

        /// <summary>Общая основа: кибитка 2×1 с тамбуром (дверь на юг, внутренняя дверь на север — в будущий мейн), потом мейн 2×2.</summary>
        static void ClassicCore(BasePlan p)
        {
            var s1 = new Stage { Name = "кибитка", Tier = 1 };
            s1.Pieces.AddRange(new[]
            {
                F(0, 1), F(1, 1),
                E(StructKind.Doorway, 0, 2, 0), E(StructKind.Wall, 1, 2, 0),                // юг: вход
                E(StructKind.Wall, 0, 1, 1), E(StructKind.Wall, 2, 1, 1),                   // запад, восток
                E(StructKind.Doorway, 0, 1, 0), E(StructKind.Wall, 1, 1, 0),                // север: дверь в будущий мейн
                E(StructKind.Door, 0, 2, 0, true), E(StructKind.Door, 0, 1, 0),
            });
            s1.Deploys.Add(("deploy_box", new Vector2(3.55f, 3.6f)));
            s1.Deploys.Add(("deploy_sleepbag", new Vector2(2.4f, 2.4f)));
            var s2 = new Stage { Name = "мейн", Tier = 2 };
            s2.Pieces.AddRange(new[]
            {
                F(0, 0), F(1, 0),
                E(StructKind.Wall, 0, 0, 1), E(StructKind.Wall, 2, 0, 1), E(StructKind.Wall, 0, 0, 0), E(StructKind.Wall, 1, 0, 0),
            });
            s2.Deploys.Add(("deploy_box", new Vector2(3.55f, 0.45f)));   // основной лут — в дальней комнате
            p.Stages.Add(s1); p.Stages.Add(s2);
            p.Claim = new Vector2(3.6f, 2.4f);
        }

        static void Utility(BasePlan p)
        {
            p.Furnace = new Vector2(-1.2f, 6.4f);
            p.Benches = new[] { new Vector2(3.0f, 6.5f), new Vector2(4.5f, 6.5f), new Vector2(6.0f, 6.7f) };
            p.BenchStand = new Vector2(4.5f, 7.8f);
        }

        static BasePlan ClassicPlan()
        {
            var p = new BasePlan { Name = "Классика: тамбур → мейн → соты с востока → металл" };
            ClassicCore(p);
            var s3 = new Stage { Name = "соты с востока", Tier = 2, Gate = b => b.MyBenchLevel() >= 1 };
            foreach (var (i, j) in new[] { (2, 0), (2, 1) }) { s3.Pieces.Add(T(i, j, 1, +1)); s3.Pieces.AddRange(TriWalls(i, j, 1, +1)); }
            var s4 = new Stage { Name = "финал: соты с запада, металл", Tier = 3, Gate = b => b.MyBenchLevel() >= 2 && b.MetalTotal() >= 300 };
            foreach (var (i, j) in new[] { (0, 0), (0, 1) }) { s4.Pieces.Add(T(i, j, 1, -1)); s4.Pieces.AddRange(TriWalls(i, j, 1, -1)); }
            p.Stages.Add(s3); p.Stages.Add(s4);
            Utility(p);
            return p;
        }

        static BasePlan HoneycombPlan()
        {
            var p = new BasePlan { Name = "Соты: тамбур → мейн → соты с боков → соты с севера, металл" };
            ClassicCore(p);
            var s3 = new Stage { Name = "соты с боков", Tier = 2, Gate = b => b.MyBenchLevel() >= 1 };
            foreach (var (i, j, side) in new[] { (2, 0, +1), (2, 1, +1), (0, 0, -1), (0, 1, -1) }) { s3.Pieces.Add(T(i, j, 1, side)); s3.Pieces.AddRange(TriWalls(i, j, 1, side)); }
            var s4 = new Stage { Name = "финал: соты с севера, металл", Tier = 3, Gate = b => b.MyBenchLevel() >= 2 && b.MetalTotal() >= 300 };
            foreach (var i in new[] { 0, 1 }) { s4.Pieces.Add(T(i, 0, 0, -1)); s4.Pieces.AddRange(TriWalls(i, 0, 0, -1)); }
            p.Stages.Add(s3); p.Stages.Add(s4);
            Utility(p);
            return p;
        }

        static BasePlan TrianglePlan()
        {
            var p = new BasePlan { Name = "Треугольная: кибитка-ромб → мейн-ромб → сота с севера → металл" };
            var s1 = new Stage { Name = "кибитка-ромб", Tier = 1 };
            s1.Pieces.AddRange(new[] { F(0, 1), T(0, 1, 1, -1), T(1, 1, 1, +1) });
            s1.Pieces.AddRange(TriWalls(0, 1, 1, -1));
            s1.Pieces.AddRange(TriWalls(1, 1, 1, +1));
            s1.Pieces.AddRange(new[] { E(StructKind.Doorway, 0, 2, 0), E(StructKind.Doorway, 0, 1, 0), E(StructKind.Door, 0, 2, 0, true), E(StructKind.Door, 0, 1, 0) });
            s1.Deploys.Add(("deploy_box", new Vector2(2.5f, 3.0f)));        // в восточном треугольнике
            s1.Deploys.Add(("deploy_sleepbag", new Vector2(0.33f, 2.5f)));
            var s2 = new Stage { Name = "мейн-ромб", Tier = 2 };
            s2.Pieces.AddRange(new[] { F(0, 0), T(0, 0, 1, -1), T(1, 0, 1, +1), E(StructKind.Wall, 0, 0, 0) });
            s2.Pieces.AddRange(TriWalls(0, 0, 1, -1));
            s2.Pieces.AddRange(TriWalls(1, 0, 1, +1));
            s2.Deploys.Add(("deploy_box", new Vector2(2.5f, 1.0f)));
            var s3 = new Stage { Name = "сота с севера", Tier = 2, Gate = b => b.MyBenchLevel() >= 1 };
            s3.Pieces.Add(T(0, 0, 0, -1)); s3.Pieces.AddRange(TriWalls(0, 0, 0, -1));
            var s4 = new Stage { Name = "финал: металл", Tier = 3, Gate = b => b.MyBenchLevel() >= 2 && b.MetalTotal() >= 300 };
            p.Stages.AddRange(new[] { s1, s2, s3, s4 });
            p.Claim = new Vector2(-0.5f, 3.0f);   // в западном треугольнике
            Utility(p);
            return p;
        }

        BasePlan _plan;
        int _stage;
        bool InsideBase => _w.FloorAt(_me.Pos) is Structure f && f.OwnerId == _me.Id;
        public string PlanInfo => _plan == null ? "" : $"{_plan.Name.Split(':')[0]}, стадия {_stage + 1}/{_plan.Stages.Count} «{_plan.Stages[_stage].Name}»";

        /// <summary>Выбор плана по характеру: осторожный — соты, экономный/быстрый — треугольная кибитка, иначе классика.</summary>
        BasePlan PickPlan(out string why)
        {
            if (_caution > 0.75f) { why = "осторожный — побольше буферов"; return HoneycombPlan(); }
            if (_greed > 0.65f) { why = "жадный — дешёвый старт, всё в фарм"; return TrianglePlan(); }
            why = "ровный характер — проверенная классика";
            return ClassicPlan();
        }

        // ---------- геометрия элементов ----------

        Vector2 Corner => new(_origin.i * Building.Cell, _origin.j * Building.Cell);
        Vector2 AtPlan(Vector2 meters) => Corner + meters;

        (Vector2 a, Vector2 b) GridEdge(int i, int j, int dir) => Building.EdgeEnds(_origin.i + i, _origin.j + j, dir);

        Vector2[] TriOf(Piece p)
        {
            var (a, b) = GridEdge(p.I, p.J, p.Dir);
            var mid = (a + b) * 0.5f;
            var n = p.Dir == 0 ? new Vector2(0, p.Side) : new Vector2(p.Side, 0);
            return new[] { a, b, mid + n * Building.TriHeight };
        }

        (Vector2 a, Vector2 b) EdgeOf(Piece p)
        {
            if (p.TriEdge < 0) return GridEdge(p.I, p.J, p.Dir);
            var t = TriOf(p);
            return p.TriEdge == 0 ? (t[0], t[2]) : (t[2], t[1]);
        }

        Vector2 AimOf(Piece p) => p.Kind switch
        {
            StructKind.Foundation => Building.CellCenter(_origin.i + p.I, _origin.j + p.J),
            StructKind.TriFoundation => Centroid(TriOf(p)),
            _ => Mid(EdgeOf(p)),
        };

        static Vector2 Centroid(Vector2[] t) => (t[0] + t[1] + t[2]) / 3f;
        static Vector2 Mid((Vector2 a, Vector2 b) e) => (e.a + e.b) * 0.5f;

        Structure Existing(Piece p)
        {
            switch (p.Kind)
            {
                case StructKind.Foundation: return _w.FoundationAt(_origin.i + p.I, _origin.j + p.J);
                case StructKind.TriFoundation:
                    var f = _w.FloorAt(Centroid(TriOf(p)));
                    return f != null && f.Kind == StructKind.TriFoundation ? f : null;
                case StructKind.Door: { var (a, b) = EdgeOf(p); return _w.DoorByKey(Building.EdgeKey(a, b)); }
                default: { var (a, b) = EdgeOf(p); var s = _w.EdgeByKey(Building.EdgeKey(a, b)); return s != null && s.Kind == p.Kind ? s : null; }
            }
        }

        /// <summary>Треугольник чертежа открыт в дом (на его основании нет стены) — значит, внутрь можно зайти.</summary>
        bool TriOpen(Piece tri)
        {
            var (a, b) = GridEdge(tri.I, tri.J, tri.Dir);
            long key = Building.EdgeKey(a, b);
            foreach (var st in _plan.Stages)
                foreach (var p in st.Pieces)
                    if (p.Kind is StructKind.Wall or StructKind.Window && p.TriEdge < 0 && Building.EdgeKey(GridEdge(p.I, p.J, p.Dir).a, GridEdge(p.I, p.J, p.Dir).b) == key) return false;
            return true;
        }

        /// <summary>Можно ли стоять в этой точке, работая по ребру: снаружи или в доступной комнате (не в закрытой соте).</summary>
        bool StandOk(Vector2 p)
        {
            foreach (var st in _plan.Stages)
                foreach (var t in st.Pieces)
                    if (t.Kind == StructKind.TriFoundation && Building.PointInPoly(p, TriOf(t)) && !TriOpen(t)) return false;
            return true;
        }

        /// <summary>
        /// Точка рядом с целью, где можно встать и откуда достаёшь рукой: не в препятствии, не сквозь стену,
        /// не в закрытой соте; ближайшая ко мне. Так бот не «кормит шкаф» через стену и не стоит на месте ящика.
        /// </summary>
        Vector2 StandNear(Vector2 target, float r = 0.95f)
        {
            Vector2 best = target + new Vector2(0, r);
            float bd = float.MaxValue;
            for (int k = 0; k < 16; k++)
            {
                float a = k * MathF.Tau / 16;
                var c = target + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                if (_w.WalkBlocked(c, _me.Radius) || !_w.ClearLine(c, target, null, true) || (_plan != null && !StandOk(c))) continue;
                float d = Vector2.Distance(c, _me.Pos);
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        float _lastUse;

        /// <summary>Подойти и использовать конкретную цель (не «что ближе»). true — на месте.</summary>
        bool Use(object target, Vector2 pos, ref InputCmd cmd, bool deposit = false, float r = 0.95f)
        {
            if (!Reach(pos, ref cmd, r)) return false;
            if (_w.Time - _lastUse > 0.4f) { _lastUse = _w.Time; _w.BotUse(_me, target, deposit); }
            return true;
        }

        /// <summary>Подойти к цели так, чтобы достать рукой; true — на месте (прицел — на цель).</summary>
        bool Reach(Vector2 target, ref InputCmd cmd, float r = 0.95f)
        {
            bool near = Vector2.Distance(_me.Pos, target) < r + 0.45f && _w.ClearLine(_me.Pos, target, null, true);
            if (!near && !MoveTo(StandNear(target, r), 0.25f, ref cmd)) return false;
            cmd.Aim = target;
            return true;
        }

        /// <summary>Где встать для работы по элементу (стройка, молоток, замок).</summary>
        Vector2 StandFor(Piece p)
        {
            if (p.Kind == StructKind.Foundation) return StandNear(AimOf(p), 0.8f);
            if (p.Kind == StructKind.TriFoundation)
            {
                var t = TriOf(p);
                if (TriOpen(p)) return Centroid(t) + Vector2.Normalize((t[0] + t[1]) * 0.5f - Centroid(t)) * 0.9f;   // из дома
                var outer = Mid((t[0], t[2]));
                return outer + Vector2.Normalize(outer - Centroid(t)) * 0.9f;   // закрытая сота — снаружи
            }
            var (a, b) = EdgeOf(p);
            var mid = (a + b) * 0.5f;
            var d = Vector2.Normalize(b - a);
            var n = new Vector2(-d.Y, d.X);
            // в комнату (если доступна), иначе наружу
            var inside = _w.FloorAt(mid + n * 0.6f) != null ? mid + n * 0.9f : mid - n * 0.9f;
            var outside = inside == mid + n * 0.9f ? mid - n * 0.9f : mid + n * 0.9f;
            return StandOk(inside) ? inside : outside;
        }

        // ---------- место под базу ----------

        bool ChooseSitePlan()
        {
            _plan ??= PickPlan(out string why);
            var home = _me.Pos;
            for (int tries = 0; tries < 80; tries++)
            {
                float ang = (float)(_rng.NextDouble() * Math.PI * 2), dist = 15 + (float)_rng.NextDouble() * 45;
                var p = home + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * dist;
                var (ci, cj) = Building.CellOf(p);
                bool ok = true;
                // весь план с соседями (треугольники выступают на клетку) и подсобка с юга
                for (int di = -1; di <= 3 && ok; di++)
                    for (int dj = -1; dj <= 4 && ok; dj++)
                        ok = _w.FoundationGeoOk(ci + di, cj + dj, out _);
                if (!ok || !_w.CanPlaceClaim(Building.CellCenter(ci, cj), out _)) continue;
                if (_w.NearMonument(p, 40)) continue;
                _origin = (ci, cj);
                _site = Building.CellCenter(ci, cj);
                Thought($"выбрал место под базу и план «{_plan.Name}»");
                return true;
            }
            return false;
        }

        int MetalTotal()
        {
            int m = _me.Count("metal_fragments");
            if (MyBox() is Deployable b) m += InBox(b, "metal_fragments");
            return m;
        }

        // ---------- исполнитель стадий ----------

        /// <summary>Строит базу по чертежу. true — занят этим в этот тик.</summary>
        bool BuildBase(Deployable claim, ref InputCmd cmd)
        {
            var box = MyBox();
            for (int si = 0; si <= _stage; si++)
            {
                var st = _plan.Stages[si];
                // 1. недостающее (в том числе снесённое рейдом): полы → стены/проёмы → двери
                foreach (var order in new[] { 0, 1, 2 })
                    foreach (var p in st.Pieces)
                    {
                        int ord = Building.IsFloor(p.Kind) ? 0 : p.Kind == StructKind.Door ? 2 : 1;
                        if (ord != order || Existing(p) != null) continue;
                        if (BuildPiece(p, box, ref cmd)) return true;
                    }
                // 2. ящики и спальник стадии
                foreach (var (item, at) in st.Deploys)
                {
                    var spot = AtPlan(at);
                    bool placed = false;
                    foreach (var d in _w.DeploysInRadius(spot, 0.8f, _depBuf))
                        if (d.OwnerId == _me.Id && Vector2.Distance(d.Pos, spot) < 0.8f) placed = true;
                    if (placed || (item == "deploy_sleepbag" && _w.ReadyBag(_me) != null)) continue;
                    if (!Has(item))
                    {
                        if (Craft(item, ref cmd)) return true;
                        if (item == "deploy_sleepbag") { if (_me.Count("cloth") < 30) { Doing = "собирает коноплю"; if (UsePickup(PickupKind.Hemp, ref cmd)) return true; } continue; }
                        Need("wood", 75, ref cmd);
                        return true;
                    }
                    Doing = item == "deploy_box" ? "ставит ящик" : "стелет спальник";
                    if (!Reach(spot, ref cmd, 1.1f)) return true;
                    if (Hold(item, ref cmd)) Click(ref cmd);
                    return true;
                }
                // 3. замки на наружные двери
                foreach (var p in st.Pieces)
                {
                    if (!p.Lock || Existing(p) is not Structure door || door.Locked) continue;
                    if (!Has("lock_key")) { if (!Craft("lock_key", ref cmd)) Need("wood", 75, ref cmd); return true; }
                    Doing = "ставит замок";
                    if (!MoveTo(StandFor(p), 0.4f, ref cmd)) return true;
                    cmd.Aim = door.Center;
                    if (Hold("lock_key", ref cmd)) Click(ref cmd);
                    return true;
                }
            }
            // 4. улучшить всё до уровня текущей стадии; 5. чинить повреждённое
            int tier = _plan.Stages[_stage].Tier;
            for (int si = 0; si <= _stage; si++)
                foreach (var p in _plan.Stages[si].Pieces)
                {
                    if (Existing(p) is not Structure s) continue;
                    if (p.Kind == StructKind.Doorway && _w.DoorByKey(Building.EdgeKey(s.A, s.B)) != null) continue;   // молоток попадёт в дверь
                    bool upgrade = s.Tier < tier && s.Tier < Building.MaxTier;
                    bool repair = s.Hp < s.MaxHp * 0.85f;
                    if (!upgrade && !repair) continue;
                    var cost = upgrade ? Building.Cost(s.Kind, s.Tier + 1) : Array.ConvertAll(Building.Cost(s.Kind, s.Tier), c => (c.id, Math.Max(1, c.n / 5)));
                    foreach (var (id, n) in cost) if (!Fetch(id, n, box, ref cmd)) return true;
                    Doing = upgrade ? $"улучшает до «{Building.TierNames[s.Tier + 1]}»: {Building.KindName(s.Kind)}" : $"чинит: {Building.KindName(s.Kind)}";
                    if (!MoveTo(StandFor(p), 0.4f, ref cmd)) return true;
                    var face = s.Center;
                    cmd.Aim = face;
                    if (Hold("tool_hammer", ref cmd) && Vector2.Dot(_me.FacingDir, Vector2.Normalize(face - _me.Pos)) > 0.9f) cmd.Fire = true;
                    return true;
                }
            // 6. следующая стадия
            if (_stage + 1 < _plan.Stages.Count && _plan.Stages[_stage + 1].Gate(this))
            {
                _w.Milestone(_me, $"база: готова стадия «{_plan.Stages[_stage].Name}», дальше — «{_plan.Stages[_stage + 1].Name}»");
                Thought($"база: «{_plan.Stages[_stage].Name}» готова → начинаю «{_plan.Stages[_stage + 1].Name}»");
                _stage++;
                return true;
            }
            return false;
        }

        /// <summary>Нужно n штук: из кармана, из своего ящика, иначе добыть. true — уже есть.</summary>
        bool Fetch(string id, int n, Deployable box, ref InputCmd cmd)
        {
            if (_me.Count(id) >= n) return true;
            if (box != null && TakeFromBox(box, id, n, ref cmd)) return false;
            if (id == "scrap") { GetScrap(n, box, ref cmd); return false; }
            if (id == "metal_fragments") { _metalGoal = Math.Max(_metalGoal, n + 60); Doing = "копит металл"; Gather(NodeKind.Metal, ref cmd); return false; }
            return Need(id, n, ref cmd);
        }

        bool BuildPiece(Piece p, Deployable box, ref InputCmd cmd)
        {
            int tier0 = p.Kind == StructKind.Door ? 1 : 0;
            foreach (var (id, n) in Building.Cost(p.Kind, tier0)) if (!Fetch(id, n, box, ref cmd)) return true;
            Doing = $"строит: {Building.KindName(p.Kind)}";
            var aim = AimOf(p);
            var stand = StandFor(p);
            // сам стою на месте будущей стены — отойти; мешает другой — следующий элемент
            if (!Building.IsFloor(p.Kind))
            {
                var (a, b) = EdgeOf(p);
                bool self = false, other = false;
                foreach (var (min, max, _, _) in Building.Boxes(p.Kind, a, b, false))
                    foreach (var o in _w.Actors)
                        if (!o.Dead && !o.Spectator && Geo.CircleBox(o.Pos, o.Radius, min, max)) { if (o == _me) self = true; else other = true; }
                if (other) return false;
                if ((self || Vector2.Distance(_me.Pos, aim) > 4f) && !MoveTo(stand, 0.3f, ref cmd)) return true;
            }
            else if (Vector2.Distance(_me.Pos, aim) > 4f && !MoveTo(aim, 3f, ref cmd)) return true;
            if (!Hold("build_plan", ref cmd)) return true;
            cmd.Aim = aim;
            if (_me.BuildPiece != p.Kind) { cmd.SetPiece = (int)p.Kind; return true; }
            Click(ref cmd);
            return true;
        }

        /// <summary>Хозяйство фермера: инструменты → место и шкаф → база по чертежу → содержание → печь → прогрессия → вооружение → рейд → сейч → фарм.</summary>
        void Plan(ref InputCmd cmd)
        {
            int wood = _me.Count("wood"), stone = _me.Count("stone");

            // 1. инструменты
            if (!Has("tool_hatchet")) { if (Craft("tool_hatchet", ref cmd)) { Gather(NodeKind.Tree, ref cmd); return; } }
            if (!Has("tool_pickaxe") && Has("tool_hatchet")) { if (Craft("tool_pickaxe", ref cmd)) { Gather(NodeKind.Stone, ref cmd); return; } }
            if (!Has("tool_hatchet") || !Has("tool_pickaxe")) { Gather(wood < 200 ? NodeKind.Tree : NodeKind.Stone, ref cmd); return; }
            if (Archetype == BotArchetype.Roamer)
            {
                if (wood < 400) Gather(NodeKind.Tree, ref cmd);
                else if (stone < 200) Gather(NodeKind.Stone, ref cmd);
                else { Doing = "бродит"; Wander(ref cmd); }
                return;
            }

            // 2. план базы, место и шкаф
            var claim = MyClaim();
            if (claim != null && _site == null)
            {
                // после загрузки или смерти: угол базы — от шкафа по чертежу
                _plan ??= PickPlan(out _);
                var corner = claim.Pos - _plan.Claim;
                _origin = Building.CellOf(corner + new Vector2(0.2f));
                _site = Building.CellCenter(_origin.i, _origin.j);
                _stage = 0;
                while (_stage + 1 < _plan.Stages.Count && _plan.Stages[_stage + 1].Pieces.TrueForAll(p => Existing(p) != null)) _stage++;
            }
            if (_site == null)
            {
                if (_plan == null) { _plan = PickPlan(out string why); Thought($"план базы «{_plan.Name}»: {why}"); }
                if (!ChooseSitePlan()) { Doing = "ищет место"; Wander(ref cmd); return; }
            }
            if (claim == null)
            {
                if (!Has("deploy_claim")) { if (!Craft("deploy_claim", ref cmd)) Gather(NodeKind.Tree, ref cmd); return; }
                Doing = "ставит шкаф";
                var spot = AtPlan(_plan.Claim);
                if (!Reach(spot, ref cmd, 1.1f)) return;
                if (Hold("deploy_claim", ref cmd)) Click(ref cmd);
                return;
            }

            // 3. План и Киянка
            if (!Has("build_plan")) { if (!Craft("build_plan", ref cmd)) Gather(NodeKind.Tree, ref cmd); return; }
            if (!Has("tool_hammer")) { if (!Craft("tool_hammer", ref cmd)) Gather(NodeKind.Tree, ref cmd); return; }

            // 4. содержание: запас на ~2 часа (раньше стройки — база не должна гнить)
            if (_w.ClaimHoursLeft(claim) < 1.0f && _w.ClaimLoad(claim).b > 0)
            {
                bool have = true;
                foreach (var (id, perHour) in _w.UpkeepPerHourOf(claim))
                    if (!Need(id, (int)MathF.Ceiling(perHour * 2), ref cmd)) { have = false; break; }
                if (!have) return;
                Doing = "кормит шкаф";
                Use(claim, claim.Pos, ref cmd);
                return;
            }

            // 5. база по чертежу
            if (BuildBase(claim, ref cmd)) return;
            var box = MyBox();
            if (box == null) return;

            // 6. печь у дома: металл на будущее (руда → печь → металл)
            int metal = MetalTotal();
            var furnace = MyFurnace();
            if (furnace == null && metal < _metalGoal)
            {
                if (!Has("deploy_furnace"))
                {
                    if (Craft("deploy_furnace", ref cmd)) return;
                    if (!Need("stone", 100, ref cmd) || !Need("wood", 50, ref cmd)) return;
                    return;
                }
                Doing = "ставит печь";
                var spot = AtPlan(_plan.Furnace);
                if (!Reach(spot, ref cmd, 1.3f)) return;
                if (Hold("deploy_furnace", ref cmd)) Click(ref cmd);
                return;
            }
            if (furnace != null)
            {
                bool ready = (!furnace.Burning && furnace.OutMetal + furnace.OutCharcoal > 0) || furnace.OutMetal >= 40;   // забираем пачкой, а не по 2
                bool haveOre = (_me.Count("metal_ore") >= 60 || _me.Count("sulfur_ore") >= 40) && _me.Count("wood") >= 40;
                if (ready || (haveOre && !furnace.Burning))
                {
                    Doing = ready ? "забирает металл из печи" : "загружает печь";
                    Use(furnace, furnace.Pos, ref cmd, false, 1.2f);
                    return;
                }
                if (metal < _metalGoal && !furnace.Burning && _me.Count("metal_ore") < 60 && !Need("metal_ore", 60, ref cmd)) return;
            }
            // 7. прогрессия, вооружение, рейд
            if (Progress(box, metal, ref cmd)) return;
            if (Arm(box, furnace, ref cmd)) return;
            if (Raid(box, furnace, ref cmd)) return;

            // 8. сейч: отнести добычу
            if (wood > 500 + 400 * _greed || stone > 300 + 300 * _greed)
            {
                Doing = "несёт добычу домой";
                Use(box, box.Pos, ref cmd, deposit: true);
                return;
            }

            // 9. фарм: чего меньше — то и добываем
            Gather(stone < wood * 0.6f ? NodeKind.Stone : NodeKind.Tree, ref cmd);
        }
    }
}
