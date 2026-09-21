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
        public bool EventConsumed;
        public bool Discovered; // runtime: revelado en el minimapa del jugador al pisarlo

        // Valor de peligro (0-5) usado por el sistema real de encuentros de Etrian Odyssey: se
        // suma a un contador de pasos cada vez que se pisa la celda. Solo celdas Normal/Event
        // tienen un valor mayor a 0; el resto (Start/End/escaleras/vacio/etc.) es siempre seguro.
        public int DangerValue;

        // Populated when Type == Event
        public EventEntry AssignedEvent;

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
