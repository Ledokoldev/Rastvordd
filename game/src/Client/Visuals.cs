using Godot;

namespace Rastvordd.Client
{
    /// <summary>Временные визуалы-заглушки. Позже заменятся моделями с анимациями Mixamo.</summary>
    public static class Visuals
    {
        public static StandardMaterial3D Mat(Color c, float rough = 0.85f)
            => new StandardMaterial3D { AlbedoColor = c, Roughness = rough };

        public static StandardMaterial3D Glow(Color c, float energy = 3f) => new StandardMaterial3D
        {
            AlbedoColor = c,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = c,
            EmissionEnergyMultiplier = energy,
        };

        public static MeshInstance3D Box(Vector3 size, Material mat)
            => new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat };

        /// <summary>Угол поворота вокруг Y, при котором -Z узла смотрит вдоль (dx, dz).</summary>
        public static float YawFor(float dx, float dz) => Mathf.Atan2(-dx, -dz);
    }

    public partial class ActorView : Node3D
    {
        readonly Node3D _body;
        readonly MeshInstance3D _gun;
        readonly StandardMaterial3D _mat;
        readonly Color _alive;
        bool _dead;

        public ActorView(bool isNpc, bool isSelf)
        {
            _alive = isSelf ? new Color(0.25f, 0.55f, 0.85f) : isNpc ? new Color(0.62f, 0.55f, 0.32f) : new Color(0.75f, 0.3f, 0.25f);
            _mat = Visuals.Mat(_alive);
            _body = new Node3D();
            AddChild(_body);

            var torso = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.3f, Height = 1.5f }, MaterialOverride = _mat };
            torso.Position = new Vector3(0, 0.75f, 0);
            _body.AddChild(torso);

            var shoulders = Visuals.Box(new Vector3(0.78f, 0.22f, 0.32f), _mat);
            shoulders.Position = new Vector3(0, 1.28f, 0);
            _body.AddChild(shoulders);

            var head = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.19f, Height = 0.38f }, MaterialOverride = Visuals.Mat(new Color(0.85f, 0.7f, 0.58f)) };
            head.Position = new Vector3(0, 1.62f, 0);
            _body.AddChild(head);

            _gun = Visuals.Box(new Vector3(0.09f, 0.1f, 0.75f), Visuals.Mat(new Color(0.12f, 0.12f, 0.13f), 0.4f));
            _gun.Position = new Vector3(0.2f, 1.2f, -0.45f);
            _body.AddChild(_gun);
        }

        public void SetState(Vector3 pos, float yaw, bool dead, bool reloading)
        {
            Position = pos;
            if (dead != _dead)
            {
                _dead = dead;
                _body.RotationDegrees = dead ? new Vector3(-90, 0, 0) : Vector3.Zero;
                _body.Position = dead ? new Vector3(0, 0.3f, 0) : Vector3.Zero;
                _mat.AlbedoColor = dead ? _alive.Darkened(0.55f) : _alive;
                _gun.Visible = !dead;
            }
            if (!dead) Rotation = new Vector3(0, yaw, 0);
            _gun.RotationDegrees = reloading ? new Vector3(-35, 25, 0) : Vector3.Zero;
        }
    }

    public partial class Crosshair : Control
    {
        public float RadiusPx = 10;
        public float HitFlash;
        public float ReloadProgress;

        public override void _Draw()
        {
            var c = HitFlash > 0 ? new Color(1, 0.25f, 0.2f) : new Color(1, 1, 1, 0.9f);
            float r = Mathf.Max(4, RadiusPx);
            DrawArc(Vector2.Zero, r, 0, Mathf.Tau, 48, c, 1.5f, true);
            DrawCircle(Vector2.Zero, 1.6f, c);
            if (HitFlash > 0)
            {
                float k = 6 + r * 0.3f;
                DrawLine(new Vector2(-k, -k), new Vector2(-k * 0.4f, -k * 0.4f), c, 2);
                DrawLine(new Vector2(k, -k), new Vector2(k * 0.4f, -k * 0.4f), c, 2);
                DrawLine(new Vector2(-k, k), new Vector2(-k * 0.4f, k * 0.4f), c, 2);
                DrawLine(new Vector2(k, k), new Vector2(k * 0.4f, k * 0.4f), c, 2);
            }
            if (ReloadProgress > 0)
                DrawArc(Vector2.Zero, r + 6, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * ReloadProgress, 48, new Color(1, 0.85f, 0.3f), 3, true);
        }
    }
}
