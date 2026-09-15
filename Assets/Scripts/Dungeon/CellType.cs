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
        Void // celda podada: no es parte de ningun camino, roca solida (no caminable, no se renderiza)
    }
}
