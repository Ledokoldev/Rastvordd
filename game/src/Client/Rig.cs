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
        public float Flinch;         // −1..1: вздрагивание от попадания (знак — откуда удар: спереди → назад)
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

        /// <summary>Конец предмета в руках относительно ног персонажа (пиксели мира).</summary>
        public Vector2 TipLocal => _canvas.TipPx - Feet;

        bool _swim;
        /// <summary>В воде: видно только выше пояса, тело опущено, срез — по воде.</summary>
        public void SetSwim(bool swim)
        {
            if (swim == _swim) return;
            _swim = swim;
            _sprite.RegionEnabled = swim;
            _sprite.RegionRect = new Rect2(0, 0, VW, Feet.Y - 26);
        }

        public void Refresh(bool dead)
        {
            // смерть: лежит на боку
            _sprite.Rotation = dead ? Mathf.Pi / 2 * (Mathf.Cos(S.Aim) >= 0 ? 1 : -1) : 0;
            // мёртв: лежит на боку, тело по центру точки смерти (поворот идёт вокруг стоп)
            _sprite.Position = dead ? new Vector2(-24 * (Mathf.Cos(S.Aim) >= 0 ? 1 : -1), -4) : _swim ? new Vector2(0, 18) : Vector2.Zero;
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
        static bool IsTool(string id) => id is "tool_rock" or "tool_hatchet" or "tool_pickaxe" or "tool_hammer" or "weapon_spear" or "weapon_torch";

        /// <summary>Конец предмета в руках (пиксели буфера) — для пламени и света факела.</summary>
        public Vector2 TipPx;

        static float HeldLength(string id) => id switch
        {
            "tool_rock" => 0.3f, "tool_hatchet" => 0.78f, "tool_pickaxe" => 0.85f, "weapon_spear" => 1.5f,
            "weapon_bow" => 0.85f, "weapon_pipe" => 0.5f, "weapon_ak" => 0.92f, "weapon_torch" => 0.55f, "tool_hammer" => 0.45f, "build_plan" => 0.35f, _ => 0.6f,
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

            // удар инструментом: корпус откидывается назад на замахе, «падает» вперёд в удар, колени пружинят
            bool toolSwing = s.Swing >= 0 && s.Held != null && IsTool(s.Held) && s.Held != "weapon_spear";
            float swingLean = 0, swingDip = 0;
            if (toolSwing)
            {
                float t = s.Swing;
                swingLean = t < 0.35f ? -0.16f * Ease(t / 0.35f)
                          : t < 0.45f ? Mathf.Lerp(-0.16f, 0.3f, Ease((t - 0.35f) / 0.1f))
                          : 0.3f * (1f - Ease((t - 0.45f) / 0.55f));
                swingDip = t > 0.38f && t < 0.75f ? Mathf.Sin((t - 0.38f) / 0.37f * Mathf.Pi) * 2.5f : 0;
            }

            float lThigh = _set.Thigh.GetHeight() - 1, lShin = _set.Shin.GetHeight() - 1;
            float hipY = -(lThigh + lShin) + 0.6f + crouch * 12 + bob + swingDip;   // в стойке ноги почти прямые
            var hipL = new Vector2(-3, hipY);
            var hipR = new Vector2(3, hipY);

            // стопы: шаг вперёд-назад вдоль взгляда + подъём; в приседе шире
            Vector2 Foot(float ph, float x)
            {
                float sw = Mathf.Sin(ph);
                var f = new Vector2(x, 0) + new Vector2(fwd.X, fwd.Y * 0.5f) * stride * sw;
                f.Y -= Mathf.Max(0, Mathf.Cos(ph)) * lift;
                return f;
            }
            // присед — полуколено: ближняя нога шагнула вперёд (колено вверх), дальняя отставлена назад (колено к земле)
            var fwdG = new Vector2(fwd.X, fwd.Y * 0.5f);   // направление по земле с ракурсным сжатием глубины
            var footFar = Foot(s.Phase + Mathf.Pi, -3) + crouch * (-fwdG * 11);
            var footNear = Foot(s.Phase, 4) + crouch * (fwdG * 7);
            if (s.Dead) { footFar = new Vector2(-7, -1); footNear = new Vector2(6, 0); }   // мёртв: ноги врозь (спрайт потом кладётся на бок)
            float kneeBend = -1;   // колени сгибаются «вперёд» (к взгляду)

            // корпус: наклон вперёд на бегу
            float lean = (s.Sprint ? 0.18f : moving ? 0.06f : 0) + crouch * 0.12f - s.Flinch * 0.3f;
            lean += swingLean * Mathf.Clamp(Mathf.Abs(fwd.X) + 0.3f, 0, 1);   // в профиль наклон виден, анфас — меньше
            var waist = new Vector2(0, hipY - 2);
            float torsoH = (back ? _set.TorsoB : _set.TorsoF).GetHeight();
            var torsoTop = waist + new Vector2(Mathf.Sin(lean) * torsoH, -Mathf.Cos(lean) * torsoH);
            var shNear = torsoTop + new Vector2(4, 3);
            var shFar = torsoTop + new Vector2(-4, 3);
            var neck = torsoTop + new Vector2(1, 1);

            // ---------- оружие и кисти ----------
            string held = s.Held;
            if (s.Dead) held = null;   // оружие выпало
            bool hasItem = held != null;
            float len = (hasItem ? HeldLength(held) : 0) * 32f;
            float wAng = aim + s.Recoil * (_mirror ? -1 : 1);
            Vector2 grip;
            float down = 0, toolSlide = -1;
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
                    // плоскость удара вертикальна: «вперёд» по земле + «вверх»; проецируем в вид 3/4 (глубина ×0,5).
                    // В профиль это поворот в плоскости экрана, анфас/спиной — инструмент идёт вверх над головой и вниз к цели.
                    float th = arc * raise;                            // угол подъёма от «вперёд»
                    var sd = new Vector2(fwd.X * Mathf.Cos(th), fwd.Y * 0.5f * Mathf.Cos(th) - Mathf.Sin(th));
                    wAng = Mathf.Atan2(sd.Y, sd.X);
                    grip = shNear + new Vector2(-2 * raise, 6 - raiseH * raise) + fwd * (4 + 3 * (1 - raise));
                    toolSlide = Mathf.Clamp(raise, 0, 1);              // на замахе кисти врозь, в ударе сходятся у конца рукояти
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
                if (held == "weapon_torch")
                {
                    // факел подняли перед собой — огонь выше головы-плеча, на бегу опускается
                    grip = shNear + new Vector2(1, s.Sprint ? 6 : 3) + fwd * 4;
                    float up = s.Sprint ? 0.5f : 1.0f;
                    var td = new Vector2(fwd.X * Mathf.Cos(up), fwd.Y * 0.5f * Mathf.Cos(up) - Mathf.Sin(up));
                    wAng = Mathf.Atan2(td.Y, td.X) + Mathf.Sin(s.Breath * 1.3f) * 0.04f;
                }
            }
            var wDir = new Vector2(Mathf.Cos(wAng), Mathf.Sin(wAng));
            bool toolGrip = hasItem && IsTool(held) && held != "weapon_spear";
            float supportAt = toolGrip ? (toolSlide >= 0 ? Mathf.Lerp(0.1f, 0.38f, toolSlide) : 0.25f) : 0.5f;
            Vector2 support = TwoHanded(held ?? "") ? grip + wDir * len * supportAt : shFar + new Vector2(1, 12) + fwd * 1.5f;
            if (s.Reload >= 0 && s.Reload > 0.3f && s.Reload < 0.7f)
                support = grip + wDir * len * 0.25f + new Vector2(0, 6);     // вторая кисть за магазином
            if (!hasItem) { grip = shNear + new Vector2(1, 12) + fwd * 1.5f; support = shFar + new Vector2(1, 12) - fwd * 1.5f; }
            if (s.Dead) { grip = shNear + new Vector2(9, -7); support = shFar + new Vector2(-9, -5); }   // руки раскинуты

            float lUp = _set.UpperArm.GetWidth() - 1, lFore = _set.ForeArm.GetWidth() - 2;
            var armNear = Ik(shNear, grip, lUp, lFore, 1);
            var armFar = Ik(shFar, support, lUp, lFore, 1);

            void DrawWeapon()
            {
                if (!hasItem) return;
                var tex = Art.HeldSprite(held);
                if (tex == null) return;
                float sc = len / (tex.GetWidth() * 0.9f);
                var center = grip + wDir * len * (toolGrip ? 0.42f : 0.3f);   // инструмент держат за конец рукояти
                var p = center + RigView.Feet;
                float rot = wAng;
                if (_mirror) { p.X = RigView.VW - p.X; rot = -rot; }
                // оружие не переворачиваем вверх ногами: зеркалим по Y, а не по X
                DrawSetTransform(p.Round(), _mirror ? Mathf.Pi - wAng : wAng, new Vector2(sc, _mirror ? -sc : sc));
                DrawTexture(tex, -tex.GetSize() / 2);

                var tip = center + wDir * len * 0.47f + RigView.Feet;
                if (_mirror) tip.X = RigView.VW - tip.X;
                TipPx = tip;
                if (held == "weapon_torch") DrawFlame(tip.Round());
            }

            // пиксельное пламя: язычки разной высоты, мерцают; всегда тянутся вверх
            void DrawFlame(Vector2 at)
            {
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                int f = (int)(s.Breath * 9f);
                for (int i = -2; i <= 2; i++)
                {
                    uint hsh = (uint)(f * 73856093 ^ (i + 7) * 19349663);
                    int h = 2 + (int)(hsh % 4) - Mathf.Abs(i);
                    if (h <= 0) continue;
                    var x = at.X + i;
                    DrawRect(new Rect2(x, at.Y - h - 1, 1, h + 1), new Color(0.95f, 0.35f, 0.1f));
                    if (h > 1) DrawRect(new Rect2(x, at.Y - h + 1, 1, h - 1), new Color(1f, 0.72f, 0.2f));
                    if (Mathf.Abs(i) <= 1 && h > 2) DrawRect(new Rect2(x, at.Y - 1, 1, 1), new Color(1f, 0.95f, 0.65f));
                }
                if ((f & 3) == 0) DrawRect(new Rect2(at.X + ((f >> 2) % 3) - 1, at.Y - 7, 1, 1), new Color(1f, 0.6f, 0.2f, 0.8f));   // искра
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

            // звено ноги от a до b: текстура тянется/сжимается — так видно ракурсное сокращение (бедро к камере)
            void Seg(Texture2D t, Vector2 a, Vector2 b, float l)
            {
                var d = b - a;
                Part(t, a, Mathf.Atan2(d.Y, d.X) - Mathf.Pi / 2, new Vector2(t.GetWidth() / 2f, 1), Mathf.Max(0.25f, d.Length() / l));
            }

            void DrawBody()
            {
                if (crouch > 0 && Mathf.Abs(fwd.X) < 0.45f)
                {
                    // присед анфас/спиной: опорное колено на земле, другая нога выставлена вперёд — бедро сокращено ракурсом
                    float f = Mathf.Sign(fwd.Y);   // +1 — к камере, −1 — от камеры
                    var kneeK = new Vector2(-3, f > 0 ? -0.5f : 1f);
                    var footK = new Vector2(-3.5f, f > 0 ? -4f : 4f);
                    var kneeF = hipR + new Vector2(0.5f, f > 0 ? 6f : 4f);
                    var footF = new Vector2(4.5f, f > 0 ? 0f : -3f);
                    Seg(_set.Shin, kneeK, footK, lShin);
                    Seg(_set.Thigh, hipL, kneeK, lThigh);
                    Seg(_set.Shin, kneeF, footF, lShin);
                    Seg(_set.Thigh, hipR, kneeF, lThigh);
                }
                else
                {
                    DrawLeg(hipL, footFar);
                    DrawLeg(hipR, footNear);
                }
                Part(_set.Pelvis, waist + new Vector2(0, 1), lean * 0.5f, new Vector2(_set.Pelvis.GetWidth() / 2f, _set.Pelvis.GetHeight() * 0.7f));
                var torso = back ? _set.TorsoB : _set.TorsoF;
                Part(torso, waist, lean, new Vector2(torso.GetWidth() / 2f, torso.GetHeight()));
                var head = back ? _set.HeadB : _set.HeadF;
                float headTilt = s.AimDown * 0.15f + lean * 0.5f - s.Flinch * 0.25f;   // в прицеле голова к прикладу; от удара голова дёргается
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
