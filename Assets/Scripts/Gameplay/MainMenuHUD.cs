using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Combat;

namespace Gameplay
{
    // Pantalla completa (fondo negro, mismo espiritu que MetaShopHUD) que tapa TODO hasta que el
    // jugador elige Continuar o Nueva Partida -- mientras este abierta, DungeonManager.IsReady
    // sigue en false y el resto del juego (movimiento, minimapa, HUD de debug) esta congelado/
    // oculto (ver los chequeos de IsReady agregados en esos scripts).
    public class MainMenuHUD : MonoBehaviour
    {
        public MainMenuManager mainMenu;
        public DungeonManager dungeonManager;

        // Clases elegidas hasta ahora en la pantalla de creacion de party (Stage.PartyCreation).
        // Se resetea cada vez que se entra de nuevo a esa etapa (Nueva Partida -> Cancelar ->
        // Nueva Partida de nuevo no deberia arrastrar la seleccion anterior).
        private readonly List<CharacterClass> _selected = new List<CharacterClass>();
        private const int PartySize = 6;

        private static readonly Dictionary<CharacterClass, string> ClassBlurbs = new Dictionary<CharacterClass, string>
        {
            { CharacterClass.Warrior, "Guerrero cuerpo a cuerpo: Corte parejo y fuerte." },
            { CharacterClass.Protector, "Tanque: puede proteger a todo el grupo a la vez." },
            { CharacterClass.Ranger, "Tirador veloz, Perforación certera." },
            { CharacterClass.Alchemist, "Ataques mágicos en ÁREA (Fuego a TODOS los enemigos)." },
            { CharacterClass.Mage, "Mago de Hielo, alto daño mágico a un solo blanco." },
            { CharacterClass.Medic, "Cura a los aliados; escala con Ataque Mágico." },
            { CharacterClass.Gunner, "Balas elementales intercambiables (Fuego/Hielo/Rayo)." },
            { CharacterClass.Berserker, "Full ataque a costa de la defensa; postura de furia." },
            { CharacterClass.Trovador, "Versátil: buffea aliados o debuffea enemigos." },
        };

        void OnGUI()
        {
            if (mainMenu == null || dungeonManager == null || !mainMenu.IsOpen) return;

            DrawBlackBackdrop();

            switch (mainMenu.CurrentStage)
            {
                case MainMenuManager.Stage.RootChoice: DrawRootChoice(); break;
                case MainMenuManager.Stage.ConfirmOverwrite: DrawConfirmOverwrite(); break;
                case MainMenuManager.Stage.PartyCreation: DrawPartyCreation(); break;
            }
        }

        private void DrawBlackBackdrop()
        {
            var old = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void DrawRootChoice()
        {
            float w = Screen.width, h = Screen.height;

            var titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 36 };
            titleStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(0, h * 0.28f, w, 60), "ETRIAN DUNGEON CRAWLER", titleStyle);

            var subStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15 };
            subStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(new Rect(0, h * 0.28f + 56, w, 26), "Prototipo de dungeon crawler roguelite", subStyle);

            float btnW = 320f, btnH = 48f, gap = 16f;
            bool canContinue = dungeonManager.HasExistingSave;
            float startY = h * 0.5f;

            if (UIButton.Draw(new Rect((w - btnW) / 2f, startY, btnW, btnH), "CONTINUAR", enabled: canContinue, accentColor: new Color(0.3f, 0.75f, 1f)))
            {
                dungeonManager.ContinueRun();
                mainMenu.SetStage(MainMenuManager.Stage.Closed);
            }
            if (!canContinue)
            {
                var hintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
                hintStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                GUI.Label(new Rect((w - btnW) / 2f, startY + btnH + 2, btnW, 18), "(todavía no hay ninguna partida guardada)", hintStyle);
            }

