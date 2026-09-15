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
        public Material gateWallMaterial;
        public Material startMarkerMaterial;
        public Material endMarkerMaterial;
        public Material questMarkerMaterial;
        public Material switchMarkerMaterial;
        public Material stairsUpMaterial;
        public Material stairsDownMaterial;
        public Material eventMarkerMaterial;
        public Material bossMarkerMaterial;
        public Material bossRoomFloorMaterial;
        public Material bossRoomCeilingMaterial;

        private GameObject _root;
        private readonly Dictionary<int, GameObject> _gateWallObjects = new Dictionary<int, GameObject>();

        public void Clear()
        {
            if (_root != null) Destroy(_root);
            _gateWallObjects.Clear();
        }

        public void Build(DungeonFloor floor, float cellSize, float wallHeight, float wallThickness, float corridorWidthFraction = 0.6f, float corridorGapMultiplier = 1f)
        {
            Clear();
            _root = new GameObject($"Floor_{floor.Index}");
            _root.transform.SetParent(transform, false);

            float corridorWidth = Mathf.Clamp(cellSize * corridorWidthFraction, 0.3f, cellSize);
            float gapSize = cellSize * Mathf.Max(0.05f, corridorGapMultiplier);
            float spacing = cellSize + gapSize;

            if (floor.HasBossRoom)
                BuildBossRoomSlab(floor, cellSize, spacing, wallHeight);

            for (int x = 0; x < floor.Width; x++)
            {
                for (int y = 0; y < floor.Height; y++)
                {
                    var cell = floor.Cells[x, y];
                    Vector3 center = CellCenter(x, y, spacing);

                    // Las celdas dentro de una sala de jefe ya quedan cubiertas por el slab grande
                    // construido arriba: no se les agrega su propio piso/techo individual.
                    if (!cell.IsBossRoom)
                    {
                        BuildFloorPad(center, corridorWidth);
                        BuildCeilingPad(center, corridorWidth, wallHeight);
                    }

                    // Cada celda sella sus propios lados con pared: como ahora hay un hueco real
                    // (spacing > cellSize) entre celdas vecinas, ya no hace falta deduplicar - cada
                    // pared es un objeto fisicamente separado en el borde de su propia celda.
                    foreach (var dir in DirectionExtensions.All)
                    {
                        if (cell.HasWall(dir))
                            BuildWall(floor, x, y, dir, center, cellSize, wallHeight, wallThickness);
                    }

                    // Puentes: solo se procesan desde Norte/Este para no duplicar el mismo puente
                    // por cada par de celdas, y se saltan cuando ambos lados pertenecen a la misma
                    // sala de jefe (ya cubierta por el slab grande).
                    if (!cell.HasWall(Direction.North) && !BothInsideSameBossRoom(floor, x, y, x, y + 1))
                        BuildBridge(center, Direction.North, spacing, cellSize, corridorWidth, wallHeight);
                    if (!cell.HasWall(Direction.East) && !BothInsideSameBossRoom(floor, x, y, x + 1, y))
                        BuildBridge(center, Direction.East, spacing, cellSize, corridorWidth, wallHeight);

                    BuildMarker(cell, center, cellSize);
                }
            }
        }

        private bool BothInsideSameBossRoom(DungeonFloor floor, int ax, int ay, int bx, int by)
        {
            if (!floor.InBounds(bx, by)) return false;
            var a = floor.Cells[ax, ay];
            var b = floor.Cells[bx, by];
            return a.IsBossRoom && b.IsBossRoom;
        }

        public Vector3 CellCenter(int x, int y, float spacing) => new Vector3(x * spacing, 0f, y * spacing);

        private void BuildFloorPad(Vector3 center, float corridorWidth)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Floor";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, -0.1f, 0);
            go.transform.localScale = new Vector3(corridorWidth, 0.2f, corridorWidth);
            ApplyMaterial(go, floorMaterial, new Color(0.35f, 0.35f, 0.38f));
        }

        private void BuildCeilingPad(Vector3 center, float corridorWidth, float wallHeight)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ceiling";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, wallHeight + 0.1f, 0);
            go.transform.localScale = new Vector3(corridorWidth, 0.2f, corridorWidth);
            ApplyMaterial(go, ceilingMaterial, new Color(0.15f, 0.15f, 0.17f));
        }

        // Un unico piso/techo grande que cubre todo el rectangulo de la sala de jefe (incluyendo
        // los huecos internos entre sus celdas), para que se vea como una sala espaciosa continua
        // en vez de varias celdas conectadas por puentes angostos.
        private void BuildBossRoomSlab(DungeonFloor floor, float cellSize, float spacing, float wallHeight)
        {
            Vector3 minCenter = CellCenter(floor.BossRoomMinX, floor.BossRoomMinY, spacing);
            Vector3 maxCenter = CellCenter(floor.BossRoomMaxX, floor.BossRoomMaxY, spacing);
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

        // Rellena, solo cuando hay paso abierto, el hueco entre el "pad" caminable de esta celda
        // y el de la celda vecina. Cuando no hay paso (pared), ese hueco queda vacio a proposito:
        // eso es lo que separa dos pasillos paralelos con un espacio real en vez de solo una pared delgada.
        private void BuildBridge(Vector3 center, Direction dir, float spacing, float cellSize, float corridorWidth, float wallHeight)
        {
            float gapLength = spacing - cellSize;
            if (gapLength <= 0.001f) return;

            var (ox, oy) = dir.Offset();
            Vector3 bridgeCenter = center + new Vector3(ox, 0, oy) * (spacing / 2f);
            bool northSouth = (dir == Direction.North || dir == Direction.South);
            Vector3 scaleXZ = northSouth
                ? new Vector3(corridorWidth, 0.2f, gapLength)
                : new Vector3(gapLength, 0.2f, corridorWidth);

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

        private void BuildWall(DungeonFloor floor, int x, int y, Direction dir, Vector3 cellCenter, float cellSize, float wallHeight, float wallThickness)
        {
            var (ox, oy) = dir.Offset();
            Vector3 edgeOffset = new Vector3(ox, 0, oy) * (cellSize / 2f);
            Vector3 pos = cellCenter + edgeOffset + new Vector3(0, wallHeight / 2f, 0);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(_root.transform, false);
            go.transform.position = pos;

            bool horizontalWall = (dir == Direction.North || dir == Direction.South);
            go.transform.localScale = horizontalWall
                ? new Vector3(cellSize, wallHeight, wallThickness)
                : new Vector3(wallThickness, wallHeight, cellSize);

            bool isGate = false;
            int gateIndex = -1;
            for (int gi = 0; gi < floor.Gates.Count; gi++)
            {
                var g = floor.Gates[gi];
                bool matchesA = (g.Ax == x && g.Ay == y && g.DirFromA == dir);
                bool matchesB = (g.Bx == x && g.By == y && g.DirFromA.Opposite() == dir);
                if (matchesA || matchesB) { isGate = true; gateIndex = gi; break; }
            }

            if (isGate)
            {
                go.name = $"GateWall_{gateIndex}";
                ApplyMaterial(go, gateWallMaterial, new Color(0.6f, 0.2f, 0.75f));
                _gateWallObjects[gateIndex] = go;
            }
            else
            {
                go.name = "Wall";
                ApplyMaterial(go, wallMaterial, new Color(0.5f, 0.45f, 0.4f));
            }
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

        public void OpenGateVisual(int gateIndex)
        {
            if (_gateWallObjects.TryGetValue(gateIndex, out var go) && go != null)
            {
                Destroy(go);
                _gateWallObjects.Remove(gateIndex);
            }
        }
    }
}
