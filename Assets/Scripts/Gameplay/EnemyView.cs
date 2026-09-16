using System;
using System.Collections;
using UnityEngine;
using Combat;

namespace Gameplay
{
    // Representacion visual de un enemigo en la escena de batalla: un modelo primitivo (todavia
    // no hay arte importado) con el shader Custom/Dissolve, usado para simular una "animacion" de
    // reaccion al golpe (pulso corto de disolucion con brillo en el borde) y de muerte
    // (disolucion completa) ya que no hay clips de animacion reales.
    public class EnemyView : MonoBehaviour
    {
        public EnemyStats Stats { get; private set; }

        private Renderer _renderer;
        private MaterialPropertyBlock _props;
        private Coroutine _activeRoutine;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int DissolveAmountId = Shader.PropertyToID("_DissolveAmount");

        public void Initialize(EnemyStats stats, Renderer renderer, Color baseColor)
        {
            Stats = stats;
            _renderer = renderer;
            _props = new MaterialPropertyBlock();
            _props.SetColor(ColorId, baseColor);
            _props.SetFloat(DissolveAmountId, 0f);
            _renderer.SetPropertyBlock(_props);
        }

        private void SetDissolve(float amount)
        {
            if (_renderer == null) return;
            _props.SetFloat(DissolveAmountId, amount);
            _renderer.SetPropertyBlock(_props);
        }

        // Pulso corto de disolucion (sube y vuelve a bajar) para simular una reaccion de golpe;
        // el brillo del borde (_EdgeColor) hace de "efecto de impacto".
        public void PlayHitPulse()
        {
            if (_activeRoutine != null) StopCoroutine(_activeRoutine);
            _activeRoutine = StartCoroutine(HitPulseRoutine());
        }

        private IEnumerator HitPulseRoutine()
        {
            const float peak = 0.35f;
            const float upTime = 0.08f;
            const float downTime = 0.18f;

            float t = 0f;
            while (t < upTime)
            {
                t += Time.deltaTime;
                SetDissolve(Mathf.Lerp(0f, peak, t / upTime));
                yield return null;
            }
            t = 0f;
            while (t < downTime)
            {
                t += Time.deltaTime;
                SetDissolve(Mathf.Lerp(peak, 0f, t / downTime));
                yield return null;
            }
            SetDissolve(0f);
            _activeRoutine = null;
        }

        // Disolucion completa (0 -> 1) para simular una animacion de muerte; avisa por callback
        // cuando termina para que quien la pidio destruya el GameObject.
        public void PlayDeathDissolve(Action onComplete)
        {
            if (_activeRoutine != null) StopCoroutine(_activeRoutine);
            _activeRoutine = StartCoroutine(DeathDissolveRoutine(onComplete));
        }

        private IEnumerator DeathDissolveRoutine(Action onComplete)
        {
            const float duration = 0.8f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                SetDissolve(Mathf.Lerp(0f, 1f, t / duration));
                yield return null;
            }
            SetDissolve(1f);
            _activeRoutine = null;
            onComplete?.Invoke();
        }
    }
}
