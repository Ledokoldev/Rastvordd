using System.Collections.Generic;
using System.Linq;
using Godot;
using Rastvordd.Sim;
using SVec = System.Numerics.Vector2;

namespace Rastvordd.Client
{
    /// <summary>
    /// Полигоны (--sandbox=aim|sound|build, docs/design/18-sandbox.md). Отдельный файл Main: хуки в Main.cs — по строке.
    /// Сохранение игрока не трогается: любой флаг, кроме --bots=, выключает его (Main._saveEnabled).
    ///  • aim — стрельбище (мишени на 10/25/50/100 м, бегущие и выглядывающие) и квадрат с укрытиями для боя с ботами;
    ///  • sound — звуковое поле (SandboxSound.cs): выстрелы по типам на дистанции и с направления, шаги «призрака», викторина направления;
    ///  • build — ровная площадка со своим шкафом, бесконечные ресурсы, мгновенный крафт, без гниения, всегда день.
    /// </summary>
    public partial class Main
    {
        string _sbMode;
        O42LiftView _sbLift;
        float _sbLiftIdle = 2;
        SandboxPanel _sbPanel;
        SandboxMarks _sbMarks;
        SVec _sbC, _sbSpawn;          // центр площадки, точка появления
        float _sbClearT, _sbRefillT;
        bool SbOn => _sbMode != null;
        /// <summary>ЛКМ не стреляет: панель развёрнута (клики по кнопкам) или звуковой полигон (там ЛКМ — ответ викторины).</summary>
        bool SbBlocksFire => _sbPanel != null && (_sbPanel.Expanded || _sbMode == "sound");
        readonly System.Random _sbRng = new(4242);

        // ---------- стрельбище ----------
        SVec _sbSquare, _sbLane;      // центр квадрата боя; огневой рубеж стрельбища
        int _sbSub;                   // 0 — мишени, 1 — боты
        sealed class SbTarget
        {
            public Actor A; public string Name; public SVec Home; public int Kind;   // 0 — стоит, 1 — бегает боком, 2 — выглядывает
            public float Amp, Speed, Dist; public SVec Out;
            public int Hits, Heads, Kills; public float Dmg, Life, FirstHit = -1, BestTtk = -1, LastTtk = -1, Flash;
        }
        readonly List<SbTarget> _sbTargets = new();
        int _sbShots, _sbPellets, _sbHits, _sbHeads, _sbKills, _sbDeaths, _sbBotKills;
        readonly Queue<(SVec pos, string text, Color c)> _sbFloat = new();
        bool _sbFlash, _sbFlashHead;
        bool _sbGod, _sbInfAmmo = true;
        int _sbArmor = 2;             // моя броня: 0 — голый, 1 — ватник, 2 — армейская, 4 — СОБР
        float _sbDeadAt = -1;

        sealed class SbBot { public Actor A; public IBrain Brain; public bool Guard; public bool Counted; public string Gun; }
        readonly List<SbBot> _sbBots = new();
        int _sbBotKind;               // 0 — боты-игроки, 1 — охрана РТ
        int _sbBotTier = 2;           // 1..4, 0 — случайно

        static readonly string[][] SbTierGuns =
        {
            new[] { "weapon_nagant", "weapon_homesmg", "weapon_pipeshotgun", "weapon_sawnoff", "weapon_bow" },
            new[] { "weapon_grach", "weapon_pps43", "weapon_svt40", "weapon_mp133" },
            new[] { "weapon_akm", "weapon_an94", "weapon_ppsh", "weapon_svd" },
            new[] { "weapon_saiga12", "weapon_mosin", "weapon_aps" },
        };
        static readonly string[] SbBotNames = { "Сёма", "Жека", "Толян", "Вован", "Лёха", "Димон", "Серёга", "Колян", "Витёк", "Саня", "Гоша", "Миха" };
        static readonly (string id, int n)[] SbAmmo = { ("ammo_762", 256), ("ammo_545", 256), ("ammo_9mm", 256), ("ammo_12g", 128), ("arrow", 64), ("ammo_bolt", 64), ("bandage", 10) };   // 10 ячеек рюкзака: одежда и стволы со стойки тоже влезут

        // ---------- стройка ----------
        bool _sbInstantCraft = true;
        Deployable _sbClaim;
        SVec _sbBuildC;               // стрельбище: центр стройплощадки
        bool _sbAimBuild; float _sbBuildT;             // стрельбище: сейчас набор строителя (F3)

