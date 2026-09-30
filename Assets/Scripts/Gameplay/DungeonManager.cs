using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DungeonGen;
using Combat;
using Meta;
using Lore;

namespace Gameplay
{
    public class DungeonManager : MonoBehaviour
    {
        public DungeonSettings settings;
        public GridPlayerController player;
        public DungeonLevelBuilder levelBuilder;
        public DebugHUD hud;
        public CombatManager combat;

        // Caja de dialogo (DialogueHUD) para eventos que merecen una pausa y un click de
        // confirmacion (cofre/palanca/lore) en vez de la linea ambiente del DebugHUD -- reusa el
        // DialogueManager que ya cuelga de GridPlayerController (mismo que el demo de taberna),
        // asi que no hace falta cablear una referencia nueva en el Inspector.
        private DialogueManager Dialogue => player != null ? player.dialogueManager : null;

        private const int TreasurePointsReward = 25;
        private static readonly string[] TreasureEquipmentIds = { "daga_venenosa", "hacha_desgarradora", "anillo_de_espinas", "grebas_aislantes", "tunica_ignifuga", "manto_glacial" };

        // Tumba del Bioma de Cuevas (ver DungeonGenerator.AddTombs / TryInteract mas abajo): chance
        // de que interactuar dispare un combate contra goblins emboscados en vez de dar botin.
        private const float TombCombatChance = 0.3f;
        // Fraccion del HP maximo que una trampa (ver DungeonGenerator.AddTrapRoom) le saca a CADA
        // integrante vivo cuando se activa. Ya no hay probabilidad de por medio: SpikeCells duele
        // apenas se pisa, y ArrowSweep dispara una flecha real (ver TrapDisparadorController) que
        // el jugador puede esquivar moviendose fuera de la linea antes de que llegue.
        private const float TrapDamageFraction = 0.15f;

        private readonly DungeonGenerator _generator = new DungeonGenerator();
        private List<DungeonFloor> _floors;
        private int _currentFloorIndex;
        private int _walkingCounter;
        private int _encounterThreshold;
        // Cuantos pasos "peligrosos" (Normal/Event) le quedan al efecto del Incienso: mientras sea
        // > 0, AccumulateDangerAndMaybeEncounter suma la mitad del peligro de cada celda en vez del
        // total, y se descuenta de a 1 por paso (no por unidad de peligro).
        private int _incenseStepsRemaining;
        private const int IncenseDurationSteps = 60;
        private readonly HashSet<int> _bossDefeatedFloors = new HashSet<int>();

        // FOE activo del piso actual (ver Gameplay/FoeController), null si este piso no tiene o ya
        // se lo vencio. _activeFoeFloorIndex es el piso para el que se creo, para no resetearlo al
        // reconstruir la geometria del MISMO piso (p.ej. al perforar una pared).
        private FoeController _activeFoe;
        private int _activeFoeFloorIndex = -1;

        // Disparador de flechas activo del piso actual (ver Gameplay/TrapDisparadorController),
        // null si este piso no tiene sala de trampas ArrowSweep o si ya fue destruido. Mismo
        // patron que _activeFoe/_activeFoeFloorIndex de arriba.
        private TrapDisparadorController _activeTrapDisparador;
        private int _activeTrapDisparadorFloorIndex = -1;

        // Roca que persigue (ver Gameplay/BoulderTrapController), null si este piso no tiene una
        // (solo la mini cueva del piso 0, ver DungeonGenerator.AddBoulderTrap). Mismo patron que
        // _activeTrapDisparador de arriba.
        private BoulderTrapController _activeBoulderTrap;
        private int _activeBoulderTrapFloorIndex = -1;

        // Corutina de la sala que colapsa activa (ver CollapseRoomRoutine) -- null mientras no haya
        // ninguna en curso. Un solo piso puede tener sala que colapsa (mini cueva del piso 0), asi
        // que un solo campo alcanza (a diferencia de FOE/disparador/roca, no hace falta reconstruir
        // nada al cambiar de piso: si el jugador se va a mitad de la ola, la corutina se corta sola).
        private Coroutine _collapseRoutine;
        private bool _isPlayerFalling;
        public bool IsPlayerFalling => _isPlayerFalling;

        // Indice en _floors del PRIMER piso de cada bioma secundario (clave = DungeonFloor.Biome:
        // 1 = Bioma 2/Cueva Intergalactica, 2 = Bioma de Cuevas, 3 = Patio, 4 = Castillo interior --
        // cada uno tiene varios pisos propios, esto es solo el punto de entrada/salida). Generico
        // por diseño: agregar un cuarto bioma no pisa el estado de los anteriores.
        private readonly Dictionary<int, int> _biomeEntryFloorIndex = new Dictionary<int, int>();

        private MetaProgress _meta;
        private int _deepestFloorReachedThisRun;
        private int _enemiesDefeatedThisRun;
        private int _bossesDefeatedThisRun;
        private int _lastRunPointsEarned;

        public DungeonFloor CurrentFloor => _floors[_currentFloorIndex];
        public int CurrentFloorIndex => _currentFloorIndex;

        // Nombre de RoomTemplate de la celda donde esta parado el jugador ahora mismo, o null si no
        // esta dentro de ninguna sala de autor (ver DungeonGen.RoomTemplate) -- usado por
        // PauseMenuHUD para el boton "Guardar forma" de la pestaña Mapa (pedido puntual: "reconocer
        // formas que se repitan entre runs").
        public string CurrentPredefinedRoomTemplateName =>
            IsReady && CurrentFloor.InBounds(player.CellX, player.CellY)
                ? CurrentFloor.Cells[player.CellX, player.CellY].PredefinedRoomTemplateName
                : null;
        // Para el fondo de combate (ver Gameplay.BattleStageController): que tematica de zona esta
        // pisando el jugador ahora mismo, para que el combate se sienta parte del mismo lugar en
        // vez de siempre el mismo fondo generico.
        public bool IsPlayerInIsolatedZone => player != null && CurrentFloor.InBounds(player.CellX, player.CellY)
            && CurrentFloor.Cells[player.CellX, player.CellY].IsIsolatedZone;
        public List<DungeonFloor> Floors => _floors;
        public bool IsCombatActive => combat != null && combat.IsActive;
        public int CurrentWalkingCounter => _walkingCounter;
        public int CurrentEncounterThreshold => _encounterThreshold;

        // FOE activo del piso actual, para que el mapa (MinimapUI / PauseMenuHUD) lo pueda dibujar.
        public FoeController ActiveFoe => _activeFoe;

        [Header("Debug")]
        [Tooltip("DEBUG/testeo: si esta prendido, el FOE se ve en el mapa aunque este parado en una celda que todavia no descubriste. Apagalo para el comportamiento real (niebla de guerra tambien lo tapa a el).")]
        public bool debugFoeAlwaysVisibleOnMap = true;

        // Toggleable desde el mapa fisico (ver PlayerMapEditorHUD, boton "Auto-pintar paredes"):
        // apagado por default (el mapa es 100% a mano, ver DungeonMapRenderer handDrawn). Si se
        // prende, cada celda nueva que el jugador pisa copia sus paredes REALES a PaintedWalls (ver
        // OnPlayerEnterCell) -- mismo resultado visual que el automapa clasico de antes, pero
        // escrito en la capa de anotaciones, asi convive con lo que el jugador ya dibujo a mano.
        public bool AutoPaintWalls;

        public MetaProgress Meta => _meta;
        public bool IsGameOverShopActive { get; private set; }
        public bool LastRunWasVictory { get; private set; }
        public bool LastRunReturnedToHub { get; private set; }
        public int LastRunPointsEarned => _lastRunPointsEarned;

        // false hasta que se genera la PRIMERA mazmorra (recien despues de que el jugador elige
        // Continuar/Nueva Partida en MainMenuHUD, ver ContinueRun/BeginBrandNewGame): mientras este
        // en false, CurrentFloor todavia no existe -- MinimapUI/AmbientParticles/DebugHUD/
        // GridPlayerController lo chequean antes de tocar cualquier cosa que dependa del piso, para
        // no explotar con un NullReferenceException mientras el menu inicial sigue abierto.
        public bool IsReady { get; private set; }
        public bool HasExistingSave => MetaSaveService.SaveExists();

        void Awake()
        {
            _meta = MetaSaveService.Load();
            if (combat != null)
            {
                combat.OnCombatFinished += HandleCombatFinished;
                combat.OnCombatFled += HandleCombatFled;
            }
            // La party y la mazmorra YA NO se generan aca: MainMenuHUD llama a ContinueRun() o
            // BeginBrandNewGame() segun lo que elija el jugador en el menu inicial.

            // Ciclo de dia y noche (pedido puntual): se agrega solo aca, no hace falta cablearlo a
            // mano en la escena -- ver DayNightCycle.
            if (GetComponent<DayNightCycle>() == null) gameObject.AddComponent<DayNightCycle>();
        }

        // "Continuar": misma party/progreso ya guardados (meta.PartyClasses, si el jugador ya habia
        // elegido una party antes; si no, PartyFactory.DefaultClasses via CombatManager.InitializeParty).
        public void ContinueRun()
        {
            if (combat == null) return;
            combat.InitializeParty(_meta);
            int seed = settings.seed != 0 ? settings.seed : System.Environment.TickCount;
            GenerateAndEnterDungeon(seed);
        }

        // "Nueva Partida": pisa el progreso guardado (banco de puntos, mejoras, items, todo) con
        // uno completamente nuevo, arma la party con las clases que el jugador acaba de elegir en
        // PartyCreationHUD, y la guarda ya mismo -- para que si cierra el juego a mitad de esta
        // primera mazmorra, "Continuar" la proxima vez use ESTA composicion, no la anterior.
        public void BeginBrandNewGame(IList<CharacterClass> chosenClasses)
        {
            if (combat == null) return;
            _meta = new MetaProgress();
            _meta.PartyClasses = new List<CharacterClass>(chosenClasses);
            MetaSaveService.Save(_meta);

            combat.InitializeParty(_meta);
            int seed = System.Environment.TickCount;
            GenerateAndEnterDungeon(seed);
        }

        // Cuantos pisos propios tiene el Bioma 2 (Cueva Intergalactica) -- "piso 1" y "piso 2" del
        // bioma, con el jefe (Kadulu) en el ultimo. Fijo, a diferencia de settings.floorCount: el
        // Bioma 2 es siempre este mismo tamano, no algo que se ajuste por partida.
        private const int Biome2FloorCount = 2;

        // Mismo criterio para el Bioma de Cuevas (Biome id 2 -- ver CombatManager.StartEncounter y
        // DungeonLevelBuilder para el resto de las diferencias visuales/de fauna). Pedido puntual:
        // "agregar pisos hasta el piso 2" -- antes 2 (indices 0-1), ahora 3 (indices 0-2), con el
        // jefe (Gorlok) en el ultimo (bossFloorStart se recalcula solo mas abajo).
        private const int CaveBiomeFloorCount = 3;
        // Bioma exterior: patio frontal, lateral y trasero. Bioma interior: salon,
        // dos niveles de torre y dos niveles de sotano.
        private const int PatioBiomeFloorCount = 3;
        private const int CastleBiomeFloorCount = 5;

