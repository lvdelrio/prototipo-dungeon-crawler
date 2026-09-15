using System.Collections.Generic;
using UnityEngine;
using DungeonGen;

namespace Gameplay
{
    public class DungeonManager : MonoBehaviour
    {
        public DungeonSettings settings;
        public EventTableAsset eventTable;
        public GridPlayerController player;
        public DungeonLevelBuilder levelBuilder;
        public DebugHUD hud;

        private readonly DungeonGenerator _generator = new DungeonGenerator();
        private List<DungeonFloor> _floors;
        private int _currentFloorIndex;

        public DungeonFloor CurrentFloor => _floors[_currentFloorIndex];
        public int CurrentFloorIndex => _currentFloorIndex;
        public List<DungeonFloor> Floors => _floors;

        void Awake()
        {
            int seed = settings.seed != 0 ? settings.seed : System.Environment.TickCount;
            _floors = _generator.GenerateDungeon(
                settings.floorCount, settings.size, settings.size, seed,
                settings.eventPercent, out var log,
                eventTable != null ? eventTable.entries : null,
                settings.stairPairsPerFloor);

            foreach (var line in log) Debug.Log(line);

            _currentFloorIndex = 0;
            levelBuilder.Build(CurrentFloor, settings.cellSize, settings.wallHeight, settings.wallThickness);

            var start = CurrentFloor.StartPos;
            player.Warp(start.x, start.y, Direction.North);
            OnPlayerEnterCell(start.x, start.y);
        }

        public Vector3 CellToWorld(int x, int y) => levelBuilder.CellCenter(x, y, settings.cellSize);

        public bool CanMove(int x, int y, Direction dir)
        {
            var floor = CurrentFloor;
            if (!floor.InBounds(x, y)) return false;
            var cell = floor.Cells[x, y];
            if (cell.HasWall(dir)) return false;
            var (ox, oy) = dir.Offset();
            return floor.InBounds(x + ox, y + oy);
        }

        public void OnPlayerEnterCell(int x, int y)
        {
            var cell = CurrentFloor.Cells[x, y];
            string message = null;
            switch (cell.Type)
            {
                case CellType.End:
                    message = "Llegaste al extremo final de este piso.";
                    break;
                case CellType.SecondaryQuest:
                    message = "Mision secundaria completada.";
                    break;
                case CellType.StairsUp:
                    message = "Escalera hacia arriba. Presiona Espacio para subir.";
                    break;
                case CellType.StairsDown:
                    message = "Escalera hacia abajo. Presiona Espacio para bajar.";
                    break;
                case CellType.ShortcutSwitch:
                    message = "Palanca de atajo. Presiona Espacio para activarla permanentemente.";
                    break;
                case CellType.Event:
                    if (!cell.EventConsumed)
                    {
                        cell.EventConsumed = true;
                        var ev = cell.AssignedEvent;
                        message = ev != null
                            ? $"Evento ({(ev.IsLucky ? "afortunado" : "desafortunado")}): {ev.Name} - {ev.Description}"
                            : "Evento activado.";
                    }
                    break;
            }
            if (message != null && hud != null) hud.SetLastMessage(message);
        }

        public void TryInteract(int x, int y)
        {
            var cell = CurrentFloor.Cells[x, y];
            if (cell.Type == CellType.ShortcutSwitch)
            {
                _generator.OpenGate(CurrentFloor, cell.ControlledGateIndex);
                levelBuilder.OpenGateVisual(cell.ControlledGateIndex);
                if (hud != null) hud.SetLastMessage("Atajo permanente activado. Ya puedes usarlo el resto de la partida.");
            }
            else if (cell.Type == CellType.StairsUp || cell.Type == CellType.StairsDown)
            {
                ChangeFloor(cell.StairTargetFloor, cell.StairTargetX, cell.StairTargetY);
            }
        }

        public void ChangeFloor(int floorIndex, int spawnX, int spawnY)
        {
            if (floorIndex < 0 || floorIndex >= _floors.Count) return;
            _currentFloorIndex = floorIndex;
            levelBuilder.Build(CurrentFloor, settings.cellSize, settings.wallHeight, settings.wallThickness);
            player.Warp(spawnX, spawnY, Direction.North);
            OnPlayerEnterCell(spawnX, spawnY);
            if (hud != null) hud.SetLastMessage($"Cambiaste al piso {floorIndex}.");
        }

        public (bool ok, List<string> issues) ValidateCurrentDungeon()
        {
            return _generator.ValidateDungeon(_floors);
        }
    }
}
