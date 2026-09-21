using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Combat;

namespace Gameplay
{
    // Carga la escena de batalla (BattleScene, aparte y additive) cuando arranca un combate y la
    // descarga cuando termina; mientras tanto apaga la camara/audio listener de la mazmorra y
    // prende los de la escena de batalla, y crea/destruye la representacion visual de cada
    // enemigo (EnemyView), sincronizada con CombatManager.Enemies por indice.
    public class BattleStageController : MonoBehaviour
    {
        public CombatManager combatManager;
        public Camera dungeonCamera;
        public AudioListener dungeonAudioListener;
        public Material dissolveMaterial;
        public string battleSceneName = "BattleScene";

        [Header("Efecto de impacto de habilidad (sprite)")]
        [Tooltip("Las 6 frames de HitImpact.png, en orden de animacion (las asigna DungeonSceneBuilder via HitEffectImporter.LoadFrames()).")]
        public Sprite[] hitImpactFrames;

        [Header("Efecto de impacto elemental (shader simple, en TODOS los golpes -- basicos y habilidades)")]
        public Material elementalBurstMaterial;

        [Header("Hit spark estilo Tekken 8 (flash + puntas radiales, en TODOS los golpes)")]
        public Material impactBurstMaterial;

        [Header("Shaders de habilidad por elemento (mas elaborados: SOLO en golpes de HABILIDAD)")]
        [Tooltip("Cada habilidad tiene su propio shader de ataque segun su elemento (no por personaje). Los ataques basicos solo usan el shader simple de arriba.")]
        public Material slashSkillMaterial;
        public Material strikeSkillMaterial;
        public Material pierceSkillMaterial;
        public Material fireSkillMaterial;
        public Material iceSkillMaterial;
        public Material voltSkillMaterial;

        private Camera _battleCamera;
        private AudioListener _battleAudioListener;
        private readonly List<Transform> _stands = new List<Transform>();
        private readonly List<EnemyView> _activeViews = new List<EnemyView>();
        // Indice de parante ocupado por cada entrada de _activeViews (misma posicion = mismo enemigo).
        private readonly List<int> _viewStandIndex = new List<int>();

        private BattleAmbientParticles _ambientParticles;

        // True cuando el combate termino (CleanupAfterCombat se llamo) mientras la carga
        // additive de la escena de batalla TODAVIA estaba en progreso: sin esto, la escena se
        // queda huerfana para siempre (cargada, sin nadie que la descargue nunca), porque
        // SceneManager.GetSceneByName la devuelve con isLoaded=false en ese momento y el chequeo
        // de limpieza la salteaba en silencio. Puede pasar en un combate real si el jugador huye
        // (o hace algo que termine el combate) muy rapido, justo al arrancar la pelea.
        private bool _cleanupPendingSceneLoad;

        // Niebla de distancia: se guarda la de la mazmorra la primera vez (ya viene cargada del
        // scene file) y se restaura al salir de combate, para que la niebla de la batalla no se
        // quede pegada en la mazmorra.
        private bool _fogSnapshotTaken;
        private bool _dungeonFogEnabled;
        private FogMode _dungeonFogMode;
        private Color _dungeonFogColor;
        private float _dungeonFogStart, _dungeonFogEnd;

        // Camara activa de la escena de batalla; la usa EnemyHealthBarHUD para proyectar la
        // posicion de cada EnemyView a coordenadas de pantalla (OnGUI) y dibujar sus barras ahi.
        public Camera ActiveBattleCamera => _battleCamera;

        public EnemyView GetEnemyView(int index) => (index >= 0 && index < _activeViews.Count) ? _activeViews[index] : null;

        private static readonly Color AllOutBurstColor = new Color(1f, 0.9f, 0.35f);

        // El flash/sacudida de camara (CombatFeedback) tiene que vivir en la camara que este
        // REALMENTE activa en cada momento: la de la mazmorra se apaga durante el combate, asi que
        // sin este swap el feedback quedaba disparandose sobre una camara invisible y nunca se veia.
        private CombatFeedback _dungeonFeedback;