        private void GenerateAndEnterDungeon(int seed)
        {
            _biomeReturnPoint.Clear();
            // El bosque necesita al menos los indices 0, 1 y 2: el jefe y la ruta al castillo
            // viven en el indice 2. Una configuracion reducida no debe borrarlos silenciosamente.
            int forestFloorCount = Mathf.Max(3, settings.floorCount);
            _floors = _generator.GenerateDungeon(
                forestFloorCount, settings.size, settings.size, seed, out var log,
                settings.stairPairsPerFloor,
                settings.bossFloorStart,
                settings.bossFloorInterval,
                settings.voidFraction,
                settings.dangerValueMin,
                settings.dangerValueMax,
                BuildLorePool(),
                extraOpeningChance: settings.extraOpeningChance);

            foreach (var line in log) Debug.Log(line);

            // Bioma 2 (Cueva Intergaláctica): un laberinto COMPLETO nuevo por derecho propio -- no
            // "el piso siguiente" del Bioma 1, sino su propia mazmorra de Biome2FloorCount pisos,
            // generada con el MISMO motor (mismas caracteristicas: jefe, trampas, candado+palanca,
            // cofres, lore, FOE) y apendiada a _floors. indexOffset la numera a continuacion de la
            // ultima del Bioma 1, asi las escaleras internas del propio Bioma 2 salen bien solas.
            // Solo se llega a su primer piso a traves de la Puerta Fria del piso 0 del Bioma 1 (ver
            // CellType.BiomeGate / TryInteract) -- nunca por la secuencia normal de escaleras del
            // Bioma 1, que nunca conecta con el.
            var biomeFloors = _generator.GenerateDungeon(
                Biome2FloorCount, settings.size, settings.size, seed ^ 0x5EED1234, out var biomeLog,
                stairPairsPerFloor: settings.stairPairsPerFloor,
                bossFloorStart: Biome2FloorCount - 1,
                bossFloorInterval: 1,
                voidFraction: settings.voidFraction,
                dangerValueMin: settings.dangerValueMin,
                dangerValueMax: settings.dangerValueMax,
                loreIdPool: BuildLorePool(),
                indexOffset: _floors.Count,
                biome: 1,
                extraOpeningChance: settings.extraOpeningChance);
            foreach (var line in biomeLog) Debug.Log(line);

            _floors.AddRange(biomeFloors);
            _biomeEntryFloorIndex.Clear();
            _biomeEntryFloorIndex[1] = biomeFloors[0].Index;

            // Bioma de Cuevas (Biome id 2): mazmorra completa aparte, apendiada a _floors. Reusa
            // el generador base y sus salas/recompensas, pero no el candado+palanca generico. Se llega desde CUALQUIER piso del
            // Bioma 1 (ver DungeonGenerator.PlaceCaveBiomeExit / CellType.CaveBiomeExit), nunca por
            // escalera normal.
            var caveBiomeFloors = _generator.GenerateDungeon(
                CaveBiomeFloorCount, settings.size, settings.size, seed ^ unchecked((int)0xCAFE5EED), out var caveBiomeLog,
                stairPairsPerFloor: settings.stairPairsPerFloor,
                bossFloorStart: CaveBiomeFloorCount - 1,
                bossFloorInterval: 1,
                voidFraction: settings.voidFraction,
                dangerValueMin: settings.dangerValueMin,
                dangerValueMax: settings.dangerValueMax,
                loreIdPool: BuildLorePool(),
                indexOffset: _floors.Count,
                biome: 2,
                extraOpeningChance: settings.extraOpeningChance);
            foreach (var line in caveBiomeLog) Debug.Log(line);

            _floors.AddRange(caveBiomeFloors);
            _biomeEntryFloorIndex[2] = caveBiomeFloors[0].Index;

            // Bioma del Patio (id 3): tres zonas exteriores encadenadas. El jefe del patio
            // aparece en el patio trasero; ambos portones conducen al bioma interior.
            var patioFloors = _generator.GenerateDungeon(
                PatioBiomeFloorCount, settings.size, settings.size, seed ^ unchecked((int)0xA710), out var patioLog,
                stairPairsPerFloor: 0,
                bossFloorStart: PatioBiomeFloorCount - 1,
                bossFloorInterval: 1,
                voidFraction: settings.voidFraction,
                dangerValueMin: settings.dangerValueMin,
                dangerValueMax: settings.dangerValueMax,
                loreIdPool: BuildLorePool(),
                indexOffset: _floors.Count,
                biome: 3,
                extraOpeningChance: settings.extraOpeningChance);
            foreach (var line in patioLog) Debug.Log(line);
            ConfigurePatioGeography(patioFloors);
            _floors.AddRange(patioFloors);
            _biomeEntryFloorIndex[3] = patioFloors[0].Index;

            // Bioma interior (id 4): el salon conecta dos rutas independientes. La torre sube
            // dos niveles hasta un jefe; el sotano baja dos niveles hasta otro jefe.
            var castleFloors = _generator.GenerateDungeon(
                CastleBiomeFloorCount, settings.size, settings.size, seed ^ unchecked((int)0xC4571E), out var castleLog,
                stairPairsPerFloor: 0,
                bossFloorStart: 2,
                bossFloorInterval: 2,
                voidFraction: settings.voidFraction,
                dangerValueMin: settings.dangerValueMin,
                dangerValueMax: settings.dangerValueMax,
                loreIdPool: BuildLorePool(),
                indexOffset: _floors.Count,
                biome: 4,
                extraOpeningChance: settings.extraOpeningChance);
            foreach (var line in castleLog) Debug.Log(line);
            ConfigureCastleInteriorGeography(castleFloors, patioFloors);
            _floors.AddRange(castleFloors);
            _biomeEntryFloorIndex[4] = castleFloors[0].Index;

            _bossDefeatedFloors.Clear();
            _deepestFloorReachedThisRun = 0;
            _enemiesDefeatedThisRun = 0;
            _bossesDefeatedThisRun = 0;

            _currentFloorIndex = 0;
            BuildActiveFloor();
            RollNewEncounterThreshold();

            var start = CurrentFloor.StartPos;
            // Mirando siempre hacia la PRIMERA salida real de la celda (ver DungeonCell.
            // FirstOpenDirection) en vez de al Norte fijo -- asi el jugador arranca mirando de
            // frente hacia donde de verdad puede caminar, que es tambien donde
            // DungeonLevelBuilder.BuildSpawnSignposts clava los carteles de controles.
            var startFacing = CurrentFloor.Cells[start.x, start.y].FirstOpenDirection();
            player.Warp(start.x, start.y, startFacing);
            OnPlayerEnterCell(start.x, start.y, advanceFoe: false);
            IsReady = true;
        }

        private static DungeonCell FindCastleCell(DungeonFloor floor, HashSet<(int x, int y)> used,
            System.Func<DungeonCell, float> score)
        {
            var walkable = floor.Cells.Cast<DungeonCell>()
                .Where(c => (c.Type == CellType.Normal || c.Type == CellType.Start)
                    && !c.IsBossRoom && !c.IsTrapRoom && !c.IsPredefinedRoom && !c.IsPuzzleTile
                    && !c.IsAmbushRoom && !c.IsCollapseRoom && !c.IsBoulderTrapCell
                    && !used.Contains((c.X, c.Y)))
                .ToList();
            var exterior = walkable.Where(c => !floor.IsInIsolatedZone(c.X, c.Y)).OrderBy(score).FirstOrDefault();
            return exterior ?? walkable.OrderBy(score).FirstOrDefault();
        }

        private static void SetCastleRouteCell(DungeonCell cell, CellType type,
            int targetFloor, DungeonCell target)
        {
            if (cell == null || target == null) return;
            cell.Type = type;
            cell.StairTargetFloor = targetFloor;
            cell.StairTargetX = target.X;
            cell.StairTargetY = target.Y;
        }

        private static void ResetCastleFloorLinks(IList<DungeonFloor> floors, int firstRegion)
        {
            foreach (var floor in floors)
            {
                floor.CastleRegion = firstRegion++;
                foreach (var cell in floor.Cells)
                {
                    if (cell.Type != CellType.StairsUp && cell.Type != CellType.StairsDown) continue;
                    cell.Type = CellType.Normal;
                    cell.StairTargetFloor = cell.StairTargetX = cell.StairTargetY = -1;
                }
            }
        }

        private static void ConfigurePatioGeography(IList<DungeonFloor> floors)
        {
            if (floors == null || floors.Count != PatioBiomeFloorCount) return;
            ResetCastleFloorLinks(floors, firstRegion: 0);
            var front = floors[0];
            var side = floors[1];
            var rear = floors[2];
            var frontUsed = new HashSet<(int, int)>();
            var sideUsed = new HashSet<(int, int)>();
            var rearUsed = new HashSet<(int, int)>();

            var frontSpawn = FindCastleCell(front, frontUsed,
                c => c.Y * 1000f + Mathf.Abs(c.X - (front.Width - 1) * 0.5f));
            if (frontSpawn == null) return;
            frontUsed.Add((frontSpawn.X, frontSpawn.Y));
            var frontGate = FindCastleCell(front, frontUsed,
                c => -c.Y * 1000f + Mathf.Abs(c.X - (front.Width - 1) * 0.5f));
            if (frontGate == null) return;
            frontUsed.Add((frontGate.X, frontGate.Y));
            var frontSideStair = FindCastleCell(front, frontUsed,
                c => Mathf.Abs(c.X) * 1000f + Mathf.Abs(c.Y - (front.Height - 1) * 0.5f));
            if (frontSideStair == null) return;
            frontUsed.Add((frontSideStair.X, frontSideStair.Y));

            var sideArrival = FindCastleCell(side, sideUsed,
                c => c.Y * 1000f + Mathf.Abs(c.X));
            if (sideArrival == null) return;
            sideUsed.Add((sideArrival.X, sideArrival.Y));
            var sideRearStair = FindCastleCell(side, sideUsed,
                c => -c.Y * 1000f + Mathf.Abs(c.X - (side.Width - 1) * 0.5f));
            if (sideRearStair == null) return;
            sideUsed.Add((sideRearStair.X, sideRearStair.Y));

            var rearArrival = FindCastleCell(rear, rearUsed,
                c => c.Y * 1000f + Mathf.Abs(c.X - (rear.Width - 1) * 0.5f));
            if (rearArrival == null) return;
            rearUsed.Add((rearArrival.X, rearArrival.Y));
            var rearGate = FindCastleCell(rear, rearUsed,
                c => c.Y * 1000f + Mathf.Abs(c.X - (rear.Width - 1) * 0.72f));
            if (rearGate == null) return;

            var oldStart = front.Cells[front.StartPos.x, front.StartPos.y];
            if (oldStart != frontSpawn && oldStart.Type == CellType.Start) oldStart.Type = CellType.Normal;
            frontSpawn.Type = CellType.Start;
            front.StartPos = (frontSpawn.X, frontSpawn.Y);

            // Ruta exterior: frente -> lateral -> fondo. El jefe del patio ocupa el patio trasero;
            // desde ahi una puerta al sur entra por la parte trasera del castillo.
            SetCastleRouteCell(frontSideStair, CellType.StairsUp, side.Index, sideArrival);
            SetCastleRouteCell(sideArrival, CellType.StairsDown, front.Index, frontSideStair);
            SetCastleRouteCell(sideRearStair, CellType.StairsUp, rear.Index, rearArrival);
            SetCastleRouteCell(rearArrival, CellType.StairsDown, side.Index, sideRearStair);
            // El porton frontal y la puerta trasera son dos entradas reales al salon del castillo.
            // La puerta trasera queda al sur del torreón, despues del patio del jefe.
            frontGate.Type = CellType.CastleMainEntrance;
            rearGate.Type = CellType.CastleRearEntrance;
        }

