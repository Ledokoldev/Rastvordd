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
        readonly Body2D _body;
        readonly Gun2D _gun;
        bool _back = false, _dead;

        public ActorView2D(bool isNpc, bool isSelf)
        {
            _body = new Body2D
            {
                Shirt = isSelf ? new Color(0.27f, 0.45f, 0.7f) : isNpc ? new Color(0.5f, 0.47f, 0.3f) : new Color(0.65f, 0.28f, 0.22f),
                Pants = isNpc ? new Color(0.33f, 0.32f, 0.22f) : new Color(0.24f, 0.24f, 0.28f),
                Hair = isNpc ? new Color(0.2f, 0.22f, 0.16f) : new Color(0.28f, 0.18f, 0.1f),
                Helmet = isNpc,
            };
            _gun = new Gun2D { Position = new Vector2(1, -Iso.H(1.05f)) };
            AddChild(_body);
            AddChild(_gun);
        }

        public override void _Draw()
        {
            // тень
            DrawSetTransform(Vector2.Zero, 0, new Vector2(1, 0.45f));
            DrawCircle(Vector2.Zero, _dead ? 16 : 10, new Color(0, 0, 0, 0.28f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        public void SetState(Vector2 pos, float facing, bool dead, bool reloading, float speed, float dt)
        {
            Position = pos;
            bool right = Mathf.Cos(facing) >= 0;
            bool back = Mathf.Sin(facing) < 0;

            if (dead != _dead)
            {
                _dead = dead;
                _body.Dead = dead;
                _gun.Visible = !dead;
                _body.Rotation = dead ? (right ? Mathf.Pi / 2 : -Mathf.Pi / 2) : 0;
                _body.Position = dead ? new Vector2(0, -4) : Vector2.Zero;
                QueueRedraw();
            }
            if (dead) { _body.QueueRedraw(); return; }

            _body.Scale = new Vector2(right ? 1 : -1, 1);
            if (back != _back)
            {
                _back = back;
                MoveChild(_gun, back ? 0 : 1);   // смотрит вверх — оружие за спиной, вниз — перед телом
            }
            _body.Back = back;
            _body.Moving = speed > 0.3f;
            _body.Phase += dt * (4f + speed * 1.6f);
            _body.QueueRedraw();

            _gun.Rotation = facing + (reloading ? (right ? 0.9f : -0.9f) : 0);
            _gun.Scale = new Vector2(1, right ? 1 : -1);
            _gun.Position = new Vector2(right ? 2 : -2, -Iso.H(1.05f) + (_body.Moving ? -Mathf.Abs(Mathf.Sin(_body.Phase)) : 0));
        }
    }

    public partial class Body2D : Node2D
    {
        public Color Shirt, Pants, Hair;
        public Color Skin = new Color(0.86f, 0.68f, 0.55f);
        public bool Back, Moving, Dead, Helmet;
        public float Phase;

        public override void _Draw()
        {
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

    public partial class Gun2D : Node2D
    {
        public override void _Draw()
        {
            var metal = new Color(0.14f, 0.14f, 0.15f);
            DrawRect(new Rect2(2, -2.5f, 24, 5), metal);                     // ствол и корпус
            DrawRect(new Rect2(26, -1.5f, 4, 3), metal.Lightened(0.2f));     // дуло
            DrawRect(new Rect2(-3, -2, 7, 6), new Color(0.42f, 0.28f, 0.15f)); // приклад
            DrawRect(new Rect2(11, 2.5f, 4, 5), metal);                      // магазин
            var skin = new Color(0.86f, 0.68f, 0.55f);
            DrawCircle(new Vector2(4, 1), 2.6f, skin);
            DrawCircle(new Vector2(13, 0), 2.4f, skin);
        }
    }

    // ---------- окружение ----------

    public partial class WallView2D : Node2D
    {
        readonly Rect2 _top, _front;
        readonly Color _topC, _frontC;
        readonly WallKind _kind;

        public WallView2D(WallInfo w)
        {
            _kind = w.Kind;
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
            DrawSetTransform(new Vector2(0, 2), 0, new Vector2(1, 1));
            DrawRect(new Rect2(_front.Position.X + 3, -4, _front.Size.X, 6), new Color(0, 0, 0, 0.2f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
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

    public partial class TreeView2D : Node2D
    {
        readonly float _trunkW;
        public float CanopyR = Iso.PPM * 1.3f;
        public Vector2 CanopyCenter => Position + new Vector2(0, -Iso.H(3.1f));
        float _alpha = 1;

        public TreeView2D(WallInfo w)
        {
            Position = new Vector2((w.Min.X + w.Max.X) * 0.5f * Iso.PPM, w.Max.Y * Iso.PPM);
            _trunkW = (w.Max.X - w.Min.X) * Iso.PPM * 0.7f;
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
            float trunkH = Iso.H(2.6f);
            DrawRect(new Rect2(-_trunkW / 2, -trunkH, _trunkW, trunkH), new Color(0.38f, 0.27f, 0.17f));
            var c = new Vector2(0, -Iso.H(3.1f));
            var leaf = new Color(0.22f, 0.38f, 0.2f, _alpha);
            DrawCircle(c + new Vector2(0, 6), CanopyR, leaf.Darkened(0.2f));
            DrawCircle(c, CanopyR * 0.95f, leaf);
            DrawCircle(c + new Vector2(-8, -9), CanopyR * 0.55f, leaf.Lightened(0.12f));
        }
    }

    public partial class BagView2D : Node2D
    {
        public bool IsCrate, Empty;

        public override void _Draw()
        {
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

        public override void _Draw()
        {
            var tip = new Vector2(0, -Iso.GunH);
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
}
