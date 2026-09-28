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

        // Mismo naranjo canonico de "golpe" que Element.Strike y EnemyView (ver ElementVisuals),
        // asi la pantalla, el borde del enemigo y el anillo de shader coinciden siempre.
        [Header("Flash al golpear a un enemigo")]
        public Color enemyHitFlashColor = new Color(ElementVisuals.GolpeColor.r, ElementVisuals.GolpeColor.g, ElementVisuals.GolpeColor.b, 0.22f);
        public float enemyHitFlashDuration = 0.12f;
        public float enemyHitShakeDuration = 0.12f;
        public float enemyHitShakeMagnitude = 0.05f;

        [Header("Flash al recibir dano la party")]
        public Color partyHitFlashColor = new Color(0.85f, 0.1f, 0.1f, 0.38f);
        public float partyHitFlashDuration = 0.18f;
        public float partyHitShakeDuration = 0.2f;
        public float partyHitShakeMagnitude = 0.09f;

        // Mismo verde canonico de curacion que ElementVisuals.HealColor.
        [Header("Flash al curar (sin sacudida: curar no es un golpe)")]
        public Color healFlashColor = new Color(ElementVisuals.HealColor.r, ElementVisuals.HealColor.g, ElementVisuals.HealColor.b, 0.22f);
        public float healFlashDuration = 0.3f;

        [Header("Flash del Ataque en Conjunto (mas fuerte y dorado)")]
        public Color allOutFlashColor = new Color(1f, 0.85f, 0.25f, 0.45f);
        public float allOutFlashDuration = 0.35f;
        public float allOutShakeDuration = 0.35f;
        public float allOutShakeMagnitude = 0.18f;

        [Header("Brillo de borde por elemento (fuego/hielo/etc, ver ElementVisuals)")]
        public Shader edgeGlowShader;
        public float elementalEdgeDuration = 0.35f;
        public float elementalEdgeIntensity = 0.75f;

        // Hit-stop (congelado breve de Time.timeScale) + aberracion cromatica de pantalla
        // completa: los dos trucos que le faltaban al feedback de impacto para acercarse al golpe
        // de anime de pelea (referencia: Sparking Zero) -- el resto (flash, sacudida, burst/
        // shockwave/spark en BattleStageController) ya estaba. "intensity" usa la MISMA escala que
        // ImpactBurstEffect/ElementalBurstEffect (1 = basico, ~2.5 = habilidad fuerte): cuanto mas
        // fuerte el golpe, mas dura el congelado y mas separados quedan los canales de color.
        [Header("Hit-stop + aberracion cromatica (escala con la intensidad del golpe)")]
        public Shader chromaShader;
        public float hitStopBaseDuration = 0.02f;
        public float hitStopMaxDuration = 0.08f;
        public float chromaBaseAmount = 0.01f;
        public float chromaMaxAmount = 0.035f;
        public float chromaFadeDuration = 0.16f;

        [Header("Hit-stop + aberracion cromatica del Ataque en Conjunto (preset fijo, mas fuerte)")]
        public float allOutHitStopDuration = 0.12f;
        public float allOutChromaAmount = 0.05f;

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

        private Material _chromaMat;
        private float _chromaIntensity;
        private float _chromaAmount;
        private Coroutine _chromaRoutine;

        private Vector3 _shakeBasePos;
        private float _shakeTimeLeft;
        private float _shakeMagnitude;

        private Coroutine _hitStopRoutine;
        private float _hitStopTimeLeft;

        void Awake()
        {
            _flashMat = LoadEffectMaterial("Materials/HitFlash", flashShader, "Hidden/HitFlash");
            _edgeMat = LoadEffectMaterial("Materials/ElementalEdgeGlow", edgeGlowShader, "Hidden/ElementalEdgeGlow");
            _chromaMat = LoadEffectMaterial("Materials/ChromaticBurst", chromaShader, "Hidden/ChromaticBurst");
        }

        // Un build standalone descarta ("strippea") cualquier shader que ningun Material real del
        // proyecto referencie -- Shader.Find funciona en el Editor (ve todos los shaders) pero
        // devuelve null en el juego compilado, y ahi se rompia el post-proceso de OnRenderImage
        // (ver Assets/Resources/Materials/HitFlash.mat y ElementalEdgeGlow.mat: esos SI cuentan
        // como "usados" porque viven en Resources, asi el build los incluye siempre).
        private static Material LoadEffectMaterial(string resourcePath, Shader overrideShader, string shaderName)
        {
            if (overrideShader != null) return new Material(overrideShader);

            var resourceMat = Resources.Load<Material>(resourcePath);
            if (resourceMat != null) return new Material(resourceMat);

            var shader = Shader.Find(shaderName);
            return shader != null ? new Material(shader) : null;
        }

        // intensity: SkillPower del golpe (1 = ataque basico, ~1.4-2.5 = habilidad), la misma
        // escala que ya usan ImpactBurstEffect/ElementalBurstEffect -- asi el hit-stop y la
        // aberracion cromatica crecen junto con el resto del VFX en vez de sentirse desconectados.
        public void OnEnemyHit(int damage, float intensity = 1f) =>
            Impact(enemyHitFlashColor, enemyHitFlashDuration, enemyHitShakeDuration, enemyHitShakeMagnitude, intensity);

        public void OnPartyHit(int damage, float intensity = 1f) =>
            Impact(partyHitFlashColor, partyHitFlashDuration, partyHitShakeDuration, partyHitShakeMagnitude, intensity);

        public void OnHeal(int amount) => Flash(healFlashColor, healFlashDuration);

        public void OnAllOutAttack()
        {
            Impact(allOutFlashColor, allOutFlashDuration, allOutShakeDuration, allOutShakeMagnitude, intensity: 1f);
            // Preset fijo (no escalado por "intensity"): el Ataque en Conjunto ya es el golpe mas
            // grande del juego, no hace falta la formula generica -- valores propios, ajustables
            // aparte en el Inspector.
            TriggerHitStop(allOutHitStopDuration);
            TriggerChroma(allOutChromaAmount);
        }

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

        private void Impact(Color flashColor, float flashDuration, float shakeDuration, float shakeMagnitude, float intensity)
        {
            Flash(flashColor, flashDuration);
            Shake(shakeDuration, shakeMagnitude);

            float t = Mathf.InverseLerp(1f, 2.5f, Mathf.Max(1f, intensity));
            TriggerHitStop(Mathf.Lerp(hitStopBaseDuration, hitStopMaxDuration, t));
            TriggerChroma(Mathf.Lerp(chromaBaseAmount, chromaMaxAmount, t));
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

        private void TriggerChroma(float amount)
        {
            _chromaAmount = Mathf.Max(_chromaAmount, amount);
            _chromaIntensity = 1f;
            if (_chromaRoutine != null) StopCoroutine(_chromaRoutine);
            _chromaRoutine = StartCoroutine(FadeChroma(chromaFadeDuration));
        }

        private IEnumerator FadeChroma(float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                _chromaIntensity = Mathf.Lerp(1f, 0f, duration > 0f ? t / duration : 1f);
                yield return null;
            }
            _chromaIntensity = 0f;
            _chromaAmount = 0f;
            _chromaRoutine = null;
        }

        // Congelado breve de Time.timeScale en el instante del golpe (el truco de "peso" de los
        // juegos de pelea: la accion se frena en seco un instante en vez de seguir fluida) -- la
        // cuenta regresiva usa Time.unscaledDeltaTime porque, con timeScale en 0, Time.deltaTime
        // se queda clavado en 0 y nunca terminaria. Pedidos superpuestos (dos golpes muy seguidos)
        // extienden la duracion en vez de arrancar un segundo congelado -- mismo criterio que
        // Shake de arriba.
        private void TriggerHitStop(float duration)
        {
            if (duration <= 0f) return;
            _hitStopTimeLeft = Mathf.Max(_hitStopTimeLeft, duration);
            if (_hitStopRoutine == null)
                _hitStopRoutine = StartCoroutine(HitStopRoutine());
        }

        private IEnumerator HitStopRoutine()
        {
            float previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            while (_hitStopTimeLeft > 0f)
            {
                _hitStopTimeLeft -= Time.unscaledDeltaTime;
                yield return null;
            }
            Time.timeScale = previousTimeScale;
            _hitStopRoutine = null;
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
            bool hasChroma = _chromaMat != null && _chromaIntensity > 0.001f && _chromaAmount > 0f;
            bool hasEdge = _edgeMat != null && _edgeIntensity > 0.001f;

            if (!hasFlash && !hasChroma && !hasEdge)
            {
                Graphics.Blit(source, destination);
                return;
            }

            RenderTexture current = source;
            RenderTexture temp1 = null;
            RenderTexture temp2 = null;

            if (hasFlash)
            {
                temp1 = RenderTexture.GetTemporary(source.width, source.height, 0, source.format);
                _flashMat.SetColor("_FlashColor", _flashColor);
                _flashMat.SetFloat("_Intensity", _flashIntensity);
                Graphics.Blit(current, temp1, _flashMat);
                current = temp1;
            }

            if (hasChroma)
            {
                temp2 = RenderTexture.GetTemporary(source.width, source.height, 0, source.format);
                _chromaMat.SetFloat("_Intensity", _chromaIntensity);
                _chromaMat.SetFloat("_Amount", _chromaAmount);
                Graphics.Blit(current, temp2, _chromaMat);
                current = temp2;
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

            if (temp1 != null) RenderTexture.ReleaseTemporary(temp1);
            if (temp2 != null) RenderTexture.ReleaseTemporary(temp2);
        }
    }
}
