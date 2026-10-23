using System;
using System.Collections.Generic;

namespace Rastvordd.Sim
{
    /// <summary>Режим огня (для карточки и логики очереди).</summary>
    public enum FireMode { Single, Semi, Auto, Burst, Pump, Bolt }

    /// <summary>Слоты обвесов на стволе (D46): прицел, дуло, цевьё, магазин, приклад.</summary>
    public enum AttSlot { Sight = 0, Muzzle = 1, Foregrip = 2, Magazine = 3, Stock = 4 }

    /// <summary>Обвес: множители к характеристикам ствола. Качество: 0 кустарный, 1 заводской, 2 армейский.</summary>
    public sealed class AttachmentDef
    {
        public string Id;
        public AttSlot Slot;
        public int Quality;
        public float NoiseMul = 1, RecoilMul = 1, HipSpreadMul = 1, MoveSpreadMul = 1, MagMul = 1, ReloadMul = 1, AdsSpreadMul = 1;
        public float SightZoom;          // прицел: 1 (коллиматор), 1.5 (2×), 2.2 (4×); механику прицеливания придумает пользователь
        public bool Flashlight;          // фонарь: ночью видно дальше, как с факелом
        public bool NoFlash;             // ПБС: без вспышки
        public float MaxShots;           // износ (кустарный глушитель — 40 выстрелов); 0 — вечный
    }

    /// <summary>
    /// Оружие, калибры, 16 компонентов, токарный станок, обвесы (docs/design/13-weapons-progression.md, версия 2).
    /// Здесь только данные; стрельба — World.StepHeld, лут — World/Containers/Prefab/Substation, боты — PlayerBot*.
    /// </summary>
    public static class Weaponry
    {
        public static int SlotCount => 5;
        public static readonly string[] SlotNames = { "прицел", "дуло", "цевьё", "магазин", "приклад" };
        public static readonly string[] QualityNames = { "кустарный", "заводской", "армейский" };

        // ---------- старые id из сохранений ----------
        public static readonly Dictionary<string, string> Legacy = new()
        {
            ["ammo_pistol"] = "ammo_9mm",
            ["ammo_rifle"] = "ammo_762",
            ["weapon_ak"] = "weapon_akm",
            ["weapon_pipe"] = "weapon_nagant",   // старый «самопал» стрелял пистолетными — ближе всего Наган
            // Ликвидатор убран (D53): вещи из старых сейвов — в ближайшие из ОЗК и армейской
            ["wear_liquidator_helmet"] = "wear_ozk_hood", ["wear_liquidator_pmk"] = "wear_ozk_gp5", ["wear_liquidator_apron"] = "wear_ozk_cape",
            ["wear_liquidator_suit"] = "wear_army_bushlat", ["wear_liquidator_pants"] = "wear_army_pants", ["wear_liquidator_overboots"] = "wear_ozk_stockings",
            ["wear_liquidator_gloves"] = "wear_ozk_gloves",
        };
        public static string Migrate(string id) => id != null && Legacy.TryGetValue(id, out var n) ? n : id;

        // ---------- справочные данные по предмету ----------
        public sealed class Meta { public string Desc; public int Rarity; public float Value; public int Stack; public (string id, int n)[] Recycle; }
        public static readonly Dictionary<string, Meta> Info = new();
        static void M(string id, string desc, int rarity, float value, int stack = 0, params (string, int)[] recycle)
            => Info[id] = new Meta { Desc = desc, Rarity = rarity, Value = value, Stack = stack, Recycle = recycle.Length > 0 ? recycle : null };

        /// <summary>Тир ствола: 0 лук/арбалет, 1–3, 4 — элита.</summary>
        public static int Tier(string id) => Items.Get(id)?.Weapon is WeaponDef w ? w.Tier : -1;
        public static bool IsGun(string id) => Items.Get(id)?.Weapon is WeaponDef w && !w.Melee;
        public static AttachmentDef Att(string id) => id == null ? null : Items.Get(id)?.Attachment;

        /// <summary>Все стволы (не ближний бой), сильнейшие первыми — для ботов.</summary>
        public static readonly List<string> GunsByPower = new();

        /// <summary>Сила на глаз (для решений ботов): лук 0,6 … АКМ 1, элита 1,05.</summary>
        public static float Power(string id) => id switch
        {
            "weapon_bow" => 0.6f, "weapon_crossbow" => 0.65f,
            "weapon_pipeshotgun" => 0.7f, "weapon_sawnoff" => 0.75f, "weapon_nagant" => 0.78f, "weapon_homesmg" => 0.82f,
            "weapon_grach" => 0.86f, "weapon_mp133" => 0.88f, "weapon_svt40" => 0.9f, "weapon_pps43" => 0.92f,
            "weapon_svd" => 0.97f, "weapon_ppsh" => 0.98f, "weapon_an94" => 1f, "weapon_akm" => 1f,
            "weapon_aps" => 0.95f, "weapon_mosin" => 1f, "weapon_saiga12" => 1.05f,
            _ => 0f,
        };

