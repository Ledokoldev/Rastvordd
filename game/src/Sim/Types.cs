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
        public bool RespawnBag;   // проснуться в своём спальнике
        public bool DebugKit;
        public bool ToggleTime;   // отладка: день/ночь
        public bool Crouch;       // присед (зажат Alt)
        public float Peek;        // 0..1: насколько игрок прицеливается (ПКМ)
        public int SelectSlot;    // -1 — без смены слота пояса
        public int Craft;         // индекс рецепта, -1 — ничего
        public int Code;          // введённый код замка, -1 — нет
        public int CodeDoor;      // id двери, к которой вводят код
        public int UiOp;          // действие в открытом окне (см. UiOp)
        public string UiItem;
        public int UiIndex;
        public int SetPiece;      // выбор элемента плана из меню (−1 — нет)

        public static InputCmd Empty => new InputCmd { SelectSlot = -1, Craft = -1, Code = -1, SetPiece = -1 };
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
    public enum DeployKind { None, Workbench, Furnace, Claim, Box, SleepBag }

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
            Add("tool_hammer", "Киянка", ItemCategory.Tool).Weapon = WeaponDef.MeleeTool("tool_hammer", 6, 0.6f, 2.0f, 1, 1, 1, 1);   // улучшает и чинит постройки
            Add("build_plan", "План", ItemCategory.Tool).Build = true;
            // взрывчатка (названия — черновик docs/naming.md)
            var satchel = Add("expl_satchel", "Самодельная бомба", ItemCategory.Weapon);
            satchel.Explosive = new ExplosiveDef { Damage = 75, Radius = 2.2f, Fuse = 5f, DudChance = 0.2f, ThrowRange = 10 };
            satchel.Stack = 10;
            var c4 = Add("expl_c4", "Взрывчатка", ItemCategory.Weapon);
            c4.Explosive = new ExplosiveDef { Damage = 500, Radius = 2.5f, Fuse = 10f, ThrowRange = 8, Sticks = true };
            c4.Stack = 10;
            Add("lock_code", "Кодовый замок", ItemCategory.Tool).Lock = true;
            Add("lock_key", "Замок с ключом", ItemCategory.Tool).Lock = true;
            var molotov = Add("expl_fire", "Коктейль Молотова", ItemCategory.Weapon);   // бутылка с горючкой: огонь по дереву
            molotov.Explosive = new ExplosiveDef { Radius = 1.6f, Fuse = 0f, ThrowRange = 12, FireTime = 14f };
            molotov.Stack = 10;
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
            Add("deploy_sleepbag", "Спальник", ItemCategory.Deployable).Deploy = DeployKind.SleepBag;
            Add("deploy_box", "Ящик", ItemCategory.Deployable).Deploy = DeployKind.Box;
            Add("deploy_claim", "Шкаф", ItemCategory.Deployable).Deploy = DeployKind.Claim;   // центр базы: зона, бюджет, содержание
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

        /// <summary>Для чего предмет — одна строка в подсказке инвентаря.</summary>
        public static string Desc(string id) => id switch
        {
            "wood" => "Стройка, крафт, топливо для печи",
            "stone" => "Инструменты, печь, улучшение построек до камня",
            "metal_ore" => "Переплавь в печи — получишь металл",
            "sulfur_ore" => "Переплавь в печи — получишь серу",
            "metal_fragments" => "Оружие, замки, взрывчатка, улучшение до металла",
            "sulfur" => "С углём на верстаке — порох",
            "charcoal" => "Остаётся в печи от дерева; с серой — порох",
            "cloth" => "Бинты, спальник, лук, факел",
            "gunpowder" => "Патроны и взрывчатка (верстак)",
            "scrap" => "Верстак, самопал, взрывчатка",
            "ammo_pistol" => "Патроны для самопала",
            "ammo_rifle" => "Патроны для калаша",
            "arrow" => "Стрелы для лука",
            "berries" => "Еда: F — съесть",
            "canned" => "Еда: F — съесть",
            "water_bottle" => "Вода: F — выпить",
            "bandage" => "Лечит: H",
            "tool_rock" => "Бей деревья и камни. Слабый, но всегда с тобой",
            "tool_hatchet" => "Рубит деревья быстрее",
            "tool_pickaxe" => "Добывает камень и руду быстрее",
            "weapon_spear" => "Ближний бой, длинный выпад",
            "tool_hammer" => "ЛКМ по постройке — улучшить или починить",
            "build_plan" => "Ставит фундаменты и стены. Зажми ПКМ — колесо",
            "expl_satchel" => "Ломает двери и дерево; бывают осечки",
            "expl_c4" => "Ломает любые стены",
            "lock_code" => "ЛКМ по двери — кодовый замок",
            "lock_key" => "ЛКМ по двери — замок, ключ остаётся у тебя",
            "expl_fire" => "Поджигает: жжёт дерево и людей",
            "weapon_torch" => "Светит ночью",
            "weapon_bow" => "Тихое оружие, нужны стрелы",
            "weapon_pipe" => "Простой огнестрел на пист. патронах",
            "weapon_ak" => "Автомат на патронах 5.56",
            "deploy_workbench" => "Крафт пороха, патронов, оружия",
            "deploy_sleepbag" => "Место респауна (B)",
            "deploy_box" => "Хранилище на 30 ячеек",
            "deploy_claim" => "Центр базы: даёт строить рядом; клади ресурсы, иначе база гниёт",
            "deploy_furnace" => "Плавит руду в металл и серу",
            _ => "",
        };

        /// <summary>Редкость (цвет рамки и время обыска): 0 обычное, 1 необычное, 2 редкое, 3 эпическое, 4 легендарное.</summary>
        public static int Rarity(string id) => id switch
        {
            "weapon_ak" or "expl_c4" => 4,
            "expl_satchel" or "ammo_rifle" or "lock_code" or "weapon_bow" => 3,
            "weapon_pipe" or "gunpowder" or "ammo_pistol" or "metal_fragments" or "expl_fire" or "lock_key" or "weapon_spear" => 2,
            "bandage" or "canned" or "water_bottle" or "charcoal" or "sulfur" or "scrap" or "arrow" or "tool_hatchet" or "tool_pickaxe" => 1,
            _ when id != null && id.StartsWith("key_") => 3,
            _ => 0,
        };

        /// <summary>Сколько секунд обыскивается ячейка с этим предметом (Arc Raiders: редкое — дольше).</summary>
        public static float SearchTime(string id) => 0.18f + 0.16f * Rarity(id);   // обыск ячейки: обычное быстро, редкое чуть дольше

        /// <summary>Сколько штук в одной ячейке.</summary>
        public static int MaxStack(string id)
        {
            var d = Get(id);
            if (d == null) return 1;
            if (d.Holdable) return System.Math.Max(1, d.Stack);
            return d.Category switch
            {
                ItemCategory.Ammo => id == "arrow" ? 64 : 128,
                ItemCategory.Medical => 10,
                ItemCategory.Food => 10,
                _ => id.StartsWith("key_") ? 1 : 1000,
            };
        }

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
            new Recipe { Output = "tool_hatchet", Cost = new[] { ("wood", 100), ("stone", 50) }, Time = 6 },
            new Recipe { Output = "tool_pickaxe", Cost = new[] { ("wood", 100), ("stone", 50) }, Time = 6 },
            new Recipe { Output = "weapon_spear", Cost = new[] { ("wood", 150) }, Time = 5 },
            new Recipe { Output = "deploy_sleepbag", Cost = new[] { ("cloth", 30) }, Time = 3 },
            new Recipe { Output = "deploy_box", Cost = new[] { ("wood", 75) }, Time = 3 },
            new Recipe { Output = "deploy_claim", Cost = new[] { ("wood", 50) }, Time = 3 },
            new Recipe { Output = "expl_satchel", Cost = new[] { ("cloth", 10), ("gunpowder", 30), ("metal_fragments", 20) }, Time = 4, Bench = 1 },
            new Recipe { Output = "expl_c4", Cost = new[] { ("gunpowder", 200), ("metal_fragments", 100), ("scrap", 20), ("cloth", 5) }, Time = 15, Bench = 1 },
            new Recipe { Output = "lock_key", Cost = new[] { ("wood", 75) }, Time = 2 },
            new Recipe { Output = "lock_code", Cost = new[] { ("metal_fragments", 100) }, Time = 4 },
            new Recipe { Output = "expl_fire", Cost = new[] { ("cloth", 10), ("sulfur", 15), ("charcoal", 10) }, Time = 3 },
            new Recipe { Output = "build_plan", Cost = new[] { ("wood", 20) }, Time = 1 },
            new Recipe { Output = "tool_hammer", Cost = new[] { ("wood", 50) }, Time = 3 },
            new Recipe { Output = "weapon_torch", Cost = new[] { ("wood", 30), ("cloth", 5) }, Time = 2 },
            new Recipe { Output = "weapon_bow", Cost = new[] { ("wood", 100), ("cloth", 30) }, Time = 8 },
            new Recipe { Output = "arrow", Count = 2, Cost = new[] { ("wood", 25), ("stone", 10) }, Time = 2 },
            new Recipe { Output = "bandage", Cost = new[] { ("cloth", 4) }, Time = 2 },
            new Recipe { Output = "deploy_furnace", Cost = new[] { ("stone", 100), ("wood", 50) }, Time = 8 },
            new Recipe { Output = "deploy_workbench", Cost = new[] { ("wood", 150), ("metal_fragments", 50), ("scrap", 25) }, Time = 10 },
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
        public bool IsBot;              // бот: E — быстрые действия, у живого игрока — окна
        public bool WantsCraftMenu;     // нажал E у верстака — клиент откроет крафт
        public UiKind OpenUi;
        public object OpenTarget;
        public float PendingSwingAt = -1;   // удар инструментом засчитывается с задержкой — когда он опускается на цель
        public WeaponState PendingSwing;
        public StructKind BuildPiece = StructKind.Foundation;   // что ставит план постройки (R — сменить)
        public Vector2 LastAim;
        public readonly ItemSlot[] Belt = new ItemSlot[BeltSize];
        public int Current;
        public const int PackSize = 24;
        /// <summary>Рюкзак: 24 ячейки, в ячейке стопка до Items.MaxStack (как в Arc Raiders).</summary>
        public readonly ItemSlot[] Pack = new ItemSlot[PackSize];
        /// <summary>Не влезло (рюкзак полон) — World выбросит рядом сумкой.</summary>
        public readonly List<ItemSlot> Overflow = new();
        public readonly List<CraftJob> CraftQueue = new();

        public ItemSlot Held => Belt[Current];
        public WeaponState Weapon => Held?.State;
        public Vector2 FacingDir => new Vector2(System.MathF.Cos(Facing), System.MathF.Sin(Facing));

        /// <summary>Сколько всего этого предмета в рюкзаке и на поясе.</summary>
        public int Count(string id)
        {
            int n = 0;
            foreach (var s in Pack) if (s != null && s.Id == id) n += s.Count;
            foreach (var s in Belt) if (s != null && s.Id == id && s.State == null) n += s.Count;
            return n;
        }

        /// <summary>Положить сколько влезет; возвращает, сколько положено. Держимое — сначала на пояс.</summary>
        public int AddUpTo(string id, int count)
        {
            if (count <= 0) return 0;
            var def = Items.Get(id);
            int max = Items.MaxStack(id), left = count;
            bool holdable = def != null && def.Holdable;
            // доложить в начатые стопки (держимое — и на поясе)
            if (max > 1)
            {
                if (holdable) foreach (var s in Belt) left -= Fill(s);
                foreach (var s in Pack) left -= Fill(s);
            }
            int Fill(ItemSlot s)
            {
                if (left <= 0 || s == null || s.Id != id || s.State != null || s.Count >= max) return 0;
                int put = System.Math.Min(left, max - s.Count);
                s.Count += put;
                return put;
            }
            // новые ячейки
            while (left > 0)
            {
                var slot = Items.NewSlot(id);
                slot.Count = System.Math.Min(left, max);
                if (!AddSlot(slot)) break;
                left -= slot.Count;
            }
            return count - left;
        }

        /// <summary>Положить предмет; не влезшее уходит в Overflow (выпадет рядом). Возвращает false, если что-то не влезло.</summary>
        public bool Add(string id, int count)
        {
            int put = AddUpTo(id, count);
            int left = count - put;
            while (left > 0)
            {
                var s = Items.NewSlot(id);
                s.Count = System.Math.Min(left, Items.MaxStack(id));
                Overflow.Add(s);
                left -= s.Count;
            }
            return put == count;
        }

        /// <summary>Положить ячейку целиком (оружие с патронами): держимое — на пояс, иначе в рюкзак. false — некуда.</summary>
        public bool AddSlot(ItemSlot slot)
        {
            var def = slot.Def;
            if (def != null && def.Holdable)
                for (int i = 0; i < BeltSize; i++)
                    if (Belt[i] == null) { Belt[i] = slot; return true; }
            for (int i = 0; i < PackSize; i++)
                if (Pack[i] == null) { Pack[i] = slot; return true; }
            return false;
        }

        public int Take(string id, int count)
        {
            int taken = 0;
            for (int pass = 0; pass < 2 && taken < count; pass++)
            {
                var arr = pass == 0 ? Pack : Belt;
                for (int i = arr.Length - 1; i >= 0 && taken < count; i--)
                {
                    var s = arr[i];
                    if (s == null || s.Id != id || s.State != null) continue;
                    int t = System.Math.Min(s.Count, count - taken);
                    s.Count -= t; taken += t;
                    if (s.Count == 0) arr[i] = null;
                }
            }
            return taken;
        }

        public void ClearAll()
        {
            for (int i = 0; i < BeltSize; i++) Belt[i] = null;
            for (int i = 0; i < PackSize; i++) Pack[i] = null;
            CraftQueue.Clear();
            Current = 0;
        }
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
        public float RaidedAt = -1000;  // когда последний раз ломали постройки этой базы
        public int RaiderId = -1;

        // ящик: хранилище (открыт всем — рейдер заберёт)
        public LootBag Storage;

        // спальник: после возрождения в нём — перезарядка
        public float ReadyAt;
        public bool On = true;           // печь зажжена (игрок включает в окне)
        public bool Burning => On && Fuel > 0 && (Ore > 0 || SulfurOre > 0);
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
        public int Tier = 2;            // уровень лута ящика: 0 — без охраны, 1 — малая охрана, 2 — крупный РТ
        /// <summary>Обыск по игрокам: сколько ячеек уже раскрыто и сколько идёт текущая.</summary>
        public readonly Dictionary<int, (int n, float t)> Search = new();
        public float Despawn;           // для сумок
        public float RespawnLeft;       // для ящиков
        public readonly List<ItemSlot> Items = new();
        public bool Empty => Items.Count == 0;
    }

    public enum SimEventKind { Reload, Shot, Swing, HitActor, HitWall, HitNode, Gather, Death, Loot, Respawn, Info, Placed, Crafted, Built, Destroyed, Door, HitStruct, Explosion, Locked, Revealed }

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
