using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    // Receives impacts from the falling-leaf particle system and feeds short radial waves
    // to the water shader through a per-surface MaterialPropertyBlock.
    [RequireComponent(typeof(Renderer), typeof(Collider))]
    public sealed class WaterRippleSurface : MonoBehaviour
    {
        private const int RippleCount = 4;
        private static readonly int[] RippleIds =
        {
            Shader.PropertyToID("_Ripple0"),
            Shader.PropertyToID("_Ripple1"),
            Shader.PropertyToID("_Ripple2"),
            Shader.PropertyToID("_Ripple3"),
        };

        // Shared impacts let each ring continue across the separate mesh tiles that form a pool.
        private static readonly Vector4[] SharedRipples = new Vector4[RippleCount];
        private static int _nextSharedRipple;
        private static int _sharedVersion;
        private static float _sharedLastRippleTime = -10f;

        private readonly List<ParticleCollisionEvent> _collisionEvents = new List<ParticleCollisionEvent>(8);
        private Renderer _renderer;
        private MaterialPropertyBlock _properties;
        private int _appliedVersion = -1;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _properties = new MaterialPropertyBlock();

            var camera = Camera.main;
            if (camera != null)
                camera.depthTextureMode |= DepthTextureMode.Depth;
        }

        private void OnParticleCollision(GameObject other)
        {
            var particleSystem = other != null ? other.GetComponent<ParticleSystem>() : null;
            if (particleSystem == null) return;

            _collisionEvents.Clear();
            int safeSize = ParticlePhysicsExtensions.GetSafeCollisionEventSize(particleSystem);
            if (_collisionEvents.Capacity < safeSize)
                _collisionEvents.Capacity = safeSize;
            int eventCount = ParticlePhysicsExtensions.GetCollisionEvents(particleSystem, gameObject, _collisionEvents);
            for (int i = 0; i < eventCount; i++)
            {
                Vector3 point = _collisionEvents[i].intersection;
                float strength = Mathf.Clamp(_collisionEvents[i].velocity.magnitude / 1.5f, 0.35f, 1f);
                SharedRipples[_nextSharedRipple] = new Vector4(point.x, point.z, Time.time, strength);
                _nextSharedRipple = (_nextSharedRipple + 1) % RippleCount;
                _sharedLastRippleTime = Time.time;
                _sharedVersion++;
            }

            if (eventCount > 0)
                ApplyRippleProperties();
        }

        private void Update()
        {
            if (_appliedVersion != _sharedVersion || Time.time - _sharedLastRippleTime < 1.5f)
                ApplyRippleProperties();
        }

        private void ApplyRippleProperties()
        {
            _renderer.GetPropertyBlock(_properties);
            for (int i = 0; i < RippleCount; i++)
                _properties.SetVector(RippleIds[i], SharedRipples[i]);
            _renderer.SetPropertyBlock(_properties);
            _appliedVersion = _sharedVersion;
        }
    }
}
