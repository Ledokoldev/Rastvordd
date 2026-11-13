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
            for (int i = 0; i < Actor.BeltSize; i++) a.Belt[i] = null;
            if (Items.Get(held) != null) a.Add(held, 1);
            a.Current = 0;
            Console.WriteLine($"витрина: станций {Prog.Stations.Length}, пород {species.Length}, в руках {held}");
        }
    }
}
