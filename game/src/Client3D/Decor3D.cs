using System.Collections.Generic;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client3D
{
    /// <summary>
    /// Декор РТ в 3D. У каждого спрайта симуляции есть след (таблицы Foot: ширина, глубина, высота), спрайт стоит
    /// передним (южным) краем в точке Pos. По имени спрайта выбирается семейство (машина, БТР, вагон, бочка, шкаф,
    /// стол, столб, забор, будка, куча…), модель собирается из примитивов по точным размерам и цвету самого спрайта.
    /// Есть game/art3d/decor/&lt;имя&gt;.glb — встаёт он (подогнанный под след). Нет ни семейства, ни следа — стойка-картинка.
    /// Декали (Flat) — плоской картинкой на земле, плакаты на стенах — картинкой на стене.
    /// Подгружается клетками 32 м вокруг игрока.
    /// </summary>
    public partial class Decor3D : Node3D
    {
        const float CellM = 32f;
        public float LoadRadius = 120f, UnloadRadius = 150f;
        readonly Dictionary<(int, int), List<int>> _cells = new();
        readonly Dictionary<(int, int), Node3D> _built = new();
        readonly List<Item> _items = new();
        (int, int) _lastCell = (int.MinValue, 0);
        readonly Queue<(int, int)> _toBuild = new();
        // живой декор: подмена спрайта (роллета открыта, щиток включён) и перемещения (вагон, стрела крана, контейнер)
        readonly Dictionary<int, MapDecor> _byId = new();
        readonly Dictionary<int, (Node3D view, Node3D cell, Vector3 offset)> _views = new();
        readonly Dictionary<int, string> _sprite = new();
        readonly Dictionary<int, (SVec pos, float rot)> _moves = new();

        enum Src { Decor, PropWall }
        struct Item
        {
            public Src Src;
            public MapDecor D;
            public WallInfo W;
        }

        public void Build(MapInfo map, MapStatic3D stat)
        {
            foreach (var d in map.Decors) { Add(new Item { Src = Src.Decor, D = d }, d.Pos); _byId[d.Id] = d; }
            foreach (var w in stat.PropWalls) Add(new Item { Src = Src.PropWall, W = w }, (w.Min + w.Max) * 0.5f);
            // на крышах — сразу, в узел своей крыши (исчезают вместе с ней)
            foreach (var (sprite, pos, h, roof) in stat.RoofItems)
            {
                var node = stat.RoofNode(roof);
                if (node == null) continue;
                var d = new MapDecor { Sprite = sprite, Pos = pos };
                var v = DecorView(d, h);
                if (v != null) node.AddChild(v);
            }
        }

        void Add(Item it, SVec pos)
        {
            var key = ((int)Mathf.Floor(pos.X / CellM), (int)Mathf.Floor(pos.Y / CellM));
            if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>();
            list.Add(_items.Count);
            _items.Add(it);
        }

        /// <summary>Каждый кадр: достроить клетки рядом с игроком (не больше пары за кадр — без рывков), убрать дальние.</summary>
        public void Step(SVec me)
        {
            var cell = ((int)Mathf.Floor(me.X / CellM), (int)Mathf.Floor(me.Y / CellM));
            if (cell != _lastCell)
            {
                _lastCell = cell;
                int r = Mathf.CeilToInt(LoadRadius / CellM);
                var want = new List<((int, int) k, float d)>();
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var k = (cell.Item1 + dx, cell.Item2 + dy);
                    if (_built.ContainsKey(k) || !_cells.ContainsKey(k)) continue;
                    float d = CellDist(k, me);
                    if (d <= LoadRadius) want.Add((k, d));
                }
                want.Sort((a, b) => a.d.CompareTo(b.d));
                _toBuild.Clear();
                foreach (var (k, _) in want) _toBuild.Enqueue(k);
                var drop = new List<(int, int)>();
                foreach (var k in _built.Keys) if (CellDist(k, me) > UnloadRadius) drop.Add(k);
                foreach (var k in drop)
                {
                    var n = _built[k];
                    foreach (int idx in _cells[k]) if (_items[idx].Src == Src.Decor) _views.Remove(_items[idx].D.Id);
                    RemoveChild(n); n.QueueFree(); _built.Remove(k);
                }
            }
            for (int i = 0; i < 2 && _toBuild.Count > 0; i++)
            {
                var k = _toBuild.Dequeue();
                if (_built.ContainsKey(k)) continue;
                var root = new Node3D();
                foreach (int idx in _cells[k])
                {
                    var it = _items[idx];
                    if (it.Src == Src.Decor) { Place(it.D, root); continue; }
                    var v = PropWallView(it.W);
                    if (v != null) root.AddChild(v);
                }
                AddChild(root);
                _built[k] = root;
            }
        }

        /// <summary>Поставить декор в клетку с учётом подменённого спрайта и перемещения.</summary>
        void Place(MapDecor d, Node3D cell)
        {
            var src = d;
            if (_sprite.TryGetValue(d.Id, out var sp) && sp != d.Sprite)
                src = new MapDecor { Id = d.Id, Sprite = sp, Pos = d.Pos, Flip = d.Flip, Flat = d.Flat, Scale = d.Scale, Rot = d.Rot, Pivot = d.Pivot, Lift = d.Lift, Batch = d.Batch };
            var v = DecorView(src, 0);
            if (v == null) { _views.Remove(d.Id); return; }
            cell.AddChild(v);
            var offset = v.Position - W3.V(d.Pos, 0);
            _views[d.Id] = (v, cell, offset);
            if (_moves.TryGetValue(d.Id, out var mv)) Move(v, offset, mv.pos, mv.rot, d.Pivot);
        }

        static void Move(Node3D v, Vector3 offset, SVec pos, float rot, bool pivot)
        {
            // стрела крана вращается вокруг точки опоры (Pos), прочее — вокруг своего центра
            float yaw = -Mathf.DegToRad(rot);
            var off = pivot ? new Basis(Vector3.Up, yaw) * offset : offset;
            v.Position = W3.V(pos, 0) + off;
            v.Rotation = new Vector3(0, yaw, 0);
        }

        /// <summary>Каждый снапшот: подмены спрайтов и перемещения декора.</summary>
        public void Sync(Snapshot s)
        {
            foreach (var (id, sprite) in s.DecorSprites)
            {
                if (_sprite.TryGetValue(id, out var cur) && cur == sprite) continue;
                _sprite[id] = sprite;
                if (_views.TryGetValue(id, out var have) && _byId.TryGetValue(id, out var d))
                {
                    have.cell.RemoveChild(have.view);
                    have.view.QueueFree();
                    _views.Remove(id);
                    Place(d, have.cell);
                }
            }
            foreach (var (id, pos, rot) in s.DecorMoves)
            {
                _moves[id] = (pos, rot);
                if (_views.TryGetValue(id, out var have) && _byId.TryGetValue(id, out var d)) Move(have.view, have.offset, pos, rot, d.Pivot);
            }
        }

        static float CellDist((int, int) k, SVec me)
        {
            float cx = (k.Item1 + 0.5f) * CellM, cy = (k.Item2 + 0.5f) * CellM;
            return Mathf.Max(0, new Vector2(cx - me.X, cy - me.Y).Length() - CellM * 0.7f);
        }

        // ---------------------------------------------------------------- размеры и цвет

        static string IdOf(string sprite) => sprite == null ? "" : sprite[(sprite.LastIndexOf('/') + 1)..];

        public static bool FootOf(string id, out (float w, float d, float h, bool legs) f)
        {
            if (World.Foot.TryGetValue(id, out f)) return true;
            if (Tier0Layouts.Foot.TryGetValue(id, out f)) return true;
            if (Tier1Layouts.Foot.TryGetValue(id, out f)) return true;
            if (Tier2Layouts.Foot.TryGetValue(id, out f)) return true;
            if (DressFoot.Foot.TryGetValue(id, out f)) return true;
            return false;
        }

        static readonly Dictionary<string, (Color main, Color dark)> _colors = new();

        /// <summary>Средний цвет непрозрачных пикселей спрайта (и тёмный — по нижней трети): чтобы 3D-модель была «того же» цвета.</summary>
        public static (Color main, Color dark) ColorsOf(string sprite)
        {
            if (_colors.TryGetValue(sprite ?? "", out var have)) return have;
            var res = (new Color(0.5f, 0.5f, 0.48f), new Color(0.32f, 0.32f, 0.3f));
            var tex = Mats3D.Tex(sprite);
            var img = tex?.GetImage();
            if (img != null)
            {
                if (img.IsCompressed()) img.Decompress();
                int w = img.GetWidth(), h = img.GetHeight();
                float r = 0, g = 0, b = 0, n = 0, dr = 0, dg = 0, db = 0, dn = 0;
                const int S = 24;
                for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var c = img.GetPixel(Mathf.Min(w - 1, x * w / S), Mathf.Min(h - 1, y * h / S));
                    if (c.A < 0.5f) continue;
                    r += c.R; g += c.G; b += c.B; n++;
                    if (y > S * 2 / 3) { dr += c.R; dg += c.G; db += c.B; dn++; }
                }
                if (n > 0)
                {
                    var main = new Color(r / n, g / n, b / n);
                    var dark = dn > 0 ? new Color(dr / dn, dg / dn, db / dn) : main.Darkened(0.35f);
                    res = (main, dark.Darkened(0.15f));
                }
            }
            _colors[sprite ?? ""] = res;
            return res;
        }

        // ---------------------------------------------------------------- виды

        Node3D PropWallView(WallInfo w)
        {
            var c = (w.Min + w.Max) * 0.5f; var s = w.Max - w.Min;
            string sprite = w.Kind switch
            {
                WallKind.Car => "props/car_wreck",
                WallKind.Barrier => "props/concrete_barrier",
                WallKind.Junk => "props/junk_pile",
                WallKind.Barrel => "props/barrel_oil",
                _ => "props/crate_wood",
            };
            var root = new Node3D { Position = W3.V(c) };
            var size = new Vector3(s.X, Mathf.Max(0.4f, w.Height), s.Y);
            if (!Glb(root, IdOf(sprite), size, ColorsOf(sprite).main)) Family(root, IdOf(sprite), size, ColorsOf(sprite), w.Id);
            return root;
        }

        /// <summary>Узел декора. <paramref name="baseH"/> — высота основания (крыша) поверх Lift.</summary>
        static Node3D DecorView(MapDecor d, float baseH)
        {
            string id = IdOf(d.Sprite);
            if (d.Flat) return Decal(d);
            if (id.StartsWith("wall_") || id.StartsWith("lamp_wall") || id.Contains("poster") || id.Contains("calendar"))
                return WallPicture(d, baseH);

            bool hasFoot = FootOf(id, out var f);
            var tex = Mats3D.Tex(d.Sprite);
            if (!hasFoot)
            {
                // размеров нет — из картинки: ширина как есть, глубина — до метра, высота — по остатку
                float tw = tex != null ? tex.GetWidth() / Client.Iso.PPM * d.Scale : 1f, th = tex != null ? tex.GetHeight() / Client.Iso.PPM * d.Scale : 1f;
                float dd = Mathf.Clamp(tw * 0.5f, 0.2f, 1.2f);
                f = (tw, dd, Mathf.Clamp((th - dd) / Client.Iso.HS, 0.1f, 12f), false);
                // техника без записи следа (подбитые танки и БТР РТ): глубина как у машины, а не «до метра» —
                // иначе равномерная подгонка сожмёт танк до игрушки
                if (Models3D.VehicleKey(id) != null) f = (tw, id.Contains("_top") ? th : tw * 0.45f, 2.4f, false);
            }
            var root = new Node3D { Position = W3.V(new SVec(d.Pos.X, d.Pos.Y - f.d * 0.5f), baseH + d.Lift) };
            var size = new Vector3(Mathf.Max(0.05f, f.w), Mathf.Max(0.05f, f.h), Mathf.Max(0.05f, f.d));
            if (Glb(root, id, size, ColorsOf(d.Sprite).main)) return root;
            if (Family(root, id, size, ColorsOf(d.Sprite), d.Id)) return root;
            if (tex == null) return null;
            if (hasFoot) { Block(root, size, ColorsOf(d.Sprite).main); return root; }
            // запасной вариант: картинка-стойка (повёрнута к камере по вертикали), чтобы мир не пустел
            root.Position = W3.V(d.Pos, baseH + d.Lift);
            root.AddChild(new Sprite3D
            {
                Texture = tex,
                PixelSize = d.Scale / Client.Iso.PPM,
                Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
                Centered = false,
                Offset = new Vector2(-tex.GetWidth() * 0.5f, 0),
                FlipH = d.Flip,
                Shaded = true,
                AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            });
            return root;
        }

        static bool Glb(Node3D root, string id, Vector3 size, Color paint)
        {
            var m = Models3D.Decor(id);
            if (m != null)
            {
                root.AddChild(m);
                Models3D.Fit(m, size, false);
                return true;
            }
            // техника: модель по типу машины, вписана без искажений, кузов — в цвет спрайта, обгоревшие — тёмные
            m = Models3D.Vehicle(id);
            if (m == null) return false;
            root.AddChild(m);
            Models3D.FitKeep(m, size);
            bool military = Models3D.VehicleKey(id) is string k && (k.Contains("tank") || k.Contains("btr") || k.Contains("brdm") || k.Contains("bmp") || k.Contains("mtlb") || k.Contains("sau") || k.Contains("heli"));
            Models3D.Paint(m, paint, !military && (id.Contains("wreck") || id.Contains("burnt") || id.Contains("burning")));
            return true;
        }

        static Node3D Decal(MapDecor d)
        {
            var tex = Mats3D.Tex(d.Sprite);
            if (tex == null) return null;
            var root = new Node3D { Position = W3.V(d.Pos, 0.03f) };
            root.AddChild(new Sprite3D
            {
                Texture = tex,
                PixelSize = d.Scale / Client.Iso.PPM,
                Axis = Vector3.Axis.Y,
                RotationDegrees = new Vector3(0, -d.Rot, 0),
                FlipH = d.Flip,
                Shaded = true,
                AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            });
            return root;
        }

        /// <summary>Плакат, часы, щиток — картинкой на стене (по Lift — высота, лицом на юг, к камере по умолчанию).</summary>
        static Node3D WallPicture(MapDecor d, float baseH)
        {
            var tex = Mats3D.Tex(d.Sprite);
            if (tex == null) return null;
            var root = new Node3D { Position = W3.V(d.Pos, baseH + Mathf.Max(0.8f, d.Lift)) };
            root.AddChild(new Sprite3D
            {
                Texture = tex,
                PixelSize = d.Scale / Client.Iso.PPM,
                FlipH = d.Flip,
                Shaded = true,
                AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(0, 0, -0.17f),
            });
            return root;
        }

        static void Block(Node3D root, Vector3 s, Color c)
        {
            var mat = s.Y > 1.6f ? (Material)Mats3D.Cut(c, 0.85f, 0f, 1.0f) : Mats3D.Solid(c, 0.85f);
            W3.AddBox(root, new Vector3(0, s.Y * 0.5f, 0), s, 0, mat);
        }

        // ---------------------------------------------------------------- семейства

        static bool Has(string id, params string[] keys)
        {
            foreach (var k in keys) if (id.Contains(k)) return true;
            return false;
        }

        /// <summary>
        /// Строит модель семейства в root (центр следа на земле). size: X — ширина, Y — высота, Z — глубина.
        /// false — семейство не узнано.
        /// </summary>
        public static bool Family(Node3D root, string id, Vector3 size, (Color main, Color dark) col, int seed)
        {
            var b = new Fam(root, size, col, seed, id);
            // порядок важен: сначала узкие и «обманчивые» имена (bush_1 — не автобус, fridge_zil — не грузовик,
            // crate_vegetable — не стол, chair_overturned — не урна), потом широкие семейства
            if (Has(id, "lm_oak", "dead_tree", "tree_")) b.Tree(Has(id, "dead", "chem"));
            else if (Has(id, "node_stone", "boulder_single")) b.Rock();
            else if (Has(id, "bush", "flower", "fern", "grass", "dead_plant", "burdock", "cattail", "agaric")) b.Bush();
            else if (Has(id, "bust", "statue", "scarecrow")) b.Statue();
            else if (Has(id, "fallen", "cable_bundle")) b.Logs();
            else if (Has(id, "fridge", "washing_machine", "dresser", "piano", "fuel_pump", "fuel_hand_pump", "ac_unit", "skylight", "roof_hatch", "vent_box", "vent_mushroom", "vent_turbine", "parapet")) b.Cabinet();
            else if (Has(id, "bus_stop", "gazebo", "well_roofed")) b.Shed(false, true);
            else if (Has(id, "larek", "garage_brick", "outhouse", "greenhouse", "elevator_room", "gai_post")) b.Shed(false, false);
            else if (Has(id, "bedside")) b.Table();
            else if (Has(id, "wagon", "rail_cistern", "rail_freight", "locomotive")) b.RailCar(Has(id, "cistern"));
            else if (Has(id, "dumpster", "trash_container", "skip")) b.Dumpster();
            else if (Has(id, "pillbox")) b.Shed(false, false);
            else if (Has(id, "signal_box", "vent_fan_box", "schema_board")) b.Cabinet();
            else if (Has(id, "sandbox")) b.Frame();
            else if (Has(id, "oxygen_cylinder", "gas_cylinder_lying")) b.Tank();
            else if (Has(id, "boat")) b.Boat();
            else if (Has(id, "liaz", "ikarus")) b.Bus();
            else if (Has(id, "pipe_bridge")) b.Machine();
            else if (Has(id, "crate", "box", "chest", "suitcase", "container", "ammo", "cardboard", "sunduk")) b.Crates(Has(id, "stack", "crates", "boxes", "pile"));
            else if (Has(id, "sleeping_bag", "bsleep_bag")) b.Bed(false);
            else if (Has(id, "chimney", "exhaust_pipe", "vent_shaft", "concrete_rings", "milk_can", "fire_extinguisher")) b.Tank();
            else if (Has(id, "concrete_pipe", "vent_duct", "vent_pipes")) b.Tank();
            else if (Has(id, "heli_")) b.Heli();
            else if (Has(id, "btr", "bmp", "brdm", "radar_p18")) b.Armored();
            else if (Has(id, "wagon", "rail_cistern", "rail_freight", "locomotive")) b.RailCar(Has(id, "cistern"));
            else if (Has(id, "bus_", "bus_wreck", "van_", "uaz452", "trailer_hut")) b.Bus();
            else if (Has(id, "truck", "gaz53", "zil", "gaz66", "dumptruck", "bulldozer", "tractor", "trailer")) b.Truck();
            else if (Has(id, "car_", "jeep", "sedan", "zhiguli", "2101", "2106", "rakushka", "moskvich")) b.Car(Has(id, "wreck", "burnt", "open"));
            else if (Has(id, "boat", "barge", "kazanka", "lodka")) b.Boat();
            else if (Has(id, "excavator", "crane", "drill_rig", "conveyor", "hopper", "pumpjack", "gantry", "loading_ramp", "engine_stand", "handcar", "wheelset", "telega")) b.Machine();
            else if (Has(id, "water_tower", "watchtower", "hunting_tower")) b.Tower(Has(id, "water"));
            else if (Has(id, "oil_tank", "water_tank", "fuel_tank", "cistern", "gas_pipe")) b.Tank();
            else if (Has(id, "barrel", "keg", "drum")) b.Barrels(Has(id, "group", "kegs", "barricade") ? 3 : 1);
            else if (Has(id, "canister", "gas_cylinder", "bucket", "cone", "trash_urn")) b.Small(true);
            else if (Has(id, "dumpster", "trash_container", "skip")) b.Dumpster();
            else if (Has(id, "tire")) b.Tires();
            else if (Has(id, "sandbag")) b.Sandbags();
            else if (Has(id, "hedgehog", "tank_trap", "czech")) b.Hedgehogs();
            else if (Has(id, "fence", "barrier", "gate", "checkpoint", "buffer_stop", "barricade", "concrete_blocks", "wire_barbed", "concrete_slabs", "garage_ramp", "wallface_")) b.Fence();
            else if (Has(id, "pole", "pylon", "mast", "antenna", "streetlight", "lamp_post", "door_lamp", "signal", "lightning_rod", "windsock", "flag", "sign", "dish", "dipole", "subst_lamp", "insulator", "subst_portal", "switch_lever", "weather_station")) b.Pole();
            else if (Has(id, "shed", "hut", "booth", "kiosk", "tent", "canopy", "shelter", "porch", "car_wash", "pillbox", "guard", "kpp", "doghouse", "beehive")) b.Shed(Has(id, "tent"), Has(id, "canopy", "car_wash", "shelter"));
            else if (Has(id, "pile", "rubble", "heap", "junk", "scrap", "ruins", "sand_pile", "boulder", "hay", "anthill", "sack")) b.Heap();
            else if (Has(id, "log", "firewood", "stump", "sleepers", "planks", "pallet")) b.Logs();
            else if (Has(id, "bed", "cot", "bunk", "mattress", "sofa", "bathtub")) b.Bed(Has(id, "bunk"));
            else if (Has(id, "chair", "stool", "armchair", "bench")) b.Chair(Has(id, "bench"));
            else if (Has(id, "table", "desk", "counter", "workbench", "workshop", "console", "duty")) b.Table();
            else if (Has(id, "shelf", "rack", "bookcase", "cable_tray", "ladder_cage")) b.Shelf();
            else if (Has(id, "transformer", "rectifier", "switchgear", "cabinet", "panel", "relay", "switchboard", "notice_board", "battery", "diesel", "gen", "ktp", "breaker",
                "pneumo", "transmitter", "locker", "safe", "cash_register", "wardrobe", "sideboard", "radiator", "tv_", "stove", "sink", "washbasin",
                "toilet", "disconnector", "reel", "recycler", "burzhuika", "phone", "vise", "jacks")) b.Cabinet();
            else if (Has(id, "playground", "swing", "carousel", "slide", "well_crane", "clothesline", "carpet_beater", "drying_rack", "frame", "sandbox", "horizontal_bar", "easel")) b.Frame();
            else if (Has(id, "top_", "boots", "bottle", "jar", "mug", "kettle", "radio", "lamp", "book", "papers", "plates", "cards", "candle", "medkit", "typewriter", "tools", "ashtray", "cans", "bicycle", "mop", "overturned", "mushroom")) b.Small(false);
            else return false;
            return true;
        }

        /// <summary>Сборщик моделей семейств: всё — в локальных координатах следа (длинная сторона вдоль X).</summary>
        sealed class Fam
        {
            readonly Node3D _root;
            readonly Vector3 _s;          // размер следа (как задан)
            readonly float L, W, H;       // длина (вдоль X после поворота), ширина, высота
            readonly Color _c, _d;
            readonly RandomNumberGenerator _rng = new();
            readonly bool _tall;
            readonly string _id;
            readonly Node3D _n;                    // узел, повёрнутый так, что длинная сторона — вдоль X
            static readonly Color Rubber = new(0.1f, 0.1f, 0.1f), Glass = new(0.16f, 0.2f, 0.24f), Rust = new(0.45f, 0.28f, 0.18f), Metal = new(0.4f, 0.41f, 0.42f);

            public Fam(Node3D root, Vector3 s, (Color main, Color dark) col, int seed, string id)
            {
                _root = root; _s = s; _c = col.main; _d = col.dark; _id = id;
                _rng.Seed = (ulong)(seed * 7919 + id.Length * 131 + 17);
                bool alongX = s.X >= s.Z;
                L = alongX ? s.X : s.Z; W = alongX ? s.Z : s.X; H = s.Y;
                _n = new Node3D { Rotation = new Vector3(0, alongX ? 0 : Mathf.Pi / 2, 0) };
                _root.AddChild(_n);
                _tall = H > 1.7f;
            }

            Material Mat(Color c, float rough = 0.85f, float metal = 0f) => _tall ? Mats3D.Cut(c, rough, metal, 1.0f) : Mats3D.Solid(c, rough, metal);
            void B(Vector3 c, Vector3 s, Color col, float rough = 0.85f, float metal = 0f, float yaw = 0) => W3.AddBox(_n, c, s, yaw, Mat(col, rough, metal));
            void C(Vector3 basePt, float h, float r, Color col, float rough = 0.85f, float metal = 0f)
                => W3.AddMesh(_n, W3.Cylinder, basePt + new Vector3(0, h * 0.5f, 0), new Vector3(r * 2, h, r * 2), 0, Mat(col, rough, metal));
            void Seg(Vector3 a, Vector3 b, float r, Color col) => MapStatic3D.Segment(_n, a, b, r, Mat(col, 0.7f, 0.3f));
            void Wheel(float x, float z, float r, float w)
            {
                var m = new MeshInstance3D { Mesh = W3.Cylinder, MaterialOverride = Mats3D.Solid(Rubber, 0.9f) };
                m.Transform = new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2) * Basis.FromScale(new Vector3(r * 2, w, r * 2)), new Vector3(x, r, z));
                _n.AddChild(m);
            }
            float R(float a, float b) => _rng.RandfRange(a, b);

            /// <summary>Дерево-ориентир (дуб-патриарх, сухое дерево у химзавода) — моделью ресурса нужного вида, по высоте следа.</summary>
            public void Tree(bool dead)
            {
                var m = Models3D.ForNode(NodeKind.Tree, 0, dead ? Biome.Swamp : Biome.Grassland);
                if (m == null) { Bush(); return; }
                _root.AddChild(m);
                var bb = Models3D.Bounds(m);
                float k = Mathf.Max(H, 4f) / Mathf.Max(0.1f, bb.Size.Y);
                m.Scale = Vector3.One * Mathf.Clamp(k, 0.5f, 3f);
            }

            public void Rock()
            {
                var m = Models3D.ForNode(NodeKind.Stone, (int)_rng.Randi() % 3, Biome.Grassland);
                if (m == null) { Heap(); return; }
                _root.AddChild(m);
                Models3D.Fit(m, new Vector3(_s.X, H, _s.Z), false);
            }

            public void Car(bool wreck)
            {
                var body = wreck ? _c.Lerp(Rust, 0.4f) : _c;
                float wr = Mathf.Min(0.33f, H * 0.24f);
                B(new Vector3(0, wr + (H * 0.45f - wr) * 0.5f + wr * 0.3f, 0), new Vector3(L, H * 0.42f, W * 0.96f), body, 0.6f, 0.3f);
                B(new Vector3(-L * 0.06f, H * 0.78f, 0), new Vector3(L * 0.5f, H * 0.38f, W * 0.84f), body, 0.6f, 0.3f);
                B(new Vector3(-L * 0.06f, H * 0.8f, 0), new Vector3(L * 0.52f, H * 0.26f, W * 0.86f), Glass, 0.15f, 0.2f);
                foreach (float x in new[] { -L * 0.32f, L * 0.32f })
                foreach (float z in new[] { -W * 0.45f, W * 0.45f })
                    if (!wreck || R(0, 1) > 0.3f) Wheel(x, z, wr, 0.2f);
                B(new Vector3(L * 0.5f, H * 0.42f, 0), new Vector3(0.04f, 0.1f, W * 0.7f), _d, 0.6f, 0.5f);   // бампер
            }

            public void Bus()
            {
                float wr = Mathf.Min(0.45f, H * 0.16f);
                B(new Vector3(0, wr + (H - wr) * 0.5f, 0), new Vector3(L, H - wr, W), _c, 0.6f, 0.3f);
                B(new Vector3(0, H * 0.66f, 0), new Vector3(L * 0.96f, H * 0.26f, W * 1.01f), Glass, 0.15f, 0.2f);
                B(new Vector3(L * 0.5f, H * 0.6f, 0), new Vector3(0.02f, H * 0.4f, W * 0.86f), Glass, 0.15f, 0.2f);
                foreach (float x in new[] { -L * 0.32f, L * 0.3f })
                foreach (float z in new[] { -W * 0.45f, W * 0.45f })
                    Wheel(x, z, wr, 0.25f);
            }

            public void Truck()
            {
                float wr = Mathf.Min(0.5f, H * 0.18f);
                float cab = Mathf.Min(L * 0.3f, 2.2f);
                B(new Vector3(L * 0.5f - cab * 0.5f, wr + (H * 0.85f - wr) * 0.5f, 0), new Vector3(cab, H * 0.85f - wr, W * 0.95f), _c, 0.6f, 0.3f);
                B(new Vector3(L * 0.5f - cab * 0.3f, H * 0.72f, 0), new Vector3(cab * 0.5f, H * 0.2f, W * 0.96f), Glass, 0.15f, 0.2f);
                float bedL = L - cab - 0.15f;
                B(new Vector3(-L * 0.5f + bedL * 0.5f, wr * 2 + 0.05f, 0), new Vector3(bedL, 0.15f, W), _d, 0.8f, 0.3f);
                B(new Vector3(-L * 0.5f + bedL * 0.5f, wr * 2 + 0.12f + (H - wr * 2) * 0.35f, 0), new Vector3(bedL, (H - wr * 2) * 0.7f, W * 0.96f), _c.Lerp(_d, 0.4f), 0.8f, 0.2f);
                foreach (float x in new[] { L * 0.5f - cab * 0.5f, -L * 0.2f, -L * 0.38f })
                foreach (float z in new[] { -W * 0.43f, W * 0.43f })
                    Wheel(x, z, wr, 0.3f);
            }

            public void Armored()
            {
                float wr = Mathf.Min(0.55f, H * 0.22f);
                var hull = _c.Lerp(new Color(0.33f, 0.37f, 0.27f), 0.3f);
                var pts = new[] { new SVec(-L / 2, -W / 2), new SVec(L / 2, -W / 2), new SVec(L / 2, W / 2), new SVec(-L / 2, W / 2) };
                var top = new[] { new SVec(-L / 2 + 0.3f, -W / 2 + 0.3f), new SVec(L / 2 - 0.8f, -W / 2 + 0.3f), new SVec(L / 2 - 0.8f, W / 2 - 0.3f), new SVec(-L / 2 + 0.3f, W / 2 - 0.3f) };
                _n.AddChild(new MeshInstance3D { Mesh = PrismMesh(pts, top, wr * 1.1f, H * 0.78f), MaterialOverride = Mat(hull, 0.7f, 0.3f) });
                C(new Vector3(-L * 0.05f, H * 0.78f, 0), H * 0.18f, W * 0.22f, hull, 0.7f, 0.3f);
                Seg(new Vector3(-L * 0.05f, H * 0.88f, 0), new Vector3(L * 0.45f, H * 0.88f, 0), 0.05f, Metal);
                int n = L > 6 ? 4 : 3;
                for (int i = 0; i < n; i++)
                    foreach (float z in new[] { -W * 0.42f, W * 0.42f })
                        Wheel(-L * 0.36f + i * (L * 0.72f / (n - 1)), z, wr, 0.35f);
            }

            public void RailCar(bool cistern)
            {
                float bog = Mathf.Min(0.9f, H * 0.25f);
                B(new Vector3(0, bog * 0.55f, 0), new Vector3(L * 0.95f, 0.18f, W * 0.8f), new Color(0.15f, 0.15f, 0.15f), 0.8f, 0.4f);
                foreach (float x in new[] { -L * 0.36f, L * 0.36f })
                foreach (float z in new[] { -W * 0.36f, W * 0.36f })
                    Wheel(x, z, bog * 0.42f, 0.12f);
                if (cistern)
                {
                    var m = new MeshInstance3D { Mesh = W3.Cylinder, MaterialOverride = Mat(_c, 0.6f, 0.4f) };
                    float r = Mathf.Min(W, H - bog) * 0.5f;
                    m.Transform = new Transform3D(new Basis(Vector3.Forward, Mathf.Pi / 2) * Basis.FromScale(new Vector3(r * 2, L * 0.92f, r * 2)), new Vector3(0, bog + r, 0));
                    _n.AddChild(m);
                    C(new Vector3(0, bog + r * 2 - 0.05f, 0), 0.3f, 0.35f, _d, 0.6f, 0.4f);
                }
                else
                {
                    B(new Vector3(0, bog + (H - bog) * 0.5f, 0), new Vector3(L, H - bog, W), _c, 0.75f, 0.3f);
                    for (int i = 1; i < 8; i++) B(new Vector3(-L / 2 + i * L / 8, bog + (H - bog) * 0.5f, W * 0.5f + 0.01f), new Vector3(0.06f, H - bog, 0.02f), _d, 0.7f, 0.3f);
                }
            }

            public void Heli()
            {
                var body = _c.Lerp(new Color(0.35f, 0.4f, 0.3f), 0.4f);
                W3.AddMesh(_n, W3.Sphere, new Vector3(L * 0.18f, H * 0.42f, 0), new Vector3(L * 0.55f, H * 0.75f, W * 0.7f), 0, Mat(body, 0.7f, 0.3f));
                Seg(new Vector3(-L * 0.05f, H * 0.5f, 0), new Vector3(-L * 0.5f, H * 0.62f, 0), Mathf.Min(0.35f, H * 0.12f), body);
                B(new Vector3(-L * 0.48f, H * 0.8f, 0), new Vector3(0.6f, H * 0.5f, 0.1f), body, 0.7f, 0.3f);
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 2.1f + R(-0.3f, 0.3f);
                    B(new Vector3(L * 0.15f + Mathf.Cos(a) * L * 0.18f, H * 0.82f + R(-0.2f, 0), Mathf.Sin(a) * L * 0.18f), new Vector3(L * 0.36f, 0.05f, 0.3f), _d, 0.8f, 0.4f, -a);
                }
                B(new Vector3(L * 0.38f, H * 0.5f, 0), new Vector3(L * 0.1f, H * 0.3f, W * 0.5f), Glass, 0.15f, 0.2f);
            }

            public void Boat()
            {
                var pts = new[] { new SVec(-L / 2, -W / 2), new SVec(L * 0.3f, -W / 2), new SVec(L / 2, 0), new SVec(L * 0.3f, W / 2), new SVec(-L / 2, W / 2) };
                var bot = new[] { new SVec(-L / 2 + 0.2f, -W * 0.3f), new SVec(L * 0.25f, -W * 0.3f), new SVec(L * 0.42f, 0), new SVec(L * 0.25f, W * 0.3f), new SVec(-L / 2 + 0.2f, W * 0.3f) };
                _n.AddChild(new MeshInstance3D { Mesh = PrismMesh(bot, pts, 0, H * 0.7f), MaterialOverride = Mat(_c, 0.7f, 0.2f) });
                B(new Vector3(0, H * 0.6f, 0), new Vector3(L * 0.7f, 0.05f, W * 0.7f), _d, 0.85f);
            }

            public void Machine()
            {
                B(new Vector3(0, H * 0.15f, 0), new Vector3(L * 0.8f, H * 0.3f, W * 0.8f), _d, 0.8f, 0.4f);
                B(new Vector3(-L * 0.15f, H * 0.42f, 0), new Vector3(L * 0.35f, H * 0.3f, W * 0.6f), _c, 0.7f, 0.3f);
                Seg(new Vector3(0, H * 0.5f, 0), new Vector3(L * 0.45f, H * 0.95f, 0), Mathf.Min(0.25f, W * 0.08f), _c);
                Seg(new Vector3(L * 0.45f, H * 0.95f, 0), new Vector3(L * 0.5f, H * 0.2f, 0), 0.05f, new Color(0.15f, 0.15f, 0.15f));
            }

            public void Tower(bool water)
            {
                float legH = H * (water ? 0.65f : 0.75f);
                foreach (float x in new[] { -L * 0.35f, L * 0.35f })
                foreach (float z in new[] { -W * 0.35f, W * 0.35f })
                    Seg(new Vector3(x, 0, z), new Vector3(x * 0.6f, legH, z * 0.6f), 0.08f, _d);
                if (water) C(new Vector3(0, legH, 0), H - legH, Mathf.Min(L, W) * 0.45f, _c, 0.6f, 0.4f);
                else
                {
                    B(new Vector3(0, legH + 0.05f, 0), new Vector3(L * 0.8f, 0.1f, W * 0.8f), _c, 0.85f);
                    B(new Vector3(0, legH + (H - legH) * 0.35f, W * 0.39f), new Vector3(L * 0.8f, (H - legH) * 0.5f, 0.06f), _c, 0.85f);
                    B(new Vector3(0, H - 0.1f, 0), new Vector3(L * 0.95f, 0.15f, W * 0.95f), _d, 0.85f);
                }
            }

            public void Tank()
            {
                if (H >= Mathf.Min(L, W))
                    C(Vector3.Zero, H, Mathf.Min(L, W) * 0.48f, _c, 0.6f, 0.4f);
                else
                {
                    float r = Mathf.Min(W, H) * 0.5f;
                    var m = new MeshInstance3D { Mesh = W3.Cylinder, MaterialOverride = Mat(_c, 0.6f, 0.4f) };
                    m.Transform = new Transform3D(new Basis(Vector3.Forward, Mathf.Pi / 2) * Basis.FromScale(new Vector3(r * 2, L, r * 2)), new Vector3(0, r, 0));
                    _n.AddChild(m);
                }
            }

            public void Barrels(int n)
            {
                float r = Mathf.Min(0.3f, Mathf.Min(L / n, W) * 0.48f);
                for (int i = 0; i < n; i++)
                {
                    float x = n == 1 ? 0 : -L / 2 + r + i * (L - 2 * r) / (n - 1);
                    C(new Vector3(x, 0, R(-0.05f, 0.05f)), H, r, i % 2 == 0 ? _c : _c.Lerp(_d, 0.4f), 0.6f, 0.35f);
                    C(new Vector3(x, H * 0.3f, 0), 0.04f, r + 0.01f, _d, 0.6f, 0.4f);
                    C(new Vector3(x, H * 0.7f, 0), 0.04f, r + 0.01f, _d, 0.6f, 0.4f);
                }
            }

            public void Small(bool cylinder)
            {
                if (cylinder) C(Vector3.Zero, H, Mathf.Min(L, W) * 0.45f, _c, 0.7f, 0.2f);
                else B(new Vector3(0, H * 0.5f, 0), new Vector3(L * 0.85f, H, W * 0.85f), _c, 0.8f);
            }

            public void Dumpster()
            {
                var pts = new[] { new SVec(-L * 0.42f, -W * 0.45f), new SVec(L * 0.42f, -W * 0.45f), new SVec(L * 0.42f, W * 0.45f), new SVec(-L * 0.42f, W * 0.45f) };
                var top = new[] { new SVec(-L / 2, -W / 2), new SVec(L / 2, -W / 2), new SVec(L / 2, W / 2), new SVec(-L / 2, W / 2) };
                _n.AddChild(new MeshInstance3D { Mesh = PrismMesh(pts, top, 0.1f, H), MaterialOverride = Mat(_c, 0.7f, 0.35f) });
                B(new Vector3(0, H - 0.08f, 0), new Vector3(L * 0.95f, 0.06f, W * 0.95f), new Color(0.2f, 0.18f, 0.15f), 0.95f);
            }

            public void Tires()
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(H / 0.22f), 1, 6);
                for (int i = 0; i < n; i++)
                    _n.AddChild(new MeshInstance3D
                    {
                        Mesh = new TorusMesh { InnerRadius = 0.2f, OuterRadius = Mathf.Min(0.42f, Mathf.Min(L, W) * 0.5f) },
                        MaterialOverride = Mats3D.Solid(Rubber, 0.9f),
                        Position = new Vector3(R(-0.05f, 0.05f), 0.11f + i * 0.21f, R(-0.05f, 0.05f)),
                    });
            }

            public void Sandbags()
            {
                var bag = new Color(0.62f, 0.56f, 0.42f).Lerp(_c, 0.3f);
                int rows = Mathf.Clamp(Mathf.RoundToInt(H / 0.22f), 1, 8), per = Mathf.Max(1, Mathf.RoundToInt(L / 0.55f));
                for (int r = 0; r < rows; r++)
                for (int i = 0; i < per; i++)
                {
                    float x = -L / 2 + (i + 0.5f + (r % 2) * 0.5f) * L / per;
                    if (x > L / 2) continue;
                    W3.AddMesh(_n, W3.Sphere, new Vector3(x, 0.11f + r * 0.21f, R(-0.03f, 0.03f)), new Vector3(L / per * 1.05f, 0.24f, Mathf.Max(0.35f, W * 0.9f)), R(-0.1f, 0.1f), Mat(bag.Darkened(R(0, 0.15f)), 0.95f));
                }
            }

            public void Hedgehogs()
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(L / 1.5f));
                for (int i = 0; i < n; i++)
                {
                    var c = new Vector3(-L / 2 + (i + 0.5f) * L / n, H * 0.5f, 0);
                    float s = Mathf.Min(H, 1.4f) * 0.6f;
                    Seg(c + new Vector3(-s, -s, -s), c + new Vector3(s, s, s), 0.07f, _c.Lerp(Rust, 0.4f));
                    Seg(c + new Vector3(s, -s, -s), c + new Vector3(-s, s, s), 0.07f, _c.Lerp(Rust, 0.4f));
                    Seg(c + new Vector3(0, -s, s), c + new Vector3(0, s, -s), 0.07f, _c.Lerp(Rust, 0.4f));
                }
            }

            public void Fence()
            {
                if (W > 0.35f || H < 1.0f)
                {
                    // бетонный блок / барьер: трапеция
                    var bot = new[] { new SVec(-L / 2, -W / 2), new SVec(L / 2, -W / 2), new SVec(L / 2, W / 2), new SVec(-L / 2, W / 2) };
                    var top = new[] { new SVec(-L / 2, -W * 0.22f), new SVec(L / 2, -W * 0.22f), new SVec(L / 2, W * 0.22f), new SVec(-L / 2, W * 0.22f) };
                    _n.AddChild(new MeshInstance3D { Mesh = PrismMesh(bot, top, 0, H), MaterialOverride = Mat(_c, 0.9f) });
                    return;
                }
                int posts = Mathf.Max(2, Mathf.RoundToInt(L / 2.5f) + 1);
                for (int i = 0; i < posts; i++) B(new Vector3(-L / 2 + i * L / (posts - 1), H * 0.5f, 0), new Vector3(0.08f, H, 0.08f), _d, 0.8f, 0.3f);
                B(new Vector3(0, H * 0.5f, 0), new Vector3(L, H * 0.8f, 0.03f), _c, 0.8f, 0.2f);
            }

            public void Pole()
            {
                var mat = _c.Lerp(new Color(0.5f, 0.5f, 0.48f), 0.4f);
                bool dish = Has(_id, "dish", "radar");
                C(Vector3.Zero, H, Mathf.Clamp(Mathf.Min(L, W) * 0.12f, 0.05f, 0.2f), mat, 0.75f, 0.3f);
                if (dish)
                    W3.AddMesh(_n, W3.Sphere, new Vector3(0.2f, H * 0.85f, 0), new Vector3(0.15f, Mathf.Min(L, 2.5f), Mathf.Min(L, 2.5f)), 0, Mat(new Color(0.8f, 0.8f, 0.78f), 0.5f, 0.3f));
                else if (Has(_id, "streetlight", "lamp", "door_lamp"))
                {
                    Seg(new Vector3(0, H * 0.95f, 0), new Vector3(Mathf.Max(0.5f, L * 0.4f), H * 0.98f, 0), 0.04f, mat);
                    B(new Vector3(Mathf.Max(0.5f, L * 0.4f), H * 0.95f, 0), new Vector3(0.35f, 0.1f, 0.2f), new Color(0.9f, 0.88f, 0.75f), 0.4f);
                }
                else if (Has(_id, "pole", "pylon", "portal", "mast"))
                {
                    B(new Vector3(0, H * 0.92f, 0), new Vector3(0.1f, 0.1f, Mathf.Max(1.6f, W)), _d, 0.8f, 0.3f);
                    foreach (float z in new[] { -0.7f, 0f, 0.7f }) C(new Vector3(0, H * 0.92f + 0.05f, z), 0.18f, 0.05f, new Color(0.75f, 0.8f, 0.78f), 0.4f);
                }
                else if (Has(_id, "sign", "flag", "windsock"))
                    B(new Vector3(0.02f, H * 0.82f, 0), new Vector3(0.04f, Mathf.Min(0.8f, H * 0.3f), Mathf.Max(0.6f, L)), _c, 0.6f);
            }

            public void Shed(bool tent, bool canopy)
            {
                float wallH = tent ? 0.1f : H * 0.72f;
                if (canopy)
                {
                    foreach (float x in new[] { -L * 0.45f, L * 0.45f })
                    foreach (float z in new[] { -W * 0.45f, W * 0.45f })
                        B(new Vector3(x, H * 0.45f, z), new Vector3(0.12f, H * 0.9f, 0.12f), _d, 0.8f, 0.3f);
                    B(new Vector3(0, H * 0.93f, 0), new Vector3(L, 0.14f, W), _c, 0.8f, 0.2f);
                    return;
                }
                if (!tent)
                {
                    B(new Vector3(0, wallH * 0.5f, 0), new Vector3(L, wallH, W), _c, 0.9f);
                    B(new Vector3(L * 0.25f, wallH * 0.42f, W * 0.5f + 0.01f), new Vector3(0.8f, wallH * 0.8f, 0.04f), _d, 0.9f);   // дверь
                    B(new Vector3(-L * 0.2f, wallH * 0.6f, W * 0.5f + 0.01f), new Vector3(0.6f, 0.5f, 0.03f), Glass, 0.2f);         // окно
                }
                // двускатная крыша
                var bot = new[] { new SVec(-L / 2 - 0.1f, -W / 2 - 0.15f), new SVec(L / 2 + 0.1f, -W / 2 - 0.15f), new SVec(L / 2 + 0.1f, W / 2 + 0.15f), new SVec(-L / 2 - 0.1f, W / 2 + 0.15f) };
                var top = new[] { new SVec(-L / 2 - 0.1f, -0.02f), new SVec(L / 2 + 0.1f, -0.02f), new SVec(L / 2 + 0.1f, 0.02f), new SVec(-L / 2 - 0.1f, 0.02f) };
                _n.AddChild(new MeshInstance3D { Mesh = PrismMesh(bot, top, wallH, H), MaterialOverride = Mat(tent ? _c : _d, 0.9f) });
            }

            public void Heap()
            {
                int n = 3 + (int)(L * W);
                n = Mathf.Min(n, 9);
                W3.AddMesh(_n, W3.Sphere, new Vector3(0, 0, 0), new Vector3(L, H * 2, W), 0, Mat(_c.Lerp(_d, 0.3f), 0.95f));
                for (int i = 0; i < n; i++)
                {
                    var s = new Vector3(R(0.2f, 0.6f), R(0.1f, 0.35f), R(0.2f, 0.6f)) * Mathf.Clamp(L * 0.4f, 0.5f, 2f);
                    B(new Vector3(R(-L * 0.35f, L * 0.35f), H * R(0.3f, 0.8f), R(-W * 0.35f, W * 0.35f)), s, i % 2 == 0 ? _d : _c, 0.9f, 0.2f, R(0, 3));
                }
            }

            public void Logs()
            {
                var wood = _c.Lerp(new Color(0.45f, 0.32f, 0.2f), 0.4f);
                if (Has(_id, "stump"))
                {
                    C(Vector3.Zero, H, Mathf.Min(L, W) * 0.45f, wood, 0.95f);
                    return;
                }
                if (Has(_id, "pallet", "planks", "board", "sleepers"))
                {
                    int layers = Mathf.Clamp(Mathf.RoundToInt(H / 0.12f), 1, 10);
                    for (int i = 0; i < layers; i++) B(new Vector3(0, 0.06f + i * 0.12f, 0), new Vector3(L, 0.1f, W * 0.95f), wood.Darkened(i % 2 * 0.1f), 0.95f, 0, R(-0.03f, 0.03f));
                    return;
                }
                float r = Mathf.Clamp(Mathf.Min(H, W) * 0.25f, 0.08f, 0.35f);
                int rows = Mathf.Max(1, Mathf.RoundToInt(H / (r * 2))), per = Mathf.Max(1, Mathf.RoundToInt(W / (r * 2)));
                for (int y = 0; y < rows; y++)
                for (int i = 0; i < per - y % 2; i++)
                {
                    var m = new MeshInstance3D { Mesh = W3.Cylinder, MaterialOverride = Mat(wood.Darkened(R(0, 0.15f)), 0.95f) };
                    m.Transform = new Transform3D(new Basis(Vector3.Forward, Mathf.Pi / 2) * Basis.FromScale(new Vector3(r * 2, L, r * 2)), new Vector3(0, r + y * r * 1.7f, -W / 2 + r + (i + (y % 2) * 0.5f) * (W - 2 * r) / Mathf.Max(1, per - 1)));
                    _n.AddChild(m);
                }
            }

            public void Bed(bool bunk)
            {
                float legs = Mathf.Min(0.35f, H * 0.4f);
                B(new Vector3(0, legs * 0.5f, 0), new Vector3(L, legs, W), _d, 0.7f, 0.3f);
                B(new Vector3(0, legs + 0.08f, 0), new Vector3(L * 0.97f, 0.16f, W * 0.95f), _c, 0.95f);
                B(new Vector3(L * 0.38f, legs + 0.2f, 0), new Vector3(L * 0.18f, 0.1f, W * 0.8f), _c.Lightened(0.25f), 0.95f);
                if (bunk && H > 1.2f)
                {
                    B(new Vector3(0, H * 0.75f, 0), new Vector3(L, 0.08f, W), _d, 0.7f, 0.3f);
                    B(new Vector3(0, H * 0.75f + 0.12f, 0), new Vector3(L * 0.97f, 0.14f, W * 0.95f), _c, 0.95f);
                    foreach (float x in new[] { -L / 2 + 0.04f, L / 2 - 0.04f })
                    foreach (float z in new[] { -W / 2 + 0.04f, W / 2 - 0.04f })
                        B(new Vector3(x, H * 0.5f, z), new Vector3(0.05f, H, 0.05f), _d, 0.7f, 0.3f);
                }
            }

            public void Chair(bool bench)
            {
                float seat = Mathf.Min(0.48f, H * 0.5f);
                B(new Vector3(0, seat, 0), new Vector3(L, 0.06f, W * 0.9f), _c, 0.85f);
                foreach (float x in new[] { -L / 2 + 0.05f, L / 2 - 0.05f })
                foreach (float z in new[] { -W / 2 + 0.05f, W / 2 - 0.05f })
                    B(new Vector3(x, seat * 0.5f, z), new Vector3(0.04f, seat, 0.04f), _d, 0.8f, 0.2f);
                if (H > seat + 0.15f) B(new Vector3(0, seat + (H - seat) * 0.5f, -W / 2 + 0.03f), new Vector3(L, H - seat, 0.05f), _c, 0.85f);
            }

            public void Table()
            {
                float top = Mathf.Max(0.4f, H - 0.04f);
                B(new Vector3(0, top, 0), new Vector3(L, 0.06f, W), _c, 0.8f);
                foreach (float x in new[] { -L / 2 + 0.06f, L / 2 - 0.06f })
                foreach (float z in new[] { -W / 2 + 0.06f, W / 2 - 0.06f })
                    B(new Vector3(x, top * 0.5f, z), new Vector3(0.06f, top, 0.06f), _d, 0.8f, 0.2f);
                if (Has(_id, "counter", "console", "desk")) B(new Vector3(0, top * 0.5f, -W / 2 + 0.03f), new Vector3(L, top, 0.04f), _d, 0.8f);
            }

            public void Shelf()
            {
                foreach (float x in new[] { -L / 2 + 0.03f, L / 2 - 0.03f })
                    B(new Vector3(x, H * 0.5f, 0), new Vector3(0.05f, H, W), _d, 0.7f, 0.3f);
                int n = Mathf.Clamp(Mathf.RoundToInt(H / 0.45f), 2, 7);
                for (int i = 0; i < n; i++)
                {
                    float y = 0.05f + i * (H - 0.1f) / (n - 1);
                    B(new Vector3(0, y, 0), new Vector3(L, 0.04f, W), _c, 0.8f, 0.2f);
                    if (i < n - 1)
                        for (int k = 0; k < 3; k++)
                            if (R(0, 1) > 0.35f) B(new Vector3(R(-L * 0.35f, L * 0.35f), y + 0.14f, 0), new Vector3(R(0.15f, 0.35f), 0.24f, W * 0.7f), _c.Lerp(_d, R(0, 1)), 0.9f);
                }
                B(new Vector3(0, H * 0.5f, -W / 2 + 0.01f), new Vector3(L, H, 0.02f), _d.Darkened(0.2f), 0.9f);
            }

            public void Crates(bool stack)
            {
                if (!stack) { Crate(new Vector3(0, 0, 0), new Vector3(L, H, W)); return; }
                int levels = Mathf.Clamp(Mathf.RoundToInt(H / 0.6f), 1, 4), per = Mathf.Clamp(Mathf.RoundToInt(L / 0.8f), 1, 4);
                float ch = H / levels, cl = L / per;
                for (int y = 0; y < levels; y++)
                for (int i = 0; i < per - (y > 0 && R(0, 1) > 0.6f ? 1 : 0); i++)
                    Crate(new Vector3(-L / 2 + cl * (i + 0.5f), y * ch, R(-0.04f, 0.04f)), new Vector3(cl * 0.96f, ch * 0.98f, W * 0.95f));
            }

            void Crate(Vector3 at, Vector3 s)
            {
                B(at + new Vector3(0, s.Y * 0.5f, 0), s, _c, 0.85f);
                B(at + new Vector3(0, s.Y * 0.5f, 0), new Vector3(s.X * 0.08f, s.Y * 1.01f, s.Z * 1.02f), _d, 0.85f);
                B(at + new Vector3(0, s.Y * 0.98f, 0), new Vector3(s.X * 1.01f, s.Y * 0.05f, s.Z * 1.02f), _d, 0.85f);
            }

            public void Cabinet()
            {
                B(new Vector3(0, H * 0.5f, 0), new Vector3(L, H, W), _c, 0.6f, 0.25f);
                // дверцы и вентиляция на лицевой (южной, +Z) стороне
                int doors = Mathf.Clamp(Mathf.RoundToInt(L / 0.7f), 1, 6);
                for (int i = 0; i < doors; i++)
                {
                    float x = -L / 2 + (i + 0.5f) * L / doors;
                    B(new Vector3(x, H * 0.5f, W / 2 + 0.005f), new Vector3(L / doors * 0.9f, H * 0.86f, 0.02f), _c.Lerp(_d, 0.35f), 0.6f, 0.25f);
                    B(new Vector3(x + L / doors * 0.32f, H * 0.5f, W / 2 + 0.02f), new Vector3(0.03f, 0.12f, 0.03f), _d.Darkened(0.4f), 0.5f, 0.6f);
                }
                if (Has(_id, "transformer", "ktp", "oil"))
                    for (int k = 0; k < 3; k++) C(new Vector3(-L * 0.3f + k * L * 0.3f, H, 0), 0.5f, 0.08f, new Color(0.62f, 0.5f, 0.35f), 0.5f);
                B(new Vector3(0, H + 0.02f, 0), new Vector3(L * 1.02f, 0.04f, W * 1.02f), _d, 0.7f, 0.3f);
            }

            public void Bush()
            {
                var leaf = _c.Lerp(new Color(0.3f, 0.45f, 0.22f), 0.3f);
                int n = Mathf.Clamp(Mathf.RoundToInt(L * W * 3), 1, 4);
                for (int i = 0; i < n; i++)
                    W3.AddMesh(_n, W3.Sphere, new Vector3(R(-L * 0.25f, L * 0.25f), H * 0.5f, R(-W * 0.25f, W * 0.25f)), new Vector3(L * 0.7f, H, W * 0.7f) * R(0.75f, 1f), 0, Mat(leaf.Darkened(R(0, 0.2f)), 0.95f));
            }

            public void Frame()
            {
                var m = _c.Lerp(new Color(0.5f, 0.5f, 0.5f), 0.3f);
                Seg(new Vector3(-L / 2, 0, -W / 2), new Vector3(-L / 2 + 0.1f, H, 0), 0.04f, m);
                Seg(new Vector3(-L / 2, 0, W / 2), new Vector3(-L / 2 + 0.1f, H, 0), 0.04f, m);
                Seg(new Vector3(L / 2, 0, -W / 2), new Vector3(L / 2 - 0.1f, H, 0), 0.04f, m);
                Seg(new Vector3(L / 2, 0, W / 2), new Vector3(L / 2 - 0.1f, H, 0), 0.04f, m);
                Seg(new Vector3(-L / 2 + 0.1f, H, 0), new Vector3(L / 2 - 0.1f, H, 0), 0.04f, m);
                if (Has(_id, "slide")) Seg(new Vector3(L * 0.2f, H * 0.8f, 0), new Vector3(L / 2, 0.1f, 0), 0.25f, _c);
                if (Has(_id, "swing")) B(new Vector3(0, H * 0.3f, 0), new Vector3(0.5f, 0.05f, 0.25f), _d, 0.8f);
            }

            public void Statue()
            {
                B(new Vector3(0, H * 0.3f, 0), new Vector3(L * 0.7f, H * 0.6f, W * 0.7f), new Color(0.6f, 0.58f, 0.55f), 0.9f);
                W3.AddMesh(_n, W3.Sphere, new Vector3(0, H * 0.78f, 0), new Vector3(L * 0.5f, H * 0.4f, W * 0.5f), 0, Mat(_c, 0.6f, 0.3f));
            }

        }

        /// <summary>Призма: нижний многоугольник на y0, верхний на y1 (одинаковое число вершин, порядок совпадает).</summary>
        static ArrayMesh PrismMesh(SVec[] bot, SVec[] top, float y0, float y1)
        {
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            int n = bot.Length;
            for (int i = 1; i + 1 < n; i++)
            {
                st.SetNormal(Vector3.Up);
                st.AddVertex(W3.V(top[0], y1)); st.AddVertex(W3.V(top[i], y1)); st.AddVertex(W3.V(top[i + 1], y1));
                st.SetNormal(Vector3.Down);
                st.AddVertex(W3.V(bot[0], y0)); st.AddVertex(W3.V(bot[i + 1], y0)); st.AddVertex(W3.V(bot[i], y0));
            }
            SVec cen = SVec.Zero;
            foreach (var p in bot) cen += p;
            cen /= n;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var a0 = W3.V(bot[i], y0); var b0 = W3.V(bot[j], y0); var a1 = W3.V(top[i], y1); var b1 = W3.V(top[j], y1);
                var nrm = (b0 - a0).Cross(a1 - a0).Normalized();
                var mid = (bot[i] + bot[j]) * 0.5f - cen;
                if (nrm.X * mid.X + nrm.Z * mid.Y < 0) nrm = -nrm;
                st.SetNormal(nrm); st.AddVertex(a0); st.AddVertex(b0); st.AddVertex(b1);
                st.SetNormal(nrm); st.AddVertex(a0); st.AddVertex(b1); st.AddVertex(a1);
            }
            return st.Commit();
        }
    }
}
