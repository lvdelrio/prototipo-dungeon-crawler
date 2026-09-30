using System.Collections;
using UnityEngine;
using DungeonGen;

namespace Gameplay
{
    public class GridPlayerController : MonoBehaviour
    {
        public DungeonManager dungeonManager;
        public DialogueManager dialogueManager;
        public PauseMenuManager pauseMenu;
        public PlayerMapViewer mapViewer;
        public ControlsTutorialHUD controlsTutorial;
        public float moveDuration = 0.18f;
        public float turnDuration = 0.12f;

        [Header("Feedback de caminata (bob de camara, muy sutil)")]
        [Tooltip("Transform de la camara (hijo del jugador). Si se deja vacio, se usa Camera.main.")]
        public Transform cameraBobTarget;
        public float bobHeight = 0.05f;
        public int bobCyclesPerStep = 1;

        [Header("Antorcha del jugador (R para sacarla/guardarla)")]
        [Tooltip("Radio de iluminacion en CELDAS (se multiplica por settings.cellSize al armar la luz).")]
        public float torchRadiusCells = 5f;

        private int _x, _y;
        private Direction _facing = Direction.North;
        private bool _busy;
        private float _cameraBaseY;
        private bool _cameraBaseCaptured;
        private Light _torchLight;
        private bool _torchLit;

        public int CellX => _x;
        public int CellY => _y;
        public Direction Facing => _facing;

        public void Warp(int x, int y, Direction facing)
        {
            _x = x;
            _y = y;
            _facing = facing;
            transform.position = dungeonManager.CellToWorld(_x, _y);
            transform.rotation = FacingRotation(facing);
        }

        private void CaptureCameraBaseIfNeeded()
        {
            if (_cameraBaseCaptured) return;
            if (cameraBobTarget == null && Camera.main != null) cameraBobTarget = Camera.main.transform;
            if (cameraBobTarget != null)
            {
                _cameraBaseY = cameraBobTarget.localPosition.y;
                _cameraBaseCaptured = true;
            }
        }

        // Antorcha del jugador (pedido puntual): una Light de verdad (no solo decoracion) colgada
        // de la camara, radio en celdas (torchRadiusCells * settings.cellSize) -- se crea recien al
        // primer R (nunca hace falta si el jugador no la usa nunca) y despues solo se prende/apaga.
        private void EnsureTorchLight()
        {
            if (_torchLight != null) return;
            CaptureCameraBaseIfNeeded();
            var parent = cameraBobTarget != null ? cameraBobTarget : transform;

            var go = new GameObject("PlayerTorchLight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            _torchLight = go.AddComponent<Light>();
            _torchLight.type = LightType.Point;
            _torchLight.color = new Color(1f, 0.75f, 0.45f);
            _torchLight.intensity = 1.8f;
            float cellSize = dungeonManager != null && dungeonManager.settings != null ? dungeonManager.settings.cellSize : 4f;
            _torchLight.range = torchRadiusCells * cellSize;
            _torchLight.shadows = LightShadows.None;
            _torchLight.renderMode = LightRenderMode.ForcePixel;
            _torchLight.enabled = false;
        }

        private void TogglePlayerTorch()
        {
            EnsureTorchLight();
            _torchLit = !_torchLit;
            _torchLight.enabled = _torchLit;
            if (dungeonManager != null && dungeonManager.hud != null)
                dungeonManager.hud.SetLastMessage(_torchLit ? "Antorcha encendida." : "Antorcha guardada.");
        }

        void Update()
        {
            if (_busy || dungeonManager == null || !dungeonManager.IsReady) return; // IsReady en false = todavia esta el menu inicial (Continuar/Nueva Partida)
            if (dungeonManager.IsPlayerFalling) return; // bloquea input durante la transicion de caida al piso inferior
            if (dungeonManager.IsCombatActive || dungeonManager.IsGameOverShopActive) return; // congelado en combate o en la tienda post-derrota
            if (controlsTutorial != null && controlsTutorial.IsOpen) return; // congelado hasta cerrar el cartel de controles (solo la primera vez)
            if (dialogueManager != null && dialogueManager.IsActive)
            {
                // Espacio avanza el dialogo (solo si NO tiene opciones -- esas se eligen con su
                // propio boton, nunca con Espacio, para no elegir "la primera" sin querer). Se
                // resuelve ACA (no en un Update() propio de DialogueHUD) y se corta con return
                // para consumir el input en este mismo frame: si no, el mismo Espacio que cierra
                // el dialogo podia colarse tambien como el "interactuar" de mas abajo en este
                // mismo Update().
                if (dialogueManager.Choices == null && Input.GetKeyDown(KeyCode.Space))
                    dialogueManager.Advance();
                return; // congelado mientras el dialogo siga en pantalla
            }

            // El menu de pausa (Codex/Equipamiento/Formacion/Guardar) solo se puede abrir "en modo
            // caminar" -- no en combate, dialogo o la tienda post-run (ya cubierto por los checks
            // de arriba) -- y mientras esta abierto, el jugador tambien queda congelado.
            if (pauseMenu != null && Input.GetKeyDown(KeyCode.I) && !pauseMenu.IsOpen) { pauseMenu.Open(); return; }
            if (pauseMenu != null && pauseMenu.IsOpen)
            {
                if (Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.Escape)) pauseMenu.Close();
                return;
            }

            // M levanta el mapa fisico cerca de camara (ver PlayerMapViewer) -- el item consumible
            // "Mapa" (revela el piso de golpe) se movio a L para liberar M, que antes lo usaba.
            if (mapViewer != null && Input.GetKeyDown(KeyCode.M)) { mapViewer.Toggle(); return; }
            if (Input.GetKeyDown(KeyCode.L)) { dungeonManager.TryUseMap(); return; }
            if (Input.GetKeyDown(KeyCode.P)) { dungeonManager.TryUseDrill(_x, _y, _facing); return; }
            if (Input.GetKeyDown(KeyCode.N)) { dungeonManager.TryUseIncense(); return; }

            // Antorcha del jugador (pedido puntual, pensada para el Bioma de Cuevas -- ver el
            // cartel de su entrada, DungeonLevelBuilder.BuildCaveEntranceSignposts): R la saca/guarda,
            // sin carga ni limite (a diferencia del Mapa/Perforador/Incienso de arriba, que SI
            // consumen del inventario meta). Funciona en cualquier piso, no solo en la cueva -- no
            // hace falta restringirla, en el resto de los biomas ya hay luz de sobra.
            if (Input.GetKeyDown(KeyCode.R)) { TogglePlayerTorch(); return; }

            // DEMO del sistema de dialogo (tecla T): reemplazar este trigger por uno real (un NPC,
            // una celda de taberna, etc.) cuando se construya el bazar/taberna de verdad.
            if (dialogueManager != null && Input.GetKeyDown(KeyCode.T))
            {
                dialogueManager.ShowChoices("Tabernero", "Bienvenido a la taberna. Tengo un trabajo si te interesa: despejar la sala de al lado.",
                    new System.Collections.Generic.List<DialogueChoice>
                    {
                        new DialogueChoice("Aceptar mision", () => dungeonManager.hud?.SetLastMessage("Mision aceptada (demo).")),
                        new DialogueChoice("Rechazar", () => dungeonManager.hud?.SetLastMessage("Rechazaste la mision (demo).")),
                    });
                return;
            }

            // Control tipo "tanque" en WASD: W/S caminan, A/D giran -- las flechitas quedaron
            // LIBRES por completo para la herramienta Pared del mapa fisico (ver
            // PlayerMapEditorHUD), sin ningun conflicto que resolver: como el giro ya no depende
            // de las flechitas, podes girar la camara con A/D para mirar donde estan las paredes
            // reales MIENTRAS estas a mitad de dibujar una (a pedido).
            if (Input.GetKeyDown(KeyCode.A)) { StartCoroutine(Turn(-1)); return; }
            if (Input.GetKeyDown(KeyCode.D)) { StartCoroutine(Turn(1)); return; }

            Direction? moveDir = null;
            if (Input.GetKeyDown(KeyCode.W)) moveDir = _facing;
            else if (Input.GetKeyDown(KeyCode.S)) moveDir = _facing.Opposite();

            if (moveDir.HasValue)
            {
                StartCoroutine(Move(moveDir.Value));
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space)) dungeonManager.TryInteract(_x, _y);
        }

