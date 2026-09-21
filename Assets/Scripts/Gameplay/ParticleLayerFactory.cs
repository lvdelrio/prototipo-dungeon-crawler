using UnityEngine;

namespace Gameplay
{
    // Crea capas de particulas (hijos de un padre dado) de forma segura: el GameObject arranca
    // INACTIVO para que "Play On Awake" no dispare una reproduccion con la configuracion de
    // fabrica antes de que el llamador termine de configurar la capa (ver el fix de
    // ElementalParticleEffect/AmbientParticles para el detalle del bug que esto evita). El
    // llamador configura los modulos que necesite y despues llama a Activate().
    public static class ParticleLayerFactory
    {
        public static ParticleSystem CreateLayer(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.SetActive(false);

            var ps = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = ParticleTextureFactory.SharedMaterial;
            return ps;
        }

        public static void Activate(ParticleSystem ps)
        {
            ps.gameObject.SetActive(true);
            ps.Play();
        }
    }
}
