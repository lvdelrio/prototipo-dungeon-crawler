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

        // Las 2 secuencias fijas de 3 teclas del QTE para la habilidad de este personaje. Al usar
        // la habilidad se elige una de las dos al azar para presentarle al jugador.
        public QteKey[] SkillSequenceA = new QteKey[0];
        public QteKey[] SkillSequenceB = new QteKey[0];

        public bool IsGuarding;

        // Habilidad especial del Protector: mientras este activa, todos los ataques enemigos de
        // esta ronda se redirigen a el (a costa de recibir el, y solo el, todo ese dano).
        public bool CanProtectAll;
        public bool IsProtectingAll;

        public bool IsAlive => HP > 0;
    }
}
