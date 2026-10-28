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
        public int Version = 7;   // 7: прогрессия D54 (жидкость в ёмкостях, станции: вкл/выкл, отравление; старые сейвы читаются как есть)   // 6: сетка клеток D52 (у установленного — пол, слоты, поворот, хитбокс; старые сейвы пересаживаются в слоты)   // 5: одежда D47 (слоты, пластины с прочностью, доза, переохлаждение; старые сейвы — голыми)   // 4: ветка оружия D46 (калибры, обвесы, износ; старые id переводятся при загрузке)   // 3: рюкзак ячейками   // 2: остров 1 км, малые РТ
        public int Seed;
        public float Time, TimeOffset;
        public int NextId;
        public int WxSky = -1, WxIndex; public float WxLeft = -1, WxWet, WxSnow, WxWindA, WxWindS;   // погода (−1 — старый сейв: цикл начнётся заново)
        public DateTime SavedAtUtc;
        public List<NodeS> Nodes = new();
        public List<(int kind, float left)> NodeRespawns = new();
        public List<StructS> Structures = new();
        public List<DeployS> Deployables = new();
        public List<BagS> Bags = new();
        public List<float> PickupRespawn = new();
        public List<PlayerS> Players = new();

        public sealed class NodeS { public int Id, Kind, Look; public float X, Y, R, Amount, Max; }   // Look — порода и возраст дерева (в старых сейвах 0 — вид по биому)
        public sealed class SlotS { public string Id; public int Count, Ammo = -1; public float Dur = -1; public List<string> Att; public List<float> AttDur; public string Liquid; public float Liters; }   // v7: жидкость в ёмкости
        public sealed class StructS { public int Id, Kind, I, J, Dir, Tier, OwnerId, ClaimId, Code, LockOwner; public float Hp, Ax, Ay, Bx, By, Cx, Cy; public bool Open, Locked, KeyLock, Shutters; public List<int> LockAuth = new(); }   // v7: Shutters — ставни на окне
        public sealed class DeployS
        {
            public int Id, Kind, OwnerId, Ore, SulfurOre, OutMetal, OutSulfur, OutCharcoal, Level;
            public int Rot, FloorId, Slots;   // v6
            public float Hx, Hy;              // v6: хитбокс (половины сторон)
            public float X, Y, Hp, Fuel, Grace, ReadyAt;
            public bool Off;                  // v7: станция выключена (по умолчанию — включена, как в старых сейвах)
            public float Work;                // v7: таймер работы станции
            public List<int> Auth = new();
            public Dictionary<string, float> Store = new();
            public List<SlotS> Storage;
        }
        public sealed class BagS { public int Id, Tier = 2; public float X, Y, Despawn, RespawnLeft; public bool IsCrate, Corpse, Safe, Unlocked; public string Rt, Box; public List<SlotS> Items = new(); }
        public sealed class PlayerS
        {
            public int Id, Current, BuildPiece;
            public string Name;
            public float X, Y, Hp, Food, Water;
            public bool Dead;
            public List<SlotS> Belt = new();
            public List<SlotS> Inventory = new();
            public List<string> Known = new();
            public List<SlotS> Worn;      // v5: одежда по слотам WearSlot
            public List<SlotS> Plates;    // v5: пластины перед/спина (Dur — прочность)
            public float RadDose, Cold;
            public float Poison;          // v7: отравление сырой водой
        }

        static readonly JsonSerializerOptions Opts = new() { IncludeFields = true, WriteIndented = false };
        public string ToJson() => JsonSerializer.Serialize(this, Opts);
        public static WorldSave FromJson(string json) => JsonSerializer.Deserialize<WorldSave>(json, Opts);
    }

    public sealed partial class World
    {
        public int Seed;

        static WorldSave.SlotS SaveSlot(ItemSlot s)
        {
            if (s == null) return null;
            var ss = new WorldSave.SlotS { Id = s.Id, Count = s.Count, Ammo = s.State?.Ammo ?? -1, Dur = s.State?.Durability ?? s.Dur };   // у пластины — её прочность
            if (s.State != null && s.State.AttCount > 0) { ss.Att = new List<string>(s.State.Att); ss.AttDur = new List<float>(s.State.AttDur); }
            if (s.Liquid != null) { ss.Liquid = s.Liquid; ss.Liters = s.Liters; }
            return ss;
        }

        /// <summary>Загрузка ячейки: старые id (ammo_pistol, ammo_rifle, weapon_ak, weapon_pipe) переводятся в новые (D46).</summary>
        static ItemSlot LoadSlot(WorldSave.SlotS s)
        {
            if (s == null) return null;
            string id = Weaponry.Migrate(s.Id);
            if (Items.Get(id) == null) return null;
            var slot = Items.NewSlot(id, Math.Max(0, s.Ammo));
            slot.Count = s.Count;
            if (slot.State is WeaponState w)
            {
                if (s.Dur >= 0 && w.Def.MaxDurability > 0) w.Durability = Math.Min(s.Dur, w.Def.MaxDurability);
                if (s.Att != null)
                    for (int k = 0; k < w.Att.Length && k < s.Att.Count; k++)
                    {
                        if (Weaponry.Att(s.Att[k]) == null) continue;
                        w.Att[k] = s.Att[k];
                        w.AttDur[k] = s.AttDur != null && k < s.AttDur.Count ? s.AttDur[k] : Weaponry.Att(s.Att[k]).MaxShots;
                    }
                w.Ammo = Math.Min(w.Ammo, w.MagSize);
            }
            else if (s.Dur >= 0 && Clothing.Plate(id) is PlateDef pd) slot.Dur = Math.Min(s.Dur, pd.Durability);
            if (s.Liquid != null && Liquids.Fits(slot, s.Liquid)) { slot.Liquid = s.Liquid; slot.Liters = Math.Min(s.Liters, slot.Def.Capacity); }
            return slot;
        }

        public WorldSave Save()
        {
            var w = new WorldSave { Seed = Seed, Time = Time, TimeOffset = TimeOffset, NextId = _nextId, SavedAtUtc = DateTime.UtcNow,
                WxSky = (int)Sky, WxIndex = WxIndex, WxLeft = WxLeft, WxWet = Wetness, WxSnow = SnowCover, WxWindA = WindAngle, WxWindS = WindStrength };
            foreach (var n in Nodes) w.Nodes.Add(new WorldSave.NodeS { Id = n.Id, Kind = (int)n.Kind, X = n.Pos.X, Y = n.Pos.Y, R = n.Radius, Amount = n.Amount, Max = n.MaxAmount, Look = n.Look });
            foreach (var (k, left) in _nodeRespawns) w.NodeRespawns.Add(((int)k, left));
            foreach (var s in Structures)
            {
                var ss = new WorldSave.StructS { Id = s.Id, Kind = (int)s.Kind, I = s.I, J = s.J, Dir = s.Dir, Tier = s.Tier, OwnerId = s.OwnerId, ClaimId = s.ClaimId, Hp = s.Hp, Open = s.Open, Locked = s.Locked, KeyLock = s.KeyLock, Shutters = s.Shutters, Code = s.Code, LockOwner = s.LockOwner, Ax = s.A.X, Ay = s.A.Y, Bx = s.B.X, By = s.B.Y, Cx = s.C.X, Cy = s.C.Y };
                ss.LockAuth.AddRange(s.LockAuth);
                w.Structures.Add(ss);
            }
            foreach (var d in Deployables)
            {
                var ds = new WorldSave.DeployS
                {
                    Id = d.Id, Kind = (int)d.Kind, OwnerId = d.OwnerId, X = d.Pos.X, Y = d.Pos.Y, Hp = d.Hp, Level = d.Level,
                    Ore = d.Ore, SulfurOre = d.SulfurOre, Fuel = d.Fuel, OutMetal = d.OutMetal, OutSulfur = d.OutSulfur, OutCharcoal = d.OutCharcoal, Grace = d.Grace, ReadyAt = d.ReadyAt - Time,
                    Rot = d.Rot, FloorId = d.FloorId, Slots = d.Slots, Hx = d.Half.X, Hy = d.Half.Y,
                    Off = !d.On && StationDefs.Get(d.Kind) != null, Work = d.Work,
                };
                ds.Auth.AddRange(d.Auth);
                foreach (var kv in d.Store) ds.Store[kv.Key] = kv.Value;
                if (d.Storage != null) ds.Storage = d.Storage.Items.ConvertAll(SaveSlot);
                w.Deployables.Add(ds);
            }
            foreach (var b in Bags)
                w.Bags.Add(new WorldSave.BagS { Id = b.Id, X = b.Pos.X, Y = b.Pos.Y, Despawn = b.Despawn, RespawnLeft = b.RespawnLeft, IsCrate = b.IsCrate, Corpse = b.Corpse, Safe = b.Safe, Unlocked = b.Unlocked, Tier = b.Tier, Rt = b.Rt, Box = b.Box, Items = b.Items.ConvertAll(SaveSlot) });
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
                ps.Worn = new List<WorldSave.SlotS>(); foreach (var s in a.Worn) ps.Worn.Add(SaveSlot(s));
                ps.Plates = new List<WorldSave.SlotS>(); foreach (var s in a.Plates) ps.Plates.Add(SaveSlot(s));
                ps.RadDose = a.RadDose; ps.Cold = a.Cold; ps.Poison = a.Poison;
                w.Players.Add(ps);
            }
            return w;
        }

        /// <summary>Применить сохранение к только что созданному миру с тем же зерном.</summary>
        public void Load(WorldSave w)
        {
            Time = w.Time;
            TimeOffset = w.TimeOffset;
            if (w.WxSky >= 0) { SetSky((Sky)w.WxSky); WxIndex = w.WxIndex; WxLeft = w.WxLeft; Wetness = w.WxWet; SnowCover = w.WxSnow; WindAngle = _windTarget = w.WxWindA; WindStrength = w.WxWindS; }

            foreach (var n in Nodes) _nodeGrid.Remove(n);
            Nodes.Clear();
            foreach (var n in w.Nodes)
            {
                var node = new ResourceNode { Id = n.Id, Kind = (NodeKind)n.Kind, Pos = new Vector2(n.X, n.Y), Radius = n.R, Amount = n.Amount, MaxAmount = n.Max, Look = (byte)n.Look };
                Nodes.Add(node);
                _nodeGrid.Insert(node, node.Pos - new Vector2(node.Radius), node.Pos + new Vector2(node.Radius));
            }
            EnsureTires();   // сохранение до покрышек — вернуть их на места
            _nodeRespawns.Clear();
            foreach (var (k, left) in w.NodeRespawns) _nodeRespawns.Add(((NodeKind)k, left));

            foreach (var d in Deployables) _deployGrid.Remove(d);
            Deployables.Clear();
            _slotUse.Clear();
            foreach (var ds in w.Deployables)
            {
                var d = new Deployable
                {
                    Id = ds.Id, Kind = (DeployKind)ds.Kind, OwnerId = ds.OwnerId, Pos = new Vector2(ds.X, ds.Y), Hp = ds.Hp, Level = ds.Level,
                    Ore = ds.Ore, SulfurOre = ds.SulfurOre, Fuel = ds.Fuel, OutMetal = ds.OutMetal, OutSulfur = ds.OutSulfur, OutCharcoal = ds.OutCharcoal, Grace = ds.Grace, ReadyAt = Time + ds.ReadyAt,
                    Rot = ds.Rot & 3, FloorId = ds.FloorId, Slots = ds.Slots, Half = new Vector2(ds.Hx, ds.Hy),
                    Work = ds.Work,
                };
                if (StationDefs.Get(d.Kind) != null) d.On = !ds.Off;
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
                    Hp = ss.Hp, Open = ss.Open, Locked = ss.Locked, KeyLock = ss.KeyLock, Code = ss.Code, Shutters = ss.Shutters,
                    LockOwner = ss.LockOwner != 0 ? ss.LockOwner : ss.OwnerId,   // сейв до владельца замка — замок ставил строитель двери
                };
                foreach (var id in ss.LockAuth) s.LockAuth.Add(id);
                (s.A, s.B, s.C) = (new Vector2(ss.Ax, ss.Ay), new Vector2(ss.Bx, ss.By), new Vector2(ss.Cx, ss.Cy));
                if (s.IsEdge && s.A == s.B) (s.A, s.B) = Building.EdgeEnds(s.I, s.J, s.Dir);   // сейв до треугольников: ребро по сетке
                if (s.Kind == StructKind.Foundation) _foundations[(s.I, s.J)] = s;
                else if (s.Kind == StructKind.TriFoundation) _tris.Add(s);
                else if (s.Kind == StructKind.Door) _doors[s.EdgeKey] = s;
                else _edges[s.EdgeKey] = s;
                Structures.Add(s);
                RebuildBoxes(s);
            }
            RebuildFloorEdges();
            // сетка D52: пол, на котором стояло, мог не сохраниться — тогда предмет вне сетки; сейв до сетки — пересадить в слоты
            foreach (var d in Deployables)
                if (d.FloorId != 0 && FloorById(d.FloorId) == null) { OccupySlots(d, false); d.FloorId = 0; d.Slots = 0; }
            if (w.Version < 6) ResnapLegacyDeploys();
            StructVersion++;

            Bags.Clear();
            foreach (var bs in w.Bags)
            {
                var b = new LootBag { Id = bs.Id, Pos = new Vector2(bs.X, bs.Y), Despawn = bs.Despawn, RespawnLeft = bs.RespawnLeft, IsCrate = bs.IsCrate, Corpse = bs.Corpse, Safe = bs.Safe, Unlocked = bs.Unlocked, Tier = bs.Tier, Rt = bs.Rt, Box = bs.Box };
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
                // одежда (v5); в старых сейвах её нет — персонаж голый
                if (ps.Worn != null)
                    for (int i = 0; i < Clothing.Slots && i < ps.Worn.Count; i++)
                        if (LoadSlot(ps.Worn[i]) is ItemSlot ws && Clothing.Def(ws.Id)?.Slot == (WearSlot)i) a.Worn[i] = ws;
                if (ps.Plates != null)
                    for (int i = 0; i < Clothing.PlateSlots && i < ps.Plates.Count; i++)
                        if (LoadSlot(ps.Plates[i]) is ItemSlot pl && Clothing.Plate(pl.Id) != null) a.Plates[i] = pl;
                a.RadDose = ps.RadDose; a.Cold = ps.Cold; a.Poison = ps.Poison;
                a.Known.Clear();
                if (ps.Known != null) foreach (var k in ps.Known) if (Tech.Get(Weaponry.Migrate(k)) != null) a.Known.Add(Weaponry.Migrate(k));
                Actors.Add(a);
            }
            _nextId = Math.Max(_nextId, w.NextId);
            FixupPrefabsAfterLoad();
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
                        foreach (var (cid, _) in Building.UpkeepCost(s.Kind, s.Tier))
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
