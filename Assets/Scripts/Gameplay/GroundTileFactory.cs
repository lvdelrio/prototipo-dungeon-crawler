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
        private static Material _grassMaterial;

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
                // Un build standalone descarta ("strippea") cualquier shader que ningun Material
                // real del proyecto referencie -- Shader.Find("Custom/PS1Ground") funciona en el
                // Editor (ve todos los shaders) pero devuelve null en el juego compilado, y ahi
                // mismo se rompia BeginBrandNewGame (ver Assets/Resources/Materials/PS1Ground.mat:
                // ese SI cuenta como "usado" porque vive en Resources, asi el build lo incluye
                // siempre). Cargarlo por asset en vez de por nombre de shader evita el problema.
                var resourceMat = Resources.Load<Material>("Materials/PS1Ground");
                if (resourceMat != null)
                {
                    _sharedMaterial = new Material(resourceMat);
                }
                else
                {
                    var shader = Shader.Find("Custom/PS1Ground");
                    _sharedMaterial = new Material(shader != null ? shader : Shader.Find("Standard"));
                    if (shader == null)
                    {
                        // Ni el asset en Resources ni el shader custom aparecieron: Standard con
                        // brillo/metalico en 0 sigue cumpliendo "roughness ninguna" como ultimo
                        // recurso (puede fallar igual en build por el mismo motivo de arriba).
                        _sharedMaterial.SetFloat("_Metallic", 0f);
                        _sharedMaterial.SetFloat("_Glossiness", 0f);
                    }
                }
            }
            return _sharedMaterial;
        }

        // Mismo patron que SharedMaterial de arriba (Resources primero, Shader.Find como ultimo
        // recurso) para Custom/PS1Grass -- ver Assets/Resources/Materials/PS1Grass.mat.
        private static Material GrassMaterial()
        {
            if (_grassMaterial == null)
            {
                var resourceMat = Resources.Load<Material>("Materials/PS1Grass");
                if (resourceMat != null)
                {
                    _grassMaterial = new Material(resourceMat);
                }
                else
                {
                    var shader = Shader.Find("Custom/PS1Grass");
                    _grassMaterial = new Material(shader != null ? shader : Shader.Find("Standard"));
                    if (shader == null)
                    {
                        _grassMaterial.SetFloat("_Metallic", 0f);
                        _grassMaterial.SetFloat("_Glossiness", 0f);
                    }
                }
            }
            return _grassMaterial;
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

            // Relieve general (pedido puntual: "que no sean tan planos y lisos"): unos pocos
            // bultos chatos y redondeados, del mismo tono que la base con variacion propia, en
            // TODAS las variantes -- rompen la superficie perfectamente lisa del cubo sin tocar
            // su collider (siguen ahi debajo, el jugador camina sobre el cubo de siempre).
            BuildGroundBumps(root.transform, mat, cellSize, tint);

            bool isGrassFamily = kind == GroundTileKind.Grass || kind == GroundTileKind.GrassLeaves || kind == GroundTileKind.GrassRockDirt;
            if (isGrassFamily)
                BuildGrassTufts(root.transform, cellSize);

            switch (kind)
            {
                case GroundTileKind.GrassLeaves: BuildLeafCards(root.transform, mat, cellSize, count: 3); break;
                case GroundTileKind.Leaves: BuildLeafCards(root.transform, mat, cellSize, count: 5); break;
                case GroundTileKind.GrassRockDirt: BuildRockAndDirtPatch(root.transform, mat, cellSize); break;
                case GroundTileKind.Dirt: BuildPebbles(root.transform, mat, cellSize); BuildCrackedPathLines(root.transform, mat, cellSize); break;
                default: break; // Grass y Path: solo la base (+ relieve/pasto de arriba)
            }
        }

        // Bultos chatos (esferas aplastadas) esparcidos sobre la base -- el relieve barato de
        // PS1/bajo-poligono: nunca desplazan la malla de verdad, solo agregan volumen ENCIMA para
        // que la superficie deje de leerse perfectamente plana. Sin collider: decoracion pura, la
        // base de abajo sigue siendo la unica superficie de colision.
        private static void BuildGroundBumps(Transform parent, Material mat, float cellSize, Color baseTint)
        {
            int count = Random.Range(3, 5);
            for (int i = 0; i < count; i++)
            {
                var bump = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bump.name = "GroundBump";
                bump.transform.SetParent(parent, false);
                Vector2 off = Random.insideUnitCircle * cellSize * 0.4f;
                float radius = cellSize * Random.Range(0.09f, 0.16f);
                float squash = Random.Range(0.28f, 0.45f); // achatado: un bulto, no una pelota
                bump.transform.localPosition = new Vector3(off.x, radius * squash * 0.5f, off.y);
                bump.transform.localScale = new Vector3(radius, radius * squash, radius);

                var col = bump.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);

                float shade = Random.Range(0.85f, 1.2f);
                Tint(bump, mat, new Color(
                    Mathf.Clamp01(baseTint.r * shade),
                    Mathf.Clamp01(baseTint.g * shade),
                    Mathf.Clamp01(baseTint.b * shade)));
            }
        }

        // Matitas de pasto de verdad (no solo el color de la base): 2 cartas cruzadas en X por
        // mata, el truco clasico de "grass card" de bajo poligono, con el shader Custom/PS1Grass
        // (ver PS1Grass.shader) que mece la punta con el viento sin animar nada por codigo -- todo
        // el movimiento vive en el vertex shader.
        private static void BuildGrassTufts(Transform parent, float cellSize)
        {
            var grassMat = GrassMaterial();
            int count = Random.Range(3, 5);
            for (int i = 0; i < count; i++)
            {
                Vector2 off = Random.insideUnitCircle * cellSize * 0.42f;
                float height = cellSize * Random.Range(0.14f, 0.24f);
                float width = cellSize * Random.Range(0.1f, 0.16f);
                float baseYaw = Random.Range(0f, 360f);
                float shade = Random.Range(0.8f, 1.25f);
                Color tint = new Color(0.22f * shade, 0.42f * shade, 0.16f * shade);

                BuildGrassCard(parent, grassMat, off, height, width, baseYaw, tint);
                BuildGrassCard(parent, grassMat, off, height, width, baseYaw + 90f, tint);
            }
        }

        private static void BuildGrassCard(Transform parent, Material mat, Vector2 off, float height, float width, float yaw, Color tint)
        {
            var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            card.name = "GrassCard";
            card.transform.SetParent(parent, false);
            // Base del quad (vertice local y=-0.5) apoyada en el piso: con localPosition.y =
            // height*0.5 el borde de abajo queda justo en Y=0, igual que el resto de los props.
            card.transform.localPosition = new Vector3(off.x, height * 0.5f, off.y);
            card.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            card.transform.localScale = new Vector3(width, height, 1f);

            var col = card.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            Tint(card, mat, tint);
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

        // Grietas de tierra seca y curtida ("caminos borrascosos" -- pedido puntual): una red de
        // segmentos CORTOS que se ramifican desde un centro comun, no un par de lineas largas al
        // azar cruzando el parche (eso se leia como dos rayones sueltos, no como una textura
        // agrietada). Cada "grieta principal" tiene chance de tirar una ramita mas corta a mitad
        // de camino, el mismo patron ramificado de una grieta real de barro seco.
        private static void BuildCrackedPathLines(Transform parent, Material mat, float cellSize)
        {
            Vector2 hub = Random.insideUnitCircle * cellSize * 0.12f;
            int primaryCount = Random.Range(3, 5);
            float baseAngle = Random.Range(0f, 360f);
            for (int i = 0; i < primaryCount; i++)
            {
                float angle = baseAngle + i * (360f / primaryCount) + Random.Range(-20f, 20f);
                float length = cellSize * Random.Range(0.16f, 0.28f);
                Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                Vector2 mid = hub + dir * (length * 0.5f);
                BuildCrackSegment(parent, mat, mid, angle, length, cellSize);

                // Ramita corta desde un punto a mitad del segmento principal -- rompe la linea
                // recta, se lee mas como una grieta real que como un palito.
                if (Random.value < 0.7f)
                {
                    Vector2 branchOrigin = hub + dir * (length * Random.Range(0.4f, 0.8f));
                    float branchAngle = angle + (Random.value < 0.5f ? 1f : -1f) * Random.Range(35f, 70f);
                    float branchLength = length * Random.Range(0.35f, 0.6f);
                    Vector2 branchDir = new Vector2(Mathf.Cos(branchAngle * Mathf.Deg2Rad), Mathf.Sin(branchAngle * Mathf.Deg2Rad));
                    Vector2 branchMid = branchOrigin + branchDir * (branchLength * 0.5f);
                    BuildCrackSegment(parent, mat, branchMid, branchAngle, branchLength, cellSize);
                }
            }
        }

        // Un segmento de grieta: posXZ es el CENTRO del segmento (en espacio local XZ de la
        // celda), angleDeg su direccion (misma convencion que Mathf.Cos/Sin usados para ubicarlo).
        private static void BuildCrackSegment(Transform parent, Material mat, Vector2 posXZ, float angleDeg, float length, float cellSize)
        {
            var crack = GameObject.CreatePrimitive(PrimitiveType.Quad);
            crack.name = "DirtCrack";
            crack.transform.SetParent(parent, false);
            crack.transform.localPosition = new Vector3(posXZ.x, 0.05f, posXZ.y);
            // -angleDeg: asi el eje "largo" del quad (su X local) queda alineado con la misma
            // direccion (cos,sin) que se uso para calcular la posicion -- ver derivacion en el eje
            // Y despues de la inclinacion de 90 en X.
            crack.transform.localRotation = Quaternion.Euler(90f, -angleDeg, 0f);
            float width = cellSize * Random.Range(0.02f, 0.035f);
            crack.transform.localScale = new Vector3(length, width, 1f);

            var col = crack.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            float shade = Random.Range(0.5f, 0.7f);
            Tint(crack, mat, new Color(0.12f * shade, 0.08f * shade, 0.05f * shade));
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
