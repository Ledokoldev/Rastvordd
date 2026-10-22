using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Ивенты режиссёра в мире: самолёт сброса (тень по земле), парашют с грузом, световой столб-маяк над грузом и
    /// закрытым ящиком (видно издалека — туда сбегаются), сигнальный дым у приземлившегося груза.
    /// </summary>
    public partial class DirectorLayer : Node2D
    {
        List<DirectorMarker> _markers = new();
        float _time;

        public DirectorLayer()
        {
            ZIndex = 5;
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Mix };
        }

        public void Sync(List<DirectorMarker> markers, float time)
        {
            _markers = markers;
            _time = time;
            QueueRedraw();
        }

        public override void _Draw()
        {
            foreach (var m in _markers)
            {
                var at = Iso.Px(m.Pos);
                if (m.Kind == "airdrop" && !m.Started)
                {
                    // самолёт: проходит над точкой за 25 с до приземления груза; парашют — с этого момента вниз
                    float k = (_time - (m.StartsAt - 45)) / 40f;
                    if (k > 0 && k < 1)
                    {
                        var p = Iso.Px(System.Numerics.Vector2.Lerp(m.PlaneFrom, m.PlaneTo, k));
                        var dir = (Iso.Px(m.PlaneTo) - Iso.Px(m.PlaneFrom)).Normalized();
                        DrawPlane(p + new Vector2(30, 20), dir, new Color(0, 0, 0, 0.35f));                 // тень на земле
                        DrawPlane(p - new Vector2(0, Iso.H(60)), dir, new Color(0.32f, 0.36f, 0.3f, 1f));   // сам «кукурузник»
                    }
                    float fall = (_time - (m.StartsAt - 25)) / 25f;
                    if (fall > 0 && fall < 1)
                    {
                        float h = Iso.H(80 * (1 - fall));
                        var c = at - new Vector2(Mathf.Sin(_time * 1.3f) * 6, h);
                        DrawCircle(at + new Vector2(6, 3), 10 * (0.4f + 0.6f * fall), new Color(0, 0, 0, 0.25f));   // тень груза
                        // купол и стропы
                        var dome = new List<Vector2>();
                        for (int i = 0; i <= 12; i++) { float a = Mathf.Pi + Mathf.Pi * i / 12f; dome.Add(c + new Vector2(Mathf.Cos(a) * 22, Mathf.Sin(a) * 14 - 28)); }
                        DrawColoredPolygon(dome.ToArray(), new Color(0.85f, 0.35f, 0.2f));
                        DrawLine(c + new Vector2(-22, -28), c + new Vector2(-4, -6), new Color(0.2f, 0.2f, 0.2f), 1);
                        DrawLine(c + new Vector2(22, -28), c + new Vector2(4, -6), new Color(0.2f, 0.2f, 0.2f), 1);
                        DrawLine(c + new Vector2(0, -42), c + new Vector2(0, -6), new Color(0.2f, 0.2f, 0.2f), 1);
                        DrawRect(new Rect2(c + new Vector2(-8, -8), new Vector2(16, 12)), new Color(0.3f, 0.36f, 0.22f));
                        DrawRect(new Rect2(c + new Vector2(-8, -8), new Vector2(16, 12)), new Color(0.9f, 0.75f, 0.2f), false, 1);
                    }
                    continue;
                }
                // маяк: световой столб над грузом / ящиком (красный — груз, жёлтый — закрытый ящик; открыт — зелёный)
                var col = m.Open ? new Color(0.4f, 1f, 0.45f) : m.Kind == "airdrop" ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 0.85f, 0.25f);
                float pulse = 0.55f + 0.45f * Mathf.Sin(_time * (m.Started && !m.Open ? 6f : 2f));
                float hh = Iso.H(40);
                DrawColoredPolygon(new[] { at + new Vector2(-6, 0), at + new Vector2(6, 0), at + new Vector2(14, -hh), at + new Vector2(-14, -hh) },
                    new Color(col, 0.18f * pulse));
                DrawColoredPolygon(new[] { at + new Vector2(-2, 0), at + new Vector2(2, 0), at + new Vector2(4, -hh), at + new Vector2(-4, -hh) },
                    new Color(col, 0.35f * pulse));
                if (m.Kind == "airdrop" && m.Started && !m.Open)
                    for (int i = 0; i < 6; i++)   // сигнальный дым
                    {
                        float s = (_time * 0.4f + i / 6f) % 1f;
                        DrawCircle(at + new Vector2(Mathf.Sin(i * 1.7f + _time) * 6 + s * 20, -s * Iso.H(14)), 6 + s * 14, new Color(0.9f, 0.3f, 0.2f, 0.35f * (1 - s)));
                    }
            }
        }

        void DrawPlane(Vector2 p, Vector2 dir, Color c)
        {
            // силуэт биплана сверху: фюзеляж, крылья, хвост
            var side = new Vector2(-dir.Y, dir.X);
            DrawColoredPolygon(new[] { p + dir * 26, p + side * 4, p - dir * 22 + side * 3, p - dir * 22 - side * 3, p - side * 4 }, c);
            DrawColoredPolygon(new[] { p + dir * 8 + side * 34, p + dir * 2 + side * 34, p + dir * 2 - side * 34, p + dir * 8 - side * 34 }, c);
            DrawColoredPolygon(new[] { p - dir * 16 + side * 12, p - dir * 21 + side * 12, p - dir * 21 - side * 12, p - dir * 16 - side * 12 }, c);
        }
    }

    /// <summary>Радио: новые слухи режиссёра — стеклянные плашки вверху по центру, держатся 12 с, последние полсекунды гаснут.</summary>
    public partial class RadioFeed : VBoxContainer
    {
        readonly List<(string text, float left)> _lines = new();
        readonly List<PanelContainer> _pills = new();
        int _lastN;
        string _sig;

        public RadioFeed()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            Theme = UiTheme.Theme;
            AddThemeConstantOverride("separation", 6);
        }

        public void Sync(List<(int n, string text)> radio, float dt)
        {
            foreach (var (n, text) in radio)
                if (n > _lastN) { _lastN = n; _lines.Add((text, 12)); }
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                var (t, left) = _lines[i];
                left -= dt;
                if (left <= 0) _lines.RemoveAt(i); else _lines[i] = (t, left);
            }
            while (_lines.Count > 3) _lines.RemoveAt(0);
            string sig = string.Join("|", _lines.ConvertAll(l => l.text));
            if (sig != _sig)
            {
                _sig = sig;
                foreach (var p in _pills) p.QueueFree();
                _pills.Clear();
                foreach (var (t, _) in _lines)
                {
                    var p = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
                    p.AddThemeStyleboxOverride("panel", UiTheme.Pill(0.66f));
                    var row = UiTheme.HBox(10);
                    p.AddChild(row);
                    var tag = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
                    tag.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(UiTheme.Accent, 0.18f), 6, null, 0, 7, 1));
                    tag.AddChild(UiTheme.Strong("РАДИО", 11, UiTheme.Accent));
                    row.AddChild(tag);
                    row.AddChild(UiTheme.Strong(t, UiTheme.FontSmall, UiTheme.Ink));
                    AddChild(p);
                    _pills.Add(p);
                }
            }
            for (int i = 0; i < _pills.Count && i < _lines.Count; i++)
                _pills[i].Modulate = new Color(1, 1, 1, Mathf.Clamp(_lines[i].left / 0.5f, 0, 1));
        }
    }
}
