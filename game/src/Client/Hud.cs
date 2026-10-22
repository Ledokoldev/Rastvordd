using System;
using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Интерфейс поверх мира в стиле «стекло»: снизу по центру — шкалы и пояс, над ними — подсказка действия [E],
    /// сверху слева — часы и короткие подсказки клавиш (гаснут, F1 — показать снова), сверху по центру — сообщения,
    /// справа снизу — оружие и патроны. Плюс окна: крафт, изучение, админка, код замка, экран смерти.
    /// </summary>
    public partial class Hud : CanvasLayer
    {
        const float HintsShowTime = 25;   // сколько секунд видны подсказки клавиш после старта

        Label _toastLabel, _buildLabel, _clockLabel, _deathText, _deathBag;
        PanelContainer _buildPanel, _toastPanel, _promptPanel, _clockPanel, _ammoPanel, _hintsPanel, _deathCard;
        Label _promptKey, _promptText, _ammoName, _ammoCount, _ammoReserve;
        PanelContainer _promptKeyCap;
        ColorRect _damageRect, _deathRect, _vision;
        CraftWindow _craftWin;
        TechWindow _techWin;
        Hotbar _hotbar;
        ShaderMaterial _visionMat;
        float _toastTime, _damageFlash, _hintsTime = HintsShowTime;

        public Crosshair Crosshair { get; private set; }
        public bool CraftOpen => _craftWin.Visible || _techWin.Visible;   // любое окно крафта/изучения: мышь видна, не стреляем
        /// <summary>Открыт инвентарь — у него свой пояс (с перетаскиванием), HUD-пояс прячем.</summary>
        public bool InventoryOpen;
        /// <summary>Открыта большая карта — прячем мелочь HUD.</summary>
        public bool MapOpen;
        bool _spectator;
        PanelContainer _adminPanel;
        public bool AdminOpen => _adminPanel != null && _adminPanel.Visible;

        /// <summary>Админка (F1): подстройки, которые нужно подбирать на слух/глаз. Сохраняются сразу.</summary>
        void BuildAdmin()
        {
            _adminPanel = UiTheme.GlassPanel(24, 0.86f);
            _adminPanel.Visible = false;
            _adminPanel.Theme = UiTheme.Theme;
            AddChild(_adminPanel);
            var box = UiTheme.VBox(10);
            _adminPanel.AddChild(box);
            var head = UiTheme.HBox(12);
            var title = UiTheme.Title("Админка");
            title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            head.AddChild(title);
            var close = UiTheme.KeyChip("F1 — закрыть");
            close.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            head.AddChild(close);
            box.AddChild(head);
            box.AddChild(UiTheme.Caption("Звук шагов и фона"));

            void Slider(string name, float min, float max, float step, System.Func<float> get, System.Action<float> set, string fmt)
            {
                var row = UiTheme.HBox(14);
                var label = UiTheme.Small(name, UiTheme.Body, UiTheme.FontSmall);
                label.AutowrapMode = TextServer.AutowrapMode.Off;
                label.CustomMinimumSize = new Vector2(230, 0);
                var value = UiTheme.Strong(get().ToString(fmt), UiTheme.FontSmall);
                value.CustomMinimumSize = new Vector2(56, 0);
                value.HorizontalAlignment = HorizontalAlignment.Right;
                var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = get(), CustomMinimumSize = new Vector2(280, 24), FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
                s.ValueChanged += v => { set((float)v); value.Text = ((float)v).ToString(fmt); Tuning.Save(); };
                row.AddChild(label);
                row.AddChild(s);
                row.AddChild(value);
                box.AddChild(row);
            }

            Slider("Скорость шагов: бетон", 0.4f, 1.8f, 0.01f, () => Tuning.StepsConcrete, v => Tuning.StepsConcrete = v, "0.00");
            Slider("Скорость шагов: трава", 0.4f, 1.8f, 0.01f, () => Tuning.StepsGrass, v => Tuning.StepsGrass = v, "0.00");
            Slider("Скорость шагов: снег", 0.4f, 1.8f, 0.01f, () => Tuning.StepsSnow, v => Tuning.StepsSnow = v, "0.00");
            Slider("Громкость шагов, дБ", -30f, 6f, 0.5f, () => Tuning.StepsVolumeDb, v => Tuning.StepsVolumeDb = v, "0.0");
            Slider("Громкость фона, дБ", -40f, 6f, 0.5f, () => Tuning.AmbientVolumeDb, v => Tuning.AmbientVolumeDb = v, "0.0");
            var stretch = new CheckBox { Text = "Шаги: растяжение без изменения высоты", ButtonPressed = Tuning.StepsStretch, FocusMode = Control.FocusModeEnum.None };
            stretch.Toggled += on => { Tuning.StepsStretch = on; Tuning.Save(); };
            box.AddChild(stretch);
            var hrtf = new CheckBox { Text = SteamHrtf.Ready ? "Направление звука: Steam Audio HRTF" : "Steam Audio не загрузился — наш бинаурал", ButtonPressed = Tuning.SteamHrtf && SteamHrtf.Ready, Disabled = !SteamHrtf.Ready, FocusMode = Control.FocusModeEnum.None };
            hrtf.Toggled += on => { Tuning.SteamHrtf = on; Tuning.Save(); };
            box.AddChild(hrtf);
            box.AddChild(UiTheme.Small("1.00 = как записано при обычном шаге. Присед и спринт меняют темп сами.", UiTheme.Dim, UiTheme.FontCaption));
            // проверка РТ: телепорт к южному краю любой РТ
            box.AddChild(UiTheme.Sep());
            box.AddChild(UiTheme.Caption("Телепорт к РТ (для проверки)"));
            _tpBox = new HFlowContainer { CustomMinimumSize = new Vector2(620, 0) };
            _tpBox.AddThemeConstantOverride("h_separation", 6);
            _tpBox.AddThemeConstantOverride("v_separation", 6);
            box.AddChild(_tpBox);
            box.AddChild(UiTheme.Sep());
            var wipe = UiTheme.Btn("Новый вайп — стереть сохранение и начать заново");
            wipe.AddThemeColorOverride("font_color", UiTheme.Bad);
            wipe.Pressed += () => WipeRequested?.Invoke();
            box.AddChild(wipe);
            _adminSounds = UiTheme.Small("", UiTheme.Good, UiTheme.FontCaption);
            _adminSounds.CustomMinimumSize = new Vector2(620, 0);
            box.AddChild(_adminSounds);
        }

        public void ToggleAdmin() => _adminPanel.Visible = !_adminPanel.Visible;
        HFlowContainer _tpBox;
        /// <summary>Кнопки телепорта к РТ: имя → действие.</summary>
        public void SetTeleports(System.Collections.Generic.List<(string name, System.Action go)> list)
        {
            if (_tpBox == null) return;
            foreach (var c in _tpBox.GetChildren()) c.QueueFree();
            foreach (var (name, go) in list)
            {
                var b = UiTheme.Btn(name, false, 0, 32);
                b.Pressed += () => go();
                _tpBox.AddChild(b);
            }
        }
        Label _adminSounds;
        public void SetAdminSounds(string text) { if (_adminSounds != null) _adminSounds.Text = text; }
        public event Action<int, int> CraftRequested;   // (рецепт, сколько раз)
        public event Action<string> ResearchRequested;
        public event Action WipeRequested;

        const string VisionShader = @"
shader_type canvas_item;
// Обзор «вилкой»: видно только внутри конуса и до первого препятствия (лучи из VisionRays).
// Всё остальное — серое: обесцвеченное и чуть темнее, без резкой рамки.
uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform sampler2D rays : filter_linear, repeat_enable;   // R = дальность луча в метрах, u = угол (-pi..pi -> 0..1)
uniform vec2 player_px;
uniform vec2 facing_dir = vec2(1.0, 0.0);
uniform float cos_half = 0.42;
uniform float px_per_m = 64.0;
uniform float range_m = 50.0;
uniform float strength = 1.0;
void fragment() {
    vec2 d = (FRAGCOORD.xy - player_px) / px_per_m;
    float dist = length(d);
    float c = dot(d / max(dist, 0.0001), facing_dir);
    float aa = 1.5 / max(dist * px_per_m, 1.0);
    float cone = smoothstep(cos_half - aa, cos_half + aa, c);
    float rd = texture(rays, vec2(atan(d.y, d.x) / 6.2831853 + 0.5, 0.5)).r;
    float open = 1.0 - smoothstep(rd - 0.03, rd + 0.03, dist);
    float vis = cone * open * (1.0 - smoothstep(range_m - 0.5, range_m, dist));
    // сам персонаж (нарисован вверх от ног) никогда не затеняется
    if (abs(d.x) < 0.55 && d.y > -2.0 && d.y < 0.3) vis = 1.0;
    vec3 col = texture(screen_tex, SCREEN_UV).rgb;
    float l = dot(col, vec3(0.3, 0.59, 0.11));
    vec3 hidden = mix(col, vec3(l), 0.65) * 0.8 + vec3(0.006, 0.01, 0.02);
    COLOR = vec4(mix(col, hidden, (1.0 - vis) * strength), 1.0);
}";

        public override void _Ready()
        {
            GetTree().Root.Theme = UiTheme.Theme;   // всплывающие подсказки и всё, что без своей темы
            _visionMat = new ShaderMaterial { Shader = new Shader { Code = VisionShader } };
            _vision = FullRect(new Color(0, 0, 0, 0));
            _vision.Material = _visionMat;
            _vision.Visible = false;   // обзор теперь в шейдерах мира (VisionFx), персонажи не затеняются

            _damageRect = FullRect(new Color(0.8f, 0, 0, 0));

            // часы и режим — маленькая плашка слева вверху
            _clockPanel = Pill();
            _clockLabel = UiTheme.Strong("", UiTheme.FontSmall);
            _clockPanel.AddChild(_clockLabel);

            // подсказки клавиш — одна строка «клавиш-таблеток», гаснет через полминуты, F1 — показать
            _hintsPanel = Pill(0.5f);
            var hints = UiTheme.HBox(6);
            _hintsPanel.AddChild(hints);
            foreach (var (key, what) in new[] { ("WASD", "ходьба"), ("Shift", "бег"), ("Alt", "присед"), ("ПКМ", "прицел"), ("R", "перезарядка"), ("E", "действие"), ("Tab", "инвентарь"), ("Q", "крафт"), ("G", "карта"), ("F", "есть"), ("H", "бинт"), ("N", "день/ночь"), ("F9", "набор"), ("F1", "админка") })
                hints.AddChild(KeyHint(key, what));

            // сообщения — сверху по центру
            _toastPanel = Pill(0.7f);
            _toastLabel = UiTheme.Strong("", UiTheme.FontBody + 1);
            _toastPanel.AddChild(_toastLabel);

            // подсказка действия: [E] + текст, над поясом
            _promptPanel = Pill(0.7f);
            var prow = UiTheme.HBox(10);
            _promptPanel.AddChild(prow);
            _promptKeyCap = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            _promptKeyCap.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1, 1, 1, 0.92f), 6, null, 0, 7, 1));
            _promptKey = UiTheme.Strong("E", UiTheme.FontSmall, UiTheme.Dark);
            _promptKey.HorizontalAlignment = HorizontalAlignment.Center;
            _promptKeyCap.AddChild(_promptKey);
            _promptKeyCap.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            prow.AddChild(_promptKeyCap);
            _promptText = UiTheme.Strong("", UiTheme.FontBody);
            prow.AddChild(_promptText);
            _buildPanel = Pill(0.55f);
            _buildLabel = UiTheme.Small("", UiTheme.Body, UiTheme.FontCaption);
            _buildLabel.AutowrapMode = TextServer.AutowrapMode.Off;
            _buildPanel.AddChild(_buildLabel);

            // оружие и патроны — справа внизу
            _ammoPanel = Pill(0.62f);
            var arow = UiTheme.HBox(10);
            _ammoPanel.AddChild(arow);
            _ammoName = UiTheme.Small("", UiTheme.Dim, UiTheme.FontSmall);
            _ammoName.AutowrapMode = TextServer.AutowrapMode.Off;
            _ammoName.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            arow.AddChild(_ammoName);
            _ammoCount = UiTheme.Strong("", 22);
            arow.AddChild(_ammoCount);
            _ammoReserve = UiTheme.Small("", UiTheme.Dim, UiTheme.FontSmall);
            _ammoReserve.AutowrapMode = TextServer.AutowrapMode.Off;
            _ammoReserve.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            arow.AddChild(_ammoReserve);

            _hotbar = new Hotbar(false) { Theme = UiTheme.Theme, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
            AddChild(_hotbar);
            BuildAdmin();
            BuildCraftPanel();
            BuildDeath();

            Crosshair = new Crosshair { MouseFilter = Control.MouseFilterEnum.Ignore };
            AddChild(Crosshair);
        }

        void BuildDeath()
        {
            _deathRect = FullRect(new Color(0.05f, 0, 0, 0.6f));
            _deathRect.Visible = false;
            var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiTheme.Theme };
            center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _deathRect.AddChild(center);
            _deathCard = UiTheme.GlassPanel(32, 0.8f);
            _deathCard.CustomMinimumSize = new Vector2(420, 0);
            center.AddChild(_deathCard);
            var box = UiTheme.VBox(10);
            _deathCard.AddChild(box);
            var t = UiTheme.Title("Тебя убили", 30);
            t.AddThemeColorOverride("font_color", UiTheme.Bad);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            box.AddChild(t);
            _deathText = UiTheme.Small("Весь лут остался на трупе — успей вернуться за ним.", UiTheme.Body, UiTheme.FontBody);
            _deathText.HorizontalAlignment = HorizontalAlignment.Center;
            box.AddChild(_deathText);
            box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
            var keys = UiTheme.HBox(10);
            keys.Alignment = BoxContainer.AlignmentMode.Center;
            keys.AddChild(BigKey("Enter", "проснуться на пляже"));
            box.AddChild(keys);
            _deathBag = UiTheme.Small("", UiTheme.Dim, UiTheme.FontSmall);
            _deathBag.HorizontalAlignment = HorizontalAlignment.Center;
            box.AddChild(_deathBag);
        }

        static Control BigKey(string key, string what)
        {
            var row = UiTheme.HBox(8);
            var cap = new PanelContainer();
            cap.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1, 1, 1, 0.92f), 8, null, 0, 10, 4));
            cap.AddChild(UiTheme.Strong(key, UiTheme.FontSmall, UiTheme.Dark));
            row.AddChild(cap);
            var l = UiTheme.Small(what, UiTheme.Ink, UiTheme.FontBody);
            l.AutowrapMode = TextServer.AutowrapMode.Off;
            l.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(l);
            return row;
        }

        static Control KeyHint(string key, string what)
        {
            var row = UiTheme.HBox(5);
            var cap = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            cap.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(1, 1, 1, 0.14f), 5, null, 0, 6, 1));
            cap.AddChild(UiTheme.Strong(key, UiTheme.FontCaption));
            row.AddChild(cap);
            var l = UiTheme.Small(what, UiTheme.Dim, UiTheme.FontCaption);
            l.AutowrapMode = TextServer.AutowrapMode.Off;
            l.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(l);
            return row;
        }

        PanelContainer Pill(float alpha = 0.62f)
        {
            var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiTheme.Theme };
            p.AddThemeStyleboxOverride("panel", UiTheme.Pill(alpha));
            AddChild(p);
            return p;
        }

        void Add(Control c) { c.Theme = UiTheme.Theme; AddChild(c); }

        /// <summary>Наблюдатель: прячем всё «своё» — пояс, шкалы, подсказки управления, прицел.</summary>
        public void SpectatorMode()
        {
            _spectator = true;
            foreach (var c in new CanvasItem[] { _hotbar, _hintsPanel, _clockPanel, _promptPanel, _ammoPanel, _buildPanel, Crosshair, _vision })
                if (c != null) c.Visible = false;
        }

        void BuildCraftPanel()
        {
            _craftWin = new CraftWindow();
            _craftWin.Craft += (i, n) => CraftRequested?.Invoke(i, n);
            _craftWin.OpenTree += OpenTech;
            AddChild(_craftWin);
            _techWin = new TechWindow();
            _techWin.Research += id => ResearchRequested?.Invoke(id);
            _techWin.OpenCraft += () => { _techWin.Visible = false; _craftWin.Visible = true; };
            AddChild(_techWin);
        }

        ColorRect FullRect(Color c)
        {
            var r = new ColorRect { Color = c, MouseFilter = Control.MouseFilterEnum.Ignore };
            r.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(r);
            return r;
        }

        /// <summary>Ставит элемент по долям экрана (0 — лево/верх, 1 — право/низ) с отступом.</summary>
        static void Place(Control c, Vector2 screen, float ax, float ay, Vector2 offset)
        {
            c.Size = c.GetCombinedMinimumSize();
            c.Position = new Vector2((screen.X - c.Size.X) * ax, (screen.Y - c.Size.Y) * ay) + offset;
        }

        // ---------- ввод кода замка ----------
        PanelContainer _codePanel;
        LineEdit _codeEdit;
        int _codeDoor;
        public bool CodeOpen => _codePanel != null && _codePanel.Visible;
        /// <summary>Игрок ввёл код: (id двери, код).</summary>
        public event System.Action<int, int> CodeEntered;

        public void ShowCode(int doorId)
        {
            if (_codePanel == null)
            {
                _codePanel = UiTheme.GlassPanel(26, 0.86f);
                _codePanel.Theme = UiTheme.Theme;
                _codePanel.Visible = false;
                AddChild(_codePanel);
                var box = UiTheme.VBox(12);
                box.CustomMinimumSize = new Vector2(320, 0);
                _codePanel.AddChild(box);
                var head = UiTheme.HBox(12);
                head.AddChild(UiTheme.IconBox("lock_code", 44));
                var tt = UiTheme.VBox(0);
                tt.AddChild(UiTheme.Title("Заперто", 22));
                var sub = UiTheme.Small("Кодовый замок · 4 цифры", UiTheme.Dim, UiTheme.FontSmall);
                sub.AutowrapMode = TextServer.AutowrapMode.Off;
                tt.AddChild(sub);
                head.AddChild(tt);
                box.AddChild(head);
                _codeEdit = new LineEdit { MaxLength = 4, PlaceholderText = "• • • •", Alignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(0, 58) };
                _codeEdit.AddThemeFontOverride("font", UiTheme.Semi);
                _codeEdit.AddThemeFontSizeOverride("font_size", 30);
                _codeEdit.AddThemeConstantOverride("minimum_character_width", 6);
                box.AddChild(_codeEdit);
                var keys = UiTheme.HBox(6);
                keys.AddChild(UiTheme.KeyChip("Enter — ввести"));
                keys.AddChild(UiTheme.KeyChip("Esc — отмена"));
                box.AddChild(keys);
                _codeEdit.TextSubmitted += txt =>
                {
                    if (int.TryParse(txt, out int code)) CodeEntered?.Invoke(_codeDoor, code);
                    HideCode();
                };
                _codeEdit.GuiInput += e => { if (e is InputEventKey k && k.Pressed && k.Keycode == Key.Escape) HideCode(); };
            }
            _codeDoor = doorId;
            _codeEdit.Text = "";
            _codePanel.Visible = true;
            var vs = GetViewport().GetVisibleRect().Size;
            _codePanel.Size = _codePanel.GetCombinedMinimumSize();
            _codePanel.Position = (vs - _codePanel.Size) / 2;
            _codeEdit.GrabFocus();
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }

        void HideCode()
        {
            _codePanel.Visible = false;
            _codeEdit.ReleaseFocus();
            Input.MouseMode = Input.MouseModeEnum.Hidden;
        }

        public void Toast(string text)
        {
            _toastLabel.Text = text;
            _toastTime = 3.5f;
        }

        public void FlashDamage() => _damageFlash = 0.45f;
        /// <summary>Q: открыть крафт; если открыто любое из окон — закрыть.</summary>
        public void ToggleCraft()
        {
            bool open = CraftOpen;
            _craftWin.Visible = !open;
            _techWin.Visible = false;
        }

        public void OpenTech() { _craftWin.Visible = false; _techWin.Visible = true; }

        public void SetVision(Vector2 playerPx, Vector2 facing, float pxPerM, float rangeM, float cosHalf, Texture2D rays, bool enabled)
        {
            _visionMat.SetShaderParameter("cos_half", cosHalf);
            _visionMat.SetShaderParameter("player_px", playerPx);
            _visionMat.SetShaderParameter("facing_dir", facing);
            _visionMat.SetShaderParameter("px_per_m", pxPerM);
            _visionMat.SetShaderParameter("range_m", rangeM);
            _visionMat.SetShaderParameter("rays", rays);
            _visionMat.SetShaderParameter("strength", enabled ? 1f : 0f);
        }

        public void UpdateHud(SelfInfo s, float dt, string modeText)
        {
            var vs = GetViewport().GetVisibleRect().Size;
            bool bigWindow = InventoryOpen || CraftOpen || MapOpen;

            // низ: шкалы и пояс
            _hotbar.Visible = !_spectator && !bigWindow && !s.Dead;
            _hotbar.Refresh(s);
            Place(_hotbar, vs, 0.5f, 1, new Vector2(0, -28));

            // часы и подсказки клавиш
            _clockLabel.Text = modeText;
            Place(_clockPanel, vs, 0, 0, new Vector2(16, 14));
            _hintsTime -= dt;
            float ha = AdminOpen ? 1 : Mathf.Clamp(_hintsTime / 2f, 0, 1);
            _hintsPanel.Visible = !_spectator && ha > 0.01f && !bigWindow && !s.Dead;
            _hintsPanel.Modulate = new Color(1, 1, 1, ha);
            Place(_hintsPanel, vs, 0, 0, new Vector2(16 + _clockPanel.Size.X + 8, 14));
            _clockPanel.Visible = !_spectator && !bigWindow;

            // оружие
            _ammoPanel.Visible = !_spectator && !s.Dead && !bigWindow && !string.IsNullOrEmpty(s.WeaponName);
            _ammoName.Text = s.WeaponName ?? "";
            _ammoCount.Visible = _ammoReserve.Visible = s.HoldingGun;
            _ammoCount.Text = s.HoldingGun ? $"{s.Ammo} / {s.Magazine}" : "";
            _ammoReserve.Text = s.HoldingGun ? $"запас {s.Reserve}" : "";
            _ammoCount.AddThemeColorOverride("font_color", s.HoldingGun && s.Ammo == 0 ? UiTheme.Bad : UiTheme.Ink);
            Place(_ammoPanel, vs, 1, 1, new Vector2(-20, -28));

            // подсказка действия
            string hint = s.Dead ? "" : s.InteractHint ?? "";
            string key = null;
            if (hint.StartsWith("[E] ")) { key = "E"; hint = hint.Substring(4); }
            else if (hint.EndsWith(" [E]")) { key = "E"; hint = hint.Substring(0, hint.Length - 4); }
            else if (hint.StartsWith("ЛКМ — ")) { key = "ЛКМ"; hint = hint.Substring(6); }
            _promptKeyCap.Visible = key != null;
            _promptKey.Text = key ?? "";
            _promptText.Text = hint;
            _promptPanel.Visible = !_spectator && hint != "" && !bigWindow;
            float promptY = -(28 + UiTheme.CellSize + 18);
            Place(_promptPanel, vs, 0.5f, 1, new Vector2(0, promptY));
            _buildLabel.Text = s.Dead || bigWindow ? "" : s.BuildStatus ?? "";
            _buildPanel.Visible = !_spectator && _buildLabel.Text != "";
            Place(_buildPanel, vs, 0.5f, 1, new Vector2(0, promptY - (_promptPanel.Visible ? _promptPanel.Size.Y + 6 : 0)));

            if (_craftWin.Visible) _craftWin.Refresh(s);
            if (_techWin.Visible) _techWin.Refresh(s);

            _deathRect.Visible = s.Dead;
            if (s.Dead)
                _deathBag.Text = s.HasBag ? (s.BagReadyIn > 0 ? $"Спальник будет готов через {s.BagReadyIn:0} с" : "B — проснуться в спальнике") : "Спальника нет — поставь его на базе, чтобы возрождаться дома";

            _toastTime -= dt;
            _toastPanel.Modulate = new Color(1, 1, 1, Mathf.Clamp(_toastTime, 0, 1));
            _toastPanel.Visible = _toastTime > 0 && _toastLabel.Text != "" && !bigWindow;
            Place(_toastPanel, vs, 0.5f, 0, new Vector2(0, 70));
            _damageFlash = Mathf.Max(0, _damageFlash - dt);
            _damageRect.Color = new Color(0.8f, 0, 0, _damageFlash * 0.5f);

            if (_adminPanel.Visible) Place(_adminPanel, vs, 0.5f, 0.45f, Vector2.Zero);

            Crosshair.HitFlash = Mathf.Max(0, Crosshair.HitFlash - dt);
            Crosshair.ReloadProgress = s.ReloadProgress;
            Crosshair.Visible = !CraftOpen && !_spectator;
            Crosshair.QueueRedraw();
        }
    }

    public partial class Crosshair : Control
    {
        public float RadiusPx = 10;
        public float HitFlash;
        public float ReloadProgress;

        public override void _Draw()
        {
            var c = HitFlash > 0 ? new Color(1, 0.25f, 0.2f) : new Color(1, 1, 1, 0.9f);
            float r = Mathf.Max(4, RadiusPx);
            DrawArc(Vector2.Zero, r, 0, Mathf.Tau, 48, c, 1.5f, true);
            DrawCircle(Vector2.Zero, 1.6f, c);
            if (HitFlash > 0)
            {
                float k = 6 + r * 0.3f;
                DrawLine(new Vector2(-k, -k), new Vector2(-k * 0.4f, -k * 0.4f), c, 2);
                DrawLine(new Vector2(k, -k), new Vector2(k * 0.4f, -k * 0.4f), c, 2);
                DrawLine(new Vector2(-k, k), new Vector2(-k * 0.4f, k * 0.4f), c, 2);
                DrawLine(new Vector2(k, k), new Vector2(k * 0.4f, k * 0.4f), c, 2);
            }
            if (ReloadProgress > 0)
                DrawArc(Vector2.Zero, r + 6, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * ReloadProgress, 48, new Color(1, 0.85f, 0.3f), 3, true);
        }
    }
}
