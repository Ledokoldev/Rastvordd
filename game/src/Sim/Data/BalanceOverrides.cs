using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Правки баланса со страницы «Паутина прогрессии» (27.10): game/data/balance.json —
    /// { "items": { id: { "scrap": лом за изучение, "cost": [[id, n], …], "time": с } } }.
    /// Применяется один раз при старте поверх данных из кода: цена изучения узла, состав и время крафта
    /// (рецепт верстака/станции или токарного станка с тем же выходом).
    /// </summary>
    public static class BalanceOverrides
    {
        public static int Applied;

        public static void Apply(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items)) return;
            foreach (var it in items.EnumerateObject())
            {
                string id = it.Name;
                var v = it.Value;
                if (v.TryGetProperty("scrap", out var s) && Tech.Get(id) is TechNode n) { n.Scrap = s.GetInt32(); Applied++; }
                (string, int)[] cost = null;
                if (v.TryGetProperty("cost", out var c))
                {
                    var l = new List<(string, int)>();
                    foreach (var p in c.EnumerateArray()) l.Add((p[0].GetString(), p[1].GetInt32()));
                    cost = l.ToArray();
                }
                float? time = v.TryGetProperty("time", out var t) ? (float)t.GetDouble() : null;
                if (cost == null && time == null) continue;
                foreach (var r in Recipes.All)
                    if (r.Output == id) { if (cost != null) r.Cost = cost; if (time != null) r.Time = time.Value; Applied++; }
                if (Weaponry.LatheFor(id) is Weaponry.LatheRecipe lr) { if (cost != null) lr.In = cost; if (time != null) lr.Time = time.Value; Applied++; }
            }
        }
    }
}
