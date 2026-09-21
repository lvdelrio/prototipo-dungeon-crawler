using System;
using System.Collections.Generic;
using System.Linq;

namespace Combat
{
    public enum ActionType { Attack, Skill, Guard, ProtectAll }

    public class PartyAction
    {
        public CharacterStats Actor = null!;
        public ActionType Type;
        public int TargetEnemyIndex;
        public int TargetAllyIndex;

        // Si la habilidad se uso con exito en el mini-juego de tiempo (QTE): pega mas fuerte / cura mas.
        public bool QteSuccess;
    }

    // Multiplicador de poder/curacion cuando el jugador completa a tiempo la secuencia del QTE.
    public static class QteBonus
    {
        public const float Multiplier = 1.25f;
    }

    // Porcentaje del TP maximo que un personaje recupera cada vez que golpea con exito a un
    // enemigo con un ATAQUE BASICO (las habilidades no regeneran TP: si lo hicieran, usar
    // habilidades seria gratis a la larga).
    public static class TpRegen
    {
        public const float PercentOnHit = 0.10f;
    }

    public class CombatEngine
    {
        // Tope de enemigos VIVOS simultaneos en pantalla (coincide con los 4 "parantes" de la
        // escena de batalla): un Slime que se divide nunca puede hacer crecer la pelea mas alla de
        // esto. Los encuentros normales arrancan con 1-3 (ver EnemyFactory.CreateRandomEncounter),
        // asi que el 4to parante solo se ocupa cuando un Slime se parte con los otros 3 ya en pie.
        public const int MaxEnemies = 4;

        // Costo en TP de la habilidad de Protector de proteger a todo el grupo (antes era gratis).
        public const int ProtectAllTpCost = 6;

        // Multiplicador de dano del Ataque en Conjunto (se aplica a la suma de ATK de toda la
        // party viva, y ese numero le pega IGUAL a cada enemigo -- como un golpe final de equipo).
        public const float AllOutAttackMultiplier = 1.5f;

        // El golpe en conjunto se "machacando el boton": la primera pulsacion ya lo dispara, y
        // cada pulsacion EXTRA (hasta un tope) suma mas dano, para que "smashear" tenga sentido.
        public const float AllOutDamagePerExtraPress = 0.12f;
        public const int AllOutMaxPresses = 12;

        // Tope: el golpe en conjunto nunca le saca a un enemigo mas de este porcentaje de SU
        // MaxHP de una sola vez, sin importar cuantos personajes vivos tenga la party ni cuanto se
        // haya machacado el boton -- sin este limite, con una party de 6 el dano total (suma de
        // ATK de todos) mas el bonus de mashear volvia el golpe en un one-shot casi garantizado
        // incluso contra el jefe, sintiendose disparatado en vez de un finisher fuerte.
        public const float AllOutDamageCapFraction = 0.5f;

        // Formula de agro: los personajes del frente concentran mas probabilidad de ser el
        // blanco de los enemigos que los de atras (ver CharacterStats.IsFrontRow).
        public const float FrontRowAggroWeight = 3f;
        public const float BackRowAggroWeight = 1f;

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

        // Se rompio el aguante de TODOS los enemigos vivos a la vez -> se habilita el Ataque en
        // Conjunto. Con 0 enemigos vivos (no deberia pasar en medio de un combate) da false.
        public bool AllEnemiesBroken()
        {
            var alive = Enemies.Where(e => e.IsAlive).ToList();
            return alive.Count > 0 && alive.All(e => e.IsBroken);
        }

