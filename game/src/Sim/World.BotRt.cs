using System;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Быстрые действия ботов на РТ и дома (этап 4 мозга, docs/design/bot-brain.md): то же, что игрок делает руками в окнах,
    /// с той же проверкой «рукой» — рядом и без стены между. Логику РТ (Substation.cs) не трогаем: зовём её же методы.
    /// </summary>
    public sealed partial class World
    {
        bool BotReach(Actor a, Vector2 pos, float r = 0f) =>
            !a.Dead && Vector2.Distance(a.Pos, pos) <= InteractRange + r && ClearLine(a.Pos, pos, null, true);

        /// <summary>Положить в ящик n штук предмета из рюкзака (сейч по выбору: инструменты и патроны остаются при себе).</summary>
        public int BotPut(Actor a, Deployable box, string id, int n)
        {
            if (box?.Storage == null || n <= 0 || id.StartsWith("key_") || !BotReach(a, box.Pos, box.Radius)) return 0;
            int put = a.Take(id, n);
            if (put <= 0) return 0;
            var same = box.Storage.Items.Find(s => s.Id == id && s.State == null);
            if (same != null) same.Count += put; else box.Storage.Items.Add(new ItemSlot(id, put));
            return put;
        }

        /// <summary>Пробка в главный щиток Подстанции (как E у щитка).</summary>
        public bool BotUsePanel(Actor a, Substation s)
        {
            if (s == null || !BotReach(a, s.PanelPos)) return false;
            UsePanel(a, s);
            return true;
        }

        /// <summary>Перфокарта в считыватель крейта (как E у запертого крейта).</summary>
        public bool BotUsePunchCrate(Actor a, LootBag crate)
        {
            if (crate == null || !crate.Punch || crate.Unlocked || !Bags.Contains(crate) || !BotReach(a, crate.Pos, 0.3f)) return false;
            UsePunchCrate(a, crate);
            return true;
        }

        /// <summary>Тайник РТ тира 0 «держи E» (бардачок, касса, заклинившая дверь): начать; дальше — стоять рядом, пока не готово.</summary>
        public bool BotUseStash(Actor a, Stash st)
        {
            if (st == null || st.Done || !BotReach(a, st.Pos)) return false;
            UseStash(a, st);
            return st.ActorId == a.Id;
        }

        /// <summary>Сейф картой доступа (как E у сейфа). true — открыт.</summary>
        public bool BotOpenSafe(Actor a, LootBag safe)
        {
            if (safe == null || !safe.Safe || !Bags.Contains(safe) || !BotReach(a, safe.Pos, 0.3f)) return false;
            if (safe.Unlocked) return true;
            if (a.Take("keycard", 1) == 0) { Info(a, "Сейф заперт: нужна карта доступа"); return false; }
            safe.Unlocked = true;
            Info(a, "Сейф открыт картой доступа");
            return true;
        }

        /// <summary>Что бот несёт в переработчик (D49): ценный лом — весь, общие детали и детали тира 2 — сверх запаса под стволы.</summary>
        public static readonly string[] BotRecycleIds = BuildRecycleIds();
        static string[] BuildRecycleIds()
        {
            var l = new System.Collections.Generic.List<string> { "comp_gears", "comp_spring", "comp_pipe", "comp_tape" };
            foreach (var v in Economy.Valuables) l.Add(v.id);
            l.AddRange(Weaponry.Tier2Parts);
            return l.ToArray();
        }

        /// <summary>Сколько лома выйдет из того, что бот отнёс бы в переработчик.</summary>
        public static int BotRecycleScrap(Func<string, int> stock, Func<string, int> keep)
        {
            int n = 0;
            foreach (var id in BotRecycleIds) n += Math.Max(0, stock(id) - keep(id)) * Economy.ScrapYield(id);
            return n;
        }

        /// <summary>Сколько общих деталей у бота сверх запаса под стволы (keep — сколько держать каждой).</summary>
        public static int BotRecyclable(Actor a, Func<string, int> keep)
        {
            int n = 0;
            foreach (var id in BotRecycleIds) n += Math.Max(0, a.Count(id) - keep(id));
            return n;
        }

        /// <summary>Переработчик: выложить лишние общие детали (сверх запаса под стволы, D46) и включить. Возвращает, сколько положено.</summary>
        public int BotRecyclerPut(Actor a, Deployable rc, Func<string, int> keep = null)
        {
            if (rc == null || rc.Kind != DeployKind.Recycler || !BotReach(a, rc.Pos, rc.Radius)) return 0;
            int n = 0;
            foreach (var id in BotRecycleIds)
            {
                int c = a.Take(id, Math.Max(0, a.Count(id) - (keep?.Invoke(id) ?? 0)));
                if (c <= 0) continue;
                var same = rc.Storage.Items.Find(s => s.Id == id);
                if (same != null) same.Count += c; else rc.Storage.Items.Add(new ItemSlot(id, c));
                n += c;
            }
            if (rc.Storage.Items.Exists(s => Items.RecycleYield(s.Id).Count > 0)) rc.On = true;
            return n;
        }

        /// <summary>Токарный станок бота (D46): положить материалы рецепта и включить (мозг — PlayerBot.Lathe.cs).</summary>
        public bool BotLathePut(Actor a, Deployable lathe, string output)
        {
            var r = Weaponry.LatheFor(output);
            if (r == null || lathe?.Kind != DeployKind.Lathe || !BotReach(a, lathe.Pos, lathe.Radius)) return false;
            foreach (var (id, n) in r.In) if (a.Count(id) < n) return false;
            foreach (var (id, n) in r.In)
            {
                int c = a.Take(id, n);
                var same = lathe.Storage.Items.Find(s => s.Id == id && s.State == null);
                if (same != null) same.Count += c; else lathe.Storage.Items.Add(new ItemSlot(id, c));
            }
            lathe.On = LatheJob(lathe) != null;
            return true;
        }

        /// <summary>Токарный станок: забрать готовые детали (id из рецептов станка, кроме сырья).</summary>
        public int BotLatheTake(Actor a, Deployable lathe)
        {
            if (lathe?.Kind != DeployKind.Lathe || !BotReach(a, lathe.Pos, lathe.Radius)) return 0;
            int got = 0;
            for (int i = lathe.Storage.Items.Count - 1; i >= 0; i--)
            {
                var s = lathe.Storage.Items[i];
                if (Weaponry.LatheFor(s.Id) == null) continue;
                int put = a.AddUpTo(s.Id, s.Count);
                got += put; s.Count -= put;
                if (s.Count <= 0) lathe.Storage.Items.RemoveAt(i);
            }
            return got;
        }

        /// <summary>Переработчик: забрать готовое (лом, металл, ткань). Детали, что ещё не разобраны, остаются.</summary>
        public int BotRecyclerTake(Actor a, Deployable rc)
        {
            if (rc == null || rc.Kind != DeployKind.Recycler || !BotReach(a, rc.Pos, rc.Radius)) return 0;
            int got = 0;
            for (int i = rc.Storage.Items.Count - 1; i >= 0; i--)
            {
                var s = rc.Storage.Items[i];
                if (Items.RecycleYield(s.Id).Count > 0) continue;
                int put = a.AddUpTo(s.Id, s.Count);
                got += put;
                s.Count -= put;
                if (s.Count <= 0) rc.Storage.Items.RemoveAt(i);
            }
            return got;
        }
    }
}
