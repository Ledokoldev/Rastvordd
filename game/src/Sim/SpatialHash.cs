using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Пространственная сетка: объект лежит во всех клетках, которые задевает его рамка.
    /// Запросы по рамке и по отрезку (проход по клеткам вдоль луча) — вместо перебора всей карты.
    /// </summary>
    public sealed class SpatialHash<T> where T : class
    {
        readonly float _cell;
        readonly Dictionary<long, List<T>> _cells = new();
        readonly Dictionary<T, (int x0, int y0, int x1, int y1)> _where = new(ReferenceEqualityComparer.Instance as IEqualityComparer<T>);
        readonly HashSet<T> _seen = new(ReferenceEqualityComparer.Instance as IEqualityComparer<T>);

        public SpatialHash(float cell) => _cell = cell;

        int C(float v) => (int)MathF.Floor(v / _cell);
        static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        public void Insert(T item, Vector2 min, Vector2 max)
        {
            var r = (C(min.X), C(min.Y), C(max.X), C(max.Y));
            _where[item] = r;
            for (int x = r.Item1; x <= r.Item3; x++)
                for (int y = r.Item2; y <= r.Item4; y++)
                {
                    if (!_cells.TryGetValue(Key(x, y), out var list)) _cells[Key(x, y)] = list = new List<T>(4);
                    list.Add(item);
                }
        }

        public void Remove(T item)
        {
            if (!_where.Remove(item, out var r)) return;
            for (int x = r.x0; x <= r.x1; x++)
                for (int y = r.y0; y <= r.y1; y++)
                    if (_cells.TryGetValue(Key(x, y), out var list)) list.Remove(item);
        }

        void Collect(int x, int y, List<T> res)
        {
            if (!_cells.TryGetValue(Key(x, y), out var list)) return;
            foreach (var it in list) if (_seen.Add(it)) res.Add(it);
        }

        /// <summary>Всё, что задевает рамку (с запасом в пределах клетки). res очищается.</summary>
        public List<T> QueryBox(Vector2 min, Vector2 max, List<T> res)
        {
            res.Clear(); _seen.Clear();
            int x0 = C(min.X), y0 = C(min.Y), x1 = C(max.X), y1 = C(max.Y);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    Collect(x, y, res);
            return res;
        }

        public List<T> QueryCircle(Vector2 c, float r, List<T> res) => QueryBox(c - new Vector2(r), c + new Vector2(r), res);

        /// <summary>Всё в клетках, через которые проходит отрезок a→b (Amanatides–Woo). res очищается.</summary>
        public List<T> QuerySegment(Vector2 a, Vector2 b, List<T> res)
        {
            res.Clear(); _seen.Clear();
            int x = C(a.X), y = C(a.Y), xe = C(b.X), ye = C(b.Y);
            var d = b - a;
            int sx = Math.Sign(d.X), sy = Math.Sign(d.Y);
            float tMaxX = sx != 0 ? ((sx > 0 ? (x + 1) * _cell : x * _cell) - a.X) / d.X : float.MaxValue;
            float tMaxY = sy != 0 ? ((sy > 0 ? (y + 1) * _cell : y * _cell) - a.Y) / d.Y : float.MaxValue;
            float tdX = sx != 0 ? _cell / MathF.Abs(d.X) : float.MaxValue;
            float tdY = sy != 0 ? _cell / MathF.Abs(d.Y) : float.MaxValue;
            for (int guard = 0; guard < 100000; guard++)
            {
                Collect(x, y, res);
                if (x == xe && y == ye) break;
                if (MathF.Abs(tMaxX - tMaxY) < 1e-5f)
                {
                    // луч проходит точно через угол — берём обе соседние клетки
                    Collect(x + sx, y, res);
                    Collect(x, y + sy, res);
                }
                if (tMaxX < tMaxY) { tMaxX += tdX; x += sx; }
                else { tMaxY += tdY; y += sy; }
                if (tMaxX > 1.0001f && tMaxY > 1.0001f && (x != xe || y != ye)) { Collect(x, y, res); break; }
            }
            return res;
        }
    }
}
