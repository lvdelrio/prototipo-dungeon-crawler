using System.Collections;
using UnityEngine;
using Combat;

namespace Gameplay
{
    // Feedback visual de impacto en combate: flash de pantalla (shader Hidden/HitFlash, como
    // post-proceso de camara) + sacudida de camara. Vive en la Main Camera; CombatManager lo
    // dispara cuando detecta que alguien recibio dano en un turno (comparando HP antes/despues),
    // sin que el motor de combate puro (Combat/CombatEngine.cs) sepa nada de esto.
    [RequireComponent(typeof(Camera))]
    public class CombatFeedback : MonoBehaviour
    {
        public Shader flashShader;

        [Header("Flash al golpear a un enemigo")]
        public Color enemyHitFlashColor = new Color(1f, 0.95f, 0.6f, 0.35f);
        public float enemyHitFlashDuration = 0.12f;
        public float enemyHitShakeDuration = 0.12f;
        public float enemyHitShakeMagnitude = 0.06f;

        [Header("Flash al recibir dano la party")]
        public Color partyHitFlashColor = new Color(0.85f, 0.1f, 0.1f, 0.55f);
        public float partyHitFlashDuration = 0.18f;
        public float partyHitShakeDuration = 0.22f;
        public float partyHitShakeMagnitude = 0.12f;

        [Header("Flash al curar (sin sacudida: curar no es un golpe)")]
        public Color healFlashColor = new Color(0.35f, 1f, 0.55f, 0.3f);
        public float healFlashDuration = 0.3f;

        [Header("Flash del Ataque en Conjunto (mas fuerte y dorado)")]
        public Color allOutFlashColor = new Color(1f, 0.85f, 0.25f, 0.6f);
        public float allOutFlashDuration = 0.35f;
        public float allOutShakeDuration = 0.35f;
        public float allOutShakeMagnitude = 0.18f;

        [Header("Brillo de borde por elemento (fuego/hielo/etc, ver ElementVisuals)")]
        public Shader edgeGlowShader;
        public float elementalEdgeDuration = 0.4f;
        public float elementalEdgeIntensity = 1.3f;

        private Material _flashMat;
        private float _flashIntensity;
        private Color _flashColor = Color.white;
        private Coroutine _flashRoutine;

        private Material _edgeMat;
        private float _edgeIntensity;
        private Color _edgeColor = Color.white;
        private Coroutine _edgeRoutine;

        // Expuesto solo para inspeccion (tests/depuracion): cuanto brillo de borde queda activo
        // en este momento.
        public float CurrentEdgeIntensity => _edgeIntensity;

        private Vector3 _shakeBasePos;
        private float _shakeTimeLeft;
        private float _shakeMagnitude;

        void Awake()
        {
            if (flashShader == null) flashShader = Shader.Find("Hidden/HitFlash");
            if (flashShader != null) _flashMat = new Material(flashShader);

            if (edgeGlowShader == null) edgeGlowShader = Shader.Find("Hidden/ElementalEdgeGlow");
            if (edgeGlowShader != null) _edgeMat = new Material(edgeGlowShader);
        }

        public void OnEnemyHit(int damage) => Impact(enemyHitFlashColor, enemyHitFlashDuration, enemyHitShakeDuration, enemyHitShakeMagnitude);

        public void OnPartyHit(int damage) => Impact(partyHitFlashColor, partyHitFlashDuration, partyHitShakeDuration, partyHitShakeMagnitude);

        public void OnHeal(int amount) => Flash(healFlashColor, healFlashDuration);

        public void OnAllOutAttack() => Impact(allOutFlashColor, allOutFlashDuration, allOutShakeDuration, allOutShakeMagnitude);

        // Brillo de borde tenido segun el elemento del golpe (fuego, hielo, etc): se ve SIEMPRE,
        // sin importar hacia donde este mirando la camara en ese momento, ademas del efecto en el
        // mundo (particulas/shader sobre el enemigo golpeado).
        public void OnElementalHit(Element element)
        {
            _edgeColor = ElementVisuals.ColorFor(element);
            _edgeIntensity = elementalEdgeIntensity;
            if (_edgeRoutine != null) StopCoroutine(_edgeRoutine);
            _edgeRoutine = StartCoroutine(FadeEdge(elementalEdgeDuration));
        }

        private IEnumerator FadeEdge(float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                _edgeIntensity = Mathf.Lerp(elementalEdgeIntensity, 0f, duration > 0f ? t / duration : 1f);
                yield return null;
            }
            _edgeIntensity = 0f;
            _edgeRoutine = null;
        }

        private void Impact(Color flashColor, float flashDuration, float shakeDuration, float shakeMagnitude)
        {
            Flash(flashColor, flashDuration);
            Shake(shakeDuration, shakeMagnitude);
        }

        private void Flash(Color color, float duration)
        {
            _flashColor = color;
            _flashIntensity = 1f;
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FadeFlash(duration));
        }

        private IEnumerator FadeFlash(float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                _flashIntensity = Mathf.Lerp(1f, 0f, duration > 0f ? t / duration : 1f);
                yield return null;
            }
            _flashIntensity = 0f;
            _flashRoutine = null;
        }

        private void Shake(float duration, float magnitude)
        {
            // Solo recapturamos la posicion de reposo cuando arranca desde quieto: si ya estaba
            // temblando, seguimos sobre la misma base para no perder la referencia real.
            if (_shakeTimeLeft <= 0f)
                _shakeBasePos = transform.localPosition;
            _shakeTimeLeft = Mathf.Max(_shakeTimeLeft, duration);
            _shakeMagnitude = Mathf.Max(_shakeMagnitude, magnitude);
        }

        void LateUpdate()
        {
            if (_shakeTimeLeft <= 0f) return;

            _shakeTimeLeft -= Time.deltaTime;
            if (_shakeTimeLeft <= 0f)
            {
                transform.localPosition = _shakeBasePos;
                return;
            }

            Vector3 offset = Random.insideUnitSphere * _shakeMagnitude;
            offset.z = 0f;
            transform.localPosition = _shakeBasePos + offset;
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            bool hasFlash = _flashMat != null && _flashIntensity > 0.001f;
            bool hasEdge = _edgeMat != null && _edgeIntensity > 0.001f;

            if (!hasFlash && !hasEdge)
            {
                Graphics.Blit(source, destination);
                return;
            }

            RenderTexture current = source;
            RenderTexture temp = null;

            if (hasFlash)
            {
                temp = RenderTexture.GetTemporary(source.width, source.height, 0, source.format);
                _flashMat.SetColor("_FlashColor", _flashColor);
                _flashMat.SetFloat("_Intensity", _flashIntensity);
                Graphics.Blit(current, temp, _flashMat);
                current = temp;
            }

            if (hasEdge)
            {
                _edgeMat.SetColor("_GlowColor", _edgeColor);
                _edgeMat.SetFloat("_Intensity", _edgeIntensity);
                Graphics.Blit(current, destination, _edgeMat);
            }
            else
            {
                Graphics.Blit(current, destination);
            }

            if (temp != null) RenderTexture.ReleaseTemporary(temp);
        }
    }
}
