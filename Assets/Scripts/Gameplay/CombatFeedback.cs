using System.Collections;
using UnityEngine;

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

        private Material _flashMat;
        private float _flashIntensity;
        private Color _flashColor = Color.white;
        private Coroutine _flashRoutine;

        private Vector3 _shakeBasePos;
        private float _shakeTimeLeft;
        private float _shakeMagnitude;

        void Awake()
        {
            if (flashShader == null) flashShader = Shader.Find("Hidden/HitFlash");
            if (flashShader != null) _flashMat = new Material(flashShader);
        }

        public void OnEnemyHit(int damage) => Impact(enemyHitFlashColor, enemyHitFlashDuration, enemyHitShakeDuration, enemyHitShakeMagnitude);

        public void OnPartyHit(int damage) => Impact(partyHitFlashColor, partyHitFlashDuration, partyHitShakeDuration, partyHitShakeMagnitude);

        public void OnHeal(int amount) => Flash(healFlashColor, healFlashDuration);

        public void OnAllOutAttack() => Impact(allOutFlashColor, allOutFlashDuration, allOutShakeDuration, allOutShakeMagnitude);

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
            if (_flashMat == null || _flashIntensity <= 0.001f)
            {
                Graphics.Blit(source, destination);
                return;
            }

            _flashMat.SetColor("_FlashColor", _flashColor);
            _flashMat.SetFloat("_Intensity", _flashIntensity);
            Graphics.Blit(source, destination, _flashMat);
        }
    }
}
