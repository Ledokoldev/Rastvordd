using System;
using System.Collections.Generic;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Куда попала пуля (D55): не по точке на модельке, а шансом — голова/конечности/корпус.
    /// Шанс в голову (%) = база + прицел + присед − движение + тир ствола + обвесы (прицелы — по дистанции) + марксманка,
    /// потолок 17%. Без прицела у марксманок и болтовок голова почти невозможна (≤ 1,5%). Все числа — здесь.
    /// </summary>
    public static class Headshots
    {
        public const float Base = 5f;            // стоя, от бедра
        public const float Aim = 1.5f;           // прицелился (ПКМ)
        public const float Crouch = 1.5f;        // присел
        public const float Moving = -1f;         // стреляет на ходу
        public const float Cap = 17f;            // больше — уже «параша»; 17–18 — край на топ-обвесах
        public const float MarksmanAim = 6f;     // марксманки и болтовки в прицеле
        public const float MarksmanHipCap = 1.5f;   // ...а от бедра — не больше 1,5%
        public const float LimbsBase = 30f, LimbsAim = -5f;   // конечности (%); в прицеле реже

        public static float TierBonus(int tier) => tier >= 4 ? 3.5f : tier == 3 ? 3f : tier == 2 ? 1.5f : 0f;

        /// <summary>Постоянный бонус обвеса (%): рукояти, приклад, компенсатор; ЛЦУ — только от бедра.</summary>
        static readonly Dictionary<string, (float aim, float hip)> Flat = new()
        {
            ["att_grip"] = (1f, 1f), ["att_grip_wood"] = (0.5f, 0.5f),
            ["att_stock_tac"] = (1f, 0.5f),
            ["att_compensator"] = (0.5f, 0.5f), ["att_compensator_army"] = (1f, 1f),
            ["att_laser"] = (0f, 1f),
        };

        /// <summary>Прицелы (только в прицеливании): бонус на эффективной дистанции, штраф вплотную (м).</summary>
        static readonly Dictionary<string, (float effMin, float effMax, float bonus, float closeUnder, float closePenalty)> Sights = new()
        {
            ["att_reddot"] = (0f, 40f, 1.5f, 0f, 0f),
            ["att_scope2x"] = (15f, 60f, 2.5f, 8f, -1.5f),
            ["att_scope4x"] = (35f, 120f, 3.5f, 15f, -4f),
        };

        public static bool Marksman(WeaponDef w) => w != null && w.Pellets == 1 && (w.Mode == FireMode.Bolt || (w.Mode == FireMode.Semi && w.Range >= 70f));

        /// <summary>Шанс в голову (%) для стрелка с его стволом на этой дистанции.</summary>
        public static float HeadChance(Actor shooter, WeaponState ws, float dist)
        {
            if (shooter == null || ws == null) return Base;
            bool aim = shooter.Peek > 0.5f;
            float c = Base + (aim ? Aim : 0) + (shooter.Crouching ? Crouch : 0) + (shooter.Vel.LengthSquared() > 0.25f ? Moving : 0);
            c += TierBonus(ws.Def.Tier);
            foreach (var id in ws.Att)
            {
                if (id == null) continue;
                if (Flat.TryGetValue(id, out var f)) c += aim ? f.aim : f.hip;
                if (aim && Sights.TryGetValue(id, out var s))
                {
                    if (dist >= s.effMin && dist <= s.effMax) c += s.bonus;
                    else if (dist < s.closeUnder) c += s.closePenalty;
                }
            }
            if (Marksman(ws.Def))
            {
                if (aim) c += MarksmanAim;
                else c = MathF.Min(c, MarksmanHipCap);
            }
            return Math.Clamp(c, 0.5f, Cap);
        }

        public static float LimbChance(Actor shooter) => LimbsBase + (shooter != null && shooter.Peek > 0.5f ? LimbsAim : 0);

        /// <summary>Бросок зоны: голова, конечности или корпус.</summary>
        public static HitZone Roll(Random rng, Actor shooter, WeaponState ws, float dist)
        {
            float r = (float)rng.NextDouble() * 100f;
            float head = HeadChance(shooter, ws, dist);
            if (r < head) return HitZone.Head;
            if (r < head + LimbChance(shooter)) return HitZone.Limbs;
            return HitZone.Torso;
        }
    }
}
