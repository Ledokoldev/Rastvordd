using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>
    /// Постройки на клиенте: по снапшоту создаёт виды (пол фундамента, стены, двери), держит список стен
    /// для теней обзора и акустики. Стены — обычные WallView2D прямо в мире, чтобы правильно сортировались по глубине.
    /// </summary>
    public sealed class BuildLayer
    {
        readonly Node2D _world;
        readonly Dictionary<int, (StructSnap snap, List<Node2D> views)> _items = new();
        readonly HashSet<int> _seen = new();
        public readonly List<WallView2D> Walls = new();
        public readonly List<WallInfo> Occluders = new();
        int _version = -1;

        public BuildLayer(Node2D world) => _world = world;

        /// <summary>true — состав построек поменялся (нужно пересобрать тени обзора и акустику).</summary>
        public bool Sync(List<StructSnap> list, int version)
        {
            bool changed = false;
            _seen.Clear();
            foreach (var s in list)
            {
                _seen.Add(s.Id);
                if (_items.TryGetValue(s.Id, out var cur))
                {
                    if (cur.snap.Tier == s.Tier && cur.snap.Open == s.Open && cur.snap.Kind == s.Kind)
                    {
                        if (Mathf.Abs(cur.snap.Hp - s.Hp) > 0.01f) { SetDamage(cur.views, s.Hp); _items[s.Id] = (s, cur.views); }
                        continue;
                    }
                    Free(cur.views);
                }
                _items[s.Id] = (s, Create(s));
                changed = true;
            }
            List<int> gone = null;
            foreach (var id in _items.Keys) if (!_seen.Contains(id)) (gone ??= new()).Add(id);
            if (gone != null) { foreach (var id in gone) { Free(_items[id].views); _items.Remove(id); } changed = true; }
            if (changed || version != _version) { _version = version; RebuildOccluders(); }
            return changed;
        }

        void Free(List<Node2D> views)
        {
            foreach (var v in views) { if (v is WallView2D w) Walls.Remove(w); v.QueueFree(); }
        }

        static void SetDamage(List<Node2D> views, float hp)
        {
            // повреждённое темнеет
            float k = 0.55f + 0.45f * Mathf.Clamp(hp, 0, 1);
            foreach (var v in views) v.SelfModulate = new Color(k, k, k);
        }

        List<Node2D> Create(StructSnap s)
        {
            var views = new List<Node2D>();
            if (s.Kind == StructKind.Foundation)
            {
                var f = new FloorView2D(s.I, s.J, s.Tier);
                _world.AddChild(f);
                views.Add(f);
            }
            else
            {
                foreach (var (min, max, h, _) in Building.Boxes(s.Kind, s.I, s.J, s.Dir, s.Open))
                {
                    var w = new WallView2D(new WallInfo { Min = min, Max = max, Height = h, Kind = WallKind.Built, Tier = s.Tier, Door = s.Kind == StructKind.Door, StructId = s.Id });
                    _world.AddChild(w);
                    views.Add(w);
                    Walls.Add(w);
                }
            }
            SetDamage(views, s.Hp);
            return views;
        }

        readonly HashSet<(int, int)> _floors = new();

        void RebuildOccluders()
        {
            _floors.Clear();
            foreach (var (snap, _) in _items.Values) if (snap.Kind == StructKind.Foundation) _floors.Add((snap.I, snap.J));
            Occluders.Clear();
            Acoustics.DynamicWalls.Clear();
            foreach (var (snap, _) in _items.Values)
                if (snap.Kind != StructKind.Foundation)
                    foreach (var (min, max, h, pass) in Building.Boxes(snap.Kind, snap.I, snap.J, snap.Dir, snap.Open))
                    {
                        Occluders.Add(new WallInfo { Min = min, Max = max, Height = h, Kind = WallKind.Built });
                        if (!pass) Acoustics.DynamicWalls.Add((min, max));
                    }
        }

        /// <summary>Стены, закрывающие персонажа (он стоит за ними), полупрозрачны.</summary>
        public void UpdateFade(Vector2 selfPx, float dt)
        {
            // внутри дома (стоим на фундаменте) — стены ниже по экрану поблизости срезаются
            var (ci, cj) = Building.CellOf(new SVec(selfPx.X / Iso.PPM, selfPx.Y / Iso.PPM));
            bool inside = _floors.Contains((ci, cj));
            float near = 12f * Iso.PPM;
            foreach (var w in Walls)
            {
                bool cut = inside && w.Position.Y > selfPx.Y && w.Position.Y - selfPx.Y < near && Mathf.Abs(w.Position.X - selfPx.X) < near;
                w.SetCut(cut);
                w.SetFade(!cut && w.Covers(selfPx), dt);
            }
        }
    }

    /// <summary>Пол фундамента: клетка 2×2 м, чуть приподнят (видна кромка).</summary>
    public partial class FloorView2D : Node2D
    {
        readonly int _tier;
        const float Lip = 4;

        public FloorView2D(int i, int j, int tier)
        {
            _tier = tier;
            ZIndex = -8;   // под людьми и стенами, над землёй и декалями
            Position = new Vector2(i * Building.Cell * Iso.PPM, j * Building.Cell * Iso.PPM);
            Material = VisionFx.ObjectMat;
            TextureFilter = TextureFilterEnum.Nearest;
            TextureRepeat = TextureRepeatEnum.Enabled;
        }

        public override void _Draw()
        {
            float s = Building.Cell * Iso.PPM;
            var top = new Rect2(0, -Lip, s, s);
            var lip = new Rect2(0, s - Lip, s, Lip);
            var (tex, tint) = _tier switch
            {
                0 => (Art.Get("tiles/floor_wood"), new Color(0.75f, 0.85f, 1.05f, 0.55f)),
                1 => (Art.Get("tiles/floor_wood"), new Color(1, 1, 1)),
                2 => (Art.Get("walls/wallface_stone"), new Color(1.05f, 1.05f, 1.05f)),
                3 => (Art.Get("walls/wallface_metal"), new Color(1, 1, 1)),
                _ => (Art.Get("walls/wallface_metal"), new Color(0.7f, 0.72f, 0.78f)),
            };
            DrawRect(new Rect2(3, s - 1, s, 4), new Color(0, 0, 0, 0.22f));   // тень
            if (tex != null) DrawTextureRect(tex, top, true, tint);
            else DrawRect(top, new Color(0.5f, 0.4f, 0.3f, tint.A));
            DrawRect(lip, new Color(0, 0, 0, _tier == 0 ? 0.15f : 0.35f));
            DrawRect(top, _tier == 0 ? new Color(0.6f, 0.8f, 1f, 0.6f) : new Color(0, 0, 0, 0.3f), false, 1);
        }
    }

    /// <summary>Призрак элемента под курсором, пока в руках план постройки.</summary>
    public partial class BuildGhost2D : Node2D
    {
        StructKind _kind;
        SVec _at;
        bool _ok;

        public BuildGhost2D() { ZIndex = 6; }

        bool _zone;
        (int i, int j) _zoneCell;
        /// <summary>Зона столба под курсором — пунктирный квадрат 17×17 клеток.</summary>
        public void SetZone(bool has, (int i, int j) cell)
        {
            if (has == _zone && cell == _zoneCell) return;
            _zone = has; _zoneCell = cell;
            QueueRedraw();
        }

        public void Set(StructKind kind, SVec aim, bool ok)
        {
            if (kind == _kind && ok == _ok && SnapKey(kind, aim) == SnapKey(_kind, _at)) return;
            _kind = kind; _at = aim; _ok = ok;
            QueueRedraw();
        }

        static (int, int, int) SnapKey(StructKind k, SVec p)
        {
            if (k == StructKind.Foundation) { var (i, j) = Building.CellOf(p); return (i, j, -1); }
            return Building.NearestEdge(p);
        }

        public override void _Draw()
        {
            var fill = _ok ? new Color(0.45f, 0.8f, 1f, 0.28f) : new Color(1f, 0.35f, 0.3f, 0.28f);
            var line = _ok ? new Color(0.6f, 0.9f, 1f, 0.85f) : new Color(1f, 0.45f, 0.4f, 0.85f);
            if (_zone)
            {
                float c = Building.Cell * Iso.PPM;
                var z = new Rect2((_zoneCell.i - World.ClaimRadius) * c, (_zoneCell.j - World.ClaimRadius) * c, (2 * World.ClaimRadius + 1) * c, (2 * World.ClaimRadius + 1) * c);
                var zc = new Color(1f, 0.85f, 0.4f, 0.55f);
                for (float x = z.Position.X; x < z.End.X; x += 12) { DrawLine(new Vector2(x, z.Position.Y), new Vector2(Mathf.Min(x + 6, z.End.X), z.Position.Y), zc, 1); DrawLine(new Vector2(x, z.End.Y), new Vector2(Mathf.Min(x + 6, z.End.X), z.End.Y), zc, 1); }
                for (float y = z.Position.Y; y < z.End.Y; y += 12) { DrawLine(new Vector2(z.Position.X, y), new Vector2(z.Position.X, Mathf.Min(y + 6, z.End.Y)), zc, 1); DrawLine(new Vector2(z.End.X, y), new Vector2(z.End.X, Mathf.Min(y + 6, z.End.Y)), zc, 1); }
            }
            if (_kind == StructKind.Foundation)
            {
                var (i, j) = Building.CellOf(_at);
                float s = Building.Cell * Iso.PPM;
                var r = new Rect2(i * s, j * s - 4, s, s);
                DrawRect(r, fill);
                DrawRect(r, line, false, 1);
                return;
            }
            var (ei, ej, dir) = Building.NearestEdge(_at);
            var boxes = Building.Boxes(_kind, ei, ej, dir, false);
            foreach (var (min, max, h, _) in boxes)
            {
                float x0 = min.X * Iso.PPM, x1 = max.X * Iso.PPM, y0 = min.Y * Iso.PPM, y1 = max.Y * Iso.PPM, hh = Iso.H(h);
                var front = new Rect2(x0, y1 - hh, x1 - x0, hh);
                var top = new Rect2(x0, y0 - hh, x1 - x0, y1 - y0);
                DrawRect(front, fill);
                DrawRect(top, fill.Lightened(0.2f));
                DrawRect(front, line, false, 1);
            }
        }
    }
}

