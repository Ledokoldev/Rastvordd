using System;
using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>Меню плана (ПКМ с Планом в руках): элементы стройки с ценой, клик — выбрать.</summary>
    public partial class BuildMenu : PanelContainer
    {
        public event Action<int> Picked;
        readonly VBoxContainer _list;
        string _sig;

        public BuildMenu()
        {
            Visible = false;
            var style = new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.07f, 0.93f), BorderColor = new Color(0.45f, 0.75f, 1f, 0.8f) };
            style.SetBorderWidthAll(2);
            style.SetCornerRadiusAll(6);
            style.ContentMarginLeft = style.ContentMarginRight = style.ContentMarginTop = style.ContentMarginBottom = 12;
            AddThemeStyleboxOverride("panel", style);
            var root = new VBoxContainer();
            root.AddThemeConstantOverride("separation", 6);
            AddChild(root);
            var title = new Label { Text = "Что строим?  (ПКМ — закрыть)" };
            title.AddThemeFontSizeOverride("font_size", 20);
            title.AddThemeColorOverride("font_color", new Color(0.7f, 0.88f, 1f));
            root.AddChild(title);
            _list = new VBoxContainer();
            _list.AddThemeConstantOverride("separation", 4);
            root.AddChild(_list);
            var tip = new Label { Text = "Всё ставится каркасом — потом Киянкой (ЛКМ) улучшаешь до дерева и камня." };
            tip.AddThemeFontSizeOverride("font_size", 13);
            tip.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.7f));
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

        public void Fill(List<(int kind, string name, string cost)> pieces, int current)
        {
            string sig = current + "|" + string.Join(";", pieces.ConvertAll(p => p.name + p.cost));
            if (sig == _sig) return;
            _sig = sig;
            foreach (var c in _list.GetChildren()) c.QueueFree();
            foreach (var (kind, name, cost) in pieces)
            {
                int k = kind;
                var b = new Button
                {
                    Text = $"{(k == current ? "> " : "")}{name} — {cost}\n    {Hints.GetValueOrDefault(name, "")}",
                    Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(420, 0),
                };
                b.AddThemeFontSizeOverride("font_size", 16);
                if (k == current) b.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
                b.Pressed += () => Picked?.Invoke(k);
                _list.AddChild(b);
            }
            CallDeferred(nameof(Recenter));
        }

        void Recenter()
        {
            var vs = GetViewportRect().Size;
            Size = GetCombinedMinimumSize();
            Position = (vs - Size) / 2;
        }
    }
}
