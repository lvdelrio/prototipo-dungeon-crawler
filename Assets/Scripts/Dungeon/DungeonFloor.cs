using System.Collections.Generic;

namespace DungeonGen
{
    // Variacion de trampa dentro de una sala de trampas (ver DungeonGenerator.AddTrapRoom):
    // ArrowSweep = una maquina dispara flechas de un lado a otro de la sala por una linea recta
    // (fila o columna); SpikeCells = picos sueltos en celdas puntuales de la sala. Misma mecanica
    // de dano (Gameplay/DungeonManager.OnPlayerEnterCell), solo cambia que celdas son peligrosas y
    // el mensaje/color.
    public enum TrapKind { ArrowSweep, SpikeCells }

    // 3 "tells" visuales para una sala de pistas (ver DungeonGenerator.AddLoreCorridorRoom /
    // Gameplay/DungeonLevelBuilder.BuildPuzzleTile): mismo mecanismo (grilla segura/insegura,
    // solo distinguible observando antes de pisar), distinta ambientacion -- Goteras = gotas que
    // splashean en piso real y caen de largo en el falso; Brasas = brasas que se asientan en piso
    // real y se hunden en el falso; PolvoDeCuarzo = polvo que se pega al piso real (cargado, tema
    // del Bioma 2) y flota de largo sobre el falso.
    public enum PuzzleKind { Goteras, Brasas, PolvoDeCuarzo }

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

        // 3 a 5 cofres GARANTIZADOS por piso, cada uno en su propio punto muerto (ver
        // DungeonGenerator.EnsureTreasure). TreasurePositions tiene TODOS; TreasurePos queda el
        // primero (compatibilidad con lo que ya lo leia). TreasureRoomCells son las mismas celdas
        // que TreasurePositions -- se mantiene aparte porque PruneToSparseMaze/PlaceBiomeGate ya
        // protegen/excluyen leyendo ESTE nombre. Vacios solo en mapas degenerados sin puntos
        // muertos libres.
        public (int x, int y)? TreasurePos;
        public List<(int, int)> TreasurePositions;
        public List<(int, int)> TreasureRoomCells;

        public (int x, int y) StartPos;
        public (int x, int y) EndPos;
        public (int x, int y) SecondaryQuestPos;

        // Ruta fija de patrulla de un FOE (enemigo fuerte que se pasea el piso, ver
        // Gameplay/FoeController) -- un tramo del camino critico Start->End, va y viene por ella.
        // Null/vacia = este piso no tiene FOE (el primer piso nunca tiene, ver
        // DungeonGenerator.PlaceFoeRoute).
        public List<(int x, int y)> FoePatrolRoute;
        public bool HasFoe => FoePatrolRoute != null && FoePatrolRoute.Count > 0;

        // Estado en vivo del FOE de este piso, guardado por Gameplay/FoeController.SaveStateTo
        // cada vez que el jugador se va a otro piso: el FOE nunca "desaparece" salvo que lo
        // derrotes en combate (eso pone FoePatrolRoute = null arriba) -- si volves a este piso mas
        // tarde, retoma exactamente donde quedo (posicion, HP, si te estaba persiguiendo, etc.) en
        // vez de reaparecer reseteado. FoeSavedX = -1 significa "todavia nunca se instancio".
        public int FoeSavedX = -1, FoeSavedY = -1;
        public int FoeSavedHp = -1;
        public bool FoeSavedIsChasing;
        public int FoeSavedRouteIndex;
        public int FoeSavedRouteDir = 1;
        public int FoeSavedStunnedSteps;

        // rectangle bounds of the isolated zone (inclusive)
        public int IsoMinX, IsoMinY, IsoMaxX, IsoMaxY;

        // sala de jefe (opcional, solo en pisos designados como "boss floor")
        public List<(int, int)> BossRoomCells;
        public int BossRoomMinX, BossRoomMinY, BossRoomMaxX, BossRoomMaxY;
        public (int x, int y) BossPos;
        public bool HasBossRoom => BossRoomCells != null && BossRoomCells.Count > 0;

        // Sala de trampas (opcional, ver DungeonGenerator.AddTrapRoom): sala grande (4x4/4x3 o mas)
        // con una maquina de flechas o picos adentro. TrapRoomCells son TODAS las celdas de la sala
        // agrandada; TrapCells son SOLO las peligrosas de verdad (la linea que barre la flecha, o
        // las celdas de picos sueltas).
        public List<(int, int)> TrapRoomCells;
        public List<(int, int)> TrapCells;
        public TrapKind TrapKind;
        public bool HasTrapRoom => TrapRoomCells != null && TrapRoomCells.Count > 0;

        // A que bioma pertenece este piso (0 = el original). Los pisos con Biome != 0 se generan
        // aparte (ver DungeonGenerator.GenerateBiomeGateFloor) y quedan fuera de la secuencia
        // normal de escaleras -- solo se llega vía CellType.BiomeGate.
        public int Biome;

        // Posicion de la celda BiomeGate ("Puerta Fria") de este piso, si tiene una, y de la
        // celda desde la que se la perfora (para protegerla de PruneToSparseMaze -- ver
        // DungeonGenerator.PlaceBiomeGate). Null si este piso no tiene ninguna.
        public (int x, int y)? BiomeGatePos;
        public (int x, int y)? BiomeGateApproachPos;

        // Solo para TrapKind.ArrowSweep (ver Gameplay/TrapDisparadorController): la maquina real
        // vive montada en la pared de UNO de los dos extremos de TrapArrowPath (TrapDisparadorPos),
        // disparando en TrapDisparadorDir. TrapArrowPath es TrapCells pero YA ORDENADA en el orden
        // real de vuelo (del disparador hacia el otro extremo), asi la flecha solo tiene que
        // recorrerla celda por celda. TrapDisabled queda true para siempre (esta run) si el jugador
        // destruye el disparador con el Perforador -- ver DungeonGenerator.TryDestroyTrapDisparador.
        public (int x, int y) TrapDisparadorPos;
        public Direction TrapDisparadorDir;
        public List<(int x, int y)> TrapArrowPath;
        public bool TrapDisabled;

        // Sala de pistas (opcional, ver DungeonGenerator.AddLoreCorridorRoom): a lo sumo una por
        // piso, en pisos fijos elegidos por PickLoreCorridorFloors (nunca el piso 0 si se puede
        // evitar). LoreCorridorRoomCells son TODAS las celdas de la sala (piso/decoracion
        // distinta); cual celda especifica tiene el fragmento de lore se sabe por
        // DungeonCell.Type == Lore dentro de esas mismas celdas.
        public List<(int, int)> LoreCorridorRoomCells;
        public PuzzleKind LoreCorridorKind;
        public bool HasLoreCorridor => LoreCorridorRoomCells != null && LoreCorridorRoomCells.Count > 0;

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
