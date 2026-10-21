using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>
    /// Земля одним шейдером по биомам с сервера: лес, поле, пустошь, тундра, пляж, болото, плюс грязь, гравий, бетон и вода.
    /// Переходы крупные и неровные (по шуму), но ступенчатые по сетке пиксель-арта; на стыках — тонкая тёмная кромка.
    /// Рельеф — светотень по высотам сервера (тот же свет сверху-слева, что на спрайтах).
    /// </summary>
    public partial class TerrainView : ColorRect
    {
        const string Code = @"
shader_type canvas_item;
uniform sampler2D mask_a : filter_linear, repeat_disable;   // R лес, G пустошь, B тундра, A пляж
uniform sampler2D mask_b : filter_linear, repeat_disable;   // R болото, G грязь, B гравий, A бетон
uniform sampler2D mask_c : filter_linear, repeat_disable;   // R вода, G высота 0..1
uniform sampler2D t_grass : filter_nearest, repeat_enable;
uniform sampler2D t_forest : filter_nearest, repeat_enable;
uniform sampler2D t_dry : filter_nearest, repeat_enable;
uniform sampler2D t_snow : filter_nearest, repeat_enable;
uniform sampler2D t_sand : filter_nearest, repeat_enable;
uniform sampler2D t_swamp : filter_nearest, repeat_enable;
uniform sampler2D t_dirt : filter_nearest, repeat_enable;
uniform sampler2D t_gravel : filter_nearest, repeat_enable;
uniform sampler2D t_concrete : filter_nearest, repeat_enable;
uniform sampler2D detail_noise : filter_linear, repeat_enable;
uniform sampler2D macro_noise : filter_linear, repeat_enable;
uniform float map_size = 400.0;
uniform float px_per_m = 32.0;
uniform float tile_m = 4.0;
uniform vec2 sun_dir = vec2(-0.6, -0.8);

void fragment() {
    vec2 m = floor(UV * map_size * px_per_m) / px_per_m;   // мировая точка, привязанная к пикселю арта
    vec2 uv = m / map_size;
    vec2 tuv = m / tile_m;
    vec4 a = texture(mask_a, uv);
    vec4 b = texture(mask_b, uv);
    vec4 c = texture(mask_c, uv);
    float grass = max(0.0, 1.0 - (a.r + a.g + a.b + a.a + b.r + b.g + b.b + b.a + c.r));

    // спокойная неровность границ: крупная волна + немного мелкой
    float j = (texture(detail_noise, m / 16.0).r - 0.5) * 0.34 + (texture(detail_noise, m / 5.0 + 0.31).r - 0.5) * 0.1;

    float w[10];
    w[0] = grass; w[1] = a.r; w[2] = a.g; w[3] = a.b; w[4] = a.a;
    w[5] = b.r; w[6] = b.g; w[7] = b.b; w[8] = b.a; w[9] = c.r;
    int id = 0; float best = -9.0, second = -9.0;
    for (int i = 0; i < 10; i++) {
        float v = w[i] + (w[i] > 0.02 ? j * (i == 9 ? 0.5 : 1.0) : -9.0);
        if (v > best) { second = best; best = v; id = i; } else if (v > second) { second = v; }
    }

    vec3 col;
    float h = c.g;
    if (id == 9) {
        // вода: у берега светлее, вдали темнее, лёгкая рябь
        float depth = clamp((0.5 - h) * 6.0, 0.0, 1.0);
        col = mix(vec3(0.22, 0.33, 0.33), vec3(0.07, 0.15, 0.21), depth);
        float rip = texture(detail_noise, m / 3.0 + vec2(TIME * 0.05, TIME * 0.03)).r;
        col *= 0.92 + floor(rip * 4.0) / 4.0 * 0.14;
        if (best - second < 0.06) col = mix(col, vec3(0.62, 0.66, 0.6), 0.55);   // пена у кромки
        COLOR = vec4(vis_apply(col, vis_at((m - vec2(map_size * 0.5)) * px_per_m)), 1.0);
    } else {
        if (id == 1) col = texture(t_forest, tuv).rgb;
        else if (id == 2) col = texture(t_dry, tuv).rgb;
        else if (id == 3) col = texture(t_snow, tuv).rgb;
        else if (id == 4) col = texture(t_sand, tuv).rgb;
        else if (id == 5) col = texture(t_swamp, tuv).rgb;
        else if (id == 6) col = texture(t_dirt, tuv).rgb;
        else if (id == 7) col = texture(t_gravel, tuv).rgb;
        else if (id == 8) col = texture(t_concrete, tuv).rgb;
        else col = texture(t_grass, tuv).rgb;

        // крупные пятна: очень плавно, чтобы не рябило
        float mac = texture(macro_noise, m / 120.0).r;
        col *= mix(0.82, 1.1, mac);
        // трава зеленее, лес чуть холоднее — иначе всё сливается в один бурый
        if (id == 0) col *= vec3(0.95, 1.06, 0.87);
        if (id == 1) col *= vec3(0.95, 1.04, 0.92);
        float lum = dot(col, vec3(0.3, 0.55, 0.15));
        col = mix(vec3(lum), col, 1.1);
        if (id == 0) {
            float dry = texture(macro_noise, m / 85.0 + vec2(0.43, 0.17)).r;
            col = mix(col, col * vec3(1.14, 1.04, 0.78), smoothstep(0.5, 0.7, dry));
            col = mix(col, col * vec3(0.8, 0.9, 0.82), smoothstep(0.42, 0.25, dry));
        }

        // рельеф по высотам сервера, ступеньками
        float e = 2.0 / map_size;
        float hx = texture(mask_c, uv + vec2(e, 0.0)).g - texture(mask_c, uv - vec2(e, 0.0)).g;
        float hy = texture(mask_c, uv + vec2(0.0, e)).g - texture(mask_c, uv - vec2(0.0, e)).g;
        vec3 nrm = normalize(vec3(-hx * 9.0, -hy * 9.0, 1.0));
        float lit = dot(nrm, normalize(vec3(-sun_dir, 1.6)));
        lit = floor(lit * 6.0 + 0.5) / 6.0;
        col *= mix(0.8, 1.14, clamp(lit, 0.0, 1.0));

        if (best - second < 0.03) col *= 0.86;   // кромка на стыке поверхностей
        COLOR = vec4(vis_apply(col, vis_at((m - vec2(map_size * 0.5)) * px_per_m)), 1.0);
    }
}";

        public TerrainView(MapInfo map, int seed)
        {
            float size = map.HalfSize * 2;
            Position = new Vector2(-map.HalfSize, -map.HalfSize) * Iso.PPM;
            Size = new Vector2(size, size) * Iso.PPM;
            MouseFilter = MouseFilterEnum.Ignore;

            BuildMasks(map, out var ma, out var mb, out var mc);
            var mat = new ShaderMaterial { Shader = new Shader { Code = Code.Replace("shader_type canvas_item;", "shader_type canvas_item;\n" + VisionFx.Lib) } };
            mat.SetShaderParameter("mask_a", ImageTexture.CreateFromImage(ma));
            mat.SetShaderParameter("mask_b", ImageTexture.CreateFromImage(mb));
            mat.SetShaderParameter("mask_c", ImageTexture.CreateFromImage(mc));
            void T(string param, string art, string fallback = null) =>
                mat.SetShaderParameter(param, Art.Get(art) ?? (fallback != null ? Art.Get(fallback) : null));
            T("t_grass", "tiles/ground_grass");
            T("t_forest", "tiles/ground_forest", "tiles/ground_dirt");
            T("t_dry", "tiles/ground_dry", "tiles/ground_sand");
            T("t_snow", "tiles/ground_snow", "tiles/ground_gravel");
            T("t_sand", "tiles/ground_sand");
            T("t_swamp", "tiles/ground_swamp", "tiles/ground_dirt");
            T("t_dirt", "tiles/ground_dirt");
            T("t_gravel", "tiles/ground_gravel");
            T("t_concrete", "tiles/ground_concrete");
            mat.SetShaderParameter("detail_noise", Noise(seed, 0.035f));
            mat.SetShaderParameter("macro_noise", Noise(seed + 7, 0.02f));
            mat.SetShaderParameter("map_size", size);
            mat.SetShaderParameter("px_per_m", Iso.PPM);
            Material = mat;
        }

        static NoiseTexture2D Noise(int seed, float freq) => new NoiseTexture2D
        {
            Width = 256, Height = 256, Seamless = true,
            Noise = new FastNoiseLite { Seed = seed, Frequency = freq, FractalOctaves = 3 },
        };

        /// <summary>Веса поверхностей по сетке биомов: размытые по соседям (широкие переходы) + тропы, площадка РТ и гравий вокруг.</summary>
        static void BuildMasks(MapInfo map, out Image ma, out Image mb, out Image mc)
        {
            int n = map.N;
            const int S = 2;              // маска в 2 раза плотнее сетки биомов (1 м) — ровнее края площадок и троп
            int ms = n * S;
            float step = map.Cell / S;
            ma = Image.CreateEmpty(ms, ms, false, Image.Format.Rgba8);
            mb = Image.CreateEmpty(ms, ms, false, Image.Format.Rgba8);
            mc = Image.CreateEmpty(ms, ms, false, Image.Format.Rgba8);
            var path = new[] { new Vector2(0, 60), new Vector2(8, 30), new Vector2(18, 8), new Vector2(20, 2), new Vector2(20, -6) };
            const int R = 2;   // радиус размытия, клеток

            for (int my = 0; my < ms; my++)
            for (int mx = 0; mx < ms; mx++)
            {
                int x = mx / S, y = my / S;
                var acc = new float[7];
                int cnt = 0;
                for (int dy = -R; dy <= R; dy++)
                for (int dx = -R; dx <= R; dx++)
                {
                    int xx = Mathf.Clamp(x + dx, 0, n - 1), yy = Mathf.Clamp(y + dy, 0, n - 1);
                    acc[(int)map.Biomes[yy * n + xx]] += 1;
                    cnt++;
                }
                for (int i = 0; i < 7; i++) acc[i] /= cnt;

                var p = new Vector2((mx + 0.5f) * step - map.HalfSize, (my + 0.5f) * step - map.HalfSize);
                // площадки РТ: бетон внутри забора, гравий по краю
                float pad = 0, gravel = 0;
                for (int yi = 0; yi < map.Yards.Count; yi++)
                {
                    if (yi < map.YardPrefab.Count && map.YardPrefab[yi]) continue;   // готовая РТ: земля своими участками
                    var (ymin, ymax) = map.Yards[yi];
                    var mn = new Vector2(ymin.X, ymin.Y); var mx2 = new Vector2(ymax.X, ymax.Y);
                    if (p.X < mn.X - 6 || p.X > mx2.X + 6 || p.Y < mn.Y - 6 || p.Y > mx2.Y + 6) continue;
                    float pd = Box(p, mn + new Vector2(3, 3), mx2 - new Vector2(3, 3), 1.5f);
                    pad = Mathf.Max(pad, pd);
                    gravel = Mathf.Max(gravel, Box(p, mn, mx2, 3f) * (1 - pd));
                }
                float d = float.MaxValue;
                for (int i = 0; i < path.Length - 1; i++) d = Mathf.Min(d, SegDist(p, path[i], path[i + 1]));
                float dirt = Mathf.Clamp(1.1f - d / 3f, 0, 1);

                // площадка/тропа «вытесняют» биом под собой
                float k = 1 - Mathf.Max(pad, Mathf.Max(gravel, dirt));
                ma.SetPixel(mx, my, new Color(acc[(int)Biome.Forest] * k, acc[(int)Biome.Arid] * k, acc[(int)Biome.Tundra] * k, acc[(int)Biome.Beach] * k));
                mb.SetPixel(mx, my, new Color(acc[(int)Biome.Swamp] * k, dirt, gravel, pad));
                float h = map.Heights[y * n + x];
                mc.SetPixel(mx, my, new Color(acc[(int)Biome.Water], Mathf.Clamp(h * 0.5f + 0.5f, 0, 1), 0, 1));
            }
        }

        static float Box(Vector2 p, Vector2 min, Vector2 max, float fade) =>
            Mathf.Clamp(1f - p.Clamp(min, max).DistanceTo(p) / fade, 0, 1);

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0, 1);
            return p.DistanceTo(a + ab * t);
        }

        /// <summary>Биом в точке (для выбора вида деревьев и т. п.).</summary>
        public static Biome BiomeAt(MapInfo map, SVec p)
        {
            int x = (int)((p.X + map.HalfSize) / map.Cell), y = (int)((p.Y + map.HalfSize) / map.Cell);
            if (x < 0 || y < 0 || x >= map.N || y >= map.N) return Biome.Water;
            return map.Biomes[y * map.N + x];
        }
    }
}
