using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>Какое окно открыто у игрока (по E): ящик, рюкзак/ящик РТ, печь, шкаф.</summary>
    public enum UiKind { None, Box, Bag, Furnace, Claim, Recycler }

    /// <summary>
    /// Действия в инвентаре. Всё — ячейки: «pack» (рюкзак), «belt» (пояс), «cont» (открытый ящик/рюкзак),
    /// «fur» (печь: 0 руда, 1 серная руда, 2 топливо, 3–5 готовое), «claim» (шкаф: дерево, камень, металл, лом).
    /// Move — перетаскивание A→B, Quick — «на другую сторону» (Shift+клик, ховер-лут).
    /// </summary>
    public enum UiOp { None, Close, Move, Quick, TakeAll, PutAll, Toggle, TakeOut, Drop }

    /// <summary>Ячейка для отрисовки.</summary>
    public struct CellView
    {
        public string Id, Name;
        public int Count, Rarity;
        public bool Hidden;          // ещё не обыскана
        public int Ammo, Magazine;   // у оружия
        public string Label;         // подпись под ячейкой (печь, шкаф)
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
                    var same = items.Find(s => s.Id == id && s.State == null && s.Count + n <= Items.MaxStack(id));
                    if (same != null) same.Count += n;
                    else if (items.Count < RecyclerSlots) items.Add(new ItemSlot(id, n));
                    else { d.On = false; break; }   // выход некуда класть — стоп
                }
                Events.Add(new SimEvent { Kind = SimEventKind.HitNode, Pos = d.Pos, OtherId = -1 });   // лязг — слышно
            }
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
            "cont" => a.OpenUi == UiKind.Box ? BoxSlots : a.OpenUi == UiKind.Recycler ? RecyclerSlots : Math.Max(UiItems(a)?.Count ?? 0, 12),
            "fur" => FurnaceIds.Length,
            "claim" => ClaimIds.Length,
            _ => 0,
        };

        ItemSlot GetCell(Actor a, string inv, int i)
        {
            if (i < 0 || i >= CellCount(a, inv)) return null;
            switch (inv)
            {
                case "pack": return a.Pack[i];
                case "belt": return a.Belt[i];
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
            }
            return null;
        }

        void SetCell(Actor a, string inv, int i, ItemSlot s)
        {
            switch (inv)
            {
                case "pack": a.Pack[i] = s; break;
                case "belt": a.Belt[i] = s; break;
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
                "fur" => i <= 2 && s.Id == FurnaceIds[i],                       // в печь: руда и дерево, готовое только забирают
                "claim" => s.Id == ClaimIds[i],
                "cont" => a.OpenUi == UiKind.Box ? i < BoxSlots : a.OpenUi != UiKind.Recycler || i < RecyclerSlots,
                _ => true,
            };
        }

        int StackLimit(string inv, string id) => inv is "fur" or "claim" ? int.MaxValue : Items.MaxStack(id);

        /// <summary>Перетаскивание: положить в пустую, докинуть в такую же стопку или поменять местами.</summary>
        void MoveCell(Actor a, string src, int si, string dst, int di)
        {
            if (src == dst && si == di) return;
            var s = GetCell(a, src, si);
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
                case UiOp.Toggle when a.OpenTarget is Deployable rc && rc.Kind == DeployKind.Recycler:
                    rc.On = !rc.On;
                    if (rc.On && !rc.Storage.Items.Exists(s => Items.RecycleYield(s.Id).Count > 0)) { rc.On = false; Info(a, "Нечего перерабатывать: положи детали или вещи"); }
                    break;
                case UiOp.Toggle when a.OpenTarget is Deployable f:
                    f.On = !f.On;
                    if (f.On && !f.Burning) Info(a, f.Fuel <= 0 ? "Нужно дерево" : "Нужна руда");
                    break;
            }
            if (a.OpenTarget is LootBag lb && lb.IsCrate && lb.Empty && lb.RespawnLeft <= 0) lb.RespawnLeft = 90f;
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
            if (a.OpenUi == UiKind.None && (src is not ("pack" or "belt") || dst is not ("pack" or "belt"))) return;
            MoveCell(a, src, si, dst, di);
        }

        void DoQuick(Actor a, string arg)
        {
            var (inv, i) = ParseCell(arg);
            if (inv == null || (a.OpenUi == UiKind.None && inv is not ("pack" or "belt"))) return;
            QuickCell(a, inv, i);
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
            if (inv is not ("pack" or "belt")) return;
            var s = GetCell(a, inv, i);
            if (s == null) return;
            SetCell(a, inv, i, null);
            DropAtFeet(a, s);
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
            Ammo = s.State?.Ammo ?? 0, Magazine = s.State != null && !s.State.Def.Melee ? s.State.Def.Magazine : 0,
        };

        public UiView BuildUi(Actor a)
        {
            if (a.OpenUi == UiKind.None) return null;
            var v = new UiView { Kind = a.OpenUi };
            switch (a.OpenUi)
            {
                case UiKind.Box:
                case UiKind.Bag:
                    v.Title = a.OpenUi == UiKind.Box ? "ЯЩИК" : a.OpenTarget is LootBag lb ? (lb.Safe ? "СЕЙФ" : lb.IsCrate ? "ЯЩИК" : lb.Corpse ? "РЮКЗАК" : "ВЕЩИ НА ЗЕМЛЕ") : "РЮКЗАК";
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
                    v.Info = f.Burning ? "Горит: руда плавится, дерево горит, копится уголь" : f.On ? "Зажжена, но не хватает руды или дерева" : "Потушена: положи руду и дерево, нажми «Зажечь»";
                    break;
                case UiKind.Recycler when a.OpenTarget is Deployable rc:
                    v.Title = "ПЕРЕРАБОТЧИК";
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
                    foreach (var (id, per) in UpkeepPerHourOf(c)) parts.Add($"{Items.Name(id)} {per:0.#}/ч");
                    v.Info = $"Нагрузка {load:0}/{BudgetMax} · фундаменты {found}/{FoundMax} · двери {doors}/{DoorMax}\n" +
                             (load <= 0 ? "Базы ещё нет — содержать нечего" :
                              $"Содержание: {string.Join(", ", parts)} · запаса на {h:0.0} ч" + (h <= 0 ? (c.Grace > 0 ? $" — гниение через {c.Grace / 60:0} мин" : " — ГНИЁТ") : ""));
                    break;
            }
            return v;
        }
    }
}
