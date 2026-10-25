using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Навигация (D45): счётчик «тыкается» (хочу идти, а стою — упёрся в стену, пропс, дверь), разбор, во что упёрся.
    /// </summary>
    public sealed partial class PlayerBot
    {
        // ---------- счётчик «тыкается» ----------

        Vector2 _bumpPrevPos;
        bool _bumpPrevMove;
        float _bumpBlocked, _bumpFree;
        bool _bumpEpisode;
        Vector2 _bumpGoal; float _bumpGoalAt = -99; int _bumpGoalN;
        public int Bumps;            // эпизодов «упёрся» (≥ 0,4 с хочу идти, а почти не двигаюсь)
        public float BumpTime;       // секунд в таких эпизодах
        public static bool BumpDiag = Array.IndexOf(Environment.GetCommandLineArgs(), "--bumpdiag") >= 0;

        /// <summary>Вызывается в начале мысли: сравнить, куда хотел в прошлый тик, с тем, сколько прошёл.</summary>
        void CountBump()
        {
            if (_me.Dead) { _bumpPrevMove = false; return; }
            float moved = Vector2.Distance(_me.Pos, _bumpPrevPos);
            if (_bumpPrevMove && moved < 0.25f * World.WalkSpeed * 0.5f * World.Dt)
            {
                _bumpBlocked += World.Dt; _bumpFree = 0;
                if (_bumpBlocked >= 0.4f)
                {
                    BumpTime += World.Dt;
                    if (!_bumpEpisode)
                    {
                        _bumpEpisode = true; Bumps++;
                        // третий упор подряд на пути к одной и той же близкой цели — считать её недостижимой (не тыкаться дальше)
                        if (Vector2.Distance(_bumpGoal, _goalFor) < 1f && _w.Time - _bumpGoalAt < 25f) _bumpGoalN++; else { _bumpGoal = _goalFor; _bumpGoalN = 1; }
                        _bumpGoalAt = _w.Time;
                        if (_bumpGoalN >= 3 && Vector2.Distance(_me.Pos, _goalFor) < 35f && !(_w.FloorAt(_goalFor) is Structure fl && fl.OwnerId == _me.Id) && !InsideBase) { MarkNoReach(_goalFor, 60); _path = null; _bumpGoalN = 0; }
                        SimPerf.Count("упёрся (эпизодов)"); LStat("упёрся");
                        if (BumpDiag) Thought($"[диаг] упёрся: {BumpWhat()} · {Doing} · цель «{GoalName}» · {_w.MonumentAt(_me.Pos)?.Name ?? "поле"} @({_me.Pos.X:0.0};{_me.Pos.Y:0.0}) путь={(_path == null ? "-" : $"{_pathIdx}/{_path.Count}")}");
                    }
                }
            }
            else if ((_bumpFree += World.Dt) > 0.3f) { _bumpBlocked = 0; _bumpEpisode = false; }
            _bumpPrevPos = _me.Pos;
        }

        void NoteMove(in InputCmd cmd)
        {
            _bumpPrevMove = cmd.Move.LengthSquared() > 0.25f && !_me.Dead;
            if (!BumpDiag || _me.Dead) return;
            if (Vector2.Distance(_me.Pos, _stillPos) > 1.5f) { _stillPos = _me.Pos; _stillAt = _w.Time; }
            else if (_w.Time - _stillAt > 45) { _stillAt = _w.Time; Thought($"[диаг] стою 45 с: {Doing} · цель «{GoalName}» · ход ({cmd.Move.X:0.00};{cmd.Move.Y:0.00}) · путь={(_path == null ? "-" : $"{_pathIdx}/{_path.Count} → {_path[_pathIdx].X:0.0};{_path[_pathIdx].Y:0.0}")} · место={(_standPick is Vector2 sp ? $"{sp.X:0.0};{sp.Y:0.0}" : "-")} · @({_me.Pos.X:0.0};{_me.Pos.Y:0.0}) · застрял {_goalStuck:0.0} · к {_goalFor.X:0.0};{_goalFor.Y:0.0}"); }
        }
        Vector2 _stillPos; float _stillAt;

        /// <summary>Во что упёрся (для разбора): стена постройки, дверь, стена/пропс РТ, глыба, установленное, человек.</summary>
        string BumpWhat()
        {
            MapWall hit = null;
            _w.WalkBlocked(_me.Pos, _me.Radius + 0.12f, mw => { hit ??= mw; return true; });   // внутри обхода стен — только запомнить (общий буфер запроса)
            string what = null;
            if (hit != null)
            {
                var mw = hit;
                what = mw.Owner != null ? (mw.Owner.Kind == StructKind.Door ? (MineDoor(mw.Owner) ? $"своя дверь (откр {mw.Owner.Open}, замок {mw.Owner.Locked}, {Vector2.Distance(_me.Pos, mw.Owner.Center):0.00} м, линия {_w.ClearLine(_me.Pos, mw.Owner.Center, mw.Owner, true)}, транзит {_transitDoor == mw.Owner}, закрыть {_toClose.Count})" : "чужая дверь") : $"постройка {mw.Owner.Kind}")
                     : mw.Hidden ? $"пропс РТ (h {mw.Wall.Height:0.0})" : mw.Prefab ? $"стена РТ (h {mw.Wall.Height:0.0})" : $"стена карты {mw.Kind}";
            }
            if (what != null) return what;
            foreach (var a in _w.Actors) if (a != _me && !a.Dead && Vector2.Distance(a.Pos, _me.Pos) < _me.Radius * 2 + 0.15f) return $"человек {a.Name}";
            if (_w.WalkBlocked(_me.Pos, _me.Radius + 0.12f)) return "глыба/дерево/установленное";
            return "ничего рядом (ёрзает)";
        }

        // ---------- недостижимые цели: маршрута нет даже в широком окне — не тыкаться, а сразу отказаться ----------

        readonly List<(Vector2 at, float until)> _noReach = new();

        void MarkNoReach(Vector2 p, float sec = 120)
        {
            _noReach.RemoveAll(x => x.until <= _w.Time);
            if (_noReach.Count > 40) _noReach.RemoveAt(0);
            _noReach.Add((p, _w.Time + sec));
            SimPerf.Count("цель недостижима");
            LStat("цель недостижима");
        }

        /// <summary>Точка недавно оказалась недостижимой (за стеной без прохода, в закрытой комнате) — выбрать другую цель.</summary>
        bool NoReach(Vector2 p)
        {
            foreach (var (at, until) in _noReach) if (until > _w.Time && Vector2.DistanceSquared(at, p) < 1.2f * 1.2f) return true;
            return false;
        }

        /// <summary>Открытое место рядом с точкой: не на чужом полу (в доме), не в препятствии; ближе ко мне. Для мести и перехвата.</summary>
        Vector2 OpenGroundNear(Vector2 p)
        {
            bool Ok(Vector2 c) => _w.FloorAt(c) == null && !_w.WalkBlocked(c, _me.Radius + 0.15f) && _w.Terrain.IsLand(c);
            if (Ok(p) && !NoReach(p)) return p;
            var toMe = _me.Pos - p;
            float a0 = MathF.Atan2(toMe.Y, toMe.X);
            foreach (float r in new[] { 5f, 8f, 12f, 16f })
                for (int k = 0; k < 12; k++)
                {
                    float a = a0 + (k % 2 == 0 ? 1 : -1) * (k / 2) * 0.52f;
                    var c = p + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                    if (Ok(c) && !NoReach(c)) return c;
                }
            return p;
        }

        // ---------- место «рукой»: стоять можно, стены между мной и целью нет, дойти можно; с кэшем ----------

        Vector2 _standFor; float _standR, _standAt = -99; Vector2? _standPick;

        /// <summary>
        /// Точка рядом с целью, откуда достаёшь рукой: не в препятствии (с запасом), без стены до цели, не в закрытой соте,
        /// дойти можно (прямая свободна — лучше; иначе дальше по очереди). Кольца 0,95 / 1,25 / 0,7 / 1,55 м — ящик на полке
        /// или в углу пропса тоже достаётся. null — встать негде (раньше возвращалась точка в стене, и бот в неё упирался).
        /// </summary>
        Vector2? StandPoint(Vector2 target, float r)
        {
            if (_standPick != null && _w.Time - _standAt < 1.0f && Vector2.DistanceSquared(_standFor, target) < 0.01f && MathF.Abs(_standR - r) < 0.01f && !BadStand(_standPick.Value))
                return _standPick;
            _standFor = target; _standR = r; _standAt = _w.Time;
            Vector2? best = null;
            float bd = float.MaxValue;
            foreach (var (rr, pad) in new[] { (r, 0.06f), (r + 0.3f, 0.06f), (MathF.Max(0.6f, r - 0.25f), 0.06f), (r + 0.6f, 0.06f), (r, 0f), (r + 0.3f, 0f) })
            {
                if (rr > World.InteractRange - 0.2f || (pad == 0 && best != null)) continue;   // впритык — только если с запасом места нет
                for (int k = 0; k < 16; k++)
                {
                    float a = k * MathF.Tau / 16;
                    var c = target + new Vector2(MathF.Cos(a), MathF.Sin(a)) * rr;
                    if (_w.WalkBlocked(c, _me.Radius + pad) || !_w.ClearLine(c, target, null, true) || (_plan != null && !StandOk(c)) || BadStand(c)) continue;
                    float d = Vector2.Distance(c, _me.Pos) + (rr - r) * 0.8f;
                    if (d > 1.2f && !_w.SegmentWalkable(_me.Pos, c, _me.Radius + 0.05f, 0, OwnDoor)) d += 6f;   // за углом — дальше по очереди
                    if (d < bd) { bd = d; best = c; }
                }
                if (best != null && bd < 6f) break;   // хорошее место на ближнем кольце — дальние не смотрим
            }
            _standPick = best;
            return best;
        }

        // ---------- дверь: подойти с моей стороны, открыть, пройти насквозь перпендикулярно ----------

        Structure _transitDoor;
        Vector2 _transitTo;
        float _transitUntil;

        static Vector2 DoorNormal(Structure door)
        {
            var d = door.B - door.A;
            var n = new Vector2(d.Y, -d.X);
            return n.LengthSquared() > 1e-6f ? Vector2.Normalize(n) : Vector2.UnitY;
        }

        void StartTransit(Structure s)
        {
            float side = SideOf(s, _me.Pos);
            if (side == 0) side = 1;
            _transitDoor = s;
            _transitTo = s.Center - DoorNormal(s) * side * 1.05f;
            _transitUntil = _w.Time + 2.5f;
            SimPerf.Count("дверь: прохожу");
        }

        /// <summary>Иду сквозь дверь: прямо через проём до точки за ней (не срезая угол о косяк). true — ввод занят.</summary>
        bool DoorTransit(ref InputCmd cmd)
        {
            var s = _transitDoor;
            if (s == null) return false;
            if (_w.Time > _transitUntil || !_w.Structures.Contains(s) || Vector2.Distance(_me.Pos, _transitTo) < 0.35f) { _transitDoor = null; return false; }
            if (!s.Open)
            {
                // закрылась перед носом (ветер, сосед) — открыть снова, если рядом
                if (Vector2.Distance(_me.Pos, s.Center) < World.InteractRange - 0.2f && _w.BotToggleDoor(_me, s)) NoteToClose(s);
                else { _transitDoor = null; return false; }
            }
            var to = _transitTo - _me.Pos;
            cmd.Move = Vector2.Normalize(to + new Vector2(1e-4f, 0));
            cmd.Aim = _transitTo;
            return true;
        }

        // ---------- скольжение вдоль стены вместо случайного поворота ----------

        float _slideSide = 1;

        /// <summary>Упёрся: выбрать ближайшее к желаемому свободное направление (сначала в ту же сторону, что в прошлый раз).</summary>
        Vector2? SlideDir(Vector2 want)
        {
            foreach (float a in new[] { 0.45f, 0.9f, 1.35f, 1.8f, 2.3f })
                foreach (float sgn in new[] { _slideSide, -_slideSide })
                {
                    var dir = Geo.Rotate(want, a * sgn);
                    if (!_w.WalkBlocked(_me.Pos + dir * 0.45f, _me.Radius, OwnDoor) && !_w.WalkBlocked(_me.Pos + dir * 0.9f, _me.Radius, OwnDoor))
                    {
                        _slideSide = sgn;
                        return dir;
                    }
                }
            return null;
        }

        // ---------- «дороги»: грубая сетка 4 м с запасом от стен, общая для всех ботов ----------

        List<Vector2> _road;
        Vector2 _roadGoal;
        float _roadAt = -99;
        int _roadIdx;

        /// <summary>
        /// Следующий отрезок пути к дальней цели: по «дорогам» (грубая сетка, кэш на всех ботов) — точка впереди в 12–22 м,
        /// до неё прямо, если свободно, иначе точный A*. Раньше дальняя цель — «25 м по прямой», и бот упирался в чужую базу или забор РТ.
        /// </summary>
        List<Vector2> RoadLeg(Vector2 goal)
        {
            if (_road == null || Vector2.Distance(_roadGoal, goal) > 8f || _w.Time - _roadAt > 25f || Vector2.Distance(_road[Math.Min(_roadIdx, _road.Count - 1)], _me.Pos) > 14f)
            {
                _road = Roads.Of(_w).Find(_me.Pos, goal);
                _roadGoal = goal; _roadAt = _w.Time; _roadIdx = 0;
                if (_road == null) { SimPerf.Count("дороги: нет пути"); return null; }
            }
            // ближайшая ко мне точка дороги (не назад)
            float bd = float.MaxValue;
            for (int i = _roadIdx; i < _road.Count && i < _roadIdx + 12; i++)
            {
                float d = Vector2.Distance(_road[i], _me.Pos);
                if (d < bd) { bd = d; _roadIdx = i; }
            }
            int pick = _roadIdx;
            for (int i = _roadIdx; i < _road.Count; i++)
            {
                if (Vector2.Distance(_road[i], _me.Pos) > 22f) break;
                pick = i;
            }
            if (pick == _roadIdx && pick < _road.Count - 1) pick++;
            var wp = _road[pick];
            if (_w.SegmentWalkable(_me.Pos, wp, _me.Radius + 0.05f, 0, OwnDoor)) return new List<Vector2> { wp };
            var fine = Pathfinder.Find(_w, _me.Pos, wp, _me.Radius, 1.2f, OwnDoor);
            if (fine == null && pick > _roadIdx) fine = Pathfinder.Find(_w, _me.Pos, _road[_roadIdx], _me.Radius, 1.0f, OwnDoor);
            return fine;
        }

        /// <summary>
        /// Сетка «дорог» мира: клетки 4 м, проходимы, если рядом с центром есть место с запасом 1 м от стен, глыб и построек;
        /// переход между соседями — если прямая проходима с запасом. Клетки считаются лениво и устаревают через 2 мин
        /// (постройки игроков меняются); РТ с охраной — дорогие (обходим, если цель не на них), вода — очень дорогая.
        /// </summary>
        sealed class Roads
        {
            const float Cell = 4f;
            const float Ttl = 120f;
            static readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, Roads> _all = new();
            public static Roads Of(World w) => _all.GetValue(w, x => new Roads(x));

            readonly World _w;
            readonly int _n;
            readonly float[] _at;          // когда посчитана клетка
            readonly byte[] _rep;          // 0 — непроходима, 1..5 — какая точка клетки свободна
            readonly float[] _cost;
            readonly sbyte[] _edge;        // 8 на клетку: 0 нет, 1 да
            readonly float[] _edgeAt;
            static readonly int[] Dx = { 1, 1, 0, -1, -1, -1, 0, 1 }, Dy = { 0, 1, 1, 1, 0, -1, -1, -1 };
            static readonly Vector2[] RepOff = { Vector2.Zero, new(1.2f, 0), new(-1.2f, 0), new(0, 1.2f), new(0, -1.2f) };

            Roads(World w)
            {
                _w = w;
                _n = (int)MathF.Ceiling(World.HalfSize * 2 / Cell) + 1;
                _at = new float[_n * _n]; Array.Fill(_at, -1e9f);
                _rep = new byte[_n * _n];
                _cost = new float[_n * _n];
                _edge = new sbyte[_n * _n * 8];
                _edgeAt = new float[_n * _n * 8]; Array.Fill(_edgeAt, -1e9f);
                _g = new float[_n * _n]; _from = new int[_n * _n]; _stamp = new int[_n * _n]; _closed = new bool[_n * _n];
            }

            int Idx(Vector2 p) => Math.Clamp((int)MathF.Floor((p.Y + World.HalfSize) / Cell), 0, _n - 1) * _n + Math.Clamp((int)MathF.Floor((p.X + World.HalfSize) / Cell), 0, _n - 1);
            Vector2 Center(int i) => new Vector2((i % _n + 0.5f) * Cell - World.HalfSize, (i / _n + 0.5f) * Cell - World.HalfSize);

            Monument _goalMon;

            void Ensure(int i)
            {
                if (_w.Time - _at[i] < Ttl) return;
                _at[i] = _w.Time;
                var c = Center(i);
                _rep[i] = 0;
                for (int k = 0; k < RepOff.Length; k++)
                    if (!_w.WalkBlocked(c + RepOff[k], 1.0f)) { _rep[i] = (byte)(k + 1); break; }
                float cost = _w.Terrain.IsLand(c) ? 1f : 6f;
                if (_w.OnRoad(c)) cost *= 0.75f;   // по дорогам мира (RoadNet.cs) идти охотнее
                foreach (var m in _w.Monuments)
                    if (m.Contains(c, 3))
                    {
                        bool guarded = false;
                        foreach (var h in _w.NpcHomes.Values) if (m.Contains(h, 2)) { guarded = true; break; }
                        cost *= guarded ? 4f : 1.6f;   // через РТ — только если цель на ней: там заборы, пропсы, охрана
                        break;
                    }
                _cost[i] = cost;
                for (int k = 0; k < 8; k++) _edgeAt[i * 8 + k] = -1e9f;
            }

            Vector2 Rep(int i) => _rep[i] == 0 ? Center(i) : Center(i) + RepOff[_rep[i] - 1];

            bool Edge(int i, int k, int j)
            {
                int e = i * 8 + k;
                if (_w.Time - _edgeAt[e] < Ttl) return _edge[e] == 1;
                _edgeAt[e] = _w.Time;
                bool ok = _w.SegmentWalkable(Rep(i), Rep(j), 0.45f);
                _edge[e] = (sbyte)(ok ? 1 : 0);
                int back = j * 8 + (k + 4) % 8;   // обратное ребро — то же
                _edge[back] = _edge[e]; _edgeAt[back] = _w.Time;
                return ok;
            }

            readonly float[] _g; readonly int[] _from; readonly int[] _stamp; readonly bool[] _closed;
            int _search;
            readonly PriorityQueue<int, float> _open = new();

            /// <summary>Путь по «дорогам» от from к to (точки клеток и сама цель), null — нет (бюджет 9000 клеток).</summary>
            public List<Vector2> Find(Vector2 from, Vector2 to)
            {
                long t0 = SimPerf.Begin();
                _goalMon = null;
                foreach (var m in _w.Monuments) if (m.Contains(to, 6)) { _goalMon = m; break; }
                int s = Idx(from), g = Idx(to);
                int stamp = ++_search;
                float H(int i) { float dx = MathF.Abs(i % _n - g % _n), dy = MathF.Abs(i / _n - g / _n); return (MathF.Max(dx, dy) + 0.414f * MathF.Min(dx, dy)) * Cell; }
                _open.Clear();
                if (_stamp[s] != stamp) { _stamp[s] = stamp; _closed[s] = false; }
                _g[s] = 0; _from[s] = -1;
                _open.Enqueue(s, H(s));
                int found = -1, expanded = 0;
                while (_open.Count > 0 && expanded < 9000)
                {
                    int c = _open.Dequeue();
                    if (_closed[c]) continue;
                    _closed[c] = true; expanded++;
                    if (H(c) <= Cell * 1.5f) { found = c; break; }   // рядом с целью — дальше точный путь
                    int cx = c % _n, cy = c / _n;
                    for (int k = 0; k < 8; k++)
                    {
                        int x = cx + Dx[k], y = cy + Dy[k];
                        if (x < 0 || y < 0 || x >= _n || y >= _n) continue;
                        int nb = y * _n + x;
                        if (_stamp[nb] != stamp) { _stamp[nb] = stamp; _g[nb] = float.MaxValue; _closed[nb] = false; }
                        if (_closed[nb]) continue;
                        Ensure(nb);
                        if (_rep[nb] == 0) continue;
                        if (c != s && !Edge(c, k, nb)) continue;
                        float cost = _cost[nb];
                        if (_goalMon != null && _goalMon.Contains(Center(nb), 3)) cost = MathF.Min(cost, 1.3f);   // цель на РТ — заходим
                        float ng = _g[c] + (k % 2 == 1 ? 1.414f : 1f) * Cell * cost;
                        if (ng >= _g[nb]) continue;
                        _g[nb] = ng; _from[nb] = c;
                        _open.Enqueue(nb, ng + H(nb));
                    }
                }
                SimPerf.Count("дороги: клеток раскрыто", expanded);
                SimPerf.End(found < 0 ? "дороги (не найден)" : "дороги", t0);
                if (found < 0) return null;
                var list = new List<Vector2>();
                for (int c = found; c != -1 && c != s; c = _from[c]) list.Add(Rep(c));
                list.Reverse();
                list.Add(to);
                return list;
            }
        }
    }
}
