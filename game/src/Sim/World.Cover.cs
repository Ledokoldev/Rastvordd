using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>Запросы для боя ботов (укрытия): что останавливает пулю и какие стены рядом. Только чтение мира.</summary>
    public sealed partial class World
    {
        /// <summary>Пролетит ли пуля от a до b: стены (кроме проёмов окон), камни и деревья, установленное. Люди не считаются.</summary>
        public bool BulletClear(Vector2 a, Vector2 b)
        {
            foreach (var w in WallsAlong(a, b))
                if (!w.PassBullets && Geo.SegmentAabb(a, b, w.Wall.Min, w.Wall.Max, out _)) return false;
            foreach (var n in NodesAlong(a, b))
                if (Geo.SegmentCircle(a, b, n.Pos, n.Radius, out _)) return false;
            foreach (var d in DeploysAlong(a, b))
                if (d.Solid && Geo.SegmentAabb(a, b, d.Min, d.Max, out _)) return false;
            return true;
        }

        /// <summary>
        /// Долетит ли пуля (как в StepProjectiles, D56): высокое (стены, ящики, деревья, бочки) — нет; низкое (блоки, камни,
        /// мешки) — пуля идёт поверх, кроме случая, когда стрелок или цель присели вплотную (≤ 2,5 м) за ним.
        /// BulletClear (любое препятствие) — для выбора укрытий, этот — для решения «стрелять».
        /// </summary>
        public bool ShotClear(Vector2 a, Vector2 b, bool shooterCrouched, bool targetCrouched)
        {
            bool LowBlocks(Vector2 min, Vector2 max) =>
                (shooterCrouched && Vector2.Distance(Vector2.Clamp(a, min, max), a) <= Hitbox.CoverReach)
                || (targetCrouched && Vector2.Distance(Vector2.Clamp(b, min, max), b) <= Hitbox.CoverReach);
            foreach (var w in WallsAlong(a, b))
            {
                if (w.PassBullets || !Geo.SegmentAabb(a, b, w.Wall.Min, w.Wall.Max, out _)) continue;
                if (w.Wall.Height >= Hitbox.GunH || LowBlocks(w.Wall.Min, w.Wall.Max)) return false;
            }
            foreach (var n in NodesAlong(a, b))
            {
                if (!Geo.SegmentCircle(a, b, n.Pos, n.Radius, out _)) continue;
                if (n.Kind is NodeKind.Tree or NodeKind.Barrel || LowBlocks(n.Pos - new Vector2(n.Radius), n.Pos + new Vector2(n.Radius))) return false;
            }
            foreach (var d in DeploysAlong(a, b))
                if (d.Solid && Geo.SegmentAabb(a, b, d.Min, d.Max, out _) && LowBlocks(d.Min, d.Max)) return false;
            return true;
        }

        /// <summary>Стены карты и построек в радиусе — через сетку, в свой список.</summary>
        public List<MapWall> WallsInRadius(Vector2 c, float r, List<MapWall> into) => _wallGrid.QueryCircle(c, r, into);
    }
}
