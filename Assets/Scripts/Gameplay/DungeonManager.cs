using System.Collections.Generic;
using UnityEngine;
using DungeonGen;
using Combat;
using Meta;

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
        private int _walkingCounter;
        private int _encounterThreshold;
        private readonly HashSet<int> _bossDefeatedFloors = new HashSet<int>();

        private MetaProgress _meta;
        private int _deepestFloorReachedThisRun;
        private int _enemiesDefeatedThisRun;
        private int _bossesDefeatedThisRun;
        private int _lastRunPointsEarned;

        public DungeonFloor CurrentFloor => _floors[_currentFloorIndex];
        public int CurrentFloorIndex => _currentFloorIndex;
        public List<DungeonFloor> Floors => _floors;
        public bool IsCombatActive => combat != null && combat.IsActive;
        public int CurrentWalkingCounter => _walkingCounter;
        public int CurrentEncounterThreshold => _encounterThreshold;

        public MetaProgress Meta => _meta;
        public bool IsGameOverShopActive { get; private set; }
        public int LastRunPointsEarned => _lastRunPointsEarned;

        void Awake()
        {
            _meta = MetaSaveService.Load();
            if (combat != null)
            {
                combat.InitializeParty(_meta);
                combat.OnCombatFinished += HandleCombatFinished;
            }

            int seed = settings.seed != 0 ? settings.seed : System.Environment.TickCount;
            GenerateAndEnterDungeon(seed);
        }

        private void GenerateAndEnterDungeon(int seed)
        {
            _floors = _generator.GenerateDungeon(
                settings.floorCount, settings.size, settings.size, seed,
                settings.eventPercent, out var log,
                eventTable != null ? eventTable.entries : null,
                settings.stairPairsPerFloor,
                settings.eventsPerFloor,
                settings.bossFloorStart,
                settings.bossFloorInterval,
                settings.voidFraction,
                settings.dangerValueMin,
                settings.dangerValueMax);

            foreach (var line in log) Debug.Log(line);

            _bossDefeatedFloors.Clear();
            _deepestFloorReachedThisRun = 0;
            _enemiesDefeatedThisRun = 0;
            _bossesDefeatedThisRun = 0;

            _currentFloorIndex = 0;
            BuildActiveFloor();
            RollNewEncounterThreshold();

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
                AccumulateDangerAndMaybeEncounter(cell);

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

        // Sistema real de encuentros de Etrian Odyssey: cada celda tiene un valor de peligro
        // (0-5) oculto que se suma a un contador de pasos. Cuando el contador supera un limite
        // tambien oculto (elegido al azar tras cada combate o al entrar a un piso nuevo), aparece
        // un encuentro de inmediato y el contador se reinicia a 0.
        private void AccumulateDangerAndMaybeEncounter(DungeonCell cell)
        {
            if (combat == null) return;
            _walkingCounter += cell.DangerValue;
            if (_walkingCounter >= _encounterThreshold)
            {
                _walkingCounter = 0;
                combat.StartEncounter(isBoss: false);
            }
        }

        private void RollNewEncounterThreshold()
        {
            _walkingCounter = 0;
            _encounterThreshold = Random.Range(settings.encounterThresholdMin, settings.encounterThresholdMax + 1);
        }

        private void HandleCombatFinished(bool victory, bool wasBoss)
        {
            RollNewEncounterThreshold();

            if (victory)
            {
                if (wasBoss)
                {
                    _bossDefeatedFloors.Add(_currentFloorIndex);
                    _bossesDefeatedThisRun++;
                }
                else
                {
                    _enemiesDefeatedThisRun += combat.Enemies.Count;
                }
                return;
            }

            // Derrota: la run termina aca. Se banca la recompensa (piso alcanzado + enemigos/jefes)
            // y se abre la pantalla de tienda/mejoras; la proxima mazmorra (semilla nueva) arranca
            // recien cuando el jugador la cierra (StartNewRun).
            _lastRunPointsEarned = _meta.AddRunRewards(_deepestFloorReachedThisRun, _enemiesDefeatedThisRun, _bossesDefeatedThisRun);
            MetaSaveService.Save(_meta);
            IsGameOverShopActive = true;
            if (hud != null) hud.SetLastMessage("La party cae derrotada. La run termina aca.");
        }

        public void StartNewRun()
        {
            if (!IsGameOverShopActive) return;
            IsGameOverShopActive = false;
            combat.InitializeParty(_meta);
            int seed = Random.Range(int.MinValue, int.MaxValue);
            GenerateAndEnterDungeon(seed);
        }

        // Item "Mapa": revela de golpe todo el piso actual (fog of war) sin moverse.
        public bool TryUseMap()
        {
            if (_meta.MapCharges <= 0) return false;
            _meta.MapCharges--;
            MetaSaveService.Save(_meta);
            foreach (var cell in CurrentFloor.Cells)
                cell.Discovered = true;
            if (hud != null) hud.SetLastMessage("Usaste un Mapa: se revelo todo este piso.");
            return true;
        }

        // Item "Perforador": intenta abrir un paso permanente (para esta run) en la pared que el
        // jugador tiene enfrente. Solo se gasta si realmente hay algo del otro lado (no Void).
        public bool TryUseDrill(int x, int y, Direction facing)
        {
            if (_meta.DrillCharges <= 0) return false;
            if (!_generator.TryDrillWall(CurrentFloor, x, y, facing))
            {
                if (hud != null) hud.SetLastMessage("El Perforador no encontro nada solido detras de esa pared.");
                return false;
            }
            _meta.DrillCharges--;
            MetaSaveService.Save(_meta);
            BuildActiveFloor();
            if (hud != null) hud.SetLastMessage("¡Perforaste la pared! Se abrio un paso permanente para esta run.");
            return true;
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
            if (floorIndex > _deepestFloorReachedThisRun) _deepestFloorReachedThisRun = floorIndex;
            BuildActiveFloor();
            RollNewEncounterThreshold();
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
