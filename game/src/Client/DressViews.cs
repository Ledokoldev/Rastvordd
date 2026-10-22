using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>
    /// Обстановка РТ (Sim/Dressing.cs) на клиенте: мелочь пачками по рядам (плакаты на стенах, вещи на столах, хлам на полу),
    /// затенение у стен, теплотрассы, живое — дым, пар, флаги, вороны, бумажки, лампы.
    /// </summary>
    public partial class DecorRow : Node2D
    {
        readonly List<(Texture2D tex, Vector2 at, bool flip, float lift)> _items = new();
        WallView2D[] _walls;           // на какой стене висит (плакат растворяется вместе со стеной)
        float[] _lastA;

        public DecorRow(List<MapDecor> items)
        {
            float minX = float.MaxValue, maxY = float.MinValue;
            foreach (var d in items) { minX = Mathf.Min(minX, d.Pos.X); maxY = Mathf.Max(maxY, d.Pos.Y); }
            Position = Iso.Px(new SVec(minX, maxY));
            foreach (var d in items)
            {
                var tex = Art.Get(d.Sprite);
                if (tex == null) continue;
                _items.Add((tex, Iso.Px(d.Pos) - Position, d.Flip, d.Lift));
            }
            TextureFilter = TextureFilterEnum.Nearest;
            Material = VisionFx.ObjectMat;
        }

        /// <summary>Привязать висящее к стенам: лицо стены сразу севернее предмета и по x накрывает его.</summary>
        public bool LinkWalls(List<WallView2D> walls)
        {
            WallView2D[] found = null;
            for (int k = 0; k < _items.Count; k++)
            {
                var (_, at, _, lift) = _items[k];
                if (lift < 0.3f) continue;
                var p = at + Position;
                foreach (var w in walls)
                {
                    if (w.Position.Y > p.Y || p.Y - w.Position.Y > 6 || p.X < w.FaceX0 || p.X > w.FaceX1) continue;
                    found ??= new WallView2D[_items.Count];
                    found[k] = w;
                    break;
                }
            }
            if (found == null) return false;
            _walls = found;
            _lastA = new float[_items.Count];
            return true;
        }

        public void SyncAlpha()
        {
            bool dirty = false;
            for (int k = 0; k < _walls.Length; k++)
                if (_walls[k] != null && Mathf.Abs(_walls[k].Alpha - _lastA[k]) > 0.01f) { _lastA[k] = _walls[k].Alpha; dirty = true; }
            if (dirty) QueueRedraw();
        }

        public override void _Draw()
        {
            for (int k = 0; k < _items.Count; k++)
            {
                var (tex, at, flip, lift) = _items[k];
                float a = _walls?[k] != null ? _walls[k].Alpha : 1f;
                if (a < 0.02f) continue;
                var p = at - new Vector2(0, Mathf.Round(Iso.H(lift)));
                if (lift < 0.05f) Shade.Sprite(this, tex, 1f, 0.22f, 2f, at);
                DrawSetTransform(p, 0, new Vector2(flip ? -1 : 1, 1));
                DrawTexture(tex, new Vector2(-Mathf.Floor(tex.GetWidth() / 2f), -tex.GetHeight() + 2), new Color(1, 1, 1, a));
            }
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    /// <summary>
    /// Затенение у стен РТ: внутри — мягкая «пыльная» тень вдоль плинтуса (углы темнее сами — полосы складываются),
    /// снаружи — шире и бурее: грязь и сырость у фундамента. Рисуется на земле под объектами.
    /// </summary>
    public partial class WallAoLayer : Node2D
    {
        readonly List<(Rect2 r, Color c)> _bands = new();

        public WallAoLayer(MapInfo map)
        {
            Material = VisionFx.GroundMat;
            bool Inside(SVec p)
            {
                foreach (var r in map.Roofs) if (p.X > r.Min.X && p.X < r.Max.X && p.Y > r.Min.Y && p.Y < r.Max.Y) return true;
                return false;
            }
            bool NearRoof(SVec p)
            {
                foreach (var r in map.Roofs) if (p.X > r.Min.X - 1.2f && p.X < r.Max.X + 1.2f && p.Y > r.Min.Y - 1.2f && p.Y < r.Max.Y + 1.2f) return true;
                return false;
            }
            foreach (var w in map.Walls)
            {
                if (w.Hidden || w.Tex == null) continue;
                var c = (w.Min + w.Max) * 0.5f;
                if (!NearRoof(c)) continue;   // заборы — без затенения (их много, и они на открытом месте)
                for (int side = 0; side < 4; side++)
                {
                    var n = side switch { 0 => new SVec(0, 1), 1 => new SVec(0, -1), 2 => new SVec(1, 0), _ => new SVec(-1, 0) };
                    float half = side < 2 ? (w.Max.Y - w.Min.Y) / 2 : (w.Max.X - w.Min.X) / 2;
                    bool inside = Inside(c + n * (half + 0.4f));
                    float width = inside ? 1.0f : 1.1f;
                    var col = inside ? new Color(0.02f, 0.02f, 0.015f) : new Color(0.1f, 0.075f, 0.04f);
                    float a0 = inside ? 0.36f : 0.32f;
                    const int steps = 10;
                    for (int i = 0; i < steps; i++)
                    {
                        float d0 = width * i / steps, d1 = width * (i + 1) / steps;
                        float a = a0 * (1 - i / (float)steps) * (1 - i / (float)steps);
                        SVec mn, mx;
                        switch (side)
                        {
                            case 0: mn = new SVec(w.Min.X, w.Max.Y + d0); mx = new SVec(w.Max.X, w.Max.Y + d1); break;
                            case 1: mn = new SVec(w.Min.X, w.Min.Y - d1); mx = new SVec(w.Max.X, w.Min.Y - d0); break;
                            case 2: mn = new SVec(w.Max.X + d0, w.Min.Y); mx = new SVec(w.Max.X + d1, w.Max.Y); break;
                            default: mn = new SVec(w.Min.X - d1, w.Min.Y); mx = new SVec(w.Min.X - d0, w.Max.Y); break;
                        }
                        _bands.Add((new Rect2(Iso.Px(mn), Iso.Px(mx) - Iso.Px(mn)), new Color(col, a)));
                    }
                }
            }
        }

        public override void _Draw()
        {
            foreach (var (r, c) in _bands) DrawRect(r, c);
        }
    }

    /// <summary>Цвет масляной панели по типу комнаты, если южная грань стены смотрит внутрь комнаты РТ.</summary>
    public static class DressPaint
    {
        public static Color? For(MapInfo map, WallInfo w)
        {
            if (w.Tex.Contains("door") || w.Tex.Contains("garage") || w.Tex.Contains("shutter") || w.Tex.Contains("fence")) return null;
            var p = new SVec((w.Min.X + w.Max.X) * 0.5f, w.Max.Y + 0.3f);
            foreach (var r in map.Rooms)
            {
                if (p.X < r.Min.X || p.X > r.Max.X || p.Y < r.Min.Y || p.Y > r.Max.Y) continue;
                return r.Kind switch
                {
                    "barracks" or "corridor" => new Color(0.24f, 0.36f, 0.27f),
                    "office" => new Color(0.26f, 0.33f, 0.43f),
                    "control" => new Color(0.29f, 0.38f, 0.34f),
                    "kitchen" => new Color(0.27f, 0.39f, 0.43f),
                    "washroom" => new Color(0.3f, 0.43f, 0.46f),
                    "medbay" => new Color(0.52f, 0.58f, 0.6f),
                    "shop" => new Color(0.36f, 0.27f, 0.2f),
                    "electrical" => new Color(0.32f, 0.34f, 0.37f),
                    "workshop" => new Color(0.3f, 0.29f, 0.26f),
                    _ => new Color(0.33f, 0.33f, 0.31f),
                };
            }
            return null;
        }
    }

    /// <summary>Участок наземной теплотрассы: прямой отрезок на высоте (две трубы в изоляции), подъём или опуск, опоры с тенью.</summary>
    public partial class PipeSegView : Node2D
    {
        readonly Vector2 _a, _b;   // точки на земле, px (относительно узла)
        readonly float _ha, _hb;   // высоты, px экрана
        readonly int _seed;

        static readonly Color Ins = new(0.62f, 0.62f, 0.6f), InsDark = new(0.36f, 0.36f, 0.35f), Rust = new(0.45f, 0.27f, 0.15f), Seam = new(0.3f, 0.3f, 0.29f);

        public PipeSegView(SVec a, float ha, SVec b, float hb, int seed)
        {
            var pa = Iso.Px(a); var pb = Iso.Px(b);
            Position = new Vector2(Mathf.Min(pa.X, pb.X), Mathf.Max(pa.Y, pb.Y));
            _a = pa - Position; _b = pb - Position;
            _ha = Iso.H(ha); _hb = Iso.H(hb);
            _seed = seed;
            Material = VisionFx.ObjectMat;
        }

        // одна труба: «цилиндр» из полос, сверху блик, снизу тень; швы изоляции поперёк, местами — рыжая ободранная изоляция
        void Tube(Vector2 p0, Vector2 p1, float r, bool horizontal)
        {
            var rng = new RandomNumberGenerator { Seed = (ulong)(_seed * 977 + (int)p0.X * 13 + (int)p0.Y) };
            float len = horizontal ? Mathf.Abs(p1.X - p0.X) : Mathf.Abs(p1.Y - p0.Y);
            int seg = Mathf.Max(1, (int)(len / 32));
            for (int s = 0; s < seg; s++)
            {
                float t0 = s / (float)seg, t1 = (s + 1) / (float)seg;
                var q0 = p0.Lerp(p1, t0); var q1 = p0.Lerp(p1, t1);
                bool torn = rng.Randf() < 0.18f;
                var body = torn ? Rust : Ins * (0.92f + rng.Randf() * 0.12f);
                Rect2 rect = horizontal
                    ? new Rect2(Mathf.Min(q0.X, q1.X), q0.Y - r, Mathf.Abs(q1.X - q0.X), 2 * r)
                    : new Rect2(q0.X - r, Mathf.Min(q0.Y, q1.Y), 2 * r, Mathf.Abs(q1.Y - q0.Y));
                DrawRect(rect, body);
                // объём: светлая полоса сверху/слева, тёмная снизу/справа
                if (horizontal)
                {
                    DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, 1)), new Color(1, 1, 0.95f, 0.35f));
                    DrawRect(new Rect2(rect.Position.X, rect.End.Y - 2, rect.Size.X, 2), new Color(InsDark, 0.9f));
                    DrawRect(new Rect2(rect.Position.X, rect.Position.Y, 1, rect.Size.Y), new Color(Seam, 0.8f));
                }
                else
                {
                    DrawRect(new Rect2(rect.Position, new Vector2(1, rect.Size.Y)), new Color(1, 1, 0.95f, 0.3f));
                    DrawRect(new Rect2(rect.End.X - 2, rect.Position.Y, 2, rect.Size.Y), new Color(InsDark, 0.9f));
                    DrawRect(new Rect2(rect.Position.X, rect.Position.Y, rect.Size.X, 1), new Color(Seam, 0.8f));
                }
                if (torn && horizontal) DrawRect(new Rect2(rect.Position.X + 3, rect.End.Y - 1, Mathf.Max(1, rect.Size.X * 0.3f), 2), new Color(0.6f, 0.58f, 0.5f, 0.8f));   // свисающий клок
            }
        }

        // бетонная опора от земли до трубы
        void Support(Vector2 ground, float h)
        {
            var c = new Color(0.47f, 0.46f, 0.43f);
            DrawRect(new Rect2(ground.X - 5, ground.Y - h, 10, h), c);
            DrawRect(new Rect2(ground.X + 3, ground.Y - h, 2, h), new Color(0, 0, 0, 0.25f));
            DrawRect(new Rect2(ground.X - 6, ground.Y - h - 2, 12, 3), new Color(0.3f, 0.29f, 0.27f));
            // тень опоры на земле
            DrawColoredPolygon(new[] { ground + new Vector2(-5, 0), ground + new Vector2(5, 0), ground + new Vector2(5 + Shade.Skew * h, -Shade.Flat * h), ground + new Vector2(-5 + Shade.Skew * h, -Shade.Flat * h) }, new Color(0, 0, 0, 0.18f));
        }

        public override void _Draw()
        {
            const float r = 3.5f, gap = 9f;   // радиус трубы и расстояние между двумя трубами (px)
            if (_a.DistanceTo(_b) < 0.5f)
            {
                // вертикальный подъём П-образного компенсатора
                float h0 = Mathf.Min(_ha, _hb), h1 = Mathf.Max(_ha, _hb);
                for (int k = 0; k < 2; k++)
                {
                    var g = _a + new Vector2(k * gap - gap / 2, -k * 4);
                    Tube(g - new Vector2(0, h1 + r), g - new Vector2(0, h0 - r), r, false);
                }
                return;
            }
            bool horiz = Mathf.Abs(_b.Y - _a.Y) < 0.5f;
            float h = _ha;
            // тень на земле — полосой, сдвинутой по свету
            var o = new Vector2(Shade.Skew * h, -Shade.Flat * h);
            if (horiz)
            {
                float x0 = Mathf.Min(_a.X, _b.X), x1 = Mathf.Max(_a.X, _b.X);
                DrawRect(new Rect2(x0 + o.X, _a.Y + o.Y - 8, x1 - x0, 9), new Color(0, 0, 0, 0.16f));
                for (float x = x0 + 16; x < x1 - 8; x += 96) Support(new Vector2(x, _a.Y), h);
                // дальняя труба выше на экране, ближняя ниже
                Tube(new Vector2(x0, _a.Y - h - 6), new Vector2(x1, _a.Y - h - 6), r, true);
                Tube(new Vector2(x0, _a.Y - h + 2), new Vector2(x1, _a.Y - h + 2), r, true);
            }
            else
            {
                float y0 = Mathf.Min(_a.Y, _b.Y), y1 = Mathf.Max(_a.Y, _b.Y);
                DrawRect(new Rect2(_a.X + o.X - 8, y0 + o.Y, 18, y1 - y0), new Color(0, 0, 0, 0.16f));
                for (float y = y0 + 16; y < y1 - 8; y += 96) Support(new Vector2(_a.X, y), h);
                Tube(new Vector2(_a.X - gap / 2, y0 - h), new Vector2(_a.X - gap / 2, y1 - h), r, false);
                Tube(new Vector2(_a.X + gap / 2, y0 - h), new Vector2(_a.X + gap / 2, y1 - h), r, false);
            }
        }
    }

    public sealed class AmbCrow { public Vector2 Off, Vel; public float H, Phase; }

    public partial class AmbFlock : Node2D
    {
        public readonly List<AmbCrow> Crows = new();
        public bool Flying;
        public float T, Back;
        public override void _Draw()
        {
            foreach (var c in Crows)
            {
                int f = Flying ? 4 + (int)(T * 12 + c.Phase * 4) % 4 : ((int)(T * 1.6f + c.Phase * 7) % 9) switch { 0 => 1, 1 => 2, 2 => 1, 5 => 3, _ => 0 };
                if (Flying) DrawCircle(c.Off + new Vector2(Shade.Skew * c.H, -Shade.Flat * c.H), 2.5f, new Color(0, 0, 0, Mathf.Max(0, 0.25f - c.H / 400f)));
                Vfx.DrawFrame(this, "crow", c.Off - new Vector2(0, c.H), 0, 0, 1, null, f);
            }
        }
    }

    public partial class AmbFlag : Node2D
    {
        public float T, Phase;
        public float H;
        public override void _Draw()
        {
            var pole = new Vector2(0, 0);
            DrawLine(pole, pole - new Vector2(0, H + 34), new Color(0.2f, 0.2f, 0.2f), 2);
            DrawLine(pole - new Vector2(1, H + 34), pole - new Vector2(1, 0), new Color(0.55f, 0.55f, 0.55f, 0.5f), 1);
            Vfx.DrawFrame(this, "flag_red", pole - new Vector2(-14, H - 6), T, 7f, 1, null, (int)Phase);
        }
    }

    public partial class AmbLamp : Node2D
    {
        public float T, Phase, H;
        public bool Flicker;
        public PointLight2D Light;
        public override void _Draw()
        {
            // провод от потолка и абажур, качается маятником
            float ang = Mathf.Sin(T * 1.3f + Phase) * 0.12f;
            var top = new Vector2(0, -Iso.H(H + 0.9f));
            var end = top + new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)) * Iso.H(0.7f);
            DrawLine(top, end, new Color(0.1f, 0.1f, 0.1f, 0.9f), 1);
            var tex = Art.Get("interior/hanging_lamp");
            if (tex != null) DrawTexture(tex, end - new Vector2(tex.GetWidth() / 2f, 1));
            else DrawCircle(end + new Vector2(0, 3), 3, new Color(0.25f, 0.35f, 0.25f));
        }
    }


    /// <summary>Живое на РТ: вороны взлетают при приближении, флаги и бумажки треплет ветер, лампы качаются и мигают, из труб — дым и пар.</summary>
    public sealed class AmbientLayer
    {
        sealed class Emitter { public string Kind; public Vector2 Px; public float H, Next; public Rect2? Roof; }
        sealed class Paper { public Vector2 Home, Pos, Vel; public float T, Phase; public Node2D Node; }

        readonly List<AmbFlock> _flocks = new();
        readonly List<AmbFlag> _flags = new();
        readonly List<AmbLamp> _lamps = new();
        readonly List<Emitter> _emit = new();
        readonly List<Paper> _papers = new();
        readonly RandomNumberGenerator _rng = new() { Seed = 777 };
        float _t;

        public AmbientLayer(MapInfo map, Node world, Node root)
        {
            foreach (var a in map.Ambients)
            {
                var px = Iso.Px(a.Pos);
                switch (a.Kind)
                {
                    case "crows":
                    {
                        var f = new AmbFlock { Position = px, Material = VisionFx.ObjectMat, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
                        int n = 3 + _rng.RandiRange(0, 3);
                        for (int i = 0; i < n; i++) f.Crows.Add(new AmbCrow { Off = new Vector2(_rng.RandfRange(-40, 40), _rng.RandfRange(-14, 14)), Phase = _rng.Randf() });
                        world.AddChild(f);
                        _flocks.Add(f);
                        break;
                    }
                    case "flag":
                    {
                        var f = new AmbFlag { Position = px, H = Iso.H(a.H), Phase = _rng.RandiRange(0, 7), ZIndex = 5, Material = VisionFx.ObjectMat, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
                        root.AddChild(f);   // на крыше — поверх крыш, как вентиляция
                        _flags.Add(f);
                        break;
                    }
                    case "lamp":
                    case "lamp_flicker":
                    {
                        var l = new AmbLamp { Position = px, H = a.H, Phase = _rng.Randf() * 6, Flicker = a.Kind == "lamp_flicker", Material = VisionFx.ObjectMat, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
                        world.AddChild(l);
                        if (l.Flicker)
                        {
                            l.Light = new PointLight2D
                            {
                                Texture = Art.LightTex, TextureScale = 5f * Iso.PPM * 2 / 256f, Color = new Color(1f, 0.85f, 0.6f), Energy = 0,
                                Position = px + new Vector2(0, -Iso.H(1.2f)), BlendMode = Light2D.BlendModeEnum.Add, ShadowEnabled = false,
                            };
                            world.AddChild(l.Light);
                        }
                        _lamps.Add(l);
                        break;
                    }
                    case "candle":
                    {
                        var light = new PointLight2D
                        {
                            Texture = Art.LightTex, TextureScale = 2.5f * Iso.PPM * 2 / 256f, Color = new Color(1f, 0.7f, 0.35f), Energy = 0.5f,
                            Position = px + new Vector2(0, -6), BlendMode = Light2D.BlendModeEnum.Add, ShadowEnabled = false,
                        };
                        world.AddChild(light);
                        var l = new AmbLamp { Position = px, Light = light, Flicker = true, H = -1 };
                        _lamps.Add(l);   // только мерцание света, без абажура
                        break;
                    }
                    case "smoke":
                    case "steam":
                    {
                        var em = new Emitter { Kind = a.Kind, Px = px, H = Iso.H(a.H), Next = _rng.Randf() * 2 };
                        // дым из трубы на крыше: когда игрок внутри (крыша растворена), не дымить посреди комнаты
                        foreach (var r in map.Roofs)
                            if (a.Pos.X > r.Min.X && a.Pos.X < r.Max.X && a.Pos.Y > r.Min.Y && a.Pos.Y < r.Max.Y)
                                em.Roof = new Rect2(Iso.Px(r.Min), Iso.Px(r.Max) - Iso.Px(r.Min)).Grow(6);
                        _emit.Add(em);
                    }
                        break;
                    case "papers":
                    {
                        var node = new Node2D { Position = px, Material = VisionFx.ObjectMat, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
                        var p = new Paper { Home = px, Pos = px, Phase = _rng.Randf() * 10, Node = node };
                        node.Draw += () => Vfx.DrawFrame(node, "paper_flutter", new Vector2(0, -2 - Mathf.Abs(Mathf.Sin(p.T * 2.3f + p.Phase)) * 10), p.T, 9f, 1, null, (int)p.Phase);
                        world.AddChild(node);
                        _papers.Add(p);
                        break;
                    }
                }
            }
        }

        public void Update(Vector2 selfPx, float dt, float night)
        {
            _t += dt;
            const float near = 1100;
            foreach (var f in _flocks)
            {
                bool close = f.Position.DistanceTo(selfPx) < near;
                f.Visible = close || f.Flying;
                if (!close && !f.Flying) continue;
                f.T += dt;
                if (!f.Flying)
                {
                    if (f.Back > 0) { f.Back -= dt; f.Visible = false; if (f.Back <= 0 && f.Position.DistanceTo(selfPx) > 15 * Iso.PPM) ResetFlock(f); else if (f.Back <= 0) f.Back = 5; continue; }
                    // подпустили ближе 6 м — взлёт с карканьем
                    if (f.Position.DistanceTo(selfPx) < 6.5f * Iso.PPM)
                    {
                        f.Flying = true; f.T = 0;
                        var away = (f.Position - selfPx).Normalized();
                        foreach (var c in f.Crows) c.Vel = (away + new Vector2(_rng.RandfRange(-0.5f, 0.5f), _rng.RandfRange(-0.4f, 0.2f))).Normalized() * _rng.RandfRange(90, 140);
                        Sfx.Play(Sfx.Pick("crow_caw", "swing"), f.Position, -10, 35, 0.15f);
                        Sfx.Play(Sfx.Pick("crow_flap", "swing"), f.Position, -14, 20, 0.2f);
                    }
                    else
                    {
                        // иногда ворона перепрыгивает
                        if (_rng.Randf() < dt * 0.3f) { var c = f.Crows[_rng.RandiRange(0, f.Crows.Count - 1)]; c.Off += new Vector2(_rng.RandfRange(-8, 8), _rng.RandfRange(-3, 3)); }
                    }
                }
                else
                {
                    foreach (var c in f.Crows) { c.Off += c.Vel * dt; c.H += dt * 70; }
                    if (f.T > 4) { f.Flying = false; f.Back = _rng.RandfRange(40, 70); f.Visible = false; }
                }
                f.QueueRedraw();
            }
            foreach (var f in _flags) { f.T += dt; if (f.Position.DistanceTo(selfPx) < near) f.QueueRedraw(); }
            foreach (var l in _lamps)
            {
                l.T += dt;
                bool close = l.Position.DistanceTo(selfPx) < near;
                if (l.H >= 0 && close) l.QueueRedraw();
                if (l.Light != null)
                {
                    // мигает: то горит, то пропадает, то дрожит — сломанный патрон; свечка дрожит мягко
                    float e;
                    if (l.H < 0) e = 0.45f + 0.15f * Mathf.Sin(_t * 9 + l.Phase) + _rng.RandfRange(-0.08f, 0.08f);
                    else
                    {
                        float k = Mathf.Sin(_t * 0.7f + l.Phase * 3);
                        e = k > 0.6f ? (_rng.Randf() < 0.5f ? 0 : 0.9f) : k > -0.2f ? 0.85f : 0.05f;
                    }
                    l.Light.Energy = close ? e * Mathf.Lerp(0.35f, 1f, night) : 0;
                    l.Light.Enabled = close;
                }
            }
            foreach (var e in _emit)
            {
                if (e.Px.DistanceTo(selfPx) > near || (e.Roof is Rect2 rr && rr.HasPoint(selfPx))) continue;
                if ((e.Next -= dt) > 0) continue;
                if (e.Kind == "smoke") { Vfx.Puff("smoke_wisp", e.Px, e.H, rise: 16, alpha: 0.55f, fps: 5, z: 5); e.Next = _rng.RandfRange(0.5f, 1.1f); }
                else { Vfx.Puff(Vfx.Tex("steam_rise") != null ? "steam_rise" : "steam_puff", e.Px, e.H, rise: 12, alpha: 0.6f, fps: 7, z: 3); e.Next = _rng.RandfRange(0.35f, 0.9f); }
            }
            foreach (var p in _papers)
            {
                if (p.Home.DistanceTo(selfPx) > near) continue;
                p.T += dt;
                // ветер гоняет листок по кругу у дома, порывами
                float gust = Mathf.Max(0, Mathf.Sin(p.T * 0.6f + p.Phase));
                p.Vel = new Vector2(Mathf.Cos(p.T * 0.4f + p.Phase), Mathf.Sin(p.T * 0.53f + p.Phase * 2) * 0.5f) * 40 * gust;
                p.Pos += p.Vel * dt;
                p.Pos = p.Home + (p.Pos - p.Home).LimitLength(4 * Iso.PPM);
                p.Node.Position = p.Pos.Round();
                p.Node.QueueRedraw();
            }
        }

        void ResetFlock(AmbFlock f)
        {
            foreach (var c in f.Crows) { c.Off = new Vector2(_rng.RandfRange(-40, 40), _rng.RandfRange(-14, 14)); c.H = 0; c.Vel = Vector2.Zero; }
            f.Visible = true;
            f.T = 0;
        }
    }
}
