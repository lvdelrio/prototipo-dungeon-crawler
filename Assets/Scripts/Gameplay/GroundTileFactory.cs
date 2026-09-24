using UnityEngine;

namespace Gameplay
{
    // "Suelo interesante" para las celdas normales del bosque (llamado desde
    // DungeonLevelBuilder.BuildFloorTile en vez del cubo chato de un solo color): cada celda elige
    // al azar una de 6 variantes -- un tilemap 3D pseudo-isometrico, casillas cuadradas del mismo
    // tamano/posicion que el piso anterior (no rompe la alineacion con paredes/techo) pero con
    // pequenos props planos tipo "carta" encima (hojas, rocas, tierra) al estilo bajo-poligono de
    // PS1. Todo generado en runtime con primitivas, igual que el resto de DungeonLevelBuilder -- no
    // hay paquetes de Synty importados en este proyecto, esto es el equivalente funcional (misma
    // variedad visual de pasto/tierra/hojas/camino) sin depender de un asset externo ni de
    // prefabs .prefab hechos a mano (los props reales requieren el Editor de Unity para serializar
    // sin arriesgar el archivo).
    public static class GroundTileFactory
    {
        private static Material _sharedMaterial;

        // Pesos de aparicion: el pasto domina (bosque tranquilo de fondo), camino y tierra marcan
        // rutas/claros, y las variantes con props (hojas, roca) son las menos comunes para que se
        // lean como detalle puntual en vez de ruido repetido en cada celda.
        private static readonly (GroundTileKind kind, float weight)[] Weights =
        {
            (GroundTileKind.Grass, 0.42f),
            (GroundTileKind.Path, 0.16f),
            (GroundTileKind.Dirt, 0.14f),
            (GroundTileKind.GrassLeaves, 0.13f),
            (GroundTileKind.GrassRockDirt, 0.10f),
            (GroundTileKind.Leaves, 0.05f),
        };

        public static GroundTileKind PickRandomKind()
        {
            float roll = Random.value;
            float acc = 0f;
            foreach (var (kind, weight) in Weights)
            {
                acc += weight;
                if (roll <= acc) return kind;
            }
            return GroundTileKind.Grass;
        }

        // Albedo verde ligeramente oscuro para las variantes de pasto (pedido puntual), tonos
        // tierra/camino para el resto -- todas oscuras y desaturadas a proposito, PS1 no tenia
        // presupuesto de iluminacion para colores brillantes en el piso.
        private static Color BaseColor(GroundTileKind kind) => kind switch
        {
            GroundTileKind.Grass => new Color(0.16f, 0.27f, 0.14f),
            GroundTileKind.GrassLeaves => new Color(0.17f, 0.26f, 0.13f),
            GroundTileKind.GrassRockDirt => new Color(0.18f, 0.24f, 0.13f),
            GroundTileKind.Dirt => new Color(0.30f, 0.22f, 0.14f),
            GroundTileKind.Leaves => new Color(0.32f, 0.24f, 0.10f),
            GroundTileKind.Path => new Color(0.24f, 0.28f, 0.20f),
            _ => new Color(0.16f, 0.27f, 0.14f),
        };

        private static Material SharedMaterial(Material overrideMat)
        {
            if (overrideMat != null) return overrideMat;
            if (_sharedMaterial == null)
            {
                var shader = Shader.Find("Custom/PS1Ground");
                _sharedMaterial = new Material(shader != null ? shader : Shader.Find("Standard"));
                if (shader == null)
                {
                    // Sin el shader custom (por ejemplo si todavia no se importo en el Editor):
                    // Standard con brillo/metalico en 0 sigue cumpliendo "roughness ninguna".
                    _sharedMaterial.SetFloat("_Metallic", 0f);
                    _sharedMaterial.SetFloat("_Glossiness", 0f);
                }
            }
            return _sharedMaterial;
        }

        // Construye una celda de piso completa: la base (mismo tamano/posicion que el cubo chato
        // anterior, top en Y=0 igual que antes -- el jugador y los marcadores asumen esa altura) mas
        // los props de la variante elegida, todo bajo un unico GameObject rotado al azar en pasos de
        // 90 grados para que celdas vecinas con la misma variante no se vean idénticas.
        public static void BuildTile(Transform parent, Vector3 center, float cellSize, Material overrideMaterial, GroundTileKind? forcedKind = null)
        {
            var kind = forcedKind ?? PickRandomKind();
            var root = new GameObject($"Ground_{kind}");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            root.transform.rotation = Quaternion.Euler(0f, 90f * Random.Range(0, 4), 0f);

            var mat = SharedMaterial(overrideMaterial);
            Color tint = BaseColor(kind);
            float jitter = Random.Range(-0.025f, 0.025f);
            tint = new Color(
                Mathf.Clamp01(tint.r + jitter),
                Mathf.Clamp01(tint.g + jitter),
                Mathf.Clamp01(tint.b + jitter));

            // El BoxCollider de este cubo SE MANTIENE (a diferencia de los props decorativos de mas
            // abajo): es la superficie donde camina el jugador, igual que el "Floor" original.
            var baseTile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseTile.name = "Base";
            baseTile.transform.SetParent(root.transform, false);
            baseTile.transform.localPosition = new Vector3(0, -0.1f, 0);
            baseTile.transform.localScale = new Vector3(cellSize, 0.2f, cellSize);
            Tint(baseTile, mat, tint);

            switch (kind)
            {
                case GroundTileKind.GrassLeaves: BuildLeafCards(root.transform, mat, cellSize, count: 3); break;
                case GroundTileKind.Leaves: BuildLeafCards(root.transform, mat, cellSize, count: 5); break;
                case GroundTileKind.GrassRockDirt: BuildRockAndDirtPatch(root.transform, mat, cellSize); break;
                case GroundTileKind.Dirt: BuildPebbles(root.transform, mat, cellSize); break;
                default: break; // Grass y Path: solo la base, sin props
            }
        }

