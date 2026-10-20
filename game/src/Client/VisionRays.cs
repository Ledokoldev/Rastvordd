using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>
    /// Лучи обзора для затенения: из ног персонажа по кругу, до первой высокой стены или ствола дерева
    /// (те же препятствия, что закрывают обзор на сервере). Дальности уходят в шейдер 1D-текстурой.
    /// К дальности добавляется «высота» препятствия: стена и крона нарисованы вверх по экрану и сами должны быть видны.
    /// </summary>
    public sealed class VisionRays
    {
        public const int Count = 512;
        const float WallLift = 1.6f, TreeLift = 0.6f;   // объекты проверяются по основанию — запас только на толщину

        readonly Image _img = Image.CreateEmpty(Count, 1, false, Image.Format.Rf);
        public readonly ImageTexture Texture;
        readonly List<WallInfo> _walls = new();
        readonly List<(SVec c, float r)> _circles = new();

        public VisionRays(MapInfo map)
        {
            foreach (var w in map.Walls)
                if (w.Height >= 2f) _walls.Add(w);
            Texture = ImageTexture.CreateFromImage(_img);
        }

        public void Update(SVec origin, float range, List<NodeSnap> nodes)
        {
            // только то, что может попасть в радиус
            var nearWalls = new List<WallInfo>();
            foreach (var w in _walls)
            {
                var c = SVec.Clamp(origin, w.Min, w.Max);
                if (SVec.Distance(c, origin) < range) nearWalls.Add(w);
            }
            _circles.Clear();
            foreach (var n in nodes)
                if (n.Kind == NodeKind.Tree && SVec.Distance(n.Pos, origin) < range + 1) _circles.Add((n.Pos, n.Radius * 0.9f));

            for (int i = 0; i < Count; i++)
            {
                float a = -Mathf.Pi + (i + 0.5f) / Count * Mathf.Tau;
                var dir = new SVec(Mathf.Cos(a), Mathf.Sin(a));
                float best = range;
                float lift = 0;
                foreach (var w in nearWalls)
                    if (RayAabb(origin, dir, w.Min, w.Max, out float t) && t < best) { best = t; lift = WallLift; }
                foreach (var (c, r) in _circles)
                    if (RayCircle(origin, dir, c, r, out float t) && t < best) { best = t; lift = TreeLift; }
                _img.SetPixel(i, 0, new Color(best + lift, 0, 0));
            }
            Texture.Update(_img);
        }

        static bool RayAabb(SVec o, SVec d, SVec min, SVec max, out float t)
        {
            float tMin = 0, tMax = float.MaxValue;
            t = 0;
            for (int axis = 0; axis < 2; axis++)
            {
                float oo = axis == 0 ? o.X : o.Y, dd = axis == 0 ? d.X : d.Y;
                float mn = axis == 0 ? min.X : min.Y, mx = axis == 0 ? max.X : max.Y;
                if (Mathf.Abs(dd) < 1e-6f) { if (oo < mn || oo > mx) return false; continue; }
                float t1 = (mn - oo) / dd, t2 = (mx - oo) / dd;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax) return false;
            }
            t = tMin;
            return true;
        }

        static bool RayCircle(SVec o, SVec d, SVec c, float r, out float t)
        {
            t = 0;
            var f = o - c;
            float b = SVec.Dot(f, d), cc = SVec.Dot(f, f) - r * r;
            if (cc < 0) return true;                 // стоим внутри — ничего не закрывает
            float disc = b * b - cc;
            if (disc < 0) return false;
            t = -b - Mathf.Sqrt(disc);
            return t >= 0;
        }
    }
}
