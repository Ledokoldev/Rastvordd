using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>
    /// Нарисованный персонаж (--hero): покадровые листы анимаций chars/hero/&lt;анимация&gt;_&lt;f|b&gt;&lt;i&gt;.png
    /// (f — вперёд-вправо, b — назад-вправо; влево — зеркало). Нарезка — scripts/slice-hero.py, листы — scripts/hero-anims.py.
    /// Выбор листа и кадра по состоянию: стоит / идёт / бежит / присед / прицел / выстрел / перезарядка / удар / смерть,
    /// с оружием — по классу (пистолет, «длинное» — автомат, винтовка, дробовик, ПП).
    /// </summary>
    public static class HeroAnims
    {
        static readonly Dictionary<string, (Texture2D[] f, Texture2D[] b)> Cache = new();

        /// <summary>Кадры анимации; нет — null (тогда берётся запасная).</summary>
        public static (Texture2D[] f, Texture2D[] b)? Get(string anim)
        {
            if (Cache.TryGetValue(anim, out var c)) return c.f == null ? null : c;
            Texture2D[] Load(char row)
            {
                var list = new List<Texture2D>();
                for (int i = 0; i < 16; i++)
                {
                    var t = Art.Get($"chars/hero/{anim}_{row}{i}");
                    if (t == null) break;
                    list.Add(t);
                }
                return list.Count > 0 ? list.ToArray() : null;
            }
            var f = Load('f'); var b = Load('b');
            Cache[anim] = (f, b ?? f);
            return f == null ? null : Cache[anim];
        }

        public enum Gun { None, Pistol, Long, Melee }

        public static Gun ClassOf(string heldId)
        {
            if (string.IsNullOrEmpty(heldId)) return Gun.None;
            var def = Rastvordd.Sim.Items.Get(heldId);
            if (def?.Weapon == null) return heldId.StartsWith("tool_") || heldId.StartsWith("weapon_spear") ? Gun.Melee : Gun.None;
            if (def.Weapon.Melee) return Gun.Melee;
            foreach (var p in new[] { "nagant", "grach", "aps", "_pm", "makarov", "_tt", "pistol", "revolver", "flare" })
                if (heldId.Contains(p)) return Gun.Pistol;
            return Gun.Long;
        }

        /// <summary>Состояние персонажа для выбора кадра.</summary>
        public struct State
        {
            public bool Back, Moving, Dead, Crouch, Reloading;
            public float Speed, Phase, Aim, Kick, Swing01, Reload01, DeadT;
            public Gun Gun;
        }

        /// <summary>Кадр и рисуется ли оружие отдельно (false — оружие уже нарисовано в кадре).</summary>
        public static (Texture2D tex, bool showHeld) Pick(in State s)
        {
            string g = s.Gun == Gun.Pistol ? "pistol" : "rifle";
            bool armed = s.Gun is Gun.Pistol or Gun.Long;
            // кандидаты по порядку: первый нарисованный лист и берётся (нет кадров «присед с оружием» — обычный присед + ствол отдельно)
            (string anim, bool loop, float t, bool held)[] c;
            float cyc = s.Phase / Mathf.Tau;
            if (s.Dead) c = new[] { ("death", false, Mathf.Clamp(s.DeadT / 1.1f, 0, 0.999f), false) };
            else if (s.Swing01 > 0) c = new[] { (s.Gun == Gun.Melee ? "melee_tool" : "melee", false, s.Swing01, false) };
            else if (s.Reloading && armed) c = new[] { ("reload_" + g, false, s.Reload01, false), ("idle_" + g, true, cyc * 0.25f, false) };
            else if (s.Crouch && armed && (s.Aim > 0.3f || s.Kick > 0.05f)) c = new[] { ("crouch_aim_" + g, true, cyc * 0.25f, false), ("crouch_idle_" + g, true, cyc * 0.25f, false), ("aim_" + g, true, cyc * 0.25f, false) };   // прицел и выстрел из приседа
            else if (s.Crouch && armed) c = new[] { ((s.Moving ? "crouch_walk_" : "crouch_idle_") + g, true, s.Moving ? cyc : cyc * 0.25f, false), (s.Moving ? "crouch_walk" : "crouch_idle", true, cyc, true) };
            else if (s.Kick > 0.05f && armed) c = new[] { ("shoot_" + g, false, 1f - s.Kick, false) };
            else if (s.Aim > 0.3f && armed) c = new[] { ("aim_" + g, true, cyc * 0.25f, false) };   // к глазу — только по ПКМ
            else if (s.Crouch) c = new[] { (s.Moving ? "crouch_walk" : "crouch_idle", true, cyc, true) };
            else if (s.Moving && s.Gun == Gun.Long) c = new[] { ("walk_rifle", true, cyc, false), ("walk", true, cyc, true) };
            else if (s.Moving) c = new[] { (s.Speed > Rastvordd.Sim.World.WalkSpeed + 0.6f ? "run" : "walk", true, cyc, true), ("walk", true, cyc, true) };
            else if (armed) c = new[] { ("idle_" + g, true, cyc * 0.25f, false), ("idle", true, cyc * 0.25f, true) };   // стоит — ствол опущен
            else c = new[] { ("idle", true, cyc * 0.25f, true) };

            foreach (var (anim, loop, t, held) in c)
            {
                var set = Get(anim);
                if (set == null) continue;
                var frames = s.Back ? set.Value.b : set.Value.f;
                int n = frames.Length;
                int i = loop ? ((int)(t * n) % n + n) % n : Mathf.Clamp((int)(t * n), 0, n - 1);
                return (frames[i], held);
            }
            var any = Get("idle") ?? Get("walk");
            return any == null ? (null, true) : ((s.Back ? any.Value.b : any.Value.f)[0], true);
        }
    }
}
