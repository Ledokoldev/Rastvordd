using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Накопление (D64, мозг v4 шаг 3; пользователь: «не копят ресы»): у каждого этапа — цель запаса (с собой + в ящике),
    /// в свободное время бот добывает то, чего не хватает сильнее всего (дерево, камень, металл, сера), а не «что меньше из двух»;
    /// сверх рабочего набора — в ящик по количеству (дерево и камень дёшевы в ломе, раньше их таскали с собой и теряли при смерти).
    /// </summary>
    public sealed partial class PlayerBot
    {
        /// <summary>Цели запаса по верстаку: дерево, камень, металл (с рудой), сера (с рудой).</summary>
        (int wood, int stone, int metal, int sulfur) StockGoal()
        {
            int bench = MyBenchLevel();
            return bench switch
            {
                >= 3 => (4000, 4000, 1500, 800),
                2 => (3000, 3000, 800, 400),
                1 => (2000, 2000, 300, 100),
                _ => (1500, 1200, 100, 0),
            };
        }

        int StockOf(string id) => id switch
        {
            "metal_fragments" => Stock("metal_fragments") + Stock("metal_ore") / 2,
            "sulfur" => Stock("sulfur") + Stock("sulfur_ore") / 2,
            _ => Stock(id),
        };

        float _stockSaidAt = -999;

        /// <summary>Фарм по дефициту: добыть то, чего меньше всего относительно цели. false — всё накоплено.</summary>
        bool FarmByDeficit(ref InputCmd cmd)
        {
            var (gw, gs, gm, gsu) = StockGoal();
            var want = new List<(string id, NodeKind node, int have, int goal)>
            {
                ("wood", NodeKind.Tree, StockOf("wood"), gw),
                ("stone", NodeKind.Stone, StockOf("stone"), gs),
            };
            if (gm > 0 && Has("tool_pickaxe")) want.Add(("metal_fragments", NodeKind.Metal, StockOf("metal_fragments"), gm));
            if (gsu > 0 && Has("tool_pickaxe")) want.Add(("sulfur", NodeKind.Sulfur, StockOf("sulfur"), gsu));
            (string id, NodeKind node, int have, int goal) best = default;
            float bestRatio = 1f;
            foreach (var w in want)
            {
                float r = w.goal <= 0 ? 1 : (float)w.have / w.goal;
                if (r < bestRatio) { bestRatio = r; best = w; }
            }
            if (best.id == null)
            {
                if (_w.Time - _stockSaidAt > 300) { _stockSaidAt = _w.Time; Thought($"запасы собраны (дерево {StockOf("wood")}, камень {StockOf("stone")}, металл {StockOf("metal_fragments")}, сера {StockOf("sulfur")}) — свободен"); LStat("запасы: собраны"); }
                return false;
            }
            Gather(best.node, ref cmd);
            Doing = $"копит {Items.Name(best.id).ToLower()} ({best.have}/{best.goal})";
            return true;
        }

        /// <summary>Сырьё сверх рабочего набора (шт.): дерево, камень, металл, сера, уголь — в ящик по количеству.</summary>
        int RawSurplusCount()
        {
            int n = 0;
            foreach (var id in new[] { "wood", "stone", "metal_fragments", "sulfur", "charcoal" })   // руда — в печь, не в ящик
            {
                int keep = KeepOf(id);
                if (keep >= int.MaxValue / 2) continue;
                n += Math.Max(0, _me.Count(id) - keep);
            }
            return n;
        }
    }
}
