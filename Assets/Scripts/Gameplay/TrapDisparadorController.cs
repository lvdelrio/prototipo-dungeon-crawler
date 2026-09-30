using System;
using System.Collections;
using UnityEngine;
using DungeonGen;

namespace Gameplay
{
    // Maquina real de una sala de trampas ArrowSweep (ver DungeonGenerator.AddTrapRoom): vive
    // montada en la pared del extremo de la linea (DungeonFloor.TrapDisparadorPos/Dir) y, al
    // pisar CUALQUIER celda de la linea (ver DungeonManager.OnPlayerEnterCell), dispara una
    // flecha real que recorre DungeonFloor.TrapArrowPath celda por celda con tiempo real entre
    // cada una -- el jugador tiene esa ventana para salir de la linea y esquivarla, ya no es una
    // probabilidad fija. Si el jugador la destruye con el Perforador queda desactivada para
    // siempre (ver DungeonGenerator.TryDestroyTrapDisparador / DungeonManager.TryUseDrill).
    public class TrapDisparadorController : MonoBehaviour
    {
        private const float SecondsPerCell = 0.16f;
        private const float ArrowFloatHeight = 0.55f;

        private DungeonFloor _floor;
        private Func<int, int, Vector3> _cellToWorld;
        private Func<(int x, int y)> _playerCell;
        private Action _onArrowHitPlayer;
        private Func<(int x, int y)?> _foeCell;
        private Action _onArrowHitFoe;
        private float _cellSize;
        private bool _firing;

        public void Initialize(DungeonFloor floor, Func<int, int, Vector3> cellToWorld,
            Func<(int x, int y)> playerCell, Action onArrowHitPlayer,
            Func<(int x, int y)?> foeCell, Action onArrowHitFoe, float cellSize)
        {
            _floor = floor;
            _cellToWorld = cellToWorld;
            _playerCell = playerCell;
            _onArrowHitPlayer = onArrowHitPlayer;
            _foeCell = foeCell;
            _onArrowHitFoe = onArrowHitFoe;
            _cellSize = cellSize;

            var (dx, dy) = floor.TrapDisparadorPos;
            var (ox, oy) = floor.TrapDisparadorDir.Opposite().Offset();
            transform.position = cellToWorld(dx, dy) + new Vector3(ox, 0f, oy) * (cellSize * 0.42f) + Vector3.up * ArrowFloatHeight;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "TrapDisparadorVisual";
            visual.transform.SetParent(transform, false);
            var col = visual.GetComponent<Collider>();
            if (col != null) Destroy(col);
            visual.transform.localScale = new Vector3(cellSize * 0.3f, cellSize * 0.3f, cellSize * 0.3f);
            visual.GetComponent<Renderer>().material.color = new Color(0.32f, 0.28f, 0.22f);
        }

        // Se llama al pisar cualquier celda de la linea. Ignorado si ya hay una flecha en vuelo
        // (un disparador, una flecha a la vez) o si esta trampa fue destruida.
        public void Fire()
        {
            if (_firing || _floor.TrapDisabled) return;
            StartCoroutine(FireRoutine());
        }

        private IEnumerator FireRoutine()
        {
            _firing = true;

            var arrow = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            arrow.name = "TrapArrow";
            var col = arrow.GetComponent<Collider>();
            if (col != null) Destroy(col);
            arrow.transform.localScale = new Vector3(_cellSize * 0.12f, _cellSize * 0.42f, _cellSize * 0.12f);
            arrow.GetComponent<Renderer>().material.color = new Color(0.7f, 0.62f, 0.2f);

            var (dirX, dirY) = _floor.TrapDisparadorDir.Offset();
            arrow.transform.rotation = Quaternion.LookRotation(new Vector3(dirX, 0f, dirY), Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            arrow.transform.position = transform.position;

            bool hitPlayer = false;
            bool hitFoe = false;
            var path = _floor.TrapArrowPath;
            for (int i = 0; i < path.Count && !_floor.TrapDisabled; i++)
            {
                var (cx, cy) = path[i];
                Vector3 start = arrow.transform.position;
                Vector3 target = _cellToWorld(cx, cy) + Vector3.up * ArrowFloatHeight;

                float t = 0f;
                while (t < SecondsPerCell)
                {
                    t += Time.deltaTime;
                    arrow.transform.position = Vector3.Lerp(start, target, Mathf.Clamp01(t / SecondsPerCell));
                    yield return null;
                }
                arrow.transform.position = target;

                var (px, py) = _playerCell();
                if (px == cx && py == cy) hitPlayer = true;

                var foePos = _foeCell?.Invoke();
                if (foePos.HasValue && foePos.Value.x == cx && foePos.Value.y == cy) hitFoe = true;

                if (hitPlayer || hitFoe) break;
            }

            Destroy(arrow);
            if (hitPlayer) _onArrowHitPlayer?.Invoke();
            if (hitFoe) _onArrowHitFoe?.Invoke();
            _firing = false;
        }
    }
}
