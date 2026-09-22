using System.Linq;
using UnityEngine;
using DungeonGen;

namespace Gameplay
{
    // Enemigo fuerte que patrulla un TRAMO fijo del piso (DungeonFloor.FoePatrolRoute, ver
    // DungeonGenerator.PlaceFoeRoute) yendo y viniendo -- evitable, no persigue por defecto. Cada
    // vez que el jugador da un paso (ver DungeonManager.AdvanceFoe), el FOE da UN paso tambien: si
    // eso lo deja en la misma celda que el jugador, quien llama arranca un combate 1 contra 1 mas
    // dificil de huir (CombatManager.StartFoeEncounter). Blanco y tranquilo mientras patrulla; si
    // el jugador se acerca (DetectRadius), se pinta rojo y lo persigue DE VERDAD (deja la ruta fija
    // y camina hacia el jugador, no solo cambia de color); si el jugador se aleja lo suficiente de
    // la ruta (LoseRadius), deja de perseguir y retoma la patrulla desde donde quedo.
    public class FoeController : MonoBehaviour
    {
        private const int DetectRadius = 3;
        private const int LoseRadius = 6;

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
            _routeIndex = 0;
            _maxHp = maxHp;
            _hp = maxHp;
            (X, Y) = floor.FoePatrolRoute[0];

            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "FoeVisual";
            visual.transform.SetParent(transform, false);
            var col = visual.GetComponent<Collider>();
            if (col != null) Destroy(col);
            visual.transform.localScale = Vector3.one * (cellSize * 0.55f);
            _renderer = visual.GetComponent<Renderer>();
            _renderer.material.color = CalmColor;

            transform.position = cellToWorld(X, Y) + Vector3.up * FloatHeight;
        }

        // Se llama 1 vez por cada paso que da el jugador. Devuelve true si el FOE quedo en la
        // MISMA celda que el jugador (colision) -- quien llama es responsable de arrancar el combate.
        public bool AdvanceStep(int playerX, int playerY)
        {
            if (!IsChasing && Chebyshev(X, Y, playerX, playerY) <= DetectRadius)
                IsChasing = true;
            else if (IsChasing && _floor.FoePatrolRoute.Min(c => Chebyshev(c.x, c.y, X, Y)) > LoseRadius)
                IsChasing = false;

            if (IsChasing) StepToward(playerX, playerY);
            else StepAlongRoute();

            transform.position = _cellToWorld(X, Y) + Vector3.up * FloatHeight;
            _renderer.material.color = IsChasing ? ChaseColor : CalmColor;

            return X == playerX && Y == playerY;
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

        // Persecucion real: de los vecinos ABIERTOS (misma regla de paredes/void que el jugador,
        // via el DungeonManager.CanMove que le paso Initialize), elige el que mas reduce la
        // distancia al jugador. Si ninguno mejora (acorralado contra una pared), se queda quieto.
        private void StepToward(int playerX, int playerY)
        {
            int bestX = X, bestY = Y;
            int bestDist = Chebyshev(X, Y, playerX, playerY);
            foreach (var dir in DirectionExtensions.All)
            {
                if (!_canMove(X, Y, dir)) continue;
                var (ox, oy) = dir.Offset();
                int nx = X + ox, ny = Y + oy;
                int dist = Chebyshev(nx, ny, playerX, playerY);
                if (dist < bestDist) { bestDist = dist; bestX = nx; bestY = ny; }
            }
            X = bestX;
            Y = bestY;
        }

        // Huida exitosa (ver CombatManager.StartFoeEncounter): retrocede a la celda de SU ruta mas
        // cercana a donde estaba, un paso atras en el sentido en que venia -- asi no vuelve a
        // colisionar apenas el jugador de un paso mas, pero tampoco desaparece del piso.
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

        private static int Chebyshev(int x1, int y1, int x2, int y2) => Mathf.Max(Mathf.Abs(x1 - x2), Mathf.Abs(y1 - y2));
    }
}
