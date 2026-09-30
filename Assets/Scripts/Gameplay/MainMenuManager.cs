namespace Gameplay
{
    // Estado del menu inicial (ver MainMenuHUD para el dibujo): arranca en RootChoice y termina en
    // Closed, que es cuando DungeonManager.IsReady pasa a true y el resto del juego (movimiento,
    // minimapa, HUD de debug) se habilita.
    public class MainMenuManager : UnityEngine.MonoBehaviour
    {
        public enum Stage { RootChoice, ConfirmOverwrite, PartyCreation, Closed }

        public Stage CurrentStage { get; private set; } = Stage.RootChoice;
        public bool IsOpen => CurrentStage != Stage.Closed;

        public void SetStage(Stage stage) => CurrentStage = stage;
    }
}
