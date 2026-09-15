using System;
using System.Collections.Generic;
using UnityEngine;
using Combat;

namespace Gameplay
{
    // Mini-juego de tiempo (QTE) que se dispara al usar una habilidad: el jugador tiene que
    // presionar una secuencia de 3 flechas dentro del tiempo limite para pegar/curar mas fuerte.
    // La secuencia en si (cual de las 2 fijas de la habilidad, y cuanto tiempo dar) la decide quien
    // arranca el QTE (CombatHUD); esta clase solo mide el tiempo y compara tecla por tecla.
    public class QteManager : MonoBehaviour
    {
        public float TimeLimit = 2.5f;

        public bool IsActive { get; private set; }
        public float TimeRemaining { get; private set; }
        public int ProgressIndex { get; private set; }
        public IReadOnlyList<QteKey> Sequence { get; private set; } = Array.Empty<QteKey>();

        private Action<bool> _onComplete;

        public void Begin(IReadOnlyList<QteKey> sequence, float timeLimit, Action<bool> onComplete)
        {
            Sequence = sequence;
            TimeLimit = timeLimit;
            TimeRemaining = timeLimit;
            ProgressIndex = 0;
            _onComplete = onComplete;
            IsActive = true;
        }

        void Update()
        {
            if (!IsActive) return;

            TimeRemaining -= Time.deltaTime;
            if (TimeRemaining <= 0f)
            {
                Finish(success: false);
                return;
            }

            QteKey? pressed = ReadKeyDown();
            if (!pressed.HasValue) return;

            if (pressed.Value == Sequence[ProgressIndex])
            {
                ProgressIndex++;
                if (ProgressIndex >= Sequence.Count)
                    Finish(success: true);
            }
            else
            {
                Finish(success: false);
            }
        }

        private QteKey? ReadKeyDown()
        {
            if (Input.GetKeyDown(KeyCode.UpArrow)) return QteKey.Up;
            if (Input.GetKeyDown(KeyCode.DownArrow)) return QteKey.Down;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) return QteKey.Left;
            if (Input.GetKeyDown(KeyCode.RightArrow)) return QteKey.Right;
            return null;
        }

        private void Finish(bool success)
        {
            IsActive = false;
            var callback = _onComplete;
            _onComplete = null;
            callback?.Invoke(success);
        }
    }
}
