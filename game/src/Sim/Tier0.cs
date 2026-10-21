using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Действие «держи E» на РТ: вскрыть бардачок, разбить кассу, выбить дверь. Пока идёт — шумит;
    /// отошёл — сорвалось. Готово — перфокарта I в руки или дверь выбита.
    /// </summary>
    public sealed class Stash
    {
        public Vector2 Pos;
        public string Verb, Sound;      // «Вскрыть бардачок»; звук: pry / glass / bang
        public float Seconds, Noise;
        public bool GivesCard;          // иначе — выбивает дверь Door
        public MapWall Door;
        public int ActorId;             // кто сейчас делает (0 — никто)
        public float Progress, _tick;
        public bool Done;
        /// <summary>Условие (тир 1): null — можно; иначе — что сказать игроку («нужен брелок»).</summary>
        public Func<Actor, string> Require;
        /// <summary>Что сделать по окончании (тир 1); иначе — перфокарта или выбитая дверь.</summary>
        public Action<Actor> OnDone;
        public bool Repeatable;         // после окончания снова доступно (рычаги, ворота, пробки)
        public bool KeepProgress;       // отошёл — прогресс не сбрасывается (кабина крана)
        public float Range = 1.8f;      // насколько можно отойти, не сорвав
    }

    /// <summary>РТ тира 0 (docs/design/11-monuments.md): без охраны, перфокарта I за маленьким локальным действием.</summary>
    public sealed class Tier0Rt
    {
        public const float RefreshTime = 600f;
        public Monument M;
        public Vector2 O;
        public readonly List<Stash> Stashes = new();
        public Pickup Card;             // перфокарта лежит (Сельмаг) — иначе её даёт тайник
        public float RefreshLeft;
        public Vector2 At(float x, float y) => O + new Vector2(x, y);
    }

    public sealed partial class World
    {
        public readonly List<Tier0Rt> Tier0 = new();
        /// <summary>Стены готовых РТ, которые могут исчезать (роллета, выбитая дверь) — клиенту нужны всегда.</summary>
        public readonly List<MapWall> RemovableWalls = new();

        void PlaceTier0()
        {
            foreach (var (key, name, json, seed) in new[]
            {
                ("gai", "Пост ГАИ", Tier0Layouts.Gai, 11),
                ("shop", "Сельмаг «Продукты»", Tier0Layouts.Shop, 23),
                ("fuel", "Заправка «Последняя капля»", Tier0Layouts.Fuel, 37),
            })
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                float w = root.GetProperty("W").GetSingle(), h = root.GetProperty("H").GetSingle(), margin = root.GetProperty("M").GetSingle();
                var rng = new Random(Seed * 7919 + seed);
                var spot = FindPrefabSpot(rng, new Vector2(w, h) + new Vector2(margin * 2), new[] { Biome.Grassland, Biome.Forest, Biome.Arid }, HalfSize * 0.15f, HalfSize * 0.8f, 45);
                if (spot == null) continue;
                var rt = new Tier0Rt { O = spot.Value + new Vector2(margin) };
                rt.M = new Monument { Name = name, Min = rt.O, Max = rt.O + new Vector2(w, h) };
                Monuments.Add(rt.M);
                Prefabs.Add(rt.M);
                ClearPrefabArea(new Monument { Min = rt.M.Min - new Vector2(margin), Max = rt.M.Max + new Vector2(margin) });
                _prefabMode = true;
                BuildFromLayout(rt, key, root, rng);
                _prefabMode = false;
                Tier0.Add(rt);
            }
        }

        static Vector2 V(JsonElement a, int i) => new(a[i].GetSingle(), a[i + 1].GetSingle());

        void BuildFromLayout(Tier0Rt rt, string key, JsonElement root, Random rng)
        {
            Vector2 P(Vector2 v) => rt.O + v;
            // земля: полигоны (сплошная трава вокруг — это уже мир)
            foreach (var g in root.GetProperty("grounds").EnumerateArray())
            {
                string tile = g[0].GetString();
                if (tile == "ground_grass") continue;
                var pts = new List<Vector2>();
                foreach (var p in g[1].EnumerateArray()) pts.Add(P(new Vector2(p[0].GetSingle(), p[1].GetSingle())));
                Patches.Add(new MapPatch { Tile = "tiles/" + tile, Poly = pts.ToArray(), Min = Vector2.Zero, Max = Vector2.Zero });
            }
            foreach (var d in root.GetProperty("decals").EnumerateArray())
                Decors.Add(new MapDecor { Id = Decors.Count, Sprite = d[0].GetString(), Pos = P(V(d, 1)), Flat = true, Scale = d[3].GetSingle(), Rot = d[4].GetSingle() });
            var doors = new List<MapWall>();
            foreach (var wl in root.GetProperty("walls").EnumerateArray())
            {
                var a = P(V(wl, 0)); var b = P(V(wl, 2));
                AddWall(a.X, a.Y, b.X, b.Y, wl[4].GetSingle(), WallKind.Concrete);
                var mw = Walls[^1];
                mw.Tex = "walls/" + wl[5].GetString();
                if (wl[5].GetString() == "wallface_door_wood") doors.Add(mw);
            }
            foreach (var r in root.GetProperty("roofs").EnumerateArray())
                Roof("tiles/" + r[5].GetString(), P(V(r, 0)), P(V(r, 2)), rng);

            Vector2? carPos = null;
            foreach (var pr in root.GetProperty("props").EnumerateArray())
            {
                string sprite = pr[0].GetString();
                var p = P(V(pr, 1));
                bool flip = pr[3].GetBoolean();
                bool planned = pr[4].ValueKind == JsonValueKind.Array;
                string id = sprite[(sprite.LastIndexOf('/') + 1)..];
                if (id == "recycler") { AddRecycler(p); continue; }
                if (planned && id.StartsWith("crate_")) { AddCrate(p, 0); continue; }
                if (planned && id == "junk_pile") { AddScrapSpot(NodeKind.Junk, p); continue; }
                if (planned && id is "barrel_oil" or "barrel_water") { AddScrapSpot(NodeKind.Barrel, p); continue; }
                if (id == "car_police_2101") carPos = p;
                if (sprite.StartsWith("grass/") || sprite.StartsWith("plants/") || sprite.StartsWith("clutter/")) { Decor(sprite, p, flip); continue; }
                Prop(sprite, p, flip);
            }

            // механика перфокарты — по метке на макете
            foreach (var mk in root.GetProperty("marks").EnumerateArray())
            {
                string label = mk[2].GetString();
                if (!label.StartsWith("ПЕРФОКАРТА")) continue;
                var p = P(V(mk, 0));
                switch (key)
                {
                    case "gai":
                        rt.Stashes.Add(new Stash { Pos = (carPos ?? p) + new Vector2(0, 0.6f), Verb = "Вскрыть бардачок «копейки»", Sound = "pry", Seconds = 4, Noise = 25, GivesCard = true });
                        break;
                    case "fuel":
                        rt.Stashes.Add(new Stash { Pos = p + new Vector2(0, 1.4f), Verb = "Разбить кассу", Sound = "glass", Seconds = 3, Noise = 30, GivesCard = true });
                        break;
                    case "shop":
                        AddPickup(PickupKind.PunchCard, p);
                        rt.Card = Pickups[^1];
                        foreach (var d in doors)
                        {
                            RemovableWalls.Add(d);
                            var c = (d.Wall.Min + d.Wall.Max) * 0.5f;
                            rt.Stashes.Add(new Stash { Pos = c + new Vector2(0, 0.7f), Verb = "Выбить заклинившую дверь", Sound = "bang", Seconds = 4, Noise = 40, Door = d });
                        }
                        break;
                }
            }
        }

        public Tier0Rt Tier0At(Vector2 p) => Tier0.Find(t => t.M.Contains(p));

        void UseStash(Actor a, Stash s)
        {
            if (s.Done) { Info(a, s.GivesCard ? "Уже вскрыто — пусто" : s.Door != null ? "Дверь уже выбита" : "Уже сделано"); return; }
            if (s.ActorId != 0 && s.ActorId != a.Id) { Info(a, "Кто-то уже этим занят"); return; }
            if (s.ActorId == a.Id) return;
            var why = s.Require?.Invoke(a);
            if (why != null) { Info(a, why); return; }
            s.ActorId = a.Id;
            if (!s.KeepProgress) s.Progress = 0;
            Info(a, $"{s.Verb}… не отходи ({s.Seconds:0} с, шумно)");
        }

        /// <summary>Каждый тик: «держи E» на тайниках, обновление РТ тира 0.</summary>
        void StepTier0()
        {
            foreach (var rt in Tier0)
            {
                foreach (var s in rt.Stashes)
                {
                    var a = StepStash(s);
                    if (a == null) continue;
                    rt.RefreshLeft = Tier0Rt.RefreshTime;
                    if (s.GivesCard)
                    {
                        a.Add("punchcard_1", 1);
                        Events.Add(new SimEvent { Kind = SimEventKind.Gather, ActorId = a.Id, Pos = s.Pos, Value = 1, Text = "Перфокарта I" });
                        Info(a, "Перфокарта I у тебя — ей открывается крейт Подстанции");
                    }
                    else if (s.Door != null)
                    {
                        if (Walls.Remove(s.Door)) _wallGrid.Remove(s.Door);
                        PrefabVersion++;
                        Info(a, "Дверь выбита");
                    }
                }
                if (rt.Card != null && !rt.Card.Available && rt.RefreshLeft <= 0) rt.RefreshLeft = Tier0Rt.RefreshTime;
                if (rt.RefreshLeft > 0 && (rt.RefreshLeft -= Dt) <= 0)
                {
                    if (Actors.Exists(a => !a.Dead && a.Team == Team.Player && rt.M.Contains(a.Pos, 4))) { rt.RefreshLeft = 60; continue; }
                    ResetTier0(rt);
                }
            }
        }

        /// <summary>Шаг «держи E»: шум, прогресс, срыв. Возвращает актёра, если действие только что закончено.</summary>
        Actor StepStash(Stash s)
        {
            if (s.ActorId == 0) return null;
            var a = Find(s.ActorId);
            if (a == null || a.Dead || Vector2.Distance(a.Pos, s.Pos) > s.Range)
            {
                if (a != null && !a.Dead) Info(a, s.KeepProgress ? "Отошёл — дело стоит, вернись и продолжи" : "Сорвалось — отошёл");
                s.ActorId = 0;
                return null;
            }
            s.Progress += Dt;
            if ((s._tick -= Dt) <= 0)
            {
                s._tick = 0.8f;
                Noises.Add(new Noise { Pos = s.Pos, Radius = s.Noise, SourceId = a.Id, Time = Time });
                Events.Add(new SimEvent { Kind = SimEventKind.Machine, ActorId = a.Id, Pos = s.Pos, Text = s.Sound });
            }
            if (s.Progress < s.Seconds) return null;
            s.ActorId = 0;
            s.Progress = 0;
            if (!s.Repeatable) s.Done = true;
            s.OnDone?.Invoke(a);
            return s.OnDone == null ? a : null;
        }

        /// <summary>Все «держи E» готовых РТ (тир 0 и тир 1).</summary>
        public IEnumerable<Stash> AllStashes()
        {
            foreach (var rt in Tier0) foreach (var s in rt.Stashes) yield return s;
            foreach (var rt in Tier1) foreach (var s in rt.Stashes) yield return s;
            foreach (var s in DirectorStashes) yield return s;
        }

        void ResetTier0(Tier0Rt rt)
        {
            foreach (var s in rt.Stashes)
            {
                s.Done = false;
                s.ActorId = 0;
                if (s.Door != null && !Walls.Contains(s.Door)) { Walls.Add(s.Door); _wallGrid.Insert(s.Door, s.Door.Wall.Min, s.Door.Wall.Max); }
            }
            if (rt.Card != null) rt.Card.RespawnLeft = 0;
            PrefabVersion++;
        }
    }
}