        /// <summary>Удобная дистанция боя: дробь — в упор, ПП — близко, винтовки — далеко.</summary>
        public static float PrefDist(string id)
        {
            if (Items.Get(id)?.Weapon is not WeaponDef w || w.Melee) return 15f;
            if (w.Pellets > 1) return 7f;
            return Math.Clamp(w.Range * 0.38f, 9f, 34f);
        }

        /// <summary>Слышимость выстрела для ботов: по шуму ствола.</summary>
        /// <summary>
        /// Дальность, на которой игрок слышит выстрел (м), по типу ствола: пистолет 150, ПП 170, дробовик 200, автомат 250,
        /// винтовка 300, марксманка/снайперка 350, лук и арбалет 25. Громкость к краю падает линейно (Acoustics).
        /// </summary>
        public static float ShotHearM(string id)
        {
            if (Items.Get(id)?.Weapon is not WeaponDef w || w.Melee) return 25f;
            return w.NoiseRadius switch { <= 12 => 25f, <= 45 => 150f, <= 50 => 170f, <= 55 => 200f, <= 70 => 250f, <= 75 => 300f, _ => 350f };
        }

        public static float HearRange(string id) => Items.Get(id)?.Weapon is WeaponDef w && !w.Melee ? Math.Max(25f, w.NoiseRadius * 2.6f) : 25f;

        // ---------- лут ----------
        public static readonly string[] CommonParts = { "comp_spring", "comp_pipe", "comp_tape", "comp_gears" };
        public static readonly string[] Tier2Parts = { "comp_trigger", "comp_bolt_carrier", "comp_barrel_rifled", "comp_frame_pistol", "comp_body_smg", "comp_body_rifle" };
        public static readonly string[] Tier3Parts = { "comp_receiver_ak", "comp_barrel_chrome", "comp_gas_block" };

        /// <summary>Общий компонент для бочек и ящиков (D49): пружина 45 % (не точится, нужна везде), труба 20, изолента 17, шестерни 18.</summary>
        public static string CommonPart(Random rng) => rng.Next(100) switch { < 20 => "comp_pipe", < 65 => "comp_spring", < 82 => "comp_tape", _ => "comp_gears" };
        /// <summary>Деталь тира 2: корпуса (только лут) вдвое чаще точимых на станке УСМ, затвора и нарезного ствола.</summary>
        public static string Tier2Part(Random rng) => rng.Next(9) switch
        {
            0 or 1 => "comp_frame_pistol", 2 or 3 => "comp_body_smg", 4 or 5 => "comp_body_rifle", 6 => "comp_trigger", 7 => "comp_bolt_carrier", _ => "comp_barrel_rifled",
        };
        public static readonly string[] ArmyAtts = { "att_scope4x", "att_suppressor_pbs", "att_compensator_army", "att_mag_ext_army", "att_scope2x" };
        public static readonly string[] FactoryAtts = { "att_reddot", "att_suppressor", "att_compensator", "att_grip", "att_flashlight", "att_laser", "att_mag_ext", "att_mag_twin", "att_stock_tac" };

        // ---------- токарный станок ----------
        public sealed class LatheRecipe { public string Output; public (string id, int n)[] In; public float Time; public bool Bench3; }
        public static readonly List<LatheRecipe> Lathe = new()
        {
            new LatheRecipe { Output = "comp_pipe", In = new[] { ("metal_fragments", 40) }, Time = 10 },
            new LatheRecipe { Output = "comp_tape", In = new[] { ("cloth", 15) }, Time = 6 },
            new LatheRecipe { Output = "comp_gears", In = new[] { ("metal_fragments", 60), ("scrap", 10) }, Time = 15 },
            new LatheRecipe { Output = "comp_oil", In = new[] { ("charcoal", 20) }, Time = 8 },
            new LatheRecipe { Output = "comp_trigger", In = new[] { ("comp_spring", 2), ("comp_gears", 1), ("metal_fragments", 50) }, Time = 30 },
            new LatheRecipe { Output = "comp_bolt_carrier", In = new[] { ("comp_spring", 3), ("comp_gears", 2), ("metal_fragments", 80) }, Time = 40 },
            new LatheRecipe { Output = "comp_barrel_rifled", In = new[] { ("comp_pipe", 2), ("comp_gears", 1), ("metal_fragments", 100) }, Time = 45 },
            new LatheRecipe { Output = "comp_barrel_chrome", In = new[] { ("comp_barrel_rifled", 2), ("comp_spring", 2), ("metal_fragments", 200) }, Time = 90, Bench3 = true },
            new LatheRecipe { Output = "comp_gas_block", In = new[] { ("comp_trigger", 1), ("comp_spring", 3), ("comp_gears", 2), ("metal_fragments", 150) }, Time = 75, Bench3 = true },
        };
        public static LatheRecipe LatheFor(string output) => Lathe.Find(r => r.Output == output);

