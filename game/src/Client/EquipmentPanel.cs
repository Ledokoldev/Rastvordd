using System;
using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Колонка «Снаряжение» в инвентаре (D47, docs/design/14): живой риг в текущей одежде, три группы слотов —
    /// Голова (2), Корпус (4), Ноги (3) — и Запястье (1); под бронёй — две мини-ячейки пластин с полоской прочности.
    /// Внизу — итог: защита по зонам, «радиация до N», «холод до T °C», штраф скорости.
    /// Ячейки — обычные SlotCell (inv "wear"/"plate"): тащи из рюкзака в слот, ПКМ по вещи в рюкзаке — надеть, ПКМ по слоту — снять.
    /// </summary>
    public partial class EquipmentPanel : PanelContainer
    {
        public const float Cell = 50, MiniCell = 34, Gap = 6;
        public const float Width = 4 * Cell + 3 * Gap + 2 * UiTheme.PanelPad;
        static readonly string[] Short = { "шлем", "лицо", "кофта", "броня", "руки", "перч.", "штаны", "колени", "обувь", "часы" };

        public readonly SlotCell[] Wear = new SlotCell[Clothing.Slots];
        public readonly SlotCell[] Plates = new SlotCell[Clothing.PlateSlots];
        readonly ThinBar[] _plateBars = new ThinBar[Clothing.PlateSlots];
        readonly Control _plateRow;
        readonly Label _head, _torso, _limbs, _rad, _cold, _speed;
        readonly RigPreview _preview;

        public EquipmentPanel(Action<SlotCell> wire)
        {
            AddThemeStyleboxOverride("panel", UiTheme.Glass());
            CustomMinimumSize = new Vector2(Width, 0);
            var g = UiTheme.VBox(6);
            AddChild(g);
            g.AddChild(UiTheme.Caption("Снаряжение"));
            _preview = new RigPreview { CustomMinimumSize = new Vector2(0, 124) };
            g.AddChild(_preview);

            SlotCell Make(string inv, int i, float side)
            {
                var c = new SlotCell { Inv = inv, Index = i, Side = side, Cell = new CellView { Label = inv == "wear" ? Short[i] : i == 0 ? "перед" : "спина" } };
                wire(c);
                return c;
            }
            HBoxContainer Row(params int[] slots)
            {
                var r = UiTheme.HBox((int)Gap);
                foreach (var i in slots) r.AddChild(Wear[i] = Make("wear", i, Cell));
                return r;
            }
            Control Group(string name, Control row)
            {
                var b = UiTheme.VBox(3);
                b.AddChild(UiTheme.Small(name, UiTheme.Faint, UiTheme.FontCaption));
                b.AddChild(row);
                return b;
            }
            g.AddChild(Group("Голова", Row(0, 1)));
            g.AddChild(Group("Корпус", Row(2, 3, 4, 5)));
            // пластины — под ячейкой брони (второй в ряду корпуса)
            var pr = UiTheme.HBox((int)Gap);
            pr.AddChild(new Control { CustomMinimumSize = new Vector2(Cell + Gap - 2, 0), MouseFilter = MouseFilterEnum.Ignore });
            for (int i = 0; i < Clothing.PlateSlots; i++)
            {
                var col = UiTheme.VBox(2);
                col.AddChild(Plates[i] = Make("plate", i, MiniCell));
                _plateBars[i] = new ThinBar { CustomMinimumSize = new Vector2(MiniCell, 3) };
                col.AddChild(_plateBars[i]);
                pr.AddChild(col);
            }
            _plateRow = pr;
            g.AddChild(pr);
            g.AddChild(Group("Ноги", Row(6, 7, 8)));
            g.AddChild(Group("Запястье", Row(9)));

            g.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 4) });
            g.AddChild(UiTheme.Sep());
            Label Line(string name)
            {
                var row = UiTheme.HBox();
                var l = UiTheme.Small(name, UiTheme.Dim, UiTheme.FontCaption);
                l.AutowrapMode = TextServer.AutowrapMode.Off;
                l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                row.AddChild(l);
                var v = UiTheme.Strong("", UiTheme.FontCaption);
                row.AddChild(v);
                g.AddChild(row);
                return v;
            }
            _head = Line("Голова");
            _torso = Line("Корпус спереди / сзади");
            _limbs = Line("Конечности");
            _rad = Line("Радиация");
            _cold = Line("Холод");
            _speed = Line("Скорость");
        }

        public void Refresh(SelfInfo s)
        {
            if (s.Worn.Count < Clothing.Slots) return;
            var ids = new string[Clothing.Slots];
            for (int i = 0; i < Clothing.Slots; i++)
            {
                var cv = s.Worn[i];
                if (cv.Id == null) cv.Label = Short[i];
                ids[i] = cv.Id;
                if (!InventoryWindow.Same(Wear[i].Cell, cv)) { Wear[i].Cell = cv; Wear[i].QueueRedraw(); }
            }
            _plateRow.Visible = s.PlateSlots > 0;
            for (int i = 0; i < Clothing.PlateSlots && i < s.PlateCells.Count; i++)
            {
                var cv = s.PlateCells[i];
                bool on = i < s.PlateSlots;
                Plates[i].Visible = on;
                _plateBars[i].Visible = on;
                if (cv.Id == null) cv.Label = i == 0 ? "перед" : "спина";
                if (!InventoryWindow.Same(Plates[i].Cell, cv) || !Mathf.IsEqualApprox(Plates[i].Cell.Durability, cv.Durability)) { Plates[i].Cell = cv; Plates[i].QueueRedraw(); }
                float d = cv.Id != null ? Mathf.Max(0, cv.Durability) : 0;
                if (!Mathf.IsEqualApprox(_plateBars[i].Value, d)) { _plateBars[i].Value = d; _plateBars[i].Fill = UiTheme.DurabilityColor(d); _plateBars[i].QueueRedraw(); }
            }
            static string P(float v) => $"{Mathf.RoundToInt(v * 100)}%";
            _head.Text = P(s.ProtHead);
            _torso.Text = $"{P(s.ProtTorsoF)} / {P(s.ProtTorsoB)}";
            _limbs.Text = P(s.ProtLimbs);
            _rad.Text = $"до {s.RadProt:0}";
            float t = Clothing.Comfort - s.Warmth;
            _cold.Text = $"до {(t > 0 ? "+" : t < 0 ? "−" : "")}{Mathf.Abs(t):0} °C";
            _speed.Text = s.SpeedPen < 0 ? $"−{-s.SpeedPen * 100:0}%" : "без штрафа";
            _speed.AddThemeColorOverride("font_color", s.SpeedPen < 0 ? UiTheme.Accent : UiTheme.Ink);
            _preview.SetWorn(ids);
        }
    }

    /// <summary>Риг крупно (живой персонаж в одежде): дышит, смотрит чуть вправо к камере. Пиксели чёткие (nearest).</summary>
    public partial class RigPreview : Control
    {
        readonly RigView _rig;
        readonly Node2D _holder = new();
        string _key = "?";
        float _t;

        public RigPreview()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            ClipContents = true;
            AddChild(_holder);
            _rig = new RigView(RigSet.Load("naked")) { Scale = new Vector2(2.5f, 2.5f) };   // × BodyScale 0,8 = ровно 2 пикселя экрана на пиксель арта
            if (RigSet.Load("naked") != null) _holder.AddChild(_rig);
        }

        public void SetWorn(string[] ids)
        {
            string key = string.Join(",", ids);
            if (key == _key) return;
            _key = key;
            _rig.SetSet(WearLook.Dressed("naked", ids));
        }

        public override void _Process(double delta)
        {
            if (!IsVisibleInTree()) return;
            _t += (float)delta;
            _holder.Position = new Vector2(Mathf.Round(Size.X / 2), Mathf.Round(Size.Y - 4));
            var s = _rig.S;
            s.Aim = 0.55f;               // анфас, чуть вправо
            s.Speed = 0; s.Gait = 0;
            s.Breath = _t * 2.2f;
            _rig.Refresh(false);
        }
    }
}

