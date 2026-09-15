using System.Collections.Generic;
using UnityEngine;

namespace DungeonGen
{
    [CreateAssetMenu(fileName = "DungeonSettings", menuName = "Dungeon/Dungeon Settings")]
    public class DungeonSettings : ScriptableObject
    {
        [Header("Tamano del mapa (cuadrado, celdas por lado - igual para todos los pisos)")]
        public int size = 16;

        [Header("Pisos")]
        public int floorCount = 3;
        public int stairPairsPerFloor = 2;

        [Header("Semilla (0 = aleatoria cada vez que se abre la escena)")]
        public int seed = 0;

        [Header("Eventos (porcentaje global usado como valor por defecto)")]
        [Range(0f, 0.5f)]
        public float eventPercent = 0.12f;

        [Header("Eventos por piso (opcional)")]
        [Tooltip("Cantidad exacta de eventos para el piso en ese indice. -1 = usar eventPercent para ese piso. Si la lista es mas corta que la cantidad de pisos, los pisos sin entrada tambien usan eventPercent.")]
        public List<int> eventsPerFloor = new List<int>();

        [Header("Escala del mundo (unidades Unity)")]
        public float cellSize = 4f;
        public float wallHeight = 3f;
        public float wallThickness = 0.2f;

        [Header("Vacio entre caminos (celdas podadas, como en el mapa real de Etrian Odyssey)")]
        [Tooltip("Fraccion de las celdas normales que se convierten en roca solida (Void) despues de generar el laberinto. Solo se podan puntas muertas que no hacen falta para llegar a ningun punto importante, asi que la solucionabilidad nunca se rompe. 0 = laberinto denso (sin vacios), como antes.")]
        [Range(0f, 0.7f)]
        public float voidFraction = 0.4f;

        [Header("Piso de jefe (sala grande obligatoria con jefe + escalera)")]
        [Tooltip("Primer piso (indice, empezando en 0) que tiene sala de jefe.")]
        public int bossFloorStart = 2;
        [Tooltip("Cada cuantos pisos se repite la sala de jefe despues del primero. 0 = desactivar salas de jefe.")]
        public int bossFloorInterval = 2;
    }
}
