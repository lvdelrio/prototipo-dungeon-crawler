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

        private GameObject _root;
        private readonly Dictionary<int, GameObject> _gateWallObjects = new Dictionary<int, GameObject>();

        public void Clear()
        {
            if (_root != null) Destroy(_root);
            _gateWallObjects.Clear();
        }

        public void Build(DungeonFloor floor, float cellSize, float wallHeight, float wallThickness)
        {
            Clear();
            _root = new GameObject($"Floor_{floor.Index}");
            _root.transform.SetParent(transform, false);

            for (int x = 0; x < floor.Width; x++)
            {
                for (int y = 0; y < floor.Height; y++)
                {
                    var cell = floor.Cells[x, y];
                    Vector3 center = CellCenter(x, y, cellSize);

                    BuildFloorTile(center, cellSize);
                    BuildCeilingTile(center, cellSize, wallHeight);

                    if (cell.HasWall(Direction.North))
                        BuildWall(floor, x, y, Direction.North, center, cellSize, wallHeight, wallThickness);
                    if (cell.HasWall(Direction.East))
                        BuildWall(floor, x, y, Direction.East, center, cellSize, wallHeight, wallThickness);
                    if (y == 0 && cell.HasWall(Direction.South))
                        BuildWall(floor, x, y, Direction.South, center, cellSize, wallHeight, wallThickness);
                    if (x == 0 && cell.HasWall(Direction.West))
                        BuildWall(floor, x, y, Direction.West, center, cellSize, wallHeight, wallThickness);

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

            switch (cell.Type)
            {
                case CellType.Start: color = Color.green; mat = startMarkerMaterial; break;
                case CellType.End: color = Color.red; mat = endMarkerMaterial; break;
                case CellType.SecondaryQuest: color = Color.yellow; mat = questMarkerMaterial; break;
                case CellType.ShortcutSwitch: color = new Color(0.2f, 0.4f, 1f); mat = switchMarkerMaterial; shape = PrimitiveType.Cylinder; break;
                case CellType.StairsUp: color = Color.cyan; mat = stairsUpMaterial; shape = PrimitiveType.Cube; break;
                case CellType.StairsDown: color = new Color(1f, 0.5f, 0f); mat = stairsDownMaterial; shape = PrimitiveType.Cube; break;
                case CellType.Event: color = Color.white; mat = eventMarkerMaterial; shape = PrimitiveType.Cylinder; break;
                default: return;
            }

            var go = GameObject.CreatePrimitive(shape);
            go.name = $"Marker_{cell.Type}";
            go.transform.SetParent(_root.transform, false);
            go.transform.position = center + new Vector3(0, 0.6f, 0);
            go.transform.localScale = Vector3.one * (cellSize * 0.25f);

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
