using System.Collections.Generic;
using UnityEngine;
using DungeonGen;

namespace Gameplay
{
    public class MinimapUI : MonoBehaviour
    {
        public DungeonManager dungeonManager;
        public GridPlayerController player;
        public PauseMenuManager pauseMenu;

        [Header("Modo de mapa")]
        [Tooltip("true = solo se ve lo que el jugador ya piso (niebla de guerra). false = mapa completo (modo debug).")]
        public bool playerMode = true;
        public KeyCode toggleModeKey = KeyCode.Tab;

        public int panelY = 10;
        public int cellPixelSize = 16;
        public int wallPixelThickness = 2;
        public int rightMargin = 10;

        // Reutilizada entre refrescos (ver RefreshWildlifePositions) en vez de instanciada de nuevo.
        private readonly List<Vector3> _wildlifePositions = new List<Vector3>();
        // Reportado: "consumen caleta de recurso" -- FindObjectsOfType (dos por refresco, uno por
        // tipo de criatura) es una de las llamadas mas caras de la API de Unity, y OnGUI puede
        // dispararse varias veces por frame (Layout + Repaint, a veces mas). Llamarla ahi directo
        // significaba 2+ escaneos completos de la escena por frame, todo el tiempo que el panel
        // este visible. Ahora se refresca UNA vez cada wildlifeRefreshInterval segundos desde
        // Update (que corre una sola vez por frame), y OnGUI solo lee la lista ya armada.
        private float _nextWildlifeRefresh;
        public float wildlifeRefreshInterval = 0.25f;

        void Update()
        {
            if (Input.GetKeyDown(toggleModeKey)) playerMode = !playerMode;

            if (Time.unscaledTime >= _nextWildlifeRefresh)
            {
                _nextWildlifeRefresh = Time.unscaledTime + wildlifeRefreshInterval;
                RefreshWildlifePositions();
            }
        }

        private void RefreshWildlifePositions()
        {
            _wildlifePositions.Clear();
            var critterAmbients = FindObjectsOfType<ForestCritterAmbient>();
            for (int i = 0; i < critterAmbients.Length; i++) critterAmbients[i].CollectPositions(_wildlifePositions);
            var mouseAmbients = FindObjectsOfType<CastleMouseAmbient>();
            for (int i = 0; i < mouseAmbients.Length; i++) mouseAmbients[i].CollectPositions(_wildlifePositions);
        }

        void OnGUI()
        {
            // El minimapa digital era una ayuda de desarrollo -- la navegacion real del juego
            // terminado es el mapa fisico dibujado a mano (ver PlayerMapEditorHUD/PlayerMapViewer).
            // Application.isEditor es false en cualquier build (Development o Release), asi que
            // esto lo saca del juego compilado sin afectar las pruebas en el Editor.
            if (!Application.isEditor) return;
            if (dungeonManager == null || player == null || !dungeonManager.IsReady) return;
            // El mapa desaparece durante el combate, la pantalla de tienda/mejoras post-run y el
            // menu de pausa: en todos los casos hay otro panel mas importante que no debe quedar tapado.
            if (dungeonManager.IsCombatActive || dungeonManager.IsGameOverShopActive) return;
            if (pauseMenu != null && pauseMenu.IsOpen) return;
            // Mantener Shift oculta el minimapa (para sacar una captura limpia, o simplemente ver
            // la esquina sin el panel encima) -- se mantiene apretado, no es un toggle.
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return;
            var floor = dungeonManager.CurrentFloor;
            if (floor == null) return;

            int w = floor.Width;
            int h = floor.Height;
            // Anclado arriba a la derecha, ajustado al tamano real del mapa, en vez de una
            // posicion X fija que en pantallas angostas podia salirse o superponerse con otra UI.
            int panelX = Screen.width - (w * cellPixelSize + 20) - rightMargin;
            float top = panelY + 24;

            GUI.Box(new Rect(panelX - 10, panelY - 10, w * cellPixelSize + 20, h * cellPixelSize + 44), "");
            string floorLabel = dungeonManager.FloorLabel(dungeonManager.CurrentFloorIndex);
            string title = playerMode
                ? $"Mapa - {floorLabel} (jugador - Tab: ver todo)"
                : $"Mapa - {floorLabel} (DEBUG: mapa completo - Tab: ocultar)";
            GUI.Label(new Rect(panelX, panelY, w * cellPixelSize, 20), title);

            DungeonMapRenderer.Draw(new Vector2(panelX, top), floor, playerMode, cellPixelSize, wallPixelThickness,
                player, showPlayerMarker: true, foe: dungeonManager.ActiveFoe, foeAlwaysVisible: dungeonManager.debugFoeAlwaysVisibleOnMap,
                wildlifePositions: _wildlifePositions, wildlifeCellSize: dungeonManager.settings.cellSize);
        }
    }
}
