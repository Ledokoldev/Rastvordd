using Godot;

namespace Rastvordd.Client
{
    /// <summary>Набор частей тела (кат-аут) для персонажа. Нет всех файлов — null, тогда используются кадры.</summary>
    public sealed class RigSet
    {
        public Texture2D HeadF, HeadB, TorsoF, TorsoB, Pelvis, UpperArm, ForeArm, Thigh, Shin;

        public static RigSet Load(string name)
        {
            Texture2D T(string p) => Art.Get($"rig/rig_{name}_{p}");
            var s = new RigSet
            {
                HeadF = T("head_f"), HeadB = T("head_b"), TorsoF = T("torso_f"), TorsoB = T("torso_b"), Pelvis = T("pelvis"),
                UpperArm = T("upperarm"), ForeArm = T("forearm"), Thigh = T("thigh"), Shin = T("shin"),
            };
            return s.HeadF != null && s.HeadB != null && s.TorsoF != null && s.TorsoB != null && s.Pelvis != null
                   && s.UpperArm != null && s.ForeArm != null && s.Thigh != null && s.Shin != null ? s : null;
        }
    }

    /// <summary>Что сейчас делает персонаж — заполняет ActorView2D каждый кадр.</summary>
    public sealed class RigState
    {
        public float Aim;            // угол прицела (мир), радианы
        public float Recoil;         // увод ствола, радианы
        public float Speed;          // м/с
        public bool Sprint, Crouch, Dead;
        public float AimDown;        // 0..1: прицеливание (ПКМ) — оружие у головы
        public float Phase;          // фаза шага
        public string Held;          // id предмета в руках
        public float Kick;           // 0..1 отдача
        public float Reload = -1;    // 0..1 или −1
        public float Swing = -1;     // 0..1 или −1
        public float Breath;         // для дыхания в стойке
    }

    /// <summary>
    /// Управляемый персонаж из частей: рисуется в маленький буфер 1:1 (каждый пиксель — пиксель арта),
    /// поэтому повёрнутые руки и ноги ложатся на пиксельную сетку и остаются чёткими. Буфер показывается спрайтом в мире.
    /// </summary>
    public partial class RigView : Node2D
    {
        public const int VW = 112, VH = 96;
        public static readonly Vector2 Feet = new(56, 90);

        readonly SubViewport _vp;
        readonly RigCanvas _canvas;
        readonly Sprite2D _sprite;
        public RigState S => _canvas.S;

        public RigView(RigSet set)
        {
            _vp = new SubViewport
            {
                Size = new Vector2I(VW, VH), TransparentBg = true, Disable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest,
                Snap2DTransformsToPixel = true, Snap2DVerticesToPixel = true,
            };
            AddChild(_vp);
            _canvas = new RigCanvas(set) { TextureFilter = TextureFilterEnum.Nearest };
            _vp.AddChild(_canvas);
            _sprite = new Sprite2D { Texture = _vp.GetTexture(), Centered = false, Offset = -Feet, TextureFilter = TextureFilterEnum.Nearest };
            AddChild(_sprite);
        }

        public void Refresh(bool dead)
        {
            // смерть: лежит на боку
            _sprite.Rotation = dead ? Mathf.Pi / 2 * (Mathf.Cos(S.Aim) >= 0 ? 1 : -1) : 0;
            _sprite.Position = dead ? new Vector2(0, -6) : Vector2.Zero;
            _sprite.Modulate = dead ? new Color(0.6f, 0.55f, 0.55f) : Colors.White;
            _canvas.QueueRedraw();
        }
    }

    public partial class RigCanvas : Node2D
    {
        public readonly RigState S = new();
        readonly RigSet _set;

        public RigCanvas(RigSet set) => _set = set;

        static bool TwoHanded(string id) => id is "weapon_ak" or "weapon_pipe" or "weapon_bow" or "weapon_spear" or "tool_pickaxe" or "tool_hatchet";
        static bool IsTool(string id) => id is "tool_rock" or "tool_hatchet" or "tool_pickaxe" or "weapon_spear";

        static float HeldLength(string id) => id switch
        {
            "tool_rock" => 0.28f, "tool_hatchet" or "tool_pickaxe" => 0.62f, "weapon_spear" => 1.5f,
            "weapon_bow" => 0.85f, "weapon_pipe" => 0.5f, "weapon_ak" => 0.92f, _ => 0.6f,
        };

        bool _mirror;

        /// <summary>Рисует часть: позиция в «правом» пространстве (смотрим вправо), поворот, точка крепления в текстуре.</summary>
        void Part(Texture2D t, Vector2 pos, float rot, Vector2 pivot, float sy = 1f)
        {
            if (t == null) return;
            var p = pos + RigView.Feet;
            if (_mirror) { p.X = RigView.VW - p.X; rot = -rot; }
            DrawSetTransform(p.Round(), rot, new Vector2(_mirror ? -1 : 1, sy));
            DrawTexture(t, -pivot);
        }

