using System;
using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>Полоска элементов над поясом, пока План в руках: выбранный подсвечен, колесо мыши листает.</summary>
    public partial class BuildBar : Control
    {
        const float C = 44, Gap = 6;
        static readonly string[] IconIds = { "foundation", "wall", "doorway", "window", "door", "triangle" };   // по номеру вида
        int _sel = -1, _ghost = -1;

        public BuildBar()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            TextureFilter = TextureFilterEnum.Linear;
            CustomMinimumSize = new Vector2(6 * C + 5 * Gap, C + 22);
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
            float cw = UiTheme.TextWidth(cap, UiTheme.FontCaption);
            var pill = new Rect2((w - cw) / 2 - 10, -4, cw + 20, 20);
            UiTheme.DrawRound(this, pill, new Color(UiTheme.PanelBg, 0.7f), 10);
            UiTheme.DrawText(this, pill.Position + new Vector2(10, 14), cap, UiTheme.FontCaption, UiTheme.Body);
            for (int slot = 0; slot < Rastvordd.Sim.Building.PieceOrder.Length; slot++)
            {
                int k = (int)Rastvordd.Sim.Building.PieceOrder[slot];
                var r = new Rect2(slot * (C + Gap), 22, C, C);
                bool on = k == _sel, gh = k == _ghost;
                UiTheme.DrawRound(this, r, on ? new Color(1, 1, 1, 0.2f) : new Color(UiTheme.PanelBg, 0.7f), 10, on ? new Color(1, 1, 1, 0.8f) : gh ? new Color(1, 1, 1, 0.35f) : UiTheme.PanelBorder, on ? 2 : 1);
                var icon = Art.Get($"icons/icon_build_{IconIds[k]}");
                if (icon != null) DrawTextureRect(icon, r.Grow(-6), false, on || gh ? Colors.White : new Color(1, 1, 1, 0.5f));
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
            TextureFilter = TextureFilterEnum.Linear;
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
                var seg = Ring(c, Inner, r1, mid - step / 2 + gap, mid + step / 2 - gap);
                DrawColoredPolygon(seg, hov ? new Color(0.24f, 0.24f, 0.26f, 0.88f) : new Color(UiTheme.PanelBg, 0.8f));
                var outline = new Vector2[seg.Length + 1];
                seg.CopyTo(outline, 0); outline[seg.Length] = seg[0];
                DrawPolyline(outline, hov ? new Color(1, 1, 1, 0.7f) : UiTheme.PanelBorder, hov ? 1.5f : 1f, true);
                if (cur) DrawColoredPolygon(Ring(c, r1 - 3, r1, mid - step / 2 + gap + 0.02f, mid + step / 2 - gap - 0.02f), Colors.White);
                var icon = Art.Get($"icons/icon_build_{IconIds[Math.Clamp(_pieces[k].kind, 0, IconIds.Length - 1)]}");
                var ip = c + Vector2.FromAngle(mid) * (hov ? IconR + 4 : IconR);
                float s = hov ? IconSize + 8 : IconSize;
                if (icon != null) DrawTextureRect(icon, new Rect2(ip - new Vector2(s, s) / 2, new Vector2(s, s)), false, _pieces[k].can ? Colors.White : new Color(0.6f, 0.55f, 0.55f));
            }
            // центр: что под курсором
            DrawCircle(c, Inner - 6, new Color(UiTheme.PanelBg, 0.92f), true, -1, true);
            DrawArc(c, Inner - 6, 0, Mathf.Tau, 48, UiTheme.PanelBorder, 1, true);
            int show = _hover >= 0 ? _hover : _pieces.FindIndex(p => p.kind == _current);
            if (show < 0) return;
            var (_, name, cost, can) = _pieces[show];
            var font = UiTheme.Text;
            void Line(string t, float y, int size, Color col)
            {
                var f = size >= 14 ? UiTheme.Semi : font;
                var sz = f.GetStringSize(t, HorizontalAlignment.Left, -1, size);
                DrawString(f, new Vector2(c.X - Mathf.Min(sz.X, Inner * 2 - 16) / 2, c.Y + y), t, HorizontalAlignment.Left, Inner * 2 - 16, size, col);
            }
            Line(name, -2, 14, UiTheme.Ink);
            Line(cost, 15, 11, can ? UiTheme.Dim : UiTheme.Bad);
            string hint = Hints.GetValueOrDefault(name, "");
            if (hint == "") return;
            float hw = UiTheme.TextWidth(hint, UiTheme.FontSmall);
            var pill = new Rect2(c.X - hw / 2 - 14, c.Y + Outer + 24, hw + 28, 30);
            UiTheme.DrawRound(this, pill, new Color(UiTheme.PanelBg, 0.8f), 15, UiTheme.PanelBorder);
            UiTheme.DrawText(this, pill.Position + new Vector2(14, 20), hint, UiTheme.FontSmall, UiTheme.Body);
        }
    }

    /// <summary>Шпаргалка «Как построить дом» (с Планом/киянкой/шкафом в руках): стеклянная панель, шаги с галочками.</summary>
    public partial class BuildGuidePanel : PanelContainer
    {
        readonly VBoxContainer _rows;
        readonly Label _title;
        string _text;

        public BuildGuidePanel()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            Theme = UiTheme.Theme;
            AddThemeStyleboxOverride("panel", UiTheme.Glass(16, 0.66f, 14));
            var box = UiTheme.VBox(7);
            AddChild(box);
            _title = UiTheme.Caption("");
            box.AddChild(_title);
            _rows = UiTheme.VBox(5);
            box.AddChild(_rows);
            Visible = false;
        }

        /// <summary>Текст с сервера: первая строка — заголовок, дальше «[x] шаг» / «[ ] шаг».</summary>
        public void SetText(string text)
        {
            text ??= "";
            Visible = text != "";
            if (text == _text) return;
            _text = text;
            foreach (var c in _rows.GetChildren()) c.QueueFree();
            var lines = text.Split('\n');
            _title.Text = lines.Length > 0 ? lines[0] : "";
            bool nextFound = false;
            for (int i = 1; i < lines.Length; i++)
            {
                string l = lines[i];
                bool done = l.StartsWith("[x]");
                string t = l.Length > 4 && l[0] == '[' ? l.Substring(4) : l;
                bool next = !done && !nextFound;
                if (next) nextFound = true;
                _rows.AddChild(new Step { Done = done, Next = next, Text = t });
            }
            ResetSize();
        }

        /// <summary>Шаг: кружок (пустой / зелёный с галочкой) и текст с переносом.</summary>
        partial class Step : HBoxContainer
        {
            public bool Done, Next;
            public string Text;
            public override void _Ready()
            {
                MouseFilter = MouseFilterEnum.Ignore;
                AddThemeConstantOverride("separation", 9);
                var mark = new Mark { Done = Done, Next = Next, CustomMinimumSize = new Vector2(16, 18), SizeFlagsVertical = SizeFlags.ShrinkBegin };
                AddChild(mark);
                var l = UiTheme.Small(Text, Done ? UiTheme.Dim : Next ? UiTheme.Ink : UiTheme.Body, UiTheme.FontSmall);
                if (Next) l.AddThemeFontOverride("font", UiTheme.Semi);
                l.CustomMinimumSize = new Vector2(300, 0);
                AddChild(l);
            }
        }

        partial class Mark : Control
        {
            public bool Done, Next;
            public override void _Draw()
            {
                var c = new Vector2(8, 10);
                if (Done)
                {
                    UiTheme.DrawDot(this, c, 7, UiTheme.Good);
                    DrawPolyline(new[] { c + new Vector2(-3.5f, 0), c + new Vector2(-1, 2.5f), c + new Vector2(3.5f, -2.5f) }, UiTheme.Dark, 1.8f, true);
                }
                else DrawArc(c, 6.5f, 0, Mathf.Tau, 24, Next ? UiTheme.Ink : new Color(1, 1, 1, 0.3f), 1.5f, true);
            }
        }
    }
}
