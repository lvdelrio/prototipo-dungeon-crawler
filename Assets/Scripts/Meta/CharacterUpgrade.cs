using System;
using Combat;

namespace Meta
{
    // Niveles comprados (permanentes, entre runs) para un personaje/clase en particular.
    [Serializable]
    public class CharacterUpgrade
    {
        public CharacterClass Class;
        public int AttackLevel;
        public int DefenseLevel;
        public int SpeedLevel;
        public int MaxHpLevel;
        public int MaxTpLevel;

        public int GetLevel(UpgradeStat stat)
        {
            switch (stat)
            {
                case UpgradeStat.Attack: return AttackLevel;
                case UpgradeStat.Defense: return DefenseLevel;
                case UpgradeStat.Speed: return SpeedLevel;
                case UpgradeStat.MaxHp: return MaxHpLevel;
                default: return MaxTpLevel;
            }
        }

        public void AddLevel(UpgradeStat stat)
        {
            switch (stat)
            {
                case UpgradeStat.Attack: AttackLevel++; break;
                case UpgradeStat.Defense: DefenseLevel++; break;
                case UpgradeStat.Speed: SpeedLevel++; break;
                case UpgradeStat.MaxHp: MaxHpLevel++; break;
                default: MaxTpLevel++; break;
            }
        }
    }
}
