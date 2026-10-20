using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>
    /// Позиционные звуки из game/audio (сейчас — синтезированные заглушки, scripts/synth-sfx.py).
    /// Громкость и панорама зависят от расстояния до камеры. Нет файла — тихо, без ошибок.
    /// </summary>
    public static class Sfx
    {
        static readonly Dictionary<string, AudioStream> Cache = new();
        static Node _parent;
        static readonly RandomNumberGenerator Rng = new();

        public static void Init(Node parent) => _parent = parent;

        public static AudioStream Get(string name)
        {
            if (Cache.TryGetValue(name, out var s)) return s;
            var res = $"res://audio/{name}.wav";
            s = ResourceLoader.Exists(res) ? GD.Load<AudioStream>(res) : null;
            Cache[name] = s;
            return s;
        }

        /// <param name="hearM">Дальше этого расстояния (м) не слышно.</param>
        public static void Play(string name, Vector2 posPx, float volumeDb = 0, float hearM = 40, float pitchVar = 0.08f)
        {
            var s = Get(name);
            if (s == null || _parent == null) return;
            var p = new AudioStreamPlayer2D
            {
                Stream = s, Position = posPx, VolumeDb = volumeDb,
                MaxDistance = hearM * Iso.PPM, Attenuation = 1.6f,
                PitchScale = 1f + Rng.RandfRange(-pitchVar, pitchVar),
            };
            _parent.AddChild(p);
            p.Finished += p.QueueFree;
            p.Play();
        }

        /// <summary>Петля (огонь в печи): возвращает плеер, который живёт вместе с объектом.</summary>
        public static AudioStreamPlayer2D Loop(string name, float volumeDb, float hearM)
        {
            var s = Get(name);
            if (s is AudioStreamWav wav && wav.LoopMode == AudioStreamWav.LoopModeEnum.Disabled)
            {
                wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                wav.LoopBegin = 0;
                wav.LoopEnd = (int)(wav.GetLength() * wav.MixRate);
            }
            return new AudioStreamPlayer2D { Stream = s, VolumeDb = volumeDb, MaxDistance = hearM * Iso.PPM, Attenuation = 1.6f };
        }
    }
}