        // Cartas planas (un Quad chato tumbado, no una malla de hoja real) -- el truco clasico de
        // PS1/bajo-poligono para foliage: se lee como hojas sueltas a distancia de juego sin gastar
        // poligonos de mas. Sin collider: son decoracion, no deben frenar ni levantar al jugador.
        private static void BuildLeafCards(Transform parent, Material mat, float cellSize, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var leaf = GameObject.CreatePrimitive(PrimitiveType.Quad);
                leaf.name = "LeafCard";
                leaf.transform.SetParent(parent, false);
                Vector2 off = Random.insideUnitCircle * cellSize * 0.35f;
                leaf.transform.localPosition = new Vector3(off.x, 0.045f + Random.Range(0f, 0.02f), off.y);
                leaf.transform.localRotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
                float s = cellSize * Random.Range(0.16f, 0.24f);
                leaf.transform.localScale = new Vector3(s, s, 1f);

                var col = leaf.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);

                float shade = Random.Range(0.85f, 1.15f);
                Tint(leaf, mat, new Color(0.36f * shade, 0.22f * shade, 0.08f * shade));
            }
        }

        private static void BuildRockAndDirtPatch(Transform parent, Material mat, float cellSize)
        {
            var dirt = GameObject.CreatePrimitive(PrimitiveType.Quad);
            dirt.name = "DirtPatch";
            dirt.transform.SetParent(parent, false);
            Vector2 dirtOff = Random.insideUnitCircle * cellSize * 0.2f;
            dirt.transform.localPosition = new Vector3(dirtOff.x, 0.04f, dirtOff.y);
            dirt.transform.localRotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            float dirtScale = cellSize * Random.Range(0.3f, 0.42f);
            dirt.transform.localScale = new Vector3(dirtScale, dirtScale, 1f);
            var dirtCol = dirt.GetComponent<Collider>();
            if (dirtCol != null) Object.Destroy(dirtCol);
            Tint(dirt, mat, new Color(0.28f, 0.20f, 0.12f));

            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rock.name = "Rock";
            rock.transform.SetParent(parent, false);
            Vector2 rockOff = Random.insideUnitCircle * cellSize * 0.28f;
            float rockH = cellSize * Random.Range(0.12f, 0.2f);
            rock.transform.localPosition = new Vector3(rockOff.x, rockH * 0.5f, rockOff.y);
            rock.transform.localRotation = Quaternion.Euler(Random.Range(-10f, 10f), Random.Range(0f, 360f), Random.Range(-10f, 10f));
            rock.transform.localScale = new Vector3(cellSize * Random.Range(0.16f, 0.24f), rockH, cellSize * Random.Range(0.16f, 0.24f));
            var rockCol = rock.GetComponent<Collider>();
            if (rockCol != null) Object.Destroy(rockCol);
            Tint(rock, mat, new Color(0.33f, 0.33f, 0.34f));
        }

        private static void BuildPebbles(Transform parent, Material mat, float cellSize)
        {
            int count = Random.Range(2, 4);
            for (int i = 0; i < count; i++)
            {
                var pebble = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pebble.name = "Pebble";
                pebble.transform.SetParent(parent, false);
                Vector2 off = Random.insideUnitCircle * cellSize * 0.32f;
                float h = cellSize * Random.Range(0.05f, 0.08f);
                pebble.transform.localPosition = new Vector3(off.x, h * 0.5f, off.y);
                pebble.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                pebble.transform.localScale = new Vector3(cellSize * Random.Range(0.08f, 0.12f), h, cellSize * Random.Range(0.08f, 0.12f));
                var col = pebble.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                Tint(pebble, mat, new Color(0.38f, 0.32f, 0.26f));
            }
        }

        private static void Tint(GameObject go, Material mat, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = mat;
            var props = new MaterialPropertyBlock();
            props.SetColor("_Color", color);
            renderer.SetPropertyBlock(props);
        }
    }
}
