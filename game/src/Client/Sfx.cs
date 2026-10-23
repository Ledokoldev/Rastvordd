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
        static Node _parent;
        static readonly RandomNumberGenerator Rng = new();
        /// <summary>Сколько раз звучал каждый звук (отладка: --sfxlog печатает при выходе).</summary>
        public static readonly Dictionary<string, int> Played = new();

        public static void Init(Node parent) => _parent = parent;

        static readonly Dictionary<string, AudioStream[]> Variants = new();

        /// <summary>
        /// Звук по id. Варианты: audio/w|b/{id}_01…_08.wav (оружие, стройка — пользовательские записи) — каждый раз случайный;
        /// одиночный файл: audio/w|b/{id}.wav, потом audio/real/{id}.mp3, потом синтезированная заглушка audio/{id}.wav.
        /// </summary>
        public static AudioStream Get(string name)
        {
            if (!Variants.TryGetValue(name, out var list))
            {
                var found = new List<AudioStream>();
                foreach (var dir in new[] { "w", "b" })
                {
                    for (int i = 1; i <= 8; i++)
                    {
                        var p = $"res://audio/{dir}/{name}_{i:00}.wav";
                        if (ResourceLoader.Exists(p)) found.Add(GD.Load<AudioStream>(p));
                    }
                    var one = $"res://audio/{dir}/{name}.wav";
                    if (found.Count == 0 && ResourceLoader.Exists(one)) found.Add(GD.Load<AudioStream>(one));
                }
                if (found.Count == 0)
                {
                    var real = $"res://audio/real/{name}.mp3";
                    var res = $"res://audio/{name}.wav";
                    var s = ResourceLoader.Exists(real) ? GD.Load<AudioStream>(real) : ResourceLoader.Exists(res) ? GD.Load<AudioStream>(res) : null;
                    if (s != null) found.Add(s);
                }
                Variants[name] = list = found.ToArray();
            }
            return list.Length == 0 ? null : list[list.Length == 1 ? 0 : (int)Rng.RandiRange(0, list.Length - 1)];
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
