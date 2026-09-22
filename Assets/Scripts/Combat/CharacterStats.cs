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

        // Evasion: chance (0-100, ver CombatEngine.EvasionToChance) de esquivar POR COMPLETO un
        // golpe enemigo antes de calcular dano -- ver CombatEngine.ExecuteEnemyAction. Tambien resta
        // peso en la formula de agro (PickAggroTarget): mas evasion, un poco menos probable ser el
        // blanco, no solo "mas dificil de acertar" una vez elegido.
        public int Evasion;

        // Suerte: chance (0-100) de golpe critico EN LOS PROPIOS ataques/habilidades de dano (ver
        // CombatEngine.LuckToCritChance) -- x1.5 de dano cuando sale.
        public int Luck;

        // Ataque magico: stat separada de Attack (fisico). Las clases con SkillUsesMagicAttack
        // (Alquimista, Mago, Medic, Trovador -- ver PartyFactory) escalan su habilidad con esta en
        // vez de con Attack; el resto la tiene en 0 y no la usa para nada.
        public int MagicAttack;
        public bool SkillUsesMagicAttack;

        public Element AttackElement;

        public string SkillName = "";
        public int SkillTpCost;
        public float SkillPower;
        public Element SkillElement;
        public bool IsHealSkill;
        public int HealAmount;

        // Alquimista: tanto el ataque basico como la habilidad pegan a TODOS los enemigos vivos a
        // la vez (ver CombatEngine.AoeDamageMultiplier para el descuento de dano por objetivo que
        // lo compensa).
        public bool AttacksAreAoe;

        // Berserker: la habilidad es una POSTURA propia (sin objetivo) que se activa/desactiva --
        // mientras IsEnraged este activo, EffectiveAttack sube y EffectiveDefense baja (ver
        // CombatEngine.EnrageAttackBonus/EnrageDefensePenalty).
        public bool IsSelfStanceSkill;
        public bool IsEnraged;

        // Trovador: la habilidad es VERSATIL -- usada sobre un aliado lo buffea (AttackBuffAmount),
        // usada sobre un enemigo lo debuffea (EnemyStats.DefenseDebuffAmount). Se elige el objetivo
        // (aliado o enemigo) al tirarla, ver PartyAction.TargetIsAlly.
        public bool IsVersatileBuffSkill;

        // Buff/debuff temporal (rondas completas, se descuenta en CombatEngine.BuildTurnOrder) que
        // se suma directo sobre Attack/Defense via EffectiveAttack/EffectiveDefense -- nunca se
        // tocan los stats base, asi que expira solo sin acumular error de redondeo.
        public int AttackBuffAmount;
        public int AttackBuffRoundsLeft;

        private const int EnrageAttackBonus = 6;
        private const int EnrageDefensePenalty = 4;

        public int EffectiveAttack => Attack + AttackBuffAmount + (IsEnraged ? EnrageAttackBonus : 0);
        public int EffectiveDefense => Defense - (IsEnraged ? EnrageDefensePenalty : 0);

        // Gunner: balas elementales opcionales (arranca con 5 de cada una, ver PartyFactory). Mientras
        // LoadedBulletElement no sea None Y quede stock de ese tipo, tanto el ataque basico como la
        // habilidad pegan con ESE elemento (se gasta 1 bala por uso) en vez del propio de siempre.
        public int FireBullets;
        public int IceBullets;
        public int VoltBullets;
        public Element LoadedBulletElement = Element.None;

        // Las 2 secuencias fijas de 3 teclas del QTE para la habilidad de este personaje. Al usar
        // la habilidad se elige una de las dos al azar para presentarle al jugador.
        public QteKey[] SkillSequenceA = new QteKey[0];
        public QteKey[] SkillSequenceB = new QteKey[0];

        public bool IsGuarding;

        // Formacion: 3 personajes adelante y 3 atras (ver CombatEngine.FrontRowAggroWeight). Los
        // de adelante concentran mas probabilidad de ser el blanco de los enemigos.
        public bool IsFrontRow;

        // Habilidad especial del Protector: mientras este activa, todos los ataques enemigos de
        // esta ronda se redirigen a el (a costa de recibir el, y solo el, todo ese dano).
        public bool CanProtectAll;
        public bool IsProtectingAll;

        public bool IsAlive => HP > 0;
    }
}
