using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Профиль симуляции по частям (docs/bot-sandbox-journal.md, «Производительность»): мозги ботов (восприятие, решение, A*),
    /// шаг мира (персонажи, раздвижка, пули, остальное), счётчики дорогих проверок (обзор, проходимость).
    /// Секундомер — Stopwatch.GetTimestamp (≈20 нс), поэтому включён всегда; отчёт — строкой «ПРОФИЛЬ» в прогонах.
    /// </summary>
    public static class SimPerf
    {
        sealed class Acc { public long Ticks, Calls, Max; }
        static readonly Dictionary<string, Acc> _acc = new();
        static readonly Dictionary<string, long> _cnt = new();
        static int _ticks;

        public static long Begin() => Stopwatch.GetTimestamp();

        public static void End(string key, long t0)
        {
            long dt = Stopwatch.GetTimestamp() - t0;
            if (!_acc.TryGetValue(key, out var a)) _acc[key] = a = new Acc();
            a.Ticks += dt; a.Calls++;
            if (dt > a.Max) a.Max = dt;
        }

        public static void Count(string key, long n = 1)
        {
            _cnt.TryGetValue(key, out long v);
            _cnt[key] = v + n;
        }

        public static void TickDone() => _ticks++;

        public static void Reset() { _acc.Clear(); _cnt.Clear(); _ticks = 0; }

        /// <summary>«ПРОФИЛЬ за N тиков: ключ — мс/тик (вызовов/тик, худший вызов мс) …; счётчики/тик; память».</summary>
        public static string Report()
        {
            int n = Math.Max(1, _ticks);
            double ms = 1000.0 / Stopwatch.Frequency;
            var sb = new StringBuilder($"ПРОФИЛЬ за {_ticks} тиков:");
            var keys = new List<string>(_acc.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                var a = _acc[k];
                sb.Append($" | {k} {a.Ticks * ms / n:0.000} мс/тик ({(double)a.Calls / n:0.##}/тик, макс {a.Max * ms:0.0} мс)");
            }
            var ck = new List<string>(_cnt.Keys);
            ck.Sort(StringComparer.Ordinal);
            foreach (var k in ck) sb.Append($" | {k} {(double)_cnt[k] / n:0.#}/тик");
            sb.Append($" | память {GC.GetTotalMemory(false) / (1024 * 1024)} МБ, сборок {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
            return sb.ToString();
        }
    }
}
