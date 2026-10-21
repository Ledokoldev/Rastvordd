using System;
using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>Интерфейс поверх мира: HP, еда, вода, пояс, крафт, подсказки, инвентарь, смерть, затемнение вне обзора.</summary>
    public partial class Hud : CanvasLayer
    {
        const int BarW = 260;

        Label _buildLabel;
        Label _hpLabel, _foodLabel, _waterLabel, _ammoLabel, _hintLabel, _toastLabel, _promptLabel, _invLabel, _deathLabel, _modeLabel;
        ColorRect _hpFill, _foodFill, _waterFill, _damageRect, _deathRect, _vision;
        PanelContainer _invPanel;
        CraftWindow _craftWin;
        TechWindow _techWin;
        HBoxContainer _belt;
        readonly List<(PanelContainer panel, Label label, StyleBoxFlat style, TextureRect icon)> _beltSlots = new();
        ShaderMaterial _visionMat;
        float _toastTime, _damageFlash;

        public Crosshair Crosshair { get; private set; }
        public bool CraftOpen => _craftWin.Visible || _techWin.Visible;   // любое окно крафта/изучения: мышь видна, не стреляем
        PanelContainer _adminPanel;
        public bool AdminOpen => _adminPanel != null && _adminPanel.Visible;

        /// <summary>Админка (F1): подстройки, которые нужно подбирать на слух/глаз. Сохраняются сразу.</summary>
        void BuildAdmin()
        {
            _adminPanel = new PanelContainer { Visible = false };
            AddChild(_adminPanel);
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 6);
            _adminPanel.AddChild(box);
            var title = new Label { Text = "АДМИНКА  (F1 — закрыть)" };
            title.AddThemeFontSizeOverride("font_size", 18);
            box.AddChild(title);

            void Slider(string name, float min, float max, float step, System.Func<float> get, System.Action<float> set, string fmt)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 10);
                var label = new Label { Text = name, CustomMinimumSize = new Vector2(230, 0) };
                var value = new Label { CustomMinimumSize = new Vector2(70, 0), Text = get().ToString(fmt) };
                var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = get(), CustomMinimumSize = new Vector2(260, 24), FocusMode = Control.FocusModeEnum.None };
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
            var wipe = new Button { Text = "Новый вайп (стереть сохранение и начать заново)", FocusMode = Control.FocusModeEnum.None };
            wipe.Pressed += () => WipeRequested?.Invoke();
            box.AddChild(wipe);
            _adminSounds = new Label();
            _adminSounds.AddThemeFontSizeOverride("font_size", 13);
            _adminSounds.AddThemeColorOverride("font_color", new Color(0.8f, 1f, 0.8f));
            box.AddChild(_adminSounds);
            var hint = new Label { Text = "1.00 = как записано при обычном шаге. Присед и спринт меняют темп сами." };
            hint.AddThemeFontSizeOverride("font_size", 13);
            hint.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.6f));
            box.AddChild(hint);
        }

        public void ToggleAdmin() => _adminPanel.Visible = !_adminPanel.Visible;
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
            _visionMat = new ShaderMaterial { Shader = new Shader { Code = VisionShader } };
            _vision = FullRect(new Color(0, 0, 0, 0));
            _vision.Material = _visionMat;
            _vision.Visible = false;   // обзор теперь в шейдерах мира (VisionFx), персонажи не затеняются

            _damageRect = FullRect(new Color(0.8f, 0, 0, 0));

            _hintLabel = MakeLabel(this, new Vector2(16, 12), 15, new Color(1, 1, 1, 0.75f));
            _hintLabel.Text = "WASD — ходьба · Shift — бег · Alt — присед · ЛКМ — удар/выстрел/поставить · ПКМ — прицелиться (с Планом — держи: колесо стройки) · R — перезарядка\nE — открыть / взаимодействие · 1–6 — пояс · Q — крафт · Tab — инвентарь · M — карта · F — съесть · H — бинт · N — день/ночь · F9 — отладочный набор (ещё раз — рейд)";
            _modeLabel = MakeLabel(this, new Vector2(16, 58), 15, new Color(1, 0.9f, 0.6f, 0.85f));

            (_hpFill, _hpLabel) = MakeBar(-50, 26, new Color(0.45f, 0.75f, 0.3f), 16);
            (_foodFill, _foodLabel) = MakeBar(-76, 18, new Color(0.85f, 0.55f, 0.2f), 13);
            (_waterFill, _waterLabel) = MakeBar(-98, 18, new Color(0.3f, 0.55f, 0.85f), 13);

            _ammoLabel = MakeLabel(this, Vector2.Zero, 22, Colors.White);
            _ammoLabel.HorizontalAlignment = HorizontalAlignment.Right;
            _promptLabel = MakeLabel(this, Vector2.Zero, 20, new Color(1, 0.9f, 0.5f));
            _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _buildLabel = MakeLabel(this, Vector2.Zero, 15, new Color(0.75f, 0.88f, 1f));
            _buildLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _toastLabel = MakeLabel(this, Vector2.Zero, 20, Colors.White);
            _toastLabel.HorizontalAlignment = HorizontalAlignment.Center;

            BuildBelt();
            BuildAdmin();

            _invPanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
            AddChild(_invPanel);
            _invLabel = new Label();
            _invLabel.AddThemeFontSizeOverride("font_size", 17);
            _invPanel.AddChild(_invLabel);

            BuildCraftPanel();

            _deathRect = FullRect(new Color(0.25f, 0, 0, 0.55f));
            _deathRect.Visible = false;
            _deathLabel = MakeLabel(_deathRect, Vector2.Zero, 30, Colors.White);
            _deathLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _deathLabel.Text = "ТЕБЯ УБИЛИ\nвесь лут остался на трупе\n\nEnter — проснуться на пляже";

            Crosshair = new Crosshair { MouseFilter = Control.MouseFilterEnum.Ignore };
            AddChild(Crosshair);
        }

        /// <summary>Наблюдатель: прячем всё «своё» — пояс, полоски, подсказки управления, прицел.</summary>
        public void SpectatorMode()
        {
            foreach (var c in new CanvasItem[] { _belt, _hintLabel, _hpFill.GetParent<CanvasItem>(), _foodFill.GetParent<CanvasItem>(), _waterFill.GetParent<CanvasItem>(), _promptLabel, _modeLabel, _ammoLabel, _buildLabel, Crosshair, _vision })
                if (c != null) c.Visible = false;
            _hintLabel.Text = "";
        }

        (ColorRect fill, Label label) MakeBar(float y, float h, Color color, int font)
        {
            var back = new ColorRect { Color = new Color(0, 0, 0, 0.55f), Size = new Vector2(BarW, h), MouseFilter = Control.MouseFilterEnum.Ignore };
            back.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
            back.Position = new Vector2(20, y);
            AddChild(back);
            var fill = new ColorRect { Color = color, Size = new Vector2(BarW, h), MouseFilter = Control.MouseFilterEnum.Ignore };
            back.AddChild(fill);
            var label = MakeLabel(back, new Vector2(8, h > 20 ? 2 : -1), font, Colors.White);
            return (fill, label);
        }

        void BuildBelt()
        {
            _belt = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            _belt.AddThemeConstantOverride("separation", 6);
            AddChild(_belt);
            for (int i = 0; i < Actor.BeltSize; i++)
            {
                var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.55f), BorderColor = new Color(1, 1, 1, 0.25f) };
                style.SetBorderWidthAll(2);
                style.SetCornerRadiusAll(4);
                style.ContentMarginLeft = style.ContentMarginRight = 6;
                style.ContentMarginTop = style.ContentMarginBottom = 4;
                var panel = new PanelContainer { CustomMinimumSize = new Vector2(104, 54), MouseFilter = Control.MouseFilterEnum.Ignore };
                panel.AddThemeStyleboxOverride("panel", style);
                var icon = new TextureRect
                {
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest, MouseFilter = Control.MouseFilterEnum.Ignore,
                    Modulate = new Color(1, 1, 1, 0.9f),
                };
                panel.AddChild(icon);
                var label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
                label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
                label.AddThemeConstantOverride("outline_size", 4);
                label.AddThemeFontSizeOverride("font_size", 12);
                panel.AddChild(label);
                _belt.AddChild(panel);
                _beltSlots.Add((panel, label, style, icon));
            }
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

        static Label MakeLabel(Node parent, Vector2 pos, int size, Color color)
        {
            var l = new Label { Position = pos, MouseFilter = Control.MouseFilterEnum.Ignore };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", color);
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            l.AddThemeConstantOverride("outline_size", 5);
            parent.AddChild(l);
            return l;
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
                _codePanel = new PanelContainer { Visible = false };
                AddChild(_codePanel);
                var box = new VBoxContainer();
                _codePanel.AddChild(box);
                box.AddChild(new Label { Text = "Заперто. Код (4 цифры), Enter — ввести, Esc — отмена" });
                _codeEdit = new LineEdit { MaxLength = 4, PlaceholderText = "____", Alignment = HorizontalAlignment.Center };
                _codeEdit.AddThemeFontSizeOverride("font_size", 28);
                box.AddChild(_codeEdit);
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
        public void ToggleInventory() => _invPanel.Visible = !_invPanel.Visible;
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

        static void SetBar(ColorRect fill, float value, float max)
        {
            fill.Size = new Vector2(BarW * Mathf.Clamp(value / max, 0, 1), fill.Size.Y);
        }

        public void UpdateHud(SelfInfo s, float dt, string modeText)
        {
            SetBar(_hpFill, s.Hp, s.MaxHp);
            _hpFill.Color = s.Hp > 50 ? new Color(0.45f, 0.75f, 0.3f) : s.Hp > 25 ? new Color(0.85f, 0.65f, 0.2f) : new Color(0.85f, 0.2f, 0.15f);
            _hpLabel.Text = $"HP {Mathf.CeilToInt(s.Hp)}   · бинты: {s.Bandages}";
            SetBar(_foodFill, s.Food, 100);
            _foodLabel.Text = $"Еда {Mathf.CeilToInt(s.Food)}";
            SetBar(_waterFill, s.Water, 100);
            _waterLabel.Text = $"Вода {Mathf.CeilToInt(s.Water)}";

            _ammoLabel.Text = s.HoldingGun ? $"{s.WeaponName}   {s.Ammo} / {s.Magazine}   ·  запас {s.Reserve}" : s.WeaponName;

            for (int i = 0; i < _beltSlots.Count; i++)
            {
                var (_, label, style, icon) = _beltSlots[i];
                var slot = s.Belt[i];
                string text = $"{i + 1}";
                icon.Texture = slot.HasValue ? Art.Icon(slot.Value.Id) : null;
                if (slot.HasValue)
                {
                    var b = slot.Value;
                    if (icon.Texture == null) text += $"  {b.Name}";
                    if (b.IsGun) text += $"\n{b.Ammo}/{b.Magazine}";
                    else if (b.Count > 1) text += $"\n×{b.Count}";
                }
                label.Text = text;
                bool sel = i == s.Current;
                style.BorderColor = sel ? new Color(1, 0.85f, 0.4f) : new Color(1, 1, 1, 0.25f);
                style.BgColor = sel ? new Color(0.25f, 0.2f, 0.08f, 0.75f) : new Color(0, 0, 0, 0.55f);
            }

            _modeLabel.Text = modeText;
            _promptLabel.Text = s.InteractHint ?? "";
            _buildLabel.Text = s.Dead ? "" : s.BuildStatus ?? "";



            if (_craftWin.Visible) _craftWin.Refresh(s);
            if (_techWin.Visible) _techWin.Refresh(s);

            _deathRect.Visible = s.Dead;
            if (s.Dead)
                _deathLabel.Text = "ТЕБЯ УБИЛИ\nвесь лут остался на трупе\n\nEnter — проснуться на пляже" + (s.HasBag ? (s.BagReadyIn > 0 ? $"\nспальник будет готов через {s.BagReadyIn:0} с" : "\nB — проснуться в спальнике") : "");

            _toastTime -= dt;
            _toastLabel.Modulate = new Color(1, 1, 1, Mathf.Clamp(_toastTime, 0, 1));
            _damageFlash = Mathf.Max(0, _damageFlash - dt);
            _damageRect.Color = new Color(0.8f, 0, 0, _damageFlash * 0.5f);

            var vs = GetViewport().GetVisibleRect().Size;
            Place(_ammoLabel, vs, 1, 1, new Vector2(-20, -20));
            if (_adminPanel.Visible) Place(_adminPanel, vs, 0.5f, 0.3f, Vector2.Zero);
            Place(_belt, vs, 0.5f, 1, new Vector2(0, -16));
            Place(_promptLabel, vs, 0.5f, 1, new Vector2(0, -90));
            Place(_buildLabel, vs, 0.5f, 1, new Vector2(0, -118));
            Place(_toastLabel, vs, 0.5f, 0, new Vector2(0, 90));
            Place(_invPanel, vs, 1, 0.5f, new Vector2(-30, 0));
            Place(_deathLabel, vs, 0.5f, 0.5f, Vector2.Zero);

            Crosshair.HitFlash = Mathf.Max(0, Crosshair.HitFlash - dt);
            Crosshair.ReloadProgress = s.ReloadProgress;
            Crosshair.Visible = !CraftOpen;
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
