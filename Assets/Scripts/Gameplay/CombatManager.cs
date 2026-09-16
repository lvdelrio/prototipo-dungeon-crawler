using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Combat;
using Meta;

namespace Gameplay
{
    // Orquesta el combate por turnos (motor puro en Combat/CombatEngine.cs) y expone el estado que
    // necesita la UI (CombatHUD) para dejar elegir accion a cada personaje vivo, uno por uno, y
    // despues resuelve la ronda turno a turno (con una pausa entre cada uno) para que se note
    // claramente cuando le toca a un aliado y cuando le toca a un enemigo.
    public class CombatManager : MonoBehaviour
    {
        [Header("Ritmo del combate")]
        [Tooltip("Pausa (segundos) antes de mostrar el resultado de cada turno individual dentro de una ronda.")]
        public float turnRevealDelay = 0.7f;

        [Header("QTE de habilidades")]
        public QteManager qteManager;
        [Tooltip("Tiempo (segundos) para completar la secuencia de 3 teclas del QTE.")]
        public float qteTimeLimit = 2.5f;

        [Header("Feedback de impacto (flash + sacudida de camara)")]
        public CombatFeedback feedback;

        [Header("Huir del combate")]
        [Tooltip("Chance (0-100) que aporta CADA personaje vivo al intentar huir (6 personajes vivos x 10% = 60%). No se puede huir de un jefe.")]
        [Range(0f, 100f)]
        public float fleeChancePerCharacter = 10f;

        // Chance total de huir: la suma de lo que aporta cada personaje vivo (menos personajes
        // vivos = mas dificil escapar), nunca mas de 100%.
        public float FleeChancePercent => Mathf.Min(100f, (Party?.Count(p => p.IsAlive) ?? 0) * fleeChancePerCharacter);

        public List<CharacterStats> Party { get; private set; }
        public List<EnemyStats> Enemies { get; private set; }
        public List<string> Log { get; } = new List<string>();

        public bool IsActive { get; private set; }
        public bool IsBossFight { get; private set; }
        public bool IsResolvingRound { get; private set; }
        public string CurrentTurnActorName { get; private set; }
        public bool CurrentTurnIsParty { get; private set; }

        // (victoria, era jefe)
        public event Action<bool, bool> OnCombatFinished;
        public event Action OnCombatStarted;
        // La party escapo con exito del combate (no cuenta como victoria ni como derrota).
        public event Action OnCombatFled;
        // Indice dentro de Enemies del enemigo que recibio dano / que acaba de caer.
        public event Action<int> OnEnemyDamaged;
        public event Action<int> OnEnemyDefeated;
        // Enemigo nuevo agregado a Enemies a mitad de combate (p.ej. crias de un Slime dividido).
        public event Action<int> OnEnemyAdded;
        // Golpe de HABILIDAD (no ataque basico) contra un enemigo: indice + elemento de la habilidad.
        public event Action<int, Element> OnEnemySkillHit;

        private CombatEngine _engine;
        private readonly System.Random _rng = new System.Random();
        private readonly Dictionary<CharacterStats, PartyAction> _queuedActions = new Dictionary<CharacterStats, PartyAction>();
        private int _chooserIndex;

        // Crea una party nueva con las stats base y le aplica los niveles de mejora permanentes
        // comprados en runs anteriores. La llama DungeonManager al arrancar y cada vez que empieza
        // una run nueva (tras la pantalla de tienda/mejoras post-derrota).
        public void InitializeParty(MetaProgress meta)
        {
            Party = PartyFactory.CreateDefaultParty();
            meta.ApplyUpgradesToParty(Party);
        }

        public void StartEncounter(bool isBoss)
        {
            if (IsActive) return;

            IsBossFight = isBoss;
            Enemies = isBoss ? new List<EnemyStats> { EnemyFactory.CreateBoss() } : EnemyFactory.CreateRandomEncounter(_rng);
            _engine = new CombatEngine(Party, Enemies, _rng);
            _queuedActions.Clear();
            _chooserIndex = 0;
            IsActive = true;
            IsResolvingRound = false;

            Log.Clear();
            Log.Add(isBoss ? "¡Aparece el Guardián de Piedra!" : "¡Un grupo de enemigos aparece!");
            OnCombatStarted?.Invoke();
            AdvanceChooser();
        }

