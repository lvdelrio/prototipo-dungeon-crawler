namespace DungeonGen
{
    public enum CellType
    {
        Normal,
        Start,
        End,
        SecondaryQuest,
        ShortcutSwitch,
        ShortcutLanding,
        StairsUp,
        StairsDown,
        Event,
        Boss,
        // Fragmento de lore (metroidvania): al pisarla se desbloquea una entrada del Codex, y esa
        // entrada es lo que hace falta saber para poder activar el atajo (ShortcutGate) de este
        // mismo piso -- ver ShortcutGate.RequiredLoreId. Siempre esta del lado "de afuera" (fuera
        // de la zona aislada que el propio atajo acorta), para que el jugador la encuentre antes.
        Lore,

        // Los 3 pilares de pacing (ver DungeonGenerator.PlacePacingPillars / articulo de Aevee Bee
        // "Pacing And Level Design In JRPGs"): un candado obligatorio en el camino critico
        // Start->End, la palanca (en un punto muerto real) que lo abre, y un cofre opcional fuera
        // del camino principal.
        LockedDoor,
        Lever,
        Treasure,

        Void // celda podada: no es parte de ningun camino, roca solida (no caminable, no se renderiza)
    }
}
