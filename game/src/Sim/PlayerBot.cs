using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>Мозг: каждый тик выдаёт ввод своему персонажу — как игрок с клавиатурой.</summary>
    public interface IBrain
    {
        Actor Me { get; }
        InputCmd Think();
    }

    /// <summary>
    /// Бот-игрок, архетип «фермер» (docs/design/06-bots.md, docs/gpt/03-bot-scenarios.md): голый старт на пляже →
    /// дерево и камень → топор и кирка → место под базу → межевой столб → дом 2×2 по шаблону (проём на юг) →
    /// улучшение до дерева → дверь → ящик → складывает добычу и кормит столб. В бой не лезет: при угрозе уходит.
    /// Управляет персонажем через тот же ввод, что и игрок: выбор слота, прицел, ЛКМ, R, E, Alt.
    /// Своё состояние почти не хранит — смотрит на мир (где мой столб, что построено), поэтому переживает смерть и загрузку.
    /// </summary>
    public enum BotArchetype { Farmer, Roamer }

    public sealed partial class PlayerBot : IBrain
    {
        public BotArchetype Archetype = BotArchetype.Farmer;
        public Actor Me => _me;
        readonly Actor _me;
        readonly World _w;
        readonly Random _rng;
        public string Doing = "";              // для отладки и подписи над головой
        public string Debug => _site == null ? "без базы" : $"база ({_origin.i},{_origin.j}) путь={(_path == null ? "-" : $"{_pathIdx}/{_path.Count}")} застрял={_goalStuck:0.0} внутри={InsideBase} узел={(_node == null ? "-" : $"{_node.Pos.X:0};{_node.Pos.Y:0}")}{(MyFurnace() is Deployable f ? $" печь={f.Pos.X:0.0};{f.Pos.Y:0.0}" : "")}";

        // характер (0..1): осторожность, жадность до ресурсов
        readonly float _caution, _greed;

        Vector2? _site;                        // выбранное место базы (клетка начала дома)
        (int i, int j) _origin;
        float _deadTime, _thinkFireToggle, _stuck, _avoidTimer, _avoidAngle;
        Vector2 _lastPos;
        bool _fireEdge;
        ResourceNode _node;
        float _nodeTimer;

        public PlayerBot(Actor me, World w, int seed)
        {
            _me = me; _w = w; _rng = new Random(seed);
            _caution = 0.5f + (float)_rng.NextDouble() * 0.4f;
            _greed = 0.3f + (float)_rng.NextDouble() * 0.5f;
        }

        // ---------- помощники ----------

        static int Recipe(string output) => Recipes.All.FindIndex(r => r.Output == output);

        int SlotOf(string id)
        {
            for (int i = 0; i < Actor.BeltSize; i++) if (_me.Belt[i]?.Id == id) return i;
            return -1;
        }

        int PackOf(string id)
        {
            for (int i = 0; i < Actor.PackSize; i++) if (_me.Pack[i]?.Id == id) return i;
            return -1;
        }

        /// <summary>Есть ли вещь — на поясе или в рюкзаке (пояс бывает забит, крафт уходит в рюкзак).</summary>
        bool Has(string id) => SlotOf(id) >= 0 || PackOf(id) >= 0;

        /// <summary>Взять предмет в руки. true — уже в руках. Из рюкзака — сначала на пояс (меняется с последним слотом).</summary>
        bool Hold(string id, ref InputCmd cmd)
        {
            int s = SlotOf(id);
            if (s < 0)
            {
                int p = PackOf(id);
                if (p < 0) return false;
                int j = Actor.BeltSize - 1;
                if (_me.Current == j) j--;   // не трогаем то, что сейчас в руках
                cmd.UiOp = (int)UiOp.Move;
                cmd.UiItem = $"pack#{p}>belt#{j}";
                return false;
            }
            if (_me.Current == s) return true;
            cmd.SelectSlot = s;
            return false;
        }

        bool Craft(string output, ref InputCmd cmd)
        {
            int r = Recipe(output);
            if (r < 0) return false;
            foreach (var job in _me.CraftQueue) if (job.Recipe.Output == output) { Doing = $"крафтит {Items.Name(output)}"; return true; }
            if (!_w.CanCraft(_me, Recipes.All[r])) return false;
            cmd.Craft = r;
            Doing = $"крафтит {Items.Name(output)}";
            return true;
        }

        // ---------- одежда (D47): надеть лучшее, сшить тряпьё/ватник, антирадин ----------

        /// <summary>Что бот шьёт сам, по порядку: тряпьё (в руках), потом у верстака 1 — ватник и кожа.</summary>
        static readonly string[] WearCraftOrder =
        {
            "wear_rags_shirt", "wear_rags_pants", "wear_rags_wraps", "wear_rags_mittens", "wear_rags_bandana",
            "wear_vatnik_telogreika", "wear_vatnik_pants", "wear_leather_helmet", "wear_vatnik_ushanka", "wear_vatnik_valenki", "wear_leather_bracers", "wear_leather_chestplate",
        };

        void Clothe(ref InputCmd cmd)
        {
            uint t = _w.Tick + (uint)_me.Id;
            if (t % 150 == 0) _w.BotDress(_me);                                      // раз в 5 с — лучшее из рюкзака на себя
            if (_me.RadDose >= 150 && _me.Count("antirad") > 0 && !cmd.Eat) cmd.Eat = true;   // F: антирадин идёт первым
            if (t % 90 != 45 || cmd.Craft >= 0 || _threat != null || _me.CraftQueue.Count > 0) return;
            foreach (var id in WearCraftOrder)
            {
                var wd = Clothing.Def(id);
                var cur = Clothing.Def(_me.Worn[(int)wd.Slot]?.Id);
                if (cur != null && Clothing.BotScore(cur) >= Clothing.BotScore(wd)) continue;   // уже не хуже
                if (_me.Count(id) > 0) continue;                                              // сшито — наденет при следующем BotDress
                int r = Recipe(id);
                if (r < 0 || !_w.CanCraft(_me, Recipes.All[r])) continue;
                int cloth = 0;
                foreach (var (cid, n) in Recipes.All[r].Cost) if (cid == "cloth") cloth = n;
                int reserve = Freezing ? 0 : 8;                                               // мёрзнет — шьёт из последней ткани
                if (_me.Count("cloth") - cloth < reserve) continue;                            // ткань на бинты — не трогать
                cmd.Craft = r;
                return;
            }
        }

        bool _warming;
        float _warmNoTry, _warmAt = -1;

        /// <summary>Мёрзнет без кофты и не из чего сшить рубаху — нужна конопля.</summary>
        bool NeedClothesCloth => _me.Cold > 25 && _me.FeltTemp < Clothing.Comfort && _me.Worn[(int)WearSlot.Kofta] == null && _me.Count("cloth") < 16;

        /// <summary>Мёрзнет: воздух + одежда ниже +15 °C и переохлаждение копится.</summary>
        bool Freezing => _me.FeltTemp < Clothing.Comfort && _me.Cold > 20;

        /// <summary>
        /// Холод (D47): переохлаждение ≥ 60 — греться у своей горящей печи или в доме (верстак внутри), пока не отпустит.
        /// Не помогает (ночь в тундре голым) — бросает на 2 мин; греться негде — ищет коноплю на одежду.
        /// </summary>
        bool WarmUp(ref InputCmd cmd)
        {
            bool cold = _me.Cold >= 60 && _me.FeltTemp < Clothing.Comfort;
            if (_warming && _me.Cold <= 5) _warming = false;
            if (NeedClothesCloth) { Doing = "мёрзнет — ищет коноплю на одежду"; if (UsePickup(PickupKind.Hemp, ref cmd)) return true; }
            if ((!_warming && !cold) || _w.Time < _warmNoTry) return false;
            Vector2? spot = MyFurnace() is Deployable f && f.Burning ? f.Pos + new Vector2(1.3f, 0.3f) : _plan != null ? BenchStand : null;
            if (spot == null)
            {
                _warming = false;
                if (_me.Worn[(int)WearSlot.Kofta] == null && _me.Count("cloth") < 15) { Doing = "мёрзнет — ищет коноплю на одежду"; if (UsePickup(PickupKind.Hemp, ref cmd)) return true; }
                _warmNoTry = _w.Time + 60;
                return false;
            }
            if (!_warming) { _warming = true; _warmAt = -1; Thought($"замёрз ({_me.FeltTemp:0} °C) — иду греться"); }
            Doing = "греется";
            if (!GoTo(spot.Value, 0.8f, ref cmd)) return true;
            if (_warmAt < 0) _warmAt = _w.Time;
            if (_w.Time - _warmAt > 6 && _me.FeltTemp < Clothing.Comfort) { _warming = false; _warmNoTry = _w.Time + 120; return false; }   // здесь не согреться
            return true;
        }

        bool CanAfford(string output)
        {
            int r = Recipe(output);
            return r >= 0 && _w.CanCraft(_me, Recipes.All[r]);
        }

        /// <summary>Идти к точке; true — дошли (в пределах tol).</summary>
        // маршрут A* (когда прямая закрыта или застряли)
        List<Vector2> _path;
        int _pathIdx;
        Vector2 _pathGoal;
        float _pathAge, _pathCheck, _goalBest = float.MaxValue, _goalStuck;
        Vector2 _goalFor;

        bool MoveTo(Vector2 p, float tol, ref InputCmd cmd)
        {
            var d = p - _me.Pos;
            CloseDoorBehind();
            if (d.Length() <= tol) { _path = null; return true; }
            if (DoorTransit(ref cmd)) return false;   // иду сквозь свою дверь — до точки за ней, не срезая о косяк

            // прогресс к цели: 1,5 с без приближения — застряли
            if (Vector2.Distance(_goalFor, p) > 0.5f) { _goalFor = p; _goalBest = float.MaxValue; _goalStuck = 0; }
            if (d.Length() < _goalBest - 0.2f) { _goalBest = d.Length(); _goalStuck = 0; } else _goalStuck += World.Dt;
            // долгое окно: за 8 с не приблизился и на 2 м (ёрзает вдоль забора — короткий счётчик сбрасывается) — считать застрявшим
            if (Vector2.Distance(_winGoal, p) > 3f || _w.Time - _winAt > 8f)
            {
                if (Vector2.Distance(_winGoal, p) <= 3f && _winDist - d.Length() < 2f && d.Length() > 6f) { _goalStuck = MathF.Max(_goalStuck, 2f); _path = null; _pathCheck = 0; _exitTryAt = -99; _road = null; SimPerf.Count("ёрзает — ищу путь"); }
                _winGoal = p; _winAt = _w.Time; _winDist = d.Length();
            }

            // идём по маршруту
            if (_path != null && Vector2.Distance(_pathGoal, p) < 1.5f && (_pathAge += World.Dt) < 15f && _goalStuck < 2.5f)
            {
                while (_pathIdx < _path.Count - 1 && Vector2.Distance(_me.Pos, _path[_pathIdx]) < 0.4f) _pathIdx++;
                var wp = _path[_pathIdx];
                if (_pathIdx < _path.Count - 1 || Vector2.Distance(_me.Pos, wp) > 0.4f)
                {
                    if (OpenDoorAhead(wp, ref cmd)) return false;
                    var dir = Vector2.Normalize(wp - _me.Pos + new Vector2(1e-4f, 0));
                    cmd.Move = Avoid(dir);   // упёрся на маршруте (сосед, свежая постройка) — скольжение вдоль
                    cmd.Aim = wp;
                    return false;
                }
                _path = null;   // дошли до конца маршрута (для дальней цели — до промежуточной точки)
            }
            else _path = null;

            // прямая закрыта (дом, дерево у угла, печь, забор РТ) или застряли — строим маршрут.
            // Прямая проверяется на 10 м вперёд и для дальних целей (раньше — только ближе 35 м: дальняя цель «за чужим домом»
            // давала упор в стену до 1,5 с застревания на каждом шаге); дальняя цель — по «дорогам».
            bool far = d.Length() >= 35;
            bool checkNow = (_pathCheck -= World.Dt) <= 0;
            if (checkNow) _pathCheck = 0.25f;
            float look = MathF.Min(d.Length(), 10f);
            if (checkNow && (_goalStuck > 1.2f || far || !_w.SegmentWalkable(_me.Pos, _me.Pos + Vector2.Normalize(d) * look, _me.Radius + 0.05f, d.Length() <= 10f ? tol + 0.4f : 0, OwnDoor)))
            {
                _pathCheck = 0.6f;
                // внутри РТ, а цель снаружи — сначала выйти через проход (иначе промежуточная точка «за забором» водила по кругу внутри)
                var path = ExitPath(p);
                if (path != null) Thought("выбираюсь с РТ через проход");
                else if (!far)
                {
                    path = Pathfinder.Find(_w, _me.Pos, p, _me.Radius, tol, OwnDoor);
                    // окно вокруг отрезка «я — цель» мало (вход в здание РТ с другой стороны) — один раз шире
                    if (path == null && _w.Time - _wideAt > 3f)
                    {
                        _wideAt = _w.Time;
                        path = Pathfinder.Find(_w, _me.Pos, p, _me.Radius, tol, OwnDoor, 45000, 26f);
                        if (path == null) { MarkNoReach(p); _pathCheck = 2f; }
                        else SimPerf.Count("A*: широкое окно помогло");
                    }
                }
                else
                {
                    // дальняя цель: прямая на 10 м свободна и дорога не нужна — не тратим поиск
                    bool clear = _goalStuck <= 1.2f && _w.SegmentWalkable(_me.Pos, _me.Pos + Vector2.Normalize(d) * 10f, _me.Radius + 0.05f, 0, OwnDoor);
                    path = clear && _roadPref < _w.Time ? null : RoadLeg(p);
                    if (path == null && !clear)
                        foreach (float turn in new[] { 0.5f, -0.5f, 1.0f, -1.0f, 1.6f, -1.6f, 2.4f, -2.4f })
                        {
                            var g2 = _me.Pos + Geo.Rotate(Vector2.Normalize(d), turn) * 25;
                            if (!_w.Terrain.IsLand(g2)) continue;
                            path = Pathfinder.Find(_w, _me.Pos, g2, _me.Radius, 1.5f, OwnDoor);
                            if (path != null) break;
                        }
                    if (path == null && !clear) { _pathCheck = 1.5f; if (_goalStuck > 2f) MarkNoReach(p, 60); }   // пути нет ни по дорогам, ни в обход — цель за стенами
                    if (path != null) _roadPref = _w.Time + 20f;      // пошёл по дороге — держусь её, а не «напрямик» через 0,6 с
                    else _pathCheck = clear ? 0.5f : _pathCheck;
                }
                if (path != null && path.Count > 0)
                {
                    _path = path; _pathIdx = 0; _pathGoal = p; _pathAge = 0; _goalStuck = 0;
                    cmd.Move = Vector2.Normalize(path[0] - _me.Pos + new Vector2(1e-4f, 0));
                    cmd.Aim = path[0];
                    return false;
                }
                // маршрута нет, потому что сам стою «в стене» (зажало в углу между треугольниками) — сначала выбраться на свободное место
                if (path == null && _w.WalkBlocked(_me.Pos, _me.Radius) && FreeSpotNear(_me.Pos) is Vector2 free)
                {
                    _path = new List<Vector2> { free }; _pathIdx = 0; _pathGoal = p; _pathAge = 13f;
                    cmd.Move = Vector2.Normalize(free - _me.Pos + new Vector2(1e-4f, 0));
                    return false;
                }
            }
            // цель недавно оказалась недостижимой (за стеной без прохода, в чужом доме) — не толкаться в стену: стоять, вызывающий сменит цель
            if (_path == null && NoReach(p) && _goalStuck > 0.5f) { SimPerf.Count("стою: цель недостижима"); return false; }
            if (OpenDoorAhead(p, ref cmd)) return false;   // напрямую: закрытую свою дверь — открыть, открытую — запомнить, чтобы закрыть за собой
            var want = Vector2.Normalize(d);
            // РТ с охраной обходим стороной, если цель не на самом РТ (дальние цели — по «дорогам», где РТ и так дорогие)
            if (_path == null && !far)
                foreach (var m in _w.Monuments)
                {
                    if (m.Contains(p, 5) || d.Length() < 15 || m.Contains(_me.Pos, 4)) continue;   // с самой РТ выходим без отталкивания (прижимало к забору)
                    var off = _me.Pos - m.Center;
                    float r = (m.Max - m.Min).Length() * 0.5f + 45;
                    float dist = off.Length();
                    if (dist < r && dist > 0.1f) want += off / dist * (r - dist) / r * 2.5f;
                }
            if (want.LengthSquared() > 0.0001f) want = Vector2.Normalize(want);
            // вблизи цели — напрямую (иначе крутится), но если упёрлись в ящик/столб дольше секунды — обходим
            if (d.Length() < 1.2f)
            {
                _nearStuck = Vector2.Distance(_me.Pos, _lastNear) < 0.02f ? _nearStuck + World.Dt : 0;
                _lastNear = _me.Pos;
                cmd.Move = _nearStuck > 1f ? Geo.Rotate(Vector2.Normalize(d), 1.3f) : Vector2.Normalize(d);
            }
            else cmd.Move = Avoid(want);
            cmd.Aim = p;
            return false;
        }

        float _wideAt = -99, _roadPref = -99;

        float _exitTryAt = -99, _winAt = -99, _winDist;
        Vector2 _winGoal;

        /// <summary>Маршрут из огороженной РТ к точке снаружи периметра (в сторону цели), если цель — за пределами этой РТ.</summary>
        List<Vector2> ExitPath(Vector2 goal)
        {
            if (_w.Time - _exitTryAt < 4f) return null;
            Monument m = null;
            foreach (var mm in _w.Monuments) if (mm.Contains(_me.Pos, 1) && !mm.Contains(goal, 4)) { m = mm; break; }
            if (m == null) return null;
            _exitTryAt = _w.Time;
            var path = Pathfinder.FindExit(_w, _me.Pos, m, _me.Radius, OwnDoor);
            if (path != null && path.Count > 0) { SimPerf.Count("выход с РТ найден"); return path; }
            return null;
        }

        /// <summary>Ближайшая свободная точка (кольца до 1,5 м), куда видно напрямую.</summary>
        Vector2? FreeSpotNear(Vector2 at)
        {
            for (float r = 0.25f; r <= 1.5f; r += 0.25f)
                for (int k = 0; k < 16; k++)
                {
                    float a = k * MathF.Tau / 16;
                    var c = at + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                    if (!_w.WalkBlocked(c, _me.Radius + 0.05f) && _w.ClearLine(at, c, null, true)) return c;
                }
            return null;
        }

        // ---------- свои двери: маршрут проходит сквозь них, бот открывает перед собой и закрывает за собой ----------

        /// <summary>Своя дверь, которую я могу открыть: без замка, с моим ключом в кармане или с моим кодом.</summary>
        bool MineDoor(Structure s) => s.Kind == StructKind.Door && (s.OwnerId == _me.Id || (_w.ClaimAt(Building.CellOf(s.Center)) is Deployable c && c.Auth.Contains(_me.Id))) && CanUnlock(s);
        bool CanUnlock(Structure s) => !s.Locked || (s.KeyLock ? _me.Count("key_" + s.Id) > 0 : s.LockAuth.Contains(_me.Id));
        bool OwnDoor(MapWall w) => w.Owner is Structure s && MineDoor(s);

        // двери рядом: раньше каждый шаг перебирал все постройки мира (у 20 ботов — тысячи); теперь список дверей в 12 м,
        // обновляется раз в 0,5 с или при смене числа построек
        readonly List<Structure> _doorsNear = new();
        float _doorsAt = -99;
        int _doorsStructCount = -1;
        Vector2 _doorsPos;

        List<Structure> DoorsNear()
        {
            if (_w.Time - _doorsAt > 0.5f || _w.Structures.Count != _doorsStructCount || Vector2.DistanceSquared(_doorsPos, _me.Pos) > 16)
            {
                _doorsAt = _w.Time; _doorsStructCount = _w.Structures.Count; _doorsPos = _me.Pos;
                _doorsNear.Clear();
                foreach (var s in _w.Structures)
                    if (s.Kind == StructKind.Door && Vector2.DistanceSquared(s.Center, _me.Pos) < 12 * 12) _doorsNear.Add(s);
            }
            return _doorsNear;
        }

        // двери, которые надо закрыть за собой (раньше помнилась одна: проходя тамбур — две двери подряд — бот «забывал» наружную,
        // а через уже открытую дверь проходил, не запоминая её вовсе, — база стояла нараспашку весь прогон)
        readonly List<(Structure door, float side)> _toClose = new();

        static float SideOf(Structure door, Vector2 p)
        {
            var d = door.B - door.A;
            return MathF.Sign((p.X - door.A.X) * d.Y - (p.Y - door.A.Y) * d.X);
        }

        void NoteToClose(Structure s)
        {
            if (!_toClose.Exists(t => t.door == s)) _toClose.Add((s, SideOf(s, _me.Pos)));
        }

        /// <summary>
        /// На пути к точке своя дверь: закрытую — подойти и открыть, открытую — пройти, но запомнить, чтобы закрыть за собой.
        /// </summary>
        bool OpenDoorAhead(Vector2 wp, ref InputCmd cmd)
        {
            foreach (var s in DoorsNear())
            {
                if (!MineDoor(s) || Vector2.DistanceSquared(s.Center, _me.Pos) > 9) continue;
                bool crosses = false, touches = false;
                var grow = new Vector2(_me.Radius + 0.08f);
                foreach (var (min, max, _, _) in Building.Boxes(StructKind.Door, s.A, s.B, false))
                {
                    if (Geo.SegmentAabb(_me.Pos, wp, min, max, out _)) { crosses = true; break; }
                    if (Geo.SegmentAabb(_me.Pos, wp, min - grow, max + grow, out _)) touches = true;
                }
                float dist = Vector2.Distance(_me.Pos, s.Center);
                // маршрут идёт вдоль закрытой своей двери (A* считает её проходимой) — плечом в неё упирался: открыть по пути
                if (!crosses && touches && !s.Open && dist <= World.InteractRange - 0.15f && _w.ClearLine(_me.Pos, s.Center, s, true))
                {
                    if (_w.BotToggleDoor(_me, s)) { NoteToClose(s); SimPerf.Count("дверь: открыл по пути"); }
                    continue;
                }
                if (!crosses) continue;
                float side = SideOf(s, _me.Pos);
                if (side == 0) side = 1;
                var front = s.Center + DoorNormal(s) * side * 0.85f;   // перед дверью с моей стороны — оттуда дверь видна и проём прямо
                if (s.Open)
                {
                    // открыта: пройти насквозь перпендикулярно (раньше шёл к точке за дверью наискосок и цеплял косяк)
                    if (dist < 1.6f) { NoteToClose(s); StartTransit(s); return DoorTransit(ref cmd); }
                    continue;
                }
                cmd.Aim = s.Center;
                // открыть, когда дотягиваюсь и стены между нами нет; иначе — встать перед дверью (не толкаться в стену возле неё)
                if (dist <= World.InteractRange - 0.15f && _w.ClearLine(_me.Pos, s.Center, s, true))
                {
                    if (_w.BotToggleDoor(_me, s)) { NoteToClose(s); StartTransit(s); SimPerf.Count("дверь: открыл"); }
                    else if (Vector2.Distance(_me.Pos, front) > 0.3f) cmd.Move = Vector2.Normalize(front - _me.Pos + new Vector2(1e-4f, 0));
                    return true;
                }
                var to = front - _me.Pos;
                cmd.Move = to.LengthSquared() > 0.01f ? Vector2.Normalize(to) : Vector2.Normalize(s.Center - _me.Pos + new Vector2(1e-4f, 0));
                return true;
            }
            return false;
        }

        /// <summary>
        /// Прошёл дверь и отошёл — закрыть за собой (база не стоит нараспашку). Передумал и ушёл от двери с той же стороны — тоже закрыть.
        /// Вызывается каждый тик (не только в пути): бот, дошедший до ящика сразу за дверью, тоже закрывает её.
        /// </summary>
        void CloseDoorBehind()
        {
            for (int i = _toClose.Count - 1; i >= 0; i--)
            {
                var (d, side) = _toClose[i];
                float dist = Vector2.Distance(_me.Pos, d.Center);
                if (!d.Open || !_w.Structures.Contains(d) || dist > 6f) { _toClose.RemoveAt(i); continue; }   // ушёл далеко — закроет «обход дома»
                if (d == _transitDoor) continue;   // прохожу сквозь неё — не закрывать перед носом
                if ((SideOf(d, _me.Pos) != side && dist > 1.1f) || dist > 1.9f)
                    if (_w.BotToggleDoor(_me, d) || !d.Open) _toClose.RemoveAt(i);
            }
        }

        /// <summary>
        /// Обход препятствия на ходу: упёрся дольше 0,35 с — скольжение вдоль стены (ближайшее к нужному свободное направление,
        /// в ту же сторону, что в прошлый раз) на 0,5–0,9 с. Раньше — случайный поворот: бот «тыкался» в угол по нескольку раз.
        /// </summary>
        Vector2 Avoid(Vector2 move)
        {
            float moved = Vector2.Distance(_me.Pos, _lastPos);
            _lastPos = _me.Pos;
            if (moved < 0.02f) _stuck += World.Dt; else _stuck = MathF.Max(0, _stuck - World.Dt);
            if (_stuck > 0.35f && _avoidTimer <= 0)
            {
                var slide = SlideDir(move);
                _avoidAngle = slide is Vector2 sd ? MathF.Atan2(move.X * sd.Y - move.Y * sd.X, Vector2.Dot(move, sd)) : (_rng.Next(2) == 0 ? -1 : 1) * (1.1f + (float)_rng.NextDouble() * 0.7f);
                _avoidTimer = 0.5f + (float)_rng.NextDouble() * 0.4f;
                _stuck = 0;
                SimPerf.Count(slide != null ? "скольжение вдоль стены" : "обход наугад");
            }
            if (_avoidTimer > 0) { _avoidTimer -= World.Dt; return Geo.Rotate(move, _avoidAngle); }
            return move;
        }

        /// <summary>ЛКМ «щелчком»: для установки нужен фронт нажатия, поэтому через тик отпускаем.</summary>
        void Click(ref InputCmd cmd)
        {
            _fireEdge = !_fireEdge;
            cmd.Fire = _fireEdge;
        }

        // ---------- база ----------

        // своё установленное — один проход по списку мира за тик (раньше MyClaim/MyBox/MyFurnace/MyBenchLevel перебирали его
        // десятки раз за тик на бота). Внутри тика мир не меняется: ставится всё в World.Step, после мозгов.
        uint _mineTick = uint.MaxValue;
        Deployable _mClaim, _mBox, _mFurnace;
        int _mBench;

        void ScanMine()
        {
            if (_mineTick == _w.Tick) return;
            _mineTick = _w.Tick;
            _mClaim = _mBox = _mFurnace = null; _mBench = 0;
            _mBoxes.Clear();
            foreach (var d in _w.Deployables)
            {
                if (d.OwnerId != _me.Id) continue;
                switch (d.Kind)
                {
                    case DeployKind.Claim: _mClaim ??= d; break;
                    case DeployKind.Box: if (d.Storage != null) _mBoxes.Add(d); break;
                    case DeployKind.Furnace: _mFurnace ??= d; break;
                    case DeployKind.Workbench: _mBench = Math.Max(_mBench, Math.Max(1, d.Level)); break;
                }
            }
            PickBox();
        }

        Deployable MyClaim() { ScanMine(); return _mClaim; }

        /// <summary>
        /// Все свои ящики (D49): в плане базы их два (кибитка и мейн), а бот пользовался только первым — ящик забивался до 30/30,
        /// рюкзак не разгружался, ценный лом и детали не доезжали до переработчика. Теперь «мой ящик» — тот, где больше места,
        /// а счёт и взятие — по всем своим ящикам.
        /// </summary>
        readonly List<Deployable> _mBoxes = new();
        void PickBox()
        {
            _mBox = null;
            foreach (var b in _mBoxes) if (_mBox == null || b.Storage.Items.Count < _mBox.Storage.Items.Count - 2) _mBox = b;
        }

        static NodeKind NodeFor(string id) => id switch { "wood" => NodeKind.Tree, "metal_ore" => NodeKind.Metal, "sulfur_ore" => NodeKind.Sulfur, "rubber" => NodeKind.Tire, _ => NodeKind.Stone };
        /// <summary>Добывается ли ресурс из узла (остальное — печь, переработчик, лут; раньше всё неизвестное «добывалось» из камня).</summary>
        static bool Gatherable(string id) => id is "wood" or "stone" or "metal_ore" or "sulfur_ore" or "rubber";

        Deployable MyFurnace() { ScanMine(); return _mFurnace; }

        Deployable MyBox() { ScanMine(); return _mBox; }

        // ---------- ресурсы ----------

        // копим с запасом: пошёл за деревом — не возвращаемся, пока не набрали порцию
        string _stockId;
        int _stockTarget;

        /// <summary>Нужно count ресурса: если не хватает — идём добывать порцию (не меньше минимальной), true — уже есть.</summary>
        bool Need(string id, int count, ref InputCmd cmd)
        {
            if (_stockId == id && _me.Count(id) < _stockTarget) { Gather(NodeFor(id), ref cmd); return false; }
            if (_me.Count(id) >= count) { if (_stockId == id) _stockId = null; return true; }
            // есть в своём ящике, а я недалеко от дома — забрать, а не добывать заново (сейч не должен тормозить стройку)
            if (MyBox() is Deployable box && InBox(box, id) >= count - _me.Count(id) && Vector2.Distance(box.Pos, _me.Pos) < 80)
            {
                Reserve(id, count);
                TakeFromBox(box, id, count, ref cmd);
                return false;
            }
            // не из узла (D49): металл — печь/руда, лом — бочки и хлам, детали — РТ. Раньше Need("metal_fragments"/"scrap") для содержания
            // шкафа вёл бота бить камень без конца (≈ 30 % времени «бьёт камень»)
            if (!Gatherable(id))
            {
                if (id == "metal_fragments") { _metalGoal = Math.Max(_metalGoal, count + 60); SmeltOrMine(ref cmd); return false; }
                if (id == "scrap") { GetScrap(count, MyBox(), ref cmd); return false; }
                if (id.StartsWith("comp_")) { _partsWant[id] = Math.Max(_partsWant.GetValueOrDefault(id), count - Stock(id)); _partsWantAt = _w.Time; }
                Doing = $"нет: {Items.Name(id)}";
                return false;
            }
            _stockId = id;
            _stockTarget = Math.Max(count, id == "wood" ? 350 : 250);
            Gather(NodeFor(id), ref cmd);
            return false;
        }

        /// <summary>Добыча: ближайший узел нужного вида, подальше от охраны. Инструмент — лучший из имеющихся.</summary>
        void Gather(NodeKind kind, ref InputCmd cmd)
        {
            bool tire = kind is NodeKind.Tire or NodeKind.TireStack;
            string tool = kind == NodeKind.Tree || tire ? (Has("tool_hatchet") ? "tool_hatchet" : "tool_rock")
                                                : (Has("tool_pickaxe") ? "tool_pickaxe" : "tool_rock");
            if (!Has(tool) && !Has("tool_rock"))
            {
                // нет даже камня — подбираем его крафтом? камень выдаётся при возрождении; просто ищем ягоды
                Doing = "без инструмента";
                return;
            }
            Doing = kind switch { NodeKind.Tree => "рубит дерево", NodeKind.Metal => "бьёт металлическую руду", NodeKind.Sulfur => "бьёт серную руду", NodeKind.Stone => "бьёт камень", _ => "режет покрышку на резину" };
            if (_node == null || !_w.Nodes.Contains(_node) || _node.Kind != kind || (_nodeTimer -= World.Dt) < 0)
            {
                _node = null;
                float best = float.MaxValue;
                foreach (var n in _w.NodesInRadius(_me.Pos, tire ? 250 : 70, _nodeBuf))
                {
                    if ((tire ? n.Kind is not (NodeKind.Tire or NodeKind.TireStack) : n.Kind != kind) || _badNodes.Contains(n)) continue;
                    if (tire ? Guarded(n.Pos) : _w.NearMonument(n.Pos, 22)) continue;   // покрышки — и на РТ без охраны
                    float d = Vector2.Distance(n.Pos, _me.Pos);
                    if (d < best) { best = d; _node = n; }
                }
                _nodeTimer = 20;
                _nodeBest = float.MaxValue; _nodeNoProgress = 0;
                if (_node == null) { Wander(ref cmd); return; }
            }
            // не можем подойти к узлу — в чёрный список, ищем другой
            if (NoReach(_node.Pos)) { _badNodes.Add(_node); _node = null; return; }
            float nd = Vector2.Distance(_me.Pos, _node.Pos);
            if (nd < _nodeBest - 0.3f) { _nodeBest = nd; _nodeNoProgress = 0; }
            else if ((_nodeNoProgress += World.Dt) > 5f && nd > _node.Radius + 1.0f)
            {
                _badNodes.Add(_node);
                if (_badNodes.Count > 40) _badNodes.Clear();
                _node = null; _nodeBest = float.MaxValue; _nodeNoProgress = 0;
                return;
            }
            if (!GoTo(_node.Pos, _node.Radius + 1.0f, ref cmd)) return;
            cmd.Aim = _node.Pos;
            if (Hold(tool, ref cmd)) cmd.Fire = true;
        }
        readonly List<ResourceNode> _nodeBuf = new();
        readonly List<Deployable> _depBuf = new();
        float _nearStuck;
        Vector2 _lastNear;
        readonly HashSet<ResourceNode> _badNodes = new();
        float _nodeBest = float.MaxValue, _nodeNoProgress;

        Vector2 _wanderTo;
        float _wanderBest = float.MaxValue, _wanderStuck;
        void Wander(ref InputCmd cmd)
        {
            // упёрлись (точка за стеной или в воде) — 5 с без продвижения: выбираем другую
            float wd = Vector2.Distance(_wanderTo, _me.Pos);
            if (wd < _wanderBest - 0.3f) { _wanderBest = wd; _wanderStuck = 0; }
            else if ((_wanderStuck += World.Dt) > 5f) _wanderTo = default;
            if (_wanderTo == default || Vector2.Distance(_wanderTo, _me.Pos) < 2)
            {
                _wanderBest = float.MaxValue; _wanderStuck = 0;
                float a = (float)(_rng.NextDouble() * Math.PI * 2);
                _wanderTo = _me.Pos + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 25;
                if (!_w.Terrain.IsLand(_wanderTo)) _wanderTo = _me.Pos * 0.8f;
            }
            GoTo(_wanderTo, 1.5f, ref cmd);
        }

        Pickup NearestPickup(PickupKind kind)
        {
            Pickup best = null;
            float bd = 90;
            foreach (var p in _w.Pickups)
            {
                if (p.Kind != kind || !p.Available || _badPickups.Contains(p)) continue;
                if (_w.NearMonument(p.Pos, 18)) continue;
                float d = Vector2.Distance(p.Pos, _me.Pos);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        bool UsePickup(PickupKind kind, ref InputCmd cmd)
        {
            var p = NearestPickup(kind);
            if (p == null) return false;
            // не подходим 8 с — пикап недоступен (за стеной, в воде), ищем другой
            float d = Vector2.Distance(_me.Pos, p.Pos);
            if (p != _pickTarget) { _pickTarget = p; _pickBest = d; _pickNoProgress = 0; }
            else if (d < _pickBest - 0.3f) { _pickBest = d; _pickNoProgress = 0; }
            else if ((_pickNoProgress += World.Dt) > 8f) { _badPickups.Add(p); _pickTarget = null; return false; }
            Use(p, p.Pos, ref cmd);
            return true;
        }
        Pickup _pickTarget;
        float _pickBest, _pickNoProgress;
        readonly HashSet<Pickup> _badPickups = new();

        // ---------- угроза ----------

        Actor Threat()
        {
            foreach (var a in _w.Actors)
            {
                if (a == _me || a.Dead || a.Spectator) continue;
                float d = Vector2.Distance(a.Pos, _me.Pos);
                bool armed = a.Weapon != null && !a.Weapon.Def.Melee;
                bool hitMe = _me.LastAttacker == a.Id && _w.Time - _me.LastDamagedTime < 4f;
                if (hitMe || (armed && d < 18 + 14 * _caution && _w.HasLineOfSight(_me.Pos, a.Pos))) return a;
            }
            return null;
        }

        // ---------- мысль ----------

        public InputCmd Think()
        {
            var cmd = InputCmd.Empty;
            cmd.Aim = _me.Pos + _me.FacingDir * 3;
            CountBump();
            StepDeath();
            if (_me.Dead)
            {
                Doing = "мёртв";
                PerceiveOwnDeath();
                if (_rtPhase != RtPhase.None) EndRun("погиб", died: true);
                if (_raid != null) { RaidEnd("погиб", false); _raid = null; _raidCooldown = _w.Time + 900; }
                if (_camp != null) EndCamp("погиб");
                if (_icpt != null) EndIntercept("погиб");
                _deadTime += World.Dt;
                if (_deadTime > 6)
                {
                    // раздел 10: просыпаться дома — спальник перезаряжается до 75 с — подождать его, а не идти голым с пляжа
                    var bag = _w.ReadyBag(_me);
                    bool ready = bag != null && _w.Time >= bag.ReadyAt;
                    bool wait = bag != null && !ready && bag.ReadyAt - _w.Time < 75 && _deadTime < 90;
                    // у спальника гибну раз за разом (холод, кемпер у двери) — проснуться на пляже
                    int near = 0;
                    if (bag != null) foreach (var (pos, at) in _deathSpots) if (_w.Time - at < 600 && Vector2.Distance(pos, bag.Pos) < 25) near++;
                    if (near >= 2 && ready) { ready = false; wait = false; Say("спальник-опасно", "у спальника гибну раз за разом — проснусь на пляже", 300); }
                    if (!wait) { cmd.RespawnBag = ready; cmd.Respawn = !ready; _deadTime = 0; _node = null; }
                }
                return cmd;
            }

            long t0 = SimPerf.Begin();
            Perceive();
            SimPerf.End("бот: восприятие", t0);
            t0 = SimPerf.Begin();
            Decide(ref cmd);
            CloseDoorBehind();
            Clothe(ref cmd);
            if ((_w.Tick + (uint)_me.Id) % 300 == 7) TidyPack();   // раз в 10 с: старые ключи и лишнее тряпьё из рюкзака (D49)
            SimPerf.End("бот: решение+действие", t0);
            NoteMove(cmd);
            return cmd;
        }

        /// <summary>Лечение, вода, еда. Раненый держит сытость повыше — тогда здоровье восстанавливается само.</summary>
        bool Needs(ref InputCmd cmd)
        {
            if (WarmUp(ref cmd)) return true;   // замёрз — греться (D47)
            bool hurt = _me.Hp < _me.MaxHp - 5;
            // ранен и нечем лечиться — бинт из ткани (конопля)
            if (_me.Hp < 60 && _me.Count("bandage") == 0)
            {
                if (_me.Count("cloth") >= 4) { if (Craft("bandage", ref cmd)) return true; }
                else { Doing = "ищет коноплю на бинт"; if (UsePickup(PickupKind.Hemp, ref cmd)) return true; }
            }
            if (_me.Hp < 70 && _me.Count("bandage") > 0) cmd.Heal = true;
            if (_me.Food < (hurt ? 60 : 35) && (_me.Count("berries") > 0 || _me.Count("canned") > 0)) cmd.Eat = true;
            if (_me.Water < (hurt ? 50 : 30)) { Doing = "идёт пить"; if (UsePickup(PickupKind.WaterBarrel, ref cmd)) return true; }
            if (_me.Food < (hurt ? 45 : 30) && _me.Count("berries") == 0) { Doing = "ищет ягоды"; if (UsePickup(PickupKind.Berries, ref cmd)) return true; }
            return false;
        }

        bool _int;
        bool Click2() { _int = !_int; return _int; }

        // ---------- прогрессия: верстаки и изучение ----------

        int _metalGoal = 300;
        // ветка оружия D46: Наган → Самоделка (тир 1), 7,62 → Грач / СВТ (тир 2), АКМ / СВД (тир 3); дробовики и элиту не учат — их только находят
        static readonly string[] ResearchOrder = { "lock_code", "gunpowder", "ammo_9mm", "weapon_nagant", "weapon_homesmg", "ammo_12g", "weapon_crossbow", "weapon_grach", "ammo_762", "weapon_svt40", "expl_satchel", "weapon_akm", "weapon_svd", "expl_c4" };
        // агрессивные (бродяги и задиристые фермеры) учат бомбы раньше патронов 7,62: рейды — на втором часу, а не «никогда»
        static readonly string[] ResearchOrderRaider = { "lock_code", "gunpowder", "ammo_9mm", "weapon_nagant", "weapon_homesmg", "ammo_12g", "expl_satchel", "weapon_crossbow", "weapon_grach", "ammo_762", "weapon_svt40", "weapon_akm", "expl_c4", "weapon_svd" };
        string[] MyResearchOrder => Aggression > 0.45f ? ResearchOrderRaider : ResearchOrder;
        static readonly string[] BenchIds = { "deploy_workbench", "deploy_workbench2", "deploy_workbench3" };

        /// <summary>Где в доме стоят верстаки (мастерская мейна) и откуда с ними работать.</summary>
        Vector2 BenchSpot(int level) => AtPlan(_plan.Benches[Math.Clamp(level - 1, 0, 2)]);
        Vector2 BenchStand => BenchStandFor();   // верстак на площадке (D52) — работать рядом с ним

        int MyBenchLevel() { ScanMine(); return _mBench; }

        int InBox(Deployable box, string id)
        {
            int n = 0;
            if (box.OwnerId == _me.Id && _mBoxes.Contains(box)) { foreach (var b in _mBoxes) n += InOne(b, id); return n; }   // свои — все ящики
            return InOne(box, id);
        }
        static int InOne(Deployable box, string id)
        {
            int n = 0;
            foreach (var it in box.Storage.Items) if (it.Id == id) n += it.Count;
            return n;
        }
        /// <summary>Свой ящик, где лежит предмет (больше всего); null — нигде.</summary>
        Deployable BoxWith(string id, bool gun = false)
        {
            ScanMine();
            Deployable best = null; int bn = 0;
            foreach (var b in _mBoxes)
            {
                int n = gun ? (b.Storage.Items.Exists(s => s.Id == id && s.State != null && !s.State.Broken) ? 1 : 0) : InOne(b, id);
                if (n > bn) { bn = n; best = b; }
            }
            return best;
        }

        /// <summary>Нужно n штук в кармане: забрать из ящика (E — всё), иначе false.</summary>
        bool TakeFromBox(Deployable box, string id, int n, ref InputCmd cmd)
        {
            if (_me.Count(id) >= n || InBox(box, id) == 0) return false;
            if (box.OwnerId == _me.Id && BoxWith(id) is Deployable src) box = src;   // из того своего ящика, где оно лежит
            Doing = $"берёт из ящика: {Items.Name(id)}";
            if (!Reach(box.Pos, ref cmd)) return true;
            cmd.Aim = box.Pos;
            _w.BotTake(_me, box, id, n - _me.Count(id));   // только нужное, а не всё (иначе «сложить — забрать» по кругу)
            Reserve(id, n);                                 // взял под дело — сейч не отнесёт обратно
            return true;
        }

        /// <summary>true — бот занят прогрессией в этот тик; false — всё сделано или ждём металл из печи.</summary>
        bool Progress(Deployable box, int metal, ref InputCmd cmd)
        {
            int lvl = MyBenchLevel();
            int scrap = _me.Count("scrap") + InBox(box, "scrap");

            // изучить, что доступно на нашем уровне
            foreach (var id in MyResearchOrder)
            {
                var n = Tech.Get(id);
                if (n == null || _me.Known.Contains(id) || n.Bench > lvl || !Array.TrueForAll(n.Requires, r => _me.Known.Contains(r))) continue;
                if (n.RequiresAny.Length > 0 && !Array.Exists(n.RequiresAny, r => _me.Known.Contains(r))) continue;
                // не тратить лом раньше времени: 12 к — когда есть дробовик, арбалет (ступень к СВТ) — с верстака 2
                if (id == "ammo_12g" && BestOwnedGun() is not ("weapon_pipeshotgun" or "weapon_sawnoff" or "weapon_mp133" or "weapon_saiga12")) continue;
                if (id == "weapon_crossbow" && lvl < 2) continue;
                if (scrap < n.Scrap) { WantScrap(n.Scrap - scrap); return GetScrap(n.Scrap - InBox(box, "scrap"), box, ref cmd); }
                if (TakeFromBox(box, "scrap", n.Scrap, ref cmd)) return true;
                Doing = $"изучает: {Items.Name(id)}";
                if (!GoTo(BenchStand, 0.6f, ref cmd)) return true;
                cmd.Aim = BenchSpot(lvl);
                cmd.Research = id;
                return true;
            }

            // следующий верстак
            if (lvl >= 3) return false;
            string bench = BenchIds[lvl];
            if (Has(bench))
            {
                Doing = $"ставит {Items.Name(bench)}";
                if (PlaceDeploy(bench, BenchSpot(lvl + 1), ref cmd)) return true;   // в слот мастерской (D52), нет места — на площадку рядом
                return false;
            }
            var r = Recipes.All[Recipe(bench)];
            foreach (var (id, n) in r.Cost)
            {
                if (_me.Count(id) >= n) continue;
                if (TakeFromBox(box, id, n, ref cmd)) return true;
                if (id == "scrap") { WantScrap(n - _me.Count("scrap")); return GetScrap(n, box, ref cmd); }
                if (id == "metal_fragments") { _metalGoal = Math.Max(_metalGoal, n + 60); Doing = $"копит металл на {Items.Name(bench)} ({metal}/{n})"; return metal >= n ? false : false; }
                // детали (верстак 3: платы ×3, шестерни ×5) — с РТ и со станка: заказать и заняться другим (D49)
                if (id.StartsWith("comp_")) { _partsWant[id] = Math.Max(_partsWant.GetValueOrDefault(id), n - Stock(id)); _partsWantAt = _w.Time; return false; }
                return !Need(id, n, ref cmd) || true;
            }
            // всё есть — крафтим у текущего верстака
            if (lvl > 0 && !GoTo(BenchStand, 0.6f, ref cmd)) { Doing = $"идёт крафтить {Items.Name(bench)}"; return true; }
            if (!Craft(bench, ref cmd)) Doing = $"не может скрафтить {Items.Name(bench)}";
            return true;
        }

        // ---------- вооружение: ствол лучше нынешнего, если есть детали; патроны; ремонт (D46) ----------

        /// <summary>Что бот крафтит, сильное первым. Дробовики и элита не крафтятся — только лут.</summary>
        static readonly string[] CraftGuns = { "weapon_akm", "weapon_svd", "weapon_an94", "weapon_ppsh", "weapon_svt40", "weapon_pps43", "weapon_grach", "weapon_homesmg", "weapon_nagant", "weapon_crossbow" };

        /// <summary>Каких деталей не хватает на следующий ствол (для походов на РТ): id → сколько.</summary>
        readonly Dictionary<string, int> _partsWant = new();
        float _partsWantAt = -999;

        static bool IsPart(string id) => id.StartsWith("comp_");

        /// <summary>Лучший свой ствол (даже без патронов), не сломанный.</summary>
        string BestOwnedGun()
        {
            string best = null; float bp = -1;
            for (int k = 0; k < Actor.BeltSize + Actor.PackSize; k++)
            {
                var s = k < Actor.BeltSize ? _me.Belt[k] : _me.Pack[k - Actor.BeltSize];
                if (s?.State is not WeaponState st || st.Def.Melee || st.Broken) continue;
                float p = Weaponry.Power(s.Id);
                if (p > bp) { bp = p; best = s.Id; }
            }
            return best;
        }

        WeaponState OwnedState(string id)
        {
            int s = SlotOf(id);
            if (s >= 0) return _me.Belt[s].State;
            int p = PackOf(id);
            return p >= 0 ? _me.Pack[p].State : null;
        }

        /// <summary>Отладка темпа (D46): верстак, лучший ствол, детали (при себе + в ящике), чего не хватает на следующий ствол.</summary>
        public string ArmsReport()
        {
            var parts = new List<string>();
            foreach (var id in new[] { "comp_spring", "comp_pipe", "comp_tape", "comp_gears", "comp_trigger", "comp_bolt_carrier", "comp_barrel_rifled", "comp_frame_pistol", "comp_body_smg", "comp_body_rifle", "comp_receiver_ak", "comp_barrel_chrome", "comp_gas_block", "comp_oil" })
                if (Stock(id) > 0) parts.Add($"{id.Substring(5)} {Stock(id)}");
            string want = _partsWant.Count > 0 ? " · не хватает: " + string.Join(", ", new List<string>(_partsWant.Keys)) : "";
            return $"верстак {MyBenchLevel()}, ствол {BestOwnedGun() ?? "-"}, металл {MetalTotal()}, лом {Stock("scrap")}, изучено {_me.Known.Count} · детали: {string.Join(", ", parts)}{want}";
        }

        /// <summary>Ствол лучше нынешнего (если изучен и есть все детали), патроны к нему, ремонт маслом. true — занят этим.</summary>
        bool Arm(Deployable box, Deployable furnace, ref InputCmd cmd)
        {
            int lvl = MyBenchLevel();
            if (lvl < 1) return false;
            string own = BestOwnedGun();
            float have = own != null ? Weaponry.Power(own) : 0;
            if (_w.Time - _partsWantAt > 60) _partsWant.Clear();
            foreach (var g in CraftGuns)
            {
                if (Weaponry.Power(g) <= have + 0.01f) break;
                if (!_me.Known.Contains(g) || Recipe(g) < 0) continue;
                var r = Recipes.All[Recipe(g)];
                if (r.Bench > lvl) continue;
                bool parts = true;
                foreach (var (id, n) in r.Cost)
                    if (IsPart(id) && Stock(id) < n) { parts = false; _partsWant[id] = Math.Max(_partsWant.GetValueOrDefault(id), n - Stock(id)); _partsWantAt = _w.Time; }
                if (!parts) continue;
                Doing = $"собирает {Items.Name(g)}";
                return CraftAtBench(g, box, ref cmd);
            }
            if (LatheWork(box, ref cmd)) return true;   // токарный станок: недостающие детали из сырья (PlayerBot.Lathe.cs)
            if (own == null) return false;
            var st = OwnedState(own);
            // ремонт: износ ниже трети, есть масло — к верстаку
            if (st != null && st.Def.MaxDurability > 0 && st.Durability < st.Def.MaxDurability * 0.35f && Stock("comp_oil") > 0)
            {
                if (TakeFromBox(box, "comp_oil", 1, ref cmd)) return true;
                Doing = $"чинит {Items.Name(own)} маслом";
                if (!GoTo(BenchStand, 0.6f, ref cmd)) return true;
                _w.Repair(_me, st);
                return true;
            }
            string ammo = st?.Def.AmmoId;
            if (ammo == null || ammo == "arrow" || _me.Count(ammo) >= 20 || !_me.Known.Contains(ammo) && Tech.Get(ammo) != null) return false;
            int ri = Recipe(ammo);
            if (ri < 0 || Recipes.All[ri].Bench > lvl) return false;
            if (TakeFromBox(box, ammo, 20, ref cmd)) return true;
            int powder = 0;
            foreach (var (id, n) in Recipes.All[ri].Cost) if (id == "gunpowder") powder = n;
            if (powder == 0 || _me.Count("gunpowder") >= powder) return CraftAtBench(ammo, box, ref cmd);
            if (TakeFromBox(box, "gunpowder", powder, ref cmd)) return true;
            return MakeGunpowder(powder, box, furnace, ref cmd);
        }

        /// <summary>Порох до target: уголь 15 + сера 10 у верстака; сера и уголь — из печи (серная руда + дерево).</summary>
        bool MakeGunpowder(int target, Deployable box, Deployable furnace, ref InputCmd cmd)
        {
            if (_me.Count("gunpowder") >= target) return false;
            if (_me.Count("charcoal") >= 15 && _me.Count("sulfur") >= 10) return CraftAtBench("gunpowder", box, ref cmd);
            if (TakeFromBox(box, "sulfur", 10, ref cmd) || TakeFromBox(box, "charcoal", 15, ref cmd)) return true;
            if (furnace == null) return false;
            // сера и уголь — из печи: загрузить серную руду и дерево, забрать готовое
            if (_me.Count("sulfur") < 10 && _me.Count("sulfur_ore") < 40 && furnace.SulfurOre + furnace.OutSulfur < 10)
                return !Need("sulfur_ore", 40, ref cmd) || true;
            Doing = "делает порох: ждёт серу и уголь из печи";
            if (furnace.OutSulfur > 0 || furnace.OutCharcoal >= 15 || (!furnace.Burning && _me.Count("sulfur_ore") > 0))
            {
                Use(furnace, furnace.Pos, ref cmd, false, 1.2f);
                return true;
            }
            return false;   // печь работает — пока фармим
        }

        // ---------- рейд: см. PlayerBot.Raid.cs (честная память о чужих базах, пушить/отступать, лут по ценности) ----------

        public bool InRaid => _raid != null;

        /// <summary>Точка снаружи двери: со стороны клетки без фундамента.</summary>
        Vector2 DoorOutside(Structure door) => door.Center + _w.OutwardOf(door.A, door.B) * 1.6f;

        /// <summary>Скрафтить у своего верстака: ресурсы из кармана или ящика.</summary>
        bool CraftAtBench(string output, Deployable box, ref InputCmd cmd)
        {
            var r = Recipes.All[Recipe(output)];
            foreach (var (id, n) in r.Cost)
            {
                if (_me.Count(id) >= n) continue;
                if (TakeFromBox(box, id, n, ref cmd)) return true;
                if (id == "scrap") return GetScrap(n, box, ref cmd);
                if (id == "metal_fragments") { _metalGoal = Math.Max(_metalGoal, n + 60); return false; }
                return !Need(id, n, ref cmd) || true;
            }
            if (r.Bench > 0 && !GoTo(BenchStand, 0.6f, ref cmd)) { Doing = $"идёт крафтить {Items.Name(output)}"; return true; }
            if (!Craft(output, ref cmd)) Doing = $"не может скрафтить {Items.Name(output)}";
            return true;
        }

        // ---------- металлолом: бочки, кучи хлама, сумки ----------

        ResourceNode _scrapNode;
        float _scrapTimer;
        readonly HashSet<ResourceNode> _badScrap = new();

        /// <summary>Набрать n лома: сначала подобрать выпавшее рядом, потом ближайшие кучи и бочки, подальше от охраны.</summary>
        bool GetScrap(int n, Deployable box, ref InputCmd cmd)
        {
            if (_me.Count("scrap") >= n) return false;
            // сумка рядом (из разбитой бочки) — подобрать
            foreach (var b in _w.Bags)
            {
                if (b.Empty || b.IsCrate || Vector2.Distance(b.Pos, _me.Pos) > 8 || MyDrop(b)) continue;
                Doing = "подбирает хлам";
                Use(b, b.Pos, ref cmd);
                return true;
            }
            if (_scrapNode == null || !_w.Nodes.Contains(_scrapNode) || (_scrapTimer -= World.Dt) < 0)
            {
                _scrapNode = null;
                float best = float.MaxValue;
                foreach (var nd in _w.NodesInRadius(_me.Pos, 220, _nodeBuf))
                {
                    if (nd.Kind is not (NodeKind.Barrel or NodeKind.Junk) || _badScrap.Contains(nd) || Guarded(nd.Pos)) continue;
                    float d = Vector2.Distance(nd.Pos, _me.Pos) * (nd.Kind == NodeKind.Junk ? 0.6f : 1f);   // в куче лома больше
                    if (d < best) { best = d; _scrapNode = nd; }
                }
                _scrapTimer = 30;
                _nodeBest = float.MaxValue; _nodeNoProgress = 0;
                if (_scrapNode == null) { Doing = "ищет лом"; Wander(ref cmd); return true; }
            }
            if (NoReach(_scrapNode.Pos)) { _badScrap.Add(_scrapNode); _scrapNode = null; return true; }
            float dist = Vector2.Distance(_me.Pos, _scrapNode.Pos);
            if (dist < _nodeBest - 0.3f) { _nodeBest = dist; _nodeNoProgress = 0; }
            else if ((_nodeNoProgress += World.Dt) > 6f && dist > _scrapNode.Radius + 1.0f) { _badScrap.Add(_scrapNode); _scrapNode = null; return true; }
            Doing = _scrapNode.Kind == NodeKind.Junk ? $"ковыряет хлам (лом {_me.Count("scrap")}/{n})" : $"бьёт бочку (лом {_me.Count("scrap")}/{n})";
            if (!MoveTo(_scrapNode.Pos, _scrapNode.Radius + 0.9f, ref cmd)) return true;
            cmd.Aim = _scrapNode.Pos;
            string tool = _scrapNode.Kind == NodeKind.Junk ? (Has("tool_pickaxe") ? "tool_pickaxe" : "tool_rock")
                                                         : (Has("weapon_spear") ? "weapon_spear" : Has("tool_hatchet") ? "tool_hatchet" : "tool_rock");
            if (Hold(tool, ref cmd)) cmd.Fire = true;
            return true;
        }

        void WantScrap(int n) { _scrapWant = Math.Max(0, n); _scrapWantAt = _w.Time; }

        /// <summary>Рядом живая охрана РТ — туда без оружия не ходим.</summary>
        bool Guarded(Vector2 p)
        {
            foreach (var a in _w.Actors)
                if (a.Team == Team.Npc && !a.Dead && Vector2.DistanceSquared(a.Pos, p) < 32 * 32) return true;
            return false;
        }

        // ---------- роумер: охота на слабых, лут трупов, обход РТ ----------

        Actor _prey;
        float _preyTimer, _aimDrift, _roamBest = float.MaxValue, _roamStuck;
        Vector2 _roamTo;

        /// <summary>Сила «кита» на глаз: что в руках (калаш > самопал > лук > копьё > инструменты > камень).</summary>
        static float KitPower(Actor a)
        {
            var id = a.Held?.Id;
            if (a.Weapon != null && !a.Weapon.Def.Melee) return Weaponry.Power(id);
            return id switch { "weapon_spear" => 0.4f, "tool_hatchet" or "tool_pickaxe" => 0.25f, _ => 0.1f };
        }

        float MyPower => Ranged() is string g ? GunPower(g) : Has("weapon_spear") ? 0.4f : 0.15f;

        /// <summary>Лучшее дальнобойное с боезапасом и не сломанное (по силе: элита, тир 3 … лук).</summary>
        string Ranged()
        {
            string best = null; float bp = -1;
            for (int k = 0; k < Actor.BeltSize + Actor.PackSize; k++)
            {
                var s = k < Actor.BeltSize ? _me.Belt[k] : _me.Pack[k - Actor.BeltSize];
                if (s?.State is not WeaponState st || st.Def.Melee || st.Broken) continue;
                float p = Weaponry.Power(s.Id);
                if (p <= bp || (st.Ammo <= 0 && _me.Count(st.Def.AmmoId) <= 0)) continue;
                bp = p; best = s.Id;
            }
            return best;
        }

        /// <summary>Охота и отбивание: дальний бой с укрытиями и пиками (PlayerBot.Combat.cs), без ствола — вплотную.</summary>
        void Hunt(Actor prey, ref InputCmd cmd) => Combat(prey, ref cmd);

        /// <summary>Дойти до точки: маршрут A* сам ведёт через свои двери (откроет и закроет).</summary>
        bool GoTo(Vector2 p, float tol, ref InputCmd cmd) => MoveTo(p, tol, ref cmd);
    }
}
