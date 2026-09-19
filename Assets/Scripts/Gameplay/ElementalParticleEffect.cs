using System.Collections;
using UnityEngine;
using Combat;

namespace Gameplay
{
    // Rafaga de particulas reales (ParticleSystem, no shader ni sprite) por elemento, disparada en
    // CUALQUIER golpe -- basico o de habilidad -- para reforzar visualmente que elemento fue: sube
    // como llamas si es Fuego, cae como esquirlas si es Hielo, chispas rapidas en Rayo, lineas
    // anchas de corte en Slash, polvo de impacto en Strike, un cono fino y veloz en Perforacion.
    // Ademas de las particulas "de color" tiene 2 capas mas para que el golpe se sienta con mas
    // peso (linea Tekken 8: luces que saltan, todo mas animado):
    //  - Una luz real (Light) que flashea fuerte en el punto de impacto e ilumina el entorno un
    //    instante, en vez de que todo el brillo sea solo textura sin afectar la escena.
    //  - Una rafaga de "chispas" blancas, chicas y muy rapidas, superpuesta a las particulas de
    //    color -- el destello nitido que salta del impacto, no solo el color del elemento.
    // Complementa (no reemplaza) al anillo de shader de ElementalBurstEffect y al sprite de
    // HitImpactEffect (solo en habilidades).
    public class ElementalParticleEffect : MonoBehaviour
    {
        public static void Spawn(Vector3 worldPosition, Element element)
        {
            var go = new GameObject($"ElementalParticles_{element}");
            // Arranca INACTIVO: si el GameObject ya estuviera activo, AddComponent<ParticleSystem>
            // dispara su OnEnable al toque y "Play On Awake" (true por defecto) lo hace empezar a
            // reproducirse YA MISMO con la configuracion de fabrica (sin la rafaga que se arma mas
            // abajo). Como esa rafaga esta programada justo en el instante 0, el sistema ya habia
            // pasado ese instante para cuando se la configuraba -- se perdia entera, sin ningun
            // error visible (el componente existia igual, solo que nunca emitia nada).
            go.SetActive(false);
            go.transform.position = worldPosition;

            var effect = go.AddComponent<ElementalParticleEffect>();

            var ps = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = ParticleTextureFactory.SharedMaterial;
            ConfigureForElement(ps, element);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;

            var flarePs = ParticleLayerFactory.CreateLayer(go.transform, "Flare");
            ConfigureFlare(flarePs);

            Color color = ElementVisuals.ColorFor(element);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = 4.5f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;

            go.SetActive(true);
            ps.Play();
            // El hijo "Flare" arranco inactivo (ver ParticleLayerFactory): activar solo el padre
            // no alcanza, SetActive(false) en un hijo queda pegado hasta que se lo reactiva a EL
            // explicitamente -- si no, Play() no hace nada porque el sistema nunca corre.
            ParticleLayerFactory.Activate(flarePs);

            float life = main.startLifetime.constantMax;
            effect.StartCoroutine(effect.FlashLight(light, Mathf.Max(0.25f, life)));
            Destroy(go, life + 0.5f);
        }

        // Sube de golpe a full brillo y se apaga rapido -- el "flash" de luz real del impacto, no
        // solo un color pintado en las particulas.
        private IEnumerator FlashLight(Light light, float duration)
        {
            const float peakIntensity = 7f;
            const float riseTime = 0.03f;

            float t = 0f;
            while (t < riseTime)
            {
                t += Time.deltaTime;
                light.intensity = Mathf.Lerp(0f, peakIntensity, t / riseTime);
                yield return null;
            }

            t = 0f;
            float fallTime = Mathf.Max(0.05f, duration - riseTime);
            while (t < fallTime)
            {
                t += Time.deltaTime;
                light.intensity = Mathf.Lerp(peakIntensity, 0f, t / fallTime);
                yield return null;
            }
            light.intensity = 0f;
        }

        // Chispas blancas, chicas y muy rapidas: el "destello" nitido del impacto, superpuesto al
        // color propio del elemento (que ponen las particulas principales de ConfigureForElement).
        private static void ConfigureFlare(ParticleSystem ps)
        {
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startColor = new Color(1f, 1f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.18f);
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 14) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;
        }

        private static void ConfigureForElement(ParticleSystem ps, Element element)
        {
            var main = ps.main;
            var emission = ps.emission;
            var shape = ps.shape;

            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24, 34) });
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;

            switch (element)
            {
                case Element.Fire:
                    main.startColor = new Color(1f, 0.45f, 0.1f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.28f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.55f);
                    main.gravityModifier = -0.45f; // sube, como llamas
                    break;

                case Element.Ice:
                    main.startColor = new Color(0.6f, 0.9f, 1f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, 2f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
                    main.gravityModifier = 0.6f; // cae, como esquirlas
                    break;

                case Element.Volt:
                    main.startColor = new Color(1f, 0.95f, 0.3f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(2.8f, 4.2f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.28f);
                    main.gravityModifier = 0f;
                    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 32, 42) }); // muchas chispas rapidas
                    break;

                case Element.Slash:
                    main.startColor = new Color(0.9f, 0.92f, 1f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 3f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.35f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.22f);
                    main.gravityModifier = 0f;
                    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 12) }); // pocas y grandes, como lineas de corte
                    break;

                case Element.Strike:
                    main.startColor = new Color(0.78f, 0.63f, 0.42f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 1.9f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.45f);
                    main.gravityModifier = 0.4f; // polvo que cae
                    break;

                case Element.Pierce:
                    main.startColor = new Color(0.55f, 0.95f, 0.55f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 4.3f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.25f);
                    main.gravityModifier = 0f;
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = 12f;
                    break;

                default:
                    main.startColor = Color.white;
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 1.6f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.14f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.4f);
                    main.gravityModifier = 0f;
                    break;
            }
        }
    }
}
