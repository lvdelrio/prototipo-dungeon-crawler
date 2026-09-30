using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonGen
{
    // Sala de autor con forma fija (ver DungeonGenerator.PlacePredefinedRooms): a diferencia del
    // resto del piso, que Carve() genera celda a celda, esto es un bloque de forma prediseñada
    // (circular-ish, en L, con pilares...) que se ESTAMPA entero en el grid antes de correr el
    // laberinto, y despues se conecta al resto del piso por sus puertas declaradas -- mismo espiritu
    // que las salas prefabricadas de Barony (ver investigacion previa), adaptado a que ESTE
    // generador ya tiene su propio sistema de vacio real/validacion y no usa archivos de mapa
    // aparte: la plantilla vive como dato en código, no como asset.
    //
    // Formato de Rows: Rows[0] es la fila MAS AL NORTE (la de arriba al leer el string en el
    // codigo), Rows[Height-1] la mas al sur -- mismo orden en que se lee un mapa dibujado a mano.
    // '.' = celda de piso segura, '^' = celda de piso PELIGROSA (misma logica de caminabilidad que
    // '.', pero lastima al pisarla -- ver DungeonGenerator.TryStampRoomAt/DungeonManager.
    // OnPlayerEnterCell, DungeonCell.IsPredefinedRoomHazard: NUNCA desactivable, es personalidad de
    // sala fija, no un mecanismo como el disparador de flechas de AddTrapRoom -- solo se puede
    // esquivar, nunca apagar), '$' = cofre (celda de piso normal que ademas es CellType.Treasure --
    // pedido puntual: "algo de valor cerca de las trampas" para que valga la pena el riesgo, NO
    // cuenta para el cupo de 3-5 cofres garantizados de EnsureTreasure, es a mayores), '#' =
    // bloqueada DE VERDAD (no es parte del footprint caminable: si cae DENTRO del rectangulo Width x
    // Height se estampa como Void solido, un pilar/obstaculo real que hay que rodear; fuera del
    // rectangulo simplemente no existe, asi se logran formas no rectangulares como una sala en L).
    // Las puertas SIEMPRE tienen que caer sobre el borde exterior de la caja Width x Height que
    // corresponda a su direccion (fila 0 para North, fila Height-1 para South, columna 0 para West,
    // columna Width-1 para East) -- RoomTemplateLibrary.ValidateAll() lo chequea al arrancar.
    public class RoomTemplate
    {
        public readonly string Name;
        public readonly int Width;
        public readonly int Height;
        public readonly string[] Rows;
        public readonly List<(int x, int y, Direction dir)> Doors;

        public RoomTemplate(string name, string[] rows, List<(int x, int y, Direction dir)> doors)
        {
            Name = name;
            Rows = rows;
            Height = rows.Length;
            Width = rows.Length > 0 ? rows[0].Length : 0;
            Doors = doors;
        }

        private char CharAt(int x, int y)
        {
            int rowIndex = Height - 1 - y;
            return Rows[rowIndex][x];
        }

        // (0,0) = esquina suroeste (Direction.South=(0,-1), Direction.West=(-1,0) -- IsIsolatedZone,
        // CellCenter y el resto del generador ya usan esa misma convencion de ejes).
        public bool IsOpen(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return false;
            char c = CharAt(x, y);
            return c == '.' || c == '^' || c == '$';
        }

        public bool IsHazard(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return false;
            return CharAt(x, y) == '^';
        }

        public bool IsReward(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return false;
            return CharAt(x, y) == '$';
        }
    }

    public static class RoomTemplateLibrary
    {
        public static readonly RoomTemplate[] All =
        {
            // Salon simple 3x3, atravesable de norte a sur -- la variante mas chica, sirve de
            // "sala de paso" con mas aire que un pasillo de 1 celda.
            new RoomTemplate(
                "SalonSimple",
                new[]
                {
                    "...",
                    "...",
                    "...",
                },
                new List<(int, int, Direction)>
                {
                    (1, 2, Direction.North),
                    (1, 0, Direction.South),
                }),

            // Sala con pilar central 5x5 y las 4 salidas posibles -- el jugador tiene que rodear el
            // pilar para cruzar de una puerta a la otra, en vez de una linea recta. Ademas, un ^
            // (celda peligrosa, ver DungeonCell.IsPredefinedRoomHazard) pegado a un lado del pilar:
            // NUNCA se puede desactivar (a diferencia de la trampa de flechas de AddTrapRoom), asi
            // que rodear por ese lado es una decision real -- atajo con riesgo vs. el lado libre. Un
            // $ (cofre, ver RoomTemplate.IsReward) justo al lado del ^: "algo de valor cerca de las
            // trampas" para que arriesgarse por ese lado valga la pena de verdad.
            new RoomTemplate(
                "SalaConPilar",
                new[]
                {
                    ".....",
                    ".....",
                    ".^#..",
                    ".$...",
                    ".....",
                },
                new List<(int, int, Direction)>
                {
                    (2, 4, Direction.North),
                    (2, 0, Direction.South),
                    (0, 2, Direction.West),
                    (4, 2, Direction.East),
                }),

            // Sala en L 5x5: una "pierna" vertical angosta (3 celdas de ancho) que se une a un
            // "pie" horizontal en la base -- el camino real dentro de la sala es un codo, no una
            // linea recta, y la forma del piso deja de ser un simple rectangulo. Un ^ en el pie,
            // justo en la fila de entrada desde la puerta oeste -- se esquiva subiendo una fila,
            // pero solo si el jugador lo nota antes de pisarlo. El $ justo arriba del ^ premia a
            // quien se desvia para esquivarlo en vez de mandarse de una.
            new RoomTemplate(
                "SalaEnL",
                new[]
                {
                    "##...",
                    "##...",
                    "##...",
                    "..$..",
                    "..^..",
                },
                new List<(int, int, Direction)>
                {
                    (3, 4, Direction.North),
                    (0, 0, Direction.West),
                }),

            // Cruce compacto 4x3 con nichos en las 4 esquinas (bloqueadas): 4 puertas, una por lado,
            // pensado como una pequeña sala de encrucijada en vez de una interseccion de pasillos
            // sin forma.
            new RoomTemplate(
                "CruceConNichos",
                new[]
                {
                    "#..#",
                    "....",
                    "#..#",
                },
                new List<(int, int, Direction)>
                {
                    (1, 2, Direction.North),
                    (2, 0, Direction.South),
                    (0, 1, Direction.West),
                    (3, 1, Direction.East),
                }),

            // Nicho chico 3x2, una sola puerta: variedad de silueta minima para tramos donde no
            // entra nada mas grande, se siente como una pausa/mirador antes de seguir.
            new RoomTemplate(
                "NichoChico",
                new[]
                {
                    "...",
                    "...",
                },
                new List<(int, int, Direction)>
                {
                    (1, 0, Direction.South),
                }),

            // Salon grande 6x5 con las 4 salidas y un par de pilares chicos -- la sala mas grande
            // del pool, pedido puntual "agregar una trampa en salas grandes": el ^ queda bien
            // adentro del salon, con las 4 celdas vecinas libres (nunca sobre el unico paso de un
            // cruce angosto como CruceConNichos, ahi cualquier trampa seria imposible de esquivar
            // para dos de las cuatro puertas) y el $ pegado al lado.
            new RoomTemplate(
                "SalonGrande",
                new[]
                {
                    "......",
                    "......",
                    ".##...",
                    "....^.",
                    "....$.",
                },
                new List<(int, int, Direction)>
                {
                    (2, 4, Direction.North),
                    (3, 0, Direction.South),
                    (0, 1, Direction.West),
                    (5, 3, Direction.East),
                }),

            // Salas propias del castillo: variantes seguras y con trampa. Los nombres tambien
            // permiten vestirlas con mobiliario tematico desde DungeonLevelBuilder.
            CastleRoom("CastleDiningSafe",
                new[] { ".....", ".#.#.", ".....", ".#.#.", "....." }),
            CastleRoom("CastleDiningTrap",
                new[] { ".....", ".#.#.", "..^..", ".#.$.", "....." }),
            CastleRoom("CastleKitchenSafe",
                new[] { ".....", ".##..", ".....", "..##.", "....." }),
            CastleRoom("CastleKitchenTrap",
                new[] { ".....", "..#..", ".^$..", "..#..", "....." }),
            CastleRoom("CastlePantrySafe",
                new[] { ".....", ".##..", ".....", "..##.", "....." }),
            CastleRoom("CastlePantryTrap",
                new[] { ".....", ".#...", "..^$.", "...#.", "....." }),
            // Sala volcanica que puede reservarse en cualquier piso interior del castillo. La
            // cascada y su cuenca son decoracion transitable, como la cascada de agua.
            CastleRoom("CastleLavafall",
                new[] { ".....", ".....", ".....", ".....", "....." }),
        };

        private static RoomTemplate CastleRoom(string name, string[] rows) => new RoomTemplate(
            name, rows, new List<(int, int, Direction)>
            {
                (2, 4, Direction.North),
                (2, 0, Direction.South),
                (0, 2, Direction.West),
                (4, 2, Direction.East),
            });

        public static RoomTemplate GetCastleVariant(string prefix, bool trapped)
        {
            string name = prefix + (trapped ? "Trap" : "Safe");
            foreach (var template in All)
                if (template.Name == name) return template;
            return null;
        }

        static RoomTemplateLibrary()
        {
            ValidateAll();
        }

        // Falla fuerte apenas se toca la clase (constructor estatico) si alguna plantilla esta mal
        // armada -- mismo espiritu que DungeonGenerator.ValidateDungeon: preferible reventar en el
        // acto a generar mazmorras con salas corruptas en silencio.
        public static void ValidateAll()
        {
            foreach (var t in All)
            {
                if (t.Width <= 0 || t.Height <= 0)
                    throw new InvalidOperationException($"RoomTemplate '{t.Name}': dimensiones invalidas.");
                foreach (var row in t.Rows)
                    if (row.Length != t.Width)
                        throw new InvalidOperationException($"RoomTemplate '{t.Name}': todas las filas deben medir {t.Width}, encontre una de {row.Length}.");

                var openCells = new List<(int x, int y)>();
                for (int x = 0; x < t.Width; x++)
                    for (int y = 0; y < t.Height; y++)
                        if (t.IsOpen(x, y)) openCells.Add((x, y));
                if (openCells.Count == 0)
                    throw new InvalidOperationException($"RoomTemplate '{t.Name}': no tiene ninguna celda de piso.");

                // Todas las celdas abiertas tienen que ser UN solo componente conexo (4-vecinos) --
                // si no, una parte de la sala quedaria inalcanzable desde la otra apenas se estampe.
                var visited = new HashSet<(int, int)>();
                var stack = new Stack<(int, int)>();
                stack.Push(openCells[0]);
                visited.Add(openCells[0]);
                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    foreach (var dir in DirectionExtensions.All)
                    {
                        var (ox, oy) = dir.Offset();
                        var next = (cx + ox, cy + oy);
                        if (t.IsOpen(next.Item1, next.Item2) && visited.Add(next))
                            stack.Push(next);
                    }
                }
                if (visited.Count != openCells.Count)
                    throw new InvalidOperationException($"RoomTemplate '{t.Name}': tiene celdas de piso desconectadas entre si.");

                if (t.Doors == null || t.Doors.Count == 0)
                    throw new InvalidOperationException($"RoomTemplate '{t.Name}': necesita al menos una puerta.");

                foreach (var (dx, dy, dir) in t.Doors)
                {
                    if (!t.IsOpen(dx, dy))
                        throw new InvalidOperationException($"RoomTemplate '{t.Name}': la puerta en ({dx},{dy}) no cae sobre una celda de piso.");
                    bool onCorrectEdge = dir switch
                    {
                        Direction.North => dy == t.Height - 1,
                        Direction.South => dy == 0,
                        Direction.East => dx == t.Width - 1,
                        Direction.West => dx == 0,
                        _ => false,
                    };
                    if (!onCorrectEdge)
                        throw new InvalidOperationException($"RoomTemplate '{t.Name}': la puerta en ({dx},{dy}) hacia {dir} no esta sobre el borde que le corresponde.");
                }
            }
        }
    }
}
