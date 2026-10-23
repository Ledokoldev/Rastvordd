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

        void StepWeather()
        {
            float target = SkyInfo.Wind(Sky);
            bool gusty = Sky is Sky.Wind or Sky.Storm or Sky.Blizzard or Sky.HeavySnow;
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
            float wetRate = Sky switch { Sky.Rain => 1f / 80f, Sky.Downpour => 1f / 35f, Sky.Storm => 1f / 30f, _ => 0f };
            Wetness = wetRate > 0 ? MathF.Min(1f, Wetness + wetRate * Dt) : MathF.Max(0f, Wetness - Dt / 240f);
        }

        /// <summary>Ходьба по погоде (на улице): снегопад и сугробы медленнее, буран — тяжело, против ветра — ещё тяжелее.</summary>
        public float WeatherSpeedMul(Actor a, Vector2 move)
        {
            if (a.Indoors || a.Swimming) return 1f;
            float k = 1f - 0.08f * SnowCover;                                            // по сугробам
            if (Sky == Sky.HeavySnow) k *= 0.94f;
            if (Sky == Sky.Blizzard) k *= 0.82f;
            if (move.LengthSquared() > 0.01f && WindStrength > 0.3f)
            {
                var wind = new Vector2(MathF.Cos(WindAngle), MathF.Sin(WindAngle));
                float against = -Vector2.Dot(Vector2.Normalize(move), wind);             // 1 — прямо против ветра
                k *= 1f - 0.22f * MathF.Max(0, against) * WindStrength + 0.06f * MathF.Max(0, -against) * WindStrength;
            }
            return k;
        }
    }
}
