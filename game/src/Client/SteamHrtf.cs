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
        public static void Play(AudioStream stream, SVec rel, float db, float pitch, bool occluded)
        {
            if (_root == null || db < -60) return;
            if (ClassDB.Instantiate("SteamAudioPlayer").AsGodotObject() is not Node3D p) return;
            p.Set("distance_attenuation", false);   // громкость по расстоянию уже посчитана
            p.Set("occlusion", false);
            p.Set("reflection", false);
            p.Set("bus", occluded ? _busOcc : _bus);
            p.Set("attenuation_model", 3);             // AudioStreamPlayer3D: ATTENUATION_DISABLED
            p.Set("unit_size", 100f);
            // совсем рядом HRTF «внутри головы» — отодвигаем на 0,8 м в ту же сторону
            var d = new Vector3(rel.X, -0.3f, rel.Y);
            if (new Vector2(rel.X, rel.Y).Length() < 0.8f) d = new Vector3(0, -0.3f, -0.8f);
            p.Position = new Vector3(0, 1.7f, 0) + d;
            _root.AddChild(p);
            p.Connect("finished", Callable.From(p.QueueFree));
            p.Call("play_stream", stream, 0f, db, pitch);
            if (OS.IsStdOutVerbose() || _played++ == 0) GD.Print($"Steam Audio: звук через HRTF ({rel.X:0.0}; {rel.Y:0.0}) м");
        }
    }
}
