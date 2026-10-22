using System;
using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

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
    /// Радиальное колесо как в Расте (План и киянка): пока зажата ПКМ — кольцо секторов вокруг центра экрана,
    /// ведёшь мышь к сектору, отпускаешь ПКМ — пункт выбран. Мышь у центра или Esc — закрыть без выбора.
    /// В центре — название, пояснение и цена наведённого пункта (что есть / чего не хватает).
    /// </summary>
    public partial class BuildMenu : Control
    {
        public event Action<int> Picked;
        public event Action<string> Rejected;   // выбран недоступный пункт — почему
        public string Title = "";               // подпись над колесом (киянка: что улучшаем)
        const float Outer = 196, Inner = 92, IconR = 144, IconSize = 54, OpenTime = 0.13f;
        List<WheelItem> _items = new();
        int _hover = -1;
        float _open;                             // 0..1 — анимация появления
        float[] _hl = new float[12];             // плавная подсветка секторов
        Vector2? _debugMouse;                    // отладка: курсор «наведён» без настоящей мыши (окно скриншота за краем экрана)

        public BuildMenu()
        {
            Visible = false;
            MouseFilter = MouseFilterEnum.Ignore;
            TextureFilter = TextureFilterEnum.Linear;
            SetAnchorsPreset(LayoutPreset.FullRect);
        }

        Vector2 Center => GetViewportRect().Size / 2;
        public int Count => _items.Count;

        public void Fill(List<WheelItem> items)
        {
            _items = items ?? new List<WheelItem>();
            if (_hl.Length < _items.Count) Array.Resize(ref _hl, _items.Count);
            if (Visible) QueueRedraw();
        }

        /// <summary>ПКМ нажата: показать колесо, курсор — в центр.</summary>
        public void Open()
        {
            Visible = true;
            _hover = -1;
            _open = 0;
            _debugMouse = null;
            Array.Clear(_hl);
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetViewport().WarpMouse(Center);
            QueueRedraw();
        }

        /// <summary>Закрыть без выбора (Esc, пропала цель).</summary>
        public void Close()
        {
            if (!Visible) return;
            Visible = false;
            Input.MouseMode = Input.MouseModeEnum.Hidden;
        }

        /// <summary>ПКМ отпущена: выбрать сектор под курсором и закрыть.</summary>
        public void Release()
        {
            if (!Visible) return;
            Close();
            if (_hover < 0 || _hover >= _items.Count) return;
            var it = _items[_hover];
            if (it.Enabled) Picked?.Invoke(it.Id);
            else Rejected?.Invoke($"{it.Name}: {it.Why}");
        }

        /// <summary>Цифры 1–6 выбирают пункт, пока колесо открыто.</summary>
        public bool PickByKey(Key key)
        {
            int i = (int)(key - Key.Key1);
            if (!Visible || i < 0 || i >= _items.Count) return false;
            _hover = i;
            Release();
            return true;
        }

        /// <summary>Отладка: навести курсор на сектор i.</summary>
        public void PointAt(int i)
        {
            int n = Math.Max(1, _items.Count);
            _debugMouse = Center + Vector2.FromAngle(SectorAngle(i, n)) * IconR;
        }

        static float SectorAngle(int k, int n) => -Mathf.Pi / 2 + k * Mathf.Tau / n;   // первый сектор — сверху, дальше по часовой

        public override void _Process(double delta)
        {
            if (!Visible) return;
            float dt = (float)delta;
            _open = Mathf.Min(1, _open + dt / OpenTime);
            int n = Math.Max(1, _items.Count);
            var d = (_debugMouse ?? GetViewport().GetMousePosition()) - Center;
            int h = -1;
            if (d.Length() > Inner * 0.55f && _items.Count > 0)
            {
                float a = Mathf.PosMod(d.Angle() + Mathf.Pi / 2 + Mathf.Pi / n, Mathf.Tau);
                h = (int)(a / (Mathf.Tau / n)) % n;
            }
            _hover = h;
            for (int k = 0; k < _items.Count; k++) _hl[k] = Mathf.MoveToward(_hl[k], k == h ? 1 : 0, dt * 9f);
            QueueRedraw();
        }

        static Vector2[] Ring(Vector2 c, float r0, float r1, float a0, float a1)
        {
            int steps = Math.Max(6, (int)((a1 - a0) * 24));
            var pts = new Vector2[(steps + 1) * 2];
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)steps);
                pts[i] = c + Vector2.FromAngle(a) * r1;
                pts[pts.Length - 1 - i] = c + Vector2.FromAngle(a) * r0;
            }
            return pts;
        }

        void Outline(Vector2[] seg, Color col, float w)
        {
            var o = new Vector2[seg.Length + 1];
            seg.CopyTo(o, 0); o[seg.Length] = seg[0];
            DrawPolyline(o, col, w, true);
        }

        void DrawItemIcon(string icon, Rect2 r, Color mod)
        {
            if (string.IsNullOrEmpty(icon)) return;
            if (icon.StartsWith("item:")) { UiTheme.DrawIcon(this, icon.Substring(5), r, mod); return; }
            if (icon == "@demolish")
            {
                // снос: кирпичная кладка с трещиной и обломками
                var c = r.GetCenter(); float s = r.Size.X / 2;
                var brick = new Color(0.72f, 0.66f, 0.6f) * mod;
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 3 - (row == 0 ? 1 : 0); col++)
                    {
                        float off = row % 2 == 0 ? s * 0.33f : 0;
                        var b = new Rect2(c.X - s * 0.95f + off + col * s * 0.66f, c.Y - s * 0.55f + row * s * 0.38f, s * 0.6f, s * 0.32f);
                        if (row == 0 && col == 1) b.Position += new Vector2(s * 0.12f, -s * 0.18f);   // вылетевший кирпич
                        DrawRect(b, brick);
                    }
                var crack = UiTheme.Bad * mod;
                DrawPolyline(new[] { c + new Vector2(-s * 0.1f, -s * 0.7f), c + new Vector2(s * 0.12f, -s * 0.2f), c + new Vector2(-s * 0.15f, s * 0.15f), c + new Vector2(s * 0.1f, s * 0.6f) }, crack, 3f, true);
                for (int i = 0; i < 4; i++) DrawCircle(c + new Vector2(-s * 0.7f + i * s * 0.45f, s * 0.82f), s * (0.07f + 0.03f * (i % 2)), brick, true, -1, true);
                return;
            }
            var tex = Art.Get(icon);
            if (tex != null) DrawTextureRect(tex, r, false, mod);
        }

        public override void _Draw()
        {
            int n = _items.Count;
            if (n == 0) return;
            float e = 1 - Mathf.Pow(1 - _open, 3);
            DrawSetTransform(Center, 0, Vector2.One * (0.86f + 0.14f * e));
            Modulate = new Color(1, 1, 1, e);
            var c = Vector2.Zero;
            float step = Mathf.Tau / n, gap = 0.018f;
            for (int k = 0; k < n; k++)
            {
                var it = _items[k];
                float mid = SectorAngle(k, n), h = _hl[k];
                float r1 = Outer + 8 * h;
                var seg = Ring(c, Inner, r1, mid - step / 2 + gap, mid + step / 2 - gap);
                var bg = new Color(UiTheme.PanelBg, 0.74f);   // полупрозрачно: подсвеченную цель видно сквозь кольцо
                var hov = it.Enabled ? new Color(0.30f, 0.30f, 0.32f, 0.92f) : new Color(0.32f, 0.17f, 0.16f, 0.9f);
                DrawColoredPolygon(seg, bg.Lerp(hov, h));
                Outline(seg, UiTheme.PanelBorder.Lerp(it.Enabled ? new Color(1, 1, 1, 0.75f) : new Color(UiTheme.Bad, 0.8f), h), 1 + h);
                if (it.Current) DrawColoredPolygon(Ring(c, r1 - 4, r1, mid - step / 2 + gap + 0.03f, mid + step / 2 - gap - 0.03f), UiTheme.Accent);
                var ip = c + Vector2.FromAngle(mid) * (IconR + 4 * h);
                float s = IconSize + 10 * h;
                var mod = it.Enabled ? Colors.White : new Color(0.55f, 0.52f, 0.52f, 0.85f);
                DrawItemIcon(it.Icon, new Rect2(ip - new Vector2(s, s) / 2, new Vector2(s, s)), mod);
                // номер для клавиши — у внешнего края сектора
                var np = c + Vector2.FromAngle(mid) * (r1 - 19) + new Vector2(-4, 5);
                UiTheme.DrawText(this, np, (k + 1).ToString(), UiTheme.FontCaption, new Color(1, 1, 1, 0.35f + 0.4f * h));
            }

            // центр: что под курсором (или текущее)
            float cr = Inner - 7;
            DrawCircle(c, cr, new Color(UiTheme.PanelBg, 0.9f), true, -1, true);
            DrawArc(c, cr, 0, Mathf.Tau, 64, UiTheme.PanelBorder, 1, true);
            if (Title != "")
            {
                float tw = UiTheme.TextWidth(Title, UiTheme.FontSmall, UiTheme.Semi);
                var pill = new Rect2(-tw / 2 - 14, -Outer - 52, tw + 28, 30);
                UiTheme.DrawRound(this, pill, new Color(UiTheme.PanelBg, 0.85f), 15, UiTheme.PanelBorder);
                UiTheme.DrawText(this, pill.Position + new Vector2(14, 20), Title, UiTheme.FontSmall, UiTheme.Ink, UiTheme.Semi);
            }
            int show = _hover >= 0 && _hover < n ? _hover : _items.FindIndex(p => p.Current);
            if (show < 0)
            {
                UiTheme.DrawText(this, new Vector2(-cr, 5), "наведи и отпусти ПКМ", UiTheme.FontCaption, UiTheme.Dim, null, HorizontalAlignment.Center, cr * 2);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                return;
            }
            var sel = _items[show];
            float w = cr * 2 - 18;
            int rows = sel.Costs?.Count ?? 0;
            var hint = string.IsNullOrEmpty(sel.Why) ? sel.Hint ?? "" : sel.Why;
            bool twoLines = UiTheme.TextWidth(hint, 11) > w;
            float y = -10 - rows * 10 - (twoLines ? 7 : 0);
            UiTheme.DrawText(this, new Vector2(-w / 2, y), sel.Name, 16, UiTheme.Ink, UiTheme.Semi, HorizontalAlignment.Center, w);
            y += 17;
            DrawMultilineString(UiTheme.Text, new Vector2(-w / 2, y), hint, HorizontalAlignment.Center, w, 11, 2, string.IsNullOrEmpty(sel.Why) ? UiTheme.Body : UiTheme.Bad);
            y += twoLines ? 22 : 9;
            for (int i = 0; i < rows; i++)
            {
                var (id, need, have) = sel.Costs[i];
                bool ok = sel.Refund || have >= need;
                string txt = sel.Refund ? $"+{need}" : $"{need}";
                string sub = sel.Refund ? "" : $" / {have}";
                float tw = UiTheme.TextWidth(txt, 13, UiTheme.Semi) + UiTheme.TextWidth(sub, 11);
                float x0 = -(tw + 22) / 2;
                UiTheme.DrawIcon(this, id, new Rect2(x0, y, 18, 18));
                UiTheme.DrawText(this, new Vector2(x0 + 22, y + 14), txt, 13, ok ? (sel.Refund ? UiTheme.Good : UiTheme.Ink) : UiTheme.Bad, UiTheme.Semi);
                UiTheme.DrawText(this, new Vector2(x0 + 22 + UiTheme.TextWidth(txt, 13, UiTheme.Semi), y + 14), sub, 11, ok ? UiTheme.Dim : UiTheme.Bad);
                y += 20;
            }
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
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
            foreach (var c in _rows.GetChildren()) { _rows.RemoveChild(c); c.QueueFree(); }   // сразу из дерева: иначе ResetSize считает и старые строки
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
