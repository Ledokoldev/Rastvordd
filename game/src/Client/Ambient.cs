using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>
    /// Фон: ветер (сильнее в тайге, пустоши и на пляже), прибой у воды, птицы днём, сверчки ночью.
    /// Громкости плавно следуют за местом и временем суток.
    /// </summary>
    public sealed class Ambient
    {
        readonly MapInfo _map;
        readonly AudioStreamPlayer _wind, _surf, _birds, _crickets;
        float _gWind, _gSurf, _gBirds, _gCrickets;

        public Ambient(Node parent, MapInfo map)
        {
            _map = map;
            _wind = Make(parent, "amb_wind");
            _surf = Make(parent, "amb_surf");
            _birds = Make(parent, "amb_birds");
            _crickets = Make(parent, "amb_crickets");
        }

        static AudioStreamPlayer Make(Node parent, string name)
        {
            var loop = Sfx.Loop(name, -80, 1);   // берём только поток с включённой петлёй
            var p = new AudioStreamPlayer { Stream = loop.Stream, VolumeDb = -80 };
            loop.QueueFree();
            parent.AddChild(p);
            if (p.Stream != null) p.Play();
            return p;
        }

        public void Update(SVec pos, float dayTime, float dt)
        {
            var b = TerrainView.BiomeAt(_map, pos);
            float night = World.NightFactorAt(dayTime);
            bool open = b is Biome.Tundra or Biome.Arid or Biome.Beach;
            bool green = b is Biome.Forest or Biome.Grassland or Biome.Swamp;

            // близость воды: проверяем точки по кругу
            float water = 0;
            for (int r = 6; r <= 24; r += 6)
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.Pi / 4;
                    if (TerrainView.BiomeAt(_map, pos + new SVec(Mathf.Cos(a) * r, Mathf.Sin(a) * r)) == Biome.Water)
                        water = Mathf.Max(water, 1f - (r - 6) / 24f);
                }

            Fade(_wind, ref _gWind, open ? 0.55f : 0.28f, dt);
            Fade(_surf, ref _gSurf, water * 0.6f, dt);
            Fade(_birds, ref _gBirds, green ? 0.4f * (1 - night) : 0, dt);
            Fade(_crickets, ref _gCrickets, green ? 0.3f * night : 0, dt);
        }

        static void Fade(AudioStreamPlayer p, ref float gain, float target, float dt)
        {
            gain = Mathf.MoveToward(gain, target, dt * 0.5f);
            p.VolumeDb = gain <= 0.001f ? -80 : Mathf.LinearToDb(gain);
        }
    }
}
