using System;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Стрельба (D57): рабочая дистанция и разброс по стойке. Все числа — здесь.
    /// Рабочая дистанция (EffRange) у каждого ствола своя: дробь ≈ 9 м, ПП ≈ 18, АКМ ≈ 30 (обзор), АН-94 ≈ 45, СВТ ≈ 55,
    /// СВД ≈ 80, Мосинка ≈ 100. Дальше урон падает линейно до 40% к пределу дальности. Прицел в прицеливании
    /// растягивает рабочую дистанцию (коллиматор ×1,1, 2× — ×1,3, 4× — ×1,6).
    /// Разброс = база ствола × стойка: от бедра стоя ×1,6 (сильно хуже), в прицеле ×0,55, присед ×0,65 сверху.
    /// </summary>
    public static class Gunplay
    {
        public const float HipK = 1.6f, AdsK = 0.55f, CrouchK = 0.65f;
        public const float FalloffMin = 0.4f;

        public static float DefaultEff(string kind) => kind switch
        {
            "shotgun" => 9, "pistol" => 15, "smg" => 18, "ar" => 30, "rifle" => 55, "sniper" => 80, "bow" => 20, "crossbow" => 25, _ => 25,
        };

        /// <summary>Множитель рабочей дистанции от прицела (только в прицеливании).</summary>
        public static float SightEffMul(float sightZoom) => sightZoom <= 0 ? 1f : sightZoom < 1.2f ? 1.1f : sightZoom < 2f ? 1.3f : 1.6f;

        /// <summary>Урон на дистанции: полный до рабочей, дальше линейно до 40% к пределу.</summary>
        public static float Falloff(float eff, float max, float dist)
        {
            if (dist <= eff || max <= eff) return 1f;
            float k = Math.Clamp((dist - eff) / (max - eff), 0f, 1f);
            return 1f - (1f - FalloffMin) * k;
        }
    }
}