        void Awake()
        {
            CaptureDungeonFogIfNeeded();
            if (combatManager == null) return;
            _dungeonFeedback = combatManager.feedback;
            combatManager.OnCombatStarted += HandleCombatStarted;
            combatManager.OnCombatFinished += HandleCombatFinished;
            combatManager.OnCombatFled += HandleCombatFled;
            combatManager.OnEnemyDamaged += HandleEnemyDamaged;
            combatManager.OnEnemyDefeated += HandleEnemyDefeated;
            combatManager.OnEnemyAdded += HandleEnemyAdded;
            combatManager.OnEnemySkillHit += HandleEnemySkillHit;
            combatManager.OnEnemyElementalHit += HandleEnemyElementalHit;
            combatManager.OnEnemyPoiseBroken += HandleEnemyPoiseBroken;
            combatManager.OnAllOutAttackUsed += HandleAllOutAttackUsed;
        }

        private void HandleCombatStarted()
        {
            var op = SceneManager.LoadSceneAsync(battleSceneName, LoadSceneMode.Additive);
            if (op != null) op.completed += _ => OnBattleSceneLoaded();
        }

        private void OnBattleSceneLoaded()
        {
            var battleScene = SceneManager.GetSceneByName(battleSceneName);
            _stands.Clear();
            _battleCamera = null;
            _battleAudioListener = null;

            foreach (var root in battleScene.GetRootGameObjects())
            {
                if (root.name == "BattleCamera")
                {
                    _battleCamera = root.GetComponent<Camera>();
                    _battleAudioListener = root.GetComponent<AudioListener>();
                }
                else if (root.name.StartsWith("BattleStand_"))
                {
                    _stands.Add(root.transform);
                }
            }
            _stands.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            if (dungeonCamera != null) dungeonCamera.enabled = false;
            if (dungeonAudioListener != null) dungeonAudioListener.enabled = false;
            if (_battleCamera != null) _battleCamera.enabled = true;
            if (_battleAudioListener != null) _battleAudioListener.enabled = true;

            if (combatManager != null && _battleCamera != null)
            {
                var battleFeedback = _battleCamera.GetComponent<CombatFeedback>();
                if (battleFeedback != null) combatManager.feedback = battleFeedback;
            }

            SpawnEnemyViews();

            bool isBoss = combatManager != null && combatManager.IsBossFight;
            ApplyBattleFog(isBoss);
            if (_battleCamera != null)
                _ambientParticles = BattleAmbientParticles.Spawn(_battleCamera.transform, isBoss);

            // El combate termino MUY rapido (p.ej. Huir justo al arrancar la pelea, o
            // SkipFightForTesting) y CleanupAfterCombat se llamo mientras la escena todavia
            // estaba cargando: en ese momento no habia nada que descargar todavia (isLoaded era
            // false), asi que se pospuso hasta ahora. Recien esta lista, hacerla de una.
            if (_cleanupPendingSceneLoad)
            {
                _cleanupPendingSceneLoad = false;
                CleanupAfterCombat();
            }
        }

        // ---------- Niebla de distancia ----------

        private void CaptureDungeonFogIfNeeded()
        {
            if (_fogSnapshotTaken) return;
            _dungeonFogEnabled = RenderSettings.fog;
            _dungeonFogMode = RenderSettings.fogMode;
            _dungeonFogColor = RenderSettings.fogColor;
            _dungeonFogStart = RenderSettings.fogStartDistance;
            _dungeonFogEnd = RenderSettings.fogEndDistance;
            _fogSnapshotTaken = true;
        }

        // Niebla mas corta y oscura que la de la mazmorra -- el fondo de la arena se pierde en la
        // penumbra detras de los enemigos (mas marcada todavia en pelea de jefe), como el fondo
        // atmosferico de un escenario de pelea.
        private void ApplyBattleFog(bool boss)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = boss ? new Color(0.05f, 0.02f, 0.02f) : new Color(0.03f, 0.03f, 0.05f);
            RenderSettings.fogStartDistance = 6f;
            RenderSettings.fogEndDistance = boss ? 20f : 16f;
        }

