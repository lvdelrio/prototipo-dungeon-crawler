using System.Collections;
using UnityEngine;

namespace Gameplay
{
    // Efecto de impacto puramente por shader (Custom/ImpactBurst, ver Assets/Shaders), estilo
    // "hit spark" de juego de pelea (Tekken 8 y similares): un quad orientado a camara sobre el
    // enemigo golpeado, con un flash blanco muy breve en el centro y puntas radiales que se
    // disparan hacia afuera y se afinan rapido. Se dispara en CUALQUIER golpe -- basico o de
    // habilidad -- ADEMAS del anillo mas suave de ElementalBurstEffect: ese anillo comunica el
    // elemento del golpe, este le da la sensacion de peso/impacto seco al golpe en si.
    public class ImpactBurstEffect : MonoBehaviour
    {
        private const float BaseDuration = 0.18f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int SpikeCountId = Shader.PropertyToID("_SpikeCount");

        // intensity: 1 = ataque basico. Las habilidades pasan su SkillPower (tipicamente 1.4-2),
        // y el Ataque en Conjunto un valor todavia mas alto -- cuanto mas fuerte el golpe, mas
        // lejos saltan las chispas (quad mas grande, el shader ya extiende las puntas casi hasta
        // el borde) y mas densas/brillantes se ven, ademas de durar un toque mas.
        public static void Spawn(Material template, Vector3 worldPosition, Color color, Quaternion facing, float intensity = 1f)
        {
            if (template == null) return;

            float t = Mathf.InverseLerp(1f, 2.5f, Mathf.Max(1f, intensity));

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ImpactBurstEffect";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.position = worldPosition;
            // Rotacion aleatoria (en el eje que mira a camara) ademas del facing: que la estrella
            // de impacto no salga siempre orientada igual, como en los juegos de pelea reales.
            go.transform.rotation = facing * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float scale = Random.Range(1.3f, 1.7f) * Mathf.Lerp(1f, 1.9f, t);
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = template;

            var props = new MaterialPropertyBlock();
            // Mas brillo (RGB por encima de 1, el shader usa Blend SrcAlpha One asi que satura
            // hacia blanco) y mas puntas para un golpe grande, en vez de solo agrandar el mismo
            // patron -- se siente mas intenso, no solo mas grande.
            props.SetColor(ColorId, color * Mathf.Lerp(1f, 1.6f, t));
            props.SetFloat(ProgressId, 0f);
            props.SetFloat(SpikeCountId, Mathf.Lerp(9f, 15f, t));
            renderer.SetPropertyBlock(props);

            var effect = go.AddComponent<ImpactBurstEffect>();
            float duration = Mathf.Lerp(BaseDuration, BaseDuration * 1.6f, t);
            effect.StartCoroutine(effect.Animate(renderer, props, duration));
        }

        private IEnumerator Animate(Renderer renderer, MaterialPropertyBlock props, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                props.SetFloat(ProgressId, Mathf.Clamp01(t / duration));
                renderer.SetPropertyBlock(props);
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
