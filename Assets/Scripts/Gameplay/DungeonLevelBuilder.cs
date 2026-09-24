using System.Collections.Generic;
using UnityEngine;
using DungeonGen;
using Lore;

namespace Gameplay
{
    public class DungeonLevelBuilder : MonoBehaviour
    {
        [Header("Materiales opcionales (si se dejan vacios se usan colores solidos)")]
        public Material floorMaterial;
        public Material ceilingMaterial;
        public Material wallMaterial;
        public Material startMarkerMaterial;
        public Material endMarkerMaterial;
        public Material questMarkerMaterial;
        public Material switchMarkerMaterial;
        public Material landingMarkerMaterial;
        public Material stairsUpMaterial;
        public Material stairsDownMaterial;
        public Material bossMarkerMaterial;
        public Material loreMarkerMaterial;
        public Material bossRoomFloorMaterial;
        public Material bossRoomCeilingMaterial;
        public Material voidBlockMaterial;
        public Material lockedDoorMarkerMaterial;
        public Material leverMarkerMaterial;
        public Material treasureMarkerMaterial;
        public Material trapMarkerMaterial;

        // Restos alrededor del marcador de lore (ver Lore.SceneDressing / BuildLoreSceneDressing).
        // Un solo campo compartido por las 4 variantes -- igual que trapMarkerMaterial cubre tanto
        // la linea de flechas como los picos, el color/forma de cada prop ya las distingue entre si
        // aun si un artista termina asignando el mismo material a todas.
        public Material loreDressingMaterial;

        // Tinte distinto (piso/pared/techo) para la zona aislada de cada piso: en BotW un mundo con
        // regiones visualmente distinguibles hace que el jugador arme su propio mapa mental
        // caminando, en vez de depender todo el tiempo del automapa -- esta zona ya tenia su color
        // propio en el minimapa (MinimapUI.FloorColor), esto lo lleva tambien a la vista en 3D.
        public Material isoFloorMaterial;
        public Material isoCeilingMaterial;
        public Material isoWallMaterial;

        // Bioma 2 (Cueva Intergalactica, DungeonFloor.Biome != 0): techo de cielo estrellado
        // (Custom/StarrySky) y piso con el reflejo tenue de esas mismas estrellas (Custom/
        // StarlitFloor, ver el shader para el porque comparten el mismo campo de estrellas). Pisa
        // por encima del tinte de zona aislada: un piso de Bioma 2 siempre se ve como Bioma 2, sea
        // o no ademas zona aislada.
        public Material biome2CeilingMaterial;
        public Material biome2FloorMaterial;

        // Aura sutil de piso alrededor de las escaleras (Custom/StairsAura, ver
        // BuildStairsFloorAura) -- se nota desde ~3 celdas de distancia por un pasillo recto.
        public Material stairsAuraMaterial;

        private GameObject _root;
        private readonly Dictionary<int, (GameObject switchGo, GameObject landingGo)> _shortcutMarkers = new Dictionary<int, (GameObject, GameObject)>();
        private readonly Dictionary<(int x, int y), GameObject> _lockedDoorMarkers = new Dictionary<(int, int), GameObject>();

        // Plantilla del burst de salpicadura (ver BuildPuzzleTile): UNA sola, compartida por todas
        // las celdas "reales" de la sala -- Unity soporta que varios sistemas de particulas padre
        // apunten al mismo sub-emitter. Se reconstruye (queda null) cada vez que Clear() destruye
        // _root, porque esta parentada ahi adentro.
        private ParticleSystem _puzzleSplashTemplate;

        public void Clear()
        {
            if (_root != null) Destroy(_root);
            _shortcutMarkers.Clear();
            _lockedDoorMarkers.Clear();
            _puzzleSplashTemplate = null;
        }

