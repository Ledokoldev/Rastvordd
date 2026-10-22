using System;
using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>
    /// Панель полигона (docs/design/18-sandbox.md): стеклянная панель слева. Сверху всегда видна сводка (счётчики, последний звук),
    /// по F2 разворачиваются кнопки режима. Клик мимо развёрнутой панели её сворачивает (как остальные окна).
    /// </summary>
    public partial class SandboxPanel : PanelContainer
    {
        readonly VBoxContainer _controls;
        readonly ScrollContainer _scroll;
        readonly Label _title;
        public readonly Label Info;
        public bool Expanded => _scroll.Visible;
        public event Action Toggled;

        public SandboxPanel(string title)
        {
            Theme = UiTheme.Theme;
            AddThemeStyleboxOverride("panel", UiTheme.Glass(14, 0.74f, 14));
            var box = UiTheme.VBox(8);
            AddChild(box);
            var top = UiTheme.HBox(10);
            _title = UiTheme.Title(title, 17);
            top.AddChild(_title);
            var chip = UiTheme.KeyChip("F2 — панель");
            chip.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            top.AddChild(chip);
            box.AddChild(top);
            Info = UiTheme.Small("", UiTheme.Body, UiTheme.FontCaption);
            Info.AutowrapMode = TextServer.AutowrapMode.Off;
            Info.CustomMinimumSize = new Vector2(430, 0);
            box.AddChild(Info);
            _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(450, 420) };
            box.AddChild(_scroll);
            _controls = UiTheme.VBox(6);
            _controls.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _scroll.AddChild(_controls);
            _scroll.Visible = false;
        }

        /// <summary>Высота прокрутки — по экрану (панель не вылезает за низ).</summary>
        public void Fit(float screenH, float top)
        {
            float h = Mathf.Min(_controls.GetCombinedMinimumSize().Y + 4, Mathf.Max(180, screenH - top - Info.Size.Y - 250));   // снизу — шкалы здоровья и пояс
            if (Mathf.Abs(_scroll.CustomMinimumSize.Y - h) > 2) { _scroll.CustomMinimumSize = new Vector2(450, h); Size = Vector2.Zero; }
        }

        public void SetExpanded(bool on)
        {
            if (_scroll.Visible == on) return;
            _scroll.Visible = on;
            Size = Vector2.Zero;   // ужать по содержимому
            Toggled?.Invoke();
        }

        public void Clear() { foreach (var c in _controls.GetChildren()) c.QueueFree(); }

        public void Caption(string text) => _controls.AddChild(UiTheme.Caption(text));

        public Label Note(string text, Color? c = null)
        {
            var l = UiTheme.Small(text, c ?? UiTheme.Dim, UiTheme.FontCaption);
            l.CustomMinimumSize = new Vector2(420, 0);
            _controls.AddChild(l);
            return l;
        }

        HFlowContainer Flow()
        {
            var f = new HFlowContainer { CustomMinimumSize = new Vector2(430, 0) };
            f.AddThemeConstantOverride("h_separation", 6);
            f.AddThemeConstantOverride("v_separation", 6);
            _controls.AddChild(f);
            return f;
        }

        /// <summary>Ряд кнопок.</summary>
        public List<Button> Buttons(params (string text, Action act)[] items)
        {
            var f = Flow();
            var list = new List<Button>();
            foreach (var (t, a) in items)
            {
                var b = UiTheme.Btn(t, false, 0, 32);
                b.Pressed += a;
                f.AddChild(b);
                list.Add(b);
            }
            return list;
        }

        /// <summary>Выбор одного из вариантов (чипы).</summary>
        public List<Button> Choice(string[] options, int selected, Action<int> pick)
        {
            var f = Flow();
            var list = new List<Button>();
            for (int i = 0; i < options.Length; i++)
            {
                int k = i;
                var b = UiTheme.Chip(options[i], i == selected);
                b.Pressed += () =>
                {
                    for (int j = 0; j < list.Count; j++) list[j].SetPressedNoSignal(j == k);
                    pick(k);
                };
                f.AddChild(b);
                list.Add(b);
            }
            return list;
        }

        /// <summary>Переключатель (чип вкл/выкл).</summary>
        public Button Toggle(string text, bool on, Action<bool> set)
        {
            var f = Flow();
            var b = UiTheme.Chip(text, on);
            b.Toggled += v => set(v);
            f.AddChild(b);
            return b;
        }

        /// <summary>Поле ввода с кнопкой.</summary>
        public LineEdit Input(string placeholder, string button, Action<string> go)
        {
            var row = UiTheme.HBox(6);
            var e = new LineEdit { PlaceholderText = placeholder, CustomMinimumSize = new Vector2(300, 32) };
            var b = UiTheme.Btn(button, true, 0, 32);
            b.Pressed += () => go(e.Text);
            e.TextSubmitted += t => { go(t); e.ReleaseFocus(); };
            row.AddChild(e);
            row.AddChild(b);
            _controls.AddChild(row);
            return e;
        }

        public OptionButton Options(IEnumerable<string> items, Action<string> pick)
        {
            var o = new OptionButton { CustomMinimumSize = new Vector2(420, 32), FocusMode = FocusModeEnum.None };
            foreach (var it in items) o.AddItem(it);
            o.ItemSelected += i => pick(o.GetItemText((int)i));
            _controls.AddChild(o);
            return o;
        }
    }
}
