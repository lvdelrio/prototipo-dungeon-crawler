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

        // Rework del QTE (ver CombatHUD.DrawQteCenterOverlay): antes apretar una tecla de la
        // secuencia no tenia NINGUN feedback de camara, solo el icono cambiaba de color en el
        // panel. Presets chicos (no escalan con "intensity" como Impact, son siempre el mismo
        // golpecito) para que cada acierto se sienta, sin pisar el peso de un golpe de combate
        // real -- el ultimo acierto (que cierra la secuencia) usa el preset "completo", mas fuerte.
        [Header("QTE: acierto de tecla (game feel chico, no es un golpe de verdad)")]
        public Color qteHitFlashColor = new Color(1f, 0.95f, 0.4f, 0.14f);
        public float qteHitFlashDuration = 0.07f;
        public float qteHitShakeDuration = 0.05f;
        public float qteHitShakeMagnitude = 0.02f;
        public float qteHitStopDuration = 0.012f;

        [Header("QTE: secuencia completa (ultimo acierto, mas fuerte)")]
        public Color qteCompleteFlashColor = new Color(0.35f, 1f, 0.45f, 0.26f);
        public float qteCompleteFlashDuration = 0.16f;
        public float qteCompleteShakeDuration = 0.1f;
        public float qteCompleteShakeMagnitude = 0.05f;
        public float qteCompleteHitStopDuration = 0.03f;

        [Header("QTE: fallo (tecla equivocada o se acabo el tiempo)")]
        public Color qteFailFlashColor = new Color(0.6f, 0.12f, 0.12f, 0.3f);
        public float qteFailFlashDuration = 0.16f;
        public float qteFailShakeDuration = 0.08f;
        public float qteFailShakeMagnitude = 0.04f;

        // Rework del Ataque en Conjunto (ver CombatHUD.DrawAllOutAttackCenterOverlay): antes
        // machacar el boton no tenia NINGUN feedback de camara hasta el golpe final. Cada pulsacion
        // ahora da un golpecito chico que crece con mashFrac (0 al primer apreton, 1 en el tope de
        // CombatEngine.AllOutMaxPresses) -- se siente que "cargar" el golpe importa, sin llegar a
        // pisar el peso del golpe final de verdad (OnAllOutAttack, ya existente).
        [Header("Ataque en Conjunto: cada pulsacion durante el machacado (escala con mashFrac 0-1)")]
        public Color allOutMashFlashColor = new Color(1f, 0.85f, 0.3f, 0.1f);
        public float allOutMashFlashDuration = 0.06f;
        public float allOutMashShakeDurationMin = 0.03f;
        public float allOutMashShakeDurationMax = 0.09f;
        public float allOutMashShakeMagnitudeMin = 0.015f;
        public float allOutMashShakeMagnitudeMax = 0.05f;

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

        // Remates mas marcados para las dos habilidades insignia que estamos prototipando. El
        // impacto normal sigue comunicando el dano; este acento extra les da peso sin cambiarlo.
        public void OnSignatureSkillImpact(CharacterClass characterClass)
        {
            if (characterClass == CharacterClass.Warrior)
            {
                Flash(new Color(1f, 0.83f, 0.53f, 0.28f), 0.16f);
                Shake(0.18f, 0.11f);
                TriggerHitStop(0.075f);
                TriggerChroma(0.035f);
            }
            else if (characterClass == CharacterClass.Mage)
            {
                Flash(new Color(0.6f, 0.88f, 1f, 0.2f), 0.2f);
                Shake(0.12f, 0.065f);
                TriggerHitStop(0.06f);
                TriggerChroma(0.03f);
            }
        }

        // Un acierto de tecla dentro del QTE (ver CombatHUD.UpdateQteFeedback, que detecta cuando
        // QteManager.ProgressIndex avanza): isLast = esta fue la tecla que cerro la secuencia
        // entera, usa el preset "completo" (mas fuerte) en vez del golpecito chico de cada tecla
        // intermedia.
        public void OnQteKeyPress(bool isLast)
        {
            if (isLast)
            {
                Flash(qteCompleteFlashColor, qteCompleteFlashDuration);
                Shake(qteCompleteShakeDuration, qteCompleteShakeMagnitude);
                TriggerHitStop(qteCompleteHitStopDuration);
            }
            else
            {
                Flash(qteHitFlashColor, qteHitFlashDuration);
                Shake(qteHitShakeDuration, qteHitShakeMagnitude);
                TriggerHitStop(qteHitStopDuration);
            }
        }

        // Tecla equivocada o se acabo el tiempo: flash rojo corto + sacudida chica, para que la
        // falla se sienta tan clara como el acierto (antes no habia ningun feedback de camara).
        public void OnQteFail()
        {
            Flash(qteFailFlashColor, qteFailFlashDuration);
            Shake(qteFailShakeDuration, qteFailShakeMagnitude);
        }

        // mashCount ya viene clampeado a CombatEngine.AllOutMaxPresses (ver
        // CombatManager.TriggerAllOutAttack). Escala linealmente de 0 a 1 -- el primer apreton
        // apenas se siente, el ultimo antes del tope sacude en serio.
        public void OnAllOutMashPress(int mashCount)
        {
            float mashFrac = Mathf.Clamp01(mashCount / (float)CombatEngine.AllOutMaxPresses);
            Flash(allOutMashFlashColor, allOutMashFlashDuration);
            Shake(Mathf.Lerp(allOutMashShakeDurationMin, allOutMashShakeDurationMax, mashFrac),
                Mathf.Lerp(allOutMashShakeMagnitudeMin, allOutMashShakeMagnitudeMax, mashFrac));
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

        // Usado por CombatHUD (numeros de dano) y EnemyHealthBarHUD (barras) para proyectar
        // posiciones de mundo a pantalla SIN el offset aleatorio del temblor -- LateUpdate de aca
        // arriba ya movio transform.position para este frame antes de que OnGUI llegue a correr
        // (el orden real es Update -> LateUpdate -> OnGUI), asi que un WorldToScreenPoint hecho a
        // ciegas en OnGUI proyecta con la camara ya temblando: el numero/barra sale tirado a
        // cualquier lado del enemigo real en vez de centrado. Esto no es "el numero tiembla un
        // poco", es que agarra una muestra al azar de Random.insideUnitSphere y se queda pegado
        // ahi el resto de su vida (el popup solo calcula su posicion UNA vez, al crearse).
        public bool IsShaking => _shakeTimeLeft > 0f;

        public Vector3 RestWorldPosition => transform.parent != null
            ? transform.parent.TransformPoint(_shakeBasePos)
            : _shakeBasePos;

        // Punto de entrada unico para CombatHUD/EnemyHealthBarHUD: proyecta worldPos a pantalla
        // con esta camara, pero si esta temblando lo hace desde RestWorldPosition en vez de la
        // posicion ya sacudida. Mueve transform.position de ida y vuelta SOLO para el calculo --
        // en el momento que corre OnGUI la camara ya renderizo este frame (el temblor real ya se
        // vio en pantalla como corresponde), asi que este swap es puramente matematico, nunca
        // visible.
        public static Vector3 WorldToScreenPointStable(Camera cam, Vector3 worldPos)
        {
            var feedback = cam.GetComponent<CombatFeedback>();
            if (feedback == null || !feedback.IsShaking)
                return cam.WorldToScreenPoint(worldPos);

            Vector3 shakenPos = cam.transform.position;
            cam.transform.position = feedback.RestWorldPosition;
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            cam.transform.position = shakenPos;
            return screenPos;
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
