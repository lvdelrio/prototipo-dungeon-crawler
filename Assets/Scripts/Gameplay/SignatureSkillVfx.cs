using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Combat;

namespace Gameplay
{
    /// <summary>Prototipos cinematograficos ligeros para las habilidades del Warrior y el Mage.</summary>
    public sealed class SignatureSkillVfx : MonoBehaviour
    {
        private static Material _iceMaterial;
        private static Material _iceTrailMaterial;
        private static Material _slashMaterial;
        private static Mesh _iceCrystalMesh;

        public static void PlayMageCast(Transform owner, Vector3 source, Vector3 target, Vector3 ground, float intensity)
        {
            var root = CreateRoot(owner, "Mage_IceLanceSequence");
            root.StartCoroutine(root.IceLanceRoutine(source, target, ground, intensity));
        }

        public static void PlayWarriorCharge(Transform owner, Vector3 target, Camera camera, float intensity)
        {
            var root = CreateRoot(owner, "Warrior_PowerSlashTell");
            root.StartCoroutine(root.WarriorChargeRoutine(target, camera, intensity));
        }

        public static void PlayMageImpact(Transform owner, Vector3 target, Camera camera, float intensity)
        {
            var root = CreateRoot(owner, "Mage_IceLanceImpact");
            root.StartCoroutine(root.IceImpactRoutine(target, camera, intensity));
        }

        public static void PlayWarriorImpact(Transform owner, Vector3 target, Camera camera, float intensity)
        {
            var root = CreateRoot(owner, "Warrior_PowerSlashImpact");
            root.StartCoroutine(root.WarriorImpactRoutine(target, camera, intensity));
        }

        private static SignatureSkillVfx CreateRoot(Transform owner, string name)
        {
            var go = new GameObject(name);
            if (owner != null) go.transform.SetParent(owner, false);
            return go.AddComponent<SignatureSkillVfx>();
        }

        private IEnumerator IceLanceRoutine(Vector3 source, Vector3 target, Vector3 ground, float intensity)
        {
            var spear = CreateCrystal("IceLanceProjectile", transform, IceMaterial());
            spear.transform.position = source;
            spear.transform.localScale = new Vector3(1f, 1f, 1f);

            var trail = spear.AddComponent<TrailRenderer>();
            trail.time = 0.25f;
            trail.minVertexDistance = 0.04f;
            trail.numCornerVertices = 2;
            trail.numCapVertices = 2;
            trail.startWidth = Mathf.Lerp(0.28f, 0.42f, Mathf.InverseLerp(1f, 2f, intensity));
            trail.endWidth = 0.015f;
            trail.sharedMaterial = IceTrailMaterial();
            trail.startColor = new Color(0.72f, 0.95f, 1f, 0.95f);
            trail.endColor = new Color(0.25f, 0.75f, 1f, 0f);
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;

            var sigil = CreateLine(transform, "IceTargetSigil", IceTrailMaterial(), 0.065f);
            const int points = 32;
            sigil.positionCount = points + 1;
            sigil.useWorldSpace = true;
            float duration = 0.28f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);
                Vector3 direction = (target - source).normalized;
                Vector3 arc = Vector3.up * Mathf.Sin(p * Mathf.PI) * 0.28f;
                spear.transform.position = Vector3.Lerp(source, target, p) + arc;
                if (direction.sqrMagnitude > 0.001f)
                    spear.transform.rotation = Quaternion.FromToRotation(Vector3.up, direction);
                float radius = Mathf.Lerp(0.08f, Mathf.Lerp(0.72f, 0.95f, Mathf.InverseLerp(1f, 2f, intensity)), p);
                for (int i = 0; i <= points; i++)
                {
                    float a = i * Mathf.PI * 2f / points;
                    sigil.SetPosition(i, ground + new Vector3(Mathf.Cos(a) * radius, 0.045f, Mathf.Sin(a) * radius));
                }
                sigil.startColor = new Color(0.55f, 0.9f, 1f, 1f - p * 0.6f);
                sigil.endColor = sigil.startColor;
                yield return null;
            }

