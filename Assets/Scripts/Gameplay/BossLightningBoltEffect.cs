using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    // Relampago corto de la sala de jefe. La ruta quebrada y sus ramificaciones se guardan como
    // puntos del mundo; cada segmento se convierte en una cinta que mira a la camara para que el
    // nucleo del shader siempre quede visible desde el angulo de juego.
    public sealed class BossLightningBoltEffect : MonoBehaviour
    {
        private const float Lifetime = 0.28f;
        private const float BoltHeight = 2.85f;
        private const string MaterialResource = "Materials/BossLightning";

        private struct RibbonPath
        {
            public Vector3[] Points;
            public float HalfWidth;
        }

        private static Material _sharedMaterial;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");

        private readonly List<RibbonPath> _paths = new List<RibbonPath>(5);
        private readonly List<Vector3> _vertices = new List<Vector3>(256);
        private readonly List<Vector2> _uvs = new List<Vector2>(256);
        private readonly List<int> _triangles = new List<int>(384);

        private Mesh _mesh;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _properties;
        private Color _color;
        private float _age;
        private float _seed;

        public static void Spawn(Vector3 impactPoint, Color color)
        {
            Material material = GetMaterial();
            if (material == null) return;

            var go = new GameObject("BossLightningBolt");
            go.AddComponent<BossLightningBoltEffect>().Initialize(impactPoint, color, material);
        }

        private static Material GetMaterial()
        {
            if (_sharedMaterial != null) return _sharedMaterial;

            _sharedMaterial = Resources.Load<Material>(MaterialResource);
            if (_sharedMaterial != null) return _sharedMaterial;

            // Fallback para escenas de Editor mientras Unity termina de importar Resources.
            Shader shader = Shader.Find("Custom/BossLightning");
            if (shader != null) _sharedMaterial = new Material(shader);
            return _sharedMaterial;
        }

        private void Initialize(Vector3 impactPoint, Color color, Material material)
        {
            _color = color;
            _seed = Random.Range(0f, 1000f);

            Vector3 bottom = impactPoint;
            bottom.y = Mathf.Max(0.06f, bottom.y);
            Vector3 top = bottom + Vector3.up * BoltHeight;
            Vector3[] mainPoints = CreateJaggedPath(top, bottom, segments: 11, jitter: 0.42f);
            _paths.Add(new RibbonPath { Points = mainPoints, HalfWidth = 0.075f });

            int branchCount = Random.Range(2, 5);
            for (int i = 0; i < branchCount; i++)
            {
                int startIndex = Random.Range(2, mainPoints.Length - 2);
                Vector3 branchStart = mainPoints[startIndex];
                Vector2 direction = Random.insideUnitCircle;
                if (direction.sqrMagnitude < 0.001f) direction = Vector2.right;
                direction.Normalize();

                float length = Random.Range(0.65f, 1.25f);
                Vector3 branchEnd = branchStart + new Vector3(direction.x * length, -Random.Range(0.35f, 0.95f), direction.y * length);
                branchEnd.y = Mathf.Max(bottom.y + 0.15f, branchEnd.y);
                Vector3[] branchPoints = CreateJaggedPath(branchStart, branchEnd, Random.Range(2, 5), 0.17f);
                _paths.Add(new RibbonPath { Points = branchPoints, HalfWidth = Random.Range(0.025f, 0.04f) });
            }

            _mesh = new Mesh { name = "BossLightningBoltMesh" };
            _mesh.MarkDynamic();
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _properties = new MaterialPropertyBlock();

            UpdateMesh();
            UpdateMaterialProperties(0f);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            UpdateMaterialProperties(Mathf.Clamp01(_age / Lifetime));
            UpdateMesh();

            if (_age >= Lifetime) Destroy(gameObject);
        }

        private void UpdateMaterialProperties(float progress)
        {
            if (_renderer == null) return;
            _properties.SetColor(ColorId, _color);
            _properties.SetFloat(ProgressId, progress);
            _properties.SetFloat(SeedId, _seed);
            _renderer.SetPropertyBlock(_properties);
        }

        private void UpdateMesh()
        {
            if (_mesh == null) return;
            Camera camera = Camera.main;
            if (camera == null) return;

            _vertices.Clear();
            _uvs.Clear();
            _triangles.Clear();

            foreach (var path in _paths)
            {
                Vector3[] points = path.Points;
                int segmentCount = points.Length - 1;
                for (int i = 0; i < segmentCount; i++)
                {
                    Vector3 start = points[i];
                    Vector3 end = points[i + 1];
                    Vector3 segment = end - start;
                    float length = segment.magnitude;
                    if (length < 0.001f) continue;

                    Vector3 direction = segment / length;
                    Vector3 midpoint = (start + end) * 0.5f;
                    Vector3 toCamera = camera.transform.position - midpoint;
                    Vector3 side = Vector3.Cross(direction, toCamera).normalized;
                    if (side.sqrMagnitude < 0.001f) side = camera.transform.right;

                    float halfWidth = path.HalfWidth;
                    // Un poco de solape oculta huecos entre las cintas cuando el rayo hace un giro.
                    start -= direction * halfWidth * 0.45f;
                    end += direction * halfWidth * 0.45f;
                    Vector3 offset = side * halfWidth;

                    int first = _vertices.Count;
                    _vertices.Add(transform.InverseTransformPoint(start - offset));
                    _vertices.Add(transform.InverseTransformPoint(start + offset));
                    _vertices.Add(transform.InverseTransformPoint(end + offset));
                    _vertices.Add(transform.InverseTransformPoint(end - offset));

                    float u0 = i / (float)segmentCount;
                    float u1 = (i + 1) / (float)segmentCount;
                    _uvs.Add(new Vector2(0f, u0));
                    _uvs.Add(new Vector2(1f, u0));
                    _uvs.Add(new Vector2(1f, u1));
                    _uvs.Add(new Vector2(0f, u1));

                    _triangles.Add(first);
                    _triangles.Add(first + 1);
                    _triangles.Add(first + 2);
                    _triangles.Add(first);
                    _triangles.Add(first + 2);
                    _triangles.Add(first + 3);
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
        }

        private static Vector3[] CreateJaggedPath(Vector3 start, Vector3 end, int segments, float jitter)
        {
            var points = new Vector3[segments + 1];
            points[0] = start;
            points[segments] = end;

            for (int i = 1; i < segments; i++)
            {
                float t = i / (float)segments;
                Vector3 point = Vector3.Lerp(start, end, t);
                float envelope = Mathf.Sin(t * Mathf.PI);
                point.x += Random.Range(-jitter, jitter) * envelope;
                point.z += Random.Range(-jitter, jitter) * envelope;
                points[i] = point;
            }

            return points;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
