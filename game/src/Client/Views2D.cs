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

    /// <summary>
    /// Тень от силуэта спрайта: та же картинка чёрным, сплющенная и скошенная вправо-вверх (свет сверху-слева).
    /// Низ тени совпадает с основанием спрайта — предмет стоит на земле, а не парит над круглым пятном.
    /// </summary>
    public static class Shade
    {
        // солнце (D-тени): наклон и длина тени и её сила зависят от времени суток — Main.UpdateSun; узлы в группе «shadow» перерисовываются
        public static float Flat = 0.42f, Skew = 0.46f, Strength = 1f;
        public const string Group = "shadow";
        static bool _lit;

        /// <summary>Солнце по времени суток t (0..1): утром тени длинные влево, в полдень короткие, вечером длинные вправо; ночью почти нет.</summary>
        public static bool SetSun(float t)
        {
            float s = Mathf.Clamp((t - 0.25f) / 0.55f, 0f, 1f);            // 0 — восход, 1 — закат
            float skew = Mathf.Lerp(-0.95f, 0.95f, s);
            float flat = 0.22f + 0.55f * Mathf.Abs(2f * s - 1f);          // в полдень короче
            float day = 1f - World.NightFactorAt(t);
            float str = 0.18f + 0.82f * day;
            bool changed = Mathf.Abs(skew - Skew) > 0.02f || Mathf.Abs(flat - Flat) > 0.02f || Mathf.Abs(str - Strength) > 0.03f;
            if (changed) { Skew = skew; Flat = flat; Strength = str; }
            if (changed || !_lit)
            {
                // светотень по нормалям — с той же стороны, откуда падает тень: утром свет справа (восток), вечером слева;
                // чуть сверху (верхние грани в 3/4 светлее), чем ниже солнце, тем сильнее боковой свет
                _lit = true;
                VisionFx.SetSun(new Vector3(-Skew * 0.9f, -0.12f, 0.45f + 0.25f * (1f - Mathf.Abs(Skew))), 1.15f * day, Mathf.Pow(Mathf.Abs(2f * s - 1f), 1.5f) * day);   // низкое солнце — тёплый свет
            }
            return changed;
        }

        const int Layers = 3;

        /// <summary>
        /// Тень спрайта «как из трассировки»: несколько копий силуэта разной длины и чуть разного наклона (размер солнечного диска).
        /// У основания копии совпадают — тень тёмная и резкая; к концу остаются только длинные — светлее и мягче (полутень).
        /// Плюс контактное затенение (AO) — тёмное пятно ровно под основанием, от солнца не зависит.
        /// </summary>
        public static void Sprite(CanvasItem ci, Texture2D tex, float scale = 1f, float alpha = 0.32f, float sink = 2f, Vector2 at = default)
        {
            ContactAo(ci, tex, scale, alpha / 0.32f, sink, at);
            float a = Mathf.Min(0.3f, alpha * 0.62f * Strength);                   // один слой: конец тени; у основания все три ≈ ×1.5 темнее
            var col = new Color(0.04f, 0.06f, 0.14f, a);   // тень холодная (небо подсвечивает её синим)
            var pos = new Vector2(-tex.GetWidth() / 2f, -tex.GetHeight() + sink);
            for (int i = 0; i < Layers; i++)
            {
                float len = 1f - 0.08f * i;                                        // 1, .92, .84 длины
                float spread = (i == 1 ? 0.025f : i == 2 ? -0.025f : 0f);          // веер полутени
                ci.DrawSetTransformMatrix(new Transform2D(new Vector2(scale, 0), new Vector2((-Skew * len + spread) * scale, Flat * len * scale), at));
                ci.DrawTexture(tex, pos, col);
            }
            ci.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        // ширина опоры спрайта (непрозрачные пиксели в нижних строках): центр и полуширина от центра текстуры, px
        static readonly System.Collections.Generic.Dictionary<Texture2D, Vector2> Feet = new();

        static Vector2 Foot(Texture2D tex)
        {
            if (Feet.TryGetValue(tex, out var f)) return f;
            var img = (tex is CanvasTexture ct ? ct.DiffuseTexture : tex)?.GetImage();
            f = new Vector2(0, tex.GetWidth() * 0.3f);
            if (img != null)
            {
                if (img.IsCompressed()) img.Decompress();
                int w = img.GetWidth(), h = img.GetHeight(), bottom = -1;
                for (int y = h - 1; y >= 0 && bottom < 0; y--)
                    for (int x = 0; x < w; x += 2) if (img.GetPixel(x, y).A > 0.5f) { bottom = y; break; }
                if (bottom >= 0)
                {
                    int band = Mathf.Max(3, h / 14), x0 = w, x1 = -1;
                    for (int y = bottom; y > bottom - band && y >= 0; y--)
                        for (int x = 0; x < w; x++) if (img.GetPixel(x, y).A > 0.5f) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); }
                    if (x1 >= x0) f = new Vector2((x0 + x1 + 1) / 2f - w / 2f, (x1 - x0 + 1) / 2f);
                }
            }
            Feet[tex] = f;
            return f;
        }

        static void ContactAo(CanvasItem ci, Texture2D tex, float scale, float k, float sink, Vector2 at)
        {
            var f = Foot(tex);
            float r = (f.Y * 1.1f + 2f) * scale;
            ci.DrawSetTransformMatrix(new Transform2D(new Vector2(1, 0), new Vector2(0, 0.32f), at + new Vector2(f.X * scale, sink - 1f)));
            ci.DrawCircle(Vector2.Zero, r, new Color(0, 0, 0, 0.10f * k));
            ci.DrawCircle(Vector2.Zero, r * 0.78f, new Color(0, 0, 0, 0.12f * k));
            ci.DrawCircle(Vector2.Zero, r * 0.52f, new Color(0, 0, 0, 0.14f * k));
        }
    }

    // ---------- персонаж-заглушка: 4 диагональные позы (левые — зеркало правых) ----------

    public partial class ActorView2D : Node2D
    {
        const float SwingTime = 0.45f;   // замах → удар → возврат (риг)

        readonly Body2D _body;
        readonly Held2D _held;
        bool _back = false, _dead;
        float _swing;

        public static bool HeroSprites;

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
            bool hero = isSelf && HeroSprites;   // --hero: пробный нарисованный персонаж (кадры chars/char_hero_*), без рига
            if (hero) sheet = "char_hero";
            _body.Front = Art.CharFrames(sheet, 'f');
            _body.BackFrames = Art.CharFrames(sheet, 'b');
            _body.WalkFront = Art.WalkFrames(sheet, 'f');
            _body.WalkBack = Art.WalkFrames(sheet, 'b');
            _body.TextureFilter = TextureFilterEnum.Nearest;
            _held = new Held2D { Position = new Vector2(1, -Iso.H(1.05f)) };
            AddChild(_body);
            AddChild(_held);
            // управляемый персонаж из частей (если части сгенерированы): руки по IK к оружию, ноги шагают, позы
            _isNpc = isNpc;
            var rigSet = hero ? null : RigSet.Load(isNpc ? "guard" : "naked");
            if (rigSet != null)
            {
                _rig = new RigView(rigSet);
                AddChild(_rig);
                _body.Visible = false;
                _held.Visible = false;
            }
            _torch = Art.FireLight(7f, 0f);
            _torch.Visible = false;
            AddChild(_torch);
        }

        readonly PointLight2D _torch;
        float _flicker;
        readonly bool _isNpc;
        string _wornKey = "";

        /// <summary>Одежда (D47): риг собирается слоями на голом наборе (охрана без одежды — свой набор guard).</summary>
        public void SetWorn(string[] worn)
        {
            if (_rig == null) return;
            string key = worn == null ? "" : string.Join(",", worn);
            if (key == _wornKey) return;
            _wornKey = key;
            _rig.SetSet(worn == null ? RigSet.Load(_isNpc ? "guard" : "naked") : WearLook.Dressed("naked", worn));
        }

        public void StartSwing() => _swing = SwingTime;

        /// <summary>Сколько раз стопа коснулась земли за последний SetState (по анимации шага) — столько звуков шага.</summary>
        public int StepContacts { get; private set; }
        float _gait, _gaitSpeed;

        float _flinch;
        /// <summary>Попадание: корпус и голова дёргаются от удара, тело на миг краснеет. fromFront — бьют спереди.</summary>
        public void Flinch(bool fromFront) => _flinch = fromFront ? 1f : -1f;

        /// <summary>Плывёт: тело по пояс в воде, круги по воде.</summary>
        public bool Swimming;
        float _ripple, _strokeT;
        bool _wasSwimming;

        string _label;
        /// <summary>Подпись над головой (боты: имя и что делает).</summary>
        public void SetLabel(string label)
        {
            if (label == _label) return;
            _label = label;
            QueueRedraw();
        }

        float _hitStop;
        /// <summary>Попадание: анимация удара замирает на мгновение — удар ощущается тяжёлым.</summary>
        public void HitStop(float seconds) => _hitStop = seconds;

        // ---------- анимации оружия в стиле Gungeon: тело — кадры, оружие с кистями анимируется кодом ----------
        float _kick, _reloadT, _reloadDur;
        bool _magDropped;
        float _facing;
        /// <summary>Выпал магазин при перезарядке: позиция (пиксели мира) и направление ствола.</summary>
        public event System.Action<Vector2, float> MagDropped;

        /// <summary>Выстрел: ствол дёргается назад и подкидывается.</summary>
        public void Kick() => _kick = 1f;

        /// <summary>Увод ствола от отдачи (радианы) — оружие в руках поворачивается туда, куда реально полетят пули.</summary>
        public float RecoilRad;
        /// <summary>0..1: прицеливание (ПКМ) — у рига оружие поднимается к голове.</summary>
        public float AimDown;
        readonly RigView _rig;

        /// <summary>Начало перезарядки: наклон, магазин выпадает, кисть за новым, возврат.</summary>
        public void StartReload(float duration)
        {
            _reloadT = 0;
            _reloadDur = Mathf.Max(0.3f, duration);
            _magDropped = false;
        }

        public override void _Draw()
        {
            // «смаз» удара: дуга перед персонажем по ходу замаха
            if (_swing > 0 && !_dead)
            {
                float t = 1f - _swing / SwingTime;
                if (t > 0.3f)
                {
                    float k = (t - 0.3f) / 0.7f;
                    var c = new Vector2(0, -Iso.H(1.05f));
                    float a0 = _facing - 1.1f, a1 = _facing - 1.1f + 1.9f * k;
                    DrawArc(c, 20, a0, a1, 12, new Color(1, 1, 1, 0.35f * (1 - k)), 4f);
                    DrawArc(c, 23, a0 + 0.3f, a1, 12, new Color(1, 1, 1, 0.18f * (1 - k)), 2f);
                }
            }
            if (!string.IsNullOrEmpty(_label) && !_dead)
            {
                var font = ThemeDB.FallbackFont;
                var size = font.GetStringSize(_label, HorizontalAlignment.Left, -1, 9);
                var at = new Vector2(-size.X / 2, -Iso.H(2.75f));
                DrawRect(new Rect2(at + new Vector2(-2, -9), new Vector2(size.X + 4, 12)), new Color(0, 0, 0, 0.45f));
                DrawString(font, at, _label, HorizontalAlignment.Left, -1, 9, new Color(0.95f, 0.9f, 0.75f));
            }
            if (Swimming && !_dead)
            {
                // круги по воде у пояса
                DrawSetTransform(new Vector2(0, -8), 0, new Vector2(1, 0.4f));
                for (int i = 0; i < 2; i++)
                {
                    float k = (_ripple * 0.8f + i * 0.5f) % 1f;
                    DrawArc(Vector2.Zero, 9 + k * 10, 0, Mathf.Tau, 24, new Color(0.85f, 0.92f, 1f, 0.5f * (1 - k)), 1.5f);
                }
                DrawCircle(Vector2.Zero, 9, new Color(0.75f, 0.85f, 0.95f, 0.25f));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                return;
            }
            // тень
            DrawSetTransform(Vector2.Zero, 0, new Vector2(1, 0.45f));
            DrawCircle(Vector2.Zero, _dead ? 16 : 10, new Color(0, 0, 0, 0.28f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        public void SetState(Vector2 pos, float facing, bool dead, bool reloading, float speed, float dt, string heldId, bool crouch = false)
        {
            Position = pos;
            bool right = Mathf.Cos(facing) >= 0;
            bool back = Mathf.Sin(facing) < 0;

            if (dead != _dead)
            {
                _dead = dead;
                _body.Dead = dead;
                _held.Visible = !dead && _rig == null;   // у скелетной модели оружие рисует сама модель
                _body.Rotation = dead ? (right ? Mathf.Pi / 2 : -Mathf.Pi / 2) : 0;
                _body.Position = dead ? new Vector2(0, -4) : Vector2.Zero;
                QueueRedraw();
            }
            if (dead) { _torch.Visible = false; _body.QueueRedraw(); if (_rig != null) { _rig.S.Aim = facing; _rig.S.Dead = true; _rig.Refresh(true); } return; }

            if (_held.ItemId != heldId) { _held.ItemId = heldId; _held.QueueRedraw(); }

            // присед: тело ниже (пока сжатием спрайта; позже — свои кадры)
            _body.Scale = new Vector2(right ? 1 : -1, crouch ? 0.8f : 1f);
            if (back != _back)
            {
                _back = back;
                MoveChild(_held, back ? 0 : 1);   // смотрит вверх — предмет за спиной, вниз — перед телом
            }
            _body.Back = back;
            // шаг: фаза идёт по человеческому темпу (Footsteps.Cadence), один цикл 2π = два шага;
            // касание пяткой — при переходе фазы через kπ (ближняя нога — чётные, дальняя — нечётные).
            // Остановился — фаза замирает (лишних шагов нет), ноги мягко сходятся (Gait → 0).
            bool moving = speed > 0.3f;
            _body.Moving = moving;
            _gaitSpeed = Mathf.Lerp(_gaitSpeed, speed, Mathf.Min(1f, dt * 10f));
            StepContacts = 0;
            if (moving)
            {
                float before = _body.Phase;
                _body.Phase += dt * Mathf.Pi * Footsteps.Cadence(_gaitSpeed, crouch);
                StepContacts = Mathf.FloorToInt(_body.Phase / Mathf.Pi) - Mathf.FloorToInt(before / Mathf.Pi);
                if (_body.Phase > Mathf.Tau * 64) _body.Phase -= Mathf.Tau * 64;   // без потери чётности касаний
            }
            _gait = Mathf.MoveToward(_gait, moving ? 1f : 0f, dt * (moving ? 6f : 5f));
            _body.QueueRedraw();

            // замах: предмет уходит назад и резко бьёт вперёд
            float swingOffset = 0;
            if (_swing > 0)
            {
                if (_hitStop > 0) _hitStop -= dt;
                else _swing -= dt;
                float t = 1f - Mathf.Clamp(_swing / SwingTime, 0, 1);
                swingOffset = t < 0.35f ? Mathf.Lerp(0, -1.3f, t / 0.35f) : Mathf.Lerp(-1.3f, 0.5f, (t - 0.35f) / 0.65f);
                if (!right) swingOffset = -swingOffset;
            }
            _facing = facing;
            float side = right ? 1 : -1;

            // отдача: назад вдоль ствола и вверх, быстро гаснет
            _kick = Mathf.MoveToward(_kick, 0, dt * 9f);
            float kickBack = _kick * _kick * 4f;
            float kickUp = -_kick * _kick * 0.18f * side;

            // перезарядка по фазам: 0–20% наклон вниз, ~30% магазин выпадает, 30–75% кисть с новым магазином, 75–100% возврат
            float tilt = 0, reloadHand = 0;
            if (reloading)
            {
                if (_reloadDur <= 0) StartReload(1.5f);   // увидели перезарядку без события — всё равно анимируем
                _reloadT += dt;
                float t = Mathf.Clamp(_reloadT / _reloadDur, 0, 1);
                tilt = t < 0.2f ? t / 0.2f : t > 0.75f ? 1f - (t - 0.75f) / 0.25f : 1f;
                tilt = Mathf.SmoothStep(0, 1, tilt) * 0.85f;
                reloadHand = t < 0.3f ? 0 : t < 0.5f ? (t - 0.3f) / 0.2f : t < 0.7f ? 1f - (t - 0.5f) / 0.2f : 0;
                if (!_magDropped && t >= 0.3f)
                {
                    _magDropped = true;
                    MagDropped?.Invoke(GlobalPosition + _held.Position, facing);
                }
            }
            else _reloadDur = 0;

            _held.Rotation = facing + RecoilRad + swingOffset + kickUp + tilt * side;
            _held.Scale = new Vector2(1, right ? 1 : -1);
            var basePos = new Vector2(right ? 2 : -2, -Iso.H(1.05f) * (crouch ? 0.78f : 1f) + (_body.Moving ? -Mathf.Abs(Mathf.Sin(_body.Phase)) : 0));
            _held.Position = (basePos - new Vector2(Mathf.Cos(facing), Mathf.Sin(facing)) * kickBack).Round();
            _held.ReloadHand = reloadHand;
            if (Swimming) { _ripple += dt; QueueRedraw(); }
            // звук воды: всплеск при входе, гребки на ходу
            if (Swimming && !_wasSwimming) Sfx.Play("splash", GlobalPosition, -4, 30, 0.1f);
            if (Swimming != _wasSwimming && !dead) Vfx.Splash(Position);   // вход в воду и выход — всплеск
            _wasSwimming = Swimming;
            if (Swimming && speed > 0.3f && (_strokeT -= dt) <= 0)
            {
                _strokeT = 0.85f;
                Sfx.Play("swim_stroke", GlobalPosition, -8, 22, 0.15f);
                Vfx.Splash(Position + new Vector2(Mathf.Cos(facing) * 8, -2), true);   // брызги от гребка
            }
            if (_rig != null)
            {
                _rig.SetSwim(Swimming);
                var rs = _rig.S;
                rs.Aim = facing;
                rs.Recoil = RecoilRad;
                rs.Speed = speed;
                rs.Sprint = speed > Rastvordd.Sim.World.WalkSpeed + 0.3f;
                rs.Crouch = crouch;
                rs.Dead = dead;
                rs.AimDown = Mathf.MoveToward(rs.AimDown, AimDown, dt * 6);
                rs.Phase = _body.Phase;
                rs.Gait = _gait;
                rs.Cadence = Footsteps.Cadence(_gaitSpeed, crouch);
                rs.Breath += dt * 1.6f;
                _flinch = Mathf.MoveToward(_flinch, 0, dt * 6);
                rs.Flinch = _flinch * Mathf.Abs(_flinch);   // резкий рывок и мягкий возврат
                _rig.Modulate = Colors.White.Lerp(new Color(1f, 0.55f, 0.5f), Mathf.Abs(_flinch) * 0.8f);
                rs.Held = Swimming ? null : heldId;   // в воде руки гребут
                rs.Kick = _kick;
                rs.Reload = reloading && _reloadDur > 0 ? Mathf.Clamp(_reloadT / _reloadDur, 0, 1) : -1;
                rs.Swing = _swing > 0 ? 1f - _swing / SwingTime : -1;
                _rig.Refresh(dead);
            }
            // факел: тёплый свет у огня, мерцает
            bool torch = heldId == "weapon_torch";
            _torch.Visible = torch;
            if (torch)
            {
                _flicker = Mathf.Lerp(_flicker, (float)GD.RandRange(-1.0, 1.0), Mathf.Min(1, dt * 14));
                _torch.Position = _rig != null ? _rig.TipLocal : new Vector2(side * 12, -Iso.H(1.5f));
                _torch.Energy = 1.15f + _flicker * 0.18f;
                _torch.TextureScale = (7f + _flicker * 0.25f) * Iso.PPM * 2 / 256f;
            }
            _held.SwingT = _swing > 0 ? 1f - _swing / SwingTime : -1f;
            if (_swing > 0 || _held.SwingT >= 0) QueueRedraw();
        }
    }

    public partial class Body2D : Node2D
    {
        public Color Shirt, Pants, Hair;
        public Color Skin = new Color(0.86f, 0.68f, 0.55f);
        public bool Back, Moving, Dead, Helmet;
        public float Phase;
        public Texture2D[] Front, BackFrames, WalkFront, WalkBack;

        public override void _Draw()
        {
            // ходьба из видео (если есть): цикл по фазе шага; стоя — первый кадр листа
            var walk = Back ? WalkBack : WalkFront;
            if (walk != null && Moving && !Dead)
            {
                int wi = (int)(Phase / Mathf.Tau * walk.Length) % walk.Length;
                DrawTexture(walk[wi], new Vector2(-walk[wi].GetWidth() / 2f, -walk[wi].GetHeight() + 2));   // 64 — старые кадры, 128 — новые (--hero)
                return;
            }
            var frames = Back ? BackFrames : Front;
            if (frames != null)
            {
                // цикл ходьбы ~7 кадров/с; стоя — первый кадр
                int idx = Moving && !Dead ? (int)(Phase / (Mathf.Pi / 2)) % 4 : 0;
                DrawTexture(frames[idx], new Vector2(-frames[idx].GetWidth() / 2f, -frames[idx].GetHeight() + 2), Dead ? new Color(0.55f, 0.5f, 0.5f) : Colors.White);
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
        public float ReloadHand;      // 0..1: вторая кисть уходит к магазину
        public float SwingT = -1;

        /// <summary>Держат двумя руками (вторая кисть на цевье).</summary>
        static bool TwoHanded(string id) => id is "weapon_ak" or "weapon_pipe" or "weapon_bow" or "weapon_spear" or "tool_pickaxe" or "tool_hatchet" || Weaponry.TwoHanded(id);

        static readonly Color Metal = new Color(0.14f, 0.14f, 0.15f);
        static readonly Color Steel = new Color(0.55f, 0.56f, 0.58f);
        static readonly Color Wood = new Color(0.45f, 0.3f, 0.16f);
        static readonly Color Skin = new Color(0.86f, 0.68f, 0.55f);
        static readonly Color StoneC = new Color(0.55f, 0.54f, 0.5f);

        public Held2D() => TextureFilter = TextureFilterEnum.Nearest;

        /// <summary>Длина предмета в руках, метры (по ней масштабируется спрайт).</summary>
        static float HeldLength(string id) => Weaponry.HeldLength(id) is float wl && wl > 0 ? wl : id switch
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
                // кисти — пиксельные квадратики: на рукояти и (у двуручного) на цевье; при перезарядке вторая уходит к магазину
                DrawRect(new Rect2(1, -1, 4, 4), Skin);
                DrawRect(new Rect2(1, 2, 4, 1), Skin.Darkened(0.25f));
                if (TwoHanded(ItemId))
                {
                    var fore = new Vector2(len * 0.62f, 0).Lerp(new Vector2(len * 0.38f, 7), ReloadHand).Round();
                    DrawRect(new Rect2(fore.X - 2, fore.Y - 1, 4, 4), Skin);
                    DrawRect(new Rect2(fore.X - 2, fore.Y + 2, 4, 1), Skin.Darkened(0.25f));
                    if (ReloadHand > 0.05f) DrawRect(new Rect2(fore.X - 1, fore.Y - 5, 3, 5), new Color(0.16f, 0.16f, 0.17f));   // новый магазин в руке
                }
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
                case string sp when Weaponry.IsGun(sp) && Weaponry.HeldLength(sp) < 0.7f:
                    DrawRect(new Rect2(2, -2, 18, 4), Metal);
                    DrawRect(new Rect2(-3, -2, 6, 6), Wood);
                    DrawCircle(new Vector2(4, 1), 2.6f, Skin);
                    DrawCircle(new Vector2(12, 0), 2.4f, Skin);
                    break;
                case "weapon_ak":
                case string lg when Weaponry.IsGun(lg):
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
        public override void _EnterTree() { AddToGroup(Shade.Group); ArtScale.Apply(this); }   // тень по солнцу (Shade.SetSun), дев-масштаб (F7)

        readonly Rect2 _top, _front;
        readonly Color _topC, _frontC;
        readonly WallKind _kind;
        readonly int _tier;
        readonly bool _door, _locked;
        readonly string _texPath;      // стены готовых РТ: своя текстура лица
        float _alpha = 1;

        readonly Biome _biome;

        public WallView2D(WallInfo w, Biome biome = Biome.Grassland)
        {
            _biome = biome;
            Material = VisionFx.ObjectMat;
            _kind = w.Kind;
            _tier = w.Tier;
            _door = w.Door;
            _locked = w.Locked;
            _texPath = w.Tex;
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
                    Shade.Sprite(this, tex, s, 0.32f * _alpha);
                    DrawSetTransform(Vector2.Zero, 0, new Vector2(s, s));
                    DrawTexture(tex, new Vector2(-tex.GetWidth() / 2, -tex.GetHeight() + 2), new Color(1, 1, 1, _alpha));
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                    return;
                }
            }
            if (!_cut) DrawCastShadow();
            // срез: изнутри дома стены перед персонажем рисуются низкими, как в Project Zomboid
            var frontR = _front;
            var topR = _top;
            if (_cut)
            {
                float hc = Mathf.Min(_front.Size.Y, Iso.H(0.45f));
                frontR = new Rect2(_front.Position.X, -hc, _front.Size.X, hc);
                topR = new Rect2(_top.Position.X, -hc - _top.Size.Y, _top.Size.X, _top.Size.Y);
            }
            var (frontTex, topTex) = _texPath != null ? (Art.Get(_texPath), Art.Get(_texPath)) : _kind switch
            {
                WallKind.Built when _door => (Art.Get(_tier >= 3 ? "walls/wallface_door_metal" : "walls/wallface_door_wood") ?? Art.Get("walls/wallface_wood"), Art.Get("tiles/floor_wood")),
                WallKind.Built => _tier switch
                {
                    0 => (Art.Get("walls/wallface_twig"), Art.Get("tiles/floor_wood")),
                    1 => (Art.Get("walls/wallface_wood"), Art.Get("tiles/floor_wood")),
                    2 => (Art.Get("walls/wallface_stone"), Art.Get("tiles/ground_gravel")),
                    _ => (Art.Get("walls/wallface_metal"), Art.Get("tiles/ground_concrete")),
                },
                WallKind.Crate => (Art.Get("walls/wallface_wood"), Art.Get("tiles/floor_wood")),
                WallKind.Rock => (Art.Get("walls/wallface_stone"), Art.Get("tiles/ground_gravel")),
                _ => (Art.Get("walls/wallface_concrete"), Art.Get("tiles/ground_concrete")),
            };
            if (_texPath != null && _texPath.Contains("chainlink") && frontTex != null)
            {
                DrawChainLink(frontR, topR, frontTex, _alpha);
                return;
            }
            if (frontTex != null && topTex != null)
            {
                // лицевая сторона темнее (в тени), верх светлее — так читается высота
                float a = _alpha;
                var tint = _kind == WallKind.Built && _tier == 0 ? new Color(0.85f, 0.92f, 1.05f) : _kind == WallKind.Built && _tier >= 5 ? new Color(0.44f, 0.46f, 0.52f) : _tier >= 4 ? new Color(0.62f, 0.66f, 0.74f) : Colors.White;
                DrawTextureRect(frontTex, frontR, true, new Color(0.72f * tint.R, 0.72f * tint.G, 0.72f * tint.B, a));
                if (Paint is Color pc) DrawPaint(frontR, pc, a);
                ShadeFace(frontR, a);
                // верх — материал самой стены, светлее лица (на него падает свет), а не текстура пола:
                // иначе верх сливается с полом и стена выглядит нарисованной на земле
                DrawTextureRect(frontTex, topR, true, new Color(1.22f * tint.R, 1.2f * tint.G, 1.15f * tint.B, a));
                DrawRect(new Rect2(topR.Position.X + 1, topR.Position.Y + 1, Mathf.Max(0, topR.Size.X - 2), 1), new Color(1, 0.97f, 0.9f, 0.18f * a));
                // светлая кромка на ребре «верх — лицо»: так читается толщина и угол
                DrawLine(new Vector2(topR.Position.X, topR.End.Y - 0.5f), new Vector2(topR.End.X, topR.End.Y - 0.5f), new Color(1, 0.97f, 0.9f, 0.28f * a), 1);
                if (_cut) DrawRect(topR, new Color(0, 0, 0, 0.35f), false, 1);
                if (_locked && !_cut)
                {
                    // кодовый замок: серая коробочка с красным огоньком
                    var lp = new Vector2(_front.End.X - Mathf.Min(9, _front.Size.X * 0.4f), _front.Position.Y + _front.Size.Y * 0.42f);
                    DrawRect(new Rect2(lp, new Vector2(4, 5)), new Color(0.35f, 0.36f, 0.38f, a));
                    DrawRect(new Rect2(lp + new Vector2(1, 1), new Vector2(2, 1)), new Color(0.9f, 0.15f, 0.1f, a));
                }
                if (_door && !_cut) DrawRect(new Rect2(_front.End.X - Mathf.Min(6, _front.Size.X * 0.3f), _front.Position.Y + _front.Size.Y * 0.5f, 2, 3), new Color(0.15f, 0.13f, 0.1f, a));   // ручка
                DrawRect(topR, new Color(0, 0, 0, 0.3f), false, 1);
                return;
            }
            if (_kind == WallKind.Built) { DrawRect(_front, new Color(0.45f, 0.36f, 0.25f, _alpha)); DrawRect(_top, new Color(0.6f, 0.5f, 0.35f, _alpha)); return; }
            var frontC = new Color(_frontC, _alpha);
            var topC = new Color(_topC, _alpha);
            DrawRect(_front, frontC);
            DrawRect(_top, topC);
            DrawRect(_top, topC.Darkened(0.2f), false, 1);
            if (_kind == WallKind.Crate)
            {
                for (float y = _front.Position.Y + 6; y < 0; y += 7)
                    DrawLine(new Vector2(_front.Position.X, y), new Vector2(_front.End.X, y), frontC.Darkened(0.25f), 1);
            }
            else if (_kind == WallKind.Concrete && _front.Size.X > 40)
            {
                for (float x = _front.Position.X + 32; x < _front.End.X; x += 32)
                    DrawLine(new Vector2(x, _front.Position.Y), new Vector2(x, 0), frontC.Darkened(0.12f), 1);
            }
        }
    }

    public partial class WallView2D
    {
        /// <summary>
        /// Тень стены на землю — по тому же свету, что у спрайтов (Shade): след стены, сдвинутый вправо-вверх на высоту,
        /// плюс тёмная полоса у основания с южной стороны (контакт с землёй).
        /// </summary>
        void DrawCastShadow()
        {
            float x0 = _front.Position.X, x1 = _front.End.X, d = _top.Size.Y, hh = _front.Size.Y;
            var o = new Vector2(Shade.Skew * hh, -Shade.Flat * hh);
            // выпуклая оболочка основания и его сдвига: при тени влево (утро) или тонкой стене прежний шестиугольник
            // самопересекался — Godot не мог его разбить на треугольники и на каждую перерисовку стены печатал ошибку
            // со стеком .NET (~10 мс): тысяча стен — фриз в 13 с при загрузке и рывки при смене солнца
            var hull = ConvexHull(stackalloc Vector2[]
            {
                new Vector2(x0, 0), new Vector2(x1, 0), new Vector2(x1, -d), new Vector2(x0, -d),
                new Vector2(x0, 0) + o, new Vector2(x1, 0) + o, new Vector2(x1, -d) + o, new Vector2(x0, -d) + o,
            });
            if (hull != null) DrawColoredPolygon(hull, new Color(0.03f, 0.04f, 0.03f, 0.3f * _alpha));
            for (int i = 0; i < 4; i++)
                DrawRect(new Rect2(x0 - 1, i, x1 - x0 + 2, 1), new Color(0, 0, 0, (0.32f - i * 0.075f) * _alpha));
        }

        /// <summary>Выпуклая оболочка (монотонная цепь); вырожденная (нулевая площадь) — null, такой полигон не рисуем.</summary>
        static Vector2[] ConvexHull(System.Span<Vector2> p)
        {
            for (int i = 1; i < p.Length; i++)
                for (int j = i; j > 0 && (p[j].X < p[j - 1].X || p[j].X == p[j - 1].X && p[j].Y < p[j - 1].Y); j--) (p[j], p[j - 1]) = (p[j - 1], p[j]);
            static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            System.Span<Vector2> h = stackalloc Vector2[p.Length * 2];
            int k = 0;
            for (int i = 0; i < p.Length; i++) { while (k >= 2 && Cross(h[k - 2], h[k - 1], p[i]) <= 0.01f) k--; h[k++] = p[i]; }
            for (int i = p.Length - 2, lo = k + 1; i >= 0; i--) { while (k >= lo && Cross(h[k - 2], h[k - 1], p[i]) <= 0.01f) k--; h[k++] = p[i]; }
            k--;
            if (k < 3) return null;
            float area = 0;
            for (int i = 0; i < k; i++) area += Cross(Vector2.Zero, h[i], h[(i + 1) % k]);
            return Mathf.Abs(area) < 1f ? null : h[..k].ToArray();
        }

        /// <summary>Прозрачность (растворение перед персонажем) и края лица по x, px мира — для того, что висит на стене.</summary>
        public float Alpha => _alpha;
        public float FaceX0 => Position.X + _front.Position.X;
        public float FaceX1 => Position.X + _front.End.X;

        /// <summary>Забор-рабица РТ: сквозная сетка, бетонные столбы через 3 м, колючка поверху.</summary>
        void DrawChainLink(Rect2 f, Rect2 top, Texture2D mesh, float a)
        {
            bool alongX = f.Size.X > 16;
            if (alongX)
            {
                DrawTextureRect(mesh, new Rect2(f.Position.X, f.Position.Y + 3, f.Size.X, f.Size.Y - 3), true, new Color(1, 1, 1, a));
                DrawRect(new Rect2(f.Position.X, f.Position.Y + 3, f.Size.X, 1), new Color(0.3f, 0.31f, 0.3f, 0.9f * a));   // верхняя струна
                for (float x = f.Position.X; x <= f.End.X + 0.5f; x += 96)
                {
                    float px = Mathf.Min(x, f.End.X - 4);
                    DrawRect(new Rect2(px, f.Position.Y - 4, 4, f.Size.Y + 4), new Color(0.5f, 0.49f, 0.46f, a));
                    DrawRect(new Rect2(px + 3, f.Position.Y - 4, 1, f.Size.Y + 4), new Color(0, 0, 0, 0.3f * a));
                }
                // колючая проволока: три струны с «колючками»
                for (int k = 0; k < 3; k++)
                {
                    float y = f.Position.Y - 1 - k * 2;
                    DrawLine(new Vector2(f.Position.X, y), new Vector2(f.End.X, y), new Color(0.22f, 0.22f, 0.21f, 0.85f * a), 1);
                    for (float x = f.Position.X + 3 + k * 2; x < f.End.X; x += 7) DrawRect(new Rect2(x, y - 1, 1, 3), new Color(0.25f, 0.24f, 0.22f, 0.8f * a));
                }
                DrawRect(new Rect2(f.Position.X, -2, f.Size.X, 2), new Color(0, 0, 0, 0.2f * a));
            }
            else
            {
                // вдоль y: сетка видна ребром — столбы и струны сверху
                DrawRect(new Rect2(f.Position.X + f.Size.X / 2 - 1, top.Position.Y, 2, top.Size.Y), new Color(0.3f, 0.31f, 0.3f, 0.7f * a));
                for (float y = top.End.Y; y >= top.Position.Y; y -= 96)
                    DrawRect(new Rect2(f.Position.X + f.Size.X / 2 - 2, y - f.Size.Y - 4, 4, f.Size.Y + 4), new Color(0.5f, 0.49f, 0.46f, a));
                DrawTextureRect(mesh, new Rect2(f.Position.X + f.Size.X / 2 - 1, f.Position.Y, 2, f.Size.Y), true, new Color(1, 1, 1, a));
            }
        }

        /// <summary>Покраска изнутри (обстановка РТ): советская стена в два тона — побелка сверху, масляная панель снизу.</summary>
        public Color? Paint;

        void DrawPaint(Rect2 r, Color pc, float a)
        {
            // верх — побелка поверх штукатурки/кирпича, грязная
            DrawRect(r, new Color(0.8f, 0.78f, 0.72f, 0.42f * a));
            float band = Mathf.Min(r.Size.Y * 0.55f, Iso.H(1.25f));
            var rng = new RandomNumberGenerator { Seed = (ulong)(Mathf.Abs(Position.X * 7 + Position.Y * 13) + 1) };
            for (float x = r.Position.X; x < r.End.X; x += 6)
            {
                float w = Mathf.Min(6, r.End.X - x);
                float h = band + rng.RandfRange(-1.2f, 1.2f);
                if (rng.Randf() < 0.07f) h -= rng.RandfRange(3, 9);   // облупилась краска — видно штукатурку
                DrawRect(new Rect2(x, r.End.Y - h, w, h), new Color(pc, 0.82f * a));
                DrawRect(new Rect2(x, r.End.Y - h, w, 1), new Color(pc.Darkened(0.45f), 0.9f * a));   // граница панели — тёмная полоса
                if (rng.Randf() < 0.12f) DrawRect(new Rect2(x + rng.RandfRange(0, 4), r.End.Y - h + 2, 2, rng.RandfRange(2, h - 2)), new Color(0.15f, 0.12f, 0.08f, 0.18f * a));   // подтёки
            }
        }

        /// <summary>Объём лица стены: внизу темнее (цоколь, затенение у земли), вверху светлее, края — тёмной гранью.</summary>
        void ShadeFace(Rect2 r, float a)
        {
            int rows = Mathf.Max(1, (int)r.Size.Y);
            for (int i = 0; i < rows; i += 2)
            {
                float t = i / (float)rows;                       // 0 — верх, 1 — низ
                float dark = t > 0.55f ? (t - 0.55f) * 0.55f : 0;
                float lit = t < 0.12f ? (0.12f - t) * 1.2f : 0;
                if (dark > 0) DrawRect(new Rect2(r.Position.X, r.Position.Y + i, r.Size.X, 2), new Color(0, 0, 0, dark * a));
                if (lit > 0) DrawRect(new Rect2(r.Position.X, r.Position.Y + i, r.Size.X, 2), new Color(1, 0.97f, 0.9f, lit * a));
            }
            float plinth = Mathf.Min(4, r.Size.Y * 0.12f);
            DrawRect(new Rect2(r.Position.X, r.End.Y - plinth, r.Size.X, plinth), new Color(0.05f, 0.05f, 0.04f, 0.3f * a));
            if (r.Size.X > 6)
            {
                DrawRect(new Rect2(r.Position.X, r.Position.Y, 1, r.Size.Y), new Color(0, 0, 0, 0.25f * a));
                DrawRect(new Rect2(r.End.X - 1, r.Position.Y, 1, r.Size.Y), new Color(0, 0, 0, 0.35f * a));
            }
        }

        /// <summary>Лицевая и верхняя текстуры построенной стены по уровню (дверь — свои).</summary>
        public static (Texture2D front, Texture2D top) BuiltTextures(int tier, bool door) => door
            ? (Art.Get(tier >= 3 ? "walls/wallface_door_metal" : "walls/wallface_door_wood") ?? Art.Get("walls/wallface_wood"), Art.Get("tiles/floor_wood"))
            : tier switch
            {
                0 => (Art.Get("walls/wallface_twig"), Art.Get("tiles/floor_wood")),
                1 => (Art.Get("walls/wallface_wood"), Art.Get("tiles/floor_wood")),
                2 => (Art.Get("walls/wallface_stone"), Art.Get("tiles/ground_gravel")),
                _ => (Art.Get("walls/wallface_metal"), Art.Get("tiles/ground_concrete")),
            };

        Rect2? _screen;
        /// <summary>Что стена (или пропс-спрайт) занимает на экране, в пикселях мира: лицевая грань + верх.</summary>
        public Rect2 ScreenRect
        {
            get
            {
                if (_screen.HasValue) return _screen.Value;
                var r = _front.Merge(_top);
                string prop = _kind switch
                {
                    WallKind.Car => "props/car_wreck",
                    WallKind.Barrier => "props/concrete_barrier",
                    WallKind.Junk => "props/junk_pile",
                    WallKind.Barrel => "props/barrel_oil",
                    WallKind.WoodCrate => "props/crate_wood",
                    _ => null,
                };
                var tex = prop != null ? Art.Styled(prop, _biome) : null;
                if (tex != null)
                {
                    float s = _kind == WallKind.Car ? 1.4f : 1f;
                    r = new Rect2(-tex.GetWidth() / 2f * s, (-tex.GetHeight() + 2) * s, tex.GetWidth() * s, tex.GetHeight() * s);
                }
                _screen = new Rect2(r.Position + Position, r.Size);
                return _screen.Value;
            }
        }

        /// <summary>Прозрачность по фокусам кадра (Occlusion); срезанная стена не растворяется.</summary>
        public void UpdateOcclusion(float dt) => SetFade(!_cut && Occlusion.Covers(ScreenRect, Position.Y), dt);

        bool _cut;
        /// <summary>Срезать стену до низкой (персонаж внутри дома, стена перед ним).</summary>
        public void SetCut(bool cut)
        {
            if (cut == _cut) return;
            _cut = cut;
            QueueRedraw();
        }

        public void SetFade(bool fade, float dt)
        {
            if (Occlusion.Step(ref _alpha, fade, dt)) QueueRedraw();
        }
    }

    /// <summary>Ресурс на карте; при ударе слегка трясётся.</summary>
    public abstract partial class NodeView2D : Node2D
    {
        public override void _EnterTree() { AddToGroup(Shade.Group); ArtScale.Apply(this); }   // тень по солнцу (Shade.SetSun), дев-масштаб (F7)

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
            Material = VisionFx.TreeMat;
            _trunkW = radiusM * 2 * Iso.PPM * 0.7f;
            _tex = Art.Tree(seed, biome);
            _tuft = Art.Get(biome is Biome.Arid or Biome.Tundra or Biome.Beach ? "decals/decal_grass_tuft_2" : "decals/decal_grass_tuft_1");
            _seed = seed;
            if (_tex != null) CanopyR = _tex.GetWidth() * 0.42f;
            TextureFilter = TextureFilterEnum.Nearest;
        }

        /// <summary>Силуэт дерева на экране (пиксели мира): крона и ствол, без прозрачных краёв спрайта и тени у корней.</summary>
        public Rect2 ScreenRect
        {
            get
            {
                float halfW = _tex != null ? _tex.GetWidth() * 0.4f : CanopyR;
                float top = Base.Y - (_tex != null ? _tex.GetHeight() * 0.95f : Iso.H(4.5f));
                return new Rect2(Base.X - halfW, top, halfW * 2, Base.Y - 4 - top);
            }
        }

        /// <summary>Крона полупрозрачна, если за деревом стоит игрок, видимый ему персонаж или курсор.</summary>
        public void UpdateOcclusion(float dt)
        {
            if (Occlusion.Step(ref _alpha, Occlusion.Covers(ScreenRect, Base.Y + 4), dt)) QueueRedraw();
        }

        public override void _Draw()
        {
            if (_tex != null) Shade.Sprite(this, _tex, 1f, 0.26f);
            else
            {
                DrawSetTransform(Vector2.Zero, 0, new Vector2(1, 0.45f));
                DrawCircle(new Vector2(10, 0), CanopyR * 0.9f, new Color(0, 0, 0, 0.2f));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
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
            if (kind == NodeKind.Barrel) SelfModulate = new Color(0.7f, 0.85f, 1.2f);   // лутовая бочка — синеватая, в отличие от декоративной
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
            float s = _kind == NodeKind.Barrel ? 1f : 0.55f + 0.45f * Mathf.Sqrt(Mathf.Clamp(Fill, 0, 1));   // бочка не уменьшается от ударов
            if (_tex != null) Shade.Sprite(this, _tex, s, 0.3f, 3f);
            else
            {
                DrawSetTransform(Vector2.Zero, 0, new Vector2(1, 0.4f));
                DrawCircle(new Vector2(4, 0), 24 * s, new Color(0, 0, 0, 0.22f));
            }
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
        public override void _EnterTree() { AddToGroup(Shade.Group); ArtScale.Apply(this); }   // тень по солнцу (Shade.SetSun), дев-масштаб (F7)

        public string Item;   // LocalItem: какой предмет РТ лежит
        public bool Available = true;
        readonly PickupKind _kind;

        readonly Biome _biome;

        public PickupView2D(PickupKind kind, Biome biome = Biome.Grassland)
        {
            _kind = kind;
            _biome = biome;
            Material = kind is PickupKind.WaterBarrel or PickupKind.OilBarrel or PickupKind.CanisterPile ? VisionFx.ObjectMat : VisionFx.GrassMat;
            TextureFilter = TextureFilterEnum.Nearest;
        }

        public override void _Draw()
        {
            if (_kind is PickupKind.Fuse or PickupKind.PunchCard or PickupKind.LocalItem)
            {
                // пробка Подстанции, перфокарта, брелок, кабель: маленькие, но с бликом, чтобы их было видно
                var ft = !Available ? null : _kind switch
                {
                    PickupKind.Fuse => Art.Get("icons/icon_fuse"),
                    PickupKind.PunchCard => Art.Get("icons/icon_punchcard_1"),
                    _ => Art.Get($"icons/icon_{Item}") ?? Art.Get(Item == "rt_cable" ? "icons/icon_tape" : "icons/icon_keycard"),
                };
                if (ft == null) return;
                DrawSetTransform(new Vector2(0, -2), 0, new Vector2(0.32f, 0.32f));
                DrawTexture(ft, new Vector2(-ft.GetWidth() / 2, -ft.GetHeight()));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                DrawCircle(new Vector2(4, -16), 1.5f, new Color(1, 1, 0.85f, 0.8f));
                return;
            }
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
                PickupKind.OilBarrel => Art.Get("props/barrel_mazut"),        // бочка с мазутом (D54): после слива — та же бочка, тусклее
                PickupKind.CanisterPile => Art.Get("props/canister_pile_rusty"),
                _ => Art.Get("props/barrel_water"),
            };
            if (tex != null)
            {
                Shade.Sprite(this, tex);
                DrawTexture(tex, new Vector2(-tex.GetWidth() / 2, -tex.GetHeight() + 2), _kind is PickupKind.OilBarrel or PickupKind.CanisterPile && !Available ? new Color(0.7f, 0.7f, 0.7f) : Colors.White);
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
        public override void _EnterTree() { AddToGroup(Shade.Group); ArtScale.Apply(this); }   // тень по солнцу (Shade.SetSun), дев-масштаб (F7)

        public bool Burning;
        readonly DeployKind _kind;
        float _flicker, _smokeAcc;

        readonly PointLight2D _light;
        readonly AudioStreamPlayer2D _crackle;

        readonly int _level;
        readonly int _rot;
        readonly Vector2 _foot;   // хитбокс в пикселях (полные стороны); начало узла — передний край следа (D52)

        public DeployView2D(DeployKind kind, int level = 0, int rot = 0, SVec half = default)
        {
            _kind = kind;
            _level = level;
            _rot = rot & 3;
            if (half == SVec.Zero) half = Footprints.HalfOf(kind, rot);
            _foot = new Vector2(half.X * 2 * Iso.PPM, half.Y * 2 * Iso.PPM);
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
            else if (StationDefs.Get(kind) is StationDef st && !st.Registered)
            {
                // станции D54: свет (лампы — белый, огонь — тёплый), звук работы
                _st = st;
                // след на полу у станций не рисуем: яркая лампа рядом «засвечивает» полупрозрачный прямоугольник (светлые квадраты)
                if (st.Light > 0)
                {
                    bool lamp = kind is DeployKind.Lamp or DeployKind.Floodlight;
                    _light = Art.FireLight(lamp ? st.Light * 0.8f : st.Light, 0f);
                    if (lamp) _light.Color = new Color(1f, 0.95f, 0.82f);
                    _light.Position = new Vector2(0, lamp ? -Iso.H(1.6f) : -10);
                    AddChild(_light);
                }
                string loop = kind switch
                {
                    DeployKind.Generator => Sfx.Pick("generator_loop", Sfx.Pick("lathe_work", "furnace_loop")),
                    DeployKind.Campfire => "furnace_loop",
                    DeployKind.Still => Sfx.Pick("still_loop", "furnace_loop"),
                    DeployKind.Drill => Sfx.Pick("drill_rig_loop", Sfx.Pick("generator_loop", "lathe_work")),
                    DeployKind.Rectifier => Sfx.Pick("rectifier_loop", Sfx.Pick("generator_loop", "furnace_loop")),
                    DeployKind.EFurnace => Sfx.Pick("efurnace_loop", "furnace_loop"),
                    DeployKind.Conveyor => Sfx.Pick("conveyor_loop", "lathe_work"),
                    _ => null,
                };
                if (loop != null) { _crackle = Sfx.Loop(loop, -9, 16); AddChild(_crackle); }
            }
        }
        readonly StationDef _st;

        public void Tick(float dt)
        {
            if (_crackle != null && _crackle.Stream != null && Burning != _crackle.Playing) { if (Burning) _crackle.Play(); else _crackle.Stop(); }
            bool steady = _kind is DeployKind.Lamp or DeployKind.Floodlight or DeployKind.EFurnace;   // электрический свет не мерцает
            if (_light != null) _light.Energy = Burning ? (steady ? 0.75f : 0.9f + 0.25f * Mathf.Sin(_flicker * 11) * Mathf.Sin(_flicker * 5.7f)) : 0f;
            if (!Burning) return;
            _flicker += dt;
            // дымок из трубы печи (верх спрайта)
            if (_kind == DeployKind.Furnace && (_smokeAcc += dt) > 0.8f) { _smokeAcc = 0; Vfx.Puff("smoke_wisp", Position + new Vector2(0, 1), 42, 12, 0.6f, 5, 3); }
            // станции D54: самогонный аппарат, буровая и костёр дымят — базу видно издалека (столб выше печного)
            if (_st != null && _st.Smoke && (_smokeAcc += dt) > (_kind == DeployKind.Campfire ? 0.9f : 0.5f))
            { _smokeAcc = 0; Vfx.Puff("smoke_wisp", Position + new Vector2(0, 1), _kind == DeployKind.Campfire ? 16 : 52, _kind == DeployKind.Campfire ? 14 : 26, 0.7f, 5, 3); }
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_st != null && _st.Sprite == null) { DrawCampfire(); return; }
            var tex = Art.Get(_kind switch { DeployKind.Workbench => _level >= 2 ? $"props/workbench{_level}" : "props/workbench", DeployKind.Claim => "props/claim_post", DeployKind.Box => "props/crate_wood", DeployKind.SleepBag => "props/sleeping_bag", DeployKind.Recycler => "props/recycler", DeployKind.Lathe => Art.Get("props/lathe") != null ? "props/lathe" : "props/recycler",
                _ when _st != null => "props/" + (_rot % 2 == 1 && Art.Get($"props/{_st.Sprite}_side") != null ? _st.Sprite + "_side" : _st.Sprite), _ => "props/furnace" });
            if (tex == null && _kind == DeployKind.Claim)
            {
                // запасной вид столба: столб с жестяным ящиком и красной тряпкой
                DrawRect(new Rect2(-2, -Iso.H(1.8f), 4, Iso.H(1.8f)), new Color(0.36f, 0.27f, 0.17f));
                DrawRect(new Rect2(-5, -Iso.H(1.1f), 10, 8), new Color(0.5f, 0.5f, 0.48f));
                DrawRect(new Rect2(2, -Iso.H(1.8f), 5, 3), new Color(0.6f, 0.15f, 0.12f));
                return;
            }
            // след на полу: прямоугольник хитбокса (сетка клеток D52) — база выглядит аккуратно, видно, что сколько занимает
            if (_kind != DeployKind.Recycler && _st == null) DrawRect(new Rect2(-_foot.X / 2, -_foot.Y, _foot.X, _foot.Y), new Color(0, 0, 0, 0.14f));
            if (tex != null)
            {
                // спрайт вписан в ширину следа (станок без своей картинки — переработчик, он крупнее клетки), поворот 2/3 — зеркало
                float sc = Mathf.Min(1f, (_foot.X + 6) / tex.GetWidth());
                Shade.Sprite(this, tex, sc, at: new Vector2(0, 2));
                DrawSetTransform(new Vector2(0, 2), 0, new Vector2(_rot >= 2 ? -sc : sc, sc));
                DrawTexture(tex, new Vector2(-tex.GetWidth() / 2, -tex.GetHeight()));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                if (_kind == DeployKind.Furnace && Burning)
                {
                    // огонь в топке: 4 кадра по кругу (~10 к/с) + мерцающее свечение
                    float k = 0.75f + 0.25f * Mathf.Sin(_flicker * 13) * Mathf.Sin(_flicker * 7.3f);
                    DrawCircle(new Vector2(-1, -11), 16, new Color(1f, 0.55f, 0.2f, 0.16f * k));
                    DrawCircle(new Vector2(-1, -11), 7, new Color(1f, 0.6f, 0.25f, 0.35f * k));
                    // огонь в топке: лист fire_torch (12 px), 12 к/с
                    Vfx.DrawFrame(this, "fire_torch", new Vector2(-1, -5), _flicker, 12);
                }
                if (_kind == DeployKind.Conveyor)
                {
                    // стрелка «куда несёт» (поворот R)
                    var d = World.ConveyorDir(_rot);
                    var dir = new Vector2(d.X, d.Y);
                    var c = new Vector2(0, -_foot.Y / 2);
                    DrawLine(c - dir * 9, c + dir * 9, new Color(1f, 0.85f, 0.3f, 0.9f), 2f);
                    DrawLine(c + dir * 9, c + dir * 4 + dir.Orthogonal() * 4, new Color(1f, 0.85f, 0.3f, 0.9f), 2f);
                    DrawLine(c + dir * 9, c + dir * 4 - dir.Orthogonal() * 4, new Color(1f, 0.85f, 0.3f, 0.9f), 2f);
                }
                if (_st != null && Burning && _kind is DeployKind.Still or DeployKind.EFurnace)
                {
                    float k = 0.75f + 0.25f * Mathf.Sin(_flicker * 13) * Mathf.Sin(_flicker * 7.3f);
                    DrawCircle(new Vector2(0, -8), 12, new Color(1f, 0.55f, 0.2f, (_kind == DeployKind.EFurnace ? 0.25f : 0.14f) * k));
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

    public partial class DeployView2D
    {
        /// <summary>Костёр (D54): камни по кругу, поленья, огонь из листа fire_torch, угли.</summary>
        void DrawCampfire()
        {
            DrawCircle(new Vector2(0, -6), 13, new Color(0, 0, 0, 0.18f));
            var stone = new Color(0.46f, 0.45f, 0.43f);
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.Tau;
                var p = new Vector2(Mathf.Cos(a) * 11, -6 + Mathf.Sin(a) * 11);
                DrawCircle(p, 3f, stone.Darkened(0.12f * (i % 3)));
            }
            var wood = new Color(0.38f, 0.25f, 0.14f);
            DrawLine(new Vector2(-8, -3), new Vector2(7, -10), wood, 3f);
            DrawLine(new Vector2(-7, -10), new Vector2(8, -3), wood.Darkened(0.2f), 3f);
            if (!Burning) { DrawCircle(new Vector2(0, -6), 4, new Color(0.2f, 0.18f, 0.16f)); return; }
            float k = 0.75f + 0.25f * Mathf.Sin(_flicker * 13) * Mathf.Sin(_flicker * 7.3f);
            DrawCircle(new Vector2(0, -8), 18, new Color(1f, 0.55f, 0.2f, 0.12f * k));
            DrawCircle(new Vector2(0, -7), 6, new Color(1f, 0.45f, 0.15f, 0.5f * k));
            Vfx.DrawFrame(this, "fire_torch", new Vector2(0, -4), _flicker, 12);
        }
    }

    public partial class BagView2D : Node2D
    {
        public override void _EnterTree() { AddToGroup(Shade.Group); ArtScale.Apply(this); }   // тень по солнцу (Shade.SetSun), дев-масштаб (F7)

        public bool IsCrate, Empty, Safe, Punch, Unlocked;
        public int Id, Tier;
        public string Look;   // ивент-ящик: "airdrop" — контейнер сброса, "military" — военный ящик
        PointLight2D _aura;

        public BagView2D()
        {
            TextureFilter = TextureFilterEnum.Nearest;
            Material = VisionFx.LootMat.Duplicate() as ShaderMaterial;
        }

        // вид крейта по уровню лута: бытовые — дерево, еда, аптечка, инструменты, электрика; охраняемые — армейский, патроны;
        // крупные РТ — оружейный и «элитный». Выбор по номеру — у одного ящика всегда один вид.
        static readonly string[][] Styles =
        {
            new[] { "loot_crate_wood", "loot_crate_food", "loot_crate_medical", "loot_crate_tools", "loot_crate_electric" },
            new[] { "loot_crate_army", "loot_crate_ammo", "loot_crate_tools", "loot_crate_medical" },
            new[] { "loot_crate_weapon", "loot_crate_army", "loot_crate_elite" },
        };
        static readonly System.Collections.Generic.Dictionary<string, Color> Accent = new()
        {
            ["loot_crate_wood"] = new(0.45f, 1f, 0.45f), ["loot_crate_food"] = new(0.45f, 0.7f, 1f), ["loot_crate_medical"] = new(1f, 0.35f, 0.35f),
            ["loot_crate_tools"] = new(1f, 0.6f, 0.2f), ["loot_crate_electric"] = new(0.4f, 0.75f, 1f), ["loot_crate_army"] = new(1f, 0.85f, 0.3f),
            ["loot_crate_ammo"] = new(1f, 0.85f, 0.3f), ["loot_crate_weapon"] = new(1f, 0.3f, 0.25f), ["loot_crate_elite"] = new(1f, 0.45f, 0.15f),
        };

        string Style => Punch ? "crate_punch" : Safe ? "loot_safe" : !IsCrate ? null : Styles[System.Math.Clamp(Tier, 0, 2)][(int)((uint)Id * 2654435761u % (uint)Styles[System.Math.Clamp(Tier, 0, 2)].Length)];

        public override void _Ready()
        {
            // ночью крейт с лутом чуть светится цветом своего вида — видно издалека, но не выдаёт стрелков
            if (IsCrate && !Punch && Style != null && Accent.TryGetValue(Style, out var c))
            {
                _aura = new PointLight2D { Texture = Art.LightTex, TextureScale = 2.2f * Iso.PPM * 2 / 256f, Color = c, Energy = 0.35f, Position = new Vector2(0, -10), BlendMode = Light2D.BlendModeEnum.Add, ShadowEnabled = true, ShadowFilter = Light2D.ShadowFilterEnum.Pcf5 };
                AddChild(_aura);
            }
        }

        public override void _Draw()
        {
            string style = Style;
            if (Look != null)
            {
                // ивент: свой спрайт; закрыт — с подсветкой лута, вскрыт и пуст — темнее
                var et = Art.Get(Look == "airdrop" ? "containers/airdrop_crate" : "containers/crate_army_green");
                if (et != null)
                {
                    (Material as ShaderMaterial)?.SetShaderParameter("loot_on", Empty ? 0f : 1f);
                    float es = Look == "airdrop" ? 1.5f : 1.8f;
                    Shade.Sprite(this, et, es, 0.32f, 1f);
                    DrawSetTransform(Vector2.Zero, 0, new Vector2(es, es));
                    DrawTexture(et, new Vector2(-et.GetWidth() / 2, -et.GetHeight() + 1), Empty ? new Color(0.5f, 0.5f, 0.5f) : Colors.White);
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                    return;
                }
            }
            Texture2D tex = style == null ? Art.Get("props/loot_bag")
                : Punch ? Art.Get(Unlocked ? "props/crate_punch_open" : "props/crate_punch")
                : (Empty ? Art.Get($"loot/{style}_open") : null) ?? Art.Get($"loot/{style}") ?? Art.Get(Safe ? "props/safe" : "props/crate_military");
            (Material as ShaderMaterial)?.SetShaderParameter("loot_on", Empty || (Punch && Unlocked) ? 0f : 1f);
            if (_aura != null) _aura.Enabled = !Empty;
            if (tex != null)
            {
                // ящики и сумки делаем чуть крупнее реального — иначе их не видно
                float s = IsCrate ? 1.4f : 1.3f;
                Shade.Sprite(this, tex, s, 0.32f, 1f);
                DrawSetTransform(Vector2.Zero, 0, new Vector2(s, s));
                bool hasOpen = style != null && Art.Get($"loot/{style}_open") != null;
                DrawTexture(tex, new Vector2(-tex.GetWidth() / 2, -tex.GetHeight() + 1), Empty && !hasOpen ? new Color(0.45f, 0.45f, 0.45f) : Colors.White);
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

    /// <summary>
    /// Призрак установки под курсором (сетка клеток D52): слоты пола под курсором (зелёный — свободен, красный — занят,
    /// жёлтый — проход у двери) и прямоугольник предмета по размеру; зелёный — встанет, красный — нет.
    /// </summary>
    public partial class GhostView2D : Node2D
    {
        bool _ok, _snapped;
        SVec _pos, _half;
        System.Collections.Generic.List<(SVec[] poly, int state)> _slots;

        public GhostView2D() { ZIndex = 6; }

        public void Set(bool ok, SVec pos, SVec half, bool snapped, System.Collections.Generic.List<(SVec[] poly, int state)> slots)
        {
            bool same = ok == _ok && pos == _pos && half == _half && snapped == _snapped && SameSlots(slots);
            if (same) return;
            _ok = ok; _pos = pos; _half = half; _snapped = snapped; _slots = slots;
            QueueRedraw();
        }

        bool SameSlots(System.Collections.Generic.List<(SVec[] poly, int state)> s)
        {
            if ((s == null) != (_slots == null)) return false;
            if (s == null) return true;
            if (s.Count != _slots.Count) return false;
            for (int i = 0; i < s.Count; i++) if (s[i].state != _slots[i].state || s[i].poly[0] != _slots[i].poly[0]) return false;
            return true;
        }

        public override void _Draw()
        {
            if (_slots != null)
                foreach (var (poly, st) in _slots)
                {
                    var c = st switch
                    {
                        1 => new Color(1f, 0.3f, 0.25f, 0.30f),      // занят
                        2 => new Color(1f, 0.8f, 0.25f, 0.22f),      // проход у двери
                        3 => _ok ? new Color(0.4f, 1f, 0.45f, 0.40f) : new Color(1f, 0.3f, 0.25f, 0.40f),   // сюда встанет
                        _ => new Color(0.4f, 1f, 0.45f, 0.16f),      // свободен
                    };
                    var pts = new Vector2[poly.Length];
                    for (int i = 0; i < pts.Length; i++) pts[i] = Iso.Px(poly[i]);
                    DrawColoredPolygon(pts, c);
                    for (int i = 0; i < pts.Length; i++) DrawLine(pts[i], pts[(i + 1) % pts.Length], new Color(c, 0.8f), 1);
                }
            var fill = _ok ? new Color(0.4f, 1f, 0.45f, 0.35f) : new Color(1f, 0.3f, 0.25f, 0.35f);
            var line = new Color(fill, 0.95f);
            var min = Iso.Px(_pos - _half); var max = Iso.Px(_pos + _half);
            float hh = Iso.H(0.5f);
            var top = new Rect2(min.X, min.Y - hh, max.X - min.X, max.Y - min.Y);
            var front = new Rect2(min.X, max.Y - hh, max.X - min.X, hh);
            DrawRect(front, fill.Darkened(0.2f));
            DrawRect(top, fill);
            DrawRect(top, line, false, 1.5f);
            DrawRect(new Rect2(min, max - min), new Color(line, 0.5f), false, 1);   // след на полу
        }
    }
}

namespace Rastvordd.Client
{
    /// <summary>Гильза или выпавший магазин: летит, падает, отскакивает, лежит и тает.</summary>
    public partial class Debris2D : Godot.Node2D
    {
        public Godot.Vector2 Vel;
        public float Z, VZ, Life = 3f;
        public Godot.Color Color = new(0.85f, 0.68f, 0.25f);
        public Godot.Vector2 Size = new(2, 1);
        public float Spin;

        /// <summary>Шаг физики; false — пора удалить.</summary>
        public bool Tick(float dt)
        {
            Life -= dt;
            if (Life <= 0) return false;
            VZ -= 420f * dt;
            Z += VZ * dt;
            Position += Vel * dt;
            Rotation += Spin * dt;
            if (Z <= 0)
            {
                Z = 0;
                if (VZ < -30) { VZ = -VZ * 0.3f; Vel *= 0.5f; Spin *= 0.5f; }   // отскок
                else { VZ = 0; Vel = Vel.MoveToward(Godot.Vector2.Zero, 200 * dt); Spin = 0; }
            }
            QueueRedraw();
            return true;
        }

        public override void _Draw()
        {
            var c = new Godot.Color(Color, Godot.Mathf.Clamp(Life, 0, 1));
            DrawSetTransform(new Godot.Vector2(0, -Z), 0, Godot.Vector2.One);
            DrawRect(new Godot.Rect2(-Size / 2, Size), c);
        }
    }
}
