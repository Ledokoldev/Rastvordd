using System;
using Godot;

namespace Rastvordd.Client
{
    /// <summary>Игровые настройки (не читы и не отладка): хранятся в user://settings_game.cfg и применяются сразу.</summary>
    public static class GameSettings
    {
        const string Path = "user://settings_game.cfg";
        public static bool Fullscreen, VSync = true, ShowFps = true, Post = true;
        public static int MaxFps;                  // 0 — без ограничения
        public static int Quality = 2;             // качество графики: 0 Низкое, 1 Среднее, 2 Высокое, 3 Ультра (--quality=N перекрывает)
        public static float MasterDb;
        public static string Theme = "", Layout = "";   // интерфейс: тема a/b/c, раскладка 1/2/3 ("" — по умолчанию); применяются при следующем запуске

        public static void Load()
        {
            var c = new ConfigFile();
            if (c.Load(Path) != Error.Ok) return;
            Fullscreen = (bool)c.GetValue("video", "fullscreen", Fullscreen);
            VSync = (bool)c.GetValue("video", "vsync", VSync);
            MaxFps = (int)c.GetValue("video", "max_fps", MaxFps);
            ShowFps = (bool)c.GetValue("video", "show_fps", ShowFps);
            Post = (bool)c.GetValue("video", "post", Post);
            Quality = Mathf.Clamp((int)c.GetValue("video", "quality", Quality), 0, 3);
            MasterDb = (float)c.GetValue("audio", "master_db", MasterDb);
            Theme = (string)c.GetValue("ui", "theme", Theme);
            Layout = (string)c.GetValue("ui", "layout", Layout);
        }

        /// <summary>--quality=0..3 — для сравнения пресетов (не сохраняется, если не трогать настройки).</summary>
        public static void CmdOverrides()
        {
            foreach (var a in OS.GetCmdlineUserArgs())
                if (a.StartsWith("--quality=") && int.TryParse(a.Substring(10), out int q)) Quality = Mathf.Clamp(q, 0, 3);
        }

        public static void Save()
        {
            var c = new ConfigFile();
            c.SetValue("video", "fullscreen", Fullscreen);
            c.SetValue("video", "vsync", VSync);
            c.SetValue("video", "max_fps", MaxFps);
            c.SetValue("video", "show_fps", ShowFps);
            c.SetValue("video", "post", Post);
            c.SetValue("video", "quality", Quality);
            c.SetValue("audio", "master_db", MasterDb);
            c.SetValue("ui", "theme", Theme);
            c.SetValue("ui", "layout", Layout);
            c.Save(Path);
        }

        /// <summary>Окно, частота кадров, общая громкость.</summary>
        public static void ApplyVideoAudio()
        {
            DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
            Engine.MaxFps = MaxFps;
            AudioServer.SetBusVolumeDb(0, MasterDb);
        }
    }

    /// <summary>Окно «Настройки» (Esc): стекло по центру, разделы «Игра», «Графика», «Звук», «Интерфейс».</summary>
    public partial class SettingsWindow : PanelContainer
    {
        public event Action WipeRequested, SaveRequested, Closed;
        public event Action<bool> FpsToggled, PostToggled;
        public event Action<int> QualityChanged;
        Button _wipe;
        bool _wipeArmed;

