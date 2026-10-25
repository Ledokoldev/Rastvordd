using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Дорожная сеть мира: у готовых РТ полосы асфальта/гравия/грунта доходят до края раскладки — здесь они
    /// продолжаются. Главные дороги (асфальт на гравийной обочине) связывают РТ деревом «ближайший к сети»,
    /// каждый свободный выезд — хотя бы грунтовкой до ближайшей дороги (так появляются и петли).
    /// Маршрут — по грубой сетке 4 м (Дейкстра: вода и края карты непроходимы, РТ обходятся, болото и лес дороже),
    /// потом упрощение, сглаживание Чайкина и лёгкое виляние шумом. Строится из сида в конце генерации мира —
    /// в сохранение не пишется, после загрузки узлы с дорог убираются заново.
    /// </summary>
    public sealed partial class World
    {
        const float RoadCell = 4f;
        int _roadN;
        byte[] _roadMask;                 // 1 — главная дорога, 2 — грунтовка (клетка 4 м)
        byte[] _roadNear;                 // рядом с дорогой (до ~14 м): вдоль неё вторую не тянуть
        int[] _roadOwner;                 // какая линия (номер + 1) легла в клетку первой

        /// <summary>Осевая линия дороги (шаг 2 м) и ширина полотна с обочиной.</summary>
        public sealed class RoadLine { public List<Vector2> P; public bool Main; public float Half; public Monument A, B; }
        public readonly List<RoadLine> RoadLines = new();

        sealed class RoadExit
        {
            public Monument M;
            public Vector2 Pos, Dir;
            public float Width;
            public string Tile;
            public int Rank;              // 3 асфальт, 2 гравий, 1 грунт
            public bool Used;
            public float Lead;            // насколько вывести дорогу прямо, чтобы выйти из зоны РТ
            public Vector2 LeadEnd => Pos + Dir * Lead;
        }

        /// <summary>На дороге ли точка (грубо, по клеткам 4 м): боты, респавн узлов.</summary>
        public bool OnRoad(Vector2 p)
        {
            if (_roadMask == null) return false;
            int x = (int)((p.X + HalfSize) / RoadCell), y = (int)((p.Y + HalfSize) / RoadCell);
            if (x < 0 || y < 0 || x >= _roadN || y >= _roadN) return false;
            return _roadMask[y * _roadN + x] != 0;
        }

        static int RoadRank(string tile) => tile.Contains("asphalt") ? 3 : tile.Contains("gravel") ? 2 : tile.Contains("dirt") ? 1 : 0;

        // ---------- выезды РТ ----------

        /// <summary>Полосы дорожной земли РТ, уходящие за край площадки хотя бы на 2 м: точка конца, направление, ширина.</summary>
        List<RoadExit> FindExits(Monument m)
        {
            var list = new List<RoadExit>();
            foreach (var pt in Patches)
            {
                if (pt.Road != 0) continue;
                int rank = RoadRank(pt.Tile);
                if (rank == 0) continue;
                var poly = pt.Poly ?? new[] { pt.Min, new Vector2(pt.Max.X, pt.Min.Y), pt.Max, new Vector2(pt.Min.X, pt.Max.Y) };
                Vector2 bmin = new(float.MaxValue), bmax = new(float.MinValue);
                foreach (var v in poly) { bmin = Vector2.Min(bmin, v); bmax = Vector2.Max(bmax, v); }
                if (!Geo.BoxOverlap(bmin, bmax, m.Min - Vector2.One, m.Max + Vector2.One)) continue;
                for (int side = 0; side < 4; side++)
                {
                    bool alongX = side < 2;                         // выезд по оси X (восток/запад)
                    float sign = side % 2 == 0 ? 1 : -1;
                    float ext = alongX ? (sign > 0 ? bmax.X : bmin.X) : (sign > 0 ? bmax.Y : bmin.Y);
                    float edge = alongX ? (sign > 0 ? m.Max.X : m.Min.X) : (sign > 0 ? m.Max.Y : m.Min.Y);
                    if ((ext - edge) * sign < 2f) continue;
                    // сечение полосы в метре от её конца
                    float cut = ext - sign * 1f;
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int i = 0; i < poly.Length; i++)
                    {
                        var a = poly[i]; var b = poly[(i + 1) % poly.Length];
                        float aa = alongX ? a.X : a.Y, ba = alongX ? b.X : b.Y;
                        if ((aa - cut) * (ba - cut) > 0 || aa == ba) continue;
                        float t = (cut - aa) / (ba - aa);
                        float o = alongX ? a.Y + (b.Y - a.Y) * t : a.X + (b.X - a.X) * t;
                        lo = MathF.Min(lo, o); hi = MathF.Max(hi, o);
                    }
                    float w = hi - lo;
                    if (w < 2f || w > 12.5f) continue;
                    float mid = (lo + hi) * 0.5f;
                    var dir = alongX ? new Vector2(sign, 0) : new Vector2(0, sign);
                    var pos = alongX ? new Vector2(ext, mid) : new Vector2(mid, ext);
                    // одна дорога — один выезд: перекрытые полосы (гравий под асфальтом) сливаются, остаётся лучшая
                    var same = list.Find(e => e.Dir == dir && Vector2.Distance(e.Pos, pos) < 8f);
                    if (same != null)
                    {
                        if (rank > same.Rank || (rank == same.Rank && w > same.Width)) { same.Pos = pos; same.Width = w; same.Tile = pt.Tile; same.Rank = rank; }
                        else if ((pos - same.Pos).Length() < 8f && Vector2.Dot(pos - same.Pos, dir) > 0) same.Pos += dir * Vector2.Dot(pos - same.Pos, dir);
                        continue;
                    }
                    list.Add(new RoadExit { M = m, Pos = pos, Dir = dir, Width = w, Tile = pt.Tile, Rank = rank });
                }
            }
            foreach (var e in list) e.Lead = LeadFor(e);
            return list;
        }

        /// <summary>Длина прямого вывода: за зону РТ (край + 8 м), не меньше 16 м — поворот у выезда плавный.</summary>
        static float LeadFor(RoadExit e)
        {
            var m = e.M;
            float edge = e.Dir.X > 0 ? m.Max.X : e.Dir.X < 0 ? -m.Min.X : e.Dir.Y > 0 ? m.Max.Y : -m.Min.Y;
            float at = Vector2.Dot(e.Pos, e.Dir);
            return MathF.Max(16f, edge + 8.5f - at);
        }

        // ---------- сетка стоимости ----------

        float[] RoadCostGrid()
        {
            int n = _roadN;
            var cost = new float[n * n];
            var nz = new Noise2(Seed * 31 + 907);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var c = new Vector2((x + 0.5f) * RoadCell - HalfSize, (y + 0.5f) * RoadCell - HalfSize);
                    int i = y * n + x;
                    if (MathF.Abs(c.X) > HalfSize - 22 || MathF.Abs(c.Y) > HalfSize - 22) { cost[i] = -1; continue; }
                    var b = Terrain.BiomeAt(c);
                    if (b == Biome.Water) { cost[i] = -1; continue; }
                    float k = b switch { Biome.Swamp => 3f, Biome.Beach => 1.8f, Biome.Forest => 1.35f, Biome.Tundra => 1.15f, _ => 1f };
                    // у воды — дороже (берег размыт), на склонах — дороже
                    for (int d = 0; d < 4; d++)
                    {
                        var q = c + new Vector2(d == 0 ? 6 : d == 1 ? -6 : 0, d == 2 ? 6 : d == 3 ? -6 : 0);
                        if (!Terrain.IsLand(q)) { k *= 2.5f; break; }
                    }
                    float h0 = Terrain.HeightAt(c), hx = Terrain.HeightAt(c + new Vector2(4, 0)), hy = Terrain.HeightAt(c + new Vector2(0, 4));
                    k *= 1 + MathF.Min(3f, (MathF.Abs(hx - h0) + MathF.Abs(hy - h0)) * 25f);
                    k *= 1 + 0.45f * (nz.Fbm(c / 70f, 2) + 1) * 0.5f;   // пологое виляние, а не линейка
                    foreach (var m in Monuments)
                    {
                        if (m.Contains(c, 7)) { k = -1; break; }
                        if (m.Contains(c, 16)) k *= 3f;
                    }
                    cost[i] = k;
                }
            return cost;
        }

        int RoadIdx(Vector2 p) => Math.Clamp((int)((p.Y + HalfSize) / RoadCell), 0, _roadN - 1) * _roadN + Math.Clamp((int)((p.X + HalfSize) / RoadCell), 0, _roadN - 1);
        Vector2 RoadCenter(int i) => new((i % _roadN + 0.5f) * RoadCell - HalfSize, (i / _roadN + 0.5f) * RoadCell - HalfSize);

        /// <summary>Дейкстра от start до первой клетки, где goal(i) ≥ 0 (бонус к стоимости цели). null — не дошли.</summary>
        List<int> RoadSearch(float[] cost, int start, Func<int, float> goal, out int reached)
        {
            int n = _roadN;
            var g = new float[n * n];
            var from = new int[n * n];
            Array.Fill(g, float.MaxValue);
            var open = new PriorityQueue<int, float>();
            g[start] = 0; from[start] = -1;
            open.Enqueue(start, 0);
            reached = -1;
            float best = float.MaxValue;
            while (open.TryDequeue(out int i, out float d))
            {
                if (d > g[i]) continue;
                if (d >= best) break;
                float gb = i == start ? -1 : goal(i);
                if (gb >= 0) { if (d + gb < best) { best = d + gb; reached = i; } continue; }
                int x = i % n, y = i / n;
                for (int k = 0; k < 8; k++)
                {
                    int dx = k switch { 0 => 1, 1 => 1, 2 => 0, 3 => -1, 4 => -1, 5 => -1, 6 => 0, _ => 1 };
                    int dy = k switch { 0 => 0, 1 => 1, 2 => 1, 3 => 1, 4 => 0, 5 => -1, 6 => -1, _ => -1 };
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                    int j = ny * n + nx;
                    float c = cost[j];
                    if (c < 0 && goal(j) < 0) continue;
                    if (c < 0) c = 1;
                    else if (_roadNear[j] != 0 && _roadMask[j] == 0) c *= 2.6f;   // параллельно дороге — дорого: пусть примыкает или уходит
                    float nd = d + c * (dx != 0 && dy != 0 ? 1.4142f : 1f);
                    if (nd < g[j]) { g[j] = nd; from[j] = i; open.Enqueue(j, nd); }
                }
            }
            if (reached < 0) return null;
            var path = new List<int>();
            for (int c = reached; c >= 0; c = from[c]) path.Add(c);
            path.Reverse();
            return path;
        }

        // ---------- геометрия линии ----------

        static void Simplify(List<Vector2> pts, int a, int b, float eps, List<Vector2> outPts)
        {
            float maxD = 0; int idx = -1;
            var ab = pts[b] - pts[a];
            float len = ab.Length();
            for (int i = a + 1; i < b; i++)
            {
                var ap = pts[i] - pts[a];
                float d = len < 1e-3f ? ap.Length() : MathF.Abs(ab.X * ap.Y - ab.Y * ap.X) / len;
                if (d > maxD) { maxD = d; idx = i; }
            }
            if (idx >= 0 && maxD > eps)
            {
                Simplify(pts, a, idx, eps, outPts);
                Simplify(pts, idx, b, eps, outPts);
            }
            else outPts.Add(pts[b]);
        }

        static List<Vector2> Chaikin(List<Vector2> p, int iters)
        {
            for (int it = 0; it < iters; it++)
            {
                var q = new List<Vector2>(p.Count * 2) { p[0] };
                for (int i = 0; i < p.Count - 1; i++)
                {
                    q.Add(p[i] * 0.75f + p[i + 1] * 0.25f);
                    q.Add(p[i] * 0.25f + p[i + 1] * 0.75f);
                }
                q.Add(p[^1]);
                p = q;
            }
            return p;
        }

        static List<Vector2> Resample(List<Vector2> p, float step)
        {
            var r = new List<Vector2> { p[0] };
            float carry = 0;
            for (int i = 0; i < p.Count - 1; i++)
            {
                var a = p[i]; var b = p[i + 1];
                float len = Vector2.Distance(a, b);
                float t = step - carry;
                while (t <= len) { r.Add(Vector2.Lerp(a, b, t / len)); t += step; }
                carry = len - (t - step);
            }
            if (Vector2.Distance(r[^1], p[^1]) > step * 0.35f) r.Add(p[^1]); else r[^1] = p[^1];
            return r;
        }

        static Vector2 Tangent(List<Vector2> p, int i)
        {
            var t = p[Math.Min(i + 1, p.Count - 1)] - p[Math.Max(i - 1, 0)];
            return t.LengthSquared() < 1e-6f ? Vector2.UnitX : Vector2.Normalize(t);
        }

        static float Smooth01(float t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

        /// <summary>Ближайшая точка на уже построенных дорогах (для примыкания).</summary>
        (Vector2 p, RoadLine line) NearestRoadPoint(Vector2 q, bool mainOnly)
        {
            float best = float.MaxValue; Vector2 bp = q; RoadLine bl = null;
            foreach (var l in RoadLines)
            {
                if (mainOnly && !l.Main) continue;
                foreach (var p in l.P)
                {
                    float d = Vector2.DistanceSquared(p, q);
                    if (d < best) { best = d; bp = p; bl = l; }
                }
            }
            return (bp, bl);
        }

        // ---------- сеть ----------

        /// <summary>Дорожная сеть мира — в самом конце генерации, своим генератором.</summary>
        void BuildRoads()
        {
            _roadN = (int)(HalfSize * 2 / RoadCell);
            _roadMask = new byte[_roadN * _roadN];
            _roadOwner = new int[_roadN * _roadN];
            _roadNear = new byte[_roadN * _roadN];
            var rng = new Random(Seed * 7919 + 4243);
            var nodes = new List<(Monument m, List<RoadExit> ex)>();
            foreach (var m in Prefabs)
            {
                var ex = FindExits(m);
                if (ex.Count > 0) nodes.Add((m, ex));
            }
            // старый склад в центре: проход в южной стене (y = −5, x 17..23)
            var old = Monuments.Find(mm => !Prefabs.Contains(mm));
            if (old != null)
            {
                var e = new RoadExit { M = old, Pos = new Vector2(20, -5.5f), Dir = Vector2.UnitY, Width = 5, Tile = "tiles/ground_dirt", Rank = 1 };
                e.Lead = LeadFor(e);
                nodes.Add((old, new List<RoadExit> { e }));
            }
            if (nodes.Count == 0) return;
            var cost = RoadCostGrid();

            // дерево: от самой центральной РТ, каждый раз — ближайшая к сети, её лучший выезд → к сети
            var connected = new List<int>();
            int first = 0;
            for (int i = 1; i < nodes.Count; i++) if (nodes[i].m.Center.LengthSquared() < nodes[first].m.Center.LengthSquared()) first = i;
            connected.Add(first);
            var left = new List<int>();
            for (int i = 0; i < nodes.Count; i++) if (i != first) left.Add(i);
            while (left.Count > 0)
            {
                int bi = -1, bc = -1; float bd = float.MaxValue;
                foreach (int u in left)
                    foreach (int c in connected)
                    {
                        float d = Vector2.Distance(nodes[u].m.Center, nodes[c].m.Center);
                        if (d < bd) { bd = d; bi = u; bc = c; }
                    }
                left.Remove(bi);
                var target = nodes[bc].m.Center;
                // выезд, смотрящий к сети: асфальт и ширина — в плюс
                RoadExit ex = null; float bs = float.MinValue;
                foreach (var e in nodes[bi].ex)
                {
                    var to = target - e.LeadEnd;
                    float s = Vector2.Dot(e.Dir, to.LengthSquared() > 1 ? Vector2.Normalize(to) : e.Dir) + e.Rank * 0.18f;
                    if (s > bs) { bs = s; ex = e; }
                }
                if (ConnectExit(cost, ex, nodes, connected, true)) connected.Add(bi);
                else connected.Add(bi);   // не дошли (вода) — остаётся отдельно, но в очередь больше не ставим
            }

            // свободные выезды: асфальтовые — главной дорогой, остальные — грунтовкой до ближайшей дороги (не своей)
            foreach (var (m, ex) in nodes)
                foreach (var e in ex)
                    if (!e.Used) ConnectExit(cost, e, nodes, null, e.Rank >= 3);

            ClearRoadsides(true);
            DressRoads(rng);
        }

        /// <summary>
        /// Дорога от выезда e: к свободному выезду РТ из connected (главная, РТ↔РТ) или к ближайшей дороге.
        /// connected == null — к дороге, но не у своей РТ (петли).
        /// </summary>
        bool ConnectExit(float[] cost, RoadExit e, List<(Monument m, List<RoadExit> ex)> nodes, List<int> connected, bool main)
        {
            var goals = new Dictionary<int, RoadExit>();
            var own = e.M;
            if (connected != null)
                foreach (int c in connected)
                    foreach (var ge in nodes[c].ex)
                        if (!ge.Used && ge != e) goals[RoadIdx(ge.LeadEnd)] = ge;
            if (connected == null)   // свободный выезд: лучше всего — в свободный выезд соседней РТ (петля сети)
                foreach (var (m, ex) in nodes)
                    if (m != own)
                        foreach (var ge in ex)
                            if (!ge.Used && Vector2.Distance(ge.LeadEnd, e.LeadEnd) < 260) goals[RoadIdx(ge.LeadEnd)] = ge;
            float ownMargin = connected == null ? 45 : 12;
            bool strict = connected == null;   // не к своим же дорогам рядом с РТ — иначе петля «вокруг себя»
            bool anyRoad = false;
            for (int i = 0; i < _roadMask.Length; i++) if (_roadMask[i] == 1 || (!main && _roadMask[i] != 0)) { anyRoad = true; break; }
            if (goals.Count == 0 && !anyRoad) return false;
            float Goal(int i)
            {
                if (goals.ContainsKey(i)) return 0;
                byte r = _roadMask[i];
                if (r == 0 || (main && r != 1)) return -1;
                if (own.Contains(RoadCenter(i), ownMargin)) return -1;
                if (strict && _roadOwner[i] > 0)
                {
                    var l = RoadLines[_roadOwner[i] - 1];
                    if ((l.A == own || l.B == own) && Vector2.Distance(RoadCenter(i), own.Center) < 170) return -1;
                }
                return 0;
            }
            int start = RoadIdx(e.LeadEnd);
            // за линией выезда (назад к РТ) у самого выезда — дорого: дорога сперва отходит, потом поворачивает
            var local = (float[])cost.Clone();
            int rc = (int)(40 / RoadCell), sc = start % _roadN, sr = start / _roadN;
            for (int y = Math.Max(0, sr - rc); y <= Math.Min(_roadN - 1, sr + rc); y++)
                for (int x = Math.Max(0, sc - rc); x <= Math.Min(_roadN - 1, sc + rc); x++)
                {
                    int i = y * _roadN + x;
                    var c = RoadCenter(i);
                    if (local[i] > 0 && Vector2.Dot(c - e.Pos, e.Dir) < e.Lead - 2 && Vector2.Distance(c, e.Pos) < 40) local[i] *= 5f;
                }
            cost = local;
            var cells = RoadSearch(cost, start, Goal, out int reached);
            if (cells == null && connected == null)
            {
                ownMargin = 0; strict = false;   // не нашли чужую — хоть к своей
                cells = RoadSearch(cost, start, Goal, out reached);
            }
            if (cells == null) return false;

            // осевая: прямой вывод из РТ → путь по сетке (упрощённый) → примыкание
            var raw = new List<Vector2>();
            foreach (int c in cells) raw.Add(RoadCenter(c));
            raw[0] = e.LeadEnd;
            goals.TryGetValue(reached, out var ge2);
            Vector2 endPt;
            Vector2? endDir = null;
            if (ge2 != null) { raw[^1] = ge2.LeadEnd; endPt = ge2.Pos; endDir = -ge2.Dir; }
            else
            {
                var (rp, _) = NearestRoadPoint(raw[^1], main);
                raw[^1] = rp; endPt = rp;
            }
            var simp = new List<Vector2> { raw[0] };
            if (raw.Count > 1) Simplify(raw, 0, raw.Count - 1, 2.6f, simp);
            var pts = new List<Vector2> { e.Pos - e.Dir * 1.2f, e.Pos, e.Pos + e.Dir * (e.Lead * 0.5f) };
            pts.AddRange(simp);
            if (ge2 != null) { pts.Add(ge2.Pos + ge2.Dir * (ge2.Lead * 0.5f)); pts.Add(ge2.Pos); pts.Add(ge2.Pos - ge2.Dir * 1.2f); }
            var line = Resample(Chaikin(pts, 4), 2f);
            Relax(line, 5, ge2 != null ? 5 : 1, 40);
            line = Resample(line, 2f);
            Wobble(line, e.Lead + 4, ge2 != null ? ge2.Lead + 4 : 8);

            e.Used = true;
            if (ge2 != null) ge2.Used = true;
            EmitRoad(line, e, ge2, main);
            return true;
        }

        /// <summary>Сглаживание Лапласа: углы пути по сетке становятся плавными дугами (радиус ~10–15 м); концы — на месте.</summary>
        static void Relax(List<Vector2> line, int keepA, int keepB, int iters)
        {
            if (line.Count < keepA + keepB + 3) return;
            var tmp = new Vector2[line.Count];
            for (int it = 0; it < iters; it++)
            {
                for (int i = 0; i < line.Count; i++) tmp[i] = line[i];
                for (int i = keepA; i < line.Count - keepB; i++)
                    line[i] = tmp[i] + ((tmp[i - 1] + tmp[i + 1]) * 0.5f - tmp[i]) * 0.5f;
            }
        }

        /// <summary>Лёгкое виляние поперёк (низкочастотный шум), у концов — ноль.</summary>
        void Wobble(List<Vector2> line, float keepA, float keepB)
        {
            if (line.Count < 6) return;
            var nz = new Noise2(Seed * 17 + RoadLines.Count * 101 + 5);
            var len = new float[line.Count];
            for (int i = 1; i < line.Count; i++) len[i] = len[i - 1] + Vector2.Distance(line[i - 1], line[i]);
            float total = len[^1];
            var outL = new Vector2[line.Count];
            for (int i = 0; i < line.Count; i++)
            {
                var t = Tangent(line, i);
                float fade = Smooth01((len[i] - keepA) / 25f) * Smooth01((total - len[i] - keepB) / 25f);
                outL[i] = line[i] + new Vector2(-t.Y, t.X) * nz.Fbm(new Vector2(len[i] / 55f, 0.37f), 2) * 2.2f * fade;
            }
            for (int i = 0; i < line.Count; i++) line[i] = outL[i];
        }

        /// <summary>Полоса из участков земли (по 9 сегментов): клиент рисует их PatchLayer'ом.</summary>
        void EmitStrip(List<Vector2> line, Func<float, float> width, float from, float to, string tile, byte road)
        {
            var len = new float[line.Count];
            for (int i = 1; i < line.Count; i++) len[i] = len[i - 1] + Vector2.Distance(line[i - 1], line[i]);
            var idx = new List<int>();
            for (int i = 0; i < line.Count; i++) if (len[i] >= from - 0.01f && len[i] <= to + 0.01f) idx.Add(i);
            if (idx.Count < 2) return;
            for (int s = 0; s < idx.Count - 1; s += 9)
            {
                int e = Math.Min(s + 9, idx.Count - 1);
                int k = e - s + 1;
                var poly = new Vector2[k * 2];
                Vector2 bmin = new(float.MaxValue), bmax = new(float.MinValue);
                for (int j = 0; j < k; j++)
                {
                    int i = idx[s + j];
                    var t = Tangent(line, i);
                    var nrm = new Vector2(-t.Y, t.X) * (width(len[i]) * 0.5f);
                    poly[j] = line[i] + nrm;
                    poly[k * 2 - 1 - j] = line[i] - nrm;
                }
                foreach (var v in poly) { bmin = Vector2.Min(bmin, v); bmax = Vector2.Max(bmax, v); }
                Patches.Add(new MapPatch { Tile = tile, Poly = poly, Min = bmin, Max = bmax, Road = road });
            }
        }

        void EmitRoad(List<Vector2> line, RoadExit a, RoadExit b, bool main)
        {
            float total = 0;
            for (int i = 1; i < line.Count; i++) total += Vector2.Distance(line[i - 1], line[i]);
            float wa = a.Width, wb = b?.Width ?? 0;
            // ширина: у выезда — как полоса РТ, за 12 м — своя
            Func<float, float> Taper(float baseW, float capA, float capB) => s =>
            {
                float w = baseW;
                w = w + (capA - w) * (1 - Smooth01(s / 12f));
                if (capB > 0) w = w + (capB - w) * (1 - Smooth01((total - s) / 12f));
                return w;
            };
            float half;
            if (main)
            {
                const float Asph = 6f, Shoulder = 8.6f;
                EmitStrip(line, Taper(Shoulder, MathF.Max(wa, 4), b != null ? MathF.Max(wb, 4) : 0), 0, total, "tiles/ground_gravel", 3);
                float s0 = a.Rank >= 3 ? 0 : 7, s1 = b == null || b.Rank >= 3 ? total : total - 7;
                EmitStrip(line, Taper(Asph, a.Rank >= 3 ? wa : Asph, b != null && b.Rank >= 3 ? wb : 0), s0, s1, "tiles/ground_asphalt", 1);
                half = Shoulder * 0.5f;
            }
            else
            {
                const float Track = 3.2f;
                string tile = a.Rank == 2 ? "tiles/ground_gravel" : "tiles/ground_dirt";
                EmitStrip(line, Taper(Track, MathF.Min(wa, 8), 0), 0, total, tile, 2);
                half = Track * 0.5f;
            }
            RoadLines.Add(new RoadLine { P = line, Main = main, Half = half, A = a.M, B = b?.M });
            byte mark = main ? (byte)1 : (byte)2;
            foreach (var p in line)
            {
                if (a.M.Contains(p, 0) || (b != null && b.M.Contains(p, 0))) continue;
                int r = (int)MathF.Ceiling((half + 14f) / RoadCell);
                int cx = (int)((p.X + HalfSize) / RoadCell), cy = (int)((p.Y + HalfSize) / RoadCell);
                for (int y = cy - r; y <= cy + r; y++)
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        if (x < 0 || y < 0 || x >= _roadN || y >= _roadN) continue;
                        var c = new Vector2((x + 0.5f) * RoadCell - HalfSize, (y + 0.5f) * RoadCell - HalfSize);
                        float dist = Vector2.Distance(c, p);
                        int i = y * _roadN + x;
                        if (dist < half + 14f) _roadNear[i] = 1;
                        if (dist > half + 1.5f) continue;
                        if (_roadOwner[i] == 0) _roadOwner[i] = RoadLines.Count;
                        if (_roadMask[i] == 0 || mark == 1) _roadMask[i] = mark;
                    }
            }
        }

        // ---------- обочины ----------

        static bool RoadsideClearKind(WallKind k) => k is WallKind.Car or WallKind.Barrier or WallKind.Barrel or WallKind.WoodCrate or WallKind.Junk;

        /// <summary>Убрать с полотна деревья, камни, пропсы мира; подбираемое — сдвинуть на обочину.</summary>
        void ClearRoadsides(bool all)
        {
            var nodes = new HashSet<ResourceNode>();
            var walls = new HashSet<MapWall>();
            foreach (var l in RoadLines)
                for (int i = 0; i < l.P.Count; i++)
                {
                    var p = l.P[i];
                    if (MonumentAt(p) is Monument mm && Prefabs.Contains(mm)) continue;
                    float r = l.Half + 0.9f;
                    foreach (var nd in NodesNear(p, r)) if (Vector2.Distance(nd.Pos, p) < r + nd.Radius * 0.5f && !InPrefab(nd.Pos)) nodes.Add(nd);
                    if (!all) continue;
                    foreach (var w in WallsNear(p, r))
                        if (w.Owner == null && !w.Prefab && RoadsideClearKind(w.Kind) && Geo.CircleBox(p, r, w.Wall.Min, w.Wall.Max)) walls.Add(w);
                    foreach (var pk in Pickups)
                    {
                        if (pk.Kind == PickupKind.Fuse || Vector2.Distance(pk.Pos, p) >= r || InPrefab(pk.Pos)) continue;
                        var d = pk.Pos - p;
                        var t = Tangent(l.P, i);
                        var side = new Vector2(-t.Y, t.X);
                        if (Vector2.Dot(d, side) < 0) side = -side;
                        pk.Pos = p + side * (r + 0.8f);
                    }
                }
            foreach (var nd in nodes) { _nodeGrid.Remove(nd); Nodes.Remove(nd); }
            foreach (var w in walls) { _wallGrid.Remove(w); Walls.Remove(w); }
        }

        bool InPrefab(Vector2 p)
        {
            foreach (var m in Prefabs) if (m.Contains(p, 0)) return true;
            return false;
        }

        /// <summary>После загрузки сохранения: узлы из сохранения на дорогах убрать.</summary>
        void ClearRoadsAfterLoad() => ClearRoadsides(false);

        /// <summary>Придорожное: столбы с проводами вдоль главных дорог, редкие брошенные машины и указатели.</summary>
        void DressRoads(Random rng)
        {
            foreach (var l in RoadLines)
            {
                if (!l.Main || l.P.Count < 40) continue;
                int sideSign = rng.Next(2) == 0 ? 1 : -1;
                Vector2? prev = null;
                for (int i = 10; i < l.P.Count - 10; i += 19 + rng.Next(4))
                {
                    var t = Tangent(l.P, i);
                    var nrm = new Vector2(-t.Y, t.X) * sideSign;
                    var p = l.P[i] + nrm * (l.Half + 1.7f);
                    if (NearMonument(p, 4) || !Terrain.IsLand(p) || !IsFree(p, 0.8f) || NearRoad(p, 1.3f)) { prev = null; continue; }
                    Decor(NearMonument(p, 30) ? "outdoor/streetlight_concrete" : "outdoor/power_pole_wood", p, rng.Next(2) == 0);   // на подъезде к РТ — фонари (светят ночью)
                    AddHiddenWall(p.X - 0.2f, p.Y - 0.4f, p.X + 0.2f, p.Y, 8f);
                    if (prev is Vector2 q && Vector2.Distance(q, p) < 52) Wire(q, 7.3f, p, 7.3f, 0.7f);
                    prev = p;
                }
                // брошенные машины на обочине и указатели — по другой стороне
                for (int i = 20; i < l.P.Count - 20; i += 1)
                {
                    if (rng.NextDouble() > 0.006) continue;
                    var t = Tangent(l.P, i);
                    var p = l.P[i] - new Vector2(-t.Y, t.X) * sideSign * (l.Half + 1.9f);
                    if (NearMonument(p, 6) || !Terrain.IsLand(p) || !IsFree(p, 2.2f) || NearRoad(p, 1.6f)) continue;
                    if (rng.Next(3) == 0) { Decor("props/out_sign_blank", p, rng.Next(2) == 0); AddHiddenWall(p.X - 0.15f, p.Y - 0.3f, p.X + 0.15f, p.Y, 2f); }
                    else AddProp(WallKind.Car, p);
                    i += 30;
                }
            }
        }

        /// <summary>Не легло ли на полотно какой-нибудь дороги (кроме обочины): r — запас.</summary>
        bool NearRoad(Vector2 p, float r)
        {
            foreach (var l in RoadLines)
                foreach (var q in l.P)
                    if (Vector2.DistanceSquared(p, q) < (l.Half + r) * (l.Half + r)) return true;
            return false;
        }
    }
}
