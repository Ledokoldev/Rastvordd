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

        /// <summary>
        /// Общая основа: кибитка 2×1 с тамбуром (дверь на юг, вторая — на север, в будущий мейн; обе под замком: пока мейна нет,
        /// северная дверь тоже наружная), потом мейн 2×2 — одна комната с мастерской: печь и верстаки стоят внутри, а не во дворе.
        /// Проход от двери до верстаков и печи свободен (западная половина), всё установленное — вдоль стен.
        /// </summary>
        static void ClassicCore(BasePlan p)
        {
            var s1 = new Stage { Name = "кибитка", Tier = 1 };
            s1.Pieces.AddRange(new[]
            {
                F(0, 1), F(1, 1),
                E(StructKind.Doorway, 0, 2, 0), E(StructKind.Wall, 1, 2, 0),                // юг: вход
                E(StructKind.Wall, 0, 1, 1), E(StructKind.Wall, 2, 1, 1),                   // запад, восток
                E(StructKind.Doorway, 0, 1, 0), E(StructKind.Wall, 1, 1, 0),                // север: дверь в будущий мейн
                E(StructKind.Door, 0, 2, 0, true), E(StructKind.Door, 0, 1, 0, true),
            });
            s1.Deploys.Add(("deploy_box", new Vector2(3.55f, 3.6f)));
            s1.Deploys.Add(("deploy_sleepbag", new Vector2(2.4f, 2.4f)));
            var s2 = new Stage { Name = "мейн", Tier = 2 };
            s2.Pieces.AddRange(new[]
            {
                F(0, 0), F(1, 0), F(0, -1), F(1, -1),
                E(StructKind.Wall, 0, 0, 1), E(StructKind.Wall, 0, -1, 1),                  // запад
                E(StructKind.Wall, 2, 0, 1), E(StructKind.Wall, 2, -1, 1),                  // восток
                E(StructKind.Wall, 0, -1, 0), E(StructKind.Wall, 1, -1, 0),                 // север
            });
            s2.Deploys.Add(("deploy_box", new Vector2(3.4f, 1.4f)));   // основной лут — в мейне, у восточной стены
            p.Stages.Add(s1); p.Stages.Add(s2);
            p.Claim = new Vector2(3.6f, 2.4f);
            // мастерская мейна: верстаки вдоль северной стены, печь у восточной; работать — из середины комнаты
            p.Furnace = new Vector2(3.2f, 0.3f);
            p.Benches = new[] { new Vector2(0.8f, -1.2f), new Vector2(2.0f, -1.2f), new Vector2(3.2f, -1.2f) };
            p.BenchStand = new Vector2(1.3f, 0.3f);
        }

        static BasePlan ClassicPlan()
        {
            var p = new BasePlan { Name = "Классика: тамбур → мейн с мастерской → соты с востока → металл" };
            ClassicCore(p);
            var s3 = new Stage { Name = "соты с востока", Tier = 2, Gate = b => b.MyBenchLevel() >= 1 };
            foreach (var (i, j) in new[] { (2, 0), (2, 1) }) { s3.Pieces.Add(T(i, j, 1, +1)); s3.Pieces.AddRange(TriWalls(i, j, 1, +1)); }
            var s4 = new Stage { Name = "финал: соты с запада, металл", Tier = 3, Gate = b => b.MyBenchLevel() >= 2 && b.MetalTotal() >= 300 };
            foreach (var (i, j) in new[] { (0, 0), (0, 1) }) { s4.Pieces.Add(T(i, j, 1, -1)); s4.Pieces.AddRange(TriWalls(i, j, 1, -1)); }
            p.Stages.Add(s3); p.Stages.Add(s4);
            return p;
        }

        static BasePlan HoneycombPlan()
        {
            var p = new BasePlan { Name = "Соты: тамбур → мейн с мастерской → соты с боков → соты с севера, металл" };
            ClassicCore(p);
            var s3 = new Stage { Name = "соты с боков", Tier = 2, Gate = b => b.MyBenchLevel() >= 1 };
            foreach (var (i, j, side) in new[] { (2, 0, +1), (2, 1, +1), (0, 0, -1), (0, 1, -1) }) { s3.Pieces.Add(T(i, j, 1, side)); s3.Pieces.AddRange(TriWalls(i, j, 1, side)); }
            var s4 = new Stage { Name = "финал: соты с севера, металл", Tier = 3, Gate = b => b.MyBenchLevel() >= 2 && b.MetalTotal() >= 300 };
            foreach (var i in new[] { 0, 1 }) { s4.Pieces.Add(T(i, -1, 0, -1)); s4.Pieces.AddRange(TriWalls(i, -1, 0, -1)); }
            p.Stages.Add(s3); p.Stages.Add(s4);
            return p;
        }

        /// <summary>
        /// Треугольная: кибитка-ромб (квадрат и два треугольника, обе двери под замком), мейн-ромб на две клетки вглубь —
        /// печь у северной стены, верстаки и ящик в треугольниках, проход посередине.
        /// </summary>
        static BasePlan TrianglePlan()
        {
            var p = new BasePlan { Name = "Треугольная: кибитка-ромб → мейн-ромб с мастерской → сота с севера → металл" };
            var s1 = new Stage { Name = "кибитка-ромб", Tier = 1 };
            s1.Pieces.AddRange(new[] { F(0, 1), T(0, 1, 1, -1), T(1, 1, 1, +1) });
            s1.Pieces.AddRange(TriWalls(0, 1, 1, -1));
            s1.Pieces.AddRange(TriWalls(1, 1, 1, +1));
            s1.Pieces.AddRange(new[] { E(StructKind.Doorway, 0, 2, 0), E(StructKind.Doorway, 0, 1, 0), E(StructKind.Door, 0, 2, 0, true), E(StructKind.Door, 0, 1, 0, true) });
            s1.Deploys.Add(("deploy_box", new Vector2(2.5f, 3.0f)));        // в восточном треугольнике
            s1.Deploys.Add(("deploy_sleepbag", new Vector2(0.33f, 2.5f)));
            var s2 = new Stage { Name = "мейн-ромб", Tier = 2 };
            s2.Pieces.AddRange(new[] { F(0, 0), F(0, -1), T(0, 0, 1, -1), T(1, 0, 1, +1), T(0, -1, 1, -1), T(1, -1, 1, +1), E(StructKind.Wall, 0, -1, 0) });
            foreach (var (i, j, side) in new[] { (0, 0, -1), (1, 0, +1), (0, -1, -1), (1, -1, +1) }) s2.Pieces.AddRange(TriWalls(i, j, 1, side));
            s2.Deploys.Add(("deploy_box", new Vector2(2.35f, 1.0f)));
            var s3 = new Stage { Name = "сота с севера", Tier = 2, Gate = b => b.MyBenchLevel() >= 1 };
            s3.Pieces.Add(T(0, -1, 0, -1)); s3.Pieces.AddRange(TriWalls(0, -1, 0, -1));
            var s4 = new Stage { Name = "финал: металл", Tier = 3, Gate = b => b.MyBenchLevel() >= 2 && b.MetalTotal() >= 300 };
            p.Stages.AddRange(new[] { s1, s2, s3, s4 });
            p.Claim = new Vector2(-0.5f, 3.0f);   // в западном треугольнике
            p.Furnace = new Vector2(1.0f, -1.2f);
            p.Benches = new[] { new Vector2(-0.35f, -1.0f), new Vector2(2.35f, -1.0f), new Vector2(-0.35f, 1.0f) };
            p.BenchStand = new Vector2(1.0f, 0.4f);
            return p;
        }

        BasePlan _plan;
        int _stage;
        bool InsideBase => _w.FloorAt(_me.Pos) is Structure f && f.OwnerId == _me.Id;
        public string PlanInfo => _plan == null ? "" : $"{_plan.Name.Split(':')[0]}, стадия {_stage + 1}/{_plan.Stages.Count} «{_plan.Stages[_stage].Name}»";

        /// <summary>Выбор плана по характеру: осторожный — соты, экономный/быстрый — треугольная кибитка, иначе классика.</summary>
        BasePlan PickPlan(out string why)
        {
            // отладка: --botplan=tri|honey|classic — всем фермерам один план
            var force = Array.Find(Environment.GetCommandLineArgs(), a => a.StartsWith("--botplan="));
            if (force != null) { why = "задано отладкой"; return force.Substring(10) switch { "tri" => TrianglePlan(), "honey" => HoneycombPlan(), _ => ClassicPlan() }; }
            if (Archetype == BotArchetype.Roamer) { why = "бродяга — быстрая кибитка, всё в снаряжение"; return TrianglePlan(); }
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
                if (_w.WalkBlocked(c, _me.Radius) || !_w.ClearLine(c, target, null, true) || (_plan != null && !StandOk(c)) || BadStand(c)) continue;
                float d = Vector2.Distance(c, _me.Pos);
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        // точки, куда не нашлось маршрута (свободный «карман» в углу треугольника за печью и т. п.) — минуту не выбирать
        readonly List<(Vector2 at, float until)> _badStands = new();
        bool BadStand(Vector2 c) => _badStands.Exists(b => b.until > _w.Time && Vector2.DistanceSquared(b.at, c) < 0.3f * 0.3f);

        /// <summary>Идти к месту работы; маршрута нет 3 с — место в чёрный список (следующий выбор возьмёт другое).</summary>
        bool GoStand(Vector2 stand, float tol, ref InputCmd cmd)
        {
            if (MoveTo(stand, tol, ref cmd)) return true;
            if (NoReach(stand) || (_path == null && _goalStuck > 3f && Vector2.Distance(_goalFor, stand) < 0.5f))
            {
                _badStands.RemoveAll(b => b.until <= _w.Time);
                _badStands.Add((stand, _w.Time + 60));
                _goalStuck = 0;
                _standPick = null;
            }
            return false;
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
            bool near = Vector2.Distance(_me.Pos, target) < MathF.Min(World.InteractRange - 0.1f, r + 0.75f) && _w.ClearLine(_me.Pos, target, null, true);
            if (!near)
            {
                // далеко — сначала дойти поближе (место «рукой» выбирается на подходе: издалека не видно, где свободно)
                if (Vector2.Distance(_me.Pos, target) > 9f) { MoveTo(target, 4f, ref cmd); return false; }
                // встать негде (цель в стене, в закрытой комнате) — не упираться, а отказаться: вызывающий пропустит цель
                if (StandPoint(target, r) is not Vector2 stand) { if (!NoReach(target)) { MarkNoReach(target, 90); Say("встать-негде", "к цели не подойти — встать негде, беру другую", 20); } return false; }
                if (!GoStand(stand, 0.25f, ref cmd)) return false;
            }
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
            // в комнату (если доступна), иначе наружу; место не занято установленным (в треугольнике кибитки стоит шкаф —
            // раньше бот упирался в него и стоял «строит: Стена» по 6 минут)
            var inDir = _w.FloorAt(mid + n * 0.6f) != null ? n : -n;
            foreach (var dir in new[] { inDir, -inDir })
                foreach (var off in new[] { 0.9f, 1.25f, 0.65f, 1.6f })   // киянка достаёт на 2 м
                    foreach (var slide in new[] { 0f, 0.5f, -0.5f, 0.8f, -0.8f })
                    {
                        var c = mid + dir * off + d * slide;
                        if (StandOk(c) && !_w.WalkBlocked(c, _me.Radius) && !BadStand(c)) return c;
                    }
            return mid + inDir * 0.9f;   // всё занято — как раньше: в комнату, подойдёт «насколько получится»
        }

        // ---------- место под базу ----------

        bool ChooseSitePlan()
        {
            _plan ??= PickPlan(out string why);
            // место — у «своей» РТ (по аппетиту к риску), на разумном расстоянии: за запретной зоной стройки, не на чужой базе
            if (_fav == null && !_favTried) { _favTried = true; _fav = PickFavourite(out _favWhy); }
            var home = _me.Pos;
            for (int tries = 0; tries < 160; tries++)
            {
                bool nearFav = _fav != null && tries < 120;
                float ang = (float)(_rng.NextDouble() * Math.PI * 2);
                float dist = nearFav ? _fav.Radius + 48 + (float)_rng.NextDouble() * 60 : 15 + (float)_rng.NextDouble() * 45;
                var p = (nearFav ? _fav.M.Center : home) + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * dist;
                if (!_w.Terrain.IsLand(p) || OtherBaseNear(p, tries < 100 ? 80 : 55)) continue;   // раздел 10: соседи вплотную резали друг друга часами
                var (ci, cj) = Building.CellOf(p);
                bool ok = true;
                // весь план с соседями (треугольники выступают на клетку): мейн — строки −1..0, кибитка — 1, двор у входа — 2..3
                for (int di = -1; di <= 3 && ok; di++)
                    for (int dj = -3; dj <= 3 && ok; dj++)
                        ok = _w.FoundationGeoOk(ci + di, cj + dj, out _);
                if (!ok || !_w.CanPlaceClaim(Building.CellCenter(ci, cj), out _)) continue;
                if (_w.NearMonument(p, 40)) continue;
                _origin = (ci, cj);
                _site = Building.CellCenter(ci, cj);
                lock (Sites) Sites.Add((_w, _site.Value));
                if (nearFav) { Thought($"строюсь у «{_fav.Short}» ({Vector2.Distance(p, _fav.M.Center):0} м): {_favWhy}; план «{_plan.Name}»"); _w.Milestone(_me, $"строится у РТ «{_fav.Short}»"); }
                else Thought($"выбрал место под базу и план «{_plan.Name}»{(_fav != null ? $" (у «{_fav.Short}» места не нашлось)" : "")}");
                return true;
            }
            return false;
        }

        readonly Dictionary<int, float> _hammerSkip = new();
        int _hammerId = -1;
        float _hammerSince;
        bool _favTried;
        string _favWhy;
        /// <summary>Места, выбранные ботами этого мира (шкафа ещё может не быть), — чтобы не строиться друг на друге.</summary>
        static readonly List<(World w, Vector2 p)> Sites = new();

        bool OtherBaseNear(Vector2 p, float r)
        {
            foreach (var d in _w.Deployables) if (d.Kind == DeployKind.Claim && d.OwnerId != _me.Id && Vector2.Distance(d.Pos, p) < r) return true;
            lock (Sites) foreach (var (w, q) in Sites) if (w == _w && Vector2.Distance(q, p) < r) return true;
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
                        // 90 с не встаёт (мешает своё установленное, рельеф) — пропустить на 10 мин: раньше бот часами «строил фундамент»
                        if (_pieceSkip.TryGetValue(p, out float skipTill) && skipTill > _w.Time) continue;
                        bool fresh = _pieceTry != p || _w.Time - _pieceTick > 5;
                        _pieceTick = _w.Time;
                        if (fresh) { _pieceTry = p; _pieceSince = _w.Time; }
                        else if (_w.Time - _pieceSince > 90) { _pieceSkip[p] = _w.Time + 600; _pieceTry = null; LStat("стройка: элемент не встаёт"); Thought($"{Building.KindName(p.Kind)} не встаёт на место — пока пропускаю"); continue; }
                        if (BuildPiece(p, box, ref cmd)) return true;
                    }
                // 2. замки на все двери стадии (кибитка — сразу, раньше ящика: база не стоит открытой): кодовый, если изучен и есть металл, иначе с ключом
                foreach (var p in st.Pieces)
                {
                    if (!p.Lock || Existing(p) is not Structure door || door.Locked) continue;
                    string lk = LockToUse();
                    if (!Has(lk)) { if (!Craft(lk, ref cmd)) { if (lk == "lock_code") Fetch("metal_fragments", 100, box, ref cmd); else Need("wood", 75, ref cmd); } return true; }
                    Doing = lk == "lock_code" ? "ставит кодовый замок" : "ставит замок";
                    if (!GoStand(StandFor(p), 0.4f, ref cmd)) return true;
                    cmd.Aim = door.Center;
                    if (Hold(lk, ref cmd)) Click(ref cmd);
                    return true;
                }
                // 3. ящики и спальник стадии — внутри комнат
                foreach (var (item, at) in st.Deploys)
                {
                    var spot = AtPlan(at);
                    // сетка D52: предмет встаёт в ближайший свободный слот, а не точно в точку чертежа — считаем штуки:
                    // это k-й ящик чертежа (по стадиям) — стоит, если своих ящиков у базы не меньше k
                    int ord = 0;
                    for (int sj = 0; sj <= si; sj++)
                        foreach (var (it2, at2) in _plan.Stages[sj].Deploys)
                        {
                            if (sj == si && at2 == at && it2 == item) break;
                            if (it2 == item) ord++;
                        }
                    bool placed = OwnDeployCount(Items.Get(item).Deploy) > ord;
                    if (placed || (item == "deploy_sleepbag" && _w.ReadyBag(_me) != null)) continue;
                    if (!Has(item))
                    {
                        if (Craft(item, ref cmd)) return true;
                        if (item == "deploy_sleepbag") { if (_me.Count("cloth") < 30) { Doing = "собирает коноплю"; if (UsePickup(PickupKind.Hemp, ref cmd)) return true; } continue; }
                        Need("wood", 75, ref cmd);
                        return true;
                    }
                    Doing = item == "deploy_box" ? "ставит ящик" : "стелет спальник";
                    if (PlaceDeploy(item, spot, ref cmd)) return true;
                    continue;
                }
            }
            // 3а. открытые свои двери (забыл, открыл кто-то чужой) — закрыть; 3б. замок с ключом → кодовый
            if (CloseOpenDoors(ref cmd)) return true;
            if (SwapKeyLocks(box, ref cmd)) return true;
            // 4. улучшить всё до уровня текущей стадии; 5. чинить повреждённое
            int tier = _plan.Stages[_stage].Tier;
            for (int si = 0; si <= _stage; si++)
                foreach (var p in _plan.Stages[si].Pieces)
                {
                    if (Existing(p) is not Structure s) continue;
                    if (p.Kind == StructKind.Doorway && _w.DoorByKey(Building.EdgeKey(s.A, s.B)) != null) continue;   // молоток попадёт в дверь
                    bool upgrade = s.Tier < tier && s.Tier < Building.MaxTier;
                    if (s.Kind == StructKind.Door && s.Locked && s.KeyLock) upgrade = false;   // дверь на ключе — деревянная: потеряю ключ — выломаю
                    bool repair = s.Hp < s.MaxHp * 0.85f;
                    if (!upgrade && !repair) continue;
                    if (_hammerSkip.TryGetValue(s.Id, out float skipUntil) && skipUntil > _w.Time) continue;
                    if (_hammerId != s.Id) { _hammerId = s.Id; _hammerSince = _w.Time; }
                    else if (_w.Time - _hammerSince > 60) { _hammerSkip[s.Id] = _w.Time + 300; _hammerId = -1; continue; }   // минуту не выходит (не подойти) — пока другое
                    var cost = upgrade ? Building.Cost(s.Kind, s.Tier + 1) : Array.ConvertAll(Building.Cost(s.Kind, s.Tier), c => (c.id, Math.Max(1, c.n / 5)));
                    foreach (var (id, n) in cost) if (!Fetch(id, n, box, ref cmd)) return true;
                    Doing = upgrade ? $"улучшает до «{Building.TierNames[s.Tier + 1]}»: {Building.KindName(s.Kind)}" : $"чинит: {Building.KindName(s.Kind)}";
                    var face = s.Center;
                    bool close = Vector2.Distance(_me.Pos, face) < (Building.IsFloor(s.Kind) ? 2.4f : 1.6f) && _w.ClearLine(_me.Pos, face, s, true);
                    if (!close && !GoStand(StandFor(p), 0.4f, ref cmd)) return true;
                    cmd.Aim = face;
                    if (Hold("tool_hammer", ref cmd) && Vector2.Dot(_me.FacingDir, Vector2.Normalize(face - _me.Pos)) > 0.9f) cmd.Fire = true;
                    return true;
                }
            // 6. следующая стадия
            if (_stage + 1 < _plan.Stages.Count && _plan.Stages[_stage + 1].Gate(this) && Archetype != BotArchetype.Roamer)   // бродяга строит минимум: живёт в кибитке
            {
                _w.Milestone(_me, $"база: готова стадия «{_plan.Stages[_stage].Name}», дальше — «{_plan.Stages[_stage + 1].Name}»");
                Thought($"база: «{_plan.Stages[_stage].Name}» готова → начинаю «{_plan.Stages[_stage + 1].Name}»");
                _stage++;
                return true;
            }
            return false;
        }

        // ---------- двери и замки ----------

        /// <summary>Какой замок ставить: кодовый (изучен, есть металл) — его не потерять со смертью; иначе с ключом.</summary>
        string LockToUse() => Has("lock_code") || (_me.Known.Contains("lock_code") && MetalTotal() >= 100) ? "lock_code" : "lock_key";

        IEnumerable<Structure> MyDoors()
        {
            foreach (var s in _w.Structures)
                if (s.Kind == StructKind.Door && s.OwnerId == _me.Id) yield return s;
        }

        /// <summary>Куда встать у двери с моей стороны (или с другой, если тут не встать).</summary>
        Vector2 DoorStand(Structure door, float off = 0.95f)
        {
            var d = Vector2.Normalize(door.B - door.A);
            var n = new Vector2(d.Y, -d.X);
            float side = SideOf(door, _me.Pos);
            if (side == 0) side = 1;
            var mine = door.Center + n * side * off;
            var other = door.Center - n * side * off;
            return !_w.WalkBlocked(mine, _me.Radius) && StandOk(mine) && !BadStand(mine) ? mine : other;
        }

        float _closeFailUntil;

        /// <summary>Своя дверь стоит открытой и никто в ней не стоит — подойти и закрыть («обход дома»).</summary>
        bool CloseOpenDoors(ref InputCmd cmd)
        {
            if (_w.Time < _closeFailUntil || _site is not Vector2 site || Vector2.Distance(_me.Pos, site) > 30) return false;
            foreach (var door in MyDoors())
            {
                if (!door.Open || _toClose.Exists(t => t.door == door) || !CanUnlock(door)) continue;
                bool busy = false;
                foreach (var (min, max, _, _) in Building.Boxes(StructKind.Door, door.A, door.B, false))
                    foreach (var o in _w.Actors)
                        if (!o.Dead && !o.Spectator && Geo.CircleBox(o.Pos, o.Radius + 0.1f, min, max)) busy = true;
                if (busy) continue;
                Doing = "закрывает дверь";
                if (!GoStand(DoorStand(door), 0.4f, ref cmd)) return true;
                cmd.Aim = door.Center;
                if (!_w.BotToggleDoor(_me, door)) _closeFailUntil = _w.Time + 5;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Замок с ключом — временный: ключ выпадает с трупа, и тогда бот заперт снаружи (или внутри, проснувшись в спальнике).
        /// Изучил кодовый и есть металл — выламывает свою деревянную дверь на ключе, ставит новую и вешает кодовый.
        /// </summary>
        bool SwapKeyLocks(Deployable box, ref InputCmd cmd)
        {
            if (!_me.Known.Contains("lock_code")) return false;
            foreach (var door in MyDoors())
            {
                if (!door.Locked || !door.KeyLock || !CanUnlock(door)) continue;
                if (!Has("lock_code"))
                {
                    if (MetalTotal() < 160) return false;   // металл нужнее на прогрессию — поменяем позже
                    if (Craft("lock_code", ref cmd)) return true;
                    return !Fetch("metal_fragments", 100, box, ref cmd) || true;
                }
                if (door.Open) { if (_w.BotToggleDoor(_me, door)) return true; }
                Doing = "меняет замок на кодовый: ломает старую дверь";
                return BreakDoor(door, ref cmd);
            }
            return false;
        }

        /// <summary>Выломать свою дверь инструментом (деревянная — берётся топором, камнем дольше).</summary>
        bool BreakDoor(Structure door, ref InputCmd cmd)
        {
            if (!GoStand(DoorStand(door, 0.9f), 0.35f, ref cmd)) return true;
            string tool = Has("tool_hatchet") ? "tool_hatchet" : Has("tool_pickaxe") ? "tool_pickaxe" : "tool_rock";
            cmd.Aim = door.Center;
            if (Hold(tool, ref cmd) && Vector2.Dot(_me.FacingDir, Vector2.Normalize(door.Center - _me.Pos)) > 0.9f) cmd.Fire = true;
            return true;
        }

        /// <summary>Есть своя дверь на ключе, а ключа нет.</summary>
        bool RecoverPending()
        {
            foreach (var door in MyDoors()) if (door.Locked && door.KeyLock && !CanUnlock(door)) return true;
            return false;
        }

        readonly HashSet<LootBag> _badKeyBags = new(), _keyBagOk = new();
        float _keyBagSince;   // до какого времени идти за ключом к трупу (0 — не идём)
        bool _breakSaid;

        /// <summary>
        /// Потерял ключ от своей двери (умер): забрать его с трупа, если тот ещё лежит; нет — выломать дверь
        /// (дверь на ключе деревянная), потом стройка поставит новую и повесит замок. true — занят этим.
        /// </summary>
        bool RecoverLockout(ref InputCmd cmd)
        {
            Structure lost = null;
            foreach (var door in MyDoors()) if (door.Locked && door.KeyLock && !CanUnlock(door)) { lost = door; break; }
            if (lost == null) { _keyBagSince = 0; _breakSaid = false; return false; }
            string key = "key_" + lost.Id;
            foreach (var b in _w.Bags)
            {
                if (b.Empty || _badKeyBags.Contains(b) || !b.Items.Exists(it => it.Id == key) || Vector2.Distance(b.Pos, _me.Pos) > 250) continue;
                if (!_keyBagOk.Contains(b))
                {
                    // труп по ту сторону запертой двери (проснулся в кибитке, а труп в мейне) — не достать: сразу ломать
                    bool reach = Vector2.Distance(b.Pos, _me.Pos) < 1.5f && _w.ClearLine(_me.Pos, b.Pos, null, true);
                    if (!reach && Vector2.Distance(b.Pos, _me.Pos) < 30 && Pathfinder.Find(_w, _me.Pos, b.Pos, _me.Radius, 1.2f, OwnDoor) == null) { _badKeyBags.Add(b); continue; }
                    _keyBagOk.Add(b);
                }
                // срок — по расстоянию (с пляжа идти долго): не успел — ломаем дверь
                if (_keyBagSince == 0) { _keyBagSince = _w.Time + 40 + Vector2.Distance(b.Pos, _me.Pos) / 3f; Thought("потерял ключ от своей двери — иду за ним к трупу"); }
                if (_w.Time > _keyBagSince) { _badKeyBags.Add(b); _keyBagSince = 0; continue; }
                Doing = "забирает ключ с трупа";
                Use(b, b.Pos, ref cmd);
                return true;
            }
            if (!_breakSaid) { _breakSaid = true; Thought("ключа от своей двери нет — выломаю её и поставлю новую"); }
            Doing = "ломает свою дверь: ключ потерян";
            return BreakDoor(lost, ref cmd);
        }

        /// <summary>Нужно n штук: из кармана, из своего ящика, иначе добыть. true — уже есть.</summary>
        bool Fetch(string id, int n, Deployable box, ref InputCmd cmd)
        {
            if (_me.Count(id) >= n) return true;
            if (box != null && TakeFromBox(box, id, n, ref cmd)) return false;
            if (id == "scrap") { GetScrap(n, box, ref cmd); return false; }
            if (id == "metal_fragments") { _metalGoal = Math.Max(_metalGoal, n + 60); SmeltOrMine(ref cmd); return false; }
            return Need(id, n, ref cmd);
        }

        /// <summary>
        /// Нужен металл: руда в кармане — в печь (с деревом на топливо), готовое — забрать; руды нет — добыть.
        /// Раньше стройка «финала» звала только добычу руды, и бот часами копал руду (8000 в кармане), не доходя до печи.
        /// </summary>
        void SmeltOrMine(ref InputCmd cmd)
        {
            var f = MyFurnace();
            int ore = _me.Count("metal_ore");
            if (f != null)
            {
                if (f.OutMetal > 0 && !f.Burning) { Doing = "забирает металл из печи"; Use(f, f.Pos, ref cmd, false, 1.2f); return; }
                if ((ore >= 60 || f.Ore >= 30) && !f.Burning)
                {
                    int wood = (int)MathF.Min(300, (ore + f.Ore) * 0.5f + 10 - f.Fuel);
                    if (wood > 0 && _me.Count("wood") < wood && !Need("wood", wood, ref cmd)) return;
                    Doing = "загружает печь";
                    Use(f, f.Pos, ref cmd, false, 1.2f);
                    return;
                }
                if (f.Burning && (ore >= 60 || f.Ore > 0)) { Doing = "ждёт металл из печи"; Gather(NodeKind.Tree, ref cmd); return; }
            }
            Doing = "копит металл";
            Gather(NodeKind.Metal, ref cmd);
        }

        readonly Dictionary<Piece, float> _pieceSkip = new();
        Piece _pieceTry;
        float _pieceSince, _pieceTick = -99;

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
                if ((self || Vector2.Distance(_me.Pos, aim) > 4f) && !GoStand(stand, 0.3f, ref cmd)) return true;
            }
            else if (Vector2.Distance(_me.Pos, aim) > 4f && !MoveTo(aim, 3f, ref cmd)) return true;
            if (!Hold("build_plan", ref cmd)) return true;
            cmd.Aim = aim;
            if (_me.BuildPiece != p.Kind) { cmd.SetPiece = (int)p.Kind; return true; }
            Click(ref cmd);
            return true;
        }

        /// <summary>Хозяйство фермера: инструменты → место и шкаф → база по чертежу → содержание → печь → прогрессия → вооружение → рейд → сейч → фарм.</summary>
        float _upkeepAt = -99;
        bool _upkeepLow;

        void Plan(ref InputCmd cmd)
        {
            int wood = _me.Count("wood"), stone = _me.Count("stone");
            if (RecoverLockout(ref cmd)) return;   // заперт своей дверью — раньше всего (даже раньше инструментов)

            // 1. инструменты
            if (!Has("tool_hatchet")) { if (Craft("tool_hatchet", ref cmd)) { Gather(NodeKind.Tree, ref cmd); return; } }
            if (!Has("tool_pickaxe") && Has("tool_hatchet")) { if (Craft("tool_pickaxe", ref cmd)) { Gather(NodeKind.Stone, ref cmd); return; } }
            if (!Has("tool_hatchet") || !Has("tool_pickaxe")) { Gather(wood < 200 ? NodeKind.Tree : NodeKind.Stone, ref cmd); return; }

            // 2. план базы, место и шкаф
            var claim = MyClaim();
            if (claim != null && _site == null)
            {
                // после загрузки или смерти: угол базы — от шкафа по чертежу
                _plan ??= PickPlan(out _);
                _origin = OriginFromClaim(claim);
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
                if (_w.FloorAt(spot) != null && PlaceDeploy("deploy_claim", spot, ref cmd)) return;   // база уже стоит (шкаф сломали) — в слот
                if (!Reach(spot, ref cmd, 1.1f)) return;
                if (Hold("deploy_claim", ref cmd)) Click(ref cmd);
                return;
            }

            // 3. План и Киянка
            if (!Has("build_plan")) { if (!Craft("build_plan", ref cmd)) Gather(NodeKind.Tree, ref cmd); return; }
            if (!Has("tool_hammer")) { if (!Craft("tool_hammer", ref cmd)) Gather(NodeKind.Tree, ref cmd); return; }

            // 4. содержание: запас на ~2 часа (раньше стройки — база не должна гнить)
            // запас шкафа — раз в 2 с (перебор всех построек мира на каждый тик)
            if (_w.Time - _upkeepAt > 2f) { _upkeepAt = _w.Time; _upkeepLow = _w.ClaimHoursLeft(claim) < 1.0f && _w.ClaimLoad(claim).b > 0; }
            if (_upkeepLow)
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
                if (PlaceDeploy("deploy_furnace", AtPlan(_plan.Furnace), ref cmd)) return;
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
            // 7. прогрессия, вооружение, рейд; без ствола (или с луком) — сначала ствол, если есть детали (D46)
            if ((Ranged() is not string rg || Weaponry.Power(rg) < 0.7f) && Arm(box, furnace, ref cmd)) return;
            if (Progress(box, metal, ref cmd)) return;
            if (Arm(box, furnace, ref cmd)) return;
            if (RaidPrep(box, furnace, ref cmd)) return;

            // 8. сейч — отдельная цель директора (PlayerBot.Loot.cs): излишек относится выборочно, рабочий запас остаётся при себе

            // 9. фарм: чего меньше — то и добываем
            Gather(stone < wood * 0.6f ? NodeKind.Stone : NodeKind.Tree, ref cmd);
        }
    }
}
