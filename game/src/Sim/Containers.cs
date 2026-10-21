using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>Какое окно открыто у игрока (по E): ящик, сумка/ящик РТ, печь, межевой столб.</summary>
    public enum UiKind { None, Box, Bag, Furnace, Claim }

    /// <summary>Действие в окне. Предмет — UiItem: «inv:id» (инвентарь), «belt:N» (пояс) или просто id ресурса.</summary>
    public enum UiOp { None, Close, Take, Put, TakeAll, PutAll, Toggle, TakeOut }

    /// <summary>Что показать в окне: две колонки (ты и контейнер) и особые поля печи и столба.</summary>
    public sealed class UiView
    {
        public UiKind Kind;
        public string Title;
        public List<(string id, string name, int count)> Items = new();      // содержимое контейнера
        public List<(string key, string id, string name, int count)> Mine = new();   // что можно положить
        // печь
        public int Ore, SulfurOre, Fuel, OutMetal, OutSulfur, OutCharcoal;
        public bool On, Burning;
        // столб
        public string ClaimText;
    }

    public sealed partial class World
    {
        public const float UiRange = 3f;

        /// <summary>Открыть окно (живым игрокам; боты пользуются быстрыми действиями).</summary>
        void OpenUi(Actor a, UiKind kind, object target)
        {
            a.OpenUi = kind;
            a.OpenTarget = target;
            if (kind == UiKind.Claim && target is Deployable c && !c.Auth.Contains(a.Id))
            {
                c.Auth.Add(a.Id);
                Info(a, "Ты вписан в столб: можно строить в его зоне");
            }
        }

        Vector2? UiTargetPos(Actor a) => a.OpenTarget switch
        {
            Deployable d when Deployables.Contains(d) => d.Pos,
            LootBag b when Bags.Contains(b) || (b.IsCrate && Bags.Contains(b)) => b.Pos,
            _ => null,
        };

        List<ItemSlot> UiItems(Actor a) => a.OpenTarget switch
        {
            Deployable d when d.Storage != null => d.Storage.Items,
            LootBag b => b.Items,
            _ => null,
        };

        /// <summary>Каждый тик: окно закрывается, если отошёл или контейнер исчез; выполняем действие из ввода.</summary>
        void StepUi(Actor a, InputCmd cmd)
        {
            if (a.OpenUi == UiKind.None) return;
            var pos = UiTargetPos(a);
            if (a.Dead || pos == null || Vector2.Distance(pos.Value, a.Pos) > UiRange || (UiOp)cmd.UiOp == UiOp.Close)
            {
                a.OpenUi = UiKind.None; a.OpenTarget = null;
                return;
            }
            var op = (UiOp)cmd.UiOp;
            if (op == UiOp.None) return;
            switch (a.OpenUi)
            {
                case UiKind.Box:
                case UiKind.Bag:
                    ContainerOp(a, UiItems(a), op, cmd.UiItem, cmd.UiIndex);
                    if (a.OpenTarget is LootBag lb && lb.IsCrate && lb.Empty && lb.RespawnLeft <= 0) lb.RespawnLeft = 90f;
                    break;
                case UiKind.Furnace: FurnaceOp(a, (Deployable)a.OpenTarget, op, cmd.UiItem); break;
                case UiKind.Claim: ClaimOp(a, (Deployable)a.OpenTarget, op, cmd.UiItem); break;
            }
        }

        /// <summary>Снять с игрока предмет по ключу «inv:id» / «belt:N» — целиком.</summary>
        ItemSlot TakeFromActor(Actor a, string key)
        {
            if (key == null) return null;
            if (key.StartsWith("belt:") && int.TryParse(key.Substring(5), out int bi) && bi >= 0 && bi < Actor.BeltSize && a.Belt[bi] != null)
            {
                var s = a.Belt[bi];
                a.Belt[bi] = null;
                return s;
            }
            string id = key.StartsWith("inv:") ? key.Substring(4) : key;
            int n = a.Take(id, int.MaxValue);
            return n > 0 ? new ItemSlot(id, n) : null;
        }

        void ContainerOp(Actor a, List<ItemSlot> items, UiOp op, string key, int index)
        {
            if (items == null) return;
            switch (op)
            {
                case UiOp.Take when index >= 0 && index < items.Count:
                    var s = items[index];
                    if (s.Def != null && s.Def.Holdable && s.Def.Stack <= 1) { if (!a.AddSlot(s)) { Info(a, "Пояс полон"); return; } }
                    else a.Add(s.Id, s.Count);
                    items.RemoveAt(index);
                    break;
                case UiOp.TakeAll:
                    for (int i = items.Count - 1; i >= 0; i--)
                    {
                        var it = items[i];
                        if (it.Def != null && it.Def.Holdable && it.Def.Stack <= 1) { if (!a.AddSlot(it)) continue; }
                        else a.Add(it.Id, it.Count);
                        items.RemoveAt(i);
                    }
                    break;
                case UiOp.Put:
                    var put = TakeFromActor(a, key);
                    if (put == null) return;
                    var same = put.State == null ? items.Find(x => x.Id == put.Id && x.State == null) : null;
                    if (same != null) same.Count += put.Count; else items.Add(put);
                    break;
                case UiOp.PutAll:
                    foreach (var st in new List<ItemStack>(a.Inventory))
                    {
                        if (st.Id.StartsWith("key_")) continue;   // ключи остаются при себе
                        var p = TakeFromActor(a, "inv:" + st.Id);
                        if (p == null) continue;
                        var m = items.Find(x => x.Id == p.Id && x.State == null);
                        if (m != null) m.Count += p.Count; else items.Add(p);
                    }
                    break;
            }
        }

        void FurnaceOp(Actor a, Deployable f, UiOp op, string key)
        {
            switch (op)
            {
                case UiOp.Put:
                    string id = key?.StartsWith("inv:") == true ? key.Substring(4) : key;
                    if (id == "metal_ore") f.Ore += a.Take(id, int.MaxValue);
                    else if (id == "sulfur_ore") f.SulfurOre += a.Take(id, int.MaxValue);
                    else if (id == "wood") f.Fuel += a.Take(id, int.MaxValue);
                    else Info(a, "В печь — только руда и дерево");
                    break;
                case UiOp.Take:   // вынуть обратно загруженное
                    if (key == "metal_ore") { a.Add(key, f.Ore); f.Ore = 0; }
                    else if (key == "sulfur_ore") { a.Add(key, f.SulfurOre); f.SulfurOre = 0; }
                    else if (key == "wood") { a.Add(key, (int)f.Fuel); f.Fuel -= (int)f.Fuel; }
                    break;
                case UiOp.TakeOut:
                    if (f.OutMetal > 0) { a.Add("metal_fragments", f.OutMetal); f.OutMetal = 0; }
                    if (f.OutSulfur > 0) { a.Add("sulfur", f.OutSulfur); f.OutSulfur = 0; }
                    if (f.OutCharcoal > 0) { a.Add("charcoal", f.OutCharcoal); f.OutCharcoal = 0; }
                    break;
                case UiOp.Toggle:
                    f.On = !f.On;
                    if (f.On && !f.Burning) Info(a, f.Fuel <= 0 ? "Нужно дерево" : "Нужна руда");
                    break;
            }
        }

        void ClaimOp(Actor a, Deployable c, UiOp op, string key)
        {
            string id = key?.StartsWith("inv:") == true ? key.Substring(4) : key;
            if (id is not ("wood" or "stone" or "metal_fragments" or "scrap")) return;
            if (op == UiOp.Put)
            {
                int n = a.Take(id, int.MaxValue);
                if (n > 0) c.Store[id] = c.Store.GetValueOrDefault(id) + n;
            }
            else if (op == UiOp.Take)
            {
                int n = (int)c.Store.GetValueOrDefault(id);
                if (n > 0) { a.Add(id, n); c.Store[id] -= n; }
            }
        }

        /// <summary>Содержимое окна для снапшота.</summary>
        public UiView BuildUi(Actor a)
        {
            if (a.OpenUi == UiKind.None) return null;
            var v = new UiView { Kind = a.OpenUi };
            switch (a.OpenTarget)
            {
                case Deployable d when a.OpenUi == UiKind.Box:
                    v.Title = "Ящик";
                    foreach (var s in d.Storage.Items) v.Items.Add((s.Id, Items.Name(s.Id), s.Count));
                    break;
                case LootBag b:
                    v.Title = b.IsCrate ? "Ящик" : "Рюкзак";
                    foreach (var s in b.Items) v.Items.Add((s.Id, Items.Name(s.Id), s.Count));
                    break;
                case Deployable f when a.OpenUi == UiKind.Furnace:
                    v.Title = "Печь";
                    v.Ore = f.Ore; v.SulfurOre = f.SulfurOre; v.Fuel = (int)f.Fuel;
                    v.OutMetal = f.OutMetal; v.OutSulfur = f.OutSulfur; v.OutCharcoal = f.OutCharcoal;
                    v.On = f.On; v.Burning = f.Burning;
                    break;
                case Deployable c when a.OpenUi == UiKind.Claim:
                    v.Title = "Межевой столб";
                    var (load, found, doors) = ClaimLoad(c);
                    float h = ClaimHoursLeft(c);
                    var need = UpkeepPerHourOf(c);
                    var parts = new List<string>();
                    foreach (var (id, per) in need) parts.Add($"{Items.Name(id)} {per:0.#}/ч");
                    v.ClaimText = $"Нагрузка {load:0}/{BudgetMax} · фундаменты {found}/{FoundMax} · двери {doors}/{DoorMax}\n" +
                                  (load <= 0 ? "Базы ещё нет — содержать нечего" :
                                   $"Содержание: {string.Join(", ", parts)}\nЗапаса хватит на {h:0.0} ч" + (h <= 0 ? (c.Grace > 0 ? $" — без запаса гниение через {c.Grace / 60:0} мин" : " — ГНИЁТ") : ""));
                    foreach (var (id, n) in c.Store) if (n >= 1) v.Items.Add((id, Items.Name(id), (int)n));
                    break;
            }
            // твои вещи: инвентарь, а для ящиков и сумок — ещё и пояс
            foreach (var st in a.Inventory)
                if (a.OpenUi is not (UiKind.Furnace or UiKind.Claim) || st.Id is "metal_ore" or "sulfur_ore" or "wood" or "stone" or "metal_fragments" or "scrap")
                    v.Mine.Add(("inv:" + st.Id, st.Id, Items.Name(st.Id), st.Count));
            if (a.OpenUi is UiKind.Box or UiKind.Bag)
                for (int i = 0; i < Actor.BeltSize; i++)
                    if (a.Belt[i] is ItemSlot s && i != a.Current) v.Mine.Add(($"belt:{i}", s.Id, $"{Items.Name(s.Id)} (пояс {i + 1})", s.Count));
            return v;
        }
    }
}
