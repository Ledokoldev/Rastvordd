using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>
    /// Отсечение далёкого (27.10): неподвижные виды мира раскладываются по ячейкам 32 м (контейнеры с Y-сортировкой —
    /// сортируются вместе с остальным миром), ячейки вне экрана выключаются целиком: движок не обходит тысячи узлов
    /// за кадром. Собственную видимость узлов (пол, растворение стен) не трогаем — выключается только контейнер.
    /// </summary>
    public sealed class ChunkCull
    {
        const float Cell = 32f * Iso.PPM;
        static readonly HashSet<string> Static = new()
        {
            "DecorView2D", "DecorRow", "WallView2D", "SlantWallView2D", "RoofView2D", "TreeView2D", "OreView2D",
            "PipeSegView", "AmbLamp", "AmbFlag", "LightOccluder2D", "FloorView2D", "TriFloorView2D",
        };

        readonly Node2D _world;
        readonly Dictionary<(int, int), Node2D> _cells = new();
        readonly HashSet<Node2D> _boxes = new();

        public ChunkCull(Node2D world) => _world = world;

        /// <summary>Разложить новые неподвижные виды по ячейкам (дёшево звать раз в пару секунд: новые деревья, постройки).</summary>
        public void Collect()
        {
            var move = new List<Node2D>();
            foreach (var ch in _world.GetChildren())
                if (ch is Node2D n && !_boxes.Contains(n) && Static.Contains(n.GetType().Name)) move.Add(n);
            foreach (var n in move)
            {
                var p = n.GlobalPosition;
                var k = (Mathf.FloorToInt(p.X / Cell), Mathf.FloorToInt(p.Y / Cell));
                if (!_cells.TryGetValue(k, out var box))
                {
                    box = new Node2D { YSortEnabled = true, Name = $"cell_{k.Item1}_{k.Item2}" };
                    _world.AddChild(box);
                    _cells[k] = box; _boxes.Add(box);
                }
                n.Reparent(box, true);
            }
        }

        /// <summary>Включить ячейки, задевающие видимую область (с запасом на высокие спрайты и тени).</summary>
        public void Update(Rect2 view)
        {
            var r = view.Grow(Cell * 0.75f);
            foreach (var (k, box) in _cells)
            {
                var cr = new Rect2(k.Item1 * Cell, k.Item2 * Cell, Cell, Cell);
                bool on = r.Intersects(cr);
                if (box.Visible != on) box.Visible = on;
            }
        }
    }
}
