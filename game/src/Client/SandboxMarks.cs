using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>
    /// Метки полигона на земле (только клиент): граница площадки, мишени с подписями, дистанции, рубеж,
    /// «призрак» звукового поля и кольцо направлений викторины (36 секторов, цвет — средняя ошибка; стрелки — истина и ответ).
    /// Каждый кадр заполняется заново (Clear → …) и перерисовывается.
    /// </summary>
    public partial class SandboxMarks : Node2D
    {
        readonly List<(Vector2 pos, string text, bool flash)> _targets = new();
        readonly List<(Vector2 a, Vector2 b)> _rects = new();
        readonly List<(Vector2 a, Vector2 b, string text)> _lines = new();
        readonly List<(Vector2 pos, string text)> _labels = new();
        Vector2? _phantom;
        (Vector2 c, float r, float[] err, (float truth, float answer)? res)? _ring;

        static readonly Color Ink = new(1, 1, 1, 0.85f), Soft = new(1, 1, 1, 0.22f), LineCol = new(1, 1, 1, 0.35f);

        public void Clear() { _targets.Clear(); _rects.Clear(); _lines.Clear(); _labels.Clear(); _phantom = null; _ring = null; }
        public void Target(Vector2 px, string text, bool flash) => _targets.Add((px, text, flash));
        public void Rect(Vector2 a, Vector2 b) => _rects.Add((a, b));
        public void Line(Vector2 a, Vector2 b, string text) => _lines.Add((a, b, text));
        public void Label(Vector2 px, string text) => _labels.Add((px, text));
        public void Phantom(Vector2 px) => _phantom = px;
        public void Ring(Vector2 center, float radiusPx, float[] sectorErr, (float truth, float answer)? result) => _ring = (center, radiusPx, sectorErr, result);

        void Text(Vector2 at, string s, int size, Color c)
        {
            var f = UiTheme.Semi;
            float w = f.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;
            var p = at - new Vector2(w / 2, 0);
            DrawStringOutline(f, p, s, HorizontalAlignment.Left, -1, size, 4, new Color(0, 0, 0, 0.7f));
            DrawString(f, p, s, HorizontalAlignment.Left, -1, size, c);
        }

        public override void _Draw()
        {
            foreach (var (a, b) in _rects) DrawRect(new Rect2(a, b - a), Soft, false, 2);
            foreach (var (a, b, t) in _lines) { DrawLine(a, b, LineCol, 2); Text(b + new Vector2(0, 16), t, 12, Ink); }
            foreach (var (p, t) in _labels) Text(p, t, 13, Ink);
            foreach (var (p, t, flash) in _targets)
            {
                // мишень на земле: кольца, при попадании — вспышка
                var c = flash ? UiTheme.Accent : new Color(0.95f, 0.35f, 0.3f, 0.75f);
                DrawArc(p, 14, 0, Mathf.Tau, 24, c, 2, true);
                DrawArc(p, 7, 0, Mathf.Tau, 16, c, 2, true);
                Text(p + new Vector2(0, 30), t, 12, flash ? UiTheme.Accent : Ink);
            }
            if (_phantom is Vector2 ph)
            {
                DrawCircle(ph, 6, new Color(0.42f, 0.71f, 1f, 0.8f));
                Text(ph + new Vector2(0, 22), "призрак", 11, UiTheme.Info);
            }
            if (_ring is var (c0, r, err, res))
            {
                // 36 секторов по 10°, 0° — вверх по экрану, по часовой (как Acoustics)
                for (int i = 0; i < 36; i++)
                {
                    float a0 = Mathf.DegToRad(i * 10 - 5 - 90), a1 = Mathf.DegToRad(i * 10 + 5 - 90);
                    Color col = Soft;
                    if (err != null && err[i] >= 0)
                    {
                        float k = Mathf.Clamp(err[i] / 60f, 0, 1);   // 0° — зелёный, 60°+ — красный
                        col = new Color(UiTheme.Good.Lerp(UiTheme.Bad, k), 0.75f);
                    }
                    DrawArc(c0, r, a0 + 0.01f, a1 - 0.01f, 6, col, i % 9 == 0 ? 6 : 4, true);
                }
                Text(c0 + new Vector2(0, -r - 10), "перед", 11, Ink);
                if (res is var (truth, answer))
                {
                    Vector2 Dir(float az) { float a = Mathf.DegToRad(az); return new Vector2(Mathf.Sin(a), -Mathf.Cos(a)); }
                    DrawLine(c0 + Dir(truth) * 20, c0 + Dir(truth) * (r + 24), UiTheme.Good, 4, true);
                    DrawCircle(c0 + Dir(truth) * (r + 24), 6, UiTheme.Good);
                    Text(c0 + Dir(truth) * (r + 44), $"звук {truth:0}°", 12, UiTheme.Good);
                    DrawLine(c0 + Dir(answer) * 20, c0 + Dir(answer) * (r + 14), UiTheme.Accent, 3, true);
                    Text(c0 + Dir(answer) * (r + 30), $"ответ {answer:0}°", 12, UiTheme.Accent);
                }
            }
        }
    }
}
