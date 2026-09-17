using UnityEngine;

namespace Gameplay
{
    // Caja de dialogo anclada abajo (mismo estilo que CombatHUD): nombre del hablante, texto, y
    // "Continuar" o los botones de cada opcion segun tenga o no ramas el dialogo actual.
    public class DialogueHUD : MonoBehaviour
    {
        public DialogueManager dialogueManager;

        void OnGUI()
        {
            if (dialogueManager == null || !dialogueManager.IsActive) return;

            float panelW = Mathf.Min(Screen.width - 40f, 820f);
            float panelH = 160f;
            float panelX = (Screen.width - panelW) / 2f;
            float panelY = Screen.height - panelH - 20f;

            GUI.Box(new Rect(panelX, panelY, panelW, panelH), "");
            GUI.Label(new Rect(panelX + 20, panelY + 10, panelW - 40, 24), dialogueManager.Speaker);

            var oldWrap = GUI.skin.label.wordWrap;
            GUI.skin.label.wordWrap = true;
            GUI.Label(new Rect(panelX + 20, panelY + 38, panelW - 40, 70), dialogueManager.Text);
            GUI.skin.label.wordWrap = oldWrap;

            if (dialogueManager.Choices != null)
            {
                float bx = panelX + 20;
                float by = panelY + panelH - 40;
                foreach (var choice in dialogueManager.Choices)
                {
                    float bw = Mathf.Min(220f, (panelW - 40f) / Mathf.Max(1, dialogueManager.Choices.Count) - 10f);
                    if (GUI.Button(new Rect(bx, by, bw, 30), choice.Label))
                        dialogueManager.Choose(choice);
                    bx += bw + 10f;
                }
            }
            else
            {
                if (GUI.Button(new Rect(panelX + panelW - 150, panelY + panelH - 40, 130, 30), "Continuar"))
                    dialogueManager.Advance();
            }
        }
    }
}
