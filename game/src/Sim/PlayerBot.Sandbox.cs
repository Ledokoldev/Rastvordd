using System;
using System.Numerics;

namespace Rastvordd.Sim
{
    /// <summary>
    /// Полигон «боты» (docs/design/18-sandbox.md): бот-игрок без хозяйства — только бой с одним противником
    /// той же логикой, что в вайпе (PlayerBot.Combat: укрытия, пики, перезарядка и бинт за укрытием, дожим, стрейф).
    /// На арене бот всегда примерно знает, где противник (иначе стоял бы без дела за стеной).
    /// </summary>
    public sealed partial class PlayerBot
    {
        public InputCmd ArenaThink(Actor foe)
        {
            var cmd = InputCmd.Empty;
            cmd.Aim = _me.Pos + _me.FacingDir * 3;
            if (_me.Dead || foe == null || foe.Dead || foe.Spectator)
            {
                if (_foe != null) ForgetFoe();
                Doing = _me.Dead ? "мёртв" : "ждёт";
                return cmd;
            }
            if (_foe != foe) NewFoe(foe);
            PerceiveFoe();
            if (_foe == null) NewFoe(foe);
            // не видит дольше 3 с — «слышит», где противник (арена маленькая)
            if (_w.Time - _foeSeenAt > 3f && _w.Time - _foeHeardAt > 3f) { _foeSeenPos = foe.Pos; _foeHeardAt = _w.Time; }
            Combat(foe, ref cmd);
            return cmd;
        }
    }

    /// <summary>Мозг бота арены: PlayerBot.ArenaThink против одной цели (игрока).</summary>
    public sealed class ArenaBrain : IBrain
    {
        readonly PlayerBot _bot;
        readonly Func<Actor> _foe;
        public ArenaBrain(Actor me, World w, int seed, Func<Actor> foe)
        {
            _bot = new PlayerBot(me, w, seed) { Archetype = BotArchetype.Roamer, Aggression = 0.8f };
            _foe = foe;
        }
        public Actor Me => _bot.Me;
        public string Doing => _bot.Doing;
        public InputCmd Think() => _bot.ArenaThink(_foe());
    }
}
