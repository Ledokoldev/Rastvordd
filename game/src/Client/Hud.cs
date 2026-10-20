using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>Интерфейс поверх мира: HP, патроны, подсказки, инвентарь, смерть, затемнение вне обзора.</summary>
    public partial class Hud : CanvasLayer
    {
        Label _hpLabel, _ammoLabel, _hintLabel, _toastLabel, _promptLabel, _invLabel, _deathLabel, _modeLabel;
        ColorRect _hpFill, _damageRect, _deathRect, _vision;
        PanelContainer _invPanel;
        ShaderMaterial _visionMat;
        float _toastTime, _damageFlash;
        public Crosshair Crosshair { get; private set; }

        const string VisionShader = @"
shader_type canvas_item;
uniform vec2 player_px;
uniform vec2 facing_dir = vec2(1.0, 0.0);
uniform float cos_half = 0.42;
uniform float near_px = 80.0;
uniform float far_px = 1600.0;
uniform float darkness = 0.55;
void fragment() {
    vec2 d = FRAGCOORD.xy - player_px;
    float dist = length(d);
    float c = dot(d / max(dist, 0.001), facing_dir);
    float cone = smoothstep(cos_half - 0.06, cos_half + 0.03, c) * (1.0 - smoothstep(far_px * 0.88, far_px, dist));
    float nearv = 1.0 - smoothstep(near_px * 0.85, near_px * 1.2, dist);
    COLOR = vec4(0.02, 0.03, 0.07, (1.0 - max(cone, nearv)) * darkness);
}";

        public override void _Ready()
        {
            _visionMat = new ShaderMaterial { Shader = new Shader { Code = VisionShader } };
            _vision = FullRect(new Color(0, 0, 0, 0));
            _vision.Material = _visionMat;

            _damageRect = FullRect(new Color(0.8f, 0, 0, 0));

            _hintLabel = MakeLabel(this, new Vector2(16, 12), 15, new Color(1, 1, 1, 0.75f));
            _hintLabel.Text = "WASD — ходьба · Shift — бег · ЛКМ — стрелять · R — перезарядка · E — лутать · H — бинт\n1/2/3 — оружие · Tab — инвентарь · колесо — зум · Esc — курсор";
            _modeLabel = MakeLabel(this, new Vector2(16, 58), 15, new Color(1, 0.9f, 0.6f, 0.85f));

            var hpBack = new ColorRect { Color = new Color(0, 0, 0, 0.55f), Size = new Vector2(260, 26), MouseFilter = Control.MouseFilterEnum.Ignore };
            hpBack.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
            hpBack.Position = new Vector2(20, -50);
            AddChild(hpBack);
            _hpFill = new ColorRect { Color = new Color(0.45f, 0.75f, 0.3f), Size = new Vector2(260, 26), MouseFilter = Control.MouseFilterEnum.Ignore };
            hpBack.AddChild(_hpFill);
            _hpLabel = MakeLabel(hpBack, new Vector2(8, 2), 16, Colors.White);

            _ammoLabel = MakeLabel(this, Vector2.Zero, 22, Colors.White);
            _ammoLabel.HorizontalAlignment = HorizontalAlignment.Right;
            _promptLabel = MakeLabel(this, Vector2.Zero, 20, new Color(1, 0.9f, 0.5f));
            _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _toastLabel = MakeLabel(this, Vector2.Zero, 20, Colors.White);
            _toastLabel.HorizontalAlignment = HorizontalAlignment.Center;

            _invPanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
            AddChild(_invPanel);
            _invLabel = new Label();
            _invLabel.AddThemeFontSizeOverride("font_size", 17);
            _invPanel.AddChild(_invLabel);

            _deathRect = FullRect(new Color(0.25f, 0, 0, 0.55f));
            _deathRect.Visible = false;
            _deathLabel = MakeLabel(_deathRect, Vector2.Zero, 30, Colors.White);
            _deathLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _deathLabel.Text = "ТЕБЯ УБИЛИ\nвесь лут остался на трупе\n\nEnter — проснуться на пляже";

            Crosshair = new Crosshair { MouseFilter = Control.MouseFilterEnum.Ignore };
            AddChild(Crosshair);
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

        public void SetVision(Vector2 playerPx, Vector2 facing, float nearPx, float farPx, float cosHalf, bool enabled)
        {
            _visionMat.SetShaderParameter("cos_half", cosHalf);
            _visionMat.SetShaderParameter("player_px", playerPx);
            _visionMat.SetShaderParameter("facing_dir", facing);
            _visionMat.SetShaderParameter("near_px", nearPx);
            _visionMat.SetShaderParameter("far_px", farPx);
            _visionMat.SetShaderParameter("darkness", enabled ? 0.55f : 0f);
        }

        public void UpdateHud(SelfInfo s, float dt, string modeText)
        {
            _hpFill.Size = new Vector2(260 * Mathf.Clamp(s.Hp / s.MaxHp, 0, 1), 26);
            _hpFill.Color = s.Hp > 50 ? new Color(0.45f, 0.75f, 0.3f) : s.Hp > 25 ? new Color(0.85f, 0.65f, 0.2f) : new Color(0.85f, 0.2f, 0.15f);
            _hpLabel.Text = $"HP {Mathf.CeilToInt(s.Hp)}   · бинты: {s.Bandages}";

            var bar = new System.Text.StringBuilder();
            for (int i = 0; i < s.Hotbar.Length; i++)
                bar.Append(i == s.Current ? $"[{i + 1} {s.Hotbar[i]}]  " : $"{i + 1} {s.Hotbar[i]}  ");
            _ammoLabel.Text = $"{bar}\n{s.WeaponName}   {s.Ammo} / {s.Magazine}   ·  запас {s.Reserve}";

            _modeLabel.Text = modeText;
            _promptLabel.Text = s.NearLoot && !s.Dead ? "[E] Залутать" : "";

            var inv = new System.Text.StringBuilder("ИНВЕНТАРЬ\n\n");
            if (s.Inventory.Count == 0) inv.Append("пусто");
            foreach (var (name, count) in s.Inventory) inv.Append($"{name} ×{count}\n");
            _invLabel.Text = inv.ToString();

            _deathRect.Visible = s.Dead;

            _toastTime -= dt;
            _toastLabel.Modulate = new Color(1, 1, 1, Mathf.Clamp(_toastTime, 0, 1));
            _damageFlash = Mathf.Max(0, _damageFlash - dt);
            _damageRect.Color = new Color(0.8f, 0, 0, _damageFlash * 0.5f);

            var vs = GetViewport().GetVisibleRect().Size;
            Place(_ammoLabel, vs, 1, 1, new Vector2(-20, -20));
            Place(_promptLabel, vs, 0.5f, 1, new Vector2(0, -130));
            Place(_toastLabel, vs, 0.5f, 0, new Vector2(0, 90));
            Place(_invPanel, vs, 1, 0.5f, new Vector2(-30, 0));
            Place(_deathLabel, vs, 0.5f, 0.5f, Vector2.Zero);

            Crosshair.HitFlash = Mathf.Max(0, Crosshair.HitFlash - dt);
            Crosshair.ReloadProgress = s.ReloadProgress;
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
