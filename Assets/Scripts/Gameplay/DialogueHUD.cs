using UnityEngine;

namespace Gameplay
{
    // Caja de dialogo anclada abajo (mismo estilo "capas apiladas" que UIButton: sombra + borde +
    // cuerpo, para que se sienta parte del mismo juego): nombre del hablante en una franja de
    // acento arriba, texto abajo, y "Continuar" o los botones de cada opcion segun tenga o no
    // ramas el dialogo actual. La usa cualquiera que llame a DialogueManager.Show/ShowChoices --
    // NPCs (demo de taberna) y tambien eventos de mazmorra (cofre/palanca/lore, ver
    // Gameplay/DungeonManager.ShowItemFoundDialogue y los CellType.Lever/Lore/Treasure).
    public class DialogueHUD : MonoBehaviour
    {
        public DialogueManager dialogueManager;

        private static readonly Color Body = new Color(0.07f, 0.07f, 0.1f);
        private static readonly Color Accent = new Color(0.15f, 0.45f, 0.85f); // azul: distingue "info/dialogo" del rojo de accion de UIButton
        private static Texture2D _whiteTex;

        void OnGUI()
        {
            if (dialogueManager == null || !dialogueManager.IsActive) return;

            float panelW = Mathf.Min(Screen.width - 40f, 820f);
            // 220 en vez de los 170 originales: los eventos de mazmorra (cofre/palanca/lore, ver
            // DungeonManager) meten bastante mas texto que una linea de dialogo de NPC comun --
            // nombre + descripcion + stats + clases + nota extra puede ser 4-5 lineas.
            float panelH = 220f;
            float panelX = (Screen.width - panelW) / 2f;
            float panelY = Screen.height - panelH - 20f;
            var rect = new Rect(panelX, panelY, panelW, panelH);

            DrawRect(new Rect(rect.x + 5, rect.y + 6, rect.width, rect.height), new Color(0f, 0f, 0f, 0.4f));
            DrawRect(new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6), Color.white);
            DrawRect(rect, Body);

            const float speakerBarH = 32f;
            bool hasSpeaker = !string.IsNullOrEmpty(dialogueManager.Speaker);
            if (hasSpeaker)
            {
                DrawRect(new Rect(rect.x, rect.y, panelW, speakerBarH), Accent);
                var speakerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    fontSize = 16,
                    alignment = TextAnchor.MiddleLeft,
                };
                speakerStyle.normal.textColor = Color.white;
                GUI.Label(new Rect(rect.x + 16, rect.y, panelW - 32, speakerBarH), dialogueManager.Speaker, speakerStyle);
            }

            float textY = rect.y + (hasSpeaker ? speakerBarH + 10f : 16f);
            var textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                wordWrap = true,
            };
            textStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(rect.x + 20, textY, panelW - 40, panelH - (textY - rect.y) - 46f), dialogueManager.Text, textStyle);

            if (dialogueManager.Choices != null)
            {
                float bx = rect.x + 20;
                float by = rect.y + panelH - 40;
                foreach (var choice in dialogueManager.Choices)
                {
                    float bw = Mathf.Min(220f, (panelW - 40f) / Mathf.Max(1, dialogueManager.Choices.Count) - 10f);
                    if (UIButton.Draw(new Rect(bx, by, bw, 30), choice.Label))
                        dialogueManager.Choose(choice);
                    bx += bw + 10f;
                }
            }
            else
            {
                if (UIButton.Draw(new Rect(rect.x + panelW - 150, rect.y + panelH - 40, 130, 30), "Continuar", accentColor: Accent))
                    dialogueManager.Advance();
            }
        }

        private static void DrawRect(Rect r, Color color)
        {
            if (_whiteTex == null)
            {
                _whiteTex = new Texture2D(1, 1);
                _whiteTex.SetPixel(0, 0, Color.white);
                _whiteTex.Apply();
            }
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, _whiteTex);
            GUI.color = old;
        }
    }
}