        static WeaponDef Gun(string id, string ammo, int tier, FireMode mode, float dmg, float interval, int mag, float reload, float range, string kind, float dur, int slots, int pellets = 1)
        {
            var w = new WeaponDef
            {
                Id = id, AmmoId = ammo, Tier = tier, Mode = mode, Damage = dmg, FireInterval = interval, Magazine = mag, ReloadTime = reload,
                Range = range, Auto = mode is FireMode.Auto or FireMode.Burst, MaxDurability = dur, Slots = slots, Pellets = pellets,
            };
            w.EffRange = Gunplay.DefaultEff(kind);
            switch (kind)
            {
                case "pistol":
                    w.BaseSpreadDeg = 2f; w.BloomPerShotDeg = 2f; w.MaxBloomDeg = 9f; w.BloomRecoverDeg = 14f;
                    w.RecoilDeg = 2.5f; w.RecoilGrowDeg = 0.3f; w.RecoilMaxDeg = 10f; w.RecoilRecoverDeg = 30f; w.BulletSpeed = 110; w.NoiseRadius = 45; break;
                case "smg":
                    w.BaseSpreadDeg = 2.5f; w.BloomPerShotDeg = 1.2f; w.MaxBloomDeg = 12f; w.BloomRecoverDeg = 14f;
                    w.RecoilDeg = 1.2f; w.RecoilGrowDeg = 0.15f; w.RecoilMaxDeg = 16f; w.RecoilRecoverDeg = 32f; w.BulletSpeed = 115; w.NoiseRadius = 50; break;
                case "rifle":
                    w.BaseSpreadDeg = 0.8f; w.BloomPerShotDeg = 2.5f; w.MaxBloomDeg = 8f; w.BloomRecoverDeg = 10f;
                    w.RecoilDeg = 3f; w.RecoilGrowDeg = 0.3f; w.RecoilMaxDeg = 14f; w.RecoilRecoverDeg = 26f; w.BulletSpeed = 165; w.NoiseRadius = 75; break;
                case "ar":
                    w.BaseSpreadDeg = 1.2f; w.BloomPerShotDeg = 2f; w.MaxBloomDeg = 13f; w.BloomRecoverDeg = 11f;
                    w.RecoilDeg = 1.6f; w.RecoilGrowDeg = 0.22f; w.RecoilMaxDeg = 26f; w.RecoilRecoverDeg = 32f;
                    w.RecoilPattern = new[] { 1, 1, -1, 1, -1, -1, -1, 1, 1, 1, -1, -1, 1, -1, -1, 1 }; w.BulletSpeed = 150; w.NoiseRadius = 70; break;
                case "shotgun":
                    w.BaseSpreadDeg = 2f; w.BloomPerShotDeg = 3f; w.MaxBloomDeg = 8f; w.BloomRecoverDeg = 12f; w.PelletSpreadDeg = 9f;
                    w.RecoilDeg = 4f; w.RecoilGrowDeg = 0.5f; w.RecoilMaxDeg = 12f; w.RecoilRecoverDeg = 30f; w.BulletSpeed = 90; w.NoiseRadius = 55; break;
                case "sniper":
                    w.BaseSpreadDeg = 0.3f; w.BloomPerShotDeg = 4f; w.MaxBloomDeg = 6f; w.BloomRecoverDeg = 6f;
                    w.RecoilDeg = 4f; w.RecoilMaxDeg = 10f; w.RecoilRecoverDeg = 20f; w.BulletSpeed = 200; w.NoiseRadius = 90; break;
                case "bow":
                    w.BaseSpreadDeg = 1.5f; w.BloomRecoverDeg = 10; w.BulletSpeed = 55; w.NoiseRadius = 12; break;
                case "crossbow":
                    w.BaseSpreadDeg = 1f; w.BloomRecoverDeg = 10; w.BulletSpeed = 62; w.NoiseRadius = 10; break;
            }
            return w;
        }

        const int S = 1, Mz = 2, F = 4, Mg = 8, St = 16, All = 31;

