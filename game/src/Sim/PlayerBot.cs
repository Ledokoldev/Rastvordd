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
            if (d.Length() <= tol) { _path = null; return true; }

            // прогресс к цели: 1,5 с без приближения — застряли
            if (Vector2.Distance(_goalFor, p) > 0.5f) { _goalFor = p; _goalBest = float.MaxValue; _goalStuck = 0; }
            if (d.Length() < _goalBest - 0.2f) { _goalBest = d.Length(); _goalStuck = 0; } else _goalStuck += World.Dt;

            // идём по маршруту
            CloseDoorBehind(ref cmd);
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
                if (path != null && path.Count > 0)
                {
                    _path = path; _pathIdx = 0; _pathGoal = p; _pathAge = 0; _goalStuck = 0;
                    cmd.Move = Vector2.Normalize(path[0] - _me.Pos + new Vector2(1e-4f, 0));
                    cmd.Aim = path[0];
                    return false;
                }
            }
            var want = Vector2.Normalize(d);
            // РТ с охраной обходим стороной, если цель не на самом РТ
            foreach (var m in _w.Monuments)
            {
                if (m.Contains(p, 5) || d.Length() < 15) continue;   // ближние перемещения (по дому, к дереву) — без отталкивания
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

        // ---------- свои двери: маршрут проходит сквозь них, бот открывает перед собой и закрывает за собой ----------

        bool MineDoor(Structure s) => s.Kind == StructKind.Door && (s.OwnerId == _me.Id || (_w.ClaimAt(Building.CellOf(s.Center)) is Deployable c && c.Auth.Contains(_me.Id)));
        bool OwnDoor(MapWall w) => w.Owner is Structure s && MineDoor(s);

        Structure _doorToClose;
        float _closeSide;

        static float SideOf(Structure door, Vector2 p)
        {
            var d = door.B - door.A;
            return MathF.Sign((p.X - door.A.X) * d.Y - (p.Y - door.A.Y) * d.X);
        }

        /// <summary>На пути к точке закрытая своя дверь — подойти и открыть.</summary>
        bool OpenDoorAhead(Vector2 wp, ref InputCmd cmd)
        {
            foreach (var s in _w.Structures)
            {
                if (s.Kind != StructKind.Door || s.Open || !MineDoor(s) || Vector2.DistanceSquared(s.Center, _me.Pos) > 9) continue;
                bool crosses = false;
                foreach (var b in s.Boxes) if (Geo.SegmentAabb(_me.Pos, wp, b.Wall.Min, b.Wall.Max, out _)) { crosses = true; break; }
                if (!crosses) continue;
                cmd.Aim = s.Center;
                if (Vector2.Distance(_me.Pos, s.Center) > 1.4f) { cmd.Move = Vector2.Normalize(s.Center - _me.Pos); return true; }
                if (_w.BotToggleDoor(_me, s)) { _doorToClose = s; _closeSide = SideOf(s, _me.Pos); }
                return true;
            }
            return false;
        }

        /// <summary>Прошёл дверь и отошёл — закрыть за собой (база не стоит нараспашку).</summary>
        void CloseDoorBehind(ref InputCmd cmd)
        {
            if (_doorToClose == null) return;
            var d = _doorToClose;
            float dist = Vector2.Distance(_me.Pos, d.Center);
            if (!d.Open || dist > 3f || !_w.Structures.Contains(d)) { _doorToClose = null; return; }
            if (SideOf(d, _me.Pos) != _closeSide && dist > 1.1f)
            {
                cmd.Aim = d.Center;
                _w.BotToggleDoor(_me, d);
                _doorToClose = null;
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
                _deadTime += World.Dt;
                if (_deadTime > 6) { cmd.RespawnBag = _w.ReadyBag(_me) is Deployable b && _w.Time >= b.ReadyAt; cmd.Respawn = !cmd.RespawnBag; _deadTime = 0; _node = null; }
                return cmd;
            }

            Perceive();
            Decide(ref cmd);
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
        static readonly string[] ResearchOrder = { "gunpowder", "ammo_pistol", "weapon_pipe", "ammo_rifle", "expl_satchel", "weapon_ak", "expl_c4" };
        static readonly string[] BenchIds = { "deploy_workbench", "deploy_workbench2", "deploy_workbench3" };

        /// <summary>Где у дома стоят верстаки (с востока, в ряд) и откуда с ними работать.</summary>
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
                if (scrap < n.Scrap) return GetScrap(n.Scrap, box, ref cmd);
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
                if (!GoTo(spot + new Vector2(1.6f, 0), 0.5f, ref cmd)) return true;
                cmd.Aim = spot;
                if (Hold(bench, ref cmd)) Click(ref cmd);
                return true;
            }
            var r = Recipes.All[Recipe(bench)];
            foreach (var (id, n) in r.Cost)
            {
                if (_me.Count(id) >= n) continue;
                if (TakeFromBox(box, id, n, ref cmd)) return true;
                if (id == "scrap") return GetScrap(n, box, ref cmd);
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

        // ---------- рейд: бомбы на дверь чужой базы, вынести ящик ----------

        Deployable _raidClaim;
        Structure _raidDoor;
        float _raidRetarget, _raidCooldown, _throwWait;
        bool _raidLooted;

        /// <summary>Дверь чужой базы, которую дешевле всего взорвать (прочность с учётом материала).</summary>
        (Deployable claim, Structure door) PickRaidTarget()
        {
            Deployable bestC = null; Structure bestD = null;
            float best = float.MaxValue;
            foreach (var c in _w.Deployables)
            {
                if (c.Kind != DeployKind.Claim || c.Auth.Contains(_me.Id) || c.OwnerId == _me.Id) continue;
                foreach (var s in _w.Structures)
                {
                    if (s.Kind != StructKind.Door || s.ClaimId != c.Id) continue;
                    float cost = BombsFor(s) * 1000 + Vector2.Distance(s.Center, _me.Pos);
                    if (cost < best) { best = cost; bestC = c; bestD = s; }
                }
            }
            return (bestC, bestD);
        }

        /// <summary>Сколько самодельных бомб нужно на постройку (с запасом на осечки).</summary>
        static int BombsFor(Structure s)
        {
            float perBomb = 75f * 0.95f * World.BlastTierMult[Math.Clamp(s.Tier, 0, 4)];
            return (int)MathF.Ceiling(s.Hp / perBomb * 1.25f);
        }

        bool Raid(Deployable box, Deployable furnace, ref InputCmd cmd)
        {
            if ((_raidCooldown -= World.Dt) > 0 || Ranged() == null || !_me.Known.Contains("expl_satchel") || MyBenchLevel() < 2) return false;
            if (_raidClaim == null || !_w.Deployables.Contains(_raidClaim) || (_raidRetarget -= World.Dt) < 0)
            {
                (_raidClaim, _raidDoor) = PickRaidTarget();
                _raidRetarget = 120;
                _raidLooted = false;
                if (_raidClaim == null) { _raidCooldown = 300; return false; }
            }
            var enemyBox = EnemyBox();
            bool doorDown = _raidDoor == null || !_w.Structures.Contains(_raidDoor);

            // 1. бомбы
            int need = doorDown ? 0 : BombsFor(_raidDoor);
            if (!doorDown && _me.Count("expl_satchel") < need && !InRaid)
            {
                var r = Recipes.All[Recipe("expl_satchel")];
                foreach (var (id, n) in r.Cost)
                {
                    if (_me.Count(id) >= n) continue;
                    if (id == "gunpowder") { Doing = $"копит порох на рейд ({_me.Count("expl_satchel")}/{need} бомб)"; return MakeGunpowder(n, box, furnace, ref cmd); }
                    if (TakeFromBox(box, id, n, ref cmd)) return true;
                    if (id == "cloth") { Doing = "собирает коноплю на бомбы"; return UsePickup(PickupKind.Hemp, ref cmd) || true; }
                    if (id == "metal_fragments") { _metalGoal = Math.Max(_metalGoal, 300); return false; }
                    return !Need(id, n, ref cmd) || true;
                }
                return CraftAtBench("expl_satchel", box, ref cmd);
            }

            // 2. дверь: бросать с внешней стороны, отходить и ждать взрыва
            if (!doorDown)
            {
                if (!InRaid) _w.Milestone(_me, $"идёт в рейд на базу {_w.Find(_raidClaim.OwnerId)?.Name ?? "?"} ({need} бомб)");
                InRaid = true;
                var outside = DoorOutside(_raidDoor);
                Doing = $"рейдит: дверь {Building.TierNames[_raidDoor.Tier]} {(int)_raidDoor.Hp}/{(int)_raidDoor.MaxHp}";
                if ((_throwWait -= World.Dt) > 0)
                {
                    // ждём взрыва в стороне, держа оружие
                    var back = outside + Vector2.Normalize(outside - _raidDoor.Center) * 4f;
                    MoveTo(back, 0.8f, ref cmd);
                    Hold(Ranged() ?? "tool_hatchet", ref cmd);
                    return true;
                }
                if (_me.Count("expl_satchel") == 0) { InRaid = false; _raidCooldown = 120; return false; }
                if (!MoveTo(outside, 0.5f, ref cmd)) return true;
                cmd.Aim = _raidDoor.Center;
                if (Hold("expl_satchel", ref cmd)) { Click(ref cmd); _throwWait = 6.5f; }
                return true;
            }

            // 3. дверь выбита — вынести ящик
            if (InRaid && !_raidLooted)
            {
                if (enemyBox == null || enemyBox.Storage.Empty) { _raidLooted = true; }
                else
                {
                    Doing = "выносит чужой ящик";
                    if (!Use(enemyBox, enemyBox.Pos, ref cmd)) return true;
                    if (Vector2.Distance(_me.Pos, enemyBox.Pos) < 1.6f && _w.Time - _lootTry > 2f)
                    {
                        _lootTry = _w.Time;
                        if (++_lootAttempts > 6) _raidLooted = true;
                    }
                    return true;
                }
                _w.Milestone(_me, $"зарейдил базу {_w.Find(_raidClaim.OwnerId)?.Name ?? "?"}");
            }
            if (InRaid) { InRaid = false; _raidCooldown = 900; _raidClaim = null; _lootAttempts = 0; }
            return false;
        }
        float _lootTry;
        int _lootAttempts;
        public bool InRaid;

        Deployable EnemyBox()
        {
            if (_raidClaim == null) return null;
            foreach (var d in _w.Deployables)
                if (d.Kind == DeployKind.Box && d.OwnerId == _raidClaim.OwnerId && d.Storage != null) return d;
            return null;
        }

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
                if (b.Empty || b.IsCrate || Vector2.Distance(b.Pos, _me.Pos) > 8) continue;
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

        /// <summary>Сила «кита» на глаз: что в руках (огнестрел > лук > копьё > инструменты > камень).</summary>
        static float KitPower(Actor a)
        {
            var id = a.Held?.Id;
            if (a.Weapon != null && !a.Weapon.Def.Melee) return id == "weapon_bow" ? 0.6f : 1f;
            return id switch { "weapon_spear" => 0.4f, "tool_hatchet" or "tool_pickaxe" => 0.25f, _ => 0.1f };
        }

        float MyPower => Ranged() is string g ? (g == "weapon_bow" ? 0.6f : 1f) : Has("weapon_spear") ? 0.4f : 0.15f;

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

        void Hunt(Actor prey, ref InputCmd cmd)
        {
            var to = prey.Pos - _me.Pos;
            float d = to.Length();
            var dir = d > 0.01f ? to / d : Vector2.UnitX;
            string gun = d > 3.5f ? Ranged() : null;
            Doing = $"{(Archetype == BotArchetype.Roamer ? "охотится на" : "отбивается от")} {prey.Name}";
            if (gun != null)
            {
                if (!Hold(gun, ref cmd)) return;
                var st = _me.Belt[SlotOf(gun)].State;
                if (st != null && st.Ammo <= 0 && st.ReloadLeft <= 0) { cmd.Reload = Click2(); return; }   // пусто — перезарядка
                // держит дистанцию 8–14 м и стрейфится
                var side = new Vector2(-dir.Y, dir.X) * (MathF.Sin(_w.Time * 0.9f + _me.Id) > 0 ? 1 : -1);
                var move = side * 0.6f + (d > 14 ? dir : d < 8 ? -dir * 0.7f : Vector2.Zero);
                cmd.Move = move.LengthSquared() > 0.01f ? Avoid(Vector2.Normalize(move)) : Vector2.Zero;
                _aimDrift = _aimDrift * 0.95f + ((float)_rng.NextDouble() - 0.5f) * 0.25f;
                cmd.Aim = prey.Pos + prey.Vel * (d / 45f) + new Vector2(-dir.Y, dir.X) * _aimDrift * d * 0.06f;
                cmd.Peek = 1;
                bool auto = st?.Def.Auto ?? false;
                cmd.Fire = _w.HasLineOfSight(_me.Pos, prey.Pos) && (auto ? MathF.Sin(_w.Time * 3f + _me.Id) > -0.3f : Click2());   // автомат — очередями
                return;
            }
            // копьё: вплотную
            string melee = Has("weapon_spear") ? "weapon_spear" : Has("tool_hatchet") ? "tool_hatchet" : "tool_rock";
            if (!Hold(melee, ref cmd)) return;
            cmd.Aim = prey.Pos;
            if (d > 1.8f) { cmd.Move = Avoid(dir); cmd.Sprint = d > 6; }
            else cmd.Fire = true;
        }

        /// <summary>Дойти до точки: маршрут A* сам ведёт через свои двери (откроет и закроет).</summary>
        bool GoTo(Vector2 p, float tol, ref InputCmd cmd) => MoveTo(p, tol, ref cmd);
    }
}
