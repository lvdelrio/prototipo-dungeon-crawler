using System.Collections.Generic;

namespace Combat
{
    // Antes una party fija de 6; ahora arma la party a partir de una LISTA de clases (para poder
    // elegir en la pantalla de creacion de una partida nueva, ver Gameplay/PartyCreationHUD).
    // CreateDefaultParty() sigue existiendo tal cual para "Continuar" (misma composicion clasica
    // de siempre) y para cualquier lugar que todavia no necesite eleccion.
    public static class PartyFactory
    {
        public static readonly CharacterClass[] DefaultClasses =
        {
            CharacterClass.Warrior, CharacterClass.Protector, CharacterClass.Ranger,
            CharacterClass.Alchemist, CharacterClass.Mage, CharacterClass.Medic,
        };

        // Las 9 clases seleccionables en la pantalla de creacion de party (las 6 clasicas + las 3
        // opcionales), en el orden en que se muestran ahi.
        public static readonly CharacterClass[] AllSelectableClasses =
        {
            CharacterClass.Warrior, CharacterClass.Protector, CharacterClass.Ranger,
            CharacterClass.Alchemist, CharacterClass.Mage, CharacterClass.Medic,
            CharacterClass.Gunner, CharacterClass.Berserker, CharacterClass.Trovador,
        };

        public static List<CharacterStats> CreateDefaultParty() => CreateParty(DefaultClasses);

        public static List<CharacterStats> CreateParty(IList<CharacterClass> classes)
        {
            var result = new List<CharacterStats>();
            foreach (var cls in classes)
                result.Add(CreateCharacter(cls));
            return result;
        }

