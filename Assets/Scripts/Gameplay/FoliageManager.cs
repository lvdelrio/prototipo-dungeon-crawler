using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gameplay
{
    // Motor de instancing para el pasto suelto (matas normales + "pasto espada" estilo Zelda, ver
    // GroundTileFactory.BuildGrassTufts/BuildSwordGrass) que antes se armaba como un GameObject+Quad
    // por brizna: un piso con muchas celdas de pasto llegaba a cientos de objetos vivos solo para
    // hierba, y TODOS se destruian y volvian a crear cada vez que DungeonLevelBuilder.Build() corria
    // de nuevo (abrir una puerta, perforar una pared, cambiar de piso). Este manager no crea ni un
    // solo GameObject por brizna: guarda cada instancia como una matriz + color y dibuja todo el
    // pasto de un mismo mesh+material en unos pocos Graphics.DrawMeshInstanced por frame.
    public class FoliageManager : MonoBehaviour
    {
        private static FoliageManager _instance;

        public static FoliageManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("FoliageManager");
                    _instance = go.AddComponent<FoliageManager>();
                }
                return _instance;
            }
        }

        // Limite duro de Graphics.DrawMeshInstanced (DX11/mayoria de plataformas): 1023 instancias
        // por llamada. Lotes mas grandes se parten en varios chunks del mismo mesh+material.
        private const int BatchSize = 1023;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int GrassInteractorId = Shader.PropertyToID("_GrassInteractor");
        private GridPlayerController _player;

        private class Chunk
        {
            public Matrix4x4[] Matrices;
            public MaterialPropertyBlock Mpb;
        }

        private class Batch
        {
            public Mesh Mesh;
            public Material Material;
            public readonly List<Matrix4x4> PendingMatrices = new List<Matrix4x4>();
            public readonly List<Color> PendingColors = new List<Color>();
            public List<Chunk> Chunks;
        }

        private readonly List<Batch> _batches = new List<Batch>();

        private static Mesh _grassTuftMesh;
        private static Mesh _swordGrassMesh;
        private static Mesh _flowerHeadMesh;
        private static Mesh _flowerCenterMesh;

        // Llamado desde DungeonLevelBuilder.Clear() (arranca cada Build()): descarta el pasto del
        // piso anterior. Los meshes base (_grassTuftMesh/_swordGrassMesh) NO se tocan -- son
        // geometria compartida reusada por todos los pisos, se generan una sola vez.
        public void ClearAll()
        {
            _batches.Clear();
        }

        // Llamado desde DungeonLevelBuilder.Build() una vez terminado de recorrer toda la grilla:
        // empaqueta las listas crudas en chunks de <=1023 con su propio MaterialPropertyBlock ya
        // armado (colores por instancia), para que Update() no arme nada nuevo cuadro a cuadro.
        public void FinalizeBatches()
        {
            foreach (var batch in _batches)
            {
                batch.Chunks = new List<Chunk>();
                int total = batch.PendingMatrices.Count;
                for (int start = 0; start < total; start += BatchSize)
                {
                    int len = Mathf.Min(BatchSize, total - start);
                    var matrices = new Matrix4x4[len];
                    var colors = new Vector4[len];
                    for (int i = 0; i < len; i++)
                    {
                        matrices[i] = batch.PendingMatrices[start + i];
                        colors[i] = batch.PendingColors[start + i];
                    }

                    var mpb = new MaterialPropertyBlock();
                    mpb.SetVectorArray(ColorId, colors);
                    batch.Chunks.Add(new Chunk { Matrices = matrices, Mpb = mpb });
                }

                // Las listas crudas ya no hacen falta una vez empaquetadas en chunks.
                batch.PendingMatrices.Clear();
                batch.PendingColors.Clear();
            }
        }

        // Hojas triangulares estrechas agrupadas en pequeños macizos. La geometria es un mesh
        // compartido (ver BuildBladeTuftMesh) y cada planta se posiciona como instancia GPU.
        // La cantidad de matas TAMBIEN sale del rng determinista (no de Random.Range global): asi
        // (floorIndex,cellX,cellY,speciesSalt) determina tanto cuantas matas hay como donde caen,
        // y la celda se ve identica aunque el piso se reconstruya (puerta, drill, cambiar de piso
        // y volver).
        public void AddGrassTufts(Vector3 cellCenter, float cellSize, int floorIndex, int cellX, int cellY, int speciesSalt, int minCount, int maxCount, Material material)
        {
            var rng = MakeRng(floorIndex, cellX, cellY, speciesSalt);
            if (rng.NextDouble() > 0.86) return;
            int count = rng.Next(minCount, maxCount);
            var batch = GetOrCreateBatch(GetGrassTuftMesh(), material);
            Vector2[] clusters = MakeClusterCenters(rng, Mathf.Clamp(count / 4, 2, 3), cellSize * 0.27f);
            for (int i = 0; i < count; i++)
            {
                Vector2 off = clusters[i % clusters.Length] + InsideUnitCircle(rng) * cellSize * 0.045f;
                float height = cellSize * Lerp(rng, 0.09f, 0.15f);
                float width = cellSize * Lerp(rng, 0.035f, 0.06f);
                float yaw = NextFloat(rng) * 360f;
                float shade = Lerp(rng, 0.8f, 1.25f);
                var tint = new Color(0.22f * shade, 0.42f * shade, 0.16f * shade);

                var pos = cellCenter + new Vector3(off.x, height * 0.5f, off.y);
                var trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), new Vector3(width, height, 1f));
                batch.PendingMatrices.Add(trs);
                batch.PendingColors.Add(tint);
            }
        }

        // Mismos rangos que BuildSwordGrass original (3 cartas a 60 grados por brote, mas chicas y
        // mas densas que el pasto normal) -- ver GetSwordGrassMesh. Misma logica de determinismo
        // que AddGrassTufts (cantidad + posiciones, todo del mismo rng sembrado por celda).
        public void AddSwordGrass(Vector3 cellCenter, float cellSize, int floorIndex, int cellX, int cellY, int speciesSalt, int minCount, int maxCount, Material material)
        {
            var rng = MakeRng(floorIndex, cellX, cellY, speciesSalt);
            if (rng.NextDouble() > 0.62) return;
            int count = rng.Next(minCount, maxCount);
            var batch = GetOrCreateBatch(GetSwordGrassMesh(), material);
            Vector2[] clusters = MakeClusterCenters(rng, Mathf.Clamp(count / 5, 1, 2), cellSize * 0.28f);
            for (int i = 0; i < count; i++)
            {
                Vector2 off = clusters[i % clusters.Length] + InsideUnitCircle(rng) * cellSize * 0.055f;
                float height = cellSize * Lerp(rng, 0.05f, 0.09f);
                float width = cellSize * Lerp(rng, 0.05f, 0.08f);
                float yaw = NextFloat(rng) * 360f;
                float shade = Lerp(rng, 0.8f, 1.25f);
                var tint = new Color(0.24f * shade, 0.46f * shade, 0.18f * shade);

                var pos = cellCenter + new Vector3(off.x, height * 0.5f, off.y);
                var trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), new Vector3(width, height, 1f));
                batch.PendingMatrices.Add(trs);
                batch.PendingColors.Add(tint);
            }
        }

        public void AddFlowers(Vector3 cellCenter, float cellSize, int floorIndex, int cellX, int cellY, Material material, bool guaranteedPatch = false)
        {
            var rng = MakeRng(floorIndex, cellX, cellY, 17);
            if (!guaranteedPatch && rng.NextDouble() > 0.24) return;
            var batch = GetOrCreateBatch(GetFlowerHeadMesh(), material);
            var stems = GetOrCreateBatch(GetSwordGrassMesh(), material);
            var centers = GetOrCreateBatch(GetFlowerCenterMesh(), material);
            int count = rng.Next(2, 5);
            var hub = InsideUnitCircle(rng) * cellSize * 0.26f;
            Color[] palette =
            {
                new Color(0.94f, 0.53f, 0.66f),
                new Color(0.98f, 0.78f, 0.32f),
                new Color(0.68f, 0.68f, 0.96f),
                new Color(0.94f, 0.88f, 0.68f)
            };
            Color flower = palette[rng.Next(palette.Length)];
            for (int i = 0; i < count; i++)
            {
                Vector2 off = hub + InsideUnitCircle(rng) * cellSize * 0.08f;
                float height = cellSize * Lerp(rng, 0.13f, 0.19f);
                float size = cellSize * Lerp(rng, 0.045f, 0.065f);
                var pos = cellCenter + new Vector3(off.x, height, off.y);
                stems.PendingMatrices.Add(Matrix4x4.TRS(cellCenter + new Vector3(off.x, height * 0.48f, off.y), Quaternion.Euler(0f, NextFloat(rng) * 360f, 0f), new Vector3(size * 0.22f, height, 1f)));
                stems.PendingColors.Add(new Color(0.16f, 0.39f, 0.13f));
                batch.PendingMatrices.Add(Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(size, size, 1f)));
                float variation = Lerp(rng, 0.88f, 1.12f);
                batch.PendingColors.Add(new Color(flower.r * variation, flower.g * variation, flower.b * variation));
                centers.PendingMatrices.Add(Matrix4x4.TRS(pos + new Vector3(0f, 0f, -0.006f), Quaternion.identity, new Vector3(size * 0.22f, size * 0.22f, 1f)));
                centers.PendingColors.Add(new Color(1f, 0.84f, 0.39f));
            }
        }

        private static Vector2[] MakeClusterCenters(System.Random rng, int count, float radius)
        {
            var centers = new Vector2[count];
            for (int i = 0; i < count; i++) centers[i] = InsideUnitCircle(rng) * radius;
            return centers;
        }

        private Batch GetOrCreateBatch(Mesh mesh, Material material)
        {
            foreach (var b in _batches)
                if (b.Mesh == mesh && b.Material == material) return b;

            // GPU instancing lo tiene que tener prendido el material para que DrawMeshInstanced
            // funcione -- se prende por codigo en vez de depender de que alguien marque el
            // checkbox "Enable GPU Instancing" a mano en cada .mat.
            material.enableInstancing = true;
            var batch = new Batch { Mesh = mesh, Material = material };
            _batches.Add(batch);
            return batch;
        }

        private void Update()
        {
            if (_player == null) _player = FindObjectOfType<GridPlayerController>();
            Vector3 playerPosition = _player != null ? _player.transform.position : Vector3.zero;
            Shader.SetGlobalVector(GrassInteractorId, _player != null
                ? new Vector4(playerPosition.x, playerPosition.y, playerPosition.z, 1.35f)
                : Vector4.zero);

            foreach (var batch in _batches)
            {
                if (batch.Chunks == null) continue;
                foreach (var chunk in batch.Chunks)
                {
                    Graphics.DrawMeshInstanced(batch.Mesh, 0, batch.Material, chunk.Matrices, chunk.Matrices.Length,
                        chunk.Mpb, ShadowCastingMode.Off, receiveShadows: false);
                }
            }
        }

        // -- RNG determinista por celda --------------------------------------------------------
        // System.Random propio (no UnityEngine.Random.state global) sembrado por floor+celda+
        // especie: no pisa el estado de Random que usan el resto de los sistemas (encuentros,
        // props estructurales, etc.), y da la MISMA disposicion de pasto cada vez que se reconstruye
        // el mismo piso en la misma run (antes se barajaba entero cada Build()).
        private static System.Random MakeRng(int floorIndex, int x, int y, int salt)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + floorIndex;
                h = h * 31 + x;
                h = h * 31 + y;
                h = h * 31 + salt;
                return new System.Random(h);
            }
        }

        private static float NextFloat(System.Random rng) => (float)rng.NextDouble();
        private static float Lerp(System.Random rng, float a, float b) => a + NextFloat(rng) * (b - a);

        private static Vector2 InsideUnitCircle(System.Random rng)
        {
            float angle = NextFloat(rng) * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(NextFloat(rng));
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }

        // -- Mallas compartidas ------------------------------------------------------------------
        // Una sola malla por especie, generada una vez y reusada por TODAS las instancias via
        // instancing -- las hojas quedan en local X/Y (planos muy estrechos), base en y=-0.5, punta
        // en y=+0.5 (asi el viento de
        // PS1Grass/PS1SwordGrass, que pesa por v.vertex.y+0.5, no necesita cambiar) y UVs 0..1
        // identicos a un Quad estandar (asi el clip() de PS1SwordGrass sigue recortando la silueta
        // de hoja-de-espada sin tocar el shader).
        private static Mesh GetGrassTuftMesh()
        {
            if (_grassTuftMesh == null)
                _grassTuftMesh = BuildBladeTuftMesh();
            return _grassTuftMesh;
        }

        private static Mesh BuildBladeTuftMesh()
        {
            const int bladeCount = 7;
            var vertices = new List<Vector3>(bladeCount * 5);
            var uvs = new List<Vector2>(bladeCount * 5);
            var triangles = new List<int>(bladeCount * 9);
            for (int i = 0; i < bladeCount; i++)
            {
                float angle = (i / (float)bladeCount) * Mathf.PI * 2f;
                var rotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);
                float height = 0.68f + ((i * 37) % 5) * 0.07f;
                float baseRadius = 0.04f + (i % 2) * 0.035f;
                int b = vertices.Count;
                // Hojas triangulares finas en abanico, con bases cortas y puntas de distintas
                // alturas. La silueta evita los dos rectangulos anchos de las antiguas tarjetas.
                vertices.Add(rotation * new Vector3(-0.11f, -0.5f, baseRadius));
                vertices.Add(rotation * new Vector3(0.11f, -0.5f, baseRadius));
                vertices.Add(rotation * new Vector3(-0.075f, 0.06f, 0.02f));
                vertices.Add(rotation * new Vector3(0.075f, 0.06f, 0.02f));
                vertices.Add(rotation * new Vector3(0.02f, -0.5f + height, -baseRadius));
                uvs.Add(new Vector2(0.43f, 0f)); uvs.Add(new Vector2(0.57f, 0f));
                uvs.Add(new Vector2(0.44f, 0.55f)); uvs.Add(new Vector2(0.56f, 0.55f)); uvs.Add(new Vector2(0.5f, 1f));
                triangles.Add(b); triangles.Add(b + 2); triangles.Add(b + 1);
                triangles.Add(b + 1); triangles.Add(b + 2); triangles.Add(b + 3);
                triangles.Add(b + 2); triangles.Add(b + 4); triangles.Add(b + 3);
            }
            var mesh = new Mesh { name = "Foliage_TaperedBladeTuft" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh GetSwordGrassMesh()
        {
            if (_swordGrassMesh == null)
                _swordGrassMesh = BuildCrossMesh("FoliageCross_SwordGrass", bladeCount: 3, angleStep: 60f);
            return _swordGrassMesh;
        }

        private static Mesh GetFlowerHeadMesh()
        {
            if (_flowerHeadMesh != null) return _flowerHeadMesh;
            // Cinco pétalos triangulares en abanico, con caras dobles por el Cull Off del material.
            var verts = new List<Vector3>(20);
            var uvs = new List<Vector2>(20);
            var tris = new List<int>(30);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 side = new Vector3(-dir.y, dir.x, 0f);
                Vector3 c = dir * 0.18f;
                int b = verts.Count;
                verts.Add(c + side * 0.13f); verts.Add(c - side * 0.13f);
                verts.Add(dir * 0.5f + side * 0.08f); verts.Add(dir * 0.5f - side * 0.08f);
                uvs.Add(Vector2.zero); uvs.Add(Vector2.right); uvs.Add(Vector2.up); uvs.Add(Vector2.one);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }
            _flowerHeadMesh = new Mesh { name = "Foliage_FlowerHead" };
            _flowerHeadMesh.SetVertices(verts); _flowerHeadMesh.SetUVs(0, uvs); _flowerHeadMesh.SetTriangles(tris, 0);
            _flowerHeadMesh.RecalculateNormals(); _flowerHeadMesh.RecalculateBounds();
            return _flowerHeadMesh;
        }

        private static Mesh GetFlowerCenterMesh()
        {
            if (_flowerCenterMesh != null) return _flowerCenterMesh;
            var vertices = new List<Vector3> { Vector3.zero };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var triangles = new List<int>();
            for (int i = 0; i < 8; i++)
            {
                float a0 = i * Mathf.PI / 4f, a1 = (i + 1) * Mathf.PI / 4f;
                vertices.Add(new Vector3(Mathf.Cos(a0) * 0.5f, Mathf.Sin(a0) * 0.5f, 0f));
                vertices.Add(new Vector3(Mathf.Cos(a1) * 0.5f, Mathf.Sin(a1) * 0.5f, 0f));
                uvs.Add(new Vector2(0.5f + Mathf.Cos(a0) * 0.5f, 0.5f + Mathf.Sin(a0) * 0.5f));
                uvs.Add(new Vector2(0.5f + Mathf.Cos(a1) * 0.5f, 0.5f + Mathf.Sin(a1) * 0.5f));
                triangles.Add(0); triangles.Add(1 + i * 2); triangles.Add(2 + i * 2);
            }
            _flowerCenterMesh = new Mesh { name = "Foliage_FlowerCenter" };
            _flowerCenterMesh.SetVertices(vertices); _flowerCenterMesh.SetUVs(0, uvs); _flowerCenterMesh.SetTriangles(triangles, 0);
            _flowerCenterMesh.RecalculateNormals(); _flowerCenterMesh.RecalculateBounds();
            return _flowerCenterMesh;
        }

        private static Mesh BuildCrossMesh(string name, int bladeCount, float angleStep)
        {
            var verts = new List<Vector3>(bladeCount * 4);
            var uvs = new List<Vector2>(bladeCount * 4);
            var tris = new List<int>(bladeCount * 6);

            for (int i = 0; i < bladeCount; i++)
            {
                var rot = Quaternion.Euler(0f, i * angleStep, 0f);
                int b = verts.Count;

                verts.Add(rot * new Vector3(-0.5f, -0.5f, 0f));
                verts.Add(rot * new Vector3(0.5f, -0.5f, 0f));
                verts.Add(rot * new Vector3(0.5f, 0.5f, 0f));
                verts.Add(rot * new Vector3(-0.5f, 0.5f, 0f));

                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(0f, 1f));

                // Cull Off en ambos shaders (PS1Grass/PS1SwordGrass) ya hace que cada carta se vea
                // de los dos lados -- alcanza con un solo orden de winding por carta, no hace falta
                // duplicar geometria para la cara de atras.
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 0); tris.Add(b + 3); tris.Add(b + 2);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
