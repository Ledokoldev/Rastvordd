using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Сохранение мира (D24). Карта (рельеф, биомы, РТ) восстанавливается из зерна, в файл идёт только то, что меняется:
    /// ресурсы, постройки, установленное, сумки и ящики, игроки, время. Охрана РТ не сохраняется — заново встаёт на посты.
    /// </summary>
    public sealed class WorldSave
    {
        public int Version = 3;   // 3: рюкзак ячейками   // 2: остров 1 км, малые РТ
        public int Seed;
        public float Time, TimeOffset;
        public int NextId;
        public DateTime SavedAtUtc;
        public List<NodeS> Nodes = new();
        public List<(int kind, float left)> NodeRespawns = new();
        public List<StructS> Structures = new();
        public List<DeployS> Deployables = new();
        public List<BagS> Bags = new();
        public List<float> PickupRespawn = new();
        public List<PlayerS> Players = new();

        public sealed class NodeS { public int Id, Kind; public float X, Y, R, Amount, Max; }
        public sealed class SlotS { public string Id; public int Count, Ammo = -1; }
        public sealed class StructS { public int Id, Kind, I, J, Dir, Tier, OwnerId, ClaimId, Code; public float Hp; public bool Open, Locked, KeyLock; public List<int> LockAuth = new(); }
        public sealed class DeployS
        {
            public int Id, Kind, OwnerId, Ore, SulfurOre, OutMetal, OutSulfur, OutCharcoal, Level;
            public float X, Y, Hp, Fuel, Grace, ReadyAt;
            public List<int> Auth = new();
            public Dictionary<string, float> Store = new();
            public List<SlotS> Storage;
        }
        public sealed class BagS { public int Id, Tier = 2; public float X, Y, Despawn, RespawnLeft; public bool IsCrate, Corpse; public List<SlotS> Items = new(); }
        public sealed class PlayerS
        {
            public int Id, Current, BuildPiece;
            public string Name;
            public float X, Y, Hp, Food, Water;
            public bool Dead;
            public List<SlotS> Belt = new();
            public List<SlotS> Inventory = new();
            public List<string> Known = new();
        }

        static readonly JsonSerializerOptions Opts = new() { IncludeFields = true, WriteIndented = false };
        public string ToJson() => JsonSerializer.Serialize(this, Opts);
        public static WorldSave FromJson(string json) => JsonSerializer.Deserialize<WorldSave>(json, Opts);
    }

    public sealed partial class World
    {
        public int Seed;

        static WorldSave.SlotS SaveSlot(ItemSlot s) => s == null ? null : new WorldSave.SlotS { Id = s.Id, Count = s.Count, Ammo = s.State?.Ammo ?? -1 };

        static ItemSlot LoadSlot(WorldSave.SlotS s)
        {
            if (s == null || Items.Get(s.Id) == null) return null;
            var slot = Items.NewSlot(s.Id, Math.Max(0, s.Ammo));
            slot.Count = s.Count;
            return slot;
        }

        public WorldSave Save()
        {
            var w = new WorldSave { Seed = Seed, Time = Time, TimeOffset = TimeOffset, NextId = _nextId, SavedAtUtc = DateTime.UtcNow };
            foreach (var n in Nodes) w.Nodes.Add(new WorldSave.NodeS { Id = n.Id, Kind = (int)n.Kind, X = n.Pos.X, Y = n.Pos.Y, R = n.Radius, Amount = n.Amount, Max = n.MaxAmount });
            foreach (var (k, left) in _nodeRespawns) w.NodeRespawns.Add(((int)k, left));
            foreach (var s in Structures)
            {
                var ss = new WorldSave.StructS { Id = s.Id, Kind = (int)s.Kind, I = s.I, J = s.J, Dir = s.Dir, Tier = s.Tier, OwnerId = s.OwnerId, ClaimId = s.ClaimId, Hp = s.Hp, Open = s.Open, Locked = s.Locked, KeyLock = s.KeyLock, Code = s.Code };
                ss.LockAuth.AddRange(s.LockAuth);
                w.Structures.Add(ss);
            }
            foreach (var d in Deployables)
            {
                var ds = new WorldSave.DeployS
                {
                    Id = d.Id, Kind = (int)d.Kind, OwnerId = d.OwnerId, X = d.Pos.X, Y = d.Pos.Y, Hp = d.Hp, Level = d.Level,
                    Ore = d.Ore, SulfurOre = d.SulfurOre, Fuel = d.Fuel, OutMetal = d.OutMetal, OutSulfur = d.OutSulfur, OutCharcoal = d.OutCharcoal, Grace = d.Grace, ReadyAt = d.ReadyAt - Time,
                };
                ds.Auth.AddRange(d.Auth);
                foreach (var kv in d.Store) ds.Store[kv.Key] = kv.Value;
                if (d.Storage != null) ds.Storage = d.Storage.Items.ConvertAll(SaveSlot);
                w.Deployables.Add(ds);
            }
            foreach (var b in Bags)
                w.Bags.Add(new WorldSave.BagS { Id = b.Id, X = b.Pos.X, Y = b.Pos.Y, Despawn = b.Despawn, RespawnLeft = b.RespawnLeft, IsCrate = b.IsCrate, Corpse = b.Corpse, Tier = b.Tier, Items = b.Items.ConvertAll(SaveSlot) });
            foreach (var p in Pickups) w.PickupRespawn.Add(p.RespawnLeft);
            foreach (var a in Actors)
            {
                if (a.Team != Team.Player) continue;
                var ps = new WorldSave.PlayerS
                {
                    Id = a.Id, Name = a.Name, X = a.Pos.X, Y = a.Pos.Y, Hp = a.Hp, Food = a.Food, Water = a.Water, Dead = a.Dead,
                    Current = a.Current, BuildPiece = (int)a.BuildPiece,
                };
                foreach (var s in a.Belt) ps.Belt.Add(SaveSlot(s));
                foreach (var it in a.Pack) ps.Inventory.Add(SaveSlot(it));
                ps.Known.AddRange(a.Known);
                w.Players.Add(ps);
            }
            return w;
        }

        /// <summary>Применить сохранение к только что созданному миру с тем же зерном.</summary>
        public void Load(WorldSave w)
        {
            Time = w.Time;
            TimeOffset = w.TimeOffset;

            foreach (var n in Nodes) _nodeGrid.Remove(n);
            Nodes.Clear();
            foreach (var n in w.Nodes)
            {
                var node = new ResourceNode { Id = n.Id, Kind = (NodeKind)n.Kind, Pos = new Vector2(n.X, n.Y), Radius = n.R, Amount = n.Amount, MaxAmount = n.Max };
                Nodes.Add(node);
                _nodeGrid.Insert(node, node.Pos - new Vector2(node.Radius), node.Pos + new Vector2(node.Radius));
            }
            _nodeRespawns.Clear();
            foreach (var (k, left) in w.NodeRespawns) _nodeRespawns.Add(((NodeKind)k, left));

            foreach (var d in Deployables) _deployGrid.Remove(d);
            Deployables.Clear();
            foreach (var ds in w.Deployables)
            {
                var d = new Deployable
                {
                    Id = ds.Id, Kind = (DeployKind)ds.Kind, OwnerId = ds.OwnerId, Pos = new Vector2(ds.X, ds.Y), Hp = ds.Hp, Level = ds.Level,
                    Ore = ds.Ore, SulfurOre = ds.SulfurOre, Fuel = ds.Fuel, OutMetal = ds.OutMetal, OutSulfur = ds.OutSulfur, OutCharcoal = ds.OutCharcoal, Grace = ds.Grace, ReadyAt = Time + ds.ReadyAt,
                };
                foreach (var id in ds.Auth) d.Auth.Add(id);
                foreach (var kv in ds.Store) d.Store[kv.Key] = kv.Value;
                if (ds.Storage != null)
                {
                    d.Storage = new LootBag { Id = NextId(), Pos = d.Pos };
                    foreach (var s in ds.Storage) if (LoadSlot(s) is ItemSlot slot) d.Storage.Items.Add(slot);
                }
                AddDeployable(d);
            }

            foreach (var ss in w.Structures)
            {
                var s = new Structure
                {
                    Id = ss.Id, Kind = (StructKind)ss.Kind, I = ss.I, J = ss.J, Dir = ss.Dir, Tier = ss.Tier, OwnerId = ss.OwnerId, ClaimId = ss.ClaimId,
                    Hp = ss.Hp, Open = ss.Open, Locked = ss.Locked, KeyLock = ss.KeyLock, Code = ss.Code,
                };
                foreach (var id in ss.LockAuth) s.LockAuth.Add(id);
                if (s.Kind == StructKind.Foundation) _foundations[(s.I, s.J)] = s;
                else if (s.Kind == StructKind.Door) _doors[(s.I, s.J, s.Dir)] = s;
                else _edges[(s.I, s.J, s.Dir)] = s;
                Structures.Add(s);
                RebuildBoxes(s);
            }
            StructVersion++;

            Bags.Clear();
            foreach (var bs in w.Bags)
            {
                var b = new LootBag { Id = bs.Id, Pos = new Vector2(bs.X, bs.Y), Despawn = bs.Despawn, RespawnLeft = bs.RespawnLeft, IsCrate = bs.IsCrate, Corpse = bs.Corpse, Tier = bs.Tier };
                foreach (var s in bs.Items) if (LoadSlot(s) is ItemSlot slot) b.Items.Add(slot);
                Bags.Add(b);
            }
            for (int i = 0; i < Pickups.Count && i < w.PickupRespawn.Count; i++) Pickups[i].RespawnLeft = w.PickupRespawn[i];

            foreach (var ps in w.Players)
            {
                var a = new Actor { Id = ps.Id, Name = ps.Name, Team = Team.Player, Pos = new Vector2(ps.X, ps.Y), Hp = ps.Hp, Food = ps.Food, Water = ps.Water, Dead = ps.Dead };
                for (int i = 0; i < Actor.BeltSize && i < ps.Belt.Count; i++) a.Belt[i] = LoadSlot(ps.Belt[i]);
                for (int i = 0; i < Actor.PackSize && i < ps.Inventory.Count; i++) a.Pack[i] = LoadSlot(ps.Inventory[i]);
                a.Current = ps.Current;
                a.BuildPiece = (StructKind)ps.BuildPiece;
                a.Known.Clear();
                if (ps.Known != null) foreach (var k in ps.Known) a.Known.Add(k);
                Actors.Add(a);
            }
            _nextId = Math.Max(_nextId, w.NextId);
        }

        /// <summary>
        /// Досчёт офлайна (D24): мир прожил seconds без игроков. Упрощённо и сразу: столбы съедают запас,
        /// без запаса — отсрочка и гниение; ресурсы, ягоды и ящики РТ восстанавливаются, сумки исчезают.
        /// Возвращает короткий отчёт для игрока.
        /// </summary>
        public List<string> CatchUp(float seconds)
        {
            var report = new List<string>();
            if (seconds < 60) return report;
            float hours = seconds / 3600f;

            foreach (var claim in new List<Deployable>(Deployables))
            {
                if (claim.Kind != DeployKind.Claim) continue;
                var eaten = new List<string>();
                int lost = 0;
                foreach (var (id, perHour) in UpkeepPerHourOf(claim))
                {
                    if (perHour <= 0) continue;
                    float have = claim.Store.GetValueOrDefault(id);
                    float need = perHour * hours;
                    if (have >= need) { claim.Store[id] = have - need; eaten.Add($"{Items.Name(id)} {need:0}"); continue; }
                    claim.Store[id] = 0;
                    if (have > 0) eaten.Add($"{Items.Name(id)} {have:0}");
                    // запас кончился через have/perHour часов; дальше отсрочка, потом гниение 1%/мин
                    float rotMin = (seconds - have / perHour * 3600f - claim.Grace) / 60f;
                    if (rotMin <= 0) { claim.Grace -= seconds - have / perHour * 3600f; continue; }
                    claim.Grace = 0;
                    report.Add($"база без запаса гнила {rotMin:0} мин (−{MathF.Min(100, rotMin * DecayPerMinute * 100):0}% прочности)");
                    foreach (var s in new List<Structure>(Structures))
                    {
                        if (s.ClaimId != claim.Id) continue;
                        foreach (var (cid, _) in Building.Cost(s.Kind, s.Tier))
                            if (cid == id)
                            {
                                bool gone = s.Hp <= s.MaxHp * DecayPerMinute * rotMin;
                                DamageStructure(s, s.MaxHp * DecayPerMinute * rotMin, -1);
                                if (gone) lost++;
                                break;
                            }
                    }
                }
                if (eaten.Count > 0) report.Add("шкаф съел: " + string.Join(", ", eaten));
                if (lost > 0) report.Add($"без запаса сгнило элементов: {lost}");
            }

            for (int i = _nodeRespawns.Count - 1; i >= 0; i--)
                _nodeRespawns[i] = (_nodeRespawns[i].kind, _nodeRespawns[i].left - seconds);   // вырастут в первый же тик
            foreach (var p in Pickups) p.RespawnLeft = MathF.Max(0, p.RespawnLeft - seconds);
            for (int i = Bags.Count - 1; i >= 0; i--)
            {
                var b = Bags[i];
                if (b.IsCrate) { if (b.Empty && b.RespawnLeft > 0) { b.RespawnLeft = 0.01f; } }
                else if ((b.Despawn -= seconds) <= 0) Bags.RemoveAt(i);
            }
            Time += seconds;
            return report;
        }

        /// <summary>Игрок по имени (после загрузки — его сохранённый персонаж).</summary>
        public Actor FindPlayer(string name)
        {
            foreach (var a in Actors) if (a.Team == Team.Player && a.Name == name) return a;
            return null;
        }
    }
}
