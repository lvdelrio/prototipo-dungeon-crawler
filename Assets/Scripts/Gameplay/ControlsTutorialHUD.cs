using UnityEngine;

namespace Gameplay
{
    // Cartel de bienvenida con los controles basicos: aparece UNA sola vez en la vida del
    // guardado (persistido en MetaProgress.SeenControlsTutorial), justo cuando el jugador entra de
    // verdad a la mazmorra por primera vez. Con DebugHUD y MinimapUI ahora ocultos fuera del Editor
    // (ver esos scripts -- eran ayudas de desarrollo, no UI para el jugador real), este cartel es
    // el unico lugar donde alguien jugando el build compilado se entera de que teclas existen.
    public class ControlsTutorialHUD : MonoBehaviour
    {
        public DungeonManager dungeonManager;

        public bool IsOpen { get; private set; }
        private bool _checked;

        private static readonly string[] Lines =
        {
            "W / S -- caminar adelante / atras",
            "A / D -- girar izquierda / derecha",
            "Espacio -- interactuar / confirmar",
            "M -- levantar el mapa fisico y dibujarlo a mano",
            "L -- usar item Mapa",
            "P -- usar Perforador",
            "N -- usar Incienso",
            "I -- menu de pausa (equipo, formacion, guardar)",
        };

        void Update()
        {
            // Se decide una sola vez, apenas IsReady pasa a true (recien salido del menu inicial):
            // si el guardado ya lo vio antes, ni se abre.
            if (_checked || dungeonManager == null || !dungeonManager.IsReady || dungeonManager.Meta == null) return;
            _checked = true;
            IsOpen = !dungeonManager.Meta.SeenControlsTutorial;
        }

        void OnGUI()
        {
            if (!IsOpen) return;

            var old = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;

            float w = Screen.width, h = Screen.height;
            float boxW = Mathf.Min(w - 80f, 620f);
            float boxX = (w - boxW) / 2f;
            float y = h * 0.18f;

            var titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 28 };
            titleStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(boxX, y, boxW, 40), "CONTROLES", titleStyle);
            y += 34f;

            var subStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
            subStyle.normal.textColor = new Color(0.65f, 0.65f, 0.65f);
            GUI.Label(new Rect(boxX, y, boxW, 22), "(esto solo aparece la primera vez)", subStyle);
            y += 40f;

            var lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            lineStyle.normal.textColor = new Color(0.88f, 0.88f, 0.88f);
            foreach (var line in Lines)
            {
                GUI.Label(new Rect(boxX + 30, y, boxW - 60, 26), line, lineStyle);
                y += 28f;
            }

            y += 24f;
            bool clicked = UIButton.Draw(new Rect(boxX + (boxW - 220f) / 2f, y, 220f, 44f), "ENTENDIDO", accentColor: new Color(1f, 0.78f, 0.2f));
            if (clicked || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
                Close();
        }

        private void Close()
        {
            IsOpen = false;
            dungeonManager.Meta.SeenControlsTutorial = true;
            MetaSaveService.Save(dungeonManager.Meta);
        }
    }
}
