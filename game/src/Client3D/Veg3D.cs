using System.Collections.Generic;
using Godot;
using Rastvordd.Client;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client3D
{
    /// <summary>
    /// Трава и цветы вокруг игрока: пучки из нескольких травинок (MultiMesh на клетку 16 м), качаются на ветру.
    /// Густота и цвет — по биому; на дорогах, тропах, площадках РТ, пляже и в воде не растёт.
    /// </summary>
    public partial class Veg3D : Node3D
    {
        const float CellM = 16f;
        public float Radius = 56f;
        MapInfo _map;
        bool[] _paved;           // клетки карты, накрытые участками (дороги, плиты, кафель)
        readonly Dictionary<(int, int), Node3D> _cells = new();
        (int, int) _last = (int.MinValue, 0);
        readonly Queue<(int, int)> _todo = new();
        ArrayMesh _tuft, _flower;
        readonly Dictionary<Biome, ShaderMaterial> _mats = new();
        ShaderMaterial _flowerMat;

        const string GrassShader = @"
shader_type spatial;
render_mode cull_disabled;
uniform vec4 base_col : source_color = vec4(0.25, 0.35, 0.15, 1.0);
uniform vec4 tip_col : source_color = vec4(0.55, 0.65, 0.3, 1.0);
uniform float sway = 0.09;
void vertex() {
    float h = UV.y;
    vec3 w = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
    VERTEX.x += sin(TIME * 1.7 + w.x * 0.35 + w.z * 0.21) * sway * h;
    VERTEX.z += cos(TIME * 1.3 + w.z * 0.31 - w.x * 0.12) * sway * 0.6 * h;
}
void fragment() {
    ALBEDO = mix(base_col.rgb, tip_col.rgb, clamp(UV.y, 0.0, 1.0));
    ROUGHNESS = 0.95;
}
";

        public void Build(MapInfo map)
        {
            _map = map;
            int n = map.N;
            _paved = new bool[n * n];
            foreach (var p in map.Patches)
            {
                SVec mn = p.Min, mx = p.Max;
                Vector2[] poly = null;
                if (p.Poly != null && p.Poly.Length >= 3)
                {
                    poly = new Vector2[p.Poly.Length];
                    mn = mx = p.Poly[0];
                    for (int i = 0; i < poly.Length; i++) { poly[i] = new Vector2(p.Poly[i].X, p.Poly[i].Y); mn = SVec.Min(mn, p.Poly[i]); mx = SVec.Max(mx, p.Poly[i]); }
                }
                int x0 = Cell(mn.X - 1), x1 = Cell(mx.X + 1), y0 = Cell(mn.Y - 1), y1 = Cell(mx.Y + 1);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (x < 0 || y < 0 || x >= n || y >= n) continue;
                    var c = new Vector2((x + 0.5f) * map.Cell - map.HalfSize, (y + 0.5f) * map.Cell - map.HalfSize);
                    bool inside = poly != null ? Geometry2D.IsPointInPolygon(c, poly) : c.X >= mn.X - 0.5f && c.X <= mx.X + 0.5f && c.Y >= mn.Y - 0.5f && c.Y <= mx.Y + 0.5f;
                    if (inside) _paved[y * n + x] = true;
                }
            }
            foreach (var (mn, mx) in map.Yards)
                for (int y = Cell(mn.Y); y <= Cell(mx.Y); y++)
                for (int x = Cell(mn.X); x <= Cell(mx.X); x++)
                    if (x >= 0 && y >= 0 && x < n && y < n) _paved[y * n + x] = true;

            _tuft = Tuft(6, 0.55f, 0.16f);
            _flower = Tuft(3, 0.35f, 0.08f, true);
            var shader = new Shader { Code = GrassShader };
            ShaderMaterial M(Color b, Color t, float sway = 0.09f)
            {
                var m = new ShaderMaterial { Shader = shader };
                m.SetShaderParameter("base_col", b);
                m.SetShaderParameter("tip_col", t);
                m.SetShaderParameter("sway", sway);
                return m;
            }
            _mats[Biome.Grassland] = M(new Color(0.24f, 0.34f, 0.13f), new Color(0.56f, 0.66f, 0.30f));
            _mats[Biome.Forest] = M(new Color(0.16f, 0.25f, 0.12f), new Color(0.36f, 0.48f, 0.22f));
            _mats[Biome.Arid] = M(new Color(0.42f, 0.36f, 0.20f), new Color(0.72f, 0.64f, 0.40f));
            _mats[Biome.Tundra] = M(new Color(0.36f, 0.40f, 0.34f), new Color(0.70f, 0.74f, 0.66f));
            _mats[Biome.Swamp] = M(new Color(0.20f, 0.26f, 0.12f), new Color(0.45f, 0.50f, 0.24f), 0.05f);
            _flowerMat = M(new Color(0.25f, 0.4f, 0.15f), new Color(0.95f, 0.88f, 0.45f));
        }

        int Cell(float m) => (int)((m + _map.HalfSize) / _map.Cell);

        /// <summary>Пучок травинок: узкие треугольники веером, UV.y — доля высоты (для ветра и цвета к кончику).</summary>
        static ArrayMesh Tuft(int blades, float h, float spread, bool flower = false)
        {
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            var rng = new RandomNumberGenerator { Seed = (ulong)(blades * 31 + (flower ? 7 : 0)) };
            for (int i = 0; i < blades; i++)
            {
                float a = i * Mathf.Tau / blades + rng.RandfRange(-0.3f, 0.3f);
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var side = new Vector3(-dir.Z, 0, dir.X) * 0.035f;
                var root = dir * rng.RandfRange(0, spread * 0.5f);
                float bh = h * rng.RandfRange(0.65f, 1.15f);
                var tip = root + dir * spread * rng.RandfRange(0.4f, 1f) + Vector3.Up * bh;
                st.SetNormal(Vector3.Up);
                st.SetUV(new Vector2(0, 0)); st.AddVertex(root - side);
                st.SetUV(new Vector2(1, 0)); st.AddVertex(root + side);
                st.SetUV(new Vector2(0.5f, 1)); st.AddVertex(tip);
                if (flower)
                {
                    // головка цветка — маленький ромб на кончике (кончик красится «tip»-цветом)
                    var up = Vector3.Up * 0.05f; var s2 = side * 1.6f;
                    st.SetUV(new Vector2(0, 1)); st.AddVertex(tip - s2);
                    st.SetUV(new Vector2(0, 1)); st.AddVertex(tip + up);
                    st.SetUV(new Vector2(0, 1)); st.AddVertex(tip + s2);
                }
            }
            return st.Commit();
        }

        public void Step(SVec me)
        {
            if (_map == null) return;
            var c = ((int)Mathf.Floor(me.X / CellM), (int)Mathf.Floor(me.Y / CellM));
            if (c != _last)
            {
                _last = c;
                int r = Mathf.CeilToInt(Radius / CellM);
                _todo.Clear();
                var want = new List<((int, int) k, float d)>();
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var k = (c.Item1 + dx, c.Item2 + dy);
                    float d = new Vector2((k.Item1 + 0.5f) * CellM - me.X, (k.Item2 + 0.5f) * CellM - me.Y).Length();
                    if (d <= Radius && !_cells.ContainsKey(k)) want.Add((k, d));
                }
                want.Sort((a, b) => a.d.CompareTo(b.d));
                foreach (var (k, _) in want) _todo.Enqueue(k);
                var drop = new List<(int, int)>();
                foreach (var k in _cells.Keys)
                    if (new Vector2((k.Item1 + 0.5f) * CellM - me.X, (k.Item2 + 0.5f) * CellM - me.Y).Length() > Radius + CellM * 1.5f) drop.Add(k);
                foreach (var k in drop) { var n = _cells[k]; if (n != null) { RemoveChild(n); n.QueueFree(); } _cells.Remove(k); }
            }
            for (int i = 0; i < 3 && _todo.Count > 0; i++)
            {
                var k = _todo.Dequeue();
                if (_cells.ContainsKey(k)) continue;
                _cells[k] = BuildCell(k);
                if (_cells[k] != null) AddChild(_cells[k]);
            }
        }

        Node3D BuildCell((int, int) k)
        {
            var rng = new RandomNumberGenerator { Seed = (ulong)((k.Item1 * 73856093) ^ (k.Item2 * 19349663)) };
            var per = new Dictionary<Biome, List<Transform3D>>();
            var flowers = new List<Transform3D>();
            const float step = 1.1f;
            int n = _map.N;
            for (float y = 0; y < CellM; y += step)
            for (float x = 0; x < CellM; x += step)
            {
                var p = new SVec(k.Item1 * CellM + x + rng.RandfRange(0, step), k.Item2 * CellM + y + rng.RandfRange(0, step));
                int gx = Cell(p.X), gy = Cell(p.Y);
                if (gx < 0 || gy < 0 || gx >= n || gy >= n) continue;
                int gi = gy * n + gx;
                var b = _map.Biomes[gi];
                if (b is Biome.Water or Biome.Beach || _paved[gi] || TerrainView.HardAt(p)) continue;
                if (_map.River != null && gi < _map.River.Length && _map.River[gi] != 0) continue;
                float density = b switch { Biome.Grassland => 0.85f, Biome.Forest => 0.6f, Biome.Swamp => 0.7f, Biome.Arid => 0.25f, Biome.Tundra => 0.35f, _ => 0.5f };
                if (rng.Randf() > density) continue;
                var xf = new Transform3D(new Basis(Vector3.Up, rng.RandfRange(0, Mathf.Tau)) * Basis.FromScale(Vector3.One * rng.RandfRange(0.7f, 1.35f)), W3.V(p, 0));
                if (b == Biome.Grassland && rng.Randf() < 0.06f) flowers.Add(xf);
                else
                {
                    if (!per.TryGetValue(b, out var list)) per[b] = list = new List<Transform3D>();
                    list.Add(xf);
                }
            }
            if (per.Count == 0 && flowers.Count == 0) return null;
            var root = new Node3D();
            foreach (var (b, list) in per)
                root.AddChild(Multi(_tuft, list, _mats.TryGetValue(b, out var m) ? m : _mats[Biome.Grassland]));
            if (flowers.Count > 0) root.AddChild(Multi(_flower, flowers, _flowerMat));
            return root;
        }

        static MultiMeshInstance3D Multi(Mesh mesh, List<Transform3D> xfs, Material mat)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh };
            mm.InstanceCount = xfs.Count;
            for (int i = 0; i < xfs.Count; i++) mm.SetInstanceTransform(i, xfs[i]);
            return new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        }
    }
}
