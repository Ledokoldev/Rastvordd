using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Хитбоксы (D56) — по видимой фигуре, а не кругом под ногами. Пуля рисуется на высоте ствола (1,15 м), поэтому
    /// человек для неё — прямоугольник на земле: от ног (вниз на GunH×HS) до макушки (вверх на (рост − GunH)×HS).
    /// Ширина — по плечам. Зона попадания — шансом (Data/Headshots.cs). Числа — здесь.
    /// </summary>
    public static class Hitbox
    {
        public const float GunH = 1.15f;          // высота пули, м (= Client Iso.GunH)
        public const float HS = 0.75f;            // сжатие высоты в виде 3/4 (= Client Iso.HS)
        public const float StandH = 1.85f, CrouchH = 1.25f, DeadH = 0.35f;
        public const float HalfWidth = 0.32f;     // полуширина фигуры, м
        public const float CoverReach = 2.5f;     // присевший в пределах 2,5 м за низким укрытием — защищён

        // силуэт (D56+): голова, корпус с руками, ноги — доли роста снизу вверх и полуширины, м (по фигуре рига)
        static readonly (float z0, float z1, float half)[] Parts = { (0f, 0.46f, 0.2f), (0.46f, 0.84f, 0.32f), (0.84f, 1.04f, 0.15f) };   // голова с каской чуть выше роста

        /// <summary>Пуля a→b задевает силуэт человека: доля пути t до первой части. Мёртвый — одна плоская коробка.</summary>
        public static bool Hit(Actor a, Vector2 p0, Vector2 p1, out float t)
        {
            t = 2;
            if (a.Dead) { var (mn, mx) = Body(a); return Geo.SegmentAabb(p0, p1, mn, mx, out t); }
            bool any = false;
            foreach (var (min, max) in Silhouette(a))
                if (Geo.SegmentAabb(p0, p1, min, max, out float tt) && tt < t) { t = tt; any = true; }
            return any;
        }

        /// <summary>Части силуэта на земле (как их видит пуля на высоте ствола).</summary>
        public static System.Collections.Generic.IEnumerable<(Vector2 min, Vector2 max)> Silhouette(Actor a)
        {
            float h = a.Crouching ? CrouchH : StandH;
            foreach (var (z0, z1, half) in Parts)
            {
                float yTop = a.Pos.Y + (GunH - z1 * h) * HS, yBot = a.Pos.Y + (GunH - z0 * h) * HS;
                yield return (new Vector2(a.Pos.X - half, yTop), new Vector2(a.Pos.X + half, yBot));
            }
        }

        public static (Vector2 min, Vector2 max) Body(Actor a)
        {
            float h = a.Dead ? DeadH : a.Crouching ? CrouchH : StandH;
            float up = System.MathF.Max(0.05f, (h - GunH) * HS), down = GunH * HS;
            return (new Vector2(a.Pos.X - HalfWidth, a.Pos.Y - up), new Vector2(a.Pos.X + HalfWidth, a.Pos.Y + down));
        }
    }
}