        // Arma el orden de turnos de la ronda (por Velocidad descendente) y resetea la guardia de
        // todos antes de empezar. Se expone por separado de la ejecucion para poder resolver la
        // ronda de a un turno a la vez (con pausas/UI entre turno y turno) en vez de todo junto.
        public List<(bool isParty, int idx)> BuildTurnOrder(Dictionary<CharacterStats, PartyAction> actions)
        {
            foreach (var p in Party)
            {
                p.IsGuarding = false;
                p.IsProtectingAll = false;
            }

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
        // falta pausar turno a turno, ni ofrecer el Ataque en Conjunto). Recibe una accion por cada
        // personaje vivo.
        public List<string> ResolveRound(Dictionary<CharacterStats, PartyAction> actions)
        {
            var order = BuildTurnOrder(actions);
            var log = new List<string>();
            foreach (var (isParty, idx) in order)
                log.AddRange(ExecuteTurn(isParty, idx, actions));
            return log;
        }

        // Toda la party viva golpea junta a CADA enemigo vivo por el mismo monto de dano (suma de
        // ATK*AllOutAttackMultiplier de la party, escalada por cuantas veces se "machaco" el
        // boton: mashCount=1 es el minimo -- ya dispara el golpe -- y cada pulsacion extra (hasta
        // AllOutMaxPresses) suma AllOutDamagePerExtraPress mas dano). Pensado para dispararse solo
        // cuando AllEnemiesBroken() es true; al usarse, todos los enemigos golpeados recuperan su
        // aguante (dejan de estar rotos) y siguen combatiendo normal desde la ronda siguiente.
        public List<string> ExecuteAllOutAttack(int mashCount)
        {
            var log = new List<string>();
            var aliveParty = Party.Where(p => p.IsAlive).ToList();
            if (aliveParty.Count == 0) return log;

            int clampedMash = Math.Max(1, Math.Min(mashCount, AllOutMaxPresses));
            float mashMultiplier = 1f + AllOutDamagePerExtraPress * (clampedMash - 1);

            int totalDamage = 0;
            foreach (var p in aliveParty)
                totalDamage += (int)Math.Round(p.Attack * AllOutAttackMultiplier * mashMultiplier);
            totalDamage = Math.Max(1, totalDamage);

            foreach (var enemy in Enemies.Where(e => e.IsAlive).ToList())
            {
                int cap = Math.Max(1, (int)Math.Round(enemy.MaxHP * AllOutDamageCapFraction));
                int dmgToEnemy = Math.Min(totalDamage, cap);
                ApplyDamageToEnemy(enemy, dmgToEnemy, out _);
                if (enemy.IsAlive)
                {
                    enemy.IsBroken = false;
                    enemy.Poise = enemy.MaxPoise;
                }
            }
            log.Add($"¡Ataque en conjunto! Toda la party golpea a la vez (hasta {totalDamage} de daño, tope {AllOutDamageCapFraction:P0} del HP máximo de cada enemigo, x{clampedMash} golpes de botón).");
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

                case ActionType.ProtectAll:
                    if (actor.TP >= ProtectAllTpCost)
                    {
                        actor.TP -= ProtectAllTpCost;
                        actor.IsProtectingAll = true;
                        log.Add($"{actor.Name} se pone al frente para proteger a todo el grupo (recibira todo el dano enemigo de esta ronda).");
                    }
                    else
                    {
                        log.Add($"{actor.Name} no tiene suficiente TP para proteger a todo el grupo.");
                    }
                    break;

                case ActionType.Attack:
                {
                    var target = PickAliveEnemy(action.TargetEnemyIndex);
                    if (target == null) break;
                    int dmg = ComputeDamageVsEnemy(actor.Attack, actor.AttackElement, target, out string note);
                    ApplyDamageToEnemy(target, dmg, out bool poiseBroke);
                    int regenAtk = RegenTpOnHit(actor);
                    log.Add($"{actor.Name} ataca a {target.Name}: {dmg} de daño.{note}{(poiseBroke ? " ¡Guardia rota!" : "")}{(regenAtk > 0 ? $" (+{regenAtk} TP)" : "")}");
                    break;
                }

                case ActionType.Skill:
                    if (actor.IsHealSkill)
                    {
                        var ally = Party[action.TargetAllyIndex];
                        if (actor.TP >= actor.SkillTpCost && ally.IsAlive)
                        {
                            actor.TP -= actor.SkillTpCost;
                            int healAmount = actor.HealAmount;
                            string qteNote = "";
                            if (action.QteSuccess)
                            {
                                healAmount = (int)Math.Round(healAmount * QteBonus.Multiplier);
                                qteNote = " ¡QTE exitoso! Cura de mas.";
                            }
                            int healed = Math.Min(ally.MaxHP - ally.HP, healAmount);
                            ally.HP += healed;
                            log.Add($"{actor.Name} usa {actor.SkillName} en {ally.Name}: recupera {healed} HP.{qteNote}");
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
                            float power = actor.Attack * actor.SkillPower;
                            string qteNote = "";
                            if (action.QteSuccess)
                            {
                                power *= QteBonus.Multiplier;
                                qteNote = " ¡QTE exitoso!";
                            }
                            int dmg = ComputeDamageVsEnemy((int)Math.Round(power), actor.SkillElement, target, out string note);
                            ApplyDamageToEnemy(target, dmg, out bool poiseBroke);
                            // Las habilidades NO regeneran TP (solo los ataques basicos, ver mas abajo).
                            log.Add($"{actor.Name} usa {actor.SkillName} en {target.Name}: {dmg} de daño.{note}{(poiseBroke ? " ¡Guardia rota!" : "")}{qteNote}");
                        }
                        else
                        {
                            int dmg = ComputeDamageVsEnemy(actor.Attack, actor.AttackElement, target, out string note);
                            ApplyDamageToEnemy(target, dmg, out bool poiseBroke);
                            int regenNoTp = RegenTpOnHit(actor);
                            log.Add($"{actor.Name} no tiene TP, ataca normal a {target.Name}: {dmg} de daño.{note}{(poiseBroke ? " ¡Guardia rota!" : "")}{(regenNoTp > 0 ? $" (+{regenNoTp} TP)" : "")}");
                        }
                    }
                    break;
            }
        }

