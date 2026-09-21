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
        private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        // Mismo naranjo de "golpe" que Element.Strike en ElementVisuals: este pulso es generico
        // (se dispara en CUALQUIER golpe, no segun elemento), asi que usa el color canonico de
        // golpe fisico en vez de un naranjo propio que se podia desalinear con el resto.
        private static readonly Color DefaultEdgeColor = new Color(ElementVisuals.GolpeColor.r, ElementVisuals.GolpeColor.g, ElementVisuals.GolpeColor.b, 1f);
        private static readonly Color BreakEdgeColor = new Color(1f, 0.95f, 0.25f, 1f);

        // Posicion (mundo) justo arriba de la cabeza del enemigo, para anclar ahi la barra de
        // vida/aguante flotante (EnemyHealthBarHUD) sin importar el tamano/escala de la especie.
        public Vector3 TopAnchor => _renderer != null
            ? new Vector3(transform.position.x, _renderer.bounds.max.y + 0.25f, transform.position.z)
            : transform.position + Vector3.up;

        public void Initialize(EnemyStats stats, Renderer renderer, Color baseColor)
        {
            Stats = stats;
            _renderer = renderer;
            _props = new MaterialPropertyBlock();
            _props.SetColor(ColorId, baseColor);
            _props.SetFloat(DissolveAmountId, 0f);
            _props.SetColor(EdgeColorId, DefaultEdgeColor);
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

        // Pulso mas fuerte y con el borde en amarillo brillante (en vez del naranja normal), para
        // que romper el "aguante" de un enemigo se note claramente distinto de un golpe comun.
        public void PlayBreakFlash()
        {
            if (_activeRoutine != null) StopCoroutine(_activeRoutine);
            _activeRoutine = StartCoroutine(BreakFlashRoutine());
        }

        private IEnumerator BreakFlashRoutine()
        {
            const float peak = 0.55f;
            const float upTime = 0.07f;
            const float holdTime = 0.12f;
            const float downTime = 0.3f;

            _props.SetColor(EdgeColorId, BreakEdgeColor);
            float t = 0f;
            while (t < upTime)
            {
                t += Time.deltaTime;
                SetDissolve(Mathf.Lerp(0f, peak, t / upTime));
                yield return null;
            }
            yield return new WaitForSeconds(holdTime);
            t = 0f;
            while (t < downTime)
            {
                t += Time.deltaTime;
                SetDissolve(Mathf.Lerp(peak, 0f, t / downTime));
                yield return null;
            }
            SetDissolve(0f);
            _props.SetColor(EdgeColorId, DefaultEdgeColor);
            if (_renderer != null) _renderer.SetPropertyBlock(_props);
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