            if (UIButton.Draw(new Rect((w - btnW) / 2f, startY + btnH + gap + 20f, btnW, btnH), "NUEVA PARTIDA", accentColor: new Color(1f, 0.78f, 0.2f)))
            {
                _selected.Clear();
                mainMenu.SetStage(canContinue ? MainMenuManager.Stage.ConfirmOverwrite : MainMenuManager.Stage.PartyCreation);
            }
        }

        private void DrawConfirmOverwrite()
        {
            float w = Screen.width, h = Screen.height;
            float boxW = Mathf.Min(w - 80f, 560f), boxH = 200f;
            float boxX = (w - boxW) / 2f, boxY = (h - boxH) / 2f;

            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.05f);
            GUI.DrawTexture(new Rect(boxX, boxY, boxW, boxH), Texture2D.whiteTexture);
            GUI.color = new Color(0.82f, 0.08f, 0.1f);
            GUI.DrawTexture(new Rect(boxX, boxY, boxW, 4f), Texture2D.whiteTexture);
            GUI.color = old;

            var msgStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = 15 };
            msgStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(boxX + 20, boxY + 20, boxW - 40, 90),
                "Ya existe una partida guardada. Empezar una nueva partida BORRA ese progreso (puntos, mejoras, items) y arranca de cero. ¿Seguro?",
                msgStyle);

            if (UIButton.Draw(new Rect(boxX + 30, boxY + boxH - 60, 220, 40), "Sí, borrar y empezar", accentColor: new Color(0.82f, 0.08f, 0.1f)))
            {
                _selected.Clear();
                mainMenu.SetStage(MainMenuManager.Stage.PartyCreation);
            }
            if (UIButton.Draw(new Rect(boxX + boxW - 250, boxY + boxH - 60, 220, 40), "Cancelar"))
                mainMenu.SetStage(MainMenuManager.Stage.RootChoice);
        }

        private void DrawPartyCreation()
        {
            float w = Screen.width, h = Screen.height;

            var titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 24 };
            titleStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(0, 24f, w, 34), $"ARMÁ TU PARTY -- elegí {PartySize} ({_selected.Count}/{PartySize})", titleStyle);

            float contentW = Mathf.Min(w - 80f, 1100f);
            float contentX = (w - contentW) / 2f;
            float top = 80f;

            const int cols = 3;
            float cardW = (contentW - (cols - 1) * 16f) / cols;
            float cardH = 110f;

            var classes = PartyFactory.AllSelectableClasses;
            for (int i = 0; i < classes.Length; i++)
            {
                var cls = classes[i];
                int col = i % cols, row = i / cols;
                var rect = new Rect(contentX + col * (cardW + 16f), top + row * (cardH + 14f), cardW, cardH);
                DrawClassCard(rect, cls);
            }

            float bottomY = top + Mathf.CeilToInt(classes.Length / (float)cols) * (cardH + 14f) + 20f;

            var hintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            hintStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(new Rect(contentX, bottomY, contentW, 24), "Clic en una clase para agregarla o sacarla de la party.", hintStyle);

            float btnW = 260f, btnH = 44f;
            if (UIButton.Draw(new Rect(contentX, bottomY + 34f, 220f, btnH), "Clásica (por defecto)"))
            {
                _selected.Clear();
                _selected.AddRange(PartyFactory.DefaultClasses);
            }

            bool canConfirm = _selected.Count == PartySize;
            if (UIButton.Draw(new Rect(contentX + contentW - btnW, bottomY + 34f, btnW, btnH), "CONFIRMAR Y EMPEZAR", enabled: canConfirm, accentColor: new Color(1f, 0.78f, 0.2f)))
            {
                dungeonManager.BeginBrandNewGame(_selected);
                mainMenu.SetStage(MainMenuManager.Stage.Closed);
            }

            if (UIButton.Draw(new Rect(contentX, bottomY + 34f + btnH + 10f, 140f, 32f), "< Volver"))
                mainMenu.SetStage(MainMenuManager.Stage.RootChoice);
        }

        private void DrawClassCard(Rect rect, CharacterClass cls)
        {
            bool selected = _selected.Contains(cls);
            var old = GUI.color;
            GUI.color = selected ? new Color(1f, 0.85f, 0.3f, 0.18f) : new Color(1f, 1f, 1f, 0.05f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = selected ? new Color(1f, 0.78f, 0.2f) : new Color(0.4f, 0.4f, 0.45f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 4f, rect.height), Texture2D.whiteTexture);
            GUI.color = old;

            var nameStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 16 };
            nameStyle.normal.textColor = selected ? new Color(1f, 0.85f, 0.4f) : Color.white;
            GUI.Label(new Rect(rect.x + 14, rect.y + 8, rect.width - 28, 22), cls.ToString(), nameStyle);

            var descStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            descStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            string blurb = ClassBlurbs.TryGetValue(cls, out var b) ? b : "";
            GUI.Label(new Rect(rect.x + 14, rect.y + 32, rect.width - 28, rect.height - 60), blurb, descStyle);

            bool wouldExceed = !selected && _selected.Count >= PartySize;
            string btnLabel = selected ? "Sacar" : "Agregar";
            if (UIButton.Draw(new Rect(rect.x + 14, rect.y + rect.height - 30, rect.width - 28, 24), btnLabel, enabled: selected || !wouldExceed))
            {
                if (selected) _selected.Remove(cls);
                else _selected.Add(cls);
            }
        }
    }
}
