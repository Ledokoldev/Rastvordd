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
global uniform float wx_snow;   // 0..1 — снег лежит (оседает шапками на объекты и на землю)
global uniform float wx_wet;    // 0..1 — мокро (темнее, лужи)
global uniform vec3 vol_sun;    // солнце в экранных осях (x вправо, y вниз, z к камере), Shade.SetSun
global uniform float vol_amp;   // сила светотени по нормалям (ночью почти 0)
global uniform float vol_warm;  // 0..1 — насколько тёплый свет (низкое солнце — золотой час)
global uniform sampler2D wx_cloud_tex : filter_linear, repeat_enable;
global uniform vec2 wx_cloud_off;  // сдвиг облаков, м (плывут по ветру)
global uniform float wx_cloud_amt; // 0..1 — сила теней от облаков (ясно — заметные, пасмурно — нет)
global uniform float gfx_q;        // качество графики 0..3 (Низкое..Ультра), GameSettings.Quality

// контактная тень (AO) у подножия стен и техники по SDF заслонов (GPU): sdf — texture_sdf(screen_uv_to_sdf(SCREEN_UV))
vec3 gfx_ao(vec3 col, float sdf) {
    if (gfx_q < 1.5) return col;
    float ao = 1.0 - smoothstep(0.0, gfx_q > 2.5 ? 26.0 : 18.0, sdf);
    return col * (1.0 - ao * (gfx_q > 2.5 ? 0.34 : 0.24)) * mix(vec3(1.0), vec3(0.94, 0.97, 1.06), ao);
}

// объём от солнца по карте нормалей (Art: CanvasTexture с _n.png): плоская нормаль (0,0,1) — множитель ровно 1,
// поэтому спрайты без карты выглядят как раньше; освещённая сторона до +18%, теневая до −28%
vec3 sun_lit(vec3 col, vec3 n) {
    float qa = gfx_q < 0.5 ? 0.55 : (gfx_q < 1.5 ? 1.0 : (gfx_q < 2.5 ? 1.6 : 2.0));
    float dk = dot(n, vol_sun) - vol_sun.z;
    if (gfx_q > 1.5 && dk < 0.0) qa *= 0.55;   // тень мягче, свет ярче — объём без потемнения картинки   // выше качество — объёмнее
    float k = 1.0 + vol_amp * qa * dk;
    k = gfx_q > 1.5 ? clamp(k, 0.74, gfx_q > 2.5 ? 1.5 : 1.36) : clamp(k, 0.72, 1.18);
    // свет тёплый, тень холодная (как в кино): освещённая сторона к жёлто-оранжевому, теневая — в синеву
    vec3 warm = mix(vec3(1.0), vec3(1.07, 1.0, 0.86), vol_warm);
    vec3 cool = vec3(0.9, 0.96, 1.1);
    vec3 tint = k >= 1.0 ? mix(vec3(1.0), warm, clamp((k - 1.0) / 0.18, 0.0, 1.0)) : mix(vec3(1.0), cool, clamp((1.0 - k) / 0.28, 0.0, 1.0));
    return col * k * mix(vec3(1.0), tint, min(1.0, vol_amp * 1.4));
}

// тени от облаков: крупные мягкие пятна плывут по ветру; в тени холоднее и темнее, на солнце — чуть теплее
vec3 clouds(vec3 col, vec2 world_px) {
    if (wx_cloud_amt < 0.01) return col;
    vec2 m = world_px / vis_ppm + wx_cloud_off;
    float n = texture(wx_cloud_tex, m / 140.0).r * 0.75 + texture(wx_cloud_tex, m / 47.0 + 0.37).r * 0.25;
    float sh = smoothstep(0.48, 0.62, n) * wx_cloud_amt;
    return mix(col * mix(vec3(1.0), vec3(1.03, 1.01, 0.96), wx_cloud_amt), col * vec3(0.74, 0.79, 0.9), sh);
}

float wx_hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

// снег и сырость на спрайте: шапка снега на верхних кромках силуэта (толщина растёт с wx_snow), лёгкий иней; мокрое — темнее
vec3 wx_object(vec3 col, sampler2D tex, vec2 uv, vec2 tps, vec2 wpx) {
    if (wx_snow > 0.02) {
        float capPx = 1.0 + floor(wx_snow * 3.0);
        float cap = 0.0;
        for (int i = 1; i <= 4; i++) {
            if (float(i) <= capPx && texture(tex, uv - vec2(0.0, tps.y * float(i))).a < 0.5) cap = 1.0;
        }
        float patchy = step(0.35 - wx_snow * 0.35, wx_hash(floor(wpx / 3.0)));
        float l = dot(col, vec3(0.3, 0.59, 0.11));
        col = mix(col, vec3(l) * 1.05 + vec3(0.03, 0.04, 0.06), wx_snow * 0.22);   // иней
        col = mix(col, vec3(0.88, 0.91, 0.96), cap * patchy * (0.55 + 0.45 * wx_snow));
        // заметает и сам спрайт: пятна снега растут с покровом (крупные + мелкие), под снегом чуть видна светотень
        float n = wx_hash(floor(wpx / 2.0)) * 0.45 + wx_hash(floor(wpx / 6.0)) * 0.55;
        float body = smoothstep(1.0 - wx_snow * 0.9, 1.04 - wx_snow * 0.9, n);
        vec3 snowc = vec3(0.86, 0.9, 0.96) * (0.78 + 0.3 * l);
        col = mix(col, snowc, body * 0.92);
    }
    col *= 1.0 - wx_wet * 0.14;
    return col;
}

