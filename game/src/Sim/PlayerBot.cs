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

    public sealed class PlayerBot : IBrain
    {
        public BotArchetype Archetype = BotArchetype.Farmer;
        public Actor Me => _me;
        readonly Actor _me;
        readonly World _w;
        readonly Random _rng;
        public string Doing = "";              // для отладки и подписи над головой
        public string Debug => _site == null ? "без базы" : $"база ({_origin.i},{_origin.j}) внутри={InsideBase} узел={(_node == null ? "-" : $"{_node.Pos.X:0};{_node.Pos.Y:0}")}";

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

        bool Has(string id) => SlotOf(id) >= 0;

        /// <summary>Взять предмет в руки. true — уже в руках.</summary>
        bool Hold(string id, ref InputCmd cmd)
        {
            int s = SlotOf(id);
            if (s < 0) return false;
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
        bool MoveTo(Vector2 p, float tol, ref InputCmd cmd)
        {
            var d = p - _me.Pos;
            if (d.Length() <= tol) return true;
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

        Vector2 CellC(int di, int dj) => Building.CellCenter(_origin.i + di, _origin.j + dj);
        Vector2 DoorCenter => Building.EdgeCenter(_origin.i, _origin.j + 2, 0);
        bool InsideBase => _site != null && Building.CellOf(_me.Pos) is var c && c.i >= _origin.i && c.i <= _origin.i + 1 && c.j >= _origin.j && c.j <= _origin.j + 1;

        /// <summary>Шаблон дома 2×2: фундаменты, стены, проём на юг (в клетке (0,1)).</summary>
        IEnumerable<(StructKind kind, int i, int j, int dir)> Template()
        {
            int i0 = _origin.i, j0 = _origin.j;
            yield return (StructKind.Foundation, i0, j0, -1);
            yield return (StructKind.Foundation, i0 + 1, j0, -1);
            yield return (StructKind.Foundation, i0, j0 + 1, -1);
            yield return (StructKind.Foundation, i0 + 1, j0 + 1, -1);
            yield return (StructKind.Doorway, i0, j0 + 2, 0);   // проём первым: в дом всегда есть вход
            yield return (StructKind.Wall, i0, j0, 0);
            yield return (StructKind.Wall, i0 + 1, j0, 0);
            yield return (StructKind.Wall, i0, j0, 1);
            yield return (StructKind.Wall, i0, j0 + 1, 1);
            yield return (StructKind.Wall, i0 + 2, j0, 1);
            yield return (StructKind.Wall, i0 + 2, j0 + 1, 1);
            yield return (StructKind.Wall, i0 + 1, j0 + 2, 0);
        }

        Structure Built((StructKind kind, int i, int j, int dir) t) =>
            t.kind == StructKind.Foundation ? _w.FoundationAt(t.i, t.j) : _w.EdgeAt(t.i, t.j, t.dir);

        /// <summary>Куда встать и куда смотреть, чтобы молотком достать элемент (изнутри дома).</summary>
        (Vector2 stand, Vector2 face) HammerSpot((StructKind kind, int i, int j, int dir) t)
        {
            int di = t.i - _origin.i, dj = t.j - _origin.j;
            if (t.kind == StructKind.Foundation)
            {
                // из соседней клетки смотрим на центр этой
                var target = CellC(di, dj);
                var from = CellC(di == 0 ? 1 : 0, dj);
                if (di == 0 && dj == 0) from = CellC(0, 1);
                if (di == 1 && dj == 1) from = CellC(0, 1);
                return (from, target);
            }
            var c = Building.EdgeCenter(t.i, t.j, t.dir);
            // клетка дома, примыкающая к ребру изнутри
            var (ca, cb) = Building.EdgeCells(t.i, t.j, t.dir);
            var inside = ca.Item1 >= _origin.i && ca.Item1 <= _origin.i + 1 && ca.Item2 >= _origin.j && ca.Item2 <= _origin.j + 1 ? ca : cb;
            var stand = Building.CellCenter(inside.Item1, inside.Item2);
            stand += Vector2.Normalize(c - stand) * 0.2f;
            // точку занял ящик/спальник/столб — сдвигаемся вдоль стены от него
            var tangent = t.dir == 0 ? Vector2.UnitX : Vector2.UnitY;
            foreach (var d in _w.DeploysInRadius(stand, 1.1f, _depBuf))
            {
                if (Vector2.Distance(d.Pos, stand) > d.Radius + 0.6f) continue;   // сетка отдаёт соседей с запасом
                float side = Vector2.Dot(stand - d.Pos, tangent);
                stand += tangent * (side >= 0 ? 1 : -1) * 0.7f;
                break;
            }
            // не дальше 0,45 от центра клетки вдоль стены — иначе упрёмся в поперечную стену
            var cc = Building.CellCenter(inside.Item1, inside.Item2);
            float along = Math.Clamp(Vector2.Dot(stand - cc, tangent), -0.45f, 0.45f);
            stand = cc + tangent * along + (stand - cc - tangent * Vector2.Dot(stand - cc, tangent));
            return (stand, c);
        }

        bool ChooseSite()
        {
            var home = _me.Pos;
            for (int tries = 0; tries < 60; tries++)
            {
                float ang = (float)(_rng.NextDouble() * Math.PI * 2), dist = 15 + (float)_rng.NextDouble() * 45;
                var p = home + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * dist;
                var (ci, cj) = Building.CellOf(p);
                bool ok = true;
                for (int di = -1; di <= 2 && ok; di++)
                    for (int dj = -1; dj <= 3 && ok; dj++)
                        ok = _w.FoundationGeoOk(ci + di, cj + dj, out _);
                if (!ok || !_w.CanPlaceClaim(Building.CellCenter(ci, cj), out _)) continue;
                if (_w.NearMonument(p, 40)) continue;   // подальше от РТ с охраной
                _origin = (ci, cj);
                _site = Building.CellCenter(ci, cj);
                return true;
            }
            return false;
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
        void Wander(ref InputCmd cmd)
        {
            if (_wanderTo == default || Vector2.Distance(_wanderTo, _me.Pos) < 2)
            {
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
                if (p.Kind != kind || !p.Available) continue;
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
            if (GoTo(p.Pos, 1.2f, ref cmd)) cmd.Interact = true;
            return true;
        }

        // ---------- угроза ----------

        Actor Threat()
        {
            foreach (var a in _w.Actors)
            {
                if (a == _me || a.Dead) continue;
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

            if (Archetype == BotArchetype.Roamer && Roam(ref cmd)) return cmd;

            // угроза: уходим (к дому, если он есть), лечимся
            var threat = Threat();
            if (threat != null) _fleeTime = 3f + 4f * _caution;
            if (_fleeTime > 0)
            {
                _fleeTime -= World.Dt;
                Doing = "убегает";
                var from = threat?.Pos ?? _me.Pos - _me.FacingDir;
                var away = _me.Pos - from;
                if (away.LengthSquared() < 0.01f) away = Vector2.UnitX;
                cmd.Move = Avoid(Vector2.Normalize(away));
                cmd.Sprint = true;
                cmd.Aim = _me.Pos + Vector2.Normalize(away) * 3;
                if (_me.Hp < 60) cmd.Heal = true;
                return cmd;
            }

            // голод и жажда
            if (_me.Food < 35 && (_me.Count("berries") > 0 || _me.Count("canned") > 0)) cmd.Eat = true;
            if (_me.Water < 30) { Doing = "идёт пить"; if (UsePickup(PickupKind.WaterBarrel, ref cmd)) return cmd; }
            if (_me.Food < 30 && _me.Count("berries") == 0) { Doing = "ищет ягоды"; if (UsePickup(PickupKind.Berries, ref cmd)) return cmd; }

            Plan(ref cmd);
            return cmd;
        }

        void Plan(ref InputCmd cmd)
        {
            int wood = _me.Count("wood"), stone = _me.Count("stone");

            // 1. инструменты
            if (!Has("tool_hatchet")) { if (Craft("tool_hatchet", ref cmd)) { Gather(NodeKind.Tree, ref cmd); return; } }
            if (!Has("tool_pickaxe") && Has("tool_hatchet")) { if (Craft("tool_pickaxe", ref cmd)) { Gather(NodeKind.Stone, ref cmd); return; } }
            if (!Has("tool_hatchet") || !Has("tool_pickaxe"))
            {
                Gather(wood < 200 ? NodeKind.Tree : NodeKind.Stone, ref cmd);
                return;
            }
            // роумер базу пока не строит: добывает то, что просит снаряжение
            if (Archetype == BotArchetype.Roamer)
            {
                if (wood < 400) Gather(NodeKind.Tree, ref cmd);
                else if (stone < 200) Gather(NodeKind.Stone, ref cmd);
                else { Doing = "бродит"; Wander(ref cmd); }   // запас есть — не копит бесконечно
                return;
            }

            // 2. место и столб
            var claim = MyClaim();
            if (claim != null && _site == null)
            {
                // после загрузки или смерти: дом там, где мой столб
                var c = Building.CellOf(claim.Pos);
                _origin = c;
                _site = Building.CellCenter(c.i, c.j);
            }
            if (_site == null && !ChooseSite()) { Doing = "ищет место"; Wander(ref cmd); return; }
            if (claim == null)
            {
                if (!Has("deploy_claim")) { if (!Craft("deploy_claim", ref cmd)) Gather(NodeKind.Tree, ref cmd); return; }
                Doing = "ставит шкаф";
                // столб — в северо-западный угол, чтобы не мешал ходить по дому
                if (!MoveTo(CellC(0, 0) + new Vector2(0.3f, 1.2f), 0.6f, ref cmd)) return;
                cmd.Aim = CellC(0, 0) + new Vector2(-0.55f, -0.55f);
                if (Hold("deploy_claim", ref cmd)) Click(ref cmd);
                return;
            }

            // 2б. рейд: ломают дом. Рейдер сильнее — держимся в стороне; рейдер ушёл/слабый — идём чинить (ниже)
            if (_w.Time - claim.RaidedAt < 60)
            {
                var raider = _w.Find(claim.RaiderId);
                if (raider != null && !raider.Dead && Vector2.Distance(raider.Pos, CellC(1, 1)) < 45 && KitPower(raider) > MyPower)
                {
                    Doing = "дом рейдят — пережидает в стороне";
                    var away = _me.Pos - raider.Pos;
                    if (away.LengthSquared() < 0.01f) away = Vector2.UnitX;
                    if (Vector2.Distance(_me.Pos, raider.Pos) < 55) { cmd.Move = Avoid(Vector2.Normalize(away)); cmd.Sprint = true; cmd.Aim = _me.Pos + Vector2.Normalize(away) * 3; }
                    return;
                }
            }

            // 3. план и молоток
            if (!Has("build_plan")) { if (!Craft("build_plan", ref cmd)) Gather(NodeKind.Tree, ref cmd); return; }
            if (!Has("tool_hammer")) { if (!Craft("tool_hammer", ref cmd)) Gather(NodeKind.Tree, ref cmd); return; }

            // 4. шаблон: ставим недостающее (снаружи, с юга)
            foreach (var t in Template())
            {
                if (Built(t) != null) continue;
                var cost = Building.Cost(t.kind, 0);
                if (!Need(cost[0].id, cost[0].n, ref cmd)) return;
                Doing = $"строит: {Building.KindName(t.kind)}";
                var aim = t.kind == StructKind.Foundation ? Building.CellCenter(t.i, t.j) : Building.EdgeCenter(t.i, t.j, t.dir);
                // ставим с места, если дотягиваемся; иначе — изнутри дома, из соседней с элементом клетки
                var stand = t.kind == StructKind.Foundation ? Building.CellCenter(t.i, t.j) : HammerSpot(t).stand;
                if (Vector2.Distance(_me.Pos, aim) > 4.5f && !GoTo(stand, 0.7f, ref cmd)) return;
                if (!Hold("build_plan", ref cmd)) return;
                cmd.Aim = aim;
                if (_me.BuildPiece != t.kind) { cmd.Reload = !_fireEdge; _fireEdge = !_fireEdge; return; }
                Click(ref cmd);
                return;
            }

            // 5. улучшить до дерева (проём — до двери: потом молоток попадает в дверь)
            const int targetTier = 1;   // сначала весь дом в дерево; камень — когда база готова (шаг 8б)
            foreach (var t in Template())
            {
                var s = Built(t);
                if (s == null || s.Tier >= targetTier) continue;
                if (t.kind == StructKind.Doorway && _w.DoorAt(t.i, t.j, 0) != null) continue;
                var cost = Building.Cost(t.kind, s.Tier + 1);
                if (!Need(cost[0].id, cost[0].n, ref cmd)) return;
                Doing = $"улучшает: {Building.KindName(t.kind)}";
                var (stand, face) = HammerSpot(t);
                if (!GoTo(stand, 0.6f, ref cmd)) return;
                cmd.Aim = face;
                if (Hold("tool_hammer", ref cmd) && Vector2.Dot(_me.FacingDir, Vector2.Normalize(face - _me.Pos)) > 0.9f) cmd.Fire = true;
                return;
            }

            // 5б. ремонт после рейда: повреждённое (ниже 85%) — молотком, ресурсы — из кармана/добыть
            var repairList = new List<((StructKind kind, int i, int j, int dir) t, Structure s)>();
            foreach (var t in Template())
                if (Built(t) is Structure bs && !(t.kind == StructKind.Doorway && _w.DoorAt(t.i, t.j, 0) != null)) repairList.Add((t, bs));   // проём с дверью — молоток попадёт в дверь
            if (_w.DoorAt(_origin.i, _origin.j + 2, 0) is Structure dr) repairList.Add(((StructKind.Door, dr.I, dr.J, 0), dr));
            foreach (var (t, s) in repairList)
            {
                if (s.Hp >= s.MaxHp * 0.85f) continue;
                if (!Has("tool_hammer")) break;
                var rc = Building.Cost(t.kind, s.Tier);
                if (!Need(rc[0].id, Math.Max(1, rc[0].n / 5), ref cmd)) return;
                Doing = $"чинит: {Building.KindName(t.kind)}";
                var (stand, face) = t.kind == StructKind.Door ? (CellC(0, 1) + new Vector2(0, 0.2f), DoorCenter) : HammerSpot(t);
                if (!GoTo(stand, 0.6f, ref cmd)) return;
                cmd.Aim = face;
                if (Hold("tool_hammer", ref cmd) && Vector2.Dot(_me.FacingDir, Vector2.Normalize(face - _me.Pos)) > 0.9f) cmd.Fire = true;
                return;
            }

            // 6. дверь
            if (_w.DoorAt(_origin.i, _origin.j + 2, 0) == null)
            {
                if (!Need("wood", 50, ref cmd)) return;
                Doing = "ставит дверь";
                if (!GoTo(CellC(0, 1) + new Vector2(0.3f, -0.4f), 0.4f, ref cmd)) return;
                if (!Hold("build_plan", ref cmd)) return;
                cmd.Aim = DoorCenter;
                if (_me.BuildPiece != StructKind.Door) { cmd.Reload = !_fireEdge; _fireEdge = !_fireEdge; return; }
                Click(ref cmd);
                return;
            }

            // 7. ящик
            var box = MyBox();
            if (box == null)
            {
                if (!Has("deploy_box")) { if (!Craft("deploy_box", ref cmd)) Gather(NodeKind.Tree, ref cmd); return; }
                Doing = "ставит ящик";
                if (!GoTo(CellC(1, 1), 0.5f, ref cmd)) return;
                cmd.Aim = CellC(1, 0) + new Vector2(0.33f, -0.33f);   // ящик в северо-восточный угол, не мешает ходить
                if (Hold("deploy_box", ref cmd)) Click(ref cmd);
                return;
            }

            // 7а. спальник дома: после смерти — проснуться у себя, а не на пляже
            if (_w.ReadyBag(_me) == null)
            {
                if (!Has("deploy_sleepbag"))
                {
                    if (Craft("deploy_sleepbag", ref cmd)) return;
                    if (_me.Count("cloth") < 30) { Doing = "собирает коноплю"; if (UsePickup(PickupKind.Hemp, ref cmd)) return; }
                }
                else
                {
                    Doing = "стелет спальник";
                    if (!GoTo(CellC(0, 1) + new Vector2(0.3f, 0), 0.5f, ref cmd)) return;
                    cmd.Aim = CellC(1, 1) + new Vector2(0.1f, 0.1f);
                    if (Hold("deploy_sleepbag", ref cmd)) Click(ref cmd);
                    return;
                }
            }

            // 7б. замок с ключом на дверь (ключ остаётся в кармане; убьют — заберут и откроют)
            var myDoor = _w.DoorAt(_origin.i, _origin.j + 2, 0);
            if (myDoor != null && !myDoor.Locked)
            {
                if (!Has("lock_key")) { if (!Craft("lock_key", ref cmd)) Need("wood", 75, ref cmd); return; }
                Doing = "ставит замок";
                if (!GoTo(CellC(0, 1) + new Vector2(0.2f, 0.1f), 0.5f, ref cmd)) return;
                cmd.Aim = DoorCenter;
                if (Hold("lock_key", ref cmd)) Click(ref cmd);
                return;
            }

            // 8. содержание: запас на ~2 часа
            if (_w.ClaimHoursLeft(claim) < 1.5f && _w.ClaimLoad(claim).b > 0)
            {
                foreach (var (id, perHour) in _w.UpkeepPerHourOf(claim))
                    if (!Need(id, (int)MathF.Ceiling(perHour * 2), ref cmd)) return;
                Doing = "кормит шкаф";
                if (!GoTo(claim.Pos + new Vector2(0.6f, 0.6f), 0.35f, ref cmd)) return;   // вплотную к столбу, дальше от двери
                cmd.Aim = claim.Pos;
                cmd.Interact = Click2();
                return;
            }

            // 8б. дом в камень: камень из кармана, не хватает — забираем из ящика, иначе добываем
            bool stoneDone = true;
            var upgrades = new List<((StructKind kind, int i, int j, int dir) t, Structure s)>();
            foreach (var t in Template())
                if (Built(t) is Structure s && s.Tier < 2 && !(t.kind == StructKind.Doorway && _w.DoorAt(t.i, t.j, 0) != null)) upgrades.Add((t, s));
            if (myDoor != null && myDoor.Tier < 2) upgrades.Add(((StructKind.Door, myDoor.I, myDoor.J, 0), myDoor));
            if (upgrades.Count > 0)
            {
                stoneDone = false;
                var (t, s) = upgrades[0];
                var cost = Building.Cost(t.kind, s.Tier + 1);
                var (id, n) = cost[0];
                if (_me.Count(id) < n)
                {
                    int inBox = 0;
                    foreach (var it in box.Storage.Items) if (it.Id == id) inBox += it.Count;
                    if (inBox >= n - _me.Count(id))
                    {
                        Doing = "берёт камень из ящика";
                        if (!GoTo(CellC(1, 0) + new Vector2(-0.35f, 0.35f), 0.4f, ref cmd)) return;
                        cmd.Aim = box.Pos;
                        cmd.Interact = Click2();
                        return;
                    }
                    if (!Need(id, n, ref cmd)) return;
                }
                Doing = $"в камень: {Building.KindName(t.kind)}";
                Vector2 stand, face;
                if (t.kind == StructKind.Door) { stand = CellC(0, 1) + new Vector2(0, 0.2f); face = DoorCenter; }
                else (stand, face) = HammerSpot(t);
                if (!GoTo(stand, 0.6f, ref cmd)) return;
                cmd.Aim = face;
                if (Hold("tool_hammer", ref cmd) && Vector2.Dot(_me.FacingDir, Vector2.Normalize(face - _me.Pos)) > 0.9f) cmd.Fire = true;
                return;
            }

            // 8в. печь у дома: металл на будущие улучшения (руда → печь → металл → в ящик)
            if (stoneDone)
            {
                int metal = _me.Count("metal_fragments");
                foreach (var it in box.Storage.Items) if (it.Id == "metal_fragments") metal += it.Count;
                var furnace = MyFurnace();
                if (furnace == null && metal < 300)
                {
                    if (!Has("deploy_furnace"))
                    {
                        if (Craft("deploy_furnace", ref cmd)) return;
                        if (!Need("stone", 100, ref cmd) || !Need("wood", 50, ref cmd)) return;
                        return;
                    }
                    Doing = "ставит печь";
                    var spot = CellC(1, 2) + new Vector2(0.6f, 1.0f);
                    if (!GoTo(spot + new Vector2(-1.2f, 0), 0.5f, ref cmd)) return;
                    cmd.Aim = spot;
                    if (Hold("deploy_furnace", ref cmd)) Click(ref cmd);
                    return;
                }
                if (furnace != null)
                {
                    bool ready = (!furnace.Burning && furnace.OutMetal + furnace.OutCharcoal > 0) || furnace.OutMetal >= 40;   // забираем пачкой, а не по 2
                    bool haveOre = _me.Count("metal_ore") >= 60 && _me.Count("wood") >= 40;
                    if (ready || (haveOre && !furnace.Burning))
                    {
                        Doing = ready ? "забирает металл из печи" : "загружает печь";
                        if (!GoTo(furnace.Pos + new Vector2(-1.0f, 0.3f), 0.4f, ref cmd)) return;
                        cmd.Aim = furnace.Pos;
                        cmd.Interact = Click2();
                        return;
                    }
                    if (metal < 300 && !furnace.Burning && _me.Count("metal_ore") < 60 && !Need("metal_ore", 60, ref cmd)) return;
                }
            }

            // 9. отнести добычу (камень копим, пока дом не каменный)
            if (wood > 500 + 400 * _greed || (stoneDone && stone > 300 + 300 * _greed))
            {
                Doing = "несёт добычу домой";
                if (!GoTo(CellC(1, 0) + new Vector2(-0.35f, 0.35f), 0.4f, ref cmd)) return;
                cmd.Aim = box.Pos;
                cmd.Crouch = true;
                cmd.Interact = Click2();
                return;
            }

            // 10. фарм: чего меньше — то и добываем; камень нужен на будущий апгрейд
            Gather(stone < wood * 0.6f ? NodeKind.Stone : NodeKind.Tree, ref cmd);
        }

        bool _int;
        bool Click2() { _int = !_int; return _int; }

        // ---------- роумер: охота на слабых, лут трупов, обход РТ ----------

        Actor _prey;
        float _preyTimer, _aimDrift;
        Vector2 _roamTo;

        /// <summary>Сила «кита» на глаз: что в руках (огнестрел > лук > копьё > инструменты > камень).</summary>
        static float KitPower(Actor a)
        {
            var id = a.Held?.Id;
            if (a.Weapon != null && !a.Weapon.Def.Melee) return id == "weapon_bow" ? 0.6f : 1f;
            return id switch { "weapon_spear" => 0.4f, "tool_hatchet" or "tool_pickaxe" => 0.25f, _ => 0.1f };
        }

        float MyPower => MathF.Max(Has("weapon_bow") && _me.Count("arrow") > 0 ? 0.6f : 0, Has("weapon_spear") ? 0.4f : 0.15f);

        /// <summary>true — роумер сам решил, что делать в этот тик (иначе общий план: еда, инструменты, фарм).</summary>
        bool Roam(ref InputCmd cmd)
        {
            // ставка: с большим грузом — не охотимся
            int load = _me.Count("wood") + _me.Count("stone") + _me.Count("metal_fragments") * 3 + _me.Count("scrap") * 5;
            bool loaded = load > 900;
            if (_me.Hp < 35) { _prey = null; return false; }   // ранен — общий план: убегает и лечится

            // цель: видимый слабее меня в 45 м (голый, с камнем) или тот, кто меня бьёт
            if (_prey == null || _prey.Dead || (_preyTimer -= World.Dt) < 0)
            {
                _prey = null;
                float best = float.MaxValue;
                foreach (var a in _w.Actors)
                {
                    if (a == _me || a.Dead || a.Team == Team.Npc) continue;
                    float d = Vector2.Distance(a.Pos, _me.Pos);
                    bool hitMe = _me.LastAttacker == a.Id && _w.Time - _me.LastDamagedTime < 5f;
                    if (!hitMe && (loaded || d > 45 || MyPower < 0.4f || KitPower(a) > MyPower + 0.05f)) continue;   // без копья/лука первым не лезет
                    if (!_w.HasLineOfSight(_me.Pos, a.Pos)) continue;
                    if (d < best) { best = d; _prey = a; }
                }
                _preyTimer = 2f;
            }
            if (_prey != null) { Hunt(_prey, ref cmd); return true; }

            // лут: сумка с трупа рядом
            LootBag bag = null;
            float bd = 35;
            foreach (var b in _w.Bags)
            {
                if (b.Empty || b.IsCrate) continue;
                float d = Vector2.Distance(b.Pos, _me.Pos);
                if (d < bd) { bd = d; bag = b; }
            }
            if (bag != null)
            {
                Doing = "лутает труп";
                if (MoveTo(bag.Pos, 1.0f, ref cmd)) cmd.Interact = Click2();
                return true;
            }

            // снаряжение: копьё, потом лук и стрелы (иначе — общий фарм)
            if (!Has("tool_hatchet")) return false;
            if (!Has("weapon_spear")) { if (!Craft("weapon_spear", ref cmd)) Need("wood", 150, ref cmd); return true; }
            bool hempNear = NearestPickup(PickupKind.Hemp) != null;
            if (!Has("weapon_bow") && (_me.Count("cloth") >= 50 || hempNear))
            {
                if (_me.Count("cloth") < 50) { Doing = "собирает коноплю"; UsePickup(PickupKind.Hemp, ref cmd); return true; }
                if (!Craft("weapon_bow", ref cmd)) Need("wood", 100, ref cmd);
                return true;
            }
            if (Has("weapon_bow") && _me.Count("arrow") < 12)
            {
                if (Craft("arrow", ref cmd)) return true;
                if (_me.Count("wood") < 25) { Need("wood", 150, ref cmd); return true; }
                Need("stone", 80, ref cmd);
                return true;
            }

            // бродит между РТ (там ходят люди), по пути смотрит по сторонам
            if (_roamTo == default || Vector2.Distance(_roamTo, _me.Pos) < 6)
            {
                // чередует: окрестности РТ и пляжи, где просыпаются голые
                if (_rng.NextDouble() < 0.5 && _w.PlayerSpawns.Count > 0)
                    _roamTo = _w.PlayerSpawns[_rng.Next(_w.PlayerSpawns.Count)] * 0.97f;
                else
                {
                    var m = _w.Monuments[_rng.Next(_w.Monuments.Count)];
                    var ang = (float)(_rng.NextDouble() * Math.PI * 2);
                    _roamTo = m.Center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * (70 + (m.Max - m.Min).Length() * 0.5f);   // дальше от охраны
                }
                if (!_w.Terrain.IsLand(_roamTo)) _roamTo *= 0.85f;
            }
            Doing = "бродит";
            if (Hold(Has("weapon_bow") ? "weapon_bow" : "weapon_spear", ref cmd)) { }
            MoveTo(_roamTo, 4, ref cmd);
            return true;
        }

        void Hunt(Actor prey, ref InputCmd cmd)
        {
            var to = prey.Pos - _me.Pos;
            float d = to.Length();
            var dir = d > 0.01f ? to / d : Vector2.UnitX;
            bool bow = Has("weapon_bow") && _me.Count("arrow") > 0 && d > 3.5f;
            Doing = $"охотится на {prey.Name}";
            if (bow)
            {
                if (!Hold("weapon_bow", ref cmd)) return;
                // держит дистанцию 8–14 м и стрейфится
                var side = new Vector2(-dir.Y, dir.X) * (MathF.Sin(_w.Time * 0.9f + _me.Id) > 0 ? 1 : -1);
                var move = side * 0.6f + (d > 14 ? dir : d < 8 ? -dir * 0.7f : Vector2.Zero);
                cmd.Move = move.LengthSquared() > 0.01f ? Avoid(Vector2.Normalize(move)) : Vector2.Zero;
                _aimDrift = _aimDrift * 0.95f + ((float)_rng.NextDouble() - 0.5f) * 0.25f;
                cmd.Aim = prey.Pos + prey.Vel * (d / 45f) + new Vector2(-dir.Y, dir.X) * _aimDrift * d * 0.06f;
                cmd.Peek = 1;
                cmd.Fire = _w.HasLineOfSight(_me.Pos, prey.Pos) && Click2();
                return;
            }
            // копьё: вплотную
            string melee = Has("weapon_spear") ? "weapon_spear" : Has("tool_hatchet") ? "tool_hatchet" : "tool_rock";
            if (!Hold(melee, ref cmd)) return;
            cmd.Aim = prey.Pos;
            if (d > 1.8f) { cmd.Move = Avoid(dir); cmd.Sprint = d > 6; }
            else cmd.Fire = true;
        }

        /// <summary>Дойти до точки с учётом двери: внутрь и наружу — через проём, закрытую дверь открываем.</summary>
        bool GoTo(Vector2 p, float tol, ref InputCmd cmd)
        {
            if (_site == null) return MoveTo(p, tol, ref cmd);
            var (pi, pj) = Building.CellOf(p);
            bool targetInside = pi >= _origin.i && pi <= _origin.i + 1 && pj >= _origin.j && pj <= _origin.j + 1;
            bool walled = _w.EdgeAt(_origin.i, _origin.j + 2, 0) != null && _w.EdgeAt(_origin.i + 1, _origin.j + 2, 0) != null
                          && _w.EdgeAt(_origin.i, _origin.j, 0) != null;
            if (targetInside == InsideBase || !walled)
            {
                // снаружи — обходим дом по углам, если он стоит на пути
                if (!InsideBase && !targetInside && _w.FoundationAt(_origin.i, _origin.j) != null)
                {
                    var min = new Vector2(_origin.i * Building.Cell, _origin.j * Building.Cell) - new Vector2(0.7f);
                    var max = new Vector2((_origin.i + 2) * Building.Cell, (_origin.j + 2) * Building.Cell) + new Vector2(0.7f);
                    // стоим вплотную к дому снаружи (например, только что вышли из проёма) — сначала шаг прочь
                    if (_me.Pos.X > min.X && _me.Pos.X < max.X && _me.Pos.Y > min.Y && _me.Pos.Y < max.Y)
                    {
                        var ctr = (min + max) * 0.5f;
                        var off = _me.Pos - ctr;
                        var outDir = MathF.Abs(off.X) / (max.X - min.X) > MathF.Abs(off.Y) / (max.Y - min.Y) ? new Vector2(MathF.Sign(off.X), 0) : new Vector2(0, MathF.Sign(off.Y));
                        cmd.Move = outDir;
                        cmd.Aim = _me.Pos + outDir * 3;
                        return false;
                    }
                    if (Geo.SegmentAabb(_me.Pos, p, min, max, out _))
                    {
                        Vector2 best = p; float bd = float.MaxValue;
                        foreach (var c in new[] { min - new Vector2(0.6f), new Vector2(max.X + 0.6f, min.Y - 0.6f), max + new Vector2(0.6f), new Vector2(min.X - 0.6f, max.Y + 0.6f) })
                        {
                            if (Vector2.Distance(_me.Pos, c) < 0.8f || Geo.SegmentAabb(_me.Pos, c, min, max, out _)) continue;   // у этого угла уже стоим
                            float cost = Vector2.Distance(_me.Pos, c) + Vector2.Distance(c, p);
                            if (cost < bd) { bd = cost; best = c; }
                        }
                        if (best != p) { MoveTo(best, 0.5f, ref cmd); return false; }
                    }
                }
                return MoveTo(p, tol, ref cmd);
            }
            // через дверь: точка перед дверью с нужной стороны → открыть → пройти
            var outside = DoorCenter + new Vector2(0, 1.0f);
            var inside = DoorCenter - new Vector2(0, 1.0f);
            var door = _w.DoorAt(_origin.i, _origin.j + 2, 0);
            var near = InsideBase ? inside : outside;
            var far = InsideBase ? outside : inside;
            if (door != null && !door.Open)
            {
                if (!MoveTo(near, 0.4f, ref cmd)) return false;
                cmd.Aim = DoorCenter;
                cmd.Interact = Click2();
                return false;
            }
            if (Vector2.Distance(_me.Pos, near) > 0.5f && Vector2.Distance(_me.Pos, DoorCenter) > 0.6f)
                return MoveTo(near, 0.4f, ref cmd) && false;
            MoveTo(far, 0.3f, ref cmd);
            return false;
        }
    }
}
