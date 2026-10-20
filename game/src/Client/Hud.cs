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

        Label _hpLabel, _foodLabel, _waterLabel, _ammoLabel, _hintLabel, _toastLabel, _promptLabel, _invLabel, _deathLabel, _modeLabel, _queueLabel;
        ColorRect _hpFill, _foodFill, _waterFill, _damageRect, _deathRect, _vision;
        PanelContainer _invPanel, _craftPanel;
        VBoxContainer _recipeBox;
        readonly List<Button> _recipeButtons = new();
        HBoxContainer _belt;
        readonly List<(PanelContainer panel, Label label, StyleBoxFlat style)> _beltSlots = new();
        ShaderMaterial _visionMat;
        float _toastTime, _damageFlash;

        public Crosshair Crosshair { get; private set; }
        public bool CraftOpen => _craftPanel.Visible;
        public event Action<int> CraftRequested;

        const string VisionShader = @"
shader_type canvas_item;
uniform vec2 player_px;
uniform vec2 facing_dir = vec2(1.0, 0.0);
uniform float cos_half = 0.42;
uniform float near_px = 80.0;
uniform float far_px = 1600.0;
uniform float darkness = 0.3;
void fragment() {
    vec2 d = FRAGCOORD.xy - player_px;
    float dist = length(d);
    float c = dot(d / max(dist, 0.001), facing_dir);
    // ровные края без размытия (как в Project Zomboid), лёгкое сглаживание только против лесенки
    float aa = 1.5 / max(dist, 1.0);
    float cone = smoothstep(cos_half - aa, cos_half + aa, c) * (1.0 - smoothstep(far_px - 1.0, far_px + 1.0, dist));
    float nearv = 1.0 - smoothstep(near_px - 1.0, near_px + 1.0, dist);
    COLOR = vec4(0.02, 0.03, 0.07, (1.0 - max(cone, nearv)) * darkness);
}";

        public override void _Ready()
        {
            _visionMat = new ShaderMaterial { Shader = new Shader { Code = VisionShader } };
            _vision = FullRect(new Color(0, 0, 0, 0));
            _vision.Material = _visionMat;

            _damageRect = FullRect(new Color(0.8f, 0, 0, 0));

            _hintLabel = MakeLabel(this, new Vector2(16, 12), 15, new Color(1, 1, 1, 0.75f));
            _hintLabel.Text = "WASD — ходьба · Shift — бег · ЛКМ — удар/выстрел/поставить · ПКМ — прицелиться · R — перезарядка\nE — взаимодействие · 1–6 — пояс · Q — крафт · Tab — инвентарь · F — съесть · H — бинт · F9 — отладочный набор";
            _modeLabel = MakeLabel(this, new Vector2(16, 58), 15, new Color(1, 0.9f, 0.6f, 0.85f));

            (_hpFill, _hpLabel) = MakeBar(-50, 26, new Color(0.45f, 0.75f, 0.3f), 16);
            (_foodFill, _foodLabel) = MakeBar(-76, 18, new Color(0.85f, 0.55f, 0.2f), 13);
            (_waterFill, _waterLabel) = MakeBar(-98, 18, new Color(0.3f, 0.55f, 0.85f), 13);

            _ammoLabel = MakeLabel(this, Vector2.Zero, 22, Colors.White);
            _ammoLabel.HorizontalAlignment = HorizontalAlignment.Right;
            _promptLabel = MakeLabel(this, Vector2.Zero, 20, new Color(1, 0.9f, 0.5f));
            _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _toastLabel = MakeLabel(this, Vector2.Zero, 20, Colors.White);
            _toastLabel.HorizontalAlignment = HorizontalAlignment.Center;

            BuildBelt();

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
                var label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
                label.AddThemeFontSizeOverride("font_size", 12);
                panel.AddChild(label);
                _belt.AddChild(panel);
                _beltSlots.Add((panel, label, style));
            }
        }

        void BuildCraftPanel()
        {
            _craftPanel = new PanelContainer { Visible = false, Position = new Vector2(20, 110) };
            AddChild(_craftPanel);
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 4);
            _craftPanel.AddChild(box);
            var title = new Label { Text = "КРАФТ  (Q — закрыть)" };
            title.AddThemeFontSizeOverride("font_size", 18);
            box.AddChild(title);
            _recipeBox = new VBoxContainer();
            _recipeBox.AddThemeConstantOverride("separation", 3);
            box.AddChild(_recipeBox);
            _queueLabel = new Label();
            _queueLabel.AddThemeFontSizeOverride("font_size", 14);
            _queueLabel.AddThemeColorOverride("font_color", new Color(1, 0.9f, 0.6f));
            box.AddChild(_queueLabel);
        }

        void EnsureRecipeButtons(List<RecipeInfo> recipes)
        {
            if (_recipeButtons.Count == recipes.Count) return;
            foreach (var b in _recipeButtons) b.QueueFree();
            _recipeButtons.Clear();
            foreach (var r in recipes)
            {
                int index = r.Index;
                var b = new Button { Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None };
                b.AddThemeFontSizeOverride("font_size", 14);
                b.Pressed += () => CraftRequested?.Invoke(index);
                _recipeBox.AddChild(b);
                _recipeButtons.Add(b);
            }
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

        public void Toast(string text)
        {
            _toastLabel.Text = text;
            _toastTime = 3.5f;
        }

        public void FlashDamage() => _damageFlash = 0.45f;
        public void ToggleInventory() => _invPanel.Visible = !_invPanel.Visible;
        public void ToggleCraft() => _craftPanel.Visible = !_craftPanel.Visible;

        public void SetVision(Vector2 playerPx, Vector2 facing, float nearPx, float farPx, float cosHalf, bool enabled)
        {
            _visionMat.SetShaderParameter("cos_half", cosHalf);
            _visionMat.SetShaderParameter("player_px", playerPx);
            _visionMat.SetShaderParameter("facing_dir", facing);
            _visionMat.SetShaderParameter("near_px", nearPx);
            _visionMat.SetShaderParameter("far_px", farPx);
            _visionMat.SetShaderParameter("darkness", enabled ? 0.3f : 0f);
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
                var (_, label, style) = _beltSlots[i];
                var slot = s.Belt[i];
                string text = $"{i + 1}";
                if (slot.HasValue)
                {
                    var b = slot.Value;
                    text += $"  {b.Name}";
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

            var inv = new System.Text.StringBuilder("ИНВЕНТАРЬ\n\n");
            if (s.Inventory.Count == 0) inv.Append("пусто");
            foreach (var (name, count) in s.Inventory) inv.Append($"{name} ×{count}\n");
            _invLabel.Text = inv.ToString();

            if (_craftPanel.Visible)
            {
                EnsureRecipeButtons(s.Recipes);
                for (int i = 0; i < s.Recipes.Count; i++)
                {
                    var r = s.Recipes[i];
                    string bench = r.NeedsBench ? (s.NearBench ? " · верстак ✓" : " · нужен верстак") : "";
                    _recipeButtons[i].Text = $"{r.Name} — {r.CostText} · {r.Time:0} с{bench}";
                    _recipeButtons[i].Disabled = !r.CanCraft;
                }
                var q = new System.Text.StringBuilder();
                if (s.CraftQueue.Count > 0) q.Append("Очередь: ");
                for (int i = 0; i < s.CraftQueue.Count; i++)
                {
                    var (name, progress) = s.CraftQueue[i];
                    q.Append(i == 0 ? $"{name} {Mathf.RoundToInt(progress * 100)}%" : $" · {name}");
                }
                _queueLabel.Text = q.ToString();
            }

            _deathRect.Visible = s.Dead;

            _toastTime -= dt;
            _toastLabel.Modulate = new Color(1, 1, 1, Mathf.Clamp(_toastTime, 0, 1));
            _damageFlash = Mathf.Max(0, _damageFlash - dt);
            _damageRect.Color = new Color(0.8f, 0, 0, _damageFlash * 0.5f);

            var vs = GetViewport().GetVisibleRect().Size;
            Place(_ammoLabel, vs, 1, 1, new Vector2(-20, -20));
            Place(_belt, vs, 0.5f, 1, new Vector2(0, -16));
            Place(_promptLabel, vs, 0.5f, 1, new Vector2(0, -90));
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