        private static DungeonCell FindCastleBossRoomCell(DungeonFloor floor, HashSet<(int, int)> used,
            System.Func<DungeonCell, float> score)
        {
            if (floor.BossRoomCells == null) return null;
            return floor.BossRoomCells
                .Select(p => floor.Cells[p.Item1, p.Item2])
                .Where(c => c.Type == CellType.Normal && !used.Contains((c.X, c.Y)))
                .OrderBy(score)
                .FirstOrDefault();
        }

        private static void ConfigureCastleInteriorGeography(IList<DungeonFloor> floors, IList<DungeonFloor> patioFloors)
        {
            if (floors == null || floors.Count != CastleBiomeFloorCount) return;
            ResetCastleFloorLinks(floors, firstRegion: 0);
            var hall = floors[0];
            var towerOne = floors[1];
            var towerBoss = floors[2];
            var cellarOne = floors[3];
            var cellarBoss = floors[4];
            var hallUsed = new HashSet<(int, int)>();
            var towerOneUsed = new HashSet<(int, int)>();
            var cellarOneUsed = new HashSet<(int, int)>();
            var towerBossUsed = new HashSet<(int, int)>();
            var cellarBossUsed = new HashSet<(int, int)>();
            // Conserva la marca Start del salon para poder regresar al patio desde la entrada del bioma.
            hallUsed.Add(hall.StartPos);

            // Dos portones llevan desde los patios al salon principal; desde ese salon nacen las
            // rutas independientes de la torre (norte/arriba) y las criptas (sur/abajo).
            var mainEntry = FindCastleCell(hall, hallUsed,
                c => -c.Y * 1000f + Mathf.Abs(c.X - (hall.Width - 1) * 0.5f));
            if (mainEntry == null) return;
            hallUsed.Add((mainEntry.X, mainEntry.Y));
            var rearEntry = FindCastleCell(hall, hallUsed,
                c => c.Y * 1000f + Mathf.Abs(c.X - (hall.Width - 1) * 0.5f));
            if (rearEntry == null) return;
            hallUsed.Add((rearEntry.X, rearEntry.Y));
            var towerExit = FindCastleCell(hall, hallUsed,
                c => -c.Y * 1000f + c.X);
            if (towerExit == null) return;
            hallUsed.Add((towerExit.X, towerExit.Y));
            var cellarExit = FindCastleCell(hall, hallUsed,
                c => c.Y * 1000f + (hall.Width - 1 - c.X));
            if (cellarExit == null) return;
            hallUsed.Add((cellarExit.X, cellarExit.Y));

            var towerArrival = FindCastleCell(towerOne, towerOneUsed,
                c => c.Y * 1000f + Mathf.Abs(c.X - (towerOne.Width - 1) * 0.5f));
            if (towerArrival == null) return;
            towerOneUsed.Add((towerArrival.X, towerArrival.Y));
            var towerAscent = FindCastleCell(towerOne, towerOneUsed,
                c => -c.Y * 1000f + Mathf.Abs(c.X - (towerOne.Width - 1) * 0.5f));
            if (towerAscent == null) return;

            var towerBossArrival = FindCastleBossRoomCell(towerBoss, towerBossUsed,
                c => c.Y * 1000f + Mathf.Abs(c.X - (towerBoss.Width - 1) * 0.5f));
            if (towerBossArrival == null) return;
            towerBossUsed.Add((towerBossArrival.X, towerBossArrival.Y));

            var cellarArrival = FindCastleCell(cellarOne, cellarOneUsed,
                c => -c.Y * 1000f + Mathf.Abs(c.X - (cellarOne.Width - 1) * 0.5f));
            if (cellarArrival == null) return;
            cellarOneUsed.Add((cellarArrival.X, cellarArrival.Y));
            var cellarDescent = FindCastleCell(cellarOne, cellarOneUsed,
                c => c.Y * 1000f + Mathf.Abs(c.X - (cellarOne.Width - 1) * 0.5f));
            if (cellarDescent == null) return;
            var cellarBossArrival = FindCastleBossRoomCell(cellarBoss, cellarBossUsed,
                c => -c.Y * 1000f + Mathf.Abs(c.X - (cellarBoss.Width - 1) * 0.5f));
            if (cellarBossArrival == null) return;

            SetCastleRouteCell(towerExit, CellType.StairsUp, towerOne.Index, towerArrival);
            SetCastleRouteCell(towerArrival, CellType.StairsDown, hall.Index, towerExit);
            SetCastleRouteCell(towerAscent, CellType.StairsUp, towerBoss.Index, towerBossArrival);
            SetCastleRouteCell(towerBossArrival, CellType.StairsDown, towerOne.Index, towerAscent);
            SetCastleRouteCell(cellarExit, CellType.StairsDown, cellarOne.Index, cellarArrival);
            SetCastleRouteCell(cellarArrival, CellType.StairsUp, hall.Index, cellarExit);
            SetCastleRouteCell(cellarDescent, CellType.StairsDown, cellarBoss.Index, cellarBossArrival);
            SetCastleRouteCell(cellarBossArrival, CellType.StairsUp, cellarOne.Index, cellarDescent);

            // Las entradas se enlazan en el mismo marco con el patio, despues de configurar ambos.
            LinkPatioEntrancesToInterior(patioFloors, floors, mainEntry, rearEntry);
        }

        private static void LinkPatioEntrancesToInterior(IList<DungeonFloor> patioFloors, IList<DungeonFloor> interiorFloors,
            DungeonCell mainEntry, DungeonCell rearEntry)
        {
            if (patioFloors == null || patioFloors.Count != PatioBiomeFloorCount || mainEntry == null || rearEntry == null) return;
            var front = patioFloors[0];
            var rear = patioFloors[2];
            var frontGate = front.Cells.Cast<DungeonCell>().FirstOrDefault(c => c.Type == CellType.CastleMainEntrance);
            var rearGate = rear.Cells.Cast<DungeonCell>().FirstOrDefault(c => c.Type == CellType.CastleRearEntrance);
            if (frontGate == null || rearGate == null) return;
            SetCastleRouteCell(frontGate, CellType.CastleMainEntrance, interiorFloors[0].Index, mainEntry);
            SetCastleRouteCell(mainEntry, CellType.CastleMainEntrance, front.Index, frontGate);
            SetCastleRouteCell(rearGate, CellType.CastleRearEntrance, interiorFloors[0].Index, rearEntry);
            SetCastleRouteCell(rearEntry, CellType.CastleRearEntrance, rear.Index, rearGate);
        }

        // Prioriza fragmentos de lore que el jugador TODAVIA NO descubrio en runs anteriores
        // (AssignLoreLock en DungeonGenerator asigna por indice de piso sobre este pool, en orden):
        // los no descubiertos van primero, asi los pisos de una run nueva casi siempre ofrecen algo
        // realmente nuevo para encontrar, en vez de repetir uno ya leido en el Codex mientras
        // todavia queden otros sin leer. Los ya descubiertos quedan al final, como relleno para
        // cuando floorCount supera la cantidad de fragmentos que existen o ya estan todos leidos --
        // en ese caso, OnPlayerEnterCell avisa que "ya lo conocias" en vez de tratarlo como nuevo.
        private string[] BuildLorePool()
        {
            var undiscovered = new List<string>();
            var discovered = new List<string>();
            foreach (var entry in LoreCatalog.All)
            {
                // Las 3 pistas de la Puerta Fria son de colocacion fija y garantizada en el piso 0
                // (ver DungeonGenerator.PlaceBiomeGateClues) -- nunca deben terminar sueltas en
                // este pool general, que las repartiria en cualquier piso atadas a un atajo random.
                if (System.Array.IndexOf(DungeonGenerator.BiomeGateLoreIds, entry.Id) >= 0) continue;
                if (_meta.IsLoreUnlocked(entry.Id)) discovered.Add(entry.Id);
                else undiscovered.Add(entry.Id);
            }
            undiscovered.AddRange(discovered);
            return undiscovered.ToArray();
        }

        private void BuildActiveFloor()
        {
            // Mantiene el horizonte del castillo y las balizas de escalera dentro del frustum aunque
            // la escena serializada tenga un far clip corto; la niebla sigue ocultando el terreno.
            var camera = Camera.main;
            if (camera != null) camera.farClipPlane = Mathf.Max(camera.farClipPlane, settings.cellSize * 55f);
            levelBuilder.Build(CurrentFloor, settings.cellSize, settings.wallHeight, settings.wallThickness);
            RefreshActiveFoe();
            RefreshActiveTrapDisparador();
            RefreshActiveBoulderTrap();
        }

        // Se llama cada vez que se (re)construye la geometria del piso activo (entrar/cambiar de
        // piso, perforar una pared con el Perforador). Si ya hay un FOE instanciado PARA ESTE
        // MISMO piso, lo deja como esta (no le resetea la patrulla solo porque perforaste una
        // pared). Si cambio de piso, el FOE NO desaparece: guarda su estado en vivo en el piso que
        // se deja (ver FoeController.SaveStateTo) antes de destruir el GameObject, asi que si el
        // piso nuevo tambien tiene FOE (o el jugador vuelve mas tarde al que se dejo), retoma
        // exactamente donde quedo en vez de reaparecer reseteado.
        private void RefreshActiveFoe()
        {
            if (_activeFoe != null && _activeFoeFloorIndex == _currentFloorIndex) return;

            if (_activeFoe != null)
            {
                _activeFoe.SaveStateTo(_floors[_activeFoeFloorIndex]);
                Destroy(_activeFoe.gameObject);
                _activeFoe = null;
            }
            _activeFoeFloorIndex = _currentFloorIndex;
            if (!CurrentFloor.HasFoe) return;

            var foeGo = new GameObject("Foe");
            _activeFoe = foeGo.AddComponent<FoeController>();
            _activeFoe.Initialize(CurrentFloor, CanMove, CellToWorld, settings.cellSize, EnemyFactory.CreateFoe(_currentFloorIndex).MaxHP, GetComponent<DayNightCycle>());
        }

