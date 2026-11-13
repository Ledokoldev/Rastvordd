using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Этажи (D60). Учёт построек (полы, рёбра, двери) и сетка стен — свои на каждом этаже. «Контекст этажа» _cf
    /// выбирает, с каким этажом работает старый код стройки, дверей, установленного и столкновений; по умолчанию — земля,
    /// поэтому боты, РТ и всё, что не знает про этажи, живут как раньше.
    /// Здесь же: опора и устойчивость (как в Rust), обрушение, лестницы и люки, падение, стрельба и обзор между этажами,
    /// задел «воздух» (высота полёта, попадания с земли).
    /// </summary>
    public sealed partial class World
    {
        sealed class LevelIdx
        {
            public readonly Dictionary<(int, int), Structure> Found = new();   // квадратные полы: фундамент/перекрытие, лестница (верх), люк
            public readonly List<Structure> Tris = new();
            public readonly Dictionary<long, Structure> Edges = new();
            public readonly Dictionary<long, Structure> Doors = new();
            public readonly Dictionary<long, (Vector2 a, Vector2 b, int n)> FloorEdges = new();
            public int Count => Found.Count + Tris.Count + Edges.Count + Doors.Count;
        }

        readonly LevelIdx[] _lv = NewLevels();
        readonly SpatialHash<MapWall>[] _grids = NewGrids();
        int _cf;   // этаж контекста

        static LevelIdx[] NewLevels()
        {
            var a = new LevelIdx[Building.MaxFloor + 1];
            for (int i = 0; i < a.Length; i++) a[i] = new LevelIdx();
            return a;
        }

        static SpatialHash<MapWall>[] NewGrids()
        {
            var a = new SpatialHash<MapWall>[Building.MaxFloor + 1];
            for (int i = 0; i < a.Length; i++) a[i] = new SpatialHash<MapWall>(GridCell);
            return a;
        }

        /// <summary>Контекст этажа: пока жив — учёт построек, стены, установленное относятся к этому этажу.</summary>
        public readonly struct FloorCtx : IDisposable
        {
            readonly World _w; readonly int _prev;
            public FloorCtx(World w, int floor) { _w = w; _prev = w._cf; w._cf = Math.Clamp(floor, 0, Building.MaxFloor); }
            public void Dispose() => _w._cf = _prev;
        }
        public FloorCtx Ctx(int floor) => new(this, floor);
        public int CtxFloor => _cf;

        SpatialHash<MapWall> GridOf(int floor) => _grids[Math.Clamp(floor, 0, Building.MaxFloor)];

        /// <summary>Есть ли хоть что-то выше земли (нет — этажная логика не нужна: старый путь целиком).</summary>
        public bool AnyUpper
        {
            get { for (int f = 1; f <= Building.MaxFloor; f++) if (_lv[f].Count > 0) return true; return false; }
        }

        readonly List<ResourceNode> _noNodes = new();
        List<ResourceNode> NoNodes() { _noNodes.Clear(); return _noNodes; }

        /// <summary>Установленное только текущего этажа (на земле без верхних этажей — без фильтра, как раньше).</summary>
        List<Deployable> OnCtxFloor(List<Deployable> l)
        {
            if (_cf == 0 && !_anyUpperDeploy) return l;
            int f = _cf;
            l.RemoveAll(d => d.Floor != f);
            return l;
        }
        bool _anyUpperDeploy;
        void UpdateUpperDeployFlag() { _anyUpperDeploy = false; foreach (var d in Deployables) if (d.Floor > 0) { _anyUpperDeploy = true; break; } }

        /// <summary>Все полы этажа (квадраты, треугольники, лестницы, люки).</summary>
        IEnumerable<Structure> PlatesOf(int floor)
        {
            var lv = _lv[Math.Clamp(floor, 0, Building.MaxFloor)];
            foreach (var s in lv.Found.Values) yield return s;
            foreach (var s in lv.Tris) yield return s;
        }

        /// <summary>Пол этажа под точкой (или null).</summary>
        public Structure PlateAt(int floor, Vector2 p)
        {
            if (floor < 0 || floor > Building.MaxFloor) return null;
            using var ctx = Ctx(floor);
            return FloorAt(p);
        }

        static IEnumerable<long> EdgeKeysOf(Vector2[] poly)
        {
            for (int i = 0; i < poly.Length; i++) yield return Building.EdgeKey(poly[i], poly[(i + 1) % poly.Length]);
        }

        // ---------- опора и устойчивость (как в Rust) ----------

        public const float MinStab = 0.25f;     // ниже — не держится: не построить, а построенное обрушится
        public const float WallLoss = 0.1f;     // перекрытие на стенах этажа ниже: −10 % за этаж
        public const float Cantilever = 0.35f;  // консоль от соседнего перекрытия: −35 % за клетку (2 клетки от стены)

        readonly Dictionary<int, float> _stab = new();
        int _stabVer = -1;

        /// <summary>Устойчивость постройки 0..1 (фундамент и всё на земле — 1).</summary>
        public float StabilityOf(Structure s)
        {
            if (s.Floor == 0) return 1f;
            EnsureStab();
            return _stab.GetValueOrDefault(s.Id);
        }

        void EnsureStab()
        {
            if (_stabVer == StructVersion) return;
            ComputeStab(_stab, null);
            _stabVer = StructVersion;
        }

        static bool Bearing(StructKind k) => k is StructKind.Wall or StructKind.Doorway or StructKind.Window;

        float BelowSupport(StructKind kind, Vector2[] poly, int i, int j, int floor, Dictionary<int, float> st, Structure exclude)
        {
            if (floor <= 0) return 1f;
            var below = _lv[floor - 1];
            float best = 0;
            if (kind == StructKind.Stairs)
                return below.Found.TryGetValue((i, j), out var bp) && bp != exclude ? st.GetValueOrDefault(bp.Id, floor - 1 == 0 ? 1f : 0f) - WallLoss : 0f;
            foreach (var key in EdgeKeysOf(poly))
                if (below.Edges.TryGetValue(key, out var w) && w != exclude && Bearing(w.Kind))
                    best = MathF.Max(best, st.GetValueOrDefault(w.Id, floor - 1 == 0 ? 1f : 0f) - WallLoss);
            return best;
        }

        /// <summary>Устойчивость всех построек: этаж за этажом, полы — от стен ниже и консолью от соседей, рёбра — от своих полов.</summary>
        void ComputeStab(Dictionary<int, float> st, Structure exclude)
        {
            st.Clear();
            var byEdge = new Dictionary<long, List<Structure>>();
            for (int f = 0; f <= Building.MaxFloor; f++)
            {
                var lv = _lv[f];
                if (lv.Count == 0) continue;
                byEdge.Clear();
                var plates = new List<Structure>();
                foreach (var p in PlatesOf(f))
                {
                    if (p == exclude) continue;
                    plates.Add(p);
                    foreach (var k in EdgeKeysOf(p.Poly)) { if (!byEdge.TryGetValue(k, out var l)) byEdge[k] = l = new List<Structure>(2); l.Add(p); }
                    st[p.Id] = f == 0 || p.Static ? 1f : BelowSupport(p.Kind, p.Poly, p.I, p.J, f, st, exclude);   // РТ (D61) держится сама
                }
                if (f > 0)
                    for (int it = 0; it < 24; it++)
                    {
                        bool changed = false;
                        foreach (var p in plates)
                            foreach (var k in EdgeKeysOf(p.Poly))
                                foreach (var nb in byEdge[k])
                                {
                                    if (nb == p) continue;
                                    float v = st[nb.Id] - Cantilever;
                                    if (v > st[p.Id] + 1e-4f) { st[p.Id] = v; changed = true; }
                                }
                        if (!changed) break;
                    }
                foreach (var e in lv.Edges.Values)
                {
                    if (e == exclude) continue;
                    float v = 0;
                    if (byEdge.TryGetValue(e.EdgeKey, out var l)) foreach (var p in l) v = MathF.Max(v, st[p.Id]);
                    st[e.Id] = v;
                }
                foreach (var e in lv.Doors.Values) st[e.Id] = lv.Edges.TryGetValue(e.EdgeKey, out var fr) ? st.GetValueOrDefault(fr.Id) : 0f;
            }
        }

        /// <summary>Устойчивость нового пола на этаже floor (ещё не поставлен).</summary>
        public float NewPlateStab(StructKind kind, Vector2[] poly, int i, int j, int floor)
        {
            if (floor == 0) return 1f;
            EnsureStab();
            float v = BelowSupport(kind, poly, i, j, floor, _stab, null);
            var keys = new HashSet<long>(EdgeKeysOf(poly));
            foreach (var p in PlatesOf(floor))
            {
                if (Vector2.DistanceSquared(p.Center, Building.Center(kind, i, j, poly[0], poly[1], poly.Length > 2 ? poly[2] : poly[0])) > 16) continue;
                foreach (var k in EdgeKeysOf(p.Poly))
                    if (keys.Contains(k)) v = MathF.Max(v, _stab.GetValueOrDefault(p.Id) - Cantilever);
            }
            return v;
        }

        /// <summary>Что обрушится, если убрать s (null — ничего): для запрета сноса киянкой.</summary>
        public Structure WouldCollapse(Structure s)
        {
            if (!AnyUpper) return null;
            var st = new Dictionary<int, float>();
            ComputeStab(st, s);
            for (int f = 1; f <= Building.MaxFloor; f++)
                foreach (var p in PlatesOf(f))
                    if (p != s && !p.Static && st.GetValueOrDefault(p.Id) < MinStab) return p;
            return null;
        }

        int _removeDepth;
        bool _settling;

        /// <summary>После сноса или подрыва: всё, что потеряло опору, обрушивается (по цепочке, этаж за этажом).</summary>
        void SettleStructures()
        {
            if (_settling || !AnyUpper) return;
            _settling = true;
            try
            {
                for (int guard = 0; guard < 64; guard++)
                {
                    ComputeStab(_stab, null);
                    _stabVer = StructVersion;
                    Structure weak = null;
                    for (int f = 1; f <= Building.MaxFloor && weak == null; f++)
                        foreach (var p in PlatesOf(f))
                            if (!p.Static && _stab.GetValueOrDefault(p.Id) < MinStab) { weak = p; break; }
                    if (weak == null) break;
                    Events.Add(new SimEvent { Kind = SimEventKind.Info, Pos = weak.Center, Z = Building.FloorZ(weak.Floor), Text = "обрушение" });
                    RemoveStructure(weak);
                }
            }
            finally { _settling = false; }
        }

        /// <summary>Пол верхнего этажа убран: установленное и сумки на нём падают этажом ниже (или на землю).</summary>
        void DropFromPlate(Structure plate)
        {
            if (plate.Floor <= 0) return;
            var poly = plate.Poly;
            foreach (var d in Deployables)
            {
                if (d.Floor != plate.Floor || !Building.PointInPoly(d.Pos, poly)) continue;
                int to = d.Floor - 1;
                while (to > 0 && PlateAt(to, d.Pos) == null) to--;
                d.Floor = to; d.FloorId = 0; d.Slots = 0;
            }
            foreach (var b in Bags)
            {
                if (b.Floor != plate.Floor || !Building.PointInPoly(b.Pos, poly)) continue;
                int to = b.Floor - 1;
                while (to > 0 && PlateAt(to, b.Pos) == null) to--;
                b.Floor = to;
            }
            UpdateUpperDeployFlag();
        }

        // ---------- стройка этажами: куда встанет элемент ----------

        /// <summary>
        /// Этаж, на который встанет элемент Плана у точки: свой этаж, а если там занято — этажом выше («ставишь на занятое —
        /// встаёт сверху»): фундамент на фундамент — перекрытие, стена на стену — стена второго этажа. Лестница — со своего этажа вверх.
        /// </summary>
        public int BuildLevel(Actor a, StructKind kind, Vector2 at)
        {
            int pf = Math.Clamp(a.Floor, 0, Building.MaxFloor);
            int top = Math.Min(pf + 1, Building.MaxFloor);
            switch (kind)
            {
                case StructKind.Stairs: return pf + 1;   // > MaxFloor — отказ в CanBuild
                case StructKind.Foundation:
                case StructKind.Hatch:
                {
                    var cell = Building.CellOf(at);
                    for (int f = kind == StructKind.Hatch ? Math.Max(1, pf) : pf; f <= top; f++)
                        if (!_lv[f].Found.ContainsKey(cell) && !TriCovers(f, cell)) return f;
                    return kind == StructKind.Hatch ? top : pf;
                }
                case StructKind.TriFoundation:
                    for (int f = pf; f <= top; f++)
                    {
                        using var ctx = Ctx(f);
                        if (TriSpot(at, out _, out _)) return f;
                    }
                    return pf;
                default:
                    for (int f = pf; f <= top; f++)
                    {
                        using var ctx = Ctx(f);
                        if (!NearestFloorEdge(at, 1.3f, out _, out _, out long key, out _)) continue;
                        bool free = kind == StructKind.Door
                            ? _edges.TryGetValue(key, out var fr) && fr.Kind == StructKind.Doorway && !_doors.ContainsKey(key)
                            : !_edges.ContainsKey(key);
                        if (free) return f;
                    }
                    return pf;
            }
        }

        bool TriCovers(int floor, (int i, int j) cell)
        {
            var sq = Building.SquarePoly(cell.i, cell.j);
            var c = Building.CellCenter(cell.i, cell.j);
            foreach (var t in _lv[floor].Tris) if (Vector2.Distance(t.Center, c) < 3 && Building.PolysOverlap(sq, t.Poly)) return true;
            return false;
        }

        /// <summary>Призрак для игрока: куда и на какой этаж встанет элемент.</summary>
        public (Vector2[] poly, Vector2 a, Vector2 b, int floor) GhostFor(Actor a, StructKind kind, Vector2 at)
        {
            int lv = BuildLevel(a, kind, at);
            if (lv > Building.MaxFloor) return (null, default, default, lv);
            using var ctx = Ctx(lv);
            if (kind is StructKind.Stairs or StructKind.Hatch) { var (ci, cj) = Building.CellOf(at); return (Building.SquarePoly(ci, cj), default, default, lv); }
            var g = GhostOf(kind, at);
            return (g.poly, g.a, g.b, lv);
        }

        /// <summary>Проверки элемента на верхнем этаже (контекст — его этаж): опора, место, лестница и люк.</summary>
        bool CanBuildUpper(Actor a, StructKind kind, Vector2 at, out string why)
        {
            why = "";
            int f = _cf;
            if (kind is StructKind.Foundation or StructKind.Hatch or StructKind.Stairs)
            {
                var (ci, cj) = Building.CellOf(at);
                if (_foundations.ContainsKey((ci, cj)) || TriCovers(f, (ci, cj))) { why = "место занято"; return false; }
                if (kind == StructKind.Stairs)
                {
                    var bp = _lv[f - 1].Found.GetValueOrDefault((ci, cj));
                    if (bp == null || bp.Kind != StructKind.Foundation) { why = "лестница ставится на пол (фундамент или перекрытие)"; return false; }
                    if (SlotsUsed(bp) != 0) { why = "на полу стоит установленное"; return false; }
                    foreach (var o in Actors)
                        if (!o.Dead && !o.Spectator && o.Floor == f && Building.CellOf(o.Pos) == (ci, cj)) { why = "мешает человек"; return false; }
                }
                float stab = NewPlateStab(kind, Building.SquarePoly(ci, cj), ci, cj, f);
                if (stab < MinStab) { why = kind == StructKind.Stairs ? "нет опоры" : "нет опоры: нужны стены этажа ниже или соседнее перекрытие"; return false; }
                return ClaimAllows(a, kind, (ci, cj), out _, out why);
            }
            if (kind == StructKind.TriFoundation)
            {
                if (!TriSpot(at, out var tri, out why)) return false;
                if (NewPlateStab(kind, tri, 0, 0, f) < MinStab) { why = "нет опоры"; return false; }
                return ClaimAllows(a, kind, Building.CellOf((tri[0] + tri[1] + tri[2]) / 3f), out _, out why);
            }
            return CanBuildEdge(a, kind, at, out why);
        }

        /// <summary>Направление подъёма лестницы: от того, кто ставит, к клетке (0 +X, 1 +Y, 2 −X, 3 −Y).</summary>
        static int StairDirFrom(Vector2 from, (int i, int j) cell)
        {
            var d = Building.CellCenter(cell.i, cell.j) - from;
            return MathF.Abs(d.X) >= MathF.Abs(d.Y) ? (d.X >= 0 ? 0 : 2) : (d.Y >= 0 ? 1 : 3);
        }

        static Vector2 DirVec(int dir) => dir switch { 0 => new Vector2(1, 0), 1 => new Vector2(0, 1), 2 => new Vector2(-1, 0), _ => new Vector2(0, -1) };

        /// <summary>Где на лестнице точка: 0 — низ (вход снизу), 1 — верх.</summary>
        public static float StairT(Structure st, Vector2 p)
        {
            var c = Building.CellCenter(st.I, st.J);
            return 0.5f + Vector2.Dot(p - c, DirVec(st.Dir)) / Building.Cell;
        }

        /// <summary>Перила и низ лестницы: внизу — бока и торец под верхом, наверху — бока и край пролёта (вход только с нужной стороны).</summary>
        void AddStairBoxes(Structure s)
        {
            var c = Building.CellCenter(s.I, s.J);
            var u = DirVec(s.Dir);
            var v = new Vector2(-u.Y, u.X);
            float half = Building.Cell / 2f, th = 0.12f;
            (Vector2, Vector2) Box(Vector2 p0, Vector2 p1) => (Vector2.Min(p0, p1), Vector2.Max(p0, p1));
            var sideA = Box(c + v * half - u * half, c + v * (half - th) + u * half);
            var sideB = Box(c - v * half - u * half, c - v * (half - th) + u * half);
            var hiEnd = Box(c + u * half - v * half, c + u * (half - th) + v * half);
            var loEnd = Box(c - u * half - v * half, c - u * (half - th) + v * half);
            void Add((Vector2 min, Vector2 max) b, int floor, float h)
            {
                var w = new MapWall { Wall = new Wall(b.min, b.max, h), Kind = WallKind.Built, Owner = s, Floor = floor };
                s.Boxes.Add(w);
                if (floor == 0) Walls.Add(w);
                GridOf(floor).Insert(w, b.min, b.max);
            }
            int lo = s.Floor - 1;
            Add(sideA, lo, Building.WallH); Add(sideB, lo, Building.WallH); Add(hiEnd, lo, Building.WallH);
            Add(sideA, s.Floor, 1.0f); Add(sideB, s.Floor, 1.0f); Add(loEnd, s.Floor, 1.0f);
        }

        /// <summary>Лестница, по которой может идти стоящий на этаже floor в этой точке (верх — его этаж или этаж выше).</summary>
        public Structure StairsAt(Vector2 p, int floor)
        {
            var cell = Building.CellOf(p);
            if (floor >= 1 && floor <= Building.MaxFloor && _lv[floor].Found.TryGetValue(cell, out var s) && s.Kind == StructKind.Stairs) return s;
            if (floor + 1 <= Building.MaxFloor && _lv[floor + 1].Found.TryGetValue(cell, out s) && s.Kind == StructKind.Stairs) return s;
            return null;
        }

        // ---------- люди на этажах ----------

        /// <summary>После движения: лестница поднимает и опускает, без пола под ногами — падение.</summary>
        void StepFloor(Actor a)
        {
            if (a.Flying) return;
            if (a.Floor == 0 && a.Z == 0 && !AnyUpper) return;
            if (!a.IsBot && StairsAt(a.Pos, a.Floor) is Structure st)
            {
                float t = Math.Clamp(StairT(st, a.Pos), 0f, 1f);
                int lo = st.Floor - 1;
                a.Z = Building.FloorZ(lo) + t * Building.Storey;
                a.Floor = t >= 0.5f ? st.Floor : lo;
                return;
            }
            if (a.Floor > 0 && PlateAt(a.Floor, a.Pos) == null) { Fall(a); return; }
            a.Z = Building.FloorZ(a.Floor);
        }

        /// <summary>Падение на этаж ниже (до ближайшего пола или земли): урон от высоты.</summary>
        void Fall(Actor a)
        {
            int to = a.Floor - 1;
            while (to > 0 && PlateAt(to, a.Pos) == null) to--;
            float drop = a.Z - Building.FloorZ(to);
            a.Floor = to;
            a.Z = Building.FloorZ(to);
            using (Ctx(to)) ResolveCollisions(a);
            float dmg = drop <= 2.2f ? 0f : (drop - 2.2f) * 20f;   // этаж ≈ 8, два ≈ 60, три — насмерть
            if (dmg > 0) Damage(a, dmg, -1, a.Pos);
            Info(a, $"Падение с {drop:0.#} м" + (dmg > 0 ? $" · −{dmg:0} HP" : ""));
            Events.Add(new SimEvent { Kind = SimEventKind.Wear, ActorId = a.Id, Pos = a.Pos, Text = "fall", Value = drop });
        }

        /// <summary>Люк рядом: снизу — люк над головой (в этой клетке), сверху — люк под ногами.</summary>
        public Structure HatchNear(Actor a)
        {
            if (a.Flying) return null;
            var cell = Building.CellOf(a.Pos);
            int up = a.Floor + 1;
            if (up <= Building.MaxFloor && _lv[up].Found.TryGetValue(cell, out var h) && h.Kind == StructKind.Hatch) return h;
            if (a.Floor >= 1)
                foreach (var s in _lv[a.Floor].Found.Values)
                    if (s.Kind == StructKind.Hatch && Vector2.Distance(s.Center, a.Pos) < 1.4f) return s;
            return null;
        }

        /// <summary>E у люка: подняться по лестнице наверх или спуститься вниз (в центр клетки).</summary>
        void UseHatch(Actor a, Structure h)
        {
            bool down = h.Floor == a.Floor;
            a.Floor = down ? h.Floor - 1 : h.Floor;
            a.Pos = h.Center;
            a.Z = Building.FloorZ(a.Floor);
            a.Vel = Vector2.Zero;
            Events.Add(new SimEvent { Kind = SimEventKind.Door, ActorId = a.Id, OtherId = h.Id, Pos = h.Center, Z = Building.FloorZ(h.Floor), Value = 1 });
            Info(a, down ? $"Спустился на {FloorName(a.Floor)}" : $"Поднялся на {FloorName(a.Floor)}");
        }

        public static string FloorName(int f) => f == 0 ? "землю" : $"{f + 1}-й этаж";

        // ---------- стрельба и обзор между этажами ----------

        public const float EyeH = 1.6f;
        static float BodyTop(Actor a) => a.Z + (a.Crouching ? 1.15f : 1.85f);
        static float ChestZ(Actor a) => a.Z + (a.Crouching ? 0.8f : Hitbox.GunH);

        /// <summary>Есть ли что-то выше земли, что делает обзор/пули объёмными (этажи или летящие).</summary>
        bool Needs3D(Actor x, Actor y) => x.Z > 0.3f || y.Z > 0.3f || x.Floor != y.Floor;

        /// <summary>
        /// Линия в объёме: от (a, za) до (b, zb). Мешают стены каждого этажа, через который она идёт (по высоте: подоконник,
        /// парапет и низкие укрытия — только ниже своего верха), стволы деревьев (до 7 м), полы этажей, которые она пересекает
        /// (лестничный пролёт пропускает). ignore — своя постройка (её не считаем).
        /// </summary>
        public bool ClearLine3(Vector2 a, float za, Vector2 b, float zb, Structure ignore = null)
        {
            float lo = MathF.Min(za, zb), hi = MathF.Max(za, zb);
            int f0 = Math.Clamp((int)MathF.Floor(lo / Building.Storey), 0, Building.MaxFloor);
            int f1 = Math.Clamp((int)MathF.Floor(hi / Building.Storey), 0, Building.MaxFloor);
            for (int f = f0; f <= f1; f++)
            {
                using var ctx = Ctx(f);
                float bz = Building.FloorZ(f);
                foreach (var w in WallsAlong(a, b))
                {
                    if (ignore != null && w.Owner != null && (w.Owner == ignore || SameEdge(w.Owner, ignore))) continue;
                    if (!Geo.SegmentAabb(a, b, w.Wall.Min, w.Wall.Max, out float t)) continue;
                    float z = za + (zb - za) * t;
                    float h = w.PassBullets ? Building.WindowH : w.Wall.Height;
                    if (z >= bz - 0.05f && z <= bz + h) return false;
                }
                if (f == 0)
                    foreach (var n in NodesAlong(a, b))
                        if (n.BlocksVision && Geo.SegmentCircle(a, b, n.Pos, n.Radius * 0.9f, out float t) && za + (zb - za) * t < 7f) return false;
            }
            if (MathF.Abs(zb - za) > 1e-3f)
                for (int L = 1; L <= Building.MaxFloor; L++)
                {
                    float pz = Building.FloorZ(L);
                    if (pz <= lo + 0.05f || pz >= hi - 0.05f) continue;
                    float t = (pz - za) / (zb - za);
                    var pl = PlateAt(L, Vector2.Lerp(a, b, t));
                    if (pl != null && pl != ignore && pl.Kind != StructKind.Stairs) return false;
                }
            return true;
        }

        /// <summary>Видит ли наблюдатель человека на другом этаже (или в воздухе): дальность и конус — по плану, линия — в объёме.</summary>
        bool CanSeeActor3(Actor viewer, Actor target, float range, float coneDeg)
        {
            var d = target.Pos - viewer.Pos;
            float dist = d.Length();
            if (dist > range) return false;
            float dz = MathF.Abs(target.Z - viewer.Z);
            if (dist < 2.5f && dz < 1.5f) return true;
            if (dist > 1.0f && MathF.Abs(Geo.AngleDiff(viewer.Facing, MathF.Atan2(d.Y, d.X))) > coneDeg * 0.5f * MathF.PI / 180f) return false;
            float tz = target.Z + (target.Crouching ? 0.9f : 1.5f);
            return ClearLine3(viewer.Pos, viewer.Z + EyeH, target.Pos, tz);
        }

        /// <summary>Высота точки прицела, если клиент её не прислал (боты): человек у прицела — на него, иначе ровно.</summary>
        float AutoAimZ(Actor a, Vector2 aim)
        {
            float best = 1.3f, z = a.Z + Hitbox.GunH;
            foreach (var o in Actors)
            {
                if (o == a || o.Dead || o.Spectator) continue;
                float d = Vector2.Distance(o.Pos, aim);
                if (d < best) { best = d; z = ChestZ(o); }
            }
            return z;
        }

        /// <summary>Наклон пули: ровно, если цель на твоей высоте; иначе — к высоте прицела (вниз с крыши, вверх в окно).</summary>
        float BulletDz(Actor a, Vector2 muzzle)
        {
            float z0 = a.Z + Hitbox.GunH;
            if (float.IsNaN(a.AimZ)) a.AimZ = AutoAimZ(a, a.LastAim);
            if (MathF.Abs(a.AimZ - z0) < 0.6f) return 0f;
            float dist = MathF.Max(1f, Vector2.Distance(a.LastAim, muzzle));
            return Math.Clamp((a.AimZ - z0) / dist, -2.5f, 2.5f);
        }

        /// <summary>Пуля в объёме (не ровно на земле): стены этажей по высоте, полы, земля, деревья, люди по росту. true — пуля кончилась.</summary>
        bool StepBullet3(Projectile p, Actor owner)
        {
            float stepLen = MathF.Min(p.Speed * Dt, p.RangeLeft);
            var a = p.Pos;
            var b = p.Pos + p.Dir * stepLen;
            float za = p.Z, zb = p.Z + p.Dz * stepLen;
            float bestT = 2;
            MapWall hitWall = null; Structure hitPlate = null; ResourceNode hitNode = null; Actor hitActor = null;
            float lo = MathF.Min(za, zb), hi = MathF.Max(za, zb);
            int f0 = Math.Clamp((int)MathF.Floor(lo / Building.Storey), 0, Building.MaxFloor);
            int f1 = Math.Clamp((int)MathF.Floor(hi / Building.Storey), 0, Building.MaxFloor);
            for (int f = f0; f <= f1; f++)
            {
                using var ctx = Ctx(f);
                float bz = Building.FloorZ(f);
                foreach (var w in WallsAlong(a, b))
                {
                    if (!Geo.SegmentAabb(a, b, w.Wall.Min, w.Wall.Max, out float t) || t >= bestT) continue;
                    float z = za + (zb - za) * t;
                    float h = w.PassBullets ? Building.WindowH : w.Wall.Height;
                    if (z >= bz - 0.05f && z <= bz + h) { bestT = t; hitWall = w; }
                }
                if (f == 0)
                    foreach (var n in NodesAlong(a, b))
                    {
                        if (!Geo.SegmentCircle(a, b, n.Pos, n.Radius, out float t) || t >= bestT) continue;
                        float nh = n.Kind == NodeKind.Tree ? 8f : n.Kind == NodeKind.Barrel ? 1.1f : 0.9f;
                        if (za + (zb - za) * t < nh) { bestT = t; hitNode = n; hitWall = null; }
                    }
            }
            if (MathF.Abs(zb - za) > 1e-4f)
            {
                for (int L = 1; L <= Building.MaxFloor; L++)
                {
                    float pz = Building.FloorZ(L);
                    if (pz <= lo || pz >= hi) continue;
                    float t = (pz - za) / (zb - za);
                    if (t >= bestT) continue;
                    var pl = PlateAt(L, Vector2.Lerp(a, b, t));
                    if (pl != null && pl.Kind != StructKind.Stairs) { bestT = t; hitPlate = pl; hitWall = null; hitNode = null; }
                }
                if (zb < 0 && za >= 0) { float t = za / (za - zb); if (t < bestT) { bestT = t; hitPlate = null; hitWall = null; hitNode = null; } }
            }
            foreach (var act in Actors)
            {
                if (act.Dead || act.Spectator || act.Id == p.OwnerId) continue;
                if (owner != null && owner.Team == Team.Npc && act.Team == Team.Npc) continue;
                if (!Hitbox.Hit(act, a, b, out float t) || t >= bestT) continue;
                float z = za + (zb - za) * t;
                if (z < act.Z - 0.1f || z > BodyTop(act)) continue;
                bestT = t; hitActor = act; hitWall = null; hitPlate = null; hitNode = null;
            }
            if (bestT <= 1)
            {
                var hitPos = a + (b - a) * bestT;
                float hz = za + (zb - za) * bestT;
                if (hitActor != null) BulletHit(hitActor, p.Pos, p.Dir, p.Damage * Gunplay.Falloff(p.EffRange, p.MaxRange, p.MaxRange - p.RangeLeft + stepLen * bestT), p.OwnerId, hitPos);
                else
                {
                    if (hitWall?.Owner is Structure st) DamageStructure(st, p.Damage * Building.BulletMult(st.Kind, st.Tier), p.OwnerId);
                    if (hitPlate != null) DamageStructure(hitPlate, p.Damage * Building.BulletMult(hitPlate.Kind, hitPlate.Tier), p.OwnerId);
                    if (hitNode != null && hitNode.Kind == NodeKind.Barrel && Nodes.Contains(hitNode)) HitBarrel(hitNode, p.Damage, p.OwnerId);
                    Events.Add(new SimEvent { Kind = SimEventKind.HitWall, Pos = hitPos, Dir = p.Dir, OtherId = p.OwnerId, Z = MathF.Max(0, hz - Hitbox.GunH) });
                }
                return true;
            }
            p.Pos = b;
            p.Z = zb;
            p.RangeLeft -= stepLen;
            return p.RangeLeft <= 0;
        }

        // ---------- задел «воздух» ----------

        /// <summary>Отладка: взлететь на высоту Building.AirZ или сесть (на землю или крышу под собой).</summary>
        void ToggleFly(Actor a)
        {
            if (!a.Flying)
            {
                a.Flying = true;
                a.Z = Building.AirZ;
                a.Floor = Building.MaxFloor;
                Info(a, $"Полёт: высота {Building.AirZ:0} м (отладка, задел под вертолёты)");
                return;
            }
            a.Flying = false;
            int to = Building.MaxFloor;
            while (to > 0 && PlateAt(to, a.Pos) == null) to--;
            a.Floor = to;
            a.Z = Building.FloorZ(to);
            using (Ctx(to)) ResolveCollisions(a);
            Info(a, "Посадка");
        }
    }
}
