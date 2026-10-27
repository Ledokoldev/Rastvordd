using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Ивенты режиссёра в мире: самолёт сброса (тень по земле), парашют с грузом, световой столб-маяк над грузом и
    /// закрытым ящиком (видно издалека — туда сбегаются), сигнальный дым у приземлившегося груза.
    /// </summary>
    public partial class DirectorLayer : Node2D
    {
        List<DirectorMarker> _markers = new();
        float _time;
        /// <summary>Сигнальные ракеты (Радиоцентр «Маяк»): где и когда пущены. Заглушка — красный огонь взлетает и висит ~25 с; красиво сделает VFX.</summary>
        public List<(System.Numerics.Vector2 pos, float t)> Flares;

        public DirectorLayer()
        {
            ZIndex = 5;
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Mix };
            TextureFilter = TextureFilterEnum.Nearest;
            // самолёт летит выше всего (кроны, крыши): отдельный слой с большим ZIndex
            _air = new AirOverlay { Owner2 = this, ZIndex = 60, TextureFilter = TextureFilterEnum.Nearest };
            AddChild(_air);
        }

        AirOverlay _air;
        float _vis = -1;            // сглаженное время для анимации самолёта/груза (снимки приходят дискретно)
        const float PlaneAlt = 14;   // м, условная высота (сжата под камеру сверху)
        internal (Vector2 p, Vector2 dir)? PlaneNow;

        public override void _Process(double delta)
        {
            if (_vis < 0 || System.Math.Abs(_vis - _time) > 1.5f) _vis = _time;
            else { _vis += (float)delta; _vis += (_time - _vis) * Mathf.Min(1f, (float)delta * 3f); }
            QueueRedraw(); _air.QueueRedraw();
        }

        partial class AirOverlay : Node2D
        {
            public DirectorLayer Owner2;
            public override void _Draw() => Owner2.DrawAir(this);
        }

        void DrawAir(CanvasItem ci)
        {
            if (PlaneNow is not { } pl) return;
            DrawPlaneOn(ci, pl.p - new Vector2(0, Iso.H(PlaneAlt)), pl.dir, Colors.White);
        }

        float _smokeAcc;
        int _smokeTick;

        public void Sync(List<DirectorMarker> markers, float time)
        {
            _markers = markers;
            EmitSmoke(time - _time);
            _time = time;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (Flares != null)
                foreach (var (fp, ft) in Flares)
                {
                    float age = _time - ft;
                    if (age < 0 || age > 25) continue;
                    var at = Iso.Px(fp);
                    // взлёт за 2,5 с на ~60 м, потом медленно опускается и гаснет
                    float hM = age < 2.5f ? 60 * (age / 2.5f) : 60 - (age - 2.5f) * 1.6f;
                    var top = at - new Vector2(Mathf.Sin(age * 0.7f) * 10 * Mathf.Min(1, age / 3), Iso.H(hM));
                    float fade = age > 20 ? (25 - age) / 5 : 1;
                    DrawLine(at, top, new Color(1, 0.75f, 0.6f, 0.25f * fade), 2);
                    for (int i = 0; i < 4; i++) DrawCircle(top, 6 + i * 9, new Color(1f, 0.15f, 0.1f, 0.3f * fade / (i + 1)));
                    DrawCircle(top, 4, new Color(1f, 0.9f, 0.8f, fade));
                }
            PlaneNow = null;
            foreach (var m in _markers)
            {
                var at = Iso.Px(m.Pos);
                if (m.Kind == "airdrop" && !m.Started)
                {
                    // самолёт: проходит над точкой за 25 с до приземления груза; парашют — с этого момента вниз
                    float k = (_vis - (m.StartsAt - 45)) / 40f;
                    if (k > 0 && k < 1)
                    {
                        var p = Iso.Px(System.Numerics.Vector2.Lerp(m.PlaneFrom, m.PlaneTo, k));
                        var dir = (Iso.Px(m.PlaneTo) - Iso.Px(m.PlaneFrom)).Normalized();
                        PlaneNow = (p, dir);
                        // тень на земле: смещена по высоте вниз-вправо (как у солнечных теней), мягкая — несколько слоёв
                        var so = new Vector2(PlaneAlt * 1.6f, PlaneAlt * 1.0f);
                        for (int i = 0; i < 3; i++) DrawPlaneOn(this, p + so + new Vector2(i - 1, (i - 1) * 0.6f) * 2.5f, dir, new Color(0, 0, 0, 0.12f));
                    }
                    float fall = (_vis - (m.StartsAt - 25)) / 25f;
                    if (fall > 0 && fall < 1)
                    {
                        // падение: быстро теряем высоту от самолёта к земле (парашют раскрывается — замедление), без скачка в начале
                        float e = 1 - Mathf.Pow(1 - fall, 1.8f);
                        float hM = PlaneAlt * (1 - e);
                        float h = Iso.H(hM);
                        float sway = Mathf.Sin(_vis * 1.3f) * 0.08f * Mathf.Min(1, fall * 4);
                        var c = at - new Vector2(sway * 60, h);
                        // тень груза: на земле под ним, смещена по высоте, сходится с грузом к приземлению
                        float r = 10 * (0.6f + 0.4f * e);
                        DrawSetTransform(at + new Vector2(hM * 1.6f, hM * 1.0f) + new Vector2(6, 2) * e, 0, new Vector2(1.6f, 0.6f));
                        DrawCircle(Vector2.Zero, r, new Color(0, 0, 0, 0.12f + 0.14f * e));
                        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                        var chute = Art.Get("containers/airdrop_descending");
                        if (chute != null)
                        {
                            // контейнер на парашюте: низ картинки — днище груза; покачивается; у земли купол опадает и гаснет
                            float sc = 1.1f;
                            float land = Mathf.Clamp((fall - 0.9f) / 0.1f, 0, 1);
                            DrawSetTransform(c, sway, new Vector2(sc, sc * (1 - 0.25f * land)));
                            DrawTexture(chute, new Vector2(-chute.GetWidth() / 2f, -chute.GetHeight()), new Color(1, 1, 1, 1 - land));
                            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                        }
                    }
                    continue;
                }
                // маяк: световой столб над грузом / ящиком (красный — груз, жёлтый — закрытый ящик; открыт — зелёный)
                var col = m.Open ? new Color(0.4f, 1f, 0.45f) : m.Kind == "airdrop" ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 0.85f, 0.25f);
                float pulse = 0.55f + 0.45f * Mathf.Sin(_time * (m.Started && !m.Open ? 6f : 2f));
                float hh = Iso.H(40);
                DrawColoredPolygon(new[] { at + new Vector2(-6, 0), at + new Vector2(6, 0), at + new Vector2(14, -hh), at + new Vector2(-14, -hh) },
                    new Color(col, 0.18f * pulse));
                DrawColoredPolygon(new[] { at + new Vector2(-2, 0), at + new Vector2(2, 0), at + new Vector2(4, -hh), at + new Vector2(-4, -hh) },
                    new Color(col, 0.35f * pulse));
            }
        }

        /// <summary>
        /// Дым-маркеры (Vfx): у приземлившегося закрытого груза — густой красный сигнальный дым столбом,
        /// у закрытого ящика — редкий жёлтый, у открытого — слабый зелёный (видно, что уже взяли).
        /// </summary>
        void EmitSmoke(float dt)
        {
            if (dt <= 0 || dt > 1) return;
            _smokeAcc += dt;
            int ticks = (int)(_smokeAcc / 0.1f);
            if (ticks == 0) return;
            _smokeAcc -= ticks * 0.1f;
            for (int t = 0; t < ticks; t++)
            {
                _smokeTick++;
                foreach (var m in _markers)
                {
                    if (m.Kind == "airdrop" && !m.Started) continue;
                    var at = Iso.Px(m.Pos);
                    if (m.Kind == "airdrop" && !m.Open)
                    {
                        // 5 клубов в секунду, поднимаются высоко и сносятся ветром
                        if (_smokeTick % 2 == 0) Vfx.Puff("smoke_red", at + new Vector2(4, 2), Iso.H(0.4f), 42, 0.9f, 2.2f, 5, new Vector2(4, 0), 500);
                    }
                    else if (!m.Open) { if (_smokeTick % 8 == 0) Vfx.Puff("smoke_yellow", at, Iso.H(0.6f), 30, 0.75f, 2.6f, 5, default, 400); }
                    else if (_smokeTick % 15 == 0) Vfx.Puff("smoke_green", at, Iso.H(0.5f), 24, 0.55f, 3f, 5, default, 400);
                }
            }
        }

        static void DrawPlaneOn(CanvasItem ci, Vector2 p, Vector2 dir, Color c)
        {
            // Ан-2 сверху: нос картинки смотрит вправо, размах крыльев 18 м
            var tex = Art.Get("vehicles/plane_an2_top");
            if (tex == null)
            {
                var side = new Vector2(-dir.Y, dir.X);
                ci.DrawColoredPolygon(new[] { p + dir * 26, p + side * 4, p - dir * 22 + side * 3, p - dir * 22 - side * 3, p - side * 4 }, c);
                ci.DrawColoredPolygon(new[] { p + dir * 8 + side * 34, p + dir * 2 + side * 34, p + dir * 2 - side * 34, p + dir * 8 - side * 34 }, c);
                return;
            }
            float sc = 18f * Iso.PPM / tex.GetHeight();
            ci.DrawSetTransform(p, dir.Angle(), new Vector2(sc, sc));
            ci.DrawTexture(tex, -tex.GetSize() / 2, c);
            ci.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    /// <summary>Радио: новые слухи режиссёра — стеклянные плашки вверху по центру, держатся 12 с, последние полсекунды гаснут.</summary>
    public partial class RadioFeed : VBoxContainer
    {
        readonly List<(string text, float left)> _lines = new();
        readonly List<PanelContainer> _pills = new();
        int _lastN;
        string _sig;

        public RadioFeed()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            Theme = UiTheme.Theme;
            AddThemeConstantOverride("separation", 6);
        }

        public void Sync(List<(int n, string text)> radio, float dt)
        {
            foreach (var (n, text) in radio)
                if (n > _lastN) { _lastN = n; _lines.Add((text, 12)); }
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                var (t, left) = _lines[i];
                left -= dt;
                if (left <= 0) _lines.RemoveAt(i); else _lines[i] = (t, left);
            }
            while (_lines.Count > 3) _lines.RemoveAt(0);
            string sig = string.Join("|", _lines.ConvertAll(l => l.text));
            if (sig != _sig)
            {
                _sig = sig;
                foreach (var p in _pills) p.QueueFree();
                _pills.Clear();
                foreach (var (t, _) in _lines)
                {
                    var p = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
                    p.AddThemeStyleboxOverride("panel", UiTheme.Pill(0.66f));
                    var row = UiTheme.HBox(10);
                    p.AddChild(row);
                    var tag = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
                    tag.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(UiTheme.Accent, 0.18f), 6, null, 0, 7, 1));
                    tag.AddChild(UiTheme.Strong("РАДИО", 11, UiTheme.Accent));
                    row.AddChild(tag);
                    row.AddChild(UiTheme.Strong(t, UiTheme.FontSmall, UiTheme.Ink));
                    AddChild(p);
                    _pills.Add(p);
                }
            }
            for (int i = 0; i < _pills.Count && i < _lines.Count; i++)
                _pills[i].Modulate = new Color(1, 1, 1, Mathf.Clamp(_lines[i].left / 0.5f, 0, 1));
        }
    }
}
