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

        /// <summary>Где декору не место (пол построек). Проверяется при создании и в RefreshBlocked.</summary>
        public System.Func<SVec, bool> Blocked;

        // полы построек, по которым декор прятали в прошлый раз (прямоугольники клеток и рамки треугольных фундаментов)
        HashSet<(float, float, float, float)> _floorRects = new();

        /// <summary>Постройки поменялись: прячем декор на полу, возвращаем там, где пол сломали.
        /// Проверяем только куски, которые задевает изменившийся пол: раньше на каждую стройку (20 ботов строят постоянно)
        /// перебирались все пучки всех кусков — по 5 проверок пола на каждый, это давало рывки.</summary>
        public void RefreshBlocked(IEnumerable<(SVec min, SVec max)> floors)
        {
            if (Blocked == null) return;
            var now = new HashSet<(float, float, float, float)>();
            foreach (var (mn, mx) in floors) now.Add((mn.X, mn.Y, mx.X, mx.Y));
            var changed = new List<(float, float, float, float)>();
            foreach (var r in now) if (!_floorRects.Contains(r)) changed.Add(r);
            foreach (var r in _floorRects) if (!now.Contains(r)) changed.Add(r);
            _floorRects = now;
            if (changed.Count == 0) return;   // поменялись стены, двери и т. п. — пол тот же
            foreach (var kv in _chunks)
            {
                float x0 = kv.Key.Item1 * Chunk - 1, y0 = kv.Key.Item2 * Chunk - 1, x1 = x0 + Chunk + 2, y1 = y0 + Chunk + 2;
                bool hit = false;
                foreach (var (a, b, c, d) in changed) if (a < x1 && c > x0 && b < y1 && d > y0) { hit = true; break; }
                if (!hit) continue;
                foreach (var n in kv.Value)
                    if (n is TuftRow tr) tr.Refresh(OnFloor);
                    else n.Visible = !OnFloor(new SVec(n.Position.X / Iso.PPM, n.Position.Y / Iso.PPM), n.HasMeta("hw") ? (float)n.GetMeta("hw") : 0.3f);
            }
        }

        /// <summary>Декор задевает пол постройки: проверяем основание и края спрайта (раньше — только точку основания, края торчали на фундаменте).</summary>
        bool OnFloor(SVec p, float halfW) =>
            Blocked != null && (Blocked(p) || Blocked(p + new SVec(-halfW, 0)) || Blocked(p + new SVec(halfW, 0)) || Blocked(p + new SVec(0, -0.4f)) || Blocked(p + new SVec(0, 0.3f)));

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
        public static bool CoverOn = true;   // густая трава-спрайты (можно выключить для слабых машин)
        public DecalLayer(MapInfo map, Node2D flatParent, Node2D sortedParent)
        {
            _map = map;
            _flatParent = flatParent;
            _sortedParent = sortedParent;
            // участки РТ и дороги с твёрдым покрытием (асфальт, бетон, гравий, полы, плитка) — по клеткам, чтобы не перебирать все
            for (int i = 0; i < map.Patches.Count; i++)
            {
                var pt = map.Patches[i];
                if (!HardTile(pt.Tile) && pt.Road == 0) continue;
                var (mn, mx) = (pt.Min, pt.Max);
                if (pt.Poly != null && pt.Poly.Length > 0)
                {
                    mn = mx = pt.Poly[0];
                    foreach (var v in pt.Poly) { mn = SVec.Min(mn, v); mx = SVec.Max(mx, v); }
                }
                _hardBox[i] = (mn, mx);
                for (int y = Mathf.FloorToInt(mn.Y / Chunk); y <= Mathf.FloorToInt(mx.Y / Chunk); y++)
                for (int x = Mathf.FloorToInt(mn.X / Chunk); x <= Mathf.FloorToInt(mx.X / Chunk); x++)
                {
                    if (!_hardCells.TryGetValue((x, y), out var l)) _hardCells[(x, y)] = l = new List<int>();
                    l.Add(i);
                }
            }
        }

        readonly Dictionary<(int, int), List<int>> _hardCells = new();
        readonly Dictionary<int, (SVec min, SVec max)> _hardBox = new();

        /// <summary>Мягкая земля (трава, лес, болото) — участок, где трава может расти; остальное твёрдое.</summary>
        static bool HardTile(string tile) =>
            tile != null && !(tile.Contains("grass") || tile.Contains("forest") || tile.Contains("swamp") || tile.Contains("moss") || tile.Contains("lawn") || tile.Contains("meadow"));

        /// <summary>Точка на твёрдом участке РТ или дороги (асфальт, бетон, гравий, полы, плитка, плиты).</summary>
        bool OnHardPatch(SVec q)
        {
            if (!_hardCells.TryGetValue((Mathf.FloorToInt(q.X / Chunk), Mathf.FloorToInt(q.Y / Chunk)), out var list)) return false;
            foreach (int i in list)
            {
                var (mn, mx) = _hardBox[i];
                if (q.X < mn.X - 0.3f || q.X > mx.X + 0.3f || q.Y < mn.Y - 0.3f || q.Y > mx.Y + 0.3f) continue;
                var poly = _map.Patches[i].Poly;
                if (poly == null || poly.Length < 3) return true;
                bool inside = false;
                for (int a = 0, b = poly.Length - 1; a < poly.Length; b = a++)
                    if ((poly[a].Y > q.Y) != (poly[b].Y > q.Y) && q.X < (poly[b].X - poly[a].X) * (q.Y - poly[a].Y) / (poly[b].Y - poly[a].Y) + poly[a].X) inside = !inside;
                if (inside) return true;
            }
            return false;
        }

        /// <summary>Густота низкой травы по биому (пучков на кусок 12×12 м): в пустыне — редкая сухая, на пляже и в снегу пустошей нет.</summary>
        static int CoverOf(Biome b) => b switch { Biome.Grassland => 260, Biome.Swamp => 200, Biome.Forest => 120, Biome.Tundra => 50, Biome.Arid => 14, _ => 0 };

        /// <summary>Куски вокруг камеры: ближний круг (он на экране) строится сразу, остальные — не дольше budgetMs за кадр,
        /// ближние первыми (раньше все недостающие куски строились в одном кадре — рывок при входе в новые места).</summary>
        public void Update(Vector2 cameraPx, double budgetMs = 3)
        {
            int cx = Mathf.FloorToInt(cameraPx.X / Iso.PPM / Chunk), cy = Mathf.FloorToInt(cameraPx.Y / Iso.PPM / Chunk);
            BuildAround(cx, cy, budgetMs, 1);

            var drop = new List<(int, int)>();
            foreach (var kv in _chunks)
                if (Mathf.Abs(kv.Key.Item1 - cx) > Radius + 1 || Mathf.Abs(kv.Key.Item2 - cy) > Radius + 1) drop.Add(kv.Key);
            foreach (var k in drop)
            {
                foreach (var n in _chunks[k]) n.QueueFree();
                _chunks.Remove(k);
            }
        }

        /// <summary>Загрузка (LoadingScreen): достроить куски вокруг точки за budgetMs. true — всё готово.</summary>
        public bool Prebuild(Vector2 cameraPx, double budgetMs) =>
            BuildAround(Mathf.FloorToInt(cameraPx.X / Iso.PPM / Chunk), Mathf.FloorToInt(cameraPx.Y / Iso.PPM / Chunk), budgetMs, -1);

        /// <summary>Строит недостающие куски кольцами от центра; кольца до forceRing — без ограничения по времени.</summary>
        bool BuildAround(int cx, int cy, double budgetMs, int forceRing)
        {
            ulong t0 = Time.GetTicksUsec();
            bool all = true;
            int built = 0;
            for (int r = 0; r <= Radius; r++)
                for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (System.Math.Max(System.Math.Abs(x - cx), System.Math.Abs(y - cy)) != r || _chunks.ContainsKey((x, y))) continue;
                    if (r > forceRing && built > 0 && (Time.GetTicksUsec() - t0) / 1000.0 >= budgetMs) { all = false; continue; }
                    _chunks[(x, y)] = Build(x, y);
                    built++;
                }
            return all;
        }

        List<Node2D> Build(int cx, int cy)
        {
            var list = new List<Node2D>();
            var rng = new RandomNumberGenerator { Seed = (ulong)(uint)(cx * 73856093 ^ cy * 19349663) };
            var center = new SVec((cx + 0.5f) * Chunk, (cy + 0.5f) * Chunk);
            var biome = TerrainView.BiomeAt(_map, center);
            if (!Fill.TryGetValue(biome, out var fill)) return list;
            bool OnYard(SVec q)
            {
                foreach (var (mn, mx) in _map.Yards) if (q.X > mn.X + 1 && q.X < mx.X - 1 && q.Y > mn.Y + 1 && q.Y < mx.Y - 1) return true;
                if (OnHardPatch(q)) return true;   // дороги мира (RoadNet.cs) и твёрдые участки РТ: асфальт, бетон, гравий, полы
                if (TerrainView.HardAt(q)) return true;   // тропа, гравий и бетон в шейдере земли
                foreach (var r in _map.Rooms) if (q.X > r.Min.X - 0.6f && q.X < r.Max.X + 0.6f && q.Y > r.Min.Y - 0.6f && q.Y < r.Max.Y + 0.6f) return true;   // в зданиях РТ (и у стен)
                return false;
            }

            // куртины: трава растёт пятнами (центр + 6–14 кустиков вокруг), между ними — просветы; плюс одиночный разброс
            var spots = new List<(SVec p, int kind)>();   // 0 — разброс, 1 — куртина, 2 — низкая трава
            int clumps = 0;   // круглые куртины-«кочки» убраны: траву даёт низкий покров из пучков (kind 2)
            for (int c = 0; c < clumps; c++)
            {
                var cc = new SVec(cx * Chunk + rng.Randf() * Chunk, cy * Chunk + rng.Randf() * Chunk);
                float rad = rng.RandfRange(1.2f, 3f);
                int n = rng.RandiRange(6, 14);
                for (int k = 0; k < n; k++)
                {
                    float a = rng.Randf() * Mathf.Tau, d = rad * Mathf.Sqrt(rng.Randf());
                    spots.Add((cc + new SVec(Mathf.Cos(a) * d, Mathf.Sin(a) * d * 0.8f), 1));
                }
            }
            for (int i = 0; i < fill.count; i++) spots.Add((new SVec(cx * Chunk + rng.Randf() * Chunk, cy * Chunk + rng.Randf() * Chunk), 0));
            // сплошной травяной покров (как в Project Zomboid): густые объёмные пучки по всей клетке, плотность по под-клеткам 3×3
            int cover = CoverOf(biome);
            if (CoverOn && cover > 0)
                for (int sy = 0; sy < 3; sy++)
                for (int sx = 0; sx < 3; sx++)
                {
                    float dens = rng.Randf();                       // поляна, редкая трава или проплешина
                    int n = (int)(cover / 9f * (dens < 0.2f ? 0.15f : dens < 0.5f ? 0.7f : 1.4f));
                    for (int k = 0; k < n; k++)
                        spots.Add((new SVec((cx + (sx + rng.Randf()) / 3f) * Chunk, (cy + (sy + rng.Randf()) / 3f) * Chunk), 2));
                }
            spots.Sort((a, b) => a.p.Y.CompareTo(b.p.Y));
            var rows = new Dictionary<int, TuftRow>();
            foreach (var (p, kind) in spots)
            {
                if (TerrainView.BiomeAt(_map, p) == Biome.Water) continue;
                if (OnYard(p)) continue;   // не на площадках РТ
                // биом берём в самой точке: на границе биомов трава смешивается естественно
                var here = TerrainView.BiomeAt(_map, p);
                // низкая трава на границе биомов: густота по месту, а не по центру куска (луговая трава не заходит на пляж и в пустыню)
                if (kind == 2 && here != biome && rng.Randf() * cover >= CoverOf(here)) continue;
                var set = Fill.TryGetValue(here, out var f2) ? f2.ids : fill.ids;
                string texPath = set[rng.RandiRange(0, set.Length - 1)];
                if (kind == 2) { string tb = here switch { Biome.Forest => "forest", Biome.Arid or Biome.Tundra => "dry", Biome.Swamp => "swamp", _ => "meadow" }; texPath = $"grass/tuft_{tb}_{rng.RandiRange(1, 12)}"; }
                else if (texPath.StartsWith("grass/grass_") && here == Biome.Beach) continue;   // на песке пляжа травы нет
                else if (texPath.StartsWith("grass/grass_")) { string tb = here switch { Biome.Forest => "forest", Biome.Arid or Biome.Tundra => "dry", Biome.Swamp => "swamp", _ => "meadow" }; texPath = $"grass/tuft_{tb}_{rng.RandiRange(1, 12)}"; }   // старые куртины-кочки → пучки
                else if (kind == 1 && !texPath.StartsWith("grass/")) { var gs = System.Array.FindAll(set, t => t.StartsWith("grass/")); if (gs.Length > 0) texPath = gs[rng.RandiRange(0, gs.Length - 1)]; }
                var tex = Art.Get(texPath);
                if (tex == null) continue;

                if (texPath.StartsWith("grass/tuft_"))
                {
                    // низкая трава: ряд по 0,5 м — один узел (раньше каждый пучок — свой Sprite2D, ~9 тыс. узлов, −20 fps)
                    int row = Mathf.FloorToInt(p.Y * 2f);
                    if (!rows.TryGetValue(row, out var tr))
                    {
                        tr = new TuftRow { Position = new Vector2(cx * Chunk * Iso.PPM, (row + 1) * 0.5f * Iso.PPM), Material = VisionFx.GrassMat, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
                        rows[row] = tr;
                    }
                    float thw = tex.GetWidth() / 2f / Iso.PPM;
                    tr.Add(tex, Iso.Px(p).Floor() - tr.Position, rng.Randf() < 0.5f, new Color(1, 1, 1) * (0.9f + rng.Randf() * 0.15f), p, thw, OnFloor(p, thw));
                    continue;
                }

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
                float hw = tex.GetWidth() / 2f / Iso.PPM;
                s.SetMeta("hw", hw);
                if (OnFloor(p, hw)) s.Visible = false;   // на полу постройки (или краем на нём)
                (flat ? _flatParent : _sortedParent).AddChild(s);
                list.Add(s);
            }
            foreach (var tr in rows.Values) { _sortedParent.AddChild(tr); list.Add(tr); }
            return list;
        }
    }

    /// <summary>Ряд низкой травы (полоса 0,5 м в клетке декора): все пучки рисуются одним узлом, сортировка по низу полосы.</summary>
    public partial class TuftRow : Node2D
    {
        readonly List<(Texture2D tex, Vector2 at, bool flip, Color mod, SVec world, float hw, bool hidden)> _items = new();

        public void Add(Texture2D tex, Vector2 at, bool flip, Color mod, SVec world, float hw, bool hidden) => _items.Add((tex, at, flip, mod, world, hw, hidden));

        /// <summary>Постройки поменялись: пучки на полу прячем, где пол сломали — возвращаем.</summary>
        public void Refresh(System.Func<SVec, float, bool> onFloor)
        {
            bool changed = false;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                bool h = onFloor(it.world, it.hw);
                if (h != it.hidden) { it.hidden = h; _items[i] = it; changed = true; }
            }
            if (changed) QueueRedraw();
        }

        public override void _Draw()
        {
            foreach (var it in _items)
            {
                if (it.hidden) continue;
                float w = it.tex.GetWidth(), h = it.tex.GetHeight();
                var r = new Rect2(it.at.X - w / 2f, it.at.Y - h + 1, w, h);
                if (it.flip) r = new Rect2(r.Position.X + w, r.Position.Y, -w, h);
                DrawTextureRect(it.tex, r, false, it.mod);
            }
        }
    }
}
