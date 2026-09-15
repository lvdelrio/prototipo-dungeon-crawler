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
                floors = gen.GenerateDungeon(settings.floorCount, settings.size, settings.size, seed,
                    settings.eventPercent, out log, eventTable.entries, settings.stairPairsPerFloor, settings.eventsPerFloor,
                    settings.bossFloorStart, settings.bossFloorInterval);
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

        // Prueba especifica del override "eventos por piso" (piso 0 sin eventos, piso1=6 exactos, piso2 usa el default).
        {
            var overrideCounts = new List<int> { 0, 6, -1 };
            var gen = new DungeonGenerator();
            try
            {
                var floors = gen.GenerateDungeon(settings.floorCount, settings.size, settings.size, 4242,
                    settings.eventPercent, out _, eventTable.entries, settings.stairPairsPerFloor, overrideCounts,
                    settings.bossFloorStart, settings.bossFloorInterval);
                var (ok, issues) = gen.ValidateDungeon(floors);
                int e0 = CountEvents(floors[0]);
                int e1 = floors.Count > 1 ? CountEvents(floors[1]) : -1;
                bool countsOk = e0 == 0 && (floors.Count <= 1 || e1 == 6);
                if (ok && countsOk)
                    Debug.Log($"[DUNGEON-VALIDATE] override eventsPerFloor OK (piso0={e0}, piso1={e1}).");
                else
                {
                    allOk = false;
                    Debug.LogError($"[DUNGEON-VALIDATE] override eventsPerFloor FALLÓ (piso0={e0}, piso1={e1}, ok={ok}).\n" + string.Join("\n", issues));
                }
            }
            catch (System.Exception ex)
            {
                allOk = false;
                Debug.LogError($"[DUNGEON-VALIDATE] override eventsPerFloor EXCEPCION: {ex}");
            }
        }

        if (allOk)
            Debug.Log("[DUNGEON-VALIDATE] RESULTADO FINAL: TODO OK");
        else
            Debug.LogError("[DUNGEON-VALIDATE] RESULTADO FINAL: HAY FALLOS");

        if (!allOk) EditorApplication.Exit(1);
    }

    private static int CountEvents(DungeonFloor floor)
    {
        int c = 0;
        for (int x = 0; x < floor.Width; x++)
            for (int y = 0; y < floor.Height; y++)
                if (floor.Cells[x, y].Type == CellType.Event) c++;
        return c;
    }
}
