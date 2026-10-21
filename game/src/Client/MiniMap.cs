using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>
    /// Миникарта в правом верхнем углу: местность вокруг (север сверху), РТ с названиями, видимые постройки и установленное,
    /// дом (за краем — стрелка на рамке с расстоянием), ты — стрелка в центре.
    /// </summary>
    public partial class MiniMap : Control
    {
        public const float Side = 200;
        const float Radius = 40;          // метров от центра до края
        readonly MapView _full;
        SVec _me;
        float _facing;
        SVec? _home;
        string _place;
        readonly List<StructSnap> _structs = new();
        readonly List<DeploySnap> _deploys = new();

        public MiniMap(MapView full)
        {
            _full = full;
            CustomMinimumSize = new Vector2(Side, Side + 18);
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public void SetState(SVec me, float facing, SVec? home, string place, List<StructSnap> structs, List<DeploySnap> deploys)
        {
            _me = me; _facing = facing; _home = home; _place = place;
            _structs.Clear(); _structs.AddRange(structs);
            _deploys.Clear(); _deploys.AddRange(deploys);
            QueueRedraw();
        }

        public override void _Draw()
        {
            var map = _full.Map;
            float k = Side / (Radius * 2);                   // пикселей на метр
            var c = new Vector2(Side / 2, Side / 2);
            Vector2 P(SVec w) => c + new Vector2(w.X - _me.X, w.Y - _me.Y) * k;
            var box = new Rect2(0, 0, Side, Side);

            DrawRect(box, new Color(0.09f, 0.18f, 0.26f));
            // вся карта острова, сдвинутая так, что ты в центре; лишнее обрезаем по рамке
            var tex = _full.Texture;
            var origin = P(new SVec(-map.HalfSize, -map.HalfSize));
            var full = new Rect2(origin, new Vector2(map.HalfSize * 2 * k, map.HalfSize * 2 * k));
            var vis = full.Intersection(box);
            if (vis.Size.X > 0 && vis.Size.Y > 0)
            {
                float texPerPx = tex.GetWidth() / full.Size.X;
                var src = new Rect2((vis.Position - full.Position) * texPerPx, vis.Size * texPerPx);
                DrawTextureRectRegion(tex, vis, src);
            }

            var font = UiTheme.Text;
            // РТ
            foreach (var (name, mn, mx) in _full.Monuments)
            {
                var r = new Rect2(P(mn), P(mx) - P(mn)).Intersection(box);
                if (r.Size.X <= 0 || r.Size.Y <= 0) continue;
                DrawRect(r, new Color(0.16f, 0.15f, 0.14f, 0.9f));
                DrawRect(r, new Color(0.95f, 0.85f, 0.5f, 0.9f), false, 1);
                var sz = font.GetStringSize(name, HorizontalAlignment.Left, -1, 11);
                var at = new Vector2(Mathf.Clamp(r.GetCenter().X - sz.X / 2, 2, Side - sz.X - 2), Mathf.Clamp(r.Position.Y - 3, 26, Side - 3));
                DrawString(font, at + Vector2.One, name, HorizontalAlignment.Left, -1, 11, new Color(0, 0, 0, 0.9f));
                DrawString(font, at, name, HorizontalAlignment.Left, -1, 11, new Color(0.98f, 0.92f, 0.7f));
            }
            // постройки: фундаменты — светлые клетки, стены — линии
            foreach (var s in _structs)
            {
                if (s.Kind == StructKind.Foundation)
                {
                    var a = P(new SVec(s.I * Building.Cell, s.J * Building.Cell));
                    var r = new Rect2(a, new Vector2(Building.Cell * k, Building.Cell * k));
                    if (box.Encloses(r)) DrawRect(r, new Color(0.42f, 0.34f, 0.26f, 0.95f));
                }
                else if (s.Kind != StructKind.Door)
                {
                    var e = Building.EdgeCenter(s.I, s.J, s.Dir);
                    var half = s.Dir == 0 ? new SVec(Building.Cell / 2, 0) : new SVec(0, Building.Cell / 2);
                    var a = P(e - half); var b = P(e + half);
                    if (box.HasPoint(a) && box.HasPoint(b)) DrawLine(a, b, new Color(0.12f, 0.1f, 0.08f), 2f);
                }
            }
            foreach (var d in _deploys)
            {
                var p = P(d.Pos);
                if (!box.Grow(-2).HasPoint(p)) continue;
                var col = d.Kind == DeployKind.Claim ? new Color(0.35f, 0.75f, 1f) : d.Burning ? new Color(1f, 0.55f, 0.2f) : new Color(0.85f, 0.8f, 0.7f);
                DrawRect(new Rect2(p - new Vector2(1.5f, 1.5f), new Vector2(3, 3)), col);
            }
            // дом: внутри — квадрат, снаружи — стрелка на рамке и расстояние
            if (_home is SVec h)
            {
                var hp = P(h);
                var blue = new Color(0.35f, 0.75f, 1f);
                if (box.Grow(-4).HasPoint(hp)) DrawRect(new Rect2(hp - new Vector2(4, 4), new Vector2(8, 8)), blue, false, 2);
                else
                {
                    var dir = (hp - c).Normalized();
                    float t = Mathf.Min((Side / 2 - 8) / Mathf.Max(Mathf.Abs(dir.X), 0.001f), (Side / 2 - 8) / Mathf.Max(Mathf.Abs(dir.Y), 0.001f));
                    var e = c + dir * t;
                    var perp = new Vector2(-dir.Y, dir.X);
                    DrawColoredPolygon(new[] { e + dir * 6, e - dir * 4 + perp * 5, e - dir * 4 - perp * 5 }, blue);
                    string dist = $"дом {SVec.Distance(_me, h):0} м";
                    var sz = font.GetStringSize(dist, HorizontalAlignment.Left, -1, 11);
                    var at = new Vector2(Mathf.Clamp(e.X - sz.X / 2, 2, Side - sz.X - 2), Mathf.Clamp(e.Y + (dir.Y > 0 ? -10 : 18), 12, Side - 3));
                    DrawString(font, at + Vector2.One, dist, HorizontalAlignment.Left, -1, 11, new Color(0, 0, 0, 0.9f));
                    DrawString(font, at, dist, HorizontalAlignment.Left, -1, 11, blue);
                }
            }
            // ты
            var fd = new Vector2(Mathf.Cos(_facing), Mathf.Sin(_facing));
            var fp = new Vector2(-fd.Y, fd.X);
            DrawColoredPolygon(new[] { c + fd * 8, c - fd * 5 + fp * 5, c - fd * 2, c - fd * 5 - fp * 5 }, new Color(1f, 0.35f, 0.25f));
            DrawRect(box, new Color(1, 1, 1, 0.25f), false, 1);
            DrawString(font, new Vector2(Side / 2 - 4, 13), "С", HorizontalAlignment.Left, -1, 11, new Color(1, 1, 1, 0.7f));

            string cap = (_place ?? "") + (_place != null ? "  ·  " : "") + "M — карта";
            var cs = font.GetStringSize(cap, HorizontalAlignment.Left, -1, 12);
            DrawString(font, new Vector2(Side - cs.X + 1, Side + 15), cap, HorizontalAlignment.Left, -1, 12, new Color(0, 0, 0, 0.9f));
            DrawString(font, new Vector2(Side - cs.X, Side + 14), cap, HorizontalAlignment.Left, -1, 12, UiTheme.Ink);
        }
    }
}