namespace Rastvordd.Client
{
    /// <summary>Карточка вещи (D47): четыре цифры — пули / радиация / тепло / скорость; у пластины — + к корпусу и прочность.</summary>
    public partial class WearStatsView : HBoxContainer
    {
        readonly (Label v, Label n)[] _cols = new (Label, Label)[4];

        public WearStatsView()
        {
            AddThemeConstantOverride("separation", 6);
            MouseFilter = MouseFilterEnum.Ignore;
            for (int i = 0; i < 4; i++)
            {
                var p = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                p.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.RowBg, UiTheme.RowRadius, null, 0, 6, 6));
                var b = UiTheme.VBox(0);
                p.AddChild(b);
                var v = UiTheme.Strong("", 16);
                v.HorizontalAlignment = HorizontalAlignment.Center;
                var n = UiTheme.Small("", UiTheme.Dim, UiTheme.FontCaption);
                n.HorizontalAlignment = HorizontalAlignment.Center;
                n.AutowrapMode = TextServer.AutowrapMode.Off;
                b.AddChild(v); b.AddChild(n);
                AddChild(p);
                _cols[i] = (v, n);
            }
            Visible = false;
        }

        public void Set(string id, float durability = -1)
        {
            var w = Clothing.Def(id);
            var pl = Clothing.Plate(id);
            Visible = (w != null && w.Slot != WearSlot.Wrist) || pl != null;
            if (!Visible) return;
            void C(int i, string v, string n, Color? c = null)
            {
                _cols[i].v.Text = v; _cols[i].n.Text = n;
                _cols[i].v.AddThemeColorOverride("font_color", c ?? UiTheme.Ink);
                _cols[i].v.GetParent().GetParent<Control>().Visible = v != null;
            }
            if (pl != null)
            {
                C(0, $"+{pl.Bonus * 100:0}%", "корпус");
                C(1, $"{pl.Durability:0}", "прочность");
                C(2, durability >= 0 ? $"{durability * 100:0}%" : null, "состояние", durability >= 0 ? UiTheme.DurabilityColor(durability) : null);
                C(3, null, "");
                return;
            }
            string zone = Clothing.SlotZone[(int)w.Slot] switch { HitZone.Head => "пули · голова", HitZone.Torso => "пули · корпус", _ => "пули · конечн." };
            C(0, $"{w.Bullet * 100:0}%", zone);
            C(1, $"{w.Rad:0}", "радиация", w.Rad > 0 ? UiTheme.Good : UiTheme.Faint);
            C(2, $"{(w.Warm > 0 ? "+" : "")}{w.Warm:0} °C", "тепло", w.Warm > 0 ? UiTheme.Accent : UiTheme.Faint);
            C(3, w.Speed < 0 ? $"−{-w.Speed * 100:0}%" : "0%", "скорость", w.Speed < 0 ? UiTheme.Bad : UiTheme.Faint);
        }

        /// <summary>Одна строка для мини-карточки.</summary>
        public static string Line(string id)
        {
            if (Clothing.Plate(id) is PlateDef p) return $"Пластина: +{p.Bonus * 100:0}% к корпусу, держит {p.Durability:0} урона";
            if (Clothing.Def(id) is not WearDef w || w.Slot == WearSlot.Wrist) return null;
            return $"Пули {w.Bullet * 100:0}% · радиация {w.Rad:0} · тепло {(w.Warm > 0 ? "+" : "")}{w.Warm:0} °C" + (w.Speed < 0 ? $" · скорость −{-w.Speed * 100:0}%" : "") + (w.Plates > 0 ? $" · пластин {w.Plates}" : "");
        }
    }
}
