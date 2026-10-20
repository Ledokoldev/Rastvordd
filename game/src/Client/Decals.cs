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

        // веса по биому: tuft1, tuft2, fern, flowers | pebbles, leaves, puddle, debris; и сколько штук на кусок
        static readonly Dictionary<Biome, (int count, int[] w)> Fill = new()
        {
            [Biome.Grassland] = (20, new[] { 30, 25, 3, 18, 10, 8, 2, 4 }),
            [Biome.Forest] = (22, new[] { 15, 8, 30, 3, 6, 35, 2, 1 }),
            [Biome.Swamp] = (18, new[] { 20, 25, 15, 2, 2, 8, 30, 3 }),
            [Biome.Tundra] = (8, new[] { 5, 30, 5, 0, 45, 5, 0, 10 }),
            [Biome.Arid] = (9, new[] { 0, 40, 0, 2, 40, 3, 0, 15 }),
            [Biome.Beach] = (7, new[] { 0, 25, 0, 0, 40, 0, 0, 35 }),
        };

        static readonly string[] Ids = { "decal_grass_tuft_1", "decal_grass_tuft_2", "decal_fern", "decal_flowers", "decal_pebbles", "decal_leaves", "decal_puddle", "decal_debris" };

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
            int sum = 0;
            foreach (var v in fill.w) sum += v;

            for (int i = 0; i < fill.count; i++)
            {
                var p = new SVec(cx * Chunk + rng.Randf() * Chunk, cy * Chunk + rng.Randf() * Chunk);
                if (TerrainView.BiomeAt(_map, p) == Biome.Water) continue;
                if (p.X > 4 && p.X < 36 && p.Y > -36 && p.Y < -4) continue;   // не на площадке РТ
                int r = rng.RandiRange(0, sum - 1), id = 0;
                for (; id < fill.w.Length; id++) { if (r < fill.w[id]) break; r -= fill.w[id]; }
                var tex = Art.Get("decals/" + Ids[Mathf.Min(id, Ids.Length - 1)]);
                if (tex == null) continue;

                bool flat = id >= 4;
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
