using UnityEngine;

namespace Gameplay
{
    /// <summary>Pequeño parpadeo visual/lumínico para las antorchas que enmarcan la entrada exterior.</summary>
    public sealed class TorchFlameFlicker : MonoBehaviour
    {
        private Transform _outerFlame;
        private Transform _innerFlame;
        private Light _light;
        private Vector3 _outerBaseScale;
        private Vector3 _innerBaseScale;
        private float _baseIntensity;
        private float _phase;

        public void Initialize(Transform outerFlame, Transform innerFlame, Light lightSource)
        {
            _outerFlame = outerFlame;
            _innerFlame = innerFlame;
            _light = lightSource;
            _outerBaseScale = outerFlame.localScale;
            _innerBaseScale = innerFlame.localScale;
            _baseIntensity = lightSource.intensity;
            _phase = Random.value * 10f;
        }

        private void Update()
        {
            float t = Time.time * 8f + _phase;
            float pulse = 0.94f + Mathf.Sin(t) * 0.045f + Mathf.Sin(t * 2.37f) * 0.025f;
            if (_outerFlame != null)
                _outerFlame.localScale = new Vector3(_outerBaseScale.x * pulse, _outerBaseScale.y * (2f - pulse), _outerBaseScale.z * pulse);
            if (_innerFlame != null)
                _innerFlame.localScale = new Vector3(_innerBaseScale.x * (2f - pulse), _innerBaseScale.y * pulse, _innerBaseScale.z * (2f - pulse));
            if (_light != null) _light.intensity = _baseIntensity * pulse;
        }
    }
}
