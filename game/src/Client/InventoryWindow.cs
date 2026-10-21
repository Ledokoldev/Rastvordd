using System;
using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Ячейка инвентаря: металлическая рамка, иконка, количество, рамка цвета редкости.
    /// Тащи мышкой в другую ячейку; Shift+клик или ПКМ — на другую сторону; Shift и провести курсором — ховер-лут.
    /// Скрытая ячейка (ещё не обыскана) — тёмная с «?», текущая обыскивается полоской.
    /// </summary>
    public partial class SlotCell : Control
    {
        public const float Size = 58;
        public string Inv;
        public int Index;
        public CellView Cell;
        public bool Searching;
        public float Progress;
        public bool Selected;          // текущий слот пояса
        public string Number;          // цифра слота пояса
        public event Action<string, int, string, int> Moved;   // (откуда, i, куда, j)
        public event Action<string, int> Quick;
        float _pop;
        bool _hover;

        public SlotCell() { CustomMinimumSize = new Vector2(Size, Size); MouseFilter = MouseFilterEnum.Stop; }

        public override void _Ready()
        {
            MouseExited += () => { _hover = false; QueueRedraw(); };
            MouseEntered += () =>
            {
                _hover = true; QueueRedraw();
                // ховер-лут: держишь Shift и ведёшь курсором — вещи перелетают
                if (Input.IsKeyPressed(Key.Shift) && Cell.Id != null) Quick?.Invoke(Inv, Index);
            };
        }

        /// <summary>Вспышка: ячейка только что раскрылась.</summary>
        public void Pop() => _pop = 1;

        public override void _Process(double delta)
        {
            if (_pop > 0) { _pop = Mathf.Max(0, _pop - (float)delta * 2.5f); QueueRedraw(); }
            else if (Searching) QueueRedraw();
        }

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, CustomMinimumSize);
            UiTheme.DrawSlot(this, r, _hover, Cell.Hidden);

            if (Cell.Hidden)
            {
                var font = UiTheme.Text;
                DrawString(font, new Vector2(Size / 2 - 7, Size / 2 + 7), "?", HorizontalAlignment.Left, -1, 20, new Color(0.6f, 0.58f, 0.5f, Searching ? 1 : 0.5f));
                if (Searching)
                {
                    // полоска обыска
                    DrawRect(new Rect2(6, Size - 11, Size - 12, 5), new Color(0, 0, 0, 0.7f));
                    DrawRect(new Rect2(6, Size - 11, (Size - 12) * Progress, 5), UiTheme.Accent);
                }
                return;
            }
            if (Cell.Id != null)
            {
                var rc = UiTheme.RarityColor(Cell.Rarity);
                if (Cell.Rarity > 0)
                {
                    // свечение редкости снизу
                    DrawRect(new Rect2(0, Size - 2, Size, 2), new Color(rc, 0.9f));
                    DrawRect(new Rect2(0, 0, Size, Size), new Color(rc, 0.08f));
                }
                var icon = Art.Icon(Cell.Id);
                if (icon != null) DrawTextureRect(icon, new Rect2(7, 5, Size - 14, Size - 14), false);
                var font = UiTheme.Text;
                string n = Cell.Magazine > 0 ? $"{Cell.Ammo}/{Cell.Magazine}" : Cell.Count > 1 ? (Cell.Count >= 10000 ? $"{Cell.Count / 1000}k" : Cell.Count.ToString()) : "";
                if (n != "")
                {
                    var sz = font.GetStringSize(n, HorizontalAlignment.Left, -1, 12);
                    DrawString(font, new Vector2(Size - 6 - sz.X + 1, Size - 6 + 1), n, HorizontalAlignment.Left, -1, 12, new Color(0, 0, 0, 0.9f));
                    DrawString(font, new Vector2(Size - 6 - sz.X, Size - 6), n, HorizontalAlignment.Left, -1, 12, UiTheme.Ink);
                }
                if (_pop > 0)
                {
                    DrawRect(r.Grow(-2), new Color(rc, 0.45f * _pop));
                    DrawRect(r.Grow(-1), new Color(rc, _pop), false, 2);
                }
            }
            else if (Cell.Label != null && Cell.Id == null)
            {
                var font = UiTheme.Text;
                var sz = font.GetStringSize(Cell.Label, HorizontalAlignment.Left, -1, 11);
                DrawString(font, new Vector2((Size - sz.X) / 2, Size / 2 + 4), Cell.Label, HorizontalAlignment.Left, -1, 11, new Color(0.5f, 0.48f, 0.42f));
            }
            if (Number != null) DrawString(UiTheme.Text, new Vector2(6, 14), Number, HorizontalAlignment.Left, -1, 12, Selected ? UiTheme.Accent : UiTheme.Dim);
            if (Selected) DrawRect(r.Grow(-1), UiTheme.Accent, false, 2);  // выбранный слот пояса
        }

        public override string _GetTooltip(Vector2 atPosition)
        {
            if (Cell.Hidden) return "Обыскивается…";
            if (Cell.Id == null) return Cell.Label ?? "";
            string extra = Cell.Magazine > 0 ? $"\nв магазине {Cell.Ammo}/{Cell.Magazine}" : Cell.Count > 1 ? $"\n×{Cell.Count}" : "";
            string desc = Rastvordd.Sim.Items.Desc(Cell.Id);
            return $"{Cell.Name}{extra}\n{UiTheme.RarityName(Cell.Rarity)}{(desc != "" ? "\n" + desc : "")}\n\nтащи мышкой · Shift+клик / ПКМ — перенести";
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            if (Cell.Id == null || Cell.Hidden) return default;
            var icon = Art.Icon(Cell.Id);
            if (icon != null) SetDragPreview(new TextureRect { Texture = icon, Size = new Vector2(48, 48), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, TextureFilter = TextureFilterEnum.Nearest, Modulate = new Color(1, 1, 1, 0.85f) });
            return $"{Inv}#{Index}";
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data) => data.VariantType == Variant.Type.String && !Cell.Hidden;

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            var s = data.AsString();
            int c = s.IndexOf('#');
            if (c > 0 && int.TryParse(s.Substring(c + 1), out int i)) Moved?.Invoke(s.Substring(0, c), i, Inv, Index);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton mb && mb.Pressed && Cell.Id != null && !Cell.Hidden &&
                ((mb.ButtonIndex == MouseButton.Left && mb.ShiftPressed) || mb.ButtonIndex == MouseButton.Right))
            {
                Quick?.Invoke(Inv, Index);
                AcceptEvent();
            }
        }
    }

    /// <summary>
    /// Окно инвентаря (Tab) и лута (E): слева персонаж с оружием в руках и места под одежду, в центре рюкзак и пояс,
    /// справа — открытый ящик/рюкзак/печь/шкаф. Всё ячейками в одном стиле.
    /// </summary>
    public partial class InventoryWindow : Control
    {
        /// <summary>(действие, аргумент) — уйдёт на сервер в следующем вводе.</summary>
        public event Action<UiOp, string> Op;

        readonly HBoxContainer _row;
        readonly PanelContainer _charPanel, _packPanel, _contPanel;
        readonly Label _packTitle, _contTitle, _contInfo, _stats;
        readonly GridContainer _packGrid, _beltGrid, _contGrid;
        readonly HBoxContainer _contButtons;
        readonly List<SlotCell> _pack = new(), _belt = new(), _cont = new();
        readonly RigView _rig;
        string _contSig, _btnSig;
        int _lastRevealed = -1;

        public InventoryWindow()
        {
            Visible = false;
            Theme = UiTheme.Theme;
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;
            var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
            dim.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(dim);

            var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
            center.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(center);
            _row = new HBoxContainer();
            _row.AddThemeConstantOverride("separation", 14);
            center.AddChild(_row);

            // ---- персонаж ----
            _charPanel = new PanelContainer();
            _row.AddChild(_charPanel);
            var cbox = new VBoxContainer();
            cbox.AddThemeConstantOverride("separation", 8);
            _charPanel.AddChild(cbox);
            cbox.AddChild(UiTheme.Header("ВЫЖИВШИЙ", out _));
            var view = new Control { CustomMinimumSize = new Vector2(230, 250), ClipContents = true };
            cbox.AddChild(view);
            var rigSet = RigSet.Load("naked");
            if (rigSet != null)
            {
                _rig = new RigView(rigSet) { Position = new Vector2(115, 228), Scale = new Vector2(3, 3) };
                view.AddChild(_rig);
            }
            // места под одежду (одежда — следующий этап)
            var gear = new GridContainer { Columns = 4 };
            gear.AddThemeConstantOverride("h_separation", 4);
            foreach (var g in new[] { "голова", "торс", "ноги", "обувь" })
                gear.AddChild(new SlotCell { Inv = "gear", Index = 0, Cell = new CellView { Label = g }, TooltipText = "Одежда — скоро" });
            cbox.AddChild(gear);
            _stats = UiTheme.Small("", UiTheme.Ink, 14);
            cbox.AddChild(_stats);

            // ---- рюкзак и пояс ----
            _packPanel = new PanelContainer();
            _row.AddChild(_packPanel);
            var pbox = new VBoxContainer();
            pbox.AddThemeConstantOverride("separation", 8);
            _packPanel.AddChild(pbox);
            pbox.AddChild(UiTheme.Header("РЮКЗАК", out _packTitle));
            _packGrid = Grid(pbox, 6);
            for (int i = 0; i < Actor.PackSize; i++) _pack.Add(AddCell(_packGrid, "pack", i));
            pbox.AddChild(UiTheme.Header("ПОЯС", out _));
            _beltGrid = Grid(pbox, 6);
            for (int i = 0; i < Actor.BeltSize; i++) { var c = AddCell(_beltGrid, "belt", i); c.Number = (i + 1).ToString(); _belt.Add(c); }
            pbox.AddChild(UiTheme.Small("Тащи мышкой · за окно — выбросить · Shift+клик или ПКМ — перенести · Shift + провести курсором — забрать всё под ним"));

            // ---- контейнер ----
            _contPanel = new PanelContainer();
            _row.AddChild(_contPanel);
            var kbox = new VBoxContainer();
            kbox.AddThemeConstantOverride("separation", 8);
            _contPanel.AddChild(kbox);
            kbox.AddChild(UiTheme.Header("", out _contTitle));
            _contInfo = UiTheme.Small("", UiTheme.Ink);
            _contInfo.CustomMinimumSize = new Vector2(360, 0);
            kbox.AddChild(_contInfo);
            var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(6 * (SlotCell.Size + 4), 5 * (SlotCell.Size + 4)), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            kbox.AddChild(scroll);
            _contGrid = new GridContainer { Columns = 6 };
            _contGrid.AddThemeConstantOverride("h_separation", 4);
            _contGrid.AddThemeConstantOverride("v_separation", 4);
            scroll.AddChild(_contGrid);
            _contButtons = new HBoxContainer();
            _contButtons.AddThemeConstantOverride("separation", 8);
            kbox.AddChild(_contButtons);
        }

        // вещь из рюкзака или пояса утащили за окно — выбросить на землю
        public override bool _CanDropData(Vector2 atPosition, Variant data) =>
            data.VariantType == Variant.Type.String && data.AsString() is var d && (d.StartsWith("pack#") || d.StartsWith("belt#"));

        public override void _DropData(Vector2 atPosition, Variant data) => Op?.Invoke(UiOp.Drop, data.AsString());

        static GridContainer Grid(Control parent, int cols)
        {
            var g = new GridContainer { Columns = cols };
            g.AddThemeConstantOverride("h_separation", 4);
            g.AddThemeConstantOverride("v_separation", 4);
            parent.AddChild(g);
            return g;
        }

        SlotCell AddCell(Control parent, string inv, int i)
        {
            var c = new SlotCell { Inv = inv, Index = i, TooltipText = " " };
            c.Moved += (s, si, d, di) => Op?.Invoke(UiOp.Move, $"{s}#{si}>{d}#{di}");
            c.Quick += (s, si) => Op?.Invoke(UiOp.Quick, $"{s}#{si}");
            parent.AddChild(c);
            return c;
        }

        void Button(string text, UiOp op)
        {
            var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38) };
            b.Pressed += () => Op?.Invoke(op, null);
            _contButtons.AddChild(b);
        }

        /// <summary>Обновить по снапшоту. ui == null — только свой инвентарь (Tab).</summary>
        public void Refresh(SelfInfo self, UiView ui, float dt)
        {
            int used = 0;
            for (int i = 0; i < _pack.Count && i < self.Pack.Count; i++)
            {
                if (self.Pack[i].Id != null) used++;
                if (!Same(_pack[i].Cell, self.Pack[i])) { _pack[i].Cell = self.Pack[i]; _pack[i].QueueRedraw(); }
            }
            _packTitle.Text = $"РЮКЗАК {used}/{Actor.PackSize}";
            for (int i = 0; i < _belt.Count && i < self.BeltCells.Count; i++)
            {
                bool sel = i == self.Current;
                if (!Same(_belt[i].Cell, self.BeltCells[i]) || _belt[i].Selected != sel) { _belt[i].Cell = self.BeltCells[i]; _belt[i].Selected = sel; _belt[i].QueueRedraw(); }
            }
            _stats.Text = $"Здоровье {self.Hp:0}   Еда {self.Food:0}   Вода {self.Water:0}";

            // живой персонаж с текущим предметом в руках
            if (_rig != null)
            {
                var s = _rig.S;
                s.Aim = 0.75f;
                s.Held = self.Belt[self.Current]?.Id;
                s.Breath += dt * 1.6f;
                _rig.Refresh(false);
            }

            _contPanel.Visible = ui != null;
            if (ui == null) { _contSig = null; _lastRevealed = -1; return; }
            _contTitle.Text = ui.Title;
            _contInfo.Text = ui.Info ?? (ui.Searching ? "Обыскиваешь…" : "");
            _contInfo.Visible = _contInfo.Text != "";
            string inv = ui.Kind switch { UiKind.Furnace => "fur", UiKind.Claim => "claim", _ => "cont" };
            _contGrid.Columns = ui.Kind is UiKind.Furnace or UiKind.Claim ? 3 : 6;
            string sig = inv + ui.Cells.Count;
            if (sig != _contSig)
            {
                _contSig = sig;
                foreach (var c in _cont) c.QueueFree();
                _cont.Clear();
                for (int i = 0; i < ui.Cells.Count; i++) _cont.Add(AddCell(_contGrid, inv, i));
                _lastRevealed = -1;
            }
            int revealed = 0;
            for (int i = 0; i < ui.Cells.Count; i++)
            {
                var cv = ui.Cells[i];
                if (!cv.Hidden && cv.Id != null) revealed = i + 1;
                var cell = _cont[i];
                bool wasHidden = cell.Cell.Hidden;
                bool searching = cv.Hidden && ui.Searching && (i == 0 || !ui.Cells[i - 1].Hidden);
                if (!Same(cell.Cell, cv) || cell.Searching != searching || (searching && Math.Abs(cell.Progress - ui.Progress) > 0.001f))
                {
                    cell.Cell = cv; cell.Searching = searching; cell.Progress = ui.Progress;
                    if (wasHidden && !cv.Hidden && _lastRevealed >= 0) cell.Pop();
                    cell.QueueRedraw();
                }
            }
            _lastRevealed = revealed;

            string bsig = $"{ui.Kind}{ui.On}";
            if (bsig != _btnSig)
            {
                _btnSig = bsig;
                foreach (var b in _contButtons.GetChildren()) b.QueueFree();
                switch (ui.Kind)
                {
                    case UiKind.Furnace:
                        Button(ui.On ? "ПОТУШИТЬ" : "ЗАЖЕЧЬ", UiOp.Toggle);
                        Button("ЗАБРАТЬ ГОТОВОЕ", UiOp.TakeOut);
                        break;
                    case UiKind.Claim:
                        Button("ЗАБРАТЬ ВСЁ", UiOp.TakeAll);
                        break;
                    case UiKind.Recycler:
                        Button(ui.On ? "ОСТАНОВИТЬ" : "ЗАПУСТИТЬ", UiOp.Toggle);
                        Button("ЗАБРАТЬ ВСЁ", UiOp.TakeAll);
                        break;
                    default:
                        Button("ЗАБРАТЬ ВСЁ", UiOp.TakeAll);
                        if (ui.Kind == UiKind.Box) Button("СЛОЖИТЬ ВСЁ", UiOp.PutAll);
                        break;
                }
            }
        }

        static bool Same(CellView a, CellView b) => a.Id == b.Id && a.Count == b.Count && a.Hidden == b.Hidden && a.Ammo == b.Ammo && a.Label == b.Label;
    }
}
