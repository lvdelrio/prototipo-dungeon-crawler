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

        // Una celda logica = una distancia cellSize = un paso del jugador, en angulos de 90°, igual
        // que el mapa real de Etrian Odyssey: cada casilla es un cuadrado completo de piso/techo, y
        // las paredes son simplemente la linea que divide una casilla de la vecina (un unico objeto
        // compartido en el borde, no un vacio fisico entre caminos).
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

                    if (!cell.IsBossRoom)
                    {
                        BuildFloorTile(center, cellSize);
                        BuildCeilingTile(center, cellSize, wallHeight);
                    }

                    // Pared compartida: se construye una sola vez por borde (Norte/Este de cada
                    // celda, mas Sur/Oeste solo en el borde del mapa) para no duplicar geometria.
                    if (cell.HasWall(Direction.North))
                        BuildWall(center, Direction.North, cellSize, wallHeight, wallThickness);
                    if (cell.HasWall(Direction.East))
                        BuildWall(center, Direction.East, cellSize, wallHeight, wallThickness);
                    if (y == 0 && cell.HasWall(Direction.South))
                        BuildWall(center, Direction.South, cellSize, wallHeight, wallThickness);
                    if (x == 0 && cell.HasWall(Direction.West))
                        BuildWall(center, Direction.West, cellSize, wallHeight, wallThickness);

                    BuildMarker(cell, center, cellSize);
                }
            }
        }

        public Vector3 CellCenter(int x, int y, float cellSize) => new Vector3(x * cellSize, 0f, y * cellSize);

        private void BuildFloorTile(Vector3 center, float cellSize)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Floor";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, -0.1f, 0);
            go.transform.localScale = new Vector3(cellSize, 0.2f, cellSize);
            ApplyMaterial(go, floorMaterial, new Color(0.35f, 0.35f, 0.38f));
        }

        private void BuildCeilingTile(Vector3 center, float cellSize, float wallHeight)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ceiling";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, wallHeight + 0.1f, 0);
            go.transform.localScale = new Vector3(cellSize, 0.2f, cellSize);
            ApplyMaterial(go, ceilingMaterial, new Color(0.15f, 0.15f, 0.17f));
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
        private void BuildWall(Vector3 cellCenter, Direction dir, float cellSize, float wallHeight, float wallThickness)
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
