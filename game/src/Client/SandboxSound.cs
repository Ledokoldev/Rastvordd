using System.Collections.Generic;
using System.Linq;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>
    /// Полигон звука (--sandbox=sound): всё играет настоящим путём (Sfx.Play → Acoustics: задержка, линейное затухание
    /// до края слышимости, окклюзия, дальний слой, Steam HRTF, если включён). На панели и в логе — что посчитала акустика.
    /// Викторина «Угадай направление»: звук один раз из случайного сектора (36 × 10°), ответ — направление прицела.
    /// Направления — как в акустике: «перед» — вверх по экрану, по часовой.
    /// </summary>
    public partial class Main
    {
        static readonly (string name, string id)[] SnGuns =
        {
            ("Пистолет", "weapon_grach"), ("ПП", "weapon_pps43"), ("Дробовик", "weapon_mp133"), ("Автомат", "weapon_akm"),
            ("Винтовка", "weapon_svt40"), ("Снайперка", "weapon_svd"), ("Лук", "weapon_bow"),
        };
        static readonly float[] SnDists = { 10, 50, 100, 200, 300 };
        static readonly string[] SnDirNames = { "перед", "перед-право", "право", "зад-право", "зад", "зад-лево", "лево", "перед-лево" };
        static readonly float[] SnRadii = { 5, 10, 20, 30, 45 };
        int _snGun = 3, _snDist = 1, _snDir;
        bool _snWall, _snRepeat;
        float _snEvery = 3, _snRepeatT;
        System.Action _snLast;
        string _snPickId = "explosion_small";
        readonly List<string> _snRows = new();
        // призрак: ходит по кругу и шагает
        bool _phOn;
        int _phGait, _phR = 1;
        float _phAng, _phStepT;
        SVec _phPos;
        int _phSteps;
        // отложенные звуки (шаги викторины)
        readonly List<(float at, System.Action play)> _snPending = new();
        float _snClock;

        /// <summary>Вектор «направление az (° по часовой от верха экрана) × расстояние».</summary>
        static SVec Az(float azDeg, float d) { float r = Mathf.DegToRad(azDeg); return new SVec(Mathf.Sin(r), -Mathf.Cos(r)) * d; }
        static float AzOf(SVec v) => (Mathf.RadToDeg(Mathf.Atan2(v.X, -v.Y)) + 360f) % 360f;
        static float AngErr(float a, float b) { float d = Mathf.Abs(a - b) % 360f; return d > 180 ? 360 - d : d; }

        SVec SnMe() => _server.World.Find(_myId).Pos;

        /// <summary>Сыграть звук в точке и записать строку: id, расстояние, слышимость, громкость (как посчитала Acoustics).</summary>
        void SnPlay(string label, System.Func<string> play, bool wall, bool log = true)
        {
            Acoustics.ForceOcclusion = wall;
            Acoustics.Last = default;
            string id;
            try { id = play(); }
            finally { Acoustics.ForceOcclusion = false; }
            var L = Acoustics.Last;
            string row = Sfx.Get(id) == null ? $"{id}: нет файла — тихо"
                : !L.Heard ? $"{id} · {label} · {L.Dist:0} м · слышно до {L.HearM:0} м → НЕ СЛЫШНО (дальше края)"
                : $"{id} · {label} · {L.Dist:0} м / {L.HearM:0} м · {L.Db:0.0} дБ{(L.Occluded ? " · за стеной" : "")}{(L.FarMix > 0.02f ? $" · дальний слой {L.FarMix * 100:0}%" : "")} · задержка {L.DelayS * 1000:0} мс{(L.Hrtf ? " · HRTF" : "")}";
            if (!log) return;
            _snRows.Insert(0, row);
            if (_snRows.Count > 5) _snRows.RemoveAt(5);
            GD.Print("SANDBOX-SOUND " + row);
        }

        void SnShot(int gun, float dist, int dir, bool wall)
        {
            var (name, id) = SnGuns[gun];
            var pos = SnMe() + Az(dir * 45, dist);
            SnPlay($"{name} {SnDirNames[dir]}", () => PlayShotSound(id, pos), wall);
        }

        void SnExplosion(bool big, float dist, int dir, bool wall)
        {
            var pos = SnMe() + Az(dir * 45, dist);
            SnPlay($"взрыв {SnDirNames[dir]}", () => { string s = big ? "explosion_big" : "explosion_small"; Sfx.Play(s, Iso.Px(pos), 4, 300, 0.06f); return s; }, wall);
        }

        void SnById(string id, float dist, int dir, bool wall)
        {
            id = (id ?? "").Trim();
            if (id == "") return;
            var pos = SnMe() + Az(dir * 45, dist);
            SnPlay($"{SnDirNames[dir]}", () => { Sfx.Play(id, Iso.Px(pos), 0, 60, 0.03f); return id; }, wall);
        }

        /// <summary>Один шаг в точке — как шаги персонажей (Main.Footstep): поверхность под ногами, громкость и слышимость по походке.</summary>
        string SnStep(SVec pos, int gait, bool log)
        {
            bool run = gait == 1, crouch = gait == 2;
            float vol = -2 + (run ? 3 : 0) - (crouch ? 6 : 0);
            float hear = crouch ? Footsteps.HearCrouch : run ? Footsteps.HearRun : Footsteps.HearWalk;
            string id = null;
            SnPlay($"шаг {(run ? "бег" : crouch ? "присед" : "ходьба")} ({SurfaceAt(pos)})", () =>
            {
                id = Footsteps.Play(SurfaceAt(pos), Iso.Px(pos), vol, hear) ?? "steps/?";
                return id;
            }, _snWall && !_quizOn || _quizOn && _quiz != null && _quiz.Wall, log);
            return id;
        }

        static readonly string[] AllSoundIds = ListSounds();
        static string[] ListSounds()
        {
            var ids = new SortedSet<string>();
            foreach (var (dir, ext, prefix) in new[] { ("res://audio/real", ".mp3", ""), ("res://audio", ".wav", ""), ("res://audio/steps", ".wav", "steps/") })
            {
                using var d = DirAccess.Open(dir);
                if (d == null) continue;
                foreach (var f in d.GetFiles())
                {
                    var n = f.Replace(".import", "").Replace(".remap", "");
                    if (n.EndsWith(ext)) ids.Add(prefix + n[..^ext.Length]);
                }
            }
            return ids.ToArray();
        }

        void SbFillSound()
        {
            var p = _sbPanel;
            p.Caption("Где звучит: дистанция и направление (перед — вверх по экрану)");
            p.Choice(System.Array.ConvertAll(SnDists, d => $"{d:0} м"), _snDist, k => _snDist = k);
            p.Choice(SnDirNames, _snDir, k => _snDir = k);
            p.Toggle("За стеной (окклюзия)", _snWall, v => _snWall = v);
            p.Caption("Выстрел");
            var shots = new List<(string, System.Action)>();
            for (int i = 0; i < SnGuns.Length; i++)
            {
                int k = i;
                shots.Add(($"{SnGuns[k].name} ({Weaponry.ShotHearM(SnGuns[k].id):0} м)", () => { _snGun = k; _snLast = () => SnShot(k, SnDists[_snDist], _snDir, _snWall); _snLast(); }));
            }
            p.Buttons(shots.ToArray());
            p.Buttons(("Взрыв малый", () => { _snLast = () => SnExplosion(false, SnDists[_snDist], _snDir, _snWall); _snLast(); }),
                      ("Взрыв C4", () => { _snLast = () => SnExplosion(true, SnDists[_snDist], _snDir, _snWall); _snLast(); }));
            p.Caption("Звук по id (из game/audio)");
            p.Options(AllSoundIds, id => _snPickId = id);
            p.Buttons(("Сыграть выбранный", () => { var id = _snPickId; _snLast = () => SnById(id, SnDists[_snDist], _snDir, _snWall); _snLast(); }));
            p.Input("или введи id: shot_akm, hit_head…", "Сыграть", id => { _snLast = () => SnById(id, SnDists[_snDist], _snDir, _snWall); _snLast(); });
            p.Caption("Повтор");
            p.Toggle("Повторять последний звук", _snRepeat, v => { _snRepeat = v; _snRepeatT = 0; });
            p.Choice(new[] { "каждые 1 с", "2 с", "3 с", "5 с" }, _snEvery switch { 1 => 0, 2 => 1, 5 => 3, _ => 2 }, k => _snEvery = k switch { 0 => 1, 1 => 2, 3 => 5, _ => 3 });
            p.Caption("Призрак: шаги по кругу вокруг тебя");
            p.Choice(new[] { "Ходьба", "Бег", "Присед" }, _phGait, k => _phGait = k);
            p.Choice(System.Array.ConvertAll(SnRadii, r => $"R {r:0} м"), _phR, k => _phR = k);
            p.Toggle("Призрак ходит", _phOn, v => { _phOn = v; _phSteps = 0; if (v) _quizOn = false; });
            p.Caption("Угадай направление");
            p.Note("Звук один раз из случайного направления (36 секторов по 10°). Повернись прицелом туда, откуда слышал, и нажми ЛКМ или пробел. Пробел без вопроса — следующий раунд.");
            p.Choice(new[] { "Любой звук", "Выстрелы", "Шаги", "Только выбранный ствол" }, _qType, k => _qType = k);
            p.Choice(new[] { "Дистанция: любая", "Ближе", "Средне", "Далеко" }, _qBand, k => _qBand = k);
            p.Toggle("Звук за стеной", _qWall, v => _qWall = v);
            p.Toggle("Сам следующий раунд", _qAuto, v => _qAuto = v);
            p.Buttons(("Начать / следующий", QuizNext), ("Повторить звук (подсказка)", QuizReplay));
            p.Buttons(("Сбросить статистику", QuizReset), ("Итоги в лог", () => QuizSummary(true)));
        }

        void SoundTick(World w, Actor me)
        {
            _snClock += World.Dt;
            for (int i = _snPending.Count - 1; i >= 0; i--)
                if (_snPending[i].at <= _snClock) { var a = _snPending[i].play; _snPending.RemoveAt(i); a(); }
            if (me.Dead) { me.Dead = false; me.Hp = me.MaxHp; me.Pos = _sbSpawn; }
            if (_snRepeat && _snLast != null && (_snRepeatT -= World.Dt) <= 0) { _snRepeatT = _snEvery; _snLast(); }
            if (_phOn)
            {
                bool run = _phGait == 1, crouch = _phGait == 2;
                float speed = crouch ? World.WalkSpeed * 0.5f : run ? World.SprintSpeed : World.WalkSpeed;
                float r = SnRadii[_phR];
                _phAng += Mathf.RadToDeg(speed / r) * World.Dt;
                _phPos = me.Pos + Az(_phAng, r);
                if ((_phStepT -= World.Dt) <= 0)
                {
                    _phStepT = 1f / Footsteps.Cadence(speed, crouch);
                    SnStep(_phPos, _phGait, _phSteps++ % 8 == 0);
                }
            }
            if (_quiz != null && _quiz.Answered && _qAuto && _snClock - _quiz.AnsweredAt > 1.8f) QuizNext();
        }

        void SoundFrame(float dt)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"HRTF {(Tuning.SteamHrtf && SteamHrtf.Ready ? "Steam Audio" : "свой (16 направлений)")} · выстрелы слышно: пистолет {Weaponry.ShotHearM("weapon_grach"):0}, ПП {Weaponry.ShotHearM("weapon_pps43"):0}, дробь {Weaponry.ShotHearM("weapon_mp133"):0}, автомат {Weaponry.ShotHearM("weapon_akm"):0}, винтовка {Weaponry.ShotHearM("weapon_svt40"):0}, СВД {Weaponry.ShotHearM("weapon_svd"):0}, лук {Weaponry.ShotHearM("weapon_bow"):0} м\n");
            sb.Append($"Шаги слышно: бег {Footsteps.HearRun:0}, ходьба {Footsteps.HearWalk:0}, присед {Footsteps.HearCrouch:0} м{(_phOn ? $" · призрак: {(_phGait == 1 ? "бег" : _phGait == 2 ? "присед" : "ходьба")}, R {SnRadii[_phR]:0} м" : "")}{(_snRepeat ? $" · повтор каждые {_snEvery:0} с" : "")}\n");
            foreach (var r in _snRows) sb.Append("  ").Append(r).Append('\n');
            sb.Append(QuizText());
            _sbPanel.Info.Text = sb.ToString().TrimEnd();
            var mePx = _selfPx;
            if (_phOn) _sbMarks.Phantom(Iso.Px(_phPos));
            _sbMarks.Ring(mePx, 36 * 4, _quizOn ? SectorErrors() : null, _quiz != null && _quiz.Answered ? (AzOf(_quiz.Src - _quiz.AnsweredFrom), _quiz.AnswerAz) : null);
            _sbMarks.Rect(Iso.Px(_sbC + new SVec(-40, -40)), Iso.Px(_sbC + new SVec(40, 40)));
        }

        bool SoundKey(Key k)
        {
            if (k != Key.Space) return false;
            if (_quiz != null && !_quiz.Answered) QuizAnswer(AzOf(_aim - SnMe()));
            else QuizNext();
            return true;
        }

        // ---------- викторина ----------

        sealed class QuizRound
        {
            public string Kind, Type, Id;     // выстрел/шаги; «Автомат»…; id звука
            public float Dist, TrueAz, AnswerAz, Err, AnsweredAt;
            public int Sector, Hints;
            public bool Wall, Answered, FrontBack;
            public SVec Src, AnsweredFrom;
            public string Weapon; public int Gait;
        }
        QuizRound _quiz;
        bool _quizOn;
        int _qType, _qBand;
        bool _qWall, _qAuto;
        readonly List<QuizRound> _qDone = new();

        void QuizNext()
        {
            _quizOn = true; _phOn = false;
            var r = new QuizRound { Sector = _qRng.Next(36), Wall = _qWall };
            r.TrueAz = r.Sector * 10;
            bool steps = _qType == 2 || (_qType == 0 && _qRng.NextDouble() < 0.3);
            float Band(float lo, float hi) { float a = lo, b = hi, t = (b - a) / 3; if (_qBand > 0) { a = lo + t * (_qBand - 1); b = a + t; } return a + (float)_qRng.NextDouble() * (b - a); }
            if (steps)
            {
                r.Kind = "шаги"; r.Gait = _qRng.Next(3);
                r.Type = r.Gait == 1 ? "шаги: бег" : r.Gait == 2 ? "шаги: присед" : "шаги: ходьба";
                float hear = r.Gait == 2 ? Footsteps.HearCrouch : r.Gait == 1 ? Footsteps.HearRun : Footsteps.HearWalk;
                r.Dist = Band(6, System.MathF.Min(40, hear * 0.9f));
            }
            else
            {
                int g = _qType == 3 ? _snGun : _qRng.Next(SnGuns.Length);
                r.Kind = "выстрел"; r.Weapon = SnGuns[g].id; r.Type = SnGuns[g].name;
                float hear = Weaponry.ShotHearM(r.Weapon);
                r.Dist = r.Weapon == "weapon_bow" ? Band(6, 22) : Band(20, System.MathF.Min(200, hear * 0.9f));
            }
            r.Dist = Mathf.Round(r.Dist);
            r.Src = SnMe() + Az(r.TrueAz, r.Dist);
            _quiz = r;
            QuizPlay(r);
            _hud.Toast("Откуда звук? Прицел туда — ЛКМ или пробел");
        }

        void QuizPlay(QuizRound r)
        {
            if (r.Kind == "выстрел") SnPlay($"викторина: {r.Type}", () => r.Id = PlayShotSound(r.Weapon, r.Src), r.Wall, false);
            else
            {
                // 4 шага на месте с лёгким смещением вбок — человек проходит мимо
                float cad = Footsteps.Cadence(r.Gait == 2 ? World.WalkSpeed * 0.5f : r.Gait == 1 ? World.SprintSpeed : World.WalkSpeed, r.Gait == 2);
                var side = Az(r.TrueAz + 90, 1);
                for (int k = 0; k < 4; k++)
                {
                    int kk = k;
                    _snPending.Add((_snClock + kk / cad, () => r.Id = SnStep(r.Src + side * ((kk - 1.5f) * 0.5f), r.Gait, false)));
                }
            }
        }

        void QuizReplay()
        {
            if (_quiz == null) { QuizNext(); return; }
            if (!_quiz.Answered) _quiz.Hints++;
            QuizPlay(_quiz);
        }

        bool QuizClick()
        {
            if (!_quizOn || _quiz == null || _quiz.Answered) return false;
            QuizAnswer(AzOf(_aim - SnMe()));
            return true;
        }

        void QuizAnswer(float az)
        {
            var r = _quiz;
            if (r == null || r.Answered) return;
            r.Answered = true;
            r.AnsweredAt = _snClock;
            r.AnsweredFrom = SnMe();
            r.AnswerAz = az;
            float truth = AzOf(r.Src - r.AnsweredFrom);   // если отошёл — честно от того места, где ответил
            r.Err = AngErr(az, truth);
            r.FrontBack = r.Err > 90;
            _qDone.Add(r);
            GD.Print($"SANDBOX-QUIZ раунд {_qDone.Count}: {r.Type} {r.Dist:0} м{(r.Wall ? " за стеной" : "")}, сектор {r.Sector} (истина {truth:0}°), ответ {az:0}°, ошибка {r.Err:0}°{(r.FrontBack ? " ПЕРЕД/ЗАД" : "")}{(r.Hints > 0 ? $", подсказок {r.Hints}" : "")}");
            _hud.Toast($"Ошибка {r.Err:0}°{(r.FrontBack ? " — перепутал перед и зад" : r.Err <= 10 ? " — точно!" : "")}");
            QuizSummary(false);
            QuizSave();
        }

        void QuizReset() { _qDone.Clear(); _quiz = null; _hud.Toast("Статистика викторины сброшена"); QuizSave(); }

        static float Median(List<float> v)
        {
            if (v.Count == 0) return 0;
            var s = v.OrderBy(x => x).ToList();
            return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
        }

        static string DistBand(float d) => d <= 15 ? "≤15 м" : d <= 40 ? "15–40 м" : d <= 80 ? "40–80 м" : d <= 140 ? "80–140 м" : ">140 м";

        string Line(IEnumerable<QuizRound> rs)
        {
            var e = rs.Select(r => r.Err).ToList();
            if (e.Count == 0) return "—";
            return $"{e.Count} р. · средн. {e.Average():0}° · медиана {Median(e):0}° · ≤10° {100f * e.Count(x => x <= 10) / e.Count:0}% · ≤20° {100f * e.Count(x => x <= 20) / e.Count:0}% · ≤45° {100f * e.Count(x => x <= 45) / e.Count:0}% · перед/зад {rs.Count(r => r.FrontBack)}";
        }

        /// <summary>Средняя ошибка по 36 секторам (−1 — данных нет): для раскраски кольца.</summary>
        float[] SectorErrors()
        {
            var s = new float[36];
            for (int i = 0; i < 36; i++)
            {
                var e = _qDone.Where(r => r.Sector == i).Select(r => r.Err).ToList();
                s[i] = e.Count > 0 ? e.Average() : -1;
            }
            return s;
        }

        string QuizText()
        {
            if (!_quizOn && _qDone.Count == 0) return "";
            var sb = new System.Text.StringBuilder("Угадай направление: ");
            if (_quiz != null && !_quiz.Answered) sb.Append($"раунд {_qDone.Count + 1} — слушай и отвечай (ЛКМ/пробел){(_quiz.Hints > 0 ? $", подсказок {_quiz.Hints}" : "")}\n");
            else if (_quiz != null) sb.Append($"{_quiz.Type} {_quiz.Dist:0} м — ошибка {_quiz.Err:0}°{(_quiz.FrontBack ? " (перед/зад!)" : "")} · пробел — дальше\n");
            else sb.Append("пробел — первый раунд\n");
            sb.Append($"  всего: {Line(_qDone)}\n");
            foreach (var g in _qDone.GroupBy(r => r.Type)) sb.Append($"  {g.Key}: {Line(g)}\n");
            foreach (var g in _qDone.GroupBy(r => DistBand(r.Dist)).OrderBy(g => g.Min(r => r.Dist))) sb.Append($"  {g.Key}: {Line(g)}\n");
            var worst = _qDone.GroupBy(r => r.Sector * 10).Select(g => (az: g.Key, err: g.Average(r => r.Err), n: g.Count())).OrderByDescending(x => x.err).Take(4).ToList();
            if (worst.Count > 0) sb.Append("  хуже всего: " + string.Join(", ", worst.Select(x => $"{x.az}° ({x.err:0}°, {x.n} р.)")) + "\n");
            if (_qDone.Count > 0) sb.Append($"  файл: {ProjectSettings.GlobalizePath(QuizPath)}\n");
            return sb.ToString();
        }

        void QuizSummary(bool full)
        {
            GD.Print($"SANDBOX-QUIZ итог: {Line(_qDone)}");
            if (!full) return;
            foreach (var g in _qDone.GroupBy(r => r.Type)) GD.Print($"SANDBOX-QUIZ   {g.Key}: {Line(g)}");
            foreach (var g in _qDone.GroupBy(r => DistBand(r.Dist))) GD.Print($"SANDBOX-QUIZ   {g.Key}: {Line(g)}");
            foreach (var g in _qDone.GroupBy(r => r.Sector).OrderBy(g => g.Key)) GD.Print($"SANDBOX-QUIZ   сектор {g.Key * 10}°: {Line(g)}");
            GD.Print($"SANDBOX-QUIZ файл: {ProjectSettings.GlobalizePath(QuizPath)}");
        }

        /// <summary>Файл итогов этой сессии: sound_quiz_ГГГГ-ММ-ДД_ЧЧ-мм.json (прошлые сессии не затираются).</summary>
        static readonly string QuizPath = $"user://sandbox/sound_quiz_{System.DateTime.Now:yyyy-MM-dd_HH-mm}.json";
        /// <summary>Свой ГСЧ викторины от времени: раунды каждый раз другие.</summary>
        readonly System.Random _qRng = new();

        /// <summary>Итоги в JSON (user://sandbox/sound_quiz_<дата>.json): раунды и сводки — чтобы разобрать потом.</summary>
        void QuizSave()
        {
            try
            {
                DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://sandbox"));
                object Sum(IEnumerable<QuizRound> rs)
                {
                    var e = rs.Select(r => r.Err).ToList();
                    return new { n = e.Count, mean = e.Count > 0 ? e.Average() : 0, median = Median(e), within10 = e.Count(x => x <= 10), within20 = e.Count(x => x <= 20), within45 = e.Count(x => x <= 45), frontBack = rs.Count(r => r.FrontBack) };
                }
                var data = new
                {
                    saved = System.DateTime.Now.ToString("s"),
                    hrtf = Tuning.SteamHrtf && SteamHrtf.Ready ? "steam" : "own",
                    total = Sum(_qDone),
                    byType = _qDone.GroupBy(r => r.Type).ToDictionary(g => g.Key, g => Sum(g)),
                    byDistance = _qDone.GroupBy(r => DistBand(r.Dist)).ToDictionary(g => g.Key, g => Sum(g)),
                    bySector = _qDone.GroupBy(r => r.Sector * 10).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(), g => Sum(g)),
                    rounds = _qDone.Select(r => new { kind = r.Kind, type = r.Type, id = r.Id, dist = r.Dist, sectorAz = r.TrueAz, answerAz = r.AnswerAz, err = r.Err, frontBack = r.FrontBack, hints = r.Hints, wall = r.Wall }),
                };
                var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(QuizPath), json);
                if (_qDone.Count == 1) GD.Print($"SANDBOX-QUIZ итоги пишутся в {ProjectSettings.GlobalizePath(QuizPath)}");
            }
            catch (System.Exception ex) { GD.PrintErr($"викторина: не записано: {ex.Message}"); }
        }

        /// <summary>Сценарий (--do=N:sb:…): quiz, ans&lt;°&gt;, anstrue&lt;±°&gt;, panel, sub1, bots3, kind1, tier3, god, shot&lt;gun&gt;, steps, summary.</summary>
        void SandboxScript(string a)
        {
            if (a == "panel") _sbPanel?.SetExpanded(!_sbPanel.Expanded);
            else if (a == "liftrestart") SandboxKey(Key.F3);
            else if (a == "quiz") QuizNext();
            else if (a == "replay") QuizReplay();
            else if (a.StartsWith("anstrue")) { if (_quiz != null) QuizAnswer((AzOf(_quiz.Src - SnMe()) + float.Parse(a[7..], System.Globalization.CultureInfo.InvariantCulture) + 360) % 360); }
            else if (a.StartsWith("ans")) QuizAnswer(float.Parse(a[3..], System.Globalization.CultureInfo.InvariantCulture));
            else if (a == "summary") QuizSummary(true);
            else if (a.StartsWith("sub")) SbSetSub(int.Parse(a[3..]));
            else if (a.StartsWith("bots")) SbSpawnBots(int.Parse(a[4..]));
            else if (a.StartsWith("kind")) _sbBotKind = int.Parse(a[4..]);
            else if (a.StartsWith("tier")) _sbBotTier = int.Parse(a[4..]);
            else if (a == "god") _sbGod = !_sbGod;
            else if (a == "wall") _snWall = !_snWall;
            else if (a.StartsWith("dist")) _snDist = int.Parse(a[4..]);
            else if (a.StartsWith("dir")) _snDir = int.Parse(a[3..]);
            else if (a.StartsWith("shot")) SnShot(int.Parse(a[4..]), SnDists[_snDist], _snDir, _snWall);
            else if (a == "allshots")
            {
                // таблица в лог: каждый ствол × каждая дистанция, спереди и за стеной
                for (int g = 0; g < SnGuns.Length; g++)
                    foreach (var d in SnDists) { SnShot(g, d, 0, false); SnShot(g, d, 2, true); }
                foreach (var d in SnDists) SnExplosion(false, d, 4, false);
                for (int gait = 0; gait < 3; gait++) foreach (var d in new[] { 5f, 10f, 20f, 30f, 45f }) SnStep(SnMe() + Az(270, d), gait, true);
            }
            else if (a.StartsWith("phantom")) { _phOn = !_phOn; if (a.Length > 7) _phGait = int.Parse(a[7..]); }
            else if (a == "boom") SnExplosion(false, SnDists[_snDist], _snDir, _snWall);
            else if (a == "kit") SbRefillBuild();
            else if (a == "reset") SbResetBuild();
            else if (a.StartsWith("rack")) SbRack(Weaponry.GunsByPower[int.Parse(a[4..])]);
            else GD.PrintErr($"sandbox: неизвестное действие сценария {a}");
        }
    }
}