        /// <summary>Регистрация всех предметов ветки оружия (зовётся из Items).</summary>
        internal static void Register(Func<string, string, ItemCategory, ItemDef> add)
        {
            // калибры
            add("ammo_9mm", "Патроны 9 мм", ItemCategory.Ammo);
            add("ammo_12g", "Патроны 12 к", ItemCategory.Ammo);
            add("ammo_762", "Патроны 7,62", ItemCategory.Ammo);
            add("ammo_545", "Патроны 5,45", ItemCategory.Ammo);
            add("ammo_bolt", "Болт", ItemCategory.Ammo);
            M("ammo_9mm", "Пистолетный патрон: Наган, Самоделка, Грач, ППС-43, ППШ, АПС", 2, 1f);
            M("ammo_12g", "Дробь 12 калибра: Самопал, Обрез, МР-133, Сайга-12", 2, 1.5f, 64);
            M("ammo_762", "Винтовочный 7,62: СВТ-40, АКМ, СВД, Мосинка", 3, 2.5f);
            M("ammo_545", "5,45 для АН-94: дешевле по пороху, урон ниже", 3, 2f);
            M("ammo_bolt", "Болт для арбалета", 1, 0.5f, 64);

            // 16 компонентов
            void C(string id, string name, string desc, int rarity, float value, int stack, params (string, int)[] rec)
            {
                add(id, name, ItemCategory.Component);
                M(id, desc, rarity, value, stack, rec);
            }
            C("comp_spring", "Пружина", "Витая стальная пружина. Сама не сделаешь — только найти", 2, 12, 50, ("scrap", 8), ("metal_fragments", 10));
            C("comp_pipe", "Труба", "Обрезок водопроводной трубы. Ствол для самоделки", 2, 10, 50, ("scrap", 6), ("metal_fragments", 20));
            C("comp_tape", "Изолента", "Синяя изолента. Держит всё, что не должно развалиться", 1, 5, 50, ("scrap", 5), ("cloth", 5));
            C("comp_gears", "Шестерни", "Шестерни от механизмов. Для замков, станков и затворов", 2, 12, 50, ("scrap", 10), ("metal_fragments", 15));
            C("comp_circuit", "Плата", "Плата с радиодеталями. Для ЛЦУ, коллиматора и сложных механизмов", 2, 25, 20, ("scrap", 12));
            C("comp_lens", "Линза", "Просветлённая линза. Без неё прицел не собрать", 2, 25, 20, ("scrap", 10));
            C("comp_trigger", "УСМ", "Ударно-спусковой механизм: спуск, курок, шептало", 3, 40, 10, ("scrap", 18), ("comp_spring", 1));
            C("comp_bolt_carrier", "Затворная группа", "Затвор с рамой. Сердце любого автомата", 3, 55, 10, ("scrap", 22), ("comp_spring", 1), ("comp_gears", 1));
            C("comp_barrel_rifled", "Нарезной ствол", "Ствол с нарезами: точнее и дальше", 3, 50, 10, ("scrap", 20), ("comp_pipe", 1));
            C("comp_frame_pistol", "Корпус пистолета", "Рамка пистолета с рукоятью", 3, 45, 5, ("scrap", 25), ("metal_fragments", 40));
            C("comp_body_smg", "Корпус ПП", "Ствольная коробка пистолета-пулемёта", 3, 55, 5, ("scrap", 28), ("metal_fragments", 50));
            C("comp_body_rifle", "Корпус винтовки", "Ложа с коробкой для винтовки", 3, 60, 5, ("scrap", 30), ("wood", 50));
            C("comp_receiver_ak", "Ствольная коробка АК", "Фрезерованная коробка автомата. Таких не делают в гараже", 4, 200, 3, ("scrap", 60), ("metal_fragments", 50));
            C("comp_barrel_chrome", "Хромированный ствол", "Хромированный канал ствола: живёт долго, бьёт кучно", 4, 150, 5, ("scrap", 50), ("metal_fragments", 40));
            C("comp_gas_block", "Газоотводный узел", "Газовая камера с поршнем — для самозарядных и автоматов", 4, 140, 5, ("scrap", 45), ("metal_fragments", 40));
            C("comp_oil", "Оружейное масло", "Смазка. Чинит износ оружия и обвесов (Alt+E у верстака со стволом в руках)", 1, 6, 20, ("scrap", 2));

            // стволы: id, патрон, тир, режим, урон, интервал, магазин, перезарядка, дальность, вид, износ (выстрелов), слоты обвесов
            void G(string id, string name, WeaponDef w, int rarity, float value, string desc)
            {
                add(id, name, ItemCategory.Weapon).Weapon = w;
                M(id, desc, rarity, value);
            }
            G("weapon_bow", "Лук", Gun("weapon_bow", "arrow", 0, FireMode.Single, 45, 0.8f, 1, 0.7f, 40, "bow", 0, 0), 3, 25, "Тихое оружие, нужны стрелы");
            G("weapon_crossbow", "Арбалет", Gun("weapon_crossbow", "ammo_bolt", 0, FireMode.Single, 65, 0.5f, 1, 2.2f, 45, "crossbow", 400, S | F), 3, 45, "Тихий и мощный: болт с двух попаданий. Долго взводится");
            G("weapon_pipeshotgun", "Самопал", Gun("weapon_pipeshotgun", "ammo_12g", 1, FireMode.Single, 12, 0.5f, 1, 2.2f, 14, "shotgun", 120, F, 8), 2, 50, "Труба на 12 калибр: один выстрел дробью в упор. Не крафтится");
            G("weapon_sawnoff", "Обрез", Gun("weapon_sawnoff", "ammo_12g", 1, FireMode.Semi, 12, 0.25f, 2, 2.6f, 15, "shotgun", 250, F, 8), 3, 90, "Двустволка: два выстрела дробью в упор. Только найти");
            G("weapon_nagant", "Наган", Gun("weapon_nagant", "ammo_9mm", 1, FireMode.Semi, 36, 0.38f, 7, 2.4f, 35, "pistol", 400, S | Mz | F), 2, 60, "Револьвер: средняя дистанция, 4 попадания");
            G("weapon_homesmg", "Самоделка", Gun("weapon_homesmg", "ammo_9mm", 1, FireMode.Auto, 20, 0.11f, 20, 2.4f, 25, "smg", 500, All), 3, 90, "Кустарный ПП: лучший ствол тира 1, вблизи");
            G("weapon_grach", "Грач", Gun("weapon_grach", "ammo_9mm", 2, FireMode.Semi, 20, 0.18f, 17, 1.6f, 35, "pistol", 700, S | Mz | F | Mg), 3, 120, "МР-443: запасной пистолет, быстрая перезарядка");
            G("weapon_pps43", "ППС-43", Gun("weapon_pps43", "ammo_9mm", 2, FireMode.Auto, 26, 0.10f, 35, 2.3f, 35, "smg", 800, All), 3, 150, "Пистолет-пулемёт: близко, большой магазин");
            G("weapon_svt40", "СВТ-40", Gun("weapon_svt40", "ammo_762", 2, FireMode.Semi, 40, 0.30f, 10, 2.8f, 70, "rifle", 800, All), 3, 160, "Самозарядная винтовка: далеко, 3 попадания");
            G("weapon_mp133", "МР-133", Gun("weapon_mp133", "ammo_12g", 2, FireMode.Pump, 14, 0.85f, 6, 3.5f, 18, "shotgun", 700, Mz | F | St, 8), 3, 160, "Помповое ружьё: в упор убивает с выстрела. Не крафтится");
            G("weapon_akm", "АКМ", Gun("weapon_akm", "ammo_762", 3, FireMode.Auto, 34, 0.11f, 30, 2.5f, 65, "ar", 1000, All), 4, 250, "Автомат Калашникова 7,62: универсал, 3 попадания");
            var an = Gun("weapon_an94", "ammo_545", 3, FireMode.Burst, 29, 0.2f, 30, 2.4f, 75, "ar", 1000, All);
            an.EffRange = 45; an.Burst = 2; an.BurstInterval = 0.03f; an.RecoilDeg = 1.0f; an.RecoilGrowDeg = 0.08f; an.BloomPerShotDeg = 0.8f; an.BaseSpreadDeg = 1f;   // «Абакан» славится кучностью
            G("weapon_an94", "АН-94 «Абакан»", an, 4, 260, "Автомат с отсечкой: очередь по 2 почти в одну точку (58 урона за раз)");
            var ppsh = Gun("weapon_ppsh", "ammo_9mm", 3, FireMode.Auto, 27, 0.065f, 71, 4.0f, 40, "smg", 1000, All);
            ppsh.RecoilDeg = 1.6f; ppsh.RecoilGrowDeg = 0.18f; ppsh.RecoilMaxDeg = 24f;   // ППШ: огромная плотность огня и злая отдача — зажал и не даёшь высунуться
            G("weapon_ppsh", "ППШ", ppsh, 4, 230, "Барабан на 71 патрон: близко, огромный темп. Долгая перезарядка");
            G("weapon_svd", "СВД", Gun("weapon_svd", "ammo_762", 3, FireMode.Semi, 60, 0.35f, 10, 2.8f, 100, "sniper", 1000, All), 4, 250, "Снайперская винтовка Драгунова: 2 попадания, 100 м");
            G("weapon_saiga12", "Сайга-12", Gun("weapon_saiga12", "ammo_12g", 4, FireMode.Semi, 14, 0.25f, 8, 3.0f, 18, "shotgun", 900, All, 8), 4, 320, "Элита: самозарядный дробовик, «лютая имба» в упор. Только редкие ивенты");
            var mosin = Gun("weapon_mosin", "ammo_762", 4, FireMode.Bolt, 110, 1.2f, 5, 3.5f, 120, "sniper", 900, S | Mz | St);
            G("weapon_mosin", "Мосинка с ПУ", mosin, 4, 300, "Элита: снайперская винтовка с затвором, 120 м. Только редкие ивенты");
            G("weapon_aps", "АПС", Gun("weapon_aps", "ammo_9mm", 4, FireMode.Auto, 22, 0.07f, 20, 1.8f, 30, "smg", 900, S | Mz | F | Mg), 4, 220, "Элита: автоматический пистолет Стечкина. Только редкие ивенты");

            // D57: рабочие дистанции и характер стволов (остальное — по классу, Data/Gunplay.cs)
            void Eff(string id, float eff) => Items.Get(id).Weapon.EffRange = eff;
            Eff("weapon_svt40", 55); Eff("weapon_svd", 80); Eff("weapon_mosin", 100); Eff("weapon_saiga12", 10); Eff("weapon_mp133", 10);
            var akm = Items.Get("weapon_akm").Weapon;
            akm.RecoilDeg = 2.2f; akm.RecoilGrowDeg = 0.3f;   // АКМ: злая отдача — оружие средней дистанции; дальше — АН-94

            // обвесы
            void A(string id, string name, AttachmentDef a, float value, string desc)
            {
                a.Id = id;
                add(id, name, ItemCategory.Component).Attachment = a;
                M(id, $"Обвес · {SlotNames[(int)a.Slot]} · {QualityNames[a.Quality]}. {desc}. Перетащи на ствол — встанет; колёсико по стволу — снять обвесы", a.Quality + 2, 20 + 40 * a.Quality, 1);
            }
            A("att_reddot", "Коллиматор", new AttachmentDef { Slot = AttSlot.Sight, Quality = 1, SightZoom = 1f, AdsSpreadMul = 0.8f }, 60, "Красная точка: в прицеливании −20% разброса");
            A("att_scope2x", "Прицел 2×", new AttachmentDef { Slot = AttSlot.Sight, Quality = 1, SightZoom = 1.5f, AdsSpreadMul = 0.85f }, 80, "Кратность 2×");
            A("att_scope4x", "ПСО 4×", new AttachmentDef { Slot = AttSlot.Sight, Quality = 2, SightZoom = 2.2f, AdsSpreadMul = 0.75f }, 150, "Армейский оптический прицел 4×");
            A("att_suppressor_can", "Глушитель-банка", new AttachmentDef { Slot = AttSlot.Muzzle, Quality = 0, NoiseMul = 0.65f, MaxShots = 40 }, 15, "−35% шума, живёт 40 выстрелов");
            A("att_suppressor", "Глушитель", new AttachmentDef { Slot = AttSlot.Muzzle, Quality = 1, NoiseMul = 0.5f }, 70, "−50% шума");
            A("att_suppressor_pbs", "ПБС", new AttachmentDef { Slot = AttSlot.Muzzle, Quality = 2, NoiseMul = 0.35f, NoFlash = true }, 150, "−65% шума, без вспышки");
            A("att_compensator", "Компенсатор", new AttachmentDef { Slot = AttSlot.Muzzle, Quality = 1, RecoilMul = 0.8f }, 50, "−20% увода ствола");
            A("att_compensator_army", "Компенсатор армейский", new AttachmentDef { Slot = AttSlot.Muzzle, Quality = 2, RecoilMul = 0.7f }, 110, "−30% увода ствола");
            A("att_grip_wood", "Рукоять на изоленте", new AttachmentDef { Slot = AttSlot.Foregrip, Quality = 0, RecoilMul = 0.9f }, 10, "−10% отдачи");
            A("att_grip", "Рукоять", new AttachmentDef { Slot = AttSlot.Foregrip, Quality = 1, RecoilMul = 0.8f }, 45, "−20% отдачи");
            A("att_torch", "Факел на изоленте", new AttachmentDef { Slot = AttSlot.Foregrip, Quality = 0, Flashlight = true }, 10, "Светит ночью");
            A("att_flashlight", "Тактический фонарь", new AttachmentDef { Slot = AttSlot.Foregrip, Quality = 1, Flashlight = true }, 40, "Светит ночью ярко и далеко");
            A("att_hip_module", "Модуль стрельбы от бедра", new AttachmentDef { Slot = AttSlot.Foregrip, Quality = 1, HipSpreadMul = 0.7f, AdsSpreadMul = 1.3f }, 70, "Для бегунов: −30% разброса от бедра, но +30% в прицеливании");
            A("att_laser", "ЛЦУ", new AttachmentDef { Slot = AttSlot.Foregrip, Quality = 1, HipSpreadMul = 0.75f }, 70, "Лазер: −25% разброса от бедра");
            A("att_mag_ext", "Увеличенный магазин", new AttachmentDef { Slot = AttSlot.Magazine, Quality = 1, MagMul = 1.5f }, 70, "+50% патронов");
            A("att_mag_ext_army", "Увеличенный магазин армейский", new AttachmentDef { Slot = AttSlot.Magazine, Quality = 2, MagMul = 1.75f }, 120, "+75% патронов");
            A("att_mag_twin_crude", "Спарка на изоленте", new AttachmentDef { Slot = AttSlot.Magazine, Quality = 0, ReloadMul = 0.8f }, 10, "−20% перезарядки");
            A("att_mag_twin", "Спарка", new AttachmentDef { Slot = AttSlot.Magazine, Quality = 1, ReloadMul = 0.65f }, 45, "−35% перезарядки");
            A("att_stock_tac", "Тактический приклад", new AttachmentDef { Slot = AttSlot.Stock, Quality = 1, MoveSpreadMul = 0.7f }, 60, "−30% разброса на ходу");

            // токарный станок
            add("deploy_lathe", "Токарный станок", ItemCategory.Deployable).Deploy = DeployKind.Lathe;
            M("deploy_lathe", "Делает детали выше из деталей ниже (как печь). Шумит. Детали тира 3 — только рядом с верстаком 3", 3, 120);
        }

