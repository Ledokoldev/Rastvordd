using System;
using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Окно по E: слева «у тебя», справа содержимое (ящик, рюкзак, печь, межевой столб).
    /// Клик по предмету — переложить целиком. Кнопки внизу — «забрать всё», «сложить всё», печь — зажечь/забрать готовое.
    /// </summary>
    public partial class UiPanel : PanelContainer
    {
        /// <summary>(действие, предмет, индекс) — уходит на сервер в следующем вводе.</summary>
        public event Action<UiOp, string, int> Action;

        readonly Label _title, _info;
        readonly VBoxContainer _left, _right;
        readonly HBoxContainer _buttons;
        string _sig;

        public UiPanel()
        {
            Visible = false;
            var style = new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.07f, 0.92f), BorderColor = new Color(0.75f, 0.65f, 0.45f, 0.8f) };
            style.SetBorderWidthAll(2);
            style.SetCornerRadiusAll(6);
            style.ContentMarginLeft = style.ContentMarginRight = style.ContentMarginTop = style.ContentMarginBottom = 12;
            AddThemeStyleboxOverride("panel", style);

            var root = new VBoxContainer();
            root.AddThemeConstantOverride("separation", 8);
            AddChild(root);
            _title = new Label();
            _title.AddThemeFontSizeOverride("font_size", 22);
            _title.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.65f));
            root.AddChild(_title);
            _info = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(620, 0) };
            _info.AddThemeFontSizeOverride("font_size", 15);
            root.AddChild(_info);

            var cols = new HBoxContainer();
            cols.AddThemeConstantOverride("separation", 16);
            root.AddChild(cols);
            _left = Column(cols, "У тебя — клик: положить");
            _right = Column(cols, "Внутри — клик: забрать");
            _buttons = new HBoxContainer();
            _buttons.AddThemeConstantOverride("separation", 8);
            root.AddChild(_buttons);
        }

        static VBoxContainer Column(Control parent, string caption)
        {
            var box = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
            box.AddThemeConstantOverride("separation", 3);
            var l = new Label { Text = caption };
            l.AddThemeFontSizeOverride("font_size", 13);
            l.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.7f));
            box.AddChild(l);
            var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(300, 300), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            var list = new VBoxContainer();
            list.AddThemeConstantOverride("separation", 3);
            list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            scroll.AddChild(list);
            box.AddChild(scroll);
            parent.AddChild(box);
            return list;
        }

        Button ItemButton(string id, string text, Action onClick)
        {
            var b = new Button { Text = text, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None, Icon = Art.Icon(id), TextureFilter = TextureFilterEnum.Nearest };
            b.AddThemeConstantOverride("icon_max_width", 28);
            b.AddThemeFontSizeOverride("font_size", 15);
            b.Pressed += onClick;
            return b;
        }

        void AddButton(string text, Action onClick, bool accent = false)
        {
            var b = new Button { Text = text, FocusMode = FocusModeEnum.None };
            b.AddThemeFontSizeOverride("font_size", 16);
            if (accent) b.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
            b.Pressed += onClick;
            _buttons.AddChild(b);
        }

        static void Clear(Node n)
        {
            foreach (var c in n.GetChildren()) c.QueueFree();
        }

        /// <summary>Обновить по снапшоту (перестраивается только при изменениях).</summary>
        public void Show(UiView v)
        {
            if (v == null) { Visible = false; _sig = null; return; }
            string sig = $"{v.Kind}|{v.Title}|{v.ClaimText}|{v.Ore}|{v.SulfurOre}|{v.Fuel}|{v.OutMetal}|{v.OutSulfur}|{v.OutCharcoal}|{v.On}|{v.Burning}|" +
                         string.Join(";", v.Items.ConvertAll(i => $"{i.id}:{i.count}")) + "|" + string.Join(";", v.Mine.ConvertAll(i => $"{i.key}:{i.count}"));
            Visible = true;
            if (sig == _sig) return;
            _sig = sig;
            Clear(_left); Clear(_right); Clear(_buttons);
            _title.Text = v.Title;

            foreach (var m in v.Mine)
            {
                var key = m.key;
                _left.AddChild(ItemButton(m.id, $"{m.name} ×{m.count}", () => Action?.Invoke(UiOp.Put, key, 0)));
            }
            if (v.Mine.Count == 0) _left.AddChild(new Label { Text = v.Kind == UiKind.Furnace ? "нет руды и дерева" : "пусто" });

            switch (v.Kind)
            {
                case UiKind.Furnace:
                    _info.Text = (v.Burning ? "Горит: руда плавится, дерево горит, уголь копится." : v.On ? "Зажжена, но не хватает руды или дерева." : "Потушена. Положи руду и дерево, нажми «Зажечь».")
                                 + "\n1 руда → 1 металл (сера → сера), дерево — топливо, из него уголь.";
                    void Row(string id, string name, int n) { if (n > 0) _right.AddChild(ItemButton(id, $"{name}: {n} — вынуть", () => Action?.Invoke(UiOp.Take, id, 0))); }
                    Row("metal_ore", "Металлическая руда", v.Ore);
                    Row("sulfur_ore", "Серная руда", v.SulfurOre);
                    Row("wood", "Дерево (топливо)", v.Fuel);
                    var ready = new Label { Text = $"Готово: металл {v.OutMetal} · сера {v.OutSulfur} · уголь {v.OutCharcoal}" };
                    ready.AddThemeColorOverride("font_color", new Color(0.7f, 1f, 0.7f));
                    _right.AddChild(ready);
                    AddButton(v.On ? "Потушить" : "Зажечь", () => Action?.Invoke(UiOp.Toggle, null, 0), true);
                    AddButton("Забрать готовое", () => Action?.Invoke(UiOp.TakeOut, null, 0));
                    break;
                case UiKind.Claim:
                    _info.Text = v.ClaimText + "\nПоложи сюда ресурсы — шкаф тратит их на содержание дома. Без запаса дом начнёт гнить.";
                    foreach (var it in v.Items)
                    {
                        var id = it.id;
                        _right.AddChild(ItemButton(id, $"{it.name}: {it.count} — забрать", () => Action?.Invoke(UiOp.Take, id, 0)));
                    }
                    if (v.Items.Count == 0) _right.AddChild(new Label { Text = "запаса нет" });
                    break;
                default:
                    _info.Text = "";
                    for (int i = 0; i < v.Items.Count; i++)
                    {
                        int idx = i;
                        var it = v.Items[i];
                        _right.AddChild(ItemButton(it.id, $"{it.name} ×{it.count}", () => Action?.Invoke(UiOp.Take, null, idx)));
                    }
                    if (v.Items.Count == 0) _right.AddChild(new Label { Text = "пусто" });
                    AddButton("Забрать всё", () => Action?.Invoke(UiOp.TakeAll, null, 0), true);
                    if (v.Kind == UiKind.Box) AddButton("Сложить всё", () => Action?.Invoke(UiOp.PutAll, null, 0));
                    break;
            }
            AddButton("Закрыть (Esc)", () => Action?.Invoke(UiOp.Close, null, 0));
            CallDeferred(nameof(Recenter));
        }

        void Recenter()
        {
            var vs = GetViewportRect().Size;
            Size = GetCombinedMinimumSize();
            Position = (vs - Size) / 2 + new Vector2(0, -20);
        }
    }
}
