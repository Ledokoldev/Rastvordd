using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    public struct ActorSnap
    {
        public int Id;
        public Vector2 Pos;
        public float Facing;
        public float RecoilDeg;      // увод ствола — клиент поворачивает оружие в руках
        public float Peek;           // прицеливание (ПКМ) — оружие у головы
        public bool Dead;
        public bool IsNpc;
        public string HeldId;
        public bool Reloading;
        public bool Sprinting;
        public bool Crouching;
        public bool Swimming;
        public string Label;         // подпись над головой (боты: имя и занятие)
    }

    public struct ProjSnap { public int Id; public Vector2 Pos; public Vector2 Dir; public bool Arrow; }
    public struct BagSnap { public int Id; public Vector2 Pos; public bool IsCrate; public bool Empty; public bool Safe; public bool Punch; public bool Unlocked; public int Tier; }
    public struct NodeSnap { public int Id; public NodeKind Kind; public Vector2 Pos; public float Radius; public float Fill; }
    public struct PickupSnap { public int Id; public PickupKind Kind; public Vector2 Pos; public bool Available; public string Item; }
    public struct DeploySnap { public int Id; public DeployKind Kind; public Vector2 Pos; public bool Burning; public int Level; public bool Seen; }

    /// <summary>Узел дерева изучения для окна верстака.</summary>
    public struct TechInfo
    {
        public string Id, Name;
        public int Bench, Scrap;
        public string[] Requires;
        public bool Known, CanResearch;
        public string Why;          // почему нельзя изучить сейчас
    }
    public struct FireSnap { public int Id; public Vector2 Pos; public float Radius, Left; }
    public struct ThrownSnap { public int Id; public string ItemId; public Vector2 Pos; public float Z; public float Fuse; }
    public struct StructSnap
    {
        public int Id; public StructKind Kind; public int I, J, Dir, Tier; public float Hp; public bool Open; public bool Locked;
        public bool Seen;                // видна сейчас, иначе запомнена
        public Vector2 A, B, C;          // ребро A–B; треугольник A, B, C
        public Vector2 Center => Building.Center(Kind, I, J, A, B, C);
        public Vector2[] Poly => Kind == StructKind.Foundation ? Building.SquarePoly(I, J) : new[] { A, B, C };
    }

    public struct BeltSlotInfo
    {
        public string Id;
        public string Name;
        public int Count;
        public bool IsGun;          // огнестрел или лук — показываем патроны
        public int Ammo, Magazine;
        public bool IsDeployable;
    }

    public struct RecipeInfo
    {
        public int Index;
        public string Output;      // id предмета (для иконки)
        public string Name;
        public string CostText;
        public bool CanCraft;
        public bool NeedsBench;
        public int Bench;          // уровень верстака
        public bool Known;         // изучен ли рецепт
        public float Time;
        public int Count;                                     // сколько штук за раз
        public List<(string id, int n, int have)> Costs;      // ингредиенты: нужно / есть
    }

    public sealed class SelfInfo
    {
        public int Id;
        public bool Dead;
        public float Hp, MaxHp, Food, Water;
        public string WeaponName;
        public bool HoldingGun;
        public int Ammo, Magazine, Reserve;
        public float ReloadProgress;    // 0..1, 0 — не перезаряжается
        public float SpreadDeg;
        public int Bandages;
        public string InteractHint;     // пусто — рядом ничего нет
        public bool HeldDeploy;
        public bool NearBench;
        public bool HoldingPlan;        // план постройки: призрак элемента под курсором
        public StructKind BuildPiece;    // выбранный элемент
        public StructKind GhostPiece;    // что поставится под курсором (на фундаменте — стена, на проёме — дверь)
        public Vector2[] GhostPoly;      // призрак пола (квадрат или треугольник)
        public Vector2 GhostA, GhostB;   // призрак ребра
        public bool BuildOk;
        public string Place;            // РТ, на котором стоишь
        public Vector2? Home;           // твой межевой столб (для карты)
        public bool HasBag;             // есть свой спальник
        public float BagReadyIn;        // секунд до готовности спальника
        public string BuildStatus;      // с планом в руках: лимиты базы под курсором
        public UiView Ui;               // открытое окно (ящик, печь, столб…)
        public string BuildGuide;       // шпаргалка «как построить дом» с отметками
        public List<(int kind, string name, string cost, bool can)> BuildPieces = new();   // меню плана
        public bool OpenCraft;          // открыть крафт (E у верстака)
        public bool HasZone;            // зона столба под курсором (клетка столба, радиус World.ClaimRadius)
        public (int i, int j) ZoneCell;
        public BeltSlotInfo?[] Belt = new BeltSlotInfo?[Actor.BeltSize];
        public int Current;
        public List<CellView> Pack = new();          // рюкзак 24 ячейки
        public List<CellView> BeltCells = new();     // пояс 6 ячеек
        public List<(string id, string name, float progress)> CraftQueue = new();
        public List<RecipeInfo> Recipes = new();
        public List<TechInfo> Tech = new();
        public int BenchLevel;          // уровень верстака рядом (0 — нет)
        public int Scrap;
    }

    /// <summary>То, что сервер отправляет клиенту за тик. Только видимое этому клиенту.</summary>
    public sealed class Snapshot
    {
        public uint Tick;
        public float Time;
        public float DayTime;      // 0..1, см. World.TimeOfDay
        public SelfInfo Self;
        public List<ActorSnap> Actors = new();
        public List<ProjSnap> Projectiles = new();
        public List<BagSnap> Bags = new();
        public List<NodeSnap> Nodes = new();
        public List<PickupSnap> Pickups = new();
        public List<DeploySnap> Deployables = new();
        public List<StructSnap> Structures = new();
        public List<ThrownSnap> Throwns = new();
        public List<FireSnap> Fires = new();
        public int StructVersion;         // меняется при любой стройке/поломке/двери
        public int PrefabVersion;         // состояние готовых РТ: роллеты, щитки
        public List<int> GoneWalls = new();
        public List<(int id, string sprite)> DecorSprites = new();
        public List<(int id, Vector2 pos, float rot)> DecorMoves = new();   // вагон, стрела крана, контейнер
        public List<DirectorMarker> Markers = new();   // ивенты режиссёра — видны всей карте
        public List<(int n, string text)> Radio = new();   // последние слухи по радио
        public List<(Vector2 pos, float heat)> Hot = new();   // тепловая карта — только наблюдателю
        public int Phase;
        public List<SimEvent> Events = new();
    }

    public struct DirectorMarker { public int Id; public string Kind, Name; public Vector2 Pos, PlaneFrom, PlaneTo; public float StartsAt, EndsAt; public bool Open, Started; }

    public struct WallInfo { public Vector2 Min, Max; public float Height; public WallKind Kind; public int Tier; public bool Door; public bool Locked; public int StructId; public int Id; public string Tex; public bool Hidden; }

    public sealed class MapInfo
    {
        public float HalfSize;
        public List<WallInfo> Walls = new();
        public List<(Vector2 min, Vector2 max)> Yards = new();   // площадки РТ: бетон под ногами
        public List<string> YardNames = new();
        public List<bool> YardPrefab = new();   // готовая РТ: земля — своими участками, без бетонной заливки
        public List<MapDecor> Decors = new();
        public List<MapPatch> Patches = new();
        public List<MapRoof> Roofs = new();
        public List<MapWire> Wires = new();
        // рельеф и биомы (сетка N×N, клетка Cell метров) — отправляются один раз при подключении
        public int N;
        public float Cell;
        public Biome[] Biomes;
        public float[] Heights;
    }

    /// <summary>
    /// Локальный сервер. Сейчас живёт в одном процессе с клиентом; позже тот же класс
    /// будет работать за сетевым транспортом. Клиент общается с ним только через
    /// InputCmd → Submit и Snapshot ← BuildSnapshot.
    /// </summary>
    public sealed class LocalServer
    {
        const float WorldViewRadius = 70f;   // ресурсы, пикапы и постройки видны без учёта конуса
        const float EventRadius = 80f;

        public readonly World World;
        readonly Dictionary<int, InputCmd> _pending = new();
        readonly List<IBrain> _brains = new();
        readonly Dictionary<int, PlayerBot> _bots = new();
        bool _botsStarted;
        readonly int _seed;
        /// <summary>Сколько ботов-игроков живёт на острове (позже — от размера карты и настроек вайпа).</summary>
        public int BotCount = BotsFromArgs();   // по умолчанию 3: 2 фермера + бродяга; песочница: --bots=N
        static readonly string[] BotNames =
        {
            "Сёма", "Жека", "Толян", "Вован", "Лёха", "Димон", "Серёга", "Колян", "Витёк", "Саня", "Петрович", "Гоша", "Миха", "Стас",
            "Валера", "Гена", "Борян", "Эдик", "Юрец", "Костян", "Тёма", "Рома", "Славик", "Игорёк", "Макс", "Паша", "Дэн", "Женёк",
            "Вадик", "Лёва", "Шурик", "Федя", "Кирюха", "Антоха", "Ванёк", "Гриша", "Егор", "Ильич", "Тимоха", "Михалыч",
        };

        static int BotsFromArgs()
        {
            var a = System.Array.Find(System.Environment.GetCommandLineArgs(), x => x.StartsWith("--bots="));
            return a != null && int.TryParse(a.Substring(7), out int n) ? System.Math.Clamp(n, 1, BotNames.Length) : 3;
        }

        /// <summary>Строка для таблицы песочницы: кто впереди в гонке (верстак, изучено, лом в ящике).</summary>
        public List<(int id, int score, string line)> BotShortLines()
        {
            var list = new List<(int, int, string)>();
            foreach (var b in _bots.Values)
            {
                var a = b.Me;
                int bench = 0;
                foreach (var d in World.Deployables) if (d.Kind == DeployKind.Workbench && d.OwnerId == a.Id) bench = System.Math.Max(bench, System.Math.Max(1, d.Level));
                string arch = b.Archetype == BotArchetype.Roamer ? "бродяга" : "фермер";
                int score = bench * 1000 + a.Known.Count * 50 + (int)(b.Stash / 10);
                list.Add((a.Id, score, $"{a.Name,-18} {arch,-8} верстак {bench} · изуч. {a.Known.Count,2} · ящик ≈{b.Stash,5:0} · {(a.Dead ? "мёртв" : b.GoalName)}"));
            }
            list.Sort((x, y) => y.Item2.CompareTo(x.Item2));
            return list;
        }
        readonly Dictionary<int, InputCmd> _inputs = new();
        readonly List<SimEvent> _eventsThisTick = new();

        public LocalServer(int seed)
        {
            World = new World(seed) { Seed = seed };
            _seed = seed;
            int i = 0;
            foreach (var npc in World.SpawnNpcs())
                _brains.Add(new NpcBrain(npc, World, seed * 31 + i++));
        }

        public MapInfo GetMap()
        {
            var t = World.Terrain;
            var m = new MapInfo { HalfSize = World.HalfSize, N = t.N, Cell = TerrainMap.Cell, Biomes = t.Biomes, Heights = t.Height };
            foreach (var mon in World.Monuments) { m.Yards.Add((mon.Min, mon.Max)); m.YardNames.Add(mon.Name); m.YardPrefab.Add(World.Prefabs.Contains(mon)); }
            foreach (var w in World.Walls)
                m.Walls.Add(new WallInfo { Min = w.Wall.Min, Max = w.Wall.Max, Height = w.Wall.Height, Kind = w.Kind, Id = w.Id, Tex = w.Tex, Hidden = w.Hidden });
            foreach (var w in World.RemovableWalls)
                if (!World.Walls.Contains(w))   // поднятая роллета, выбитая дверь — тоже нужны клиенту, чтобы вернуть их при обновлении РТ
                    m.Walls.Add(new WallInfo { Min = w.Wall.Min, Max = w.Wall.Max, Height = w.Wall.Height, Kind = w.Kind, Id = w.Id, Tex = w.Tex });
            foreach (var d in World.Decors) m.Decors.Add(new MapDecor { Id = d.Id, Sprite = d.Sprite, Pos = d.Pos, Flip = d.Flip, Flat = d.Flat, Scale = d.Scale, Rot = d.Rot, Pivot = d.Pivot, Lift = d.Lift });
            m.Patches.AddRange(World.Patches);
            m.Roofs.AddRange(World.Roofs);
            m.Wires.AddRange(World.Wires);
            return m;
        }

        /// <summary>Подключение: если в сохранении есть персонаж с этим именем — продолжаем им.</summary>
        public int Connect(string name) => (World.FindPlayer(name) ?? World.SpawnPlayer(name)).Id;

        /// <summary>Песочница: смотреть на ботов без своего персонажа.</summary>
        public int ConnectSpectator() => World.SpawnSpectator().Id;

        /// <summary>Строки панели наблюдателя: боты, их дела и прогресс.</summary>
        public List<(int id, string line)> BotLines()
        {
            var list = new List<(int, string)>();
            foreach (var b in _bots.Values)
            {
                var a = b.Me;
                int bench = 0;
                foreach (var d in World.Deployables) if (d.Kind == DeployKind.Workbench && d.OwnerId == a.Id) bench = System.Math.Max(bench, System.Math.Max(1, d.Level));
                string arch = b.Archetype == BotArchetype.Roamer ? "бродяга" : "фермер";
                list.Add((a.Id, $"{a.Name} · {arch} · цель: {b.GoalName} · {(a.Dead ? "мёртв" : b.Doing)}\n   мысль: {b.LastThought}\n   HP {a.Hp:0} · {(b.PlanInfo != "" ? b.PlanInfo + " · " : "")}верстак {bench} · изучено {a.Known.Count}/{Tech.All.Count} · лом {a.Count("scrap")} · металл {a.Count("metal_fragments")} · груз ≈{b.Load:0} · ящик ≈{b.Stash:0} · построек {World.Structures.FindAll(s => s.OwnerId == a.Id).Count}"));
            }
            return list;
        }

        /// <summary>Загрузить сохранение (до Connect). false — файл от другого зерна/версии.</summary>
        public bool LoadSave(string json, out string report)
        {
            report = null;
            var save = WorldSave.FromJson(json);
            if (save == null || save.Version != 3 || save.Seed != World.Seed) return false;
            World.Load(save);
            // мир жил, пока игра была закрыта (не больше суток за раз)
            float away = (float)System.Math.Min(24 * 3600, (System.DateTime.UtcNow - save.SavedAtUtc).TotalSeconds);
            var lines = World.CatchUp(away);
            if (away >= 60)
                report = $"Тебя не было {(away >= 3600 ? $"{away / 3600:0.0} ч" : $"{away / 60:0} мин")}" + (lines.Count > 0 ? ": " + string.Join("; ", lines) : "");
            return true;
        }

        public string SaveJson() => World.Save().ToJson();

        /// <summary>Боты-игроки: после загрузки продолжают своими персонажами (по имени), иначе — новые с пляжа.</summary>
        void StartBots()
        {
            _botsStarted = true;
            for (int i = 0; i < BotCount && i < BotNames.Length; i++)
            {
                string name = BotNames[i] + " (бот)";
                var a = World.FindPlayer(name) ?? World.SpawnPlayer(name);
                a.IsBot = true;
                var bot = new PlayerBot(a, World, _seed * 17 + i) { Archetype = i % 3 == 2 ? BotArchetype.Roamer : BotArchetype.Farmer };
                _bots[a.Id] = bot;
                _brains.Add(bot);
            }
        }

        /// <summary>Отладка: прокрутить мир на seconds без клиента, печатая занятия ботов.</summary>
        /// <summary>Отладка рейдов: в эту секунду мира первый бот получает бомбы и патроны (−1 — нет).</summary>
        public float RaidKitAt = -1;

        public void FastForward(float seconds, System.Action<string> log, int focusId = -1)
        {
            int ticks = (int)(seconds / World.Dt);
            foreach (var tier in new[] { 0, 1, 2 })
            {
                var crates = World.Bags.FindAll(b => b.IsCrate && b.Tier == tier);
                log($"ящики уровня {tier}: {crates.Count} · пример: {(crates.Count > 0 ? string.Join(", ", crates[0].Items.ConvertAll(s => $"{Items.Name(s.Id)} ×{s.Count}")) : "-")}");
            }
            int shown = World.Milestones.Count;
            var thoughtsShown = new Dictionary<int, int>();
            bool basedump = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--basedump") >= 0;
            var killAt = new List<float>();
            var killArg = System.Array.Find(System.Environment.GetCommandLineArgs(), a => a.StartsWith("--killbot="));
            if (killArg != null) foreach (var k in killArg.Substring(10).Split(',')) killAt.Add(float.Parse(k, System.Globalization.CultureInfo.InvariantCulture));
            // отладка походов на РТ: --rtkit=секунда — фермеры получают самопал, 60 патронов, 3 бинта и перфокарту I
            var rtArg = System.Array.Find(System.Environment.GetCommandLineArgs(), a => a.StartsWith("--rtkit="));
            float rtKitAt = rtArg != null ? float.Parse(rtArg.Substring(8), System.Globalization.CultureInfo.InvariantCulture) : -1;
            var duelArg = System.Array.Find(System.Environment.GetCommandLineArgs(), a => a.StartsWith("--duel="));
            if (duelArg != null)
            {
                var k = duelArg.Substring(7).Split(':');
                _duel = new DuelState { KitA = DuelGun(k[0]), KitB = DuelGun(k.Length > 1 ? k[1] : k[0]) };
            }
            for (int t = 0; t < ticks; t++)
            {
                if (focusId >= 0) _pending[focusId] = InputCmd.Empty;
                Tick();
                while (shown < World.Milestones.Count) log("ВЕХА " + World.Milestones[shown++]);
                foreach (var b in _bots.Values)
                {
                    // журнал мыслей: решения директора с причинами
                    int fresh = b.ThoughtTotal - thoughtsShown.GetValueOrDefault(b.Me.Id);
                    for (int k = System.Math.Max(0, b.Thoughts.Count - fresh); k < b.Thoughts.Count; k++) log($"МЫСЛЬ {b.Me.Name}: {b.Thoughts[k]}");
                    thoughtsShown[b.Me.Id] = b.ThoughtTotal;
                }
                if (_duel != null) DuelStep(log);
                // отладка замков: --killbot=секунда[,ещё] — первый фермер умирает (ключи остаются на трупе)
                if (killAt.Count > 0 && World.Time >= killAt[0])
                {
                    killAt.RemoveAt(0);
                    foreach (var b in _bots.Values)
                    {
                        if (b.Archetype == BotArchetype.Roamer || b.Me.Dead) continue;
                        log($"[{World.Time,6:0}s] отладка: убит {b.Me.Name} @({b.Me.Pos.X:0.0};{b.Me.Pos.Y:0.0})");
                        var keys = new List<string>();
                        foreach (var it in b.Me.Pack) if (it != null && it.Id.StartsWith("key_")) keys.Add(it.Id);
                        foreach (var it in b.Me.Belt) if (it != null && it.Id.StartsWith("key_")) keys.Add(it.Id);
                        World.Damage(b.Me, 1000, -1, b.Me.Pos);
                        log($"[{World.Time,6:0}s] отладка: ключи на трупе: {(keys.Count > 0 ? string.Join(", ", keys) : "нет")}");
                        break;
                    }
                }
                if (rtKitAt >= 0 && World.Time >= rtKitAt)
                {
                    rtKitAt = -1;
                    foreach (var b in _bots.Values)
                    {
                        if (b.Archetype == BotArchetype.Roamer || b.Me.Dead) continue;
                        if (b.Me.Count("weapon_pipe") == 0 && !System.Array.Exists(b.Me.Belt, s => s?.Id == "weapon_pipe")) b.Me.AddSlot(Items.NewSlot("weapon_pipe", 10));
                        b.Me.Add("ammo_pistol", 60); b.Me.Add("bandage", 3); b.Me.Add("punchcard_1", 1);
                        log($"[{World.Time,6:0}s] отладка: {b.Me.Name} получил самопал, 60 патронов, 3 бинта, перфокарту I");
                    }
                }
                if (RaidKitAt >= 0 && World.Time >= RaidKitAt)
                {
                    RaidKitAt = -1;
                    // --raidkitbot=N — кому выдать (по порядку ботов: 0 Сёма, 1 Жека, 2 Толян); по умолчанию первому
                    var rkb = System.Array.Find(System.Environment.GetCommandLineArgs(), a => a.StartsWith("--raidkitbot="));
                    int skip = rkb != null ? int.Parse(rkb.Substring(13)) : 0;
                    foreach (var b in _bots.Values)
                    {
                        if (skip-- > 0) continue;
                        b.Me.Add("expl_satchel", 14); b.Me.Add("ammo_pistol", 60);
                        log($"[{World.Time,6:0}s] отладка: {b.Me.Name} получил 14 бомб и 60 патронов");
                        break;
                    }
                }
                if (t > 0 && t % (int)(60 / World.Dt) == 0) { log($"[{World.Time,6:0}s] {SimPerf.Report()}"); SimPerf.Reset(); }
                // здоровье мозга: доля времени в бегстве и в бою/охоте за 10 мин (> 30 % — тревога)
                if (t > 0 && t % (int)(600 / World.Dt) == 0)
                    foreach (var b in _bots.Values) log($"[{World.Time,6:0}s] ЗДОРОВЬЕ {b.Me.Name}: {b.HealthReport()}");
                foreach (var e in World.Events)
                    if (e.Kind == SimEventKind.Death)
                        log($"[{World.Time,6:0}s] смерть: {World.Find(e.ActorId)?.Name} ← {World.Find(e.OtherId)?.Name ?? "?"}");
                if (basedump && t % (int)(30 / World.Dt) == 0)
                    foreach (var b in _bots.Values)
                    {
                        var mine = World.Structures.FindAll(s => s.OwnerId == b.Me.Id && s.Kind == StructKind.Door);
                        if (mine.Count > 0) log($"[{World.Time,6:0}s] ДВЕРИ {b.Me.Name}: открыто {mine.FindAll(s => s.Open).Count} из {mine.Count}, с замком {mine.FindAll(s => s.Locked).Count}");
                    }
                if (t % (int)(30 / World.Dt) == 0)
                    foreach (var b in _bots.Values)
                        log($"[{World.Time,6:0}s] {b.Me.Name} @({b.Me.Pos.X:0.0};{b.Me.Pos.Y:0.0}) {b.Debug}: {b.Doing} · HP {b.Me.Hp:0} · груз {b.Load:0} ящик {b.Stash:0} · дерево {b.Me.Count("wood")} камень {b.Me.Count("stone")} металл {b.Me.Count("metal_fragments")} лом {b.Me.Count("scrap")} · изучено {b.Me.Known.Count} · E→{World.NearestInteract(b.Me).kind} · построек {World.Structures.FindAll(s => s.OwnerId == b.Me.Id).Count} · {World.LastInfo.GetValueOrDefault(b.Me.Id)}");
            }

            if (basedump) BaseDump(log);
            // итог боёв: укрытия, пики, перезарядки, отходы (счётчики PlayerBot.CombatStats)
            if (_duel != null) log($"ДУЭЛЬ итог: {_duel.KitA} {_duel.WinA} — {_duel.KitB} {_duel.WinB}, ничьих {_duel.Draw}, раундов {_duel.Round}");
            float tf = 0, th = 0, ta = 0;
            foreach (var b in _bots.Values) { tf += b.TotFlee; th += b.TotFight; ta += b.TotAll; }
            if (ta > 0) log($"ЗДОРОВЬЕ итог: в бегстве {tf / ta:P1}, в бою/охоте {th / ta:P1} времени живых ботов");
            foreach (var b in _bots.Values)
            {
                var parts = new List<string>();
                foreach (var kv in b.CombatStats) parts.Add($"{kv.Key} {kv.Value}");
                log($"БОЙ-СТАТ {b.Me.Name}: {(parts.Count > 0 ? string.Join(", ", parts) : "боёв не было")}");
                var lp = new List<string>();
                foreach (var kv in b.LootStats) lp.Add($"{kv.Key} {kv.Value}");
                log($"ЛУТ-СТАТ {b.Me.Name}: ящик ≈{b.Stash:0} лома, при себе ≈{b.Load:0}; {(lp.Count > 0 ? string.Join(", ", lp) : "-")}");
            }
        }

        /// <summary>
        /// Отладка баз (--basedump): у каждого бота — проёмы, двери (с замком или без) и установленное:
        /// где стоит и на своём ли полу («внутри»/«СНАРУЖИ»), ящики — сколько внутри предметов.
        /// </summary>
        void BaseDump(System.Action<string> log)
        {
            foreach (var b in _bots.Values)
            {
                var a = b.Me;
                int doorways = 0, doors = 0, locked = 0, floors = 0, walls = 0;
                foreach (var s in World.Structures)
                {
                    if (s.OwnerId != a.Id) continue;
                    switch (s.Kind)
                    {
                        case StructKind.Doorway: doorways++; break;
                        case StructKind.Door: doors++; if (s.Locked) locked++; log($"БАЗА {a.Name}: дверь @({s.Center.X:0.0};{s.Center.Y:0.0}) {Building.TierNames[s.Tier]}, {(s.Locked ? (s.KeyLock ? "замок с ключом" : "кодовый замок") : "БЕЗ ЗАМКА")}{(s.Open ? ", ОТКРЫТА" : ", закрыта")}"); break;
                        case StructKind.Foundation or StructKind.TriFoundation: floors++; break;
                        default: walls++; break;
                    }
                }
                log($"БАЗА {a.Name}: {b.PlanInfo} · полов {floors}, стен/окон {walls}, проёмов {doorways}, дверей {doors}, с замком {locked}");
                foreach (var d in World.Deployables)
                {
                    if (d.OwnerId != a.Id) continue;
                    bool inside = World.FloorAt(d.Pos) is Structure f && f.OwnerId == a.Id;
                    string extra = d.Storage != null ? $", предметов {d.Storage.Items.Count}" : "";
                    log($"БАЗА {a.Name}: {d.Kind} ур.{d.Level} @({d.Pos.X:0.0};{d.Pos.Y:0.0}) {(inside ? "внутри" : "СНАРУЖИ")}{extra}");
                }
            }
        }

        // ---------- отладка боя: --duel=ak:pipe ----------

        /// <summary>
        /// Дуэль ботов (этап 3 мозга): первый бот (фермер) и третий (бродяга) получают оружие по --duel=ak:pipe
        /// (ak, pipe, bow, spear) и встают в 18 м друг от друга на поле с камнями и деревьями, подальше от РТ и баз.
        /// Раунд — до смерти или 100 с, затем новое поле. Сохранение игрока не трогает (обычный отладочный прогон).
        /// </summary>
        sealed class DuelState { public string KitA, KitB; public int Round, WinA, WinB, Draw; public float NextAt = 20, EndAt = -1, StartAt; }
        DuelState _duel;
        readonly System.Random _duelRng = new(7);
        readonly List<ResourceNode> _duelNodes = new();

        static string DuelGun(string k) => k switch { "ak" => "weapon_ak", "pipe" => "weapon_pipe", "bow" => "weapon_bow", "spear" => "weapon_spear", _ => k };

        void DuelStep(System.Action<string> log)
        {
            var d = _duel;
            PlayerBot a = null, b = null;
            int i = 0;
            foreach (var bot in _bots.Values) { if (i == 0) a = bot; if (i == 2) b = bot; i++; }
            if (a == null || b == null) return;
            if (d.EndAt > 0)
            {
                if (!a.Me.Dead && !b.Me.Dead && World.Time < d.EndAt) return;
                string res;
                if (a.Me.Dead && b.Me.Dead) { res = "оба мертвы"; d.Draw++; }
                else if (a.Me.Dead) { res = $"победил {b.Me.Name} ({Items.Name(d.KitB)}, осталось HP {b.Me.Hp:0})"; d.WinB++; }
                else if (b.Me.Dead) { res = $"победил {a.Me.Name} ({Items.Name(d.KitA)}, осталось HP {a.Me.Hp:0})"; d.WinA++; }
                else { res = $"ничья по времени (HP {a.Me.Hp:0} / {b.Me.Hp:0})"; d.Draw++; }
                log($"ДУЭЛЬ раунд {d.Round}: {res} за {World.Time - d.StartAt:0} с");
                d.EndAt = -1; d.NextAt = World.Time + 8;
                return;
            }
            if (World.Time < d.NextAt) return;
            // поле: суша, не у РТ и не у баз, вокруг камни и деревья
            Vector2 c = default, pa = default, pb = default;
            int cover = 0;
            for (int k = 0; k < 600; k++)
            {
                c = new Vector2((float)(_duelRng.NextDouble() * 2 - 1) * 380, (float)(_duelRng.NextDouble() * 2 - 1) * 380);
                if (!World.Terrain.IsLand(c) || World.NearMonument(c, 90)) continue;
                bool nearBase = false;
                foreach (var dp in World.Deployables) if (dp.Kind == DeployKind.Claim && Vector2.Distance(dp.Pos, c) < 70) nearBase = true;
                if (nearBase) continue;
                cover = World.NodesInRadius(c, 16, _duelNodes).Count;
                if (cover < 4 || cover > 30) continue;
                float ang = (float)(_duelRng.NextDouble() * System.Math.PI * 2);
                var dir = new Vector2(System.MathF.Cos(ang), System.MathF.Sin(ang));
                pa = FreeNear(c - dir * 9); pb = FreeNear(c + dir * 9);
                if (World.Terrain.IsLand(pa) && World.Terrain.IsLand(pb)) break;
            }
            DuelPrepare(a.Me, d.KitA, pa);
            DuelPrepare(b.Me, d.KitB, pb);
            d.Round++; d.StartAt = World.Time; d.EndAt = World.Time + 100;
            log($"ДУЭЛЬ раунд {d.Round}: {a.Me.Name} ({Items.Name(d.KitA)}) против {b.Me.Name} ({Items.Name(d.KitB)}) у ({c.X:0};{c.Y:0}), камней и деревьев рядом {cover}");
        }

        Vector2 FreeNear(Vector2 p)
        {
            for (int r = 0; r < 12; r++)
                for (int k = 0; k < 8; k++)
                {
                    var q = p + new Vector2(System.MathF.Cos(k * 0.785f), System.MathF.Sin(k * 0.785f)) * r * 0.5f;
                    if (World.IsFree(q, 0.5f)) return q;
                }
            return p;
        }

        void DuelPrepare(Actor x, string gun, Vector2 at)
        {
            if (x.Dead) World.RespawnPlayer(x);
            // убрать стволы и патроны с прошлого раунда (и подобранные с трупа) — раунд идёт ровно с заданным оружием
            static bool GunOrAmmo(ItemSlot s) => s != null && (s.Def?.Category == ItemCategory.Ammo || (s.Def?.Weapon != null && !s.Def.Weapon.Melee));
            for (int s = 0; s < Actor.BeltSize; s++) if (GunOrAmmo(x.Belt[s])) x.Belt[s] = null;
            for (int s = 0; s < Actor.PackSize; s++) if (GunOrAmmo(x.Pack[s])) x.Pack[s] = null;
            var wd = Items.Get(gun).Weapon;
            // оружие — сразу в руки (первый слот пояса), чтобы противник его видел
            for (int s = 0; s < Actor.BeltSize; s++) if (x.Belt[s]?.Id == gun) x.Belt[s] = null;
            for (int s = 0; s < Actor.PackSize; s++) if (x.Pack[s]?.Id == gun) x.Pack[s] = null;
            var old = x.Belt[0];
            x.Belt[0] = Items.NewSlot(gun, wd.Melee ? 0 : wd.Magazine);
            x.Current = 0;
            if (old != null) x.AddSlot(old);
            if (!wd.Melee) x.Add(wd.AmmoId, gun == "weapon_bow" ? 25 : gun == "weapon_pipe" ? 40 : 90);
            if (x.Count("bandage") < 3) x.Add("bandage", 3 - x.Count("bandage"));
            x.Hp = x.MaxHp; x.Food = 100; x.Water = 100;
            x.Pos = at; x.Vel = Vector2.Zero;
        }

        /// <summary>Отладка: «рейд» — снять долю прочности со всех построек бота от имени attackerId.</summary>
        public int DamageBotBase(int index, float fraction, int attackerId)
        {
            int i = 0, n = 0;
            foreach (var b in _bots.Values)
                if (i++ == index)
                    foreach (var s in new List<Structure>(World.Structures))
                        if (s.OwnerId == b.Me.Id) { World.DamageStructure(s, s.MaxHp * fraction, attackerId); n++; }
            return n;
        }

        public Vector2 BotHome(int index)
        {
            int i = 0;
            foreach (var b in _bots.Values)
                if (i++ == index)
                    foreach (var d in World.Deployables) if (d.Kind == DeployKind.Claim && d.OwnerId == b.Me.Id) return d.Pos;
            return BotPos(index);
        }

        public Vector2 BotPos(int index) { int i = 0; foreach (var b in _bots.Values) if (i++ == index) return b.Me.Pos; return Vector2.Zero; }

        public string BotLabel(int actorId) => _bots.TryGetValue(actorId, out var b) ? $"{b.Me.Name}: {b.Doing}" : null;

        public void Submit(int clientId, InputCmd cmd) => _pending[clientId] = cmd;

        static readonly bool PerfLog = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--perflog") >= 0;

        public void Tick()
        {
            _inputs.Clear();
            foreach (var kv in _pending) _inputs[kv.Key] = kv.Value;
            _pending.Clear();
            if (!_botsStarted) StartBots();
            long t0 = SimPerf.Begin();
            foreach (var b in _brains)
            {
                long tb = SimPerf.Begin();
                _inputs[b.Me.Id] = b.Think();
                SimPerf.End(b is PlayerBot ? "мозг бота" : "мозг охраны", tb);
            }
            SimPerf.End("мозги всего", t0);
            long ts = SimPerf.Begin();
            World.Step(_inputs);
            SimPerf.End("шаг мира", ts);
            SimPerf.End("тик всего", t0);
            SimPerf.TickDone();
            // --perflog: профиль раз в 30 с мира в stdout (окно песочницы: сколько стоит тик и сколько памяти)
            if (PerfLog && World.Tick % (int)(30 / World.Dt) == 0) { System.Console.WriteLine($"[{World.Time,6:0}s] {SimPerf.Report()} | процесс {System.Environment.WorkingSet / (1024 * 1024)} МБ"); SimPerf.Reset(); }
            _eventsThisTick.Clear();
            _eventsThisTick.AddRange(World.Events);
            Chronicle?.Record(World, _bots);
        }

        /// <summary>Летопись вайпа (scripts/wipe-report.py): включает клиент или отладка --chronicle=путь.</summary>
        public Chronicle Chronicle;

        /// <summary>Начать летопись в файл (null — путь из --chronicle=…, если задан).</summary>
        public void StartChronicle(string path = null)
        {
            path ??= System.Array.Find(System.Environment.GetCommandLineArgs(), a => a.StartsWith("--chronicle="))?.Substring(12);
            if (path == null || Chronicle != null) return;
            Chronicle = new Chronicle(path, World);
        }

        readonly List<ResourceNode> _snapNodes = new();
        readonly List<Deployable> _snapDeploys = new();

        public Snapshot BuildSnapshot(int clientId)
        {
            var me = World.Find(clientId);
            var snap = new Snapshot { Tick = World.Tick, Time = World.Time, DayTime = World.TimeOfDay };
            snap.Self = BuildSelf(me);
            snap.Self.Place = World.MonumentAt(me.Pos)?.Name;
            snap.Self.Ui = World.BuildUi(me);
            if (!me.Dead && me.Held?.Id is "build_plan" or "tool_hammer" or "deploy_claim") snap.Self.BuildGuide = BuildGuide(me);
            if (!me.Dead && me.Held?.Def?.Build == true)
                foreach (var pk in Building.PieceOrder)
                {
                    int k = (int)pk;
                    var cost = Building.Cost(pk, pk == StructKind.Door ? 1 : 0);
                    snap.Self.BuildPieces.Add((k, Building.KindName((StructKind)k), string.Join(", ", System.Array.ConvertAll(cost, c => $"{Items.Name(c.id)} {c.n}")), System.Array.TrueForAll(cost, c => me.Count(c.id) >= c.n)));
                }
            snap.Self.OpenCraft = me.WantsCraftMenu;
            me.WantsCraftMenu = false;
            foreach (var d in World.Deployables) if (d.Kind == DeployKind.Claim && d.Auth.Contains(me.Id)) { snap.Self.Home = d.Pos; break; }
            var myBag = World.ReadyBag(me);
            snap.Self.HasBag = myBag != null;
            snap.Self.BagReadyIn = myBag != null ? System.Math.Max(0, myBag.ReadyAt - World.Time) : 0;
            if (me.Held?.Def?.Build == true && World.ClaimAt(Building.CellOf(me.LastAim)) is Deployable zc) { snap.Self.ZoneCell = Building.CellOf(zc.Pos); snap.Self.HasZone = true; snap.Self.BuildStatus = BuildStatus(zc, me); }
            else if (me.Held?.Def?.Build == true) snap.Self.BuildStatus = "Нет зоны шкафа — сначала поставь шкаф";
            float r2 = WorldViewRadius * WorldViewRadius;

            foreach (var a in World.Actors)
            {
                bool visible = a.Id == me.Id || IsVisibleTo(me, a);
                if (!visible) continue;
                var w = a.Weapon;
                snap.Actors.Add(new ActorSnap
                {
                    Id = a.Id, Pos = a.Pos, Facing = a.Facing, RecoilDeg = a.Weapon?.Recoil ?? 0, Peek = a.Peek, Dead = a.Dead, IsNpc = a.Team == Team.Npc,
                    HeldId = a.Held?.Id, Reloading = w != null && w.ReloadLeft > 0,
                    Sprinting = a.Vel.Length() > World.WalkSpeed + 0.3f,
                    Crouching = a.Crouching,
                    Swimming = a.Swimming,
                    Label = BotLabel(a.Id),
                });
            }

            foreach (var p in World.Projectiles)
                if (Vector2.DistanceSquared(p.Pos, me.Pos) < EventRadius * EventRadius)
                    snap.Projectiles.Add(new ProjSnap { Id = p.Id, Pos = p.Pos, Dir = p.Dir, Arrow = p.Arrow });

            foreach (var b in World.Bags)
                if (Vector2.DistanceSquared(b.Pos, me.Pos) < 60 * 60 && (b.IsCrate || IsPointVisible(me, b.Pos)))
                    snap.Bags.Add(new BagSnap { Id = b.Id, Pos = b.Pos, IsCrate = b.IsCrate, Empty = b.Empty, Safe = b.Safe, Punch = b.Punch, Unlocked = b.Unlocked, Tier = b.Tier });

            foreach (var n in World.NodesInRadius(me.Pos, WorldViewRadius, _snapNodes))
                if (Vector2.DistanceSquared(n.Pos, me.Pos) < r2)
                    snap.Nodes.Add(new NodeSnap { Id = n.Id, Kind = n.Kind, Pos = n.Pos, Radius = n.Radius, Fill = n.Amount / n.MaxAmount });

            foreach (var p in World.Pickups)
                if (Vector2.DistanceSquared(p.Pos, me.Pos) < r2)
                    snap.Pickups.Add(new PickupSnap { Id = p.Id, Kind = p.Kind, Pos = p.Pos, Available = p.Available, Item = p.Item });

            // туман войны для построек и установленного (D32): своё видно всегда, чужое — только в обзоре, остальное — как запомнил
            float vr = World.ViewRange(me), vc = World.ViewCone(me);
            var dmem = Memory(_deployMem, clientId);
            var aliveD = new HashSet<int>();
            foreach (var d in World.DeploysInRadius(me.Pos, WorldViewRadius, _snapDeploys))
            {
                if (Vector2.DistanceSquared(d.Pos, me.Pos) >= r2) continue;
                aliveD.Add(d.Id);
                bool seen = me.Spectator || !me.Dead && (World.IsOwnBase(me, d) || Vector2.Distance(me.Pos, d.Pos) < 1.5f || World.SeesPoint(me, d.Pos, vr, vc));
                if (seen) dmem[d.Id] = new DeploySnap { Id = d.Id, Kind = d.Kind, Pos = d.Pos, Burning = d.Burning, Level = d.Level, Seen = true };
                else if (dmem.TryGetValue(d.Id, out var old)) { old.Seen = false; dmem[d.Id] = old; }
            }
            Forget(dmem, aliveD, id => dmem[id].Pos, me, vr, vc);
            foreach (var d in dmem.Values)
                if (Vector2.DistanceSquared(d.Pos, me.Pos) < r2) snap.Deployables.Add(d);

            foreach (var t in World.Throwns)
                if (Vector2.DistanceSquared(t.Pos, me.Pos) < EventRadius * EventRadius)
                    snap.Throwns.Add(new ThrownSnap { Id = t.Id, ItemId = t.ItemId, Pos = t.Pos, Z = t.Z, Fuse = t.Landed ? t.FuseLeft : -1 });
            foreach (var f in World.Fires)
                if (Vector2.DistanceSquared(f.Pos, me.Pos) < EventRadius * EventRadius)
                    snap.Fires.Add(new FireSnap { Id = f.Id, Pos = f.Pos, Radius = f.Radius, Left = f.Left });
            snap.StructVersion = World.StructVersion;
            snap.PrefabVersion = World.PrefabVersion;
            World.CollectGoneWalls(snap.GoneWalls);
            foreach (var s in World.Substations) snap.DecorSprites.Add((s.Panel.Id, s.Panel.Sprite));
            foreach (var d in World.MovingDecors) snap.DecorMoves.Add((d.Id, d.Pos, d.Rot));
            if (World.Director is { } dir)
            {
                snap.Phase = dir.Phase;
                foreach (var ev in dir.Events)
                    snap.Markers.Add(new DirectorMarker { Id = ev.Id, Kind = ev.Kind, Name = ev.Name, Pos = ev.Pos, PlaneFrom = ev.PlaneFrom, PlaneTo = ev.PlaneTo, StartsAt = ev.StartsAt, EndsAt = ev.EndsAt, Open = ev.Open, Started = ev.Kind == "airdrop" ? ev.Crate != null : ev.StartsAt > 0 });
                for (int i = System.Math.Max(0, dir.Radio.Count - 6); i < dir.Radio.Count; i++) snap.Radio.Add((dir.Radio[i].n, dir.Radio[i].text));
                if (me.Spectator) snap.Hot.AddRange(dir.HotSpots(12));
            }
            var smem = Memory(_structMem, clientId);
            var aliveS = new HashSet<int>();
            foreach (var st in World.Structures)
            {
                if (Vector2.DistanceSquared(st.Center, me.Pos) >= r2) continue;
                aliveS.Add(st.Id);
                bool seen = me.Spectator || !me.Dead && (World.IsOwnBase(me, st) || World.SeesStructure(me, st, vr, vc));
                if (seen) smem[st.Id] = new StructSnap { Id = st.Id, Kind = st.Kind, I = st.I, J = st.J, Dir = st.Dir, Tier = st.Tier, Hp = st.Hp / st.MaxHp, Open = st.Open, Locked = st.Locked, Seen = true, A = st.A, B = st.B, C = st.C };
                else if (smem.TryGetValue(st.Id, out var old)) { old.Seen = false; smem[st.Id] = old; }
            }
            Forget(smem, aliveS, id => smem[id].Center, me, vr, vc);
            foreach (var st in smem.Values)
            {
                if (Vector2.DistanceSquared(st.Center, me.Pos) < r2) snap.Structures.Add(st);
            }

            foreach (var e in _eventsThisTick)
            {
                bool mine = e.ActorId == me.Id || e.OtherId == me.Id;
                bool personal = e.Kind is SimEventKind.Loot or SimEventKind.Gather or SimEventKind.Info
                                       or SimEventKind.Crafted or SimEventKind.Placed or SimEventKind.Locked or SimEventKind.Revealed;   // Built видят все рядом: слышно стройку
                if (personal && e.ActorId != me.Id) continue;
                if (!mine && Vector2.DistanceSquared(e.Pos, me.Pos) > EventRadius * EventRadius) continue;
                snap.Events.Add(e);
            }
            return snap;
        }

        SelfInfo BuildSelf(Actor me)
        {
            var held = me.Held;
            var w = me.Weapon;
            bool gun = w != null && !w.Def.Melee;
            var s = new SelfInfo
            {
                Id = me.Id, Dead = me.Dead, Hp = me.Hp, MaxHp = me.MaxHp, Food = me.Food, Water = me.Water,
                WeaponName = held != null ? Items.Name(held.Id) : "пустые руки",
                HoldingGun = gun,
                Ammo = gun ? w.Ammo : 0, Magazine = gun ? w.Def.Magazine : 0,
                Reserve = gun ? me.Count(w.Def.AmmoId) : 0,
                ReloadProgress = gun && w.ReloadLeft > 0 ? 1f - w.ReloadLeft / w.Def.ReloadTime : 0,
                SpreadDeg = World.CurrentSpreadDeg(me),
                Bandages = me.Count("bandage"),
                Current = me.Current,
                HeldDeploy = held?.Def?.Deploy is DeployKind.Workbench or DeployKind.Furnace,
                NearBench = World.NearBench(me.Pos),
                BenchLevel = World.BenchLevel(me.Pos),
                Scrap = me.Count("scrap"),
            };
            foreach (var n in Tech.All)
            {
                bool can = World.CanResearch(me, n, out string why);
                s.Tech.Add(new TechInfo { Id = n.Id, Name = Items.Name(n.Id), Bench = n.Bench, Scrap = n.Scrap, Requires = n.Requires, Known = me.Known.Contains(n.Id), CanResearch = can, Why = why });
            }

            for (int i = 0; i < Actor.BeltSize; i++)
            {
                var slot = me.Belt[i];
                if (slot == null) continue;
                var st = slot.State;
                bool isGun = st != null && !st.Def.Melee;
                s.Belt[i] = new BeltSlotInfo
                {
                    Id = slot.Id, Name = Items.Name(slot.Id), Count = slot.Count,
                    IsGun = isGun, Ammo = isGun ? st.Ammo : 0, Magazine = isGun ? st.Def.Magazine : 0,
                    IsDeployable = slot.Def?.Deploy != DeployKind.None,
                };
            }

            foreach (var it in me.Pack) s.Pack.Add(World.Cell(it));
            foreach (var it in me.Belt) s.BeltCells.Add(World.Cell(it));
            foreach (var job in me.CraftQueue)
                s.CraftQueue.Add((job.Recipe.Output, Items.Name(job.Recipe.Output), 1f - job.Left / job.Recipe.Time));

            for (int i = 0; i < Recipes.All.Count; i++)
            {
                var r = Recipes.All[i];
                var cost = new List<string>();
                var costs = new List<(string, int, int)>();
                foreach (var (id, n) in r.Cost) { cost.Add($"{Items.Name(id)} {n}"); costs.Add((id, n, me.Count(id))); }
                s.Recipes.Add(new RecipeInfo
                {
                    Index = i,
                    Output = r.Output,
                    Name = Items.Name(r.Output) + (r.Count > 1 ? $" ×{r.Count}" : ""),
                    CostText = string.Join(", ", cost),
                    CanCraft = World.CanCraft(me, r),
                    NeedsBench = r.Bench > 0,
                    Bench = r.Bench,
                    Known = Tech.Knows(me, r.Output),
                    Time = r.Time,
                    Count = r.Count,
                    Costs = costs,
                });
            }

            if (!me.Dead) s.InteractHint = Hint(World.NearestInteract(me));
            if (!me.Dead && held?.Def?.Build == true)
            {
                s.HoldingPlan = true;
                s.BuildPiece = me.BuildPiece;
                var ghost = World.EffectivePiece(me, me.LastAim);
                s.GhostPiece = ghost;
                (s.GhostPoly, s.GhostA, s.GhostB) = World.GhostOf(ghost, me.LastAim);
                s.BuildOk = World.CanBuild(me, ghost, me.LastAim, out string why);
                var cost = Building.Cost(ghost, ghost == StructKind.Door ? 1 : 0);
                string price = string.Join(", ", System.Array.ConvertAll(cost, c => $"{Items.Name(c.id)} {c.n}"));
                if (string.IsNullOrEmpty(s.InteractHint))
                    s.InteractHint = $"ЛКМ — {Building.KindName(ghost)} · {price}{(s.BuildOk ? "" : " · " + why)}";
            }
            else if (!me.Dead && held?.Id == "tool_hammer" && World.TargetStructure(me, 2f) is Structure t && string.IsNullOrEmpty(s.InteractHint))
            {
                string state = $"{Building.KindName(t.Kind)} ({Building.TierNames[t.Tier]}) {(int)t.Hp}/{(int)t.MaxHp}";
                if (t.Tier < Building.MaxTier)
                {
                    var up = Building.Cost(t.Kind, t.Tier + 1);
                    s.InteractHint = $"{state} · ЛКМ — {Building.TierNames[t.Tier + 1]}: " + string.Join(", ", System.Array.ConvertAll(up, c => $"{Items.Name(c.id)} {c.n}"));
                }
                else s.InteractHint = state + (t.Hp < t.MaxHp ? " · ЛКМ — починить" : "");
            }
            return s;
        }

        string Hint((World.InteractKind kind, object target) it) => it.kind switch
        {
            World.InteractKind.Stash => "[E] " + ((Stash)it.target).Verb,
            World.InteractKind.Bag when World.Director?.Events.Find(e => e.Crate == it.target) is { } ev && !ev.Open => ev.Kind == "airdrop" ? $"Сброс груза — откроется через {System.MathF.Ceiling(ev.StartsAt - World.Time)} с" : ev.StartsAt > 0 ? $"Закрытый ящик — взлом, {System.MathF.Ceiling(ev.StartsAt - World.Time)} с" : "Закрытый ящик — запусти взлом рядом [E]",
            World.InteractKind.Panel => ((Substation)it.target).Powered ? "Главный щиток — включён" : $"[E] Главный щиток — вставить пробку ({((Substation)it.target).FusesIn}/2)",
            World.InteractKind.Bag when ((LootBag)it.target).Punch && !((LootBag)it.target).Unlocked => "[E] Крейт — перфокарта I",
            World.InteractKind.Bag => ((LootBag)it.target) is var lb && lb.Safe && !lb.Unlocked ? "[E] Сейф — нужна карта доступа" : lb.Safe ? "[E] Открыть сейф" : lb.IsCrate ? "[E] Открыть ящик" : lb.Corpse ? "[E] Обыскать рюкзак" : "[E] Подобрать вещи",
            World.InteractKind.Recycler => "[E] Переработчик — детали в лом",
            World.InteractKind.Pickup => ((Pickup)it.target).Kind switch
            {
                PickupKind.Hemp => "[E] Собрать коноплю",
                PickupKind.Berries => "[E] Собрать ягоды",
                PickupKind.Fuse => "[E] Взять пробку",
                PickupKind.PunchCard => "[E] Взять перфокарту I",
                PickupKind.LocalItem => $"[E] Взять: {Items.Name(((Pickup)it.target).Item)}",
                _ => "[E] Попить",
            },
            World.InteractKind.Furnace => FurnaceHint((Deployable)it.target),
            World.InteractKind.Workbench => "[E] Верстак — изучение и крафт",
            World.InteractKind.Box => ((Deployable)it.target).Storage.Empty ? "[E] Ящик (пуст)" : $"[E] Ящик ({((Deployable)it.target).Storage.Items.Count} шт.)",
            World.InteractKind.Claim => ClaimHint(World, (Deployable)it.target),
            World.InteractKind.Door => (((Structure)it.target).Open ? "[E] Закрыть дверь" : "[E] Открыть дверь") + (((Structure)it.target).Locked ? " · замок" : ""),
            _ => "",
        };

        /// <summary>Шпаргалка по шагам: что уже сделано на твоей базе.</summary>
        string BuildGuide(Actor me)
        {
            Deployable claim = null;
            foreach (var d in World.Deployables) if (d.Kind == DeployKind.Claim && d.OwnerId == me.Id) { claim = d; break; }
            int found = 0, walls = 0, doorways = 0, doors = 0, locked = 0, weak = 0, total = 0;
            if (claim != null)
                foreach (var s in World.Structures)
                {
                    if (s.ClaimId != claim.Id) continue;
                    total++;
                    if (s.Tier == 0) weak++;
                    switch (s.Kind)
                    {
                        case StructKind.Foundation: case StructKind.TriFoundation: found++; break;
                        case StructKind.Wall: case StructKind.Window: walls++; break;
                        case StructKind.Doorway: doorways++; break;
                        case StructKind.Door: doors++; if (s.Locked) locked++; break;
                    }
                }
            float hours = claim != null ? World.ClaimHoursLeft(claim) : 0;
            string L(bool done, string text) => (done ? "[x] " : "[ ] ") + text;
            return "КАК ПОСТРОИТЬ ДОМ\n" +
                L(claim != null, "Шкаф: возьми его в руки, ЛКМ по земле") + "\n" +
                L(found >= 1, $"Фундаменты: План в руки, ЛКМ по земле ({found})") + "\n" +
                L(walls >= 3, $"Стены: наведи План на фундамент — край станет стеной, ЛКМ ({walls})") + "\n" +
                L(doorways >= 1, "Проём: зажми ПКМ → Дверной проём, ЛКМ по краю") + "\n" +
                L(doors >= 1, "Дверь: наведи План на проём, ЛКМ") + "\n" +
                L(locked >= 1, "Замок (крафт Q) — в руки, ЛКМ по двери") + "\n" +
                L(total > 0 && weak == 0, "Киянкой ЛКМ: каркас → дерево → камень") + "\n" +
                L(hours >= 1, $"Ресурсы в шкаф (E), иначе сгниёт ({hours:0.0} ч)");
        }

        string BuildStatus(Deployable c, Actor me)
        {
            var (b, found, doors) = World.ClaimLoad(c);
            float h = World.ClaimHoursLeft(c);
            string who = c.Auth.Contains(me.Id) ? "твоя база" : "ЧУЖАЯ зона";
            string upkeep = b <= 0 ? "" : h > 0 ? $" · запас {h:0.0} ч" : " · запаса нет";
            return $"{who}: нагрузка {b:0}/{World.BudgetMax} (осталось {World.BudgetMax - b:0}) · фундаменты {found}/{World.FoundMax} · двери {doors}/{World.DoorMax}{upkeep}";
        }

        static string ClaimHint(World w, Deployable c)
        {
            var (b, found, doors) = w.ClaimLoad(c);
            float h = w.ClaimHoursLeft(c);
            string upkeep = b <= 0 ? "" : h > 0 ? $" · запас {h:0.0} ч" : c.Grace > 0 ? $" · ЗАПАСА НЕТ, гниение через {c.Grace / 60:0} мин" : " · ГНИЁТ";
            return $"[E] Шкаф · нагрузка {b:0}/{World.BudgetMax} · фундаменты {found}/{World.FoundMax} · двери {doors}/{World.DoorMax}{upkeep}";
        }

        static string FurnaceHint(Deployable f)
        {
            string state = f.Burning ? $"горит · руда {f.Ore + f.SulfurOre}, дерево {(int)f.Fuel}" : "не горит";
            int ready = f.OutMetal + f.OutSulfur + f.OutCharcoal;
            return $"[E] Печь ({state}{(ready > 0 ? $" · готово {ready}" : "")})";
        }

        /// <summary>Серверный туман войны: клиент не получает тех, кого не видит (защита от wallhack).</summary>
        bool IsVisibleTo(Actor viewer, Actor target)
        {
            if (viewer.Spectator) return Vector2.Distance(viewer.Pos, target.Pos) < WorldViewRadius;
            if (viewer.Dead) return Vector2.Distance(viewer.Pos, target.Pos) < 30;
            if (World.CanSeeActor(viewer, target, World.ViewRange(viewer), World.ViewCone(viewer))) return true;
            // выстрел выдаёт стрелка, если между нами нет стен
            bool shotRecently = World.Time - target.LastShotTime < 0.6f;
            return shotRecently && Vector2.Distance(viewer.Pos, target.Pos) < 60 && World.HasLineOfSight(viewer.Pos, target.Pos);
        }

        // память тумана войны: что каждый игрок видел из построек и установленного
        readonly Dictionary<int, Dictionary<int, StructSnap>> _structMem = new();
        readonly Dictionary<int, Dictionary<int, DeploySnap>> _deployMem = new();

        static Dictionary<int, T> Memory<T>(Dictionary<int, Dictionary<int, T>> all, int clientId)
        {
            if (!all.TryGetValue(clientId, out var m)) all[clientId] = m = new Dictionary<int, T>();
            return m;
        }

        /// <summary>Запомненное, чего больше нет: забываем, только когда место снова в обзоре (иначе так и висит в тумане).</summary>
        void Forget<T>(Dictionary<int, T> mem, HashSet<int> alive, System.Func<int, Vector2> posOf, Actor me, float vr, float vc)
        {
            List<int> gone = null;
            foreach (var id in mem.Keys)
            {
                if (alive.Contains(id)) continue;
                var p = posOf(id);
                if (Vector2.DistanceSquared(p, me.Pos) < WorldViewRadius * WorldViewRadius && !World.SeesPoint(me, p, vr, vc)) continue;
                (gone ??= new()).Add(id);
            }
            if (gone != null) foreach (var id in gone) mem.Remove(id);
        }

        bool IsPointVisible(Actor viewer, Vector2 p) => viewer.Spectator ||
            Vector2.Distance(viewer.Pos, p) < 3 || World.CanSee(viewer, p, World.ViewRange(viewer), World.ViewCone(viewer));
    }
}
