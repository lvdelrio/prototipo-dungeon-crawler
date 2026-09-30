using UnityEngine;

namespace Gameplay
{
    /// <summary>Low-cost warm pulse from the castle's lavafall.</summary>
    public sealed class LavaGlowFlicker : MonoBehaviour
    {
        private Light _light;
        private float _baseIntensity;
        private float _phase;

        public void Initialize(Light source)
        {
            _light = source;
            _baseIntensity = source != null ? source.intensity : 0f;
            _phase = Random.value * 10f;
        }

        private void Update()
        {
            if (_light == null) return;
            float t = Time.time * 5.7f + _phase;
            _light.intensity = _baseIntensity * (0.9f + Mathf.Sin(t) * 0.055f + Mathf.Sin(t * 2.1f) * 0.035f);
        }
    }
}
