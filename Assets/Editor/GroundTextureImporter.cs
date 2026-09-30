using UnityEditor;
using UnityEngine;

// Ajustes de importacion automaticos para texturas de piso que se repiten (ver
// Gameplay/GroundTileFactory.cs, GrassGroundMaterial) -- a diferencia de las capas de parallax de
// Assets/Sprites/Forest/ (Clamp, sin mipmaps, se ven una sola vez a distancia fija de la pared),
// una textura de PISO se repite en world-space sobre toda la extension del piso y se ve a
// distancias muy variadas segun por donde camine el jugador: Repeat (para que se pueda mosaiquear)
// y mipmaps prendidos (para que no titile/aliasee de lejos) son la config correcta aca, lo opuesto
// de lo que hace falta para una capa de pared que se ve una sola vez.
public class GroundTextureImporter : AssetPostprocessor
{
    private const string FolderPrefix = "Assets/Sprites/Ground/";

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(FolderPrefix)) return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = false; // textura RGB opaca, sin canal alpha
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Bilinear;
        importer.sRGBTexture = true;
    }
}
