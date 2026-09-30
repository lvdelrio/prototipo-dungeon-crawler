using System.Collections.Generic;
using System.Linq;
using DungeonGen;
using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// Ratones ligeros que cruzan el salon del castillo y desaparecen por la escalera al sotano.
    /// Cada uno sigue su PROPIA ruta BFS fija (ver Initialize, no pueden atravesar paredes), pero
    /// el movimiento entre waypoints se resuelve como boids (separacion/alineacion/cohesion contra
    /// los demas ratones activos, ver ComputeFlockSteer) en vez de un MoveTowards ciego -- asi, si
    /// dos rutas se cruzan cerca de la escalera, se esquivan y se agrupan como manada de verdad en
    /// vez de superponerse.
    /// </summary>
    public sealed class CastleMouseAmbient : MonoBehaviour
    {
        private sealed class Mouse
        {
            public Transform root;
            public Vector3[] path;
            public int next;
            public Vector3 position;
            public Vector3 velocity;
            public float speed;
            public float phase;
            public float respawnAt;
        }

        private readonly List<Mouse> _mice = new List<Mouse>();
        private static Material _furMaterial;
        private static Material _earMaterial;
        private float _cellSize = 4f;
        // Caja que contiene todas las rutas BFS (union de waypoints de cada raton) con un margen --
        // misma proteccion que ForestCritterAmbient contra que el boid steering (ComputeFlockSteer)
        // empuje a alguno fuera del salon por un rato largo.
        private Vector3 _boundsMin;
        private Vector3 _boundsMax;
        private bool _hasBounds;

        public void Initialize(DungeonFloor floor, float cellSize)
        {
            if (floor == null || floor.CastleRegion != 0) return;
            _cellSize = Mathf.Max(1f, cellSize);
            float boundsMargin = _cellSize * 1.5f;
            _boundsMin = new Vector3(-boundsMargin, 0f, -boundsMargin);
            _boundsMax = new Vector3((floor.Width - 1) * cellSize + boundsMargin, 0f, (floor.Height - 1) * cellSize + boundsMargin);
            _hasBounds = true;
            (int x, int y)? stairs = null;
            for (int x = 0; x < floor.Width && stairs == null; x++)
            for (int y = 0; y < floor.Height; y++)
                if (floor.Cells[x, y].Type == CellType.StairsDown)
                {
                    stairs = (x, y);
                    break;
                }
            if (!stairs.HasValue)
            {
                Debug.Log("[WildlifeDebug] CastleMouseAmbient: no se encontro StairsDown en el salon.");
                return;
            }

            int[,] distance = new int[floor.Width, floor.Height];
            int[,] nextX = new int[floor.Width, floor.Height];
            int[,] nextY = new int[floor.Width, floor.Height];
            for (int x = 0; x < floor.Width; x++)
            for (int y = 0; y < floor.Height; y++) distance[x, y] = -1;

            var queue = new Queue<(int x, int y)>();
            var goal = stairs.Value;
            distance[goal.x, goal.y] = 0;
            queue.Enqueue(goal);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var direction in DirectionExtensions.All)
                {
                    var offset = direction.Offset();
                    int nx = current.x + offset.dx;
                    int ny = current.y + offset.dy;
                    if (!floor.InBounds(nx, ny) || distance[nx, ny] >= 0) continue;
                    var from = floor.Cells[current.x, current.y];
                    var to = floor.Cells[nx, ny];
                    if (to.Type == CellType.Void || from.HasWall(direction) || to.HasWall(direction.Opposite())) continue;
                    distance[nx, ny] = distance[current.x, current.y] + 1;
                    nextX[nx, ny] = current.x;
                    nextY[nx, ny] = current.y;
                    queue.Enqueue((nx, ny));
                }
            }

            var starts = new List<(int x, int y, int distance)>();
            for (int x = 0; x < floor.Width; x++)
            for (int y = 0; y < floor.Height; y++)
            {
                var cell = floor.Cells[x, y];
                if (distance[x, y] >= 4 && cell.Type != CellType.Void && !cell.IsBossRoom
                    && cell.Type != CellType.StairsUp && cell.Type != CellType.StairsDown)
                    starts.Add((x, y, distance[x, y]));
            }
            starts = starts.OrderByDescending(c => c.distance).ToList();
            var usedStarts = new List<(int x, int y)>();
            int count = Mathf.Min(3, starts.Count);
            for (int i = 0; i < count; i++)
            {
                var start = starts[i];
                if (usedStarts.Any(p => Mathf.Abs(p.x - start.x) + Mathf.Abs(p.y - start.y) < 3)) continue;
                usedStarts.Add((start.x, start.y));
                var route = new List<Vector3>();
                int x = start.x, y = start.y;
                route.Add(new Vector3(x * cellSize, 0f, y * cellSize));
                int guard = floor.Width * floor.Height;
                while ((x != goal.x || y != goal.y) && guard-- > 0)
                {
                    int tx = nextX[x, y], ty = nextY[x, y];
                    if (tx == x && ty == y) break;
                    x = tx;
                    y = ty;
                    route.Add(new Vector3(x * cellSize, 0f, y * cellSize));
                }
                if (route.Count < 4 || x != goal.x || y != goal.y) continue;
                CreateMouse(route.ToArray(), cellSize, i);
            }
            Debug.Log($"[WildlifeDebug] CastleMouseAmbient: {_mice.Count} raton(es) creado(s), starts candidatos={starts.Count}.");
        }

        // Para MinimapUI (pedido puntual: "que los boids se vean en el mapa de debug") -- salteando
        // los que estan esperando su respawn (SetActive(false), ver Update), que en el mundo 3D
        // tampoco se ven.
        public void CollectPositions(List<Vector3> results)
        {
            for (int i = 0; i < _mice.Count; i++)
            {
                var mouse = _mice[i];
                if (mouse.root != null && mouse.root.gameObject.activeSelf)
                    results.Add(mouse.position);
            }
        }

        private void CreateMouse(Vector3[] route, float cellSize, int index)
        {
            var root = new GameObject($"CastleMouse_{index}").transform;
            root.SetParent(transform, false);
            root.position = route[0];
            root.localScale = Vector3.one * Mathf.Clamp(cellSize / 2.8f, 0.65f, 1.2f);
            var fur = Palette(ref _furMaterial, new Color(0.24f, 0.2f, 0.17f));
            var pink = Palette(ref _earMaterial, new Color(0.5f, 0.27f, 0.27f));
            AddPart(root, PrimitiveType.Sphere, "Body", new Vector3(0f, 0.075f, 0f), new Vector3(0.22f, 0.12f, 0.34f), fur);
            AddPart(root, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.105f, 0.13f), new Vector3(0.15f, 0.12f, 0.15f), fur);
            AddPart(root, PrimitiveType.Sphere, "EarLeft", new Vector3(-0.052f, 0.17f, 0.16f), new Vector3(0.065f, 0.07f, 0.035f), pink);
            AddPart(root, PrimitiveType.Sphere, "EarRight", new Vector3(0.052f, 0.17f, 0.16f), new Vector3(0.065f, 0.07f, 0.035f), pink);
            var tail = AddPart(root, PrimitiveType.Cylinder, "Tail", new Vector3(0f, 0.055f, -0.18f), new Vector3(0.018f, 0.18f, 0.018f), pink);
            tail.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _mice.Add(new Mouse
            {
                root = root,
                path = route,
                next = 1,
                position = route[0],
                velocity = Vector3.zero,
                speed = 1.05f + index * 0.13f,
                phase = index * 1.7f,
            });
        }

        private static Transform AddPart(Transform parent, PrimitiveType shape, string name, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            var collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            return part.transform;
        }

        private static Material Palette(ref Material material, Color color)
        {
            if (material != null) return material;
            var shader = Shader.Find("Standard");
            material = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { color = color };
            return material;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            // Mismos radios de enjambre que ForestCritterAmbient.ComputeFlockSteer, pero
            // proporcionales al ratón (mas chico y mas al ras del piso que una mariposa).
            float neighborRadiusSqr = (_cellSize * 0.9f) * (_cellSize * 0.9f);
            float separationRadiusSqr = (_cellSize * 0.3f) * (_cellSize * 0.3f);

            for (int i = 0; i < _mice.Count; i++)
            {
                var mouse = _mice[i];
                if (mouse.root == null) continue;
                if (mouse.respawnAt > 0f)
                {
                    if (Time.time < mouse.respawnAt) continue;
                    mouse.root.gameObject.SetActive(true);
                    mouse.position = mouse.path[0];
                    mouse.velocity = Vector3.zero;
                    mouse.root.position = mouse.position;
                    mouse.next = 1;
                    mouse.respawnAt = 0f;
                }

                if (mouse.next >= mouse.path.Length)
                {
                    mouse.root.gameObject.SetActive(false);
                    mouse.respawnAt = Time.time + 2.5f + mouse.phase;
                    continue;
                }

                // Sigue su propia ruta BFS (unica forma de no atravesar paredes), pero la
                // aceleracion hacia el proximo waypoint se combina con boids contra el resto de la
                // manada (ver ComputeFlockSteer) en vez de ir directo como un riel.
                Vector3 target = mouse.path[mouse.next];
                Vector3 seek = target - mouse.position;
                Vector3 desiredVelocity = seek.sqrMagnitude > 0.0001f ? seek.normalized * mouse.speed : Vector3.zero;
                Vector3 steer = ComputeFlockSteer(i, neighborRadiusSqr, separationRadiusSqr);
                Vector3 acceleration = (desiredVelocity - mouse.velocity) * 2.5f + steer;

                mouse.velocity += acceleration * dt;
                if (mouse.velocity.sqrMagnitude > mouse.speed * mouse.speed)
                    mouse.velocity = mouse.velocity.normalized * mouse.speed;

                mouse.position += mouse.velocity * dt;
                if (_hasBounds)
                {
                    mouse.position.x = Mathf.Clamp(mouse.position.x, _boundsMin.x, _boundsMax.x);
                    mouse.position.z = Mathf.Clamp(mouse.position.z, _boundsMin.z, _boundsMax.z);
                }
                mouse.root.position = mouse.position;
                if (mouse.velocity.sqrMagnitude > 0.0001f)
                    mouse.root.rotation = Quaternion.Slerp(mouse.root.rotation, Quaternion.LookRotation(mouse.velocity, Vector3.up), dt * 6f);

                if ((mouse.position - target).sqrMagnitude < 0.0064f) mouse.next++;
            }
        }

        // Separacion + alineacion + cohesion contra el resto de los ratones ACTIVOS (los que estan
        // esperando su respawn no cuentan como vecinos, ver mouse.respawnAt en Update): a lo sumo 3
        // ratones por piso (ver Initialize), O(n^2) es gratis.
        private Vector3 ComputeFlockSteer(int index, float neighborRadiusSqr, float separationRadiusSqr)
        {
            var mouse = _mice[index];
            Vector3 separation = Vector3.zero;
            Vector3 avgVelocity = Vector3.zero;
            Vector3 avgPosition = Vector3.zero;
            int neighborCount = 0;
            for (int j = 0; j < _mice.Count; j++)
            {
                if (j == index) continue;
                var other = _mice[j];
                if (other.root == null || !other.root.gameObject.activeSelf) continue;
                Vector3 offset = mouse.position - other.position;
                float distSqr = offset.sqrMagnitude;
                if (distSqr > neighborRadiusSqr || distSqr < 0.0001f) continue;
                neighborCount++;
                avgVelocity += other.velocity;
                avgPosition += other.position;
                if (distSqr < separationRadiusSqr)
                    separation += offset / distSqr;
            }
            if (neighborCount == 0) return separation * 2.5f;

            Vector3 alignment = (avgVelocity / neighborCount) - mouse.velocity;
            Vector3 cohesion = (avgPosition / neighborCount) - mouse.position;
            return separation * 2.5f + alignment * 0.5f + cohesion * 0.3f;
        }
    }
}
