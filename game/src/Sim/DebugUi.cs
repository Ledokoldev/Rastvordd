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

        public void DebugKill(Actor a) => Kill(a, -1);
    }
}
