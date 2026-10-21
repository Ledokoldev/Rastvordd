using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>
    /// Steam Audio (Valve) — настоящий HRTF для наушников через плагин godot-steam-audio (addons/godot-steam-audio).
    /// Мир у нас 2D, поэтому держим невидимую 3D-сцену в отдельном SubViewport: слушатель всегда в начале координат
    /// и смотрит в «верх экрана», источники ставим относительно него (x → x, y экрана → z).
    /// Затухание, задержку, окклюзию и эхо места по-прежнему считает Acoustics; Steam Audio отвечает только за направление (HRTF).
    /// Плагин в альфе — если класса нет или он упал при создании, остаётся наш упрощённый бинаурал.
    /// </summary>
    public static class SteamHrtf
    {
        static Node3D _root;
        static string _bus = "Master", _busOcc = "Master";
        static int _played;
        public static bool Ready => _root != null;

        public static void Init(Node parent, MapInfo map)
        {
            if (!ClassDB.ClassExists("SteamAudioPlayer") || !ClassDB.ClassExists("SteamAudioConfig")) return;
            var vp = new SubViewport
            {
                Size = new Vector2I(2, 2), OwnWorld3D = true, AudioListenerEnable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            };
            parent.AddChild(vp);
            var root = new Node3D { Name = "SteamAudioWorld" };
            vp.AddChild(root);
            if (ClassDB.Instantiate("SteamAudioConfig").AsGodotObject() is not Node cfg) return;
            root.AddChild(cfg);
            var cam = new Camera3D { Position = new Vector3(0, 1.7f, 0), Current = true };   // смотрит в −Z = верх экрана
            root.AddChild(cam);
            if (ClassDB.Instantiate("SteamAudioListener").AsGodotObject() is Node listener) cam.AddChild(listener);

            _bus = MakeBus("AcHrtf", false);
            _busOcc = MakeBus("AcHrtfOcc", true);
            _root = root;
            GD.Print("Steam Audio HRTF: готов");
        }

        static string MakeBus(string name, bool occluded)
        {
            int i = AudioServer.BusCount;
            AudioServer.AddBus();
            AudioServer.SetBusName(i, name);
            AudioServer.SetBusSend(i, "Master");
            if (occluded) AudioServer.AddBusEffect(i, new AudioEffectLowPassFilter { CutoffHz = 750 });
            return name;
        }

        /// <summary>Сухой (прямой) звук с направлением. rel — источник относительно слушателя, метры.</summary>
        const int PoolSize = 24;
        static readonly System.Collections.Generic.List<Node3D> _pool = new();
        static int _next;

        public static void Play(AudioStream stream, SVec rel, float db, float pitch, bool occluded)
        {
            if (_root == null || db < -60) return;
            // плееры Steam Audio — из пула: каждый новый плеер плагин держит в нативной памяти и не отдаёт
            // (на ×10 в песочнице процесс рос на ~45 МБ за минуту мира); пул — 24 штуки, занятые по кругу
            Node3D p = null;
            for (int i = 0; i < _pool.Count && p == null; i++)
            {
                var q = _pool[(_next + i) % _pool.Count];
                if (!(bool)q.Call("is_playing")) p = q;
            }
            if (p == null && _pool.Count < PoolSize)
            {
                if (ClassDB.Instantiate("SteamAudioPlayer").AsGodotObject() is not Node3D np) return;
                np.Set("distance_attenuation", false);   // громкость по расстоянию уже посчитана
                np.Set("occlusion", false);
                np.Set("reflection", false);
                np.Set("attenuation_model", 3);             // AudioStreamPlayer3D: ATTENUATION_DISABLED
                np.Set("unit_size", 100f);
                _root.AddChild(np);
                _pool.Add(np);
                p = np;
            }
            p ??= _pool[_next % _pool.Count];             // все заняты — заменяем самый давний звук
            _next = (_pool.IndexOf(p) + 1) % _pool.Count;
            p.Set("bus", occluded ? _busOcc : _bus);
            // совсем рядом HRTF «внутри головы» — отодвигаем на 0,8 м в ту же сторону
            var d = new Vector3(rel.X, -0.3f, rel.Y);
            if (new Vector2(rel.X, rel.Y).Length() < 0.8f) d = new Vector3(0, -0.3f, -0.8f);
            p.Position = new Vector3(0, 1.7f, 0) + d;
            p.Call("play_stream", stream, 0f, db, pitch);
            if (OS.IsStdOutVerbose() || _played++ == 0) GD.Print($"Steam Audio: звук через HRTF ({rel.X:0.0}; {rel.Y:0.0}) м");
        }
    }
}
