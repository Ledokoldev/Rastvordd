using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>Какое окно открыто у игрока (по E): ящик, рюкзак/ящик РТ, печь, шкаф.</summary>
    public enum UiKind { None, Box, Bag, Furnace, Claim, Recycler, Tank }   // Tank — бочка 200 л (D54)

    /// <summary>
    /// Действия в инвентаре. Всё — ячейки: «pack» (рюкзак), «belt» (пояс), «cont» (открытый ящик/рюкзак),
    /// «fur» (печь: 0 руда, 1 серная руда, 2 топливо, 3–5 готовое), «claim» (шкаф: дерево, камень, металл, лом).
    /// Move — перетаскивание A→B, Quick — «на другую сторону» (Shift+клик, ховер-лут).
    /// </summary>
    public enum UiOp { None, Close, Move, Quick, TakeAll, PutAll, Toggle, TakeOut, Drop, Detach, Repair, Fill, Empty, Drink }   // Fill/Empty/Drink — ёмкость в карточке: налить у источника, вылить, выпить (D54)   // Detach — снять обвесы со ствола в ячейке, Repair — починить ствол маслом у верстака

    /// <summary>Ячейка для отрисовки.</summary>
    public struct CellView
    {
        public string Id, Name;
        public int Count, Rarity;
        public bool Hidden;          // ещё не обыскана
        public int Ammo, Magazine;   // у оружия
        public string Label;         // подпись под ячейкой (печь, шкаф)
        public float Durability;     // у оружия: 0..1 (−1 — не изнашивается)
        public string[] Att;         // у оружия: id обвесов по слотам (прицел, дуло, цевьё, магазин, приклад), null — пусто
        public float SightZoom;      // кратность прицела на стволе (0 — нет)
        public string Liquid;        // ёмкость (D54): что налито
        public float Liters, Capacity;
    }

    /// <summary>Что показать в окне справа (контейнер); рюкзак и пояс приходят в SelfInfo всегда.</summary>
    public sealed class UiView
    {
        public UiKind Kind;
        public string Title;
        public List<CellView> Cells = new();
        public float Progress;                // обыск текущей ячейки, 0..1
        public bool Searching;
        public bool On, Burning;               // печь
        public string Info;                    // пояснение (печь, шкаф)
        // для наглядного окна (вход → выход, шкалы)
        public float FuelSeconds = -1, OreSeconds = -1;              // печь: на сколько хватит дерева и руды
        public float Hours = -1, Grace;                              // шкаф: запас содержания, часов; отсрочка гниения, с
        public List<(string label, float value, float max)> Gauges = new();   // шкаф: нагрузка, фундаменты, двери
        public List<(string id, float perHour)> Upkeep = new();               // шкаф: расход в час
        // токарный станок (окно переработчика, Kind = Recycler, Machine = "lathe")
        public string Machine;                                                // "recycler" / "lathe"
        public string Making;                                                 // что сейчас точится (id), null — ничего
        public List<(string output, string inputs, bool can)> Makes = new();  // рецепты станка: выход, вход, доступно ли здесь
    }

    public sealed partial class World
    {
        public const float UiRange = 3f;
        public const int BoxSlots = 30;
        public const int RecyclerSlots = 12;

        /// <summary>Переработчики: раз в 2 с разбирают одну вещь из ячеек; выход кладётся туда же.</summary>
        void StepRecyclers()
        {
            foreach (var d in Deployables)
            {
                if (d.Kind != DeployKind.Recycler || !d.On) continue;
                d.SmeltTimer += Dt;
                if (d.SmeltTimer < 2f) continue;
                d.SmeltTimer = 0;
                var items = d.Storage.Items;
                int idx = items.FindIndex(s => Items.RecycleYield(s.Id).Count > 0);
                if (idx < 0) { d.On = false; continue; }
                var it = items[idx];
                var yields = Items.RecycleYield(it.Id);
                if (it.Count > 1) it.Count--; else items.RemoveAt(idx);
                foreach (var (id, n) in yields)
                {
                    if (id == "scrap") Economy.Book(Economy.IsValuable(it.Id) ? "переработчик: ценный лом" : it.Id.StartsWith("comp_") ? "переработчик: детали" : "переработчик: прочее", n);
                    var same = items.Find(s => s.Id == id && s.State == null && s.Count + n <= Items.MaxStack(id));
                    if (same != null) same.Count += n;
                    else if (items.Count < RecyclerSlots) items.Add(new ItemSlot(id, n));
                    else { d.On = false; break; }   // выход некуда класть — стоп
                }
                Events.Add(new SimEvent { Kind = SimEventKind.HitNode, Pos = d.Pos, OtherId = -1 });   // лязг — слышно
            }
        }
        // ---------- токарный станок (D46): как печь — кладёшь детали ниже, получаешь выше; шумит ----------

        /// <summary>Что станок будет точить из того, что в нём лежит: самый сложный выполнимый рецепт (тир 3 — только у верстака 3).</summary>
        Weaponry.LatheRecipe LatheJob(Deployable d)
        {
            bool b3 = BenchLevel(d.Pos, LatheBench3Range) >= 3;
            var items = d.Storage.Items;
            for (int i = Weaponry.Lathe.Count - 1; i >= 0; i--)
            {
                var r = Weaponry.Lathe[i];
                if (r.Bench3 && !b3) continue;
                bool ok = true;
                foreach (var (id, n) in r.In)
                {
                    int have = 0;
                    foreach (var s in items) if (s.Id == id && s.State == null) have += s.Count;
                    if (have < n) { ok = false; break; }
                }
                if (ok) return r;
            }
            return null;
        }

        void StepLathes()
        {
            foreach (var d in Deployables)
            {
                if (d.Kind != DeployKind.Lathe || !d.On) continue;
                var r = LatheJob(d);
                if (r == null) { d.On = false; d.SmeltTimer = 0; continue; }
                float before = d.SmeltTimer;
                d.SmeltTimer += Dt * (d.Powered ? StationDefs.Get(DeployKind.Lathe).PowerSpeed : 1f);   // под током — быстрее (D54)
                if ((int)(before / 2f) != (int)(d.SmeltTimer / 2f))
                {
                    Noises.Add(new Noise { Pos = d.Pos, Radius = 45f, SourceId = d.OwnerId, Time = Time });   // визг резца — слышно издалека
                    Events.Add(new SimEvent { Kind = SimEventKind.HitNode, Pos = d.Pos, OtherId = -2 });   // −2 — звук станка (клиент: lathe_work)
                }
                if (d.SmeltTimer < r.Time) continue;
                d.SmeltTimer = 0;
                var items = d.Storage.Items;
                if (items.Count >= RecyclerSlots && !items.Exists(s => s.Id == r.Output && s.Count < Items.MaxStack(r.Output)))
                {
                    // некуда класть: сначала проверим, освободится ли ячейка после списания
                    int freed = 0;
                    foreach (var (id, n) in r.In) { int have = 0; foreach (var s in items) if (s.Id == id) have += s.Count; if (have == n) freed++; }
                    if (freed == 0) { d.On = false; continue; }
                }
                foreach (var (id, n) in r.In)
                {
                    int left = n;
                    for (int i = items.Count - 1; i >= 0 && left > 0; i--)
                    {
                        var s = items[i];
                        if (s.Id != id || s.State != null) continue;
                        int t = Math.Min(left, s.Count);
                        s.Count -= t; left -= t;
                        if (s.Count <= 0) items.RemoveAt(i);
                    }
                }
                var same = items.Find(s => s.Id == r.Output && s.Count < Items.MaxStack(r.Output));
                if (same != null) same.Count++; else items.Add(new ItemSlot(r.Output, 1));
                Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = d.OwnerId, Pos = d.Pos, Text = $"станок: {Items.Name(r.Output)}" });
            }
        }

        // ---------- обвесы и ремонт (D46) ----------

        /// <summary>Поставить обвес на ствол. Старый обвес из того же слота — в рюкзак (кустарный изношенный — в мусор).</summary>
        public bool Attach(Actor a, WeaponState w, string attId, out string msg)
        {
            var ad = Weaponry.Att(attId);
            msg = null;
            if (ad == null || w == null || w.Def.Melee) { msg = "Обвес ставится только на ствол"; return false; }
            if (!w.Def.Accepts(ad.Slot)) { msg = $"{Items.Name(w.Def.Id)}: нет места под «{Weaponry.SlotNames[(int)ad.Slot]}»"; return false; }
            int k = (int)ad.Slot;
            TakeOff(a, w, k);
            w.Att[k] = attId;
            w.AttDur[k] = ad.MaxShots;
            msg = $"{Items.Name(attId)} → {Items.Name(w.Def.Id)}";
            return true;
        }

        /// <summary>Снять обвес из слота k в рюкзак; патроны сверх нового магазина — тоже в рюкзак.</summary>
        void TakeOff(Actor a, WeaponState w, int k)
        {
            var old = w.Att[k];
            if (old == null) return;
            var ad = Weaponry.Att(old);
            w.Att[k] = null;
            if (ad != null && ad.MaxShots > 0 && w.AttDur[k] < ad.MaxShots) Info(a, $"{Items.Name(old)} помят — выброшен");
            else a.Add(old, 1);
            if (w.Ammo > w.MagSize) { a.Add(w.Def.AmmoId, w.Ammo - w.MagSize); w.Ammo = w.MagSize; }
        }

        /// <summary>Снять все обвесы со ствола в ячейке своего рюкзака/пояса.</summary>
        void DoDetach(Actor a, string arg)
        {
            var (inv, i) = ParseCell(arg);
            if (inv is not ("pack" or "belt") || GetCell(a, inv, i)?.State is not WeaponState w) return;
            if (w.AttCount == 0) { Info(a, "Обвесов нет"); return; }
            for (int k = 0; k < w.Att.Length; k++) TakeOff(a, w, k);
            Info(a, $"Обвесы сняты: {Items.Name(w.Def.Id)}");
        }

        /// <summary>
        /// Ремонт (D46, минимально): у любого верстака, 1 оружейное масло — +50% износа и свежие обвесы с ресурсом;
        /// сломанный (0%) — ещё и пружина. Игрок: Alt+E у верстака со стволом в руках или UiOp.Repair по ячейке.
        /// </summary>
        public bool Repair(Actor a, WeaponState w)
        {
            if (w == null || w.Def.Melee || w.Def.MaxDurability <= 0) { Info(a, "Чинить нечего: возьми ствол в руки"); return false; }
            bool table = StationNear(a.Pos, DeployKind.GunTable) != null;   // оружейный стол (D54): до 100%
            if (BenchLevel(a.Pos) < 1 && !table) { Info(a, "Чинить — у верстака или оружейного стола"); return false; }
            bool worn = w.Durability < w.Def.MaxDurability - 0.5f;
            for (int k = 0; k < w.Att.Length; k++) if (Weaponry.Att(w.Att[k]) is AttachmentDef ad && ad.MaxShots > 0 && w.AttDur[k] < ad.MaxShots) worn = true;
            if (!worn) { Info(a, $"{Items.Name(w.Def.Id)} в порядке"); return false; }
            if (a.Count("comp_oil") < 1) { Info(a, "Нужно оружейное масло (бочки, ящики, токарный станок)"); return false; }
            if (w.Broken && a.Count("comp_spring") < 1) { Info(a, "Ствол сломан: нужно масло и пружина"); return false; }
            a.Take("comp_oil", 1);
            if (w.Broken) a.Take("comp_spring", 1);
            w.Durability = MathF.Min(w.Def.MaxDurability, w.Durability + w.Def.MaxDurability * (table ? Prog.GunRepairTable : Prog.GunRepairBench));
            for (int k = 0; k < w.Att.Length; k++) if (Weaponry.Att(w.Att[k]) is AttachmentDef ad) w.AttDur[k] = ad.MaxShots;
            Info(a, $"{Items.Name(w.Def.Id)} починен: {w.Durability / w.Def.MaxDurability * 100:0}%");
            return true;
        }

        static readonly string[] FurnaceIds = { "metal_ore", "sulfur_ore", "wood", "metal_fragments", "sulfur", "charcoal" };
        static readonly string[] ClaimIds = { "wood", "stone", "metal_fragments", "scrap" };

        /// <summary>Открыть окно (живым игрокам; боты пользуются быстрыми действиями).</summary>
        void OpenUi(Actor a, UiKind kind, object target)
        {
            a.OpenUi = kind;
            a.OpenTarget = target;
            if (kind == UiKind.Claim && target is Deployable c && !c.Auth.Contains(a.Id))
            {
                c.Auth.Add(a.Id);
                Info(a, "Ты вписан в шкаф: можно строить в его зоне");
            }
        }

        Vector2? UiTargetPos(Actor a) => a.OpenTarget switch
        {
            Deployable d when Deployables.Contains(d) => d.Pos,
            LootBag b when Bags.Contains(b) => b.Pos,
            _ => null,
        };

        List<ItemSlot> UiItems(Actor a) => a.OpenTarget switch
        {
            Deployable d when d.Storage != null => d.Storage.Items,
            LootBag b => b.Items,
            _ => null,
        };

        /// <summary>Контейнер, который надо обыскивать: только ящики РТ и вещи с трупов. Выброшенное и ящики баз — сразу.</summary>
        LootBag SearchBag(Actor a) => a.OpenTarget switch
        {
            LootBag b when b.IsCrate || b.Corpse => b,
            _ => null,
        };

        /// <summary>Отметка «обыскал до конца»: повторно не обыскивается, даже если туда что-то положат.</summary>
        const int Searched = 1_000_000;

        int Revealed(Actor a)
        {
            var b = SearchBag(a);
            return b == null ? int.MaxValue : b.Search.GetValueOrDefault(a.Id).n;
        }

        void Unreveal(Actor a, int index)
        {
            var b = SearchBag(a);
            if (b == null) return;
            var st = b.Search.GetValueOrDefault(a.Id);
            if (index < st.n) b.Search[a.Id] = (st.n - 1, st.t);
        }

        // ---------- ячейки ----------

        int CellCount(Actor a, string inv) => inv switch
        {
            "pack" => Actor.PackSize,
            "belt" => Actor.BeltSize,
            "cont" => a.OpenTarget is Deployable cd && cd.Storage != null ? StorageCap(cd) : Math.Max(UiItems(a)?.Count ?? 0, 12),
            "tank" => 1,
            "fur" => FurnaceIds.Length,
            "claim" => ClaimIds.Length,
            "wear" => Clothing.Slots,          // одежда (D47): 10 слотов WearSlot
            "plate" => Clothing.PlateSlots,    // пластины: перед, спина
            _ => 0,
        };

        ItemSlot GetCell(Actor a, string inv, int i)
        {
            if (i < 0 || i >= CellCount(a, inv)) return null;
            switch (inv)
            {
                case "pack": return a.Pack[i];
                case "belt": return a.Belt[i];
                case "wear": return a.Worn[i];
                case "plate": return a.Plates[i];
                case "cont":
                    var items = UiItems(a);
                    if (items == null || i >= items.Count || i >= Revealed(a)) return null;   // не обыскано — не взять
                    return items[i];
                case "fur" when a.OpenTarget is Deployable f:
                    int n = i switch { 0 => f.Ore, 1 => f.SulfurOre, 2 => (int)f.Fuel, 3 => f.OutMetal, 4 => f.OutSulfur, _ => f.OutCharcoal };
                    return n > 0 ? new ItemSlot(FurnaceIds[i], n) : null;
                case "claim" when a.OpenTarget is Deployable c:
                    int m = (int)c.Store.GetValueOrDefault(ClaimIds[i]);
                    return m > 0 ? new ItemSlot(ClaimIds[i], m) : null;
                case "tank" when a.OpenTarget is Deployable t && Liquids.Tank(t) is var (lq, l) && lq != null:
                    return new ItemSlot(lq, (int)l);
            }
            return null;
        }

        void SetCell(Actor a, string inv, int i, ItemSlot s)
        {
            switch (inv)
            {
                case "pack": a.Pack[i] = s; break;
                case "belt": a.Belt[i] = s; break;
                case "wear":
                    a.Worn[i] = s;
                    if (s != null) WearSound(a, s.Id);
                    break;
                case "plate":
                    a.Plates[i] = s;
                    if (s != null) WearSound(a, s.Id);
                    break;
                case "cont":
                    var items = UiItems(a);
                    if (s == null) { if (i < items.Count) { items.RemoveAt(i); Unreveal(a, i); } }
                    else if (i < items.Count) items[i] = s;
                    else items.Add(s);
                    break;
                case "fur" when a.OpenTarget is Deployable f:
                    int n = s?.Count ?? 0;
                    switch (i)
                    {
                        case 0: f.Ore = n; break;
                        case 1: f.SulfurOre = n; break;
                        case 2: f.Fuel = n + (f.Fuel - MathF.Floor(f.Fuel)); break;
                        case 3: f.OutMetal = n; break;
                        case 4: f.OutSulfur = n; break;
                        default: f.OutCharcoal = n; break;
                    }
                    break;
                case "claim" when a.OpenTarget is Deployable c:
                    c.Store[ClaimIds[i]] = s?.Count ?? 0;
                    break;
            }
        }

        /// <summary>Можно ли положить эту вещь в эту ячейку.</summary>
        bool Accepts(Actor a, string inv, int i, ItemSlot s)
        {
            if (s == null) return true;
            return inv switch
            {
                "belt" => s.Def != null && s.Def.Holdable,                     // пояс — то, что держат в руках
                "wear" => Clothing.Def(s.Id) is WearDef wd && (int)wd.Slot == i,   // вещь — только в свой слот
                "plate" => Clothing.Plate(s.Id) != null && i < Clothing.PlateCap(a),   // пластина — в карман надетой брони
                "fur" => i <= 2 && s.Id == FurnaceIds[i],                       // в печь: руда и дерево, готовое только забирают
                "claim" => s.Id == ClaimIds[i],
                "cont" => a.OpenTarget is not Deployable cd || cd.Storage == null || i < StorageCap(cd),
                "tank" => false,
                _ => true,
            };
        }

        int StackLimit(string inv, string id) => inv is "fur" or "claim" ? int.MaxValue : Items.MaxStack(id);

        /// <summary>Перетаскивание: положить в пустую, докинуть в такую же стопку или поменять местами.</summary>
        void MoveCell(Actor a, string src, int si, string dst, int di)
        {
            if (src == dst && si == di) return;
            var s = GetCell(a, src, si);
            // обвес на ствол в своём рюкзаке/поясе — встаёт на ствол (D46)
            if (s?.Def?.Attachment != null && dst is "pack" or "belt" && GetCell(a, dst, di)?.State is WeaponState tw && !tw.Def.Melee)
            {
                if (Attach(a, tw, s.Id, out string msg))
                {
                    s.Count--;
                    SetCell(a, src, si, s.Count > 0 ? s : null);
                }
                Info(a, msg);
                return;
            }
            if (LiquidMove(a, src, si, s, dst, di)) return;   // ёмкости: перелить / в бочку / из бочки (D54)
            if (s == null || di < 0 || di >= CellCount(a, dst) || !Accepts(a, dst, di, s)) return;
            if (dst == "cont" && di >= Revealed(a) && di < (UiItems(a)?.Count ?? 0)) return;   // в необысканную — нельзя
            var d = GetCell(a, dst, di);
            if (d == null)
            {
                if (src == "cont" && dst == "cont") { var list = UiItems(a); list.RemoveAt(si); list.Add(s); return; }
                SetCell(a, dst, di, s);
                SetCell(a, src, si, null);
            }
            else if (d.Id == s.Id && d.State == null && s.State == null)
            {
                int room = StackLimit(dst, d.Id) - d.Count;
                int n = Math.Min(room, s.Count);
                if (n <= 0) return;
                d.Count += n; s.Count -= n;
                SetCell(a, dst, di, d);
                SetCell(a, src, si, s.Count > 0 ? s : null);
            }
            else if (Accepts(a, src, si, d) && src is not ("fur" or "claim") && dst is not ("fur" or "claim"))
            {
                SetCell(a, dst, di, s);
                SetCell(a, src, si, d);
            }
        }

        /// <summary>Быстро на другую сторону: из рюкзака/пояса — в открытый контейнер, из контейнера — к себе.</summary>
        void QuickCell(Actor a, string src, int si)
        {
            var s = GetCell(a, src, si);
            if (s == null) return;
            // одежда (D47): из рюкзака/пояса — надеть (окно не открыто); из слота одежды — снять в рюкзак (или в открытый контейнер)
            if (src is "pack" or "belt" && a.OpenUi == UiKind.None && (Clothing.Def(s.Id) != null || Clothing.Plate(s.Id) != null))
            {
                if (Clothing.Def(s.Id) is WearDef wd) { MoveCell(a, src, si, "wear", (int)wd.Slot); FixPlates(a); return; }
                int cap = Clothing.PlateCap(a);
                if (cap == 0) { Info(a, "Пластину некуда вставить: надень жилет из знаков, 6Б2 или «Кору»"); return; }
                int free = Array.FindIndex(a.Plates, 0, cap, p => p == null);
                MoveCell(a, src, si, "plate", free >= 0 ? free : 0);
                return;
            }
            if (src is "wear" or "plate")
            {
                string to = a.OpenUi is UiKind.Box or UiKind.Bag ? "cont" : "pack";
                if (to == "cont" && Accepts(a, "cont", UiItems(a).Count, s)) { MoveCell(a, src, si, "cont", UiItems(a).Count); return; }
                int empty = Array.FindIndex(a.Pack, p => p == null);
                if (empty < 0) { Info(a, "Рюкзак полон"); return; }
                MoveCell(a, src, si, "pack", empty);
                return;
            }
            if (src == "tank") { foreach (var dst0 in new[] { "pack", "belt" }) for (int k = 0; k < CellCount(a, dst0); k++) if (Liquids.Fits(GetCell(a, dst0, k), s.Id) && LiquidMove(a, "tank", 0, s, dst0, k)) return; Info(a, "Некуда налить: нужна пустая ёмкость"); return; }
            if (src is "pack" or "belt" && a.OpenUi == UiKind.Tank) { if (!LiquidMove(a, src, si, s, "tank", 0)) Info(a, "В бочку — только ёмкость с жидкостью (бочка держит одну жидкость)"); return; }
            if (src is "pack" or "belt")
            {
                string dst = a.OpenUi switch { UiKind.Box or UiKind.Bag or UiKind.Recycler => "cont", UiKind.Furnace => "fur", UiKind.Claim => "claim", _ => src == "pack" ? "belt" : "pack" };
                int count = CellCount(a, dst);
                // сначала доложить в такую же стопку, потом — в первую подходящую пустую
                for (int pass = 0; pass < 2 && GetCell(a, src, si) != null; pass++)
                    for (int i = 0; i < count && GetCell(a, src, si) != null; i++)
                    {
                        var d = GetCell(a, dst, i);
                        if (pass == 0 ? d != null && d.Id == s.Id : d == null && Accepts(a, dst, i, s) && !(dst == "cont" && i < (UiItems(a)?.Count ?? 0)))
                            MoveCell(a, src, si, dst, i);
                    }
                if (GetCell(a, src, si) != null && dst == "cont" && Accepts(a, "cont", UiItems(a).Count, s)) MoveCell(a, src, si, "cont", UiItems(a).Count);
                return;
            }
            // из контейнера — к себе: стопками сколько влезет, оружие — целиком
            if (s.State == null && Items.MaxStack(s.Id) > 1)
            {
                int put = a.AddUpTo(s.Id, s.Count);
                if (put <= 0) { Info(a, "Рюкзак полон"); return; }
                s.Count -= put;
                SetCell(a, src, si, s.Count > 0 ? s : null);
            }
            else if (a.AddSlot(s)) SetCell(a, src, si, null);
            else Info(a, "Рюкзак полон");
        }

        /// <summary>Каждый тик: обыск, закрытие окна, действия из ввода.</summary>
        void StepUi(Actor a, InputCmd cmd)
        {
            var op = (UiOp)cmd.UiOp;
            // свой рюкзак и пояс можно перекладывать и без открытого окна (Tab)
            if (a.OpenUi == UiKind.None)
            {
                if (op == UiOp.Move) DoMove(a, cmd.UiItem);
                else if (op == UiOp.Quick) DoQuick(a, cmd.UiItem);
                else if (op == UiOp.Drop) DoDrop(a, cmd.UiItem);
                else if (op == UiOp.Detach) DoDetach(a, cmd.UiItem);
                else if (op == UiOp.Repair && ParseCell(cmd.UiItem) is var (ri, rj) && ri is "pack" or "belt") RepairCell(a, GetCell(a, ri, rj));
                else if (op is UiOp.Fill or UiOp.Empty or UiOp.Drink) ContainerOp(a, op, cmd.UiItem);
                return;
            }
            var pos = UiTargetPos(a);
            if (a.Dead || pos == null || Vector2.Distance(pos.Value, a.Pos) > UiRange || op == UiOp.Close || !ClearLine(a.Pos, pos.Value, null, true))
            {
                a.OpenUi = UiKind.None; a.OpenTarget = null;
                return;
            }
            // обыск: ячейки раскрываются по одной, редкое — дольше (Arc Raiders)
            if (SearchBag(a) is LootBag sb)
            {
                var st = sb.Search.GetValueOrDefault(a.Id);
                if (st.n < Searched && st.n >= sb.Items.Count) sb.Search[a.Id] = (Searched, 0);
                else if (st.n < sb.Items.Count)
                {
                    st.t += Dt;
                    var it = sb.Items[st.n];
                    if (st.t >= Items.SearchTime(it.Id))
                    {
                        Events.Add(new SimEvent { Kind = SimEventKind.Revealed, ActorId = a.Id, Pos = a.Pos, Value = Items.Rarity(it.Id), Text = it.Id });
                        st = (st.n + 1, 0);
                    }
                    sb.Search[a.Id] = st;
                }
            }
            switch (op)
            {
                case UiOp.Move: DoMove(a, cmd.UiItem); break;
                case UiOp.Quick: DoQuick(a, cmd.UiItem); break;
                case UiOp.Drop: DoDrop(a, cmd.UiItem); break;
                case UiOp.Detach: DoDetach(a, cmd.UiItem); break;
                case UiOp.Repair when ParseCell(cmd.UiItem) is var (ri, rj) && ri is "pack" or "belt": RepairCell(a, GetCell(a, ri, rj)); break;
                case UiOp.Fill or UiOp.Empty or UiOp.Drink: ContainerOp(a, op, cmd.UiItem); break;
                case UiOp.TakeAll:
                    string inv = a.OpenUi switch { UiKind.Furnace => "fur", UiKind.Claim => "claim", _ => "cont" };
                    for (int i = CellCount(a, inv) - 1; i >= 0; i--) if (inv != "fur" || i >= 3) QuickCell(a, inv, i);
                    break;
                case UiOp.PutAll:
                    for (int i = 0; i < Actor.PackSize; i++)
                        if (a.Pack[i] is ItemSlot p && !p.Id.StartsWith("key_")) QuickCell(a, "pack", i);   // ключи остаются при себе
                    break;
                case UiOp.TakeOut:
                    for (int i = 3; i < 6; i++) QuickCell(a, "fur", i);
                    break;
                case UiOp.Toggle when a.OpenTarget is Deployable lt && lt.Kind == DeployKind.Lathe:
                    lt.On = !lt.On;
                    if (lt.On && LatheJob(lt) == null) { lt.On = false; Info(a, "Станку не из чего точить: положи металл, ткань, уголь или детали по рецепту"); }
                    break;
                case UiOp.Toggle when a.OpenTarget is Deployable st && StationDefs.Get(st.Kind) is StationDef sdef && !sdef.Registered:
                    st.On = !st.On;
                    if (st.On && sdef.NeedsPower && !st.Powered) Info(a, $"{sdef.Name}: нет тока — поставь дизель-генератор рядом (15 м) и залей топляк");
                    else if (st.On && sdef.Burn != null && !HasBurn(st, sdef)) Info(a, $"{sdef.Name}: нет топлива — {Items.Name(sdef.Burn).ToLower()}");
                    break;
                case UiOp.Toggle when a.OpenTarget is Deployable rc && rc.Kind == DeployKind.Recycler:
                    rc.On = !rc.On;
                    if (rc.On && !rc.Storage.Items.Exists(s => Items.RecycleYield(s.Id).Count > 0)) { rc.On = false; Info(a, "Нечего перерабатывать: положи детали или вещи"); }
                    break;
                case UiOp.Toggle when a.OpenTarget is Deployable f:
                    f.On = !f.On;
                    if (f.On && !f.Burning) Info(a, f.Fuel <= 0 ? "Нужно дерево" : "Нужна руда");
                    break;
            }
            if (a.OpenTarget is LootBag lb && lb.IsCrate && lb.Empty && lb.RespawnLeft <= 0) lb.RespawnLeft = CrateRespawnTime(lb);
        }

        static (string inv, int i) ParseCell(string s)
        {
            int c = s?.IndexOf('#') ?? -1;
            return c > 0 && int.TryParse(s.Substring(c + 1), out int i) ? (s.Substring(0, c), i) : (null, -1);
        }

        /// <summary>«src#i>dst#j».</summary>
        void DoMove(Actor a, string arg)
        {
            var parts = arg?.Split('>');
            if (parts == null || parts.Length != 2) return;
            var (src, si) = ParseCell(parts[0]);
            var (dst, di) = ParseCell(parts[1]);
            if (src == null || dst == null) return;
            if (a.OpenUi == UiKind.None && (src is not ("pack" or "belt" or "wear" or "plate") || dst is not ("pack" or "belt" or "wear" or "plate"))) return;
            MoveCell(a, src, si, dst, di);
            if (src == "wear" || dst == "wear") FixPlates(a);   // сняли бронежилет — пластины в рюкзак
        }

        void DoQuick(Actor a, string arg)
        {
            var (inv, i) = ParseCell(arg);
            if (inv == null || (a.OpenUi == UiKind.None && inv is not ("pack" or "belt" or "wear" or "plate"))) return;
            QuickCell(a, inv, i);
            if (inv == "wear" || Clothing.Def(GetCell(a, inv, i)?.Id) != null) FixPlates(a);
        }

        /// <summary>Не влезшее в рюкзак — сумкой под ноги.</summary>
        void DropOverflow(Actor a)
        {
            if (a.Overflow.Count == 0) return;
            foreach (var s in a.Overflow) DropAtFeet(a, s);
            a.Overflow.Clear();
            Info(a, "Рюкзак полон — лишнее выпало рядом");
        }

        /// <summary>Выбросить предмет из своего рюкзака или пояса.</summary>
        void DoDrop(Actor a, string arg)
        {
            var (inv, i) = ParseCell(arg);
            if (inv is not ("pack" or "belt" or "wear" or "plate")) return;
            var s = GetCell(a, inv, i);
            if (s == null) return;
            SetCell(a, inv, i, null);
            DropAtFeet(a, s);
            if (inv == "wear") FixPlates(a);
        }

        /// <summary>Положить вещь под ноги: в лежащую рядом выброшенную сумку или новую. Открывается сразу, без обыска.</summary>
        void DropAtFeet(Actor a, ItemSlot s)
        {
            var at = a.Pos + new Vector2(0.4f, 0.3f);
            LootBag bag = null;
            foreach (var b in Bags)
                if (!b.IsCrate && !b.Corpse && Vector2.Distance(b.Pos, at) < 1f) { bag = b; break; }
            if (bag == null) { bag = new LootBag { Id = NextId(), Pos = at }; Bags.Add(bag); }
            bag.Despawn = 300f;
            bag.Items.Add(s);
        }

        // ---------- для снапшота ----------

        public static CellView Cell(ItemSlot s, string label = null) => s == null ? new CellView { Label = label } : new CellView
        {
            Id = s.Id, Name = Items.Name(s.Id), Count = s.Count, Rarity = Items.Rarity(s.Id), Label = label,
            Ammo = s.State?.Ammo ?? 0, Magazine = s.State != null && !s.State.Def.Melee ? s.State.MagSize : 0,
            Durability = s.State != null && s.State.Def.MaxDurability > 0 ? s.State.Durability / s.State.Def.MaxDurability : Clothing.PlateFrac(s),   // у пластины — её прочность (D47)
            Att = s.State != null && s.State.AttCount > 0 ? (string[])s.State.Att.Clone() : null,
            SightZoom = s.State?.SightZoom ?? 0,
            Liquid = s.Liquid, Liters = s.Liters, Capacity = s.Def?.Capacity ?? 0,
        };

        public UiView BuildUi(Actor a)
        {
            if (a.OpenUi == UiKind.None) return null;
            var v = new UiView { Kind = a.OpenUi };
            switch (a.OpenUi)
            {
                case UiKind.Box:
                case UiKind.Bag:
                    v.Title = a.OpenUi == UiKind.Box ? (a.OpenTarget is Deployable bx && bx.Kind == DeployKind.BoxLarge ? "БОЛЬШОЙ ЯЩИК" : "ЯЩИК") : a.OpenTarget is LootBag lb ? (lb.Safe ? "СЕЙФ" : lb.IsCrate ? "ЯЩИК" : lb.Corpse ? "РЮКЗАК" : "ВЕЩИ НА ЗЕМЛЕ") : "РЮКЗАК";
                    var items = UiItems(a);
                    int revealed = Math.Min(Revealed(a), items.Count);
                    int cells = CellCount(a, "cont");
                    for (int i = 0; i < cells; i++)
                        v.Cells.Add(i < items.Count ? (i < revealed ? Cell(items[i]) : new CellView { Hidden = true }) : new CellView());
                    if (SearchBag(a) is LootBag sb && revealed < items.Count)
                    {
                        v.Searching = true;
                        v.Progress = Math.Clamp(sb.Search.GetValueOrDefault(a.Id).t / Items.SearchTime(items[revealed].Id), 0, 1);
                    }
                    break;
                case UiKind.Furnace when a.OpenTarget is Deployable f:
                    v.Title = "ПЕЧЬ";
                    string[] labels = { "руда", "сера", "топливо", "металл", "сера", "уголь" };
                    for (int i = 0; i < 6; i++) v.Cells.Add(Cell(GetCell(a, "fur", i), labels[i]));
                    v.On = f.On; v.Burning = f.Burning;
                    v.FuelSeconds = f.Fuel * 0.8f;                        // 0.5 дерева за 0.4 с (StepFurnaces)
                    v.OreSeconds = (f.Ore + f.SulfurOre) * 0.4f;
                    v.Info = f.Burning ? "Горит: руда плавится, дерево горит, копится уголь" : f.On ? "Зажжена, но не хватает руды или дерева" : "Потушена: положи руду и дерево, нажми «Зажечь»";
                    break;
                case UiKind.Tank when a.OpenTarget is Deployable tk:
                    return BuildTankUi(tk);
                case UiKind.Recycler when a.OpenTarget is Deployable sd && StationDefs.Get(sd.Kind) is StationDef sdf && !sdf.Registered:
                    BuildStationUi(a, sd, sdf, v);
                    break;
                case UiKind.Recycler when a.OpenTarget is Deployable lt && lt.Kind == DeployKind.Lathe:
                {
                    v.Title = "ТОКАРНЫЙ СТАНОК";
                    v.Machine = "lathe";
                    for (int i = 0; i < RecyclerSlots; i++) v.Cells.Add(i < lt.Storage.Items.Count ? Cell(lt.Storage.Items[i]) : new CellView());
                    var job = lt.On ? LatheJob(lt) : null;
                    v.On = lt.On; v.Burning = job != null;
                    v.Making = job?.Output;
                    if (job != null) v.Progress = Math.Clamp(lt.SmeltTimer / job.Time, 0, 1);
                    bool b3 = BenchLevel(lt.Pos, LatheBench3Range) >= 3;
                    foreach (var r in Weaponry.Lathe)
                    {
                        var inp = new List<string>();
                        foreach (var (id, n) in r.In) inp.Add($"{Items.Name(id)} {n}");
                        v.Makes.Add((r.Output, string.Join(" + ", inp), !r.Bench3 || b3));
                    }
                    v.Info = job != null ? $"Точит: {Items.Name(job.Output)} ({lt.SmeltTimer:0}/{job.Time:0} с). Шумно — слышно издалека"
                                         : "Положи материалы и нажми «Запустить»: труба ← металл 40, изолента ← ткань 15, шестерни ← металл 60 + лом 10, масло ← уголь 20, " +
                                           "УСМ, затворная группа, нарезной ствол — из пружин, шестерней и труб" + (b3 ? "; рядом верстак 3 — можно хром. ствол и газоотвод" : "; детали тира 3 — только рядом с верстаком 3");
                    break;
                }
                case UiKind.Recycler when a.OpenTarget is Deployable rc:
                    v.Title = "ПЕРЕРАБОТЧИК";
                    v.Machine = "recycler";
                    for (int i = 0; i < RecyclerSlots; i++) v.Cells.Add(i < rc.Storage.Items.Count ? Cell(rc.Storage.Items[i]) : new CellView());
                    v.On = rc.On; v.Burning = rc.On;
                    v.Info = rc.On ? "Работает: детали и вещи превращаются в лом и металл (шумно — слышно издалека)"
                                   : "Положи детали (шестерни, пружины, трубы, изоленту) или ненужные вещи и нажми «Запустить»";
                    break;
                case UiKind.Claim when a.OpenTarget is Deployable c:
                    v.Title = "ШКАФ";
                    string[] cl = { "дерево", "камень", "металл", "лом" };
                    for (int i = 0; i < 4; i++) v.Cells.Add(Cell(GetCell(a, "claim", i), cl[i]));
                    var (load, found, doors) = ClaimLoad(c);
                    float h = ClaimHoursLeft(c);
                    var parts = new List<string>();
                    foreach (var (id, per) in UpkeepPerHourOf(c)) { parts.Add($"{Items.Name(id)} {per:0.#}/ч"); v.Upkeep.Add((id, per)); }
                    v.Hours = load <= 0 ? -1 : h; v.Grace = c.Grace;
                    v.Gauges.Add(("Нагрузка", load, BudgetMax));
                    v.Gauges.Add(("Фундаменты", found, FoundMax));
                    v.Gauges.Add(("Двери", doors, DoorMax));
                    v.Info = $"Нагрузка {load:0}/{BudgetMax} · фундаменты {found}/{FoundMax} · двери {doors}/{DoorMax}\n" +
                             (load <= 0 ? "Базы ещё нет — содержать нечего" :
                              $"Содержание: {string.Join(", ", parts)} · запаса на {h:0.0} ч" + (h <= 0 ? (c.Grace > 0 ? $" — гниение через {c.Grace / 60:0} мин" : " — ГНИЁТ") : ""));
                    break;
            }
            return v;
        }
    }
}
