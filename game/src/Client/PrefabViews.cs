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
        readonly bool _flip;
        float _alpha = 1;
        Texture2D _tex;
        Rect2 _screen;

        public DecorView2D(MapDecor d)
        {
            Position = Iso.Px(d.Pos);
            _flip = d.Flip;
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

        public void UpdateOcclusion(float dt)
        {
            if (Occlusion.Step(ref _alpha, Occlusion.Covers(_screen, Position.Y), dt)) QueueRedraw();
        }

        public override void _Draw()
        {
            if (_tex == null) return;
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
