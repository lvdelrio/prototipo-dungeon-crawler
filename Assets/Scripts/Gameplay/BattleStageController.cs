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

        private Camera _battleCamera;
        private AudioListener _battleAudioListener;
        private readonly List<Transform> _stands = new List<Transform>();
        private readonly List<EnemyView> _activeViews = new List<EnemyView>();
        // Indice de parante ocupado por cada entrada de _activeViews (misma posicion = mismo enemigo).
        private readonly List<int> _viewStandIndex = new List<int>();

        void Awake()
        {
            if (combatManager == null) return;
            combatManager.OnCombatStarted += HandleCombatStarted;
            combatManager.OnCombatFinished += HandleCombatFinished;
            combatManager.OnCombatFled += HandleCombatFled;
            combatManager.OnEnemyDamaged += HandleEnemyDamaged;
            combatManager.OnEnemyDefeated += HandleEnemyDefeated;
            combatManager.OnEnemyAdded += HandleEnemyAdded;
            combatManager.OnEnemySkillHit += HandleEnemySkillHit;
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

            SpawnEnemyViews();
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

        // Golpe de habilidad: ademas del pulso de disolucion, aparece el efecto de impacto (sprite
        // HitImpact.png) sobre el enemigo, teñido segun el elemento para que se note la diferencia
        // entre habilidades. El tinte se mezcla al 55% para no perder el dibujo original.
        private void HandleEnemySkillHit(int index, Element element)
        {
            if (index < 0 || index >= _activeViews.Count) return;
            var view = _activeViews[index];
            if (view == null || hitImpactFrames == null || hitImpactFrames.Length == 0) return;

            Color tint = Color.Lerp(Color.white, ElementColor(element), 0.55f);
            Quaternion facing = _battleCamera != null ? _battleCamera.transform.rotation : Quaternion.identity;
            HitImpactEffect.Spawn(hitImpactFrames, view.transform.position, tint, facing);
        }

        private static Color ElementColor(Element element)
        {
            switch (element)
            {
                case Element.Fire: return new Color(1f, 0.35f, 0.12f);
                case Element.Ice: return new Color(0.4f, 0.85f, 1f);
                case Element.Volt: return new Color(1f, 0.92f, 0.2f);
                case Element.Slash: return new Color(0.85f, 0.9f, 1f);
                case Element.Strike: return new Color(1f, 0.75f, 0.4f);
                case Element.Pierce: return new Color(0.8f, 1f, 0.7f);
                default: return Color.white;
            }
        }

        private void HandleEnemyDefeated(int index)
        {
            if (index < 0 || index >= _activeViews.Count) return;
            var view = _activeViews[index];
            if (view == null) return;
            view.PlayDeathDissolve(() =>
            {
                if (view != null) Destroy(view.gameObject);
            });
        }

        private void HandleCombatFinished(bool victory, bool wasBoss) => CleanupAfterCombat();

        // Escapar es otra forma de terminar el combate (ni victoria ni derrota): limpia la escena
        // de batalla igual que un fin de combate normal.
        private void HandleCombatFled() => CleanupAfterCombat();

        private void CleanupAfterCombat()
        {
            ClearViews();

            if (dungeonCamera != null) dungeonCamera.enabled = true;
            if (dungeonAudioListener != null) dungeonAudioListener.enabled = true;

            var battleScene = SceneManager.GetSceneByName(battleSceneName);
            if (battleScene.IsValid() && battleScene.isLoaded)
                SceneManager.UnloadSceneAsync(battleScene);
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
