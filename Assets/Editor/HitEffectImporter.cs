using UnityEditor;
using UnityEngine;

// Configura el sprite sheet de la animacion de impacto (HitImpact.png = hit_intento-0001.png del
// usuario: una tira horizontal de 6 frames de 37x44, 222x44 en total) como "Multiple Sprite" con
// point filtering (pixel art), y expone las 6 sprites en orden de animacion para Gameplay/
// HitImpactEffect.cs.
public static class HitEffectImporter
{
    public const string TexturePath = "Assets/Sprites/Effects/HitImpact.png";
    private const int FrameW = 37, FrameH = 44, ImageH = 44;

    [MenuItem("Dungeon/Slice Hit Impact Sprite")]
    public static void EnsureSliced()
    {
        var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"No se encontro el importer para {TexturePath}. ¿Se copio el archivo?");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.spritePixelsPerUnit = 37f;
        importer.alphaIsTransparency = true;

        // Tira horizontal de una sola fila: los 6 frames van de izquierda a derecha, todos con
        // Y=0 (la imagen mide exactamente FrameH de alto, asi que no hace falta convertir el
        // origen Y de "arriba-izquierda" a "abajo-izquierda" de Unity: coinciden).
        const int frameCount = 6;
        var spritesheet = new SpriteMetaData[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            spritesheet[i] = new SpriteMetaData
            {
                name = $"HitImpact_frame{i}",
                rect = new Rect(i * FrameW, 0, FrameW, FrameH),
                pivot = new Vector2(0.5f, 0.5f),
                alignment = (int)SpriteAlignment.Center,
            };
        }

        importer.spritesheet = spritesheet;
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
        // SaveAndReimport a veces no alcanza a materializar todos los sub-assets nuevos en la
        // misma pasada (visto en la practica: solo quedaban 3 de 6 sprites cargables). Se fuerza
        // un reimport sincronico explicito para garantizar que los 6 queden disponibles ya mismo.
        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Debug.Log($"HitImpact.png recortado en {frameCount} frames.");
    }

    [MenuItem("Dungeon/Debug Hit Impact Sprites")]
    public static void DebugPrintSprites()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(TexturePath);
        Debug.Log($"LoadAllAssetsAtPath devolvio {assets.Length} objetos:");
        foreach (var a in assets)
            Debug.Log($" - {a.GetType().Name} '{a.name}'");
    }

    // Devuelve las 6 sprites en orden de animacion (frame0..frame5); null si todavia no se recorto.
    public static Sprite[] LoadFrames()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(TexturePath);
        var frames = new Sprite[6];
        foreach (var asset in assets)
        {
            if (asset is Sprite sprite && sprite.name.StartsWith("HitImpact_frame"))
            {
                int idx = int.Parse(sprite.name.Substring("HitImpact_frame".Length));
                if (idx >= 0 && idx < frames.Length) frames[idx] = sprite;
            }
        }
        return frames;
    }
}
