using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Combat;

namespace Gameplay
{
    public class CombatHUD : MonoBehaviour
    {
        public CombatManager combatManager;
        public BattleStageController battleStage;

        private ActionType? _pendingType;
        private static Texture2D _whiteTex;
        private bool _wasActive;
        private bool _inspecting;
        private bool _showingAbilities;
        private bool _showingItems;
        private ItemActionKind? _pendingItem;

        // Reveal "estiloso" del menu de accion (inspirado en Persona 5: capas apiladas, acento
        // rojo/negro, entrada en cascada) -- se reinicia cada vez que le toca elegir a alguien nuevo.
        private CharacterStats _actionMenuChooser;
        private float _actionMenuRevealStart;

        // Numeros de dano/curacion flotantes: se detectan comparando el HP visto en el frame
        // anterior contra el actual (asi el motor de combate puro no necesita saber nada de UI).
        private readonly Dictionary<EnemyStats, int> _lastEnemyHp = new Dictionary<EnemyStats, int>();
        private readonly Dictionary<CharacterStats, int> _lastPartyHp = new Dictionary<CharacterStats, int>();
        private readonly List<DamagePopup> _popups = new List<DamagePopup>();
        private const float PopupDuration = 0.9f;

        private struct DamagePopup
        {
            public string Text;
            public Color Color;
            public float StartTime;
            public float X, Y;
            // Corrimiento horizontal fijo, sorteado UNA vez al crearse (ver TrackHpChange): sin
            // esto, un golpe multi-hit o un Ataque en Conjunto que pega a varios a la vez saca
            // varios numeros perfectamente apilados uno arriba del otro, ilegibles.
            public float JitterX;
        }

        private QteManager QteManager => combatManager != null ? combatManager.qteManager : null;

        // Rework del QTE (antes: toda la secuencia mostrada de entrada en el panel lateral, ahora:
        // overlay centrado en pantalla, una sola tecla revelada a la vez -- ver
        // DrawQteCenterOverlay). Este bloque de estado detecta CAMBIOS en QteManager.ProgressIndex/
        // IsActive frame a frame (polling desde Update, no eventos) para disparar el game feel
        // (CombatFeedback) y animar el "pop" del icono actual sin tener que tocar QteManager -- esa
        // clase se mantiene deliberadamente ciega a la UI (ver su comentario de clase).
        private bool _qteWasActive;
        private int _qteLastQteProgress;
        private float _qtePressPopStart = -10f;
        private const float QtePopDuration = 0.22f;
        // Cuanto sigue dibujandose el overlay DESPUES de que QteManager.IsActive pasa a false, para
        // que el jugador alcance a leer el resultado (tilde verde / cruz roja) en vez de que el
        // panel desaparezca en el mismo frame que termina.
        private float _qteResultLingerStart = -10f;
        private bool _qteResultWasSuccess;
        private const float QteResultLingerDuration = 0.4f;

        // Rework del banner del Ataque en Conjunto (ver DrawAllOutAttackCenterOverlay): mismo
        // patron de polling que el QTE de arriba -- detecta cuando AllOutAttackMashCount sube para
        // disparar el "pop" del contador grande.
        private int _lastAllOutMashCount;
        private float _allOutMashPopStart = -10f;
        private const float AllOutMashPopDuration = 0.18f;

        private void UpdateAllOutMashFeedback()
        {
            if (combatManager.AllOutAttackReady && combatManager.AllOutAttackMashCount > _lastAllOutMashCount)
                _allOutMashPopStart = Time.time;
            _lastAllOutMashCount = combatManager.AllOutAttackReady ? combatManager.AllOutAttackMashCount : 0;
        }

        // Poll desde Update (una vez por frame, a diferencia de OnGUI que puede correr varias veces
        // por frame con distintos Event.current) -- compara el ProgressIndex/IsActive de este frame
        // contra el anterior y dispara feedback de camara + el timer del "pop" del icono cuando
        // corresponde. No depende del orden de ejecucion contra QteManager.Update(): lee
        // ProgressIndex/Sequence, que QteManager deja intactos despues de terminar (ver Finish()),
        // asi que el acierto de la ULTIMA tecla se detecta igual sin importar si este Update corre
        // antes o despues del de QteManager en el mismo frame.
        private void UpdateQteFeedback()
        {
            var qte = QteManager;
            if (qte == null) return;
            bool active = qte.IsActive;

            if (active && !_qteWasActive)
                _qteLastQteProgress = 0; // arranca una secuencia nueva (personaje distinto, u otra ronda)

            if (qte.Sequence.Count > 0 && qte.ProgressIndex > _qteLastQteProgress)
            {
                bool wasLast = qte.ProgressIndex >= qte.Sequence.Count;
                combatManager.feedback?.OnQteKeyPress(wasLast);
                _qtePressPopStart = Time.time;
                _qteLastQteProgress = qte.ProgressIndex;
            }

            if (_qteWasActive && !active)
            {
                bool success = qte.Sequence.Count > 0 && qte.ProgressIndex >= qte.Sequence.Count;
                if (!success) combatManager.feedback?.OnQteFail();
                _qteResultWasSuccess = success;
                _qteResultLingerStart = Time.time;
            }

            _qteWasActive = active;
        }

        // Ademas de los botones, varios atajos de teclado -- se chequean en Update (no en OnGUI)
        // para no disparar varias veces por el mismo frame. Espacio hace distintas cosas segun el
        // estado (Ataque en Conjunto listo / resumen de victoria / menu de accion normal), nunca
        // mas de una a la vez porque esos 3 estados son mutuamente excluyentes.
        void Update()
        {
            if (combatManager == null || !combatManager.IsActive) return;

            UpdateQteFeedback();
            UpdateAllOutMashFeedback();

            if (combatManager.AllOutAttackReady && Input.GetKeyDown(KeyCode.Space))
                combatManager.TriggerAllOutAttack();
            else if (combatManager.IsShowingVictorySummary && Input.GetKeyDown(KeyCode.Space))
                combatManager.DismissVictorySummary();
            else if (Input.GetKeyDown(KeyCode.Space) && CanAutoAttackNow())
                combatManager.AutoAttackRemaining();

            // Esc: mismo atajo que el boton "[TEST] Saltar" (ver DungeonManager linea similar para
            // el menu de pausa) -- SkipFightForTesting ya se auto-protege si no corresponde
            // (!IsActive || IsResolvingRound), asi que no hace falta duplicar esa condicion aca.
            if (Input.GetKeyDown(KeyCode.Escape))
                combatManager.SkipFightForTesting();
        }

