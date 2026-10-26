using Godot;

namespace Rastvordd.Client
{
    /// <summary>Набор частей тела (кат-аут) для персонажа. Нет всех файлов — null, тогда используются кадры.</summary>
    public sealed class RigSet
    {
        public Texture2D HeadF, HeadB, TorsoF, TorsoB, Pelvis, UpperArm, ForeArm, Thigh, Shin;
        /// <summary>Имя набора (rig_&lt;имя&gt;_*) — ключ ручной настройки RigTune (F6).</summary>
        public string Name;

        /// <summary>Часть по имени файла (head_f, torso_b, …) — для сборки одежды (WearLook).</summary>
        public Texture2D Part(string p) => p switch
        {
            "head_f" => HeadF, "head_b" => HeadB, "torso_f" => TorsoF, "torso_b" => TorsoB, "pelvis" => Pelvis,
            "upperarm" => UpperArm, "forearm" => ForeArm, "thigh" => Thigh, _ => Shin,
        };

        static readonly System.Collections.Generic.Dictionary<Texture2D, Texture2D> _shaped = new();

        /// <summary>
        /// Силуэт корпуса по-человечески: покатые плечи от шеи, грудь во всю ширину, к поясу чуть уже. Сгенерированный
        /// корпус был «трапецией» с торчащими углами-плечами (а растянутый вширь — ещё и коробкой). Цвета берутся из исходника.
        /// </summary>
        static Texture2D ShapeTorso(Texture2D t)
        {
            if (t == null) return null;
            if (_shaped.TryGetValue(t, out var done)) return done;
            var src = t.GetImage();
            if (src.IsCompressed()) src.Decompress();
            int w = src.GetWidth(), h = src.GetHeight();
            var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
            float cx = (w - 1) / 2f, half = w / 2f;
            for (int y = 0; y < h; y++)
            {
                float k = y switch { 0 => 0.38f, 1 => 0.62f, 2 => 0.82f, 3 => 0.94f, _ => Mathf.Lerp(1f, 0.8f, Mathf.Clamp((y - 5f) / (h - 6f), 0, 1)) };
                for (int x = 0; x < w; x++)
                {
                    if (Mathf.Abs(x - cx) > half * k) continue;
                    // цвет — из исходника; если там пусто (срезанный угол плеча стал телом) — ближайший непрозрачный в строке
                    var c = src.GetPixel(x, y);
                    for (int d = 1; c.A < 0.5f && d < w; d++)
                    {
                        int xl = x - d, xr = x + d;
                        if (xl >= 0 && src.GetPixel(xl, y).A >= 0.5f) c = src.GetPixel(xl, y);
                        else if (xr < w && src.GetPixel(xr, y).A >= 0.5f) c = src.GetPixel(xr, y);
                    }
                    if (c.A >= 0.5f) img.SetPixel(x, y, new Color(c, 1));
                }
            }
            var tex = ImageTexture.CreateFromImage(img);
            _shaped[t] = tex;
            return tex;
        }

        public static RigSet Load(string name)
        {
            Texture2D T(string p) => Art.Get($"rig/rig_{name}_{p}");
            var s = new RigSet
            {
                Name = name,
                HeadF = T("head_f"), HeadB = T("head_b"), TorsoF = ShapeTorso(T("torso_f")), TorsoB = ShapeTorso(T("torso_b")), Pelvis = T("pelvis"),
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
        public float Phase;          // фаза шага: цикл 2π = два шага, касание пяткой при Phase = kπ
        public float Gait = -1;      // 0..1: насколько ноги «в шаге» (плавно гаснет при остановке); −1 — по Speed
        public float Cadence;        // шагов/с (0 — по Speed)
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
        public const float BodyScale = 0.8f;

        readonly SubViewport _vp;
        readonly RigCanvas _canvas;
        readonly Sprite2D _sprite;
        public RigState S => _canvas.S;
        /// <summary>Сменить набор частей (одежда надета/снята — WearLook.Dressed).</summary>
        public void SetSet(RigSet set) { if (set != null) { _canvas.Set = set; _canvas.QueueRedraw(); } }

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
            // рост: части нарисованы 1:1 (≈1,8 м = 58 px), а мир по высоте сжат как вид 3/4 (Iso.HS = 0,75) — иначе человек
            // ростом почти со стену и огромный рядом с мебелью и техникой РТ
            _sprite = new Sprite2D { Texture = _vp.GetTexture(), Centered = false, Offset = -Feet, TextureFilter = TextureFilterEnum.Nearest, Scale = new Vector2(BodyScale, BodyScale) };
            AddChild(_sprite);
        }

        /// <summary>Конец предмета в руках относительно ног персонажа (пиксели мира).</summary>
        public Vector2 TipLocal => (_canvas.TipPx - Feet) * BodyScale;

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
            _sprite.Position = (dead ? new Vector2(-24 * (Mathf.Cos(S.Aim) >= 0 ? 1 : -1), -4) : _swim ? new Vector2(0, 18) : Vector2.Zero) * BodyScale;
            _sprite.Modulate = dead ? new Color(0.6f, 0.55f, 0.55f) : Colors.White;
            _canvas.QueueRedraw();
        }
    }

