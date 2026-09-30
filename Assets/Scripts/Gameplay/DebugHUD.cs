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
        // Input crudo del campo "Piso" del panel de warp -- se parsea recien al tocar "Ir", asi
        // el jugador puede borrar/escribir sin que cada tecla intente un ChangeFloor invalido.
        private string _floorJumpInput = "";

        public void SetLastMessage(string msg) => _lastMessage = msg;

        // Nombre corto para cada bioma; mantiene el mismo mapeo que DungeonManager.FloorLabel.
        private static string BiomeButtonLabel(int biome) => biome switch
        {
            0 => "Bioma 0 (Principal)",
            1 => "Bioma 1 (Espacial)",
            2 => "Bioma 2 (Cuevas)",
            3 => "Bioma 3 (Patio)",
            4 => "Bioma 4 (Castillo)",
            _ => $"Bioma {biome}",
        };

        void OnGUI()
        {
            // Panel de debug (contador de peligro oculto, boton "Validar Dungeon", etc.): solo
            // tiene sentido para desarrollo, nunca en un build real -- Application.isEditor es
            // false en cualquier .exe compilado (Development o Release), aunque siga true al
            // darle Play adentro del Editor.
            if (!Application.isEditor) return;
            if (dungeonManager == null || player == null || !dungeonManager.IsReady) return;
            // Se esconde durante el combate, la pantalla de tienda/mejoras post-run y el menu de
            // pausa: en todos los casos hay otro panel mas importante que no debe quedar tapado.
            if (dungeonManager.IsCombatActive || dungeonManager.IsGameOverShopActive) return;
            if (pauseMenu != null && pauseMenu.IsOpen) return;

            // Evita que IMGUI le de foco de teclado al campo de texto "Piso" (_floorJumpInput) cada
            // vez que se aprieta Tab (pedido puntual: "tab me hace escribir en el textbox de
            // arriba") -- sin esto, el ciclo automatico de foco de Unity le agarra el Tab porque es
            // el unico control con foco de teclado del panel, y las teclas siguientes se van al
            // cuadro de texto en vez de al juego. Interceptar el evento ANTES de dibujar cualquier
            // control y limpiar el foco alcanza; Input.GetKeyDown(KeyCode.Tab) en MinimapUI sigue
            // viendo la tecla igual (es polling de bajo nivel, no pasa por el Event de IMGUI), asi
            // que "modo mapa" (Tab) sigue funcionando sin tocar nada ahi.
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Tab)
            {
                GUIUtility.keyboardControl = 0;
                Event.current.Use();
            }

            GUI.Box(new Rect(10, 10, 460, 230), "");
            GUI.Box(new Rect(480, 10, 260, 210), "");
            GUI.Label(new Rect(20, 15, 440, 20),
                $"Piso: {dungeonManager.CurrentFloorIndex} | Celda: ({player.CellX},{player.CellY}) | Mirando: {player.Facing}");
            GUI.Label(new Rect(20, 35, 440, 50), "W/S: caminar | A/D: girar | Espacio: interactuar | Tab: modo mapa | Shift (mantener): ocultar minimapa\nM: levantar mapa fisico (flechitas: pintar pared) | L: usar item Mapa | P: usar Perforador | N: usar Incienso | I: menú de pausa | T: dialogo DEMO (taberna de prueba)");
            GUI.Label(new Rect(20, 75, 440, 40), $"Ultimo evento: {_lastMessage}");
            GUI.Label(new Rect(20, 115, 440, 20), $"Peligro acumulado: {dungeonManager.CurrentWalkingCounter} / {dungeonManager.CurrentEncounterThreshold} (limite oculto real en el juego; visible aca solo para debug)");
            if (dungeonManager.Meta != null)
                GUI.Label(new Rect(20, 135, 440, 20), $"Puntos: {dungeonManager.Meta.BankedPoints} | Mapas: {dungeonManager.Meta.MapCharges} | Perforadores: {dungeonManager.Meta.DrillCharges} | Incienso: {dungeonManager.Meta.IncenseCharges}");

            // Debug puntual: para verificar a mano el castillo lejano (DungeonLevelBuilder.
            // BuildDistantCastleLandmark) hace falta saber DONDE queda la Puerta del castillo en
            // este piso -- sin esto, no hay forma de saber si "no se ve" es porque el jugador esta
            // mirando para cualquier otro lado o porque el objeto de verdad no aparece.
            if (dungeonManager.CurrentFloor.CastleGatePos.HasValue)
            {
                var gate = dungeonManager.CurrentFloor.CastleGatePos.Value;
                GUI.Label(new Rect(20, 155, 440, 20), $"[DEBUG] Puerta del castillo en celda ({gate.x},{gate.y}) de este piso.");
            }

            if (UIButton.Draw(new Rect(20, 178, 160, 25), "Validar Dungeon"))
            {
                var (ok, issues) = dungeonManager.ValidateCurrentDungeon();
                _validationResult = ok
                    ? "VALIDACION OK: mapa 100% resoluble (start->end, mision secundaria, atajo, multi-piso)."
                    : "FALLOS:\n" + string.Join("\n", issues);
            }

            GUI.Label(new Rect(20, 208, 900, 300), _validationResult);

            // Panel de warp de biomas: salto directo al primer piso de cada bioma existente esta
            // run (ver DungeonManager.DebugWarpToBiome), sin tener que jugar hasta la Puerta Fria o
            // la escalera de la cueva -- pensado solo para testear ambientacion/geometria/enemigos
            // de cada bioma a mano.
            GUI.Label(new Rect(490, 15, 240, 20), "DEBUG: saltar a bioma");
            float y = 35;
            foreach (var biome in dungeonManager.DebugAvailableBiomes())
            {
                if (UIButton.Draw(new Rect(490, y, 240, 25), BiomeButtonLabel(biome)))
                    dungeonManager.DebugWarpToBiome(biome);
                y += 30;
            }

            GUI.Label(new Rect(490, y + 5, 240, 20), $"Piso global (0-{dungeonManager.Floors.Count - 1}):");
            _floorJumpInput = GUI.TextField(new Rect(490, y + 25, 100, 25), _floorJumpInput);
            if (UIButton.Draw(new Rect(600, y + 25, 130, 25), "Ir a piso"))
            {
                if (int.TryParse(_floorJumpInput, out var floorIndex))
                    dungeonManager.DebugWarpToFloor(floorIndex);
            }
        }
    }
}
