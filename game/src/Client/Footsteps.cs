using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>Поверхность под ногами — от неё набор шагов (audio/steps, наборы пользователя, CREDITS.txt).</summary>
    public enum Surface { None, Concrete, Grass, Metal, Mud, Sand, Snow, Wood }

    /// <summary>
    /// Шаги — отдельные записи по поверхностям: бетон, трава, металл, грязь, песок, снег, дерево.
    /// Каждый шаг — случайный вариант своей поверхности, не повторяющий предыдущий; лёгкий разброс высоты.
    /// </summary>
    public static class Footsteps
    {
        static readonly Dictionary<Surface, (string prefix, int count)> Sets = new()
        {
            [Surface.Concrete] = ("concrete", 6),
            [Surface.Grass] = ("grass", 19),
            [Surface.Metal] = ("metal", 5),
            [Surface.Mud] = ("mud", 5),
            [Surface.Sand] = ("sand", 14),
            [Surface.Snow] = ("snow", 12),
            [Surface.Wood] = ("wood", 9),
        };
        static readonly Dictionary<Surface, int> _last = new();
        static readonly RandomNumberGenerator _rng = new();

        /// <summary>Темп ходьбы (шагов/с) на полной скорости шага — как у человека: 1,8–2,0.</summary>
        public const float WalkCadence = 1.9f;
        /// <summary>Темп бега (полный спринт): 2,6–2,9 шагов/с.</summary>
        public const float SprintCadence = 2.8f;
        /// <summary>Темп шага вприсядку: 1,3–1,5 шагов/с.</summary>
        public const float CrouchCadence = 1.4f;

        /// <summary>
        /// Темп шагов (шагов в секунду) от скорости. Скорость в игре выше реальной (ходьба 4,5 м/с), поэтому темп берём
        /// человеческий, а шаг выходит «длинный» (длина = скорость / темп). На неполной скорости темп плавно падает
        /// (как у человека: замедляясь, он и шагает реже, и короче), между шагом и спринтом — плавно растёт.
        /// Анимация ног идёт по этому темпу, а звук шага — по касанию стопы в анимации.
        /// </summary>
        public static float Cadence(float speed, bool crouch = false)
        {
            if (speed <= 0) return 0;
            if (crouch)
            {
                float vc = Sim.World.WalkSpeed * 0.5f;
                return CrouchCadence * (0.55f + 0.45f * Mathf.Min(speed / vc, 1.3f));
            }
            float vw = Sim.World.WalkSpeed;
            if (speed <= vw) return WalkCadence * (0.55f + 0.45f * speed / vw);
            float k = Mathf.Clamp((speed - vw) / (Sim.World.SprintSpeed - vw), 0, 1.2f);
            return Mathf.Lerp(WalkCadence, SprintCadence, k);
        }

        /// <summary>Длина шага, м: скорость / темп.</summary>
        public static float Stride(float speed, bool crouch = false)
        {
            float c = Cadence(speed, crouch);
            return c > 0 ? speed / c : 0;
        }

        /// <summary>Где слышно чужие шаги (м): обзор ~50 м — бег почти на весь обзор, шаг на 3/5, присед на четверть.</summary>
        public const float HearRun = 45f, HearWalk = 30f, HearCrouch = 12f;

        public static void Play(Surface s, Vector2 posPx, float volumeDb, float hearM)
        {
            if (!Sets.TryGetValue(s, out var set)) return;
            int k = _rng.RandiRange(1, set.count);
            if (set.count > 1 && _last.GetValueOrDefault(s) == k) k = k % set.count + 1;
            _last[s] = k;
            Sfx.Play($"steps/{set.prefix}_step_{k:00}", posPx, volumeDb, hearM, 0.06f, 1f, Sfx.Room.Quiet);
        }
    }
}