        // Mismo patron que RefreshActiveFoe: solo recrea el disparador si cambio de piso (perforar
        // una pared no lo debe resetear). No crea nada si el piso no tiene sala ArrowSweep, o si
        // ya fue destruido (ver DungeonGenerator.TryDestroyTrapDisparador).
        private void RefreshActiveTrapDisparador()
        {
            if (_activeTrapDisparador != null && _activeTrapDisparadorFloorIndex == _currentFloorIndex) return;

            if (_activeTrapDisparador != null)
            {
                Destroy(_activeTrapDisparador.gameObject);
                _activeTrapDisparador = null;
            }
            _activeTrapDisparadorFloorIndex = _currentFloorIndex;
            if (!CurrentFloor.HasTrapRoom || CurrentFloor.TrapKind != TrapKind.ArrowSweep || CurrentFloor.TrapDisabled) return;

            var go = new GameObject("TrapDisparador");
            _activeTrapDisparador = go.AddComponent<TrapDisparadorController>();
            _activeTrapDisparador.Initialize(CurrentFloor, CellToWorld, () => (player.CellX, player.CellY),
                () => ApplyTrapDamage("¡Una flecha te atraviesa el paso!"),
                () => _activeFoe != null ? ((int x, int y)?)(_activeFoe.X, _activeFoe.Y) : null,
                OnTrapArrowHitFoe, settings.cellSize);
        }

        // Mismo patron que RefreshActiveTrapDisparador, para la roca que persigue (ver
        // DungeonGenerator.AddBoulderTrap / Gameplay/BoulderTrapController) -- solo la mini cueva
        // del piso 0 tiene una.
        private void RefreshActiveBoulderTrap()
        {
            if (_activeBoulderTrap != null && _activeBoulderTrapFloorIndex == _currentFloorIndex) return;

            if (_activeBoulderTrap != null)
            {
                Destroy(_activeBoulderTrap.gameObject);
                _activeBoulderTrap = null;
            }
            _activeBoulderTrapFloorIndex = _currentFloorIndex;
            if (!CurrentFloor.HasBoulderTrap) return;

            var go = new GameObject("BoulderTrap");
            _activeBoulderTrap = go.AddComponent<BoulderTrapController>();
            _activeBoulderTrap.Initialize(CurrentFloor, CellToWorld, () => (player.CellX, player.CellY),
                () => ApplyTrapDamage("¡La roca te aplasta contra la pared!"), settings.cellSize);
        }

        // Arranca la ola de colapso de la sala que colapsa (ver DungeonGenerator.
        // AddCollapsingRushRoom), si todavia no hay una en curso.
        private void TriggerCollapseRoom()
        {
            if (_collapseRoutine != null || CurrentFloor.CollapseOrder == null) return;
            _collapseRoutine = StartCoroutine(CollapseRoomRoutine(CurrentFloor, _currentFloorIndex));
        }

        // Cada CollapseSecondsPerTile segundos, la siguiente celda de CollapseOrder (ya ordenada
        // por distancia real desde la entrada, ver AddCollapsingRushRoom) queda IsCollapseFallen y
        // pierde su piso visual (ver DungeonLevelBuilder.CollapseFloorVisual) -- si el jugador
        // sigue parado justo ahi cuando le toca el turno, cae de inmediato en vez de esperar a que
        // de un paso mas.
        private const float CollapseSecondsPerTile = 0.4f;

        private System.Collections.IEnumerator CollapseRoomRoutine(DungeonFloor floor, int floorIndexAtStart)
        {
            foreach (var (cx, cy) in floor.CollapseOrder)
            {
                yield return new WaitForSeconds(CollapseSecondsPerTile);

                // El jugador se fue de este piso a mitad de la ola (escalera, atajo, cambio por
                // otro colapso/Goteras) -- no seguir tocando un piso que ya no es el activo.
                if (_currentFloorIndex != floorIndexAtStart) break;

                var cell = floor.Cells[cx, cy];
                cell.IsCollapseFallen = true;
                levelBuilder?.CollapseFloorVisual(cx, cy);

                if (player != null && player.CellX == cx && player.CellY == cy && !IsCombatActive)
                {
                    if (BeginPitFall(true, "¡El suelo se rompe! Caés por un túnel profundo hacia las cuevas."))
                        break;
                    ApplyTrapDamage("¡El piso colapsa bajo tus pies!");
                }
            }
            _collapseRoutine = null;
        }

        private bool BeginPitFall(bool preferCaveBiome, string message)
        {
            int targetIndex = -1;
            if (preferCaveBiome && CurrentFloor.Biome == 0)
            {
                if (!_biomeEntryFloorIndex.TryGetValue(2, out targetIndex)) targetIndex = -1;
                else if (CurrentFloor.CaveBiomeExitPos.HasValue)
                {
                    var returnPos = CurrentFloor.CaveBiomeExitPos.Value;
                    _biomeReturnPoint[2] = (_currentFloorIndex, returnPos.x, returnPos.y);
                }
            }
            else if (preferCaveBiome && CurrentFloor.Biome == 2)
            {
                for (int i = _currentFloorIndex + 1; i < _floors.Count; i++)
                {
                    if (_floors[i].Biome != 2) continue;
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex < 0 || targetIndex >= _floors.Count)
                targetIndex = _currentFloorIndex + 1 < _floors.Count ? _currentFloorIndex + 1 : -1;
            if (targetIndex < 0) return false;

            var target = _floors[targetIndex];
            BeginFallToFloor(targetIndex, target.StartPos.x, target.StartPos.y, message);
            return true;
        }

        private void BeginFallToFloor(int targetFloorIndex, int spawnX, int spawnY, string message)
        {
            if (_isPlayerFalling) return;
            StartCoroutine(FallToFloorRoutine(targetFloorIndex, spawnX, spawnY, message));
        }

        private System.Collections.IEnumerator FallToFloorRoutine(int targetFloorIndex, int spawnX, int spawnY, string message)
        {
            _isPlayerFalling = true;
            if (player == null)
            {
                _isPlayerFalling = false;
                ChangeFloor(targetFloorIndex, spawnX, spawnY);
                if (hud != null) hud.SetLastMessage(message);
                yield break;
            }

            Vector3 start = player.transform.position;
            Quaternion startRotation = player.transform.rotation;
            float duration = 1.35f;
            float fallDistance = Mathf.Max(6f, settings.wallHeight * 2.6f);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float easedDrop = t * t;
                player.transform.position = start + Vector3.down * (fallDistance * easedDrop);
                float tilt = Mathf.Sin(t * Mathf.PI * 2f) * 8f;
                player.transform.rotation = startRotation * Quaternion.Euler(t * 42f, 0f, tilt);
                yield return null;
            }

            _isPlayerFalling = false;
            ChangeFloor(targetFloorIndex, spawnX, spawnY);
            if (hud != null) hud.SetLastMessage(message);
        }

        private void UpdateCaveLighting(DungeonCell cell)
        {
            var dayNight = GetComponent<DayNightCycle>();
            if (dayNight == null) return;
            bool inCave = CurrentFloor.Biome == 2 || (CurrentFloor.Biome == 0 && cell.IsIsolatedZone);
            dayNight.SetCaveEnvironment(inCave);
        }

