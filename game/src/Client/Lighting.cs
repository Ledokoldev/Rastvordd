using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;

namespace Rastvordd.Client
{
    /// <summary>
    /// Свет готовых РТ ночью: фонари со световым конусом, лампы у дверей, лампы в корпусе Подстанции и сполохи трансформаторов
    /// при включённом питании. Все источники бросают настоящие тени: стены, хитбоксы техники и машины — заслоны (LightOccluder2D),
    /// свет «трассируется» от источника до первого заслона, за ним — тень.
    /// </summary>
    public sealed class LightingLayer
    {
        sealed class Lamp
        {
            public PointLight2D Light;
            public LightCone2D Cone;
            public float Base, Phase;
            public bool Broken, NeedsPower;
            public bool Radio;      // питание от дизеля Радиоцентра (иначе — щиток Подстанции)
        }

        readonly List<Lamp> _lamps = new();
        readonly RandomNumberGenerator _rng = new() { Seed = 4242 };
        float _t;

        public LightingLayer(MapInfo map, Node world)
        {
            // заслоны: всё, что выше полутора метров (стены карты и РТ, техника, машины, шкафы)
            var occ = new Node2D { Name = "LightOccluders" };
            world.AddChild(occ);
            foreach (var w in map.Walls)
            {
                if (w.Height < 1.4f) continue;
                if (w.Max.X - w.Min.X < 0.7f && w.Max.Y - w.Min.Y < 0.7f) continue;   // столбы фонарей и тонкие опоры — не заслон (иначе лампа гасит сама себя)
                var a = Iso.Px(w.Min); var b = Iso.Px(w.Max);
                occ.AddChild(new LightOccluder2D
                {
                    Occluder = new OccluderPolygon2D { Polygon = new[] { a, new Vector2(b.X, a.Y), b, new Vector2(a.X, b.Y) }, CullMode = OccluderPolygon2D.CullModeEnum.Disabled },
                });
            }

            foreach (var d in map.Decors)
            {
                if (d.Flat) continue;
                var p = Iso.Px(d.Pos);
                switch (d.Sprite)
                {
                    case "props/subst_lamp":
                        // уличный фонарь: голова смещена от столба, свет падает на землю под ней; натриевый, тёплый
                        AddLamp(world, p + new Vector2(d.Flip ? 26 : -26, 6), 10f, new Color(1f, 0.72f, 0.42f), 1.35f, Iso.H(6.2f), true);
                        break;
                    case "props/door_lamp_post":
                        AddLamp(world, p + new Vector2(0, 4), 5.5f, new Color(1f, 0.86f, 0.62f), 1.1f, Iso.H(1.9f), false);
                        break;
                    case "props/fuel_canopy":
                        // под навесом АЗС — два ряда старых ламп
                        AddLamp(world, p + new Vector2(-120, -40), 6.5f, new Color(0.9f, 0.95f, 1f), 1.0f, 0, false);
                        AddLamp(world, p + new Vector2(120, -40), 6.5f, new Color(0.9f, 0.95f, 1f), 1.0f, 0, false);
                        break;
                    case "props/gai_post":
                    case "props/subst_kpp":
                        // окно будки светится: кто-то оставил лампу
                        AddLamp(world, p + new Vector2(0, 14), 4.5f, new Color(1f, 0.8f, 0.5f), 0.9f, 0, false);
                        break;
                    case "props/phone_booth":
                        AddLamp(world, p + new Vector2(0, 6), 3f, new Color(0.85f, 0.95f, 1f), 0.8f, 0, false);
                        break;
                    case "props/radio_watchtower":
                        // прожектор на вышке Радиоцентра: светит вниз-вперёд, на подход к вышке (горит всегда ночью — у вышки свой аккумулятор)
                        AddLamp(world, Iso.Px(d.Pos + new System.Numerics.Vector2(3f, 6f)), 13f, new Color(0.85f, 0.92f, 1f), 1.5f, Iso.H(7.5f), true);
                        break;
                    case "props/radio_console":
                    case "props/radio_transmitter_big":
                        // аппаратура «Маяка» светится лампами и шкалами, когда завели дизель
                        AddLamp(world, p + new Vector2(0, -16), 5f, d.Sprite.EndsWith("console") ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.75f, 0.35f), 0.9f, 0, false, needsPower: true, flicker: false, radio: true);
                        break;
                    case "props/subst_transformer":
                        // гудит под нагрузкой: холодные сполохи, только при питании
                        AddLamp(world, p + new Vector2(0, -10), 7f, new Color(0.55f, 0.75f, 1f), 0.9f, 0, false, needsPower: true, flicker: true);
                        break;
                }
            }
            // лампы в корпусе Подстанции — загораются, когда включён щиток
            foreach (var r in map.Roofs)
            {
                if (r.Tile != "tiles/roof_tar") continue;
                var c = (Iso.Px(r.Min) + Iso.Px(r.Max)) / 2;
                AddLamp(world, c, Mathf.Min(9f, (r.Max - r.Min).Length() * 0.45f), new Color(0.82f, 0.92f, 1f), 1.2f, 0, false, needsPower: true);
            }
        }

