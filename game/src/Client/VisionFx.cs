using Godot;

namespace Rastvordd.Client
{
    /// <summary>
    /// Затенение вне обзора, как в Project Zomboid: серым становится только мир (земля, деревья, стены, предметы),
    /// персонажи не затеняются никогда. Параметры обзора — глобальные для всех шейдеров.
    /// Земля проверяет видимость попиксельно (TerrainView), объекты — целиком по точке основания.
    /// </summary>
    public static class VisionFx
    {
        /// <summary>Функция для вставки в шейдеры: видимость точки мира (в пикселях мира) и цвет «в тени».</summary>
        public const string Lib = @"
global uniform vec2 vis_player;
global uniform vec2 vis_facing;
global uniform float vis_cos_half;
global uniform float vis_range;
global uniform float vis_ppm;
global uniform float vis_strength;
global uniform sampler2D vis_rays : filter_linear, repeat_enable;

float vis_at(vec2 world_px) {
    vec2 d = (world_px - vis_player) / vis_ppm;
    float dist = length(d);
    float c = dot(d / max(dist, 0.0001), vis_facing);
    float cone = smoothstep(vis_cos_half - 0.01, vis_cos_half + 0.01, c);
    float rd = texture(vis_rays, vec2(atan(d.y, d.x) / 6.2831853 + 0.5, 0.5)).r;
    float open = 1.0 - smoothstep(rd - 0.05, rd + 0.05, dist);
    return cone * open * (1.0 - smoothstep(vis_range - 0.5, vis_range, dist));
}

vec3 vis_apply(vec3 col, float v) {
    // приглушённо, а не темно: немного обесцветить и чуть притемнить
    float l = dot(col, vec3(0.3, 0.59, 0.11));
    vec3 hidden = mix(col, vec3(l), 0.55) * 0.82 + vec3(0.004, 0.008, 0.016);
    return mix(col, hidden, (1.0 - v) * vis_strength);
}
";

        const string ObjectShader = "shader_type canvas_item;\n" + Lib + @"
varying vec2 base_px;
void vertex() { base_px = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xy; }
void fragment() { COLOR.rgb = vis_apply(COLOR.rgb, vis_at(base_px)); }
";

        static ShaderMaterial _objectMat;
        /// <summary>Материал для объектов мира (не персонажей): видимость по точке основания узла.</summary>
        public static ShaderMaterial ObjectMat => _objectMat ??= new ShaderMaterial { Shader = new Shader { Code = ObjectShader } };

        static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            void Add(string name, RenderingServer.GlobalShaderParameterType type, Variant value)
            {
                RenderingServer.GlobalShaderParameterAdd(name, type, value);
            }
            Add("vis_player", RenderingServer.GlobalShaderParameterType.Vec2, Vector2.Zero);
            Add("vis_facing", RenderingServer.GlobalShaderParameterType.Vec2, Vector2.Right);
            Add("vis_cos_half", RenderingServer.GlobalShaderParameterType.Float, 0.42f);
            Add("vis_range", RenderingServer.GlobalShaderParameterType.Float, 50f);
            Add("vis_ppm", RenderingServer.GlobalShaderParameterType.Float, Iso.PPM);
            Add("vis_strength", RenderingServer.GlobalShaderParameterType.Float, 1f);
            Add("vis_rays", RenderingServer.GlobalShaderParameterType.Sampler2D, new Variant());
        }

        public static void Update(Vector2 playerWorldPx, Vector2 facing, float cosHalf, float rangeM, Texture2D rays, bool enabled)
        {
            RenderingServer.GlobalShaderParameterSet("vis_player", playerWorldPx);
            RenderingServer.GlobalShaderParameterSet("vis_facing", facing);
            RenderingServer.GlobalShaderParameterSet("vis_cos_half", cosHalf);
            RenderingServer.GlobalShaderParameterSet("vis_range", rangeM);
            RenderingServer.GlobalShaderParameterSet("vis_rays", rays);
            RenderingServer.GlobalShaderParameterSet("vis_strength", enabled ? 1f : 0f);
        }
    }
}
