namespace Combat
{
    public class EnemyStats
    {
        public string Name = "";
        public int MaxHP;
        public int HP;
        public int Attack;
        public int Defense;
        public int Speed;
        public Element AttackElement;
        public Element Weakness;
        public Element Resistance;

        public bool IsAlive => HP > 0;
    }
}
