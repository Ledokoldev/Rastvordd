using System;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Мелкий реквизит мира (props/rprop_*, по 3 варианта): кучки по 3–7 предметов там, где им место.
    /// Только декор, не лут. На полотне дороги не ставим никогда (RoadDist > 3.5 м для каждого предмета).
    ///  обочина     — шины, конусы, упавшие знаки, автохлам, мусор: 4–14 м от дороги
    ///  окраина     — быт, стройматериалы, бельё, хозяйство: 20–90 м от посёлка (РТ)
    ///  поле/хутор  — сено, плуг, телега, бидоны, ульи, колонка: открытое поле 60–350 м от посёлка
    ///  лес         — пни, валежник, корни, грибы, ягоды, кости, валуны: внутри леса
    ///  берег       — камыш, лодка, удочки, кострище, палатка: 3–15 м от реки/озера
    ///  военное     — мешки, колючка, ежи, каски, ленты: высоты вдали от дорог
    ///  кладбище    — кресты, надгробия: поле у посёлка, на отшибе
    /// </summary>
    public sealed partial class World
    {
        static readonly (string Name, int Clusters, float R, string[] Props)[] ScatterGroups =
        {
            ("roadside", 70, 5, new[] { "tire_car", "tire_stack", "road_cone", "road_sign_fallen", "road_barrier", "concrete_barrier", "car_door", "wheel_rim", "car_hood", "engine_block", "car_seat", "trash_bags", "oil_stain", "signpost", "lamp_post_broken", "electric_box", "billboard_broken", "graffiti_slab", "puddle_debris", "shopping_cart", "jerrycan" }),
            ("outskirts", 45, 7, new[] { "bench_park", "washing_machine", "fridge_old", "tv_crt", "mattress", "cardboard_boxes", "bicycle_old", "baby_pram", "chair_wood", "table_wood", "laundry_line", "wheelbarrow", "garden_hose", "watering_can", "toolbench_small", "ladder_wood", "pallet", "pallet_stack", "cinder_blocks", "bricks_pile", "pipes_stack", "cable_drum", "concrete_ring", "transformer_box", "pipe_valve", "tank_water_small", "kiosk", "phone_booth", "vending_soda", "newspaper_stand", "mailbox", "bus_stop_sign", "fire_hydrant", "manhole", "bucket_metal", "gas_cylinder" }),
            ("farm", 30, 8, new[] { "haystack_small", "hay_bales", "scarecrow", "plough_old", "cart_wooden", "milk_cans", "beehive", "trough", "well_concrete", "pump_hand", "bucket_metal", "logs_pile", "fence_wood_piece", "fence_mesh_piece", "wheelbarrow" }),
            ("forest", 90, 6, new[] { "stump", "log_fallen", "fallen_branch", "rootball", "mushrooms", "berry_bush", "fern", "anthill", "bush_dry", "deer_skull", "bones", "rocks_cluster", "boulder_big", "rock_small", "logs_pile" }),
            ("shore", 30, 4, new[] { "reeds", "boat_small", "fishing_rod_stand", "campfire_cold", "tent_small", "tarp_shelter", "bucket_metal", "rock_small" }),
            ("military", 18, 5, new[] { "sandbags", "barbed_coil", "tank_hedgehog", "trench_planks", "ammo_belts", "helmet_old", "gas_mask_ground", "radio_old", "bunker_door", "jerrycan" }),
            ("cemetery", 4, 6, new[] { "gravestone", "cross_wooden", "gravestone", "cross_wooden", "monument_small", "bush_dry" }),
        };

        public static bool NoScatter;
        void ScatterProps()
        {
            if (NoScatter) return;
            foreach (var g in ScatterGroups)
            {
                int placed = 0, groups = 0;
                for (int n = 0; n < g.Clusters; n++)
                {
                    Vector2 c = default; bool ok = false;
                    for (int t = 0; t < 400 && !ok; t++)
                    {
                        c = new Vector2(Rf(-HalfSize + 30, HalfSize - 30), Rf(-HalfSize + 30, HalfSize - 30));
                        ok = ScatterSpot(g.Name, c, g.R);
                    }
                    if (!ok) continue;
                    _lmAreas.Add((c, g.R)); groups++;
                    int k = _lr.Next(3, 8);
                    for (int i = 0; i < k; i++)
                    {
                        var b = g.Props[_lr.Next(g.Props.Length)];
                        var p = Around(c, 0, g.R);
                        if (!ScatterItemOk(p)) continue;
                        ScatterItem(b, p); placed++;
                    }
                }
                Console.WriteLine($"реквизит {g.Name}: кучек {groups}/{g.Clusters}, предметов {placed}");
            }
        }

        bool ScatterSpot(string kind, Vector2 c, float r)
        {
            if (!Terrain.IsLand(c) || Terrain.BiomeAt(c) is Biome.Water or Biome.Beach || NearMonument(c, r + 4)) return false;
            if (RoadDist(c) < r + 4) return false;
            foreach (var a in _lmAreas) if (Vector2.Distance(a.c, c) < a.r + r + 4) return false;
            foreach (var s in PlayerSpawns) if (Vector2.Distance(s, c) < r + 10) return false;
            float rd = RoadDist(c), w = WaterDist(c), m = MonDist(c);
            return kind switch
            {
                "roadside" => rd < r + 10 && w > 8,
                "outskirts" => m is > 12 and < 90 && w > 8,
                "farm" => Open(c) && m is > 60 and < 350 && w > 10,
                "forest" => Terrain.BiomeAt(c) == Biome.Forest && w > 8,
                "shore" => w is > 3 and < 15 && Terrain.BiomeAt(c) != Biome.Swamp,
                "military" => Hill(c) > 0.3f && rd > 40 && w > 10,
                "cemetery" => Open(c) && m is > 40 and < 160 && rd > 15,
                _ => false,
            };
        }

        bool ScatterItemOk(Vector2 p) =>
            Terrain.IsLand(p) && Terrain.BiomeAt(p) != Biome.Water && RoadDist(p) > 3.5f && Terrain.RiverNear(p) > 2.5f && !NearMonument(p, 2) && IsFree(p, 0.8f);

        void ScatterItem(string b, Vector2 p)
        {
            var (h, flat) = ScatterSize(b);
            string sprite = $"props/rprop_{b}_0{_lr.Next(1, 4)}";
            bool flip = _lr.Next(2) == 0;
            if (flat) { Flat(sprite, p); return; }
            if (h < 0.7f) { Decor(sprite, p, flip); return; }   // мелочь не мешает проходу
            float w = MathF.Min(MathF.Max(h * 0.9f, 0.6f), 4f);
            Solid(sprite, p, w, MathF.Min(w * 0.5f, 1.2f), h, flip);
        }

        static (float h, bool flat) ScatterSize(string b) => b switch
        {
            "oil_stain" or "puddle_debris" or "manhole" or "ammo_belts" or "bones" or "car_hood" or "garden_hose" or "pallet" or "gas_mask_ground" => (0.05f, true),
            "billboard_broken" => (4f, false),
            "lamp_post_broken" => (3f, false),
            "ladder_wood" or "kiosk" => (2.4f, false),
            "phone_booth" or "bus_stop_sign" or "rootball" or "tank_water_small" or "swing_playground" => (2f, false),
            "scarecrow" or "signpost" or "boulder_big" or "bunker_door" or "vending_soda" or "cross_wooden" or "laundry_line" => (1.7f, false),
            "haystack_small" or "hay_bales" or "fence_mesh_piece" or "electric_box" or "transformer_box" or "monument_small" or "newspaper_stand" or "reeds" or "tarp_shelter" or "cable_drum" or "pump_hand" or "graffiti_slab" or "tank_hedgehog" => (1.3f, false),
            "road_barrier" or "fence_wood_piece" or "cart_wooden" or "rocks_cluster" or "tent_small" or "gravestone" => (1f, false),
            "bench_park" or "washing_machine" or "fridge_old" or "chair_wood" or "baby_pram" or "toolbench_small" or "concrete_ring" or "beehive" or "plough_old" or "well_concrete" or "concrete_barrier" or "shopping_cart" or "fire_hydrant" or "tire_stack" or "table_wood" or "bush_dry" or "fern" or "pallet_stack" => (0.8f, false),
            _ => (0.5f, false),
        };
    }
}
