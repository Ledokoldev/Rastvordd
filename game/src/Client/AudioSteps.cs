using Godot;

namespace Rastvordd.Client
{
    /// <summary>Подстройки из админки (F1), сохраняются в user://tuning.cfg.</summary>
    public static class Tuning
    {
        const string Path = "user://tuning.cfg";

        /// <summary>Скорость записи шагов по поверхностям (1 = как записано при обычном шаге).</summary>
        public static float StepsConcrete = 1f, StepsGrass = 1f, StepsSnow = 1f;
        public static float StepsVolumeDb = -6f;
        public static float AmbientVolumeDb = 0f;          // общий фон (ветер, прибой, птицы, сверчки)
        public static bool StepsStretch = true;            // растяжение без изменения высоты (можно выключить для сравнения)
        public static bool SteamHrtf = true;               // направление звука через Steam Audio HRTF (иначе наш бинаурал)

        public static void Load()
        {
            var cfg = new ConfigFile();
            if (cfg.Load(Path) != Error.Ok) return;
            StepsConcrete = (float)cfg.GetValue("steps", "concrete", StepsConcrete);
            StepsGrass = (float)cfg.GetValue("steps", "grass", StepsGrass);
            StepsSnow = (float)cfg.GetValue("steps", "snow", StepsSnow);
            StepsVolumeDb = (float)cfg.GetValue("steps", "volume_db", StepsVolumeDb);
            AmbientVolumeDb = (float)cfg.GetValue("ambient", "volume_db", AmbientVolumeDb);
            StepsStretch = (bool)cfg.GetValue("steps", "stretch", StepsStretch);
            SteamHrtf = (bool)cfg.GetValue("acoustics", "steam_hrtf", SteamHrtf);
        }

        public static void Save()
        {
            var cfg = new ConfigFile();
            cfg.SetValue("steps", "concrete", StepsConcrete);
            cfg.SetValue("steps", "grass", StepsGrass);
            cfg.SetValue("steps", "snow", StepsSnow);
            cfg.SetValue("steps", "volume_db", StepsVolumeDb);
            cfg.SetValue("ambient", "volume_db", AmbientVolumeDb);
            cfg.SetValue("steps", "stretch", StepsStretch);
            cfg.SetValue("acoustics", "steam_hrtf", SteamHrtf);
            cfg.Save(Path);
        }
    }

    public enum StepSurface { None, Concrete, Grass, Snow }

    /// <summary>
    /// Шаги игрока из длинных записей: файл идёт непрерывно, пока идёшь, и встаёт на паузу при остановке
    /// (следующий шаг продолжает с того же места). Скорость — растяжение по времени без изменения высоты:
    /// PitchScale плеера ускоряет запись, а эффект PitchShift на шине возвращает высоту обратно.
    /// </summary>
    public sealed class FootstepLoop
    {
        readonly AudioStreamPlayer _concrete, _grass, _snow;
        readonly AudioEffectPitchShift _shift;
        AudioStreamPlayer _active;
        float _fade;   // 0..1: громкость для мягкой остановки

        public FootstepLoop(Node parent)
        {
            int bus = AudioServer.BusCount;
            AudioServer.AddBus();
            AudioServer.SetBusName(bus, "PlayerSteps");
            AudioServer.SetBusSend(bus, "Master");
            _shift = new AudioEffectPitchShift { FftSize = AudioEffectPitchShift.FftSizeEnum.Size2048 };
            AudioServer.AddBusEffect(bus, _shift);

            _concrete = Make(parent, "steps_concrete");
            _grass = Make(parent, "steps_grass");
            _snow = Make(parent, "steps_snow");
        }

        static AudioStreamPlayer Make(Node parent, string name)
        {
            var s = Sfx.Get(name);
            if (s is AudioStreamMP3 mp3) mp3.Loop = true;   // дошла до конца — тихо продолжает с начала
            var p = new AudioStreamPlayer { Stream = s, Bus = "PlayerSteps", VolumeDb = -80 };
            parent.AddChild(p);
            return p;
        }

        /// <param name="speed">Скорость персонажа, м/с: от неё темп шагов (присед медленнее, спринт быстрее).</param>
        public void Update(StepSurface surface, float speed, float dt)
        {
            var want = surface switch
            {
                StepSurface.Concrete => _concrete,
                StepSurface.Grass => _grass,
                StepSurface.Snow => _snow,
                _ => null,
            };
            if (want?.Stream == null || speed < 0.4f) want = null;

            // сменилась поверхность или остановились — гасим текущую запись и ставим на паузу
            if (_active != null && _active != want)
            {
                _fade = Mathf.MoveToward(_fade, 0, dt * 10);
                _active.VolumeDb = Gain(_fade);
                if (_fade > 0) return;
                _active.StreamPaused = true;
                _active = null;
            }
            if (want == null) return;

            if (_active == null)
            {
                _active = want;
                if (!want.Playing) want.Play();
                want.StreamPaused = false;
            }
            _fade = Mathf.MoveToward(_fade, 1, dt * 10);

            float tune = surface switch { StepSurface.Concrete => Tuning.StepsConcrete, StepSurface.Snow => Tuning.StepsSnow, _ => Tuning.StepsGrass };
            float rate = Mathf.Clamp(speed / Sim.World.WalkSpeed, 0.45f, 1.7f) * tune;
            _active.PitchScale = rate;
            _shift.PitchScale = Tuning.StepsStretch ? 1f / rate : 1f;
            _active.VolumeDb = Gain(_fade);
        }

        static float Gain(float k) => k <= 0.001f ? -80 : Tuning.StepsVolumeDb + Mathf.LinearToDb(k);
    }
}
