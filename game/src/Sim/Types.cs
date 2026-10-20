using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>Ввод одного тика. Одинаковый для игрока и бота: боты управляют персонажем так же, как люди.</summary>
    public struct InputCmd
    {
        public Vector2 Move;      // направление в плоскости XZ, длина до 1
        public Vector2 Aim;       // точка прицела в мире
        public bool Fire;         // зажата ли стрельба
        public bool Sprint;
        public bool Reload;       // разовые нажатия (действуют один тик)
        public bool Interact;
        public bool Heal;
        public bool Respawn;
        public int SelectSlot;    // -1 — без смены оружия

        public static InputCmd Empty => new InputCmd { SelectSlot = -1 };
    }

    public sealed class WeaponDef
    {
        public string Id;
        public string Name;
        public string AmmoId;
        public float Damage;
        public float FireInterval;
        public int Magazine;
        public float ReloadTime;
        public float BaseSpreadDeg;
        public float BloomPerShotDeg;
        public float MaxBloomDeg;
        public float BloomRecoverDeg;   // градусов в секунду
        public float BulletSpeed;
        public float Range;
        public bool Auto;
        public float NoiseRadius;

        public static readonly WeaponDef Pipe = new WeaponDef
        {
            Id = "weapon_pipe", Name = "Самопал", AmmoId = "ammo_pistol",
            Damage = 22, FireInterval = 0.2f, Magazine = 10, ReloadTime = 1.7f,
            BaseSpreadDeg = 2.5f, BloomPerShotDeg = 2f, MaxBloomDeg = 9f, BloomRecoverDeg = 14f,
            BulletSpeed = 110, Range = 40, Auto = false, NoiseRadius = 45,
        };

        public static readonly WeaponDef Ak = new WeaponDef
        {
            Id = "weapon_ak", Name = "Калаш", AmmoId = "ammo_rifle",
            Damage = 30, FireInterval = 0.12f, Magazine = 30, ReloadTime = 2.5f,
            BaseSpreadDeg = 1.2f, BloomPerShotDeg = 1.1f, MaxBloomDeg = 8f, BloomRecoverDeg = 11f,
            BulletSpeed = 150, Range = 65, Auto = true, NoiseRadius = 70,
        };

        public static readonly Dictionary<string, WeaponDef> All = new()
        {
            [Pipe.Id] = Pipe,
            [Ak.Id] = Ak,
        };
    }

    public sealed class WeaponState
    {
        public WeaponDef Def;
        public int Ammo;
        public float Cooldown;
        public float Bloom;
        public float ReloadLeft;   // > 0 — идёт перезарядка
        public WeaponState(WeaponDef def, int ammo) { Def = def; Ammo = ammo; }
    }

    public sealed class ItemStack
    {
        public string Id;
        public int Count;
        public ItemStack(string id, int count) { Id = id; Count = count; }
    }

    public static class Items
    {
        public static readonly Dictionary<string, string> Names = new()
        {
            ["ammo_pistol"] = "Патроны (пист.)",
            ["ammo_rifle"] = "Патроны 5.56",
            ["scrap"] = "Металлолом",
            ["cloth"] = "Ткань",
            ["bandage"] = "Бинт",
            ["metal_fragments"] = "Металл",
            ["gunpowder"] = "Порох",
        };

        public static string Name(string id) =>
            Names.TryGetValue(id, out var n) ? n : WeaponDef.All.TryGetValue(id, out var w) ? w.Name : id;
    }

    public enum Team { Player = 0, Npc = 1 }

    public sealed class Actor
    {
        public int Id;
        public string Name;
        public Team Team;
        public Vector2 Pos;
        public Vector2 Vel;
        public float Facing;            // радианы, atan2(y, x)
        public float Radius = 0.35f;
        public float Hp = 100, MaxHp = 100;
        public bool Dead;
        public float DeadTime;
        public int LastAttacker = -1;
        public float LastShotTime = -100;
        public float LastDamagedTime = -100;
        public float HealCooldown;
        public bool FireHeldLastTick;
        public readonly List<WeaponState> Weapons = new();
        public int Current;
        public readonly List<ItemStack> Inventory = new();

        public WeaponState Weapon => Weapons.Count == 0 ? null : Weapons[Current];
        public Vector2 FacingDir => new Vector2(System.MathF.Cos(Facing), System.MathF.Sin(Facing));

        public int Count(string id)
        {
            int n = 0;
            foreach (var s in Inventory) if (s.Id == id) n += s.Count;
            return n;
        }

        public void Add(string id, int count)
        {
            if (count <= 0) return;
            foreach (var s in Inventory) if (s.Id == id) { s.Count += count; return; }
            Inventory.Add(new ItemStack(id, count));
        }

        public int Take(string id, int count)
        {
            int taken = 0;
            for (int i = Inventory.Count - 1; i >= 0 && taken < count; i--)
            {
                var s = Inventory[i];
                if (s.Id != id) continue;
                int t = System.Math.Min(s.Count, count - taken);
                s.Count -= t; taken += t;
                if (s.Count == 0) Inventory.RemoveAt(i);
            }
            return taken;
        }
    }

    public sealed class Wall
    {
        public Vector2 Min, Max;
        public float Height;            // >= 2 — высокая (закрывает обзор), иначе низкое укрытие
        public bool BlocksVision => Height >= 2f;
        public Wall(Vector2 min, Vector2 max, float h) { Min = min; Max = max; Height = h; }
    }

    public sealed class Projectile
    {
        public int Id;
        public int OwnerId;
        public Vector2 Pos;
        public Vector2 Dir;
        public float Speed;
        public float RangeLeft;
        public float Damage;
    }

    /// <summary>Контейнер с лутом: труп, сумка или ящик на РТ.</summary>
    public sealed class LootBag
    {
        public int Id;
        public Vector2 Pos;
        public bool IsCrate;            // ящик на РТ (респавнится), иначе сумка с трупа
        public float Despawn;           // для сумок
        public float RespawnLeft;       // для ящиков
        public readonly List<ItemStack> Items = new();
        public readonly List<WeaponState> Weapons = new();
        public bool Empty => Items.Count == 0 && Weapons.Count == 0;
    }

    public enum SimEventKind { Shot, HitActor, HitWall, Death, Loot, Respawn }

    public struct SimEvent
    {
        public SimEventKind Kind;
        public int ActorId;     // кто стрелял / кто получил урон / кто умер / кто лутал
        public int OtherId;     // атакующий
        public Vector2 Pos;
        public Vector2 Dir;
        public float Value;     // урон
        public string Text;
    }
}