float vis_at(vec2 world_px) {
    vec2 d = (world_px - vis_player) / vis_ppm;
    float dist = length(d);
    float c = dot(d / max(dist, 0.0001), vis_facing);
    float cone = smoothstep(vis_cos_half - 0.12, vis_cos_half + 0.06, c);
    float rd = texture(vis_rays, vec2(atan(d.y, d.x) / 6.2831853 + 0.5, 0.5)).r;
    float open = 1.0 - smoothstep(rd - 0.4, rd + 0.9, dist);
    return cone * open * (1.0 - smoothstep(vis_range - 4.0, vis_range, dist));
}

vec3 vis_apply_raw(vec3 col, float v) {
    // приглушённо, а не темно: немного обесцветить и чуть притемнить
    float l = dot(col, vec3(0.3, 0.59, 0.11));
    vec3 hidden = mix(col, vec3(l), 0.28) * vec3(0.8, 0.84, 0.92);   // вне обзора: приглушённо и холоднее, но мир живой
    return mix(col, hidden, (1.0 - v) * vis_strength);
}

vec3 vis_env(vec3 col, vec2 world_px) { return vis_apply_raw(clouds(col, world_px), vis_at(world_px)); }
";

        const string ObjectShader = "shader_type canvas_item;\n" + Lib + @"
varying vec2 base_px;
varying vec2 local_px;
void vertex() { base_px = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xy; local_px = VERTEX; }
void fragment() { COLOR.rgb = vis_env(wx_object(sun_lit(COLOR.rgb, NORMAL), TEXTURE, UV, TEXTURE_PIXEL_SIZE, base_px + local_px), base_px); }
";

        // земля и плоские слои (участки РТ, декали, входы, провода): видимость по каждому пикселю, а не по точке узла —
        // иначе весь слой РТ гаснет или светится целиком от одной точки
        const string GroundShader = "shader_type canvas_item;\n" + Lib + @"
varying vec2 world_px;
void vertex() { world_px = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy; }
void fragment() { COLOR.rgb = vis_env(gfx_ao(COLOR.rgb, texture_sdf(screen_uv_to_sdf(SCREEN_UV))), world_px); }
";

        const string SwayShader = "shader_type canvas_item;\n" + Lib + @"
uniform float sway_amp = 1.5;
uniform float sway_height = 120.0;
varying vec2 base_px;
void vertex() {
    base_px = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xy;
    // ветер: верх качается, основание и тень на земле стоят; смещение целыми пикселями (пиксель-арт)
    float h = clamp(-VERTEX.y / sway_height, 0.0, 1.0);
    float ph = (base_px.x + VERTEX.x) * 0.031 + base_px.y * 0.017;   // + VERTEX.x: в ряду травы (TuftRow) пучки качаются вразнобой
    float off = (sin(TIME * 1.3 + ph) * 0.7 + sin(TIME * 2.9 + ph * 1.7) * 0.3) * sway_amp * h * h;
    VERTEX.x += round(off);
}
uniform float root_dark = 0.0;
uniform float root_from = 0.45;   // с какой высоты спрайта (доля, 1 — низ) начинается затемнение к земле
uniform vec3 tint = vec3(1.0);
void fragment() {
    COLOR.rgb = sun_lit(COLOR.rgb * tint, NORMAL);   // трава под цвет земли (иначе светлые куртины «лежат» на ней)
    COLOR.rgb *= 1.0 - root_dark * smoothstep(root_from, 1.0, UV.y);   // трава «врастает»: к земле темнее, как травинки в шейдере земли
    COLOR.rgb = vis_env(wx_object(COLOR.rgb, TEXTURE, UV, TEXTURE_PIXEL_SIZE, base_px + UV * 64.0), base_px);
}
";

        static ShaderMaterial _treeMat, _grassMat;
        /// <summary>Деревья: видимость + покачивание кроны.</summary>
        public static ShaderMaterial TreeMat => _treeMat ??= TreeSway();
        static ShaderMaterial TreeSway()
        {
            var m = Sway(2.2f, 170f);
            m.SetShaderParameter("root_dark", 0.3f); m.SetShaderParameter("root_from", 0.86f);   // только низ ствола темнеет к земле (корни в почве), без подкраски травы
            return m;
        }
        /// <summary>Трава, кусты, цветы: видимость + лёгкое покачивание.</summary>
        public static ShaderMaterial GrassMat => _grassMat ??= Sway(1.3f, 40f, 0.4f);

        static ShaderMaterial Sway(float amp, float height, float rootDark = 0f)
        {
            var m = new ShaderMaterial { Shader = new Shader { Code = SwayShader } };
            m.SetShaderParameter("root_dark", rootDark);
            if (rootDark > 0) m.SetShaderParameter("tint", new Vector3(0.8f, 0.92f, 0.7f));
            m.SetShaderParameter("sway_amp", amp);
            m.SetShaderParameter("sway_height", height);
            return m;
        }

        const string LootShader = "shader_type canvas_item;\n" + Lib + @"
