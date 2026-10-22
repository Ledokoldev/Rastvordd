using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>Подсказка у курсора над постройкой: что это, из чего и сколько прочности осталось.</summary>
    public partial class StructTip : Control
    {
        StructSnap? _s;

        public StructTip() { MouseFilter = MouseFilterEnum.Ignore; SetAnchorsPreset(LayoutPreset.FullRect); }

        /// <summary>Постройка под точкой (метры): сначала дверь/стена у ближайшего ребра, потом фундамент.</summary>
        public static StructSnap? Pick(List<StructSnap> all, SVec p)
        {
            StructSnap? frame = null;
            float best = 0.5f;
            foreach (var s in all)
            {
                if (Building.IsFloor(s.Kind)) continue;
                float d = Building.SegDist(p, s.A, s.B);
                if (s.Kind == StructKind.Door && d < 0.5f) return s;
                if (d < best) { best = d; frame = s; }
            }
            if (frame != null) return frame;
            var (ci, cj) = Building.CellOf(p);
            foreach (var s in all)
                if ((s.Kind == StructKind.Foundation && s.I == ci && s.J == cj) || (s.Kind == StructKind.TriFoundation && Building.PointInPoly(p, s.Poly))) return s;
            return null;
        }

        public void Set(StructSnap? s)
        {
            if (s == null && _s == null) return;
            _s = s;
            QueueRedraw();
        }

        public override void _Process(double delta) { if (_s != null) QueueRedraw(); }

        public override void _Draw()
        {
            if (_s is not StructSnap s) return;
            float max = Building.MaxHp(s.Kind, s.Tier), k = Mathf.Clamp(s.Hp, 0, 1);   // в снапшоте прочность — доля
            string title = $"{Building.KindName(s.Kind)} · {Building.TierNames[s.Tier]}{(s.Locked ? " · замок" : "")}";
            string hp = $"{Mathf.CeilToInt(s.Hp * max)} / {(int)max}";
            float tw = UiTheme.TextWidth(title, UiTheme.FontSmall, UiTheme.Semi);
            float w = Mathf.Max(tw, 140) + 28, h = 62;
            var at = GetViewport().GetMousePosition() + new Vector2(22, 20);
            var vs = GetViewportRect().Size;
            at.X = Mathf.Min(at.X, vs.X - w - 4); at.Y = Mathf.Min(at.Y, vs.Y - h - 4);
            DrawStyleBox(UiTheme.Glass(0, 0.8f, 12), new Rect2(at, new Vector2(w, h)));
            UiTheme.DrawText(this, at + new Vector2(14, 22), title, UiTheme.FontSmall, UiTheme.Ink, UiTheme.Semi);
            var col = UiTheme.Bad.Lerp(UiTheme.Good, k);
            UiTheme.DrawBar(this, new Rect2(at + new Vector2(14, 32), new Vector2(w - 28, 6)), k, col);
            UiTheme.DrawText(this, at + new Vector2(14, 54), hp, UiTheme.FontCaption, UiTheme.Dim);
        }
    }
}