        // Espacio hace "Auto" (mismo que el boton) SOLO parado en el menu de acciones de nivel
        // superior (Atacar/Habilidades/Guardia/Auto, ver el else-if de mas abajo en OnGUI) de
        // alguien que todavia no eligio esta ronda -- nunca durante un QTE, mientras se resuelve
        // la ronda, ni con ningun submenu (Habilidades/Items) ya abierto.
        private bool CanAutoAttackNow()
        {
            if (combatManager.AllOutAttackReady || combatManager.IsShowingVictorySummary || combatManager.IsResolvingRound) return false;
            if (QteManager != null && QteManager.IsActive) return false;
            if (_pendingType != null || _showingAbilities || _showingItems || _pendingItem.HasValue) return false;
            return combatManager.GetChooser() != null;
        }

        void OnGUI()
        {
            if (combatManager == null || !combatManager.IsActive)
            {
                _pendingType = null;
                _wasActive = false;
                _showingAbilities = false;
                _showingItems = false;
                _pendingItem = null;
                _actionMenuChooser = null;
                return;
            }

            if (!_wasActive)
            {
                // Arranca un combate nuevo: se limpia el historial de HP para no generar popups
                // falsos comparando contra enemigos/estado de una pelea anterior.
                _lastEnemyHp.Clear();
                _lastPartyHp.Clear();
                _popups.Clear();
                _wasActive = true;
            }

            // Resumen de victoria: se dibuja SOLO esto (nada del panel normal de abajo) hasta que
            // el jugador confirma con "Continuar" -- ver CombatManager.DismissVictorySummary.
            if (combatManager.IsShowingVictorySummary)
            {
                DrawVictorySummary();
                if (battleStage != null && battleStage.IsForestCombat) UIButton.DrawForestEffects();
                return;
            }

            // El panel se ancla abajo (deja el resto de la pantalla, arriba, libre para que se vea
            // la escena de batalla 3D con los enemigos al fondo, en vez de tapar toda la pantalla),
            // pero nunca mas chico que el contenido minimo ni mas grande de lo necesario.
            float panelH = Mathf.Clamp(Screen.height - 40f, 440f, 560f);
            float panelY = Screen.height - panelH - 10f;
            float panelX = 10f;
            float panelW = Screen.width - 20f;
            GUI.Box(new Rect(panelX, panelY, panelW, panelH), "");
            GUI.Label(new Rect(panelX + 10, panelY + 5, panelW - 350, 24), combatManager.IsBossFight ? "COMBATE DE JEFE" : "COMBATE");

            if (DrawBattleButton(new Rect(panelX + panelW - 330, panelY + 4, 100, 24), $"Huir ({combatManager.FleeChancePercent:F0}%)", enabled: !combatManager.IsBossFight))
                combatManager.TryFlee();
            if (DrawBattleButton(new Rect(panelX + panelW - 220, panelY + 4, 100, 24), "Rendirse"))
                combatManager.Surrender();
            if (DrawBattleButton(new Rect(panelX + panelW - 110, panelY + 4, 100, 24), "[TEST] Saltar"))
                combatManager.SkipFightForTesting();

            float y = panelY + 32;

            // --- Enemigos ---
            GUI.Label(new Rect(panelX + 10, y, 200, 20), "Enemigos:");
            if (DrawBattleButton(new Rect(panelX + 200, y - 2, 150, 22), _inspecting ? "Ocultar debilidades" : "Inspeccionar"))
                _inspecting = !_inspecting;
            y += 22;
            foreach (var enemy in combatManager.Enemies)
            {
                bool isTurn = combatManager.IsResolvingRound && !combatManager.CurrentTurnIsParty && combatManager.CurrentTurnActorName == enemy.Name;
                string status = enemy.IsAlive ? StatusTags(enemy) : " - derrotado";
                DrawTurnLine(panelX + 20, y, panelW - 40, $"{enemy.Name}{status}", isTurn, isEnemyTurn: true);

                // Barra de vida con textura (ver Gameplay/HealthBarWidget) en vez del texto "HP
                // X/Y" de antes -- showText:true porque el numero exacto sigue importando para
                // decisiones tacticas (a quien rematar, etc.), a diferencia de la barra flotante
                // sobre el enemigo en la escena 3D (EnemyHealthBarHUD), que es a proposito muda.
                if (enemy.IsAlive)
                    HealthBarWidget.Draw(new Rect(panelX + 260, y + 2, 150, 16), (enemy, BarKind.Hp), enemy.HP, enemy.MaxHP, BarKind.Hp, showText: true);

                string weaknessLabel = enemy.Weaknesses != null && enemy.Weaknesses.Length > 0
                    ? string.Join("/", enemy.Weaknesses.Select(ElementLabel))
                    : ElementLabel(Element.None);
                if (_inspecting)
                {
                    string inspect = $"Débil: {weaknessLabel}  Resiste: {ElementLabel(enemy.Resistance)}  DEF {enemy.Defense}  VEL {enemy.Speed}";
                    GUI.Label(new Rect(panelX + 425, y, panelW - 445, 20), inspect);
                }

                var (popupX, popupY) = EnemyPopupPosition(combatManager.Enemies.IndexOf(enemy), panelX + panelW - 60, y);
                TrackHpChange(_lastEnemyHp, enemy, enemy.HP, popupX, popupY);
                y += 20;
            }

            y += 12;

            // --- Party ---
            GUI.Label(new Rect(panelX + 10, y, 300, 20), "Party:");
            y += 22;
            foreach (var member in combatManager.Party)
            {
                bool isTurn = combatManager.IsResolvingRound && combatManager.CurrentTurnIsParty && combatManager.CurrentTurnActorName == member.Name;
                string status = !member.IsAlive ? "caído" : member.IsProtectingAll ? "protegiendo al grupo" : member.IsGuarding ? "en guardia" : "listo";
                string row = member.IsFrontRow ? "frente" : "fondo";
                DrawTurnLine(panelX + 20, y, panelW - 40, $"{member.Name} ({member.Class}, {row})  [{status}]", isTurn, isEnemyTurn: false);

                // HP y TP como barras con textura (ver Gameplay/HealthBarWidget) en vez del texto
                // "HP X/Y TP X/Y" de antes -- pedido puntual. showText:true, el numero exacto sigue
                // ahi, solo que superpuesto a la barra en vez de ser todo el contenido.
                if (member.IsAlive)
                {
                    // Clave compuesta (member, kind): member SOLO no alcanza -- HP y TP del MISMO
                    // personaje son dos barras distintas, con su propio trail cada una. Sin el
                    // kind ahi, la barra de TP pisaria el estado de animacion de la de HP y
                    // viceversa (misma key, misma entrada de diccionario).
                    HealthBarWidget.Draw(new Rect(panelX + 300, y + 2, 120, 16), (member, BarKind.Hp), member.HP, member.MaxHP, BarKind.Hp, showText: true);
                    HealthBarWidget.Draw(new Rect(panelX + 430, y + 2, 90, 16), (member, BarKind.Tp), member.TP, member.MaxTP, BarKind.Tp, showText: true);
                }

                TrackHpChange(_lastPartyHp, member, member.HP, panelX + panelW - 60, y);
                y += 20;
            }

            y += 12;

            if (combatManager.AllOutAttackReady)
            {
                // El banner en si ya NO se dibuja aca (ver DrawAllOutAttackCenterOverlay, llamado
                // aparte al final de OnGUI, mismo criterio que el QTE): esta linea solo ocupa el
                // lugar del menu de accion normal mientras dura la ventana de machacado.
                GUI.Label(new Rect(panelX + 10, y, panelW - 20, 20), "¡Mirá el centro de la pantalla!");
                y += 24;
            }
            else if (QteManager != null && QteManager.IsActive)
            {
                // La secuencia en si ya NO se dibuja aca (ver DrawQteCenterOverlay, llamado aparte
                // al final de OnGUI): esta linea solo ocupa el lugar del menu de accion normal
                // mientras dura el QTE, para que no aparezcan botones de Atacar/Habilidades
                // superpuestos debajo del overlay centrado.
                GUI.Label(new Rect(panelX + 10, y, panelW - 20, 20), "¡Mirá el centro de la pantalla!");
                y += 24;
            }
            else if (combatManager.IsResolvingRound)
            {
                string who = combatManager.CurrentTurnIsParty ? "un aliado" : "un enemigo";
                var oldColor = GUI.color;
                GUI.color = combatManager.CurrentTurnIsParty ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.4f, 0.4f);
                GUI.Label(new Rect(panelX + 10, y, panelW - 20, 24), $"► Turno de {combatManager.CurrentTurnActorName} ({who})");
                GUI.color = oldColor;
                y += 28;
            }
            else
            {
                var chooser = combatManager.GetChooser();
                if (chooser != null)
                {
                    GUI.Label(new Rect(panelX + 10, y, panelW - 130, 20), $"Turno de {chooser.Name}:");
                    if (DrawBattleButton(new Rect(panelX + panelW - 120, y - 2, 120, 22), "◄ Volver", enabled: combatManager.CanGoBack))
                    {
                        combatManager.GoToPreviousChooser();
                        _pendingType = null;
                        _showingAbilities = false;
                    }
                    y += 24;

                    // Gunner: selector de bala cargada, arriba de todo el resto del menu de
                    // acciones (Atacar/Habilidades ya usan la que este cargada, ver
                    // CombatEngine.ResolveElement). Ocupa su propia fila solo para esta clase.
                    if (chooser.Class == CharacterClass.Gunner)
                    {
                        DrawGunnerWeaponModeSelector(panelX, y, chooser);
                        y += 30;
                        DrawGunnerBulletSelector(panelX, y, chooser);
                        y += 30;
                    }

                    // Ranger: mismo criterio que el selector de bala del Gunner de arriba, pero
                    // para flechas de estado (ver CombatEngine.ApplyLoadedArrowStatus) -- Atacar Y
                    // Ataque Cruzado usan igual la que este cargada.
                    if (chooser.Class == CharacterClass.Ranger)
                    {
                        DrawRangerArrowSelector(panelX, y, chooser);
                        y += 30;
                    }

                    if (_showingAbilities)
                    {
                        // Mismo lugar que el boton "Habilidades" (mover lo menos posible): solo las
                        // habilidades de ESTE personaje, cada una con su costo de TP y su boton.
                        string skillLabel = chooser.IsHealSkill
                            ? $"{chooser.SkillName} - cura {chooser.HealAmount + chooser.MagicAttack / 2} HP ({chooser.SkillTpCost} TP)"
                            : chooser.IsSelfStanceSkill
                                ? $"{chooser.SkillName} - postura propia, {(chooser.IsEnraged ? "desactivar" : "activar")} ({chooser.SkillTpCost} TP)"
                                : chooser.IsVersatileBuffSkill
                                    ? $"{chooser.SkillName} - buffea aliado / debuffea enemigo ({chooser.SkillTpCost} TP)"
                                    : chooser.SkillIsAoe
                                        ? $"{chooser.SkillName} - {ElementLabel(chooser.SkillElement)} x{chooser.SkillPower:F1} a TODOS ({chooser.SkillTpCost} TP)"
                                        : chooser.SkillHitsEnemyFrontRow
                                            ? $"{chooser.SkillName} - {ElementLabel(chooser.SkillElement)} x{chooser.SkillPower:F1} a la fila delantera ({chooser.SkillTpCost} TP)"
                                            : $"{chooser.SkillName} - {ElementLabel(chooser.SkillElement)} x{chooser.SkillPower:F1} ({chooser.SkillTpCost} TP)";
                        if (DrawBattleButton(new Rect(panelX + 20, y, 340, 26), skillLabel))
                        {
                            _showingAbilities = false;
                            if (chooser.IsSelfStanceSkill)
                                combatManager.SubmitAction(new PartyAction { Actor = chooser, Type = ActionType.Skill });
                            else if (chooser.SkillIsAoe || chooser.SkillHitsEnemyFrontRow)
                                combatManager.SubmitAction(new PartyAction { Actor = chooser, Type = ActionType.Skill });
                            else
                                _pendingType = ActionType.Skill;
                        }

                        if (chooser.CanProtectAll)
                        {
                            string protectLabel = $"Proteger a todos ({CombatEngine.ProtectAllTpCost} TP)";
                            if (DrawBattleButton(new Rect(panelX + 370, y, 220, 26), protectLabel))
                            {
                                combatManager.SubmitAction(new PartyAction { Actor = chooser, Type = ActionType.ProtectAll });
                                _showingAbilities = false;
                            }
                        }

                        if (DrawBattleButton(new Rect(panelX + 600, y, 100, 26), "Cerrar"))
                            _showingAbilities = false;
                    }
                    else if (_pendingType == null && !_showingItems && !_pendingItem.HasValue)
                    {
                        TrackActionMenuReveal(chooser);

                        if (DrawP5Button(new Rect(panelX + 20, y, 120, 30), "Atacar", 0))
                        {
                            // El ataque basico SIEMPRE pide objetivo, para toda clase -- a diferencia
                            // de la habilidad, que si puede ser AoE (ver chooser.SkillIsAoe arriba).
                            _pendingType = ActionType.Attack;
                        }

                        if (DrawP5Button(new Rect(panelX + 150, y, 150, 30), "Habilidades", 1))
                            _showingAbilities = true;

                        if (DrawP5Button(new Rect(panelX + 310, y, 120, 30), "Guardia", 2))
                        {
                            combatManager.SubmitAction(new PartyAction { Actor = chooser, Type = ActionType.Guard });
                            _pendingType = null;
                        }

                        if (DrawP5Button(new Rect(panelX + 440, y, 170, 30), "Auto", 3))
                        {
                            combatManager.AutoAttackRemaining();
                            _pendingType = null;
                        }

                        if (DrawP5Button(new Rect(panelX + 620, y, 120, 30), "Ítems", 4))
                            _showingItems = true;
                    }
                    else if (_showingItems)
                    {
                        GUI.Label(new Rect(panelX + 20, y, 300, 20), "Elegí un ítem:");
                        y += 22;
                        string potionLabel = $"Poción ({combatManager.PotionCharges}) - cura {CombatEngine.PotionHealAmount} HP";
                        if (DrawBattleButton(new Rect(panelX + 20, y, 260, 26), potionLabel, enabled: combatManager.PotionCharges > 0))
                        {
                            _showingItems = false;
                            _pendingItem = ItemActionKind.Potion;
                        }
                        string reviverLabel = $"Revivir ({combatManager.ReviverCharges}) - devuelve a un caído";
                        if (DrawBattleButton(new Rect(panelX + 290, y, 260, 26), reviverLabel, enabled: combatManager.ReviverCharges > 0))
                        {
                            _showingItems = false;
                            _pendingItem = ItemActionKind.Reviver;
                        }
                        if (DrawBattleButton(new Rect(panelX + 560, y, 100, 26), "Cerrar")) _showingItems = false;
                    }
                    else if (_pendingItem.HasValue)
                    {
                        bool wantsAlive = _pendingItem.Value == ItemActionKind.Potion;
                        GUI.Label(new Rect(panelX + 20, y, 340, 20), wantsAlive ? "Elegí a quién curar:" : "Elegí a quién revivir:");
                        y += 22;
                        float bx = panelX + 20;
                        foreach (var ally in combatManager.Party.Where(p => p.IsAlive == wantsAlive))
                        {
                            if (DrawBattleButton(new Rect(bx, y, 150, 26), ally.Name))
                            {
                                combatManager.SubmitItemAction(chooser, _pendingItem.Value, combatManager.Party.IndexOf(ally));
                                _pendingItem = null;
                            }
                            bx += 160;
                        }
                        if (DrawBattleButton(new Rect(panelX + 20, y + 34, 100, 24), "Cancelar")) _pendingItem = null;
                    }
                    else if (_pendingType == ActionType.Skill && chooser.IsHealSkill)
                    {
                        GUI.Label(new Rect(panelX + 20, y, 300, 20), "Elegí a quién curar (o revivir, si está caído):");
                        y += 22;
                        float bx = panelX + 20;
                        foreach (var ally in combatManager.Party)
                        {
                            if (DrawBattleButton(new Rect(bx, y, 150, 26), ally.IsAlive ? ally.Name : $"{ally.Name} (caído)"))
                            {
                                var action = new PartyAction { Actor = chooser, Type = ActionType.Skill, TargetAllyIndex = combatManager.Party.IndexOf(ally) };
                                combatManager.SubmitAction(action);
                                _pendingType = null;
                            }
                            bx += 160;
                        }
                        if (DrawBattleButton(new Rect(panelX + 20, y + 34, 100, 24), "Cancelar")) _pendingType = null;
                    }
                    else if (_pendingType == ActionType.Skill && chooser.IsVersatileBuffSkill)
                    {
                        // Trovador: se puede tirar sobre CUALQUIER aliado (buff) o enemigo (debuff)
                        // -- ambas listas juntas, ver PartyAction.TargetIsAlly para distinguir cual.
                        GUI.Label(new Rect(panelX + 20, y, 400, 20), "Elegí a quién buffear (aliado) o debuffear (enemigo):");
                        y += 22;
                        float bx = panelX + 20;
                        foreach (var ally in combatManager.Party.Where(p => p.IsAlive))
                        {
                            if (DrawBattleButton(new Rect(bx, y, 150, 26), $"{ally.Name} (buff)"))
                            {
                                var action = new PartyAction { Actor = chooser, Type = ActionType.Skill, TargetIsAlly = true, TargetAllyIndex = combatManager.Party.IndexOf(ally) };
                                combatManager.SubmitAction(action);
                                _pendingType = null;
                            }
                            bx += 160;
                        }
                        y += 30;
                        bx = panelX + 20;
                        foreach (var enemy in combatManager.Enemies.Where(e => e.IsAlive))
                        {
                            int idx = combatManager.Enemies.IndexOf(enemy);
                            if (DrawBattleButton(new Rect(bx, y, 150, 26), $"{enemy.Name} (debuff)"))
                            {
                                var action = new PartyAction { Actor = chooser, Type = ActionType.Skill, TargetIsAlly = false, TargetEnemyIndex = idx };
                                combatManager.SubmitAction(action);
                                _pendingType = null;
                            }
                            bx += 160;
                        }
                        if (DrawBattleButton(new Rect(panelX + 20, y + 34, 100, 24), "Cancelar")) _pendingType = null;
                    }
                    else if (_pendingType.HasValue)
                    {
                        GUI.Label(new Rect(panelX + 20, y, 300, 20), "Elegí un objetivo:");
                        y += 22;
                        // Hasta 3 enemigos vivos a la vez podian entrar en una sola fila; con el
                        // Slime pudiendo dividirse hasta 6 a la vez (CombatEngine.MaxEnemies), sin
                        // esto la fila se salia de la pantalla y los ultimos objetivos quedaban
                        // inalcanzables. Envuelve a una fila nueva cada 3 botones.
                        const int perRow = 3;
                        float bx = panelX + 20;
                        int col = 0;
                        foreach (var enemy in combatManager.Enemies.Where(e => e.IsAlive))
                        {
                            int idx = combatManager.Enemies.IndexOf(enemy);
                            if (DrawBattleButton(new Rect(bx, y, 180, 26), enemy.Name))
                            {
                                var action = new PartyAction { Actor = chooser, Type = _pendingType.Value, TargetEnemyIndex = idx };
                                combatManager.SubmitAction(action);
                                _pendingType = null;
                            }
                            col++;
                            if (col >= perRow) { col = 0; bx = panelX + 20; y += 30; }
                            else bx += 190;
                        }
                        if (col != 0) y += 30;
                        if (DrawBattleButton(new Rect(panelX + 20, y + 4, 100, 24), "Cancelar")) _pendingType = null;
                    }
                }
                else
                {
                    GUI.Label(new Rect(panelX + 10, y, panelW - 20, 20), "Resolviendo ronda...");
                }
            }

