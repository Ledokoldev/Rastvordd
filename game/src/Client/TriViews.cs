using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>Пол треугольного фундамента: та же текстура и кромка, что у квадратного.</summary>
    public partial class TriFloorView2D : Node2D
    {
        readonly Vector2[] _pts;
        readonly int _tier;
        const float Lip = 4;

        public TriFloorView2D(SVec a, SVec b, SVec c, int tier)
        {
            _tier = tier;
            ZIndex = -8;
            var center = Iso.Px((a + b + c) / 3f);
            Position = center;
            _pts = new[] { Iso.Px(a) - center, Iso.Px(b) - center, Iso.Px(c) - center };
            Material = VisionFx.ObjectMat;
            TextureFilter = TextureFilterEnum.Nearest;
            TextureRepeat = TextureRepeatEnum.Enabled;
        }

        public override void _Draw()
        {
            var (tex, tint) = FloorLook(_tier);
            var top = new Vector2[3];
            var shadow = new Vector2[3];
            for (int i = 0; i < 3; i++) { top[i] = _pts[i] + new Vector2(0, -Lip); shadow[i] = _pts[i] + new Vector2(3, 2); }
            DrawColoredPolygon(shadow, new Color(0, 0, 0, 0.22f));
            DrawColoredPolygon(_pts, new Color(0, 0, 0, _tier == 0 ? 0.15f : 0.35f));   // кромка
            if (tex != null)
            {
                var uv = new Vector2[3];
                for (int i = 0; i < 3; i++) uv[i] = (top[i] + Position) / tex.GetSize();
                DrawPolygon(top, new[] { tint, tint, tint }, uv, tex);
            }
            else DrawColoredPolygon(top, new Color(0.5f, 0.4f, 0.3f, tint.A));
            var line = _tier == 0 ? new Color(0.6f, 0.8f, 1f, 0.6f) : new Color(0, 0, 0, 0.3f);
            for (int i = 0; i < 3; i++) DrawLine(top[i], top[(i + 1) % 3], line, 1);
        }

        public static (Texture2D tex, Color tint) FloorLook(int tier) => tier switch
        {
            0 => (Art.Get("tiles/floor_wood"), new Color(0.75f, 0.85f, 1.05f, 0.55f)),
            1 => (Art.Get("tiles/floor_wood"), new Color(1, 1, 1)),
            2 => (Art.Get("walls/wallface_stone"), new Color(1.05f, 1.05f, 1.05f)),
            3 => (Art.Get("walls/wallface_metal"), new Color(1, 1, 1)),
            _ => (Art.Get("walls/wallface_metal"), new Color(0.7f, 0.72f, 0.78f)),
        };
    }

    /// <summary>
    /// Наклонная стена (на ребре треугольника): ровная призма — лицевая грань и верх, текстуры по уровню, как у обычной стены.
    /// Физика у неё — цепочка мелких прямоугольников, а рисуется гладко.
    /// </summary>
    public partial class SlantWallView2D : Node2D
    {
        readonly Vector2 _f0, _f1, _b0, _b1;   // нижние края лицевой (ближе к камере) и задней стороны, px относительно Position
        readonly float _h;
        readonly int _tier;
        readonly bool _door;
        float _alpha = 1;
        bool _cut;

        public SlantWallView2D(SVec p0, SVec p1, float heightM, int tier, bool door)
        {
            _tier = tier; _door = door;
            Material = VisionFx.ObjectMat;
            TextureFilter = TextureFilterEnum.Nearest;
            TextureRepeat = TextureRepeatEnum.Enabled;
            var d = SVec.Normalize(p1 - p0);
            var n = new SVec(-d.Y, d.X);
            if (n.Y < 0) n = -n;                      // лицевая сторона — к камере (вниз по экрану)
            float th = Building.Thick * 0.5f;
            var f0 = Iso.Px(p0 + n * th); var f1 = Iso.Px(p1 + n * th);
            var b0 = Iso.Px(p0 - n * th); var b1 = Iso.Px(p1 - n * th);
            Position = new Vector2((f0.X + f1.X) * 0.5f, Mathf.Max(f0.Y, f1.Y));   // глубина — по нижнему краю
            _f0 = f0 - Position; _f1 = f1 - Position; _b0 = b0 - Position; _b1 = b1 - Position;
            _h = Iso.H(heightM);
        }

        public override void _Draw()
        {
            float h = _cut ? Mathf.Min(_h, Iso.H(0.45f)) : _h;
            var up = new Vector2(0, -h);
            var (frontTex, topTex) = WallView2D.BuiltTextures(_tier, _door);
            var tint = _tier == 0 ? new Color(0.85f, 0.92f, 1.05f) : _tier >= 5 ? new Color(0.44f, 0.46f, 0.52f) : _tier >= 4 ? new Color(0.62f, 0.66f, 0.74f) : Colors.White;
            var fc = new Color(0.78f * tint.R, 0.78f * tint.G, 0.78f * tint.B, _alpha);
            var tc = new Color(1.05f * tint.R, 1.05f * tint.G, 1.05f * tint.B, _alpha);
            DrawColoredPolygon(new[] { _f0 + new Vector2(3, 0), _f1 + new Vector2(3, 0), _f1 + new Vector2(3, 4), _f0 + new Vector2(3, 4) }, new Color(0, 0, 0, 0.18f));
            var front = new[] { _f0, _f1, _f1 + up, _f0 + up };
            var top = new[] { _f0 + up, _f1 + up, _b1 + up, _b0 + up };
            if (frontTex != null && topTex != null)
            {
                float len = _f0.DistanceTo(_f1);
                var fuv = new[] { new Vector2(0, 1), new Vector2(len / frontTex.GetWidth(), 1), new Vector2(len / frontTex.GetWidth(), 1 - h / frontTex.GetHeight()), new Vector2(0, 1 - h / frontTex.GetHeight()) };
                DrawPolygon(front, new[] { fc, fc, fc, fc }, fuv, frontTex);
                var tuv = new Vector2[4];
                for (int i = 0; i < 4; i++) tuv[i] = (top[i] + Position) / topTex.GetSize();
                DrawPolygon(top, new[] { tc, tc, tc, tc }, tuv, topTex);
            }
            else
            {
                DrawColoredPolygon(front, new Color(0.45f, 0.36f, 0.25f, _alpha));
                DrawColoredPolygon(top, new Color(0.6f, 0.5f, 0.35f, _alpha));
            }
            DrawLine(_f0 + up, _f1 + up, new Color(0, 0, 0, 0.35f), 1);
            DrawPolyline(new[] { top[0], top[1], top[2], top[3], top[0] }, new Color(0, 0, 0, 0.25f), 1);
        }

        /// <summary>Что стена занимает на экране (пиксели мира): лицевая грань + верх.</summary>
        public Rect2 ScreenRect => new(Position + new Vector2(Mathf.Min(_f0.X, _f1.X), Mathf.Min(_b0.Y, _b1.Y) - _h),
            new Vector2(Mathf.Abs(_f1.X - _f0.X) + 1, Mathf.Max(_f0.Y, _f1.Y) - Mathf.Min(_b0.Y, _b1.Y) + _h));

        /// <summary>Прозрачность по фокусам кадра (Occlusion); срезанная стена не растворяется.</summary>
        public void UpdateOcclusion(float dt) => SetFade(!_cut && Occlusion.Covers(ScreenRect, Position.Y), dt);

        public void SetCut(bool cut) { if (cut != _cut) { _cut = cut; QueueRedraw(); } }

        public void SetFade(bool fade, float dt)
        {
            if (Occlusion.Step(ref _alpha, fade, dt)) QueueRedraw();
        }
    }
}
