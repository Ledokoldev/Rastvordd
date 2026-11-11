using System;
using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Как одежда видна на риге (docs/design/14, «Как одежда видна в игре»). Ткань — МАТЕРИАЛ: узор + цвет, светотень берётся
    /// с голой части (яркость пикселя относительно средней), поэтому 50 вещей не требуют 400 спрайтов. Жёсткие вещи (шлемы,
    /// противогазы, жилеты, наколенники, налокотники, часы) — накладки: если художник положил art/rig/rig_wear_&lt;id&gt;_&lt;f|b&gt;.png
    /// (размер как у rig_naked_head_f/torso_f: голова 10×12, торс 16×19; для конечностей — rig_wear_&lt;id&gt;_&lt;часть&gt;.png),
    /// берётся файл, иначе процедурная заглушка тем же пикселем. Порядок слоёв: обувь → штаны → наколенники → кофта →
    /// перчатки → руки → броня → лицо → шлем → часы. Всё собирается один раз на комплект в текстуры частей (кэш).
    /// </summary>
    public static class WearLook
    {
        public enum Pat { Plain, Weave, Quilt, Noise, Planks, Stripe, Gloss, Fur, Camo, Sign }

        /// <summary>Материал: основной и второй цвет, узор.</summary>
        public readonly struct Mat
        {
            public readonly Color A, B; public readonly Pat P;
            public Mat(uint a, uint b, Pat p) { A = UiTheme.Hex(a); B = UiTheme.Hex(b); P = p; }
        }

        // ---------- материалы по вещам ----------
        static readonly Mat Burlap = new(0x8a7550, 0x7b6846, Pat.Weave);
        static readonly Mat Quilt = new(0x3b4048, 0x2a2e34, Pat.Quilt);
        static readonly Mat Felt = new(0x8c8580, 0x77706b, Pat.Noise);
        static readonly Mat FurM = new(0x6b4f36, 0x4e3826, Pat.Fur);
        static readonly Mat Leather = new(0x6e4a2c, 0x593a22, Pat.Noise);
        static readonly Mat Wood = new(0x8a6a40, 0x5e4528, Pat.Planks);
        static readonly Mat Robe = new(0x4a5866, 0x3c4855, Pat.Plain);
        static readonly Mat RubberGrey = new(0x3f4a40, 0x323b33, Pat.Gloss);
        static readonly Mat RubberBlack = new(0x2b2b2b, 0x1e1e1e, Pat.Gloss);
        static readonly Mat Ozk = new(0x56663e, 0x45532f, Pat.Gloss);
        static readonly Mat Khaki = new(0x5f5d40, 0x4c4a32, Pat.Camo);
        static readonly Mat KhakiPlain = new(0x6a6845, 0x5a5839, Pat.Noise);
        static readonly Mat Boots = new(0x232120, 0x161514, Pat.Gloss);
        static readonly Mat Kirza = new(0x3a2e26, 0x2a211b, Pat.Noise);
        static readonly Mat L1 = new(0xc8c8bc, 0xaeaea2, Pat.Quilt);
        static readonly Mat Tact = new(0x24272c, 0x1a1c20, Pat.Noise);
        static readonly Mat Metal = new(0x8a9096, 0x5c6166, Pat.Gloss);
        static readonly Mat ArmyGreen = new(0x4d5c35, 0x3a4627, Pat.Gloss);
        static readonly Mat Black = new(0x222326, 0x17181a, Pat.Noise);
        static readonly Mat Bandana = new(0x8b3a2f, 0x6e2c23, Pat.Plain);
        static readonly Mat Scarf = new(0x9b6b3a, 0x7a4f28, Pat.Stripe);
        static readonly Mat White = new(0xd6d6cc, 0xbdbdb2, Pat.Gloss);
        static readonly Mat Lead = new(0x6e7378, 0x575b60, Pat.Plain);
        static readonly Mat SignVest = new(0x2f5fa8, 0xe6e6e6, Pat.Sign);

        /// <summary>Мягкая вещь: материал. Жёсткая — материал заглушки-накладки (файл rig_wear_* важнее).</summary>
        /// <summary>Основной цвет вещи — для 3D-клиента (Client3D/Ents3D.cs: одежда слоями поверх персонажа).</summary>
        internal static Color? Tint3D(string id) => MatOf(id) is Mat m ? m.A : null;

        static Mat? MatOf(string id) => id switch
        {
            "wear_rags_shirt" or "wear_rags_pants" or "wear_rags_wraps" or "wear_rags_mittens" => Burlap,
            "wear_rags_bandana" => Bandana,
            "wear_vatnik_telogreika" or "wear_vatnik_pants" => Quilt,
            "wear_vatnik_valenki" => Felt,
            "wear_vatnik_mittens" => new Mat(0x5a4a3a, 0x4a3c2e, Pat.Noise),
            "wear_vatnik_ushanka" => FurM,
            "wear_vatnik_scarf" => Scarf,
            "wear_leather_helmet" or "wear_leather_bracers" or "wear_leather_pants" => Leather,
            "wear_leather_chestplate" or "wear_leather_shinguards" => Wood,
            "wear_spec_robe" => Robe,
            "wear_spec_hood" or "wear_spec_pants" => RubberGrey,
            "wear_spec_boots" or "wear_spec_gloves" or "wear_scrap_kneepads" => RubberBlack,
            "wear_spec_r2" => new Mat(0xb7b7ad, 0x55574f, Pat.Plain),
            "wear_scrap_pot" or "wear_scrap_bracers" => Metal,
            "wear_scrap_balaclava" or "wear_sobr_nomex" => Black,
            "wear_scrap_signvest" => SignVest,
            "wear_scrap_kirza" => Kirza,
            "wear_ozk_hood" or "wear_ozk_cape" or "wear_ozk_gloves" or "wear_ozk_stockings" => Ozk,
            "wear_ozk_gp5" => new Mat(0x5d6650, 0x2c3027, Pat.Gloss),
            "wear_army_bushlat" => Khaki,
            "wear_army_pants" => KhakiPlain,
            "wear_army_ssh68" or "wear_army_6b2" or "wear_army_elbowpads" or "wear_army_kneepads" => ArmyGreen,
            "wear_army_bertsy" or "wear_sobr_boots" => Boots,
            "wear_army_gloves" => new Mat(0x33352a, 0x26281f, Pat.Noise),
            "wear_sobr_sfera" => new Mat(0x3a3f35, 0x26291f, Pat.Gloss),
            "wear_sobr_kora" => new Mat(0x3a4032, 0x2b3025, Pat.Noise),   // тёмная олива поверх чёрного костюма — жилет читается
            "wear_sobr_elbowpads" or "wear_sobr_kneepads" or "wear_sobr_gloves" => Black,
            "wear_sobr_suit" or "wear_sobr_pants" => Tact,
            _ => null,
        };

        // ---------- узоры ----------
        static uint Hash(int x, int y, int s) { unchecked { uint h = (uint)(x * 73856093 ^ y * 19349663 ^ s * 83492791); h ^= h >> 13; h *= 0x5bd1e995; return h ^ (h >> 15); } }

        static Color PatternAt(Mat m, int x, int y, int seed)
        {
            switch (m.P)
            {
                case Pat.Weave: return ((x + y) & 1) == 0 ? m.A : m.B;
                case Pat.Quilt: return y % 3 == 2 ? m.B : m.A;                 // стёжка ватника
                case Pat.Noise: return Hash(x, y, seed) % 5 == 0 ? m.B : m.A;
                case Pat.Planks: return x % 3 == 2 ? m.B : m.A;                // доски
                case Pat.Stripe: return (y / 2) % 2 == 0 ? m.A : m.B;
                case Pat.Gloss: return m.A;
                case Pat.Fur: return Hash(x, y, seed) % 3 == 0 ? m.B : m.A;
                case Pat.Camo: return Hash(x / 2, y / 2, seed) % 3 == 0 ? m.B : m.A;
                case Pat.Sign: return (y / 2) % 2 == 0 ? m.A : m.B;            // полосы дорожного знака
                default: return m.A;
            }
        }

        // ---------- сборка рига ----------
        static readonly Dictionary<string, RigSet> Cache = new();
        static readonly string[] Parts = { "head_f", "head_b", "torso_f", "torso_b", "pelvis", "upperarm", "forearm", "thigh", "shin" };

        /// <summary>Набор частей в одежде (кэш по комплекту). worn == null — голый базовый набор.</summary>
        public static RigSet Dressed(string baseName, string[] worn)
        {
            if (worn == null || Array.TrueForAll(worn, w => w == null)) return RigSet.Load(baseName);
            string key = baseName + "|" + string.Join(",", worn);
            if (Cache.TryGetValue(key, out var hit)) return hit;
            var bare = RigSet.Load(baseName);
            if (bare == null) return null;
            var tex = new Dictionary<string, Texture2D>();
            foreach (var p in Parts) tex[p] = Compose(p, bare.Part(p), worn);
            var set = new RigSet
            {
                HeadF = tex["head_f"], HeadB = tex["head_b"], TorsoF = tex["torso_f"], TorsoB = tex["torso_b"], Pelvis = tex["pelvis"],
                UpperArm = tex["upperarm"], ForeArm = tex["forearm"], Thigh = tex["thigh"], Shin = tex["shin"],
            };
            if (Cache.Count > 64) Cache.Clear();
            Cache[key] = set;
            return set;
        }

        static Image ImageOf(Texture2D t)
        {
            var img = t.GetImage();
            if (img.IsCompressed()) img.Decompress();
            img.Convert(Image.Format.Rgba8);
            return img;
        }

        static float Lum(Color c) => c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;

        /// <summary>Одна часть: голая текстура → слои одежды по порядку.</summary>
        static Texture2D Compose(string part, Texture2D bare, string[] worn)
        {
            var src = ImageOf(bare);
            var img = (Image)src.Duplicate();
            int w = img.GetWidth(), h = img.GetHeight();
            float sum = 0; int n = 0;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { var c = src.GetPixel(x, y); if (c.A > 0.5f) { sum += Lum(c); n++; } }
            float mean = n > 0 ? Math.Max(0.05f, sum / n) : 0.5f;

            // заливка ткани по маске: светотень с голой части
            void Fabric(Mat m, Func<int, int, bool> mask, int seed)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var c = src.GetPixel(x, y);
                        if (c.A < 0.5f || !mask(x, y)) continue;
                        float l = Lum(c) / mean;
                        var col = PatternAt(m, x, y, seed);
                        col = l < 0.45f ? col.Darkened(0.6f) : col * Mathf.Clamp(0.55f + 0.45f * l, 0.6f, 1.25f);
                        if (m.P == Pat.Gloss && y == 0 + (h > 6 ? 1 : 0) && x % 3 == 1) col = col.Lightened(0.25f);   // блик резины/металла
                        img.SetPixel(x, y, new Color(col, 1));
                    }
            }
            // жёсткая накладка: сначала файл художника, иначе заливка материалом (обвод — темнее)
            void Hard(string id, Mat m, Func<int, int, bool> mask, string side)
            {
                var file = Art.Get($"rig/rig_wear_{id}_{side}") ?? Art.Get($"rig/rig_wear_{id}_{part}");
                if (file != null && file.GetWidth() == w && file.GetHeight() == h) { img.BlendRect(ImageOf(file), new Rect2I(0, 0, w, h), Vector2I.Zero); return; }
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (!mask(x, y)) continue;
                        bool edge = !mask(x - 1, y) || !mask(x + 1, y) || !mask(x, y - 1) || !mask(x, y + 1);
                        var col = PatternAt(m, x, y, 7);
                        if (edge) col = col.Darkened(0.35f);
                        else if (m.P == Pat.Gloss && y <= 1 + (h / 6)) col = col.Lightened(0.18f);
                        img.SetPixel(x, y, new Color(col, 1));
                    }
            }
            bool Op(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && src.GetPixel(x, y).A > 0.5f;
            // ряд пикселей части: первый и последний непрозрачный (для «вставки» жилета внутрь силуэта)
            (int a, int b) RowSpan(int y) { int a = -1, b = -1; for (int x = 0; x < w; x++) if (Op(x, y)) { if (a < 0) a = x; b = x; } return (a, b); }

            string W(WearSlot s) => worn.Length > (int)s ? worn[(int)s] : null;
            bool front = part.EndsWith("_f"), back = part.EndsWith("_b");
            string side = front ? "f" : back ? "b" : part;

            // 1. обувь: низ голени и стопа
            if (part == "shin" && W(WearSlot.Feet) is string feet && MatOf(feet) is Mat fm)
                Fabric(fm, (x, y) => y >= (feet is "wear_rags_wraps" or "wear_ozk_stockings" ? h * 0.45f : h * 0.6f), 1);
            // 2. штаны: таз, бедро, голень (над обувью)
            if (W(WearSlot.Legs) is string legs && MatOf(legs) is Mat lm)
            {
                bool shod = W(WearSlot.Feet) != null;
                if (part is "pelvis" or "thigh") Fabric(lm, (x, y) => true, 2);
                if (part == "shin") Fabric(lm, (x, y) => y < (shod ? (W(WearSlot.Feet) is "wear_rags_wraps" or "wear_ozk_stockings" ? h * 0.45f : h * 0.6f) : h * 0.8f), 2);
            }
            // 3. наколенники: верх голени (колено)
            if (part == "shin" && W(WearSlot.Knees) is string knee && MatOf(knee) is Mat km)
                Hard(knee, km, (x, y) => Op(x, y) && y <= 3, side);
            // 4. кофта: торс, плечо, предплечье (кисть — голая или перчатка)
            string top = W(WearSlot.Kofta);
            if (W(WearSlot.Armor) is "wear_ozk_cape") top = "wear_ozk_cape";   // плащ ОЗК — тоже ткань поверх всего
            if (top != null && MatOf(top) is Mat tm)
            {
                if (part is "torso_f" or "torso_b" or "upperarm") Fabric(tm, (x, y) => true, 3);
                if (part == "forearm") Fabric(tm, (x, y) => x < w - 3, 3);
                if (top == "wear_ozk_cape" && part == "pelvis") Fabric(tm, (x, y) => y < h * 0.6f, 3);   // плащ ниже пояса
            }
            // 5. перчатки: кисть (конец предплечья)
            if (part == "forearm" && W(WearSlot.Gloves) is string gl && MatOf(gl) is Mat gm) Fabric(gm, (x, y) => x >= w - 3, 4);
            // 6. руки: налокотники/наручи — локоть (конец плеча) и предплечье
            if (W(WearSlot.Arms) is string arm && MatOf(arm) is Mat am)
            {
                if (part == "upperarm" && arm.EndsWith("elbowpads")) Hard(arm, am, (x, y) => Op(x, y) && x >= w - 3, side);
                if (part == "forearm" && !arm.EndsWith("elbowpads")) Hard(arm, am, (x, y) => Op(x, y) && x >= 2 && x < w - 3, side);
            }
            // 7. броня: жилет/нагрудник/фартук — на торсе спереди и сзади (фартук — только спереди)
            if (part is "torso_f" or "torso_b" && W(WearSlot.Armor) is string ar && ar != "wear_ozk_cape" && MatOf(ar) is Mat arm2)
            {
                bool apron = ar == "wear_liquidator_apron";
                if (!(apron && back))
                    Hard(ar, arm2, (x, y) =>
                    {
                        if (y < h * 0.16f || y > h * (apron ? 0.95f : 0.82f) || !Op(x, y)) return false;
                        var (a, b) = RowSpan(y);
                        return x > a && x < b;   // внутри силуэта — по бокам видна кофта
                    }, side);
            }
            // 8. лицо: маска/шарф/балаклава/противогаз — на голове спереди; сзади — ремешок/ткань
            if (part is "head_f" or "head_b" && W(WearSlot.Face) is string face && MatOf(face) is Mat fcm)
            {
                switch (face)
                {
                    case "wear_scrap_balaclava" or "wear_sobr_nomex":
                        Fabric(fcm, (x, y) => !(front && y >= h * 0.45f && y <= h * 0.55f && x > 1 && x < w - 2), 5);   // прорезь для глаз
                        break;
                    case "wear_ozk_gp5":
                        if (front) Hard(face, fcm, (x, y) => Op(x, y) && y >= h * 0.4f, side);
                        else Fabric(fcm, (x, y) => y >= h * 0.35f && y <= h * 0.45f, 5);
                        if (front && face == "wear_ozk_gp5") { int cx = w / 2; for (int dy = 0; dy < 2; dy++) for (int dx = -1; dx <= 0; dx++) Put(img, cx + dx, h - 2 + dy - 1, new Color(0.18f, 0.2f, 0.16f)); }   // фильтр
                        if (front) { Put(img, w / 2 - 2, (int)(h * 0.5f), new Color(0.75f, 0.85f, 0.85f)); Put(img, w / 2 + 1, (int)(h * 0.5f), new Color(0.75f, 0.85f, 0.85f)); }   // стёкла
                        break;
                    case "wear_spec_r2":
                        if (front) Hard(face, fcm, (x, y) => Op(x, y) && y >= h * 0.62f && y <= h * 0.85f && x >= w / 2 - 2 && x <= w / 2 + 1, side);
                        break;
                    default:   // бандана, шарф — низ лица и шея
                        if (front) Fabric(fcm, (x, y) => y >= h * 0.62f, 5);
                        else Fabric(fcm, (x, y) => y >= h * 0.75f, 5);
                        break;
                }
            }
            // 9. голова: шапки и капюшоны — ткань по верху (ушанка и капюшон — с боков ниже), шлемы — накладка
            if (part is "head_f" or "head_b" && W(WearSlot.Head) is string hd && MatOf(hd) is Mat hm)
            {
                bool hood = hd is "wear_spec_hood" or "wear_ozk_hood" or "wear_vatnik_ushanka";
                bool soft = hood || hd == "wear_leather_helmet";
                Func<int, int, bool> cap = (x, y) =>
                {
                    if (!Op(x, y)) return false;
                    if (y < h * 0.42f) return true;
                    if (!hood) return false;
                    var (a, b) = RowSpan(y);
                    return back ? y < h * 0.8f : y < h * 0.75f && (x <= a + 1 || x >= b - 1);   // уши/края капюшона по бокам лица
                };
                if (soft) Fabric(hm, cap, 6);
                else Hard(hd, hm, (x, y) => Op(x, y) && y < h * 0.45f, side);
                if (front && hd == "wear_liquidator_helmet") for (int x = 2; x < w - 2; x++) Put(img, x, (int)(h * 0.45f), new Color(0.6f, 0.78f, 0.86f, 1));   // забрало
            }
            // 10. часы: тёмный ремешок и светлый циферблат у кисти
            if (part == "forearm" && W(WearSlot.Wrist) == "wear_watch")
            {
                int x = w - 4;
                for (int y = 0; y < h; y++) if (Op(x, y)) Put(img, x, y, new Color(0.12f, 0.12f, 0.13f));
                if (Op(x, h / 2)) Put(img, x, h / 2, new Color(0.85f, 0.85f, 0.7f));
            }
            return ImageTexture.CreateFromImage(img);
        }

        static void Put(Image img, int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) return;
            img.SetPixel(x, y, c);
        }
    }
}
