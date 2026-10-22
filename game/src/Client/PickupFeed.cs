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
        const float Life = 3.5f, RowH = 32, W = 280;
        const int MaxRows = 6;
        sealed class Row { public string Id, Name; public int N; public float T; }
        readonly List<Row> _rows = new();
        Dictionary<string, int> _last;
        readonly Dictionary<string, string> _names = new();

        public PickupFeed()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            TextureFilter = TextureFilterEnum.Linear;
            CustomMinimumSize = new Vector2(W, (RowH + 6) * MaxRows);
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
            float y = Size.Y - RowH;
            for (int i = _rows.Count - 1; i >= 0; i--, y -= RowH + 6)
            {
                var r = _rows[i];
                float a = Mathf.Clamp(r.T / 0.5f, 0, 1);                       // гаснет последние полсекунды
                float slide = Mathf.Clamp((Life - r.T) / 0.15f, 0, 1);          // въезжает справа
                string plus = $"+{r.N}";
                float pw = UiTheme.TextWidth(plus, UiTheme.FontBody, UiTheme.Semi);
                float nw = Mathf.Min(UiTheme.TextWidth(r.Name, UiTheme.FontSmall), W - RowH - pw - 34);
                float w = RowH + 10 + pw + 8 + nw + 14;                          // плашка по длине текста, прижата вправо
                float x = Size.X - w + 40 * (1 - slide);
                var rect = new Rect2(x, y, w, RowH);
                UiTheme.DrawRound(this, rect, new Color(UiTheme.PanelBg, 0.62f * a), 12, new Color(UiTheme.PanelBorder, UiTheme.PanelBorder.A * a));
                UiTheme.DrawIcon(this, r.Id, new Rect2(x + 6, y + 3, RowH - 6, RowH - 6), new Color(1, 1, 1, a));
                UiTheme.DrawText(this, new Vector2(x + RowH + 10, y + RowH / 2 + 5), plus, UiTheme.FontBody, new Color(UiTheme.Good, a), UiTheme.Semi);
                UiTheme.DrawText(this, new Vector2(x + RowH + 18 + pw, y + RowH / 2 + 5), r.Name, UiTheme.FontSmall, new Color(UiTheme.Body, a), null, HorizontalAlignment.Left, nw);
            }
        }
    }
}
