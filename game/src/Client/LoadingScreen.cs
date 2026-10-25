using System.Collections.Generic;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>
    /// Короткая загрузка перед тем, как отдать управление: заранее строит декор вокруг точки появления, подгружает текстуры
    /// растительности и один раз рисует каждый шейдер мира (прогрев) — иначе всё это случалось в первые секунды игры рывками.
    /// Экран плоский и простой (тёмный фон, надпись, тонкая полоса), без рамок. Мир на это время стоит.
    /// </summary>
    public partial class LoadingScreen : CanvasLayer
    {
        // шаг загрузки: вызывается, пока не вернёт true; null — дождаться следующего кадра (чтобы прогрев успел отрисоваться)
        readonly List<System.Func<bool>> _steps = new();
        int _at;
        readonly ColorRect _bar, _barBack;
        readonly Label _label;
        readonly Control _root;
        float _fade = -1;

        public bool Done { get; private set; }

        public LoadingScreen()
        {
            Layer = 100;
            _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(_root);
            var bg = new ColorRect { Color = new Color(0.055f, 0.06f, 0.065f), MouseFilter = Control.MouseFilterEnum.Ignore };
            bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _root.AddChild(bg);
            _label = new Label
            {
                Text = "Загрузка мира",
                HorizontalAlignment = HorizontalAlignment.Center,
                Size = new Vector2(400, 30),
            };
            _label.AddThemeFontOverride("font", UiTheme.Semi);
            _label.AddThemeFontSizeOverride("font_size", 18);
            _label.AddThemeColorOverride("font_color", UiTheme.Body);
            _root.AddChild(_label);
            _barBack = new ColorRect { Color = UiTheme.BarBack, Size = new Vector2(320, 3) };
            _root.AddChild(_barBack);
            _bar = new ColorRect { Color = UiTheme.Accent, Size = new Vector2(0, 3) };
            _root.AddChild(_bar);
        }

        public void Add(System.Func<bool> step) => _steps.Add(step);
        public void Add(System.Action step) => _steps.Add(() => { step(); return true; });
        /// <summary>Дождаться следующего кадра.</summary>
        public void NextFrame() => _steps.Add(null);

        /// <summary>Шаги загрузки, не дольше budgetMs за кадр (экран успевает обновлять полосу).</summary>
        public void Step(double budgetMs, float dt)
        {
            var vs = GetViewport().GetVisibleRect().Size;
            _label.Position = new Vector2(vs.X / 2 - 200, vs.Y / 2 - 34);
            _barBack.Position = new Vector2(vs.X / 2 - 160, vs.Y / 2 + 4);
            _bar.Position = _barBack.Position;
            if (_fade >= 0)
            {
                // всё готово: экран тает за четверть секунды
                _fade += dt / 0.25f;
                _root.Modulate = new Color(1, 1, 1, 1 - Mathf.Min(1, _fade));
                if (_fade >= 1) QueueFree();
                return;
            }
            ulong t0 = Time.GetTicksUsec();
            while (_at < _steps.Count && (Time.GetTicksUsec() - t0) / 1000.0 < budgetMs)
            {
                var s = _steps[_at];
                if (s == null) { _at++; break; }
                if (s()) _at++;
            }
            _bar.Size = new Vector2(320f * _at / Mathf.Max(1, _steps.Count), 3);
            if (_at >= _steps.Count) { Done = true; _fade = 0; }
        }

        /// <summary>Все картинки папок art/&lt;dir&gt; (без карт нормалей — Art.Get берёт их сам).</summary>
        public static List<string> ArtIn(params string[] dirs)
        {
            var list = new List<string>();
            foreach (var d in dirs)
                foreach (var f in DirAccess.GetFilesAt($"res://art/{d}"))
                {
                    // в экспортированной сборке рядом лежат только .import/.remap
                    string name = f.Replace(".import", "").Replace(".remap", "");
                    if (!name.EndsWith(".png") || name.EndsWith("_n.png")) continue;
                    string id = $"{d}/{name[..^4]}";
                    if (!list.Contains(id)) list.Add(id);
                }
            return list;
        }

        /// <summary>Прогрев шейдеров: по спрайту на каждый материал мира (с картой нормалей и без) рядом с камерой, под экраном загрузки.</summary>
        public static Node2D WarmUp(Vector2 atPx)
        {
            var root = new Node2D { Position = atPx };
            var texs = new List<Texture2D>();
            foreach (var id in new[] { "trees/tree_oak_1", "grass/tuft_meadow_1", "clutter/flowers_poppy", "nodes/node_stone_1" })
                if (Art.Get(id) is Texture2D t) texs.Add(t);
            var mats = new Material[] { null, VisionFx.ObjectMat, VisionFx.GrassMat, VisionFx.TreeMat, VisionFx.LootMat, VisionFx.GroundMat };
            int k = 0;
            foreach (var m in mats)
                foreach (var t in texs)
                    root.AddChild(new Sprite2D { Texture = t, Material = m, Position = new Vector2(k++ % 8 * 8, k / 8 * 8), TextureFilter = CanvasItem.TextureFilterEnum.Nearest });
            return root;
        }
    }
}
