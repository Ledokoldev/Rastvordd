using Godot;

namespace Rastvordd.Client
{
    /// <summary>
    /// Отладка рига (--riggallery[=naked|guard]): сетка поз — строки = ракурсы, столбцы = состояния и фазы удара.
    /// Нужна, чтобы шлифовать анимации по одному скриншоту.
    /// </summary>
    public partial class RigGallery : CanvasLayer
    {
        const int Scale = 2, CellW = 40, CellH = 70;

        static readonly (string name, float ang)[] Angles =
        {
            ("→", 0), ("↘", 0.7f), ("↓", 1.45f), ("↗", -0.7f), ("↑", -1.45f), ("←", Mathf.Pi),
        };

        // состояние, предмет, параметр
        static readonly (string name, string held, System.Action<RigState> set)[] Columns =
        {
            ("стоит", "weapon_ak", s => { }),
            ("шаг 1", "weapon_ak", s => { s.Speed = 3; s.Phase = 0.8f; }),
            ("шаг 2", "weapon_ak", s => { s.Speed = 3; s.Phase = 0.8f + Mathf.Pi; }),
            ("бег", "weapon_ak", s => { s.Speed = 6; s.Sprint = true; s.Phase = 1.2f; }),
            ("присед", "weapon_ak", s => { s.Crouch = true; }),
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
        };

        public RigGallery(string who)
        {
            Layer = 100;
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
                    rv.Refresh(false);
                }
        }
    }
}
