using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>Спрайт готовой РТ (трансформатор, портал, шкаф…): основанием в точке, тень от силуэта, прозрачность заслона.</summary>
    public partial class DecorView2D : Node2D
    {
        string _sprite;
        readonly bool _flip, _pivot;
        readonly float _lift;
        float _rot;
        float _alpha = 1;
        Texture2D _tex;
        Rect2 _screen;

        public DecorView2D(MapDecor d)
        {
            Position = Iso.Px(d.Pos);
            _flip = d.Flip;
            _pivot = d.Pivot;
            _lift = d.Lift;
            _rot = d.Rot;
            TextureFilter = TextureFilterEnum.Nearest;
            Material = VisionFx.ObjectMat;
            SetSprite(d.Sprite);
        }

        public void SetSprite(string sprite)
        {
            if (sprite == _sprite) return;
            _sprite = sprite;
            _tex = Art.Get(sprite);
            float w = _tex?.GetWidth() ?? 16, h = _tex?.GetHeight() ?? 16;
            _screen = new Rect2(Position.X - w / 2, Position.Y - h, w, h);
            QueueRedraw();
        }

        /// <summary>Подвижное украшение (вагон, стрела крана, контейнер): новое место и поворот из снапшота.</summary>
        public void MoveTo(System.Numerics.Vector2 pos, float rot)
        {
            var p = Iso.Px(pos);
            if (p.DistanceSquaredTo(Position) < 0.25f && Mathf.Abs(rot - _rot) < 0.2f) return;
            Position = p;
            _rot = rot;
            float w = _tex?.GetWidth() ?? 16, h = _tex?.GetHeight() ?? 16;
            _screen = new Rect2(Position.X - w / 2, Position.Y - h, w, h);
            QueueRedraw();
        }

        public void UpdateOcclusion(float dt)
        {
            if (Occlusion.Step(ref _alpha, Occlusion.Covers(_screen, Position.Y), dt)) QueueRedraw();
        }

        public override void _Draw()
        {
            if (_tex == null) return;
            if (_pivot)
            {
                // стрела крана: висит на высоте, вращается вокруг левого края (точка опоры), в виде 3/4 — по экрану
                var at = new Vector2(0, -Iso.H(_lift));
                float a = Mathf.DegToRad(_rot);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f);
                DrawSetTransform(at, Mathf.Atan2(dir.Y, dir.X), Vector2.One);
                DrawTexture(_tex, new Vector2(0, -_tex.GetHeight() / 2f), new Color(1, 1, 1, _alpha));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                return;
            }
            if (_flip) DrawSetTransform(Vector2.Zero, 0, new Vector2(-1, 1));
            Shade.Sprite(this, _tex, 1f, 0.32f);
            if (_flip) DrawSetTransform(Vector2.Zero, 0, new Vector2(-1, 1));
            DrawTexture(_tex, new Vector2(-_tex.GetWidth() / 2f, -_tex.GetHeight() + 2), new Color(1, 1, 1, _alpha));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    /// <summary>Участки земли готовых РТ: гравий, асфальт, бетон, кафель — поверх рельефа, под всем остальным.</summary>
    public partial class PatchLayer : Node2D
    {
        readonly List<MapPatch> _patches;

        public PatchLayer(List<MapPatch> patches)
        {
            _patches = patches;
            TextureFilter = TextureFilterEnum.Nearest;
            TextureRepeat = TextureRepeatEnum.Enabled;
            Material = VisionFx.ObjectMat;
        }

        public override void _Draw()
        {
            foreach (var p in _patches)
            {
                var tex = Art.Get(p.Tile);
                if (p.Poly != null)
                {
                    // неровный участок: текстура мира по координатам (UV в пикселях, повтор)
                    var pts = new Vector2[p.Poly.Length];
                    var uvs = new Vector2[p.Poly.Length];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        pts[i] = Iso.Px(p.Poly[i]);
                        uvs[i] = tex != null ? pts[i] / tex.GetSize() : Vector2.Zero;
                    }
                    if (tex != null) DrawPolygon(pts, new[] { Colors.White }, uvs, tex);
                    else DrawColoredPolygon(pts, new Color(0.35f, 0.34f, 0.3f));
                    continue;
                }
                var r = new Rect2(Iso.Px(p.Min), Iso.Px(p.Max) - Iso.Px(p.Min));
                if (tex != null) DrawTextureRect(tex, r, true);
                else DrawRect(r, new Color(0.35f, 0.34f, 0.3f));
                // мягкий край: тёмная кромка, чтобы участок не выглядел наклеенным
                DrawRect(r, new Color(0, 0, 0, 0.12f), false, 2);
            }
        }
    }

    /// <summary>
    /// Входы зданий РТ (Entrances.cs): бетонное крыльцо со ступенями у двери и пандус у ворот — по ширине проёма,
    /// в сторону наружу. Рисуется под объектами: по нему ходят.
    /// </summary>
    public partial class EntranceLayer : Node2D
    {
        readonly List<MapEntrance> _list;
        Texture2D _tex;

        public EntranceLayer(List<MapEntrance> list)
        {
            _list = list;
            TextureFilter = TextureFilterEnum.Nearest;
            TextureRepeat = TextureRepeatEnum.Enabled;
            Material = VisionFx.ObjectMat;
        }

        // прямоугольник по осям «вдоль проёма» u и «наружу» n → мировые min/max
        static (SVec min, SVec max) Box(MapEntrance e, float u0, float u1, float n0, float n1)
        {
            float o = e.Side is 0 or 2 ? 1 : -1;
            SVec P(float u, float n) => e.Side < 2 ? new SVec(e.A.X + u, e.A.Y + o * n) : new SVec(e.A.X + o * n, e.A.Y + u);
            var a = P(u0, n0); var b = P(u1, n1);
            return (SVec.Min(a, b), SVec.Max(a, b));
        }

        Vector2 Up(SVec p, float h) => Iso.Px(p) - new Vector2(0, Iso.H(h));

        void Quad(Vector2[] q, float shade)
        {
            var c = new Color(shade, shade * 0.99f, shade * 0.96f);
            if (_tex != null)
            {
                var uv = new Vector2[q.Length];
                for (int i = 0; i < q.Length; i++) uv[i] = q[i] / _tex.GetSize();
                DrawPolygon(q, new[] { c, c, c, c }[..q.Length], uv, _tex);
            }
            else DrawColoredPolygon(q, c * new Color(0.62f, 0.61f, 0.58f));
        }

        // бетонный блок высотой h: верх (светлый) и видимая южная грань (в тени)
        void Block(SVec min, SVec max, float h, float topShade)
        {
            var top = new[] { Up(min, h), Up(new SVec(max.X, min.Y), h), Up(max, h), Up(new SVec(min.X, max.Y), h) };
            var face = new[] { Up(new SVec(min.X, max.Y), h), Up(max, h), Iso.Px(max), Iso.Px(new SVec(min.X, max.Y)) };
            Quad(face, 0.55f);
            Quad(top, topShade);
            DrawLine(top[0], top[1], new Color(1, 1, 1, 0.18f), 1);              // скол кромки на свету
            DrawPolyline(new[] { top[0], top[1], top[2], face[2], face[3], top[3], top[0] }, new Color(0, 0, 0, 0.35f), 1);
        }

        public override void _Draw()
        {
            _tex ??= Art.Get("tiles/ground_concrete");
            foreach (var e in _list)
            {
                float len = (e.B - e.A).Length(), d = e.Depth;
                // контактная тень по земле — чуть шире и дальше
                var (smin, smax) = Box(e, -0.15f, len + 0.15f, 0, d + 0.25f);
                DrawRect(new Rect2(Iso.Px(smin), Iso.Px(smax) - Iso.Px(smin)), new Color(0, 0, 0, 0.16f));

                if (e.Ramp) DrawRamp(e, len, d);
                else DrawPorch(e, len, d);
            }
        }

        void DrawPorch(MapEntrance e, float len, float d)
        {
            float split = d * 0.55f;
            var (pmin, pmax) = Box(e, 0, len, 0, split);           // площадка у двери
            var (smin, smax) = Box(e, 0, len, split, d);           // ступень
            const float hp = 0.3f, hs = 0.15f;
            // порядок: дальнее от камеры — раньше
            if (e.Side == 1) { Block(smin, smax, hs, 0.9f); Block(pmin, pmax, hp, 1f); }
            else { Block(pmin, pmax, hp, 1f); Block(smin, smax, hs, 0.9f); }
            // перила из трубы по краям, если крыльцо не во всю стену
            var rail = new Color(0.24f, 0.25f, 0.26f);
            foreach (float u in new[] { 0.12f, len - 0.12f })
            {
                var (a, _) = Box(e, u, u, 0.1f, 0.1f);
                var (b, _) = Box(e, u, u, d - 0.1f, d - 0.1f);
                var pa = Up(a, hp); var pb = Up(b, hs);
                var ta = pa - new Vector2(0, Iso.H(0.85f)); var tb = pb - new Vector2(0, Iso.H(0.85f));
                DrawLine(pa, ta, rail, 2); DrawLine(pb, tb, rail, 2); DrawLine(ta, tb, rail, 2);
                DrawLine(ta + new Vector2(0, -1), tb + new Vector2(0, -1), new Color(0.6f, 0.62f, 0.64f, 0.6f), 1);   // блик
            }
        }

        void DrawRamp(MapEntrance e, float len, float d)
        {
            const float hr = 0.25f;
            float o = e.Side is 0 or 2 ? 1 : -1;
            // наклонная плоскость: у стены — hr, на краю — 0
            SVec P(float u, float n) => e.Side < 2 ? new SVec(e.A.X + u, e.A.Y + o * n) : new SVec(e.A.X + o * n, e.A.Y + u);
            float H(float n) => hr * (1 - n / d);
            var q = new[] { Up(P(0, 0), H(0)), Up(P(len, 0), H(0)), Up(P(len, d), H(d)), Up(P(0, d), H(d)) };
            // боковая грань-клин видна, когда пандус идёт вбок (восток/запад)
            if (e.Side >= 2)
            {
                var (_, mx) = Box(e, 0, len, 0, d);
                float yS = mx.Y;
                var w0 = new SVec(P(0, 0).X, yS); var w1 = new SVec(P(0, d).X, yS);
                Quad(new[] { Up(w0, hr), Iso.Px(w0), Iso.Px(w1) }, 0.5f);
            }
            Quad(q, 0.92f);
            // рифление поперёк уклона
            for (float n = 0.3f; n < d - 0.1f; n += 0.3f)
                DrawLine(Up(P(0.15f, n), H(n)), Up(P(len - 0.15f, n), H(n)), new Color(0, 0, 0, 0.22f), 1);
            // жёлто-чёрная кромка у края пандуса
            int seg = Mathf.Max(2, (int)(len / 0.4f));
            for (int i = 0; i < seg; i++)
            {
                float u0 = len * i / seg, u1 = len * (i + 1) / seg;
                var col = i % 2 == 0 ? new Color(0.86f, 0.68f, 0.16f) : new Color(0.12f, 0.12f, 0.12f);
                DrawColoredPolygon(new[] { Up(P(u0, d - 0.22f), H(d - 0.22f)), Up(P(u1, d - 0.22f), H(d - 0.22f)), Iso.Px(P(u1, d)), Iso.Px(P(u0, d)) }, new Color(col, 0.85f));
            }
            DrawPolyline(new[] { q[0], q[1], q[2], q[3], q[0] }, new Color(0, 0, 0, 0.35f), 1);
        }
    }

    /// <summary>
    /// Крыша над комнатой (D40): снаружи прячет, что внутри; растворяется, когда свой персонаж внутри
    /// (или стоит в дверях). Рисуется поверх всего на высоте стен.
    /// </summary>
    public partial class RoofView2D : Node2D
    {
        readonly Rect2 _rect;          // след комнаты на земле, пиксели мира
        readonly float _h;
        readonly Texture2D _tex;
        readonly List<RoofItem> _items;
        float _alpha = 1;

        public RoofView2D(MapRoof r, float wallH = 2.8f)
        {
            _rect = new Rect2(Iso.Px(r.Min), Iso.Px(r.Max) - Iso.Px(r.Min));
            _h = Iso.H(wallH);
            _tex = Art.Get(r.Tile) ?? Art.Get("tiles/ground_asphalt");
            _items = r.Items ?? new List<RoofItem>();
            _items.Sort((a, b) => a.Pos.Y.CompareTo(b.Pos.Y));
            ZIndex = 4;
            TextureFilter = TextureFilterEnum.Nearest;
            TextureRepeat = TextureRepeatEnum.Enabled;
            Material = VisionFx.ObjectMat;
        }

        public void UpdateFade(Vector2 selfPx, float dt)
        {
            bool inside = _rect.Grow(6).HasPoint(selfPx);
            float target = inside ? 0f : 1f;
            if (Mathf.Abs(_alpha - target) < 0.01f) return;
            _alpha = Mathf.MoveToward(_alpha, target, dt * 4f);
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_alpha <= 0.01f) return;
            var r = new Rect2(_rect.Position + new Vector2(-4, -_h - 4), _rect.Size + new Vector2(8, 8));   // свес крыши
            if (_tex != null) DrawTextureRect(_tex, r, true, new Color(0.82f, 0.8f, 0.78f, _alpha));
            else DrawRect(r, new Color(0.25f, 0.24f, 0.23f, _alpha));
            // парапет: светлая кромка сверху, тень снизу — крыша читается плоской плитой
            DrawRect(new Rect2(r.Position, new Vector2(r.Size.X, 3)), new Color(1, 0.97f, 0.9f, 0.18f * _alpha));
            DrawRect(new Rect2(r.Position.X, r.End.Y - 4, r.Size.X, 4), new Color(0, 0, 0, 0.35f * _alpha));
            DrawRect(r, new Color(0, 0, 0, 0.45f * _alpha), false, 2);
            // вентиляция, трубы, антенны — стоят на крыше, с тенью по тому же свету
            foreach (var it in _items)
            {
                var tex = Art.Get(it.Sprite);
                if (tex == null) continue;
                var at = Iso.Px(it.Pos) - new Vector2(0, _h);
                Shade.Sprite(this, tex, 1f, 0.3f * _alpha, 2f, at);
                DrawTexture(tex, at + new Vector2(-tex.GetWidth() / 2f, -tex.GetHeight() + 2), new Color(1, 1, 1, _alpha));
            }
        }
    }

    /// <summary>Декали готовых РТ: трещины, разметка, лужи, пятна — плоско на земле, без теней.</summary>
    public partial class FlatDecorLayer : Node2D
    {
        readonly List<MapDecor> _items;

        public FlatDecorLayer(List<MapDecor> items)
        {
            _items = items;
            TextureFilter = TextureFilterEnum.Nearest;
            Material = VisionFx.ObjectMat;
        }

        public override void _Draw()
        {
            foreach (var d in _items)
            {
                var tex = Art.Get(d.Sprite);
                if (tex == null) continue;
                DrawSetTransform(Iso.Px(d.Pos), Mathf.DegToRad(-d.Rot), new Vector2(d.Scale, d.Scale));
                DrawTexture(tex, -tex.GetSize() / 2);
            }
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    /// <summary>Провода ОРУ и ЛЭП: тонкие линии с провисом, на своей высоте.</summary>
    public partial class WireLayer : Node2D
    {
        readonly List<MapWire> _wires;

        public WireLayer(List<MapWire> wires)
        {
            _wires = wires;
            ZIndex = 3;
            Material = VisionFx.ObjectMat;
        }

        public override void _Draw()
        {
            var c = new Color(0.11f, 0.1f, 0.09f, 0.9f);
            var pts = new Vector2[13];
            foreach (var w in _wires)
            {
                var a = Iso.Px(w.A) - new Vector2(0, Iso.H(w.Ha));
                var b = Iso.Px(w.B) - new Vector2(0, Iso.H(w.Hb));
                for (int i = 0; i < pts.Length; i++)
                {
                    float t = i / (float)(pts.Length - 1);
                    pts[i] = a.Lerp(b, t) + new Vector2(0, w.Sag * Iso.PPM * 4 * t * (1 - t));
                }
                DrawPolyline(pts, c, 1);
            }
        }
    }

    /// <summary>Всё «готовых РТ» на клиенте: виды по id, применение снапшота (поднятые роллеты, щитки).</summary>
    public sealed class PrefabLayer
    {
        public readonly Dictionary<int, WallView2D> WallsById = new();
        readonly Dictionary<int, DecorView2D> _decors = new();
        readonly List<RoofView2D> _roofs = new();
        int _version = -1;

        public PrefabLayer(MapInfo map, Node world, Node root)
        {
            var flat = map.Decors.FindAll(d => d.Flat);
            if (flat.Count > 0) root.AddChild(new FlatDecorLayer(flat) { ZIndex = -9 });
            foreach (var d in map.Decors)
            {
                if (d.Flat) continue;
                var v = new DecorView2D(d);
                world.AddChild(v);
                _decors[d.Id] = v;
            }
            if (map.Patches.Count > 0) root.AddChild(new PatchLayer(map.Patches) { ZIndex = -10 });
            if (map.Entrances.Count > 0) root.AddChild(new EntranceLayer(map.Entrances) { ZIndex = -8 });
            if (map.Wires.Count > 0) root.AddChild(new WireLayer(map.Wires));
            foreach (var r in map.Roofs)
            {
                var v = new RoofView2D(r);
                root.AddChild(v);
                _roofs.Add(v);
            }
        }

        public void Sync(Snapshot snap)
        {
            foreach (var (id, pos, rot) in snap.DecorMoves)
                if (_decors.TryGetValue(id, out var mv)) mv.MoveTo(pos, rot);
            if (snap.PrefabVersion == _version) return;
            _version = snap.PrefabVersion;
            var gone = new HashSet<int>(snap.GoneWalls);
            foreach (var (id, v) in WallsById) v.Visible = !gone.Contains(id);
            foreach (var (id, sprite) in snap.DecorSprites)
                if (_decors.TryGetValue(id, out var d)) d.SetSprite(sprite);
        }

        public void Update(Vector2 selfPx, float dt)
        {
            foreach (var d in _decors.Values) d.UpdateOcclusion(dt);
            foreach (var r in _roofs) r.UpdateFade(selfPx, dt);
        }
    }
}
