using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Combat;

namespace Gameplay
{
    // Orquesta el combate por turnos (motor puro en Combat/CombatEngine.cs) y expone el estado que
    // necesita la UI (CombatHUD) para dejar elegir accion a cada personaje vivo, uno por uno, antes
    // de resolver la ronda completa.
    public class CombatManager : MonoBehaviour
    {
        public List<CharacterStats> Party { get; private set; }
        public List<EnemyStats> Enemies { get; private set; }
        public List<string> Log { get; } = new List<string>();

        public bool IsActive { get; private set; }
        public bool IsBossFight { get; private set; }

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

            Log.Clear();
            Log.Add(isBoss ? "¡Aparece el Guardián de Piedra!" : "¡Un grupo de enemigos aparece!");
            AdvanceChooser();
        }

        public bool HasChooser => IsActive && _chooserIndex < Party.Count;
        public CharacterStats GetChooser() => HasChooser ? Party[_chooserIndex] : null;

        public IEnumerable<CharacterStats> AliveParty => Party.Where(p => p.IsAlive);
        public IEnumerable<EnemyStats> AliveEnemies => Enemies.Where(e => e.IsAlive);

        public void SubmitAction(PartyAction action)
        {
            if (!IsActive) return;
            _queuedActions[action.Actor] = action;
            _chooserIndex++;
            AdvanceChooser();
        }

        private void AdvanceChooser()
        {
            while (_chooserIndex < Party.Count && !Party[_chooserIndex].IsAlive)
                _chooserIndex++;

            if (_chooserIndex >= Party.Count)
                ResolveRound();
        }

        private void ResolveRound()
        {
            var roundLog = _engine.ResolveRound(_queuedActions);
            Log.AddRange(roundLog);
            _queuedActions.Clear();
            _chooserIndex = 0;

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

        private void EndCombat(bool victory)
        {
            IsActive = false;
            OnCombatFinished?.Invoke(victory, IsBossFight);
        }
    }
}
