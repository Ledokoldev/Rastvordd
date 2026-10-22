using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    public sealed partial class World
    {
        /// <summary>Займёт ли круг (человек) эту точку: стены, глыбы и стволы, установленное — как при движении.</summary>
        public bool WalkBlocked(Vector2 p, float r, Func<MapWall, bool> passable = null)
        {
            foreach (var w in WallsNear(p, r + 0.5f))
                if (Geo.CircleBox(p, r, w.Wall.Min, w.Wall.Max) && (passable == null || !passable(w))) return true;
            foreach (var n in NodesNear(p, r + 1f))
                if (Vector2.DistanceSquared(p, n.Pos) < (r + n.Radius) * (r + n.Radius)) return true;
            foreach (var d in DeploysNear(p, r + 1f))
                if (d.Solid && Geo.CircleBox(p, r, d.Min, d.Max)) return true;
            return false;
        }

        /// <summary>Проходим ли отрезок (шагом 0,35 м); последние stopShort метров не проверяются (цель бывает внутри препятствия).</summary>
        public bool SegmentWalkable(Vector2 a, Vector2 b, float r, float stopShort = 0, Func<MapWall, bool> passable = null)
        {
            float len = Vector2.Distance(a, b) - stopShort;
            if (len <= 0) return true;
            var dir = Vector2.Normalize(b - a);
            SimPerf.Count("проходимость отрезка");
            for (float t = 0.35f; t < len; t += 0.35f)
                if (WalkBlocked(a + dir * t, r, passable)) return false;
            return true;
        }
    }

    /// <summary>
    /// A* по сетке 0,25 м вокруг бота: обход стен, деревьев, камней, установленного. Вода проходима, но дорогая.
    /// Путь «натягивается» — лишние точки выкидываются, где видна прямая.
    /// </summary>
    public static class Pathfinder
    {
        const float Step = 0.25f;
        const int MaxCells = 120000;   // буферы общие (не на каждый поиск), поэтому окно можно держать больше — хватает на выход с крупной РТ

        /// <summary>passable — какие стены можно «пройти» (свои двери: бот их откроет по пути).</summary>
        public static List<Vector2> Find(World w, Vector2 from, Vector2 to, float r, float tol, Func<MapWall, bool> passable = null, int budget = Budget, float margin = 8f)
        {
            long t0 = SimPerf.Begin();
            var span = Vector2.Abs(to - from);
            while (margin > 8f && (span.X + 2 * margin) * (span.Y + 2 * margin) / (Step * Step) > MaxCells * 0.95f) margin -= 2f;   // окно не больше буферов
            var res = FindInner(w, from, to, r, tol, passable, budget, null, null, Vector2.Min(from, to) - new Vector2(margin), Vector2.Max(from, to) + new Vector2(margin));
            SimPerf.End(res == null ? "A* (не найден)" : "A*", t0);
            return res;
        }

        /// <summary>
        /// Выход из огороженной РТ: путь до любой клетки снаружи прямоугольника m (с запасом), окно — вся РТ с полями.
        /// Обычный поиск ищет в окне вокруг отрезка «я — цель» и не видит ворот на другой стороне РТ.
        /// </summary>
        public static List<Vector2> FindExit(World w, Vector2 from, Monument m, float r, Func<MapWall, bool> passable = null)
        {
            long t0 = SimPerf.Begin();
            var lo = m.Min - new Vector2(4); var hi = m.Max + new Vector2(4);
            bool Outside(Vector2 p) => p.X < lo.X || p.Y < lo.Y || p.X > hi.X || p.Y > hi.Y;
            float ToOut(Vector2 p) => MathF.Max(0, MathF.Min(MathF.Min(p.X - lo.X, hi.X - p.X), MathF.Min(p.Y - lo.Y, hi.Y - p.Y)));
            var res = FindInner(w, from, (lo + hi) * 0.5f, r, 0, passable, 60000, Outside, ToOut, lo - new Vector2(3), hi + new Vector2(3));
            SimPerf.End(res == null ? "A* выход с РТ (не найден)" : "A* выход с РТ", t0);
            return res;
        }

        // буферы поиска переиспользуются (раньше — 4 массива по 60 тыс. клеток на каждый вызов: ≈600 КБ мусора в куче больших
        // объектов на поиск); «чистота» клетки — по номеру поиска, без очистки массивов
        static byte[] _state = new byte[MaxCells];
        static float[] _g = new float[MaxCells];
        static int[] _from = new int[MaxCells];
        static int[] _stamp = new int[MaxCells];
        static bool[] _closed = new bool[MaxCells];
        static int _search;
        static readonly PriorityQueue<int, float> _open = new();
        /// <summary>Сколько клеток раскрыть за поиск: путь вокруг дома/РТ укладывается в 3–6 тыс., дальше — почти всегда «пути нет».</summary>
        public const int Budget = 20000;

        static List<Vector2> FindInner(World w, Vector2 from, Vector2 to, float r, float tol, Func<MapWall, bool> passable, int budget,
            Func<Vector2, bool> goalTest = null, Func<Vector2, float> heur = null, Vector2? boxMin = null, Vector2? boxMax = null)
        {
            // сетка выровнена по миру (кратно 0,5 м): центр клетки попадает ровно в середину дверного проёма
            var min = boxMin ?? Vector2.Min(from, to) - new Vector2(8);
            min = new Vector2(MathF.Floor(min.X / Step) * Step, MathF.Floor(min.Y / Step) * Step);
            var max = boxMax ?? Vector2.Max(from, to) + new Vector2(8);
            int nx = (int)((max.X - min.X) / Step) + 1, ny = (int)((max.Y - min.Y) / Step) + 1;
            if (nx * ny > MaxCells) return null;

            int stampNow = ++_search;
            if (stampNow == int.MaxValue) { Array.Clear(_stamp); _search = stampNow = 1; }
            var state = _state; var g = _g; var from_ = _from; var stamp = _stamp; var closed = _closed;
            void Touch(int i) { if (stamp[i] != stampNow) { stamp[i] = stampNow; state[i] = 0; g[i] = float.MaxValue; closed[i] = false; } }
            Vector2 P(int i) => min + new Vector2(i % nx, i / nx) * Step;
            int Idx(Vector2 p) => Math.Clamp((int)MathF.Round((p.Y - min.Y) / Step), 0, ny - 1) * nx + Math.Clamp((int)MathF.Round((p.X - min.X) / Step), 0, nx - 1);
            bool Free(int i)
            {
                Touch(i);
                if (state[i] == 0) state[i] = (byte)(w.WalkBlocked(P(i), r, passable) ? 2 : 1);
                return state[i] == 1;
            }

            int start = Idx(from), goal = Idx(to);
            float H(int i) { if (heur != null) return heur(P(i)); var d = Vector2.Abs(P(i) - to); return (MathF.Max(d.X, d.Y) + 0.414f * MathF.Min(d.X, d.Y)); }
            var open = _open;
            open.Clear();
            Touch(start);
            g[start] = 0; from_[start] = -1;
            open.Enqueue(start, H(start));
            int found = -1, expanded = 0;
            while (open.Count > 0 && expanded < budget)
            {
                int c = open.Dequeue();
                if (closed[c]) continue;
                closed[c] = true;
                expanded++;
                if (goalTest != null ? goalTest(P(c)) : Vector2.Distance(P(c), to) <= tol + 0.3f) { found = c; break; }
                int cx = c % nx, cy = c / nx;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= nx || y >= ny) continue;
                        int n = y * nx + x;
                        if (!Free(n) || closed[n]) continue;
                        if (dx != 0 && dy != 0 && (!Free(cy * nx + x) || !Free(y * nx + cx))) continue;   // углы не срезаем
                        float step = (dx != 0 && dy != 0 ? 1.414f : 1f) * Step * (w.Terrain.IsLand(P(n)) ? 1f : 4f);
                        float ng = g[c] + step;
                        if (ng >= g[n]) continue;
                        g[n] = ng; from_[n] = c;
                        open.Enqueue(n, ng + H(n));
                    }
            }
            SimPerf.Count("A*: клеток раскрыто", expanded);
            if (found < 0) return null;

            var raw = new List<Vector2>();
            for (int c = found; c != -1 && c != start; c = from_[c]) raw.Add(P(c));
            raw.Reverse();
            if (raw.Count == 0) return null;
            // натянуть: от текущей точки прыгаем к самой дальней видимой
            var path = new List<Vector2>();
            var cur = from;
            int k = 0;
            while (k < raw.Count)
            {
                int far = k;
                for (int j = Math.Min(raw.Count - 1, k + 48); j > k; j--)   // не дальше 48 клеток (12 м) вперёд: иначе на длинном пути — тысячи проверок отрезков
                    if (w.SegmentWalkable(cur, raw[j], r, 0, passable)) { far = j; break; }
                path.Add(raw[far]);
                cur = raw[far];
                k = far + 1;
            }
            return path;
        }
    }
}
