using System;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Одежда в бою и в среде (D47, docs/design/14): зоны попадания, защита по зонам, пластины, штраф скорости,
    /// радиация по РТ и холод по биому/времени суток. Данные и константы — Clothing.cs.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>Броня для HUD: защита корпуса спереди (0..1) с учётом пластины.</summary>
        public float ArmorReduction(Actor a) => Clothing.Zone(a, HitZone.Torso, 0);

        /// <summary>Отдельный ГСЧ для одежды: лут и наряд охраны не сдвигают основной поток мира (карта по зерну та же).</summary>
        Random _wearRng;
        internal Random WearRng => _wearRng ??= new Random(Seed * 92821 + 4447);

        /// <summary>Хуки радиации для будущих ивентов (позиция ПВО 20, обломки вертолёта 45, «Объект 42» 50/80): центр, радиус, уровень.</summary>
        public readonly System.Collections.Generic.List<(Vector2 c, float r, float level)> RadSpots = new();
        /// <summary>Хук: склад «Центральный» (РТ тира 2, по документу 30–45). Пока 0 — не просили.</summary>
        public float RadWarehouse = 0;
        /// <summary>Хуки погоды: дождь (−4 °C, мокрый) и зима (−15 °C). Дождя в игре пока нет — выключено.</summary>
        public bool Raining, Winter;

        // ---------- зоны попадания ----------

        /// <summary>Зона по расстоянию линии пули от центра (пороги 0,12/0,28/0,40 м для тела 0,40 м, масштаб по радиусу).</summary>
        public static HitZone ZoneOf(float offset, float radius)
        {
            float k = radius / Clothing.DocBodyRadius;
            return offset < Clothing.HeadR * k ? HitZone.Head : offset < Clothing.TorsoR * k ? HitZone.Torso : HitZone.Limbs;
        }

        public static float ZoneMul(HitZone z) => z switch { HitZone.Head => Clothing.HeadMul, HitZone.Torso => Clothing.TorsoMul, _ => Clothing.LimbMul };

        /// <summary>Сторона корпуса, куда пришёл удар: 0 — спереди (из передней полусферы), 1 — со спины.</summary>
        public static int SideOf(Actor t, Vector2 shotDir) => Vector2.Dot(t.FacingDir, -shotDir) >= 0 ? 0 : 1;

        /// <summary>
        /// Попадание пули: зона по расстоянию линии от центра → множитель (голова ×1,8, корпус ×1, конечности ×0,6) →
        /// защита зоны (корпус — с пластиной своей стороны) → урон. Пластина изнашивается на сырой урон попадания в её сторону.
        /// </summary>
        public void BulletHit(Actor t, Vector2 from, Vector2 dir, float damage, int ownerId, Vector2 hitPos)
        {
            var d = dir.LengthSquared() > 1e-6f ? Vector2.Normalize(dir) : Vector2.UnitX;
            var rel = t.Pos - from;
            float offset = MathF.Abs(rel.X * d.Y - rel.Y * d.X);
            // зона — шансом (D55, Data/Headshots.cs): прицел, присед, тир ствола, обвесы, марксманки; без стрелка (тесты) — по смещению
            var shooter = ownerId >= 0 ? Find(ownerId) : null;
            var zone = shooter?.Weapon is WeaponState sw ? Headshots.Roll(Rng, shooter, sw, Vector2.Distance(shooter.Pos, hitPos)) : ZoneOf(offset, t.Radius);
            float raw = damage * ZoneMul(zone);
            int side = SideOf(t, d);
            float prot = Clothing.Zone(t, zone, side);
            string tag = zone switch { HitZone.Head => Clothing.HardHelmet(t) ? "helmet" : "head", HitZone.Torso => "torso", _ => "limb" };
            if (zone == HitZone.Torso && Clothing.ActivePlate(t, side) is ItemSlot plate)
            {
                tag = "plate";
                plate.Dur = Clothing.PlateDur(plate) - raw;
                if (plate.Dur <= 0)
                {
                    t.Plates[side] = null;
                    Events.Add(new SimEvent { Kind = SimEventKind.Wear, ActorId = t.Id, Pos = t.Pos, Text = "plate_break" });
                    Info(t, $"{Items.Name(plate.Id)} {(side == 0 ? "спереди" : "сзади")} разбита");
                }
            }
            Damage(t, raw * (1f - prot), ownerId, hitPos, tag);
        }

        /// <summary>Ближний бой: защита корпуса ×0,5 (со стороны удара), пластины не изнашиваются.</summary>
        float MeleeAfterArmor(Actor victim, Actor attacker, float dmg)
        {
            var dir = victim.Pos - attacker.Pos;
            int side = dir.LengthSquared() > 1e-6f ? SideOf(victim, Vector2.Normalize(dir)) : 0;
            return dmg * (1f - Clothing.MeleeFactor * Clothing.Zone(victim, HitZone.Torso, side));
        }

        /// <summary>Множитель скорости от брони: 1 + сумма штрафов (не ниже 0,85).</summary>
        public static float SpeedMul(Actor a) => (1f + Clothing.SpeedPenalty(a)) * Liquids.HeavyMul(a);   // + полная канистра 20 л (D54)

        // ---------- радиация и температура ----------


        /// <summary>Уровень радиации в точке (ед.): по РТ (docs/design/14, «Уровни радиации») и хукам.</summary>
        public float RadLevelAt(Vector2 p)
        {
            float lvl = 0;
            // фон — по тиру РТ (D53), одинаковый по всей территории: правило «какой сет куда пускает» читается без исключений
            foreach (var s in Substations) if (s.M.Contains(p)) lvl = MathF.Max(lvl, Clothing.RtRad[1]);
            foreach (var rt in Tier1) if (rt.M.Contains(p)) lvl = MathF.Max(lvl, Clothing.RtRad[1]);
            foreach (var rt in Tier2) if (rt.M.Contains(p)) lvl = MathF.Max(lvl, Clothing.RtRad[2]);
            if (RadWarehouse > 0 && MonumentAt(p)?.Name == "Склад «Центральный»") lvl = MathF.Max(lvl, RadWarehouse);
            foreach (var (c, r, l) in RadSpots) if (Vector2.Distance(p, c) < r) lvl = MathF.Max(lvl, l);
            return lvl;
        }

        /// <summary>Температура воздуха (°C): биом днём, ночь −10, дождь −4, зима −15 (сезоны выключены).</summary>
        public float AirTempAt(Vector2 p)
        {
            float t = Terrain.BiomeAt(p) switch
            {
                Biome.Arid => 18, Biome.Beach or Biome.Grassland => 14, Biome.Forest => 10, Biome.Swamp => 10, Biome.Tundra => -8, _ => 12,
            };
            t += Clothing.NightCold * NightFactorAt(TimeOfDay);
            if (Raining) t += Clothing.RainCold;
            if (Winter) t += Clothing.WinterCold;
            return t;
        }

        bool UnderRoof(Vector2 p)
        {
            if (FloorAt(p) != null) return true;   // в своей постройке (на фундаменте)
            foreach (var r in Roofs) if (p.X > r.Min.X && p.X < r.Max.X && p.Y > r.Min.Y && p.Y < r.Max.Y) return true;
            return false;
        }

        bool NearHeat(Vector2 p)
        {
            foreach (var d in DeploysNear(p, 4f)) if ((d.Kind == DeployKind.Furnace || StationDefs.Get(d.Kind)?.Warm == true) && d.Burning && Vector2.Distance(d.Pos, p) < 4f) return true;   // печь и костёр (D54)
            foreach (var f in Fires) if (Vector2.Distance(f.Pos, p) < f.Radius + 3f) return true;
            return false;
        }

        /// <summary>Пересчитать «где стоит» (3 раза в секунду): радиация, воздух, огонь, крыша.</summary>
        public void SenseEnvironment(Actor a)
        {
            a.RadHere = RadLevelAt(a.Pos);
            a.AirTemp = AirTempAt(a.Pos);
            a.NearFire = NearHeat(a.Pos);
            a.Indoors = UnderRoof(a.Pos);
            if (a.Swimming || (Raining && !a.Indoors)) a.WetLeft = 60f;   // мокрый ещё минуту после воды
            a.FeltTemp = a.AirTemp + Clothing.Warmth(a) + (a.NearFire ? Clothing.FireWarm : 0) + (a.Indoors ? Clothing.IndoorWarm : 0) + (a.WetLeft > 0 ? Clothing.WetCold : 0);
        }

        /// <summary>Журнал смертей от среды (для прогонов ботов).</summary>
        public readonly System.Collections.Generic.List<string> EnvDeaths = new();

        /// <summary>Доза, переохлаждение и их урон. Игроки и боты; охране РТ не нужно.</summary>
        void StepEnvironment(Actor a)
        {
            if (a.Team == Team.Npc) return;
            if ((Tick + (uint)a.Id) % 10 == 0) SenseEnvironment(a);
            a.WetLeft = MathF.Max(0, a.WetLeft - Dt);
            EnvTick(a, Dt);
            if (a.Hp <= 0 && !a.Dead)
            {
                EnvDeaths.Add($"[{Time,6:0}s] {a.Name} умер от {(a.RadDose >= Clothing.RadHurt1 ? $"радиации (доза {a.RadDose:0}, уровень {a.RadHere:0}, защита {Clothing.RadProt(a):0})" : $"холода ({a.FeltTemp:0} °C, воздух {a.AirTemp:0}, тепло одежды {Clothing.Warmth(a):0})")} @({a.Pos.X:0};{a.Pos.Y:0}) {Terrain.BiomeAt(a.Pos)}");
                Kill(a, -1);
            }
        }

        /// <summary>Шаг дозы и переохлаждения на dt (без смерти — её решает вызывающий). Открыт для самотеста.</summary>
        public static void EnvTick(Actor a, float dt)
        {
            // радиация: не хватает защиты — доза копится (уровень − защита) × 0,1 в секунду, иначе выводится
            float lack = a.RadHere - Clothing.RadProt(a);
            if (lack > 0) a.RadDose = MathF.Min(Clothing.RadMax, a.RadDose + lack * Clothing.RadRate * dt);
            else a.RadDose = MathF.Max(0, a.RadDose - Clothing.RadDecay * dt);
            float radDmg = a.RadDose >= Clothing.RadHurt3 ? 3f : a.RadDose >= Clothing.RadHurt1 ? 1f : 0f;

            // холод: воздух + тепло + огонь/дом − мокрый ≥ +15 — комфортно, иначе копится переохлаждение, на 100 — урон
            float deficit = Clothing.Comfort - a.FeltTemp;
            if (deficit > 0) a.Cold = MathF.Min(100, a.Cold + deficit * Clothing.ColdRise * dt);
            else a.Cold = MathF.Max(0, a.Cold - Clothing.ColdRecover * dt);
            float coldDmg = a.Cold >= 100 && deficit > 0 ? Clothing.ColdDmgBase + deficit * Clothing.ColdDmgPerDeg : 0f;

            if (radDmg + coldDmg > 0) a.Hp -= (radDmg + coldDmg) * dt;
        }

        /// <summary>Тошнота от радиации: лечение хуже (сытость и бинты — вполовину).</summary>
        public static float HealMul(Actor a) => a.RadDose >= Clothing.RadNausea ? 0.5f : 1f;

        /// <summary>Металл на бегу гремит: шум для слуха ботов раз в секунду.</summary>
        void ArmorRattle(Actor a, bool sprint)
        {
            if (!sprint || (Tick + (uint)a.Id) % 30 != 0 || !Clothing.Noisy(a)) return;
            Noises.Add(new Noise { Pos = a.Pos, Radius = 14f, SourceId = a.Id, Time = Time });
        }

        /// <summary>Антирадин: −120 дозы. true — принят.</summary>
        bool TakeAntirad(Actor a)
        {
            if (a.RadDose < 20 || a.Count("antirad") <= 0) return false;
            a.Take("antirad", 1);
            a.RadDose = MathF.Max(0, a.RadDose - Clothing.AntiradDose);
            Info(a, $"Антирадин: доза {a.RadDose:0}");
            return true;
        }

        // ---------- надеть / снять ----------

        /// <summary>Лишние пластины (броня сменилась на меньшую) — в рюкзак.</summary>
        void FixPlates(Actor a)
        {
            int cap = Clothing.PlateCap(a);
            for (int i = cap; i < Clothing.PlateSlots; i++)
            {
                if (a.Plates[i] == null) continue;
                var s = a.Plates[i];
                a.Plates[i] = null;
                if (!a.AddSlot(s)) a.Overflow.Add(s);
            }
        }

        void WearSound(Actor a, string id) => Events.Add(new SimEvent { Kind = SimEventKind.Wear, ActorId = a.Id, Pos = a.Pos, Text = Clothing.EquipSound(id) });

        /// <summary>
        /// Боты: надеть лучшее из рюкзака по Clothing.BotScore, вставить лучшие пластины. Прямо, без окон.
        /// Снятое — обратно в рюкзак (на место надетого).
        /// </summary>
        public void BotDress(Actor a)
        {
            for (int i = 0; i < Actor.PackSize; i++)
            {
                var s = a.Pack[i];
                if (Clothing.Def(s?.Id) is not WearDef d) continue;
                int k = (int)d.Slot;
                var cur = a.Worn[k];
                if (cur != null && Clothing.BotScore(Clothing.Def(cur.Id)) >= Clothing.BotScore(d)) continue;
                a.Worn[k] = s;
                a.Pack[i] = cur;
                Milestone(a, $"надел: {Items.Name(s.Id)}");   // для отчёта прогонов ботов
            }
            FixPlates(a);
            for (int side = 0; side < Clothing.PlateCap(a); side++)
                for (int i = 0; i < Actor.PackSize; i++)
                {
                    var s = a.Pack[i];
                    if (Clothing.Plate(s?.Id) is not PlateDef pd) continue;
                    var cur = a.Plates[side];
                    float curV = cur == null ? -1 : Clothing.Plate(cur.Id).Bonus * Clothing.PlateDur(cur);
                    if (pd.Bonus * Clothing.PlateDur(s) <= curV) continue;
                    a.Plates[side] = s;
                    a.Pack[i] = cur;
                }
        }

        /// <summary>Надеть набор (охрана РТ, отладка, самотест): по id прямо в слоты, рюкзак не трогается.</summary>
        public void Dress(Actor a, params string[] ids)
        {
            foreach (var id in ids)
            {
                if (Clothing.Def(id) is WearDef d) a.Worn[(int)d.Slot] = new ItemSlot(id, 1);
                else if (Clothing.Plate(id) != null)
                    for (int i = 0; i < Clothing.PlateSlots; i++) if (a.Plates[i] == null) { a.Plates[i] = new ItemSlot(id, 1); break; }
            }
        }
    }
}
