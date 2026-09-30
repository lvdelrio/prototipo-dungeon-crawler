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

        // Tumba del Bioma de Cuevas (pedido puntual, ver DungeonGenerator.AddTombs): el tesoro
        // garantizado de ESE bioma, con un giro -- interactuar tiene 70% de chance de dar loot
        // (igual que un cofre comun) y 30% de disparar un combate contra goblins emboscados
        // adentro (ver Gameplay/DungeonManager.TryInteract). Ocupa 2 celdas de ancho visualmente
        // (ver DungeonLevelBuilder.BuildTombDecor), pero solo esta celda es la interactiva de
        // verdad -- la otra mitad del sarcofago es decoracion pura sobre una celda normal vecina.
        Tomb,

        // La "Puerta Fria" (ver DungeonGenerator.PlaceBiomeGate): una celda real sellada detras de
        // una pared normal, en el piso 0. NO depende de ningun lore ni flag -- cualquiera que la
        // encuentre y la perfore con el Perforador (TryDrillWall generico, sin casos especiales)
        // la abre igual, con o sin pistas. Las 3 pistas de lore (ver Lore/LoreEntry.cs) solo
        // ayudan a saber DONDE buscarla. Al interactuar ya perforada, lleva al Bioma 2.
        BiomeGate,

        // Escalera al fondo de la zona aislada (ver DungeonGenerator.PlaceCaveBiomeExit): a
        // diferencia de la Puerta Fria (secreta, sellada, necesita el Perforador), esta es una
        // escalera comun y visible en el punto mas profundo de la "mini cueva" del piso 0 -- se
        // encuentra caminando, sin lore ni pistas, y lleva directo al Bioma 2 igual que la Puerta
        // Fria (mismo destino, mecanismo de entrada totalmente distinto).
        CaveBiomeExit,

        // Camino secundario desde uno de los cuadrantes del segundo piso del bosque hacia el castillo.
        CastleGate,

        // Aparece al vencer al jefe del bosque y devuelve la expedicion al hub entre runs.
        HubPortal,

        // Puertas del patio frontal/trasero al torreón; su StairTargetFloor apunta al destino.
        CastleMainEntrance,
        CastleRearEntrance,

        Void // celda podada: no es parte de ningun camino, roca solida (no caminable, no se renderiza)
    }
}