        private Direction TurnDirection(Direction facing, int steps)
        {
            int idx = ((int)facing + steps) % 4;
            if (idx < 0) idx += 4;
            return (Direction)idx;
        }

        private IEnumerator Move(Direction dir)
        {
            if (!dungeonManager.CanMove(_x, _y, dir)) yield break;

            CaptureCameraBaseIfNeeded();

            _busy = true;
            var (ox, oy) = dir.Offset();
            int nx = _x + ox, ny = _y + oy;

            Vector3 from = transform.position;
            Vector3 to = dungeonManager.CellToWorld(nx, ny);
            float t = 0f;
            while (t < moveDuration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / moveDuration);
                transform.position = Vector3.Lerp(from, to, p);
                ApplyBob(p);
                yield return null;
            }
            transform.position = to;
            ApplyBob(0f);
            _x = nx;
            _y = ny;

            dungeonManager.OnPlayerEnterCell(_x, _y);
            _busy = false;
        }

        // Un pequeno vaiven vertical de la camara durante el paso: sube y baja una vez (o varias,
        // segun bobCyclesPerStep) para dar sensacion de peso al caminar, sin exagerar.
        private void ApplyBob(float progress)
        {
            if (cameraBobTarget == null) return;
            float bob = Mathf.Sin(progress * Mathf.PI * bobCyclesPerStep) * bobHeight;
            var lp = cameraBobTarget.localPosition;
            lp.y = _cameraBaseY + Mathf.Abs(bob);
            cameraBobTarget.localPosition = lp;
        }

        private IEnumerator Turn(int steps)
        {
            _busy = true;
            _facing = TurnDirection(_facing, steps);
            Quaternion from = transform.rotation;
            Quaternion to = FacingRotation(_facing);
            float t = 0f;
            while (t < turnDuration)
            {
                t += Time.deltaTime;
                transform.rotation = Quaternion.Slerp(from, to, t / turnDuration);
                yield return null;
            }
            transform.rotation = to;
            _busy = false;
        }

        private Quaternion FacingRotation(Direction facing)
        {
            float yaw = facing switch
            {
                Direction.North => 0f,
                Direction.East => 90f,
                Direction.South => 180f,
                Direction.West => 270f,
                _ => 0f
            };
            return Quaternion.Euler(0, yaw, 0);
        }
    }
}
