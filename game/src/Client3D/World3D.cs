using System.Collections.Generic;
using Godot;
using Rastvordd.Client;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client3D
{
    /// <summary>
    /// Остров: земля чанками с текстурами биомов (те же маски и плитки, что у 2D: TerrainView.BuildMasks),
    /// океан, участки (дороги, гравий, кафель, плиты). Симуляция плоская — земля ровная, вода опущена.
    /// </summary>
    public partial class Terrain3D : Node3D
    {
        const int Step = 2;          // одна вершина на 2 клетки (4 м)
        const int ChunkQuads = 50;   // квадов в чанке по стороне (200 м)
        public float ViewRange = 300f;

        const string TerrainShader = @"
shader_type spatial;
render_mode cull_disabled;
uniform sampler2D mask_a : filter_linear;   // r лес, g сухо, b тундра, a пляж
uniform sampler2D mask_b : filter_linear;   // r болото, g тропа, b гравий, a бетон площадки
uniform sampler2D mask_c : filter_linear;   // r вода
uniform sampler2D t_grass : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D t_forest : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D t_dry : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D t_snow : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D t_sand : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D t_swamp : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D t_dirt : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D t_gravel : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D t_concrete : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float map_half = 1000.0;
uniform float tile_m = 4.0;
uniform float snow_cover = 0.0;   // снег лёг (Snapshot.SnowCover)
uniform float wetness = 0.0;      // после дождя (Snapshot.Wetness) — темнее и с бликом
varying vec3 wpos;

void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }

void fragment() {
    vec2 uv = (wpos.xz + vec2(map_half)) / (map_half * 2.0);
    vec2 t = wpos.xz / tile_m;
    vec4 a = texture(mask_a, uv);
    vec4 b = texture(mask_b, uv);
    vec4 c = texture(mask_c, uv);
    float grass = max(0.0, 1.0 - (a.r + a.g + a.b + a.a + b.r + b.g + b.b + b.a + c.r));
    vec3 col = texture(t_grass, t).rgb * grass
        + texture(t_forest, t).rgb * a.r + texture(t_dry, t).rgb * a.g + texture(t_snow, t).rgb * a.b + texture(t_sand, t).rgb * a.a
        + texture(t_swamp, t).rgb * b.r + texture(t_dirt, t).rgb * b.g + texture(t_gravel, t).rgb * b.b + texture(t_concrete, t).rgb * b.a
        + texture(t_sand, t).rgb * vec3(0.55, 0.6, 0.55) * c.r;   // дно под водой
    float sum = grass + a.r + a.g + a.b + a.a + b.r + b.g + b.b + b.a + c.r;
    col /= max(sum, 0.001);
    float land = 1.0 - clamp(c.r * 2.0, 0.0, 1.0);
    col = mix(col, col * 0.62, wetness * land);
    float n = fract(sin(dot(floor(wpos.xz * 1.5), vec2(12.9898, 78.233))) * 43758.5453);
    float snow = smoothstep(0.0, 1.0, snow_cover * 1.3 - n * 0.3) * land;
    col = mix(col, vec3(0.9, 0.92, 0.95), snow);
    ALBEDO = col;
    ROUGHNESS = mix(0.96, 0.35, wetness * land * (1.0 - snow));
}
";

        ShaderMaterial _mat;
        float _snow = -1, _wet = -1;

        /// <summary>Погода на земле: снег и мокрость (меняются плавно, обновляем при заметной разнице).</summary>
        public void SetWeather(float snow, float wet)
        {
            if (_mat == null) return;
            if (Mathf.Abs(snow - _snow) > 0.01f) { _snow = snow; _mat.SetShaderParameter("snow_cover", snow); }
            if (Mathf.Abs(wet - _wet) > 0.01f) { _wet = wet; _mat.SetShaderParameter("wetness", wet); }
        }

        public void Build(MapInfo map)
        {
            int n = map.N;
            bool Wet(int x, int y)
            {
                x = System.Math.Clamp(x, 0, n - 1); y = System.Math.Clamp(y, 0, n - 1);
                int i = y * n + x;
                return map.Biomes[i] == Biome.Water || (map.River != null && i < map.River.Length && map.River[i] != 0);
            }

            // маски и плитки — как у 2D-земли, чтобы биомы, тропы и площадки РТ совпали один в один
            TerrainView.BuildMasks(map, out var ma, out var mb, out var mc);
            var mat = _mat = new ShaderMaterial { Shader = new Shader { Code = TerrainShader } };
            mat.SetShaderParameter("mask_a", ImageTexture.CreateFromImage(ma));
            mat.SetShaderParameter("mask_b", ImageTexture.CreateFromImage(mb));
            mat.SetShaderParameter("mask_c", ImageTexture.CreateFromImage(mc));
            void T(string param, string tile, string fallback) => mat.SetShaderParameter(param, Mats3D.Tex(tile) ?? Mats3D.Tex(fallback));
            T("t_grass", "tiles/ground_grass", "tiles/ground_dirt");
            T("t_forest", "tiles/ground_forest", "tiles/ground_dirt");
            T("t_dry", "tiles/ground_dry", "tiles/ground_sand");
            T("t_snow", "tiles/ground_snow", "tiles/ground_gravel");
            T("t_sand", "tiles/ground_sand", "tiles/ground_dirt");
            T("t_swamp", "tiles/ground_swamp", "tiles/ground_dirt");
            T("t_dirt", "tiles/ground_dirt", "tiles/ground_grass");
            T("t_gravel", "tiles/ground_gravel", "tiles/ground_dirt");
            T("t_concrete", "tiles/ground_concrete", "tiles/ground_gravel");
            mat.SetShaderParameter("map_half", map.HalfSize);

            int verts = n / Step;
            int chunks = (verts + ChunkQuads - 1) / ChunkQuads;
            for (int cy = 0; cy < chunks; cy++)
            for (int cx = 0; cx < chunks; cx++)
            {
                int q0x = cx * ChunkQuads, q0y = cy * ChunkQuads;
                int qw = System.Math.Min(ChunkQuads, verts - 1 - q0x), qh = System.Math.Min(ChunkQuads, verts - 1 - q0y);
                if (qw <= 0 || qh <= 0) continue;
                int vw = qw + 1, vh = qh + 1;
                var pos = new Vector3[vw * vh];
                var nor = new Vector3[vw * vh];
                bool anyLand = false;
                for (int y = 0; y < vh; y++)
                for (int x = 0; x < vw; x++)
                {
                    int gx = (q0x + x) * Step, gy = (q0y + y) * Step;
                    bool wet = Wet(gx, gy);
                    anyLand |= !wet;
                    int k = y * vw + x;
                    pos[k] = new Vector3(gx * map.Cell - map.HalfSize, wet ? -0.7f : 0f, gy * map.Cell - map.HalfSize);
                    nor[k] = Vector3.Up;
                }
                if (!anyLand) continue;   // открытое море — только плоскость океана
                var idx = new int[qw * qh * 6];
                int t = 0;
                for (int y = 0; y < qh; y++)
                for (int x = 0; x < qw; x++)
                {
                    int a = y * vw + x, b = a + 1, c = a + vw, d = c + 1;
                    idx[t++] = a; idx[t++] = b; idx[t++] = c;
                    idx[t++] = b; idx[t++] = d; idx[t++] = c;
                }
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = pos;
                arrays[(int)Mesh.ArrayType.Normal] = nor;
                arrays[(int)Mesh.ArrayType.Index] = idx;
                var mesh = new ArrayMesh();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = ViewRange });
            }

            // океан: полупрозрачный — у берега видно дно
            AddChild(new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(map.HalfSize * 2.6f, map.HalfSize * 2.6f) },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.10f, 0.25f, 0.32f, 0.82f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    Roughness = 0.08f, Metallic = 0.3f,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(0, -0.25f, 0),
            });

            BuildPatches(map);
        }

        /// <summary>Участки другой плиткой (дороги, обочины, гравий, кафель, плиты ПАГ) — по мешу на плитку.</summary>
        void BuildPatches(MapInfo map)
        {
            var groups = new Dictionary<(string tile, int layer), (List<Vector3> v, List<Vector2> uv)>();
            foreach (var p in map.Patches)
            {
                if (string.IsNullOrEmpty(p.Tile)) continue;
                // слои по высоте, чтобы не мерцали: обочина < грунтовка < асфальт < плиты < прочее (кафель в корпусах)
                int layer = p.Slab != 0 ? 4 : p.Road switch { 3 => 0, 2 => 1, 1 => 2, _ => 3 };
                if (!groups.TryGetValue((p.Tile, layer), out var g)) groups[(p.Tile, layer)] = g = (new List<Vector3>(), new List<Vector2>());
                float h = 0.012f + layer * 0.006f;
                void Tri(SVec a, SVec b, SVec c)
                {
                    foreach (var q in new[] { a, b, c }) { g.v.Add(W3.V(q, h)); g.uv.Add(new Vector2(q.X, q.Y) / 4f); }
                }
                if (p.Poly != null && p.Poly.Length >= 3)
                {
                    var poly = new Vector2[p.Poly.Length];
                    for (int i = 0; i < poly.Length; i++) poly[i] = new Vector2(p.Poly[i].X, p.Poly[i].Y);
                    var tri = Geometry2D.TriangulatePolygon(poly);
                    for (int i = 0; i + 2 < tri.Length; i += 3) Tri(p.Poly[tri[i]], p.Poly[tri[i + 1]], p.Poly[tri[i + 2]]);
                }
                else
                {
                    var a = p.Min; var c = p.Max; var b = new SVec(c.X, a.Y); var d = new SVec(a.X, c.Y);
                    Tri(a, b, c); Tri(a, c, d);
                }
            }
            foreach (var ((tile, _), g) in groups)
            {
                if (g.v.Count == 0) continue;
                var nor = new Vector3[g.v.Count];
                for (int i = 0; i < nor.Length; i++) nor[i] = Vector3.Up;
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = g.v.ToArray();
                arrays[(int)Mesh.ArrayType.Normal] = nor;
                arrays[(int)Mesh.ArrayType.TexUV] = g.uv.ToArray();
                var mesh = new ArrayMesh();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                var tex = Mats3D.Tex(tile);
                var m = new StandardMaterial3D
                {
                    AlbedoTexture = tex,
                    AlbedoColor = tex == null ? new Color(0.45f, 0.45f, 0.43f) : Colors.White,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                    TextureRepeat = true,
                    Roughness = 0.95f,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                };
                AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            }
        }
    }

    /// <summary>
    /// Статика карты: стены РТ с их текстурами (кирпич, бетон, профлист, рабица…) — по MultiMesh на текстуру,
    /// крыши на высоте стен под ними, провода, теплотрассы, крыльца. Стены-укрытия (машины, бочки, барьеры, ящики,
    /// хлам) рисует Decor3D моделями — их список в PropWalls. Разрушенные стены (GoneWalls) прячутся.
    /// </summary>
    public partial class MapStatic3D : Node3D
    {
        readonly Dictionary<int, (MultiMesh mm, int index, Transform3D xf)> _walls = new();
        readonly HashSet<int> _gone = new();
        readonly List<(Node3D node, SVec min, SVec max)> _roofs = new();
        public readonly List<WallInfo> PropWalls = new();
        /// <summary>Что стоит на крышах (вентиляция, трубы, антенны) — с высотой крыши; рисует Decor3D, прячется вместе с крышей.</summary>
        public readonly List<(string sprite, SVec pos, float h, int roof)> RoofItems = new();

        static bool IsProp(WallKind k) => k is WallKind.Car or WallKind.Barrier or WallKind.Junk or WallKind.Barrel or WallKind.WoodCrate;

        static string WallTex(WallInfo w) => w.Tex ?? w.Kind switch
        {
            WallKind.Crate => "walls/wallface_wood",
            WallKind.Rock => "walls/wallface_stone",
            _ => "walls/wallface_concrete",
        };

        public void Build(MapInfo map)
        {
            var groups = new Dictionary<string, List<WallInfo>>();
            foreach (var w in map.Walls)
            {
                if (w.Hidden || w.Kind == WallKind.Built) continue;   // хитбоксы декора и свои постройки рисуются отдельно
                if (IsProp(w.Kind)) { PropWalls.Add(w); continue; }
                string tex = WallTex(w);
                if (!groups.TryGetValue(tex, out var list)) groups[tex] = list = new List<WallInfo>();
                list.Add(w);
            }
            foreach (var (tex, list) in groups)
            {
                var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = W3.Box };
                mm.InstanceCount = list.Count;
                for (int i = 0; i < list.Count; i++)
                {
                    var w = list[i];
                    var c = (w.Min + w.Max) * 0.5f; var s = w.Max - w.Min;
                    float h = Mathf.Max(0.3f, w.Height);
                    var xf = new Transform3D(Basis.FromScale(new Vector3(Mathf.Max(0.05f, s.X), h, Mathf.Max(0.05f, s.Y))), W3.V(c, h * 0.5f));
                    mm.SetInstanceTransform(i, xf);
                    _walls[w.Id] = (mm, i, xf);
                }
                bool metal = tex.Contains("metal") || tex.Contains("corrugated") || tex.Contains("garage") || tex.Contains("shutter") || tex.Contains("profile");
                var mat = Mats3D.CutTex(Mats3D.Tex(tex), Colors.White, 3f, 0.6f, null, metal ? 0.55f : 0.92f, metal ? 0.35f : 0f);
                AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat });
            }

            BuildRoofs(map);
            BuildWires(map);
            BuildPipes(map);
            BuildEntrances(map);
        }

        /// <summary>Крыша — на высоте самых высоких стен под ней (иначе 3,2 м), с плиткой крыши; что на ней — в RoofItems.</summary>
        void BuildRoofs(MapInfo map)
        {
            for (int ri = 0; ri < map.Roofs.Count; ri++)
            {
                var r = map.Roofs[ri];
                float top = 0;
                foreach (var w in map.Walls)
                {
                    if (w.Hidden || w.Kind == WallKind.Built) continue;
                    if (w.Max.X < r.Min.X - 0.6f || w.Min.X > r.Max.X + 0.6f || w.Max.Y < r.Min.Y - 0.6f || w.Min.Y > r.Max.Y + 0.6f) continue;
                    top = Mathf.Max(top, w.Height);
                }
                if (top < 1.5f) top = 3.2f;
                var c = (r.Min + r.Max) * 0.5f; var s = r.Max - r.Min;
                var root = new Node3D();
                AddChild(root);
                var tex = Mats3D.Tex(string.IsNullOrEmpty(r.Tile) ? "tiles/roof_tar" : r.Tile);
                var mat = Mats3D.CutTex(tex, Colors.White, 4f, top - 0.6f, null, 0.92f);
                W3.AddBox(root, W3.V(c, top + 0.1f), new Vector3(s.X + 0.4f, 0.2f, s.Y + 0.4f), 0, mat);
                // парапет по краю — плоские советские крыши
                var par = Mats3D.CutTex(Mats3D.Tex("walls/wallface_concrete"), Colors.White, 3f, top - 0.6f);
                W3.AddBox(root, W3.V(new SVec(c.X, r.Min.Y - 0.15f), top + 0.32f), new Vector3(s.X + 0.4f, 0.25f, 0.12f), 0, par);
                W3.AddBox(root, W3.V(new SVec(c.X, r.Max.Y + 0.15f), top + 0.32f), new Vector3(s.X + 0.4f, 0.25f, 0.12f), 0, par);
                W3.AddBox(root, W3.V(new SVec(r.Min.X - 0.15f, c.Y), top + 0.32f), new Vector3(0.12f, 0.25f, s.Y + 0.4f), 0, par);
                W3.AddBox(root, W3.V(new SVec(r.Max.X + 0.15f, c.Y), top + 0.32f), new Vector3(0.12f, 0.25f, s.Y + 0.4f), 0, par);
                _roofs.Add((root, r.Min, r.Max));
                if (r.Items != null) foreach (var it in r.Items) RoofItems.Add((it.Sprite, it.Pos, top + 0.2f, _roofs.Count - 1));
            }
        }

        /// <summary>Отрезок-цилиндр между двумя точками (провода, трубы, опоры).</summary>
        public static MeshInstance3D Segment(Node3D parent, Vector3 a, Vector3 b, float radius, Material mat, bool shadows = true)
        {
            var d = b - a; float len = d.Length();
            if (len < 1e-3f) return null;
            var y = d / len;
            var x = Mathf.Abs(y.Y) < 0.95f ? y.Cross(Vector3.Up).Normalized() : y.Cross(Vector3.Right).Normalized();
            var z = x.Cross(y).Normalized();
            var m = new MeshInstance3D
            {
                Mesh = W3.Cylinder,
                MaterialOverride = mat,
                CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                Transform = new Transform3D(new Basis(x * radius * 2, y * len, z * radius * 2), (a + b) * 0.5f),
            };
            parent.AddChild(m);
            return m;
        }

        void BuildWires(MapInfo map)
        {
            var mat = Mats3D.Solid(new Color(0.12f, 0.12f, 0.12f), 0.6f, 0.4f);
            foreach (var w in map.Wires)
            {
                const int segs = 8;
                Vector3 prev = default;
                for (int i = 0; i <= segs; i++)
                {
                    float t = i / (float)segs;
                    var p = SVec.Lerp(w.A, w.B, t);
                    float h = Mathf.Lerp(w.Ha, w.Hb, t) - w.Sag * 4f * t * (1 - t);
                    var cur = W3.V(p, h);
                    if (i > 0) Segment(this, prev, cur, 0.02f, mat, false);
                    prev = cur;
                }
            }
        }

        void BuildPipes(MapInfo map)
        {
            var pipe = Mats3D.Solid(new Color(0.70f, 0.70f, 0.66f), 0.5f, 0.5f);
            var post = Mats3D.Solid(new Color(0.35f, 0.33f, 0.30f), 0.8f, 0.3f);
            foreach (var p in map.Pipes)
            {
                if (p.P == null || p.P.Length < 2) continue;
                for (int i = 0; i + 1 < p.P.Length; i++)
                {
                    float ha = p.H != null && i < p.H.Length ? p.H[i] : 0.8f, hb = p.H != null && i + 1 < p.H.Length ? p.H[i + 1] : 0.8f;
                    var a = W3.V(p.P[i], ha); var b = W3.V(p.P[i + 1], hb);
                    Segment(this, a, b, 0.22f, pipe);
                    // опоры каждые ~4 м
                    float len = (b - a).Length();
                    for (float s = 0; s < len; s += 4f)
                    {
                        var q = a.Lerp(b, s / Mathf.Max(len, 0.01f));
                        if (q.Y > 0.3f) W3.AddBox(this, new Vector3(q.X, q.Y * 0.5f - 0.1f, q.Z), new Vector3(0.12f, q.Y - 0.2f, 0.12f), 0, post);
                    }
                }
            }
        }

        /// <summary>Крыльцо со ступенькой или пандус у входа в корпус РТ.</summary>
        void BuildEntrances(MapInfo map)
        {
            var mat = Mats3D.CutTex(Mats3D.Tex("tiles/ground_concrete"), new Color(0.9f, 0.9f, 0.88f), 3f, 2f);
            foreach (var e in map.Entrances)
            {
                var out2 = e.Side switch { 0 => new SVec(0, 1), 1 => new SVec(0, -1), 2 => new SVec(1, 0), _ => new SVec(-1, 0) };
                float width = (e.B - e.A).Length() + 0.6f, depth = Mathf.Max(0.8f, e.Depth);
                var c = (e.A + e.B) * 0.5f + out2 * (depth * 0.5f);
                bool alongX = e.Side is 0 or 1;
                var size = alongX ? new Vector3(width, 0.18f, depth) : new Vector3(depth, 0.18f, width);
                W3.AddBox(this, W3.V(c, 0.09f), size, 0, mat);
                if (!e.Ramp)
                {
                    var sc = (e.A + e.B) * 0.5f + out2 * (depth + 0.2f);
                    W3.AddBox(this, W3.V(sc, 0.05f), alongX ? new Vector3(width, 0.1f, 0.4f) : new Vector3(0.4f, 0.1f, width), 0, mat);
                }
            }
        }

        public void SetGone(List<int> gone)
        {
            if (gone == null) return;
            foreach (var id in gone)
            {
                if (!_gone.Add(id) || !_walls.TryGetValue(id, out var w)) continue;
                w.mm.SetInstanceTransform(w.index, new Transform3D(Basis.FromScale(Vector3.Zero), w.xf.Origin));
            }
        }

        public bool GoneWall(int id) => _gone.Contains(id);

        public Node3D RoofNode(int i) => i >= 0 && i < _roofs.Count ? _roofs[i].node : null;

        /// <summary>Крыша над персонажем исчезает целиком (как в Zomboid, когда заходишь в дом). Возвращает номер этой крыши или −1.</summary>
        public int UpdateRoofs(SVec me)
        {
            int inside = -1;
            for (int i = 0; i < _roofs.Count; i++)
            {
                var (node, mn, mx) = _roofs[i];
                bool under = me.X > mn.X && me.X < mx.X && me.Y > mn.Y && me.Y < mx.Y;
                node.Visible = !under;
                if (under) inside = i;
            }
            return inside;
        }
    }
}
