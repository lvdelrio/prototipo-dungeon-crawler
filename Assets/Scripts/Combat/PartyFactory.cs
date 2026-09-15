using System.Collections.Generic;

namespace Combat
{
    // Party fija de 6 personajes (sin niveles/arbol de habilidades todavia - eso queda para despues).
    public static class PartyFactory
    {
        public static List<CharacterStats> CreateDefaultParty()
        {
            return new List<CharacterStats>
            {
                new CharacterStats
                {
                    Name = "Warrior", Class = CharacterClass.Warrior,
                    MaxHP = 45, HP = 45, MaxTP = 10, TP = 10,
                    Attack = 12, Defense = 5, Speed = 5,
                    AttackElement = Element.Slash,
                    SkillName = "Corte Poderoso", SkillTpCost = 4, SkillPower = 1.6f, SkillElement = Element.Slash,
                },
                new CharacterStats
                {
                    Name = "Protector", Class = CharacterClass.Protector,
                    MaxHP = 55, HP = 55, MaxTP = 8, TP = 8,
                    Attack = 8, Defense = 9, Speed = 4,
                    AttackElement = Element.Strike,
                    SkillName = "Golpe Escudo", SkillTpCost = 4, SkillPower = 1.4f, SkillElement = Element.Strike,
                },
                new CharacterStats
                {
                    Name = "Ranger", Class = CharacterClass.Ranger,
                    MaxHP = 38, HP = 38, MaxTP = 10, TP = 10,
                    Attack = 11, Defense = 4, Speed = 8,
                    AttackElement = Element.Pierce,
                    SkillName = "Tiro Certero", SkillTpCost = 5, SkillPower = 1.7f, SkillElement = Element.Pierce,
                },
                new CharacterStats
                {
                    Name = "Alchemist", Class = CharacterClass.Alchemist,
                    MaxHP = 32, HP = 32, MaxTP = 16, TP = 16,
                    Attack = 7, Defense = 3, Speed = 5,
                    AttackElement = Element.Fire,
                    SkillName = "Bola de Fuego", SkillTpCost = 6, SkillPower = 1.8f, SkillElement = Element.Fire,
                },
                new CharacterStats
                {
                    Name = "Mage", Class = CharacterClass.Mage,
                    MaxHP = 32, HP = 32, MaxTP = 16, TP = 16,
                    Attack = 7, Defense = 3, Speed = 5,
                    AttackElement = Element.Ice,
                    SkillName = "Lanza de Hielo", SkillTpCost = 6, SkillPower = 1.8f, SkillElement = Element.Ice,
                },
                new CharacterStats
                {
                    Name = "Medic", Class = CharacterClass.Medic,
                    MaxHP = 40, HP = 40, MaxTP = 20, TP = 20,
                    Attack = 6, Defense = 4, Speed = 6,
                    AttackElement = Element.Strike,
                    SkillName = "Curacion", SkillTpCost = 6, SkillPower = 0f, SkillElement = Element.None,
                    IsHealSkill = true, HealAmount = 25,
                },
            };
        }
    }
}
