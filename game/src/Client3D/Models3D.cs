using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client3D
{
    /// <summary>
    /// Реестр 3D-моделей из game/art3d/ (их делает scripts/gen-3d-assets.py; свои/бесплатные кладутся поверх с тем же именем).
    /// Нет файла — рисуется заглушка из примитивов, ничего не ломается.
    ///
    /// Договорённость (docs/design/27-3d-client.md): 1 единица = 1 м, Y — вверх, опорная точка на земле в центре, «перед» — +X.
    ///   char_player / char_bot / char_guard / char_naked — части hips/torso/head/arm_l/arm_r/leg_l/leg_r двигает Actor3D
    ///   held_&lt;id предмета&gt; (held_weapon_akm, held_tool_hatchet…), запасные held_weapon / held_tool
    ///   node_tree_&lt;биом&gt;_N (forest, grassland, tundra, swamp, arid), node_stone_N, node_metal_N, node_sulfur_N, node_barrel…
    ///   deploy_&lt;вид&gt; (deploy_workbench, deploy_furnace…), loot_crate_*, loot_bag, pickup_hemp, pickup_berries
    ///   decor/&lt;имя спрайта&gt; — модель для декора РТ (decor/car_wreck.glb), подгоняется под его след
    ///
    /// Файл ещё не импортирован Godot (нет .import) — читается напрямую через GltfDocument, так что модели
    /// работают и без открытия редактора.
    /// </summary>
    public static class Models3D
    {
        const string Dir = "res://art3d/";
        static readonly Dictionary<string, Node3D> _templates = new();
        static readonly Dictionary<string, int> _variants = new();

        static Node3D Template(string key)
        {
            if (_templates.TryGetValue(key, out var have)) return have;
            Node3D found = null;
            // own/ — свои модели в стиле игры (Blender: scripts/blender/build.py, docs/design/28-3d-style.md),
            // hq/ — готовые (Poly Haven: scripts/polyhaven_convert.gd), потом сгенерированные заглушки
            foreach (var (sub, ext) in new[] { ("own/", ".gltf"), ("own/", ".glb"), ("hq/", ".glb"), ("", ".glb"), ("", ".gltf"), ("", ".tscn") })
            {
                var path = Dir + sub + key + ext;
                if (ResourceLoader.Exists(path))
                {
                    found = GD.Load<PackedScene>(path)?.Instantiate() as Node3D;
                    if (found != null) break;
                }
                if (ext != ".tscn" && FileAccess.FileExists(path))
                {
                    // не импортирован — собираем сцену из файла во время игры
                    var doc = new GltfDocument();
                    var state = new GltfState();
                    if (doc.AppendFromFile(path, state) == Error.Ok) found = doc.GenerateScene(state) as Node3D;
                    if (found != null) break;
                }
            }
            _templates[key] = found;
            return found;
        }

        static Node3D Make(string key) => Template(key)?.Duplicate() as Node3D;
        static bool Exists(string key) => Template(key) != null;

        /// <summary>Модель с вариантами: key_0, key_1… по номеру; нет вариантов — просто key.</summary>
        static Node3D Variant(string key, int look)
        {
            if (!_variants.TryGetValue(key, out int n))
            {
                n = 0;
                while (n < 16 && Exists($"{key}_{n}")) n++;
                _variants[key] = n;
            }
            return n > 0 ? Make($"{key}_{((look % n) + n) % n}") : Make(key);
        }

        public static Node3D Naked() => Make("char_naked");

        public static Node3D Character(bool me, bool npc) => Make(me ? "char_player" : npc ? "char_guard" : "char_bot") ?? Make("char_bot");

        public static Node3D Held(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            var m = Make("held_" + itemId);
            if (m != null) return m;
            var def = Items.Get(itemId);
            if (def?.Weapon != null) return Make("held_weapon");
            return Make("held_tool");
        }

        static string BiomeKey(Biome b) => b switch
        {
            Biome.Forest => "forest",
            Biome.Grassland => "grassland",
            Biome.Tundra => "tundra",
            Biome.Swamp => "swamp",
            Biome.Arid => "arid",
            Biome.Beach => "grassland",
            _ => "forest",
        };

        /// <summary>
        /// Модель ресурса. Деревья: сначала по породе (node_tree_&lt;порода&gt;[_snow|_arid|_swamp]_N, как в 2D Art.Tree),
        /// вариант — по seed (номер узла); нет породы (старые сейвы) — по биому.
        /// </summary>
        public static Node3D ForNode(NodeKind kind, int look, Biome biome, int seed = 0)
        {
            if (kind == NodeKind.Tree)
            {
                var sp = ResourceNode.SpeciesOf((byte)look);
                if (sp != TreeSpecies.Auto)
                {
                    string key = "node_tree_" + sp.ToString().ToLowerInvariant();
                    string sub = biome == Biome.Tundra ? "_snow" : biome == Biome.Arid ? "_arid" : biome == Biome.Swamp ? "_swamp" : "";
                    int v = (int)(((uint)seed * 2654435761u) >> 8);
                    var m = (sub != "" ? Variant(key + sub, v) : null) ?? Variant(key, v);
                    if (m != null) return m;
                }
                return Variant("node_tree_" + BiomeKey(biome), look) ?? Variant("node_tree", look);
            }
            return Variant("node_" + kind.ToString().ToLowerInvariant(), look);
        }

        public static Node3D ForDeploy(DeployKind kind) => Make("deploy_" + kind.ToString().ToLowerInvariant());

        /// <summary>Ящик лута: по виду из 2D (Look/Box: …medical, …elite, …army, …ammo) или по уровню.</summary>
        public static Node3D Crate(int tier, bool safe, string look = null)
        {
            string l = (look ?? "").ToLowerInvariant();
            string key = safe || l.Contains("safe") ? "loot_crate_safe"
                : l.Contains("medic") || l.Contains("food") ? "loot_crate_medical"
                : l.Contains("elite") || l.Contains("electric") ? "loot_crate_elite"
                : l.Contains("army") || l.Contains("ammo") || l.Contains("weapon") || l.Contains("military") ? "loot_crate_army"
                : tier >= 3 ? "loot_crate_elite" : tier == 2 ? "loot_crate_medical" : tier == 1 ? "loot_crate_army" : "loot_crate_wood";
            return Make(key);
        }
        public static Node3D Bag() => Make("loot_bag");
        public static Node3D Pickup(PickupKind kind) => kind switch
        {
            PickupKind.Hemp => Make("pickup_hemp"),
            PickupKind.Berries => Make("pickup_berries"),
            _ => null,
        };

        public static Node3D Decor(string spriteId) => string.IsNullOrEmpty(spriteId) ? null : Make("decor/" + spriteId);

        /// <summary>
        /// Техника РТ: id спрайта (vehicles/…) → своя модель типа decor/veh_* (scripts/blender/models/_veh.py).
        /// Одна модель на тип машины, цвет кузова — из спрайта (<see cref="Paint"/>).
        /// </summary>
        public static string VehicleKey(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            bool H(params string[] p) { foreach (var s in p) if (id.Contains(s)) return true; return false; }
            if (H("heli_")) return "veh_heli_wreck";
            if (H("plane_an2", "plane_")) return "veh_plane";
            if (H("radar")) return "veh_radar";
            if (H("radio_relay", "kung", "zil131")) return "veh_truck_kung";
            if (H("tanker", "fuel_truck")) return "veh_truck_tanker";
            if (H("kamaz", "dump")) return "veh_dump";
            if (H("tank_t34")) return "veh_tank_old";
            if (H("turretless")) return "veh_tank_turretless";
            if (H("tank_t")) return "veh_tank";
            if (H("btr")) return "veh_btr";
            if (H("brdm")) return "veh_brdm";
            if (H("bmp")) return "veh_bmp";
            if (H("mtlb")) return "veh_mtlb";
            if (H("sau_")) return "veh_sau";
            if (H("gazelle")) return "veh_van_truck";
            if (H("uaz452", "bukhanka", "rafik")) return "veh_van";
            if (H("liaz", "ikarus", "bus_", "paz")) return "veh_bus";
            if (H("truck_", "gaz53", "gaz66", "zil130", "ural4320", "maz_")) return "veh_truck";
            if (H("uaz469", "jeep", "niva", "uaz_")) return "veh_jeep";
            if (H("moto_", "sidecar")) return "veh_moto";
            if (H("tractor")) return "veh_tractor";
            if (H("trailer_2pts")) return "veh_trailer";
            if (H("wagon_gondola")) return "veh_wagon_gondola";
            if (H("wagon_flatcar")) return "veh_wagon_flat";
            if (H("wagon_boxcar", "wagon_freight")) return "veh_wagon_box";
            if (H("vaz2109")) return "veh_sedan_hatch";
            if (H("zaz", "zaporozhets")) return "veh_sedan_small";
            if (H("volga")) return "veh_sedan_long";
            if (H("car_", "sedan", "moskvich", "vaz")) return "veh_sedan";
            return null;
        }

        public static Node3D Vehicle(string id) => VehicleKey(id) is string k ? Make("decor/" + k) : null;

        /// <summary>
        /// Вписать машину в след без искажения пропорций: равный масштаб по меньшей стороне, поворот на 90°,
        /// если длинная сторона модели не совпадает с длинной стороной следа. Высота — пропорционально.
        /// </summary>
        public static void FitKeep(Node3D model, Vector3 size)
        {
            var b = Bounds(model).Size;
            bool turn = (b.X >= b.Z) != (size.X >= size.Z);
            float bx = turn ? b.Z : b.X, bz = turn ? b.X : b.Z;
            float s = Mathf.Min(size.X / Mathf.Max(0.01f, bx), size.Z / Mathf.Max(0.01f, bz));
            var pivot = new Node3D { Rotation = new Vector3(0, turn ? Mathf.Pi / 2 : 0, 0), Scale = Vector3.One * s };
            var parent = model.GetParent();
            parent?.RemoveChild(model);
            pivot.AddChild(model);
            parent?.AddChild(pivot);
            var bb = Bounds(model);
            var c = bb.GetCenter();
            model.Position = new Vector3(-c.X, -bb.Position.Y, -c.Z);
        }

        static readonly Dictionary<(Color, bool), (Material paint, Material glass)> _paints = new();

        /// <summary>Перекрасить кузов (материал car_paint) в цвет спрайта; burnt — обгоревший: темнее, стёкла чёрные.</summary>
        public static void Paint(Node3D model, Color col, bool burnt)
        {
            if (!_paints.TryGetValue((col, burnt), out var m))
            {
                var tint = burnt ? col.Darkened(0.55f).Lerp(new Color(0.22f, 0.11f, 0.05f), 0.5f) : col;
                m = (null, null);
                _paints[(col, burnt)] = m = (Recolor(tint), burnt ? new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.02f), Roughness = 1f } : null);
            }
            void Walk(Node n)
            {
                if (n is MeshInstance3D mi && mi.Mesh != null)
                    for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                    {
                        var name = mi.Mesh.SurfaceGetMaterial(i)?.ResourceName ?? "";
                        if (name == "car_paint") mi.SetSurfaceOverrideMaterial(i, Bind(m.paint, mi.Mesh.SurfaceGetMaterial(i)));
                        else if (burnt && name == "glass_dark") mi.SetSurfaceOverrideMaterial(i, m.glass);
                    }
                foreach (var c in n.GetChildren()) Walk(c);
            }
            Walk(model);
        }

        static Material Recolor(Color tint) => new StandardMaterial3D { AlbedoColor = tint, ResourceName = "car_paint_tinted" };

        // кузову нужна своя текстура износа (сколы, грязь) — берём её из материала модели, цвет — от спрайта
        static readonly Dictionary<(Material, Material), Material> _bound = new();
        static Material Bind(Material tinted, Material src)
        {
            if (_bound.TryGetValue((tinted, src), out var r)) return r;
            if (src is BaseMaterial3D b && tinted is BaseMaterial3D t)
            {
                var d = (BaseMaterial3D)b.Duplicate();
                var c = t.AlbedoColor;
                d.AlbedoColor = new Color(c.R * 1.6f, c.G * 1.6f, c.B * 1.6f);   // текстура светлая (≈0,55): ×1,6 — цвет спрайта как есть
                r = d;
            }
            else r = tinted;
            _bound[(tinted, src)] = r;
            return r;
        }

        /// <summary>Границы всех мешей модели в координатах узла root.</summary>
        public static Aabb Bounds(Node3D root)
        {
            bool any = false;
            var box = new Aabb();
            void Walk(Node n, Transform3D t)
            {
                foreach (var c in n.GetChildren())
                {
                    if (c is not Node3D c3) continue;
                    var ct = t * c3.Transform;
                    if (c3 is MeshInstance3D mi && mi.Mesh != null)
                    {
                        var b = ct * mi.Mesh.GetAabb();
                        box = any ? box.Merge(b) : b;
                        any = true;
                    }
                    Walk(c3, ct);
                }
            }
            if (root is MeshInstance3D rm && rm.Mesh != null) { box = rm.Mesh.GetAabb(); any = true; }
            Walk(root, Transform3D.Identity);
            return any ? box : new Aabb(Vector3.Zero, Vector3.One);
        }

        /// <summary>
        /// Подогнать модель под размер (м): по X/Z — точно, по Y — точно (uniformY=false) или средним масштабом X/Z.
        /// Основание — на земле в центре. Модель должна быть уже без своего масштаба.
        /// </summary>
        public static void Fit(Node3D model, Vector3 size, bool uniformY)
        {
            var b = Bounds(model);
            var s = b.Size;
            float sx = size.X / Mathf.Max(0.01f, s.X), sz = size.Z / Mathf.Max(0.01f, s.Z);
            float sy = uniformY ? (sx + sz) * 0.5f : size.Y / Mathf.Max(0.01f, s.Y);
            var k = new Vector3(sx, sy, sz);
            model.Scale = k;
            var c = b.GetCenter();
            model.Position = new Vector3(-c.X * k.X, -b.Position.Y * k.Y, -c.Z * k.Z);
        }
    }
}
