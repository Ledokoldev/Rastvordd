using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Лента «что подобрал» справа внизу, как в Расте: иконка, +N и название. Считается по разнице содержимого
    /// рюкзака и пояса между снапшотами — ловит добычу, лут, крафт и подборы одним способом.
    /// </summary>
    public partial class PickupFeed : Control
    {
        const float Life = 3.5f, RowH = 30, W = 260;
        const int MaxRows = 6;
        sealed class Row { public string Id, Name; public int N; public float T; }
        readonly List<Row> _rows = new();
        Dictionary<string, int> _last;
        readonly Dictionary<string, string> _names = new();

        public PickupFeed()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            CustomMinimumSize = new Vector2(W, RowH * MaxRows);
        }

        /// <summary>Сбросить базу (смерть, загрузка): следующая разница не считается подбором.</summary>
        public void Reset() => _last = null;

        public void Update(SelfInfo self, float dt)
        {
            var now = new Dictionary<string, int>();
            void Count(List<CellView> cells)
            {
                foreach (var c in cells)
                {
                    if (c.Id == null) continue;
                    now[c.Id] = now.GetValueOrDefault(c.Id) + System.Math.Max(1, c.Count);
                    _names[c.Id] = c.Name;
                }
            }
            Count(self.Pack);
            Count(self.BeltCells);
            if (_last != null && !self.Dead)
                foreach (var (id, n) in now)
                {
                    int d = n - _last.GetValueOrDefault(id);
                    if (d > 0) Add(id, _names.GetValueOrDefault(id, id), d);
                }
            _last = self.Dead ? null : now;

            for (int i = _rows.Count - 1; i >= 0; i--)
            {
                _rows[i].T -= dt;
                if (_rows[i].T <= 0) _rows.RemoveAt(i);
            }
            QueueRedraw();
        }

        void Add(string id, string name, int n)
        {
            // то же самое подряд — копим в одной строке и продлеваем
            var row = _rows.Find(r => r.Id == id);
            if (row != null) { row.N += n; row.T = Life; _rows.Remove(row); _rows.Add(row); return; }
            _rows.Add(new Row { Id = id, Name = name, N = n, T = Life });
            if (_rows.Count > MaxRows) _rows.RemoveAt(0);
        }

        public override void _Draw()
        {
            var font = UiTheme.Text;
            float y = Size.Y - RowH;
            for (int i = _rows.Count - 1; i >= 0; i--, y -= RowH + 2)
            {
                var r = _rows[i];
                float a = Mathf.Clamp(r.T / 0.5f, 0, 1);                       // гаснет последние полсекунды
                float slide = Mathf.Clamp((Life - r.T) / 0.12f, 0, 1);          // въезжает справа
                float x = W * (1 - slide) * 0.3f;
                var rect = new Rect2(x, y, W, RowH);
                DrawRect(rect, new Color(UiTheme.PanelBg, UiTheme.PanelBg.A * a));
                DrawRect(new Rect2(x, y, 2, RowH), new Color(0.45f, 0.8f, 0.4f, a));
                var icon = Art.Icon(r.Id);
                if (icon != null) DrawTextureRect(icon, new Rect2(x + 6, y + 2, RowH - 4, RowH - 4), false, new Color(1, 1, 1, a));
                string plus = $"+{r.N}";
                DrawString(font, new Vector2(x + RowH + 8, y + 20), plus, HorizontalAlignment.Left, -1, 14, new Color(0.55f, 0.9f, 0.5f, a));
                float px = font.GetStringSize(plus, HorizontalAlignment.Left, -1, 14).X;
                DrawString(font, new Vector2(x + RowH + 14 + px, y + 20), r.Name, HorizontalAlignment.Left, W - RowH - 24 - px, 14, new Color(UiTheme.Ink, a));
            }
        }
    }
}
