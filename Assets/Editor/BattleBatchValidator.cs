using System.IO;
using System.Linq;
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
    private const int TestMashPresses = 4;

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
                TestDialogue();
                TestAmbientParticles();
                TestElementalParticles();
                // Formacion y menu de pausa/progresion se prueban ANTES de arrancar el combate:
                // ahora se editan desde la exploracion (CanChangeFormation ya no depende de estar
                // en combate), no desde el menu de accion de la pelea.
                TestFormation();
                TestPauseMenuAndProgression();
                _combatManager.StartEncounter(false);
                TestGoBack();
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
                    Check("El material del efecto elemental (shader) quedo asignado", _battleStage.elementalBurstMaterial != null);

                    // El flash/sacudida de camara vivia SOLO en la camara de la mazmorra, que se
                    // apaga durante el combate -- nunca se veia en una pelea real. Confirma que
                    // CombatManager.feedback ahora apunta al CombatFeedback de la camara de
                    // batalla (la que esta realmente activa) mientras dura el combate.
                    var battleFeedback = battleCam != null ? battleCam.GetComponent<CombatFeedback>() : null;
                    Check("BattleCamera tiene su propio CombatFeedback", battleFeedback != null);
                    Check("CombatManager.feedback apunta al CombatFeedback de la camara de batalla (no a la de la mazmorra, apagada)",
                        battleFeedback != null && _combatManager.feedback == battleFeedback);

                    TestElementalEdgeGlow(battleFeedback);
                    TestBattleAmbientParticles();
                    TestPoiseBreakSetup();
                    SetPhase(10);
                }
                break;

            // Fuerza el aguante roto de TODOS los enemigos (ver TestPoiseBreakSetup) y espera a que
            // CombatManager ofrezca el Ataque en Conjunto (AllOutAttackReady) tras el primer turno
            // de la ronda, despues de que la party someta su accion.
            case 10:
                if (elapsed > PhaseTimeout)
                {
                    Check("Se rompio el aguante de todos los enemigos y aparecio el aviso de Ataque en Conjunto", false, $"paso {PhaseTimeout}s");
                    Finish();
                    return;
                }
                if (_combatManager.AllOutAttackReady)
                {
                    Check("AllOutAttackReady se activa cuando todos los enemigos quedan rotos a la vez", true);
                    // "Machacar" el boton varias veces: cada pulsacion deberia sumar al conteo (y
                    // despues, mas dano) -- no alcanza con apretarlo una sola vez.
                    for (int i = 0; i < TestMashPresses; i++)
                        _combatManager.TriggerAllOutAttack();
                    Check($"Machacar {TestMashPresses} veces suma {TestMashPresses} al conteo de mash",
                        _combatManager.AllOutAttackMashCount == TestMashPresses, $"real={_combatManager.AllOutAttackMashCount}");
                    SetPhase(11);
                }
                break;

            // Espera a que se resuelva el golpe en conjunto (y el resto de la ronda) y confirma que
            // los enemigos recibieron dano, dejaron de estar rotos y el log quedo con el mensaje.
            case 11:
                if (elapsed > PhaseTimeout)
                {
                    Check("El Ataque en Conjunto se resolvio a tiempo", false, $"paso {PhaseTimeout}s");
                    Finish();
                    return;
                }
                if (!_combatManager.AllOutAttackReady && !_combatManager.IsResolvingRound)
                {
                    Check("Tras el Ataque en Conjunto, AllOutAttackReady vuelve a false", !_combatManager.AllOutAttackReady);
                    Check("Tras el Ataque en Conjunto, ningun enemigo sigue con el aguante roto",
                        _combatManager.Enemies.Where(e => e.IsAlive).All(e => !e.IsBroken));
                    Check("Todos los enemigos recibieron dano del golpe en conjunto",
                        _combatManager.Enemies.All(e => e.HP < e.MaxHP), string.Join(",", _combatManager.Enemies.Select(e => $"{e.Name}={e.HP}/{e.MaxHP}")));
                    Check("El log de combate registra el Ataque en Conjunto",
                        _combatManager.Log.Any(l => l.Contains("Ataque en conjunto")));

                    // El dano real tiene que coincidir con la formula exacta para TestMashPresses
                    // pulsaciones (no solo "recibio algo de dano"): confirma que mashear de verdad
                    // escala el golpe, no que quedo pegado en el minimo de 1 pulsacion.
                    float mashMultiplier = 1f + Combat.CombatEngine.AllOutDamagePerExtraPress * (TestMashPresses - 1);
                    int expectedDmg = _combatManager.Party.Where(p => p.IsAlive)
                        .Sum(p => (int)System.Math.Round(p.Attack * Combat.CombatEngine.AllOutAttackMultiplier * mashMultiplier));
                    int actualDmg = _combatManager.Enemies[0].MaxHP - _combatManager.Enemies[0].HP;
                    Check("el dano del Ataque en Conjunto escala segun la formula exacta de mashCount",
                        actualDmg == expectedDmg, $"esperado={expectedDmg} real={actualDmg}");

                    var views2 = Object.FindObjectsOfType<EnemyView>();
                    Check("Las representaciones visuales de los enemigos siguen en pie tras el golpe en conjunto", views2.Length == _combatManager.Enemies.Count(e => e.IsAlive));

                    // Ahora si: dispara la disolucion real de un enemigo (mismo codigo que una
                    // muerte de verdad), el efecto de impacto de habilidad (sprite) y el efecto
                    // elemental (shader) -- para poder fotografiar los tres a mitad de camino.
                    var battleCamGo2 = GameObject.Find("BattleCamera");
                    var battleCam2 = battleCamGo2 != null ? battleCamGo2.GetComponent<Camera>() : null;
                    Quaternion camRot = battleCam2 != null ? battleCam2.transform.rotation : Quaternion.identity;
                    if (views2.Length > 0) views2[0].PlayDeathDissolve(null);
                    if (_battleStage.hitImpactFrames != null && _battleStage.hitImpactFrames.Length > 0 && views2.Length > 0)
                    {
                        var pos = views2[0].transform.position + (views2.Length > 1 ? Vector3.zero : new Vector3(1.6f, 0.3f, -0.5f));
                        HitImpactEffect.Spawn(_battleStage.hitImpactFrames, pos, new Color(1f, 0.6f, 0.3f), camRot);
                    }
                    if (_battleStage.elementalBurstMaterial != null && views2.Length > 1)
                    {
                        ElementalBurstEffect.Spawn(_battleStage.elementalBurstMaterial, views2[1].transform.position, new Color(1f, 0.35f, 0.12f), camRot);
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
                    Check("No queda BattleAmbientParticles colgado tras terminar el combate", Object.FindObjectOfType<BattleAmbientParticles>() == null);
                    Check("La niebla de la mazmorra se restaura al salir de combate", RenderSettings.fogEndDistance > 20f, $"fogEndDistance={RenderSettings.fogEndDistance}");
                    Check("CombatManager.feedback vuelve a la camara de la mazmorra al salir de combate",
                        _combatManager.feedback != null && _combatManager.feedback.GetComponent<Camera>() == Camera.main);

                    // Segunda pelea: probar "Huir" (forzado al 100% para que el resultado sea
                    // determinista) y confirmar que se limpia igual que un combate normal, pero
                    // sin contar como derrota (no abre la tienda/mejoras post-run).
                    _combatManager.fleeChancePerCharacter = 100f; // asi FleeChancePercent (por personaje vivo) queda en 100%
                    _combatManager.StartEncounter(false);
                    SetPhase(4);
                }
                break;

            case 4: // esperar a que la segunda pelea (para probar Huir) este activa y luego huir
                if (elapsed > PhaseTimeout)
                {
                    Check("La segunda pelea (test de Huir) arranco a tiempo", false, $"paso {PhaseTimeout}s");
                    Finish();
                    return;
                }
                if (_combatManager.IsActive)
                {
                    _combatManager.TryFlee();
                    SetPhase(5);
                }
                break;

            case 5: // confirmar que Huir (100% de chance) termina el combate sin contar como derrota
                if (elapsed > PhaseTimeout)
                {
                    Check("Huir termino el combate y limpio la escena de batalla a tiempo", false, $"paso {PhaseTimeout}s");
                    Finish();
                    return;
                }
                var battleScene3 = SceneManager.GetSceneByName(BattleSceneBuilder.SceneName);
                bool sceneGoneAfterFlee = !battleScene3.IsValid() || !battleScene3.isLoaded;
                if (!_combatManager.IsActive && sceneGoneAfterFlee)
                {
                    Check("Huir con 100% de chance termina el combate", true);
                    Check("Huir NO cuenta como derrota (no abre la tienda de fin de run)", !_dungeonManager.IsGameOverShopActive);
                    Check("La camara de la mazmorra se reactiva tras huir", _battleStage.dungeonCamera != null && _battleStage.dungeonCamera.enabled);
                    Finish();
                }
                break;
        }
    }

    // Prueba el sistema de dialogo de punta a punta: muestra una linea con 2 opciones, confirma
    // que IsActive/Choices se llenan bien, elige una opcion, y confirma que ejecuta su callback y
    // se cierra. Tambien confirma que GridPlayerController tiene la referencia cableada.
    private static void TestDialogue()
    {
        var dialogueManager = Object.FindObjectOfType<DialogueManager>();
        var player = Object.FindObjectOfType<GridPlayerController>();
        if (!Check("DialogueManager presente en Play Mode", dialogueManager != null)) return;

        bool chosenCallbackRan = false;
        dialogueManager.ShowChoices("Tabernero (test)", "¿Aceptas la mision?", new System.Collections.Generic.List<DialogueChoice>
        {
            new DialogueChoice("Aceptar", () => chosenCallbackRan = true),
            new DialogueChoice("Rechazar", () => { }),
        });

        Check("ShowChoices deja el dialogo activo", dialogueManager.IsActive);
        Check("ShowChoices carga las 2 opciones", dialogueManager.Choices != null && dialogueManager.Choices.Count == 2);

        dialogueManager.Choose(dialogueManager.Choices[0]);
        Check("Elegir una opcion ejecuta su callback", chosenCallbackRan);
        Check("Elegir una opcion cierra el dialogo", !dialogueManager.IsActive);

        Check("GridPlayerController tiene el DialogueManager cableado", player != null && player.dialogueManager == dialogueManager);
    }

    // Prueba el boton "Volver": elige una accion para el primer personaje, retrocede, y confirma
    // que la accion se deshizo y que el mismo personaje vuelve a poder elegir.
    private static void TestGoBack()
    {
        var first = _combatManager.GetChooser();
        if (!Check("Hay un primer personaje para elegir accion al arrancar el combate", first != null))
            return;

        Check("CanGoBack es false antes de elegir ninguna accion", !_combatManager.CanGoBack);
        _combatManager.SubmitAction(new Combat.PartyAction { Actor = first, Type = Combat.ActionType.Guard });

        var second = _combatManager.GetChooser();
        Check("Tras elegir, le toca al siguiente personaje", second != null && second != first);
        Check("CanGoBack es true despues de elegir una accion", _combatManager.CanGoBack);

        _combatManager.GoToPreviousChooser();
        var backTo = _combatManager.GetChooser();
        Check("Volver deja elegir de nuevo al mismo personaje de antes", backTo == first, $"esperado={first?.Name} real={backTo?.Name}");
        Check("Volver deshace la accion elegida (CanGoBack vuelve a false)", !_combatManager.CanGoBack);
    }

    // Prueba la formacion (3 al frente / 3 al fondo): mover a alguien del fondo al frente debe
    // mantener el balance 3/3 (bajando automaticamente a otro), y lo mismo al revertirlo.
    private static void TestFormation()
    {
        var party = _combatManager.Party;
        int frontBefore = party.Count(p => p.IsFrontRow);
        Check("La party arranca con exactamente 3 al frente y 3 al fondo", frontBefore == 3, $"frente={frontBefore}");

        var backMember = party.First(p => !p.IsFrontRow);
        _combatManager.SetFrontRow(backMember, true);
        Check("Mover a alguien del fondo al frente mantiene el balance 3 y 3", party.Count(p => p.IsFrontRow) == 3, $"frente={party.Count(p => p.IsFrontRow)}");
        Check("El personaje movido queda en el frente", backMember.IsFrontRow);

        _combatManager.SetFrontRow(backMember, false);
        Check("Devolverlo al fondo tambien mantiene el balance 3 y 3", party.Count(p => p.IsFrontRow) == 3, $"frente={party.Count(p => p.IsFrontRow)}");
        Check("El personaje vuelve a quedar en el fondo", !backMember.IsFrontRow);
    }

    // Confirma que la mazmorra tiene su sistema de particulas de ambiente activo apenas arranca
    // Play Mode (se configura solo, en su propio Awake/LateUpdate, sin que este test lo dispare).
    // No alcanza con confirmar que el componente existe (eso paso incluso con el bug real: el
    // sistema quedaba "reproduciendose" pero nunca emitia ninguna particula porque arrancaba a
    // jugar con Play On Awake ANTES de terminar de configurarse). Se fuerza un paso de simulacion
    // con Simulate() y se chequea particleCount > 0 para confirmar que de verdad emite algo.
    private static void TestAmbientParticles()
    {
        var ambient = Object.FindObjectOfType<AmbientParticles>();
        if (!Check("AmbientParticles presente en la escena de mazmorra", ambient != null)) return;
        var layers = ambient.GetComponentsInChildren<ParticleSystem>();
        if (!Check("AmbientParticles tiene sus 2 capas (cerca + lejos)", layers.Length == 2, $"encontradas={layers.Length}")) return;

        foreach (var ps in layers)
        {
            Check($"{ps.name}: esta reproduciendose", ps.isPlaying);
            ps.Simulate(2f, true, false);
            Check($"{ps.name}: realmente emite particulas tras un rato (no se queda en 0)", ps.particleCount > 0, $"particleCount={ps.particleCount}");
        }

        Check("la niebla de distancia esta activa en la mazmorra", RenderSettings.fog);
    }

    // Confirma que al entrar en combate aparecen las particulas de ambiente de la escena de
    // batalla (2 capas, igual criterio que en la mazmorra) y que la niebla de distancia cambia a
    // la version (mas corta y oscura) de combate.
    private static void TestBattleAmbientParticles()
    {
        var ambient = Object.FindObjectOfType<BattleAmbientParticles>();
        if (!Check("BattleAmbientParticles aparece al entrar en combate", ambient != null)) return;

        var layers = ambient.GetComponentsInChildren<ParticleSystem>();
        Check("BattleAmbientParticles tiene sus 2 capas (cerca + lejos)", layers.Length == 2, $"encontradas={layers.Length}");
        foreach (var ps in layers)
        {
            ps.Simulate(2f, true, false);
            Check($"{ps.name}: realmente emite particulas (no se queda en 0)", ps.particleCount > 0, $"particleCount={ps.particleCount}");
        }

        Check("la niebla cambia a la version de combate (mas corta que la de la mazmorra)",
            RenderSettings.fog && RenderSettings.fogEndDistance <= 20f, $"fogEndDistance={RenderSettings.fogEndDistance}");
    }

    // Confirma que ElementalParticleEffect.Spawn no solo crea los ParticleSystem, sino que la
    // rafaga principal Y la de "flare" configuradas para cada elemento realmente emiten
    // particulas (particleCount > 0) -- esto es lo que detecta el bug real de "Play On Awake"
    // antes de terminar de configurarse. Tambien confirma la luz de impacto real (Light, para el
    // golpe "mas llamativo" tipo Tekken 8).
    private static void TestElementalParticles()
    {
        foreach (Combat.Element element in System.Enum.GetValues(typeof(Combat.Element)))
        {
            var before = new System.Collections.Generic.HashSet<ParticleSystem>(Object.FindObjectsOfType<ParticleSystem>());
            ElementalParticleEffect.Spawn(Vector3.zero, element);

            var created = new System.Collections.Generic.List<ParticleSystem>();
            foreach (var ps in Object.FindObjectsOfType<ParticleSystem>())
            {
                if (!before.Contains(ps)) created.Add(ps);
            }
            if (!Check($"ElementalParticleEffect.Spawn crea las 2 capas (principal + flare) para {element}", created.Count == 2, $"encontradas={created.Count}"))
                continue;

            foreach (var ps in created)
            {
                ps.Simulate(0.02f, true, false);
                Check($"{element} ({ps.name}): realmente emite particulas (no se queda en 0)", ps.particleCount > 0, $"particleCount={ps.particleCount}");
            }

            var light = created[0].transform.root.GetComponentInChildren<Light>();
            Check($"{element}: tiene una luz de impacto real (Light)", light != null);
        }
    }

    // Confirma que CombatFeedback.OnElementalHit activa el brillo de borde (edge glow), la pieza
    // que pidio el usuario para "ver reflejado en el borde de la camara" el elemento del golpe.
    private static void TestElementalEdgeGlow(CombatFeedback battleFeedback)
    {
        if (!Check("Hay un CombatFeedback de batalla para probar el brillo de borde", battleFeedback != null)) return;
        Check("el brillo de borde arranca apagado", battleFeedback.CurrentEdgeIntensity <= 0f);
        battleFeedback.OnElementalHit(Combat.Element.Fire);
        Check("OnElementalHit activa el brillo de borde", battleFeedback.CurrentEdgeIntensity > 0f, $"intensity={battleFeedback.CurrentEdgeIntensity}");
    }

    // Prueba el menu de pausa (PauseMenuManager) y las piezas de progresion que vive muestran:
    // Codex (desbloquear lore) y Equipamiento (comprar + equipar con efecto INMEDIATO sobre la
    // party ya creada, no solo el guardado permanente).
    private static void TestPauseMenuAndProgression()
    {
        var pauseMenu = Object.FindObjectOfType<Gameplay.PauseMenuManager>();
        var player = Object.FindObjectOfType<GridPlayerController>();
        if (!Check("PauseMenuManager presente en Play Mode", pauseMenu != null)) return;
        Check("GridPlayerController tiene el PauseMenuManager cableado", player != null && player.pauseMenu == pauseMenu);

        Check("el menu de pausa arranca cerrado", !pauseMenu.IsOpen);
        pauseMenu.Open();
        Check("Open() lo abre", pauseMenu.IsOpen);
        pauseMenu.Close();
        Check("Close() lo cierra", !pauseMenu.IsOpen);

        var meta = _dungeonManager.Meta;
        string loreId = Lore.LoreCatalog.All[0].Id;
        Check("el lore de prueba arranca sin desbloquear", !meta.IsLoreUnlocked(loreId));
        meta.UnlockLore(loreId);
        Check("UnlockLore lo desbloquea en el Codex", meta.IsLoreUnlocked(loreId));

        meta.BankedPoints = 1000;
        string itemId = Combat.EquipmentCatalog.All[0].Id;
        bool bought = meta.TryPurchaseItem(itemId);
        Check("comprar un accesorio con puntos suficientes funciona", bought && meta.OwnsItem(itemId));

        var warrior = _combatManager.Party.Find(p => p.Class == Combat.CharacterClass.Warrior);
        int atkBefore = warrior.Attack;
        _combatManager.SetEquippedItemLive(meta, Combat.CharacterClass.Warrior, itemId);
        int expectedBonus = Combat.EquipmentCatalog.Find(itemId).AttackBonus;
        Check("equipar en vivo suma el bonus del item a la party YA creada (no hace falta empezar otra run)",
            warrior.Attack == atkBefore + expectedBonus, $"antes={atkBefore} despues={warrior.Attack} bonus={expectedBonus}");

        _combatManager.SetEquippedItemLive(meta, Combat.CharacterClass.Warrior, "");
        Check("desequipar en vivo devuelve el ATK al valor original", warrior.Attack == atkBefore, $"real={warrior.Attack}");
    }

    // Fuerza el aguante roto de TODOS los enemigos activos (en vez de tener que grindear golpes
    // reales durante el test) y somete Guardia para toda la party viva, lo que dispara la
    // resolucion de ronda -- ahi CombatManager deberia detectar AllEnemiesBroken() y ofrecer el
    // Ataque en Conjunto (fase 10). Se sube el HP y se baja la Velocidad de los enemigos a 0 para
    // que ninguno actue antes que la party (no se recuperarian solos) ni muera con el golpe de
    // prueba, dejando limpio el resto del test de disolucion/impacto que sigue despues.
    private static void TestPoiseBreakSetup()
    {
        foreach (var e in _combatManager.Enemies)
        {
            e.MaxHP = 9999;
            e.HP = 9999;
            e.Speed = 0;
            e.MaxPoise = 10;
            e.Poise = 0;
            e.IsBroken = true;
        }
        foreach (var p in _combatManager.Party.Where(p => p.IsAlive))
            _combatManager.SubmitAction(new Combat.PartyAction { Actor = p, Type = Combat.ActionType.Guard });
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
