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

        void Update()
        {
            if (Input.GetKeyDown(toggleModeKey)) playerMode = !playerMode;
        }

        void OnGUI()
        {
            if (dungeonManager == null || player == null || !dungeonManager.IsReady) return;
            // El mapa desaparece durante el combate, la pantalla de tienda/mejoras post-run y el
            // menu de pausa: en todos los casos hay otro panel mas importante que no debe quedar tapado.
            if (dungeonManager.IsCombatActive || dungeonManager.IsGameOverShopActive) return;
            if (pauseMenu != null && pauseMenu.IsOpen) return;
            var floor = dungeonManager.CurrentFloor;
            if (floor == null) return;

            int w = floor.Width;
            int h = floor.Height;
            // Anclado arriba a la derecha, ajustado al tamano real del mapa, en vez de una
            // posicion X fija que en pantallas angostas podia salirse o superponerse con otra UI.
            int panelX = Screen.width - (w * cellPixelSize + 20) - rightMargin;
            float top = panelY + 24;

            GUI.Box(new Rect(panelX - 10, panelY - 10, w * cellPixelSize + 20, h * cellPixelSize + 44), "");
            string title = playerMode
                ? $"Mapa - Piso {dungeonManager.CurrentFloorIndex} (jugador - Tab: ver todo)"
                : $"Mapa - Piso {dungeonManager.CurrentFloorIndex} (DEBUG: mapa completo - Tab: ocultar)";
            GUI.Label(new Rect(panelX, panelY, w * cellPixelSize, 20), title);

            DungeonMapRenderer.Draw(new Vector2(panelX, top), floor, playerMode, cellPixelSize, wallPixelThickness,
                player, showPlayerMarker: true, foe: dungeonManager.ActiveFoe, foeAlwaysVisible: dungeonManager.debugFoeAlwaysVisibleOnMap);
        }
    }
}
