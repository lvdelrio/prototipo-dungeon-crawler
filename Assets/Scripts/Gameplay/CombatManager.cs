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
        // vivos = mas dificil escapar), nunca mas de 100% -- salvo contra un FOE (ver
        // StartFoeEncounter), donde queda fija en FoeFleeChancePercent sin importar cuantos
        // personajes sigan vivos: es un enemigo fuerte de proposito, escapar tiene que costar.
        public const float FoeFleeChancePercent = 30f;
        public float FleeChancePercent => IsFoeFight
            ? FoeFleeChancePercent
            : Mathf.Min(100f, (Party?.Count(p => p.IsAlive) ?? 0) * fleeChancePerCharacter);

        public List<CharacterStats> Party { get; private set; }
        public List<EnemyStats> Enemies { get; private set; }
        public List<string> Log { get; } = new List<string>();

        public bool IsActive { get; private set; }
        public bool IsBossFight { get; private set; }
        // No se resetea al terminar el combate (ver EndCombat/EndCombatFled): queda leible por
        // DungeonManager.HandleCombatFinished/HandleCombatFled, que se llaman DESDE el mismo evento
        // que dispara el fin del combate, para saber si hay que empujar al FOE hacia atras
        // (huida exitosa) o despawnearlo (victoria) -- recien se pisa en el proximo StartEncounter/
        // StartFoeEncounter.
        public bool IsFoeFight { get; private set; }
        public bool IsResolvingRound { get; private set; }
        public string CurrentTurnActorName { get; private set; }
        public bool CurrentTurnIsParty { get; private set; }

        // Estadisticas de ESTE combate (se resetean en StartEncounter), para el resumen de
        // victoria -- inspirado en juegos que muestran, al final de cada pelea, informacion util
        // para la proxima decision (quien hizo mas dano, quien curo, quien recibio mas golpes) en
        // vez de solo un mensaje generico de "Victoria" y seguir caminando.
        public Dictionary<CharacterStats, int> DamageDealtThisFight { get; } = new Dictionary<CharacterStats, int>();
        public Dictionary<CharacterStats, int> HealingDoneThisFight { get; } = new Dictionary<CharacterStats, int>();
        public Dictionary<CharacterStats, int> DamageTakenThisFight { get; } = new Dictionary<CharacterStats, int>();
        public int AllOutAttackDamageThisFight { get; private set; }

        // True apenas se derrota al ultimo enemigo por combate REAL (no por SkipFightForTesting,
        // que sigue terminando el combate al instante como antes): el combate en si sigue
        // "activo" (la escena de batalla no se descarga todavia) hasta que el jugador confirma
        // con DismissVictorySummary(), para poder mostrar el resumen de la pelea.
        public bool IsShowingVictorySummary { get; private set; }

        [Header("Ataque en Conjunto (se habilita si se rompe el aguante de TODOS los enemigos a la vez)")]
        [Tooltip("Segundos que dura la ventana para 'machacar' el boton: cada pulsacion suma mas dano (ver CombatEngine.ExecuteAllOutAttack). Si nadie aprieta nada, se pierde la oportunidad esta ronda.")]
        public float allOutAttackMashWindow = 3f;

        // True mientras dura la ventana de "machacado" del Ataque en Conjunto. La UI (CombatHUD)
        // muestra el boton/aviso especial solo mientras esto es true.
        public bool AllOutAttackReady { get; private set; }

        // Cuantas veces se apreto el boton/Espacio durante la ventana actual (0 mientras nadie
        // apreto nada todavia). Mas pulsaciones = mas dano al ejecutar el golpe.
        public int AllOutAttackMashCount { get; private set; }

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
        // Golpe de HABILIDAD (no ataque basico) contra un enemigo: indice + elemento + intensidad
        // (SkillPower de quien la uso, tipicamente 1.4-2 -- mas fuerte la habilidad, mas grande y
        // llamativo el efecto). Un ataque basico siempre usa intensidad 1 (ver ReportHitFeedback).
        public event Action<int, Element, float> OnEnemySkillHit;
        // CUALQUIER golpe contra un enemigo (ataque basico o habilidad, no curacion): indice +
        // elemento + intensidad. Para el efecto de shader elemental, que se ve en todos los golpes.
        public event Action<int, Element, float> OnEnemyElementalHit;
        // Se le acaba de romper el aguante a este enemigo (indice): pierde su proximo turno.
        public event Action<int> OnEnemyPoiseBroken;
        // Se rompio el aguante de TODOS los enemigos a la vez: aparece el aviso de Ataque en Conjunto.
        public event Action OnAllOutAttackReady;
        // El jugador uso el Ataque en Conjunto (golpe dorado a todos los enemigos vivos a la vez).
        public event Action OnAllOutAttackUsed;

        private CombatEngine _engine;
        private readonly System.Random _rng = new System.Random();
        private readonly Dictionary<CharacterStats, PartyAction> _queuedActions = new Dictionary<CharacterStats, PartyAction>();
        private int _chooserIndex;
        private bool _allOutOfferedThisRound;

        // Crea una party nueva con las stats base y le aplica los niveles de mejora permanentes
        // comprados en runs anteriores. La llama DungeonManager al arrancar y cada vez que empieza
        // una run nueva (tras la pantalla de tienda/mejoras post-derrota).
        // Guardado para SubmitItemAction (validar/descontar cargas de Pocion/Revivir): a
        // diferencia del TP (que vive en CharacterStats, dentro del motor puro), las cargas de
        // items son un recurso de META, comprado en la tienda y persistido entre runs.
        private MetaProgress _meta;

        public void InitializeParty(MetaProgress meta)
        {
            _meta = meta;
            // meta.PartyClasses vacio = guardado viejo o todavia no se eligio nunca una party propia
            // (ver PartyCreationHUD) -- cae de vuelta a la composicion clasica de siempre.
            Party = meta.PartyClasses != null && meta.PartyClasses.Count > 0
                ? PartyFactory.CreateParty(meta.PartyClasses)
                : PartyFactory.CreateDefaultParty();
            meta.ApplyUpgradesToParty(Party);
        }

        public int PotionCharges => _meta?.PotionCharges ?? 0;
        public int ReviverCharges => _meta?.ReviverCharges ?? 0;

        // Boton "Items": a diferencia de SubmitAction (que solo encola), esto ADEMAS valida y
        // descuenta la carga correspondiente en MetaProgress -- si no hay ninguna, no hace nada
        // (la UI no deberia dejar llegar a este caso, pero por las dudas no corrompe nada).
        public void SubmitItemAction(CharacterStats actor, ItemActionKind kind, int targetAllyIndex)
        {
            if (!IsActive || IsResolvingRound || _meta == null) return;

            if (kind == ItemActionKind.Potion)
            {
                if (_meta.PotionCharges <= 0) return;
                _meta.PotionCharges--;
            }
            else if (kind == ItemActionKind.Reviver)
            {
                if (_meta.ReviverCharges <= 0) return;
                _meta.ReviverCharges--;
            }
            MetaSaveService.Save(_meta);

            SubmitAction(new PartyAction { Actor = actor, Type = ActionType.Item, ItemKind = kind, TargetAllyIndex = targetAllyIndex });
        }

        // floorIndex (0 = primer piso): escala dificultad -- daño de enemigos y que tan probable es
        // un encuentro de 3 en vez de 2 suben a medida que se baja mas. Default 0 para no romper
        // llamadas existentes (tests) que no les importa la escala. biome (0 = zona original, 1 =
        // Cueva Intergaláctica detrás de la Puerta Fría, ver DungeonFloor.Biome) cambia todo el
        // bestiario, incluido el jefe.
        public void StartEncounter(bool isBoss, int floorIndex = 0, int biome = 0)
        {
            if (IsActive) return;

            IsBossFight = isBoss;
            IsFoeFight = false;
            if (biome == 1)
            {
                Enemies = isBoss ? new List<EnemyStats> { EnemyFactory.CreateKadulu(floorIndex) } : EnemyFactory.CreateCaveEncounter(_rng, floorIndex);
                StartEncounterCommon(isBoss ? "¡Kadulu emerge de la oscuridad!" : "¡Algo se mueve entre las rocas!");
                return;
            }
            Enemies = isBoss ? new List<EnemyStats> { EnemyFactory.CreateBoss(floorIndex) } : EnemyFactory.CreateRandomEncounter(_rng, floorIndex);
            StartEncounterCommon(isBoss ? "¡Aparece el Guardián de Piedra!" : "¡Un grupo de enemigos aparece!");
        }

        // Colision con un FOE (ver Gameplay/FoeController): pelea 1 contra 1 con un enemigo fuerte
        // de proposito, huida mas dificil (ver FleeChancePercent) -- no cuenta como jefe (IsBossFight
        // sigue en false, SI se puede huir), pero DungeonManager reacciona distinto al terminar
        // (empuja al FOE si escapaste, lo despawnea si lo venciste).
        public void StartFoeEncounter(EnemyStats foe)
        {
            if (IsActive || foe == null) return;

            IsBossFight = false;
            IsFoeFight = true;
            Enemies = new List<EnemyStats> { foe };
            StartEncounterCommon($"¡{foe.Name} te alcanzó!");
        }

        private void StartEncounterCommon(string openingLogLine)
        {
            _engine = new CombatEngine(Party, Enemies, _rng);
            _queuedActions.Clear();
            _chooserIndex = 0;
            IsActive = true;
            IsResolvingRound = false;
            IsShowingVictorySummary = false;

            DamageDealtThisFight.Clear();
            HealingDoneThisFight.Clear();
            DamageTakenThisFight.Clear();
            AllOutAttackDamageThisFight = 0;
            foreach (var p in Party.Where(p => p.IsAlive))
            {
                DamageDealtThisFight[p] = 0;
                HealingDoneThisFight[p] = 0;
                DamageTakenThisFight[p] = 0;
            }

            Log.Clear();
            Log.Add(openingLogLine);
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

        // Boton "Volver": deshace la accion ya elegida del ultimo personaje que la eligio esta
        // ronda (el jugador se arrepintio) y vuelve a dejarlo elegir de nuevo. Solo funciona
        // mientras se estan eligiendo acciones (no una vez que la ronda ya se esta resolviendo).
        public bool CanGoBack => IsActive && !IsResolvingRound && _queuedActions.Count > 0;

        public void GoToPreviousChooser()
        {
            if (!CanGoBack) return;

            int idx = _chooserIndex - 1;
            while (idx >= 0 && (!Party[idx].IsAlive || !_queuedActions.ContainsKey(Party[idx])))
                idx--;
            if (idx < 0) return;

            _queuedActions.Remove(Party[idx]);
            _chooserIndex = idx;
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

        // Boton "Continuar" del resumen de victoria: recien aca termina el combate de verdad (se
        // descarga la escena de batalla, se resume la exploracion).
        public void DismissVictorySummary()
        {
            if (!IsShowingVictorySummary) return;
            IsShowingVictorySummary = false;
            EndCombat(victory: true);
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

        // Boton/Espacio "Ataque en Conjunto": cada pulsacion durante la ventana de "machacado"
        // suma una mas al conteo (hasta CombatEngine.AllOutMaxPresses); no dispara nada por si
        // sola, es OfferAllOutAttack() quien ejecuta el golpe (con mas dano cuantas mas veces se
        // haya apretado) al terminar la ventana. Solo tiene efecto mientras AllOutAttackReady.
        public void TriggerAllOutAttack()
        {
            if (!AllOutAttackReady) return;
            AllOutAttackMashCount = Mathf.Min(AllOutAttackMashCount + 1, CombatEngine.AllOutMaxPresses);
        }

        // Formacion: se edita desde el menu de pausa DURANTE LA EXPLORACION (no en combate, para
        // no complicar el orden de turnos de una ronda que ya esta en marcha).
        public bool CanChangeFormation => Party != null && !IsActive;

        // Pone a "target" al frente o al fondo. Para mantener siempre 3 y 3 (ver
        // CombatEngine.FrontRowAggroWeight), si el cambio desbalancea la formacion se intercambia
        // automaticamente con el primero que encuentre del lado que queda de mas: asi el jugador
        // solo elige "quiero a este adelante/atras" sin tener que armar el par a mano.
        // Curar fuera de combate (menu de pausa, pestana Habilidades): misma cuenta que la rama
        // IsHealSkill de CombatEngine.ExecutePartyAction (TP -= SkillTpCost, curar hasta HealAmount
        // sin pasarse del maximo), pero sin QTE ni orden de turnos -- es aritmetica directa sobre
        // los MISMOS CharacterStats persistentes que ve el jugador al entrar en combate, asi que el
        // resultado se nota de inmediato. Devuelve cuanto se curo de verdad (0 si no se pudo: sin
        // TP suficiente, objetivo caido, o el que cura no tiene habilidad de curacion).
        public int UseHealSkillOutOfCombat(CharacterStats healer, CharacterStats target)
        {
            if (!CanChangeFormation || healer == null || target == null) return 0;
            if (!healer.IsHealSkill || !healer.IsAlive || !target.IsAlive) return 0;
            if (healer.TP < healer.SkillTpCost) return 0;

            healer.TP -= healer.SkillTpCost;
            // Misma cuenta que el heal en combate (CombatEngine.ExecutePartyAction, rama
            // IsHealSkill): el MagicAttack de quien cura suma un extra sobre el flat HealAmount.
            int healAmount = healer.HealAmount + healer.MagicAttack / 2;
            int healed = Math.Max(0, Math.Min(target.MaxHP - target.HP, healAmount));
            target.HP += healed;
            return healed;
        }

        // Pocion fuera de combate (menu de pausa): misma cuenta que ActionType.Item/Potion en
        // combate (CombatEngine.PotionHealAmount), pero descontando la carga directo aca en vez de
        // pasar por SubmitItemAction (no hay ronda/turno fuera de combate). Devuelve cuanto se
        // curo de verdad (0 si no se pudo: sin cargas, objetivo caido, o HP ya lleno).
        public int UsePotionOutOfCombat(MetaProgress meta, CharacterStats target)
        {
            if (!CanChangeFormation || meta == null || target == null) return 0;
            if (meta.PotionCharges <= 0 || !target.IsAlive) return 0;

            int healed = Math.Max(0, Math.Min(target.MaxHP - target.HP, CombatEngine.PotionHealAmount));
            if (healed <= 0) return 0;

            meta.PotionCharges--;
            MetaSaveService.Save(meta);
            target.HP += healed;
            return healed;
        }

        public void SetFrontRow(CharacterStats target, bool front)
        {
            if (!CanChangeFormation || target == null || target.IsFrontRow == front) return;
            var partner = Party.FirstOrDefault(p => p != target && p.IsFrontRow == front);
            target.IsFrontRow = front;
            if (partner != null) partner.IsFrontRow = !front;
        }

        // Intercambio EXPLICITO entre 2 integrantes puntuales (click en uno, click en el otro --
        // ver PauseMenuHUD.DrawFormation), a diferencia de SetFrontRow (que mueve a uno solo y le
        // busca pareja cualquiera del otro lado). Mantiene el balance 3/3 siempre, sin importar si
        // "a" y "b" ya estaban del mismo lado (en ese caso no cambia nada).
        public void SwapFormation(CharacterStats a, CharacterStats b)
        {
            if (!CanChangeFormation || a == null || b == null || a == b) return;
            bool aWasFront = a.IsFrontRow;
            a.IsFrontRow = b.IsFrontRow;
            b.IsFrontRow = aWasFront;
        }

        // Cambia la INSTANCIA equipada en UN slot de una clase (menu de pausa, fuera de combate) y
        // ajusta al toque las stats del personaje YA CREADO (no solo el guardado permanente):
        // recalcula la suma de TODOS sus 6 slots antes y despues del cambio y aplica la diferencia,
        // incluyendo el HP actual si cambia el maximo (asi no hace falta esperar a la proxima run
        // para ver el efecto ni recrear la party entera). Devuelve false si el cambio no era valido
        // (instancia no poseida, ya puesta en otro slot, no calza en este slot, o arma restringida
        // a otras clases -- ver MetaProgress.SetEquippedInstance) y en ese caso no toca nada.
        public bool SetEquippedItemLive(MetaProgress meta, CharacterClass cls, EquipmentSlotType slot, string newInstanceId, int accessoryIndex = 0)
        {
            if (!CanChangeFormation || meta == null) return false;
            var character = Party.FirstOrDefault(p => p.Class == cls);
            if (character == null) return false;

            var before = EquipmentTotals.From(meta.GetEquippedItems(cls));
            if (!meta.SetEquippedInstance(cls, slot, newInstanceId, accessoryIndex)) return false;
            var after = EquipmentTotals.From(meta.GetEquippedItems(cls));

            character.Attack += after.Attack - before.Attack;
            character.MagicAttack += after.MagicAttack - before.MagicAttack;
            character.Defense += after.Defense - before.Defense;
            character.Speed += after.Speed - before.Speed;
            character.Evasion += after.Evasion - before.Evasion;
            int dHp = after.MaxHp - before.MaxHp;
            character.MaxHP += dHp;
            character.HP = Mathf.Clamp(character.HP + dHp, 1, character.MaxHP);

            // Estos no son deltas: se recalculan enteros desde los items equipados (ninguna clase
            // los tiene de base fuera del equipo, asi que pisarlos con el total actual es seguro).
            character.OnHitStatusName = after.OnHitWeapon?.OnHitStatusName;
            character.OnHitStatusChancePercent = after.OnHitWeapon?.OnHitStatusChancePercent ?? 0;
            character.OnHitStatusDamagePercent = after.OnHitWeapon?.OnHitStatusDamagePercent ?? 0;
            character.OnHitStatusRounds = after.OnHitWeapon?.OnHitStatusRounds ?? 0;
            character.ThornsReflectPercent = after.Thorns;
            character.Resistances = after.Resistances;
            return true;
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
            _allOutOfferedThisRound = false;
            var order = _engine.BuildTurnOrder(_queuedActions);

            foreach (var (isParty, idx) in order)
            {
                CurrentTurnIsParty = isParty;
                CurrentTurnActorName = isParty ? Party[idx].Name : Enemies[idx].Name;
                yield return new WaitForSeconds(turnRevealDelay);

                PartyAction currentPartyAction = null;
                if (isParty) _queuedActions.TryGetValue(Party[idx], out currentPartyAction);

                // Si a este personaje le toca ejecutar una habilidad, el QTE se juega justo ahora
                // (en el momento real de su turno), no cuando se elige el objetivo. Se salta para
                // la postura propia del Berserker (IsSelfStanceSkill): no hay ningun numero que un
                // QTE exitoso pueda mejorar ahi, jugarlo igual se sentiria como una trampa.
                if (isParty && qteManager != null && Party[idx].IsAlive && currentPartyAction != null
                    && currentPartyAction.Type == ActionType.Skill && !Party[idx].IsSelfStanceSkill)
                    yield return RunSkillQte(Party[idx], currentPartyAction);

                int[] partyHpBefore = Party.Select(p => p.HP).ToArray();
                int[] enemyHpBefore = Enemies.Select(e => e.HP).ToArray();
                bool[] enemyBrokenBefore = Enemies.Select(e => e.IsBroken).ToArray();

                var turnLog = _engine.ExecuteTurn(isParty, idx, _queuedActions);
                Log.AddRange(turnLog);
                AccumulateFightStats(isParty, idx, partyHpBefore, enemyHpBefore);

                // Golpe de habilidad (no ataque basico, no curacion) contra un enemigo: dispara el
                // efecto de impacto especial (shader unico por elemento) ademas del feedback normal.
                // Un ataque BASICO nunca usa Party[idx].AttackElement para el feedback visual: sin
                // importar el arma/elemento propio de cada personaje, todo ataque basico se reporta
                // como Element.Strike (el "golpe" naranjo generico y comun a todos, ver
                // ElementVisuals.GolpeColor). Solo las HABILIDADES conservan su elemento real y por
                // lo tanto su shader unico (SkillMaterialFor en BattleStageController) -- la
                // distincion visual por elemento es exclusiva de las habilidades, nunca del ataque
                // basico de cada personaje.
                bool isSkillHit = isParty && currentPartyAction != null && currentPartyAction.Type == ActionType.Skill && !Party[idx].IsHealSkill;
                Element hitElement = Element.None;
                float hitIntensity = 1f;
                if (isSkillHit) { hitElement = Party[idx].SkillElement; hitIntensity = Party[idx].SkillPower; }
                else if (isParty && currentPartyAction != null && currentPartyAction.Type == ActionType.Attack) hitElement = Element.Strike;
                ReportHitFeedback(partyHpBefore, enemyHpBefore, enemyBrokenBefore, isSkillHit, hitElement, hitIntensity);

                // En cuanto la pelea queda decidida no se esperan mas turnos ni personajes: se corta
                // la ronda ahi mismo en vez de seguir resolviendo al resto del orden de turnos.
                if (_engine.AllEnemiesDefeated() || _engine.AllPartyDefeated())
                    break;

                // Se rompio el aguante de TODOS los enemigos vivos a la vez (una sola oferta por
                // ronda): se pausa para dejar que el jugador decida usar el Ataque en Conjunto.
                if (!_allOutOfferedThisRound && _engine.AllEnemiesBroken())
                {
                    _allOutOfferedThisRound = true;
                    yield return OfferAllOutAttack();
                    if (AllOutAttackMashCount > 0)
                    {
                        // A proposito, no es un bug de estado: el resto del orden de turnos de ESTA
                        // ronda se descarta (junto con _queuedActions.Clear() de mas abajo, asi que
                        // ninguna accion en cola se arrastra a la ronda siguiente) porque el Ataque
                        // en Conjunto YA les pego a todos los enemigos vivos. Antes esto pasaba en
                        // silencio y se sentia como que a esos personajes "se les cancelo el turno"
                        // sin explicacion -- este log aclara que fue el Ataque en Conjunto. Todos
                        // los personajes que se quedaron sin actuar vuelven a elegir accion normal
                        // en la ronda siguiente (AdvanceChooser mas abajo).
                        Log.Add("¡El resto de la ronda se salta: el Ataque en Conjunto ya definio el turno!");
                        break;
                    }
                }
            }

            CurrentTurnActorName = null;
            _queuedActions.Clear();
            _chooserIndex = 0;
            IsResolvingRound = false;

            if (_engine.AllEnemiesDefeated())
            {
                Log.Add(IsBossFight ? "¡Venciste al Guardián de Piedra!" : IsFoeFight ? "¡Venciste al FOE!" : "¡Victoria!");
                // Le da tiempo a la animacion de disolucion del ultimo enemigo caido antes de
                // mostrar el resumen de la pelea. El combate NO termina todavia (EndCombat recien
                // se llama desde DismissVictorySummary, cuando el jugador confirma haber visto el
                // resumen): la escena de batalla se queda cargada mientras tanto.
                yield return new WaitForSeconds(1f);
                IsShowingVictorySummary = true;
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

        // Pausa la ronda para ofrecer el Ataque en Conjunto: durante allOutAttackMashWindow
        // segundos cuenta cuantas veces se aprieta el boton/Espacio, y al terminar la ventana
        // ejecuta el golpe (con mas dano cuantas mas pulsaciones hubo) -- o no hace nada si nadie
        // apreto ni una vez, perdiendo la oportunidad esta ronda.
        private IEnumerator OfferAllOutAttack()
        {
            AllOutAttackMashCount = 0;
            AllOutAttackReady = true;
            Log.Add("¡Se rompe el aguante de TODOS los enemigos a la vez! ¡Machacá el botón para el Ataque en Conjunto!");
            OnAllOutAttackReady?.Invoke();

            float t = 0f;
            while (t < allOutAttackMashWindow)
            {
                t += Time.deltaTime;
                yield return null;
            }
            AllOutAttackReady = false;

            if (AllOutAttackMashCount <= 0) yield break;

            int[] enemyHpBeforeBurst = Enemies.Select(e => e.HP).ToArray();
            int previousCount = enemyHpBeforeBurst.Length;

            var burstLog = _engine.ExecuteAllOutAttack(AllOutAttackMashCount);
            Log.AddRange(burstLog);
            feedback?.OnAllOutAttack();

            for (int i = 0; i < previousCount; i++)
            {
                int dmg = enemyHpBeforeBurst[i] - Enemies[i].HP;
                if (dmg <= 0) continue;
                // Dano del golpe en conjunto: es de toda la party junta (la formula suma el ATK de
                // todos los vivos), asi que no se le atribuye a un solo personaje -- se muestra
                // como su propia fila aparte en el resumen de victoria.
                AllOutAttackDamageThisFight += dmg;
                OnEnemyDamaged?.Invoke(i);
                if (enemyHpBeforeBurst[i] > 0 && Enemies[i].HP <= 0)
                    OnEnemyDefeated?.Invoke(i);
            }
            for (int i = previousCount; i < Enemies.Count; i++)
                OnEnemyAdded?.Invoke(i);

            OnAllOutAttackUsed?.Invoke();
            yield return new WaitForSeconds(0.6f);
        }

        // Suma al total de ESTE combate cuanto dano hizo / cuanto curo el actor de este turno (si
        // fue un personaje de la party), y cuanto dano recibio cada miembro de la party (sin
        // importar quien actuo) -- para el resumen de victoria (ver DamageDealtThisFight/
        // HealingDoneThisFight/DamageTakenThisFight). El Ataque en Conjunto se acumula aparte, en
        // OfferAllOutAttack, porque no es de un solo personaje.
        private void AccumulateFightStats(bool isParty, int idx, int[] partyHpBefore, int[] enemyHpBefore)
        {
            if (isParty && idx < Party.Count)
            {
                var actor = Party[idx];
                int dealt = 0;
                for (int i = 0; i < enemyHpBefore.Length && i < Enemies.Count; i++)
                    dealt += Math.Max(0, enemyHpBefore[i] - Enemies[i].HP);
                if (dealt > 0 && DamageDealtThisFight.ContainsKey(actor)) DamageDealtThisFight[actor] += dealt;

                int healed = 0;
                for (int i = 0; i < partyHpBefore.Length && i < Party.Count; i++)
                    healed += Math.Max(0, Party[i].HP - partyHpBefore[i]);
                if (healed > 0 && HealingDoneThisFight.ContainsKey(actor)) HealingDoneThisFight[actor] += healed;
            }

            for (int i = 0; i < partyHpBefore.Length && i < Party.Count; i++)
            {
                int taken = Math.Max(0, partyHpBefore[i] - Party[i].HP);
                if (taken > 0 && DamageTakenThisFight.ContainsKey(Party[i])) DamageTakenThisFight[Party[i]] += taken;
            }
        }

        // Compara el HP de todos antes/despues del turno que se acaba de ejecutar: dispara el
        // flash/sacudida de camara y avisa (por indice) que enemigo recibio dano, cayo o se le
        // rompio el aguante, para que la escena de batalla (BattleStageController/EnemyView) anime
        // el golpe, la disolucion de muerte o el flash de ruptura. El motor de combate puro no sabe
        // nada de esto.
        private void ReportHitFeedback(int[] partyHpBefore, int[] enemyHpBefore, bool[] enemyBrokenBefore, bool isSkillHit, Element hitElement, float hitIntensity)
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
                if (hitElement != Element.None) feedback?.OnElementalHit(hitElement);
                OnEnemyDamaged?.Invoke(i);
                OnEnemyElementalHit?.Invoke(i, hitElement, hitIntensity);
                if (isSkillHit) OnEnemySkillHit?.Invoke(i, hitElement, hitIntensity);
                if (!enemyBrokenBefore[i] && Enemies[i].IsBroken)
                    OnEnemyPoiseBroken?.Invoke(i);
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
