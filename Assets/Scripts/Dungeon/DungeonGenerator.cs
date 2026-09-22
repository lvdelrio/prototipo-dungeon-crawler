using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen
{
    public class DungeonGenerator
    {
        // ---------- Public orchestration ----------

        public List<DungeonFloor> GenerateDungeon(int floorCount, int width, int height, int seed, float eventPercent, out List<string> log, IList<EventEntry> eventPool = null, int stairPairsPerFloor = 2, IList<int> eventCountsPerFloor = null, int bossFloorStart = 2, int bossFloorInterval = 2, float voidFraction = 0.4f, int dangerValueMin = 0, int dangerValueMax = 5, IList<string> loreIdPool = null)
        {
            log = new List<string>();
            var rng = new Random(seed);
            var floors = new List<DungeonFloor>();

            for (int i = 0; i < floorCount; i++)
            {
                var floor = GenerateFloor(width, height, i, rng);

                bool isBossFloor = bossFloorInterval > 0 && i >= bossFloorStart && (i - bossFloorStart) % bossFloorInterval == 0;
                if (isBossFloor)
                {
                    bool added = AddBossRoom(floor, rng);
                    log.Add(added
                        ? $"Piso {i}: sala de jefe en ({floor.BossRoomMinX},{floor.BossRoomMinY})-({floor.BossRoomMaxX},{floor.BossRoomMaxY}), jefe en {floor.BossPos}."
                        : $"Piso {i}: se pidio sala de jefe pero no hubo espacio libre (mapa muy chico).");
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
                bool pillarsAdded = PlacePacingPillars(floor, rng);
                log.Add(pillarsAdded
                    ? $"Piso {i}: candado en {floor.LockedDoors[0].DoorX},{floor.LockedDoors[0].DoorY} (palanca en {floor.LockedDoors[0].LeverX},{floor.LockedDoors[0].LeverY})."
                    : $"Piso {i}: camino critico muy corto/sin tramo libre, sin candado este piso.");

                // Cofre GARANTIZADO por piso (a diferencia del opcional de arriba, que solo aparece
                // si PlacePacingPillars encontro lugar): todo piso tiene que darle al jugador algo
                // que facilite la exploracion. Tiene que ir ANTES de podar, por la misma razon que
                // los pilares de pacing arriba.
                bool treasureAdded = EnsureTreasure(floor, rng);
                log.Add(treasureAdded
                    ? $"Piso {i}: cofre en {floor.TreasurePos}."
                    : $"Piso {i}: sin punto muerto libre para el cofre (mapa demasiado chico/denso).");

                PlaceFoeRoute(floor, rng);
                log.Add(floor.HasFoe
                    ? $"Piso {i}: FOE patrullando {floor.FoePatrolRoute.Count} celdas."
                    : $"Piso {i}: sin FOE este piso.");

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

            foreach (var floor in floors)
            {
                bool hasOverride = eventCountsPerFloor != null
                    && floor.Index < eventCountsPerFloor.Count
                    && eventCountsPerFloor[floor.Index] >= 0;

                if (hasOverride)
                    PlaceEventsExact(floor, rng, eventCountsPerFloor[floor.Index], eventPool);
                else
                    PlaceEvents(floor, rng, eventPercent, eventPool);

                AssignDangerValues(floor, rng, dangerValueMin, dangerValueMax);

                string loreId = (loreIdPool != null && loreIdPool.Count > 0)
                    ? loreIdPool[floor.Index % loreIdPool.Count]
                    : $"lore_piso{floor.Index}";
                AssignLoreLock(floor, rng, loreId);
            }

            return floors;
        }

        // Le da a cada celda Normal/Event un valor de peligro (0-5) al azar. Todas las demas
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
                    if (cell.Type == CellType.Normal || cell.Type == CellType.Event)
                        cell.DangerValue = rng.Next(lo, hi + 1);
                }
            }
        }

        // ---------- Single floor generation ----------

        public DungeonFloor GenerateFloor(int width, int height, int index, Random rng)
        {
            var floor = new DungeonFloor(width, height, index);

            // 1. Pick isolated zone rectangle (~20-32% of area), anchored at a random corner.
            float frac = 0.20f + (float)rng.NextDouble() * 0.12f;
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

            // 2. Carve outside region and inside region as two independent spanning trees.
            Carve(floor, (x, y) => !floor.IsInIsolatedZone(x, y), rng);
            Carve(floor, (x, y) => floor.IsInIsolatedZone(x, y), rng);

            // 3. Boundary edges between the two regions.
            var boundary = FindBoundaryEdges(floor);
            if (boundary.Count == 0)
                throw new InvalidOperationException("No se encontraron bordes entre zona aislada y el resto (dimensiones muy chicas).");

            Shuffle(boundary, rng);

            // Entrance: open permanently, connects the two trees into one.
            var entrance = boundary[0];
            OpenWallBetween(floor, entrance.ax, entrance.ay, entrance.dir);

            // Shortcut gate: pick a different edge, keep closed, register as a gate.
            var gateEdge = boundary.FirstOrDefault(e => !(e.ax == entrance.ax && e.ay == entrance.ay && e.dir == entrance.dir));
            if (gateEdge.Equals(default((int, int, Direction))))
                gateEdge = entrance; // extremely small maps fallback (shouldn't normally happen)

            var gate = new ShortcutGate
            {
                Ax = gateEdge.ax,
                Ay = gateEdge.ay,
                DirFromA = gateEdge.dir,
                IsOpen = false
            };
            var (dx, dy) = gateEdge.dir.Offset();
            gate.Bx = gateEdge.ax + dx;
            gate.By = gateEdge.ay + dy;
            floor.Gates.Add(gate);
            int gateIndex = floor.Gates.Count - 1;

            // 4. Start = farthest reachable cell from an outside-region seed cell; End = farthest cell from Start.
            var outsideSeed = FirstCellMatching(floor, (x, y) => !floor.IsInIsolatedZone(x, y)) ?? (0, 0);
            var (startPos, _) = FarthestCell(floor, outsideSeed);
            var (endPos, _) = FarthestCell(floor, startPos, new HashSet<(int, int)> { startPos });
            floor.StartPos = startPos;
            floor.EndPos = endPos;
            floor.Cells[startPos.x, startPos.y].Type = CellType.Start;
            floor.Cells[endPos.x, endPos.y].Type = CellType.End;

            // 5. Secondary quest room: a dead-end (degree 1) cell, preferably outside region, not Start/End,
            //    y preferentemente sin quedar pegada (sin paso) a Start o End.
            var secondary = FindDeadEnd(floor, preferOutside: true, exclude: new HashSet<(int, int)> { startPos, endPos, (gate.Bx, gate.By) },
                avoidAdjacentTo: new HashSet<(int, int)> { startPos, endPos });
            floor.SecondaryQuestPos = secondary;
            floor.Cells[secondary.Item1, secondary.Item2].Type = CellType.SecondaryQuest;

            // 6. Shortcut switch: inside cell of the gate; if occupied, find nearest free inside cell.
            //    La pared entre Ax/Ay y Bx/By NUNCA se abre: queda como el "vacio" permanente entre
            //    ambos lados. El atajo, una vez activado, teletransporta entre el switch y el punto
            //    de llegada en vez de dejar caminar a traves de esa pared.
            var switchPos = FindFreeCellNear(floor, (gate.Bx, gate.By), c => floor.IsInIsolatedZone(c.Item1, c.Item2), new HashSet<(int, int)> { startPos, endPos, secondary },
                avoidAdjacentTo: new HashSet<(int, int)> { startPos, endPos, secondary });
            floor.Cells[switchPos.Item1, switchPos.Item2].Type = CellType.ShortcutSwitch;
            floor.Cells[switchPos.Item1, switchPos.Item2].ControlledGateIndex = gateIndex;
            gate.SwitchX = switchPos.Item1;
            gate.SwitchY = switchPos.Item2;

            // 7. Punto de llegada: celda del lado de afuera, cerca del borde del gate.
            var landingPos = FindFreeCellNear(floor, (gate.Ax, gate.Ay), c => !floor.IsInIsolatedZone(c.Item1, c.Item2), new HashSet<(int, int)> { startPos, endPos, secondary, switchPos },
                avoidAdjacentTo: new HashSet<(int, int)> { startPos, endPos, secondary, switchPos });
            floor.Cells[landingPos.Item1, landingPos.Item2].Type = CellType.ShortcutLanding;
            floor.Cells[landingPos.Item1, landingPos.Item2].ControlledGateIndex = gateIndex;
            gate.LandingX = landingPos.Item1;
            gate.LandingY = landingPos.Item2;

            // Ultimo recurso: en mapas muy chicos puede no existir ninguna celda candidata que evite
            // quedar pegada a otro punto importante. Si igual quedo asi, en vez de dejarlos pegados
            // sin vacio ni conexion, los conectamos directamente (es preferible a violar la regla).
            RepairAdjacentImportantPoints(floor, startPos, endPos, secondary, switchPos, landingPos);

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

        private bool RectOverlapsIsolatedZone(DungeonFloor floor, int rx, int ry, int rw, int rh)
        {
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

        private ((int x, int y) pos, int dist) FarthestCell(DungeonFloor floor, (int x, int y) from, HashSet<(int, int)> avoidAdjacentTo = null)
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
                    if (dist[next] > bestOverallDist) { bestOverallDist = dist[next]; bestOverall = next; }
                    if (dist[next] > bestSafeDist && !ViolatesAdjacency(floor, next, avoidAdjacentTo))
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
                    if (preferOutside && floor.IsInIsolatedZone(x, y)) continue;
                    if (Degree(floor, x, y) == 1) candidates.Add((x, y));
                }
            }
            if (candidates.Count == 0)
            {
                // fallback: allow isolated zone / any degree-1 cell.
                for (int x = 0; x < floor.Width; x++)
                    for (int y = 0; y < floor.Height; y++)
                        if (!exclude.Contains((x, y)) && Degree(floor, x, y) == 1)
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
            if (!exclude.Contains(near) && floor.Cells[near.x, near.y].Type == CellType.Normal)
                found.Add(near);

            var visited = new HashSet<(int, int)> { near };
            var queue = new Queue<(int, int)>();
            queue.Enqueue(near);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!exclude.Contains(cur) && floor.Cells[cur.Item1, cur.Item2].Type == CellType.Normal)
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
            var lowerFree = (restrictLowerTo != null && restrictLowerTo.Count > 0)
                ? restrictLowerTo.Where(c => lower.Cells[c.Item1, c.Item2].Type == CellType.Normal).ToList()
                : FreeNormalCells(lower);
            var upperFree = FreeNormalCells(upper);
            Shuffle(lowerFree, rng);
            Shuffle(upperFree, rng);

            int count = Math.Min(pairCount, Math.Min(lowerFree.Count, upperFree.Count));
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

        // Excluye celdas de sala de jefe: ahi nunca deben caer eventos ni escaleras "genericas"
        // (las escaleras de la sala de jefe se fuerzan aparte, via restrictLowerTo).
        private List<(int, int)> FreeNormalCells(DungeonFloor floor)
        {
            var list = new List<(int, int)>();
            for (int x = 0; x < floor.Width; x++)
                for (int y = 0; y < floor.Height; y++)
                    if (floor.Cells[x, y].Type == CellType.Normal && !floor.Cells[x, y].IsBossRoom && !floor.Cells[x, y].IsTreasureRoom && !floor.Cells[x, y].IsTrapRoom)
                        list.Add((x, y));
            return list;
        }

        // ---------- Events ----------

        public void PlaceEvents(DungeonFloor floor, Random rng, float percent, IList<EventEntry> pool = null)
        {
            var free = FreeNormalCells(floor);
            int count = (int)Math.Round(free.Count * percent);
            PlaceEventsOnCells(floor, rng, count, pool, free);
        }

        public void PlaceEventsExact(DungeonFloor floor, Random rng, int count, IList<EventEntry> pool = null)
        {
            var free = FreeNormalCells(floor);
            PlaceEventsOnCells(floor, rng, count, pool, free);
        }

        private void PlaceEventsOnCells(DungeonFloor floor, Random rng, int count, IList<EventEntry> pool, List<(int, int)> free)
        {
            var effectivePool = (pool != null && pool.Count > 0) ? pool : EventTable.Entries;
            Shuffle(free, rng);
            count = Math.Max(0, Math.Min(count, free.Count));
            for (int i = 0; i < count; i++)
            {
                var (x, y) = free[i];
                var cell = floor.Cells[x, y];
                cell.Type = CellType.Event;
                cell.AssignedEvent = RollFromPool(effectivePool, rng);
            }
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

        // Cofre GARANTIZADO por piso: PlacePacingPillars de arriba solo pone uno si el candado se
        // pudo colocar Y sobraba otro punto muerto libre despues de reservar el suyo -- en la
        // practica bastantes pisos (camino corto, mapa chico) se quedaban sin ninguno. Este metodo
        // se llama SIEMPRE despues (ver GenerateDungeon) y no hace nada si ya hay uno; si no, busca
        // cualquier otro punto muerto libre del piso completo (sin restringir a "antes/despues de
        // la puerta" como el otro) y pone el cofre ahi. Solo puede fallar si el piso no tiene NINGUN
        // punto muerto libre, algo muy raro salvo mapas degenerados.
        private bool EnsureTreasure(DungeonFloor floor, Random rng)
        {
            if (floor.TreasurePos.HasValue) return true;

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

            var candidates = FindLeavesWithin(floor, null, used);
            if (candidates.Count == 0) return false;

            Shuffle(candidates, rng);
            var pos = candidates[0];

            // Intento (best-effort) de agrandar el cofre a un cuadrante 2x2, mas notorio que una
            // sola celda; si no hay forma segura de hacerlo, TryGrowTreasureRoom no toca nada y se
            // sigue con el cofre de 1 sola celda de siempre.
            TryGrowTreasureRoom(floor, pos, used, rng);

            floor.Cells[pos.x, pos.y].Type = CellType.Treasure;
            floor.Cells[pos.x, pos.y].IsTreasureRoom = true;
            floor.TreasurePos = pos;
            if (floor.TreasureRoomCells == null) floor.TreasureRoomCells = new List<(int, int)> { pos };
            return true;
        }

        // Ruta fija de patrulla para el FOE de este piso (enemigo fuerte que se pasea, ver
        // Gameplay/FoeController): un TRAMO del camino critico Start->End (nunca el camino entero,
        // deja margen antes/despues para no pisar los marcadores de Start/End). Cualquier celda
        // INTERNA de un camino entre 2 puntos protegidos tiene grado >= 2 por construccion (ver el
        // comentario de PruneToSparseMaze mas abajo), asi que la ruta sobrevive la poda sola, sin
        // necesitar proteccion extra como el candado/cofre. El FOE aparece SI O SI cada 2 pisos,
        // pero nunca en el primero (Index 0, piso de respiro): Index 1, 3, 5... si tienen, Index
        // 0, 2, 4... no -- alternancia fija, no probabilidad.
        private void PlaceFoeRoute(DungeonFloor floor, Random rng)
        {
            if (floor.Index % 2 != 1) return;

            var path = FindPath(floor, floor.StartPos, floor.EndPos);
            const int desiredLen = 6;
            const int margin = 1;
            int usable = path.Count - margin * 2;
            if (usable < 4) return; // camino critico muy corto, sin FOE este piso

            int len = Math.Min(desiredLen, usable);
            int maxStartIdx = path.Count - margin - len;
            if (maxStartIdx < margin) return;
            int startIdx = rng.Next(margin, maxStartIdx + 1);
            floor.FoePatrolRoute = path.GetRange(startIdx, len);
        }

        // Prueba las 4 orientaciones posibles de un cuadrante 2x2 que incluyan `pos` (arriba-
        // derecha, arriba-izquierda, abajo-derecha, abajo-izquierda) y se queda con la primera que
        // cumple TODO esto:
        //  - Las 3 celdas extra estan dentro del mapa, son Normal, no son sala de jefe, no estan en
        //    la zona aislada y no estan en `used` (Start/End/mision secundaria/candados/atajos).
        //  - El grado EXTERNO del bloque completo (aristas que cruzan del cuadrante hacia afuera)
        //    es exactamente 1 -- la MISMA garantia que un cofre de 1 sola celda (punto muerto real:
        //    hay que desviarse del camino a proposito, nunca se puede entrar "de paso" ni queda
        //    flotando desconectado). Fusionar celdas nunca cierra paredes existentes (solo abre las
        //    internas, igual que AddBossRoom), asi que esto no puede romper la conectividad general.
        // Si ninguna orientacion cumple, no hace nada (el cofre se queda de 1 sola celda).
        private void TryGrowTreasureRoom(DungeonFloor floor, (int x, int y) pos, HashSet<(int, int)> used, Random rng)
        {
            var orientations = new (int dx, int dy)[][]
            {
                new[] { (0, 0), (1, 0), (0, 1), (1, 1) },
                new[] { (0, 0), (-1, 0), (0, 1), (-1, 1) },
                new[] { (0, 0), (1, 0), (0, -1), (1, -1) },
                new[] { (0, 0), (-1, 0), (0, -1), (-1, -1) },
            };
            Shuffle(orientations, rng);

            foreach (var offsets in orientations)
            {
                var block = new List<(int x, int y)>();
                bool valid = true;
                foreach (var (dx, dy) in offsets)
                {
                    int nx = pos.x + dx, ny = pos.y + dy;
                    if (!floor.InBounds(nx, ny)) { valid = false; break; }
                    if ((nx, ny) != pos)
                    {
                        var extra = floor.Cells[nx, ny];
                        if (extra.Type != CellType.Normal || extra.IsBossRoom || extra.IsTrapRoom
                            || used.Contains((nx, ny)) || floor.IsInIsolatedZone(nx, ny))
                        { valid = false; break; }
                    }
                    block.Add((nx, ny));
                }
                if (!valid) continue;

                var blockSet = new HashSet<(int, int)>(block);
                int externalDegree = 0;
                foreach (var (bx, by) in block)
                    foreach (var dir in DirectionExtensions.All)
                    {
                        var (ox, oy) = dir.Offset();
                        if (blockSet.Contains((bx + ox, by + oy))) continue;
                        if (!floor.Cells[bx, by].HasWall(dir)) externalDegree++;
                    }
                if (externalDegree != 1) continue;

                foreach (var (bx, by) in block)
                {
                    var cell = floor.Cells[bx, by];
                    cell.IsTreasureRoom = true;
                    foreach (var dir in DirectionExtensions.All)
                    {
                        var (ox, oy) = dir.Offset();
                        if (blockSet.Contains((bx + ox, by + oy)))
                            cell.SetWall(dir, false);
                    }
                }

                floor.TreasureRoomCells = block;
                return;
            }
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
                    if (cell.Type != CellType.Normal || cell.IsBossRoom) continue;
                    if (exclude.Contains((x, y))) continue;
                    if (within != null && !within.Contains((x, y))) continue;
                    if (Degree(floor, x, y) == 1) result.Add((x, y));
                }
            }
            return result;
        }

        private EventEntry RollFromPool(IList<EventEntry> pool, Random rng)
        {
            int total = 0;
            foreach (var e in pool) total += Math.Max(1, e.Weight);
            int roll = rng.Next(total);
            int acc = 0;
            foreach (var e in pool)
            {
                acc += Math.Max(1, e.Weight);
                if (roll < acc) return e;
            }
            return pool[pool.Count - 1];
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
            int totalCells = CountNonVoid(floor);

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

            if (floor.TreasurePos.HasValue)
            {
                var treasurePos = floor.TreasurePos.Value;
                if (!reachableUnlocked.Contains(treasurePos))
                    issues.Add($"Piso {floor.Index}: el cofre en {treasurePos} NO es alcanzable ni activando todas las palancas.");

                if (floor.HasTreasureRoom2x2)
                {
                    // Cuadrante 2x2 (TryGrowTreasureRoom): la garantia de "punto muerto real" se
                    // mide sobre el BLOQUE completo, no celda por celda -- tiene que tener
                    // exactamente una arista que cruce hacia afuera del cuadrante.
                    var blockSet = new HashSet<(int, int)>(floor.TreasureRoomCells);
                    int externalDegree = 0;
                    foreach (var (bx, by) in floor.TreasureRoomCells)
                        foreach (var dir in DirectionExtensions.All)
                        {
                            var (ox, oy) = dir.Offset();
                            if (blockSet.Contains((bx + ox, by + oy))) continue;
                            if (!floor.Cells[bx, by].HasWall(dir)) externalDegree++;
                        }
                    if (externalDegree != 1)
                        issues.Add($"Piso {floor.Index}: el cuadrante 2x2 del cofre no quedo como un unico punto muerto real (grado externo del bloque = {externalDegree}, deberia ser 1).");
                }
                else if (Degree(floor, treasurePos.Item1, treasurePos.Item2) != 1)
                {
                    issues.Add($"Piso {floor.Index}: el cofre en {treasurePos} no quedo en un punto muerto real (grado != 1) -- no exige desviarse del camino.");
                }
            }

            if (floor.HasBossRoom)
            {
                foreach (var (bx, by) in floor.BossRoomCells)
                {
                    if (floor.Cells[bx, by].Type == CellType.Event)
                        issues.Add($"Piso {floor.Index}: hay un evento en ({bx},{by}), dentro de la sala de jefe (no deberia haber eventos ahi).");
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

                bool hasNextFloor = i < floors.Count - 1;
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

            int totalAllCells = floors.Sum(f => CountNonVoid(f));
            if (visited.Count != totalAllCells)
                issues.Add($"Multi-piso: solo {visited.Count}/{totalAllCells} celdas alcanzables cruzando todos los pisos desde el Start del piso 0 (con todas las palancas activadas).");

            foreach (var f in floors)
                foreach (var door in f.LockedDoors)
                    SetDoorWallState(f, door, false);

            return (issues.Count == 0, issues);
        }
    }
}
