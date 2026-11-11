using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client3D
{
    /// <summary>Пул узлов по id: создаёт новые, прячет пропавшие из снапшота (объекты за 70 м и т. п.).</summary>
    public sealed class Pool3D
    {
        readonly Node3D _parent;
        readonly Dictionary<int, Node3D> _nodes = new();
        readonly HashSet<int> _seen = new();
        public Pool3D(Node3D parent) { _parent = parent; }

        public void Begin() => _seen.Clear();

        /// <summary>Узел для id; <paramref name="key"/> — вид: поменялся (дерево срубили, ящик стал пустым) — узел пересоздаётся.</summary>
        public Node3D Get(int id, string key, System.Func<Node3D> make)
        {
            _seen.Add(id);
            if (_nodes.TryGetValue(id, out var n) && (string)n.GetMeta("k", "") == key) return n;
            if (n != null) { _parent.RemoveChild(n); n.QueueFree(); }
            n = make();
            n.SetMeta("k", key);
            _parent.AddChild(n);
            _nodes[id] = n;
            return n;
        }

        public void End()
        {
            List<int> gone = null;
            foreach (var id in _nodes.Keys) if (!_seen.Contains(id)) (gone ??= new()).Add(id);
            if (gone == null) return;
            foreach (var id in gone) { var n = _nodes[id]; _parent.RemoveChild(n); n.QueueFree(); _nodes.Remove(id); }
        }

        public bool TryGet(int id, out Node3D n) => _nodes.TryGetValue(id, out n);
    }

    /// <summary>
    /// Персонаж из частей (hips, torso, head, arm_l, arm_r, leg_l, leg_r — так их называет scripts/gen-3d-assets.py):
    /// процедурная анимация — шаг по пройденному пути (ходьба/бег), присед, руки держат оружие к прицелу, отдача
    /// (увод ствола RecoilDeg + толчок на выстреле), взмах инструментом, смерть. Модели нет — та же фигура из примитивов.
    /// Оружие крепится в правую кисть (arm_r), рукоятка в точке хвата.
    /// </summary>
    public partial class Actor3D : Node3D
    {
        public Node3D Body;
        Node3D _hips, _torso, _head, _armL, _armR, _legL, _legR, _hand, _gun;
        float _hipsY = 0.88f;
        string _held;
        bool _heldGun;
        Label3D _label;
        float _kick, _phase, _speed, _time;
        string _wornKey;
        readonly List<Node3D> _wear = new();
        /// <summary>Сколько раз стопа коснулась земли за последний Apply — Main3D играет столько шагов.</summary>
        public int Steps;
        Vector3 _lastPos;
        bool _hasLast;
        public static readonly Color Me = new(0.28f, 0.55f, 0.72f), Bot = new(0.72f, 0.48f, 0.28f), Guard = new(0.42f, 0.46f, 0.30f);

        /// <param name="dressed">на персонаже есть одежда — база «голая», вещи надеваются слоями (как риг 2D, D47)</param>
        public static Actor3D Make(bool me, bool npc, bool dressed = false)
        {
            var a = new Actor3D();
            a.Body = new Node3D();
            a.AddChild(a.Body);
            var model = dressed ? Models3D.Naked() ?? Models3D.Character(me, npc) : Models3D.Character(me, npc);
            if (model == null) model = Stub(me ? Me : npc ? Guard : Bot);
            a.Body.AddChild(model);
            a._hips = model.FindChild("hips", true, false) as Node3D;
            a._torso = model.FindChild("torso", true, false) as Node3D;
            a._head = model.FindChild("head", true, false) as Node3D;
            a._armL = model.FindChild("arm_l", true, false) as Node3D;
            a._armR = model.FindChild("arm_r", true, false) as Node3D;
            a._legL = model.FindChild("leg_l", true, false) as Node3D;
            a._legR = model.FindChild("leg_r", true, false) as Node3D;
            if (a._hips != null) a._hipsY = a._hips.Position.Y;
            a._hand = new Node3D { Position = new Vector3(0, -0.56f, 0), Rotation = new Vector3(0, 0, -Mathf.Pi / 2) };
            (a._armR ?? a.Body).AddChild(a._hand);
            return a;
        }

        /// <summary>Фигура из примитивов с теми же суставами, что у модели.</summary>
        static Node3D Stub(Color cloth)
        {
            var c = Mats3D.Solid(cloth, 0.9f);
            var pants = Mats3D.Solid(new Color(0.22f, 0.22f, 0.24f), 0.95f);
            var skin = Mats3D.Solid(new Color(0.85f, 0.70f, 0.58f), 0.8f);
            var root = new Node3D();
            var hips = new Node3D { Name = "hips", Position = new Vector3(0, 0.88f, 0) }; root.AddChild(hips);
            W3.AddBox(hips, Vector3.Zero, new Vector3(0.24f, 0.16f, 0.36f), 0, pants);
            var torso = new Node3D { Name = "torso", Position = new Vector3(0, 0.06f, 0) }; hips.AddChild(torso);
            W3.AddBox(torso, new Vector3(0, 0.27f, 0), new Vector3(0.25f, 0.55f, 0.44f), 0, c);
            var head = new Node3D { Name = "head", Position = new Vector3(0, 0.62f, 0) }; torso.AddChild(head);
            W3.AddBox(head, new Vector3(0, 0.12f, 0), new Vector3(0.21f, 0.24f, 0.2f), 0, skin);
            W3.AddBox(head, new Vector3(0.11f, 0.09f, 0), new Vector3(0.03f, 0.05f, 0.04f), 0, skin);
            foreach (var (side, z) in new[] { ("l", -0.25f), ("r", 0.25f) })
            {
                var arm = new Node3D { Name = "arm_" + side, Position = new Vector3(0, 0.5f, z) }; torso.AddChild(arm);
                W3.AddBox(arm, new Vector3(0, -0.27f, 0), new Vector3(0.12f, 0.54f, 0.12f), 0, c);
                W3.AddBox(arm, new Vector3(0, -0.56f, 0), new Vector3(0.09f, 0.1f, 0.08f), 0, skin);
                var leg = new Node3D { Name = "leg_" + side, Position = new Vector3(0, -0.04f, z * 0.44f) }; hips.AddChild(leg);
                W3.AddBox(leg, new Vector3(0, -0.4f, 0), new Vector3(0.15f, 0.8f, 0.15f), 0, pants);
                W3.AddBox(leg, new Vector3(0.04f, -0.79f, 0), new Vector3(0.26f, 0.1f, 0.15f), 0, Mats3D.Solid(new Color(0.17f, 0.14f, 0.12f), 0.9f));
            }
            return root;
        }

        /// <summary>Что в руках: модель held_&lt;id&gt;, иначе заглушка ствола или инструмента.</summary>
        void SetHeld(string id)
        {
            if (id == _held) return;
            _held = id;
            if (_gun != null) { _hand.RemoveChild(_gun); _gun.QueueFree(); _gun = null; }
            if (string.IsNullOrEmpty(id)) { _heldGun = false; return; }
            var def = Items.Get(id);
            _heldGun = def?.Weapon != null && !(def.Weapon.Melee);
            _gun = Models3D.Held(id);
            if (_gun == null)
            {
                _gun = new Node3D();
                if (_heldGun)
                {
                    var metal = Mats3D.Solid(new Color(0.18f, 0.18f, 0.19f), 0.5f, 0.6f);
                    var wood = Mats3D.Solid(new Color(0.45f, 0.30f, 0.18f), 0.8f);
                    W3.AddBox(_gun, new Vector3(0.35f, 0.02f, 0), new Vector3(0.62f, 0.07f, 0.06f), 0, metal);
                    W3.AddBox(_gun, new Vector3(-0.15f, -0.02f, 0), new Vector3(0.3f, 0.1f, 0.05f), 0, wood);
                    W3.AddBox(_gun, new Vector3(0.1f, -0.1f, 0), new Vector3(0.06f, 0.14f, 0.04f), 0, metal);
                }
                else W3.AddBox(_gun, new Vector3(0.25f, 0, 0), new Vector3(0.55f, 0.05f, 0.05f), 0, Mats3D.Solid(new Color(0.5f, 0.4f, 0.3f), 0.8f));
            }
            _hand.AddChild(_gun);
        }

        /// <summary>
        /// Одежда слоями поверх тела по слотам WearSlot: шапка/шлем, маска, кофта с рукавами, броня, наручи, перчатки,
        /// штаны, наколенники, обувь. Цвет — основной цвет вещи из 2D (WearLook), иначе по названию.
        /// </summary>
        void SetWorn(string[] worn)
        {
            string key = worn == null ? "" : string.Join("|", worn);
            if (key == _wornKey) return;
            _wornKey = key;
            foreach (var n in _wear) { n.GetParent()?.RemoveChild(n); n.QueueFree(); }
            _wear.Clear();
            if (worn == null) return;
            for (int slot = 0; slot < worn.Length; slot++)
            {
                var id = worn[slot];
                if (string.IsNullOrEmpty(id)) continue;
                var c = Client.WearLook.Tint3D(id) ?? Guess(id);
                bool hard = id.Contains("helmet") || id.Contains("ssh68") || id.Contains("sfera") || id.Contains("pot") || id.Contains("6b2") || id.Contains("kora") || id.Contains("chestplate");
                var mat = Mats3D.Solid(c, hard ? 0.5f : 0.92f, hard ? 0.35f : 0f);
                switch ((WearSlot)slot)
                {
                    case WearSlot.Head:
                        Shell(_head, new Vector3(-0.01f, 0.25f, 0), new Vector3(0.25f, hard ? 0.12f : 0.09f, 0.24f), mat);
                        if (hard) Shell(_head, new Vector3(-0.02f, 0.19f, 0), new Vector3(0.24f, 0.07f, 0.26f), mat);
                        else if (id.Contains("ushanka") || id.Contains("hood")) { Shell(_head, new Vector3(-0.02f, 0.1f, -0.11f), new Vector3(0.2f, 0.16f, 0.03f), mat); Shell(_head, new Vector3(-0.02f, 0.1f, 0.11f), new Vector3(0.2f, 0.16f, 0.03f), mat); }
                        break;
                    case WearSlot.Face:
                        Shell(_head, new Vector3(0.1f, 0.08f, 0), new Vector3(0.04f, 0.15f, 0.19f), mat);
                        if (id.Contains("gp5") || id.Contains("r2")) Shell(_head, new Vector3(0.13f, 0.05f, 0), new Vector3(0.06f, 0.07f, 0.07f), Mats3D.Solid(new Color(0.15f, 0.15f, 0.15f), 0.6f));
                        break;
                    case WearSlot.Kofta:
                        Shell(_torso, new Vector3(0, 0.27f, 0), new Vector3(0.28f, 0.56f, 0.48f), mat);
                        Shell(_armL, new Vector3(0, -0.2f, 0), new Vector3(0.14f, 0.4f, 0.14f), mat);
                        Shell(_armR, new Vector3(0, -0.2f, 0), new Vector3(0.14f, 0.4f, 0.14f), mat);
                        if (id.Contains("robe") || id.Contains("cape") || id.Contains("bushlat") || id.Contains("telogreika"))
                            Shell(_hips, new Vector3(0, -0.08f, 0), new Vector3(0.27f, 0.22f, 0.4f), mat);
                        break;
                    case WearSlot.Armor:
                        Shell(_torso, new Vector3(0.005f, 0.3f, 0), new Vector3(0.31f, 0.38f, 0.5f), mat);
                        if (id.Contains("signvest")) Shell(_torso, new Vector3(0.16f, 0.32f, 0), new Vector3(0.01f, 0.05f, 0.46f), Mats3D.Solid(new Color(0.9f, 0.9f, 0.88f), 0.6f));
                        break;
                    case WearSlot.Arms:
                        Shell(_armL, new Vector3(0, -0.4f, 0), new Vector3(0.135f, 0.2f, 0.135f), mat);
                        Shell(_armR, new Vector3(0, -0.4f, 0), new Vector3(0.135f, 0.2f, 0.135f), mat);
                        break;
                    case WearSlot.Gloves:
                        Shell(_armL, new Vector3(0, -0.56f, 0), new Vector3(0.1f, 0.11f, 0.095f), mat);
                        Shell(_armR, new Vector3(0, -0.56f, 0), new Vector3(0.1f, 0.11f, 0.095f), mat);
                        break;
                    case WearSlot.Legs:
                        Shell(_hips, Vector3.Zero, new Vector3(0.26f, 0.18f, 0.38f), mat);
                        Shell(_legL, new Vector3(0, -0.37f, 0), new Vector3(0.17f, 0.7f, 0.17f), mat);
                        Shell(_legR, new Vector3(0, -0.37f, 0), new Vector3(0.17f, 0.7f, 0.17f), mat);
                        break;
                    case WearSlot.Knees:
                        Shell(_legL, new Vector3(0.07f, -0.42f, 0), new Vector3(0.06f, 0.13f, 0.13f), mat);
                        Shell(_legR, new Vector3(0.07f, -0.42f, 0), new Vector3(0.06f, 0.13f, 0.13f), mat);
                        break;
                    case WearSlot.Feet:
                        Shell(_legL, new Vector3(0.04f, -0.78f, 0), new Vector3(0.28f, id.Contains("valenki") || id.Contains("stockings") ? 0.3f : 0.13f, 0.17f), mat);
                        Shell(_legR, new Vector3(0.04f, -0.78f, 0), new Vector3(0.28f, id.Contains("valenki") || id.Contains("stockings") ? 0.3f : 0.13f, 0.17f), mat);
                        break;
                }
            }
        }

        void Shell(Node3D part, Vector3 at, Vector3 size, Material mat)
        {
            if (part == null) return;
            _wear.Add(W3.AddBox(part, at, size, 0, mat));
        }

        static Color Guess(string id) =>
            id.Contains("army") || id.Contains("ozk") ? new Color(0.33f, 0.38f, 0.24f)
            : id.Contains("sobr") || id.Contains("spec") ? new Color(0.16f, 0.17f, 0.2f)
            : id.Contains("leather") ? new Color(0.43f, 0.29f, 0.17f)
            : id.Contains("scrap") ? new Color(0.45f, 0.4f, 0.35f)
            : new Color(0.45f, 0.42f, 0.36f);

        public void Kick() => _kick = 1f;

        static void Rot(Node3D n, float x, float y, float z) { if (n != null) n.Rotation = new Vector3(x, y, z); }

        public void Apply(ActorSnap a, SVec pos, float facing, float dt, bool showLabel)
        {
            var p3 = W3.V(pos, a.Swimming ? -0.95f : 0f);
            // скорость — по пройденному пути (симуляция шлёт только позицию)
            if (_hasLast && dt > 0)
            {
                float v = new Vector2(p3.X - _lastPos.X, p3.Z - _lastPos.Z).Length() / dt;
                if (v > 20f) v = 0;   // телепорт/респавн
                _speed = Mathf.Lerp(_speed, v, 1 - Mathf.Exp(-12f * dt));
            }
            _lastPos = p3; _hasLast = true;
            Position = p3;
            Rotation = new Vector3(0, W3.YawOfAngle(facing), 0);
            SetHeld(a.Dead ? null : a.HeldId);
            SetWorn(a.Worn);
            _kick = Mathf.MoveToward(_kick, 0, dt * 8f);
            _time += dt;
            Steps = 0;

            if (a.Dead)
            {
                Body.Rotation = new Vector3(0, 0, Mathf.Pi / 2);
                Body.Position = new Vector3(-0.1f, 0.15f, 0);
                Rot(_legL, 0, 0, 0.1f); Rot(_legR, 0, 0, -0.15f);
                Rot(_armL, 0.3f, 0, 0.4f); Rot(_armR, -0.3f, 0, 0.8f);
                if (_hips != null) _hips.Position = new Vector3(0, _hipsY, 0);
                UpdateLabel(a, false);
                return;
            }
            Body.Rotation = Vector3.Zero;
            Body.Position = Vector3.Zero;

            // шаг: фаза по пройденному пути, размах — по скорости (бег шире)
            float amp = Mathf.Clamp(_speed / 3.5f, 0, 1) * (a.Sprinting ? 0.75f : 0.55f);
            float before = _phase;
            _phase += _speed * dt * (a.Sprinting ? 2.6f : 3.0f);
            // касание стопы — каждые пол-оборота фазы (левая, правая)
            if (_speed > 0.4f) Steps = Mathf.FloorToInt(_phase / Mathf.Pi) - Mathf.FloorToInt(before / Mathf.Pi);
            float sw = Mathf.Sin(_phase) * amp;
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.045f * amp / 0.55f;

            float crouch = a.Crouching ? 1f : 0f;
            if (_hips != null) _hips.Position = new Vector3(0, _hipsY - 0.3f * crouch + bob, 0);
            if (crouch > 0)
            {
                Rot(_legL, 0, 0, 0.75f + sw * 0.4f);
                Rot(_legR, 0, 0, 0.75f - sw * 0.4f);
                Rot(_torso, 0, 0, -0.25f);
            }
            else
            {
                Rot(_legL, 0, 0, sw);
                Rot(_legR, 0, 0, -sw);
                Rot(_torso, 0, 0, a.Sprinting ? -0.12f * amp : 0);
            }

            float recoil = -Mathf.DegToRad(a.RecoilDeg);
            if (_heldGun)
            {
                // оружие к прицелу: обе руки вперёд, прицел (Peek) — выше к плечу, толчок на выстреле
                float raise = Mathf.Pi / 2 - 0.08f + 0.12f * a.Peek + 0.14f * _kick;
                if (a.Reloading)
                {
                    // перезарядка: ствол опущен и завален набок, левая рука ходит к магазину
                    Rot(_armR, 0, 0.2f, raise - 0.45f);
                    Rot(_armL, 0, -0.9f, raise - 0.6f + Mathf.Sin(_time * 9f) * 0.25f);
                    if (_hand != null) _hand.Rotation = new Vector3(0.6f, 0, -Mathf.Pi / 2);
                }
                else
                {
                    Rot(_armR, 0, recoil, raise);
                    Rot(_armL, 0, -0.55f + recoil, raise - 0.1f);
                    if (_hand != null) _hand.Rotation = new Vector3(0, 0, -Mathf.Pi / 2);
                }
                Rot(_head, 0, 0, 0.05f * a.Peek);
            }
            else if (!string.IsNullOrEmpty(_held))
            {
                // инструмент/нож: правая рука чуть вперёд, на ударе — взмах сверху вниз
                float swing = _kick > 0 ? Mathf.Sin((1 - _kick) * Mathf.Pi) * 1.6f : 0;
                Rot(_armR, 0, 0, 0.5f + swing - sw * 0.3f);
                Rot(_armL, 0, 0, sw * 0.8f);
                Rot(_head, 0, 0, 0);
            }
            else
            {
                Rot(_armR, 0, 0, sw * 0.8f);
                Rot(_armL, 0, 0, -sw * 0.8f);
                Rot(_head, 0, 0, 0);
            }
            UpdateLabel(a, showLabel);
        }

        void UpdateLabel(ActorSnap a, bool showLabel)
        {
            bool want = showLabel && !string.IsNullOrEmpty(a.Label) && !a.Dead;
            if (want && _label == null)
            {
                _label = new Label3D
                {
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true,
                    PixelSize = 0.01f,
                    FontSize = 34,
                    OutlineSize = 8,
                    Modulate = new Color(1, 1, 1, 0.9f),
                    Position = new Vector3(0, 2.25f, 0),
                };
                AddChild(_label);
            }
            if (_label != null)
            {
                _label.Visible = want;
                if (want && _label.Text != a.Label) _label.Text = a.Label;
            }
        }
    }

    /// <summary>Ресурсы, установленное, ящики, пикапы — заглушки по виду; настоящие модели подхватывает Models3D.</summary>
    public static class Props3D
    {
        public static Node3D NodeView(NodeSnap n, Biome biome)
        {
            var root = new Node3D();
            var model = Models3D.ForNode(n.Kind, n.Look, biome);
            float k = 0.6f + 0.4f * Mathf.Clamp(n.Fill, 0, 1);
            if (model != null)
            {
                // ресурс тает по мере добычи (Fill), поворот — по Look, чтобы одинаковые модели не стояли «под копирку»
                model.Scale = Vector3.One * (n.Kind == NodeKind.Tree ? 0.85f + 0.3f * (n.Look % 7) / 6f : k);
                model.Rotation = new Vector3(0, n.Look * 0.9f, 0);
                root.AddChild(model);
                return root;
            }
            float r = Mathf.Max(0.25f, n.Radius);
            switch (n.Kind)
            {
                case NodeKind.Tree:
                {
                    var bark = Mats3D.Solid(new Color(0.36f, 0.27f, 0.20f), 0.95f);
                    bool pine = n.Look % 2 == 0;
                    var leaf = Mats3D.Cut(pine ? new Color(0.20f, 0.33f, 0.22f) : new Color(0.30f, 0.45f, 0.22f), 0.95f, 0f, 1.6f);
                    float h = 3.2f + (n.Look % 5) * 0.5f;
                    W3.AddMesh(root, W3.Cylinder, new Vector3(0, h * 0.5f, 0), new Vector3(r * 1.1f, h, r * 1.1f), 0, bark);
                    if (pine)
                    {
                        W3.AddMesh(root, W3.Cone, new Vector3(0, h * 0.55f + 1.4f, 0), new Vector3(3.2f, 3.6f, 3.2f), 0, leaf);
                        W3.AddMesh(root, W3.Cone, new Vector3(0, h * 0.55f + 3.0f, 0), new Vector3(2.3f, 2.8f, 2.3f), 0, leaf);
                    }
                    else W3.AddMesh(root, W3.Sphere, new Vector3(0, h + 0.9f, 0), new Vector3(3.8f, 3.0f, 3.8f), 0, leaf);
                    break;
                }
                case NodeKind.Stone:
                case NodeKind.Metal:
                case NodeKind.Sulfur:
                {
                    var c = n.Kind switch { NodeKind.Metal => new Color(0.50f, 0.36f, 0.28f), NodeKind.Sulfur => new Color(0.78f, 0.70f, 0.30f), _ => new Color(0.52f, 0.52f, 0.50f) };
                    var rock = Mats3D.Solid(new Color(0.46f, 0.45f, 0.43f), 0.95f);
                    W3.AddMesh(root, W3.Sphere, new Vector3(0, r * 0.45f * k, 0), new Vector3(r * 2.2f, r * 1.4f, r * 2f) * k, n.Look * 0.7f, rock);
                    W3.AddMesh(root, W3.Sphere, new Vector3(r * 0.3f, r * 0.7f * k, r * 0.1f), new Vector3(r * 1.1f, r * 0.8f, r * 1.1f) * k, 0, Mats3D.Solid(c, 0.7f, n.Kind == NodeKind.Metal ? 0.4f : 0f));
                    break;
                }
                case NodeKind.Barrel:
                    W3.AddMesh(root, W3.Cylinder, new Vector3(0, 0.45f, 0), new Vector3(0.6f, 0.9f, 0.6f), 0, Mats3D.Solid(new Color(0.30f, 0.38f, 0.48f), 0.6f, 0.3f));
                    break;
                case NodeKind.Junk:
                    W3.AddMesh(root, W3.Sphere, new Vector3(0, 0.15f, 0), new Vector3(r * 2.2f, 0.6f, r * 2f), n.Look, Mats3D.Solid(new Color(0.42f, 0.36f, 0.30f), 0.9f, 0.2f));
                    W3.AddBox(root, new Vector3(0.2f, 0.35f, -0.1f), new Vector3(0.5f, 0.3f, 0.4f), 0.6f, Mats3D.Solid(new Color(0.45f, 0.30f, 0.22f), 0.8f, 0.3f));
                    break;
                case NodeKind.Tire:
                case NodeKind.TireStack:
                {
                    var rubber = Mats3D.Solid(new Color(0.12f, 0.12f, 0.12f), 0.9f);
                    int count = n.Kind == NodeKind.TireStack ? 3 : 1;
                    for (int i = 0; i < count; i++)
                        W3.AddMesh(root, new TorusMesh { InnerRadius = 0.22f, OuterRadius = 0.42f }, new Vector3(0, 0.1f + i * 0.2f, 0), Vector3.One, i * 0.4f, rubber);
                    break;
                }
            }
            return root;
        }

        static (float h, Color c, bool tall) DeployLook(DeployKind k) => k switch
        {
            DeployKind.Box => (0.6f, new Color(0.50f, 0.36f, 0.22f), false),
            DeployKind.BoxLarge => (0.8f, new Color(0.46f, 0.33f, 0.20f), false),
            DeployKind.Workbench => (0.95f, new Color(0.40f, 0.42f, 0.38f), false),
            DeployKind.Furnace => (1.3f, new Color(0.30f, 0.29f, 0.28f), true),
            DeployKind.EFurnace => (1.4f, new Color(0.35f, 0.36f, 0.40f), true),
            DeployKind.Claim => (1.8f, new Color(0.44f, 0.34f, 0.24f), true),
            DeployKind.SleepBag => (0.12f, new Color(0.55f, 0.20f, 0.18f), false),
            DeployKind.Recycler => (1.7f, new Color(0.30f, 0.45f, 0.32f), true),
            DeployKind.Lathe => (1.25f, new Color(0.38f, 0.44f, 0.50f), true),
            DeployKind.Generator => (1.0f, new Color(0.70f, 0.58f, 0.20f), false),
            DeployKind.Campfire => (0.25f, new Color(0.35f, 0.27f, 0.20f), false),
            DeployKind.Lamp => (1.6f, new Color(0.30f, 0.30f, 0.30f), true),
            DeployKind.Floodlight => (2.2f, new Color(0.30f, 0.30f, 0.32f), true),
            DeployKind.Barrel => (1.1f, new Color(0.30f, 0.38f, 0.30f), false),
            DeployKind.Drill => (2.6f, new Color(0.55f, 0.45f, 0.20f), true),
            DeployKind.Still or DeployKind.Rectifier => (1.8f, new Color(0.55f, 0.45f, 0.35f), true),
            _ => (0.9f, new Color(0.45f, 0.45f, 0.42f), false),
        };

        public static Node3D Deploy(DeploySnap d)
        {
            var root = new Node3D();
            var model = Models3D.ForDeploy(d.Kind);
            if (model != null)
            {
                // поворот по Rot (четверти), размер — под хитбокс: модель внутри повёрнутого узла подгоняется под неповёрнутый след
                var pivot = new Node3D { Rotation = new Vector3(0, -d.Rot * Mathf.Pi / 2, 0) };
                root.AddChild(pivot);
                pivot.AddChild(model);
                var half = d.Half == default ? new SVec(0.4f, 0.4f) : d.Half;
                var foot = d.Rot % 2 != 0 ? new SVec(half.Y * 2, half.X * 2) : new SVec(half.X * 2, half.Y * 2);
                // по высоте — пропорционально (у модели своя высота: печь выше ящика)
                Models3D.Fit(model, new Vector3(foot.X, 1f, foot.Y), true);
                if (!d.Seen) Dim(model);
            }
            else
            {
                var (h, c, tall) = DeployLook(d.Kind);
                if (!d.Seen) c = c.Darkened(0.45f);
                var half = d.Half == default ? new SVec(0.4f, 0.4f) : d.Half;
                Material mat = tall ? Mats3D.Cut(c, 0.8f, 0.15f, 1.0f) : Mats3D.Solid(c, 0.8f, 0.1f);
                W3.AddBox(root, new Vector3(0, h * 0.5f, 0), new Vector3(half.X * 2, h, half.Y * 2), 0, mat);
                if (d.Kind == DeployKind.Workbench)   // столешница
                    W3.AddBox(root, new Vector3(0, h + 0.03f, 0), new Vector3(half.X * 2 + 0.1f, 0.06f, half.Y * 2 + 0.1f), 0, Mats3D.Solid(new Color(0.55f, 0.40f, 0.25f), 0.8f));
                if (d.Kind == DeployKind.Box || d.Kind == DeployKind.BoxLarge)   // крышка
                    W3.AddBox(root, new Vector3(0, h + 0.02f, 0), new Vector3(half.X * 2 + 0.04f, 0.05f, half.Y * 2 + 0.04f), 0, Mats3D.Solid(c.Darkened(0.2f), 0.8f));
            }
            bool light = d.Burning;   // печь, костёр — горят; лампа, прожектор — включены
            if (light)
            {
                float lh = d.Kind is DeployKind.Floodlight ? 2.3f : d.Kind is DeployKind.Lamp ? 1.7f : 0.6f;
                var fire = d.Kind is DeployKind.Lamp or DeployKind.Floodlight ? new Color(1f, 0.92f, 0.75f) : new Color(1f, 0.55f, 0.2f);
                W3.AddMesh(root, W3.Sphere, new Vector3(0, lh, 0), Vector3.One * 0.25f, 0, Mats3D.Glow(fire, 3f), false);
                root.AddChild(new OmniLight3D { Position = new Vector3(0, lh + 0.3f, 0), LightColor = fire, LightEnergy = 1.6f, OmniRange = d.Kind is DeployKind.Floodlight ? 16f : 7f, ShadowEnabled = false });
            }
            return root;
        }

        public static Node3D Bag(BagSnap b)
        {
            var root = new Node3D();
            var model = b.IsCrate ? Models3D.Crate(b.Tier, b.Safe, b.Look ?? b.Box) : Models3D.Bag();
            if (model != null)
            {
                model.Rotation = new Vector3(0, (b.Id % 4) * Mathf.Pi / 2 + (b.IsCrate ? 0 : b.Id * 0.7f), 0);
                root.AddChild(model);
                if (b.IsCrate && !b.Empty) W3.AddBox(root, new Vector3(0, 0.64f, 0), new Vector3(0.3f, 0.03f, 0.3f), 0, Mats3D.Glow(new Color(0.95f, 0.8f, 0.35f), 1.2f), false);
                if (b.Empty) Dim(model);
                return root;
            }
            if (b.IsCrate)
            {
                var c = b.Safe ? new Color(0.30f, 0.33f, 0.36f) : b.Tier >= 2 ? new Color(0.25f, 0.35f, 0.45f) : b.Tier == 1 ? new Color(0.35f, 0.42f, 0.30f) : new Color(0.50f, 0.38f, 0.24f);
                if (b.Empty) c = c.Darkened(0.4f);
                W3.AddBox(root, new Vector3(0, 0.3f, 0), new Vector3(0.9f, 0.6f, 0.6f), 0, Mats3D.Solid(c, 0.75f, b.Safe ? 0.5f : 0.1f));
                if (!b.Empty) W3.AddBox(root, new Vector3(0, 0.61f, 0), new Vector3(0.92f, 0.04f, 0.1f), 0, Mats3D.Glow(new Color(0.95f, 0.8f, 0.35f), 1.2f));   // есть что взять
            }
            else
            {
                var c = b.Empty ? new Color(0.25f, 0.23f, 0.21f) : new Color(0.38f, 0.30f, 0.22f);
                W3.AddMesh(root, W3.Sphere, new Vector3(0, 0.18f, 0), new Vector3(0.7f, 0.36f, 0.5f), 0, Mats3D.Solid(c, 0.95f));
            }
            return root;
        }

        public static Node3D Pickup(PickupSnap p)
        {
            var root = new Node3D();
            var model = Models3D.Pickup(p.Kind);
            if (model != null) { model.Rotation = new Vector3(0, p.Id * 1.3f, 0); root.AddChild(model); return root; }
            switch (p.Kind)
            {
                case PickupKind.Hemp:
                    W3.AddMesh(root, W3.Cone, new Vector3(0, 0.45f, 0), new Vector3(0.5f, 0.9f, 0.5f), 0, Mats3D.Solid(new Color(0.35f, 0.55f, 0.25f), 0.9f));
                    break;
                case PickupKind.Berries:
                    W3.AddMesh(root, W3.Sphere, new Vector3(0, 0.35f, 0), new Vector3(0.9f, 0.7f, 0.9f), 0, Mats3D.Solid(new Color(0.25f, 0.42f, 0.22f), 0.9f));
                    W3.AddMesh(root, W3.Sphere, new Vector3(0.2f, 0.55f, 0.15f), Vector3.One * 0.12f, 0, Mats3D.Solid(new Color(0.6f, 0.12f, 0.2f), 0.5f));
                    break;
                case PickupKind.WaterBarrel:
                case PickupKind.OilBarrel:
                    W3.AddMesh(root, W3.Cylinder, new Vector3(0, 0.45f, 0), new Vector3(0.6f, 0.9f, 0.6f), 0, Mats3D.Solid(p.Kind == PickupKind.WaterBarrel ? new Color(0.25f, 0.40f, 0.55f) : new Color(0.12f, 0.12f, 0.12f), 0.6f, 0.3f));
                    break;
                case PickupKind.CanisterPile:
                    for (int i = 0; i < 3; i++) W3.AddBox(root, new Vector3(i * 0.3f - 0.3f, 0.18f, (i % 2) * 0.2f), new Vector3(0.25f, 0.36f, 0.15f), i * 0.5f, Mats3D.Solid(new Color(0.55f, 0.18f, 0.14f), 0.6f, 0.3f));
                    break;
                default:
                    W3.AddBox(root, new Vector3(0, 0.12f, 0), new Vector3(0.25f, 0.06f, 0.18f), 0.4f, Mats3D.Glow(new Color(0.95f, 0.85f, 0.4f), 1.5f));
                    break;
            }
            return root;
        }

        /// <summary>Затемнить модель (вне обзора / пустой ящик): поверх материалов — тёмная накладка.</summary>
        static void Dim(Node3D n)
        {
            var shade = new StandardMaterial3D { AlbedoColor = new Color(0, 0, 0, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
            foreach (var c in n.FindChildren("*", "MeshInstance3D", true, false))
                if (c is MeshInstance3D mi) mi.MaterialOverlay = shade;
        }

        public static Node3D Tracer(bool arrow)
        {
            var root = new Node3D();
            if (arrow) W3.AddBox(root, Vector3.Zero, new Vector3(0.7f, 0.03f, 0.03f), 0, Mats3D.Solid(new Color(0.5f, 0.38f, 0.22f), 0.8f), false);
            else W3.AddBox(root, Vector3.Zero, new Vector3(1.1f, 0.035f, 0.035f), 0, Mats3D.Glow(new Color(1f, 0.85f, 0.45f), 4f), false);
            return root;
        }

        public static Node3D Thrown(string item)
        {
            var root = new Node3D();
            bool c4 = item != null && item.Contains("c4");
            if (c4) W3.AddBox(root, Vector3.Zero, new Vector3(0.25f, 0.12f, 0.18f), 0, Mats3D.Solid(new Color(0.35f, 0.38f, 0.30f), 0.7f), false);
            else W3.AddMesh(root, W3.Sphere, Vector3.Zero, Vector3.One * 0.18f, 0, Mats3D.Solid(new Color(0.30f, 0.33f, 0.26f), 0.6f, 0.3f), false);
            return root;
        }

        public static Node3D Fire(float radius)
        {
            var root = new Node3D();
            W3.AddMesh(root, W3.Cylinder, new Vector3(0, 0.05f, 0), new Vector3(radius * 2, 0.08f, radius * 2), 0, Mats3D.Glow(new Color(1f, 0.45f, 0.15f, 0.75f), 2.5f, true), false);
            root.AddChild(new OmniLight3D { Position = new Vector3(0, 1f, 0), LightColor = new Color(1f, 0.55f, 0.2f), LightEnergy = 2f, OmniRange = radius * 3 + 3, ShadowEnabled = false });
            return root;
        }
    }

    /// <summary>Короткоживущие эффекты: вспышка у дула, попадание, взрыв, дым пыли при сносе.</summary>
    public partial class Fx3D : Node3D
    {
        readonly List<(Node3D node, float t, float life, float grow)> _live = new();

        void Spawn(Vector3 at, float size, Color c, float energy, float life, float grow, float lightRange)
        {
            var root = new Node3D { Position = at };
            W3.AddMesh(root, W3.Sphere, Vector3.Zero, Vector3.One * size, 0, Mats3D.Glow(c, energy, true), false);
            if (lightRange > 0) root.AddChild(new OmniLight3D { LightColor = c, LightEnergy = energy, OmniRange = lightRange, ShadowEnabled = false });
            AddChild(root);
            _live.Add((root, 0, life, grow));
        }

        // ---------- дым: мягкие клубы-билборды (поднимаются, растут, тают, сносятся ветром) ----------
        readonly List<(MeshInstance3D m, StandardMaterial3D mat, Vector3 vel, float t, float life, float size, float alpha)> _puffs = new();
        static Texture2D _soft;
        static QuadMesh _quad;

        static Texture2D Soft()
        {
            if (_soft != null) return _soft;
            var g = new Gradient();
            g.SetColor(0, new Color(1, 1, 1, 1));
            g.SetColor(1, new Color(1, 1, 1, 0));
            _soft = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 64, Height = 64 };
            return _soft;
        }

        /// <summary>Клуб дыма/пара: цвет (с прозрачностью), размер, время жизни, скорость подъёма, снос (ветер).</summary>
        public void Puff(Vector3 at, Color c, float size, float life, float rise, Vector3 drift)
        {
            if (_puffs.Count > 260) return;
            _quad ??= new QuadMesh { Size = Vector2.One };
            var mat = new StandardMaterial3D
            {
                AlbedoTexture = Soft(),
                AlbedoColor = c,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            var m = new MeshInstance3D { Mesh = _quad, MaterialOverride = mat, Position = at, Scale = Vector3.One * size, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(m);
            var jitter = new Vector3((float)GD.RandRange(-0.15, 0.15), 0, (float)GD.RandRange(-0.15, 0.15));
            _puffs.Add((m, mat, new Vector3(0, rise, 0) + drift + jitter, 0, life, size, c.A));
        }

        public void Muzzle(Vector3 at) => Spawn(at, 0.35f, new Color(1f, 0.8f, 0.4f, 0.9f), 4f, 0.06f, 0.5f, 9f);
        public void Hit(Vector3 at, bool flesh) => Spawn(at, 0.18f, flesh ? new Color(0.7f, 0.1f, 0.1f, 0.9f) : new Color(1f, 0.9f, 0.6f, 0.9f), 2f, 0.12f, 1.5f, 0);
        public void Explosion(Vector3 at, bool big) => Spawn(at + Vector3.Up * 0.6f, big ? 1.8f : 1.1f, new Color(1f, 0.6f, 0.25f, 0.9f), 5f, big ? 0.55f : 0.4f, big ? 7f : 5f, big ? 30f : 18f);
        public void Dust(Vector3 at) => Spawn(at + Vector3.Up * 0.6f, 1.2f, new Color(0.7f, 0.65f, 0.55f, 0.5f), 0.6f, 0.5f, 3f, 0);

        public override void _Process(double delta)
        {
            float dtp = (float)delta;
            for (int i = _puffs.Count - 1; i >= 0; i--)
            {
                var p = _puffs[i];
                p.t += dtp;
                if (p.t >= p.life) { RemoveChild(p.m); p.m.QueueFree(); _puffs.RemoveAt(i); continue; }
                float k = p.t / p.life;
                p.m.Position += p.vel * dtp;
                p.m.Scale = Vector3.One * p.size * (1f + 1.8f * k);
                var c = p.mat.AlbedoColor;
                p.mat.AlbedoColor = new Color(c.R, c.G, c.B, p.alpha * (k < 0.15f ? k / 0.15f : 1f - (k - 0.15f) / 0.85f));
                _puffs[i] = p;
            }
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var (n, t, life, grow) = _live[i];
                t += (float)delta;
                if (t >= life) { RemoveChild(n); n.QueueFree(); _live.RemoveAt(i); continue; }
                float k = t / life;
                n.Scale = Vector3.One * (1f + grow * k);
                foreach (var c in n.GetChildren()) if (c is OmniLight3D l) l.LightEnergy *= 0.85f;
                _live[i] = (n, t, life, grow);
            }
        }
    }
}
