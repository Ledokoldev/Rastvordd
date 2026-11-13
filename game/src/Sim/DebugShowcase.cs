using System;
using System.Numerics;

namespace Rastvordd.Sim
{
    public sealed partial class World
    {
        /// <summary>
        /// Отладка 3D-моделей («витрина», запуск 3D с --showcase): перед игроком ряд всех станций, за ним деревья
        /// каждой породы, сбоку ящики лута всех видов, ресурсы; в руки — выбранный ствол. Только для отладочного запуска.
        /// </summary>
        public void DebugShowcase(Actor a, string held = "weapon_akm")
        {
            DebugOpenUi(a, "all");
            var species = new[] { TreeSpecies.Pine, TreeSpecies.Spruce, TreeSpecies.Birch, TreeSpecies.Oak, TreeSpecies.Aspen,
                                  TreeSpecies.Poplar, TreeSpecies.Rowan, TreeSpecies.Willow, TreeSpecies.Apple, TreeSpecies.Dead, TreeSpecies.Coastal };
            for (int i = 0; i < species.Length; i++)
            {
                var p = a.Pos + new Vector2(-13 + i * 2.6f, 7f + (i % 2) * 2.5f);
                var node = new ResourceNode { Id = NextId(), Kind = NodeKind.Tree, Pos = p, Radius = 0.4f, Amount = 150, MaxAmount = 150,
                                              Look = ResourceNode.TreeLook(species[i], TreeAge.Mature) };
                Nodes.Add(node);
                _nodeGrid.Insert(node, p - new Vector2(node.Radius), p + new Vector2(node.Radius));
            }
            var kinds = new[] { NodeKind.Stone, NodeKind.Metal, NodeKind.Sulfur, NodeKind.Barrel, NodeKind.Junk, NodeKind.Tire, NodeKind.TireStack };
            for (int i = 0; i < kinds.Length; i++) AddNode(kinds[i], a.Pos + new Vector2(6.5f, -1f + i * 2.3f));
            string[] boxes = { "wood", "army", "medical", "elite" };
            for (int i = 0; i < boxes.Length; i++)
                Bags.Add(new LootBag { Id = NextId(), Pos = a.Pos + new Vector2(-6.5f, -1f + i * 1.4f), IsCrate = true, Tier = i, Box = boxes[i] });
            Bags.Add(new LootBag { Id = NextId(), Pos = a.Pos + new Vector2(-6.5f, 4.6f), IsCrate = true, Safe = true, Tier = 2 });
            // стоянка техники РТ (все типы моделей) — ряды к югу от деревьев
            string[] veh = { "car_vaz2106_red_side", "car_vaz2109_grey_side", "car_zaz968_beige_side", "car_volga24_black_side", "wreck_burnt_sedan_side",
                             "jeep_uaz469_side", "van_uaz452_side", "van_gazelle_side", "bus_paz672_side", "truck_zil130_side", "truck_kamaz_dump_side",
                             "radio_relay_truck", "tractor_belarus_side", "trailer_2pts_side", "moto_ural_sidecar_side", "tank_t55_wreck_side",
                             "tank_t34_wreck", "tank_t72_wreck_turretless", "btr70_wreck", "brdm2_wreck", "bmp1_wreck", "mtlb_wreck", "sau_2s1_wreck",
                             "wagon_boxcar_green_side", "wagon_gondola_coal_side" };
            float vx = -24, vy = 16;
            foreach (var id in veh)
            {
                float w = id.StartsWith("wagon") ? 14 : id.Contains("tank") || id.Contains("bus") || id.Contains("truck") || id.Contains("kamaz") || id.Contains("relay") ? 7 : id.Contains("btr") || id.Contains("bmp") || id.Contains("sau") || id.Contains("mtlb") ? 6.5f : 4.4f;
                if (vx + w > 26) { vx = -24; vy += 5f; }
                Solid("vehicles/" + id, a.Pos + new Vector2(vx + w / 2, vy), w, w * 0.45f, 2.4f);
                vx += w + 1.2f;
            }
            for (int i = 0; i < Actor.BeltSize; i++) a.Belt[i] = null;
            if (Items.Get(held) != null) a.Add(held, 1);
            a.Current = 0;
            Console.WriteLine($"витрина: станций {Prog.Stations.Length}, пород {species.Length}, в руках {held}");
        }
    }
}
