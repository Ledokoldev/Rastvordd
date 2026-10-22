using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Вход в здание РТ: крыльцо со ступенями у двери или пандус у ворот. Только картинка — по нему ходят,
    /// хитбокса нет. Строится по проёму в стене, поэтому всегда по ширине двери и смотрит наружу.
    /// </summary>
    public struct MapEntrance
    {
        public Vector2 A, B;     // концы проёма по линии стены
        public int Side;         // куда смотрит наружу: 0 — юг (+y, к камере), 1 — север, 2 — восток (+x), 3 — запад
        public float Depth;      // насколько выходит от стены, м
        public bool Ramp;        // пандус (ворота, роллета) — иначе крыльцо со ступенями
    }

    public sealed partial class World
    {
        public readonly List<MapEntrance> Entrances = new();

        static bool IsGateTex(string tex) => tex != null &&
            (tex.EndsWith("wallface_garage") || tex.EndsWith("wallface_shutter") || tex.EndsWith("wallface_door_metal") || tex.EndsWith("wallface_door_wood"));

        /// <summary>
        /// Входы для всех зданий (крыш) РТ, построенных начиная с крыши roofFrom: ищет проёмы и двери/ворота
        /// по краям каждой крыши и ставит перед ними крыльцо или пандус по ширине проёма.
        /// </summary>
        void AddEntrances(int roofFrom)
        {
            for (int ri = roofFrom; ri < Roofs.Count; ri++)
            {
                var r = Roofs[ri];
                for (int side = 0; side < 4; side++)
                {
                    bool horiz = side < 2;
                    float line = side switch { 0 => r.Max.Y, 1 => r.Min.Y, 2 => r.Max.X, _ => r.Min.X };
                    float lo = horiz ? r.Min.X : r.Min.Y, hi = horiz ? r.Max.X : r.Max.Y;
                    // что закрыто стенами вдоль этого края; двери и ворота — проёмы
                    var solid = new List<(float a, float b)>();
                    var gates = new List<(float a, float b, bool big)>();
                    foreach (var w in Walls)
                    {
                        if (!w.Prefab || w.Hidden) continue;
                        var mn = w.Wall.Min; var mx = w.Wall.Max;
                        float thin = horiz ? mx.Y - mn.Y : mx.X - mn.X;
                        float mid = horiz ? (mn.Y + mx.Y) / 2 : (mn.X + mx.X) / 2;
                        if (thin > 0.8f || MathF.Abs(mid - line) > 0.5f) continue;
                        float a = MathF.Max(lo, horiz ? mn.X : mn.Y), b = MathF.Min(hi, horiz ? mx.X : mx.Y);
                        if (b - a < 0.05f) continue;
                        if (IsGateTex(w.Tex)) gates.Add((a, b, w.Tex.EndsWith("garage") || w.Tex.EndsWith("shutter"))); else solid.Add((a, b));
                    }
                    if (solid.Count == 0 && gates.Count == 0) continue;   // край без стен — не здание
                    solid.Sort((x, y) => x.a.CompareTo(y.a));
                    var gaps = new List<(float a, float b, bool gate)>();   // gate — ворота под машину
                    float cur = lo;
                    foreach (var (a, b) in solid)
                    {
                        if (a - cur >= 0.8f) gaps.Add((cur, a, false));
                        cur = MathF.Max(cur, b);
                    }
                    if (hi - cur >= 0.8f) gaps.Add((cur, hi, false));
                    // ворота/двери-текстуры — тоже входы (проём занят полотном, но крыльцо нужно)
                    foreach (var g in gates) gaps.Add((g.a, g.b, g.big));
                    // проёмы внутри «дыр» из ворот не дублируем: просто сливаем пересекающиеся
                    gaps.Sort((x, y) => x.a.CompareTo(y.a));
                    for (int i = 0; i < gaps.Count; i++)
                    {
                        var (a, b, gate) = gaps[i];
                        while (i + 1 < gaps.Count && gaps[i + 1].a <= b + 0.1f) { b = MathF.Max(b, gaps[i + 1].b); gate |= gaps[i + 1].gate; i++; }
                        TryEntrance(side, line, a, b, gate, ri);
                    }
                }
            }
        }

        void TryEntrance(int side, float line, float a, float b, bool gate, int roofIdx)
        {
            float width = b - a;
            if (width < 0.8f || width > 7f) return;              // целая стена без стен — это навес/открытая площадка
            bool ramp = gate || width >= 3.2f;           // ворота/роллета или широкий проём — пандус, дверь — крыльцо
            float out0 = side is 0 or 2 ? 1 : -1;               // знак «наружу»
            bool horiz = side < 2;
            foreach (float depth in ramp ? new[] { 2.4f, 1.6f } : new[] { 1.5f, 1.0f })
            {
                // площадь входа (немного шире проёма) — снаружи стены, с отступом от её толщины
                float pad = ramp ? 0.3f : 0.45f;
                float s0 = line + out0 * 0.15f, s1 = line + out0 * (0.15f + depth);
                Vector2 min, max;
                if (horiz) { min = new(a - pad, MathF.Min(s0, s1)); max = new(b + pad, MathF.Max(s0, s1)); }
                else { min = new(MathF.Min(s0, s1), a - pad); max = new(MathF.Max(s0, s1), b + pad); }
                if (!EntranceAreaFree(min, max, roofIdx)) continue;
                Entrances.Add(new MapEntrance
                {
                    A = horiz ? new Vector2(a - pad, line + out0 * 0.15f) : new Vector2(line + out0 * 0.15f, a - pad),
                    B = horiz ? new Vector2(b + pad, line + out0 * 0.15f) : new Vector2(line + out0 * 0.15f, b + pad),
                    Side = side, Depth = depth, Ramp = ramp,
                });
                return;
            }
        }

        bool EntranceAreaFree(Vector2 min, Vector2 max, int roofIdx)
        {
            // не внутри соседнего здания (дверь между комнатами — не вход)
            var c = (min + max) * 0.5f;
            for (int i = 0; i < Roofs.Count; i++)
                if (i != roofIdx && c.X > Roofs[i].Min.X && c.X < Roofs[i].Max.X && c.Y > Roofs[i].Min.Y && c.Y < Roofs[i].Max.Y) return false;
            // не налезает на видимые стены (заборы, соседние постройки)
            var sMin = min + new Vector2(0.05f); var sMax = max - new Vector2(0.05f);
            foreach (var w in Walls)
            {
                if (w.Hidden) continue;
                if (w.Wall.Min.X < sMax.X && w.Wall.Max.X > sMin.X && w.Wall.Min.Y < sMax.Y && w.Wall.Max.Y > sMin.Y) return false;
            }
            return true;
        }
    }
}
