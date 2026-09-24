using System;
using UnityEngine;
using DungeonGen;

namespace Gameplay
{
    // Dibujo del mapa de un piso (grilla + paredes + marcadores), factorizado de MinimapUI para
    // que el mismo dibujo se pueda usar en el minimapa de esquina (IMGUI, un solo piso, el
    // actual), en la pestaña "Mapa" del menu de pausa (IMGUI, cualquier piso ya explorado, ver
    // PauseMenuHUD.DrawMap) Y en el mapa fisico que el personaje sostiene cerca de camara
    // (Texture2D, ver PlayerMapViewer) -- misma geometria de celdas/paredes/marcadores en los 3
    // casos, solo cambia DONDE se pinta cada rect (DrawCore recibe ese "donde" como delegate).
    // Paleta calcada de un automapa real de Etrian Odyssey: piso celeste, vacio azul oscuro.
    public static class DungeonMapRenderer
    {
        public static readonly Color VoidColor = new Color(0.04f, 0.1f, 0.2f);
        public static readonly Color PathColor = new Color(0.47f, 0.67f, 0.82f);

        // Anotaciones del jugador (ver DungeonCell.PaintedWalls/PaintedFloorColorIndex y
        // PlayerMapEditorHUD, que es quien las escribe): capa aparte de la mazmorra REAL de
        // arriba, dibujada encima. GridLineColor son las lineas de "cuaderno cuadriculado" (un
        // borde por celda, ADEMAS de los muros) -- celeste mas oscuro que el piso (PathColor), no
        // blanco, para que no compita visualmente con las paredes pintadas de abajo. PaintedWallColor
        // es blanca y gruesa (ver paintedWallThickness en DrawCore) para que un trazo del jugador
        // se lea fuerte y clarito contra el piso, bien distinto del VoidColor oscuro de un muro
        // real revelado por el automapa. Los 3 colores de piso son EXACTAMENTE los que ya usa
        // FloorColor/MarkerColor mas abajo (piso normal celeste, trampa naranja, tesoro dorado).
        public static readonly Color GridLineColor = new Color(0.22f, 0.42f, 0.55f, 0.85f);
        public static readonly Color PaintedWallColor = new Color(1f, 1f, 1f, 0.97f);

        // Modo "mapa a mano" (handDrawn, ver DrawCore): el automapa NO revela piso/paredes/
        // marcadores -- el jugador arma su propio mapa con las herramientas. Lo UNICO automatico
        // es este celeste (WalkedColor) marcando por donde ya caminaste, como una miguitas de pan.
        // Mas brillante/opaco que la primera version (0.55 de alpha se perdia contra el fondo
        // oscuro -- subido a 0.85 y un poco mas claro para que se note bien).
        public static readonly Color WalkedColor = new Color(0.3f, 0.65f, 1f, 0.85f);

        public static readonly Color[] FloorPaintColors =
        {
            PathColor,
            new Color(0.55f, 0.22f, 0.05f),
            new Color(1f, 0.82f, 0.1f),
        };
        public static readonly string[] FloorPaintNames = { "Normal", "Peligro", "Objetivo" };

        private static Texture2D _whiteTex;

        // origin = esquina superior izquierda del area de dibujo (en coordenadas de GUI).
        // playerMode: true = niebla de guerra (solo celdas Discovered), false = mapa completo.
        // player/showPlayerMarker: si showPlayerMarker es true, dibuja el punto magenta del jugador
        // usando player.CellX/CellY/Facing (se omite al mostrar un piso que no es el actual).
        // Mismo blanco/rojo que FoeController usa en el mundo 3D (CalmColor/ChaseColor) -- para que
        // el punto del mapa y la esfera que ves caminar sean, a simple vista, el mismo bicho.
        private static readonly Color FoeCalmColor = Color.white;
        private static readonly Color FoeChaseColor = new Color(0.85f, 0.12f, 0.1f);

