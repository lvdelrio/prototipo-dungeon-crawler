using System.Collections.Generic;

namespace DungeonGen
{
    public class ShortcutGate
    {
        public int Ax, Ay;      // cell on the "outside" side
        public int Bx, By;      // cell on the "isolated zone" side
        public Direction DirFromA; // direction from A to B
        public bool IsOpen;
    }

    public class DungeonFloor
    {
        public int Index;
        public int Width;
        public int Height;
        public DungeonCell[,] Cells;
        public List<ShortcutGate> Gates = new List<ShortcutGate>();

        public (int x, int y) StartPos;
        public (int x, int y) EndPos;
        public (int x, int y) SecondaryQuestPos;

        // rectangle bounds of the isolated zone (inclusive)
        public int IsoMinX, IsoMinY, IsoMaxX, IsoMaxY;

        // sala de jefe (opcional, solo en pisos designados como "boss floor")
        public List<(int, int)> BossRoomCells;
        public int BossRoomMinX, BossRoomMinY, BossRoomMaxX, BossRoomMaxY;
        public (int x, int y) BossPos;
        public bool HasBossRoom => BossRoomCells != null && BossRoomCells.Count > 0;

        public DungeonFloor(int width, int height, int index)
        {
            Width = width;
            Height = height;
            Index = index;
            Cells = new DungeonCell[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    Cells[x, y] = new DungeonCell(x, y);
        }

        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        public bool IsInIsolatedZone(int x, int y) =>
            x >= IsoMinX && x <= IsoMaxX && y >= IsoMinY && y <= IsoMaxY;
    }
}
