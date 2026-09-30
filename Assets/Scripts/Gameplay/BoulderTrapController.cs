using System;
using System.Collections;
using UnityEngine;
using DungeonGen;

namespace Gameplay
{
    // Trampa de roca que persigue (ver DungeonGenerator.AddBoulderTrap): a diferencia de
    // TrapDisparadorController (que dispara UNA flecha por una linea fija), esto recorre
    // DungeonFloor.BoulderTrapPath entero cada vez que se dispara, arrancando del mismo lugar
    // donde el jugador piso para activarla -- hay que correr por el resto del corredor mas rapido
    // que la roca, o te alcanza en la celda a la que llega. Se puede volver a disparar cada vez
    // que se re-entra al corredor (no es de un solo uso, a diferencia de la sala que colapsa).
    public class BoulderTrapController : MonoBehaviour
    {
        private const float SecondsPerCell = 0.22f;

        private DungeonFloor _floor;
        private Func<int, int, Vector3> _cellToWorld;
        private Func<(int x, int y)> _playerCell;
        private Action _onBoulderHitPlayer;
        private float _cellSize;
        private bool _rolling;

        public void Initialize(DungeonFloor floor, Func<int, int, Vector3> cellToWorld,
            Func<(int x, int y)> playerCell, Action onBoulderHitPlayer, float cellSize)
        {
            _floor = floor;
            _cellToWorld = cellToWorld;
            _playerCell = playerCell;
            _onBoulderHitPlayer = onBoulderHitPlayer;
            _cellSize = cellSize;
        }

        // Se llama al pisar BoulderTrapStartPos (ver DungeonManager.OnPlayerEnterCell). Ignorado
        // si ya hay una roca rodando (una a la vez) o si este piso no tiene esta trampa.
        public void Trigger()
        {
            if (_rolling || _floor == null || !_floor.HasBoulderTrap) return;
            StartCoroutine(RollRoutine());
        }

        private IEnumerator RollRoutine()
        {
            _rolling = true;
            var path = _floor.BoulderTrapPath;

            var boulder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            boulder.name = "BoulderTrap";
            var col = boulder.GetComponent<Collider>();
            if (col != null) Destroy(col);
            boulder.transform.localScale = Vector3.one * (_cellSize * 0.8f);
            boulder.GetComponent<Renderer>().material.color = new Color(0.35f, 0.32f, 0.3f);
            boulder.transform.position = _cellToWorld(path[0].x, path[0].y) + Vector3.up * (_cellSize * 0.4f);

            bool hitPlayer = false;
            for (int i = 1; i < path.Count; i++)
            {
                var (cx, cy) = path[i];
                Vector3 start = boulder.transform.position;
                Vector3 target = _cellToWorld(cx, cy) + Vector3.up * (_cellSize * 0.4f);

                float t = 0f;
                while (t < SecondsPerCell)
                {
                    t += Time.deltaTime;
                    // Rueda de verdad mientras avanza (no solo se desliza): un giro visible por
                    // celda recorrida, alrededor del eje perpendicular a la direccion de avance.
                    boulder.transform.position = Vector3.Lerp(start, target, Mathf.Clamp01(t / SecondsPerCell));
                    Vector3 axis = Vector3.Cross(Vector3.up, (target - start).normalized);
                    boulder.transform.Rotate(axis, 360f * (Time.deltaTime / SecondsPerCell), Space.World);
                    yield return null;
                }
                boulder.transform.position = target;

                var (px, py) = _playerCell();
                if (!hitPlayer && px == cx && py == cy)
                {
                    hitPlayer = true;
                    _onBoulderHitPlayer?.Invoke();
                }
            }

            Destroy(boulder);
            _rolling = false;
        }
    }
}
