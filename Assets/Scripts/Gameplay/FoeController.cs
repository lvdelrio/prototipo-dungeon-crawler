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

        // Pedido puntual: en persecucion de verdad (StepToward, no la patrulla), cada
        // ChaseRestInterval pasos el FOE se queda quieto UNO, dandole al jugador una ventana real
        // para reaccionar (curarse, doblar una esquina, etc.) en vez de una persecucion sin
        // respiro. Se reinicia cada vez que arranca una persecucion nueva (ver AdvanceStep).
        private const int ChaseRestInterval = 3;

        // Radio de exclusion alrededor de CUALQUIER escalera del piso (ver DungeonGenerator.
        // FoeStairsExclusionRadius -- misma constante, un solo punto de verdad): StepToward/BfsNextStep
        // jamas planea un camino que entre ahi, ni siquiera si el jugador esta parado adentro (en ese
        // caso el BFS no encuentra camino y el FOE se queda quieto, ver BfsNextStep). Las escaleras
        // son zona seguras de verdad, no solo "dificiles de alcanzar".

        // ---------- Comportamiento de dia/noche en la rutina BASICA (patrulla, no persecucion) ----------
        // Pedido puntual: "al menos 3 comportamientos, tanto de dia como de noche". Los 3 viven en
        // StepAlongRoute/CanSeePlayer, atados a Gameplay.DayNightCycle.NightAmount (0 = dia pleno,
        // 1 = noche plena -- ver ese archivo). Se usa un umbral (>=0.5) en vez del valor continuo
        // para que el cambio de comportamiento sea binario y facil de probar/observar, aunque el
        // color (mas abajo) si interpola parejo con el valor real.
        //  1) Vision: de noche ve mas lejos (NightVisionBonus) -- mas peligroso, mas dificil de evadir.
        //  2) Tempo: de noche, cada paso de patrulla tiene chance de sumar un paso extra de una
        //     (mas inquieto/agresivo); de dia siempre es un paso por llamada, como antes.
        //  3) Descanso: de dia, al llegar a una punta de su ruta y dar la vuelta, se queda quieto
        //     unos pasos antes de retomar (tranquilo a la luz); de noche da la vuelta de inmediato.
        private const float NightThreshold = 0.5f;
        private const int NightVisionBonus = 3;
        private const float NightExtraStepChance = 0.4f;
        private const int DayRestStepsAtTurn = 2;

        private static readonly Color CalmColorDay = Color.white;
        private static readonly Color CalmColorNight = new Color(0.55f, 0.35f, 0.78f);
        private static readonly Color ChaseColor = new Color(0.85f, 0.12f, 0.1f);

        // Altura fija (por encima del piso de la celda) a la que flota la esfera -- mas o menos a
        // la altura de la vista del jugador, para que se vea de frente al cruzarse en un pasillo.
        private const float FloatHeight = 0.9f;

        private DungeonFloor _floor;
        private System.Func<int, int, Direction, bool> _canMove;
        private System.Func<int, int, Vector3> _cellToWorld;
        private DayNightCycle _dayNight;
        private int _routeIndex;
        private int _routeDir = 1;
        private int _stunnedSteps;
        private int _restSteps;
        private int _chaseStepCounter;
        private List<(int x, int y)> _stairs;
        private Renderer _renderer;
        private int _hp;
        private int _maxHp;

        public int X { get; private set; }
        public int Y { get; private set; }
        public bool IsChasing { get; private set; }

        // >= NightThreshold en vez del valor continuo: los 3 comportamientos de abajo cambian de
        // golpe entre "modo dia" y "modo noche", no se van atenuando a mitad de camino.
        private bool IsNight => _dayNight != null && _dayNight.NightAmount >= NightThreshold;

        public void Initialize(DungeonFloor floor, System.Func<int, int, Direction, bool> canMove,
            System.Func<int, int, Vector3> cellToWorld, float cellSize, int maxHp, DayNightCycle dayNight = null)
        {
            _floor = floor;
            _canMove = canMove;
            _cellToWorld = cellToWorld;
            _maxHp = maxHp;
            _dayNight = dayNight;

            _stairs = new List<(int x, int y)>();
            for (int x = 0; x < floor.Width; x++)
                for (int y = 0; y < floor.Height; y++)
                    if (floor.Cells[x, y].Type == CellType.StairsUp || floor.Cells[x, y].Type == CellType.StairsDown)
                        _stairs.Add((x, y));

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
                // Pedido puntual: arranca del lado OPUESTO a la escalera por la que el jugador sube
                // a este piso -- el ULTIMO indice de la ruta es el extremo mas cerca de floor.EndPos
                // (ver DungeonGenerator.PlaceFoeRoute), nunca del lado de floor.StartPos/la escalera
                // real de entrada. _routeDir=-1 porque desde ahi lo natural es empezar caminando
                // hacia atras (indices decrecientes), de vuelta hacia el otro extremo.
                _routeIndex = floor.FoePatrolRoute.Count - 1;
                _routeDir = -1;
                _hp = maxHp;
                (X, Y) = floor.FoePatrolRoute[_routeIndex];
            }

            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "FoeVisual";
            visual.transform.SetParent(transform, false);
            var col = visual.GetComponent<Collider>();
            if (col != null) Destroy(col);
            visual.transform.localScale = Vector3.one * (cellSize * 0.55f);
            _renderer = visual.GetComponent<Renderer>();
            _renderer.material.color = IsChasing ? ChaseColor : CurrentCalmColor;

            transform.position = cellToWorld(X, Y) + Vector3.up * FloatHeight;
        }

        // Cuarto detalle (cosmetico, no cuenta como uno de los 3 comportamientos de arriba): tinte
        // mas ominoso de noche mientras patrulla tranquilo, interpolado parejo con el NightAmount
        // real (no con el umbral binario de IsNight) para que la transicion se vea suave.
        private Color CurrentCalmColor => Color.Lerp(CalmColorDay, CalmColorNight, _dayNight != null ? _dayNight.NightAmount : 0f);

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
                _chaseStepCounter = 0; // persecucion nueva: arranca la cuenta de pasos de cero
            }
            else if (IsChasing && !canSee)
            {
                int distFromRoute = _floor.FoePatrolRoute.Min(c => Chebyshev(c.x, c.y, playerX, playerY));
                if (distFromRoute > LoseRouteDistance) IsChasing = false;
            }

            if (IsChasing)
            {
                // Pedido puntual: cada ChaseRestInterval pasos de persecucion de verdad, se queda
                // quieto UNO -- le da al jugador una ventana real para reaccionar en vez de una
                // persecucion sin respiro. La colision (return de mas abajo) se sigue chequeando
                // igual aunque no se haya movido: si el jugador camina hacia el FOE quieto, choca.
                _chaseStepCounter++;
                if (_chaseStepCounter >= ChaseRestInterval) _chaseStepCounter = 0;
                else StepToward(playerX, playerY);
            }
            else
            {
                StepAlongRoute();
            }

            transform.position = _cellToWorld(X, Y) + Vector3.up * FloatHeight;
            _renderer.material.color = IsChasing ? ChaseColor : CurrentCalmColor;

            return X == playerX && Y == playerY;
        }

        // Vision en linea recta (mismo criterio que un FOE de Etrian Odyssey): solo detecta si el
        // jugador comparte fila o columna Y no hay ninguna pared cerrada entre medio, hasta
        // EffectiveVisionRange casillas. Reusa el mismo _canMove que ya usa el jugador para
        // moverse, asi que respeta paredes/Void real del piso sin duplicar esa logica.
        private bool CanSeePlayer(int playerX, int playerY)
        {
            if (X == playerX && Y == playerY) return true;

            int visionRange = EffectiveVisionRange;
            if (Y == playerY)
            {
                int dist = Mathf.Abs(playerX - X);
                if (dist > visionRange) return false;
                return IsClearLine(playerX > X ? Direction.East : Direction.West, dist);
            }
            if (X == playerX)
            {
                int dist = Mathf.Abs(playerY - Y);
                if (dist > visionRange) return false;
                return IsClearLine(playerY > Y ? Direction.North : Direction.South, dist);
            }
            return false;
        }

        // Comportamiento 1 (noche): ve mas lejos en la oscuridad -- mas peligroso, mas dificil de
        // evadir por un pasillo largo. De dia se queda en el VisionRange base de siempre.
        private int EffectiveVisionRange => IsNight ? VisionRange + NightVisionBonus : VisionRange;

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
            // Comportamiento 3 (dia): descansando en una punta de la ruta -- no se mueve este
            // paso, solo cuenta la cuenta regresiva. De noche esto nunca llega a activarse (ver
            // mas abajo), asi que siempre sigue de largo.
            if (_restSteps > 0) { _restSteps--; return; }

            var route = _floor.FoePatrolRoute;
            int next = _routeIndex + _routeDir;
            bool reachedEnd = next < 0 || next >= route.Count;
            if (reachedEnd)
            {
                _routeDir = -_routeDir;
                next = _routeIndex + _routeDir;
                if (!IsNight) _restSteps = DayRestStepsAtTurn;
            }
            _routeIndex = Mathf.Clamp(next, 0, route.Count - 1);
            (X, Y) = route[_routeIndex];

            // Comportamiento 2 (noche): inquieto/agresivo -- chance de sumar un paso extra de una
            // (sin esperar el proximo paso del jugador). Nunca en el mismo llamado en que recien
            // dio la vuelta, para no pisarse con el comportamiento 3 de arriba.
            if (IsNight && !reachedEnd && Random.value < NightExtraStepChance)
            {
                _routeIndex = Mathf.Clamp(_routeIndex + _routeDir, 0, route.Count - 1);
                (X, Y) = route[_routeIndex];
            }
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
                    // Pedido puntual: las escaleras son zona segura de VERDAD -- el FOE ni siquiera
                    // planea un camino que entre ahi. Si el objetivo (el jugador) esta parado
                    // adentro, esto hace que el BFS nunca lo alcance y devuelva null mas abajo (se
                    // queda quieto ese paso), en vez de acercarse hasta el borde.
                    if (IsWithinStairsExclusionZone(next.Item1, next.Item2)) continue;
                    visited.Add(next);
                    cameFrom[next] = cur;
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        private bool IsWithinStairsExclusionZone(int x, int y)
        {
            foreach (var s in _stairs)
                if (Chebyshev(x, y, s.x, s.y) < DungeonGenerator.FoeStairsExclusionRadius) return true;
            return false;
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
            _renderer.material.color = CurrentCalmColor;
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
            // Si la patrulla lo dejo justo parado en una escalera (celda comun de su ruta como
            // cualquier otra), un paso atras en su propia ruta ANTES de guardar: sin esto, volver a
            // usar esa misma escalera mas tarde te aparecia justo encima de el.
            StepOffStairs(floor);

            floor.FoeSavedX = X;
            floor.FoeSavedY = Y;
            floor.FoeSavedHp = _hp;
            floor.FoeSavedIsChasing = IsChasing;
            floor.FoeSavedRouteIndex = _routeIndex;
            floor.FoeSavedRouteDir = _routeDir;
            floor.FoeSavedStunnedSteps = _stunnedSteps;
        }

        // Retrocede por la ruta (misma direccion en la que venia caminando, invertida) hasta que la
        // celda actual ya no sea una escalera, o hasta agotar la ruta -- lo normal es 1 sola
        // iteracion, el bucle es solo para el caso raro de 2 escaleras seguidas en la ruta.
        private void StepOffStairs(DungeonFloor floor)
        {
            var route = _floor.FoePatrolRoute;
            if (route.Count <= 1) return;

            for (int i = 0; i < route.Count; i++)
            {
                var type = floor.Cells[X, Y].Type;
                if (type != CellType.StairsUp && type != CellType.StairsDown) return;

                int back = _routeIndex - _routeDir;
                if (back < 0 || back >= route.Count) back = _routeIndex + _routeDir; // el limite de la ruta ES la escalera: probar para el otro lado
                if (back < 0 || back >= route.Count) return; // ruta de 1 sola celda y esa es la escalera: no hay adonde retroceder

                _routeIndex = back;
                (X, Y) = route[_routeIndex];
            }
        }

        private static int Chebyshev(int x1, int y1, int x2, int y2) => Mathf.Max(Mathf.Abs(x1 - x2), Mathf.Abs(y1 - y2));
    }
}
