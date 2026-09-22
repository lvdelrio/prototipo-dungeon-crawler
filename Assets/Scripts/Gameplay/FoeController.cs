using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DungeonGen;

namespace Gameplay
{
    // Enemigo fuerte que patrulla un TRAMO fijo del piso (DungeonFloor.FoePatrolRoute, ver
    // DungeonGenerator.PlaceFoeRoute) yendo y viniendo -- evitable, no persigue por defecto. Cada
    // vez que el jugador da un paso, el FOE da UN paso tambien (ver AdvanceStep): si eso lo deja
    // en la misma celda, quien llama arranca un combate 1 contra 1 mas dificil de huir (ver
    // CombatManager.StartFoeEncounter). Blanco y tranquilo mientras patrulla; si te VE (linea
    // recta sin pared en el medio, ver CanSeePlayer) se pinta rojo y te persigue DE VERDAD usando
    // BFS (ver StepToward) en vez de un greedy que se traba en esquinas. Deja de perseguir si te
    // alejas lo suficiente de su ruta Y ademas ya no te ve (ver LoseRouteDistance). Nunca
    // desaparece del piso salvo que lo derrotes en combate -- si cambias de piso, guarda su
    // estado (ver SaveStateTo) y lo retoma exacto si volves.
    public class FoeController : MonoBehaviour
    {
        // Alcance de vision en linea recta (fila/columna compartida, sin pared cerrada en el
        // medio) -- no es un radio omnidireccional, tiene que "mirar" por el pasillo.
        private const int VisionRange = 6;

        // Si el jugador se aleja de la ruta de patrulla mas de esto Y ya no esta a la vista, el
        // FOE deja de perseguir y retoma la patrulla desde donde quedo.
        private const int LoseRouteDistance = 3;

        // Huida exitosa: cuantos pasos del jugador quedan "gratis" (el FOE no actua) despues del
        // empuje hacia atras -- le da tiempo real de sacarle distancia.
        private const int FleeStunSteps = 2;

        private static readonly Color CalmColor = Color.white;
        private static readonly Color ChaseColor = new Color(0.85f, 0.12f, 0.1f);

        // Altura fija (por encima del piso de la celda) a la que flota la esfera -- mas o menos a
        // la altura de la vista del jugador, para que se vea de frente al cruzarse en un pasillo.
        private const float FloatHeight = 0.9f;

        private DungeonFloor _floor;
        private System.Func<int, int, Direction, bool> _canMove;
        private System.Func<int, int, Vector3> _cellToWorld;
        private int _routeIndex;
        private int _routeDir = 1;
        private int _stunnedSteps;
        private Renderer _renderer;
        private int _hp;
        private int _maxHp;

        public int X { get; private set; }
        public int Y { get; private set; }
        public bool IsChasing { get; private set; }

        public void Initialize(DungeonFloor floor, System.Func<int, int, Direction, bool> canMove,
            System.Func<int, int, Vector3> cellToWorld, float cellSize, int maxHp)
        {
            _floor = floor;
            _canMove = canMove;
            _cellToWorld = cellToWorld;
            _maxHp = maxHp;

            if (floor.FoeSavedX >= 0)
            {
                X = floor.FoeSavedX;
                Y = floor.FoeSavedY;
                _hp = floor.FoeSavedHp;
                IsChasing = floor.FoeSavedIsChasing;
                _routeIndex = floor.FoeSavedRouteIndex;
                _routeDir = floor.FoeSavedRouteDir;
                _stunnedSteps = floor.FoeSavedStunnedSteps;
            }
            else
            {
                _routeIndex = 0;
                _routeDir = 1;
                _hp = maxHp;
                (X, Y) = floor.FoePatrolRoute[0];
            }

            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "FoeVisual";
            visual.transform.SetParent(transform, false);
            var col = visual.GetComponent<Collider>();
            if (col != null) Destroy(col);
            visual.transform.localScale = Vector3.one * (cellSize * 0.55f);
            _renderer = visual.GetComponent<Renderer>();
            _renderer.material.color = IsChasing ? ChaseColor : CalmColor;

            transform.position = cellToWorld(X, Y) + Vector3.up * FloatHeight;
        }

        // Se llama 1 vez por cada paso que da el jugador. Devuelve true si el FOE quedo en la
        // MISMA celda que el jugador (colision) -- quien llama es responsable de arrancar el combate.
        public bool AdvanceStep(int playerX, int playerY)
        {
            if (_stunnedSteps > 0)
            {
                _stunnedSteps--;
                return false; // aturdido tras una huida: no detecta ni se mueve este paso
            }

            bool canSee = CanSeePlayer(playerX, playerY);
            if (!IsChasing && canSee)
            {
                IsChasing = true;
            }
            else if (IsChasing && !canSee)
            {
                int distFromRoute = _floor.FoePatrolRoute.Min(c => Chebyshev(c.x, c.y, playerX, playerY));
                if (distFromRoute > LoseRouteDistance) IsChasing = false;
            }

            if (IsChasing) StepToward(playerX, playerY);
            else StepAlongRoute();

            transform.position = _cellToWorld(X, Y) + Vector3.up * FloatHeight;
            _renderer.material.color = IsChasing ? ChaseColor : CalmColor;

            return X == playerX && Y == playerY;
        }

