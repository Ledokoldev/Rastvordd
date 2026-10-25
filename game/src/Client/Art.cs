using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Спрайты из game/art (пиксель-арт, 32 px на метр, см. docs/design/10-art-bible.md).
    /// Если файла нет — возвращает null, и вид рисует заглушку кодом.
    /// </summary>
    public static class Art
    {
        static readonly Dictionary<string, Texture2D> Cache = new();

        static Texture2D _light;
        /// <summary>Мягкое круглое пятно для PointLight2D.</summary>
        public static Texture2D LightTex => _light ??= new GradientTexture2D
        {
            Width = 256, Height = 256, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
            Gradient = new Gradient { Colors = new[] { Colors.White, new Color(1, 1, 1, 0) } },
        };

        /// <summary>Тёплый свет огня.</summary>
        public static PointLight2D FireLight(float sizeM, float energy) => new PointLight2D
        {
            Texture = LightTex, TextureScale = sizeM * Iso.PPM * 2 / 256f, Energy = energy,
            Color = new Color(1f, 0.68f, 0.38f), BlendMode = Light2D.BlendModeEnum.Add,
            // огонь, факел, вспышка выстрела — с настоящими тенями от стен и техники (LightOccluder2D)
            ShadowEnabled = true, ShadowFilter = Light2D.ShadowFilterEnum.Pcf5, ShadowFilterSmooth = 2f, ShadowColor = new Color(0, 0, 0, 0.9f),
        };

        public static Texture2D Get(string path)
        {
            if (Cache.TryGetValue(path, out var t)) return t;
            var res = $"res://art/{path}.png";
            t = ResourceLoader.Exists(res) ? GD.Load<Texture2D>(res) : null;
            // объём: есть карта нормалей <name>_n.png (scripts/gen-normals.py) — отдаём CanvasTexture, шейдеры мира
            // берут NORMAL и сами светят солнцем (VisionFx: lit); фильтр наследуется от узла (nearest)
            var nres = $"res://art/{path}_n.png";
            if (t != null && ResourceLoader.Exists(nres))
                t = new CanvasTexture { DiffuseTexture = t, NormalTexture = GD.Load<Texture2D>(nres) };
            Cache[path] = t;
            return t;
        }

        /// <summary>Кадры ходьбы персонажа: 'f' — лицом (вниз-вправо), 'b' — спиной (вверх-вправо). Нет файлов — null.</summary>
        public static Texture2D[] CharFrames(string baseName, char row)
        {
            var frames = new Texture2D[4];
            for (int i = 0; i < 4; i++)
            {
                frames[i] = Get($"chars/{baseName}_{row}{i}");
                if (frames[i] == null) return null;
            }
            return frames;
        }

        /// <summary>Цикл ходьбы из видео (Wan 2.2): chars/<base>_walk_<f|b>0..N. Нет — null.</summary>
        public static Texture2D[] WalkFrames(string baseName, char row)
        {
            var list = new List<Texture2D>();
            for (int i = 0; i < 16; i++)
            {
                var t = Get($"chars/{baseName}_walk_{row}{i}");
                if (t == null) break;
                list.Add(t);
            }
            return list.Count >= 2 ? list.ToArray() : null;
        }

        /// <summary>Детерминированный выбор варианта по id объекта.</summary>
        public static Texture2D Pick(int seed, params string[] paths)
        {
            uint h = (uint)seed * 2654435761u;
            for (int i = 0; i < paths.Length; i++)
            {
                var t = Get(paths[(int)((h + (uint)i) % (uint)paths.Length)]);
                if (t != null) return t;
            }
            return null;
        }

        const string Pine1 = "trees/tree_pine_1", Pine2 = "trees/tree_pine_2", Oak = "trees/tree_oak_dead", Birch = "trees/tree_birch", Leafy = "trees/tree_leafy";

        const string SnowSpruce1 = "trees/tree_spruce_snow_1", SnowSpruce2 = "trees/tree_spruce_snow_2", FrozenBirch = "trees/tree_birch_frozen",
                     Dry1 = "trees/tree_dead_dry_1", Dry2 = "trees/tree_dead_dry_2", Willow = "trees/tree_swamp_willow", SwampDead = "trees/tree_swamp_dead",
                     Coastal = "trees/tree_pine_coastal";

        /// <summary>Вид дерева зависит от биома: в тайге ели под снегом, в пустоши сухие, на болоте ивы и гниющие стволы.</summary>
        public static Texture2D Tree(int seed, Biome biome = Biome.Grassland) => biome switch
        {
            Biome.Forest => PickOr(seed, new[] { Pine1, Pine2, Pine1, Leafy, Oak }, Pine1),
            Biome.Tundra => PickOr(seed, new[] { SnowSpruce1, SnowSpruce2, SnowSpruce1, FrozenBirch }, Pine2),
            Biome.Arid => PickOr(seed, new[] { Dry1, Dry2, Dry1, Oak }, Oak),
            Biome.Swamp => PickOr(seed, new[] { Willow, SwampDead, Willow, Birch }, Oak),
            Biome.Beach => PickOr(seed, new[] { Coastal, Coastal, Pine2 }, Pine2),
            _ => PickOr(seed, new[] { Leafy, Birch, Leafy, Birch, Pine1 }, Leafy),
        };

        /// <summary>Вариант по seed; если файла нет — запасной (чтобы в тайге не вылезло лиственное дерево).</summary>
        static Texture2D PickOr(int seed, string[] paths, string fallback)
        {
            uint h = (uint)seed * 2654435761u;
            return Get(paths[(int)(h % (uint)paths.Length)]) ?? Get(fallback);
        }

        /// <summary>Камни и руды под биом: в тайге со снегом, в пустоши выгоревшие, на болоте во мху; в поле/лесу — обычные.</summary>
        public static Texture2D Node(NodeKind kind, int seed, Biome biome)
        {
            if (kind == NodeKind.Barrel) return Get("props/barrel_oil");
            if (kind == NodeKind.Junk) return Styled("props/junk_pile", biome);
            if (kind == NodeKind.Tire) return Get("clutter/clutter_tire");
            if (kind == NodeKind.TireStack) return Get("props/gar_tire_stack");
            string k = kind switch { NodeKind.Metal => "metal", NodeKind.Sulfur => "sulfur", _ => "stone" };
            string suffix = biome switch { Biome.Tundra => "tundra", Biome.Arid => "arid", Biome.Beach => "arid", Biome.Swamp => "swamp", _ => null };
            return (suffix != null ? Get($"nodes/node_{k}_{suffix}") : null) ?? Node(kind, seed);
        }

        /// <summary>Вариант укрытия/предмета под биом, иначе базовый.</summary>
        public static Texture2D Styled(string basePath, Biome biome)
        {
            string suffix = biome switch { Biome.Tundra => "_snow", Biome.Arid or Biome.Beach => "_sand", _ => null };
            return (suffix != null ? Get(basePath + suffix) : null) ?? Get(basePath);
        }

        public static Texture2D Node(NodeKind kind, int seed) => kind switch
        {
            NodeKind.Metal => Pick(seed, "nodes/node_metal_1", "nodes/node_metal_2"),
            NodeKind.Sulfur => Pick(seed, "nodes/node_sulfur_1", "nodes/node_sulfur_2"),
            _ => Pick(seed, "nodes/node_stone_1", "nodes/node_stone_2"),
        };

        // id предмета в симуляции → картинка (если имена не совпадают с icon_<id> / weapons/<id>)
        static readonly Dictionary<string, string> IconMap = new()
        {
            ["tool_rock"] = "weapons/weapon_rock",
            ["tool_hatchet"] = "weapons/weapon_hatchet",
            ["tool_pickaxe"] = "weapons/weapon_pickaxe",
            ["tool_hammer"] = "weapons/weapon_hammer",
            ["build_plan"] = "weapons/weapon_build_plan",
            ["deploy_furnace"] = "icons/icon_furnace",
            ["deploy_workbench"] = "icons/icon_workbench",
            ["deploy_workbench2"] = "icons/icon_workbench2",
            ["deploy_workbench3"] = "icons/icon_workbench3",
            ["deploy_sleepbag"] = "icons/icon_sleeping_bag",
            ["comp_gears"] = "icons/icon_gears",
            ["comp_spring"] = "icons/icon_spring",
            ["comp_pipe"] = "icons/icon_metal_pipe",
            ["deploy_box"] = "icons/icon_wood_box",
            ["deploy_claim"] = "icons/icon_tool_cupboard",
            ["expl_satchel"] = "icons/icon_satchel",
            ["expl_c4"] = "icons/icon_c4",
            ["expl_fire"] = "icons/icon_molotov",
            ["lock_code"] = "icons/icon_code_lock",
            ["lock_key"] = "icons/icon_key_lock",
            // D54: станции, инструменты
            ["deploy_still"] = "icons/icon_still", ["deploy_sewing"] = "icons/icon_sewing", ["deploy_chem"] = "icons/icon_chem",
            ["deploy_generator"] = "icons/icon_generator", ["deploy_lamp"] = "icons/icon_lamp", ["deploy_floodlight"] = "icons/icon_lamp",
            ["deploy_efurnace"] = "icons/icon_efurnace", ["deploy_guntable"] = "icons/icon_guntable", ["deploy_medtable"] = "icons/icon_medtable",
            ["deploy_conveyor"] = "icons/icon_conveyor", ["deploy_rectifier"] = "icons/icon_rectifier", ["deploy_campfire"] = "icons/icon_campfire",
            ["deploy_barrel"] = "icons/icon_barrel_200", ["deploy_box_large"] = "icons/icon_box_large", ["deploy_drill"] = "icons/icon_drill_rig",
            ["deploy_lathe"] = "icons/icon_lathe",
            ["tool_metal_hatchet"] = "weapons/weapon_metal_hatchet", ["tool_metal_pickaxe"] = "weapons/weapon_metal_pickaxe",
            ["tool_chainsaw"] = "icons/icon_chainsaw", ["tool_jackhammer"] = "icons/icon_jackhammer",
        };

        // D46: пока нет арта нового ствола/детали/обвеса — похожая старая картинка (новый файл icons/icon_<id> или weapons/<id> перекроет)
        static readonly Dictionary<string, string> Fallback = new()
        {
            ["weapon_pipeshotgun"] = "weapons/weapon_waterpipe", ["weapon_sawnoff"] = "weapons/weapon_doublebarrel", ["weapon_mp133"] = "weapons/weapon_doublebarrel",
            ["weapon_saiga12"] = "weapons/weapon_ak", ["weapon_nagant"] = "weapons/weapon_revolver", ["weapon_grach"] = "weapons/weapon_revolver",
            ["weapon_aps"] = "weapons/weapon_revolver", ["weapon_homesmg"] = "weapons/weapon_smg", ["weapon_pps43"] = "weapons/weapon_smg",
            ["weapon_ppsh"] = "weapons/weapon_smg", ["weapon_svt40"] = "weapons/weapon_bolt", ["weapon_svd"] = "weapons/weapon_bolt",
            ["weapon_mosin"] = "weapons/weapon_bolt", ["weapon_akm"] = "weapons/weapon_ak", ["weapon_an94"] = "weapons/weapon_ak",
            ["ammo_9mm"] = "icons/icon_ammo_pistol", ["ammo_545"] = "icons/icon_ammo_rifle", ["ammo_762"] = "icons/icon_ammo_rifle",
            ["ammo_12g"] = "icons/icon_ammo_shotgun", ["ammo_bolt"] = "icons/icon_arrow", ["deploy_lathe"] = "icons/icon_workbench2",
            ["tool_chainsaw"] = "weapons/weapon_metal_hatchet", ["tool_jackhammer"] = "weapons/weapon_metal_pickaxe",   // в руках — пока похожий спрайт (D54)
            ["att_compensator_army"] = "icons/icon_att_compensator", ["att_mag_ext_army"] = "icons/icon_att_mag_ext", ["att_mag_twin_crude"] = "icons/icon_att_mag_twin",
        };

        /// <summary>Одежда (D47): пока художник не положил icons/icon_wear_*, — похожая старая картинка по слоту.</summary>
        static string WearFallback(string id)
        {
            if (id.StartsWith("plate_")) return "icons/icon_metal_chestplate";
            if (id == "wear_watch") return "icons/icon_gears";
            if (id is "rubber") return "icons/icon_tape";
            if (id is "leather") return "icons/icon_cloth";
            if (id is "antirad") return "icons/icon_syringe";
            if (Clothing.Def(id) is not WearDef d) return null;
            return d.Slot switch
            {
                WearSlot.Head => d.Metal ? "icons/icon_coffeecan_helmet" : "icons/icon_bandana",
                WearSlot.Face => id.Contains("gp5") || id.Contains("pmk") || id.Contains("r2") ? "icons/icon_metal_facemask" : "icons/icon_bandana",
                WearSlot.Kofta => id.StartsWith("wear_rags") ? "icons/icon_burlap_shirt" : "icons/icon_hoodie",
                WearSlot.Armor => id.Contains("signvest") ? "icons/icon_roadsign_vest" : id.Contains("chestplate") ? "icons/icon_wood_armor" : id.Contains("ozk") ? "icons/icon_hide_vest" : "icons/icon_metal_chestplate",
                WearSlot.Arms or WearSlot.Gloves => "icons/icon_gloves",
                WearSlot.Legs => id.StartsWith("wear_rags") ? "icons/icon_burlap_trousers" : "icons/icon_pants",
                WearSlot.Knees => "icons/icon_roadsign_kilt",
                WearSlot.Feet => id.StartsWith("wear_rags") ? "icons/icon_burlap_shoes" : "icons/icon_boots",
                _ => "icons/icon_gears",
            };
        }

        static string FallbackPath(string id) =>
            WearFallback(id) is string wf ? wf :
            Fallback.TryGetValue(id, out var f) ? f
            : id.StartsWith("comp_") ? "icons/icon_gears"
            : id == "val_copper_wire" ? "icons/icon_comp_tape"
            : id == "val_motor" || id == "val_battery" ? "icons/icon_gears"
            : id.StartsWith("bp_frag") ? "icons/icon_doc_blueprint"
            : id.StartsWith("val_") ? "icons/icon_comp_circuit"
            : id.StartsWith("att_") ? "icons/icon_gears"
            : null;

        /// <summary>Иконка предмета для интерфейса: icons/icon_&lt;id&gt;, weapons/&lt;id&gt;, старое имя, похожая картинка.</summary>
        public static Texture2D Icon(string itemId)
        {
            if (itemId == null) return null;
            var t = Get($"icons/icon_{itemId}") ?? Get($"weapons/{itemId}");
            if (t != null) return t;
            if (IconMap.TryGetValue(itemId, out var p) && Get(p) is Texture2D m) return m;
            return FallbackPath(itemId) is string fb ? Get(fb) : null;
        }

        /// <summary>Спрайт предмета в руках (вид сбоку, ствол вправо). Только для оружия и инструментов.</summary>
        public static Texture2D HeldSprite(string itemId)
        {
            if (itemId == null) return null;
            if (Get($"weapons/{itemId}") is Texture2D t) return t;
            if (IconMap.TryGetValue(itemId, out var p) && p.StartsWith("weapons/")) return Get(p);
            return Fallback.TryGetValue(itemId, out var f) && f.StartsWith("weapons/") ? Get(f) : null;
        }
    }
}
