namespace Rastvordd.Sim
{
    public sealed partial class World
    {
        /// <summary>
        /// Броня: доля урона пулями в торс, которую срезает надетая одежда (0..1). ЗАГЛУШКА — одежды пока нет, всегда 0.
        /// Сюда подключить систему одежды; значение уходит в SelfInfo.Armor и показывается в HUD («Броня N%»).
        /// </summary>
        public float ArmorReduction(Actor a) => 0f;
    }
}