    public partial class RigCanvas : Node2D
    {
        public readonly RigState S = new();
        RigSet _set;
        public RigSet Set { get => _set; set => _set = value; }

        public RigCanvas(RigSet set) => _set = set;

        static bool TwoHanded(string id) => id is "weapon_ak" or "weapon_pipe" or "weapon_bow" or "weapon_spear" or "tool_pickaxe" or "tool_hatchet" || Rastvordd.Sim.Weaponry.TwoHanded(id);
        static bool IsTool(string id) => id is "tool_rock" or "tool_hatchet" or "tool_pickaxe" or "tool_hammer" or "weapon_spear" or "weapon_torch";

        /// <summary>Конец предмета в руках (пиксели буфера) — для пламени и света факела.</summary>
        public Vector2 TipPx;

        static float HeldLength(string id) => Rastvordd.Sim.Weaponry.HeldLength(id) is float wl && wl > 0 ? wl : id switch
        {
            "tool_rock" => 0.3f, "tool_hatchet" => 0.78f, "tool_pickaxe" => 0.85f, "weapon_spear" => 1.5f,
            "weapon_bow" => 0.85f, "weapon_pipe" => 0.5f, "weapon_ak" => 0.92f, "weapon_torch" => 0.55f, "tool_hammer" => 0.45f, "build_plan" => 0.35f, _ => 0.6f,
        };

        bool _mirror;

        /// <summary>Рисует часть: позиция в «правом» пространстве (смотрим вправо), поворот, точка крепления в текстуре.</summary>
        void Part(Texture2D t, Vector2 pos, float rot, Vector2 pivot, float sy = 1f, float sx = 1f, string key = null, bool len = true)
        {
            if (t == null) return;
            if (key != null)
            {
                // ручная настройка (F6): ширина, длина, сдвиг части — сдвиг вдоль осей части, повёрнутых с ней
                var tu = RigTune.Get(_set.Name, key);
                sx *= tu.Sx; if (len) sy *= tu.Sy;
                pos += new Vector2(tu.Ox, tu.Oy);   // сдвиг экранный: X — вправо, Y — вниз
            }
            var p = pos + RigView.Feet;
            if (_mirror) { p.X = RigView.VW - p.X; rot = -rot; }
            DrawSetTransform(p.Round(), rot, new Vector2(_mirror ? -sx : sx, sy));
            DrawTexture(t, -pivot);
        }