        private void ExecuteEnemyAction(EnemyStats enemy, List<string> log)
        {
            // Si le rompieron el aguante, pierde este turno (y se recupera para el siguiente).
            if (enemy.IsBroken)
            {
                log.Add($"{enemy.Name} esta aturdido (guardia rota) y pierde su turno.");
                enemy.IsBroken = false;
                enemy.Poise = enemy.MaxPoise;
                return;
            }

            var aliveParty = Party.Where(p => p.IsAlive).ToList();
            if (aliveParty.Count == 0) return;

            // Si alguien esta protegiendo a todo el grupo, todo el dano enemigo de esta ronda se
            // le redirige a el/ella (a su propio costo), sin importar la formacion.
            var protector = aliveParty.FirstOrDefault(p => p.IsProtectingAll);
            var target = protector ?? PickAggroTarget(aliveParty);

            int dmg = Math.Max(1, enemy.Attack - target.Defense / 2);
            bool wasGuarding = target.IsGuarding;
            if (wasGuarding) dmg = Math.Max(1, dmg / 2);
            target.HP = Math.Max(0, target.HP - dmg);
            log.Add($"{enemy.Name} ataca a {target.Name}: {dmg} de daño.{(wasGuarding ? " (bloqueado con guardia)" : "")}");
        }

        // Elige a quien ataca un enemigo: cada personaje del frente pesa FrontRowAggroWeight y
        // cada uno de atras BackRowAggroWeight, asi que el frente concentra mas probabilidad de
        // ser el blanco pero el de atras nunca queda en 0% (formula de agro por formacion).
        private CharacterStats PickAggroTarget(List<CharacterStats> aliveParty)
        {
            float totalWeight = aliveParty.Sum(p => p.IsFrontRow ? FrontRowAggroWeight : BackRowAggroWeight);
            double roll = _rng.NextDouble() * totalWeight;
            double acc = 0;
            foreach (var p in aliveParty)
            {
                acc += p.IsFrontRow ? FrontRowAggroWeight : BackRowAggroWeight;
                if (roll < acc) return p;
            }
            return aliveParty[aliveParty.Count - 1];
        }

        // Aplica dano a un enemigo (a su vida y, si esta vivo y no estaba ya roto, a su aguante) y,
        // si con eso muere y tiene OnDeathSplit configurado (p.ej. un Slime grande), lo reemplaza
        // por sus versiones mas debiles -- hasta el tope MaxEnemies. El enemigo original queda
        // "derrotado" en su lugar (no se borra de la lista: mantiene estables los indices que usan
        // el resto de los sistemas). poiseBroke sale en true si este golpe fue el que rompio el
        // aguante (para poder anotarlo en el log del que llama).
        private void ApplyDamageToEnemy(EnemyStats target, int dmg, out bool poiseBroke)
        {
            poiseBroke = false;
            bool wasAlive = target.IsAlive;
            target.HP = Math.Max(0, target.HP - dmg);

            if (target.IsAlive && !target.IsBroken && target.MaxPoise > 0)
            {
                target.Poise = Math.Max(0, target.Poise - dmg);
                if (target.Poise <= 0)
                {
                    target.IsBroken = true;
                    poiseBroke = true;
                }
            }

            if (wasAlive && !target.IsAlive && target.OnDeathSplit != null)
            {
                var split = target.OnDeathSplit;
                target.OnDeathSplit = null; // que los reemplazos no vuelvan a dividirse en cadena
                var replacements = split();
                // Todo o nada: si no hay lugar para TODOS los reemplazos, no se agrega ninguno (un
                // Slime partiendose en un solo hijo, a medias, se veia como un bug en vez de una
                // decision de diseño). Con lugar de sobra, entran todos los que devuelva el split.
                // OJO: el Slime que se acaba de morir NO se borra de Enemies (mantiene indices
                // estables), asi que Enemies.Count todavia lo cuenta como si siguiera "ocupando
                // lugar" aunque ya este muerto -- hay que contar solo los que siguen VIVOS (el
                // Slime recien muerto queda afuera solo porque target.IsAlive ya es false aca).
                int aliveAfterSplit = Enemies.Count(e => e.IsAlive) + replacements.Count;
                if (replacements != null && aliveAfterSplit <= MaxEnemies)
                {
                    Enemies.AddRange(replacements);
                }
            }
        }

        // Recupera un porcentaje del TP maximo del personaje tras un golpe exitoso (ataque basico o
        // habilidad de dano); devuelve cuanto se recupero, para poder anotarlo en el log.
        private int RegenTpOnHit(CharacterStats actor)
        {
            int before = actor.TP;
            int regen = (int)Math.Round(actor.MaxTP * TpRegen.PercentOnHit);
            actor.TP = Math.Min(actor.MaxTP, actor.TP + regen);
            return actor.TP - before;
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
