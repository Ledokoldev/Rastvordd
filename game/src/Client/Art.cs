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
        };

        public static Texture2D Get(string path)
        {
            if (Cache.TryGetValue(path, out var t)) return t;
            var res = $"res://art/{path}.png";
            t = ResourceLoader.Exists(res) ? GD.Load<Texture2D>(res) : null;
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
            ["deploy_furnace"] = "icons/icon_furnace",
            ["deploy_workbench"] = "icons/icon_workbench",
        };

        /// <summary>Иконка предмета для интерфейса.</summary>
        public static Texture2D Icon(string itemId)
        {
            if (itemId == null) return null;
            if (IconMap.TryGetValue(itemId, out var p)) return Get(p);
            return Get($"icons/icon_{itemId}") ?? Get($"weapons/{itemId}");
        }

        /// <summary>Спрайт предмета в руках (вид сбоку, ствол вправо). Только для оружия и инструментов.</summary>
        public static Texture2D HeldSprite(string itemId)
        {
            if (itemId == null) return null;
            if (IconMap.TryGetValue(itemId, out var p) && p.StartsWith("weapons/")) return Get(p);
            return Get($"weapons/{itemId}");
        }
    }
}
