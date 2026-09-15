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

        [Header("Semilla (0 = aleatoria cada vez)")]
        public int seed = 12345;

        [Header("Eventos")]
        [Range(0f, 0.5f)]
        public float eventPercent = 0.12f;

        [Header("Escala del mundo (unidades Unity)")]
        public float cellSize = 4f;
        public float wallHeight = 3f;
        public float wallThickness = 0.2f;
    }
}
