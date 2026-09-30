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

        // Niebla de distancia: limita lo que se puede ver hacia adelante a 3 CUADRANTES (celdas) a
        // lo sumo -- CAMBIAR ESTOS 2 NUMEROS (start/end, en "cuadrantes") es lo unico que hace
        // falta para ajustar cuanto se ve. Antes el degrade empezaba recien a partir de la celda 1
        // y terminaba de cerrar en la 3 (o sea, entre la 1 y la 3 todavia se distinguia bastante
        // geometria) -- ahora arranca casi de inmediato (0.6) y ya esta 100% negro a partir de la
        // 2.4, para que la sensacion de "no se que hay mas alla" sea real y no solo nominal.
        const float cellSizeForFog = 4f;
        const float fogStartInCells = 0.6f;
        const float fogEndInCells = 2.4f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.05f, 0.05f, 0.08f);
        RenderSettings.fogStartDistance = cellSizeForFog * fogStartInCells;
        RenderSettings.fogEndDistance = cellSizeForFog * fogEndInCells;

        var managerGo = new GameObject("DungeonManager");
        var manager = managerGo.AddComponent<DungeonManager>();
        var builder = managerGo.AddComponent<DungeonLevelBuilder>();
        // Cielo estrellado del Bioma 2 (piso/techo, ver DungeonLevelBuilder.BuildFloorTile/
        // BuildCeilingTile) -- a diferencia del resto de los materiales de DungeonLevelBuilder
        // (que se dejan null a proposito y caen al color solido de ApplyMaterial), estos SI
        // necesitan su shader propio para que el campo de estrellas exista.
        builder.biome2CeilingMaterial = GetOrCreateMaterial("Assets/Data/Biome2CeilingMaterial.mat", "Custom/StarrySky");
        builder.biome2FloorMaterial = GetOrCreateMaterial("Assets/Data/Biome2FloorMaterial.mat", "Custom/StarlitFloor");
        builder.stairsAuraMaterial = GetOrCreateMaterial("Assets/Data/StairsAuraMaterial.mat", "Custom/StairsAura");

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

        var mainMenuManagerGo = new GameObject("MainMenuManager");
        var mainMenuManager = mainMenuManagerGo.AddComponent<MainMenuManager>();
        var mainMenuHudGo = new GameObject("MainMenuHUD");
        var mainMenuHud = mainMenuHudGo.AddComponent<MainMenuHUD>();

        manager.settings = settings;
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
        combatHud.battleStage = battleStage;
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
        battleStage.impactBurstMaterial = GetOrCreateMaterial("Assets/Data/ImpactBurstMaterial.mat", "Custom/ImpactBurst");
        // Los 6 comparten EL MISMO shader (Custom/SkillBurst): la diferencia entre habilidades es
        // solo el color con el que se tiñe cada material, nunca la forma. Son 6 assets de
        // material separados (no 1 solo) para que cada uno pueda tener su propio _Color fijo en
        // el Inspector si hiciera falta ajustarlo a mano mas adelante.
        battleStage.slashSkillMaterial = GetOrCreateMaterial("Assets/Data/SlashSkillMaterial.mat", "Custom/SkillBurst");
        battleStage.strikeSkillMaterial = GetOrCreateMaterial("Assets/Data/StrikeSkillMaterial.mat", "Custom/SkillBurst");
        battleStage.pierceSkillMaterial = GetOrCreateMaterial("Assets/Data/PierceSkillMaterial.mat", "Custom/SkillBurst");
        battleStage.fireSkillMaterial = GetOrCreateMaterial("Assets/Data/FireSkillMaterial.mat", "Custom/SkillBurst");
        battleStage.iceSkillMaterial = GetOrCreateMaterial("Assets/Data/IceSkillMaterial.mat", "Custom/SkillBurst");
        battleStage.voltSkillMaterial = GetOrCreateMaterial("Assets/Data/VoltSkillMaterial.mat", "Custom/SkillBurst");
        // Shockwave por distorsion + spark anguloso, en TODO golpe basico y de habilidad (ver
        // BattleStageController.shockwaveMaterial/sparkMaterial).
        battleStage.shockwaveMaterial = GetOrCreateMaterial("Assets/Data/ShockwaveMaterial.mat", "Custom/ImpactShockwave");
        battleStage.sparkMaterial = GetOrCreateMaterial("Assets/Data/SparkMaterial.mat", "Custom/ImpactSpark");
        playerController.dialogueManager = dialogueManager;
        dialogueHud.dialogueManager = dialogueManager;
        enemyBarsHud.combatManager = combatManager;
        enemyBarsHud.battleStage = battleStage;
        playerController.pauseMenu = pauseMenu;
        // Mapa fisico que el personaje levanta cerca de camara (tecla M, ver PlayerMapViewer) --
        // colgado de la MISMA camara que cameraBobTarget de arriba, para que se mueva con el bob
        // de caminata como cualquier objeto sostenido en primera persona.
        var mapViewer = PlayerMapPropBuilder.Attach(cameraGo.transform, manager, playerController, pauseMenu);

        // Panel de edicion (grilla + herramientas Pared/Pintar piso/Simbolos, ver
        // PlayerMapEditorHUD): pantalla completa, aparece mientras mapViewer.IsOpen.
        var mapEditorGo = new GameObject("PlayerMapEditorHUD");
        var mapEditorHud = mapEditorGo.AddComponent<PlayerMapEditorHUD>();
        mapEditorHud.dungeonManager = manager;
        mapEditorHud.player = playerController;
        mapEditorHud.mapViewer = mapViewer;
        MapIconImporter.EnsureConfigured();
        foreach (var kv in MapIconImporter.LoadAll())
            mapEditorHud.icons.Add(new MapIconEntry { Id = kv.Key, Label = kv.Key, Sprite = kv.Value });
        hud.pauseMenu = pauseMenu;
        minimap.pauseMenu = pauseMenu;
        pauseMenuHud.pauseMenu = pauseMenu;
        pauseMenuHud.dungeonManager = manager;
        pauseMenuHud.combatManager = combatManager;
        pauseMenuHud.player = playerController;
        ambientParticles.dungeonManager = manager;
        ambientParticles.player = playerController;
        ambientParticles.followTarget = cameraGo.transform;
        mainMenuHud.mainMenu = mainMenuManager;
        mainMenuHud.dungeonManager = manager;

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

}
