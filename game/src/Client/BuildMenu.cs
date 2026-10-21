using System;
using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>Плитка элемента стройки: иконка в металлической ячейке, название и цена (красная — не хватает ресурсов).</summary>
    public partial class BuildTile : Control
    {
        public const float W = 104, H = 132;
        public int Kind;
        public string Title, Cost, Hint;
        public bool Can, Selected;
        public event Action<int> Picked;
        bool _hover;

        public BuildTile()
        {
            CustomMinimumSize = new Vector2(W, H);
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
        }

        public override void _Ready()
        {
            MouseEntered += () => { _hover = true; QueueRedraw(); };
            MouseExited += () => { _hover = false; QueueRedraw(); };
        }

        static readonly string[] IconIds = { "foundation", "wall", "doorway", "window", "door" };

        public override void _Draw()
        {
            var cell = new Rect2(0, 0, W, W);
            UiTheme.DrawSlot(this, cell, _hover);
            var icon = Art.Get($"icons/icon_build_{IconIds[Math.Clamp(Kind, 0, IconIds.Length - 1)]}");
            if (icon != null) DrawTextureRect(icon, new Rect2(12, 10, W - 24, W - 24), false, Can ? Colors.White : new Color(0.55f, 0.5f, 0.5f));
            DrawString(UiTheme.Text, new Vector2(8, 17), (Kind + 1).ToString(), HorizontalAlignment.Left, -1, 12, Selected ? UiTheme.Accent : UiTheme.Dim);
            if (Selected) DrawRect(cell.Grow(-1), UiTheme.Accent, false, 2);

            var text = UiTheme.Text;
            var ts = text.GetStringSize(Title, HorizontalAlignment.Left, -1, 13);
            DrawString(text, new Vector2((W - ts.X) / 2, W + 14), Title, HorizontalAlignment.Left, -1, 13, Selected ? UiTheme.Accent : UiTheme.Ink);
            var cs = text.GetStringSize(Cost, HorizontalAlignment.Left, -1, 11);
            DrawString(text, new Vector2((W - cs.X) / 2, W + 28), Cost, HorizontalAlignment.Left, -1, 11, Can ? UiTheme.Dim : new Color(0.9f, 0.42f, 0.38f));
        }

        public override string _GetTooltip(Vector2 atPosition) => $"{Title}\n{Hint}\nцена: {Cost}{(Can ? "" : "\nне хватает ресурсов")}";

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left) { Picked?.Invoke(Kind); AcceptEvent(); }
        }
    }

    /// <summary>Меню плана (ПКМ с Планом в руках): сетка элементов стройки, клик или цифра — выбрать.</summary>
    public partial class BuildMenu : PanelContainer
    {
        public event Action<int> Picked;
        readonly HBoxContainer _row;
        readonly Label _desc;
        string _sig;
        readonly List<BuildTile> _tiles = new();

        public BuildMenu()
        {
            Visible = false;
            Theme = UiTheme.Theme;
            var root = new VBoxContainer();
            root.AddThemeConstantOverride("separation", 10);
            AddChild(root);
            root.AddChild(UiTheme.Header("СТРОЙКА", out _));
            _row = new HBoxContainer();
            _row.AddThemeConstantOverride("separation", 10);
            root.AddChild(_row);
            _desc = UiTheme.Small("", UiTheme.Ink, 14);
            _desc.HorizontalAlignment = HorizontalAlignment.Center;
            _desc.AutowrapMode = TextServer.AutowrapMode.Off;
            root.AddChild(_desc);
            var tip = UiTheme.Small("Всё ставится каркасом — потом Киянкой (ЛКМ) улучшаешь до дерева и камня.\nКлик или цифра 1–5 — выбрать · ПКМ — закрыть", null, 12);
            tip.HorizontalAlignment = HorizontalAlignment.Center;
            tip.AutowrapMode = TextServer.AutowrapMode.Off;
            root.AddChild(tip);
        }

        static readonly Dictionary<string, string> Hints = new()
        {
            ["Фундамент"] = "пол 2×2 м, с него всё начинается",
            ["Стена"] = "на край фундамента",
            ["Дверной проём"] = "стена с проходом, в неё ставится дверь",
            ["Окно"] = "стена с окном: видно и можно стрелять",
            ["Дверь"] = "в дверной проём",
        };

        public void Fill(List<(int kind, string name, string cost, bool can)> pieces, int current)
        {
            string sig = current + "|" + string.Join(";", pieces.ConvertAll(p => p.name + p.cost + p.can));
            if (sig == _sig) return;
            _sig = sig;
            foreach (var c in _row.GetChildren()) c.QueueFree();
            _tiles.Clear();
            foreach (var (kind, name, cost, can) in pieces)
            {
                var t = new BuildTile { Kind = kind, Title = name, Cost = cost, Can = can, Selected = kind == current, Hint = Hints.GetValueOrDefault(name, "") };
                t.Picked += k => Picked?.Invoke(k);
                t.MouseEntered += () => _desc.Text = $"{t.Title} — {t.Hint}";
                _row.AddChild(t);
                _tiles.Add(t);
                if (t.Selected) _desc.Text = $"{t.Title} — {t.Hint}";
            }
            CallDeferred(nameof(Recenter));
        }

        /// <summary>Цифры 1–5 выбирают элемент, пока меню открыто.</summary>
        public bool PickByKey(Key key)
        {
            int i = (int)(key - Key.Key1);
            if (!Visible || i < 0 || i >= _tiles.Count) return false;
            Picked?.Invoke(_tiles[i].Kind);
            return true;
        }

        void Recenter()
        {
            var vs = GetViewportRect().Size;
            Size = Vector2.Zero;
            Size = GetCombinedMinimumSize();
            Position = (vs - Size) / 2;
        }
    }
}
