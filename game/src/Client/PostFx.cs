using Godot;
using Rastvordd.Sim;
using Sky = Rastvordd.Sim.Sky;

namespace Rastvordd.Client
{
    /// <summary>
    /// Пост-обработка всего мира на GPU (HUD не трогает — HUD на слое выше): цветокор по времени суток и погоде,
    /// мягкое свечение только ярких пикселей (лампы, огонь, вспышки), виньетка, лёгкое зерно,
    /// низкий туман ночью и на рассвете, пылинки в солнечном свете. Спрайты не размываются: база — точный пиксель.
    /// F11 — вкл/выкл, --nopost — запуск без неё (для сравнения).
    /// </summary>
    public sealed class PostFx
    {
        const string Code = @"shader_type canvas_item;
render_mode unshaded;
uniform sampler2D scr : hint_screen_texture, filter_linear_mipmap;
uniform sampler2D noise_tex : repeat_enable, filter_linear;
uniform vec3 grade_mul = vec3(1.0);
uniform float sat = 1.0;
uniform float contrast = 1.0;
uniform vec3 shadow_tint = vec3(0.0);
uniform float bloom_amt = 0.2;
uniform float bloom_thr = 0.8;
uniform float vignette = 0.25;
uniform float grain = 0.02;
uniform vec2 cam_px;
uniform float zoom = 1.0;
uniform float fog = 0.0;
uniform vec3 fog_col = vec3(0.6, 0.66, 0.75);
uniform float motes = 0.0;
uniform vec2 wind = vec2(1.0, 0.0);
uniform float quality = 2.0;            // качество 0..3
uniform vec2 sun_dir = vec2(0.0, -1.0);   // куда падают тени (экран), лучи идут так же
uniform float rays = 0.0;         // сила солнечных лучей
uniform vec3 ray_col = vec3(1.0, 0.92, 0.75);
uniform vec2 fog_off = vec2(0.0);   // накопленный снос тумана (px): ветер × время, без скачков при повороте ветра

float h21(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

void fragment() {
    vec2 uv = SCREEN_UV;
    vec3 c = textureLod(scr, uv, 0.0).rgb;
    // свечение: только то, что ярче порога, из размытых мип-уровней (сам спрайт остаётся резким)
    vec2 px = SCREEN_PIXEL_SIZE;
    if (quality > 2.5) {
        // Ультра: лёгкая хроматическая аберрация только по краям кадра
        float e = smoothstep(0.38, 0.75, length(uv - 0.5));
        vec2 ca = (uv - 0.5) * 0.0025 * e;
        c.r = mix(c.r, textureLod(scr, uv + ca, 0.0).r, e);
        c.b = mix(c.b, textureLod(scr, uv - ca, 0.0).b, e);
    }
    if (quality > 1.5) {
        // резкость (Высокое/Ультра)
        vec3 nb = (textureLod(scr, uv + vec2(px.x, 0.0), 0.0).rgb + textureLod(scr, uv - vec2(px.x, 0.0), 0.0).rgb
                 + textureLod(scr, uv + vec2(0.0, px.y), 0.0).rgb + textureLod(scr, uv - vec2(0.0, px.y), 0.0).rgb) * 0.25;
        c += (c - nb) * (quality > 2.5 ? 0.35 : 0.2);
    }
    if (quality > 0.5) {
        vec3 b = textureLod(scr, uv, 2.0).rgb * 0.45 + textureLod(scr, uv, 3.5).rgb * 0.35 + textureLod(scr, uv, 5.0).rgb * 0.2;
        if (quality > 2.5) {
            // многоотводное свечение: 4 диагональных отвода на двух уровнях — круглее и мягче
            for (int i = 0; i < 4; i++) {
                vec2 o = vec2(i < 2 ? -1.0 : 1.0, (i % 2 == 0) ? -1.0 : 1.0) * px;
                b += textureLod(scr, uv + o * 6.0, 3.0).rgb * 0.18 + textureLod(scr, uv + o * 18.0, 4.5).rgb * 0.12;
            }
            b /= 2.2;
        }
        float bm = max(b.r, max(b.g, b.b));
        c += b * smoothstep(bloom_thr, bloom_thr + 0.25, bm) * bloom_amt * (quality > 1.5 ? 1.3 : 1.0);
    }
    // солнечные лучи (Высокое/Ультра): яркие места (просветы крон, освещённые крыши) тянутся полосами по солнцу
    if (quality > 1.5 && rays > 0.005) {
        int n = quality > 2.5 ? 20 : 10;
        float st = quality > 2.5 ? 0.007 : 0.012;
        float acc = 0.0, w = 1.0, ws = 0.0;
        for (int i = 1; i <= 20; i++) {
            if (i > n) break;
            vec3 sm = textureLod(scr, uv - sun_dir * st * float(i), 2.0).rgb;
            acc += max(dot(sm, vec3(0.3, 0.59, 0.11)) - 0.62, 0.0) * w;
            ws += w; w *= 0.9;
        }
        c += ray_col * (acc / ws) * rays * 3.0;
    }

    vec2 world = cam_px + (uv - 0.5) / SCREEN_PIXEL_SIZE / zoom;
    vec2 wp = floor(world / 2.0) * 2.0;   // пиксель-арт: туман и пылинки — крупным пикселем
    // низкий туман: два слоя шума плывут по ветру, лёгкими полосами
    if (fog > 0.005) {
        float n = texture(noise_tex, wp / 900.0 + fog_off / 900.0).r * 0.65 + texture(noise_tex, wp / 330.0 - fog_off * 0.6 / 330.0 + 0.31).r * 0.35;
        float f = mix(smoothstep(0.38, 0.78, n), 0.55 + 0.45 * n, smoothstep(0.85, 1.0, fog)) * fog;   // густой туман — сплошная пелена, а не клочья
        c = mix(c, fog_col, f * mix(0.42, 0.68, smoothstep(0.85, 1.0, fog)));
    }
    // пылинки в солнце: по одной на клетку, медленно дрейфуют и мерцают
    if (motes > 0.005) {
        vec2 q = wp + vec2(TIME * 5.0 * wind.x, TIME * 3.0 + sin(TIME * 0.3) * 8.0);
        vec2 cell = floor(q / 56.0);
        vec2 fr = q - cell * 56.0;
        float h = h21(cell);
        vec2 p = vec2(h21(cell + 1.3), h21(cell + 7.1)) * 44.0 + 6.0 + vec2(sin(TIME * 0.7 + h * 6.0), cos(TIME * 0.5 + h * 5.0)) * 4.0;
        vec2 d = abs(fr - p);
        float m = step(max(d.x, d.y), 1.0) * step(0.72, h) * (0.6 + 0.4 * sin(TIME * 0.6 + h * 40.0));
        c += vec3(1.0, 0.94, 0.78) * max(m, 0.0) * motes * 0.28;
    }

    // цветокор: насыщенность, контраст, оттенок времени суток, холодные тени
    float l = dot(c, vec3(0.3, 0.59, 0.11));
    c = mix(vec3(l), c, sat);
    c = (c - 0.5) * contrast + 0.5;
    c = c * grade_mul + shadow_tint * (1.0 - smoothstep(0.0, 0.45, l));
    // виньетка
    vec2 v = (uv - 0.5) * vec2(1.0, 0.82);
    c *= 1.0 - vignette * smoothstep(0.32, 0.78, length(v) * 1.25);
    // зерно: по 2×2 экранных пикселя, 24 кадра в секунду
    // зерно: только Ультра и неподвижное (не «рябит»), ночью выключено
    if (quality > 2.5) c += (h21(floor(FRAGCOORD.xy / 2.0)) - 0.5) * grain;
    COLOR = vec4(c, 1.0);
}
";
        readonly ColorRect _rect;
        readonly Rid _mat;
        bool _on = true;

        static readonly StringName PGrade = "grade_mul", PSat = "sat", PCon = "contrast", PShadow = "shadow_tint", PBloom = "bloom_amt",
            PThr = "bloom_thr", PVig = "vignette", PGrain = "grain", PCam = "cam_px", PZoom = "zoom", PFog = "fog", PFogCol = "fog_col",
            PMotes = "motes", PWind = "wind", PQ = "quality", PSunDir = "sun_dir", PRays = "rays", PRayCol = "ray_col";
        int _q = 2;

        /// <summary>Качество 0..3: Низкое — цвет и туман; Среднее — + свечение, пылинки; Высокое — + резкость, лучи солнца; Ультра — многоотводное свечение, длинные лучи, аберрация по краям, зерно.</summary>
        public void SetQuality(int q) { _q = q; Set(PQ, (float)q); }

        public PostFx(Node root)
        {
            var layer = new CanvasLayer { Layer = 0 };   // поверх мира и погоды, под HUD (Hud — слой 1)
            root.AddChild(layer);
            var mat = new ShaderMaterial { Shader = new Shader { Code = Code } };
            mat.SetShaderParameter("noise_tex", new NoiseTexture2D { Width = 256, Height = 256, Seamless = true, Normalize = true,
                Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 0.01f, FractalOctaves = 3 } });
            _mat = mat.GetRid();
            _rect = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Material = mat };
            _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(_rect);
            foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--nopost") _on = false;
            _rect.Visible = _on;
        }