        // Una celda logica = una distancia cellSize = un paso del jugador, en angulos de 90°, igual
        // que el mapa real de Etrian Odyssey. Entre caminos que no estan conectados puede haber
        // celdas "Void": esas se construyen como un BLOQUE SOLIDO real (un volumen de piso a techo),
        // no como una simple pared delgada - asi separan un camino de otro con un bloque de verdad.
        public void Build(DungeonFloor floor, float cellSize, float wallHeight, float wallThickness)
        {
            Clear();
            _root = new GameObject($"Floor_{floor.Index}");
            _root.transform.SetParent(transform, false);

            if (floor.HasBossRoom)
                BuildBossRoomSlab(floor, cellSize, wallHeight);

            for (int x = 0; x < floor.Width; x++)
            {
                for (int y = 0; y < floor.Height; y++)
                {
                    var cell = floor.Cells[x, y];
                    Vector3 center = CellCenter(x, y, cellSize);

                    if (cell.Type == CellType.Void)
                    {
                        BuildVoidBlock(center, cellSize, wallHeight);
                        continue;
                    }

                    bool isIso = cell.IsIsolatedZone;
                    bool isBiome2 = floor.Biome != 0;
                    // Goteras falso (ver DungeonManager.OnPlayerEnterCell): ahi de verdad no hay
                    // piso -- pisarlo te hace caer al piso de abajo, asi que tiene que VERSE como un
                    // hueco real, no como piso normal con una trampa escondida debajo.
                    bool isVisibleVoidHole = cell.IsPuzzleTile && !cell.IsPuzzleTileSafe && floor.LoreCorridorKind == PuzzleKind.Goteras;
                    if (!cell.IsBossRoom)
                    {
                        if (!isVisibleVoidHole) BuildFloorTile(center, cellSize, isIso, isBiome2);
                        BuildCeilingTile(center, cellSize, wallHeight, isIso, isBiome2);
                    }

                    // Pared: se construye una sola vez por borde compartido entre dos celdas reales
                    // (Norte/Este de cada celda). Si el vecino es Void, no hace falta pared propia -
                    // el bloque solido del vacio ya cubre y sella ese borde. Si no hay vecino (borde
                    // del mapa), esta celda es la unica que puede construirla.
                    foreach (var dir in DirectionExtensions.All)
                    {
                        if (!cell.HasWall(dir)) continue;
                        var (ox, oy) = dir.Offset();
                        int nx = x + ox, ny = y + oy;
                        bool outOfBounds = !floor.InBounds(nx, ny);
                        if (outOfBounds)
                        {
                            BuildWall(center, dir, cellSize, wallHeight, wallThickness, isIso);
                            continue;
                        }
                        if (floor.Cells[nx, ny].Type == CellType.Void) continue;

                        bool isPrimaryDir = dir == Direction.North || dir == Direction.East;
                        if (isPrimaryDir)
                            BuildWall(center, dir, cellSize, wallHeight, wallThickness, isIso);
                    }

                    BuildMarker(cell, center, cellSize, wallHeight);
                    if (cell.IsTrapCell && !floor.TrapDisabled) BuildTrapMarker(center, cellSize, floor.TrapKind);
                    if (cell.IsPuzzleTile) BuildPuzzleTile(center, cellSize, cell.IsPuzzleTileSafe, floor.LoreCorridorKind);
                    if (cell.Type == CellType.Lore) BuildLoreSceneDressing(cell, center, cellSize);
                }
            }
        }

        // Marcador de peligro, chato y pegado al piso (no un marcador "de interaccion" como los de
        // BuildMarker), para que el jugador pueda LEER el patron de la sala y decidir si cruzar o
        // no en vez de que sea una sorpresa invisible. Cada TrapKind se ve claramente distinto:
        // ArrowSweep es una placa lisa (la trayectoria de la flecha), SpikeCells son picos
        // sueltos que sobresalen del piso (ver BuildSpikeMarker) -- de un vistazo se nota si el
        // peligro es "una linea que cruza" o "no pises esta celda puntual".
        private void BuildTrapMarker(Vector3 center, float cellSize, TrapKind kind)
        {
            if (kind == TrapKind.ArrowSweep) BuildArrowLineMarker(center, cellSize);
            else BuildSpikeMarker(center, cellSize);
        }

        private void BuildArrowLineMarker(Vector3 center, float cellSize)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TrapMarker";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, 0.03f, 0);
            go.transform.localScale = new Vector3(cellSize * 0.85f, 0.05f, cellSize * 0.85f);

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            ApplyMaterial(go, trapMarkerMaterial, new Color(0.75f, 0.1f, 0.05f));
        }

