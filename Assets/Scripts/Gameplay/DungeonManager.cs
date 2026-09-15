using System.Collections.Generic;
using UnityEngine;
using DungeonGen;
using Combat;

namespace Gameplay
{
    public class DungeonManager : MonoBehaviour
    {
        public DungeonSettings settings;
        public EventTableAsset eventTable;
        public GridPlayerController player;
        public DungeonLevelBuilder levelBuilder;
        public DebugHUD hud;
        public CombatManager combat;

        private readonly DungeonGenerator _generator = new DungeonGenerator();
        private List<DungeonFloor> _floors;
        private int _currentFloorIndex;
        private float _currentEncounterChance;
        private readonly HashSet<int> _bossDefeatedFloors = new HashSet<int>();

        public DungeonFloor CurrentFloor => _floors[_currentFloorIndex];
        public int CurrentFloorIndex => _currentFloorIndex;
        public List<DungeonFloor> Floors => _floors;
        public bool IsCombatActive => combat != null && combat.IsActive;
        public float CurrentEncounterChancePercent => _currentEncounterChance;

        void Awake()
        {
            int seed = settings.seed != 0 ? settings.seed : System.Environment.TickCount;
            _floors = _generator.GenerateDungeon(
                settings.floorCount, settings.size, settings.size, seed,
                settings.eventPercent, out var log,
                eventTable != null ? eventTable.entries : null,
                settings.stairPairsPerFloor,
                settings.eventsPerFloor,
                settings.bossFloorStart,
                settings.bossFloorInterval,
                settings.voidFraction);

            foreach (var line in log) Debug.Log(line);

            _currentEncounterChance = settings.encounterBaseChance;
            if (combat != null) combat.OnCombatFinished += HandleCombatFinished;

            _currentFloorIndex = 0;
            BuildActiveFloor();

            var start = CurrentFloor.StartPos;
            player.Warp(start.x, start.y, Direction.North);
            OnPlayerEnterCell(start.x, start.y);
        }

        private void BuildActiveFloor()
        {
            levelBuilder.Build(CurrentFloor, settings.cellSize, settings.wallHeight, settings.wallThickness);
        }

        public Vector3 CellToWorld(int x, int y) => levelBuilder.CellCenter(x, y, settings.cellSize);

        public bool CanMove(int x, int y, Direction dir)
        {
            var floor = CurrentFloor;
            if (!floor.InBounds(x, y)) return false;
            var cell = floor.Cells[x, y];
            if (cell.HasWall(dir)) return false;
            var (ox, oy) = dir.Offset();
            int nx = x + ox, ny = y + oy;
            return floor.InBounds(nx, ny) && floor.Cells[nx, ny].Type != CellType.Void;
        }

        public void OnPlayerEnterCell(int x, int y)
        {
            var cell = CurrentFloor.Cells[x, y];
            cell.Discovered = true;

            if ((cell.Type == CellType.Normal || cell.Type == CellType.Event) && !IsCombatActive)
                TryRollEncounter();

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
                    message = IsGateOpen(cell)
                        ? "Punto de atajo activo. Presiona Espacio para teletransportarte al otro lado."
                        : "Palanca de atajo. Presiona Espacio para activarla permanentemente.";
                    break;
                case CellType.ShortcutLanding:
                    message = IsGateOpen(cell)
                        ? "Punto de atajo activo. Presiona Espacio para teletransportarte al otro lado."
                        : "Un punto extrano al borde de un vacio. Quiza haya algo del otro lado.";
                    break;
                case CellType.Boss:
                    message = _bossDefeatedFloors.Contains(_currentFloorIndex)
                        ? "El jefe de este piso ya fue derrotado. La escalera para avanzar esta en esta sala."
                        : "Sala del jefe. Presiona Espacio para enfrentarlo.";
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

        private bool IsGateOpen(DungeonCell cell) =>
            cell.ControlledGateIndex >= 0 && CurrentFloor.Gates[cell.ControlledGateIndex].IsOpen;

        private void TryRollEncounter()
        {
            if (combat == null) return;
            if (Random.value < _currentEncounterChance / 100f)
            {
                _currentEncounterChance = settings.encounterBaseChance;
                combat.StartEncounter(isBoss: false);
            }
            else
            {
                _currentEncounterChance = Mathf.Min(settings.encounterCap, _currentEncounterChance + settings.encounterIncrement);
            }
        }

        private void HandleCombatFinished(bool victory, bool wasBoss)
        {
            _currentEncounterChance = settings.encounterBaseChance;

            if (wasBoss && victory)
                _bossDefeatedFloors.Add(_currentFloorIndex);

            if (!victory)
            {
                // Derrota: la party despierta malherida de vuelta en el Start de este piso.
                var start = CurrentFloor.StartPos;
                player.Warp(start.x, start.y, Direction.North);
                CurrentFloor.Cells[start.x, start.y].Discovered = true;
                if (hud != null) hud.SetLastMessage("Despiertan de vuelta cerca del inicio del piso...");
            }
        }

        public void TryInteract(int x, int y)
        {
            var cell = CurrentFloor.Cells[x, y];

            if (cell.Type == CellType.ShortcutSwitch || cell.Type == CellType.ShortcutLanding)
            {
                if (!IsGateOpen(cell))
                {
                    // Solo la palanca (lado del switch) puede activar el atajo por primera vez.
                    if (cell.Type == CellType.ShortcutSwitch)
                    {
                        _generator.OpenGate(CurrentFloor, cell.ControlledGateIndex);
                        levelBuilder.ActivateShortcutVisual(cell.ControlledGateIndex);
                        if (hud != null) hud.SetLastMessage("Atajo activado de forma permanente: ahora puedes teletransportarte entre este punto y el otro lado del vacio.");
                    }
                    else if (hud != null)
                    {
                        hud.SetLastMessage("Este punto de atajo todavia esta inactivo.");
                    }
                    return;
                }

                if (_generator.TryGetTeleportTarget(CurrentFloor, x, y, out int tx, out int ty))
                {
                    player.Warp(tx, ty, player.Facing);
                    OnPlayerEnterCell(tx, ty);
                    if (hud != null) hud.SetLastMessage("Te teletransportaste a traves del atajo.");
                }
            }
            else if (cell.Type == CellType.StairsUp || cell.Type == CellType.StairsDown)
            {
                ChangeFloor(cell.StairTargetFloor, cell.StairTargetX, cell.StairTargetY);
            }
            else if (cell.Type == CellType.Boss)
            {
                if (_bossDefeatedFloors.Contains(_currentFloorIndex))
                {
                    if (hud != null) hud.SetLastMessage("El jefe de este piso ya fue derrotado.");
                }
                else if (combat != null && !combat.IsActive)
                {
                    combat.StartEncounter(isBoss: true);
                }
            }
        }

        public void ChangeFloor(int floorIndex, int spawnX, int spawnY)
        {
            if (floorIndex < 0 || floorIndex >= _floors.Count) return;
            _currentFloorIndex = floorIndex;
            BuildActiveFloor();
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
