using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Установленное по сетке клеток (D52): бот ставит ящики, спальник, печь, верстаки и станок в слоты своих полов
    /// по тем же правилам, что игрок (World.FitDeploy/BestFit): ящики — в углы, верстаки и печь — длинной стороной к стене,
    /// место у верстаков (BenchStand) и четверть у дверного проёма не занимает. Нет места в доме (треугольная база, станок
    /// на целую клетку) — кладёт рядом с домом отдельный фундамент-площадку и ставит туда.
    /// </summary>
    public sealed partial class PlayerBot
    {
        Piece _pad;   // площадка под станок, которая сейчас строится

        /// <summary>Поставить предмет как можно ближе к точке чертежа. true — занят этим в этот тик; false — некуда.</summary>
        bool PlaceDeploy(string item, Vector2 want, ref InputCmd cmd)
        {
            var kind = Items.Get(item)?.Deploy ?? DeployKind.None;
            if (kind == DeployKind.None) return false;
            Vector2? keep = _plan != null && kind != DeployKind.Claim ? AtPlan(_plan.BenchStand) : null;
            if (_w.BestFit(_me, kind, want, 8f, keep) is not World.DeployFit fit)
            {
                if (kind != DeployKind.Claim && BuildPad(want, ref cmd)) return true;
                LStat($"сетка: некуда — {Items.Name(item)}");
                Say("сетка-некуда", $"{Items.Name(item)} поставить некуда — ни слота, ни площадки", 120);
                return false;
            }
            _pad = null;
            var size = Footprints.SizeOf(kind);
            float r = size == Footprint.Quarter ? 1.1f : size == Footprint.Half ? 1.45f : 1.65f;   // стоять вне будущего хитбокса
            if (!Reach(fit.Pos, ref cmd, r)) return true;
            _me.DeployRot = fit.Rot;
            cmd.Aim = fit.Pos;
            if (Hold(item, ref cmd)) Click(ref cmd);
            return true;
        }

        /// <summary>Фундамент-площадка у дома под то, чему нет места внутри: клетка рядом со своим полом, не на месте будущих стадий и не у входа.</summary>
        bool BuildPad(Vector2 want, ref InputCmd cmd)
        {
            if (_plan == null) return false;
            if (_pad == null || Existing(_pad) != null || (_pieceSkip.TryGetValue(_pad, out float till) && till > _w.Time))
            {
                _pad = null;
                var cell = PadCell(want);
                if (cell == null) return false;
                _pad = F(cell.Value.i - _origin.i, cell.Value.j - _origin.j);
                Thought("в доме нет места — кладу фундамент-площадку рядом");
                LStat("сетка: площадка");
            }
            if (_pieceTry != _pad || _w.Time - _pieceTick > 5) { _pieceTry = _pad; _pieceSince = _w.Time; }
            else if (_w.Time - _pieceSince > 90) { _pieceSkip[_pad] = _w.Time + 600; _pieceTry = null; _pad = null; return false; }
            _pieceTick = _w.Time;
            return BuildPiece(_pad, MyBox(), ref cmd);
        }

        (int i, int j)? PadCell(Vector2 want)
        {
            var claim = MyClaim();
            if (claim == null) return null;
            // полы чертежа всех стадий и проёмы — туда площадку не класть
            var planPolys = new List<Vector2[]>();
            var doorEdges = new HashSet<long>();
            foreach (var st in _plan.Stages)
                foreach (var p in st.Pieces)
                {
                    if (p.Kind == StructKind.Foundation) planPolys.Add(Building.SquarePoly(_origin.i + p.I, _origin.j + p.J));
                    else if (p.Kind == StructKind.TriFoundation) planPolys.Add(TriOf(p));
                    else if (p.Kind is StructKind.Doorway or StructKind.Door) { var (a, b) = EdgeOf(p); doorEdges.Add(Building.EdgeKey(a, b)); }
                }
            (int, int)? best = null;
            float bd = 7f;
            var seen = new HashSet<(int, int)>();
            foreach (var s in _w.Structures)
            {
                if (s.Kind != StructKind.Foundation || s.OwnerId != _me.Id) continue;
                // рядом с домом: до двух клеток от своего пола (у треугольной базы соседние клетки заняты сотами)
                for (int di = -2; di <= 2; di++)
                for (int dj = -2; dj <= 2; dj++)
                {
                    var c = (s.I + di, s.J + dj);
                    if (!seen.Add(c) || _w.FoundationAt(c.Item1, c.Item2) != null) continue;
                    var sq = Building.SquarePoly(c.Item1, c.Item2);
                    if (planPolys.Exists(pp => Building.PolysOverlap(sq, pp))) continue;
                    bool door = false;
                    for (int k = 0; k < 4 && !door; k++) door = doorEdges.Contains(Building.EdgeKey(sq[k], sq[(k + 1) % 4]));
                    if (door || _w.ClaimAt(c) != claim || !_w.FoundationGeoOk(c.Item1, c.Item2, out _)) continue;
                    if (SkippedPad(c)) continue;
                    float d = Vector2.Distance(Building.CellCenter(c.Item1, c.Item2), want);
                    if (d < bd) { bd = d; best = c; }
                }
            }
            return best;
        }

        /// <summary>Площадка в этой клетке уже не вставала (10 мин пауза) — ключи _pieceSkip — объекты Piece, сверяем по клетке.</summary>
        bool SkippedPad((int i, int j) c)
        {
            foreach (var (p, till) in _pieceSkip)
                if (till > _w.Time && p.Kind == StructKind.Foundation && _origin.i + p.I == c.i && _origin.j + p.J == c.j && !_plan.Stages.Exists(st => st.Pieces.Contains(p))) return true;
            return false;
        }

        /// <summary>Сколько своих предметов этого вида стоит у базы.</summary>
        int OwnDeployCount(DeployKind kind)
        {
            var claim = MyClaim();
            int n = 0;
            foreach (var d in _w.Deployables)
                if (d.OwnerId == _me.Id && d.Kind == kind && (claim == null || Vector2.Distance(d.Pos, claim.Pos) < 20)) n++;
            return n;
        }

        /// <summary>Верстак наибольшего уровня.</summary>
        Deployable MyTopBench()
        {
            Deployable best = null;
            foreach (var d in _w.Deployables)
                if (d.OwnerId == _me.Id && d.Kind == DeployKind.Workbench && (best == null || d.Level > best.Level)) best = d;
            return best;
        }

        int _standBenchId = -1;
        Vector2 _standBench;
        float _standBenchAt = -99;

        /// <summary>Где работать с верстаками: точка чертежа, а если верстак встал дальше (на площадке) — рядом с ним.</summary>
        Vector2 BenchStandFor()
        {
            var stand = AtPlan(_plan.BenchStand);
            var b = MyTopBench();
            if (b == null || Vector2.Distance(stand, b.Pos) < World.BenchRange - 0.8f) return stand;
            if (b.Id != _standBenchId || _w.Time - _standBenchAt > 10)
            {
                _standBenchId = b.Id; _standBenchAt = _w.Time;
                _standBench = StandNear(b.Pos, b.Radius + 0.6f);
            }
            return _standBench;
        }

        /// <summary>После загрузки: угол базы — от шкафа по чертежу; шкаф мог встать в слот со сдвигом — берём угол, где стоит больше элементов чертежа.</summary>
        (int i, int j) OriginFromClaim(Deployable claim)
        {
            var guess = Building.CellOf(claim.Pos - _plan.Claim + new Vector2(0.2f));
            (int, int) best = guess; int bn = -1;
            for (int di = -1; di <= 1; di++)
                for (int dj = -1; dj <= 1; dj++)
                {
                    _origin = (guess.i + di, guess.j + dj);
                    int n = 0;
                    foreach (var st in _plan.Stages) foreach (var p in st.Pieces) if (Building.IsFloor(p.Kind) && Existing(p) != null) n++;
                    if (n > bn || (n == bn && di == 0 && dj == 0)) { bn = n; best = _origin; }
                }
            return best;
        }
    }
}
