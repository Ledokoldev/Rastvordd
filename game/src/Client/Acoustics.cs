using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>
    /// Акустика мира (без внешних библиотек).
    ///
    /// Уровень 1 — физика звука в мире:
    ///  • задержка по скорости звука (343 м/с): дальний выстрел слышен позже вспышки;
    ///  • затухание с расстоянием — линейное (по просьбе пользователя): вплотную полная громкость, на краю слышимости
    ///    тихо (−16 дБ), но слышно; дальше края — тишина. Край у каждого звука свой (шаги, выстрелы по типу ствола);
    ///  • ближний/дальний звук: с расстоянием растёт глухой «дальний» слой — тоже по направлению, чтобы было понятно, откуда;
    ///  • окклюзия: стены и стволы между слушателем и источником глушат звук (НЧ-фильтр, −7 дБ);
    ///  • реверберация по месту, где стоит слушатель: двор РТ гулкий, лес мягкий, поле сухое; шаги почти без неё.
    ///
    /// Уровень 2 — бинаурал для наушников (упрощённый HRTF):
    ///  • 16 шин-направлений; в каждой своя межушная задержка (до 0,66 мс) и разница громкостей ушей;
    ///  • задние направления глуше на высоких частотах — так перед отличается от зада;
    ///  • «перед» — верх экрана: слушаем сверху над персонажем, как смотрит камера.
    /// </summary>
    public static class Acoustics
    {
        const int Sectors = 16;
        const float SpeedOfSound = 343f;
        const float MaxItdMs = 0.66f;
        const float EdgeGain = 0.16f;   // громкость на краю слышимости (≈ −16 дБ)

        static Node _parent;
        static readonly string[] Dry = new string[Sectors];
        static readonly string[] DryOccluded = new string[Sectors];
        static readonly string[] FarDir = new string[Sectors];
        static AudioEffectReverb _roomReverb;
        static SVec _listener;
        static readonly List<(SVec a, SVec b)> _wallBoxes = new();
        static readonly List<(SVec c, float r)> _trees = new();
        /// <summary>Стены построек — тоже глушат звук.</summary>
        public static readonly List<(SVec a, SVec b)> DynamicWalls = new();
        static readonly List<(double at, System.Action play)> _pending = new();
        static double _clock;
        static readonly RandomNumberGenerator Rng = new();
        public static bool Ready => _parent != null;

        public static void Init(Node parent, MapInfo map)
        {
            _parent = parent;
            foreach (var w in map.Walls)
                if (w.Height >= 2f) _wallBoxes.Add((w.Min, w.Max));

            // реверберация места (где стоит слушатель) и «дальний» слой
            int room = AddBus("AcRoom");
            _roomReverb = new AudioEffectReverb { RoomSize = 0.3f, Damping = 0.6f, Wet = 0.12f, Dry = 0f, PredelayMsec = 20 };
            AudioServer.AddBusEffect(room, _roomReverb);

            // бинауральные направления
            for (int i = 0; i < Sectors; i++)
            {
                float az = i * Mathf.Tau / Sectors;   // 0 — перед (верх экрана), по часовой
                Dry[i] = $"AcDir{i}";
                DryOccluded[i] = $"AcDirOcc{i}";
                FarDir[i] = $"AcFarDir{i}";
                foreach (var (name, occluded) in new[] { (Dry[i], false), (DryOccluded[i], true), (FarDir[i], false) })
                {
                    int bus = AddBus(name);
                    float s = Mathf.Sin(az), c = Mathf.Cos(az);
                    // межушная задержка: дальнее ухо слышит позже; разница громкостей ушей
                    var delay = new AudioEffectDelay
                    {
                        Dry = 0f, FeedbackActive = false,
                        Tap1Active = true, Tap1Pan = -1f, Tap1DelayMs = Mathf.Max(0, s) * MaxItdMs + 0.01f, Tap1LevelDb = Mathf.Max(0, s) * -7f,
                        Tap2Active = true, Tap2Pan = 1f, Tap2DelayMs = Mathf.Max(0, -s) * MaxItdMs + 0.01f, Tap2LevelDb = Mathf.Max(0, -s) * -7f,
                    };
                    AudioServer.AddBusEffect(bus, delay);
                    // сзади — тусклее на высоких
                    if (c < 0) AudioServer.AddBusEffect(bus, new AudioEffectHighShelfFilter { CutoffHz = 2500, Gain = Mathf.DbToLinear(-7f * -c) });
                    if (occluded) AudioServer.AddBusEffect(bus, new AudioEffectLowPassFilter { CutoffHz = 750 });
                    if (name == FarDir[i])
                    {
                        // дальний раскат: глухой и чуть размытый, но с того же направления (раньше шёл отовсюду — не понять, откуда)
                        AudioServer.AddBusEffect(bus, new AudioEffectLowPassFilter { CutoffHz = 1300 });
                        AudioServer.AddBusEffect(bus, new AudioEffectReverb { RoomSize = 0.6f, Damping = 0.6f, Wet = 0.18f, Dry = 0.82f, PredelayMsec = 40, Spread = 0.2f });
                    }
                }
            }
        }

        static int AddBus(string name)
        {
            int i = AudioServer.BusCount;
            AudioServer.AddBus();
            AudioServer.SetBusName(i, name);
            AudioServer.SetBusSend(i, "Master");
            return i;
        }

        /// <summary>Каждый кадр: где слушатель, какие деревья рядом, какая реверберация места.</summary>
        public static void Update(SVec listenerM, List<NodeSnap> nodes, Biome biome, bool inConcreteYard, double dt)
        {
            _listener = listenerM;
            _trees.Clear();
            foreach (var n in nodes)
                if (n.Kind == NodeKind.Tree) _trees.Add((n.Pos, n.Radius * 0.9f));

            // реверберация места: плавно к цели
            (float room, float damp, float wet, float pre) target = inConcreteYard ? (0.82f, 0.25f, 0.32f, 45f)
                : biome switch
                {
                    // открытое место почти сухое: отражать звук нечему (раньше в поле «гудело»)
                    Biome.Forest => (0.4f, 0.75f, 0.1f, 15f),
                    Biome.Swamp => (0.3f, 0.8f, 0.05f, 15f),
                    Biome.Tundra => (0.25f, 0.9f, 0.03f, 20f),   // снег глушит
                    Biome.Arid or Biome.Beach => (0.2f, 0.6f, 0.03f, 20f),
                    _ => (0.2f, 0.7f, 0.03f, 15f),
                };
            float k = (float)Mathf.Min(1, dt * 1.5);
            _roomReverb.RoomSize = Mathf.Lerp(_roomReverb.RoomSize, target.room, k);
            _roomReverb.Damping = Mathf.Lerp(_roomReverb.Damping, target.damp, k);
            _roomReverb.Wet = Mathf.Lerp(_roomReverb.Wet, target.wet, k);
            _roomReverb.PredelayMsec = Mathf.Lerp(_roomReverb.PredelayMsec, target.pre, k);

            _clock += dt;
            for (int i = _pending.Count - 1; i >= 0; i--)
                if (_pending[i].at <= _clock) { _pending[i].play(); _pending.RemoveAt(i); }
        }

        /// <summary>Звук в точке мира. Задержка, расстояние, окклюзия, направление и эхо считаются здесь.</summary>
        /// <param name="roomSend">доля эха места: 1 — выстрелы и взрывы, 0,3 — шаги и мелочь</param>
        public static void Play(AudioStream stream, SVec posM, float volumeDb, float hearM, float pitch, float roomSend = 1f)
        {
            if (stream == null || _parent == null) return;
            var d = posM - _listener;
            float dist = d.Length();
            if (dist > hearM) return;

            // затухание линейное: вплотную (≤ 1 м) — полная громкость, на краю слышимости — EdgeGain
            float near = Mathf.Clamp((dist - 1f) / Mathf.Max(1f, hearM - 1f), 0f, 1f);
            float db = volumeDb + Mathf.LinearToDb(Mathf.Lerp(1f, EdgeGain, near));

            bool occluded = dist > 1.5f && Occluded(_listener, posM);
            if (occluded) db -= 7f;

            // направление относительно экрана: 0 — вверх (перед), по часовой
            float az = dist < 0.8f ? 0 : Mathf.Atan2(d.X, -d.Y);
            int sector = ((int)Mathf.Round(az / Mathf.Tau * Sectors) % Sectors + Sectors) % Sectors;
            string dryBus = occluded ? DryOccluded[sector] : Dry[sector];

            float farMix = Mathf.SmoothStep(40f, 220f, dist) * 0.6f;   // доля «дальнего» слоя
            double delay = dist / SpeedOfSound;

            void Start()
            {
                float dryDb = db + Mathf.LinearToDb(Mathf.Max(0.001f, 1f - farMix * 0.75f));
                if (Tuning.SteamHrtf && SteamHrtf.Ready) SteamHrtf.Play(stream, posM - _listener, dryDb, pitch, occluded);   // HRTF Valve
                else Spawn(stream, dryBus, dryDb, pitch);
                if (roomSend > 0.01f) Spawn(stream, "AcRoom", db - 2f + Mathf.LinearToDb(roomSend), pitch);   // эхо места слушателя
                if (farMix > 0.02f) Spawn(stream, FarDir[sector], db + Mathf.LinearToDb(farMix), pitch);     // глухой дальний раскат — с той же стороны
            }
            if (delay < 0.015) Start();
            else _pending.Add((_clock + delay, Start));
        }

        static void Spawn(AudioStream s, string bus, float db, float pitch)
        {
            if (db < -60) return;
            var p = new AudioStreamPlayer { Stream = s, Bus = bus, VolumeDb = db, PitchScale = pitch };
            _parent.AddChild(p);
            p.Finished += p.QueueFree;
            p.Play();
        }

        static bool Occluded(SVec a, SVec b)
        {
            foreach (var (min, max) in _wallBoxes)
                if (Geo.SegmentAabb(a, b, min, max, out _)) return true;
            foreach (var (min, max) in DynamicWalls)
                if (Geo.SegmentAabb(a, b, min, max, out _)) return true;
            foreach (var (c, r) in _trees)
                if (Geo.SegmentCircle(a, b, c, r, out float t) && t > 0.02f && t < 0.98f) return true;
            return false;
        }

        public static float RandomPitch(float basePitch, float var) => basePitch * (1f + Rng.RandfRange(-var, var));
    }
}