        // foe/foeAlwaysVisible: opcionales -- si se pasa un FoeController activo, se dibuja su
        // posicion actual (blanco tranquilo / rojo persiguiendo, ver FoeController.IsChasing).
        // Respeta la niebla de guerra igual que el resto del mapa (solo se ve si la celda donde
        // esta parado ya fue Discovered) SALVO que foeAlwaysVisible sea true (ver
        // DungeonManager.debugFoeAlwaysVisibleOnMap -- pensado para testeo).
        public static void Draw(Vector2 origin, DungeonFloor floor, bool playerMode, int cellPixelSize, float wallPixelThickness,
            GridPlayerController player, bool showPlayerMarker, FoeController foe = null, bool foeAlwaysVisible = false, bool showGrid = false, bool showAnnotations = false, bool handDrawn = false)
        {
            DrawCore(DrawRect, origin, floor, playerMode, cellPixelSize, wallPixelThickness, player, showPlayerMarker, foe, foeAlwaysVisible, showGrid, showAnnotations, handDrawn);
        }

        // Mismo dibujo que Draw, pero volcado a una Texture2D (coordenadas Y abajo-arriba, al
        // reves que GUI) en vez de a pantalla -- usado por PlayerMapViewer para el "dibujo" que
        // se ve sobre el papel del mapa fisico. Llama Apply() al final; el llamador es responsable
        // de crear la textura del tamaño correcto (floor.Width/Height * cellPixelSize).
        public static void DrawToTexture(Texture2D tex, DungeonFloor floor, bool playerMode, int cellPixelSize, float wallPixelThickness,
            GridPlayerController player, bool showPlayerMarker, FoeController foe = null, bool foeAlwaysVisible = false, bool showGrid = false, bool showAnnotations = false, bool handDrawn = false)
        {
            if (tex == null) return;
            var clearColor = new Color(0f, 0f, 0f, 0f);
            var clearPixels = new Color[tex.width * tex.height];
            for (int i = 0; i < clearPixels.Length; i++) clearPixels[i] = clearColor;
            tex.SetPixels(clearPixels);

            void TexRect(Rect r, Color c) => FillTexRect(tex, r, c);
            DrawCore(TexRect, Vector2.zero, floor, playerMode, cellPixelSize, wallPixelThickness, player, showPlayerMarker, foe, foeAlwaysVisible, showGrid, showAnnotations, handDrawn);
            tex.Apply();
        }

