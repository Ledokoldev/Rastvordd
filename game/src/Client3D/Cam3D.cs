using Godot;

namespace Rastvordd.Client3D
{
    /// <summary>
    /// Камера в духе Project Zomboid: сверху под углом, узкий угол обзора (почти изометрия, но с глубиной).
    /// Поворот: Z / X — по 45° с плавным доворотом, или зажатая средняя кнопка мыши. Колёсико — зум.
    /// ПКМ (прицеливание) — камера тянется к курсору, как в 2D-версии.
    /// Все числа вынесены в поля, чтобы подкрутить ощущение без перестройки логики.
    /// </summary>
    public partial class Cam3D : Node3D
    {
        public float Pitch = 58f;          // угол наклона, градусы от горизонта
        public float Fov = 32f;            // узкий — ближе к изометрии
        public float Distance = 34f;       // базовая дистанция до точки взгляда, м
        public float MinZoom = 0.55f, MaxZoom = 1.8f;
        public float Follow = 12f;         // скорость догонялки за персонажем
        public float PeekPull = 0.35f;     // доля пути к курсору при прицеливании
        public float RotateStep = 45f;

        public float Yaw;                  // радианы; 0 — камера на юге, смотрит на север (−Y симуляции = верх экрана)
        float _yawTarget;
        float _zoom = 1f, _zoomTarget = 1f;
        Vector3 _focus;
        bool _snapped;
        public Camera3D Cam { get; private set; }
        public float Shake;

        readonly RandomNumberGenerator _rng = new();

        public override void _Ready()
        {
            Cam = new Camera3D { Fov = Fov, Near = 0.5f, Far = 400f, Current = true };
            AddChild(Cam);
        }

        public void RotateBy(int steps) => _yawTarget += Mathf.DegToRad(RotateStep) * steps;
        public void DragRotate(float pixels) { _yawTarget += pixels * 0.006f; Yaw = _yawTarget; }
        public void ZoomBy(float k) => _zoomTarget = Mathf.Clamp(_zoomTarget * k, MinZoom, MaxZoom);

        /// <summary>Вектор «вперёд» (верх экрана) и «вправо» на земле — для перевода WASD в мир.</summary>
        public Vector3 Forward => new Vector3(-Mathf.Sin(Yaw), 0, -Mathf.Cos(Yaw));
        public Vector3 Right => new Vector3(Mathf.Cos(Yaw), 0, -Mathf.Sin(Yaw));

        /// <param name="target">где персонаж (земля под ногами)</param>
        /// <param name="aim">точка прицела на земле</param>
        /// <param name="peek">0..1 — насколько зажат прицел</param>
        public void Step(Vector3 target, Vector3 aim, float peek, float dt)
        {
            Yaw = Mathf.LerpAngle(Yaw, _yawTarget, 1 - Mathf.Exp(-10 * dt));
            _zoom = Mathf.Lerp(_zoom, _zoomTarget * (1f + 0.12f * peek), 1 - Mathf.Exp(-10 * dt));

            var pull = aim - target; pull.Y = 0;
            float maxPull = 14f * _zoom;
            if (pull.Length() > maxPull) pull = pull.Normalized() * maxPull;
            var want = target + pull * PeekPull * peek;
            if (!_snapped) { _focus = want; _snapped = true; }
            _focus = _focus.Lerp(want, 1 - Mathf.Exp(-Follow * dt));

            float p = Mathf.DegToRad(Pitch), d = Distance * _zoom;
            var offset = new Vector3(Mathf.Sin(Yaw) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(Yaw) * Mathf.Cos(p)) * d;
            Shake = Mathf.MoveToward(Shake, 0, dt * 6f);
            var jitter = Shake > 0.01f ? new Vector3(_rng.RandfRange(-1, 1), 0, _rng.RandfRange(-1, 1)) * 0.12f * Shake : Vector3.Zero;

            Cam.GlobalPosition = _focus + offset + jitter;
            Cam.LookAt(_focus + jitter, Vector3.Up);
        }

        /// <summary>Куда на плоскости высотой h попадает луч из камеры через точку экрана.</summary>
        public Vector3? GroundAt(Vector2 screen, float h)
        {
            var o = Cam.ProjectRayOrigin(screen);
            var n = Cam.ProjectRayNormal(screen);
            if (Mathf.Abs(n.Y) < 1e-4f) return null;
            float t = (h - o.Y) / n.Y;
            if (t < 0) return null;
            return o + n * t;
        }
    }
}
