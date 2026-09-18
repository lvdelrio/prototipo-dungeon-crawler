using UnityEngine;
using Combat;

namespace Gameplay
{
    // Rafaga de particulas reales (ParticleSystem, no shader ni sprite) por elemento, disparada en
    // CUALQUIER golpe -- basico o de habilidad -- para reforzar visualmente que elemento fue: sube
    // como llamas si es Fuego, cae como esquirlas si es Hielo, chispas rapidas en Rayo, lineas
    // anchas de corte en Slash, polvo de impacto en Strike, un cono fino y veloz en Perforacion.
    // Complementa (no reemplaza) al anillo de shader de ElementalBurstEffect y al sprite de
    // HitImpactEffect (solo en habilidades).
    public class ElementalParticleEffect : MonoBehaviour
    {
        public static void Spawn(Vector3 worldPosition, Element element)
        {
            var go = new GameObject($"ElementalParticles_{element}");
            go.transform.position = worldPosition;

            var ps = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = ParticleTextureFactory.SharedMaterial;

            ConfigureForElement(ps, element);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            ps.Play();

            float life = main.startLifetime.constantMax;
            Destroy(go, life + 0.5f);
        }

        private static void ConfigureForElement(ParticleSystem ps, Element element)
        {
            var main = ps.main;
            var emission = ps.emission;
            var shape = ps.shape;

            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18, 26) });
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;

            switch (element)
            {
                case Element.Fire:
                    main.startColor = new Color(1f, 0.4f, 0.1f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
                    main.gravityModifier = -0.4f; // sube, como llamas
                    break;

                case Element.Ice:
                    main.startColor = new Color(0.6f, 0.9f, 1f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
                    main.gravityModifier = 0.6f; // cae, como esquirlas
                    break;

                case Element.Volt:
                    main.startColor = new Color(1f, 0.95f, 0.3f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.28f);
                    main.gravityModifier = 0f;
                    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 26, 34) }); // muchas chispas rapidas
                    break;

                case Element.Slash:
                    main.startColor = new Color(0.9f, 0.92f, 1f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.22f);
                    main.gravityModifier = 0f;
                    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6, 10) }); // pocas y grandes, como lineas de corte
                    break;

                case Element.Strike:
                    main.startColor = new Color(0.75f, 0.6f, 0.4f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.45f);
                    main.gravityModifier = 0.4f; // polvo que cae
                    break;

                case Element.Pierce:
                    main.startColor = new Color(0.55f, 0.95f, 0.55f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 3.6f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
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
