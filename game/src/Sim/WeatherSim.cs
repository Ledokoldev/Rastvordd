using System;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Погода во времени: ветер (направление гуляет, порывы, в буре и грозе резкие смены), снег копится и тает,
    /// земля мокнет и сохнет (лужи). Ходьба: снегопад и буран тормозят, против ветра — сильнее. Числа — здесь.
    /// </summary>
    public sealed partial class World
    {
        public float WindAngle, WindStrength;   // откуда дует ветер не важно — куда: угол движения воздуха (рад), сила 0..1
        public float SnowCover;                 // 0..1 — сколько снега лежит (на земле и на объектах)
        public float Wetness;                   // 0..1 — мокрая земля, лужи
        float _windTarget, _windTurnT;
        readonly Random _wxRng = new(7171);

        // ---------- цикл погоды: детерминирован от зерна мира и номера смены, сохраняется ----------
        public float WxLeft = -1f;   // сколько ещё секунд держится текущая погода (<0 — ещё не начат цикл)
        public int WxIndex;          // номер смены погоды (зерно следующей)
        public static bool WxLog = true;
        /// <summary>Плавность смены: прежняя погода и доля перехода 0..1 (за WxRampSec игровых секунд).</summary>
        public Sky WxPrev; public float WxBlend = 1f;
        public const float WxRampSec = 90f;

        static float WxDur(Sky s, Random r)
        {
            var (a, b) = s switch
            {
                Sky.Clear => (300f, 720f), Sky.Wind => (180f, 420f), Sky.Rain => (240f, 540f), Sky.Downpour => (150f, 300f),
                Sky.Storm => (120f, 270f), Sky.Fog => (180f, 420f), Sky.Snow => (240f, 480f), Sky.HeavySnow => (150f, 300f), _ => (90f, 200f),
            };
            return a + (float)r.NextDouble() * (b - a);
        }

        /// <summary>Следующая погода: веса по времени суток (туман — на рассвете и ночью) и по текущей (ливень/гроза — из дождя, буран — из снега).</summary>
        Sky WxPick(Random r)
        {
            float tod = TimeOfDay, night = NightFactorAt(tod);
            float dawn = MathF.Max(0f, 1f - MathF.Abs(tod - 0.26f) / 0.08f);
            bool wet = SkyInfo.Wet(Sky), snowy = SkyInfo.Snowy(Sky);
            float fogW = 3f + 22f * MathF.Max(dawn, night) - (tod > 0.4f && tod < 0.7f ? 2.5f : 0f);
            bool windy = Sky is Sky.Wind or Sky.Fog;   // «тучи»: дождь обычно после ветра/хмари, гроза — лишь иногда после дождя
            var w = new (Sky s, float w)[]
            {
                (Sky.Clear, snowy ? 10f : 34f), (Sky.Wind, wet ? 8f : 14f), (Sky.Rain, windy ? 22f : wet ? 6f : 3f), (Sky.Downpour, wet || windy ? 5f : 0.5f),
                (Sky.Storm, wet ? 3f : 0f), (Sky.Fog, snowy ? 0f : fogW),
                (Sky.Snow, snowy ? 14f : 0.35f), (Sky.HeavySnow, snowy ? 9f : 0f), (Sky.Blizzard, Sky == Sky.HeavySnow ? 5f : 0f),   // снег — редкая зимняя полоса (~1–2 % в сутки), зато надолго
            };
            float tot = 0; foreach (var x in w) tot += x.s == Sky ? 0f : x.w;
            float pick = (float)r.NextDouble() * tot;
            foreach (var x in w) { pick -= x.s == Sky ? 0f : x.w; if (pick <= 0) return x.s; }
            return Sky.Clear;
        }

        void StepWeatherCycle()
        {
            if (WxLeft < 0) { var r0 = new Random(unchecked(Seed * 7919 + 17)); WxLeft = WxDur(Sky, r0); return; }
            if ((WxLeft -= Dt) > 0) return;
            NextWeather(null, 0f);
        }

        /// <summary>Сменить погоду: force — на заданную (админка/отладка), иначе — по таблице; dur 0 — обычная длительность.</summary>
        public void NextWeather(Sky? force, float dur)
        {
            var r = new Random(unchecked(Seed * 7919 + ++WxIndex * 104729));
            var old = Sky;
            var s = force ?? WxPick(r);
            if (s != old) { if (WxBlend >= 0.5f) WxPrev = old; WxBlend = 0f; }   // смена посреди смены — уходим от той, что ещё преобладала
            SetSky(s);
            WxLeft = dur > 0 ? dur : WxDur(s, r);
            if (WxLog) Console.WriteLine($"[погода] {Time,7:0}s сутки {TimeOfDay:0.00}: {SkyInfo.Names[(int)old]} → {SkyInfo.Names[(int)s]} на {WxLeft / 60f:0.0} мин (мокро {Wetness:0.00}, снег {SnowCover:0.00})");
        }

        void StepWeather()
        {
            StepWeatherCycle();
            WxBlend = MathF.Min(1f, WxBlend + Dt / WxRampSec);
            float cw = SkyInfo.CloudW(WxBlend);
            float target = SkyInfo.Wind(WxPrev) + (SkyInfo.Wind(Sky) - SkyInfo.Wind(WxPrev)) * cw;   // ветер крепнет/стихает вместе с тучами
            bool gusty = cw > 0.5f && Sky is Sky.Wind or Sky.Storm or Sky.Blizzard or Sky.HeavySnow;
            // направление: плавно гуляет; в бурю/грозу/ветер — каждые 5–15 с резкий разворот на 40–120°
            if ((_windTurnT -= Dt) <= 0)
            {
                _windTurnT = gusty ? 5f + (float)_wxRng.NextDouble() * 10f : 20f + (float)_wxRng.NextDouble() * 30f;
                float turn = (gusty ? 0.7f + (float)_wxRng.NextDouble() * 1.4f : (float)_wxRng.NextDouble() * 0.6f) * (_wxRng.Next(2) == 0 ? -1 : 1);
                _windTarget = WindAngle + turn;
            }
            WindAngle += Math.Clamp(Geo.AngleDiff(WindAngle, _windTarget), -0.9f * Dt, 0.9f * Dt);
            float gust = gusty ? 0.82f + 0.18f * MathF.Sin(Time * 0.9f) * MathF.Sin(Time * 0.37f + 1.3f) : 1f;
            WindStrength += (target * gust - WindStrength) * Math.Min(1f, Dt * 0.6f);

            // снег: копится в снегопад (сильнее — быстрее), тает без него (под дождём — вдвое быстрее)
            float snowRate = Sky switch { Sky.Snow => 1f / 240f, Sky.HeavySnow => 1f / 110f, Sky.Blizzard => 1f / 70f, _ => 0f };
            SnowCover = snowRate > 0 ? MathF.Min(1f, SnowCover + snowRate * Dt) : MathF.Max(0f, SnowCover - (SkyInfo.Wet(Sky) ? 1f / 90f : 1f / 200f) * Dt);
            // мокрота и лужи: дождь мочит, без дождя сохнет за ~4 мин
            float wetRate = Sky switch { Sky.Rain => 1f / 80f, Sky.Downpour => 1f / 35f, Sky.Storm => 1f / 30f, _ => 0f } * SkyInfo.PrecipIn(WxBlend);
            Wetness = wetRate > 0 ? MathF.Min(1f, Wetness + wetRate * Dt) : MathF.Max(0f, Wetness - Dt / 240f);
        }

        /// <summary>Против ветра медленнее на этот процент (× сила ветра), по ветру — быстрее на половину его.</summary>
        public const float WindHeadPenalty = 0.2f;

        /// <summary>
        /// Ходьба по погоде (на улице): общее замедление — только в ливень, грозу и буран; ветер (в любой погоде, где он есть) —
        /// против ветра медленнее до WindHeadPenalty × сила, по ветру быстрее на вдвое меньший процент, боком — без изменений.
        /// </summary>
        public float WeatherSpeedMul(Actor a, Vector2 move)
        {
            if (a.Indoors || a.Swimming) return 1f;
            float k = Sky switch { Sky.Downpour => 0.95f, Sky.Storm => 0.92f, Sky.Blizzard => 0.85f, _ => 1f };
            if (move.LengthSquared() > 0.01f && WindStrength > 0.05f)
            {
                var wind = new Vector2(MathF.Cos(WindAngle), MathF.Sin(WindAngle));
                float along = Vector2.Dot(Vector2.Normalize(move), wind);                  // +1 — по ветру, −1 — против
                k *= along < 0 ? 1f + WindHeadPenalty * WindStrength * along : 1f + WindHeadPenalty * 0.5f * WindStrength * along;
            }
            return k;
        }
    }
}
