using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DungeonGen;
using Gameplay;

public static class DungeonSceneBuilder
{
    [MenuItem("Dungeon/Build Test Scene")]
    public static void Build()
    {
        var settings = GetOrCreateSettings();
        var eventTable = GetOrCreateEventTable();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraGo = new GameObject("Main Camera");
        var cam = cameraGo.AddComponent<Camera>();
        cameraGo.tag = "MainCamera";
        cameraGo.AddComponent<AudioListener>();
        cam.nearClipPlane = 0.05f;
        var combatFeedback = cameraGo.AddComponent<CombatFeedback>();

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);

        var managerGo = new GameObject("DungeonManager");
        var manager = managerGo.AddComponent<DungeonManager>();
        var builder = managerGo.AddComponent<DungeonLevelBuilder>();

        var playerGo = new GameObject("Player");
        var playerController = playerGo.AddComponent<GridPlayerController>();
        var cc = playerGo.AddComponent<CharacterController>();
        cc.height = 1.6f;
        cc.radius = 0.4f;
        cc.center = new Vector3(0, 0.8f, 0);
        cameraGo.transform.SetParent(playerGo.transform, false);
        cameraGo.transform.localPosition = new Vector3(0, 1.2f, 0);

        var hudGo = new GameObject("DebugHUD");
        var hud = hudGo.AddComponent<DebugHUD>();

        var minimapGo = new GameObject("MinimapUI");
        var minimap = minimapGo.AddComponent<MinimapUI>();

        var combatGo = new GameObject("CombatManager");
        var combatManager = combatGo.AddComponent<CombatManager>();
        var combatHudGo = new GameObject("CombatHUD");
        var combatHud = combatHudGo.AddComponent<CombatHUD>();
        var qteGo = new GameObject("QteManager");
        var qteManager = qteGo.AddComponent<QteManager>();
        var metaShopGo = new GameObject("MetaShopHUD");
        var metaShopHud = metaShopGo.AddComponent<MetaShopHUD>();

        manager.settings = settings;
        manager.eventTable = eventTable;
        manager.player = playerController;
        manager.levelBuilder = builder;
        manager.hud = hud;
        manager.combat = combatManager;
        playerController.dungeonManager = manager;
        playerController.cameraBobTarget = cameraGo.transform;
        hud.dungeonManager = manager;
        hud.player = playerController;
        minimap.dungeonManager = manager;
        minimap.player = playerController;
        combatHud.combatManager = combatManager;
        combatManager.qteManager = qteManager;
        combatManager.feedback = combatFeedback;
        metaShopHud.dungeonManager = manager;
        metaShopHud.combatManager = combatManager;

        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/DungeonTest.unity");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("DungeonTest scene creada correctamente en Assets/Scenes/DungeonTest.unity");
    }

    private static DungeonSettings GetOrCreateSettings()
    {
        const string path = "Assets/Data/DefaultDungeonSettings.asset";
        var existing = AssetDatabase.LoadAssetAtPath<DungeonSettings>(path);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder("Assets/Data"))
            AssetDatabase.CreateFolder("Assets", "Data");

        var settings = ScriptableObject.CreateInstance<DungeonSettings>();
        settings.size = 16;
        settings.floorCount = 3;
        settings.stairPairsPerFloor = 2;
        settings.seed = 12345;
        settings.eventPercent = 0.12f;
        settings.cellSize = 4f;
        settings.wallHeight = 3f;
        settings.wallThickness = 0.2f;
        AssetDatabase.CreateAsset(settings, path);
        return settings;
    }

    private static EventTableAsset GetOrCreateEventTable()
    {
        const string path = "Assets/Data/DefaultEventTable.asset";
        var existing = AssetDatabase.LoadAssetAtPath<EventTableAsset>(path);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder("Assets/Data"))
            AssetDatabase.CreateFolder("Assets", "Data");

        var table = ScriptableObject.CreateInstance<EventTableAsset>();
        table.entries = new List<EventEntry>(EventTable.Entries);
        AssetDatabase.CreateAsset(table, path);
        return table;
    }
}
