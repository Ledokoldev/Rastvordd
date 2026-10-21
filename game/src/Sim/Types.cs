using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>Ввод одного тика. Одинаковый для игрока и бота: боты управляют персонажем так же, как люди.</summary>
    public struct InputCmd
    {
        public Vector2 Move;      // направление в плоскости XZ, длина до 1
        public Vector2 Aim;       // точка прицела в мире
        public bool Fire;         // зажата ли ЛКМ (стрельба / удар / установка)
        public bool Sprint;
        public bool Reload;       // разовые нажатия (действуют один тик)
        public bool Interact;
        public bool Heal;
        public bool Eat;
        public bool Respawn;
        public bool DebugKit;
        public bool ToggleTime;   // отладка: день/ночь
        public bool Crouch;       // присед (зажат Alt)
        public float Peek;        // 0..1: насколько игрок прицеливается (ПКМ)
        public int SelectSlot;    // -1 — без смены слота пояса
        public int Craft;         // индекс рецепта, -1 — ничего
        public int Code;          // введённый код замка, -1 — нет
        public int CodeDoor;      // id двери, к которой вводят код

        public static InputCmd Empty => new InputCmd { SelectSlot = -1, Craft = -1, Code = -1 };
    }

    public enum NodeKind { Tree, Stone, Metal, Sulfur }

    public sealed class WeaponDef
    {
        public string Id;
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

        // отдача: ствол уводит в сторону по узору (знаки по очереди выстрелов), с нарастанием; мышкой компенсируется
        public float RecoilDeg;            // увод за первый выстрел очереди
        public float RecoilGrowDeg;        // прибавка за каждый следующий
        public float RecoilMaxDeg = 20;
        public float RecoilRecoverDeg = 35; // возврат, когда не стреляешь (град/с)
        public int[] RecoilPattern;        // знаки увода: 1 — по часовой, −1 — против; null — случайно

        // ближний бой и добыча
        public bool Melee;
        public float MeleeArcDeg = 90;
        public float GatherTree, GatherStone, GatherMetal, GatherSulfur;

        public float Gather(NodeKind k) => k switch
        {
            NodeKind.Tree => GatherTree,
            NodeKind.Stone => GatherStone,
            NodeKind.Metal => GatherMetal,
            _ => GatherSulfur,
        };

        public static WeaponDef MeleeTool(string id, float dmg, float interval, float range, float tree, float stone, float metal, float sulfur, float arc = 90) => new WeaponDef
        {
            Id = id, Melee = true, Damage = dmg, FireInterval = interval, Range = range, MeleeArcDeg = arc,
            GatherTree = tree, GatherStone = stone, GatherMetal = metal, GatherSulfur = sulfur, NoiseRadius = 12,
        };
    }

    public sealed class WeaponState
    {
        public WeaponDef Def;
        public int Ammo;
        public float Cooldown;
        public float Bloom;
        public float Recoil;        // текущий увод ствола, градусы
        public int Streak;          // выстрелов в текущей очереди
        public float SinceShot = 10;
        public float ReloadLeft;   // > 0 — идёт перезарядка
        public WeaponState(WeaponDef def, int ammo) { Def = def; Ammo = ammo; }
    }

    public enum ItemCategory { Resource, Component, Tool, Weapon, Ammo, Food, Medical, Deployable }
    public enum DeployKind { None, Workbench, Furnace, Claim, Box }

    public sealed class ItemDef
    {
        public string Id;
        public string Name;
        public ItemCategory Category;
        public WeaponDef Weapon;
        public float Food, Water, Heal;
        public DeployKind Deploy;
        public bool Build;              // план постройки: ЛКМ ставит выбранный элемент
        public ExplosiveDef Explosive;  // бросается ЛКМ
        public bool Lock;               // кодовый замок: ЛКМ по двери — поставить
        public int Stack = 1;           // сколько штук в одном слоте пояса

        /// <summary>Предметы, которые держат в руках, живут на поясе (по одному в слоте).</summary>
        public bool Holdable => Weapon != null || Deploy != DeployKind.None || Build || Explosive != null || Lock;
    }

    /// <summary>Предмет в слоте пояса или в контейнере. У оружия — своё состояние (патроны в магазине).</summary>
    public sealed class ItemSlot
    {
        public string Id;
        public int Count;
        public WeaponState State;
        public ItemSlot(string id, int count, WeaponState state = null) { Id = id; Count = count; State = state; }
        public ItemDef Def => Items.Get(Id);
    }

    public static class Items
    {
        static readonly Dictionary<string, ItemDef> Defs = new();

        static ItemDef Add(string id, string name, ItemCategory cat)
        {
            var d = new ItemDef { Id = id, Name = name, Category = cat };
            Defs[id] = d;
            return d;
        }

        static Items()
        {
            // ресурсы и компоненты
            Add("wood", "Дерево", ItemCategory.Resource);
            Add("stone", "Камень", ItemCategory.Resource);
            Add("metal_ore", "Металлическая руда", ItemCategory.Resource);
            Add("sulfur_ore", "Серная руда", ItemCategory.Resource);
            Add("metal_fragments", "Металл", ItemCategory.Resource);
            Add("sulfur", "Сера", ItemCategory.Resource);
            Add("charcoal", "Уголь", ItemCategory.Resource);
            Add("cloth", "Ткань", ItemCategory.Resource);
            Add("gunpowder", "Порох", ItemCategory.Component);
            Add("scrap", "Металлолом", ItemCategory.Component);

            // патроны
            Add("ammo_pistol", "Патроны (пист.)", ItemCategory.Ammo);
            Add("ammo_rifle", "Патроны 5.56", ItemCategory.Ammo);
            Add("arrow", "Стрела", ItemCategory.Ammo);

            // еда и медицина
            Add("berries", "Ягоды", ItemCategory.Food).Food = 12;
            Defs["berries"].Water = 4;
            Add("canned", "Консервы", ItemCategory.Food).Food = 35;
            Add("water_bottle", "Бутылка воды", ItemCategory.Food).Water = 35;
            Add("bandage", "Бинт", ItemCategory.Medical).Heal = 25;

            // инструменты: урон, интервал, дальность, добыча дерево/камень/металл/сера
            Add("tool_rock", "Булыжник", ItemCategory.Tool).Weapon = WeaponDef.MeleeTool("tool_rock", 8, 0.7f, 1.7f, 6, 6, 4, 4);
            Add("tool_hatchet", "Каменный топор", ItemCategory.Tool).Weapon = WeaponDef.MeleeTool("tool_hatchet", 15, 0.8f, 1.8f, 16, 3, 2, 2);
            Add("tool_pickaxe", "Каменная кирка", ItemCategory.Tool).Weapon = WeaponDef.MeleeTool("tool_pickaxe", 14, 0.9f, 1.8f, 3, 14, 10, 9);
            Add("weapon_spear", "Деревянное копьё", ItemCategory.Weapon).Weapon = WeaponDef.MeleeTool("weapon_spear", 35, 0.9f, 2.3f, 1, 1, 1, 1, 50);
            Add("tool_hammer", "Молоток", ItemCategory.Tool).Weapon = WeaponDef.MeleeTool("tool_hammer", 6, 0.6f, 2.0f, 1, 1, 1, 1);   // улучшает и чинит постройки
            Add("build_plan", "План постройки", ItemCategory.Tool).Build = true;
            // взрывчатка (названия — черновик docs/naming.md)
            var satchel = Add("expl_satchel", "Связка", ItemCategory.Weapon);
            satchel.Explosive = new ExplosiveDef { Damage = 75, Radius = 2.2f, Fuse = 5f, DudChance = 0.2f, ThrowRange = 10 };
            satchel.Stack = 10;
            var c4 = Add("expl_c4", "Шашка с часами", ItemCategory.Weapon);
            c4.Explosive = new ExplosiveDef { Damage = 500, Radius = 2.5f, Fuse = 10f, ThrowRange = 8, Sticks = true };
            c4.Stack = 10;
            Add("lock_code", "Кодовый замок", ItemCategory.Tool).Lock = true;
            Add("lock_key", "Замок с ключом", ItemCategory.Tool).Lock = true;
            Add("weapon_torch", "Факел", ItemCategory.Weapon).Weapon = WeaponDef.MeleeTool("weapon_torch", 10, 0.8f, 1.6f, 1, 1, 1, 1);   // светит ночью

            // огнестрел и лук
            Add("weapon_bow", "Лук", ItemCategory.Weapon).Weapon = new WeaponDef
            {
                Id = "weapon_bow", AmmoId = "arrow",
                Damage = 45, FireInterval = 0.8f, Magazine = 1, ReloadTime = 0.7f,
                BaseSpreadDeg = 1.5f, BloomPerShotDeg = 0, MaxBloomDeg = 0, BloomRecoverDeg = 10,
                BulletSpeed = 55, Range = 40, Auto = false, NoiseRadius = 12,
            };
            Add("weapon_pipe", "Самопал", ItemCategory.Weapon).Weapon = new WeaponDef
            {
                Id = "weapon_pipe", AmmoId = "ammo_pistol",
                Damage = 22, FireInterval = 0.2f, Magazine = 10, ReloadTime = 1.7f,
                BaseSpreadDeg = 2.5f, BloomPerShotDeg = 2.5f, MaxBloomDeg = 11f, BloomRecoverDeg = 14f,
                RecoilDeg = 3.2f, RecoilGrowDeg = 0.4f, RecoilMaxDeg = 12f, RecoilRecoverDeg = 28f,
                BulletSpeed = 110, Range = 40, Auto = false, NoiseRadius = 45,
            };
            Add("weapon_ak", "Калаш", ItemCategory.Weapon).Weapon = new WeaponDef
            {
                Id = "weapon_ak", AmmoId = "ammo_rifle",
                Damage = 30, FireInterval = 0.12f, Magazine = 30, ReloadTime = 2.5f,
                BaseSpreadDeg = 1.2f, BloomPerShotDeg = 2.0f, MaxBloomDeg = 13f, BloomRecoverDeg = 11f,
                RecoilDeg = 1.6f, RecoilGrowDeg = 0.22f, RecoilMaxDeg = 26f, RecoilRecoverDeg = 32f,
                RecoilPattern = new[] { 1, 1, -1, 1, -1, -1, -1, 1, 1, 1, -1, -1, 1, -1, -1, 1 },
                BulletSpeed = 150, Range = 65, Auto = true, NoiseRadius = 70,
            };

            // устанавливаемое
            Add("deploy_workbench", "Верстак", ItemCategory.Deployable).Deploy = DeployKind.Workbench;
            Add("deploy_box", "Ящик", ItemCategory.Deployable).Deploy = DeployKind.Box;
            Add("deploy_claim", "Межевой столб", ItemCategory.Deployable).Deploy = DeployKind.Claim;   // центр базы: зона, бюджет, содержание
            Add("deploy_furnace", "Печь", ItemCategory.Deployable).Deploy = DeployKind.Furnace;
        }

        public static ItemDef Get(string id)
        {
            if (Defs.TryGetValue(id, out var d)) return d;
            // ключ от конкретного замка: key_<id замка> — заводится по первому запросу
            if (id != null && id.StartsWith("key_") && int.TryParse(id.Substring(4), out int lockId))
                return Add(id, $"Ключ №{lockId % 1000:000}", ItemCategory.Component);
            return null;
        }
        public static string Name(string id) => Get(id)?.Name ?? id;

        /// <summary>Новый предмет для пояса: у оружия сразу заводится состояние.</summary>
        public static ItemSlot NewSlot(string id, int ammo = 0)
        {
            var def = Get(id);
            return new ItemSlot(id, 1, def?.Weapon != null ? new WeaponState(def.Weapon, ammo) : null);
        }
    }

    public sealed class Recipe
    {
        public string Output;
        public int Count = 1;
        public (string id, int count)[] Cost;
        public float Time;
        public int Bench;        // 0 — в руках, 1 — нужен верстак рядом
    }

    public static class Recipes
    {
        public static readonly List<Recipe> All = new()
        {
            new Recipe { Output = "tool_hatchet", Cost = new[] { ("wood", 200), ("stone", 100) }, Time = 6 },
            new Recipe { Output = "tool_pickaxe", Cost = new[] { ("wood", 200), ("stone", 100) }, Time = 6 },
            new Recipe { Output = "weapon_spear", Cost = new[] { ("wood", 300) }, Time = 5 },
            new Recipe { Output = "deploy_box", Cost = new[] { ("wood", 100) }, Time = 3 },
            new Recipe { Output = "deploy_claim", Cost = new[] { ("wood", 100) }, Time = 3 },
            new Recipe { Output = "expl_satchel", Cost = new[] { ("cloth", 10), ("gunpowder", 30), ("metal_fragments", 20) }, Time = 4, Bench = 1 },
            new Recipe { Output = "expl_c4", Cost = new[] { ("gunpowder", 200), ("metal_fragments", 100), ("scrap", 20), ("cloth", 5) }, Time = 15, Bench = 1 },
            new Recipe { Output = "lock_key", Cost = new[] { ("wood", 75) }, Time = 2 },
            new Recipe { Output = "lock_code", Cost = new[] { ("metal_fragments", 100) }, Time = 4 },
            new Recipe { Output = "build_plan", Cost = new[] { ("wood", 20) }, Time = 1 },
            new Recipe { Output = "tool_hammer", Cost = new[] { ("wood", 100) }, Time = 3 },
            new Recipe { Output = "weapon_torch", Cost = new[] { ("wood", 30), ("cloth", 5) }, Time = 2 },
            new Recipe { Output = "weapon_bow", Cost = new[] { ("wood", 200), ("cloth", 50) }, Time = 8 },
            new Recipe { Output = "arrow", Count = 2, Cost = new[] { ("wood", 25), ("stone", 10) }, Time = 2 },
            new Recipe { Output = "bandage", Cost = new[] { ("cloth", 4) }, Time = 2 },
            new Recipe { Output = "deploy_furnace", Cost = new[] { ("stone", 200), ("wood", 100) }, Time = 8 },
            new Recipe { Output = "deploy_workbench", Cost = new[] { ("wood", 300), ("metal_fragments", 50), ("scrap", 25) }, Time = 10 },
            new Recipe { Output = "gunpowder", Count = 5, Cost = new[] { ("charcoal", 15), ("sulfur", 10) }, Time = 3, Bench = 1 },
            new Recipe { Output = "ammo_pistol", Count = 5, Cost = new[] { ("metal_fragments", 10), ("gunpowder", 5) }, Time = 3, Bench = 1 },
            new Recipe { Output = "ammo_rifle", Count = 5, Cost = new[] { ("metal_fragments", 10), ("gunpowder", 10) }, Time = 4, Bench = 1 },
            new Recipe { Output = "weapon_pipe", Cost = new[] { ("metal_fragments", 100), ("wood", 50), ("scrap", 10) }, Time = 10, Bench = 1 },
        };
    }

    public sealed class CraftJob
    {
        public Recipe Recipe;
        public float Left;
    }

    public enum Team { Player = 0, Npc = 1 }

    public sealed class Actor
    {
        public const int BeltSize = 6;

        public int Id;
        public string Name;
        public Team Team;
        public Vector2 Pos;
        public Vector2 Vel;
        public float Facing;            // радианы, atan2(y, x)
        public float Radius = 0.35f;
        public float Hp = 100, MaxHp = 100;
        public float Food = 60, Water = 60;
        public bool Dead;
        public float DeadTime;
        public int LastAttacker = -1;
        public float LastShotTime = -100;
        public float LastDamagedTime = -100;
        public float HealCooldown;
        public bool FireHeldLastTick;
        public float Peek;
        public bool Crouching;
        public bool Swimming;           // в воде: медленно, без оружия
        public float PendingSwingAt = -1;   // удар инструментом засчитывается с задержкой — когда он опускается на цель
        public WeaponState PendingSwing;
        public StructKind BuildPiece = StructKind.Foundation;   // что ставит план постройки (R — сменить)
        public Vector2 LastAim;
        public readonly ItemSlot[] Belt = new ItemSlot[BeltSize];
        public int Current;
        public readonly List<ItemStack> Inventory = new();
        public readonly List<CraftJob> CraftQueue = new();

        public ItemSlot Held => Belt[Current];
        public WeaponState Weapon => Held?.State;
        public Vector2 FacingDir => new Vector2(System.MathF.Cos(Facing), System.MathF.Sin(Facing));

        public int Count(string id)
        {
            int n = 0;
            foreach (var s in Inventory) if (s.Id == id) n += s.Count;
            return n;
        }

        /// <summary>Складывает предмет: держимое — на пояс, остальное — в инвентарь. Возвращает false, если пояс полон.</summary>
        public bool Add(string id, int count)
        {
            if (count <= 0) return true;
            var def = Items.Get(id);
            if (def != null && def.Holdable)
            {
                // складываемое держимое (взрывчатка) — докладываем в уже начатый слот
                if (def.Stack > 1)
                    foreach (var s in Belt)
                        if (s != null && s.Id == id && s.Count < def.Stack)
                        {
                            int put = System.Math.Min(count, def.Stack - s.Count);
                            s.Count += put; count -= put;
                            if (count == 0) return true;
                        }
                while (count > 0)
                {
                    var slot = Items.NewSlot(id);
                    slot.Count = System.Math.Min(count, def.Stack);
                    if (!AddSlot(slot)) return false;
                    count -= slot.Count;
                }
                return true;
            }
            foreach (var s in Inventory) if (s.Id == id) { s.Count += count; return true; }
            Inventory.Add(new ItemStack(id, count));
            return true;
        }

        /// <summary>Кладёт слот (например, оружие с патронами в магазине) в первый свободный слот пояса.</summary>
        public bool AddSlot(ItemSlot slot)
        {
            var def = slot.Def;
            if (def == null || !def.Holdable) { Add(slot.Id, slot.Count); return true; }
            for (int i = 0; i < BeltSize; i++)
                if (Belt[i] == null) { Belt[i] = slot; return true; }
            return false;
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

        public void ClearAll()
        {
            for (int i = 0; i < BeltSize; i++) Belt[i] = null;
            Inventory.Clear();
            CraftQueue.Clear();
            Current = 0;
        }
    }

    public sealed class ItemStack
    {
        public string Id;
        public int Count;
        public ItemStack(string id, int count) { Id = id; Count = count; }
    }

    public sealed class Wall
    {
        public Vector2 Min, Max;
        public float Height;            // >= 2 — высокая (закрывает обзор), иначе низкое укрытие
        public bool BlocksVision => Height >= 2f;
        public Wall(Vector2 min, Vector2 max, float h) { Min = min; Max = max; Height = h; }
    }

    /// <summary>Ресурс на карте: дерево или каменная/рудная глыба. Круглое препятствие.</summary>
    public sealed class ResourceNode
    {
        public int Id;
        public NodeKind Kind;
        public Vector2 Pos;
        public float Radius;
        public float Amount, MaxAmount;
        public bool BlocksVision => Kind == NodeKind.Tree;

        public string ResourceId => Kind switch
        {
            NodeKind.Tree => "wood",
            NodeKind.Stone => "stone",
            NodeKind.Metal => "metal_ore",
            _ => "sulfur_ore",
        };
    }

    public enum PickupKind { Hemp, Berries, WaterBarrel }

    /// <summary>То, что собирают руками по E: конопля, ягоды, бочка с водой.</summary>
    public sealed class Pickup
    {
        public int Id;
        public PickupKind Kind;
        public Vector2 Pos;
        public float RespawnLeft;       // > 0 — уже собрано, ждём
        public bool Available => RespawnLeft <= 0;
    }

    public sealed class Deployable
    {
        public int Id;
        public DeployKind Kind;
        public Vector2 Pos;
        public float Radius = 0.6f;
        public int OwnerId;
        public float Hp = 300;          // ломается взрывчаткой

        // печь
        public int Ore, SulfurOre;
        public float Fuel;               // дерево внутри
        public int OutMetal, OutSulfur, OutCharcoal;
        public float SmeltTimer, CharcoalAcc;

        // межевой столб (центр базы): кто может строить, запас на содержание, отсрочка гниения
        public readonly HashSet<int> Auth = new();
        public readonly Dictionary<string, float> Store = new();
        public float Grace = 1800f;

        // ящик: хранилище (открыт всем — рейдер заберёт)
        public LootBag Storage;
        public bool Burning => Fuel > 0 && (Ore > 0 || SulfurOre > 0);
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
        public bool Arrow;
    }

    /// <summary>Контейнер с лутом: труп, сумка или ящик на РТ.</summary>
    public sealed class LootBag
    {
        public int Id;
        public Vector2 Pos;
        public bool IsCrate;            // ящик на РТ (респавнится), иначе сумка с трупа
        public float Despawn;           // для сумок
        public float RespawnLeft;       // для ящиков
        public readonly List<ItemSlot> Items = new();
        public bool Empty => Items.Count == 0;
    }

    public enum SimEventKind { Reload, Shot, Swing, HitActor, HitWall, HitNode, Gather, Death, Loot, Respawn, Info, Placed, Crafted, Built, Destroyed, Door, HitStruct, Explosion, Locked }

    public struct SimEvent
    {
        public SimEventKind Kind;
        public int ActorId;     // кто стрелял / кто получил урон / кто умер / кто лутал
        public int OtherId;     // атакующий / узел
        public Vector2 Pos;
        public Vector2 Dir;
        public float Value;     // урон / количество
        public string Text;
    }
}
