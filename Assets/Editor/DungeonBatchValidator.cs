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

        int[] seeds = { settings.seed, 1, 2, 3, 999, 777 };
        bool allOk = true;

        foreach (var seed in seeds)
        {
            var gen = new DungeonGenerator();
            List<DungeonFloor> floors;
            List<string> log;
            try
            {
                floors = gen.GenerateDungeon(settings.floorCount, settings.size, settings.size, seed,
                    out log, settings.stairPairsPerFloor,
                    settings.bossFloorStart, settings.bossFloorInterval, settings.voidFraction);
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
                var bossFloors = string.Join(",", floors.FindAll(f => f.HasBossRoom).ConvertAll(f => f.Index));
                Debug.Log($"[DUNGEON-VALIDATE] seed={seed} OK - {floors.Count} pisos, {settings.size}x{settings.size}, pisos con jefe=[{bossFloors}].");
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
