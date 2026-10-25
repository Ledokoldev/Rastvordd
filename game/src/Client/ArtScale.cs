using System.Collections.Generic;
using System.Reflection;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>
    /// Дев-масштаб ассетов (F7): навёлся на объект, [ / ] — меньше/больше на 5%, \ — сброс. Масштаб запоминается для ВСЕХ объектов
    /// с той же картинкой (ключ — путь текстуры) в user://art_scale.json и применяется при появлении объекта; тень масштабируется вместе.
    /// </summary>
    public static class ArtScale
    {
        const string File = "user://art_scale.json";
        static Dictionary<string, float> _map;

        static Dictionary<string, float> Map
        {
            get
            {
                if (_map != null) return _map;
                _map = new();
                if (FileAccess.FileExists(File) && Json.ParseString(FileAccess.GetFileAsString(File)).AsGodotDictionary() is { } d)
                    foreach (var kv in d) _map[kv.Key.AsString()] = (float)kv.Value.AsDouble();
                return _map;
            }
        }

        /// <summary>Ключ объекта: путь его картинки (поле _tex), иначе — тип узла.</summary>
        public static string KeyOf(Node n)
        {
            var f = n.GetType().GetField("_tex", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (f?.GetValue(n) is Texture2D t && !string.IsNullOrEmpty(t.ResourcePath)) return t.ResourcePath;
            return n.GetType().Name;
        }

        public static void Apply(Node2D n)
        {
            if (Map.Count > 0 && Map.TryGetValue(KeyOf(n), out float k)) n.Scale = Vector2.One * k;
        }

        public static float Get(string key) => Map.TryGetValue(key, out float k) ? k : 1f;

        public static void Set(SceneTree tree, string key, float k)
        {
            if (Mathf.Abs(k - 1f) < 0.001f) Map.Remove(key); else Map[key] = k;
            var d = new Godot.Collections.Dictionary();
            foreach (var kv in Map) d[kv.Key] = kv.Value;
            using (var fa = FileAccess.Open(File, FileAccess.ModeFlags.Write)) fa?.StoreString(Json.Stringify(d, "  "));
            foreach (var n in tree.GetNodesInGroup(Shade.Group))
                if (n is Node2D n2 && KeyOf(n2) == key) n2.Scale = Vector2.One * k;
        }
    }
}
