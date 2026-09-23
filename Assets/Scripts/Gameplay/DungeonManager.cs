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

        // Indice en _floors del PRIMER piso del Bioma 2 (Cueva Intergaláctica, ver
        // GenerateAndEnterDungeon -- el bioma tiene varios pisos propios, este es solo el punto de
        // entrada/salida), -1 si todavia no se genero ninguno para esta run.
        private int _biomeGateFloorIndex = -1;

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

        // FOE activo del piso actual, para que el mapa (MinimapUI / PauseMenuHUD) lo pueda dibujar.
        public FoeController ActiveFoe => _activeFoe;

        [Header("Debug")]
        [Tooltip("DEBUG/testeo: si esta prendido, el FOE se ve en el mapa aunque este parado en una celda que todavia no descubriste. Apagalo para el comportamiento real (niebla de guerra tambien lo tapa a el).")]
        public bool debugFoeAlwaysVisibleOnMap = true;

        public MetaProgress Meta => _meta;
        public bool IsGameOverShopActive { get; private set; }
        public bool LastRunWasVictory { get; private set; }
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

        private void GenerateAndEnterDungeon(int seed)
        {
            _floors = _generator.GenerateDungeon(
                settings.floorCount, settings.size, settings.size, seed, out var log,
                settings.stairPairsPerFloor,
                settings.bossFloorStart,
                settings.bossFloorInterval,
                settings.voidFraction,
                settings.dangerValueMin,
                settings.dangerValueMax,
                BuildLorePool());

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
                biome: 1);
            foreach (var line in biomeLog) Debug.Log(line);

            _floors.AddRange(biomeFloors);
            _biomeGateFloorIndex = biomeFloors[0].Index;

            _bossDefeatedFloors.Clear();
            _deepestFloorReachedThisRun = 0;
            _enemiesDefeatedThisRun = 0;
            _bossesDefeatedThisRun = 0;

            _currentFloorIndex = 0;
            BuildActiveFloor();
            RollNewEncounterThreshold();

            var start = CurrentFloor.StartPos;
            player.Warp(start.x, start.y, Direction.North);
            OnPlayerEnterCell(start.x, start.y, advanceFoe: false);
            IsReady = true;
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
            levelBuilder.Build(CurrentFloor, settings.cellSize, settings.wallHeight, settings.wallThickness);
            RefreshActiveFoe();
            RefreshActiveTrapDisparador();
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
            _activeFoe.Initialize(CurrentFloor, CanMove, CellToWorld, settings.cellSize, EnemyFactory.CreateFoe(_currentFloorIndex).MaxHP);
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
            var cell = CurrentFloor.Cells[x, y];
            cell.Discovered = true;

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

            // Casilla obligatoria del camino critico (ver DungeonGenerator.PlaceMandatoryPathHazard):
            // duele una sola vez (EventConsumed), no en cada backtrack por el mismo pasillo.
            if (cell.IsMandatoryHazard && !cell.EventConsumed && !IsCombatActive)
            {
                cell.EventConsumed = true;
                ApplyTrapDamage("¡Una trampa te alcanza en pleno camino!");
            }

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
                    ChangeFloor(_currentFloorIndex + 1, below.StartPos.x, below.StartPos.y);
                    if (hud != null) hud.SetLastMessage("¡El piso cede bajo tus pies! Caes al piso de abajo.");
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
                        ? "El jefe de este piso ya fue derrotado. La escalera para avanzar esta en esta sala."
                        : "Sala del jefe. Presiona Espacio para enfrentarlo.";
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
                case CellType.Start:
                    // Solo el Start del PRIMER piso del Bioma 2 es la vuelta a la Puerta Fria -- el
                    // resto de sus pisos (ahora el bioma tiene varios, ver GenerateAndEnterDungeon)
                    // tambien tienen su propio Start (todo piso lo tiene, es de donde arrancarias si
                    // entraras por ahi), pero ese es solo un marcador normal, no una salida.
                    if (CurrentFloor.Biome != 0 && _currentFloorIndex == _biomeGateFloorIndex)
                        message = "La Puerta Fría, del otro lado. Presiona Espacio para volver.";
                    break;
            }
            if (message != null && hud != null) hud.SetLastMessage(message);
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
                EndRun(won: true, "¡Derrotaste al jefe! La run termina con exito.");
                return;
            }

            if (victory)
            {
                _enemiesDefeatedThisRun += combat.Enemies.Count;
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
        private void EndRun(bool won, string message)
        {
            _lastRunPointsEarned = _meta.AddRunRewards(_deepestFloorReachedThisRun, _enemiesDefeatedThisRun, _bossesDefeatedThisRun);
            MetaSaveService.Save(_meta);
            LastRunWasVictory = won;
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
                    combat.StartEncounter(isBoss: true, floorIndex: _currentFloorIndex, biome: CurrentFloor.Biome);
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
                // Reconstruir el piso es necesario para que la pared recien abierta deje de
                // renderizarse como solida; el marcador rojo de la puerta se recrea en ese rebuild
                // (el CellType sigue siendo LockedDoor), asi que el cambio a verde va DESPUES.
                BuildActiveFloor();
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
            else if (cell.Type == CellType.LockedDoor)
            {
                if (hud != null) hud.SetLastMessage("Un mecanismo sella el paso. Hace falta encontrar la palanca que lo abre.");
            }
            else if (cell.Type == CellType.BiomeGate)
            {
                EnterBiomeGateFloor();
            }
            else if (cell.Type == CellType.Start && CurrentFloor.Biome != 0 && _currentFloorIndex == _biomeGateFloorIndex)
            {
                ReturnFromBiomeGateFloor();
            }
        }

        // Cruzar la Puerta Fria hacia el PRIMER piso del Bioma 2 (ver GenerateAndEnterDungeon):
        // solo llega hasta aca quien ya la encontro y la perforo (TryUseDrill generico), asi que
        // no hay ningun chequeo extra -- la pared ya era la unica barrera real.
        private void EnterBiomeGateFloor()
        {
            if (_biomeGateFloorIndex < 0) return;
            var target = _floors[_biomeGateFloorIndex].StartPos;
            ChangeFloor(_biomeGateFloorIndex, target.x, target.y);
            if (hud != null) hud.SetLastMessage("Cruzás la Puerta Fría. El aire cambia por completo.");
        }

        // Vuelta al piso 0 desde el Bioma 2: se para sobre Start (que ahi no tiene otro uso, ya
        // que a este piso nunca se entra por escalera) y aparece de vuelta EXACTAMENTE sobre la
        // Puerta Fria (DungeonFloor.BiomeGatePos, ver PlaceBiomeGate) -- no un paso mas atras en
        // BiomeGateApproachPos, que dejaba al jugador del otro lado de la puerta en vez de encima
        // de ella. Es seguro: cruzar de vuelta exige interactuar (TryInteract/CellType.BiomeGate),
        // aparecer parado ahi no dispara nada solo por pisarlo (ver OnPlayerEnterCell).
        private void ReturnFromBiomeGateFloor()
        {
            var back = _floors[0].BiomeGatePos;
            if (!back.HasValue) return;
            ChangeFloor(0, back.Value.x, back.Value.y);
            if (hud != null) hud.SetLastMessage("Volvés a través de la Puerta Fría.");
        }

        // Etiqueta de piso para UI (minimapa/menu de pausa): el Bioma 2 tiene su PROPIA numeracion
        // de piso (1, 2, ...) aunque internamente floorIndex siga la secuencia global de _floors
        // (necesaria para que StairTargetFloor funcione) -- sin esto, el Bioma 2 se leia como "Piso
        // 3" (continuando la numeracion del Bioma 1) en vez de la mazmorra propia que es.
        public string FloorLabel(int floorIndex)
        {
            if (floorIndex < 0 || floorIndex >= _floors.Count) return $"Piso {floorIndex}";
            if (_floors[floorIndex].Biome == 0 || _biomeGateFloorIndex < 0) return $"Piso {floorIndex}";
            return $"Bioma 2 - Piso {floorIndex - _biomeGateFloorIndex + 1}";
        }

        public void ChangeFloor(int floorIndex, int spawnX, int spawnY)
        {
            if (floorIndex < 0 || floorIndex >= _floors.Count) return;
            _currentFloorIndex = floorIndex;
            if (floorIndex > _deepestFloorReachedThisRun) _deepestFloorReachedThisRun = floorIndex;
            BuildActiveFloor();
            RollNewEncounterThreshold();
            player.Warp(spawnX, spawnY, Direction.North);
            OnPlayerEnterCell(spawnX, spawnY, advanceFoe: false);
            if (hud != null) hud.SetLastMessage($"Cambiaste al piso {floorIndex}.");
        }

        public (bool ok, List<string> issues) ValidateCurrentDungeon()
        {
            return _generator.ValidateDungeon(_floors);
        }
    }
}