        /// <summary>До того, как клиент возьмёт карту: место, укрытия (стены карты), мишени. Ботов-игроков вайпа нет.</summary>
        void SandboxSetup()
        {
            if (_sbMode == null) return;
            var w = _server.World;
            _server.BotCount = 0;
            w.DamageHook = SbDamageHook;
            var me = w.Find(_myId);
            switch (_sbMode)
            {
                case "aim":
                {
                    // квадрат боя 60×60 и справа от него дорожка стрельбища на 100+ м к северу (вверх по экрану)
                    _sbC = w.FindSandboxSpot(new SVec(-94, -112), new SVec(66, 34));
                    _sbSquare = _sbC;
                    _sbLane = _sbC + new SVec(50, 26);
                    w.SandboxClear(_sbC + new SVec(-34, -112), _sbC + new SVec(66, 34), true);
                    // стройплощадка 52×60 слева от квадрата боя (F3 — набор строителя и туда)
                    _sbBuildC = _sbC + new SVec(-66, 0);
                    w.SandboxClear(_sbBuildC + new SVec(-26, -30), _sbBuildC + new SVec(26, 30), true);
                    BuildArenaCover();
                    SpawnTargets();
                    _sbSpawn = _sbLane;
                    break;
                }
                case "lift":
                {
                    _sbC = w.FindSandboxSpot(new SVec(-40, -40), new SVec(40, 40));
                    w.SandboxClear(_sbC + new SVec(-40, -40), _sbC + new SVec(40, 40), true);
                    _sbSpawn = _sbC + new SVec(-4, 0);
                    if (!OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--zoom="))) _zoom = 1.5f;
                    break;
                }
                case "sound":
                {
                    _sbC = w.FindSandboxSpot(new SVec(-40, -40), new SVec(40, 40));
                    w.SandboxClear(_sbC + new SVec(-40, -40), _sbC + new SVec(40, 40), true);
                    // стена, за которую можно зайти самому: окклюзия по-настоящему, а не только галочкой
                    // (сзади-справа, между линиями 8 направлений панели — в таблице «за стеной» только по галочке)
                    w.SandboxWall(_sbC + new SVec(2, 13.8f), _sbC + new SVec(9, 14.2f), 2.6f, WallKind.Concrete);
                    _sbSpawn = _sbC;
                    break;
                }
                case "build":
                {
                    _sbC = w.FindSandboxSpot(new SVec(-36, -36), new SVec(36, 36));
                    w.SandboxClear(_sbC + new SVec(-36, -36), _sbC + new SVec(36, 36), true);
                    _sbSpawn = _sbC + new SVec(0, 3);
                    break;
                }
                default:
                    GD.PrintErr($"sandbox: неизвестный режим {_sbMode} (aim, sound, build, lift)");
                    _sbMode = null;
                    w.DamageHook = null;
                    return;
            }
            me.Pos = _sbSpawn;
            me.Vel = SVec.Zero;
            World.SandboxKeepAlive(me);
            w.SandboxHoldNoon();
            if (_sbMode == "aim") SbGiveKit();
            if (_sbMode == "build")
            {
                World.SandboxLearnAll(me);
                me.ClearAll();
                w.GiveBuilderKit(me);
                for (int k = 0; k < 9 && _sbClaim == null; k++)
                    _sbClaim = w.SandboxClaim(me, _sbC + new SVec(1.5f + (k % 3) * 2, 0.5f + (k / 3) * 2), out var why);
                if (_sbClaim == null) GD.PrintErr("sandbox: шкаф не встал");
                SbRefillBuild();
            }
            if (_sbMode != "aim") w.SandboxDress(me, 1);   // не мёрзнуть (значок «холодно» мешает)
            GD.Print($"sandbox: режим {_sbMode}, центр ({_sbC.X:0};{_sbC.Y:0}), я ({me.Pos.X:0.0};{me.Pos.Y:0.0}){(_sbClaim != null ? $", шкаф ({_sbClaim.Pos.X:0.0};{_sbClaim.Pos.Y:0.0})" : "")}");
        }

        /// <summary>Укрытия квадрата: бетонные блоки (низкие), стены (высокие), ящики, домик 6×5 с проёмом.</summary>
        void BuildArenaCover()
        {
            var w = _server.World;
            var c = _sbSquare;
            void Box(float x, float y, float sx, float sy, float h, WallKind k) => w.SandboxWall(c + new SVec(x - sx / 2, y - sy / 2), c + new SVec(x + sx / 2, y + sy / 2), h, k);
            // домик: север, запад, восток целиком, юг — с проёмом 1,6 м
            const float t = 0.3f, H = 2.6f;
            Box(-11, -14, 6, t, H, WallKind.Concrete);
            Box(-14, -11.5f, t, 5, H, WallKind.Concrete);
            Box(-8, -11.5f, t, 5, H, WallKind.Concrete);
            Box(-12.6f, -9, 2.8f, t, H, WallKind.Concrete);
            Box(-8.6f, -9, 1.2f, t, H, WallKind.Concrete);
            // низкие бетонные блоки (из-за них — присед)
            foreach (var (x, y, horiz) in new[] { (0f, -6f, true), (8f, 4f, false), (-4f, 10f, true), (16f, -14f, true), (-20f, 6f, false), (20f, 14f, true), (4f, -20f, true), (-2f, 18f, true), (-24f, -14f, true) })
                Box(x, y, horiz ? 2.2f : 0.8f, horiz ? 0.8f : 2.2f, 1.1f, WallKind.Barrier);
            // высокие стены
            Box(10, -4, 0.4f, 5, H, WallKind.Concrete);
            Box(-6, 0, 5, 0.4f, H, WallKind.Concrete);
            Box(22, 0, 0.4f, 6, H, WallKind.Concrete);
            Box(-20, -22, 6, 0.4f, H, WallKind.Concrete);
            Box(14, -24, 4, 0.4f, H, WallKind.Concrete);
            // ящики
            foreach (var (x, y) in new[] { (12f, 18f), (13.3f, 18f), (12.6f, 16.8f), (-16f, 18f), (-26f, -4f), (26f, -24f), (2f, 6f) })
                Box(x, y, 1.2f, 1.2f, 1.3f, WallKind.WoodCrate);
            // стена-укрытие выглядывающей мишени на стрельбище
            var l = _sbLane;
            w.SandboxWall(l + new SVec(3, -28.2f), l + new SVec(7, -27.8f), 2.6f, WallKind.Concrete);
        }

        void SpawnTargets()
        {
            var w = _server.World;
            var l = _sbLane;
            SbTarget T(string name, SVec at, int kind, float dist)
            {
                var a = w.SandboxActor(name, Team.Player, at);
                a.Facing = Mathf.Pi / 2;   // лицом к рубежу (на юг)
                var t = new SbTarget { A = a, Name = name, Home = at, Kind = kind, Dist = dist };
                _sbTargets.Add(t);
                return t;
            }
            foreach (float d in new[] { 10f, 25f, 50f, 100f }) T($"{d:0} м", l + new SVec(-4, -d), 0, d);
            T("бег 20 м", l + new SVec(6, -20), 1, 20).Amp = 3;
            var r = T("бег 40 м", l + new SVec(9, -40), 1, 40); r.Amp = 4; r.Speed = 1;
            var p = T("выгляд. 30 м", l + new SVec(5, -29.2f), 2, 30); p.Out = l + new SVec(8.8f, -29.2f);
        }

        void SbClearTargets()
        {
            foreach (var t in _sbTargets) _server.World.SandboxRemoveActor(t.A);
            _sbTargets.Clear();
        }

        /// <summary>Мой набор на стрельбище: 6 стволов на поясе, патроны всех калибров, бинты, броня по выбору.</summary>
        void SbGiveKit()
        {
            var w = _server.World; var me = w.Find(_myId);
            me.ClearAll();
            foreach (var g in new[] { "weapon_akm", "weapon_svd", "weapon_mp133", "weapon_pps43", "weapon_grach", "weapon_bow" })
                me.AddSlot(Items.NewSlot(g, Items.Get(g).Weapon.Magazine));
            me.Current = 0;
            SbTopUpAmmo(me);
            w.SandboxDress(me, _sbArmor);
            me.Hp = me.MaxHp;
        }

        static void SbTopUpAmmo(Actor me)
        {
            foreach (var (id, n) in SbAmmo) { int have = me.Count(id); if (have < n) me.AddUpTo(id, n - have); }
        }

        /// <summary>Ствол со стойки — в текущую ячейку пояса (старый — убрать), полный магазин.</summary>
        void SbRack(string gun)
        {
            var me = _server.World.Find(_myId);
            if (me == null || me.Dead) return;
            for (int i = 0; i < Actor.BeltSize; i++)
                if (me.Belt[i]?.Id == gun) { _slot = i; _hud.Toast($"{Items.Name(gun)} — ячейка {i + 1}"); return; }
            int at = me.Current;
            me.Belt[at] = Items.NewSlot(gun, Items.Get(gun).Weapon.Magazine);
            if (me.Belt[at].State != null) me.Belt[at].State.Ammo = me.Belt[at].State.MagSize;
            SbTopUpAmmo(me);
            _hud.Toast($"{Items.Name(gun)} — в ячейку {at + 1}");
        }

        void SbRefillMags(Actor me)
        {
            foreach (var s in me.Belt) if (s?.State is WeaponState st && !st.Def.Melee) { st.Ammo = st.MagSize; st.ReloadLeft = 0; }
            SbTopUpAmmo(me);
        }

        /// <summary>Перехват урона: мишени не умирают (урон копится до «убит» и сбрасывается), бессмертие — мне.</summary>
        bool SbDamageHook(Actor target, float dmg, int attackerId, string zone)
        {
            if (_sbMode == null) return false;
            var t = _sbTargets.Find(x => x.A == target);
            if (t != null)
            {
                var w = _server.World;
                bool head = zone is "head" or "helmet";
                if (attackerId == _myId) { _sbHits++; if (head) _sbHeads++; _sbFlash = true; _sbFlashHead |= head; }
                t.Hits++; if (head) t.Heads++;
                t.Dmg += dmg; t.Flash = 0.25f;
                if (t.FirstHit < 0) t.FirstHit = w.Time;
                t.Life += dmg;
                _sbFloat.Enqueue((t.A.Pos, $"-{dmg:0}{(head ? " в голову" : zone == "limb" ? " в ногу" : "")}", head ? UiTheme.Accent : new Color(1, 0.85f, 0.8f)));
                if (t.Life >= target.MaxHp)
                {
                    t.Kills++; if (attackerId == _myId) _sbKills++;
                    t.LastTtk = w.Time - t.FirstHit;
                    if (t.BestTtk < 0 || t.LastTtk < t.BestTtk) t.BestTtk = t.LastTtk;
                    _sbFloat.Enqueue((t.A.Pos + new SVec(0, -0.6f), $"УБИТ за {t.LastTtk:0.00} с", UiTheme.Good));
                    GD.Print($"sandbox: мишень {t.Name} убита за {t.LastTtk:0.00} с ({t.Hits} попаданий, в голову {t.Heads})");
                    t.Life = 0; t.FirstHit = -1;
                }
                return true;
            }
            if (target.Id == _myId && _sbGod) return true;
            // боты арены друг друга не ранят: все против тебя
            if (_sbBots.Exists(b => b.A == target) && _sbBots.Exists(b => b.A.Id == attackerId)) return true;
            return false;
        }

        /// <summary>Каждый тик мира, до ввода: без голода/холода, полдень, движение мишеней, боты, возрождение.</summary>
        void SandboxPreTick()
        {
            if (_sbMode == null) return;
            var w = _server.World;
            var me = w.Find(_myId);
            World.SandboxKeepAlive(me);
            w.SandboxHoldNoon();
            if ((_sbClearT -= World.Dt) <= 0)
            {
                // отросшие ресурсы, сумки с трупов — убрать с площадки
                _sbClearT = 3f;
                if (_sbMode == "aim") w.SandboxClear(_sbC + new SVec(-34, -112), _sbC + new SVec(66, 34));
                else w.SandboxClear(_sbC + new SVec(-40, -40), _sbC + new SVec(40, 40));
            }
            switch (_sbMode)
            {
                case "aim": AimTick(w, me); break;
                case "build": BuildTick(w, me); break;
                case "sound": SoundTick(w, me); break;
            }
        }

        void AimTick(World w, Actor me)
        {
            if (_sbAimBuild && (_sbBuildT -= World.Dt) <= 0) { _sbBuildT = 2f; SbRefillBuild(); }
            if ((_sbRefillT -= World.Dt) <= 0) { _sbRefillT = 1f; if (_sbInfAmmo && !me.Dead && !_sbAimBuild) SbTopUpAmmo(me); }
            // смерть — сразу снова на рубеже (через 1,5 с, чтобы увидеть)
            if (me.Dead)
            {
                if (_sbDeadAt < 0) { _sbDeadAt = w.Time; _sbDeaths++; }
                else if (w.Time - _sbDeadAt > 1.5f)
                {
                    me.Dead = false; me.Hp = me.MaxHp; me.Pos = _sbSub == 1 ? _sbSquare + new SVec(0, 26) : _sbLane; me.Vel = SVec.Zero;
                    SbGiveKit(); _sbAimBuild = false;
                    _sbDeadAt = -1;
                    _hud.Toast("Снова на полигоне");
                }
            }
            // мишени: стоят, бегают боком, выглядывают из-за стены
            float time = w.Time;
            foreach (var t in _sbTargets)
            {
                var a = t.A;
                a.Hp = a.MaxHp; a.Dead = false;
                World.SandboxKeepAlive(a);
                var cmd = InputCmd.Empty;
                cmd.Aim = a.Pos + new SVec(0, 5);   // смотрят на рубеж
                SVec goal = t.Home;
                if (t.Kind == 1)
                {
                    float period = t.Amp * 2 / (t.Speed > 0 ? World.SprintSpeed : World.WalkSpeed);
                    goal = t.Home + new SVec(((int)(time / period) % 2 == 0 ? 1 : -1) * t.Amp, 0);
                    cmd.Sprint = t.Speed > 0;
                    cmd.Aim = a.Pos + new SVec(goal.X > a.Pos.X ? 3 : -3, 2);   // бег — лицом по ходу (иначе спринт не включится)
                }
                else if (t.Kind == 2)
                {
                    float ph = time % 4.5f;
                    goal = ph > 2.5f && ph < 4f ? t.Out : t.Home;
                }
                var d = goal - a.Pos;
                cmd.Move = d.Length() > 0.15f ? SVec.Normalize(d) : SVec.Zero;
                if (t.Kind == 0 && d.Length() > 0.4f) cmd.Move = SVec.Normalize(d) * 0.5f;
                _server.Submit(a.Id, cmd);
                if (t.Flash > 0) t.Flash -= World.Dt;
            }
            // боты: убитых — считать и убрать через 3 с
            for (int i = _sbBots.Count - 1; i >= 0; i--)
            {
                var b = _sbBots[i];
                World.SandboxKeepAlive(b.A);
                if (!b.A.Dead) continue;
                if (!b.Counted)
                {
                    b.Counted = true;
                    if (b.A.LastAttacker == _myId) { _sbBotKills++; GD.Print($"sandbox: убит бот {b.A.Name} ({Items.Name(b.Gun)})"); }
                }
                if (w.Time - b.A.DeadTime > 3f) { SbRemoveBot(b); _sbBots.RemoveAt(i); }
            }
        }

        void SbRemoveBot(SbBot b)
        {
            _server.RemoveBrain(b.A.Id);
            _server.World.SandboxRemoveActor(b.A);
        }

        /// <summary>Выпустить n ботов на северный край квадрата (боты-игроки или охрана РТ) с оружием выбранного тира.</summary>
        void SbSpawnBots(int n)
        {
            var w = _server.World;
            for (int k = 0; k < n; k++)
            {
                int tier = _sbBotTier > 0 ? _sbBotTier : _sbRng.Next(1, 5);
                var guns = SbTierGuns[tier - 1];
                string gun = guns[_sbRng.Next(guns.Length)];
                bool guard = _sbBotKind == 1;
                var at = _sbSquare + new SVec(-20 + (float)_sbRng.NextDouble() * 40, guard ? -14 + (float)_sbRng.NextDouble() * 20 : -26 + (float)_sbRng.NextDouble() * 4);
                for (int tries = 0; tries < 20 && !w.IsFree(at, 0.6f); tries++) at += new SVec(1.3f, 0.7f);
                string name = (guard ? "Охранник " : "") + SbBotNames[(_sbBots.Count + k + _sbBotKills) % SbBotNames.Length] + (guard ? "" : " (бот)");
                var a = w.SandboxActor(name, guard ? Team.Npc : Team.Player, at);
                var wd = Items.Get(gun).Weapon;
                a.Belt[0] = Items.NewSlot(gun, wd.Magazine); a.Current = 0;
                a.Add(wd.AmmoId, 400);
                a.Add("bandage", 4);
                w.SandboxDress(a, tier switch { 1 => 1, 4 => 4, _ => 2 });
                a.Hp = a.MaxHp;
                a.Facing = Mathf.Pi / 2;
                IBrain brain = guard ? new NpcBrain(a, w, _sbRng.Next()) : new ArenaBrain(a, w, _sbRng.Next(), () => w.Find(_myId));
                _server.AddBrain(brain);
                _sbBots.Add(new SbBot { A = a, Brain = brain, Guard = guard, Gun = gun });
            }
            _hud.Toast($"Выпущено {n}: {(_sbBotKind == 1 ? "охрана РТ" : "боты-игроки")}, тир {(_sbBotTier == 0 ? "случайный" : _sbBotTier.ToString())}");
        }

        void SbClearBots()
        {
            foreach (var b in _sbBots) SbRemoveBot(b);
            _sbBots.Clear();
        }

        /// <summary>Мишени ↔ боты: телепорт на рубеж / в квадрат.</summary>
        void SbSetSub(int sub)
        {
            _sbSub = sub;
            var me = _server.World.Find(_myId);
            if (sub == 0) { SbClearBots(); if (_sbTargets.Count == 0) SpawnTargets(); me.Pos = _sbLane; }
            else { SbClearTargets(); me.Pos = _sbSquare + new SVec(0, 26); }
            me.Vel = SVec.Zero;
            SbFillPanel();
        }

        void SbResetStats()
        {
            _sbShots = _sbPellets = _sbHits = _sbHeads = _sbKills = _sbDeaths = _sbBotKills = 0;
            foreach (var t in _sbTargets) { t.Hits = t.Heads = t.Kills = 0; t.Dmg = t.Life = 0; t.FirstHit = t.BestTtk = t.LastTtk = -1; }
            _hud.Toast("Счётчики сброшены");
        }

        void BuildTick(World w, Actor me)
        {
            if (_sbInstantCraft) foreach (var j in me.CraftQueue) j.Left = 0;
            if ((_sbRefillT -= World.Dt) <= 0) { _sbRefillT = 2f; SbRefillBuild(); }
            if (me.Dead)
            {
                me.Dead = false; me.Hp = me.MaxHp; me.Pos = _sbSpawn;
                w.GiveBuilderKit(me);
            }
        }

        /// <summary>Ресурсы — до 5000, шкаф с запасом на сутки и без отсрочки гниения.</summary>
        void SbRefillBuild()
        {
            var w = _server.World; var me = w.Find(_myId);
            if (me == null || me.Dead) return;
            foreach (var (id, n) in new[] { ("wood", 3000), ("stone", 3000), ("metal_fragments", 2000), ("scrap", 500), ("cloth", 100), ("lock_code", 4) })
            {
                int have = me.Count(id);
                if (have < n) me.AddUpTo(id, n - have);   // только сколько влезет: без сумок на землю
            }
            foreach (var d in w.Deployables)
                if (d.Kind == DeployKind.Claim && d.Auth.Contains(_myId))
                {
                    d.Grace = 1800f;
                    foreach (var id in new[] { "wood", "stone", "metal_fragments" }) d.Store[id] = 100000;
                }
        }

        void SbResetBuild()
        {
            int n = _server.World.SandboxResetBuild();
            var me = _server.World.Find(_myId);
            me.Pos = _sbSpawn;
            _hud.Toast($"Площадка очищена: убрано {n}");
        }

        // ---------- клиент: панель, события, метки ----------

        /// <summary>В конце _Ready: панель и метки на земле.</summary>
        void SandboxReady()
        {
            if (_sbMode == null) return;
            _sbPanel = new SandboxPanel(_sbMode switch { "aim" => "Полигон — стрельба", "sound" => "Полигон — звук", "lift" => "Полигон — лифт", _ => "Полигон — стройка" }) { Position = new Vector2(16, 56) };
            _sbPanel.Toggled += () => Input.MouseMode = _sbPanel.Expanded || UiShown || _hud.CraftOpen || _hud.AdminOpen ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Hidden;
            _hud.AddChild(_sbPanel);
            _sbMarks = new SandboxMarks { ZIndex = -8 };
            AddChild(_sbMarks);
            if (_sbMode == "lift")
            {
                _sbLift = new O42LiftView { Position = Iso.Px(_sbC) };
                _world.AddChild(_sbLift);
                _sbLift.DoorSlammed += () => _shake = Mathf.Min(1.5f, _shake + 0.8f);
                _sbLift.StateChanged += state => GD.Print($"O42-LIFT: {state}");
            }
            SbFillPanel();
            DisplayServer.WindowSetTitle($"Раствордд — {(_sbMode switch { "aim" => "полигон: стрельба", "sound" => "полигон: звук", "lift" => "полигон: лифт", _ => "полигон: стройка" })}");
            _hud.Toast(_sbMode switch
            {
                "aim" => "Полигон: стрельба. F2 — панель (мишени, боты, стойка оружия). Сохранение не трогается.",
                "sound" => "Полигон: звук. F2 — панель. Наушники! Сохранение не трогается.",
                "lift" => "Полигон: лифт. F3 — начать заново, F2 — панель. Посадка 15 с.",
                _ => "Полигон: стройка. Свой шкаф, ресурсы бесконечные, крафт мгновенный. F2 — панель.",
            });
        }

        void SbFillPanel()
        {
            if (_sbPanel == null) return;
            _sbPanel.Clear();
            switch (_sbMode)
            {
                case "aim": SbFillAim(); break;
                case "sound": SbFillSound(); break;
                case "build": SbFillBuild(); break;
                case "lift":
                    _sbPanel.Note("Объект 42: предупреждение 10 с, открытие 1,5 с, посадка 15 с, закрытие и спуск 3 с. Цикл повторяется после паузы 2 с.");
                    _sbPanel.Buttons(("Начать заново (F3)", () => _sbLift.Start(15)));
                    break;
            }
        }

        void SbFillAim()
        {
            var p = _sbPanel;
            p.Caption("Режим");
            p.Buttons((_sbAimBuild ? "К стрельбе (F3)" : "На стройплощадку (F3)", () => { SbAimToggleBuild(); SbFillPanel(); }));
            p.Choice(new[] { "Мишени", "Боты" }, _sbSub, SbSetSub);
            if (_sbSub == 0)
            {
                p.Caption("Мишени");
                p.Note("Стоят на 10, 25, 50 и 100 м (слева), бегают боком на 20 и 40 м, выглядывают из-за стены на 30 м. Мишени не умирают: 100 урона — «убит», время до убийства.");
                p.Buttons(("Сбросить счётчики", SbResetStats));
            }
            else
            {
                p.Caption("Кто");
                p.Choice(new[] { "Боты-игроки", "Охрана РТ" }, _sbBotKind, k => _sbBotKind = k);
                p.Caption("Оружие ботов");
                p.Choice(new[] { "Случайно", "Тир 1", "Тир 2", "Тир 3", "Тир 4" }, _sbBotTier, k => _sbBotTier = k);
                p.Buttons(("+1 бот", () => SbSpawnBots(1)), ("+3", () => SbSpawnBots(3)), ("+5", () => SbSpawnBots(5)), ("Убрать ботов", SbClearBots));
                p.Toggle("Бессмертие (урон ботов выключен)", _sbGod, v => { _sbGod = v; _hud.Toast(v ? "Бессмертие: вкл" : "Бессмертие: выкл"); });
                p.Buttons(("Сбросить счёт", SbResetStats));
            }
            p.Caption("Я");
            p.Buttons(("Восстановить HP и броню", () =>
            {
                var me = _server.World.Find(_myId);
                if (me.Dead) return;
                me.Hp = me.MaxHp; _server.World.SandboxDress(me, _sbArmor); _hud.Toast("HP и броня восстановлены");
            }), ("Патроны и магазины", () => SbRefillMags(_server.World.Find(_myId))));
            p.Caption("Моя броня");
            p.Choice(new[] { "Голый", "Ватник/кожа", "Армейская", "СОБР" }, _sbArmor switch { 0 => 0, 1 => 1, 4 => 3, _ => 2 }, k =>
            {
                _sbArmor = k switch { 0 => 0, 1 => 1, 3 => 4, _ => 2 };
                _server.World.SandboxDress(_server.World.Find(_myId), _sbArmor);
            });
            p.Toggle("Бесконечные патроны", _sbInfAmmo, v => _sbInfAmmo = v);
            p.Caption("Стойка: ствол в текущую ячейку (1–6 — ячейки пояса)");
            var guns = new List<(string, System.Action)>();
            foreach (var g in Weaponry.GunsByPower) { var id = g; guns.Add((Items.Name(id), () => SbRack(id))); }
            p.Buttons(guns.ToArray());
            // пресеты: ствол + обвесы под рабочую дистанцию (D57) — быстро сравнить характер стволов
            p.Caption("Пресеты (ствол + обвесы)");
            var presets = new (string name, string gun, string[] atts)[]
            {
                ("ППШ — штурм в упор", "weapon_ppsh", new[] { "att_grip" }),
                ("ППС-43 + модуль бедра", "weapon_pps43", new[] { "att_hip_module" }),
                ("АКМ — средняя, коллиматор", "weapon_akm", new[] { "att_reddot", "att_compensator_army", "att_stock_tac" }),
                ("АН-94 — дальняя, 2×", "weapon_an94", new[] { "att_scope2x", "att_grip", "att_stock_tac" }),
                ("СВД — 4×", "weapon_svd", new[] { "att_scope4x", "att_compensator_army" }),
                ("Мосинка — 4×", "weapon_mosin", new[] { "att_scope4x" }),
                ("Сайга — в упор", "weapon_saiga12", new[] { "att_reddot" }),
            };
            var pb = new List<(string, System.Action)>();
            foreach (var pr in presets) { var x = pr; pb.Add((x.name, () => SbPreset(x.gun, x.atts))); }
            p.Buttons(pb.ToArray());
        }

        /// <summary>F3 на стрельбище: стрелковый набор ↔ набор строителя (шкаф на стройплощадке, ресурсы пополняются).</summary>
        void SbAimToggleBuild()
        {
            var w = _server.World; var me = w.Find(_myId);
            if (me == null) return;
            _sbAimBuild = !_sbAimBuild;
            if (_sbAimBuild)
            {
                World.SandboxLearnAll(me);
                me.ClearAll();
                w.GiveBuilderKit(me);
                for (int k = 0; k < 9 && _sbClaim == null; k++)
                    _sbClaim = w.SandboxClaim(me, _sbBuildC + new SVec(1.5f + (k % 3) * 2, 0.5f + (k / 3) * 2), out _);
                SbRefillBuild();
                w.SandboxDress(me, 1);
                me.Pos = _sbBuildC + new SVec(0, 3);
                _hud.Toast("Стройка: набор строителя, шкаф стоит, ресурсы пополняются. F3 — обратно к стрельбе");
            }
            else
            {
                SbGiveKit();
                me.Pos = _sbLane;
                _hud.Toast("Стрельба: стрелковый набор. F3 — на стройплощадку");
            }
            me.Vel = SVec.Zero;
            _slot = 0;
        }

        void SbPreset(string gun, string[] atts)
        {
            SbRack(gun);
            var me = _server.World.Find(_myId);
            if (me?.Belt[me.Current]?.State is not WeaponState st) return;
            for (int k = 0; k < st.Att.Length; k++) st.Att[k] = null;
            foreach (var at in atts) _server.World.Attach(me, st, at, out _);
            st.Ammo = st.MagSize;
        }

        void SbFillBuild()
        {
            var p = _sbPanel;
            p.Note("Свой шкаф уже стоит. Ресурсы пополняются сами, все рецепты изучены, без гниения и содержания, всегда день, ботов и рейдеров нет.");
            p.Buttons(("Набор строителя (F8)", () => _server.World.GiveBuilderKit(_server.World.Find(_myId))), ("Пополнить ресурсы", SbRefillBuild));
            p.Buttons(("Снести все постройки", SbResetBuild), ("Верстаки 1–3 и печь", () =>
            {
                var me = _server.World.Find(_myId);
                foreach (var id in new[] { "deploy_workbench", "deploy_workbench2", "deploy_workbench3", "deploy_furnace", "deploy_box" }) me.Add(id, 1);
                _hud.Toast("В рюкзак: верстаки 1–3, печь, ящик");
            }));
            p.Toggle("Мгновенный крафт", _sbInstantCraft, v => _sbInstantCraft = v);
            p.Buttons(("Вернуться к шкафу", () => _server.World.Find(_myId).Pos = _sbSpawn));
        }

        /// <summary>События тика (из HandleEvents): выстрелы — в счётчик.</summary>
        void SandboxEvent(SimEvent e)
        {
            if (_sbMode != "aim") return;
            if (e.Kind == SimEventKind.Shot && e.ActorId == _myId)
            {
                _sbShots++;
                _sbPellets += System.Math.Max(1, Items.Get(e.Text)?.Weapon?.Pellets ?? 1);
            }
        }

        /// <summary>Каждый кадр: сводка на панели, метки на земле, всплывающий урон, маркер попадания.</summary>
        void SandboxFrame(float dt)
        {
            if (_sbMode == null || _sbPanel == null) return;
            var vs = GetViewport().GetVisibleRect().Size;
            _sbPanel.Fit(vs.Y, _sbPanel.Position.Y);
            while (_sbFloat.Count > 0) { var (pos, text, c) = _sbFloat.Dequeue(); FloatText(pos, text, c); }
            if (_sbFlash) { _hud.Crosshair.HitFlash = _sbFlashHead ? 0.3f : 0.15f; _sbFlash = _sbFlashHead = false; }
            var me = _server.World.Find(_myId);
            _sbMarks.Clear();
            switch (_sbMode)
            {
                case "aim":
                {
                    float acc = _sbPellets > 0 ? 100f * _sbHits / _sbPellets : 0;
                    var sb = new System.Text.StringBuilder();
                    if (_sbSub == 0)
                    {
                        sb.Append($"Выстрелов {_sbShots} (дробин/пуль {_sbPellets}) · попаданий {_sbHits} · точность {acc:0}% · в голову {_sbHeads} · «убито» {_sbKills}\n");
                        foreach (var t in _sbTargets)
                            sb.Append($"  {t.Name}: {t.Hits} попад.{(t.Heads > 0 ? $" ({t.Heads} в голову)" : "")}{(t.Kills > 0 ? $" · убит {t.Kills}× · TTK {t.LastTtk:0.00} с, лучшее {t.BestTtk:0.00} с" : "")}\n");
                    }
                    else
                    {
                        int alive = _sbBots.FindAll(b => !b.A.Dead).Count;
                        sb.Append($"Убил ботов {_sbBotKills} · умер {_sbDeaths} · живых ботов {alive} · HP {me.Hp:0}{(_sbGod ? " · бессмертие" : "")}\n");
                        sb.Append($"Выстрелов {_sbShots} · попаданий по мишеням не считаем — бой\n");
                        foreach (var b in _sbBots)
                            sb.Append($"  {b.A.Name,-18} {Items.Name(b.Gun),-10} HP {(b.A.Dead ? "мёртв" : b.A.Hp.ToString("0"))}{(b.Brain is ArenaBrain ab ? " · " + ab.Doing : "")}\n");
                    }
                    _sbPanel.Info.Text = sb.ToString().TrimEnd();
                    foreach (var t in _sbTargets)
                        _sbMarks.Target(Iso.Px(t.Kind == 0 ? t.Home : t.A.Pos), $"{t.Name}{(t.Hits > 0 ? $" · {t.Hits}" : "")}", t.Flash > 0);
                    _sbMarks.Rect(Iso.Px(_sbSquare + new SVec(-30, -30)), Iso.Px(_sbSquare + new SVec(30, 30)));
                    _sbMarks.Line(Iso.Px(_sbLane + new SVec(-8, 0.8f)), Iso.Px(_sbLane + new SVec(14, 0.8f)), "огневой рубеж");
                    foreach (float d in new[] { 10f, 25f, 50f, 100f })
                        _sbMarks.Label(Iso.Px(_sbLane + new SVec(-10, -d)), $"{d:0} м");
                    break;
                }
                case "build":
                    _sbPanel.Info.Text = $"Построек {_server.World.Structures.Count} · установлено {_server.World.Deployables.Count(d => d.Kind != DeployKind.Claim && SVec.Distance(d.Pos, _sbC) < 50)} · дерево {me.Count("wood")} · камень {me.Count("stone")} · металл {me.Count("metal_fragments")}";
                    _sbMarks.Rect(Iso.Px(_sbC + new SVec(-36, -36)), Iso.Px(_sbC + new SVec(36, 36)));
                    break;
                case "lift":
                    if (_sbLift.State == O42LiftView.LiftState.Idle)
                    {
                        _sbLiftIdle -= dt;
                        if (_sbLiftIdle <= 0) { _sbLiftIdle = 2; _sbLift.Start(15); }
                    }
                    string phase = _sbLift.State switch
                    {
                        O42LiftView.LiftState.Idle => "Ожидание",
                        O42LiftView.LiftState.Warning => "Предупреждение",
                        O42LiftView.LiftState.Opening => "Открытие дверей",
                        O42LiftView.LiftState.Boarding => "Посадка",
                        _ => "Закрытие и спуск"
                    };
                    _sbPanel.Info.Text = $"Объект 42 · {phase} · {_sbLift.Remaining:0.0} с\nF3 — начать цикл заново · посадка в демо 15 с";
                    break;
                case "sound":
                    SoundFrame(dt);
                    break;
            }
            _sbMarks.QueueRedraw();
        }

        /// <summary>F2 — развернуть/свернуть панель; остальные клавиши режима (звук: пробел — ответ).</summary>
        bool SandboxKey(Key k)
        {
            if (_sbMode == null) return false;
            if (_sbMode == "lift" && k == Key.F3) { _sbLift.Start(15); return true; }
            if (_sbMode == "aim" && k == Key.F3) { SbAimToggleBuild(); return true; }
            if (k == Key.F2) { _sbPanel.SetExpanded(!_sbPanel.Expanded); return true; }
            if (_sbMode == "sound") return SoundKey(k);
            return false;
        }

        /// <summary>Клик мимо развёрнутой панели — свернуть (как админка). Звук: клик по миру — ответ викторины.</summary>
        bool SandboxClick(InputEventMouseButton mb)
        {
            if (_sbMode == null || _sbPanel == null || mb.ButtonIndex != MouseButton.Left) return false;
            bool onPanel = _sbPanel.GetGlobalRect().HasPoint(mb.GlobalPosition);
            if (onPanel) return false;
            if (_sbMode == "sound" && QuizClick()) return true;
            if (_sbPanel.Expanded && !UiTheme.HitsPanel(_hud, mb.GlobalPosition)) { _sbPanel.SetExpanded(false); return true; }
            return false;
        }
    }
}
