using System.Collections;
using UnityEngine;

namespace Gameplay
{
    // PRUEBA: spawner del shockwave por distorsion (Custom/ImpactShockwave, ver Assets/Shaders).
    // Mismo patron que ElementalBurstEffect/ImpactBurstEffect (quad orientado a camara, anima
    // _Progress via MaterialPropertyBlock). Tiene dos modos (ver el shader): slashMode=true es el
    // tajo del golpe basico (corte recto, nitido, casi instantaneo, dura poco), slashMode=false es
    // el anillo turbulento de Fuego (un poco mas lento). Se combina con ImpactSparkEffect (silueta
    // angular) para el golpe final.
    public class ImpactShockwaveEffect : MonoBehaviour
    {
        private const float SlashDuration = 0.28f;
        private const float RadialDuration = 0.42f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int SlashDirId = Shader.PropertyToID("_SlashDir");

        public static void Spawn(Material template, Vector3 worldPosition, Color color, Quaternion facing, bool slashMode, float intensity = 1f)
        {
            if (template == null) return;

            float t = Mathf.InverseLerp(1f, 2.5f, Mathf.Max(1f, intensity));

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = slashMode ? "ImpactShockwaveEffect_Slash" : "ImpactShockwaveEffect_Radial";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.position = worldPosition;
            go.transform.rotation = facing;
            float baseScale = slashMode ? 2.6f : 2.1f;
            float scale = Random.Range(baseScale - 0.15f, baseScale + 0.15f) * Mathf.Lerp(1f, 1.4f, t);
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = template;

            var props = new MaterialPropertyBlock();
            props.SetColor(ColorId, color);
            props.SetFloat(ProgressId, 0f);
            props.SetFloat(ModeId, slashMode ? 1f : 0f);
            if (slashMode) props.SetFloat(SlashDirId, Random.Range(0f, Mathf.PI * 2f));
            renderer.SetPropertyBlock(props);

            var effect = go.AddComponent<ImpactShockwaveEffect>();
            float baseDuration = slashMode ? SlashDuration : RadialDuration;
            float duration = Mathf.Lerp(baseDuration, baseDuration * 1.3f, t);
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
