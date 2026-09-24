using System;
using System.Collections.Generic;
using UnityEngine;
using DungeonGen;

namespace Gameplay
{
    [Serializable]
    public struct MapIconEntry
    {
        public string Id;
        public string Label;
        public Sprite Sprite;
    }

    // Herramientas (Pared / Pintar piso / Simbolos) para el MISMO mapa fisico en 3D que el
    // personaje levanta (PlayerMapViewer dibuja la grilla+anotaciones en su sprite "Drawing", ver
    // DungeonMapRenderer.DrawCore con handDrawn:true). Este script NO dibuja el mapa -- solo la
    // barra de herramientas en 2D, y traduce clicks de mouse a coordenadas de grilla proyectando
    // los bordes de Drawing.bounds a pantalla cada frame (GetMapScreenRect), asi el click SIEMPRE
    // cae exacto sobre lo que se ve, sin importar la pose actual del Animator (abriendo/abierto).
    //
    // Todo lo que se pinta aca queda en DungeonCell.PaintedWalls/PaintedFloorColorIndex/
    // PaintedSymbolIcon -- son ANOTACIONES del jugador, nunca tocan DungeonCell.Walls (la mazmorra
    // real: colision/movimiento siguen exactamente igual). Se puede pintar/colocar en CUALQUIER
    // celda -- descubierta o no -- a proposito ("toda la libertad": el jugador arma su propio
    // mapa, puede equivocarse o dibujar por adelantado, igual que en un mapa de papel de verdad).
    //
    // Herramienta "Pared": se clickea una arista (interseccion de la grilla) y despues otra --
    // si quedan alineadas en la misma fila o columna, se pinta (o se borra, si ya estaba pintado)
    // un trazo de pared a mano a lo largo de todos los tramos entre las dos, Y LA CADENA SIGUE:
    // el punto elegido pasa a ser la arista que acabas de tocar, asi el siguiente click continua
    // el trazo sin tener que volver a elegir el inicio. Clickear el MISMO punto de nuevo corta la
    // cadena; clickear uno lejano/no alineado arranca una nueva desde ahi. Las flechitas del
    // teclado hacen lo mismo en pasos de 1 (ver Update/ToggleSegmentFromVertex).
    public class PlayerMapEditorHUD : MonoBehaviour
    {
        public DungeonManager dungeonManager;
        public GridPlayerController player;
        public PlayerMapViewer mapViewer;
        public List<MapIconEntry> icons = new List<MapIconEntry>();

        private const float ToolbarWidth = 190f;
        private const float Padding = 14f;
        private const float VertexSnapPixels = 14f;

        private enum Tool { None, Wall, Paint, Symbol }
        private Tool _tool = Tool.None;
        private int _paintColorIndex;
        private string _selectedIcon;

        private Vector2Int? _pendingVertex;
        private static Texture2D _whiteTex;

        // true mientras hay una arista elegida (click) esperando que las flechitas del teclado
        // digan hacia donde pintar (ver Update()/ToggleSegmentFromVertex). Las flechitas ya no
        // las usa GridPlayerController para nada (ese giro/movimiento se movio entero a WASD),
        // asi que esto es solo el gate interno de este script -- sin conflicto que arbitrar.
        private bool IsCapturingArrowKeys => mapViewer != null && mapViewer.IsOpen && _tool == Tool.Wall && _pendingVertex.HasValue;

        void OnGUI()
        {
            if (mapViewer == null || !mapViewer.IsOpen) { _pendingVertex = null; return; }
            if (dungeonManager == null) return;
            var floor = dungeonManager.CurrentFloor;
            if (floor == null) return;

            Rect mapRect = GetMapScreenRect();
            if (mapRect.width < 4f || mapRect.height < 4f) return; // el papel todavia esta practicamente cerrado/oculto

            DrawPendingVertexHint(mapRect, floor);
            HandleMapClick(mapRect, floor);

            float toolbarX = mapRect.xMax + Padding;
            float toolbarH = Mathf.Max(mapRect.height, 220f);
            DrawToolbar(new Rect(toolbarX, mapRect.y, ToolbarWidth, toolbarH), floor);
        }

