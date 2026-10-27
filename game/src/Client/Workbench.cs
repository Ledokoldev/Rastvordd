using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>Общие мелочи окон: строка «подпись … значение» и замок.</summary>
    public static class UiLines
    {
        public static void DrawLock(CanvasItem c, Vector2 at, Color col)
        {
            c.DrawArc(at + new Vector2(5, 4.5f), 3.2f, Mathf.Pi, Mathf.Tau, 10, col, 1.5f, true);
            UiTheme.DrawRound(c, new Rect2(at + new Vector2(0, 4.5f), new Vector2(10, 7.5f)), col, 2);
        }

        public static void DrawStar(CanvasItem c, Vector2 center, float r, Color col, bool filled)
        {
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = -Mathf.Pi / 2 + i * Mathf.Pi / 5, rr = i % 2 == 0 ? r : r * 0.45f;
                pts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
            }
            if (filled) c.DrawColoredPolygon(pts, col);
            else { var loop = pts.Append(pts[0]).ToArray(); c.DrawPolyline(loop, col, 1.4f, true); }
        }

        public static Control Line(string label, string value, Color col)
        {
            var l = UiTheme.HBox(12);
            var a = UiTheme.Small(label, UiTheme.Dim, UiTheme.FontSmall);
            a.AutowrapMode = TextServer.AutowrapMode.Off;
            a.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            l.AddChild(a);
            var b = UiTheme.Strong(value, UiTheme.FontSmall, col);
            b.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            b.HorizontalAlignment = HorizontalAlignment.Right;
            b.CustomMinimumSize = new Vector2(Math.Min(150, UiTheme.TextWidth(value, UiTheme.FontSmall, UiTheme.Semi) + 2), 0);
            l.AddChild(b);
            return l;
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

    /// <summary>Что крафтил игрок чаще всего и избранное: user://craft_stats.json, user://craft_favs.json (в отладочных прогонах не пишется).</summary>
    public static class CraftPrefs
    {
        static Dictionary<string, int> _stats;
        static HashSet<string> _favs;
        public static int Version;
        static bool Persist => !OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--savefile") || a.StartsWith("--do=") || a.StartsWith("--simtest"));

        static string Read(string path)
        {
            try { if (!FileAccess.FileExists(path)) return null; using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read); return f?.GetAsText(); }
            catch { return null; }
        }
        static void Write(string path, string text)
        {
            if (!Persist) return;
            try { using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write); f?.StoreString(text); } catch { }
        }

        public static Dictionary<string, int> Stats
        {
            get
            {
                if (_stats != null) return _stats;
                _stats = new();
                try { var t = Read("user://craft_stats.json"); if (!string.IsNullOrEmpty(t)) _stats = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(t) ?? new(); } catch { }
                return _stats;
            }
        }
        public static HashSet<string> Favs
        {
            get
            {
                if (_favs != null) return _favs;
                _favs = new();
                try { var t = Read("user://craft_favs.json"); if (!string.IsNullOrEmpty(t)) _favs = (System.Text.Json.JsonSerializer.Deserialize<List<string>>(t) ?? new()).ToHashSet(); } catch { }
                return _favs;
            }
        }
        public static void Bump(string id, int n)
        {
            Stats[id] = Stats.GetValueOrDefault(id) + Math.Max(1, n);
            Version++;
            Write("user://craft_stats.json", System.Text.Json.JsonSerializer.Serialize(Stats));
        }
        public static void ToggleFav(string id)
        {
            if (!Favs.Remove(id)) Favs.Add(id);
            Version++;
            Write("user://craft_favs.json", System.Text.Json.JsonSerializer.Serialize(Favs.OrderBy(x => x).ToList()));
        }
        /// <summary>Отладка: наполнить пример, ничего не сохраняя.</summary>
        public static void Demo(IEnumerable<string> ids)
        {
            int k = 40;
            foreach (var id in ids) { Stats[id] = k--; if (k < 25) break; }
            foreach (var id in ids.Skip(3).Take(4)) Favs.Add(id);
            Version++;
        }
    }

    /// <summary>Вкладка окна: свой цвет, значок верстака, имя и состояние («рядом» / «закрыто»).</summary>
    public partial class WbTab : Control
    {
        public int Index;
        public string IconId, Title, Sub = "";
        public Color Col;
        public bool Active, Locked, Near;
        public event Action<int> Pressed;
        bool _hover;
        public WbTab() { CustomMinimumSize = new Vector2(196, 50); MouseFilter = MouseFilterEnum.Stop; TextureFilter = TextureFilterEnum.Linear; }
        public override void _Ready()
        {
            MouseEntered += () => { _hover = true; QueueRedraw(); };
            MouseExited += () => { _hover = false; QueueRedraw(); };
        }
        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            var bg = Active ? new Color(Col, 0.22f) : _hover ? new Color(1, 1, 1, 0.09f) : new Color(1, 1, 1, 0.04f);
            UiTheme.DrawRound(this, r, bg, 12, Active ? new Color(Col, 0.9f) : new Color(1, 1, 1, 0.07f), Active ? 2 : 1);
            if (Active) DrawRect(new Rect2(14, Size.Y - 3, Size.X - 28, 3), Col);
            else if (IconId != null) DrawRect(new Rect2(14, Size.Y - 2, Size.X - 28, 2), new Color(Col, 0.35f));
            float x = 10;
            if (IconId != null)
            {
                UiTheme.DrawIcon(this, IconId, new Rect2(x, 8, 32, 32), Locked ? new Color(1, 1, 1, 0.45f) : Colors.White);
                x += 40;
            }
            UiTheme.DrawText(this, new Vector2(x, 24), Title, 15, Active ? Col : Locked ? UiTheme.Dim : UiTheme.Ink, UiTheme.Semi);
            if (Sub != "") UiTheme.DrawText(this, new Vector2(x, 40), Sub, 11, Near ? UiTheme.Good : UiTheme.Dim);
            string key = (Index + 1).ToString();
            UiTheme.DrawText(this, new Vector2(Size.X - 16, 16), key, 11, UiTheme.Faint, UiTheme.Semi);
            if (Locked) UiLines.DrawLock(this, new Vector2(Size.X - 20, Size.Y - 22), UiTheme.Dim);
        }
        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left) { Pressed?.Invoke(Index); AcceptEvent(); }
        }
    }

    /// <summary>Лента узлов (как в Расте): линия — ряд плиток слева направо, коннекторы между соседними.</summary>
    public partial class LaneTree : Control
    {
        public const float Tile = 60, StepX = 102, LaneH = 118, PadX = 14, PadY = 10;
        public List<List<string>> Lanes = new();
        public Func<string, int> StateOf;               // 0 закрыто, 1 можно изучить, 2 изучено
        public Func<string, int> PriceOf;
        public Func<string, string> NameOf;
        public Func<string, bool> Affordable;
        public Action<string> Picked;
        public string Selected;
        public Color Accent = UiTheme.Accent;
        string _hover;

        public LaneTree() { MouseFilter = MouseFilterEnum.Stop; TextureFilter = TextureFilterEnum.Linear; }

        public void Rebuild(List<List<string>> lanes)
        {
            Lanes = lanes;
            int w = lanes.Count == 0 ? 1 : lanes.Max(l => l.Count);
            CustomMinimumSize = new Vector2(PadX * 2 + w * StepX, PadY * 2 + Math.Max(1, lanes.Count) * LaneH);
            QueueRedraw();
        }

        public static Vector2 TilePos(int lane, int i) => new(PadX + i * StepX + (StepX - Tile) / 2, PadY + lane * LaneH);

        public override void _Draw()
        {
            for (int li = 0; li < Lanes.Count; li++)
            {
                var row = new Rect2(4, PadY + li * LaneH - 6, Size.X - 8, LaneH - 6);
                UiTheme.DrawRound(this, row, new Color(1, 1, 1, 0.025f), 12);
                var lane = Lanes[li];
                for (int i = 0; i + 1 < lane.Count; i++)
                {
                    var a = TilePos(li, i) + new Vector2(Tile + 2, Tile / 2);
                    var b = TilePos(li, i + 1) + new Vector2(-2, Tile / 2);
                    int sa = StateOf(lane[i]), sb = StateOf(lane[i + 1]);
                    var c = sa == 2 ? new Color(Accent, 0.95f) : new Color(Accent, 0.28f);
                    DrawLine(a, b - new Vector2(5, 0), c, sa == 2 ? 3 : 2, true);
                    DrawColoredPolygon(new[] { b, b + new Vector2(-7, -4.5f), b + new Vector2(-7, 4.5f) }, c);
                }
                for (int i = 0; i < lane.Count; i++) DrawNode(lane[i], TilePos(li, i));
            }
        }

        void DrawNode(string id, Vector2 p)
        {
            int st = StateOf(id);
            bool hover = _hover == id, sel = Selected == id;
            var r = new Rect2(p, new Vector2(Tile, Tile));
            UiTheme.DrawSlot(this, r, hover, st == 0, sel);
            if (st == 1 && !sel) UiTheme.DrawRound(this, r, new Color(Accent, 0.10f), UiTheme.CellRadius, new Color(Accent, 0.95f), 2);
            if (st == 2 && !sel) UiTheme.DrawRound(this, r, Colors.Transparent, UiTheme.CellRadius, new Color(UiTheme.Good, 0.5f), 1);
            float isz = Tile * 0.68f;
            UiTheme.DrawIcon(this, id, new Rect2(p + new Vector2((Tile - isz) / 2, (Tile - isz) / 2), new Vector2(isz, isz)),
                st == 2 ? Colors.White : st == 1 ? new Color(1, 1, 1, 0.92f) : new Color(1, 1, 1, 0.32f));
            if (st == 2) UiTheme.DrawDot(this, p + new Vector2(Tile - 10, 10), 4, UiTheme.Good);
            else if (st == 0) UiLines.DrawLock(this, p + new Vector2(Tile - 16, 5), UiTheme.Dim);
            var font = UiTheme.Text;
            var col = st == 2 ? UiTheme.Ink : st == 1 ? UiTheme.Body : UiTheme.Dim;
            DrawMultilineString(font, p + new Vector2(-(StepX - Tile) / 2, Tile + 15), NameOf(id), HorizontalAlignment.Center, StepX, 11, 2, col);
            if (st != 2)
            {
                string pr = $"{PriceOf(id)} лома";
                UiTheme.DrawText(this, p + new Vector2(0, Tile + 47), pr, 11, Affordable(id) ? UiTheme.Body : UiTheme.Faint, null, HorizontalAlignment.Center, Tile);
            }
        }

        string At(Vector2 pos)
        {
            for (int li = 0; li < Lanes.Count; li++)
                for (int i = 0; i < Lanes[li].Count; i++)
                    if (new Rect2(TilePos(li, i) - new Vector2(4, 0), new Vector2(Tile + 8, Tile + 46)).HasPoint(pos)) return Lanes[li][i];
            return null;
        }

        public override string _GetTooltip(Vector2 atPosition)
        {
            var id = At(atPosition);
            return id == null ? "" : NameOf(id);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseMotion mm)
            {
                var h = At(mm.Position);
                if (h != _hover) { _hover = h; QueueRedraw(); }
            }
            else if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                var id = At(mb.Position);
                if (id != null) { Picked?.Invoke(id); AcceptEvent(); }
            }
        }

        public override void _Notification(int what)
        {
            if (what == NotificationMouseExit && _hover != null) { _hover = null; QueueRedraw(); }
        }
    }

    /// <summary>Названия линий слева от ленты (не прокручиваются).</summary>
    public partial class LaneTitles : Control
    {
        public const float W = 128;
        public List<(string title, string sub)> Items = new();
        public Color Accent = UiTheme.Accent;
        public LaneTitles() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(W, 0); }
        public void Set(List<(string, string)> items)
        {
            Items = items;
            CustomMinimumSize = new Vector2(W, LaneTree.PadY * 2 + Math.Max(1, items.Count) * LaneTree.LaneH);
            QueueRedraw();
        }
        public override void _Draw()
        {
            for (int i = 0; i < Items.Count; i++)
            {
                float y = LaneTree.PadY + i * LaneTree.LaneH;
                DrawRect(new Rect2(2, y - 2, 3, LaneTree.Tile + 4), Accent);
                DrawMultilineString(UiTheme.Semi, new Vector2(14, y + 14), Items[i].title, HorizontalAlignment.Left, W - 14, 13, 3, UiTheme.Ink);
                UiTheme.DrawText(this, new Vector2(14, y + LaneTree.Tile + 6), Items[i].sub, 11, UiTheme.Dim);
            }
        }
    }

    /// <summary>Одна секция «Все рецепты»: заголовок и список выходов (id предметов).</summary>
    public sealed class RecipeSection { public string Key, Title; public List<string> Ids = new(); public string Empty; public float Y; }

    /// <summary>Длинная сетка плиток «Все рецепты»: секции подряд, звезда избранного при наведении, ПКМ — тоже избранное.</summary>
    public partial class RecipeGrid : Control
    {
        public const float Tile = 62, StepX = 88, StepY = 112, HeadH = 34;
        public const int Cols = 10;
        public List<RecipeSection> Sections = new();
        public Func<string, RecipeInfo?> RecipeOf;
        public Func<string, int> BenchNow;
        public Action<string> Picked;
        public Action<string> FavToggled;
        public string Selected;
        public Color Accent = UiTheme.Accent;
        string _hover;
        (string sec, int i) _hoverKey;

        public RecipeGrid() { MouseFilter = MouseFilterEnum.Stop; TextureFilter = TextureFilterEnum.Linear; }

        public void Rebuild(List<RecipeSection> secs)
        {
            Sections = secs;
            float y = 4;
            foreach (var s in secs)
            {
                s.Y = y;
                y += HeadH;
                int rows = (s.Ids.Count + Cols - 1) / Cols;
                y += s.Ids.Count == 0 ? 30 : rows * StepY;
                y += 8;
            }
            CustomMinimumSize = new Vector2(Cols * StepX + 8, y + 8);
            QueueRedraw();
        }

        Vector2 TilePos(RecipeSection s, int i) => new(4 + (i % Cols) * StepX + (StepX - Tile) / 2, s.Y + HeadH + (i / Cols) * StepY);

        public override void _Draw()
        {
            var favs = CraftPrefs.Favs;
            foreach (var s in Sections)
            {
                UiTheme.DrawText(this, new Vector2(6, s.Y + 20), s.Title.ToUpper(), UiTheme.FontCaption, UiTheme.Dim, UiTheme.Semi);
                float tw = UiTheme.TextWidth(s.Title.ToUpper(), UiTheme.FontCaption, UiTheme.Semi);
                UiTheme.DrawText(this, new Vector2(tw + 16, s.Y + 20), s.Ids.Count.ToString(), UiTheme.FontCaption, UiTheme.Faint);
                DrawLine(new Vector2(tw + 44, s.Y + 16), new Vector2(Size.X - 8, s.Y + 16), UiTheme.Line, 1);
                if (s.Ids.Count == 0) { UiTheme.DrawText(this, new Vector2(8, s.Y + HeadH + 14), s.Empty ?? "", UiTheme.FontSmall, UiTheme.Faint); continue; }
                for (int i = 0; i < s.Ids.Count; i++) DrawTile(s, i, favs);
            }
        }

        void DrawTile(RecipeSection s, int i, HashSet<string> favs)
        {
            string id = s.Ids[i];
            var ri = RecipeOf(id);
            if (ri == null) return;
            var r = ri.Value;
            var p = TilePos(s, i);
            bool hover = _hoverKey == (s.Key, i), sel = Selected == id;
            int bench = BenchNow(id);
            bool blocked = !r.Known || (r.NeedsBench && r.Bench > bench) || !r.StationOk;
            bool ok = r.CanCraft && !blocked;
            var rect = new Rect2(p, new Vector2(Tile, Tile));
            UiTheme.DrawSlot(this, rect, hover, !ok && !sel, sel);
            float isz = Tile * 0.68f;
            UiTheme.DrawIcon(this, id, new Rect2(p + new Vector2((Tile - isz) / 2, (Tile - isz) / 2), new Vector2(isz, isz)),
                ok ? Colors.White : !r.Known ? new Color(1, 1, 1, 0.28f) : new Color(1, 1, 1, 0.5f));
            if (!r.Known) UiLines.DrawLock(this, p + new Vector2(Tile - 16, 5), UiTheme.Dim);
            else if (ok) UiTheme.DrawDot(this, p + new Vector2(Tile - 10, 10), 3.5f, UiTheme.Good);
            if (r.Station != null || (r.NeedsBench && r.Bench > 0))
            {
                string b = r.Station != null ? "Ст" : $"B{r.Bench}";
                var bc = r.Station != null ? (r.StationOk ? UiTheme.Dim : UiTheme.Bad) : (r.Bench > bench ? UiTheme.Bad : BenchColor(r.Bench));
                float bw = UiTheme.TextWidth(b, 10, UiTheme.Semi) + 8;
                UiTheme.DrawRound(this, new Rect2(p + new Vector2(Tile - bw - 3, Tile - 16), new Vector2(bw, 13)), new Color(0, 0, 0, 0.45f), 4);
                UiTheme.DrawText(this, p + new Vector2(Tile - bw + 1, Tile - 6), b, 10, bc, UiTheme.Semi);
            }
            bool fav = favs.Contains(id);
            if (fav || hover) UiLines.DrawStar(this, p + new Vector2(11, 11), 6.5f, fav ? UiTheme.Accent : new Color(1, 1, 1, 0.6f), fav);
            var col = ok ? UiTheme.Ink : r.Known ? UiTheme.Body : UiTheme.Faint;
            DrawMultilineString(UiTheme.Text, p + new Vector2(-(StepX - Tile) / 2 + 2, Tile + 15), Short(r.Name), HorizontalAlignment.Center, StepX - 4, 11, 2, col);
        }

        public static Color BenchColor(int b) => WorkbenchWindow.TierColor(b - 1);
        static string Short(string n) => n;

        (string sec, int i)? At(Vector2 pos)
        {
            foreach (var s in Sections)
                for (int i = 0; i < s.Ids.Count; i++)
                    if (new Rect2(TilePos(s, i) - new Vector2(4, 0), new Vector2(Tile + 8, Tile + 36)).HasPoint(pos)) return (s.Key, i);
            return null;
        }
        string IdAt((string sec, int i) k) => Sections.First(s => s.Key == k.sec).Ids[k.i];

        public override string _GetTooltip(Vector2 atPosition)
        {
            if (At(atPosition) is not { } k) return "";
            var id = IdAt(k);
            var ri = RecipeOf(id);
            return ri == null ? "" : $"{ri.Value.Name}\n{ri.Value.CostText}{(ri.Value.Known ? "" : "\nне изучено")}";
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseMotion mm)
            {
                var h = At(mm.Position) ?? default;
                if (h != _hoverKey) { _hoverKey = h; QueueRedraw(); }
            }
            else if (e is InputEventMouseButton mb && mb.Pressed)
            {
                if (At(mb.Position) is not { } k) return;
                var id = IdAt(k);
                var p = TilePos(Sections.First(s => s.Key == k.sec), k.i);
                if (mb.ButtonIndex == MouseButton.Right) { FavToggled?.Invoke(id); AcceptEvent(); }
                else if (mb.ButtonIndex == MouseButton.Left)
                {
                    if (new Rect2(p, new Vector2(22, 22)).HasPoint(mb.Position)) FavToggled?.Invoke(id);
                    else Picked?.Invoke(id);
                    AcceptEvent();
                }
            }
        }

        public override void _Notification(int what)
        {
            if (what == NotificationMouseExit && _hoverKey != default) { _hoverKey = default; QueueRedraw(); }
        }
    }

    /// <summary>
    /// Единое окно верстака: «Верстак 1/2/3» (дерево изучения линиями, как в Расте) и «Все рецепты» (длинная сетка).
    /// Справа общая карточка: изучение и крафт выбранного предмета рядом, очередь. E у верстака — вкладка его уровня, Q — «Все рецепты», 1–4 — переключение.
    /// </summary>
    public partial class WorkbenchWindow : Control
    {
        public event Action<int, int> Craft;    // (индекс рецепта, сколько раз)
        public event Action<string> Research;
        public event Action ClickedOutside;

        public static readonly Color[] TierCols = { UiTheme.Hex(0xd9985a), UiTheme.Hex(0x6aa9e6), UiTheme.Hex(0xea6a3c), UiTheme.Hex(0xbdbdbd) };
        public static Color TierColor(int tier) => TierCols[Math.Clamp(tier, 0, 3)];
        static readonly string[] BenchIcons = { "deploy_workbench", "deploy_workbench2", "deploy_workbench3", null };
        static readonly string[] TabNames = { "Верстак 1", "Верстак 2", "Верстак 3", "Все рецепты" };

        /// <summary>Только добыча (D46): дробовики и элита не крафтятся.</summary>
        static readonly HashSet<string> LootOnly = new() { "weapon_pipeshotgun", "weapon_sawnoff", "weapon_mp133", "weapon_saiga12", "weapon_mosin", "weapon_aps" };
        static readonly string[] Cats = { "Строительство", "Инструменты", "Оружие", "Патроны", "Обвесы", "Броня и одежда", "Медицина", "Станции", "Разное" };

        static string CatOf(string id)
        {
            if (StationDefs.ByItem(id) != null || id.StartsWith("deploy_workbench") || id == "deploy_furnace") return "Станции";
            if (id.StartsWith("tool_") || id.StartsWith("flask_") || id.StartsWith("canister_")) return "Инструменты";
            if (id.StartsWith("weapon_") || id.StartsWith("expl_")) return "Оружие";
            if (id.StartsWith("ammo_") || id == "arrow") return "Патроны";
            if (id.StartsWith("att_")) return "Обвесы";
            var cat = Items.Get(id)?.Category;
            if (cat == ItemCategory.Clothing) return "Броня и одежда";
            if (cat == ItemCategory.Medical) return "Медицина";
            if (id.StartsWith("deploy_") || id.StartsWith("lock_") || id == "bars" || id == "build_plan") return "Строительство";
            return "Разное";
        }

        public int FakeBench = -1;   // отладка: показывать, будто рядом верстак N
        public int Tab { get; private set; }

        readonly PanelContainer _left, _right;
        readonly HBoxContainer _tabsRow;
        readonly WbTab[] _tabs = new WbTab[4];
        readonly Control _headIcon;
        readonly Label _title, _sub, _scrap;
        readonly Control _benchBox, _allBox;
        readonly LaneTree _tree;
        readonly LaneTitles _laneTitles;
        readonly ScrollContainer _treeScroll, _gridScroll;
        readonly RecipeGrid _grid;
        readonly HFlowContainer _chips;
        readonly Label _hint;

        readonly BigIcon _bigIcon;
        readonly Label _name, _type, _desc;
        readonly VBoxContainer _body;
        readonly HBoxContainer _queue;
        readonly Label _queueTitle;
        readonly ScrollContainer _rightScroll;

        List<RecipeInfo> _recipes = new();
        List<TechInfo> _tech = new();
        readonly Dictionary<string, TechInfo> _techBy = new();
        int _bench, _scrapHave, _amount = 1;
        string _sel, _cardSig, _queueSig, _gridSig;

        public WorkbenchWindow()
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
            var row = UiTheme.HBox(UiTheme.PanelGap - 10);
            center.AddChild(row);

            // ---- левая панель: вкладки, шапка, содержимое ----
            _left = UiTheme.GlassPanel(18);
            _left.CustomMinimumSize = new Vector2(Cols(), 0);
            row.AddChild(_left);
            var lbox = UiTheme.VBox(10);
            _left.AddChild(lbox);
            _tabsRow = UiTheme.HBox(8);
            lbox.AddChild(_tabsRow);
            for (int i = 0; i < 4; i++)
            {
                var t = new WbTab { Index = i, IconId = BenchIcons[i], Title = TabNames[i], Col = TierColor(i) };
                t.Pressed += SetTab;
                _tabs[i] = t;
                _tabsRow.AddChild(t);
            }
            // шапка
            var head = UiTheme.HBox(12);
            lbox.AddChild(head);
            _headIcon = new HeadIcon { CustomMinimumSize = new Vector2(52, 52) };
            head.AddChild(_headIcon);
            var hv = UiTheme.VBox(0);
            hv.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            hv.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            head.AddChild(hv);
            _title = UiTheme.Title("", 24);
            hv.AddChild(_title);
            _sub = UiTheme.Small("", UiTheme.Dim, UiTheme.FontSmall);
            _sub.AutowrapMode = TextServer.AutowrapMode.Off;
            hv.AddChild(_sub);
            var chip = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = "Металлолом" };
            chip.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.RowBg, UiTheme.RowRadius, null, 0, 10, 4));
            var sc = UiTheme.HBox(6);
            chip.AddChild(sc);
            sc.AddChild(UiTheme.IconBox("scrap", 26, 0));
            _scrap = UiTheme.Strong("", UiTheme.FontBody);
            _scrap.VerticalAlignment = VerticalAlignment.Center;
            sc.AddChild(_scrap);
            head.AddChild(chip);

            // содержимое вкладок верстака
            _benchBox = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            ((HBoxContainer)_benchBox).AddThemeConstantOverride("separation", 0);
            lbox.AddChild(_benchBox);
            _laneTitles = new LaneTitles();
            _benchBox.AddChild(_laneTitles);
            _treeScroll = new ScrollContainer { CustomMinimumSize = new Vector2(Cols() - 36 - LaneTitles.W, 4 * LaneTree.LaneH + 42), VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
            _benchBox.AddChild(_treeScroll);
            _tree = new LaneTree
            {
                StateOf = NodeState, NameOf = NodeName,
                PriceOf = id => _techBy.TryGetValue(id, out var t) ? t.Scrap : 0,
                Affordable = id => _techBy.TryGetValue(id, out var t) && _scrapHave >= t.Scrap,
                Picked = id => Select(id),
            };
            _treeScroll.AddChild(_tree);

            // «Все рецепты»
            _allBox = UiTheme.VBox(8);
            lbox.AddChild(_allBox);
            _chips = new HFlowContainer();
            _chips.AddThemeConstantOverride("h_separation", 6);
            _chips.AddThemeConstantOverride("v_separation", 6);
            _allBox.AddChild(_chips);
            _gridScroll = new ScrollContainer { CustomMinimumSize = new Vector2(Cols() - 36, 4 * LaneTree.LaneH - 4), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            _allBox.AddChild(_gridScroll);
            _grid = new RecipeGrid
            {
                RecipeOf = id => _recipes.Any(r => r.Output == id) ? _recipes.First(r => r.Output == id) : null,
                BenchNow = _ => _bench,
                Picked = id => Select(id),
                FavToggled = id => { CraftPrefs.ToggleFav(id); _gridSig = null; },
            };
            _gridScroll.AddChild(_grid);

            _hint = UiTheme.Small("", UiTheme.Faint, UiTheme.FontCaption);
            _hint.AutowrapMode = TextServer.AutowrapMode.Off;
            lbox.AddChild(_hint);

            // ---- правая карточка ----
            _right = UiTheme.GlassPanel(18);
            _right.CustomMinimumSize = new Vector2(ItemCard.W + 16, 0);
            row.AddChild(_right);
            var rb = UiTheme.VBox(6);
            _right.AddChild(rb);
            _bigIcon = new BigIcon { CustomMinimumSize = new Vector2(0, 92) };
            rb.AddChild(_bigIcon);
            _name = UiTheme.Title("", 21);
            rb.AddChild(_name);
            _type = UiTheme.Small("", UiTheme.Dim, UiTheme.FontSmall);
            rb.AddChild(_type);
            _rightScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(ItemCard.W - 4, 300) };
            rb.AddChild(_rightScroll);
            var sv = UiTheme.VBox(6);
            sv.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _rightScroll.AddChild(sv);
            _desc = UiTheme.Small("", UiTheme.Body, UiTheme.FontSmall);
            _desc.CustomMinimumSize = new Vector2(ItemCard.W - 24, 0);
            sv.AddChild(_desc);
            _body = UiTheme.VBox(6);
            sv.AddChild(_body);
            rb.AddChild(UiTheme.Sep());
            _queueTitle = UiTheme.Caption("Очередь");
            rb.AddChild(_queueTitle);
            _queue = UiTheme.HBox(6);
            _queue.CustomMinimumSize = new Vector2(0, 44);
            rb.AddChild(_queue);

            SetTab(0);
        }

        static float Cols() => 1010;

        // отдельный контрол: значок верстака в цветной плашке
        partial class HeadIcon : Control
        {
            public string Id; public Color Col = UiTheme.Accent;
            public HeadIcon() { MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Linear; }
            public override void _Draw()
            {
                UiTheme.DrawRound(this, new Rect2(Vector2.Zero, Size), new Color(Col, 0.18f), 14, new Color(Col, 0.8f), 2);
                if (Id != null) UiTheme.DrawIcon(this, Id, new Rect2(Vector2.Zero, Size).Grow(-8));
            }
        }

        public override void _GuiInput(InputEvent e)
        {
            if (UiTheme.ClickOutside(this, e)) { AcceptEvent(); ClickedOutside?.Invoke(); }
        }

        // ---------- вкладки ----------
        public void OpenTab(int tab)
        {
            Visible = true;
            SetTab(tab);
        }

        public void SetTab(int tab)
        {
            tab = Math.Clamp(tab, 0, 3);
            bool changed = tab != Tab || _tabs[tab] == null || !_tabs[tab].Active;
            Tab = tab;
            var col = TierColor(tab);
            for (int i = 0; i < 4; i++) _tabs[i].Active = i == tab;
            foreach (var t in _tabs) t.QueueRedraw();
            _benchBox.Visible = tab < 3;
            _allBox.Visible = tab == 3;
            var gp = Glass(col);
            _left.AddThemeStyleboxOverride("panel", gp);
            ((HeadIcon)_headIcon).Col = col; ((HeadIcon)_headIcon).Id = BenchIcons[tab]; _headIcon.Visible = tab < 3; _headIcon.QueueRedraw();
            _title.AddThemeColorOverride("font_color", col);
            _tree.Accent = col; _laneTitles.Accent = col; _grid.Accent = col;
            if (tab < 3) BuildLanes(tab + 1);
            else BuildChips();
            _cardSig = null; _gridSig = null;
            if (changed) PickDefault();
        }

        static StyleBoxFlat Glass(Color acc)
        {
            var st = UiTheme.Glass(18);
            st.BgColor = new Color(UiTheme.PanelBg.Lerp(acc, 0.07f), Mathf.Max(0.82f, UiTheme.PanelBg.A));
            st.BorderColor = new Color(acc, 0.75f);
            st.SetBorderWidthAll(2);
            return st;
        }

        void PickDefault()
        {
            if (Tab < 3)
            {
                var first = _tree.Lanes.SelectMany(l => l).FirstOrDefault(id => NodeState(id) == 1) ?? _tree.Lanes.SelectMany(l => l).FirstOrDefault();
                if (first != null) { _sel = first; _tree.Selected = first; }
            }
            else if (_sel == null || !_recipes.Any(r => r.Output == _sel))
            {
                _sel = _recipes.FirstOrDefault(r => !LootOnly.Contains(r.Output)).Output ?? _sel;
            }
            _tree.Selected = Tab < 3 ? _sel : null;
            _grid.Selected = Tab == 3 ? _sel : null;
        }

        void Select(string id)
        {
            _sel = id; _amount = 1; _cardSig = null;
            _tree.Selected = Tab < 3 ? id : null; _grid.Selected = Tab == 3 ? id : null;
            _tree.QueueRedraw(); _grid.QueueRedraw();
        }

        // ---------- данные дерева ----------
        static string NodeName(string id)
        {
            var n = Clothing.TechName(id) ?? Items.Name(id);
            int p = n.IndexOf('(');
            return p > 0 ? n[..p].Trim() : n;
        }

        int NodeState(string id)
        {
            if (!_techBy.TryGetValue(id, out var t)) return 0;
            if (t.Known) return 2;
            bool pre = (t.Requires ?? Array.Empty<string>()).All(r => _techBy.TryGetValue(r, out var p) ? p.Known : true);
            var any = Tech.Get(id)?.RequiresAny ?? Array.Empty<string>();
            if (any.Length > 0 && !any.Any(r => _techBy.TryGetValue(r, out var p) && p.Known)) pre = false;
            return pre && _bench >= t.Bench ? 1 : 0;
        }

        void BuildLanes(int tier)
        {
            var lanes = new List<List<string>>();
            var titles = new List<(string, string)>();
            var nodes = Tech.All.Where(n => n.Bench == tier).ToList();
            for (int ln = 0; ln < Prog.LaneNames.Length; ln++)
            {
                var ids = nodes.Where(n => n.Lane == ln).OrderBy(n => n.Order).Select(n => n.Id).ToList();
                if (ids.Count == 0) continue;
                lanes.Add(ids);
                titles.Add((Prog.LaneNames[ln], ""));
            }
            var rest = nodes.Where(n => n.Lane < 0).Select(n => n.Id).ToList();
            if (rest.Count > 0) { lanes.Add(rest); titles.Add(("Прочее", "")); }
            _tree.Rebuild(lanes);
            _laneTitles.Set(titles);
            _treeScroll.ScrollHorizontal = 0;
        }

        void UpdateLaneSubs()
        {
            var subs = new List<(string, string)>();
            for (int i = 0; i < _tree.Lanes.Count; i++)
            {
                int k = _tree.Lanes[i].Count(id => NodeState(id) == 2);
                subs.Add((_laneTitles.Items.Count > i ? _laneTitles.Items[i].title : "", $"{k} / {_tree.Lanes[i].Count} изучено"));
            }
            if (subs.Count == _laneTitles.Items.Count && subs.Zip(_laneTitles.Items).Any(z => z.First.Item2 != z.Second.sub)) { _laneTitles.Items = subs; _laneTitles.QueueRedraw(); }
        }

        void BuildChips()
        {
            foreach (var c in _chips.GetChildren()) { _chips.RemoveChild(c); c.QueueFree(); }
            var names = new List<string> { "Избранное", "Часто крафтишь" };
            names.AddRange(Cats);
            foreach (var n in names)
            {
                var b = UiTheme.Chip(n);
                b.ToggleMode = false;
                string key = n;
                b.Pressed += () => { var s = _grid.Sections.FirstOrDefault(x => x.Title == key); if (s != null) _gridScroll.ScrollVertical = (int)s.Y; };
                _chips.AddChild(b);
            }
        }

        void BuildGrid()
        {
            var favs = CraftPrefs.Favs;
            var avail = _recipes.Where(r => !LootOnly.Contains(r.Output)).Select(r => r.Output).Distinct().ToList();
            var secs = new List<RecipeSection>
            {
                new() { Key = "fav", Title = "Избранное", Ids = avail.Where(favs.Contains).ToList(), Empty = "Нажми ★ на плитке или правую кнопку мыши — рецепт окажется здесь" },
                new()
                {
                    Key = "freq", Title = "Часто крафтишь",
                    Ids = CraftPrefs.Stats.Where(kv => kv.Value > 0 && avail.Contains(kv.Key)).OrderByDescending(kv => kv.Value).Take(14).Select(kv => kv.Key).ToList(),
                    Empty = "Здесь появятся рецепты, которые ты крафтишь чаще всего",
                },
            };
            foreach (var c in Cats) secs.Add(new RecipeSection { Key = c, Title = c, Ids = avail.Where(id => CatOf(id) == c).ToList() });
            secs.RemoveAll(s => s.Ids.Count == 0 && s.Key != "fav" && s.Key != "freq");
            _grid.Rebuild(secs);
        }

        // ---------- обновление ----------
        public void Refresh(SelfInfo s)
        {
            _recipes = s.Recipes;
            _tech = s.Tech;
            _techBy.Clear();
            foreach (var t in _tech) _techBy[t.Id] = t;
            _bench = FakeBench >= 0 ? FakeBench : s.BenchLevel;
            _scrapHave = s.Scrap;
            if (_demo && _recipes.Count > 0) ApplyDemo();
            _scrap.Text = $"{s.Scrap}";

            for (int i = 0; i < 3; i++)
            {
                _tabs[i].Locked = _bench < i + 1;
                _tabs[i].Near = _bench >= i + 1;
                _tabs[i].Sub = _bench >= i + 1 ? "рядом ✓" : _bench == 0 ? "нет рядом" : "нужен выше";
                _tabs[i].QueueRedraw();
            }
            _tabs[3].Sub = "крафт и избранное";
            _tabs[3].QueueRedraw();

            if (Tab < 3)
            {
                int tier = Tab + 1;
                _title.Text = $"Верстак {tier}";
                var tn = Tech.All.Where(n => n.Bench == tier).ToList();
                int known = tn.Count(n => _techBy.TryGetValue(n.Id, out var t) && t.Known);
                bool here = _bench >= tier;
                _sub.Text = here ? $"Рядом — можно изучать и крафтить · изучено {known} из {tn.Count}"
                    : _bench == 0 ? $"Верстака нет рядом — смотреть можно, изучать и крафтить нельзя · изучено {known} из {tn.Count}"
                    : $"Нужен верстак {tier} рядом (сейчас {_bench}) — изучение и крафт закрыты · изучено {known} из {tn.Count}";
                _sub.AddThemeColorOverride("font_color", here ? UiTheme.Good : UiTheme.Bad);
                UpdateLaneSubs();
                _tree.QueueRedraw();
                _hint.Text = "Клик — выбрать узел · колесо мыши — прокрутка вправо · 1–4 — вкладки · Esc — закрыть";
                if (_tree.GetParent() == _treeScroll && Input.IsKeyPressed(Key.Shift)) { }
            }
            else
            {
                _title.Text = "Все рецепты";
                _sub.Text = _bench > 0 ? $"Верстак {_bench} рядом · рецепты с замком ещё не изучены" : "Верстака рядом нет — доступно то, что делается в руках";
                _sub.AddThemeColorOverride("font_color", UiTheme.Dim);
                string sig = _recipes.Count + "|" + CraftPrefs.Version + "|" + _bench;
                if (sig != _gridSig)
                {
                    _gridSig = sig;
                    BuildGrid();
                    if (_sel == null || !_recipes.Any(r => r.Output == _sel)) PickDefault();
                }
                _grid.QueueRedraw();
                _hint.Text = "Клик — выбрать · ★ или ПКМ — в избранное · B1–B3 — нужный верстак · 1–4 — вкладки · Q/Esc — закрыть";
            }

            RefreshCard(s);
            RefreshQueue(s);
        }

        RecipeInfo? RecipeFor(string id)
        {
            foreach (var r in _recipes) if (r.Output == id) return r;
            return null;
        }

        bool Blocked(RecipeInfo r) => !r.Known || (r.NeedsBench && r.Bench > _bench) || !r.StationOk;

        int MaxTimes(RecipeInfo r)
        {
            if (Blocked(r) || r.Costs == null) return 0;
            int m = 99;
            foreach (var (_, n, have) in r.Costs) m = Math.Min(m, n > 0 ? have / n : 99);
            return m;
        }

        void RefreshCard(SelfInfo s)
        {
            if (_sel == null) return;
            string id = _sel;
            string nodeId = Clothing.TechKey(id) ?? id;
            var node = Tech.Get(nodeId);
            _techBy.TryGetValue(nodeId, out var ti);
            var rec = RecipeFor(id);
            int rar = Items.Rarity(id);
            if (_bigIcon.Id != id) { _bigIcon.Id = id; _bigIcon.Rarity = rar; _bigIcon.QueueRedraw(); }
            _name.Text = Clothing.TechName(id) ?? Items.Name(id);
            _type.Text = $"{UiTheme.RarityName(rar)} · {UiTheme.CategoryName(id)}";
            _type.AddThemeColorOverride("font_color", rar > 0 ? UiTheme.RarityColor(rar) : UiTheme.Dim);
            _desc.Text = Items.Desc(id);

            string sig = id + "|" + _amount + "|" + _bench + "|" + s.Scrap + "|" + (node != null && ti.Id != null ? ti.Known + "" + ti.CanResearch : "-")
                + "|" + (rec == null ? "-" : rec.Value.Known + "" + rec.Value.CanCraft + rec.Value.StationOk + string.Join(";", rec.Value.Costs.Select(c => c.id + c.have)));
            if (sig == _cardSig) return;
            _cardSig = sig;
            foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }

            var acc = TierColor(Tab < 3 ? Tab : Math.Max(0, (node?.Bench ?? rec?.Bench ?? 1) - 1));
            // характеристики (не больше пяти строк)
            var stats = Items.Stats(id);
            if (stats.Count > 0)
            {
                var sb = UiTheme.VBox(2);
                foreach (var (label, value) in stats.Take(5)) sb.AddChild(UiLines.Line(label, value, UiTheme.Ink));
                _body.AddChild(sb);
            }

            // ----- изучение -----
            if (node != null && ti.Id != null)
            {
                _body.AddChild(Section("Изучение", acc));
                if (ti.Known) _body.AddChild(UiTheme.Small("Изучено ✓ — рецепт открыт", UiTheme.Good, UiTheme.FontSmall));
                else
                {
                    bool nearOk = _bench >= node.Bench;
                    _body.AddChild(UiLines.Line($"Верстак {node.Bench}", nearOk ? "рядом ✓" : _bench == 0 ? "нет рядом" : $"сейчас {_bench}", nearOk ? UiTheme.Good : UiTheme.Bad));
                    foreach (var r in node.Requires)
                    {
                        bool k = _techBy.TryGetValue(r, out var pt) && pt.Known;
                        _body.AddChild(UiLines.Line(NodeName(r), k ? "изучено ✓" : "не изучено", k ? UiTheme.Good : UiTheme.Bad));
                    }
                    _body.AddChild(UiLines.Line("Металлолом", $"{s.Scrap} / {node.Scrap}", s.Scrap >= node.Scrap ? UiTheme.Good : UiTheme.Bad));
                    var btn = UiTheme.Btn($"Изучить — {node.Scrap} лома", true, 0, 42);
                    btn.AddThemeFontSizeOverride("font_size", UiTheme.FontBody);
                    btn.Disabled = !ti.CanResearch;
                    string nid = node.Id;
                    btn.Pressed += () => Research?.Invoke(nid);
                    _body.AddChild(btn);
                    if (!ti.CanResearch && !string.IsNullOrEmpty(ti.Why)) _body.AddChild(Reason(Capital(ti.Why), UiTheme.Bad));
                }
            }
            else if (node == null) _body.AddChild(UiTheme.Small("Известно с начала — изучать не нужно", UiTheme.Dim, UiTheme.FontSmall));

            // ----- крафт -----
            if (rec != null)
            {
                var r = rec.Value;
                _body.AddChild(Section("Крафт", acc));
                var flow = new HFlowContainer();
                flow.AddThemeConstantOverride("h_separation", 6);
                flow.AddThemeConstantOverride("v_separation", 6);
                foreach (var (cid, n, have) in r.Costs)
                {
                    var p = new PanelContainer { TooltipText = Items.Name(cid), MouseFilter = MouseFilterEnum.Stop };
                    p.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.RowBg, UiTheme.RowRadius, null, 0, 6, 4));
                    var h = UiTheme.HBox(6);
                    p.AddChild(h);
                    h.AddChild(UiTheme.IconBox(cid, 26, 0));
                    int need = n * _amount;
                    var cnt = UiTheme.Strong($"{have}/{need}", UiTheme.FontSmall, have >= need ? UiTheme.Good : UiTheme.Bad);
                    cnt.VerticalAlignment = VerticalAlignment.Center;
                    h.AddChild(cnt);
                    flow.AddChild(p);
                }
                _body.AddChild(flow);
                _body.AddChild(UiTheme.Small($"Время: {r.Time * _amount:0} с" + (r.Count > 1 ? $" · получишь {r.Count * _amount} шт." : "") + (r.Boosted ? " · станция рядом: быстрее" : ""), UiTheme.Dim, UiTheme.FontSmall));

                var amount = UiTheme.HBox(6);
                Button Small(string t, Action a, float w = 36)
                {
                    var b = UiTheme.Btn(t, false, w, 34);
                    b.Pressed += () => { a(); _cardSig = null; };
                    amount.AddChild(b);
                    return b;
                }
                Small("−", () => _amount = Math.Max(1, _amount - 1));
                var al = UiTheme.Strong($"×{_amount}", 16);
                al.CustomMinimumSize = new Vector2(46, 0);
                al.HorizontalAlignment = HorizontalAlignment.Center;
                al.VerticalAlignment = VerticalAlignment.Center;
                amount.AddChild(al);
                Small("+", () => _amount = Math.Min(99, _amount + 1));
                Small("Макс", () => _amount = Math.Max(1, MaxTimes(r)), 0).SizeFlagsHorizontal = SizeFlags.ExpandFill;
                _body.AddChild(amount);
                var cb = UiTheme.Btn(_amount > 1 ? $"Скрафтить ×{_amount}" : "Скрафтить", true, 0, 42);
                cb.AddThemeFontSizeOverride("font_size", UiTheme.FontBody);
                cb.Disabled = MaxTimes(r) < _amount;
                int ridx = r.Index, amt = _amount; string outId = r.Output;
                cb.Pressed += () => { Craft?.Invoke(ridx, amt); CraftPrefs.Bump(outId, amt); _amount = 1; _cardSig = null; _gridSig = null; };
                _body.AddChild(cb);
                string why = !r.Known ? $"Сначала изучи «{NodeName(nodeId)}» — кнопка выше"
                    : r.Station != null && !r.StationOk ? $"Нужен {r.Station.ToLower()} рядом"
                    : r.NeedsBench && r.Bench > _bench ? $"Нужен верстак {r.Bench} рядом" + (_bench > 0 ? $" (сейчас {_bench})" : " — подойди к нему")
                    : MaxTimes(r) < _amount ? "Не хватает ресурсов" : "";
                if (why != "") _body.AddChild(Reason(why, UiTheme.Bad));
            }
            else if (node != null) _body.AddChild(UiTheme.Small("Это открывает рецепт, а не предмет — крафт в списке выше по цепочке", UiTheme.Faint, UiTheme.FontCaption));
        }

        static string Capital(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

        static Control Section(string text, Color acc)
        {
            var v = UiTheme.VBox(3);
            var sp = new Control { CustomMinimumSize = new Vector2(0, 4) };
            v.AddChild(sp);
            var h = UiTheme.HBox(8);
            var bar = new ColorRect { Color = acc, CustomMinimumSize = new Vector2(3, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            h.AddChild(bar);
            var l = UiTheme.Strong(text.ToUpper(), UiTheme.FontCaption, UiTheme.Dim);
            h.AddChild(l);
            v.AddChild(h);
            return v;
        }

        Label Reason(string t, Color c)
        {
            var l = UiTheme.Small(t, c, UiTheme.FontCaption);
            l.CustomMinimumSize = new Vector2(ItemCard.W - 24, 0);
            return l;
        }

        void RefreshQueue(SelfInfo s)
        {
            string qsig = string.Join(";", s.CraftQueue.ConvertAll(q => q.id));
            if (qsig != _queueSig)
            {
                _queueSig = qsig;
                foreach (var c in _queue.GetChildren()) { _queue.RemoveChild(c); c.QueueFree(); }
                foreach (var (id, name, _) in s.CraftQueue) _queue.AddChild(new QueueCell { Id = id, TooltipText = name });
                if (s.CraftQueue.Count == 0) { var e = UiTheme.Small("пусто", UiTheme.Faint, UiTheme.FontSmall); e.AutowrapMode = TextServer.AutowrapMode.Off; _queue.AddChild(e); }
                _queueTitle.Text = s.CraftQueue.Count > 0 ? $"Очередь · {s.CraftQueue.Count}" : "Очередь";
            }
            if (s.CraftQueue.Count > 0 && _queue.GetChildCount() > 0 && _queue.GetChild(0) is QueueCell first)
            {
                first.Progress = s.CraftQueue[0].progress;
                first.QueueRedraw();
            }
        }

        /// <summary>Отладка: плитки «часто» и «избранное» для снимка.</summary>
        bool _demo;
        public void DemoPrefs() { _demo = true; }
        void ApplyDemo() { _demo = false; CraftPrefs.Demo(_recipes.Where(r => r.Known).Select(r => r.Output).Concat(_recipes.Select(r => r.Output)).Distinct()); _gridSig = null; }
    }
}
