using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen
{
    public class DungeonGenerator
    {
        // ---------- Public orchestration ----------

        // indexOffset/biome: para generar el Bioma 2 (Cueva Intergalactica, ver Gameplay/
        // DungeonManager.GenerateAndEnterDungeon) con el mismo motor base que el Bioma 1 (jefe,
        // trampas, cofres, lore, FOE), pero sin el candado+palanca generico en la cueva, en vez de un piso
        // suelto aparte -- indexOffset hace que floor.Index siga la numeracion global de _floors
        // (asi las escaleras/StairTargetFloor salen bien solas, sin remapeo), y biome!=0 desactiva
        // lo exclusivo del Bioma 1 (Puerta Fria y sus 3 salas de pistas: PlaceBiomeGate solo debe
        // existir UNA vez en toda la run, en el piso 0 del Bioma 1, nunca dentro del propio Bioma 2).
        public List<DungeonFloor> GenerateDungeon(int floorCount, int width, int height, int seed, out List<string> log, int stairPairsPerFloor = 2, int bossFloorStart = 2, int bossFloorInterval = 2, float voidFraction = 0.4f, int dangerValueMin = 0, int dangerValueMax = 5, IList<string> loreIdPool = null, int indexOffset = 0, int biome = 0, float extraOpeningChance = 0.12f)
        {
            log = new List<string>();
            var rng = new Random(seed);
            var floors = new List<DungeonFloor>();
            int[] loreCorridorFloors = biome == 0 ? PickLoreCorridorFloors(floorCount) : Array.Empty<int>();

            // Correccion de un pedido anterior: la mini-cueva (zona aislada de tamano de cuadrante
            // + la escalera al Bioma de Cuevas, ver PlaceCaveBiomeExit) NO se sortea por piso --
            // vive UNICA y EXCLUSIVAMENTE en el piso 0 del bioma raiz, pedido puntual y explicito.
            // (Se probo sortearla en cualquier piso en un commit anterior; esto revierte ESA parte
            // nada mas.) La Puerta Fria (PlaceBiomeGate, mas abajo) tampoco se mueve de aca: sigue
            // siendo siempre el piso 0, es un mecanismo aparte.
            int caveExitFloorIndex = biome == 0 ? 0 : -1;

            for (int i = 0; i < floorCount; i++)
            {
                bool hasSpecialQuadrant = biome == 0 && i == 0;
                var floor = GenerateFloor(width, height, indexOffset + i, rng,
                    isCaveExitFloor: i == caveExitFloorIndex, useIsolatedZone: hasSpecialQuadrant,
                    biome: biome, biomeFloorIndex: i);

                // Tiene que ir ANTES de cualquier sala especial (jefe/trampas/candado/cofre/lore):
                // en este punto floor.Cells solo tiene Start/End/SecundariaQuest/Switch/Landing
                // (puestos por GenerateFloor) y el resto es Normal, asi que abrir cruces extra aca
                // no puede pisar ninguna sala que todavia no existe. Igual de importante: tiene que
                // ir ANTES de PlacePacingPillars (ver mas abajo) -- su verificacion de "cortar esta
                // arista de verdad desconecta todo" ya sabe lidiar con ciclos (los prueba con BFS),
                // pero solo si los ciclos YA EXISTEN cuando elige donde poner el candado. Si los
                // cruces se agregaran despues, el candado podria terminar en un tramo que un cruce
                // recien abierto vuelve esquivable.
                int crossingsAdded = AddCrossingPaths(floor, rng);
                log.Add($"Piso {i}: {crossingsAdded} cruce(s) extra abiertos cerca del camino Start->End.");

                // Mismo momento y misma razon que AddCrossingPaths de arriba (sus ciclos tienen que
                // existir ANTES de PlacePacingPillars, ver el comentario ahi) -- pedido puntual
                // inspirado en un video sobre diseño de niveles: reglas simples que fusionan
                // cuartitos chicos en camaras mas grandes, en vez de sistemas complejos nuevos.
                int braided = BraidExtraOpenings(floor, rng, extraOpeningChance);
                log.Add($"Piso {i}: {braided} pared(es) extra fusionadas (camaras mas grandes).");

                // El jefe del bosque debe existir siempre en su tercer piso (indice 2), aunque
                // una configuracion personalizada cambie el intervalo global de jefes.
                bool isForestBossFloor = biome == 0 && i == 2;
                bool isPatioBossFloor = biome == 3 && i == 2;
                bool isCastleFinalBossFloor = biome == 4 && (i == 2 || i == 4);
                bool isBossFloor = isForestBossFloor
                    || isPatioBossFloor
                    || isCastleFinalBossFloor
                    || (bossFloorInterval > 0 && i >= bossFloorStart && (i - bossFloorStart) % bossFloorInterval == 0);
                if (isBossFloor)
                {
                    bool added = AddBossRoom(floor, rng);
                    if (!added && (isForestBossFloor || isPatioBossFloor || isCastleFinalBossFloor))
                        added = AddFallbackBossMarker(floor);
                    log.Add(added
                        ? $"Piso {i}: sala de jefe en ({floor.BossRoomMinX},{floor.BossRoomMinY})-({floor.BossRoomMaxX},{floor.BossRoomMaxY}), jefe en {floor.BossPos}."
                        : $"Piso {i}: se pidio sala de jefe pero no hubo espacio libre (mapa muy chico). El encuentro no se pudo colocar.");
                }

                // Sala de trampas: no en el primer piso (respiro) ni siempre (variedad -- que no
                // toda bajada tenga una), 50% del resto. Va ANTES de podar por la misma razon que
                // la sala de jefe (sus celdas quedan protegidas al fusionarse en una sola sala).
                if (i > 0 && rng.NextDouble() < 0.5)
                {
                    bool trapAdded = AddTrapRoom(floor, rng);
                    log.Add(trapAdded
                        ? $"Piso {i}: sala de trampas ({floor.TrapKind}), {floor.TrapCells.Count} celdas peligrosas."
                        : $"Piso {i}: se intento sala de trampas pero no hubo espacio libre.");
                }

                // El candado+palanca obligatorios tienen que colocarse ANTES de podar, para poder
                // marcar sus celdas como protegidas y que la poda no se las coma como puntas
                // muertas sueltas (mismo motivo por el que el cofre, mas abajo, tambien va antes).
                // El bioma de cuevas no usa el puzzle generico de puerta+palanca: ahi aparecen
                // peligros, luces y recompensas propios de la cueva, nunca una palanca de otro
                // sistema que rompa su identidad o deje un candado sin contexto.
                bool pillarsAdded = biome != 2 && PlacePacingPillars(floor, rng);
                log.Add(biome == 2
                    ? $"Piso {i}: bioma de cuevas, sin candado ni palanca."
                    : pillarsAdded
                        ? $"Piso {i}: candado en {floor.LockedDoors[0].DoorX},{floor.LockedDoors[0].DoorY} (palanca en {floor.LockedDoors[0].LeverX},{floor.LockedDoors[0].LeverY})."
                        : $"Piso {i}: camino critico muy corto/sin tramo libre, sin candado este piso.");

                // Cofre GARANTIZADO por piso (a diferencia del opcional de arriba, que solo aparece
                // si PlacePacingPillars encontro lugar): todo piso tiene que darle al jugador algo
                // que facilite la exploracion. Tiene que ir ANTES de podar, por la misma razon que
                // los pilares de pacing arriba. Pedido puntual: en el Bioma de Cuevas las tumbas
                // (ver AddTombs) SON el tesoro garantizado -- nunca los dos sistemas en el mismo piso.
                if (biome == 2)
                {
                    bool tombsAdded = AddTombs(floor, rng);
                    log.Add(tombsAdded
                        ? $"Piso {i}: {floor.TombPositions.Count} tumba(s) en {string.Join(", ", floor.TombPositions)}."
                        : $"Piso {i}: sin punto muerto libre para ninguna tumba (mapa demasiado chico/denso).");
                }
                else
                {
                    bool treasureAdded = EnsureTreasure(floor, rng);
                    log.Add(treasureAdded
                        ? $"Piso {i}: {floor.TreasurePositions.Count} cofre(s) en {string.Join(", ", floor.TreasurePositions)}."
                        : $"Piso {i}: sin punto muerto libre para ningun cofre (mapa demasiado chico/denso).");
                }

                // Boveda de cascada (ver AddWaterfallVault): pedido puntual, "puede aparecer en
                // cualquier piso donde haya agua" -- en vez de una lista fija de biomas "tematicos",
                // el criterio real es si el Void de ESTE biome se renderiza como agua de verdad
                // (ver DungeonLevelBuilder.BuildVoidBlock): biome 0 (bosque), 1 (cueva
                // intergalactica) y 2 (Bioma de Cuevas) SI tienen laguna de agua en cada celda Void;
                // biome 3 (patio) tiene ruinas solidas en vez de agua, y biome 4 (castillo) tiene
                // LAVA, no agua -- esos dos quedan afuera aunque tengan Void igual.
                if (biome == 0 || biome == 1 || biome == 2)
                {
                    bool waterfallAdded = AddWaterfallVault(floor, rng);
                    if (waterfallAdded)
                        log.Add($"Piso {i}: boveda de cascada en {floor.WaterfallVaultPos}.");
                }

                // Trampas de goblins (pedido puntual, "nido de goblins"): unas cuantas celdas sueltas
                // del piso, cada una dispara su propio combate al pisarla -- ver AddGoblinTraps.
                if (biome == 2)
                    AddGoblinTraps(floor, rng);

                // La Puerta Fria (ver PlaceBiomeGate) solo existe en el piso 0 del Bioma 1 -- es la
                // entrada escondida al Bioma 2 (ver Gameplay/DungeonManager.GenerateAndEnterDungeon).
                // biome==0 evita que el propio Bioma 2 genere una Puerta Fria anidada hacia si mismo.
                // Sus 3 pistas YA NO estan ahi: viven repartidas en pisos fijos del Bioma 1 (ver
                // PickLoreCorridorFloors), cada una en su propia sala de pistas.
                if (i == 0 && biome == 0)
                {
                    bool gateAdded = PlaceBiomeGate(floor, rng);
                    log.Add(gateAdded
                        ? $"Piso {i}: Puerta Fria sellada en {floor.BiomeGatePos} (perforable desde {floor.BiomeGateApproachPos})."
                        : $"Piso {i}: sin punto muerto libre para la Puerta Fria (mapa demasiado chico/denso).");
                }

                // Mecanismo APARTE de la Puerta Fria de arriba (mismo espiritu, otro bioma de
                // destino -- el Bioma de Cuevas -- pero sin secreto): una escalera comun al fondo
                // de la zona aislada. Independiente del piso 0: puede tocarle a cualquier piso del
                // bioma raiz (ver caveExitFloorIndex, sorteado arriba una sola vez por run).
                if (i == caveExitFloorIndex && biome == 0)
                {
                    bool caveExitAdded = PlaceCaveBiomeExit(floor);
                    log.Add(caveExitAdded
                        ? $"Piso {i}: escalera al Bioma de Cuevas al fondo de la zona aislada en {floor.CaveBiomeExitPos}."
                        : $"Piso {i}: sin celda libre al fondo de la zona aislada para la escalera al Bioma de Cuevas.");

                    // 3 peligros propios de la mini cueva (pedido puntual: "agregar salas en la mini
                    // zona de cueva", que se sentia vacia comparada con el resto del piso) -- los 3
                    // son best-effort (ver DungeonFloor.HasBoulderTrap/HasCollapseRoom/HasAmbushRoom):
                    // si no encuentran lugar libre en el cuadrante, se saltan sin romper nada. Van
                    // DESPUES de PlaceCaveBiomeExit para no competir por el punto "mas profundo" que
                    // esa escalera ya reservo.
                    bool boulderAdded = AddBoulderTrap(floor, rng);
                    log.Add(boulderAdded
                        ? $"Piso {i}: trampa de roca en la mini cueva, corredor de {floor.BoulderTrapPath.Count} celdas desde {floor.BoulderTrapStartPos}."
                        : $"Piso {i}: sin corredor recto libre en la mini cueva para la trampa de roca.");

                    bool collapseAdded = AddCollapsingRushRoom(floor, rng);
                    log.Add(collapseAdded
                        ? $"Piso {i}: sala que colapsa en la mini cueva, entrada {floor.CollapseEntryPos} -> salida {floor.CollapseExitPos}."
                        : $"Piso {i}: sin lugar en la mini cueva para la sala que colapsa (hacen falta 2 conexiones alejadas).");

                    bool ambushAdded = AddAmbushRoom(floor, rng);
                    log.Add(ambushAdded
                        ? $"Piso {i}: sala de emboscada en la mini cueva, {floor.AmbushRoomCells.Count} celdas."
                        : $"Piso {i}: sin lugar libre en la mini cueva para la sala de emboscada.");
                }

                // El piso 2 del bosque tiene una bifurcacion opcional al castillo dentro de la
                // zona aislada. Su esquina ya se eligio al azar entre los cuatro cuadrantes en
                // GenerateFloor, asi la ruta no aparece siempre en el mismo lugar.
                if (i == 2 && biome == 0)
                {
                    bool castleGateAdded = PlaceCastleGate(floor, rng);
                    log.Add(castleGateAdded
                        ? $"Piso {i}: acceso al castillo en uno de los cuadrantes, {floor.CastleGatePos}."
                        : $"Piso {i}: no se encontro celda libre para el acceso al castillo.");
                }

                for (int lc = 0; lc < loreCorridorFloors.Length; lc++)
                {
                    if (loreCorridorFloors[lc] != i) continue;

                    // Goteras es la unica de las 3 variantes que hace caer al piso de ABAJO
                    // (floorIndex+1, ver DungeonManager.OnPlayerEnterCell) en vez de solo doler
                    // como Brasas/Polvo de Cuarzo -- el piso 0 es el piso de respiro (mismo
                    // criterio que mas abajo en PlaceFoeRoute: nunca en el Index 0), asi que no
                    // tiene sentido que la primerisima sala de pistas de la run mande al jugador
                    // de sorpresa a un piso mas dificil sin ningun aviso previo. La forzamos al
                    // piso 1 en cambio: con floorCount por defecto (3, ver DungeonSettings) el
                    // piso 1 siempre tiene un piso 2 debajo al que caer de verdad -- el piso 2
                    // seria el ULTIMO en ese caso, y ahi la caida degradaria en un dano de trampa
                    // comun (ver el fallback en DungeonManager) en vez de cambiar de piso de verdad.
                    PuzzleKind kind = i == 0 ? PuzzleKind.Brasas : i == 1 ? PuzzleKind.Goteras : PuzzleKind.PolvoDeCuarzo;

                    bool corridorAdded = AddLoreCorridorRoom(floor, rng, BiomeGateLoreIds[lc], kind);
                    log.Add(corridorAdded
                        ? $"Piso {i}: sala de pistas ({floor.LoreCorridorKind}) con {BiomeGateLoreIds[lc]}."
                        : $"Piso {i}: no se pudo colocar la sala de pistas de {BiomeGateLoreIds[lc]} (mapa demasiado chico/denso).");
                }

                int voided = PruneToSparseMaze(floor, rng, voidFraction);
                log.Add($"Piso {i}: poda de pasillos -> {voided} celdas convertidas en vacio (roca solida).");

                floors.Add(floor);
                log.Add($"Piso {i}: maze generado. Start={floor.StartPos} End={floor.EndPos} SecundariaQuest={floor.SecondaryQuestPos} ZonaAislada=({floor.IsoMinX},{floor.IsoMinY})-({floor.IsoMaxX},{floor.IsoMaxY}) Gates={floor.Gates.Count}");
            }

            for (int i = 0; i < floorCount - 1; i++)
            {
                var lower = floors[i];
                int pairs = lower.HasBossRoom ? 1 : stairPairsPerFloor;
                var restrict = lower.HasBossRoom ? lower.BossRoomCells : null;
                PlaceStairsBetween(lower, floors[i + 1], rng, pairCount: pairs, restrictLowerTo: restrict);
                log.Add($"Escaleras piso {i} <-> {i + 1} colocadas ({pairs} pares{(lower.HasBossRoom ? ", forzadas dentro de la sala de jefe" : "")}).");
            }

            // Pedido puntual: el FOE tiene que arrancar del lado OPUESTO a la escalera por la que el
            // jugador sube a este piso, y ademas su ruta entera (patrulla + persecucion, ver
            // FoeController) tiene que mantenerse a un radio minimo de las escaleras -- ninguna de
            // las dos cosas se puede resolver mientras el piso genera (ahi todavia no hay ninguna
            // escalera real: PlaceStairsBetween recien corrio arriba). Por eso PlaceFoeRoute se
            // movio a este loop, DESPUES de que todas las escaleras (la garantizada Start/End Y los
            // pares extra al azar) ya existen de verdad.
            foreach (var floor in floors)
            {
                PlaceFoeRoute(floor);
                log.Add(floor.HasFoe
                    ? $"Piso {floor.Index}: FOE patrullando {floor.FoePatrolRoute.Count} celdas."
                    : $"Piso {floor.Index}: sin FOE este piso.");
            }

            foreach (var floor in floors)
            {
                AssignDangerValues(floor, rng, dangerValueMin, dangerValueMax);

                string loreId = (loreIdPool != null && loreIdPool.Count > 0)
                    ? loreIdPool[floor.Index % loreIdPool.Count]
                    : $"lore_piso{floor.Index}";
                AssignLoreLock(floor, rng, loreId);
            }

            return floors;
        }

        // Le da a cada celda Normal un valor de peligro (0-5) al azar. Todas las demas
        // (Start/End/escaleras/vacio/switch/etc.) se quedan en 0: son siempre seguras de pisar.
        private void AssignDangerValues(DungeonFloor floor, Random rng, int min, int max)
        {
            int lo = Math.Max(0, Math.Min(min, max));
            int hi = Math.Max(lo, Math.Max(min, max));
            for (int x = 0; x < floor.Width; x++)
            {
                for (int y = 0; y < floor.Height; y++)
                {
                    var cell = floor.Cells[x, y];
                    if (cell.Type == CellType.Normal)
                        cell.DangerValue = rng.Next(lo, hi + 1);
                }
            }
        }

        // ---------- Single floor generation ----------

        public DungeonFloor GenerateFloor(int width, int height, int index, Random rng,
            bool isCaveExitFloor = false, bool useIsolatedZone = true, int biome = 0, int biomeFloorIndex = 0)
        {
            var floor = new DungeonFloor(width, height, index);
            floor.Biome = biome;
            floor.BiomeFloorIndex = biomeFloorIndex;

            if (useIsolatedZone)
            {
                // Solo el primer piso del bosque reserva un cuadrante para la mini cueva y la
                // Puerta Fria. Los demas pisos se tallan sobre el mapa completo.
                float frac = isCaveExitFloor
                    ? 0.23f + (float)rng.NextDouble() * 0.04f
                    : 0.20f + (float)rng.NextDouble() * 0.12f;
                int zoneW = Math.Max(2, (int)Math.Round(width * Math.Sqrt(frac)));
                int zoneH = Math.Max(2, (int)Math.Round(height * Math.Sqrt(frac)));
                zoneW = Math.Min(zoneW, width - 2);
                zoneH = Math.Min(zoneH, height - 2);

                int corner = rng.Next(4);
                int minX, minY;
                switch (corner)
                {
                    case 0: minX = 0; minY = 0; break;
                    case 1: minX = width - zoneW; minY = 0; break;
                    case 2: minX = 0; minY = height - zoneH; break;
                    default: minX = width - zoneW; minY = height - zoneH; break;
                }
                floor.IsoMinX = minX;
                floor.IsoMinY = minY;
                floor.IsoMaxX = minX + zoneW - 1;
                floor.IsoMaxY = minY + zoneH - 1;

                for (int x = floor.IsoMinX; x <= floor.IsoMaxX; x++)
                    for (int y = floor.IsoMinY; y <= floor.IsoMaxY; y++)
                        floor.Cells[x, y].IsIsolatedZone = true;
            }
            else
            {
                // Rectangulo vacio: IsInIsolatedZone devuelve false para todo el piso.
                floor.IsoMinX = floor.IsoMinY = 1;
                floor.IsoMaxX = floor.IsoMaxY = 0;
            }

            // 1.5. Salas de autor con forma prediseñada (ver PlacePredefinedRooms): tienen que
            // estamparse ANTES de Carve -- una vez colocadas, sus celdas quedan excluidas de la
            // region que Carve() recorre (como si esa forma fuera roca solida para el laberinto),
            // asi que el pasillo generado rodea la sala en vez de atravesarla, y despues se conecta
            // a ella por sus puertas declaradas.
            PlacePredefinedRooms(floor, rng);

            // 2. Cada piso normal usa un solo arbol de expansion sobre todo el mapa. Solo el
            // primer piso del bosque mantiene los dos sectores y el atajo de su cuadrante secreto.
            ShortcutGate gate = null;
            if (useIsolatedZone)
            {
                Carve(floor, (x, y) => !floor.IsInIsolatedZone(x, y) && !floor.Cells[x, y].IsPredefinedRoom, rng);
                Carve(floor, (x, y) => floor.IsInIsolatedZone(x, y), rng);

                var boundary = FindBoundaryEdges(floor);
                if (boundary.Count == 0)
                    throw new InvalidOperationException("No se encontraron bordes entre zona aislada y el resto (dimensiones muy chicas).");
                Shuffle(boundary, rng);
                var entrance = boundary[0];
                OpenWallBetween(floor, entrance.ax, entrance.ay, entrance.dir);
                var gateEdge = boundary.FirstOrDefault(e => !(e.ax == entrance.ax && e.ay == entrance.ay && e.dir == entrance.dir));
                if (gateEdge.Equals(default((int, int, Direction)))) gateEdge = entrance;

                gate = new ShortcutGate { Ax = gateEdge.ax, Ay = gateEdge.ay, DirFromA = gateEdge.dir, IsOpen = false };
                var (dx, dy) = gateEdge.dir.Offset();
                gate.Bx = gateEdge.ax + dx;
                gate.By = gateEdge.ay + dy;
                floor.Gates.Add(gate);
            }
            else
            {
                Carve(floor, (x, y) => !floor.Cells[x, y].IsPredefinedRoom, rng);
            }

            // 4. Start = farthest reachable cell from an outside-region seed cell; End = farthest cell from Start.
            //    Ninguno de los dos puede caer dentro de la zona aislada (el area "secreta" detras
            //    del atajo/switch): como la entrada permanente la conecta al resto del arbol, sin
            //    esta restriccion la celda mas lejana bien puede terminar ahi adentro. Para Start
            //    eso significaria arrancar la run parado en medio del secreto que se supone hay que
            //    descubrir explorando; para End (que PlaceStairsBetween usa como anclaje real de la
            //    escalera de bajada, ver mas abajo) seria peor -- la zona aislada dejaria de ser
            //    opcional, porque la escalera obligatoria para seguir bajando quedaria adentro.
            var outsideSeed = FirstCellMatching(floor, (x, y) => !floor.IsInIsolatedZone(x, y)
                && floor.Cells[x, y].Type == CellType.Normal && !floor.Cells[x, y].IsPredefinedRoom) ?? (0, 0);
            var (startPos, _) = FarthestCell(floor, outsideSeed, allowed: c => !floor.IsInIsolatedZone(c.Item1, c.Item2));
            var (endPos, _) = FarthestCell(floor, startPos, new HashSet<(int, int)> { startPos }, allowed: c => !floor.IsInIsolatedZone(c.Item1, c.Item2));
            floor.StartPos = startPos;
            floor.EndPos = endPos;
            floor.Cells[startPos.x, startPos.y].Type = CellType.Start;
            floor.Cells[endPos.x, endPos.y].Type = CellType.End;

            // 5. Secondary quest room: a dead-end (degree 1) cell, preferably outside region, not Start/End,
            //    y preferentemente sin quedar pegada (sin paso) a Start o End.
            var secondary = FindDeadEnd(floor, preferOutside: true,
                exclude: gate != null
                    ? new HashSet<(int, int)> { startPos, endPos, (gate.Bx, gate.By) }
                    : new HashSet<(int, int)> { startPos, endPos },
                avoidAdjacentTo: new HashSet<(int, int)> { startPos, endPos });
            floor.SecondaryQuestPos = secondary;
            floor.Cells[secondary.Item1, secondary.Item2].Type = CellType.SecondaryQuest;

            // 6. Shortcut switch: inside cell of the gate; if occupied, find nearest free inside cell.
            //    La pared entre Ax/Ay y Bx/By NUNCA se abre: queda como el "vacio" permanente entre
            //    ambos lados. El atajo, una vez activado, teletransporta entre el switch y el punto
            //    de llegada en vez de dejar caminar a traves de esa pared.
            if (gate != null)
            {
                var switchPos = FindFreeCellNear(floor, (gate.Bx, gate.By), c => floor.IsInIsolatedZone(c.Item1, c.Item2), new HashSet<(int, int)> { startPos, endPos, secondary },
                    avoidAdjacentTo: new HashSet<(int, int)> { startPos, endPos, secondary });
                floor.Cells[switchPos.Item1, switchPos.Item2].Type = CellType.ShortcutSwitch;
                floor.Cells[switchPos.Item1, switchPos.Item2].ControlledGateIndex = 0;
                gate.SwitchX = switchPos.Item1;
                gate.SwitchY = switchPos.Item2;

                var landingPos = FindFreeCellNear(floor, (gate.Ax, gate.Ay), c => !floor.IsInIsolatedZone(c.Item1, c.Item2), new HashSet<(int, int)> { startPos, endPos, secondary, switchPos },
                    avoidAdjacentTo: new HashSet<(int, int)> { startPos, endPos, secondary, switchPos });
                floor.Cells[landingPos.Item1, landingPos.Item2].Type = CellType.ShortcutLanding;
                floor.Cells[landingPos.Item1, landingPos.Item2].ControlledGateIndex = 0;
                gate.LandingX = landingPos.Item1;
                gate.LandingY = landingPos.Item2;
                RepairAdjacentImportantPoints(floor, startPos, endPos, secondary, switchPos, landingPos);
            }
            else
            {
                RepairAdjacentImportantPoints(floor, startPos, endPos, secondary);
            }

            return floor;
        }

        private void RepairAdjacentImportantPoints(DungeonFloor floor, params (int x, int y)[] points)
        {
            for (int i = 0; i < points.Length; i++)
            {
                for (int j = i + 1; j < points.Length; j++)
                {
                    foreach (var dir in DirectionExtensions.All)
                    {
                        var (ox, oy) = dir.Offset();
                        if (points[i].x + ox == points[j].x && points[i].y + oy == points[j].y && floor.Cells[points[i].x, points[i].y].HasWall(dir))
                        {
                            OpenWallBetween(floor, points[i].x, points[i].y, dir);
                        }
                    }
                }
            }
        }

        // ---------- Salas de autor con forma prediseñada ----------

        private const int MinPredefinedRoomsPerFloor = 2;
        private const int MaxPredefinedRoomsPerFloor = 4;

        // Intenta colocar entre Min y MaxPredefinedRoomsPerFloor salas de RoomTemplateLibrary.All
        // en posiciones libres del piso (nunca dentro de la zona aislada). Cada intento fallido
        // (no entro en ningun lado sin romper la conectividad del resto) simplemente se descarta --
        // en un piso chico o ya ocupado, terminar con menos salas de las pedidas (incluso 0) es un
        // resultado valido, nunca un error.
        private void PlacePredefinedRooms(DungeonFloor floor, Random rng)
        {
            int desired = rng.Next(MinPredefinedRoomsPerFloor, MaxPredefinedRoomsPerFloor + 1);
            int placed = 0;
            int attempts = 0;
            int maxAttempts = desired * 6; // presupuesto de intentos, no de exito garantizado

            // Celdas "de afuera" a las que ya se conecto la puerta de alguna sala ya colocada esta
            // pasada -- BUG encontrado con el harness de consola: sin reservarlas, una sala colocada
            // DESPUES podia estampar su propio piso o pilar justo arriba del conector de una sala
            // YA colocada, dejandola sin ninguna conexion real al resto del piso (mi chequeo de
            // conectividad solo mira si la grilla LIBRE sigue siendo un bloque, no sabe que esa
            // celda puntual era la unica entrada de otra sala). Se acumulan aca y cualquier sala
            // nueva las trata como si ya estuvieran ocupadas.
            var reservedConnectors = new HashSet<(int, int)>();

            // El interior del castillo siempre recibe sus tres salas reconocibles. Se distribuyen
            // entre el salon, la primera planta de sotano y el fondo del sotano; cada una elige su
            // plantilla segura o su variante con trampa de forma determinista para esta run.
            if (floor.Biome == 4)
            {
                // Una sala de cascada de lava puede aparecer en cualquiera de los pisos interiores
                // del bioma castillo; probabilidad por piso para que no se repita en todos. El patio
                // es Biome 3 y nunca entra en este bloque.
                if (rng.NextDouble() < 0.35)
                {
                    var lavafall = RoomTemplateLibrary.All.FirstOrDefault(t => t.Name == "CastleLavafall");
                    if (lavafall != null && TryPlaceRoom(floor, lavafall, rng, reservedConnectors)) placed++;
                }

                string roomPrefix = floor.BiomeFloorIndex switch
                {
                    0 => "CastleDining",
                    3 => "CastleKitchen",
                    4 => "CastlePantry",
                    _ => null,
                };
                if (roomPrefix != null)
                {
                    var themed = RoomTemplateLibrary.GetCastleVariant(roomPrefix, rng.Next(2) == 1);
                    if (themed != null && TryPlaceRoom(floor, themed, rng, reservedConnectors)) placed++;
                }
            }

            while (placed < desired && attempts < maxAttempts)
            {
                attempts++;
                var template = RoomTemplateLibrary.All[rng.Next(RoomTemplateLibrary.All.Length)];
                if (floor.Biome == 4 && template.Name.StartsWith("Castle")) continue;
                if (TryPlaceRoom(floor, template, rng, reservedConnectors)) placed++;
            }
        }

        private bool TryPlaceRoom(DungeonFloor floor, RoomTemplate template, Random rng, HashSet<(int, int)> reservedConnectors)
        {
            var positions = new List<(int x, int y)>();
            for (int ox = 0; ox <= floor.Width - template.Width; ox++)
                for (int oy = 0; oy <= floor.Height - template.Height; oy++)
                    positions.Add((ox, oy));
            if (positions.Count == 0) return false;
            Shuffle(positions, rng);

            foreach (var (ox, oy) in positions)
            {
                if (TryStampRoomAt(floor, template, ox, oy, reservedConnectors)) return true;
            }
            return false;
        }

        // Fuerza bruta por solapamiento (mismo espiritu que la colocacion de salas de Barony): si
        // la posicion es valida, la estampa de una y devuelve true; si no, no toca nada y devuelve
        // false para que TryPlaceRoom pruebe la siguiente posicion.
        private bool TryStampRoomAt(DungeonFloor floor, RoomTemplate template, int ox, int oy, HashSet<(int, int)> reservedConnectors)
        {
            var footprint = new HashSet<(int, int)>(); // celdas de piso caminable ('.' o '^')
            var pillars = new HashSet<(int, int)>(); // '#' DENTRO del rectangulo: obstaculo solido real
            for (int lx = 0; lx < template.Width; lx++)
            {
                for (int ly = 0; ly < template.Height; ly++)
                {
                    int gx = ox + lx, gy = oy + ly;
                    if (floor.IsInIsolatedZone(gx, gy)) return false;
                    if (floor.Cells[gx, gy].IsPredefinedRoom) return false;
                    if (reservedConnectors.Contains((gx, gy))) return false; // conector de OTRA sala, no se puede pisar
                    if (template.IsOpen(lx, ly)) footprint.Add((gx, gy));
                    else pillars.Add((gx, gy));
                }
            }

            foreach (var (dx, dy, dir) in template.Doors)
            {
                int gx = ox + dx, gy = oy + dy;
                var (offX, offY) = dir.Offset();
                int ex = gx + offX, ey = gy + offY;
                if (!floor.InBounds(ex, ey)) return false;
                if (floor.IsInIsolatedZone(ex, ey)) return false;
                if (floor.Cells[ex, ey].IsPredefinedRoom) return false;
                if (footprint.Contains((ex, ey))) return false; // la puerta no puede dar a la propia sala
            }

            // Salvavidas de conectividad: si estampar esta sala (piso Y pilares -- un pilar tambien
            // deja de ser parte de la region "libre" que Carve() recorre) partiera el resto del piso
            // en dos bolsones sin relacion, Carve() (que arranca de UNA sola celda semilla) solo
            // recorreria uno de los dos, dejando el otro como roca "Normal" con todas las paredes
            // cerradas para siempre -- un bolson roto que ni la poda ni la validacion final podrian
            // arreglar. Se descarta esta posicion entera antes de tocar nada si eso llegara a pasar.
            var reserved = new HashSet<(int, int)>(footprint);
            reserved.UnionWith(pillars);
            if (!OutsideRegionStaysConnectedWithout(floor, reserved)) return false;

            foreach (var (gx, gy) in footprint)
            {
                var cell = floor.Cells[gx, gy];
                cell.IsPredefinedRoom = true;
                cell.PredefinedRoomTemplateName = template.Name;
                int lx0 = gx - ox, ly0 = gy - oy;
                if (template.IsHazard(lx0, ly0)) cell.IsPredefinedRoomHazard = true;
                // Cofre de autor (ver RoomTemplate '$'): a proposito NO se agrega a
                // floor.TreasurePositions/TreasureRoomCells -- esas listas son del cupo GARANTIZADO
                // de EnsureTreasure (que exige que cada cofre quede en un punto muerto real, grado
                // 1, cosa que un cofre adentro de una sala normalmente NO es); esto es loot extra,
                // aparte, que se abre exactamente igual (mismo CellType.Treasure/TryInteract).
                if (template.IsReward(lx0, ly0))
                {
                    cell.Type = CellType.Treasure;
                    cell.IsTreasureRoom = true;
                }
                foreach (var dir in DirectionExtensions.All)
                {
                    var (offX, offY) = dir.Offset();
                    int lx = lx0 + offX, ly = ly0 + offY;
                    if (template.IsOpen(lx, ly))
                        cell.SetWall(dir, false);
                }
            }

            // Pilar de autor: la MISMA convencion que ya usa PruneToSparseMaze para el resto del
            // piso (celda "Void" = roca solida, no caminable, no exigida por ValidateDungeon via
            // CountNonVoid) -- asi el pilar se renderiza gratis con el bloque solido que
            // DungeonLevelBuilder ya construye para cualquier Void, sin necesitar geometria nueva.
            foreach (var (gx, gy) in pillars)
            {
                var cell = floor.Cells[gx, gy];
                cell.IsPredefinedRoom = true;
                cell.Type = CellType.Void;
            }

            foreach (var (dx, dy, dir) in template.Doors)
            {
                OpenWallBetween(floor, ox + dx, oy + dy, dir);
                var (offX, offY) = dir.Offset();
                reservedConnectors.Add((ox + dx + offX, oy + dy + offY));
            }

            return true;
        }

        // Flood fill sobre la grilla PLANA (todavia no hay ninguna pared tallada en la region de
        // afuera a esta altura -- PlacePredefinedRooms corre antes que Carve): confirma que, sacando
        // la zona aislada y toda sala de autor ya colocada (mas la candidata que se esta evaluando
        // ahora, footprintCandidate), lo que queda sigue siendo UN solo bloque conexo. Si lo es,
        // Carve() (spanning tree desde una sola semilla) garantiza despues que TODAS esas celdas
        // terminan en el mismo arbol -- misma logica que ya usan las dos regiones (afuera/zona
        // aislada) de este mismo metodo, aplicada a una tercera clase de "hueco" en el grid.
        private bool OutsideRegionStaysConnectedWithout(DungeonFloor floor, HashSet<(int, int)> footprintCandidate)
        {
            bool Free(int x, int y) => !floor.IsInIsolatedZone(x, y) && !floor.Cells[x, y].IsPredefinedRoom && !footprintCandidate.Contains((x, y));

            (int, int)? seed = null;
            int totalFree = 0;
            for (int x = 0; x < floor.Width; x++)
            {
                for (int y = 0; y < floor.Height; y++)
                {
                    if (!Free(x, y)) continue;
                    totalFree++;
                    seed ??= (x, y);
                }
            }
            if (totalFree == 0) return true;

            var visited = new HashSet<(int, int)> { seed.Value };
            var stack = new Stack<(int, int)>();
            stack.Push(seed.Value);
            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Pop();
                foreach (var dir in DirectionExtensions.All)
                {
                    var (ox, oy) = dir.Offset();
                    int nx = cx + ox, ny = cy + oy;
                    if (!floor.InBounds(nx, ny) || !Free(nx, ny) || visited.Contains((nx, ny))) continue;
                    visited.Add((nx, ny));
                    stack.Push((nx, ny));
                }
            }
            return visited.Count == totalFree;
        }

        private bool RectOverlapsPredefinedRoom(DungeonFloor floor, int rx, int ry, int rw, int rh)
        {
            for (int x = rx; x < rx + rw; x++)
                for (int y = ry; y < ry + rh; y++)
                    if (floor.Cells[x, y].IsPredefinedRoom) return true;
            return false;
        }

        // ---------- Caminos multiples entre Start y End ----------

        // El laberinto base (Carve, en GenerateFloor) es un arbol de expansion perfecto: por
        // definicion existe UN SOLO camino entre dos celdas cualesquiera. El foco del piso son sus
        // dos extremos -- Start por un lado, End por el otro -- asi que en vez de dejar una unica
        // ruta obligatoria entre ambos, esto abre unas pocas paredes extra cerca del camino critico
        // Start->End para que existan VARIAS formas reales de avanzar que se cruzan entre si
        // (bifurcan y se reencuentran mas adelante), sin tocar la zona aislada ni ninguna sala
        // especial. Rescatado y reimplementado desde feature/laberinto-caminos-cruzados (commit
        // 300e0e3, rama vieja nunca mergeada): la idea original seguia intacta, solo el codigo
        // estaba desactualizado (usaba CellType.Event, borrado hace tiempo con el sistema de
        // eventos). Cada pared abierta agrega un ciclo real al grafo -- los pilares de pacing
        // (PlacePacingPillars) ya saben detectar y esquivar ciclos con BFS al elegir donde poner el
        // candado obligatorio, asi que esto es seguro de combinar con el resto de la generacion
        // SIEMPRE que corra antes (ver el comentario en GenerateDungeon). Devuelve cuantos cruces
        // extra pudo abrir (0 a 3): en mapas chicos o muy densos puede no encontrar vecino valido en
        // alguno de los 3 puntos de bifurcacion -- no es un error, ver AddLoreCorridorRoom para el
        // mismo patron de fallback silencioso.
        private int AddCrossingPaths(DungeonFloor floor, Random rng)
        {
            var mainPath = FindPath(floor, floor.StartPos, floor.EndPos);
            if (mainPath.Count < 8) return 0; // camino muy corto, no hay lugar para cruces reales

            const int margin = 2;
            int[] forkIndices = { mainPath.Count / 4, mainPath.Count / 2, mainPath.Count * 3 / 4 };
            int opened = 0;

            foreach (int idx in forkIndices)
            {
                if (idx < margin || idx >= mainPath.Count - margin) continue;
                var (cx, cy) = mainPath[idx];
                var cell = floor.Cells[cx, cy];
                if (cell.Type != CellType.Normal) continue;

                var dirs = new List<Direction>(DirectionExtensions.All);
                Shuffle(dirs, rng);
                foreach (var dir in dirs)
                {
                    if (!cell.HasWall(dir)) continue; // ya conectado (o es el propio camino)
                    var (ox, oy) = dir.Offset();
                    int nx = cx + ox, ny = cy + oy;
                    if (!floor.InBounds(nx, ny)) continue;

                    var neighbor = floor.Cells[nx, ny];
                    if (neighbor.Type != CellType.Normal) continue;
                    if (neighbor.IsBossRoom || neighbor.IsTrapRoom || neighbor.IsTreasureRoom || neighbor.IsPredefinedRoom) continue;
                    if (floor.IsInIsolatedZone(nx, ny) != floor.IsInIsolatedZone(cx, cy)) continue;

                    OpenWallBetween(floor, cx, cy, dir);
                    opened++;
                    break;
                }
            }

            return opened;
        }

        // ---------- Fusion ocasional de celdas (camaras mas grandes e irregulares) ----------

        // Pedido puntual inspirado en un video sobre diseño de niveles (Spelunky/Isaac/Risk of
        // Rain, y el prototipo propio del autor: darle a cada puerta 50% de chance de NO aparecer
        // fusiono cuartitos chicos en camaras mas grandes con un cambio casi gratis). Adaptado a
        // ESTE laberinto: abrir CUALQUIER pared al azar rompería la identidad de corredor angosto
        // tipo Etrian Odyssey (ver DungeonSettings.voidFraction), asi que esto es mas selectivo --
        // solo tira la moneda en paredes entre 2 celdas Normal comunes (nunca sala de jefe/trampas/
        // autor, mismo criterio que AddCrossingPaths) que YA estan conectadas al resto por OTRO
        // lado (grado >= 2 ANTES de esta pasada). Eso es lo que garantiza no comerse un punto
        // muerto real: el cofre garantizado, la Puerta Fria y la palanca del candado (todos corren
        // DESPUES, ver GenerateDungeon) dependen de que sigan existiendo puntas sueltas de verdad.
        // Mismo lugar en el pipeline que AddCrossingPaths y misma razon: sus ciclos nuevos tienen
        // que existir ANTES de que PlacePacingPillars elija donde poner el candado (su verificacion
        // por BFS de "cortar esta arista desconecta todo" solo es valida si ya conoce todos los
        // ciclos del piso).
        private int BraidExtraOpenings(DungeonFloor floor, Random rng, float chance)
        {
            if (chance <= 0f) return 0;

            // Snapshot de aristas candidatas ANTES de abrir nada: los grados de todas las celdas se
            // miden sobre el laberinto tal cual esta al entrar aca, asi abrir una arista nunca hace
            // que otra candidata (ya evaluada) quede invalida a mitad de la pasada.
            var candidates = new List<(int x, int y, Direction dir)>();
            for (int x = 0; x < floor.Width; x++)
            {
                for (int y = 0; y < floor.Height; y++)
                {
                    var cell = floor.Cells[x, y];
                    if (cell.Type != CellType.Normal || cell.IsPredefinedRoom) continue;
                    if (Degree(floor, x, y) < 2) continue; // punta suelta real: nunca tocarla

                    // Solo Norte/Este por celda, igual que DungeonLevelBuilder.BuildWall: cada pared
                    // compartida se evalua una sola vez (desde el lado que le toca), no dos.
                    foreach (var dir in new[] { Direction.North, Direction.East })
                    {
                        if (!cell.HasWall(dir)) continue; // ya conectados de este lado
                        var (ox, oy) = dir.Offset();
                        int nx = x + ox, ny = y + oy;
                        if (!floor.InBounds(nx, ny)) continue;

                        var neighbor = floor.Cells[nx, ny];
                        if (neighbor.Type != CellType.Normal || neighbor.IsPredefinedRoom) continue;
                        if (Degree(floor, nx, ny) < 2) continue;
                        if (floor.IsInIsolatedZone(nx, ny) != floor.IsInIsolatedZone(x, y)) continue;

                        candidates.Add((x, y, dir));
                    }
                }
            }

            int opened = 0;
            foreach (var (x, y, dir) in candidates)
            {
                if (rng.NextDouble() >= chance) continue;
                OpenWallBetween(floor, x, y, dir);
                opened++;
            }
            return opened;
        }

        // ---------- Sala de jefe ----------

        // Fusiona un bloque rectangular de tamano/posicion aleatorios en una unica sala grande
        // (abre todas las paredes internas del bloque) y coloca al jefe en el centro. El tamano y la
        // posicion varian por piso/semilla para que cada sala de jefe resulte distinta.
        private bool AddBossRoom(DungeonFloor floor, Random rng)
        {
            int minSize = 3;
            int maxSize = Math.Max(minSize, Math.Min(6, Math.Min(floor.Width, floor.Height) - 1));

            for (int attempt = 0; attempt < 40; attempt++)
            {
                int rw = rng.Next(minSize, maxSize + 1);
                int rh = rng.Next(minSize, maxSize + 1);
                if (rw > floor.Width || rh > floor.Height) continue;

                int rx = rng.Next(0, floor.Width - rw + 1);
                int ry = rng.Next(0, floor.Height - rh + 1);

                if (RectOverlapsIsolatedZone(floor, rx, ry, rw, rh)) continue;
                if (RectOverlapsSpecialCells(floor, rx, ry, rw, rh)) continue;
                if (RectOverlapsPredefinedRoom(floor, rx, ry, rw, rh)) continue;

                var cells = new List<(int, int)>();
                for (int x = rx; x < rx + rw; x++)
                {
                    for (int y = ry; y < ry + rh; y++)
                    {
                        cells.Add((x, y));
                        var cell = floor.Cells[x, y];
                        cell.IsBossRoom = true;
                        foreach (var dir in DirectionExtensions.All)
                        {
                            var (ox, oy) = dir.Offset();
                            int nx = x + ox, ny = y + oy;
                            if (nx >= rx && nx < rx + rw && ny >= ry && ny < ry + rh)
                                cell.SetWall(dir, false);
                        }
                    }
                }

                floor.BossRoomCells = cells;
                floor.BossRoomMinX = rx;
                floor.BossRoomMinY = ry;
                floor.BossRoomMaxX = rx + rw - 1;
                floor.BossRoomMaxY = ry + rh - 1;

                int cx = rx + rw / 2;
                int cy = ry + rh / 2;
                floor.Cells[cx, cy].Type = CellType.Boss;
                floor.BossPos = (cx, cy);

                return true;
            }

            return false;
        }

        // Respaldo para el jefe obligatorio del bosque: en mapas demasiado ocupados puede no
        // caber la sala grande. Coloca el marcador en una celda normal alcanzable y la registra
        // como sala de jefe para que el combate, el render y la escalera usen la misma posicion.
        private bool AddFallbackBossMarker(DungeonFloor floor)
        {
            foreach (var door in floor.LockedDoors) SetDoorWallState(floor, door, true);
            var reachable = BfsReachable(floor, floor.StartPos);
            var candidates = reachable
                .Where(c => floor.Cells[c.Item1, c.Item2].Type == CellType.Normal
                    && !floor.Cells[c.Item1, c.Item2].IsPredefinedRoom
                    && !floor.Cells[c.Item1, c.Item2].IsTrapRoom
                    && !floor.Cells[c.Item1, c.Item2].IsPuzzleTile)
                .OrderByDescending(c => Math.Abs(c.Item1 - floor.StartPos.x) + Math.Abs(c.Item2 - floor.StartPos.y))
                .ToList();
            foreach (var door in floor.LockedDoors) SetDoorWallState(floor, door, false);
            if (candidates.Count == 0) return false;

            var pos = candidates[0];
            var roomCells = new List<(int, int)> { pos };
            // Reserva unas celdas conectadas para que, si este piso tiene continuacion,
            // PlaceStairsBetween todavia encuentre sitio para la escalera dentro de la sala.
            bool expanded;
            do
            {
                expanded = false;
                foreach (var c in roomCells.ToArray())
                {
                    foreach (var dir in DirectionExtensions.All)
                    {
                        if (roomCells.Count >= 4) break;
                        var (ox, oy) = dir.Offset();
                        int nx = c.Item1 + ox, ny = c.Item2 + oy;
                        if (!floor.InBounds(nx, ny) || floor.Cells[c.Item1, c.Item2].HasWall(dir)) continue;
                        var next = (nx, ny);
                        if (!reachable.Contains(next) || roomCells.Contains(next)) continue;
                        var nextCell = floor.Cells[nx, ny];
                        if (nextCell.Type != CellType.Normal || nextCell.IsPredefinedRoom || nextCell.IsTrapRoom || nextCell.IsPuzzleTile) continue;
                        roomCells.Add(next);
                        nextCell.IsBossRoom = true;
                        expanded = true;
                    }
                    if (roomCells.Count >= 4) break;
                }
            } while (expanded && roomCells.Count < 4);

            floor.Cells[pos.Item1, pos.Item2].IsBossRoom = true;
            floor.Cells[pos.Item1, pos.Item2].Type = CellType.Boss;
            floor.BossRoomCells = roomCells;
            floor.BossRoomMinX = roomCells.Min(c => c.Item1);
            floor.BossRoomMinY = roomCells.Min(c => c.Item2);
            floor.BossRoomMaxX = roomCells.Max(c => c.Item1);
            floor.BossRoomMaxY = roomCells.Max(c => c.Item2);
            floor.BossPos = pos;
            return true;
        }

        private bool RectOverlapsIsolatedZone(DungeonFloor floor, int rx, int ry, int rw, int rh)
        {
            if (floor.IsoMinX > floor.IsoMaxX || floor.IsoMinY > floor.IsoMaxY) return false;
            return rx <= floor.IsoMaxX && rx + rw - 1 >= floor.IsoMinX
                && ry <= floor.IsoMaxY && ry + rh - 1 >= floor.IsoMinY;
        }

        private bool RectOverlapsSpecialCells(DungeonFloor floor, int rx, int ry, int rw, int rh)
        {
            (int x, int y)[] special = { floor.StartPos, floor.EndPos, floor.SecondaryQuestPos };
            foreach (var (x, y) in special)
            {
                if (x >= rx && x < rx + rw && y >= ry && y < ry + rh) return true;
            }
            foreach (var gate in floor.Gates)
            {
                if (gate.Bx >= rx && gate.Bx < rx + rw && gate.By >= ry && gate.By < ry + rh) return true;
            }
            // La Puerta Fria (piso 0 del Bioma 1, ver PlaceBiomeGate) no estaba aca porque hasta
            // ahora ninguna sala de pistas caia en el piso 0 -- PickLoreCorridorFloors la manda ahi
            // a proposito ahora, asi que sin esto una sala de pistas podia superponerse y corromper
            // la celda sellada o su punto de acercamiento.
            if (floor.BiomeGatePos.HasValue)
            {
                var (gx, gy) = floor.BiomeGatePos.Value;
                if (gx >= rx && gx < rx + rw && gy >= ry && gy < ry + rh) return true;
            }
            if (floor.BiomeGateApproachPos.HasValue)
            {
                var (ax, ay) = floor.BiomeGateApproachPos.Value;
                if (ax >= rx && ax < rx + rw && ay >= ry && ay < ry + rh) return true;
            }
            return false;
        }

        // ---------- Sala de trampas ----------

        // Fusiona un bloque rectangular GRANDE (4x4, 4x3 o mas -- el lado mayor nunca es menor a 4)
        // en una sala abierta, mismo mecanismo que AddBossRoom, y le pone una trampa adentro al
        // azar (ver DungeonFloor.TrapKind): ArrowSweep (una fila o columna entera de la sala es la
        // linea de tiro de una maquina de flechas) o SpikeCells (~40% de las celdas de la sala,
        // sueltas al azar, son picos). Opcional -- no todos los pisos tienen una (ver
        // GenerateDungeon) y puede no encontrar lugar en mapas chicos/ya ocupados (false, sin tocar
        // nada).
        private bool AddTrapRoom(DungeonFloor floor, Random rng)
        {
            int minSide = 3, maxSide = 5;
            if (Math.Min(floor.Width, floor.Height) - 1 < minSide) return false;

            for (int attempt = 0; attempt < 40; attempt++)
            {
                int rw = rng.Next(minSide, maxSide + 1);
                int rh = rng.Next(minSide, maxSide + 1);
                if (Math.Max(rw, rh) < 4) continue; // nunca un cuadrante chico tipo 3x3: 4x4/4x3 o mas
                if (rw > floor.Width || rh > floor.Height) continue;

                int rx = rng.Next(0, floor.Width - rw + 1);
                int ry = rng.Next(0, floor.Height - rh + 1);

                if (RectOverlapsIsolatedZone(floor, rx, ry, rw, rh)) continue;
                if (RectOverlapsSpecialCells(floor, rx, ry, rw, rh)) continue;
                if (RectOverlapsPredefinedRoom(floor, rx, ry, rw, rh)) continue;

                bool blocked = false;
                for (int x = rx; x < rx + rw && !blocked; x++)
                    for (int y = ry; y < ry + rh; y++)
                        if (floor.Cells[x, y].IsBossRoom) { blocked = true; break; }
                if (blocked) continue;

                // TrapKind y (si es ArrowSweep) la geometria/pared del disparador se deciden ANTES
                // de mutar nada del piso: si el disparador no puede quedar sobre una pared de
                // verdad, descartamos este intento entero y probamos otra ubicacion mas abajo, sin
                // dejar celdas o paredes a medio abrir de un intento fallido.
                var trapKind = rng.Next(2) == 0 ? TrapKind.ArrowSweep : TrapKind.SpikeCells;
                bool horizontal = rng.Next(2) == 0;
                var lineCells = new List<(int, int)>();
                Direction startDir = default, endDir = default;
                bool fromStart = true;
                if (trapKind == TrapKind.ArrowSweep)
                {
                    if (horizontal)
                    {
                        int sweepY = ry + rh / 2;
                        for (int x = rx; x < rx + rw; x++) lineCells.Add((x, sweepY));
                    }
                    else
                    {
                        int sweepX = rx + rw / 2;
                        for (int y = ry; y < ry + rh; y++) lineCells.Add((sweepX, y));
                    }

                    // El disparador (ver Gameplay/TrapDisparadorController) vive en la pared de UNO
                    // de los dos extremos de la linea, disparando hacia el otro extremo -- asi la
                    // flecha recorre TODA la sala y el jugador tiene el tiempo real de vuelo para
                    // salir de la linea antes de que llegue. TIENE que quedar sobre una pared de
                    // verdad (HasWall == true), nunca sobre un hueco ya abierto por un corredor,
                    // para que el Perforador siempre pueda apuntarle y destruirlo.
                    startDir = horizontal ? Direction.East : Direction.North;
                    endDir = horizontal ? Direction.West : Direction.South;
                    var startCell = lineCells[0];
                    var endCell = lineCells[lineCells.Count - 1];
                    bool startHasWall = floor.Cells[startCell.Item1, startCell.Item2].HasWall(startDir.Opposite());
                    bool endHasWall = floor.Cells[endCell.Item1, endCell.Item2].HasWall(endDir.Opposite());
                    if (!startHasWall && !endHasWall) continue; // ningun extremo tiene pared solida: reintentar en otra ubicacion

                    fromStart = startHasWall && endHasWall ? rng.Next(2) == 0 : startHasWall;
                }

                var cells = new List<(int, int)>();
                for (int x = rx; x < rx + rw; x++)
                {
                    for (int y = ry; y < ry + rh; y++)
                    {
                        cells.Add((x, y));
                        var cell = floor.Cells[x, y];
                        cell.IsTrapRoom = true;
                        foreach (var dir in DirectionExtensions.All)
                        {
                            var (ox, oy) = dir.Offset();
                            int nx = x + ox, ny = y + oy;
                            if (nx >= rx && nx < rx + rw && ny >= ry && ny < ry + rh)
                                cell.SetWall(dir, false);
                        }
                    }
                }

                floor.TrapRoomCells = cells;
                floor.TrapKind = trapKind;

                var trapCells = new List<(int, int)>();
                if (trapKind == TrapKind.ArrowSweep)
                {
                    trapCells = lineCells;
                    floor.TrapDisparadorPos = fromStart ? lineCells[0] : lineCells[lineCells.Count - 1];
                    floor.TrapDisparadorDir = fromStart ? startDir : endDir;
                    floor.TrapArrowPath = new List<(int, int)>(lineCells);
                    if (!fromStart) floor.TrapArrowPath.Reverse();
                }
                else
                {
                    var shuffled = new List<(int, int)>(cells);
                    Shuffle(shuffled, rng);
                    int count = Math.Max(2, cells.Count * 2 / 5);
                    trapCells.AddRange(shuffled.GetRange(0, Math.Min(count, shuffled.Count)));
                }

                foreach (var (tx, ty) in trapCells) floor.Cells[tx, ty].IsTrapCell = true;
                floor.TrapCells = trapCells;
                return true;
            }
            return false;
        }

        // ---------- Poda de pasillos (asi el mapa deja celdas como vacio real, no un laberinto perfecto) ----------

        // Convierte en "Void" (roca solida, no caminable, no se renderiza) puntas muertas del arbol
        // de expansion que no son necesarias para llegar a ningun punto importante. Como solo se
        // podan hojas (grado 1) que no estan protegidas, el camino entre dos puntos protegidos
        // cualesquiera SIEMPRE sigue intacto (en un arbol, todo nodo intermedio de un camino tiene
        // grado >= 2, nunca puede volverse hoja). Devuelve cuantas celdas quedaron vacias.
        private int PruneToSparseMaze(DungeonFloor floor, Random rng, float voidFraction)
        {
            if (voidFraction <= 0f) return 0;

            // Podar tiene que "ver" el arbol como si todos los candados ya estuvieran abiertos: si
            // se poda con la puerta todavia cerrada, el subarbol entero que queda del otro lado
            // aparenta ser una cadena de puntas muertas (cada celda pierde su unica conexion "de
            // entrada") y la poda se lo come en cascada convirtiendolo en Void PERMANENTE -- ni
            // siquiera activar la palanca despues puede recuperar celdas que ya son roca solida.
            foreach (var door in floor.LockedDoors) SetDoorWallState(floor, door, true);

            var protectedCells = new HashSet<(int, int)> { floor.StartPos, floor.EndPos, floor.SecondaryQuestPos };
            if (floor.HasBossRoom)
                foreach (var c in floor.BossRoomCells) protectedCells.Add(c);
            if (floor.HasTrapRoom)
                foreach (var c in floor.TrapRoomCells) protectedCells.Add(c);
            if (floor.HasLoreCorridor)
                foreach (var c in floor.LoreCorridorRoomCells) protectedCells.Add(c);
            foreach (var gate in floor.Gates)
            {
                protectedCells.Add((gate.SwitchX, gate.SwitchY));
                protectedCells.Add((gate.LandingX, gate.LandingY));
            }
            foreach (var door in floor.LockedDoors)
            {
                protectedCells.Add((door.DoorX, door.DoorY));
                protectedCells.Add((door.LeverX, door.LeverY));
            }
            if (floor.TreasureRoomCells != null)
                foreach (var c in floor.TreasureRoomCells) protectedCells.Add(c);
            if (floor.CastleGatePos.HasValue) protectedCells.Add(floor.CastleGatePos.Value);
            // La boveda de cascada (ver AddWaterfallVault) es un punto muerto real A PROPOSITO --
            // sin proteger, la poda se la come como a cualquier otro callejon sin salida sobrante.
            if (floor.WaterfallVaultPos.HasValue) protectedCells.Add(floor.WaterfallVaultPos.Value);
            // Tumbas del Bioma de Cuevas (ver AddTombs) son puntos muertos reales A PROPOSITO --
            // mismo motivo que el cofre comun de arriba, sin proteger la poda se las come igual.
            if (floor.TombPositions != null)
                foreach (var t in floor.TombPositions) protectedCells.Add(t);
            // Trampas de goblins (ver AddGoblinTraps): a diferencia de lo anterior, la MAYORIA cae
            // sobre celdas de grado >= 2 (candidatas = cualquier Normal libre, no solo puntos
            // muertos), asi que esto rara vez hace falta -- pero si alguna SI cayo justo en un
            // callejon sin salida, protegerla evita perderla en silencio.
            for (int x = 0; x < floor.Width; x++)
                for (int y = 0; y < floor.Height; y++)
                    if (floor.Cells[x, y].IsGoblinTrap) protectedCells.Add((x, y));
            // Salas de autor con forma prediseñada (ver PlacePredefinedRooms): una celda de borde
            // angosto de la plantilla (ej. la punta de un brazo en L) puede tener grado 1 sin ser un
            // callejon sin salida "real" del laberinto -- si no se protege, la poda se la come igual
            // que a cualquier otra punta muerta y rompe la forma de autor.
            for (int x = 0; x < floor.Width; x++)
                for (int y = 0; y < floor.Height; y++)
                    if (floor.Cells[x, y].IsPredefinedRoom) protectedCells.Add((x, y));
            // La celda desde la que se perfora la Puerta Fria (ver PlaceBiomeGate) tiene que
            // seguir siendo una celda real caminable normal -- si la poda la come, la puerta queda
            // sellada de los DOS lados y ya no hay forma de encontrarla ni con el Perforador.
            if (floor.BiomeGateApproachPos.HasValue) protectedCells.Add(floor.BiomeGateApproachPos.Value);

            // Pedido puntual: la zona aislada del piso que aloja la escalera al Bioma de Cuevas
            // (ver PlaceCaveBiomeExit) tiene que sentirse como una mini-cueva COMPLETA que ocupa
            // todo su cuadrante -- no un laberinto ralo con agujeros de roca solida como el resto
            // del piso. Se protege ENTERA de la poda (nunca se conviernte en Void), a diferencia
            // de la zona aislada "comun" (sin escalera, en cualquier otro piso), que sigue
            // podandose igual que siempre.
            if (floor.CaveBiomeExitPos.HasValue)
            {
                for (int x = floor.IsoMinX; x <= floor.IsoMaxX; x++)
                    for (int y = floor.IsoMinY; y <= floor.IsoMaxY; y++)
                        protectedCells.Add((x, y));
            }

            int totalNormal = 0;
            for (int x = 0; x < floor.Width; x++)
                for (int y = 0; y < floor.Height; y++)
                    if (floor.Cells[x, y].Type == CellType.Normal) totalNormal++;

            int targetVoidCount = (int)(totalNormal * Math.Clamp(voidFraction, 0f, 0.85f));
            int voided = 0;

            while (voided < targetVoidCount)
            {
                var leaves = new List<(int, int)>();
                for (int x = 0; x < floor.Width; x++)
                {
                    for (int y = 0; y < floor.Height; y++)
                    {
                        if (floor.Cells[x, y].Type != CellType.Normal) continue;
                        if (protectedCells.Contains((x, y))) continue;
                        if (Degree(floor, x, y) == 1) leaves.Add((x, y));
                    }
                }

                if (leaves.Count == 0) break;

                Shuffle(leaves, rng);
                int take = Math.Min(leaves.Count, targetVoidCount - voided);
                for (int i = 0; i < take; i++)
                {
                    PruneCell(floor, leaves[i]);
                    voided++;
                }
            }

            foreach (var door in floor.LockedDoors) SetDoorWallState(floor, door, false);

            return voided;
        }

        private void PruneCell(DungeonFloor floor, (int x, int y) pos)
        {
            var cell = floor.Cells[pos.x, pos.y];
            foreach (var dir in DirectionExtensions.All)
            {
                if (!cell.HasWall(dir))
                {
                    var (ox, oy) = dir.Offset();
                    int nx = pos.x + ox, ny = pos.y + oy;
                    if (floor.InBounds(nx, ny))
                        floor.Cells[nx, ny].SetWall(dir.Opposite(), true);
                }
                cell.SetWall(dir, true);
            }
            cell.Type = CellType.Void;
        }

        // ---------- Maze carving (iterative recursive backtracker) ----------

        private void Carve(DungeonFloor floor, Func<int, int, bool> inRegion, Random rng)
        {
            (int x, int y)? start = FirstCellMatching(floor, inRegion);
            if (start == null) return;

            var visited = new HashSet<(int, int)>();
            var stack = new Stack<(int, int)>();
            stack.Push(start.Value);
            visited.Add(start.Value);

            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Peek();
                var neighbors = new List<(int nx, int ny, Direction dir)>();
                foreach (var dir in DirectionExtensions.All)
                {
                    var (ox, oy) = dir.Offset();
                    int nx = cx + ox, ny = cy + oy;
                    if (floor.InBounds(nx, ny) && inRegion(nx, ny) && !visited.Contains((nx, ny)))
                        neighbors.Add((nx, ny, dir));
                }

                if (neighbors.Count == 0)
                {
                    stack.Pop();
                    continue;
                }

                var pick = neighbors[rng.Next(neighbors.Count)];
                OpenWallBetween(floor, cx, cy, pick.dir);
                visited.Add((pick.nx, pick.ny));
                stack.Push((pick.nx, pick.ny));
            }
        }

        private void OpenWallBetween(DungeonFloor floor, int x, int y, Direction dir)
        {
            var (ox, oy) = dir.Offset();
            int nx = x + ox, ny = y + oy;
            floor.Cells[x, y].SetWall(dir, false);
            floor.Cells[nx, ny].SetWall(dir.Opposite(), false);
        }

        // Item "Perforador": intenta abrir un paso PERMANENTE (para esta run) a traves de la pared
        // en la direccion dada desde (x,y). Solo funciona si hay una pared ahi y del otro lado hay
        // una celda real dentro del mapa (no Void, no roca fuera de los limites); si no, no hace
        // nada y devuelve false (para no gastar el item en vano).
        public bool TryDrillWall(DungeonFloor floor, int x, int y, Direction dir)
        {
            if (!floor.InBounds(x, y)) return false;
            var cell = floor.Cells[x, y];
            if (!cell.HasWall(dir)) return false;

            var (ox, oy) = dir.Offset();
            int nx = x + ox, ny = y + oy;
            if (!floor.InBounds(nx, ny)) return false;
            if (floor.Cells[nx, ny].Type == CellType.Void) return false;

            OpenWallBetween(floor, x, y, dir);
            return true;
        }

        // Perforador apuntado exactamente a la pared donde vive el disparador de flechas de la
        // sala de trampas (ver DungeonFloor.TrapDisparadorPos/Dir). A diferencia de TryDrillWall,
        // esto NO exige que haya una celda real del otro lado (la maquina suele estar montada
        // sobre roca solida de borde) -- destruye la maquina sin abrir ningun paso, apagando esa
        // trampa para el resto de la run. Devuelve false si esa pared no es la del disparador, si
        // el piso no es ArrowSweep, o si ya estaba destruido.
        public bool TryDestroyTrapDisparador(DungeonFloor floor, int x, int y, Direction dir)
        {
            if (!floor.HasTrapRoom || floor.TrapKind != TrapKind.ArrowSweep || floor.TrapDisabled) return false;
            if (floor.TrapDisparadorPos.x != x || floor.TrapDisparadorPos.y != y) return false;
            if (dir != floor.TrapDisparadorDir.Opposite()) return false;

            floor.TrapDisabled = true;
            return true;
        }

        // Activa el atajo: no abre ninguna pared (el vacio entre el switch y el punto de llegada es
        // permanente), solo habilita el teletransporte entre ambos extremos.
        public void OpenGate(DungeonFloor floor, int gateIndex)
        {
            floor.Gates[gateIndex].IsOpen = true;
        }

        // Si la celda (x,y) es un extremo de atajo activo, devuelve el otro extremo para teletransportar.
        public bool TryGetTeleportTarget(DungeonFloor floor, int x, int y, out int targetX, out int targetY)
        {
            targetX = -1;
            targetY = -1;
            var cell = floor.Cells[x, y];
            if (cell.Type != CellType.ShortcutSwitch && cell.Type != CellType.ShortcutLanding) return false;
            if (cell.ControlledGateIndex < 0 || cell.ControlledGateIndex >= floor.Gates.Count) return false;

            var gate = floor.Gates[cell.ControlledGateIndex];
            if (!gate.IsOpen) return false;

            if (cell.Type == CellType.ShortcutSwitch) { targetX = gate.LandingX; targetY = gate.LandingY; }
            else { targetX = gate.SwitchX; targetY = gate.SwitchY; }
            return true;
        }

        // ---------- Helpers ----------

        private List<(int ax, int ay, Direction dir)> FindBoundaryEdges(DungeonFloor floor)
        {
            var result = new List<(int, int, Direction)>();
            for (int x = 0; x < floor.Width; x++)
            {
                for (int y = 0; y < floor.Height; y++)
                {
                    bool aInside = floor.IsInIsolatedZone(x, y);
                    // El ancla "de afuera" (entrada permanente O gate del atajo, ver GenerateFloor)
                    // tiene que ser una celda comun del arbol de expansion de Carve(): una celda de
                    // sala predefinida (ver PlacePredefinedRooms) solo esta conectada al resto por
                    // SUS puertas declaradas, y un pilar de esa misma sala (Void, ver
                    // TryStampRoomAt) no tiene NINGUNA conexion real -- si cualquiera de las dos
                    // terminara siendo este ancla, la zona aislada entera quedaria colgando de una
                    // celda sin camino de vuelta al resto del piso. BUG encontrado con el harness de
                    // consola: sin este chequeo, ValidateDungeon reportaba la zona aislada completa
                    // como inalcanzable en un puñado de semillas.
                    if (!aInside && floor.Cells[x, y].IsPredefinedRoom) continue;
                    foreach (var dir in DirectionExtensions.All)
                    {
                        var (ox, oy) = dir.Offset();
                        int nx = x + ox, ny = y + oy;
                        if (!floor.InBounds(nx, ny)) continue;
                        bool bInside = floor.IsInIsolatedZone(nx, ny);
                        if (aInside != bInside && !aInside)
                            result.Add((x, y, dir)); // record edge with A = outside cell
                    }
                }
            }
            return result;
        }

        private (int, int)? FirstCellMatching(DungeonFloor floor, Func<int, int, bool> pred)
        {
            for (int x = 0; x < floor.Width; x++)
                for (int y = 0; y < floor.Height; y++)
                    if (pred(x, y)) return (x, y);
            return null;
        }

        // Dos celdas distintas e importantes (Start/End/mision secundaria/switch/etc.) nunca deben
        // quedar pegadas por una sola pared: si son vecinas en la grilla, tiene que haber paso entre
        // ellas (forman una sola zona) o no ser vecinas en absoluto. No hay forma de meter un vacio
        // "entre medio" de dos celdas que ya son adyacentes, asi que evitamos el caso eligiendo otra
        // celda candidata en vez de esa.
        private bool IsAdjacentUnconnected(DungeonFloor floor, (int x, int y) a, (int x, int y) b)
        {
            foreach (var dir in DirectionExtensions.All)
            {
                var (ox, oy) = dir.Offset();
                if (a.x + ox == b.x && a.y + oy == b.y)
                    return floor.Cells[a.x, a.y].HasWall(dir);
            }
            return false;
        }

        private bool ViolatesAdjacency(DungeonFloor floor, (int x, int y) candidate, HashSet<(int, int)> avoid)
        {
            if (avoid == null) return false;
            foreach (var p in avoid)
                if (IsAdjacentUnconnected(floor, candidate, p)) return true;
            return false;
        }

        // allowed: si se pasa, solo las celdas que cumplen el predicado pueden ganar como resultado
        // (bestOverall/bestSafe) -- igual se siguen recorriendo/encolando todas para el BFS, solo
        // se descartan como CANDIDATO final. `from` nunca se filtra por este predicado (es el punto
        // de partida, se asume valido de entrada).
        private ((int x, int y) pos, int dist) FarthestCell(DungeonFloor floor, (int x, int y) from, HashSet<(int, int)> avoidAdjacentTo = null, Func<(int, int), bool> allowed = null)
        {
            var dist = new Dictionary<(int, int), int>();
            var queue = new Queue<(int, int)>();
            dist[from] = 0;
            queue.Enqueue(from);
            (int, int) bestOverall = from;
            int bestOverallDist = 0;
            (int, int)? bestSafe = null;
            int bestSafeDist = -1;

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                var cell = floor.Cells[cur.Item1, cur.Item2];
                foreach (var dir in DirectionExtensions.All)
                {
                    if (cell.HasWall(dir)) continue;
                    var (ox, oy) = dir.Offset();
                    var next = (cur.Item1 + ox, cur.Item2 + oy);
                    if (!floor.InBounds(next.Item1, next.Item2) || dist.ContainsKey(next)) continue;
                    dist[next] = dist[cur] + 1;
                    bool isAllowed = allowed == null || allowed(next);
                    if (isAllowed && dist[next] > bestOverallDist) { bestOverallDist = dist[next]; bestOverall = next; }
                    if (isAllowed && dist[next] > bestSafeDist && !ViolatesAdjacency(floor, next, avoidAdjacentTo))
                    {
                        bestSafeDist = dist[next];
                        bestSafe = next;
                    }
                    queue.Enqueue(next);
                }
            }

            if (bestSafe.HasValue) return (bestSafe.Value, bestSafeDist);
            return (bestOverall, bestOverallDist);
        }

        public HashSet<(int, int)> BfsReachable(DungeonFloor floor, (int x, int y) from)
        {
            var visited = new HashSet<(int, int)> { from };
            var queue = new Queue<(int, int)>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                var cell = floor.Cells[cur.Item1, cur.Item2];
                foreach (var dir in DirectionExtensions.All)
                {
                    if (cell.HasWall(dir)) continue;
                    var (ox, oy) = dir.Offset();
                    var next = (cur.Item1 + ox, cur.Item2 + oy);
                    if (!floor.InBounds(next.Item1, next.Item2) || visited.Contains(next)) continue;
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }
            return visited;
        }

        private int Degree(DungeonFloor floor, int x, int y)
        {
            int d = 0;
            foreach (var dir in DirectionExtensions.All)
                if (!floor.Cells[x, y].HasWall(dir)) d++;
            return d;
        }

        private int CountNonVoid(DungeonFloor floor)
        {
            int c = 0;
            for (int x = 0; x < floor.Width; x++)
                for (int y = 0; y < floor.Height; y++)
                    if (floor.Cells[x, y].Type != CellType.Void) c++;
            return c;
        }

        private (int, int) FindDeadEnd(DungeonFloor floor, bool preferOutside, HashSet<(int, int)> exclude, HashSet<(int, int)> avoidAdjacentTo = null)
        {
            var candidates = new List<(int, int)>();
            for (int x = 0; x < floor.Width; x++)
            {
                for (int y = 0; y < floor.Height; y++)
                {
                    if (exclude.Contains((x, y))) continue;
                    if (floor.Cells[x, y].IsPredefinedRoom) continue;
                    if (preferOutside && floor.IsInIsolatedZone(x, y)) continue;
                    if (Degree(floor, x, y) == 1) candidates.Add((x, y));
                }
            }
            if (candidates.Count == 0)
            {
                // fallback: allow isolated zone / any degree-1 cell.
                for (int x = 0; x < floor.Width; x++)
                    for (int y = 0; y < floor.Height; y++)
                        if (!exclude.Contains((x, y)) && !floor.Cells[x, y].IsPredefinedRoom && Degree(floor, x, y) == 1)
                            candidates.Add((x, y));
            }
            if (candidates.Count == 0)
                throw new InvalidOperationException("No se encontro celda sin salida para la mision secundaria.");

            // Preferimos una que no quede pegada (sin paso) a otro punto importante como Start/End.
            int safeIndex = candidates.FindIndex(c => !ViolatesAdjacency(floor, c, avoidAdjacentTo));
            return safeIndex >= 0 ? candidates[safeIndex] : candidates[0];
        }

        private (int, int) FindFreeCellNear(DungeonFloor floor, (int x, int y) near, Func<(int, int), bool> region, HashSet<(int, int)> exclude, HashSet<(int, int)> avoidAdjacentTo = null)
        {
            var found = new List<(int, int)>();
            if (!exclude.Contains(near) && floor.Cells[near.x, near.y].Type == CellType.Normal && !floor.Cells[near.x, near.y].IsPredefinedRoom)
                found.Add(near);

            var visited = new HashSet<(int, int)> { near };
            var queue = new Queue<(int, int)>();
            queue.Enqueue(near);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!exclude.Contains(cur) && floor.Cells[cur.Item1, cur.Item2].Type == CellType.Normal && !floor.Cells[cur.Item1, cur.Item2].IsPredefinedRoom)
                {
                    found.Add(cur);
                    if (found.Count >= 12) break; // suficientes candidatos, no hace falta recorrer todo
                }
                var cell = floor.Cells[cur.Item1, cur.Item2];
                foreach (var dir in DirectionExtensions.All)
                {
                    if (cell.HasWall(dir)) continue;
                    var (ox, oy) = dir.Offset();
                    var next = (cur.Item1 + ox, cur.Item2 + oy);
                    if (!floor.InBounds(next.Item1, next.Item2) || visited.Contains(next) || !region(next)) continue;
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            if (found.Count == 0)
                throw new InvalidOperationException("No se encontro celda libre cerca del portón para el switch.");

            int safeIndex = found.FindIndex(c => !ViolatesAdjacency(floor, c, avoidAdjacentTo));
            return safeIndex >= 0 ? found[safeIndex] : found[0];
        }

        private void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // ---------- Stairs ----------

        public void PlaceStairsBetween(DungeonFloor lower, DungeonFloor upper, Random rng, int pairCount, IList<(int, int)> restrictLowerTo = null)
        {
            int pairsPlaced = 0;

            // Primer par: coincide con EndPos(lower)/StartPos(upper) -- el mismo backbone al que ya
            // se anclan AddCrossingPaths/PlacePacingPillars/PlaceFoeRoute/AssignLoreLock. Antes, ese
            // backbone se calculaba sobre dos celdas que solo eran marcadores de sabor (CellType.
            // Start/End no hacen nada mecanico), mientras la escalera real -- por donde el jugador
            // EN LOS HECHOS entra y sale del piso, ver DungeonManager.OnPlayerEnterCell -> ChangeFloor
            // usando StairTargetX/Y -- se elegia aparte, al azar, sin ninguna relacion. Resultado: la
            // ruta que el generador diversifica con cuidado no era la que el jugador de verdad
            // recorria para progresar. Esto los une: la celda EndPos deja de ser un cartel decorativo
            // y pasa a ser la escalera de subida de verdad (y StartPos del piso de arriba, la de
            // bajada). Nada del resto del piso cambia -- zona aislada, mision secundaria, cofres,
            // ramas muertas siguen exactamente igual -- asi que el mapa sigue sintiendose como un
            // mapa real para explorar, no un pasillo. Nunca se hace en pisos de jefe (restrictLowerTo
            // != null: la escalera tiene que salir DENTRO de la sala del jefe, sin importar donde
            // haya quedado EndPos). El chequeo de zona aislada es un segundo seguro nada mas --
            // GenerateFloor ya excluye la zona aislada al elegir EndPos (mismo motivo que StartPos:
            // si la escalera obligatoria quedara ahi adentro, el secreto dejaria de ser opcional) --
            // pero si esa garantia cambia algun dia sin tocar esta funcion, mejor caer al fallback
            // de abajo que dejar la zona aislada obligatoria en silencio.
            if (restrictLowerTo == null && !lower.IsInIsolatedZone(lower.EndPos.x, lower.EndPos.y))
            {
                var lowerEnd = lower.EndPos;
                var upperStart = upper.StartPos;

                var lCell = lower.Cells[lowerEnd.x, lowerEnd.y];
                lCell.Type = CellType.StairsUp;
                lCell.StairTargetFloor = upper.Index;
                lCell.StairTargetX = upperStart.x;
                lCell.StairTargetY = upperStart.y;

                var uCell = upper.Cells[upperStart.x, upperStart.y];
                uCell.Type = CellType.StairsDown;
                uCell.StairTargetFloor = lower.Index;
                uCell.StairTargetX = lowerEnd.x;
                uCell.StairTargetY = lowerEnd.y;

                pairsPlaced = 1;
            }

            // Pares adicionales (o el unico par, en piso de jefe): celdas libres al azar, igual que
            // antes -- son la variedad extra de "mas de una forma fisica de bajar", no la ruta
            // garantizada.
            var lowerFree = (restrictLowerTo != null && restrictLowerTo.Count > 0)
                ? restrictLowerTo.Where(c => lower.Cells[c.Item1, c.Item2].Type == CellType.Normal).ToList()
                : FreeNormalCells(lower);
            var upperFree = FreeNormalCells(upper);
            Shuffle(lowerFree, rng);
            Shuffle(upperFree, rng);

            int remaining = Math.Max(0, pairCount - pairsPlaced);
            int count = Math.Min(remaining, Math.Min(lowerFree.Count, upperFree.Count));
            for (int i = 0; i < count; i++)
            {
                var lPos = lowerFree[i];
                var uPos = upperFree[i];

                var lCell = lower.Cells[lPos.Item1, lPos.Item2];
                lCell.Type = CellType.StairsUp;
                lCell.StairTargetFloor = upper.Index;
                lCell.StairTargetX = uPos.Item1;
                lCell.StairTargetY = uPos.Item2;

                var uCell = upper.Cells[uPos.Item1, uPos.Item2];
                uCell.Type = CellType.StairsDown;
                uCell.StairTargetFloor = lower.Index;
                uCell.StairTargetX = lPos.Item1;
                uCell.StairTargetY = lPos.Item2;
            }
        }

        // Excluye celdas de sala de jefe: ahi nunca deben caer escaleras "genericas" (las de la
        // sala de jefe se fuerzan aparte, via restrictLowerTo), ni lore/tesoro/etc. Tambien excluye
        // la zona aislada -- BUG encontrado por el usuario: sin este chequeo, un par de escaleras
        // "extra" (PlaceStairsBetween, el remaining despues del par principal EndPos/StartPos)
        // podia caer por puro azar DENTRO de la zona aislada, incluida la que aloja la escalera al
        // Bioma de Cuevas (ver PlaceCaveBiomeExit) -- eso es lo que el usuario piso pensando que
        // era la escalera nueva: una StairsUp comun (te sube de piso) que por casualidad quedo
        // adentro de la cueva, tapando/confundiendo con la escalera de verdad.
        private List<(int, int)> FreeNormalCells(DungeonFloor floor)
        {
            var list = new List<(int, int)>();
            for (int x = 0; x < floor.Width; x++)
                for (int y = 0; y < floor.Height; y++)
                    if (floor.Cells[x, y].Type == CellType.Normal && !floor.Cells[x, y].IsBossRoom && !floor.Cells[x, y].IsTreasureRoom
                        && !floor.Cells[x, y].IsTrapRoom && !floor.Cells[x, y].IsPuzzleTile && !floor.Cells[x, y].IsIsolatedZone
                        && !floor.Cells[x, y].IsPredefinedRoom)
                        list.Add((x, y));
            return list;
        }

        // Ata el atajo (shortcut) de este piso a un fragmento de lore: activarlo (en el juego, ver
        // Gameplay.DungeonManager) exige haber descubierto ese lore en OTRO punto del mismo piso,
        // siempre por el camino normal (nunca dentro de la zona aislada que el propio atajo
        // acorta, para que el jugador pueda encontrarlo ANTES de necesitarlo). La zona aislada
        // sigue siendo alcanzable a pie sin el lore -- el atajo es una comodidad, no la unica
        // entrada -- asi que esto no cambia ninguna garantia de conectividad ya validada.
        private void AssignLoreLock(DungeonFloor floor, Random rng, string loreId)
        {
            if (floor.Gates.Count == 0 || string.IsNullOrEmpty(loreId)) return;
            var gate = floor.Gates[0];

            var candidates = FreeNormalCells(floor).Where(c => !floor.IsInIsolatedZone(c.Item1, c.Item2)).ToList();
            if (candidates.Count == 0) return; // mapa muy chico: sin lugar libre, no se agrega lore este piso

            Shuffle(candidates, rng);
            var pos = candidates[0];
            var cell = floor.Cells[pos.Item1, pos.Item2];
            cell.Type = CellType.Lore;
            cell.AssignedLoreId = loreId;
            cell.DangerValue = 0; // leer una pista de lore es seguro, igual que Start/End/escaleras
            gate.RequiredLoreId = loreId;
        }

        private (int, int)? FindLoreCell(DungeonFloor floor, string loreId)
        {
            for (int x = 0; x < floor.Width; x++)
                for (int y = 0; y < floor.Height; y++)
                {
                    var c = floor.Cells[x, y];
                    if (c.Type == CellType.Lore && c.AssignedLoreId == loreId) return (x, y);
                }
            return null;
        }

        // Los pilares de pacing de un dungeon crawler "interesante" (ver el analisis de Etrian
        // Odyssey vs. Bravely Default en el articulo de Aevee Bee, "Pacing And Level Design In
        // JRPGs"): en vez de ir de Start a End en linea recta, hay que activar una PALANCA para
        // desbloquear un tramo del camino (CellType.Lever / LockedDoor), y esa palanca esta en un
        // PUNTO MUERTO real (nunca sobre el camino principal), asi que hay backtracking de verdad
        // al volver. El candado es sobre una arista del camino Start->End: como el mapa base es un
        // arbol (sin ciclos), cortar esa arista SIEMPRE desconecta End de Start hasta activar la
        // palanca -- no hace falta un candado "artificial", es estructural.
        // (El cofre YA NO se coloca aca -- es GARANTIZADO por piso via EnsureTreasure, sin importar
        // si este candado se pudo colocar o no, ver GenerateDungeon.)
        // Devuelve false (sin tocar nada) si el camino es demasiado corto para que tenga sentido.
        private bool PlacePacingPillars(DungeonFloor floor, Random rng)
        {
            var path = FindPath(floor, floor.StartPos, floor.EndPos);
            if (path.Count < 5) return false;

            // Candado dentro del primer 60% del camino (nunca pegado al final) y nunca sobre una
            // celda que ya sea especial (Start/End/SecundariaQuest/Switch/Landing) o de sala de
            // jefe (ahi puede haber ciclos por la fusion de la sala, y cortar una arista podria no
            // desconectar nada de verdad).
            int maxIdx = Math.Max(1, (int)(path.Count * 0.6f) - 1);
            var candidateIndices = new List<int>();
            for (int idx = 1; idx <= maxIdx; idx++)
            {
                var a = floor.Cells[path[idx].x, path[idx].y];
                var b = floor.Cells[path[idx + 1].x, path[idx + 1].y];
                if (a.Type == CellType.Normal && b.Type == CellType.Normal && !a.IsBossRoom && !b.IsBossRoom)
                    candidateIndices.Add(idx);
            }
            if (candidateIndices.Count == 0) return false;

            // Una sala de jefe fusiona varias celdas en una sola habitacion abierta (ver
            // AddBossRoom): si dos de esas celdas ya tenian, cada una, su propia arista de arbol
            // hacia afuera de la sala, fusionarlas crea un CICLO en lo que hasta entonces era un
            // arbol puro. Eso rompe la garantia de "cortar cualquier arista del camino desconecta
            // todo lo que sigue" en la que se basa este candado -- puede existir un rodeo por la
            // sala de jefe que la esquive por completo. Por eso no basta con elegir un candidato
            // cualquiera: hay que probarlo de verdad (cerrar la pared y comprobar con BFS que
            // beyondPos deja de ser alcanzable) y, si un ciclo lo esquiva, descartarlo y probar
            // otro tramo del camino.
            Shuffle(candidateIndices, rng);
            (int x, int y) doorPos = default, beyondPos = default;
            Direction doorDirValue = default;
            bool doorFound = false;
            foreach (var idx in candidateIndices)
            {
                var candidateDoorPos = path[idx];
                var candidateBeyondPos = path[idx + 1];
                var candidateDir = DirectionTo(candidateDoorPos, candidateBeyondPos);
                if (candidateDir == null) continue; // no deberia pasar: son consecutivas en el camino

                floor.Cells[candidateDoorPos.x, candidateDoorPos.y].SetWall(candidateDir.Value, true);
                floor.Cells[candidateBeyondPos.x, candidateBeyondPos.y].SetWall(candidateDir.Value.Opposite(), true);

                bool actuallyDisconnects = !BfsReachable(floor, floor.StartPos).Contains(candidateBeyondPos);
                if (actuallyDisconnects)
                {
                    doorPos = candidateDoorPos;
                    beyondPos = candidateBeyondPos;
                    doorDirValue = candidateDir.Value;
                    doorFound = true;
                    break;
                }

                // Hay un ciclo (probablemente via una sala de jefe) que esquiva este tramo: deshacer
                // y probar el siguiente candidato.
                floor.Cells[candidateDoorPos.x, candidateDoorPos.y].SetWall(candidateDir.Value, false);
                floor.Cells[candidateBeyondPos.x, candidateBeyondPos.y].SetWall(candidateDir.Value.Opposite(), false);
            }
            if (!doorFound) return false;
            Direction? doorDir = doorDirValue;

            floor.Cells[doorPos.x, doorPos.y].Type = CellType.LockedDoor;

            // beyondPos tambien queda reservado: si el cofre cayera justo ahi, conseguirlo no
            // exigiria ningun desvio real -- bastaria con cruzar la puerta que ya se iba a cruzar.
            var usedPositions = new HashSet<(int, int)> { floor.StartPos, floor.EndPos, floor.SecondaryQuestPos, doorPos, beyondPos };
            foreach (var gate in floor.Gates)
            {
                usedPositions.Add((gate.SwitchX, gate.SwitchY));
                usedPositions.Add((gate.LandingX, gate.LandingY));
            }

            // Palanca: un punto muerto alcanzable SIN cruzar la puerta que se acaba de cerrar.
            var reachableBeforeDoor = BfsReachable(floor, floor.StartPos);
            var leverCandidates = FindLeavesWithin(floor, reachableBeforeDoor, usedPositions);
            if (leverCandidates.Count == 0)
            {
                // No hay donde poner la palanca: mejor sin candado que con un piso irresoluble.
                floor.Cells[doorPos.x, doorPos.y].SetWall(doorDir.Value, false);
                floor.Cells[beyondPos.x, beyondPos.y].SetWall(doorDir.Value.Opposite(), false);
                floor.Cells[doorPos.x, doorPos.y].Type = CellType.Normal;
                return false;
            }
            Shuffle(leverCandidates, rng);
            var leverPos = leverCandidates[0];
            floor.Cells[leverPos.x, leverPos.y].Type = CellType.Lever;
            usedPositions.Add(leverPos);

            floor.LockedDoors.Add(new LockedDoor
            {
                DoorX = doorPos.x,
                DoorY = doorPos.y,
                DoorDir = doorDir.Value,
                LeverX = leverPos.x,
                LeverY = leverPos.y,
                IsUnlocked = false,
            });

            // El cofre YA NO se coloca aca: EnsureTreasure (llamado siempre despues, ver
            // GenerateDungeon) es la UNICA fuente del cofre garantizado de cada piso, para que el
            // intento de agrandarlo a un cuadrante 2x2 (TryGrowTreasureRoom) se aplique siempre por
            // el mismo camino, sin importar si este candado se pudo colocar o no.
            return true;
        }

        // 3 a 5 cofres GARANTIZADOS por piso (segun cuanto espacio libre real haya), cada uno en su
        // propio punto muerto -- el contenido de cada uno (plata, arma con habilidad, herramienta)
        // se resuelve recien al abrirlo (ver Gameplay/DungeonManager.RollTreasureLoot), aca solo se
        // elige DONDE quedan. Solo puede devolver menos de 3 (incluso 0) en mapas muy chicos/densos
        // sin suficientes puntos muertos libres.
        private bool EnsureTreasure(DungeonFloor floor, Random rng)
        {
            if (floor.TreasurePositions != null && floor.TreasurePositions.Count > 0) return true;

            var used = new HashSet<(int, int)> { floor.StartPos, floor.EndPos, floor.SecondaryQuestPos };
            foreach (var door in floor.LockedDoors)
            {
                used.Add((door.DoorX, door.DoorY));
                used.Add((door.LeverX, door.LeverY));
            }
            foreach (var gate in floor.Gates)
            {
                used.Add((gate.SwitchX, gate.SwitchY));
                used.Add((gate.LandingX, gate.LandingY));
            }

            int desired = rng.Next(3, 6);
            floor.TreasurePositions = new List<(int, int)>();
            floor.TreasureRoomCells = new List<(int, int)>();

            for (int i = 0; i < desired; i++)
            {
                // BUG encontrado con el harness de consola (55% de fallo!): sin excluir la zona
                // aislada aca, este cofre GARANTIZADO (3-5 por piso, busca en TODO el piso sin
                // restriccion) se comia los callejones sin salida de la zona aislada -- la MISMA
                // celda que PlaceCaveBiomeExit (que corre despues) necesita para la escalera al
                // Bioma de Cuevas. Si la zona aislada es chica, 2-3 cofres random ya alcanzan para
                // dejarla sin ningun punto muerto libre, y la escalera nunca aparece (en ESE punto
                // la zona aislada entera queda sin la mini-cueva completa que se supone que es).
                // Mismo criterio que ya usa AssignLoreLock: el cofre garantizado es del piso
                // "normal", la zona aislada tiene su propio secreto aparte (el atajo).
                var candidates = FindLeavesWithin(floor, null, used).Where(c => !floor.IsInIsolatedZone(c.x, c.y)).ToList();
                if (candidates.Count == 0) break;

                Shuffle(candidates, rng);
                var pos = candidates[0];
                floor.Cells[pos.x, pos.y].Type = CellType.Treasure;
                floor.Cells[pos.x, pos.y].IsTreasureRoom = true;
                floor.TreasurePositions.Add(pos);
                floor.TreasureRoomCells.Add(pos);
                used.Add(pos);
            }

            if (floor.TreasurePositions.Count > 0) floor.TreasurePos = floor.TreasurePositions[0];
            return floor.TreasurePositions.Count > 0;
        }

        // ---------- Boveda de cascada (pedido puntual) ----------

        // Pedido puntual: en cualquier piso donde haya agua de verdad (bosque/cueva intergalactica/
        // Bioma de Cuevas -- nunca patio ni castillo, ver el filtro de biome en GenerateDungeon)
        // puede aparecer una sala con una cascada que cae desde una fuente de agua, y detras de la
        // cascada una boveda con un guardian fuerte y un tesoro garantizado. Reusa el mismo
        // mecanismo que un cofre/la Puerta Fria: toma un punto muerto
        // REAL (grado 1, ver FindLeavesWithin) para la boveda -- su UNICA conexion real ya es la
        // pared que da a su vecino, que es justo donde va la cascada (ver DungeonLevelBuilder.
        // BuildWaterfallDecor). No hace falta abrir ni sellar ninguna pared: el punto muerto YA
        // esta conectado de un solo lado, que es exactamente la geometria que se necesita. Va antes
        // de podar (PruneToSparseMaze la protege via floor.WaterfallVaultPos), igual que el cofre y
        // la Puerta Fria de arriba.
        private const float WaterfallVaultChance = 0.25f;

        private bool AddWaterfallVault(DungeonFloor floor, Random rng)
        {
            if (rng.NextDouble() >= WaterfallVaultChance) return false;

            var used = new HashSet<(int, int)> { floor.StartPos, floor.EndPos, floor.SecondaryQuestPos };
            foreach (var door in floor.LockedDoors)
            {
                used.Add((door.DoorX, door.DoorY));
                used.Add((door.LeverX, door.LeverY));
            }
            foreach (var gate in floor.Gates)
            {
                used.Add((gate.SwitchX, gate.SwitchY));
                used.Add((gate.LandingX, gate.LandingY));
            }
            if (floor.TreasureRoomCells != null)
                foreach (var c in floor.TreasureRoomCells) used.Add(c);
            if (floor.TombPositions != null)
                foreach (var c in floor.TombPositions) used.Add(c);

            var candidates = FindLeavesWithin(floor, null, used).Where(c => !floor.IsInIsolatedZone(c.x, c.y)).ToList();
            if (candidates.Count == 0) return false;

            Shuffle(candidates, rng);
            var vaultPos = candidates[0];
            var vaultCell = floor.Cells[vaultPos.x, vaultPos.y];
            var approachDir = vaultCell.FirstOpenDirection(); // su unico lado sin pared -- hacia el cuarto de la cascada
            var (ox, oy) = approachDir.Offset();
            var roomPos = (vaultPos.x + ox, vaultPos.y + oy);

            vaultCell.IsWaterfallVaultRoom = true;
            floor.Cells[roomPos.Item1, roomPos.Item2].IsWaterfallRoom = true;
            floor.WaterfallVaultPos = vaultPos;
            return true;
        }

        // ---------- Tumbas del Bioma de Cuevas (pedido puntual) ----------

        // Reemplaza al cofre comun (EnsureTreasure) SOLO en el Bioma de Cuevas: mismo criterio de
        // colocacion (puntos muertos reales, ver FindLeavesWithin) pero el contenido se resuelve
        // recien al interactuar -- 70% tesoro, 30% combate contra goblins emboscados (ver
        // Gameplay/DungeonManager.TryInteract, rama CellType.Tomb). La celda de la que viene su
        // unica conexion real (el pasillo de acceso, siempre Normal y caminable) se marca
        // IsTombDecor para que el sarcofago se vea ocupando 2 celdas sin tocar la conectividad del
        // laberinto.
        private bool AddTombs(DungeonFloor floor, Random rng)
        {
            var used = new HashSet<(int, int)> { floor.StartPos, floor.EndPos, floor.SecondaryQuestPos };
            foreach (var door in floor.LockedDoors)
            {
                used.Add((door.DoorX, door.DoorY));
                used.Add((door.LeverX, door.LeverY));
            }
            foreach (var gate in floor.Gates)
            {
                used.Add((gate.SwitchX, gate.SwitchY));
                used.Add((gate.LandingX, gate.LandingY));
            }

            int desired = rng.Next(3, 6);
            floor.TombPositions = new List<(int, int)>();

            for (int i = 0; i < desired; i++)
            {
                var candidates = FindLeavesWithin(floor, null, used).Where(c => !floor.IsInIsolatedZone(c.x, c.y)).ToList();
                if (candidates.Count == 0) break;

                Shuffle(candidates, rng);
                var pos = candidates[0];
                floor.Cells[pos.x, pos.y].Type = CellType.Tomb;
                floor.TombPositions.Add(pos);
                used.Add(pos);

                var approachDir = floor.Cells[pos.x, pos.y].FirstOpenDirection();
                var (ox, oy) = approachDir.Offset();
                int nx = pos.x + ox, ny = pos.y + oy;
                if (floor.InBounds(nx, ny) && floor.Cells[nx, ny].Type == CellType.Normal
                    && !floor.Cells[nx, ny].IsPredefinedRoom && !used.Contains((nx, ny)))
                    floor.Cells[nx, ny].IsTombDecor = true;
            }

            return floor.TombPositions.Count > 0;
        }

        // ---------- Trampas de goblins del Bioma de Cuevas (pedido puntual) ----------

        // A diferencia de AddAmbushRoom (una unica bandera por piso, restringida a la zona
        // aislada), esto marca celdas INDIVIDUALES sueltas por TODO el piso -- cada una dispara su
        // propio combate una sola vez (ver DungeonCell.GoblinTrapTriggered, por celda en vez de por
        // piso), asi puede haber varias independientes. Sin tell visual a proposito (ver Gameplay/
        // DungeonManager.OnPlayerEnterCell): son goblins emboscando, no un mecanismo desactivable
        // como la sala de trampas comun.
        private const int GoblinTrapCount = 3;

        private void AddGoblinTraps(DungeonFloor floor, Random rng)
        {
            var used = new HashSet<(int, int)> { floor.StartPos, floor.EndPos, floor.SecondaryQuestPos };
            foreach (var door in floor.LockedDoors)
            {
                used.Add((door.DoorX, door.DoorY));
                used.Add((door.LeverX, door.LeverY));
            }
            foreach (var gate in floor.Gates)
            {
                used.Add((gate.SwitchX, gate.SwitchY));
                used.Add((gate.LandingX, gate.LandingY));
            }
            if (floor.TombPositions != null)
                foreach (var t in floor.TombPositions) used.Add(t);
            if (floor.WaterfallVaultPos.HasValue) used.Add(floor.WaterfallVaultPos.Value);

            var candidates = new List<(int, int)>();
            for (int x = 0; x < floor.Width; x++)
            {
                for (int y = 0; y < floor.Height; y++)
                {
                    var cell = floor.Cells[x, y];
                    if (cell.Type != CellType.Normal || cell.IsPredefinedRoom || cell.IsTombDecor) continue;
                    if (used.Contains((x, y)) || floor.IsInIsolatedZone(x, y)) continue;
                    candidates.Add((x, y));
                }
            }
            if (candidates.Count == 0) return;

            Shuffle(candidates, rng);
            int count = Math.Min(GoblinTrapCount, candidates.Count);
            for (int i = 0; i < count; i++)
                floor.Cells[candidates[i].Item1, candidates[i].Item2].IsGoblinTrap = true;
        }

        // ---------- Puerta Fria (entrada escondida al Bioma 2) ----------

        // Toma una celda que todavia es un punto muerto REAL (grado 1, ver FindLeavesWithin) y
        // sella su unica conexion: a partir de ahi es inalcanzable a pie, una pared cualquiera
        // mas -- ninguna diferencia visible con cualquier otro muro del piso. NO depende de
        // ningun lore ni flag para abrirse: es TryDrillWall generico, el mismo Perforador de
        // siempre, sobre la celda del otro lado. Las pistas (PlaceBiomeGateClues) son pura ayuda
        // para saber DONDE buscarla, nunca un requisito -- el camino siempre estuvo ahi.
        private bool PlaceBiomeGate(DungeonFloor floor, Random rng)
        {
            var used = new HashSet<(int, int)> { floor.StartPos, floor.EndPos, floor.SecondaryQuestPos };
            foreach (var door in floor.LockedDoors)
            {
                used.Add((door.DoorX, door.DoorY));
                used.Add((door.LeverX, door.LeverY));
            }
            foreach (var gate in floor.Gates)
            {
                used.Add((gate.SwitchX, gate.SwitchY));
                used.Add((gate.LandingX, gate.LandingY));
            }
            if (floor.TreasureRoomCells != null)
                foreach (var c in floor.TreasureRoomCells) used.Add(c);

            // La Puerta Fria tiene que ser alcanzable SOLO explorando el piso a pie: nunca detras de
            // un candado+palanca (PlacePacingPillars, que ya corrio y dejo esas puertas cerradas a
            // esta altura -- ver GenerateDungeon), porque eso obligaria a resolver un mecanismo sin
            // relacion antes de poder usar las pistas de lore, rompiendo esa logica. Tampoco dentro
            // de la zona aislada: ya es su propio secreto (el atajo/switch), no hace falta anidar un
            // segundo secreto adentro del primero. (La escalera de la zona aislada hacia el Bioma 2
            // es un mecanismo APARTE, ver DungeonGenerator.PlaceCaveBiomeExit -- esta Puerta Fria no
            // se mueve de aca.)
            var reachableFree = new HashSet<(int, int)>(
                BfsReachable(floor, floor.StartPos).Where(c => !floor.IsInIsolatedZone(c.Item1, c.Item2)));

            var candidates = FindLeavesWithin(floor, reachableFree, used);
            if (candidates.Count == 0) return false;

            Shuffle(candidates, rng);
            var pos = candidates[0];
            var cell = floor.Cells[pos.x, pos.y];

            Direction openDir = default;
            bool found = false;
            foreach (var dir in DirectionExtensions.All)
            {
                if (!cell.HasWall(dir)) { openDir = dir; found = true; break; }
            }
            if (!found) return false; // no deberia pasar: FindLeavesWithin ya garantiza grado 1

            var (ox, oy) = openDir.Offset();
            var approach = (pos.x + ox, pos.y + oy);

            cell.SetWall(openDir, true);
            floor.Cells[approach.Item1, approach.Item2].SetWall(openDir.Opposite(), true);

            cell.Type = CellType.BiomeGate;
            floor.BiomeGatePos = pos;
            floor.BiomeGateApproachPos = approach;
            return true;
        }

        // ---------- Peligros de la mini cueva (zona aislada del piso 0) ----------
        // Pedido puntual: la mini cueva (ver PlaceCaveBiomeExit) se sentia vacia comparada con el
        // resto del piso -- 3 mecanismos propios, cada uno best-effort (devuelven false sin tocar
        // nada si no encuentran lugar, nunca revientan la generacion).

        // Celdas de la zona aislada que NINGUNO de los 3 metodos de abajo puede tocar/pisar.
        // RectOverlapsSpecialCells no alcanza aca: estos corren DESPUES de PlacePacingPillars/
        // EnsureTreasure/PlaceCaveBiomeExit (ver el orden real en GenerateDungeon), y ademas el
        // switch/landing REAL de un atajo puede haberse reubicado lejos de Gate.Bx/By si esa celda
        // ya estaba ocupada (ver GenerateFloor, FindFreeCellNear).
        private HashSet<(int, int)> IsolatedZoneReservedCells(DungeonFloor floor)
        {
            var used = new HashSet<(int, int)> { floor.StartPos, floor.EndPos, floor.SecondaryQuestPos };
            if (floor.CaveBiomeExitPos.HasValue) used.Add(floor.CaveBiomeExitPos.Value);
            if (floor.BiomeGatePos.HasValue) used.Add(floor.BiomeGatePos.Value);
            if (floor.BiomeGateApproachPos.HasValue) used.Add(floor.BiomeGateApproachPos.Value);
            foreach (var gate in floor.Gates)
            {
                used.Add((gate.SwitchX, gate.SwitchY));
                used.Add((gate.LandingX, gate.LandingY));
            }
            foreach (var door in floor.LockedDoors)
            {
                used.Add((door.DoorX, door.DoorY));
                used.Add((door.LeverX, door.LeverY));
            }
            if (floor.TreasureRoomCells != null)
                foreach (var c in floor.TreasureRoomCells) used.Add(c);
            if (floor.HasBoulderTrap)
                foreach (var c in floor.BoulderTrapPath) used.Add(c);
            if (floor.HasCollapseRoom)
                foreach (var c in floor.CollapseRoomCells) used.Add(c);
            if (floor.HasAmbushRoom)
                foreach (var c in floor.AmbushRoomCells) used.Add(c);
            return used;
        }

        // Roca que persigue (Gameplay/BoulderTrapController hace el movimiento en vivo, esto solo
        // elige el corredor): a diferencia de AddTrapRoom/AddBossRoom, NO fusiona una sala nueva --
        // reusa un tramo YA carveado por Carve() (un corredor recto de verdad, con sus paredes
        // internas ya abiertas), asi que no hace falta tocar ninguna pared. El extremo lejano
        // SIEMPRE tiene una salida real (Degree >= 2: la propia linea mas al menos una salida
        // extra) para que "correr hacia adelante" tenga a donde llegar -- nunca un callejon sin
        // salida esperando para aplastarte contra la pared.
        private bool AddBoulderTrap(DungeonFloor floor, Random rng)
        {
            const int length = 5;
            var used = IsolatedZoneReservedCells(floor);
            var path = FindStraightCorridor(floor, rng, length, used);
            if (path == null) return false;

            foreach (var (x, y) in path) floor.Cells[x, y].IsBoulderTrapCell = true;
            floor.BoulderTrapPath = path;
            floor.BoulderTrapStartPos = path[0];
            return true;
        }

        // Busca TODOS los tramos rectos de <length> celdas dentro de la zona aislada (horizontal o
        // vertical) que no toquen `excluded` y cuyo extremo lejano tenga una salida real ademas de
        // la linea misma, y devuelve uno al azar -- null si no hay ninguno.
        private List<(int, int)> FindStraightCorridor(DungeonFloor floor, Random rng, int length, HashSet<(int, int)> excluded)
        {
            var candidates = new List<List<(int, int)>>();
            for (int x = floor.IsoMinX; x <= floor.IsoMaxX; x++)
            {
                for (int y = floor.IsoMinY; y <= floor.IsoMaxY; y++)
                {
                    if (floor.Cells[x, y].Type != CellType.Normal) continue;
                    var east = TryWalkStraight(floor, x, y, Direction.East, length, excluded);
                    if (east != null) candidates.Add(east);
                    var south = TryWalkStraight(floor, x, y, Direction.South, length, excluded);
                    if (south != null) candidates.Add(south);
                }
            }
            if (candidates.Count == 0) return null;
            return candidates[rng.Next(candidates.Count)];
        }

        private List<(int, int)> TryWalkStraight(DungeonFloor floor, int startX, int startY, Direction dir, int length, HashSet<(int, int)> excluded)
        {
            if (excluded.Contains((startX, startY))) return null;

            var run = new List<(int, int)> { (startX, startY) };
            int cx = startX, cy = startY;
            for (int i = 1; i < length; i++)
            {
                if (floor.Cells[cx, cy].HasWall(dir)) return null;
                var (ox, oy) = dir.Offset();
                cx += ox; cy += oy;
                if (!floor.IsInIsolatedZone(cx, cy) || floor.Cells[cx, cy].Type != CellType.Normal) return null;
                if (excluded.Contains((cx, cy))) return null;
                run.Add((cx, cy));
            }

            // El extremo lejano tiene que poder seguir hacia algun lado que no sea volver por donde
            // vino -- Degree cuenta la conexion de entrada Y cualquier salida extra, asi que >= 2
            // significa "hay a donde correr".
            if (Degree(floor, cx, cy) < 2) return null;
            return run;
        }

        // Sala grande donde HAY que cruzar rapido (ver Gameplay/DungeonManager.CollapseRoomRoutine):
        // mismo mecanismo de fusion que AddTrapRoom/AddBossRoom, pero a diferencia de esas dos
        // reintenta hasta encontrar una ubicacion con conexiones reales en DOS lados bien separados
        // (entrada/salida) -- sin eso no seria "cruzar" nada, solo entrar y volver.
        private bool AddCollapsingRushRoom(DungeonFloor floor, Random rng)
        {
            int minSide = 5, maxSide = 7;
            int zoneW = floor.IsoMaxX - floor.IsoMinX + 1;
            int zoneH = floor.IsoMaxY - floor.IsoMinY + 1;
            if (zoneW < minSide || zoneH < minSide) return false;

            var used = IsolatedZoneReservedCells(floor);

            for (int attempt = 0; attempt < 40; attempt++)
            {
                int rw = rng.Next(minSide, Math.Min(maxSide, zoneW) + 1);
                int rh = rng.Next(minSide, Math.Min(maxSide, zoneH) + 1);

                int rx = floor.IsoMinX + rng.Next(0, zoneW - rw + 1);
                int ry = floor.IsoMinY + rng.Next(0, zoneH - rh + 1);

                if (RectOverlapsSpecialCells(floor, rx, ry, rw, rh)) continue;
                if (RectOverlapsPredefinedRoom(floor, rx, ry, rw, rh)) continue;

                bool blocked = false;
                for (int x = rx; x < rx + rw && !blocked; x++)
                    for (int y = ry; y < ry + rh; y++)
                        if (floor.Cells[x, y].IsBossRoom || floor.Cells[x, y].IsTrapRoom || used.Contains((x, y)))
                            { blocked = true; break; }
                if (blocked) continue;

                // Puntos de conexion real con el resto de la mazmorra: bordes del rectangulo cuya
                // pared hacia AFUERA ya estaba abierta (Carve() corrio mucho antes que esto).
                var connections = new List<(int x, int y)>();
                for (int x = rx; x < rx + rw; x++)
                {
                    for (int y = ry; y < ry + rh; y++)
                    {
                        foreach (var dir in DirectionExtensions.All)
                        {
                            var (ox, oy) = dir.Offset();
                            int nx = x + ox, ny = y + oy;
                            bool outside = nx < rx || nx >= rx + rw || ny < ry || ny >= ry + rh;
                            if (outside && floor.InBounds(nx, ny) && !floor.Cells[x, y].HasWall(dir))
                                connections.Add((x, y));
                        }
                    }
                }
                if (connections.Count < 2) continue;

                (int x, int y) best1 = connections[0], best2 = connections[1];
                int bestDist = -1;
                for (int i = 0; i < connections.Count; i++)
                    for (int j = i + 1; j < connections.Count; j++)
                    {
                        int d = Math.Abs(connections[i].x - connections[j].x) + Math.Abs(connections[i].y - connections[j].y);
                        if (d > bestDist) { bestDist = d; best1 = connections[i]; best2 = connections[j]; }
                    }
                // Muy pegadas (mismo rincon): no se siente un cruce de punta a punta, reintentar otra ubicacion.
                if (bestDist < Math.Min(rw, rh)) continue;

                var cells = new List<(int, int)>();
                for (int x = rx; x < rx + rw; x++)
                {
                    for (int y = ry; y < ry + rh; y++)
                    {
                        cells.Add((x, y));
                        var cell = floor.Cells[x, y];
                        cell.IsCollapseRoom = true;
                        foreach (var dir in DirectionExtensions.All)
                        {
                            var (ox, oy) = dir.Offset();
                            int nx = x + ox, ny = y + oy;
                            if (nx >= rx && nx < rx + rw && ny >= ry && ny < ry + rh)
                                cell.SetWall(dir, false);
                        }
                    }
                }

                floor.CollapseRoomCells = cells;
                floor.CollapseEntryPos = best1;
                floor.CollapseExitPos = best2;
                // Ola de colapso: BFS por distancia real desde la entrada -- colapsa primero lo mas
                // cerca de donde entraste, cada vez mas lejos, como si te persiguiera hacia la
                // salida. La salida en si NUNCA esta en la lista (tiene que seguir siendo piso real
                // para poder terminar de cruzar).
                floor.CollapseOrder = BfsOrderWithinRoom(cells, best1, best2);
                return true;
            }
            return false;
        }

        private List<(int, int)> BfsOrderWithinRoom(List<(int, int)> roomCells, (int, int) start, (int, int) exclude)
        {
            var cellSet = new HashSet<(int, int)>(roomCells);
            var dist = new Dictionary<(int, int), int> { [start] = 0 };
            var queue = new Queue<(int, int)>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                foreach (var dir in DirectionExtensions.All)
                {
                    var (ox, oy) = dir.Offset();
                    var next = (cx + ox, cy + oy);
                    if (!cellSet.Contains(next) || dist.ContainsKey(next)) continue;
                    dist[next] = dist[(cx, cy)] + 1;
                    queue.Enqueue(next);
                }
            }
            return dist.Keys.Where(c => c != exclude).OrderBy(c => dist[c]).ToList();
        }

        // Sala de emboscada: mismo mecanismo de fusion que AddTrapRoom/AddBossRoom pero sin
        // necesitar ninguna conexion especial (con 1 sola alcanza) -- pisar CUALQUIER celda de la
        // sala dispara el combate sorpresa, ver Gameplay/DungeonManager.OnPlayerEnterCell.
        private bool AddAmbushRoom(DungeonFloor floor, Random rng)
        {
            int minSide = 3, maxSide = 4;
            int zoneW = floor.IsoMaxX - floor.IsoMinX + 1;
            int zoneH = floor.IsoMaxY - floor.IsoMinY + 1;
            if (zoneW < minSide || zoneH < minSide) return false;

            var used = IsolatedZoneReservedCells(floor);

            for (int attempt = 0; attempt < 40; attempt++)
            {
                int rw = rng.Next(minSide, Math.Min(maxSide, zoneW) + 1);
                int rh = rng.Next(minSide, Math.Min(maxSide, zoneH) + 1);

                int rx = floor.IsoMinX + rng.Next(0, zoneW - rw + 1);
                int ry = floor.IsoMinY + rng.Next(0, zoneH - rh + 1);

                if (RectOverlapsSpecialCells(floor, rx, ry, rw, rh)) continue;
                if (RectOverlapsPredefinedRoom(floor, rx, ry, rw, rh)) continue;

                bool blocked = false;
                for (int x = rx; x < rx + rw && !blocked; x++)
                    for (int y = ry; y < ry + rh; y++)
                        if (floor.Cells[x, y].IsBossRoom || floor.Cells[x, y].IsTrapRoom || used.Contains((x, y)))
                            { blocked = true; break; }
                if (blocked) continue;

                var cells = new List<(int, int)>();
                for (int x = rx; x < rx + rw; x++)
                {
                    for (int y = ry; y < ry + rh; y++)
                    {
                        cells.Add((x, y));
                        var cell = floor.Cells[x, y];
                        cell.IsAmbushRoom = true;
                        foreach (var dir in DirectionExtensions.All)
                        {
                            var (ox, oy) = dir.Offset();
                            int nx = x + ox, ny = y + oy;
                            if (nx >= rx && nx < rx + rw && ny >= ry && ny < ry + rh)
                                cell.SetWall(dir, false);
                        }
                    }
                }

                floor.AmbushRoomCells = cells;
                return true;
            }
            return false;
        }

        // Elige una de las cuatro esquinas del mapa y coloca ahi una bifurcacion al castillo.
        // Se priorizan las zonas de bosque normal para que el hito se descubra entre los arboles;
        // la zona aislada queda como respaldo si el cuadrante sorteado esta demasiado ocupado.
        private bool PlaceCastleGate(DungeonFloor floor, Random rng)
        {
            foreach (var door in floor.LockedDoors) SetDoorWallState(floor, door, true);
            var reachable = BfsReachable(floor, floor.StartPos);
            int quadrant = rng.Next(4);
            int halfX = floor.Width / 2, halfY = floor.Height / 2;
            bool InQuadrant((int x, int y) c, int q) =>
                ((c.x >= halfX) ? 1 : 0) + ((c.y >= halfY) ? 2 : 0) == q;
            bool Eligible((int x, int y) c) => floor.Cells[c.x, c.y].Type == CellType.Normal
                && !floor.Cells[c.x, c.y].IsPredefinedRoom
                && !floor.Cells[c.x, c.y].IsBossRoom
                && !floor.Cells[c.x, c.y].IsTrapRoom
                && !floor.Cells[c.x, c.y].IsAmbushRoom
                && !floor.Cells[c.x, c.y].IsCollapseRoom
                && !floor.Cells[c.x, c.y].IsPuzzleTile
                && !floor.Cells[c.x, c.y].IsBoulderTrapCell;

            var candidates = reachable.Where(c => InQuadrant(c, quadrant)
                && !floor.IsInIsolatedZone(c.Item1, c.Item2)
                && Eligible(c) && Degree(floor, c.Item1, c.Item2) == 1).ToList();
            if (candidates.Count == 0)
                candidates = reachable.Where(c => !floor.IsInIsolatedZone(c.Item1, c.Item2)
                    && Eligible(c) && Degree(floor, c.Item1, c.Item2) == 1).ToList();
            if (candidates.Count == 0)
                candidates = reachable.Where(c => !floor.IsInIsolatedZone(c.Item1, c.Item2) && Eligible(c)).ToList();
            if (candidates.Count == 0)
                candidates = reachable.Where(c => floor.IsInIsolatedZone(c.Item1, c.Item2) && Eligible(c)).ToList();
            foreach (var door in floor.LockedDoors) SetDoorWallState(floor, door, false);

            if (candidates.Count == 0)
            {
                // Ultimo respaldo: si el cuadrante sorteado y la zona aislada estan ocupados,
                // deja el acceso en cualquier celda normal alcanzable. No se debe perder la ruta
                // opcional al castillo por la distribucion de salas de una semilla concreta.
                candidates = reachable.Where(c => Eligible(c) && !floor.IsInIsolatedZone(c.Item1, c.Item2)).ToList();
            }
            if (candidates.Count == 0) return false;
            // Usa una punta del laberinto para que la puerta se descubra explorando.
            var pos = candidates[rng.Next(candidates.Count)];
            floor.Cells[pos.Item1, pos.Item2].Type = CellType.CastleGate;
            floor.CastleGatePos = pos;
            return true;
        }

        // Escalera al fondo de la zona aislada (pedido puntual: "genera una mini dungeon en el
        // cuadrante de la cueva y al final de esta una escalera al bioma de cuevas") -- mecanismo
        // APARTE de la Puerta Fria de arriba: mismo destino (Bioma 2), pero sin secreto ni
        // Perforador. Se busca el punto MAS PROFUNDO de la zona aislada (el callejon sin salida
        // mas lejano de su entrada, o si no hay ninguno libre, la celda normal mas lejana que sea)
        // y se lo marca directamente como CaveBiomeExit -- no hay pared que sellar, es una celda
        // caminable comun, se encuentra explorando la cueva sin pistas de por medio.
        private bool PlaceCaveBiomeExit(DungeonFloor floor)
        {
            var used = new HashSet<(int, int)> { floor.StartPos, floor.EndPos, floor.SecondaryQuestPos };
            if (floor.BiomeGatePos.HasValue) used.Add(floor.BiomeGatePos.Value);
            if (floor.BiomeGateApproachPos.HasValue) used.Add(floor.BiomeGateApproachPos.Value);
            foreach (var gate in floor.Gates)
            {
                used.Add((gate.SwitchX, gate.SwitchY));
                used.Add((gate.LandingX, gate.LandingY));
            }
            if (floor.TreasureRoomCells != null)
                foreach (var c in floor.TreasureRoomCells) used.Add(c);

            // BUG encontrado con el harness de consola (55% de fallo!): PlacePacingPillars ya corrio
            // para este piso (ver GenerateDungeon) y puede haber dejado un candado obligatorio
            // cerrado en el camino hacia la zona aislada -- perfectamente resoluble para el jugador
            // de verdad (activa la palanca y listo), pero un BfsReachable con el candado TODAVIA
            // cerrado ve la zona aislada entera como inalcanzable y esta funcion se rendia sin
            // buscar mas. Mismo criterio que ValidateFloor ya usa para su propio chequeo
            // "activando TODAS las palancas": se simulan todos los candados abiertos SOLO para
            // decidir la posicion (la escalera tiene que sentirse coherente con el recorrido real
            // una vez resuelto el piso), y se restauran cerrados antes de salir -- la escalera en si
            // sigue sin ningun requisito extra para usarla, exactamente como antes.
            foreach (var door in floor.LockedDoors) SetDoorWallState(floor, door, true);

            var reachableInZone = new HashSet<(int, int)>(
                BfsReachable(floor, floor.StartPos).Where(c => floor.IsInIsolatedZone(c.Item1, c.Item2)));

            bool Eligible((int x, int y) c) => reachableInZone.Contains(c) && !used.Contains(c)
                && floor.Cells[c.x, c.y].Type == CellType.Normal;

            // Preferir un callejon sin salida de verdad (grado 1), para que se sienta como el
            // "fondo" de la cueva; si no queda ninguno libre (zona chica/muy abierta), cualquier
            // celda normal alcanzable sirve de respaldo -- FarthestCell ya prioriza la mas lejana
            // entre las permitidas.
            var leaves = new HashSet<(int, int)>(
                reachableInZone.Where(c => Eligible(c) && Degree(floor, c.Item1, c.Item2) == 1));
            Func<(int, int), bool> allowed = leaves.Count > 0
                ? (Func<(int, int), bool>)(c => leaves.Contains(c))
                : (c => Eligible(c));

            (int x, int y) pos = default;
            bool found = reachableInZone.Count > 0;
            if (found) (pos, _) = FarthestCell(floor, floor.StartPos, allowed: allowed);

            foreach (var door in floor.LockedDoors) SetDoorWallState(floor, door, false);

            if (!found || !Eligible(pos)) return false; // no se encontro ninguna celda valida

            floor.Cells[pos.x, pos.y].Type = CellType.CaveBiomeExit;
            floor.CaveBiomeExitPos = pos;
            return true;
        }

        // 3 fragmentos de lore de colocacion GARANTIZADA (a diferencia del pool aleatorio de
        // AssignLoreLock): cada uno hace un trabajo distinto -- plantar la pregunta, señalar DONDE
        // buscar, señalar CON QUE herramienta -- para que juntos, y solo juntos, le digan al
        // jugador como encontrar y abrir la Puerta Fria sin nunca ser obligatorios de verdad.
        // Publico (no privado como el resto de estos arrays de apoyo) para que
        // Gameplay/DungeonManager.BuildLorePool y PauseMenuHUD puedan reconocer estos 3 IDs (para
        // excluirlos del pool general de lore por piso, y para pintarlos distinto en el Codex).
        public static readonly string[] BiomeGateLoreIds = { "puerta_fria_1", "puerta_fria_2", "puerta_fria_3" };

        // En que pisos van las 3 salas de pistas: una por piso, arrancando en el piso 0 (las 3
        // pistas de la Puerta Fria quedan concentradas al principio del Bioma 1, en vez de
        // repartidas a lo largo de toda la mazmorra) -- a proposito COMPARTE el piso 0 con la
        // Puerta Fria en si (PlaceBiomeGate, un par de celdas nada mas), asi que en el peor caso de
        // un piso 0 muy chico/denso esa sala puntual puede no entrar (ver el log de
        // AddLoreCorridorRoom), no es un error. En mazmorras de menos de 3 pisos hay repetidos --
        // en ese caso compiten por espacio en el mismo piso igual.
        private int[] PickLoreCorridorFloors(int floorCount)
        {
            if (floorCount <= 1) return new[] { 0, 0, 0 };
            var picks = new int[3];
            for (int f = 0; f < 3; f++)
                picks[f] = Math.Min(floorCount - 1, f);
            return picks;
        }

        private IEnumerable<(int x, int y)> RectCells(int rx, int ry, int rw, int rh)
        {
            for (int x = rx; x < rx + rw; x++)
                for (int y = ry; y < ry + rh; y++)
                    yield return (x, y);
        }

        // Camino serpenteado dentro de un area totalmente abierta (la sala de pistas fusiona TODAS
        // sus paredes internas, ver AddLoreCorridorRoom): random walk con backtracking, igual
        // tecnica que Carve (el laberinto base), acotado a `allowed` y detenido apenas se alcanza
        // `to` -- en ese momento el stack ES el camino simple (sin revisitar celdas) de `from` a
        // `to`. Siempre encuentra camino porque `allowed` es un rectangulo solido (4-conectado, sin
        // huecos).
        //
        // SESGADO hacia `to` a proposito (medido con un diagnostico temporal: sin sesgo, la
        // variante sin sesgo dejaba la sala 80-100% "segura" en varios seeds -- un random walk sin
        // preferencia de direccion tiende a explorar CASI TODA el area chica antes de chocar con el
        // objetivo de pura casualidad, exactamente lo opuesto de "ruta correcta minoritaria rodeada
        // de obstaculo"). Cada paso prefiere (75% de las veces, si hay alguna opcion asi) una celda
        // que ACERQUE en distancia Manhattan a `to`; el resto de las veces elige cualquier opcion
        // valida -- eso sigue dando el zigzag serpenteado sin dejar que el camino se coma la sala.
        private List<(int x, int y)> CarveSerpentinePath(HashSet<(int, int)> allowed, (int x, int y) from, (int x, int y) to, Random rng)
        {
            var visited = new HashSet<(int, int)> { from };
            var stack = new List<(int, int)> { from };

            while (stack[stack.Count - 1] != to)
            {
                var (cx, cy) = stack[stack.Count - 1];
                var options = new List<(int, int)>();
                foreach (var dir in DirectionExtensions.All)
                {
                    var (ox, oy) = dir.Offset();
                    var next = (cx + ox, cy + oy);
                    if (allowed.Contains(next) && !visited.Contains(next)) options.Add(next);
                }

                if (options.Count == 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                    if (stack.Count == 0) return null; // no deberia pasar: allowed es un rectangulo solido conexo
                    continue;
                }

                int curDist = ManhattanDistance((cx, cy), to);
                var improving = options.Where(o => ManhattanDistance(o, to) < curDist).ToList();
                var pick = (improving.Count > 0 && rng.NextDouble() < 0.75)
                    ? improving[rng.Next(improving.Count)]
                    : options[rng.Next(options.Count)];

                visited.Add(pick);
                stack.Add(pick);
            }

            return stack;
        }

        private int ManhattanDistance((int x, int y) a, (int x, int y) b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

        // Sala de pistas (ver DungeonFloor.LoreCorridorKind / Gameplay/DungeonLevelBuilder.
        // BuildPuzzleTile): fusiona un rectangulo grande en una sola sala (mismo patron que
        // AddBossRoom/AddTrapRoom). A diferencia de antes (anillo siempre seguro + interior al
        // azar), ahora hay una RUTA deliberada: se eligen 2 celdas del anillo que ya tenian una
        // conexion real hacia afuera del rectangulo ("portales", entrada y salida -- las mas
        // alejadas entre si si hay varias candidatas) y se traza un camino serpenteado (ver
        // CarveSerpentinePath) desde la entrada hasta el fragmento de lore, y otro desde el lore
        // hasta la salida. Todo lo que NO sea esa ruta (resto del anillo incluido) es obstaculo --
        // real (Goteras) u oculto (Brasas/Polvo de Cuarzo), segun PuzzleKind. Si el rectangulo
        // candidato no tiene al menos 2 portales, se descarta y se prueba otro (mismo reintento
        // acotado de siempre): sin 2 conexiones reales a la mazmorra no hay como definir una
        // entrada y una salida distintas.
        private bool AddLoreCorridorRoom(DungeonFloor floor, Random rng, string loreId, PuzzleKind kind)
        {
            int minSide = 4, maxSide = 6;
            if (Math.Min(floor.Width, floor.Height) - 1 < minSide) return false;

            // Esta funcion corre DESPUES de PlacePacingPillars (necesita floor.TreasureRoomCells y
            // floor.BiomeGatePos ya resueltos para no pisarlos, ver RectOverlapsSpecialCells), asi
            // que a esta altura puede existir un candado ya cerrado y verificado. Fusionar un
            // rectangulo entero en una sala abierta (como ya hacen AddBossRoom/AddTrapRoom, ambas
            // ANTES del candado) puede crear un ciclo real en el grafo -- si ese rectangulo tiene
            // celdas de los DOS lados de una puerta cerrada, el candado deja de bloquear nada
            // aunque su propia verificacion (BFS en PlacePacingPillars) haya sido correcta en su
            // momento, porque ese ciclo todavia no existia cuando se hizo esa verificacion.
            // reachableFromStart es el estado "de arranque" (candados cerrados, tal cual esta el
            // piso ahora mismo): si un candidato pisa celdas de ambos lados, se descarta y se
            // prueba otro rectangulo, mismo reintento acotado que ya usa el resto de la funcion.
            var reachableFromStart = BfsReachable(floor, floor.StartPos);

            for (int attempt = 0; attempt < 40; attempt++)
            {
                int rw = rng.Next(minSide, maxSide + 1);
                int rh = rng.Next(minSide, maxSide + 1);
                if (rw > floor.Width || rh > floor.Height) continue;

                int rx = rng.Next(0, floor.Width - rw + 1);
                int ry = rng.Next(0, floor.Height - rh + 1);

                if (RectOverlapsIsolatedZone(floor, rx, ry, rw, rh)) continue;
                if (RectOverlapsSpecialCells(floor, rx, ry, rw, rh)) continue;
                if (RectOverlapsPredefinedRoom(floor, rx, ry, rw, rh)) continue;

                bool blocked = false;
                for (int x = rx; x < rx + rw && !blocked; x++)
                    for (int y = ry; y < ry + rh; y++)
                        if (floor.Cells[x, y].IsBossRoom || floor.Cells[x, y].IsTrapRoom || floor.Cells[x, y].IsTreasureRoom || floor.Cells[x, y].IsPuzzleTile)
                        { blocked = true; break; }
                if (blocked) continue;

                var cells = RectCells(rx, ry, rw, rh).ToList();
                if (cells.Any(c => reachableFromStart.Contains(c)) && cells.Any(c => !reachableFromStart.Contains(c)))
                    continue; // fusionar esto bypassearia un candado ya cerrado
                var ring = cells.Where(c => c.x == rx || c.x == rx + rw - 1 || c.y == ry || c.y == ry + rh - 1).ToList();
                var interior = cells.Except(ring).ToList();
                if (interior.Count == 0) continue;

                // Portales: celdas del anillo que YA tenian una conexion real hacia afuera del
                // rectangulo (una pared abierta hacia un vecino fuera de sus limites) -- las unicas
                // que sirven de entrada/salida de verdad, porque esta funcion nunca toca las
                // paredes EXTERNAS del rectangulo, solo fusiona las internas.
                var portals = new List<(int x, int y)>();
                foreach (var (x, y) in ring)
                {
                    var cell = floor.Cells[x, y];
                    foreach (var dir in DirectionExtensions.All)
                    {
                        var (ox, oy) = dir.Offset();
                        int nx = x + ox, ny = y + oy;
                        bool outsideRect = nx < rx || nx >= rx + rw || ny < ry || ny >= ry + rh;
                        if (outsideRect && !cell.HasWall(dir)) { portals.Add((x, y)); break; }
                    }
                }
                if (portals.Count < 2) continue; // sin 2 conexiones reales no hay entrada Y salida distintas

                // Entrada/salida = el par de portales mas alejado entre si (si hay varios
                // candidatos), para que el cruce real de la sala sea largo en vez de un atajo
                // pegado a una esquina.
                (int x, int y) entrance = portals[0], exit = portals[1];
                int bestDist = -1;
                for (int a = 0; a < portals.Count; a++)
                    for (int b = a + 1; b < portals.Count; b++)
                    {
                        int d = Math.Abs(portals[a].x - portals[b].x) + Math.Abs(portals[a].y - portals[b].y);
                        if (d > bestDist) { bestDist = d; entrance = portals[a]; exit = portals[b]; }
                    }

                var loreCell = interior[interior.Count / 2];
                var allowed = new HashSet<(int, int)>(cells);

                var pathIn = CarveSerpentinePath(allowed, entrance, loreCell, rng);
                var pathOut = CarveSerpentinePath(allowed, loreCell, exit, rng);
                if (pathIn == null || pathOut == null) continue; // no deberia pasar (rectangulo solido)

                var safeSet = new HashSet<(int, int)>(pathIn);
                safeSet.UnionWith(pathOut);

                foreach (var (x, y) in cells)
                {
                    var cell = floor.Cells[x, y];
                    foreach (var dir in DirectionExtensions.All)
                    {
                        var (ox, oy) = dir.Offset();
                        int nx = x + ox, ny = y + oy;
                        if (nx >= rx && nx < rx + rw && ny >= ry && ny < ry + rh)
                            cell.SetWall(dir, false);
                    }
                    cell.IsPuzzleTile = true;
                    cell.IsPuzzleTileSafe = safeSet.Contains((x, y));
                }

                floor.Cells[loreCell.x, loreCell.y].Type = CellType.Lore;
                floor.Cells[loreCell.x, loreCell.y].AssignedLoreId = loreId;
                floor.Cells[loreCell.x, loreCell.y].DangerValue = 0;

                floor.LoreCorridorRoomCells = cells;
                floor.LoreCorridorKind = kind;
                return true;
            }
            return false;
        }

        // Radio minimo (Chebyshev, ver FoeController.Chebyshev) que el FOE tiene que respetar
        // respecto de CUALQUIER escalera del piso -- pedido puntual, para que las escaleras sean
        // siempre una zona a salvo de verdad. Compartida con Gameplay/FoeController (usa esta MISMA
        // constante para vetar celdas al perseguir vía BFS): un solo punto de verdad para no
        // desincronizar el radio de generacion del radio de persecucion en runtime.
        public const int FoeStairsExclusionRadius = 5;

        // Ruta fija de patrulla para el FOE de este piso (enemigo fuerte que se pasea, ver
        // Gameplay/FoeController): usa el camino Start->End (pedido puntual, "que cruce un lado del
        // mapa al otro" -- Start/End suelen quedar en lados opuestos del piso). Se llama DESPUES de
        // PlaceStairsBetween (ver GenerateDungeon) porque necesita conocer TODAS las escaleras
        // reales del piso -- la garantizada (StartPos/EndPos, ver el comentario de
        // PlaceStairsBetween) y los pares extra al azar -- para recortar el tramo del camino que
        // respeta FoeStairsExclusionRadius de todas ellas. Pedido puntual: el FOE tiene que arrancar
        // del lado OPUESTO a la escalera por la que el jugador sube a este piso (StartPos, ver
        // PlaceStairsBetween) -- FoeController.Initialize arranca desde el ULTIMO indice de esta
        // lista, que es el extremo del tramo mas cerca de EndPos, nunca de StartPos. El FOE aparece
        // SI O SI en TODOS los pisos salvo el primero (Index 0, piso de respiro).
        private void PlaceFoeRoute(DungeonFloor floor)
        {
            if (floor.Index == 0) return;

            var path = FindPath(floor, floor.StartPos, floor.EndPos);
            const int margin = 1;
            if (path.Count - margin * 2 < 4) return; // camino critico muy corto, sin FOE este piso

            var stairs = new List<(int x, int y)>();
            for (int x = 0; x < floor.Width; x++)
                for (int y = 0; y < floor.Height; y++)
                    if (floor.Cells[x, y].Type == CellType.StairsUp || floor.Cells[x, y].Type == CellType.StairsDown)
                        stairs.Add((x, y));

            bool FarFromAllStairs((int x, int y) c) => stairs.All(s =>
                Math.Max(Math.Abs(c.x - s.x), Math.Abs(c.y - s.y)) >= FoeStairsExclusionRadius);

            // Tramo contiguo mas largo (dentro del margen de siempre) que respeta el radio -- no
            // alcanza con filtrar celda por celda: StepAlongRoute camina la lista en orden, asi que
            // la ruta final tiene que seguir siendo un camino real sin saltos.
            int bestStart = -1, bestLen = 0, curStart = -1, curLen = 0;
            for (int idx = margin; idx < path.Count - margin; idx++)
            {
                if (FarFromAllStairs(path[idx]))
                {
                    if (curStart < 0) curStart = idx;
                    curLen++;
                    if (curLen > bestLen) { bestLen = curLen; bestStart = curStart; }
                }
                else
                {
                    curStart = -1;
                    curLen = 0;
                }
            }
            if (bestLen < 4) return; // ningun tramo lo bastante largo lejos de todas las escaleras

            floor.FoePatrolRoute = path.GetRange(bestStart, bestLen);
        }

        // Abre (o vuelve a cerrar) la pared de una puerta bloqueada especifica -- usado tanto por
        // el desbloqueo real (UnlockDoor) como por la validacion (simular abrir/cerrar de a una).
        private void SetDoorWallState(DungeonFloor floor, LockedDoor door, bool open)
        {
            floor.Cells[door.DoorX, door.DoorY].SetWall(door.DoorDir, !open);
            var (dx, dy) = door.DoorDir.Offset();
            int nx = door.DoorX + dx, ny = door.DoorY + dy;
            if (floor.InBounds(nx, ny)) floor.Cells[nx, ny].SetWall(door.DoorDir.Opposite(), !open);
        }

        // Activa la palanca de esta puerta bloqueada: a diferencia del ShortcutGate (que nunca abre
        // una pared, solo teletransporta), esto SI abre un paso real y permanente en el camino
        // principal -- una vez activada, el candado desaparece para el resto de la run.
        public void UnlockDoor(DungeonFloor floor, int doorIndex)
        {
            var door = floor.LockedDoors[doorIndex];
            if (door.IsUnlocked) return;
            door.IsUnlocked = true;
            SetDoorWallState(floor, door, true);
        }

        // Camino unico de Start a End (el mapa base es un arbol: no hay ciclos, asi que solo puede
        // haber un camino simple entre dos celdas cualesquiera). Devuelve la lista vacia si por
        // algun motivo no son alcanzables entre si (no deberia pasar).
        private List<(int x, int y)> FindPath(DungeonFloor floor, (int x, int y) from, (int x, int y) to)
        {
            var parent = new Dictionary<(int, int), (int, int)>();
            var visited = new HashSet<(int, int)> { from };
            var queue = new Queue<(int, int)>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (cur == to) break;
                var cell = floor.Cells[cur.Item1, cur.Item2];
                foreach (var dir in DirectionExtensions.All)
                {
                    if (cell.HasWall(dir)) continue;
                    var (ox, oy) = dir.Offset();
                    var next = (cur.Item1 + ox, cur.Item2 + oy);
                    if (!floor.InBounds(next.Item1, next.Item2) || visited.Contains(next)) continue;
                    visited.Add(next);
                    parent[next] = cur;
                    queue.Enqueue(next);
                }
            }

            var path = new List<(int, int)>();
            if (!visited.Contains(to)) return path;
            var walk = to;
            path.Add(walk);
            while (walk != from)
            {
                walk = parent[walk];
                path.Add(walk);
            }
            path.Reverse();
            return path;
        }

        // Direccion de a hacia b si son vecinas en la grilla; null si no lo son.
        private Direction? DirectionTo((int x, int y) a, (int x, int y) b)
        {
            foreach (var dir in DirectionExtensions.All)
            {
                var (ox, oy) = dir.Offset();
                if (a.x + ox == b.x && a.y + oy == b.y) return dir;
            }
            return null;
        }

        // Celdas Normal de grado 1 (puntas muertas reales), excluyendo salas de jefe y lo que ya
        // este en uso; si "within" no es null, ademas exige que la celda este en ese conjunto
        // (para pedir "un punto muerto alcanzable SIN cruzar tal puerta", por ejemplo).
        private List<(int x, int y)> FindLeavesWithin(DungeonFloor floor, HashSet<(int, int)> within, HashSet<(int, int)> exclude)
        {
            var result = new List<(int x, int y)>();
            for (int x = 0; x < floor.Width; x++)
            {
                for (int y = 0; y < floor.Height; y++)
                {
                    var cell = floor.Cells[x, y];
                    if (cell.Type != CellType.Normal || cell.IsBossRoom || cell.IsPuzzleTile || cell.IsPredefinedRoom) continue;
                    if (exclude.Contains((x, y))) continue;
                    if (within != null && !within.Contains((x, y))) continue;
                    if (Degree(floor, x, y) == 1) result.Add((x, y));
                }
            }
            return result;
        }

        // ---------- Validation ----------

        public (bool ok, List<string> issues) ValidateFloor(DungeonFloor floor)
        {
            var issues = new List<string>();

            // "Locked" = estado real con el que arranca el piso (candados obligatorios cerrados,
            // atajos opcionales cerrados). "Unlocked" simula TODOS los candados obligatorios ya
            // activados (los atajos opcionales se dejan cerrados a proposito: nunca deberian hacer
            // falta para la conectividad base) -- asi se puede comprobar por separado que (a) no se
            // llega a nada del otro lado de un candado sin activarlo, y (b) jugando normal (activando
            // las palancas que hagan falta) se termina llegando a absolutamente todo.
            var reachableLocked = BfsReachable(floor, floor.StartPos);

            foreach (var door in floor.LockedDoors) SetDoorWallState(floor, door, true);
            var reachableUnlocked = BfsReachable(floor, floor.StartPos);
            foreach (var door in floor.LockedDoors) SetDoorWallState(floor, door, false);

            // Las celdas Void son vacio intencional (podado): no cuentan como "deberian ser alcanzables".
            // La celda de la Puerta Fria (CellType.BiomeGate, ver PlaceBiomeGate) tampoco: a
            // proposito es inalcanzable caminando (ni activando todas las palancas), solo el
            // Perforador la abre, y eso no depende de ningun candado.
            int totalCells = CountNonVoid(floor) - (floor.BiomeGatePos.HasValue ? 1 : 0);

            if (reachableUnlocked.Count != totalCells)
                issues.Add($"Piso {floor.Index}: activando TODAS las palancas solo se llega a {reachableUnlocked.Count}/{totalCells} celdas (no-vacias).");

            if (!reachableUnlocked.Contains(floor.EndPos))
                issues.Add($"Piso {floor.Index}: End {floor.EndPos} NO alcanzable ni activando todas las palancas.");

            if (!reachableUnlocked.Contains(floor.SecondaryQuestPos))
                issues.Add($"Piso {floor.Index}: mision secundaria {floor.SecondaryQuestPos} NO alcanzable ni activando todas las palancas.");

            for (int di = 0; di < floor.LockedDoors.Count; di++)
            {
                var door = floor.LockedDoors[di];
                if (!floor.Cells[door.DoorX, door.DoorY].HasWall(door.DoorDir))
                    issues.Add($"Piso {floor.Index}: la puerta bloqueada {di} en ({door.DoorX},{door.DoorY}) esta abierta de entrada (deberia arrancar cerrada).");

                var (dx, dy) = door.DoorDir.Offset();
                var beyondPos = (door.DoorX + dx, door.DoorY + dy);

                if (reachableLocked.Contains(beyondPos))
                    issues.Add($"Piso {floor.Index}: se llega a {beyondPos} SIN activar la palanca {di} (el candado no esta bloqueando nada real).");

                if (!reachableLocked.Contains((door.LeverX, door.LeverY)))
                    issues.Add($"Piso {floor.Index}: la palanca {di} en ({door.LeverX},{door.LeverY}) NO es alcanzable sin cruzar su propia puerta (softlock).");

                if (door.LeverX == beyondPos.Item1 && door.LeverY == beyondPos.Item2)
                    issues.Add($"Piso {floor.Index}: la palanca {di} quedo DEL OTRO LADO de su propia puerta.");

                // Activar SOLO esta puerta (dejando las demas como estaban) debe abrir de verdad el
                // paso hacia beyondPos, sin depender de que otras palancas tambien esten activadas.
                SetDoorWallState(floor, door, true);
                var reachableWithThisDoor = BfsReachable(floor, floor.StartPos);
                SetDoorWallState(floor, door, false);
                if (!reachableWithThisDoor.Contains(beyondPos))
                    issues.Add($"Piso {floor.Index}: activar la palanca {di} no abrio un paso real hacia {beyondPos}.");
            }

            if (floor.TreasurePositions != null)
            {
                foreach (var treasurePos in floor.TreasurePositions)
                {
                    if (!reachableUnlocked.Contains(treasurePos))
                        issues.Add($"Piso {floor.Index}: un cofre en {treasurePos} NO es alcanzable ni activando todas las palancas.");
                    else if (Degree(floor, treasurePos.Item1, treasurePos.Item2) != 1)
                        issues.Add($"Piso {floor.Index}: el cofre en {treasurePos} no quedo en un punto muerto real (grado != 1) -- no exige desviarse del camino.");
                }
            }


            // Ningun par de puntos importantes deberia quedar pegado por una sola pared sin conexion.
            var importantPoints = new List<(string name, int x, int y)>
            {
                ("Start", floor.StartPos.x, floor.StartPos.y),
                ("End", floor.EndPos.x, floor.EndPos.y),
                ("SecondaryQuest", floor.SecondaryQuestPos.x, floor.SecondaryQuestPos.y),
            };
            foreach (var gate in floor.Gates)
            {
                importantPoints.Add(("Switch", gate.SwitchX, gate.SwitchY));
                importantPoints.Add(("Landing", gate.LandingX, gate.LandingY));
            }
            for (int i = 0; i < importantPoints.Count; i++)
            {
                for (int j = i + 1; j < importantPoints.Count; j++)
                {
                    var a = importantPoints[i];
                    var b = importantPoints[j];
                    if (IsAdjacentUnconnected(floor, (a.x, a.y), (b.x, b.y)))
                        issues.Add($"Piso {floor.Index}: {a.name} ({a.x},{a.y}) y {b.name} ({b.x},{b.y}) quedaron pegados por una sola pared, sin vacio ni conexion entre ellos.");
                }
            }

            for (int gi = 0; gi < floor.Gates.Count; gi++)
            {
                var gate = floor.Gates[gi];
                var switchCell = FindCellOfType(floor, CellType.ShortcutSwitch, gi);
                var landingCell = FindCellOfType(floor, CellType.ShortcutLanding, gi);

                if (switchCell == null)
                {
                    issues.Add($"Piso {floor.Index}: no se encontro celda switch para el gate {gi}.");
                    continue;
                }
                if (landingCell == null)
                {
                    issues.Add($"Piso {floor.Index}: no se encontro celda de llegada para el gate {gi}.");
                    continue;
                }
                if (switchCell.Value == landingCell.Value)
                    issues.Add($"Piso {floor.Index}: switch y punto de llegada del atajo {gi} son la misma celda.");

                if (!reachableUnlocked.Contains(switchCell.Value))
                    issues.Add($"Piso {floor.Index}: switch del atajo {gi} en {switchCell.Value} NO alcanzable (deberia serlo via la entrada larga).");
                if (!reachableUnlocked.Contains(landingCell.Value))
                    issues.Add($"Piso {floor.Index}: punto de llegada del atajo {gi} en {landingCell.Value} NO alcanzable desde Start.");

                // La pared entre ambos lados del gate debe seguir cerrada SIEMPRE (el vacio es
                // permanente); el atajo nunca debe convertirse en un paso caminable.
                if (!floor.Cells[gate.Ax, gate.Ay].HasWall(gate.DirFromA))
                    issues.Add($"Piso {floor.Index}: la pared del gate {gi} esta abierta (deberia quedar cerrada para siempre; el atajo es un teletransporte, no un paso).");

                bool teleportOkFromSwitch = TryGetTeleportTarget(floor, switchCell.Value.Item1, switchCell.Value.Item2, out int tx, out int ty);
                bool wasOpen = gate.IsOpen;
                if (!wasOpen)
                {
                    if (teleportOkFromSwitch)
                        issues.Add($"Piso {floor.Index}: el atajo {gi} deberia estar inactivo pero TryGetTeleportTarget devolvio un destino.");
                    OpenGate(floor, gi);
                    teleportOkFromSwitch = TryGetTeleportTarget(floor, switchCell.Value.Item1, switchCell.Value.Item2, out tx, out ty);
                    if (!teleportOkFromSwitch || (tx, ty) != landingCell.Value)
                        issues.Add($"Piso {floor.Index}: al activar el atajo {gi}, el switch no teletransporta al punto de llegada correcto.");
                    bool teleportOkFromLanding = TryGetTeleportTarget(floor, landingCell.Value.Item1, landingCell.Value.Item2, out int lx, out int ly);
                    if (!teleportOkFromLanding || (lx, ly) != switchCell.Value)
                        issues.Add($"Piso {floor.Index}: al activar el atajo {gi}, el punto de llegada no teletransporta al switch correcto.");
                    gate.IsOpen = false; // deja el estado limpio tras la simulacion
                }

                if (!string.IsNullOrEmpty(gate.RequiredLoreId))
                {
                    var loreCell = FindLoreCell(floor, gate.RequiredLoreId);
                    if (loreCell == null)
                    {
                        issues.Add($"Piso {floor.Index}: el atajo {gi} exige el lore '{gate.RequiredLoreId}' pero no hay ninguna celda Lore con ese id en el piso.");
                    }
                    else
                    {
                        if (!reachableUnlocked.Contains(loreCell.Value))
                            issues.Add($"Piso {floor.Index}: la celda Lore '{gate.RequiredLoreId}' en {loreCell.Value} no es alcanzable.");
                        if (floor.IsInIsolatedZone(loreCell.Value.Item1, loreCell.Value.Item2))
                            issues.Add($"Piso {floor.Index}: la celda Lore '{gate.RequiredLoreId}' esta DENTRO de la zona que su propio atajo acorta (el jugador no podria encontrarla sin ya haber cruzado).");
                    }
                }
            }

            return (issues.Count == 0, issues);
        }

        private (int, int)? FindCellOfType(DungeonFloor floor, CellType type, int gateIndex)
        {
            for (int x = 0; x < floor.Width; x++)
                for (int y = 0; y < floor.Height; y++)
                {
                    var c = floor.Cells[x, y];
                    if (c.Type == type && c.ControlledGateIndex == gateIndex)
                        return (x, y);
                }
            return null;
        }

        public (bool ok, List<string> issues) ValidateDungeon(List<DungeonFloor> floors)
        {
            var issues = new List<string>();
            foreach (var floor in floors)
            {
                var (ok, floorIssues) = ValidateFloor(floor);
                issues.AddRange(floorIssues);
            }

            for (int i = 0; i < floors.Count; i++)
            {
                var floor = floors[i];
                if (!floor.HasBossRoom) continue;

                bool hasBossMarker = floor.BossRoomCells.Any(c => floor.Cells[c.Item1, c.Item2].Type == CellType.Boss);
                if (!hasBossMarker)
                    issues.Add($"Piso {i}: es piso de jefe pero no se encontro la celda del jefe dentro de la sala.");

                // El piso de jefe FINAL de un bioma (el ultimo antes de cambiar a Biome distinto, o
                // el ultimo de toda la lista) no tiene escalera de subida propia -- no hay a donde
                // subir todavia dentro del mismo bioma. Sin este chequeo de Biome, el jefe final del
                // Bioma 1 exigia (a torcidas) una escalera hacia el piso 0 del Bioma 2, que nunca se
                // conectan por escalera (solo por la Puerta Fria).
                // El Bioma 4 se ramifica desde el salon: sus pisos de jefe (torre II y sotano II)
                // son extremos de rutas separadas, asi que el siguiente indice no representa un
                // piso por el que deban continuar sus escaleras.
                bool hasNextFloor = floor.Biome != 4 && i < floors.Count - 1 && floors[i + 1].Biome == floor.Biome;
                if (hasNextFloor)
                {
                    bool stairsInRoom = floor.BossRoomCells.Any(c => floor.Cells[c.Item1, c.Item2].Type == CellType.StairsUp);
                    if (!stairsInRoom)
                        issues.Add($"Piso {i}: es piso de jefe pero la escalera de subida no quedo dentro de la sala del jefe.");
                }
            }

            // Cross-floor reachability: BFS across floors using stair links, starting at floor0 Start.
            // Se simula TODAS las palancas activadas en TODOS los pisos (igual criterio que en
            // ValidateFloor): la conectividad de base nunca depende de los atajos opcionales, pero
            // SI depende de haber activado cada candado obligatorio en algun momento de la run.
            foreach (var f in floors)
                foreach (var door in f.LockedDoors)
                    SetDoorWallState(f, door, true);

            var visited = new HashSet<(int floorIdx, int x, int y)>();
            var queue = new Queue<(int, int, int)>();
            var startTuple = (0, floors[0].StartPos.x, floors[0].StartPos.y);
            visited.Add(startTuple);
            queue.Enqueue(startTuple);

            while (queue.Count > 0)
            {
                var (fi, x, y) = queue.Dequeue();
                var floor = floors[fi];
                var localReachable = BfsReachable(floor, (x, y));
                foreach (var (lx, ly) in localReachable)
                {
                    var key = (fi, lx, ly);
                    if (!visited.Contains(key))
                    {
                        visited.Add(key);
                        queue.Enqueue(key);
                    }
                    var cell = floor.Cells[lx, ly];
                    if ((cell.Type == CellType.StairsUp || cell.Type == CellType.StairsDown) && cell.StairTargetFloor >= 0)
                    {
                        var targetKey = (cell.StairTargetFloor, cell.StairTargetX, cell.StairTargetY);
                        if (!visited.Contains(targetKey))
                        {
                            visited.Add(targetKey);
                            queue.Enqueue(targetKey);
                        }
                    }
                }
            }

            // El Bioma 2 (Biome != 0, ver DungeonManager.GenerateAndEnterDungeon) y la celda de la
            // Puerta Fria que lleva a el son contenido OPCIONAL a proposito -- nunca hace falta
            // cruzarlos para resolver la mazmorra principal, asi que quedan afuera de la garantia
            // de resolubilidad: son un secreto de verdad, no una parte obligatoria del recorrido.
            int totalAllCells = floors.Where(f => f.Biome == 0).Sum(f => CountNonVoid(f) - (f.BiomeGatePos.HasValue ? 1 : 0));
            if (visited.Count != totalAllCells)
                issues.Add($"Multi-piso: solo {visited.Count}/{totalAllCells} celdas alcanzables cruzando todos los pisos desde el Start del piso 0 (con todas las palancas activadas).");

            foreach (var f in floors)
                foreach (var door in f.LockedDoors)
                    SetDoorWallState(f, door, false);

            return (issues.Count == 0, issues);
        }
    }
}