namespace Rastvordd.Client
{
    /// <summary>Брошенная взрывчатка: летит по дуге, лежит; у связки искрит фитиль, у шашки мигает огонёк и пищит.</summary>
    public partial class ThrownView2D : Godot.Node2D
    {
        public string ItemId;
        public float Z, Fuse = -1;
        float _t, _beep;
        readonly Godot.PointLight2D _light;
        readonly Godot.AudioStreamPlayer2D _hiss;

        public ThrownView2D(string itemId)
        {
            ItemId = itemId;
            TextureFilter = TextureFilterEnum.Nearest;
            _light = Art.FireLight(1.6f, 0f);
            _light.Color = itemId == "expl_c4" ? new Godot.Color(1f, 0.2f, 0.15f) : new Godot.Color(1f, 0.6f, 0.25f);
            AddChild(_light);
            if (itemId == "expl_satchel") { _hiss = Sfx.Loop("fuse_hiss", -10, 12); if (_hiss != null) { AddChild(_hiss); _hiss.Play(); } }
        }

        public void Tick(float dt)
        {
            _t += dt;
            if (ItemId == "expl_c4" && Fuse >= 0)
            {
                _beep -= dt;
                if (_beep <= 0) { _beep = Fuse < 3 ? 0.35f : 1f; Sfx.Play("c4_beep", GlobalPosition, -8, 15, 0f); }
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            float lift = Z * Iso.PPM * 0.75f;
            DrawSetTransform(Godot.Vector2.Zero, 0, new Godot.Vector2(1, 0.45f));
            DrawCircle(Godot.Vector2.Zero, 5, new Godot.Color(0, 0, 0, 0.3f));
            DrawSetTransform(Godot.Vector2.Zero, 0, Godot.Vector2.One);
            var tex = Art.Icon(ItemId);
            var at = new Godot.Vector2(0, -6 - lift);
            if (tex != null) DrawTextureRect(tex, new Godot.Rect2(at - new Godot.Vector2(8, 8), new Godot.Vector2(16, 16)), false);
            else DrawRect(new Godot.Rect2(at - new Godot.Vector2(5, 4), new Godot.Vector2(10, 8)), new Godot.Color(0.5f, 0.4f, 0.3f));
            bool on;
            if (ItemId == "expl_c4") on = Fuse >= 0 && ((int)(_t * (Fuse < 3 ? 6 : 2)) % 2 == 0);
            else on = true;
            _light.Position = at + new Godot.Vector2(4, -4);
            _light.Energy = on ? (ItemId == "expl_c4" ? 0.9f : 0.6f + 0.3f * Godot.Mathf.Sin(_t * 37)) : 0f;
            if (!on) return;
            if (ItemId == "expl_c4") DrawRect(new Godot.Rect2(at + new Godot.Vector2(3, -5), new Godot.Vector2(2, 2)), new Godot.Color(1f, 0.15f, 0.1f));
            else
                for (int i = 0; i < 3; i++)
                {
                    float a = _t * 23 + i * 2.1f;
                    DrawRect(new Godot.Rect2(at + new Godot.Vector2(5 + Godot.Mathf.Cos(a) * 2, -6 + Godot.Mathf.Sin(a * 1.3f) * 2), Godot.Vector2.One), new Godot.Color(1f, 0.8f, 0.3f));
                }
        }
    }
}
