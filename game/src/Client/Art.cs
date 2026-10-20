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

        public static Texture2D Get(string path)
        {
            if (Cache.TryGetValue(path, out var t)) return t;
            var res = $"res://art/{path}.png";
            t = ResourceLoader.Exists(res) ? GD.Load<Texture2D>(res) : null;
            Cache[path] = t;
            return t;
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

        static readonly string[] Trees = { "trees/tree_pine_1", "trees/tree_pine_2", "trees/tree_oak_dead", "trees/tree_birch", "trees/tree_leafy", "trees/tree_pine_1" };

        public static Texture2D Tree(int seed) => Pick(seed, Trees);

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
