using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client3D
{
    /// <summary>
    /// Геометрия построек из данных симуляции: те же размеры, что в Building (клетка 2 м, стена 0,3 × 2,6 м,
    /// подоконник 1 м, косяк 0,5 м). Один набор функций рисует и настоящие постройки, и призрак Плана.
    /// </summary>
    public static class Pieces3D
    {
        public const float FloorTop = 0.1f, FloorBottom = -0.2f;
        public const float DoorH = 2.1f, WindowTop = 2.0f;

        /// <summary>Пол (квадрат или треугольник) по вершинам многоугольника.</summary>
        public static void Floor(Node3D parent, SVec[] poly, Material mat)
        {
            if (poly == null || poly.Length < 3) return;
            if (poly.Length == 4 && IsAxisSquare(poly))
            {
                SVec mn = poly[0], mx = poly[0];
                foreach (var p in poly) { mn = SVec.Min(mn, p); mx = SVec.Max(mx, p); }
                var c = (mn + mx) * 0.5f; var s = mx - mn;
                W3.AddBox(parent, W3.V(c, (FloorTop + FloorBottom) * 0.5f), new Vector3(s.X, FloorTop - FloorBottom, s.Y), 0, mat);
                return;
            }
            parent.AddChild(new MeshInstance3D { Mesh = Prism(poly, FloorBottom, FloorTop), MaterialOverride = mat });
        }

        static bool IsAxisSquare(SVec[] p)
        {
            for (int i = 0; i < 4; i++)
            {
                var d = p[(i + 1) % 4] - p[i];
                if (System.MathF.Abs(d.X) > 1e-3f && System.MathF.Abs(d.Y) > 1e-3f) return false;
            }
            return true;
        }

        /// <summary>Призма по многоугольнику: верх, низ и бока с нормалями наружу.</summary>
        public static ArrayMesh Prism(SVec[] poly, float y0, float y1)
        {
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            SVec cen = SVec.Zero;
            foreach (var p in poly) cen += p;
            cen /= poly.Length;
            for (int i = 1; i + 1 < poly.Length; i++)
            {
                st.SetNormal(Vector3.Up);
                st.AddVertex(W3.V(poly[0], y1)); st.AddVertex(W3.V(poly[i], y1)); st.AddVertex(W3.V(poly[i + 1], y1));
                st.SetNormal(Vector3.Down);
                st.AddVertex(W3.V(poly[0], y0)); st.AddVertex(W3.V(poly[i + 1], y0)); st.AddVertex(W3.V(poly[i], y0));
            }
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Length];
                var e = b - a;
                var nrm = new SVec(e.Y, -e.X);
                if (SVec.Dot(nrm, (a + b) * 0.5f - cen) < 0) nrm = -nrm;
                var n3 = new Vector3(nrm.X, 0, nrm.Y).Normalized();
                st.SetNormal(n3);
                st.AddVertex(W3.V(a, y0)); st.AddVertex(W3.V(b, y0)); st.AddVertex(W3.V(b, y1));
                st.SetNormal(n3);
                st.AddVertex(W3.V(a, y0)); st.AddVertex(W3.V(b, y1)); st.AddVertex(W3.V(a, y1));
            }
            return st.Commit();
        }

        /// <summary>Отрезок стены от t0 до t1 (доли ребра A–B) и по высоте от h0 до h1.</summary>
        static void Seg(Node3D parent, SVec a, SVec b, float t0, float t1, float h0, float h1, Material mat, float thick = Building.Thick)
        {
            var d = b - a; float len = d.Length();
            if (len < 1e-3f || t1 <= t0 || h1 <= h0) return;
            var p0 = a + d * t0; var p1 = a + d * t1;
            var c = (p0 + p1) * 0.5f;
            W3.AddBox(parent, W3.V(c, (h0 + h1) * 0.5f), new Vector3(len * (t1 - t0), h1 - h0, thick), W3.Yaw(d.X, d.Y), mat);
        }

        /// <summary>
        /// Стена, проём, окно — твёрдые части берутся из Building.Spans (те же, что у пуль и движения),
        /// поверх — косметика: перемычка над проёмом и балка над окном. Дверь — отдельно (Door), её проём рисует Doorway.
        /// </summary>
        public static void Edge(Node3D parent, StructKind kind, SVec a, SVec b, Material mat, bool open = false)
        {
            if ((b - a).Length() < 1e-3f || kind == StructKind.Door) return;
            foreach (var (t0, t1, h, _) in Building.Spans(kind, open))
                Seg(parent, a, b, t0, t1, 0, h, mat);
            float post = Building.PostW / Building.Cell;
            if (kind == StructKind.Doorway) Seg(parent, a, b, post, 1 - post, DoorH, Building.WallH, mat);
            if (kind == StructKind.Window && !open) Seg(parent, a, b, 0.3f, 0.7f, WindowTop, Building.WallH, mat);
        }

        /// <summary>Полотно двери в проёме A–B: закрыта — в плоскости стены, открыта — повёрнута на 90° вокруг петли.</summary>
        public static void Door(Node3D parent, SVec a, SVec b, bool open, Material mat, Material lockMat)
        {
            var d = b - a; float len = d.Length();
            if (len < 1e-3f) return;
            var u = d / len;
            float post = len * Building.PostW / Building.Cell;
            float w = len - post * 2 - 0.04f;
            var hinge = a + u * (post + 0.02f);
            var dir = open ? new SVec(-u.Y, u.X) : u;
            var c = hinge + dir * (w * 0.5f);
            W3.AddBox(parent, W3.V(c, DoorH * 0.5f - 0.02f), new Vector3(w, DoorH - 0.04f, 0.08f), W3.Yaw(dir.X, dir.Y), mat);
            if (lockMat != null)
            {
                var lp = hinge + dir * (w - 0.15f);
                W3.AddBox(parent, W3.V(lp, 1.05f), new Vector3(0.1f, 0.16f, 0.16f), W3.Yaw(dir.X, dir.Y), lockMat);
            }
        }
    }

    /// <summary>Постройки игроков и ботов. Пересобираются целиком, когда меняется StructVersion (стройка, урон, двери).</summary>
    public partial class Struct3D : Node3D
    {
        long _key = long.MinValue;
        public readonly Dictionary<int, StructSnap> ById = new();
        /// <summary>Стены построек — в лучи обзора (как BuildLayer.Occluders у 2D). Меняется вместе с постройками.</summary>
        public readonly List<WallInfo> Occluders = new();
        public int Version;
        readonly List<(SVec[] poly, int tier)> _floors = new();

        /// <summary>Уровень пола под точкой (−1 — не на постройке): для звука шагов.</summary>
        public int FloorTier(SVec p)
        {
            foreach (var (poly, tier) in _floors)
                if (Inside(poly, p)) return tier;
            return -1;
        }

        static bool Inside(SVec[] poly, SVec p)
        {
            if (poly == null || poly.Length < 3) return false;
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) inside = !inside;
            return inside;
        }

        public void Sync(Snapshot s)
        {
            // StructVersion меняется при стройке, уроне и дверях; набор id — когда постройки входят в радиус 70 м и выходят из него
            long key = s.StructVersion;
            foreach (var st in s.Structures) key = key * 31 + st.Id * 2 + (st.Seen ? 1 : 0);
            if (key == _key) return;
            _key = key;
            W3.ClearChildren(this);
            ById.Clear();
            Occluders.Clear();
            _floors.Clear();
            Version++;
            _mine.Clear();
            _mineFloors.Clear();
            _ownCut = false;
            var lockMat = Mats3D.Solid(new Color(0.85f, 0.70f, 0.25f), 0.4f, 0.6f);
            // столбы на углах: концы рёбер (стены, проёмы, окна, двери) — чтобы на стыке двух стен не было щели
            var posts = new Dictionary<(int, int), (SVec at, int tier, bool seen, Node3D owner)>();
            foreach (var st in s.Structures)
            {
                ById[st.Id] = st;
                bool floor = Building.IsFloor(st.Kind);
                var node = new Node3D();
                AddChild(node);
                if (floor) _floors.Add((st.Poly, st.Tier));
                if (st.Mine) { if (floor) _mineFloors.Add(st.Poly); else _mine.Add(node); }
                if (!floor)
                    foreach (var end in new[] { st.A, st.B })
                    {
                        var k = ((int)System.MathF.Round(end.X * 20), (int)System.MathF.Round(end.Y * 20));
                        if (!posts.TryGetValue(k, out var p) || st.Tier > p.tier) posts[k] = (end, st.Tier, st.Seen, node);
                    }
                else if (st.Kind == StructKind.Wall || (st.Kind == StructKind.Window && st.Open) || (st.Kind == StructKind.Door && !st.Open))
                {
                    // осевые рёбра — прямоугольником толщины стены; диагональ (треугольник) обзор не режет
                    var d = st.B - st.A;
                    if (System.MathF.Abs(d.X) < 1e-3f || System.MathF.Abs(d.Y) < 1e-3f)
                    {
                        var half = new SVec(Building.Thick * 0.5f, Building.Thick * 0.5f);
                        Occluders.Add(new WallInfo { Min = SVec.Min(st.A, st.B) - half, Max = SVec.Max(st.A, st.B) + half, Height = Building.WallH, Kind = WallKind.Built, Id = -st.Id });
                    }
                }
                var mat = Mats3D.Building(st.Tier, st.Seen, floor, st.Kind == StructKind.Door);
                if (floor) Pieces3D.Floor(node, st.Poly, mat);
                else
                {
                    Pieces3D.Edge(node, st.Kind, st.A, st.B, mat, st.Open);
                    if (st.Kind == StructKind.Door) Pieces3D.Door(node, st.A, st.B, st.Open, mat, st.Locked ? lockMat : null);
                }
                // повреждённое темнеет (как в 2D): 100% — как новое, 0% — почти вдвое темнее
                SetShade(node, 0.55f + 0.45f * Mathf.Clamp(st.Hp, 0f, 1f));
            }
            foreach (var (at, tier, seen, owner) in posts.Values)
            {
                var post = W3.AddBox(owner, W3.V(at, Building.WallH * 0.5f), new Vector3(Building.Thick + 0.02f, Building.WallH + 0.02f, Building.Thick + 0.02f), 0, Mats3D.Building(tier, seen, false));
                post.SetInstanceShaderParameter("shade", 1f);
            }
        }

        readonly List<Node3D> _mine = new();
        readonly List<SVec[]> _mineFloors = new();
        bool _ownCut;

        /// <summary>Стою на полу своей базы (шкаф мой) — все её стены срезаны до низа, как в 2D.</summary>
        public bool OwnCut => _ownCut;

        static void SetShade(Node n, float k)
        {
            foreach (var c in n.GetChildren())
                if (c is GeometryInstance3D g) g.SetInstanceShaderParameter("shade", k);
        }

        static void SetCutAll(Node n, bool on)
        {
            foreach (var c in n.GetChildren())
                if (c is GeometryInstance3D g) g.SetInstanceShaderParameter("cut_all", on ? 1f : 0f);
        }

        /// <summary>Каждый кадр: на полу своей базы — срезать все её стены; сошёл — вернуть.</summary>
        public void UpdateOwnCut(SVec me, bool alive)
        {
            bool own = false;
            if (alive)
                foreach (var poly in _mineFloors)
                    if (Inside(poly, me)) { own = true; break; }
            if (own == _ownCut) return;
            _ownCut = own;
            foreach (var n in _mine) SetCutAll(n, own);
        }
    }

    /// <summary>
    /// Призрак Плана и установленного в руках, подсветка слотов пола (D52), рамка цели киянки и E.
    /// Перестраивается только когда меняется то, что рисуем.
    /// </summary>
    public partial class Ghost3D : Node3D
    {
        string _key = "";
        static readonly Color Ok = new(0.35f, 0.95f, 0.55f, 0.45f), Bad = new(1f, 0.35f, 0.3f, 0.45f);
        static readonly Color[] SlotCol = { new(0.35f, 0.95f, 0.55f, 0.35f), new(1f, 0.35f, 0.3f, 0.35f), new(1f, 0.85f, 0.3f, 0.35f), new(0.4f, 0.8f, 1f, 0.5f) };

        bool _ownCut;

        public void Sync(SelfInfo self, StructSnap? hammerTarget, bool demolish, bool hammerOk, StructSnap? door, bool ownCut = false)
        {
            _ownCut = ownCut;
            var sb = new System.Text.StringBuilder();
            if (ownCut) sb.Append('C');
            if (self != null && !self.Dead)
            {
                if (self.HasZone && (self.HoldingPlan || self.HeldDeploy)) sb.Append($"Z{self.ZoneCell}");
                if (self.HoldingPlan) sb.Append($"P{(int)self.GhostPiece}{self.BuildOk}{Poly(self.GhostPoly)}{self.GhostA}{self.GhostB}");
                if (self.HeldDeploy) sb.Append($"D{self.DeployOk}{self.DeployPos}{self.DeployHalf}");
                if (self.DeploySlots != null) foreach (var (p, st) in self.DeploySlots) sb.Append($"S{st}{Poly(p)}");
                if (self.InteractHalf != default && door == null && hammerTarget == null) sb.Append($"E{self.InteractAt}{self.InteractHalf}");
            }
            if (hammerTarget is StructSnap h) sb.Append($"H{h.Id}{demolish}{hammerOk}");
            if (door is StructSnap dr) sb.Append($"O{dr.Id}{dr.Open}");
            string key = sb.ToString();
            if (key == _key) return;
            _key = key;
            W3.ClearChildren(this);
            if (key.Length == 0 || self == null) return;

            if (self.HasZone && (self.HoldingPlan || self.HeldDeploy) && !self.Dead) Zone(self.ZoneCell);
            if (self.HoldingPlan && !self.Dead)
            {
                var m = Mats3D.Glow(self.BuildOk ? Ok : Bad, 1.2f, true);
                if (Building.IsFloor(self.GhostPiece) && self.GhostPoly != null) Pieces3D.Floor(this, self.GhostPoly, m);
                else if (self.GhostA != self.GhostB)
                {
                    Pieces3D.Edge(this, self.GhostPiece, self.GhostA, self.GhostB, m);
                    if (self.GhostPiece == StructKind.Door) Pieces3D.Door(this, self.GhostA, self.GhostB, false, m, null);
                }
            }
            if (self.HeldDeploy && !self.Dead && self.DeployHalf != default)
            {
                var m = Mats3D.Glow(self.DeployOk ? Ok : Bad, 1.2f, true);
                W3.AddBox(this, W3.V(self.DeployPos, 0.5f), new Vector3(self.DeployHalf.X * 2, 1f, self.DeployHalf.Y * 2), 0, m, false);
            }
            if (self.DeploySlots != null && !self.Dead)
                foreach (var (poly, st) in self.DeploySlots)
                {
                    if (poly == null || poly.Length < 3) continue;
                    var flat = new MeshInstance3D { Mesh = Pieces3D.Prism(poly, Pieces3D.FloorTop + 0.01f, Pieces3D.FloorTop + 0.03f), MaterialOverride = Mats3D.Glow(SlotCol[System.Math.Clamp(st, 0, 3)], 1f, true) };
                    flat.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                    AddChild(flat);
                }
            if (hammerTarget is StructSnap t)
            {
                var m = Mats3D.Glow(demolish ? new Color(1f, 0.35f, 0.3f, 0.35f) : hammerOk ? new Color(1f, 0.75f, 0.3f, 0.35f) : new Color(0.7f, 0.7f, 0.7f, 0.3f), 1f, true);
                Outline(t, m, 0.06f);
            }
            if (door is StructSnap ds) Outline(ds, Mats3D.Glow(new Color(1f, 0.95f, 0.8f, 0.25f), 1f, true), 0.04f);
            if (self.InteractHalf != default && door == null && hammerTarget == null && !self.Dead)
            {
                var m = Mats3D.Glow(new Color(1f, 0.95f, 0.8f, 0.22f), 1f, true);
                W3.AddBox(this, W3.V(self.InteractAt, 0.05f), new Vector3(self.InteractHalf.X * 2 + 0.2f, 0.06f, self.InteractHalf.Y * 2 + 0.2f), 0, m, false);
            }
        }

        void Outline(StructSnap t, Material m, float grow)
        {
            if (Building.IsFloor(t.Kind)) { Pieces3D.Floor(this, t.Poly, m); return; }
            var d = t.B - t.A; float len = d.Length();
            if (len < 1e-3f) return;
            float h = _ownCut && t.Mine ? 0.45f : Building.WallH;   // срезанная стена — рамка той же низкой высоты
            W3.AddBox(this, W3.V((t.A + t.B) * 0.5f, h * 0.5f), new Vector3(len + grow, h + grow, Building.Thick + grow * 2), W3.Yaw(d.X, d.Y), m, false);
        }

        /// <summary>Зона шкафа под курсором: пунктир по внешним краям клеток зоны (круг по клеткам, как в 2D).</summary>
        void Zone((int i, int j) cell)
        {
            var m = Mats3D.Glow(new Color(1f, 0.85f, 0.4f, 0.6f), 1.4f, true);
            float c = Building.Cell, y = Pieces3D.FloorTop + 0.04f;
            void Dash(SVec p0, SVec p1)
            {
                var dir = p1 - p0; float len = dir.Length();
                if (len < 1e-3f) return;
                for (float s = 0; s < len; s += 0.5f)
                {
                    float e = System.MathF.Min(s + 0.25f, len);
                    var mid = p0 + dir * ((s + e) * 0.5f / len);
                    W3.AddBox(this, W3.V(mid, y), new Vector3(e - s, 0.03f, 0.06f), W3.Yaw(dir.X, dir.Y), m, false);
                }
            }
            int R = World.ClaimRadius;
            for (int di = -R - 1; di <= R + 1; di++)
                for (int dj = -R - 1; dj <= R + 1; dj++)
                {
                    if (!World.InClaimZone(di, dj)) continue;
                    float x0 = (cell.i + di) * c, y0 = (cell.j + dj) * c;
                    if (!World.InClaimZone(di, dj - 1)) Dash(new SVec(x0, y0), new SVec(x0 + c, y0));
                    if (!World.InClaimZone(di, dj + 1)) Dash(new SVec(x0, y0 + c), new SVec(x0 + c, y0 + c));
                    if (!World.InClaimZone(di - 1, dj)) Dash(new SVec(x0, y0), new SVec(x0, y0 + c));
                    if (!World.InClaimZone(di + 1, dj)) Dash(new SVec(x0 + c, y0), new SVec(x0 + c, y0 + c));
                }
        }

        static string Poly(SVec[] p)
        {
            if (p == null) return "-";
            var sb = new System.Text.StringBuilder();
            foreach (var v in p) sb.Append($"{v.X:0.00},{v.Y:0.00};");
            return sb.ToString();
        }
    }
}