        public static CharacterStats CreateCharacter(CharacterClass cls)
        {
            switch (cls)
            {
                case CharacterClass.Warrior:
                    return new CharacterStats
                    {
                        Name = "Warrior", Class = CharacterClass.Warrior,
                        MaxHP = 45, HP = 45, MaxTP = 10, TP = 10,
                        Attack = 12, Defense = 5, Speed = 5, Evasion = 5, Luck = 5,
                        AttackElement = Element.Slash,
                        SkillName = "Corte Poderoso", SkillTpCost = 4, SkillPower = 1.6f, SkillElement = Element.Slash,
                        SkillSequenceA = new[] { QteKey.Up, QteKey.Left, QteKey.Down },
                        SkillSequenceB = new[] { QteKey.Right, QteKey.Up, QteKey.Right },
                        IsFrontRow = true,
                    };

                case CharacterClass.Protector:
                    return new CharacterStats
                    {
                        Name = "Protector", Class = CharacterClass.Protector,
                        MaxHP = 55, HP = 55, MaxTP = 8, TP = 8,
                        Attack = 8, Defense = 9, Speed = 4, Evasion = 2, Luck = 3,
                        AttackElement = Element.Strike,
                        SkillName = "Golpe Escudo", SkillTpCost = 4, SkillPower = 1.4f, SkillElement = Element.Strike,
                        SkillSequenceA = new[] { QteKey.Down, QteKey.Down, QteKey.Up },
                        SkillSequenceB = new[] { QteKey.Left, QteKey.Left, QteKey.Right },
                        CanProtectAll = true,
                        IsFrontRow = true,
                    };

                case CharacterClass.Ranger:
                    return new CharacterStats
                    {
                        Name = "Ranger", Class = CharacterClass.Ranger,
                        MaxHP = 38, HP = 38, MaxTP = 10, TP = 10,
                        Attack = 11, Defense = 4, Speed = 8, Evasion = 15, Luck = 8,
                        AttackElement = Element.Pierce,
                        SkillName = "Tiro Certero", SkillTpCost = 5, SkillPower = 1.7f, SkillElement = Element.Pierce,
                        SkillSequenceA = new[] { QteKey.Left, QteKey.Right, QteKey.Up },
                        SkillSequenceB = new[] { QteKey.Down, QteKey.Down, QteKey.Up },
                        IsFrontRow = true,
                    };

                case CharacterClass.Alchemist:
                    // Ataca (basico Y habilidad) a TODOS los enemigos vivos a la vez -- ver
                    // CombatEngine.AoeDamageMultiplier para el descuento por objetivo. Especializado
                    // en Ataque Magico: su habilidad escala con MagicAttack, no con Attack.
                    return new CharacterStats
                    {
                        Name = "Alchemist", Class = CharacterClass.Alchemist,
                        MaxHP = 30, HP = 30, MaxTP = 16, TP = 16,
                        Attack = 6, Defense = 3, Speed = 5, Evasion = 5, Luck = 6,
                        MagicAttack = 10, SkillUsesMagicAttack = true,
                        AttackElement = Element.Fire,
                        SkillName = "Lluvia de Fuego", SkillTpCost = 7, SkillPower = 1.6f, SkillElement = Element.Fire,
                        AttacksAreAoe = true,
                        SkillSequenceA = new[] { QteKey.Up, QteKey.Up, QteKey.Down },
                        SkillSequenceB = new[] { QteKey.Right, QteKey.Left, QteKey.Right },
                    };

                case CharacterClass.Mage:
                    return new CharacterStats
                    {
                        Name = "Mage", Class = CharacterClass.Mage,
                        MaxHP = 32, HP = 32, MaxTP = 16, TP = 16,
                        Attack = 7, Defense = 3, Speed = 5, Evasion = 5, Luck = 6,
                        MagicAttack = 11, SkillUsesMagicAttack = true,
                        AttackElement = Element.Ice,
                        SkillName = "Lanza de Hielo", SkillTpCost = 6, SkillPower = 1.8f, SkillElement = Element.Ice,
                        SkillSequenceA = new[] { QteKey.Down, QteKey.Up, QteKey.Left },
                        SkillSequenceB = new[] { QteKey.Left, QteKey.Right, QteKey.Down },
                    };

                case CharacterClass.Medic:
                    // La curacion (ver CombatEngine/CombatManager.UseHealSkillOutOfCombat) suma
                    // MagicAttack/2 sobre el HealAmount base -- especializado en Ataque Magico igual
                    // que Alchemist/Mage/Trovador, aunque su habilidad no hace dano.
                    return new CharacterStats
                    {
                        Name = "Medic", Class = CharacterClass.Medic,
                        MaxHP = 40, HP = 40, MaxTP = 20, TP = 20,
                        Attack = 6, Defense = 4, Speed = 6, Evasion = 8, Luck = 10,
                        MagicAttack = 8,
                        AttackElement = Element.Strike,
                        SkillName = "Curacion", SkillTpCost = 6, SkillPower = 0f, SkillElement = Element.None,
                        IsHealSkill = true, HealAmount = 25,
                        SkillSequenceA = new[] { QteKey.Up, QteKey.Down, QteKey.Up },
                        SkillSequenceB = new[] { QteKey.Right, QteKey.Up, QteKey.Left },
                    };

                case CharacterClass.Gunner:
                    // Balas elementales (ver CharacterStats.LoadedBulletElement): arranca con 5 de
                    // cada elemento magico basico (Fuego/Hielo/Rayo); mientras tenga una cargada Y
                    // stock, tanto el ataque basico como la habilidad pegan con ESE elemento.
                    return new CharacterStats
                    {
                        Name = "Gunner", Class = CharacterClass.Gunner,
                        MaxHP = 34, HP = 34, MaxTP = 12, TP = 12,
                        Attack = 10, Defense = 4, Speed = 7, Evasion = 12, Luck = 10,
                        AttackElement = Element.Pierce,
                        SkillName = "Disparo Cargado", SkillTpCost = 5, SkillPower = 1.6f, SkillElement = Element.Pierce,
                        FireBullets = 5, IceBullets = 5, VoltBullets = 5,
                        SkillSequenceA = new[] { QteKey.Right, QteKey.Right, QteKey.Left },
                        SkillSequenceB = new[] { QteKey.Up, QteKey.Right, QteKey.Down },
                    };

                case CharacterClass.Berserker:
                    // Full ataque a costa de la defensa: mucho ATQ/HP base, DEF muy baja, y su
                    // habilidad es una POSTURA propia (sin objetivo) que empuja eso todavia mas
                    // mientras este activa (ver CharacterStats.IsEnraged).
                    return new CharacterStats
                    {
                        Name = "Berserker", Class = CharacterClass.Berserker,
                        MaxHP = 48, HP = 48, MaxTP = 9, TP = 9,
                        Attack = 15, Defense = 3, Speed = 6, Evasion = 0, Luck = 4,
                        AttackElement = Element.Strike,
                        SkillName = "Furia Descontrolada", SkillTpCost = 3, SkillPower = 0f, SkillElement = Element.None,
                        IsSelfStanceSkill = true,
                        SkillSequenceA = new[] { QteKey.Down, QteKey.Right, QteKey.Up },
                        SkillSequenceB = new[] { QteKey.Left, QteKey.Down, QteKey.Left },
                        IsFrontRow = true,
                    };

                case CharacterClass.Trovador:
                    // Habilidad VERSATIL (ver CharacterStats.IsVersatileBuffSkill): sobre un aliado
                    // buffea su Ataque, sobre un enemigo debuffea su Defensa. Especializado en
                    // Ataque Magico igual que Alchemist/Mage/Medic (stat presente aunque su
                    // habilidad no hace dano directo).
                    return new CharacterStats
                    {
                        Name = "Trovador", Class = CharacterClass.Trovador,
                        MaxHP = 34, HP = 34, MaxTP = 18, TP = 18,
                        Attack = 6, Defense = 3, Speed = 6, Evasion = 8, Luck = 14,
                        MagicAttack = 8,
                        AttackElement = Element.Strike,
                        SkillName = "Canción de Guerra", SkillTpCost = 5, SkillPower = 0f, SkillElement = Element.None,
                        IsVersatileBuffSkill = true,
                        SkillSequenceA = new[] { QteKey.Up, QteKey.Right, QteKey.Up },
                        SkillSequenceB = new[] { QteKey.Down, QteKey.Left, QteKey.Down },
                    };

                default:
                    throw new System.ArgumentOutOfRangeException(nameof(cls), cls, "Clase sin definicion en PartyFactory.CreateCharacter");
            }
        }
    }
}
