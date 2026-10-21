using System;
using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>Ячейка рецепта: иконка, сколько штук за раз; тусклая — не хватает ресурсов или верстака.</summary>
    public partial class RecipeCell : Control
    {
        public const float Size = 64;
        public RecipeInfo R;
        public bool Selected, Blocked;
        public event Action<RecipeCell> Clicked, DoubleClicked;
        bool _hover;

        public RecipeCell() { CustomMinimumSize = new Vector2(Size, Size); MouseFilter = MouseFilterEnum.Stop; }

        public override void _Ready()
        {
            MouseEntered += () => { _hover = true; QueueRedraw(); };
            MouseExited += () => { _hover = false; QueueRedraw(); };
        }

        public override void _Draw()
        {
            var r = new Rect2(0, 0, Size, Size);
            UiTheme.DrawSlot(this, r, _hover);
            bool ok = R.CanCraft && !Blocked;
            var icon = Art.Icon(R.Output);
            if (icon != null) DrawTextureRect(icon, r.Grow(-9), false, ok ? Colors.White : new Color(1, 1, 1, 0.35f));
            var font = UiTheme.Text;
            if (R.Count > 1)
            {
                string n = $"×{R.Count}";
                var sz = font.GetStringSize(n, HorizontalAlignment.Left, -1, 12);
                DrawString(font, new Vector2(Size - 5 - sz.X, Size - 5), n, HorizontalAlignment.Left, -1, 12, UiTheme.Ink);
            }
            if (R.NeedsBench) DrawString(font, new Vector2(5, 14), "В", HorizontalAlignment.Left, -1, 11, Blocked ? new Color(0.9f, 0.42f, 0.38f) : UiTheme.Dim);
            if (ok) DrawRect(new Rect2(0, Size - 2, Size, 2), new Color(0.45f, 0.8f, 0.4f, 0.8f));
            if (Selected) DrawRect(r.Grow(-1), UiTheme.Accent, false, 2);
        }

        public override string _GetTooltip(Vector2 atPosition) => $"{R.Name}\n{R.CostText}";

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.DoubleClick) DoubleClicked?.Invoke(this); else Clicked?.Invoke(this);
                AcceptEvent();
            }
        }
    }

    /// <summary>
    /// Окно крафта (Q, или E у верстака): вкладки, сетка рецептов квадратами, карточка выбранного рецепта
    /// с ингредиентами «есть/нужно», количеством и кнопкой, очередь с прогрессом.
    /// Двойной клик по рецепту — скрафтить одну штуку.
    /// </summary>
    public partial class CraftWindow : Control
    {
        public event Action<int, int> Craft;    // (индекс рецепта, сколько раз)
        static readonly string[] Tabs = { "Всё", "Инструменты", "Оружие", "Стройка", "Рейд", "Разное" };

        static int TabOf(string id) => id switch
        {
            _ when id.StartsWith("tool_") || id == "build_plan" => 1,
            _ when id.StartsWith("weapon_") || id.StartsWith("ammo_") || id == "arrow" => 2,
            _ when id.StartsWith("deploy_") || id.StartsWith("lock_") => 3,
            _ when id.StartsWith("expl_") => 4,
            _ => 5,
        };

        readonly HBoxContainer _tabs;
        readonly GridContainer _grid;
        readonly List<RecipeCell> _cells = new();
        readonly TextureRect _bigIcon;
        readonly Label _name, _desc, _time, _bench, _amountLabel;
        readonly VBoxContainer _costs;
        readonly Button _craftBtn;
        readonly HBoxContainer _queue;
        readonly Label _queueTitle;
        List<RecipeInfo> _recipes = new();
        bool _nearBench;
        int _tab, _selected = -1, _amount = 1;
        string _gridSig, _costSig, _queueSig;

        public CraftWindow()
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
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 14);
            center.AddChild(row);

            // ---- рецепты ----
            var left = new PanelContainer();
            row.AddChild(left);
            var lbox = new VBoxContainer();
            lbox.AddThemeConstantOverride("separation", 8);
            left.AddChild(lbox);
            lbox.AddChild(UiTheme.Header("КРАФТ", out _));
            _tabs = new HBoxContainer();
            _tabs.AddThemeConstantOverride("separation", 4);
            lbox.AddChild(_tabs);
            for (int i = 0; i < Tabs.Length; i++)
            {
                int t = i;
                var b = new Button { Text = Tabs[i], FocusMode = FocusModeEnum.None, ToggleMode = true, ButtonPressed = i == 0 };
                b.AddThemeFontSizeOverride("font_size", 12);
                b.Pressed += () => { _tab = t; for (int k = 0; k < _tabs.GetChildCount(); k++) ((Button)_tabs.GetChild(k)).ButtonPressed = k == t; _gridSig = null; };
                _tabs.AddChild(b);
            }
            var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(6 * (RecipeCell.Size + 4), 5 * (RecipeCell.Size + 4)), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            lbox.AddChild(scroll);
            _grid = new GridContainer { Columns = 6 };
            _grid.AddThemeConstantOverride("h_separation", 4);
            _grid.AddThemeConstantOverride("v_separation", 4);
            scroll.AddChild(_grid);
            lbox.AddChild(UiTheme.Small("Клик — выбрать · двойной клик — скрафтить · «В» — нужен верстак рядом · Q — закрыть", null, 12));

            // ---- карточка рецепта ----
            var right = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
            row.AddChild(right);
            var rbox = new VBoxContainer();
            rbox.AddThemeConstantOverride("separation", 8);
            right.AddChild(rbox);
            var top = new HBoxContainer();
            top.AddThemeConstantOverride("separation", 10);
            rbox.AddChild(top);
            _bigIcon = new TextureRect { CustomMinimumSize = new Vector2(72, 72), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.Nearest };
            top.AddChild(_bigIcon);
            var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            top.AddChild(titles);
            _name = new Label();
            _name.AddThemeFontSizeOverride("font_size", 16);
            _name.AddThemeColorOverride("font_color", UiTheme.Accent);
            titles.AddChild(_name);
            _desc = UiTheme.Small("", UiTheme.Dim, 12);
            _desc.CustomMinimumSize = new Vector2(200, 0);
            titles.AddChild(_desc);
            rbox.AddChild(UiTheme.Header("НУЖНО", out _));
            _costs = new VBoxContainer();
            _costs.AddThemeConstantOverride("separation", 4);
            rbox.AddChild(_costs);
            _time = UiTheme.Small("", UiTheme.Ink, 13);
            rbox.AddChild(_time);
            _bench = UiTheme.Small("", UiTheme.Ink, 13);
            rbox.AddChild(_bench);

            var amount = new HBoxContainer();
            amount.AddThemeConstantOverride("separation", 4);
            rbox.AddChild(amount);
            Button Small(string t, Action a)
            {
                var b = new Button { Text = t, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(36, 32) };
                b.Pressed += a;
                amount.AddChild(b);
                return b;
            }
            Small("−", () => _amount = Math.Max(1, _amount - 1));
            _amountLabel = new Label { CustomMinimumSize = new Vector2(44, 0), HorizontalAlignment = HorizontalAlignment.Center };
            _amountLabel.AddThemeFontSizeOverride("font_size", 15);
            amount.AddChild(_amountLabel);
            Small("+", () => _amount = Math.Min(99, _amount + 1));
            Small("МАКС", () => _amount = Math.Max(1, MaxTimes()));
            _craftBtn = new Button { Text = "СКРАФТИТЬ", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40) };
            _craftBtn.AddThemeFontSizeOverride("font_size", 14);
            _craftBtn.Pressed += () => { if (_selected >= 0) Craft?.Invoke(_recipes[_selected].Index, _amount); _amount = 1; };
            rbox.AddChild(_craftBtn);

            rbox.AddChild(UiTheme.Header("ОЧЕРЕДЬ", out _queueTitle));
            _queue = new HBoxContainer();
            _queue.AddThemeConstantOverride("separation", 4);
            _queue.CustomMinimumSize = new Vector2(0, 44);
            rbox.AddChild(_queue);
        }

        bool Blocked(RecipeInfo r) => r.NeedsBench && !_nearBench;

        /// <summary>Сколько раз можно скрафтить выбранное прямо сейчас.</summary>
        int MaxTimes()
        {
            if (_selected < 0) return 0;
            var r = _recipes[_selected];
            if (Blocked(r) || r.Costs == null) return 0;
            int m = 99;
            foreach (var (_, n, have) in r.Costs) m = Math.Min(m, n > 0 ? have / n : 99);
            return m;
        }

        public void Refresh(SelfInfo s)
        {
            _recipes = s.Recipes;
            _nearBench = s.NearBench;
            if (_selected >= _recipes.Count) _selected = -1;

            // сетка: пересобираем при смене вкладки или списка
            string sig = _tab + "|" + _recipes.Count;
            if (sig != _gridSig)
            {
                _gridSig = sig;
                foreach (var c in _cells) c.QueueFree();
                _cells.Clear();
                for (int i = 0; i < _recipes.Count; i++)
                {
                    if (_tab != 0 && TabOf(_recipes[i].Output) != _tab) continue;
                    int idx = i;
                    var c = new RecipeCell { R = _recipes[i] };
                    c.Clicked += _ => { _selected = idx; _amount = 1; };
                    c.DoubleClicked += _ => { _selected = idx; Craft?.Invoke(_recipes[idx].Index, 1); };
                    _grid.AddChild(c);
                    _cells.Add(c);
                }
                if (_selected < 0 || (_tab != 0 && TabOf(_recipes[_selected].Output) != _tab))
                    _selected = _cells.Count > 0 ? _recipes.FindIndex(r => r.Index == _cells[0].R.Index) : -1;
            }
            foreach (var c in _cells)
            {
                int i = _recipes.FindIndex(r => r.Index == c.R.Index);
                c.R = _recipes[i];
                c.Blocked = Blocked(c.R);
                c.Selected = i == _selected;
                c.QueueRedraw();
            }

            // карточка
            if (_selected >= 0)
            {
                var r = _recipes[_selected];
                _bigIcon.Texture = Art.Icon(r.Output);
                _name.Text = r.Name;
                _desc.Text = Items.Desc(r.Output);
                string csig = _selected + "|" + _amount + "|" + string.Join(";", r.Costs.ConvertAll(c => c.id + c.have));
                if (csig != _costSig)
                {
                    _costSig = csig;
                    foreach (var c in _costs.GetChildren()) c.QueueFree();
                    foreach (var (id, n, have) in r.Costs)
                    {
                        var line = new HBoxContainer();
                        line.AddThemeConstantOverride("separation", 8);
                        line.AddChild(new TextureRect { Texture = Art.Icon(id), CustomMinimumSize = new Vector2(28, 28), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.Nearest });
                        var nm = UiTheme.Small(Items.Name(id), UiTheme.Ink, 13);
                        nm.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                        nm.VerticalAlignment = VerticalAlignment.Center;
                        line.AddChild(nm);
                        int need = n * _amount;
                        var cnt = UiTheme.Small($"{have} / {need}", have >= need ? new Color(0.55f, 0.85f, 0.5f) : new Color(0.9f, 0.42f, 0.38f), 13);
                        cnt.VerticalAlignment = VerticalAlignment.Center;
                        cnt.AutowrapMode = TextServer.AutowrapMode.Off;
                        line.AddChild(cnt);
                        _costs.AddChild(line);
                    }
                }
                _time.Text = $"Время: {r.Time * _amount:0} с" + (r.Count > 1 ? $" · получишь {r.Count * _amount} шт." : "");
                _bench.Text = !r.NeedsBench ? "Можно в руках" : _nearBench ? "Верстак рядом ✓" : "Нужен верстак рядом";
                _bench.AddThemeColorOverride("font_color", Blocked(r) ? new Color(0.9f, 0.42f, 0.38f) : UiTheme.Dim);
                _amountLabel.Text = $"×{_amount}";
                _craftBtn.Disabled = MaxTimes() < _amount;
                _craftBtn.Text = _amount > 1 ? $"СКРАФТИТЬ ×{_amount}" : "СКРАФТИТЬ";
            }

            // очередь
            string qsig = string.Join(";", s.CraftQueue.ConvertAll(q => q.id));
            if (qsig != _queueSig)
            {
                _queueSig = qsig;
                foreach (var c in _queue.GetChildren()) c.QueueFree();
                foreach (var (id, name, _) in s.CraftQueue)
                    _queue.AddChild(new QueueCell { Id = id, TooltipText = name });
                _queueTitle.Text = s.CraftQueue.Count > 0 ? $"ОЧЕРЕДЬ · {s.CraftQueue.Count}" : "ОЧЕРЕДЬ ПУСТА";
            }
            if (s.CraftQueue.Count > 0 && _queue.GetChildCount() > 0 && _queue.GetChild(0) is QueueCell first)
            {
                first.Progress = s.CraftQueue[0].progress;
                first.QueueRedraw();
            }
        }
    }

    /// <summary>Ячейка очереди крафта: иконка и полоска прогресса у первой.</summary>
    public partial class QueueCell : Control
    {
        public string Id;
        public float Progress = -1;
        public QueueCell() { CustomMinimumSize = new Vector2(44, 44); MouseFilter = MouseFilterEnum.Stop; }

        public override void _Draw()
        {
            var r = new Rect2(0, 0, 44, 44);
            UiTheme.DrawSlot(this, r);
            var icon = Art.Icon(Id);
            if (icon != null) DrawTextureRect(icon, r.Grow(-6), false, Progress >= 0 ? Colors.White : new Color(1, 1, 1, 0.5f));
            if (Progress >= 0)
            {
                DrawRect(new Rect2(3, 38, 38, 4), new Color(0, 0, 0, 0.6f));
                DrawRect(new Rect2(3, 38, 38 * Progress, 4), UiTheme.Accent);
            }
        }
    }
}