        // Rectangulo de pantalla que ocupa AHORA MISMO el sprite "Drawing" del mapa fisico (ver
        // PlayerMapViewer.drawingRenderer) -- se recalcula cada frame proyectando sus 4 esquinas
        // REALES (en espacio local del sprite, transformadas por su propio transform) a pantalla.
        //
        // ANTES usaba renderer.bounds (un AABB en espacio MUNDO) -- eso se rompia apenas el
        // jugador giraba la camara: el prop es hijo de la camara, asi que al girar tambien gira EN
        // MUNDO (aunque su rotacion LOCAL nunca cambia), y un AABB alineado a los ejes del mundo
        // se agranda/deforma con cualquier rotacion en vez de seguir representando el rectangulo
        // real -- de ahi que los botones de la derecha (anclados a este rect) parecieran
        // "desaparecer" o desubicarse al girar. Transformando las 4 esquinas locales una por una
        // se obtiene el rectangulo real que se ve en pantalla sin importar hacia donde mires.
        private Rect GetMapScreenRect()
        {
            var renderer = mapViewer.drawingRenderer;
            var cam = Camera.main;
            if (renderer == null || renderer.sprite == null || cam == null) return default;

            var sprite = renderer.sprite;
            Vector2 half = new Vector2(sprite.rect.width, sprite.rect.height) / sprite.pixelsPerUnit / 2f;
            var t = renderer.transform;

            Vector2 p0 = ProjectToScreen(t.TransformPoint(new Vector3(-half.x, -half.y, 0f)));
            Vector2 p1 = ProjectToScreen(t.TransformPoint(new Vector3(half.x, -half.y, 0f)));
            Vector2 p2 = ProjectToScreen(t.TransformPoint(new Vector3(half.x, half.y, 0f)));
            Vector2 p3 = ProjectToScreen(t.TransformPoint(new Vector3(-half.x, half.y, 0f)));

            float minX = Mathf.Min(Mathf.Min(p0.x, p1.x), Mathf.Min(p2.x, p3.x));
            float maxX = Mathf.Max(Mathf.Max(p0.x, p1.x), Mathf.Max(p2.x, p3.x));
            float minY = Mathf.Min(Mathf.Min(p0.y, p1.y), Mathf.Min(p2.y, p3.y));
            float maxY = Mathf.Max(Mathf.Max(p0.y, p1.y), Mathf.Max(p2.y, p3.y));
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private Vector2 ProjectToScreen(Vector3 worldPos)
        {
            Vector3 sp = Camera.main.WorldToScreenPoint(worldPos);
            return new Vector2(sp.x, Screen.height - sp.y);
        }

        // Flechitas del teclado: con una arista ya elegida (_pendingVertex, ver HandleMapClick),
        // cada flecha pinta (o borra) el tramo de pared de UN paso en esa direccion y mueve la
        // arista elegida un paso mas alla -- asi una seguidilla de flechas dibuja un trazo largo
        // sin tener que clickear cada esquina. GridPlayerController no gira/mueve al personaje con
        // estas mismas teclas mientras IsCapturingArrowKeys es true.
        void Update()
        {
            if (!IsCapturingArrowKeys || dungeonManager == null) return;
            var floor = dungeonManager.CurrentFloor;
            if (floor == null) return;

            if (Input.GetKeyDown(KeyCode.UpArrow)) ToggleSegmentFromVertex(floor, 0, -1);
            else if (Input.GetKeyDown(KeyCode.DownArrow)) ToggleSegmentFromVertex(floor, 0, 1);
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) ToggleSegmentFromVertex(floor, -1, 0);
            else if (Input.GetKeyDown(KeyCode.RightArrow)) ToggleSegmentFromVertex(floor, 1, 0);
        }

        private void ToggleSegmentFromVertex(DungeonFloor floor, int dx, int dy)
        {
            var v = _pendingVertex.Value;
            if (dy != 0)
            {
                int j = dy < 0 ? v.y - 1 : v.y;
                if (j < 0 || j >= floor.Height) return;
                SetVerticalSegment(floor, v.x, j, !VerticalSegmentPainted(floor, v.x, j));
                v.y += dy;
            }
            else
            {
                int i = dx < 0 ? v.x - 1 : v.x;
                if (i < 0 || i >= floor.Width) return;
                SetHorizontalSegment(floor, i, v.y, !HorizontalSegmentPainted(floor, i, v.y));
                v.x += dx;
            }
            if (v.x >= 0 && v.x <= floor.Width && v.y >= 0 && v.y <= floor.Height)
                _pendingVertex = v;
            mapViewer.MarkDirty();
        }

