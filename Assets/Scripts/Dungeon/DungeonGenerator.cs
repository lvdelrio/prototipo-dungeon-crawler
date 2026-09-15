using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen
{
    public class DungeonGenerator
    {
        // ---------- Public orchestration ----------

        public List<DungeonFloor> GenerateDungeon(int floorCount, int width, int height, int seed, float eventPercent, out List<string> log, IList<EventEntry> eventPool = null, int stairPairsPerFloor = 2, IList<int> eventCountsPerFloor = null, int bossFloorStart = 2, int bossFloorInterval = 2, float voidFraction = 0.4f)
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
            }

            return floors;
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

        // ---------- Poda de pasillos (asi el mapa deja celdas como vacio real, no un laberinto perfecto) ----------

        // Convierte en "Void" (roca solida, no caminable, no se renderiza) puntas muertas del arbol
        // de expansion que no son necesarias para llegar a ningun punto importante. Como solo se
        // podan hojas (grado 1) que no estan protegidas, el camino entre dos puntos protegidos
        // cualesquiera SIEMPRE sigue intacto (en un arbol, todo nodo intermedio de un camino tiene
        // grado >= 2, nunca puede volverse hoja). Devuelve cuantas celdas quedaron vacias.
        private int PruneToSparseMaze(DungeonFloor floor, Random rng, float voidFraction)
        {
            if (voidFraction <= 0f) return 0;

            var protectedCells = new HashSet<(int, int)> { floor.StartPos, floor.EndPos, floor.SecondaryQuestPos };
            if (floor.HasBossRoom)
                foreach (var c in floor.BossRoomCells) protectedCells.Add(c);
            foreach (var gate in floor.Gates)
            {
                protectedCells.Add((gate.SwitchX, gate.SwitchY));
                protectedCells.Add((gate.LandingX, gate.LandingY));
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
                    if (floor.Cells[x, y].Type == CellType.Normal && !floor.Cells[x, y].IsBossRoom)
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
            var reachableClosed = BfsReachable(floor, floor.StartPos);

            // Las celdas Void son vacio intencional (podado): no cuentan como "deberian ser alcanzables".
            int totalCells = CountNonVoid(floor);

            if (reachableClosed.Count != totalCells)
                issues.Add($"Piso {floor.Index}: solo {reachableClosed.Count}/{totalCells} celdas (no-vacias) alcanzables con el atajo cerrado.");

            if (!reachableClosed.Contains(floor.EndPos))
                issues.Add($"Piso {floor.Index}: End {floor.EndPos} NO alcanzable desde Start.");

            if (!reachableClosed.Contains(floor.SecondaryQuestPos))
                issues.Add($"Piso {floor.Index}: mision secundaria {floor.SecondaryQuestPos} NO alcanzable desde Start.");

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

                if (!reachableClosed.Contains(switchCell.Value))
                    issues.Add($"Piso {floor.Index}: switch del atajo {gi} en {switchCell.Value} NO alcanzable (deberia serlo via la entrada larga).");
                if (!reachableClosed.Contains(landingCell.Value))
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
                issues.Add($"Multi-piso: solo {visited.Count}/{totalAllCells} celdas alcanzables cruzando todos los pisos desde el Start del piso 0.");

            return (issues.Count == 0, issues);
        }
    }
}
