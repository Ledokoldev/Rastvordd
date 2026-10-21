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
        // трава своего цвета + мелочи и цветы по биому (повтор id = чаще встречается)
        const string M1 = "grass/grass_meadow_1", M2 = "grass/grass_meadow_2", F1 = "grass/grass_forest_1", F2 = "grass/grass_forest_2",
                     S1 = "grass/grass_swamp_1", S2 = "grass/grass_swamp_2", A1 = "grass/grass_arid_1", A2 = "grass/grass_arid_2",
                     T1 = "grass/grass_tundra_1", T2 = "grass/grass_tundra_2", B1 = "grass/grass_beach_1", B2 = "grass/grass_beach_2",
                     Bush = "plants/bush_1";
        static string C(string id) => "clutter/" + id;

        static readonly Dictionary<Biome, (int count, string[] ids)> Fill = new()
        {
            // в поле хлам редко (по одному на ~25 штук), иначе повторяется; трава и цветы — основа
            [Biome.Grassland] = (9, new[] { M1, M1, M1, M1, M1, M2, M2, M2, Bush, Bush, C("flowers_poppy"), C("flowers_poppy"), C("flowers_dandelion"), C("flowers_dandelion"),
                                            C("flowers_chamomile"), C("flowers_chamomile"), C("flowers_lupine"), M1, M2, C("clutter_tire"), C("clutter_planks"), C("clutter_bucket"),
                                            M1, M2, C("flowers_dandelion"), C("clutter_fence"), M1, M2, C("clutter_bicycle"), M1, C("clutter_sign"), M2 }),
            [Biome.Forest] = (8, new[] { F1, F1, F2, F2, Bush, C("clutter_stump"), C("clutter_log"), C("clutter_mushrooms"), C("clutter_mushrooms"), C("flowers_heather"), C("clutter_bones") }),
            [Biome.Swamp] = (9, new[] { S1, S1, S2, S2, C("clutter_log"), C("clutter_mushrooms"), C("clutter_bones"), C("clutter_stump") }),
            [Biome.Arid] = (5, new[] { A1, A1, A2, C("clutter_bones"), C("clutter_tire"), C("clutter_canister"), C("clutter_mattress"), C("clutter_cone"), C("clutter_sign") }),
            [Biome.Tundra] = (5, new[] { T1, T1, T2, C("flowers_snowdrop"), C("clutter_log"), C("clutter_stump"), C("clutter_bones") }),
            [Biome.Beach] = (5, new[] { B1, B1, B2, C("clutter_planks"), C("clutter_tire"), C("clutter_canister"), C("clutter_pallet"), C("clutter_bones") }),
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
            bool OnYard(SVec q) { foreach (var (mn, mx) in _map.Yards) if (q.X > mn.X + 1 && q.X < mx.X - 1 && q.Y > mn.Y + 1 && q.Y < mx.Y - 1) return true; return false; }

            for (int i = 0; i < fill.count; i++)
            {
                var p = new SVec(cx * Chunk + rng.Randf() * Chunk, cy * Chunk + rng.Randf() * Chunk);
                if (TerrainView.BiomeAt(_map, p) == Biome.Water) continue;
                if (OnYard(p)) continue;   // не на площадках РТ
                // биом берём в самой точке: на границе биомов трава смешивается естественно
                var here = TerrainView.BiomeAt(_map, p);
                var set = Fill.TryGetValue(here, out var f2) ? f2.ids : fill.ids;
                string texPath = set[rng.RandiRange(0, set.Length - 1)];
                var tex = Art.Get(texPath);
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
                    Material = set == null ? VisionFx.ObjectMat : (texPath.StartsWith("grass/") || texPath.StartsWith("plants/") || texPath.Contains("flowers_") ? VisionFx.GrassMat : VisionFx.ObjectMat),
                    Modulate = new Color(1, 1, 1, flat ? 0.85f : 1f) * (0.9f + rng.Randf() * 0.15f),
                };
                (flat ? _flatParent : _sortedParent).AddChild(s);
                list.Add(s);
            }
            return list;
        }
    }
}
