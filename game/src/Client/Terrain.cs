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
uniform sampler2D mask_c : filter_linear, repeat_disable;   // R вода, G высота 0..1, B до берега /24 м
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
        // вода (D-gfx): глубина по расстоянию до берега (маска c.b, считана при загрузке) и высоте, рябь двумя слоями,
        // отражение неба по времени суток (vol_amp/vol_warm), блики по направлению солнца, пена-прибой у кромки, каустика на мели
        float sd = c.b * 24.0;                                    // до берега, м
        float depth = clamp(max((0.5 - h) * 5.0, sd / 14.0), 0.0, 1.0);
        vec3 shallow = vec3(0.24, 0.38, 0.36), deep = vec3(0.05, 0.13, 0.20);
        col = mix(shallow, deep, smoothstep(0.0, 1.0, depth));
        vec2 w1 = m / 3.0 + vec2(TIME * 0.045, TIME * 0.02);
        vec2 w2 = vec2(m.y, -m.x) / 4.7 + vec2(-TIME * 0.03, TIME * 0.038);
        float n1 = texture(detail_noise, w1).r, n2 = texture(detail_noise, w2).r;
        float wave = n1 + n2 - 1.0;                               // −1..1
        // наклон волны по разности соседних выборок — для бликов
        float gx = texture(detail_noise, w1 + vec2(0.02, 0.0)).r - n1;
        float day = clamp(vol_amp / 1.15 + vol_warm * 0.5, 0.0, 1.0);
        vec3 sky = mix(vec3(0.05, 0.07, 0.13), mix(vec3(0.46, 0.58, 0.68), vec3(0.78, 0.52, 0.36), vol_warm), day);
        float fres = 0.18 + 0.22 * depth;
        col = mix(col, sky, fres * (0.6 + 0.4 * floor((wave * 0.5 + 0.5) * 4.0) / 4.0));
        col *= 0.93 + floor((wave * 0.5 + 0.5) * 4.0) / 4.0 * 0.14;
        // блики: гребни, повёрнутые к солнцу; при низком солнце — длинная тёплая дорожка
        float facing = clamp(gx * 40.0 * sign(-vol_sun.x + 0.0001) + 0.5, 0.0, 1.0);
        float crest = n1 * n2;
        float thr = mix(0.57, 0.42, vol_warm);
        float glint = step(thr, crest) * facing * day;
        col += glint * mix(vec3(0.55, 0.6, 0.6), vec3(0.85, 0.55, 0.3), vol_warm) * 0.55;
        // каустика на мели: тонкие светлые жилки из |шум−0.5|
        float cz = abs(texture(detail_noise, m / 2.2 + vec2(TIME * 0.06, -TIME * 0.04)).r - texture(detail_noise, m / 1.7 - vec2(TIME * 0.05, 0.02)).r);
        float shallowK = (1.0 - smoothstep(1.0, 5.0, sd)) * day;
        col += step(cz, 0.018) * shallowK * vec3(0.10, 0.14, 0.12);
        // пена: кромка + бегущие к берегу полосы прибоя
        float edge = 1.0 - smoothstep(0.0, 0.07, best - second);
        float calm = smoothstep(0.6, 0.9, c.a);   // река — 0: без бегущих полос прибоя, только кромка
        float surf = step(0.8, fract(sd * 0.7 + TIME * 0.35 + wave * 0.35)) * (1.0 - smoothstep(0.6, 2.6, sd)) * calm;
        float foam = max(edge, surf * 0.75);
        col = mix(col, vec3(0.70, 0.74, 0.70) * (0.35 + 0.65 * max(day, 0.15)), step(0.5, foam) * 0.65);
        col = floor(col * 48.0 + 0.5) / 48.0;                     // квантование — пиксельная палитра
        COLOR = vec4(vis_env(gfx_ao(col, texture_sdf(screen_uv_to_sdf(SCREEN_UV))), (m - vec2(map_size * 0.5)) * px_per_m), 1.0);
    } else {
        if (id == 1) col = mix(texture(t_forest, tuv).rgb, texture(t_forest, vec2(tuv.y, -tuv.x) + vec2(0.53, 0.19)).rgb, smoothstep(0.35, 0.65, texture(macro_noise, m / 21.0 + vec2(0.6, 0.1)).r));
        else if (id == 2) col = texture(t_dry, tuv).rgb;
        else if (id == 3) col = texture(t_snow, tuv).rgb;
        else if (id == 4) col = texture(t_sand, tuv).rgb;
        else if (id == 5) col = texture(t_swamp, tuv).rgb;
        else if (id == 6) col = texture(t_dirt, tuv).rgb;
        else if (id == 7) col = texture(t_gravel, tuv).rgb;
        else if (id == 8) col = texture(t_concrete, tuv).rgb;
        else {
            // против повтора тайла: второй отсчёт со сдвигом и поворотом, смешение по крупному шуму
            vec2 tuv2 = vec2(-tuv.y, tuv.x) * 1.0 + vec2(0.37, 0.61);
            float bl = smoothstep(0.35, 0.65, texture(macro_noise, m / 23.0 + vec2(0.2, 0.8)).r);
            col = mix(texture(t_grass, tuv).rgb, texture(t_grass, tuv2).rgb, bl);
        }

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

        // травинки: в траве, лесу, пустоши и болоте земля не плоская — у каждого пикселя-корня может расти стебель 2–5 px
        // со светлым кончиком и тёмным корнем (свет сверху-слева, как на спрайтах); между стеблями — тень
        if (id == 0 || id == 1 || id == 2 || id == 5) {
            vec2 pp = floor(m * px_per_m);
            float dens = (id == 0 ? 0.2 : id == 1 ? 0.11 : id == 2 ? 0.08 : 0.15) * smoothstep(0.3, 0.65, texture(macro_noise, m / 7.0 + vec2(0.31, 0.77)).r);   // пятнами, между ними спокойная земля
            float blade = 0.0;
            // хеш без синуса (у sin на больших координатах — диагональные полосы); наклон у каждого стебля свой: влево, прямо, вправо
            for (int k = 0; k < 5; k++) {
                for (int dx = -1; dx <= 1; dx++) {
                    vec2 root = pp + vec2(float(dx), float(k));
                    vec3 q = fract(vec3(root.xyx) * vec3(0.1031, 0.1030, 0.0973));
                    q += dot(q, q.yzx + 33.33);
                    float hr = fract((q.x + q.y) * q.z);
                    if (hr >= dens) continue;
                    float hv = fract(hr * 57.31 + q.y);
                    float lean = hv < 0.33 ? -1.0 : (hv < 0.66 ? 0.0 : 1.0);
                    float hgt = 2.0 + floor(fract(hr * 91.7) * 4.0);
                    // стебель от корня вверх: на высоте k он смещён на lean*k/3 пикселя
                    if (float(k) < hgt && float(dx) + floor(lean * float(k) / 3.0 + 0.5) == 0.0) blade = max(blade, (float(k) + 1.0) / hgt);
                }
            }
            vec3 tip = col * (id == 2 ? vec3(1.22, 1.17, 1.0) : vec3(1.15, 1.21, 1.0));
            if (blade > 0.0) col = mix(col * 0.86, tip, blade);
        }

        if (best - second < 0.03) col *= 0.86;   // кромка на стыке поверхностей

        // погода: сырость темнит землю, в низинах собираются лужи; снег ложится пятнами, растущими до сплошного покрова
        if (id != 3) {
            col *= 1.0 - wx_wet * 0.1;
            float pn = texture(macro_noise, m / 9.0 + vec2(0.71, 0.29)).r * 0.7 + texture(detail_noise, m / 2.5).r * 0.3;
            float puddle = smoothstep(0.86 - wx_wet * 0.12, 0.88 - wx_wet * 0.12, pn) * step(0.05, wx_wet) * (1.0 - step(0.5, wx_snow));
            float glint = step(0.9, texture(detail_noise, m / 1.3 + vec2(TIME * 0.02, 0.0)).r);
            vec3 water = mix(col * 0.6, vec3(0.42, 0.48, 0.56), 0.6) + glint * 0.12;   // отражение неба, блики
            col = mix(col, water, puddle);
            float sn = texture(macro_noise, m / 14.0 + vec2(0.13, 0.57)).r * 0.6 + texture(detail_noise, m / 3.0).r * 0.4;
            float cover = smoothstep(1.02 - wx_snow * 1.15, 1.06 - wx_snow * 1.15, sn);
            col = mix(col, texture(t_snow, tuv).rgb * 1.03, cover);
        }
        COLOR = vec4(vis_env(gfx_ao(col, texture_sdf(screen_uv_to_sdf(SCREEN_UV))), (m - vec2(map_size * 0.5)) * px_per_m), 1.0);
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
        internal static void BuildMasks(MapInfo map, out Image ma, out Image mb, out Image mc)   // internal — маски берёт и 3D-клиент (Client3D/World3D.cs)
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
            var shore = ShoreDist(map);   // до берега, м — один раз при загрузке (пена, мель, каустика)
            var soft = SoftBiomes(map, 3);   // поле долей биомов суши, размытое двумя проходами (~12 м)
            _hard = new byte[ms * ms]; _hardN = ms; _hardStep = step; _hardHalf = map.HalfSize;

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
                {
                    // суша: широкий мягкий переход между биомами (размытое поле ~12 м, билинейно, с волнистым сдвигом) — без ступенек клеток
                    float wx = (mx + 0.5f) * step, wy = (my + 0.5f) * step;
                    float ox = Mathf.Sin(wy * 0.071f + Mathf.Sin(wx * 0.043f) * 2f) * 3f + Mathf.Sin(wx * 0.23f + wy * 0.17f) * 1.2f;
                    float oy = Mathf.Sin(wx * 0.067f + Mathf.Sin(wy * 0.051f) * 2f) * 3f + Mathf.Sin(wy * 0.21f - wx * 0.19f) * 1.2f;
                    float fx = Mathf.Clamp((wx + ox) / map.Cell - 0.5f, 0, n - 1.001f), fy = Mathf.Clamp((wy + oy) / map.Cell - 0.5f, 0, n - 1.001f);
                    int ix = (int)fx, iy = (int)fy; float tx = fx - ix, ty = fy - iy;
                    int ix1 = Mathf.Min(ix + 1, n - 1), iy1 = Mathf.Min(iy + 1, n - 1);
                    float land = 1, sum = 0;
                    var lb = new float[7];
                    foreach (int bi in SoftSet)
                    {
                        var f = soft[bi];
                        if (bi is (int)Biome.Water or (int)Biome.Beach)   // кромка воды без сдвига — совпадает с физикой берега
                        {
                            float gx = Mathf.Clamp(wx / map.Cell - 0.5f, 0, n - 1.001f), gy = Mathf.Clamp(wy / map.Cell - 0.5f, 0, n - 1.001f);
                            int jx = (int)gx, jy = (int)gy, jx1 = Mathf.Min(jx + 1, n - 1), jy1 = Mathf.Min(jy + 1, n - 1); float ux = gx - jx, uy = gy - jy;
                            lb[bi] = Mathf.Lerp(Mathf.Lerp(f[jy * n + jx], f[jy * n + jx1], ux), Mathf.Lerp(f[jy1 * n + jx], f[jy1 * n + jx1], ux), uy);
                        }
                        else lb[bi] = Mathf.Lerp(Mathf.Lerp(f[iy * n + ix], f[iy * n + ix1], tx), Mathf.Lerp(f[iy1 * n + ix], f[iy1 * n + ix1], tx), ty);
                        sum += lb[bi];
                    }
                    if (sum > 0.001f) foreach (int bi in SoftSet) acc[bi] = lb[bi] / sum * land;
                }

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
                _hard[my * ms + mx] = (byte)(Mathf.Clamp(dirt + gravel + pad, 0, 1) * 255);
                float h = map.Heights[y * n + x];
                mc.SetPixel(mx, my, new Color(acc[(int)Biome.Water], Mathf.Clamp(h * 0.5f + 0.5f, 0, 1), Mathf.Clamp(Bilerp(shore, n, (mx + 0.5f) / S - 0.5f, (my + 0.5f) / S - 0.5f) / 24f, 0, 1), map.River != null && map.River[y * n + x] is 1 or 3 ? 0.5f : 1f));   // A: 0,5 — река и озёра (тихая вода, без прибоя)   // плавно между клетками: без «ступенек» мели у берега
            }
        }

        static readonly int[] SoftSet = { (int)Biome.Grassland, (int)Biome.Forest, (int)Biome.Arid, (int)Biome.Tundra, (int)Biome.Swamp, (int)Biome.Beach, (int)Biome.Water };

        /// <summary>Доли биомов суши по клеткам, размытые двумя раздельными box-проходами радиуса r (≈ треугольное ядро). Вода/пляж не участвуют.</summary>
        static float[][] SoftBiomes(MapInfo map, int r0)
        {
            int n = map.N; var res = new float[7][]; var tmp = new float[n * n];
            foreach (int bi in SoftSet)
            {
                int r = bi is (int)Biome.Beach or (int)Biome.Water ? 1 : r0;   // пляж/берег и кромка воды — узкие: размываем слабо
                var f = new float[n * n];
                for (int i = 0; i < n * n; i++) f[i] = (int)map.Biomes[i] == bi ? 1 : 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int y = 0; y < n; y++) { float acc = 0; int row = y * n; for (int x = -r; x < n; x++) { if (x + r < n) acc += f[row + x + r]; if (x - r - 1 >= 0) acc -= f[row + x - r - 1]; if (x >= 0) tmp[row + x] = acc; } }
                    for (int x = 0; x < n; x++) { float acc = 0; for (int y = -r; y < n; y++) { if (y + r < n) acc += tmp[(y + r) * n + x]; if (y - r - 1 >= 0) acc -= tmp[(y - r - 1) * n + x]; if (y >= 0) f[y * n + x] = acc; } }
                }
                // в доли 0..1: без нормировки широко размытая суша «весила» в (2r+1)² раз больше узкой воды — узкая река рисовалась травой
                float norm = 1f / Mathf.Pow(2 * r + 1, 4);
                for (int i = 0; i < n * n; i++) f[i] *= norm;
                res[bi] = f;
            }
            return res;
        }

        /// <summary>Расстояние от клетки воды до ближайшей суши, м (двухпроходная фаска 3-4); суша — 0. Отрицательное ±половина клетки, чтобы кромка была на границе.</summary>
        static float[] ShoreDist(MapInfo map)
        {
            int n = map.N;
            var d = new float[n * n];
            const float Inf = 1e9f, D1 = 1f, D2 = 1.4142f;
            for (int i = 0; i < d.Length; i++) d[i] = map.Biomes[i] == Biome.Water ? Inf : 0;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x; if (d[i] == 0) continue;
                float v = d[i];
                if (x > 0) v = Mathf.Min(v, d[i - 1] + D1);
                if (y > 0) { v = Mathf.Min(v, d[i - n] + D1); if (x > 0) v = Mathf.Min(v, d[i - n - 1] + D2); if (x < n - 1) v = Mathf.Min(v, d[i - n + 1] + D2); }
                d[i] = v;
            }
            for (int y = n - 1; y >= 0; y--)
            for (int x = n - 1; x >= 0; x--)
            {
                int i = y * n + x; if (d[i] == 0) continue;
                float v = d[i];
                if (x < n - 1) v = Mathf.Min(v, d[i + 1] + D1);
                if (y < n - 1) { v = Mathf.Min(v, d[i + n] + D1); if (x < n - 1) v = Mathf.Min(v, d[i + n + 1] + D2); if (x > 0) v = Mathf.Min(v, d[i + n - 1] + D2); }
                d[i] = v;
            }
            for (int i = 0; i < d.Length; i++) d[i] = d[i] >= Inf ? 999 : d[i] * map.Cell;
            return d;
        }

        static float Bilerp(float[] g, int n, float fx, float fy)
        {
            int x0 = Mathf.Clamp((int)Mathf.Floor(fx), 0, n - 1), y0 = Mathf.Clamp((int)Mathf.Floor(fy), 0, n - 1);
            int x1 = Mathf.Min(x0 + 1, n - 1), y1 = Mathf.Min(y0 + 1, n - 1);
            float tx = Mathf.Clamp(fx - x0, 0, 1), ty = Mathf.Clamp(fy - y0, 0, 1);
            float a = Mathf.Min(g[y0 * n + x0], 60), b = Mathf.Min(g[y0 * n + x1], 60), c = Mathf.Min(g[y1 * n + x0], 60), d = Mathf.Min(g[y1 * n + x1], 60);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        static float Box(Vector2 p, Vector2 min, Vector2 max, float fade) =>
            Mathf.Clamp(1f - p.Clamp(min, max).DistanceTo(p) / fade, 0, 1);

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0, 1);
            return p.DistanceTo(a + ab * t);
        }

        // твёрдая поверхность шейдера земли (тропа, гравий и бетон площадок) по маске, 0..255 — для декора (трава там не растёт)
        static byte[] _hard;
        static int _hardN;
        static float _hardStep, _hardHalf;

        /// <summary>Точка на тропе, гравии или бетоне земли (а не на биоме) — траве там не место.</summary>
        public static bool HardAt(SVec p)
        {
            if (_hard == null) return false;
            int x = (int)((p.X + _hardHalf) / _hardStep), y = (int)((p.Y + _hardHalf) / _hardStep);
            return x >= 0 && y >= 0 && x < _hardN && y < _hardN && _hard[y * _hardN + x] > 110;
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
