using System;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Станции D54 ботами: рецепт со станцией (стволы тира 3 — оружейный стол) — сделать станцию у верстака 3, поставить
    /// (свободная клетка мастерской или площадка), крафтить, стоя у неё. Остальные станции боты не трогают (необязательные).
    /// Легированная сталь — как деталь: не хватает — «заказ» (_partsWant), поход на РТ и переработчик.
    /// </summary>
    public sealed partial class PlayerBot
    {
        float _stationBlock = -999, _stationTry = -1;

        Deployable MyStation(DeployKind k)
        {
            var c = MyClaim();
            if (c == null) return null;
            foreach (var d in _w.Deployables) if (d.Kind == k && d.OwnerId == _me.Id && Vector2.Distance(d.Pos, c.Pos) < 40) return d;
            return null;
        }

        /// <summary>Своя станция для рецепта: есть — вернуть; нет — сделать/поставить (true в busy — занят этим в этот тик).</summary>
        Deployable EnsureStation(DeployKind k, Deployable box, ref InputCmd cmd, out bool busy)
        {
            busy = false;
            var st = MyStation(k);
            if (st != null) { _stationTry = -1; return st; }
            if (_w.Time < _stationBlock) return null;
            var def = StationDefs.Get(k);
            if (def == null) return null;
            if (!Has(def.Item))
            {
                if (Tech.Get(def.Item) != null && !_me.Known.Contains(def.Item)) return null;   // не изучено — изучит в Progress
                Doing = $"делает: {def.Name}";
                busy = CraftAtBench(def.Item, box, ref cmd);
                return null;
            }
            if (_stationTry < 0) _stationTry = _w.Time;
            if (_w.Time - _stationTry > 150) { _stationBlock = _w.Time + 600; _stationTry = -1; Thought($"{def.Name}: поставить негде — позже"); return null; }
            Doing = $"ставит: {def.Name}";
            busy = PlaceDeploy(def.Item, BenchSpot(Math.Max(1, def.Bench)), ref cmd);
            if (!busy) { _stationBlock = _w.Time + 600; _stationTry = -1; }
            return null;
        }

        /// <summary>Лег. сталь и детали — «заказываются» (поход на РТ, переработчик), а не добываются.</summary>
        static bool IsOrdered(string id) => id.StartsWith("comp_") || id.StartsWith("bp_frag") || id == Prog.Alloy;
    }
}
