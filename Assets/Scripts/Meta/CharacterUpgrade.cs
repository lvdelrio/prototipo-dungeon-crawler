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
        public int MagicAttackLevel;
        public int EvasionLevel;
        public int LuckLevel;

        public int GetLevel(UpgradeStat stat)
        {
            switch (stat)
            {
                case UpgradeStat.Attack: return AttackLevel;
                case UpgradeStat.Defense: return DefenseLevel;
                case UpgradeStat.Speed: return SpeedLevel;
                case UpgradeStat.MaxHp: return MaxHpLevel;
                case UpgradeStat.MagicAttack: return MagicAttackLevel;
                case UpgradeStat.Evasion: return EvasionLevel;
                case UpgradeStat.Luck: return LuckLevel;
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
                case UpgradeStat.MagicAttack: MagicAttackLevel++; break;
                case UpgradeStat.Evasion: EvasionLevel++; break;
                case UpgradeStat.Luck: LuckLevel++; break;
                default: MaxTpLevel++; break;
            }
        }
    }
}
