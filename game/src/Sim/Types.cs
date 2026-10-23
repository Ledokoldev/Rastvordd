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
        public string Research;   // изучить узел дерева (id предмета) у верстака
        public int Follow;        // наблюдатель: за кем следить (id; 0 — свободная камера)
        public int HammerFocus;   // колесо киянки открыто над этой постройкой (id; 0 — нет): сервер считает пункты колеса для неё
        public int HammerOp;      // выбор в колесе киянки: 1–4 — улучшить до уровня, World.HammerDemolish — снести (0 — нет)
        public int HammerTarget;  // над какой постройкой выбран пункт колеса киянки

        public static InputCmd Empty => new InputCmd { SelectSlot = -1, Craft = -1, Code = -1, SetPiece = -1 };
    }

    public enum NodeKind { Tree, Stone, Metal, Sulfur, Barrel, Junk, Tire, TireStack }   // Barrel — бочка с хламом (ломается), Junk — куча хлама (ковыряется, даёт лом); Tire / TireStack — покрышка / стопка покрышек (режется на резину)

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

        // ветка оружия (D46, Weaponry.cs)
        public int Tier;                   // 0 лук/арбалет, 1–3, 4 — элита
        public FireMode Mode;
        public int Pellets = 1;            // дробь: дробин за выстрел (урон Damage — за дробину)
        public float PelletSpreadDeg;      // конус дроби
        public int Burst;                  // отсечка: выстрелов в очереди (АН-94 — 2)
        public float BurstInterval;        // интервал внутри очереди; FireInterval — между очередями
        public float MaxDurability;        // выстрелов до поломки (0 — не ломается)
        public int Slots;                  // какие обвесы ставятся: биты AttSlot
        public bool Accepts(AttSlot s) => (Slots & (1 << (int)s)) != 0;

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
        public int BurstLeft;      // отсечка: сколько ещё выстрелов в текущей очереди
        public float Durability;   // выстрелов до поломки (Def.MaxDurability — новый)
        public readonly string[] Att = new string[5];     // обвесы по слотам AttSlot
        public readonly float[] AttDur = new float[5];    // износ обвеса (кустарный глушитель — 40 выстрелов)
        public WeaponState(WeaponDef def, int ammo) { Def = def; Ammo = ammo; Durability = def.MaxDurability; }

        float Mul(System.Func<AttachmentDef, float> f)
        {
            float m = 1f;
            foreach (var id in Att) if (Weaponry.Att(id) is AttachmentDef a) m *= f(a);
            return m;
        }
        public bool Broken => Def.MaxDurability > 0 && Durability <= 0;
        public int MagSize => System.Math.Max(1, (int)System.MathF.Round(Def.Magazine * Mul(a => a.MagMul)));
        public float Reload => Def.ReloadTime * Mul(a => a.ReloadMul);
        public float Noise => Def.NoiseRadius * Mul(a => a.NoiseMul);
        public float RecoilMul => Mul(a => a.RecoilMul);
        public float HipSpreadMul => Mul(a => a.HipSpreadMul);
        public float MoveSpreadMul => Mul(a => a.MoveSpreadMul);
        public float AdsSpreadMul => Mul(a => a.AdsSpreadMul);
        /// <summary>Кратность прицела: 0 — без прицела, 1 коллиматор, 1.5 — 2×, 2.2 — 4× (механику прицеливания сделает пользователь).</summary>
        public float SightZoom => Weaponry.Att(Att[(int)AttSlot.Sight])?.SightZoom ?? 0f;
        public bool Flashlight { get { foreach (var id in Att) if (Weaponry.Att(id)?.Flashlight == true) return true; return false; } }
        public bool NoFlash => Weaponry.Att(Att[(int)AttSlot.Muzzle])?.NoFlash == true;
        public int AttCount { get { int n = 0; foreach (var id in Att) if (id != null) n++; return n; } }
    }

    public enum ItemCategory { Resource, Component, Tool, Weapon, Ammo, Food, Medical, Deployable, Clothing }   // Clothing — одежда, пластины, часы (D47, Clothing.cs)
    public enum DeployKind { None, Workbench, Furnace, Claim, Box, SleepBag, Recycler, Lathe,   // Recycler — переработчик на РТ (ничей, не ломается); Lathe — токарный станок (D46)
        Still, Sewing, Chem, Generator, Lamp, Floodlight, EFurnace, GunTable, MedTable, Conveyor, Rectifier, Campfire, Barrel, BoxLarge, Drill }   // станции прогрессии (D54, Data/Progression.cs: таблица Prog.Stations); новые — только в конец (сейвы хранят число)

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
        public AttachmentDef Attachment;  // обвес на ствол (att_*)
        public WearDef Wear;              // одежда (wear_*), Clothing.cs
        public PlateDef Plate;            // бронепластина (plate_*)
        // D54 (Data/Progression.cs): ёмкость для жидкости, жидкость, топливо инструмента, лечение
        public float Capacity;            // ёмкость, литров (фляга, канистра); 0 — не ёмкость
        public bool WaterOnly;            // фляга: только вода
        public bool IsLiquid;             // жидкость (нефть, топляк, дизель, вода): живёт только в ёмкостях, считается в литрах
        public string FuelId;             // инструмент на топливе (бензопила — топляк, отбойник — дизель)
        public float FuelPerHit;          // литров за удар
        public bool CuresPoison;          // аптечка, шприц: снимают отравление
        public bool Bars;                 // решётки/ставни: ЛКМ по окну

        /// <summary>Предметы, которые держат в руках, живут на поясе (по одному в слоте).</summary>
        public bool Holdable => Weapon != null || Deploy != DeployKind.None || Build || Explosive != null || Lock || Bars;
    }

    /// <summary>Предмет в слоте пояса или в контейнере. У оружия — своё состояние (патроны в магазине).</summary>
    public sealed class ItemSlot
    {
        public string Id;
        public int Count;
        public WeaponState State;
        public float Dur = -1;          // прочность пластины (урона до разрушения); −1 — новая
        public string Liquid;           // ёмкость (D54): что налито (null — пусто)
        public float Liters;            // сколько литров
        public ItemSlot(string id, int count, WeaponState state = null) { Id = id; Count = count; State = state; }
        public bool IsContainer => Def?.Capacity > 0;
        public ItemDef Def => Items.Get(Id);
    }

    public static partial class Items
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
            Add("keycard", "Карта доступа", ItemCategory.Component);
            Add("punchcard_1", "Перфокарта I", ItemCategory.Component);
            Add("punchcard_2", "Перфокарта II", ItemCategory.Component);
            Add("punchcard_3", "Перфокарта III", ItemCategory.Component);
            Add("fuse", "Пробка", ItemCategory.Component);
            Add("rt_keyfob", "Брелок от ворот", ItemCategory.Component);
            Add("rt_cable", "Кабель-перемычка", ItemCategory.Component);
            Add("scrap", "Металлолом", ItemCategory.Component);

            // патроны (калибры — Weaponry)
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

            // огнестрел, лук, арбалет, калибры, 16 компонентов, обвесы, токарный станок (D46)
            Weaponry.Register(Add);
            Weaponry.Finish();

            // одежда, пластины, часы, резина, кожа, антирадин (D47)
            Clothing.Register(Add);

            // ценный лом — только на переработку (D49, Economy.cs)
            Economy.Register(Add);

            // прогрессия D54: сталь, жидкости и ёмкости, медицина, инструменты, станции (Data/Progression.cs)
            StationDefs.Register(Add);

            // устанавливаемое
            Add("deploy_workbench", "Верстак", ItemCategory.Deployable).Deploy = DeployKind.Workbench;
            Add("deploy_workbench2", "Верстак 2", ItemCategory.Deployable).Deploy = DeployKind.Workbench;
            Add("deploy_workbench3", "Верстак 3", ItemCategory.Deployable).Deploy = DeployKind.Workbench;
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

        /// <summary>Уровень верстака, который ставит предмет (0 — не верстак).</summary>
        public static int BenchLevel(string id) => id switch { "deploy_workbench" => 1, "deploy_workbench2" => 2, "deploy_workbench3" => 3, _ => 0 };

        /// <summary>Что даёт переработчик за одну штуку (D33): детали — лом и металл, крафтовые вещи — половину рецепта.</summary>
        public static List<(string id, int n)> RecycleYield(string id)
        {
            var list = RecycleBase(id);
            if (id != null && Prog.RecycleAlloy.TryGetValue(id, out int alloy)) list.Add((Prog.Alloy, alloy));   // легированная сталь (D54)
            return list;
        }

        static List<(string id, int n)> RecycleBase(string id)
        {
            var list = new List<(string, int)>();
            if (id != null && Weaponry.Info.TryGetValue(id, out var meta) && meta.Recycle != null) { list.AddRange(meta.Recycle); return list; }
            if (Clothing.LootRecycle(id) is (string, int)[] cr) { list.AddRange(cr); return list; }   // лутовая одежда и пластины (D49)
            var r = Recipes.All.Find(x => x.Output == id);
            if (r == null || r.Count > 1) return list;           // сырьё, патроны и порох не перерабатываются
            foreach (var (cid, n) in r.Cost) if (n / 2 > 0) list.Add((cid, n / 2));
            return list;
        }

        /// <summary>Характеристики для карточек крафта и изучения: (название, значение).</summary>
        public static List<(string label, string value)> Stats(string id)
        {
            var list = new List<(string, string)>();
            var def = Get(id);
            if (def == null) return list;
            if (def.Weapon is WeaponDef w)
            {
                if (w.Melee)
                {
                    list.Add(("Урон", $"{w.Damage:0}"));
                    list.Add(("Удары", $"{60f / w.FireInterval:0} в минуту"));
                    if (w.GatherTree > 1 || w.GatherStone > 1)
                    {
                        list.Add(("Дерево за удар", $"{w.GatherTree:0}"));
                        list.Add(("Камень за удар", $"{w.GatherStone:0}"));
                        list.Add(("Руда за удар", $"{w.GatherMetal:0}"));
                    }
                }
                else
                {
                    list.Add(("Урон", w.Pellets > 1 ? $"{w.Pellets}×{w.Damage:0} (дробь)" : $"{w.Damage:0}"));
                    string mode = w.Mode switch { FireMode.Auto => ", автомат", FireMode.Burst => $", очередь по {w.Burst}", FireMode.Pump => ", помпа", FireMode.Bolt => ", затвор", FireMode.Single => ", однозаряд", _ => "" };
                    list.Add(("Скорострельность", $"{60f / w.FireInterval:0} выстр./мин" + mode));
                    list.Add(("Магазин", $"{w.Magazine}"));
                    list.Add(("Перезарядка", $"{w.ReloadTime:0.0} с"));
                    list.Add(("Дальность", $"{w.Range:0} м"));
                    if (w.AmmoId != null) list.Add(("Патроны", Name(w.AmmoId)));
                    if (w.Tier > 0) list.Add(("Тир", w.Tier >= 4 ? "элита" : $"{w.Tier}"));
                    if (w.MaxDurability > 0) list.Add(("Износ", $"{w.MaxDurability:0} выстрелов"));
                    if (w.Slots != 0)
                    {
                        var sl = new List<string>();
                        for (int k = 0; k < 5; k++) if (w.Accepts((AttSlot)k)) sl.Add(Weaponry.SlotNames[k]);
                        list.Add(("Обвесы", string.Join(", ", sl)));
                    }
                }
            }
            if (def.Explosive is ExplosiveDef e)
            {
                if (e.FireTime > 0) { list.Add(("Горит", $"{e.FireTime:0} с")); list.Add(("Радиус огня", $"{e.Radius:0.0} м")); }
                else
                {
                    list.Add(("Урон", $"{e.Damage:0}"));
                    list.Add(("Радиус", $"{e.Radius:0.0} м"));
                    list.Add(("Фитиль", $"{e.Fuse:0} с"));
                    if (e.DudChance > 0) list.Add(("Осечка", $"{e.DudChance * 100:0}%"));
                    if (e.Sticks) list.Add(("Крепится", "к стене"));
                }
                list.Add(("Бросок", $"до {e.ThrowRange:0} м"));
            }
            if (def.Attachment is AttachmentDef at)
            {
                list.Add(("Слот", Weaponry.SlotNames[(int)at.Slot]));
                list.Add(("Качество", Weaponry.QualityNames[at.Quality]));
                if (at.NoiseMul != 1) list.Add(("Шум", $"−{(1 - at.NoiseMul) * 100:0}%"));
                if (at.RecoilMul != 1) list.Add(("Отдача", $"−{(1 - at.RecoilMul) * 100:0}%"));
                if (at.HipSpreadMul != 1) list.Add(("Разброс от бедра", $"−{(1 - at.HipSpreadMul) * 100:0}%"));
                if (at.MoveSpreadMul != 1) list.Add(("Разброс на ходу", $"−{(1 - at.MoveSpreadMul) * 100:0}%"));
                if (at.AdsSpreadMul != 1) list.Add(("Разброс в прицеле", $"−{(1 - at.AdsSpreadMul) * 100:0}%"));
                if (at.MagMul != 1) list.Add(("Магазин", $"+{(at.MagMul - 1) * 100:0}%"));
                if (at.ReloadMul != 1) list.Add(("Перезарядка", $"−{(1 - at.ReloadMul) * 100:0}%"));
                if (at.SightZoom > 0) list.Add(("Кратность", at.SightZoom <= 1 ? "1× (коллиматор)" : $"{at.SightZoom:0.#}"));
                if (at.Flashlight) list.Add(("Фонарь", "да"));
                if (at.MaxShots > 0) list.Add(("Ресурс", $"{at.MaxShots:0} выстрелов"));
            }
            if (Weaponry.LatheFor(id) is Weaponry.LatheRecipe lr)
            {
                var parts = new List<string>();
                foreach (var (cid, n) in lr.In) parts.Add($"{Name(cid)} {n}");
                list.Add(("Токарный станок", string.Join(", ", parts) + (lr.Bench3 ? " (верстак 3)" : "")));
            }
            if (def.Wear is WearDef wd)
            {
                list.Add(("Слот", Clothing.SlotNames[(int)wd.Slot]));
                if (wd.Slot != WearSlot.Wrist)
                {
                    list.Add(("Пули", $"{wd.Bullet * 100:0}%" + (Clothing.SlotZone[(int)wd.Slot] is HitZone z ? z switch { HitZone.Head => " (голова)", HitZone.Torso => " (корпус)", _ => " (конечности)" } : "")));
                    list.Add(("Радиация", $"{wd.Rad:0}"));
                    list.Add(("Тепло", $"{(wd.Warm > 0 ? "+" : "")}{wd.Warm:0} °C"));
                    if (wd.Speed < 0) list.Add(("Скорость", $"{wd.Speed * 100:0}%"));
                    if (wd.Plates > 0) list.Add(("Пластин", wd.Plates == 1 ? "1 (спереди)" : "2 (спереди и сзади)"));
                }
            }
            if (def.Plate is PlateDef pd)
            {
                list.Add(("Корпус", $"+{pd.Bonus * 100:0}% со своей стороны"));
                list.Add(("Прочность", $"{pd.Durability:0} урона"));
            }
            if (BenchLevel(id) > 0) list.Add(("Открывает", $"крафт уровня {BenchLevel(id)}"));
            if (def.FuelId != null) list.Add(("Топливо", $"{Name(def.FuelId)}, {def.FuelPerHit:0.##} л за удар"));   // бензопила, отбойник (D54)
            if (def.Capacity > 0) list.Add(("Объём", $"{def.Capacity:0.#} л{(def.WaterOnly ? ", только вода" : "")}"));
            if (StationDefs.Get(def.Deploy) is StationDef sd && !sd.Registered)
            {
                list.Add(("Место", Footprints.SizeName(sd.Size)));
                if (sd.Power > 0) list.Add(("Ток", sd.NeedsPower ? $"{sd.Power:0} (без тока не работает)" : $"{sd.Power:0} (под током ×{sd.PowerSpeed:0.##})"));
                if (sd.PowerOut > 0) list.Add(("Даёт ток", $"{sd.PowerOut:0} на {sd.PowerRadius:0} м"));
                if (sd.Burn != null) list.Add(("Жжёт", $"{Name(sd.Burn)} {sd.BurnPerMin:0.##}{(Liquids.Is(sd.Burn) ? " л" : " шт.")}/мин"));
            }
            return list;
        }

        /// <summary>Ценность одной штуки «в ломах» — для решений ботов: стеш, груз, что подобрать, ради чего рисковать.</summary>
        public static float Value(string id) => id != null && Weaponry.Info.TryGetValue(id, out var m) ? m.Value : id != null && Clothing.TryMeta(id, out var cm) ? cm.Value : id switch
        {
            "wood" or "stone" => 0.05f,
            "metal_ore" or "sulfur_ore" => 0.15f,
            "metal_fragments" or "charcoal" => 0.3f,
            "sulfur" => 0.5f,
            "cloth" => 0.3f,
            "scrap" => 1f,
            "gunpowder" => 1.5f,
            "arrow" => 0.3f,
            "keycard" => 40f,
            "punchcard_1" => 30f,
            "punchcard_2" => 80f,
            "punchcard_3" => 200f,
            "fuse" => 0f,
            "bandage" => 3f,
            "berries" or "canned" or "water_bottle" => 1f,
            "weapon_spear" => 8f,
            "expl_satchel" => 50f,
            "expl_c4" => 300f,
            "expl_fire" => 15f,
            "lock_code" => 30f,
            _ when id.StartsWith("deploy_workbench") => 80f,
            _ when id.StartsWith("tool_") => 5f,
            _ => 1f,
        };

        /// <summary>Для чего предмет — одна строка в подсказке инвентаря.</summary>
        public static string Desc(string id) => id != null && Weaponry.Info.TryGetValue(id, out var m) ? m.Desc : id != null && Clothing.TryMeta(id, out var cm) ? WearDesc(id, cm.Desc) : id switch
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
            "scrap" => "Изучение и верстаки. Больше всего — из переработчика на РТ: компоненты и ценный лом",
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
            "deploy_workbench" => "E — дерево изучения. Крафт уровня 1: порох, 9 мм, 12 к, Наган, Самоделка, арбалет. Alt+E со стволом в руках — починка маслом",
            "deploy_workbench2" => "Изучение и крафт уровня 2: 7,62, Грач, ППС-43, СВТ-40, обвесы, токарный станок, самодельная бомба",
            "deploy_workbench3" => "Изучение и крафт уровня 3: 5,45, АКМ, АН-94, ППШ, СВД, взрывчатка; детали тира 3 на станке рядом",
            "deploy_sleepbag" => "Место респауна (B)",
            "deploy_box" => "Хранилище на 30 ячеек",
            "deploy_claim" => "Центр базы: даёт строить рядом; клади ресурсы, иначе база гниёт",
            "deploy_furnace" => "Плавит руду в металл и серу",
            "keycard" => "Открывает сейф на РТ с охраной (одна карта — один сейф)",
            "punchcard_1" => "Открывает крейт РТ тира 1 (Подстанция). Тратится",
            "punchcard_2" => "Открывает РТ тира 2 (пульт Радиоцентра «Маяк»). Тратится",
            "punchcard_3" => "Для РТ тира 3 (ещё не построены). Редкость с вызова «Маяка»",
            "fuse" => "Пробка Подстанции: вставь в главный щиток. За забор не вынести",
            "rt_keyfob" => "Брелок от гаражных ворот: жми у гаражей — нужные ворота запищат. Из Гаражей не вынести",
            "rt_cable" => "Перемычка для кросса АТС. Из АТС не вынести",
            _ => "",
        };

        /// <summary>Описание одежды: своя строка + «как надеть» и откуда (комплект).</summary>
        static string WearDesc(string id, string own)
        {
            var wd = Clothing.Def(id);
            if (wd == null) return own;
            var set = Clothing.Sets.Find(s => s.Key == wd.Set);
            string head = wd.Slot == WearSlot.Wrist ? "" : $"{Clothing.SlotNames[(int)wd.Slot]} · комплект «{set?.Name}» ({set?.Source}). ";
            return head + (own != "" ? own + ". " : "") + "ПКМ — надеть";
        }

        /// <summary>Редкость (цвет рамки и время обыска): 0 обычное, 1 необычное, 2 редкое, 3 эпическое, 4 легендарное.</summary>
        public static int Rarity(string id) => id != null && Weaponry.Info.TryGetValue(id, out var m) ? m.Rarity : id != null && Clothing.TryMeta(id, out var cm) ? cm.Rarity : id switch
        {
            "keycard" => 3,
            "punchcard_1" => 2,
            "punchcard_2" => 3,
            "punchcard_3" => 4,
            "fuse" => 1,
            "expl_c4" => 4,
            "expl_satchel" or "lock_code" => 3,
            "gunpowder" or "metal_fragments" or "expl_fire" or "lock_key" or "weapon_spear" => 2,
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
            if (Weaponry.Info.TryGetValue(id, out var m) && m.Stack > 0) return m.Stack;
            if (Clothing.TryMeta(id, out var cm) && cm.Stack > 0) return cm.Stack;
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
        public int Bench;        // какой уровень верстака нужен рядом: 0 — можно в руках, 1–3
        public DeployKind Station;   // станция рядом обязательна (D54: оружейный стол, медстол, химстол…); None — не нужна
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
            new Recipe { Output = "expl_satchel", Cost = new[] { ("cloth", 10), ("gunpowder", 30), ("metal_fragments", 20) }, Time = 4, Bench = 2 },
            new Recipe { Output = "expl_c4", Cost = new[] { ("gunpowder", 200), ("metal_fragments", 100), ("scrap", 20), ("cloth", 5) }, Time = 15, Bench = 3 },
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
            new Recipe { Output = "deploy_workbench", Cost = new[] { ("wood", 150), ("metal_fragments", 50), ("scrap", 50) }, Time = 10 },
            new Recipe { Output = "gunpowder", Count = 5, Cost = new[] { ("charcoal", 15), ("sulfur", 10) }, Time = 3, Bench = 1 },
            // верстаки (D49): лом 50 / 150 / 400, третий — ещё платы ×3 и шестерни ×5
            new Recipe { Output = "deploy_workbench2", Cost = new[] { ("metal_fragments", 300), ("scrap", 150) }, Time = 15, Bench = 1 },
            new Recipe { Output = "deploy_workbench3", Cost = new[] { ("metal_fragments", 600), ("scrap", 400), ("comp_circuit", 3), ("comp_gears", 5) }, Time = 25, Bench = 2 },
        };

        static Recipes() { Weaponry.AddRecipes(All); Clothing.AddRecipes(All); StationDefs.AddRecipes(All); }   // + D54: сталь, станции, ёмкости, инструменты   // стволы, патроны, обвесы, станок (D46); одежда (D47)
    }

    /// <summary>Узел дерева изучения: изучается за металлолом у верстака нужного уровня после предков.</summary>
    public sealed class TechNode
    {
        public string Id;                // id предмета, рецепт которого открывается
        public int Bench;                // уровень верстака
        public int Scrap;                // цена в металлоломе
        public string[] Requires = System.Array.Empty<string>();
        public string[] RequiresAny = System.Array.Empty<string>();   // хотя бы один из (АКМ — через Грач или СВТ)
        public int Col = -1, Row = -1;   // место в окне изучения (столбец = верстак − 1); −1 — по умолчанию клиента
    }

    /// <summary>Дерево изучения (D28). Всё, чего в дереве нет, известно с начала (примитив, станки).</summary>
    public static class Tech
    {
        public static readonly List<TechNode> All = new()
        {
            // цены D49: «изучить всё» ≈ 250 / 650 / 1300 лома по верстакам (вместе с ветками оружия и одежды)
            new TechNode { Id = "gunpowder", Bench = 1, Scrap = 15, Col = 0, Row = 0 },
            new TechNode { Id = "lock_code", Bench = 1, Scrap = 15, Col = 0, Row = 10 },
            new TechNode { Id = "expl_fire", Bench = 1, Scrap = 15, Col = 0, Row = 11 },
            new TechNode { Id = "expl_satchel", Bench = 2, Scrap = 45, Requires = new[] { "gunpowder" }, Col = 1, Row = 0 },
            new TechNode { Id = "expl_c4", Bench = 3, Scrap = 160, Requires = new[] { "expl_satchel" }, Col = 2, Row = 0 },
        };

        static Tech() { Weaponry.AddTech(All); Clothing.AddTech(All); StationDefs.AddTech(All); }   // ветка оружия и обвесов (D46), комплекты одежды (D47)

        public static TechNode Get(string id) => All.Find(n => n.Id == id);

        /// <summary>Знает ли игрок рецепт (боты изучают так же, как люди).</summary>
        public static bool Knows(Actor a, string id)
        {
            id = Clothing.TechKey(id) ?? id;   // одежда тиров 2–3: один узел на комплект
            return Get(id) == null || a.Known.Contains(id);
        }
    }

    public sealed class CraftJob
    {
        public Recipe Recipe;
        public float Left, Total;
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
        public bool WantsCraftMenu;     // нажал E у верстака — клиент откроет дерево изучения
        public bool WantsCraftList;     // …а у станции (D54) — сразу список крафта
        public readonly HashSet<string> Known = new();   // изученные узлы дерева (примитив известен всегда, см. Tech)
        public bool Spectator;          // наблюдатель: летает, видит всё, мир его не замечает
        public UiKind OpenUi;
        public object OpenTarget;
        public float PendingSwingAt = -1;   // удар инструментом засчитывается с задержкой — когда он опускается на цель
        public WeaponState PendingSwing;
        public int HammerFocus;         // колесо киянки открыто над этой постройкой (не сохраняется)
        public StructKind BuildPiece = StructKind.Foundation;   // что ставит план постройки (R — сменить)
        public int DeployRot;           // поворот установленного в руках (R), 0..3
        public Vector2 LastAim;
        public readonly ItemSlot[] Belt = new ItemSlot[BeltSize];
        public int Current;
        public const int PackSize = 24;
        /// <summary>Рюкзак: 24 ячейки, в ячейке стопка до Items.MaxStack (как в Arc Raiders).</summary>
        public readonly ItemSlot[] Pack = new ItemSlot[PackSize];
        /// <summary>Не влезло (рюкзак полон) — World выбросит рядом сумкой.</summary>
        public readonly List<ItemSlot> Overflow = new();
        public readonly List<CraftJob> CraftQueue = new();
        /// <summary>Пробки Подстанции — отдельный счётчик, не занимают ячейку (за забор не выносятся, при смерти возвращаются на РТ).</summary>
        public int Fuses;

        // одежда (D47): 10 слотов (WearSlot), пластины перед/спина; радиация и холод
        public readonly ItemSlot[] Worn = new ItemSlot[Clothing.Slots];
        public readonly ItemSlot[] Plates = new ItemSlot[Clothing.PlateSlots];
        public float Poison;            // отравление сырой водой (D54): секунд осталось
        public float RadDose;           // накопленная доза
        public float Cold;              // переохлаждение 0..100
        public float WetLeft;           // мокрый ещё столько секунд (вода, дождь)
        public float RadHere, AirTemp, FeltTemp;   // где стоит: уровень радиации, воздух, воздух + тепло + огонь/дом/мокрый (для HUD и ботов)
        public bool NearFire, Indoors;

        public ItemSlot Held => Belt[Current];
        public WeaponState Weapon => Held?.State;
        public Vector2 FacingDir => new Vector2(System.MathF.Cos(Facing), System.MathF.Sin(Facing));

        /// <summary>Сколько всего этого предмета в рюкзаке и на поясе.</summary>
        public int Count(string id)
        {
            if (id == "fuse") return Fuses;
            if (Liquids.Is(id)) return (int)(Liquids.Total(Pack, id) + Liquids.Total(Belt, id) + 1e-3f);   // литры в ёмкостях (D54)
            int n = 0;
            foreach (var s in Pack) if (s != null && s.Id == id) n += s.Count;
            foreach (var s in Belt) if (s != null && s.Id == id && s.State == null) n += s.Count;
            return n;
        }

        /// <summary>Положить сколько влезет; возвращает, сколько положено. Держимое — сначала на пояс.</summary>
        public int AddUpTo(string id, int count)
        {
            if (count <= 0) return 0;
            if (id == "fuse") { Fuses += count; return count; }
            if (Liquids.Is(id)) return Liquids.AddToActor(this, id, count);   // жидкость — в ёмкости (нет места — в новую канистру)
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
            if (id == "fuse") { int f = System.Math.Min(Fuses, System.Math.Max(0, count)); Fuses -= f; return f; }
            if (Liquids.Is(id)) return Liquids.TakeFromActor(this, id, count);
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
            for (int i = 0; i < Worn.Length; i++) Worn[i] = null;
            for (int i = 0; i < Plates.Length; i++) Plates[i] = null;
            RadDose = 0; Cold = 0; WetLeft = 0;
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
            NodeKind.Barrel or NodeKind.Junk => "scrap",
            NodeKind.Tire or NodeKind.TireStack => "rubber",
            _ => "sulfur_ore",
        };
    }

    public enum PickupKind { Hemp, Berries, WaterBarrel, Fuse, PunchCard, LocalItem, OilBarrel, CanisterPile }   // OilBarrel / CanisterPile — бочка с мазутом / ржавые канистры: нефть в ёмкость (D54)

    /// <summary>То, что собирают руками по E: конопля, ягоды, бочка с водой.</summary>
    public sealed class Pickup
    {
        public int Id;
        public PickupKind Kind;
        public Vector2 Pos;
        public float RespawnLeft;       // > 0 — уже собрано, ждём
        public string Item;             // LocalItem: предмет РТ (брелок, кабель) — живёт только на своей РТ
        public bool Available => RespawnLeft <= 0;
    }

    public sealed class Deployable
    {
        public int Id;
        public DeployKind Kind;
        public Vector2 Pos;
        public float Radius = 0.6f;     // дальность «рукой» и взрывы: половина длинной стороны хитбокса
        public Vector2 Half;            // хитбокс — прямоугольник по осям (D52): половины сторон; 0 — по таблице Footprints
        public int Rot;                 // поворот 0..3: половина — вдоль X (0/2) или Y (1/3), целая — куда «смотрит»
        public int FloorId, Slots;      // пол (id постройки) и занятые слоты (биты); 0 — стоит вне сетки
        public bool Solid => Kind != DeployKind.SleepBag;   // спальник плоский: по нему ходят, пули летят над ним
        public Vector2 Min => Pos - Half;
        public Vector2 Max => Pos + Half;
        public int OwnerId;
        public float Hp = 300;          // ломается взрывчаткой
        public int Level;               // верстак: 1–3

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
        public bool Powered;             // станция под током генератора (D54; считается каждую секунду, не сохраняется)
        public float Work;               // станции D54: таймер текущей работы
        public bool Active;              // станция D54 работает (горит, крутится) — для клиента и тепла
        public bool Burning => Kind == DeployKind.Furnace ? On && Fuel > 0 && (Ore > 0 || SulfurOre > 0) : Active;
    }

    public sealed class Projectile
    {
        public Vector2 LowCover; public bool PassedLowCover;   // пролетела над низким укрытием (D56): присевший сразу за ним — защищён
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
        public bool Corpse;             // вещи с трупа: обыскиваются по ячейке, как ящик РТ; выброшенное — открыто сразу
        public bool Safe;               // сейф на РТ: открывается картой доступа, до следующего наполнения
        public bool Unlocked;
        public bool Punch;              // крейт РТ под перфокарту: открывает таймер РТ, наполняет обновление РТ
        public int Tier = 2;            // уровень лута ящика: 0 — без охраны, 1 — малая охрана, 2 — крупный РТ
        public string Look;             // вид ивент-ящика: "airdrop" — контейнер сброса, "military" — военный ящик (иначе по тиру)
        /// <summary>Обыск по игрокам: сколько ячеек уже раскрыто и сколько идёт текущая.</summary>
        public readonly Dictionary<int, (int n, float t)> Search = new();
        public float Despawn;           // для сумок
        public float RespawnLeft;       // для ящиков
        public string Src;              // откуда лут (учёт дохода лома, Economy.Ledger): null — по флагам (см. Source)
        public string Source => Src ?? (Safe ? "сейф" : Look != null ? "ивент" : Punch ? "крейт перфокарты" : IsCrate ? $"ящик РТ {Tier}" : Corpse ? "труп" : null);
        public readonly List<ItemSlot> Items = new();
        public bool Empty => Items.Count == 0;
    }

    public enum SimEventKind { Reload, Shot, Swing, HitActor, HitWall, HitNode, Gather, Death, Loot, Respawn, Info, Placed, Crafted, Built, Destroyed, Door, HitStruct, Explosion, Locked, Revealed, Machine, Wear, Demolished }   // Wear — звук одежды/брони (Text — id звука), личное

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
