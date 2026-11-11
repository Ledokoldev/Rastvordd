using System.Collections.Generic;
using Godot;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client3D
{
    /// <summary>
    /// Общие меши, материалы и перевод координат для 3D-клиента.
    /// Симуляция живёт на плоскости (X, Y); в 3D это пол: X → X, Y → Z, высота — Y.
    /// </summary>
    public static class W3
    {
        public static Vector3 V(SVec p, float h = 0f) => new Vector3(p.X, h, p.Y);
        public static SVec S(Vector3 v) => new SVec(v.X, v.Z);
        /// <summary>Поворот вокруг Y, чтобы локальная ось +X смотрела вдоль направления (dx, dy) симуляции.</summary>
        public static float Yaw(float dx, float dy) => -Mathf.Atan2(dy, dx);
        public static float YawOfAngle(float simAngle) => -simAngle;

        public static readonly BoxMesh Box = new BoxMesh { Size = Vector3.One };
        public static readonly SphereMesh Sphere = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 16, Rings = 8 };
        public static readonly CylinderMesh Cylinder = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 12, Rings = 1 };
        public static readonly CylinderMesh Cone = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 10, Rings = 1 };

        /// <summary>Коробка единичного меша: центр, размер и поворот вокруг Y.</summary>
        public static MeshInstance3D AddMesh(Node3D parent, Mesh mesh, Vector3 center, Vector3 size, float yaw, Material mat, bool shadows = true)
        {
            var m = new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = mat,
                CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            };
            m.Transform = new Transform3D(new Basis(Vector3.Up, yaw) * Basis.FromScale(size), center);
            parent.AddChild(m);
            return m;
        }

        public static MeshInstance3D AddBox(Node3D parent, Vector3 center, Vector3 size, float yaw, Material mat, bool shadows = true)
            => AddMesh(parent, Box, center, size, yaw, mat, shadows);

        public static void ClearChildren(Node n)
        {
            foreach (var c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); }
        }
    }

    /// <summary>
    /// Материалы. «Срезающий» шейдер — как в Project Zomboid: всё, что выше пояса и стоит между камерой
    /// и персонажем, вырезается кругом с мягким краем, чтобы стены, крыши и кроны не закрывали игрока.
    /// </summary>
    public static class Mats3D
    {
        const string CutShaderCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform vec4 albedo : source_color = vec4(1.0);
uniform float roughness = 0.9;
uniform float metallic = 0.0;
uniform vec3 cut_center = vec3(0.0, -1000.0, 0.0);   // точка у персонажа (м)
uniform vec3 cam_pos = vec3(0.0, 100.0, 0.0);         // позиция камеры — с C#, чтобы тени резались так же
uniform float cut_radius = 0.0;                        // 0 — выключено
uniform float cut_keep = 0.6;                          // ниже этой высоты не режем: видно, где стоит стена
// текстура по мировым координатам (трипланар): кирпич одного размера на любой длине стены
uniform sampler2D tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D top_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform float use_tex = 0.0;      // 1 — красить текстурой
uniform float use_top = 0.0;      // 1 — у верхних граней своя текстура (пол фундамента)
uniform float tile_m = 3.0;       // метров на плитку
uniform float top_light = 1.15;   // верх светлее лица — так читается толщина
varying vec3 wpos;
varying vec3 wnorm;

void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    wnorm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
    if (cut_radius > 0.0 && wpos.y > cut_keep) {
        vec3 d = cut_center - cam_pos;
        float len = length(d);
        d /= max(len, 0.001);
        vec3 v = wpos - cam_pos;
        float t = dot(v, d);
        float r = length(v - d * t);
        if (t < len - 0.5 && r < cut_radius) {
            float edge = smoothstep(cut_radius * 0.7, cut_radius, r);
            float n = fract(sin(dot(floor(FRAGCOORD.xy), vec2(12.9898, 78.233))) * 43758.5453);
            if (n > edge) discard;
        }
    }
    vec3 c = albedo.rgb;
    if (use_tex > 0.5) {
        vec3 an = abs(wnorm);
        vec4 tx;
        if (an.y > an.x && an.y > an.z) {
            tx = use_top > 0.5 ? texture(top_tex, wpos.xz / tile_m) : texture(tex, wpos.xz / tile_m);
            c *= top_light;
        } else if (an.x > an.z) tx = texture(tex, vec2(wpos.z / tile_m, 1.0 - wpos.y / tile_m));
        else tx = texture(tex, vec2(wpos.x / tile_m, 1.0 - wpos.y / tile_m));
        if (tx.a < 0.4) discard;   // сетка-рабица, решётки — прозрачные клетки
        c *= tx.rgb;
    }
    ALBEDO = c;
    ROUGHNESS = roughness;
    METALLIC = metallic;
}
";
        static Shader _cutShader;
        static readonly List<ShaderMaterial> _cutMats = new();
        static readonly Dictionary<string, Material> _cache = new();

        /// <summary>Материал, который режется над персонажем (стены, крыши, кроны, высокие пропсы).</summary>
        public static ShaderMaterial Cut(Color c, float roughness = 0.9f, float metallic = 0f, float keep = 0.6f)
        {
            string key = $"cut:{c.ToHtml()}:{roughness:0.00}:{metallic:0.00}:{keep:0.00}";
            if (_cache.TryGetValue(key, out var have)) return (ShaderMaterial)have;
            _cutShader ??= new Shader { Code = CutShaderCode };
            var m = new ShaderMaterial { Shader = _cutShader };
            m.SetShaderParameter("albedo", c);
            m.SetShaderParameter("roughness", roughness);
            m.SetShaderParameter("metallic", metallic);
            m.SetShaderParameter("cut_keep", keep);
            _cutMats.Add(m);
            _cache[key] = m;
            return m;
        }

        /// <summary>
        /// Режущийся материал с текстурой по мировым координатам: стены РТ, крыши, постройки игрока.
        /// top — своя текстура для верхних граней (пол фундамента); null — та же.
        /// </summary>
        public static ShaderMaterial CutTex(Texture2D tex, Color tint, float tileM = 3f, float keep = 0.6f, Texture2D top = null, float roughness = 0.9f, float metallic = 0f)
        {
            if (tex == null) return Cut(tint, roughness, metallic, keep);
            string key = $"cuttex:{tex.ResourcePath}:{top?.ResourcePath}:{tint.ToHtml()}:{tileM:0.00}:{keep:0.00}:{roughness:0.00}:{metallic:0.00}";
            if (_cache.TryGetValue(key, out var have)) return (ShaderMaterial)have;
            _cutShader ??= new Shader { Code = CutShaderCode };
            var m = new ShaderMaterial { Shader = _cutShader };
            m.SetShaderParameter("albedo", tint);
            m.SetShaderParameter("roughness", roughness);
            m.SetShaderParameter("metallic", metallic);
            m.SetShaderParameter("cut_keep", keep);
            m.SetShaderParameter("tex", tex);
            m.SetShaderParameter("use_tex", 1f);
            m.SetShaderParameter("tile_m", tileM);
            if (top != null) { m.SetShaderParameter("top_tex", top); m.SetShaderParameter("use_top", 1f); }
            _cutMats.Add(m);
            _cache[key] = m;
            return m;
        }

        /// <summary>Обычный материал (земля, мелочь, персонажи).</summary>
        public static StandardMaterial3D Solid(Color c, float roughness = 0.85f, float metallic = 0f)
        {
            string key = $"solid:{c.ToHtml()}:{roughness:0.00}:{metallic:0.00}";
            if (_cache.TryGetValue(key, out var have)) return (StandardMaterial3D)have;
            var m = new StandardMaterial3D { AlbedoColor = c, Roughness = roughness, Metallic = metallic };
            _cache[key] = m;
            return m;
        }

        /// <summary>Светящийся материал без освещения (трассеры, вспышки, огонь, призрак стройки).</summary>
        public static StandardMaterial3D Glow(Color c, float energy = 2f, bool alpha = false)
        {
            string key = $"glow:{c.ToHtml()}:{energy:0.00}:{alpha}";
            if (_cache.TryGetValue(key, out var have)) return (StandardMaterial3D)have;
            var m = new StandardMaterial3D
            {
                AlbedoColor = c,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                EmissionEnabled = true,
                Emission = new Color(c.R, c.G, c.B),
                EmissionEnergyMultiplier = energy,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            if (alpha) m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            _cache[key] = m;
            return m;
        }

        /// <summary>Каждый кадр: куда смотрит камера и где персонаж — для всех режущих материалов.</summary>
        public static void UpdateCut(Vector3 camPos, Vector3 center, float radius)
        {
            foreach (var m in _cutMats)
            {
                m.SetShaderParameter("cam_pos", camPos);
                m.SetShaderParameter("cut_center", center);
                m.SetShaderParameter("cut_radius", radius);
            }
        }

        static readonly Dictionary<string, Texture2D> _tex = new();

        /// <summary>
        /// PNG из game/art напрямую (без CanvasTexture, которую Art.Get отдаёт для 2D-освещения — в 3D она не работает).
        /// </summary>
        public static Texture2D Tex(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_tex.TryGetValue(path, out var t)) return t;
            var res = $"res://art/{path}.png";
            t = ResourceLoader.Exists(res) ? GD.Load<Texture2D>(res) : null;
            _tex[path] = t;
            return t;
        }

        // ---------- палитра (low-poly, приглушённая, под «стекло» интерфейса) ----------

        /// <summary>Уровни постройки: каркас, дерево, камень, металл, сталь, броня.</summary>
        public static readonly Color[] Tier =
        {
            new Color(0.80f, 0.70f, 0.45f),
            new Color(0.55f, 0.38f, 0.22f),
            new Color(0.58f, 0.58f, 0.55f),
            new Color(0.48f, 0.33f, 0.24f),
            new Color(0.36f, 0.40f, 0.44f),
            new Color(0.25f, 0.27f, 0.30f),
        };

        public static Color TierColor(int tier, bool seen)
        {
            var c = Tier[System.Math.Clamp(tier, 0, Tier.Length - 1)];
            return seen ? c : c.Darkened(0.45f);
        }

        static readonly string[] TierTex = { "twig", "wood", "stone", "metal", "steel", "armored" };

        /// <summary>
        /// Материал постройки игрока по уровню — те же текстуры, что у 2D: стены walls/wallface_&lt;уровень&gt;,
        /// фундамент: бока tiles/bfound_&lt;уровень&gt;, пол tiles/bfloor_&lt;уровень&gt;. Вне обзора — темнее.
        /// </summary>
        public static Material Building(int tier, bool seen, bool floor, bool door = false)
        {
            int t = System.Math.Clamp(tier, 0, TierTex.Length - 1);
            var tint = seen ? Colors.White : new Color(0.55f, 0.55f, 0.58f);
            if (t == 0) tint *= new Color(1.05f, 1.02f, 0.95f);
            Texture2D tex = floor ? Tex("tiles/bfound_" + TierTex[t]) : door ? Tex(t >= 3 ? "walls/wallface_door_metal" : "walls/wallface_door_wood") : Tex("walls/wallface_" + TierTex[t]);
            var top = floor ? Tex("tiles/bfloor_" + TierTex[t]) : null;
            if (tex == null) return Cut(TierColor(t, seen), TierRough(t), TierMetal(t));
            return CutTex(tex, tint, floor ? 2f : 2.6f, 0.6f, top, TierRough(t), TierMetal(t));
        }

        public static float TierMetal(int tier) => tier >= 3 ? 0.35f : 0f;
        public static float TierRough(int tier) => tier >= 3 ? 0.55f : 0.9f;
    }
}