            // --- Log de combate (ultimas lineas; un poco mas grande que antes) ---
            const float logH = 140f;
            float logY = panelY + panelH - logH;
            GUI.Box(new Rect(panelX + 10, logY, panelW - 20, logH), "");
            var lastLines = combatManager.Log.Skip(Mathf.Max(0, combatManager.Log.Count - 7));
            float ly = logY + 6;
            foreach (var line in lastLines)
            {
                GUI.Label(new Rect(panelX + 18, ly, panelW - 36, 18), line);
                ly += 18;
            }

            DrawDamagePopups();
            if (battleStage != null && battleStage.IsForestCombat) UIButton.DrawForestEffects();
            DrawQteCenterOverlay();
            DrawAllOutAttackCenterOverlay();
        }

        // Compara el HP actual contra el ultimo visto para ese combatiente; si bajo o subio,
        // crea un numero flotante (rojo = dano, verde = curacion) en la posicion de su linea.
        // Centra el numero flotante sobre la representacion 3D real del enemigo (proyectando su
        // TopAnchor a coordenadas de pantalla con la camara de batalla, igual que EnemyHealthBarHUD),
        // en vez de dejarlo pegado a la fila de texto del panel de abajo. Si todavia no hay camara o
        // vista (p.ej. el primer frame antes de que cargue la escena de batalla), cae de nuevo a la
        // posicion del panel como antes.
        private (float x, float y) EnemyPopupPosition(int enemyIndex, float fallbackX, float fallbackY)
        {
            var cam = battleStage != null ? battleStage.ActiveBattleCamera : null;
            var view = battleStage != null && enemyIndex >= 0 ? battleStage.GetEnemyView(enemyIndex) : null;
            if (cam == null || !cam.enabled || view == null) return (fallbackX, fallbackY);

            // WorldToScreenPointStable (no WorldToScreenPoint a secas): la camara de batalla
            // tiembla en CombatFeedback.LateUpdate ANTES de que este OnGUI corra este frame, asi
            // que proyectar con la posicion ya temblando tira el numero a cualquier lado del
            // enemigo real -- ver el comentario en CombatFeedback.WorldToScreenPointStable.
            Vector3 screenPos = CombatFeedback.WorldToScreenPointStable(cam, view.TopAnchor);
            if (screenPos.z <= 0f) return (fallbackX, fallbackY);

            return (screenPos.x, Screen.height - screenPos.y - 18f);
        }