        // La flecha (ya en vuelo, disparada por el jugador o por el FOE pisando la linea) alcanzo
        // la celda donde esta parado el FOE en ese instante: a diferencia del jugador, el FOE SI
        // puede morir de esto (ver FoeController.ApplyTrapDamage).
        private void OnTrapArrowHitFoe()
        {
            if (_activeFoe == null) return;
            bool died = _activeFoe.ApplyTrapDamage(TrapDamageFraction);
            if (died)
            {
                Destroy(_activeFoe.gameObject);
                _activeFoe = null;
                CurrentFloor.FoePatrolRoute = null;
                if (hud != null) hud.SetLastMessage("¡El FOE cayo en su propia trampa!");
            }
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

        // advanceFoe=false en los 3 lugares que TELETRANSPORTAN al jugador en vez de moverlo un
        // paso real (spawn inicial, atajo, cambio de piso por escalera -- ver los call sites): sin
        // esto, aparecer justo en la celda donde el FOE ya estaba parado (perfectamente posible, es
        // una celda caminable comun de su ruta) disparaba combate en el mismo frame de llegada, sin
        // ninguna chance de esquivarlo. Un paso real del jugador (GridPlayerController) si lo avanza.
        public void OnPlayerEnterCell(int x, int y, bool advanceFoe = true)
        {
            if (_isPlayerFalling) return;
            var cell = CurrentFloor.Cells[x, y];
            UpdateCaveLighting(cell);
            cell.Discovered = true;
            if (AutoPaintWalls) AutoPaintCellWalls(cell);

            // La celda central marcada como jefe inicia el combate al pisarla; no depende de que
            // el jugador adivine que tambien debe pulsar Espacio sobre el marcador.
            if (cell.Type == CellType.Boss
                && !_bossDefeatedFloors.Contains(_currentFloorIndex)
                && !IsCombatActive && combat != null)
            {
                if (hud != null) hud.SetLastMessage("¡El jefe te bloquea el paso!");
                combat.StartEncounter(isBoss: true, floorIndex: _currentFloorIndex,
                    biome: CurrentFloor.Biome, castleRegion: CurrentFloor.CastleRegion);
            }

            if (cell.Type == CellType.Normal && !IsCombatActive)
                AccumulateDangerAndMaybeEncounter(cell);

            // El FOE (ver Gameplay/FoeController) da UN paso por cada paso del jugador -- si eso lo
            // deja en la MISMA celda, colisiona y arranca un combate 1 contra 1 (huida mas dificil,
            // ver CombatManager.StartFoeEncounter). No avanza mientras ya hay un combate en curso
            // (p.ej. el encuentro random de esta misma celda ya empezo primero).
            if (advanceFoe && _activeFoe != null && !IsCombatActive)
            {
                bool collided = _activeFoe.AdvanceStep(x, y);
                if (collided) combat.StartFoeEncounter(EnemyFactory.CreateFoe(_currentFloorIndex));
                else HandleFoeSteppedOnTrap();
            }

            // Sala de trampas (ver DungeonGenerator.AddTrapRoom): SpikeCells duele apenas se pisa
            // (no hay forma de esquivar un pico que ya esta bajo tus pies). ArrowSweep en cambio
            // solo ARRANCA el disparo (ver TrapDisparadorController.Fire) -- el dano real llega
            // despues, cuando la flecha recorre la sala y de verdad te alcanza; hasta entonces hay
            // tiempo real para salir de la linea.
            if (cell.IsTrapCell && !IsCombatActive && !CurrentFloor.TrapDisabled)
            {
                if (CurrentFloor.TrapKind == TrapKind.ArrowSweep)
                    _activeTrapDisparador?.Fire();
                else
                    ApplyTrapDamage("¡Pisaste una trampa de picos!");
            }

            // Roca que persigue (ver DungeonGenerator.AddBoulderTrap / Gameplay/
            // BoulderTrapController): pisar el extremo de arranque del corredor la dispara -- se
            // puede volver a disparar cada vez que se re-entra, a diferencia de la sala que colapsa.
            if (cell.IsBoulderTrapCell && !IsCombatActive && CurrentFloor.HasBoulderTrap
                && (x, y) == CurrentFloor.BoulderTrapStartPos)
                _activeBoulderTrap?.Trigger();

            // Sala de emboscada (ver DungeonGenerator.AddAmbushRoom): pisar CUALQUIER celda de la
            // sala dispara el combate, una sola vez por run -- despues queda "limpia".
            if (cell.IsAmbushRoom && !CurrentFloor.AmbushTriggered && !IsCombatActive && combat != null)
            {
                CurrentFloor.AmbushTriggered = true;
                if (hud != null) hud.SetLastMessage("¡Una emboscada!");
                combat.StartEncounter(isBoss: false, floorIndex: _currentFloorIndex, biome: CurrentFloor.Biome);
            }

            // Trampa de goblins del Bioma de Cuevas (ver DungeonGenerator.AddGoblinTraps): a
            // diferencia de la emboscada de arriba (UNA bandera por piso), cada celda dispara su
            // PROPIO combate una sola vez -- puede haber varias independientes en el mismo piso.
            if (cell.IsGoblinTrap && !cell.GoblinTrapTriggered && !IsCombatActive && combat != null)
            {
                cell.GoblinTrapTriggered = true;
                if (hud != null) hud.SetLastMessage("¡El suelo cede -- goblins saltan de sus escondites!");
                combat.StartEncounter(isBoss: false, floorIndex: _currentFloorIndex, biome: CurrentFloor.Biome);
            }

            // Boveda de cascada (ver DungeonGenerator.AddWaterfallVault): pisar la boveda dispara el
            // combate contra su guardian, una sola vez por run -- el tesoro garantizado se entrega
            // al ganar (ver HandleCombatFinished/RollWaterfallVaultLoot), no al pisar.
            if (cell.IsWaterfallVaultRoom && !CurrentFloor.WaterfallVaultTriggered && !IsCombatActive && combat != null)
            {
                CurrentFloor.WaterfallVaultTriggered = true;
                if (hud != null) hud.SetLastMessage("¡Un guardián protege este lugar!");
                combat.StartVaultEncounter(EnemyFactory.CreateWaterfallGuardian(_currentFloorIndex));
            }

            // Sala que colapsa (ver DungeonGenerator.AddCollapsingRushRoom): pisar la entrada
            // arranca la ola de colapso (una sola vez por run). Si la celda en la que el jugador
            // ACABA de entrar ya habia colapsado antes (volvio a pisarla despues de la ola, o la
            // ola le alcanzo los pies mientras dudaba, ver CollapseRoomRoutine), cae de inmediato --
            // mismo mecanismo que Goteras mas abajo.
            if (cell.IsCollapseRoom && !CurrentFloor.CollapseTriggered && !IsCombatActive
                && (x, y) == CurrentFloor.CollapseEntryPos)
            {
                CurrentFloor.CollapseTriggered = true;
                TriggerCollapseRoom();
                if (hud != null) hud.SetLastMessage("¡El piso empieza a ceder detrás tuyo! ¡Corré!");
            }
            if (cell.IsCollapseFallen && !IsCombatActive)
            {
                if (BeginPitFall(true, "¡El suelo se rompe! Caés por un túnel profundo hacia las cuevas."))
                    return;
                ApplyTrapDamage("¡El piso colapsa bajo tus pies!");
            }

            // Peligro de una sala de autor con forma prediseñada (ver RoomTemplate '^' /
            // DungeonGenerator.TryStampRoomAt): a diferencia de la trampa de arriba, esta es
            // personalidad FIJA de esa sala -- no depende de CurrentFloor.TrapKind/TrapDisabled (ese
            // sistema es exclusivo de AddTrapRoom) y no existe forma de desactivarla, solo de
            // esquivarla a pie.
            if (cell.IsPredefinedRoomHazard && !IsCombatActive)
                ApplyTrapDamage("¡Un peligro oculto en la sala te lastima!");

            // Sala de pistas (ver DungeonGenerator.AddLoreCorridorRoom): pisar una celda de la
            // grilla que NO es piso real. En Goteras (agua) el piso directamente NO ESTA (ver
            // DungeonLevelBuilder.Build) y esto te hace caer de verdad al piso de abajo -- no
            // duele, la particula rosada es una guia de camino, no una trampa. Brasas/Polvo de
            // Cuarzo siguen ocultos: duele igual que una trampa de picos, el tell de particulas
            // (ver DungeonLevelBuilder.BuildPuzzleTile) es lo unico que avisa antes de pisar.
            if (cell.IsPuzzleTile && !cell.IsPuzzleTileSafe && !IsCombatActive)
            {
                if (CurrentFloor.LoreCorridorKind == PuzzleKind.Goteras && _currentFloorIndex + 1 < _floors.Count)
                {
                    var below = _floors[_currentFloorIndex + 1];
                    BeginFallToFloor(_currentFloorIndex + 1, below.StartPos.x, below.StartPos.y,
                        "¡El piso cede bajo tus pies! Caes al piso de abajo.");
                    return;
                }
                ApplyTrapDamage("¡El piso cede bajo tus pies!");
            }

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
                        ? CurrentFloor.Biome == 3
                            ? "El guardián del patio ya cayó. La puerta trasera lleva al castillo."
                            : "El jefe de esta ruta ya fue derrotado."
                        : IsCombatActive
                            ? "¡El jefe te bloquea el paso!"
                            : "¡Sala del jefe! Entra en la marca roja para iniciar el combate.";
                    break;
                case CellType.Lore:
                    // "Ya lo conocias" se decide ANTES de UnlockLore (que es idempotente y no
                    // devuelve si ya estaba desbloqueado de una run anterior, solo si esta celda en
                    // particular era nueva). BuildLorePool ya prioriza que esto casi nunca pase,
                    // pero si floorCount supera la cantidad de fragmentos que existen, o el jugador
                    // ya los leyo TODOS, no queda otra que repetir uno -- avisa distinto en vez de
                    // quedarse en silencio como antes.
                    bool wasAlreadyKnown = _meta.IsLoreUnlocked(cell.AssignedLoreId);
                    _meta.UnlockLore(cell.AssignedLoreId);
                    if (!cell.EventConsumed)
                    {
                        cell.EventConsumed = true;
                        MetaSaveService.Save(_meta);
                        var entry = LoreCatalog.Find(cell.AssignedLoreId);
                        string loreMessage = wasAlreadyKnown
                            ? $"Ya conocías este fragmento de lore: \"{(entry != null ? entry.Title : cell.AssignedLoreId)}\" (no había ninguno nuevo para este piso)."
                            : entry != null
                                ? $"¡Nuevo fragmento de lore! \"{entry.Title}\" (revisa el Códex en el menú de pausa)."
                                : "¡Encontraste un fragmento de lore!";
                        if (Dialogue != null) Dialogue.Show("Códex", loreMessage);
                        else message = loreMessage; // fallback: linea ambiente si no hay DialogueManager
                    }
                    break;
                case CellType.LockedDoor:
                    message = FindDoorAt(x, y) is LockedDoor lockedHere && lockedHere.IsUnlocked
                        ? null
                        : "Un mecanismo sella el paso. Hace falta encontrar la palanca que lo abre.";
                    break;
                case CellType.Lever:
                    var doorForLever = FindDoorForLever(x, y);
                    message = doorForLever == null
                        ? null
                        : doorForLever.IsUnlocked
                            ? "La palanca ya esta activada."
                            : "Una palanca. Presiona Espacio para activarla y abrir el candado de forma permanente.";
                    break;
                case CellType.Treasure:
                    message = cell.EventConsumed
                        ? null
                        : "Un cofre. Presiona Espacio para abrirlo.";
                    break;
                case CellType.BiomeGate:
                    message = "Una corriente helada sale de la grieta que acabas de abrir. Presiona Espacio para cruzar.";
                    break;
                case CellType.CaveBiomeExit:
                    message = "Una escalera de piedra baja hacia la oscuridad. Presiona Espacio para descender.";
                    break;
                case CellType.CastleGate:
                    message = "¡Encontraste el acceso al castillo! Párate en el arco magenta y presiona Espacio para entrar.";
                    break;
                case CellType.CastleMainEntrance:
                    message = CurrentFloor.Biome == 4
                        ? "El portón norte devuelve al patio frontal. Presiona Espacio para salir."
                        : "El portón principal del castillo. Presiona Espacio para entrar.";
                    break;
                case CellType.CastleRearEntrance:
                    message = CurrentFloor.Biome == 4
                        ? "La puerta sur devuelve al patio trasero. Presiona Espacio para salir."
                        : "La puerta trasera, al sur del torreón. Presiona Espacio para entrar.";
                    break;
                case CellType.HubPortal:
                    message = "El portal al refugio está abierto. Presiona Espacio para volver al hub.";
                    break;
                case CellType.Start:
                    // Solo el Start del PRIMER piso de CADA bioma secundario es la vuelta a su
                    // entrada -- el resto de sus pisos (todo bioma tiene varios, ver
                    // GenerateAndEnterDungeon) tambien tienen su propio Start (todo piso lo tiene,
                    // es de donde arrancarias si entraras por ahi), pero ese es solo un marcador
                    // normal, no una salida.
                    if (CurrentFloor.Biome != 0 && IsBiomeEntryFloor(_currentFloorIndex, CurrentFloor.Biome))
                        message = CurrentFloor.Biome switch
                        {
                            1 => "La Puerta Fría, del otro lado. Presiona Espacio para volver.",
                            2 => "La escalera de piedra, del otro lado. Presiona Espacio para volver.",
                            3 => "El arco del bosque, del otro lado. Presiona Espacio para volver.",
                            _ => "El acceso al patio, del otro lado. Presiona Espacio para volver."
                        };
                    break;
            }
            if (message != null && hud != null) hud.SetLastMessage(message);
        }

        // Copia las paredes REALES de la celda a PaintedWalls -- mismo resultado visual que el
        // automapa clasico (revelar solo, sin dibujar nada del jugador), pero escrito en la capa de
        // anotaciones para que conviva con lo que ya dibujaste a mano en vez de pisarlo. Solo se usa
        // si AutoPaintWalls esta prendido (ver el toggle en PlayerMapEditorHUD).
        private static void AutoPaintCellWalls(DungeonCell cell)
        {
            foreach (var d in DirectionExtensions.All)
                if (cell.HasWall(d)) cell.SetPaintedWall(d, true);
        }

