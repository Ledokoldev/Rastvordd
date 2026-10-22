using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>
    /// Карта острова (G): стеклянная панель, карта со скруглёнными углами, РТ — аккуратные плашки с названиями,
    /// большой маркер «я здесь», дом, ивенты режиссёра; справа — легенда.
    /// </summary>
    public partial class MapView : Control
    {
        const float LegendW = 230, Gap = 20, Pad = 16;
        readonly MapInfo _map;
        readonly ImageTexture _tex;
        readonly List<(string name, SVec min, SVec max)> _monuments;
        public MapInfo Map => _map;
        public Texture2D Texture => _tex;
        public List<(string name, SVec min, SVec max)> Monuments => _monuments;
        SVec _me;
        float _facing;
        SVec? _home;
        readonly Control _mask, _canvas;
        Rect2 _mapRect;

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
            TextureFilter = TextureFilterEnum.Linear;
            // карта обрезается скруглённой маской
            _mask = new RoundMask { MouseFilter = MouseFilterEnum.Ignore, ClipChildren = ClipChildrenMode.Only };
            AddChild(_mask);
            _canvas = new Control { MouseFilter = MouseFilterEnum.Ignore, TextureFilter = TextureFilterEnum.Nearest };
            _canvas.Draw += DrawMap;
            _mask.AddChild(_canvas);
            var over = new Control { MouseFilter = MouseFilterEnum.Ignore };
            over.SetAnchorsPreset(LayoutPreset.FullRect);
            over.Draw += () => DrawOverlay(over);
            AddChild(over);
            _over = over;
        }
        readonly Control _over;

        partial class RoundMask : Control
        {
            public override void _Draw() => UiTheme.DrawRound(this, new Rect2(Vector2.Zero, Size), Colors.White, 12);
        }

        /// <summary>Ивенты режиссёра (видны всем) и тепловая карта (только наблюдателю).</summary>
        public List<DirectorMarker> Markers = new();
        public List<(SVec pos, float heat)> Hot = new();
        public float Time;

        public void SetState(SVec me, float facing, SVec? home)
        {
            _me = me; _facing = facing; _home = home;
            if (Visible) Redraw();
        }

        void Redraw() { Layout(); QueueRedraw(); _canvas.QueueRedraw(); _over.QueueRedraw(); }

        void Layout()
        {
            var vs = GetViewportRect().Size;
            float side = Mathf.Min(vs.Y * 0.84f, vs.X - LegendW - Gap - 2 * Pad - 80);
            float total = side + 2 * Pad + Gap + LegendW;
            var p = new Vector2((vs.X - total) / 2 + Pad, (vs.Y - side) / 2);
            _mapRect = new Rect2(p, new Vector2(side, side));
            _mask.Position = p; _mask.Size = _mapRect.Size;
            _canvas.Size = _mapRect.Size;
        }

        Vector2 P(SVec w) => new Vector2((w.X + _map.HalfSize) / (_map.HalfSize * 2), (w.Y + _map.HalfSize) / (_map.HalfSize * 2)) * _mapRect.Size.X;

        public override void _Draw()
        {
            var vs = GetViewportRect().Size;
            DrawRect(new Rect2(Vector2.Zero, vs), UiTheme.Backdrop);
            // стеклянная подложка под карту и легенду
            DrawStyleBox(UiTheme.Glass(), new Rect2(_mapRect.Position - new Vector2(Pad, Pad), _mapRect.Size + new Vector2(2 * Pad, 2 * Pad)));
            var leg = new Rect2(_mapRect.End.X + Pad + Gap, _mapRect.Position.Y - Pad, LegendW, 0);
            float y = leg.Position.Y + UiTheme.PanelPad;
            float lx = leg.Position.X + UiTheme.PanelPad;
            var items = new List<(int kind, string text, Color col)>
            {
                (0, "я здесь", Colors.White),
                (1, "дом (шкаф)", UiTheme.Info),
                (2, "РТ", UiTheme.Ink),
                (3, "сброс груза", new Color(1f, 0.35f, 0.25f)),
                (3, "закрытый ящик", UiTheme.Accent),
                (3, "открыто", UiTheme.Good),
            };
            if (Hot.Count > 0) items.Add((4, "жаркие места", new Color(1f, 0.2f, 0.1f)));
            float legH = UiTheme.PanelPad * 2 + 40 + 20 + items.Count * 30 + 54;
            DrawStyleBox(UiTheme.Glass(), new Rect2(leg.Position, new Vector2(LegendW, legH)));
            UiTheme.DrawText(this, new Vector2(lx, y + 22), "Карта", UiTheme.FontTitle, UiTheme.Ink, UiTheme.Semi);
            y += 40;
            UiTheme.DrawText(this, new Vector2(lx, y + 12), "ОБОЗНАЧЕНИЯ", UiTheme.FontCaption, UiTheme.Dim);
            y += 26;
            foreach (var (kind, text, col) in items)
            {
                var c = new Vector2(lx + 10, y + 8);
                switch (kind)
                {
                    case 0: DrawMe(this, c, -Mathf.Pi / 2, 0.75f); break;
                    case 1: UiTheme.DrawRound(this, new Rect2(c - new Vector2(6, 6), new Vector2(12, 12)), col, 3); break;
                    case 2: UiTheme.DrawRound(this, new Rect2(c - new Vector2(8, 6), new Vector2(16, 12)), new Color(0.08f, 0.08f, 0.09f, 0.7f), 3, new Color(1, 1, 1, 0.55f)); break;
                    case 3: UiTheme.DrawDot(this, c, 6, col); break;
                    default: UiTheme.DrawDot(this, c, 7, new Color(col, 0.45f)); break;
                }
                UiTheme.DrawText(this, new Vector2(lx + 30, y + 13), text, UiTheme.FontSmall, UiTheme.Body);
                y += 30;
            }
            y += 10;
            var chip = new Rect2(lx, y, 150, 26);
            UiTheme.DrawRound(this, chip, UiTheme.ChipBg, 8);
            UiTheme.DrawText(this, chip.Position + new Vector2(10, 17), "G / Esc — закрыть", UiTheme.FontCaption, UiTheme.Hex(0xdddddd));
        }

        void DrawMap()
        {
            var c = _canvas;
            var full = new Rect2(Vector2.Zero, _mapRect.Size);
            c.DrawTextureRect(_tex, full, false);
            c.DrawRect(full, new Color(0, 0, 0, 0.12f));   // карта чуть приглушена — метки читаются лучше
        }

        void DrawOverlay(Control o)
        {
            if (_mapRect.Size.X <= 0) return;
            var origin = _mapRect.Position;
            Vector2 Q(SVec w) => origin + P(w);
            var font = UiTheme.Text;
            UiTheme.DrawRound(o, _mapRect, Colors.Transparent, 12, new Color(1, 1, 1, 0.14f));

            foreach (var (name, mn, mx) in _monuments)
            {
                var a = Q(mn); var b = Q(mx);
                UiTheme.DrawRound(o, new Rect2(a, b - a), new Color(0.08f, 0.08f, 0.09f, 0.55f), 3, new Color(1, 1, 1, 0.55f));
                // название — тёмная плашка над площадкой
                float w = UiTheme.TextWidth(name, UiTheme.FontCaption, UiTheme.Semi);
                var pill = new Rect2((a.X + b.X) / 2 - w / 2 - 7, a.Y - 24, w + 14, 20);
                UiTheme.DrawRound(o, pill, new Color(UiTheme.PanelBg, 0.85f), 10);
                UiTheme.DrawText(o, pill.Position + new Vector2(7, 14), name, UiTheme.FontCaption, UiTheme.Ink, UiTheme.Semi);
            }
            // тепловая карта (наблюдатель): красные пятна — где мясорубка
            foreach (var (hp0, heat) in Hot)
            {
                var hp1 = Q(hp0);
                float r = Mathf.Clamp(6 + heat * 0.6f, 6, 40) * _mapRect.Size.X / 900f;
                o.DrawCircle(hp1, r, new Color(1f, 0.15f, 0.05f, Mathf.Clamp(0.15f + heat * 0.01f, 0.15f, 0.55f)), true, -1, true);
            }
            // ивенты режиссёра: сброс груза, закрытый ящик — с таймером
            foreach (var m in Markers)
            {
                var mp0 = Q(m.Pos);
                var col = m.Open ? UiTheme.Good : m.Kind == "airdrop" ? new Color(1f, 0.35f, 0.25f) : UiTheme.Accent;
                UiTheme.DrawDot(o, mp0, 10, new Color(0, 0, 0, 0.5f));
                UiTheme.DrawDot(o, mp0, 6.5f, col);
                string tl = m.Open ? "открыт" : m.Kind == "airdrop" ? (m.Started ? $"{Mathf.CeilToInt(m.StartsAt - Time)} с" : "летит") : m.Started ? $"взлом {Mathf.CeilToInt(m.StartsAt - Time)} с" : "закрыт";
                string label = $"{m.Name} · {tl}";
                float w = UiTheme.TextWidth(label, UiTheme.FontCaption);
                var pill = new Rect2(mp0 + new Vector2(13, -10), new Vector2(w + 14, 20));
                UiTheme.DrawRound(o, pill, new Color(UiTheme.PanelBg, 0.85f), 10);
                UiTheme.DrawText(o, pill.Position + new Vector2(7, 14), label, UiTheme.FontCaption, col);
            }
            if (_home is SVec h)
            {
                var hp = Q(h);
                UiTheme.DrawRound(o, new Rect2(hp - new Vector2(7, 7), new Vector2(14, 14)), UiTheme.Info, 4, Colors.White, 2);
                UiTheme.DrawText(o, hp + new Vector2(12, 5), "дом", UiTheme.FontSmall, UiTheme.Info, UiTheme.Semi);
            }
            // я здесь: пульсирующее кольцо, белая стрелка по взгляду, подпись
            var mp = Q(_me);
            float t = (Godot.Time.GetTicksMsec() % 1600) / 1600f;
            o.DrawArc(mp, 12 + 16 * t, 0, Mathf.Tau, 40, new Color(1, 1, 1, 0.6f * (1 - t)), 2, true);
            DrawMe(o, mp, _facing, 1.2f);
            string me = "я здесь";
            float mw = UiTheme.TextWidth(me, UiTheme.FontCaption, UiTheme.Semi);
            var mpill = new Rect2(mp + new Vector2(-mw / 2 - 8, 20), new Vector2(mw + 16, 20));
            UiTheme.DrawRound(o, mpill, new Color(1, 1, 1, 0.92f), 10);
            UiTheme.DrawText(o, mpill.Position + new Vector2(8, 14), me, UiTheme.FontCaption, UiTheme.Dark, UiTheme.Semi);
        }

        /// <summary>Маркер игрока: белая стрелка в тёмном круге.</summary>
        public static void DrawMe(CanvasItem c, Vector2 at, float facing, float k)
        {
            var dir = new Vector2(Mathf.Cos(facing), Mathf.Sin(facing));
            var perp = new Vector2(-dir.Y, dir.X);
            c.DrawCircle(at, 12 * k, new Color(0, 0, 0, 0.55f), true, -1, true);
            c.DrawArc(at, 12 * k, 0, Mathf.Tau, 32, Colors.White, 1.5f, true);
            c.DrawColoredPolygon(new[] { at + dir * 8 * k, at - dir * 5 * k + perp * 5.5f * k, at - dir * 2 * k, at - dir * 5 * k - perp * 5.5f * k }, Colors.White);
        }
    }
}
