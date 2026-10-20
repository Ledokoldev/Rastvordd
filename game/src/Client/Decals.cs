using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>
    /// Мелочи на земле (пучки травы, папоротник, камешки, листья, цветы, лужи, мусор), чтобы поверхность не была однотонной.
    /// Только для красоты, без коллизий. Раскладываются кусками вокруг камеры, детерминированно по координатам и биому.
    /// Плоские (лужи, листья) — под всем; объёмные (трава, папоротник) — в y-sort вместе с миром.
    /// </summary>
    public sealed class DecalLayer
    {
        const float Chunk = 12f;
        const int Radius = 3;   // кусков вокруг камеры

        readonly MapInfo _map;
        readonly Node2D _flatParent, _sortedParent;
        readonly Dictionary<(int, int), List<Node2D>> _chunks = new();

        static readonly string[] Flat = { "decal_pebbles", "decal_leaves", "decal_puddle", "decal_debris" };

        // крупные куртины травы своего цвета в каждом биоме (мелкие декали в пикселе были кляксами)
        static readonly Dictionary<Biome, (int count, string[] ids)> Fill = new()
        {
            [Biome.Grassland] = (6, new[] { "grass/grass_meadow_1", "grass/grass_meadow_1", "grass/grass_meadow_2", "plants/bush_1" }),
            [Biome.Forest] = (5, new[] { "grass/grass_forest_1", "grass/grass_forest_2", "grass/grass_forest_1", "plants/bush_1" }),
            [Biome.Swamp] = (7, new[] { "grass/grass_swamp_1", "grass/grass_swamp_2", "grass/grass_swamp_1" }),
            [Biome.Arid] = (3, new[] { "grass/grass_arid_1", "grass/grass_arid_1", "grass/grass_arid_2" }),
            [Biome.Tundra] = (3, new[] { "grass/grass_tundra_1", "grass/grass_tundra_2" }),
            [Biome.Beach] = (3, new[] { "grass/grass_beach_1", "grass/grass_beach_2" }),
        };
        public DecalLayer(MapInfo map, Node2D flatParent, Node2D sortedParent)
        {
            _map = map;
            _flatParent = flatParent;
            _sortedParent = sortedParent;
        }

        public void Update(Vector2 cameraPx)
        {
            int cx = Mathf.FloorToInt(cameraPx.X / Iso.PPM / Chunk), cy = Mathf.FloorToInt(cameraPx.Y / Iso.PPM / Chunk);
            for (int y = cy - Radius; y <= cy + Radius; y++)
            for (int x = cx - Radius; x <= cx + Radius; x++)
                if (!_chunks.ContainsKey((x, y))) _chunks[(x, y)] = Build(x, y);

            var drop = new List<(int, int)>();
            foreach (var kv in _chunks)
                if (Mathf.Abs(kv.Key.Item1 - cx) > Radius + 1 || Mathf.Abs(kv.Key.Item2 - cy) > Radius + 1) drop.Add(kv.Key);
            foreach (var k in drop)
            {
                foreach (var n in _chunks[k]) n.QueueFree();
                _chunks.Remove(k);
            }
        }

        List<Node2D> Build(int cx, int cy)
        {
            var list = new List<Node2D>();
            var rng = new RandomNumberGenerator { Seed = (ulong)(uint)(cx * 73856093 ^ cy * 19349663) };
            var center = new SVec((cx + 0.5f) * Chunk, (cy + 0.5f) * Chunk);
            var biome = TerrainView.BiomeAt(_map, center);
            if (!Fill.TryGetValue(biome, out var fill)) return list;

            for (int i = 0; i < fill.count; i++)
            {
                var p = new SVec(cx * Chunk + rng.Randf() * Chunk, cy * Chunk + rng.Randf() * Chunk);
                if (TerrainView.BiomeAt(_map, p) == Biome.Water) continue;
                if (p.X > 4 && p.X < 36 && p.Y > -36 && p.Y < -4) continue;   // не на площадке РТ
                // биом берём в самой точке: на границе биомов трава смешивается естественно
                var here = TerrainView.BiomeAt(_map, p);
                var set = Fill.TryGetValue(here, out var f2) ? f2.ids : fill.ids;
                var tex = Art.Get(set[rng.RandiRange(0, set.Length - 1)]);
                if (tex == null) continue;

                bool flat = false;
                var s = new Sprite2D
                {
                    Texture = tex,
                    Centered = false,
                    Offset = new Vector2(-tex.GetWidth() / 2f, flat ? -tex.GetHeight() / 2f : -tex.GetHeight() + 1),
                    Position = Iso.Px(p).Floor(),
                    FlipH = rng.Randf() < 0.5f,
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                    Modulate = new Color(1, 1, 1, flat ? 0.85f : 1f) * (0.9f + rng.Randf() * 0.15f),
                };
                (flat ? _flatParent : _sortedParent).AddChild(s);
                list.Add(s);
            }
            return list;
        }
    }
}
