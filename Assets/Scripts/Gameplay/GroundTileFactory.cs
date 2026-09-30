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
            (GroundTileKind.FlowerBush, 0.035f),
            (GroundTileKind.FlowerPatch, 0.045f),
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
            GroundTileKind.FlowerBush => new Color(0.15f, 0.25f, 0.13f),
            GroundTileKind.FlowerPatch => new Color(0.17f, 0.27f, 0.13f),
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

        private static Material _grassGroundMaterial;

        // Mismo Custom/PS1Ground que SharedMaterial, pero con la textura pintada de verdad
        // asignada (ver Assets/Resources/Materials/PS1GroundGrass.mat -> Assets/Sprites/Ground/
        // PineForestFloor.png) -- SOLO para las variantes de pasto (isGrassFamily en BuildTile).
        // Mismo patron Resources-primero que SharedMaterial/GrassMaterial/SwordGrassMaterial: si el
        // asset no esta (por ej. corriendo sin haber wireado el material en el Editor), cae de
        // nuevo al shader sin textura -- se ve como el pasto de siempre, no rompe nada.
        private static Material GrassGroundMaterial()
        {
            if (_grassGroundMaterial == null)
            {
                var resourceMat = Resources.Load<Material>("Materials/PS1GroundGrass");
                _grassGroundMaterial = resourceMat != null ? new Material(resourceMat) : SharedMaterial(null);
            }
            return _grassGroundMaterial;
        }

        // Mismo patron que SharedMaterial de arriba (Resources primero, Shader.Find como ultimo
        // recurso) para Custom/PS1Grass -- ver Assets/Resources/Materials/PS1Grass.mat.
        public static Material GrassMaterial()
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

        private static Material _swordGrassMaterial;

        // Mismo patron que GrassMaterial de arriba, para Custom/PS1SwordGrass (ver
        // Assets/Shaders/PS1SwordGrass.shader) -- el pasto chico estilo Zelda.
        private static Material SwordGrassMaterial()
        {
            if (_swordGrassMaterial == null)
            {
                var resourceMat = Resources.Load<Material>("Materials/PS1SwordGrass");
                if (resourceMat != null)
                {
                    _swordGrassMaterial = new Material(resourceMat);
                }
                else
                {
                    var shader = Shader.Find("Custom/PS1SwordGrass");
                    _swordGrassMaterial = new Material(shader != null ? shader : Shader.Find("Standard"));
                    if (shader == null)
                    {
                        _swordGrassMaterial.SetFloat("_Metallic", 0f);
                        _swordGrassMaterial.SetFloat("_Glossiness", 0f);
                    }
                }
            }
            return _swordGrassMaterial;
        }

        // Construye una celda de piso completa: la base (mismo tamano/posicion que el cubo chato
        // anterior, top en Y=0 igual que antes -- el jugador y los marcadores asumen esa altura) mas
        // los props de la variante elegida, todo bajo un unico GameObject rotado al azar en pasos de
        // 90 grados para que celdas vecinas con la misma variante no se vean idénticas.
        public static GroundTileKind BuildTile(Transform parent, Vector3 center, float cellSize, Material overrideMaterial, int floorIndex, int cellX, int cellY, GroundTileKind? forcedKind = null)
        {
            var kind = forcedKind ?? PickRandomKind();
            var root = new GameObject($"Ground_{kind}");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            root.transform.rotation = Quaternion.Euler(0f, 90f * Random.Range(0, 4), 0f);

            // Pedido puntual, correccion: la primera pasada pintaba TODA la celda (base + bultos +
            // dientes de borde + rocas/arbustos) con la textura real, pareja y entera -- "no pintar
            // todo de un solo material entero... se ve extraño", y ademas se colaba en props que
            // no son piso (rocas, arbustos). El piso vuelve al color plano PS1 de siempre para
            // todo (base, bultos, dientes, props); la textura real de Assets/Sprites/Ground/
            // PineForestFloor.png ahora es EXCLUSIVA del piso, y en pedacitos chicos sueltos
            // encima (ver BuildGroundTexturePatches mas abajo), no una sabana continua.
            bool isGrassFamily = kind == GroundTileKind.Grass || kind == GroundTileKind.GrassLeaves
                || kind == GroundTileKind.GrassRockDirt || kind == GroundTileKind.Bush
                || kind == GroundTileKind.FlowerBush || kind == GroundTileKind.FlowerPatch;

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
            baseTile.isStatic = true; // nunca se mueve ni cambia de material -- que el static batching lo agrupe
            Tint(baseTile, mat, tint);

            // Relieve mas ancho y legible en claros de tierra/pasto. Los caminos quedan despejados;
            // asi el borde de cada celda no se llena de dientes repetidos ni parece cuadriculado.
            if (kind != GroundTileKind.Path && kind != GroundTileKind.Leaves)
                BuildGroundBumps(root.transform, mat, cellSize, tint);

            if (isGrassFamily)
            {
                // Pedacitos sueltos de la textura pintada real (ver comentario de arriba) --
                // ENCIMA del piso de color plano, tapando solo una fraccion chica de la celda cada
                // uno, nunca la celda entera.
                BuildGroundTexturePatches(root.transform, cellSize);
                if (kind != GroundTileKind.Bush && kind != GroundTileKind.FlowerBush)
                {
                    if (kind == GroundTileKind.FlowerPatch)
                        FoliageManager.Instance.AddGrassTufts(center, cellSize, floorIndex, cellX, cellY, 31, 3, 5, GrassMaterial());
                    else BuildGrassTufts(center, cellSize, floorIndex, cellX, cellY);
                    if (kind != GroundTileKind.FlowerPatch) BuildSwordGrass(center, cellSize, floorIndex, cellX, cellY);
                    if (kind == GroundTileKind.FlowerPatch || kind == GroundTileKind.Grass || kind == GroundTileKind.GrassLeaves)
                        FoliageManager.Instance.AddFlowers(center, cellSize, floorIndex, cellX, cellY, GrassMaterial(), guaranteedPatch: kind == GroundTileKind.FlowerPatch);
                }
            }

            switch (kind)
            {
                case GroundTileKind.GrassLeaves: BuildLeafCards(root.transform, mat, cellSize, count: 3); break;
                case GroundTileKind.Leaves: BuildLeafCards(root.transform, mat, cellSize, count: 5); break;
                case GroundTileKind.GrassRockDirt: BuildRockAndDirtPatch(root.transform, mat, cellSize); break;
                case GroundTileKind.Dirt: BuildPebbles(root.transform, mat, cellSize); break;
                case GroundTileKind.Bush: BuildBushClump(root.transform, mat, cellSize); break;
                case GroundTileKind.FlowerBush: BuildBushClump(root.transform, mat, cellSize, true); break;
                case GroundTileKind.FlowerPatch: break;
                default: break; // Grass y Path: solo la base (+ relieve/pasto de arriba)
            }

            return kind;
        }

        // Bultos chatos (esferas aplastadas) esparcidos sobre la base -- el relieve barato de
        // PS1/bajo-poligono: nunca desplazan la malla de verdad, solo agregan volumen ENCIMA para
        // que la superficie deje de leerse perfectamente plana. Sin collider: decoracion pura, la
        // base de abajo sigue siendo la unica superficie de colision.
        private static void BuildGroundBumps(Transform parent, Material mat, float cellSize, Color baseTint)
        {
            int count = Random.Range(1, 3);
            for (int i = 0; i < count; i++)
            {
                var bump = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bump.name = "GroundBump";
                bump.transform.SetParent(parent, false);
                Vector2 off = Random.insideUnitCircle * cellSize * 0.23f;
                float radius = cellSize * Random.Range(0.22f, 0.34f);
                float squash = Random.Range(0.48f, 0.72f);
                bump.transform.localPosition = new Vector3(off.x, radius * squash * 0.34f, off.y);
                bump.transform.localScale = new Vector3(radius, radius * squash, radius);
                bump.isStatic = true; // nunca se mueve ni cambia de material -- que el static batching lo agrupe

                var col = bump.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);

                float shade = Random.Range(0.85f, 1.2f);
                Tint(bump, mat, new Color(
                    Mathf.Clamp01(baseTint.r * shade),
                    Mathf.Clamp01(baseTint.g * shade),
                    Mathf.Clamp01(baseTint.b * shade)));
            }
        }

        // Pedacitos sueltos de la textura pintada real (Assets/Sprites/Ground/PineForestFloor.png,
        // pedido puntual: "pintarlo de pedacitos", no la celda entera de una sola vez). Cartas
        // chatas tumbadas (mismo truco que BuildLeafCards) con GrassGroundMaterial -- ese material
        // muestrea por POSICION DE MUNDO (ver Custom/PS1Ground _MainTex/_TexScale), asi que cada
        // pedacito, al estar en un punto de mundo distinto, muestra un recorte distinto de la
        // textura sin necesitar UVs particulares por instancia. Pisan apenas 0.012 sobre el piso
        // -- lo justo para no pelearse en el z-buffer con la base de abajo, invisible a simple
        // vista pero evita parpadeo.
        private static void BuildGroundTexturePatches(Transform parent, float cellSize)
        {
            var mat = GrassGroundMaterial();
            int count = Random.Range(2, 4);
            for (int i = 0; i < count; i++)
            {
                var patch = GameObject.CreatePrimitive(PrimitiveType.Quad);
                patch.name = "GroundTexturePatch";
                patch.transform.SetParent(parent, false);
                float s = cellSize * Random.Range(0.22f, 0.34f);
                Vector2 off = RandomFlatPatchOffset(cellSize, s);
                patch.transform.localPosition = new Vector3(off.x, 0.012f + Random.Range(0f, 0.008f), off.y);
                patch.transform.localRotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
                patch.transform.localScale = new Vector3(s, s, 1f);

                var col = patch.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);

                // Blanco: el color real lo aporta la textura, no un tinte extra encima (a
                // diferencia del resto de los props, que son color plano sin textura).
                Tint(patch, mat, Color.white);
            }
        }

        // Desplaza una carta cuadrada tumbada sin dejar que sus esquinas crucen el limite de la
        // celda cuando se rota. El radio usa la semidiagonal de la carta, no solo la mitad de su
        // lado, para cubrir cualquier orientacion.
        private static Vector2 RandomFlatPatchOffset(float cellSize, float patchSize)
        {
            float edgeMargin = cellSize * 0.025f;
            float halfDiagonal = patchSize * 0.70710678f;
            float maxOffset = Mathf.Max(0f, cellSize * 0.5f - halfDiagonal - edgeMargin);
            return Random.insideUnitCircle * maxOffset;
        }

        // Matitas de pasto de verdad (no solo el color de la base): 2 cartas cruzadas en X por
        // mata, el truco clasico de "grass card" de bajo poligono, con el shader Custom/PS1Grass
        // (ver PS1Grass.shader) que mece la punta con el viento sin animar nada por codigo -- todo
        // el movimiento vive en el vertex shader. Antes esto instanciaba 2 GameObject+Quad por
        // mata; ahora cada mata es solo una matriz que FoliageManager dibuja con GPU instancing
        // (ver FoliageManager.AddGrassTufts), sin crear ni un GameObject.
        private static void BuildGrassTufts(Vector3 cellCenter, float cellSize, int floorIndex, int cellX, int cellY)
        {
            // 8-14 matas de hojas estrechas en grupos pequeños, dejando claros visibles en vez de cobertura uniforme.
            // tambien es determinista por celda, ver FoliageManager.AddGrassTufts.
            FoliageManager.Instance.AddGrassTufts(cellCenter, cellSize, floorIndex, cellX, cellY, speciesSalt: 1, minCount: 8, maxCount: 15, GrassMaterial());
        }

        // Pasto chico estilo Zelda ("hojas de espada", pedido puntual): mas chico y mas denso que
        // BuildGrassTufts de arriba, con el shader Custom/PS1SwordGrass (recorta cada carta en
        // punta via clip(), ver el shader) en vez de la carta rectangular lisa de PS1Grass -- se
        // lee como una capa de detalle fino ENCIMA del pasto de base, no en su reemplazo. Cada
        // brote tiene 3 cartas a 60 grados (ver FoliageManager.GetSwordGrassMesh), tambien via GPU
        // instancing en vez de GameObjects sueltos.
        private static void BuildSwordGrass(Vector3 cellCenter, float cellSize, int floorIndex, int cellX, int cellY)
        {
            // Menos brotes de detalle: ahora acompañan el grupo principal en vez de formar otra capa pareja.
            // por celda, ver FoliageManager.AddSwordGrass.
            FoliageManager.Instance.AddSwordGrass(cellCenter, cellSize, floorIndex, cellX, cellY, speciesSalt: 2, minCount: 4, maxCount: 8, SwordGrassMaterial());
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
                float s = cellSize * Random.Range(0.16f, 0.24f);
                Vector2 off = RandomFlatPatchOffset(cellSize, s);
                leaf.transform.localPosition = new Vector3(off.x, 0.045f + Random.Range(0f, 0.02f), off.y);
                leaf.transform.localRotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
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
            float dirtScale = cellSize * Random.Range(0.3f, 0.42f);
            Vector2 dirtOff = RandomFlatPatchOffset(cellSize, dirtScale);
            dirt.transform.localPosition = new Vector3(dirtOff.x, 0.04f, dirtOff.y);
            dirt.transform.localRotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            dirt.transform.localScale = new Vector3(dirtScale, dirtScale, 1f);
            var dirtCol = dirt.GetComponent<Collider>();
            if (dirtCol != null) Object.Destroy(dirtCol);
            Tint(dirt, mat, new Color(0.28f, 0.20f, 0.12f));

            BuildDeformedRock(parent, mat, cellSize, Random.insideUnitCircle * cellSize * 0.28f);
        }

        // Roca "deforme" (pedido puntual: mas irregular que un solo cubo liso): un bloque
        // principal mas 2-3 bultos mas chicos superpuestos en angulos al azar -- el mismo truco
        // barato de bajo poligono que un boulder de verdad (una malla esculpida a mano), solo que
        // con primitivas apiladas en vez de una malla propia.
        private static void BuildDeformedRock(Transform parent, Material mat, float cellSize, Vector2 rockOff)
        {
            float rockH = cellSize * Random.Range(0.14f, 0.24f);
            Color tint = new Color(0.33f, 0.33f, 0.34f);

            var core = GameObject.CreatePrimitive(PrimitiveType.Cube);
            core.name = "Rock";
            core.transform.SetParent(parent, false);
            core.transform.localPosition = new Vector3(rockOff.x, rockH * 0.5f, rockOff.y);
            core.transform.localRotation = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f));
            core.transform.localScale = new Vector3(cellSize * Random.Range(0.16f, 0.24f), rockH, cellSize * Random.Range(0.16f, 0.24f));
            var coreCol = core.GetComponent<Collider>();
            if (coreCol != null) Object.Destroy(coreCol);
            Tint(core, mat, tint);

            int lumps = Random.Range(2, 4);
            for (int i = 0; i < lumps; i++)
            {
                var lump = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lump.name = "RockLump";
                lump.transform.SetParent(parent, false);
                Vector2 lumpOff = rockOff + Random.insideUnitCircle * cellSize * 0.1f;
                float lumpH = rockH * Random.Range(0.4f, 0.75f);
                lump.transform.localPosition = new Vector3(lumpOff.x, lumpH * Random.Range(0.3f, 0.7f), lumpOff.y);
                lump.transform.localRotation = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f));
                lump.transform.localScale = new Vector3(
                    cellSize * Random.Range(0.08f, 0.16f),
                    lumpH,
                    cellSize * Random.Range(0.08f, 0.16f));

                var lumpCol = lump.GetComponent<Collider>();
                if (lumpCol != null) Object.Destroy(lumpCol);

                float shade = Random.Range(0.9f, 1.1f);
                Tint(lump, mat, new Color(tint.r * shade, tint.g * shade, tint.b * shade));
            }
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

        // Dientes chatos metidos hacia dentro del borde. Marcan un contorno irregular sin invadir
        // el cuadrante vecino. Los 4 lados son ejes X/Z y las celdas quedan alineadas a los ejes.
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
            float inset = cellSize * 0.025f;
            int teeth = Random.Range(3, 5);
            for (int i = 0; i < teeth; i++)
            {
                float alongSize = cellSize * Random.Range(0.16f, 0.26f);
                float alongLimit = Mathf.Max(0f, half - alongSize * 0.5f - inset);
                float along = Random.Range(-alongLimit, alongLimit);
                float reach = cellSize * Random.Range(0.08f, 0.24f); // profundidad del relieve hacia el interior

                var tooth = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tooth.name = "EdgeTooth";
                tooth.transform.SetParent(parent, false);

                float perpCenter = outwardSign * (half - reach * 0.5f - inset);
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
        private static void BuildBushClump(Transform parent, Material mat, float cellSize, bool flowering = false)
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

            if (!flowering) return;
            // Acentos pequeños sobre la copa: la masa sigue leyéndose como arbusto verde.
            int flowers = Random.Range(4, 8);
            Color[] colors = { new Color(0.96f, 0.66f, 0.32f), new Color(0.9f, 0.55f, 0.68f), new Color(0.88f, 0.84f, 0.62f) };
            for (int i = 0; i < flowers; i++)
            {
                var flower = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                flower.name = "BushFlower";
                flower.transform.SetParent(parent, false);
                Vector2 off = hub + Random.insideUnitCircle * cellSize * 0.2f;
                float size = cellSize * Random.Range(0.045f, 0.075f);
                flower.transform.localPosition = new Vector3(off.x, cellSize * Random.Range(0.15f, 0.23f), off.y);
                flower.transform.localScale = new Vector3(size, size * 0.7f, size);
                var col = flower.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                Tint(flower, mat, colors[Random.Range(0, colors.Length)]);
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
