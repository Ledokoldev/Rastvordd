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
        /// <summary>Сколько раз звучал каждый звук (отладка: --sfxlog печатает при выходе).</summary>
        public static readonly Dictionary<string, int> Played = new();

        public static void Init(Node parent) => _parent = parent;

        public static AudioStream Get(string name)
        {
            if (Cache.TryGetValue(name, out var s)) return s;
            // сначала настоящие сэмплы (audio/real/*.mp3), потом синтезированные заглушки
            var real = $"res://audio/real/{name}.mp3";
            var res = $"res://audio/{name}.wav";
            s = ResourceLoader.Exists(real) ? GD.Load<AudioStream>(real) : ResourceLoader.Exists(res) ? GD.Load<AudioStream>(res) : null;
            Cache[name] = s;
            return s;
        }

        /// <summary>Звук по id, а если файла ещё нет — заглушка (D46: свои звуки кладутся потом по имени файла).</summary>
        public static string Pick(string name, string fallback) => Get(name) != null ? name : fallback;

        /// <param name="hearM">Дальше этого расстояния (м) не слышно.</param>
        /// <summary>Сколько эха места подмешать: шаги и мелочь — чуть-чуть (в поле не гудят), выстрелы и взрывы — полностью.</summary>
        public enum Room { Full, Quiet }

        public static void Play(string name, Vector2 posPx, float volumeDb = 0, float hearM = 40, float pitchVar = 0.08f, float basePitch = 1f, Room room = Room.Full)
        {
            var s = Get(name);
            if (s == null || _parent == null) return;
            Played[name] = Played.GetValueOrDefault(name) + 1;
            if (Acoustics.Ready)
            {
                // звук мира — через акустику: задержка, окклюзия, эхо, бинаурал
                Acoustics.Play(s, new System.Numerics.Vector2(posPx.X / Iso.PPM, posPx.Y / Iso.PPM), volumeDb, hearM, Acoustics.RandomPitch(basePitch, pitchVar), room == Room.Full ? 1f : 0.3f);
                return;
            }
            var p = new AudioStreamPlayer2D
            {
                Stream = s, Position = posPx, VolumeDb = volumeDb,
                MaxDistance = hearM * Iso.PPM, Attenuation = 1.6f,
                PitchScale = basePitch * (1f + Rng.RandfRange(-pitchVar, pitchVar)),
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
