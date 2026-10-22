using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Полигоны (docs/design/18-sandbox.md): ровная площадка вдали от РТ, укрытия, мишени, свой шкаф.
    /// Только отладочные запуски (--sandbox=…): сохранение игрока они не трогают (Main._saveEnabled).
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// Перехват урона (полигон): вернуть true — урон не наносится (бессмертие, мишени), событие попадания всё равно
        /// уходит клиенту (маркер, звук, счётчики). Аргументы: цель, урон после брони, кто, зона.
        /// </summary>
        public Func<Actor, float, int, string, bool> DamageHook;

        /// <summary>
        /// Найти ровный кусок суши под полигон: прямоугольник [lo, hi] относительно центра целиком на суше (не вода, не болото),
        /// не ближе margin м к РТ. Из подходящих — где меньше всего стен карты и ресурсов (их всё равно уберём). Детерминированно.
        /// </summary>
        public Vector2 FindSandboxSpot(Vector2 lo, Vector2 hi, float margin = 60f)
        {
            Vector2 best = Vector2.Zero; float bestScore = float.MaxValue;
            int fBounds = 0, fBiome = 0, fMon = 0, fClaim = 0;
            var tmp = new List<ResourceNode>();
            var tmpW = new List<MapWall>();
            for (float x = -400; x <= 400; x += 20)
                for (float y = -400; y <= 400; y += 20)
                {
                    var c = new Vector2(x, y);
                    var mn = c + lo; var mx = c + hi;
                    if (mn.X < -HalfSize + 20 || mn.Y < -HalfSize + 20 || mx.X > HalfSize - 20 || mx.Y > HalfSize - 20) { fBounds++; continue; }
                    bool ok = true;
                    int odd = 0;   // не трава/лес: снег, болото, пустошь, пляж — хуже (погода, шаги)
                    for (float sx = mn.X; sx <= mx.X + 0.1f && ok; sx += 6)
                        for (float sy = mn.Y; sy <= mx.Y + 0.1f && ok; sy += 6)
                        {
                            var b = Terrain.BiomeAt(new Vector2(sx, sy));
                            if (b == Biome.Water) ok = false;
                            else if (b is not (Biome.Grassland or Biome.Forest)) odd++;
                        }
                    if (!ok) { fBiome++; continue; }
                    foreach (var corner in new[] { mn, mx, new Vector2(mn.X, mx.Y), new Vector2(mx.X, mn.Y), c })
                        if (NearMonument(corner, margin)) { ok = false; break; }
                    if (!ok) { fMon++; continue; }
                    foreach (var d in Deployables) if (d.Kind == DeployKind.Claim && Vector2.Distance(d.Pos, c) < 120) ok = false;
                    if (!ok) { fClaim++; continue; }
                    float r = (mx - mn).Length() * 0.5f;
                    var mid = (mn + mx) * 0.5f;
                    int walls = WallsInRadius(mid, r, tmpW).Count;
                    int nodes = NodesInRadius(mid, r, tmp).Count;
                    float score = walls * 10 + nodes + odd * 100 + c.Length() * 0.01f;   // при равенстве — ближе к центру острова
                    if (score < bestScore) { bestScore = score; best = c; }
                }
            Console.WriteLine($"sandbox: место {best} (оценка {bestScore:0}); отсеяно: край {fBounds}, вода/болото {fBiome}, РТ {fMon}, базы {fClaim}");
            return best;
        }

        /// <summary>Убрать ресурсы, пикапы, сумки с трупов и стены карты (не постройки) в прямоугольнике.</summary>
        public int SandboxClear(Vector2 min, Vector2 max, bool walls = false)
        {
            int n = 0;
            bool In(Vector2 p, float pad = 0) => p.X > min.X - pad && p.X < max.X + pad && p.Y > min.Y - pad && p.Y < max.Y + pad;
            for (int i = Nodes.Count - 1; i >= 0; i--)
                if (In(Nodes[i].Pos, Nodes[i].Radius)) { _nodeGrid.Remove(Nodes[i]); Nodes.RemoveAt(i); n++; }
            Pickups.RemoveAll(p => In(p.Pos));
            Bags.RemoveAll(b => !b.IsCrate && In(b.Pos));
            if (walls)
                for (int i = Walls.Count - 1; i >= 0; i--)
                {
                    var w = Walls[i];
                    if (w.Owner != null || w.Prefab) continue;
                    if (In((w.Wall.Min + w.Wall.Max) * 0.5f)) { _wallGrid.Remove(w); Walls.RemoveAt(i); n++; }
                }
            return n;
        }

        /// <summary>Укрытие полигона — стена карты (бетонный блок, стена, ящик). Ставить до того, как клиент возьмёт карту.</summary>
        public void SandboxWall(Vector2 min, Vector2 max, float h, WallKind kind) => AddWall(min.X, min.Y, max.X, max.Y, h, kind);

        /// <summary>Свой шкаф в точке (полигон «стройка»). null — не встал (причина в why).</summary>
        public Deployable SandboxClaim(Actor a, Vector2 at, out string why)
        {
            var fit = FitDeploy(a, DeployKind.Claim, at, 0);
            why = fit.Why;
            if (!fit.Ok) return null;
            var d = new Deployable { Id = NextId(), Kind = DeployKind.Claim, OwnerId = a.Id, Level = 1 };
            ApplyFit(d, fit);
            d.Auth.Add(a.Id);
            AddDeployable(d);
            return d;
        }

        /// <summary>Снести все постройки и установленное, кроме шкафов (кнопка «очистить» полигона стройки).</summary>
        public int SandboxResetBuild()
        {
            int n = Structures.Count;
            foreach (var s in new List<Structure>(Structures)) RemoveStructure(s);
            foreach (var d in new List<Deployable>(Deployables))
                if (d.Kind != DeployKind.Claim) { RemoveDeployable(d); n++; }
            return n;
        }

        /// <summary>Держать полдень (полигон): сдвиг времени суток, мир идёт как обычно.</summary>
        public void SandboxHoldNoon()
        {
            float diff = TodToPhase(0.5f) - DayPhase;
            if (diff > 0.5f) diff -= 1f;
            if (diff < -0.5f) diff += 1f;
            TimeOffset += diff * DayLength;
        }

        /// <summary>Без голода, жажды, радиации и холода.</summary>
        public static void SandboxKeepAlive(Actor a)
        {
            a.Food = 100; a.Water = 100; a.RadDose = 0; a.Cold = 0; a.WetLeft = 0;
        }

        /// <summary>Убрать персонажа полигона (бот, мишень) из мира насовсем.</summary>
        public void SandboxRemoveActor(Actor a)
        {
            Actors.Remove(a);
            NpcHomes.Remove(a.Id);
        }

        /// <summary>Новый персонаж полигона: мишень или бот (без пляжного старта и без мозга).</summary>
        public Actor SandboxActor(string name, Team team, Vector2 pos)
        {
            var a = new Actor { Id = NextId(), Name = name, Team = team, Pos = pos, IsBot = true };
            Actors.Add(a);
            if (team == Team.Npc) NpcHomes[a.Id] = pos;
            return a;
        }

        /// <summary>Одеть персонажа полигона по уровню (0 — голый, 1 — ватник/кожа, 2 — армейская, 4 — СОБР).</summary>
        public void SandboxDress(Actor a, int tier)
        {
            Array.Clear(a.Worn); Array.Clear(a.Plates);
            if (tier <= 0) return;
            Dress(a, Clothing.GuardKit(WearRng, tier));
            if (tier >= 2) Dress(a, tier >= 4 ? "plate_ceramic" : "plate_steel", tier >= 4 ? "plate_ceramic" : "plate_steel");
        }

        /// <summary>Выучить всё дерево (полигон «стройка»: крафт без изучения).</summary>
        public static void SandboxLearnAll(Actor a)
        {
            foreach (var n in Tech.All) a.Known.Add(n.Id);
        }
    }
}
