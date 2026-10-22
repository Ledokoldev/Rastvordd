using System;
using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>Узел дерева: иконка; изучен — зелёная точка, доступен — янтарная рамка и замок, закрыт — тусклый с замком.</summary>
    public partial class TechCell : Control
    {
        public const float Side = 68;
        public TechInfo T;
        public bool Selected;
        public event Action<TechCell> Clicked;
        bool _hover;

        public TechCell() { CustomMinimumSize = new Vector2(Side, Side); Size = new Vector2(Side, Side); MouseFilter = MouseFilterEnum.Stop; TextureFilter = TextureFilterEnum.Linear; }

        public override void _Ready()
        {
            MouseEntered += () => { _hover = true; QueueRedraw(); };
            MouseExited += () => { _hover = false; QueueRedraw(); };
        }

        public override void _Draw()
        {
            var r = new Rect2(0, 0, Side, Side);
            bool open = T.Known || T.CanResearch;
            UiTheme.DrawSlot(this, r, _hover, !open, Selected);
            if (T.CanResearch && !T.Known && !Selected) UiTheme.DrawRound(this, r, Colors.Transparent, UiTheme.CellRadius, new Color(UiTheme.Accent, 0.7f), 1);
            float isz = Side * UiTheme.IconFill;
            UiTheme.DrawIcon(this, T.Id, new Rect2((Side - isz) / 2, (Side - isz) / 2, isz, isz), T.Known ? Colors.White : open ? new Color(1, 1, 1, 0.8f) : new Color(1, 1, 1, 0.3f));
            if (T.Known) UiTheme.DrawDot(this, new Vector2(Side - 12.5f, 12.5f), 3.5f, UiTheme.Good);
            else TechWindow.DrawLock(this, new Vector2(Side - 18, 8), T.CanResearch ? UiTheme.Accent : UiTheme.Dim);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left) { Clicked?.Invoke(this); AcceptEvent(); }
        }
    }

    /// <summary>
    /// Дерево изучения (E у верстака): три столбца — верстак 1, 2, 3; узлы скруглёнными ячейками, линии от предков к потомкам;
    /// справа карточка: картинка, что это, характеристики, что нужно, белая кнопка «Изучить». Внизу — что известно с начала.
    /// </summary>
    public partial class TechWindow : Control
    {
        public event Action<string> Research;
        public event Action OpenCraft;

        const float ColW = 200, RowH = 96, Top = 64;
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
        readonly BigIcon _icon;
        readonly VBoxContainer _stats, _reqs;
        readonly Button _learn;
        List<TechInfo> _tech = new();
        List<RecipeInfo> _recipes = new();
        int _benchLevel;
        string _selected = "gunpowder", _cardSig;

        public static void DrawLock(CanvasItem c, Vector2 at, Color col)
        {
            c.DrawArc(at + new Vector2(5, 4.5f), 3.2f, Mathf.Pi, Mathf.Tau, 10, col, 1.5f, true);
            UiTheme.DrawRound(c, new Rect2(at + new Vector2(0, 4.5f), new Vector2(10, 7.5f)), col, 2);
        }

        public TechWindow()
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

            // ---- дерево ----
            var left = UiTheme.GlassPanel();
            row.AddChild(left);
            var lbox = UiTheme.VBox(10);
            left.AddChild(lbox);
            var head = UiTheme.HBox(12);
            lbox.AddChild(head);
            var h = UiTheme.Title("Дерево изучения");
            h.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            head.AddChild(h);
            var scrapChip = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = "Металлолом" };
            scrapChip.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.RowBg, UiTheme.RowRadius, null, 0, 10, 4));
            var sc = UiTheme.HBox(6);
            scrapChip.AddChild(sc);
            sc.AddChild(UiTheme.IconBox("scrap", 26, 0));
            _scrap = UiTheme.Strong("", UiTheme.FontBody);
            _scrap.VerticalAlignment = VerticalAlignment.Center;
            sc.AddChild(_scrap);
            head.AddChild(scrapChip);
            var craft = UiTheme.Btn("Крафт (Q)", false, 0, 34);
            craft.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            craft.Pressed += () => OpenCraft?.Invoke();
            head.AddChild(craft);
            _bench = UiTheme.Small("", UiTheme.Dim, UiTheme.FontSmall);
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

            lbox.AddChild(UiTheme.Caption("Известно с начала"));
            var starter = UiTheme.HBox(4);
            lbox.AddChild(starter);
            foreach (var id in Starter)
            {
                var ic = (IconView)UiTheme.IconBox(id, 36);
                ic.TooltipText = Items.Name(id);
                ic.MouseFilter = MouseFilterEnum.Stop;
                starter.AddChild(ic);
            }

            // ---- карточка ----
            var right = UiTheme.GlassPanel();
            right.CustomMinimumSize = new Vector2(ItemCard.W, 0);
            row.AddChild(right);
            var rbox = UiTheme.VBox(8);
            right.AddChild(rbox);
            _icon = new BigIcon { CustomMinimumSize = new Vector2(0, 120) };
            rbox.AddChild(_icon);
            _name = UiTheme.Title("", 22);
            rbox.AddChild(_name);
            _desc = UiTheme.Small("", UiTheme.Body, UiTheme.FontBody);
            _desc.CustomMinimumSize = new Vector2(ItemCard.W - 2 * UiTheme.PanelPad, 0);
            rbox.AddChild(_desc);
            rbox.AddChild(UiTheme.Caption("Характеристики"));
            _stats = UiTheme.VBox(3);
            rbox.AddChild(_stats);
            rbox.AddChild(UiTheme.Caption("Чтобы изучить"));
            _reqs = UiTheme.VBox(3);
            rbox.AddChild(_reqs);
            rbox.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
            _cost = UiTheme.Small("", UiTheme.Dim, UiTheme.FontSmall);
            rbox.AddChild(_cost);
            _learn = UiTheme.Btn("Изучить", true, 0, 44);
            _learn.AddThemeFontSizeOverride("font_size", UiTheme.FontBody);
            _learn.Pressed += () => Research?.Invoke(_selected);
            rbox.AddChild(_learn);
            _why = UiTheme.Small("", UiTheme.Bad, UiTheme.FontCaption);
            _why.CustomMinimumSize = new Vector2(ItemCard.W - 2 * UiTheme.PanelPad, 0);
            rbox.AddChild(_why);
        }

        // линии от предков к потомкам и подписи столбцов
        void DrawTree()
        {
            var font = UiTheme.Text;
            for (int col = 0; col < 3; col++)
            {
                string title = $"Верстак {col + 1}";
                bool here = _benchLevel >= col + 1;
                UiTheme.DrawRound(_tree, new Rect2(col * ColW + 16, 2, 40, 40), new Color(1, 1, 1, here ? 0.1f : 0.05f), 10);
                UiTheme.DrawIcon(_tree, col == 0 ? "deploy_workbench" : $"deploy_workbench{col + 1}", new Rect2(col * ColW + 20, 6, 32, 32), here ? Colors.White : new Color(1, 1, 1, 0.45f));
                UiTheme.DrawText(_tree, new Vector2(col * ColW + 64, 20), title, 14, here ? UiTheme.Ink : UiTheme.Body, UiTheme.Semi);
                UiTheme.DrawText(_tree, new Vector2(col * ColW + 64, 37), here ? "рядом ✓" : col == 0 ? "дерево, металл, лом" : $"крафт у верстака {col}", 12, here ? UiTheme.Good : UiTheme.Dim);
            }
            foreach (var t in _tech)
            {
                if (!_cells.TryGetValue(t.Id, out var child)) continue;
                foreach (var req in t.Requires)
                {
                    if (!_cells.TryGetValue(req, out var parent)) continue;
                    bool done = parent.T.Known;
                    var col = done ? new Color(UiTheme.Good, 0.7f) : new Color(1, 1, 1, 0.16f);
                    Vector2 a, b;
                    if (Mathf.IsEqualApprox(parent.Position.X, child.Position.X))
                    {
                        a = parent.Position + new Vector2(TechCell.Side / 2, TechCell.Side);
                        b = child.Position + new Vector2(TechCell.Side / 2, 0);
                        _tree.DrawLine(a, b, col, 2, true);
                    }
                    else
                    {
                        a = parent.Position + new Vector2(TechCell.Side, TechCell.Side / 2);
                        b = child.Position + new Vector2(0, TechCell.Side / 2);
                        float mx = b.X - 18;   // поворот у самого потомка — линия не идёт через чужие узлы
                        _tree.DrawPolyline(new[] { a, new Vector2(mx, a.Y), new Vector2(mx, b.Y), b }, col, 2, true);
                    }
                }
            }
            // подписи под узлами
            foreach (var c in _cells.Values)
            {
                string n = c.T.Name ?? "";
                var sz = font.GetStringSize(n, HorizontalAlignment.Left, -1, 12);
                _tree.DrawString(font, c.Position + new Vector2(TechCell.Side / 2 - sz.X / 2, TechCell.Side + 16), n, HorizontalAlignment.Left, -1, 12, c.T.Known ? UiTheme.Ink : UiTheme.Dim);
            }
        }

        public void Refresh(SelfInfo s)
        {
            _tech = s.Tech;
            _recipes = s.Recipes;
            _benchLevel = s.BenchLevel;
            _scrap.Text = $"{s.Scrap}";
            _bench.Text = s.BenchLevel > 0 ? $"Рядом верстак {s.BenchLevel} — можно изучать узлы уровня до {s.BenchLevel}" : "Верстака рядом нет — смотреть можно, изучать — только у верстака";
            foreach (var t in _tech)
                if (_cells.TryGetValue(t.Id, out var c)) { c.T = t; c.Selected = t.Id == _selected; c.QueueRedraw(); }
            _tree.QueueRedraw();

            var sel = _tech.Find(t => t.Id == _selected);
            if (sel.Id == null) return;
            if (_icon.Id != sel.Id) { _icon.Id = sel.Id; _icon.Rarity = Items.Rarity(sel.Id); _icon.QueueRedraw(); }
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
                var green = UiTheme.Good; var red = UiTheme.Bad;
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
            _learn.Text = sel.Known ? "Изучено ✓" : $"Изучить — {sel.Scrap} лома";
            _why.Text = sel.Known || sel.CanResearch ? "" : sel.Why;
        }

        /// <summary>Строка «подпись … значение» для карточек.</summary>
        public static Control Line(string label, string value, Color col)
        {
            var l = UiTheme.HBox(12);
            var a = UiTheme.Small(label, UiTheme.Dim, UiTheme.FontSmall);
            a.AutowrapMode = TextServer.AutowrapMode.Off;
            a.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            l.AddChild(a);
            var b = UiTheme.Strong(value, UiTheme.FontSmall, col);
            b.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            b.HorizontalAlignment = HorizontalAlignment.Right;
            b.CustomMinimumSize = new Vector2(Math.Min(180, UiTheme.TextWidth(value, UiTheme.FontSmall, UiTheme.Semi) + 2), 0);
            l.AddChild(b);
            return l;
        }
    }
}
