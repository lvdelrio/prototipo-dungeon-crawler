using UnityEngine;

namespace Gameplay
{
    public class DebugHUD : MonoBehaviour
    {
        public DungeonManager dungeonManager;
        public GridPlayerController player;

        private string _lastMessage = "";
        private string _validationResult = "";

        public void SetLastMessage(string msg) => _lastMessage = msg;

        void OnGUI()
        {
            if (dungeonManager == null || player == null) return;

            GUI.Box(new Rect(10, 10, 460, 150), "");
            GUI.Label(new Rect(20, 15, 440, 20),
                $"Piso: {dungeonManager.CurrentFloorIndex} | Celda: ({player.CellX},{player.CellY}) | Mirando: {player.Facing}");
            GUI.Label(new Rect(20, 35, 440, 20), "WASD: mover/strafe | flechas izq/der: girar | Espacio: interactuar");
            GUI.Label(new Rect(20, 55, 440, 40), $"Ultimo evento: {_lastMessage}");

            if (GUI.Button(new Rect(20, 100, 160, 25), "Validar Dungeon"))
            {
                var (ok, issues) = dungeonManager.ValidateCurrentDungeon();
                _validationResult = ok
                    ? "VALIDACION OK: mapa 100% resoluble (start->end, mision secundaria, atajo, multi-piso)."
                    : "FALLOS:\n" + string.Join("\n", issues);
            }

            GUI.Label(new Rect(20, 130, 900, 300), _validationResult);
        }
    }
}
