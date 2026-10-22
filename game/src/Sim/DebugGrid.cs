using System;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Отладка сетки клеток D52 (--gridshow): у клетки (ci, cj) строится база 2×2 + клетка под станок + треугольник,
    /// и всё ставится через ту же проверку, что у игрока (FitDeploy): 4 ящика в одной клетке, верстак и печь (половины)
    /// в другой, токарный станок (целая), 2 ящика в треугольнике. Возвращает точку прицела для призрака со слотами.
    /// </summary>
    public sealed partial class World
    {
        public Vector2 DebugGridDemo(Actor me, int ci, int cj, string mode = "half")
        {
            var o = new Vector2(ci * Building.Cell, cj * Building.Cell);
            Vector2 P(float x, float y) => o + new Vector2(x, y);
            bool wasBot = me.IsBot;
            me.IsBot = true;   // без подмены элемента (на полу — стена, на проёме — дверь)
            me.Add("wood", 5000); me.Add("stone", 5000);

            void Build(StructKind k, Vector2 at)
            {
                me.Pos = at + new Vector2(0.3f, 2.5f);
                me.BuildPiece = k;
                TryBuild(me, at);
            }
            void Place(DeployKind k, Vector2 at, int rot = 0, int level = 1)
            {
                var fit = FitDeploy(me, k, at, rot);
                if (!fit.Ok) { Console.WriteLine($"gridshow: {k} в {at}: {fit.Why}"); return; }
                var d = new Deployable { Id = NextId(), Kind = k, OwnerId = me.Id, Level = level };
                ApplyFit(d, fit);
                if (k is DeployKind.Box or DeployKind.Lathe) d.Storage = new LootBag { Id = NextId(), Pos = d.Pos };
                d.Auth.Add(me.Id);
                AddDeployable(d);
            }

            // шкаф — до фундамента (как боты): потом пол «усыновит» его в слот
            me.Pos = P(1.5f, 4.5f);
            Place(DeployKind.Claim, P(1.5f, 2.5f));
            foreach (var (i, j) in new[] { (0, 0), (1, 0), (0, 1), (1, 1), (2, 0) }) Build(StructKind.Foundation, P(i * 2 + 1, j * 2 + 1));
            Build(StructKind.TriFoundation, P(1, -0.3f));                       // треугольник к северному краю клетки (0,0)
            Build(StructKind.Wall, P(3, 0)); Build(StructKind.Wall, P(5, 0));   // север (у (0,0) — треугольник)
            Build(StructKind.Doorway, P(1, 4));                                 // юг клетки (0,1): четверти перед проёмом — свободны

            Place(DeployKind.Box, P(0.5f, 0.5f)); Place(DeployKind.Box, P(1.5f, 0.5f));   // 4 ящика в клетке (0,0)
            Place(DeployKind.Box, P(0.5f, 1.5f)); Place(DeployKind.Box, P(1.5f, 1.5f));
            Place(DeployKind.Workbench, P(3, 0.5f), 0, 2);                      // клетка (1,0): верстак 2 (половина) + печь (половина)
            Place(DeployKind.Furnace, P(3, 1.5f), 0);
            Place(DeployKind.Lathe, P(5, 1), 1);                                // клетка (2,0): станок (целая)
            var tri = FloorAt(P(1, -0.5f));
            if (tri != null) foreach (var s in Footprints.Slots(tri)) Place(DeployKind.Box, s.c);   // 2 ящика в треугольнике
            Place(DeployKind.SleepBag, P(0.5f, 2.5f));
            Place(DeployKind.Box, P(3.5f, 2.5f));                               // клетка (1,1): занятый слот — красный

            // для картинки: полы — камень, стены — дерево (каркас прозрачный)
            foreach (var st in Structures)
                if (st.OwnerId == me.Id) { st.Tier = st.IsFloor ? 2 : 1; st.Hp = st.MaxHp; }
            StructVersion++;

            me.IsBot = wasBot;
            me.Pos = mode == "door" ? P(3.2f, 4.6f) : P(1.5f, 3.4f);   // в проходе у двери: камера захватывает всю базу
            me.DeployRot = 0;
            return mode switch
            {
                "tri" => P(1, -0.6f),        // ящик над треугольником: оба слота заняты — красные
                "lathe" => P(3, 3),          // станок (целая) в клетку (1,1), где уже стоит ящик — красный
                "door" => P(1, 3.5f),        // клетка у проёма: жёлтые четверти перед дверью
                _ => P(3, 3.5f),             // верстак (половина) в свободный нижний ряд клетки (1,1) — зелёный
            };
        }
    }
}