        // ---------------------------------------------------------------------------------------
        // Dibujo (solo el punto de "arista elegida" -- el mapa en si lo pinta PlayerMapViewer)
        // ---------------------------------------------------------------------------------------

        private void DrawPendingVertexHint(Rect mapRect, DungeonFloor floor)
        {
            if (!_pendingVertex.HasValue) return;
            var v = _pendingVertex.Value;
            Vector2 p = VertexToScreen(mapRect, floor, v);
            var old = GUI.color;
            GUI.color = new Color(1f, 0.85f, 0.2f);
            GUI.DrawTexture(new Rect(p.x - 4, p.y - 4, 8, 8), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private Vector2 VertexToScreen(Rect mapRect, DungeonFloor floor, Vector2Int v)
        {
            return new Vector2(
                mapRect.x + (v.x / (float)floor.Width) * mapRect.width,
                mapRect.y + (v.y / (float)floor.Height) * mapRect.height);
        }

        private Sprite FindIcon(string id)
        {
            foreach (var entry in icons)
                if (entry.Id == id) return entry.Sprite;
            return null;
        }

        // ---------------------------------------------------------------------------------------
        // Input
        // ---------------------------------------------------------------------------------------

        private void HandleMapClick(Rect mapRect, DungeonFloor floor)
        {
            var e = Event.current;
            if (e == null || e.type != EventType.MouseDown || e.button != 0) return;
            if (!mapRect.Contains(e.mousePosition)) return;

            int w = floor.Width, h = floor.Height;
            Vector2 local = e.mousePosition - mapRect.position;
            Vector2 frac = new Vector2(local.x / mapRect.width, local.y / mapRect.height);

            if (_tool == Tool.Wall)
            {
                var vertex = NearestVertex(local, mapRect, w, h);
                if (!vertex.HasValue) return;

                if (_pendingVertex.HasValue)
                {
                    var a = _pendingVertex.Value;
                    if (a == vertex.Value)
                    {
                        _pendingVertex = null; // mismo punto: corta la cadena
                    }
                    else if (a.x == vertex.Value.x || a.y == vertex.Value.y)
                    {
                        ToggleWallLine(floor, a, vertex.Value);
                        _pendingVertex = vertex.Value; // sigue la cadena desde aca (concatenado)
                        mapViewer.MarkDirty();
                    }
                    else
                    {
                        _pendingVertex = vertex; // lejos/no alineado: arranca una linea nueva
                    }
                }
                else
                {
                    _pendingVertex = vertex;
                }
                e.Use();
                return;
            }

            if (_tool != Tool.Paint && _tool != Tool.Symbol) return;

            int cellX = Mathf.Clamp(Mathf.FloorToInt(frac.x * w), 0, w - 1);
            int cellScreenRow = Mathf.Clamp(Mathf.FloorToInt(frac.y * h), 0, h - 1);
            int worldY = h - 1 - cellScreenRow;
            var cell = floor.Cells[cellX, worldY];

            if (_tool == Tool.Paint)
            {
                cell.PaintedFloorColorIndex = cell.PaintedFloorColorIndex == _paintColorIndex ? -1 : _paintColorIndex;
                mapViewer.MarkDirty();
                e.Use();
            }
            else if (!string.IsNullOrEmpty(_selectedIcon))
            {
                cell.PaintedSymbolIcon = cell.PaintedSymbolIcon == _selectedIcon ? null : _selectedIcon;
                mapViewer.MarkDirty();
                e.Use();
            }
        }

        // Vertices de la grilla: (0,0) a (w,h) en "espacio de pantalla" (fila 0 = arriba de todo),
        // MISMA convencion que usa DungeonMapRenderer.DrawCore para las esquinas de cada celda.
        // Solo devuelve un vertice si el click cayo lo bastante cerca de una interseccion real.
        private Vector2Int? NearestVertex(Vector2 local, Rect mapRect, int w, int h)
        {
            float cellPxX = mapRect.width / w;
            float cellPxY = mapRect.height / h;
            int cx = Mathf.RoundToInt(local.x / cellPxX);
            int cy = Mathf.RoundToInt(local.y / cellPxY);
            if (cx < 0 || cx > w || cy < 0 || cy > h) return null;

            float vx = cx * cellPxX, vy = cy * cellPxY;
            if (Vector2.Distance(local, new Vector2(vx, vy)) > VertexSnapPixels) return null;
            return new Vector2Int(cx, cy);
        }

        private void ToggleWallLine(DungeonFloor floor, Vector2Int a, Vector2Int b)
        {
            if (a == b) return;

            if (a.y == b.y)
            {
                int cy = a.y;
                int xMin = Mathf.Min(a.x, b.x), xMax = Mathf.Max(a.x, b.x);
                bool targetState = !HorizontalSegmentPainted(floor, xMin, cy);
                for (int i = xMin; i < xMax; i++) SetHorizontalSegment(floor, i, cy, targetState);
            }
            else if (a.x == b.x)
            {
                int cx = a.x;
                int yMin = Mathf.Min(a.y, b.y), yMax = Mathf.Max(a.y, b.y);
                bool targetState = !VerticalSegmentPainted(floor, cx, yMin);
                for (int j = yMin; j < yMax; j++) SetVerticalSegment(floor, cx, j, targetState);
            }
        }

        // "Segmento horizontal i" = el tramo de grilla entre los vertices (i,cy) y (i+1,cy): el
        // borde entre la fila de pantalla (cy-1) [arriba] y la fila (cy) [abajo], en la columna
        // mundo x=i. Cada lado se pinta en la celda correspondiente si existe (los bordes del
        // piso solo tienen un lado real).
        private void SetHorizontalSegment(DungeonFloor floor, int i, int cy, bool value)
        {
            int w = floor.Width, h = floor.Height;
            if (i < 0 || i >= w) return;
            if (cy < h)
            {
                int worldYBelow = h - 1 - cy;
                floor.Cells[i, worldYBelow].SetPaintedWall(Direction.North, value);
            }
            if (cy - 1 >= 0)
            {
                int worldYAbove = h - cy;
                if (worldYAbove < h) floor.Cells[i, worldYAbove].SetPaintedWall(Direction.South, value);
            }
        }

        // Misma logica que usa DungeonMapRenderer para el relleno de esquinas -- un solo lugar de
        // verdad para "que cuenta como pintado" (ver DungeonMapRenderer.IsHorizontalSegmentPainted).
        private bool HorizontalSegmentPainted(DungeonFloor floor, int i, int cy) => DungeonMapRenderer.IsHorizontalSegmentPainted(floor, i, cy);

        // "Segmento vertical j" = el tramo entre los vertices (cx,j) y (cx,j+1): el borde entre la
        // columna de pantalla (cx-1) [izquierda] y la columna (cx) [derecha], en la fila de
        // pantalla j (mundo y = h-1-j).
        private void SetVerticalSegment(DungeonFloor floor, int cx, int j, bool value)
        {
            int w = floor.Width, h = floor.Height;
            if (j < 0 || j >= h) return;
            int worldY = h - 1 - j;
            if (cx < w) floor.Cells[cx, worldY].SetPaintedWall(Direction.West, value);
            if (cx - 1 >= 0) floor.Cells[cx - 1, worldY].SetPaintedWall(Direction.East, value);
        }

        private bool VerticalSegmentPainted(DungeonFloor floor, int cx, int j) => DungeonMapRenderer.IsVerticalSegmentPainted(floor, cx, j);

        // ---------------------------------------------------------------------------------------
        // Barra de herramientas (a la derecha del mapa)
        // ---------------------------------------------------------------------------------------

        private void DrawToolbar(Rect rect, DungeonFloor floor)
        {
            float y = rect.y;
            GUI.Label(new Rect(rect.x, y, rect.width, 20), "Herramientas:");
            y += 24;

            if (UIButton.Draw(new Rect(rect.x, y, rect.width, 26), ToolLabel(Tool.Wall, "Pared")))
                SetTool(Tool.Wall);
            y += 30;
            if (UIButton.Draw(new Rect(rect.x, y, rect.width, 26), ToolLabel(Tool.Paint, "Pintar piso")))
                SetTool(Tool.Paint);
            y += 30;
            if (UIButton.Draw(new Rect(rect.x, y, rect.width, 26), ToolLabel(Tool.Symbol, "Simbolos")))
                SetTool(Tool.Symbol);
            y += 34;

            if (_tool == Tool.Wall)
            {
                var hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
                string hint = _pendingVertex.HasValue
                    ? "Usa las flechitas para pintar hacia ese lado (se encadena solo), o clickea otra esquina."
                    : "Clickea una esquina de la grilla: despues elegi otra esquina, o usa las flechitas del teclado.";
                GUI.Label(new Rect(rect.x, y, rect.width, 48), hint, hintStyle);
                y += 52;
            }
            else if (_tool == Tool.Paint)
            {
                GUI.Label(new Rect(rect.x, y, rect.width, 18), "Color:");
                y += 20;
                for (int i = 0; i < DungeonMapRenderer.FloorPaintColors.Length; i++)
                {
                    var old = GUI.color;
                    GUI.color = DungeonMapRenderer.FloorPaintColors[i];
                    string label = (_paintColorIndex == i ? "> " : "") + DungeonMapRenderer.FloorPaintNames[i];
                    int captured = i;
                    if (UIButton.Draw(new Rect(rect.x, y, rect.width, 24), label))
                        _paintColorIndex = captured;
                    GUI.color = old;
                    y += 28;
                }
                var hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
                GUI.Label(new Rect(rect.x, y, rect.width, 34), "Clickea una celda para pintarla (de nuevo para borrar).", hintStyle);
                y += 38;
            }
            else if (_tool == Tool.Symbol)
            {
                GUI.Label(new Rect(rect.x, y, rect.width, 18), "Elegi un simbolo:");
                y += 20;
                const float iconSize = 34f;
                const int perRow = 4;
                int col = 0;
                foreach (var entry in icons)
                {
                    var iconRect = new Rect(rect.x + col * (iconSize + 6f), y, iconSize, iconSize);
                    var old = GUI.color;
                    if (_selectedIcon == entry.Id) GUI.color = new Color(1f, 0.9f, 0.5f);
                    if (entry.Sprite != null && GUI.Button(iconRect, entry.Sprite.texture))
                        _selectedIcon = entry.Id;
                    GUI.color = old;
                    col++;
                    if (col >= perRow) { col = 0; y += iconSize + 6f; }
                }
                if (col != 0) y += iconSize + 6f;
                y += 6f;
                var hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
                GUI.Label(new Rect(rect.x, y, rect.width, 34), "Clickea una celda para poner el simbolo elegido (de nuevo para sacarlo).", hintStyle);
                y += 38;
            }

            y += 8;
            if (UIButton.Draw(new Rect(rect.x, y, rect.width, 24), "Limpiar dibujo de este piso"))
                ClearAnnotations(floor);
            y += 30;
            if (_tool != Tool.None && UIButton.Draw(new Rect(rect.x, y, rect.width, 24), "Ninguna herramienta"))
                SetTool(Tool.None);
        }

        private string ToolLabel(Tool t, string label) => (_tool == t ? "> " : "") + label;

        private void SetTool(Tool t)
        {
            _tool = _tool == t ? Tool.None : t; // click de nuevo sobre la misma = apagarla
            _pendingVertex = null;
        }

        private void ClearAnnotations(DungeonFloor floor)
        {
            foreach (var cell in floor.Cells)
            {
                cell.SetPaintedWall(Direction.North, false);
                cell.SetPaintedWall(Direction.South, false);
                cell.SetPaintedWall(Direction.East, false);
                cell.SetPaintedWall(Direction.West, false);
                cell.PaintedFloorColorIndex = -1;
                cell.PaintedSymbolIcon = null;
            }
            mapViewer.MarkDirty();
        }
    }
}
