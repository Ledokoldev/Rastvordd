using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Деревья: где растут, какой породы и возраста (вид — в ResourceNode.Look). Правила места — Ecology.TreeSuit.
    /// Лес — сплошной массив: в глуби старые деревья под сомкнутым пологом, к опушке молодняк и подрост, дальше кусты и луг
    /// (не стена деревьев). Поле открытое: редкие одинокие старые деревья, рощицы вокруг «материнского» дерева, живые изгороди.
    /// Пустошь почти без деревьев, на болоте ивы и сухостой, в тундре редкие низкие ели. Всего деревьев — как раньше (±20 %).
    /// </summary>
    public sealed partial class World
    {
        SiteMap _sites;
        /// <summary>Условия места (влажность, свет, рельеф, опушка) — по сетке рельефа и биомов.</summary>
        public SiteMap Sites => _sites ??= new SiteMap(Terrain.N, TerrainMap.Cell, HalfSize, Terrain.Biomes, Terrain.Height);

        /// <summary>Плотность деревьев (шт. на м²): по биому, глубине в нём и рельефу.</summary>
        static float TreeDensity(in Site s)
        {
            float steep = s.Steep;
            float d = s.Biome switch
            {
                // лес гуще к середине: опушка → чаща
                // плавно гуще от опушки к чаще (1/75 → 1/26)
                Biome.Forest => 1 / 75f + (1 / 26f - 1 / 75f) * Math.Clamp((s.Depth - 2f) / 14f, 0, 1),
                // поле: одинокие деревья редки; у самого леса — подрост и молодняк (опушка переходит в луг постепенно)
                Biome.Grassland => s.ForestNear ? (s.Depth < 4 ? 1 / 220f : 1 / 650f) : 1 / 3600f,
                Biome.Swamp => 1 / 100f * (0.5f + s.Wet * 0.6f),
                Biome.Tundra => 1 / 380f,
                Biome.Arid => 1 / 2600f,
                Biome.Beach => 1 / 1700f,
                _ => 0,
            };
            return d * (1 - steep * 0.5f);   // на круче деревьям хуже
        }

        /// <summary>Возраст: в глуби леса старые, на опушке молодые и подрост; одинокое в поле — старое, тундровые — карликовые.</summary>
        static TreeAge AgeFor(Random r, in Site s)
        {
            double x = r.NextDouble();
            switch (s.Biome)
            {
                case Biome.Forest:
                    if (s.Depth < 5) return x < 0.3 ? TreeAge.Sapling : x < 0.75 ? TreeAge.Young : TreeAge.Mature;
                    if (s.Depth < 12) return x < 0.08 ? TreeAge.Sapling : x < 0.3 ? TreeAge.Young : x < 0.85 ? TreeAge.Mature : TreeAge.Old;
                    return x < 0.04 ? TreeAge.Sapling : x < 0.12 ? TreeAge.Young : x < 0.55 ? TreeAge.Mature : TreeAge.Old;
                case Biome.Grassland:
                    if (s.ForestNear) return x < 0.45 ? TreeAge.Sapling : x < 0.9 ? TreeAge.Young : TreeAge.Mature;   // поросль у опушки
                    return x < 0.55 ? TreeAge.Old : x < 0.9 ? TreeAge.Mature : TreeAge.Young;                        // одинокий великан
                case Biome.Tundra: return x < 0.35 ? TreeAge.Sapling : x < 0.85 ? TreeAge.Young : TreeAge.Mature;
                case Biome.Swamp: return x < 0.15 ? TreeAge.Sapling : x < 0.45 ? TreeAge.Young : x < 0.9 ? TreeAge.Mature : TreeAge.Old;
                default: return x < 0.4 ? TreeAge.Young : TreeAge.Mature;
            }
        }

        /// <summary>Просвет до соседних стволов по возрасту: у старого крона шире.</summary>
        static float Spacing(TreeAge a) => a switch { TreeAge.Sapling => 1.1f, TreeAge.Young => 1.7f, TreeAge.Old => 2.9f, _ => 2.3f };

        bool TreeSpotOk(Vector2 p, float clear) => Terrain.IsLand(p) && !InMonument(p) && !OnRoad(p) && IsFree(p, clear);

        /// <summary>Посадить дерево: возраст задаёт ствол (хитбокс), запас дерева и спрайт.</summary>
        void AddTree(Random r, Vector2 p, TreeSpecies sp, TreeAge age)
        {
            // не в воде и не у самой кромки (28.10): ни один генератор (ивы у реки, подрост) не сажает дерево в реку/озеро
            if (!Terrain.IsLand(p)) return;
            for (int k2 = 0; k2 < 6; k2++) { float a2 = k2 * MathF.PI / 3; if (!Terrain.IsLand(p + new Vector2(MathF.Cos(a2), MathF.Sin(a2)) * 1.6f)) return; }
            float k = (float)r.NextDouble();
            var (radius, wood) = age switch
            {
                TreeAge.Sapling => (0.2f + k * 0.05f, 35f + k * 20f),
                TreeAge.Young => (0.28f + k * 0.06f, 85f + k * 40f),
                TreeAge.Old => (0.52f + k * 0.12f, 230f + k * 80f),
                _ => (0.38f + k * 0.1f, 135f + k * 70f),
            };
            if (sp == TreeSpecies.Dead) wood *= 0.7f;   // сухостой — трухлявый
            var node = new ResourceNode { Id = NextId(), Kind = NodeKind.Tree, Pos = p, Radius = radius, Amount = wood, MaxAmount = wood, Look = ResourceNode.TreeLook(sp, age) };
            Nodes.Add(node);
            _nodeGrid.Insert(node, p - new Vector2(radius), p + new Vector2(radius));
        }

        /// <summary>Ближайшее дерево с известной породой в радиусе r (для правила семян).</summary>
        ResourceNode MotherNear(Vector2 p, float r)
        {
            ResourceNode best = null; float bd = r;
            foreach (var n in NodesNear(p, r))
            {
                if (n.Kind != NodeKind.Tree || n.Look == 0) continue;
                float d = Vector2.Distance(n.Pos, p);
                if (d < bd) { bd = d; best = n; }
            }
            return best;
        }

        /// <summary>
        /// Одно дерево по правилам места; false — место не подошло.
        /// Распространение семян (правило 3): рядом с деревом (≤ 7 м) чаще растёт та же порода, если место ей подходит,
        /// а возле старого «материнского» дерева — его подрост и молодняк. Так лес складывается куртинами пород.
        /// </summary>
        bool TryTreeAt(Random r, Vector2 p, in Site s)
        {
            float conifer = Terrain.ConiferAt(p);
            var age = AgeFor(r, s);
            var sp = StandSpecies(r, p, s) is TreeSpecies d && d != TreeSpecies.Auto && Ecology.TreeSuit(d, s, conifer) > 0.08f
                ? d : Ecology.PickTree(r, s, conifer);
            var mother = MotherNear(p, 7f);
            if (mother != null && r.NextDouble() < 0.3)
            {
                var msp = ResourceNode.SpeciesOf(mother.Look);
                if (msp != TreeSpecies.Dead && Ecology.TreeSuit(msp, s, conifer) > 0.3f)
                {
                    sp = msp;
                    if (ResourceNode.AgeOf(mother.Look) == TreeAge.Old && age >= TreeAge.Mature && r.NextDouble() < 0.5)
                        age = r.NextDouble() < 0.5 ? TreeAge.Sapling : TreeAge.Young;   // подрост под старым деревом своей породы
                }
            }
            if (sp == TreeSpecies.Dead && age == TreeAge.Sapling) age = TreeAge.Young;
            if (!TreeSpotOk(p, Spacing(age))) return false;
            AddTree(r, p, sp, age);
            return true;
        }

        /// <summary>
        /// Главная порода древостоя (≈80 %): медленный шум «типа насаждения» + место. Ельник на севере и в сырости, сосняк на сухом и песке,
        /// березняки и осинники, дубравы на ровной плодородной почве; у реки ивы и тополя. Шум с дрожью — породы на стыке перемешаны.
        /// Auto — без доминанты (выбор по пригодности места).
        /// </summary>
        TreeSpecies StandSpecies(Random r, Vector2 p, in Site s)
        {
            float rv = Terrain.RiverNear(p);
            if (rv < 9 && s.Biome is Biome.Grassland or Biome.Forest or Biome.Beach or Biome.Swamp && r.NextDouble() < 0.85 - rv * 0.05)
                return r.NextDouble() < 0.72 ? TreeSpecies.Willow : TreeSpecies.Poplar;
            if (s.Biome is not (Biome.Forest or Biome.Grassland) || r.NextDouble() > 0.8) return TreeSpecies.Auto;
            float st = Terrain.StandAt(p) + (float)(r.NextDouble() - 0.5) * 0.16f;
            bool dry = s.Wet < 0.42f || s.SouthSun > 0.4f || s.Height > 0.8f;
            if (st < 0.3f) return dry && s.SouthSun > -0.2f ? TreeSpecies.Pine : TreeSpecies.Spruce;   // хвойный массив
            if (st < 0.45f) return TreeSpecies.Birch;                                                      // березняк
            if (st < 0.55f) return r.NextDouble() < 0.6 ? TreeSpecies.Aspen : TreeSpecies.Birch;          // осинник
            if (st < 0.72f) return s.Steep < 0.3f && s.Wet > 0.3f && s.Wet < 0.7f ? TreeSpecies.Oak : TreeSpecies.Birch;   // дубрава
            return dry ? TreeSpecies.Pine : TreeSpecies.Spruce;                                            // бор / ельник
        }

        Vector2 RandomIslandPoint(Random r) => new((float)(r.NextDouble() * 2 - 1) * HalfSize, (float)(r.NextDouble() * 2 - 1) * HalfSize);

        void PlaceTrees()
        {
            var r = new Random(Seed * 7919 + 17);   // свой генератор: остальная раскладка от деревьев не зависит
            const float probeArea = 6f, maxDens = 1 / 28f;
            int probes = (int)(HalfSize * HalfSize * 4 / probeArea);
            for (int i = 0; i < probes; i++)
            {
                var p = RandomIslandPoint(r);
                var b = Terrain.BiomeAt(p);
                if (b == Biome.Water) continue;
                if (r.NextDouble() > maxDens * probeArea) continue;
                var s = Sites.At(p);
                float dens = TreeDensity(s);
                if (s.Biome == Biome.Forest) dens *= Math.Clamp(Terrain.StandDensity(p), 0.35f, 1.4f);   // редины и куртины внутри массива
                float rv = Terrain.RiverNear(p);
                if (rv < 12 && s.Biome is Biome.Grassland or Biome.Beach or Biome.Arid) dens = MathF.Max(dens, (s.Biome == Biome.Arid ? 1 / 160f : 1 / 70f) * (1 - rv / 12f));   // рощи вдоль реки
                if (r.NextDouble() * maxDens > dens) continue;
                TryTreeAt(r, p, s);
            }

            // поле: рощицы (3–8 деревьев вокруг материнского) и живые изгороди (молодняк и подрост цепочкой)
            float grass = 0;
            foreach (var b in Terrain.Biomes) if (b == Biome.Grassland) grass += TerrainMap.Cell * TerrainMap.Cell;
            int copses = (int)(grass / 11000f), hedges = (int)(grass / 20000f);
            for (int c = 0, tries = 0; c < copses && tries < copses * 30; tries++)
            {
                var cc = RandomIslandPoint(r);
                if (Terrain.BiomeAt(cc) != Biome.Grassland) continue;
                var cs = Sites.At(cc);
                if (cs.Depth < 8 || cs.ForestNear) continue;
                var msp = Ecology.PickTree(r, cs, Terrain.ConiferAt(cc));
                if (msp is TreeSpecies.Dead or TreeSpecies.Coastal || !TreeSpotOk(cc, Spacing(TreeAge.Old))) continue;
                c++;
                AddTree(r, cc, msp, TreeAge.Old);   // материнское дерево в середине рощицы
                int n = r.Next(2, 8);
                float rad = 3.5f + n * 0.9f;
                for (int k = 0, t = 0; k < n && t < n * 6; t++)
                {
                    float a = (float)r.NextDouble() * MathF.Tau, d = 2.5f + (rad - 2.5f) * MathF.Sqrt((float)r.NextDouble());
                    var p = cc + new Vector2(MathF.Cos(a), MathF.Sin(a)) * d;
                    if (Terrain.BiomeAt(p) != Biome.Grassland) continue;
                    // к краю рощицы моложе: семена ложатся вокруг
                    var age = d > rad * 0.7f ? (r.NextDouble() < 0.5 ? TreeAge.Sapling : TreeAge.Young) : (r.NextDouble() < 0.7 ? TreeAge.Mature : TreeAge.Young);
                    var sp = r.NextDouble() < 0.7 ? msp : Ecology.PickTree(r, Sites.At(p), Terrain.ConiferAt(p));
                    if (sp is TreeSpecies.Dead or TreeSpecies.Coastal || !TreeSpotOk(p, Spacing(age))) continue;
                    AddTree(r, p, sp, age);
                    k++;
                }
            }
            for (int h = 0, tries = 0; h < hedges && tries < hedges * 30; tries++)
            {
                var a0 = RandomIslandPoint(r);
                if (Terrain.BiomeAt(a0) != Biome.Grassland || Sites.EdgeDepth(a0) < 5) continue;
                h++;
                float ang = (float)r.NextDouble() * MathF.PI, len = 22f + (float)r.NextDouble() * 30f;
                var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang)); var nrm = new Vector2(-dir.Y, dir.X);
                for (float d = 0; d < len; d += 2.4f + (float)r.NextDouble() * 2.2f)
                {
                    if (r.NextDouble() < 0.18) { d += 3; continue; }   // прореха в изгороди
                    var p = a0 + dir * d + nrm * (float)(r.NextDouble() - 0.5) * 1.2f;
                    if (Terrain.BiomeAt(p) != Biome.Grassland) break;
                    var age = r.NextDouble() < 0.45 ? TreeAge.Sapling : r.NextDouble() < 0.8 ? TreeAge.Young : TreeAge.Mature;
                    if (!TreeSpotOk(p, Spacing(age) * 0.8f)) continue;
                    var sp = r.Next(4) switch { 0 => TreeSpecies.Rowan, 1 => TreeSpecies.Birch, 2 => TreeSpecies.Aspen, _ => TreeSpecies.Oak };
                    AddTree(r, p, sp, age);
                }
            }
        }

        /// <summary>
        /// Человек (правило 5): заброшенные РТ зарастают — у оград молодые берёзы, осины и подрост (не на дорогах).
        /// После готовых РТ и дорог, своим генератором.
        /// </summary>
        void OvergrowMonuments()
        {
            var r = new Random(Seed * 104729 + 3);
            foreach (var m in Monuments)
            {
                var size = m.Max - m.Min;
                int n = (int)((size.X + size.Y) * 2 * 11 / 110f);   // полоса 3–14 м вокруг ограды, ~1 дерево на 110 м²
                for (int i = 0; i < n; i++)
                {
                    float t = (float)r.NextDouble(), off = 3f + (float)r.NextDouble() * 11f;
                    var p = r.Next(4) switch
                    {
                        0 => new Vector2(m.Min.X + size.X * t, m.Min.Y - off),
                        1 => new Vector2(m.Min.X + size.X * t, m.Max.Y + off),
                        2 => new Vector2(m.Min.X - off, m.Min.Y + size.Y * t),
                        _ => new Vector2(m.Max.X + off, m.Min.Y + size.Y * t),
                    };
                    var b = Terrain.BiomeAt(p);
                    if (b is Biome.Water or Biome.Beach or Biome.Arid || NearMonument(p, 2.5f)) continue;
                    var age = r.NextDouble() < 0.5 ? TreeAge.Sapling : TreeAge.Young;
                    if (!TreeSpotOk(p, Spacing(age) + 0.5f)) continue;
                    var sp = b == Biome.Tundra ? TreeSpecies.Spruce : b == Biome.Swamp ? TreeSpecies.Willow : r.NextDouble() < 0.7 ? TreeSpecies.Birch : TreeSpecies.Aspen;
                    AddTree(r, p, sp, age);
                }
            }
        }

        /// <summary>Отросшее дерево (респавн): место по тем же правилам — в лесу чаще, в поле редко; вырастает молодым.</summary>
        void RespawnTree()
        {
            for (int t = 0; t < 400; t++)
            {
                var p = new Vector2(Range(-HalfSize + 5, HalfSize - 5), Range(-HalfSize + 5, HalfSize - 5));
                if (!Terrain.IsLand(p) || OnRoad(p) || InMonument(p)) continue;
                var s = Sites.At(p);
                if (Rng.NextDouble() > TreeDensity(s) * 28f) continue;
                var sp = Ecology.PickTree(Rng, s, Terrain.ConiferAt(p));
                var age = sp == TreeSpecies.Dead || Rng.NextDouble() < 0.4 ? TreeAge.Mature : TreeAge.Young;
                if (!TreeSpotOk(p, Spacing(age))) continue;
                AddTree(Rng, p, sp, age);
                return;
            }
            AddNode(NodeKind.Tree, RandomFreePoint(1.5f, NodeKind.Tree));
        }

        /// <summary>Сводка: деревьев и дерева (ед.) по биомам, доля биомов на суше, возрасты.</summary>
        public string TreeStats()
        {
            var cnt = new Dictionary<Biome, int>(); var wood = new Dictionary<Biome, float>(); var area = new Dictionary<Biome, int>();
            var ages = new int[5];
            foreach (var b in Terrain.Biomes) area[b] = area.GetValueOrDefault(b) + 1;
            int all = 0; float allWood = 0;
            foreach (var n in Nodes)
            {
                if (n.Kind != NodeKind.Tree) continue;
                var b = Terrain.BiomeAt(n.Pos);
                cnt[b] = cnt.GetValueOrDefault(b) + 1; wood[b] = wood.GetValueOrDefault(b) + n.MaxAmount;
                ages[(int)ResourceNode.AgeOf(n.Look)]++;
                all++; allWood += n.MaxAmount;
            }
            int land = Terrain.Biomes.Length - area.GetValueOrDefault(Biome.Water);
            var parts = new List<string>();
            foreach (Biome b in Enum.GetValues<Biome>())
                if (b != Biome.Water) parts.Add($"{b} {100f * area.GetValueOrDefault(b) / land:0}% дер {cnt.GetValueOrDefault(b)} ({wood.GetValueOrDefault(b):0})");
            var slopes = new List<float>(); var rr = new Random(1);
            while (slopes.Count < 3000) { var q = RandomIslandPoint(rr); if (Terrain.IsLand(q)) slopes.Add(Sites.At(q).Slope); }
            slopes.Sort();
            return $"крутизна p50 {slopes[1500]:0.00} p90 {slopes[2700]:0.00} p99 {slopes[2970]:0.00} | деревья: {all}, дерева {allWood:0}, возраст ?/подрост/мол/взр/стар {string.Join("/", ages)} | " + string.Join(", ", parts);
        }
    }
}
