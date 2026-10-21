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

        /// <summary>Длина шага, м: спринт — шире.</summary>
        public static float Stride(float speed) => speed > Sim.World.WalkSpeed * 1.3f ? 1.05f : 0.75f;

        public static void Play(Surface s, Vector2 posPx, float volumeDb, float hearM)
        {
            if (!Sets.TryGetValue(s, out var set)) return;
            int k = _rng.RandiRange(1, set.count);
            if (set.count > 1 && _last.GetValueOrDefault(s) == k) k = k % set.count + 1;
            _last[s] = k;
            Sfx.Play($"steps/{set.prefix}_step_{k:00}", posPx, volumeDb, hearM, 0.06f);
        }
    }
}