        // Cofre garantizado (3 a 5 por piso, ver DungeonGenerator.EnsureTreasure): 50% plata, 30%
        // un arma con habilidad (veneno/sangrado/espinas -- si ya la tenes, plata equivalente en
        // vez de un duplicado inutil), 20% herramienta (un accesorio utilitario, o si ya lo tenes,
        // una carga extra de exploracion al azar). El PRIMER cofre que abris en toda la partida
        // (ver MetaProgress.FirstChestBonusGiven) suma ademas, siempre, una carga de Perforador de
        // regalo -- sin depender del sorteo, para que jugar la primera vez nunca te deje sin forma
        // de perforar un camino cerrado. foundItem sale null salvo que el premio sea un equipo
        // nuevo (arma/herramienta): en ese caso el llamador (ver ShowItemFoundDialogue) arma un
        // dialogo mas rico con nombre/descripcion/stats/clases en vez de solo el mensaje plano.
        private (string message, EquipmentItem foundItem) RollTreasureLoot()
        {
            string firstChestBonus = "";
            if (!_meta.FirstChestBonusGiven)
            {
                _meta.FirstChestBonusGiven = true;
                _meta.DrillCharges++;
                firstChestBonus = " Ademas, tu primer cofre trae de regalo +1 carga de Perforador.";
            }

            float roll = Random.value;
            if (roll < 0.5f)
            {
                _meta.BankedPoints += TreasurePointsReward;
                return ($"¡Encontraste un cofre! +{TreasurePointsReward} puntos.{firstChestBonus}", null);
            }

            if (roll < 0.8f)
            {
                string equipId = TreasureEquipmentIds[Random.Range(0, TreasureEquipmentIds.Length)];
                var equip = EquipmentCatalog.Find(equipId);
                if (_meta.OwnsItem(equipId))
                {
                    _meta.BankedPoints += equip.Cost;
                    return ($"¡Encontraste un cofre! Ya tenías {equip.Name} -- +{equip.Cost} puntos en su lugar.{firstChestBonus}", null);
                }
                _meta.AddToInventory(equipId);
                return (firstChestBonus, equip);
            }

            const string toolId = "guantes_del_explorador";
            if (!_meta.OwnsItem(toolId))
            {
                _meta.AddToInventory(toolId);
                return (firstChestBonus, EquipmentCatalog.Find(toolId));
            }

            int roll2 = Random.Range(0, 3);
            if (roll2 == 0) { _meta.MapCharges++; return ($"¡Encontraste un cofre! +1 carga de Mapa.{firstChestBonus}", null); }
            if (roll2 == 1) { _meta.DrillCharges++; return ($"¡Encontraste un cofre! +1 carga de Perforador.{firstChestBonus}", null); }
            _meta.IncenseCharges++;
            return ($"¡Encontraste un cofre! +1 carga de Incienso.{firstChestBonus}", null);
        }

        // Boveda de cascada (ver DungeonCell.IsWaterfallVaultRoom): a diferencia de un cofre comun
        // (RollTreasureLoot, que la mitad de las veces solo da puntos), ganarle al guardian SIEMPRE
        // entrega una pieza de equipo real de TreasureEquipmentIds -- pedido puntual, "un tesoro muy
        // util". Si ya la tenias, se convierte en puntos igual que RollTreasureLoot (nunca "nada").
        private (string message, EquipmentItem foundItem) RollWaterfallVaultLoot()
        {
            string equipId = TreasureEquipmentIds[Random.Range(0, TreasureEquipmentIds.Length)];
            var equip = EquipmentCatalog.Find(equipId);
            if (_meta.OwnsItem(equipId))
            {
                _meta.BankedPoints += equip.Cost;
                return ($"¡El guardián caído dejó un botín! Ya tenías {equip.Name} -- +{equip.Cost} puntos en su lugar.", null);
            }
            _meta.AddToInventory(equipId);
            return ("", equip);
        }

        // Dialogo de "encontraste un item" (ver caja de dialogo en Gameplay/DialogueHUD): nombre,
        // descripcion, que stats sube y que clases lo pueden usar -- si por algun motivo no hay
        // DialogueManager disponible (p.ej. un harness de test), cae de vuelta a la linea ambiente
        // del DebugHUD con un resumen mas corto.
        private void ShowItemFoundDialogue(EquipmentItem item, string extraNote)
        {
            string text = $"¡Encontraste {item.Name}!\n{item.Description}\n\nBonus: {item.DescribeStats()}\nClases: {item.DescribeAllowedClasses()}";
            if (!string.IsNullOrEmpty(extraNote)) text += $"\n{extraNote.Trim()}";

            if (Dialogue != null) Dialogue.Show("Cofre", text);
            else if (hud != null) hud.SetLastMessage($"¡Encontraste {item.Name}! {item.Description}");
        }

        private bool IsGateOpen(DungeonCell cell) =>
            cell.ControlledGateIndex >= 0 && CurrentFloor.Gates[cell.ControlledGateIndex].IsOpen;

        private LockedDoor FindDoorAt(int x, int y) =>
            CurrentFloor.LockedDoors.Find(d => d.DoorX == x && d.DoorY == y);

        private LockedDoor FindDoorForLever(int x, int y) =>
            CurrentFloor.LockedDoors.Find(d => d.LeverX == x && d.LeverY == y);

        // Mismo disparador que activa el jugador (ver OnPlayerEnterCell): si el FOE pisa la linea
        // de flechas, tambien la dispara, y si pisa picos, le duele al toque -- a diferencia del
        // jugador, el FOE SI puede morir de esto (ver OnTrapArrowHitFoe / FoeController.ApplyTrapDamage).
        private void HandleFoeSteppedOnTrap()
        {
            if (_activeFoe == null || CurrentFloor.TrapDisabled) return;
            var foeCell = CurrentFloor.Cells[_activeFoe.X, _activeFoe.Y];
            if (!foeCell.IsTrapCell) return;

            if (CurrentFloor.TrapKind == TrapKind.ArrowSweep)
            {
                _activeTrapDisparador?.Fire();
            }
            else
            {
                bool died = _activeFoe.ApplyTrapDamage(TrapDamageFraction);
                if (died)
                {
                    Destroy(_activeFoe.gameObject);
                    _activeFoe = null;
                    CurrentFloor.FoePatrolRoute = null;
                    if (hud != null) hud.SetLastMessage("¡El FOE cayo en su propia trampa!");
                }
            }
        }

        // Sistema real de encuentros de Etrian Odyssey: cada celda tiene un valor de peligro
        // (0-5) oculto que se suma a un contador de pasos. Cuando el contador supera un limite
        // tambien oculto (elegido al azar tras cada combate o al entrar a un piso nuevo), aparece
        // un encuentro de inmediato y el contador se reinicia a 0.
        // Sala de trampas: flechas o picos (ver DungeonFloor.TrapKind) le sacan TrapDamageFraction
        // del HP MAXIMO a CADA integrante vivo de la party, de golpe (no es un ataque de combate,
        // no pasa por Defensa/Evasion) -- pero nunca por debajo de 1: una trampa duele en serio,
        // pero nunca termina la run por si sola (a diferencia del FOE, que SI puede matarte en
        // combate si colisiona con vos). Reusa el mismo flash/sacudida de camara roja que un golpe
        // en combate (ver CombatFeedback.OnPartyHit) para que el dano se sienta igual de real
        // caminando por la mazmorra.
        private void ApplyTrapDamage(string message)
        {
            int lastDmg = 0;
            foreach (var p in combat.Party.Where(p => p.IsAlive))
            {
                lastDmg = Mathf.Max(1, Mathf.RoundToInt(p.MaxHP * TrapDamageFraction));
                p.HP = Mathf.Max(1, p.HP - lastDmg);
            }

            if (combat.feedback != null) combat.feedback.OnPartyHit(lastDmg);
            if (hud != null) hud.SetLastMessage($"{message} Toda la party recibe {TrapDamageFraction:P0} de su HP máximo de daño.");
        }

        private void AccumulateDangerAndMaybeEncounter(DungeonCell cell)
        {
            if (combat == null) return;

            int danger = cell.DangerValue;
            if (_incenseStepsRemaining > 0)
            {
                danger /= 2;
                _incenseStepsRemaining--;
            }
            _walkingCounter += danger;

            if (_walkingCounter >= _encounterThreshold)
            {
                _walkingCounter = 0;
                combat.StartEncounter(isBoss: false, floorIndex: _currentFloorIndex, biome: CurrentFloor.Biome);
            }
        }

        // Item "Incienso": mientras dura (IncenseDurationSteps pasos "peligrosos"), el peligro que
        // acumula cada paso se reduce a la mitad -- no elimina los encuentros, solo hace que tarden
        // bastante mas en aparecer, para cruzar rapido un tramo sin pelear tanto.
        public bool TryUseIncense()
        {
            if (_meta.IncenseCharges <= 0) return false;
            _meta.IncenseCharges--;
            MetaSaveService.Save(_meta);
            _incenseStepsRemaining = IncenseDurationSteps;
            if (hud != null) hud.SetLastMessage($"Encendiste el Incienso: el peligro de cada paso baja a la mitad por los proximos {IncenseDurationSteps} pasos.");
            return true;
        }

        private void RollNewEncounterThreshold()
        {
            _walkingCounter = 0;
            _encounterThreshold = Random.Range(settings.encounterThresholdMin, settings.encounterThresholdMax + 1);
        }

        private void HandleCombatFinished(bool victory, bool wasBoss)
        {
            RollNewEncounterThreshold();

            // combat.IsFoeFight NO se pisa hasta el proximo StartEncounter/StartFoeEncounter (ver
            // ese comentario en CombatManager), asi que todavia describe el combate que se acaba de
            // terminar. Vencer al FOE lo saca del piso para siempre (FoePatrolRoute a null: que
            // RefreshActiveFoe no lo vuelva a crear si el jugador sale y vuelve a entrar al piso).
            if (victory && combat.IsFoeFight)
            {
                if (_activeFoe != null) { Destroy(_activeFoe.gameObject); _activeFoe = null; }
                CurrentFloor.FoePatrolRoute = null;
            }

            if (victory && wasBoss)
            {
                _bossDefeatedFloors.Add(_currentFloorIndex);
                _bossesDefeatedThisRun++;
                if (CurrentFloor.Biome == 0)
                {
                    SpawnHubPortal();
                    BuildActiveFloor();
                    if (hud != null) hud.SetLastMessage("¡El Guardián cayó! Un portal al refugio se abrió en la sala.");
                    return;
                }
                if (CurrentFloor.Biome == 3 || CurrentFloor.Biome == 4)
                {
                    if (hud != null) hud.SetLastMessage(CurrentFloor.Biome == 3
                        ? "¡El guardián del patio cayó! El acceso trasero al castillo queda libre."
                        : CurrentFloor.CastleRegion == 4
                            ? "¡El custodio de la cripta cayó! La ruta del sótano está despejada."
                            : "¡El Castellano cayó! La ruta de la torre está despejada.");
                    return;
                }
                EndRun(won: true, "¡Derrotaste al jefe! La run termina con exito.");
                return;
            }

            if (victory)
            {
                _enemiesDefeatedThisRun += combat.Enemies.Count;
                // combat.IsVaultFight (ver CombatManager.StartVaultEncounter): mismo criterio de
                // "no se pisa hasta el proximo Start*Encounter" que IsFoeFight arriba, asi que
                // todavia describe la pelea que se acaba de ganar.
                if (combat.IsVaultFight)
                {
                    var (lootMessage, foundItem) = RollWaterfallVaultLoot();
                    if (foundItem != null) ShowItemFoundDialogue(foundItem, lootMessage);
                    else if (hud != null && !string.IsNullOrEmpty(lootMessage)) hud.SetLastMessage(lootMessage);
                }
                return;
            }

            // Derrota o rendicion: la run termina aca.
            EndRun(won: false, "La party cae derrotada. La run termina aca.");
        }