        private void TrackHpChange<T>(Dictionary<T, int> lastHp, T key, int currentHp, float x, float y) where T : class
        {
            if (lastHp.TryGetValue(key, out int previous) && previous != currentHp)
            {
                int delta = currentHp - previous;
                var color = delta < 0 ? new Color(1f, 0.3f, 0.3f) : new Color(0.4f, 1f, 0.5f);
                string text = delta < 0 ? delta.ToString() : $"+{delta}";
                float jitter = Random.Range(-14f, 14f);
                _popups.Add(new DamagePopup { Text = text, Color = color, StartTime = Time.time, X = x, Y = y, JitterX = jitter });
            }
            lastHp[key] = currentHp;
        }

        private static GUIStyle _popupStyle;

        // Duracion del "pop" de entrada (ver EaseOutBack mas abajo) -- corto a proposito, el golpe
        // ya pasó, esto es solo el numero reaccionando de inmediato a el.
        private const float PopDuration = 0.14f;

        private void DrawDamagePopups()
        {
            if (_popupStyle == null)
            {
                _popupStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 18,
                };
            }

            const float w = 90f, h = 26f;
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                float age = Time.time - _popups[i].StartTime;
                if (age >= PopupDuration) { _popups.RemoveAt(i); continue; }

                float frac = age / PopupDuration;
                var popup = _popups[i];
                _popupStyle.normal.textColor = new Color(popup.Color.r, popup.Color.g, popup.Color.b, 1f - frac * frac);

