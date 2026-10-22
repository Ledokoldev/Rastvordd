using System.Numerics;

namespace Rastvordd.Sim
{
    public sealed partial class World
    {
        /// <summary>Отладка (скриншоты меню): поставить рядом нужный объект с содержимым и открыть его окно.</summary>
        public void DebugOpenUi(Actor a, string kind)
        {
            var at = a.Pos + new Vector2(0, -1.2f);
            if (kind == "bag")
            {
                var bag = new LootBag { Id = NextId(), Pos = at, IsCrate = true, Tier = 1 };
                FillPunchCrate(bag);
                Bags.Add(bag);
                OpenUi(a, UiKind.Bag, bag);
                return;
            }
            if (StationDefs.ByItem("deploy_" + kind) is StationDef sdef && !sdef.Registered) { DebugStation(a, sdef, at); return; }   // open:<станция> (D54)
            if (kind == "tank") { DebugStation(a, StationDefs.ByItem("deploy_barrel"), at); return; }
            if (kind is "all" or "alln")
            {
                // отладка D54: все станции в ряд перед игроком (скриншот мира), генератор — посередине
                float x = -9;
                foreach (var st in Prog.Stations)
                {
                    if (st.Registered || st.OnNode || (kind == "alln" && st.Light > 0)) continue;
                    var sd = new Deployable { Id = NextId(), Kind = st.Kind, OwnerId = a.Id, Hp = 1e9f, Half = Footprints.HalfOf(st.Kind, 0) };
                    sd.Pos = a.Pos + new Vector2(x + sd.Half.X, -2.5f - sd.Half.Y - (x > 4 ? 3.2f : 0));
                    if (x > 4) sd.Pos.X -= 13.5f;
                    x += sd.Half.X * 2 + 0.6f;
                    sd.Storage = new LootBag { Id = NextId(), Pos = sd.Pos };
                    if (st.Burn == "wood") sd.Storage.Items.Add(new ItemSlot("wood", 50));
                    if (st.Kind == DeployKind.Generator) sd.Storage.Items.Add(Liquids.Make("canister_20", "fuel", 20));
                    if (st.Kind == DeployKind.Still) sd.Storage.Items.Add(Liquids.Make("canister_20", "crude", 20));
                    if (st.Kind == DeployKind.Barrel) { sd.Storage = null; sd.Store["liq:diesel"] = 150; }
                    sd.Auth.Add(a.Id);
                    AddDeployable(sd);
                }
                _powerTimer = 0;
                return;
            }
            var dk = kind switch { "furnace" => DeployKind.Furnace, "claim" => DeployKind.Claim, "recycler" => DeployKind.Recycler, "lathe" => DeployKind.Lathe, _ => DeployKind.Box };
            var d = new Deployable { Id = NextId(), Kind = dk, Pos = at, OwnerId = a.Id, Radius = 0.5f, Hp = 1e9f };
            d.Storage = new LootBag { Id = NextId(), Pos = at };
            d.Storage.Items.Add(new ItemSlot("wood", 500));
            d.Storage.Items.Add(new ItemSlot("scrap", 120));
            if (dk == DeployKind.Furnace) { d.Ore = 300; d.SulfurOre = 100; }
            if (dk == DeployKind.Lathe) { d.Storage.Items.Clear(); d.Storage.Items.Add(new ItemSlot("metal_fragments", 300)); d.Storage.Items.Add(new ItemSlot("comp_spring", 3)); d.Storage.Items.Add(new ItemSlot("comp_gears", 2)); d.Storage.Items.Add(new ItemSlot("comp_pipe", 1)); d.On = true; }
            d.Auth.Add(a.Id);
            AddDeployable(d);
            OpenUi(a, dk switch { DeployKind.Furnace => UiKind.Furnace, DeployKind.Claim => UiKind.Claim, DeployKind.Recycler or DeployKind.Lathe => UiKind.Recycler, _ => UiKind.Box }, d);
        }

        /// <summary>Отладка D54: поставить станцию рядом (с сырьём и током) и открыть её окно.</summary>
        void DebugStation(Actor a, StationDef s, Vector2 at)
        {
            var d = new Deployable { Id = NextId(), Kind = s.Kind, Pos = at, OwnerId = a.Id, Hp = 1e9f, Half = Footprints.HalfOf(s.Kind, 0) };
            d.Pos = at + new Vector2(0, -d.Half.Y);
            if (s.OnNode)
            {
                // буровая: к ближайшему рудному узлу, игрок — рядом
                ResourceNode best = null;
                foreach (var n in Nodes) if (n.Kind == NodeKind.Metal && (best == null || Vector2.Distance(n.Pos, a.Pos) < Vector2.Distance(best.Pos, a.Pos))) best = n;
                if (best != null) { d.Pos = best.Pos + new Vector2(best.Radius + d.Half.X + 0.2f, 0); a.Pos = d.Pos + new Vector2(0, 2.2f); }
            }
            d.Storage = new LootBag { Id = NextId(), Pos = d.Pos };
            switch (s.Kind)
            {
                case DeployKind.Still: d.Storage.Items.Add(Liquids.Make("canister_20", "crude", 12)); d.Storage.Items.Add(Liquids.Make("canister_5", null, 0)); d.Storage.Items.Add(new ItemSlot("wood", 60)); d.Storage.Items.Add(new ItemSlot("berries", 30)); break;
                case DeployKind.Campfire: d.Storage.Items.Add(Liquids.Make("flask_1", "water_raw", 1)); d.Storage.Items.Add(new ItemSlot("wood", 20)); break;
                case DeployKind.Generator: d.Storage.Items.Add(Liquids.Make("canister_20", "fuel", 18)); break;
                case DeployKind.EFurnace: d.Storage.Items.Add(new ItemSlot("metal_ore", 200)); d.Storage.Items.Add(new ItemSlot("sulfur_ore", 80)); break;
                case DeployKind.Rectifier: d.Storage.Items.Add(Liquids.Make("canister_20", "fuel", 16)); d.Storage.Items.Add(Liquids.Make("canister_5", null, 0)); break;
                case DeployKind.Drill: d.Storage.Items.Add(Liquids.Make("canister_20", "diesel", 10)); break;
                case DeployKind.Barrel: d.Storage = null; d.Store["liq:fuel"] = 120; break;
            }
            d.Auth.Add(a.Id);
            AddDeployable(d);
            if (s.NeedsPower || s.Power > 0)
            {
                // генератор рядом, чтобы было видно работу под током
                var g = new Deployable { Id = NextId(), Kind = DeployKind.Generator, Pos = at + new Vector2(3.2f, 0), OwnerId = a.Id, Hp = 1e9f, Half = Footprints.HalfOf(DeployKind.Generator, 0) };
                g.Storage = new LootBag { Id = NextId(), Pos = g.Pos };
                g.Storage.Items.Add(Liquids.Make("canister_20", "fuel", 20));
                g.Auth.Add(a.Id);
                AddDeployable(g);
                _powerTimer = 0;
            }
            if (s.Kind is DeployKind.Sewing or DeployKind.Chem or DeployKind.GunTable or DeployKind.MedTable) { a.WantsCraftMenu = true; a.WantsCraftList = true; return; }
            if (s.Kind is DeployKind.Lamp or DeployKind.Floodlight) return;
            OpenUi(a, s.Kind == DeployKind.Barrel ? UiKind.Tank : s.Kind == DeployKind.BoxLarge ? UiKind.Box : UiKind.Recycler, d);
        }

        public void DebugKill(Actor a) => Kill(a, -1);
    }
}