        private void RestoreDungeonFog()
        {
            RenderSettings.fog = _dungeonFogEnabled;
            RenderSettings.fogMode = _dungeonFogMode;
            RenderSettings.fogColor = _dungeonFogColor;
            RenderSettings.fogStartDistance = _dungeonFogStart;
            RenderSettings.fogEndDistance = _dungeonFogEnd;
        }

        private void SpawnEnemyViews()
        {
            ClearViews();
            if (combatManager == null || combatManager.Enemies == null) return;

            var order = StandOrderForCount(combatManager.Enemies.Count);
            for (int i = 0; i < combatManager.Enemies.Count && i < order.Length; i++)
            {
                _activeViews.Add(CreateEnemyVisual(combatManager.Enemies[i], _stands[order[i]].position));
                _viewStandIndex.Add(order[i]);
            }
        }

        // Un enemigo nuevo aparecio a mitad de combate (p.ej. las 2 crias de un Slime que se
        // dividio al morir): le busca un parante libre (uno que ningun otro enemigo activo este
        // usando ya) y le crea su representacion visual ahi.
        private void HandleEnemyAdded(int index)
        {
            if (combatManager == null || combatManager.Enemies == null) return;
            if (index < 0 || index >= combatManager.Enemies.Count) return;

            int standIdx = FirstFreeStandIndex();
            if (standIdx < 0) return; // no deberia pasar: CombatEngine ya respeta el tope de parantes

            while (_activeViews.Count <= index)
            {
                _activeViews.Add(null);
                _viewStandIndex.Add(-1);
            }

            _activeViews[index] = CreateEnemyVisual(combatManager.Enemies[index], _stands[standIdx].position);
            _viewStandIndex[index] = standIdx;
        }

        private int FirstFreeStandIndex()
        {
            for (int s = 0; s < _stands.Count; s++)
                if (!_viewStandIndex.Contains(s)) return s;
            return -1;
        }

        // Con menos de 3 enemigos, se centran en vez de arrancar siempre por el parante de la
        // izquierda (1 enemigo -> parante del medio; 2 -> izquierda y derecha).
        private static int[] StandOrderForCount(int count)
        {
            switch (count)
            {
                case 1: return new[] { 1 };
                case 2: return new[] { 0, 2 };
                default: return new[] { 0, 1, 2 };
            }
        }

        // Como todavia no hay arte importado, cada especie de enemigo se representa con una
        // figura primitiva y un color distintos (placeholder), con el shader de disolucion.
        private EnemyView CreateEnemyVisual(EnemyStats stats, Vector3 position)
        {
            PrimitiveType shape;
            Vector3 scale;
            Color baseColor;

            if (stats.Name.Contains("Lobo"))
            {
                shape = PrimitiveType.Capsule;
                scale = new Vector3(1.4f, 1.6f, 1.4f);
                baseColor = new Color(0.68f, 0.6f, 0.5f);
            }
            else if (stats.Name.Contains("Escarabajo"))
            {
                shape = PrimitiveType.Cube;
                scale = new Vector3(1.8f, 1.1f, 2.0f);
                baseColor = new Color(0.35f, 0.5f, 0.3f);
            }
            else if (stats.Name.Contains("Guardian"))
            {
                shape = PrimitiveType.Cylinder;
                scale = new Vector3(2.6f, 3.0f, 2.6f);
                baseColor = new Color(0.62f, 0.62f, 0.68f);
            }
            else if (stats.Name.Contains("Slime"))
            {
                bool isCria = stats.Name.Contains("Cria");
                shape = PrimitiveType.Sphere;
                scale = isCria ? new Vector3(0.9f, 0.7f, 0.9f) : new Vector3(1.7f, 1.3f, 1.7f);
                baseColor = isCria ? new Color(0.5f, 0.8f, 0.55f) : new Color(0.3f, 0.68f, 0.4f);
            }
            else
            {
                shape = PrimitiveType.Sphere;
                scale = Vector3.one;
                baseColor = Color.gray;
            }

            var go = GameObject.CreatePrimitive(shape);
            go.name = $"EnemyView_{stats.Name}";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.position = position;
            go.transform.localScale = scale;

            var renderer = go.GetComponent<Renderer>();
            if (dissolveMaterial != null) renderer.sharedMaterial = dissolveMaterial;

            var view = go.AddComponent<EnemyView>();
            view.Initialize(stats, renderer, baseColor);
            return view;
        }