                // Pop de entrada con sobre-elongacion (EaseOutBack): el numero nace mas grande de
                // lo que va a quedar y "rebota" hasta su tamano final en PopDuration segundos --
                // esto es lo que lee como "juicy" en vez de aparecer/desaparecer parejo. Despues
                // del pop, se achica un poco de nuevo hacia el final (settle) para que el fade no
                // se sienta como que el numero se queda pegado del mismo tamano todo el rato.
                float popT = Mathf.Clamp01(age / PopDuration);
                float scale = Mathf.Max(0.05f, EaseOutBack(popT) * Mathf.Lerp(1f, 0.85f, frac));

                // Sube con ease-out (rapido al salir, se frena) en vez de velocidad constante --
                // se siente mas como un impacto real que un ascensor. JitterX separa golpes
                // simultaneos (multi-hit, Ataque en Conjunto) para que no queden apilados.
                float riseY = 30f * (1f - (1f - frac) * (1f - frac));
                float cx = popup.X + popup.JitterX;
                float cy = popup.Y - riseY;

                var oldMatrix = GUI.matrix;
                GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), new Vector2(cx, cy));
                GUI.Label(new Rect(cx - w / 2f, cy - h / 2f, w, h), popup.Text, _popupStyle);
                GUI.matrix = oldMatrix;
            }
        }

        // Clasica curva "back ease out" (Robert Penner): pasa de 0 a 1 pero se pasa de largo hasta
        // ~1.1 antes de asentarse -- el "rebote" del pop. c1/c3 son las constantes estandar de esta
        // familia de curvas, no valores ajustados a mano.
        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        // Gunner: que arma tiene puesta (ver CharacterStats.WeaponMode / CombatEngine.
        // ResolveClassAwareHit) -- Rifle pega colateral en cruz (misma fila y columna del
        // objetivo) con descuento por objetivo extra, Shotgun pega mas fuerte pero solo al
        // objetivo elegido. Independiente de la bala elemental cargada (selector de abajo).
        private void DrawGunnerWeaponModeSelector(float panelX, float y, CharacterStats gunner)
        {
            (GunnerWeaponMode mode, string label)[] options =
            {
                (GunnerWeaponMode.Rifle, "Rifle (colateral fila+col)"),
                (GunnerWeaponMode.Shotgun, "Escopeta (x1.4, 1 objetivo)"),
            };

            float bx = panelX + 20;
            foreach (var (mode, label) in options)
            {
                bool active = gunner.WeaponMode == mode;
                var old = GUI.color;
                if (active) GUI.color = new Color(1f, 0.85f, 0.3f);
                if (DrawBattleButton(new Rect(bx, y, 220, 24), label))
                    gunner.WeaponMode = mode;
                GUI.color = old;
                bx += 226;
            }
        }

        // Gunner: fila de botones para cargar una bala elemental (o volver a las normales). El
        // elemento cargado se guarda directo en CharacterStats.LoadedBulletElement -- CombatEngine
        // lo lee al resolver el ataque basico Y la habilidad (ver ResolveElement), y gasta 1 bala
        // de stock cada vez que de verdad pega con el.
        private void DrawGunnerBulletSelector(float panelX, float y, CharacterStats gunner)
        {
            (Element element, string label, int stock)[] options =
            {
                (Element.None, "Normal", -1),
                (Element.Fire, "Fuego", gunner.FireBullets),
                (Element.Ice, "Hielo", gunner.IceBullets),
                (Element.Volt, "Rayo", gunner.VoltBullets),
            };

            float bx = panelX + 20;
            foreach (var (element, label, stock) in options)
            {
                bool loaded = gunner.LoadedBulletElement == element;
                bool enabled = element == Element.None || stock > 0;
                string text = element == Element.None ? label : $"{label} ({stock})";
                var old = GUI.color;
                if (loaded) GUI.color = new Color(1f, 0.85f, 0.3f);
                if (DrawBattleButton(new Rect(bx, y, 130, 24), text, enabled: enabled))
                    gunner.LoadedBulletElement = element;
                GUI.color = old;
                bx += 136;
            }
        }

        // Ranger: fila de botones para cargar una flecha de estado (o volver a las normales), mismo
        // criterio que DrawGunnerBulletSelector de arriba. El tipo cargado se guarda directo en
        // CharacterStats.LoadedArrowType -- CombatEngine lo lee al resolver CUALQUIER golpe que
        // conecte (ataque basico Y habilidad, ver ApplyLoadedArrowStatus), y gasta 1 flecha de
        // stock cada vez que de verdad se usa.
        private void DrawRangerArrowSelector(float panelX, float y, CharacterStats ranger)
        {
            (RangerArrowType type, string label, int stock)[] options =
            {
                (RangerArrowType.None, "Normal", -1),
                (RangerArrowType.Poison, "Veneno", ranger.PoisonArrows),
                (RangerArrowType.Paralysis, "Parálisis", ranger.ParalysisArrows),
                (RangerArrowType.Bleed, "Sangrado", ranger.BleedArrows),
            };

            float bx = panelX + 20;
            foreach (var (type, label, stock) in options)
            {
                bool loaded = ranger.LoadedArrowType == type;
                bool enabled = type == RangerArrowType.None || stock > 0;
                string text = type == RangerArrowType.None ? label : $"{label} ({stock})";
                var old = GUI.color;
                if (loaded) GUI.color = new Color(1f, 0.85f, 0.3f);
                if (DrawBattleButton(new Rect(bx, y, 130, 24), text, enabled: enabled))
                    ranger.LoadedArrowType = type;
                GUI.color = old;
                bx += 136;
            }
        }

        // Etiquetas cortas de estado para la fila de un enemigo vivo (ver DrawTurnLine mas arriba)
        // -- IsBroken ya existia, ahora se suman Paralizado/Sangrando/Veneno (ver EnemyStats,
        // CombatEngine.ApplyLoadedArrowStatus/ExecuteEnemyAction) para que el jugador vea de un
        // vistazo que estados tiene encima cada uno, sin tener que inspeccionar nada mas.
        private string StatusTags(EnemyStats enemy)
        {
            var tags = new List<string>();
            if (enemy.IsBroken) tags.Add("ROTO: pierde su turno");
            if (enemy.IsParalyzed) tags.Add("paralizado");
            if (enemy.IsVulnerable) tags.Add("sangrando");
            if (enemy.DotRoundsLeft > 0) tags.Add(string.IsNullOrEmpty(enemy.DotLabel) ? "veneno" : enemy.DotLabel.ToLowerInvariant());
            return tags.Count > 0 ? $" [{string.Join(", ", tags)}]" : "";
        }

        private string ElementLabel(Element element)
        {
            switch (element)
            {
                case Element.Fire: return "Fuego";
                case Element.Ice: return "Hielo";
                case Element.Volt: return "Rayo";
                case Element.Slash: return "Corte";
                case Element.Strike: return "Golpe";
                case Element.Pierce: return "Perforación";
                default: return "Ninguno";
            }
        }

        // Rework del banner del Ataque en Conjunto (antes: banner angosto en el panel lateral, ver
        // el commit anterior). Ahora vive en el CENTRO de pantalla, mismo criterio que
        // DrawQteCenterOverlay: dos barras separadas (tiempo restante de la ventana vs. cuanto se
        // "cargo" machacando) y un contador grande que hace un "pop" con rebote cada vez que sube
        // (EaseOutBack, ver UpdateAllOutMashFeedback).
        private void DrawAllOutAttackCenterOverlay()
        {
            if (!combatManager.AllOutAttackReady) return;

            int mashCount = combatManager.AllOutAttackMashCount;
            float mashFrac = Mathf.Clamp01(mashCount / (float)CombatEngine.AllOutMaxPresses);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (8f + mashFrac * 10f));
            Color glow = Color.Lerp(new Color(0.85f, 0.65f, 0.1f), new Color(1f, 0.95f, 0.55f), Mathf.Max(pulse, mashFrac));

            float cx = Screen.width * 0.5f;
            const float boxW = 420f, boxH = 168f;
            float boxY = Screen.height * 0.5f - boxH - 30f;
            var boxRect = new Rect(cx - boxW * 0.5f, boxY, boxW, boxH);

            DrawRect(new Rect(boxRect.x - 4, boxRect.y - 4, boxRect.width + 8, boxRect.height + 8), new Color(0f, 0f, 0f, 0.45f));
            DrawRect(boxRect, new Color(0.08f, 0.06f, 0.02f, 0.92f));
            DrawRect(new Rect(boxRect.x, boxRect.y, boxRect.width, 4), glow);
            DrawRect(new Rect(boxRect.x, boxRect.y + boxRect.height - 4, boxRect.width, 4), glow);

            var oldColor = GUI.color;
            var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = glow;
            GUI.Label(new Rect(boxRect.x, boxRect.y + 10, boxRect.width, 32), "¡AGUANTE ROTO!", titleStyle);
            GUI.color = oldColor;

            // --- Barra de tiempo restante: la ventana se cierra sola, no depende de cuanto machaques ---
            float barW = boxRect.width - 60, barH = 10;
            var timeBarRect = new Rect(cx - barW * 0.5f, boxRect.y + 48, barW, barH);
            DrawRect(timeBarRect, new Color(0.2f, 0.2f, 0.22f));
            float timeFrac = combatManager.allOutAttackMashWindow > 0f
                ? Mathf.Clamp01(combatManager.AllOutAttackTimeRemaining / combatManager.allOutAttackMashWindow) : 0f;
            DrawRect(new Rect(timeBarRect.x, timeBarRect.y, timeBarRect.width * timeFrac, barH), new Color(0.9f, 0.3f, 0.2f));

            // --- Barra de carga: cuanto se "machaco" hasta ahora, hasta el tope ---
            var chargeBarRect = new Rect(cx - barW * 0.5f, timeBarRect.y + barH + 8, barW, barH);
            DrawRect(chargeBarRect, new Color(0.2f, 0.2f, 0.22f));
            DrawRect(new Rect(chargeBarRect.x, chargeBarRect.y, chargeBarRect.width * mashFrac, barH), glow);

            // --- Contador de golpes: pop con rebote cada vez que sube (ver UpdateAllOutMashFeedback) ---
            float popT = Mathf.Clamp01((Time.time - _allOutMashPopStart) / AllOutMashPopDuration);
            float scale = Mathf.Max(0.05f, EaseOutBack(popT));
            var countRect = new Rect(cx - 60f, chargeBarRect.y + barH + 8, 120f, 44f);
            var savedMatrix = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), countRect.center);
            var countStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            countStyle.normal.textColor = mashCount > 0 ? glow : new Color(0.6f, 0.6f, 0.6f);
            GUI.Label(countRect, mashCount > 0 ? $"x{mashCount}" : "—", countStyle);
            GUI.matrix = savedMatrix;

            string hint = mashCount > 0 ? "¡SEGUÍ MACHACANDO! (Espacio)" : "¡MACHACÁ Espacio para el Ataque en Conjunto!";
            var hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            hintStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            GUI.Label(new Rect(boxRect.x, countRect.y + countRect.height, boxRect.width, 18), hint, hintStyle);

            if (DrawBattleButton(new Rect(cx - 130f, boxRect.y + boxRect.height + 10f, 260f, 34f), "¡ATAQUE EN CONJUNTO!", accentColor: glow))
                combatManager.TriggerAllOutAttack();
        }

        // Resumen de la pelea que se acaba de ganar: quien hizo mas dano, quien curo, quien
        // recibio mas golpes -- inspirado en juegos que muestran esta info al final de cada
        // combate (no solo al final de la run) para que el jugador entienda que funciono y pueda
        // ajustar formacion/objetivos la proxima vez, en vez de un simple mensaje de "Victoria"
        // que desaparece sin dejar nada util.
        private void DrawVictorySummary()
        {
            float panelW = Mathf.Min(Screen.width - 80f, 640f);
            float panelH = Mathf.Min(Screen.height - 80f, 60f + combatManager.Party.Count * 30f + 150f);
            float panelX = (Screen.width - panelW) / 2f;
            float panelY = (Screen.height - panelH) / 2f;

            DrawRect(new Rect(panelX, panelY, panelW, panelH), new Color(0.06f, 0.06f, 0.08f, 0.97f));
            DrawRect(new Rect(panelX, panelY, panelW, 4), new Color(0.85f, 0.7f, 0.15f));

            var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = new Color(0.95f, 0.85f, 0.4f);
            GUI.Label(new Rect(panelX, panelY + 10, panelW, 32), combatManager.IsBossFight ? "¡VICTORIA DE JEFE!" : "¡VICTORIA!", titleStyle);

            var defeated = combatManager.Enemies.Where(e => !e.IsAlive).Select(e => e.Name).ToList();
            GUI.Label(new Rect(panelX + 20, panelY + 46, panelW - 40, 20), $"Derrotaste a: {string.Join(", ", defeated)}");

            float y = panelY + 76;
            var headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(panelX + 20, y, 200, 20), "Personaje", headerStyle);
            GUI.Label(new Rect(panelX + 260, y, 120, 20), "Daño hecho", headerStyle);
            GUI.Label(new Rect(panelX + 390, y, 120, 20), "Curación", headerStyle);
            GUI.Label(new Rect(panelX + 510, y, 110, 20), "Daño recibido", headerStyle);
            y += 24;

            foreach (var member in combatManager.Party)
            {
                int dealt = combatManager.DamageDealtThisFight.TryGetValue(member, out var d) ? d : 0;
                int healed = combatManager.HealingDoneThisFight.TryGetValue(member, out var h) ? h : 0;
                int taken = combatManager.DamageTakenThisFight.TryGetValue(member, out var t) ? t : 0;
                GUI.Label(new Rect(panelX + 20, y, 230, 22), member.IsAlive ? member.Name : $"{member.Name} (caído)");
                GUI.Label(new Rect(panelX + 260, y, 120, 22), dealt.ToString());
                GUI.Label(new Rect(panelX + 390, y, 120, 22), healed > 0 ? $"+{healed}" : "-");
                GUI.Label(new Rect(panelX + 510, y, 110, 22), taken.ToString());
                y += 26;
            }

            if (combatManager.AllOutAttackDamageThisFight > 0)
            {
                y += 6;
                var goldStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                goldStyle.normal.textColor = new Color(0.95f, 0.85f, 0.4f);
                GUI.Label(new Rect(panelX + 20, y, panelW - 40, 22), $"Ataque en Conjunto: {combatManager.AllOutAttackDamageThisFight} de daño total", goldStyle);
                y += 26;
            }

            if (DrawBattleButton(new Rect(panelX + panelW - 180, panelY + panelH - 44, 160, 34), "Continuar"))
                combatManager.DismissVictorySummary();
        }

        private void TrackActionMenuReveal(CharacterStats chooser)
        {
            if (chooser == _actionMenuChooser) return;
            _actionMenuChooser = chooser;
            _actionMenuRevealStart = Time.time;
        }

        private bool DrawBattleButton(Rect rect, string label, bool enabled = true, Color? accentColor = null, int fontSize = 0)
        {
            return battleStage != null && battleStage.IsForestCombat
                ? UIButton.DrawForest(rect, label, enabled, accentColor, fontSize)
                : UIButton.Draw(rect, label, enabled, accentColor, fontSize);
        }

        // Mismo boton compartido (UIButton) que el resto del juego, pero con una entrada extra:
        // desliza desde la izquierda con una desaceleracion marcada, en cascada segun "order"
        // (cada boton entra un poco despues que el anterior) -- el gesto de los menus de Persona
        // 5. No es clickeable hasta que termina de entrar, para que la animacion se note.
        private bool DrawP5Button(Rect target, string label, int order)
        {
            if (battleStage != null && battleStage.IsForestCombat)
            {
                const float forestDelay = 0.045f;
                const float forestDuration = 0.18f;
                float forestT = Mathf.Clamp01((Time.time - _actionMenuRevealStart - order * forestDelay) / forestDuration);
                if (forestT <= 0f) return false;
                float rise = (1f - forestT) * 12f;
                var forestRect = new Rect(target.x, target.y + rise, target.width, target.height);
                bool forestClicked = DrawBattleButton(forestRect, label);
                return forestT >= 1f && forestClicked;
            }

            const float perStepDelay = 0.06f;
            const float duration = 0.22f;
            float t = Mathf.Clamp01((Time.time - _actionMenuRevealStart - order * perStepDelay) / duration);
            if (t <= 0f) return false;

            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float slide = (1f - eased) * (target.width + 40f);
            var r = new Rect(target.x - slide, target.y, target.width, target.height);

            bool clicked = DrawBattleButton(r, label.ToUpperInvariant());
            return t >= 1f && clicked;
        }

        // Overlay del QTE: rework completo del dibujo viejo (que vivia adentro del panel lateral y
        // mostraba TODA la secuencia revelada de entrada, ver el commit anterior). Ahora:
        //  - vive en el CENTRO real de la pantalla (Screen.width/height), no en el panel de abajo;
        //  - solo se ve la tecla que toca AHORA (qte.Sequence[qte.ProgressIndex]) -- las que faltan
        //    quedan como pips grises sin revelar cual son, asi no se puede memorizar la secuencia
        //    entera de un vistazo como antes;
        //  - la tecla actual "pop-ea" (arranca grande y se asienta) cada vez que ProgressIndex
        //    avanza, ver UpdateQteFeedback/_qtePressPopStart;
        //  - al terminar (exito o fallo) se queda un instante mas (QteResultLingerDuration) con el
        //    resultado grande en vez de desaparecer en seco.
        private void DrawQteCenterOverlay()
        {
            var qte = QteManager;
            if (qte == null) return;
            bool active = qte.IsActive;
            bool lingering = !active && Time.time - _qteResultLingerStart < QteResultLingerDuration;
            if (!active && !lingering) return;

            float cx = Screen.width * 0.5f;
            const float boxW = 260f, boxH = 176f;
            // Un poco arriba del centro exacto: deja libre la mitad inferior de la pantalla, donde
            // sigue viviendo el panel de combate de siempre.
            float boxY = Screen.height * 0.5f - boxH - 30f;
            var boxRect = new Rect(cx - boxW * 0.5f, boxY, boxW, boxH);

            DrawRect(new Rect(boxRect.x - 3, boxRect.y - 3, boxRect.width + 6, boxRect.height + 6), new Color(0f, 0f, 0f, 0.4f));
            DrawRect(boxRect, new Color(0.05f, 0.05f, 0.07f, 0.88f));

            var titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
            titleStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            GUI.Label(new Rect(boxRect.x, boxRect.y + 6, boxRect.width, 20), "¡Repetí la secuencia!", titleStyle);

            if (active)
            {
                // --- Barra de tiempo ---
                float barW = boxRect.width - 40, barH = 10;
                var barRect = new Rect(cx - barW * 0.5f, boxRect.y + 30, barW, barH);
                DrawRect(barRect, new Color(0.2f, 0.2f, 0.22f));
                float frac = qte.TimeLimit > 0f ? Mathf.Clamp01(qte.TimeRemaining / qte.TimeLimit) : 0f;
                Color barColor = Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.3f, 0.9f, 0.3f), frac);
                DrawRect(new Rect(barRect.x, barRect.y, barRect.width * frac, barRect.height), barColor);

                // --- Pips de progreso: SOLO cuentan aciertos, nunca revelan que tecla es cada uno ---
                float pipSize = 10f, pipGap = 6f;
                float pipsTotalW = qte.Sequence.Count * pipSize + Mathf.Max(0, qte.Sequence.Count - 1) * pipGap;
                float pipX = cx - pipsTotalW * 0.5f;
                float pipY = barRect.y + barH + 10;
                for (int i = 0; i < qte.Sequence.Count; i++)
                {
                    Color c = i < qte.ProgressIndex ? new Color(0.3f, 0.9f, 0.3f) : new Color(0.4f, 0.4f, 0.45f);
                    DrawRect(new Rect(pipX, pipY, pipSize, pipSize), c);
                    pipX += pipSize + pipGap;
                }

                // --- La tecla que toca AHORA: grande, sola, en el centro, con "pop" al acertar ---
                // Reusa EaseOutBack (ver DrawDamagePopups mas abajo, mismo truco que los numeros de
                // dano): la tecla nueva "nace" en 0 y rebota hasta su tamano final en QtePopDuration
                // segundos, en vez de aparecer/quedarse quieta -- la primera tecla de una secuencia
                // (nunca hubo un acierto previo, _qtePressPopStart sigue en su default viejo) cae ya
                // asentada en escala 1, sin pop de entrada.
                const float glyphSize = 68f;
                float glyphY = pipY + 26;
                float popT = Mathf.Clamp01((Time.time - _qtePressPopStart) / QtePopDuration);
                float scale = Mathf.Max(0.05f, EaseOutBack(popT));
                Color glyphBg = Color.Lerp(new Color(1f, 0.9f, 0.2f), Color.white, popT);

                var glyphRect = new Rect(cx - glyphSize * 0.5f, glyphY, glyphSize, glyphSize);
                var savedMatrix = GUI.matrix;
                GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), glyphRect.center);

                DrawRect(new Rect(glyphRect.x - 4, glyphRect.y - 4, glyphRect.width + 8, glyphRect.height + 8), new Color(0f, 0f, 0f, 0.5f));
                DrawRect(glyphRect, glyphBg);

                var oldColor = GUI.color;
                GUI.color = Color.black;
                var glyphStyle = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                GUI.Label(glyphRect, ArrowGlyph(qte.Sequence[qte.ProgressIndex]), glyphStyle);
                GUI.color = oldColor;

                GUI.matrix = savedMatrix;
            }
            else
            {
                // --- Linger: resultado final (tilde verde / cruz roja), sin revelar nada mas ---
                var resultStyle = new GUIStyle(GUI.skin.label) { fontSize = 56, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                var oldColor = GUI.color;
                GUI.color = _qteResultWasSuccess ? new Color(0.4f, 1f, 0.45f) : new Color(1f, 0.35f, 0.35f);
                GUI.Label(new Rect(boxRect.x, boxRect.y + 60, boxRect.width, 90), _qteResultWasSuccess ? "✓" : "✗", resultStyle);
                GUI.color = oldColor;
            }
        }

        private string ArrowGlyph(QteKey key)
        {
            switch (key)
            {
                case QteKey.Up: return "↑";
                case QteKey.Down: return "↓";
                case QteKey.Left: return "←";
                default: return "→";
            }
        }

        // Resalta con un fondo tenue la linea del combatiente al que le toca actuar en este momento
        // de la ronda, para distinguir claramente un turno de aliado de uno de enemigo.
        private void DrawTurnLine(float x, float y, float w, string text, bool isCurrentTurn, bool isEnemyTurn)
        {
            if (isCurrentTurn)
            {
                Color highlight = isEnemyTurn ? new Color(0.5f, 0.15f, 0.15f, 0.6f) : new Color(0.15f, 0.4f, 0.2f, 0.6f);
                DrawRect(new Rect(x - 4, y - 1, w + 8, 20), highlight);
            }
            GUI.Label(new Rect(x, y, w, 20), isCurrentTurn ? $"► {text}" : text);
        }

        private void DrawRect(Rect rect, Color color)
        {
            if (_whiteTex == null)
            {
                _whiteTex = new Texture2D(1, 1);
                _whiteTex.SetPixel(0, 0, Color.white);
                _whiteTex.Apply();
            }
            var oldColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _whiteTex);
            GUI.color = oldColor;
        }
    }
}