        /// <summary>Держат двумя руками (для рисовки): всё, кроме пистолетов и ближнего боя.</summary>
        public static bool TwoHanded(string id) => IsGun(id) && id is not ("weapon_nagant" or "weapon_grach" or "weapon_aps");

        /// <summary>Длина ствола в руках, метры (по ней масштабируется спрайт). 0 — не ствол из этой ветки.</summary>
        public static float HeldLength(string id) => id switch
        {
            "weapon_nagant" or "weapon_grach" or "weapon_aps" => 0.35f,
            "weapon_pipeshotgun" => 0.55f, "weapon_sawnoff" => 0.6f, "weapon_homesmg" or "weapon_pps43" => 0.65f, "weapon_ppsh" => 0.8f,
            "weapon_crossbow" => 0.75f, "weapon_bow" => 0.85f, "weapon_mp133" or "weapon_saiga12" => 0.9f,
            "weapon_akm" or "weapon_an94" => 0.92f, "weapon_svt40" => 1.0f, "weapon_svd" or "weapon_mosin" => 1.05f,
            _ => 0f,
        };

        /// <summary>Порядок по силе — после регистрации.</summary>
        internal static void Finish()
        {
            GunsByPower.Clear();
            foreach (var id in new[] { "weapon_saiga12", "weapon_akm", "weapon_an94", "weapon_mosin", "weapon_ppsh", "weapon_svd", "weapon_aps",
                                       "weapon_pps43", "weapon_svt40", "weapon_mp133", "weapon_grach", "weapon_homesmg", "weapon_nagant",
                                       "weapon_sawnoff", "weapon_pipeshotgun", "weapon_crossbow", "weapon_bow" })
                GunsByPower.Add(id);
        }

