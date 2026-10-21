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
        const float SwingTime = 0.45f;   // замах → удар → возврат (риг)

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
            _body.WalkFront = Art.WalkFrames(sheet, 'f');
            _body.WalkBack = Art.WalkFrames(sheet, 'b');
            _body.TextureFilter = TextureFilterEnum.Nearest;
            _held = new Held2D { Position = new Vector2(1, -Iso.H(1.05f)) };
            AddChild(_body);
            AddChild(_held);
            // управляемый персонаж из частей (если части сгенерированы): руки по IK к оружию, ноги шагают, позы
            var rigSet = RigSet.Load(isNpc ? "guard" : "naked");
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

        public void StartSwing() => _swing = SwingTime;

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
            _body.Moving = speed > 0.3f;
            _body.Phase += dt * (4f + speed * 1.6f);
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
            _wasSwimming = Swimming;
            if (Swimming && speed > 0.3f && (_strokeT -= dt) <= 0) { _strokeT = 0.85f; Sfx.Play("swim_stroke", GlobalPosition, -8, 22, 0.15f); }
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
                DrawTexture(walk[wi], new Vector2(-32, -62));
                return;
            }
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
        public float ReloadHand;      // 0..1: вторая кисть уходит к магазину
        public float SwingT = -1;

        /// <summary>Держат двумя руками (вторая кисть на цевье).</summary>
        static bool TwoHanded(string id) => id is "weapon_ak" or "weapon_pipe" or "weapon_bow" or "weapon_spear" or "tool_pickaxe" or "tool_hatchet";

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
        readonly int _tier;
        readonly bool _door, _locked;
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
                    DrawTexture(tex, new Vector2(-tex.GetWidth() / 2, -tex.GetHeight() + 2), new Color(1, 1, 1, _alpha));
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                    return;
                }
            }
            DrawRect(new Rect2(_front.Position.X + 3, -2, _front.Size.X, 6), new Color(0, 0, 0, 0.2f));
            // срез: изнутри дома стены перед персонажем рисуются низкими, как в Project Zomboid
            var frontR = _front;
            var topR = _top;
            if (_cut)
            {
                float hc = Mathf.Min(_front.Size.Y, Iso.H(0.45f));
                frontR = new Rect2(_front.Position.X, -hc, _front.Size.X, hc);
                topR = new Rect2(_top.Position.X, -hc - _top.Size.Y, _top.Size.X, _top.Size.Y);
            }
            var (frontTex, topTex) = _kind switch
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
            if (frontTex != null && topTex != null)
            {
                // лицевая сторона темнее (в тени), верх светлее — так читается высота
                float a = _alpha;
                var tint = _kind == WallKind.Built && _tier == 0 ? new Color(0.85f, 0.92f, 1.05f) : _kind == WallKind.Built && _tier >= 4 ? new Color(0.62f, 0.66f, 0.74f) : Colors.White;
                DrawTextureRect(frontTex, frontR, true, new Color(0.78f * tint.R, 0.78f * tint.G, 0.78f * tint.B, a));
                DrawTextureRect(topTex, topR, true, new Color(1.05f * tint.R, 1.05f * tint.G, 1.05f * tint.B, a));
                if (_cut) DrawRect(topR, new Color(0, 0, 0, 0.35f), false, 1);
                if (_locked && !_cut)
                {
                    // кодовый замок: серая коробочка с красным огоньком
                    var lp = new Vector2(_front.End.X - Mathf.Min(9, _front.Size.X * 0.4f), _front.Position.Y + _front.Size.Y * 0.42f);
                    DrawRect(new Rect2(lp, new Vector2(4, 5)), new Color(0.35f, 0.36f, 0.38f, a));
                    DrawRect(new Rect2(lp + new Vector2(1, 1), new Vector2(2, 1)), new Color(0.9f, 0.15f, 0.1f, a));
                }
                if (_door && !_cut) DrawRect(new Rect2(_front.End.X - Mathf.Min(6, _front.Size.X * 0.3f), _front.Position.Y + _front.Size.Y * 0.5f, 2, 3), new Color(0.15f, 0.13f, 0.1f, a));   // ручка
                DrawLine(new Vector2(topR.Position.X, topR.End.Y), topR.End, new Color(0, 0, 0, 0.35f), 1);
                DrawRect(topR, new Color(0, 0, 0, 0.25f), false, 1);
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
            Material = kind == PickupKind.WaterBarrel ? VisionFx.ObjectMat : VisionFx.GrassMat;
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

        readonly int _level;

        public DeployView2D(DeployKind kind, int level = 0)
        {
            _kind = kind;
            _level = level;
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
            var tex = Art.Get(_kind switch { DeployKind.Workbench => _level >= 2 ? $"props/workbench{_level}" : "props/workbench", DeployKind.Claim => "props/claim_post", DeployKind.Box => "props/crate_wood", DeployKind.SleepBag => "props/sleeping_bag", DeployKind.Recycler => "props/recycler", _ => "props/furnace" });
            if (tex == null && _kind == DeployKind.Claim)
            {
                // запасной вид столба: столб с жестяным ящиком и красной тряпкой
                DrawRect(new Rect2(-2, -Iso.H(1.8f), 4, Iso.H(1.8f)), new Color(0.36f, 0.27f, 0.17f));
                DrawRect(new Rect2(-5, -Iso.H(1.1f), 10, 8), new Color(0.5f, 0.5f, 0.48f));
                DrawRect(new Rect2(2, -Iso.H(1.8f), 5, 3), new Color(0.6f, 0.15f, 0.12f));
                return;
            }
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
        public bool IsCrate, Empty, Safe;

        public BagView2D()
        {
            TextureFilter = TextureFilterEnum.Nearest;
            Material = VisionFx.ObjectMat;
        }

        public override void _Draw()
        {
            var tex = Art.Get(Safe ? "props/safe" : IsCrate ? "props/crate_military" : "props/loot_bag") ?? Art.Get("props/crate_military");
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
