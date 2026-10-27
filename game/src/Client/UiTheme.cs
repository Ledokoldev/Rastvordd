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
        public static Color Ink = Hex(0xf1f1f1);              // основной текст
        public static Color Body = Hex(0xbdbdbd);             // описания
        public static Color Dim = Hex(0x9a9a9a);              // подписи, прописные заголовки
        public static Color Faint = Hex(0x6a6a6a);            // совсем второстепенное
        public static Color Accent = Hex(0xf0b357);           // янтарь: прогресс, активное
        public static Color Good = Hex(0x7bd389);             // хватает, изучено, горит
        public static Color Bad = Hex(0xe5675f);              // не хватает, гниёт
        public static Color Info = Hex(0x6cb6ff);             // дом, вода, ссылки
        public static Color Dark = Hex(0x111111);             // текст на белой кнопке

        public static Color HpColor = Hex(0x7bd389);
        public static Color FoodColor = Hex(0xf0b357);
        public static Color WaterColor = Hex(0x6cb6ff);
        public static Color ArmorColor = Hex(0xa8a8a8);

        public static Color PanelBg = new(20 / 255f, 20 / 255f, 22 / 255f, 0.72f);   // стекло
        public static Color PanelBorder = new(1, 1, 1, 0.09f);
        public static Color Shadow = new(0, 0, 0, 0.35f);
        public static Color Backdrop = new(0, 0, 0, 0.5f);                          // затемнение за большим окном
        public static Color SlotBg = new(1, 1, 1, 0.07f);
        public static Color SlotEmpty = new(1, 1, 1, 0.03f);
        public static Color SlotHover = new(1, 1, 1, 0.12f);
        public static Color SlotBorder = new(1, 1, 1, 0.08f);
        public static Color SlotSelBg = new(1, 1, 1, 0.18f);
        public static Color SlotSelBorder = new(1, 1, 1, 0.75f);
        public static Color RowBg = new(1, 1, 1, 0.05f);
        public static Color ChipBg = new(1, 1, 1, 0.08f);
        public static Color Line = new(1, 1, 1, 0.08f);
        public static Color BarBack = new(1, 1, 1, 0.12f);


        public static Color Tint = Colors.White;   // база полупрозрачных наложений (ячейки, ховер, линии)
        public static Color Hi = Colors.White;     // выделение: заливка активной кнопки/чипа, ручка ползунка
        public static Color T(float a) => new(Tint, a);
        static char _variant = '0';
        public static char Variant => _variant;

        static UiTheme()
        {
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--uitheme=") && arg.Length > 10) { Apply(arg[10]); break; }
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--uilayout=") && arg.Length > 11 && arg[11] is >= '1' and <= '3')
                {
                    if (_variant == '0') Apply('a');   // раскладки — только на теме «Графит»
                    ApplyLayout(arg[11] - '0');
                    break;
                }
        }

        /// <summary>Раскладка интерфейса: 0 — исходная, 1 «Моноблок» (квадрат, единое окно), 2 «Створки» (мягкие листья, док слева и справа),
        /// 3 «Зеркало» (срезанные диагональные углы, пояс справа, карточка слева).</summary>
        public static int Layout { get; private set; }

        public static void ApplyLayout(int n)
        {
            Layout = n;
            if (n == 1)
            {   // моноблок: прямые углы, тонкая бирюзовая линия, панели стыкуются без зазоров, шкалы лентой над поясом
                PanelRadius = 2; CellRadius = 2; RowRadius = 2; ChipRadius = 2; ShadowSize = 0;
                PanelBorder = new Color(Accent, 0.30f); SlotBorder = new Color(Accent, 0.14f);
            }
            else if (n == 2)
            {   // створки: крупные «листовые» панели (острые углы по диагонали), мягкие ячейки, док слева и справа
                PanelRadius = 28; CellRadius = 26; RowRadius = 12; ChipRadius = 16; ShadowSize = 28;
                PanelBorder = new Color(1, 1, 1, 0.09f);
            }
            else if (n == 3)
            {   // зеркало: срезанные углы по диагонали, строгая сетка, акцентная кромка
                PanelRadius = 18; CellRadius = 12; RowRadius = 4; ChipRadius = 4; ShadowSize = 20;
                PanelBorder = new Color(Accent, 0.20f); SlotBorder = new Color(1, 1, 1, 0.09f);
            }
            _theme = null;
        }

        /// <summary>Доля экрана по X для пояса: центр / лево / право.</summary>
        public static float BeltAx => Layout == 2 ? 0f : Layout == 3 ? 1f : 0.5f;
        /// <summary>Боковой отступ пояса от края экрана.</summary>
        public static float BeltSideMargin => Layout == 2 ? 24f : Layout == 3 ? -24f : 0f;

        /// <summary>Скругления панели: у раскладок 2 и 3 — по диагонали (лист / срез).</summary>
        static void Corners(StyleBoxFlat f, int radius)
        {
            if (Layout == 2) { f.CornerRadiusTopLeft = radius; f.CornerRadiusBottomRight = radius; f.CornerRadiusTopRight = Mathf.Min(radius, 6); f.CornerRadiusBottomLeft = Mathf.Min(radius, 6); }
            else if (Layout == 3) { f.CornerRadiusTopLeft = 0; f.CornerRadiusBottomRight = 0; f.CornerRadiusTopRight = radius; f.CornerRadiusBottomLeft = radius; }
        }

        /// <summary>Форма «вырезанной» плашки для карты и т.п.: учитывает раскладку.</summary>
        public static void DrawShape(CanvasItem c, Rect2 r, Color bg, int radius, Color? border = null)
        {
            var s = Box(bg, radius, border);
            if (radius < r.Size.X / 2) Corners(s, radius);   // круг остаётся кругом
            c.DrawStyleBox(s, r);
        }

        /// <summary>Пресеты внешнего вида: a «Графит», b «Тёплый», c «Контраст»; иначе — исходный.</summary>
        public static void Apply(char v)
        {
            v = char.ToLowerInvariant(v);
            if (v == 'a')
            {   // графит: тёмное стекло, холодная тонкая линия, один мягкий акцент
                Ink = Hex(0xeceef2); Body = Hex(0xb4b9c2); Dim = Hex(0x8b919c); Faint = Hex(0x5c616b);
                Accent = Hex(0x5fc4d6); Good = Hex(0x76cf9a); Bad = Hex(0xe46f6f); Info = Hex(0x7aa8f0); Dark = Hex(0x0d1014);
                PanelBg = new(14 / 255f, 17 / 255f, 21 / 255f, 0.86f); PanelBorder = new(1, 1, 1, 0.07f); Shadow = new(0, 0, 0, 0.45f);
                SlotBg = new(1, 1, 1, 0.05f); SlotEmpty = new(1, 1, 1, 0.02f); SlotHover = new(1, 1, 1, 0.10f); SlotBorder = new(1, 1, 1, 0.06f);
                SlotSelBg = Accent * new Color(1, 1, 1, 0.16f); SlotSelBorder = Accent;
                RowBg = new(1, 1, 1, 0.04f); ChipBg = new(1, 1, 1, 0.06f); Line = new(1, 1, 1, 0.06f); BarBack = new(1, 1, 1, 0.09f);
                Tint = Colors.White; Hi = Accent;
                PanelRadius = 8; CellRadius = 6; RowRadius = 6; ChipRadius = 6; PanelBorderW = 1; ShadowSize = 24;
            }
            else if (v == 'b')
            {   // тёплый: мягкий кофейный тон, кремовый текст, приглушённая охра
                Ink = Hex(0xf1e9dc); Body = Hex(0xc4b8a6); Dim = Hex(0x9a8e7d); Faint = Hex(0x6b6254);
                Accent = Hex(0xd9a35f); Good = Hex(0x9bc47a); Bad = Hex(0xd47a5f); Info = Hex(0x8fb4c2); Dark = Hex(0x2a2218);
                PanelBg = new(38 / 255f, 31 / 255f, 26 / 255f, 0.88f); PanelBorder = new(1, 0.92f, 0.8f, 0.10f); Shadow = new(0.05f, 0.03f, 0, 0.4f);
                Tint = Hex(0xfff0d8); Hi = Hex(0xf1e4cc);
                SlotBg = T(0.06f); SlotEmpty = T(0.025f); SlotHover = T(0.12f); SlotBorder = T(0.08f);
                SlotSelBg = new(Accent, 0.22f); SlotSelBorder = Accent;
                RowBg = T(0.05f); ChipBg = T(0.08f); Line = T(0.09f); BarBack = T(0.12f);
                PanelRadius = 14; CellRadius = 12; RowRadius = 9; ChipRadius = 14; PanelBorderW = 1; ShadowSize = 20;
            }
            else if (v == 'c')
            {   // контраст: почти чёрный, острые углы, белый текст, один сигнальный акцент
                Ink = Hex(0xffffff); Body = Hex(0xd2d2d2); Dim = Hex(0xa0a0a0); Faint = Hex(0x707070);
                Accent = Hex(0xffd23f); Good = Hex(0x5fe08a); Bad = Hex(0xff5d52); Info = Hex(0x6cc8ff); Dark = Hex(0x000000);
                PanelBg = new(0, 0, 0, 0.92f); PanelBorder = new(1, 1, 1, 0.28f); Shadow = new(0, 0, 0, 0.0f);
                SlotBg = new(1, 1, 1, 0.07f); SlotEmpty = new(1, 1, 1, 0.025f); SlotHover = new(1, 1, 1, 0.16f); SlotBorder = new(1, 1, 1, 0.20f);
                SlotSelBg = new(Accent, 0.18f); SlotSelBorder = Accent;
                RowBg = new(1, 1, 1, 0.06f); ChipBg = new(1, 1, 1, 0.10f); Line = new(1, 1, 1, 0.22f); BarBack = new(1, 1, 1, 0.16f);
                Tint = Colors.White; Hi = Accent;
                PanelRadius = 0; CellRadius = 0; RowRadius = 0; ChipRadius = 0; PanelBorderW = 1; ShadowSize = 0;
            }
            else return;
            _variant = v; _theme = null;
        }

        // ================= РАЗМЕРЫ =================
        public static int PanelRadius = 18, CellRadius = 14, RowRadius = 10, ChipRadius = 16;
        public static int PanelBorderW = 1, ShadowSize = 18;
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
        public static StyleBoxFlat Glass(int pad = PanelPad, float alpha = -1, int radius = -1)
        {
            var f = Box(alpha < 0 ? PanelBg : new Color(PanelBg, alpha), radius < 0 ? PanelRadius : radius, PanelBorder, PanelBorderW, pad);
            f.ShadowColor = Shadow; f.ShadowSize = ShadowSize; f.ShadowOffset = new Vector2(0, 6);
            if (radius < 0) Corners(f, PanelRadius);
            return f;
        }

        /// <summary>Мелкая плашка HUD (подсказки, лента, часы): то же стекло, меньше отступы и скругление.</summary>
        public static StyleBoxFlat Pill(float alpha = 0.62f) => Box(new Color(PanelBg, alpha), Mathf.Min(12, PanelRadius), PanelBorder, 1, 12, 7);

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
                t.SetStylebox("normal", "Button", Box(T(0.04f), RowRadius, T(0.22f), 1, 16, 9));
                t.SetStylebox("hover", "Button", Box(T(0.12f), RowRadius, T(0.4f), 1, 16, 9));
                t.SetStylebox("pressed", "Button", Box(T(0.9f), RowRadius, T(0.9f), 1, 16, 9));
                t.SetStylebox("hover_pressed", "Button", Box(T(0.95f), RowRadius, Hi, 1, 16, 9));
                t.SetStylebox("disabled", "Button", Box(T(0.02f), RowRadius, T(0.08f), 1, 16, 9));
                t.SetStylebox("focus", "Button", new StyleBoxEmpty());
                t.SetColor("font_color", "Button", Ink);
                t.SetColor("font_hover_color", "Button", Hi);
                t.SetColor("font_pressed_color", "Button", Dark);
                t.SetColor("font_hover_pressed_color", "Button", Dark);
                t.SetColor("font_disabled_color", "Button", Faint);
                t.SetFont("font", "Button", Semi);
                t.SetFontSize("font_size", "Button", FontSmall);

                // главная кнопка — белая заливка, тёмный текст
                t.SetTypeVariation("Primary", "Button");
                t.SetStylebox("normal", "Primary", Box(Hi * 0.95f, RowRadius, null, 0, 18, 10));
                t.SetStylebox("hover", "Primary", Box(Hi, RowRadius, null, 0, 18, 10));
                t.SetStylebox("pressed", "Primary", Box(Accent, RowRadius, null, 0, 18, 10));
                t.SetStylebox("disabled", "Primary", Box(T(0.08f), RowRadius, null, 0, 18, 10));
                t.SetColor("font_color", "Primary", Dark);
                t.SetColor("font_hover_color", "Primary", Dark);
                t.SetColor("font_pressed_color", "Primary", Dark);
                t.SetColor("font_disabled_color", "Primary", Faint);

                // фильтр-«таблетка»: активная — белая
                t.SetTypeVariation("Chip", "Button");
                t.SetStylebox("normal", "Chip", Box(ChipBg, ChipRadius, null, 0, 14, 6));
                t.SetStylebox("hover", "Chip", Box(T(0.15f), ChipRadius, null, 0, 14, 6));
                t.SetStylebox("pressed", "Chip", Box(Hi, ChipRadius, null, 0, 14, 6));
                t.SetStylebox("hover_pressed", "Chip", Box(Hi, ChipRadius, null, 0, 14, 6));
                t.SetColor("font_color", "Chip", Hex(0xd0d0d0));
                t.SetFont("font", "Chip", Text);
                t.SetFontSize("font_size", "Chip", FontSmall);

                // поле ввода
                t.SetStylebox("normal", "LineEdit", Box(T(0.06f), RowRadius, T(0.15f), 1, 12, 8));
                t.SetStylebox("focus", "LineEdit", Box(T(0.0f), RowRadius, T(0.6f), 1, 12, 8));
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
                t.SetStylebox("grabber_area", "HSlider", Box(T(0.75f), 3, null, 0, 0, 2));
                t.SetStylebox("grabber_area_highlight", "HSlider", Box(Hi, 3, null, 0, 0, 2));
                t.SetIcon("grabber", "HSlider", Dot(14, Hi));
                t.SetIcon("grabber_highlight", "HSlider", Dot(16, Hi));

                // тонкие полосы прокрутки
                t.SetStylebox("scroll", "VScrollBar", Box(T(0.03f), 3, null, 0, 3, 0));
                t.SetStylebox("grabber", "VScrollBar", Box(T(0.22f), 3, null, 0, 3, 0));
                t.SetStylebox("grabber_highlight", "VScrollBar", Box(T(0.4f), 3, null, 0, 3, 0));
                t.SetStylebox("grabber_pressed", "VScrollBar", Box(T(0.5f), 3, null, 0, 3, 0));

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
            if (size == FontTitle && Layout == 1) { l.Uppercase = true; size = 15; l.AddThemeColorOverride("font_color", Accent); }   // узкая шапка раздела
            else if (size == FontTitle && Layout == 3) { size = 20; l.AddThemeColorOverride("font_color", Ink); }
            else l.AddThemeColorOverride("font_color", Ink);
            l.AddThemeFontOverride("font", Semi);
            l.AddThemeFontSizeOverride("font_size", size);
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

        /// <summary>Клик попал в какую-нибудь видимую панель окна (или пояс)? Иначе — «мимо», окно закрывается.</summary>
        public static bool HitsPanel(Node root, Vector2 globalPos)
        {
            foreach (var n in root.FindChildren("*", "", true, false))
                if (n is Control c && (c is PanelContainer || c is Hotbar) && c.IsVisibleInTree() && c.GetGlobalRect().HasPoint(globalPos)) return true;
            return false;
        }

        /// <summary>Окно на весь экран: нажатие мыши мимо его панелей.</summary>
        public static bool ClickOutside(Control root, InputEvent e) =>
            e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex is MouseButton.Left or MouseButton.Right && !HitsPanel(root, mb.GlobalPosition);

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
            float rad = Mathf.Min(CellRadius, r.Size.X * (Layout == 2 ? 0.30f : 0.18f));
            var st = Box(bg, (int)rad, border, selected ? 2 : 1);
            if (Layout == 3) { st.CornerRadiusTopLeft = 0; st.CornerRadiusBottomRight = 0; st.CornerRadiusTopRight = (int)rad; st.CornerRadiusBottomLeft = (int)rad; }
            if (hover && !selected && Layout > 0)
            {   // свечение под курсором
                var glow = Box(new Color(Accent, 0.07f), (int)rad + 3, new Color(Accent, 0.55f), 1);
                if (Layout == 3) { glow.CornerRadiusTopLeft = 0; glow.CornerRadiusBottomRight = 0; }
                c.DrawStyleBox(glow, r.Grow(2));
            }
            c.DrawStyleBox(st, r);
        }

        public static void DrawDot(CanvasItem c, Vector2 at, float radius, Color col) => c.DrawCircle(at, radius, col, true, -1, true);

        /// <summary>Полоска со скруглёнными концами: фон и заполнение 0..1.</summary>
        public static void DrawBar(CanvasItem c, Rect2 r, float frac, Color col, Color? back = null)
        {
            float rad = Layout is 1 or 3 ? 1 : r.Size.Y / 2;
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
            // вписать с сохранением пропорций (не сплющивать картинку в неквадратной рамке)
            var ts = tex.GetSize();
            if (ts.X > 0 && ts.Y > 0 && r.Size.X > 0 && r.Size.Y > 0)
            {
                float k = Mathf.Min(r.Size.X / ts.X, r.Size.Y / ts.Y);
                var fit = ts * k;
                r = new Rect2(r.GetCenter() - fit / 2, fit);
            }
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
            if (Id != null) UiTheme.DrawIcon(this, Id, r.Grow(-Mathf.Min(Size.X, Size.Y) * 0.12f), Mod);
        }
    }
}
