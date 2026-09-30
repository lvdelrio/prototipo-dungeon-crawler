using System;
using System.Collections.Generic;
using System.Linq;

namespace Combat
{
    public enum ActionType { Attack, Skill, Guard, ProtectAll, Item }

    // Que hace un ActionType.Item -- la carga en si (comprada en la tienda, ver
    // Meta.MetaProgress.PotionCharges/ReviverCharges) se valida y descuenta ANTES de encolar la
    // accion (ver Gameplay/CombatManager.SubmitItemAction), no aca: el motor puro no sabe nada de
    // inventario, solo ejecuta la accion que ya se pago.
    public enum ItemActionKind { Potion, Reviver }

    public class PartyAction
    {
        public CharacterStats Actor = null!;
        public ActionType Type;
        public int TargetEnemyIndex;
        public int TargetAllyIndex;
        public ItemActionKind ItemKind;

        // Si la habilidad se uso con exito en el mini-juego de tiempo (QTE): pega mas fuerte / cura mas.
        public bool QteSuccess;

        // Solo para la habilidad versatil del Trovador (CharacterStats.IsVersatileBuffSkill): true
        // si el objetivo elegido fue un aliado (TargetAllyIndex, buff) en vez de un enemigo
        // (TargetEnemyIndex, debuff).
        public bool TargetIsAlly;
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
        // Tope de enemigos VIVOS simultaneos en pantalla (coincide con los 6 "parantes" de la
        // escena de batalla). Los encuentros normales arrancan con 1-3 (ver
        // EnemyFactory.CreateRandomEncounter), pero el PEOR caso real es un encuentro de 3 Slimes:
        // si los 3 mueren y los 3 se dividen, terminan siendo 3*2=6 crias vivas a la vez (ninguna
        // de las 3 "ranuras" originales sigue ocupada, todas fueron reemplazadas). El tope tiene
        // que cubrir ese caso completo, no solo "un Slime de mas".
        public const int MaxEnemies = 6;

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

        // El aguante (Poise) ya NO baja lo mismo que el HP: un ataque basico machacado sin pensar
        // apenas lo mella, pero explotar la debilidad elemental del enemigo lo rompe mucho mas
        // rapido -- en un caso real, un punto flojo se resiente mas con un golpe bien dado que con
        // uno cualquiera. Un golpe de habilidad que NO es debilidad queda en el medio (1x, como
        // era antes para todo).
        public const float PoiseDamageBasicAttack = 0.5f;
        public const float PoiseDamageWeaknessHit = 1.6f;

        // Alquimista (CharacterStats.SkillIsAoe): su habilidad pega a TODOS los enemigos vivos a la
        // vez, pero con este descuento por objetivo -- si no, barrer la pantalla entera sin
        // penalidad seria estrictamente mejor que pegarle a uno solo.
        public const float AoeDamageMultiplier = 0.65f;

        // Trovador (CharacterStats.IsVersatileBuffSkill): cuanto sube el Ataque de un aliado
        // buffeado (flat), y por cuantas RONDAS completas dura tanto el buff como el debuff
        // (BuildTurnOrder descuenta 1 al arrancar cada ronda nueva).
        public const int VersatileBuffAmount = 6;
        public const int VersatileBuffRounds = 3;

        // El debuff de Defensa NO es un numero flat (a diferencia del buff de Ataque de arriba):
        // un -6 fijo es enorme contra un enemigo temprano de DEF 6-8 pero un redondeo contra un
        // jefe tardio de DEF 30+, asi que pierde sentido a medida que avanza la run. Calculado
        // como % de la Defensa ACTUAL del objetivo al momento de tirarla (snapshot, no se
        // recalcula si su Defensa cambia despues), escala solo con lo grande que es el enemigo.
        public const float DefenseDebuffPercent = 0.35f;

