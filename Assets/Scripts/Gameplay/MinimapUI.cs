using UnityEngine;
using DungeonGen;

namespace Gameplay
{
    public class MinimapUI : MonoBehaviour
    {
        public DungeonManager dungeonManager;
        public GridPlayerController player;

        [Header("Modo de mapa")]
        [Tooltip("true = solo se ve lo que el jugador ya piso (niebla de guerra). false = mapa completo (modo debug).")]
        public bool playerMode = true;
        public KeyCode toggleModeKey = KeyCode.Tab;

        public int panelX = 500;
        public int panelY = 10;
        public int cellPixelSize = 16;
        public int wallPixelThickness = 2;

        private static Texture2D _whiteTex;

        void Update()
        {
            if (Input.GetKeyDown(toggleModeKey)) playerMode = !playerMode;
        }

        void OnGUI()
        {
            if (dungeonManager == null || player == null) return;
            var floor = dungeonManager.CurrentFloor;
            if (floor == null) return;

            int w = floor.Width;
            int h = floor.Height;
            float top = panelY + 24;

            GUI.Box(new Rect(panelX - 10, panelY - 10, w * cellPixelSize + 20, h * cellPixelSize + 44), "");
            string title = playerMode
                ? $"Mapa - Piso {dungeonManager.CurrentFloorIndex} (jugador - Tab: ver todo)"
                : $"Mapa - Piso {dungeonManager.CurrentFloorIndex} (DEBUG: mapa completo - Tab: ocultar)";
            GUI.Label(new Rect(panelX, panelY, w * cellPixelSize, 20), title);

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    var cell = floor.Cells[x, y];
                    float px = panelX + x * cellPixelSize;
                    float py = top + (h - 1 - y) * cellPixelSize;

                    if (cell.Type == CellType.Void)
                    {
                        DrawRect(new Rect(px, py, cellPixelSize, cellPixelSize), VoidColor);
                        continue;
                    }

                    bool revealed = !playerMode || cell.Discovered;
                    if (!revealed)
                    {
                        DrawRect(new Rect(px, py, cellPixelSize, cellPixelSize), VoidColor);
                        continue;
                    }

                    DrawRect(new Rect(px, py, cellPixelSize, cellPixelSize), FloorColor(cell));

                    if (cell.HasWall(Direction.North))
                        DrawRect(new Rect(px, py, cellPixelSize, wallPixelThickness), VoidColor);
                    if (cell.HasWall(Direction.South))
                        DrawRect(new Rect(px, py + cellPixelSize - wallPixelThickness, cellPixelSize, wallPixelThickness), VoidColor);
                    if (cell.HasWall(Direction.West))
                        DrawRect(new Rect(px, py, wallPixelThickness, cellPixelSize), VoidColor);
                    if (cell.HasWall(Direction.East))
                        DrawRect(new Rect(px + cellPixelSize - wallPixelThickness, py, wallPixelThickness, cellPixelSize), VoidColor);

                    Color? markerColor = MarkerColor(cell);
                    if (markerColor.HasValue)
                    {
                        float m = cellPixelSize * 0.4f;
                        DrawRect(new Rect(px + (cellPixelSize - m) / 2f, py + (cellPixelSize - m) / 2f, m, m), markerColor.Value);
                    }
                }
            }

            // Jugador: punto magenta + marca de la direccion hacia la que mira. Siempre visible en ambos modos.
            float ppx = panelX + player.CellX * cellPixelSize + cellPixelSize / 2f;
            float ppy = top + (h - 1 - player.CellY) * cellPixelSize + cellPixelSize / 2f;
            var (fx, fy) = player.Facing.Offset();
            float facingPx = ppx + fx * (cellPixelSize * 0.35f);
            float facingPy = ppy - fy * (cellPixelSize * 0.35f);

            DrawRect(new Rect(ppx - 4, ppy - 4, 8, 8), Color.magenta);
            DrawRect(new Rect(facingPx - 2, facingPy - 2, 4, 4), Color.magenta);
        }

        // Paleta calcada de un automapa real de Etrian Odyssey: piso celeste, vacio azul oscuro.
        private static readonly Color VoidColor = new Color(0.04f, 0.1f, 0.2f);
        private static readonly Color PathColor = new Color(0.47f, 0.67f, 0.82f);

        private Color FloorColor(DungeonCell cell)
        {
            if (cell.Type == CellType.Event && cell.EventConsumed)
                return new Color(0.35f, 0.42f, 0.5f);
            if (cell.IsBossRoom)
                return new Color(0.5f, 0.16f, 0.16f);
            return cell.IsIsolatedZone ? new Color(0.30f, 0.20f, 0.35f) : PathColor;
        }

        private Color? MarkerColor(DungeonCell cell)
        {
            switch (cell.Type)
            {
                case CellType.Start: return Color.green;
                case CellType.End: return Color.red;
                case CellType.SecondaryQuest: return Color.yellow;
                case CellType.ShortcutSwitch: return new Color(0.2f, 0.4f, 1f);
                case CellType.ShortcutLanding: return new Color(0.85f, 0.45f, 0.1f);
                case CellType.StairsUp: return Color.cyan;
                case CellType.StairsDown: return new Color(1f, 0.5f, 0f);
                case CellType.Event: return cell.EventConsumed ? (Color?)null : Color.white;
                case CellType.Boss: return new Color(1f, 0f, 0.1f);
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
