using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Этап 4 мозга: рейд и антирейд (docs/design/bot-brain.md). Всё честно: бот знает о чужих базах только то, что видел
    /// (двери с прочностью и материалом на момент взгляда, ящики, верстаки, сколько построек), о хозяине — где и с чем видел
    /// его последний раз и видел ли его смерть. Рейд: когда есть бомбы на дверь, ствол и патроны, база того стоит, а хозяин далеко,
    /// мёртв или слабее. В рейде — пушить или отступать (потери, патроны, защитник сильнее, бомбы кончились), из ящика брать
    /// по ценности, сколько унесу, и домой. Антирейд: взрывы и удары по своим стенам на слух → защищать (ствол, расстояние,
    /// сколько рейдеров и с чем) или переждать и потом отстроить.
    /// </summary>
    public sealed partial class PlayerBot
    {
        // ---------- память о чужих базах ----------

        sealed class KnownBase
        {
            public int ClaimId, OwnerId;
            public Vector2 Pos;
            public float SeenAt;
            public readonly HashSet<int> Pieces = new();
            public readonly Dictionary<int, (Vector2 pos, Vector2 a, Vector2 b, int tier, float hp, float seenAt)> Doors = new();
            public readonly Dictionary<int, Vector2> Boxes = new();
            public int Bench;
            public float OwnerSeenAt = -999, OwnerKit = -1, OwnerDeadAt = -999;
            public Vector2 OwnerSeenPos;
            public float RaidedAt = -999, FailedAt = -999;
            public int Looks;
            /// <summary>Богатство на глаз: постройки, увиденные ящики и верстаки; закрытый дом без увиденного ящика — «ящик там наверняка есть».</summary>
            public float Value => Pieces.Count * 1.5f + Math.Max(Boxes.Count, Pieces.Count >= 8 ? 1 : 0) * 60 + Bench * 50;
        }

        readonly Dictionary<int, KnownBase> _bases = new();
        float _baseScanAt;
        readonly HashSet<int> _myStructs = new();

        /// <summary>Чужие базы в поле зрения: раз в 2 с — постройки, двери, ящики, верстаки, хозяин.</summary>
        void PerceiveBases()
        {
            if (_w.Time - _baseScanAt < 2f) return;
            _baseScanAt = _w.Time;
            float range = _w.ViewRange(_me);
            var myClaim = MyClaim();
            _myStructs.Clear();
            foreach (var s in _w.Structures)
            {
                if (s.OwnerId == _me.Id) { _myStructs.Add(s.Id); continue; }
                if (s.ClaimId == 0 || (myClaim != null && s.ClaimId == myClaim.Id)) continue;
                float d = Vector2.Distance(s.Center, _me.Pos);
                if (d > range) continue;
                var probe = s.Center + Vector2.Normalize(_me.Pos - s.Center + new Vector2(0.001f, 0)) * 0.6f;
                if (d > 2.5f && !_w.HasLineOfSight(_me.Pos, probe)) continue;
                if (!_bases.TryGetValue(s.ClaimId, out var kb))
                {
                    kb = _bases[s.ClaimId] = new KnownBase { ClaimId = s.ClaimId, OwnerId = s.OwnerId, Pos = s.Center };
                    var owner = _w.Find(s.OwnerId);
                    Thought($"вижу чужую базу{(owner != null ? $" ({owner.Name})" : "")} в {d:0} м на {Dir(_me.Pos, s.Center)} — запомнил");
                    LStat("баз замечено");
                }
                kb.SeenAt = _w.Time;
                kb.Pieces.Add(s.Id);
                if (s.Kind == StructKind.Door) kb.Doors[s.Id] = (s.Center, s.A, s.B, s.Tier, s.Hp, _w.Time);
            }
            // двери, которые помню, но на их месте сейчас пусто (сменили, выбили) — забыть
            foreach (var kb in _bases.Values)
                foreach (var id in new List<int>(kb.Doors.Keys))
                {
                    var dm = kb.Doors[id];
                    if (Vector2.Distance(dm.pos, _me.Pos) < 25 && !_w.Structures.Exists(x => x.Id == id) && _w.HasLineOfSight(_me.Pos, dm.pos + Vector2.Normalize(_me.Pos - dm.pos + new Vector2(0.001f, 0)) * 0.6f)) kb.Doors.Remove(id);
                }
            foreach (var dp in _w.DeploysInRadius(_me.Pos, range, _depBuf))
            {
                if (dp.OwnerId == _me.Id || dp.OwnerId == 0 || dp.Kind == DeployKind.Recycler) continue;
                if (Vector2.Distance(dp.Pos, _me.Pos) > 2.5f && !_w.HasLineOfSight(_me.Pos, dp.Pos)) continue;
                KnownBase kb = null;
                foreach (var b in _bases.Values) if (b.OwnerId == dp.OwnerId && Vector2.Distance(b.Pos, dp.Pos) < 30) { kb = b; break; }
                if (kb == null) continue;
                if (dp.Kind == DeployKind.Claim) kb.Pos = dp.Pos;
                if (dp.Kind == DeployKind.Box) kb.Boxes[dp.Id] = dp.Pos;
                if (dp.Kind == DeployKind.Workbench) kb.Bench = Math.Max(kb.Bench, Math.Max(1, dp.Level));
            }
            // хозяева: где и с чем видел; видел ли смерть
            foreach (var a in _w.Actors)
            {
                if (a == _me || a.Spectator || a.Team == Team.Npc) continue;
                foreach (var kb in _bases.Values)
                {
                    if (kb.OwnerId != a.Id) continue;
                    if (!a.Dead && _w.CanSeeActor(_me, a, range, 260f)) { kb.OwnerSeenAt = _w.Time; kb.OwnerSeenPos = a.Pos; kb.OwnerKit = KitPower(a); }
                }
            }
        }

        /// <summary>Смерть хозяина базы, которую я видел или сам убил (из событий тика).</summary>
        void PerceiveDeaths()
        {
            foreach (var e in _w.Events)
            {
                if (e.Kind != SimEventKind.Death) continue;
                bool seen = e.OtherId == _me.Id || (Vector2.Distance(e.Pos, _me.Pos) < _w.ViewRange(_me) && _w.HasLineOfSight(_me.Pos, e.Pos));
                if (!seen) continue;
                foreach (var kb in _bases.Values) if (kb.OwnerId == e.ActorId) kb.OwnerDeadAt = _w.Time;
            }
        }

        static int BombsFor(int tier, float hp)
        {
            float perBomb = 75f * 0.95f * World.BlastTierMult[Math.Clamp(tier, 0, 4)];
            return (int)MathF.Ceiling(hp / perBomb * 1.25f);
        }

        /// <summary>Самая дешёвая дверь базы по памяти (бомбы × 1000 + дорога).</summary>
        int CheapestDoor(KnownBase kb, out int bombs)
        {
            bombs = int.MaxValue;
            int pick = 0;
            float best = float.MaxValue;
            foreach (var (id, d) in kb.Doors)
            {
                int n = BombsFor(d.tier, d.hp);
                float c = n * 1000 + Vector2.Distance(d.pos, _me.Pos);
                if (c < best) { best = c; pick = id; bombs = n; }
            }
            return pick;
        }

        // ---------- рейд ----------

        sealed class RaidState
        {
            public KnownBase Base;
            public int DoorId;
            public float StartAt, HpLost, LastHp;
            public bool Breached, Looted, Leaving;
            public float ThrowWait, BoxTry;
            public int BoxTries, Threw;
            public float Loot;
            public float Value0;
            public readonly HashSet<int> BoxesDone = new(), Rooms = new();
            public int RoomId;
            public float ApproachBest = float.MaxValue, ApproachAt, BoxSince, InsideAt;
            public int BoxId;
        }
        RaidState _raid;
        float _raidCooldown = 600, _raidJudgeAt = -999, _raidScoreCache;
        KnownBase _raidPick;
        string _raidWhy;
        int _raidNeed;

        int Bombs => Stock("expl_satchel");

        /// <summary>
        /// Цель «рейд»: известная база с дверью, бомб хватает (с собой и в ящике), ствол и патроны, здоров.
        /// Очки — богатство базы на глаз × где хозяин (далеко / мёртв / слабее) × тяга к драке.
        /// </summary>
        float RaidScore()
        {
            if (_raid != null) return 72 * MathF.Max(W("рейд"), 0.8f);
            if (_w.Time < _raidCooldown || _rtPhase != RtPhase.None || _rj != null) return 0;
            if (_w.Time - _raidJudgeAt < 5) return _raidScoreCache;
            _raidJudgeAt = _w.Time;
            _raidScoreCache = 0;
            var gun = Ranged();
            if (gun == null || AmmoLeft(gun) < 20 || _me.Hp < 80 || _bases.Count == 0) return 0;
            float best = 0;
            foreach (var kb in _bases.Values)
            {
                if (kb.Doors.Count == 0 || _w.Time - kb.FailedAt < 900 || _w.Time - kb.RaidedAt < 1200) continue;
                if (kb.OwnerId == _me.Id) continue;
                CheapestDoor(kb, out int need);
                if (Bombs < need) { Say("рейд-бомбы", $"рейд на {_w.Find(kb.OwnerId)?.Name ?? "?"} — не сейчас: бомб {Bombs}/{need}", 300); continue; }
                float worth = Math.Clamp(kb.Value / 250f, 0.2f, 1f);
                float owner = 0;
                string why = "";
                if (_w.Time - kb.OwnerDeadAt < 150) { owner = 0.45f; why = "хозяин только что умер"; }
                else if (_w.Time - kb.OwnerSeenAt < 300 && Vector2.Distance(kb.OwnerSeenPos, kb.Pos) > 120) { owner = 0.35f; why = $"хозяина видел в {Vector2.Distance(kb.OwnerSeenPos, kb.Pos):0} м от дома"; }
                else if (kb.OwnerKit >= 0 && kb.OwnerKit > MyPower + 0.1f) { owner = -0.4f; why = "хозяин сильнее меня"; }
                else if (kb.OwnerKit >= 0 && kb.OwnerKit < MyPower - 0.1f) { owner = 0.15f; why = "хозяин слабее"; }
                else why = "где хозяин — не знаю";
                float s = (35 + 30 * worth + 30 * owner) * (0.6f + Aggression) * W("рейд");
                if (s > best) { best = s; _raidPick = kb; _raidNeed = need; _raidWhy = $"база ≈{kb.Value:0} (ящиков {kb.Boxes.Count}, верстак {kb.Bench}), {why}, бомб {Bombs}/{need}"; }
            }
            if (_raidPick != null && best > 0) Say("рейд-решение", $"рейд на базу {_w.Find(_raidPick.OwnerId)?.Name ?? "?"}: {_raidWhy} → {(best >= 50 ? "иду" : "пока нет")}", 180);
            _raidScoreCache = best >= 50 ? best : 0;
            return _raidScoreCache;
        }

        bool RaidAct(ref InputCmd cmd)
        {
            if (_raid == null)
            {
                if (_raidPick == null) return false;
                _raid = new RaidState { Base = _raidPick, StartAt = _w.Time, LastHp = _me.Hp, Value0 = CarriedValue() };
                _raid.DoorId = CheapestDoor(_raidPick, out _);
                _w.Milestone(_me, $"идёт в рейд на базу {_w.Find(_raidPick.OwnerId)?.Name ?? "?"} ({_raidNeed} бомб)");
                Thought($"иду в рейд: {_raidWhy}");
                LStat("рейдов начато");
            }
            var r = _raid;
            if (_me.Hp < r.LastHp) r.HpLost += r.LastHp - _me.Hp;
            r.LastHp = _me.Hp;
            var gun = Ranged();
            var box = MyBox();

            // сборы: бомбы из ящика
            // все бомбы с собой: дверь могли сменить или укрепить, за ней бывает вторая
            if (!r.Breached && !r.Leaving && r.Threw == 0 && box != null && InBox(box, "expl_satchel") > 0 && Vector2.Distance(_me.Pos, box.Pos) < 120)
            {
                Doing = "берёт бомбы из ящика";
                int all = _me.Count("expl_satchel") + InBox(box, "expl_satchel");
                if (TakeFromBox(box, "expl_satchel", all, ref cmd)) { Reserve("expl_satchel", all); return true; }
            }

            // пушить или отступать
            if (!r.Leaving)
            {
                string why = null;
                if (_me.Hp < 45) why = $"ранен ({_me.Hp:0} HP)";
                else if (r.HpLost > 60) why = $"потери: −{r.HpLost:0} HP за рейд";
                else if (gun == null || AmmoLeft(gun) < 8) why = "кончаются патроны";
                else if (_w.Time - r.StartAt > 480) why = "рейд затянулся";
                else
                    foreach (var a in _w.Actors)
                    {
                        if (a == _me || a.Dead || a.Spectator || a.Team == Team.Npc || Vector2.Distance(a.Pos, r.Base.Pos) > 40) continue;
                        if (!_w.CanSeeActor(_me, a, _w.ViewRange(_me), 260f)) continue;
                        if (KitPower(a) > MyPower + 0.05f || (KitPower(a) >= MyPower - 0.05f && _me.Hp < 70)) { why = $"защитник {a.Name} с {Items.Name(a.Held?.Id ?? "tool_rock")}"; break; }
                    }
                if (why != null) { RaidEnd($"отступаю: {why}", false); return true; }
            }
            if (r.Leaving) return RaidGoHome(ref cmd);

            var door = _w.Structures.Find(s => s.Id == r.DoorId);
            if (!r.Breached)
            {
                if (door == null)
                {
                    // двери, что помню, нет: взорвал сам — заходить; иначе память устарела (дверь сменили) — подойти и посмотреть заново
                    var mem = r.Base.Doors.TryGetValue(r.DoorId, out var dm) ? dm.pos : r.Base.Pos;
                    if (r.Threw > 0) { r.Breached = true; Thought("дверь выбита — захожу"); }
                    else if (Vector2.Distance(_me.Pos, mem) > 7 || !_w.HasLineOfSight(_me.Pos, mem + Vector2.Normalize(_me.Pos - mem + new Vector2(0.001f, 0)) * 0.6f))
                    {
                        Doing = "рейд: подходит к двери";
                        if (gun != null) Hold(gun, ref cmd);
                        GoTo(mem, 4f, ref cmd);
                        float nearD = Vector2.Distance(_me.Pos, mem);
                        if (nearD < r.ApproachBest - 1) { r.ApproachBest = nearD; r.ApproachAt = _w.Time; }
                        else if (_w.Time - r.ApproachAt > 40) RaidEnd("не подойти к двери", false);
                        return true;
                    }
                    else
                    {
                        r.Base.Doors.Remove(r.DoorId);
                        if (r.Base.Doors.Count == 0) { RaidEnd("двери, что помнил, нет — другой не вижу", false); return true; }
                        r.DoorId = CheapestDoor(r.Base, out int need2);
                        if (_me.Count("expl_satchel") < need2) { RaidEnd($"дверь сменили — на новую бомб мало ({_me.Count("expl_satchel")}/{need2})", false); return true; }
                        Thought("дверь сменили — беру другую");
                        return true;
                    }
                }
                else
                {
                    var outside = DoorOutside(door);
                    Doing = $"рейдит: дверь {Building.TierNames[door.Tier]} {(int)door.Hp}/{(int)door.MaxHp}";
                    if ((r.ThrowWait -= World.Dt) > 0)
                    {
                        var back = outside + Vector2.Normalize(outside - door.Center + new Vector2(0.001f, 0)) * 4f;
                        MoveTo(back, 0.8f, ref cmd);
                        Hold(gun ?? "tool_hatchet", ref cmd);
                        return true;
                    }
                    if (_me.Count("expl_satchel") == 0) { RaidEnd("бомбы кончились, дверь стоит", false); return true; }
                    if (!MoveTo(outside, 0.5f, ref cmd)) { if (gun != null) Hold(gun, ref cmd); return true; }
                    cmd.Aim = door.Center;
                    if (Hold("expl_satchel", ref cmd)) { Click(ref cmd); r.ThrowWait = 6.5f; r.Threw++; LStat("бомб брошено"); }
                    return true;
                }
            }

            // внутри: ящики по памяти и что увижу; недостижим — следующая дверь. Внутри не дольше 100 с — хозяин мог услышать взрывы
            if (r.InsideAt == 0) r.InsideAt = _w.Time;
            if (_w.Time - r.InsideAt > 100) { RaidEnd(r.Loot > 0 ? "долго внутри — уношу, что есть" : "долго внутри, ящиков не нашёл", r.Loot > 0); return true; }
            Deployable target = null;
            float bd = float.MaxValue;
            foreach (var d in _w.Deployables)
            {
                if (d.Kind != DeployKind.Box || d.OwnerId != r.Base.OwnerId || d.Storage == null || r.BoxesDone.Contains(d.Id)) continue;
                if (!r.Base.Boxes.ContainsKey(d.Id) && !(Vector2.Distance(d.Pos, _me.Pos) < 15 && _w.HasLineOfSight(_me.Pos, d.Pos))) continue;
                r.Base.Boxes[d.Id] = d.Pos;
                float dist = Vector2.Distance(d.Pos, _me.Pos);
                if (dist < bd) { bd = dist; target = d; }
            }
            if (target == null)
            {
                // ящика не видно — обойти комнаты (полы базы по очереди); в комнату не пройти — взорвать ближнюю дверь
                Structure room = null; float rd = float.MaxValue;
                foreach (var st in _w.Structures)
                    if (Building.IsFloor(st.Kind) && st.ClaimId == r.Base.ClaimId && !r.Rooms.Contains(st.Id))
                    { float dd = Vector2.Distance(st.Center, _me.Pos); if (dd < rd) { rd = dd; room = st; } }
                if (room == null || r.Rooms.Count >= 8)
                {
                    RaidEnd(r.Loot > 0 ? "вынес всё, что нашёл" : "ящиков не нашёл", r.Loot > 0);
                    return true;
                }
                if (r.RoomId != room.Id) { r.RoomId = room.Id; r.BoxTry = _w.Time; }
                Doing = "ищет ящик в чужом доме";
                if (gun != null) Hold(gun, ref cmd);
                if (GoTo(room.Center, 0.9f, ref cmd) || _w.Time - r.BoxTry > 10)
                {
                    if (_w.Time - r.BoxTry > 10 && _path == null)
                    {
                        Structure next = null; float nd = float.MaxValue;
                        foreach (var st in _w.Structures)
                            if (st.Kind == StructKind.Door && st.ClaimId == r.Base.ClaimId && Vector2.Distance(st.Center, _me.Pos) < 8)
                            { float c = Vector2.Distance(st.Center, room.Center); if (c < nd) { nd = c; next = st; } }
                        if (next != null && _me.Count("expl_satchel") >= BombsFor(next.Tier, next.Hp))
                        { r.DoorId = next.Id; r.Breached = false; r.Threw = 0; Thought($"в комнату не пройти — взрываю {Building.KindName(next.Kind).ToLower()} ({Building.TierNames[next.Tier]})"); return true; }
                    }
                    r.Rooms.Add(room.Id);
                }
                return true;
            }
            Doing = "выносит чужой ящик";
            if (r.BoxId != target.Id) { r.BoxId = target.Id; r.BoxSince = _w.Time; }
            if (_w.Time - r.BoxSince > 30) { r.BoxesDone.Add(target.Id); Thought("до ящика за 30 с не добраться — бросаю"); return true; }
            if (!Reach(target.Pos, ref cmd))
            {
                if (_goalStuck > 6 && _path == null)
                {
                    // ящик за следующей дверью: взорвать ближайшую к ящику дверь этой базы
                    Structure next = null; float nd = float.MaxValue;
                    foreach (var s in _w.Structures)
                        if (s.Kind == StructKind.Door && s.ClaimId == r.Base.ClaimId && Vector2.Distance(s.Center, _me.Pos) < 12)
                        { float c = Vector2.Distance(s.Center, target.Pos) + Vector2.Distance(s.Center, _me.Pos); if (c < nd) { nd = c; next = s; } }
                    if (next != null && _me.Count("expl_satchel") >= BombsFor(next.Tier, next.Hp)) { r.DoorId = next.Id; r.Breached = false; Thought("ящик за второй дверью — взрываю и её"); _goalStuck = 0; }
                    else { r.BoxesDone.Add(target.Id); Thought("до ящика не добраться — бомб на вторую дверь нет"); }
                }
                return true;
            }
            LootEnemyBox(target, ref cmd);
            return true;
        }

        /// <summary>Из чужого ящика — по ценности, пока есть место; дерево и камень — только если рюкзак свободен.</summary>
        void LootEnemyBox(Deployable box, ref InputCmd cmd)
        {
            var r = _raid;
            var items = new List<ItemSlot>(box.Storage.Items);
            items.Sort((a, b) => (Items.Value(b.Id) * b.Count).CompareTo(Items.Value(a.Id) * a.Count));
            var took = new List<string>();
            float v = 0;
            foreach (var it in items)
            {
                if (it.State != null) continue;
                if (Items.Value(it.Id) < 0.1f && FreePack() < 6) continue;   // дешёвое — когда место есть
                int before = _me.Count(it.Id);
                int n = it.Count;
                _w.BotTake(_me, box, it.Id, n);
                int got = _me.Count(it.Id) - before;
                if (got > 0) { took.Add($"{Items.Name(it.Id)} ×{got}"); v += Items.Value(it.Id) * got; }
                if (FreePack() == 0) break;
            }
            if (box.Storage.Items.Exists(s => s.State != null) && FreePack() > 0 && _w.Time - _lastUse > 0.4f) { _lastUse = _w.Time; _w.BotUse(_me, box); took.Add("оружие"); }
            r.BoxesDone.Add(box.Id);
            r.Loot += v;
            if (took.Count > 0) { Thought($"вынес из ящика ≈{v:0} лома: {string.Join(", ", took.GetRange(0, Math.Min(4, took.Count)))}"); LStat("вынесено из чужих ящиков, ломов", (int)v); }
        }

        void RaidEnd(string why, bool success)
        {
            var r = _raid;
            if (r == null || r.Leaving) return;
            r.Leaving = true;
            float got = r.Loot;
            var owner = _w.Find(r.Base.OwnerId)?.Name ?? "?";
            success |= got > 0;   // отступил, но с добычей — рейд удался
            if (success) { r.Base.RaidedAt = _w.Time; _w.Milestone(_me, $"зарейдил базу {owner}: вынес ≈{got:0} лома"); LStat("рейдов удачных"); }
            else { r.Base.FailedAt = _w.Time; LStat("рейдов сорвано"); }
            Thought($"рейд на {owner}: {why}{(got > 1 ? $", несу ≈{got:0} лома" : "")} → домой");
        }

        bool RaidGoHome(ref InputCmd cmd)
        {
            var claim = MyClaim();
            Doing = "уходит из рейда домой";
            if (Ranged() is string g) Hold(g, ref cmd);
            if (claim == null || Vector2.Distance(_me.Pos, claim.Pos) < 14 || _w.Time - _raid.StartAt > 900)
            {
                _raid = null;
                _raidCooldown = _w.Time + 600;
                return false;
            }
            GoTo(claim.Pos + new Vector2(0.6f, 0.6f), 3f, ref cmd);
            return true;
        }

        /// <summary>Подготовка рейда в хозяйстве: известна база, бомб не хватает — крафтить (порох, ткань, металл).</summary>
        bool RaidPrep(Deployable box, Deployable furnace, ref InputCmd cmd)
        {
            if (!_me.Known.Contains("expl_satchel") || MyBenchLevel() < 2 || Ranged() == null) return false;
            KnownBase target = null; int need = 0; float best = 0;
            foreach (var kb in _bases.Values)
            {
                if (kb.Doors.Count == 0 || _w.Time - kb.RaidedAt < 1200) continue;
                CheapestDoor(kb, out int n);
                float v = kb.Value / (1 + n);
                if (n <= 12 && v > best) { best = v; target = kb; need = n; }
            }
            if (target == null || Bombs >= need || target.Value < 60) return false;
            var rcp = Recipes.All[Recipe("expl_satchel")];
            foreach (var (id, n) in rcp.Cost)
            {
                if (_me.Count(id) >= n) continue;
                if (TakeFromBox(box, id, n, ref cmd)) { Reserve(id, n); return true; }
                if (id == "gunpowder") { Doing = $"копит порох на рейд ({Bombs}/{need} бомб)"; return MakeGunpowder(n, box, furnace, ref cmd); }
                if (id == "cloth") { Doing = "собирает коноплю на бомбы"; return UsePickup(PickupKind.Hemp, ref cmd) || true; }
                if (id == "metal_fragments") { _metalGoal = Math.Max(_metalGoal, 300); return false; }
                return !Need(id, n, ref cmd) || true;
            }
            return CraftAtBench("expl_satchel", box, ref cmd);
        }

        // ---------- разведка: бомбы есть, а чужих баз не знаю ----------

        Vector2 _scoutTo;
        float _scoutUntil, _scoutAng;
        RtInfo _scoutRt;
        int _scoutStep;
        readonly Dictionary<string, float> _scouted = new();

        float ScoutScore()
        {
            if (_raid != null || _rtPhase != RtPhase.None || Ranged() == null || _me.Hp < 70) return 0;
            if (Bombs < 3 || MyClaim() == null) return 0;
            if (_bases.Count > 0 && !UnseenDoorBase(out _)) return 0;
            if (_w.Time < _scoutUntil && _scoutTo != default) return 80;   // начал обход — дойти (иначе походы на РТ перебивают на полпути)
            return 55 * MathF.Max(W("рейд"), 1f);
        }

        /// <summary>Знаю базу, но её дверей не видел (видел стену сбоку) — сходить посмотреть.</summary>
        bool UnseenDoorBase(out KnownBase kb)
        {
            kb = null;
            foreach (var b in _bases.Values)
                if (b.Doors.Count == 0 && _w.Time - b.FailedAt > 900) { kb = b; return true; }
            return false;
        }

        /// <summary>Чужие базы — у РТ (там и строятся): обойти кольцо вокруг чужой РТ; известную базу без двери — обойти её саму.</summary>
        bool Scout(ref InputCmd cmd)
        {
            if (UnseenDoorBase(out var look) && (_scoutTo == default || Vector2.Distance(_scoutTo, _me.Pos) < 4 || _w.Time > _scoutUntil))
            {
                float a = (float)(_rng.NextDouble() * MathF.Tau);
                _scoutTo = look.Pos + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 16;
                _scoutUntil = _w.Time + 60;
                if (++look.Looks > 5) { look.FailedAt = _w.Time; look.Looks = 0; }
                Say("разведка-база", $"знаю базу {_w.Find(look.OwnerId)?.Name ?? "?"}, но двери не видел — обхожу посмотреть (бомб {Bombs})", 90);
            }
            else if (_scoutTo == default || Vector2.Distance(_scoutTo, _me.Pos) < 6 || _w.Time > _scoutUntil)
            {
                // кольцо вокруг РТ — 4 точки; РТ — сначала тихие (у них строятся осторожные), ближние, давно не осмотренные
                if (_scoutRt == null || _scoutStep >= 4)
                {
                    float best = float.MaxValue;
                    _scoutRt = null;
                    foreach (var rt in Rts())
                    {
                        if (rt.M.Contains(Home, rt.Radius + 130) || _w.Time - _scouted.GetValueOrDefault(rt.M.Name, -9999) < 1200) continue;   // своя округа и недавно осмотренные
                        float c = rt.Risk * 60 + Vector2.Distance(rt.M.Center, _me.Pos);
                        if (c < best) { best = c; _scoutRt = rt; }
                    }
                    if (_scoutRt == null) { _scouted.Clear(); _scoutRt = Rts()[_rng.Next(Rts().Count)]; }
                    _scouted[_scoutRt.M.Name] = _w.Time;
                    _scoutStep = 0;
                    _scoutAng = (float)(_rng.NextDouble() * MathF.Tau);
                    Say("разведка", $"бомбы есть ({Bombs}), чужих баз не знаю — обхожу округу «{_scoutRt.Short}» (там строятся)", 60);
                }
                var r = _scoutRt;
                float a = _scoutAng + _scoutStep++ * MathF.PI / 2;
                _scoutTo = r.M.Center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (r.Radius + 75);
                if (!_w.Terrain.IsLand(_scoutTo)) _scoutTo = r.M.Center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (r.Radius + 45);
                _scoutUntil = _w.Time + 120;
            }
            Doing = "разведка: ищет чужие базы";
            if (Ranged() is string g) Hold(g, ref cmd);
            GoTo(_scoutTo, 4f, ref cmd);
            return true;
        }

        // ---------- антирейд (на слух и на глаз) ----------

        float _alarmAt = -999, _alarmFirst, _selfHitAt = -999;
        Vector2 _alarmPos;
        string _alarmWhat;
        readonly Dictionary<int, float> _raiders = new();   // кто замечен у базы во время тревоги → сила кита
        bool _raidAlarm, _defend;
        float _rebuildUntil;
        int _piecesAtAlarm;
        float _stashAtAlarm;

        void PerceiveRaid()
        {
            var claim = MyClaim();
            if (claim == null) return;
            foreach (var e in _w.Events)
            {
                if (e.ActorId == _me.Id) { if (e.Kind == SimEventKind.HitStruct) _selfHitAt = _w.Time; continue; }
                float toBase = Vector2.Distance(e.Pos, claim.Pos), toMe = Vector2.Distance(e.Pos, _me.Pos);
                string what = null;
                if (e.Kind == SimEventKind.Explosion && toBase < 40 && toMe < 260) what = $"взрыв у дома ({Items.Name(e.Text)})";
                else if (e.Kind == SimEventKind.HitStruct && _myStructs.Contains(e.OtherId) && toMe < 60) what = "бьют по моим стенам";
                else if (e.Kind == SimEventKind.Destroyed && _myStructs.Contains(e.OtherId) && toMe < 260 && _w.Time - _selfHitAt > 3) what = $"снесли {e.Text}";
                if (what == null) continue;
                if (_w.Time - _alarmAt > 120) { _alarmFirst = _w.Time; _raiders.Clear(); _piecesAtAlarm = _myStructs.Count; _stashAtAlarm = _stash; _raidAlarm = false; }
                _alarmAt = _w.Time; _alarmPos = e.Pos; _alarmWhat = what;
            }
            if (_w.Time - _alarmAt < 120)
                foreach (var a in _w.Actors)
                {
                    if (a == _me || a.Dead || a.Spectator || a.Team == Team.Npc || Vector2.Distance(a.Pos, claim.Pos) > 40) continue;
                    if (_w.CanSeeActor(_me, a, _w.ViewRange(_me), 260f)) { if (!_raiders.ContainsKey(a.Id)) { Thought($"у моей базы {a.Name} с {Items.Name(a.Held?.Id ?? "tool_rock")}"); Offend(a, "Raid", $"рейдил мою базу ({_alarmWhat})"); } _raiders[a.Id] = KitPower(a); _lastDanger = _w.Time; }
                }
        }

        float AntiRaidScore()
        {
            var claim = MyClaim();
            if (claim == null) return 0;
            if (_w.Time - _alarmAt > 90)
            {
                if (_raidAlarm)
                {
                    _raidAlarm = false;
                    int lost = Math.Max(0, _piecesAtAlarm - _myStructs.Count);
                    Thought($"у дома тихо — рейд кончился: снесено построек {lost}, ящик ≈{_stashAtAlarm:0} → ≈{_stash:0} лома; отстраиваю и чиню");
                    LStat("рейдов по мне пережито");
                    if (lost > 0) _w.Milestone(_me, $"пережил рейд: снесено {lost}, отстраивает");
                    _rebuildUntil = _w.Time + 240;   // сначала отстроиться, потом походы
                }
                return 0;
            }
            if (_threat != null) return 0;   // рейдер на виду — это бой (или отход), а не бег к дому
            float dist = Vector2.Distance(_me.Pos, claim.Pos);
            var gun = Ranged();
            float maxKit = 0.8f;   // слышал взрывы — у них как минимум бомбы и, наверное, ствол
            if (_raiders.Count > 0) { maxKit = 0; foreach (var k in _raiders.Values) maxKit = MathF.Max(maxKit, k); }
            int n = Math.Max(1, _raiders.Count);
            string why;
            bool defend;
            if (gun == null) { defend = false; why = "нечем стрелять"; }
            else if (AmmoLeft(gun) < 10) { defend = false; why = "мало патронов"; }
            else if (_me.Hp < 50) { defend = false; why = $"ранен ({_me.Hp:0} HP)"; }
            else if (MyPower < maxKit - 0.1f) { defend = false; why = $"у них оружие сильнее ({(maxKit >= 1 ? "калаш" : "самопал")})"; }
            else if (n > (MyPower > maxKit + 0.1f ? 2 : 1)) { defend = false; why = $"их {n}"; }
            else if (dist > 300) { defend = false; why = $"далеко ({dist:0} м) — не успею"; }
            else { defend = true; why = $"в ящике ≈{_stash:0} лома, {Items.Name(gun)} не слабее, их {n}"; }
            if (!_raidAlarm || defend != _defend)
            {
                _raidAlarm = true;
                _defend = defend;
                Thought($"мой дом рейдят ({_alarmWhat}) → {(defend ? "иду защищать" : "пережидаю в стороне, потом отстрою")}: {why}");
                LStat(defend ? "антирейд: защищать" : "антирейд: переждать");
            }
            if (!defend && dist > 90) return 0;     // далеко и не иду — живу дальше
            return (defend ? 90 : 80) * W("антирейд");
        }

        bool AntiRaid(ref InputCmd cmd)
        {
            var claim = MyClaim();
            if (claim == null) return false;
            var gun = Ranged();
            if (!_defend)
            {
                Doing = "дом рейдят — пережидает в стороне";
                var away = _me.Pos - _alarmPos;
                if (away.LengthSquared() < 0.01f) away = Vector2.UnitX;
                if (Vector2.Distance(_me.Pos, claim.Pos) < 75) { MoveTo(claim.Pos + Vector2.Normalize(away) * 85, 3f, ref cmd); cmd.Sprint = true; }
                else { cmd.Crouch = true; cmd.Aim = _alarmPos; }
                return true;
            }
            Doing = "бежит защищать дом";
            if (gun != null) Hold(gun, ref cmd);
            float d = Vector2.Distance(_me.Pos, claim.Pos);
            if (d < 30) cmd.Crouch = d > 6;   // последние метры — тихо
            GoTo(claim.Pos + new Vector2(0.6f, 0.6f), 1.5f, ref cmd);
            return true;
        }
    }
}
