using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    public enum WallKind { Concrete, Crate, Rock, Tree }

    public sealed class MapWall
    {
        public Wall Wall;
        public WallKind Kind;
    }

    public struct Noise
    {
        public Vector2 Pos;
        public float Radius;
        public int SourceId;
        public float Time;
    }

    /// <summary>Авторитарная симуляция мира. Ничего не знает про Godot.</summary>
    public sealed class World
    {
        public const float Dt = 1f / 30f;
        public const float HalfSize = 60f;
        public const float WalkSpeed = 4.5f;
        public const float SprintSpeed = 6.8f;
        public const float Accel = 40f;
        public const float NpcRespawnTime = 45f;

        public float Time;
        public uint Tick;
        public readonly Random Rng;
        public readonly List<Actor> Actors = new();
        public readonly List<MapWall> Walls = new();
        public readonly List<Projectile> Projectiles = new();
        public readonly List<LootBag> Bags = new();
        public readonly List<SimEvent> Events = new();
        public readonly List<Noise> Noises = new();
        public readonly Dictionary<int, Vector2> NpcHomes = new();
        public readonly List<Vector2> PlayerSpawns = new();

        int _nextId = 1;

        public World(int seed)
        {
            Rng = new Random(seed);
            BuildMap();
        }

        public int NextId() => _nextId++;

        // ---------- карта ----------

        void AddWall(float x1, float y1, float x2, float y2, float h, WallKind kind)
        {
            var min = new Vector2(MathF.Min(x1, x2), MathF.Min(y1, y2));
            var max = new Vector2(MathF.Max(x1, x2), MathF.Max(y1, y2));
            Walls.Add(new MapWall { Wall = new Wall(min, max, h), Kind = kind });
        }

        void BuildMap()
        {
            const float t = 0.35f, h = 2.6f;
            // РТ: огороженный склад (x 5..35, y -35..-5) с проходами
            AddWall(5, -35 - t, 18, -35 + t, h, WallKind.Concrete);
            AddWall(22, -35 - t, 35, -35 + t, h, WallKind.Concrete);
            AddWall(5, -5 - t, 17, -5 + t, h, WallKind.Concrete);
            AddWall(23, -5 - t, 35, -5 + t, h, WallKind.Concrete);
            AddWall(5 - t, -35, 5 + t, -22, h, WallKind.Concrete);
            AddWall(5 - t, -18, 5 + t, -5, h, WallKind.Concrete);
            AddWall(35 - t, -35, 35 + t, -5, h, WallKind.Concrete);
            // здание внутри
            AddWall(11, -29, 19, -28.4f, h, WallKind.Concrete);
            AddWall(11, -29, 11.6f, -21, h, WallKind.Concrete);
            AddWall(18.4f, -29, 19, -24, h, WallKind.Concrete);
            AddWall(11, -21.6f, 16, -21, h, WallKind.Concrete);
            // укрытия внутри
            AddWall(24, -14, 26, -12.8f, 1.0f, WallKind.Crate);
            AddWall(28, -26, 29.2f, -24, 1.0f, WallKind.Crate);
            AddWall(21, -31, 23, -30, 1.0f, WallKind.Crate);
            AddWall(8, -12, 10, -11, 1.0f, WallKind.Crate);

            AddCrate(new Vector2(14, -25));
            AddCrate(new Vector2(31, -31));
            AddCrate(new Vector2(30, -9));

            foreach (var p in new[] { new Vector2(15, -24), new Vector2(28, -30), new Vector2(29, -10),
                                      new Vector2(10, -15), new Vector2(22, -20), new Vector2(20, 2) })
                NpcHomes[-NpcHomes.Count - 1] = p;   // ключи временные, заменяются при спавне

            // случайные укрытия, камни и деревья вне РТ
            for (int i = 0; i < 150; i++)
            {
                var p = new Vector2(Range(-HalfSize + 3, HalfSize - 3), Range(-HalfSize + 3, HalfSize - 3));
                if (p.X > 0 && p.X < 40 && p.Y > -40 && p.Y < 0) continue;     // не внутри РТ
                if (p.X < -35 && p.Y > 30) continue;                            // не на спавне
                int roll = Rng.Next(10);
                if (roll < 5)
                {
                    float r = Range(0.35f, 0.55f);
                    AddWall(p.X - r, p.Y - r, p.X + r, p.Y + r, 5f, WallKind.Tree);
                }
                else if (roll < 8)
                {
                    float w = Range(0.8f, 2.2f), d = Range(0.7f, 1.6f);
                    AddWall(p.X - w / 2, p.Y - d / 2, p.X + w / 2, p.Y + d / 2, 1.1f, WallKind.Rock);
                }
                else
                {
                    bool horiz = Rng.Next(2) == 0;
                    float len = Range(3f, 7f);
                    if (horiz) AddWall(p.X - len / 2, p.Y - t, p.X + len / 2, p.Y + t, h, WallKind.Concrete);
                    else AddWall(p.X - t, p.Y - len / 2, p.X + t, p.Y + len / 2, h, WallKind.Concrete);
                }
            }

            for (int i = 0; i < 6; i++)
                PlayerSpawns.Add(new Vector2(Range(-55, -42), Range(35, 55)));
        }

        float Range(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

        void AddCrate(Vector2 pos)
        {
            var bag = new LootBag { Id = NextId(), Pos = pos, IsCrate = true };
            FillCrate(bag);
            Bags.Add(bag);
        }

        void FillCrate(LootBag bag)
        {
            bag.Items.Clear();
            bag.Weapons.Clear();
            void Maybe(double chance, string id, int min, int max)
            {
                if (Rng.NextDouble() < chance) bag.Items.Add(new ItemStack(id, Rng.Next(min, max + 1)));
            }
            Maybe(0.5, "ammo_rifle", 15, 40);
            Maybe(0.6, "ammo_pistol", 10, 30);
            Maybe(0.8, "scrap", 5, 25);
            Maybe(0.5, "bandage", 1, 2);
            Maybe(0.5, "metal_fragments", 20, 60);
            Maybe(0.35, "gunpowder", 10, 30);
            if (Rng.NextDouble() < 0.15) bag.Weapons.Add(new WeaponState(WeaponDef.Ak, 0));
            if (bag.Empty) bag.Items.Add(new ItemStack("scrap", 5));
        }

        // ---------- актёры ----------

        public Actor SpawnPlayer(string name)
        {
            var a = new Actor { Id = NextId(), Name = name, Team = Team.Player };
            Actors.Add(a);
            RespawnPlayer(a);
            return a;
        }

        public void RespawnPlayer(Actor a)
        {
            a.Pos = PlayerSpawns[Rng.Next(PlayerSpawns.Count)];
            a.Vel = Vector2.Zero;
            a.Hp = a.MaxHp;
            a.Dead = false;
            a.Weapons.Clear();
            a.Inventory.Clear();
            a.Weapons.Add(new WeaponState(WeaponDef.Pipe, WeaponDef.Pipe.Magazine));
            a.Current = 0;
            a.Add("ammo_pistol", 30);
            a.Add("bandage", 2);
            Events.Add(new SimEvent { Kind = SimEventKind.Respawn, ActorId = a.Id, Pos = a.Pos });
        }

        public List<Actor> SpawnNpcs()
        {
            var homes = new List<Vector2>(NpcHomes.Values);
            NpcHomes.Clear();
            var list = new List<Actor>();
            foreach (var home in homes)
            {
                var a = new Actor { Id = NextId(), Name = "Охранник", Team = Team.Npc };
                NpcHomes[a.Id] = home;
                Actors.Add(a);
                RespawnNpc(a);
                list.Add(a);
            }
            return list;
        }

        void RespawnNpc(Actor a)
        {
            a.Pos = NpcHomes[a.Id];
            a.Vel = Vector2.Zero;
            a.Hp = a.MaxHp;
            a.Dead = false;
            a.Weapons.Clear();
            a.Inventory.Clear();
            a.Weapons.Add(new WeaponState(WeaponDef.Ak, WeaponDef.Ak.Magazine));
            a.Current = 0;
            a.Add("ammo_rifle", Rng.Next(30, 75));
            a.Add("scrap", Rng.Next(3, 15));
            if (Rng.NextDouble() < 0.4) a.Add("bandage", 1);
        }

        public Actor Find(int id)
        {
            foreach (var a in Actors) if (a.Id == id) return a;
            return null;
        }

        // ---------- восприятие ----------

        public bool HasLineOfSight(Vector2 a, Vector2 b)
        {
            foreach (var w in Walls)
            {
                if (!w.Wall.BlocksVision) continue;
                if (Geo.SegmentAabb(a, b, w.Wall.Min, w.Wall.Max, out _)) return false;
            }
            return true;
        }

        /// <summary>Видит ли наблюдатель цель: конус обзора, дальность, стены. Вплотную чувствуется всегда.</summary>
        public bool CanSee(Actor viewer, Vector2 target, float range = 45f, float coneDeg = 120f)
        {
            var d = target - viewer.Pos;
            float dist = d.Length();
            if (dist > range) return false;
            if (dist < 2.5f) return true;
            float ang = MathF.Atan2(d.Y, d.X);
            if (MathF.Abs(Geo.AngleDiff(viewer.Facing, ang)) > coneDeg * 0.5f * MathF.PI / 180f) return false;
            return HasLineOfSight(viewer.Pos, target);
        }

        // ---------- шаг ----------

        public void Step(Dictionary<int, InputCmd> inputs)
        {
            Events.Clear();
            Time += Dt;
            Tick++;
            Noises.RemoveAll(n => Time - n.Time > 3f);

            foreach (var a in Actors)
            {
                inputs.TryGetValue(a.Id, out var cmd);
                if (a.Dead)
                {
                    if (a.Team == Team.Npc && Time - a.DeadTime > NpcRespawnTime) RespawnNpc(a);
                    else if (a.Team == Team.Player && cmd.Respawn) RespawnPlayer(a);
                    continue;
                }
                StepActor(a, cmd);
            }

            SeparateActors();
            StepProjectiles();
            StepBags();
        }

        void StepActor(Actor a, InputCmd cmd)
        {
            // оружие
            if (cmd.SelectSlot >= 0 && cmd.SelectSlot < a.Weapons.Count && cmd.SelectSlot != a.Current)
            {
                a.Weapon.ReloadLeft = 0;
                a.Current = cmd.SelectSlot;
            }

            // движение
            var move = cmd.Move;
            if (move.LengthSquared() > 1) move = Vector2.Normalize(move);
            bool sprint = cmd.Sprint && move.LengthSquared() > 0.01f;
            float speed = sprint ? SprintSpeed : WalkSpeed;
            a.Vel = Geo.MoveToward(a.Vel, move * speed, Accel * Dt);
            a.Pos += a.Vel * Dt;
            for (int pass = 0; pass < 2; pass++)
                foreach (var w in Walls)
                    a.Pos = Geo.PushOut(a.Pos, a.Radius, w.Wall.Min, w.Wall.Max);
            a.Pos = Vector2.Clamp(a.Pos, new Vector2(-HalfSize + a.Radius), new Vector2(HalfSize - a.Radius));

            // взгляд
            var toAim = cmd.Aim - a.Pos;
            if (toAim.LengthSquared() > 0.04f) a.Facing = MathF.Atan2(toAim.Y, toAim.X);

            // лечение
            a.HealCooldown -= Dt;
            if (cmd.Heal && a.HealCooldown <= 0 && a.Hp < a.MaxHp && a.Take("bandage", 1) > 0)
            {
                a.Hp = MathF.Min(a.MaxHp, a.Hp + 25);
                a.HealCooldown = 2f;
            }

            StepWeapon(a, cmd, sprint);

            if (cmd.Interact) TryLoot(a);
        }

        void StepWeapon(Actor a, InputCmd cmd, bool sprint)
        {
            var w = a.Weapon;
            if (w == null) return;
            var def = w.Def;
            w.Cooldown -= Dt;
            w.Bloom = MathF.Max(0, w.Bloom - def.BloomRecoverDeg * Dt);

            if (w.ReloadLeft > 0)
            {
                w.ReloadLeft -= Dt;
                if (w.ReloadLeft <= 0)
                {
                    w.ReloadLeft = 0;
                    w.Ammo += a.Take(def.AmmoId, def.Magazine - w.Ammo);
                }
            }
            else if (cmd.Reload && w.Ammo < def.Magazine && a.Count(def.AmmoId) > 0)
            {
                w.ReloadLeft = def.ReloadTime;
            }

            bool trigger = def.Auto ? cmd.Fire : cmd.Fire && !a.FireHeldLastTick;
            a.FireHeldLastTick = cmd.Fire;
            if (!trigger || sprint || w.ReloadLeft > 0 || w.Cooldown > 0) return;

            if (w.Ammo <= 0)
            {
                if (a.Count(def.AmmoId) > 0) w.ReloadLeft = def.ReloadTime;
                return;
            }

            w.Ammo--;
            w.Cooldown = def.FireInterval;
            float spreadDeg = CurrentSpreadDeg(a);
            w.Bloom = MathF.Min(def.MaxBloomDeg, w.Bloom + def.BloomPerShotDeg);
            // разброс: сумма двух случайных — чаще ближе к центру
            float r = ((float)Rng.NextDouble() + (float)Rng.NextDouble() - 1f) * 0.5f;
            float ang = a.Facing + r * spreadDeg * MathF.PI / 180f;
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            var muzzle = a.Pos + dir * (a.Radius + 0.35f);
            Projectiles.Add(new Projectile
            {
                Id = NextId(), OwnerId = a.Id, Pos = muzzle, Dir = dir,
                Speed = def.BulletSpeed, RangeLeft = def.Range, Damage = def.Damage,
            });
            a.LastShotTime = Time;
            Noises.Add(new Noise { Pos = a.Pos, Radius = def.NoiseRadius, SourceId = a.Id, Time = Time });
            Events.Add(new SimEvent { Kind = SimEventKind.Shot, ActorId = a.Id, Pos = muzzle, Dir = dir });
        }

        /// <summary>Полный разброс в градусах: база + отдача + штраф за движение.</summary>
        public float CurrentSpreadDeg(Actor a)
        {
            var w = a.Weapon;
            if (w == null) return 0;
            float moving = a.Vel.Length() / WalkSpeed;
            return w.Def.BaseSpreadDeg + w.Bloom + MathF.Min(1.5f, moving) * 3f;
        }

        void TryLoot(Actor a)
        {
            LootBag best = null;
            float bestD = 1.9f;
            foreach (var b in Bags)
            {
                if (b.Empty) continue;
                float d = Vector2.Distance(a.Pos, b.Pos);
                if (d < bestD) { bestD = d; best = b; }
            }
            if (best == null) return;

            var parts = new List<string>();
            foreach (var s in best.Items)
            {
                a.Add(s.Id, s.Count);
                parts.Add($"{Items.Name(s.Id)} ×{s.Count}");
            }
            best.Items.Clear();
            for (int i = best.Weapons.Count - 1; i >= 0; i--)
            {
                var w = best.Weapons[i];
                var owned = a.Weapons.Find(x => x.Def == w.Def);
                if (owned != null)
                {
                    if (w.Ammo > 0) { a.Add(w.Def.AmmoId, w.Ammo); parts.Add($"{Items.Name(w.Def.AmmoId)} ×{w.Ammo}"); }
                }
                else if (a.Weapons.Count < 3)
                {
                    a.Weapons.Add(w);
                    parts.Add(w.Def.Name);
                }
                else continue;
                best.Weapons.RemoveAt(i);
            }
            if (best.IsCrate && best.Empty) best.RespawnLeft = 90f;
            Events.Add(new SimEvent { Kind = SimEventKind.Loot, ActorId = a.Id, Pos = best.Pos, Text = string.Join(", ", parts) });
        }

        void SeparateActors()
        {
            for (int i = 0; i < Actors.Count; i++)
            {
                var a = Actors[i];
                if (a.Dead) continue;
                for (int j = i + 1; j < Actors.Count; j++)
                {
                    var b = Actors[j];
                    if (b.Dead) continue;
                    var d = b.Pos - a.Pos;
                    float min = a.Radius + b.Radius;
                    float dist = d.Length();
                    if (dist >= min || dist < 1e-5f) continue;
                    var push = d / dist * (min - dist) * 0.5f;
                    a.Pos -= push;
                    b.Pos += push;
                }
            }
        }

        void StepProjectiles()
        {
            for (int i = Projectiles.Count - 1; i >= 0; i--)
            {
                var p = Projectiles[i];
                float stepLen = MathF.Min(p.Speed * Dt, p.RangeLeft);
                var a = p.Pos;
                var b = p.Pos + p.Dir * stepLen;
                var owner = Find(p.OwnerId);

                float bestT = 2;
                Actor hitActor = null;
                foreach (var w in Walls)
                    if (Geo.SegmentAabb(a, b, w.Wall.Min, w.Wall.Max, out float t) && t < bestT) bestT = t;
                foreach (var act in Actors)
                {
                    if (act.Dead || act.Id == p.OwnerId) continue;
                    if (owner != null && owner.Team == Team.Npc && act.Team == Team.Npc) continue;
                    if (Geo.SegmentCircle(a, b, act.Pos, act.Radius, out float t) && t < bestT)
                    {
                        bestT = t;
                        hitActor = act;
                    }
                }

                if (bestT <= 1)
                {
                    var hitPos = a + (b - a) * bestT;
                    if (hitActor != null) Damage(hitActor, p.Damage, p.OwnerId, hitPos);
                    else Events.Add(new SimEvent { Kind = SimEventKind.HitWall, Pos = hitPos, Dir = p.Dir, OtherId = p.OwnerId });
                    Projectiles.RemoveAt(i);
                    continue;
                }

                p.Pos = b;
                p.RangeLeft -= stepLen;
                if (p.RangeLeft <= 0) Projectiles.RemoveAt(i);
            }
        }

        public void Damage(Actor target, float dmg, int attackerId, Vector2 pos)
        {
            target.Hp -= dmg;
            target.LastAttacker = attackerId;
            target.LastDamagedTime = Time;
            Events.Add(new SimEvent { Kind = SimEventKind.HitActor, ActorId = target.Id, OtherId = attackerId, Pos = pos, Value = dmg });
            if (target.Hp <= 0) Kill(target, attackerId);
        }

        void Kill(Actor a, int killerId)
        {
            a.Hp = 0;
            a.Dead = true;
            a.DeadTime = Time;
            var bag = new LootBag { Id = NextId(), Pos = a.Pos, Despawn = 300f };
            foreach (var s in a.Inventory) bag.Items.Add(s);
            foreach (var w in a.Weapons) { w.ReloadLeft = 0; bag.Weapons.Add(w); }
            a.Inventory.Clear();
            a.Weapons.Clear();
            if (!bag.Empty) Bags.Add(bag);
            Events.Add(new SimEvent { Kind = SimEventKind.Death, ActorId = a.Id, OtherId = killerId, Pos = a.Pos });
        }

        void StepBags()
        {
            for (int i = Bags.Count - 1; i >= 0; i--)
            {
                var b = Bags[i];
                if (b.IsCrate)
                {
                    if (b.Empty)
                    {
                        b.RespawnLeft -= Dt;
                        if (b.RespawnLeft <= 0) FillCrate(b);
                    }
                }
                else
                {
                    b.Despawn -= Dt;
                    if (b.Despawn <= 0 || b.Empty) Bags.RemoveAt(i);
                }
            }
        }
    }
}
