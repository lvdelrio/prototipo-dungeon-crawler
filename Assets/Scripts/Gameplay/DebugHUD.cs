using UnityEngine;

namespace Gameplay
{
    public class DebugHUD : MonoBehaviour
    {
        public DungeonManager dungeonManager;
        public GridPlayerController player;
        public PauseMenuManager pauseMenu;

        private string _lastMessage = "";
        private string _validationResult = "";

        public void SetLastMessage(string msg) => _lastMessage = msg;

        void OnGUI()
        {
            if (dungeonManager == null || player == null || !dungeonManager.IsReady) return;
            // Se esconde durante el combate, la pantalla de tienda/mejoras post-run y el menu de
            // pausa: en todos los casos hay otro panel mas importante que no debe quedar tapado.
            if (dungeonManager.IsCombatActive || dungeonManager.IsGameOverShopActive) return;
            if (pauseMenu != null && pauseMenu.IsOpen) return;

            GUI.Box(new Rect(10, 10, 460, 210), "");
            GUI.Label(new Rect(20, 15, 440, 20),
                $"Piso: {dungeonManager.CurrentFloorIndex} | Celda: ({player.CellX},{player.CellY}) | Mirando: {player.Facing}");
            GUI.Label(new Rect(20, 35, 440, 40), "W/S: caminar | A/D: girar | Espacio: interactuar | Tab: modo mapa\nM: levantar mapa fisico (flechitas: pintar pared) | L: usar item Mapa | P: usar Perforador | N: usar Incienso | I: menú de pausa | T: dialogo DEMO (taberna de prueba)");
            GUI.Label(new Rect(20, 75, 440, 40), $"Ultimo evento: {_lastMessage}");
            GUI.Label(new Rect(20, 115, 440, 20), $"Peligro acumulado: {dungeonManager.CurrentWalkingCounter} / {dungeonManager.CurrentEncounterThreshold} (limite oculto real en el juego; visible aca solo para debug)");
            if (dungeonManager.Meta != null)
                GUI.Label(new Rect(20, 135, 440, 20), $"Puntos: {dungeonManager.Meta.BankedPoints} | Mapas: {dungeonManager.Meta.MapCharges} | Perforadores: {dungeonManager.Meta.DrillCharges} | Incienso: {dungeonManager.Meta.IncenseCharges}");

            if (UIButton.Draw(new Rect(20, 158, 160, 25), "Validar Dungeon"))
            {
                var (ok, issues) = dungeonManager.ValidateCurrentDungeon();
                _validationResult = ok
                    ? "VALIDACION OK: mapa 100% resoluble (start->end, mision secundaria, atajo, multi-piso)."
                    : "FALLOS:\n" + string.Join("\n", issues);
            }

            GUI.Label(new Rect(20, 188, 900, 300), _validationResult);
        }
    }
}