        private void HandleEnemyDamaged(int index)
        {
            if (index >= 0 && index < _activeViews.Count) _activeViews[index]?.PlayHitPulse();
        }

        // Adelanta el punto de aparicion de un efecto hacia la camara, hasta mas o menos la
        // superficie del modelo (en vez de su centro): un efecto en el centro exacto queda medio
        // tapado por la propia mitad cercana del modelo del enemigo, y se ve como si estuviera
        // "atras" en vez de "al frente" -- justo lo que pidio el usuario que se corrigiera.
        private Vector3 EffectAnchor(EnemyView view)
        {
            if (view == null) return Vector3.zero;
            Vector3 pos = view.transform.position;
            if (_battleCamera == null) return pos;

            Vector3 toCamera = (_battleCamera.transform.position - pos).normalized;
            Vector3 ext = view.Extents;
            // Distancia (a lo largo de toCamera) hasta el borde de la caja que envuelve al modelo:
            // trata al modelo como una caja alineada a los ejes, que para las formas primitivas
            // actuales (esfera/capsula/cubo) es una aproximacion mas que suficiente.
            float radius = Mathf.Abs(toCamera.x) * ext.x + Mathf.Abs(toCamera.y) * ext.y + Mathf.Abs(toCamera.z) * ext.z;
            return pos + toCamera * radius;
        }

        // Golpe de habilidad: ademas del pulso de disolucion, aparece el efecto de impacto (sprite
        // HitImpact.png) sobre el enemigo, teñido segun el elemento para que se note la diferencia
        // entre habilidades, Y el shader propio de esa habilidad (SkillMaterialFor) -- cada
        // habilidad tiene su propio shader de ataque segun su elemento (no por personaje: dos
        // habilidades del mismo elemento comparten shader). Los ataques basicos NUNCA llegan aca;
        // solo usan el shader simple de HandleEnemyElementalHit. "intensity" (SkillPower de quien
        // la uso) agranda el efecto para las habilidades mas fuertes.
        private void HandleEnemySkillHit(int index, Element element, float intensity)
        {
            if (index < 0 || index >= _activeViews.Count) return;
            var view = _activeViews[index];
            if (view == null) return;

            Quaternion facing = _battleCamera != null ? _battleCamera.transform.rotation : Quaternion.identity;
            Vector3 anchor = EffectAnchor(view);

            if (hitImpactFrames != null && hitImpactFrames.Length > 0)
            {
                Color tint = Color.Lerp(Color.white, ElementVisuals.ColorFor(element), 0.55f);
                HitImpactEffect.Spawn(hitImpactFrames, anchor, tint, facing);
            }

            var skillMaterial = SkillMaterialFor(element);
            if (skillMaterial != null)
                ElementalBurstEffect.Spawn(skillMaterial, anchor, ElementVisuals.ColorFor(element), facing, intensity);
        }

        private Material SkillMaterialFor(Element element)
        {
            switch (element)
            {
                case Element.Slash: return slashSkillMaterial;
                case Element.Strike: return strikeSkillMaterial;
                case Element.Pierce: return pierceSkillMaterial;
                case Element.Fire: return fireSkillMaterial;
                case Element.Ice: return iceSkillMaterial;
                case Element.Volt: return voltSkillMaterial;
                default: return null;
            }
        }

