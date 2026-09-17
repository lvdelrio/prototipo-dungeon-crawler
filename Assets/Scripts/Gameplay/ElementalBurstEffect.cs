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
        private const float Duration = 0.35f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");

        public static void Spawn(Material template, Vector3 worldPosition, Color color, Quaternion facing)
        {
            if (template == null) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ElementalBurstEffect";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.position = worldPosition;
            go.transform.rotation = facing;
            float scale = Random.Range(1.6f, 2.1f);
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = template;

            var props = new MaterialPropertyBlock();
            props.SetColor(ColorId, color);
            props.SetFloat(ProgressId, 0f);
            renderer.SetPropertyBlock(props);

            var effect = go.AddComponent<ElementalBurstEffect>();
            effect.StartCoroutine(effect.Animate(renderer, props));
        }

        private IEnumerator Animate(Renderer renderer, MaterialPropertyBlock props)
        {
            float t = 0f;
            while (t < Duration)
            {
                t += Time.deltaTime;
                props.SetFloat(ProgressId, Mathf.Clamp01(t / Duration));
                renderer.SetPropertyBlock(props);
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
