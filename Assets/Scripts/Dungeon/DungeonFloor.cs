using System.Collections.Generic;

namespace DungeonGen
{
    public class ShortcutGate
    {
        public int Ax, Ay;      // celda "afuera" del borde elegido (referencia, nunca se camina por ahi)
        public int Bx, By;      // celda "adentro" de la zona aislada, en ese mismo borde
        public Direction DirFromA; // direccion de A a B (la pared entre ambas queda cerrada para siempre)
        public bool IsOpen;

        // Puntos reales del teletransporte (pueden diferir de Ax/Ay y Bx/By si esas celdas ya
        // estaban ocupadas por otro elemento y hubo que reubicar el switch o el punto de llegada).
        public int SwitchX, SwitchY;
        public int LandingX, LandingY;

        // Si no es null/vacio, activar este atajo (pisar el switch) exige haber desbloqueado esta
        // entrada de Codex primero (ver CellType.Lore). La zona aislada sigue siendo alcanzable a
        // pie sin el lore -- esto solo bloquea el TELETRANSPORTE de conveniencia, nunca el acceso.
        public string RequiredLoreId;
    }

    // Candado OBLIGATORIO sobre una arista del camino critico Start->End (a diferencia del
    // ShortcutGate, que siempre es opcional/atajo): la pared entre DoorX/DoorY y su vecino en
    // DoorDir arranca cerrada de verdad, y no hay otro camino alrededor (el mapa base es un
    // arbol) -- hay que encontrar la palanca en LeverX/LeverY, que siempre esta en un punto
    // muerto alcanzable SIN cruzar la puerta, y despues volver (backtracking real).
    public class LockedDoor
    {
        public int DoorX, DoorY;
        public Direction DoorDir;
        public bool IsUnlocked;
        public int LeverX, LeverY;
    }

    public class DungeonFloor
    {
        public int Index;
        public int Width;
        public int Height;
        public DungeonCell[,] Cells;
        public List<ShortcutGate> Gates = new List<ShortcutGate>();
        public List<LockedDoor> LockedDoors = new List<LockedDoor>();

        // Cofre opcional fuera del camino principal (null si el mapa fue demasiado chico como
        // para reservarle un punto muerto propio, aparte del de la palanca y la mision secundaria).
        public (int x, int y)? TreasurePos;

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
