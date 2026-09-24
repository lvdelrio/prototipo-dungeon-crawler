namespace Gameplay
{
    // Variantes del tilemap de piso de bosque (ver GroundTileFactory). Cada una define solo el
    // color base y que props chatos se agregan encima -- la geometria real sigue siendo la misma
    // base cuadrada que antes tenia BuildFloorTile, asi la altura del piso no cambia.
    public enum GroundTileKind
    {
        Grass,
        GrassLeaves,
        GrassRockDirt,
        Dirt,
        Leaves,
        Path
    }
}