// крейт с лутом: по силуэту раз в ~3 с пробегает косой блик, по краю — лёгкое мерцание; пустой — без эффекта
uniform float loot_on = 1.0;
varying vec2 base_px;
varying vec2 local_px;
void vertex() { base_px = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xy; local_px = VERTEX; }
void fragment() {
    float ph = fract(TIME / 3.2 + base_px.x * 0.0071 + base_px.y * 0.0043);
    float band = (local_px.x + local_px.y * 0.6) * 0.035 - (ph * 6.0 - 2.5);
    float glint = exp(-band * band * 6.0) * step(ph, 0.55) * loot_on;
    COLOR.rgb = sun_lit(COLOR.rgb, NORMAL) + vec3(1.0, 0.95, 0.75) * glint * 0.55 * COLOR.a;
    COLOR.rgb = vis_env(COLOR.rgb, base_px);
}
";
        static ShaderMaterial _lootMat;
        /// <summary>Крейты с лутом: видимость + бегущий блик, чтобы их было видно среди декоративных ящиков.</summary>
        public static ShaderMaterial LootMat => _lootMat ??= new ShaderMaterial { Shader = new Shader { Code = LootShader } };

        static ShaderMaterial _objectMat;
        /// <summary>Материал для объектов мира (не персонажей): видимость по точке основания узла.</summary>
        static ShaderMaterial _groundMat;
        public static ShaderMaterial GroundMat => _groundMat ??= new ShaderMaterial { Shader = new Shader { Code = GroundShader } };
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
            Add("wx_snow", RenderingServer.GlobalShaderParameterType.Float, 0f);
            Add("wx_wet", RenderingServer.GlobalShaderParameterType.Float, 0f);
            Add("vol_sun", RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(0, 0, 1));
            Add("vol_amp", RenderingServer.GlobalShaderParameterType.Float, 0f);
            Add("vol_warm", RenderingServer.GlobalShaderParameterType.Float, 0f);
            // облака: бесшовный шум, крупные мягкие пятна
            var cloud = new NoiseTexture2D { Width = 256, Height = 256, Seamless = true, Normalize = true,
                Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 0.012f, FractalOctaves = 3 } };
            Add("wx_cloud_tex", RenderingServer.GlobalShaderParameterType.Sampler2D, cloud);
            Add("wx_cloud_off", RenderingServer.GlobalShaderParameterType.Vec2, Vector2.Zero);
            Add("wx_cloud_amt", RenderingServer.GlobalShaderParameterType.Float, 0f);
            Add("gfx_q", RenderingServer.GlobalShaderParameterType.Float, (float)GameSettings.Quality);
        }

        /// <summary>Качество графики в шейдерах мира (0..3).</summary>
        public static void SetQuality(int q) { if (_registered) RenderingServer.GlobalShaderParameterSet("gfx_q", (float)q); }

        static Vector2 _cloudOff;
        /// <summary>Тени облаков: плывут по ветру (скорость м/с), сила по погоде и дню.</summary>
        public static void SetClouds(Vector2 windMs, float amount, float dt)
        {
            if (!_registered) return;
            _cloudOff -= windMs * dt;
            RenderingServer.GlobalShaderParameterSet("wx_cloud_off", _cloudOff);
            RenderingServer.GlobalShaderParameterSet("wx_cloud_amt", amount);
        }

        /// <summary>Солнце для светотени по нормалям (Shade.SetSun): направление на солнце в экранных осях и сила.</summary>
        public static void SetSun(Vector3 dir, float amp, float warm = 0f)
        {
            if (!_registered) return;
            RenderingServer.GlobalShaderParameterSet("vol_warm", warm);
            RenderingServer.GlobalShaderParameterSet("vol_sun", dir.Normalized());
            RenderingServer.GlobalShaderParameterSet("vol_amp", amp);
        }

        /// <summary>Погода в шейдерах: сколько снега лежит и насколько мокро.</summary>
        public static void SetWeather(float snow, float wet)
        {
            RenderingServer.GlobalShaderParameterSet("wx_snow", snow);
            RenderingServer.GlobalShaderParameterSet("wx_wet", wet);
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
