using System.Collections;
using UnityEngine;

namespace Gameplay
{
    // PRUEBA: spawner del spark anguloso (Custom/ImpactSpark, ver Assets/Shaders). Mismo patron
    // que los demas efectos de impacto (quad orientado a camara, anima _Progress via
    // MaterialPropertyBlock). slashMode=true = cruz de 4 puntas (golpe basico), slashMode=false =
    // estallido de 6 puntas asimetrico (magico/Fuego). Se dispara junto a ImpactShockwaveEffect,
    // como acento encima del shockwave.
    public class ImpactSparkEffect : MonoBehaviour
    {
        private const float BaseDuration = 0.2f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int RotationId = Shader.PropertyToID("_Rotation");

        public static void Spawn(Material template, Vector3 worldPosition, Color color, Quaternion facing, bool slashMode, float intensity = 1f)
        {
            if (template == null) return;

            float t = Mathf.InverseLerp(1f, 2.5f, Mathf.Max(1f, intensity));

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = slashMode ? "ImpactSparkEffect_Slash" : "ImpactSparkEffect_Magic";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.position = worldPosition;
            go.transform.rotation = facing;
            float scale = Random.Range(1.5f, 1.8f) * Mathf.Lerp(1f, 1.4f, t);
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = template;

            var props = new MaterialPropertyBlock();
            props.SetColor(ColorId, color * Mathf.Lerp(1f, 1.3f, t));
            props.SetFloat(ProgressId, 0f);
            props.SetFloat(ModeId, slashMode ? 1f : 0f);
            props.SetFloat(RotationId, Random.Range(0f, Mathf.PI * 2f));
            renderer.SetPropertyBlock(props);

            var effect = go.AddComponent<ImpactSparkEffect>();
            float duration = Mathf.Lerp(BaseDuration, BaseDuration * 1.4f, t);
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