        // La party escapo con exito del combate: la run sigue igual que antes de que empezara la
        // pelea (no cuenta como victoria ni como derrota, no se banca nada de este encuentro).
        private void HandleCombatFled()
        {
            RollNewEncounterThreshold();
            // Huida exitosa de un FOE: se lo empuja de vuelta a su ruta (ver FoeController.PushBack)
            // en vez de dejarlo exactamente donde colisiono, para no volver a chocar apenas el
            // jugador de un paso mas.
            if (combat.IsFoeFight && _activeFoe != null) _activeFoe.PushBack();
            if (hud != null) hud.SetLastMessage("Escapaste del combate.");
        }

        // La run termina (por derrota, rendicion, o por vencer a un jefe): se banca la recompensa
        // (piso mas profundo alcanzado + enemigos/jefes derrotados) y se abre la pantalla de
        // tienda/mejoras; la proxima mazmorra (semilla nueva) arranca recien al cerrarla (StartNewRun).
        private void EndRun(bool won, string message, bool viaHubPortal = false)
        {
            _lastRunPointsEarned = _meta.AddRunRewards(_deepestFloorReachedThisRun, _enemiesDefeatedThisRun, _bossesDefeatedThisRun);
            MetaSaveService.Save(_meta);
            LastRunWasVictory = won;
            LastRunReturnedToHub = viaHubPortal;
            IsGameOverShopActive = true;
            if (hud != null) hud.SetLastMessage(message);
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
        // jugador tiene enfrente. Solo se gasta si realmente hay algo del otro lado (no Void) --
        // SALVO que esa pared sea exactamente la del disparador de flechas de una sala de trampas
        // (ver DungeonGenerator.TryDestroyTrapDisparador), en cuyo caso lo destruye y desactiva esa
        // trampa para siempre en vez de abrir un paso.
        public bool TryUseDrill(int x, int y, Direction facing)
        {
            if (_meta.DrillCharges <= 0) return false;

            if (_generator.TryDestroyTrapDisparador(CurrentFloor, x, y, facing))
            {
                _meta.DrillCharges--;
                MetaSaveService.Save(_meta);
                if (_activeTrapDisparador != null)
                {
                    Destroy(_activeTrapDisparador.gameObject);
                    _activeTrapDisparador = null;
                }
                BuildActiveFloor();
                if (hud != null) hud.SetLastMessage("¡Destruiste el disparador de flechas! Esa trampa ya no va a disparar.");
                return true;
            }

            if (!_generator.TryDrillWall(CurrentFloor, x, y, facing))
            {
                if (hud != null) hud.SetLastMessage("El Perforador no encontro nada solido detras de esa pared.");
                return false;
            }
            _meta.DrillCharges--;
            MetaSaveService.Save(_meta);
            // Pedido puntual: "perforar una pared no deberia reconstruir el laberinto entero, solo
            // esa pared deberia cambiar" -- TryDrillWall ya puso cell.HasWall(facing) en false,
            // RebuildWallAt borra solo la geometria de esa pared puntual sin tocar el resto del
            // piso (nada de BuildActiveFloor: la fauna ambiental y todo lo demas ni se entera).
            levelBuilder.RebuildWallAt(CurrentFloor, x, y, facing);
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
                        var gate = CurrentFloor.Gates[cell.ControlledGateIndex];
                        if (!string.IsNullOrEmpty(gate.RequiredLoreId) && !_meta.IsLoreUnlocked(gate.RequiredLoreId))
                        {
                            if (hud != null) hud.SetLastMessage("Hay una inscripción en el mecanismo que todavía no podés descifrar. Quizá haya algo de lore sobre esto en otra parte del mapa...");
                            return;
                        }
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
                    OnPlayerEnterCell(tx, ty, advanceFoe: false);
                    if (hud != null) hud.SetLastMessage("Te teletransportaste a traves del atajo.");
                }
            }
            else if (cell.Type == CellType.StairsUp || cell.Type == CellType.StairsDown)
            {
                Direction arrivalFacing = cell.StairTargetFloor >= 0 && CurrentFloor.Biome >= 3
                    && _floors[cell.StairTargetFloor].Biome >= 3
                    ? CastleArrivalFacing(cell, _floors[cell.StairTargetFloor])
                    : Direction.North;
                ChangeFloor(cell.StairTargetFloor, cell.StairTargetX, cell.StairTargetY, arrivalFacing);
            }
            else if (cell.Type == CellType.Boss)
            {
                if (_bossDefeatedFloors.Contains(_currentFloorIndex))
                {
                    if (hud != null) hud.SetLastMessage(CurrentFloor.Biome == 3
                        ? "El guardián del patio ya cayó. La puerta trasera lleva al castillo."
                        : "El jefe de esta ruta ya fue derrotado.");
                }
                else if (combat != null && !combat.IsActive)
                {
                    combat.StartEncounter(isBoss: true, floorIndex: _currentFloorIndex,
                        biome: CurrentFloor.Biome, castleRegion: CurrentFloor.CastleRegion);
                }
            }
            else if (cell.Type == CellType.Lever)
            {
                var door = FindDoorForLever(x, y);
                if (door == null) return;
                if (door.IsUnlocked)
                {
                    if (Dialogue != null) Dialogue.Show("Palanca", "La palanca ya esta activada.");
                    else if (hud != null) hud.SetLastMessage("La palanca ya esta activada.");
                    return;
                }
                int doorIndex = CurrentFloor.LockedDoors.IndexOf(door);
                _generator.UnlockDoor(CurrentFloor, doorIndex);
                // Pedido puntual (mismo criterio que el Perforador, ver TryUseDrill): nada de
                // reconstruir el piso entero por una sola pared. UnlockDoor ya puso
                // cell.HasWall(door.DoorDir) en false -- RebuildWallAt borra solo esa pared puntual.
                // OJO: la celda de la PUERTA (door.DoorX/DoorY/DoorDir), no la de la palanca (x,y) --
                // FindDoorForLever las separa a proposito, son dos celdas distintas. El marcador
                // rojo NO se toca por este cambio (vive en _lockedDoorMarkers, ajeno a _root), asi
                // que UnlockDoorVisual lo encuentra igual y lo pasa a verde a continuacion.
                levelBuilder.RebuildWallAt(CurrentFloor, door.DoorX, door.DoorY, door.DoorDir);
                levelBuilder.UnlockDoorVisual(door.DoorX, door.DoorY);
                if (Dialogue != null) Dialogue.Show("Palanca", "¡Activaste la palanca! El candado se abrio de forma permanente.");
                else if (hud != null) hud.SetLastMessage("¡Activaste la palanca! El candado se abrio de forma permanente.");
            }
            else if (cell.Type == CellType.Treasure)
            {
                if (cell.EventConsumed)
                {
                    if (Dialogue != null) Dialogue.Show("Cofre", "Este cofre ya esta vacio.");
                    else if (hud != null) hud.SetLastMessage("Este cofre ya esta vacio.");
                    return;
                }
                cell.EventConsumed = true;
                var (lootMessage, foundItem) = RollTreasureLoot();
                MetaSaveService.Save(_meta);
                if (foundItem != null) ShowItemFoundDialogue(foundItem, lootMessage);
                else if (Dialogue != null) Dialogue.Show("Cofre", lootMessage);
                else if (hud != null) hud.SetLastMessage(lootMessage);
            }
            // Tumba del Bioma de Cuevas (ver DungeonGenerator.AddTombs): el tesoro garantizado de
            // ESE bioma en vez del cofre comun de arriba -- pedido puntual, 70% tesoro (mismo
            // RollTreasureLoot de siempre) y 30% de que sean goblins emboscados adentro en vez de
            // botin (combate comun del bioma, ver CombatManager.StartEncounter -- ya sale con el
            // bestiario goblin de EnemyFactory.CreateRockCaveEncounter).
            else if (cell.Type == CellType.Tomb)
            {
                if (cell.EventConsumed)
                {
                    if (Dialogue != null) Dialogue.Show("Tumba", "Esta tumba ya fue saqueada.");
                    else if (hud != null) hud.SetLastMessage("Esta tumba ya fue saqueada.");
                    return;
                }
                cell.EventConsumed = true;
                if (Random.value < TombCombatChance)
                {
                    if (combat != null && !IsCombatActive)
                    {
                        if (hud != null) hud.SetLastMessage("¡Goblins saltan de la tumba!");
                        combat.StartEncounter(isBoss: false, floorIndex: _currentFloorIndex, biome: CurrentFloor.Biome);
                    }
                }
                else
                {
                    var (tombLootMessage, tombFoundItem) = RollTreasureLoot();
                    MetaSaveService.Save(_meta);
                    if (tombFoundItem != null) ShowItemFoundDialogue(tombFoundItem, tombLootMessage);
                    else if (Dialogue != null) Dialogue.Show("Tumba", tombLootMessage);
                    else if (hud != null) hud.SetLastMessage(tombLootMessage);
                }
            }
            else if (cell.Type == CellType.LockedDoor)
            {
                if (hud != null) hud.SetLastMessage("Un mecanismo sella el paso. Hace falta encontrar la palanca que lo abre.");
            }
            else if (cell.Type == CellType.BiomeGate)
            {
                EnterBiomeGateFloor();
            }
            else if (cell.Type == CellType.CaveBiomeExit)
            {
                EnterCaveBiomeExit();
            }
            else if (cell.Type == CellType.CastleGate)
            {
                EnterCastleBiome();
            }
            else if (cell.Type == CellType.CastleMainEntrance || cell.Type == CellType.CastleRearEntrance)
            {
                if (cell.StairTargetFloor >= 0)
                {
                    if (CurrentFloor.Biome == 3 && _floors[cell.StairTargetFloor].Biome == 4)
                        _biomeReturnPoint[4] = (_currentFloorIndex, x, y);
                    ChangeFloor(cell.StairTargetFloor, cell.StairTargetX, cell.StairTargetY,
                        CastleArrivalFacing(cell, _floors[cell.StairTargetFloor]));
                }
            }
            else if (cell.Type == CellType.HubPortal)
            {
                EndRun(won: true, "Derrotaste al jefe del bosque y regresaste al refugio por el portal.", viaHubPortal: true);
            }
            else if (cell.Type == CellType.Start && CurrentFloor.Biome != 0 && IsBiomeEntryFloor(_currentFloorIndex, CurrentFloor.Biome))
            {
                ReturnFromSecondaryBiome();
            }
        }

        // true si floorIndex es el PRIMER piso (el punto de entrada/salida) del bioma secundario
        // dado -- generico para cualquier cantidad de biomas ademas del raiz (0).
        private bool IsBiomeEntryFloor(int floorIndex, int biome) =>
            _biomeEntryFloorIndex.TryGetValue(biome, out var idx) && idx == floorIndex;