        void AddLamp(Node world, Vector2 at, float radiusM, Color c, float energy, float headH, bool cone, bool needsPower = false, bool flicker = false, bool radio = false)
        {
            var l = new PointLight2D
            {
                Texture = Art.LightTex, TextureScale = radiusM * Iso.PPM * 2 / 256f, Color = c, Energy = 0, Position = at,
                BlendMode = Light2D.BlendModeEnum.Add, ShadowEnabled = true, ShadowFilter = Light2D.ShadowFilterEnum.Pcf13,
                ShadowFilterSmooth = 2.5f, ShadowColor = new Color(0, 0, 0, 0.92f), Enabled = false,
            };
            world.AddChild(l);
            var lamp = new Lamp { Light = l, Base = energy, Phase = _rng.Randf() * 50, NeedsPower = needsPower, Radio = radio, Broken = flicker || (!radio && _rng.Randf() < 0.18f) };
            if (cone && headH > 0)
            {
                lamp.Cone = new LightCone2D { Position = at, Head = new Vector2(0, -headH), RadiusPx = radiusM * Iso.PPM * 0.75f, Tint = c, ZIndex = 3 };
                world.AddChild(lamp.Cone);
            }
            _lamps.Add(lamp);
        }

        /// <summary>night — 0 днём, 1 ночью; powered — включён ли щиток Подстанции; radioPowered — молотит ли дизель Радиоцентра; view — видимая часть мира (дальние лампы спят).</summary>
        public void Update(float night, bool substPowered, Rect2 view, float dt, bool radioPowered = false)
        {
            _t += dt;
            var near = view.Grow(400);
            foreach (var lamp in _lamps)
            {
                bool powered = lamp.Radio ? radioPowered : substPowered;
                bool on = night > 0.05f && (!lamp.NeedsPower || powered) && near.HasPoint(lamp.Light.Position);
                if (lamp.NeedsPower && powered && near.HasPoint(lamp.Light.Position)) on = true;   // питание — лампы горят и днём (внутри темно)
                lamp.Light.Enabled = on;
                if (!on) { if (lamp.Cone != null) lamp.Cone.Visible = false; continue; }
                float k = lamp.NeedsPower ? Mathf.Max(night, 0.45f) : night;
                float e = lamp.Base * k;
                if (lamp.Broken)
                {
                    // старая лампа: мерцает и иногда гаснет на миг
                    float s = Mathf.Sin(_t * 23 + lamp.Phase) * Mathf.Sin(_t * 7.3f + lamp.Phase * 1.7f);
                    e *= s > 0.85f ? 0.15f : 0.85f + 0.15f * Mathf.Sin(_t * 41 + lamp.Phase);
                }
                lamp.Light.Energy = e;
                if (lamp.Cone != null) { lamp.Cone.Visible = true; lamp.Cone.Strength = e / lamp.Base * 0.22f; lamp.Cone.QueueRedraw(); }
            }
        }
    }

    /// <summary>Световой конус фонаря в воздухе: мягкий аддитивный «луч» от головы фонаря к пятну на земле.</summary>
    public partial class LightCone2D : Node2D
    {
        public Vector2 Head;
        public float RadiusPx, Strength;
        public Color Tint;

        public LightCone2D()
        {
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        }

        public override void _Draw()
        {
            if (Strength <= 0.005f) return;
            const int N = 18;
            var pts = new Vector2[N + 1];
            var cols = new Color[N + 1];
            // свечение лампы: мягкий ореол у головы фонаря
            DrawCircle(Head, 10, new Color(Tint, Strength * 0.6f));
            DrawCircle(Head, 4, new Color(1, 1, 0.9f, Mathf.Min(1, Strength * 3f)));
            pts[0] = Head;
            cols[0] = new Color(Tint, Strength * 0.8f);
            for (int i = 0; i < N; i++)
            {
                // нижняя дуга пятна на земле (эллипс в виде 3/4)
                float a = Mathf.Pi * i / (N - 1);
                pts[i + 1] = new Vector2(Mathf.Cos(a) * RadiusPx * 0.62f, Mathf.Sin(a) * RadiusPx * 0.22f);
                cols[i + 1] = new Color(Tint, 0);
            }
            DrawPolygon(pts, cols);
        }
    }
}
