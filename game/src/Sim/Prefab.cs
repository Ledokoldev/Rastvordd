using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>Спрайт-украшение РТ: рисуется клиентом основанием в точке Pos; столкновения — отдельными невидимыми стенами.</summary>
    public sealed class MapDecor
    {
        public int Id;
        public string Sprite;          // путь в game/art без .png, например "props/subst_transformer"
        public Vector2 Pos;
        public bool Flip;
        public bool Flat;              // декаль на земле (трещины, разметка, лужи): без тени, под всеми объектами
        public float Scale = 1, Rot;   // для декалей: масштаб и поворот (градусы)
        public bool Pivot;             // стрела крана: вращается вокруг левого края (точка опоры — Pos)
        public float Lift;             // поднята над землёй, м (стрела крана; на стене — плакат, на столе — мелочь)
        public bool Batch;             // обстановка (Dressing.cs): мелочь без хитбокса — клиент рисует пачками по рядам
    }

    /// <summary>Участок земли другой текстурой (гравий ОРУ, асфальт, кафель в корпусе) — под всеми объектами.</summary>
    public struct MapPatch { public string Tile; public Vector2 Min, Max; public Vector2[] Poly; public int Slab; public byte Road; }   // Road: дорога мира (RoadNet.cs) — 1 асфальт, 2 грунтовка, 3 обочина под асфальтом   // Slab: тропа из плит ПАГ (1 — вдоль x, 2 — вдоль y)   // Poly — неровный участок (иначе прямоугольник Min–Max)

    /// <summary>Крыша над комнатой: снаружи прячет, что внутри; клиент растворяет её, когда игрок внутри (D40).</summary>
    public struct MapRoof { public string Tile; public Vector2 Min, Max; public List<RoofItem> Items; }

    /// <summary>Что стоит на крыше (вентиляция, труба, антенна…) — только картинка, исчезает вместе с крышей.</summary>
    public struct RoofItem { public string Sprite; public Vector2 Pos; }

    /// <summary>Провод между двумя точками на высоте (ОРУ, ЛЭП) — только картинка.</summary>
    public struct MapWire { public Vector2 A, B; public float Ha, Hb, Sag; }

    public sealed partial class World
    {
        public readonly List<MapDecor> Decors = new();
        public readonly List<MapPatch> Patches = new();
        public readonly List<MapRoof> Roofs = new();
        public readonly List<MapWire> Wires = new();
        /// <summary>Режиссёр вайпа: фаза, тепловая карта, ивенты, слухи (Director.cs).</summary>
        public Director Director;
        /// <summary>«Держи E» ивентов режиссёра (закрытый ящик).</summary>
        public readonly List<Stash> DirectorStashes = new();

        /// <summary>Лут ивента режиссёра: больше и ценнее, к поздней фазе — взрывчатка (чтобы рейды случались).</summary>
        internal void FillEventCrate(LootBag bag, int phase)
        {
            bag.Items.Clear();
            bag.Search.Clear();
            bag.Unlocked = false;
            // D46: детали тира 2–3, армейские обвесы, редко элита (Сайга-12, АПС, Мосинка — только отсюда)
            bag.Items.Add(new ItemSlot("scrap", Rng.Next(20, 41)));
            bag.Items.Add(new ItemSlot(Economy.Tier2(Rng), Rng.Next(1, 3)));
            if (Rng.NextDouble() < 0.35) bag.Items.Add(new ItemSlot(Economy.High(Rng), 1));   // высокий лом — пока только ивенты (D49)
            bag.Items.Add(new ItemSlot("comp_gears", Rng.Next(1, 4)));
            bag.Items.Add(new ItemSlot("comp_spring", Rng.Next(1, 4)));
            bag.Items.Add(new ItemSlot(Weaponry.Tier2Part(Rng), 1));
            if (Rng.NextDouble() < 0.5) bag.Items.Add(new ItemSlot(Weaponry.Tier2Part(Rng), 1));
            bag.Items.Add(new ItemSlot(phase >= 1 ? "ammo_762" : "ammo_9mm", Rng.Next(20, 50)));
            if (Rng.NextDouble() < 0.4) bag.Items.Add(new ItemSlot("ammo_12g", Rng.Next(8, 20)));
            bag.Items.Add(new ItemSlot("bandage", Rng.Next(2, 5)));
            if (Rng.NextDouble() < 0.5) bag.Items.Add(new ItemSlot("comp_oil", Rng.Next(1, 3)));
            if (Rng.NextDouble() < 0.4) bag.Items.Add(new ItemSlot(Rng.Next(2) == 0 ? "comp_lens" : "comp_circuit", 1));
            if (Rng.NextDouble() < 0.5) bag.Items.Add(new ItemSlot("punchcard_1", 1));
            if (phase >= 1 && Rng.NextDouble() < 0.4) bag.Items.Add(new ItemSlot("punchcard_2", 1));
            if (Rng.NextDouble() < 0.35) bag.Items.Add(new ItemSlot(Pick(Weaponry.ArmyAtts), 1));
            else if (Rng.NextDouble() < 0.3) bag.Items.Add(new ItemSlot(Pick(Weaponry.FactoryAtts), 1));
            if (Rng.NextDouble() < 0.3) bag.Items.Add(Items.NewSlot(Pick(new[] { "weapon_sawnoff", "weapon_mp133", "weapon_grach", "weapon_svt40" })));
            TryTier3(bag, 0.85);
            TryElite(bag, phase, 0.15);   // элита: фаза ≥ 2, с 3 ч, не чаще раза в 3,5 ч на мир (D49)
            if (phase >= 1) bag.Items.Add(new ItemSlot("gunpowder", Rng.Next(40, 100)));
            if (phase >= 2 && Rng.NextDouble() < 0.6) bag.Items.Add(Items.NewSlot("expl_satchel"));
            if (phase >= 2 && Rng.NextDouble() < 0.25) bag.Items.Add(Items.NewSlot("expl_c4"));
            Clothing.Loot(WearRng, bag, "event");   // тир 4 (СОБР, Ликвидатор), армейская и керамическая пластины — только отсюда (D47)
        }
        /// <summary>Готовые РТ (собранные из спрайтов): площадка не заливается бетоном, лес и узлы внутри убираются.</summary>
        public readonly List<Monument> Prefabs = new();

        /// <summary>Хитбоксы пропсов РТ: ширина, глубина следа и высота укрытия (≥ 2 м — закрывает обзор). Legs — твёрдые только опоры по краям.</summary>
        internal static readonly Dictionary<string, (float w, float d, float h, bool legs)> Foot = new()   // internal — размеры пропсов берёт 3D-клиент (Client3D/Decor3D.cs)
        {
            ["subst_transformer"] = (6.6f, 3.4f, 3.5f, false),
            ["subst_portal"] = (10.2f, 1.0f, 1.1f, true),
            ["subst_breaker"] = (4.0f, 1.6f, 2.5f, false),
            ["subst_disconnector"] = (3.0f, 0.7f, 0.7f, true),
            ["subst_pylon_fallen"] = (8.0f, 2.2f, 1.0f, false),
            ["subst_cable_drum"] = (2.2f, 1.0f, 2.2f, false),
            ["subst_truck"] = (6.2f, 2.6f, 2.8f, false),
            ["subst_kpp"] = (3.4f, 3.0f, 3.0f, false),
            ["subst_main_panel"] = (1.4f, 0.5f, 2.0f, false),
            ["subst_main_panel_on"] = (1.4f, 0.5f, 2.0f, false),
            ["subst_switchgear"] = (3.4f, 0.8f, 2.2f, false),
            ["subst_ktp"] = (3.4f, 2.6f, 3.0f, false),
            ["subst_oil_tank"] = (5.0f, 2.2f, 2.6f, false),
            ["subst_lamp"] = (0.5f, 0.5f, 8f, false),
            ["subst_relay_cabinet"] = (1.2f, 0.6f, 1.8f, false),
            ["subst_insulator_pallet"] = (1.2f, 1.0f, 1.0f, false),
            ["subst_reel_stack"] = (2.4f, 1.0f, 1.2f, false),
            ["int_desk"] = (1.4f, 0.7f, 0.8f, false),
            ["int_lockers"] = (1.4f, 0.5f, 1.9f, false),
            ["int_shelf"] = (1.3f, 0.5f, 2.0f, false),
            ["int_table"] = (1.2f, 0.8f, 0.8f, false),
            ["int_burzhuika"] = (0.7f, 0.7f, 1.0f, false),
            ["int_cot"] = (0.8f, 1.9f, 0.5f, false),
            ["car_wreck"] = (3.3f, 1.5f, 1.5f, false),
            ["concrete_barrier"] = (1.2f, 0.5f, 0.9f, false),
            ["barrel_oil"] = (0.55f, 0.5f, 1.0f, false),
            ["crate_wood"] = (0.75f, 0.65f, 0.8f, false),
            ["junk_pile"] = (0.9f, 0.6f, 0.8f, false),
            ["workbench"] = (1.2f, 0.6f, 0.9f, false),
        };

        // ---------- конструктор РТ: координаты — от угла РТ (o), в метрах, север — меньший y ----------

        MapDecor Decor(string sprite, Vector2 p, bool flip = false)
        {
            var d = new MapDecor { Id = Decors.Count, Sprite = sprite, Pos = p, Flip = flip };
            Decors.Add(d);
            return d;
        }

        /// <summary>Пропс со спрайтом и хитбоксом из таблицы Foot (без записи — только картинка).</summary>
        MapDecor Prop(string sprite, Vector2 p, bool flip = false)
        {
            var d = Decor(sprite, p, flip);
            string id = sprite[(sprite.LastIndexOf('/') + 1)..];
            if (!Foot.TryGetValue(id, out var f) && !Tier0Layouts.Foot.TryGetValue(id, out f) && !Tier1Layouts.Foot.TryGetValue(id, out f) && !Tier2Layouts.Foot.TryGetValue(id, out f)) return d;
            if (f.legs)
            {
                AddHiddenWall(p.X - f.w / 2, p.Y - f.d, p.X - f.w / 2 + f.d, p.Y, f.h);
                AddHiddenWall(p.X + f.w / 2 - f.d, p.Y - f.d, p.X + f.w / 2, p.Y, f.h);
            }
            else AddHiddenWall(p.X - f.w / 2, p.Y - f.d, p.X + f.w / 2, p.Y, f.h);
            return d;
        }

        void AddHiddenWall(float x0, float y0, float x1, float y1, float h)
        {
            AddWall(x0, y0, x1, y1, h, WallKind.Concrete);
            Walls[^1].Hidden = true;
        }

        /// <summary>Стена вдоль отрезка (горизонтального или вертикального) толщиной t с текстурой лица tex.</summary>
        MapWall WallT(Vector2 a, Vector2 b, float h, string tex, float t = 0.3f)
        {
            float x0 = MathF.Min(a.X, b.X), x1 = MathF.Max(a.X, b.X), y0 = MathF.Min(a.Y, b.Y), y1 = MathF.Max(a.Y, b.Y);
            if (x0 == x1) { x0 -= t / 2; x1 += t / 2; }
            if (y0 == y1) { y0 -= t / 2; y1 += t / 2; }
            AddWall(x0, y0, x1, y1, h, WallKind.Concrete);
            var w = Walls[^1];
            w.Tex = tex;
            return w;
        }

        void Patch(string tile, Vector2 min, Vector2 max) => Patches.Add(new MapPatch { Tile = tile, Min = min, Max = max });
        static readonly string[] RoofSprites =
        {
            "props/roof_vent_box", "props/roof_vent_mushroom", "props/roof_chimney_brick", "props/roof_exhaust_pipe", "props/roof_hatch",
            "props/roof_ac_unit", "props/roof_antenna", "props/roof_panels", "props/roof_junk", "props/roof_water_tank",
        };

        /// <summary>Крыша с «начинкой»: 1 предмет на ~25 м², случайно, не вплотную друг к другу и к краю.</summary>
        void Roof(string tile, Vector2 min, Vector2 max, Random rng)
        {
            var items = new List<RoofItem>();
            var size = max - min;
            int n = Math.Clamp((int)(size.X * size.Y / 25f), 1, 9);
            for (int tries = 0; tries < n * 12 && items.Count < n; tries++)
            {
                var p = min + new Vector2(1.2f + (float)rng.NextDouble() * (size.X - 2.4f), 1.6f + (float)rng.NextDouble() * (size.Y - 2.4f));
                if (items.Exists(it => Vector2.Distance(it.Pos, p) < 2.6f)) continue;
                items.Add(new RoofItem { Sprite = RoofSprites[rng.Next(RoofSprites.Length)], Pos = p });
            }
            Roofs.Add(new MapRoof { Tile = tile, Min = min, Max = max, Items = items });
        }
        void Wire(Vector2 a, float ha, Vector2 b, float hb, float sag = 0.5f) => Wires.Add(new MapWire { A = a, B = b, Ha = ha, Hb = hb, Sag = sag });

        /// <summary>
        /// Место под готовую РТ размером size: суша без пляжа, нужный биом, далеко от других РТ и от спавнов.
        /// Свой генератор случайных чисел — чтобы новая РТ не сдвигала остальную раскладку мира (и старые сохранения).
        /// </summary>
        Vector2? FindPrefabSpot(Random rng, Vector2 size, Biome[] biomes, float minR, float maxR, float gap = 110)
        {
            for (int tries = 0; tries < 1500; tries++)
            {
                float ang = (float)rng.NextDouble() * MathF.Tau, r = minR + (float)rng.NextDouble() * (maxR - minR);
                var o = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r - size * 0.5f;
                if (Array.IndexOf(biomes, Terrain.BiomeAt(o + size * 0.5f)) < 0) continue;
                bool ok = true;
                for (float x = -8; x <= size.X + 8 && ok; x += 4)
                    for (float y = -8; y <= size.Y + 8 && ok; y += 4)
                    {
                        var p = o + new Vector2(x, y);
                        ok = Terrain.IsLand(p) && Terrain.BiomeAt(p) != Biome.Beach;
                    }
                var c = o + size * 0.5f;
                foreach (var m in Monuments) ok &= Vector2.Distance(m.Center, c) > gap + (m.Max - m.Min).Length() * 0.5f + size.Length() * 0.5f;
                if (ok) return o;
            }
            return null;
        }

        /// <summary>Расчистка площадки РТ: деревья, камни, кусты, хлам и случайные укрытия, попавшие внутрь, убираются.</summary>
        void ClearPrefabArea(Monument m)
        {
            for (int i = Nodes.Count - 1; i >= 0; i--)
                if (m.Contains(Nodes[i].Pos, 3)) { _nodeGrid.Remove(Nodes[i]); Nodes.RemoveAt(i); }
            for (int i = Walls.Count - 1; i >= 0; i--)
            {
                var w = Walls[i];
                if (w.Owner != null || w.Prefab || !m.Contains((w.Wall.Min + w.Wall.Max) * 0.5f, 2)) continue;
                _wallGrid.Remove(w);
                Walls.RemoveAt(i);
            }
            Pickups.RemoveAll(p => m.Contains(p.Pos, 2) && p.Kind != PickupKind.Fuse);
        }

        /// <summary>После загрузки сохранения: узлы из сохранения на площадке РТ убрать, крейты РТ найти или вернуть.</summary>
        void FixupPrefabsAfterLoad()
        {
            foreach (var m in Prefabs)
                for (int i = Nodes.Count - 1; i >= 0; i--)
                    if (m.Contains(Nodes[i].Pos, 3)) { _nodeGrid.Remove(Nodes[i]); Nodes.RemoveAt(i); }
            foreach (var s in Substations) { s.Relink(this); ResetSubstation(s); }   // состояние РТ не сохраняется: после загрузки — свежая
            foreach (var t in Tier0) ResetTier0(t);
            foreach (var t in Tier1)
            {
                // крейт тира 1 в сохранении — обычная сумка без признака «под перфокарту»: убрать копии, вернуть свой
                Bags.RemoveAll(b => b != t.Crate && b.IsCrate && (Vector2.Distance(b.Pos, t.CratePos) < 0.6f || Vector2.Distance(b.Pos, t.WagonStart) < 0.6f
                    || Vector2.Distance(b.Pos, t.ContStart) < 0.6f || Vector2.Distance(b.Pos, t.ContEnd) < 0.6f || t.Capsules.Exists(c => Vector2.Distance(b.Pos, c) < 1.5f)));
                ResetTier1(t);
            }
            foreach (var t in Tier2) ResetTier2(t);
            ClearRoadsAfterLoad();   // узлы из сохранения на дорогах (RoadNet.cs)
            ClearLandmarksAfterLoad();   // деревья из сохранения на площадках ориентиров
        }
    }
}
