using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>
    /// Проекция 3/4: земля рисуется как вид сверху (1 м = PPM пикселей), высота объектов — вверх по экрану со сжатием HS.
    /// Все объекты мира стоят в узле с y-sort, их начало координат — точка касания земли.
    /// </summary>
    public static class Iso
    {
        public const float PPM = 32f;
        public const float HS = 0.75f;
        public static readonly float GunH = H(1.15f);
        public static Vector2 Px(SVec v) => new Vector2(v.X * PPM, v.Y * PPM);
        public static float H(float meters) => meters * PPM * HS;
    }

    // ---------- персонаж-заглушка: 4 диагональные позы (левые — зеркало правых) ----------

    public partial class ActorView2D : Node2D
    {
        const float SwingTime = 0.28f;

        readonly Body2D _body;
        readonly Held2D _held;
        bool _back = false, _dead;
        float _swing;

        public ActorView2D(bool isNpc, bool isSelf)
        {
            _body = new Body2D
            {
                Shirt = isSelf ? new Color(0.27f, 0.45f, 0.7f) : isNpc ? new Color(0.5f, 0.47f, 0.3f) : new Color(0.65f, 0.28f, 0.22f),
                Pants = isNpc ? new Color(0.33f, 0.32f, 0.22f) : new Color(0.24f, 0.24f, 0.28f),
                Hair = isNpc ? new Color(0.2f, 0.22f, 0.16f) : new Color(0.28f, 0.18f, 0.1f),
                Helmet = isNpc,
            };
            // спрайты персонажа, если сгенерированы; охрана — свой лист, игроки пока «голые»
            string sheet = isNpc ? "char_guard" : "char_naked";
            _body.Front = Art.CharFrames(sheet, 'f');
            _body.BackFrames = Art.CharFrames(sheet, 'b');
            _body.TextureFilter = TextureFilterEnum.Nearest;
            _held = new Held2D { Position = new Vector2(1, -Iso.H(1.05f)) };
            AddChild(_body);
            AddChild(_held);
        }

        public void StartSwing() => _swing = SwingTime;

        public override void _Draw()
        {
            // тень
            DrawSetTransform(Vector2.Zero, 0, new Vector2(1, 0.45f));
            DrawCircle(Vector2.Zero, _dead ? 16 : 10, new Color(0, 0, 0, 0.28f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        public void SetState(Vector2 pos, float facing, bool dead, bool reloading, float speed, float dt, string heldId)
        {
            Position = pos;
            bool right = Mathf.Cos(facing) >= 0;
            bool back = Mathf.Sin(facing) < 0;

            if (dead != _dead)
            {
                _dead = dead;
                _body.Dead = dead;
                _held.Visible = !dead;
                _body.Rotation = dead ? (right ? Mathf.Pi / 2 : -Mathf.Pi / 2) : 0;
                _body.Position = dead ? new Vector2(0, -4) : Vector2.Zero;
                QueueRedraw();
            }
            if (dead) { _body.QueueRedraw(); return; }

            if (_held.ItemId != heldId) { _held.ItemId = heldId; _held.QueueRedraw(); }

            _body.Scale = new Vector2(right ? 1 : -1, 1);
            if (back != _back)
            {
                _back = back;
                MoveChild(_held, back ? 0 : 1);   // смотрит вверх — предмет за спиной, вниз — перед телом
            }
            _body.Back = back;
            _body.Moving = speed > 0.3f;
            _body.Phase += dt * (4f + speed * 1.6f);
            _body.QueueRedraw();

            // замах: предмет уходит назад и резко бьёт вперёд
            float swingOffset = 0;
            if (_swing > 0)
            {
                _swing -= dt;
                float t = 1f - Mathf.Clamp(_swing / SwingTime, 0, 1);
                swingOffset = t < 0.35f ? Mathf.Lerp(0, -1.3f, t / 0.35f) : Mathf.Lerp(-1.3f, 0.5f, (t - 0.35f) / 0.65f);
                if (!right) swingOffset = -swingOffset;
            }
            _held.Rotation = facing + swingOffset + (reloading ? (right ? 0.9f : -0.9f) : 0);
            _held.Scale = new Vector2(1, right ? 1 : -1);
            _held.Position = new Vector2(right ? 2 : -2, -Iso.H(1.05f) + (_body.Moving ? -Mathf.Abs(Mathf.Sin(_body.Phase)) : 0));
        }
    }

    public partial class Body2D : Node2D
    {
        public Color Shirt, Pants, Hair;
        public Color Skin = new Color(0.86f, 0.68f, 0.55f);
        public bool Back, Moving, Dead, Helmet;
        public float Phase;
        public Texture2D[] Front, BackFrames;

        public override void _Draw()
        {
            var frames = Back ? BackFrames : Front;
            if (frames != null)
            {
                // цикл ходьбы ~7 кадров/с; стоя — первый кадр
                int idx = Moving && !Dead ? (int)(Phase / (Mathf.Pi / 2)) % 4 : 0;
                DrawTexture(frames[idx], new Vector2(-32, -62), Dead ? new Color(0.55f, 0.5f, 0.5f) : Colors.White);
                return;
            }
            var shirt = Dead ? Shirt.Darkened(0.45f) : Shirt;
            var pants = Dead ? Pants.Darkened(0.45f) : Pants;
            float bob = Moving && !Dead ? -Mathf.Abs(Mathf.Sin(Phase)) * 1.5f : 0;
            float l1 = Moving ? Mathf.Max(0, Mathf.Sin(Phase)) * 3 : 0;
            float l2 = Moving ? Mathf.Max(0, -Mathf.Sin(Phase)) * 3 : 0;

            // ноги
            DrawRect(new Rect2(-6, -13 - l1, 5, 13), pants);
            DrawRect(new Rect2(1, -13 - l2, 5, 13), pants.Darkened(0.15f));
            DrawRect(new Rect2(-6, -2 - l1, 5, 2), new Color(0.15f, 0.12f, 0.1f));
            DrawRect(new Rect2(1, -2 - l2, 5, 2), new Color(0.15f, 0.12f, 0.1f));

            // корпус
            var torso = new Rect2(-8, -31 + bob, 16, 19);
            DrawRect(torso, Back ? shirt.Darkened(0.12f) : shirt);
            DrawRect(new Rect2(-8, -14 + bob, 16, 2), shirt.Darkened(0.35f));
            if (!Back) DrawRect(new Rect2(4, -30 + bob, 4, 17), shirt.Lightened(0.12f));   // освещённый бок

            // голова
            var head = new Vector2(1, -38 + bob);
            if (Back)
            {
                DrawCircle(head, 7.5f, Skin.Darkened(0.1f));
                DrawCircle(head + new Vector2(-0.5f, -0.5f), 7.3f, Helmet ? new Color(0.3f, 0.32f, 0.25f) : Hair);
            }
            else
            {
                DrawCircle(head + new Vector2(-1, -1), 7.5f, Helmet ? new Color(0.3f, 0.32f, 0.25f) : Hair);
                DrawCircle(head + new Vector2(1.2f, 1.2f), 6.2f, Skin);
                if (!Dead)
                {
                    DrawRect(new Rect2(head.X + 2, head.Y + 0.5f, 1.6f, 2), new Color(0.1f, 0.08f, 0.08f));
                    DrawRect(new Rect2(head.X + 5, head.Y + 0.5f, 1.6f, 2), new Color(0.1f, 0.08f, 0.08f));
                }
            }
        }
    }

    /// <summary>Предмет в руках: рисуется по id, вращается за прицелом.</summary>
    public partial class Held2D : Node2D
    {
        public string ItemId;

        static readonly Color Metal = new Color(0.14f, 0.14f, 0.15f);
        static readonly Color Steel = new Color(0.55f, 0.56f, 0.58f);
        static readonly Color Wood = new Color(0.45f, 0.3f, 0.16f);
        static readonly Color Skin = new Color(0.86f, 0.68f, 0.55f);
        static readonly Color StoneC = new Color(0.55f, 0.54f, 0.5f);

        public Held2D() => TextureFilter = TextureFilterEnum.Nearest;

        /// <summary>Длина предмета в руках, метры (по ней масштабируется спрайт).</summary>
        static float HeldLength(string id) => id switch
        {
            "tool_rock" => 0.3f,
            "tool_hatchet" or "tool_pickaxe" => 0.65f,
            "weapon_spear" => 1.6f,
            "weapon_bow" => 0.9f,
            "weapon_pipe" => 0.5f,
            "weapon_ak" => 0.95f,
            _ => 0.7f,
        };

        public override void _Draw()
        {
            var tex = Art.HeldSprite(ItemId);
            if (tex != null)
            {
                float len = HeldLength(ItemId) * Iso.PPM;
                float s = len / (tex.GetWidth() * 0.9f);
                DrawSetTransform(new Vector2(len * 0.3f, 0), 0, new Vector2(s, s));
                DrawTexture(tex, new Vector2(-tex.GetWidth() / 2, -tex.GetHeight() / 2));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                DrawCircle(new Vector2(3, 0.5f), 2.4f, Skin);
                return;
            }
            switch (ItemId)
            {
                case null:
                    DrawCircle(new Vector2(6, 0), 2.6f, Skin);
                    break;
                case "tool_rock":
                    DrawCircle(new Vector2(9, 0), 5, StoneC);
                    DrawCircle(new Vector2(8, -1.5f), 2, StoneC.Lightened(0.2f));
                    DrawCircle(new Vector2(5, 1), 2.6f, Skin);
                    break;
                case "tool_hatchet":
                    DrawLine(new Vector2(0, 0), new Vector2(19, 0), Wood, 3);
                    DrawColoredPolygon(new[] { new Vector2(14, -1), new Vector2(21, -7), new Vector2(23, -1), new Vector2(18, 1) }, StoneC);
                    DrawCircle(new Vector2(4, 0), 2.6f, Skin);
                    break;
                case "tool_pickaxe":
                    DrawLine(new Vector2(0, 0), new Vector2(19, 0), Wood, 3);
                    DrawColoredPolygon(new[] { new Vector2(19, -9), new Vector2(22, -2), new Vector2(22, 2), new Vector2(19, 9), new Vector2(18, 0) }, StoneC);
                    DrawCircle(new Vector2(4, 0), 2.6f, Skin);
                    break;
                case "weapon_spear":
                    DrawLine(new Vector2(-8, 0), new Vector2(30, 0), Wood, 2.5f);
                    DrawColoredPolygon(new[] { new Vector2(30, -2.5f), new Vector2(37, 0), new Vector2(30, 2.5f) }, Wood.Lightened(0.3f));
                    DrawCircle(new Vector2(2, 0), 2.6f, Skin);
                    DrawCircle(new Vector2(12, 0), 2.4f, Skin);
                    break;
                case "weapon_bow":
                    DrawArc(new Vector2(6, 0), 11, -1.3f, 1.3f, 16, Wood, 2.5f);
                    DrawLine(new Vector2(6 + 11 * Mathf.Cos(-1.3f), 11 * Mathf.Sin(-1.3f)), new Vector2(6 + 11 * Mathf.Cos(1.3f), 11 * Mathf.Sin(1.3f)), new Color(0.85f, 0.85f, 0.8f), 1);
                    DrawCircle(new Vector2(16.5f, 0), 2.6f, Skin);
                    break;
                case "weapon_pipe":
                    DrawRect(new Rect2(2, -2, 18, 4), Metal);
                    DrawRect(new Rect2(-3, -2, 6, 6), Wood);
                    DrawCircle(new Vector2(4, 1), 2.6f, Skin);
                    DrawCircle(new Vector2(12, 0), 2.4f, Skin);
                    break;
                case "weapon_ak":
                    DrawRect(new Rect2(2, -2.5f, 24, 5), Metal);
                    DrawRect(new Rect2(26, -1.5f, 4, 3), Metal.Lightened(0.2f));
                    DrawRect(new Rect2(-3, -2, 7, 6), new Color(0.42f, 0.28f, 0.15f));
                    DrawRect(new Rect2(11, 2.5f, 4, 5), Metal);
                    DrawCircle(new Vector2(4, 1), 2.6f, Skin);
                    DrawCircle(new Vector2(13, 0), 2.4f, Skin);
                    break;
                default:
                    // устанавливаемое — несём коробку
                    DrawRect(new Rect2(4, -6, 14, 12), ItemId == "deploy_furnace" ? StoneC : Wood);
                    DrawRect(new Rect2(4, -6, 14, 12), new Color(0, 0, 0, 0.3f), false, 1);
                    DrawCircle(new Vector2(4, 0), 2.6f, Skin);
                    break;
            }
        }
    }

    // ---------- окружение ----------

    public partial class WallView2D : Node2D
    {
        readonly Rect2 _top, _front;
        readonly Color _topC, _frontC;
        readonly WallKind _kind;

        readonly Biome _biome;

        public WallView2D(WallInfo w, Biome biome = Biome.Grassland)
        {
            _biome = biome;
            Material = VisionFx.ObjectMat;
            _kind = w.Kind;
            TextureFilter = TextureFilterEnum.Nearest;
            TextureRepeat = TextureRepeatEnum.Enabled;
            float cx = (w.Min.X + w.Max.X) * 0.5f * Iso.PPM;
            Position = new Vector2(cx, w.Max.Y * Iso.PPM);
            float x0 = w.Min.X * Iso.PPM - cx, wd = (w.Max.X - w.Min.X) * Iso.PPM;
            float depth = (w.Max.Y - w.Min.Y) * Iso.PPM, h = Iso.H(w.Height);
            _front = new Rect2(x0, -h, wd, h);
            _top = new Rect2(x0, -h - depth, wd, depth);
            (_topC, _frontC) = w.Kind switch
            {
                WallKind.Crate => (new Color(0.62f, 0.45f, 0.26f), new Color(0.45f, 0.31f, 0.17f)),
                WallKind.Rock => (new Color(0.6f, 0.59f, 0.56f), new Color(0.42f, 0.41f, 0.4f)),
                _ => (new Color(0.7f, 0.69f, 0.66f), new Color(0.5f, 0.49f, 0.47f)),
            };
        }

        public override void _Draw()
        {
            // пропсы-укрытия — спрайтом, по центру «следа», основанием на передний край
            string prop = _kind switch
            {
                WallKind.Car => "props/car_wreck",
                WallKind.Barrier => "props/concrete_barrier",
                WallKind.Junk => "props/junk_pile",
                WallKind.Barrel => "props/barrel_oil",
                WallKind.WoodCrate => "props/crate_wood",
                _ => null,
            };
            if (prop != null)
            {
                var tex = Art.Styled(prop, _biome);
                if (tex != null)
                {
                    float s = _kind == WallKind.Car ? 1.4f : 1f;
                    float depth = _top.Size.Y;
                    DrawSetTransform(new Vector2(0, -depth * 0.5f), 0, new Vector2(1, 0.45f));
                    DrawCircle(new Vector2(3, 0), tex.GetWidth() * 0.5f * s, new Color(0, 0, 0, 0.22f));
                    DrawSetTransform(Vector2.Zero, 0, new Vector2(s, s));
                    DrawTexture(tex, new Vector2(-tex.GetWidth() / 2, -tex.GetHeight() + 2));
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                    return;
                }
            }
            DrawRect(new Rect2(_front.Position.X + 3, -2, _front.Size.X, 6), new Color(0, 0, 0, 0.2f));
            var (frontTex, topTex) = _kind switch
            {
                WallKind.Crate => (Art.Get("walls/wallface_wood"), Art.Get("tiles/floor_wood")),
                WallKind.Rock => (Art.Get("walls/wallface_stone"), Art.Get("tiles/ground_gravel")),
                _ => (Art.Get("walls/wallface_concrete"), Art.Get("tiles/ground_concrete")),
            };
            if (frontTex != null && topTex != null)
            {
                // лицевая сторона темнее (в тени), верх светлее — так читается высота
                DrawTextureRect(frontTex, _front, true, new Color(0.78f, 0.78f, 0.78f));
                DrawTextureRect(topTex, _top, true, new Color(1.05f, 1.05f, 1.05f));
                DrawLine(new Vector2(_top.Position.X, _top.End.Y), _top.End, new Color(0, 0, 0, 0.35f), 1);
                DrawRect(_top, new Color(0, 0, 0, 0.25f), false, 1);
                return;
            }
            DrawRect(_front, _frontC);
            DrawRect(_top, _topC);
            DrawRect(_top, _topC.Darkened(0.2f), false, 1);
            if (_kind == WallKind.Crate)
            {
                for (float y = _front.Position.Y + 6; y < 0; y += 7)
                    DrawLine(new Vector2(_front.Position.X, y), new Vector2(_front.End.X, y), _frontC.Darkened(0.25f), 1);
            }
            else if (_kind == WallKind.Concrete && _front.Size.X > 40)
            {
                for (float x = _front.Position.X + 32; x < _front.End.X; x += 32)
                    DrawLine(new Vector2(x, _front.Position.Y), new Vector2(x, 0), _frontC.Darkened(0.12f), 1);
            }
        }
    }

    /// <summary>Ресурс на карте; при ударе слегка трясётся.</summary>
    public abstract partial class NodeView2D : Node2D
    {
        public Vector2 Base;
        public float Fill = 1;
        float _shake;

        public void Hit() => _shake = 0.18f;

        public void Tick(float dt)
        {
            if (_shake <= 0) return;
            _shake -= dt;
            Position = Base + new Vector2(Mathf.Sin(_shake * 90) * 2.5f * (_shake / 0.18f), 0);
            if (_shake <= 0) Position = Base;
        }
    }

    public partial class TreeView2D : NodeView2D
    {
        readonly float _trunkW;
        readonly Texture2D _tex, _tuft;
        readonly int _seed;
        public float CanopyR = Iso.PPM * 1.3f;
        public Vector2 CanopyCenter => Base + new Vector2(0, _tex != null ? -_tex.GetHeight() * 0.62f : -Iso.H(3.1f));
        float _alpha = 1;

        public TreeView2D(Vector2 basePx, float radiusM, int seed = 0, Biome biome = Biome.Grassland)
        {
            Base = Position = basePx;
            Material = VisionFx.ObjectMat;
            _trunkW = radiusM * 2 * Iso.PPM * 0.7f;
            _tex = Art.Tree(seed, biome);
            _tuft = Art.Get(biome is Biome.Arid or Biome.Tundra or Biome.Beach ? "decals/decal_grass_tuft_2" : "decals/decal_grass_tuft_1");
            _seed = seed;
            if (_tex != null) CanopyR = _tex.GetWidth() * 0.42f;
            TextureFilter = TextureFilterEnum.Nearest;
        }

        /// <summary>Крона полупрозрачна, если под ней игрок — чтобы не терять персонажа.</summary>
        public void SetFade(bool fade, float dt)
        {
            float target = fade ? 0.35f : 1f;
            if (Mathf.Abs(target - _alpha) < 0.01f) return;
            _alpha = Mathf.MoveToward(_alpha, target, dt * 4);
            QueueRedraw();
        }

        public override void _Draw()
        {
            DrawSetTransform(Vector2.Zero, 0, new Vector2(1, 0.45f));
            DrawCircle(new Vector2(10, 0), CanopyR * 0.9f, new Color(0, 0, 0, 0.2f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            if (_tex != null)
            {
                DrawTexture(_tex, new Vector2(-_tex.GetWidth() / 2, -_tex.GetHeight() + 2), new Color(1, 1, 1, _alpha));
                // пучки травы у корней — дерево «вросло» в землю
                if (_tuft != null && false)   // пучки у корней отключены: в пикселе это кляксы
                    for (int i = 0; i < 2; i++)
                    {
                        float ox = ((_seed * (i + 3) * 37) % 17) - 8;
                        DrawTexture(_tuft, new Vector2(ox - _tuft.GetWidth() / 2, -_tuft.GetHeight() + 3 + i));
                    }
                return;
            }
            float trunkH = Iso.H(2.6f);
            DrawRect(new Rect2(-_trunkW / 2, -trunkH, _trunkW, trunkH), new Color(0.38f, 0.27f, 0.17f));
            var c = new Vector2(0, -Iso.H(3.1f));
            var leaf = new Color(0.22f, 0.38f, 0.2f, _alpha);
            DrawCircle(c + new Vector2(0, 6), CanopyR, leaf.Darkened(0.2f));
            DrawCircle(c, CanopyR * 0.95f, leaf);
            DrawCircle(c + new Vector2(-8, -9), CanopyR * 0.55f, leaf.Lightened(0.12f));
        }
    }

    public partial class OreView2D : NodeView2D
    {
        readonly NodeKind _kind;
        readonly Vector2[] _shape;
        readonly Vector2[] _specks;
        readonly Texture2D _tex;

        public OreView2D(Vector2 basePx, NodeKind kind, float radiusM, int seed, Biome biome = Biome.Grassland)
        {
            Base = Position = basePx;
            Material = VisionFx.ObjectMat;
            _kind = kind;
            _tex = Art.Node(kind, seed, biome);
            TextureFilter = TextureFilterEnum.Nearest;
            var rng = new RandomNumberGenerator { Seed = (ulong)seed };
            float r = radiusM * Iso.PPM;
            // неровная глыба: вид 3/4 — сплюснутый многоугольник, поднятый над землёй
            _shape = new Vector2[9];
            for (int i = 0; i < _shape.Length; i++)
            {
                float a = Mathf.Pi + Mathf.Pi * i / (_shape.Length - 1);   // верхняя полуокружность
                float k = rng.RandfRange(0.8f, 1.1f);
                _shape[i] = new Vector2(Mathf.Cos(a) * r * k, Mathf.Sin(a) * r * 1.1f * k);
            }
            _specks = new Vector2[6];
            for (int i = 0; i < _specks.Length; i++)
                _specks[i] = new Vector2(rng.RandfRange(-r * 0.6f, r * 0.6f), rng.RandfRange(-r * 0.9f, -r * 0.15f));
        }

        public override void _Draw()
        {
            float s = 0.55f + 0.45f * Mathf.Sqrt(Mathf.Clamp(Fill, 0, 1));
            DrawSetTransform(Vector2.Zero, 0, new Vector2(1, 0.4f));
            DrawCircle(new Vector2(4, 0), 24 * s, new Color(0, 0, 0, 0.22f));
            DrawSetTransform(Vector2.Zero, 0, new Vector2(s, s));
            if (_tex != null)
            {
                // при добыче глыба уменьшается
                DrawTexture(_tex, new Vector2(-_tex.GetWidth() / 2, -_tex.GetHeight() + 3));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                return;
            }
            var body = new Color(0.5f, 0.49f, 0.46f);
            DrawColoredPolygon(_shape, body);
            // освещённая верхушка
            var top = new Vector2[_shape.Length];
            for (int i = 0; i < top.Length; i++) top[i] = _shape[i] * 0.7f + new Vector2(-2, -3);
            DrawColoredPolygon(top, body.Lightened(0.15f));
            var speck = _kind switch
            {
                NodeKind.Metal => new Color(0.78f, 0.45f, 0.22f),
                NodeKind.Sulfur => new Color(0.9f, 0.82f, 0.25f),
                _ => body.Darkened(0.25f),
            };
            foreach (var p in _specks) DrawCircle(p, _kind == NodeKind.Stone ? 1.5f : 2.6f, speck);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    public partial class PickupView2D : Node2D
    {
        public bool Available = true;
        readonly PickupKind _kind;

        readonly Biome _biome;

        public PickupView2D(PickupKind kind, Biome biome = Biome.Grassland)
        {
            _kind = kind;
            _biome = biome;
            Material = VisionFx.ObjectMat;
            TextureFilter = TextureFilterEnum.Nearest;
        }

        public override void _Draw()
        {
            // спрайт: у собранного куста ягод — просто куст, собранная конопля — пенёк (заглушка ниже)
            var tex = _kind switch
            {
                PickupKind.Hemp => !Available ? null
                    : _biome == Biome.Tundra ? Art.Get("plants/hemp_tundra") ?? Art.Get("plants/hemp_plant")
                    : _biome is Biome.Arid or Biome.Beach ? Art.Get("plants/hemp_arid") ?? Art.Get("plants/hemp_plant")
                    : Art.Get("plants/hemp_plant"),
                PickupKind.Berries => _biome == Biome.Tundra
                    ? (Available ? Art.Get("plants/berry_tundra") : Art.Get("grass/grass_tundra_2")) ?? Art.Get("plants/berry_bush")
                    : Art.Get(Available ? "plants/berry_bush" : "plants/bush_1"),
                _ => Art.Get("props/barrel_water"),
            };
            if (tex != null)
            {
                DrawSetTransform(Vector2.Zero, 0, new Vector2(1, 0.4f));
                DrawCircle(new Vector2(2, 0), tex.GetWidth() * 0.4f, new Color(0, 0, 0, 0.22f));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                DrawTexture(tex, new Vector2(-tex.GetWidth() / 2, -tex.GetHeight() + 2));
                return;
            }
            switch (_kind)
            {
                case PickupKind.Hemp:
                    if (!Available) { DrawLine(new Vector2(-2, 0), new Vector2(-2, -4), new Color(0.4f, 0.35f, 0.2f), 2); break; }
                    var g = new Color(0.36f, 0.55f, 0.24f);
                    for (int i = -3; i <= 3; i++)
                        DrawLine(new Vector2(i, 0), new Vector2(i * 3.2f, -18 - (3 - Mathf.Abs(i)) * 3), i % 2 == 0 ? g : g.Darkened(0.2f), 2);
                    break;
                case PickupKind.Berries:
                    var leaf = new Color(0.2f, 0.36f, 0.18f);
                    DrawCircle(new Vector2(0, -8), 10, Available ? leaf : leaf.Darkened(0.35f));
                    DrawCircle(new Vector2(-5, -11), 6, Available ? leaf.Lightened(0.1f) : leaf.Darkened(0.3f));
                    if (Available)
                        foreach (var p in new[] { new Vector2(-4, -6), new Vector2(3, -10), new Vector2(5, -4), new Vector2(-1, -13), new Vector2(-7, -9) })
                            DrawCircle(p, 1.8f, new Color(0.75f, 0.12f, 0.15f));
                    break;
                case PickupKind.WaterBarrel:
                    var blue = new Color(0.2f, 0.36f, 0.55f);
                    DrawRect(new Rect2(-8, -22, 16, 22), blue);
                    DrawRect(new Rect2(-8, -16, 16, 2), blue.Darkened(0.3f));
                    DrawRect(new Rect2(-8, -7, 16, 2), blue.Darkened(0.3f));
                    DrawSetTransform(new Vector2(0, -22), 0, new Vector2(1, 0.4f));
                    DrawCircle(Vector2.Zero, 8, blue.Lightened(0.2f));
                    DrawCircle(Vector2.Zero, 5.5f, new Color(0.15f, 0.28f, 0.4f));
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                    break;
            }
        }
    }

    public partial class DeployView2D : Node2D
    {
        public bool Burning;
        readonly DeployKind _kind;
        float _flicker;

        readonly PointLight2D _light;
        readonly AudioStreamPlayer2D _crackle;

        public DeployView2D(DeployKind kind)
        {
            _kind = kind;
            Material = VisionFx.ObjectMat;
            TextureFilter = TextureFilterEnum.Nearest;
            if (kind == DeployKind.Furnace)
            {
                _light = Art.FireLight(6f, 0f);
                _light.Position = new Vector2(-1, -10);
                AddChild(_light);
                _crackle = Sfx.Loop("furnace_loop", -8, 14);
                AddChild(_crackle);
            }
        }

        public void Tick(float dt)
        {
            if (_crackle != null && _crackle.Stream != null && Burning != _crackle.Playing) { if (Burning) _crackle.Play(); else _crackle.Stop(); }
            if (_light != null) _light.Energy = Burning ? 0.9f + 0.25f * Mathf.Sin(_flicker * 11) * Mathf.Sin(_flicker * 5.7f) : 0f;
            if (!Burning) return;
            _flicker += dt;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var tex = Art.Get(_kind == DeployKind.Workbench ? "props/workbench" : "props/furnace");
            if (tex != null)
            {
                DrawSetTransform(Vector2.Zero, 0, new Vector2(1, 0.4f));
                DrawCircle(new Vector2(2, 0), tex.GetWidth() * 0.45f, new Color(0, 0, 0, 0.25f));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                DrawTexture(tex, new Vector2(-tex.GetWidth() / 2, -tex.GetHeight() + 2));
                if (_kind == DeployKind.Furnace && Burning)
                {
                    // огонь в топке: 4 кадра по кругу (~10 к/с) + мерцающее свечение
                    float k = 0.75f + 0.25f * Mathf.Sin(_flicker * 13) * Mathf.Sin(_flicker * 7.3f);
                    DrawCircle(new Vector2(-1, -11), 16, new Color(1f, 0.55f, 0.2f, 0.16f * k));
                    DrawCircle(new Vector2(-1, -11), 7, new Color(1f, 0.6f, 0.25f, 0.35f * k));
                    var fire = Art.Get($"fx/fire_{(int)(_flicker * 10) % 4 + 1}");
                    if (fire != null)
                    {
                        float fs = 0.6f;
                        DrawSetTransform(new Vector2(-1, -6), 0, new Vector2(fs, fs));
                        DrawTexture(fire, new Vector2(-fire.GetWidth() / 2, -fire.GetHeight()));
                        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                    }
                }
                return;
            }
            if (_kind == DeployKind.Workbench)
            {
                var wood = new Color(0.5f, 0.34f, 0.18f);
                DrawRect(new Rect2(-20, -20, 4, 20), wood.Darkened(0.3f));
                DrawRect(new Rect2(16, -20, 4, 20), wood.Darkened(0.3f));
                DrawRect(new Rect2(-22, -24, 44, 6), wood.Darkened(0.15f));   // передняя кромка
                DrawRect(new Rect2(-22, -36, 44, 12), wood);                  // столешница
                DrawRect(new Rect2(-14, -34, 10, 4), new Color(0.5f, 0.5f, 0.52f));   // инструменты
                DrawRect(new Rect2(4, -33, 12, 3), new Color(0.35f, 0.35f, 0.38f));
                return;
            }
            var stone = new Color(0.45f, 0.44f, 0.42f);
            DrawRect(new Rect2(-14, -26, 28, 26), stone);
            DrawRect(new Rect2(-14, -32, 28, 8), stone.Lightened(0.12f));
            DrawRect(new Rect2(-6, -16, 12, 10), new Color(0.08f, 0.07f, 0.07f));   // топка
            if (Burning)
            {
                float k = 0.75f + 0.25f * Mathf.Sin(_flicker * 13) * Mathf.Sin(_flicker * 7.3f);
                DrawRect(new Rect2(-5, -14, 10, 8), new Color(1f, 0.55f, 0.15f, k));
                DrawCircle(new Vector2(0, -10), 18, new Color(1f, 0.55f, 0.2f, 0.12f * k));
            }
        }
    }

    public partial class BagView2D : Node2D
    {
        public bool IsCrate, Empty;

        public BagView2D()
        {
            TextureFilter = TextureFilterEnum.Nearest;
            Material = VisionFx.ObjectMat;
        }

        public override void _Draw()
        {
            var tex = Art.Get(IsCrate ? "props/crate_military" : "props/loot_bag");
            if (tex != null)
            {
                // ящики и сумки делаем чуть крупнее реального — иначе их не видно
                float s = IsCrate ? 1.4f : 1.3f;
                DrawSetTransform(Vector2.Zero, 0, new Vector2(1, 0.4f));
                DrawCircle(new Vector2(2, 0), tex.GetWidth() * 0.5f * s, new Color(0, 0, 0, 0.25f));
                DrawSetTransform(Vector2.Zero, 0, new Vector2(s, s));
                DrawTexture(tex, new Vector2(-tex.GetWidth() / 2, -tex.GetHeight() + 1), Empty ? new Color(0.45f, 0.45f, 0.45f) : Colors.White);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                return;
            }
            float w = IsCrate ? 34 : 18, d = IsCrate ? 18 : 12, h = IsCrate ? 16 : 10;
            var top = IsCrate ? (Empty ? new Color(0.22f, 0.25f, 0.18f) : new Color(0.36f, 0.48f, 0.26f)) : new Color(0.55f, 0.4f, 0.22f);
            var front = top.Darkened(0.3f);
            DrawRect(new Rect2(-w / 2, -h, w, h), front);
            DrawRect(new Rect2(-w / 2, -h - d, w, d), top);
            if (IsCrate && !Empty) DrawRect(new Rect2(-3, -h + 3, 6, 5), new Color(0.85f, 0.75f, 0.3f));
        }
    }

    public partial class TracerView2D : Node2D
    {
        public Vector2 Dir;
        public bool Arrow;

        public override void _Draw()
        {
            var tip = new Vector2(0, -Iso.GunH);
            if (Arrow)
            {
                DrawLine(tip - Dir * 14, tip, new Color(0.55f, 0.4f, 0.22f), 2);
                DrawLine(tip - Dir * 3, tip, new Color(0.75f, 0.75f, 0.75f), 2);
                return;
            }
            var tail = tip - Dir * 16;
            DrawLine(tail, tip, new Color(1, 0.8f, 0.35f, 0.5f), 4);
            DrawLine(tail + Dir * 6, tip, new Color(1, 0.95f, 0.7f), 2);
        }
    }

    public partial class FlashView2D : Node2D
    {
        public Color Color;
        public float Radius, Life, Max;

        public override void _Draw()
        {
            float k = Mathf.Clamp(Life / Max, 0, 1);
            DrawCircle(new Vector2(0, -Iso.GunH), Radius * (0.6f + 0.4f * k), new Color(Color, k));
        }
    }

    /// <summary>Всплывающая надпись над точкой мира: «+16 Дерево».</summary>
    public partial class FloatText2D : Node2D
    {
        public string Text;
        public Color Color = Colors.White;
        public float Life = 1.2f;
        const float MaxLife = 1.2f;

        public bool Tick(float dt)
        {
            Life -= dt;
            QueueRedraw();
            return Life > 0;
        }

        public override void _Draw()
        {
            float t = 1f - Life / MaxLife;
            var font = ThemeDB.FallbackFont;
            var pos = new Vector2(-80, -Iso.H(2.2f) - t * 22);
            var c = new Color(Color, Mathf.Clamp(Life / 0.4f, 0, 1));
            DrawStringOutline(font, pos, Text, HorizontalAlignment.Center, 160, 13, 4, new Color(0, 0, 0, c.A * 0.8f));
            DrawString(font, pos, Text, HorizontalAlignment.Center, 160, 13, c);
        }
    }

    /// <summary>Призрак установки под курсором: зелёный — можно поставить, красный — далеко.</summary>
    public partial class GhostView2D : Node2D
    {
        public bool Ok;

        public override void _Draw()
        {
            var c = Ok ? new Color(0.4f, 1f, 0.45f, 0.45f) : new Color(1f, 0.3f, 0.25f, 0.45f);
            DrawSetTransform(Vector2.Zero, 0, new Vector2(1, 0.55f));
            DrawCircle(Vector2.Zero, 0.65f * Iso.PPM, c);
            DrawArc(Vector2.Zero, 0.65f * Iso.PPM, 0, Mathf.Tau, 32, new Color(c, 0.9f), 1.5f, true);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}