        private static void DrawCore(Action<Rect, Color> drawRect, Vector2 origin, DungeonFloor floor, bool playerMode, int cellPixelSize, float wallPixelThickness,
            GridPlayerController player, bool showPlayerMarker, FoeController foe, bool foeAlwaysVisible, bool showGrid, bool showAnnotations, bool handDrawn)
        {
            if (floor == null) return;

            int w = floor.Width;
            int h = floor.Height;
            // Trazo de pared a mano: ANTES se le sumaba +1 fijo a wallPixelThickness (para que
            // siempre quedara un pelo mas grueso que un muro real) -- eso topeaba el minimo y no
            // dejaba ajustar el grosor con libertad desde el Inspector, asi que se saco: ahora
            // wallPixelThickness (PlayerMapViewer, en el mapa a mano nunca se dibuja un muro real
            // de todas formas) ES directamente el grosor pintado, sin piso ni suma escondida.
            float paintedWallThickness = wallPixelThickness;

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    var cell = floor.Cells[x, y];
                    float px = origin.x + x * cellPixelSize;
                    float py = origin.y + (h - 1 - y) * cellPixelSize;
                    var cellRect = new Rect(px, py, cellPixelSize, cellPixelSize);

                    bool isVoid = cell.Type == CellType.Void;
                    bool revealed = !isVoid && (!playerMode || cell.Discovered);

                    if (handDrawn)
                    {
                        // "El jugador arma el mapa": nada de piso/paredes/marcadores reales se
                        // regala solo. Lo UNICO automatico es un celeste marcando las celdas ya
                        // pisadas (WalkedColor), como una miguitas de pan -- ni siquiera eso
                        // distingue pared de piso, solo "estuviste aca".
                        drawRect(cellRect, VoidColor);
                        if (cell.Discovered) drawRect(cellRect, WalkedColor);
                    }
                    else
                    {
                        drawRect(cellRect, revealed ? FloorColor(cell, floor.TrapDisabled) : VoidColor);
                    }

                    // Grilla "cuaderno cuadriculado": TODO el piso arranca cuadriculado, como una
                    // hoja de cuaderno en blanco -- ANTES de explorar nada, no solo en las celdas ya
                    // reveladas (a pedido: "todo el mapa debe partir cuadriculado"). Se dibuja ANTES
                    // de los muros reales/pintados (que son mas gruesos y opacos y quedan por
                    // encima), asi que en un borde con muro real la linea de grilla ni se nota --
                    // solo se ve en los bordes "abiertos".
                    if (showGrid)
                    {
                        const float gridThickness = 1f;
                        drawRect(new Rect(px, py, cellPixelSize, gridThickness), GridLineColor);
                        drawRect(new Rect(px, py + cellPixelSize - gridThickness, cellPixelSize, gridThickness), GridLineColor);
                        drawRect(new Rect(px, py, gridThickness, cellPixelSize), GridLineColor);
                        drawRect(new Rect(px + cellPixelSize - gridThickness, py, gridThickness, cellPixelSize), GridLineColor);
                    }

                    // Las anotaciones del jugador (piso pintado, paredes a mano) valen en CUALQUIER
                    // celda -- descubierta o no, real o "erronea" -- a proposito: el jugador tiene
                    // que poder equivocarse o dibujar por adelantado, igual que en un mapa de papel
                    // de verdad (a pedido: "toda la libertad"). Nunca dependen de `revealed`.
                    if (showAnnotations && cell.PaintedFloorColorIndex >= 0 && cell.PaintedFloorColorIndex < FloorPaintColors.Length)
                    {
                        var paint = FloorPaintColors[cell.PaintedFloorColorIndex];
                        drawRect(cellRect, new Color(paint.r, paint.g, paint.b, 0.65f));
                    }

                    if (showAnnotations)
                    {
                        if (cell.HasPaintedWall(Direction.North))
                            drawRect(new Rect(px, py, cellPixelSize, paintedWallThickness), PaintedWallColor);
                        if (cell.HasPaintedWall(Direction.South))
                            drawRect(new Rect(px, py + cellPixelSize - paintedWallThickness, cellPixelSize, paintedWallThickness), PaintedWallColor);
                        if (cell.HasPaintedWall(Direction.West))
                            drawRect(new Rect(px, py, paintedWallThickness, cellPixelSize), PaintedWallColor);
                        if (cell.HasPaintedWall(Direction.East))
                            drawRect(new Rect(px + cellPixelSize - paintedWallThickness, py, paintedWallThickness, cellPixelSize), PaintedWallColor);

                        // Simbolo colocado a mano (ver PlayerMapEditorHUD): no se puede blitear el
                        // icono vectorial de verdad a esta resolucion tan chica sin que quede un
                        // borron, asi que se marca con un punto de color -- el color sale de un
                        // hash del nombre del icono, asi cada tipo de simbolo queda siempre con el
                        // mismo color sin necesitar una tabla aparte. La barra de herramientas
                        // (2D) es la que muestra el icono real al elegirlo.
                        if (!string.IsNullOrEmpty(cell.PaintedSymbolIcon))
                        {
                            float m = cellPixelSize * 0.55f;
                            drawRect(new Rect(px + (cellPixelSize - m) / 2f, py + (cellPixelSize - m) / 2f, m, m), SymbolMarkerColor(cell.PaintedSymbolIcon));
                        }
                    }

                    if (handDrawn || !revealed) continue; // modo a mano: nunca hay paredes/marcadores reales. Sin explorar: tampoco.

                    if (cell.HasWall(Direction.North))
                        drawRect(new Rect(px, py, cellPixelSize, wallPixelThickness), VoidColor);
                    if (cell.HasWall(Direction.South))
                        drawRect(new Rect(px, py + cellPixelSize - wallPixelThickness, cellPixelSize, wallPixelThickness), VoidColor);
                    if (cell.HasWall(Direction.West))
                        drawRect(new Rect(px, py, wallPixelThickness, cellPixelSize), VoidColor);
                    if (cell.HasWall(Direction.East))
                        drawRect(new Rect(px + cellPixelSize - wallPixelThickness, py, wallPixelThickness, cellPixelSize), VoidColor);

                    Color? markerColor = MarkerColor(cell);
                    if (markerColor.HasValue)
                    {
                        float m = cellPixelSize * 0.4f;
                        drawRect(new Rect(px + (cellPixelSize - m) / 2f, py + (cellPixelSize - m) / 2f, m, m), markerColor.Value);
                    }
                }
            }

