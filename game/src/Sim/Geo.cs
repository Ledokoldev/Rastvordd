using System;
using System.Numerics;

namespace Rastvordd.Sim
{
    public static class Geo
    {
        /// <summary>Пересечение отрезка a→b с прямоугольником. t — доля отрезка до входа.</summary>
        public static bool SegmentAabb(Vector2 a, Vector2 b, Vector2 min, Vector2 max, out float t)
        {
            t = 0;
            float tMin = 0, tMax = 1;
            Vector2 d = b - a;
            for (int axis = 0; axis < 2; axis++)
            {
                float o = axis == 0 ? a.X : a.Y;
                float dd = axis == 0 ? d.X : d.Y;
                float mn = axis == 0 ? min.X : min.Y;
                float mx = axis == 0 ? max.X : max.Y;
                if (MathF.Abs(dd) < 1e-6f)
                {
                    if (o < mn || o > mx) return false;
                }
                else
                {
                    float inv = 1f / dd;
                    float t1 = (mn - o) * inv, t2 = (mx - o) * inv;
                    if (t1 > t2) (t1, t2) = (t2, t1);
                    tMin = MathF.Max(tMin, t1);
                    tMax = MathF.Min(tMax, t2);
                    if (tMin > tMax) return false;
                }
            }
            t = tMin;
            return true;
        }

        public static bool SegmentCircle(Vector2 a, Vector2 b, Vector2 c, float r, out float t)
        {
            t = 0;
            Vector2 d = b - a, f = a - c;
            float A = Vector2.Dot(d, d);
            float C = Vector2.Dot(f, f) - r * r;
            if (C < 0) return true;           // начало отрезка внутри круга
            if (A < 1e-9f) return false;
            float B = 2 * Vector2.Dot(f, d);
            float disc = B * B - 4 * A * C;
            if (disc < 0) return false;
            float t1 = (-B - MathF.Sqrt(disc)) / (2 * A);
            if (t1 < 0 || t1 > 1) return false;
            t = t1;
            return true;
        }

        /// <summary>Выталкивает круг из прямоугольника.</summary>
        public static bool BoxOverlap(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax) =>
            aMin.X < bMax.X && aMax.X > bMin.X && aMin.Y < bMax.Y && aMax.Y > bMin.Y;

        public static bool CircleBox(Vector2 c, float r, Vector2 min, Vector2 max) =>
            Vector2.DistanceSquared(Vector2.Clamp(c, min, max), c) < r * r;

        public static Vector2 PushOut(Vector2 p, float r, Vector2 min, Vector2 max)
        {
            var closest = Vector2.Clamp(p, min, max);
            var delta = p - closest;
            float dist = delta.Length();
            if (dist >= r) return p;
            if (dist > 1e-5f) return closest + delta / dist * r;
            // центр внутри — выталкиваем по ближайшей стороне
            float left = p.X - min.X, right = max.X - p.X, down = p.Y - min.Y, up = max.Y - p.Y;
            float m = MathF.Min(MathF.Min(left, right), MathF.Min(down, up));
            if (m == left) return new Vector2(min.X - r, p.Y);
            if (m == right) return new Vector2(max.X + r, p.Y);
            if (m == down) return new Vector2(p.X, min.Y - r);
            return new Vector2(p.X, max.Y + r);
        }

        public static Vector2 MoveToward(Vector2 from, Vector2 to, float maxDelta)
        {
            var d = to - from;
            float len = d.Length();
            return len <= maxDelta || len < 1e-6f ? to : from + d / len * maxDelta;
        }

        public static float AngleDiff(float a, float b)
        {
            float d = (b - a) % (MathF.PI * 2);
            if (d > MathF.PI) d -= MathF.PI * 2;
            if (d < -MathF.PI) d += MathF.PI * 2;
            return d;
        }

        public static Vector2 Rotate(Vector2 v, float rad)
        {
            float c = MathF.Cos(rad), s = MathF.Sin(rad);
            return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }
    }
}