        /// <summary>Телосложение: корпус, таз и конечности шире нарисованных на ~45% (рост тот же) — крепкие мужики, не дрищи.</summary>
        const float Broad = 1.45f;
        /// <summary>Множитель толщины части: до целого числа пикселей, чтобы пиксели не «плясали».</summary>
        static float Thick(int px) => Mathf.Max(px + 1, Mathf.Round(px * Broad)) / px;
        /// <summary>Корпус шире нарисованного меньше, чем ноги и таз: плечи ≈ 0,9 высоты корпуса, как у человека.</summary>
        /// <summary>Ноги не толще нужного (иначе «жирный»), зато шире расставлены.</summary>
        static float LegThick(int px) => Mathf.Max(px + 1, Mathf.Round(px * 1.25f)) / px;
        static float TorsoThick(int px) => Mathf.Max(px + 1, Mathf.Round(px * 1.15f)) / px;
        /// <summary>Руки толще остального тела (иначе на крепком торсе они выглядят тонкими и кривыми).</summary>
        static float ArmThick(int px) => Mathf.Max(px + 2, Mathf.Round(px * 1.8f)) / px;
        /// <summary>Цвета руки из текстуры части (рукав уже окрашен одеждой — WearLook): заливка, тень, обводка, кисть.</summary>
        static readonly System.Collections.Generic.Dictionary<ulong, (Color fill, Color shade, Color line, Color tip)> _armPal = new();
        static (Color fill, Color shade, Color line, Color tip) ArmPal(Texture2D t)
        {
            ulong key = t.GetRid().Id;
            if (_armPal.TryGetValue(key, out var pal)) return pal;
            var img = t.GetImage();
            if (img.IsCompressed()) img.Decompress();
            var all = new System.Collections.Generic.Dictionary<Color, int>();
            var end = new System.Collections.Generic.Dictionary<Color, int>();
            int w = img.GetWidth(), h = img.GetHeight();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = img.GetPixel(x, y);
                    if (c.A < 0.5f) continue;
                    c.A = 1;
                    all[c] = (all.TryGetValue(c, out int na) ? na : 0) + 1;
                    if (x >= w - 2) end[c] = (end.TryGetValue(c, out int ne) ? ne : 0) + 1;
                }
            Color line = Colors.Black, fill = Colors.Gray, tip = Colors.Gray;
            float darkest = 9;
            foreach (var (c, _) in all) if (c.Luminance < darkest) { darkest = c.Luminance; line = c; }
            int best = -1;
            foreach (var (c, n) in all) if (c.Luminance > darkest + 0.06f && n > best) { best = n; fill = c; }
            best = -1;
            foreach (var (c, n) in end) if (c.Luminance > darkest + 0.06f && n > best) { best = n; tip = c; }
            if (best < 0) tip = fill;
            line = fill.Darkened(0.42f);   // мягкая обводка в тон рукава (чёрная по краю выглядела слишком резкой)
            pal = (fill, fill.Darkened(0.2f), line, tip);
            _armPal[key] = pal;
            return pal;
        }

        /// <summary>Голова чуть шире нарисованной — лицо и шлем читаются как человеческие.</summary>
        static float HeadWide(int px) => px < 7 ? Mathf.Max(px + 2, Mathf.Round(px * 1.2f)) / px : 1f;   // крупные головы — без расширения (иначе «сплющена»)

