using System.Collections.Generic;
using UnityEngine;
using DungeonGen;
using Lore;

namespace Gameplay
{
    public class DungeonLevelBuilder : MonoBehaviour
    {
        [Header("Materiales opcionales (si se dejan vacios se usan colores solidos)")]
        public Material floorMaterial;
        public Material ceilingMaterial;
        public Material wallMaterial;
        [Tooltip("Pared del bosque de verdad (Custom/ForestWall): corteza + manchas de hoja con vaiven de luz sutil, sin desplazar vertices (ver comentario junto a BuildWall). Si se deja vacio, el bosque usa wallMaterial como cualquier otro bioma.")]
        public Material forestWallMaterial;
        [Tooltip("Capas de parallax con arte real pegadas enfrente de la pared del bosque (Custom/ForestLayerCutout, ver ForestParallaxWallFactory) -- capa lejana y capa cercana. Opcionales: si se dejan vacias, la pared del bosque se ve solo con forestWallMaterial, sin capas de mas.")]
        public Material forestLayerFarMaterial;
        public Material forestLayerNearMaterial;
        [Tooltip("Color plano oscuro para la BASE de la pared cuando las dos capas de arte de arriba estan asignadas (ver hasForestArt en BuildWall) -- un material COMPARTIDO (no instanciado por pared como el fallback de color de ApplyMaterial) para no perder el static batching en la geometria mas numerosa del piso.")]
        public Material forestBackdropMaterial;
        public Material startMarkerMaterial;
        public Material endMarkerMaterial;
        public Material questMarkerMaterial;
        public Material switchMarkerMaterial;
        public Material landingMarkerMaterial;
        public Material stairsUpMaterial;
        public Material stairsDownMaterial;
        public Material bossMarkerMaterial;
        public Material loreMarkerMaterial;
        public Material bossRoomFloorMaterial;
        public Material bossRoomCeilingMaterial;
        public Material voidBlockMaterial;
        public Material lockedDoorMarkerMaterial;
        public Material leverMarkerMaterial;
        public Material treasureMarkerMaterial;
        public Material trapMarkerMaterial;

        [Header("Suelo de bosque: tilemap de pasto/tierra/hojas/camino (ver GroundTileFactory)")]
        [Tooltip("Reemplaza el piso chato de las celdas normales (no zona aislada, no Bioma 2) por el tilemap variado de pasto/tierra/hojas/camino, estilo PS1. Desactivar vuelve al piso liso de un solo color (floorMaterial).")]
        public bool useForestGroundTiles = true;
        [Tooltip("Material opcional para el tilemap de pasto (shader Custom/PS1Ground); si se deja vacio se genera uno en runtime.")]
        public Material groundTileMaterial;
        [Tooltip("Material opcional para el parche de piso al rojo vivo de las celdas peligrosas de Brasas (ver BuildBrasasDangerFloor); si se deja vacio usa un color solido de respaldo.")]
        public Material brasasDangerFloorMaterial;

        [Header("Carteles de madera con los controles, clavados donde arranca la run (ver BuildSpawnSignposts)")]
        public Material signpostWoodMaterial;
        public Material signpostBoardMaterial;
        [Tooltip("Texto del cartel de movimiento (\\n = salto de linea).")]
        public string signpostMoveText = "WASD\nMOVERSE";
        [Tooltip("Texto del cartel del mapa (\\n = salto de linea).")]
        public string signpostMapText = "M\nMAPA";
        [Tooltip("Tamano de la letra en el mundo (TextMesh.characterSize) -- mas alto = letras mas grandes.")]
        public float signpostCharacterSize = 0.13f;
        [Tooltip("Resolucion de la fuente (TextMesh.fontSize) -- subirlo si el texto se ve pixelado de cerca.")]
        public int signpostFontSize = 48;

        // Restos alrededor del marcador de lore (ver Lore.SceneDressing / BuildLoreSceneDressing).
        // Un solo campo compartido por las 4 variantes -- igual que trapMarkerMaterial cubre tanto
        // la linea de flechas como los picos, el color/forma de cada prop ya las distingue entre si
        // aun si un artista termina asignando el mismo material a todas.
        public Material loreDressingMaterial;

        // Tinte distinto (piso/pared/techo) para la zona aislada de cada piso: en BotW un mundo con
        // regiones visualmente distinguibles hace que el jugador arme su propio mapa mental
        // caminando, en vez de depender todo el tiempo del automapa -- esta zona ya tenia su color
        // propio en el minimapa (MinimapUI.FloorColor), esto lo lleva tambien a la vista en 3D.
        public Material isoFloorMaterial;
        public Material isoCeilingMaterial;
        public Material isoWallMaterial;

        // Bioma 2 (Cueva Intergalactica, DungeonFloor.Biome == 1 especificamente -- el Bioma de
        // Cuevas nuevo, Biome == 2, reusa isoFloorMaterial/isoCeilingMaterial/isoWallMaterial de
        // arriba en vez de un cuarto set de campos, ver BuildFloorTile/BuildCeilingTile/
        // BuildBossRoomSlab): techo de cielo estrellado (Custom/StarrySky) y piso con el reflejo
        // tenue de esas mismas estrellas (Custom/StarlitFloor, ver el shader para el porque
        // comparten el mismo campo de estrellas). Pisa por encima del tinte de zona aislada: un
        // piso de Bioma 2 siempre se ve como Bioma 2, sea o no ademas zona aislada.
        public Material biome2CeilingMaterial;
        public Material biome2FloorMaterial;

        // Aura sutil de piso alrededor de las escaleras (Custom/StairsAura, ver
        // BuildStairsFloorAura) -- se nota desde ~3 celdas de distancia por un pasillo recto.
        public Material stairsAuraMaterial;

        private GameObject _root;
        private static Mesh _waterSurfaceMesh;
        private static Material _forestCanopyMaterial;
        private static Material _pitRockMaterial;
        private static readonly Color[] CaveRockPalette =
        {
            new Color(0.22f, 0.23f, 0.23f),
            new Color(0.29f, 0.30f, 0.30f),
            new Color(0.25f, 0.30f, 0.26f),
            new Color(0.34f, 0.30f, 0.25f),
        };
        private static Material[] _caveRockPaletteMaterials;
        private static Mesh _torchFlameMesh;
        private static Material _torchFlameMaterial;
        private static Material _torchCoreMaterial;
        private static Material _castleStoneMaterial;
        private static Material _castleWallMaterial;
        private static Material _castleCeilingMaterial;
        private static Material _castleTrimMaterial;
        private static Material _castleRugMaterial;
        private static Material _castlePortalMaterial;
        private static Material _castleLandmarkTemplate;
        private static readonly Dictionary<Color, Material> _castlePaletteMaterials = new Dictionary<Color, Material>();
        private static readonly Dictionary<Color, Material> _castleLandmarkMaterials = new Dictionary<Color, Material>();
        private static Material _cobwebMaterial;
        private const int MaxCaveTorchesPerFloor = 5;
        private const int MaxCeilingCandelabraPerFloor = 4;
        private const int MaxCaveCobwebsPerFloor = 14;
        private const int MaxCaveStalactitesPerFloor = 10;
        private int _caveTorchesBuilt;
        private int _ceilingCandelabraBuilt;
        private int _caveCobwebsBuilt;
        private int _caveStalactitesBuilt;
        private readonly List<Vector3> _wildlifeHabitatPoints = new List<Vector3>();
        // Centro de cada laguna de agua real (Void que NO es lava/ruina, ver BuildVoidBlock) --
        // pedido puntual: "que vuelen por el agua, que se dirijan al agua", ademas de los arbustos
        // de arriba. Se suma a _wildlifeHabitatPoints en BuildAmbientCritters como destino de
        // vuelo mas, no reemplaza el paseo por arbustos.
        private readonly List<Vector3> _wildlifeWaterPoints = new List<Vector3>();
        // Centro de la primer celda de la mini-cueva del piso 0 con umbral valido (mismo hallazgo
        // que BuildMiniCaveEntranceSignpost, ver Build) -- destino de migracion nocturna para las
        // mariposas/polillas del bosque real (ver BuildAmbientCritters).
        private Vector3? _caveEntrancePoint;
        // Antorchas con parpadeo (TorchFlameFlicker, ver BuildTorch flicker:true) pedidas desde el
        // loop principal de Build() -- castillo, entrada de cueva del piso 0, antorcha de cascada.
        // MISMO bug/fix que _wildlifeHabitatPoints de arriba (ver el comentario grande junto a
        // StaticBatchingUtility.Combine en Build): si BuildTorch se llamara ahi mismo durante el
        // loop, el Combine posterior fusionaria su llama en la malla estatica y el parpadeo por
        // Update() dejaria de verse. Se guarda solo (centro, direccion de pared) -- cellSize y
        // wallHeight no cambian durante todo el Build(), asi que Build() los reusa tal cual al
        // vaciar esta lista despues del Combine.
        private readonly List<(Vector3 center, Direction dir)> _pendingFlickerTorches = new List<(Vector3, Direction)>();
        private readonly Dictionary<int, (GameObject switchGo, GameObject landingGo)> _shortcutMarkers = new Dictionary<int, (GameObject, GameObject)>();
        private readonly Dictionary<(int x, int y), GameObject> _lockedDoorMarkers = new Dictionary<(int, int), GameObject>();
        // Ancla (GameObject vacio) de cada pared, para poder borrar UNA pared puntual en runtime
        // (ver RebuildWallAt) sin reconstruir el piso entero -- pedido puntual: "perforar una pared
        // no deberia reconstruir el laberinto desde cero". Registrada bajo las DOS claves posibles
        // (celda,direccion) de esa pared compartida (ver el loop principal en Build(), BuildWall se
        // llama una sola vez por pared desde el lado "dueño" North/East, pero el jugador puede
        // perforarla mirando desde cualquiera de los dos lados) -- normalizar la clave al leer en
        // vez de escribir las dos duplicaria esa logica isPrimaryDir/outOfBounds en un segundo
        // lugar, con riesgo de desincronizarse en silencio.
        private readonly Dictionary<(int x, int y, Direction dir), GameObject> _wallAnchors = new Dictionary<(int, int, Direction), GameObject>();

        // Plantilla del burst de salpicadura (ver BuildPuzzleTile): UNA sola, compartida por todas
        // las celdas "reales" de la sala -- Unity soporta que varios sistemas de particulas padre
        // apunten al mismo sub-emitter. Se reconstruye (queda null) cada vez que Clear() destruye
        // _root, porque esta parentada ahi adentro.
        private ParticleSystem _puzzleSplashTemplate;

        public void Clear()
        {
            if (_root != null) Destroy(_root);
            _shortcutMarkers.Clear();
            _lockedDoorMarkers.Clear();
            _collapseMarkers.Clear();
            _wallAnchors.Clear();
            _puzzleSplashTemplate = null;
            // Pasto instanciado del piso anterior (ver FoliageManager) -- vive fuera de _root
            // porque se dibuja con Graphics.DrawMeshInstanced, no como hijos del GameObject del
            // piso, asi que Destroy(_root) no lo toca por si solo.
            FoliageManager.Instance.ClearAll();
        }

        // Una celda logica = una distancia cellSize = un paso del jugador, en angulos de 90°, igual
        // que el mapa real de Etrian Odyssey. Entre caminos que no estan conectados puede haber
        // celdas "Void": esas se construyen como un BLOQUE SOLIDO real (un volumen de piso a techo),
        // no como una simple pared delgada - asi separan un camino de otro con un bloque de verdad.
        public void Build(DungeonFloor floor, float cellSize, float wallHeight, float wallThickness)
        {
            Clear();
            _wildlifeHabitatPoints.Clear();
            _wildlifeWaterPoints.Clear();
            _pendingFlickerTorches.Clear();
            _caveTorchesBuilt = 0;
            _ceilingCandelabraBuilt = 0;
            _caveCobwebsBuilt = 0;
            _caveStalactitesBuilt = 0;
            _root = new GameObject($"Floor_{floor.Index}");
            _root.transform.SetParent(transform, false);

            // Umbral de la zona aislada del piso 0 (ver mas abajo, isIso && floor.Biome == 0):
            // BuildMiniCaveEntranceSignpost se llama una vez por CELDA candidata a umbral, pero el
            // cartel tiene que existir una sola vez por piso -- este flag corta la busqueda apenas
            // el primer umbral valido lo construye. _caveEntrancePoint se guarda en el mismo momento
            // (pedido puntual: "de noche las mariposas... deben dirigirse a la cueva donde en la
            // entrada hay antorchas", ver BuildAmbientCritters/ForestCritterAmbient).
            bool miniCaveSignBuilt = false;
            _caveEntrancePoint = null;

            if (floor.HasBossRoom)
                BuildBossRoomSlab(floor, cellSize, wallHeight);

            for (int x = 0; x < floor.Width; x++)
            {
                for (int y = 0; y < floor.Height; y++)
                {
                    var cell = floor.Cells[x, y];
                    Vector3 center = CellCenter(x, y, cellSize);

                    if (cell.Type == CellType.Void)
                    {
                        BuildVoidBlock(floor, x, y, center, cellSize, wallHeight);
                        continue;
                    }

                    bool isIso = cell.IsIsolatedZone;
                    bool isRockCaveBiome = floor.Biome == 2;
                    bool isCastleBiome = floor.Biome == 3 || floor.Biome == 4;
                    bool isCastleExterior = floor.Biome == 3;
                    // El Bioma de Cuevas (nuevo) reusa TAL CUAL el look rocoso ya construido para
                    // la zona aislada (piso/techo/pared, telaranas, estalactitas) en vez de armar
                    // un tercer set de materiales desde cero -- toda celda de ese bioma se trata
                    // como si fuera zona aislada a efectos puramente visuales (esto NO toca
                    // cell.IsIsolatedZone, que sigue siendo el flag real de generacion).
                    bool useCaveLook = !isCastleBiome && (isIso || isRockCaveBiome);
                    bool isBiome2 = floor.Biome == 1; // de aca en mas, especificamente "espacial" (antes "!= 0" tambien agarraba el bioma nuevo)
                    // Bosque "de verdad": SOLO el bioma raiz normal, nunca su propia zona aislada
                    // (esa sigue siendo "entrada de cueva" con useCaveLook) ni ningun otro bioma --
                    // usado para las ramas de canopia colgando del techo (ver BuildCanopyBranch mas
                    // abajo) y para elegir forestWallMaterial en vez de wallMaterial (ver BuildWall).
                    // El techo sigue con material solido de siempre, sin shader.
                    bool isForestBiome = floor.Biome == 0 && !useCaveLook;
                    // Goteras falso (ver DungeonManager.OnPlayerEnterCell): ahi de verdad no hay
                    // piso -- pisarlo te hace caer al piso de abajo, asi que tiene que VERSE como un
                    // hueco real, no como piso normal con una trampa escondida debajo.
                    bool isVisibleVoidHole = cell.IsPuzzleTile && !cell.IsPuzzleTileSafe && floor.LoreCorridorKind == PuzzleKind.Goteras;
                    if (!cell.IsBossRoom)
                    {
                        // Las celdas de escalera fuerzan Grass (sin props): una roca de
                        // GrassRockDirt saliendo justo debajo del icono pseudo-3D de la escalera
                        // (ver BuildStairsIcon) se veia amontonado con el.
                        bool forceCleanFloor = cell.Type == CellType.StairsUp || cell.Type == CellType.StairsDown;
                        GroundTileKind? groundKind = null;
                        if (!isVisibleVoidHole)
                            groundKind = BuildFloorTile(center, cellSize, useCaveLook, isBiome2, isCastleBiome, floor.Index, x, y, forceCleanFloor, floor.CastleRegion, isCastleExterior);
                        if (isForestBiome && (groundKind == GroundTileKind.Bush || groundKind == GroundTileKind.FlowerBush))
                            _wildlifeHabitatPoints.Add(center + Vector3.up * 0.65f);
                        // El bosque recibe una sola superficie de copa para todo el mapa; crear
                        // un cubo por celda dejaba una cuadrícula visible contra el cielo.
                        if (!isForestBiome && !isCastleExterior)
                            BuildCeilingTile(center, cellSize, wallHeight, useCaveLook, isBiome2, isCastleBiome);

                        // Telaranas y estalactitas (pedido puntual, ver foto de referencia): en toda
                        // celda con look de cueva (zona aislada del bosque O el Bioma de Cuevas
                        // entero) -- 30% de chance de una chica, 8% de una GRANDE ademas (en la foto
                        // de referencia eran mucho mas grandes que lo que habia), 25% de estalactita
                        // colgando del techo, todo independiente entre si.
                        if (useCaveLook && !isBiome2)
                        {
                            // Telaranas construyen muchos segmentos de malla; caps y probabilidades
                            // menores conservan la lectura cavernosa sin cientos de objetos por piso.
                            if (_caveCobwebsBuilt < MaxCaveCobwebsPerFloor && Random.value < 0.12f)
                            {
                                BuildCobweb(cell, center, cellSize, wallHeight, big: false);
                                _caveCobwebsBuilt++;
                            }
                            if (_caveCobwebsBuilt < MaxCaveCobwebsPerFloor && Random.value < 0.025f)
                            {
                                BuildCobweb(cell, center, cellSize, wallHeight, big: true);
                                _caveCobwebsBuilt++;
                            }
                            if (_caveStalactitesBuilt < MaxCaveStalactitesPerFloor && Random.value < 0.1f)
                            {
                                BuildStalactite(center, wallHeight);
                                _caveStalactitesBuilt++;
                            }
                        }

                        // Candelabros colgantes de techo, escasos para no llenar la escena de
                        // luces y objetos. No van sobre tumbas ni sus celdas decorativas.
                        if (isRockCaveBiome && cell.Type != CellType.Tomb && !cell.IsTombDecor
                            && _ceilingCandelabraBuilt < MaxCeilingCandelabraPerFloor && Random.value < 0.08f)
                        {
                            BuildCeilingCandelabra(center, cellSize, wallHeight);
                            _ceilingCandelabraBuilt++;
                        }

                        // Ramas colgando del techo (pedido puntual: "agrega algo que simule
                        // ramificacion de arboles que parezca un bosque frondoso" -- un techo de
                        // color solido es una superficie CHATA por definicion, esto le agrega
                        // volumen de verdad colgando hacia el jugador, ver BuildCanopyBranch). Solo
                        // en el bosque de verdad, nunca en la zona aislada (esa sigue siendo cueva).
                        if (isForestBiome && !isIso && Random.value < 0.4f)
                            BuildCanopyBranch(center, cellSize, wallHeight);
                    }

                    // Pared: se construye una sola vez por borde compartido entre dos celdas reales
                    // (Norte/Este de cada celda). Si el vecino es Void, no hace falta pared propia -
                    // el bloque solido del vacio ya cubre y sella ese borde. Si no hay vecino (borde
                    // del mapa), esta celda es la unica que puede construirla.
                    foreach (var dir in DirectionExtensions.All)
                    {
                        if (!cell.HasWall(dir)) continue;
                        var (ox, oy) = dir.Offset();
                        int nx = x + ox, ny = y + oy;
                        bool outOfBounds = !floor.InBounds(nx, ny);
                        if (outOfBounds)
                        {
                            var edgeAnchor = BuildWall(center, dir, cellSize, wallHeight, wallThickness, useCaveLook, isForestBiome, isCastleBiome);
                            _wallAnchors[(x, y, dir)] = edgeAnchor;
                            // Tope estricto de luces reales por piso de cueva; el resto conserva su
                            // frecuencia de antorchas.
                            if (isRockCaveBiome) TryBuildCaveTorch(center, dir, cellSize, wallHeight);
                            else if (isCastleBiome && Random.value < 0.12f) _pendingFlickerTorches.Add((center, dir));
                            continue;
                        }
                        if (floor.Cells[nx, ny].Type == CellType.Void) continue;

                        bool isPrimaryDir = dir == Direction.North || dir == Direction.East;
                        if (isPrimaryDir)
                        {
                            var wallAnchor = BuildWall(center, dir, cellSize, wallHeight, wallThickness, useCaveLook, isForestBiome, isCastleBiome);
                            // Las dos claves apuntan al MISMO ancla (ver comentario junto a
                            // _wallAnchors): el jugador puede perforar mirando desde cualquiera de
                            // los dos lados de esta pared, aunque BuildWall solo se llamo una vez.
                            _wallAnchors[(x, y, dir)] = wallAnchor;
                            _wallAnchors[(nx, ny, dir.Opposite())] = wallAnchor;
                            if (isRockCaveBiome) TryBuildCaveTorch(center, dir, cellSize, wallHeight);
                            else if (isCastleBiome && Random.value < 0.12f) _pendingFlickerTorches.Add((center, dir));
                        }
                    }

                    if (isIso && floor.Biome == 0)
                    {
                        BuildCaveEntranceTorches(floor, x, y, center, cellSize);
                        // Cartel "R ANTORCHA" (pedido puntual: el jugador deberia enterarse del
                        // mecanismo la PRIMERA vez que pisa un espacio oscuro con antorchas, que es
                        // esta mini-cueva del piso 0 -- no recien en la entrada del Bioma de Cuevas
                        // real varios pisos despues, ver BuildCaveEntranceSignposts).
                        if (!miniCaveSignBuilt)
                        {
                            miniCaveSignBuilt = BuildMiniCaveEntranceSignpost(floor, x, y, center, cellSize);
                            if (miniCaveSignBuilt) _caveEntrancePoint = center;
                        }
                    }

                    if (cell.IsWaterfallRoom)
                        BuildWaterfallDecor(floor, x, y, center, cellSize, wallHeight);

                    if (cell.Type == CellType.Tomb)
                        BuildTombDecor(cell, center, cellSize, wallHeight);

                    BuildMarker(cell, center, cellSize, wallHeight, floor.Width, floor.Height, floor.Biome == 3 && floor.CastleRegion == 0);
                    if (cell.IsTrapCell && !floor.TrapDisabled) BuildTrapMarker(center, cellSize, floor.TrapKind);
                    // Peligro de sala de autor (ver RoomTemplate '^' / DungeonGenerator.
                    // TryStampRoomAt): a diferencia del marcador de picos de arriba (bien visible a
                    // proposito, sala de trampas dedicada), este tiene que ser CASI invisible --
                    // pedido puntual: "sigiloso, que pille desprevenido si no va atento al suelo".
                    // Ver BuildHazardTell mas abajo. Sin depender de floor.TrapDisabled: este
                    // peligro es personalidad fija de la sala, nunca se desactiva.
                    if (cell.IsPredefinedRoomHazard) BuildHazardTell(center, cellSize);
                    if (cell.IsPuzzleTile) BuildPuzzleTile(center, cellSize, cell.IsPuzzleTileSafe, floor.LoreCorridorKind);
                    // Brasas: la celda "falsa" duele igual que un pico (ver DungeonManager.
                    // OnPlayerEnterCell) pero el unico aviso era la particula de brasas cayendo
                    // desde arriba -- facil de perder de vista. Esto pone el peligro EN el piso
                    // mismo, bien visible, sin tocar el mecanismo del puzzle.
                    if (cell.IsPuzzleTile && !cell.IsPuzzleTileSafe && floor.LoreCorridorKind == PuzzleKind.Brasas)
                        BuildBrasasDangerFloor(center, cellSize);
                    if (cell.Type == CellType.Lore) BuildLoreSceneDressing(cell, center, cellSize);
                    // Corredor de la roca que persigue (ver DungeonGenerator.AddBoulderTrap): igual
                    // criterio que el resto de las trampas -- se lee de un vistazo, nunca invisible
                    // como el peligro de sala de autor de arriba.
                    if (cell.IsBoulderTrapCell) BuildBoulderCorridorMarker(center, cellSize);
                    // Sala que colapsa (ver DungeonGenerator.AddCollapsingRushRoom): grieta de aviso
                    // por celda, que CollapseFloorVisual reemplaza por un pozo cuando de verdad le
                    // toca el turno de caer (ver Gameplay/DungeonManager.CollapseRoomRoutine).
                    if (cell.IsCollapseRoom) BuildCollapseMarker(x, y, center, cellSize);
                }
            }

            if (floor.Biome == 0)
                BuildForestCanopy(floor, cellSize, wallHeight);

            if (floor.Biome == 4)
                BuildCastleRoomDressing(floor, cellSize, wallHeight);

            // Carteles de madera con los controles basicos, SOLO en el piso 0 (el arranque real de
            // la run) y clavados justo donde aparece el jugador -- a diferencia de un cartel de UI
            // (ControlsTutorialHUD, que necesita que lo enganchen a mano en la escena), esto sale
            // gratis con cada Build() porque es geometria de la mazmorra como cualquier otra, asi
            // que SIEMPRE esta ahi sin depender de wiring manual.
            if (floor.Index == 0)
                BuildSpawnSignposts(floor, cellSize);

            // Cartel de entrada a la cueva (pedido puntual, "como los del inicio"): mismo prop de
            // madera (BuildSignpost), pero solo en el PRIMER piso PROPIO del Bioma de Cuevas
            // (BiomeFloorIndex, no floor.Index -- ese sigue la numeracion global de TODOS los
            // biomas concatenados) y con contenido distinto, ver BuildCaveEntranceSignposts.
            if (floor.Biome == 2 && floor.BiomeFloorIndex == 0)
                BuildCaveEntranceSignposts(floor, cellSize);

            // Optimizacion puntual (pedido: "que corra a 60fps fluido"): marcar go.isStatic = true
            // en la geometria que nunca se mueve ni cambia de material (pared/piso/techo/vacio/sala
            // de jefe, ver esos metodos) NO alcanza solo -- el static batching automatico de Unity
            // solo procesa objetos que ya estaban en la escena al compilar el build, y toda esta
            // mazmorra se genera en runtime via CreatePrimitive. Para objetos instanciados en
            // runtime hay que pedir el combine a mano con StaticBatchingUtility.Combine: agrupa por
            // material compartido a los hijos de _root en unos pocos draw calls en vez de uno por
            // cada pared/piso/techo individual (en un piso de 16x16 son cientos).
            //
            // BUG encontrado (reportado como "las mariposas/ratones estan quietas" pese a que su
            // logica de movimiento SI actualizaba posicion/velocidad cada frame, confirmado con un
            // overlay de debug en vivo): el comentario original de aca decia que los props
            // dinamicos "no llevan isStatic, asi que Combine los deja afuera" -- FALSO. La API en
            // tiempo de ejecucion StaticBatchingUtility.Combine(root) NO mira isStatic para nada
            // (eso es solo lo que respeta el batching automatico del Editor al compilar el build,
            // un mecanismo totalmente distinto): agarra CUALQUIER Renderer que encuentre colgando
            // de _root al momento de llamarla y lo funde en una malla combinada, estatica, congelada
            // en ese instante -- mover el transform de un hijo despues de eso ya no tiene efecto
            // visual ninguno, aunque el codigo siga corriendo perfecto (por eso el pasto con viento
            // se salva: GroundTileFactory lo dibuja aparte via Graphics.DrawMeshInstanced, nunca es
            // un Renderer hijo de _root; pero la fauna ambiental SI lo era, y quedaba fusionada).
            // Fix: BuildAmbientCritters/BuildCastleMice (la unica geometria de _root que de verdad
            // se mueve frame a frame) se llaman DESPUES del Combine, para que sus Renderers ni
            // existan todavia cuando Combine recorre la jerarquia.
            //
            // MISMO bug en las antorchas con parpadeo (TorchFlameFlicker, BuildTorch flicker:true):
            // se piden desde adentro del loop principal de arriba (paredes de castillo, entrada de
            // la mini-cueva del piso 0, antorcha de cascada) igual que cualquier otra pared/piso, asi
            // que su llama tambien quedaba fusionada y congelada antes de este fix. A diferencia de
            // la fauna, BuildTorch se llama desde MUCHOS puntos dispersos del loop (no un solo metodo
            // al final) -- en vez de reordenar cada call site, el loop solo encola (centro, direccion)
            // en _pendingFlickerTorches y recien aca, ya pasado el Combine, se construyen las
            // antorchas de verdad con esos mismos datos.
            StaticBatchingUtility.Combine(_root);

            foreach (var (torchCenter, torchDir) in _pendingFlickerTorches)
                BuildTorch(torchCenter, torchDir, cellSize, wallHeight, flicker: true);

            if (floor.Biome == 0 || floor.Biome == 2)
                BuildAmbientCritters(floor, cellSize, wallHeight);
            else if (floor.Biome == 4)
                BuildCastleMice(floor, cellSize);

            // Pasto suelto de todo el piso (ver GroundTileFactory.BuildGrassTufts/BuildSwordGrass,
            // que fueron acumulando instancias en FoliageManager celda por celda durante el loop de
            // arriba): recien ahora, con el piso entero recorrido, se empaquetan en lotes listos
            // para Graphics.DrawMeshInstanced.
            FoliageManager.Instance.FinalizeBatches();
        }

