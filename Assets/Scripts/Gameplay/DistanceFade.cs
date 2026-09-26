using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    // Atenua un grupo de renderers a transparente cuando la camara esta MUY cerca (pedido
    // puntual: la escalera se veia rara cuando el jugador estaba parado encima o pegado a ella,
    // tapando toda la pantalla). Cada renderer registrado necesita su propio material ya
    // configurado para alpha blending (ver DungeonLevelBuilder.ConfigureForAlphaFade) -- esto solo
    // ajusta el canal alfa del color via MaterialPropertyBlock, no toca el modo de blend.
    public class DistanceFade : MonoBehaviour
    {
        public float fullyVisibleDistance = 2.4f;
        public float invisibleDistance = 0.9f;

        private readonly List<(Renderer renderer, Color color)> _targets = new List<(Renderer, Color)>();
        private MaterialPropertyBlock _block;

        public void Register(Renderer renderer, Color color) => _targets.Add((renderer, color));

        void LateUpdate()
        {
            if (Camera.main == null || _targets.Count == 0) return;
            _block ??= new MaterialPropertyBlock();

            float dist = Vector3.Distance(Camera.main.transform.position, transform.position);
            float alpha = Mathf.Clamp01(Mathf.InverseLerp(invisibleDistance, fullyVisibleDistance, dist));

            foreach (var (renderer, color) in _targets)
            {
                if (renderer == null) continue;
                _block.SetColor("_Color", new Color(color.r, color.g, color.b, color.a * alpha));
                renderer.SetPropertyBlock(_block);
            }
        }
    }
}
