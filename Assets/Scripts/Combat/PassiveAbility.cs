namespace Combat
{
    // Pasiva equipable (ver Meta.MetaProgress.EquippedPassives / Gameplay.PauseMenuHUD.
    // DrawEquipment): a diferencia del equipamiento (Combat/EquipmentItem.cs), NO son objetos
    // fisicos que se compran o se encuentran -- las SlotsPerCharacter pasivas de cada personaje se
    // eligen libremente entre TODO el catalogo, sin restriccion de clase ni de cuantas veces se
    // repite la misma entre personajes distintos (2 personajes pueden tener "Vigor" a la vez).
    // Bonus de stats planos, mismo estilo que EquipmentItem pero mas simple (sin slot fisico, sin
    // resistencia elemental, sin proc on-hit).
    public class PassiveAbility
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";

        public int AttackBonus;
        public int MagicAttackBonus;
        public int DefenseBonus;
        public int SpeedBonus;
        public int EvasionBonus;
        public int LuckBonus;
        public int MaxHpBonus;
        public int MaxTpBonus;
        public int ThornsReflectPercent;
    }

    public static class PassiveCatalog
    {
        // Slots por personaje (pedido puntual: "agregar slots para pasivas") -- ver
        // Meta.MetaProgress.GetEquippedPassives/SetEquippedPassive y PauseMenuHUD.DrawEquipment.
        public const int SlotsPerCharacter = 2;

        public static readonly PassiveAbility[] All =
        {
            new PassiveAbility { Id = "vigor", Name = "Vigor", Description = "Mas resistencia general.", MaxHpBonus = 12 },
            new PassiveAbility { Id = "reserva", Name = "Reserva", Description = "Mas TP para usar habilidades mas seguido.", MaxTpBonus = 6 },
            new PassiveAbility { Id = "reflejos", Name = "Reflejos", Description = "Mas facil esquivar un golpe entero.", EvasionBonus = 8 },
            new PassiveAbility { Id = "furia_contenida", Name = "Furia Contenida", Description = "Golpea un poco mas fuerte, fisico y magico.", AttackBonus = 2, MagicAttackBonus = 2 },
            new PassiveAbility { Id = "piel_de_piedra", Name = "Piel de Piedra", Description = "Un poco mas dificil de lastimar.", DefenseBonus = 3 },
            new PassiveAbility { Id = "paso_ligero", Name = "Paso Ligero", Description = "Actua un poco antes en el orden de turnos.", SpeedBonus = 2 },
            new PassiveAbility { Id = "fortuna", Name = "Fortuna", Description = "Mas chance de golpe critico.", LuckBonus = 5 },
            new PassiveAbility { Id = "espinas_menores", Name = "Espinas Menores", Description = "Devuelve un poco de todo golpe recibido.", ThornsReflectPercent = 10 },
        };

        public static PassiveAbility Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var p in All)
                if (p.Id == id) return p;
            return null;
        }
    }
}
