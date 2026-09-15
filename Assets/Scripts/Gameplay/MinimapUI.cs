using UnityEngine;
using DungeonGen;

namespace Gameplay
{
    public class MinimapUI : MonoBehaviour
    {
        public DungeonManager dungeonManager;
        public GridPlayerController player;

        public int panelX = 500;
        public int panelY = 10;
        public int cellPixelSize = 16;
        public int wallPixelThickness = 2;

        private static Texture2D _whiteTex;

        void OnGUI()
        {
            if (dungeonManager == null || player == null) return;
            var floor = dungeonManager.CurrentFloor;
            if (floor == null) return;

            int w = floor.Width;
            int h = floor.Height;
            float top = panelY + 24;

            GUI.Box(new Rect(panelX - 10, panelY - 10, w * cellPixelSize + 20, h * cellPixelSize + 44), "");
            GUI.Label(new Rect(panelX, panelY, w * cellPixelSize, 20), $"Mapa - Piso {dungeonManager.CurrentFloorIndex}");

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    var cell = floor.Cells[x, y];
                    float px = panelX + x * cellPixelSize;
                    float py = top + (h - 1 - y) * cellPixelSize;

                    DrawRect(new Rect(px, py, cellPixelSize, cellPixelSize), FloorColor(cell));

                    if (cell.HasWall(Direction.North))
                        DrawRect(new Rect(px, py, cellPixelSize, wallPixelThickness), Color.black);
                    if (cell.HasWall(Direction.South))
                        DrawRect(new Rect(px, py + cellPixelSize - wallPixelThickness, cellPixelSize, wallPixelThickness), Color.black);
                    if (cell.HasWall(Direction.West))
                        DrawRect(new Rect(px, py, wallPixelThickness, cellPixelSize), Color.black);
                    if (cell.HasWall(Direction.East))
                        DrawRect(new Rect(px + cellPixelSize - wallPixelThickness, py, wallPixelThickness, cellPixelSize), Color.black);

                    Color? markerColor = MarkerColor(cell);
                    if (markerColor.HasValue)
                    {
                        float m = cellPixelSize * 0.4f;
                        DrawRect(new Rect(px + (cellPixelSize - m) / 2f, py + (cellPixelSize - m) / 2f, m, m), markerColor.Value);
                    }
                }
            }

            // Jugador: punto magenta + marca de la direccion hacia la que mira.
            float ppx = panelX + player.CellX * cellPixelSize + cellPixelSize / 2f;
            float ppy = top + (h - 1 - player.CellY) * cellPixelSize + cellPixelSize / 2f;
            var (fx, fy) = player.Facing.Offset();
            float facingPx = ppx + fx * (cellPixelSize * 0.35f);
            float facingPy = ppy - fy * (cellPixelSize * 0.35f);

            DrawRect(new Rect(ppx - 4, ppy - 4, 8, 8), Color.magenta);
            DrawRect(new Rect(facingPx - 2, facingPy - 2, 4, 4), Color.magenta);
        }

        private Color FloorColor(DungeonCell cell)
        {
            if (cell.Type == CellType.Event && cell.EventConsumed)
                return new Color(0.45f, 0.45f, 0.45f);
            return cell.IsIsolatedZone ? new Color(0.30f, 0.20f, 0.35f) : new Color(0.62f, 0.62f, 0.66f);
        }

        private Color? MarkerColor(DungeonCell cell)
        {
            switch (cell.Type)
            {
                case CellType.Start: return Color.green;
                case CellType.End: return Color.red;
                case CellType.SecondaryQuest: return Color.yellow;
                case CellType.ShortcutSwitch: return new Color(0.2f, 0.4f, 1f);
                case CellType.StairsUp: return Color.cyan;
                case CellType.StairsDown: return new Color(1f, 0.5f, 0f);
                case CellType.Event: return cell.EventConsumed ? (Color?)null : Color.white;
                default: return null;
            }
        }

        private void DrawRect(Rect rect, Color color)
        {
            if (_whiteTex == null)
            {
                _whiteTex = new Texture2D(1, 1);
                _whiteTex.SetPixel(0, 0, Color.white);
                _whiteTex.Apply();
            }
            var oldColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _whiteTex);
            GUI.color = oldColor;
        }
    }
}