            // Relleno de esquinas del trazo pintado: cuando un giro de 90 grados esta partido
            // entre DOS CELDAS DISTINTAS (p.ej. el muro Este de una celda y el Sur de la celda de
            // al lado, formando un escalon), cada tira solo entra "paintedWallThickness" pixeles
            // en SU propia celda y las dos tiras no llegan a superponerse en el vertice
            // compartido -- queda un huequito exactamente en la esquina. Se recorre cada vertice
            // de la grilla y, si hay algun segmento pintado horizontal Y alguno vertical
            // tocandolo (sin importar de que celda salga cada uno), se tapa el hueco con un
            // cuadradito centrado ahi.
            if (showAnnotations)
            {
                for (int cx = 0; cx <= w; cx++)
                {
                    for (int cy = 0; cy <= h; cy++)
                    {
                        if (!IsHorizontalSegmentPainted(floor, cx - 1, cy) && !IsHorizontalSegmentPainted(floor, cx, cy)) continue;
                        if (!IsVerticalSegmentPainted(floor, cx, cy - 1) && !IsVerticalSegmentPainted(floor, cx, cy)) continue;

                        float vx = origin.x + cx * cellPixelSize;
                        float vy = origin.y + cy * cellPixelSize;
                        drawRect(new Rect(vx - paintedWallThickness / 2f, vy - paintedWallThickness / 2f, paintedWallThickness, paintedWallThickness), PaintedWallColor);
                    }
                }
            }

            if (foe != null && floor.InBounds(foe.X, foe.Y) && (foeAlwaysVisible || floor.Cells[foe.X, foe.Y].Discovered))
            {
                float fpx = origin.x + foe.X * cellPixelSize + cellPixelSize / 2f;
                float fpy = origin.y + (h - 1 - foe.Y) * cellPixelSize + cellPixelSize / 2f;
                float m = cellPixelSize * 0.6f;
                drawRect(new Rect(fpx - m / 2f, fpy - m / 2f, m, m), foe.IsChasing ? FoeChaseColor : FoeCalmColor);
            }

