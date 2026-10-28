using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Летопись вайпа: файл JSON Lines на каждый запуск мира. Раз в 5 с мира — где каждый игрок и бот, HP, цель и дело;
    /// события — смерти (кто кого), взрывы, снос построек, вехи, мысли ботов, механики РТ. Разбор — scripts/wipe-report.py
    /// (карта маршрутов, посещения РТ, «тусовки», застревания, беготня по кругу).
    /// </summary>
    public sealed class Chronicle : IDisposable
    {
        public const float SampleEvery = 5f;
        readonly StreamWriter _w;
        float _nextSample, _nextEcon;
        int _milestones;
        readonly Dictionary<int, string> _lastThought = new();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public Chronicle(string path, World w)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _w = new StreamWriter(path, false, new UTF8Encoding(false));
            var sb = new StringBuilder("{\"type\":\"header\",\"seed\":").Append(w.Seed).Append(",\"half\":").Append(F(World.HalfSize))
                .Append(",\"started\":\"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("\",\"monuments\":[");
            for (int i = 0; i < w.Monuments.Count; i++)
            {
                var m = w.Monuments[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(S(m.Name)).Append(",\"min\":[").Append(F(m.Min.X)).Append(',').Append(F(m.Min.Y))
                  .Append("],\"max\":[").Append(F(m.Max.X)).Append(',').Append(F(m.Max.Y)).Append("]}");
            }
            sb.Append("]}");
            _w.WriteLine(sb.ToString());
            _w.Flush();
        }

        static string F(float v) => v.ToString("0.#", Inv);
        static string S(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"";

        void Line(float t, string type, string rest) => _w.WriteLine($"{{\"t\":{F(t)},\"type\":\"{type}\"{(rest.Length > 0 ? "," + rest : "")}}}");

        /// <summary>После каждого тика мира.</summary>
        public void Record(World w, IReadOnlyDictionary<int, PlayerBot> bots)
        {
            float t = w.Time;
            foreach (var e in w.Events)
            {
                switch (e.Kind)
                {
                    case SimEventKind.Death:
                    {
                        var v = w.Find(e.ActorId); var k = e.OtherId != 0 ? w.Find(e.OtherId) : null;
                        Line(t, "death", $"\"victim\":{S(v?.Name)},\"killer\":{S(k?.Name)},\"weapon\":{S(k?.Held?.Id)},\"x\":{F(e.Pos.X)},\"y\":{F(e.Pos.Y)},\"place\":{S(w.MonumentAt(e.Pos)?.Name)}");
                        break;
                    }
                    case SimEventKind.Explosion:
                        Line(t, "explosion", $"\"by\":{S(w.Find(e.ActorId)?.Name)},\"what\":{S(e.Text)},\"x\":{F(e.Pos.X)},\"y\":{F(e.Pos.Y)}");
                        break;
                    case SimEventKind.Destroyed:
                        Line(t, "destroyed", $"\"by\":{S(w.Find(e.ActorId)?.Name)},\"what\":{S(e.Text)},\"x\":{F(e.Pos.X)},\"y\":{F(e.Pos.Y)}");
                        break;
                    case SimEventKind.Machine:
                        Line(t, "rt", $"\"by\":{S(w.Find(e.ActorId)?.Name)},\"what\":{S(e.Text)},\"x\":{F(e.Pos.X)},\"y\":{F(e.Pos.Y)},\"place\":{S(w.MonumentAt(e.Pos)?.Name)}");
                        break;
                }
            }
            for (; _milestones < w.Milestones.Count; _milestones++) Line(t, "milestone", $"\"text\":{S(w.Milestones[_milestones])}");
            foreach (var (id, b) in bots)
            {
                string th = b.LastThought;
                if (string.IsNullOrEmpty(th) || (_lastThought.TryGetValue(id, out var old) && old == th)) continue;
                _lastThought[id] = th;
                Line(t, "thought", $"\"who\":{S(b.Me.Name)},\"text\":{S(th)},\"x\":{F(b.Me.Pos.X)},\"y\":{F(b.Me.Pos.Y)}");
            }

            if (t < _nextSample) return;
            _nextSample = t + SampleEvery;
            // доход лома по источникам (D49, Economy.Ledger) — накопительно, раз в 10 мин мира
            if (t >= _nextEcon)
            {
                _nextEcon = t + 600;
                var eb = new StringBuilder("\"ledger\":{");
                bool efirst = true;
                foreach (var (k, v) in Economy.Snapshot()) { if (!efirst) eb.Append(','); efirst = false; eb.Append(S(k)).Append(':').Append(F((float)v)); }
                Line(t, "econ", eb.Append('}').ToString());
                // поток предметов (scripts/item-flow.py) и что лежит у ботов: в карманах и в своих ящиках
                var (fi, fo) = Economy.FlowSnapshot();
                var held = new Dictionary<string, double>();
                void Hold(ItemSlot s) { if (s != null && s.Count > 0) held[s.Id] = held.GetValueOrDefault(s.Id) + s.Count; }
                foreach (var a in w.Actors)
                {
                    if (a.Spectator || a.Team != Team.Player) continue;
                    foreach (var s in a.Belt) Hold(s);
                    foreach (var s in a.Pack) Hold(s);
                }
                foreach (var d in w.Deployables) if (d.Storage != null && d.OwnerId != 0 && d.Kind != DeployKind.Recycler) foreach (var s in d.Storage.Items) Hold(s);
                string J(Dictionary<string, double> m) { var b = new StringBuilder("{"); bool f1 = true; foreach (var (k, v) in m) { if (!f1) b.Append(','); f1 = false; b.Append(S(k)).Append(':').Append(F((float)v)); } return b.Append('}').ToString(); }
                Line(t, "items", $"\"in\":{J(fi)},\"out\":{J(fo)},\"held\":{J(held)}");
            }
            var sb = new StringBuilder("\"actors\":[");
            bool first = true;
            foreach (var a in w.Actors)
            {
                if (a.Spectator || a.Team != Team.Player) continue;
                bots.TryGetValue(a.Id, out var b);
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":").Append(a.Id).Append(",\"name\":").Append(S(a.Name)).Append(",\"bot\":").Append(a.IsBot ? "true" : "false")
                  .Append(",\"x\":").Append(F(a.Pos.X)).Append(",\"y\":").Append(F(a.Pos.Y)).Append(",\"hp\":").Append(F(a.Hp))
                  .Append(",\"dead\":").Append(a.Dead ? "true" : "false").Append(",\"held\":").Append(S(a.Held?.Id))
                  .Append(",\"goal\":").Append(S(b?.GoalName)).Append(",\"doing\":").Append(S(b?.Doing))
                  .Append(",\"place\":").Append(S(w.MonumentAt(a.Pos)?.Name)).Append(",\"scrap\":").Append(a.Count("scrap")).Append('}');
            }
            sb.Append(']');
            Line(t, "pos", sb.ToString());
            _w.Flush();
        }

        public void Dispose() { _w.Flush(); _w.Dispose(); }
    }
}
