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
        public readonly List<SlantWallView2D> Slants = new();
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
                    if (cur.snap.Tier == s.Tier && cur.snap.Open == s.Open && cur.snap.Kind == s.Kind && cur.snap.Locked == s.Locked)
                    {
                        if (Mathf.Abs(cur.snap.Hp - s.Hp) > 0.01f) SetDamage(cur.views, s.Hp);
                        if (cur.snap.Seen != s.Seen) SetFog(cur.views, s.Seen);
                        _items[s.Id] = (s, cur.views);
                        continue;
                    }
                    Free(cur.views);
                }
                var created = Create(s);
                SetFog(created, s.Seen);
                _items[s.Id] = (s, created);
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
            foreach (var v in views) { if (v is WallView2D w) Walls.Remove(w); if (v is SlantWallView2D sw) Slants.Remove(sw); v.QueueFree(); }
        }

        /// <summary>Туман войны: постройка не в обзоре — серая, «как запомнил».</summary>
        public static readonly Color FogTint = new(0.55f, 0.58f, 0.64f, 0.9f);

        static void SetFog(List<Node2D> views, bool seen)
        {
            foreach (var v in views) v.Modulate = seen ? Colors.White : FogTint;
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
            else if (s.Kind == StructKind.TriFoundation)
            {
                var f = new TriFloorView2D(s.A, s.B, s.C, s.Tier);
                _world.AddChild(f);
                views.Add(f);
            }
            else if (System.MathF.Abs(s.A.X - s.B.X) < 0.01f || System.MathF.Abs(s.A.Y - s.B.Y) < 0.01f)
            {
                foreach (var (min, max, h, _) in Building.Boxes(s.Kind, s.A, s.B, s.Open))
                {
                    var w = new WallView2D(new WallInfo { Min = min, Max = max, Height = h, Kind = WallKind.Built, Tier = s.Tier, Door = s.Kind == StructKind.Door, Locked = s.Locked, StructId = s.Id });
                    _world.AddChild(w);
                    views.Add(w);
                    Walls.Add(w);
                }
            }
            else
            {
                // наклонное ребро (треугольник): каждая часть — гладкая призма
                foreach (var (t0, t1, h, _) in Building.Spans(s.Kind, s.Open))
                {
                    var w = new SlantWallView2D(SVec.Lerp(s.A, s.B, t0), SVec.Lerp(s.A, s.B, t1), h, s.Tier, s.Kind == StructKind.Door);
                    _world.AddChild(w);
                    views.Add(w);
                    Slants.Add(w);
                }
            }
            SetDamage(views, s.Hp);
            return views;
        }

        readonly HashSet<(int, int)> _floors = new();
        readonly List<SVec[]> _triFloors = new();
        readonly Dictionary<(int, int), int> _floorTier = new();
        readonly List<(SVec[] poly, int tier)> _triTier = new();

        /// <summary>Уровень фундамента под точкой (для звука шагов) или -1.</summary>
        public int FloorTier(SVec p)
        {
            if (_floorTier.TryGetValue(Building.CellOf(p), out var t)) return t;
            foreach (var (poly, tier) in _triTier) if (Building.PointInPoly(p, poly)) return tier;
            return -1;
        }

        /// <summary>Точка на полу (фундаменте) постройки.</summary>
        public bool OnFloor(SVec p)
        {
            if (_floors.Contains(Building.CellOf(p))) return true;
            foreach (var t in _triFloors) if (Building.PointInPoly(p, t)) return true;
            return false;
        }

        void RebuildOccluders()
        {
            _floors.Clear();
            _triFloors.Clear();
            _floorTier.Clear();
            _triTier.Clear();
            foreach (var (snap, _) in _items.Values)
            {
                if (snap.Kind == StructKind.Foundation) { _floors.Add((snap.I, snap.J)); _floorTier[(snap.I, snap.J)] = snap.Tier; }
                else if (snap.Kind == StructKind.TriFoundation) { _triFloors.Add(snap.Poly); _triTier.Add((snap.Poly, snap.Tier)); }
            }
            Occluders.Clear();
            Acoustics.DynamicWalls.Clear();
            foreach (var (snap, _) in _items.Values)
                if (!Building.IsFloor(snap.Kind))
                    foreach (var (min, max, h, pass) in Building.Boxes(snap.Kind, snap.A, snap.B, snap.Open))
                    {
                        Occluders.Add(new WallInfo { Min = min, Max = max, Height = h, Kind = WallKind.Built });
                        if (!pass) Acoustics.DynamicWalls.Add((min, max));
                    }
        }

        /// <summary>Стены, закрывающие фокусы кадра (свой персонаж, видимые персонажи, курсор — см. Occlusion), полупрозрачны.</summary>
        public void UpdateFade(Vector2 selfPx, float dt)
        {
            // внутри дома (стоим на фундаменте) — стены ниже по экрану поблизости срезаются
            bool inside = OnFloor(new SVec(selfPx.X / Iso.PPM, selfPx.Y / Iso.PPM));
            float near = 12f * Iso.PPM;
            foreach (var w in Walls)
            {
                bool cut = inside && w.Position.Y > selfPx.Y && w.Position.Y - selfPx.Y < near && Mathf.Abs(w.Position.X - selfPx.X) < near;
                w.SetCut(cut);
                w.UpdateOcclusion(dt);
            }
            foreach (var w in Slants)
            {
                bool cut = inside && w.Position.Y > selfPx.Y && w.Position.Y - selfPx.Y < near && Mathf.Abs(w.Position.X - selfPx.X) < near;
                w.SetCut(cut);
                w.UpdateOcclusion(dt);
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

        public Color? Tint;   // подсветка цели киянки (иначе — цвета призрака: годится / нет)

        bool _zone;
        (int i, int j) _zoneCell;
        /// <summary>Зона столба под курсором — пунктирный квадрат 17×17 клеток.</summary>
        public void SetZone(bool has, (int i, int j) cell)
        {
            if (has == _zone && cell == _zoneCell) return;
            _zone = has; _zoneCell = cell;
            QueueRedraw();
        }

        SVec[] _poly;
        SVec _ea, _eb;

        /// <summary>Призрак: пол (многоугольник) или ребро — как посчитал сервер.</summary>
        public void Set(StructKind kind, SVec[] poly, SVec a, SVec b, bool ok)
        {
            bool same = kind == _kind && ok == _ok && a == _ea && b == _eb && (poly == null) == (_poly == null) && (poly == null || (poly.Length == _poly.Length && poly[0] == _poly[0] && poly[^1] == _poly[^1]));
            if (same) return;
            _kind = kind; _poly = poly; _ea = a; _eb = b; _ok = ok;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var fill = _ok ? new Color(0.45f, 0.8f, 1f, 0.28f) : new Color(1f, 0.35f, 0.3f, 0.28f);
            var line = _ok ? new Color(0.6f, 0.9f, 1f, 0.85f) : new Color(1f, 0.45f, 0.4f, 0.85f);
            float lw = Tint != null ? 2.5f : 1;
            if (Tint is Color tint) { fill = new Color(tint, 0.32f); line = new Color(tint, 0.95f); }
            if (_zone)
            {
                float c = Building.Cell * Iso.PPM;
                var zc = new Color(1f, 0.85f, 0.4f, 0.55f);
                // зона шкафа — круг по клеткам: пунктир по внешним краям клеток зоны
                void Dash(Vector2 p0, Vector2 p1)
                {
                    float len = p0.DistanceTo(p1);
                    for (float t = 0; t < len; t += 12) DrawLine(p0.Lerp(p1, t / len), p0.Lerp(p1, Mathf.Min(t + 6, len) / len), zc, 1);
                }
                int R = World.ClaimRadius;
                for (int di = -R - 1; di <= R + 1; di++)
                    for (int dj = -R - 1; dj <= R + 1; dj++)
                    {
                        if (!World.InClaimZone(di, dj)) continue;
                        float x0 = (_zoneCell.i + di) * c, y0 = (_zoneCell.j + dj) * c;
                        if (!World.InClaimZone(di, dj - 1)) Dash(new Vector2(x0, y0), new Vector2(x0 + c, y0));
                        if (!World.InClaimZone(di, dj + 1)) Dash(new Vector2(x0, y0 + c), new Vector2(x0 + c, y0 + c));
                        if (!World.InClaimZone(di - 1, dj)) Dash(new Vector2(x0, y0), new Vector2(x0, y0 + c));
                        if (!World.InClaimZone(di + 1, dj)) Dash(new Vector2(x0 + c, y0), new Vector2(x0 + c, y0 + c));
                    }
            }
            if (Building.IsFloor(_kind))
            {
                if (_poly == null) return;
                var pts = new Vector2[_poly.Length];
                for (int i = 0; i < pts.Length; i++) pts[i] = Iso.Px(_poly[i]) + new Vector2(0, -4);
                DrawColoredPolygon(pts, fill);
                for (int i = 0; i < pts.Length; i++) DrawLine(pts[i], pts[(i + 1) % pts.Length], line, lw);
                return;
            }
            if (_ea == _eb) return;
            var boxes = Building.Boxes(_kind, _ea, _eb, false);
            foreach (var (min, max, h, _) in boxes)
            {
                float x0 = min.X * Iso.PPM, x1 = max.X * Iso.PPM, y0 = min.Y * Iso.PPM, y1 = max.Y * Iso.PPM, hh = Iso.H(h);
                var front = new Rect2(x0, y1 - hh, x1 - x0, hh);
                var top = new Rect2(x0, y0 - hh, x1 - x0, y1 - y0);
                DrawRect(front, fill);
                DrawRect(top, fill.Lightened(0.2f));
                DrawRect(front, line, false, lw); if (Tint != null) DrawRect(top, line, false, lw);
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
            if (itemId == "expl_satchel") { _hiss = Sfx.Loop("fuse_hiss", -10, 12); if (_hiss != null) { _hiss.Autoplay = true; AddChild(_hiss); } }
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

namespace Rastvordd.Client
{
    /// <summary>Горящее пятно (молотов): языки пламени fx/fire_medium, подпалина, угольки-частицы, столб чёрного дыма, тёплый свет, треск.</summary>
    public partial class FireView2D : Godot.Node2D
    {
        public float Radius, Left;
        float _t;
        readonly Godot.PointLight2D _light;
        readonly Godot.Vector2[] _spots;
        readonly Godot.CpuParticles2D _embers;
        float _smokeAcc;
        int _smokeI;

        public FireView2D(float radiusM, int seed)
        {
            Radius = radiusM;
            TextureFilter = TextureFilterEnum.Nearest;
            Material = VisionFx.ObjectMat;
            _light = Art.FireLight(radiusM * 3.5f, 1.4f);
            AddChild(_light);
            var crackle = Sfx.Loop("furnace_loop", -4, 25);
            if (crackle != null) { crackle.Autoplay = true; AddChild(crackle); }
            // угольки над огнём — частицы с листом embers
            _embers = Vfx.Embers(radiusM * Iso.PPM * 1.4f, 10);
            _embers.Position = new Godot.Vector2(0, -Iso.H(0.6f));
            AddChild(_embers);
            var rng = new Godot.RandomNumberGenerator { Seed = (ulong)seed };
            _spots = new Godot.Vector2[System.Math.Clamp((int)(radiusM * 2.5f), 3, 7)];
            for (int i = 0; i < _spots.Length; i++)
            {
                float a = rng.Randf() * Godot.Mathf.Tau, r = Godot.Mathf.Sqrt(rng.Randf()) * radiusM * Iso.PPM * 0.8f;
                _spots[i] = new Godot.Vector2(Godot.Mathf.Cos(a) * r, Godot.Mathf.Sin(a) * r * 0.5f).Round();
            }
            System.Array.Sort(_spots, (p, q) => p.Y.CompareTo(q.Y));   // дальние языки рисуются первыми
        }

        public void Tick(float dt)
        {
            _t += dt;
            _light.Energy = (Left < 2 ? Left / 2 : 1) * (1.2f + 0.3f * Godot.Mathf.Sin(_t * 13) * Godot.Mathf.Sin(_t * 5.3f));
            _embers.Emitting = Left > 1.5f;
            // столб чёрного маслянистого дыма над огнём (Vfx: вне кадра не создаётся)
            _smokeAcc += dt * (Left > 2 ? 1f : 0.4f);
            if (_smokeAcc > 0.45f / Godot.Mathf.Max(1f, Radius))
            {
                _smokeAcc = 0;
                Vfx.Puff("smoke_black", Position + _spots[_smokeI++ % _spots.Length], Iso.H(1.2f), 22, 0.75f, 2.4f, 4);
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            float fade = Left < 2 ? Left / 2 : 1;
            var scorch = Art.Get("decals/decal_burnt_spot");
            if (scorch != null)
            {
                // копоть: существующая декаль подпалины по размеру пятна (целый масштаб — пиксели не плывут)
                float sc = Godot.Mathf.Max(1, Godot.Mathf.Round(Radius * 2 * Iso.PPM / scorch.GetWidth()));
                var sz = scorch.GetSize() * sc;
                DrawTextureRect(scorch, new Godot.Rect2(-sz / 2, sz), false, new Godot.Color(1, 1, 1, 0.55f * fade));
            }
            // языки пламени — лист fire_medium (40 px), у каждого своя фаза; на угасании — маленький огонь
            for (int i = 0; i < _spots.Length; i++)
                Vfx.DrawFrame(this, fade > 0.5f && i % 3 != 2 ? "fire_medium" : "fire_small", _spots[i], _t, 12, 1, null, i * 3);
        }
    }
}

namespace Rastvordd.Client
{
    /// <summary>Мягкая рамка цели E на земле (верстак, ящик, печь, шкаф, сумка, находка): видно, с чем сработает кнопка.</summary>
    public partial class InteractMark : Node2D
    {
        System.Numerics.Vector2 _at, _half;

        public void Set(System.Numerics.Vector2 at, System.Numerics.Vector2 half)
        {
            if (at == _at && half == _half) return;
            _at = at; _half = half;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_half == default) return;
            var c = new System.Numerics.Vector2[]
            {
                _at + new System.Numerics.Vector2(-_half.X, -_half.Y), _at + new System.Numerics.Vector2(_half.X, -_half.Y),
                _at + new System.Numerics.Vector2(_half.X, _half.Y), _at + new System.Numerics.Vector2(-_half.X, _half.Y),
            };
            var pts = System.Array.ConvertAll(c, p => Iso.Px(p));
            DrawColoredPolygon(pts, new Color(1f, 0.95f, 0.8f, 0.08f));
            // уголки, а не сплошная рамка — аккуратнее и не спорит с подсветкой киянки
            var line = new Color(1f, 0.95f, 0.8f, 0.55f);
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = pts[i], prev = pts[(i + 3) % 4], next = pts[(i + 1) % 4];
                DrawLine(p, p + (next - p) * 0.3f, line, 1.5f, true);
                DrawLine(p, p + (prev - p) * 0.3f, line, 1.5f, true);
            }
        }
    }
}

namespace Rastvordd.Client
{
    /// <summary>Полигон, F4: хитбоксы как их видит пуля. Красное — останавливает пулю, жёлтое — низкое (пуля проходит поверх), голубое — тела.</summary>
    public partial class HitboxOverlay : Node2D
    {
        public World W;
        public int MeId;

        public override void _Process(double delta) => QueueRedraw();

        public override void _Draw()
        {
            if (W?.Find(MeId) is not Actor me) return;
            const float R = 45f;
            // пуля летит на высоте ствола — рамки там же, где её видно на экране
            DrawSetTransform(new Vector2(0, -Iso.H(Hitbox.GunH)), 0, Vector2.One);
            var stop = new Color(1f, 0.25f, 0.2f, 0.9f); var low = new Color(1f, 0.85f, 0.2f, 0.9f); var body = new Color(0.35f, 0.85f, 1f, 0.9f);
            void Box(System.Numerics.Vector2 min, System.Numerics.Vector2 max, Color c)
            {
                var r = new Rect2(Iso.Px(min), Iso.Px(max) - Iso.Px(min));
                DrawRect(r, new Color(c, 0.12f));
                DrawRect(r, c, false, 1.5f);
            }
            foreach (var w in W.Walls)
            {
                if (w.PassBullets || System.Numerics.Vector2.Distance(System.Numerics.Vector2.Clamp(me.Pos, w.Wall.Min, w.Wall.Max), me.Pos) > R) continue;
                if (W.PropMaskOf(w) is PropMask.Mask m)
                {
                    var col = w.Wall.Height >= Hitbox.GunH ? stop : low;
                    // проп — контур по пикселям картинки (в плоскости пули он ложится ровно на спрайт)
                    var (rmin, _) = PropMask.Region(w.Wall.Min, w.Wall.Max, m);
                    var o = Iso.Px(rmin);
                    bool A(int x, int y) => x >= 0 && y >= 0 && x < m.W && y < m.H && m.A[y * m.W + x];
                    for (int y = 0; y < m.H; y++)
                        for (int x = 0; x < m.W; x++)
                        {
                            if (!A(x, y)) continue;
                            var p = o + new Vector2(x, y) * m.S;
                            if (!A(x, y - 1)) DrawLine(p, p + new Vector2(m.S, 0), col, 1);
                            if (!A(x, y + 1)) DrawLine(p + new Vector2(0, m.S), p + new Vector2(m.S, m.S), col, 1);
                            if (!A(x - 1, y)) DrawLine(p, p + new Vector2(0, m.S), col, 1);
                            if (!A(x + 1, y)) DrawLine(p + new Vector2(m.S, 0), p + new Vector2(m.S, m.S), col, 1);
                        }
                    continue;
                }
                Box(w.Wall.Min, w.Wall.Max, w.Wall.Height >= Hitbox.GunH ? stop : low);
            }
            foreach (var n in W.NodesInRadius(me.Pos, R, new System.Collections.Generic.List<ResourceNode>()))
            {
                var c = n.Kind is NodeKind.Tree or NodeKind.Barrel ? stop : low;
                DrawArc(Iso.Px(n.Pos), n.Radius * Iso.PPM, 0, Mathf.Tau, 24, c, 1.5f, true);
            }
            foreach (var d in W.Deployables)
                if (d.Solid && System.Numerics.Vector2.Distance(d.Pos, me.Pos) < R) Box(d.Min, d.Max, low);
            foreach (var a in W.Actors)
            {
                if (a.Dead || a.Spectator || System.Numerics.Vector2.Distance(a.Pos, me.Pos) > R) continue;
                var (min, max) = Hitbox.Body(a);
                Box(min, max, body);
            }
        }
    }
}
