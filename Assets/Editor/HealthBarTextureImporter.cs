using UnityEditor;
using UnityEngine;

// Ajustes de importacion automaticos para las texturas de barra de vida/TP (recortadas de "Health
// Bar Asset Pack 2" de Adwit Rahman, ver Gameplay/HealthBarWidget.cs) -- mismo patron que
// GroundTextureImporter.cs, pero al reves: esto es pixel art CHICO que se dibuja a un tamano fijo
// de pantalla (nunca se repite en world-space), asi que Point (nada de blur en los bordes de la
// flecha) y Clamp (no tiene sentido moisaiquear un sprite de barra) son la config correcta.
public class HealthBarTextureImporter : AssetPostprocessor
{
    private const string FolderPrefix = "Assets/Resources/Sprites/UI/";

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(FolderPrefix)) return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true; // puntas en flecha con canal alpha real
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Point;
        importer.sRGBTexture = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
    }
}
