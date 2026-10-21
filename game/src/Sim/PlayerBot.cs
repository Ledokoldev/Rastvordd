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
    public sealed class PlayerBot : IBrain
    {
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
            cmd.Move = d.Length() < 1.2f ? Vector2.Normalize(d) : Avoid(Vector2.Normalize(d));   // вблизи цели — без обходов, иначе крутится
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
            if (_stockId == id && _me.Count(id) < _stockTarget) { Gather(id == "wood" ? NodeKind.Tree : NodeKind.Stone, ref cmd); return false; }
            if (_me.Count(id) >= count) { if (_stockId == id) _stockId = null; return true; }
            _stockId = id;
            _stockTarget = Math.Max(count, id == "wood" ? 350 : 250);
            Gather(id == "wood" ? NodeKind.Tree : NodeKind.Stone, ref cmd);
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
                    if (n.Kind != kind) continue;
                    if (_w.NearMonument(n.Pos, 22)) continue;
                    float d = Vector2.Distance(n.Pos, _me.Pos);
                    if (d < best) { best = d; _node = n; }
                }
                _nodeTimer = 20;
                if (_node == null) { Wander(ref cmd); return; }
            }
            if (!GoTo(_node.Pos, _node.Radius + 1.0f, ref cmd)) return;
            cmd.Aim = _node.Pos;
            if (Hold(tool, ref cmd)) cmd.Fire = true;
        }
        readonly List<ResourceNode> _nodeBuf = new();

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
                if (_deadTime > 6) { cmd.Respawn = true; _deadTime = 0; _node = null; }
                return cmd;
            }

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
                Doing = "ставит столб";
                // столб — в северо-западный угол, чтобы не мешал ходить по дому
                if (!MoveTo(CellC(0, 0) + new Vector2(0.3f, 1.2f), 0.6f, ref cmd)) return;
                cmd.Aim = CellC(0, 0) + new Vector2(-0.55f, -0.55f);
                if (Hold("deploy_claim", ref cmd)) Click(ref cmd);
                return;
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
            int targetTier = stone >= 600 && _greed > 0.5f ? 2 : 1;
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

            // 6. дверь
            if (_w.DoorAt(_origin.i, _origin.j + 2, 0) == null)
            {
                if (!Need("wood", 150, ref cmd)) return;
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
                cmd.Aim = CellC(1, 0) + new Vector2(0.12f, -0.12f);   // ящик радиусом 0,6 — не ближе 0,65 к стенам
                if (Hold("deploy_box", ref cmd)) Click(ref cmd);
                return;
            }

            // 8. содержание: запас на ~2 часа
            if (_w.ClaimHoursLeft(claim) < 1.5f && _w.ClaimLoad(claim).b > 0)
            {
                foreach (var (id, perHour) in _w.UpkeepPerHourOf(claim))
                    if (!Need(id, (int)MathF.Ceiling(perHour * 2), ref cmd)) return;
                Doing = "кормит столб";
                if (!GoTo(claim.Pos + new Vector2(0.6f, 0.6f), 0.35f, ref cmd)) return;   // вплотную к столбу, дальше от двери
                cmd.Aim = claim.Pos;
                cmd.Interact = Click2();
                return;
            }

            // 9. отнести добычу
            if (wood > 500 + 400 * _greed || stone > 300 + 300 * _greed)
            {
                Doing = "несёт добычу домой";
                if (!GoTo(CellC(1, 0) + new Vector2(-0.3f, 0.9f), 0.4f, ref cmd)) return;
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
