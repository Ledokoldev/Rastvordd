using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>Сколько места в клетке фундамента занимает установленное (D52, docs/design/16-building-grid.md).</summary>
    public enum Footprint { Quarter, Half, Full }

    /// <summary>
    /// Размеры установленного: слоты на фундаменте и прямоугольный хитбокс. Таблица — данные: новая станция
    /// (самогонный аппарат, генератор, химстол, медстол, электропечь, оружейный стол, бочка 200 л, ректификационная колонна…)
    /// получает свой DeployKind и одну строку здесь.
    /// W — хитбокс вдоль «длинной» стороны (вдоль X при повороте 0/2), D — поперёк, метры.
    /// </summary>
    public static class Footprints
    {
        public readonly struct Spec
        {
            public readonly Footprint Size;
            public readonly float W, D;
            public Spec(Footprint size, float w, float d) { Size = size; W = w; D = d; }
        }

        static readonly Dictionary<DeployKind, Spec> Table = new()
        {
            [DeployKind.Box] = new(Footprint.Quarter, 0.85f, 0.85f),
            [DeployKind.SleepBag] = new(Footprint.Quarter, 0.85f, 0.6f),
            [DeployKind.Claim] = new(Footprint.Quarter, 0.5f, 0.5f),
            [DeployKind.Workbench] = new(Footprint.Half, 1.85f, 0.85f),   // верстак 1–3
            [DeployKind.Furnace] = new(Footprint.Half, 1.7f, 0.85f),
            [DeployKind.Lathe] = new(Footprint.Full, 1.85f, 1.85f),
            [DeployKind.Recycler] = new(Footprint.Full, 1.8f, 1.8f),      // стоит на РТ, вне сетки
            // будущие станции: [DeployKind.Still] = Half, [DeployKind.Generator] = Full, [DeployKind.ChemTable] = Half,
            // [DeployKind.MedTable] = Half, [DeployKind.ElectricFurnace] = Full, [DeployKind.GunTable] = Full,
            // [DeployKind.Barrel200] = Full, [DeployKind.Column] = Full, малый ящик / лампа / канистра 20 л = Quarter
        };

        public static Spec Of(DeployKind k) => Table.TryGetValue(k, out var s) ? s : new Spec(Footprint.Quarter, 0.85f, 0.85f);
        public static Footprint SizeOf(DeployKind k) => Of(k).Size;

        /// <summary>Половины сторон хитбокса (по осям мира) для поворота: 0/2 — длинная сторона вдоль X, 1/3 — вдоль Y.</summary>
        public static Vector2 HalfOf(DeployKind k, int rot)
        {
            var s = Of(k);
            return (rot & 1) == 0 ? new Vector2(s.W, s.D) * 0.5f : new Vector2(s.D, s.W) * 0.5f;
        }

        public static string SizeName(Footprint f) => f switch { Footprint.Quarter => "четверть", Footprint.Half => "половина", _ => "целая клетка" };

        // ---------- слоты пола ----------

        // слот в треугольнике: целый квадрат 1×1 в треугольник со стороной 2 м не влезает — два квадрата 0,6 м у основания,
        // целиком внутри (и в стороне от косых стен)
        public const float TriSlot = 0.6f;
        const float TriAlong = 0.34f, TriIn = 0.35f;   // центры слотов: доля основания и отступ от него внутрь, м

        /// <summary>Слоты пола: центр и оси (u — вдоль, v — поперёк) и половина стороны. Квадрат — 4 слота 1×1 (индекс qx + 2·qy), треугольник — 2.</summary>
        public static (Vector2 c, Vector2 u, Vector2 v, float h)[] Slots(Structure f)
        {
            if (f.Kind == StructKind.Foundation)
            {
                var o = new Vector2(f.I * Building.Cell, f.J * Building.Cell);
                var r = new (Vector2, Vector2, Vector2, float)[4];
                for (int q = 0; q < 4; q++) r[q] = (o + new Vector2((q & 1) + 0.5f, (q >> 1) + 0.5f), Vector2.UnitX, Vector2.UnitY, 0.5f);
                return r;
            }
            var (u, v) = TriAxes(f);
            return new[]
            {
                (f.A + (f.B - f.A) * TriAlong + v * TriIn, u, v, TriSlot * 0.5f),
                (f.A + (f.B - f.A) * (1 - TriAlong) + v * TriIn, u, v, TriSlot * 0.5f),
            };
        }

        static (Vector2 u, Vector2 v) TriAxes(Structure f)
        {
            var u = Vector2.Normalize(f.B - f.A);
            var v = new Vector2(-u.Y, u.X);
            if (Vector2.Dot(f.C - (f.A + f.B) * 0.5f, v) < 0) v = -v;
            return (u, v);
        }

        public static int SlotCount(Structure f) => f.Kind == StructKind.Foundation ? 4 : 2;

        /// <summary>Многоугольник слота (для подсветки).</summary>
        public static Vector2[] SlotPoly((Vector2 c, Vector2 u, Vector2 v, float h) s, float shrink = 0.06f)
        {
            float h = s.h - shrink;
            return new[] { s.c - s.u * h - s.v * h, s.c + s.u * h - s.v * h, s.c + s.u * h + s.v * h, s.c - s.u * h + s.v * h };
        }

        public static Vector2[] RectPoly(Vector2 min, Vector2 max) => new[] { min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y) };

        /// <summary>Какие слоты займёт предмет, если курсор в точке at на полу f. 0 — не помещается (в треугольник — только четверть).</summary>
        public static int MaskFor(Structure f, Footprint size, int rot, Vector2 at)
        {
            if (f.Kind == StructKind.TriFoundation)
            {
                if (size != Footprint.Quarter) return 0;
                var sl = Slots(f);
                return Vector2.DistanceSquared(at, sl[0].c) <= Vector2.DistanceSquared(at, sl[1].c) ? 1 : 2;
            }
            float lx = at.X - f.I * Building.Cell, ly = at.Y - f.J * Building.Cell;
            int qx = Math.Clamp((int)MathF.Floor(lx), 0, 1), qy = Math.Clamp((int)MathF.Floor(ly), 0, 1);
            return size switch
            {
                Footprint.Quarter => 1 << (qx + 2 * qy),
                Footprint.Half => (rot & 1) == 0 ? 3 << (2 * qy) : 5 << qx,
                _ => 15,
            };
        }

        /// <summary>Центр занятых слотов.</summary>
        public static Vector2 MaskCenter(Structure f, int mask)
        {
            var sl = Slots(f);
            Vector2 sum = Vector2.Zero; int n = 0;
            for (int q = 0; q < sl.Length; q++) if ((mask & (1 << q)) != 0) { sum += sl[q].c; n++; }
            return n == 0 ? f.Center : sum / n;
        }

        /// <summary>Все варианты постановки в пол: (маска, поворот). Для ботов и пересадки старых сейвов.</summary>
        public static IEnumerable<(int mask, int rot)> Options(Structure f, Footprint size)
        {
            if (f.Kind == StructKind.TriFoundation)
            {
                if (size != Footprint.Quarter) yield break;
                yield return (1, 0); yield return (2, 0);
                yield break;
            }
            switch (size)
            {
                case Footprint.Quarter: for (int q = 0; q < 4; q++) yield return (1 << q, 0); break;
                case Footprint.Half: yield return (3, 0); yield return (12, 0); yield return (5, 1); yield return (10, 1); break;
                default: for (int r = 0; r < 4; r++) yield return (15, r); break;
            }
        }
    }

    public sealed partial class World
    {
        /// <summary>Занятые слоты пола (id пола → битовая маска). Собирается из установленного: снос/подбор освобождают слоты.</summary>
        readonly Dictionary<int, int> _slotUse = new();
        /// <summary>Отладка: почему BestFit отбрасывал варианты (печатается в итоге --simtest).</summary>
        public readonly Dictionary<string, int> FitRejects = new();

        public int SlotsUsed(Structure f) => f == null ? 0 : _slotUse.GetValueOrDefault(f.Id);

        Structure FloorById(int id)
        {
            if (id == 0) return null;
            foreach (var s in Structures) if (s.Id == id && s.IsFloor) return s;
            return null;
        }

        void OccupySlots(Deployable d, bool on)
        {
            if (d.FloorId == 0 || d.Slots == 0) return;
            int m = _slotUse.GetValueOrDefault(d.FloorId);
            m = on ? m | d.Slots : m & ~d.Slots;
            if (m == 0) _slotUse.Remove(d.FloorId); else _slotUse[d.FloorId] = m;
        }

        /// <summary>Убрать установленное из мира (сломали, подобрали): слоты пола освобождаются.</summary>
        void RemoveDeployable(Deployable d)
        {
            if (!Deployables.Remove(d)) return;
            _deployGrid.Remove(d);
            OccupySlots(d, false);
        }

        /// <summary>Пол снесён: стоявшее на нём остаётся, но уже вне сетки (свободно поставленное).</summary>
        void DropSlotsOf(Structure floor)
        {
            _slotUse.Remove(floor.Id);
            foreach (var d in Deployables) if (d.FloorId == floor.Id) { d.FloorId = 0; d.Slots = 0; }
        }

        /// <summary>Слоты у дверного проёма: четверть перед проёмом держится свободной (с обеих сторон).</summary>
        public int DoorwayMask(Structure f)
        {
            var poly = f.Poly;
            var sl = Footprints.Slots(f);
            int m = 0;
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Length];
                if (!_edges.TryGetValue(Building.EdgeKey(a, b), out var e) || e.Kind != StructKind.Doorway) continue;
                var mid = (a + b) * 0.5f;
                for (int q = 0; q < sl.Length; q++) if (Vector2.Distance(sl[q].c, mid) < 0.95f) m |= 1 << q;
            }
            return m;
        }

        /// <summary>Проём на ребре a–b загородит стоящее в слотах у него (с любой стороны).</summary>
        bool DoorwayBlocked(Vector2 a, Vector2 b)
        {
            var mid = (a + b) * 0.5f;
            long key = Building.EdgeKey(a, b);
            foreach (var f in FloorsNear(mid, 2.5f))
            {
                int used = SlotsUsed(f);
                if (used == 0) continue;
                var poly = f.Poly;
                bool onEdge = false;
                for (int i = 0; i < poly.Length && !onEdge; i++) onEdge = Building.EdgeKey(poly[i], poly[(i + 1) % poly.Length]) == key;
                if (!onEdge) continue;
                var sl = Footprints.Slots(f);
                for (int q = 0; q < sl.Length; q++) if ((used & (1 << q)) != 0 && Vector2.Distance(sl[q].c, mid) < 0.95f) return true;
            }
            return false;
        }

        /// <summary>Куда и как встанет установленное.</summary>
        public struct DeployFit
        {
            public bool Ok;
            public string Why;
            public Vector2 Pos, Half;
            public int FloorId, Slots, Rot;
            public bool Snapped;              // в слоты пола
        }

        static Footprint SizeOfDeploy(DeployKind k) => Footprints.SizeOf(k);

        /// <summary>
        /// Проверка постановки: на полу — в слоты клетки под курсором (свой шкаф, свободные слоты, проход у двери);
        /// на голой земле — свободно, но целая клетка (станки) — только на фундамент.
        /// </summary>
        public DeployFit FitDeploy(Actor a, DeployKind kind, Vector2 at, int rot)
        {
            var size = SizeOfDeploy(kind);
            rot &= 3;
            var fit = new DeployFit { Rot = rot, Pos = at, Half = Footprints.HalfOf(kind, rot) };
            var floor = FloorAt(at);
            if (floor != null)
            {
                fit.Snapped = true;
                fit.FloorId = floor.Id;
                int mask = Footprints.MaskFor(floor, size, rot, at);
                if (mask == 0) { fit.Why = "в треугольник — только четверть (ящик, спальник, шкаф)"; fit.Pos = floor.Center; return fit; }
                fit.Slots = mask;
                fit.Pos = Footprints.MaskCenter(floor, mask);
                if (floor.Kind == StructKind.TriFoundation) fit.Half = Vector2.Min(fit.Half, new Vector2(Footprints.TriSlot * 0.5f));
                return CheckSlots(a, kind, floor, fit);
            }
            if (size == Footprint.Full) { fit.Why = $"{Footprints.SizeName(size)}: только на фундамент"; return fit; }
            var min = at - fit.Half; var max = at + fit.Half;
            var rect = Footprints.RectPoly(min, max);
            foreach (var f in FloorsNear(at, 2.5f))
                if (Building.PolysOverlap(rect, f.Poly)) { fit.Why = "наведи на фундамент — встанет в клетку"; return fit; }
            if (!Terrain.IsLand(at)) { fit.Why = "вода"; return fit; }
            if (!AreaClear(a, min, max, null, out fit.Why)) return fit;
            fit.Ok = true;
            return fit;
        }

        DeployFit CheckSlots(Actor a, DeployKind kind, Structure floor, DeployFit fit, bool people = true)
        {
            if (kind != DeployKind.Claim && a != null && ClaimAt(Building.CellOf(floor.Center)) is Deployable claim && !claim.Auth.Contains(a.Id))
            { fit.Why = "чужая база — нужен доступ к шкафу"; return fit; }
            if ((SlotsUsed(floor) & fit.Slots) != 0) { fit.Why = "место занято"; return fit; }
            if ((DoorwayMask(floor) & fit.Slots) != 0) { fit.Why = "проход у двери держим свободным"; return fit; }
            if (!AreaClear(a, fit.Pos - fit.Half, fit.Pos + fit.Half, floor, out fit.Why, people)) return fit;
            fit.Ok = true;
            return fit;
        }

        /// <summary>Хитбокс не задевает стены, узлы, другое установленное (вне сетки) и людей.</summary>
        bool AreaClear(Actor a, Vector2 min, Vector2 max, Structure floor, out string why, bool people = true)
        {
            why = "";
            var c = (min + max) * 0.5f;
            float r = MathF.Max(max.X - min.X, max.Y - min.Y);
            // стены постройки стоят на рёбрах — по краю слота допуск (толщина стены)
            var imin = min + new Vector2(0.16f); var imax = max - new Vector2(0.16f);
            foreach (var w in WallsNear(c, r))
            {
                if (floor != null && w.Owner != null) continue;   // в слотах: стены пола стоят на его рёбрах, слоты — внутри
                if (Geo.BoxOverlap(floor != null ? imin : min, floor != null ? imax : max, w.Wall.Min, w.Wall.Max)) { why = w.Owner != null ? "мешает стена" : "мешает препятствие"; return false; }
            }
            foreach (var n in NodesNear(c, r))
                if (Geo.CircleBox(n.Pos, n.Radius * 0.8f, min, max)) { why = "мешает дерево/камень"; return false; }
            foreach (var d in DeploysNear(c, r))
                if (Geo.BoxOverlap(min + new Vector2(0.03f), max - new Vector2(0.03f), d.Min, d.Max)) { why = "мешает установленное"; return false; }
            if (people)
                foreach (var o in Actors)
                    if (!o.Dead && !o.Spectator && Geo.CircleBox(o.Pos, o.Radius * 0.9f, min, max)) { why = o == a ? "стоишь на месте — отойди" : "мешает человек"; return false; }
            return true;
        }

        /// <summary>Подсветка слотов пола под курсором: многоугольник и состояние (0 — свободен, 1 — занят, 2 — проход у двери, 3 — займёт).</summary>
        public List<(Vector2[] poly, int state)> SlotHighlight(Actor a, DeployKind kind, Vector2 at, int rot)
        {
            var list = new List<(Vector2[], int)>();
            var floor = FloorAt(at);
            if (floor == null) return list;
            var sl = Footprints.Slots(floor);
            int used = SlotsUsed(floor), door = DoorwayMask(floor);
            int want = Footprints.MaskFor(floor, SizeOfDeploy(kind), rot, at);
            for (int q = 0; q < sl.Length; q++)
            {
                int bit = 1 << q;
                int st = (used & bit) != 0 ? 1 : (door & bit) != 0 ? 2 : (want & bit) != 0 ? 3 : 0;
                list.Add((Footprints.SlotPoly(sl[q]), st));
            }
            return list;
        }

        /// <summary>Поставить в слоты (после FitDeploy.Ok): позиция, хитбокс, учёт занятости.</summary>
        void ApplyFit(Deployable d, DeployFit fit)
        {
            d.Pos = fit.Pos;
            d.Half = fit.Half;
            d.Rot = fit.Rot;
            d.FloorId = fit.Snapped ? fit.FloorId : 0;
            d.Slots = fit.Snapped ? fit.Slots : 0;
            if (d.Storage != null) d.Storage.Pos = d.Pos;
        }

        /// <summary>
        /// Лучшее место рядом с точкой want в своей базе (для ботов): свободные слоты полов в радиусе, ближе к want;
        /// ящики — в углы, верстаки и печь — длинной стороной к стене; keepClear — точка, где бот стоит у верстаков.
        /// </summary>
        public DeployFit? BestFit(Actor a, DeployKind kind, Vector2 want, float radius, Vector2? keepClear = null)
        {
            var size = SizeOfDeploy(kind);
            DeployFit? best = null;
            float bs = float.MaxValue;
            foreach (var f in FloorsNear(want, radius))
            {
                if (ClaimAt(Building.CellOf(f.Center)) is not Deployable claim || !claim.Auth.Contains(a.Id)) continue;
                foreach (var (mask, rot) in Footprints.Options(f, size))
                {
                    var fit = new DeployFit { Rot = rot, FloorId = f.Id, Slots = mask, Snapped = true, Half = Footprints.HalfOf(kind, rot) };
                    fit.Pos = Footprints.MaskCenter(f, mask);
                    if (f.Kind == StructKind.TriFoundation) fit.Half = Vector2.Min(fit.Half, new Vector2(Footprints.TriSlot * 0.5f));
                    if (keepClear is Vector2 kc && Geo.CircleBox(kc, 0.75f, fit.Pos - fit.Half, fit.Pos + fit.Half)) { FitRejects[$"{kind}: место у верстаков"] = FitRejects.GetValueOrDefault($"{kind}: место у верстаков") + 1; continue; }
                    fit = CheckSlots(null, kind, f, fit, false);   // люди отойдут: бот встанет вне хитбокса
                    if (!fit.Ok) { FitRejects[$"{kind}: {fit.Why}"] = FitRejects.GetValueOrDefault($"{kind}: {fit.Why}") + 1; continue; }
                    float score = Vector2.Distance(fit.Pos, want);
                    int walls = WallSides(fit.Pos - fit.Half, fit.Pos + fit.Half);
                    if (size == Footprint.Quarter) score -= 0.3f * Math.Min(walls, 2);   // ящик — в угол
                    else if (size == Footprint.Half) score -= LongSideOnWall(fit) ? 0.6f : 0;   // верстак, печь — к стене
                    if (score < bs) { bs = score; best = fit; }
                }
            }
            return best;
        }

        /// <summary>Сколько сторон прямоугольника прилегает к стене/окну постройки.</summary>
        int WallSides(Vector2 min, Vector2 max)
        {
            int n = 0;
            var c = (min + max) * 0.5f;
            foreach (var probe in new[] { new Vector2(min.X - 0.12f, c.Y), new Vector2(max.X + 0.12f, c.Y), new Vector2(c.X, min.Y - 0.12f), new Vector2(c.X, max.Y + 0.12f) })
                if (NearestFloorEdge(probe, 0.2f, out _, out _, out long key, out _) && _edges.TryGetValue(key, out var e) && e.Kind is StructKind.Wall or StructKind.Window) n++;
            return n;
        }

        bool LongSideOnWall(DeployFit fit)
        {
            var min = fit.Pos - fit.Half; var max = fit.Pos + fit.Half;
            var c = fit.Pos;
            bool alongX = fit.Half.X >= fit.Half.Y;
            var probes = alongX ? new[] { new Vector2(c.X, min.Y - 0.12f), new Vector2(c.X, max.Y + 0.12f) } : new[] { new Vector2(min.X - 0.12f, c.Y), new Vector2(max.X + 0.12f, c.Y) };
            foreach (var p in probes)
                if (NearestFloorEdge(p, 0.3f, out _, out _, out long key, out _) && _edges.TryGetValue(key, out var e) && e.Kind is StructKind.Wall or StructKind.Window) return true;
            return false;
        }

        /// <summary>
        /// Пол поставлен: стоящее на нём вне сетки (шкаф ставят до фундамента) занимает ближайший слот без сдвига —
        /// бот считает свою базу от позиции шкафа.
        /// </summary>
        void AdoptDeploysOnFloor(Structure floor)
        {
            foreach (var d in DeploysNear(floor.Center, 1.6f))
            {
                if (d.FloorId != 0 || d.Kind == DeployKind.Recycler || !Building.PointInPoly(d.Pos, floor.Poly)) continue;
                TrySnapExisting(d, floor, false);
            }
        }

        /// <summary>Пересадить стоящее на полу в слот (ближайший свободный вариант). move — сдвинуть в центр слотов.</summary>
        bool TrySnapExisting(Deployable d, Structure floor, bool move)
        {
            var size = SizeOfDeploy(d.Kind);
            int used = SlotsUsed(floor);
            (int mask, int rot)? best = null; float bd = float.MaxValue;
            foreach (var (mask, rot) in Footprints.Options(floor, size))
            {
                if ((used & mask) != 0) continue;
                float dist = Vector2.Distance(Footprints.MaskCenter(floor, mask), d.Pos);
                if (dist < bd) { bd = dist; best = (mask, rot); }
            }
            if (best == null) return false;
            _deployGrid.Remove(d);
            d.FloorId = floor.Id;
            d.Slots = best.Value.mask;
            d.Rot = best.Value.rot;
            if (move)
            {
                d.Pos = Footprints.MaskCenter(floor, d.Slots);
                d.Half = Footprints.HalfOf(d.Kind, d.Rot);
                if (floor.Kind == StructKind.TriFoundation) d.Half = Vector2.Min(d.Half, new Vector2(Footprints.TriSlot * 0.5f));
                if (d.Storage != null) d.Storage.Pos = d.Pos;
            }
            InsertDeployGrid(d);
            OccupySlots(d, true);
            return true;
        }

        /// <summary>Сейв до сетки (v5 и раньше): установленное на полах — в слоты, где получится; остальное — как стояло.</summary>
        void ResnapLegacyDeploys()
        {
            int ok = 0, legacy = 0;
            foreach (var d in Deployables)
            {
                if (d.FloorId != 0 || d.Kind == DeployKind.Recycler) continue;
                var f = FloorAt(d.Pos);
                if (f == null) continue;
                if (TrySnapExisting(d, f, d.Kind != DeployKind.Claim)) ok++; else legacy++;
            }
            if (ok + legacy > 0) Console.WriteLine($"сейв до сетки: в слоты {ok}, оставлено как было {legacy}");
        }
    }
}
