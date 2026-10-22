using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Единый стиль интерфейса «стекло» (D2): тёмные полупрозрачные панели со скруглением и тонкой светлой рамкой,
    /// гладкий системный шрифт (Segoe UI), скруглённые ячейки, редкость — цветная точка, кнопки-«таблетки».
    /// ВСЕ цвета, размеры, радиусы и фабрики стилей — здесь: правка внешнего вида = правка этого файла.
    /// </summary>
    public static class UiTheme
    {
        // ================= ПАЛИТРА =================
        public static readonly Color Ink = Hex(0xf1f1f1);              // основной текст
        public static readonly Color Body = Hex(0xbdbdbd);             // описания
        public static readonly Color Dim = Hex(0x9a9a9a);              // подписи, прописные заголовки
        public static readonly Color Faint = Hex(0x6a6a6a);            // совсем второстепенное
        public static readonly Color Accent = Hex(0xf0b357);           // янтарь: прогресс, активное
        public static readonly Color Good = Hex(0x7bd389);             // хватает, изучено, горит
        public static readonly Color Bad = Hex(0xe5675f);              // не хватает, гниёт
        public static readonly Color Info = Hex(0x6cb6ff);             // дом, вода, ссылки
        public static readonly Color Dark = Hex(0x111111);             // текст на белой кнопке

        public static readonly Color HpColor = Hex(0x7bd389);
        public static readonly Color FoodColor = Hex(0xf0b357);
        public static readonly Color WaterColor = Hex(0x6cb6ff);
        public static readonly Color ArmorColor = Hex(0xa8a8a8);

        public static readonly Color PanelBg = new(20 / 255f, 20 / 255f, 22 / 255f, 0.72f);   // стекло
        public static readonly Color PanelBorder = new(1, 1, 1, 0.09f);
        public static readonly Color Shadow = new(0, 0, 0, 0.35f);
        public static readonly Color Backdrop = new(0, 0, 0, 0.5f);                          // затемнение за большим окном
        public static readonly Color SlotBg = new(1, 1, 1, 0.07f);
        public static readonly Color SlotEmpty = new(1, 1, 1, 0.03f);
        public static readonly Color SlotHover = new(1, 1, 1, 0.12f);
        public static readonly Color SlotBorder = new(1, 1, 1, 0.08f);
        public static readonly Color SlotSelBg = new(1, 1, 1, 0.18f);
        public static readonly Color SlotSelBorder = new(1, 1, 1, 0.75f);
        public static readonly Color RowBg = new(1, 1, 1, 0.05f);
        public static readonly Color ChipBg = new(1, 1, 1, 0.08f);
        public static readonly Color Line = new(1, 1, 1, 0.08f);
        public static readonly Color BarBack = new(1, 1, 1, 0.12f);

        // ================= РАЗМЕРЫ =================
        public const int PanelRadius = 18, CellRadius = 14, RowRadius = 10, ChipRadius = 16;
        public const int PanelPad = 22, PanelGap = 28;
        public const int FontTitle = 24, FontBody = 14, FontCaption = 12, FontSmall = 13;
        public const float CellSize = 88, CellGap = 10;          // ячейки рюкзака, пояса, ящиков
        public const float IconFill = 0.64f;                     // доля ячейки под иконку

        /// <summary>Длинные предметы (вид сбоку) в ячейке: наклон и увеличение, чтобы читались.</summary>
        static readonly Dictionary<string, (float deg, float scale)> IconTilt = new()
        {
            ["weapon_spear"] = (-40, 1.5f), ["weapon_bow"] = (-40, 1.5f), ["build_plan"] = (-40, 1.3f),
            ["weapon_torch"] = (-40, 1.45f), ["tool_hammer"] = (-40, 1.4f), ["tool_hatchet"] = (-40, 1.4f),
            ["tool_pickaxe"] = (-40, 1.4f), ["weapon_ak"] = (-35, 1.35f), ["weapon_pipe"] = (0, 1.2f),
            // оружие D46: иконки уже нарисованы по диагонали во весь кадр — только увеличиваем, чтобы читались
            ["weapon_akm"] = (0, 1.3f), ["weapon_an94"] = (0, 1.3f), ["weapon_ppsh"] = (0, 1.3f), ["weapon_svd"] = (0, 1.35f),
            ["weapon_svt40"] = (0, 1.35f), ["weapon_mosin"] = (0, 1.35f), ["weapon_saiga12"] = (0, 1.3f), ["weapon_mp133"] = (0, 1.35f),
            ["weapon_sawnoff"] = (0, 1.2f), ["weapon_pipeshotgun"] = (0, 1.3f), ["weapon_pps43"] = (0, 1.25f), ["weapon_homesmg"] = (0, 1.25f),
            ["weapon_crossbow"] = (0, 1.25f),
            ["comp_barrel_rifled"] = (0, 1.25f), ["comp_barrel_chrome"] = (0, 1.25f), ["comp_body_rifle"] = (0, 1.25f),
        };

        // ================= РЕДКОСТЬ И КАТЕГОРИИ =================
        public static Color RarityColor(int r) => r switch
        {
            1 => Hex(0x5cc96b),
            2 => Hex(0x4aa3ff),
            3 => Hex(0xb46cff),
            4 => Hex(0xf2b234),
            _ => Dim,
        };

        /// <summary>Цвет полоски износа: целое — зелёное, половина — янтарь, почти сломано — красное.</summary>
        public static Color DurabilityColor(float k) => k > 0.5f ? Good : k > 0.2f ? Accent : Bad;

        /// <summary>Ветка дерева изучения (цвет линий и метки): 0 пистолеты и ПП, 1 винтовки, 2 патроны, 3 обвесы, 4 прочее.</summary>
        public static int Branch(string id) => id switch
        {
            "weapon_nagant" or "weapon_homesmg" or "weapon_grach" or "weapon_pps43" or "weapon_ppsh" or "weapon_aps" => 0,
            "weapon_crossbow" or "weapon_svt40" or "weapon_akm" or "weapon_an94" or "weapon_svd" or "weapon_mosin" => 1,
            "gunpowder" => 2,
            _ when id != null && id.StartsWith("ammo_") => 2,
            _ when id != null && id.StartsWith("att_") => 3,
            _ => 4,
        };
        public static readonly string[] BranchNames = { "Пистолеты и ПП", "Винтовки", "Порох и патроны", "Обвесы", "Взрывчатка и разное" };
        public static Color BranchColor(int b) => b switch { 0 => Hex(0x6cb6ff), 1 => Hex(0xf0b357), 2 => Hex(0xbdbdbd), 3 => Hex(0xb46cff), _ => Hex(0xe5675f) };

        public static string RarityName(int r) => r switch { 1 => "Необычное", 2 => "Редкое", 3 => "Эпическое", 4 => "Легендарное", _ => "Обычное" };

        public static string CategoryName(string id) => Items.Get(id)?.Category switch
        {
            ItemCategory.Resource => "Ресурс",
            ItemCategory.Component => id != null && (id.StartsWith("key") || id.StartsWith("punchcard") || id.StartsWith("rt_") || id == "fuse") ? "Ключ"
                : id != null && id.StartsWith("att_") ? "Обвес" : "Компонент",
            ItemCategory.Tool => "Инструмент",
            ItemCategory.Weapon => "Оружие",
            ItemCategory.Ammo => "Патроны",
            ItemCategory.Food => "Еда",
            ItemCategory.Medical => "Медицина",
            ItemCategory.Deployable => "Установка",
            ItemCategory.Clothing => id != null && id.StartsWith("plate_") ? "Бронепластина" : id == "wear_watch" ? "Прибор" : "Одежда",
            _ => "",
        };

        // ================= ШРИФТЫ =================
        static Font _text, _bold, _semi;
        static readonly string[] Families = { "Segoe UI Variable Display", "Segoe UI", "Arial" };
        public static Font Text => _text ??= Sys(400);
        public static Font Semi => _semi ??= Sys(600);
        public static Font Bold => _bold ??= Sys(700);

        static Font Sys(int weight)
        {
            var f = new SystemFont
            {
                FontNames = Families, FontWeight = weight,
                Antialiasing = TextServer.FontAntialiasing.Gray, Hinting = TextServer.Hinting.Light,
                SubpixelPositioning = TextServer.SubpixelPositioning.Auto, AllowSystemFallback = true,
            };
            var rubik = GD.Load<FontFile>("res://fonts/Rubik.ttf");
            if (rubik != null) f.Fallbacks = new Godot.Collections.Array<Font> { rubik };
            return f;
        }

        // ================= СТИЛИ =================
        public static StyleBoxFlat Box(Color bg, int radius, Color? border = null, int bw = 1, float padX = 0, float padY = -1)
        {
            var f = new StyleBoxFlat { BgColor = bg, AntiAliasing = true, CornerDetail = 8 };
            f.SetCornerRadiusAll(radius);
            if (border is Color b) { f.BorderColor = b; f.SetBorderWidthAll(bw); }
            f.ContentMarginLeft = f.ContentMarginRight = padX;
            f.ContentMarginTop = f.ContentMarginBottom = padY < 0 ? padX : padY;
            return f;
        }

        /// <summary>Стеклянная панель окна: полупрозрачная, рамка 1 px, мягкая тень.</summary>
        public static StyleBoxFlat Glass(int pad = PanelPad, float alpha = -1, int radius = PanelRadius)
        {
            var f = Box(alpha < 0 ? PanelBg : new Color(PanelBg, alpha), radius, PanelBorder, 1, pad);
            f.ShadowColor = Shadow; f.ShadowSize = 18; f.ShadowOffset = new Vector2(0, 6);
            return f;
        }

        /// <summary>Мелкая плашка HUD (подсказки, лента, часы): то же стекло, меньше отступы и скругление.</summary>
        public static StyleBoxFlat Pill(float alpha = 0.62f) => Box(new Color(PanelBg, alpha), 12, PanelBorder, 1, 12, 7);

        static Theme _theme;
        public static Theme Theme
        {
            get
            {
                if (_theme != null) return _theme;
                var t = new Theme { DefaultFont = Text, DefaultFontSize = FontBody };
                t.SetStylebox("panel", "PanelContainer", Glass());
                t.SetStylebox("panel", "Panel", Glass());
                t.SetColor("font_color", "Label", Ink);

                // кнопка по умолчанию — вторичная: контур
                t.SetStylebox("normal", "Button", Box(new Color(1, 1, 1, 0.04f), RowRadius, new Color(1, 1, 1, 0.22f), 1, 16, 9));
                t.SetStylebox("hover", "Button", Box(new Color(1, 1, 1, 0.12f), RowRadius, new Color(1, 1, 1, 0.4f), 1, 16, 9));
                t.SetStylebox("pressed", "Button", Box(new Color(1, 1, 1, 0.9f), RowRadius, new Color(1, 1, 1, 0.9f), 1, 16, 9));
                t.SetStylebox("hover_pressed", "Button", Box(new Color(1, 1, 1, 0.95f), RowRadius, Colors.White, 1, 16, 9));
                t.SetStylebox("disabled", "Button", Box(new Color(1, 1, 1, 0.02f), RowRadius, new Color(1, 1, 1, 0.08f), 1, 16, 9));
                t.SetStylebox("focus", "Button", new StyleBoxEmpty());
                t.SetColor("font_color", "Button", Ink);
                t.SetColor("font_hover_color", "Button", Colors.White);
                t.SetColor("font_pressed_color", "Button", Dark);
                t.SetColor("font_hover_pressed_color", "Button", Dark);
                t.SetColor("font_disabled_color", "Button", Faint);
                t.SetFont("font", "Button", Semi);
                t.SetFontSize("font_size", "Button", FontSmall);

                // главная кнопка — белая заливка, тёмный текст
                t.SetTypeVariation("Primary", "Button");
                t.SetStylebox("normal", "Primary", Box(new Color(0.95f, 0.95f, 0.95f), RowRadius, null, 0, 18, 10));
                t.SetStylebox("hover", "Primary", Box(Colors.White, RowRadius, null, 0, 18, 10));
                t.SetStylebox("pressed", "Primary", Box(Accent, RowRadius, null, 0, 18, 10));
                t.SetStylebox("disabled", "Primary", Box(new Color(1, 1, 1, 0.08f), RowRadius, null, 0, 18, 10));
                t.SetColor("font_color", "Primary", Dark);
                t.SetColor("font_hover_color", "Primary", Dark);
                t.SetColor("font_pressed_color", "Primary", Dark);
                t.SetColor("font_disabled_color", "Primary", Faint);

                // фильтр-«таблетка»: активная — белая
                t.SetTypeVariation("Chip", "Button");
                t.SetStylebox("normal", "Chip", Box(ChipBg, ChipRadius, null, 0, 14, 6));
                t.SetStylebox("hover", "Chip", Box(new Color(1, 1, 1, 0.15f), ChipRadius, null, 0, 14, 6));
                t.SetStylebox("pressed", "Chip", Box(Colors.White, ChipRadius, null, 0, 14, 6));
                t.SetStylebox("hover_pressed", "Chip", Box(Colors.White, ChipRadius, null, 0, 14, 6));
                t.SetColor("font_color", "Chip", Hex(0xd0d0d0));
                t.SetFont("font", "Chip", Text);
                t.SetFontSize("font_size", "Chip", FontSmall);

                // поле ввода
                t.SetStylebox("normal", "LineEdit", Box(new Color(1, 1, 1, 0.06f), RowRadius, new Color(1, 1, 1, 0.15f), 1, 12, 8));
                t.SetStylebox("focus", "LineEdit", Box(new Color(1, 1, 1, 0.0f), RowRadius, new Color(1, 1, 1, 0.6f), 1, 12, 8));
                t.SetColor("font_color", "LineEdit", Ink);
                t.SetColor("font_placeholder_color", "LineEdit", Faint);
                t.SetColor("caret_color", "LineEdit", Accent);

                // галочки и ползунки (админка)
                t.SetColor("font_color", "CheckBox", Body);
                t.SetColor("font_hover_color", "CheckBox", Ink);
                t.SetColor("font_pressed_color", "CheckBox", Ink);
                t.SetColor("font_hover_pressed_color", "CheckBox", Ink);
                foreach (var s in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" }) t.SetStylebox(s, "CheckBox", Box(Colors.Transparent, 0, null, 0, 2, 4));
                t.SetStylebox("focus", "CheckBox", new StyleBoxEmpty());
                t.SetStylebox("slider", "HSlider", Box(BarBack, 3, null, 0, 0, 2));
                t.SetStylebox("grabber_area", "HSlider", Box(new Color(1, 1, 1, 0.75f), 3, null, 0, 0, 2));
                t.SetStylebox("grabber_area_highlight", "HSlider", Box(Colors.White, 3, null, 0, 0, 2));
                t.SetIcon("grabber", "HSlider", Dot(14, Colors.White));
                t.SetIcon("grabber_highlight", "HSlider", Dot(16, Colors.White));

                // тонкие полосы прокрутки
                t.SetStylebox("scroll", "VScrollBar", Box(new Color(1, 1, 1, 0.03f), 3, null, 0, 3, 0));
                t.SetStylebox("grabber", "VScrollBar", Box(new Color(1, 1, 1, 0.22f), 3, null, 0, 3, 0));
                t.SetStylebox("grabber_highlight", "VScrollBar", Box(new Color(1, 1, 1, 0.4f), 3, null, 0, 3, 0));
                t.SetStylebox("grabber_pressed", "VScrollBar", Box(new Color(1, 1, 1, 0.5f), 3, null, 0, 3, 0));

                // всплывающие подсказки — тоже стекло
                t.SetStylebox("panel", "TooltipPanel", Glass(12, 0.92f, 12));
                t.SetColor("font_color", "TooltipLabel", Ink);
                t.SetFontSize("font_size", "TooltipLabel", FontSmall);
                return _theme = t;
            }
        }

        static Texture2D Dot(int d, Color c)
        {
            var img = Image.CreateEmpty(d, d, false, Image.Format.Rgba8);
            float r = d / 2f;
            for (int y = 0; y < d; y++)
                for (int x = 0; x < d; x++)
                {
                    float dist = new Vector2(x + 0.5f - r, y + 0.5f - r).Length();
                    img.SetPixel(x, y, new Color(c, Mathf.Clamp(r - dist, 0, 1)));
                }
            return ImageTexture.CreateFromImage(img);
        }

        // ================= ГОТОВЫЕ ЭЛЕМЕНТЫ =================
        /// <summary>Стеклянная панель с отступом.</summary>
        public static PanelContainer GlassPanel(int pad = PanelPad, float alpha = -1)
        {
            var p = new PanelContainer();
            p.AddThemeStyleboxOverride("panel", Glass(pad, alpha));
            return p;
        }

        public static VBoxContainer VBox(int sep = 8) { var b = new VBoxContainer(); b.AddThemeConstantOverride("separation", sep); return b; }
        public static HBoxContainer HBox(int sep = 8) { var b = new HBoxContainer(); b.AddThemeConstantOverride("separation", sep); return b; }

        /// <summary>Заголовок окна: 24, полужирный.</summary>
        public static Label Title(string text, int size = FontTitle)
        {
            var l = new Label { Text = text };
            l.AddThemeFontOverride("font", Semi);
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", Ink);
            return l;
        }

        /// <summary>Подпись раздела: мелкие серые прописные.</summary>
        public static Label Caption(string text)
        {
            var l = new Label { Text = text, Uppercase = true };
            l.AddThemeFontSizeOverride("font_size", FontCaption);
            l.AddThemeColorOverride("font_color", Dim);
            return l;
        }

        /// <summary>Совместимость: заголовок раздела (теперь — просто подпись без линии).</summary>
        public static Control Header(string text, out Label label)
        {
            label = Caption(text);
            return label;
        }

        public static Label Small(string text, Color? color = null, int size = FontSmall)
        {
            var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", color ?? Dim);
            return l;
        }

        public static Label Strong(string text, int size = FontBody, Color? color = null)
        {
            var l = new Label { Text = text };
            l.AddThemeFontOverride("font", Semi);
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", color ?? Ink);
            return l;
        }

        public static Button Btn(string text, bool primary = false, float minW = 0, float h = 38)
        {
            var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(minW, h) };
            if (primary) b.ThemeTypeVariation = "Primary";
            return b;
        }

        public static Button Chip(string text, bool on = false)
        {
            var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, ToggleMode = true, ButtonPressed = on, ThemeTypeVariation = "Chip" };
            return b;
        }

        /// <summary>Клавиша-подсказка: серая плашка «ЛКМ — взять».</summary>
        public static Control KeyChip(string text)
        {
            var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            p.AddThemeStyleboxOverride("panel", Box(ChipBg, 8, null, 0, 10, 5));
            var l = new Label { Text = text };
            l.AddThemeFontSizeOverride("font_size", FontCaption);
            l.AddThemeColorOverride("font_color", Hex(0xdddddd));
            p.AddChild(l);
            return p;
        }

        /// <summary>Тонкая разделительная линия.</summary>
        public static Control Sep() => new ColorRect { Color = Line, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore };

        /// <summary>Иконка предмета как отдельный контрол (с наклоном длинных вещей).</summary>
        public static Control IconBox(string id, float size, float boxAlpha = 0.06f, int radius = 8)
        {
            var c = new IconView { Id = id, CustomMinimumSize = new Vector2(size, size), BoxAlpha = boxAlpha, Radius = radius };
            return c;
        }

        // ================= РИСОВАНИЕ =================
        public static void DrawRound(CanvasItem c, Rect2 r, Color bg, float radius, Color? border = null, int bw = 1)
        {
            var s = Box(bg, (int)radius, border, bw);
            c.DrawStyleBox(s, r);
        }

        /// <summary>Ячейка: пустая — еле видная, с вещью — светлее, наведённая — ещё светлее.</summary>
        public static void DrawSlot(CanvasItem c, Rect2 r, bool hover = false, bool dim = false, bool selected = false, bool empty = false)
        {
            var bg = selected ? SlotSelBg : hover ? SlotHover : dim || empty ? SlotEmpty : SlotBg;
            var border = selected ? SlotSelBorder : SlotBorder;
            float rad = Mathf.Min(CellRadius, r.Size.X * 0.18f);
            DrawRound(c, r, bg, rad, border, selected ? 2 : 1);
        }

        public static void DrawDot(CanvasItem c, Vector2 at, float radius, Color col) => c.DrawCircle(at, radius, col, true, -1, true);

        /// <summary>Полоска со скруглёнными концами: фон и заполнение 0..1.</summary>
        public static void DrawBar(CanvasItem c, Rect2 r, float frac, Color col, Color? back = null)
        {
            float rad = r.Size.Y / 2;
            DrawRound(c, r, back ?? BarBack, rad);
            frac = Mathf.Clamp(frac, 0, 1);
            if (frac > 0) DrawRound(c, new Rect2(r.Position, new Vector2(Mathf.Max(r.Size.Y, r.Size.X * frac), r.Size.Y)), col, rad);
        }

        /// <summary>Иконка предмета в прямоугольнике; длинное оружие наклонено и увеличено.</summary>
        public static void DrawIcon(CanvasItem c, string id, Rect2 r, Color? mod = null)
        {
            var tex = Art.Icon(id);
            if (tex == null) return;
            var m = mod ?? Colors.White;
            if (IconTilt.TryGetValue(id, out var t))
            {
                var center = r.GetCenter();
                c.DrawSetTransform(center, Mathf.DegToRad(t.deg), new Vector2(t.scale, t.scale));
                c.DrawTextureRect(tex, new Rect2(-r.Size / 2, r.Size), false, m);
                c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            else c.DrawTextureRect(tex, r, false, m);
        }

        public static void DrawText(CanvasItem c, Vector2 pos, string text, int size, Color col, Font font = null, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1)
            => c.DrawString(font ?? Text, pos, text, align, width, size, col);

        public static float TextWidth(string text, int size, Font font = null) => (font ?? Text).GetStringSize(text, HorizontalAlignment.Left, -1, size).X;

        /// <summary>Значки шкал: здоровье — крест, еда — кружок, вода — капля-треугольник, броня — щит.</summary>
        public static void DrawVitalIcon(CanvasItem c, int kind, Vector2 center, Color col)
        {
            switch (kind)
            {
                case 0:
                    c.DrawRect(new Rect2(center + new Vector2(-5, -1.5f), new Vector2(10, 3)), col);
                    c.DrawRect(new Rect2(center + new Vector2(-1.5f, -5), new Vector2(3, 10)), col);
                    break;
                case 1: c.DrawCircle(center, 4, col, true, -1, true); break;
                case 3: c.DrawColoredPolygon(new[] { center + new Vector2(-5, -5), center + new Vector2(5, -5), center + new Vector2(5, 0), center + new Vector2(0, 6), center + new Vector2(-5, 0) }, col); break;
                default: c.DrawColoredPolygon(new[] { center + new Vector2(0, -5), center + new Vector2(5, 4), center + new Vector2(-5, 4) }, col); break;
            }
        }

        public static Color Hex(uint rgb, float a = 1) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
    }

    /// <summary>Иконка предмета в скруглённой плашке (строки карточек, рецепты, очередь).</summary>
    public partial class IconView : Control
    {
        public string Id;
        public float BoxAlpha = 0.06f;
        public int Radius = 8;
        public Color Mod = Colors.White;
        public IconView() { MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Linear; }
        public void Set(string id) { if (id == Id) return; Id = id; QueueRedraw(); }
        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            if (BoxAlpha > 0) UiTheme.DrawRound(this, r, new Color(1, 1, 1, BoxAlpha), Radius);
            if (Id != null) UiTheme.DrawIcon(this, Id, r.Grow(-Size.X * 0.12f), Mod);
        }
    }
}
