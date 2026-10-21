using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>Элементы базы. Фундамент — клетка 2×2 м, остальное стоит на рёбрах клеток.</summary>
    public enum StructKind { Foundation, Wall, Doorway, Window, Door }

    public sealed class Structure
    {
        public int Id;
        public StructKind Kind;
        public int I, J, Dir;          // клетка; для ребра Dir: 0 — горизонтальное (y = J·2), 1 — вертикальное (x = I·2)
        public int Tier;               // 0 разметка, 1 дерево, 2 камень, 3 металл, 4 сталь
        public float Hp;
        public bool Open;              // дверь открыта
        public bool Locked;            // на двери замок
        public bool KeyLock;           // замок с ключом (иначе кодовый)
        public int Code;
        public readonly HashSet<int> LockAuth = new();
        public int OwnerId;
        public int ClaimId;            // к какому столбу привязан (бюджет и содержание)
        public readonly List<MapWall> Boxes = new();
        public float MaxHp => Building.MaxHp(Kind, Tier);
        public bool IsEdge => Kind != StructKind.Foundation;
        public Vector2 Center => IsEdge ? Building.EdgeCenter(I, J, Dir) : Building.CellCenter(I, J);
    }

    /// <summary>
    /// Геометрия и цены строительства. Общая для сервера и клиента: клиент по тем же правилам
    /// строит картинку и тени обзора из снапшота (id, вид, клетка, уровень).
    /// </summary>
    public static class Building
    {
        public const float Cell = 2f;          // клетка строительства (D21)
        public const float Thick = 0.3f;       // толщина стены
        public const float WallH = 2.6f;       // высота стены: закрывает обзор
        public const float WindowH = 1.0f;     // подоконник: укрытие, обзор и пули проходят
        public const float PostW = 0.5f;       // косяк дверного проёма
        public const float BuildRange = 6f;
        public const int MaxTier = 4;

        public static readonly string[] TierNames = { "разметка", "дерево", "камень", "металл", "сталь" };
        static readonly float[] TierHp = { 10, 250, 500, 1000, 2000 };

        public static string KindName(StructKind k) => k switch
        {
            StructKind.Foundation => "Фундамент",
            StructKind.Wall => "Стена",
            StructKind.Doorway => "Дверной проём",
            StructKind.Window => "Окно",
            _ => "Дверь",
        };

        public static float MaxHp(StructKind k, int tier) =>
            TierHp[Math.Clamp(tier, 0, MaxTier)] * (k == StructKind.Foundation ? 1.5f : k == StructKind.Door ? 0.8f : 1f);

        /// <summary>Цена установки (tier 0; дверь сразу деревянная) или улучшения до tier.</summary>
        public static (string id, int n)[] Cost(StructKind k, int tier)
        {
            float m = k == StructKind.Foundation ? 1.5f : k == StructKind.Window || k == StructKind.Doorway ? 0.7f : 1f;
            int N(float v) => (int)MathF.Round(v * m / 5f) * 5;
            if (k == StructKind.Door)
                return tier <= 1 ? new[] { ("wood", 150) } : tier == 2 ? new[] { ("stone", 200) } : tier == 3 ? new[] { ("metal_fragments", 150) } : new[] { ("metal_fragments", 250), ("scrap", 25) };
            return tier switch
            {
                0 => new[] { ("wood", N(30)) },
                1 => new[] { ("wood", N(200)) },
                2 => new[] { ("stone", N(300)) },
                3 => new[] { ("metal_fragments", N(200)) },
                _ => new[] { ("metal_fragments", N(300)), ("scrap", N(25)) },
            };
        }

        /// <summary>Во сколько раз урон по постройке меньше/больше: инструментом — только слабые уровни, пулями — двери и разметка (D22).</summary>
        public static float MeleeMult(StructKind k, int tier) => tier switch { 0 => 1f, 1 => k == StructKind.Door ? 0.35f : 0.25f, _ => 0f };
        public static float BulletMult(StructKind k, int tier) =>
            tier == 0 ? 1f : k == StructKind.Door ? (tier == 1 ? 0.5f : tier == 2 ? 0.3f : tier == 3 ? 0.15f : 0.06f) : 0f;

        public static (int i, int j) CellOf(Vector2 p) => ((int)MathF.Floor(p.X / Cell), (int)MathF.Floor(p.Y / Cell));
        public static Vector2 CellCenter(int i, int j) => new((i + 0.5f) * Cell, (j + 0.5f) * Cell);
        public static Vector2 EdgeCenter(int i, int j, int dir) =>
            dir == 0 ? new Vector2((i + 0.5f) * Cell, j * Cell) : new Vector2(i * Cell, (j + 0.5f) * Cell);

        /// <summary>Ближайшее к точке ребро сетки.</summary>
        public static (int i, int j, int dir) NearestEdge(Vector2 p)
        {
            int hj = (int)MathF.Round(p.Y / Cell), hi = (int)MathF.Floor(p.X / Cell);
            int vi = (int)MathF.Round(p.X / Cell), vj = (int)MathF.Floor(p.Y / Cell);
            float dh = MathF.Abs(p.Y - hj * Cell), dv = MathF.Abs(p.X - vi * Cell);
            return dh <= dv ? (hi, hj, 0) : (vi, vj, 1);
        }

        /// <summary>Клетки по обе стороны ребра.</summary>
        public static ((int, int) a, (int, int) b) EdgeCells(int i, int j, int dir) =>
            dir == 0 ? ((i, j - 1), (i, j)) : ((i - 1, j), (i, j));

        /// <summary>Рамка вдоль ребра от доли t0 до t1 его длины.</summary>
        static (Vector2 min, Vector2 max) Span(int i, int j, int dir, float t0, float t1)
        {
            float h = Thick * 0.5f;
            if (dir == 0)
            {
                float x0 = i * Cell, y = j * Cell;
                return (new Vector2(x0 + t0 * Cell, y - h), new Vector2(x0 + t1 * Cell, y + h));
            }
            float x = i * Cell, y0 = j * Cell;
            return (new Vector2(x - h, y0 + t0 * Cell), new Vector2(x + h, y0 + t1 * Cell));
        }

        /// <summary>Твёрдые части элемента: рамки, высота, пропускает ли пули.</summary>
        public static List<(Vector2 min, Vector2 max, float h, bool passBullets)> Boxes(StructKind k, int i, int j, int dir, bool open)
        {
            var list = new List<(Vector2, Vector2, float, bool)>();
            float post = PostW / Cell;
            switch (k)
            {
                case StructKind.Wall:
                    { var (a, b) = Span(i, j, dir, 0, 1); list.Add((a, b, WallH, false)); }
                    break;
                case StructKind.Doorway:
                    { var (a, b) = Span(i, j, dir, 0, post); list.Add((a, b, WallH, false)); }
                    { var (a, b) = Span(i, j, dir, 1 - post, 1); list.Add((a, b, WallH, false)); }
                    break;
                case StructKind.Window:
                    { var (a, b) = Span(i, j, dir, 0, 0.3f); list.Add((a, b, WallH, false)); }
                    { var (a, b) = Span(i, j, dir, 0.7f, 1); list.Add((a, b, WallH, false)); }
                    { var (a, b) = Span(i, j, dir, 0.3f, 0.7f); list.Add((a, b, WindowH, true)); }
                    break;
                case StructKind.Door:
                    if (!open) { var (a, b) = Span(i, j, dir, post, 1 - post); list.Add((a, b, WallH, false)); }
                    break;
            }
            return list;
        }
    }

    public sealed partial class World
    {
        public readonly List<Structure> Structures = new();
        readonly Dictionary<(int, int), Structure> _foundations = new();
        readonly Dictionary<(int, int, int), Structure> _edges = new();
        readonly Dictionary<(int, int, int), Structure> _doors = new();
        readonly List<Actor> _qActors = new();

        /// <summary>Постройки поменялись (для клиента: пересобрать тени обзора и акустику).</summary>
        public int StructVersion;

        bool HasFoundation(int i, int j) => _foundations.ContainsKey((i, j));
        public Structure FoundationAt(int i, int j) => _foundations.GetValueOrDefault((i, j));
        public Structure EdgeAt(int i, int j, int dir) => _edges.GetValueOrDefault((i, j, dir));
        public Structure DoorAt(int i, int j, int dir) => _doors.GetValueOrDefault((i, j, dir));

        /// <summary>Клетка свободна под фундамент по геометрии (без правил столба): суша, не РТ, нет препятствий.</summary>
        public bool FoundationGeoOk(int ci, int cj, out string why)
        {
            why = "";
            if (HasFoundation(ci, cj)) { why = "уже стоит"; return false; }
            var min = new Vector2(ci * Building.Cell, cj * Building.Cell);
            var max = min + new Vector2(Building.Cell);
            if (!Terrain.IsLand(min) || !Terrain.IsLand(max) || !Terrain.IsLand(new Vector2(min.X, max.Y)) || !Terrain.IsLand(new Vector2(max.X, min.Y)))
            { why = "вода"; return false; }
            if (NearMonument(min, 8) || NearMonument(max, 8) || NearMonument((min + max) * 0.5f, 8)) { why = "рядом с РТ нельзя"; return false; }
            var c = (min + max) * 0.5f;
            foreach (var w in WallsNear(c, 1.5f))
                if (w.Owner == null && Geo.BoxOverlap(min, max, w.Wall.Min, w.Wall.Max)) { why = "мешает препятствие"; return false; }
            foreach (var n in NodesNear(c, 1.5f))
                if (Geo.CircleBox(n.Pos, n.Radius, min, max)) { why = "мешает дерево/камень"; return false; }
            foreach (var d in DeploysNear(c, 1.5f))
                if (d.Kind != DeployKind.Claim && Geo.CircleBox(d.Pos, d.Radius, min, max)) { why = "мешает установленное"; return false; }
            return true;
        }

        string CostText((string id, int n)[] cost) => string.Join(", ", Array.ConvertAll(cost, c => $"{Items.Name(c.id)} {c.n}"));

        bool CanPay(Actor a, (string id, int n)[] cost)
        {
            foreach (var (id, n) in cost) if (a.Count(id) < n) return false;
            return true;
        }

        void Pay(Actor a, (string id, int n)[] cost)
        {
            foreach (var (id, n) in cost) a.Take(id, n);
        }

        /// <summary>Можно ли поставить элемент в эту точку; причина отказа — для подсказки.</summary>
        public bool CanBuild(Actor a, StructKind kind, Vector2 at, out string why)
        {
            why = "";
            if (Vector2.Distance(a.Pos, at) > Building.BuildRange) { why = "далеко"; return false; }
            if (kind == StructKind.Foundation)
            {
                var (ci, cj) = Building.CellOf(at);
                if (!FoundationGeoOk(ci, cj, out why)) return false;
                return ClaimAllows(a, kind, (ci, cj), out _, out why);
            }

            var (i, j, dir) = Building.NearestEdge(at);
            var (ca, cb) = Building.EdgeCells(i, j, dir);
            if (!HasFoundation(ca.Item1, ca.Item2) && !HasFoundation(cb.Item1, cb.Item2)) { why = "нужен фундамент рядом"; return false; }
            if (kind == StructKind.Door)
            {
                if (!_edges.TryGetValue((i, j, dir), out var frame) || frame.Kind != StructKind.Doorway) { why = "нужен дверной проём"; return false; }
                if (_doors.ContainsKey((i, j, dir))) { why = "дверь уже есть"; return false; }
            }
            else if (_edges.ContainsKey((i, j, dir))) { why = "занято"; return false; }
            foreach (var (min, max, _, _) in Building.Boxes(kind, i, j, dir, false))
                foreach (var o in Actors)
                    if (!o.Dead && Geo.CircleBox(o.Pos, o.Radius, min, max)) { why = "мешает человек"; return false; }
            var own = HasFoundation(ca.Item1, ca.Item2) ? ca : cb;
            return ClaimAllows(a, kind, own, out _, out why);
        }

        void TryBuild(Actor a, Vector2 at)
        {
            var kind = a.BuildPiece;
            if (!CanBuild(a, kind, at, out string why)) { Info(a, $"{Building.KindName(kind)}: {why}"); return; }
            int tier = kind == StructKind.Door ? 1 : 0;
            var cost = Building.Cost(kind, tier);
            if (!CanPay(a, cost)) { Info(a, $"Не хватает: {CostText(cost)}"); return; }
            Pay(a, cost);
            Structure s;
            if (kind == StructKind.Foundation)
            {
                var (ci, cj) = Building.CellOf(at);
                s = new Structure { Id = NextId(), Kind = kind, I = ci, J = cj, Dir = -1, Tier = tier, OwnerId = a.Id };
                _foundations[(ci, cj)] = s;
            }
            else
            {
                var (i, j, dir) = Building.NearestEdge(at);
                s = new Structure { Id = NextId(), Kind = kind, I = i, J = j, Dir = dir, Tier = tier, OwnerId = a.Id };
                if (kind == StructKind.Door) _doors[(i, j, dir)] = s; else _edges[(i, j, dir)] = s;
            }
            s.Hp = s.MaxHp;
            s.ClaimId = ClaimAt(kind == StructKind.Foundation ? (s.I, s.J) : Building.CellOf(s.Center))?.Id ?? 0;
            if (s.ClaimId == 0 && s.IsEdge)
            {
                var (ea, eb) = Building.EdgeCells(s.I, s.J, s.Dir);
                s.ClaimId = (ClaimAt(ea) ?? ClaimAt(eb))?.Id ?? 0;
            }
            Structures.Add(s);
            RebuildBoxes(s);
            StructVersion++;
            Events.Add(new SimEvent { Kind = SimEventKind.Built, ActorId = a.Id, OtherId = s.Id, Pos = s.Center, Text = Building.KindName(kind) });
        }

        void RebuildBoxes(Structure s)
        {
            foreach (var w in s.Boxes) { Walls.Remove(w); _wallGrid.Remove(w); }
            s.Boxes.Clear();
            if (!s.IsEdge) return;
            foreach (var (min, max, h, pass) in Building.Boxes(s.Kind, s.I, s.J, s.Dir, s.Open))
            {
                var w = new MapWall { Wall = new Wall(min, max, h), Kind = WallKind.Built, Owner = s, PassBullets = pass };
                s.Boxes.Add(w);
                Walls.Add(w);
                _wallGrid.Insert(w, min, max);
            }
        }

        void RemoveStructure(Structure s)
        {
            if (!Structures.Remove(s)) return;
            foreach (var w in s.Boxes) { Walls.Remove(w); _wallGrid.Remove(w); }
            s.Boxes.Clear();
            StructVersion++;
            Events.Add(new SimEvent { Kind = SimEventKind.Destroyed, OtherId = s.Id, Pos = s.Center, Text = Building.KindName(s.Kind), Value = s.Tier });
            switch (s.Kind)
            {
                case StructKind.Foundation:
                    _foundations.Remove((s.I, s.J));
                    // рёбра, у которых не осталось фундамента ни с одной стороны, падают
                    foreach (var e in new List<Structure>(_edges.Values))
                    {
                        var (ca, cb) = Building.EdgeCells(e.I, e.J, e.Dir);
                        if (!HasFoundation(ca.Item1, ca.Item2) && !HasFoundation(cb.Item1, cb.Item2)) RemoveStructure(e);
                    }
                    break;
                case StructKind.Door:
                    _doors.Remove((s.I, s.J, s.Dir));
                    break;
                default:
                    _edges.Remove((s.I, s.J, s.Dir));
                    if (_doors.TryGetValue((s.I, s.J, s.Dir), out var door)) RemoveStructure(door);
                    break;
            }
        }

        /// <summary>Урон постройке. Возвращает реально нанесённый урон (0 — уровень слишком прочный для этого оружия).</summary>
        public float DamageStructure(Structure s, float dmg, int attackerId)
        {
            if (dmg <= 0 || !Structures.Contains(s)) return 0;
            s.Hp -= dmg;
            if (s.Hp <= 0) RemoveStructure(s);
            return dmg;
        }

        /// <summary>Постройка перед персонажем (куда смотрит, в пределах досягаемости). Дверь важнее проёма.</summary>
        public Structure TargetStructure(Actor a, float reach)
        {
            // точка удара — куда наведён прицел, но не дальше досягаемости
            var toAim = a.LastAim - a.Pos;
            float dl = toAim.Length();
            var hit = dl > 0.05f ? a.Pos + toAim / dl * MathF.Min(dl, reach + 0.3f) : a.Pos + a.FacingDir * MathF.Min(reach, 1.3f);
            Structure best = null;
            float bestD = 0.75f;   // дальше от ребра — значит целимся в пол (фундамент)
            var (i, j, dir) = Building.NearestEdge(hit);
            foreach (var key in new[] { (i, j, dir) })
            {
                if (_doors.TryGetValue(key, out var d) && Vector2.Distance(d.Center, hit) < bestD + 0.3f) return d;
                if (_edges.TryGetValue(key, out var e)) { float dd = DistToEdge(e, hit); if (dd < bestD) { bestD = dd; best = e; } }
            }
            if (best != null) return best;
            var (ci, cj) = Building.CellOf(hit);
            return _foundations.TryGetValue((ci, cj), out var f) && Vector2.Distance(a.Pos, f.Center) < reach + 1.6f ? f : null;
        }

        static float DistToEdge(Structure e, Vector2 p)
        {
            var c = e.Center;
            return e.Dir == 0 ? MathF.Abs(p.Y - c.Y) + MathF.Max(0, MathF.Abs(p.X - c.X) - Building.Cell / 2)
                              : MathF.Abs(p.X - c.X) + MathF.Max(0, MathF.Abs(p.Y - c.Y) - Building.Cell / 2);
        }

        /// <summary>Молоток: улучшить на уровень или починить.</summary>
        void HammerHit(Actor a, Structure s)
        {
            if (s.Hp < s.MaxHp - 0.5f && (s.Tier >= Building.MaxTier || !CanPay(a, Building.Cost(s.Kind, s.Tier + 1))))
            {
                // ремонт: пятая часть цены текущего уровня за 20% прочности
                var full = Building.Cost(s.Kind, s.Tier);
                var cost = Array.ConvertAll(full, c => (c.id, Math.Max(1, c.n / 5)));
                if (!CanPay(a, cost)) { Info(a, $"На ремонт не хватает: {CostText(cost)}"); return; }
                Pay(a, cost);
                s.Hp = MathF.Min(s.MaxHp, s.Hp + s.MaxHp * 0.2f);
                Events.Add(new SimEvent { Kind = SimEventKind.Built, ActorId = a.Id, OtherId = s.Id, Pos = s.Center, Text = "ремонт" });
                return;
            }
            if (s.Tier >= Building.MaxTier) { Info(a, $"{Building.KindName(s.Kind)}: уже максимальный уровень"); return; }
            var up = Building.Cost(s.Kind, s.Tier + 1);
            if (!CanPay(a, up)) { Info(a, $"До уровня «{Building.TierNames[s.Tier + 1]}» не хватает: {CostText(up)}"); return; }
            Pay(a, up);
            s.Tier++;
            s.Hp = s.MaxHp;
            StructVersion++;
            Events.Add(new SimEvent { Kind = SimEventKind.Built, ActorId = a.Id, OtherId = s.Id, Pos = s.Center, Text = Building.TierNames[s.Tier], Value = s.Tier });
        }

        /// <summary>ЛКМ с замком в руках по двери: поставить кодовый замок, код — случайный, сообщается установщику.</summary>
        void InstallLock(Actor a)
        {
            var door = TargetStructure(a, 2f);
            if (door == null || door.Kind != StructKind.Door) { Info(a, "Замок ставится на дверь — подойди и наведи на неё"); return; }
            if (door.Locked) { Info(a, "На двери уже есть замок"); return; }
            door.Locked = true;
            var held = a.Held;
            door.KeyLock = held.Id == "lock_key";
            held.Count--;
            if (held.Count <= 0) a.Belt[a.Current] = null;
            StructVersion++;
            if (door.KeyLock)
            {
                // ключ — обычный предмет: потеряешь (убьют) — тот, кто подберёт, откроет дверь
                a.Add("key_" + door.Id, 1);
                Info(a, $"Замок поставлен. {Items.Name("key_" + door.Id)} — в инвентаре");
                return;
            }
            door.Code = Rng.Next(1000, 10000);
            door.LockAuth.Add(a.Id);
            Info(a, $"Кодовый замок поставлен. Код: {door.Code} — запомни");
        }

        /// <summary>Ввод кода у запертой двери: верный — доступ и дверь открывается; неверный — удар током.</summary>
        void EnterCode(Actor a, int doorId, int code)
        {
            Structure door = null;
            foreach (var s in Structures) if (s.Id == doorId) { door = s; break; }
            if (door == null || !door.Locked || Vector2.Distance(door.Center, a.Pos) > 3f) return;
            if (code == door.Code)
            {
                door.LockAuth.Add(a.Id);
                Info(a, "Код принят");
                if (!door.Open) ToggleDoor(a, door);
            }
            else
            {
                Damage(a, 5, -1, a.Pos);
                Info(a, "Неверный код — бьёт током");
            }
        }

        /// <summary>E по двери: открыть/закрыть. Закрыть нельзя, если в проёме кто-то стоит.</summary>
        void ToggleDoor(Actor a, Structure door)
        {
            if (door.Locked && door.KeyLock)
            {
                if (a.Count("key_" + door.Id) <= 0) { Info(a, "Заперто на ключ"); Events.Add(new SimEvent { Kind = SimEventKind.Door, ActorId = a.Id, OtherId = door.Id, Pos = door.Center, Value = -1 }); return; }
            }
            else if (door.Locked && !door.LockAuth.Contains(a.Id))
            {
                Events.Add(new SimEvent { Kind = SimEventKind.Locked, ActorId = a.Id, OtherId = door.Id, Pos = door.Center });
                return;
            }
            if (door.Open)
                foreach (var (min, max, _, _) in Building.Boxes(StructKind.Door, door.I, door.J, door.Dir, false))
                    foreach (var o in Actors)
                        if (!o.Dead && Geo.CircleBox(o.Pos, o.Radius, min, max)) { Info(a, "В проёме кто-то стоит"); return; }
            door.Open = !door.Open;
            RebuildBoxes(door);
            StructVersion++;
            Events.Add(new SimEvent { Kind = SimEventKind.Door, ActorId = a.Id, OtherId = door.Id, Pos = door.Center, Value = door.Open ? 1 : 0 });
        }

        // ---------- межевой столб: зона, бюджет, содержание (комбинация Астры, docs/gpt/05-stability.md) ----------

        public const int ClaimRadius = 8;        // клеток по d∞
        public const int ClaimGap = 21;          // минимум между столбами
        public const float BudgetMax = 360, FoundMax = 160, DoorMax = 48;
        const float UpkeepPerHour = 0.04f;       // доля цены текущих элементов
        const float DecayPerMinute = 0.01f;      // доля максимального HP

        static (int, int) ClaimCell(Deployable c) => Building.CellOf(c.Pos);

        static int CellDist((int i, int j) a, (int i, int j) b) => Math.Max(Math.Abs(a.i - b.i), Math.Abs(a.j - b.j));

        /// <summary>Столб, в зону которого попадает клетка (зоны не пересекаются).</summary>
        public Deployable ClaimAt((int i, int j) cell)
        {
            foreach (var d in Deployables)
                if (d.Kind == DeployKind.Claim && CellDist(ClaimCell(d), cell) <= ClaimRadius) return d;
            return null;
        }

        /// <summary>Можно ли поставить столб: зоны не должны сближаться ближе зазора.</summary>
        public bool CanPlaceClaim(Vector2 at, out string why)
        {
            why = "";
            var c = Building.CellOf(at);
            foreach (var d in Deployables)
                if (d.Kind == DeployKind.Claim && CellDist(ClaimCell(d), c) < ClaimGap) { why = "рядом другая база"; return false; }
            return true;
        }

        /// <summary>Нагрузка базы: B = фундаменты + 0,5·стены/проёмы/окна + 4·двери; проём с дверью считается дверью.</summary>
        public (float b, int found, int doors) ClaimLoad(Deployable claim)
        {
            float b = 0; int found = 0, doors = 0;
            foreach (var s in Structures)
            {
                if (s.ClaimId != claim.Id) continue;
                switch (s.Kind)
                {
                    case StructKind.Foundation: b += 1; found++; break;
                    case StructKind.Door: b += 4; doors++; break;
                    case StructKind.Doorway: if (!_doors.ContainsKey((s.I, s.J, s.Dir))) b += 0.5f; break;
                    default: b += 0.5f; break;
                }
            }
            return (b, found, doors);
        }

        /// <summary>Расход содержания в час по ресурсам: 4% цены текущих уровней × надбавка за размер.</summary>
        public Dictionary<string, float> UpkeepPerHourOf(Deployable claim)
        {
            var need = new Dictionary<string, float>();
            foreach (var s in Structures)
            {
                if (s.ClaimId != claim.Id) continue;
                foreach (var (id, n) in Building.Cost(s.Kind, s.Tier))
                    need[id] = need.GetValueOrDefault(id) + n * UpkeepPerHour;
            }
            float b = ClaimLoad(claim).b;
            float m = 1 + MathF.Pow(MathF.Max(0, b - 120) / 120f, 2);
            foreach (var k in new List<string>(need.Keys)) need[k] *= m;
            return need;
        }

        /// <summary>На сколько часов хватит запаса (минимум по ресурсам).</summary>
        public float ClaimHoursLeft(Deployable claim)
        {
            float hours = float.MaxValue;
            foreach (var (id, perHour) in UpkeepPerHourOf(claim))
                if (perHour > 0) hours = MathF.Min(hours, claim.Store.GetValueOrDefault(id) / perHour);
            return hours == float.MaxValue ? 0 : hours;
        }

        /// <summary>Проверка правил столба для нового элемента: своя зона, лимиты нагрузки, фундаментов и дверей.</summary>
        bool ClaimAllows(Actor a, StructKind kind, (int i, int j) cell, out Deployable claim, out string why)
        {
            why = "";
            claim = ClaimAt(cell);
            if (claim == null) { why = "нужен межевой столб рядом (зона 8 клеток)"; return false; }
            if (!claim.Auth.Contains(a.Id)) { why = "чужая зона"; return false; }
            var (b, found, doors) = ClaimLoad(claim);
            float add = kind == StructKind.Foundation ? 1 : kind == StructKind.Door ? 3.5f : 0.5f;   // дверь заменяет 0,5 проёма на 4
            if (b + add > BudgetMax) { why = $"бюджет базы исчерпан ({b:0}/{BudgetMax})"; return false; }
            if (kind == StructKind.Foundation && found + 1 > FoundMax) { why = $"фундаментов максимум {FoundMax}"; return false; }
            if (kind == StructKind.Door && doors + 1 > DoorMax) { why = $"дверей максимум {DoorMax}"; return false; }
            return true;
        }

        /// <summary>E по столбу: положить запас на содержание (до ~2 часов по каждому ресурсу).</summary>
        void UseClaim(Actor a, Deployable claim)
        {
            if (!claim.Auth.Contains(a.Id)) { claim.Auth.Add(a.Id); Info(a, "Ты вписан в столб: можно строить в его зоне"); return; }
            var need = UpkeepPerHourOf(claim);
            var parts = new List<string>();
            foreach (var (id, perHour) in need)
            {
                int want = (int)MathF.Ceiling(perHour * 2f - claim.Store.GetValueOrDefault(id));
                if (want <= 0) continue;
                int got = a.Take(id, want);
                if (got <= 0) continue;
                claim.Store[id] = claim.Store.GetValueOrDefault(id) + got;
                parts.Add($"{Items.Name(id)} {got}");
            }
            float h = ClaimHoursLeft(claim);
            Info(a, parts.Count > 0 ? $"В столб: {string.Join(", ", parts)} · хватит на {h:0.0} ч" : need.Count == 0 ? "Столб: базы ещё нет — содержать нечего" : $"Столб: запас на {h:0.0} ч");
        }

        /// <summary>Раз в секунду: столбы едят запас; без запаса — отсрочка 30 минут, потом гниение тех элементов, чей ресурс кончился.</summary>
        void StepClaims()
        {
            if (Tick % 30 != 0) return;
            foreach (var claim in Deployables)
            {
                if (claim.Kind != DeployKind.Claim) continue;
                var lacking = new HashSet<string>();
                foreach (var (id, perHour) in UpkeepPerHourOf(claim))
                {
                    float left = claim.Store.GetValueOrDefault(id) - perHour / 3600f;
                    if (left < 0) { lacking.Add(id); left = 0; }
                    claim.Store[id] = left;
                }
                if (lacking.Count == 0) { claim.Grace = 1800f; continue; }
                claim.Grace -= 1f;
                if (claim.Grace > 0) continue;
                foreach (var s in new List<Structure>(Structures))
                {
                    if (s.ClaimId != claim.Id) continue;
                    foreach (var (id, _) in Building.Cost(s.Kind, s.Tier))
                        if (lacking.Contains(id)) { DamageStructure(s, s.MaxHp * DecayPerMinute / 60f, -1); break; }
                }
            }
        }

        Structure NearestDoor(Vector2 p, float range)
        {
            Structure best = null;
            float bd = range;
            foreach (var d in _doors.Values)
            {
                float dist = Vector2.Distance(d.Center, p);
                if (dist < bd) { bd = dist; best = d; }
            }
            return best;
        }
    }
}
