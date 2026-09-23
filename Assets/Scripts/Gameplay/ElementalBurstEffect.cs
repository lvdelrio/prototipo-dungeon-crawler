using System.Collections;
using UnityEngine;

namespace Gameplay
{
    // Efecto de impacto puramente por shader (Custom/ElementalBurst, ver Assets/Shaders): un
    // quad orientado a camara sobre el enemigo golpeado, que anima _Progress de 0 a 1 (anillo que
    // crece y se desvanece) con el color del elemento de la habilidad o ataque. Complementa al
    // sprite de HitImpactEffect (que solo aparece en golpes de habilidad); este dispara en
    // CUALQUIER golpe -- basico o de habilidad -- para que se note el elemento siempre.
    public class ElementalBurstEffect : MonoBehaviour
    {
        private const float BaseDuration = 0.35f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");

        // intensity: ver ImpactBurstEffect.Spawn -- mismo criterio (1 = basico, hasta ~2.5+ para
        // habilidades fuertes / el Ataque en Conjunto), agranda el anillo y lo hace durar un poco mas.
        public static void Spawn(Material template, Vector3 worldPosition, Color color, Quaternion facing, float intensity = 1f)
        {
            if (template == null) return;

            float t = Mathf.InverseLerp(1f, 2.5f, Mathf.Max(1f, intensity));

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ElementalBurstEffect";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.position = worldPosition;
            go.transform.rotation = facing;
            float scale = Random.Range(1.6f, 2.1f) * Mathf.Lerp(1f, 1.5f, t);
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = template;

            var props = new MaterialPropertyBlock();
            props.SetColor(ColorId, color);
            props.SetFloat(ProgressId, 0f);
            renderer.SetPropertyBlock(props);

            var effect = go.AddComponent<ElementalBurstEffect>();
            float duration = Mathf.Lerp(BaseDuration, BaseDuration * 1.3f, t);
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
