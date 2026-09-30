namespace DungeonGen
{
    public class DungeonCell
    {
        public int X;
        public int Y;
        public bool[] Walls = { true, true, true, true }; // indexed by Direction, true = wall present
        public CellType Type = CellType.Normal;
        public bool IsIsolatedZone;
        public bool IsBossRoom;
        // true en cualquier celda de cofre (ver DungeonGenerator.EnsureTreasure) -- cada cofre es
        // su propia celda de 1x1, esta bandera solo sirve para pintarla distinto en el mapa.
        public bool IsTreasureRoom;

        // Sala de trampas (ver DungeonGenerator.AddTrapRoom): IsTrapRoom marca TODAS las celdas de
        // la sala agrandada (piso/decoracion distinta, igual que la sala de jefe); IsTrapCell marca
        // SOLO las celdas peligrosas de verdad dentro de ella (la linea que barre la maquina de
        // flechas, o las celdas de picos sueltas segun TrapKind) -- pisar una de esas tiene chance
        // de activar la trampa (ver DungeonManager.OnPlayerEnterCell).
        public bool IsTrapRoom;
        public bool IsTrapCell;

        // Celda de una sala de autor con forma prediseñada (ver DungeonGenerator.
        // PlacePredefinedRooms/RoomTemplate): sus paredes internas y puertas se estampan del
        // molde, no las decide Carve(). Protegida de PruneToSparseMaze y excluida de cualquier
        // busqueda de punto libre (cofre/mision secundaria/Puerta Fria/etc.) para que la forma de
        // autor quede intacta y "limpia", sin contenido de otro sistema apilado encima.
        public bool IsPredefinedRoom;

        // Nombre del RoomTemplate del que salio esta celda (ver DungeonGenerator.TryStampRoomAt),
        // null si IsPredefinedRoom es false. Sirve para el "codex de formas" (ver MetaProgress.
        // RecognizedRoomTemplateNames / PauseMenuHUD): el jugador puede guardar a mano, desde la
        // pestaña Mapa, que ya reconoce esta forma -- asi la reconoce entre runs aunque la
        // mazmorra entera cambie.
        public string PredefinedRoomTemplateName;

        // Celda peligrosa DENTRO de una sala de autor (ver RoomTemplate, caracter '^'): lastima al
        // pisarla igual que una trampa de picos, pero es personalidad de esa sala especifica, NO el
        // sistema de DungeonFloor.TrapKind/AddTrapRoom (que sigue siendo el unico dueño de esos
        // campos) -- por eso es un flag totalmente aparte. A proposito nunca tiene una forma de
        // desactivarse: la unica manera de lidiar con ella es esquivarla, a diferencia de la trampa
        // de flechas que si se puede destruir con el Perforador.
        public bool IsPredefinedRoomHazard;

        // Sala de pistas (ver DungeonGenerator.AddLoreCorridorRoom): grilla de piso "trampa" donde
        // solo un tell visual (particulas, ver DungeonLevelBuilder.BuildPuzzleTile) distingue las
        // celdas reales (IsPuzzleTileSafe true) de las que ceden. IsPuzzleTile marca cualquier
        // celda de la grilla, sea segura o no.
        public bool IsPuzzleTile;
        public bool IsPuzzleTileSafe;

        // Peligros de la mini cueva del piso 0 (ver DungeonFloor.BoulderTrapPath/CollapseRoomCells/
        // AmbushRoomCells para el detalle de cada mecanismo -- estos 3 flags solo marcan que celda
        // pertenece a cual).
        public bool IsBoulderTrapCell;
        public bool IsCollapseRoom;
        // Runtime: esta celda de la sala que colapsa ya cayo (ver Gameplay/DungeonManager.
        // CollapseRoomRoutine) -- pisarla hace caer al piso de abajo, igual que Goteras.
        public bool IsCollapseFallen;
        public bool IsAmbushRoom;

        // Boveda de cascada (pedido puntual, ver DungeonGenerator.AddWaterfallVault): un guardian
        // fuerte con un tesoro garantizado, al fondo de un callejon sin salida detras de una
        // cascada. IsWaterfallRoom marca la celda de ADELANTE (la cascada real + la fuente de agua
        // que la alimenta, solo decorativo); IsWaterfallVaultRoom marca la celda de la boveda en si
        // (su unica conexion real es esa cascada) -- pisarla dispara el combate contra el guardian,
        // ver Gameplay/DungeonManager.OnPlayerEnterCell.
        public bool IsWaterfallRoom;
        public bool IsWaterfallVaultRoom;

        // Trampa de goblins del Bioma de Cuevas (pedido puntual, ver DungeonGenerator.AddGoblinTraps):
        // celda de piso normal SIN ningun tell visual (a proposito -- son goblins emboscando, no un
        // mecanismo que se pueda desactivar como la sala de trampas comun). Pisarla dispara UN
        // combate, una sola vez -- GoblinTrapTriggered es POR CELDA (a diferencia de
        // DungeonFloor.AmbushTriggered, que es una unica bandera por piso) porque puede haber varias
        // trampas de estas en el mismo piso, cada una independiente.
        public bool IsGoblinTrap;
        public bool GoblinTrapTriggered;

        // Otra mitad puramente decorativa de una tumba vecina (ver CellType.Tomb / DungeonGenerator.
        // AddTombs): esta celda sigue siendo Normal y caminable, IsTombDecor solo le agrega el resto
        // del sarcofago encima en DungeonLevelBuilder.BuildTombDecor.
        public bool IsTombDecor;

        public bool EventConsumed;
        public bool Discovered; // runtime: revelado en el minimapa del jugador al pisarlo

        // Anotaciones a mano del jugador sobre SU copia del mapa (ver PlayerMapEditorHUD): NO
        // forman parte de la mazmorra real -- Walls arriba sigue siendo la unica fuente de verdad
        // para movimiento/colision, esto es solo dibujo/notas encima. Mismo ciclo de vida que
        // Discovered (en memoria durante la run, no se guarda en el archivo de save).
        public bool[] PaintedWalls = { false, false, false, false };
        public int PaintedFloorColorIndex = -1; // -1 = sin pintar; indice en DungeonMapRenderer.FloorPaintColors
        public string PaintedSymbolIcon; // null = sin simbolo; nombre de icono en MapIconImporter.IconNames

        public bool HasPaintedWall(Direction d) => PaintedWalls[(int)d];
        public void SetPaintedWall(Direction d, bool value) => PaintedWalls[(int)d] = value;

        // Valor de peligro (0-5) usado por el sistema real de encuentros de Etrian Odyssey: se
        // suma a un contador de pasos cada vez que se pisa la celda. Solo celdas Normal tienen un
        // valor mayor a 0; el resto (Start/End/escaleras/vacio/etc.) es siempre seguro.
        public int DangerValue;

        // Populated when Type == Lore: el id de la entrada de Codex que se desbloquea al pisarla.
        public string AssignedLoreId;

        // Populated when Type == StairsUp / StairsDown or one of the castle entrance doors.
        public int StairTargetFloor = -1;
        public int StairTargetX = -1;
        public int StairTargetY = -1;

        // Populated when Type == ShortcutSwitch
        public int ControlledGateIndex = -1;

        public DungeonCell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool HasWall(Direction d) => Walls[(int)d];
        public void SetWall(Direction d, bool value) => Walls[(int)d] = value;

        // Primera direccion sin pared, en el orden fijo Norte/Este/Sur/Oeste -- usado para saber
        // hacia donde mirar al arrancar en esta celda (ver DungeonManager.GenerateAndEnterDungeon
        // y Gameplay.DungeonLevelBuilder.BuildSpawnSignposts, que TIENEN que coincidir en el
        // resultado para que el jugador arranque mirando justo a los carteles). fallback por si
        // esta celda no tuviera ningun lado abierto (no deberia pasar en una celda real conectada
        // al resto de la mazmorra).
        public Direction FirstOpenDirection(Direction fallback = Direction.North)
        {
            foreach (var dir in DirectionExtensions.All)
                if (!HasWall(dir)) return dir;
            return fallback;
        }
    }
}
