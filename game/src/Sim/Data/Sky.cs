namespace Rastvordd.Sim
{
    /// <summary>Погода (небо): одна на весь остров. Клиент рисует, сервер — холод, намокание, обзор.</summary>
    public enum Sky { Clear, Wind, Rain, Downpour, Storm, Snow, HeavySnow, Blizzard, Fog }

    public static class SkyInfo
    {
        public static readonly string[] Names = { "Ясно", "Ветер", "Дождь", "Ливень", "Гроза", "Снег", "Сильный снег", "Буран", "Туман" };
        static float SS(float a, float b, float x) { x = System.Math.Clamp((x - a) / (b - a), 0f, 1f); return x * x * (3f - 2f * x); }
        /// <summary>Плавная смена погоды (blend 0..1 за WxRampSec): вес новой погоды для неба/цвета/тумана/облаков — тучи приходят первыми.</summary>
        public static float CloudW(float blend) => SS(0f, 0.7f, blend);
        /// <summary>Осадки новой погоды начинаются позже туч (вторая половина смены).</summary>
        public static float PrecipIn(float blend) => SS(0.35f, 1f, blend);
        /// <summary>Осадки старой погоды стихают в первой половине смены.</summary>
        public static float PrecipOut(float blend) => 1f - SS(0f, 0.6f, blend);
        public static bool Wet(Sky s) => s is Sky.Rain or Sky.Downpour or Sky.Storm;
        public static bool Snowy(Sky s) => s is Sky.Snow or Sky.HeavySnow or Sky.Blizzard;
        /// <summary>Сила ветра 0..1: наклон дождя, снос снега, полосы ветра.</summary>
        public static float Wind(Sky s) => s switch { Sky.Wind => 0.8f, Sky.Rain => 0.15f, Sky.Downpour => 0.3f, Sky.Storm => 0.7f, Sky.Snow => 0.1f, Sky.HeavySnow => 0.35f, Sky.Blizzard => 1f, Sky.Fog => 0.03f, _ => 0f };
        /// <summary>Множитель дальности обзора.</summary>
        public static float ViewMul(Sky s) => s switch { Sky.Downpour => 0.85f, Sky.Storm => 0.8f, Sky.HeavySnow => 0.75f, Sky.Blizzard => 0.5f, Sky.Fog => 0.65f, _ => 1f };
    }
}
