namespace DungeonGen
{
    public class DungeonCell
    {
        public int X;
        public int Y;
        public bool[] Walls = { true, true, true, true }; // indexed by Direction, true = wall present
        public CellType Type = CellType.Normal;
        public bool IsIsolatedZone;
        public bool EventConsumed;
        public bool Discovered; // runtime: revelado en el minimapa del jugador al pisarlo

        // Populated when Type == Event
        public EventEntry AssignedEvent;

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