        // Marcador de peligro, chato y pegado al piso (no un marcador "de interaccion" como los de
        // BuildMarker), para que el jugador pueda LEER el patron de la sala y decidir si cruzar o
        // no en vez de que sea una sorpresa invisible. Cada TrapKind se ve claramente distinto:
        // ArrowSweep es una placa lisa (la trayectoria de la flecha), SpikeCells son picos
        // sueltos que sobresalen del piso (ver BuildSpikeMarker) -- de un vistazo se nota si el
        // peligro es "una linea que cruza" o "no pises esta celda puntual".
        private void BuildTrapMarker(Vector3 center, float cellSize, TrapKind kind)
        {
            if (kind == TrapKind.ArrowSweep) BuildArrowLineMarker(center, cellSize);
            else BuildSpikeMarker(center, cellSize);
        }

        private void BuildArrowLineMarker(Vector3 center, float cellSize)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TrapMarker";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, 0.03f, 0);
            go.transform.localScale = new Vector3(cellSize * 0.85f, 0.05f, cellSize * 0.85f);

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            ApplyMaterial(go, trapMarkerMaterial, new Color(0.75f, 0.1f, 0.05f));
        }

        // Picos sueltos: un par de cunas oscuras y filosas asomando del piso en vez de la placa
        // lisa de la linea de flechas, asi la sala se lee distinto a primera vista.
        private void BuildSpikeMarker(Vector3 center, float cellSize)
        {
            var offsets = new[] { new Vector2(-0.2f, -0.15f), new Vector2(0.18f, -0.2f), new Vector2(0.02f, 0.2f) };
            foreach (var off in offsets)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "SpikeMarker";
                go.transform.SetParent(_root.transform, false);
                go.transform.position = center + new Vector3(off.x * cellSize, cellSize * 0.13f, off.y * cellSize);
                go.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
                go.transform.localScale = new Vector3(cellSize * 0.16f, cellSize * 0.28f, cellSize * 0.16f);

                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);

