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
            (GroundTileKind.Grass, 0.38f),
            (GroundTileKind.Path, 0.16f),
            (GroundTileKind.Dirt, 0.14f),
            (GroundTileKind.GrassLeaves, 0.12f),
            (GroundTileKind.GrassRockDirt, 0.09f),
            (GroundTileKind.Bush, 0.06f),
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
            GroundTileKind.Bush => new Color(0.15f, 0.25f, 0.13f),
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

            // Borde no uniforme (pedido puntual, referencia: un piso "zigzagueante" con el borde
            // irregular, no un cuadrado perfecto): dientes chatos que sobresalen un poco del borde
            // exacto de la celda, del mismo tono. No hay boolean/CSG con primitivas, asi que esto
            // es una aproximacion -- no calza perfecto diente con diente contra el vecino, pero
            // rompe la silueta cuadrada de cada celda.
            BuildJaggedEdge(root.transform, mat, cellSize, tint);

            bool isGrassFamily = kind == GroundTileKind.Grass || kind == GroundTileKind.GrassLeaves
                || kind == GroundTileKind.GrassRockDirt || kind == GroundTileKind.Bush;
            if (isGrassFamily)
                BuildGrassTufts(root.transform, cellSize);

            switch (kind)
            {
                case GroundTileKind.GrassLeaves: BuildLeafCards(root.transform, mat, cellSize, count: 3); break;
                case GroundTileKind.Leaves: BuildLeafCards(root.transform, mat, cellSize, count: 5); break;
                case GroundTileKind.GrassRockDirt: BuildRockAndDirtPatch(root.transform, mat, cellSize); break;
                case GroundTileKind.Dirt: BuildPebbles(root.transform, mat, cellSize); break;
                case GroundTileKind.Bush: BuildBushClump(root.transform, mat, cellSize); break;
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
            int count = Random.Range(4, 7); // mas finitas -- compensar con un poco mas de cantidad
            for (int i = 0; i < count; i++)
            {
                Vector2 off = Random.insideUnitCircle * cellSize * 0.42f;
                float height = cellSize * Random.Range(0.09f, 0.15f); // "mas fino y pequeno" -- pedido puntual
                float width = cellSize * Random.Range(0.035f, 0.06f);
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

        // Dientes chatos que sobresalen un poco del borde exacto de la celda (pedido puntual: que
        // el borde del piso no se vea como un cuadrado perfecto). 2-3 dientes por lado, tamano y
        // cuanto sobresalen al azar -- los 4 lados son siempre ejes X/Z (las celdas son cuadradas
        // alineadas a los ejes), asi que no hace falta rotar nada, solo elegir que eje es "a lo
        // largo del lado" y cual es "hacia afuera".
        private static void BuildJaggedEdge(Transform parent, Material mat, float cellSize, Color tint)
        {
            BuildEdgeTeeth(parent, mat, cellSize, tint, alongIsX: true, outwardSign: 1f);  // borde +Z
            BuildEdgeTeeth(parent, mat, cellSize, tint, alongIsX: true, outwardSign: -1f); // borde -Z
            BuildEdgeTeeth(parent, mat, cellSize, tint, alongIsX: false, outwardSign: 1f); // borde +X
            BuildEdgeTeeth(parent, mat, cellSize, tint, alongIsX: false, outwardSign: -1f);// borde -X
        }

        private static void BuildEdgeTeeth(Transform parent, Material mat, float cellSize, Color tint, bool alongIsX, float outwardSign)
        {
            float half = cellSize * 0.5f;
            int teeth = Random.Range(2, 4);
            for (int i = 0; i < teeth; i++)
            {
                float along = (Random.value - 0.5f) * cellSize * 0.7f;
                float alongSize = cellSize * Random.Range(0.14f, 0.24f);
                float reach = cellSize * Random.Range(0.03f, 0.12f); // cuanto sobresale del borde exacto

                var tooth = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tooth.name = "EdgeTooth";
                tooth.transform.SetParent(parent, false);

                float perpCenter = outwardSign * (half + reach * 0.5f);
                tooth.transform.localPosition = alongIsX
                    ? new Vector3(along, -0.03f, perpCenter)
                    : new Vector3(perpCenter, -0.03f, along);
                tooth.transform.localScale = alongIsX
                    ? new Vector3(alongSize, 0.06f, reach)
                    : new Vector3(reach, 0.06f, alongSize);

                var col = tooth.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);

                float shade = Random.Range(0.9f, 1.1f);
                Tint(tooth, mat, new Color(
                    Mathf.Clamp01(tint.r * shade),
                    Mathf.Clamp01(tint.g * shade),
                    Mathf.Clamp01(tint.b * shade)));
            }
        }

        // Arbusto: un amontonado de 3-4 bultos superpuestos (esferas achatadas mas grandes y menos
        // chatas que BuildGroundBumps), como la silueta redondeada de un matorral. Tile propio
        // (GroundTileKind.Bush), no un prop suelto sobre pasto -- se ve desde lejos como un bulto
        // solido de follaje en vez de una mancha de color.
        private static void BuildBushClump(Transform parent, Material mat, float cellSize)
        {
            Vector2 hub = Random.insideUnitCircle * cellSize * 0.15f;
            int lobes = Random.Range(3, 5);
            for (int i = 0; i < lobes; i++)
            {
                var lobe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lobe.name = "BushLobe";
                lobe.transform.SetParent(parent, false);
                Vector2 off = hub + Random.insideUnitCircle * cellSize * 0.14f;
                float radius = cellSize * Random.Range(0.18f, 0.26f);
                lobe.transform.localPosition = new Vector3(off.x, radius * 0.42f, off.y);
                lobe.transform.localScale = new Vector3(radius, radius * 0.75f, radius);

                var col = lobe.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);

                float shade = Random.Range(0.85f, 1.15f);
                Tint(lobe, mat, new Color(0.13f * shade, 0.24f * shade, 0.1f * shade));
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
