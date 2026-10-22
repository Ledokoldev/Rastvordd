using System;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Токарный станок ботами (D46, раздел 10 bot-brain.md): на ствол не хватает деталей, которые станок точит из сырья
    /// (труба из металла, изолента из ткани, шестерни, УСМ, затвор, нарезной ствол), — поставить станок у дома (верстак 2),
    /// положить сырьё по рецепту (World.BotLathePut), потом забрать готовое (World.BotLatheTake). Не вышло поставить — пауза 10 мин.
    /// </summary>
    public sealed partial class PlayerBot
    {
        // дорогие детали первыми: иначе станок съедал пружины на лишние УСМ, а на затвор и газоотвод к АКМ их не оставалось
        static readonly string[] LatheParts = { "comp_gas_block", "comp_barrel_chrome", "comp_bolt_carrier", "comp_barrel_rifled", "comp_trigger", "comp_gears", "comp_pipe", "comp_tape" };
        float _latheBlock = -999, _latheTry = -1;
        static readonly Vector2[] LatheSpots = { new(3.0f, 5.6f), new(3.0f, -1.8f), new(-1.8f, 2.0f), new(7.8f, 2.0f), new(3.0f, 9.0f), new(-2.5f, 7.0f) };

        Deployable MyLathe()
        {
            var c = MyClaim();
            if (c == null) return null;
            foreach (var d in _w.Deployables) if (d.Kind == DeployKind.Lathe && d.OwnerId == _me.Id && Vector2.Distance(d.Pos, c.Pos) < 40) return d;
            return null;
        }

        /// <summary>Какую деталь точить: нужна на ствол и сырьё на неё есть (при себе и в ящике).</summary>
        string LatheTarget()
        {
            if (_partsWant.Count == 0 || _w.Time - _partsWantAt > 60) return null;
            foreach (var id in LatheParts)
            {
                if (!_partsWant.ContainsKey(id)) continue;
                var r = Weaponry.LatheFor(id);
                if (r == null || (r.Bench3 && (MyBenchLevel() < 3 || (MyLathe() is Deployable lt && _w.BenchLevel(lt.Pos, World.LatheBench3Range) < 3)))) continue;   // хромированный ствол и газоотвод — станок у верстака 3
                if (CanLathe(r)) return id;
                // не хватает промежуточной детали, которую тоже точит станок (газоотвод ← УСМ, затвор ← шестерни) — сначала её
                foreach (var (inId, n) in r.In)
                    if (Stock(inId) < n && Weaponry.LatheFor(inId) is Weaponry.LatheRecipe sub && !sub.Bench3 && CanLathe(sub)) return inId;
            }
            return null;
        }

        bool CanLathe(Weaponry.LatheRecipe r)
        {
            foreach (var (inId, n) in r.In) if (Stock(inId) < n + (inId == "metal_fragments" ? 50 : 0)) return false;
            return true;
        }

        /// <summary>true — занят станком в этот тик.</summary>
        bool LatheWork(Deployable box, ref InputCmd cmd)
        {
            if (_w.Time < _latheBlock || MyBenchLevel() < 2) return false;
            var lathe = MyLathe();
            // готовое — забрать
            if (lathe != null && !lathe.On && lathe.Storage.Items.Count > 0)
            {
                Doing = "забирает детали со станка";
                if (!Reach(lathe.Pos, ref cmd, 1.2f)) return true;
                int got = _w.BotLatheTake(_me, lathe);
                if (got > 0) { Thought($"забрал со станка {got} дет."); LStat("станок: деталей", got); }
                else _latheBlock = _w.Time + 120;
                return true;
            }
            string want = LatheTarget();
            if (want == null || (lathe != null && lathe.On)) return false;
            if (lathe == null)
            {
                // станок: металл 250, лом 50, шестерни 2 (верстак 2) — только если всё это уже есть, ради станка не фармим
                if (!Has("deploy_lathe"))
                {
                    if (Stock("metal_fragments") < 300 || Stock("scrap") < 50 || Stock("comp_gears") < 2 + (want == "comp_gears" ? 0 : 0)) return false;
                    Doing = "делает токарный станок";
                    return CraftAtBench("deploy_lathe", box, ref cmd);
                }
                if (_latheTry < 0) _latheTry = _w.Time;
                if (_w.Time - _latheTry > 150) { _latheBlock = _w.Time + 600; _latheTry = -1; Thought("станок поставить негде — позже"); return false; }
                // места по очереди (25 с на каждое): у южной стены кибитки, у северной, западной, восточной, дальше на юг —
                // раньше было одно место, и когда его занимала стройка мейна, станок не вставал никогда (D49)
                var spot = AtPlan(LatheSpots[(int)((_w.Time - _latheTry) / 25f) % LatheSpots.Length]);
                cmd.Aim = spot;
                Doing = "ставит токарный станок";
                if (!Reach(spot, ref cmd, 1.1f)) return true;
                if (Hold("deploy_lathe", ref cmd)) Click(ref cmd);
                return true;
            }
            _latheTry = -1;
            var rec = Weaponry.LatheFor(want);
            foreach (var (id, n) in rec.In)
                if (_me.Count(id) < n) { if (TakeFromBox(box, id, n, ref cmd)) return true; return false; }
            Doing = $"точит на станке: {Items.Name(want)}";
            if (!Reach(lathe.Pos, ref cmd, 1.2f)) return true;
            if (_w.BotLathePut(_me, lathe, want)) { Thought($"на ствол не хватает «{Items.Name(want)}» — точу на станке"); LStat("станок: заказ"); }
            else _latheBlock = _w.Time + 120;
            return true;
        }
    }
}
