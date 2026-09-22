using System.Collections.Generic;
using UnityEngine;
using DungeonGen;

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
        public Material eventMarkerMaterial;
        public Material bossMarkerMaterial;
        public Material loreMarkerMaterial;
        public Material bossRoomFloorMaterial;
        public Material bossRoomCeilingMaterial;
        public Material voidBlockMaterial;
        public Material lockedDoorMarkerMaterial;
        public Material leverMarkerMaterial;
        public Material treasureMarkerMaterial;
        public Material trapMarkerMaterial;

        // Tinte distinto (piso/pared/techo) para la zona aislada de cada piso: en BotW un mundo con
        // regiones visualmente distinguibles hace que el jugador arme su propio mapa mental
        // caminando, en vez de depender todo el tiempo del automapa -- esta zona ya tenia su color
        // propio en el minimapa (MinimapUI.FloorColor), esto lo lleva tambien a la vista en 3D.
        public Material isoFloorMaterial;
        public Material isoCeilingMaterial;
        public Material isoWallMaterial;

        private GameObject _root;
        private readonly Dictionary<int, (GameObject switchGo, GameObject landingGo)> _shortcutMarkers = new Dictionary<int, (GameObject, GameObject)>();
        private readonly Dictionary<(int x, int y), GameObject> _lockedDoorMarkers = new Dictionary<(int, int), GameObject>();

        public void Clear()
        {
            if (_root != null) Destroy(_root);
            _shortcutMarkers.Clear();
            _lockedDoorMarkers.Clear();
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
                    if (!cell.IsBossRoom)
                    {
                        BuildFloorTile(center, cellSize, isIso);
                        BuildCeilingTile(center, cellSize, wallHeight, isIso);
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

        private void BuildFloorTile(Vector3 center, float cellSize, bool isIso)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Floor";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, -0.1f, 0);
            go.transform.localScale = new Vector3(cellSize, 0.2f, cellSize);
            ApplyMaterial(go,
                isIso ? isoFloorMaterial : floorMaterial,
                isIso ? new Color(0.24f, 0.17f, 0.30f) : new Color(0.35f, 0.35f, 0.38f));
        }

        private void BuildCeilingTile(Vector3 center, float cellSize, float wallHeight, bool isIso)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ceiling";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, wallHeight + 0.1f, 0);
            go.transform.localScale = new Vector3(cellSize, 0.2f, cellSize);
            ApplyMaterial(go,
                isIso ? isoCeilingMaterial : ceilingMaterial,
                isIso ? new Color(0.12f, 0.08f, 0.16f) : new Color(0.15f, 0.15f, 0.17f));
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

            var floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floorGo.name = "BossRoomFloor";
            floorGo.transform.SetParent(_root.transform, false);
            floorGo.transform.position = roomCenter + new Vector3(0, -0.1f, 0);
            floorGo.transform.localScale = new Vector3(sizeX, 0.2f, sizeZ);
            ApplyMaterial(floorGo, bossRoomFloorMaterial, new Color(0.45f, 0.14f, 0.14f));

            var ceilGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceilGo.name = "BossRoomCeiling";
            ceilGo.transform.SetParent(_root.transform, false);
            ceilGo.transform.position = roomCenter + new Vector3(0, wallHeight + 0.1f, 0);
            ceilGo.transform.localScale = new Vector3(sizeX, 0.2f, sizeZ);
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
                case CellType.Event: color = Color.white; mat = eventMarkerMaterial; shape = PrimitiveType.Cylinder; break;
                case CellType.Boss: color = new Color(0.7f, 0f, 0.05f); mat = bossMarkerMaterial; shape = PrimitiveType.Capsule; scale = cellSize * 0.55f; break;
                case CellType.Lore: color = new Color(0.75f, 0.35f, 1f); mat = loreMarkerMaterial; shape = PrimitiveType.Sphere; scale = cellSize * 0.3f; break;
                case CellType.LockedDoor: color = new Color(0.55f, 0.1f, 0.1f); mat = lockedDoorMarkerMaterial; shape = PrimitiveType.Cube; scale = cellSize * 0.5f; break;
                case CellType.Lever: color = new Color(0.15f, 0.9f, 0.35f); mat = leverMarkerMaterial; shape = PrimitiveType.Cylinder; scale = cellSize * 0.3f; break;
                case CellType.Treasure: color = new Color(1f, 0.82f, 0.1f); mat = treasureMarkerMaterial; shape = PrimitiveType.Cube; scale = cellSize * 0.35f; break;
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
