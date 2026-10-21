using System;
using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>Узел дерева: иконка; изучен — зелёная полоска, доступен — жёлтая рамка, закрыт — тусклый с замком.</summary>
    public partial class TechCell : Control
    {
        public const float Side = 64;
        public TechInfo T;
        public bool Selected;
        public event Action<TechCell> Clicked;
        bool _hover;

        public TechCell() { CustomMinimumSize = new Vector2(Side, Side); Size = new Vector2(Side, Side); MouseFilter = MouseFilterEnum.Stop; }

        public override void _Ready()
        {
            MouseEntered += () => { _hover = true; QueueRedraw(); };
            MouseExited += () => { _hover = false; QueueRedraw(); };
        }

        public override void _Draw()
        {
            var r = new Rect2(0, 0, Side, Side);
            UiTheme.DrawSlot(this, r, _hover);
            var icon = Art.Icon(T.Id);
            bool open = T.Known || T.CanResearch;
            if (icon != null) DrawTextureRect(icon, r.Grow(-9), false, T.Known ? Colors.White : open ? new Color(1, 1, 1, 0.8f) : new Color(1, 1, 1, 0.3f));
            if (T.Known) DrawRect(new Rect2(0, Side - 3, Side, 3), new Color(0.45f, 0.8f, 0.4f));
            else
            {
                TechWindow.DrawLock(this, new Vector2(Side - 14, 6), T.CanResearch ? UiTheme.Accent : UiTheme.Dim);
                if (T.CanResearch) DrawRect(r.Grow(-1), new Color(UiTheme.Accent, 0.6f), false, 1);
            }
            if (Selected) DrawRect(r.Grow(-1), UiTheme.Accent, false, 2);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left) { Clicked?.Invoke(this); AcceptEvent(); }
        }
    }

    /// <summary>
    /// Дерево изучения (E у верстака): три столбца — верстак 1, 2, 3; узлы квадратами, линии от предков к потомкам;
    /// справа карточка: что это, характеристики, рецепт, цена в металлоломе, что нужно, кнопка «Изучить».
    /// Внизу — что известно с самого начала.
    /// </summary>
    public partial class TechWindow : Control
    {
        public event Action<string> Research;
        public event Action OpenCraft;

        const float ColW = 190, RowH = 92, Top = 64;
        // раскладка: (столбец = верстак − 1, строка) — потомки на строке предка
        static readonly Dictionary<string, (int col, int row)> Layout = new()
        {
            ["gunpowder"] = (0, 0), ["ammo_pistol"] = (0, 1), ["weapon_pipe"] = (0, 2), ["lock_code"] = (0, 3), ["expl_fire"] = (0, 4),
            ["expl_satchel"] = (1, 0), ["ammo_rifle"] = (1, 1),
            ["expl_c4"] = (2, 0), ["weapon_ak"] = (2, 1),
        };
        static readonly string[] Starter = { "tool_hatchet", "tool_pickaxe", "weapon_spear", "weapon_bow", "arrow", "weapon_torch", "bandage", "build_plan", "tool_hammer", "deploy_claim", "deploy_box", "deploy_sleepbag", "lock_key", "deploy_furnace", "deploy_workbench" };

        readonly Control _tree;
        readonly Dictionary<string, TechCell> _cells = new();
        readonly Label _scrap, _bench, _name, _desc, _cost, _why;
        readonly TextureRect _icon;
        readonly VBoxContainer _stats, _reqs;
        readonly Button _learn;
        List<TechInfo> _tech = new();
        List<RecipeInfo> _recipes = new();
        int _benchLevel;
        string _selected = "gunpowder", _cardSig;

        public static void DrawLock(CanvasItem c, Vector2 at, Color col)
        {
            c.DrawArc(at + new Vector2(4, 4), 3, Mathf.Pi, Mathf.Tau, 8, col, 1.5f);
            c.DrawRect(new Rect2(at + new Vector2(0, 4), new Vector2(8, 6)), col);
        }

        public TechWindow()
        {
            Visible = false;
            Theme = UiTheme.Theme;
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;
            var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
            dim.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(dim);
            var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
            center.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(center);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 14);
            center.AddChild(row);

            // ---- дерево ----
            var left = new PanelContainer();
            row.AddChild(left);
            var lbox = new VBoxContainer();
            lbox.AddThemeConstantOverride("separation", 8);
            left.AddChild(lbox);
            var head = new HBoxContainer();
            head.AddThemeConstantOverride("separation", 16);
            lbox.AddChild(head);
            var h = UiTheme.Header("ДЕРЕВО ИЗУЧЕНИЯ", out _);
            h.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            head.AddChild(h);
            _scrap = UiTheme.Small("", UiTheme.Ink, 13);
            _scrap.AutowrapMode = TextServer.AutowrapMode.Off;
            head.AddChild(_scrap);
            var craft = new Button { Text = "КРАФТ (Q)", FocusMode = FocusModeEnum.None };
            craft.Pressed += () => OpenCraft?.Invoke();
            head.AddChild(craft);
            _bench = UiTheme.Small("", UiTheme.Dim, 12);
            lbox.AddChild(_bench);

            _tree = new Control { CustomMinimumSize = new Vector2(3 * ColW, Top + 5 * RowH), MouseFilter = MouseFilterEnum.Pass };
            _tree.Draw += DrawTree;
            lbox.AddChild(_tree);
            foreach (var (id, (col, r)) in Layout)
            {
                var c = new TechCell { Position = new Vector2(col * ColW + 20, Top + r * RowH) };
                c.Clicked += cell => { _selected = cell.T.Id; _cardSig = null; };
                _tree.AddChild(c);
                _cells[id] = c;
            }

            lbox.AddChild(UiTheme.Header("ИЗВЕСТНО С НАЧАЛА", out _));
            var starter = new HBoxContainer();
            starter.AddThemeConstantOverride("separation", 2);
            lbox.AddChild(starter);
            foreach (var id in Starter)
                starter.AddChild(new TextureRect { Texture = Art.Icon(id), CustomMinimumSize = new Vector2(34, 34), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.Nearest, TooltipText = Items.Name(id) });

            // ---- карточка ----
            var right = new PanelContainer { CustomMinimumSize = new Vector2(320, 0) };
            row.AddChild(right);
            var rbox = new VBoxContainer();
            rbox.AddThemeConstantOverride("separation", 8);
            right.AddChild(rbox);
            var top = new HBoxContainer();
            top.AddThemeConstantOverride("separation", 10);
            rbox.AddChild(top);
            _icon = new TextureRect { CustomMinimumSize = new Vector2(72, 72), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.Nearest };
            top.AddChild(_icon);
            var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            top.AddChild(titles);
            _name = new Label();
            _name.AddThemeFontSizeOverride("font_size", 16);
            _name.AddThemeColorOverride("font_color", UiTheme.Accent);
            titles.AddChild(_name);
            _desc = UiTheme.Small("", UiTheme.Dim, 12);
            _desc.CustomMinimumSize = new Vector2(220, 0);
            titles.AddChild(_desc);
            rbox.AddChild(UiTheme.Header("ХАРАКТЕРИСТИКИ", out _));
            _stats = new VBoxContainer();
            _stats.AddThemeConstantOverride("separation", 2);
            rbox.AddChild(_stats);
            rbox.AddChild(UiTheme.Header("ЧТОБЫ ИЗУЧИТЬ", out _));
            _reqs = new VBoxContainer();
            _reqs.AddThemeConstantOverride("separation", 2);
            rbox.AddChild(_reqs);
            _cost = UiTheme.Small("", UiTheme.Ink, 13);
            rbox.AddChild(_cost);
            _learn = new Button { Text = "ИЗУЧИТЬ", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40) };
            _learn.AddThemeFontSizeOverride("font_size", 14);
            _learn.Pressed += () => Research?.Invoke(_selected);
            rbox.AddChild(_learn);
            _why = UiTheme.Small("", new Color(0.9f, 0.42f, 0.38f), 12);
            rbox.AddChild(_why);
        }

        // линии от предков к потомкам и подписи столбцов
        void DrawTree()
        {
            var font = UiTheme.Text;
            for (int col = 0; col < 3; col++)
            {
                string title = $"ВЕРСТАК {col + 1}";
                bool here = _benchLevel >= col + 1;
                var icon = Art.Icon(col == 0 ? "deploy_workbench" : $"deploy_workbench{col + 1}");
                if (icon != null) _tree.DrawTextureRect(icon, new Rect2(col * ColW + 20, 4, 36, 36), false, here ? Colors.White : new Color(1, 1, 1, 0.45f));
                _tree.DrawString(font, new Vector2(col * ColW + 62, 20), title, HorizontalAlignment.Left, -1, 13, here ? UiTheme.Accent : UiTheme.Ink);
                _tree.DrawString(font, new Vector2(col * ColW + 62, 36), here ? "рядом" : col == 0 ? "крафт: дерево, металл, лом" : $"крафт у верстака {col}", HorizontalAlignment.Left, -1, 11, UiTheme.Dim);
            }
            foreach (var t in _tech)
            {
                if (!_cells.TryGetValue(t.Id, out var child)) continue;
                foreach (var req in t.Requires)
                {
                    if (!_cells.TryGetValue(req, out var parent)) continue;
                    bool done = parent.T.Known;
                    var col = done ? new Color(0.45f, 0.8f, 0.4f, 0.8f) : new Color(1, 1, 1, 0.2f);
                    Vector2 a, b;
                    if (Mathf.IsEqualApprox(parent.Position.X, child.Position.X))
                    {
                        a = parent.Position + new Vector2(TechCell.Side / 2, TechCell.Side);
                        b = child.Position + new Vector2(TechCell.Side / 2, 0);
                        _tree.DrawLine(a, b, col, 2);
                    }
                    else
                    {
                        a = parent.Position + new Vector2(TechCell.Side, TechCell.Side / 2);
                        b = child.Position + new Vector2(0, TechCell.Side / 2);
                        float mx = b.X - 18;   // поворот у самого потомка — линия не идёт через чужие узлы
                        _tree.DrawPolyline(new[] { a, new Vector2(mx, a.Y), new Vector2(mx, b.Y), b }, col, 2);
                    }
                }
            }
            // подписи под узлами
            foreach (var c in _cells.Values)
            {
                string n = c.T.Name ?? "";
                var sz = font.GetStringSize(n, HorizontalAlignment.Left, -1, 11);
                _tree.DrawString(font, c.Position + new Vector2(TechCell.Side / 2 - sz.X / 2, TechCell.Side + 13), n, HorizontalAlignment.Left, -1, 11, c.T.Known ? UiTheme.Ink : UiTheme.Dim);
            }
        }

        public void Refresh(SelfInfo s)
        {
            _tech = s.Tech;
            _recipes = s.Recipes;
            _benchLevel = s.BenchLevel;
            _scrap.Text = $"Металлолом: {s.Scrap}";
            _bench.Text = s.BenchLevel > 0 ? $"Рядом верстак {s.BenchLevel} — можно изучать узлы уровня до {s.BenchLevel}" : "Верстака рядом нет — смотреть можно, изучать — только у верстака";
            foreach (var t in _tech)
                if (_cells.TryGetValue(t.Id, out var c)) { c.T = t; c.Selected = t.Id == _selected; c.QueueRedraw(); }
            _tree.QueueRedraw();

            var sel = _tech.Find(t => t.Id == _selected);
            if (sel.Id == null) return;
            _icon.Texture = Art.Icon(sel.Id);
            _name.Text = sel.Name;
            _desc.Text = Items.Desc(sel.Id);
            string sig = sel.Id + sel.Known + sel.CanResearch + s.Scrap + s.BenchLevel;
            if (sig != _cardSig)
            {
                _cardSig = sig;
                foreach (var c in _stats.GetChildren()) c.QueueFree();
                foreach (var (label, value) in Items.Stats(sel.Id)) _stats.AddChild(Line(label, value, UiTheme.Ink));
                var rec = _recipes.Find(r => r.Output == sel.Id);
                if (rec.Name != null) _stats.AddChild(Line("Рецепт", rec.CostText + (rec.Count > 1 ? $" → ×{rec.Count}" : ""), UiTheme.Dim));
                if (_stats.GetChildCount() == 0) _stats.AddChild(UiTheme.Small("—", UiTheme.Dim, 12));

                foreach (var c in _reqs.GetChildren()) c.QueueFree();
                var green = new Color(0.55f, 0.85f, 0.5f); var red = new Color(0.9f, 0.42f, 0.38f);
                _reqs.AddChild(Line($"Верстак {sel.Bench}", s.BenchLevel >= sel.Bench ? "рядом ✓" : "нет рядом", s.BenchLevel >= sel.Bench ? green : red));
                foreach (var r in sel.Requires)
                {
                    bool k = _tech.Exists(t => t.Id == r && t.Known);
                    _reqs.AddChild(Line(Items.Name(r), k ? "изучено ✓" : "не изучено", k ? green : red));
                }
                _reqs.AddChild(Line("Металлолом", $"{s.Scrap} / {sel.Scrap}", s.Scrap >= sel.Scrap ? green : red));
            }
            _cost.Text = sel.Known ? "Изучено — рецепт есть в крафте (Q)" : $"Цена: {sel.Scrap} металлолома";
            _learn.Disabled = !sel.CanResearch;
            _learn.Text = sel.Known ? "ИЗУЧЕНО" : $"ИЗУЧИТЬ — {sel.Scrap} ЛОМА";
            _why.Text = sel.Known || sel.CanResearch ? "" : sel.Why;
        }

        static Control Line(string label, string value, Color col)
        {
            var l = new HBoxContainer();
            var a = UiTheme.Small(label, UiTheme.Dim, 13);
            a.AutowrapMode = TextServer.AutowrapMode.Off;
            a.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            l.AddChild(a);
            var b = UiTheme.Small(value, col, 13);
            b.AutowrapMode = TextServer.AutowrapMode.Off;
            l.AddChild(b);
            return l;
        }
    }
}