        // Vision en linea recta (mismo criterio que un FOE de Etrian Odyssey): solo detecta si el
        // jugador comparte fila o columna Y no hay ninguna pared cerrada entre medio, hasta
        // VisionRange casillas. Reusa el mismo _canMove que ya usa el jugador para moverse, asi
        // que respeta paredes/Void real del piso sin duplicar esa logica.
        private bool CanSeePlayer(int playerX, int playerY)
        {
            if (X == playerX && Y == playerY) return true;

            if (Y == playerY)
            {
                int dist = Mathf.Abs(playerX - X);
                if (dist > VisionRange) return false;
                return IsClearLine(playerX > X ? Direction.East : Direction.West, dist);
            }
            if (X == playerX)
            {
                int dist = Mathf.Abs(playerY - Y);
                if (dist > VisionRange) return false;
                return IsClearLine(playerY > Y ? Direction.North : Direction.South, dist);
            }
            return false;
        }

        private bool IsClearLine(Direction dir, int dist)
        {
            var (ox, oy) = dir.Offset();
            int cx = X, cy = Y;
            for (int i = 0; i < dist; i++)
            {
                if (!_canMove(cx, cy, dir)) return false;
                cx += ox;
                cy += oy;
            }
            return true;
        }

        private void StepAlongRoute()
        {
            var route = _floor.FoePatrolRoute;
            int next = _routeIndex + _routeDir;
            if (next < 0 || next >= route.Count)
            {
                _routeDir = -_routeDir;
                next = _routeIndex + _routeDir;
            }
            _routeIndex = Mathf.Clamp(next, 0, route.Count - 1);
            (X, Y) = route[_routeIndex];
        }

        // Persecucion real via BFS (equivalente a Dijkstra en una grilla sin costos por arista,
        // mucho mas barato de calcular): el piso es chico (un par de cientos de celdas como
        // mucho), asi que recalcular el camino entero cada paso es un costo trivial y evita el
        // problema del greedy anterior (elegir el vecino que mas acerca en linea recta podia
        // trabarlo contra una pared si esa direccion resultaba ser la bloqueada). Si no hay camino
        // (no deberia pasar en un piso conectado), se queda quieto.
        private void StepToward(int playerX, int playerY)
        {
            var next = BfsNextStep(playerX, playerY);
            if (next.HasValue) { X = next.Value.x; Y = next.Value.y; }
        }

        private (int x, int y)? BfsNextStep(int targetX, int targetY)
        {
            var start = (X, Y);
            var target = (targetX, targetY);
            if (start == target) return null;

            var visited = new HashSet<(int, int)> { start };
            var cameFrom = new Dictionary<(int, int), (int, int)>();
            var queue = new Queue<(int, int)>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (cur == target)
                {
                    var step = cur;
                    while (cameFrom[step] != start) step = cameFrom[step];
                    return step;
                }
                foreach (var dir in DirectionExtensions.All)
                {
                    if (!_canMove(cur.Item1, cur.Item2, dir)) continue;
                    var (ox, oy) = dir.Offset();
                    var next = (cur.Item1 + ox, cur.Item2 + oy);
                    if (visited.Contains(next)) continue;
                    visited.Add(next);
                    cameFrom[next] = cur;
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        // Huida exitosa (ver CombatManager.StartFoeEncounter): retrocede a la celda de SU ruta mas
        // cercana a donde estaba, un paso atras en el sentido en que venia, Y ademas queda
        // aturdido FleeStunSteps pasos del jugador (no detecta ni se mueve) -- entre el retroceso
        // y el aturdimiento, el jugador le saca varias casillas reales de ventaja.
        public void PushBack()
        {
            var route = _floor.FoePatrolRoute;
            int nearestIdx = 0, nearestDist = int.MaxValue;
            for (int i = 0; i < route.Count; i++)
            {
                int d = Chebyshev(route[i].x, route[i].y, X, Y);
                if (d < nearestDist) { nearestDist = d; nearestIdx = i; }
            }
            _routeIndex = Mathf.Clamp(nearestIdx - _routeDir, 0, route.Count - 1);
            (X, Y) = route[_routeIndex];
            IsChasing = false;
            _stunnedSteps = FleeStunSteps;
            transform.position = _cellToWorld(X, Y) + Vector3.up * FloatHeight;
            _renderer.material.color = CalmColor;
        }

        // Dano de una trampa (picos o flecha, ver DungeonManager.HandleFoeSteppedOnTrap /
        // OnTrapArrowHitFoe) sobre el propio MaxHP del FOE. A diferencia del jugador, no hay piso
        // minimo de 1: el FOE SI puede morir aca. Devuelve true si murio -- quien llama es
        // responsable de destruir este GameObject y limpiar la ruta del piso.
        public bool ApplyTrapDamage(float damageFraction)
        {
            int dmg = Mathf.Max(1, Mathf.RoundToInt(_maxHp * damageFraction));
            _hp = Mathf.Max(0, _hp - dmg);
            return _hp <= 0;
        }

        // Se llama justo antes de destruir este GameObject al cambiar de piso (ver
        // DungeonManager.RefreshActiveFoe): vuelca el estado en vivo en el DungeonFloor para
        // restaurarlo exacto si el jugador vuelve mas tarde. El FOE nunca "resetea" solo porque lo
        // perdiste de vista un rato -- unicamente Initialize (con floor.FoeSavedX == -1, primera
        // vez) lo arranca desde el inicio de su ruta con HP lleno.
        public void SaveStateTo(DungeonFloor floor)
        {
            floor.FoeSavedX = X;
            floor.FoeSavedY = Y;
            floor.FoeSavedHp = _hp;
            floor.FoeSavedIsChasing = IsChasing;
            floor.FoeSavedRouteIndex = _routeIndex;
            floor.FoeSavedRouteDir = _routeDir;
            floor.FoeSavedStunnedSteps = _stunnedSteps;
        }

        private static int Chebyshev(int x1, int y1, int x2, int y2) => Mathf.Max(Mathf.Abs(x1 - x2), Mathf.Abs(y1 - y2));
    }
}
