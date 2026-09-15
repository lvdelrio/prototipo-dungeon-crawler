using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Combat;

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

        private CombatEngine _engine;
        private readonly System.Random _rng = new System.Random();
        private readonly Dictionary<CharacterStats, PartyAction> _queuedActions = new Dictionary<CharacterStats, PartyAction>();
        private int _chooserIndex;

        void Awake()
        {
            Party = PartyFactory.CreateDefaultParty();
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

                // Si a este personaje le toca ejecutar una habilidad, el QTE se juega justo ahora
                // (en el momento real de su turno), no cuando se elige el objetivo.
                if (isParty && qteManager != null && Party[idx].IsAlive && _queuedActions.TryGetValue(Party[idx], out var action) && action.Type == ActionType.Skill)
                    yield return RunSkillQte(Party[idx], action);

                var turnLog = _engine.ExecuteTurn(isParty, idx, _queuedActions);
                Log.AddRange(turnLog);

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
                EndCombat(victory: true);
            }
            else if (_engine.AllPartyDefeated())
            {
                Log.Add("La party cae derrotada... despiertan malheridos.");
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

        private void EndCombat(bool victory)
        {
            IsActive = false;
            OnCombatFinished?.Invoke(victory, IsBossFight);
        }
    }
}
