using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>
    /// Ручная настройка рига (F6, на ноутбуке Ctrl+6): для каждой части — ширина, длина, сдвиг по X/Y.
    /// Хранится по набору частей в user://rig_tune_&lt;набор&gt;.json; применяется в RigCanvas при рисовании.
    /// Особые ключи: «hips» — расстояние между бёдрами (X), «shoulders» — плечи (X — шире/уже, Y — выше/ниже).
    /// </summary>
    public static class RigTune
    {
        public sealed class T { public float Sx = 1, Sy = 1, Ox, Oy; }

        public static readonly string[] Parts = { "upper", "head", "torso", "pelvis", "shoulders", "upperarm", "forearm", "hips", "thigh", "shin" };
        public static readonly Dictionary<string, string> Names = new()
        {
            ["upper"] = "Верх тела ↕ (Сдвиг Y)", ["head"] = "Голова", ["torso"] = "Корпус", ["pelvis"] = "Таз", ["shoulders"] = "Плечи (крепление рук)",
            ["upperarm"] = "Плечо руки", ["forearm"] = "Предплечье", ["hips"] = "Бёдра (расстановка ног)", ["thigh"] = "Бедро", ["shin"] = "Голень",
        };

        static readonly Dictionary<string, Dictionary<string, T>> _sets = new();
        static readonly T One = new();

        /// <summary>Настройка части набора; нет — единичная.</summary>
        public static T Get(string set, string part)
        {
            if (set == null) return One;
            var d = Load(set);
            return d.TryGetValue(part, out var t) ? t : One;
        }

        /// <summary>Изменяемая настройка (для окна).</summary>
        public static T Edit(string set, string part)
        {
            var d = Load(set);
            if (!d.TryGetValue(part, out var t)) d[part] = t = new T();
            return t;
        }

        static Dictionary<string, T> Load(string set)
        {
            if (_sets.TryGetValue(set, out var d)) return d;
            d = new Dictionary<string, T>();
            _sets[set] = d;
            var path = $"user://rig_tune_{set}.json";
            if (!FileAccess.FileExists(path)) return d;
            var json = Json.ParseString(FileAccess.GetFileAsString(path));
            if (json.VariantType != Variant.Type.Dictionary) return d;
            foreach (var kv in json.AsGodotDictionary())
            {
                var v = kv.Value.AsGodotDictionary();
                d[kv.Key.AsString()] = new T { Sx = (float)v["sx"], Sy = (float)v["sy"], Ox = (float)v["ox"], Oy = (float)v["oy"] };
            }
            return d;
        }

        public static void Save(string set)
        {
            var root = new Godot.Collections.Dictionary();
            foreach (var kv in Load(set))
                root[kv.Key] = new Godot.Collections.Dictionary { ["sx"] = kv.Value.Sx, ["sy"] = kv.Value.Sy, ["ox"] = kv.Value.Ox, ["oy"] = kv.Value.Oy };
            using var f = FileAccess.Open($"user://rig_tune_{set}.json", FileAccess.ModeFlags.Write);
            f.StoreString(Json.Stringify(root, "  "));
        }

        public static void Reset(string set, string part) => Load(set).Remove(part);
    }

    /// <summary>Окно настройки рига: часть, ползунки ширины/длины/сдвига, замедление, сохранение. Плоское, минимальное.</summary>
    public partial class RigTuneWindow : PanelContainer
    {
        readonly string _set;
        readonly OptionButton _part = new();
        readonly (HSlider s, Label v)[] _sl = new (HSlider, Label)[4];
        static readonly (string name, float min, float max, float step)[] Ranges =
            { ("Ширина", 0.4f, 2f, 0.05f), ("Длина", 0.4f, 2f, 0.05f), ("Сдвиг X", -8, 8, 0.5f), ("Сдвиг Y", -8, 8, 0.5f) };
        bool _sync;

        public RigTuneWindow(string set)
        {
            _set = set;
            AnchorLeft = 1; AnchorRight = 1; OffsetLeft = -356; OffsetRight = -16; OffsetTop = 120; GrowHorizontal = GrowDirection.Begin; CustomMinimumSize = new Vector2(340, 0);
            AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.08f, 0.09f, 0.1f, 0.88f), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 });
            var box = new VBoxContainer(); AddChild(box);
            box.AddChild(new Label { Text = $"Риг «{set}» — настройка (F6)" });
            foreach (var p in RigTune.Parts) _part.AddItem(RigTune.Names[p]);
            _part.ItemSelected += _ => Sync();
            box.AddChild(_part);
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                var row = new HBoxContainer(); box.AddChild(row);
                row.AddChild(new Label { Text = Ranges[i].name, CustomMinimumSize = new Vector2(70, 0) });
                var s = new HSlider { MinValue = Ranges[i].min, MaxValue = Ranges[i].max, Step = Ranges[i].step, CustomMinimumSize = new Vector2(150, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
                var v = new Label { CustomMinimumSize = new Vector2(44, 0) };
                s.ValueChanged += val => Changed(k, (float)val);
                row.AddChild(s); row.AddChild(v);
                _sl[i] = (s, v);
            }
            var btns = new HBoxContainer(); box.AddChild(btns);
            var reset = new Button { Text = "Сброс части" }; reset.Pressed += () => { RigTune.Reset(_set, Cur); Sync(); };
            var save = new Button { Text = "Сохранить" }; save.Pressed += () => { RigTune.Save(_set); save.Text = "Сохранено ✓"; };
            btns.AddChild(reset); btns.AddChild(save);
            var aim = new CheckBox { Text = "Поза прицела (вместо ПКМ)" };
            aim.Toggled += on => ForceAim = on;
            box.AddChild(aim);
            var slow = new CheckBox { Text = "Замедление ×0,25" };
            slow.Toggled += on => Engine.TimeScale = on ? 0.25 : 1.0;
            box.AddChild(slow);
            box.AddChild(new Label { Text = "Ходи, приседай, целься — смотри шаг и хват. «Сохранить» пишет в user://rig_tune_" + set + ".json", Modulate = new Color(1, 1, 1, 0.6f), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(310, 0) });
            Sync();
        }

        string Cur => RigTune.Parts[_part.Selected < 0 ? 0 : _part.Selected];

        void Sync()
        {
            _sync = true;
            var t = RigTune.Get(_set, Cur);
            float[] v = { t.Sx, t.Sy, t.Ox, t.Oy };
            for (int i = 0; i < 4; i++) { _sl[i].s.Value = v[i]; _sl[i].v.Text = v[i].ToString("0.##"); }
            _sync = false;
        }

        void Changed(int k, float val)
        {
            _sl[k].v.Text = val.ToString("0.##");
            if (_sync) return;
            var t = RigTune.Edit(_set, Cur);
            if (k == 0) t.Sx = val; else if (k == 1) t.Sy = val; else if (k == 2) t.Ox = val; else t.Oy = val;
        }

        /// <summary>Галочка «поза прицела»: пока окно открыто, мышь в игру не идёт — прицел включается отсюда.</summary>
        public static bool ForceAim;

        public override void _ExitTree() { Engine.TimeScale = 1.0; ForceAim = false; }
    }
}