        internal static void AddRecipes(List<Recipe> all)
        {
            Recipe R(string output, float time, int bench, int count, params (string, int)[] cost) => new Recipe { Output = output, Time = time, Bench = bench, Count = count, Cost = cost };
            all.AddRange(new[]
            {
                // патроны
                R("ammo_9mm", 3, 1, 5, ("metal_fragments", 10), ("gunpowder", 5)),
                R("ammo_12g", 3, 1, 5, ("metal_fragments", 15), ("gunpowder", 10)),
                R("ammo_762", 4, 2, 5, ("metal_fragments", 15), ("gunpowder", 15)),
                R("ammo_545", 4, 3, 5, ("metal_fragments", 10), ("gunpowder", 10)),
                R("ammo_bolt", 2, 0, 2, ("wood", 25), ("metal_fragments", 5)),
                // верстак 1
                R("weapon_crossbow", 10, 1, 1, ("wood", 150), ("cloth", 20), ("comp_spring", 1)),
                R("weapon_nagant", 10, 1, 1, ("metal_fragments", 100), ("comp_spring", 2), ("comp_pipe", 1)),
                R("weapon_homesmg", 14, 1, 1, ("metal_fragments", 125), ("comp_spring", 2), ("comp_pipe", 2), ("comp_tape", 2)),
                // верстак 2
                R("weapon_grach", 16, 2, 1, ("metal_fragments", 150), ("comp_frame_pistol", 1), ("comp_trigger", 1), ("comp_spring", 2)),
                R("weapon_pps43", 18, 2, 1, ("metal_fragments", 175), ("comp_body_smg", 1), ("comp_bolt_carrier", 1), ("comp_spring", 3)),
                R("weapon_svt40", 18, 2, 1, ("metal_fragments", 200), ("wood", 100), ("comp_body_rifle", 1), ("comp_barrel_rifled", 1), ("comp_trigger", 1)),
                // верстак 3
                R("weapon_akm", 25, 3, 1, ("metal_fragments", 300), ("wood", 100), ("comp_receiver_ak", 1), ("comp_bolt_carrier", 1), ("comp_gas_block", 1), ("comp_spring", 4)),
                R("weapon_an94", 25, 3, 1, ("metal_fragments", 300), ("comp_receiver_ak", 1), ("comp_barrel_chrome", 1), ("comp_bolt_carrier", 1), ("comp_gas_block", 1), ("comp_gears", 3)),
                R("weapon_ppsh", 25, 3, 1, ("metal_fragments", 250), ("wood", 150), ("comp_body_smg", 1), ("comp_barrel_chrome", 1), ("comp_bolt_carrier", 1), ("comp_spring", 4)),
                R("weapon_svd", 25, 3, 1, ("metal_fragments", 300), ("wood", 100), ("comp_body_rifle", 1), ("comp_barrel_chrome", 1), ("comp_gas_block", 1), ("comp_trigger", 1)),
                // станок
                R("deploy_lathe", 20, 2, 1, ("metal_fragments", 250), ("scrap", 50), ("comp_gears", 2)),
                // обвесы: кустарные (верстак 1) и заводские (верстак 2)
                R("att_suppressor_can", 5, 1, 1, ("metal_fragments", 50), ("comp_tape", 1)),
                R("att_grip_wood", 4, 1, 1, ("wood", 50), ("comp_tape", 1), ("rubber", 2)),   // обмотка из резины
                R("att_torch", 4, 1, 1, ("wood", 30), ("cloth", 5), ("comp_tape", 1)),
                R("att_mag_twin_crude", 4, 1, 1, ("comp_tape", 2)),
                R("att_reddot", 10, 2, 1, ("metal_fragments", 75), ("comp_lens", 1), ("comp_circuit", 1)),
                R("att_scope2x", 12, 2, 1, ("metal_fragments", 100), ("comp_lens", 2), ("comp_gears", 1)),
                R("att_suppressor", 10, 2, 1, ("metal_fragments", 150), ("comp_barrel_rifled", 1), ("comp_tape", 1)),
                R("att_compensator", 8, 2, 1, ("metal_fragments", 100), ("comp_gears", 2)),
                R("att_grip", 6, 2, 1, ("metal_fragments", 75), ("rubber", 4), ("comp_gears", 1)),   // прорезиненная рукоять
                R("att_flashlight", 6, 2, 1, ("metal_fragments", 50), ("comp_circuit", 1)),
                R("att_laser", 8, 2, 1, ("metal_fragments", 50), ("comp_circuit", 1), ("comp_lens", 1)),
                R("att_mag_ext", 8, 2, 1, ("metal_fragments", 100), ("comp_spring", 2)),
                R("att_mag_twin", 6, 2, 1, ("metal_fragments", 75), ("comp_spring", 1)),
                R("att_stock_tac", 8, 2, 1, ("metal_fragments", 100), ("wood", 50), ("comp_gears", 1)),
            });
        }