        // Suerte (CharacterStats.Luck, 0-100): chance de critico en los propios golpes, x1.5 dano
        // cuando sale. Evasion (CharacterStats/EnemyStats.Evasion, 0-100): chance de esquivar por
        // completo un golpe recibido, ademas de pesar un poco menos en la formula de agro (ver
        // PickAggroTarget) -- mas dificil de encontrar, no solo de acertarle una vez encontrado.
        public const float CritDamageMultiplier = 1.5f;

        // Items usables en combate (ver ActionType.Item) -- la carga se paga afuera del motor, ver
        // Gameplay/CombatManager.SubmitItemAction. Pocion: cura un flat. Revivir: trae de vuelta a
        // un aliado caido con una fraccion de su HP maximo, nunca al 100% (para que igual sea mejor
        // no caer que confiar siempre en que te revivan).
        public const int PotionHealAmount = 40;
        public const float ReviverHpFraction = 0.4f;

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

                // Buff del Trovador: dura VersatileBuffRounds rondas COMPLETAS: se descuenta 1 al
                // arrancar cada ronda nueva (no la misma ronda en la que se aplico), y al llegar a 0
                // se apaga solo (vuelve a 0 el monto, no solo el contador).
                if (p.AttackBuffRoundsLeft > 0)
                {
                    p.AttackBuffRoundsLeft--;
                    if (p.AttackBuffRoundsLeft <= 0) p.AttackBuffAmount = 0;
                }
            }
            foreach (var e in Enemies)
            {
                if (e.DefenseDebuffRoundsLeft > 0)
                {
                    e.DefenseDebuffRoundsLeft--;
                    if (e.DefenseDebuffRoundsLeft <= 0) e.DefenseDebuffAmount = 0;
                }

                // Dano sostenido (veneno/sangrado, ver EnemyStats.DotRoundsLeft): tickea al
                // arrancar cada ronda nueva, reusa ApplyDamageToEnemy para que muerte/OnDeathSplit
                // se manejen igual que cualquier otro golpe. El popup de dano flotante (CombatHUD,
                // compara HP entre frames) ya lo muestra solo, no hace falta loguearlo aparte.
                if (e.DotRoundsLeft > 0 && e.IsAlive)
                {
                    e.DotRoundsLeft--;
                    ApplyDamageToEnemy(e, e.DotDamagePerRound, isBasicAttack: false, isWeaknessHit: false, out _, attacker: null);
                }
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
                // Ni "basico" ni "debilidad" -- toda la party junta pegandole a todos por igual,
                // sin elemento (ver arriba): multiplicador de aguante neutro (1x), igual que antes.
                ApplyDamageToEnemy(enemy, dmgToEnemy, isBasicAttack: false, isWeaknessHit: false, out _);
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

                case ActionType.Item:
                {
                    // La carga (Meta.MetaProgress.PotionCharges/ReviverCharges) ya se pago y
                    // desconto ANTES de llegar aca (ver Gameplay/CombatManager.SubmitItemAction) --
                    // este motor puro solo resuelve el efecto.
                    var ally = Party[action.TargetAllyIndex];
                    if (action.ItemKind == ItemActionKind.Potion)
                    {
                        if (!ally.IsAlive) { log.Add($"{actor.Name} no puede usar una Poción en {ally.Name} (caído)."); break; }
                        int healed = Math.Min(ally.MaxHP - ally.HP, PotionHealAmount);
                        ally.HP += healed;
                        log.Add($"{actor.Name} usa una Poción en {ally.Name}: recupera {healed} HP.");
                    }
                    else if (action.ItemKind == ItemActionKind.Reviver)
                    {
                        if (ally.IsAlive) { log.Add($"{actor.Name} no puede usar un Revivir en {ally.Name} (no está caído)."); break; }
                        ally.HP = Math.Max(1, (int)Math.Round(ally.MaxHP * ReviverHpFraction));
                        log.Add($"{actor.Name} usa un Revivir en {ally.Name}: vuelve al combate con {ally.HP} HP.");
                    }
                    break;
                }

                case ActionType.Attack:
                {
                    // El ataque basico SIEMPRE es a un solo objetivo, para toda clase (incluido el
                    // Alquimista: solo su HABILIDAD es AoE, ver CharacterStats.SkillIsAoe mas abajo).
                    Element element = ResolveElement(actor, actor.AttackElement);
                    var target = PickAliveEnemy(action.TargetEnemyIndex);
                    if (target == null) break;
                    int dmg = ComputeDamageVsEnemy(actor.EffectiveAttack, element, target, out string note, out bool isWeak, actor.Luck);
                    ApplyDamageToEnemy(target, dmg, isBasicAttack: true, isWeaknessHit: isWeak, out bool poiseBroke, actor);
                    ConsumeLoadedBullet(actor);
                    int regenAtk = RegenTpOnHit(actor);
                    log.Add($"{actor.Name} ataca a {target.Name}: {dmg} de daño.{note}{(poiseBroke ? " ¡Guardia rota!" : "")}{(regenAtk > 0 ? $" (+{regenAtk} TP)" : "")}");
                    break;
                }

                case ActionType.Skill:
                    if (actor.IsHealSkill)
                    {
                        var ally = Party[action.TargetAllyIndex];
                        if (actor.TP < actor.SkillTpCost)
                        {
                            log.Add($"{actor.Name} no pudo usar {actor.SkillName} (sin TP).");
                            break;
                        }
                        actor.TP -= actor.SkillTpCost;
                        // El Medic (y cualquier otro sanador futuro) especializado en magia:
                        // curar tambien escala un poco con MagicAttack, no solo el flat HealAmount.
                        int healAmount = actor.HealAmount + actor.MagicAttack / 2;
                        string qteNote = "";
                        if (action.QteSuccess)
                        {
                            healAmount = (int)Math.Round(healAmount * QteBonus.Multiplier);
                            qteNote = " ¡QTE exitoso!";
                        }
                        if (!ally.IsAlive)
                        {
                            // "Habilidad de revivir": la misma habilidad de curar, apuntada a un
                            // aliado caido, lo revive en vez de curarle HP que no tiene -- no hace
                            // falta una habilidad separada para esto.
                            ally.HP = Math.Min(ally.MaxHP, healAmount);
                            log.Add($"{actor.Name} usa {actor.SkillName} en {ally.Name}: lo revive con {ally.HP} HP.{qteNote}");
                        }
                        else
                        {
                            int healed = Math.Min(ally.MaxHP - ally.HP, healAmount);
                            ally.HP += healed;
                            log.Add($"{actor.Name} usa {actor.SkillName} en {ally.Name}: recupera {healed} HP.{qteNote}");
                        }
                    }
                    else if (actor.IsSelfStanceSkill)
                    {
                        // Berserker: postura propia, sin objetivo -- alterna EffectiveAttack/Defense
                        // via CharacterStats.IsEnraged (ver ahi el bonus/penalidad exactos).
                        if (actor.TP >= actor.SkillTpCost)
                        {
                            actor.TP -= actor.SkillTpCost;
                            actor.IsEnraged = !actor.IsEnraged;
                            log.Add(actor.IsEnraged
                                ? $"{actor.Name} usa {actor.SkillName}: entra en furia (mas ataque, menos defensa)."
                                : $"{actor.Name} usa {actor.SkillName} de nuevo: se calma.");
                        }
                        else
                        {
                            log.Add($"{actor.Name} no tiene suficiente TP para {actor.SkillName}.");
                        }
                    }
                    else if (actor.IsVersatileBuffSkill)
                    {
                        // Trovador: sobre un aliado buffea Ataque, sobre un enemigo debuffea
                        // Defensa -- el jugador elige cual al tirarla (ver PartyAction.TargetIsAlly).
                        if (actor.TP < actor.SkillTpCost)
                        {
                            log.Add($"{actor.Name} no tiene suficiente TP para {actor.SkillName}.");
                            break;
                        }
                        actor.TP -= actor.SkillTpCost;
                        // QTE exitoso: el buff/debuff pega mas fuerte (mismo multiplicador que
                        // dano/curacion), en vez de no hacer nada como pasaria si se dejara pasar.
                        int amount = action.QteSuccess ? (int)Math.Round(VersatileBuffAmount * QteBonus.Multiplier) : VersatileBuffAmount;
                        string qteNoteVersatile = action.QteSuccess ? " ¡QTE exitoso!" : "";
                        if (action.TargetIsAlly)
                        {
                            var ally = Party[action.TargetAllyIndex];
                            if (!ally.IsAlive) { log.Add($"{actor.Name} no pudo usar {actor.SkillName} (objetivo caído)."); break; }
                            ally.AttackBuffAmount = amount;
                            ally.AttackBuffRoundsLeft = VersatileBuffRounds;
                            log.Add($"{actor.Name} usa {actor.SkillName} en {ally.Name}: +{amount} ATQ por {VersatileBuffRounds} rondas.{qteNoteVersatile}");
                        }
                        else
                        {
                            var target = PickAliveEnemy(action.TargetEnemyIndex);
                            if (target == null) break;
                            int baseDebuff = Math.Max(1, (int)Math.Round(target.Defense * DefenseDebuffPercent));
                            int debuffAmount = action.QteSuccess ? (int)Math.Round(baseDebuff * QteBonus.Multiplier) : baseDebuff;
                            target.DefenseDebuffAmount = debuffAmount;
                            target.DefenseDebuffRoundsLeft = VersatileBuffRounds;
                            log.Add($"{actor.Name} usa {actor.SkillName} en {target.Name}: -{debuffAmount} DEF ({DefenseDebuffPercent:P0} de su Defensa) por {VersatileBuffRounds} rondas.{qteNoteVersatile}");
                        }
                    }
                    else
                    {
                        Element element = ResolveElement(actor, actor.SkillElement);
                        int basePower = actor.SkillUsesMagicAttack ? actor.MagicAttack : actor.EffectiveAttack;

                        if (actor.SkillIsAoe)
                        {
                            var targets = Enemies.Where(e => e.IsAlive).ToList();
                            if (targets.Count == 0) break;

                            // Sin TP para la habilidad AoE: cae al ataque basico normal, que SIEMPRE
                            // es a un solo objetivo (nunca a todos, ni siquiera para el Alquimista) --
                            // como el submit de una habilidad AoE nunca pide target, se usa el primer
                            // enemigo vivo (mismo criterio de PickAliveEnemy con indice invalido).
                            if (actor.TP < actor.SkillTpCost)
                            {
                                var basicTarget = PickAliveEnemy(action.TargetEnemyIndex);
                                if (basicTarget == null) break;
                                Element basicElement = ResolveElement(actor, actor.AttackElement);
                                int dmgBasic = ComputeDamageVsEnemy(actor.EffectiveAttack, basicElement, basicTarget, out string noteBasic, out bool weakBasic, actor.Luck);
                                ApplyDamageToEnemy(basicTarget, dmgBasic, isBasicAttack: true, isWeaknessHit: weakBasic, out bool brokeBasic, actor);
                                ConsumeLoadedBullet(actor);
                                int regenNoTp = RegenTpOnHit(actor);
                                log.Add($"{actor.Name} no tiene TP, ataca normal a {basicTarget.Name}: {dmgBasic}{noteBasic}{(brokeBasic ? " ¡Rota!" : "")}{(regenNoTp > 0 ? $" (+{regenNoTp} TP)" : "")}");
                                break;
                            }

                            actor.TP -= actor.SkillTpCost;
                            float aoePower = basePower * actor.SkillPower * AoeDamageMultiplier;
                            string qteNoteAoe = "";
                            if (action.QteSuccess) { aoePower *= QteBonus.Multiplier; qteNoteAoe = " ¡QTE exitoso!"; }
                            var parts = new List<string>();
                            foreach (var t in targets)
                            {
                                int dmgEach = ComputeDamageVsEnemy((int)Math.Round(aoePower), element, t, out string noteEach, out bool weakEach, actor.Luck);
                                ApplyDamageToEnemy(t, dmgEach, isBasicAttack: false, isWeaknessHit: weakEach, out bool brokeEach, actor);
                                parts.Add($"{t.Name} {dmgEach}{noteEach}{(brokeEach ? " ¡Rota!" : "")}");
                            }
                            ConsumeLoadedBullet(actor);
                            log.Add($"{actor.Name} usa {actor.SkillName} en TODOS: {string.Join(", ", parts)}.{qteNoteAoe}");
                        }
                        else
                        {
                            var target = PickAliveEnemy(action.TargetEnemyIndex);
                            if (target == null) break;
                            if (actor.TP >= actor.SkillTpCost)
                            {
                                actor.TP -= actor.SkillTpCost;
                                float power = basePower * actor.SkillPower;
                                string qteNote = "";
                                if (action.QteSuccess)
                                {
                                    power *= QteBonus.Multiplier;
                                    qteNote = " ¡QTE exitoso!";
                                }
                                int dmg = ComputeDamageVsEnemy((int)Math.Round(power), element, target, out string note, out bool isWeak, actor.Luck);
                                ApplyDamageToEnemy(target, dmg, isBasicAttack: false, isWeaknessHit: isWeak, out bool poiseBroke, actor);
                                ConsumeLoadedBullet(actor);
                                // Las habilidades NO regeneran TP (solo los ataques basicos, ver mas abajo).
                                log.Add($"{actor.Name} usa {actor.SkillName} en {target.Name}: {dmg} de daño.{note}{(poiseBroke ? " ¡Guardia rota!" : "")}{qteNote}");
                            }
                            else
                            {
                                Element basicElement = ResolveElement(actor, actor.AttackElement);
                                int dmg = ComputeDamageVsEnemy(actor.EffectiveAttack, basicElement, target, out string note, out bool isWeak, actor.Luck);
                                ApplyDamageToEnemy(target, dmg, isBasicAttack: true, isWeaknessHit: isWeak, out bool poiseBroke, actor);
                                ConsumeLoadedBullet(actor);
                                int regenNoTp = RegenTpOnHit(actor);
                                log.Add($"{actor.Name} no tiene TP, ataca normal a {target.Name}: {dmg} de daño.{note}{(poiseBroke ? " ¡Guardia rota!" : "")}{(regenNoTp > 0 ? $" (+{regenNoTp} TP)" : "")}");
                            }
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

            // Evasion: chance de esquivar el golpe entero ANTES de calcular dano (protegiendo a
            // todo el grupo no te salva de que te elijan, pero un Protector nunca tiene mucha
            // Evasion de base igual -- ver PartyFactory).
            if (target.Evasion > 0 && _rng.NextDouble() * 100.0 < target.Evasion)
            {
                log.Add($"{enemy.Name} ataca a {target.Name}: ¡esquivado!");
                return;
            }

            int dmg = Math.Max(1, enemy.Attack - target.EffectiveDefense / 2);

            // Resistencia elemental (Chest/Greaves equipados, ver CharacterStats.Resistances): recien
            // ahora AttackElement de un enemigo (seteado en EnemyFactory) tiene algun efecto real.
            int resistPercent = target.ResistancePercentFor(enemy.AttackElement);
            if (resistPercent > 0)
                dmg = Math.Max(1, dmg - (int)Math.Round(dmg * resistPercent / 100f));

            bool wasGuarding = target.IsGuarding;
            if (wasGuarding) dmg = Math.Max(1, dmg / 2);
            target.HP = Math.Max(0, target.HP - dmg);
            log.Add($"{enemy.Name} ataca a {target.Name}: {dmg} de daño.{(wasGuarding ? " (bloqueado con guardia)" : "")}");

            // Espinas (CharacterStats.ThornsReflectPercent): pasivo, ninguna tirada de por medio --
            // mientras este equipado, todo golpe recibido le devuelve una parte al que lo dio.
            if (target.ThornsReflectPercent > 0 && enemy.IsAlive)
            {
                int reflected = Math.Max(1, (int)Math.Round(dmg * target.ThornsReflectPercent / 100f));
                ApplyDamageToEnemy(enemy, reflected, isBasicAttack: false, isWeaknessHit: false, out _);
                log.Add($"Las espinas de {target.Name} le devuelven {reflected} de daño a {enemy.Name}.");
            }
        }

        // Elige a quien ataca un enemigo: cada personaje del frente pesa FrontRowAggroWeight y
        // cada uno de atras BackRowAggroWeight (el frente concentra mas probabilidad de ser el
        // blanco, pero el de atras nunca queda en 0%), ajustado ademas por Evasion -- mas evasion,
        // un poco menos probable ser elegido de entrada (nunca menos del 25% del peso base, para
        // que ni el mas evasivo quede practicamente invisible).
        private CharacterStats PickAggroTarget(List<CharacterStats> aliveParty)
        {
            float totalWeight = aliveParty.Sum(AggroWeight);
            double roll = _rng.NextDouble() * totalWeight;
            double acc = 0;
            foreach (var p in aliveParty)
            {
                acc += AggroWeight(p);
                if (roll < acc) return p;
            }
            return aliveParty[aliveParty.Count - 1];
        }

        private float AggroWeight(CharacterStats p)
        {
            float baseWeight = p.IsFrontRow ? FrontRowAggroWeight : BackRowAggroWeight;
            float evasionFactor = Math.Max(0.25f, 1f - p.Evasion / 200f);
            return baseWeight * evasionFactor;
        }

        // Aplica dano a un enemigo (a su vida y, si esta vivo y no estaba ya roto, a su aguante) y,
        // si con eso muere y tiene OnDeathSplit configurado (p.ej. un Slime grande), lo reemplaza
        // por sus versiones mas debiles -- hasta el tope MaxEnemies. El enemigo original queda
        // "derrotado" en su lugar (no se borra de la lista: mantiene estables los indices que usan
        // el resto de los sistemas). poiseBroke sale en true si este golpe fue el que rompio el
        // aguante (para poder anotarlo en el log del que llama). isBasicAttack/isWeaknessHit
        // deciden que porcentaje del dano de HP se le resta tambien al aguante (ver
        // PoiseDamageBasicAttack/PoiseDamageWeaknessHit) -- el dano a la vida en si no cambia.
        // attacker (null en golpes que no vienen de un personaje, p.ej. un tick de veneno):
        // si tiene un arma con OnHitStatusChancePercent, este es el UNICO lugar donde se tira esa
        // chance -- centralizado aca en vez de en cada uno de los ~6 call sites de Attack/Skill.
        private void ApplyDamageToEnemy(EnemyStats target, int dmg, bool isBasicAttack, bool isWeaknessHit, out bool poiseBroke, CharacterStats attacker = null)
        {
            poiseBroke = false;
            if (dmg <= 0) return; // esquivado (EnemyStats.Evasion en ComputeDamageVsEnemy) -- ni vida ni aguante
            bool wasAlive = target.IsAlive;
            target.HP = Math.Max(0, target.HP - dmg);

            if (target.IsAlive && attacker != null && attacker.OnHitStatusChancePercent > 0
                && _rng.NextDouble() * 100.0 < attacker.OnHitStatusChancePercent)
            {
                int dotDamage = Math.Max(1, (int)Math.Round(target.MaxHP * attacker.OnHitStatusDamagePercent / 100f));
                target.DotDamagePerRound = dotDamage;
                target.DotRoundsLeft = attacker.OnHitStatusRounds;
                target.DotLabel = attacker.OnHitStatusName;
            }

            if (target.IsAlive && !target.IsBroken && target.MaxPoise > 0)
            {
                float poiseMultiplier = isWeaknessHit
                    ? PoiseDamageWeaknessHit / Math.Max(1f, target.PoiseWeaknessResistance)
                    : isBasicAttack ? PoiseDamageBasicAttack : 1f;
                int poiseDamage = Math.Max(1, (int)Math.Round(dmg * poiseMultiplier));
                target.Poise = Math.Max(0, target.Poise - poiseDamage);
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

        // Gunner: si tiene una bala elemental cargada Y todavia le queda stock de ese tipo, ese
        // elemento reemplaza al que se le pasa (el propio del ataque/habilidad). Cualquier otro
        // personaje (LoadedBulletElement siempre None) usa defaultElement sin cambios.
        private Element ResolveElement(CharacterStats actor, Element defaultElement)
        {
            if (actor.LoadedBulletElement == Element.None) return defaultElement;
            return BulletStock(actor, actor.LoadedBulletElement) > 0 ? actor.LoadedBulletElement : defaultElement;
        }

        private int BulletStock(CharacterStats actor, Element element)
        {
            switch (element)
            {
                case Element.Fire: return actor.FireBullets;
                case Element.Ice: return actor.IceBullets;
                case Element.Volt: return actor.VoltBullets;
                default: return 0;
            }
        }

        // Gasta 1 bala del tipo cargado (si el golpe de verdad uso una bala elemental); si se queda
        // sin stock de ese tipo, vuelve solo a las balas normales (LoadedBulletElement = None).
        private void ConsumeLoadedBullet(CharacterStats actor)
        {
            if (actor.LoadedBulletElement == Element.None) return;
            switch (actor.LoadedBulletElement)
            {
                case Element.Fire: if (actor.FireBullets > 0) actor.FireBullets--; break;
                case Element.Ice: if (actor.IceBullets > 0) actor.IceBullets--; break;
                case Element.Volt: if (actor.VoltBullets > 0) actor.VoltBullets--; break;
            }
            if (BulletStock(actor, actor.LoadedBulletElement) <= 0) actor.LoadedBulletElement = Element.None;
        }

        private EnemyStats? PickAliveEnemy(int preferredIndex)
        {
            if (preferredIndex >= 0 && preferredIndex < Enemies.Count && Enemies[preferredIndex].IsAlive)
                return Enemies[preferredIndex];
            return Enemies.FirstOrDefault(e => e.IsAlive);
        }

        // luckPercent (0-100, CharacterStats.Luck de quien ataca): chance de golpe critico (x1.5).
        // Devuelve 0 (dmg real, no el piso de 1 de siempre) si el enemigo esquiva por completo via
        // su propio Evasion -- ApplyDamageToEnemy corta apenas ve un 0, sin tocar vida ni aguante.
        private int ComputeDamageVsEnemy(int power, Element element, EnemyStats enemy, out string note, out bool isWeak, int luckPercent = 0)
        {
            isWeak = false;
            if (enemy.Evasion > 0 && _rng.NextDouble() * 100.0 < enemy.Evasion)
            {
                note = " ¡Esquivado!";
                return 0;
            }

            int dmg = Math.Max(1, power - enemy.EffectiveDefense / 2);
            note = "";
            isWeak = enemy.IsWeakTo(element);
            if (isWeak)
            {
                dmg *= 2;
                note = " ¡Débil!";
            }
            else if (element != Element.None && element == enemy.Resistance)
            {
                dmg = Math.Max(1, dmg / 2);
                note = " (resistido)";
            }

            if (luckPercent > 0 && _rng.NextDouble() * 100.0 < luckPercent)
            {
                dmg = (int)Math.Round(dmg * CritDamageMultiplier);
                note += " ¡Crítico!";
            }
            return dmg;
        }
    }
}
