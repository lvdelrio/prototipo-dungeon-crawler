using System.Collections;
using UnityEngine;

namespace Gameplay
{
    // Efecto de impacto 2D (sprite, no shader) que se muestra al conectar un golpe de habilidad:
    // reproduce como flipbook las 6 frames de HitImpact.png (arte del usuario, exportado de
    // hit_intento-0001.aseprite), con tinte segun el elemento de la habilidad y una variacion al
    // azar (rotacion, escala, espejo, offset) para que ningun golpe se vea exactamente igual.
    public class HitImpactEffect : MonoBehaviour
    {
        private const float FrameDuration = 0.06f;

        public static void Spawn(Sprite[] frames, Vector3 worldPosition, Color tint, Quaternion facing)
        {
            if (frames == null || frames.Length == 0 || frames[0] == null) return;

            var go = new GameObject("HitImpactEffect");

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = frames[0];
            sr.color = tint;
            sr.sortingOrder = 100;

            // Variacion al azar: nunca el mismo golpe se ve exactamente igual al anterior.
            float randomRoll = Random.Range(-20f, 20f);
            float randomScale = Random.Range(1.4f, 2.0f);
            bool flipX = Random.value < 0.5f;
            Vector2 randomOffset = Random.insideUnitCircle * 0.3f;

            go.transform.position = worldPosition + (Vector3)randomOffset;
            go.transform.rotation = facing * Quaternion.Euler(0f, 0f, randomRoll);
            go.transform.localScale = new Vector3(randomScale * (flipX ? -1f : 1f), randomScale, 1f);

            var effect = go.AddComponent<HitImpactEffect>();
            effect.StartCoroutine(effect.PlayAndDestroy(sr, frames));
        }

        private IEnumerator PlayAndDestroy(SpriteRenderer sr, Sprite[] frames)
        {
            foreach (var frame in frames)
            {
                if (frame != null) sr.sprite = frame;
                yield return new WaitForSeconds(FrameDuration);
            }
            Destroy(gameObject);
        }
    }
}
