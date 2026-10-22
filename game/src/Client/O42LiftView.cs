using System;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>Клиентская анимация оголовка. Не меняет мир, посадку и расписание сервера.</summary>
    public partial class O42LiftView : Node2D
    {
        public enum LiftState { Idle, Warning, Opening, Boarding, ClosingDescend }
        public LiftState State { get; private set; } = LiftState.Idle;
        public event Action<LiftState> StateChanged;
        public event Action DoorSlammed;
        public float Remaining => State == LiftState.Boarding ? Mathf.Max(0, _boarding - _elapsed) : Mathf.Max(0, Duration - _elapsed);
        float Duration => State switch { LiftState.Warning => 10, LiftState.Opening => 1.5f, LiftState.Boarding => _boarding, LiftState.ClosingDescend => 3, _ => 0 };
        float _boarding = 180, _elapsed, _clock, _beacon, _sound, _steam;
        bool _slammed;
        Texture2D _closed, _open;
        PointLight2D _red, _amber;
        readonly Node2D _glow = new();
        static readonly Vector2 Origin = new(-112, -190);
        static readonly Vector2[] Corners = { new(-75, -182), new(73, -182), new(89, -75), new(-88, -75) };
        // Четыре входа в проекции 3/4: северный виден по индикатору на крыше.
        static readonly Vector2[] Entrances = { new(0, -172), new(91, -78), new(0, -53), new(-91, -78) };
        static readonly Color Red = new(1, 0.12f, 0.06f), Amber = new(1, 0.55f, 0.12f);

        public override void _Ready()
        {
            _closed = Art.Get("concept/o42_lift_n2");
            _open = Art.Get("concept/o42_lift_n2_open");
            TextureFilter = TextureFilterEnum.Nearest;
            _glow.Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
            AddChild(_glow);
            _glow.Draw += DrawGlow;
            _red = Art.FireLight(5, 0); _red.Color = Red; _red.Position = new(0, -20); AddChild(_red);
            _amber = Art.FireLight(1.6f, 0.25f); _amber.Color = Amber; AddChild(_amber);
        }

        /// <summary>Начать заново, включая предупреждение. Длительность посадки — в секундах.</summary>
        public void Start(float boardingSeconds = 180)
        {
            if (!float.IsFinite(boardingSeconds) || boardingSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(boardingSeconds));
            _boarding = boardingSeconds;
            Change(LiftState.Warning);
        }

        void Change(LiftState state)
        {
            State = state; _elapsed = _sound = _steam = 0; _slammed = false;
            if (state == LiftState.Opening)
            {
                Play("o42_door_open", "fuse_hiss", -5);
                foreach (var p in Entrances) Vfx.Dust(ToGlobal(p), true, 0.4f);
            }
            StateChanged?.Invoke(state);
        }

        void Play(string id, string fallback, float db, float pitch = 1) =>
            Sfx.Play(Sfx.Pick(id, fallback), GlobalPosition, db, 65, 0, pitch, Sfx.Room.Quiet);

        float Aperture => State switch
        {
            LiftState.Opening => Mathf.SmoothStep(0, 1, _elapsed / 1.5f),
            LiftState.Boarding => 1,
            LiftState.ClosingDescend => 1 - Mathf.SmoothStep(0, 1, _elapsed / 0.55f),
            _ => 0
        };

        public override void _Process(double delta)
        {
            float dt = (float)delta;
            _clock += dt; _elapsed += dt;
            _beacon = Mathf.PosMod(_beacon + dt * (State == LiftState.Warning ? 5 : 0.7f), Mathf.Tau);
            if (State != LiftState.Idle && _elapsed >= Duration)
                Change(State switch { LiftState.Warning => LiftState.Opening, LiftState.Opening => LiftState.Boarding, LiftState.Boarding => LiftState.ClosingDescend, _ => LiftState.Idle });
            if (State == LiftState.ClosingDescend && !_slammed && _elapsed >= 0.55f)
            {
                _slammed = true;
                Play("o42_door_slam", "hit_metal_ore", 1, 0.65f);
                Vfx.Dust(GlobalPosition);
                DoorSlammed?.Invoke();
            }
            _sound -= dt; _steam -= dt;
            if (_sound <= 0)
            {
                _sound = State == LiftState.Warning ? 1.15f : 0.55f;
                if (State == LiftState.Warning) Play("o42_siren", "c4_beep", -6);
                if (State == LiftState.Boarding) Play("o42_hum", "fuse_hiss", -25, 0.55f);
                if (State == LiftState.ClosingDescend && _slammed)
                    Play("o42_descend", "fire_whoosh", -8 - 22 * _elapsed / 3, 0.85f - 0.35f * _elapsed / 3);
            }
            if (_steam <= 0 && State is LiftState.Opening or LiftState.ClosingDescend)
            {
                _steam = 0.24f;
                foreach (var p in Entrances) Vfx.Steam(ToGlobal(p + new Vector2(0, 15)), 12, 1);
            }
            _red.Energy = State == LiftState.Warning ? 0.18f + 0.12f * (1 + Mathf.Sin(_clock * 7)) : Aperture * 0.75f;
            _amber.Position = new Vector2(Mathf.Cos(_beacon) * 40, -136 + Mathf.Sin(_beacon) * 22);
            QueueRedraw(); _glow.QueueRedraw();
        }

        public override void _Draw()
        {
            if (_closed == null || _open == null) return;
            var rect = new Rect2(Origin, new Vector2(224, 219));
            float a = Aperture;
            // У исходников разные крыши и ширина проёма. Сохраняем один корпус,
            // закрывая открытый спрайт створками из закрытого: без двойных контуров
            // и растворения металла. При полном открытии остаётся только open.
            var tint = new Color(0.78f + 0.22f * a, 0.8f + 0.2f * a, 0.82f + 0.18f * a);
            DrawTextureRect(_open, rect, false, tint);
            if (a < 1)
            {
                var door = new Rect2(-67, -47, 122, 59);
                float width = door.Size.X * 0.5f * (1 - a);
                var src = new Rect2(76, 152, 62, 54);
                DrawTextureRectRegion(_closed, new Rect2(door.Position, new Vector2(width, door.Size.Y)),
                    new Rect2(src.Position + new Vector2(src.Size.X * a * 0.5f, 0), new Vector2(src.Size.X * (1 - a) * 0.5f, src.Size.Y)), tint);
                DrawTextureRectRegion(_closed, new Rect2(door.End.X - width, door.Position.Y, width, door.Size.Y),
                    new Rect2(src.Position + new Vector2(src.Size.X * 0.5f, 0), new Vector2(src.Size.X * (1 - a) * 0.5f, src.Size.Y)), tint);
            }
        }

        void Lamp(Vector2 p, Color c, float energy)
        {
            _glow.DrawTextureRect(Art.LightTex, new Rect2(p - Vector2.One * 14, Vector2.One * 28), false, new Color(c, 0.35f * energy));
            _glow.DrawCircle(p, 3, new Color(c * energy, 1));
            if (energy > 0.5f) _glow.DrawCircle(p + new Vector2(-0.6f, -0.6f), 1, new Color(1, 0.95f, 0.8f));
        }

        void DrawGlow()
        {
            if (Aperture > 0)
                _glow.DrawTextureRect(Art.LightTex, new Rect2(-73, -68, 146, 100), false, new Color(Red, Aperture * 0.2f));
            var roof = new Vector2(0, -135);
            var ray = new Vector2(Mathf.Cos(_beacon) * 62, Mathf.Sin(_beacon) * 38);
            var side = new Vector2(-ray.Y, ray.X).Normalized() * 12;
            _glow.DrawPolygon(new[] { roof, roof + ray - side, roof + ray + side }, new[] { new Color(Amber, 0.55f), new Color(Amber, 0.02f), new Color(Amber, 0.02f) });
            Lamp(roof, Amber, 0.8f);
            for (int i = 0; i < Corners.Length; i++)
                Lamp(Corners[i], State == LiftState.Idle ? Amber : Red,
                    State == LiftState.Warning ? ((int)(_elapsed * 4) % 4 == i ? 1 : 0.12f) : 0.2f);
            if (State is LiftState.Boarding or LiftState.Opening or LiftState.ClosingDescend)
            {
                var color = State == LiftState.Boarding ? (Remaining <= 10 ? Red : Remaining <= 60 ? Amber : new Color(0.25f, 1, 0.4f)) : Red;
                float blink = State == LiftState.Boarding && Remaining <= 10 ? (Mathf.Sin(_elapsed * 12) > 0 ? 1 : 0.12f) : 1;
                foreach (var p in Entrances)
                {
                    Lamp(p, color, blink);
                    if (State == LiftState.Boarding)
                    {
                        _glow.DrawRect(new Rect2(p + new Vector2(-12, 6), new Vector2(24, 2)), new Color(0.03f, 0.04f, 0.04f));
                        _glow.DrawRect(new Rect2(p + new Vector2(-12, 6), new Vector2(24 * Remaining / _boarding, 2)), color);
                    }
                }
            }
            if (State == LiftState.ClosingDescend && _slammed)
            {
                float x = -18 + Mathf.PosMod(_elapsed * 85, 36);
                _glow.DrawLine(new Vector2(x, -172), new Vector2(x + 3, -159), new Color(1, 0.7f, 0.4f, 0.65f * (1 - _elapsed / 3)), 2);
            }
        }
    }
}