        public bool Toggle() { _on = !_on; _rect.Visible = _on; return _on; }

        static float Bell(float t, float c, float w) { float d = (t - c) / w; return Mathf.Max(0, 1 - d * d); }

        /// <param name="day">Время суток 0..1 (0.25 — рассвет, 0.76 — закат).</param>
        static float Grey(Sky s) => s switch { Sky.Rain => 0.55f, Sky.Downpour => 0.8f, Sky.Storm => 1f, Sky.Snow => 0.35f, Sky.HeavySnow => 0.55f, Sky.Blizzard => 0.7f, _ => 0f };
        public void Update(Vector2 camPx, float zoom, float day, float night, Sky sky, float windAngle, float windStrength, Sky prev = Sky.Clear, float blend = 1f)
        {
            if (!_on) return;
            float warm = Mathf.Max(Bell(day, 0.27f, 0.06f), Bell(day, 0.76f, 0.07f));   // золотой час
            float cw = SkyInfo.CloudW(blend);   // плавная смена погоды: всё смешано между прежней и новой
            float wet = Mathf.Lerp(SkyInfo.Wet(prev) ? 1 : 0, SkyInfo.Wet(sky) ? 1 : 0, cw), snowy = Mathf.Lerp(SkyInfo.Snowy(prev) ? 1 : 0, SkyInfo.Snowy(sky) ? 1 : 0, cw);
            float grey = Mathf.Lerp(Grey(prev), Grey(sky), cw);
            float fogW = Mathf.Lerp((int)prev == 8 ? 1 : 0, (int)sky == 8 ? 1 : 0, cw);   // погода «Туман» наползает и рассеивается за время смены
            float sat = 1.06f - 0.1f * night - 0.3f * grey + 0.06f * warm;
            float con = 1.04f - 0.04f * grey - 0.04f * night;
            var g = new Vector3(1, 1, 1);
            g = g.Lerp(new Vector3(1.08f, 0.98f, 0.86f), warm * 0.45f);
            g = g.Lerp(new Vector3(0.9f, 0.97f, 1.1f), night * 0.6f);
            g = g.Lerp(new Vector3(0.97f, 0.99f, 1.02f), grey);
            var shadow = new Vector3(-0.004f, 0.004f, 0.03f) * (0.5f + night) + new Vector3(0.02f, -0.005f, -0.02f) * warm;
            float fog = Mathf.Max(night * 0.55f, Bell(day, 0.26f, 0.07f) * 0.85f) + 0.25f * wet + 0.15f * snowy;
            fog *= 1f - 0.6f * Mathf.Clamp(windStrength, 0, 1);
            fog = Mathf.Lerp(fog, Mathf.Max(fog, 0.95f), fogW);   // погода «Туман»: густой, по всему экрану (27.10)
            var fogCol = new Vector3(0.62f, 0.68f, 0.76f).Lerp(new Vector3(0.18f, 0.22f, 0.32f), night).Lerp(new Vector3(0.85f, 0.72f, 0.62f), warm * 0.5f);
            float motes = (_q >= 1 ? 1f : 0f) * (1f - night) * Mathf.Lerp(prev is Sky.Clear or Sky.Wind ? 1f : 0f, sky is Sky.Clear or Sky.Wind ? 1f : 0f, cw) * (1f + warm);
            var wind = new Vector2(Mathf.Cos(windAngle), Mathf.Sin(windAngle)) * (0.4f + windStrength);

            if (_q >= 2) { sat += _q >= 3 ? 0.12f : 0.08f; con += _q >= 3 ? 0.05f : 0.03f; g *= _q >= 3 ? 1.16f : 1.1f; }   // Высокое/Ультра — сочнее и контрастнее
            Set(PGrade, g); Set(PSat, sat); Set(PCon, con); Set(PShadow, shadow);
            Set(PBloom, 0.22f + 0.55f * night); Set(PThr, 0.82f - 0.3f * night);
            Set(PVig, (0.2f + 0.06f * night + 0.08f * grey) * (_q >= 2 ? 1.1f : 1f)); Set(PGrain, 0.012f * (1f - night));
            Set(PSunDir, new Vector2(Shade.Skew, -Shade.Flat).Normalized());
            Set(PRays, (1f - night) * (1f - grey) * (0.1f + 0.22f * warm) * (_q >= 3 ? 1.3f : 1f));
            Set(PRayCol, new Vector3(1f, 0.93f, 0.8f).Lerp(new Vector3(1f, 0.75f, 0.5f), warm));
            Set(PCam, camPx); Set(PZoom, zoom); Set(PFog, Mathf.Min(fog, 0.8f + 0.2f * fogW)); Set(PFogCol, fogCol);
            Set(PMotes, motes); Set(PWind, wind);
            ulong nowUs = Time.GetTicksUsec(); float fdt = _fogUs == 0 ? 0f : Mathf.Min(0.1f, (nowUs - _fogUs) / 1e6f); _fogUs = nowUs;
            _fogOff += new Vector2(Mathf.Cos(windAngle), Mathf.Sin(windAngle)) * (1f + 5f * windStrength) * fdt;   // туман: 1–6 px/с по ветру
            Set("fog_off", _fogOff);
        }

        Vector2 _fogOff; ulong _fogUs;
        void Set(StringName p, Variant v) => RenderingServer.MaterialSetParam(_mat, p, v);
    }
}
