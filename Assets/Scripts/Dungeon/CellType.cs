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
        Void // celda podada: no es parte de ningun camino, roca solida (no caminable, no se renderiza)
    }
}
