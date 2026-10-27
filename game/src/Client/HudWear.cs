using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// HUD над шкалами (D47): часы «Электроника» (если надеты) — время, воздух, радиация там, где стоишь;
    /// ниже — значки состояния, видны всегда: облучение, тошнота, холодно/переохлаждение, мокрый.
    /// Без часов цифр нет — только значки.
    /// </summary>
    public partial class ConditionView : Control
    {
        public const float W = 220, H = 58, ChipH = 22;
        SelfInfo _s;
        float _day;
        string _sig;

        public ConditionView() { CustomMinimumSize = new Vector2(W, H); Size = CustomMinimumSize; MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.Linear; }

        public void Set(SelfInfo s, float dayTime)
        {
            _s = s; _day = dayTime;
            int mins = (int)(dayTime * 24 * 60);
            string sig = s.HasWatch + "|" + mins + "|" + Mathf.RoundToInt(s.AirTemp) + "|" + Mathf.RoundToInt(s.RadHere) + "|" + s.Irradiated + s.Nausea + s.Freezing + s.Wet + s.Poisoned + (s.Cold >= 100) + "|" + Mathf.RoundToInt(s.RadDose / 10);
            if (sig == _sig) return;
            _sig = sig;
            QueueRedraw();
        }

        public bool HasContent => _s != null && (_s.HasWatch || _s.Irradiated || _s.Nausea || _s.Freezing || _s.Wet || _s.Poisoned);

        public override void _Draw()
        {
            if (_s == null) return;
            var s = _s;
            float y = H - ChipH;
            // значки — нижний ряд, слева направо
            float x = 0;
            void Chip(int icon, string text, Color c)
            {
                float tw = UiTheme.TextWidth(text, UiTheme.FontCaption, UiTheme.Semi);
                var r = new Rect2(x, y, 26 + tw + 10, ChipH);
                UiTheme.DrawRound(this, r, new Color(UiTheme.PanelBg, 0.62f), 10, UiTheme.PanelBorder);
                Icon(icon, new Vector2(x + 13, y + ChipH / 2), c);
                UiTheme.DrawText(this, new Vector2(x + 24, y + 15.5f), text, UiTheme.FontCaption, c, UiTheme.Semi);
                x += r.Size.X + 5;
            }
            if (s.Irradiated) Chip(0, s.HasWatch && s.RadDose >= 1 ? $"облучение · {s.RadDose:0}" : "облучение", s.RadDose >= Clothing.RadHurt1 ? UiTheme.Bad : UiTheme.Accent);
            if (s.Nausea) Chip(1, "тошнота", s.RadDose >= Clothing.RadHurt1 ? UiTheme.Bad : UiTheme.Accent);
            if (s.Poisoned) Chip(1, "отравление", UiTheme.Bad);   // сырая вода (D54)
            if (s.Freezing) Chip(2, s.Cold >= 100 ? "переохлаждение" : "холодно", s.Cold >= 100 ? UiTheme.Bad : UiTheme.Info);
            if (s.Wet) Chip(3, "мокрый", UiTheme.Info);

            // часы — над значками
            if (!s.HasWatch) return;
            int mins = (int)(_day * 24 * 60);
            string time = $"{mins / 60:00}:{mins % 60:00}";
            string temp = $"{(s.AirTemp > 0.5f ? "+" : s.AirTemp < -0.5f ? "−" : "")}{Mathf.Abs(Mathf.RoundToInt(s.AirTemp))} °C";
            string rad = $"{s.RadHere:0}";
            float wy = y - ChipH - 6;
            float w1 = UiTheme.TextWidth(time, UiTheme.FontSmall, UiTheme.Semi), w2 = UiTheme.TextWidth(temp, UiTheme.FontSmall, UiTheme.Semi), w3 = UiTheme.TextWidth(rad, UiTheme.FontSmall, UiTheme.Semi);
            var wr = new Rect2(0, wy, 12 + w1 + 14 + 18 + w2 + 14 + 18 + w3 + 12, ChipH);
            UiTheme.DrawRound(this, wr, new Color(UiTheme.PanelBg, 0.7f), 10, UiTheme.PanelBorder);
            float cx = 12;
            UiTheme.DrawText(this, new Vector2(cx, wy + 16), time, UiTheme.FontSmall, UiTheme.Ink, UiTheme.Semi);
            cx += w1 + 14;
            Icon(2, new Vector2(cx + 5, wy + ChipH / 2), s.FeltTemp < Clothing.Comfort ? UiTheme.Info : UiTheme.Dim);
            UiTheme.DrawText(this, new Vector2(cx + 14, wy + 16), temp, UiTheme.FontSmall, UiTheme.Ink, UiTheme.Semi);
            cx += 18 + w2 + 14;
            Icon(0, new Vector2(cx + 5, wy + ChipH / 2), s.RadHere > s.RadProt ? UiTheme.Accent : UiTheme.Dim);
            UiTheme.DrawText(this, new Vector2(cx + 14, wy + 16), rad, UiTheme.FontSmall, s.RadHere > s.RadProt ? UiTheme.Accent : UiTheme.Ink, UiTheme.Semi);
        }

        /// <summary>Значки: 0 — радиация (трилистник), 1 — тошнота (волна), 2 — снежинка, 3 — капля.</summary>
        void Icon(int kind, Vector2 c, Color col)
        {
            switch (kind)
            {
                case 0:
                    for (int k = 0; k < 3; k++)
                    {
                        float a0 = -Mathf.Pi / 2 + k * Mathf.Tau / 3 - 0.5f;
                        var pts = new Vector2[8];
                        pts[0] = c + new Vector2(Mathf.Cos(a0 + 0.5f), Mathf.Sin(a0 + 0.5f)) * 1.6f;
                        for (int i = 0; i < 7; i++) { float a = a0 + i / 6f; pts[i + 1] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 6f; }
                        DrawColoredPolygon(pts, col);
                    }
                    DrawCircle(c, 1.3f, col);
                    break;
                case 1:
                    for (int i = 0; i < 2; i++)
                    {
                        var pts = new Vector2[7];
                        for (int k = 0; k < 7; k++) pts[k] = c + new Vector2(-6 + k * 2, (i * 4 - 2) + ((k & 1) == 0 ? -1.2f : 1.2f));
                        DrawPolyline(pts, col, 1.6f, true);
                    }
                    break;
                case 2:
                    for (int k = 0; k < 3; k++)
                    {
                        var d = new Vector2(Mathf.Cos(k * Mathf.Pi / 3), Mathf.Sin(k * Mathf.Pi / 3)) * 5.5f;
                        DrawLine(c - d, c + d, col, 1.6f, true);
                    }
                    break;
                default:
                    DrawColoredPolygon(new[] { c + new Vector2(0, -6), c + new Vector2(4, 1), c + new Vector2(0, 5), c + new Vector2(-4, 1) }, col);
                    break;
            }
        }
    }

    /// <summary>
    /// Звуки состояния (D47, docs/sound-list.md): щелчки дозиметра чаще при большем облучении, дрожь на холоде,
    /// дыхание в противогазе. Своих файлов ещё нет — заглушки (lock_click, rummage); дрожь и дыхание — только когда появятся файлы.
    /// </summary>
    public sealed class WearSfx
    {
        float _tick, _shiver, _breath;

        public void Update(SelfInfo s, float dt, Vector2 at)
        {
            if (s == null || s.Dead) return;
            float lack = s.RadHere - s.RadProt;
            // дозиметр трещит и без прибора — у самого опасного (как в документе); частота — по недостающей защите
            if (lack > 0 || s.RadHere >= 20)
            {
                _tick -= dt;
                if (_tick <= 0)
                {
                    string n = lack > 20 ? "rad_tick_high" : lack > 8 ? "rad_tick_mid" : "rad_tick_low";
                    Sfx.Play(Sfx.Pick(n, "lock_click"), at, -20, 6, 0.3f, 1.6f);
                    _tick = Mathf.Max(0.12f, 1.4f - Mathf.Max(lack, 1) * 0.05f) * (0.6f + GD.Randf() * 0.8f);
                }
            }
            if (s.Freezing && s.Cold > 30)
            {
                _shiver -= dt;
                if (_shiver <= 0) { if (Sfx.Get("cold_shiver") != null) Sfx.Play("cold_shiver", at, -10, 8); _shiver = 9 + GD.Randf() * 6; }
            }
            // противогаз: глухое дыхание (только когда появится файл)
            if (s.Worn.Count > 1 && s.Worn[1].Id is "wear_ozk_gp5")
            {
                _breath -= dt;
                if (_breath <= 0) { if (Sfx.Get("gasmask_breath_loop") != null) Sfx.Play("gasmask_breath_loop", at, -14, 6, 0.05f); _breath = 5f; }
            }
        }
    }
}
