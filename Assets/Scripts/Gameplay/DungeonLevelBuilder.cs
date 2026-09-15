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
        public Material bossRoomFloorMaterial;
        public Material bossRoomCeilingMaterial;

        private GameObject _root;
        private readonly Dictionary<int, (GameObject switchGo, GameObject landingGo)> _shortcutMarkers = new Dictionary<int, (GameObject, GameObject)>();

        public void Clear()
        {
            if (_root != null) Destroy(_root);
            _shortcutMarkers.Clear();
        }

        // Una celda logica sigue siendo una distancia cellSize = un paso del jugador (eso NO cambia).
        // Pero cada celda ahora es "dueña" de su propio pedazo de piso y de sus propias paredes,
        // mas angosto que cellSize (pathWidthFraction): dos caminos que no estan conectados entre si
        // JAMAS comparten la misma pared - cada uno tiene la suya, con un vacio real en el medio.
        // Cuando SI hay paso, se construye un puente que rellena exactamente ese vacio.
        public void Build(DungeonFloor floor, float cellSize, float wallHeight, float wallThickness, float pathWidthFraction = 0.6f)
        {
            Clear();
            _root = new GameObject($"Floor_{floor.Index}");
            _root.transform.SetParent(transform, false);

            float pathWidth = Mathf.Clamp(cellSize * pathWidthFraction, 0.5f, cellSize);

            if (floor.HasBossRoom)
                BuildBossRoomSlab(floor, cellSize, wallHeight);

            for (int x = 0; x < floor.Width; x++)
            {
                for (int y = 0; y < floor.Height; y++)
                {
                    var cell = floor.Cells[x, y];
                    Vector3 center = CellCenter(x, y, cellSize);

                    if (!cell.IsBossRoom)
                    {
                        BuildFloorTile(center, pathWidth);
                        BuildCeilingTile(center, pathWidth, wallHeight);
                    }

                    // Cada celda construye sus propias 4 paredes (ninguna se comparte con la vecina):
                    // asi dos caminos sin conexion quedan separados por un vacio real, no por una
                    // unica pared en el medio.
                    foreach (var dir in DirectionExtensions.All)
                    {
                        if (cell.HasWall(dir))
                            BuildWall(center, dir, pathWidth, wallHeight, wallThickness);
                    }

                    // Puentes: solo se procesan desde Norte/Este para no duplicar el mismo puente por
                    // cada par de celdas, y se saltan si ambos lados son parte de la misma sala de
                    // jefe (ya cubierta por el slab grande).
                    if (!cell.HasWall(Direction.North) && !BothInsideSameBossRoom(floor, x, y, x, y + 1))
                        BuildBridge(center, Direction.North, cellSize, pathWidth, wallHeight);
                    if (!cell.HasWall(Direction.East) && !BothInsideSameBossRoom(floor, x, y, x + 1, y))
                        BuildBridge(center, Direction.East, cellSize, pathWidth, wallHeight);

                    BuildMarker(cell, center, cellSize);
                }
            }
        }

        private bool BothInsideSameBossRoom(DungeonFloor floor, int ax, int ay, int bx, int by)
        {
            if (!floor.InBounds(bx, by)) return false;
            return floor.Cells[ax, ay].IsBossRoom && floor.Cells[bx, by].IsBossRoom;
        }

        public Vector3 CellCenter(int x, int y, float cellSize) => new Vector3(x * cellSize, 0f, y * cellSize);

        private void BuildFloorTile(Vector3 center, float pathWidth)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Floor";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, -0.1f, 0);
            go.transform.localScale = new Vector3(pathWidth, 0.2f, pathWidth);
            ApplyMaterial(go, floorMaterial, new Color(0.35f, 0.35f, 0.38f));
        }

        private void BuildCeilingTile(Vector3 center, float pathWidth, float wallHeight)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ceiling";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, wallHeight + 0.1f, 0);
            go.transform.localScale = new Vector3(pathWidth, 0.2f, pathWidth);
            ApplyMaterial(go, ceilingMaterial, new Color(0.15f, 0.15f, 0.17f));
        }

        // Rellena, solo cuando hay paso, el vacio entre el borde del camino de esta celda y el de la
        // vecina (ambos a pathWidth, dentro de la MISMA distancia cellSize - el paso del jugador no
        // cambia). Cuando no hay paso, ese vacio queda vacio de verdad: ninguna de las dos celdas
        // construye nada ahi, cada una se queda solo con su propia pared.
        private void BuildBridge(Vector3 center, Direction dir, float cellSize, float pathWidth, float wallHeight)
        {
            float gapLength = cellSize - pathWidth;
            if (gapLength <= 0.001f) return;

            var (ox, oy) = dir.Offset();
            Vector3 bridgeCenter = center + new Vector3(ox, 0, oy) * (cellSize / 2f);
            bool northSouth = (dir == Direction.North || dir == Direction.South);
            Vector3 scaleXZ = northSouth
                ? new Vector3(pathWidth, 0.2f, gapLength)
                : new Vector3(gapLength, 0.2f, pathWidth);

            var floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floorGo.name = "FloorBridge";
            floorGo.transform.SetParent(_root.transform, false);
            floorGo.transform.position = bridgeCenter + new Vector3(0, -0.1f, 0);
            floorGo.transform.localScale = scaleXZ;
            ApplyMaterial(floorGo, floorMaterial, new Color(0.35f, 0.35f, 0.38f));

            var ceilGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceilGo.name = "CeilingBridge";
            ceilGo.transform.SetParent(_root.transform, false);
            ceilGo.transform.position = bridgeCenter + new Vector3(0, wallHeight + 0.1f, 0);
            ceilGo.transform.localScale = scaleXZ;
            ApplyMaterial(ceilGo, ceilingMaterial, new Color(0.15f, 0.15f, 0.17f));
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

        // Pared propia de ESTA celda unicamente (nunca se comparte con la vecina): se ubica en el
        // borde de su propio ancho angosto (pathWidth), no en el borde de cellSize. Por eso dos
        // caminos sin conexion quedan cada uno con su propia pared, separados por un vacio real,
        // en vez de estar "pegados" con una unica pared en el medio. La pared de un atajo NUNCA se
        // destruye: el vacio entre el switch y el punto de llegada se cruza por teletransporte.
        private void BuildWall(Vector3 cellCenter, Direction dir, float pathWidth, float wallHeight, float wallThickness)
        {
            var (ox, oy) = dir.Offset();
            Vector3 edgeOffset = new Vector3(ox, 0, oy) * (pathWidth / 2f);
            Vector3 pos = cellCenter + edgeOffset + new Vector3(0, wallHeight / 2f, 0);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Wall";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = pos;

            bool horizontalWall = (dir == Direction.North || dir == Direction.South);
            go.transform.localScale = horizontalWall
                ? new Vector3(pathWidth, wallHeight, wallThickness)
                : new Vector3(wallThickness, wallHeight, pathWidth);

            ApplyMaterial(go, wallMaterial, new Color(0.5f, 0.45f, 0.4f));
        }

        private void BuildMarker(DungeonCell cell, Vector3 center, float cellSize)
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
    }
}
