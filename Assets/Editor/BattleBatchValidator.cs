using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Gameplay;

// Prueba de integracion en Play Mode (batch): entra en Play, arranca un combate de verdad,
// confirma que la escena de batalla se carga de forma additive y que aparece una representacion
// visual por cada enemigo, toma una captura de pantalla real (con un enemigo a mitad de disolver
// para mostrar el efecto), termina el combate y confirma que todo se limpia. No reemplaza el
// playtesting a mano (la sensacion real solo la puede juzgar el usuario), pero detecta bugs de
// cableado (escena no registrada, referencias nulas, camara/audio listener mal togglados) que la
// sola compilacion no puede ver.
public static class BattleBatchValidator
{
    private const string ScreenshotPath = "Assets/../battle_scene_screenshot.png";
    private const double PhaseTimeout = 8.0;

    private static int _phase;
    private static double _phaseStart;
    private static bool _allOk = true;
    private static DungeonManager _dungeonManager;
    private static CombatManager _combatManager;
    private static BattleStageController _battleStage;
    private static bool _prevEnterPlayModeOptionsEnabled;
    private static EnterPlayModeOptions _prevEnterPlayModeOptions;
    private static double _globalStart;
    private const double GlobalTimeout = 40.0;

    [MenuItem("Dungeon/Validate Battle Scene (Play Mode Batch)")]
    public static void ValidateFromBatch()
    {
        _phase = 0;
        _allOk = true;

        // Al entrar en Play Mode, Unity por defecto hace un "domain reload" que borra todo el
        // estado estatico (incluida esta suscripcion a EditorApplication.update) apenas se llama
        // a isPlaying=true. Se desactiva el reload de dominio solo para esta corrida (y se
        // restaura al terminar) para que el validador pueda seguir el hilo entre frames.
        _prevEnterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        _prevEnterPlayModeOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

        EditorSceneManager.OpenScene("Assets/Scenes/DungeonTest.unity", OpenSceneMode.Single);
        _globalStart = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static bool Check(string name, bool ok, string detail = "")
    {
        if (ok) Debug.Log($"[BATTLE-VALIDATE] OK - {name}");
        else { Debug.LogError($"[BATTLE-VALIDATE] FALLO - {name} {detail}"); _allOk = false; }
        return ok;
    }

    private static void SetPhase(int phase)
    {
        _phase = phase;
        _phaseStart = EditorApplication.timeSinceStartup;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;

        if (EditorApplication.timeSinceStartup - _globalStart > GlobalTimeout)
        {
            Check("El test completo termino antes del timeout global", false, $"paso {GlobalTimeout}s, fase={_phase}");
            Finish();
            return;
        }

        double elapsed = EditorApplication.timeSinceStartup - _phaseStart;

        switch (_phase)
        {
            case 0:
                _dungeonManager = Object.FindObjectOfType<DungeonManager>();
                _combatManager = Object.FindObjectOfType<CombatManager>();
                _battleStage = Object.FindObjectOfType<BattleStageController>();
                if (!Check("DungeonManager/CombatManager/BattleStageController presentes en Play Mode",
                        _dungeonManager != null && _combatManager != null && _battleStage != null))
                {
                    Finish();
                    return;
                }
                _combatManager.StartEncounter(false);
                SetPhase(1);
                break;

            case 1: // esperar a que la carga additive de BattleScene termine y se instancien los enemigos
                if (elapsed > PhaseTimeout)
                {
                    Check("BattleScene se cargo y los enemigos aparecieron a tiempo", false, $"paso {PhaseTimeout}s");
                    Finish();
                    return;
                }
                var battleScene = SceneManager.GetSceneByName(BattleSceneBuilder.SceneName);
                var views = Object.FindObjectsOfType<EnemyView>();
                if (battleScene.IsValid() && battleScene.isLoaded && views.Length == _combatManager.Enemies.Count && views.Length > 0)
                {
                    Check("BattleScene cargada de forma additive al entrar en combate", true);
                    Check("Se instancio una representacion visual por cada enemigo", true, $"count={views.Length}");
                    var battleCamGo = GameObject.Find("BattleCamera");
                    var battleCam = battleCamGo != null ? battleCamGo.GetComponent<Camera>() : null;
                    Check("La camara de batalla quedo activa", battleCam != null && battleCam.enabled);
                    Check("La camara de la mazmorra quedo apagada durante el combate", _battleStage.dungeonCamera != null && !_battleStage.dungeonCamera.enabled);
                    Check("Los 6 frames del efecto de impacto quedaron asignados", _battleStage.hitImpactFrames != null && _battleStage.hitImpactFrames.Length == 6 && _battleStage.hitImpactFrames[0] != null);

                    // Dispara la disolucion real de un enemigo (mismo codigo que usa una muerte de
                    // verdad) y el efecto de impacto de habilidad (mismo codigo que un golpe real
                    // de habilidad) para poder fotografiar ambos a mitad de camino.
                    views[0].PlayDeathDissolve(null);
                    if (_battleStage.hitImpactFrames != null && _battleStage.hitImpactFrames.Length > 0)
                    {
                        var pos = views[0].transform.position + (views.Length > 1 ? Vector3.zero : new Vector3(1.6f, 0.3f, -0.5f));
                        HitImpactEffect.Spawn(_battleStage.hitImpactFrames, pos, new Color(1f, 0.6f, 0.3f), battleCam != null ? battleCam.transform.rotation : Quaternion.identity);
                    }
                    SetPhase(2);
                }
                break;

            case 2: // dejar que la disolucion avance un poco (pero no demasiado) antes de la foto
                if (elapsed > 0.15)
                {
                    TakeScreenshot();
                    _combatManager.SkipFightForTesting();
                    SetPhase(3);
                }
                break;

            case 3: // esperar a que el combate termine y la escena de batalla se descargue
                if (elapsed > PhaseTimeout)
                {
                    Check("El combate termino y BattleScene se descargo a tiempo", false, $"paso {PhaseTimeout}s");
                    Finish();
                    return;
                }
                var battleScene2 = SceneManager.GetSceneByName(BattleSceneBuilder.SceneName);
                bool sceneGone = !battleScene2.IsValid() || !battleScene2.isLoaded;
                if (!_combatManager.IsActive && sceneGone)
                {
                    Check("Al terminar el combate, BattleScene se descarga", true);
                    Check("La camara de la mazmorra se reactiva al terminar el combate", _battleStage.dungeonCamera != null && _battleStage.dungeonCamera.enabled);
                    var remaining = Object.FindObjectsOfType<EnemyView>();
                    Check("No quedan EnemyView colgados tras terminar el combate", remaining.Length == 0, $"quedaron={remaining.Length}");
                    Finish();
                }
                break;
        }
    }

    private static void TakeScreenshot()
    {
        var camGo = GameObject.Find("BattleCamera");
        var cam = camGo != null ? camGo.GetComponent<Camera>() : null;
        if (cam == null)
        {
            Check("Captura de pantalla de la escena de batalla", false, "no se encontro BattleCamera");
            return;
        }

        const int w = 1024, h = 768;
        var rt = new RenderTexture(w, h, 24);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        cam.targetTexture = prevTarget;
        RenderTexture.active = prevActive;

        string fullPath = Path.GetFullPath(ScreenshotPath);
        File.WriteAllBytes(fullPath, tex.EncodeToPNG());
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Check("Captura de pantalla de la escena de batalla guardada", true, fullPath);
    }

    private static void Finish()
    {
        EditorApplication.update -= Tick;
        if (_allOk)
            Debug.Log("[BATTLE-VALIDATE] RESULTADO FINAL: TODO OK");
        else
            Debug.LogError("[BATTLE-VALIDATE] RESULTADO FINAL: HAY FALLOS");

        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.isPlaying = false;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorSettings.enterPlayModeOptionsEnabled = _prevEnterPlayModeOptionsEnabled;
        EditorSettings.enterPlayModeOptions = _prevEnterPlayModeOptions;
        EditorApplication.Exit(_allOk ? 0 : 1);
    }
}