        /// <summary>Двухзвенный IK: из плеча (бедра) к цели, сгиб в сторону bendSign. Возвращает углы звеньев.</summary>
        static (float a1, float a2, Vector2 joint) Ik(Vector2 root, Vector2 target, float l1, float l2, float bendSign)
        {
            var d = target - root;
            float dist = Mathf.Clamp(d.Length(), 0.5f, l1 + l2 - 0.05f);
            float baseA = Mathf.Atan2(d.Y, d.X);
            float cos = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist), -1, 1);
            float a1 = baseA + bendSign * Mathf.Acos(cos);
            var joint = root + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * l1;
            float a2 = Mathf.Atan2(target.Y - joint.Y, target.X - joint.X);
            return (a1, a2, joint);
        }

        static float Ease(float t) => t * t * (3 - 2 * t);

        public override void _Draw()
        {
            var s = S;
            // работаем в «правом» пространстве: смотрим вправо (влево — зеркало)
            float aim = s.Aim;
            _mirror = Mathf.Cos(aim) < 0;
            if (_mirror) aim = Mathf.Pi - aim;
            aim = Mathf.Wrap(aim, -Mathf.Pi, Mathf.Pi);
            bool back = Mathf.Sin(aim) < -0.05f;          // смотрим вверх — видим спину
            var fwd = new Vector2(Mathf.Cos(aim), Mathf.Sin(aim));

            float crouch = s.Crouch ? 1 : 0;
            bool moving = s.Speed > 0.4f && !s.Dead;
            float stride = moving ? (s.Sprint ? 5.5f : s.Crouch ? 2.5f : 4f) : 0;
            float lift = moving ? (s.Sprint ? 3.5f : 2f) : 0;
            float bob = moving ? -Mathf.Abs(Mathf.Sin(s.Phase)) * (s.Sprint ? 1.5f : 1f) : Mathf.Sin(s.Breath) * 0.4f;

            float lThigh = _set.Thigh.GetHeight() - 1, lShin = _set.Shin.GetHeight() - 1;
            float hipY = -(lThigh + lShin) + 0.6f + crouch * 7 + bob;   // в стойке ноги почти прямые
            var hipL = new Vector2(-3, hipY);
            var hipR = new Vector2(3, hipY);

            // стопы: шаг вперёд-назад вдоль взгляда + подъём; в приседе шире
            Vector2 Foot(float ph, float x)
            {
                float sw = Mathf.Sin(ph);
                var f = new Vector2(x + crouch * Mathf.Sign(x) * 2, 0) + fwd * stride * sw;
                f.Y -= Mathf.Max(0, Mathf.Cos(ph)) * lift;
                return f;
            }
            var footFar = Foot(s.Phase + Mathf.Pi, -3);
            var footNear = Foot(s.Phase, 4);
            float kneeBend = -1;   // колени сгибаются «вперёд» (к взгляду)

            // корпус: наклон вперёд на бегу
            float lean = s.Sprint ? 0.18f : moving ? 0.06f : 0;
            var waist = new Vector2(0, hipY - 2);
            float torsoH = (back ? _set.TorsoB : _set.TorsoF).GetHeight();
            var torsoTop = waist + new Vector2(Mathf.Sin(lean) * torsoH, -Mathf.Cos(lean) * torsoH);
            var shNear = torsoTop + new Vector2(4, 3);
            var shFar = torsoTop + new Vector2(-4, 3);
            var neck = torsoTop + new Vector2(1, 1);

            // ---------- оружие и кисти ----------
            string held = s.Held;
            bool hasItem = held != null;
            float len = (hasItem ? HeldLength(held) : 0) * 32f;
            float wAng = aim + s.Recoil * (_mirror ? -1 : 1);
            Vector2 grip;
            float down = 0;
            if (s.Swing >= 0 && hasItem && IsTool(held))
            {
                // удар: замах за плечо → резкий удар вниз-вперёд → возврат
                float t = s.Swing;
                if (held == "weapon_spear")
                {
                    float k = t < 0.3f ? -Ease(t / 0.3f) * 0.4f : t < 0.5f ? Mathf.Lerp(-0.4f, 1f, Ease((t - 0.3f) / 0.2f)) : 1f - Ease((t - 0.5f) / 0.5f);
                    grip = shNear + new Vector2(1, 6) + fwd * (2 + 9 * k);
                }
                else
                {
                    // свой замах у инструмента: кирка — высоко над головой, топор — из-за плеча, камень — одной рукой
                    float arc = held == "tool_pickaxe" ? 2.8f : held == "tool_hatchet" ? 2.2f : 1.7f;
                    float raiseH = held == "tool_pickaxe" ? 15f : held == "tool_hatchet" ? 12f : 9f;
                    // 0–0.35 замах (медленно, с «оттяжкой»), 0.35–0.45 резкий удар (попадание ≈0.45), 0.45–1 отдача и возврат
                    float raise = t < 0.35f ? Ease(t / 0.35f)
                                : t < 0.45f ? 1f - (t - 0.35f) / 0.1f * 1.3f
                                : -0.3f * (1f - Ease((t - 0.45f) / 0.55f));
                    wAng = aim - arc * raise;                          // поднят назад → опускается вперёд-вниз к цели
                    grip = shNear + new Vector2(-2 * raise, 6 - raiseH * raise) + fwd * (4 + 3 * (1 - raise));
                }
            }
            else
            {
                float aimUp = s.AimDown;
                grip = shNear + new Vector2(2, 7 - 6 * aimUp) + fwd * (3 + 2 * aimUp);   // у пояса → у щеки
                if (s.Sprint) { down = 0.9f; grip += new Vector2(-1, 3); }              // бег: ствол опущен
                if (s.Reload >= 0)
                {
                    float r = s.Reload;
                    float tilt = r < 0.2f ? r / 0.2f : r > 0.75f ? 1f - (r - 0.75f) / 0.25f : 1f;
                    down = Mathf.Max(down, Ease(tilt) * 0.8f);
                }
                wAng = aim + down + s.Recoil * (_mirror ? -1 : 1) - s.Kick * 0.12f;
                grip -= new Vector2(Mathf.Cos(wAng), Mathf.Sin(wAng)) * s.Kick * 3f;
            }
            var wDir = new Vector2(Mathf.Cos(wAng), Mathf.Sin(wAng));
            Vector2 support = TwoHanded(held ?? "") ? grip + wDir * len * 0.5f : shFar + new Vector2(1, 12) + fwd * 1.5f;
            if (s.Reload >= 0 && s.Reload > 0.3f && s.Reload < 0.7f)
                support = grip + wDir * len * 0.25f + new Vector2(0, 6);     // вторая кисть за магазином
            if (!hasItem) { grip = shNear + new Vector2(1, 12) + fwd * 1.5f; support = shFar + new Vector2(1, 12) - fwd * 1.5f; }

            float lUp = _set.UpperArm.GetWidth() - 1, lFore = _set.ForeArm.GetWidth() - 2;
            var armNear = Ik(shNear, grip, lUp, lFore, 1);
            var armFar = Ik(shFar, support, lUp, lFore, 1);

            void DrawWeapon()
            {
                if (!hasItem) return;
                var tex = Art.HeldSprite(held);
                if (tex == null) return;
                float sc = len / (tex.GetWidth() * 0.9f);
                var center = grip + wDir * len * 0.3f;
                var p = center + RigView.Feet;
                float rot = wAng;
                if (_mirror) { p.X = RigView.VW - p.X; rot = -rot; }
                // оружие не переворачиваем вверх ногами: зеркалим по Y, а не по X
                DrawSetTransform(p.Round(), _mirror ? Mathf.Pi - wAng : wAng, new Vector2(sc, _mirror ? -sc : sc));
                DrawTexture(tex, -tex.GetSize() / 2);
            }

            void DrawArm((float a1, float a2, Vector2 joint) arm, Vector2 sh)
            {
                Part(_set.UpperArm, sh, arm.a1, new Vector2(1, _set.UpperArm.GetHeight() / 2f));
                Part(_set.ForeArm, arm.joint, arm.a2, new Vector2(1, _set.ForeArm.GetHeight() / 2f));
            }

            void DrawLeg(Vector2 hip, Vector2 foot)
            {
                var leg = Ik(hip, foot, lThigh, lShin, kneeBend);
                Part(_set.Thigh, hip, leg.a1 - Mathf.Pi / 2, new Vector2(_set.Thigh.GetWidth() / 2f, 1));
                Part(_set.Shin, leg.joint, leg.a2 - Mathf.Pi / 2, new Vector2(_set.Shin.GetWidth() / 2f, 1));
            }

            void DrawBody()
            {
                DrawLeg(hipL, footFar);
                DrawLeg(hipR, footNear);
                Part(_set.Pelvis, waist + new Vector2(0, 1), lean * 0.5f, new Vector2(_set.Pelvis.GetWidth() / 2f, _set.Pelvis.GetHeight() * 0.7f));
                var torso = back ? _set.TorsoB : _set.TorsoF;
                Part(torso, waist, lean, new Vector2(torso.GetWidth() / 2f, torso.GetHeight()));
                var head = back ? _set.HeadB : _set.HeadF;
                float headTilt = s.AimDown * 0.15f + lean * 0.5f;   // в прицеле голова к прикладу
                Part(head, neck, headTilt, new Vector2(head.GetWidth() / 2f, head.GetHeight()));
            }

            if (back)
            {
                // спиной: оружие и руки дальше от камеры — за телом
                DrawArm(armFar, shFar);
                DrawWeapon();
                DrawArm(armNear, shNear);
                DrawBody();
            }
            else
            {
                DrawArm(armFar, shFar);
                DrawBody();
                DrawWeapon();
                DrawArm(armNear, shNear);
            }
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}
