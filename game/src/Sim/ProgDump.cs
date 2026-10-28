using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;

namespace Rastvordd.Sim
{
    /// <summary>Отладка (--progdump=путь): полный машиночитаемый дамп прогрессии и экономики из живых данных игры (для сайта дерева прогрессии).</summary>
    public static class ProgDump
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const int Samples = 3000;

        static void Stats(Dictionary<string, object> st, string pre, object def)
        {
            if (def == null) return;
            foreach (var f in def.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                var v = f.GetValue(def);
                if (f.Name is "Id" or "Name" or "Weapon" || v == null) continue;
                switch (v)
                {
                    case float x when x != 0: st[pre + f.Name] = Math.Round(x, 3); break;
                    case double x when x != 0: st[pre + f.Name] = Math.Round(x, 3); break;
                    case int x when x != 0: st[pre + f.Name] = x; break;
                    case bool x when x: st[pre + f.Name] = true; break;
                    case string x when x.Length > 0: st[pre + f.Name] = x; break;
                    case Enum x: st[pre + f.Name] = x.ToString(); break;
                }
            }
        }

        public static string Build(World w)
        {
            var ids = new HashSet<string>();
            string Id(string s) { if (s != null) ids.Add(s); return s; }
            var notes = new List<string>();

            // ---- рецепты (итоговый список после RecipeOverride)
            var recipes = new List<object>();
            foreach (var r in Recipes.All)
            {
                string tech = Clothing.TechKey(r.Output) ?? (Tech.Get(r.Output) != null ? r.Output : null);
                recipes.Add(new Dictionary<string, object>
                {
                    ["out"] = Id(r.Output), ["count"] = r.Count,
                    ["cost"] = r.Cost.Select(c => new object[] { Id(c.id), c.count }).ToList(),
                    ["bench"] = r.Bench, ["station"] = r.Station == DeployKind.None ? "" : r.Station.ToString(),
                    ["station_name"] = r.Station == DeployKind.None ? "" : World.StationName(r.Station),
                    ["time"] = r.Time, ["tech"] = tech,
                });
            }

            // токарный станок: рецепты живут в Weaponry.Lathe, не в Recipes.All
            foreach (var r in Weaponry.Lathe)
                recipes.Add(new Dictionary<string, object>
                {
                    ["out"] = Id(r.Output), ["count"] = 1,
                    ["cost"] = r.In.Select(c => new object[] { Id(c.id), c.n }).ToList(),
                    ["bench"] = r.Bench3 ? 3 : 2, ["station"] = "Lathe", ["station_name"] = "Токарный станок",
                    ["time"] = r.Time, ["tech"] = Tech.Get(r.Output) != null ? r.Output : null,
                });

            // ---- дерево изучения
            var tech2 = new List<object>();
            foreach (var n in Tech.All)
                tech2.Add(new Dictionary<string, object>
                {
                    ["id"] = Id(n.Id), ["name"] = Clothing.TechName(n.Id) ?? Items.Name(n.Id), ["bench"] = n.Bench, ["scrap"] = n.Scrap, ["lane"] = n.Lane, ["order"] = n.Order,
                    ["req"] = n.Requires.Select(Id).ToList(), ["req_any"] = n.RequiresAny.Select(Id).ToList(),
                });

            // ---- лут (сэмплирование реальных функций)
            var loot = new Dictionary<string, object>();
            var refl = typeof(World);
            MethodInfo M(string name) => refl.GetMethod(name, All) ?? throw new Exception("нет метода " + name);
            var fillCrate = M("FillCrate"); var fillPunch = M("FillPunchCrate"); var fillEvent = M("FillEventCrate");
            var fillRadio = M("FillRadioDrop"); var hitBarrel = M("HitBarrel"); var npcDrop = M("NpcDrop"); var respawnNpc = M("RespawnNpc");
            var f3 = refl.GetField("_tier3ByHour", All); var fe = refl.GetField("_eliteAt", All);
            var npcKit = (Dictionary<Vector2, string>)refl.GetField("NpcKit", All).GetValue(w);
            w.Director ??= new Director(w.Seed);
            float savedTime = w.Time; int savedPhase = w.Director.Phase;
            void Late(int phase, float time) { w.Time = time; w.Director.Phase = phase; }
            void Reset() { ((System.Collections.IDictionary)f3.GetValue(w)).Clear(); fe.SetValue(w, -1e9f); }

            void Sample(string key, string label, string zone, string where, Func<LootBag> make)
            {
                var has = new Dictionary<string, int>(); var sum = new Dictionary<string, long>();
                int ok = 0;
                try
                {
                    for (int i = 0; i < Samples; i++)
                    {
                        Reset();
                        var bag = make(); if (bag == null) continue; ok++;
                        var seen = new HashSet<string>();
                        foreach (var s in bag.Items)
                        {
                            sum[s.Id] = sum.GetValueOrDefault(s.Id) + (s.IsContainer || s.Def?.IsLiquid == true ? 1 : s.Count);
                            if (seen.Add(s.Id)) has[s.Id] = has.GetValueOrDefault(s.Id) + 1;
                        }
                    }
                }
                catch (Exception ex) { notes.Add($"лут {key}: ошибка сэмплирования {ex.InnerException?.Message ?? ex.Message}"); }
                var items = new Dictionary<string, object>();
                foreach (var (id, c) in has.OrderByDescending(k => k.Value))
                    items[Id(id)] = new Dictionary<string, object> { ["p"] = Math.Round(c / (double)Math.Max(1, ok), 4), ["avg"] = Math.Round(sum[id] / (double)c, 2) };
                loot[key] = new Dictionary<string, object> { ["label"] = label, ["zone"] = zone, ["where"] = where, ["rolls"] = ok, ["items"] = items };
            }

            LootBag Crate(int tier, bool safe = false, string rt = null, string box = null) { var b = new LootBag { Id = 1, IsCrate = true, Tier = tier, Safe = safe, Rt = rt, Box = box }; fillCrate.Invoke(w, new object[] { b }); return b; }
            LootBag Punch(bool el) { var b = new LootBag { Id = 1, IsCrate = true, Tier = 1, Punch = true }; fillPunch.Invoke(w, new object[] { b, el }); return b; }
            LootBag Event(int ph) { var b = new LootBag { Id = 1, IsCrate = true, Tier = 2, Punch = true, Look = "airdrop" }; fillEvent.Invoke(w, new object[] { b, ph }); return b; }

            Late(2, 6 * 3600f);   // поздний вайп: детали тира 3 и элита доступны (в ранней фазе их нет вообще)
            Sample("crate0", "Ящик РТ тира 0 (без охраны)", "world", "Придорожные ящики и мелкие РТ без охраны; респаун 7–10 мин", () => Crate(0));
            Sample("crate1", "Ящик РТ тира 1 (малая охрана)", "rt", "РТ тира 1 с малой охраной (гаражи, АТС и т. п.); респаун 15–21 мин", () => Crate(1));
            foreach (var (rk, rn, what) in new[] { ("garages", "Гаражи", "пружины, изолента, труба, резина"), ("depot", "Депо", "шестерни, пружины, металл"), ("quarry", "Карьер", "порох, сера, труба"), ("substation", "Подстанция", "медный провод, платы"), ("ats", "АТС", "платы, линзы, радиодетали") })
                Sample("crate1_" + rk, $"Ящик РТ 1 — {rn}", "rt", $"Ящик на РТ «{rn}»: как обычный тир 1, но чаще ×{World.ProfileBoost}: {what}", () => Crate(1, false, rk));
            Sample("crate2", "Ящик РТ тира 2 (военный, склад)", "rt", "Склад с калашами и Радиоцентр «Маяк» (военные ящики); респаун 20–30 мин", () => Crate(2));
            Sample("safe", "Сейф РТ", "rt", "Сейфы на РТ, открываются картой доступа", () => Crate(2, true));
            Sample("punch_crate", "Крейт перфокарты (РТ тира 1)", "rt", "Главный крейт РТ тира 1 под перфокарту I (гаражи и др.)", () => Punch(false));
            Sample("punch_crate_electronics", "Крейт перфокарты (электроника: АТС, Подстанция)", "rt", "Главный крейт АТС и Подстанции «Тихая»: больше плат, радиодеталей, моторов", () => Punch(true));
            Sample("event_late", "Ивент режиссёра: аирдроп / закрытый ящик (поздняя фаза)", "other", "Сброс с самолёта и закрытый ящик на РТ; фаза 2, после 5 ч вайпа", () => Event(2));
            // тематические ящики (LootBoxes.cs): каждый вид на каждом тире, без профиля РТ
            foreach (var bx in World.BoxKinds)
                for (int bt = 0; bt <= 2; bt++)
                    if (bt >= 1 || bx is not ("army" or "weapon" or "elite"))
                    {
                        int tt = bt; string bb = bx;
                        Sample($"box_{bx}_{bt}", $"{World.BoxName(bx)} · тир {bt}", "box", "", () => Crate(tt, false, null, bb));
                    }
            Late(1, 1.5f * 3600f);
            Sample("event_mid", "Ивент режиссёра: аирдроп / закрытый ящик (средняя фаза)", "other", "Сброс с самолёта и закрытый ящик на РТ; фаза 1", () => Event(1));
            Late(0, 600f);
            Sample("event_early", "Ивент режиссёра: аирдроп / закрытый ящик (ранняя фаза)", "other", "Сброс с самолёта и закрытый ящик на РТ; фаза 0", () => Event(0));
            Late(2, 6 * 3600f);
            Sample("radio_drop", "Борт «Маяка» (вызов с Радиоцентра)", "monument", "Контейнер, который игрок вызывает ракетой на Радиоцентре «Маяк»; площадка РТ 2", () =>
            { var b = new LootBag { Id = 1, IsCrate = true, Tier = 2, Punch = true, Look = "airdrop" }; fillRadio.Invoke(w, new object[] { b }); return b; });

            // бочки: обычная и на РТ
            LootBag Barrel(Vector2 pos)
            {
                var node = new ResourceNode { Id = 1, Kind = NodeKind.Barrel, Pos = pos, Radius = 0.35f, Amount = 1, MaxAmount = 1 };
                int n0 = w.Bags.Count;
                hitBarrel.Invoke(w, new object[] { node, 1e9f, 0 });
                var b = w.Bags.Count > n0 ? w.Bags[^1] : null;
                if (b != null) w.Bags.RemoveRange(n0, w.Bags.Count - n0);
                w.Events.Clear();
                return b;
            }
            Sample("barrel", "Бочка (мир)", "world", "Бочки с хламом по острову; ломаются, респаун 3–5 мин", () => Barrel(new Vector2(1e5f, 1e5f)));
            if (w.Monuments.Count > 0) Sample("barrel_rt", "Бочка на РТ", "rt", "Бочки внутри и у РТ; чуть богаче обычных", () => Barrel(w.Monuments[0].Center));

            // охрана РТ: что падает с трупа
            foreach (var kit in new[] { "club", "bow", "pipe", "pps", "grach", "svt", "akm" })
            {
                string key = "npc_" + kit;
                string label = kit switch { "club" => "Охранник РТ (дубина/копьё)", "bow" => "Охранник РТ (лук)", "pipe" => "Охранник РТ (самопал/наган)", "pps" => "Охранник Радиоцентра (ППС-43)", "grach" => "Охранник Радиоцентра (Грач)", "svt" => "Снайпер Радиоцентра (СВТ-40)", _ => "Охранник склада (АКМ)" };
                string tier = kit is "club" or "bow" or "pipe" ? "РТ тира 1" : kit == "akm" ? "Склад (РТ 2)" : "Радиоцентр (РТ 2)";
                Sample(key, label, "npc", $"Труп охраны: {tier}; ствол падает редко (10%) и только тира 1–2, одежда 25%", () =>
                {
                    var pos = new Vector2(7e4f + loot.Count, 7e4f);
                    var a = new Actor { Id = w.NextId(), Name = "dump", Team = Team.Npc };
                    w.Actors.Add(a); w.NpcHomes[a.Id] = pos; npcKit[pos] = kit;
                    try { respawnNpc.Invoke(w, new object[] { a }); var b = new LootBag { Id = 1, Corpse = true }; npcDrop.Invoke(w, new object[] { a, b }); return b; }
                    finally { w.Actors.Remove(a); w.NpcHomes.Remove(a.Id); npcKit.Remove(pos); }
                });
            }
            w.Time = savedTime; w.Director.Phase = savedPhase;

            // ---- переработка
            var recycle = new Dictionary<string, object>();
            var defsField = typeof(Items).GetField("Defs", All);
            foreach (var id in ((System.Collections.IDictionary)defsField.GetValue(null)).Keys.Cast<string>().ToList())
            {
                var y = Items.RecycleYield(id);
                if (y.Count > 0) recycle[Id(id)] = y.Select(c => new object[] { Id(c.id), c.n }).ToList();
            }

            // ---- добыча
            var gather = new Dictionary<string, object>();
            var tools = new List<string> { "tool_hatchet", "tool_pickaxe", "weapon_spear" };
            tools.AddRange(Prog.Tools.Select(t => t.Id));
            foreach (var tid in tools)
            {
                if (Items.Get(tid)?.Weapon is not WeaponDef wd) continue;
                foreach (var (kind, res) in new[] { (NodeKind.Tree, "wood"), (NodeKind.Stone, "stone"), (NodeKind.Metal, "metal_ore"), (NodeKind.Sulfur, "sulfur_ore") })
                {
                    float g = wd.Gather(kind);
                    if (g > 0) gather[$"{kind.ToString().ToLower()}|{tid}"] = new List<object> { new object[] { Id(res), g } };
                }
                Id(tid);
            }
            gather["metal|bonus"] = new List<object> { new object[] { Id("stone"), "1/3 от добытого за удар (минимум 1)" } };
            gather["sulfur|bonus"] = new List<object> { new object[] { "stone", "1/3 от добытого за удар (минимум 1)" } };
            gather["tree|node"] = new List<object> { new object[] { "wood", "120–200 за дерево" } };
            gather["stone|node"] = new List<object> { new object[] { "stone", 150 } };
            gather["metal|node"] = new List<object> { new object[] { "metal_ore", 90 } };
            gather["sulfur|node"] = new List<object> { new object[] { "sulfur_ore", 60 } };
            gather["junk_pile"] = new List<object> { new object[] { "scrap", "1–2 за кучу (по 1–2 за удар)" }, new object[] { "metal_fragments", "18% шанс 3–7 за удар" } };
            gather["tire"] = new List<object> { new object[] { "rubber", "2–4 за покрышку (топор/копьё — по 2 за удар)" } };
            gather["tire_stack"] = new List<object> { new object[] { "rubber", "6–10 за стопку" } };
            gather["hemp"] = new List<object> { new object[] { "cloth", 10 } };
            gather["berries"] = new List<object> { new object[] { "berries", 3 } };
            gather["water_barrel"] = new List<object> { new object[] { "water_raw", $"налить {Prog.FillFromBarrel} л в ёмкость" } };
            gather["oil_barrel"] = new List<object> { new object[] { "crude", $"{Prog.OilBarrel.min}–{Prog.OilBarrel.max} л нефти в ёмкость, респаун {Prog.OilBarrel.respawn} с" } };
            gather["canister_pile"] = new List<object> { new object[] { "crude", $"{Prog.CanisterPile.min}–{Prog.CanisterPile.max} л нефти в ёмкость, респаун {Prog.CanisterPile.respawn} с" } };
            gather["fuel_pump"] = new List<object> { new object[] { "fuel", $"{Prog.FuelPump.min}–{Prog.FuelPump.max} л топляка за раз (насос Заправки)" } };
            foreach (var k in new[] { "berries", "water_raw", "crude", "fuel", "rubber", "cloth", "scrap", "metal_fragments", "wood", "stone", "metal_ore", "sulfur_ore" }) Id(k);

            // ---- плавка и станки
            var smelt = new List<object>
            {
                new object[] { Id("metal_ore"), Id("metal_fragments"), 1, "Печь (0,4 с, 0,5 дерева за руду)" },
                new object[] { Id("sulfur_ore"), Id("sulfur"), 1, "Печь (0,4 с, 0,5 дерева за руду)" },
                new object[] { Id("wood"), Id("charcoal"), 1, "Печь: побочный уголь 0,5 за каждую плавку (дерево горит)" },
            };
            foreach (var m in Prog.Machines)
                foreach (var (inId, n) in m.In)
                    smelt.Add(new object[] { Id(inId), Id(m.Output), Math.Round(1.0 / n, 4), $"{World.StationName(Array.Find(Prog.Stations, s => s.Machine == m.Machine)?.Kind ?? DeployKind.None)} ({m.Machine}): {n} → 1, {m.Time} с" });

            notes.Add("Легированная сталь (alloy) не добывается и не плавится: только лут (крейты РТ 1–2, сейфы, перфокарты, ивенты/аирдропы) и переработка деталей тира 2–3, дорогого ценного лома и пластин.");
            notes.Add("Лут сэмплирован на поздней фазе вайпа (6 ч, фаза 2): детали тира 3 (ствольная коробка АК и др.) и элита доступны только там; режиссёр ограничивает тир 3 до 5 в час на мир, элиту — раз в 3,5 ч. Для ранних фаз смотри event_mid/event_early.");
            notes.Add("Ствольная коробка АК (comp_receiver_ak) — только лут (≈2/3 всех деталей тира 3).");
            notes.Add("Охрана РТ: ствол выпадает с шансом 10% и только тира 1–2; калаши со склада не выпадают. Игроки/боты теряют всё при смерти (фулл-лут).");
            notes.Add("Переработка крафтовых вещей возвращает половину рецепта (только штучные, Count=1); патроны и порох не перерабатываются. Лутовая одежда — лом по тиру.");
            notes.Add($"Сэмплов на источник: {Samples}. p — доля контейнеров с предметом, avg — среднее количество при наличии (жидкости — штуки ёмкостей).");

            // ---- предметы
            var meta = Weaponry.Info;
            var items = new Dictionary<string, object>();
            foreach (var id in ids.OrderBy(s => s, StringComparer.Ordinal))
            {
                var d = Items.Get(id);
                var o = new Dictionary<string, object> { ["name"] = d?.Name ?? id, ["cat"] = d?.Category.ToString() ?? "?" };
                if (meta.TryGetValue(id, out var mm) && !string.IsNullOrEmpty(mm.Desc)) o["desc"] = mm.Desc;
                else if (Clothing.TryMeta(id, out var cm) && !string.IsNullOrEmpty(cm.Desc)) o["desc"] = cm.Desc;
                var st = new Dictionary<string, object>();   // полные статы (ПКМ в «Паутине»): все ненулевые поля определений
                Stats(st, "", d); Stats(st, "оружие.", d?.Weapon); Stats(st, "одежда.", Clothing.Def(id)); Stats(st, "пластина.", Clothing.Plate(id));
                if (st.Count > 0) o["stats"] = st;
                items[id] = o;
            }

            var root = new Dictionary<string, object>
            {
                ["items"] = items, ["recipes"] = recipes, ["tech"] = tech2, ["loot"] = loot, ["recycle"] = recycle,
                ["gather"] = gather, ["smelt"] = smelt, ["notes"] = notes,
                ["crates"] = w.Bags.Where(b => b.IsCrate).GroupBy(b => (rt: w.MonumentAt(b.Pos)?.Name ?? "вне РТ", b.Tier, box: b.Safe ? "safe" : b.Punch ? "punch" : b.Box ?? "?"))
                    .Select(g => (object)new object[] { g.Key.rt, g.Key.Tier, g.Key.box, g.Count() }).ToList(),
                ["rtboxes"] = World.RtBoxes.ToDictionary(kv => kv.Key, kv => (object)kv.Value.Select(x => new object[] { x.box, x.w }).ToList()),
            };
            return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
    }
}
