using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>
    /// Прозрачность заслонов, как в Project Zomboid. В виде 3/4 высокие объекты (стены, кроны, машины) рисуются вверх
    /// по экрану и закрывают то, что стоит СЕВЕРНЕЕ них, хотя в симуляции стена — тонкая линия, а дерево заслоняет
    /// только стволом. Поэтому каждый кадр собираются «фокусы» — тело своего персонажа, тела всех персонажей из снапшота
    /// (сервер присылает только тех, кого игрок честно видит, D32) и пятно вокруг курсора — и всё высокое,
    /// что стоит ПЕРЕД фокусом (основание южнее) и перекрывает его на экране, становится полупрозрачным.
    /// Скрытых сервер не присылает вовсе, так что прозрачность ничего лишнего не выдаёт.
    /// Координаты — пиксели мира (как у узлов в y-sort), «ноги» — точка касания земли.
    /// </summary>
    public static class Occlusion
    {
        /// <summary>Непрозрачность заслона, закрывающего фокус.</summary>
        public const float Alpha = 0.35f;
        /// <summary>Скорость растворения (долей непрозрачности в секунду).</summary>
        public const float Speed = 4.5f;

        readonly struct Focus
        {
            public readonly Rect2 Rect;
            public readonly float FeetY;
            public Focus(Rect2 r, float feetY) { Rect = r; FeetY = feetY; }
        }

        static readonly List<Focus> _focus = new();
        static Rect2 _view;

        /// <summary>Начало кадра: view — видимая часть мира с запасом; заслоны вне её не проверяются (их сотни на карте).</summary>
        public static void Begin(Rect2 view)
        {
            _focus.Clear();
            _view = view;
        }

        /// <summary>Тело персонажа (≈1,9 м) с запасом по ширине — под оружие и руки.</summary>
        public static void AddBody(Vector2 feet, bool dead = false)
        {
            if (dead) { _focus.Add(new Focus(new Rect2(feet.X - 16, feet.Y - 12, 32, 14), feet.Y)); return; }
            float top = feet.Y - Iso.H(1.9f) - 4;
            _focus.Add(new Focus(new Rect2(feet.X - 11, top, 22, feet.Y - top), feet.Y));
        }

        /// <summary>Пятно вокруг курсора: точка курсора на экране и земля под ней (курсор указывает на высоту груди).</summary>
        public static void AddCursor(Vector2 cursorPx, Vector2 groundPx)
        {
            const float r = 22;
            _focus.Add(new Focus(new Rect2(cursorPx.X - r, cursorPx.Y - r, r * 2, groundPx.Y - cursorPx.Y + r * 2), groundPx.Y));
        }

        /// <summary>Рядом с камерой ли заслон (остальные не проверяем).</summary>
        public static bool Near(Rect2 r) => _view.Intersects(r);

        /// <summary>Закрывает ли заслон с экранным прямоугольником rect и основанием baseY хоть один фокус позади себя.</summary>
        public static bool Covers(Rect2 rect, float baseY)
        {
            if (!_view.Intersects(rect)) return false;
            foreach (var f in _focus)
                if (f.FeetY < baseY - 1 && f.Rect.Intersects(rect)) return true;
            return false;
        }

        /// <summary>Шаг плавного растворения; true — непрозрачность изменилась (нужна перерисовка).</summary>
        public static bool Step(ref float alpha, bool fade, float dt)
        {
            float target = fade ? Alpha : 1f;
            if (Mathf.Abs(target - alpha) < 0.005f) return false;
            alpha = Mathf.MoveToward(alpha, target, dt * Speed);
            return true;
        }
    }
}
