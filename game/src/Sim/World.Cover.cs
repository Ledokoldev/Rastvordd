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

        /// <summary>Стены карты и построек в радиусе — через сетку, в свой список.</summary>
        public List<MapWall> WallsInRadius(Vector2 c, float r, List<MapWall> into) => _wallGrid.QueryCircle(c, r, into);
    }
}
