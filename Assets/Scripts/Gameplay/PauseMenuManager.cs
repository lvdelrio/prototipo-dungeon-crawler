using UnityEngine;

namespace Gameplay
{
    // Menu de pausa (tecla I, solo durante la exploracion): Mapa, Leyenda, Habilidades, Codex,
    // Equipamiento, Formacion, Guardar. Mientras esta abierto, el jugador no se puede mover (igual
    // que con un dialogo activo) -- ver el chequeo correspondiente en GridPlayerController.Update().
    public class PauseMenuManager : MonoBehaviour
    {
        public enum Tab { None, Codex, Equipment, Formation, Map, Legend, Skills, Stats }

        public bool IsOpen { get; private set; }
        public Tab CurrentTab { get; private set; } = Tab.None;

        public void Open()
        {
            IsOpen = true;
            CurrentTab = Tab.None;
        }

        public void Close()
        {
            IsOpen = false;
            CurrentTab = Tab.None;
        }

        public void ShowTab(Tab tab) => CurrentTab = tab;
    }
}