        public SettingsWindow()
        {
            SetAnchorsPreset(LayoutPreset.Center);
            GrowHorizontal = GrowDirection.Both; GrowVertical = GrowDirection.Both;
            CustomMinimumSize = new Vector2(560, 0);
            AddThemeStyleboxOverride("panel", UiTheme.Glass(16, 0.94f, 22));
            Visible = false;
            var box = UiTheme.VBox(10);
            AddChild(box);

            var head = UiTheme.HBox(10);
            var title = UiTheme.Strong("Настройки", 22);
            title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            head.AddChild(title);
            var close = UiTheme.Btn("Закрыть  Esc");
            close.Pressed += () => Close();
            head.AddChild(close);
            box.AddChild(head);

            // ---- игра ----
            Section(box, "Игра");
            var row = UiTheme.HBox(10);
            var save = UiTheme.Btn("Сохранить мир  F5");
            save.Pressed += () => SaveRequested?.Invoke();
            row.AddChild(save);
            _wipe = UiTheme.Btn("Новый вайп");
            _wipe.AddThemeColorOverride("font_color", UiTheme.Bad);
            _wipe.Pressed += () =>
            {
                // защита от случайного клика: первый раз — просим подтвердить
                if (!_wipeArmed) { _wipeArmed = true; _wipe.Text = "Точно? Мир сотрётся — нажми ещё раз"; return; }
                WipeRequested?.Invoke();
            };
            row.AddChild(_wipe);
            box.AddChild(row);

            // ---- графика ----
            Section(box, "Графика");
            Options(box, "Качество графики", new[] { "Низкое", "Среднее", "Высокое", "Ультра" }, new[] { 0, 1, 2, 3 }, GameSettings.Quality, v => { GameSettings.Quality = v; QualityChanged?.Invoke(v); });
            Check(box, "Полный экран", GameSettings.Fullscreen, on => { GameSettings.Fullscreen = on; GameSettings.ApplyVideoAudio(); });
            Check(box, "Вертикальная синхронизация (без разрывов кадра)", GameSettings.VSync, on => { GameSettings.VSync = on; GameSettings.ApplyVideoAudio(); });
            Options(box, "Ограничение FPS", new[] { "нет", "60", "120", "144", "240" }, new[] { 0, 60, 120, 144, 240 }, GameSettings.MaxFps, v => { GameSettings.MaxFps = v; GameSettings.ApplyVideoAudio(); });
            Check(box, "Счётчик FPS  F10", GameSettings.ShowFps, on => { GameSettings.ShowFps = on; FpsToggled?.Invoke(on); });
            Check(box, "Пост-обработка (цвет, свечение, виньетка)", GameSettings.Post, on => { GameSettings.Post = on; PostToggled?.Invoke(on); });

            // ---- звук ----
            Section(box, "Звук");
            Slider(box, "Общая громкость, дБ", -40, 6, 0.5f, GameSettings.MasterDb, v => { GameSettings.MasterDb = v; GameSettings.ApplyVideoAudio(); });
            Slider(box, "Фон (ветер, птицы, прибой), дБ", -40, 6, 0.5f, Tuning.AmbientVolumeDb, v => { Tuning.AmbientVolumeDb = v; Tuning.Save(); });
            Slider(box, "Шаги, дБ", -30, 6, 0.5f, Tuning.StepsVolumeDb, v => { Tuning.StepsVolumeDb = v; Tuning.Save(); });
            if (SteamHrtf.Ready) Check(box, "Объёмный звук (Steam Audio HRTF)", Tuning.SteamHrtf, on => { Tuning.SteamHrtf = on; Tuning.Save(); });

            // ---- интерфейс ----
            Section(box, "Интерфейс (после перезапуска)");
            Options(box, "Тема", new[] { "обычная", "Графит", "Тёплый", "Контраст" }, new[] { 0, 1, 2, 3 }, " abc".IndexOf(GameSettings.Theme is "" ? " " : GameSettings.Theme), v => GameSettings.Theme = v == 0 ? "" : " abc"[v].ToString());
            Options(box, "Раскладка", new[] { "обычная", "Моноблок", "Створки", "Зеркало" }, new[] { 0, 1, 2, 3 }, GameSettings.Layout is "" ? 0 : int.Parse(GameSettings.Layout), v => GameSettings.Layout = v == 0 ? "" : v.ToString());
        }

        void Section(VBoxContainer box, string name) { box.AddChild(UiTheme.Sep()); box.AddChild(UiTheme.Caption(name)); }

        void Check(VBoxContainer box, string text, bool on, Action<bool> set)
        {
            var c = new CheckBox { Text = text, ButtonPressed = on, FocusMode = FocusModeEnum.None };
            c.Toggled += v => { set(v); GameSettings.Save(); };
            box.AddChild(c);
        }

        void Slider(VBoxContainer box, string name, float min, float max, float step, float val, Action<float> set)
        {
            var row = UiTheme.HBox(14);
            var l = UiTheme.Small(name, UiTheme.Body, UiTheme.FontSmall);
            l.CustomMinimumSize = new Vector2(230, 0);
            var v = UiTheme.Strong(val.ToString("0.0"), UiTheme.FontSmall);
            v.CustomMinimumSize = new Vector2(48, 0); v.HorizontalAlignment = HorizontalAlignment.Right;
            var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = val, CustomMinimumSize = new Vector2(220, 24), FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            s.ValueChanged += x => { set((float)x); v.Text = ((float)x).ToString("0.0"); GameSettings.Save(); };
            row.AddChild(l); row.AddChild(s); row.AddChild(v);
            box.AddChild(row);
        }

        void Options(VBoxContainer box, string name, string[] labels, int[] values, int cur, Action<int> set)
        {
            var row = UiTheme.HBox(14);
            var l = UiTheme.Small(name, UiTheme.Body, UiTheme.FontSmall);
            l.CustomMinimumSize = new Vector2(230, 0);
            var o = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            for (int i = 0; i < labels.Length; i++) o.AddItem(labels[i], i);
            int sel = Array.IndexOf(values, cur);
            o.Selected = sel < 0 ? 0 : sel;
            o.ItemSelected += i => { set(values[i]); GameSettings.Save(); };
            row.AddChild(l); row.AddChild(o);
            box.AddChild(row);
        }

        public void Open() { _wipeArmed = false; _wipe.Text = "Новый вайп"; Visible = true; }
        public void Close() { Visible = false; Closed?.Invoke(); }
    }
}
