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

        // Sala de pistas (ver DungeonGenerator.AddLoreCorridorRoom): grilla de piso "trampa" donde
        // solo un tell visual (particulas, ver DungeonLevelBuilder.BuildPuzzleTile) distingue las
        // celdas reales (IsPuzzleTileSafe true) de las que ceden. IsPuzzleTile marca cualquier
        // celda de la grilla, sea segura o no.
        public bool IsPuzzleTile;
        public bool IsPuzzleTileSafe;

        public bool EventConsumed;
        public bool Discovered; // runtime: revelado en el minimapa del jugador al pisarlo

        // Valor de peligro (0-5) usado por el sistema real de encuentros de Etrian Odyssey: se
        // suma a un contador de pasos cada vez que se pisa la celda. Solo celdas Normal tienen un
        // valor mayor a 0; el resto (Start/End/escaleras/vacio/etc.) es siempre seguro.
        public int DangerValue;

        // Populated when Type == Lore: el id de la entrada de Codex que se desbloquea al pisarla.
        public string AssignedLoreId;

        // Populated when Type == StairsUp / StairsDown
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
    }
}
