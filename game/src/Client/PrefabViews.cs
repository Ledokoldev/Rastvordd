using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>Спрайт готовой РТ (трансформатор, портал, шкаф…): основанием в точке, тень от силуэта, прозрачность заслона.</summary>
    /// <summary>
    /// Ночные огни: фонари, лампы, светильники среди декора светят в темноте (PointLight2D, тёплый или холодный свет),
    /// яркость по ночи — из Main (Set). Карты нормалей (_n.png) дают объём и от этих огней.
    /// </summary>
    public static class NightLights
    {
        static readonly System.Collections.Generic.List<PointLight2D> All = new();
        static float _k = -1;

        /// <summary>Свет у спрайта: радиус, м (0 — нет), цвет, высота лампы (доля высоты спрайта).</summary>
        public static (float size, Color col, float top) Kind(string sprite)
        {
            if (sprite == null) return (0, default, 0);
            if (sprite.Contains("streetlight_concrete")) return (9f, new Color(1f, 0.78f, 0.5f), 0.92f);   // натриевый фонарь
            if (sprite.Contains("streetlight")) return (6f, new Color(1f, 0.86f, 0.62f), 0.9f);
            if (sprite.Contains("red_lamp")) return (4f, new Color(1f, 0.25f, 0.18f), 0.5f);           // аварийка
            if (sprite.Contains("lamp_wall") || sprite.Contains("lantern")) return (5f, new Color(1f, 0.8f, 0.55f), 0.5f);
            if (sprite.Contains("top_lamp")) return (3f, new Color(1f, 0.85f, 0.6f), 0.6f);
            return (0, default, 0);
        }

        public static PointLight2D Make(float sizeM, Color col, Vector2 at)
        {
            var l = new PointLight2D
            {
                Texture = Art.LightTex, TextureScale = sizeM * Iso.PPM * 2 / 256f, Energy = 0f, Color = col, Position = new Vector2(at.X, Mathf.Min(0, at.Y * 0.08f)),   // пятно света — на земле у основания (вид 3/4), а не на высоте плафона
                BlendMode = Light2D.BlendModeEnum.Add, Enabled = false, Height = -at.Y,   // лампа на высоте столба: объём по нормалям
            };
            l.TreeExiting += () => All.Remove(l);
            All.Add(l);
            if (_k >= 0) l.Energy = _k;
            return l;
        }

        /// <summary>Ночь 0..1 → яркость огней (днём выключены, чтобы не тратить кадр).</summary>
        public static void Set(float night)
        {
            float k = Mathf.Clamp((night - 0.25f) / 0.5f, 0f, 1f) * 1.1f;
            if (Mathf.Abs(k - _k) >= 0.01f) { _k = k; foreach (var l in All) l.Energy = k; _cullAt = 0; }
            if (Engine.GetProcessFrames() >= _cullAt) { _cullAt = Engine.GetProcessFrames() + 20; Cull(); }
        }

        // включены только ближайшие к камере огни (не больше Cap): цена кадра не растёт с числом фонарей на карте
        const int Cap = 16;
        static ulong _cullAt;
        static float[] _d = System.Array.Empty<float>();
        static PointLight2D[] _l = System.Array.Empty<PointLight2D>();

        static void Cull()
        {
            bool on = _k > 0.02f;
            var cam = (Engine.GetMainLoop() as SceneTree)?.Root?.GetCamera2D();
            if (!on || cam == null || All.Count <= Cap) { foreach (var l in All) l.Enabled = on; return; }
            var c = cam.GetScreenCenterPosition();
            int n = All.Count;
            if (_d.Length < n) { _d = new float[n * 2]; _l = new PointLight2D[n * 2]; }
            for (int i = 0; i < n; i++) { _l[i] = All[i]; _d[i] = All[i].GlobalPosition.DistanceSquaredTo(c); }
            System.Array.Sort(_d, _l, 0, n);
            for (int i = 0; i < n; i++) _l[i].Enabled = i < Cap;
            System.Array.Clear(_l, 0, n);
        }
    }

    public partial class DecorView2D : Node2D
    {
        public override void _EnterTree() { AddToGroup(Shade.Group); ArtScale.Apply(this); }   // тень по солнцу (Shade.SetSun), дев-масштаб (F7)

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
            if (NightLights.Kind(d.Sprite) is var (size, col, top) && size > 0 && _tex != null)
                { AddChild(NightLights.Make(size, col, new Vector2(0, -_tex.GetHeight() * top - d.Lift * Iso.PPM)));}
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
        readonly int _pass;   // дороги: 0 — все проходы, 1 — кромка, 2 — обочина и грунтовки, 3 — асфальт (куски одного прохода идут подряд — порядок слоёв сохраняется)

        public PatchLayer(List<MapPatch> patches, int pass = 0)
        {
            _patches = patches;
            _pass = pass;
            TextureFilter = TextureFilterEnum.Nearest;
            TextureRepeat = TextureRepeatEnum.Enabled;
            Material = VisionFx.GroundMat;
        }

        public override void _Draw()
        {
            DrawRoads();
            foreach (var p in _patches)
            {
                if (p.Road != 0) continue;   // дороги мира — раньше, под участками РТ
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
                if (p.Slab > 0) { DrawSlabs(r, p.Slab == 2); continue; }
                // мягкий край: тёмная кромка, чтобы участок не выглядел наклеенным
                DrawRect(r, new Color(0, 0, 0, 0.12f), false, 2);
            }
        }
    }

    /// <summary>
    /// Дороги мира (RoadNet.cs): полосы-многоугольники «левый край вперёд, правый назад». Порядок: рваная тёмная
    /// кромка в траву → гравийная обочина → грунтовки → асфальт с выкрошенным краем, редкой разметкой, трещинами и ямами.
    /// Всё без размытия: пиксельные полосы и линии.
    /// </summary>
    public partial class PatchLayer
    {
        static float Hash(Vector2 v, int k)
        {
            uint h = (uint)((int)v.X * 73856093) ^ (uint)((int)v.Y * 19349663) ^ (uint)(k * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xffff) / 65535f;
        }

        void DrawRoadPoly(MapPatch p)
        {
            // обочина чуть светлее (пыльный гравий), асфальт чуть темнее — края читаются
            // по четырёхугольнику на сегмент: на крутом изгибе полоса может перехлестнуть сама себя — без триангуляции это не страшно
            var tex = Art.Get(p.Tile);
            var size = tex?.GetSize() ?? Vector2.One;
            var col = tex == null ? new Color(0.35f, 0.34f, 0.3f) : p.Road == 3 ? new Color(1.14f, 1.11f, 1.04f) : p.Road == 1 ? new Color(0.88f, 0.88f, 0.9f) : Colors.White;
            int n = p.Poly.Length, k = n / 2;
            for (int j = 0; j < k - 1; j++)
            {
                var q = new[] { Iso.Px(p.Poly[j]), Iso.Px(p.Poly[j + 1]), Iso.Px(p.Poly[n - 2 - j]), Iso.Px(p.Poly[n - 1 - j]) };
                DrawPrimitive(q, new[] { col, col, col, col }, new[] { q[0] / size, q[1] / size, q[2] / size, q[3] / size }, tex);
            }
        }

        /// <summary>Лежит ли точка на дороге мира (с запасом ~0.4 м): мелочь-декали туда не кладутся.</summary>
        public static bool OnRoad(List<MapPatch> patches, SVec q)
        {
            foreach (var pt in patches)
            {
                if (pt.Road == 0 || pt.Poly == null) continue;
                if (q.X < pt.Min.X - 0.5f || q.X > pt.Max.X + 0.5f || q.Y < pt.Min.Y - 0.5f || q.Y > pt.Max.Y + 0.5f) continue;
                bool inside = false;
                for (int i = 0, j = pt.Poly.Length - 1; i < pt.Poly.Length; j = i++)
                {
                    var a = pt.Poly[i]; var b = pt.Poly[j];
                    if ((a.Y > q.Y) != (b.Y > q.Y) && q.X < (b.X - a.X) * (q.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
                }
                if (inside) return true;
            }
            return false;
        }

        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col) => DrawPrimitive(new[] { a, b, c, d }, new[] { col, col, col, col }, null);

        // край полосы: точки стороны и нормаль наружу (пиксели)
        static void Side(MapPatch p, bool right, List<Vector2> pts, List<Vector2> nrm)
        {
            pts.Clear(); nrm.Clear();
            int k = p.Poly.Length / 2;
            for (int j = 0; j < k; j++)
            {
                var l = Iso.Px(p.Poly[j]); var r = Iso.Px(p.Poly[p.Poly.Length - 1 - j]);
                var n = (l - r).Normalized();
                pts.Add(right ? r : l); nrm.Add(right ? -n : n);
            }
        }

        readonly List<Vector2> _sp = new(), _sn = new();

        /// <summary>Рваная полоса вдоль края: w0..w1 пикселей от кромки (отрицательные — внутрь).</summary>
        void Band(MapPatch p, float w0, float w1, Color c, int salt)
        {
            for (int s = 0; s < 2; s++)
            {
                Side(p, s == 1, _sp, _sn);
                for (int j = 0; j < _sp.Count - 1; j++)
                {
                    float a = w0 + (w1 - w0) * Hash(_sp[j], salt + s), b = w0 + (w1 - w0) * Hash(_sp[j + 1], salt + s);
                    var mid = (_sp[j] + _sp[j + 1]) * 0.5f;
                    float m = w0 + (w1 - w0) * Hash(mid, salt + 7 + s);
                    var nm = ((_sn[j] + _sn[j + 1]) * 0.5f).Normalized();
                    // два полу-четырёхугольника: середина отрезка тоже «рвёт» край
                    Quad(_sp[j], mid, mid + nm * m, _sp[j] + _sn[j] * a, c);
                    Quad(mid, _sp[j + 1], _sp[j + 1] + _sn[j + 1] * b, mid + nm * m, c);
                }
            }
        }

        void DrawRoads()
        {
            bool any = false;
            foreach (var p in _patches) if (p.Road != 0 && p.Poly != null) { any = true; break; }
            if (!any) return;
            // 1. кромка в траве: вытоптано и темнее
            if (_pass is 0 or 1)
            foreach (var p in _patches)
                if (p.Road is 2 or 3 && p.Poly != null)
                {
                    Band(p, 2, 9, new Color(0.13f, 0.11f, 0.06f, 0.16f), 11);
                    Band(p, 0, 4, new Color(0.1f, 0.08f, 0.05f, 0.22f), 23);
                }
            // 2. обочина, 3. грунтовки
            if (_pass is 0 or 2) foreach (var p in _patches) if (p.Road == 3 && p.Poly != null) DrawRoadPoly(p);
            if (_pass is 0 or 2)
            foreach (var p in _patches)
                if (p.Road == 2 && p.Poly != null)
                {
                    DrawRoadPoly(p);
                    Band(p, -5, -1, new Color(0.08f, 0.06f, 0.03f, 0.14f), 31);
                    DrawTrackMarks(p);
                }
            // 4. асфальт
            if (_pass is 0 or 3)
            foreach (var p in _patches)
                if (p.Road == 1 && p.Poly != null)
                {
                    DrawRoadPoly(p);
                    Band(p, -6, -2, new Color(0.06f, 0.06f, 0.05f, 0.22f), 41);    // выкрошенный край
                    Band(p, -2, 0, new Color(0.32f, 0.29f, 0.22f, 0.35f), 43);     // гравий в щербинах
                    DrawAsphaltMarks(p);
                }
        }

        /// <summary>Колея: две тёмные полосы, между ними — травка.</summary>
        void DrawTrackMarks(MapPatch p)
        {
            int k = p.Poly.Length / 2;
            for (int j = 0; j < k - 1; j++)
            {
                var l0 = Iso.Px(p.Poly[j]); var r0 = Iso.Px(p.Poly[p.Poly.Length - 1 - j]);
                var l1 = Iso.Px(p.Poly[j + 1]); var r1 = Iso.Px(p.Poly[p.Poly.Length - 2 - j]);
                if ((l0 - r0).Length() > 5 * Iso.PPM) continue;   // широкий выезд у РТ — без колеи
                foreach (float f in new[] { 0.3f, 0.7f })
                    DrawLine(l0.Lerp(r0, f), l1.Lerp(r1, f), new Color(0.05f, 0.04f, 0.02f, 0.2f), 4);
                if (Hash(l0, 5) < 0.7f) DrawLine(l0.Lerp(r0, 0.5f), l1.Lerp(r1, 0.5f), new Color(0.3f, 0.38f, 0.16f, 0.22f), 5);
            }
        }

        /// <summary>Асфальт: блёклая прерывистая осевая (местами стёрта), трещины, редкие ямы.</summary>
        void DrawAsphaltMarks(MapPatch p)
        {
            int k = p.Poly.Length / 2;
            for (int j = 0; j < k - 1; j++)
            {
                var l0 = Iso.Px(p.Poly[j]); var r0 = Iso.Px(p.Poly[p.Poly.Length - 1 - j]);
                var l1 = Iso.Px(p.Poly[j + 1]); var r1 = Iso.Px(p.Poly[p.Poly.Length - 2 - j]);
                var c0 = (l0 + r0) * 0.5f; var c1 = (l1 + r1) * 0.5f;
                float w = (l0 - r0).Length();
                if (w > 7 * Iso.PPM) continue;
                if (j % 3 == 0)
                {
                    float h = Hash(c0, 3);
                    if (h > 0.22f) DrawLine(c0, c0.Lerp(c1, 0.75f), new Color(0.86f, 0.83f, 0.7f, 0.12f + 0.3f * (h - 0.22f)), 3);
                }
                float hc = Hash(c0, 9);
                if (hc < 0.3f)
                {
                    // трещина: ломаная от случайной точки поперёк полосы
                    var across = (l0 - r0) / w;
                    var along = (c1 - c0).Normalized();
                    var q = c0 + across * (Hash(c0, 10) - 0.5f) * w * 0.7f;
                    var pts = new Vector2[4];
                    pts[0] = q;
                    for (int i = 1; i < 4; i++)
                    {
                        var d = along * (Hash(q, 20 + i) * 2 - 1) + across * (Hash(q, 30 + i) * 2 - 1);
                        pts[i] = pts[i - 1] + d.Normalized() * (6 + 8 * Hash(q, 40 + i));
                    }
                    DrawPolyline(pts, new Color(0.03f, 0.03f, 0.03f, 0.4f), 1);
                }
                else if (hc > 0.965f)
                {
                    // яма: неровный тёмный многоугольник со светлой кромкой
                    var across = (l0 - r0) / w;
                    var cc = c0.Lerp(c1, 0.5f) + across * (Hash(c0, 11) - 0.5f) * w * 0.5f;
                    float rad = 5 + 6 * Hash(c0, 12);
                    var poly = new Vector2[7];
                    for (int i = 0; i < 7; i++)
                    {
                        float ang = i * Mathf.Tau / 7;
                        poly[i] = cc + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.75f) * rad * (0.7f + 0.5f * Hash(cc, 50 + i));
                    }
                    DrawColoredPolygon(poly, new Color(0.05f, 0.05f, 0.04f, 0.5f));
                    DrawPolyline(poly[..4], new Color(0.6f, 0.58f, 0.5f, 0.25f), 1);
                }
            }
        }
    }

    public partial class PatchLayer
    {
        /// <summary>Тропа из плит ПАГ: плиты по 2 м с тёмными швами, у каждой свой оттенок, края в земле.</summary>
        void DrawSlabs(Rect2 r, bool alongY)
        {
            var rng = new RandomNumberGenerator { Seed = (ulong)(Mathf.Abs(r.Position.X * 3 + r.Position.Y * 7) + 1) };
            float step = 2 * Iso.PPM;
            float len = alongY ? r.Size.Y : r.Size.X, wid = alongY ? r.Size.X : r.Size.Y;
            int across = wid > 2.6f * Iso.PPM ? 2 : 1;
            for (float t = 0; t < len; t += step)
                for (int k = 0; k < across; k++)
                {
                    float s0 = wid * k / across, s1 = wid * (k + 1) / across;
                    var cell = alongY ? new Rect2(r.Position.X + s0, r.Position.Y + t, s1 - s0, Mathf.Min(step, len - t)) : new Rect2(r.Position.X + t, r.Position.Y + s0, Mathf.Min(step, len - t), s1 - s0);
                    float v = rng.RandfRange(-0.08f, 0.1f);
                    DrawRect(cell, v > 0 ? new Color(1, 1, 0.95f, v) : new Color(0, 0, 0, -v));
                    DrawRect(cell, new Color(0.05f, 0.05f, 0.04f, 0.55f), false, 1.5f);
                    if (rng.Randf() < 0.25f) DrawRect(new Rect2(cell.Position + new Vector2(2, 2), new Vector2(3, 3)), new Color(0.2f, 0.18f, 0.12f, 0.6f));   // монтажная петля
                }
            DrawRect(r, new Color(0.12f, 0.1f, 0.06f, 0.4f), false, 3);   // края в грязи
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
            Material = VisionFx.GroundMat;
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
        public override void _EnterTree() { AddToGroup(Shade.Group); ArtScale.Apply(this); }   // тень по солнцу (Shade.SetSun), дев-масштаб (F7)

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
            Material = VisionFx.GroundMat;
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
            Material = VisionFx.GroundMat;
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
        AmbientLayer _ambient;
        readonly List<DecorRow> _liftRows = new();
        bool _linked;
        int _version = -1;

        /// <summary>Дорога-полоса («левый край вперёд, правый назад», k пар) → куски по n пар с общей парой на стыке.</summary>
        static IEnumerable<MapPatch> SplitRoad(MapPatch p, int n)
        {
            if (p.Poly == null || p.Poly.Length < 4) { yield return p; yield break; }
            int k = p.Poly.Length / 2;
            if (k <= n) { yield return p; yield break; }
            for (int s = 0; s < k - 1; s += n)
            {
                int e = System.Math.Min(k - 1, s + n);
                int m = e - s + 1;
                var poly = new SVec[m * 2];
                for (int j = 0; j < m; j++) { poly[j] = p.Poly[s + j]; poly[m * 2 - 1 - j] = p.Poly[p.Poly.Length - 1 - (s + j)]; }
                var q = p; q.Poly = poly;
                yield return q;
            }
        }

        /// <summary>Разложить по клеткам 32 м (порядок внутри клетки сохраняется).</summary>
        static List<List<T>> ByCell<T>(List<T> items, System.Func<T, SVec> pos)
        {
            var cells = new Dictionary<(int, int), List<T>>();
            var order = new List<List<T>>();
            foreach (var it in items)
            {
                var p = pos(it);
                var k = ((int)System.MathF.Floor(p.X / 32f), (int)System.MathF.Floor(p.Y / 32f));
                if (!cells.TryGetValue(k, out var l)) { cells[k] = l = new List<T>(); order.Add(l); }
                l.Add(it);
            }
            return order;
        }

        public PrefabLayer(MapInfo map, Node world, Node root)
        {
            // слои «на всю карту» режем на куски 32 м: один узел на весь остров не отсекается камерой и рисуется
            // целиком каждый кадр (в центре острова было ~6,7 тыс. вызовов отрисовки и −50 % FPS) (27.10)
            foreach (var g in ByCell(map.Decors.FindAll(d => d.Flat), d => d.Pos)) root.AddChild(new FlatDecorLayer(g) { ZIndex = -9 });
            var batch = new Dictionary<(int, int), List<MapDecor>>();
            foreach (var d in map.Decors)
            {
                if (d.Flat) continue;
                if (d.Batch)
                {
                    // обстановка: пачками по рядам 0,1 м и кускам 24 м — меньше узлов, сортировка по глубине сохраняется
                    var key = ((int)System.MathF.Floor(d.Pos.Y / 0.1f), (int)System.MathF.Floor(d.Pos.X / 24f));
                    if (!batch.TryGetValue(key, out var l)) batch[key] = l = new List<MapDecor>();
                    l.Add(d);
                    continue;
                }
                var v = new DecorView2D(d);
                world.AddChild(v);
                _decors[d.Id] = v;
            }
            foreach (var l in batch.Values)
            {
                var row = new DecorRow(l);
                world.AddChild(row);
                if (l.Exists(d => d.Lift > 0.3f)) _liftRows.Add(row);
            }
            if (map.Patches.Count > 0)
            {
                var roads = map.Patches.FindAll(p => p.Road != 0);   // дороги — одним слоем: полосы кромка → обочина → асфальт рисуются проходами по всей сети
                var pieces = new List<MapPatch>();
                foreach (var r in roads) pieces.AddRange(SplitRoad(r, 12));
                var rc = ByCell(pieces, p => p.Poly != null ? p.Poly[0] : (p.Min + p.Max) * 0.5f);
                for (int pass = 1; pass <= 3; pass++) foreach (var g in rc) root.AddChild(new PatchLayer(g, pass) { ZIndex = -10 });
                foreach (var g in ByCell(map.Patches.FindAll(p => p.Road == 0), p => p.Poly != null ? p.Poly[0] : (p.Min + p.Max) * 0.5f)) root.AddChild(new PatchLayer(g) { ZIndex = -10 });
            }
            if (map.Rooms.Count > 0)
                foreach (var g in ByCell(WallAoLayer.Build(map), b => new SVec(b.r.Position.X / Iso.PPM, b.r.Position.Y / Iso.PPM))) root.AddChild(new WallAoLayer(g) { ZIndex = -9 });
            int seed = 0;
            foreach (var p in map.Pipes)
                for (int i = 0; i < p.P.Length - 1; i++)
                    world.AddChild(new PipeSegView(p.P[i], p.H[i], p.P[i + 1], p.H[i + 1], seed++));
            if (System.Environment.GetEnvironmentVariable("NOAMB") == null) _ambient = new AmbientLayer(map, world, root);
            foreach (var g in ByCell(map.Entrances, e => e.A)) root.AddChild(new EntranceLayer(g) { ZIndex = -8 });
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

        public void Update(Vector2 selfPx, float dt, float night = 0)
        {
            _ambient?.Update(selfPx, dt, night);
            if (!_linked)
            {
                _linked = true;
                var walls = new List<WallView2D>(WallsById.Values);
                _liftRows.RemoveAll(r => !r.LinkWalls(walls));
            }
            foreach (var r in _liftRows) r.SyncAlpha();
            foreach (var d in _decors.Values) d.UpdateOcclusion(dt);
            foreach (var r in _roofs) r.UpdateFade(selfPx, dt);
        }
    }
}