            Destroy(sigil.gameObject);
            spear.GetComponent<Renderer>().enabled = false;
            yield return new WaitForSeconds(0.3f); // deja que el rastro se disipe tras el impacto.
            Destroy(spear);
            Destroy(gameObject);
        }

        private IEnumerator IceImpactRoutine(Vector3 center, Camera camera, float intensity)
        {
            int count = Mathf.RoundToInt(Mathf.Lerp(7f, 11f, Mathf.InverseLerp(1f, 2f, intensity)));
            var shards = new Transform[count];
            var directions = new Vector3[count];
            var lengths = new float[count];
            Vector3 right = camera != null ? camera.transform.right : Vector3.right;
            Vector3 up = camera != null ? camera.transform.up : Vector3.up;
            Vector3 towardCamera = camera != null ? -camera.transform.forward * 0.2f : -Vector3.forward * 0.2f;
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count + Random.Range(-0.18f, 0.18f);
                directions[i] = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle) + towardCamera).normalized;
                lengths[i] = Random.Range(0.65f, 1.45f) * Mathf.Lerp(1f, 1.3f, Mathf.InverseLerp(1f, 2f, intensity));
                var shard = CreateCrystal("IceImpactShard", transform, IceMaterial());
                shard.transform.position = center;
                shard.transform.localScale = Vector3.one * Random.Range(0.58f, 0.9f);
                shard.transform.rotation = Quaternion.FromToRotation(Vector3.up, directions[i]);
                shards[i] = shard.transform;
            }

            float elapsed = 0f;
            const float duration = 0.32f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float p = Mathf.Clamp01(elapsed / duration);
                for (int i = 0; i < count; i++)
                {
                    if (shards[i] == null) continue;
                    shards[i].position = center + directions[i] * (lengths[i] * p);
                    float scale = 1f - p * 0.82f;
                    shards[i].localScale = Vector3.one * Mathf.Lerp(0.8f, 0.16f, p);
                }
                yield return null;
            }
            Destroy(gameObject);
        }

        private IEnumerator WarriorChargeRoutine(Vector3 target, Camera camera, float intensity)
        {
            Vector3 right = camera != null ? camera.transform.right : Vector3.right;
            Vector3 up = camera != null ? camera.transform.up : Vector3.up;
            Vector3 forward = camera != null ? camera.transform.forward : Vector3.forward;
            var line = CreateLine(transform, "WarriorChargeArc", SlashMaterial(), 0.42f);
            line.useWorldSpace = true;
            line.positionCount = 9;
            float elapsed = 0f;
            const float duration = 0.28f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float p = Mathf.Clamp01(elapsed / duration);
                float span = Mathf.Lerp(0.2f, 2.1f * Mathf.Lerp(1f, 1.2f, Mathf.InverseLerp(1f, 2f, intensity)), p);
                for (int i = 0; i < line.positionCount; i++)
                {
                    float u = i / (float)(line.positionCount - 1);
                    line.SetPosition(i, target - forward * 0.12f + right * ((u - 0.5f) * span)
                        + up * (Mathf.Sin(u * Mathf.PI) * 0.24f + (u - 0.5f) * 0.48f));
                }
                line.startColor = new Color(1f, 0.94f, 0.72f, 0.95f * (1f - p * 0.4f));
                line.endColor = line.startColor;
                yield return null;
            }
            Destroy(line.gameObject);
            Destroy(gameObject);
        }

        private IEnumerator WarriorImpactRoutine(Vector3 center, Camera camera, float intensity)
        {
            Vector3 right = camera != null ? camera.transform.right : Vector3.right;
            Vector3 up = camera != null ? camera.transform.up : Vector3.up;
            Vector3 forward = camera != null ? camera.transform.forward : Vector3.forward;
            const float duration = 0.2f;
            var first = CreateLine(transform, "WarriorSlashPrimary", SlashMaterial(), 0.72f);
            var second = CreateLine(transform, "WarriorSlashCross", SlashMaterial(), 0.48f);
            first.useWorldSpace = second.useWorldSpace = true;
            first.positionCount = second.positionCount = 9;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float p = Mathf.Clamp01(elapsed / duration);
                UpdateSlash(first, center, right, up, forward, p, -0.66f, 1.9f * Mathf.Lerp(1f, 1.2f, Mathf.InverseLerp(1f, 2f, intensity)));
                float crossP = Mathf.Clamp01((p - 0.12f) / 0.88f);
                UpdateSlash(second, center, right, up, forward, crossP, 0.7f, 1.55f * Mathf.Lerp(1f, 1.2f, Mathf.InverseLerp(1f, 2f, intensity)));
                Color color = Color.Lerp(new Color(1f, 1f, 0.94f, 1f), new Color(1f, 0.63f, 0.2f, 0f), p);
                first.startColor = first.endColor = color;
                second.startColor = second.endColor = color;
                yield return null;
            }
            Destroy(gameObject);
        }

        private static void UpdateSlash(LineRenderer line, Vector3 center, Vector3 right, Vector3 up,
            Vector3 forward, float progress, float angle, float maxLength)
        {
            Vector3 axis = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)).normalized;
            Vector3 bend = (-right * Mathf.Sin(angle) + up * Mathf.Cos(angle)).normalized;
            float length = Mathf.Lerp(0.18f, maxLength, progress);
            for (int i = 0; i < line.positionCount; i++)
            {
                float u = i / (float)(line.positionCount - 1);
                line.SetPosition(i, center - forward * 0.15f + axis * ((u - 0.5f) * length * 2f)
                    + bend * (Mathf.Sin(u * Mathf.PI) * 0.13f));
            }
        }

        private static LineRenderer CreateLine(Transform parent, string name, Material material, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.widthMultiplier = width;
            line.numCapVertices = 3;
            line.numCornerVertices = 3;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startColor = line.endColor = Color.white;
            return line;
        }

        private static Material IceMaterial()
        {
            if (_iceMaterial != null) return _iceMaterial;
            var shader = Shader.Find("Standard");
            _iceMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default"))
            {
                color = new Color(0.55f, 0.92f, 1f),
                enableInstancing = true,
            };
            if (_iceMaterial.HasProperty("_EmissionColor"))
            {
                _iceMaterial.EnableKeyword("_EMISSION");
                _iceMaterial.SetColor("_EmissionColor", new Color(0.18f, 0.75f, 1f) * 1.8f);
            }
            return _iceMaterial;
        }

        private static Material IceTrailMaterial()
        {
            if (_iceTrailMaterial != null) return _iceTrailMaterial;
            var shader = Shader.Find("Particles/Additive");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _iceTrailMaterial = new Material(shader) { color = Color.white };
            return _iceTrailMaterial;
        }

        private static Material SlashMaterial()
        {
            if (_slashMaterial != null) return _slashMaterial;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _slashMaterial = new Material(shader) { color = Color.white };
            return _slashMaterial;
        }

        private static GameObject CreateCrystal(string name, Transform parent, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.AddComponent<MeshFilter>().sharedMesh = IceCrystalMesh();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        private static Mesh IceCrystalMesh()
        {
            if (_iceCrystalMesh != null) return _iceCrystalMesh;
            const int sides = 6;
            var vertices = new List<Vector3>(sides * 2 + 1);
            for (int ring = 0; ring < 2; ring++)
            {
                float radius = ring == 0 ? 0.12f : 0.085f;
                float height = ring == 0 ? -0.62f : 0.14f;
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * Mathf.PI * 2f / sides;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius));
                }
            }
            vertices.Add(new Vector3(0f, 0.78f, 0f));
            int tip = vertices.Count - 1;
            var triangles = new List<int>(sides * 9);
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                int low = i;
                int lowNext = next;
                int high = sides + i;
                int highNext = sides + next;
                triangles.Add(low); triangles.Add(high); triangles.Add(lowNext);
                triangles.Add(lowNext); triangles.Add(high); triangles.Add(highNext);
                triangles.Add(high); triangles.Add(tip); triangles.Add(highNext);
            }
            _iceCrystalMesh = new Mesh { name = "LowPolyIceCrystal" };
            _iceCrystalMesh.SetVertices(vertices);
            _iceCrystalMesh.SetTriangles(triangles, 0);
            _iceCrystalMesh.RecalculateNormals();
            _iceCrystalMesh.RecalculateBounds();
            return _iceCrystalMesh;
        }

    }
}