        public bool HasChooser => IsActive && !IsResolvingRound && _chooserIndex < Party.Count;
        public CharacterStats GetChooser() => HasChooser ? Party[_chooserIndex] : null;

        public IEnumerable<CharacterStats> AliveParty => Party.Where(p => p.IsAlive);
        public IEnumerable<EnemyStats> AliveEnemies => Enemies.Where(e => e.IsAlive);

        public void SubmitAction(PartyAction action)
        {
            if (!IsActive || IsResolvingRound) return;
            _queuedActions[action.Actor] = action;
            _chooserIndex++;
            AdvanceChooser();
        }

        // Boton "Rendirse": termina el combate de inmediato como derrota, sin jugar mas rondas.
        public void Surrender()
        {
            if (!IsActive || IsResolvingRound) return;
            Log.Add("La party decide rendirse.");
            EndCombat(victory: false);
        }

        // Boton "Huir": chance de escapar sin jugar la ronda. No se puede huir de un jefe. Si
        // falla, se pierde el turno de toda la party esta ronda (solo actuan los enemigos).
        public void TryFlee()
        {
            if (!IsActive || IsResolvingRound) return;
            if (IsBossFight)
            {
                Log.Add("No se puede huir de un jefe.");
                return;
            }

            _queuedActions.Clear();
            bool success = UnityEngine.Random.value < FleeChancePercent / 100f;
            if (success)
            {
                Log.Add("¡La party escapa del combate!");
                EndCombatFled();
            }
            else
            {
                Log.Add("El intento de huir fallo... los enemigos aprovechan la oportunidad.");
                _chooserIndex = Party.Count;
                StartCoroutine(ResolveRoundCoroutine());
            }
        }

        // Boton de test (para probar el resto del juego rapido): gana el combate actual al
        // instante, sin jugarlo. Cuenta como victoria normal (suma enemigos/jefes derrotados).
        public void SkipFightForTesting()
        {
            if (!IsActive || IsResolvingRound) return;
            foreach (var e in Enemies) e.HP = 0;
            Log.Add("[TEST] Combate saltado: victoria instantanea.");
            EndCombat(victory: true);
        }

        // Boton "Auto": pone Ataque basico (al primer enemigo vivo) para todos los personajes que
        // todavia no eligieron accion esta ronda, y arranca la resolucion de inmediato.
        public void AutoAttackRemaining()
        {
            if (!IsActive || IsResolvingRound) return;
            foreach (var p in Party.Where(p => p.IsAlive && !_queuedActions.ContainsKey(p)))
                _queuedActions[p] = new PartyAction { Actor = p, Type = ActionType.Attack, TargetEnemyIndex = 0 };
            _chooserIndex = Party.Count;
            AdvanceChooser();
        }

        private void AdvanceChooser()
        {
            while (_chooserIndex < Party.Count && !Party[_chooserIndex].IsAlive)
                _chooserIndex++;

            if (_chooserIndex >= Party.Count)
                StartCoroutine(ResolveRoundCoroutine());
        }

