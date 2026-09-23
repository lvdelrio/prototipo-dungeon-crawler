using UnityEngine;
using DungeonGen;

namespace Gameplay
{
    // Dibujo del mapa de un piso (grilla + paredes + marcadores), en IMGUI puro (GUI.DrawTexture),
    // factorizado de MinimapUI para que el mismo dibujo se pueda usar tanto en el minimapa de
    // esquina (un solo piso, el actual) como en la pestaña "Mapa" del menu de pausa (cualquier piso
    // ya explorado, con sub-pestanas -- ver PauseMenuHUD.DrawMap). Paleta calcada de un automapa
    // real de Etrian Odyssey: piso celeste, vacio azul oscuro.
    public static class DungeonMapRenderer
    {
        public static readonly Color VoidColor = new Color(0.04f, 0.1f, 0.2f);
        public static readonly Color PathColor = new Color(0.47f, 0.67f, 0.82f);

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
        public static void Draw(Vector2 origin, DungeonFloor floor, bool playerMode, int cellPixelSize, int wallPixelThickness,
            GridPlayerController player, bool showPlayerMarker, FoeController foe = null, bool foeAlwaysVisible = false)
        {
            if (floor == null) return;

            int w = floor.Width;
            int h = floor.Height;

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    var cell = floor.Cells[x, y];
                    float px = origin.x + x * cellPixelSize;
                    float py = origin.y + (h - 1 - y) * cellPixelSize;

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

                    DrawRect(new Rect(px, py, cellPixelSize, cellPixelSize), FloorColor(cell, floor.TrapDisabled));

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

            if (foe != null && floor.InBounds(foe.X, foe.Y) && (foeAlwaysVisible || floor.Cells[foe.X, foe.Y].Discovered))
            {
                float fpx = origin.x + foe.X * cellPixelSize + cellPixelSize / 2f;
                float fpy = origin.y + (h - 1 - foe.Y) * cellPixelSize + cellPixelSize / 2f;
                float m = cellPixelSize * 0.6f;
                DrawRect(new Rect(fpx - m / 2f, fpy - m / 2f, m, m), foe.IsChasing ? FoeChaseColor : FoeCalmColor);
            }

            if (showPlayerMarker && player != null)
            {
                float ppx = origin.x + player.CellX * cellPixelSize + cellPixelSize / 2f;
                float ppy = origin.y + (h - 1 - player.CellY) * cellPixelSize + cellPixelSize / 2f;
                var (fx, fy) = player.Facing.Offset();
                float facingPx = ppx + fx * (cellPixelSize * 0.35f);
                float facingPy = ppy - fy * (cellPixelSize * 0.35f);

                DrawRect(new Rect(ppx - 4, ppy - 4, 8, 8), Color.magenta);
                DrawRect(new Rect(facingPx - 2, facingPy - 2, 4, 4), Color.magenta);
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
            if (cell.IsMandatoryHazard && !cell.EventConsumed)
                return new Color(0.6f, 0.35f, 0.05f);
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
    }
}
