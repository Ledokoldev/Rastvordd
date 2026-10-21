using System;
using System.Collections.Generic;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>Взрывчатка: бросается по дуге, лежит с фитилём, взрывается. Урон по постройкам зависит от уровня.</summary>
    public sealed class ExplosiveDef
    {
        public float Damage;          // по людям и базовый по постройкам
        public float Radius;          // метры
        public float Fuse;            // секунды до взрыва
        public float DudChance;       // шанс осечки (связка)
        public float ThrowRange;
        public bool Sticks;           // шашка прилипает к стене
    }

    public sealed class Thrown
    {
        public int Id;
        public string ItemId;
        public int OwnerId;
        public Vector2 Pos, From, To;
        public float Flight, FlightTime;   // полёт по дуге
        public float Z;                    // высота над землёй (для картинки)
        public float FuseLeft;
        public bool Dud;
        public bool Landed => Flight >= FlightTime;
    }

    public sealed partial class World
    {
        public readonly List<Thrown> Throwns = new();

        /// <summary>Множитель урона взрывом по уровню: разметка рассыпается, камень — 2 шашки, металл — 4, сталь — 8.</summary>
        static readonly float[] BlastTierMult = { 3f, 1.2f, 0.55f, 0.5f, 0.5f };

        void Throw(Actor a, Vector2 aim)
        {
            var held = a.Held;
            var ex = held.Def.Explosive;
            var d = aim - a.Pos;
            float dist = MathF.Min(d.Length(), ex.ThrowRange);
            var dir = d.LengthSquared() > 0.01f ? Vector2.Normalize(d) : a.FacingDir;
            var from = a.Pos + dir * (a.Radius + 0.2f);
            var to = a.Pos + dir * dist;
            // через стену не перебросить: падает у стены (шашка прилипает к ней)
            float bestT = 1;
            foreach (var w in WallsAlong(from, to))
                if (!w.PassBullets && w.Wall.BlocksVision && Geo.SegmentAabb(from, to, w.Wall.Min, w.Wall.Max, out float t) && t < bestT) bestT = t;
            if (bestT < 1) to = from + (to - from) * MathF.Max(0, bestT - 0.15f / MathF.Max(0.1f, Vector2.Distance(from, to)));
            float flight = 0.25f + Vector2.Distance(from, to) / 14f;
            Throwns.Add(new Thrown
            {
                Id = NextId(), ItemId = held.Id, OwnerId = a.Id, Pos = from, From = from, To = to,
                FlightTime = flight, FuseLeft = ex.Fuse, Dud = Rng.NextDouble() < ex.DudChance,
            });
            held.Count--;
            if (held.Count <= 0) a.Belt[a.Current] = null;
            Events.Add(new SimEvent { Kind = SimEventKind.Swing, ActorId = a.Id, Pos = a.Pos, Dir = dir, Text = "throw" });
            Noises.Add(new Noise { Pos = a.Pos, Radius = 6, SourceId = a.Id, Time = Time });
        }

        void StepThrowns()
        {
            for (int i = Throwns.Count - 1; i >= 0; i--)
            {
                var t = Throwns[i];
                if (!t.Landed)
                {
                    t.Flight = MathF.Min(t.FlightTime, t.Flight + Dt);
                    float k = t.Flight / t.FlightTime;
                    t.Pos = Vector2.Lerp(t.From, t.To, k);
                    t.Z = 4f * k * (1 - k) * (0.6f + Vector2.Distance(t.From, t.To) * 0.08f);
                    continue;
                }
                t.Z = 0;
                t.FuseLeft -= Dt;
                if (t.FuseLeft > 0) continue;
                Throwns.RemoveAt(i);
                if (t.Dud)
                {
                    // осечка связки: фитиль потух — можно поднять и бросить снова (пока просто лежит как предмет)
                    Events.Add(new SimEvent { Kind = SimEventKind.Info, ActorId = t.OwnerId, Pos = t.Pos, Text = "Осечка: фитиль потух" });
                    var bag = new LootBag { Id = NextId(), Pos = t.Pos, Despawn = 120 };
                    bag.Items.Add(Items.NewSlot(t.ItemId));
                    Bags.Add(bag);
                    continue;
                }
                Explode(t);
            }
        }

        void Explode(Thrown t)
        {
            var ex = Items.Get(t.ItemId).Explosive;
            float r = ex.Radius;
            // постройки: урон от расстояния до ближайшей точки элемента
            foreach (var s in new List<Structure>(Structures))
            {
                float dist;
                if (s.IsEdge)
                {
                    dist = float.MaxValue;
                    foreach (var (min, max, _, _) in Building.Boxes(s.Kind, s.I, s.J, s.Dir, s.Open))
                        dist = MathF.Min(dist, Vector2.Distance(Vector2.Clamp(t.Pos, min, max), t.Pos));
                    if (s.Boxes.Count == 0) dist = Vector2.Distance(s.Center, t.Pos);
                }
                else dist = Vector2.Distance(Vector2.Clamp(t.Pos, new Vector2(s.I, s.J) * Building.Cell, new Vector2(s.I + 1, s.J + 1) * Building.Cell), t.Pos) + 0.6f;
                if (dist > r) continue;
                float fall = 1f - 0.5f * dist / r;
                DamageStructure(s, ex.Damage * fall * BlastTierMult[Math.Clamp(s.Tier, 0, 4)], t.OwnerId);
            }
            // люди: только если между взрывом и человеком нет стены
            foreach (var a in Actors)
            {
                if (a.Dead) continue;
                float dist = Vector2.Distance(a.Pos, t.Pos);
                if (dist > r * 1.4f || !HasLineOfSight(t.Pos, a.Pos)) continue;
                Damage(a, ex.Damage * 0.35f * (1f - dist / (r * 1.4f)), t.OwnerId, a.Pos);
            }
            Noises.Add(new Noise { Pos = t.Pos, Radius = 60, SourceId = t.OwnerId, Time = Time });
            Events.Add(new SimEvent { Kind = SimEventKind.Explosion, ActorId = t.OwnerId, Pos = t.Pos, Value = r, Text = t.ItemId });
        }
    }
}
