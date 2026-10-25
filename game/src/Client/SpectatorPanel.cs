using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>
    /// Панель наблюдателя (песочница ботов): скорость и время, за кем следим (подробно), таблица гонки по столбцам
    /// с цветами, строка режиссёра и последние вехи. Стеклянная панель слева сверху.
    /// </summary>
    public partial class SpectatorPanel : PanelContainer
    {
        public struct Row { public int Id, Num; public string Name, Goal; public bool Roamer, Dead; public int Bench, Known; public float Stash; }

        readonly Label _head, _follow;
        readonly HBoxContainer _chips;
        readonly GridContainer _table;
        readonly VBoxContainer _race, _miles;
        readonly HFlowContainer _director;
        readonly Label _milesCap;

        public SpectatorPanel()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            Theme = UiTheme.Theme;
            AddThemeStyleboxOverride("panel", UiTheme.Glass(16, 0.7f, 14));
            var box = UiTheme.VBox(8);
            AddChild(box);
            var top = UiTheme.HBox(10);
            _head = UiTheme.Title("Наблюдатель", 18);
            top.AddChild(_head);
            _chips = UiTheme.HBox(6);
            _chips.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            top.AddChild(_chips);
            box.AddChild(top);
            var keys = UiTheme.HBox(6);
            foreach (var k in new[] { "Tab — следующий бот", "F — свободная камера", "WASD/Shift — лететь", "1–6 — скорость" }) keys.AddChild(UiTheme.KeyChip(k));
            box.AddChild(keys);
            _follow = UiTheme.Small("", UiTheme.Body, UiTheme.FontCaption);
            _follow.CustomMinimumSize = new Vector2(600, 0);
            box.AddChild(_follow);
            _race = UiTheme.VBox(6);
            _race.AddChild(UiTheme.Caption("Гонка"));
            _table = new GridContainer { Columns = 7 };
            _table.AddThemeConstantOverride("h_separation", 14);
            _table.AddThemeConstantOverride("v_separation", 3);
            _race.AddChild(_table);
            box.AddChild(_race);
            box.AddChild(UiTheme.Caption("Режиссёр"));
            _director = new HFlowContainer { CustomMinimumSize = new Vector2(600, 0) };
            _director.AddThemeConstantOverride("h_separation", 6);
            _director.AddThemeConstantOverride("v_separation", 4);
            box.AddChild(_director);
            _milesCap = UiTheme.Caption("Вехи");
            box.AddChild(_milesCap);
            _miles = UiTheme.VBox(1);
            box.AddChild(_miles);
        }

        static Label Cell(string t, Color c, bool bold = false, HorizontalAlignment al = HorizontalAlignment.Left)
        {
            var l = bold ? UiTheme.Strong(t, UiTheme.FontCaption, c) : UiTheme.Small(t, c, UiTheme.FontCaption);
            l.AutowrapMode = TextServer.AutowrapMode.Off;
            l.HorizontalAlignment = al;
            return l;
        }

        static void Clear(Node n) { foreach (var c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); } }

        public void Set(string speed, string time, string followDetail, List<Row> rows, int followId,
                        string phase, List<(string name, bool open)> markers, float hot, List<string> milestones)
        {
            Clear(_chips);
            _chips.AddChild(UiTheme.KeyChip(speed));
            _chips.AddChild(UiTheme.KeyChip(time));

            _follow.Text = followDetail ?? "";
            _follow.Visible = _follow.Text != "";

            Clear(_table);
            _race.Visible = rows.Count > 0;
            foreach (var h in new[] { "#", "Бот", "Тип", "Верстак", "Изуч.", "Ящик", "Цель" }) _table.AddChild(Cell(h.ToUpper(), UiTheme.Faint, false, h is "Верстак" or "Изуч." or "Ящик" ? HorizontalAlignment.Right : HorizontalAlignment.Left));
            for (int i = 0; i < rows.Count && i < 24; i++)
            {
                var r = rows[i];
                bool me = r.Id == followId;
                var nameCol = r.Dead ? UiTheme.Bad : me ? Colors.White : i == 0 ? UiTheme.Accent : UiTheme.Ink;
                _table.AddChild(Cell((me ? "▶" : "") + r.Num, me ? Colors.White : UiTheme.Faint, me));   // постоянный номер бота (Tab, Ctrl+Shift+номер)
                var n = Cell(r.Name, nameCol, me || i == 0);
                n.CustomMinimumSize = new Vector2(140, 0); n.ClipText = true;
                _table.AddChild(n);
                _table.AddChild(Cell(r.Roamer ? "бродяга" : "фермер", r.Roamer ? UiTheme.Info : UiTheme.Good));
                _table.AddChild(Cell(r.Bench > 0 ? r.Bench.ToString() : "—", r.Bench >= 3 ? UiTheme.Accent : r.Bench > 0 ? UiTheme.Ink : UiTheme.Faint, r.Bench > 0, HorizontalAlignment.Right));
                _table.AddChild(Cell(r.Known.ToString(), UiTheme.Body, false, HorizontalAlignment.Right));
                _table.AddChild(Cell($"≈{r.Stash:0}", UiTheme.Body, false, HorizontalAlignment.Right));
                var g = Cell(r.Dead ? "мёртв" : r.Goal ?? "", r.Dead ? UiTheme.Bad : UiTheme.Dim);
                g.CustomMinimumSize = new Vector2(220, 0); g.ClipText = true;
                _table.AddChild(g);
            }

            Clear(_director);
            var ph = new PanelContainer();
            ph.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(UiTheme.Accent, 0.18f), 8, null, 0, 9, 3));
            ph.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            ph.AddChild(UiTheme.Strong("фаза: " + phase, UiTheme.FontCaption, UiTheme.Accent));
            _director.AddChild(ph);
            foreach (var (name, open) in markers)
            {
                var m = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
                m.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.ChipBg, 8, null, 0, 9, 3));
                m.AddChild(Cell(name + (open ? " · открыт" : ""), open ? UiTheme.Good : UiTheme.Body));
                _director.AddChild(m);
            }
            if (hot > 0) { var hl = Cell($"самое горячее место: жар {hot:0}", UiTheme.Bad); hl.SizeFlagsVertical = SizeFlags.ShrinkCenter; _director.AddChild(hl); }

            Clear(_miles);
            _milesCap.Visible = milestones.Count > 0;
            for (int i = System.Math.Max(0, milestones.Count - 10); i < milestones.Count; i++)
            {
                var l = UiTheme.Small(milestones[i], i == milestones.Count - 1 ? UiTheme.Ink : UiTheme.Dim, UiTheme.FontCaption);
                l.AutowrapMode = TextServer.AutowrapMode.Off;
                _miles.AddChild(l);
            }
            ResetSize();
        }
    }
}
