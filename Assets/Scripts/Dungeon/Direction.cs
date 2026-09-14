using System;

namespace DungeonGen
{
    public enum Direction { North = 0, East = 1, South = 2, West = 3 }

    public static class DirectionExtensions
    {
        public static readonly Direction[] All = { Direction.North, Direction.East, Direction.South, Direction.West };

        public static Direction Opposite(this Direction d) => d switch
        {
            Direction.North => Direction.South,
            Direction.South => Direction.North,
            Direction.East => Direction.West,
            Direction.West => Direction.East,
            _ => throw new ArgumentOutOfRangeException()
        };

        public static (int dx, int dy) Offset(this Direction d) => d switch
        {
            Direction.North => (0, 1),
            Direction.South => (0, -1),
            Direction.East => (1, 0),
            Direction.West => (-1, 0),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}
