using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Reconfigura los 12 iconos del pack "Clean Vector Icons" (copiados a mano en
// Assets/Sprites/MapIcons, ver PlayerMapEditorHUD) como Sprite/Single -- al no venir de un
// .unitypackage importado de verdad, Unity los trae por defecto como Texture normal, no Sprite.
// Mismo patron que HitEffectImporter para HitImpact.png.
public static class MapIconImporter
{
    public const string Folder = "Assets/Sprites/MapIcons";

    // name -> archivo. Elegidos del pack original para calzar con los marcadores que ya existen
    // en DungeonMapRenderer.MarkerColor/MarkerLegend, mas un puñado de simbolos genericos para
    // notas libres del jugador (Question/Avoid/Important/Alert).
    public static readonly string[] IconNames =
    {
        "Icon_Flag", "Icon_Question", "Icon_DoorLocked", "Icon_DoorOpen", "Icon_Treasure",
        "Icon_Danger", "Icon_Lore", "Icon_Shortcut", "Icon_Trap", "Icon_Avoid",
        "Icon_Important", "Icon_Alert",
    };

    [MenuItem("Dungeon/Configure Map Icons")]
    public static void EnsureConfigured()
    {
        foreach (var name in IconNames)
        {
            string path = $"{Folder}/{name}.png";
            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning($"MapIconImporter: falta {path}.");
                continue;
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"MapIconImporter: no se pudo obtener el TextureImporter de {path}.");
                continue;
            }
            if (importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single)
                continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Bilinear; // son iconos vectoriales prolijos, no pixel art -- Point se veria dentado
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = 100f;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }
    }

    public static Sprite Load(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/{name}.png");

    public static Dictionary<string, Sprite> LoadAll()
    {
        var dict = new Dictionary<string, Sprite>();
        foreach (var name in IconNames)
        {
            var sprite = Load(name);
            if (sprite != null) dict[name] = sprite;
        }
        return dict;
    }
}
