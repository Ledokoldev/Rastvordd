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

        public static readonly Color Ink = new(0.92f, 0.88f, 0.78f);
        public static readonly Color Dim = new(0.62f, 0.6f, 0.55f);
        public static readonly Color Accent = new(1f, 0.78f, 0.3f);

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

        public static StyleBox Box(string tex, int margin, float alpha = 1f, Color? fallback = null)
        {
            var t = Art.Get($"ui/{tex}");
            if (t == null)
            {
                var f = new StyleBoxFlat { BgColor = fallback ?? new Color(0.1f, 0.09f, 0.08f, 0.94f), BorderColor = new Color(0.5f, 0.38f, 0.25f) };
                f.SetBorderWidthAll(3);
                f.SetContentMarginAll(margin);
                return f;
            }
            var s = new StyleBoxTexture { Texture = t, ModulateColor = new Color(1, 1, 1, alpha) };
            s.TextureMarginLeft = s.TextureMarginRight = s.TextureMarginTop = s.TextureMarginBottom = margin;
            s.SetContentMarginAll(margin + 4);
            return s;
        }

        public static Theme Theme
        {
            get
            {
                if (_theme != null) return _theme;
                var t = new Theme { DefaultFont = Text, DefaultFontSize = 15 };
                t.SetStylebox("panel", "PanelContainer", Box("ui_panel", 14));
                var btn = Box("ui_button", 10, 1f, new Color(0.3f, 0.32f, 0.2f));
                var hover = Box("ui_button", 10, 1f, new Color(0.36f, 0.38f, 0.25f));
                if (hover is StyleBoxTexture ht) ht.ModulateColor = new Color(1.25f, 1.2f, 1.05f);
                var pressed = Box("ui_button", 10, 1f, new Color(0.25f, 0.26f, 0.17f));
                if (pressed is StyleBoxTexture pt) pt.ModulateColor = new Color(0.8f, 0.8f, 0.75f);
                t.SetStylebox("normal", "Button", btn);
                t.SetStylebox("hover", "Button", hover);
                t.SetStylebox("pressed", "Button", pressed);
                t.SetStylebox("focus", "Button", new StyleBoxEmpty());
                t.SetColor("font_color", "Button", Ink);
                t.SetColor("font_hover_color", "Button", Accent);
                t.SetFont("font", "Button", Pixel);
                t.SetFontSize("font_size", "Button", 10);
                t.SetColor("font_color", "Label", Ink);
                t.SetColor("font_outline_color", "Label", new Color(0, 0, 0, 0.85f));
                t.SetConstant("outline_size", "Label", 3);
                var tip = new StyleBoxFlat { BgColor = new Color(0.06f, 0.06f, 0.05f, 0.96f), BorderColor = Accent };
                tip.SetBorderWidthAll(1);
                tip.SetContentMarginAll(8);
                t.SetStylebox("panel", "TooltipPanel", tip);
                t.SetColor("font_color", "TooltipLabel", Ink);
                return _theme = t;
            }
        }

        /// <summary>Заголовок окна: пиксельный шрифт на плашке.</summary>
        public static Control Header(string text, out Label label)
        {
            var plate = new PanelContainer { CustomMinimumSize = new Vector2(0, 34) };
            plate.AddThemeStyleboxOverride("panel", Box("ui_header", 12, 1f, new Color(0.15f, 0.14f, 0.12f)));
            label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            label.AddThemeFontOverride("font", Pixel);
            label.AddThemeFontSizeOverride("font_size", 12);
            label.AddThemeColorOverride("font_color", Accent);
            plate.AddChild(label);
            return plate;
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
