using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Gameplay;

// Construye y guarda la escena de batalla (aparte de la mazmorra): una camara propia (apagada
// por defecto, la prende BattleStageController al entrar en combate) y una luz propia mirando 3
// "parantes" donde se instancian los enemigos.
public static class BattleSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/BattleScene.unity";
    public const string SceneName = "BattleScene";

    // Bien lejos de la mazmorra (que vive cerca de Y=0) para que nunca se puedan pisar/ver entre si.
    private static readonly Vector3 StageOrigin = new Vector3(0f, 200f, 0f);

    [MenuItem("Dungeon/Build Battle Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraGo = new GameObject("BattleCamera");
        var cam = cameraGo.AddComponent<Camera>();
        var listener = cameraGo.AddComponent<AudioListener>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.04f, 0.04f, 0.07f);
        cameraGo.transform.position = StageOrigin + new Vector3(0f, 1.2f, -6f);
        cameraGo.transform.rotation = Quaternion.identity;
        cam.enabled = false;
        listener.enabled = false;
        // Sin esto, el flash/sacudida de camara (CombatFeedback) solo existia en la camara de la
        // mazmorra -- que BattleStageController APAGA apenas arranca un combate -- asi que nunca
        // se veia durante una pelea real (solo la sacudida de una camara invisible). La cámara de
        // batalla necesita su propia instancia; BattleStageController hace el swap al entrar/salir.
        cameraGo.AddComponent<CombatFeedback>();

        // Luz propia de la escena de batalla (no depende de que la mazmorra este cargada ni de
        // como este rotada su luz): apunta de frente/arriba hacia los parantes para que la cara
        // que mira a la camara quede bien iluminada.
        var lightGo = new GameObject("BattleLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.position = StageOrigin;
        lightGo.transform.rotation = Quaternion.Euler(35f, 20f, 0f);

        var standPositions = new[]
        {
            StageOrigin + new Vector3(-2.6f, 1.2f, 0f),
            StageOrigin + new Vector3(0f, 1.2f, 0f),
            StageOrigin + new Vector3(2.6f, 1.2f, 0f),
        };
        for (int i = 0; i < standPositions.Length; i++)
        {
            var stand = new GameObject($"BattleStand_{i}");
            stand.transform.position = standPositions[i];
        }

        EditorSceneManager.SaveScene(scene, ScenePath);

        RegisterInBuildSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("BattleScene creada correctamente en " + ScenePath);
    }

    // SceneManager.LoadSceneAsync por nombre necesita que la escena figure en Build Settings
    // (tanto para Play Mode del editor como para un build real).
    private static void RegisterInBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        EnsureScene(scenes, "Assets/Scenes/DungeonTest.unity");
        EnsureScene(scenes, ScenePath);
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void EnsureScene(List<EditorBuildSettingsScene> scenes, string path)
    {
        if (!System.IO.File.Exists(path)) return;
        if (scenes.Any(s => s.path == path)) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
    }
}
