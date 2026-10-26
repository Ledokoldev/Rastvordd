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
            string anim; float t; bool loop = true, held = true;
            string g = s.Gun == Gun.Pistol ? "pistol" : "rifle";
            bool armed = s.Gun is Gun.Pistol or Gun.Long;
            if (s.Dead) { anim = "death"; t = Mathf.Clamp(s.DeadT / 1.1f, 0, 0.999f); loop = false; held = false; }
            else if (s.Swing01 > 0) { anim = s.Gun == Gun.Melee ? "melee_tool" : "melee"; t = s.Swing01; loop = false; held = false; }
            else if (s.Reloading && armed) { anim = "reload_" + g; t = s.Reload01; loop = false; held = false; }
            else if (s.Kick > 0.05f && armed) { anim = "shoot_" + g; t = 1f - s.Kick; loop = false; held = false; }
            else if (s.Aim > 0.3f && armed) { anim = "aim_" + g; t = s.Phase / Mathf.Tau; held = false; }
            else if (s.Crouch) { anim = s.Moving ? "crouch_walk" : "crouch_idle"; t = s.Phase / Mathf.Tau; }
            else if (s.Moving && s.Gun == Gun.Long) { anim = "walk_rifle"; t = s.Phase / Mathf.Tau; held = false; }
            else if (s.Moving) { anim = s.Speed > Rastvordd.Sim.World.WalkSpeed + 0.6f ? "run" : "walk"; t = s.Phase / Mathf.Tau; }
            else if (s.Gun == Gun.Long) { anim = "aim_rifle"; t = 0; held = false; }      // стоя с автоматом — в готовности
            else { anim = "idle"; t = s.Phase / Mathf.Tau * 0.25f; }

            var set = Get(anim) ?? Get(s.Moving ? "walk" : "idle") ?? Get("walk");
            if (set == null) return (null, true);
            var frames = s.Back ? set.Value.b : set.Value.f;
            int n = frames.Length;
            int i = loop ? ((int)(t * n) % n + n) % n : Mathf.Clamp((int)(t * n), 0, n - 1);
            return (frames[i], held);
        }
    }
}
