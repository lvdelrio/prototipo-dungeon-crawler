namespace Combat
{
    public class CharacterStats
    {
        public string Name = "";
        public CharacterClass Class;

        public int MaxHP;
        public int HP;
        public int MaxTP;
        public int TP;

        public int Attack;
        public int Defense;
        public int Speed;

        public Element AttackElement;

        public string SkillName = "";
        public int SkillTpCost;
        public float SkillPower;
        public Element SkillElement;
        public bool IsHealSkill;
        public int HealAmount;

        public bool IsGuarding;

        public bool IsAlive => HP > 0;
    }
}
