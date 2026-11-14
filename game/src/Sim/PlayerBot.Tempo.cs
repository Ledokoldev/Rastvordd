using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Мозг v4: темп и затишье (D65; пользователь: «как старт вайпа в Расте — все рашат», «бот не должен всё время заниматься
    /// хозяйством — строить в затишье», «рисковать, а не ждать 100% безопасности», «скорость решает»).
    /// Темп (0..1) — давление прогресса: начало вайпа и отставание от сервера. Затишье — рядом нет боя, шума и событий.
    /// Хозяйство дешевеет вне затишья и при высоком темпе; выходы (РТ, слух, роум, сбросы) — дорожают.
    /// Против «вечного фарма»: дома больше половины последних 10 минут — хозяйство ещё дешевле.
    /// </summary>
    public sealed partial class PlayerBot
    {
        /// <summary>Прогресс каждого бота (верстак + ствол) — общий «темп сервера», как в Расте видно, кто уже со стволами.</summary>
        static readonly Dictionary<int, float> _progress = new();

        float _tempo, _tempoAt = -99, _houseShare;
        bool _lull = true;
        string _tempoWhy = "";
        readonly Queue<bool> _houseWin = new();   // раз в 10 с: было ли хозяйство (окно 10 мин)
        float _houseWinAt = -99;
        bool _farmStuckSaid;

        public float Tempo => _tempo;
        public bool Lull => _lull;

        float MyProgress() => MyBenchLevel() + 2 * MyPower;

        void StepTempo()
        {
            if (_w.Time - _tempoAt < 2f) return;
            _tempoAt = _w.Time;
            float mine = MyProgress();
            _progress[_me.Id] = mine;
            float sum = 0; int n = 0;
            foreach (var (id, p) in _progress) if (id != _me.Id) { sum += p; n++; }
            float behind = n > 0 ? Math.Clamp((sum / n - mine) / 1.5f, 0, 1) : 0;
            float early = Math.Clamp(1 - (_w.Time - 20 * 60) / (20 * 60f), 0.25f, 1);   // первые 20 мин — рашим, к 40-й — спокойнее
            float t = MathF.Max(early, behind) * (0.7f + 0.3f * Aggression + 0.15f * Greed);
            t = Math.Clamp(t, 0, 1);
            string why = early >= behind ? "начало вайпа" : $"отстаю от сервера ({mine:0.0} против {sum / Math.Max(1, n):0.0})";
            if (MathF.Abs(t - _tempo) > 0.25f) Thought($"темп {t:0.00}: {why}");
            _tempo = t; _tempoWhy = why;

            // затишье: 60 с без боя и выстрелов рядом, без угрозы; аирдроп/слух рядом — не затишье
            bool loud = _w.Time - _lastDanger < 60 || _w.Time - _foeSeenAt < 45;
            foreach (var h in _heard) if (_w.Time - h.Time < 60 && Vector2.Distance(h.Pos, _me.Pos) < 220) { loud = true; break; }
            if (!loud && _w.Director != null)
                foreach (var r in _w.Director.Rumors)
                    if (r.Until > _w.Time && r.Kind == "airdrop" && Vector2.Distance(r.Pos, _me.Pos) < 350) { loud = true; break; }
            _lull = !loud;

            // окно «сколько сижу дома»
            if (_w.Time - _houseWinAt >= 10)
            {
                _houseWinAt = _w.Time;
                _houseWin.Enqueue(_goal?.Name == "хозяйство");
                while (_houseWin.Count > 60) _houseWin.Dequeue();
                int k = 0; foreach (var b in _houseWin) if (b) k++;
                _houseShare = _houseWin.Count >= 12 ? (float)k / _houseWin.Count : 0;
                if (_houseShare > 0.5f && !_farmStuckSaid) { _farmStuckSaid = true; Thought($"засиделся дома ({_houseShare:P0} последних минут) — пора на движ"); LStat("темп: засиделся дома"); }
                else if (_houseShare < 0.35f) _farmStuckSaid = false;
            }
        }

        /// <summary>Множитель хозяйства: строю в затишье; базовое (шкаф, ящик) — всегда.</summary>
        float HouseMul()
        {
            if (MyClaim() == null || MyBox() == null) return 1f;
            float m = (_lull ? 1f : 0.55f) * (1 - 0.35f * _tempo);
            if (_houseShare > 0.5f) m *= 0.6f;
            return m;
        }

        /// <summary>Множитель выходов (РТ, слух, роум, сбросы): темп толкает наружу.</summary>
        float OutMul() => 1 + 0.45f * _tempo + (_houseShare > 0.5f ? 0.2f : 0);
    }
}
