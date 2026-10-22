using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Ячейка инвентаря: скруглённая плашка, иконка, количество справа внизу, редкость — точка слева вверху.
    /// Тащи мышкой в другую ячейку; Shift+клик или ПКМ — на другую сторону; Shift и провести курсором — ховер-лут.
    /// Скрытая ячейка (ещё не обыскана) — «?», текущая обыскивается: крутится кольцо и идёт полоска.
    /// </summary>
    public partial class SlotCell : Control
    {
        float _side = UiTheme.CellSize;
        public float Side { get => _side; set { _side = value; CustomMinimumSize = new Vector2(value, value); } }
        public string Inv;
        public int Index;
        public CellView Cell;
        public bool Searching;
        public float Progress;
        public bool Selected;          // выбранная ячейка (карточка) или активный слот пояса
        public string Number;          // цифра слота пояса
        public bool Dimmed;            // не подходит под фильтр категории
        public bool ShowTip;           // всплывающая мини-карточка (когда большой карточки нет)
        public bool Backing;           // тёмная подложка (пояс в HUD поверх мира)
        public event Action<string, int, string, int> Moved;   // (откуда, i, куда, j)
        public event Action<string, int> Quick, Picked, Hovered, Detach;   // Detach — колёсико по стволу: снять обвесы (D46)
        float _pop, _spin;
        bool _hover;

        public SlotCell()
        {
            CustomMinimumSize = new Vector2(_side, _side);
            MouseFilter = MouseFilterEnum.Stop;
            TextureFilter = TextureFilterEnum.Linear;
            TooltipText = " ";
        }

        public override void _Ready()
        {
            MouseExited += () => { _hover = false; QueueRedraw(); Hovered?.Invoke(Inv, -1); };
            MouseEntered += () =>
            {
                _hover = true; QueueRedraw();
                Hovered?.Invoke(Inv, Index);
                // ховер-лут: держишь Shift и ведёшь курсором — вещи перелетают
                if (Input.IsKeyPressed(Key.Shift) && Cell.Id != null) Quick?.Invoke(Inv, Index);
            };
        }

        /// <summary>Вспышка: ячейка только что раскрылась.</summary>
        public void Pop() => _pop = 1;

        public override void _Process(double delta)
        {
            if (_pop > 0) { _pop = Mathf.Max(0, _pop - (float)delta * 2.5f); QueueRedraw(); }
            else if (Searching) { _spin += (float)delta * 6f; QueueRedraw(); }
        }

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            float s = Size.X;
            if (Backing) UiTheme.DrawRound(this, r, new Color(UiTheme.PanelBg, 0.55f), UiTheme.CellRadius);

            if (Cell.Hidden)
            {
                UiTheme.DrawSlot(this, r, false, true);
                var c = r.GetCenter();
                if (Searching)
                {
                    // кольцо-спиннер и полоска обыска
                    DrawArc(c, s * 0.17f, 0, Mathf.Tau, 32, new Color(1, 1, 1, 0.08f), 2.5f, true);
                    DrawArc(c, s * 0.17f, _spin, _spin + 1.8f, 16, UiTheme.Accent, 2.5f, true);
                    UiTheme.DrawBar(this, new Rect2(10, s - 12, s - 20, 4), Progress, UiTheme.Accent);
                }
                else
                {
                    float w = UiTheme.TextWidth("?", 22, UiTheme.Semi);
                    UiTheme.DrawText(this, c + new Vector2(-w / 2, 8), "?", 22, new Color(1, 1, 1, 0.3f), UiTheme.Semi);
                }
                return;
            }

            if (Cell.Id != null)
            {
                UiTheme.DrawSlot(this, r, _hover, false, Selected);
                float a = Dimmed ? 0.22f : 1f;
                float isz = s * UiTheme.IconFill;
                UiTheme.DrawIcon(this, Cell.Id, new Rect2((s - isz) / 2, (s - isz) / 2 - s * 0.02f, isz, isz), new Color(1, 1, 1, a));
                if (Cell.Rarity > 0)
                {
                    var dotAt = Number == null ? new Vector2(13.5f, 13.5f) : new Vector2(s - 13.5f, 13.5f);
                    UiTheme.DrawDot(this, dotAt, 3.5f, new Color(UiTheme.RarityColor(Cell.Rarity), a));
                }
                bool worn = Cell.Durability >= 0;
                if (worn)   // износ ствола — тонкая полоска по низу ячейки
                    UiTheme.DrawBar(this, new Rect2(9, s - 7, s - 18, 3), Cell.Durability, new Color(UiTheme.DurabilityColor(Cell.Durability), a), new Color(1, 1, 1, 0.1f * a));
                DrawAttPips(this, Cell, new Vector2(12, s - (worn ? 14 : 11)), a);
                string n = Cell.Magazine > 0 ? $"{Cell.Ammo}/{Cell.Magazine}" : Cell.Count > 1 ? (Cell.Count >= 10000 ? $"{Cell.Count / 1000}k" : Cell.Count.ToString()) : "";
                if (n != "")
                {
                    int fs = Cell.Magazine > 0 ? 12 : (s < 70 ? 12 : 14);
                    float w = UiTheme.TextWidth(n, fs, UiTheme.Semi);
                    var at = new Vector2(s - 9 - w, s - (worn ? 12 : 8));
                    UiTheme.DrawText(this, at + new Vector2(0, 1), n, fs, new Color(0, 0, 0, 0.6f * a), UiTheme.Semi);
                    UiTheme.DrawText(this, at, n, fs, new Color(UiTheme.Ink, a), UiTheme.Semi);
                }
                if (_pop > 0)
                {
                    var rc = UiTheme.RarityColor(Cell.Rarity);
                    UiTheme.DrawRound(this, r, new Color(rc, 0.35f * _pop), UiTheme.CellRadius, new Color(rc, _pop), 2);
                }
            }
            else
            {
                UiTheme.DrawSlot(this, r, _hover, false, false, true);
                if (Cell.Label != null)
                {
                    float w = UiTheme.TextWidth(Cell.Label, 12);
                    UiTheme.DrawText(this, new Vector2((s - w) / 2, s / 2 + 4), Cell.Label, 12, UiTheme.Faint);
                }
            }
            if (Number != null) UiTheme.DrawText(this, new Vector2(10, 18), Number, 12, Selected ? UiTheme.Ink : new Color(1, 1, 1, 0.55f));
        }

        /// <summary>
        /// Обвесы на стволе — точки по слотам, которые ствол принимает (прицел, дуло, цевьё, магазин, приклад):
        /// заполненная — стоит обвес (цвет его качества), пустое кольцо — слот свободен.
        /// </summary>
        public static void DrawAttPips(CanvasItem c, CellView cell, Vector2 at, float a = 1)
        {
            if (Items.Get(cell.Id)?.Weapon is not WeaponDef w || w.Slots == 0) return;
            float x = at.X;
            for (int k = 0; k < Weaponry.SlotCount; k++)
            {
                if (!w.Accepts((AttSlot)k)) continue;
                string att = cell.Att != null && k < cell.Att.Length ? cell.Att[k] : null;
                var p = new Vector2(x, at.Y);
                if (att != null) UiTheme.DrawDot(c, p, 2.8f, new Color(UiTheme.RarityColor(Items.Rarity(att)), a));
                else c.DrawArc(p, 2.4f, 0, Mathf.Tau, 12, new Color(1, 1, 1, 0.35f * a), 1f, true);
                x += 7.5f;
            }
        }

        public override string _GetTooltip(Vector2 atPosition)
        {
            if (Cell.Hidden) return ShowTip ? "Обыскивается…" : "";
            if (!ShowTip || Cell.Id == null) return "";
            return Cell.Name ?? Cell.Id;
        }

        public override GodotObject _MakeCustomTooltip(string forText)
        {
            if (Cell.Id == null || Cell.Hidden) return null;
            return ItemCard.Mini(Cell);
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            if (Cell.Id == null || Cell.Hidden) return default;
            var icon = new IconView { Id = Cell.Id, BoxAlpha = 0.12f, Radius = 12, Size = new Vector2(64, 64), Modulate = new Color(1, 1, 1, 0.9f) };
            SetDragPreview(icon);
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
            if (e is InputEventMouseButton mb && mb.Pressed && Cell.Id != null && !Cell.Hidden)
            {
                if ((mb.ButtonIndex == MouseButton.Left && mb.ShiftPressed) || mb.ButtonIndex == MouseButton.Right)
                {
                    Quick?.Invoke(Inv, Index);
                    AcceptEvent();
                }
                else if (mb.ButtonIndex == MouseButton.Left) Picked?.Invoke(Inv, Index);
                else if (mb.ButtonIndex == MouseButton.Middle) { Detach?.Invoke(Inv, Index); AcceptEvent(); }
            }
        }
    }

    /// <summary>Здоровье, еда, вода, броня: значок, полоска, число. Слева от пояса, всегда на экране.</summary>
    public partial class VitalsView : Control
    {
        public const float W = 220, RowH = 22;
        float _hp, _maxHp = 100, _food, _water, _armor;

        public VitalsView() { CustomMinimumSize = new Vector2(W, RowH * 4); Size = CustomMinimumSize; MouseFilter = MouseFilterEnum.Ignore; }

        public void Set(float hp, float maxHp, float food, float water, float armor)
        {
            if (Mathf.IsEqualApprox(hp, _hp) && Mathf.IsEqualApprox(food, _food) && Mathf.IsEqualApprox(water, _water) && Mathf.IsEqualApprox(armor, _armor)) return;
            _hp = hp; _maxHp = Mathf.Max(1, maxHp); _food = food; _water = water; _armor = armor;
            QueueRedraw();
        }

        public override void _Draw()
        {
            float y0 = Size.Y - RowH * 4;
            (float v, float max, Color c, string n)[] rows =
            {
                (_hp, _maxHp, _hp / _maxHp > 0.25f ? UiTheme.HpColor : UiTheme.Bad, Mathf.CeilToInt(_hp).ToString()),
                (_food, 100, UiTheme.FoodColor, Mathf.CeilToInt(_food).ToString()),
                (_water, 100, UiTheme.WaterColor, Mathf.CeilToInt(_water).ToString()),
                (_armor, 1, UiTheme.ArmorColor, $"{Mathf.RoundToInt(_armor * 100)}%"),   // броня: на сколько режет урон пулями в торс
            };
            for (int i = 0; i < rows.Length; i++)
            {
                float cy = y0 + i * RowH + RowH / 2;
                UiTheme.DrawVitalIcon(this, i, new Vector2(9, cy), rows[i].c);
                UiTheme.DrawBar(this, new Rect2(28, cy - 4, W - 28 - 46, 8), rows[i].v / rows[i].max, rows[i].c);
                float w = UiTheme.TextWidth(rows[i].n, UiTheme.FontSmall, UiTheme.Semi);
                UiTheme.DrawText(this, new Vector2(W - w, cy + 5), rows[i].n, UiTheme.FontSmall, Hex(0xd6d6d6), UiTheme.Semi);
            }
        }

        static Color Hex(uint c) => UiTheme.Hex(c);
    }

    /// <summary>
    /// Низ экрана: 6 ячеек пояса ровно по центру экрана; шкалы висят СЛЕВА от пояса и на его положение не влияют
    /// (размер контрола = только пояс). В HUD — только показывает, в инвентаре — тащи и бросай.
    /// </summary>
    public partial class Hotbar : Control
    {
        public const float Gap = 22;                                   // от шкал / ленты подборов до пояса
        public static float BeltWidth => Actor.BeltSize * UiTheme.CellSize + (Actor.BeltSize - 1) * UiTheme.CellGap;
        public readonly VitalsView Vitals = new();
        public readonly List<SlotCell> Cells = new();

        public Hotbar(bool interactive)
        {
            MouseFilter = MouseFilterEnum.Ignore;
            CustomMinimumSize = new Vector2(BeltWidth, UiTheme.CellSize);
            var belt = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            belt.AddThemeConstantOverride("separation", (int)UiTheme.CellGap);
            AddChild(belt);
            for (int i = 0; i < Actor.BeltSize; i++)
            {
                var c = new SlotCell { Inv = "belt", Index = i, Number = (i + 1).ToString(), Cell = new CellView() };
                if (!interactive) { c.MouseFilter = MouseFilterEnum.Ignore; c.TooltipText = ""; c.Backing = true; }
                belt.AddChild(c);
                Cells.Add(c);
            }
            // шкалы — за левым краем пояса, низом по низу ячеек
            Vitals.Position = new Vector2(-VitalsView.W - Gap, UiTheme.CellSize - VitalsView.RowH * 4);
            AddChild(Vitals);
        }

        public void Refresh(SelfInfo self)
        {
            Vitals.Set(self.Hp, self.MaxHp, self.Food, self.Water, self.Armor);
            for (int i = 0; i < Cells.Count && i < self.BeltCells.Count; i++)
            {
                bool sel = i == self.Current;
                var c = Cells[i];
                if (!InventoryWindow.Same(c.Cell, self.BeltCells[i]) || c.Selected != sel) { c.Cell = self.BeltCells[i]; c.Selected = sel; c.QueueRedraw(); }
            }
        }
    }

    /// <summary>Место под одежду: пунктирная рамка и бледный силуэт вещи.</summary>
    public partial class GearSlot : Control
    {
        public string Ghost;
        public GearSlot() { CustomMinimumSize = new Vector2(76, 76); MouseFilter = MouseFilterEnum.Stop; TextureFilter = TextureFilterEnum.Linear; }

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            UiTheme.DrawRound(this, r, new Color(1, 1, 1, 0.04f), UiTheme.CellRadius);
            DrawDashedRound(this, r.Grow(-0.5f), UiTheme.CellRadius, new Color(1, 1, 1, 0.16f));
            var tex = Ghost != null ? Art.Get(Ghost) : null;
            if (tex != null) DrawTextureRect(tex, r.Grow(-12), false, new Color(0.75f, 0.75f, 0.75f, 0.25f));
        }

        public static void DrawDashedRound(CanvasItem c, Rect2 r, float rad, Color col, float dash = 5, float gap = 4)
        {
            void Dashes(Vector2 a, Vector2 b)
            {
                float len = a.DistanceTo(b);
                var d = (b - a) / Mathf.Max(len, 0.001f);
                for (float t = 0; t < len; t += dash + gap) c.DrawLine(a + d * t, a + d * Mathf.Min(len, t + dash), col, 1);
            }
            var p = r.Position; var e = r.End;
            Dashes(new Vector2(p.X + rad, p.Y), new Vector2(e.X - rad, p.Y));
            Dashes(new Vector2(e.X, p.Y + rad), new Vector2(e.X, e.Y - rad));
            Dashes(new Vector2(e.X - rad, e.Y), new Vector2(p.X + rad, e.Y));
            Dashes(new Vector2(p.X, e.Y - rad), new Vector2(p.X, p.Y + rad));
            c.DrawArc(new Vector2(p.X + rad, p.Y + rad), rad, Mathf.Pi, Mathf.Pi * 1.5f, 8, col, 1, true);
            c.DrawArc(new Vector2(e.X - rad, p.Y + rad), rad, Mathf.Pi * 1.5f, Mathf.Tau, 8, col, 1, true);
            c.DrawArc(new Vector2(e.X - rad, e.Y - rad), rad, 0, Mathf.Pi * 0.5f, 8, col, 1, true);
            c.DrawArc(new Vector2(p.X + rad, e.Y - rad), rad, Mathf.Pi * 0.5f, Mathf.Pi, 8, col, 1, true);
        }
    }

    /// <summary>Большая картинка предмета на карточке: мягкое свечение цвета редкости.</summary>
    public partial class BigIcon : Control
    {
        static GradientTexture2D _glow;
        public string Id;
        public int Rarity;
        public BigIcon() { MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Linear; }

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            UiTheme.DrawRound(this, r, new Color(1, 1, 1, 0.03f), UiTheme.CellRadius);
            _glow ??= new GradientTexture2D
            {
                Width = 128, Height = 128, Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
                Gradient = new Gradient { Colors = new[] { Colors.White, new Color(1, 1, 1, 0) } },
            };
            var glowCol = Rarity > 0 ? UiTheme.RarityColor(Rarity) : new Color(1, 1, 1);
            float gh = Size.Y * 1.1f;
            DrawTextureRect(_glow, new Rect2(r.GetCenter() - new Vector2(gh * 0.9f, gh / 2), new Vector2(gh * 1.8f, gh)), false, new Color(glowCol, Rarity > 0 ? 0.24f : 0.07f));
            if (Id == null) return;
            float s = Mathf.Min(Size.Y - 30, 120);
            UiTheme.DrawIcon(this, Id, new Rect2(r.GetCenter() - new Vector2(s, s) / 2, new Vector2(s, s)));
        }
    }

    /// <summary>
    /// Карточка предмета (справа во всю высоту окна): картинка, имя, редкость и категория, описание,
    /// «Нужно для крафта» (2 самых простых рецепта, где тратится), «Переработка» (что даст переработчик), клавиши.
    /// </summary>
    public partial class ItemCard : PanelContainer
    {
        public const float W = 340;
        readonly BigIcon _icon = new() { CustomMinimumSize = new Vector2(0, 150) };
        readonly Label _name, _count, _type, _desc, _empty, _where;
        readonly VBoxContainer _uses, _usesBox, _recBox, _gunBox, _gunStats, _gunAtts, _keysItem, _keysGun;
        readonly HFlowContainer _rec;
        readonly ThinBar _durBar;
        readonly Label _durText;
        string _sig;

        public ItemCard()
        {
            AddThemeStyleboxOverride("panel", UiTheme.Glass());
            CustomMinimumSize = new Vector2(W, 0);
            var box = UiTheme.VBox(0);
            AddChild(box);
            box.AddChild(_icon);
            box.AddChild(Gap(14));
            var nameRow = UiTheme.HBox(8);
            _name = UiTheme.Title("");
            _name.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            nameRow.AddChild(_name);
            _count = UiTheme.Small("", UiTheme.Dim, 15);
            _count.AutowrapMode = TextServer.AutowrapMode.Off;
            _count.SizeFlagsVertical = SizeFlags.ShrinkEnd;
            nameRow.AddChild(_count);
            box.AddChild(nameRow);
            _type = UiTheme.Small("", UiTheme.Dim, UiTheme.FontSmall);
            box.AddChild(_type);
            box.AddChild(Gap(8));
            _desc = UiTheme.Small("", UiTheme.Body, UiTheme.FontBody);
            _desc.CustomMinimumSize = new Vector2(W - 2 * UiTheme.PanelPad, 0);
            box.AddChild(_desc);
            _empty = UiTheme.Small("Наведи на предмет или кликни по нему — здесь появится, для чего он нужен.", UiTheme.Dim, UiTheme.FontBody);
            _empty.CustomMinimumSize = new Vector2(W - 2 * UiTheme.PanelPad, 0);
            box.AddChild(_empty);

            // оружие: износ, характеристики, обвесы по слотам
            _gunBox = UiTheme.VBox(4);
            _gunBox.AddChild(Gap(8));
            var dur = UiTheme.HBox(10);
            var dl = UiTheme.Small("Состояние", UiTheme.Dim, UiTheme.FontSmall);
            dl.AutowrapMode = TextServer.AutowrapMode.Off;
            dur.AddChild(dl);
            _durBar = new ThinBar { CustomMinimumSize = new Vector2(0, 6), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            dur.AddChild(_durBar);
            _durText = UiTheme.Strong("", UiTheme.FontSmall);
            dur.AddChild(_durText);
            _gunBox.AddChild(dur);
            _gunBox.AddChild(Gap(6));
            _gunBox.AddChild(UiTheme.Caption("Характеристики"));
            _gunStats = UiTheme.VBox(1);
            _gunBox.AddChild(_gunStats);
            _gunBox.AddChild(Gap(6));
            _gunBox.AddChild(UiTheme.Caption("Обвесы"));
            _gunAtts = UiTheme.VBox(3);
            _gunBox.AddChild(_gunAtts);
            box.AddChild(_gunBox);

            _usesBox = UiTheme.VBox(6);
            _usesBox.AddChild(Gap(10));
            _usesBox.AddChild(UiTheme.Caption("Нужно для крафта"));
            _uses = UiTheme.VBox(6);
            _usesBox.AddChild(_uses);
            box.AddChild(_usesBox);

            _recBox = UiTheme.VBox(6);
            _recBox.AddChild(Gap(10));
            _recBox.AddChild(UiTheme.Caption("Переработка"));
            _rec = new HFlowContainer();
            _rec.AddThemeConstantOverride("h_separation", 8);
            _rec.AddThemeConstantOverride("v_separation", 6);
            _recBox.AddChild(_rec);
            _where = UiTheme.Small("", UiTheme.Dim, UiTheme.FontCaption);
            _recBox.AddChild(_where);
            box.AddChild(_recBox);

            box.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 12) });
            // клавиши — две строки «таблеток»; у ствола — свои (снять обвесы, ремонт)
            VBoxContainer Keys(string[][] lines)
            {
                var kb = UiTheme.VBox(6);
                foreach (var line in lines)
                {
                    var keys = UiTheme.HBox(6);
                    foreach (var k in line) keys.AddChild(UiTheme.KeyChip(k));
                    kb.AddChild(keys);
                }
                box.AddChild(kb);
                return kb;
            }
            _keysItem = Keys(new[] { new[] { "Тащи — переложить", "За окно — выбросить" }, new[] { "ПКМ / Shift+клик — на пояс" } });
            _keysGun = Keys(new[] { new[] { "Колёсико — снять обвесы", "Обвес — тащи на ствол" }, new[] { "Alt+E у верстака — ремонт маслом" } });
            Show(null);
        }

        static Control Gap(float h) => new Control { CustomMinimumSize = new Vector2(0, h), MouseFilter = MouseFilterEnum.Ignore };

        /// <summary>Рецепты, где тратится предмет: сначала самые простые (меньше ингредиентов, ниже верстак, быстрее).</summary>
        public static List<Recipe> UsesOf(string id, int max = 2) =>
            Recipes.All.Where(r => r.Cost.Any(c => c.id == id))
                .OrderBy(r => r.Cost.Length).ThenBy(r => r.Bench).ThenBy(r => r.Time).Take(max).ToList();

        public void Show(CellView? cell)
        {
            var cv = cell ?? default;
            string id = cv.Id;
            int count = cv.Count;
            string sig = id + "|" + count + "|" + cv.Durability.ToString("0.00") + "|" + (cv.Att != null ? string.Join(",", cv.Att) : "");
            if (sig == _sig) return;
            _sig = sig;
            bool has = id != null;
            bool gun = has && Items.Get(id)?.Weapon is WeaponDef gw && !gw.Melee;
            _gunBox.Visible = gun;
            _keysGun.Visible = gun;
            _keysItem.Visible = !gun;
            if (gun) FillGun(cv);
            _empty.Visible = !has;
            _name.Visible = _count.Visible = _type.Visible = _desc.Visible = has;
            _icon.Id = id;
            _icon.Rarity = has ? Items.Rarity(id) : 0;
            _icon.QueueRedraw();
            _usesBox.Visible = _recBox.Visible = has;
            if (!has) return;

            int rar = Items.Rarity(id);
            _name.Text = Items.Name(id);
            _count.Text = count > 1 ? $"×{count}" : "";
            _type.Text = $"{UiTheme.RarityName(rar)} · {UiTheme.CategoryName(id)}";
            _type.AddThemeColorOverride("font_color", rar > 0 ? UiTheme.RarityColor(rar) : UiTheme.Dim);
            _desc.Text = Items.Desc(id);
            _desc.Visible = _desc.Text != "";

            foreach (var c in _uses.GetChildren()) c.QueueFree();
            var uses = UsesOf(id);
            foreach (var r in uses) _uses.AddChild(RecipeRow(r, id));
            if (uses.Count == 0) _uses.AddChild(UiTheme.Small("Ни в одном рецепте не тратится", UiTheme.Faint, UiTheme.FontSmall));
            _usesBox.Visible = !gun || uses.Count > 0;   // у ствола — место под характеристики

            foreach (var c in _rec.GetChildren()) c.QueueFree();
            var yield = Items.RecycleYield(id);
            foreach (var (yid, n) in yield) _rec.AddChild(YieldChip(yid, n));
            _rec.Visible = yield.Count > 0;
            _recBox.Visible = !gun;   // ствол на переработку не главное — экономим высоту под обвесы
            _icon.CustomMinimumSize = new Vector2(0, gun ? 110 : 150);
            if (gun && Items.Get(id).Weapon.Tier > 0) _type.Text += Items.Get(id).Weapon.Tier >= 4 ? " · элита" : $" · тир {Items.Get(id).Weapon.Tier}";
            _where.Text = yield.Count > 0 ? "Переработчик на РТ · за 1 шт." : "Не перерабатывается";
        }

        /// <summary>Ствол: износ, характеристики (урон, темп и режим, магазин, калибр, дальность, тир) и обвесы по слотам.</summary>
        void FillGun(CellView cv)
        {
            var w = Items.Get(cv.Id).Weapon;
            bool worn = cv.Durability >= 0;
            _durBar.GetParent<Control>().Visible = worn;
            if (worn)
            {
                _durBar.Value = cv.Durability; _durBar.Fill = UiTheme.DurabilityColor(cv.Durability); _durBar.QueueRedraw();
                _durText.Text = $"{Mathf.RoundToInt(cv.Durability * 100)}%";
                _durText.AddThemeColorOverride("font_color", UiTheme.DurabilityColor(cv.Durability));
            }
            foreach (var c in _gunStats.GetChildren()) c.QueueFree();
            foreach (var (label, value) in Items.Stats(cv.Id))
            {
                if (label is "Обвесы" or "Износ" or "Перезарядка" or "Тир") continue;   // обвесы — ниже, износ — полоской, тир — в подзаголовке
                string v = value;
                if (label == "Магазин" && cv.Magazine > 0) v = $"{cv.Ammo} / {cv.Magazine}";   // с обвесом магазин больше
                if (label == "Патроны") v = value.Replace("Патроны ", "");
                _gunStats.AddChild(TechWindow.Line(label == "Патроны" ? "Калибр" : label, v, UiTheme.Ink));
            }

            foreach (var c in _gunAtts.GetChildren()) c.QueueFree();
            int slots = 0;
            var free = new List<string>();
            for (int k = 0; k < Weaponry.SlotCount; k++)
            {
                if (!w.Accepts((AttSlot)k)) continue;
                slots++;
                string att = cv.Att != null && k < cv.Att.Length ? cv.Att[k] : null;
                if (att == null) { free.Add(Weaponry.SlotNames[k]); continue; }
                var row = UiTheme.HBox(8);
                var sl = UiTheme.Small(Weaponry.SlotNames[k], UiTheme.Dim, UiTheme.FontSmall);
                sl.AutowrapMode = TextServer.AutowrapMode.Off;
                sl.CustomMinimumSize = new Vector2(70, 0);
                sl.VerticalAlignment = VerticalAlignment.Center;
                row.AddChild(sl);
                row.AddChild(UiTheme.IconBox(att, 24, 0.06f, 6));
                var an = UiTheme.Strong(Items.Name(att), UiTheme.FontSmall, UiTheme.RarityColor(Items.Rarity(att)));
                an.VerticalAlignment = VerticalAlignment.Center;
                row.AddChild(an);
                _gunAtts.AddChild(row);
            }
            if (free.Count > 0) _gunAtts.AddChild(UiTheme.Small("Свободно: " + string.Join(", ", free), UiTheme.Faint, UiTheme.FontCaption));
            if (slots == 0) _gunAtts.AddChild(UiTheme.Small("Обвесы не ставятся", UiTheme.Faint, UiTheme.FontSmall));
        }

        static Control RecipeRow(Recipe r, string id)
        {
            var p = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            p.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.RowBg, UiTheme.RowRadius, null, 0, 8, 7));
            var row = UiTheme.HBox(10);
            p.AddChild(row);
            row.AddChild(UiTheme.IconBox(r.Output, 40));
            var t = UiTheme.VBox(0);
            t.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            t.AddChild(UiTheme.Strong(Items.Name(r.Output) + (r.Count > 1 ? $" ×{r.Count}" : "")));
            var other = r.Cost.Where(c => c.id != id).Select(c => $"{Items.Name(c.id).ToLower()} {c.count}");
            string bench = r.Bench > 0 ? $"у верстака {r.Bench}" : "без верстака";
            var small = UiTheme.Small(string.Join(" · ", new[] { bench }.Concat(other)), UiTheme.Dim, UiTheme.FontCaption);
            small.CustomMinimumSize = new Vector2(200, 0);
            t.AddChild(small);
            row.AddChild(t);
            int need = r.Cost.First(c => c.id == id).count;
            var n = UiTheme.Small($"×{need}", Hex(0xdddddd), UiTheme.FontBody);
            n.AutowrapMode = TextServer.AutowrapMode.Off;
            n.VerticalAlignment = VerticalAlignment.Center;
            n.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(n);
            return p;
        }

        public static Control YieldChip(string id, int n)
        {
            var p = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            p.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.RowBg, UiTheme.RowRadius, null, 0, 10, 5));
            var row = UiTheme.HBox(6);
            p.AddChild(row);
            row.AddChild(UiTheme.IconBox(id, 28, 0));
            var b = UiTheme.Strong(n.ToString(), 15);
            b.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(b);
            var s = UiTheme.Small(Items.Name(id), UiTheme.Dim, UiTheme.FontCaption);
            s.AutowrapMode = TextServer.AutowrapMode.Off;
            s.VerticalAlignment = VerticalAlignment.Center;
            s.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(s);
            return p;
        }

        /// <summary>Мини-карточка для всплывающей подсказки (когда открыт ящик и большой карточки нет).</summary>
        public static Control Mini(CellView c)
        {
            var box = UiTheme.VBox(4);
            box.CustomMinimumSize = new Vector2(260, 0);
            var head = UiTheme.HBox(10);
            head.AddChild(UiTheme.IconBox(c.Id, 44));
            var t = UiTheme.VBox(0);
            t.AddChild(UiTheme.Strong(c.Name + (c.Count > 1 ? $"  ×{c.Count}" : ""), 15));
            int rar = c.Rarity;
            t.AddChild(UiTheme.Small($"{UiTheme.RarityName(rar)} · {UiTheme.CategoryName(c.Id)}", rar > 0 ? UiTheme.RarityColor(rar) : UiTheme.Dim, UiTheme.FontCaption));
            head.AddChild(t);
            box.AddChild(head);
            string desc = Items.Desc(c.Id);
            if (c.Magazine > 0) desc = $"В магазине {c.Ammo}/{c.Magazine}. " + desc;
            if (c.Durability >= 0) desc = $"Состояние {Mathf.RoundToInt(c.Durability * 100)}%. " + desc;
            if (c.Att != null)
            {
                var atts = c.Att.Where(x => x != null).Select(Items.Name).ToList();
                if (atts.Count > 0) desc = "Обвесы: " + string.Join(", ", atts) + ". " + desc;
            }
            if (desc != "") { var d = UiTheme.Small(desc, UiTheme.Body, UiTheme.FontSmall); d.CustomMinimumSize = new Vector2(260, 0); box.AddChild(d); }
            var y = Items.RecycleYield(c.Id);
            if (y.Count > 0)
                box.AddChild(UiTheme.Small("Переработка: " + string.Join(", ", y.Select(x => $"{Items.Name(x.id).ToLower()} {x.n}")), UiTheme.Dim, UiTheme.FontCaption));
            return box;
        }

        static Color Hex(uint c) => UiTheme.Hex(c);
    }

    /// <summary>Тонкая белая полоска заполнения (ячейки рюкзака).</summary>
    public partial class ThinBar : Control
    {
        public float Value;
        public Color Fill = Colors.White;
        public ThinBar() { CustomMinimumSize = new Vector2(180, 4); MouseFilter = MouseFilterEnum.Ignore; }
        public override void _Draw() => UiTheme.DrawBar(this, new Rect2(Vector2.Zero, Size), Value, Fill, new Color(1, 1, 1, 0.15f));
    }

    /// <summary>Значок состояния: точка и подпись («Горит», «Работает», «Гниёт»).</summary>
    public partial class StateBadge : PanelContainer
    {
        readonly Label _l;
        readonly Dot _dot;
        partial class Dot : Control
        {
            public Color C;
            public override void _Draw() => UiTheme.DrawDot(this, Size / 2, 4, C);
        }
        public StateBadge()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            var row = UiTheme.HBox(7);
            AddChild(row);
            _dot = new Dot { CustomMinimumSize = new Vector2(8, 8), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
            row.AddChild(_dot);
            _l = UiTheme.Strong("", UiTheme.FontSmall);
            row.AddChild(_l);
        }
        public void Set(string text, Color c)
        {
            _l.Text = text;
            _l.AddThemeColorOverride("font_color", c);
            _dot.C = c; _dot.QueueRedraw();
            AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(c, 0.12f), 14, null, 0, 12, 5));
        }
    }

    /// <summary>
    /// Окно инвентаря (Tab) и лута (E). Слева «Снаряжение» столбиком, в центре «Рюкзак» 6×4 с фильтрами,
    /// справа карточка выбранного предмета во всю высоту. Открыт ящик/печь/шкаф — снаряжение и карточка уходят,
    /// справа от рюкзака — окно контейнера (подсказки по предметам — всплывающие мини-карточки).
    /// Внизу — шкалы и пояс (тащи туда-обратно).
    /// </summary>
    public partial class InventoryWindow : Control
    {
        /// <summary>(действие, аргумент) — уйдёт на сервер в следующем вводе.</summary>
        public event Action<UiOp, string> Op;

        static readonly string[] Filters = { "Всё", "Ресурсы", "Компоненты", "Снаряжение", "Медицина" };
        static bool Match(int f, string id)
        {
            if (f == 0 || id == null) return true;
            var cat = Items.Get(id)?.Category;
            return f switch
            {
                1 => cat == ItemCategory.Resource,
                2 => cat == ItemCategory.Component,
                3 => cat is ItemCategory.Tool or ItemCategory.Weapon or ItemCategory.Ammo or ItemCategory.Deployable,
                _ => cat is ItemCategory.Food or ItemCategory.Medical,
            };
        }

        readonly PanelContainer _gearPanel, _packPanel, _contPanel;
        readonly ItemCard _card;
        readonly Label _usedLabel, _fuses, _contTitle, _contInfo;
        readonly ThinBar _usedBar;
        readonly StateBadge _badge;
        readonly GridContainer _packGrid;
        readonly VBoxContainer _contBody;
        readonly HBoxContainer _contButtons;
        readonly HBoxContainer _chips;
        readonly Hotbar _hotbar;
        readonly List<SlotCell> _pack = new(), _cont = new();
        string _contSig, _btnSig, _extraSig;
        int _lastRevealed = -1, _filter;
        (string inv, int i) _sel = (null, -1), _hov = (null, -1);
        SelfInfo _self;
        // контейнер-специфичное
        Label _furnaceLine, _claimHours, _claimSub;
        VBoxContainer _gauges;
        HFlowContainer _upkeep;

        public InventoryWindow()
        {
            Visible = false;
            Theme = UiTheme.Theme;
            TextureFilter = TextureFilterEnum.Linear;
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;
            var dim = new ColorRect { Color = UiTheme.Backdrop, MouseFilter = MouseFilterEnum.Ignore };
            dim.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(dim);

            // окна — по центру между верхом экрана и поясом
            var area = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
            area.SetAnchorsPreset(LayoutPreset.FullRect);
            area.OffsetTop = 30; area.OffsetBottom = -(28 + UiTheme.CellSize + 16);
            AddChild(area);
            var row = UiTheme.HBox(UiTheme.PanelGap);
            area.AddChild(row);

            // ---- снаряжение ----
            _gearPanel = UiTheme.GlassPanel();
            _gearPanel.CustomMinimumSize = new Vector2(176, 0);
            row.AddChild(_gearPanel);
            var g = UiTheme.VBox(10);
            _gearPanel.AddChild(g);
            g.AddChild(UiTheme.Caption("Снаряжение"));
            foreach (var (name, ghost) in new[] { ("Голова", "icons/icon_coffeecan_helmet"), ("Торс", "icons/icon_hoodie"), ("Руки", "icons/icon_gloves"), ("Ноги", "icons/icon_pants"), ("Обувь", "icons/icon_boots") })
            {
                var eq = UiTheme.HBox(12);
                eq.AddChild(new GearSlot { Ghost = ghost, TooltipText = "Одежда — скоро", SizeFlagsVertical = SizeFlags.ShrinkCenter });
                var l = UiTheme.Small(name, UiTheme.Dim, UiTheme.FontBody);
                l.AutowrapMode = TextServer.AutowrapMode.Off;
                l.VerticalAlignment = VerticalAlignment.Center;
                l.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                eq.AddChild(l);
                g.AddChild(eq);
            }
            g.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
            g.AddChild(UiTheme.Sep());
            var armor = UiTheme.HBox();
            var al = UiTheme.Small("Защита", UiTheme.Dim, UiTheme.FontSmall);
            al.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            armor.AddChild(al);
            armor.AddChild(UiTheme.Strong("0%", UiTheme.FontSmall));
            g.AddChild(armor);

            // ---- рюкзак ----
            _packPanel = UiTheme.GlassPanel();
            row.AddChild(_packPanel);
            var p = UiTheme.VBox(14);
            _packPanel.AddChild(p);
            var top = UiTheme.HBox(12);
            p.AddChild(top);
            var title = UiTheme.Title("Рюкзак");
            title.SizeFlagsVertical = SizeFlags.ShrinkEnd;
            top.AddChild(title);
            _fuses = UiTheme.Small("", UiTheme.Accent, UiTheme.FontSmall);
            _fuses.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            top.AddChild(_fuses);
            top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
            var used = UiTheme.VBox(6);
            used.SizeFlagsVertical = SizeFlags.ShrinkEnd;
            _usedLabel = UiTheme.Small("", UiTheme.Hex(0xc9c9c9), UiTheme.FontBody);
            _usedLabel.HorizontalAlignment = HorizontalAlignment.Right;
            used.AddChild(_usedLabel);
            _usedBar = new ThinBar();
            used.AddChild(_usedBar);
            top.AddChild(used);

            _chips = UiTheme.HBox(8);
            p.AddChild(_chips);
            for (int i = 0; i < Filters.Length; i++)
            {
                int f = i;
                var b = UiTheme.Chip(Filters[i], i == 0);
                b.Pressed += () => SetFilter(f);
                _chips.AddChild(b);
            }
            _packGrid = Grid(p, 6);
            for (int i = 0; i < Actor.PackSize; i++) _pack.Add(AddCell(_packGrid, "pack", i));

            // ---- карточка предмета ----
            _card = new ItemCard();
            row.AddChild(_card);

            // ---- контейнер (вместо снаряжения и карточки) ----
            _contPanel = UiTheme.GlassPanel();
            _contPanel.CustomMinimumSize = new Vector2(6 * UiTheme.CellSize + 5 * UiTheme.CellGap + 2 * UiTheme.PanelPad, 0);
            row.AddChild(_contPanel);
            var k = UiTheme.VBox(14);
            _contPanel.AddChild(k);
            var khead = UiTheme.HBox(12);
            k.AddChild(khead);
            _contTitle = UiTheme.Title("");
            _contTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            khead.AddChild(_contTitle);
            _badge = new StateBadge { SizeFlagsVertical = SizeFlags.ShrinkCenter };
            khead.AddChild(_badge);
            _contInfo = UiTheme.Small("", UiTheme.Body, UiTheme.FontSmall);
            _contInfo.CustomMinimumSize = new Vector2(400, 0);
            k.AddChild(_contInfo);
            _contBody = UiTheme.VBox(12);
            k.AddChild(_contBody);
            k.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
            _contButtons = UiTheme.HBox(10);
            k.AddChild(_contButtons);

            // ---- низ: шкалы и пояс ----
            _hotbar = new Hotbar(true);
            _hotbar.SetAnchorsPreset(LayoutPreset.CenterBottom);
            _hotbar.GrowHorizontal = GrowDirection.Both;
            _hotbar.GrowVertical = GrowDirection.Begin;
            _hotbar.OffsetBottom = -28; _hotbar.OffsetTop = -28 - UiTheme.CellSize;
            AddChild(_hotbar);
            foreach (var c in _hotbar.Cells) Wire(c);
        }

        void SetFilter(int f)
        {
            _filter = f;
            for (int i = 0; i < _chips.GetChildCount(); i++) ((Button)_chips.GetChild(i)).ButtonPressed = i == f;
            foreach (var c in _pack) { c.Dimmed = !Match(f, c.Cell.Id); c.QueueRedraw(); }
        }

        // вещь из рюкзака или пояса утащили за окно — выбросить на землю
        public override bool _CanDropData(Vector2 atPosition, Variant data) =>
            data.VariantType == Variant.Type.String && data.AsString() is var d && (d.StartsWith("pack#") || d.StartsWith("belt#"));

        public override void _DropData(Vector2 atPosition, Variant data) => Op?.Invoke(UiOp.Drop, data.AsString());

        static GridContainer Grid(Control parent, int cols)
        {
            var g = new GridContainer { Columns = cols };
            g.AddThemeConstantOverride("h_separation", (int)UiTheme.CellGap);
            g.AddThemeConstantOverride("v_separation", (int)UiTheme.CellGap);
            parent.AddChild(g);
            return g;
        }

        void Wire(SlotCell c)
        {
            c.Moved += (s, si, d, di) => Op?.Invoke(UiOp.Move, $"{s}#{si}>{d}#{di}");
            c.Quick += (s, si) => Op?.Invoke(UiOp.Quick, $"{s}#{si}");
            c.Detach += (s, si) => Op?.Invoke(UiOp.Detach, $"{s}#{si}");
            c.Picked += (s, si) => { _sel = (s, si); MarkSelected(); };
            c.Hovered += (s, si) => { if (si >= 0) _hov = (s, si); else if (_hov.inv == s) _hov = (null, -1); };
        }

        SlotCell AddCell(Control parent, string inv, int i)
        {
            var c = new SlotCell { Inv = inv, Index = i, Cell = new CellView() };
            Wire(c);
            parent.AddChild(c);
            return c;
        }

        /// <summary>Отладка (скриншоты): выбрать ячейку «pack~3» / «belt~1».</summary>
        public void DebugSelect(string at)
        {
            var p = at.Split('~');
            if (p.Length == 2 && int.TryParse(p[1], out int i)) { _sel = (p[0], i); _hov = (null, -1); MarkSelected(); }
        }

        void MarkSelected()
        {
            foreach (var c in _pack) { bool s = _sel.inv == "pack" && _sel.i == c.Index; if (c.Selected != s) { c.Selected = s; c.QueueRedraw(); } }
        }

        CellView? CellAt((string inv, int i) at)
        {
            if (_self == null || at.inv == null || at.i < 0) return null;
            return at.inv switch
            {
                "pack" when at.i < _self.Pack.Count => _self.Pack[at.i],
                "belt" when at.i < _self.BeltCells.Count => _self.BeltCells[at.i],
                _ when at.i < _cont.Count => _cont[at.i].Cell,
                _ => null,
            };
        }

        /// <summary>Обновить по снапшоту. ui == null — только свой инвентарь (Tab).</summary>
        public void Refresh(SelfInfo self, UiView ui, float dt)
        {
            _self = self;
            int used = 0;
            for (int i = 0; i < _pack.Count && i < self.Pack.Count; i++)
            {
                if (self.Pack[i].Id != null) used++;
                if (!Same(_pack[i].Cell, self.Pack[i])) { _pack[i].Cell = self.Pack[i]; _pack[i].Dimmed = !Match(_filter, self.Pack[i].Id); _pack[i].QueueRedraw(); }
            }
            _usedLabel.Text = $"{used} / {Actor.PackSize}";
            _usedBar.Value = used / (float)Actor.PackSize;
            _usedBar.QueueRedraw();
            _fuses.Text = self.Fuses > 0 ? $"пробки {self.Fuses}/2" : "";
            _hotbar.Refresh(self);

            bool cont = ui != null;
            _gearPanel.Visible = _card.Visible = !cont;
            _contPanel.Visible = cont;
            foreach (var c in _pack) c.ShowTip = cont;
            foreach (var c in _hotbar.Cells) c.ShowTip = cont;

            if (!cont)
            {
                _contSig = null; _lastRevealed = -1;
                // ничего не выбрано — карточка первой вещи рюкзака
                if (CellAt(_sel) is not CellView cur || cur.Id == null)
                {
                    int first = self.Pack.FindIndex(c => c.Id != null);
                    if (first >= 0) { _sel = ("pack", first); MarkSelected(); }
                }
                var show = CellAt(_hov) is CellView h && h.Id != null ? h : CellAt(_sel) is CellView s && s.Id != null ? s : (CellView?)null;
                _card.Show(show);
                return;
            }
            RefreshContainer(ui);
        }

        void RefreshContainer(UiView ui)
        {
            _contTitle.Text = Cap(ui.Title);
            string inv = ui.Kind switch { UiKind.Furnace => "fur", UiKind.Claim => "claim", _ => "cont" };
            string sig = inv + ui.Kind + ui.Machine + ui.Cells.Count;
            if (sig != _contSig)
            {
                _contSig = sig;
                BuildContainerBody(ui, inv);
                _lastRevealed = -1;
                _btnSig = null;
            }

            int revealed = 0;
            for (int i = 0; i < ui.Cells.Count && i < _cont.Count; i++)
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

            // состояние и пояснение
            switch (ui.Kind)
            {
                case UiKind.Furnace:
                    if (ui.Burning) _badge.Set("Горит", UiTheme.Accent);
                    else if (ui.On) _badge.Set("Ждёт сырья", UiTheme.Dim);
                    else _badge.Set("Не горит", UiTheme.Faint);
                    _contInfo.Text = ui.Burning ? "Руда плавится, дерево прогорает в уголь" : ui.On ? "Положи руду и дерево — продолжит сама" : "Положи руду и дерево, нажми «Зажечь»";
                    if (_furnaceLine != null)
                        _furnaceLine.Text = $"Дерева хватит на {Clock(ui.FuelSeconds)} · руды на {Clock(ui.OreSeconds)}";
                    break;
                case UiKind.Recycler when ui.Machine == "lathe":
                    RefreshLathe(ui);
                    break;
                case UiKind.Recycler:
                    if (ui.On) _badge.Set("Работает", UiTheme.Good); else _badge.Set("Остановлен", UiTheme.Faint);
                    _contInfo.Text = ui.On ? "Разбирает по одной вещи раз в 2 с. Шумно — слышно издалека" : "Положи детали или ненужные вещи и нажми «Запустить»";
                    break;
                case UiKind.Claim:
                    RefreshClaim(ui);
                    break;
                default:
                    _badge.Visible = ui.Searching;
                    if (ui.Searching) _badge.Set("Обыск", UiTheme.Accent);
                    _contInfo.Text = ui.Searching ? "Обыскиваешь… редкое ищется дольше" : "";
                    break;
            }
            if (ui.Kind != UiKind.Box && ui.Kind != UiKind.Bag) _badge.Visible = true;
            _contInfo.Visible = _contInfo.Text != "";

            string bsig = $"{ui.Kind}{ui.On}";
            if (bsig != _btnSig)
            {
                _btnSig = bsig;
                foreach (var b in _contButtons.GetChildren()) b.QueueFree();
                switch (ui.Kind)
                {
                    case UiKind.Furnace:
                        Button(ui.On ? "Потушить" : "Зажечь", UiOp.Toggle, true);
                        Button("Забрать готовое", UiOp.TakeOut);
                        break;
                    case UiKind.Claim:
                        Button("Забрать всё", UiOp.TakeAll);
                        break;
                    case UiKind.Recycler:
                        Button(ui.On ? "Остановить" : "Запустить", UiOp.Toggle, true);
                        Button("Забрать всё", UiOp.TakeAll);
                        break;
                    default:
                        Button("Забрать всё", UiOp.TakeAll, true);
                        if (ui.Kind == UiKind.Box) Button("Сложить всё", UiOp.PutAll);
                        break;
                }
            }
        }

        HBoxContainer _latheJob;
        GridContainer _latheList;
        string _latheSig;

        /// <summary>Станок: состояние, текущая деталь с прогрессом, список рецептов «детали → деталь» с тем, чего не хватает.</summary>
        void RefreshLathe(UiView ui)
        {
            if (ui.Making != null) _badge.Set("Точит", UiTheme.Good);
            else if (ui.On) _badge.Set("Ждёт материалов", UiTheme.Dim);
            else _badge.Set("Остановлен", UiTheme.Faint);
            _contInfo.Text = ui.On ? "" : "Положи материалы и нажми «Запустить». Точит самую сложную деталь, на которую хватает; шумно";
            if (_latheJob == null) return;

            var have = new Dictionary<string, int>();
            foreach (var c in ui.Cells) if (c.Id != null && c.Durability < 0) have[c.Id] = have.GetValueOrDefault(c.Id) + c.Count;
            string sig = ui.Making + "|" + (int)(ui.Progress * 100) + "|" + string.Join(";", have.Select(kv => kv.Key + kv.Value)) + "|" + string.Join(";", ui.Makes.Select(m => m.can ? "1" : "0"));
            if (sig == _latheSig) return;
            _latheSig = sig;

            // текущая работа
            foreach (var c in _latheJob.GetChildren()) c.QueueFree();
            if (ui.Making != null)
            {
                _latheJob.AddChild(UiTheme.IconBox(ui.Making, 48, 0.1f, 10));
                var col = UiTheme.VBox(5);
                col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                col.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                var top = UiTheme.HBox(8);
                var nm = UiTheme.Strong("Точит: " + Items.Name(ui.Making), UiTheme.FontBody);
                nm.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                top.AddChild(nm);
                top.AddChild(UiTheme.Strong($"{Mathf.RoundToInt(ui.Progress * 100)}%", UiTheme.FontSmall, UiTheme.Accent));
                col.AddChild(top);
                col.AddChild(new ThinBar { Value = ui.Progress, Fill = UiTheme.Accent, CustomMinimumSize = new Vector2(0, 6) });
                _latheJob.AddChild(col);
            }
            else
            {
                var e = UiTheme.Small(ui.On ? "Ничего не точит — не хватает материалов ни на одну деталь" : "Станок остановлен", UiTheme.Dim, UiTheme.FontSmall);
                e.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                _latheJob.AddChild(e);
            }

            // рецепты
            foreach (var c in _latheList.GetChildren()) c.QueueFree();
            for (int i = 0; i < Weaponry.Lathe.Count; i++)
            {
                var r = Weaponry.Lathe[i];
                bool can = i < ui.Makes.Count ? ui.Makes[i].can : !r.Bench3;
                bool enough = can && r.In.All(x => have.GetValueOrDefault(x.id) >= x.n);
                bool now = r.Output == ui.Making;
                var p = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, TooltipText = Items.Desc(r.Output) };
                p.AddThemeStyleboxOverride("panel", UiTheme.Box(now ? new Color(1, 1, 1, 0.12f) : UiTheme.RowBg, UiTheme.RowRadius, now ? new Color(1, 1, 1, 0.6f) : enough ? new Color(UiTheme.Good, 0.35f) : null, 1, 8, 6));
                p.CustomMinimumSize = new Vector2(285, 0);
                var row = UiTheme.HBox(8);
                p.AddChild(row);
                var ic = (IconView)UiTheme.IconBox(r.Output, 34);
                if (!can) ic.Mod = new Color(1, 1, 1, 0.35f);
                ic.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                row.AddChild(ic);
                var col = UiTheme.VBox(2);
                col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                var head = UiTheme.HBox(6);
                var nm = UiTheme.Strong(Items.Name(r.Output), UiTheme.FontSmall, can ? UiTheme.Ink : UiTheme.Dim);
                nm.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                head.AddChild(nm);
                var tm = UiTheme.Small(r.Bench3 && !can ? "нужен верстак 3" : $"{r.Time:0} с", r.Bench3 && !can ? UiTheme.Bad : UiTheme.Faint, UiTheme.FontCaption);
                tm.AutowrapMode = TextServer.AutowrapMode.Off;
                head.AddChild(tm);
                col.AddChild(head);
                var ins = UiTheme.HBox(8);
                foreach (var (id, n) in r.In)
                {
                    int h = have.GetValueOrDefault(id);
                    var chip = UiTheme.HBox(3);
                    var ci = UiTheme.IconBox(id, 18, 0);
                    ci.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                    chip.AddChild(ci);
                    var t = UiTheme.Small(h >= n ? $"{n}" : $"{h}/{n}", h >= n ? UiTheme.Good : UiTheme.Bad, UiTheme.FontCaption);
                    t.AutowrapMode = TextServer.AutowrapMode.Off;
                    t.VerticalAlignment = VerticalAlignment.Center;
                    chip.AddChild(t);
                    chip.TooltipText = Items.Name(id);
                    ins.AddChild(chip);
                }
                col.AddChild(ins);
                row.AddChild(col);
                _latheList.AddChild(p);
            }
        }

        void RefreshClaim(UiView ui)
        {
            if (ui.Hours < 0) _badge.Set("Базы нет", UiTheme.Faint);
            else if (ui.Hours <= 0) _badge.Set(ui.Grace > 0 ? "Скоро гниёт" : "Гниёт", UiTheme.Bad);
            else _badge.Set("Защищено", UiTheme.Good);
            _contInfo.Text = "";
            if (_claimHours == null) return;
            _claimHours.Text = ui.Hours < 0 ? "—" : ui.Hours <= 0 ? "0 ч" : $"{ui.Hours:0.0} ч";
            _claimHours.AddThemeColorOverride("font_color", ui.Hours < 0 ? UiTheme.Dim : ui.Hours <= 0 ? UiTheme.Bad : ui.Hours < 2 ? UiTheme.Accent : UiTheme.Ink);
            _claimSub.Text = ui.Hours < 0 ? "Базы ещё нет — содержать нечего" : ui.Hours <= 0 ? (ui.Grace > 0 ? $"гниение через {ui.Grace / 60:0} мин — доложи ресурсов" : "постройки теряют прочность") : "запас содержания";
            string sig = string.Join(";", ui.Upkeep.Select(u => u.id + u.perHour.ToString("0.#"))) + "|" + string.Join(";", ui.Gauges.Select(gg => gg.value.ToString("0")));
            if (sig == _extraSig) return;
            _extraSig = sig;
            foreach (var c in _upkeep.GetChildren()) c.QueueFree();
            foreach (var (id, per) in ui.Upkeep)
            {
                var chip = ItemCard.YieldChip(id, 0);
                // число — расход в час
                var row = (HBoxContainer)chip.GetChild(0);
                ((Label)row.GetChild(1)).Text = per.ToString("0.#");
                ((Label)row.GetChild(2)).Text = Items.Name(id).ToLower() + " / ч";
                _upkeep.AddChild(chip);
            }
            if (ui.Upkeep.Count == 0) _upkeep.AddChild(UiTheme.Small("—", UiTheme.Faint, UiTheme.FontSmall));
            foreach (var c in _gauges.GetChildren()) c.QueueFree();
            foreach (var (label, value, max) in ui.Gauges) _gauges.AddChild(GaugeRow(label, value, max));
        }

        static Control GaugeRow(string label, float value, float max)
        {
            var box = UiTheme.VBox(5);
            var top = UiTheme.HBox();
            var l = UiTheme.Small(label, UiTheme.Dim, UiTheme.FontSmall);
            l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            top.AddChild(l);
            top.AddChild(UiTheme.Strong($"{value:0} / {max:0}", UiTheme.FontSmall));
            box.AddChild(top);
            float k = max > 0 ? value / max : 0;
            var bar = new ThinBar { Value = k, Fill = k > 0.9f ? UiTheme.Bad : k > 0.7f ? UiTheme.Accent : UiTheme.Ink, CustomMinimumSize = new Vector2(0, 6) };
            box.AddChild(bar);
            return box;
        }

        void BuildContainerBody(UiView ui, string inv)
        {
            foreach (var c in _contBody.GetChildren()) c.QueueFree();
            _cont.Clear();
            _furnaceLine = null; _claimHours = null; _extraSig = null; _latheJob = null; _latheList = null; _latheSig = null;
            _badge.Visible = true;

            switch (ui.Kind)
            {
                case UiKind.Furnace:
                {
                    // вход → выход
                    var flow = UiTheme.HBox(14);
                    _contBody.AddChild(flow);
                    var inBox = UiTheme.VBox(8);
                    inBox.AddChild(UiTheme.Caption("Сырьё и топливо"));
                    var inRow = UiTheme.HBox((int)UiTheme.CellGap);
                    inBox.AddChild(inRow);
                    flow.AddChild(inBox);
                    flow.AddChild(Arrow());
                    var outBox = UiTheme.VBox(8);
                    outBox.AddChild(UiTheme.Caption("Готово"));
                    var outRow = UiTheme.HBox((int)UiTheme.CellGap);
                    outBox.AddChild(outRow);
                    flow.AddChild(outBox);
                    for (int i = 0; i < ui.Cells.Count; i++) _cont.Add(AddCell(i < 3 ? inRow : outRow, inv, i, 80));
                    _furnaceLine = UiTheme.Small("", UiTheme.Dim, UiTheme.FontSmall);
                    _contBody.AddChild(_furnaceLine);
                    _contBody.AddChild(UiTheme.Small("Руда → металл · серная руда → сера · дерево → уголь", UiTheme.Faint, UiTheme.FontCaption));
                    break;
                }
                case UiKind.Recycler when ui.Machine == "lathe":
                {
                    // токарный станок: материалы → деталь (как печь, но рецептов много)
                    _contBody.AddChild(UiTheme.Caption("Материалы"));
                    var grid = Grid(_contBody, 6);
                    for (int i = 0; i < ui.Cells.Count; i++) _cont.Add(AddCell(grid, inv, i, 64));
                    _latheJob = UiTheme.HBox(12);
                    _latheJob.CustomMinimumSize = new Vector2(0, 48);
                    _contBody.AddChild(_latheJob);
                    _contBody.AddChild(UiTheme.Caption("Что точит станок · детали → деталь"));
                    _latheList = new GridContainer { Columns = 2 };
                    _latheList.AddThemeConstantOverride("h_separation", 8);
                    _latheList.AddThemeConstantOverride("v_separation", 6);
                    _contBody.AddChild(_latheList);
                    break;
                }
                case UiKind.Recycler:
                {
                    _contBody.AddChild(UiTheme.Caption("Что переработать"));
                    var grid = Grid(_contBody, 6);
                    for (int i = 0; i < ui.Cells.Count; i++) _cont.Add(AddCell(grid, inv, i));
                    var res = UiTheme.HBox(8);
                    res.AddChild(Arrow());
                    res.AddChild(ItemCard.YieldChip("scrap", 0));
                    res.AddChild(ItemCard.YieldChip("metal_fragments", 0));
                    foreach (var ch in res.GetChildren()) if (ch is PanelContainer pc) ((Label)((HBoxContainer)pc.GetChild(0)).GetChild(1)).Text = "";
                    var note = UiTheme.Small("детали — в лом и металл, вещи — в половину рецепта", UiTheme.Dim, UiTheme.FontCaption);
                    note.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                    note.CustomMinimumSize = new Vector2(220, 0);
                    res.AddChild(note);
                    _contBody.AddChild(res);
                    break;
                }
                case UiKind.Claim:
                {
                    var head = UiTheme.HBox(14);
                    _contBody.AddChild(head);
                    _claimHours = UiTheme.Title("", 34);
                    head.AddChild(_claimHours);
                    _claimSub = UiTheme.Small("", UiTheme.Dim, UiTheme.FontSmall);
                    _claimSub.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                    _claimSub.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    head.AddChild(_claimSub);
                    _contBody.AddChild(UiTheme.Caption("Ресурсы на содержание"));
                    var cells = UiTheme.HBox((int)UiTheme.CellGap);
                    _contBody.AddChild(cells);
                    for (int i = 0; i < ui.Cells.Count; i++) _cont.Add(AddCell(cells, inv, i));
                    _contBody.AddChild(UiTheme.Caption("Расход в час"));
                    _upkeep = new HFlowContainer();
                    _upkeep.AddThemeConstantOverride("h_separation", 8);
                    _contBody.AddChild(_upkeep);
                    _contBody.AddChild(UiTheme.Caption("Нагрузка базы"));
                    _gauges = UiTheme.VBox(10);
                    _contBody.AddChild(_gauges);
                    break;
                }
                default:
                {
                    int rows = (ui.Cells.Count + 5) / 6;
                    var scroll = new ScrollContainer
                    {
                        CustomMinimumSize = new Vector2(6 * UiTheme.CellSize + 5 * UiTheme.CellGap + (rows > 5 ? 12 : 0), Math.Min(rows, 5) * (UiTheme.CellSize + UiTheme.CellGap) - UiTheme.CellGap),
                        HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                    };
                    _contBody.AddChild(scroll);
                    var grid = Grid(scroll, 6);
                    for (int i = 0; i < ui.Cells.Count; i++) _cont.Add(AddCell(grid, inv, i));
                    break;
                }
            }
            foreach (var c in _cont) c.ShowTip = true;
        }

        SlotCell AddCell(Control parent, string inv, int i, float side)
        {
            var c = AddCell(parent, inv, i);
            c.Side = side;
            return c;
        }

        static Control Arrow()
        {
            var l = UiTheme.Small("→", UiTheme.Faint, 22);
            l.AutowrapMode = TextServer.AutowrapMode.Off;
            l.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            return l;
        }

        static string Clock(float s) => s <= 0 ? "0:00" : $"{(int)(s / 60)}:{(int)(s % 60):00}";
        static string Cap(string t) => string.IsNullOrEmpty(t) ? "" : t.Substring(0, 1) + t.Substring(1).ToLower();

        void Button(string text, UiOp op, bool primary = false)
        {
            var b = UiTheme.Btn(text, primary, 150, 40);
            b.Pressed += () => Op?.Invoke(op, null);
            _contButtons.AddChild(b);
        }

        public static bool Same(CellView a, CellView b) => a.Id == b.Id && a.Count == b.Count && a.Hidden == b.Hidden && a.Ammo == b.Ammo && a.Label == b.Label
            && Mathf.IsEqualApprox(a.Durability, b.Durability) && SameAtt(a.Att, b.Att);

        static bool SameAtt(string[] a, string[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