            if (showPlayerMarker && player != null)
            {
                float ppx = origin.x + player.CellX * cellPixelSize + cellPixelSize / 2f;
                float ppy = origin.y + (h - 1 - player.CellY) * cellPixelSize + cellPixelSize / 2f;
                var (fx, fy) = player.Facing.Offset();
                float facingPx = ppx + fx * (cellPixelSize * 0.35f);
                float facingPy = ppy - fy * (cellPixelSize * 0.35f);

                drawRect(new Rect(ppx - 4, ppy - 4, 8, 8), Color.magenta);
                drawRect(new Rect(facingPx - 2, facingPy - 2, 4, 4), Color.magenta);
            }
        }

        // trapDisabled: true si el disparador de esta sala ya fue destruido (ver
        // DungeonGenerator.TryDestroyTrapDisparador) -- una trampa desactivada se pinta como piso
        // normal, para que el mapa confirme de un vistazo que ese peligro ya no existe.
        public static Color FloorColor(DungeonCell cell, bool trapDisabled = false)
        {
            if (cell.IsBossRoom)
                return new Color(0.5f, 0.16f, 0.16f);
            if (cell.IsTrapRoom && !trapDisabled)
                return cell.IsTrapCell ? new Color(0.55f, 0.22f, 0.05f) : new Color(0.4f, 0.28f, 0.12f);
            if (cell.IsPuzzleTile)
                return cell.IsPuzzleTileSafe ? new Color(0.35f, 0.45f, 0.5f) : new Color(0.5f, 0.28f, 0.1f);
            return cell.IsIsolatedZone ? new Color(0.30f, 0.20f, 0.35f) : PathColor;
        }

        public static Color? MarkerColor(DungeonCell cell)
        {
            switch (cell.Type)
            {
                // Inicio/Salida: ocultos a proposito (ver DrawLegend/MarkerLegend abajo) -- un
                // jugador nuevo los confundia con "hay que volver ahi" o "objetivo real", cuando en
                // realidad son solo de donde entraste y las escaleras ya cumplen ese rol mejor.
                case CellType.Start: return null;
                case CellType.End: return null;
                case CellType.SecondaryQuest: return Color.yellow;
                case CellType.ShortcutSwitch: return new Color(0.2f, 0.4f, 1f);
                case CellType.ShortcutLanding: return new Color(0.85f, 0.45f, 0.1f);
                case CellType.StairsUp: return Color.cyan;
                case CellType.StairsDown: return new Color(1f, 0.5f, 0f);
                case CellType.Boss: return new Color(1f, 0f, 0.1f);
                // Violeta apagado una vez leido (EventConsumed, ver DungeonManager.OnPlayerEnterCell)
                // -- distingue de un lejos "esto ya lo leiste" de un violeta brillante "todavia hay
                // algo nuevo aca", en vez de quedarse siempre igual sin importar si ya lo visitaste.
                case CellType.Lore:
                    bool isMysteryClue = System.Array.IndexOf(DungeonGenerator.BiomeGateLoreIds, cell.AssignedLoreId) >= 0;
                    if (cell.EventConsumed) return isMysteryClue ? new Color(0.5f, 0.45f, 0.25f) : new Color(0.4f, 0.32f, 0.45f);
                    return isMysteryClue ? new Color(1f, 0.82f, 0.25f) : new Color(0.75f, 0.35f, 1f);
                case CellType.LockedDoor: return new Color(0.55f, 0.1f, 0.1f);
                case CellType.Lever: return new Color(0.15f, 0.9f, 0.35f);
                case CellType.Treasure: return new Color(1f, 0.82f, 0.1f);
                case CellType.BiomeGate: return new Color(0.55f, 0.85f, 1f);
                default: return null;
            }
        }

        // Leyenda del mapa: MISMOS colores que FloorColor/MarkerColor de arriba (una sola fuente de
        // verdad, para que la leyenda nunca pueda desincronizarse de lo que el mapa realmente
        // dibuja), con una descripcion en criollo de que es cada cosa -- usada por la pestana
        // "Leyenda" del menu de pausa (ver PauseMenuHUD.DrawLegend).
        public readonly struct LegendEntry
        {
            public readonly Color Color;
            public readonly string Label;
            public readonly string Description;
            public LegendEntry(Color color, string label, string description)
            {
                Color = color;
                Label = label;
                Description = description;
            }
        }

        public static readonly LegendEntry[] MarkerLegend =
        {
            new LegendEntry(Color.magenta, "Vos", "Tu posicion actual y hacia donde estas mirando."),
            new LegendEntry(Color.yellow, "Mision secundaria", "Objetivo opcional de este piso."),
            new LegendEntry(new Color(0.2f, 0.4f, 1f), "Interruptor de atajo", "Actívalo para abrir un teletransporte permanente hacia la zona aislada."),
            new LegendEntry(new Color(0.85f, 0.45f, 0.1f), "Llegada de atajo", "Donde aparecés al usar el teletransporte del interruptor."),
            new LegendEntry(Color.cyan, "Escalera (subir)", "Lleva al piso de arriba."),
            new LegendEntry(new Color(1f, 0.5f, 0f), "Escalera (bajar)", "Lleva al piso de abajo."),
            new LegendEntry(new Color(1f, 0f, 0.1f), "Jefe", "La celda exacta del jefe, dentro de su sala."),
            new LegendEntry(new Color(0.75f, 0.35f, 1f), "Fragmento de lore", "Desbloquea una entrada del Códex al pisarla. Se apaga (violeta grisáceo) una vez leído."),
            new LegendEntry(new Color(0.55f, 0.1f, 0.1f), "Puerta bloqueada", "Hay que activar su palanca para abrirla de forma permanente."),
            new LegendEntry(new Color(0.15f, 0.9f, 0.35f), "Palanca", "Abre la puerta bloqueada correspondiente."),
            new LegendEntry(new Color(1f, 0.82f, 0.1f), "Cofre", "Tesoro: puntos + una carga de Perforador, siempre hay uno por piso."),
            new LegendEntry(new Color(0.55f, 0.85f, 1f), "Puerta Fría", "Entrada escondida al Bioma 2, en el piso 0. Cualquier pared puede ser esta -- el Perforador la abre igual, con o sin pistas."),
        };

        public static readonly LegendEntry[] FloorLegend =
        {
            new LegendEntry(PathColor, "Piso normal", "Celda caminable ya descubierta."),
            new LegendEntry(new Color(0.5f, 0.16f, 0.16f), "Piso de sala de jefe", "Parte del piso, mas grande, de la sala del jefe."),
            new LegendEntry(new Color(0.55f, 0.22f, 0.05f), "Trampa (celda peligrosa)", "Flechas o picos: pisarla tiene chance de dañar a toda la party."),
            new LegendEntry(new Color(0.4f, 0.28f, 0.12f), "Sala de trampas (resto)", "Parte segura de una sala de trampas -- las peligrosas se ven mas oscuras/rojas."),
            new LegendEntry(new Color(0.30f, 0.20f, 0.35f), "Zona aislada", "Solo se llega por un desvio largo o por el atajo de teletransporte."),
            new LegendEntry(VoidColor, "Sin explorar / vacío", "Roca solida real, o una celda que todavia no pisaste (niebla de guerra)."),
        };

        // "Segmento horizontal i" = el tramo de grilla entre los vertices (i,cy) y (i+1,cy) --
        // MISMA convencion que PlayerMapEditorHUD (que es quien pinta estos segmentos con las
        // herramientas): esta pintado si CUALQUIERA de las 2 celdas que comparten ese borde lo
        // tiene marcado de su lado (North de la celda de abajo, o South de la de arriba).
        public static bool IsHorizontalSegmentPainted(DungeonFloor floor, int i, int cy)
        {
            int w = floor.Width, h = floor.Height;
            if (i < 0 || i >= w) return false;
            if (cy < h && floor.Cells[i, h - 1 - cy].HasPaintedWall(Direction.North)) return true;
            if (cy - 1 >= 0 && h - cy < h && floor.Cells[i, h - cy].HasPaintedWall(Direction.South)) return true;
            return false;
        }

        // "Segmento vertical j" = el tramo entre los vertices (cx,j) y (cx,j+1): pintado si
        // cualquiera de las 2 celdas que comparten ese borde lo tiene marcado (West de la celda
        // de la derecha, o East de la de la izquierda).
        public static bool IsVerticalSegmentPainted(DungeonFloor floor, int cx, int j)
        {
            int w = floor.Width, h = floor.Height;
            if (j < 0 || j >= h) return false;
            int worldY = h - 1 - j;
            if (cx < w && floor.Cells[cx, worldY].HasPaintedWall(Direction.West)) return true;
            if (cx - 1 >= 0 && floor.Cells[cx - 1, worldY].HasPaintedWall(Direction.East)) return true;
            return false;
        }

        // Color estable por icono (hash del nombre -> tono), ver el comentario en DrawCore.
        private static Color SymbolMarkerColor(string iconId)
        {
            int hash = 0;
            unchecked { foreach (char c in iconId) hash = hash * 31 + c; }
            float hue = (Mathf.Abs(hash) % 360) / 360f;
            return Color.HSVToRGB(hue, 0.7f, 0.95f);
        }

        // Un piso cuenta como "explorado" (para las sub-pestanas del menu de pausa) si el jugador
        // ya piso al menos una celda; evita listar pisos que todavia no se visitaron nunca.
        public static bool HasAnyDiscoveredCell(DungeonFloor floor)
        {
            if (floor == null) return false;
            foreach (var cell in floor.Cells)
                if (cell.Discovered) return true;
            return false;
        }

        private static void DrawRect(Rect rect, Color color)
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

        // Equivalente de DrawRect pero pintando pixeles reales de una Texture2D en vez de un
        // GUI.DrawTexture -- DrawCore genera coordenadas estilo GUI (origen arriba-izquierda, Y
        // crece hacia abajo); Texture2D.SetPixel usa origen abajo-izquierda, asi que se invierte Y
        // aca (un solo lugar) en vez de duplicar toda la logica de celdas/paredes para el caso texture.
        private static void FillTexRect(Texture2D tex, Rect rect, Color color)
        {
            int x0 = Mathf.RoundToInt(rect.x);
            int y0 = Mathf.RoundToInt(rect.y);
            int w = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            int flippedY0 = tex.height - y0 - h;

            int clampedX0 = Mathf.Clamp(x0, 0, tex.width);
            int clampedY0 = Mathf.Clamp(flippedY0, 0, tex.height);
            int clampedX1 = Mathf.Clamp(x0 + w, 0, tex.width);
            int clampedY1 = Mathf.Clamp(flippedY0 + h, 0, tex.height);
            int blockW = clampedX1 - clampedX0;
            int blockH = clampedY1 - clampedY0;
            if (blockW <= 0 || blockH <= 0) return;

            if (color.a >= 0.999f)
            {
                // Opaco: no hace falta leer lo que habia antes.
                var block = new Color[blockW * blockH];
                for (int i = 0; i < block.Length; i++) block[i] = color;
                tex.SetPixels(clampedX0, clampedY0, blockW, blockH, block);
                return;
            }

            // Semi-transparente (lineas de grilla, piso pintado, celeste de "caminado"): se mezcla
            // ("over" de Porter-Duff) con lo que YA HABIA en la textura en vez de reemplazarlo --
            // si no, SetPixels tira el contenido anterior y el color translucido recien se mezcla
            // al renderizar el sprite, contra lo que haya DETRAS en la escena (el papel), no contra
            // el resto del dibujo (mismo resultado visual que GUI.DrawTexture da gratis en pantalla).
            var existing = tex.GetPixels(clampedX0, clampedY0, blockW, blockH);
            for (int i = 0; i < existing.Length; i++)
            {
                var bg = existing[i];
                float outA = color.a + bg.a * (1f - color.a);
                if (outA <= 0.0001f) { existing[i] = new Color(0f, 0f, 0f, 0f); continue; }
                float r = (color.r * color.a + bg.r * bg.a * (1f - color.a)) / outA;
                float g = (color.g * color.a + bg.g * bg.a * (1f - color.a)) / outA;
                float b = (color.b * color.a + bg.b * bg.a * (1f - color.a)) / outA;
                existing[i] = new Color(r, g, b, outA);
            }
            tex.SetPixels(clampedX0, clampedY0, blockW, blockH, existing);
        }
    }
}
