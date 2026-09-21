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
        private const float Duration = 0.18f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");

        public static void Spawn(Material template, Vector3 worldPosition, Color color, Quaternion facing)
        {
            if (template == null) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ImpactBurstEffect";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.position = worldPosition;
            // Rotacion aleatoria (en el eje que mira a camara) ademas del facing: que la estrella
            // de impacto no salga siempre orientada igual, como en los juegos de pelea reales.
            go.transform.rotation = facing * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float scale = Random.Range(1.3f, 1.7f);
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = template;

            var props = new MaterialPropertyBlock();
            props.SetColor(ColorId, color);
            props.SetFloat(ProgressId, 0f);
            renderer.SetPropertyBlock(props);

            var effect = go.AddComponent<ImpactBurstEffect>();
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
