using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Элементы базы. Фундамент — клетка 2×2 м; треугольный фундамент — равносторонний треугольник со стороной 2 м,
    /// приставленный к свободному краю любого пола. Стены, проёмы, окна и двери стоят на рёбрах полов (в том числе наклонных).
    /// Новые виды — только в конец (числа сохраняются в сейве и снапшоте).
    /// </summary>
    public enum StructKind { Foundation, Wall, Doorway, Window, Door, TriFoundation, Stairs, Hatch, LowWall }   // D60: лестница, люк, парапет (низкая стена)

    public sealed class Structure
    {
        public int Id;
        public StructKind Kind;
        public int I, J, Dir;          // квадратный фундамент — клетка; у рёбер сетки — тоже (для ботов и старых сейвов)
        public Vector2 A, B, C;        // ребро: A–B; треугольник: A–B — край, к которому приставлен, C — вершина
        public int Tier;               // 0 разметка, 1 дерево, 2 камень, 3 металл, 4 сталь
        public float Hp;
        public bool Open;              // дверь открыта; у окна — ставни закрыты (D54)
        public bool Shutters;          // на окне решётка со ставнями (D54)
        public bool Locked;            // на двери замок
        public bool KeyLock;           // замок с ключом (иначе кодовый)
        public int Code = -1;          // код кодового замка 0000–9999; -1 — владелец ещё не задал
        public readonly HashSet<int> LockAuth = new();   // кто ввёл верный код (как в Rust): открывают без повторного ввода
        public int LockOwner;          // кто поставил замок: ключевой открывает без ключа, кодовый — задаёт и меняет код
        public int OwnerId;
        public int ClaimId;            // к какому столбу привязан (бюджет и содержание)
        public readonly List<MapWall> Boxes = new();
        public int Floor;              // этаж (D60): 0 — земля (фундамент), 1.. — перекрытие и всё, что на нём; у лестницы — верхний этаж
        public bool Static;            // часть РТ (D61): неразрушима, не сохраняется (строится генератором мира), к ней не пристроиться
        public float MaxHp => Building.MaxHp(Kind, Tier);
        public bool IsFloor => Building.IsFloor(Kind);
        public bool IsEdge => !IsFloor;
        public Vector2 Center => Building.IsSquare(Kind) ? Building.CellCenter(I, J) : Kind == StructKind.TriFoundation ? (A + B + C) / 3f : (A + B) * 0.5f;
        public Vector2[] Poly => Building.IsSquare(Kind) ? Building.SquarePoly(I, J) : new[] { A, B, C };
        public long EdgeKey => Building.EdgeKey(A, B);
    }

    /// <summary>
    /// Геометрия и цены строительства. Общая для сервера и клиента: клиент по тем же правилам
    /// строит картинку и тени обзора из снапшота (id, вид, вершины, уровень).
    /// </summary>
    public static class Building
    {
        public const float Cell = 2f;          // клетка строительства (D21)
        public const float Thick = 0.3f;       // толщина стены
        public const float WallH = 2.6f;       // высота стены: закрывает обзор
        public const float WindowH = 1.0f;     // подоконник: укрытие, обзор и пули проходят
        public const float PostW = 0.5f;       // косяк дверного проёма
        public const float BuildRange = 6f;
        public const int MaxTier = 5;   // 5 — броня (D54): легированная сталь поверх стали
        public static readonly float TriHeight = Cell * MathF.Sqrt(3) / 2f;

        public static readonly string[] TierNames = { "каркас", "дерево", "камень", "металл", "сталь", Prog.ArmorTierName };
        static readonly float[] TierHp = { 10, 250, 500, 1000, 2000, Prog.ArmorTierHp };

        public static bool IsFloor(StructKind k) => k is StructKind.Foundation or StructKind.TriFoundation or StructKind.Stairs or StructKind.Hatch;
        /// <summary>Занимает клетку сетки целиком (квадратный пол, лестница, люк).</summary>
        public static bool IsSquare(StructKind k) => k is StructKind.Foundation or StructKind.Stairs or StructKind.Hatch;

        // ---------- этажи (D60) ----------
        public const float Storey = WallH;     // высота этажа = высота стены: стена второго этажа встаёт ровно на первую
        public const int MaxFloor = 3;         // 4 этажа: земля + 3 перекрытия
        public const float LowWallH = 1.0f;    // парапет: укрытие по пояс, обзор поверх
        public const float AirZ = 9f;          // выше этого — «воздух» (задел под вертолёты): над крышами, без стен и полов
        public static float FloorZ(int floor) => floor * Storey;
        public static int FloorOfZ(float z) => Math.Clamp((int)MathF.Floor((z + 0.3f) / Storey), 0, MaxFloor);

        /// <summary>Порядок элементов в колесе и при листании.</summary>
        public static readonly StructKind[] PieceOrder = { StructKind.Foundation, StructKind.TriFoundation, StructKind.Wall, StructKind.Doorway, StructKind.Window, StructKind.Door, StructKind.Stairs, StructKind.Hatch, StructKind.LowWall };

        public static StructKind NextPiece(StructKind k, int step = 1)
        {
            int i = Array.IndexOf(PieceOrder, k);
            return PieceOrder[((i < 0 ? 0 : i) + step + PieceOrder.Length) % PieceOrder.Length];
        }

        /// <summary>Центр элемента из снапшота.</summary>
        public static Vector2 Center(StructKind k, int i, int j, Vector2 a, Vector2 b, Vector2 c) =>
            IsSquare(k) ? CellCenter(i, j) : k == StructKind.TriFoundation ? (a + b + c) / 3f : (a + b) * 0.5f;

        public static string KindName(StructKind k) => k switch
        {
            StructKind.Foundation => "Фундамент",
            StructKind.TriFoundation => "Треугольный фундамент",
            StructKind.Wall => "Стена",
            StructKind.Doorway => "Дверной проём",
            StructKind.Window => "Окно",
            StructKind.Stairs => "Лестница",
            StructKind.Hatch => "Люк с лестницей",
            StructKind.LowWall => "Парапет",
            _ => "Дверь",
        };

        /// <summary>Название с учётом этажа: пол выше земли — перекрытие (D60).</summary>
        public static string KindName(StructKind k, int floor) =>
            floor > 0 && k == StructKind.Foundation ? "Перекрытие" : floor > 0 && k == StructKind.TriFoundation ? "Треугольное перекрытие" : KindName(k);

        public static float MaxHp(StructKind k, int tier) =>
            TierHp[Math.Clamp(tier, 0, MaxTier)] * (IsFloor(k) ? 1.5f : k == StructKind.Door ? 0.8f : k == StructKind.LowWall ? 0.6f : 1f);

        /// <summary>Цена установки (tier 0; дверь сразу деревянная) или улучшения до tier.</summary>
        public static (string id, int n)[] Cost(StructKind k, int tier)
        {
            float m = k is StructKind.Foundation or StructKind.Stairs ? 1.5f : k == StructKind.Hatch ? 1.3f : k == StructKind.TriFoundation ? 0.75f : k == StructKind.Window || k == StructKind.Doorway ? 0.7f : k == StructKind.LowWall ? 0.5f : 1f;
            int N(float v) => Math.Max(5, (int)MathF.Round(v * m / 5f) * 5);
            if (k == StructKind.Door)
                return tier <= 1 ? new[] { ("wood", 50) } : tier == 2 ? new[] { ("stone", 75) } : tier == 3 ? new[] { ("metal_fragments", 40) } : tier == 4 ? new[] { ("metal_fragments", 75), ("scrap", 10) } : Prog.ArmorDoorCost;
            if (tier >= 5) return Array.ConvertAll(Prog.ArmorWallCost, c => (c.Item1, Math.Max(1, (int)MathF.Round(c.Item2 * m))));   // бронеуровень (D54)
            // в 4–6 раз дешевле растовского: дерево даёт 120–200, камень 150 (2026-10-21) — дом 2×2 из дерева ≈ 4 дерева
            return tier switch
            {
                0 => new[] { ("wood", N(10)) },
                1 => new[] { ("wood", N(30)) },
                2 => new[] { ("stone", N(50)) },
                3 => new[] { ("metal_fragments", N(35)) },
                _ => new[] { ("metal_fragments", N(50)), ("scrap", N(5)) },
            };
        }

        /// <summary>Содержание считается от цены уровня; бронеуровень (сталь не кладут в шкаф) — металлом и ломом, как вдвое «стали» (D54).</summary>
        public static (string id, int n)[] UpkeepCost(StructKind k, int tier)
        {
            if (tier < 5) return Cost(k, tier);
            return Array.ConvertAll(Cost(k, 4), c => (c.id, c.n * 2));
        }

        /// <summary>Во сколько раз урон по постройке меньше/больше: инструментом — только слабые уровни, пулями — двери и разметка (D22).</summary>
        public static float MeleeMult(StructKind k, int tier) => tier switch { 0 => 1f, 1 => k == StructKind.Door ? 0.35f : 0.25f, _ => 0f };
        public static float BulletMult(StructKind k, int tier) =>
            tier == 0 ? 1f : k == StructKind.Door ? (tier == 1 ? 0.5f : tier == 2 ? 0.3f : tier == 3 ? 0.15f : tier == 4 ? 0.06f : Prog.ArmorDoorBulletMult) : 0f;

        public static (int i, int j) CellOf(Vector2 p) => ((int)MathF.Floor(p.X / Cell), (int)MathF.Floor(p.Y / Cell));
        public static Vector2 CellCenter(int i, int j) => new((i + 0.5f) * Cell, (j + 0.5f) * Cell);
        public static Vector2 EdgeCenter(int i, int j, int dir) =>
            dir == 0 ? new Vector2((i + 0.5f) * Cell, j * Cell) : new Vector2(i * Cell, (j + 0.5f) * Cell);

        /// <summary>Концы ребра сетки.</summary>
        public static (Vector2 a, Vector2 b) EdgeEnds(int i, int j, int dir) =>
            dir == 0 ? (new Vector2(i * Cell, j * Cell), new Vector2((i + 1) * Cell, j * Cell))
                     : (new Vector2(i * Cell, j * Cell), new Vector2(i * Cell, (j + 1) * Cell));

        public static Vector2[] SquarePoly(int i, int j) => new[]
        {
            new Vector2(i * Cell, j * Cell), new Vector2((i + 1) * Cell, j * Cell),
            new Vector2((i + 1) * Cell, (j + 1) * Cell), new Vector2(i * Cell, (j + 1) * Cell),
        };

        /// <summary>Клетки по обе стороны ребра сетки (для ботов).</summary>
        public static ((int, int) a, (int, int) b) EdgeCells(int i, int j, int dir) =>
            dir == 0 ? ((i, j - 1), (i, j)) : ((i - 1, j), (i, j));

        // ---------- ключи вершин и рёбер: квадраты и треугольники делят рёбра по совпадающим вершинам ----------

        /// <summary>Вершина, квантованная до 5 см (погрешность вычисления вершин треугольников не мешает совпадению).</summary>
        public static long VKey(Vector2 p) => ((long)(ushort)(short)MathF.Round(p.X * 20) << 16) | (ushort)(short)MathF.Round(p.Y * 20);

        public static long EdgeKey(Vector2 a, Vector2 b)
        {
            long ka = VKey(a), kb = VKey(b);
            return ka < kb ? (ka << 32) | kb : (kb << 32) | ka;
        }

        public static long EdgeKey(int i, int j, int dir) { var (a, b) = EdgeEnds(i, j, dir); return EdgeKey(a, b); }

        /// <summary>Вершина треугольника, приставленного к ребру a–b со стороны точки toward.</summary>
        public static Vector2 TriApex(Vector2 a, Vector2 b, Vector2 toward)
        {
            var mid = (a + b) * 0.5f;
            var d = Vector2.Normalize(b - a);
            var n = new Vector2(-d.Y, d.X);
            if (Vector2.Dot(toward - mid, n) < 0) n = -n;
            return mid + n * TriHeight;
        }

        public static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / MathF.Max(1e-6f, ab.LengthSquared()), 0, 1);
            return Vector2.Distance(p, a + ab * t);
        }

        public static bool PointInPoly(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                    p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) inside = !inside;
            return inside;
        }

        /// <summary>Пересекаются ли выпуклые многоугольники (касание по краю — не пересечение).</summary>
        public static bool PolysOverlap(Vector2[] a, Vector2[] b)
        {
            foreach (var poly in new[] { a, b })
                for (int i = 0; i < poly.Length; i++)
                {
                    var e = poly[(i + 1) % poly.Length] - poly[i];
                    var axis = new Vector2(-e.Y, e.X);
                    float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                    foreach (var v in a) { float d = Vector2.Dot(v, axis); amin = MathF.Min(amin, d); amax = MathF.Max(amax, d); }
                    foreach (var v in b) { float d = Vector2.Dot(v, axis); bmin = MathF.Min(bmin, d); bmax = MathF.Max(bmax, d); }
                    float eps = 0.06f * axis.Length();
                    if (amax <= bmin + eps || bmax <= amin + eps) return false;
                }
            return true;
        }

        /// <summary>Части элемента вдоль ребра: доли длины [t0, t1], высота, пропускает ли пули.</summary>
        public static List<(float t0, float t1, float h, bool pass)> Spans(StructKind k, bool open)
        {
            var list = new List<(float, float, float, bool)>();
            float post = PostW / Cell;
            switch (k)
            {
                case StructKind.Wall: list.Add((0, 1, WallH, false)); break;
                case StructKind.Doorway: list.Add((0, post, WallH, false)); list.Add((1 - post, 1, WallH, false)); break;
                case StructKind.Window: list.Add((0, 0.3f, WallH, false)); list.Add((0.7f, 1, WallH, false)); list.Add(open ? (0.3f, 0.7f, WallH, false) : (0.3f, 0.7f, WindowH, true)); break;   // окно: Open — ставни закрыты (D54)
                case StructKind.Door: if (!open) list.Add((post, 1 - post, WallH, false)); break;
                case StructKind.LowWall: list.Add((0, 1, LowWallH, false)); break;   // парапет (D60): низкое укрытие
            }
            return list;
        }

        /// <summary>
        /// Твёрдые части элемента на ребре a–b. Ребро по оси — одна рамка на часть; наклонное — цепочка мелких рамок
        /// по 0,25 м (столкновения, пули и обзор работают с прямоугольниками).
        /// </summary>
        public static List<(Vector2 min, Vector2 max, float h, bool passBullets)> Boxes(StructKind k, Vector2 a, Vector2 b, bool open)
        {
            var list = new List<(Vector2, Vector2, float, bool)>();
            float ht = Thick * 0.5f;
            bool axis = MathF.Abs(a.X - b.X) < 0.01f || MathF.Abs(a.Y - b.Y) < 0.01f;
            float len = Vector2.Distance(a, b);
            foreach (var (t0, t1, h, pass) in Spans(k, open))
            {
                int n = axis ? 1 : Math.Max(1, (int)MathF.Ceiling(len * (t1 - t0) / 0.25f));
                var dir = Vector2.Normalize(b - a);
                var nrm = new Vector2(-dir.Y, dir.X) * ht;   // толщина — только поперёк ребра (иначе косяки съедают проём)
                for (int s = 0; s < n; s++)
                {
                    var p0 = Vector2.Lerp(a, b, t0 + (t1 - t0) * s / n);
                    var p1 = Vector2.Lerp(a, b, t0 + (t1 - t0) * (s + 1) / n);
                    var mn = Vector2.Min(Vector2.Min(p0 + nrm, p0 - nrm), Vector2.Min(p1 + nrm, p1 - nrm));
                    var mx = Vector2.Max(Vector2.Max(p0 + nrm, p0 - nrm), Vector2.Max(p1 + nrm, p1 - nrm));
                    list.Add((mn, mx, h, pass));
                }
            }
            return list;
        }

        public static List<(Vector2 min, Vector2 max, float h, bool passBullets)> Boxes(StructKind k, int i, int j, int dir, bool open)
        {
            var (a, b) = EdgeEnds(i, j, dir);
            return Boxes(k, a, b, open);
        }
    }

    public sealed partial class World
    {
        public readonly List<Structure> Structures = new();
        // учёт элементов — свой на каждом этаже (D60, World.Floors.cs): поля ниже — этаж текущего контекста _cf (по умолчанию земля)
        Dictionary<(int, int), Structure> _foundations => _lv[_cf].Found;   // квадратные полы: фундамент, перекрытие, лестница (верх), люк
        List<Structure> _tris => _lv[_cf].Tris;
        Dictionary<long, Structure> _edges => _lv[_cf].Edges;
        Dictionary<long, Structure> _doors => _lv[_cf].Doors;
        /// <summary>Рёбра полов: концы и сколько полов их делит (1 — край базы, 2 — внутреннее).</summary>
        Dictionary<long, (Vector2 a, Vector2 b, int n)> _floorEdges => _lv[_cf].FloorEdges;
        readonly List<Actor> _qActors = new();

        /// <summary>Постройки поменялись (для клиента: пересобрать тени обзора и акустику).</summary>
        public int StructVersion;

        bool HasFoundation(int i, int j) => _foundations.ContainsKey((i, j));
        public Structure FoundationAt(int i, int j) => _foundations.GetValueOrDefault((i, j));
        public Structure EdgeAt(int i, int j, int dir) => _edges.GetValueOrDefault(Building.EdgeKey(i, j, dir));
        public Structure DoorAt(int i, int j, int dir) => _doors.GetValueOrDefault(Building.EdgeKey(i, j, dir));
        public Structure EdgeByKey(long key) => _edges.GetValueOrDefault(key);
        public Structure DoorByKey(long key) => _doors.GetValueOrDefault(key);

        /// <summary>Пол (квадрат или треугольник) под точкой.</summary>
        public Structure FloorAt(Vector2 p)
        {
            var (ci, cj) = Building.CellOf(p);
            if (_foundations.TryGetValue((ci, cj), out var f)) return f;
            foreach (var t in _tris) if (Vector2.DistanceSquared(t.Center, p) < 2.5f && Building.PointInPoly(p, t.Poly)) return t;
            return null;
        }

        IEnumerable<Structure> FloorsNear(Vector2 p, float r)
        {
            var (ci, cj) = Building.CellOf(p);
            int cr = (int)MathF.Ceiling(r / Building.Cell);
            for (int i = ci - cr; i <= ci + cr; i++)
                for (int j = cj - cr; j <= cj + cr; j++)
                    if (_foundations.TryGetValue((i, j), out var f)) yield return f;
            foreach (var t in _tris) if (Vector2.Distance(t.Center, p) < r + 1.5f) yield return t;
        }

        /// <summary>Учёт рёбер пола: +1 при постановке, −1 при сносе; ребро без полов уносит стоящие на нём стены и двери.</summary>
        void CountFloorEdges(Structure f, int delta, bool dropOrphans = true)
        {
            var poly = f.Poly;
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Length];
                long key = Building.EdgeKey(a, b);
                int n = (_floorEdges.TryGetValue(key, out var e) ? e.n : 0) + delta;
                if (n > 0) { _floorEdges[key] = (a, b, n); continue; }
                _floorEdges.Remove(key);
                if (!dropOrphans) continue;
                if (_doors.TryGetValue(key, out var door)) RemoveStructure(door);
                if (_edges.TryGetValue(key, out var piece)) RemoveStructure(piece);
            }
        }

        /// <summary>Ближайшее к точке ребро пола (не дальше maxDist).</summary>
        public bool NearestFloorEdge(Vector2 p, float maxDist, out Vector2 a, out Vector2 b, out long key, out int floors)
        {
            a = b = default; key = 0; floors = 0;
            float best = maxDist;
            foreach (var (k, e) in _floorEdges)
            {
                if (MathF.Abs(e.a.X - p.X) > 3 || MathF.Abs(e.a.Y - p.Y) > 3) continue;
                float d = Building.SegDist(p, e.a, e.b);
                if (d < best) { best = d; a = e.a; b = e.b; key = k; floors = e.n; }
            }
            return key != 0;
        }

        /// <summary>Сторона ребра, где пола нет (наружу), — единичная нормаль.</summary>
        public Vector2 OutwardOf(Vector2 a, Vector2 b)
        {
            var d = Vector2.Normalize(b - a);
            var n = new Vector2(-d.Y, d.X);
            var mid = (a + b) * 0.5f;
            return FloorAt(mid + n * 0.5f) == null ? n : -n;
        }

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
            var sq = Building.SquarePoly(ci, cj);
            foreach (var t in _tris) if (Vector2.Distance(t.Center, c) < 3 && Building.PolysOverlap(sq, t.Poly)) { why = "мешает треугольный фундамент"; return false; }
            foreach (var w in WallsNear(c, 1.5f))
                if (w.Owner == null && Geo.BoxOverlap(min, max, w.Wall.Min, w.Wall.Max)) { why = "мешает препятствие"; return false; }
            foreach (var n in NodesNear(c, 1.5f))
                if (Geo.CircleBox(n.Pos, n.Radius, min, max)) { why = "мешает дерево/камень"; return false; }
            foreach (var d in DeploysNear(c, 1.5f))
                if (d.Kind != DeployKind.Claim && d.FloorId == 0 && Geo.BoxOverlap(min, max, d.Min + new Vector2(0.05f), d.Max - new Vector2(0.05f))) { why = "мешает установленное"; return false; }
            return true;
        }

        /// <summary>Куда встанет треугольник: на ближайший свободный край пола, вершиной наружу.</summary>
        public bool TriSpot(Vector2 at, out Vector2[] tri, out string why)
        {
            tri = null; why = "";
            if (!NearestFloorEdge(at, 2.1f, out var a, out var b, out _, out int floors)) { why = "наведи на край фундамента"; return false; }
            if (floors >= 2) { why = "край занят: с обеих сторон пол"; return false; }
            var mid = (a + b) * 0.5f;
            var c = mid + OutwardOf(a, b) * Building.TriHeight;
            tri = new[] { a, b, c };
            var centroid = (a + b + c) / 3f;
            foreach (var f in FloorsNear(centroid, 2.5f)) if (Building.PolysOverlap(tri, f.Poly)) { why = "мешает другой фундамент"; return false; }
            foreach (var p in new[] { centroid, Vector2.Lerp(centroid, a, 0.8f), Vector2.Lerp(centroid, b, 0.8f), Vector2.Lerp(centroid, c, 0.8f) })
            {
                if (!Terrain.IsLand(p)) { why = "вода"; return false; }
                if (NearMonument(p, 8)) { why = "рядом с РТ нельзя"; return false; }
            }
            foreach (var w in WallsNear(centroid, 1.5f))
                if (w.Owner == null && Building.PolysOverlap(tri, new[] { w.Wall.Min, new Vector2(w.Wall.Max.X, w.Wall.Min.Y), w.Wall.Max, new Vector2(w.Wall.Min.X, w.Wall.Max.Y) })) { why = "мешает препятствие"; return false; }
            foreach (var n in NodesNear(centroid, 1.5f))
                if (Building.PointInPoly(n.Pos, tri) || Building.SegDist(n.Pos, a, c) < n.Radius || Building.SegDist(n.Pos, b, c) < n.Radius) { why = "мешает дерево/камень"; return false; }
            foreach (var d in DeploysNear(centroid, 1.5f))
                if (d.Kind != DeployKind.Claim && d.FloorId == 0 && Building.PolysOverlap(tri, Footprints.RectPoly(d.Min, d.Max))) { why = "мешает установленное"; return false; }
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
            foreach (var (id, n) in cost) { a.Take(id, n); if (a.Team == Team.Player) Economy.Out("стройка", id, n); }
        }

        /// <summary>Клетка столба для элемента на ребре: со стороны пола.</summary>
        (int, int) EdgeOwnerCell(Vector2 a, Vector2 b)
        {
            var mid = (a + b) * 0.5f;
            var d = Vector2.Normalize(b - a);
            var n = new Vector2(-d.Y, d.X);
            var side = FloorAt(mid + n * 0.5f) != null ? mid + n * 0.5f : mid - n * 0.5f;
            return Building.CellOf(FloorAt(side)?.Center ?? side);
        }

        /// <summary>Можно ли поставить элемент в эту точку; причина отказа — для подсказки.</summary>
        public bool CanBuild(Actor a, StructKind kind, Vector2 at, out string why)
        {
            why = "";
            if (Vector2.Distance(a.Pos, at) > Building.BuildRange) { why = "далеко"; return false; }
            if (NearMonument(at, 2)) { why = "на РТ строить нельзя"; return false; }
            // этажи (D60): боты строят только на земле; игрок — на свой этаж или этажом выше (BuildLevel)
            int level = a.IsBot ? 0 : BuildLevel(a, kind, at);
            if (level > Building.MaxFloor) { why = $"выше {Building.MaxFloor + 1} этажей нельзя"; return false; }
            if (level == 0 && kind is StructKind.Stairs or StructKind.Hatch) { why = "нужен пол"; return false; }
            using var ctx = Ctx(level);
            if (level > 0) return CanBuildUpper(a, kind, at, out why);
            if (kind == StructKind.Foundation)
            {
                var (ci, cj) = Building.CellOf(at);
                if (!FoundationGeoOk(ci, cj, out why)) return false;
                return ClaimAllows(a, kind, (ci, cj), out _, out why);
            }
            if (kind == StructKind.TriFoundation)
            {
                if (!TriSpot(at, out var tri, out why)) return false;
                return ClaimAllows(a, kind, Building.CellOf((tri[0] + tri[1] + tri[2]) / 3f), out _, out why);
            }
            return CanBuildEdge(a, kind, at, out why);
        }

        /// <summary>Элемент на ребре пола текущего этажа (контекст): стена, проём, окно, дверь, парапет.</summary>
        bool CanBuildEdge(Actor a, StructKind kind, Vector2 at, out string why)
        {
            why = "";
            if (!NearestFloorEdge(at, 1.3f, out var ea, out var eb, out long key, out _)) { why = _cf > 0 ? "нужен край перекрытия" : "нужен край фундамента"; return false; }
            if (kind == StructKind.Door)
            {
                if (!_edges.TryGetValue(key, out var frame) || frame.Kind != StructKind.Doorway) { why = "нужен дверной проём"; return false; }
                if (_doors.ContainsKey(key)) { why = "дверь уже есть"; return false; }
            }
            else if (_edges.ContainsKey(key)) { why = "занято"; return false; }
            if (kind == StructKind.Doorway && DoorwayBlocked(ea, eb)) { why = "перед проёмом стоит установленное"; return false; }
            foreach (var (min, max, _, _) in Building.Boxes(kind, ea, eb, false))
                foreach (var o in Actors)
                    if (!o.Dead && !o.Spectator && o.Floor == _cf && Geo.CircleBox(o.Pos, o.Radius, min, max)) { why = "мешает человек"; return false; }
            return ClaimAllows(a, kind, EdgeOwnerCell(ea, eb), out _, out why);
        }

        /// <summary>
        /// Что на самом деле поставит План (людям): курсор на пустом дверном проёме — дверь;
        /// остальное — ровно то, что выбрано (стена вместо фундамента больше не подставляется).
        /// </summary>
        public StructKind EffectivePiece(Actor a, Vector2 at)
        {
            var k = a.BuildPiece;
            if (a.IsBot) return k;
            for (int f = a.Floor; f <= Math.Min(a.Floor + 1, Building.MaxFloor); f++)   // этажи (D60): свой этаж и этаж выше
            {
                using var ctx = Ctx(f);
                if (NearestFloorEdge(at, 1.0f, out _, out _, out long key, out _) && _edges.TryGetValue(key, out var e) && e.Kind == StructKind.Doorway && !_doors.ContainsKey(key)) return StructKind.Door;
            }
            return k;
        }

        /// <summary>Призрак: многоугольник пола или ребро, куда встанет элемент.</summary>
        public (Vector2[] poly, Vector2 a, Vector2 b) GhostOf(StructKind kind, Vector2 at)
        {
            if (kind == StructKind.Foundation) { var (ci, cj) = Building.CellOf(at); return (Building.SquarePoly(ci, cj), default, default); }
            if (kind == StructKind.TriFoundation)
            {
                TriSpot(at, out var tri, out _);
                if (tri == null && NearestFloorEdge(at, 1.8f, out var ta, out var tb, out _, out _)) tri = new[] { ta, tb, Building.TriApex(ta, tb, at) };
                return (tri, default, default);
            }
            if (NearestFloorEdge(at, 1.3f, out var a, out var b, out _, out _)) return (null, a, b);
            return (null, default, default);
        }

        void TryBuild(Actor a, Vector2 at)
        {
            var kind = EffectivePiece(a, at);
            if (!CanBuild(a, kind, at, out string why)) { Info(a, $"{Building.KindName(kind)}: {why}"); return; }
            int tier = kind == StructKind.Door ? 1 : 0;
            var cost = Building.Cost(kind, tier);
            if (!CanPay(a, cost)) { Info(a, $"Не хватает: {CostText(cost)}"); return; }
            Pay(a, cost);
            int level = a.IsBot ? 0 : BuildLevel(a, kind, at);
            using var ctx = Ctx(level);
            var s = new Structure { Id = NextId(), Kind = kind, Dir = -1, Tier = tier, OwnerId = a.Id, Floor = level };
            if (Building.IsSquare(kind))
            {
                (s.I, s.J) = Building.CellOf(at);
                if (kind == StructKind.Stairs) s.Dir = StairDirFrom(a.Pos, (s.I, s.J));   // поднимается от того, кто ставит
                _foundations[(s.I, s.J)] = s;
            }
            else if (kind == StructKind.TriFoundation)
            {
                TriSpot(at, out var tri, out _);
                (s.A, s.B, s.C) = (tri[0], tri[1], tri[2]);
                _tris.Add(s);
            }
            else
            {
                NearestFloorEdge(at, 1.3f, out s.A, out s.B, out long key, out _);
                if (kind == StructKind.Door) _doors[key] = s; else _edges[key] = s;
            }
            s.Hp = s.MaxHp;
            s.ClaimId = ClaimAt(s.IsFloor ? Building.CellOf(s.Center) : EdgeOwnerCell(s.A, s.B))?.Id ?? 0;
            Structures.Add(s);
            if (s.IsFloor) { CountFloorEdges(s, +1); AdoptDeploysOnFloor(s); }
            RebuildBoxes(s);
            StructVersion++;
            Events.Add(new SimEvent { Kind = SimEventKind.Built, ActorId = a.Id, OtherId = s.Id, Pos = s.Center, Z = Building.FloorZ(level), Text = Building.KindName(kind, level) });
        }

        void RebuildBoxes(Structure s)
        {
            foreach (var w in s.Boxes) { if (w.Floor == 0) Walls.Remove(w); GridOf(w.Floor).Remove(w); }
            s.Boxes.Clear();
            if (s.Kind == StructKind.Stairs) { AddStairBoxes(s); return; }   // перила и низ лестницы — на двух этажах (D60)
            if (!s.IsEdge) return;
            foreach (var (min, max, h, pass) in Building.Boxes(s.Kind, s.A, s.B, s.Open))
            {
                var w = new MapWall { Wall = new Wall(min, max, h), Kind = WallKind.Built, Owner = s, PassBullets = pass, Floor = s.Floor };
                s.Boxes.Add(w);
                if (s.Floor == 0) Walls.Add(w);   // стены верхних этажей — только в сетке своего этажа
                GridOf(s.Floor).Insert(w, min, max);
            }
        }

        void RemoveStructure(Structure s, bool quiet = false)
        {
            if (!Structures.Remove(s)) return;
            _removeDepth++;
            using var ctx = Ctx(s.Floor);
            foreach (var w in s.Boxes) { if (w.Floor == 0) Walls.Remove(w); GridOf(w.Floor).Remove(w); }
            s.Boxes.Clear();
            StructVersion++;
            if (!quiet) Events.Add(new SimEvent { Kind = SimEventKind.Destroyed, OtherId = s.Id, Pos = s.Center, Z = Building.FloorZ(s.Floor), Text = Building.KindName(s.Kind, s.Floor), Value = s.Tier });
            switch (s.Kind)
            {
                case StructKind.Foundation:
                case StructKind.Stairs:
                case StructKind.Hatch:
                    DropSlotsOf(s);
                    _foundations.Remove((s.I, s.J));
                    CountFloorEdges(s, -1);   // рёбра, у которых не осталось пола ни с одной стороны, падают
                    break;
                case StructKind.TriFoundation:
                    DropSlotsOf(s);
                    _tris.Remove(s);
                    CountFloorEdges(s, -1);
                    break;
                case StructKind.Door:
                    _doors.Remove(s.EdgeKey);
                    break;
                default:
                    _edges.Remove(s.EdgeKey);
                    if (_doors.TryGetValue(s.EdgeKey, out var door)) RemoveStructure(door);
                    break;
            }
            if (s.IsFloor && s.Floor > 0) DropFromPlate(s);   // установленное и сумки с этого перекрытия — этажом ниже
            _removeDepth--;
            if (_removeDepth == 0) SettleStructures();   // без опоры — обрушение (D60)
        }

        /// <summary>Пересобрать учёт рёбер полов (после загрузки) — на всех этажах.</summary>
        void RebuildFloorEdges()
        {
            for (int f = 0; f <= Building.MaxFloor; f++)
            {
                using var ctx = Ctx(f);
                _floorEdges.Clear();
                foreach (var fl in _foundations.Values) CountFloorEdges(fl, +1, false);
                foreach (var t in _tris) CountFloorEdges(t, +1, false);
            }
        }

        /// <summary>Урон постройке. Возвращает реально нанесённый урон (0 — уровень слишком прочный для этого оружия).</summary>
        public float DamageStructure(Structure s, float dmg, int attackerId)
        {
            if (dmg <= 0 || s.Static || !Structures.Contains(s)) return 0;   // постройки РТ не ломаются (D61)
            s.Hp -= dmg;
            // столб помнит рейд: боты-хозяева решают — прятаться или идти чинить
            if (attackerId >= 0 && s.ClaimId != 0)
                foreach (var c in Deployables)
                    if (c.Id == s.ClaimId) { c.RaidedAt = Time; c.RaiderId = attackerId; break; }
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
            // этажи (D60): постройки своего этажа; присев (Alt) — перекрытие над головой
            if (a.Crouching && !a.IsBot && a.Floor < Building.MaxFloor && PlateAt(a.Floor + 1, hit) is Structure ceil && Vector2.Distance(a.Pos, ceil.Center) < reach + 1.6f) return ceil;
            using var ctx = Ctx(a.Floor);
            Structure best = null;
            float bestD = 0.75f;   // дальше от ребра — значит целимся в пол (фундамент)
            foreach (var d in _doors.Values)
                if (Vector2.DistanceSquared(d.Center, hit) < 9 && DistToEdge(d, hit) < 0.85f) return d;   // дверь — только если целимся почти в неё
            if (FloorAt(hit) != null) bestD = 0.45f;   // целимся в пол (у треугольника всё близко к рёбрам) — стена только вплотную
            foreach (var e in _edges.Values)
            {
                if (Vector2.DistanceSquared(e.Center, hit) > 9) continue;
                float dd = DistToEdge(e, hit);
                if (dd < bestD) { bestD = dd; best = e; }
            }
            if (best != null) return best;
            var f = FloorAt(hit);
            return f != null && Vector2.Distance(a.Pos, f.Center) < reach + 1.6f ? f : null;
        }

        internal static float DistToEdge(Structure e, Vector2 p) => Building.SegDist(p, e.A, e.B);

        /// <summary>Молоток: улучшить на уровень или починить.</summary>
        void HammerHit(Actor a, Structure s)
        {
            // режим киянки: улучшить сразу до выбранного уровня или снести; уже на этом уровне (или выше) — починить
            bool modeRepair = false;
            if (a.HammerMode != 0)
            {
                int m = a.HammerMode;
                modeRepair = m != HammerDemolish && s.Tier >= m;
                if (!modeRepair) { HammerWheel(a, s.Id, m); return; }
                if (s.Hp >= s.MaxHp - 0.5f) { Info(a, $"{Building.KindName(s.Kind)}: уже {Building.TierNames[s.Tier]}, цел"); return; }
            }
            if (s.Hp < s.MaxHp - 0.5f && (modeRepair || s.Tier >= Building.MaxTier || !CanPay(a, Building.Cost(s.Kind, s.Tier + 1))))
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

        // ---------- колесо киянки (ПКМ): улучшить сразу до уровня, снести ----------

        public const int HammerDemolish = 9;      // пункт колеса «Снести» (1–4 — улучшить до уровня)
        public const float HammerWheelRange = 5f; // от персонажа до центра постройки

        public Structure StructById(int id)
        {
            if (id <= 0) return null;
            foreach (var s in Structures) if (s.Id == id) return s;
            return null;
        }

        /// <summary>Право стройки на постройку: шкаф, к которому она привязана, и ты в нём вписан.</summary>
        public bool HasBuildPrivilege(Actor a, Structure s)
        {
            Deployable claim = null;
            foreach (var d in Deployables) if (d.Kind == DeployKind.Claim && d.Id == s.ClaimId) { claim = d; break; }
            claim ??= ClaimAt(s.IsFloor ? Building.CellOf(s.Center) : EdgeOwnerCell(s.A, s.B));
            return claim == null || claim.Auth.Contains(a.Id);   // без шкафа — может любой (как в Расте)
        }

        /// <summary>Возврат за снос: половина цены текущего уровня (минимум 1 каждого ресурса).</summary>
        public static (string id, int n)[] DemolishRefund(Structure s) =>
            Array.ConvertAll(Building.Cost(s.Kind, s.Tier), c => (c.id, Math.Max(1, c.n / 2)));

        /// <summary>
        /// Почему пункт колеса киянки сейчас нельзя выбрать ("" — можно). Улучшить — только вверх и в зоне своего шкафа;
        /// снести — только своё, в зоне своего шкафа и если на элементе ничего не держится
        /// (стена на краю пола без соседнего пола, дверь в проёме, установленное на полу).
        /// </summary>
        public string HammerWhy(Actor a, Structure s, int op)
        {
            if (s == null || !Structures.Contains(s)) return "постройки нет";
            if (s.Static) return "это РТ — не твоя постройка";
            if (Vector2.Distance(a.Pos, s.Center) > HammerWheelRange) return "далеко";
            using var ctx = Ctx(s.Floor);
            if (!HasBuildPrivilege(a, s)) return "чужой шкаф — впишись в него";
            if (op >= 1 && op <= Building.MaxTier)
            {
                if (op <= s.Tier) return op == s.Tier ? "уже этот уровень" : "ниже текущего уровня";
                if (!CanPay(a, Building.Cost(s.Kind, op))) return "не хватает ресурсов";
                return "";
            }
            if (op != HammerDemolish) return "нет такого действия";
            if (s.OwnerId != a.Id) return "сносить можно только своё";
            if (s.Kind == StructKind.Doorway && _doors.ContainsKey(s.EdgeKey)) return "в проёме дверь — сначала снеси её";
            if (s.IsFloor)
            {
                foreach (var d in Deployables) if (d.FloorId == s.Id) return "на полу стоит установленное — убери его";
                var poly = s.Poly;
                for (int i = 0; i < poly.Length; i++)
                {
                    long key = Building.EdgeKey(poly[i], poly[(i + 1) % poly.Length]);
                    bool alone = !_floorEdges.TryGetValue(key, out var fe) || fe.n <= 1;
                    if (alone && (_edges.ContainsKey(key) || _doors.ContainsKey(key))) return "на краю стоит стена — сначала снеси её";
                }
                if (s.Floor < Building.MaxFloor && _lv[s.Floor + 1].Found.TryGetValue((s.I, s.J), out var up) && up.Kind == StructKind.Stairs && Building.IsSquare(s.Kind))
                    return "на полу стоит лестница — сначала снеси её";
            }
            if (WouldCollapse(s) is Structure victim) return $"держит {Building.KindName(victim.Kind, victim.Floor).ToLower()} — сначала сними верх";   // этажи (D60)
            return "";
        }

        /// <summary>Что сделает ЛКМ киянкой в этом режиме по этой постройке: подпись и можно ли.</summary>
        public (string text, bool ok) HammerModeInfo(Actor a, Structure s, int mode)
        {
            if (mode == HammerDemolish)
            {
                string w = HammerWhy(a, s, mode);
                return w == "" ? ($"ЛКМ — снести (вернёт {CostText(DemolishRefund(s))})", true) : ($"снести нельзя: {w}", false);
            }
            if (s.Tier >= mode)
            {
                if (s.Hp >= s.MaxHp - 0.5f) return ($"уже {Building.TierNames[s.Tier]}", false);
                var full = Building.Cost(s.Kind, s.Tier);
                var cost = Array.ConvertAll(full, c => (c.id, Math.Max(1, c.n / 5)));
                return CanPay(a, cost) ? ($"ЛКМ — починить ({CostText(cost)})", true) : ($"на ремонт не хватает: {CostText(cost)}", false);
            }
            string why = HammerWhy(a, s, mode);
            return why == "" ? ($"ЛКМ — в {Building.TierNames[mode]} ({CostText(Building.Cost(s.Kind, mode))})", true)
                             : ($"в {Building.TierNames[mode]} нельзя: {why}", false);
        }

        public string HammerModeText(Actor a, Structure s, int mode) => HammerModeInfo(a, s, mode).text;

        // ---------- режим киянки «забрать»: поднять установленное (верстак, печь, ящик…) обратно в инвентарь ----------

        public const int HammerTake = 8;          // режим колёсика: забрать установленное

        /// <summary>Установленное под прицелом киянки (не дальше досягаемости).</summary>
        public Deployable TargetDeploy(Actor a, float reach)
        {
            var toAim = a.LastAim - a.Pos;
            float dl = toAim.Length();
            var hit = dl > 0.05f ? a.Pos + toAim / dl * MathF.Min(dl, reach + 0.6f) : a.Pos + a.FacingDir * MathF.Min(reach, 1.3f);
            Deployable best = null;
            float bestD = 0.5f, bestC = float.MaxValue;   // от точки прицела до края хитбокса; внутри нескольких — ближайший центр
            foreach (var d in Deployables)
            {
                if (d.Kind == DeployKind.Recycler || Vector2.DistanceSquared(d.Pos, hit) > 9) continue;
                var half = d.Half != default ? d.Half : new Vector2(d.Radius, d.Radius);
                float dd = Vector2.Max(Vector2.Abs(hit - d.Pos) - half, Vector2.Zero).Length();
                float dc = Vector2.DistanceSquared(d.Pos, hit);
                if (dd < bestD || (dd == bestD && dc < bestC)) { bestD = dd; bestC = dc; best = d; }
            }
            return best;
        }

        /// <summary>Каким предметом станет установленное, если его забрать.</summary>
        public static string DeployItemId(Deployable d)
        {
            foreach (var id in Items.AllIds())
                if (Items.Get(id) is ItemDef def && def.Deploy == d.Kind && (d.Kind != DeployKind.Workbench || Items.BenchLevel(id) == d.Level)) return id;
            return null;
        }

        /// <summary>Почему нельзя забрать ("" — можно): только в зоне своего шкафа (без шкафа — только своё) и пустое.</summary>
        public string TakeWhy(Actor a, Deployable d)
        {
            if (d == null) return "наведи на установленное";
            if (Vector2.Distance(a.Pos, d.Pos) > HammerWheelRange) return "далеко";
            if (DeployItemId(d) == null) return "это не забрать";
            var claim = d.Kind == DeployKind.Claim ? d : ClaimAt(Building.CellOf(d.Pos));
            if (claim != null ? !claim.Auth.Contains(a.Id) : d.OwnerId != a.Id) return "шкаф не твой";
            if (d.Kind == DeployKind.Claim)
                foreach (var s in Structures) if (s.ClaimId == d.Id) return "к шкафу привязаны постройки";
            if (d.Storage != null && d.Storage.Items.Exists(x => x != null && x.Count > 0)) return "внутри вещи — сначала выложи";
            if (d.Ore > 0 || d.SulfurOre > 0 || d.Fuel > 0.01f || d.OutMetal > 0 || d.OutSulfur > 0 || d.OutCharcoal > 0) return "внутри что-то осталось — выгреби";
            return "";
        }

        /// <summary>Забрать установленное: предмет — в инвентарь, место освобождается (можно переставить).</summary>
        void TakeDeploy(Actor a, Deployable d)
        {
            string why = TakeWhy(a, d);
            if (why != "") { Info(a, d == null ? why : $"{Items.Name(DeployItemId(d) ?? "")}: {why}"); return; }
            string id = DeployItemId(d);
            RemoveDeployable(d);
            a.Add(id, 1);
            Info(a, $"Забрано: {Items.Name(id)}");
        }

        /// <summary>Пункт колеса киянки выбран: улучшить постройку сразу до уровня или снести с возвратом половины цены.</summary>
        void HammerWheel(Actor a, int structId, int op)
        {
            if (a.Held?.Id != "tool_hammer") return;
            var s = StructById(structId);
            string why = HammerWhy(a, s, op);
            if (why != "") { Info(a, s == null ? why : $"{Building.KindName(s.Kind)}: {why}"); return; }
            if (op == HammerDemolish)
            {
                var refund = DemolishRefund(s);
                foreach (var (id, n) in refund) a.Add(id, n);
                if (s.Kind == StructKind.Door && s.Locked) a.Add(s.KeyLock ? "lock_key" : "lock_code", 1);   // замок — обратно в руки
                Events.Add(new SimEvent { Kind = SimEventKind.Demolished, ActorId = a.Id, OtherId = s.Id, Pos = s.Center, Value = s.Tier, Text = Building.KindName(s.Kind) });
                RemoveStructure(s, quiet: true);
                Info(a, $"Снесено: {Building.KindName(s.Kind)} · возврат {CostText(refund)}");
                return;
            }
            Pay(a, Building.Cost(s.Kind, op));
            s.Tier = op;
            s.Hp = s.MaxHp;
            StructVersion++;
            Events.Add(new SimEvent { Kind = SimEventKind.Built, ActorId = a.Id, OtherId = s.Id, Pos = s.Center, Text = Building.TierNames[s.Tier], Value = s.Tier });
        }

        /// <summary>ЛКМ с замком в руках по двери: поставить замок. Кодовый — владелец сразу задаёт код в окне (бот — сам себе).</summary>
        void InstallLock(Actor a)
        {
            var door = TargetStructure(a, 2f);
            if (door == null || door.Kind != StructKind.Door) { Info(a, "Замок ставится на дверь — подойди и наведи на неё"); return; }
            if (door.Locked) { Info(a, "На двери уже есть замок"); return; }
            door.Locked = true;
            var held = a.Held;
            door.KeyLock = held.Id == "lock_key";
            door.LockOwner = a.Id;
            door.LockAuth.Clear();
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
            door.LockAuth.Add(a.Id);
            if (a.IsBot) { door.Code = Rng.Next(0, 10000); return; }   // бот свой код «помнит»: он в LockAuth
            door.Code = -1;
            Info(a, "Кодовый замок поставлен — задай код");
            LockPrompt(a, door, 1);
        }

        /// <summary>Личное событие окну кода: 0 — ввести код, 1 — задать (владелец), 2 — код принят, -2 — неверный.</summary>
        void LockPrompt(Actor a, Structure door, int mode) =>
            Events.Add(new SimEvent { Kind = SimEventKind.Locked, ActorId = a.Id, OtherId = door.Id, Pos = door.Center, Value = mode });

        /// <summary>Alt+E владельца по своей двери с кодовым замком — сменить код.</summary>
        bool OwnerCodePrompt(Actor a, Structure door)
        {
            if (!door.Locked || door.KeyLock || door.LockOwner != a.Id) return false;
            LockPrompt(a, door, 1);
            return true;
        }

        /// <summary>
        /// Ввод кода у двери. Владелец замка — задаёт/меняет код (список допущенных сбрасывается);
        /// остальные: верный — доступ навсегда и дверь открывается; неверный — удар током.
        /// </summary>
        void EnterCode(Actor a, int doorId, int code)
        {
            if (doorId < 0) { EnterConsoleCode(a, doorId, code); return; }   // пульт РТ тира 2 (Радиоцентр)
            Structure door = null;
            foreach (var s in Structures) if (s.Id == doorId) { door = s; break; }
            if (door == null || !door.Locked || door.KeyLock || code is < 0 or > 9999 || Vector2.Distance(door.Center, a.Pos) > 3f) return;
            if (door.LockOwner == a.Id)
            {
                bool first = door.Code < 0;
                door.Code = code;
                door.LockAuth.Clear();
                door.LockAuth.Add(a.Id);
                StructVersion++;
                Info(a, first ? "Код задан" : "Код сменён — доступ остальных сброшен");
                LockPrompt(a, door, 2);
                return;
            }
            if (door.Code >= 0 && code == door.Code)
            {
                door.LockAuth.Add(a.Id);
                Info(a, "Код принят");
                LockPrompt(a, door, 2);
                if (!door.Open) ToggleDoor(a, door);
            }
            else
            {
                Damage(a, 5, -1, a.Pos);
                Info(a, "Неверный код — бьёт током");
                LockPrompt(a, door, -2);
            }
        }

        /// <summary>E по двери: открыть/закрыть. Закрыть нельзя, если в проёме кто-то стоит.</summary>
        /// <summary>Бот открывает или закрывает конкретную дверь (рукой: рядом и без стены между).</summary>
        /// <summary>Бот меняет свой замок на ключе на кодовый у двери (игрок делает то же сносом двери киянкой). Ключ и старый замок — обратно.</summary>
        public bool BotSwapLock(Actor a, Structure door)
        {
            if (!Structures.Contains(door) || !door.Locked || !door.KeyLock || a.Count("lock_code") <= 0) return false;
            if (door.LockOwner != a.Id && a.Count("key_" + door.Id) <= 0) return false;
            if (Vector2.Distance(a.Pos, door.Center) > InteractRange + 0.4f) return false;
            a.Take("lock_code", 1);
            a.Take("key_" + door.Id, a.Count("key_" + door.Id));
            a.Add("lock_key", 1);
            door.KeyLock = false;
            door.LockOwner = a.Id;
            door.LockAuth.Clear();
            door.LockAuth.Add(a.Id);
            door.Code = Rng.Next(0, 10000);
            StructVersion++;
            return true;
        }

        public bool BotToggleDoor(Actor a, Structure door)
        {
            if (!Structures.Contains(door) || Vector2.Distance(a.Pos, door.Center) > InteractRange + 0.4f || !ClearLine(a.Pos, door.Center, door, true)) return false;
            bool was = door.Open;
            ToggleDoor(a, door);
            return door.Open != was;
        }

        void ToggleDoor(Actor a, Structure door)
        {
            if (door.Locked && door.KeyLock)
            {
                if (door.LockOwner != a.Id && a.Count("key_" + door.Id) <= 0) { Info(a, "Заперто на ключ"); Events.Add(new SimEvent { Kind = SimEventKind.Door, ActorId = a.Id, OtherId = door.Id, Pos = door.Center, Value = -1 }); return; }
            }
            else if (door.Locked && door.Code < 0 && door.LockOwner == a.Id && !a.IsBot) { LockPrompt(a, door, 1); return; }   // код ещё не задан — сначала задать
            else if (door.Locked && !door.LockAuth.Contains(a.Id))
            {
                LockPrompt(a, door, 0);
                return;
            }
            if (door.Open)
                foreach (var (min, max, _, _) in Building.Boxes(StructKind.Door, door.A, door.B, false))
                    foreach (var o in Actors)
                        if (!o.Dead && o.Floor == door.Floor && Geo.CircleBox(o.Pos, o.Radius, min, max)) { Info(a, "В проёме кто-то стоит"); return; }
            door.Open = !door.Open;
            RebuildBoxes(door);
            StructVersion++;
            Events.Add(new SimEvent { Kind = SimEventKind.Door, ActorId = a.Id, OtherId = door.Id, Pos = door.Center, Value = door.Open ? 1 : 0 });
        }

        // ---------- межевой столб: зона, бюджет, содержание (комбинация Астры, docs/gpt/05-stability.md) ----------

        public const int ClaimRadius = 8;        // клеток: зона — круг (по центрам клеток, di²+dj² ≤ R²+R)
        public const int ClaimGap = 2 * ClaimRadius + 1;   // минимум между шкафами (клеток, по кругу): зоны не пересекаются
        public const float BudgetMax = 360, FoundMax = 160, DoorMax = 48;
        const float UpkeepPerHour = 0.04f;       // доля цены текущих элементов
        const float DecayPerMinute = 0.01f;      // доля максимального HP

        static (int, int) ClaimCell(Deployable c) => Building.CellOf(c.Pos);

        static int CellDist((int i, int j) a, (int i, int j) b) => Math.Max(Math.Abs(a.i - b.i), Math.Abs(a.j - b.j));

        /// <summary>Клетка в круглой зоне шкафа (смещение от клетки шкафа в клетках).</summary>
        public static bool InClaimZone(int di, int dj) => di * di + dj * dj <= ClaimRadius * ClaimRadius + ClaimRadius;

        /// <summary>Столб, в зону которого попадает клетка (зоны не пересекаются).</summary>
        public Deployable ClaimAt((int i, int j) cell)
        {
            foreach (var d in Deployables)
                if (d.Kind == DeployKind.Claim && ClaimCell(d) is var c && InClaimZone(cell.i - c.Item1, cell.j - c.Item2)) return d;
            return null;
        }

        /// <summary>Можно ли поставить столб: зоны не должны сближаться ближе зазора.</summary>
        public bool CanPlaceClaim(Vector2 at, out string why)
        {
            why = "";
            var c = Building.CellOf(at);
            foreach (var d in Deployables)
                if (d.Kind == DeployKind.Claim && ClaimCell(d) is var o && (c.Item1 - o.Item1) * (c.Item1 - o.Item1) + (c.Item2 - o.Item2) * (c.Item2 - o.Item2) < ClaimGap * ClaimGap)
                { why = "рядом чужая зона — отойди дальше"; return false; }
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
                    case StructKind.TriFoundation: b += 0.5f; found++; break;
                    case StructKind.Door: b += 4; doors++; break;
                    case StructKind.Doorway: if (!_doors.ContainsKey(s.EdgeKey)) b += 0.5f; break;
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
                foreach (var (id, n) in Building.UpkeepCost(s.Kind, s.Tier))
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
            if (claim == null) return true;   // шкаф не обязателен: без него строить можно, он — приват (чужим нельзя, лимиты базы)
            if (!claim.Auth.Contains(a.Id)) { why = "чужая зона"; return false; }
            var (b, found, doors) = ClaimLoad(claim);
            float add = kind == StructKind.Foundation ? 1 : kind == StructKind.Door ? 3.5f : 0.5f;   // дверь заменяет 0,5 проёма на 4; треугольник — полфундамента
            if (b + add > BudgetMax) { why = $"бюджет базы исчерпан ({b:0}/{BudgetMax})"; return false; }
            if (Building.IsFloor(kind) && found + 1 > FoundMax) { why = $"фундаментов максимум {FoundMax}"; return false; }
            if (kind == StructKind.Door && doors + 1 > DoorMax) { why = $"дверей максимум {DoorMax}"; return false; }
            return true;
        }

        /// <summary>E по столбу: положить запас на содержание (до ~2 часов по каждому ресурсу).</summary>
        void UseClaim(Actor a, Deployable claim)
        {
            if (!claim.Auth.Contains(a.Id)) { claim.Auth.Add(a.Id); Info(a, "Ты вписан в шкаф: можно строить в его зоне"); return; }
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
            Info(a, parts.Count > 0 ? $"В шкаф: {string.Join(", ", parts)} · хватит на {h:0.0} ч" : need.Count == 0 ? "Шкаф: базы ещё нет — содержать нечего" : $"Шкаф: запас на {h:0.0} ч");
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
                    Economy.Out("содержание", id, MathF.Min(claim.Store.GetValueOrDefault(id), perHour / 3600f));
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
                    foreach (var (id, _) in Building.UpkeepCost(s.Kind, s.Tier))
                        if (lacking.Contains(id)) { DamageStructure(s, s.MaxHp * DecayPerMinute / 60f, -1); break; }
                }
            }
        }

        static bool SameEdge(Structure x, Structure y) => x.IsEdge && y.IsEdge && x.EdgeKey == y.EdgeKey;

        /// <summary>
        /// Чистая линия между точками, не считая самой постройки ignore (и её ребра — косяков проёма).
        /// reach — «достать рукой»: мешает любая часть постройки (стена, окно, закрытая дверь) и высокие стены карты, низкие укрытия — нет;
        /// иначе — обзор: мешают высокие стены и стволы.
        /// </summary>
        public bool ClearLine(Vector2 a, Vector2 b, Structure ignore, bool reach)
        {
            foreach (var w in WallsAlong(a, b))
            {
                if (ignore != null && w.Owner != null && (w.Owner == ignore || SameEdge(w.Owner, ignore))) continue;
                bool blocks = reach ? (w.Owner != null || w.Wall.Height >= 1.2f) : w.Wall.BlocksVision;
                if (blocks && Geo.SegmentAabb(a, b, w.Wall.Min, w.Wall.Max, out _)) return false;
            }
            if (!reach)
                foreach (var n in NodesAlong(a, b))
                    if (n.BlocksVision && Geo.SegmentCircle(a, b, n.Pos, n.Radius * 0.9f, out _)) return false;
            return true;
        }

        /// <summary>Видна ли точка: дальность, конус, линия обзора (без стен постройки ignore).</summary>
        public bool SeesPoint(Actor v, Vector2 p, float range, float coneDeg, Structure ignore = null, float pz = 0f)
        {
            var d = p - v.Pos;
            float dist = d.Length();
            if (dist > range) return false;
            if (dist > 1.5f && MathF.Abs(Geo.AngleDiff(v.Facing, MathF.Atan2(d.Y, d.X))) > coneDeg * 0.5f * MathF.PI / 180f) return false;
            if (v.Z > 0.3f || pz > 0.3f) return ClearLine3(v.Pos, v.Z + EyeH, p, pz, ignore);   // этажи (D60)
            return ClearLine(v.Pos, p, ignore, false);
        }

        /// <summary>Видна ли постройка хоть краем: центр и края стены, центр и углы пола.</summary>
        public bool SeesStructure(Actor v, Structure st, float range, float coneDeg)
        {
            var c = st.Center;
            float pz = st.Floor > 0 ? Building.FloorZ(st.Floor) + (st.IsEdge ? 1.3f : 0.15f) : 0f;   // верхний этаж — смотрим на его высоту (D60)
            if (SeesPoint(v, c, range, coneDeg, st, pz)) return true;
            if (st.IsEdge)
            {
                var along = Vector2.Normalize(st.B - st.A) * 0.7f;
                return SeesPoint(v, c + along, range, coneDeg, st, pz) || SeesPoint(v, c - along, range, coneDeg, st, pz);
            }
            foreach (var corner in st.Poly)
                if (SeesPoint(v, Vector2.Lerp(c, corner, 0.7f), range, coneDeg, st, pz)) return true;
            return false;
        }

        /// <summary>Своя база: постройка привязана к шкафу, где игрок авторизован.</summary>
        public bool IsOwnBase(Actor a, Structure st)
        {
            if (st.OwnerId == a.Id) return true;
            foreach (var d in Deployables) if (d.Id == st.ClaimId && d.Kind == DeployKind.Claim) return d.Auth.Contains(a.Id);
            return false;
        }

        public bool IsOwnBase(Actor a, Deployable dp)
        {
            if (dp.OwnerId == a.Id || (dp.Kind == DeployKind.Claim && dp.Auth.Contains(a.Id))) return true;
            return ClaimAt(Building.CellOf(dp.Pos)) is Deployable c && c.Auth.Contains(a.Id);
        }

        public const float DoorReach = 1.25f;   // дверь по взгляду: до полотна не дальше ~половины клетки

        /// <summary>
        /// Дверь, на которую смотрит игрок: полотно в пределах DoorReach, в полуплоскости взгляда; ближе всего к линии взгляда.
        /// Сначала те, что можешь открыть (не заперта, вписан в кодовый замок, есть ключ). Буфер 2:1 — две двери в треугольнике:
        /// смотришь на внутреннюю — открывается она, развернулся — наружная.
        /// </summary>
        public Structure AimedDoor(Actor a)
        {
            Structure best = null;
            float bestScore = float.MinValue;
            var face = a.FacingDir;
            foreach (var d in _doors.Values)
            {
                float dist = Building.SegDist(a.Pos, d.A, d.B);
                if (dist > DoorReach) continue;
                var to = d.Center - a.Pos;
                float len = to.Length();
                float dot = len < 0.05f ? 1f : Vector2.Dot(face, to / len);
                if (dot < 0.1f && dist > 0.35f) continue;   // за спиной — нет (вплотную в проёме — можно)
                float score = dot + (CanOpenDoor(a, d) ? 10f : 0f) - dist * 0.2f;
                if (score > bestScore) { bestScore = score; best = d; }
            }
            return best;
        }

        public bool CanOpenDoor(Actor a, Structure door) =>
            !door.Locked || (door.KeyLock ? door.LockOwner == a.Id || a.Count("key_" + door.Id) > 0 : door.LockAuth.Contains(a.Id));

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
