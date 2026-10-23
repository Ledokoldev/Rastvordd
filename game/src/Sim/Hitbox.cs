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

        public static (Vector2 min, Vector2 max) Body(Actor a)
        {
            float h = a.Dead ? DeadH : a.Crouching ? CrouchH : StandH;
            float up = System.MathF.Max(0.05f, (h - GunH) * HS), down = GunH * HS;
            return (new Vector2(a.Pos.X - HalfWidth, a.Pos.Y - up), new Vector2(a.Pos.X + HalfWidth, a.Pos.Y + down));
        }
    }
}
