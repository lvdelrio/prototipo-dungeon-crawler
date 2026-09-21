using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    // Sistema de dialogo generico: una linea simple con "Continuar", o una linea con varias
    // opciones (ramas) que ejecutan una accion al elegirlas. No sabe nada de NPCs, misiones ni
    // mazmorra -- cualquier sistema (un NPC, un cofre, un cartel) puede llamar a Show/ShowChoices
    // para mostrar texto y esperar al jugador. Mientras IsActive es true, GridPlayerController
    // congela el movimiento (igual que en combate o en la tienda).
    public class DialogueManager : MonoBehaviour
    {
        public bool IsActive { get; private set; }
        public string Speaker { get; private set; } = "";
        public string Text { get; private set; } = "";
        public IReadOnlyList<DialogueChoice> Choices { get; private set; }

        private Action _onClose;

        // Linea simple: se cierra con "Continuar" (o llamando a Advance()).
        public void Show(string speaker, string text, Action onClose = null)
        {
            Speaker = speaker;
            Text = text;
            Choices = null;
            _onClose = onClose;
            IsActive = true;
        }

        // Linea con opciones: se cierra eligiendo una de las opciones (no hay "Continuar").
        public void ShowChoices(string speaker, string text, List<DialogueChoice> choices)
        {
            Speaker = speaker;
            Text = text;
            Choices = choices;
            _onClose = null;
            IsActive = true;
        }

        // Usado por el boton "Continuar" del DialogueHUD; no hace nada si el dialogo actual tiene
        // opciones (esos se cierran eligiendo una opcion, no con "Continuar").
        public void Advance()
        {
            if (!IsActive || Choices != null) return;
            IsActive = false;
            var callback = _onClose;
            _onClose = null;
            callback?.Invoke();
        }

        public void Choose(DialogueChoice choice)
        {
            if (!IsActive || choice == null) return;
            IsActive = false;
            Choices = null;
            choice.OnChoose?.Invoke();
        }
    }
}
