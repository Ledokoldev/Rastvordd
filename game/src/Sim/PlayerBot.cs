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
        float _deadTime, _thinkFireToggle, _stuck, _avoidTimer, _avoidAngle, _fleeTime;
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

            // прогресс к цели: 1,5 с без приближения — застряли
            if (Vector2.Distance(_goalFor, p) > 0.5f) { _goalFor = p; _goalBest = float.MaxValue; _goalStuck = 0; }
            if (d.Length() < _goalBest - 0.2f) { _goalBest = d.Length(); _goalStuck = 0; } else _goalStuck += World.Dt;

            // идём по маршруту
            if (_path != null && Vector2.Distance(_pathGoal, p) < 1.5f && (_pathAge += World.Dt) < 15f && _goalStuck < 2.5f)
            {
                while (_pathIdx < _path.Count - 1 && Vector2.Distance(_me.Pos, _path[_pathIdx]) < 0.4f) _pathIdx++;
                var wp = _path[_pathIdx];
                if (_pathIdx < _path.Count - 1 || Vector2.Distance(_me.Pos, wp) > 0.4f)
                {
                    if (OpenDoorAhead(wp, ref cmd)) return false;
                    cmd.Move = Vector2.Normalize(wp - _me.Pos);
                    cmd.Aim = wp;
                    return false;
                }
                _path = null;   // дошли до конца маршрута (для дальней цели — до промежуточной точки)
            }
            else _path = null;

            // прямая закрыта (дом, дерево у угла, печь) или застряли — строим маршрут
            if ((_pathCheck -= World.Dt) <= 0 && (_goalStuck > 1.5f || (d.Length() < 35 && !_w.SegmentWalkable(_me.Pos, p, _me.Radius + 0.05f, tol + 0.4f))))
            {
                _pathCheck = 0.6f;
                var goal = d.Length() < 35 ? p : _me.Pos + Vector2.Normalize(d) * 25;
                var path = Pathfinder.Find(_w, _me.Pos, goal, _me.Radius, d.Length() < 35 ? tol : 1.5f, OwnDoor);
                // дальняя цель, а промежуточная точка за стеной (выход с РТ через ворота сбоку) — пробуем повёрнутые направления
                if (path == null && d.Length() >= 35)
                    foreach (float turn in new[] { 0.5f, -0.5f, 1.0f, -1.0f, 1.6f, -1.6f, 2.4f, -2.4f })
                    {
                        var g2 = _me.Pos + Geo.Rotate(Vector2.Normalize(d), turn) * 25;
                        if (!_w.Terrain.IsLand(g2)) continue;
                        path = Pathfinder.Find(_w, _me.Pos, g2, _me.Radius, 1.5f, OwnDoor);
                        if (path != null) break;
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
            if (OpenDoorAhead(p, ref cmd)) return false;   // напрямую: закрытую свою дверь — открыть, открытую — запомнить, чтобы закрыть за собой
            var want = Vector2.Normalize(d);
            // РТ с охраной обходим стороной, если цель не на самом РТ
            foreach (var m in _w.Monuments)
            {
                if (m.Contains(p, 5) || d.Length() < 15 || m.Contains(_me.Pos, 4)) continue;   // с самой РТ выходим без отталкивания (прижимало к забору)   // ближние перемещения (по дому, к дереву) — без отталкивания
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
            foreach (var s in _w.Structures)
            {
                if (s.Kind != StructKind.Door || !MineDoor(s) || Vector2.DistanceSquared(s.Center, _me.Pos) > 9) continue;
                bool crosses = false;
                foreach (var (min, max, _, _) in Building.Boxes(StructKind.Door, s.A, s.B, false)) if (Geo.SegmentAabb(_me.Pos, wp, min, max, out _)) { crosses = true; break; }
                if (!crosses) continue;
                if (s.Open) { if (Vector2.Distance(_me.Pos, s.Center) < 1.5f) NoteToClose(s); continue; }
                cmd.Aim = s.Center;
                if (Vector2.Distance(_me.Pos, s.Center) > 1.4f) { cmd.Move = Vector2.Normalize(s.Center - _me.Pos); return true; }
                if (_w.BotToggleDoor(_me, s)) NoteToClose(s);
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
                if ((SideOf(d, _me.Pos) != side && dist > 1.1f) || dist > 1.9f)
                    if (_w.BotToggleDoor(_me, d) || !d.Open) _toClose.RemoveAt(i);
            }
        }

        Vector2 Avoid(Vector2 move)
        {
            float moved = Vector2.Distance(_me.Pos, _lastPos);
            _lastPos = _me.Pos;
            if (moved < 0.02f) _stuck += World.Dt; else _stuck = MathF.Max(0, _stuck - World.Dt);
            if (_stuck > 0.35f && _avoidTimer <= 0)
            {
                _avoidAngle = (_rng.Next(2) == 0 ? -1 : 1) * (1.1f + (float)_rng.NextDouble() * 0.7f);
                _avoidTimer = 0.5f + (float)_rng.NextDouble() * 0.6f;
                _stuck = 0;
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

        Deployable MyClaim()
        {
            foreach (var d in _w.Deployables) if (d.Kind == DeployKind.Claim && d.OwnerId == _me.Id) return d;
            return null;
        }

        static NodeKind NodeFor(string id) => id switch { "wood" => NodeKind.Tree, "metal_ore" => NodeKind.Metal, "sulfur_ore" => NodeKind.Sulfur, _ => NodeKind.Stone };

        Deployable MyFurnace()
        {
            foreach (var d in _w.Deployables) if (d.Kind == DeployKind.Furnace && d.OwnerId == _me.Id) return d;
            return null;
        }

        Deployable MyBox()
        {
            foreach (var d in _w.Deployables) if (d.Kind == DeployKind.Box && d.OwnerId == _me.Id) return d;
            return null;
        }

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
            _stockId = id;
            _stockTarget = Math.Max(count, id == "wood" ? 350 : 250);
            Gather(NodeFor(id), ref cmd);
            return false;
        }

        /// <summary>Добыча: ближайший узел нужного вида, подальше от охраны. Инструмент — лучший из имеющихся.</summary>
        void Gather(NodeKind kind, ref InputCmd cmd)
        {
            string tool = kind == NodeKind.Tree ? (Has("tool_hatchet") ? "tool_hatchet" : "tool_rock")
                                                : (Has("tool_pickaxe") ? "tool_pickaxe" : "tool_rock");
            if (!Has(tool) && !Has("tool_rock"))
            {
                // нет даже камня — подбираем его крафтом? камень выдаётся при возрождении; просто ищем ягоды
                Doing = "без инструмента";
                return;
            }
            Doing = kind == NodeKind.Tree ? "рубит дерево" : "бьёт камень";
            if (_node == null || !_w.Nodes.Contains(_node) || _node.Kind != kind || (_nodeTimer -= World.Dt) < 0)
            {
                _node = null;
                float best = float.MaxValue;
                foreach (var n in _w.NodesInRadius(_me.Pos, 70, _nodeBuf))
                {
                    if (n.Kind != kind || _badNodes.Contains(n)) continue;
                    if (_w.NearMonument(n.Pos, 22)) continue;
                    float d = Vector2.Distance(n.Pos, _me.Pos);
                    if (d < best) { best = d; _node = n; }
                }
                _nodeTimer = 20;
                _nodeBest = float.MaxValue; _nodeNoProgress = 0;
                if (_node == null) { Wander(ref cmd); return; }
            }
            // не можем подойти к узлу — в чёрный список, ищем другой
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
            if (_me.Dead)
            {
                Doing = "мёртв";
                if (_rtPhase != RtPhase.None) EndRun("погиб", died: true);
                if (_raid != null) { RaidEnd("погиб", false); _raid = null; _raidCooldown = _w.Time + 900; }
                _deadTime += World.Dt;
                if (_deadTime > 6) { cmd.RespawnBag = _w.ReadyBag(_me) is Deployable b && _w.Time >= b.ReadyAt; cmd.Respawn = !cmd.RespawnBag; _deadTime = 0; _node = null; }
                return cmd;
            }

            Perceive();
            Decide(ref cmd);
            CloseDoorBehind();
            return cmd;
        }

        /// <summary>Лечение, вода, еда. Раненый держит сытость повыше — тогда здоровье восстанавливается само.</summary>
        bool Needs(ref InputCmd cmd)
        {
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
        static readonly string[] ResearchOrder = { "lock_code", "gunpowder", "ammo_pistol", "weapon_pipe", "ammo_rifle", "expl_satchel", "weapon_ak", "expl_c4" };
        static readonly string[] BenchIds = { "deploy_workbench", "deploy_workbench2", "deploy_workbench3" };

        /// <summary>Где в доме стоят верстаки (мастерская мейна) и откуда с ними работать.</summary>
        Vector2 BenchSpot(int level) => AtPlan(_plan.Benches[Math.Clamp(level - 1, 0, 2)]);
        Vector2 BenchStand => AtPlan(_plan.BenchStand);

        int MyBenchLevel()
        {
            int lvl = 0;
            foreach (var d in _w.Deployables) if (d.Kind == DeployKind.Workbench && d.OwnerId == _me.Id) lvl = Math.Max(lvl, Math.Max(1, d.Level));
            return lvl;
        }

        int InBox(Deployable box, string id)
        {
            int n = 0;
            foreach (var it in box.Storage.Items) if (it.Id == id) n += it.Count;
            return n;
        }

        /// <summary>Нужно n штук в кармане: забрать из ящика (E — всё), иначе false.</summary>
        bool TakeFromBox(Deployable box, string id, int n, ref InputCmd cmd)
        {
            if (_me.Count(id) >= n || InBox(box, id) == 0) return false;
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
            foreach (var id in ResearchOrder)
            {
                var n = Tech.Get(id);
                if (n == null || _me.Known.Contains(id) || n.Bench > lvl || !Array.TrueForAll(n.Requires, r => _me.Known.Contains(r))) continue;
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
                var spot = BenchSpot(lvl + 1);
                if (!Reach(spot, ref cmd, 1.3f)) return true;   // место — в мастерской внутри дома, подходим изнутри
                if (Hold(bench, ref cmd)) Click(ref cmd);
                return true;
            }
            var r = Recipes.All[Recipe(bench)];
            foreach (var (id, n) in r.Cost)
            {
                if (_me.Count(id) >= n) continue;
                if (TakeFromBox(box, id, n, ref cmd)) return true;
                if (id == "scrap") { WantScrap(n - _me.Count("scrap")); return GetScrap(n, box, ref cmd); }
                if (id == "metal_fragments") { _metalGoal = Math.Max(_metalGoal, n + 60); Doing = $"копит металл на {Items.Name(bench)} ({metal}/{n})"; return metal >= n ? false : false; }
                return !Need(id, n, ref cmd) || true;
            }
            // всё есть — крафтим у текущего верстака
            if (lvl > 0 && !GoTo(BenchStand, 0.6f, ref cmd)) { Doing = $"идёт крафтить {Items.Name(bench)}"; return true; }
            if (!Craft(bench, ref cmd)) Doing = $"не может скрафтить {Items.Name(bench)}";
            return true;
        }

        // ---------- вооружение: самопал и патроны ----------

        /// <summary>Самопал и 20 патронов: сера → печь (сера и уголь) → порох → патроны. true — занят этим.</summary>
        bool Arm(Deployable box, Deployable furnace, ref InputCmd cmd)
        {
            if (!_me.Known.Contains("weapon_pipe") || MyBenchLevel() < 1) return false;
            if (!Has("weapon_pipe")) return CraftAtBench("weapon_pipe", box, ref cmd);
            if (_me.Count("ammo_pistol") >= 20 || !_me.Known.Contains("ammo_pistol")) return false;
            if (_me.Count("gunpowder") >= 5) return CraftAtBench("ammo_pistol", box, ref cmd);
            if (TakeFromBox(box, "gunpowder", 5, ref cmd)) return true;
            return MakeGunpowder(5, box, furnace, ref cmd);
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
            if (a.Weapon != null && !a.Weapon.Def.Melee) return id == "weapon_bow" ? 0.6f : id == "weapon_pipe" ? 0.8f : 1f;
            return id switch { "weapon_spear" => 0.4f, "tool_hatchet" or "tool_pickaxe" => 0.25f, _ => 0.1f };
        }

        float MyPower => Ranged() is string g ? GunPower(g) : Has("weapon_spear") ? 0.4f : 0.15f;

        /// <summary>Лучшее дальнобойное с боезапасом: калаш, самопал, лук.</summary>
        string Ranged()
        {
            foreach (var (gun, ammo) in new[] { ("weapon_ak", "ammo_rifle"), ("weapon_pipe", "ammo_pistol"), ("weapon_bow", "arrow") })
            {
                int s = SlotOf(gun), p = PackOf(gun);
                var st = s >= 0 ? _me.Belt[s].State : p >= 0 ? _me.Pack[p].State : null;
                if ((s >= 0 || p >= 0) && ((st?.Ammo ?? 0) > 0 || _me.Count(ammo) > 0)) return gun;
            }
            return null;
        }

        /// <summary>Охота и отбивание: дальний бой с укрытиями и пиками (PlayerBot.Combat.cs), без ствола — вплотную.</summary>
        void Hunt(Actor prey, ref InputCmd cmd) => Combat(prey, ref cmd);

        /// <summary>Дойти до точки: маршрут A* сам ведёт через свои двери (откроет и закроет).</summary>
        bool GoTo(Vector2 p, float tol, ref InputCmd cmd) => MoveTo(p, tol, ref cmd);
    }
}