                ApplyMaterial(go, trapMarkerMaterial, new Color(0.3f, 0.06f, 0.05f));
            }
        }

        // Corredor de la roca que persigue (ver DungeonGenerator.AddBoulderTrap): una placa chata
        // de piedra agrietada, mismo espiritu que BuildArrowLineMarker de arriba -- se lee de un
        // vistazo como "esto es una linea de trampa", sin revelar todavia que la trampa es una roca.
        private void BuildBoulderCorridorMarker(Vector3 center, float cellSize)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "BoulderCorridorMarker";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, 0.025f, 0);
            go.transform.localScale = new Vector3(cellSize * 0.9f, 0.04f, cellSize * 0.9f);

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            ApplyMaterial(go, trapMarkerMaterial, new Color(0.32f, 0.28f, 0.24f));
        }

        // Marcador por celda de la sala que colapsa (ver DungeonGenerator.AddCollapsingRushRoom):
        // arranca como una grieta de aviso chata; cuando le toca el turno de caer de verdad (ver
        // Gameplay/DungeonManager.CollapseRoomRoutine) CollapseFloorVisual la reemplaza por un pozo
        // oscuro. Se guarda por celda en _collapseMarkers para poder encontrarla despues en runtime.
        private readonly Dictionary<(int x, int y), GameObject> _collapseMarkers = new Dictionary<(int, int), GameObject>();

        private void BuildCollapseMarker(int x, int y, Vector3 center, float cellSize)
        {
            var go = new GameObject($"CollapseMarker_{x}_{y}");
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + Vector3.up * 0.045f;
            go.transform.localScale = Vector3.one * cellSize; // los hijos usan escala relativa 1, no hace falta cellSize despues

            var crack = GameObject.CreatePrimitive(PrimitiveType.Quad);
            crack.name = "CollapseCrack";
            crack.transform.SetParent(go.transform, false);
            crack.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            crack.transform.localScale = new Vector3(0.85f, 0.85f, 1f);
            var col = crack.GetComponent<Collider>();
            if (col != null) Destroy(col);
            ApplyMaterial(crack, brasasDangerFloorMaterial, new Color(0.35f, 0.28f, 0.22f, 0.55f));

            _collapseMarkers[(x, y)] = go;
        }

        // Llamado por DungeonManager cuando una celda de la sala que colapsa termina de caer:
        // reemplaza la grieta de aviso por un pozo oscuro real -- puramente visual, la celda REAL
        // sigue siendo caminable a nivel de grilla (DungeonManager.OnPlayerEnterCell es quien
        // decide que pisarla te hace caer, ver DungeonCell.IsCollapseFallen).
        public void CollapseFloorVisual(int x, int y)
        {
            if (!_collapseMarkers.TryGetValue((x, y), out var go) || go == null) return;
            foreach (Transform child in go.transform) Destroy(child.gameObject);

            var hole = GameObject.CreatePrimitive(PrimitiveType.Quad);
            hole.name = "CollapsedHole";
            hole.transform.SetParent(go.transform, false);
            hole.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            hole.transform.localScale = new Vector3(0.95f, 0.95f, 1f);
            var col = hole.GetComponent<Collider>();
            if (col != null) Destroy(col);
            ApplyMaterial(hole, voidBlockMaterial, Color.black);
        }

        // Pista de un peligro de sala de autor (ver RoomTemplate '^'): pedido puntual "sigiloso, que
        // pille desprevenido si no va atento al suelo" -- nada de picos sobresaliendo ni color de
        // alarma. Un solo parche chato, apenas mas oscuro que el piso alrededor (se lee como tierra
        // pisoteada o una baldosa floja, no como un cartel de "peligro"), con posicion y rotacion al
        // azar para que tampoco se sienta como un icono de UI perfectamente centrado. Sin collider:
        // es pura decoracion sobre el piso real, el dano lo decide DungeonCell.IsPredefinedRoomHazard
        // en DungeonManager.OnPlayerEnterCell.
        private void BuildHazardTell(Vector3 center, float cellSize)
        {
            var patch = GameObject.CreatePrimitive(PrimitiveType.Quad);
            patch.name = "PredefinedRoomHazardTell";
            patch.transform.SetParent(_root.transform, false);
            Vector2 jitter = Random.insideUnitCircle * cellSize * 0.12f;
            patch.transform.position = center + new Vector3(jitter.x, 0.03f, jitter.y);
            patch.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            patch.transform.localScale = new Vector3(cellSize * 0.4f, cellSize * 0.4f, 1f);

            var col = patch.GetComponent<Collider>();
            if (col != null) Destroy(col);

            ApplyMaterial(patch, null, new Color(0.22f, 0.19f, 0.15f));
        }

        // Celda de una sala de pistas (ver DungeonGenerator.AddLoreCorridorRoom). Brasas/Polvo de
        // Cuarzo siguen ocultos a proposito (NUNCA un color de marcador, solo el comportamiento de
        // la particula distingue piso real de piso falso). Goteras (agua) ya NO es un puzzle oculto
        // -- ver DungeonManager.OnPlayerEnterCell: pisar una celda falsa te hace caer al piso de
        // abajo de verdad, asi que el piso falta directamente (ver Build/BuildFloorTile) y esta
        // particula es solo la guia visual de "por aca cae el agua, por aca NO hay piso".
        private void BuildPuzzleTile(Vector3 center, float cellSize, bool isSafe, PuzzleKind kind)
        {
            bool isVisibleVoid = kind == PuzzleKind.Goteras && !isSafe;
            string name = $"PuzzleTile_{kind}_{(isSafe ? "real" : "falso")}";
            var ps = ParticleLayerFactory.CreateLayer(_root.transform, name);
            ps.transform.position = center + new Vector3(0, cellSize * 1.6f, 0);

            var main = ps.main;
            main.loop = true;
            main.startSpeed = 0f;
            main.startSize = cellSize * (isVisibleVoid ? 0.13f : 0.05f);
            main.gravityModifier = isSafe ? 1.1f : 0.5f;
            main.maxParticles = isVisibleVoid ? 20 : 8;
            // Real: vive lo justo para llegar al piso y chocar (ver collision module abajo, ahi
            // desaparece de verdad, no por vencerse la vida). Falso: nada la frena, asi que necesita
            // vida larga para de verdad "seguir de largo" hasta perderse de la camara en vez de
            // desvanecerse a mitad de caida como antes.
            main.startLifetime = isSafe ? 2.5f : 3.5f;

            Color color = kind switch
            {
                PuzzleKind.Brasas => new Color(1f, 0.5f, 0.15f),
                PuzzleKind.PolvoDeCuarzo => new Color(0.55f, 0.85f, 1f),
                _ => new Color(0.5f, 0.75f, 1f), // Goteras
            };
            // Goteras falso: bien visible (es la guia del camino al tesoro, no un tell sutil que
            // ocultar). Brasas/Polvo de Cuarzo falso: siguen semitransparentes, sutiles a proposito.
            main.startColor = isSafe ? color : (isVisibleVoid ? color : new Color(color.r, color.g, color.b, 0.35f));

            var emission = ps.emission;
            emission.rateOverTime = isSafe ? 2.5f : (isVisibleVoid ? 6f : 1.2f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = cellSize * 0.3f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            // Lo real choca de verdad contra un plano puesto a la altura del piso y desaparece ahi
            // mismo con un mini-splash (ver BuildPuzzleSplashTemplate); lo falso no tiene plano, asi
            // que sigue cayendo de largo sin frenar -- en Brasas/Polvo de Cuarzo es el unico tell
            // real de la sala, en Goteras es ademas coherente con que ahi de verdad no hay piso.
            if (isSafe)
            {
                var floorPlane = new GameObject("PuzzleTileFloorPlane");
                floorPlane.transform.SetParent(_root.transform, false);
                floorPlane.transform.position = center + Vector3.up * 0.05f;
                floorPlane.transform.rotation = Quaternion.identity; // normal hacia +Y (arriba)

                var collision = ps.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.Planes;
                collision.SetPlane(0, floorPlane.transform);
                collision.lifetimeLoss = 1f; // desaparece apenas toca, no se acumula en el piso

                if (_puzzleSplashTemplate == null) _puzzleSplashTemplate = BuildPuzzleSplashTemplate(cellSize);
                ps.subEmitters.AddSubEmitter(_puzzleSplashTemplate, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing, 1f);
            }

            ParticleLayerFactory.Activate(ps);

            // Borde: una segunda capa igual pero mas grande, oscura y detras (mismo shape/emision,
            // sin colision -- cae junto a la principal) -- asoma como un anillo alrededor de cada
            // gota en vez de una silueta lisa, para que se note incluso sobre un piso claro.
            if (isVisibleVoid) BuildPuzzleTileOutline(center, cellSize);
        }

        // Burst chico y unico (no loop) que dispara la particula "real" justo al chocar contra el
        // piso -- unas pocas gotas mas que salen disparadas hacia los costados, el efecto de
        // salpicadura. Una sola instancia (ver _puzzleSplashTemplate) sirve de plantilla para TODAS
        // las celdas reales de la sala, Unity soporta que varios sub-emitters compartan la misma.
        private ParticleSystem BuildPuzzleSplashTemplate(float cellSize)
        {
            var ps = ParticleLayerFactory.CreateLayer(_root.transform, "PuzzleTile_Splash");

            var main = ps.main;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = 0.25f;
            main.startSpeed = cellSize * 1.3f;
            main.startSize = cellSize * 0.06f;
            main.startColor = new Color(1f, 1f, 1f, 0.7f);
            main.gravityModifier = 0.6f;
            main.maxParticles = 16;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 5) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // dispara hacia afuera/arriba desde el punto de choque

            return ps;
        }

        private void BuildPuzzleTileOutline(Vector3 center, float cellSize)
        {
            var ps = ParticleLayerFactory.CreateLayer(_root.transform, "PuzzleTile_Goteras_borde");
            ps.transform.position = center + new Vector3(0, cellSize * 1.6f, 0);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = 3.5f; // misma vida que la capa principal falsa, cae junto a ella
            main.startSpeed = 0f;
            main.startSize = cellSize * 0.19f;
            main.gravityModifier = 0.5f;
            main.maxParticles = 20;
            main.startColor = new Color(0.05f, 0.15f, 0.3f, 0.8f);

            var emission = ps.emission;
            emission.rateOverTime = 6f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = cellSize * 0.3f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sortingOrder = -1; // detras de la capa clara principal

            ParticleLayerFactory.Activate(ps);
        }

        // Parche de piso agrietado al rojo vivo sobre una celda de Brasas peligrosa (ver Build):
        // reemplaza la unica pista anterior (la particula de brasas cayendo, facil de perder de
        // vista) por algo que se lee de un vistazo SIN tocar el mecanismo del puzzle -- la celda
        // sigue siendo la misma, IsPuzzleTileSafe sigue decidiendo el dano en DungeonManager.
        // OnPlayerEnterCell, esto es solo el aviso. Sin collider: es decoracion sobre el piso real.
        private void BuildBrasasDangerFloor(Vector3 center, float cellSize)
        {
            var basePatch = GameObject.CreatePrimitive(PrimitiveType.Quad);
            basePatch.name = "BrasasDangerFloor";
            basePatch.transform.SetParent(_root.transform, false);
            basePatch.transform.position = center + new Vector3(0, 0.055f, 0);
            basePatch.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            basePatch.transform.localScale = new Vector3(cellSize * 0.92f, cellSize * 0.92f, 1f);
            var baseCol = basePatch.GetComponent<Collider>();
            if (baseCol != null) Destroy(baseCol);
            ApplyMaterial(basePatch, brasasDangerFloorMaterial, new Color(0.14f, 0.03f, 0.02f));

            // 3 grietas brillantes tipo lava encima de la base oscura -- el contraste oscuro/al rojo
            // vivo es lo que se lee como "peligro" a distancia, no un color plano.
            var cracks = new[]
            {
                (offset: new Vector2(-0.2f, 0.1f), rotY: 30f),
                (offset: new Vector2(0.15f, -0.15f), rotY: 110f),
                (offset: new Vector2(0.02f, 0.25f), rotY: 70f),
            };
            foreach (var crack in cracks)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "BrasasCrack";
                go.transform.SetParent(_root.transform, false);
                go.transform.position = center + new Vector3(crack.offset.x * cellSize, 0.06f, crack.offset.y * cellSize);
                go.transform.rotation = Quaternion.Euler(90f, crack.rotY, 0f);
                go.transform.localScale = new Vector3(cellSize * 0.5f, cellSize * 0.09f, 1f);
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                ApplyMaterial(go, null, new Color(1f, 0.35f, 0.05f));
            }
        }

        // Dos carteles de madera en la celda SIGUIENTE a donde arranca la run (ver Build), no en
        // la propia celda de Start -- y mirando hacia atras, al jugador. "Siguiente" es la
        // primera salida real de la celda de Start (ver DungeonCell.FirstOpenDirection), la MISMA
        // cuenta que usa DungeonManager.GenerateAndEnterDungeon para pararlo mirando para aca: el
        // jugador siempre arranca mirando de frente hacia los carteles, nunca de costado o de
        // espaldas.
        private void BuildSpawnSignposts(DungeonFloor floor, float cellSize)
        {
            var startCell = floor.Cells[floor.StartPos.x, floor.StartPos.y];
            Direction facing = startCell.FirstOpenDirection();
            var (ox, oy) = facing.Offset();

            Vector3 aheadCenter = CellCenter(floor.StartPos.x + ox, floor.StartPos.y + oy, cellSize);
            // Perpendicular a la direccion de avance (rotar 90 grados en el plano XZ), para
            // separar los dos carteles a los costados de la celda de llegada.
            Vector3 side = new Vector3(-oy, 0, ox) * cellSize * 0.28f;
            // El texto tiene que MIRAR hacia atras, de vuelta al jugador que viene desde Start --
            // el yaw que corresponde es el mismo de "facing" (NO facing.Opposite(): eso se probo
            // recien y el cartel paso de "tapado por el tablon" a "visible pero en espejo" -- ver
            // BuildSignpost/textZ mas abajo para el fix real, que es de que CARA del tablon cuelga
            // el texto, no de este yaw).
            float signYaw = DirectionYaw(facing);

            BuildSignpost(aheadCenter - side, signpostMoveText, signYaw);
            BuildSignpost(aheadCenter + side, signpostMapText, signYaw);
        }

        // Mismo prop/geometria que BuildSpawnSignposts de arriba (dos carteles a los costados de la
        // celda siguiente a Start, mirando hacia atras al jugador), pero pedido puntual: contenido
        // de advertencia/tip de la cueva en vez de repetir los controles basicos, que ya se
        // explicaron en el piso 0 real del juego.
        private void BuildCaveEntranceSignposts(DungeonFloor floor, float cellSize)
        {
            var startCell = floor.Cells[floor.StartPos.x, floor.StartPos.y];
            Direction facing = startCell.FirstOpenDirection();
            var (ox, oy) = facing.Offset();

            Vector3 aheadCenter = CellCenter(floor.StartPos.x + ox, floor.StartPos.y + oy, cellSize);
            Vector3 side = new Vector3(-oy, 0, ox) * cellSize * 0.28f;
            float signYaw = DirectionYaw(facing);

            BuildSignpost(aheadCenter - side, "CUIDADO\nNIDO GOBLIN", signYaw);
            BuildSignpost(aheadCenter + side, "R\nANTORCHA", signYaw);
        }

        // Mismo mapeo Direction -> yaw que GridPlayerController.FacingRotation (TIENEN que
        // coincidir: es el mismo "hacia donde mira" para el jugador y para el cartel).
        private static float DirectionYaw(Direction d) => d switch
        {
            Direction.North => 0f,
            Direction.East => 90f,
            Direction.South => 180f,
            Direction.West => 270f,
            _ => 0f
        };

        private void BuildSignpost(Vector3 basePos, string text, float facingYaw)
        {
            var root = new GameObject("Signpost");
            root.transform.SetParent(_root.transform, false);
            root.transform.position = basePos;
            root.transform.rotation = Quaternion.Euler(0, facingYaw, 0);

            const float postHeight = 1.5f;
            const float postThickness = 0.12f;
            float boardY = postHeight * 0.82f;

            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "SignpostPost";
            post.transform.SetParent(root.transform, false);
            post.transform.localPosition = new Vector3(0, postHeight * 0.5f, 0);
            post.transform.localScale = new Vector3(postThickness, postHeight, postThickness);
            var postCol = post.GetComponent<Collider>();
            if (postCol != null) Destroy(postCol);
            ApplyMaterial(post, signpostWoodMaterial, new Color(0.32f, 0.2f, 0.12f));

            const float boardW = 1.7f;
            const float boardH = 0.95f;
            const float boardDepth = 0.1f;
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "SignpostBoard";
            board.transform.SetParent(root.transform, false);
            board.transform.localPosition = new Vector3(0, boardY, 0);
            board.transform.localScale = new Vector3(boardW, boardH, boardDepth);
            var boardCol = board.GetComponent<Collider>();
            if (boardCol != null) Destroy(boardCol);
            ApplyMaterial(board, signpostBoardMaterial, new Color(0.42f, 0.27f, 0.15f));

            // Vetas de madera (pedido puntual: "que se parezca mas a madera"): tablones oscuros
            // finitos superpuestos a la cara del cartel, mismo truco que las grietas de tierra.
            BuildWoodGrain(root.transform, boardY, boardW, boardH, boardDepth);

            // UN solo texto (antes eran dos, uno de cada lado, y sin cull de backface se veian
            // los dos superpuestos/mezclados) -- ahora sabemos con certeza desde que lado lo va a
            // ver el jugador (root ya esta rotado hacia el), asi que alcanza con el lado de
            // adelante. Pegado casi a ras de la cara del tablon (mitad del grosor + un pelo), no a
            // un 0.045 fijo -- y el texto ahora se reescala para entrar siempre en el tablon (ver
            // BuildSignText): esas dos cosas juntas eran por que "no estaba sobre la superficie".
            // BUG encontrado con capturas reales en juego: el signo tenia que ser NEGATIVO, no
            // positivo. TextMesh lee bien (sin espejo) visto desde el lado -Z de SU PROPIO local
            // (identico al de "root", ya que este objeto no tiene rotacion local propia). El
            // jugador se acerca desde Start caminando HACIA "facing" -- con signYaw = facing, el
            // jugador queda parado del lado local -Z del cartel. Con textZ positivo el texto colgaba
            // de la cara local +Z (la de ATRAS, del otro lado del cubo solido del tablon: quedaba
            // tapado, tablon en blanco). Con textZ negativo cuelga de la cara local -Z, la que el
            // tablon nunca tapa desde donde el jugador realmente esta parado, y que ademas es el
            // lado donde TextMesh lee normal (no en espejo).
            float textZ = -(boardDepth * 0.5f + 0.01f);
            BuildSignText(root.transform, new Vector3(0, boardY, textZ), text, boardW * 0.92f, boardH * 0.8f);
        }

        // Tablones finitos y oscuros superpuestos horizontalmente sobre la cara del cartel --
        // rompe el color plano de un solo cubo marron y lo hace leer como madera de verdad, sin
        // necesitar una textura importada (este proyecto no tiene ninguna).
        private void BuildWoodGrain(Transform parent, float boardY, float boardW, float boardH, float boardDepth)
        {
            int planks = 3;
            float plankH = boardH / planks;
            for (int i = 0; i < planks; i++)
            {
                float y = boardY - boardH * 0.5f + plankH * (i + 0.5f);
                var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                line.name = "WoodGrainLine";
                line.transform.SetParent(parent, false);
                line.transform.localPosition = new Vector3(0, y - plankH * 0.42f, 0);
                line.transform.localScale = new Vector3(boardW * 0.98f, plankH * 0.08f, boardDepth * 1.05f);
                var col = line.GetComponent<Collider>();
                if (col != null) Destroy(col);
                ApplyMaterial(line, null, new Color(0.24f, 0.14f, 0.07f));
            }
        }

        private void BuildSignText(Transform parent, Vector3 localPos, string text, float maxWidth, float maxHeight)
        {
            var go = new GameObject("SignText");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.characterSize = signpostCharacterSize;
            tm.fontSize = signpostFontSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.95f, 0.9f, 0.75f);

            // El material de fuente por defecto de TextMesh es transparente y no escribe
            // profundidad -- por eso el texto se veia "flotando" a traves de las paredes en vez de
            // taparse detras de ellas. Custom/PS1SignText usa la MISMA textura de fuente pero en
            // la cola AlphaTest (si escribe profundidad), asi vuelve a ocluirse como cualquier
            // geometria opaca.
            var renderer = go.GetComponent<MeshRenderer>();
            var textMat = new Material(SignTextMaterial());
            textMat.mainTexture = tm.font.material.mainTexture;
            renderer.material = textMat;

            // Reescala el objeto entero para que el bloque de texto SIEMPRE entre en el tablon,
            // sin importar signpostCharacterSize/signpostFontSize elegidos en el Inspector -- con
            // texto de 2 lineas el bloque generado podia ser mas alto que el propio cartel y
            // quedaba sobresaliendo por fuera de su superficie en vez de sentado sobre ella.
            // TextMesh NO tiene MeshFilter (agregarle uno a mano tira "conflicts with TextMesh",
            // Unity los trata como mutuamente excluyentes) asi que el bounds hay que leerlo del
            // MeshRenderer, que es en espacio de MUNDO. El cartel solo rota en Y de a multiplos de
            // 90 grados (ver BuildSpawnSignposts), asi que la altura (eje Y) nunca se mezcla con el
            // ancho -- el ancho real queda repartido entre bounds.x y bounds.z segun el yaw exacto,
            // por eso se usa el mayor de los dos (el otro queda ~0).
            float worldWidth = Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.z);
            float worldHeight = renderer.bounds.size.y;
            float scaleX = worldWidth > 0.0001f ? maxWidth / worldWidth : 1f;
            float scaleY = worldHeight > 0.0001f ? maxHeight / worldHeight : 1f;
            float fit = Mathf.Min(1f, scaleX, scaleY); // solo achica si hace falta, nunca agranda de mas
            go.transform.localScale *= fit;
        }

        private static Material _signTextMaterial;

        private static Material SignTextMaterial()
        {
            if (_signTextMaterial == null)
            {
                var resourceMat = Resources.Load<Material>("Materials/PS1SignText");
                if (resourceMat != null)
                {
                    _signTextMaterial = new Material(resourceMat);
                }
                else
                {
                    var shader = Shader.Find("Custom/PS1SignText");
                    _signTextMaterial = new Material(shader != null ? shader : Shader.Find("Legacy Shaders/Transparent/Cutout/VertexLit"));
                }
            }
            return _signTextMaterial;
        }

        // Puesta en escena de la celda de lore (ver Lore.SceneDressing): busca el fragmento
        // asignado y, si describe un evento con marca fisica, agrega 2-3 props chicos ALREDEDOR del
        // marcador ya construido por BuildMarker -- la idea es que la sala respalde lo que el texto
        // cuenta en vez de que el fragmento sea la unica fuente de la escena. Silencioso si el id no
        // resuelve o el fragmento es None (ver comentario en LoreEntry.cs): no forzar escombros que
        // el texto no sostiene.
        private void BuildLoreSceneDressing(DungeonCell cell, Vector3 center, float cellSize)
        {
            var entry = LoreCatalog.Find(cell.AssignedLoreId);
            if (entry == null) return;

            switch (entry.Dressing)
            {
                case SceneDressing.Scorched: BuildScorchedDressing(center, cellSize); break;
                case SceneDressing.Collapsed: BuildCollapsedDressing(center, cellSize); break;
                case SceneDressing.Ambush: BuildAmbushDressing(center, cellSize); break;
                case SceneDressing.Camp: BuildCampDressing(center, cellSize); break;
                default: return; // None: nada que agregar, a proposito
            }
        }

        // "Un trozo de mapa, quemado en los bordes" (mapa_fragmentado): parches de ceniza chatos
        // pegados al piso alrededor del marcador, como el rastro de un fuego que ya se apago.
        private void BuildScorchedDressing(Vector3 center, float cellSize)
        {
            var offsets = new[] { new Vector2(-0.32f, 0.1f), new Vector2(0.28f, 0.22f), new Vector2(0.05f, -0.35f) };
            foreach (var off in offsets)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "LoreDressing_Ash";
                go.transform.SetParent(_root.transform, false);
                go.transform.position = center + new Vector3(off.x * cellSize, 0.025f, off.y * cellSize);
                go.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                go.transform.localScale = new Vector3(cellSize * 0.22f, 0.03f, cellSize * 0.18f);

                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);

                ApplyMaterial(go, loreDressingMaterial, new Color(0.07f, 0.06f, 0.05f));
            }
        }

        // "Mucho antes de que llegaramos", eco sin firma (voz_en_la_piedra): un par de escombros
        // caidos e irregulares, mas viejos que el resto de la sala -- esto lleva ahi mucho tiempo.
        private void BuildCollapsedDressing(Vector3 center, float cellSize)
        {
            var rubble = new[]
            {
                (pos: new Vector2(-0.3f, -0.22f), scale: 0.22f, rotY: 12f),
                (pos: new Vector2(0.26f, -0.12f), scale: 0.16f, rotY: 50f),
                (pos: new Vector2(0.02f, 0.33f), scale: 0.19f, rotY: 205f),
            };
            foreach (var r in rubble)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "LoreDressing_Rubble";
                go.transform.SetParent(_root.transform, false);
                go.transform.position = center + new Vector3(r.pos.x * cellSize, cellSize * r.scale * 0.5f, r.pos.y * cellSize);
                go.transform.rotation = Quaternion.Euler(UnityEngine.Random.Range(-8f, 8f), r.rotY, UnityEngine.Random.Range(-8f, 8f));
                go.transform.localScale = Vector3.one * cellSize * r.scale;

                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);

                ApplyMaterial(go, loreDressingMaterial, new Color(0.32f, 0.29f, 0.26f));
            }
        }

        // "Golpean donde la formacion esta mas expuesta" (cronicas_guardianes): restos de un
        // combate ya terminado -- una placa quebrada en el piso y un par de esquirlas oscuras, no
        // otra trampa activa (esto ya paso, no es un peligro presente).
        private void BuildAmbushDressing(Vector3 center, float cellSize)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "LoreDressing_BrokenPlate";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(-0.05f * cellSize, 0.03f, 0.28f * cellSize);
            go.transform.rotation = Quaternion.Euler(0f, 20f, 6f);
            go.transform.localScale = new Vector3(cellSize * 0.4f, 0.05f, cellSize * 0.32f);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            ApplyMaterial(go, loreDressingMaterial, new Color(0.35f, 0.14f, 0.1f));

            var shardOffsets = new[] { new Vector2(0.24f, -0.2f), new Vector2(-0.3f, -0.08f) };
            foreach (var off in shardOffsets)
            {
                var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shard.name = "LoreDressing_Shard";
                shard.transform.SetParent(_root.transform, false);
                shard.transform.position = center + new Vector3(off.x * cellSize, cellSize * 0.09f, off.y * cellSize);
                shard.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 35f);
                shard.transform.localScale = new Vector3(cellSize * 0.1f, cellSize * 0.18f, cellSize * 0.05f);
                var shardCol = shard.GetComponent<Collider>();
                if (shardCol != null) Destroy(shardCol);
                ApplyMaterial(shard, loreDressingMaterial, new Color(0.22f, 0.08f, 0.07f));
            }
        }

        // Las 3 pistas de la Puerta Fria (puerta_fria_1/2/3): la misma expedicion acampo en cada
        // punto de su recorrido. Un pozo de fogata apagado + algo parecido a un lugar donde dormir,
        // para que las 3 salas se lean como el mismo camino en vez de tres escenas sin relacion.
        private void BuildCampDressing(Vector3 center, float cellSize)
        {
            var pit = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pit.name = "LoreDressing_FirePit";
            pit.transform.SetParent(_root.transform, false);
            pit.transform.position = center + new Vector3(0.26f * cellSize, 0.02f, -0.24f * cellSize);
            pit.transform.localScale = new Vector3(cellSize * 0.22f, 0.02f, cellSize * 0.22f);
            var pitCol = pit.GetComponent<Collider>();
            if (pitCol != null) Destroy(pitCol);
            ApplyMaterial(pit, loreDressingMaterial, new Color(0.1f, 0.09f, 0.08f));

            var bedroll = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bedroll.name = "LoreDressing_Bedroll";
            bedroll.transform.SetParent(_root.transform, false);
            bedroll.transform.position = center + new Vector3(-0.28f * cellSize, 0.035f, 0.2f * cellSize);
            bedroll.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            bedroll.transform.localScale = new Vector3(cellSize * 0.4f, 0.07f, cellSize * 0.18f);
            var bedrollCol = bedroll.GetComponent<Collider>();
            if (bedrollCol != null) Destroy(bedrollCol);
            ApplyMaterial(bedroll, loreDressingMaterial, new Color(0.3f, 0.24f, 0.15f));
        }

        // Un bloque solido de piso a techo (y un poco mas, para que no se vean costuras) que ocupa
        // toda la celda: esto es lo que separa dos caminos entre si, no una pared delgada.
        // Pedido puntual: "el agua no es una pared" -- antes esta celda era un cubo SOLIDO Y
        // VISIBLE de piso a techo (bloqueaba Y se veia como una pared). Ahora el bloqueo de paso es
        // un collider invisible (nadie ve una "pared" ahi, pero el jugador sigue sin poder entrar
        // -- el Perforador tampoco puede abrir camino a traves de una celda Void, asi que esto no
        // le quita nada a esa mecanica) y la parte VISIBLE es una laguna hundida (agua normal;
        // lava dentro del bioma castillo, ver BuildSunkenPool), a la vista desde el borde del pasillo.
        private void BuildVoidBlock(DungeonFloor floor, int x, int y, Vector3 center, float cellSize, float wallHeight)
        {
            var blocker = new GameObject("VoidBlocker");
            blocker.transform.SetParent(_root.transform, false);
            blocker.transform.position = center + new Vector3(0, wallHeight / 2f, 0);
            var col = blocker.AddComponent<BoxCollider>();
            col.size = new Vector3(cellSize, wallHeight + 0.4f, cellSize);
            blocker.isStatic = true;

            if (floor.Biome == 4)
            {
                // Los vacios del interior del castillo son pozos de lava hundidos, con la misma
                // forma y fusion entre celdas que las lagunas de agua de los otros biomas.
                // El patio (Biome 3) conserva sus muros/ruinas y nunca recibe lava.
                BuildSunkenPool(floor, x, y, center, cellSize, lava: true);
                return;
            }

            if (floor.Biome == 3)
            {
                var ruin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ruin.name = "CastleSolidRuin";
                ruin.transform.SetParent(_root.transform, false);
                ruin.transform.position = blocker.transform.position;
                ruin.transform.localScale = new Vector3(cellSize, wallHeight, cellSize);
                ruin.isStatic = true;
                var ruinCollider = ruin.GetComponent<Collider>();
                if (ruinCollider != null) Destroy(ruinCollider);
                ruin.GetComponent<Renderer>().sharedMaterial = CastleMaterial(ref _castleWallMaterial, new Color(0.28f, 0.28f, 0.32f));
                return;
            }

            // Agua real (no lava, no ruina): candidato de vuelo para las mariposas/polillas (ver
            // BuildAmbientCritters) en los mismos biomas donde esas criaturas existen -- mismo
            // criterio que _wildlifeHabitatPoints.Add de los arbustos mas arriba, misma altura de
            // vuelo (up*0.65) para que no haya un salto brusco al pasar de un tipo de destino al otro.
            if (floor.Biome == 0 || floor.Biome == 2)
                _wildlifeWaterPoints.Add(center + Vector3.up * 0.65f);

            BuildSunkenPool(floor, x, y, center, cellSize, lava: false);
        }

        // Laguna chica hundida por debajo del nivel del piso (Y=0), visible desde el borde del
        // pasillo -- no una superficie de agua flotando en el aire ni una pared de agua. PitDepth
        // es lo que se hunde el fondo real; el agua queda a mitad de esa profundidad, asi el pozo
        // tiene borde de roca visible arriba y un fondo opaco que se ve a traves del agua.
        //
        // Pedido puntual: "si hay agua a algun costado de otra agua, que se junten" -- antes las 4
        // paredes finas del pozo se construian siempre, sin importar que hubiera del otro lado. Por
        // cada borde, si la celda vecina TAMBIEN es Void, se omite esa pared puntual (nada separa
        // los dos pozos ahi) y la superficie de agua se agranda a (casi) el tamano completo de la
        // celda de ese lado -- dos pozos Void contiguos quedan como una sola laguna continua en vez
        // de dos charcos separados por una franja de roca.
        private void BuildSunkenPool(DungeonFloor floor, int x, int y, Vector3 center, float cellSize, bool lava)
        {
            const float pitDepth = 1.0f;
            var rockFallback = lava ? new Color(0.055f, 0.035f, 0.03f) : new Color(0.12f, 0.11f, 0.1f);

            bool IsFused(Direction dir)
            {
                var (dx, dy) = dir.Offset();
                int nx = x + dx, ny = y + dy;
                return floor.InBounds(nx, ny) && floor.Cells[nx, ny].Type == CellType.Void;
            }

            bool fusedNorth = IsFused(Direction.North);
            bool fusedSouth = IsFused(Direction.South);
            bool fusedEast = IsFused(Direction.East);
            bool fusedWest = IsFused(Direction.West);

            // 4 paredes finas del pozo, una por borde de la celda, desde Y=0 (nivel del piso de
            // los vecinos) hasta el fondo -- sin esto el agua se veria flotando en un agujero sin
            // bordes en vez de un pozo real. Se salta la pared de cualquier borde fusionado.
            (float ox, float oz, bool fused)[] edges =
            {
                (0, 1, fusedNorth), (0, -1, fusedSouth), (1, 0, fusedEast), (-1, 0, fusedWest),
            };
            foreach (var (ox, oz, fused) in edges)
            {
                if (fused) continue;
                var wallGo = new GameObject("VoidPitRockRim");
                wallGo.transform.SetParent(_root.transform, false);
                wallGo.transform.position = center + new Vector3(ox, 0, oz) * (cellSize / 2f) + new Vector3(0, -pitDepth / 2f, 0);
                if (oz == 0) wallGo.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                wallGo.isStatic = true;
                var filter = wallGo.AddComponent<MeshFilter>();
                filter.sharedMesh = BuildPitRockRimMesh(cellSize, pitDepth, floor.Index, x, y, (int)ox, (int)oz);
                var renderer = wallGo.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = lava
                    ? CastlePaletteMaterial(new Color(0.095f, 0.07f, 0.06f))
                    : PitRockMaterial(rockFallback);
            }

            // Fondo opaco del pozo, un poco mas abajo del agua. Se ve a traves de la superficie;
            // el tamano completo hace que los fondos de pozos vecinos calcen borde a borde.
            var floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floorGo.name = "VoidPitFloor";
            floorGo.transform.SetParent(_root.transform, false);
            floorGo.transform.position = center + new Vector3(0, -pitDepth - 0.05f, 0);
            floorGo.transform.localScale = new Vector3(cellSize, 0.1f, cellSize);
            var floorCol = floorGo.GetComponent<Collider>();
            if (floorCol != null) Destroy(floorCol);
            floorGo.isStatic = true;
            ApplyMaterial(floorGo, null, rockFallback);

            // Superficie del agua (Custom/VoidWater), a mitad de la profundidad del pozo -- deja
            // borde de roca visible arriba (se nota que es un pozo hundido) y fondo opaco abajo.
            // 0.98 de margen en los bordes SIN fusionar (para no asomar contra la pared de roca de
            // ese lado); en los bordes fusionados va a (casi) el 100% para que la superficie de
            // este pozo casi toque la del vecino -- casi y no exacto a proposito: dos quads
            // coincidiendo en el mismo plano exacto parpadearian entre si (z-fighting), un margen
            // de menos de un milimetro es imperceptible pero evita ese problema.
            float marginX = (fusedEast || fusedWest) ? 0.999f : 0.98f;
            float marginZ = (fusedNorth || fusedSouth) ? 0.999f : 0.98f;
            var waterGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            waterGo.name = lava ? "CastleVoidLavaSurface" : "VoidWaterSurface";
            waterGo.transform.SetParent(_root.transform, false);
            waterGo.transform.position = center + new Vector3(0, -pitDepth * 0.5f, 0);
            waterGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            waterGo.transform.localScale = new Vector3(cellSize * marginX, cellSize * marginZ, 1f);
            waterGo.GetComponent<MeshFilter>().sharedMesh = GetWaterSurfaceMesh();
            var oldWaterCollider = waterGo.GetComponent<Collider>();
            if (oldWaterCollider != null) Destroy(oldWaterCollider);
            var waterCol = waterGo.AddComponent<BoxCollider>();
            waterCol.size = new Vector3(1f, 1f, 0.02f);
            // The project's Water layer isolates these colliders from ordinary physics queries;
            // falling leaves opt into this layer through their particle collision mask.
            int waterLayer = LayerMask.NameToLayer("Water");
            if (waterLayer >= 0) waterGo.layer = waterLayer;
            waterGo.AddComponent<WaterRippleSurface>();
            if (lava)
                ApplyMaterial(waterGo, CastleLavaMaterial(), new Color(0.95f, 0.19f, 0.015f));
            else
                ApplyMaterial(waterGo, voidBlockMaterial, new Color(0.1f, 0.35f, 0.32f));
        }

        private static Material PitRockMaterial(Color fallback)
        {
            if (_pitRockMaterial != null) return _pitRockMaterial;
            _pitRockMaterial = Resources.Load<Material>("Materials/PitRockEdge");
            if (_pitRockMaterial != null) return _pitRockMaterial;
            var shader = Shader.Find("Custom/PitRockEdge");
            _pitRockMaterial = new Material(shader != null ? shader : Shader.Find("Standard"));
            if (_pitRockMaterial.HasProperty("_Color")) _pitRockMaterial.SetColor("_Color", fallback);
            return _pitRockMaterial;
        }

        private static Mesh BuildPitRockRimMesh(float length, float height, int floor, int cellX, int cellY, int edgeX, int edgeY)
        {
            const int segments = 14;
            const float thickness = 0.12f;
            unchecked
            {
                int seed = 23;
                seed = seed * 31 + floor; seed = seed * 31 + cellX; seed = seed * 31 + cellY;
                seed = seed * 31 + edgeX; seed = seed * 31 + edgeY;
                var rng = new System.Random(seed);
                var vertices = new List<Vector3>(segments * 20);
                var uvs = new List<Vector2>(segments * 20);
                var colors = new List<Color>(segments * 20);
                var triangles = new List<int>(segments * 30);
                float previousTop = 0.5f + (float)(rng.NextDouble() * 0.28 - 0.14);
                float previousDepth = thickness * 0.5f;
                float edgeSeed = (float)rng.NextDouble();

                for (int i = 0; i < segments; i++)
                {
                    float u0 = i / (float)segments;
                    float u1 = (i + 1) / (float)segments;
                    float top = 0.5f + (float)(rng.NextDouble() * 0.28 - 0.14);
                    float depth = thickness * (0.38f + (float)rng.NextDouble() * 0.24f);
                    float x0 = (u0 - 0.5f) * length;
                    float x1 = (u1 - 0.5f) * length;

                    for (int side = -1; side <= 1; side += 2)
                    {
                        int b = vertices.Count;
                        float z0 = side * previousDepth;
                        float z1 = side * depth;
                        vertices.Add(new Vector3(x0, -0.5f, z0)); vertices.Add(new Vector3(x1, -0.5f, z1));
                        vertices.Add(new Vector3(x0, previousTop, z0)); vertices.Add(new Vector3(x1, top, z1));
                        uvs.Add(new Vector2(u0, 0f)); uvs.Add(new Vector2(u1, 0f));
                        uvs.Add(new Vector2(u0, 1f)); uvs.Add(new Vector2(u1, 1f));
                        for (int c = 0; c < 4; c++) colors.Add(new Color(edgeSeed, 0f, 0f, 1f));
                        triangles.Add(b); triangles.Add(b + 2); triangles.Add(b + 1);
                        triangles.Add(b + 1); triangles.Add(b + 2); triangles.Add(b + 3);
                    }

                    // Tapa superior dentada para que el corte de tierra no sea una línea recta.
                    int topBase = vertices.Count;
                    vertices.Add(new Vector3(x0, previousTop, -previousDepth));
                    vertices.Add(new Vector3(x1, top, -depth));
                    vertices.Add(new Vector3(x0, previousTop, previousDepth));
                    vertices.Add(new Vector3(x1, top, depth));
                    uvs.Add(new Vector2(u0, 1f)); uvs.Add(new Vector2(u1, 1f));
                    uvs.Add(new Vector2(u0, 1f)); uvs.Add(new Vector2(u1, 1f));
                    for (int c = 0; c < 4; c++) colors.Add(new Color(edgeSeed, 0f, 0f, 1f));
                    triangles.Add(topBase); triangles.Add(topBase + 1); triangles.Add(topBase + 2);
                    triangles.Add(topBase + 1); triangles.Add(topBase + 3); triangles.Add(topBase + 2);

                    previousTop = top;
                    previousDepth = depth;
                }

                var mesh = new Mesh { name = "JaggedRockPitRim" };
                mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }
        }

        // A normal Quad has only four vertices, so vertex-shader waves cannot move its surface.
        // Reuse a modest grid across all pool tiles; world-space wave phases keep neighboring tiles
        // aligned while the shader provides both rolling waves and per-leaf impact rings.
        private static Mesh GetWaterSurfaceMesh()
        {
            if (_waterSurfaceMesh != null) return _waterSurfaceMesh;

            const int divisions = 8;
            var vertices = new Vector3[(divisions + 1) * (divisions + 1)];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[divisions * divisions * 6];
            for (int z = 0; z <= divisions; z++)
            {
                for (int x = 0; x <= divisions; x++)
                {
                    int index = z * (divisions + 1) + x;
                    float u = x / (float)divisions;
                    float v = z / (float)divisions;
                    vertices[index] = new Vector3(u - 0.5f, v - 0.5f, 0f);
                    uv[index] = new Vector2(u, v);
                }
            }

            int triangle = 0;
            for (int z = 0; z < divisions; z++)
            {
                for (int x = 0; x < divisions; x++)
                {
                    int a = z * (divisions + 1) + x;
                    int b = a + 1;
                    int d = a + divisions + 1;
                    int c = d + 1;
                    triangles[triangle++] = a;
                    triangles[triangle++] = b;
                    triangles[triangle++] = c;
                    triangles[triangle++] = a;
                    triangles[triangle++] = c;
                    triangles[triangle++] = d;
                }
            }

            _waterSurfaceMesh = new Mesh { name = "SubdividedWaterSurface" };
            _waterSurfaceMesh.vertices = vertices;
            _waterSurfaceMesh.uv = uv;
            _waterSurfaceMesh.triangles = triangles;
            _waterSurfaceMesh.RecalculateNormals();
            _waterSurfaceMesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, 1f, 0.6f));
            return _waterSurfaceMesh;
        }

        public Vector3 CellCenter(int x, int y, float cellSize) => new Vector3(x * cellSize, 0f, y * cellSize);

        private GroundTileKind? BuildFloorTile(Vector3 center, float cellSize, bool isIso, bool isBiome2, bool isCastleBiome, int floorIndex, int cellX, int cellY, bool forceCleanFloor = false, int castleRegion = -1, bool isCastleExterior = false)
        {
            if (isCastleBiome)
            {
                BuildCastleFloorTile(center, cellSize, floorIndex, cellX, cellY, castleRegion, isCastleExterior, forceCleanFloor);
                return null;
            }
            if (isBiome2)
            {
                var goBiome2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                goBiome2.name = "Floor";
                goBiome2.transform.SetParent(_root.transform, false);
                goBiome2.transform.position = center + new Vector3(0, -0.1f, 0);
                goBiome2.transform.localScale = new Vector3(cellSize, 0.2f, cellSize);
                ApplyMaterial(goBiome2, biome2FloorMaterial, new Color(0.05f, 0.05f, 0.08f));
                return null;
            }

            if (isIso)
            {
                // Zona aislada = entrada de cueva (pedido puntual: ya no tinte morado parejo,
                // piso rocoso) -- ver BuildRockyFloorTile.
                BuildRockyFloorTile(center, cellSize);
                return null;
            }

            // Zona normal del bosque: en vez de un cubo chato de un solo color, el tilemap
            // variado de GroundTileFactory.
            if (useForestGroundTiles)
            {
                // forceCleanFloor (escaleras): Grass a proposito, sin props -- nunca una roca u
                // otro prop saliendo debajo del icono pseudo-3D de la escalera.
                var forcedKind = forceCleanFloor ? GroundTileKind.Grass : (GroundTileKind?)null;
                return GroundTileFactory.BuildTile(_root.transform, center, cellSize, groundTileMaterial, floorIndex, cellX, cellY, forcedKind);
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Floor";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, -0.1f, 0);
            go.transform.localScale = new Vector3(cellSize, 0.2f, cellSize);
            ApplyMaterial(go, floorMaterial, new Color(0.35f, 0.35f, 0.38f));
            return null;
        }

        private void BuildCastleFloorTile(Vector3 center, float cellSize, int floorIndex, int cellX, int cellY, int castleRegion, bool isExterior, bool forceCleanFloor)
        {
            var baseTile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseTile.name = "CastleStoneFloor";
            baseTile.transform.SetParent(_root.transform, false);
            baseTile.transform.position = center + Vector3.down * 0.1f;
            baseTile.transform.localScale = new Vector3(cellSize, 0.2f, cellSize);
            baseTile.isStatic = true;
            int gardenPattern = Mathf.Abs(cellX * 92821 + cellY * 68917 + floorIndex * 31337 + castleRegion * 997);
            bool gardenBed = isExterior && !forceCleanFloor && gardenPattern % 7 < 3;
            Color stoneTint = isExterior
                ? castleRegion switch
                {
                    0 => gardenBed ? new Color(0.2f, 0.28f, 0.16f) : new Color(0.43f, 0.41f, 0.33f),
                    1 => gardenBed ? new Color(0.18f, 0.27f, 0.17f) : new Color(0.37f, 0.39f, 0.34f),
                    _ => gardenBed ? new Color(0.2f, 0.29f, 0.18f) : new Color(0.33f, 0.35f, 0.3f)
                }
                : castleRegion switch
                {
                    3 => new Color(0.19f, 0.18f, 0.18f),
                    4 => new Color(0.12f, 0.14f, 0.18f),
                    _ => new Color(0.25f, 0.26f, 0.29f)
                };
            baseTile.GetComponent<Renderer>().sharedMaterial = CastlePaletteMaterial(stoneTint);

            // El patio mezcla caminos de piedra con bancales naturales. El follaje usa el mismo
            // instancing GPU que el bosque: mucha densidad visible, pocos objetos y sin colision.
            if (gardenBed)
            {
                var foliage = FoliageManager.Instance;
                Material foliageMaterial = GroundTileFactory.GrassMaterial();
                foliage.AddGrassTufts(center, cellSize, floorIndex, cellX, cellY, 71, 14, 23, foliageMaterial);
                foliage.AddSwordGrass(center, cellSize, floorIndex, cellX, cellY, 73, 7, 12, foliageMaterial);
                if (gardenPattern % 3 == 0)
                    foliage.AddFlowers(center, cellSize, floorIndex, cellX, cellY, foliageMaterial, guaranteedPatch: true);
                if (gardenPattern % 11 == 0)
                    BuildGardenShrub(center, cellSize, gardenPattern);
            }

            // Baldosas de mármol y una alfombra bordó recurrente hacen que los corredores se lean
            // como interiores nobles, sin texturas nuevas ni un material por celda.
            if (!isExterior && (cellX + cellY + floorIndex) % 5 == 0)
            {
                var runner = GameObject.CreatePrimitive(PrimitiveType.Cube);
                runner.name = "CastleRug";
                runner.transform.SetParent(_root.transform, false);
                runner.transform.position = center + Vector3.up * 0.012f;
                runner.transform.localScale = new Vector3(cellSize * 0.36f, 0.025f, cellSize * 0.88f);
                runner.isStatic = true;
                var collider = runner.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                runner.GetComponent<Renderer>().sharedMaterial = CastleMaterial(ref _castleRugMaterial, new Color(0.32f, 0.055f, 0.075f));
            }
        }

        private void BuildGardenShrub(Vector3 center, float cellSize, int seed)
        {
            var rng = new System.Random(seed);
            int count = 2 + rng.Next(3);
            for (int i = 0; i < count; i++)
            {
                var shrub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                shrub.name = "CourtyardGardenShrub";
                shrub.transform.SetParent(_root.transform, false);
                float x = ((float)rng.NextDouble() - 0.5f) * cellSize * 0.34f;
                float z = ((float)rng.NextDouble() - 0.5f) * cellSize * 0.34f;
                float size = cellSize * (0.16f + (float)rng.NextDouble() * 0.1f);
                shrub.transform.position = center + new Vector3(x, size * 0.38f, z);
                shrub.transform.localScale = new Vector3(size * 1.25f, size * 0.8f, size);
                shrub.isStatic = true;
                var collider = shrub.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                float shade = 0.88f + rng.Next(3) * 0.12f;
                shrub.GetComponent<Renderer>().sharedMaterial = CastlePaletteMaterial(new Color(0.12f * shade, 0.26f * shade, 0.1f * shade));
            }
        }

        // Zona aislada = "entrada de cueva" (pedido puntual: antes tenia un tinte morado parejo,
        // ahora piso rocoso gris/marron): base chata + un par de piedras chatas encima, mismo
        // lenguaje visual que GroundTileFactory.BuildGroundBumps pero en tonos de roca.
        private void BuildRockyFloorTile(Vector3 center, float cellSize)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Floor";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, -0.1f, 0);
            go.transform.localScale = new Vector3(cellSize, 0.2f, cellSize);
            go.isStatic = true;
            int basePalette = Random.Range(0, CaveRockPalette.Length);
            SetRockMaterial(go, isoFloorMaterial, basePalette);

            // Cinco siluetas de suelo rocoso: losas, cascajo, guijarros, piedra partida y roca
            // expuesta. Cambian cantidad, escala, giro y tono sin añadir texturas ni materiales.
            int variant = Random.Range(0, 5);
            int count = variant == 0 ? Random.Range(1, 3) : variant == 2 ? Random.Range(4, 7) : Random.Range(2, 5);
            for (int i = 0; i < count; i++)
            {
                bool pebble = variant == 2 || (variant == 1 && Random.value < 0.35f);
                var rock = GameObject.CreatePrimitive(pebble ? PrimitiveType.Sphere : PrimitiveType.Cube);
                rock.name = "CaveRock";
                rock.transform.SetParent(_root.transform, false);
                Vector2 off = Random.insideUnitCircle * cellSize * (variant == 0 ? 0.3f : 0.36f);
                float sizeX = cellSize * Random.Range(0.09f, variant == 0 ? 0.3f : 0.22f);
                float sizeZ = sizeX * Random.Range(0.65f, 1.3f);
                float sizeY = sizeX * (variant == 0 ? Random.Range(0.18f, 0.32f) : Random.Range(0.3f, 0.72f));
                rock.transform.position = center + new Vector3(off.x, sizeY * 0.42f, off.y);
                rock.transform.localScale = new Vector3(sizeX, sizeY, sizeZ);
                rock.transform.rotation = Quaternion.Euler(Random.Range(-10f, 18f), Random.Range(0f, 180f), Random.Range(-14f, 14f));
                rock.isStatic = true;

                var col = rock.GetComponent<Collider>();
                if (col != null) Destroy(col);

                int palette = variant == 3 ? 3 : Random.Range(0, CaveRockPalette.Length);
                SetRockMaterial(rock, isoWallMaterial, palette);
            }
        }

        private static void SetRockMaterial(GameObject go, Material material, int paletteIndex)
        {
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material != null ? material : CaveRockMaterial(paletteIndex);
        }

        private static Material CaveRockMaterial(int paletteIndex)
        {
            if (_caveRockPaletteMaterials == null)
            {
                _caveRockPaletteMaterials = new Material[CaveRockPalette.Length];
                for (int i = 0; i < _caveRockPaletteMaterials.Length; i++)
                {
                    var material = new Material(FallbackStandardMaterial());
                    material.color = CaveRockPalette[i];
                    _caveRockPaletteMaterials[i] = material;
                }
            }
            return _caveRockPaletteMaterials[Mathf.Abs(paletteIndex) % _caveRockPaletteMaterials.Length];
        }

        // Telarana en una esquina de la cueva (pedido puntual, ver foto de referencia): un abanico
        // de hebras que radian desde un punto de anclaje arriba (donde se juntarian pared y techo)
        // mas 2 "anillos" que las cruzan a distintas fracciones del radio -- el mismo patron
        // radial+concentrico de una telarana real, aproximado con primitivas finitas (hebras =
        // cubos finitos y largos, no hay curvas).
        // big=true: telarana grande (pedido puntual, ver foto de referencia -- las de antes eran
        // muy chicas comparado con la imagen), radio y cantidad de hebras notablemente mayores,
        // pero MISMO patron radial+concentrico.
        private void BuildCobweb(DungeonCell cell, Vector3 center, float cellSize, float wallHeight, bool big)
        {
            var corners = new List<Vector2>(4);
            if (cell.HasWall(Direction.West) && cell.HasWall(Direction.South)) corners.Add(new Vector2(-1f, -1f));
            if (cell.HasWall(Direction.West) && cell.HasWall(Direction.North)) corners.Add(new Vector2(-1f, 1f));
            if (cell.HasWall(Direction.East) && cell.HasWall(Direction.South)) corners.Add(new Vector2(1f, -1f));
            if (cell.HasWall(Direction.East) && cell.HasWall(Direction.North)) corners.Add(new Vector2(1f, 1f));
            if (corners.Count == 0) return; // sin dos paredes que se junten, no hay esquina donde anclarla

            Vector2 corner = corners[Random.Range(0, corners.Count)];
            float cornerSignX = corner.x;
            float cornerSignZ = corner.y;
            float inset = Mathf.Max(0.04f, cellSize * 0.025f);
            Vector3 anchor = center + new Vector3(
                cornerSignX * (cellSize * 0.5f - inset),
                wallHeight - inset,
                cornerSignZ * (cellSize * 0.5f - inset));

            var root = new GameObject(big ? "CobwebBig" : "Cobweb");
            root.transform.SetParent(_root.transform, false);
            root.transform.position = anchor;

            int spokes = big ? Random.Range(8, 11) : Random.Range(5, 7);
            float spanAngle = big ? 150f : 110f;
            // Pedido puntual: "las telaranas estan para afuera" -- baseAngle era 100% al azar, sin
            // relacion con la esquina elegida arriba, asi que a veces el abanico apuntaba HACIA la
            // pared/esquina (hacia afuera del cuarto) en vez de hacia el cuarto. towardRoomAngle es
            // la direccion que se aleja de la esquina hacia el centro de la celda -- el abanico
            // ahora siempre arranca centrado ahi (+-25 grados de variacion, no queda identica cada
            // vez pero nunca aparece mirando para el lado equivocado).
            float towardRoomAngle = Mathf.Atan2(-cornerSignX, -cornerSignZ) * Mathf.Rad2Deg;
            float baseAngle = towardRoomAngle + Random.Range(-25f, 25f);
            float radius = big ? cellSize * Random.Range(0.75f, 0.95f) : cellSize * Random.Range(0.35f, 0.52f);
            float strandThickness = big ? 0.03f : 0.015f;

            var tips = new Vector3[spokes];
            for (int i = 0; i < spokes; i++)
            {
                float t = spokes <= 1 ? 0.5f : (float)i / (spokes - 1);
                float angle = baseAngle + (t - 0.5f) * spanAngle;
                float downAngle = Mathf.Lerp(24f, 68f, t);
                float yaw = angle * Mathf.Deg2Rad;
                float down = downAngle * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(down), -Mathf.Sin(down),
                    Mathf.Cos(yaw) * Mathf.Cos(down));
                tips[i] = dir * radius;
                BuildCobwebStrand(root.transform, Vector3.zero, tips[i], strandThickness);
            }

            // Anillos: conectan hebras vecinas a distintas fracciones del radio, como los hilos
            // concentricos de una telarana real -- una grande lleva un anillo mas que una chica.
            int rings = big ? 3 : 2;
            for (int ring = 0; ring < rings; ring++)
            {
                float frac = (ring + 1f) / (rings + 1f);
                for (int i = 0; i < spokes - 1; i++)
                    BuildCobwebStrand(root.transform, tips[i] * frac, tips[i + 1] * frac, strandThickness);
            }
        }

        private void BuildCobwebStrand(Transform parent, Vector3 a, Vector3 b, float thickness)
        {
            var strand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strand.name = "CobwebStrand";
            strand.transform.SetParent(parent, false);
            Vector3 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.0001f) return;
            strand.transform.localPosition = (a + b) * 0.5f;
            strand.transform.localRotation = Quaternion.LookRotation(delta);
            strand.transform.localScale = new Vector3(thickness, thickness, length);

            var col = strand.GetComponent<Collider>();
            if (col != null) Destroy(col);

            if (_cobwebMaterial == null)
            {
                _cobwebMaterial = new Material(FallbackStandardMaterial());
                _cobwebMaterial.color = new Color(0.82f, 0.8f, 0.75f);
            }
            strand.GetComponent<Renderer>().sharedMaterial = _cobwebMaterial;
            DisableShadows(strand.GetComponent<Renderer>());
        }

        // Estalactitas colgando del techo de la cueva (pedido puntual, ver foto de referencia):
        // 3 segmentos apilados que se van achicando hacia la punta -- mismo truco chato de PS1
        // que el resto (primitivas apiladas, no una malla conica de verdad).
        private void BuildStalactite(Vector3 center, float wallHeight)
        {
            float length = Random.Range(0.5f, 1.1f);
            const int segments = 3;
            Vector2 off = Random.insideUnitCircle * 0.9f; // dentro de la celda, lejos de las paredes
            Vector3 basePos = center + new Vector3(off.x, wallHeight - 0.02f, off.y);
            float segLen = length / segments;
            float widthTop = Random.Range(0.16f, 0.24f);

            for (int i = 0; i < segments; i++)
            {
                float t = segments <= 1 ? 0f : (float)i / (segments - 1); // 0 arriba, 1 en la punta
                float width = Mathf.Lerp(widthTop, widthTop * 0.15f, t);
                var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seg.name = "Stalactite";
                seg.transform.SetParent(_root.transform, false);
                float y = basePos.y - segLen * (i + 0.5f);
                seg.transform.position = new Vector3(basePos.x, y, basePos.z);
                seg.transform.localScale = new Vector3(width, segLen * 1.05f, width);

                var col = seg.GetComponent<Collider>();
                if (col != null) Destroy(col);

                SetRockMaterial(seg, isoWallMaterial, Random.Range(0, CaveRockPalette.Length));
            }
        }

        // Rama de arbol colgando del techo del bosque (pedido puntual: "agrega algo que simule
        // ramificacion de arboles que parezca un bosque frondoso" -- un shader sobre una superficie
        // CHATA nunca deja de sentirse chato, esto le agrega volumen de verdad). Un tronquito corto
        // bajando del techo, 2-3 ramas que se abren desde la punta en angulos/yaws distintos
        // (apuntando para abajo y afuera, nunca rectas hacia abajo), y bultos de hojas (esferas
        // achatadas, mismo truco barato que GroundTileFactory.BuildGroundBumps) agarrados cerca de
        // cada punta de rama -- eso es lo que se lee como "denso"/frondoso, no el tronco en si.
        private void BuildCanopyBranch(Vector3 center, float cellSize, float wallHeight)
        {
            Vector2 off = Random.insideUnitCircle * cellSize * 0.3f;
            Vector3 trunkTop = center + new Vector3(off.x, wallHeight, off.y);
            float trunkLen = Random.Range(0.3f, 0.55f);
            float trunkWidth = Random.Range(0.08f, 0.13f);

            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trunk.name = "CanopyTrunk";
            trunk.transform.SetParent(_root.transform, false);
            trunk.transform.position = trunkTop - new Vector3(0, trunkLen * 0.5f, 0);
            trunk.transform.localScale = new Vector3(trunkWidth, trunkLen, trunkWidth);
            var trunkCol = trunk.GetComponent<Collider>();
            if (trunkCol != null) Destroy(trunkCol);
            float trunkShade = Random.Range(0.8f, 1.2f);
            ApplyMaterial(trunk, null, new Color(0.16f * trunkShade, 0.1f * trunkShade, 0.06f * trunkShade));

            Vector3 branchOrigin = trunkTop - new Vector3(0, trunkLen, 0);
            int branchCount = Random.Range(2, 4);
            for (int b = 0; b < branchCount; b++)
            {
                float branchLen = Random.Range(0.35f, 0.65f);
                float yaw = Random.Range(0f, 360f);
                float pitchDown = Random.Range(35f, 65f); // para abajo/afuera, nunca rectas
                var rot = Quaternion.Euler(pitchDown, yaw, 0f);
                Vector3 dir = rot * Vector3.down;

                var branch = GameObject.CreatePrimitive(PrimitiveType.Cube);
                branch.name = "CanopyBranch";
                branch.transform.SetParent(_root.transform, false);
                branch.transform.position = branchOrigin + dir * (branchLen * 0.5f);
                branch.transform.rotation = rot;
                branch.transform.localScale = new Vector3(trunkWidth * 0.6f, branchLen, trunkWidth * 0.6f);
                var branchCol = branch.GetComponent<Collider>();
                if (branchCol != null) Destroy(branchCol);
                float branchShade = Random.Range(0.8f, 1.2f);
                ApplyMaterial(branch, null, new Color(0.16f * branchShade, 0.1f * branchShade, 0.06f * branchShade));

                Vector3 tip = branchOrigin + dir * branchLen;
                int leafCount = Random.Range(2, 4);
                for (int l = 0; l < leafCount; l++)
                {
                    Vector2 jitter = Random.insideUnitCircle * 0.15f;
                    var leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    leaf.name = "CanopyLeafClump";
                    leaf.transform.SetParent(_root.transform, false);
                    float radius = Random.Range(0.12f, 0.22f);
                    float squash = Random.Range(0.5f, 0.75f);
                    leaf.transform.position = tip + new Vector3(jitter.x, Random.Range(-0.05f, 0.1f), jitter.y);
                    leaf.transform.localScale = new Vector3(radius, radius * squash, radius);
                    var leafCol = leaf.GetComponent<Collider>();
                    if (leafCol != null) Destroy(leafCol);
                    float leafShade = Random.Range(0.75f, 1.25f);
                    ApplyMaterial(leaf, null, new Color(0.1f * leafShade, 0.22f * leafShade, 0.08f * leafShade));
                }
            }
        }

        // Antorcha de pared en el Bioma de Cuevas (pedido puntual: "que vayan iluminando el camino
        // a lo largo") -- soporte + llama (primitivas, mismo estilo que el resto) mas una Light de
        // verdad (sin sombras, alcance corto) para que ilumine el pasillo de verdad, no solo
        // decore. cellCenter/dir son la MISMA pared que ya construyo BuildWall justo antes -- se
        // clava del lado de ADENTRO de esa pared, mirando hacia el centro de la celda.
        private void TryBuildCaveTorch(Vector3 cellCenter, Direction dir, float cellSize, float wallHeight)
        {
            if (_caveTorchesBuilt >= MaxCaveTorchesPerFloor || Random.value >= 0.12f) return;
            BuildTorch(cellCenter, dir, cellSize, wallHeight);
            _caveTorchesBuilt++;
        }

        private void BuildTorch(Vector3 cellCenter, Direction dir, float cellSize, float wallHeight, bool flicker = false)
        {
            var (ox, oy) = dir.Offset();
            Vector3 wallPos = cellCenter + new Vector3(ox, 0, oy) * (cellSize / 2f);
            Vector3 pos = wallPos - new Vector3(ox, 0, oy) * 0.15f + new Vector3(0, wallHeight * 0.55f, 0);

            var root = new GameObject("Torch");
            root.transform.SetParent(_root.transform, false);
            root.transform.position = pos;

            var bracket = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bracket.name = "TorchBracket";
            bracket.transform.SetParent(root.transform, false);
            bracket.transform.localPosition = new Vector3(0, -0.05f, 0);
            bracket.transform.localScale = new Vector3(0.06f, 0.35f, 0.06f);
            var bracketCol = bracket.GetComponent<Collider>();
            if (bracketCol != null) Destroy(bracketCol);
            ApplyMaterial(bracket, null, new Color(0.2f, 0.13f, 0.08f));
            DisableShadows(bracket.GetComponent<Renderer>());

            EnsureTorchFlameAssets();
            var flame = new GameObject("TorchFlameOuter");
            flame.transform.SetParent(root.transform, false);
            flame.transform.localPosition = new Vector3(0, 0.19f, 0);
            flame.transform.localScale = new Vector3(0.22f, 0.62f, 0.22f);
            flame.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            flame.AddComponent<MeshFilter>().sharedMesh = _torchFlameMesh;
            var outerRenderer = flame.AddComponent<MeshRenderer>();
            outerRenderer.sharedMaterial = _torchFlameMaterial;
            DisableShadows(outerRenderer);

            var core = new GameObject("TorchFlameCore");
            core.transform.SetParent(root.transform, false);
            core.transform.localPosition = new Vector3(0f, 0.25f, -0.005f);
            core.transform.localScale = new Vector3(0.1f, 0.37f, 0.1f);
            core.transform.localRotation = flame.transform.localRotation;
            core.AddComponent<MeshFilter>().sharedMesh = _torchFlameMesh;
            var coreRenderer = core.AddComponent<MeshRenderer>();
            coreRenderer.sharedMaterial = _torchCoreMaterial;
            DisableShadows(coreRenderer);

            var lightGo = new GameObject("TorchLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0, 0.2f, 0);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.67f, 0.38f);
            light.intensity = 2.2f;
            light.range = cellSize * 2.1f;
            light.shadows = LightShadows.None; // muchas antorchas por piso -- sombras las harian carisimas
            light.renderMode = LightRenderMode.ForcePixel;
            if (flicker)
                root.AddComponent<TorchFlameFlicker>().Initialize(flame.transform, core.transform, light);
        }

        // Candelabro de techo: una cadena corta, brazo con tres velas y una sola luz sin sombras.
        // Su cantidad esta limitada por piso desde el loop principal de Build().
        private void BuildCeilingCandelabra(Vector3 cellCenter, float cellSize, float wallHeight)
        {
            var root = new GameObject("CeilingCandelabra");
            root.transform.SetParent(_root.transform, false);
            root.transform.position = cellCenter + Vector3.up * (wallHeight - 0.12f);
            root.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            var woodMaterial = CastlePaletteMaterial(new Color(0.22f, 0.14f, 0.08f));
            const float hangLength = 0.38f;
            var chain = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            chain.name = "CandelabraChain";
            chain.transform.SetParent(root.transform, false);
            chain.transform.localPosition = new Vector3(0f, -hangLength * 0.5f, 0f);
            chain.transform.localScale = new Vector3(0.025f, hangLength * 0.5f, 0.025f);
            var chainCollider = chain.GetComponent<Collider>();
            if (chainCollider != null) Destroy(chainCollider);
            chain.GetComponent<Renderer>().sharedMaterial = woodMaterial;
            DisableShadows(chain.GetComponent<Renderer>());

            var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arm.name = "CandelabraArm";
            arm.transform.SetParent(root.transform, false);
            arm.transform.localPosition = new Vector3(0f, -hangLength, 0f);
            arm.transform.localScale = new Vector3(0.5f, 0.05f, 0.12f);
            var armCol = arm.GetComponent<Collider>();
            if (armCol != null) Destroy(armCol);
            arm.GetComponent<Renderer>().sharedMaterial = woodMaterial;
            DisableShadows(arm.GetComponent<Renderer>());

            EnsureTorchFlameAssets();
            float[] armPositions = { -0.19f, 0f, 0.19f };
            foreach (float armX in armPositions)
            {
                var flame = new GameObject("CandleFlame");
                flame.transform.SetParent(root.transform, false);
                flame.transform.localPosition = new Vector3(armX, -hangLength + 0.035f, 0f);
                flame.transform.localScale = new Vector3(0.065f, 0.12f, 0.065f);
                flame.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                flame.AddComponent<MeshFilter>().sharedMesh = _torchFlameMesh;
                var flameRenderer = flame.AddComponent<MeshRenderer>();
                flameRenderer.sharedMaterial = _torchFlameMaterial;
                DisableShadows(flameRenderer);
            }

            var lightGo = new GameObject("CandelabraLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, -hangLength + 0.16f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.62f, 0.31f);
            light.intensity = 0.85f;
            light.range = cellSize * 1.25f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        // Tumba del Bioma de Cuevas (pedido puntual, ver DungeonGenerator.AddTombs): sarcofago de
        // piedra alargado, orientado hacia su unica conexion real para que visualmente "invada" el
        // pasillo de acceso (la mitad decorativa cae sobre la celda vecina, marcada IsTombDecor por
        // el generador -- esta funcion no necesita tocarla, alcanza con que el prop sea lo bastante
        // largo). Asi se lee ocupando 2 celdas sin tocar la conectividad real del laberinto. 70%
        // tesoro / 30% combate contra goblins al interactuar, ver Gameplay/DungeonManager.TryInteract.
        private void BuildTombDecor(DungeonCell cell, Vector3 center, float cellSize, float wallHeight)
        {
            var approachDir = cell.FirstOpenDirection();
            var (ox, oy) = approachDir.Offset();
            Vector3 axis = new Vector3(ox, 0f, oy);
            if (axis.sqrMagnitude < 0.01f) axis = Vector3.forward;

            var root = new GameObject("Tomb");
            root.transform.SetParent(_root.transform, false);
            // Centrado a mitad de camino entre esta celda y la vecina decorativa, para que el
            // sarcofago quede realmente "entre las dos", no encajonado en una sola.
            root.transform.position = center + axis * (cellSize * 0.5f);
            root.transform.rotation = Quaternion.LookRotation(axis, Vector3.up);

            var stoneColor = new Color(0.32f, 0.31f, 0.3f);
            float length = cellSize * 1.5f;

            var basePiece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            basePiece.name = "TombBase";
            basePiece.transform.SetParent(root.transform, false);
            basePiece.transform.localPosition = new Vector3(0, cellSize * 0.16f, 0);
            basePiece.transform.localScale = new Vector3(cellSize * 0.62f, cellSize * 0.32f, length);
            var baseCol = basePiece.GetComponent<Collider>();
            if (baseCol != null) Destroy(baseCol);
            ApplyMaterial(basePiece, null, stoneColor);
            DisableShadows(basePiece.GetComponent<Renderer>());

            var lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lid.name = "TombLid";
            lid.transform.SetParent(root.transform, false);
            lid.transform.localPosition = new Vector3(0, cellSize * 0.34f, 0);
            lid.transform.localScale = new Vector3(cellSize * 0.7f, cellSize * 0.06f, length * 1.04f);
            var lidCol = lid.GetComponent<Collider>();
            if (lidCol != null) Destroy(lidCol);
            ApplyMaterial(lid, null, new Color(0.4f, 0.39f, 0.37f));
            DisableShadows(lid.GetComponent<Renderer>());

            // Grieta/relieve tallado sobre la tapa (mismo truco que las vetas de madera de los
            // carteles, ver BuildWoodGrain): una franja fina y oscura superpuesta, para que se lea
            // como piedra labrada en vez de un bloque liso.
            var carving = GameObject.CreatePrimitive(PrimitiveType.Cube);
            carving.name = "TombCarving";
            carving.transform.SetParent(root.transform, false);
            carving.transform.localPosition = new Vector3(0, cellSize * 0.375f, 0);
            carving.transform.localScale = new Vector3(cellSize * 0.12f, 0.012f, length * 0.85f);
            var carvingCol = carving.GetComponent<Collider>();
            if (carvingCol != null) Destroy(carvingCol);
            ApplyMaterial(carving, null, new Color(0.18f, 0.17f, 0.16f));
            DisableShadows(carving.GetComponent<Renderer>());
        }

        private static void EnsureTorchFlameAssets()
        {
            if (_torchFlameMesh != null) return;
            // Malla de llama baja en poligonos, angosta en la base y terminada en una punta
            // desplazada; la capa amarilla interior separa el fuego de una esfera naranja.
            var vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(-0.32f, 0.48f, -0.32f), new Vector3(0.38f, 0.52f, -0.3f),
                new Vector3(0.31f, 0.45f, 0.34f), new Vector3(-0.38f, 0.5f, 0.3f),
                new Vector3(0.12f, 1f, 0.04f),
            };
            var triangles = new List<int>(48);
            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                triangles.Add(i); triangles.Add(next); triangles.Add(i + 4);
                triangles.Add(next); triangles.Add(next + 4); triangles.Add(i + 4);
                triangles.Add(i + 4); triangles.Add(next + 4); triangles.Add(8);
            }
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int swap = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = swap;
            }
            _torchFlameMesh = new Mesh { name = "LowPolyTorchFlame" };
            _torchFlameMesh.vertices = vertices;
            _torchFlameMesh.triangles = triangles.ToArray();
            _torchFlameMesh.RecalculateNormals();
            _torchFlameMesh.RecalculateBounds();

            Shader shader = Shader.Find("Standard");
            _torchFlameMaterial = new Material(shader) { color = new Color(1f, 0.24f, 0.035f) };
            _torchCoreMaterial = new Material(shader) { color = new Color(1f, 0.78f, 0.25f) };
            EnableTorchEmission(_torchFlameMaterial, new Color(1f, 0.13f, 0.015f));
            EnableTorchEmission(_torchCoreMaterial, new Color(1f, 0.52f, 0.08f));
        }

        private static void EnableTorchEmission(Material material, Color emission)
        {
            if (!material.HasProperty("_EmissionColor")) return;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
        }

        private static void DisableShadows(Renderer renderer)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void BuildCaveEntranceTorches(DungeonFloor floor, int x, int y, Vector3 center, float cellSize)
        {
            var cell = floor.Cells[x, y];
            foreach (var opening in DirectionExtensions.All)
            {
                var (ox, oy) = opening.Offset();
                int nx = x + ox;
                int ny = y + oy;
                if (!floor.InBounds(nx, ny) || floor.Cells[nx, ny].Type == CellType.Void) continue;
                if (floor.Cells[nx, ny].IsIsolatedZone || cell.HasWall(opening) || floor.Cells[nx, ny].HasWall(opening.Opposite())) continue;

                // El acceso se ilumina desde adentro de la cueva, con una antorcha a cada lado.
                Direction sideA = opening == Direction.North || opening == Direction.South ? Direction.East : Direction.North;
                Direction sideB = sideA.Opposite();
                Vector3 outsideCenter = CellCenter(nx, ny, cellSize);
                var outside = floor.Cells[nx, ny];
                if (outside.HasWall(sideA)) _pendingFlickerTorches.Add((outsideCenter, sideA));
                else if (cell.HasWall(sideA)) _pendingFlickerTorches.Add((center, sideA));
                if (outside.HasWall(sideB)) _pendingFlickerTorches.Add((outsideCenter, sideB));
                else if (cell.HasWall(sideB)) _pendingFlickerTorches.Add((center, sideB));
                break;
            }
        }

        // Mismo criterio de "umbral" que BuildCaveEntranceTorches de arriba (primer vecino NO
        // aislado, sin pared entre medio) pero para plantar UN cartel (no dos: aca solo hace falta
        // un tip, no un par de textos distintos como BuildSpawnSignposts/BuildCaveEntranceSignposts)
        // justo en la celda de la zona aislada que da a ese umbral, mirando hacia afuera -- de cara
        // al jugador que viene entrando desde el resto del piso 0. Devuelve true apenas encuentra y
        // construye el cartel, para que el llamador (ver Build) corte la busqueda en las demas
        // celdas de la zona aislada.
        private bool BuildMiniCaveEntranceSignpost(DungeonFloor floor, int x, int y, Vector3 center, float cellSize)
        {
            var cell = floor.Cells[x, y];
            foreach (var opening in DirectionExtensions.All)
            {
                var (ox, oy) = opening.Offset();
                int nx = x + ox;
                int ny = y + oy;
                if (!floor.InBounds(nx, ny) || floor.Cells[nx, ny].Type == CellType.Void) continue;
                if (floor.Cells[nx, ny].IsIsolatedZone || cell.HasWall(opening) || floor.Cells[nx, ny].HasWall(opening.Opposite())) continue;

                Direction facing = opening.Opposite();
                var (fx, fy) = facing.Offset();
                Vector3 side = new Vector3(-fy, 0, fx) * cellSize * 0.28f;
                BuildSignpost(center - side, "R\nANTORCHA", DirectionYaw(facing));
                return true;
            }
            return false;
        }

        private void BuildCastleMice(DungeonFloor floor, float cellSize)
        {
            // TEMPORAL (ver DungeonLevelBuilder.BuildAmbientCritters, mismo diagnostico): sacar
            // despues de confirmar la causa de "no veo los ratones".
            Debug.Log($"[WildlifeDebug] BuildCastleMice piso {floor.Index} CastleRegion={floor.CastleRegion}");
            if (floor.CastleRegion != 0) return;
            var go = new GameObject("CastleMiceScurryingToCellar");
            go.transform.SetParent(_root.transform, false);
            go.AddComponent<CastleMouseAmbient>().Initialize(floor, cellSize);
        }

        private void BuildCastleRoomDressing(DungeonFloor floor, float cellSize, float wallHeight)
        {
            var roomCenters = new Dictionary<string, (Vector3 sum, int count)>();
            for (int x = 0; x < floor.Width; x++)
            for (int y = 0; y < floor.Height; y++)
            {
                var cell = floor.Cells[x, y];
                string name = cell.PredefinedRoomTemplateName;
                if (!cell.IsPredefinedRoom || string.IsNullOrEmpty(name) || !name.StartsWith("Castle")) continue;
                if (roomCenters.TryGetValue(name, out var current))
                    roomCenters[name] = (current.sum + CellCenter(x, y, cellSize), current.count + 1);
                else
                    roomCenters.Add(name, (CellCenter(x, y, cellSize), 1));
            }

            foreach (var entry in roomCenters)
            {
                Vector3 center = entry.Value.sum / entry.Value.count;
                string name = entry.Key;
                // Deja libre la celda central del tell para que el mobiliario no esconda una trampa.
                if (name.Contains("Trap")) center += Vector3.right * cellSize * 0.56f;
                if (name.Contains("Dining"))
                {
                    BuildCastleFurniture(center, new Vector3(cellSize * 0.56f, 0.11f, cellSize * 0.42f), new Color(0.28f, 0.13f, 0.065f), "DiningTable");
                    BuildCastleFurniture(center + new Vector3(0f, cellSize * 0.075f, 0f), new Vector3(cellSize * 0.48f, 0.045f, cellSize * 0.34f), new Color(0.42f, 0.25f, 0.12f), "DiningTableTop");
                    BuildCastleFurniture(center + new Vector3(0f, 0.09f, cellSize * 0.31f), new Vector3(cellSize * 0.68f, 0.09f, cellSize * 0.09f), new Color(0.24f, 0.12f, 0.065f), "DiningBench");
                    BuildCastleFurniture(center + new Vector3(0f, 0.09f, -cellSize * 0.31f), new Vector3(cellSize * 0.68f, 0.09f, cellSize * 0.09f), new Color(0.24f, 0.12f, 0.065f), "DiningBench");
                }
                else if (name.Contains("Kitchen"))
                {
                    BuildCastleFurniture(center + new Vector3(-cellSize * 0.2f, 0.11f, 0f), new Vector3(cellSize * 0.18f, 0.22f, cellSize * 0.62f), new Color(0.35f, 0.28f, 0.2f), "KitchenCounter");
                    BuildCastleFurniture(center + new Vector3(cellSize * 0.2f, 0.1f, 0f), new Vector3(cellSize * 0.18f, 0.2f, cellSize * 0.62f), new Color(0.34f, 0.27f, 0.19f), "KitchenCounter");
                    BuildCastleFurniture(center + new Vector3(-cellSize * 0.2f, 0.23f, 0f), new Vector3(cellSize * 0.19f, 0.035f, cellSize * 0.64f), new Color(0.19f, 0.2f, 0.2f), "KitchenHob");
                }
                else if (name.Contains("Pantry"))
                {
                    for (int shelf = 0; shelf < 3; shelf++)
                        BuildCastleFurniture(center + new Vector3(0f, 0.1f + shelf * 0.14f, 0f), new Vector3(cellSize * 0.66f, 0.035f, cellSize * 0.16f), new Color(0.31f, 0.19f, 0.095f), "PantryShelf");
                    BuildCastleFurniture(center + new Vector3(-cellSize * 0.31f, 0.21f, 0f), new Vector3(0.06f, 0.38f, cellSize * 0.2f), new Color(0.26f, 0.15f, 0.075f), "PantryShelfSide");
                    BuildCastleFurniture(center + new Vector3(cellSize * 0.31f, 0.21f, 0f), new Vector3(0.06f, 0.38f, cellSize * 0.2f), new Color(0.26f, 0.15f, 0.075f), "PantryShelfSide");
                }
                else if (name == "CastleLavafall")
                {
                    BuildCastleLavafall(center, cellSize, wallHeight);
                }
            }
        }

        private void BuildCastleLavafall(Vector3 center, float cellSize, float wallHeight)
        {
            var basin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            basin.name = "CastleLavafallBasaltBasin";
            basin.transform.SetParent(_root.transform, false);
            basin.transform.position = center + Vector3.up * 0.015f;
            basin.transform.localScale = new Vector3(cellSize * 0.78f, 0.08f, cellSize * 0.42f);
            basin.isStatic = true;
            var basinCollider = basin.GetComponent<Collider>();
            if (basinCollider != null) Destroy(basinCollider);
            basin.GetComponent<Renderer>().sharedMaterial = CastlePaletteMaterial(new Color(0.075f, 0.055f, 0.05f));

            var pool = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pool.name = "CastleLavaSurface";
            pool.transform.SetParent(_root.transform, false);
            pool.transform.position = center + Vector3.up * 0.065f;
            pool.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            pool.transform.localScale = new Vector3(cellSize * 0.72f, cellSize * 0.37f, 1f);
            pool.GetComponent<MeshFilter>().sharedMesh = GetWaterSurfaceMesh();
            var poolCollider = pool.GetComponent<Collider>();
            if (poolCollider != null) Destroy(poolCollider);
            var trigger = pool.AddComponent<BoxCollider>();
            trigger.size = new Vector3(1f, 1f, 0.02f);
            int waterLayer = LayerMask.NameToLayer("Water");
            if (waterLayer >= 0) pool.layer = waterLayer;
            pool.AddComponent<WaterRippleSurface>();
            ApplyMaterial(pool, CastleLavaMaterial(), new Color(0.95f, 0.19f, 0.015f));

            // Cortina corta que alimenta la cuenca. Se mantiene centrada y transitable; no tapa
            // ninguna de las cuatro salidas de la sala.
            var fall = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fall.name = "CastleLavafallCurtain";
            fall.transform.SetParent(_root.transform, false);
            var fallCollider = fall.GetComponent<Collider>();
            if (fallCollider != null) Destroy(fallCollider);
            fall.transform.position = center + new Vector3(0f, wallHeight * 0.4f, cellSize * 0.18f);
            fall.transform.localScale = new Vector3(cellSize * 0.32f, wallHeight * 0.72f, 1f);
            ApplyMaterial(fall, CastleLavafallMaterial(), new Color(1f, 0.25f, 0.025f, 0.9f));

            // Dos jambas de basalto y un dintel hacen legible la fuente sin crear una pared nueva.
            for (int side = -1; side <= 1; side += 2)
                BuildCastleFurniture(center + new Vector3(side * cellSize * 0.2f, wallHeight * 0.36f, cellSize * 0.2f),
                    new Vector3(cellSize * 0.045f, wallHeight * 0.72f, cellSize * 0.055f), new Color(0.09f, 0.075f, 0.07f), "LavafallBasaltJamb");
            BuildCastleFurniture(center + new Vector3(0f, wallHeight * 0.75f, cellSize * 0.2f),
                new Vector3(cellSize * 0.45f, wallHeight * 0.06f, cellSize * 0.07f), new Color(0.09f, 0.075f, 0.07f), "LavafallBasaltLintel");

            var glow = new GameObject("CastleLavafallGlow");
            glow.transform.SetParent(_root.transform, false);
            glow.transform.position = center + new Vector3(0f, 0.55f, cellSize * 0.15f);
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.22f, 0.035f);
            light.intensity = 1.6f;
            light.range = cellSize * 2.4f;
            light.shadows = LightShadows.None;
            glow.AddComponent<LavaGlowFlicker>().Initialize(light);
        }

        private static Material _castleLavaMaterial;
        private static Material _castleLavafallMaterial;

        private static Material CastleLavaMaterial()
        {
            if (_castleLavaMaterial == null)
            {
                _castleLavaMaterial = Resources.Load<Material>("Materials/CastleLava");
                if (_castleLavaMaterial == null)
                {
                    var shader = Shader.Find("Custom/CastleLava");
                    if (shader != null) _castleLavaMaterial = new Material(shader);
                }
            }
            return _castleLavaMaterial;
        }

        private static Material CastleLavafallMaterial()
        {
            if (_castleLavafallMaterial == null)
            {
                _castleLavafallMaterial = Resources.Load<Material>("Materials/CastleLavafall");
                if (_castleLavafallMaterial == null)
                {
                    var shader = Shader.Find("Custom/CastleLavafall");
                    if (shader != null) _castleLavafallMaterial = new Material(shader);
                }
            }
            return _castleLavafallMaterial;
        }

        private void BuildCastleFurniture(Vector3 position, Vector3 scale, Color color, string name)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = name;
            piece.transform.SetParent(_root.transform, false);
            piece.transform.position = position + Vector3.up * 0.025f;
            piece.transform.localScale = scale;
            piece.isStatic = true;
            var collider = piece.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            piece.GetComponent<Renderer>().sharedMaterial = CastlePaletteMaterial(color);
        }

        private void BuildAmbientCritters(DungeonFloor floor, float cellSize, float wallHeight)
        {
            // Pedido puntual: "en las cuevas solo polillas" -- los 2 biomas de cueva de verdad
            // (Bioma de Cuevas Y Cueva Intergalactica/Bioma 2, ver DungeonFloor.Biome) cuentan
            // igual aca, no solo el rocoso. El bosque raiz (Biome 0) sigue con mariposas de dia.
            bool isCaveBiome = floor.Biome == 1 || floor.Biome == 2;

            // Arbustos + puntos de reposo genericos (cuevas); las lagunas de agua real
            // (_wildlifeWaterPoints, ver BuildVoidBlock) se pasan APARTE a Initialize: mismo
            // destino de vuelo valido que un arbusto, pero con estilo de revoloteo propio ("que
            // jueguen arriba del agua", ver ForestCritterAmbient.atWater/useWaterStyle), no
            // mezclado en la misma bolsa sin distincion.
            var habitats = new List<Vector3>(_wildlifeHabitatPoints);
            // Los biomas de cueva no tienen arbustos: usa pequeñas zonas de reposo repartidas por
            // el recorrido, pero solo activa polillas de noche (atraídas por las luces del nivel).
            if (isCaveBiome || habitats.Count + _wildlifeWaterPoints.Count < 3)
            {
                for (int x = 0; x < floor.Width; x++)
                for (int y = 0; y < floor.Height; y++)
                {
                    if (habitats.Count >= 12) break;
                    var cell = floor.Cells[x, y];
                    if (cell.Type == CellType.Void || cell.IsBossRoom) continue;
                    if (floor.Biome == 0 && cell.IsIsolatedZone) continue;
                    if ((x * 3 + y * 5) % 7 != 0) continue;
                    habitats.Add(CellCenter(x, y, cellSize) + Vector3.up * 0.65f);
                    if (habitats.Count >= 12) break;
                }
            }

            if (habitats.Count == 0 && _wildlifeWaterPoints.Count == 0) return;
            var manager = FindObjectOfType<DungeonManager>();
            var go = new GameObject(isCaveBiome ? "CaveMoths" : "ForestButterfliesAndMoths");
            go.transform.SetParent(_root.transform, false);
            var ambient = go.AddComponent<ForestCritterAmbient>();
            ambient.Initialize(habitats, _wildlifeWaterPoints, manager != null ? manager.player : null,
                manager != null ? manager.GetComponent<DayNightCycle>() : null,
                isCaveBiome, cellSize, floor, wallHeight, _caveEntrancePoint);
        }

        // Historial (para quien vuelva a tocar esto): hubo un intento anterior de shader de
        // parallax de pared/techo del bosque (Custom/ForestWallParallax) que se saco del proyecto
        // porque el parpadeo reportado no se iba -- tras varias rondas la causa real resulto ser
        // geometria de pared/piso/techo superpuesta (ver BuildWall), no el shader en si, pero ya se
        // habia vuelto al material solido para sacar esa variable de encima mientras se
        // diagnosticaba. Con la geometria ya entendida y arreglada, Custom/ForestWall (mas simple:
        // corteza + manchas de hoja, sin parallax de capas, sin desplazar vertices) vuelve a estar
        // asignado para las paredes del bosque real -- ver isForestBiome/forestWallMaterial en
        // BuildWall. El techo sigue sin sentirse chato gracias a BuildCanopyBranch (geometria real
        // colgando, no textura) que se sigue llamando igual desde el loop principal de Build().
        private void BuildCeilingTile(Vector3 center, float cellSize, float wallHeight, bool isIso, bool isBiome2, bool isCastleBiome)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ceiling";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, wallHeight + 0.1f, 0);
            go.transform.localScale = new Vector3(cellSize, 0.2f, cellSize);
            go.isStatic = true; // nunca se mueve ni cambia de material -- que el static batching lo agrupe
            if (isCastleBiome)
                go.GetComponent<Renderer>().sharedMaterial = CastleMaterial(ref _castleCeilingMaterial, new Color(0.18f, 0.18f, 0.22f));
            else if (isBiome2)
                ApplyMaterial(go, biome2CeilingMaterial, new Color(0.02f, 0.02f, 0.07f));
            else
                ApplyMaterial(go,
                    isIso ? isoCeilingMaterial : ceilingMaterial,
                    isIso ? new Color(0.08f, 0.07f, 0.06f) : new Color(0.15f, 0.15f, 0.17f));
        }

        // Una unica lamina continua de copa sobre todo el bosque. El shader usa coordenadas
        // globales, asi que el patron no se reinicia en cada celda y tampoco hay juntas visibles.
        private void BuildForestCanopy(DungeonFloor floor, float cellSize, float wallHeight)
        {
            if (_forestCanopyMaterial == null)
                _forestCanopyMaterial = Resources.Load<Material>("Materials/ForestCanopy");

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ForestCanopyGlobal";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = new Vector3(
                (floor.Width - 1) * cellSize * 0.5f,
                wallHeight + 0.25f,
                (floor.Height - 1) * cellSize * 0.5f);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(floor.Width * cellSize, floor.Height * cellSize, 1f);
            go.isStatic = true;
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            ApplyMaterial(go, _forestCanopyMaterial, new Color(0.045f, 0.12f, 0.035f));
        }

        // Aura sutil de piso alrededor de la escalera (Custom/StairsAura, ver Assets/Shaders): un
        // quad chato y grande sobre el piso (radio de unas 3 celdas), asi el jugador nota que hay
        // una escalera cerca desde un pasillo recto ANTES de estar encima -- pero solo por linea de
        // vista real, las paredes la ocluyen como a cualquier geometria (nada de "brillar a traves
        // de la pared"). No se crea nada si no hay material asignado (sin fallback de color solido:
        // un parche opaco se veria peor que no tener aura).
        private void BuildStairsFloorAura(Vector3 center, float cellSize, bool goingUp)
        {
            if (stairsAuraMaterial == null) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "StairsFloorAura";
            go.transform.SetParent(_root.transform, false);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            const float radiusInCells = 3f;
            float diameter = cellSize * radiusInCells * 2f;
            go.transform.position = center + new Vector3(0, 0.02f, 0);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(diameter, diameter, 1f);

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = stairsAuraMaterial;
            var props = new MaterialPropertyBlock();
            props.SetColor("_Color", goingUp ? new Color(0.35f, 1f, 1f, 1f) : new Color(1f, 0.55f, 0.1f, 1f));
            renderer.SetPropertyBlock(props);
        }

        // Un unico piso/techo grande que cubre todo el rectangulo de la sala de jefe, para que se
        // vea como una sala espaciosa continua en vez de celdas individuales.
        private void BuildBossRoomSlab(DungeonFloor floor, float cellSize, float wallHeight)
        {
            Vector3 minCenter = CellCenter(floor.BossRoomMinX, floor.BossRoomMinY, cellSize);
            Vector3 maxCenter = CellCenter(floor.BossRoomMaxX, floor.BossRoomMaxY, cellSize);
            float sizeX = (maxCenter.x - minCenter.x) + cellSize;
            float sizeZ = (maxCenter.z - minCenter.z) + cellSize;
            Vector3 roomCenter = new Vector3((minCenter.x + maxCenter.x) / 2f, 0f, (minCenter.z + maxCenter.z) / 2f);

            bool isSpaceBiome = floor.Biome == 1;
            bool isRockCaveBiome = floor.Biome == 2;
            bool isCastleBiome = floor.Biome == 3 || floor.Biome == 4;

            var floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floorGo.name = "BossRoomFloor";
            floorGo.transform.SetParent(_root.transform, false);
            floorGo.transform.position = roomCenter + new Vector3(0, -0.1f, 0);
            floorGo.transform.localScale = new Vector3(sizeX, 0.2f, sizeZ);
            floorGo.isStatic = true; // nunca se mueve ni cambia de material -- que el static batching lo agrupe
            if (isCastleBiome)
                floorGo.GetComponent<Renderer>().sharedMaterial = CastleMaterial(ref _castleStoneMaterial, new Color(0.25f, 0.26f, 0.29f));
            else if (isSpaceBiome)
                ApplyMaterial(floorGo, biome2FloorMaterial, new Color(0.05f, 0.05f, 0.08f));
            else if (isRockCaveBiome)
                ApplyMaterial(floorGo, isoFloorMaterial, new Color(0.24f, 0.22f, 0.20f));
            else
                ApplyMaterial(floorGo, bossRoomFloorMaterial, new Color(0.45f, 0.14f, 0.14f));

            var ceilGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceilGo.name = "BossRoomCeiling";
            ceilGo.transform.SetParent(_root.transform, false);
            ceilGo.transform.position = roomCenter + new Vector3(0, wallHeight + 0.1f, 0);
            ceilGo.transform.localScale = new Vector3(sizeX, 0.2f, sizeZ);
            ceilGo.isStatic = true; // nunca se mueve ni cambia de material -- que el static batching lo agrupe
            if (isCastleBiome)
                ceilGo.GetComponent<Renderer>().sharedMaterial = CastleMaterial(ref _castleCeilingMaterial, new Color(0.18f, 0.18f, 0.22f));
            else if (isSpaceBiome)
                ApplyMaterial(ceilGo, biome2CeilingMaterial, new Color(0.02f, 0.02f, 0.07f));
            else if (isRockCaveBiome)
                ApplyMaterial(ceilGo, isoCeilingMaterial, new Color(0.08f, 0.07f, 0.06f));
            else
                ApplyMaterial(ceilGo, bossRoomCeilingMaterial, new Color(0.2f, 0.08f, 0.08f));
        }

        // Pared compartida entre esta celda y su vecina: la linea que las divide, exactamente en el
        // borde de cellSize (igual que el mapa real de Etrian Odyssey, sin vacio fisico). La pared
        // de un atajo NUNCA se destruye: el vacio entre el switch y el punto de llegada se cruza por
        // teletransporte, no caminando.
        // Devuelve el ancla (no la pared en si -- ver _wallAnchors) para que el llamador la registre
        // por celda/direccion: pedido puntual "perforar una pared no deberia reconstruir el
        // laberinto entero", ver RebuildWallAt. El ancla es un GameObject vacio en el origen
        // (transform identidad) parentado a _root, y TODO lo que esta pared construye (el cubo,
        // las capas de parallax del bosque, los estandartes de castillo) cuelga de ESE ancla en vez
        // de colgar directo de _root -- SetParent(x, false) contra un ancla en el origen da las
        // mismas posiciones mundiales de siempre, este cambio de jerarquia es puramente estructural.
        // Asi, Destroy(ancla) en runtime borra la pared completa de un saque sin tocar nada mas del
        // piso (StaticBatchingUtility.Combine sigue funcionando igual: agarra CUALQUIER Renderer que
        // cuelgue de _root sin importar la profundidad, y destruir un GameObject ya incluido en un
        // Combine anterior lo saca de renderizar sin rehacer el Combine -- mismo mecanismo que ya
        // usa CollapseFloorVisual mas arriba en este archivo).
        private GameObject BuildWall(Vector3 cellCenter, Direction dir, float cellSize, float wallHeight, float wallThickness, bool isIso, bool isForest, bool isCastle = false)
        {
            var (ox, oy) = dir.Offset();
            Vector3 edgeOffset = new Vector3(ox, 0, oy) * (cellSize / 2f);
            Vector3 pos = cellCenter + edgeOffset + new Vector3(0, wallHeight / 2f, 0);

            var anchor = new GameObject("WallAnchor");
            anchor.transform.SetParent(_root.transform, false);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Wall";
            go.transform.SetParent(anchor.transform, false);
            go.transform.position = pos;

            // Padding vertical (BuildVoidBlock ya usaba el mismo truco): el borde de arriba de la
            // pared quedaba EXACTAMENTE en el mismo plano Y que la cara de abajo del techo (los dos
            // en wallHeight), y el de abajo exactamente en el mismo plano que la cara de arriba del
            // piso (los dos en Y=0) -- dos superficies ocupando el mismo lugar del z-buffer,
            // z-fighting de manual. Unos centimetros de mas arriba y abajo, mismo centro, escondidos
            // detras del piso/techo (que los siguen tapando igual, cero cambio visual).
            //
            // NO se recorta el largo horizontal (a diferencia de una version anterior de este
            // metodo): reducir las paredes Este/Oeste un wallThickness para "no invadir la esquina"
            // de la pared Norte/Sur sonaba bien en el papel, pero asumia que esa esquina SIEMPRE
            // estaba cubierta por la pared Norte/Sur de la MISMA celda -- falso si esa celda esta
            // abierta de ese lado (sin pared ahi). Resultado real: un hueco angosto de verdad,
            // "se ve a traves de la pared" (reportado con captura). Revertido -- el pique minusculo
            // de superposicion en la esquina (wallThickness x wallThickness) es preferible a un
            // agujero real; ninguno de los dos causaba el parpadeo reportado originalmente.
            bool horizontalWall = (dir == Direction.North || dir == Direction.South);
            float paddedHeight = wallHeight + 0.4f;
            go.transform.localScale = horizontalWall
                ? new Vector3(cellSize, paddedHeight, wallThickness)
                : new Vector3(wallThickness, paddedHeight, cellSize);
            go.isStatic = true; // nunca se mueve ni cambia de material -- que el static batching lo agrupe (es el elemento mas numeroso de todos)

            // isForest (bosque raiz real, nunca su zona aislada -- ver isForestBiome en Build())
            // usa Custom/ForestWall si hay material asignado; el resto de biomas sigue exactamente
            // igual que antes (isoWallMaterial o wallMaterial solido). isForest nunca es true al
            // mismo tiempo que isIso, pero el orden de la condicion deja isIso con prioridad de
            // todos modos por si algun dia se combinan.
            //
            // hasForestArt: si las dos capas de parallax con arte real estan asignadas (ver mas
            // abajo), la base de la pared pasa a ser un color plano bien oscuro en vez del shader
            // procedural de corteza -- las dos compitiendo a la vez (corteza dorada generada +
            // arbol pintado de verdad encima) se ve como un choque de estilos, no como profundidad.
            // El shader procedural sigue siendo el fallback automatico si alguna de las dos capas
            // se deja sin asignar (por ejemplo, mientras se prueban distintas ilustraciones).
            bool hasForestArt = isForest && forestLayerFarMaterial != null && forestLayerNearMaterial != null;
            Material mat = isCastle ? CastleMaterial(ref _castleWallMaterial, new Color(0.28f, 0.28f, 0.32f))
                : isIso ? isoWallMaterial
                : hasForestArt && forestBackdropMaterial != null ? forestBackdropMaterial
                : hasForestArt ? null
                : isForest && forestWallMaterial != null ? forestWallMaterial
                : wallMaterial;
            Color fallback = isIso ? new Color(0.32f, 0.29f, 0.26f)
                : hasForestArt ? new Color(0.03f, 0.05f, 0.06f)
                : isForest ? new Color(0.16f, 0.28f, 0.13f)
                : new Color(0.5f, 0.45f, 0.4f);
            ApplyMaterial(go, mat, fallback);

            // Capas de parallax con arte real (ver ForestParallaxWallFactory), SOLO bosque real --
            // se paran bien afuera del volumen solido de esta pared (mas alla de wallThickness/2),
            // nunca dentro ni coincidiendo con su superficie, asi que no interactuan con el padding
            // vertical de arriba ni pueden reabrir el bug de parpadeo historico documentado ahi.
            if (isForest)
                ForestParallaxWallFactory.Build(anchor.transform, pos, new Vector3(ox, 0, oy), cellSize, wallHeight, wallThickness, forestLayerFarMaterial, forestLayerNearMaterial);
            if (isCastle && Random.value < 0.16f)
                BuildCastleWallBanners(anchor.transform, pos, dir, cellSize, wallHeight, wallThickness);

            return anchor;
        }

        private void BuildCastleWallBanners(Transform parent, Vector3 wallCenter, Direction wallDir, float cellSize, float wallHeight, float wallThickness)
        {
            var (dx, dz) = wallDir.Offset();
            var wallNormal = new Vector3(dx, 0f, dz);
            bool horizontal = wallDir == Direction.North || wallDir == Direction.South;
            foreach (float side in new[] { -1f, 1f })
            {
                var banner = GameObject.CreatePrimitive(PrimitiveType.Cube);
                banner.name = "CastleBanner";
                banner.transform.SetParent(parent, false);
                banner.transform.position = wallCenter + wallNormal * side * (wallThickness * 0.5f + 0.025f) + Vector3.up * (wallHeight * 0.58f);
                banner.transform.localScale = horizontal
                    ? new Vector3(cellSize * 0.36f, wallHeight * 0.54f, 0.025f)
                    : new Vector3(0.025f, wallHeight * 0.54f, cellSize * 0.36f);
                banner.isStatic = true;
                var collider = banner.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                banner.GetComponent<Renderer>().sharedMaterial = CastleMaterial(ref _castleRugMaterial, new Color(0.34f, 0.045f, 0.07f));

                var crest = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crest.name = "CastleBannerCrest";
                crest.transform.SetParent(parent, false);
                crest.transform.position = banner.transform.position + Vector3.up * (wallHeight * 0.11f);
                crest.transform.localScale = horizontal
                    ? new Vector3(cellSize * 0.15f, wallHeight * 0.11f, 0.035f)
                    : new Vector3(0.035f, wallHeight * 0.11f, cellSize * 0.15f);
                crest.isStatic = true;
                var crestCollider = crest.GetComponent<Collider>();
                if (crestCollider != null) Destroy(crestCollider);
                crest.GetComponent<Renderer>().sharedMaterial = CastleMaterial(ref _castleTrimMaterial, new Color(0.72f, 0.55f, 0.22f));
            }
        }

        private void BuildMarker(DungeonCell cell, Vector3 center, float cellSize, float wallHeight, int floorWidth, int floorHeight, bool monumentalCastleFront = false)
        {
            if (cell.Type == CellType.CastleGate)
            {
                BuildCastleGateMarker(cell, center, cellSize, wallHeight, floorWidth, floorHeight);
                return;
            }
            if (cell.Type == CellType.CastleMainEntrance || cell.Type == CellType.CastleRearEntrance)
            {
                BuildCastleDoorMarker(cell, center, cellSize, wallHeight, monumentalCastleFront);
                return;
            }
            if (cell.Type == CellType.HubPortal)
            {
                BuildHubPortalMarker(cell, center, cellSize, wallHeight);
                return;
            }

            Color color;
            Material mat = null;
            PrimitiveType shape = PrimitiveType.Sphere;
            float scale = cellSize * 0.25f;

            switch (cell.Type)
            {
                case CellType.Start: color = Color.green; mat = startMarkerMaterial; break;
                case CellType.End: color = Color.red; mat = endMarkerMaterial; break;
                case CellType.SecondaryQuest: color = Color.yellow; mat = questMarkerMaterial; break;
                case CellType.ShortcutSwitch: color = new Color(0.2f, 0.4f, 1f); mat = switchMarkerMaterial; shape = PrimitiveType.Cylinder; break;
                case CellType.ShortcutLanding: color = new Color(0.85f, 0.45f, 0.1f); mat = landingMarkerMaterial; shape = PrimitiveType.Cylinder; break;
                case CellType.StairsUp: color = Color.cyan; mat = stairsUpMaterial; shape = PrimitiveType.Cube; break;
                case CellType.StairsDown: color = new Color(1f, 0.5f, 0f); mat = stairsDownMaterial; shape = PrimitiveType.Cube; break;
                case CellType.Boss: color = new Color(0.7f, 0f, 0.05f); mat = bossMarkerMaterial; shape = PrimitiveType.Capsule; scale = cellSize * 0.55f; break;
                case CellType.Lore:
                    // Las 3 pistas de la Puerta Fria (ver DungeonGenerator.BiomeGateLoreIds) se ven
                    // distintas -- dorado en vez de violeta, y mas grandes -- para que se lean como
                    // mecanicamente importantes apenas aparecen en pantalla, no solo sabor.
                    bool isMysteryClue = System.Array.IndexOf(DungeonGenerator.BiomeGateLoreIds, cell.AssignedLoreId) >= 0;
                    color = isMysteryClue ? new Color(1f, 0.82f, 0.25f) : new Color(0.75f, 0.35f, 1f);
                    mat = loreMarkerMaterial;
                    shape = PrimitiveType.Sphere;
                    scale = isMysteryClue ? cellSize * 0.42f : cellSize * 0.3f;
                    break;
                case CellType.LockedDoor: color = new Color(0.55f, 0.1f, 0.1f); mat = lockedDoorMarkerMaterial; shape = PrimitiveType.Cube; scale = cellSize * 0.5f; break;
                case CellType.Lever: color = new Color(0.15f, 0.9f, 0.35f); mat = leverMarkerMaterial; shape = PrimitiveType.Cylinder; scale = cellSize * 0.3f; break;
                case CellType.Treasure: color = new Color(1f, 0.82f, 0.1f); mat = treasureMarkerMaterial; shape = PrimitiveType.Cube; scale = cellSize * 0.35f; break;
                // Sin material dedicado a proposito (ver CellType.BiomeGate): no hace falta tocar
                // DungeonSceneBuilder por esto, el color de respaldo alcanza para el brillo frio
                // que se supone que tiene la grieta recien abierta.
                case CellType.BiomeGate: color = new Color(0.55f, 0.85f, 1f); shape = PrimitiveType.Sphere; scale = cellSize * 0.35f; break;
                // Escalera al fondo de la zona aislada (ver DungeonGenerator.PlaceCaveBiomeExit):
                // violeta, para no confundirse ni con StairsUp/Down (cyan/naranja, van a otro
                // piso) ni con la Puerta Fria (celeste, mecanismo secreto aparte) -- misma silueta
                // de escalera, pero un color que se lee como "portal a otro lado", no "otro piso".
                case CellType.CaveBiomeExit: color = new Color(0.65f, 0.3f, 0.95f); shape = PrimitiveType.Cube; break;
                case CellType.CastleGate: color = new Color(0.8f, 0.62f, 0.26f); break;
                case CellType.HubPortal: color = new Color(0.2f, 0.8f, 1f); break;
                default: return;
            }

            bool isStairs = cell.Type == CellType.StairsUp || cell.Type == CellType.StairsDown || cell.Type == CellType.CaveBiomeExit;
            if (isStairs)
            {
                // Antes: un cubo liso cyan/naranja. Ahora: una silueta de escalera con volumen
                // real (pseudo-3D, no un sprite chato) que gira sobre si misma para mirar siempre
                // al jugador (ver BillboardY) -- se distingue de cualquier otro bloque de la
                // mazmorra de un vistazo, no solo por el color.
                BuildStairsIcon(center, scale * 1.4f * 1.5f, color, mat); // +50% de tamano -- pedido puntual
            }
            else
            {
                var go = GameObject.CreatePrimitive(shape);
                go.name = $"Marker_{cell.Type}";
                go.transform.SetParent(_root.transform, false);
                go.transform.position = center + new Vector3(0, scale * 0.5f, 0);
                go.transform.localScale = Vector3.one * scale;

                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);

                ApplyMaterial(go, mat, color);

                if (cell.Type == CellType.LockedDoor)
                    _lockedDoorMarkers[(cell.X, cell.Y)] = go;

                if (cell.Type == CellType.ShortcutSwitch || cell.Type == CellType.ShortcutLanding)
                {
                    var entry = _shortcutMarkers.TryGetValue(cell.ControlledGateIndex, out var pair) ? pair : (null, null);
                    if (cell.Type == CellType.ShortcutSwitch) entry.switchGo = go; else entry.landingGo = go;
                    _shortcutMarkers[cell.ControlledGateIndex] = entry;
                }
            }

            if (isStairs)
            {
                var beaconGo = new GameObject("StairsBeacon");
                beaconGo.transform.SetParent(_root.transform, false);
                beaconGo.transform.position = center;
                beaconGo.AddComponent<StairsBeacon>().Configure(cell.Type == CellType.StairsUp, cellSize, wallHeight);

                BuildStairsFloorAura(center, cellSize, cell.Type == CellType.StairsUp);
            }
        }

        private void BuildCastleGateMarker(DungeonCell cell, Vector3 center, float cellSize, float wallHeight, int floorWidth, int floorHeight)
        {
            CastleMaterial(ref _castleTrimMaterial, new Color(0.65f, 0.48f, 0.2f));
            CastleMaterial(ref _castlePortalMaterial, new Color(0.13f, 0.32f, 0.48f));
            var root = new GameObject("CastleThreshold");
            root.transform.SetParent(_root.transform, false);
            root.transform.position = center;
            var towardPath = cell.FirstOpenDirection().Offset();
            root.transform.rotation = Quaternion.LookRotation(new Vector3(towardPath.dx, 0f, towardPath.dy), Vector3.up);
            float width = cellSize * 0.72f;
            float height = wallHeight * 0.82f;
            AddCastleBlock(root.transform, new Vector3(-width * 0.42f, height * 0.45f, 0f), new Vector3(width * 0.13f, height * 0.9f, cellSize * 0.12f), _castleWallMaterial, new Color(0.28f, 0.26f, 0.24f), "CastleGatePillar");
            AddCastleBlock(root.transform, new Vector3(width * 0.42f, height * 0.45f, 0f), new Vector3(width * 0.13f, height * 0.9f, cellSize * 0.12f), _castleWallMaterial, new Color(0.28f, 0.26f, 0.24f), "CastleGatePillar");
            AddCastleBlock(root.transform, new Vector3(0f, height * 0.9f, 0f), new Vector3(width, height * 0.14f, cellSize * 0.14f), _castleTrimMaterial, new Color(0.65f, 0.48f, 0.2f), "CastleGateLintel");
            BuildDistantCastleLandmark(center, cellSize, wallHeight, floorWidth, floorHeight);
        }

        private void BuildCastleDoorMarker(DungeonCell cell, Vector3 center, float cellSize, float wallHeight, bool monumentalCastleFront)
        {
            bool rear = cell.Type == CellType.CastleRearEntrance;
            var root = new GameObject(rear ? "CastleRearDoor" : "CastleMainGate");
            root.transform.SetParent(_root.transform, false);
            root.transform.position = center;
            var approach = cell.FirstOpenDirection().Offset();
            root.transform.rotation = Quaternion.LookRotation(new Vector3(approach.dx, 0f, approach.dy), Vector3.up);
            bool monumentalFront = !rear && monumentalCastleFront;
            float width = cellSize * (rear ? 0.67f : monumentalFront ? 0.92f : 0.72f);
            float height = wallHeight * (rear ? 0.88f : monumentalFront ? 1.38f : 0.88f);
            var stone = rear ? new Color(0.24f, 0.21f, 0.19f) : new Color(0.27f, 0.28f, 0.31f);
            var trim = rear ? new Color(0.38f, 0.22f, 0.12f) : new Color(0.67f, 0.49f, 0.21f);
            var door = rear ? new Color(0.18f, 0.075f, 0.035f) : new Color(0.075f, 0.095f, 0.14f);
            AddCastleBlock(root.transform, new Vector3(-width * 0.42f, height * 0.45f, 0f),
                new Vector3(width * 0.14f, height * 0.9f, cellSize * 0.16f), _castleWallMaterial, stone, "CastleDoorPillar");
            AddCastleBlock(root.transform, new Vector3(width * 0.42f, height * 0.45f, 0f),
                new Vector3(width * 0.14f, height * 0.9f, cellSize * 0.16f), _castleWallMaterial, stone, "CastleDoorPillar");
            AddCastleBlock(root.transform, new Vector3(0f, height * 0.9f, 0f),
                new Vector3(width, height * 0.13f, cellSize * 0.18f), _castleTrimMaterial, trim, "CastleDoorLintel");
            AddCastleBlock(root.transform, new Vector3(0f, height * 0.44f, -cellSize * 0.07f),
                new Vector3(width * 0.68f, height * 0.74f, cellSize * 0.08f), _castleWallMaterial, door, "CastleDoorLeaf");
            AddCastleBlock(root.transform, new Vector3(width * 0.2f, height * 0.43f, -cellSize * 0.115f),
                new Vector3(width * 0.025f, height * 0.67f, 0.035f), _castleTrimMaterial, trim, "CastleDoorSeam");
        }

        // Rework (pedido puntual: "castillo gigante que se vea a distancia, pese a la
        // iluminacion"): la version vieja quedaba pegada a la celda de la Puerta ( wallHeight*1.35
        // de alto), pero el techo por-celda (BuildCeilingTile, a wallHeight+0.1) Y la copa global
        // del bosque (BuildForestCanopy, a wallHeight+0.25) cubren TODO el piso a una altura menor
        // -- el castillo quedaba tapado por su propio techo, invisible incluso parado al lado.
        // Ahora se lo empuja fuera del rectangulo que cubre la copa y apenas por encima de esa
        // altura. Mantenerlo cerca y bajo hace que entre en el frustum de la camara de exploracion,
        // que mira hacia el camino y no al cielo.
        // El shader Custom/CastleLandmark es unlit y no aplica niebla (ver ese archivo), asi que se
        // lee igual de dia, de noche o con niebla.
        //
        // "Afuera" se calcula respecto del CENTRO real del piso (mismo origen que BuildForestCanopy
        // usa para la copa), no de cell.FirstOpenDirection() de la celda de la Puerta -- esa function
        // devuelve CUALQUIER lado sin pared de la celda (el primero que encuentra, sin relacion real
        // con "hacia donde queda el resto del mapa"), asi que usarla para esto podia mandar el
        // castillo gigante hacia un costado arbitrario, sin ninguna vista despejada real desde el
        // resto del piso. Alejandolo del centro hacia el borde mas cercano es predecible: siempre
        // termina "detras" del piso, del lado donde ya hay menos mapa entre el jugador y el horizonte.
        private void BuildDistantCastleLandmark(Vector3 center, float cellSize, float wallHeight, int floorWidth, int floorHeight)
        {
            var mural = new GameObject("DistantCastleLandmark");
            mural.transform.SetParent(_root.transform, false);
            Vector3 mapCenter = new Vector3((floorWidth - 1) * cellSize * 0.5f, 0f, (floorHeight - 1) * cellSize * 0.5f);
            Vector3 fromCenter = center - mapCenter;
            Vector3 outward = Mathf.Abs(fromCenter.x) >= Mathf.Abs(fromCenter.z)
                ? new Vector3(Mathf.Sign(fromCenter.x != 0f ? fromCenter.x : 1f), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(fromCenter.z != 0f ? fromCenter.z : 1f));
            const float outwardDistanceInCells = 10f;
            mural.transform.position = center + outward * (cellSize * outwardDistanceInCells) + Vector3.up * (wallHeight * 1.5f);
            mural.AddComponent<BillboardY>();
            float width = cellSize * 10f;
            float height = wallHeight * 6f;
            // Rework (pedido puntual: "sacale el fondo negro... que se vea a distancia sin que se
            // vea raro"): antes habia un bloque "DistantRidge" -- una placa ancha (todo el ancho del
            // mural) y bien oscura (casi negra) pegada atras/debajo de las torres, pensada como
            // "loma lejana". De lejos, contra un cielo de noche igual de oscuro (ver DayNightCycle.
            // nightFogColor), se leia como un rectangulo negro solido detras del castillo en vez de
            // una loma -- se saca directamente, las torres solas ya se leen como un castillo sin
            // necesitar un "piso" debajo.
            AddDistantCastleBlock(mural.transform, new Vector3(0f, height * 0.49f, -0.08f), new Vector3(width * 0.3f, height * 0.85f, 0.65f), new Color(0.43f, 0.49f, 0.58f), "Keep");
            AddDistantCastleBlock(mural.transform, new Vector3(-width * 0.28f, height * 0.43f, -0.12f), new Vector3(width * 0.14f, height, 0.7f), new Color(0.52f, 0.57f, 0.64f), "WestTower");
            AddDistantCastleBlock(mural.transform, new Vector3(width * 0.28f, height * 0.43f, -0.12f), new Vector3(width * 0.14f, height, 0.7f), new Color(0.52f, 0.57f, 0.64f), "EastTower");
            AddDistantCastleBlock(mural.transform, new Vector3(0f, height * 0.97f, -0.16f), new Vector3(width * 0.2f, height * 0.12f, 0.9f), new Color(0.9f, 0.7f, 0.34f), "KeepCrown");
        }

        private void AddDistantCastleBlock(Transform parent, Vector3 localPosition, Vector3 localScale, Color color, string name)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = localPosition;
            block.transform.localScale = localScale;
            var collider = block.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            if (!_castleLandmarkMaterials.TryGetValue(color, out var material) || material == null)
            {
                if (_castleLandmarkTemplate == null)
                    _castleLandmarkTemplate = Resources.Load<Material>("Materials/CastleLandmark");
                if (_castleLandmarkTemplate != null)
                    material = new Material(_castleLandmarkTemplate) { color = color, enableInstancing = true };
                else
                {
                    var shader = Shader.Find("Custom/CastleLandmark");
                    if (shader == null) shader = FallbackStandardMaterial().shader;
                    material = new Material(shader) { color = color, enableInstancing = true };
                }
                _castleLandmarkMaterials[color] = material;
            }
            block.GetComponent<Renderer>().sharedMaterial = material;
        }

        private void BuildHubPortalMarker(DungeonCell cell, Vector3 center, float cellSize, float wallHeight)
        {
            CastleMaterial(ref _castleTrimMaterial, new Color(0.7f, 0.56f, 0.25f));
            CastleMaterial(ref _castlePortalMaterial, new Color(0.1f, 0.55f, 0.9f));
            var root = new GameObject("HubPortal");
            root.transform.SetParent(_root.transform, false);
            root.transform.position = center;
            var approach = cell.FirstOpenDirection().Offset();
            root.transform.rotation = Quaternion.LookRotation(new Vector3(approach.dx, 0f, approach.dy), Vector3.up);
            float width = cellSize * 0.64f;
            float height = wallHeight * 0.78f;
            AddCastleBlock(root.transform, new Vector3(-width * 0.44f, height * 0.45f, 0f), new Vector3(width * 0.12f, height * 0.92f, 0.12f), _castleTrimMaterial, new Color(0.45f, 0.34f, 0.19f), "HubPortalPillar");
            AddCastleBlock(root.transform, new Vector3(width * 0.44f, height * 0.45f, 0f), new Vector3(width * 0.12f, height * 0.92f, 0.12f), _castleTrimMaterial, new Color(0.45f, 0.34f, 0.19f), "HubPortalPillar");
            AddCastleBlock(root.transform, new Vector3(0f, height * 0.91f, 0f), new Vector3(width, height * 0.12f, 0.14f), _castleTrimMaterial, new Color(0.7f, 0.56f, 0.25f), "HubPortalLintel");
            AddCastleBlock(root.transform, new Vector3(0f, height * 0.45f, -0.035f), new Vector3(width * 0.67f, height * 0.76f, 0.025f), _castlePortalMaterial, new Color(0.1f, 0.55f, 0.9f), "HubPortalLight");
        }

        private void AddCastleBlock(Transform parent, Vector3 localPosition, Vector3 localScale, Material sharedMaterial, Color fallback, string name)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = localPosition;
            block.transform.localScale = localScale;
            var collider = block.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            var material = CastlePaletteMaterial(fallback);
            if (name == "HubPortalLight")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", fallback * 1.8f);
            }
            else if (name == "Keep" || name == "WestTower" || name == "EastTower" || name == "KeepCrown")
            {
                // La silueta queda legible de día y de noche aunque el resto del horizonte se funda
                // con la niebla ambiental.
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", fallback * 0.35f);
            }
            block.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Material CastlePaletteMaterial(Color color)
        {
            if (_castlePaletteMaterials.TryGetValue(color, out var cached) && cached != null) return cached;
            var material = new Material(FallbackStandardMaterial()) { color = color, enableInstancing = true };
            _castlePaletteMaterials[color] = material;
            return material;
        }

        private static Material CastleMaterial(ref Material cached, Color color)
        {
            if (cached == null)
                cached = new Material(FallbackStandardMaterial()) { color = color, enableInstancing = true };
            return cached;
        }

        // Silueta de escalera ascendente (4 escalones, cada uno mas alto que el anterior) con
        // volumen real -- no un sprite chato -- envuelta en BillboardY para que siempre presente
        // esa cara al jugador sin importar desde que pasillo se la mire. El mismo perfil sirve
        // para subir y para bajar: el color (cyan/naranja, igual que antes) sigue siendo lo que
        // distingue cual es cual, exactamente como cuando ambas eran un cubo liso. Sin inclinacion
        // (se probo y se revirtio -- pedido puntual) y un 50% mas grande que la version original.
        private void BuildStairsIcon(Vector3 center, float scale, Color color, Material mat)
        {
            var root = new GameObject("StairsIcon");
            root.transform.SetParent(_root.transform, false);
            root.transform.position = center;
            root.AddComponent<BillboardY>();
            // Se pone transparente cuando la camara esta muy cerca (pedido puntual: parado
            // encima o pegado a la escalera, el icono tapaba toda la pantalla y se veia raro).
            var fade = root.AddComponent<DistanceFade>();

            const int steps = 4;
            float totalW = scale * 1.1f;
            float stepW = totalW / steps;
            float depth = scale * 0.3f;
            for (int i = 0; i < steps; i++)
            {
                float stepH = scale * (0.2f + i * 0.18f);
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "StairStep";
                block.transform.SetParent(root.transform, false);
                float x = -totalW * 0.5f + stepW * (i + 0.5f);
                block.transform.localPosition = new Vector3(x, stepH * 0.5f, 0);
                block.transform.localScale = new Vector3(stepW, stepH, depth); // sin hueco entre escalones, silueta continua

                var col = block.GetComponent<Collider>();
                if (col != null) Destroy(col);
                ApplyMaterial(block, mat, color);

                var rend = block.GetComponent<Renderer>();
                ConfigureForAlphaFade(rend.material); // .material clona el material si era compartido
                fade.Register(rend, color);
            }
        }

        // Configura un material (Standard o el fallback de FallbackStandardMaterial) para que
        // respete el canal alfa del color (modo "Fade" de Standard) -- por defecto un material
        // Standard es opaco e ignora el alfa del color por completo. SetFloat/SetInt/EnableKeyword
        // sobre una propiedad que el shader no tiene no hacen nada (no tiran error), asi que esto
        // es inofensivo aunque el material termine siendo otro shader de respaldo.
        private static void ConfigureForAlphaFade(Material mat)
        {
            mat.SetFloat("_Mode", 3f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }

        // Boveda de cascada (pedido puntual, ver DungeonGenerator.AddWaterfallVault): esta celda
        // (IsWaterfallRoom) tiene la cascada real -- busca hacia que lado da la boveda (el unico
        // vecino con IsWaterfallVaultRoom) y le pone una cortina de agua cayendo justo en esa
        // apertura (sin collider: se camina a traves, "detras de la cascada" es la boveda). La
        // fuente que la alimenta es un espejo de agua en el piso de ESTE mismo cuarto, reusando el
        // shader Custom/VoidWater (ver Assets/Data/VoidWaterMaterial.mat / BuildVoidPit mas arriba)
        // en vez de inventar uno nuevo para una superficie horizontal.
        private static Material _waterfallCurtainMaterial;
        private static Material _waterfallPondMaterial;

        private static Material WaterfallCurtainMaterial()
        {
            if (_waterfallCurtainMaterial == null)
            {
                _waterfallCurtainMaterial = Resources.Load<Material>("Materials/Waterfall");
                if (_waterfallCurtainMaterial == null)
                {
                    var shader = Shader.Find("Custom/Waterfall");
                    if (shader != null) _waterfallCurtainMaterial = new Material(shader);
                }
            }
            return _waterfallCurtainMaterial;
        }

        private static Material WaterfallPondMaterial()
        {
            if (_waterfallPondMaterial == null)
            {
                var shader = Shader.Find("Custom/VoidWater");
                if (shader != null) _waterfallPondMaterial = new Material(shader);
            }
            return _waterfallPondMaterial;
        }

        private void BuildWaterfallDecor(DungeonFloor floor, int x, int y, Vector3 center, float cellSize, float wallHeight)
        {
            Direction? vaultDir = null;
            foreach (var dir in DirectionExtensions.All)
            {
                var (dox, doy) = dir.Offset();
                int nx = x + dox, ny = y + doy;
                if (floor.InBounds(nx, ny) && floor.Cells[nx, ny].IsWaterfallVaultRoom) { vaultDir = dir; break; }
            }
            if (!vaultDir.HasValue) return;

            var (dx, dy) = vaultDir.Value.Offset();
            Vector3 archCenter = center + new Vector3(dx, 0, dy) * (cellSize / 2f);

            var curtain = GameObject.CreatePrimitive(PrimitiveType.Quad);
            curtain.name = "WaterfallCurtain";
            curtain.transform.SetParent(_root.transform, false);
            var curtainCol = curtain.GetComponent<Collider>();
            if (curtainCol != null) Destroy(curtainCol); // decorativo: se camina a traves, hacia la boveda
            curtain.transform.position = archCenter + Vector3.up * (wallHeight * 0.5f);
            curtain.transform.rotation = Quaternion.LookRotation(new Vector3(dx, 0f, dy), Vector3.up);
            curtain.transform.localScale = new Vector3(cellSize * 0.92f, wallHeight * 0.98f, 1f);
            ApplyMaterial(curtain, WaterfallCurtainMaterial(), new Color(0.35f, 0.6f, 0.7f, 0.6f));

            // Fuente de agua: el espejo que "recibe" la cascada, del lado de ESTE cuarto (nunca en
            // la boveda del otro lado, que se queda seca -- ahi va el tesoro, no el agua).
            var pond = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pond.name = "WaterfallPond";
            pond.transform.SetParent(_root.transform, false);
            var pondCol = pond.GetComponent<Collider>();
            if (pondCol != null) Destroy(pondCol);
            pond.transform.position = center - new Vector3(dx, 0f, dy) * (cellSize * 0.15f) + Vector3.up * 0.03f;
            pond.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            pond.transform.localScale = new Vector3(cellSize * 0.7f, cellSize * 0.7f, 1f);
            ApplyMaterial(pond, WaterfallPondMaterial(), new Color(0.1f, 0.35f, 0.42f));

            // Antorcha de ambiente del lado OPUESTO a la cascada -- solo si de verdad hay una pared
            // real ahi (si ese lado fuera otro pasaje abierto, quedaria flotando sin nada que la
            // sostenga).
            var oppositeDir = vaultDir.Value.Opposite();
            if (floor.Cells[x, y].HasWall(oppositeDir))
                _pendingFlickerTorches.Add((center, oppositeDir));
        }

        private static Material _fallbackStandardMaterial;

        // Shader.Find("Standard") anda en el Editor (ve todos los shaders) pero un build
        // standalone descarta cualquier shader que ningun Material real referencie -- como NINGUNO
        // de los campos de material de este componente esta asignado por defecto (son "opcionales",
        // ver los [Header] de arriba), ese fallback devolvia null en el juego compilado y explotaba
        // ApplyMaterial (ver Assets/Resources/Materials/FallbackStandard.mat: al vivir en Resources
        // el build siempre lo incluye).
        private static Material FallbackStandardMaterial()
        {
            if (_fallbackStandardMaterial == null)
            {
                _fallbackStandardMaterial = Resources.Load<Material>("Materials/FallbackStandard");
                if (_fallbackStandardMaterial == null)
                {
                    var shader = Shader.Find("Standard");
                    _fallbackStandardMaterial = new Material(shader != null ? shader : Shader.Find("Diffuse"));
                }
            }
            return _fallbackStandardMaterial;
        }

        private void ApplyMaterial(GameObject go, Material mat, Color fallbackColor)
        {
            var renderer = go.GetComponent<Renderer>();
            if (mat != null)
            {
                renderer.sharedMaterial = mat;
            }
            else
            {
                var instanced = new Material(FallbackStandardMaterial());
                instanced.color = fallbackColor;
                renderer.material = instanced;
            }
        }

        // Al activar el atajo, los dos marcadores (switch y llegada) cambian a un color brillante
        // compartido para que se note que ya se puede teletransportar entre ambos.
        public void ActivateShortcutVisual(int gateIndex)
        {
            if (!_shortcutMarkers.TryGetValue(gateIndex, out var pair)) return;
            var activeColor = new Color(1f, 0.95f, 0.2f);
            if (pair.switchGo != null) ApplyMaterial(pair.switchGo, null, activeColor);
            if (pair.landingGo != null) ApplyMaterial(pair.landingGo, null, activeColor);
        }

        // Al activar la palanca, el marcador de la puerta bloqueada pasa de rojo (cerrada) a un
        // verde apagado (abierta para siempre), para que quede claro de un vistazo que ese candado
        // ya no bloquea nada en esta run.
        public void UnlockDoorVisual(int doorX, int doorY)
        {
            if (!_lockedDoorMarkers.TryGetValue((doorX, doorY), out var go) || go == null) return;
            ApplyMaterial(go, null, new Color(0.2f, 0.55f, 0.25f));
        }

        // Borra UNA pared puntual sin reconstruir el piso (pedido puntual: "perforar una pared no
        // deberia reconstruir el laberinto desde cero, solo esa pared deberia cambiar") -- llamado
        // por DungeonManager.TryUseDrill (Perforador) y por el caso CellType.Lever de TryInteract
        // (palanca de puerta bloqueada), ambos DESPUES de que DungeonGenerator ya puso
        // cell.HasWall(dir) en false. No hay nada que reconstruir en el lugar de la pared -- la
        // apertura queda como esta, el resto del piso (fauna ambiental incluida) ni se entera.
        public void RebuildWallAt(DungeonFloor floor, int x, int y, Direction dir)
        {
            if (!_wallAnchors.TryGetValue((x, y, dir), out var anchor) || anchor == null) return;
            _wallAnchors.Remove((x, y, dir));
            var (ox, oy) = dir.Offset();
            int nx = x + ox, ny = y + oy;
            if (floor.InBounds(nx, ny)) _wallAnchors.Remove((nx, ny, dir.Opposite()));
            Destroy(anchor);
        }
    }
}