        private IEnumerator ResolveRoundCoroutine()
        {
            IsResolvingRound = true;
            var order = _engine.BuildTurnOrder(_queuedActions);

            foreach (var (isParty, idx) in order)
            {
                CurrentTurnIsParty = isParty;
                CurrentTurnActorName = isParty ? Party[idx].Name : Enemies[idx].Name;
                yield return new WaitForSeconds(turnRevealDelay);

                PartyAction currentPartyAction = null;
                if (isParty) _queuedActions.TryGetValue(Party[idx], out currentPartyAction);

                // Si a este personaje le toca ejecutar una habilidad, el QTE se juega justo ahora
                // (en el momento real de su turno), no cuando se elige el objetivo.
                if (isParty && qteManager != null && Party[idx].IsAlive && currentPartyAction != null && currentPartyAction.Type == ActionType.Skill)
                    yield return RunSkillQte(Party[idx], currentPartyAction);

                int[] partyHpBefore = Party.Select(p => p.HP).ToArray();
                int[] enemyHpBefore = Enemies.Select(e => e.HP).ToArray();

                var turnLog = _engine.ExecuteTurn(isParty, idx, _queuedActions);
                Log.AddRange(turnLog);

                // Golpe de habilidad (no ataque basico, no curacion) contra un enemigo: dispara el
                // efecto de impacto especial ademas del feedback normal.
                bool isSkillHit = isParty && currentPartyAction != null && currentPartyAction.Type == ActionType.Skill && !Party[idx].IsHealSkill;
                Element skillElement = isSkillHit ? Party[idx].SkillElement : Element.None;
                ReportHitFeedback(partyHpBefore, enemyHpBefore, isSkillHit, skillElement);

                // En cuanto la pelea queda decidida no se esperan mas turnos ni personajes: se corta
                // la ronda ahi mismo en vez de seguir resolviendo al resto del orden de turnos.
                if (_engine.AllEnemiesDefeated() || _engine.AllPartyDefeated())
                    break;
            }

            CurrentTurnActorName = null;
            _queuedActions.Clear();
            _chooserIndex = 0;
            IsResolvingRound = false;

            if (_engine.AllEnemiesDefeated())
            {
                Log.Add(IsBossFight ? "¡Venciste al Guardián de Piedra!" : "¡Victoria!");
                // Le da tiempo a la animacion de disolucion del ultimo enemigo caido antes de
                // cerrar el combate y descargar la escena de batalla.
                yield return new WaitForSeconds(1f);
                EndCombat(victory: true);
            }
            else if (_engine.AllPartyDefeated())
            {
                Log.Add("La party cae derrotada. La run termina aca...");
                foreach (var p in Party) p.HP = Math.Max(1, p.HP);
                EndCombat(victory: false);
            }
            else
            {
                AdvanceChooser();
            }
        }

        // Elige al azar una de las 2 secuencias fijas de QTE del personaje y espera a que el
        // jugador la resuelva (a tiempo o no) antes de dejar que se ejecute la habilidad.
        private IEnumerator RunSkillQte(CharacterStats actor, PartyAction action)
        {
            var sequence = _rng.Next(2) == 0 ? actor.SkillSequenceA : actor.SkillSequenceB;
            if (sequence == null || sequence.Length == 0) yield break;

            bool? result = null;
            qteManager.Begin(sequence, qteTimeLimit, success => result = success);
            while (result == null) yield return null;
            action.QteSuccess = result.Value;
        }

        // Compara el HP de todos antes/despues del turno que se acaba de ejecutar: dispara el
        // flash/sacudida de camara y avisa (por indice) que enemigo recibio dano o cayo, para que
        // la escena de batalla (BattleStageController/EnemyView) anime el golpe o la disolucion
        // de muerte. El motor de combate puro no sabe nada de esto.
        private void ReportHitFeedback(int[] partyHpBefore, int[] enemyHpBefore, bool isSkillHit, Element skillElement)
        {
            for (int i = 0; i < Party.Count; i++)
            {
                int dmg = partyHpBefore[i] - Party[i].HP;
                if (dmg > 0) feedback?.OnPartyHit(dmg);
                else if (dmg < 0) feedback?.OnHeal(-dmg);
            }

            // Solo se recorren los indices que YA existian antes de este turno: si un Slime se
            // dividio al morir, Enemies.Count crecio durante ExecuteTurn y enemyHpBefore (tomado
            // antes) no tiene entradas para los indices nuevos.
            int previousEnemyCount = enemyHpBefore.Length;
            for (int i = 0; i < previousEnemyCount; i++)
            {
                int dmg = enemyHpBefore[i] - Enemies[i].HP;
                if (dmg <= 0) continue;

                feedback?.OnEnemyHit(dmg);
                OnEnemyDamaged?.Invoke(i);
                if (isSkillHit) OnEnemySkillHit?.Invoke(i, skillElement);
                if (enemyHpBefore[i] > 0 && Enemies[i].HP <= 0)
                    OnEnemyDefeated?.Invoke(i);
            }

            for (int i = previousEnemyCount; i < Enemies.Count; i++)
                OnEnemyAdded?.Invoke(i);
        }

        private void EndCombat(bool victory)
        {
            IsActive = false;
            OnCombatFinished?.Invoke(victory, IsBossFight);
        }

        private void EndCombatFled()
        {
            IsActive = false;
            OnCombatFled?.Invoke();
        }
    }
}