        /// <summary>
        /// Дерево изучения ветки оружия (лом, D49). Путь до АКМ: порох 15 + 9 мм 20 + Наган 35 + Самоделка 60 + Грач 75 + 7,62 60 + АКМ 420
        /// ≈ 685 лома (через Арбалет 20 + СВТ 90 — ≈ 630). «Изучить всё» с одеждой и взрывчаткой: верстак 1 ≈ 250, 2 ≈ 670, 3 ≈ 1300.
        /// Дробовики и элита — без узлов (не крафтятся).
        /// </summary>
        internal static void AddTech(List<TechNode> all)
        {
            TechNode N(string id, int bench, int scrap, int col, int row, string[] req = null, string[] any = null)
                => new TechNode { Id = id, Bench = bench, Scrap = scrap, Col = col, Row = row, Requires = req ?? Array.Empty<string>(), RequiresAny = any ?? Array.Empty<string>() };
            all.AddRange(new[]
            {
                // патроны
                N("ammo_9mm", 1, 20, 0, 1, new[] { "gunpowder" }),
                N("ammo_12g", 1, 20, 0, 2, new[] { "gunpowder" }),
                N("ammo_762", 2, 60, 1, 1, new[] { "ammo_9mm" }),
                N("ammo_545", 3, 60, 2, 1, new[] { "ammo_762" }),
                // ветка «Пистолеты и ПП»
                N("weapon_nagant", 1, 35, 0, 3, new[] { "ammo_9mm" }),
                N("weapon_homesmg", 1, 60, 0, 4, new[] { "weapon_nagant" }),
                N("weapon_grach", 2, 75, 1, 2, new[] { "weapon_homesmg" }),
                N("weapon_pps43", 2, 90, 1, 3, new[] { "weapon_grach" }),
                N("weapon_ppsh", 3, 160, 2, 3, new[] { "weapon_pps43" }),
                // ветка «Винтовки»
                N("weapon_crossbow", 1, 20, 0, 5),
                N("weapon_svt40", 2, 90, 1, 4, new[] { "weapon_crossbow" }),
                N("weapon_akm", 3, 420, 2, 2, new[] { "ammo_762" }, new[] { "weapon_grach", "weapon_svt40" }),
                N("weapon_an94", 3, 200, 2, 4, new[] { "ammo_545" }, new[] { "weapon_grach", "weapon_svt40" }),
                N("weapon_svd", 3, 160, 2, 5, new[] { "weapon_svt40", "ammo_762" }),
                // обвесы
                N("att_suppressor_can", 1, 12, 0, 6),
                N("att_grip_wood", 1, 12, 0, 7),
                N("att_torch", 1, 12, 0, 8),
                N("att_mag_twin_crude", 1, 12, 0, 9),
                N("att_reddot", 2, 25, 1, 5),
                N("att_scope2x", 2, 35, 1, 6, new[] { "att_reddot" }),
                N("att_suppressor", 2, 30, 1, 7, new[] { "att_suppressor_can" }),
                N("att_compensator", 2, 25, 1, 8),
                N("att_grip", 2, 20, 1, 9, new[] { "att_grip_wood" }),
                N("att_flashlight", 2, 15, 1, 10, new[] { "att_torch" }),
                N("att_laser", 2, 25, 1, 11, new[] { "att_flashlight" }),
                N("att_mag_ext", 2, 30, 1, 12),
                N("att_mag_twin", 2, 20, 1, 13, new[] { "att_mag_twin_crude" }),
                N("att_stock_tac", 2, 25, 1, 14),
            });
        }
    }
}