        // Dos entradas DISTINTAS al Bioma 2 (Puerta Fria, siempre piso 0) y al Bioma de Cuevas
        // (escalera al fondo de la zona aislada, ver DungeonGenerator.PlaceBiomeGate/
        // PlaceCaveBiomeExit) -- ahora que la escalera de la cueva puede tocarle a CUALQUIER piso
        // del bioma raiz (no solo el 0), la vuelta tiene que recordar de que PISO Y celda exactos
        // se entro, no solo la celda: _biomeReturnPoint guarda ambos, por biome id (1 o 2), para
        // que la vuelta te deje exactamente ahi sea cual sea la entrada que uses.
        private readonly Dictionary<int, (int floorIndex, int x, int y)> _biomeReturnPoint = new Dictionary<int, (int, int, int)>();

        // Cruzar la Puerta Fria hacia el PRIMER piso del Bioma 2 (ver GenerateAndEnterDungeon):
        // solo llega hasta aca quien ya la encontro y la perforo (TryUseDrill generico), asi que
        // no hay ningun chequeo extra -- la pared ya era la unica barrera real.
        private void EnterBiomeGateFloor()
        {
            if (!_biomeEntryFloorIndex.TryGetValue(1, out var entryFloorIndex)) return;
            var gatePos = CurrentFloor.BiomeGatePos;
            if (gatePos.HasValue) _biomeReturnPoint[1] = (_currentFloorIndex, gatePos.Value.x, gatePos.Value.y);
            var target = _floors[entryFloorIndex].StartPos;
            ChangeFloor(entryFloorIndex, target.x, target.y);
            if (hud != null) hud.SetLastMessage("Cruzás la Puerta Fría. El aire cambia por completo.");
        }

        // Bajar por la escalera al fondo de la zona aislada (ver
        // DungeonGenerator.PlaceCaveBiomeExit): mismo espiritu que la Puerta Fria, pero sin secreto
        // y hacia el Bioma de Cuevas (Biome id 2, no el Bioma 2/espacial) -- puede tocarle a
        // cualquier piso del bioma raiz esta run, CurrentFloor ya es ese piso cuando se interactua.
        private void EnterCaveBiomeExit()
        {
            if (!_biomeEntryFloorIndex.TryGetValue(2, out var entryFloorIndex)) return;
            var exitPos = CurrentFloor.CaveBiomeExitPos;
            if (exitPos.HasValue) _biomeReturnPoint[2] = (_currentFloorIndex, exitPos.Value.x, exitPos.Value.y);
            var target = _floors[entryFloorIndex].StartPos;
            ChangeFloor(entryFloorIndex, target.x, target.y);
            if (hud != null) hud.SetLastMessage("Bajás por la escalera de la cueva. El aire cambia por completo.");
        }

        private void SpawnHubPortal()
        {
            var floor = CurrentFloor;
            var candidates = floor.HasBossRoom
                ? floor.BossRoomCells.Where(c => floor.Cells[c.Item1, c.Item2].Type == CellType.Normal).ToList()
                : new List<(int, int)>();
            if (candidates.Count == 0)
                candidates.Add(floor.BossPos);
            var portal = candidates
                .OrderBy(c => Mathf.Abs(c.Item1 - floor.BossPos.x) + Mathf.Abs(c.Item2 - floor.BossPos.y))
                .First();
            floor.Cells[portal.Item1, portal.Item2].Type = CellType.HubPortal;
            floor.HubPortalPos = portal;
        }

        private void EnterCastleBiome()
        {
            if (!_biomeEntryFloorIndex.TryGetValue(3, out var entryFloorIndex)) return;
            var gatePos = CurrentFloor.CastleGatePos;
            if (gatePos.HasValue) _biomeReturnPoint[3] = (_currentFloorIndex, gatePos.Value.x, gatePos.Value.y);
            var target = _floors[entryFloorIndex].StartPos;
            ChangeFloor(entryFloorIndex, target.x, target.y);
            if (hud != null) hud.SetLastMessage("Entras al patio frontal. El portón principal se alza al norte; las escaleras laterales rodean el castillo.");
        }

        // Vuelta al bioma raiz desde CUALQUIER bioma secundario: se para sobre Start (que ahi no
        // tiene otro uso, ya que a ese piso nunca se entra por escalera) y aparece de vuelta
        // EXACTAMENTE sobre la entrada que uso para llegar -- piso Y celda (_biomeReturnPoint,
        // seteado en EnterBiomeGateFloor o EnterCaveBiomeExit segun cual haya sido), no siempre el
        // piso 0 (la escalera de la cueva puede estar en cualquier piso del bioma raiz). Es seguro:
        // cruzar de vuelta exige interactuar (TryInteract), aparecer parado ahi no dispara nada
        // solo por pisarlo (ver OnPlayerEnterCell).
        private void ReturnFromSecondaryBiome()
        {
            int biome = CurrentFloor.Biome;
            if (!_biomeReturnPoint.TryGetValue(biome, out var back)) return;
            Direction facing = biome == 4
                ? CastleArrivalFacing(_floors[back.floorIndex].Cells[back.x, back.y], _floors[back.floorIndex])
                : Direction.North;
            ChangeFloor(back.floorIndex, back.x, back.y, facing);
            if (hud != null) hud.SetLastMessage(biome switch
            {
                1 => "Volvés a través de la Puerta Fría.",
                2 => "Volvés a través de la escalera de la cueva.",
                3 => "Volvés al bosque por el arco del patio.",
                _ => "Volvés al patio del castillo."
            });
        }

        // Etiqueta de piso para UI (minimapa/menu de pausa): cada bioma secundario tiene su PROPIA
        // numeracion de piso (1, 2, ...) aunque internamente floorIndex siga la secuencia global de
        // _floors (necesaria para que StairTargetFloor funcione) -- sin esto, se leia como
        // continuacion de la numeracion del bioma raiz en vez de la mazmorra propia que es.
        public string FloorLabel(int floorIndex)
        {
            if (floorIndex < 0 || floorIndex >= _floors.Count) return $"Piso {floorIndex}";
            int biome = _floors[floorIndex].Biome;
            if (biome == 0 || !_biomeEntryFloorIndex.TryGetValue(biome, out var entryIdx)) return $"Piso {floorIndex}";
            if (biome == 3 || biome == 4)
            {
                string castleRegion = biome == 3
                    ? _floors[floorIndex].CastleRegion switch
                    {
                        0 => "Patio frontal",
                        1 => "Patio lateral",
                        2 => "Patio trasero - jefe del patio",
                        _ => "Patio"
                    }
                    : _floors[floorIndex].CastleRegion switch
                    {
                        0 => "Salón principal",
                        1 => "Torre I",
                        2 => "Torre II - jefe",
                        3 => "Sótano I",
                        4 => "Sótano II - jefe",
                        _ => "Interior"
                    };
                return biome == 3 ? $"Patio - {castleRegion}" : $"Castillo - {castleRegion}";
            }
            string biomeName = biome switch { 1 => "Bioma 2", 2 => "Bioma de Cuevas", _ => $"Bioma {biome}" };
            return $"{biomeName} - Piso {floorIndex - entryIdx + 1}";
        }

        private Direction CastleArrivalFacing(DungeonCell sourceCell, DungeonFloor targetFloor)
        {
            if (targetFloor.Biome == 3)
            {
                if (sourceCell.Type == CellType.CastleMainEntrance) return Direction.South;
                if (sourceCell.Type == CellType.CastleRearEntrance) return Direction.North;
                return sourceCell.Type == CellType.StairsDown ? Direction.South : Direction.North;
            }
            if (targetFloor.CastleRegion == 0)
            {
                if (sourceCell.Type == CellType.CastleMainEntrance) return Direction.South;
                if (sourceCell.Type == CellType.CastleRearEntrance) return Direction.North;
                return CurrentFloor.CastleRegion >= 3 ? Direction.North : Direction.South;
            }
            return sourceCell.Type == CellType.StairsDown ? Direction.South : Direction.North;
        }

        public void ChangeFloor(int floorIndex, int spawnX, int spawnY, Direction facing = Direction.North)
        {
            if (floorIndex < 0 || floorIndex >= _floors.Count) return;
            _isPlayerFalling = false;
            _currentFloorIndex = floorIndex;
            if (floorIndex > _deepestFloorReachedThisRun) _deepestFloorReachedThisRun = floorIndex;
            BuildActiveFloor();
            RollNewEncounterThreshold();
            player.Warp(spawnX, spawnY, facing);
            OnPlayerEnterCell(spawnX, spawnY, advanceFoe: false);
            if (hud != null) hud.SetLastMessage($"Cambiaste al piso {floorIndex}.");
            if (hud != null && CurrentFloor.Biome == 0 && CurrentFloor.Index == 2)
                hud.SetLastMessage("CASTILLO: el arco magenta está marcado en el minimapa. Explora este piso y pulsa Espacio al llegar.");
        }

        public (bool ok, List<string> issues) ValidateCurrentDungeon()
        {
            return _generator.ValidateDungeon(_floors);
        }

        // DEBUG/testeo: lista de biomas existentes esta run (0 = raiz, 1/2 = secundarios, 3 = patio,
        // 4 = interior del castillo; todos existen desde el inicio de la run).
        public IEnumerable<int> DebugAvailableBiomes()
        {
            yield return 0;
            foreach (var biome in _biomeEntryFloorIndex.Keys) yield return biome;
        }

        // DEBUG/testeo: salta directo al PRIMER piso de un bioma (0 = raiz, 1 = Bioma 2 espacial,
        // 2 = Bioma de Cuevas, 3 = Patio, 4 = Castillo interior) sin tener que jugar hasta encontrar su acceso sin
        // la cueva. Reusa el mismo ChangeFloor que esos caminos reales, asi que geometria/threshold/
        // FOE quedan tan consistentes como entrando de verdad -- solo que instantaneo.
        public bool DebugWarpToBiome(int biome)
        {
            if (_floors == null) return false;
            int entryFloorIndex = 0;
            if (biome != 0 && !_biomeEntryFloorIndex.TryGetValue(biome, out entryFloorIndex)) return false;
            return DebugWarpToFloor(entryFloorIndex);
        }

        // DEBUG/testeo: mismo salto de arriba pero a un piso GLOBAL puntual (indice de _floors, no
        // el numero "Piso N" que muestra FloorLabel dentro de un bioma) -- para probar un piso
        // profundo de cualquier bioma sin caminar toda la mazmorra hasta ahi. Aparece sobre el Start
        // de ese piso.
        public bool DebugWarpToFloor(int floorIndex)
        {
            if (_floors == null || floorIndex < 0 || floorIndex >= _floors.Count) return false;
            var target = _floors[floorIndex].StartPos;
            ChangeFloor(floorIndex, target.x, target.y);
            if (hud != null) hud.SetLastMessage($"[DEBUG] Warp a {FloorLabel(floorIndex)} (piso global {floorIndex}, bioma {_floors[floorIndex].Biome}).");
            return true;
        }
    }
}
