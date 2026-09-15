using System;
using System.Collections.Generic;
using System.Linq;

namespace Combat
{
    public enum ActionType { Attack, Skill, Guard }

    public class PartyAction
    {
        public CharacterStats Actor = null!;
        public ActionType Type;
        public int TargetEnemyIndex;
        public int TargetAllyIndex;
    }

    public class CombatEngine
    {
        public readonly List<CharacterStats> Party;
        public readonly List<EnemyStats> Enemies;
        private readonly Random _rng;

        public CombatEngine(List<CharacterStats> party, List<EnemyStats> enemies, Random rng)
        {
            Party = party;
            Enemies = enemies;
            _rng = rng;
        }

        public bool AllEnemiesDefeated() => Enemies.All(e => !e.IsAlive);
        public bool AllPartyDefeated() => Party.All(p => !p.IsAlive);

        // Arma el orden de turnos de la ronda (por Velocidad descendente) y resetea la guardia de
        // todos antes de empezar. Se expone por separado de la ejecucion para poder resolver la
        // ronda de a un turno a la vez (con pausas/UI entre turno y turno) en vez de todo junto.
        public List<(bool isParty, int idx)> BuildTurnOrder(Dictionary<CharacterStats, PartyAction> actions)
        {
            foreach (var p in Party) p.IsGuarding = false;

            var order = new List<(bool isParty, int idx, int speed)>();
            for (int i = 0; i < Party.Count; i++)
                if (Party[i].IsAlive && actions.ContainsKey(Party[i]))
                    order.Add((true, i, Party[i].Speed));
            for (int i = 0; i < Enemies.Count; i++)
                if (Enemies[i].IsAlive)
                    order.Add((false, i, Enemies[i].Speed));

            return order.OrderByDescending(o => o.speed).Select(o => (o.isParty, o.idx)).ToList();
        }

        // Ejecuta el turno de UN combatiente (que ya deberia venir de BuildTurnOrder) y devuelve el
        // log de lo que paso en ese turno especifico (vacio si ya estaba caido para entonces).
        public List<string> ExecuteTurn(bool isParty, int idx, Dictionary<CharacterStats, PartyAction> actions)
        {
            var log = new List<string>();
            if (isParty)
            {
                var actor = Party[idx];
                if (actor.IsAlive && actions.TryGetValue(actor, out var action))
                    ExecutePartyAction(actor, action, log);
            }
            else
            {
                var enemy = Enemies[idx];
                if (enemy.IsAlive)
                    ExecuteEnemyAction(enemy, log);
            }
            return log;
        }

        // Resuelve una ronda completa de una sola vez (usado por tests/simulaciones donde no hace
        // falta pausar turno a turno). Recibe una accion por cada personaje vivo.
        public List<string> ResolveRound(Dictionary<CharacterStats, PartyAction> actions)
        {
            var order = BuildTurnOrder(actions);
            var log = new List<string>();
            foreach (var (isParty, idx) in order)
                log.AddRange(ExecuteTurn(isParty, idx, actions));
            return log;
        }

        private void ExecutePartyAction(CharacterStats actor, PartyAction action, List<string> log)
        {
            switch (action.Type)
            {
                case ActionType.Guard:
                    actor.IsGuarding = true;
                    log.Add($"{actor.Name} se pone en guardia.");
                    break;

                case ActionType.Attack:
                {
                    var target = PickAliveEnemy(action.TargetEnemyIndex);
                    if (target == null) break;
                    int dmg = ComputeDamageVsEnemy(actor.Attack, actor.AttackElement, target, out string note);
                    target.HP = Math.Max(0, target.HP - dmg);
                    log.Add($"{actor.Name} ataca a {target.Name}: {dmg} de daño.{note}");
                    break;
                }

                case ActionType.Skill:
                    if (actor.IsHealSkill)
                    {
                        var ally = Party[action.TargetAllyIndex];
                        if (actor.TP >= actor.SkillTpCost && ally.IsAlive)
                        {
                            actor.TP -= actor.SkillTpCost;
                            int healed = Math.Min(ally.MaxHP - ally.HP, actor.HealAmount);
                            ally.HP += healed;
                            log.Add($"{actor.Name} usa {actor.SkillName} en {ally.Name}: recupera {healed} HP.");
                        }
                        else
                        {
                            log.Add($"{actor.Name} no pudo usar {actor.SkillName} (sin TP o objetivo caido).");
                        }
                    }
                    else
                    {
                        var target = PickAliveEnemy(action.TargetEnemyIndex);
                        if (target == null) break;
                        if (actor.TP >= actor.SkillTpCost)
                        {
                            actor.TP -= actor.SkillTpCost;
                            int power = (int)Math.Round(actor.Attack * actor.SkillPower);
                            int dmg = ComputeDamageVsEnemy(power, actor.SkillElement, target, out string note);
                            target.HP = Math.Max(0, target.HP - dmg);
                            log.Add($"{actor.Name} usa {actor.SkillName} en {target.Name}: {dmg} de daño.{note}");
                        }
                        else
                        {
                            int dmg = ComputeDamageVsEnemy(actor.Attack, actor.AttackElement, target, out string note);
                            target.HP = Math.Max(0, target.HP - dmg);
                            log.Add($"{actor.Name} no tiene TP, ataca normal a {target.Name}: {dmg} de daño.{note}");
                        }
                    }
                    break;
            }
        }

        private void ExecuteEnemyAction(EnemyStats enemy, List<string> log)
        {
            var aliveParty = Party.Where(p => p.IsAlive).ToList();
            if (aliveParty.Count == 0) return;
            var target = aliveParty[_rng.Next(aliveParty.Count)];

            int dmg = Math.Max(1, enemy.Attack - target.Defense / 2);
            bool wasGuarding = target.IsGuarding;
            if (wasGuarding) dmg = Math.Max(1, dmg / 2);
            target.HP = Math.Max(0, target.HP - dmg);
            log.Add($"{enemy.Name} ataca a {target.Name}: {dmg} de daño.{(wasGuarding ? " (bloqueado con guardia)" : "")}");
        }

        private EnemyStats? PickAliveEnemy(int preferredIndex)
        {
            if (preferredIndex >= 0 && preferredIndex < Enemies.Count && Enemies[preferredIndex].IsAlive)
                return Enemies[preferredIndex];
            return Enemies.FirstOrDefault(e => e.IsAlive);
        }

        private int ComputeDamageVsEnemy(int power, Element element, EnemyStats enemy, out string note)
        {
            int dmg = Math.Max(1, power - enemy.Defense / 2);
            note = "";
            if (element != Element.None && element == enemy.Weakness)
            {
                dmg *= 2;
                note = " ¡Débil!";
            }
            else if (element != Element.None && element == enemy.Resistance)
            {
                dmg = Math.Max(1, dmg / 2);
                note = " (resistido)";
            }
            return dmg;
        }
    }
}