        // Efecto de shader + rafaga de particulas reales (sin sprite) que se ven en CUALQUIER
        // golpe -- basico o de habilidad -- para que el elemento del ataque siempre tenga algun
        // feedback visual, no solo las habilidades (que ademas tienen el sprite de HitImpactEffect).
        // "intensity" (SkillPower de la habilidad, o 1 en un ataque basico) agranda y hace mas
        // intensas las chispas cuanto mas fuerte es el golpe.
        private void HandleEnemyElementalHit(int index, Element element, float intensity)
        {
            if (index < 0 || index >= _activeViews.Count) return;
            var view = _activeViews[index];
            if (view == null) return;

            Quaternion elementalFacing = _battleCamera != null ? _battleCamera.transform.rotation : Quaternion.identity;
            Vector3 anchor = EffectAnchor(view);
            if (elementalBurstMaterial != null)
                ElementalBurstEffect.Spawn(elementalBurstMaterial, anchor, ElementVisuals.ColorFor(element), elementalFacing, intensity);

            // Hit spark estilo Tekken 8 (flash + puntas radiales): se dispara SIEMPRE, con el mismo
            // color que el anillo elemental de arriba (naranjo "golpe" en ataques basicos, color
            // propio en habilidades), para que cualquier golpe se sienta con mas peso/impacto.
            if (impactBurstMaterial != null)
                ImpactBurstEffect.Spawn(impactBurstMaterial, anchor, ElementVisuals.ColorFor(element), elementalFacing, intensity);

            ElementalParticleEffect.Spawn(anchor, element);
        }

        // Se le rompio el aguante a este enemigo: pulso mas fuerte con el borde en amarillo
        // brillante (en vez del pulso naranja normal de un golpe comun), para que se note.
        private void HandleEnemyPoiseBroken(int index)
        {
            if (index < 0 || index >= _activeViews.Count) return;
            _activeViews[index]?.PlayBreakFlash();
        }

        // El jugador uso el Ataque en Conjunto: golpe dorado (VFX + pulso) en TODOS los enemigos
        // activos a la vez, para que se sienta como un golpe de equipo y no un golpe mas.
        private void HandleAllOutAttackUsed()
        {
            Quaternion facing = _battleCamera != null ? _battleCamera.transform.rotation : Quaternion.identity;
            for (int i = 0; i < _activeViews.Count; i++)
            {
                var view = _activeViews[i];
                if (view == null) continue;

                // Si el golpe en conjunto acaba de matar a este enemigo, OnEnemyDefeated ya disparo
                // PlayDeathDissolve unas lineas antes en el mismo frame (la view todavia no es null:
                // eso recien pasa cuando esa disolucion termina). Pulsarlo aca tambien pisaria esa
                // corrutina con un pulso corto que nunca llama al callback de limpieza -- el
                // enemigo se quedaba visible para siempre en vez de desaparecer. Los enemigos
                // muertos ya tienen su propio efecto de muerte; no hace falta este pulso extra.
                bool isDead = combatManager != null && combatManager.Enemies != null
                    && i < combatManager.Enemies.Count && !combatManager.Enemies[i].IsAlive;
                if (isDead) continue;

                view.PlayHitPulse();
                Vector3 anchor = EffectAnchor(view);
                // El golpe mas grande del juego (toda la party a la vez): intensidad fija bien alta,
                // mas grande y llamativo que cualquier habilidad individual.
                const float allOutIntensity = 2.4f;
                if (elementalBurstMaterial != null)
                    ElementalBurstEffect.Spawn(elementalBurstMaterial, anchor, AllOutBurstColor, facing, allOutIntensity);
                if (impactBurstMaterial != null)
                    ImpactBurstEffect.Spawn(impactBurstMaterial, anchor, AllOutBurstColor, facing, allOutIntensity);
            }
        }


