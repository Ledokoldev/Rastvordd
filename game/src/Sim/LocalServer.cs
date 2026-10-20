using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    public struct ActorSnap
    {
        public int Id;
        public Vector2 Pos;
        public float Facing;
        public bool Dead;
        public bool IsNpc;
        public string WeaponId;
        public bool Reloading;
        public bool Sprinting;
    }

    public struct ProjSnap { public int Id; public Vector2 Pos; public Vector2 Dir; }
    public struct BagSnap { public int Id; public Vector2 Pos; public bool IsCrate; public bool Empty; }

    public sealed class SelfInfo
    {
        public int Id;
        public bool Dead;
        public float Hp, MaxHp;
        public string WeaponName;
        public int Ammo, Magazine, Reserve;
        public float ReloadProgress;    // 0..1, 0 — не перезаряжается
        public float SpreadDeg;
        public int Bandages;
        public bool NearLoot;
        public string[] Hotbar;
        public int Current;
        public List<(string name, int count)> Inventory = new();
    }

    /// <summary>То, что сервер отправляет клиенту за тик. Только видимое этому клиенту.</summary>
    public sealed class Snapshot
    {
        public uint Tick;
        public float Time;
        public SelfInfo Self;
        public List<ActorSnap> Actors = new();
        public List<ProjSnap> Projectiles = new();
        public List<BagSnap> Bags = new();
        public List<SimEvent> Events = new();
    }

    public struct WallInfo { public Vector2 Min, Max; public float Height; public WallKind Kind; }

    public sealed class MapInfo
    {
        public float HalfSize;
        public List<WallInfo> Walls = new();
    }

    /// <summary>
    /// Локальный сервер. Сейчас живёт в одном процессе с клиентом; позже тот же класс
    /// будет работать за сетевым транспортом. Клиент общается с ним только через
    /// InputCmd → Submit и Snapshot ← BuildSnapshot.
    /// </summary>
    public sealed class LocalServer
    {
        public readonly World World;
        readonly Dictionary<int, InputCmd> _pending = new();
        readonly List<NpcBrain> _brains = new();
        readonly Dictionary<int, InputCmd> _inputs = new();
        readonly List<SimEvent> _eventsThisTick = new();

        public LocalServer(int seed)
        {
            World = new World(seed);
            int i = 0;
            foreach (var npc in World.SpawnNpcs())
                _brains.Add(new NpcBrain(npc, World, seed * 31 + i++));
        }

        public MapInfo GetMap()
        {
            var m = new MapInfo { HalfSize = World.HalfSize };
            foreach (var w in World.Walls)
                m.Walls.Add(new WallInfo { Min = w.Wall.Min, Max = w.Wall.Max, Height = w.Wall.Height, Kind = w.Kind });
            return m;
        }

        public int Connect(string name) => World.SpawnPlayer(name).Id;

        public void Submit(int clientId, InputCmd cmd) => _pending[clientId] = cmd;

        public void Tick()
        {
            _inputs.Clear();
            foreach (var kv in _pending) _inputs[kv.Key] = kv.Value;
            _pending.Clear();
            int bi = 0;
            foreach (var a in World.Actors)
                if (a.Team == Team.Npc) _inputs[a.Id] = _brains[bi++].Think();
            World.Step(_inputs);
            _eventsThisTick.Clear();
            _eventsThisTick.AddRange(World.Events);
        }

        public Snapshot BuildSnapshot(int clientId)
        {
            var me = World.Find(clientId);
            var snap = new Snapshot { Tick = World.Tick, Time = World.Time };
            snap.Self = BuildSelf(me);

            foreach (var a in World.Actors)
            {
                bool visible = a.Id == me.Id || IsVisibleTo(me, a);
                if (!visible) continue;
                var w = a.Weapon;
                snap.Actors.Add(new ActorSnap
                {
                    Id = a.Id, Pos = a.Pos, Facing = a.Facing, Dead = a.Dead, IsNpc = a.Team == Team.Npc,
                    WeaponId = w?.Def.Id, Reloading = w != null && w.ReloadLeft > 0,
                    Sprinting = a.Vel.Length() > World.WalkSpeed + 0.3f,
                });
            }

            foreach (var p in World.Projectiles)
                if (Vector2.DistanceSquared(p.Pos, me.Pos) < 80 * 80)
                    snap.Projectiles.Add(new ProjSnap { Id = p.Id, Pos = p.Pos, Dir = p.Dir });

            foreach (var b in World.Bags)
                if (Vector2.DistanceSquared(b.Pos, me.Pos) < 60 * 60 && (b.IsCrate || IsPointVisible(me, b.Pos)))
                    snap.Bags.Add(new BagSnap { Id = b.Id, Pos = b.Pos, IsCrate = b.IsCrate, Empty = b.Empty });

            foreach (var e in _eventsThisTick)
            {
                bool mine = e.ActorId == me.Id || e.OtherId == me.Id;
                if (e.Kind == SimEventKind.Loot && !mine) continue;
                if (!mine && Vector2.DistanceSquared(e.Pos, me.Pos) > 80 * 80) continue;
                snap.Events.Add(e);
            }
            return snap;
        }

        SelfInfo BuildSelf(Actor me)
        {
            var w = me.Weapon;
            var s = new SelfInfo
            {
                Id = me.Id, Dead = me.Dead, Hp = me.Hp, MaxHp = me.MaxHp,
                WeaponName = w?.Def.Name ?? "—",
                Ammo = w?.Ammo ?? 0, Magazine = w?.Def.Magazine ?? 0,
                Reserve = w != null ? me.Count(w.Def.AmmoId) : 0,
                ReloadProgress = w != null && w.ReloadLeft > 0 ? 1f - w.ReloadLeft / w.Def.ReloadTime : 0,
                SpreadDeg = World.CurrentSpreadDeg(me),
                Bandages = me.Count("bandage"),
                Current = me.Current,
                Hotbar = me.Weapons.ConvertAll(x => x.Def.Name).ToArray(),
            };
            foreach (var it in me.Inventory) s.Inventory.Add((Items.Name(it.Id), it.Count));
            foreach (var b in World.Bags)
                if (!b.Empty && Vector2.Distance(b.Pos, me.Pos) < 1.9f) { s.NearLoot = true; break; }
            return s;
        }

        /// <summary>Серверный туман войны: клиент не получает тех, кого не видит (защита от wallhack).</summary>
        bool IsVisibleTo(Actor viewer, Actor target)
        {
            if (viewer.Dead) return Vector2.Distance(viewer.Pos, target.Pos) < 30;
            if (World.CanSee(viewer, target.Pos, World.ViewRange(viewer), World.ViewCone(viewer))) return true;
            // выстрел выдаёт стрелка, если между нами нет стен
            bool shotRecently = World.Time - target.LastShotTime < 0.6f;
            return shotRecently && Vector2.Distance(viewer.Pos, target.Pos) < 60 && World.HasLineOfSight(viewer.Pos, target.Pos);
        }

        bool IsPointVisible(Actor viewer, Vector2 p) =>
            Vector2.Distance(viewer.Pos, p) < 3 || World.CanSee(viewer, p, World.ViewRange(viewer), World.ViewCone(viewer));
    }
}
