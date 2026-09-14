using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DungeonGen;

public static class DungeonBatchValidator
{
    [MenuItem("Dungeon/Validate (Batch Log)")]
    public static void ValidateFromBatch()
    {
        var settings = AssetDatabase.LoadAssetAtPath<DungeonSettings>("Assets/Data/DefaultDungeonSettings.asset");
        var eventTable = AssetDatabase.LoadAssetAtPath<EventTableAsset>("Assets/Data/DefaultEventTable.asset");

        int[] seeds = { settings.seed, 1, 2, 3, 999, 777 };
        bool allOk = true;

        foreach (var seed in seeds)
        {
            var gen = new DungeonGenerator();
            List<DungeonFloor> floors;
            List<string> log;
            try
            {
                floors = gen.GenerateDungeon(settings.floorCount, settings.width, settings.height, seed,
                    settings.eventPercent, out log, eventTable.entries, settings.stairPairsPerFloor);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DUNGEON-VALIDATE] seed={seed} EXCEPCION: {ex}");
                allOk = false;
                continue;
            }

            var (ok, issues) = gen.ValidateDungeon(floors);
            if (ok)
            {
                Debug.Log($"[DUNGEON-VALIDATE] seed={seed} OK - {floors.Count} pisos, {settings.width}x{settings.height}.");
            }
            else
            {
                allOk = false;
                Debug.LogError($"[DUNGEON-VALIDATE] seed={seed} FALLÓ:\n" + string.Join("\n", issues));
            }
        }

        if (allOk)
            Debug.Log("[DUNGEON-VALIDATE] RESULTADO FINAL: TODO OK");
        else
            Debug.LogError("[DUNGEON-VALIDATE] RESULTADO FINAL: HAY FALLOS");

        if (!allOk) EditorApplication.Exit(1);
    }
}
