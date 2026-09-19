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

        // Segunda luz de relleno, mas debil y desde el angulo casi opuesto: con una sola luz
        // direccional, las paredes que quedan de espaldas a ella se ven casi negras mientras las
        // de frente quedan muy claras aunque sean exactamente el mismo material (confirmado con
        // raycasts: mismo GameObject "Wall", mismo color base, se veian muy distintas igual). Solo
        // ambient no alcanzaba a corregirlo; con esta segunda luz ninguna pared queda totalmente
        // a oscuras sin importar hacia donde mire.
        var fillLightGo = new GameObject("Fill Light");
        var fillLight = fillLightGo.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.intensity = 0.55f;
        fillLight.color = new Color(0.85f, 0.9f, 1f);
        fillLightGo.transform.rotation = Quaternion.Euler(35, 155, 0);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);

        // Niebla de distancia: los pasillos lejanos y las paredes del fondo se pierden en la
        // penumbra en vez de verse nitidos de punta a punta -- da sensacion de "todavia no sé qué
        // hay ahí" en vez de que el mapa entero quede a la vista apenas se dobla una esquina.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.05f, 0.05f, 0.08f);
        RenderSettings.fogStartDistance = 8f;
        RenderSettings.fogEndDistance = 26f;

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
        var battleStageGo = new GameObject("BattleStageController");
        var battleStage = battleStageGo.AddComponent<BattleStageController>();
        var dialogueGo = new GameObject("DialogueManager");
        var dialogueManager = dialogueGo.AddComponent<DialogueManager>();
        var dialogueHudGo = new GameObject("DialogueHUD");
        var dialogueHud = dialogueHudGo.AddComponent<DialogueHUD>();
        var enemyBarsGo = new GameObject("EnemyHealthBarHUD");
        var enemyBarsHud = enemyBarsGo.AddComponent<EnemyHealthBarHUD>();
        var pauseMenuGo = new GameObject("PauseMenuManager");
        var pauseMenu = pauseMenuGo.AddComponent<PauseMenuManager>();
        var pauseMenuHudGo = new GameObject("PauseMenuHUD");
        var pauseMenuHud = pauseMenuHudGo.AddComponent<PauseMenuHUD>();
        var ambientGo = new GameObject("AmbientParticles");
        var ambientParticles = ambientGo.AddComponent<AmbientParticles>();

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
        battleStage.combatManager = combatManager;
        battleStage.dungeonCamera = cam;
        battleStage.dungeonAudioListener = cameraGo.GetComponent<AudioListener>();
        battleStage.dissolveMaterial = GetOrCreateDissolveMaterial();
        battleStage.battleSceneName = BattleSceneBuilder.SceneName;
        HitEffectImporter.EnsureSliced();
        battleStage.hitImpactFrames = HitEffectImporter.LoadFrames();
        battleStage.elementalBurstMaterial = GetOrCreateElementalBurstMaterial();
        battleStage.slashSkillMaterial = GetOrCreateMaterial("Assets/Data/SlashSkillMaterial.mat", "Custom/SlashBurst");
        battleStage.strikeSkillMaterial = GetOrCreateMaterial("Assets/Data/StrikeSkillMaterial.mat", "Custom/StrikeBurst");
        battleStage.pierceSkillMaterial = GetOrCreateMaterial("Assets/Data/PierceSkillMaterial.mat", "Custom/PierceBurst");
        battleStage.fireSkillMaterial = GetOrCreateMaterial("Assets/Data/FireSkillMaterial.mat", "Custom/FireBurst");
        battleStage.iceSkillMaterial = GetOrCreateMaterial("Assets/Data/IceSkillMaterial.mat", "Custom/IceBurst");
        battleStage.voltSkillMaterial = GetOrCreateMaterial("Assets/Data/VoltSkillMaterial.mat", "Custom/VoltBurst");
        playerController.dialogueManager = dialogueManager;
        dialogueHud.dialogueManager = dialogueManager;
        enemyBarsHud.combatManager = combatManager;
        enemyBarsHud.battleStage = battleStage;
        playerController.pauseMenu = pauseMenu;
        hud.pauseMenu = pauseMenu;
        minimap.pauseMenu = pauseMenu;
        pauseMenuHud.pauseMenu = pauseMenu;
        pauseMenuHud.dungeonManager = manager;
        pauseMenuHud.combatManager = combatManager;
        ambientParticles.dungeonManager = manager;
        ambientParticles.player = playerController;
        ambientParticles.followTarget = cameraGo.transform;

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
        if (existing != null)
        {
            // El asset ya existia de antes: se le fuerza el valor actual de eventPercent, asi los
            // cambios de balance (menos eventos en el mapa) aplican tambien a proyectos existentes.
            existing.eventPercent = 0.06f;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Data"))
            AssetDatabase.CreateFolder("Assets", "Data");

        var settings = ScriptableObject.CreateInstance<DungeonSettings>();
        settings.size = 16;
        settings.floorCount = 3;
        settings.stairPairsPerFloor = 2;
        settings.seed = 12345;
        settings.eventPercent = 0.06f;
        settings.cellSize = 4f;
        settings.wallHeight = 3f;
        settings.wallThickness = 0.2f;
        AssetDatabase.CreateAsset(settings, path);
        return settings;
    }

    private static Material GetOrCreateDissolveMaterial()
    {
        const string path = "Assets/Data/EnemyDissolveMaterial.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder("Assets/Data"))
            AssetDatabase.CreateFolder("Assets", "Data");

        var shader = Shader.Find("Custom/Dissolve");
        var material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Material GetOrCreateElementalBurstMaterial()
    {
        return GetOrCreateMaterial("Assets/Data/ElementalBurstMaterial.mat", "Custom/ElementalBurst");
    }

    // Shaders de habilidad por elemento (mas elaborados que el anillo simple de arriba): cada uno
    // vive en su propio asset, para que se puedan ajustar por separado en el Inspector si hace falta.
    private static Material GetOrCreateMaterial(string path, string shaderName)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder("Assets/Data"))
            AssetDatabase.CreateFolder("Assets", "Data");

        var shader = Shader.Find(shaderName);
        var material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
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
