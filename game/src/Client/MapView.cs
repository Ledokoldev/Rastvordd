using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>Карта острова (M): биомы, РТ с названиями, где ты и где твой межевой столб.</summary>
    public partial class MapView : Control
    {
        readonly MapInfo _map;
        readonly ImageTexture _tex;
        readonly List<(string name, SVec min, SVec max)> _monuments;
        SVec _me;
        float _facing;
        SVec? _home;

        static Color BiomeColor(Biome b) => b switch
        {
            Biome.Water => new Color(0.09f, 0.18f, 0.26f),
            Biome.Beach => new Color(0.62f, 0.58f, 0.46f),
            Biome.Grassland => new Color(0.36f, 0.43f, 0.24f),
            Biome.Forest => new Color(0.2f, 0.29f, 0.17f),
            Biome.Arid => new Color(0.55f, 0.47f, 0.33f),
            Biome.Tundra => new Color(0.78f, 0.8f, 0.82f),
            _ => new Color(0.3f, 0.34f, 0.22f),
        };

        public MapView(MapInfo map, List<(string, SVec, SVec)> monuments)
        {
            _map = map;
            _monuments = monuments;
            var img = Image.CreateEmpty(map.N, map.N, false, Image.Format.Rgb8);
            for (int y = 0; y < map.N; y++)
                for (int x = 0; x < map.N; x++)
                {
                    var b = map.Biomes[y * map.N + x];
                    var c = BiomeColor(b);
                    float h = map.Heights[y * map.N + x];
                    if (b != Biome.Water) c = c.Darkened(Mathf.Clamp(0.15f - h * 0.15f, 0, 0.15f));   // рельеф слегка
                    img.SetPixel(x, y, c);
                }
            _tex = ImageTexture.CreateFromImage(img);
            Visible = false;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public void SetState(SVec me, float facing, SVec? home)
        {
            _me = me; _facing = facing; _home = home;
            if (Visible) QueueRedraw();
        }

        public override void _Draw()
        {
            var vs = GetViewportRect().Size;
            float side = Mathf.Min(vs.X, vs.Y) * 0.86f;
            var origin = (vs - new Vector2(side, side)) / 2;
            DrawRect(new Rect2(Vector2.Zero, vs), new Color(0, 0, 0, 0.55f));
            DrawTextureRect(_tex, new Rect2(origin, new Vector2(side, side)), false);
            DrawRect(new Rect2(origin, new Vector2(side, side)), new Color(0.8f, 0.75f, 0.6f, 0.8f), false, 2);
            Vector2 P(SVec w) => origin + new Vector2((w.X + _map.HalfSize) / (_map.HalfSize * 2), (w.Y + _map.HalfSize) / (_map.HalfSize * 2)) * side;

            var font = ThemeDB.FallbackFont;
            foreach (var (name, mn, mx) in _monuments)
            {
                var a = P(mn); var b = P(mx);
                DrawRect(new Rect2(a, b - a), new Color(0.15f, 0.14f, 0.13f, 0.9f));
                DrawRect(new Rect2(a, b - a), new Color(0.95f, 0.85f, 0.5f), false, 1);
                string label = name;
                var sz = font.GetStringSize(label, HorizontalAlignment.Left, -1, 13);
                var at = new Vector2((a.X + b.X) / 2 - sz.X / 2, a.Y - 4);
                DrawString(font, at + Vector2.One, label, HorizontalAlignment.Left, -1, 13, new Color(0, 0, 0, 0.8f));
                DrawString(font, at, label, HorizontalAlignment.Left, -1, 13, new Color(0.98f, 0.92f, 0.7f));
            }
            if (_home is SVec h)
            {
                var hp = P(h);
                DrawRect(new Rect2(hp - new Vector2(5, 5), new Vector2(10, 10)), new Color(0.35f, 0.75f, 1f));
                DrawString(font, hp + new Vector2(8, 5), "дом", HorizontalAlignment.Left, -1, 13, new Color(0.6f, 0.85f, 1f));
            }
            // ты — стрелка по направлению взгляда
            var mp = P(_me);
            var dir = new Vector2(Mathf.Cos(_facing), Mathf.Sin(_facing));
            var perp = new Vector2(-dir.Y, dir.X);
            DrawColoredPolygon(new[] { mp + dir * 10, mp - dir * 6 + perp * 6, mp - dir * 3, mp - dir * 6 - perp * 6 }, new Color(1f, 0.35f, 0.25f));
            DrawString(font, origin + new Vector2(8, side - 10), "M — закрыть карту", HorizontalAlignment.Left, -1, 13, new Color(0.9f, 0.9f, 0.85f));
        }
    }
}