        // Picos sueltos: un par de cunas oscuras y filosas asomando del piso en vez de la placa
        // lisa de la linea de flechas, asi la sala se lee distinto a primera vista.
        private void BuildSpikeMarker(Vector3 center, float cellSize)
        {
            var offsets = new[] { new Vector2(-0.2f, -0.15f), new Vector2(0.18f, -0.2f), new Vector2(0.02f, 0.2f) };
            foreach (var off in offsets)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "SpikeMarker";
                go.transform.SetParent(_root.transform, false);
                go.transform.position = center + new Vector3(off.x * cellSize, cellSize * 0.13f, off.y * cellSize);
                go.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
                go.transform.localScale = new Vector3(cellSize * 0.16f, cellSize * 0.28f, cellSize * 0.16f);

                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);

                ApplyMaterial(go, trapMarkerMaterial, new Color(0.3f, 0.06f, 0.05f));
            }
        }

        // Celda de una sala de pistas (ver DungeonGenerator.AddLoreCorridorRoom). Brasas/Polvo de
        // Cuarzo siguen ocultos a proposito (NUNCA un color de marcador, solo el comportamiento de
        // la particula distingue piso real de piso falso). Goteras (agua) ya NO es un puzzle oculto
        // -- ver DungeonManager.OnPlayerEnterCell: pisar una celda falsa te hace caer al piso de
        // abajo de verdad, asi que el piso falta directamente (ver Build/BuildFloorTile) y esta
        // particula es solo la guia visual de "por aca cae el agua, por aca NO hay piso".
        private void BuildPuzzleTile(Vector3 center, float cellSize, bool isSafe, PuzzleKind kind)
        {
            bool isVisibleVoid = kind == PuzzleKind.Goteras && !isSafe;
            string name = $"PuzzleTile_{kind}_{(isSafe ? "real" : "falso")}";
            var ps = ParticleLayerFactory.CreateLayer(_root.transform, name);
            ps.transform.position = center + new Vector3(0, cellSize * 1.6f, 0);

            var main = ps.main;
            main.loop = true;
            main.startSpeed = 0f;
            main.startSize = cellSize * (isVisibleVoid ? 0.13f : 0.05f);
            main.gravityModifier = isSafe ? 1.1f : 0.5f;
            main.maxParticles = isVisibleVoid ? 20 : 8;
            // Real: vive lo justo para llegar al piso y chocar (ver collision module abajo, ahi
            // desaparece de verdad, no por vencerse la vida). Falso: nada la frena, asi que necesita
            // vida larga para de verdad "seguir de largo" hasta perderse de la camara en vez de
            // desvanecerse a mitad de caida como antes.
            main.startLifetime = isSafe ? 2.5f : 3.5f;

            Color color = kind switch
            {
                PuzzleKind.Brasas => new Color(1f, 0.5f, 0.15f),
                PuzzleKind.PolvoDeCuarzo => new Color(0.55f, 0.85f, 1f),
                _ => new Color(0.5f, 0.75f, 1f), // Goteras
            };
            // Goteras falso: bien visible (es la guia del camino al tesoro, no un tell sutil que
            // ocultar). Brasas/Polvo de Cuarzo falso: siguen semitransparentes, sutiles a proposito.
            main.startColor = isSafe ? color : (isVisibleVoid ? color : new Color(color.r, color.g, color.b, 0.35f));

            var emission = ps.emission;
            emission.rateOverTime = isSafe ? 2.5f : (isVisibleVoid ? 6f : 1.2f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = cellSize * 0.3f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            // Lo real choca de verdad contra un plano puesto a la altura del piso y desaparece ahi
            // mismo con un mini-splash (ver BuildPuzzleSplashTemplate); lo falso no tiene plano, asi
            // que sigue cayendo de largo sin frenar -- en Brasas/Polvo de Cuarzo es el unico tell
            // real de la sala, en Goteras es ademas coherente con que ahi de verdad no hay piso.
            if (isSafe)
            {
                var floorPlane = new GameObject("PuzzleTileFloorPlane");
                floorPlane.transform.SetParent(_root.transform, false);
                floorPlane.transform.position = center + Vector3.up * 0.05f;
                floorPlane.transform.rotation = Quaternion.identity; // normal hacia +Y (arriba)

                var collision = ps.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.Planes;
                collision.SetPlane(0, floorPlane.transform);
                collision.lifetimeLoss = 1f; // desaparece apenas toca, no se acumula en el piso

                if (_puzzleSplashTemplate == null) _puzzleSplashTemplate = BuildPuzzleSplashTemplate(cellSize);
                ps.subEmitters.AddSubEmitter(_puzzleSplashTemplate, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing, 1f);
            }

            ParticleLayerFactory.Activate(ps);

            // Borde: una segunda capa igual pero mas grande, oscura y detras (mismo shape/emision,
            // sin colision -- cae junto a la principal) -- asoma como un anillo alrededor de cada
            // gota en vez de una silueta lisa, para que se note incluso sobre un piso claro.
            if (isVisibleVoid) BuildPuzzleTileOutline(center, cellSize);
        }

        // Burst chico y unico (no loop) que dispara la particula "real" justo al chocar contra el
        // piso -- unas pocas gotas mas que salen disparadas hacia los costados, el efecto de
        // salpicadura. Una sola instancia (ver _puzzleSplashTemplate) sirve de plantilla para TODAS
        // las celdas reales de la sala, Unity soporta que varios sub-emitters compartan la misma.
        private ParticleSystem BuildPuzzleSplashTemplate(float cellSize)
        {
            var ps = ParticleLayerFactory.CreateLayer(_root.transform, "PuzzleTile_Splash");

            var main = ps.main;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = 0.25f;
            main.startSpeed = cellSize * 1.3f;
            main.startSize = cellSize * 0.06f;
            main.startColor = new Color(1f, 1f, 1f, 0.7f);
            main.gravityModifier = 0.6f;
            main.maxParticles = 16;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 5) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // dispara hacia afuera/arriba desde el punto de choque

            return ps;
        }

        private void BuildPuzzleTileOutline(Vector3 center, float cellSize)
        {
            var ps = ParticleLayerFactory.CreateLayer(_root.transform, "PuzzleTile_Goteras_borde");
            ps.transform.position = center + new Vector3(0, cellSize * 1.6f, 0);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = 3.5f; // misma vida que la capa principal falsa, cae junto a ella
            main.startSpeed = 0f;
            main.startSize = cellSize * 0.19f;
            main.gravityModifier = 0.5f;
            main.maxParticles = 20;
            main.startColor = new Color(0.05f, 0.15f, 0.3f, 0.8f);

            var emission = ps.emission;
            emission.rateOverTime = 6f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = cellSize * 0.3f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sortingOrder = -1; // detras de la capa clara principal

            ParticleLayerFactory.Activate(ps);
        }

        // Puesta en escena de la celda de lore (ver Lore.SceneDressing): busca el fragmento
        // asignado y, si describe un evento con marca fisica, agrega 2-3 props chicos ALREDEDOR del
        // marcador ya construido por BuildMarker -- la idea es que la sala respalde lo que el texto
        // cuenta en vez de que el fragmento sea la unica fuente de la escena. Silencioso si el id no
        // resuelve o el fragmento es None (ver comentario en LoreEntry.cs): no forzar escombros que
        // el texto no sostiene.
        private void BuildLoreSceneDressing(DungeonCell cell, Vector3 center, float cellSize)
        {
            var entry = LoreCatalog.Find(cell.AssignedLoreId);
            if (entry == null) return;

            switch (entry.Dressing)
            {
                case SceneDressing.Scorched: BuildScorchedDressing(center, cellSize); break;
                case SceneDressing.Collapsed: BuildCollapsedDressing(center, cellSize); break;
                case SceneDressing.Ambush: BuildAmbushDressing(center, cellSize); break;
                case SceneDressing.Camp: BuildCampDressing(center, cellSize); break;
                default: return; // None: nada que agregar, a proposito
            }
        }

        // "Un trozo de mapa, quemado en los bordes" (mapa_fragmentado): parches de ceniza chatos
        // pegados al piso alrededor del marcador, como el rastro de un fuego que ya se apago.
        private void BuildScorchedDressing(Vector3 center, float cellSize)
        {
            var offsets = new[] { new Vector2(-0.32f, 0.1f), new Vector2(0.28f, 0.22f), new Vector2(0.05f, -0.35f) };
            foreach (var off in offsets)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "LoreDressing_Ash";
                go.transform.SetParent(_root.transform, false);
                go.transform.position = center + new Vector3(off.x * cellSize, 0.025f, off.y * cellSize);
                go.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                go.transform.localScale = new Vector3(cellSize * 0.22f, 0.03f, cellSize * 0.18f);

                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);

                ApplyMaterial(go, loreDressingMaterial, new Color(0.07f, 0.06f, 0.05f));
            }
        }

        // "Mucho antes de que llegaramos", eco sin firma (voz_en_la_piedra): un par de escombros
        // caidos e irregulares, mas viejos que el resto de la sala -- esto lleva ahi mucho tiempo.
        private void BuildCollapsedDressing(Vector3 center, float cellSize)
        {
            var rubble = new[]
            {
                (pos: new Vector2(-0.3f, -0.22f), scale: 0.22f, rotY: 12f),
                (pos: new Vector2(0.26f, -0.12f), scale: 0.16f, rotY: 50f),
                (pos: new Vector2(0.02f, 0.33f), scale: 0.19f, rotY: 205f),
            };
            foreach (var r in rubble)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "LoreDressing_Rubble";
                go.transform.SetParent(_root.transform, false);
                go.transform.position = center + new Vector3(r.pos.x * cellSize, cellSize * r.scale * 0.5f, r.pos.y * cellSize);
                go.transform.rotation = Quaternion.Euler(UnityEngine.Random.Range(-8f, 8f), r.rotY, UnityEngine.Random.Range(-8f, 8f));
                go.transform.localScale = Vector3.one * cellSize * r.scale;

                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);

                ApplyMaterial(go, loreDressingMaterial, new Color(0.32f, 0.29f, 0.26f));
            }
        }

        // "Golpean donde la formacion esta mas expuesta" (cronicas_guardianes): restos de un
        // combate ya terminado -- una placa quebrada en el piso y un par de esquirlas oscuras, no
        // otra trampa activa (esto ya paso, no es un peligro presente).
        private void BuildAmbushDressing(Vector3 center, float cellSize)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "LoreDressing_BrokenPlate";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(-0.05f * cellSize, 0.03f, 0.28f * cellSize);
            go.transform.rotation = Quaternion.Euler(0f, 20f, 6f);
            go.transform.localScale = new Vector3(cellSize * 0.4f, 0.05f, cellSize * 0.32f);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            ApplyMaterial(go, loreDressingMaterial, new Color(0.35f, 0.14f, 0.1f));

            var shardOffsets = new[] { new Vector2(0.24f, -0.2f), new Vector2(-0.3f, -0.08f) };
            foreach (var off in shardOffsets)
            {
                var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shard.name = "LoreDressing_Shard";
                shard.transform.SetParent(_root.transform, false);
                shard.transform.position = center + new Vector3(off.x * cellSize, cellSize * 0.09f, off.y * cellSize);
                shard.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 35f);
                shard.transform.localScale = new Vector3(cellSize * 0.1f, cellSize * 0.18f, cellSize * 0.05f);
                var shardCol = shard.GetComponent<Collider>();
                if (shardCol != null) Destroy(shardCol);
                ApplyMaterial(shard, loreDressingMaterial, new Color(0.22f, 0.08f, 0.07f));
            }
        }

        // Las 3 pistas de la Puerta Fria (puerta_fria_1/2/3): la misma expedicion acampo en cada
        // punto de su recorrido. Un pozo de fogata apagado + algo parecido a un lugar donde dormir,
        // para que las 3 salas se lean como el mismo camino en vez de tres escenas sin relacion.
        private void BuildCampDressing(Vector3 center, float cellSize)
        {
            var pit = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pit.name = "LoreDressing_FirePit";
            pit.transform.SetParent(_root.transform, false);
            pit.transform.position = center + new Vector3(0.26f * cellSize, 0.02f, -0.24f * cellSize);
            pit.transform.localScale = new Vector3(cellSize * 0.22f, 0.02f, cellSize * 0.22f);
            var pitCol = pit.GetComponent<Collider>();
            if (pitCol != null) Destroy(pitCol);
            ApplyMaterial(pit, loreDressingMaterial, new Color(0.1f, 0.09f, 0.08f));

            var bedroll = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bedroll.name = "LoreDressing_Bedroll";
            bedroll.transform.SetParent(_root.transform, false);
            bedroll.transform.position = center + new Vector3(-0.28f * cellSize, 0.035f, 0.2f * cellSize);
            bedroll.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            bedroll.transform.localScale = new Vector3(cellSize * 0.4f, 0.07f, cellSize * 0.18f);
            var bedrollCol = bedroll.GetComponent<Collider>();
            if (bedrollCol != null) Destroy(bedrollCol);
            ApplyMaterial(bedroll, loreDressingMaterial, new Color(0.3f, 0.24f, 0.15f));
        }

        // Un bloque solido de piso a techo (y un poco mas, para que no se vean costuras) que ocupa
        // toda la celda: esto es lo que separa dos caminos entre si, no una pared delgada.
        private void BuildVoidBlock(Vector3 center, float cellSize, float wallHeight)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "VoidBlock";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, wallHeight / 2f, 0);
            go.transform.localScale = new Vector3(cellSize, wallHeight + 0.4f, cellSize);
            ApplyMaterial(go, voidBlockMaterial, new Color(0.08f, 0.08f, 0.09f));
        }

        public Vector3 CellCenter(int x, int y, float cellSize) => new Vector3(x * cellSize, 0f, y * cellSize);

        private void BuildFloorTile(Vector3 center, float cellSize, bool isIso, bool isBiome2)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Floor";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, -0.1f, 0);
            go.transform.localScale = new Vector3(cellSize, 0.2f, cellSize);
            if (isBiome2)
                ApplyMaterial(go, biome2FloorMaterial, new Color(0.05f, 0.05f, 0.08f));
            else
                ApplyMaterial(go,
                    isIso ? isoFloorMaterial : floorMaterial,
                    isIso ? new Color(0.24f, 0.17f, 0.30f) : new Color(0.35f, 0.35f, 0.38f));
        }

        private void BuildCeilingTile(Vector3 center, float cellSize, float wallHeight, bool isIso, bool isBiome2)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ceiling";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, wallHeight + 0.1f, 0);
            go.transform.localScale = new Vector3(cellSize, 0.2f, cellSize);
            if (isBiome2)
                ApplyMaterial(go, biome2CeilingMaterial, new Color(0.02f, 0.02f, 0.07f));
            else
                ApplyMaterial(go,
                    isIso ? isoCeilingMaterial : ceilingMaterial,
                    isIso ? new Color(0.12f, 0.08f, 0.16f) : new Color(0.15f, 0.15f, 0.17f));
        }

        // Aura sutil de piso alrededor de la escalera (Custom/StairsAura, ver Assets/Shaders): un
        // quad chato y grande sobre el piso (radio de unas 3 celdas), asi el jugador nota que hay
        // una escalera cerca desde un pasillo recto ANTES de estar encima -- pero solo por linea de
        // vista real, las paredes la ocluyen como a cualquier geometria (nada de "brillar a traves
        // de la pared"). No se crea nada si no hay material asignado (sin fallback de color solido:
        // un parche opaco se veria peor que no tener aura).
        private void BuildStairsFloorAura(Vector3 center, float cellSize, bool goingUp)
        {
            if (stairsAuraMaterial == null) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "StairsFloorAura";
            go.transform.SetParent(_root.transform, false);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            const float radiusInCells = 3f;
            float diameter = cellSize * radiusInCells * 2f;
            go.transform.position = center + new Vector3(0, 0.02f, 0);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(diameter, diameter, 1f);

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = stairsAuraMaterial;
            var props = new MaterialPropertyBlock();
            props.SetColor("_Color", goingUp ? new Color(0.35f, 1f, 1f, 1f) : new Color(1f, 0.55f, 0.1f, 1f));
            renderer.SetPropertyBlock(props);
        }

        // Un unico piso/techo grande que cubre todo el rectangulo de la sala de jefe, para que se
        // vea como una sala espaciosa continua en vez de celdas individuales.
        private void BuildBossRoomSlab(DungeonFloor floor, float cellSize, float wallHeight)
        {
            Vector3 minCenter = CellCenter(floor.BossRoomMinX, floor.BossRoomMinY, cellSize);
            Vector3 maxCenter = CellCenter(floor.BossRoomMaxX, floor.BossRoomMaxY, cellSize);
            float sizeX = (maxCenter.x - minCenter.x) + cellSize;
            float sizeZ = (maxCenter.z - minCenter.z) + cellSize;
            Vector3 roomCenter = new Vector3((minCenter.x + maxCenter.x) / 2f, 0f, (minCenter.z + maxCenter.z) / 2f);

            bool isBiome2 = floor.Biome != 0;

            var floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floorGo.name = "BossRoomFloor";
            floorGo.transform.SetParent(_root.transform, false);
            floorGo.transform.position = roomCenter + new Vector3(0, -0.1f, 0);
            floorGo.transform.localScale = new Vector3(sizeX, 0.2f, sizeZ);
            if (isBiome2)
                ApplyMaterial(floorGo, biome2FloorMaterial, new Color(0.05f, 0.05f, 0.08f));
            else
                ApplyMaterial(floorGo, bossRoomFloorMaterial, new Color(0.45f, 0.14f, 0.14f));

            var ceilGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceilGo.name = "BossRoomCeiling";
            ceilGo.transform.SetParent(_root.transform, false);
            ceilGo.transform.position = roomCenter + new Vector3(0, wallHeight + 0.1f, 0);
            ceilGo.transform.localScale = new Vector3(sizeX, 0.2f, sizeZ);
            if (isBiome2)
                ApplyMaterial(ceilGo, biome2CeilingMaterial, new Color(0.02f, 0.02f, 0.07f));
            else
                ApplyMaterial(ceilGo, bossRoomCeilingMaterial, new Color(0.2f, 0.08f, 0.08f));
        }

        // Pared compartida entre esta celda y su vecina: la linea que las divide, exactamente en el
        // borde de cellSize (igual que el mapa real de Etrian Odyssey, sin vacio fisico). La pared
        // de un atajo NUNCA se destruye: el vacio entre el switch y el punto de llegada se cruza por
        // teletransporte, no caminando.
        private void BuildWall(Vector3 cellCenter, Direction dir, float cellSize, float wallHeight, float wallThickness, bool isIso)
        {
            var (ox, oy) = dir.Offset();
            Vector3 edgeOffset = new Vector3(ox, 0, oy) * (cellSize / 2f);
            Vector3 pos = cellCenter + edgeOffset + new Vector3(0, wallHeight / 2f, 0);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Wall";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = pos;

            bool horizontalWall = (dir == Direction.North || dir == Direction.South);
            go.transform.localScale = horizontalWall
                ? new Vector3(cellSize, wallHeight, wallThickness)
                : new Vector3(wallThickness, wallHeight, cellSize);

            ApplyMaterial(go,
                isIso ? isoWallMaterial : wallMaterial,
                isIso ? new Color(0.4f, 0.32f, 0.48f) : new Color(0.5f, 0.45f, 0.4f));
        }

        private void BuildMarker(DungeonCell cell, Vector3 center, float cellSize, float wallHeight)
        {
            Color color;
            Material mat = null;
            PrimitiveType shape = PrimitiveType.Sphere;
            float scale = cellSize * 0.25f;

            switch (cell.Type)
            {
                case CellType.Start: color = Color.green; mat = startMarkerMaterial; break;
                case CellType.End: color = Color.red; mat = endMarkerMaterial; break;
                case CellType.SecondaryQuest: color = Color.yellow; mat = questMarkerMaterial; break;
                case CellType.ShortcutSwitch: color = new Color(0.2f, 0.4f, 1f); mat = switchMarkerMaterial; shape = PrimitiveType.Cylinder; break;
                case CellType.ShortcutLanding: color = new Color(0.85f, 0.45f, 0.1f); mat = landingMarkerMaterial; shape = PrimitiveType.Cylinder; break;
                case CellType.StairsUp: color = Color.cyan; mat = stairsUpMaterial; shape = PrimitiveType.Cube; break;
                case CellType.StairsDown: color = new Color(1f, 0.5f, 0f); mat = stairsDownMaterial; shape = PrimitiveType.Cube; break;
                case CellType.Boss: color = new Color(0.7f, 0f, 0.05f); mat = bossMarkerMaterial; shape = PrimitiveType.Capsule; scale = cellSize * 0.55f; break;
                case CellType.Lore:
                    // Las 3 pistas de la Puerta Fria (ver DungeonGenerator.BiomeGateLoreIds) se ven
                    // distintas -- dorado en vez de violeta, y mas grandes -- para que se lean como
                    // mecanicamente importantes apenas aparecen en pantalla, no solo sabor.
                    bool isMysteryClue = System.Array.IndexOf(DungeonGenerator.BiomeGateLoreIds, cell.AssignedLoreId) >= 0;
                    color = isMysteryClue ? new Color(1f, 0.82f, 0.25f) : new Color(0.75f, 0.35f, 1f);
                    mat = loreMarkerMaterial;
                    shape = PrimitiveType.Sphere;
                    scale = isMysteryClue ? cellSize * 0.42f : cellSize * 0.3f;
                    break;
                case CellType.LockedDoor: color = new Color(0.55f, 0.1f, 0.1f); mat = lockedDoorMarkerMaterial; shape = PrimitiveType.Cube; scale = cellSize * 0.5f; break;
                case CellType.Lever: color = new Color(0.15f, 0.9f, 0.35f); mat = leverMarkerMaterial; shape = PrimitiveType.Cylinder; scale = cellSize * 0.3f; break;
                case CellType.Treasure: color = new Color(1f, 0.82f, 0.1f); mat = treasureMarkerMaterial; shape = PrimitiveType.Cube; scale = cellSize * 0.35f; break;
                // Sin material dedicado a proposito (ver CellType.BiomeGate): no hace falta tocar
                // DungeonSceneBuilder por esto, el color de respaldo alcanza para el brillo frio
                // que se supone que tiene la grieta recien abierta.
                case CellType.BiomeGate: color = new Color(0.55f, 0.85f, 1f); shape = PrimitiveType.Sphere; scale = cellSize * 0.35f; break;
                default: return;
            }

            var go = GameObject.CreatePrimitive(shape);
            go.name = $"Marker_{cell.Type}";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, scale * 0.5f, 0);
            go.transform.localScale = Vector3.one * scale;

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            ApplyMaterial(go, mat, color);

            if (cell.Type == CellType.StairsUp || cell.Type == CellType.StairsDown)
            {
                var beaconGo = new GameObject("StairsBeacon");
                beaconGo.transform.SetParent(_root.transform, false);
                beaconGo.transform.position = center;
                beaconGo.AddComponent<StairsBeacon>().Configure(cell.Type == CellType.StairsUp, cellSize, wallHeight);

                BuildStairsFloorAura(center, cellSize, cell.Type == CellType.StairsUp);
            }

            if (cell.Type == CellType.LockedDoor)
                _lockedDoorMarkers[(cell.X, cell.Y)] = go;

            if (cell.Type == CellType.ShortcutSwitch || cell.Type == CellType.ShortcutLanding)
            {
                var entry = _shortcutMarkers.TryGetValue(cell.ControlledGateIndex, out var pair) ? pair : (null, null);
                if (cell.Type == CellType.ShortcutSwitch) entry.switchGo = go; else entry.landingGo = go;
                _shortcutMarkers[cell.ControlledGateIndex] = entry;
            }
        }

        private void ApplyMaterial(GameObject go, Material mat, Color fallbackColor)
        {
            var renderer = go.GetComponent<Renderer>();
            if (mat != null)
            {
                renderer.sharedMaterial = mat;
            }
            else
            {
                var instanced = new Material(Shader.Find("Standard"));
                instanced.color = fallbackColor;
                renderer.material = instanced;
            }
        }

        // Al activar el atajo, los dos marcadores (switch y llegada) cambian a un color brillante
        // compartido para que se note que ya se puede teletransportar entre ambos.
        public void ActivateShortcutVisual(int gateIndex)
        {
            if (!_shortcutMarkers.TryGetValue(gateIndex, out var pair)) return;
            var activeColor = new Color(1f, 0.95f, 0.2f);
            if (pair.switchGo != null) ApplyMaterial(pair.switchGo, null, activeColor);
            if (pair.landingGo != null) ApplyMaterial(pair.landingGo, null, activeColor);
        }

        // Al activar la palanca, el marcador de la puerta bloqueada pasa de rojo (cerrada) a un
        // verde apagado (abierta para siempre), para que quede claro de un vistazo que ese candado
        // ya no bloquea nada en esta run.
        public void UnlockDoorVisual(int doorX, int doorY)
        {
            if (!_lockedDoorMarkers.TryGetValue((doorX, doorY), out var go) || go == null) return;
            ApplyMaterial(go, null, new Color(0.2f, 0.55f, 0.25f));
        }
    }
}
