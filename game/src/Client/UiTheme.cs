using Godot;

namespace Rastvordd.Client
{
    /// <summary>
    /// Единый стиль интерфейса: ржавый клёпаный металл (рамки окон), ячейки из потёртого металла, оливковые кнопки,
    /// плашки заголовков с жёлто-чёрными полосами. Шрифты: Press Start 2P — заголовки и цифры, Rubik — текст.
    /// </summary>
    public static class UiTheme
    {
        static Theme _theme;
        static FontFile _pixel, _text;

        public static FontFile Pixel => _pixel ??= Load("res://fonts/PressStart2P.ttf");
        public static FontFile Text => _text ??= Load("res://fonts/Rubik.ttf");

        static FontFile Load(string path)
        {
            var f = GD.Load<FontFile>(path);
            if (f != null) { f.Antialiasing = TextServer.FontAntialiasing.Gray; f.Hinting = TextServer.Hinting.Light; }
            return f;
        }

        public static readonly Color Ink = new(0.9f, 0.9f, 0.88f);
        public static readonly Color Dim = new(0.58f, 0.58f, 0.56f);
        public static readonly Color Accent = new(0.95f, 0.72f, 0.32f);

        /// <summary>Цвета рамок по редкости: обычное, необычное, редкое, эпическое, легендарное.</summary>
        public static Color RarityColor(int r) => r switch
        {
            1 => new Color(0.45f, 0.8f, 0.4f),
            2 => new Color(0.35f, 0.62f, 1f),
            3 => new Color(0.72f, 0.45f, 1f),
            4 => new Color(1f, 0.75f, 0.25f),
            _ => new Color(0.55f, 0.53f, 0.5f),
        };

        public static string RarityName(int r) => r switch { 1 => "необычное", 2 => "редкое", 3 => "эпическое", 4 => "легендарное", _ => "обычное" };

        static StyleBoxFlat Flat(Color bg, int margin, int radius = 2)
        {
            var f = new StyleBoxFlat { BgColor = bg };
            f.SetCornerRadiusAll(radius);
            f.SetContentMarginAll(margin);
            return f;
        }

        public static readonly Color PanelBg = new(0.07f, 0.07f, 0.07f, 0.86f);
        public static readonly Color SlotBg = new(1f, 1f, 1f, 0.07f);
        public static readonly Color SlotHover = new(1f, 1f, 1f, 0.13f);

        /// <summary>Пустая квадратная ячейка: плоская полупрозрачная плашка без рамок.</summary>
        public static void DrawSlot(CanvasItem c, Rect2 r, bool hover = false, bool dim = false) =>
            c.DrawRect(r, dim ? new Color(1, 1, 1, 0.035f) : hover ? SlotHover : SlotBg);

        public static Theme Theme
        {
            get
            {
                if (_theme != null) return _theme;
                var t = new Theme { DefaultFont = Text, DefaultFontSize = 14 };
                t.SetStylebox("panel", "PanelContainer", Flat(PanelBg, 14));
                t.SetStylebox("normal", "Button", Flat(new Color(1, 1, 1, 0.08f), 8));
                t.SetStylebox("hover", "Button", Flat(new Color(1, 1, 1, 0.15f), 8));
                t.SetStylebox("pressed", "Button", Flat(new Color(1, 1, 1, 0.04f), 8));
                t.SetStylebox("disabled", "Button", Flat(new Color(1, 1, 1, 0.03f), 8));
                t.SetStylebox("focus", "Button", new StyleBoxEmpty());
                t.SetColor("font_color", "Button", Ink);
                t.SetColor("font_hover_color", "Button", Colors.White);
                t.SetColor("font_pressed_color", "Button", Accent);
                t.SetFontSize("font_size", "Button", 13);
                t.SetColor("font_color", "Label", Ink);
                var tip = Flat(new Color(0.05f, 0.05f, 0.05f, 0.95f), 8);
                t.SetStylebox("panel", "TooltipPanel", tip);
                t.SetColor("font_color", "TooltipLabel", Ink);
                return _theme = t;
            }
        }

        /// <summary>Заголовок блока: мелкие приглушённые прописные буквы и тонкая линия под ними.</summary>
        public static Control Header(string text, out Label label)
        {
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 4);
            label = new Label { Text = text, Uppercase = true };
            label.AddThemeFontSizeOverride("font_size", 12);
            label.AddThemeColorOverride("font_color", Dim);
            box.AddChild(label);
            box.AddChild(new ColorRect { Color = new Color(1, 1, 1, 0.12f), CustomMinimumSize = new Vector2(0, 1) });
            return box;
        }

        public static Label Small(string text, Color? color = null, int size = 13)
        {
            var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", color ?? Dim);
            return l;
        }
    }
}
