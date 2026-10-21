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
                "ammo_pistol" => 60,
                "ammo_rifle" => 120,
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
            if (box.Storage.Items.Count >= World.BoxSlots && Surplus().TrueForAll(s => !box.Storage.Items.Exists(x => x.Id == s.id))) return 0;
            float v = _surplus;
            if (v < 25) return 0;
            float home = Vector2.Distance(_me.Pos, box.Pos);
            float s = 20 + 45 * Math.Clamp(v / 200f, 0, 1);
            if (DangerRecent) s += 12;          // стреляют — ценное лучше в ящик
            if (home < 30) s += 15;             // рядом — по пути
            if (_rtPhase == RtPhase.Leave) s += 10;
            return MathF.Min(s, 78) * W("сейч");
        }

        string _stashSaidFor;
        float _stashBest = float.MaxValue, _stashSince, _stashBlock;

        bool StashAct(ref InputCmd cmd)
        {
            var box = MyBox();
            if (box == null) return false;
            var list = Surplus();
            if (list.Count == 0) return false;
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
            foreach (var (id, n) in list)
            {
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
