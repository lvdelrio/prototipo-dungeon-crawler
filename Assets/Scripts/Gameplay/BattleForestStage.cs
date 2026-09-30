using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gameplay
{
    // Fondo exclusivo de los combates en bosque. Reutiliza el arte de las paredes de la mazmorra,
    // pero lo separa en profundidad y deja una lámina de agua entre ambas capas.
    public sealed class BattleForestStage : MonoBehaviour
    {
        private Transform _camera;
        private Vector3 _cameraStart;
        private Transform _far;
        private Transform _near;
        private Vector3 _farBase;
        private Vector3 _nearBase;

        public static void Spawn(Scene scene, Camera camera, DungeonLevelBuilder source)
        {
            if (!scene.IsValid() || camera == null || source == null) return;
            var go = new GameObject("BattleForestStage");
            SceneManager.MoveGameObjectToScene(go, scene);
            var stage = go.AddComponent<BattleForestStage>();
            stage.Build(camera, source);
        }

        private void Build(Camera camera, DungeonLevelBuilder source)
        {
            _camera = camera.transform;
            _cameraStart = _camera.position;
            var backdrop = GameObject.Find("BattleBackdrop");
            if (backdrop != null) backdrop.SetActive(false);

            _far = Layer("ForestCombatFar", source.forestLayerFarMaterial, 31f, 66f, 37f, 205.6f);
            _near = Layer("ForestCombatNear", source.forestLayerNearMaterial, 21f, 58f, 33f, 205.1f);
            if (_far != null) _farBase = _far.localPosition;
            if (_near != null) _nearBase = _near.localPosition;

            if (source.voidBlockMaterial != null)
            {
                var water = GameObject.CreatePrimitive(PrimitiveType.Quad);
                water.name = "ForestCombatWater";
                water.transform.SetParent(transform, false);
                water.transform.position = new Vector3(0f, 201.8f, 13f);
                water.transform.localScale = new Vector3(48f, 5.6f, 1f);
                var collider = water.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                water.GetComponent<MeshFilter>().sharedMesh = BuildGrid(32, 8);
                var renderer = water.GetComponent<Renderer>();
                renderer.sharedMaterial = new Material(source.voidBlockMaterial);
                renderer.sharedMaterial.renderQueue = 2990;
            }
            camera.depthTextureMode |= DepthTextureMode.Depth;
        }

        private Transform Layer(string name, Material material, float z, float width, float height, float y)
        {
            if (material == null) return null;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, y, z);
            go.transform.localScale = new Vector3(width, height, 1f);
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        private void LateUpdate()
        {
            if (_camera == null) return;
            Vector3 delta = _camera.position - _cameraStart;
            if (_far != null) _far.localPosition = _farBase + new Vector3(delta.x * 0.12f, delta.y * 0.08f, 0f);
            if (_near != null) _near.localPosition = _nearBase + new Vector3(delta.x * 0.28f, delta.y * 0.18f, 0f);
        }

        private static Mesh BuildGrid(int xDivisions, int yDivisions)
        {
            var vertices = new Vector3[(xDivisions + 1) * (yDivisions + 1)];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[xDivisions * yDivisions * 6];
            for (int y = 0; y <= yDivisions; y++)
            for (int x = 0; x <= xDivisions; x++)
            {
                int i = y * (xDivisions + 1) + x;
                float u = x / (float)xDivisions, v = y / (float)yDivisions;
                vertices[i] = new Vector3(u - 0.5f, v - 0.5f, 0f);
                uv[i] = new Vector2(u, v);
            }
            int t = 0;
            for (int y = 0; y < yDivisions; y++)
            for (int x = 0; x < xDivisions; x++)
            {
                int i = y * (xDivisions + 1) + x;
                triangles[t++] = i; triangles[t++] = i + xDivisions + 1; triangles[t++] = i + 1;
                triangles[t++] = i + 1; triangles[t++] = i + xDivisions + 1; triangles[t++] = i + xDivisions + 2;
            }
            var mesh = new Mesh { name = "BattleWaterGrid" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
