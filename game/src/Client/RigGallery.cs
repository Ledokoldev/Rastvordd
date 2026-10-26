using Godot;

namespace Rastvordd.Client
{
    /// <summary>
    /// Отладка рига (--riggallery[=naked|guard]): сетка поз — строки = ракурсы, столбцы = состояния и фазы удара.
    /// Нужна, чтобы шлифовать анимации по одному скриншоту.
    /// </summary>
    public partial class RigGallery : CanvasLayer
    {
        const int Scale = 2, CellW = 38, CellH = 70;

        static readonly (string name, float ang)[] Angles =
        {
            ("→", 0), ("↘", 0.7f), ("↓", 1.45f), ("↗", -0.7f), ("↑", -1.45f), ("←", Mathf.Pi),
        };

        // состояние, предмет, параметр
        static readonly (string name, string held, System.Action<RigState> set)[] Columns =
        {
            ("стоит", "weapon_ak", s => { }),
            ("пятка", "weapon_ak", s => { s.Speed = 4.5f; s.Phase = 0; }),
            ("опора", "weapon_ak", s => { s.Speed = 4.5f; s.Phase = 0.6f; }),
            ("перенос", "weapon_ak", s => { s.Speed = 4.5f; s.Phase = 1.5f; }),
            ("бег 1", "weapon_ak", s => { s.Speed = 6.8f; s.Sprint = true; s.Phase = 0.4f; }),
            ("бег 2", "weapon_ak", s => { s.Speed = 6.8f; s.Sprint = true; s.Phase = 1.6f; }),
            ("присед", "weapon_ak", s => { s.Crouch = true; }),
            ("присед-шаг", "weapon_ak", s => { s.Crouch = true; s.Speed = 2.25f; s.Phase = 0.8f; }),
            ("прицел", "weapon_ak", s => { s.AimDown = 1; }),
            ("выстрел", "weapon_ak", s => { s.AimDown = 1; s.Kick = 1; }),
            ("перезар.", "weapon_ak", s => { s.Reload = 0.5f; }),
            ("топор 0", "tool_hatchet", s => { s.Swing = 0.0f; }),
            ("топор .2", "tool_hatchet", s => { s.Swing = 0.2f; }),
            ("топор .35", "tool_hatchet", s => { s.Swing = 0.35f; }),
            ("топор .42", "tool_hatchet", s => { s.Swing = 0.42f; }),
            ("топор .5", "tool_hatchet", s => { s.Swing = 0.5f; }),
            ("топор .8", "tool_hatchet", s => { s.Swing = 0.8f; }),
            ("кирка .2", "tool_pickaxe", s => { s.Swing = 0.2f; }),
            ("кирка .35", "tool_pickaxe", s => { s.Swing = 0.35f; }),
            ("кирка .42", "tool_pickaxe", s => { s.Swing = 0.42f; }),
            ("кирка .5", "tool_pickaxe", s => { s.Swing = 0.5f; }),
            ("кирка .8", "tool_pickaxe", s => { s.Swing = 0.8f; }),
            ("мёртв", "weapon_ak", s => { s.Dead = true; }),
        };

        public RigGallery(string who)
        {
            Layer = 100;
            // Лист ревью: --riggallery=hero:review[:фаза]. Все 28 поз в одном кадре без заслонов мира.
            if (who.Contains(":review")) { Review(who); return; }
            var set = RigSet.Load(who);
            var bg = new ColorRect { Color = new Color(0.23f, 0.26f, 0.22f), Size = new Vector2(4000, 3000) };
            AddChild(bg);
            if (set == null) return;
            var root = new Node2D { Scale = new Vector2(Scale, Scale), Position = new Vector2(20, 24) };
            AddChild(root);
            for (int c = 0; c < Columns.Length; c++)
            {
                var lbl = new Label { Text = Columns[c].name, Position = new Vector2(20 + c * CellW * Scale, 2) };
                lbl.AddThemeFontSizeOverride("font_size", 12);
                AddChild(lbl);
            }
            for (int r = 0; r < Angles.Length; r++)
                for (int c = 0; c < Columns.Length; c++)
                {
                    var rv = new RigView(set) { Position = new Vector2(c * CellW + CellW / 2, r * CellH + CellH - 4) };
                    root.AddChild(rv);
                    var s = rv.S;
                    s.Aim = Angles[r].ang;
                    s.Held = Columns[c].held;
                    Columns[c].set(s);
                    rv.Refresh(s.Dead);
                }
        }

        void Review(string who)
        {
            var args = who.Split(':');
            float phase = args.Length > 2 && float.TryParse(args[2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0.8f;
            var set = RigSet.Load(args[0]);
            AddChild(new ColorRect { Color = new Color(0.16f, 0.18f, 0.17f), Size = new Vector2(1600, 900) });
            if (set == null) return;
            string[] states = { "стоит", "идёт", "бежит", "присед", "крадётся", "прицел", "прицел в приседе" };
            string[] dirs = { "к камере", "вправо", "от камеры", "влево" };
            float[] angles = { Mathf.Pi / 2, 0, -Mathf.Pi / 2, Mathf.Pi };
            for (int row = 0; row < 7; row++)
                for (int col = 0; col < 4; col++)
                {
                    var origin = new Vector2(col * 400, row * 128);
                    var label = new Label { Text = states[row] + " · " + dirs[col], Position = origin + new Vector2(8, 2) };
                    label.AddThemeFontSizeOverride("font_size", 14);
                    AddChild(label);
                    var rv = new RigView(set) { Position = origin + new Vector2(200, 122), Scale = new Vector2(2, 2) };
                    AddChild(rv);
                    var s = rv.S;
                    s.Aim = angles[col]; s.Held = "weapon_ak"; s.Phase = phase;
                    s.Crouch = row is 3 or 4 or 6;
                    s.Sprint = row == 2;
                    s.Speed = row == 1 ? 4.5f : row == 2 ? 6.8f : row == 4 ? 2.25f : 0;
                    s.AimDown = row >= 5 ? 1 : 0;
                    rv.Refresh(false);
                }
        }
    }
}
