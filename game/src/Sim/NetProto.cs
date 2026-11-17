using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Сетевой протокол (D73) без Godot: кодирование ввода и снимков. Транспорт (TCP, NetLink) — в клиентской части.
    /// Снимок шлётся каждый тик надёжным упорядоченным каналом, но только изменившиеся поля
    /// (поле Snapshot или SelfInfo целиком), сжатый Brotli. Первый снимок — полный.
    /// </summary>
    public static class NetProto
    {
        public const int Version = 1;
        public const int DefaultPort = 7777;
        public const int MaxPlayers = 8;

        // типы сообщений: первый байт пакета
        public const byte Hello = 1;     // клиент → сервер: версия, имя
        public const byte Welcome = 2;   // сервер → клиент: id персонажа, зерно мира, отпечаток карты
        public const byte Snap = 3;      // сервер → клиент: снимок (дельта)
        public const byte Input = 4;     // клиент → сервер: InputCmd
        public const byte Reject = 5;    // сервер → клиент: отказ (текст)
        public const byte Ready = 6;     // клиент → сервер: карта построена — слать снимки (до этого копились бы минуты устаревшего мира)

        public sealed class HelloMsg { public int Version; public string Name; }
        public sealed class WelcomeMsg { public int Version; public int Id; public int Seed; public bool NoScatter; public long MapHash; }

        public static readonly JsonSerializerOptions Opts = new()
        {
            IncludeFields = true,
            IgnoreReadOnlyProperties = true,   // StructSnap.Center/Poly — считаются на месте
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        static readonly JsonSerializerOptions InputOpts = new(Opts) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault };

        public static byte[] Pack(byte type, string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var b = new byte[body.Length + 1];
            b[0] = type;
            Buffer.BlockCopy(body, 0, b, 1, body.Length);
            return b;
        }
        public static string Text(byte[] pkt) => Encoding.UTF8.GetString(pkt, 1, pkt.Length - 1);

        public static byte[] EncodeHello(string name) => Pack(Hello, JsonSerializer.Serialize(new HelloMsg { Version = Version, Name = name }, Opts));
        public static byte[] EncodeWelcome(WelcomeMsg m) => Pack(Welcome, JsonSerializer.Serialize(m, Opts));
        public static byte[] EncodeInput(InputCmd c) => Pack(Input, JsonSerializer.Serialize(c, InputOpts));
        public static T Decode<T>(byte[] pkt) => JsonSerializer.Deserialize<T>(Text(pkt), Opts);
        public static InputCmd DecodeInput(byte[] pkt) => JsonSerializer.Deserialize<InputCmd>(Text(pkt), InputOpts);

        /// <summary>Отпечаток карты: клиент строит мир сам по зерну и сверяет, что вышло то же.</summary>
        public static long MapHash(World w)
        {
            unchecked
            {
                long h = 1469598103934665603L;
                void Mix(long v) { h ^= v; h *= 1099511628211L; }
                Mix(w.Terrain.N);
                for (int i = 0; i < w.Terrain.Height.Length; i += 97) Mix((long)(w.Terrain.Height[i] * 100));
                foreach (var m in w.Monuments) { Mix(m.Name.GetHashCode(StringComparison.Ordinal) & 0xffff); Mix((long)m.Min.X); Mix((long)m.Min.Y); }
                Mix(w.Decors.Count);
                Mix(w.Prefabs.Count);
                return h;
            }
        }

        static readonly FieldInfo[] SnapFields = typeof(Snapshot).GetFields(BindingFlags.Public | BindingFlags.Instance);
        static readonly FieldInfo[] SelfFields = typeof(SelfInfo).GetFields(BindingFlags.Public | BindingFlags.Instance);

        /// <summary>Кодировщик снимков для одного клиента (помнит, что уже отправил).</summary>
        public sealed class SnapEncoder
        {
            readonly Dictionary<string, string> _last = new();

            public byte[] Encode(Snapshot s)
            {
                var sb = new StringBuilder(4096);
                sb.Append('{');
                bool first = true;
                void Put(string key, object v, Type t, bool always)
                {
                    string j = JsonSerializer.Serialize(v, t, Opts);
                    if (!always && _last.TryGetValue(key, out var old) && old == j) return;
                    _last[key] = j;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(key).Append("\":").Append(j);
                }
                foreach (var f in SnapFields)
                {
                    if (f.Name == nameof(Snapshot.Self))
                    {
                        if (s.Self == null) continue;
                        foreach (var sf in SelfFields) Put("Self." + sf.Name, sf.GetValue(s.Self), sf.FieldType, false);
                        continue;
                    }
                    Put(f.Name, f.GetValue(s), f.FieldType, f.Name is nameof(Snapshot.Events) or nameof(Snapshot.Tick));
                }
                sb.Append('}');
                var raw = Encoding.UTF8.GetBytes(sb.ToString());
                using var ms = new MemoryStream();
                ms.WriteByte(Snap);
                using (var bz = new BrotliStream(ms, CompressionLevel.Fastest, true)) bz.Write(raw, 0, raw.Length);
                return ms.ToArray();
            }
        }

        /// <summary>Декодер снимков: собирает полный Snapshot из дельт (по порядку).</summary>
        public sealed class SnapDecoder
        {
            static readonly Dictionary<string, FieldInfo> SnapByName = new(), SelfByName = new();
            static SnapDecoder()
            {
                foreach (var f in SnapFields) SnapByName[f.Name] = f;
                foreach (var f in SelfFields) SelfByName[f.Name] = f;
            }

            Snapshot _cur;
            public int LastBytes;

            public Snapshot Decode(byte[] pkt)
            {
                LastBytes = pkt.Length;
                string json;
                using (var ms = new MemoryStream(pkt, 1, pkt.Length - 1))
                using (var bz = new BrotliStream(ms, CompressionMode.Decompress))
                using (var rd = new StreamReader(bz, Encoding.UTF8)) json = rd.ReadToEnd();
                // новый объект: неизменные поля — ссылками на прежние (клиент держит прошлый снимок для сглаживания)
                var s = new Snapshot();
                var self = new SelfInfo();
                if (_cur != null)
                {
                    foreach (var f in SnapFields) f.SetValue(s, f.GetValue(_cur));
                    if (_cur.Self != null) foreach (var f in SelfFields) f.SetValue(self, f.GetValue(_cur.Self));
                }
                s.Self = self;
                s.Events = new List<SimEvent>();
                using var doc = JsonDocument.Parse(json);
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (p.Name.StartsWith("Self."))
                    {
                        if (SelfByName.TryGetValue(p.Name.Substring(5), out var sf)) sf.SetValue(self, p.Value.Deserialize(sf.FieldType, Opts));
                    }
                    else if (SnapByName.TryGetValue(p.Name, out var f)) f.SetValue(s, p.Value.Deserialize(f.FieldType, Opts));
                }
                _cur = s;
                return s;
            }
        }
    }
}
