using System;
using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>Ячейка рецепта: иконка, сколько штук за раз; тусклая — не хватает ресурсов или верстака; зелёная точка — можно скрафтить.</summary>
    public partial class RecipeCell : Control
    {
        public const float Size = 76;
        public RecipeInfo R;
        public bool Selected, Blocked;
        public event Action<RecipeCell> Clicked, DoubleClicked;
        bool _hover;

        public RecipeCell() { CustomMinimumSize = new Vector2(Size, Size); MouseFilter = MouseFilterEnum.Stop; TextureFilter = TextureFilterEnum.Linear; }

        public override void _Ready()
        {
            MouseEntered += () => { _hover = true; QueueRedraw(); };
            MouseExited += () => { _hover = false; QueueRedraw(); };
        }

        public override void _Draw()
        {
            var r = new Rect2(0, 0, Size, Size);
            bool ok = R.CanCraft && !Blocked;
            UiTheme.DrawSlot(this, r, _hover, !ok && !Selected, Selected);
            float isz = Size * UiTheme.IconFill;
            UiTheme.DrawIcon(this, R.Output, new Rect2((Size - isz) / 2, (Size - isz) / 2, isz, isz),
                ok ? Colors.White : !R.Known ? new Color(1, 1, 1, 0.2f) : new Color(1, 1, 1, 0.45f));
            if (!R.Known) TechWindow.DrawLock(this, new Vector2(Size - 18, 9), UiTheme.Dim);
            else if (ok) UiTheme.DrawDot(this, new Vector2(Size - 12.5f, 12.5f), 3.5f, UiTheme.Good);
            if (R.Count > 1)
            {
                string n = $"×{R.Count}";
                float w = UiTheme.TextWidth(n, 12, UiTheme.Semi);
                UiTheme.DrawText(this, new Vector2(Size - 8 - w, Size - 8), n, 12, UiTheme.Ink, UiTheme.Semi);
            }
            if (R.NeedsBench) UiTheme.DrawText(this, new Vector2(9, 18), $"В{R.Bench}", 11, Blocked ? UiTheme.Bad : UiTheme.Dim, UiTheme.Semi);
        }

        public override string _GetTooltip(Vector2 atPosition) => $"{R.Name}\n{R.CostText}{(R.Known ? "" : "\nне изучено — дерево изучения у верстака")}";

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
    /// Окно крафта (Q, или E у верстака): фильтры-«таблетки», сетка рецептов, справа карточка выбранного рецепта
    /// (картинка, описание, ингредиенты «есть/нужно», количество, большая белая кнопка) и очередь с прогрессом.
    /// Двойной клик по рецепту — скрафтить одну штуку.
    /// </summary>
    public partial class CraftWindow : Control
    {
        public event Action<int, int> Craft;    // (индекс рецепта, сколько раз)
        public event Action OpenTree;
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
        readonly BigIcon _bigIcon;
        readonly Label _name, _type, _desc, _time, _bench, _amountLabel;
        readonly VBoxContainer _costs, _stats;
        readonly Button _craftBtn;
        readonly HBoxContainer _queue;
        readonly Label _queueTitle;
        List<RecipeInfo> _recipes = new();
        int _benchLevel;
        int _tab, _selected = -1, _amount = 1;
        string _gridSig, _costSig, _queueSig, _statSig;

        public CraftWindow()
        {
            Visible = false;
            Theme = UiTheme.Theme;
            TextureFilter = TextureFilterEnum.Linear;
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;
            var dim = new ColorRect { Color = UiTheme.Backdrop, MouseFilter = MouseFilterEnum.Ignore };
            dim.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(dim);
            var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
            center.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(center);
            var row = UiTheme.HBox(UiTheme.PanelGap);
            center.AddChild(row);

            // ---- рецепты ----
            var left = UiTheme.GlassPanel();
            row.AddChild(left);
            var lbox = UiTheme.VBox(14);
            left.AddChild(lbox);
            var head = UiTheme.HBox(12);
            lbox.AddChild(head);
            var h = UiTheme.Title("Крафт");
            h.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            head.AddChild(h);
            var tree = UiTheme.Btn("Дерево изучения", false, 0, 34);
            tree.Pressed += () => OpenTree?.Invoke();
            head.AddChild(tree);
            _tabs = UiTheme.HBox(8);
            lbox.AddChild(_tabs);
            for (int i = 0; i < Tabs.Length; i++)
            {
                int t = i;
                var b = UiTheme.Chip(Tabs[i], i == 0);
                b.Pressed += () => { _tab = t; for (int k = 0; k < _tabs.GetChildCount(); k++) ((Button)_tabs.GetChild(k)).ButtonPressed = k == t; _gridSig = null; };
                _tabs.AddChild(b);
            }
            var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(7 * (RecipeCell.Size + UiTheme.CellGap), 5 * (RecipeCell.Size + UiTheme.CellGap)), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            lbox.AddChild(scroll);
            _grid = new GridContainer { Columns = 7 };
            _grid.AddThemeConstantOverride("h_separation", (int)UiTheme.CellGap);
            _grid.AddThemeConstantOverride("v_separation", (int)UiTheme.CellGap);
            scroll.AddChild(_grid);
            var keys = UiTheme.HBox(6);
            foreach (var k in new[] { "Клик — выбрать", "Двойной клик — скрафтить", "В1–В3 — нужен верстак", "Q — закрыть" }) keys.AddChild(UiTheme.KeyChip(k));
            lbox.AddChild(keys);

            // ---- карточка рецепта ----
            var right = UiTheme.GlassPanel();
            right.CustomMinimumSize = new Vector2(ItemCard.W, 0);
            row.AddChild(right);
            var rbox = UiTheme.VBox(0);
            right.AddChild(rbox);
            _bigIcon = new BigIcon { CustomMinimumSize = new Vector2(0, 110) };
            rbox.AddChild(_bigIcon);
            rbox.AddChild(Gap(12));
            _name = UiTheme.Title("", 22);
            rbox.AddChild(_name);
            _type = UiTheme.Small("", UiTheme.Dim, UiTheme.FontSmall);
            rbox.AddChild(_type);
            rbox.AddChild(Gap(6));
            _desc = UiTheme.Small("", UiTheme.Body, UiTheme.FontBody);
            _desc.CustomMinimumSize = new Vector2(ItemCard.W - 2 * UiTheme.PanelPad, 0);
            rbox.AddChild(_desc);
            _stats = UiTheme.VBox(2);
            rbox.AddChild(_stats);
            rbox.AddChild(Gap(12));
            rbox.AddChild(UiTheme.Caption("Нужно"));
            rbox.AddChild(Gap(6));
            _costs = UiTheme.VBox(6);
            rbox.AddChild(_costs);
            rbox.AddChild(Gap(8));
            _time = UiTheme.Small("", UiTheme.Dim, UiTheme.FontSmall);
            rbox.AddChild(_time);
            _bench = UiTheme.Small("", UiTheme.Dim, UiTheme.FontSmall);
            rbox.AddChild(_bench);
            rbox.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 12) });

            var amount = UiTheme.HBox(6);
            rbox.AddChild(amount);
            Button Small(string t, Action a, float w = 38)
            {
                var b = UiTheme.Btn(t, false, w, 36);
                b.Pressed += a;
                amount.AddChild(b);
                return b;
            }
            Small("−", () => _amount = Math.Max(1, _amount - 1));
            _amountLabel = UiTheme.Strong("", 16);
            _amountLabel.CustomMinimumSize = new Vector2(48, 0);
            _amountLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _amountLabel.VerticalAlignment = VerticalAlignment.Center;
            amount.AddChild(_amountLabel);
            Small("+", () => _amount = Math.Min(99, _amount + 1));
            Small("Макс", () => _amount = Math.Max(1, MaxTimes()), 0).SizeFlagsHorizontal = SizeFlags.ExpandFill;
            rbox.AddChild(Gap(8));
            _craftBtn = UiTheme.Btn("Скрафтить", true, 0, 44);
            _craftBtn.AddThemeFontSizeOverride("font_size", UiTheme.FontBody);
            _craftBtn.Pressed += () => { if (_selected >= 0) Craft?.Invoke(_recipes[_selected].Index, _amount); _amount = 1; };
            rbox.AddChild(_craftBtn);

            rbox.AddChild(Gap(14));
            _queueTitle = UiTheme.Caption("Очередь");
            rbox.AddChild(_queueTitle);
            rbox.AddChild(Gap(6));
            _queue = UiTheme.HBox(6);
            _queue.CustomMinimumSize = new Vector2(0, 44);
            rbox.AddChild(_queue);
        }

        static Control Gap(float h) => new Control { CustomMinimumSize = new Vector2(0, h), MouseFilter = MouseFilterEnum.Ignore };

        bool Blocked(RecipeInfo r) => !r.Known || r.Bench > _benchLevel;

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
            _benchLevel = s.BenchLevel;
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
                int rar = Items.Rarity(r.Output);
                if (_bigIcon.Id != r.Output) { _bigIcon.Id = r.Output; _bigIcon.Rarity = rar; _bigIcon.QueueRedraw(); }
                _name.Text = r.Name;
                _type.Text = $"{UiTheme.RarityName(rar)} · {UiTheme.CategoryName(r.Output)}";
                _type.AddThemeColorOverride("font_color", rar > 0 ? UiTheme.RarityColor(rar) : UiTheme.Dim);
                _desc.Text = Items.Desc(r.Output);
                if (_statSig != r.Output)
                {
                    _statSig = r.Output;
                    foreach (var c in _stats.GetChildren()) c.QueueFree();
                    var st = Items.Stats(r.Output);
                    if (st.Count > 0) _stats.AddChild(Gap(8));
                    foreach (var (label, value) in st) _stats.AddChild(TechWindow.Line(label, value, UiTheme.Ink));
                }
                string csig = _selected + "|" + _amount + "|" + string.Join(";", r.Costs.ConvertAll(c => c.id + c.have));
                if (csig != _costSig)
                {
                    _costSig = csig;
                    foreach (var c in _costs.GetChildren()) c.QueueFree();
                    foreach (var (id, n, have) in r.Costs)
                    {
                        var p = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
                        p.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.RowBg, UiTheme.RowRadius, null, 0, 8, 5));
                        var line = UiTheme.HBox(10);
                        p.AddChild(line);
                        line.AddChild(UiTheme.IconBox(id, 32));
                        var nm = UiTheme.Small(Items.Name(id), UiTheme.Ink, UiTheme.FontSmall);
                        nm.AutowrapMode = TextServer.AutowrapMode.Off;
                        nm.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                        nm.VerticalAlignment = VerticalAlignment.Center;
                        line.AddChild(nm);
                        int need = n * _amount;
                        var cnt = UiTheme.Strong($"{have} / {need}", UiTheme.FontSmall, have >= need ? UiTheme.Good : UiTheme.Bad);
                        cnt.VerticalAlignment = VerticalAlignment.Center;
                        line.AddChild(cnt);
                        _costs.AddChild(p);
                    }
                }
                _time.Text = $"Время: {r.Time * _amount:0} с" + (r.Count > 1 ? $" · получишь {r.Count * _amount} шт." : "");
                _bench.Text = !r.Known ? $"Не изучено — изучи в дереве у верстака {Math.Max(1, Tech.Get(r.Output)?.Bench ?? 1)}"
                    : !r.NeedsBench ? "Можно в руках" : _benchLevel >= r.Bench ? $"Верстак {r.Bench} рядом ✓" : $"Нужен верстак {r.Bench} рядом";
                _bench.AddThemeColorOverride("font_color", Blocked(r) ? UiTheme.Bad : UiTheme.Dim);
                _amountLabel.Text = $"×{_amount}";
                _craftBtn.Disabled = MaxTimes() < _amount;
                _craftBtn.Text = _amount > 1 ? $"Скрафтить ×{_amount}" : "Скрафтить";
            }

            // очередь
            string qsig = string.Join(";", s.CraftQueue.ConvertAll(q => q.id));
            if (qsig != _queueSig)
            {
                _queueSig = qsig;
                foreach (var c in _queue.GetChildren()) c.QueueFree();
                foreach (var (id, name, _) in s.CraftQueue)
                    _queue.AddChild(new QueueCell { Id = id, TooltipText = name });
                if (s.CraftQueue.Count == 0) { var e = UiTheme.Small("пусто — двойной клик по рецепту", UiTheme.Faint, UiTheme.FontSmall); e.AutowrapMode = TextServer.AutowrapMode.Off; _queue.AddChild(e); }
                _queueTitle.Text = s.CraftQueue.Count > 0 ? $"Очередь · {s.CraftQueue.Count}" : "Очередь";
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
        public QueueCell() { CustomMinimumSize = new Vector2(44, 44); MouseFilter = MouseFilterEnum.Stop; TextureFilter = TextureFilterEnum.Linear; }

        public override void _Draw()
        {
            var r = new Rect2(0, 0, 44, 44);
            UiTheme.DrawSlot(this, r);
            UiTheme.DrawIcon(this, Id, r.Grow(-7), Progress >= 0 ? Colors.White : new Color(1, 1, 1, 0.5f));
            if (Progress >= 0) UiTheme.DrawBar(this, new Rect2(6, 36, 32, 4), Progress, UiTheme.Accent);
        }
    }
}
