using System;
using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>Полоска элементов над поясом, пока План в руках: выбранный подсвечен, колесо мыши листает.</summary>
    public partial class BuildBar : Control
    {
        const float C = 40, Gap = 4;
        static readonly string[] IconIds = { "foundation", "wall", "doorway", "window", "door", "triangle" };   // по номеру вида
        int _sel = -1, _ghost = -1;

        public BuildBar()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            CustomMinimumSize = new Vector2(6 * C + 5 * Gap, C + 20);
        }

        public void Set(int selected, int ghost)
        {
            if (selected == _sel && ghost == _ghost) return;
            _sel = selected; _ghost = ghost;
            QueueRedraw();
        }

        public override void _Draw()
        {
            float w = 6 * C + 5 * Gap;
            string cap = $"{Rastvordd.Sim.Building.KindName((Rastvordd.Sim.StructKind)_sel)}   ·   зажми ПКМ — колесо";
            var fs = UiTheme.Text.GetStringSize(cap, HorizontalAlignment.Left, -1, 12);
            DrawString(UiTheme.Text, new Vector2((w - fs.X) / 2 + 1, 13), cap, HorizontalAlignment.Left, -1, 12, new Color(0, 0, 0, 0.8f));
            DrawString(UiTheme.Text, new Vector2((w - fs.X) / 2, 12), cap, HorizontalAlignment.Left, -1, 12, UiTheme.Ink);
            for (int slot = 0; slot < Rastvordd.Sim.Building.PieceOrder.Length; slot++)
            {
                int k = (int)Rastvordd.Sim.Building.PieceOrder[slot];
                var r = new Rect2(slot * (C + Gap), 20, C, C);
                DrawRect(r, new Color(0.07f, 0.07f, 0.07f, 0.75f));
                var icon = Art.Get($"icons/icon_build_{IconIds[k]}");
                if (icon != null) DrawTextureRect(icon, r.Grow(-4), false, k == _sel || k == _ghost ? Colors.White : new Color(1, 1, 1, 0.45f));
                if (k == _sel) DrawRect(r.Grow(-1), UiTheme.Accent, false, 2);
                else if (k == _ghost) DrawRect(r.Grow(-1), new Color(UiTheme.Accent, 0.55f), false, 1);
            }
        }
    }

    /// <summary>
    /// Колесо стройки как в Расте: пока зажата ПКМ с Планом — круг секторов вокруг центра экрана,
    /// ведёшь мышь к сектору, отпускаешь ПКМ — элемент выбран. Мышь у центра — выбор не меняется.
    /// </summary>
    public partial class BuildMenu : Control
    {
        public event Action<int> Picked;
        const float Outer = 160, Inner = 58, IconR = 110, IconSize = 58;
        static readonly string[] IconIds = { "foundation", "wall", "doorway", "window", "door", "triangle" };   // по номеру вида
        static readonly Dictionary<string, string> Hints = new()
        {
            ["Фундамент"] = "пол 2×2 м, с него всё начинается",
            ["Стена"] = "на край фундамента",
            ["Дверной проём"] = "стена с проходом под дверь",
            ["Окно"] = "видно и можно стрелять",
            ["Треугольный фундамент"] = "к любому свободному краю пола",
            ["Дверь"] = "в дверной проём",
        };
        List<(int kind, string name, string cost, bool can)> _pieces = new();
        int _current, _hover = -1;

        public BuildMenu()
        {
            Visible = false;
            MouseFilter = MouseFilterEnum.Ignore;
            SetAnchorsPreset(LayoutPreset.FullRect);
        }

        Vector2 Center => GetViewportRect().Size / 2;

        public void Fill(List<(int kind, string name, string cost, bool can)> pieces, int current)
        {
            _pieces = pieces;
            _current = current;
        }

        /// <summary>ПКМ нажата: показать колесо, курсор — в центр.</summary>
        public void Open()
        {
            Visible = true;
            _hover = -1;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetViewport().WarpMouse(Center);
            QueueRedraw();
        }

        /// <summary>ПКМ отпущена: выбрать сектор под курсором и закрыть.</summary>
        public void Release()
        {
            if (!Visible) return;
            Visible = false;
            Input.MouseMode = Input.MouseModeEnum.Hidden;
            if (_hover >= 0 && _hover < _pieces.Count) Picked?.Invoke(_pieces[_hover].kind);
        }

        /// <summary>Цифры 1–5 выбирают элемент, пока колесо открыто.</summary>
        public bool PickByKey(Key key)
        {
            int i = (int)(key - Key.Key1);
            if (!Visible || i < 0 || i >= _pieces.Count) return false;
            _hover = i;
            Release();
            return true;
        }

        static float SectorAngle(int k, int n) => -Mathf.Pi / 2 + k * Mathf.Tau / n;   // первый сектор — сверху, дальше по часовой

        public override void _Process(double delta)
        {
            if (!Visible) return;
            int n = Math.Max(1, _pieces.Count);
            var d = GetViewport().GetMousePosition() - Center;
            int h = -1;
            if (d.Length() > Inner * 0.6f)
            {
                float a = Mathf.PosMod(d.Angle() + Mathf.Pi / 2 + Mathf.Pi / n, Mathf.Tau);
                h = (int)(a / (Mathf.Tau / n)) % n;
            }
            if (h != _hover) { _hover = h; QueueRedraw(); }
        }

        static Vector2[] Ring(Vector2 c, float r0, float r1, float a0, float a1)
        {
            const int steps = 18;
            var pts = new Vector2[(steps + 1) * 2];
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)steps);
                pts[i] = c + Vector2.FromAngle(a) * r1;
                pts[pts.Length - 1 - i] = c + Vector2.FromAngle(a) * r0;
            }
            return pts;
        }

        public override void _Draw()
        {
            int n = _pieces.Count;
            if (n == 0) return;
            var c = Center;
            float step = Mathf.Tau / n, gap = 0.025f;
            for (int k = 0; k < n; k++)
            {
                float mid = SectorAngle(k, n);
                bool hov = k == _hover, cur = _pieces[k].kind == _current;
                float r1 = hov ? Outer + 8 : Outer;
                DrawColoredPolygon(Ring(c, Inner, r1, mid - step / 2 + gap, mid + step / 2 - gap),
                    hov ? new Color(0.2f, 0.2f, 0.19f, 0.92f) : new Color(0.07f, 0.07f, 0.07f, 0.82f));
                if (hov || cur)
                    DrawColoredPolygon(Ring(c, r1 - 3, r1, mid - step / 2 + gap, mid + step / 2 - gap), hov ? UiTheme.Accent : new Color(UiTheme.Accent, 0.5f));
                var icon = Art.Get($"icons/icon_build_{IconIds[Math.Clamp(_pieces[k].kind, 0, IconIds.Length - 1)]}");
                var ip = c + Vector2.FromAngle(mid) * (hov ? IconR + 4 : IconR);
                float s = hov ? IconSize + 8 : IconSize;
                if (icon != null) DrawTextureRect(icon, new Rect2(ip - new Vector2(s, s) / 2, new Vector2(s, s)), false, _pieces[k].can ? Colors.White : new Color(0.6f, 0.55f, 0.55f));
            }
            // центр: что под курсором
            DrawCircle(c, Inner - 6, new Color(0.05f, 0.05f, 0.05f, 0.88f));
            int show = _hover >= 0 ? _hover : _pieces.FindIndex(p => p.kind == _current);
            if (show < 0) return;
            var (_, name, cost, can) = _pieces[show];
            var font = UiTheme.Text;
            void Line(string t, float y, int size, Color col)
            {
                var sz = font.GetStringSize(t, HorizontalAlignment.Left, -1, size);
                DrawString(font, new Vector2(c.X - sz.X / 2, c.Y + y), t, HorizontalAlignment.Left, -1, size, col);
            }
            Line(name, -4, 14, _hover >= 0 ? UiTheme.Accent : UiTheme.Ink);
            Line(cost, 14, 11, can ? UiTheme.Dim : new Color(0.9f, 0.42f, 0.38f));
            string hint = Hints.GetValueOrDefault(name, "");
            var hs = font.GetStringSize(hint, HorizontalAlignment.Left, -1, 13);
            var hp = new Vector2(c.X - hs.X / 2, c.Y + Outer + 34);
            DrawString(font, hp + Vector2.One, hint, HorizontalAlignment.Left, -1, 13, new Color(0, 0, 0, 0.9f));
            DrawString(font, hp, hint, HorizontalAlignment.Left, -1, 13, UiTheme.Ink);
        }
    }
}
