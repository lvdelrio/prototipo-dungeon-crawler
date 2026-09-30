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

        [Header("Encuentros aleatorios (sistema real de Etrian Odyssey)")]
        [Tooltip("Cada celda Normal tiene un 'valor de peligro' al azar entre este minimo y el maximo (0-5). Al pisarla, ese valor se suma a un contador interno de pasos.")]
        [Range(0, 5)]
        public int dangerValueMin = 0;
        [Tooltip("Maximo del valor de peligro por celda (0-5).")]
        [Range(0, 5)]
        public int dangerValueMax = 5;
        [Tooltip("Minimo del limite oculto: cuando el contador de pasos supera este limite (elegido al azar tras cada combate o al entrar a un piso nuevo), aparece un encuentro y el contador se reinicia.")]
        public int encounterThresholdMin = 16;
        [Tooltip("Maximo del limite oculto de pasos.")]
        public int encounterThresholdMax = 64;
    }
}
