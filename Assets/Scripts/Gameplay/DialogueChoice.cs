using System;

namespace Gameplay
{
    // Una opcion dentro de un dialogo con ramas (p.ej. "Aceptar mision" / "Rechazar"). OnChoose se
    // ejecuta apenas el jugador la elige, antes de cerrar el dialogo.
    public class DialogueChoice
    {
        public string Label;
        public Action OnChoose;

        public DialogueChoice(string label, Action onChoose)
        {
            Label = label;
            OnChoose = onChoose;
        }
    }
}
