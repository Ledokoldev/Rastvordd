using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Самопроверка этажей (D60, отладка: --simtest=N --floortest): настоящей стройкой Плана — дом в два этажа,
    /// перекрытия и консоли (устойчивость), лестница (подъём и спуск шагами), падение с края, люк, стрельба вниз и вверх,
    /// пол как укрытие, обзор между этажами, обрушение без опоры, взрыв не бьёт через перекрытие, полёт (задел «воздух»).
    /// Печатает «ЭТАЖ-ТЕСТ … OK/ПРОВАЛ». Сейв не трогает.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>Свободный прямоугольник клеток под фундамент рядом с точкой (w×h клеток).</summary>
        public (int i, int j) FindBuildSpot(Vector2 near, int w, int h)
        {
            var (c0, r0) = Building.CellOf(near);
            for (int rad = 0; rad < 60; rad++)
                for (int di = -rad; di <= rad; di++)
                    for (int dj = -rad; dj <= rad; dj++)
                    {
                        if (Math.Max(Math.Abs(di), Math.Abs(dj)) != rad) continue;
                        bool ok = true;
                        for (int x = -1; x <= w && ok; x++)
                            for (int y = -1; y <= h && ok; y++)
                                ok = FoundationGeoOk(c0 + di + x, r0 + dj + y, out _) && ClaimAt((c0 + di + x, r0 + dj + y)) == null;
                        if (ok) return (c0 + di, r0 + dj);
                    }
            return (c0, r0);
        }

        /// <summary>
        /// Дом в два этажа Планом (как строит игрок): фундамент w×h, стены (на юге — дверной проём), перекрытие,
        /// лестница в углу (i0, j0+h-1) — поднимается на восток, стены второго этажа с окнами и парапет-крыша не ставится.
        /// Возвращает строителя (убрать после) — у него ресурсы и План.
        /// </summary>
        public readonly List<string> BuildFails = new();
        public Actor BuildDemoHouse(int i0, int j0, int w, int h, Actor b, bool windows = true)
        {
            b.Belt[0] = Items.NewSlot("build_plan"); b.Current = 0;
            b.Add("wood", 9000);
            void Put(StructKind k, Vector2 at, int floor)
            {
                b.Floor = floor; b.Z = Building.FloorZ(floor);
                b.BuildPiece = k;
                b.Pos = at + new Vector2(0.9f, 0.9f);   // не на месте стены (иначе «мешает человек»)
                if (k == StructKind.Stairs) b.Pos = at - new Vector2(1.2f, 0);   // ставит, стоя к западу: подъём на восток
                int n0 = Structures.Count;
                using (Ctx(floor)) TryBuild(b, at);
                if (Structures.Count == n0) BuildFails.Add($"{Building.KindName(k)} эт.{floor + 1}: {LastInfo.GetValueOrDefault(b.Id)}");
            }
            Vector2 C(int i, int j) => Building.CellCenter(i, j);
            for (int i = i0; i < i0 + w; i++) for (int j = j0; j < j0 + h; j++) Put(StructKind.Foundation, C(i, j), 0);
            for (int f = 0; f < 2; f++)
            {
                for (int i = i0; i < i0 + w; i++)
                {
                    Put(f == 0 && i == i0 + w - 1 ? StructKind.Doorway : f == 1 && windows && i == i0 + 1 ? StructKind.Window : StructKind.Wall, new Vector2((i + 0.5f) * Building.Cell, (j0 + h) * Building.Cell), f);   // юг
                    Put(f == 1 && windows && i == i0 + w - 2 ? StructKind.Window : StructKind.Wall, new Vector2((i + 0.5f) * Building.Cell, j0 * Building.Cell), f);   // север
                }
                for (int j = j0; j < j0 + h; j++)
                {
                    Put(StructKind.Wall, new Vector2(i0 * Building.Cell, (j + 0.5f) * Building.Cell), f);              // запад
                    Put(f == 1 && windows && j == j0 + 1 ? StructKind.Window : StructKind.Wall, new Vector2((i0 + w) * Building.Cell, (j + 0.5f) * Building.Cell), f);   // восток
                }
                if (f == 0)
                {
                    Put(StructKind.Stairs, C(i0 + 1, j0 + h - 1), 0);   // лестница — до перекрытия, у южной стены: вход с запада, выход на восток
                    for (int i = i0; i < i0 + w; i++) for (int j = j0; j < j0 + h; j++) Put(StructKind.Foundation, C(i, j), 0);   // на занятое — встаёт перекрытием
                }
            }
            foreach (var s in Structures) if (s.OwnerId == b.Id && s.Tier == 0) { s.Tier = 1; s.Hp = s.MaxHp; }   // дерево: пули не ломают
            StructVersion++;
            b.Floor = 0; b.Z = 0;
            return b;
        }

        public void FloorSelfTest(Action<string> log)
        {
            int fails = 0;
            void Check(string what, bool ok) { if (!ok) fails++; log($"ЭТАЖ-ТЕСТ {what}: {(ok ? "OK" : "ПРОВАЛ")}"); }
            Actor Mk(string name) { var t = new Actor { Id = NextId(), Name = name, Team = Team.Player }; Actors.Add(t); return t; }
            Vector2 C(int i, int j) => Building.CellCenter(i, j);

            var bld = Mk("Тест-строитель");
            var (i0, j0) = FindBuildSpot(PlayerSpawns[0] + new Vector2(0, -30), 7, 6);
            const int W = 3, H = 3;
            BuildDemoHouse(i0, j0, W, H, bld);
            foreach (var bf in BuildFails) log("ЭТАЖ-ТЕСТ отказ: " + bf);
            int up = 0; foreach (var s in Structures) if (s.Floor == 1) up++;
            Check($"дом построен Планом: элементов второго этажа {up}", up >= 8 + 12);
            var ceil = PlateAt(1, C(i0 + 1, j0));
            Check("перекрытие на стенах: устойчивость 90 %", ceil != null && MathF.Abs(StabilityOf(ceil) - 0.9f) < 0.01f);
            var stairs = StairsAt(C(i0 + 1, j0 + H - 1), 0);
            Check("лестница стоит (верх — 2-й этаж)", stairs != null && stairs.Floor == 1);

            // консоль: перекрытие за стену на 1 и 2 клетки держится, на 3 — нет
            bld.Floor = 1; bld.Z = Building.Storey; bld.BuildPiece = StructKind.Foundation;
            bool Ceil(int i, int j) { bld.Pos = C(i, j) - new Vector2(1.5f, 0); using (Ctx(1)) TryBuild(bld, C(i, j)); return PlateAt(1, C(i, j)) != null; }
            Check("консоль: 1 клетка от стены", Ceil(i0 + W, j0 + 1));
            Check("консоль: 2 клетки", Ceil(i0 + W + 1, j0 + 1));
            Check("консоль: 3 клетки — нет опоры", !Ceil(i0 + W + 2, j0 + 1));
            bld.Floor = 0; bld.Z = 0;

            // ---- лестница: подъём и спуск шагами ----
            var p = Mk("Тест-ходок");
            void Walk(Actor a, Vector2 dir, float sec)
            {
                for (int k = 0; k < (int)(sec / Dt); k++)
                {
                    var c = InputCmd.Empty; c.Move = dir; c.Aim = a.Pos + dir * 5;
                    Step(new Dictionary<int, InputCmd> { [a.Id] = c });
                }
            }
            p.Pos = C(i0 + 1, j0 + H - 1) - new Vector2(0.7f, 0);
            Walk(p, new Vector2(1, 0), 1.6f);
            Check($"подъём по лестнице: этаж {p.Floor + 1}, высота {p.Z:0.0} м", p.Floor == 1 && p.Z > 2.4f);
            Walk(p, new Vector2(0, -1), 1.2f);
            Check("наверху ходит по перекрытию", p.Floor == 1 && PlateAt(1, p.Pos) != null);
            p.Pos = C(i0 + 2, j0 + H - 1); p.Floor = 1; p.Z = Building.Storey;
            Walk(p, new Vector2(-1, 0), 2.0f);
            Check($"спуск по лестнице: этаж {p.Floor + 1}", p.Floor == 0 && p.Z < 0.3f);

            // ---- падение с края консоли ----
            p.Pos = C(i0 + W + 1, j0 + 1); p.Floor = 1; p.Z = Building.Storey; p.Hp = p.MaxHp;
            Walk(p, new Vector2(1, 0), 1.0f);
            Check($"падение с края: на земле, HP {p.Hp:0}", p.Floor == 0 && p.Hp < p.MaxHp && !p.Dead);

            // ---- стрельба между этажами ----
            var top = Mk("Тест-наверху"); var low = Mk("Тест-внизу");
            float Duel(Actor shooter, Actor target, Vector2 sPos, int sF, Vector2 tPos, int tF)
            {
                float dealt = 0;
                var wd = Items.Get("weapon_pps43").Weapon;
                shooter.Belt[0] = Items.NewSlot("weapon_pps43", wd.Magazine); shooter.Current = 0; shooter.Add(wd.AmmoId, 200);
                for (int k = 0; k < (int)(1.2f / Dt); k++)
                {
                    shooter.Pos = sPos; shooter.Floor = sF; shooter.Z = Building.FloorZ(sF);
                    target.Pos = tPos; target.Floor = tF; target.Z = Building.FloorZ(tF);
                    var c = InputCmd.Empty; c.Aim = target.Pos; c.Fire = true; c.Peek = 1;
                    float hp0 = target.Hp;
                    Step(new Dictionary<int, InputCmd> { [shooter.Id] = c, [target.Id] = InputCmd.Empty });
                    dealt += MathF.Max(0, hp0 - target.Hp);
                    target.Hp = target.MaxHp; target.Dead = false;
                }
                return dealt;
            }
            var edge = C(i0 + W + 1, j0 + 1);
            var ground = edge + new Vector2(9, 0);
            Check("с консоли вниз по человеку на земле — попадает", Duel(top, low, edge, 1, ground, 0) > 0);
            Check("с земли вверх по человеку на консоли — попадает", Duel(low, top, ground, 0, edge, 1) > 0);
            Check("сквозь перекрытие вниз (в доме под ногами) — нет", Duel(top, low, C(i0 + 1, j0 + 1), 1, C(i0 + 2, j0 + 1), 0) == 0);
            Check("с земли сквозь стену второго этажа — нет", Duel(low, top, C(i0 + 2, j0 - 5), 0, C(i0 + 2, j0 + 1), 1) == 0);
            top.Crouching = false;
            low.Facing = MathF.Atan2(edge.Y - ground.Y, edge.X - ground.X);
            top.Pos = edge; top.Floor = 1; top.Z = Building.Storey; low.Pos = ground; low.Floor = 0; low.Z = 0;
            Check("обзор: с земли видно человека на открытом краю", CanSeeActor(low, top, 50, 130));
            top.Pos = C(i0 + 1, j0);
            low.Facing = MathF.Atan2(top.Pos.Y - ground.Y, top.Pos.X - ground.X);
            Check("обзор: с земли не видно человека в комнате второго этажа", !CanSeeActor(low, top, 50, 130));

            // ---- люк ----
            bld.Floor = 1; bld.Z = Building.Storey; bld.BuildPiece = StructKind.Hatch; bld.Pos = C(i0 + W + 1, j0 + 1) - new Vector2(1.5f, 0);
            bld.BuildPiece = StructKind.Foundation;
            bool hatchOk;
            {
                // люк — на втором этаже, на клетке, где перекрытия нет: снимаем дальнюю консоль и ставим люк туда
                var cons = PlateAt(1, C(i0 + W + 1, j0 + 1));
                if (cons != null) RemoveStructure(cons, true);
                bld.Floor = 0; bld.Z = 0; bld.BuildPiece = StructKind.Hatch; bld.Pos = C(i0 + W + 1, j0 + 1) - new Vector2(1.2f, 0);
                TryBuild(bld, C(i0 + W + 1, j0 + 1));
                var hatch = PlateAt(1, C(i0 + W + 1, j0 + 1));
                hatchOk = hatch != null && hatch.Kind == StructKind.Hatch;
                Check("люк поставлен", hatchOk);
                if (hatchOk)
                {
                    p.Pos = hatch.Center; p.Floor = 0; p.Z = 0;
                    var (k, t) = NearestInteract(p);
                    if (k == InteractKind.Hatch) UseHatch(p, (Structure)t);
                    Check("люк: снизу — наверх", p.Floor == 1);
                    (k, t) = NearestInteract(p);
                    if (k == InteractKind.Hatch) UseHatch(p, (Structure)t);
                    Check("люк: сверху — вниз", p.Floor == 0);
                }
            }

            // ---- взрыв на втором этаже не бьёт человека внизу ----
            low.Pos = C(i0 + 1, j0 + 1); low.Floor = 0; low.Z = 0; low.Hp = low.MaxHp;
            var bomb = new Thrown { Id = NextId(), ItemId = "expl_satchel", OwnerId = top.Id, Pos = C(i0 + 1, j0 + 1), From = C(i0 + 1, j0 + 1), To = C(i0 + 1, j0 + 1), Floor = 1 };
            using (Ctx(1)) Explode(bomb);
            Check("бомба на втором этаже: человек под перекрытием цел", low.Hp >= low.MaxHp);

            // ---- обрушение: без стен первого этажа перекрытие падает ----
            int before = 0; foreach (var s in Structures) if (s.Floor == 1) before++;
            var walls0 = new List<Structure>();
            foreach (var s in Structures) if (s.Floor == 0 && s.IsEdge && s.OwnerId == bld.Id) walls0.Add(s);
            var demol = walls0.Count > 0 ? HammerWhy(bld, walls0[0], HammerDemolish) : "";
            bld.Pos = walls0.Count > 0 ? walls0[0].Center : bld.Pos;
            demol = walls0.Count > 0 ? HammerWhy(bld, walls0[0], HammerDemolish) : "";
            log($"ЭТАЖ-ТЕСТ (снос стены киянкой: «{demol}»)");
            foreach (var s in walls0) DamageStructure(s, 1e6f, top.Id);
            int after = 0; foreach (var s in Structures) if (s.Floor == 1 && s.Kind != StructKind.Stairs) after++;
            Check($"обрушение: второй этаж {before} → {after}", after < before / 2);

            // ---- воздух: летящего достаёт пуля с земли ----
            var flyer = Mk("Тест-летун");
            flyer.Flying = true; flyer.Z = Building.AirZ; flyer.Floor = Building.MaxFloor;
            float FlyDuel()
            {
                float dealt = 0;
                var wd = Items.Get("weapon_pps43").Weapon;
                low.Belt[0] = Items.NewSlot("weapon_pps43", wd.Magazine); low.Current = 0; low.Add(wd.AmmoId, 200);
                var at = ground + new Vector2(0, 6);
                log($"ЭТАЖ-ТЕСТ (воздух: стрелок на суше {Terrain.IsLand(at)}, летун на суше {Terrain.IsLand(at + new Vector2(12, 0))})");
                for (int k = 0; k < (int)(1.2f / Dt); k++)
                {
                    low.Pos = at; low.Floor = 0; low.Z = 0;
                    flyer.Pos = at + new Vector2(12, 0); flyer.Z = Building.AirZ;
                    var c = InputCmd.Empty; c.Aim = flyer.Pos; c.Fire = true; c.Peek = 1;
                    float hp0 = flyer.Hp;
                    Step(new Dictionary<int, InputCmd> { [low.Id] = c, [flyer.Id] = InputCmd.Empty });
                    dealt += MathF.Max(0, hp0 - flyer.Hp);
                    flyer.Hp = flyer.MaxHp; flyer.Dead = false;
                }
                return dealt;
            }
            Check("воздух: пуля с земли попадает в летящего", FlyDuel() > 0);
            low.Facing = 0; low.Pos = ground + new Vector2(0, 6); flyer.Pos = low.Pos + new Vector2(12, 0);
            Check("воздух: летящего видно с земли", CanSeeActor(low, flyer, 50, 130));

            foreach (var a in new[] { bld, p, top, low, flyer }) Actors.Remove(a);
            log($"ЭТАЖ-ТЕСТ итог: {(fails == 0 ? "всё OK" : $"провалов {fails}")}");
        }
    }
}
