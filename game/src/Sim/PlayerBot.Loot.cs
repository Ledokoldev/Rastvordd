using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Этап 4 мозга (docs/design/bot-brain.md): ценность лута и сейч. Бот держит при себе рабочий запас (инструменты, оружие,
    /// патроны на бой, бинты, дерево и камень на стройку), остальное ценное относит в ящик: чем больше несёт и чем опаснее вокруг,
    /// тем раньше идёт домой. Взятое из ящика под дело бронируется — его не относят обратно (иначе «сложить — забрать» по кругу).
    /// При полном рюкзаке выбрасывает самое дешёвое (дерево, камень) ради ценного.
    /// </summary>
    public sealed partial class PlayerBot
    {
        // ---------- бронь: взял под дело — не сейчить ----------

        readonly Dictionary<string, (int n, float until)> _reserved = new();

        void Reserve(string id, int n) => _reserved[id] = (Math.Max(n, Reserved(id)), _w.Time + 120);
        int Reserved(string id) => _reserved.TryGetValue(id, out var r) && r.until > _w.Time ? r.n : 0;

        /// <summary>Счётчики сейча и походов для прогонов («ЛУТ-СТАТ» в конце лога).</summary>
        public readonly Dictionary<string, int> LootStats = new();
        void LStat(string k, int n = 1) => LootStats[k] = LootStats.GetValueOrDefault(k) + n;

        /// <summary>Сколько держать при себе; излишек — в ящик. int.MaxValue — не сейчить вовсе.</summary>
        int KeepOf(string id)
        {
            var def = Items.Get(id);
            if (def == null || id.StartsWith("key_") || id == "fuse") return int.MaxValue;
            if (def.Explosive != null) return _raid != null || _rtPhase != RtPhase.None ? int.MaxValue : 0;   // бомбы — дома, пока не в рейде
            if (def.Holdable) return int.MaxValue;                       // инструменты, оружие, план, замки, установка — при себе
            int keep = id switch
            {
                "wood" => 700,
                "stone" => 500,
                "metal_ore" or "sulfur_ore" => int.MaxValue / 2,          // руда — в печь, а не в ящик
                "metal_fragments" => 150,
                "cloth" => 80,
                "ammo_762" or "ammo_545" => 120,
                "ammo_9mm" or "ammo_12g" => 60,
                "ammo_bolt" => 40,
                "comp_oil" => 2,
                "arrow" => 40,
                "bandage" => 4,
                "berries" or "canned" or "water_bottle" => 10,
                _ => 0,                                                   // лом, детали, карты, порох, сера, уголь — в ящик
            };
            if (_rtPhase is RtPhase.Prep or RtPhase.Travel or RtPhase.Loot && TripNeeds(id) > 0) keep = Math.Max(keep, TripNeeds(id));
            return keep >= int.MaxValue / 2 ? keep : keep + Reserved(id);
        }

        /// <summary>Что лишнее несу: (предмет, сколько отнести).</summary>
        List<(string id, int n)> Surplus()
        {
            var list = new List<(string, int)>();
            var seen = new HashSet<string>();
            void Look(ItemSlot s)
            {
                if (s == null || s.State != null || !seen.Add(s.Id)) return;
                int keep = KeepOf(s.Id);
                if (keep >= int.MaxValue / 2) return;
                int extra = _me.Count(s.Id) - keep;
                if (extra > 0) list.Add((s.Id, extra));
            }
            foreach (var s in _me.Pack) Look(s);
            foreach (var s in _me.Belt) Look(s);
            list.Sort((a, b) => (Items.Value(b.Item1) * b.Item2).CompareTo(Items.Value(a.Item1) * a.Item2));
            return list;
        }

        float SurplusValue()
        {
            float v = 0;
            foreach (var (id, n) in Surplus()) v += Items.Value(id) * n;
            return v;
        }

        float _surplus, _lastDanger = -999;

        /// <summary>Недавняя опасность: стреляли рядом, видел вооружённого, били меня.</summary>
        bool DangerRecent => _w.Time - _lastDanger < 60;

        // ---------- цель «сейч» ----------

        /// <summary>
        /// Нести ли добычу домой: ценность излишка (в ломах), опасность вокруг, близость дома.
        /// 200 лома в кармане — идёт даже издалека; мелочь — только когда и так рядом с домом.
        /// </summary>
        float StashScore()
        {
            if (MyBox() is not Deployable box || _site == null || _raid != null || _w.Time < _stashBlock) return 0;
            if (_rtPhase is RtPhase.Travel or RtPhase.Loot) return 0;   // в походе мешок не бросают — несут после
            if (box.Storage.Items.Count >= World.BoxSlots && !TidyBox(box, true) && Surplus().TrueForAll(s => !box.Storage.Items.Exists(x => x.Id == s.id))) return 0;
            float v = _surplus;
            bool guns = SpareGun() != null;
            if (v < 25 && !guns) return 0;
            float home = Vector2.Distance(_me.Pos, box.Pos);
            // раздел 10, правило сдачи: ≥ 150 лома при себе, рюкзак на 70 %, после похода на РТ или лута убитого, ночью — домой и сложить всё,
            // кроме своего набора. Это выше охоты, перехвата и мести: несущий лут игрок в Расте идёт домой, а не воюет.
            string why = DepositDue(v);
            if (why != null)
            {
                if (_depositSaid != why) { _depositSaid = why; Thought($"пора домой сложить лут: {why}"); LStat("сдача: " + why.Split(' ')[0]); }
                return 89;
            }
            _depositSaid = null;
            float s = 20 + 45 * Math.Clamp(v / 150f, 0, 1) + (guns ? 15 : 0);
            if (DangerRecent) s += 12;          // стреляют — ценное лучше в ящик
            if (home < 30) s += 15;             // рядом — по пути
            if (_rtPhase == RtPhase.Leave) s += 10;
            return MathF.Min(s, 78) * W("сейч");
        }

        string _depositSaid;
        /// <summary>Пора сдать лут (после похода на РТ, лута убитого, сброса): до какого времени действует.</summary>
        float _depositAfter = -999;
        void DepositSoon() => _depositAfter = _w.Time + 600;

        /// <summary>Причина нести всё домой сейчас (null — не обязательно).</summary>
        string DepositDue(float v)
        {
            if (v < 25 && SpareGun() == null) return null;
            // 2026-10-27 (напарник): не бегать домой после каждого мелкого подлута — пороги 40 → 100 лома, рюкзак 70 → 85 %
            if (v >= CarryAvoid) return $"несу ≈{v:0} лома";
            int used = 0;
            foreach (var s in _me.Pack) if (s != null) used++;
            if (used >= Actor.PackSize * 0.85f && v >= 60) return $"рюкзак полон ({used}/{Actor.PackSize})";
            if (_w.Time < _depositAfter && v >= 100) return "после добычи (РТ, убитый, груз) — сначала в ящик";
            if (Night && v >= 100 && _rtPhase == RtPhase.None) return "ночь — сортирую дома";
            return null;
        }

        /// <summary>Ночь (раздел 10: меньше бродить, крафт, сортировка, фарм у дома).</summary>
        bool Night => World.NightFactorAt(_w.TimeOfDay) > 0.6f;

        /// <summary>Лишний ствол при себе (кроме лучшего с патронами и лучшего вообще): в ящик — ценное дома, при себе набор.</summary>
        ItemSlot SpareGun()
        {
            string keep1 = Ranged(), keep2 = BestOwnedGun();
            for (int k = 0; k < Actor.BeltSize + Actor.PackSize; k++)
            {
                var s = k < Actor.BeltSize ? _me.Belt[k] : _me.Pack[k - Actor.BeltSize];
                if (s?.State is not WeaponState st || st.Def.Melee) continue;
                if (!st.Broken && (s.Id == keep1 || s.Id == keep2)) continue;
                if (s.Id == "weapon_bow" && keep1 == "weapon_bow") continue;
                return s;
            }
            return null;
        }

        /// <summary>Забрать ствол из ящика целиком (ячейку с износом и патронами). true — занят этим в этот тик.</summary>
        bool TakeGunFromBox(Deployable box, string id, ref InputCmd cmd)
        {
            if (Has(id)) return false;
            var slot = box.Storage.Items.Find(s => s.Id == id && s.State != null && !(s.State is WeaponState ws && ws.Broken));
            if (slot == null) return false;
            Doing = $"берёт из ящика: {Items.Name(id)}";
            if (!Reach(box.Pos, ref cmd)) return true;
            box.Storage.Items.Remove(slot);
            if (!_me.AddSlot(slot)) { box.Storage.Items.Add(slot); return false; }
            Reserve(id, 1);
            LStat("набор: ствол из ящика");
            return true;
        }

        /// <summary>Положить лишний ствол в ящик целиком (с патронами в магазине и износом). Бот уже у ящика.</summary>
        bool PutSpareGun(Deployable box)
        {
            var s = SpareGun();
            if (s == null || box.Storage.Items.Count >= World.BoxSlots || Vector2.Distance(_me.Pos, box.Pos) > World.InteractRange + box.Radius) return false;
            for (int i = 0; i < Actor.BeltSize; i++) if (_me.Belt[i] == s) { if (_me.Current == i) _me.Current = 0; _me.Belt[i] = null; }
            for (int i = 0; i < Actor.PackSize; i++) if (_me.Pack[i] == s) _me.Pack[i] = null;
            box.Storage.Items.Add(s);
            return true;
        }

        /// <summary>
        /// Ящик почти полон (D49): выкинуть мелочь по одной ячейке — одежду не лучше надетой, кустарные обвесы, сломанные стволы,
        /// дешёвое (ценность ячейки &lt; 12 лома). Раньше ящик забивался одеждой до 30/30, рюкзак не разгружался, походы на РТ
        /// кончались «рюкзак полон» на входе, а ценный лом не доходил до переработчика.
        /// </summary>
        bool TidyBox(Deployable box, bool dry = false)
        {
            var items = box.Storage.Items;
            if (items.Count < World.BoxSlots - 3) return false;
            var tossed = new List<string>();
            for (int k = 0; k < 6 && items.Count >= World.BoxSlots - 5; k++)
            {
                int pick = -1; float low = 12;
                for (int i = 0; i < items.Count; i++)
                {
                    var s = items[i];
                    if (s.Id == "scrap" || s.Id.StartsWith("key_") || s.Id.StartsWith("comp_") || Economy.IsValuable(s.Id) || s.Id.StartsWith("deploy_")) continue;
                    float v = s.State is WeaponState ws ? (ws.Broken ? 1 : Items.Value(s.Id)) : Items.Value(s.Id) * s.Count;
                    if (Clothing.Def(s.Id) is WearDef wd && Clothing.Def(_me.Worn[(int)wd.Slot]?.Id) is WearDef cur && Clothing.BotScore(cur) >= Clothing.BotScore(wd)) v = MathF.Min(v, 2);
                    if (v < low) { low = v; pick = i; }
                }
                if (pick < 0) break;
                if (dry) return true;
                tossed.Add(Items.Name(items[pick].Id));
                items.RemoveAt(pick);
            }
            if (tossed.Count > 0) { Thought($"ящик забит — выкинул хлам: {string.Join(", ", tossed)}"); LStat("ящик: выкинул хлам", tossed.Count); }
            return tossed.Count > 0;
        }

        /// <summary>
        /// Рюкзак (D49): ключи от дверей, которых больше нет, и запасная одежда не лучше надетой (дешевле 12 лома) — выкинуть.
        /// Раньше рюкзак забивался ключами и тряпьём, походы на РТ кончались «рюкзак полон» на входе.
        /// </summary>
        void TidyPack()
        {
            int used = 0;
            foreach (var s in _me.Pack) if (s != null) used++;
            if (used < Actor.PackSize * 0.6f) return;
            var tossed = new List<string>();
            for (int i = 0; i < Actor.PackSize; i++)
            {
                var s = _me.Pack[i];
                if (s == null) continue;
                bool junk = false;
                if (s.Id.StartsWith("key_") && int.TryParse(s.Id.Substring(4), out int door)) junk = !_w.Structures.Exists(x => x.Id == door);
                else if (Clothing.Def(s.Id) is WearDef wd && Items.Value(s.Id) < 12 && Clothing.Def(_me.Worn[(int)wd.Slot]?.Id) is WearDef cur && Clothing.BotScore(cur) >= Clothing.BotScore(wd)) junk = true;
                if (!junk) continue;
                tossed.Add(Items.Name(s.Id));
                _me.Pack[i] = null;
            }
            if (tossed.Count > 0) { Thought($"рюкзак: выкинул лишнее — {string.Join(", ", tossed)}"); LStat("рюкзак: выкинул", tossed.Count); }
        }

        string _stashSaidFor;
        float _stashBest = float.MaxValue, _stashSince, _stashBlock;

        bool StashAct(ref InputCmd cmd)
        {
            var box = MyBox();
            if (box == null) return false;
            var list = Surplus();
            if (list.Count == 0 && SpareGun() == null) return false;
            if (list.Count == 0) list.Add((SpareGun().Id, 0));
            float v = 0;
            foreach (var (id, n) in list) v += Items.Value(id) * n;
            Doing = $"несёт добычу домой (≈{v:0} лома)";
            if (_stashSaidFor != $"{box.Id}:{(int)(_w.Time / 120)}")
            {
                _stashSaidFor = $"{box.Id}:{(int)(_w.Time / 120)}";
                Thought($"несу домой ≈{v:0} лома ({Items.Name(list[0].id)} ×{list[0].n}{(list.Count > 1 ? $" и ещё {list.Count - 1}" : "")}){(DangerRecent ? " — рядом стреляли" : "")}");
            }
            if (Vector2.Distance(_me.Pos, box.Pos) > 25 && Ranged() is string g) Hold(g, ref cmd);
            // нет продвижения к ящику 45 с — бросить на 2 минуты (директор выберет другое, потом снова)
            float bdist = Vector2.Distance(_me.Pos, box.Pos);
            if (bdist < _stashBest - 1f) { _stashBest = bdist; _stashSince = _w.Time; }
            else if (_w.Time - _stashSince > 45 && bdist > 2.5f) { _stashBlock = _w.Time + 120; _stashBest = float.MaxValue; _stashSince = _w.Time; Thought("до ящика не дойти — отложу сейч"); return false; }
            if (!Reach(box.Pos, ref cmd, 1.0f)) return true;
            _stashBest = float.MaxValue; _stashSince = _w.Time;
            var parts = new List<string>();
            int moved = 0;
            TidyBox(box);
            for (int k = 0; k < 4; k++)
            {
                string gid = SpareGun()?.Id;
                if (gid == null || !PutSpareGun(box)) break;
                moved++; LStat("сдача: ствол в ящик");
                if (parts.Count < 4) parts.Add(Items.Name(gid));
            }
            foreach (var (id, n) in list)
            {
                if (n <= 0) continue;
                if (box.Storage.Items.Count >= World.BoxSlots && !box.Storage.Items.Exists(x => x.Id == id)) continue;
                int put = _w.BotPut(_me, box, id, n);
                if (put <= 0) continue;
                moved += put;
                if (_rtPhase != RtPhase.None) _rtStashed[id] = _rtStashed.GetValueOrDefault(id) + put;   // для итога похода
                if (parts.Count < 4) parts.Add($"{Items.Name(id)} ×{put}");
            }
            if (moved > 0)
            {
                LStat("сейч");
                LStat("сейч, ломов", (int)v);
                Thought($"сложил в ящик: {string.Join(", ", parts)} (ящик ≈{StashValue():0} лома)");
            }
            _surplus = SurplusValue();
            if (moved > 0) _depositAfter = -999;
            return moved > 0 || _surplus >= 25;
        }

        // ---------- рюкзак полон: выбросить дешёвое ради ценного ----------

        readonly List<(Vector2 at, float until)> _drops = new();

        /// <summary>Сумка, которую я сам выбросил (не подбирать её обратно).</summary>
        bool MyDrop(LootBag b) => !b.IsCrate && _drops.Exists(d => d.until > _w.Time && Vector2.DistanceSquared(d.at, b.Pos) < 2.5f * 2.5f);

        int FreePack()
        {
            int n = 0;
            foreach (var s in _me.Pack) if (s == null) n++;
            return n;
        }

        /// <summary>
        /// Освободить ячейку под вещь ценностью incoming: выбросить самую дешёвую стопку из «тяжёлого» (дерево, камень, ткань,
        /// ягоды, руда), если она дешевле. true — в этот тик выбрасываю (ввод занят).
        /// </summary>
        bool MakeRoom(float incoming, ref InputCmd cmd)
        {
            if (FreePack() >= 2) return false;
            int worst = -1;
            float wv = float.MaxValue;
            for (int i = 0; i < Actor.PackSize; i++)
            {
                var s = _me.Pack[i];
                if (s == null || s.State != null) continue;
                if (s.Id is not ("wood" or "stone" or "cloth" or "berries" or "metal_ore" or "sulfur_ore" or "charcoal" or "water_bottle" or "canned")) continue;
                float v = Items.Value(s.Id) * s.Count;
                if (v < wv) { wv = v; worst = i; }
            }
            if (worst < 0 || wv >= incoming) return false;
            var it = _me.Pack[worst];
            Thought($"рюкзак полон — выбрасываю {Items.Name(it.Id)} ×{it.Count} (≈{wv:0} лома) ради более ценного");
            LStat("выброшено ради ценного");
            cmd.UiOp = (int)UiOp.Drop;
            cmd.UiItem = $"pack#{worst}";
            _drops.RemoveAll(d => d.until < _w.Time);
            _drops.Add((_me.Pos, _w.Time + 600));
            return true;
        }

        /// <summary>Ценность ящика на глаз до обыска: по уровню (бытовой, малая охрана, крупный РТ).</summary>
        static float CrateGuess(LootBag b) => b.Safe ? 90 : b.Punch ? 150 : b.Tier switch { 0 => 15, 1 => 30, _ => 50 };
    }
}