        /// <summary>Основание шеи на картинке головы: центр непрозрачного в нижних строках (в 3/4 шея не по центру картинки).</summary>
        static readonly System.Collections.Generic.Dictionary<Texture2D, float> _neckX = new();
        static float NeckX(Texture2D t)
        {
            if (_neckX.TryGetValue(t, out var v)) return v;
            var img = t.GetImage(); float sum = 0; int n = 0;
            for (int y = img.GetHeight() - 3; y < img.GetHeight(); y++)
                for (int x = 0; x < img.GetWidth(); x++) if (img.GetPixel(x, y).A > 0.5f) { sum += x + 0.5f; n++; }
            return _neckX[t] = n > 0 ? sum / n : t.GetWidth() / 2f;
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
            float gait = s.Dead ? 0 : s.Gait >= 0 ? s.Gait : s.Speed > 0.4f ? 1 : 0;
            bool moving = gait > 0.5f;
            var fwdG = new Vector2(fwd.X, fwd.Y * 0.5f);   // направление по земле с ракурсным сжатием глубины

            // ---------- походка ----------
            // Цикл 2π = два шага. У каждой ноги своя доля цикла u: [0, duty) — опора (стопа на земле, едет назад
            // равномерно, как земля под идущим), [duty, 1) — перенос (стопа поднимается и выносится вперёд).
            // u = 0 — удар пяткой: ровно в этот момент Views2D считает касание и звучит шаг.
            float lThigh = (_set.Thigh.GetHeight() - 1) * RigTune.Get(_set.Name, "thigh").Sy, lShin = (_set.Shin.GetHeight() - 1) * RigTune.Get(_set.Name, "shin").Sy, legL = lThigh + lShin;
            float cad = s.Cadence > 0 ? s.Cadence : Footsteps.Cadence(s.Speed, s.Crouch);
            float duty = s.Sprint ? 0.4f : 0.6f;     // бег — с фазой полёта, ходьба — с двойной опорой
            // полуразмах шага без проскальзывания: за время опоры стопа проходит 2S = скорость × время опоры.
            // Скорость в игре выше человеческой, поэтому шаг ограничен длиной ноги — остаток «доезжает» скольжением.
            float noSlide = cad > 0 ? s.Speed * (Iso.PPM / RigView.BodyScale) * duty / cad : 0;
            float strideMax = legL * (s.Crouch ? 0.26f : s.Sprint ? 0.44f : 0.36f);
            float stride = Mathf.Min(noSlide, strideMax) * gait;
            float lift = (s.Crouch ? 1.5f : s.Sprint ? 4f : 2.2f) * gait;
            // таз: ниже всего при двойной опоре (обе ноги врозь), выше всего над опорной ногой; ноги в стойке прямые
            float sEff = stride * fwdG.Length();
            float bobAmp = legL - Mathf.Sqrt(Mathf.Max(0, legL * legL - sEff * sEff));
            float bob = gait * ((s.Crouch ? 0 : s.Sprint ? 1.2f : 0.4f) + bobAmp * (0.5f + 0.5f * Mathf.Cos(2 * s.Phase)));
            float breath = moving ? 0 : Mathf.Sin(s.Breath) * 0.4f;   // дыхание — только корпусом, колени не гнутся

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

            // стоя ноги прямые (таз на полную длину ноги); присед стоя — полуколено, вприсядку на ходу таз чуть выше
            float hipY = -legL + 0.05f + crouch * (12 - 3 * gait) + bob + swingDip;
            float hipsX = RigTune.Get(_set.Name, "hips").Ox;
            var hipL = new Vector2(-4 - hipsX, hipY);   // ноги шире расставлены: просвет между ними, фигура не «бочкой»
            var hipR = new Vector2(5 + hipsX, hipY);

            // стопа ноги с долей цикла u: вдоль взгляда (по земле) + подъём при переносе
            Vector2 Foot(float u, float x)
            {
                u -= Mathf.Floor(u);
                float along, up = 0;
                if (u < duty) along = stride * (1 - 2 * u / duty);
                else
                {
                    float t = (u - duty) / (1 - duty);
                    along = -stride + 2 * stride * Ease(t);
                    up = Mathf.Sin(t * Mathf.Pi) * lift;
                }
                return new Vector2(x, -up) + fwdG * along;
            }
            float cyc = s.Phase / Mathf.Tau;
            // присед — полуколено: ближняя нога шагнула вперёд (колено вверх), дальняя отставлена назад (колено к земле);
            // на ходу вприсядку — обычный шаг на согнутых ногах
            float kneel = crouch * (1 - gait);
            var footFar = Foot(cyc + 0.5f, hipL.X) + kneel * (-fwdG * 11);
            var footNear = Foot(cyc, hipR.X) + kneel * (fwdG * 7);
            if (s.Dead) { footFar = new Vector2(-7, -1); footNear = new Vector2(6, 0); }   // мёртв: ноги врозь (спрайт потом кладётся на бок)
            float kneeBend = -1;   // колени сгибаются «вперёд» (к взгляду)

            // корпус: наклон вперёд на бегу
            float lean = (s.Sprint ? 0.18f : 0.06f) * gait + crouch * 0.12f - s.Flinch * 0.3f;
            lean += swingLean * Mathf.Clamp(Mathf.Abs(fwd.X) + 0.3f, 0, 1);   // в профиль наклон виден, анфас — меньше
            float upper = RigTune.Get(_set.Name, "upper").Oy;   // F6 «Верх тела ↕»: корпус, голова и руки выше/ниже относительно ног
            var waist = new Vector2(0, hipY - 2 + breath + upper);
            float torsoH = (back ? _set.TorsoB : _set.TorsoF).GetHeight() * RigTune.Get(_set.Name, "torso").Sy;
            var torsoTop = waist + new Vector2(Mathf.Sin(lean) * torsoH, -Mathf.Cos(lean) * torsoH);
            var shT = RigTune.Get(_set.Name, "shoulders");
            var shNear = torsoTop + new Vector2(6 + shT.Ox, 3 + shT.Oy);   // плечи шире (телосложение крепче)
            var shFar = torsoTop + new Vector2(-6 - shT.Ox, 3 + shT.Oy);
            var neck = torsoTop + new Vector2(0, 1);   // шея над центром плеч (раньше +1 вперёд — голова «выезжала»)

            // ---------- оружие и кисти ----------
            string held = s.Held;
            if (s.Dead) held = null;   // оружие выпало
            bool hasItem = held != null;
            var wT = RigTune.Get(_set.Name, "weapon");   // F6 «Оружие»: размер (Sx), цевьё ближе/дальше (Sy), сдвиг X/Y
            float len = (hasItem ? HeldLength(held) : 0) * 32f * wT.Sx;
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
                // хват перед грудью: анфас/спиной — ближе к середине корпуса, в профиль — у плеча; у пояса → у щеки
                float face = Mathf.Abs(fwd.Y);
                grip = shNear + new Vector2(2 - 8 * face, 7 - 6 * aimUp - 2 * face) + new Vector2(fwd.X, fwd.Y * 0.5f) * (3 + 2 * aimUp);
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
            // ствол лежит горизонтально: в виде 3/4 глубина сжата вдвое — к камере/от камеры он смотрит не прямо вниз
            float proj = 1f;
            if (hasItem && !(s.Swing >= 0 && IsTool(held)) && held != "weapon_torch")
            {
                // к камере ствол чуть наискось через корпус (как «на грудь»), а не отвесно вдоль ноги
                float cant = Mathf.Max(0, fwd.Y) * Mathf.Max(0, Mathf.Abs(fwd.Y) - Mathf.Abs(fwd.X)) * 0.6f;
                var g = new Vector2(Mathf.Cos(wAng + cant), Mathf.Sin(wAng + cant) * 0.5f);
                wAng = Mathf.Atan2(g.Y, g.X);   // картинка ствола одна на все ракурсы: только поворот, без сжатия
            }
            float lenP = len * proj;
            var wDir = new Vector2(Mathf.Cos(wAng), Mathf.Sin(wAng));
            bool toolGrip = hasItem && IsTool(held) && held != "weapon_spear";
            float supportAt = toolGrip ? (toolSlide >= 0 ? Mathf.Lerp(0.1f, 0.38f, toolSlide) : 0.25f) : 0.5f;
            // две точки хвата: рукоятка (grip) и цевьё перед магазином (support, 58% длины от рукоятки)
            if (hasItem && !toolGrip) grip += new Vector2(wT.Ox, wT.Oy);
            Vector2 support = TwoHanded(held ?? "") ? grip + wDir * lenP * (toolGrip ? supportAt : 0.58f * wT.Sy) : shFar + new Vector2(1, 12) + fwd * 1.5f;
            if (s.Reload >= 0 && s.Reload > 0.3f && s.Reload < 0.7f)
                support = grip + wDir * lenP * 0.25f + new Vector2(0, 6);     // вторая кисть за магазином
            if (!hasItem) { grip = shNear + new Vector2(1, 12) + fwd * 1.5f; support = shFar + new Vector2(1, 12) - fwd * 1.5f; }
            if (s.Dead) { grip = shNear + new Vector2(9, -7); support = shFar + new Vector2(-9, -5); }   // руки раскинуты

            var upT = RigTune.Get(_set.Name, "upperarm"); var foT = RigTune.Get(_set.Name, "forearm");
            float lUp = (_set.UpperArm.GetWidth() - 1) * upT.Sy, lFore = (_set.ForeArm.GetWidth() - 2) * foT.Sy;
            float lUpBase = lUp, lForeBase = lFore;
            // вниз/влево/вправо видимая (ближняя к камере) рука вытянута к цевью, дальняя держит рукоятку — хват читается;
            // спиной — как было (ближняя у рукоятки). Инструменты и удар — без смены.
            bool swapGrip = hasItem && !back && TwoHanded(held) && !toolGrip && s.Swing < 0 && !(s.Reload > 0.3f && s.Reload < 0.7f);
            // вытянутая к цевью рука: почти прямая (локоть ~165°, не 180°), локоть ниже линии — кисть берёт цевьё снизу.
            // Длины руки подгоняются под расстояние до цевья (±25%), иначе на коротком стволе рука сгибалась бы «гармошкой».
            float nearK = 1f;
            (float a1, float a2, Vector2 joint) armNear;
            if (swapGrip)
            {
                // рука своей длины (как другая): не она тянется к цевью, а оружие придвигается к корпусу,
                // пока цевьё не окажется на расстоянии почти прямой руки (локоть ~165°)
                float want = (lUp + lFore) * 0.985f;
                for (int it = 0; it < 3; it++)
                {
                    float excess = (support + new Vector2(0, 1.5f) - shNear).Length() - want;
                    if (excess <= 0) break;
                    var pull = -wDir * excess;
                    grip += pull; support += pull;
                }
                var tgt = support + new Vector2(0, 1.5f);   // ладонь под цевьём
                var e1 = Ik(shNear, tgt, lUp, lFore, 1);
                var e2 = Ik(shNear, tgt, lUp, lFore, -1);
                armNear = e1.joint.Y >= e2.joint.Y ? e1 : e2;   // локоть вниз
            }
            else armNear = Ik(shNear, grip, lUp, lFore, 1);
            var armFar = Ik(shFar, swapGrip ? grip : support, lUp, lFore, 1);
            // «магнит»: оружие лежит в кистях. Кисть не достала до точки хвата (в прицеле ствол у щеки выше плеч) —
            // двигаем оружие к кисти, а не оставляем висеть в воздухе; две итерации — для обеих рук.
            if (hasItem && TwoHanded(held) && !toolGrip && s.Swing < 0)
            {
                Vector2 Hand((float a1, float a2, Vector2 joint) arm) => arm.joint + new Vector2(Mathf.Cos(arm.a2), Mathf.Sin(arm.a2)) * lFore;
                for (int it = 0; it < 2; it++)
                {
                    var nearT = swapGrip ? support + new Vector2(0, 1.5f) : grip;
                    var miss = Hand(armNear) - nearT;
                    grip += miss; support += miss;
                    var farT = swapGrip ? grip : support;
                    var farMiss = Hand(armFar) - farT;
                    if (farMiss.LengthSquared() > 0.25f) { grip += farMiss * 0.5f; support += farMiss * 0.5f; }
                    armNear = swapGrip ? PickDown(Ik(shNear, support + new Vector2(0, 1.5f), lUp, lFore, 1), Ik(shNear, support + new Vector2(0, 1.5f), lUp, lFore, -1)) : Ik(shNear, grip, lUp, lFore, 1);
                    armFar = Ik(shFar, swapGrip ? grip : support, lUp, lFore, 1);
                }
            }
            static (float a1, float a2, Vector2 joint) PickDown((float a1, float a2, Vector2 joint) x, (float a1, float a2, Vector2 joint) y) => x.joint.Y >= y.joint.Y ? x : y;

            void DrawWeapon()
            {
                if (!hasItem) return;
                var tex = Art.HeldSprite(held);
                if (tex == null) return;
                float sc = len / (tex.GetWidth() * 0.9f);
                var center = grip + wDir * lenP * (toolGrip ? 0.42f : 0.3f);   // инструмент держат за конец рукояти
                var p = center + RigView.Feet;
                float rot = wAng;
                if (_mirror) { p.X = RigView.VW - p.X; rot = -rot; }
                // оружие не переворачиваем вверх ногами: зеркалим по Y, а не по X
                DrawSetTransform(p.Round(), _mirror ? Mathf.Pi - wAng : wAng, new Vector2(sc, _mirror ? -sc : sc));
                DrawTexture(tex, -tex.GetSize() / 2);

                var tip = center + wDir * lenP * 0.47f + RigView.Feet;
                if (_mirror) tip.X = RigView.VW - tip.X;
                TipPx = tip;
                if (held == "weapon_torch") DrawFlame(tip.Round());
            }

            // пиксельное пламя: язычки разной высоты, мерцают; всегда тянутся вверх
            void DrawFlame(Vector2 at)
            {
                // лист fx/fire_torch (12 px, 8 кадров), низ пламени — на конце факела
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                Vfx.DrawFrame(this, "fire_torch", at + new Vector2(0, 2), s.Breath, 9f);
            }

            // рука — нарисованные части (плечо, предплечье с кистью), повёрнутые по суставам IK; в локте — «шарнир» цвета рукава,
            // чтобы на сгибе не было щели. Раньше руки были капсулами одного цвета («две палки»), текстура давала только цвет.
            void DrawArm((float a1, float a2, Vector2 joint) arm, Vector2 sh, float k = 1f)
            {
                float lUp = lUpBase * k, lFore = lForeBase * k;
                var up = ArmPal(_set.UpperArm);
                int hu = _set.UpperArm.GetHeight(), hf = _set.ForeArm.GetHeight();
                float su = hu < 6 ? ArmThick(hu) : 1f, sf = hf < 5 ? ArmThick(hf) : 1f;   // старые крошечные части — толще
                Part(_set.UpperArm, sh + new Vector2(upT.Ox, upT.Oy), arm.a1, new Vector2(0.5f, hu / 2f), su * upT.Sx, (lUp + 1) / _set.UpperArm.GetWidth());
                var j = arm.joint + RigView.Feet; if (_mirror) j.X = RigView.VW - j.X;
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                DrawCircle(j.Round(), Mathf.Max(1.5f, hu * su * 0.42f), up.fill);
                Part(_set.ForeArm, arm.joint + new Vector2(foT.Ox, foT.Oy), arm.a2, new Vector2(1f, hf / 2f), sf * foT.Sx, (lFore + 2) / _set.ForeArm.GetWidth());
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("", "CS8321")] void Capsule(Vector2 a, Vector2 b, float w, Color c)
            {
                DrawLine(a, b, c, w);
                DrawCircle(a, w * 0.5f, c);
                DrawCircle(b, w * 0.5f, c);
            }

            void DrawLeg(Vector2 hip, Vector2 foot)
            {
                var leg = Ik(hip, foot, lThigh, lShin, kneeBend);
                Part(_set.Thigh, hip, leg.a1 - Mathf.Pi / 2, new Vector2(_set.Thigh.GetWidth() / 2f, 1), 1, LegThick(_set.Thigh.GetWidth()), "thigh");
                Part(_set.Shin, leg.joint, leg.a2 - Mathf.Pi / 2, new Vector2(_set.Shin.GetWidth() / 2f, 1), 1, LegThick(_set.Shin.GetWidth()), "shin");
            }

            // звено ноги от a до b: текстура тянется/сжимается — так видно ракурсное сокращение (бедро к камере)
            void Seg(Texture2D t, Vector2 a, Vector2 b, float l)
            {
                var d = b - a;
                Part(t, a, Mathf.Atan2(d.Y, d.X) - Mathf.Pi / 2, new Vector2(t.GetWidth() / 2f, 1), Mathf.Max(0.25f, d.Length() / l) * (t == _set.Thigh ? RigTune.Get(_set.Name, "thigh").Sy : RigTune.Get(_set.Name, "shin").Sy), LegThick(t.GetWidth()), t == _set.Thigh ? "thigh" : "shin", false);
            }

            void DrawBody()
            {
                if (crouch > 0 && gait < 0.5f && Mathf.Abs(fwd.X) < 0.45f)
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
                Part(_set.Pelvis, new Vector2(0.5f, hipY - 1 + upper), lean * 0.5f, new Vector2(_set.Pelvis.GetWidth() / 2f, _set.Pelvis.GetHeight() * 0.7f), 1, Thick(_set.Pelvis.GetWidth()), "pelvis");
                var torso = back ? _set.TorsoB : _set.TorsoF;
                Part(torso, waist, lean, new Vector2(torso.GetWidth() / 2f, torso.GetHeight()), 1, TorsoThick(torso.GetWidth()), "torso");
                var head = back ? _set.HeadB : _set.HeadF;
                float headTilt = s.AimDown * 0.06f - s.Flinch * 0.25f;   // голова держится прямо (наклон корпуса не добавляем); в прицеле — чуть к прикладу
                Part(head, neck, headTilt, new Vector2(NeckX(head), head.GetHeight()), 1, HeadWide(head.GetWidth()), "head");
            }

            if (back)
            {
                // спиной: оружие и руки дальше от камеры — за телом
                DrawArm(armFar, shFar);
                DrawWeapon();
                DrawArm(armNear, shNear, nearK);
                DrawBody();
            }
            else if (fwd.Y > 0.35f && hasItem && TwoHanded(held))
            {
                // к камере: оружие перед грудью; рука на цевье подхватывает снизу — под оружием, кисть на рукоятке — поверх
                DrawBody();
                if (swapGrip) { DrawArm(armNear, shNear, nearK); DrawWeapon(); DrawArm(armFar, shFar); }
                else { DrawWeapon(); DrawArm(armFar, shFar); DrawArm(armNear, shNear, nearK); }
            }
            else
            {
                DrawArm(armFar, shFar);
                DrawBody();
                if (swapGrip) { DrawArm(armNear, shNear, nearK); DrawWeapon(); }   // ладонь под цевьём — оружие поверх руки
                else { DrawWeapon(); DrawArm(armNear, shNear, nearK); }
            }
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}
