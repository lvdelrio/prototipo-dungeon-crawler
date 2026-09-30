using UnityEditor;
using UnityEngine;

// Ajustes de importacion automaticos para las capas de parallax del bosque (ver
// Gameplay/ForestParallaxWallFactory.cs): sin esto, Unity importa el PNG con su configuracion por
// defecto (Alpha Is Transparency apagado, Wrap Repeat, mipmaps) y la transparencia sale mal
// (huecos negros solidos en vez de ver la capa de atras) ademas de filtrado raro en los bordes al
// repetirse. Se aplica solo a archivos bajo Assets/Sprites/Forest/ -- no toca ninguna otra textura
// del proyecto (que hoy no tiene ninguna, todo shader procedural, pero por las dudas).
public class ForestLayerTextureImporter : AssetPostprocessor
{
    private const string FolderPrefix = "Assets/Sprites/Forest/";

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(FolderPrefix)) return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false; // capa 2D siempre a la misma distancia relativa de la camara, sin beneficio real de mipmaps
        importer.wrapMode = TextureWrapMode.Clamp; // no es un tile que se repite -- Repeat sangra el borde opuesto por los bordes de la imagen
        importer.filterMode = FilterMode.Bilinear; // arte pintado a mano, no pixel art -- nada de Point
        importer.sRGBTexture = true;
    }
}
