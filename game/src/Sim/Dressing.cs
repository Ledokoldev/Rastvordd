using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace Rastvordd.Sim
{
    /// <summary>Комната готовой РТ (для клиента: покраска стен в два тона по типу комнаты).</summary>
    public struct MapRoom { public Vector2 Min, Max; public string Kind; }

    /// <summary>Наземная теплотрасса: ломаная по точкам на своей высоте (П-образный подъём над дорогой — две точки с разной высотой в одном месте).</summary>
    public struct MapPipe { public Vector2[] P; public float[] H; }

    /// <summary>Живое на РТ: дым, пар, флаг, вороны, бумажки, качающаяся лампа, свечка (рисует и двигает клиент).</summary>
    public struct MapAmbient { public string Kind; public Vector2 Pos; public float H; }

    /// <summary>
    /// Обстановка РТ (docs/design/11-monuments.md, «Обстановка»): по комнатам (найденным по стенам под крышами) раскладывает
    /// предметы слоями — стена (плакаты, часы, батареи), мебель (по правилам типа комнаты, с хитбоксом), мелочь на столах,
    /// мусор и декали на полу, 1–2 «истории» на здание. Снаружи — бурьян и грязь у стен, тропы из плит, теплотрассы, живое.
    /// Всё детерминированно (своё зерно на РТ), проходы между дверями и подходы к луту сохраняются (проверка связности).
    /// </summary>
    public sealed partial class World
    {
        public readonly List<MapRoom> Rooms = new();
        public readonly List<MapPipe> Pipes = new();
        public readonly List<MapAmbient> Ambients = new();
        /// <summary>Сводка обстановки по РТ (для отчёта: --dressreport).</summary>
        public readonly List<string> DressReport = new();
        readonly Dictionary<Monument, (string key, List<(Vector2 p, string kind)> tags)> _dressInfo = new();

        /// <summary>Запомнить РТ для обстановки: ключ (gai, ats, radio…) и метки типов комнат из макета ("rooms": [[x, y, "kind"], …]).</summary>
        void RegisterDress(Monument m, string key, Vector2 o, JsonElement? root, params (float x, float y, string kind)[] extra)
        {
            var tags = new List<(Vector2, string)>();
            if (root is JsonElement r && r.TryGetProperty("rooms", out var rooms))
                foreach (var t in rooms.EnumerateArray()) tags.Add((o + new Vector2(t[0].GetSingle(), t[1].GetSingle()), t[2].GetString()));
            foreach (var (x, y, k) in extra) tags.Add((o + new Vector2(x, y), k));
            _dressInfo[m] = (key, tags);
        }

        // ---------- сетка здания ----------

        const byte GFree = 0, GWall = 1, GBarrier = 2, GSolid = 3, GDoor = 4;

        sealed class DressGrid
        {
            public const float S = 0.25f;
            public Vector2 O;
            public int W, H;
            public byte[] K;
            public float[] FaceY;        // у клеток стены: y южной грани (для того, что висит на стене)
            public bool[] Window;        // клетка стены с окном
            public bool[] Res;           // мебель с хитбоксом сюда нельзя (лут, двери, проходы)
            public bool[] Occ;           // уже что-то стоит (спрайт без хитбокса)
            public bool[] Cov;           // пол чем-то покрыт (для плотности)
            public int[] Comp;
            public int Idx(int i, int j) => j * W + i;
            public bool In(int i, int j) => i >= 0 && j >= 0 && i < W && j < H;
            public Vector2 C(int i, int j) => O + new Vector2((i + 0.5f) * S, (j + 0.5f) * S);
            public (int i, int j) Cell(Vector2 p) => ((int)MathF.Floor((p.X - O.X) / S), (int)MathF.Floor((p.Y - O.Y) / S));
        }

        sealed class DressRoom
        {
            public DressGrid G;
            public int Id;
            public List<int> Cells = new();
            public Vector2 Min, Max;
            public string Kind;
            public float Area;
            public List<int> DoorCells = new();       // клетки комнаты у проёмов
            public List<int> Need = new();            // что должно оставаться достижимым (двери, лут)
            public bool Exterior;                     // есть выход наружу
        }

        // ---------- комплекты комнат ----------

        /// <summary>Мебель: спрайт, сколько (мин–макс), где (N — у северной стены, S — у южной, WE — у боковых, C — посередине), стулья.</summary>
        sealed class KitItem
        {
            public string S; public int Min, Max; public string Where; public bool Chairs; public bool Run;
            public KitItem(string s, int min, int max, string where, bool chairs = false, bool run = false) { S = s; Min = min; Max = max; Where = where; Chairs = chairs; Run = run; }
        }

        sealed class Kit
        {
            public KitItem[] Big = Array.Empty<KitItem>();
            public string[] Wall = Array.Empty<string>();
            public string[] Clutter = Array.Empty<string>();
            public string[] Floor = Array.Empty<string>();
            public string[] Tops = Array.Empty<string>();
            public float Radiators = 0.5f;
            public string[] Islands = Array.Empty<string>();   // «острова» посреди комнаты: крупное с хитбоксом + мелочь вокруг
            public string IslandFloor;                          // что под островом (коврик, пятно масла, картон)
        }

        static readonly string[] Posters = { "interior/wall_poster_worker", "interior/wall_poster_safety", "interior/wall_poster_space", "interior/wall_poster_pinup" };
        static readonly string[] GenericFloor = { "decals/floor_grime", "decals/floor_grime", "decals/floor_stain", "decals/decal_debris", "decals/floor_plaster", "decals/floor_papers", "decals/decal_cigarette_butts" };
        static readonly string[] GenericClutter = { "clutter/clutter_planks", "clutter/clutter_bucket", "interior/cardboard_pile", "decals/floor_bottles", "decals/floor_cans", "interior/top_cans", "interior/top_bottles" };

        static readonly Dictionary<string, Kit> Kits = new()
        {
            ["barracks"] = new Kit
            {
                Islands = new[] { "furniture/bed_iron_side", "furniture/bed_iron_side", "furniture/cot_folding_side", "furniture/table_kitchen" }, IslandFloor = "decals/floor_rug",
                Big = new[]
                {
                    new KitItem("furniture/bed_iron_side", 2, 8, "WE", run: true), new KitItem("furniture/bunk_bed_side", 0, 2, "WE", run: true),
                    new KitItem("furniture/locker_metal", 2, 5, "N", run: true), new KitItem("furniture/wardrobe_single", 0, 1, "N"),
                    new KitItem("furniture/table_kitchen", 1, 1, "C", chairs: true), new KitItem("interior/bedside_table", 1, 3, "S"),
                },
                Wall = new[] { "interior/wall_poster_worker", "interior/wall_poster_space", "interior/wall_clock", "interior/wall_pennant", "interior/wall_calendar", "interior/wall_portrait", "furniture/int_notice_board" },
                Clutter = new[] { "clutter/int_boots_pair", "furniture/stool_wood", "clutter/clutter_bucket", "clutter/clutter_mattress", "interior/chair_overturned" },
                Floor = new[] { "decals/floor_rags", "decals/floor_bottles", "decals/floor_cans", "decals/decal_cigarette_butts", "decals/floor_shoes", "decals/floor_rug", "decals/floor_lino_patch" },
                Tops = new[] { "interior/top_mug", "interior/top_ashtray", "interior/top_cards", "interior/top_bottles", "interior/top_candle", "interior/top_books", "interior/top_plates" },
                Radiators = 0.8f,
            },
            ["office"] = new Kit
            {
                Islands = new[] { "furniture/table_office", "furniture/table_office", "furniture/int_sofa_office", "furniture/armchair", "interior/filing_cabinet" }, IslandFloor = "decals/floor_rug",
                Big = new[]
                {
                    new KitItem("furniture/table_office", 1, 3, "NC", chairs: true), new KitItem("furniture/bookcase", 0, 2, "N", run: true),
                    new KitItem("interior/filing_cabinet", 1, 2, "N", run: true), new KitItem("furniture/safe_old", 0, 1, "N"),
                    new KitItem("furniture/wardrobe_single", 0, 1, "N"), new KitItem("furniture/int_sofa_office", 0, 1, "S"),
                },
                Wall = new[] { "interior/wall_portrait", "interior/wall_map", "interior/wall_calendar", "interior/wall_clock", "interior/wall_pennant", "interior/wall_poster_worker", "furniture/int_notice_board", "interior/wall_shelf_small" },
                Clutter = new[] { "furniture/int_dead_plant", "furniture/coat_rack", "outdoor/trash_urn", "interior/cardboard_pile", "interior/top_box", "interior/top_papers", "furniture/chair_office" },
                Floor = new[] { "decals/floor_papers", "decals/floor_rug", "decals/floor_lino_patch", "decals/floor_stain", "decals/decal_carpet_red" },
                Tops = new[] { "interior/top_papers", "interior/top_papers", "interior/top_phone", "interior/top_lamp", "interior/top_typewriter", "interior/top_ashtray", "interior/top_mug", "interior/top_books" },
                Radiators = 0.7f,
            },
            ["control"] = new Kit
            {
                Islands = new[] { "furniture/table_office", "outdoor/electric_cabinet", "props/ats_cable_tray_stack" }, IslandFloor = "decals/decal_cables",
                Big = new[]
                {
                    new KitItem("furniture/table_office", 1, 2, "N", chairs: true), new KitItem("outdoor/electric_cabinet", 1, 2, "N", run: true),
                    new KitItem("interior/filing_cabinet", 0, 1, "N"), new KitItem("furniture/shelf_metal_rack", 0, 1, "N"),
                },
                Wall = new[] { "interior/wall_switchboard_small", "interior/wall_electric_box", "interior/wall_clock", "interior/wall_poster_safety", "interior/wall_map", "furniture/int_notice_board" },
                Clutter = new[] { "furniture/chair_office", "furniture/int_fire_extinguisher", "interior/top_papers", "interior/top_box", "props/ats_cable_bundle" },
                Floor = new[] { "decals/decal_cables", "decals/floor_papers", "decals/floor_lino_patch", "decals/decal_cigarette_butts" },
                Tops = new[] { "interior/top_papers", "interior/top_mug", "interior/top_ashtray", "interior/top_radio", "interior/top_lamp", "interior/top_phone" },
                Radiators = 0.4f,
            },
            ["workshop"] = new Kit
            {
                Islands = new[] { "furniture/table_workshop", "props/gar_engine_stand", "props/barrel_oil", "props/gar_tire_stack", "props/crate_military", "props/box_large" }, IslandFloor = "decals/decal_oil_stain",
                Big = new[]
                {
                    new KitItem("furniture/table_workshop", 1, 2, "N"), new KitItem("furniture/tool_rack", 0, 1, "N"), new KitItem("furniture/shelf_metal_rack", 1, 2, "N", run: true),
                    new KitItem("props/gar_shelf_jars", 0, 1, "N"), new KitItem("props/gar_engine_stand", 0, 1, "C"), new KitItem("props/barrel_oil", 0, 2, "S"),
                },
                Wall = new[] { "interior/wall_tool_board", "interior/wall_tool_board", "interior/wall_poster_safety", "interior/wall_poster_pinup", "interior/wall_calendar", "interior/wall_electric_box", "interior/wall_clock" },
                Clutter = new[] { "clutter/clutter_canister", "clutter/clutter_tire", "clutter/clutter_bucket", "interior/bucket_mop", "clutter/clutter_planks", "furniture/stool_metal", "interior/cardboard_pile", "interior/top_tools" },
                Floor = new[] { "decals/decal_oil_stain", "decals/decal_oil_stain_big", "decals/floor_rags", "decals/decal_debris", "decals/decal_puddle_oily", "decals/floor_wood_chips" },
                Tops = new[] { "interior/top_tools", "interior/top_tools", "interior/top_jars", "interior/top_cans", "interior/top_kettle", "interior/top_bottles", "interior/top_radio" },
                Radiators = 0.2f,
            },
            ["storage"] = new Kit
            {
                Islands = new[] { "props/box_large", "props/crate_military", "furniture/shelf_metal_rack", "props/crate_wood", "props/box_large" }, IslandFloor = "decals/floor_wood_chips",
                Big = new[]
                {
                    new KitItem("furniture/shelf_metal_rack", 2, 5, "NWE", run: true), new KitItem("props/box_large", 0, 2, "S"), new KitItem("props/crate_military", 0, 2, "S"),
                },
                Wall = new[] { "interior/wall_poster_safety", "interior/wall_calendar", "interior/wall_electric_box" },
                Clutter = new[] { "interior/cardboard_pile", "interior/cardboard_pile", "clutter/clutter_pallet", "clutter/clutter_planks", "interior/top_box", "clutter/clutter_bucket", "props/crate_wood" },
                Floor = new[] { "decals/floor_wood_chips", "decals/floor_plaster", "decals/floor_papers" },
                Tops = new[] { "interior/top_box", "interior/top_jars", "interior/top_cans" },
                Radiators = 0.15f,
            },
            ["kitchen"] = new Kit
            {
                Islands = new[] { "furniture/table_kitchen_oilcloth", "furniture/table_round_dining" }, IslandFloor = "decals/floor_lino_patch",
                Big = new[]
                {
                    new KitItem("furniture/table_kitchen_oilcloth", 1, 1, "C", chairs: true), new KitItem("furniture/stove_gas", 1, 1, "N"), new KitItem("furniture/sink_kitchen", 1, 1, "N"),
                    new KitItem("furniture/fridge_zil", 0, 1, "N"), new KitItem("furniture/sideboard_servant", 0, 1, "N"), new KitItem("furniture/shelf_wall_wood", 0, 1, "N"),
                },
                Wall = new[] { "interior/wall_calendar", "interior/wall_clock", "interior/wall_shelf_small", "interior/wall_poster_pinup", "interior/wall_shelf_small" },
                Clutter = new[] { "furniture/stool_wood", "clutter/clutter_bucket", "interior/top_cans", "interior/top_bottles", "interior/top_kettle" },
                Floor = new[] { "decals/floor_lino_patch", "decals/floor_stain", "decals/floor_cans", "decals/floor_rug", "decals/floor_bottles" },
                Tops = new[] { "interior/top_plates", "interior/top_kettle", "interior/top_mug", "interior/top_jars", "interior/top_bottles", "interior/top_cans" },
                Radiators = 0.6f,
            },
            ["washroom"] = new Kit
            {
                Big = new[] { new KitItem("furniture/washbasin", 1, 3, "N", run: true), new KitItem("furniture/toilet", 0, 2, "N", run: true), new KitItem("furniture/bathtub_side", 0, 1, "S") },
                Wall = new[] { "interior/wall_mirror", "interior/wall_mold", "interior/wall_mirror" },
                Clutter = new[] { "interior/bucket_mop", "clutter/clutter_bucket", "decals/floor_rags" },
                Floor = new[] { "decals/floor_leak", "decals/floor_stain", "decals/floor_stain", "decals/floor_rags" },
                Radiators = 0.3f,
            },
            ["corridor"] = new Kit
            {
                Big = new[] { new KitItem("furniture/bench_indoor", 0, 1, "NS") },
                Wall = new[] { "furniture/int_notice_board", "interior/wall_poster_worker", "interior/wall_poster_space", "interior/wall_clock", "interior/wall_map" },
                Clutter = new[] { "furniture/coat_rack", "outdoor/trash_urn", "furniture/int_dead_plant", "furniture/int_fire_extinguisher", "interior/bucket_mop" },
                Floor = new[] { "decals/floor_rug_runner", "decals/floor_papers", "decals/decal_leaves", "decals/floor_shoes" },
                Radiators = 0.6f,
            },
            ["electrical"] = new Kit
            {
                Islands = new[] { "props/subst_cable_drum", "props/ats_cable_tray_stack", "outdoor/electric_cabinet" }, IslandFloor = "decals/decal_cables",
                Big = new[] { new KitItem("outdoor/electric_cabinet", 1, 3, "N", run: true), new KitItem("props/subst_relay_cabinet", 0, 2, "NWE", run: true) },
                Wall = new[] { "interior/wall_switchboard_small", "interior/wall_electric_box", "interior/wall_poster_safety", "interior/wall_switchboard_small" },
                Clutter = new[] { "props/ats_cable_bundle", "furniture/int_fire_extinguisher", "clutter/clutter_bucket" },
                Floor = new[] { "decals/decal_cables", "decals/floor_stain", "decals/decal_debris" },
                Radiators = 0.1f,
            },
            ["medbay"] = new Kit
            {
                Islands = new[] { "furniture/hospital_bed_side", "furniture/table_office" }, IslandFloor = "decals/floor_stain",
                Big = new[]
                {
                    new KitItem("furniture/hospital_bed_side", 1, 3, "WE", run: true), new KitItem("furniture/medical_cabinet", 1, 1, "N"), new KitItem("furniture/washbasin", 0, 1, "N"),
                    new KitItem("furniture/table_office", 0, 1, "N", chairs: true), new KitItem("interior/filing_cabinet", 0, 1, "N"),
                },
                Wall = new[] { "interior/wall_first_aid", "interior/wall_poster_safety", "interior/wall_clock", "interior/wall_calendar" },
                Clutter = new[] { "furniture/stool_metal", "clutter/clutter_bucket", "interior/top_medkit" },
                Floor = new[] { "decals/floor_bandages", "decals/floor_stain", "decals/floor_papers", "decals/floor_blood" },
                Tops = new[] { "interior/top_medkit", "interior/top_papers", "interior/top_mug" },
                Radiators = 0.7f,
            },
            ["shop"] = new Kit
            {
                Islands = new[] { "furniture/shelf_metal_rack", "props/bottle_crates", "props/box_large", "props/beer_kegs" }, IslandFloor = "decals/decal_trash_scatter",
                Big = new[] { new KitItem("furniture/shelf_metal_rack", 0, 2, "N", run: true) },
                Wall = new[] { "interior/wall_poster_pinup", "interior/wall_poster_worker", "interior/wall_calendar", "interior/wall_clock" },
                Clutter = new[] { "interior/cardboard_pile", "interior/top_box", "props/bottle_crates", "interior/top_bottles", "interior/top_cans", "clutter/clutter_pallet" },
                Floor = new[] { "decals/floor_bottles", "decals/floor_cans", "decals/decal_glass_shards", "decals/floor_papers", "decals/decal_trash_scatter" },
                Radiators = 0.4f,
            },
        };

        /// <summary>Размер декали (диаметр «пятна», м) — для учёта плотности.</summary>
        static float DecalSize(string s) => s switch
        {
            "decals/floor_dirt" => 1.5f, "decals/floor_grime" => 3.2f, "decals/floor_grime_2" => 3.6f, "decals/floor_lino_patch" => 1.1f, "decals/floor_rug" => 1.0f, "decals/floor_rug_runner" => 1.3f,
            "decals/floor_stain" => 0.9f, "decals/floor_leak" => 1.1f, "decals/floor_blood" => 1.0f, "decals/decal_oil_stain" => 1.4f, "decals/decal_oil_stain_big" => 1.1f,
            "decals/decal_puddle_oily" => 1.3f, "decals/decal_cables" => 1.1f, "decals/decal_trash_scatter" => 1.3f,
            _ => 0.7f,
        };

        static (float w, float d, float h, bool legs)? FootOf(string sprite)
        {
            string id = sprite[(sprite.LastIndexOf('/') + 1)..];
            if (Foot.TryGetValue(id, out var f) || Tier0Layouts.Foot.TryGetValue(id, out f) || Tier1Layouts.Foot.TryGetValue(id, out f)
                || Tier2Layouts.Foot.TryGetValue(id, out f) || DressFoot.Foot.TryGetValue(id, out f)) return f;
            return null;
        }

        static bool HasTop(string sprite) => sprite.Contains("table_") || sprite.Contains("int_desk") || sprite.Contains("int_table") || sprite.Contains("workbench")
            || sprite.Contains("school_desk") || sprite.Contains("dresser") || sprite.Contains("bedside_table") || sprite.Contains("operator_desk") || sprite.Contains("duty_desk");

        // ---------- главное ----------

        void DressAllPrefabs()
        {
            // отладка: --nodress — РТ как до обстановки (снимки «до/после»)
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--nodress") >= 0) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _prefabMode = true;
            foreach (var m in Prefabs)
            {
                if (!_dressInfo.TryGetValue(m, out var info)) continue;
                int h = 17;
                foreach (char c in m.Name) h = unchecked(h * 31 + c);
                var rng = new Random(Seed * 7919 + h);
                int d0 = Decors.Count, w0 = Walls.Count;
                var stats = DressMonument(m, info.key, info.tags, rng);
                int flat = 0, spr = 0;
                for (int i = d0; i < Decors.Count; i++) if (Decors[i].Flat) flat++; else spr++;
                DressReport.Add($"{m.Name}: комнат {stats.rooms}, пол занят {stats.before:P0} → {stats.after:P0}, декалей +{flat}, спрайтов +{spr}, мебели с хитбоксом +{Walls.Count - w0}, теплотрасс {stats.pipes}");
            }
            _prefabMode = false;
            DressReport.Add($"обстановка всех РТ: {sw.ElapsedMilliseconds} мс");
        }

        (int rooms, float before, float after, int pipes) DressMonument(Monument m, string key, List<(Vector2 p, string kind)> tags, Random rng)
        {
            if (Environment.GetEnvironmentVariable("NOWINGS") == null) AddWings(m, tags, rng);
            var roofs = Roofs.FindAll(r => m.Contains((r.Min + r.Max) * 0.5f));
            var allRooms = new List<DressRoom>();
            float covBefore = 0, covAfter = 0, area = 0;
            foreach (var roof in roofs)
            {
                var g = BuildDressGrid(roof.Min, roof.Max);
                var rooms = FindRooms(g, allRooms.Count);
                foreach (var r in rooms)
                {
                    r.Kind = RoomKind(r, key, tags, roof);
                    Rooms.Add(new MapRoom { Min = r.Min, Max = r.Max, Kind = r.Kind });
                    covBefore += Coverage(r) * r.Area;
                }
                foreach (var r in rooms)
                {
                    int before = Decors.Count;
                    DressRoomLayers(r, rng);
                    int hung = 0; for (int i = before; i < Decors.Count; i++) if (Decors[i].Lift > 0.5f && Decors[i].Sprite.Contains("wall_")) hung++;
                    DressReport.Add($"   {r.Kind} {r.Min - m.Min}–{r.Max - m.Min} {r.Area:0} м², двери {r.Need.Count}, на стене {hung}, северных отрезков {WallRuns(r, 0).Count}");
                }
                // истории: 1–2 на здание, в просторных комнатах
                var big = rooms.FindAll(r => r.Area >= 10);
                int stories = big.Count == 0 ? 0 : (roof.Max - roof.Min).X * (roof.Max - roof.Min).Y > 150 ? 2 : 1;
                var used = new HashSet<DressRoom>();
                for (int s = 0; s < stories && big.Count > 0; s++)
                {
                    var r = big[rng.Next(big.Count)];
                    if (used.Contains(r) && big.Count > 1) { s--; big.Remove(r); continue; }
                    used.Add(r);
                    Vignette(r, rng, key);
                }
                foreach (var r in rooms) { FillFloor(r, rng); covAfter += Coverage(r) * r.Area; area += r.Area; }
                allRooms.AddRange(rooms);
            }
            if (key == "gai") GaiCheckpoint(m, rng);
            AddAnnexes(m, roofs, rng);
            DressExterior(m, roofs, rng);
            AddPerimeter(m, rng);
            int pipes = AddHeatingMains(m, roofs, rng);
            AddAmbients(m, roofs, allRooms, rng);
            return (allRooms.Count, area > 0 ? covBefore / area : 0, area > 0 ? covAfter / area : 0, pipes);
        }

        // ---------- сетка: стены, проёмы, комнаты ----------

        DressGrid BuildDressGrid(Vector2 min, Vector2 max)
        {
            const float S = DressGrid.S;
            var g = new DressGrid { O = min, W = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / S)), H = Math.Max(1, (int)MathF.Ceiling((max.Y - min.Y) / S)) };
            int n = g.W * g.H;
            g.K = new byte[n]; g.FaceY = new float[n]; Array.Fill(g.FaceY, float.MinValue); g.Window = new bool[n]; g.Res = new bool[n]; g.Occ = new bool[n]; g.Cov = new bool[n]; g.Comp = new int[n];
            var lo = min - new Vector2(0.4f); var hi = max + new Vector2(0.4f);
            foreach (var w in Walls)
            {
                if (!w.Prefab) continue;
                var a = w.Wall.Min; var b = w.Wall.Max;
                if (b.X < lo.X || a.X > hi.X || b.Y < lo.Y || a.Y > hi.Y) continue;
                bool door = IsGateTex(w.Tex) || RemovableWalls.Contains(w);
                bool win = w.PassBullets || (w.Tex != null && (w.Tex.Contains("window") || w.Tex.Contains("storefront") || w.Tex.Contains("shop_bars")));
                byte k = w.Hidden ? GSolid : door ? GDoor : GWall;
                MarkRect(g, a, b, k, 0.02f, w.Wall.Max.Y, win);
            }
            foreach (var bag in Bags) Reserve(g, bag.Pos, 1.1f);
            foreach (var p in Pickups) Reserve(g, p.Pos, 0.9f);
            foreach (var s in AllStashes()) Reserve(g, s.Pos, 1.2f);
            foreach (var nd in Nodes)
            {
                if (nd.Pos.X < lo.X || nd.Pos.X > hi.X || nd.Pos.Y < lo.Y || nd.Pos.Y > hi.Y) continue;
                MarkRect(g, nd.Pos - new Vector2(nd.Radius), nd.Pos + new Vector2(nd.Radius), GSolid, 0, 0, false);
                Reserve(g, nd.Pos, nd.Radius + 0.8f);
            }
            foreach (var d in Deployables) Reserve(g, d.Pos, 1.6f);
            // спрайты без хитбокса, что уже стоят (из макета): тоже «занято»
            foreach (var d in Decors)
            {
                if (d.Flat || d.Pos.X < min.X || d.Pos.X > max.X || d.Pos.Y < min.Y || d.Pos.Y > max.Y) continue;
                var f = FootOf(d.Sprite);
                float fw = f?.w ?? 0.6f, fd = f?.d ?? 0.4f;
                for (int j = 0; j < g.H; j++)
                    for (int i = 0; i < g.W; i++)
                    {
                        var c = g.C(i, j);
                        if (c.X > d.Pos.X - fw / 2 && c.X < d.Pos.X + fw / 2 && c.Y > d.Pos.Y - fd && c.Y < d.Pos.Y + 0.05f) { g.Occ[g.Idx(i, j)] = true; g.Cov[g.Idx(i, j)] = true; }
                    }
            }
            // проёмы между комнатами: от торца стены — луч вдоль неё до другой стены (≤ 2,6 м) — это дверь
            foreach (var w in Walls)
            {
                if (!w.Prefab || w.Hidden) continue;
                var a = w.Wall.Min; var b = w.Wall.Max;
                if (b.X < min.X - 0.2f || a.X > max.X + 0.2f || b.Y < min.Y - 0.2f || a.Y > max.Y + 0.2f) continue;
                bool horiz = b.X - a.X >= b.Y - a.Y;
                var c = (a + b) * 0.5f;
                if (horiz) { CastBarrier(g, new Vector2(a.X, c.Y), new Vector2(-1, 0)); CastBarrier(g, new Vector2(b.X, c.Y), new Vector2(1, 0)); }
                else { CastBarrier(g, new Vector2(c.X, a.Y), new Vector2(0, -1)); CastBarrier(g, new Vector2(c.X, b.Y), new Vector2(0, 1)); }
            }
            return g;
        }

        static void MarkRect(DressGrid g, Vector2 a, Vector2 b, byte k, float pad, float faceY, bool win)
        {
            var (i0, j0) = g.Cell(a - new Vector2(pad)); var (i1, j1) = g.Cell(b + new Vector2(pad));
            for (int j = Math.Max(0, j0); j <= Math.Min(g.H - 1, j1); j++)
                for (int i = Math.Max(0, i0); i <= Math.Min(g.W - 1, i1); i++)
                {
                    var c = g.C(i, j);
                    if (c.X < a.X - pad - DressGrid.S * 0.5f || c.X > b.X + pad + DressGrid.S * 0.5f || c.Y < a.Y - pad - DressGrid.S * 0.5f || c.Y > b.Y + pad + DressGrid.S * 0.5f) continue;
                    int id = g.Idx(i, j);
                    // стена важнее: хитбокс пропса не затирает стену, дверь — тоже
                    if (g.K[id] == GWall || (g.K[id] == GDoor && k == GSolid)) continue;
                    g.K[id] = k;
                    if (k == GWall || k == GDoor) { g.FaceY[id] = MathF.Max(g.FaceY[id], faceY); g.Window[id] |= win; }
                }
        }

        static void Reserve(DressGrid g, Vector2 p, float r)
        {
            var (ci, cj) = g.Cell(p);
            int rr = (int)MathF.Ceiling(r / DressGrid.S);
            for (int j = cj - rr; j <= cj + rr; j++)
                for (int i = ci - rr; i <= ci + rr; i++)
                    if (g.In(i, j) && Vector2.Distance(g.C(i, j), p) <= r) g.Res[g.Idx(i, j)] = true;
        }

        static void CastBarrier(DressGrid g, Vector2 from, Vector2 dir)
        {
            const float S = DressGrid.S, Max = 2.6f;
            var cells = new List<int>();
            for (float t = S * 0.5f; t <= Max; t += S * 0.5f)
            {
                var p = from + dir * t;
                var (i, j) = g.Cell(p);
                if (!g.In(i, j)) { Commit(); return; }   // край здания
                int id = g.Idx(i, j);
                if (g.K[id] is GWall or GDoor)
                {
                    if (t > S * 1.1f) { Commit(); return; }
                    continue;   // ещё в своей стене
                }
                if (!cells.Contains(id)) cells.Add(id);
            }
            void Commit() { foreach (int id in cells) if (g.K[id] is GFree or GSolid) g.K[id] = GBarrier; }
        }

        List<DressRoom> FindRooms(DressGrid g, int idBase)
        {
            var rooms = new List<DressRoom>();
            Array.Fill(g.Comp, -1);
            var q = new Queue<int>();
            for (int s = 0; s < g.K.Length; s++)
            {
                if (g.Comp[s] != -1 || g.K[s] is not (GFree or GSolid)) continue;
                var r = new DressRoom { G = g, Id = idBase + rooms.Count };
                int cid = rooms.Count;
                g.Comp[s] = cid;
                q.Enqueue(s);
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    r.Cells.Add(c);
                    int ci = c % g.W, cj = c / g.W;
                    foreach (var (di, dj) in Dirs4)
                    {
                        int ni = ci + di, nj = cj + dj;
                        if (!g.In(ni, nj)) { r.Exterior = true; continue; }
                        int n = g.Idx(ni, nj);
                        if (g.Comp[n] != -1 || g.K[n] is not (GFree or GSolid)) continue;
                        g.Comp[n] = cid;
                        q.Enqueue(n);
                    }
                }
                rooms.Add(r);
            }
            const float A = DressGrid.S * DressGrid.S;
            var keep = new List<DressRoom>();
            foreach (var r in rooms)
            {
                r.Area = r.Cells.Count * A;
                if (r.Area < 2.5f) { foreach (int c in r.Cells) g.Comp[c] = -2; continue; }
                r.Min = new Vector2(float.MaxValue); r.Max = new Vector2(float.MinValue);
                foreach (int c in r.Cells)
                {
                    var p = g.C(c % g.W, c / g.W);
                    r.Min = Vector2.Min(r.Min, p - new Vector2(DressGrid.S / 2)); r.Max = Vector2.Max(r.Max, p + new Vector2(DressGrid.S / 2));
                }
                keep.Add(r);
            }
            // перенумеровать компоненты под оставшиеся комнаты
            for (int k = 0; k < keep.Count; k++) foreach (int c in keep[k].Cells) g.Comp[c] = k;
            for (int k = 0; k < keep.Count; k++)
            {
                var r = keep[k];
                r.Id = idBase + k;
                // двери: клетки комнаты рядом с проёмом (барьер, полотно двери, выход за край здания)
                foreach (int c in r.Cells)
                {
                    int ci = c % g.W, cj = c / g.W;
                    foreach (var (di, dj) in Dirs4)
                    {
                        int ni = ci + di, nj = cj + dj;
                        if (!g.In(ni, nj) || g.K[g.Idx(ni, nj)] is GBarrier or GDoor) { r.DoorCells.Add(c); break; }
                    }
                }
                foreach (int c in r.DoorCells) ReserveCell(g, c, 1.2f);
                // нужные точки: середины дверных групп и лут внутри
                var seen = new HashSet<int>();
                foreach (int c in r.DoorCells)
                {
                    if (seen.Contains(c)) continue;
                    var grp = new List<int>();
                    var qq = new Queue<int>(); qq.Enqueue(c); seen.Add(c);
                    while (qq.Count > 0)
                    {
                        int x = qq.Dequeue(); grp.Add(x);
                        foreach (int y in r.DoorCells)
                            if (!seen.Contains(y) && Math.Abs(y % g.W - x % g.W) + Math.Abs(y / g.W - x / g.W) <= 2) { seen.Add(y); qq.Enqueue(y); }
                    }
                    if (grp.Count >= 3) r.Need.Add(grp[grp.Count / 2]);
                }
                foreach (var bag in Bags) AddNeed(r, bag.Pos);
                foreach (var s in AllStashes()) AddNeed(r, s.Pos);
                foreach (var p in Pickups) AddNeed(r, p.Pos);
                // что уже недостижимо в исходном виде — не требовать (узкие проёмы, лут за мебелью макета)
                var reach = Reach(r, null);
                if (r.Need.Count > 0) r.Need = r.Need.FindAll(n => reach.Contains(Free(r, n)));
            }
            return keep;
        }

        static readonly (int, int)[] Dirs4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        static void ReserveCell(DressGrid g, int c, float r) => Reserve(g, g.C(c % g.W, c / g.W), r);

        static void AddNeed(DressRoom r, Vector2 p)
        {
            var g = r.G;
            var (i, j) = g.Cell(p);
            if (!g.In(i, j)) return;
            int c = g.Idx(i, j);
            if (g.Comp[c] >= 0 && r.Cells.Contains(c)) r.Need.Add(c);
        }

        /// <summary>Ближайшая к c проходимая клетка комнаты (лут стоит в своём хитбоксе).</summary>
        static int Free(DressRoom r, int c)
        {
            var g = r.G;
            int best = c; float bd = float.MaxValue;
            int ci = c % g.W, cj = c / g.W;
            for (int j = cj - 5; j <= cj + 5; j++)
                for (int i = ci - 5; i <= ci + 5; i++)
                {
                    if (!g.In(i, j)) continue;
                    int id = g.Idx(i, j);
                    if (g.Comp[id] != g.Comp[c] || Blocked(g, i, j, null)) continue;
                    float d = (i - ci) * (i - ci) + (j - cj) * (j - cj);
                    if (d < bd) { bd = d; best = id; }
                }
            return best;
        }

        /// <summary>Клетка непроходима для человека (радиус 0,35 м): рядом стена или хитбокс.</summary>
        static bool Blocked(DressGrid g, int i, int j, HashSet<int> extra)
        {
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int ni = i + di, nj = j + dj;
                    if (!g.In(ni, nj)) continue;
                    int id = g.Idx(ni, nj);
                    if (g.K[id] is GWall or GSolid || (extra != null && extra.Contains(id))) return true;
                }
            return false;
        }

        static HashSet<int> Reach(DressRoom r, HashSet<int> extra)
        {
            var g = r.G;
            var seen = new HashSet<int>();
            if (r.Need.Count == 0) return seen;
            int s = Free(r, r.Need[0]);
            var q = new Queue<int>();
            q.Enqueue(s); seen.Add(s);
            int comp = g.Comp[r.Cells[0]];
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                int ci = c % g.W, cj = c / g.W;
                foreach (var (di, dj) in Dirs4)
                {
                    int ni = ci + di, nj = cj + dj;
                    if (!g.In(ni, nj)) continue;
                    int n = g.Idx(ni, nj);
                    if (seen.Contains(n) || g.Comp[n] != comp || Blocked(g, ni, nj, extra)) continue;
                    seen.Add(n);
                    q.Enqueue(n);
                }
            }
            return seen;
        }

        static float Coverage(DressRoom r)
        {
            int n = 0;
            foreach (int c in r.Cells) if (r.G.Cov[c] || r.G.K[c] == GSolid) n++;
            return r.Cells.Count == 0 ? 0 : n / (float)r.Cells.Count;
        }

        // ---------- тип комнаты ----------

        string RoomKind(DressRoom r, string key, List<(Vector2 p, string kind)> tags, MapRoof roof)
        {
            var g = r.G;
            foreach (var (p, kind) in tags)
            {
                var (i, j) = g.Cell(p);
                if (g.In(i, j) && r.Cells.Contains(g.Idx(i, j))) return kind;
            }
            var score = new Dictionary<string, int>();
            void Add(string k, int v) => score[k] = score.GetValueOrDefault(k) + v;
            foreach (var d in Decors)
            {
                if (d.Flat || d.Pos.X < r.Min.X || d.Pos.X > r.Max.X || d.Pos.Y < r.Min.Y || d.Pos.Y > r.Max.Y + 0.3f) continue;
                string s = d.Sprite;
                if (s.Contains("bed") || s.Contains("cot") || s.Contains("bunk")) Add("barracks", 3);
                if (s.Contains("int_desk") || s.Contains("table_office") || s.Contains("bookcase") || s.Contains("school_desk") || s.Contains("duty_desk")) Add("office", 2);
                if (s.Contains("rack") && (s.Contains("radio") || s.Contains("ats_relay")) || s.Contains("console") || s.Contains("transmitter") || s.Contains("switchboard") || s.Contains("operator") || s.Contains("cross_frame") || s.Contains("patch_panel")) Add("control", 3);
                if (s.Contains("workbench") || s.Contains("gar_") || s.Contains("tool_rack") || s.Contains("table_workshop") || s.Contains("locomotive") || s.Contains("engine") || s.Contains("relay_truck")) Add("workshop", 3);
                if (s.Contains("shop_") || s.Contains("fridge_display") || s.Contains("cash_register")) Add("shop", 3);
                if (s.Contains("washbasin") || s.Contains("toilet")) Add("washroom", 4);
                if (s.Contains("stove") || s.Contains("table_kitchen") || s.Contains("fridge_zil")) Add("kitchen", 3);
                if (s.Contains("switchgear") || s.Contains("relay_cabinet") || s.Contains("rectifier") || s.Contains("battery") || s.Contains("diesel")) Add("electrical", 3);
                if (s.Contains("medical") || s.Contains("hospital")) Add("medbay", 3);
                if (s.Contains("shelf") || s.Contains("crate") || s.Contains("box") || s.Contains("pallet") || s.Contains("rack_warehouse") || s.Contains("bottle_crates") || s.Contains("kegs")) Add("storage", 1);
            }
            string best = null; int bv = 0;
            foreach (var (k, v) in score) if (v > bv) { bv = v; best = k; }
            var size = r.Max - r.Min;
            if (best == null && r.Area < 14 && MathF.Max(size.X, size.Y) > 3 * MathF.Min(size.X, size.Y)) return "corridor";
            if (best != null) return best;
            return key switch
            {
                "gai" => "office", "shop" => "storage", "fuel" => "shop", "garages" => "workshop", "ats" => "control", "depot" => "workshop",
                "quarry" => "office", "subst" => "electrical", _ => r.Area < 12 ? "storage" : "office",
            };
        }

        // ---------- слои комнаты ----------

        void DressRoomLayers(DressRoom r, Random rng)
        {
            if (!Kits.TryGetValue(r.Kind, out var kit)) kit = Kits["storage"];
            var g = r.G;
            // б) мебель: по комплекту, сначала крупное
            foreach (var it in kit.Big)
            {
                int n = rng.Next(it.Min, it.Max + 1);
                if (r.Area < 12) n = Math.Min(n, Math.Max(it.Min > 0 ? 1 : 0, n / 2));
                PlaceFurniture(r, it, n, rng, kit);
            }
            // острова посреди комнаты — чтобы середина не пустовала (тропы между дверями не трогаем)
            PlaceIslands(r, kit, rng);
            // а) стена: батареи, плакаты, часы, лотки, пятна
            DressWalls(r, kit, rng);
            // мелочь по углам и у стен
            int clutter = Math.Min((int)(r.Area / 14f) + rng.Next(0, 3), 14);
            var pool = new List<string>(kit.Clutter);
            pool.Add(GenericClutter[rng.Next(GenericClutter.Length)]);
            var used = new Dictionary<string, int>();
            for (int k = 0; k < clutter; k++)
            {
                string c = pool[rng.Next(pool.Count)];
                if (used.GetValueOrDefault(c) >= 2) continue;   // не больше двух одинаковых на комнату
                used[c] = used.GetValueOrDefault(c) + 1;
                PlaceSmall(r, c, rng, nearWall: rng.NextDouble() < 0.8);
            }
        }

        void PlaceIslands(DressRoom r, Kit kit, Random rng)
        {
            if (kit.Islands.Length == 0) return;
            var g = r.G;
            var path = PathCells(r);
            int free = 0;
            foreach (int c in r.Cells) if (g.K[c] == GFree && !g.Occ[c]) free++;
            int n = Math.Min((int)(free * DressGrid.S * DressGrid.S / 16f), 9);
            for (int k = 0, tries = 0; k < n && tries < n * 25; tries++)
            {
                string s = kit.Islands[rng.Next(kit.Islands.Length)];
                var f = FootOf(s);
                if (f == null) continue;
                var (w, d, h, legs) = f.Value;
                int c0 = r.Cells[rng.Next(r.Cells.Count)];
                var p = g.C(c0 % g.W, c0 / g.W) + new Vector2(0, d / 2);
                // запас вокруг: 0,7 м до стен и другой мебели, не на тропе
                var cells = FootCells(r, p + new Vector2(0, 0.7f), w + 1.4f, d + 1.4f);
                if (cells == null) continue;
                bool ok = true;
                foreach (int c in cells) if (g.K[c] is GWall or GSolid || path.Contains(c)) { ok = false; break; }
                if (!ok || !TryFurniture(r, s, p, w, d, h, legs, rng.NextDouble() < 0.5)) continue;
                k++;
                if (HasTop(s) && kit.Tops.Length > 0) AddTops(r, p, w, d, h, kit.Tops, rng);
                if (s.Contains("table_kitchen") || s.Contains("table_round") || s.Contains("table_office")) AddChairs(r, s, p, w, d, rng);
                if (kit.IslandFloor != null && rng.NextDouble() < 0.6) { AddFlat(kit.IslandFloor, p - new Vector2(0, d / 2), 1f, rng, kit.IslandFloor.Contains("rug") || kit.IslandFloor.Contains("carpet") ? 0 : null); MarkCov(r, p - new Vector2(0, d / 2), 0.9f); }
                // мелочь вокруг острова
                int around = 1 + rng.Next(3);
                for (int m = 0; m < around && kit.Clutter.Length > 0; m++)
                {
                    var q = p + new Vector2(((float)rng.NextDouble() - 0.5f) * (w + 1.0f), 0.25f + (float)rng.NextDouble() * 0.4f);
                    PlaceSmallAt(r, kit.Clutter[rng.Next(kit.Clutter.Length)], q, rng);
                }
            }
        }

        /// <summary>Мебель с хитбоксом по правилу места; проходы и подходы к луту проверяются.</summary>
        void PlaceFurniture(DressRoom r, KitItem it, int count, Random rng, Kit kit)
        {
            var f = FootOf(it.S);
            if (f == null || count <= 0) return;
            var (w, d, h, legs) = f.Value;
            var g = r.G;
            var cands = new List<(Vector2 p, bool rotSide)>();
            foreach (char side in it.Where)
            {
                switch (side)
                {
                    case 'N':
                    case 'S':
                        foreach (var (x0, x1, y, _) in WallRuns(r, side == 'N' ? 0 : 1))
                        {
                            float baseY = side == 'N' ? y + d + 0.05f : y - 0.05f;
                            if (it.Run)
                            {
                                float start = x0 + w / 2 + 0.1f;
                                for (float x = start; x <= x1 - w / 2 - 0.1f; x += w + 0.02f) cands.Add((new Vector2(x, baseY), false));
                            }
                            else
                                for (float x = x0 + w / 2 + 0.1f; x <= x1 - w / 2 - 0.1f; x += 0.5f) cands.Add((new Vector2(x, baseY), false));
                        }
                        break;
                    case 'W':
                    case 'E':
                        // кровати и стеллажи у боковых стен: торцом к стене (длинная сторона — поперёк комнаты)
                        foreach (var (y0, y1, x, west) in SideRuns(r))
                        {
                            float cx = west ? x + w / 2 + 0.05f : x - w / 2 - 0.05f;
                            for (float y = y0 + d + 0.1f; y <= y1 - 0.1f; y += d + (it.Run ? 0.45f : 0.6f)) cands.Add((new Vector2(cx, y), !west));
                        }
                        break;
                    case 'C':
                        for (int k = 0; k < 40; k++)
                        {
                            var p = new Vector2(Lerp(r.Min.X + w / 2 + 0.9f, r.Max.X - w / 2 - 0.9f, (float)rng.NextDouble()), Lerp(r.Min.Y + d + 0.9f, r.Max.Y - 0.9f, (float)rng.NextDouble()));
                            cands.Add((p, false));
                        }
                        break;
                }
            }
            if (cands.Count == 0) return;
            // ряды (шкафчики, койки) — подряд от случайного начала; одиночное — случайно
            int startIdx = rng.Next(cands.Count);
            if (!it.Run) Shuffle(cands, rng);
            int placed = 0;
            for (int k = 0; k < cands.Count && placed < count; k++)
            {
                var (p, flip) = cands[(startIdx + k) % cands.Count];
                if (!it.Run) (p, flip) = cands[k];
                if (!TryFurniture(r, it.S, p, w, d, h, legs, flip)) continue;
                placed++;
                if (it.Chairs) AddChairs(r, it.S, p, w, d, rng);
                if (HasTop(it.S) && kit.Tops.Length > 0) AddTops(r, p, w, d, h, kit.Tops, rng);
                if (it.S.EndsWith("bed_iron_side") || it.S.EndsWith("hospital_bed_side"))
                {
                    // у койки — тумбочка у изголовья или сапоги
                    var at = p + new Vector2(flip ? -w / 2 + 0.3f : w / 2 - 0.3f, 0.5f);
                    if (rng.NextDouble() < 0.55) PlaceSmallAt(r, rng.NextDouble() < 0.25 ? "clutter/int_boots_pair" : "interior/bedside_table", at, rng);
                }
            }
        }

        static float Lerp(float a, float b, float t) => b < a ? (a + b) / 2 : a + (b - a) * t;

        static void Shuffle<T>(List<T> l, Random rng)
        {
            for (int i = l.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); }
        }

        bool TryFurniture(DressRoom r, string sprite, Vector2 p, float w, float d, float h, bool legs, bool flip)
        {
            var g = r.G;
            var cells = FootCells(r, p, w, d);
            if (cells == null) return false;
            foreach (int c in cells) if (g.K[c] != GFree || g.Res[c] || g.Occ[c]) return false;
            var extra = new HashSet<int>(cells);
            if (r.Need.Count > 1)
            {
                var reach = Reach(r, extra);
                foreach (int n in r.Need) if (!reach.Contains(Free(r, n))) return false;
            }
            // крупное не ставить вплотную к двери снаружи комнаты
            foreach (int c in cells) { g.K[c] = GSolid; g.Cov[c] = true; }
            var dec = Decor(sprite, p, flip);
            if (legs)
            {
                AddHiddenWall(p.X - w / 2, p.Y - d, p.X - w / 2 + d, p.Y, h);
                AddHiddenWall(p.X + w / 2 - d, p.Y - d, p.X + w / 2, p.Y, h);
            }
            else AddHiddenWall(p.X - w / 2, p.Y - d, p.X + w / 2, p.Y, h);
            return true;
        }

        /// <summary>Клетки следа предмета (основание — передний край по центру); null — вылезает из комнаты.</summary>
        static List<int> FootCells(DressRoom r, Vector2 p, float w, float d)
        {
            var g = r.G;
            var list = new List<int>();
            var (i0, j0) = g.Cell(new Vector2(p.X - w / 2 + 0.01f, p.Y - d + 0.01f));
            var (i1, j1) = g.Cell(new Vector2(p.X + w / 2 - 0.01f, p.Y - 0.01f));
            int comp = g.Comp[r.Cells[0]];
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    if (!g.In(i, j)) return null;
                    int id = g.Idx(i, j);
                    if (g.Comp[id] != comp) return null;
                    list.Add(id);
                }
            return list;
        }

        /// <summary>Отрезки вдоль северной (side 0) или южной (1) стены комнаты: x0, x1, y грани стены, есть ли окно.</summary>
        static List<(float x0, float x1, float y, bool win)> WallRuns(DressRoom r, int side)
        {
            var g = r.G;
            var runs = new List<(float, float, float, bool)>();
            var rows = new Dictionary<int, List<int>>();
            foreach (int c in r.Cells)
            {
                int i = c % g.W, j = c / g.W;
                int nj = side == 0 ? j - 1 : j + 1;
                if (!g.In(i, nj) || g.K[g.Idx(i, nj)] != GWall) continue;
                if (!rows.TryGetValue(j, out var l)) rows[j] = l = new List<int>();
                l.Add(i);
            }
            foreach (var (j, l) in rows)
            {
                l.Sort();
                int s = 0;
                for (int k = 1; k <= l.Count; k++)
                {
                    if (k < l.Count && l[k] == l[k - 1] + 1) continue;
                    int i0 = l[s], i1 = l[k - 1];
                    if (i1 - i0 >= 2)
                    {
                        float y;
                        bool win = false;
                        if (side == 0)
                        {
                            y = float.MinValue;
                            for (int i = i0; i <= i1; i++) { int wc = g.Idx(i, j - 1); y = MathF.Max(y, g.FaceY[wc]); win |= g.Window[wc]; }
                            y = MathF.Max(y, g.O.Y + j * DressGrid.S);
                        }
                        else
                        {
                            y = g.O.Y + (j + 1) * DressGrid.S;
                            for (int i = i0; i <= i1; i++) win |= g.Window[g.Idx(i, j + 1)];
                        }
                        runs.Add((g.O.X + i0 * DressGrid.S, g.O.X + (i1 + 1) * DressGrid.S, y, win));
                    }
                    s = k;
                }
            }
            return runs;
        }

        /// <summary>Отрезки вдоль боковых стен: y0, y1, x грани, западная ли.</summary>
        static List<(float y0, float y1, float x, bool west)> SideRuns(DressRoom r)
        {
            var g = r.G;
            var runs = new List<(float, float, float, bool)>();
            foreach (bool west in new[] { true, false })
            {
                var cols = new Dictionary<int, List<int>>();
                foreach (int c in r.Cells)
                {
                    int i = c % g.W, j = c / g.W;
                    int ni = west ? i - 1 : i + 1;
                    if (!g.In(ni, j) || g.K[g.Idx(ni, j)] != GWall) continue;
                    if (!cols.TryGetValue(i, out var l)) cols[i] = l = new List<int>();
                    l.Add(j);
                }
                foreach (var (i, l) in cols)
                {
                    l.Sort();
                    int s = 0;
                    for (int k = 1; k <= l.Count; k++)
                    {
                        if (k < l.Count && l[k] == l[k - 1] + 1) continue;
                        if (l[k - 1] - l[s] >= 4)
                            runs.Add((g.O.Y + l[s] * DressGrid.S, g.O.Y + (l[k - 1] + 1) * DressGrid.S, west ? g.O.X + i * DressGrid.S : g.O.X + (i + 1) * DressGrid.S, west));
                        s = k;
                    }
                }
            }
            return runs;
        }

        /// <summary>
        /// Стулья лицом к столу: с юга — спинкой к камере (вид сзади), с боков — профилем к столу, с севера (если стол
        /// не у стены) — лицом к камере. Стол у стены — один стул спереди, спинкой к нам.
        /// </summary>
        void AddChairs(DressRoom r, string table, Vector2 p, float w, float d, Random rng)
        {
            bool office = table.Contains("office") || table.Contains("desk");
            string front = office ? "furniture/chair_office" : "furniture/chair_kitchen";
            string back = office ? "furniture/chair_office_back" : "furniture/chair_kitchen_back";
            string side = office ? "furniture/chair_office_side" : "furniture/chair_kitchen_side";
            var g = r.G;
            var (ni, nj) = g.Cell(new Vector2(p.X, p.Y - d - 0.3f));
            bool atWall = !g.In(ni, nj) || g.K[g.Idx(ni, nj)] is GWall or GSolid;
            // юг: стул отодвинут от стола, спинкой к камере (сортируется после стола — закрывает его край, как и должен)
            PlaceSmallAt(r, rng.NextDouble() < 0.12 ? "interior/chair_overturned" : back, p + new Vector2((float)(rng.NextDouble() - 0.5) * w * 0.4f, 0.3f), rng, flip: rng.NextDouble() < 0.5, force: true);
            if (office && atWall) return;
            if (w >= 1.0f || !office)
            {
                // запад: профилем, смотрит вправо (на стол); восток — зеркально
                PlaceSmallAt(r, side, p + new Vector2(-w / 2 - 0.2f, -d * 0.3f), rng, flip: false, force: true);
                if (rng.NextDouble() < 0.75) PlaceSmallAt(r, side, p + new Vector2(w / 2 + 0.2f, -d * 0.3f), rng, flip: true, force: true);
            }
            if (!atWall && rng.NextDouble() < 0.6) PlaceSmallAt(r, front, p + new Vector2((float)(rng.NextDouble() - 0.5) * w * 0.3f, -d - 0.05f), rng, flip: rng.NextDouble() < 0.5, force: true);
        }

        void AddTops(DressRoom r, Vector2 p, float w, float d, float h, string[] tops, Random rng)
        {
            int n = 1 + rng.Next(Math.Max(1, (int)(w / 0.5f)));
            n = Math.Min(n, 4);
            var used = new List<float>();
            for (int k = 0; k < n; k++)
            {
                float x = p.X + ((float)rng.NextDouble() - 0.5f) * w * 0.7f;
                if (used.Exists(u => MathF.Abs(u - x) < 0.3f)) continue;
                used.Add(x);
                // сортировка — по переднему краю стола (+ чуть), картинка поднята на столешницу
                var dec = Decor(tops[rng.Next(tops.Length)], new Vector2(x, p.Y + 0.01f + k * 0.002f), rng.NextDouble() < 0.5);
                dec.Lift = h + d * 0.28f + (float)rng.NextDouble() * 0.06f;
                dec.Batch = true;
            }
        }

        /// <summary>Слой стены: батареи под окнами, плакаты, часы, карты, кабельные лотки, отслоившаяся штукатурка, плесень.</summary>
        void DressWalls(DressRoom r, Kit kit, Random rng)
        {
            var g = r.G;
            var hungUsed = new Dictionary<string, int>();
            var near = Decors.FindAll(d => !d.Flat && !d.Batch && d.Pos.X > r.Min.X - 2 && d.Pos.X < r.Max.X + 2 && d.Pos.Y > r.Min.Y - 0.5f && d.Pos.Y < r.Max.Y + 1.5f);
            foreach (var (x0, x1, y, win) in WallRuns(r, 0))
            {
                // что стоит у стены: высота по x — туда вешаем выше или не вешаем
                float RunHeight(float xa, float xb)
                {
                    float hmax = 0;
                    foreach (var d in near)
                    {
                        if (d.Pos.Y < y || d.Pos.Y > y + 1.4f) continue;
                        var f = FootOf(d.Sprite);
                        float fw = f?.w ?? 0.6f;
                        if (d.Pos.X + fw / 2 < xa || d.Pos.X - fw / 2 > xb) continue;
                        hmax = MathF.Max(hmax, f?.h ?? 1f);
                    }
                    return hmax;
                }
                // батареи — под окнами и через одну на глухих
                if (rng.NextDouble() < kit.Radiators + (win ? 0.4f : 0))
                    for (float x = x0 + 0.9f; x < x1 - 0.7f; x += rng.Next(3, 6))
                    {
                        if (RunHeight(x - 0.55f, x + 0.55f) > 0) continue;
                        var (ci, cj) = g.Cell(new Vector2(x, y + 0.1f));
                        if (!g.In(ci, cj) || g.Res[g.Idx(ci, cj)] && !win) continue;
                        var dec = Decor("furniture/radiator", new Vector2(x, y + 0.08f));
                        dec.Batch = true;
                        MarkOcc(r, new Vector2(x, y + 0.2f), 0.5f);
                    }
                // висящее на стене: шаг 1,2–2,6 м
                for (float x = x0 + 0.5f + (float)rng.NextDouble() * 0.8f; x < x1 - 0.4f; x += 1.2f + (float)rng.NextDouble() * 1.4f)
                {
                    if (rng.NextDouble() < 0.18) continue;
                    float under = RunHeight(x - 0.4f, x + 0.4f);
                    if (under > 2.1f) continue;   // высокие стеллажи и шкафы закрывают стену
                    string s = kit.Wall.Length > 0 && rng.NextDouble() < 0.8 ? kit.Wall[rng.Next(kit.Wall.Length)] : Posters[rng.Next(Posters.Length)];
                    // часы, карта, портрет — по одному на комнату; плакаты и доски — не больше двух одинаковых
                    int lim = s.Contains("poster") || s.Contains("notice") || s.Contains("tool_board") || s.Contains("switchboard") ? 2 : 1;
                    if (hungUsed.GetValueOrDefault(s) >= lim) { s = Posters[rng.Next(Posters.Length)]; if (hungUsed.GetValueOrDefault(s) >= 2) continue; }
                    hungUsed[s] = hungUsed.GetValueOrDefault(s) + 1;
                    float lift = s switch
                    {
                        "interior/wall_clock" => 1.95f, "interior/wall_pennant" => 1.5f, "interior/wall_map" => 1.05f, "furniture/int_notice_board" => 1.0f,
                        "interior/wall_tool_board" => 0.95f, "interior/wall_switchboard_small" => 1.0f, "interior/wall_mirror" => 1.15f, _ => 1.15f,
                    };
                    lift = MathF.Max(lift, under + 0.15f) + (float)(rng.NextDouble() - 0.5) * 0.12f;
                    var dec = Decor(s, new Vector2(x, y + 0.03f), false);
                    dec.Lift = lift;
                    dec.Batch = true;
                }
                // отвалившаяся штукатурка, плесень у пола и под потолком
                for (float x = x0 + 0.4f; x < x1 - 0.4f; x += 1.0f + (float)rng.NextDouble() * 2.5f)
                {
                    if (rng.NextDouble() < 0.6) continue;
                    bool mold = rng.NextDouble() < 0.35;
                    var dec = Decor(mold ? "interior/wall_mold" : "interior/wall_plaster_peel", new Vector2(x, y + 0.02f), rng.NextDouble() < 0.5);
                    dec.Lift = !mold && rng.NextDouble() < 0.4 ? 0.05f : 1.4f + (float)rng.NextDouble() * 0.7f;   // плесень — под потолком, не у пола
                    dec.Batch = true;
                    if (dec.Lift < 0.1f) { var p = new Vector2(x, y + 0.25f); AddFlat("decals/floor_plaster", p, 0.8f + (float)rng.NextDouble() * 0.3f, rng); MarkCov(r, p, 0.4f); }
                }
                // заколоченное окно изнутри
                if (win && rng.NextDouble() < 0.35)
                {
                    float x = Lerp(x0 + 0.8f, x1 - 0.8f, (float)rng.NextDouble());
                    if (RunHeight(x - 0.5f, x + 0.5f) < 0.9f) { var dec = Decor("interior/wall_window_boarded", new Vector2(x, y + 0.04f)); dec.Lift = 0.95f; dec.Batch = true; }
                }
                // битое стекло под окнами
                if (win)
                    for (float x = x0 + 0.5f; x < x1 - 0.3f; x += 1.5f + (float)rng.NextDouble() * 2)
                        if (rng.NextDouble() < 0.5) { var p = new Vector2(x, y + 0.35f + (float)rng.NextDouble() * 0.3f); AddFlat("decals/decal_glass_shards", p, 0.9f, rng); MarkCov(r, p, 0.35f); }
                // кабельные лотки / труба под потолком — в аппаратных и щитовых
                if (r.Kind is "control" or "electrical" or "workshop" && x1 - x0 > 3)
                {
                    for (float x = x0 + 1.5f; x < x1 - 1.4f; x += 3f)
                    {
                        var dec = Decor(r.Kind == "workshop" ? "decals/ats_pipe_run" : "furniture/int_cable_trays", new Vector2(x, y + 0.025f));
                        dec.Lift = 2.35f;
                        dec.Batch = true;
                    }
                }
            }
            // боковые стены: их лицо в 3/4 не видно, поэтому вешаем то, что торчит из стены в комнату и читается сверху:
            // бра, щиток с трубкой проводки, огнетушитель на кронштейне, аптечка; на полу у стены — вешалка, ведро
            foreach (var (y0, y1, x, west) in SideRuns(r))
            {
                if (y1 - y0 < 2.0f) continue;
                for (float y = y0 + 0.9f + (float)rng.NextDouble(); y < y1 - 0.3f; y += 2.2f + (float)rng.NextDouble() * 2.2f)
                {
                    if (rng.NextDouble() < 0.3) continue;
                    var at = new Vector2(west ? x + 0.14f : x - 0.14f, y);
                    var (ci, cj) = g.Cell(at);
                    if (!g.In(ci, cj) || g.K[g.Idx(ci, cj)] == GSolid) continue;
                    double k = rng.NextDouble();
                    if (k < 0.3) { var dl = Decor("props/lamp_wall", at, !west); dl.Lift = 1.85f; dl.Batch = true; }
                    else if (k < 0.5) { var de = Decor(r.Kind == "medbay" ? "interior/wall_first_aid" : "interior/wall_electric_box", at, !west); de.Lift = 0.95f; de.Batch = true; }
                    else if (k < 0.75) { var df = Decor("furniture/int_fire_extinguisher", at + new Vector2(west ? 0.08f : -0.08f, 0), !west); df.Lift = 0.35f; df.Batch = true; }
                    else PlaceSmallAt(r, r.Kind is "barracks" or "office" or "corridor" ? "furniture/coat_rack" : "clutter/clutter_bucket", new Vector2(west ? x + 0.3f : x - 0.3f, y), rng);
                }
            }
            // южная стена: её лицо изнутри не видно (стена срезается, когда игрок внутри) — только низкое у плинтуса:
            // батареи под окнами, ведро, пыль
            foreach (var (x0, x1, y, win) in WallRuns(r, 1))
            {
                if (!win && rng.NextDouble() < 0.6) continue;
                for (float x = x0 + 1.0f; x < x1 - 0.8f; x += rng.Next(3, 6))
                {
                    var at = new Vector2(x, y - 0.06f);
                    var (ci, cj) = g.Cell(at - new Vector2(0, 0.1f));
                    if (!g.In(ci, cj) || g.K[g.Idx(ci, cj)] != GFree || g.Occ[g.Idx(ci, cj)] || g.Res[g.Idx(ci, cj)]) continue;
                    var dr = Decor("furniture/radiator", at); dr.Batch = true;
                    MarkOcc(r, at - new Vector2(0, 0.1f), 0.5f);
                }
            }
        }

        void MarkOcc(DressRoom r, Vector2 p, float rad)
        {
            var g = r.G;
            var (ci, cj) = g.Cell(p);
            int rr = (int)MathF.Ceiling(rad / DressGrid.S);
            for (int j = cj - rr; j <= cj + rr; j++)
                for (int i = ci - rr; i <= ci + rr; i++)
                    if (g.In(i, j) && Vector2.Distance(g.C(i, j), p) <= rad) { g.Occ[g.Idx(i, j)] = true; g.Cov[g.Idx(i, j)] = true; }
        }

        void MarkCov(DressRoom r, Vector2 p, float rad)
        {
            var g = r.G;
            var (ci, cj) = g.Cell(p);
            int rr = (int)MathF.Ceiling(rad / DressGrid.S);
            for (int j = cj - rr; j <= cj + rr; j++)
                for (int i = ci - rr; i <= ci + rr; i++)
                    if (g.In(i, j) && Vector2.Distance(g.C(i, j), p) <= rad) g.Cov[g.Idx(i, j)] = true;
        }

        MapDecor AddFlat(string sprite, Vector2 p, float scale, Random rng, float? rot = null)
        {
            var d = new MapDecor { Id = Decors.Count, Sprite = sprite, Pos = p, Flat = true, Scale = scale, Rot = rot ?? (float)(rng.NextDouble() * 360) };
            Decors.Add(d);
            return d;
        }

        /// <summary>Мелочь без хитбокса в свободной клетке (у стены или где угодно).</summary>
        void PlaceSmall(DressRoom r, string sprite, Random rng, bool nearWall)
        {
            var g = r.G;
            for (int t = 0; t < 30; t++)
            {
                int c = r.Cells[rng.Next(r.Cells.Count)];
                int i = c % g.W, j = c / g.W;
                if (g.K[c] != GFree || g.Occ[c]) continue;
                if (nearWall)
                {
                    bool wall = false;
                    for (int dj = -2; dj <= 2 && !wall; dj++) for (int di = -2; di <= 2 && !wall; di++) wall = g.In(i + di, j + dj) && g.K[g.Idx(i + di, j + dj)] is GWall or GSolid;
                    if (!wall) continue;
                }
                PlaceSmallAt(r, sprite, g.C(i, j) + new Vector2(0, DressGrid.S * 0.4f), rng);
                return;
            }
        }

        void PlaceSmallAt(DressRoom r, string sprite, Vector2 p, Random rng, bool? flip = null, bool force = false)
        {
            var g = r.G;
            var (i, j) = g.Cell(p - new Vector2(0, 0.1f));
            if (!g.In(i, j)) return;
            int c = g.Idx(i, j);
            if (g.Comp[c] != g.Comp[r.Cells[0]] || g.K[c] is GWall or GSolid) return;
            if (!force && g.Occ[c]) return;
            if (sprite.StartsWith("decals/")) { AddFlat(sprite, p, 0.85f + (float)rng.NextDouble() * 0.3f, rng); MarkCov(r, p, DecalSize(sprite) * 0.4f); return; }
            if (!force && (sprite == "furniture/chair_office" || sprite == "furniture/chair_kitchen"))
            {
                // брошенный стул — как стоял: лицом, боком или спинкой
                double k = rng.NextDouble();
                if (k < 0.33) sprite += "_side"; else if (k < 0.6) sprite += "_back";
            }
            var d = Decor(sprite, p, flip ?? rng.NextDouble() < 0.5);
            d.Batch = true;
            MarkOcc(r, p - new Vector2(0, 0.15f), 0.3f);
        }

        static readonly HashSet<string> FeatureDecals = new() { "decals/decal_cables", "decals/floor_rug", "decals/floor_rug_runner", "decals/floor_lino_patch", "decals/floor_blood", "decals/decal_carpet_red", "decals/floor_leak", "decals/decal_puddle_oily", "decals/decal_oil_stain_big" };
        static readonly string[] Litter = { "decals/decal_debris", "decals/floor_papers", "decals/decal_cigarette_butts", "decals/floor_plaster", "decals/floor_bottles", "decals/floor_cans", "decals/floor_stain", "decals/floor_wood_chips", "decals/decal_leaves" };

        /// <summary>Клетки троп между дверями и к луту (кратчайшие пути, раздутые на ~0,5 м) — там чище, «натоптано».</summary>
        static HashSet<int> PathCells(DressRoom r)
        {
            var g = r.G;
            var path = new HashSet<int>();
            if (r.Need.Count < 2) return path;
            int comp = g.Comp[r.Cells[0]];
            int s = Free(r, r.Need[0]);
            var prev = new Dictionary<int, int> { [s] = -1 };
            var q = new Queue<int>(); q.Enqueue(s);
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                int ci = c % g.W, cj = c / g.W;
                foreach (var (di, dj) in Dirs4)
                {
                    int ni = ci + di, nj = cj + dj;
                    if (!g.In(ni, nj)) continue;
                    int n = g.Idx(ni, nj);
                    if (prev.ContainsKey(n) || g.Comp[n] != comp || g.K[n] is GWall or GSolid) continue;
                    prev[n] = c; q.Enqueue(n);
                }
            }
            for (int k = 1; k < r.Need.Count; k++)
                for (int c = Free(r, r.Need[k]); prev.ContainsKey(c) && c != -1; c = prev[c])
                {
                    int ci = c % g.W, cj = c / g.W;
                    for (int dj = -2; dj <= 2; dj++) for (int di = -2; di <= 2; di++) if (g.In(ci + di, cj + dj)) path.Add(g.Idx(ci + di, cj + dj));
                }
            return path;
        }

        /// <summary>
        /// г) пол: 1–3 «вещи» по типу комнаты (коврик, дорожка, кусок линолеума), пыль и грязь гуще у стен и в углах,
        /// мусор — больше по краям, на тропах между дверями чище. Цель — 60–80 % площади чем-то занято.
        /// </summary>
        void FillFloor(DressRoom r, Random rng)
        {
            if (!Kits.TryGetValue(r.Kind, out var kit)) kit = Kits["storage"];
            var g = r.G;
            var path = PathCells(r);
            int WallNear(int i, int j, int rad)
            {
                int n = 0;
                for (int dj = -rad; dj <= rad; dj++) for (int di = -rad; di <= rad; di++) if (g.In(i + di, j + dj) && g.K[g.Idx(i + di, j + dj)] is GWall) n++;
                return n;
            }
            // «вещи»: по 1–2 из комплекта, в свободной середине или у мебели
            var feats = new List<string>();
            foreach (var f in kit.Floor) if (FeatureDecals.Contains(f)) feats.Add(f);
            int nf = feats.Count == 0 ? 0 : Math.Min(1 + (int)(r.Area / 40f), 3);
            for (int k = 0; k < nf; k++)
            {
                string f = feats[rng.Next(feats.Count)];
                if (f == "decals/floor_blood" && rng.NextDouble() < 0.7) continue;
                for (int t = 0; t < 20; t++)
                {
                    int c = r.Cells[rng.Next(r.Cells.Count)];
                    if (WallNear(c % g.W, c / g.W, 3) > 0 && !f.Contains("runner")) continue;
                    var p = g.C(c % g.W, c / g.W);
                    // дорожка ~0,8×2,6 м, большой ковёр — не больше 2×3 м (иначе не по масштабу 32 px/м)
                    float sc = f.Contains("runner") ? 0.85f + (float)rng.NextDouble() * 0.1f : f.Contains("carpet") ? 0.6f : 0.9f + (float)rng.NextDouble() * 0.2f;
                    AddFlat(f, p, sc, rng, f.Contains("rug") || f.Contains("lino") ? (float)(rng.Next(2) * 90 + rng.NextDouble() * 12 - 6) : null);
                    MarkCov(r, p, DecalSize(f) * sc * 0.5f);
                    break;
                }
            }
            // грязь у стен: мягкие вытянутые пятна вдоль плинтуса и в углах (гладкие, без пиксельной ряби)
            foreach (var (x0, x1, y, _) in WallRuns(r, 0))
                for (float x = x0 + 0.6f + (float)rng.NextDouble(); x < x1 - 0.4f; x += 2.2f + (float)rng.NextDouble() * 1.6f)
                { var q = new Vector2(x, y + 0.55f); AddFlat("decals/floor_grime_2", q, 0.55f + (float)rng.NextDouble() * 0.25f, rng, (float)(rng.NextDouble() * 8 - 4)); MarkCov(r, q, 0.7f); }
            foreach (var (x0, x1, y, _) in WallRuns(r, 1))
                for (float x = x0 + 0.6f + (float)rng.NextDouble(); x < x1 - 0.4f; x += 2.4f + (float)rng.NextDouble() * 1.8f)
                { var q = new Vector2(x, y - 0.5f); AddFlat("decals/floor_grime_2", q, 0.5f + (float)rng.NextDouble() * 0.25f, rng, (float)(rng.NextDouble() * 8 - 4)); MarkCov(r, q, 0.6f); }
            foreach (var (y0, y1, x, west) in SideRuns(r))
                for (float y = y0 + 0.6f + (float)rng.NextDouble(); y < y1 - 0.4f; y += 2.2f + (float)rng.NextDouble() * 1.6f)
                { var q = new Vector2(west ? x + 0.5f : x - 0.5f, y); AddFlat("decals/floor_grime_2", q, 0.55f + (float)rng.NextDouble() * 0.25f, rng, (float)(90 + rng.NextDouble() * 8 - 4)); MarkCov(r, q, 0.7f); }
            // мусор у комнаты свой: 2 общих вида + то, что из комплекта (не всё подряд — иначе каша)
            var litterPool = new List<string>();
            for (int k = 0; k < 2; k++) litterPool.Add(Litter[rng.Next(Litter.Length)]);
            foreach (var f in kit.Floor) if (!FeatureDecals.Contains(f)) litterPool.Add(f);
            float target = 0.62f + (float)rng.NextDouble() * 0.16f;
            float cov = Coverage(r);
            int guard = (int)(r.Area * 3) + 20;
            for (int it = 0; cov < target && guard-- > 0; it++)
            {
                if (it % 8 == 7) cov = Coverage(r);
                int c = r.Cells[rng.Next(r.Cells.Count)];
                if (g.Cov[c] || g.K[c] == GSolid) continue;
                int i = c % g.W, j = c / g.W;
                int wn = WallNear(i, j, 3);
                bool onPath = path.Contains(c);
                // середина и тропы — реже (но не пусто): отбраковка по весу
                double keep = wn > 0 ? 1.0 : onPath ? 0.35 : 0.6;
                if (rng.NextDouble() > keep) continue;
                var p = g.C(i, j) + new Vector2((float)(rng.NextDouble() - 0.5) * 0.3f, (float)(rng.NextDouble() - 0.5) * 0.3f);
                // основа — мягкая пыль и пятна (низкий контраст), мусор — редкими акцентами, больше у стен
                string s;
                double roll = rng.NextDouble();
                if (onPath) s = roll < 0.6 ? "decals/floor_grime" : roll < 0.85 ? "decals/floor_stain" : "decals/decal_debris";
                else if (wn > 0) s = roll < 0.45 ? "decals/floor_grime" : roll < 0.62 ? "decals/floor_stain" : litterPool[rng.Next(litterPool.Count)];
                else s = roll < 0.4 ? "decals/floor_grime" : roll < 0.62 ? "decals/floor_stain" : litterPool[rng.Next(litterPool.Count)];
                float sc = s == "decals/floor_grime" ? 0.45f + (float)rng.NextDouble() * 0.4f : 0.8f + (float)rng.NextDouble() * 0.4f;
                AddFlat(s, p, sc, rng);
                MarkCov(r, p, DecalSize(s) * sc * 0.42f);
                // изредка рядом — мелочь-спрайт (бутылка, сапоги, ведро), не на тропе
                if (!onPath && wn > 0 && rng.NextDouble() < 0.025 && !g.Occ[c])
                {
                    var pool = kit.Clutter.Length > 0 ? kit.Clutter : GenericClutter;
                    PlaceSmallAt(r, pool[rng.Next(pool.Length)], p + new Vector2(0.2f, 0.25f), rng);
                }
            }
        }

        // ---------- истории ----------

        void Vignette(DressRoom r, Random rng, string key)
        {
            var g = r.G;
            // свободное место ~2×2 м
            Vector2? Spot(float rad)
            {
                for (int t = 0; t < 80; t++)
                {
                    int c = r.Cells[rng.Next(r.Cells.Count)];
                    var p = g.C(c % g.W, c / g.W);
                    bool ok = true;
                    var (ci, cj) = g.Cell(p);
                    int rr = (int)(rad / DressGrid.S);
                    for (int j = cj - rr; j <= cj + rr && ok; j++)
                        for (int i = ci - rr; i <= ci + rr && ok; i++)
                            ok = g.In(i, j) && g.Comp[g.Idx(i, j)] == g.Comp[c] && g.K[g.Idx(i, j)] == GFree && !g.Occ[g.Idx(i, j)];
                    if (ok) return p;
                }
                return null;
            }
            var kinds = new List<string> { "struggle", "camp", "medic", "cards", "barricade" };
            string k = kinds[rng.Next(kinds.Count)];
            switch (k)
            {
                case "camp":
                {
                    var s = Spot(1.1f);
                    if (s is not Vector2 p) return;
                    // кто-то ночевал: матрас со спальником, костерок, банки, свечка
                    PlaceSmallAt(r, "clutter/clutter_mattress", p + new Vector2(-0.5f, 0.1f), rng, force: true);
                    PlaceSmallAt(r, "props/sleeping_bag", p + new Vector2(-0.5f, 0.45f), rng, flip: false, force: true);
                    AddFlat("decals/floor_campfire", p + new Vector2(0.7f, 0.2f), 1f, rng);
                    AddFlat("decals/floor_cans", p + new Vector2(0.5f, 0.7f), 1f, rng);
                    AddFlat("decals/floor_bottles", p + new Vector2(-1.1f, -0.2f), 1f, rng);
                    PlaceSmallAt(r, "interior/top_candle", p + new Vector2(-1.0f, 0.55f), rng, force: true);
                    PlaceSmallAt(r, "interior/top_cans", p + new Vector2(0.95f, 0.65f), rng, force: true);
                    Ambients.Add(new MapAmbient { Kind = "candle", Pos = p + new Vector2(-1.0f, 0.55f), H = 0.15f });
                    MarkCov(r, p, 1.2f);
                    break;
                }
                case "struggle":
                {
                    var s = Spot(0.9f);
                    if (s is not Vector2 p) return;
                    PlaceSmallAt(r, "interior/chair_overturned", p, rng, force: true);
                    AddFlat("decals/floor_papers", p + new Vector2(0.6f, 0.3f), 1.1f, rng);
                    AddFlat("decals/floor_papers", p + new Vector2(-0.5f, 0.6f), 0.9f, rng);
                    AddFlat("decals/floor_wood_chips", p + new Vector2(-0.3f, -0.2f), 1f, rng);
                    AddFlat("decals/decal_glass_shards", p + new Vector2(0.3f, 0.8f), 0.9f, rng);
                    PlaceSmallAt(r, "interior/top_lamp", p + new Vector2(0.9f, 0.9f), rng, force: true);
                    MarkCov(r, p, 1.0f);
                    break;
                }
                case "medic":
                {
                    var s = Spot(0.9f);
                    if (s is not Vector2 p) return;
                    AddFlat("decals/floor_blood", p, 1.1f, rng);
                    AddFlat("decals/floor_bandages", p + new Vector2(0.7f, 0.3f), 1f, rng);
                    PlaceSmallAt(r, "interior/top_medkit", p + new Vector2(-0.6f, 0.5f), rng, force: true);
                    AddFlat("decals/floor_rags", p + new Vector2(-0.4f, -0.4f), 0.9f, rng);
                    MarkCov(r, p, 1.0f);
                    break;
                }
                case "cards":
                {
                    var s = Spot(1.1f);
                    if (s is not Vector2 p) return;
                    var f = FootOf("furniture/table_folding_card");
                    if (f == null || !TryFurniture(r, "furniture/table_folding_card", p, f.Value.w, f.Value.d, f.Value.h, false, false)) return;
                    var top = Decor("interior/top_cards", p + new Vector2(0, 0.01f));
                    top.Lift = f.Value.h + 0.15f; top.Batch = true;
                    PlaceSmallAt(r, "furniture/chair_kitchen_back", p + new Vector2(0.1f, 0.3f), rng, force: true);
                    PlaceSmallAt(r, "furniture/chair_kitchen_side", p + new Vector2(-0.68f, -0.15f), rng, flip: false, force: true);
                    PlaceSmallAt(r, "interior/chair_overturned", p + new Vector2(0.8f, -0.1f), rng, force: true);
                    AddFlat("decals/floor_cards", p + new Vector2(0.4f, 0.75f), 1f, rng);
                    AddFlat("decals/floor_bottles", p + new Vector2(-0.6f, 0.7f), 1f, rng);
                    AddFlat("decals/decal_cigarette_butts", p + new Vector2(0.1f, 0.9f), 1f, rng);
                    MarkCov(r, p, 1.1f);
                    break;
                }
                case "barricade":
                {
                    // дверь подпёрли шкафом и досками (если у комнаты несколько дверей — проход останется)
                    if (r.DoorCells.Count == 0) return;
                    int c = r.DoorCells[rng.Next(r.DoorCells.Count)];
                    var p = g.C(c % g.W, c / g.W);
                    PlaceSmallAt(r, "interior/barricade_planks", p + new Vector2(0.9f, 0.3f), rng, force: true);
                    var f = FootOf("furniture/wardrobe_single");
                    if (f != null) TryFurniture(r, "furniture/wardrobe_single", p + new Vector2(-1.1f, 0.6f), f.Value.w, f.Value.d, f.Value.h, false, false);
                    AddFlat("decals/floor_wood_chips", p + new Vector2(0.4f, 0.8f), 1f, rng);
                    MarkCov(r, p, 0.9f);
                    break;
                }
            }
        }

        // ---------- снаружи ----------

        /// <summary>Здания «сидят» в земле: бурьян и грязь вдоль стен, лужи и мусор у входов, тропы из плит от входов.</summary>
        void DressExterior(Monument m, List<MapRoof> roofs, Random rng)
        {
            bool InsideRoof(Vector2 p) => Roofs.Exists(r => p.X > r.Min.X - 0.2f && p.X < r.Max.X + 0.2f && p.Y > r.Min.Y - 0.2f && p.Y < r.Max.Y + 0.2f);
            bool NearEntrance(Vector2 p, float pad) => Entrances.Exists(e =>
            {
                var a = Vector2.Min(e.A, e.B) - new Vector2(pad); var b = Vector2.Max(e.A, e.B) + new Vector2(pad);
                return p.X > a.X - e.Depth && p.X < b.X + e.Depth && p.Y > a.Y - e.Depth && p.Y < b.Y + e.Depth;
            });
            string[] weeds = { "grass/weeds_wall", "grass/weeds_wall", "grass/grass_meadow_1", "grass/grass_arid_2", "grass/grass_forest_2", "grass/grass_meadow_2" };
            // крупные пропсы без крыши (будка ГАИ, автобус, цистерна): тоже «сидят» в бурьяне и грязи
            foreach (var d in Decors.ToArray())
            {
                if (d.Flat || d.Batch || !m.Contains(d.Pos)) continue;
                var f = FootOf(d.Sprite);
                if (f == null || f.Value.w < 2.5f || f.Value.h < 1.2f) continue;
                for (float t = -f.Value.w / 2; t <= f.Value.w / 2; t += 0.6f + (float)rng.NextDouble() * 0.6f)
                {
                    foreach (float dy in new[] { 0.25f, -f.Value.d - 0.2f })
                    {
                        var p = d.Pos + new Vector2(t, dy);
                        if (rng.NextDouble() < 0.5 || WalkBlocked(p, 0.1f)) continue;
                        if (rng.NextDouble() < 0.6) { var w = Decor(weeds[rng.Next(weeds.Length)], p, rng.NextDouble() < 0.5); w.Batch = true; }
                        else AddFlat(rng.NextDouble() < 0.5 ? "decals/floor_grime" : "decals/decal_leaves", p, rng.NextDouble() < 0.5 ? 0.6f : 1f, rng);
                    }
                }
            }
            foreach (var r in roofs)
            {
                for (int side = 0; side < 4; side++)
                {
                    bool horiz = side < 2;
                    float lo = horiz ? r.Min.X : r.Min.Y, hi = horiz ? r.Max.X : r.Max.Y;
                    for (float t = lo + 0.3f; t < hi - 0.3f; t += 0.55f + (float)rng.NextDouble() * 0.7f)
                    {
                        float off = 0.35f + (float)rng.NextDouble() * 0.25f;
                        var p = side switch
                        {
                            0 => new Vector2(t, r.Max.Y + off), 1 => new Vector2(t, r.Min.Y - off * 0.5f), 2 => new Vector2(r.Max.X + off, t), _ => new Vector2(r.Min.X - off, t),
                        };
                        if (!m.Contains(p, 6) || InsideRoof(p) || NearEntrance(p, 0.6f) || WalkBlocked(p, 0.15f)) continue;
                        double roll = rng.NextDouble();
                        if (roll < 0.42) { var d = Decor(weeds[rng.Next(weeds.Length)], p, rng.NextDouble() < 0.5); d.Batch = true; }
                        else if (roll < 0.7)
                        {
                            string[] dirt = { "decals/floor_grime_2", "decals/floor_grime_2", "decals/decal_moss_patch", "decals/decal_leaves", "decals/decal_debris", "decals/decal_bricks_scatter" };
                            string ds = dirt[rng.Next(dirt.Length)];
                            bool gr = ds.Contains("grime");
                            AddFlat(ds, p + new Vector2(0, side == 1 ? -0.2f : 0.2f), gr ? 0.5f + (float)rng.NextDouble() * 0.25f : 0.8f + (float)rng.NextDouble() * 0.4f, rng, gr ? (side < 2 ? 0 : 90) + (float)(rng.NextDouble() * 8 - 4) : null);
                        }
                    }
                }
            }
            // у входов: лужа, окурки, мусор; от входа — тропа из плит до твёрдой земли
            foreach (var e in Entrances)
            {
                var mid = (e.A + e.B) * 0.5f;
                if (!m.Contains(mid, 2)) continue;
                var outDir = e.Side switch { 0 => new Vector2(0, 1), 1 => new Vector2(0, -1), 2 => new Vector2(1, 0), _ => new Vector2(-1, 0) };
                var front = mid + outDir * (e.Depth + 0.8f);
                if (rng.NextDouble() < 0.6) AddFlat(rng.NextDouble() < 0.5 ? "decals/decal_puddle_big" : "decals/decal_puddle", front + new Vector2((float)rng.NextDouble() - 0.5f, 0.3f), 0.8f + (float)rng.NextDouble() * 0.4f, rng);
                AddFlat("decals/decal_cigarette_butts", front + new Vector2(0.6f, 0), 1f, rng);
                if (rng.NextDouble() < 0.5) AddFlat("decals/decal_trash_scatter", front + new Vector2(-0.8f, 0.4f), 0.8f, rng);
                if (e.Ramp) AddFlat("decals/decal_tire_tracks_mud", front + outDir * 1.5f, 1.1f, rng, e.Side < 2 ? 0 : 90);
                SlabPath(m, e, mid, outDir, rng);
            }
        }

        /// <summary>
        /// Пристройки к корпусам (Г/Т-силуэт): комната 5–9 × 3,5–5 м со своей крышей и наружной дверью у глухой стены корпуса,
        /// а если на этом участке стены была дверь — тамбур перед ней (проход сохраняется, крыльцо переезжает на новую дверь).
        /// Место проверяется: ничего не стоит, не дорога, не лут, не чужие входы, перед новой дверью свободно.
        /// </summary>
        void AddWings(Monument m, List<(Vector2 p, string kind)> tags, Random rng)
        {
            var roofs = Roofs.FindAll(r => m.Contains((r.Min + r.Max) * 0.5f));
            roofs.Sort((a, b) => ((b.Max - b.Min).X * (b.Max - b.Min).Y).CompareTo((a.Max - a.Min).X * (a.Max - a.Min).Y));
            string[] kinds = { "storage", "kitchen", "workshop", "office", "washroom", "storage", "barracks" };
            string[] roofTiles = { "tiles/roof_metal_patched", "tiles/roof_ruberoid_torn", "tiles/roof_tar", "tiles/roof_ruberoid_patched" };
            int[] sides = { 0, 2, 3 };
            foreach (var r in roofs)
            {
                var size = r.Max - r.Min;
                float area = size.X * size.Y;
                if (area < 60) continue;
                int want = area > 300 ? 2 : 1;
                var why = new int[8];
                for (int t = 0, made = 0; t < (r.Equals(roofs[0]) ? 400 : 150) && made < want; t++)
                {
                    int side = rng.NextDouble() < 0.15 ? 1 : sides[rng.Next(3)];   // 0 юг, 1 север, 2 восток, 3 запад
                    bool horiz = side < 2;
                    float edge = horiz ? size.X : size.Y;
                    float L = MathF.Min(4 + (float)rng.NextDouble() * 5, edge - 2), D = 3.0f + (float)rng.NextDouble() * 2f;
                    if (L < 4f) continue;
                    float lo = (horiz ? r.Min.X : r.Min.Y) + 0.6f, hi = (horiz ? r.Max.X : r.Max.Y) - 0.6f - L;
                    if (hi < lo) continue;
                    float t0 = MathF.Max(lo, MathF.Round(lo + (float)rng.NextDouble() * (hi - lo)));
                    Vector2 mn, mx;
                    switch (side)
                    {
                        case 0: mn = new Vector2(t0, r.Max.Y); mx = new Vector2(t0 + L, r.Max.Y + D); break;
                        case 1: mn = new Vector2(t0, r.Min.Y - D); mx = new Vector2(t0 + L, r.Min.Y); break;
                        case 2: mn = new Vector2(r.Max.X, t0); mx = new Vector2(r.Max.X + D, t0 + L); break;
                        default: mn = new Vector2(r.Min.X - D, t0); mx = new Vector2(r.Min.X, t0 + L); break;
                    }
                    if (!m.Contains(mn, 2) || !m.Contains(mx, 2)) { why[0]++; continue; }
                    float line = side switch { 0 => r.Max.Y, 1 => r.Min.Y, 2 => r.Max.X, _ => r.Min.X };
                    // двери корпуса на этом участке стены → тамбур
                    var covered = Entrances.FindAll(e =>
                    {
                        var c = (e.A + e.B) * 0.5f;
                        if (e.Side != side) return false;
                        return horiz ? c.X > mn.X + 0.6f && c.X < mx.X - 0.6f && MathF.Abs(c.Y - line) < 0.5f
                                     : c.Y > mn.Y + 0.6f && c.Y < mx.Y - 0.6f && MathF.Abs(c.X - line) < 0.5f;
                    });
                    if (covered.Exists(e => e.Ramp)) { why[1]++; continue; }   // ворота и роллеты тамбуром не закрываем
                    // вход, задетый только краем, — нельзя
                    bool clash = false;
                    foreach (var e in Entrances)
                    {
                        if (covered.Contains(e)) continue;
                        var ext = e.Side switch { 0 => new Vector2(0, e.Depth), 1 => new Vector2(0, -e.Depth), 2 => new Vector2(e.Depth, 0), _ => new Vector2(-e.Depth, 0) };
                        var a0 = Vector2.Min(e.A, e.B + ext); var a1 = Vector2.Max(e.A + ext, e.B);
                        if (BoxHit(a0 - new Vector2(0.3f), a1 + new Vector2(0.3f), mn - new Vector2(0.3f), mx + new Vector2(0.3f))) { clash = true; break; }
                    }
                    if (clash) { why[1]++; continue; }
                    // пусто: отступ 0,35 м от стены корпуса, чтобы не цеплять её саму
                    var smn = mn; var smx = mx;
                    switch (side) { case 0: smn.Y += 0.35f; break; case 1: smx.Y -= 0.35f; break; case 2: smn.X += 0.35f; break; default: smx.X -= 0.35f; break; }
                    bool ok = true; int hard = 0, n = 0;
                    var clear = new HashSet<MapWall>();   // мелкие пропсы макета на месте пристройки — убрать (бочка, ящик, хлам)
                    float ex0 = side == 2 ? 0 : 0.3f, ex1 = side == 3 ? 0 : 0.3f, ey0 = side == 0 ? 0 : 0.3f, ey1 = side == 1 ? 0 : 0.3f;   // к стене корпуса — без запаса
                    for (float x = smn.X - ex0; x <= smx.X + ex1 && ok; x += 0.4f)
                        for (float y = smn.Y - ey0; y <= smx.Y + ey1 && ok; y += 0.4f)
                        {
                            var q = new Vector2(x, y);
                            n++; if (OnAsphalt(q)) hard++;
                            if (Roofs.Exists(o => x > o.Min.X - 0.1f && x < o.Max.X + 0.1f && y > o.Min.Y - 0.1f && y < o.Max.Y + 0.1f)) { ok = false; break; }
                            foreach (var w in WallsNear(q, 0.6f))
                            {
                                if (!Geo.CircleBox(q, 0.1f, w.Wall.Min, w.Wall.Max)) continue;
                                var ws = w.Wall.Max - w.Wall.Min;
                                if (w.Hidden && w.Prefab && ws.X < 3.5f && ws.Y < 3.5f && !RemovableWalls.Contains(w)) clear.Add(w); else { ok = false; break; }
                            }
                            if (ok && (NodesNear(q, 1.5f).Exists(nd => Vector2.Distance(nd.Pos, q) < nd.Radius + 0.1f) || Deployables.Exists(dp => Vector2.Distance(dp.Pos, q) < 1.5f))) ok = false;
                        }
                    if (!ok) { why[2]++; continue; }
                    if (hard > n * 0.25f) { why[3]++; continue; }   // на дорогу не строим
                    var gmn = mn - new Vector2(1.5f); var gmx = mx + new Vector2(1.5f);
                    if (Bags.Exists(b => BoxHit(b.Pos, b.Pos, gmn, gmx)) || Pickups.Exists(pk => BoxHit(pk.Pos, pk.Pos, gmn, gmx))) { why[4]++; continue; }
                    bool stash = false;
                    foreach (var st in AllStashes()) if (BoxHit(st.Pos, st.Pos, gmn, gmx)) { stash = true; break; }
                    if (stash) continue;
                    // наружная дверь: в дальней стене (у тамбура — против старой двери)
                    float along0 = horiz ? mn.X : mn.Y, along1 = horiz ? mx.X : mx.Y;
                    float dc = covered.Count > 0 ? (horiz ? (covered[0].A.X + covered[0].B.X) / 2 : (covered[0].A.Y + covered[0].B.Y) / 2) : along0 + 1.2f + (float)rng.NextDouble() * (along1 - along0 - 2.4f);
                    dc = Math.Clamp(dc, along0 + 1.0f, along1 - 1.0f);
                    float far = side switch { 0 => mx.Y, 1 => mn.Y, 2 => mx.X, _ => mn.X };
                    float outS = side is 0 or 2 ? 1 : -1;
                    var doorOut = horiz ? new Vector2(dc, far + outS * 1.5f) : new Vector2(far + outS * 1.5f, dc);
                    bool doorFree = m.Contains(doorOut, 6);
                    for (float k = -1; k <= 1 && doorFree; k += 0.5f)
                        doorFree = !WalkBlocked(doorOut + (horiz ? new Vector2(k, 0) : new Vector2(0, k)), 0.3f);
                    if (!doorFree) { why[5]++; continue; }
                    // стены: материал — как у корпуса
                    string tex = "walls/wallface_brick";
                    float bestD = 99;
                    foreach (var w in Walls)
                    {
                        if (!w.Prefab || w.Hidden || w.Tex == null || IsGateTex(w.Tex) || w.Tex.Contains("fence") || w.Tex.Contains("window") || w.Tex.Contains("storefront") || w.Tex.Contains("chainlink")) continue;
                        float dd = Vector2.Distance((w.Wall.Min + w.Wall.Max) * 0.5f, (mn + mx) * 0.5f);
                        if (dd < bestD && dd < 20) { bestD = dd; tex = w.Tex; }
                    }
                    const float h = 3.0f;
                    if (horiz)
                    {
                        WallT(new Vector2(mn.X, line), new Vector2(mn.X, far), h, tex);
                        WallT(new Vector2(mx.X, line), new Vector2(mx.X, far), h, tex);
                        WallT(new Vector2(mn.X, far), new Vector2(dc - 0.65f, far), h, tex);
                        WallT(new Vector2(dc + 0.65f, far), new Vector2(mx.X, far), h, tex);
                    }
                    else
                    {
                        WallT(new Vector2(line, mn.Y), new Vector2(far, mn.Y), h, tex);
                        WallT(new Vector2(line, mx.Y), new Vector2(far, mx.Y), h, tex);
                        WallT(new Vector2(far, mn.Y), new Vector2(far, dc - 0.65f), h, tex);
                        WallT(new Vector2(far, dc + 0.65f), new Vector2(far, mx.Y), h, tex);
                    }
                    foreach (var w in clear) { Walls.Remove(w); _wallGrid.Remove(w); }
                    // спрайты на месте пристройки — спрятать (id декора не трогаем: по нему клиент двигает подвижные)
                    foreach (var d in Decors)
                        if (!d.Flat && d.Pos.X > mn.X - 0.2f && d.Pos.X < mx.X + 0.2f && d.Pos.Y > mn.Y && d.Pos.Y < mx.Y + 0.6f && !MovingDecors.Contains(d)) d.Sprite = "none";
                    foreach (var e in covered) Entrances.Remove(e);
                    Roof(roofTiles[rng.Next(roofTiles.Length)], mn, mx, rng);
                    AddEntrances(Roofs.Count - 1);
                    tags.Add(((mn + mx) * 0.5f, covered.Count > 0 ? "corridor" : kinds[rng.Next(kinds.Length)]));
                    DressReport.Add($"   пристройка {(covered.Count > 0 ? "тамбур" : "комната")} {mn - m.Min}–{mx - m.Min}");
                    made++;
                }
                if (r.Equals(roofs[0])) DressReport.Add($"   крыло: отказы {string.Join(",", why)}");
            }
        }

        static bool BoxHit(Vector2 amin, Vector2 amax, Vector2 bmin, Vector2 bmax) =>
            MathF.Min(amin.X, amax.X) < bmax.X && MathF.Max(amin.X, amax.X) > bmin.X && MathF.Min(amin.Y, amax.Y) < bmax.Y && MathF.Max(amin.Y, amax.Y) > bmin.Y;

        /// <summary>
        /// Пост ГАИ стоит на асфальте — забор там не ставят: на дороге у будки «змейка» из бетонных блоков (полосы в разбежку,
        /// проезд остаётся), конусы по осевой, позиция из мешков с песком у будки.
        /// </summary>
        void GaiCheckpoint(Monument m, Random rng)
        {
            var post = Decors.Find(d => d.Sprite.EndsWith("gai_post") && m.Contains(d.Pos));
            if (post == null) return;
            // дорога: ближайшая полоса асфальта слева или справа от будки
            float? x0 = null, x1 = null;
            foreach (int dir in new[] { -1, 1 })
            {
                for (float x = post.Pos.X + dir * 3; MathF.Abs(x - post.Pos.X) < 25; x += dir * 0.5f)
                {
                    var q = new Vector2(x, post.Pos.Y - 1);
                    if (OnAsphalt(q)) { if (x0 == null) x0 = x; x1 = x; }
                    else if (x0 != null) break;
                }
                if (x0 != null) break;
            }
            if (x0 == null || x1 == null) return;
            float lo = MathF.Min(x0.Value, x1.Value), hi = MathF.Max(x0.Value, x1.Value), c = (lo + hi) / 2;
            if (hi - lo < 5) return;
            void Block(Vector2 p, string s)
            {
                var f = FootOf(s);
                float w = f?.w ?? 1.2f, d = f?.d ?? 0.5f;
                for (float x = -w / 2; x <= w / 2; x += 0.4f) if (WalkBlocked(p + new Vector2(x, -d / 2), 0.3f)) return;
                Prop(s, p, rng.NextDouble() < 0.5);
            }
            // две линии блоков в разбежку: севернее будки перекрыта левая половина, южнее — правая
            for (float x = lo + 0.7f; x < c + 0.3f; x += 1.3f) Block(new Vector2(x, post.Pos.Y - 9), "props/concrete_barrier");
            for (float x = hi - 0.7f; x > c - 0.3f; x -= 1.3f) Block(new Vector2(x, post.Pos.Y + 5), "props/concrete_barrier");
            for (float y = post.Pos.Y - 7.5f; y < post.Pos.Y + 4; y += 2.2f) { var cn = Decor("clutter/clutter_cone", new Vector2(c + (float)(rng.NextDouble() - 0.5) * 0.4f, y), rng.NextDouble() < 0.5); cn.Batch = true; }
            // мешки с песком у будки со стороны дороги
            float side = post.Pos.X > c ? -1 : 1;
            Block(new Vector2(post.Pos.X + side * 4.2f, post.Pos.Y + 1.6f), "props/sandbag_wall");
            Block(new Vector2(post.Pos.X + side * 4.2f, post.Pos.Y - 5.6f), "props/sandbag_corner");
            DressReport.Add($"   пост ГАИ: дорога {lo - m.Min.X:0}–{hi - m.Min.X:0}");
        }

        /// <summary>
        /// Ломаем «коробку»: к наружным стенам приставлены сарайчик, мусорные баки, поленница, кольца, кабельный шкаф,
        /// газовая труба вдоль фасада, куча кирпича — по 1–3 на здание, не у входов и не на дороге.
        /// </summary>
        void AddAnnexes(Monument m, List<MapRoof> roofs, Random rng)
        {
            string[] sideItems = { "outdoor/shed_wood", "outdoor/dumpster_skip_side", "outdoor/concrete_rings", "outdoor/ruins_brick_pile", "outdoor/trash_containers_row_angled", "outdoor/shed_wood_angled" };
            string[] southItems = { "outdoor/gas_pipe_side", "outdoor/electric_cabinet", "outdoor/firewood_stack_side", "outdoor/trash_container", "outdoor/tires_pile", "outdoor/log_pile_side", "outdoor/gas_pipe_side" };
            foreach (var r in roofs)
            {
                var size = r.Max - r.Min;
                if (size.X * size.Y < 40) continue;
                int want = 1 + (size.X * size.Y > 200 ? 1 : 0) + rng.Next(2);
                for (int t = 0, made = 0; t < 30 && made < want; t++)
                {
                    int side = rng.Next(3);   // 0 — юг, 1 — восток, 2 — запад (север за зданием не видно)
                    string s = side == 0 ? southItems[rng.Next(southItems.Length)] : sideItems[rng.Next(sideItems.Length)];
                    var f = FootOf(s);
                    if (f == null) continue;
                    var (w, d, h, legs) = f.Value;
                    Vector2 p = side switch
                    {
                        0 => new Vector2(Lerp(r.Min.X + w / 2 + 0.5f, r.Max.X - w / 2 - 0.5f, (float)rng.NextDouble()), r.Max.Y + 0.2f + d),
                        1 => new Vector2(r.Max.X + 0.2f + w / 2, Lerp(r.Min.Y + d + 0.6f, r.Max.Y - 0.3f, (float)rng.NextDouble())),
                        _ => new Vector2(r.Min.X - 0.2f - w / 2, Lerp(r.Min.Y + d + 0.6f, r.Max.Y - 0.3f, (float)rng.NextDouble())),
                    };
                    if (!m.Contains(p, 3)) continue;
                    // свободно: след с запасом 0,8 м (кроме стороны у стены), не у входа, не на дороге, не в другом здании
                    var mn = new Vector2(p.X - w / 2 - 0.8f, p.Y - d - 0.8f); var mx = new Vector2(p.X + w / 2 + 0.8f, p.Y + 0.8f);
                    if (side == 0) mn.Y = r.Max.Y + 0.2f; else if (side == 1) mn.X = r.Max.X + 0.2f; else mx.X = r.Min.X - 0.2f;
                    bool ok = !Entrances.Exists(e => Vector2.Distance((e.A + e.B) * 0.5f, p) < e.Depth + w / 2 + 2.5f) && !(HardGround(p) && h > 1.5f);
                    for (float x = mn.X; x <= mx.X && ok; x += 0.4f)
                        for (float y = mn.Y; y <= mx.Y && ok; y += 0.4f)
                            ok = !WalkBlocked(new Vector2(x, y), 0.05f) && !Roofs.Exists(q => x > q.Min.X && x < q.Max.X && y > q.Min.Y && y < q.Max.Y);
                    if (!ok) continue;
                    Prop(s, p, side == 2 || rng.NextDouble() < 0.3);
                    made++;
                }
            }
        }

        /// <summary>
        /// Забор по периметру РТ, где своего нет: куски рабицы с колючкой, бетонных ПО-2 или профлиста по 6–14 м с проломами
        /// (не замкнут — РТ остаётся проходимой со всех сторон), на дороге — проезд и будка КПП, у концов — поваленные секции.
        /// </summary>
        void AddPerimeter(Monument m, Random rng)
        {
            foreach (var w in Walls)
                if (w.Prefab && !w.Hidden && w.Tex != null && w.Tex.Contains("fence") && m.Contains((w.Wall.Min + w.Wall.Max) * 0.5f, 2)) return;   // свой забор уже есть
            string[] texs = { "walls/wallface_chainlink", "walls/wallface_chainlink", "walls/wallface_fence_po2", "walls/fence_metal_profile" };
            var gates = new List<(Vector2 a, Vector2 b, int side)>();
            for (int side = 0; side < 4; side++)
            {
                const float off = 1.5f;
                bool horiz = side < 2;
                float line = side switch { 0 => m.Max.Y + off, 1 => m.Min.Y - off, 2 => m.Max.X + off, _ => m.Min.X - off };
                float lo = (horiz ? m.Min.X : m.Min.Y) - off, hi = (horiz ? m.Max.X : m.Max.Y) + off;
                Vector2 P(float t) => horiz ? new Vector2(t, line) : new Vector2(line, t);
                string tex = texs[rng.Next(texs.Length)];
                float h = tex.Contains("chainlink") ? 2.0f : 2.2f;
                // что можно огородить: не дорога, не вход, ничего не стоит
                bool Free(Vector2 p) => !HardGround(p) && !WalkBlocked(p, 0.6f) && !Entrances.Exists(e => Vector2.Distance((e.A + e.B) * 0.5f, p) < e.Depth + 4)
                    && !Roofs.Exists(r => p.X > r.Min.X - 1 && p.X < r.Max.X + 1 && p.Y > r.Min.Y - 1 && p.Y < r.Max.Y + 1);
                float runStart = float.NaN, hardStart = float.NaN;
                var runs = new List<(float a, float b)>();
                for (float t = lo; t <= hi + 0.01f; t += 0.5f)
                {
                    var p = P(t);
                    bool free = t < hi && Free(p);
                    if (free && float.IsNaN(runStart)) runStart = t;
                    if (!free && !float.IsNaN(runStart)) { runs.Add((runStart, t - 0.5f)); runStart = float.NaN; }
                    bool hard = t < hi && HardGround(p);
                    if (hard && float.IsNaN(hardStart)) hardStart = t;
                    if (!hard && !float.IsNaN(hardStart)) { if (t - hardStart >= 3.5f) gates.Add((P(hardStart), P(t), side)); hardStart = float.NaN; }
                }
                foreach (var (a, b) in runs)
                {
                    for (float t = a + (float)rng.NextDouble() * 4; t < b - 3; )
                    {
                        float len = MathF.Min(6 + (float)rng.NextDouble() * 8, b - t);
                        if (len < 3) break;
                        if (rng.NextDouble() < 0.72)
                        {
                            var wall = WallT(P(t), P(t + len), h, tex, tex.Contains("chainlink") ? 0.15f : 0.25f);
                            if (tex.Contains("chainlink")) wall.PassBullets = true;   // сетка: видно и простреливается
                            // у конца куска — поваленная секция или бурьян
                            var end = P(t + len + 1.6f) + (horiz ? new Vector2(0, side == 0 ? 0.8f : -0.4f) : new Vector2(side == 2 ? 0.6f : -0.6f, 0));
                            if (rng.NextDouble() < 0.35 && !WalkBlocked(end, 1.2f)) { var fd = Decor(tex.Contains("chainlink") ? "outdoor/fence_chainlink_torn" : "props/fence_panels_fallen", end, rng.NextDouble() < 0.5); fd.Batch = horiz; }
                            for (float k = t; k < t + len; k += 0.8f + (float)rng.NextDouble())
                                if (rng.NextDouble() < 0.4) { var wd = Decor(rng.NextDouble() < 0.5 ? "grass/weeds_wall" : "grass/grass_meadow_1", P(k) + (horiz ? new Vector2(0, 0.35f) : new Vector2(0.3f, 0)), rng.NextDouble() < 0.5); wd.Batch = true; }
                        }
                        t += len + 4 + (float)rng.NextDouble() * 6;
                    }
                }
            }
            // на асфальте забор не ставят: проезд перекрыт бетонными блоками по краям, в полосе — «змейка», конусы, мешки
            foreach (var (a, b, side) in gates)
            {
                float len = Vector2.Distance(a, b);
                if (len < 6) continue;
                var along = Vector2.Normalize(b - a);
                var inward = side switch { 0 => new Vector2(0, -1), 1 => new Vector2(0, 1), 2 => new Vector2(-1, 0), _ => new Vector2(1, 0) };
                float lane0 = len / 2 - 2.6f, lane1 = len / 2 + 2.6f;   // полоса 5,2 м посередине свободна
                bool horizLine = MathF.Abs(along.X) > 0.5f;
                void Block(Vector2 p, string s)
                {
                    var f = FootOf(s);
                    float w = f?.w ?? 1.2f, d = f?.d ?? 0.5f;
                    for (float x = -w / 2; x <= w / 2; x += 0.4f) if (WalkBlocked(p + new Vector2(x, -d / 2), 0.35f)) return;
                    Prop(s, p, rng.NextDouble() < 0.5);
                }
                for (float t = 0.8f; t < len - 0.6f; t += 1.35f)
                {
                    if (t > lane0 - 0.7f && t < lane1 + 0.7f) continue;
                    if (rng.NextDouble() < 0.2) { t += 1.2f; continue; }   // выбитый блок — щель
                    var p = a + along * t;
                    Block(horizLine ? p : p + new Vector2(0, 0.3f), horizLine ? "props/concrete_barrier" : "props/concrete_barrier");
                }
                // «змейка» в полосе: два блока внутрь территории в разбежку
                Block(a + along * (lane0 + 1.0f) + inward * 3.5f, "props/concrete_barrier");
                Block(a + along * (lane1 - 1.0f) + inward * 7f, "props/concrete_barrier");
                foreach (float t in new[] { lane0, lane1 })
                    if (rng.NextDouble() < 0.8) { var c = Decor("clutter/clutter_cone", a + along * t + inward * 0.6f, rng.NextDouble() < 0.5); c.Batch = true; }
                // мешки с песком у края полосы, будто пост
                var sb = a + along * (lane0 - 2.2f) + inward * 2.2f;
                if (rng.NextDouble() < 0.7) Block(sb, "props/sandbag_wall");
            }
            // КПП у самого широкого проезда: будка сбоку, внутри территории
            gates.Sort((x, y) => Vector2.Distance(y.a, y.b).CompareTo(Vector2.Distance(x.a, x.b)));
            foreach (var (a, b, side) in gates)
            {
                var f = FootOf("outdoor/kpp_booth");
                if (f == null) break;
                var inward = side switch { 0 => new Vector2(0, -1), 1 => new Vector2(0, 1), 2 => new Vector2(-1, 0), _ => new Vector2(1, 0) };
                var along = Vector2.Normalize(b - a);
                bool placed = false;
                foreach (var basePt in new[] { a - along * 2.2f, b + along * 2.2f })
                {
                    var p = basePt + inward * 2.6f;
                    var mn = p - new Vector2(f.Value.w / 2 + 0.6f, f.Value.d + 0.6f); var mx = p + new Vector2(f.Value.w / 2 + 0.6f, 0.6f);
                    bool ok = !HardGround(p);
                    for (float x = mn.X; x <= mx.X && ok; x += 0.5f) for (float y = mn.Y; y <= mx.Y && ok; y += 0.5f) ok = !WalkBlocked(new Vector2(x, y), 0.2f) && !Roofs.Exists(r => x > r.Min.X && x < r.Max.X && y > r.Min.Y && y < r.Max.Y);
                    if (!ok) continue;
                    Prop("outdoor/kpp_booth", p, side == 3);
                    AddFlat("decals/decal_cigarette_butts", p + new Vector2(0.4f, 0.5f), 1f, rng);
                    Ambients.Add(new MapAmbient { Kind = "flag", Pos = p + new Vector2(f.Value.w / 2 - 0.3f, -f.Value.d * 0.5f), H = 2.6f });
                    placed = true;
                    break;
                }
                if (placed) break;
            }
        }

        /// <summary>Тропа из бетонных плит ПАГ от входа до дороги/площадки (или до 12 м), если земля под ней мягкая.</summary>
        void SlabPath(Monument m, MapEntrance e, Vector2 mid, Vector2 dir, Random rng)
        {
            float width = MathF.Max(2f, (e.B - e.A).Length() + 0.4f);
            if (e.Ramp) width = MathF.Max(width, 3.2f);
            var start = mid + dir * (e.Depth + 0.15f);
            float len = 0;
            for (float t = 0; t <= 12; t += 0.5f)
            {
                var p = start + dir * t;
                if (!m.Contains(p, 1) || HardGround(p) || Roofs.Exists(r => p.X > r.Min.X && p.X < r.Max.X && p.Y > r.Min.Y && p.Y < r.Max.Y) || WalkBlocked(p, 0.4f)) break;
                len = t;
            }
            if (len < 2) return;
            var side = new Vector2(-dir.Y, dir.X) * (width / 2);
            var a = start - side; var b = start + side + dir * (len + 0.4f);
            Patches.Add(new MapPatch { Tile = "tiles/ground_concrete", Min = Vector2.Min(a, b), Max = Vector2.Max(a, b), Slab = dir.Y != 0 ? 2 : 1 });
            // трещины и трава в швах
            for (float t = 1; t < len; t += 1.5f + (float)rng.NextDouble() * 2)
                AddFlat(rng.NextDouble() < 0.5 ? "decals/decal_crack_concrete" : "decals/decal_grass_tuft_2", start + dir * t + side * ((float)rng.NextDouble() - 0.5f), 0.8f, rng);
        }

        bool OnAsphalt(Vector2 p)
        {
            foreach (var pt in Patches)
            {
                if (pt.Tile == null || !pt.Tile.Contains("asphalt")) continue;
                if (pt.Poly != null) { if (InPoly(p, pt.Poly)) return true; }
                else if (p.X >= pt.Min.X && p.X <= pt.Max.X && p.Y >= pt.Min.Y && p.Y <= pt.Max.Y) return true;
            }
            return false;
        }

        bool HardGround(Vector2 p)
        {
            foreach (var pt in Patches)
            {
                if (pt.Tile == null || !(pt.Tile.Contains("asphalt") || pt.Tile.Contains("concrete") || pt.Tile.Contains("gravel") || pt.Tile.Contains("floor"))) continue;
                if (pt.Poly != null) { if (InPoly(p, pt.Poly)) return true; }
                else if (p.X >= pt.Min.X && p.X <= pt.Max.X && p.Y >= pt.Min.Y && p.Y <= pt.Max.Y) return true;
            }
            return false;
        }

        /// <summary>Теплотрасса между зданиями РТ: на опорах ~1,1 м, П-образный подъём над проходами и посередине длинных участков.</summary>
        int AddHeatingMains(Monument m, List<MapRoof> roofs, Random rng)
        {
            if (roofs.Count < 2) return 0;
            int made = 0;
            var order = new List<MapRoof>(roofs);
            order.Sort((a, b) => ((b.Max - b.Min).X * (b.Max - b.Min).Y).CompareTo((a.Max - a.Min).X * (a.Max - a.Min).Y));
            var linked = new HashSet<int>();
            for (int ai = 0; ai < order.Count && made < 3; ai++)
            {
                var A = order[ai];
                if ((A.Max - A.Min).X * (A.Max - A.Min).Y < 40) continue;   // пристройки — не концы трассы
                // ближайшие другие здания (не вплотную) по очереди, пока трасса не найдётся
                var cand = new List<int>();
                for (int bj = 0; bj < order.Count; bj++)
                {
                    if (bj == ai || linked.Contains(ai * 100 + bj) || linked.Contains(bj * 100 + ai)) continue;
                    var B = order[bj];
                    float gap = RectGap(A, B);
                    if (gap < 5 || gap > 45 || (B.Max - B.Min).X * (B.Max - B.Min).Y < 40) continue;
                    cand.Add(bj);
                }
                cand.Sort((x, y) => RectGap(A, order[x]).CompareTo(RectGap(A, order[y])));
                int bestJ = -1;
                List<Vector2> route = null;
                foreach (int bj in cand) { route = PipeRoute(A, order[bj], rng); if (route != null) { bestJ = bj; break; } }
                if (route == null) continue;
                linked.Add(ai * 100 + bestJ);
                // высоты: 1,1 м, П-подъём 4,2 м над входами и в середине длинных прямых
                var pts = new List<Vector2>(); var hs = new List<float>();
                const float low = 1.1f, high = 4.2f;
                for (int s = 0; s < route.Count - 1; s++)
                {
                    var a = route[s]; var b = route[s + 1];
                    float len = Vector2.Distance(a, b);
                    var dir = len > 0 ? (b - a) / len : Vector2.Zero;
                    pts.Add(a); hs.Add(low);
                    var bumps = new List<float>();
                    foreach (var e in Entrances)
                    {
                        var em = (e.A + e.B) * 0.5f;
                        float t = Vector2.Dot(em - a, dir);
                        if (t > 2 && t < len - 2 && DistToSeg(em, a, b) < e.Depth + 3) bumps.Add(t);
                    }
                    if (bumps.Count == 0 && len > 16) bumps.Add(len * (0.35f + (float)rng.NextDouble() * 0.3f));
                    bumps.Sort();
                    float last = -10;
                    foreach (float t in bumps)
                    {
                        if (t - last < 7) continue;
                        last = t;
                        float h0 = t - 2.2f, h1 = t + 2.2f;
                        pts.Add(a + dir * h0); hs.Add(low);
                        pts.Add(a + dir * h0); hs.Add(high);
                        pts.Add(a + dir * h1); hs.Add(high);
                        pts.Add(a + dir * h1); hs.Add(low);
                        Ambients.Add(new MapAmbient { Kind = "steam", Pos = a + dir * (h1 + 0.2f), H = low + 0.2f });
                    }
                }
                pts.Add(route[^1]); hs.Add(low);
                Pipes.Add(new MapPipe { P = pts.ToArray(), H = hs.ToArray() });
                DressReport.Add("   труба: " + string.Join(" ", pts.ConvertAll(q => $"({q.X - m.Min.X:0.0};{q.Y - m.Min.Y:0.0})")) + " h " + string.Join(",", hs));
                if (rng.NextDouble() < 0.7) Ambients.Add(new MapAmbient { Kind = "steam", Pos = Vector2.Lerp(route[0], route[1], 0.5f), H = low + 0.1f });
                made++;
            }
            return made;
        }

        static bool InPoly(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) inside = !inside;
            return inside;
        }

        static float RectGap(MapRoof a, MapRoof b)
        {
            float dx = MathF.Max(0, MathF.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
            float dy = MathF.Max(0, MathF.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        static float DistToSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.LengthSquared() < 1e-6f ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Г-образная трасса от стены A до стены B (по осям), не через другие здания и не по стенам.</summary>
        List<Vector2> PipeRoute(MapRoof A, MapRoof B, Random rng)
        {
            var ca = (A.Min + A.Max) * 0.5f; var cb = (B.Min + B.Max) * 0.5f;
            var cands = new List<List<Vector2>>();
            // выход из A по горизонтали или вертикали в сторону B
            float yA = Math.Clamp(cb.Y, A.Min.Y + 1, A.Max.Y - 1), xA = Math.Clamp(cb.X, A.Min.X + 1, A.Max.X - 1);
            float yB = Math.Clamp(ca.Y, B.Min.Y + 1, B.Max.Y - 1), xB = Math.Clamp(ca.X, B.Min.X + 1, B.Max.X - 1);
            // прямая по x, если высоты перекрываются
            float oy0 = MathF.Max(A.Min.Y, B.Min.Y) + 1, oy1 = MathF.Min(A.Max.Y, B.Max.Y) - 1;
            if (oy1 > oy0)
            {
                bool right = cb.X > ca.X;
                foreach (float k in new[] { 0.5f, 0.2f, 0.8f })
                {
                    float y = oy0 + (oy1 - oy0) * k;
                    cands.Add(new List<Vector2> { new(right ? A.Max.X + 0.4f : A.Min.X - 0.4f, y), new(right ? B.Min.X - 0.4f : B.Max.X + 0.4f, y) });
                }
            }
            float ox0 = MathF.Max(A.Min.X, B.Min.X) + 1, ox1 = MathF.Min(A.Max.X, B.Max.X) - 1;
            if (ox1 > ox0)
            {
                bool down = cb.Y > ca.Y;
                foreach (float k in new[] { 0.5f, 0.2f, 0.8f })
                {
                    float x = ox0 + (ox1 - ox0) * k;
                    cands.Add(new List<Vector2> { new(x, down ? A.Max.Y + 0.4f : A.Min.Y - 0.4f), new(x, down ? B.Min.Y - 0.4f : B.Max.Y + 0.4f) });
                }
            }
            // Г: из A по x, в B по y (и наоборот); угол — в нескольких местах вдоль стен, чтобы обойти пристройки и оборудование
            foreach (float k in new[] { 0.5f, 0.2f, 0.8f })
            {
                float ya = A.Min.Y + 1 + (A.Max.Y - A.Min.Y - 2) * k, xb = B.Min.X + 1 + (B.Max.X - B.Min.X - 2) * k;
                float xa = A.Min.X + 1 + (A.Max.X - A.Min.X - 2) * k, yb = B.Min.Y + 1 + (B.Max.Y - B.Min.Y - 2) * k;
                {
                    bool right = cb.X > A.Max.X, down = ca.Y < B.Min.Y;
                    if ((right || cb.X < A.Min.X) && (down || ca.Y > B.Max.Y))
                        cands.Add(new List<Vector2> { new(right ? A.Max.X + 0.4f : A.Min.X - 0.4f, ya), new(xb, ya), new(xb, down ? B.Min.Y - 0.4f : B.Max.Y + 0.4f) });
                }
                {
                    bool down = cb.Y > A.Max.Y, right = ca.X < B.Min.X;
                    if ((down || cb.Y < A.Min.Y) && (right || ca.X > B.Max.X))
                        cands.Add(new List<Vector2> { new(xa, down ? A.Max.Y + 0.4f : A.Min.Y - 0.4f), new(xa, yb), new(right ? B.Min.X - 0.4f : B.Max.X + 0.4f, yb) });
                }
            }
            // запретные зоны: крупное оборудование с запасом и площадки, где его много (ОРУ подстанции)
            var bans = new List<(Vector2 min, Vector2 max)>();
            var lo = Vector2.Min(A.Min, B.Min) - new Vector2(40); var hi = Vector2.Max(A.Max, B.Max) + new Vector2(40);
            var equip = new List<Vector2>();
            foreach (var d in Decors)
            {
                if (d.Flat || d.Batch || d.Pos.X < lo.X || d.Pos.X > hi.X || d.Pos.Y < lo.Y || d.Pos.Y > hi.Y) continue;
                var f = FootOf(d.Sprite);
                if (f == null || f.Value.w < 3f) continue;
                string id = d.Sprite;
                if (!(id.Contains("subst_") || id.Contains("transformer") || id.Contains("diesel") || id.Contains("ktp") || id.Contains("tank") || id.Contains("dish") || id.Contains("mast") || id.Contains("generator"))) continue;   // энергооборудование, баки, антенны
                equip.Add(d.Pos);
                bans.Add((d.Pos - new Vector2(f.Value.w / 2 + 0.5f, f.Value.d + 0.5f), d.Pos + new Vector2(f.Value.w / 2 + 0.5f, 0.5f)));
            }
            foreach (var pt in Patches)
            {
                if (pt.Tile == null || !pt.Tile.Contains("gravel")) continue;
                Vector2 mn = pt.Min, mx = pt.Max;
                if (pt.Poly != null) { mn = new Vector2(float.MaxValue); mx = new Vector2(float.MinValue); foreach (var q in pt.Poly) { mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); } }
                if (equip.FindAll(e => e.X > mn.X && e.X < mx.X && e.Y > mn.Y && e.Y < mx.Y).Count >= 4) bans.Add((mn, mx));   // ОРУ: много аппаратов на гравии
            }
            foreach (var c in cands)
            {
                bool ok = true;
                for (int i = 0; i < c.Count - 1 && ok; i++)
                {
                    var a = c[i]; var b = c[i + 1];
                    float len = Vector2.Distance(a, b);
                    if (len < 0.5f) continue;
                    for (float t = 0.3f; t < len - 0.3f && ok; t += 0.5f)
                    {
                        var p = Vector2.Lerp(a, b, t / len);
                        foreach (var r in Roofs) if (p.X > r.Min.X - 0.3f && p.X < r.Max.X + 0.3f && p.Y > r.Min.Y - 0.3f && p.Y < r.Max.Y + 0.3f) { ok = false; break; }
                        if (ok) foreach (var (bm, bx) in bans) if (p.X > bm.X && p.X < bx.X && p.Y > bm.Y && p.Y < bx.Y) { ok = false; break; }
                        if (ok) foreach (var w in WallsNear(p, 0.6f)) if (!w.Hidden && w.Wall.Height > 1.2f && Geo.CircleBox(p, 0.25f, w.Wall.Min, w.Wall.Max)) { ok = false; break; }
                    }
                }
                if (ok) return c;
            }
            return null;
        }

        /// <summary>Живое: дым из труб на крышах, флаг, вороны на открытых местах, бумажки, качающиеся лампы в комнатах.</summary>
        void AddAmbients(Monument m, List<MapRoof> roofs, List<DressRoom> rooms, Random rng)
        {
            foreach (var r in roofs)
                foreach (var it in r.Items ?? new List<RoofItem>())
                    if ((it.Sprite.Contains("chimney") || it.Sprite.Contains("exhaust")) && rng.NextDouble() < 0.7)
                        Ambients.Add(new MapAmbient { Kind = "smoke", Pos = it.Pos, H = 2.8f + 1.0f });
            // флаг: на крыше самого большого здания или у ворот
            if (roofs.Count > 0)
            {
                var big = roofs[0];
                foreach (var r in roofs) if ((r.Max - r.Min).LengthSquared() > (big.Max - big.Min).LengthSquared()) big = r;
                Ambients.Add(new MapAmbient { Kind = "flag", Pos = new Vector2(big.Max.X - 1.2f, big.Min.Y + 1.0f), H = 2.8f });
            }
            // открытые места снаружи: вороны и бумажки
            int crows = 1 + (int)((m.Max - m.Min).X * (m.Max - m.Min).Y / 3500f), papers = 2 + rng.Next(3);
            for (int t = 0; t < 300 && (crows > 0 || papers > 0); t++)
            {
                var p = new Vector2(Lerp(m.Min.X + 3, m.Max.X - 3, (float)rng.NextDouble()), Lerp(m.Min.Y + 3, m.Max.Y - 3, (float)rng.NextDouble()));
                if (Roofs.Exists(r => p.X > r.Min.X - 2 && p.X < r.Max.X + 2 && p.Y > r.Min.Y - 2 && p.Y < r.Max.Y + 2) || WalkBlocked(p, 1.2f)) continue;
                if (crows > 0) { Ambients.Add(new MapAmbient { Kind = "crows", Pos = p }); crows--; }
                else { Ambients.Add(new MapAmbient { Kind = "papers", Pos = p }); papers--; }
            }
            // лампы под потолком: в части комнат, одна из трёх — мигает
            foreach (var r in rooms)
            {
                if (r.Area < 8 || rng.NextDouble() < 0.45) continue;
                var g = r.G;
                int c = r.Cells[r.Cells.Count / 2 + rng.Next(-r.Cells.Count / 6, r.Cells.Count / 6 + 1)];
                Ambients.Add(new MapAmbient { Kind = rng.NextDouble() < 0.35 ? "lamp_flicker" : "lamp", Pos = g.C(c % g.W, c / g.W), H = 2.6f });
            }
        }
    }
}
