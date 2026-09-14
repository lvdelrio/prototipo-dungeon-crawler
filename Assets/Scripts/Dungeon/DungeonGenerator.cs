using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen
{
    public class DungeonGenerator
    {
        // ---------- Public orchestration ----------

        public List<DungeonFloor> GenerateDungeon(int floorCount, int width, int height, int seed, float eventPercent, out List<string> log, IList<EventEntry> eventPool = null, int stairPairsPerFloor = 2)
        {
            log = new List<string>();
            var rng = new Random(seed);
            var floors = new List<DungeonFloor>();

            for (int i = 0; i < floorCount; i++)
            {
                var floor = GenerateFloor(width, height, i, rng);
                floors.Add(floor);
                log.Add($"Piso {i}: maze generado. Start={floor.StartPos} End={floor.EndPos} SecundariaQuest={floor.SecondaryQuestPos} ZonaAislada=({floor.IsoMinX},{floor.IsoMinY})-({floor.IsoMaxX},{floor.IsoMaxY}) Gates={floor.Gates.Count}");
            }

            for (int i = 0; i < floorCount - 1; i++)
            {
                PlaceStairsBetween(floors[i], floors[i + 1], rng, pairCount: stairPairsPerFloor);
                log.Add($"Escaleras piso {i} <-> {i + 1} colocadas ({stairPairsPerFloor} pares).");
            }

            foreach (var floor in floors)
            {
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
            var (endPos, _) = FarthestCell(floor, startPos);
            floor.StartPos = startPos;
            floor.EndPos = endPos;
            floor.Cells[startPos.x, startPos.y].Type = CellType.Start;
            floor.Cells[endPos.x, endPos.y].Type = CellType.End;

            // 5. Secondary quest room: a dead-end (degree 1) cell, preferably outside region, not Start/End.
            var secondary = FindDeadEnd(floor, preferOutside: true, exclude: new HashSet<(int, int)> { startPos, endPos, (gate.Bx, gate.By) });
            floor.SecondaryQuestPos = secondary;
            floor.Cells[secondary.Item1, secondary.Item2].Type = CellType.SecondaryQuest;

            // 6. Shortcut switch: inside cell of the gate; if occupied, find nearest free inside cell.
            var switchPos = FindFreeCellNear(floor, (gate.Bx, gate.By), c => floor.IsInIsolatedZone(c.Item1, c.Item2), new HashSet<(int, int)> { startPos, endPos, secondary });
            floor.Cells[switchPos.Item1, switchPos.Item2].Type = CellType.ShortcutSwitch;
            floor.Cells[switchPos.Item1, switchPos.Item2].ControlledGateIndex = gateIndex;

            return floor;
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

        public void OpenGate(DungeonFloor floor, int gateIndex)
        {
            var gate = floor.Gates[gateIndex];
            if (gate.IsOpen) return;
            OpenWallBetween(floor, gate.Ax, gate.Ay, gate.DirFromA);
            gate.IsOpen = true;
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

        private ((int x, int y) pos, int dist) FarthestCell(DungeonFloor floor, (int x, int y) from)
        {
            var dist = new Dictionary<(int, int), int>();
            var queue = new Queue<(int, int)>();
            dist[from] = 0;
            queue.Enqueue(from);
            (int, int) best = from;
            int bestDist = 0;

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
                    if (dist[next] > bestDist) { bestDist = dist[next]; best = next; }
                    queue.Enqueue(next);
                }
            }

            return (best, bestDist);
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

        private (int, int) FindDeadEnd(DungeonFloor floor, bool preferOutside, HashSet<(int, int)> exclude)
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
            return candidates[0];
        }

        private (int, int) FindFreeCellNear(DungeonFloor floor, (int x, int y) near, Func<(int, int), bool> region, HashSet<(int, int)> exclude)
        {
            if (!exclude.Contains(near) && floor.Cells[near.x, near.y].Type == CellType.Normal)
                return near;

            var visited = new HashSet<(int, int)> { near };
            var queue = new Queue<(int, int)>();
            queue.Enqueue(near);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!exclude.Contains(cur) && floor.Cells[cur.Item1, cur.Item2].Type == CellType.Normal)
                    return cur;
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
            throw new InvalidOperationException("No se encontro celda libre cerca del portón para el switch.");
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

        public void PlaceStairsBetween(DungeonFloor lower, DungeonFloor upper, Random rng, int pairCount)
        {
            var lowerFree = FreeNormalCells(lower);
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

        private List<(int, int)> FreeNormalCells(DungeonFloor floor)
        {
            var list = new List<(int, int)>();
            for (int x = 0; x < floor.Width; x++)
                for (int y = 0; y < floor.Height; y++)
                    if (floor.Cells[x, y].Type == CellType.Normal)
                        list.Add((x, y));
            return list;
        }

        // ---------- Events ----------

        public void PlaceEvents(DungeonFloor floor, Random rng, float percent, IList<EventEntry> pool = null)
        {
            var effectivePool = (pool != null && pool.Count > 0) ? pool : EventTable.Entries;
            var free = FreeNormalCells(floor);
            Shuffle(free, rng);
            int count = (int)Math.Round(free.Count * percent);
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

            int totalCells = floor.Width * floor.Height;
            if (reachableClosed.Count != totalCells)
                issues.Add($"Piso {floor.Index}: solo {reachableClosed.Count}/{totalCells} celdas alcanzables con el atajo cerrado.");

            if (!reachableClosed.Contains(floor.EndPos))
                issues.Add($"Piso {floor.Index}: End {floor.EndPos} NO alcanzable desde Start.");

            if (!reachableClosed.Contains(floor.SecondaryQuestPos))
                issues.Add($"Piso {floor.Index}: mision secundaria {floor.SecondaryQuestPos} NO alcanzable desde Start.");

            for (int gi = 0; gi < floor.Gates.Count; gi++)
            {
                var gate = floor.Gates[gi];
                var switchCell = FindCellOfType(floor, CellType.ShortcutSwitch, gi);
                if (switchCell == null)
                {
                    issues.Add($"Piso {floor.Index}: no se encontro celda switch para el gate {gi}.");
                    continue;
                }
                if (!reachableClosed.Contains(switchCell.Value))
                    issues.Add($"Piso {floor.Index}: switch del atajo {gi} en {switchCell.Value} NO alcanzable con el atajo cerrado (deberia serlo via la entrada larga).");

                // simulate opening and confirm shortcut actually shortens the path
                bool wasOpen = gate.IsOpen;
                if (!wasOpen)
                {
                    OpenGate(floor, gi);
                    var reachableOpen = BfsReachable(floor, floor.StartPos);
                    if (reachableOpen.Count != totalCells)
                        issues.Add($"Piso {floor.Index}: tras abrir el atajo {gi} igual faltan celdas alcanzables ({reachableOpen.Count}/{totalCells}).");
                    // close it back for a clean state after the check (undo simulation)
                    floor.Cells[gate.Ax, gate.Ay].SetWall(gate.DirFromA, true);
                    floor.Cells[gate.Bx, gate.By].SetWall(gate.DirFromA.Opposite(), true);
                    gate.IsOpen = false;
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

            int totalAllCells = floors.Sum(f => f.Width * f.Height);
            if (visited.Count != totalAllCells)
                issues.Add($"Multi-piso: solo {visited.Count}/{totalAllCells} celdas alcanzables cruzando todos los pisos desde el Start del piso 0.");

            return (issues.Count == 0, issues);
        }
    }
}