        // Los primeros FrontStandCount parantes (0,1,2) son la fila de adelante, mas cerca de la
        // camara; el resto (3,4,5) es la fila de atras, de reserva para cuando un Slime se divide
        // con el frente ya lleno (ver BattleSceneBuilder). Coincide con el orden de
        // StandOrderForCount y con como se nombraron/ordenaron los BattleStand_N.
        private const int FrontStandCount = 3;

        private void HandleEnemyDefeated(int index)
        {
            if (index < 0 || index >= _activeViews.Count) return;
            var view = _activeViews[index];
            if (view == null) return;
            view.PlayDeathDissolve(() =>
            {
                if (view != null) Destroy(view.gameObject);
                // Libera el parante que ocupaba este enemigo: sin esto, FirstFreeStandIndex()
                // seguia contandolo como "ocupado" para siempre despues de morir, y una cria de
                // Slime que necesitaba ese lugar (HandleEnemyAdded) se quedaba sin representacion
                // visual aunque el motor de combate SI la haya agregado a Enemies.
                if (index < _viewStandIndex.Count) _viewStandIndex[index] = -1;
                if (index < _activeViews.Count) _activeViews[index] = null;
                PromoteBackRowEnemies();
            });
        }

        // Si un enemigo del frente acaba de morir y dejo un parante libre, el primero que siga
        // vivo en la fila de atras avanza a ese lugar -- sin esto, una vez que el frente se vacia
        // los enemigos restantes se quedan chicos y lejos en el fondo en vez de acercarse como si
        // fueran los enemigos iniciales (justo lo que pidio el usuario).
        private void PromoteBackRowEnemies()
        {
            if (_stands.Count < FrontStandCount) return;

            for (int frontStand = 0; frontStand < FrontStandCount; frontStand++)
            {
                if (_viewStandIndex.Contains(frontStand)) continue; // ya ocupado

                int backViewIndex = -1;
                for (int i = 0; i < _viewStandIndex.Count; i++)
                {
                    if (_viewStandIndex[i] < FrontStandCount) continue; // -1 (libre) o ya en el frente
                    if (_activeViews[i] == null) continue;
                    backViewIndex = i;
                    break;
                }
                if (backViewIndex < 0) break; // no queda nadie atras para promover

                _viewStandIndex[backViewIndex] = frontStand;
                _activeViews[backViewIndex].MoveTo(_stands[frontStand].position);
            }
        }

        private void HandleCombatFinished(bool victory, bool wasBoss) => CleanupAfterCombat();

        // Escapar es otra forma de terminar el combate (ni victoria ni derrota): limpia la escena
        // de batalla igual que un fin de combate normal.
        private void HandleCombatFled() => CleanupAfterCombat();

        private void CleanupAfterCombat()
        {
            var battleSceneCheck = SceneManager.GetSceneByName(battleSceneName);
            if (battleSceneCheck.IsValid() && !battleSceneCheck.isLoaded)
            {
                // El combate termino mientras la carga additive todavia estaba en progreso (ver
                // _cleanupPendingSceneLoad): nada de esto (camara, niebla, parantes) existe todavia
                // de verdad, asi que no hay nada que limpiar ni descargar TODAVIA. Se reintenta
                // completa desde OnBattleSceneLoaded en cuanto la carga termine.
                _cleanupPendingSceneLoad = true;
                return;
            }

            ClearViews();

            if (_ambientParticles != null)
            {
                Destroy(_ambientParticles.gameObject);
                _ambientParticles = null;
            }
            RestoreDungeonFog();
            if (combatManager != null && _dungeonFeedback != null) combatManager.feedback = _dungeonFeedback;

            if (dungeonCamera != null) dungeonCamera.enabled = true;
            if (dungeonAudioListener != null) dungeonAudioListener.enabled = true;

            if (battleSceneCheck.IsValid() && battleSceneCheck.isLoaded)
                SceneManager.UnloadSceneAsync(battleSceneCheck);
        }

        private void ClearViews()
        {
            foreach (var view in _activeViews)
                if (view != null) Destroy(view.gameObject);
            _activeViews.Clear();
            _viewStandIndex.Clear();
        }
    }
}
