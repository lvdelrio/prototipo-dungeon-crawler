namespace Combat
{
    public enum CharacterClass
    {
        Warrior,
        Protector,
        Ranger,
        Alchemist,
        Mage,
        Medic,
        // Clases opcionales: se eligen en la pantalla de creacion de party de una partida nueva
        // (ver Gameplay/PartyCreationHUD), no forman parte de la party fija por defecto.
        Gunner,
        Berserker,
        Trovador,
    }
}
