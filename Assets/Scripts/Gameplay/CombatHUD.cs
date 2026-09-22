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
        }

        private QteManager QteManager => combatManager != null ? combatManager.qteManager : null;

        // Ademas del boton, se puede "smashear" Espacio para el Ataque en Conjunto (mas estiloso
        // que solo un click) -- se chequea en Update (no en OnGUI) para no disparar varias veces
        // por el mismo frame.
        void Update()
        {
            if (combatManager == null) return;
            if (combatManager.AllOutAttackReady && Input.GetKeyDown(KeyCode.Space))
                combatManager.TriggerAllOutAttack();
            else if (combatManager.IsShowingVictorySummary && Input.GetKeyDown(KeyCode.Space))
                combatManager.DismissVictorySummary();
        }

        void OnGUI()
        {
            if (combatManager == null || !combatManager.IsActive)
            {
                _pendingType = null;
                _wasActive = false;
                _showingAbilities = false;
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

            if (UIButton.Draw(new Rect(panelX + panelW - 330, panelY + 4, 100, 24), $"Huir ({combatManager.FleeChancePercent:F0}%)", enabled: !combatManager.IsBossFight))
                combatManager.TryFlee();
            if (UIButton.Draw(new Rect(panelX + panelW - 220, panelY + 4, 100, 24), "Rendirse"))
                combatManager.Surrender();
            if (UIButton.Draw(new Rect(panelX + panelW - 110, panelY + 4, 100, 24), "[TEST] Saltar"))
                combatManager.SkipFightForTesting();

            float y = panelY + 32;

            // --- Enemigos ---
            GUI.Label(new Rect(panelX + 10, y, 200, 20), "Enemigos:");
            if (UIButton.Draw(new Rect(panelX + 200, y - 2, 150, 22), _inspecting ? "Ocultar debilidades" : "Inspeccionar"))
                _inspecting = !_inspecting;
            y += 22;
            foreach (var enemy in combatManager.Enemies)
            {
                bool isTurn = combatManager.IsResolvingRound && !combatManager.CurrentTurnIsParty && combatManager.CurrentTurnActorName == enemy.Name;
                string status = enemy.IsAlive ? $"HP {enemy.HP}/{enemy.MaxHP}{(enemy.IsBroken ? " [ROTO: pierde su turno]" : "")}" : "derrotado";
                string weaknessLabel = enemy.Weaknesses != null && enemy.Weaknesses.Length > 0
                    ? string.Join("/", enemy.Weaknesses.Select(ElementLabel))
                    : ElementLabel(Element.None);
                string inspect = _inspecting
                    ? $"  |  Debil: {weaknessLabel}  Resiste: {ElementLabel(enemy.Resistance)}  DEF {enemy.Defense}  VEL {enemy.Speed}"
                    : "";
                DrawTurnLine(panelX + 20, y, panelW - 40, $"{enemy.Name} - {status}{inspect}", isTurn, isEnemyTurn: true);
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
                DrawTurnLine(panelX + 20, y, panelW - 40, $"{member.Name} ({member.Class}, {row}) - HP {member.HP}/{member.MaxHP}  TP {member.TP}/{member.MaxTP}  [{status}]", isTurn, isEnemyTurn: false);
                TrackHpChange(_lastPartyHp, member, member.HP, panelX + panelW - 60, y);
                y += 20;
            }

            y += 12;

            if (combatManager.AllOutAttackReady)
            {
                DrawAllOutAttackBanner(panelX, y, panelW);
                y += 90;
            }
            else if (QteManager != null && QteManager.IsActive)
            {
                DrawQteOverlay(panelX, y, panelW);
                y += 90;
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
                    if (UIButton.Draw(new Rect(panelX + panelW - 120, y - 2, 120, 22), "◄ Volver", enabled: combatManager.CanGoBack))
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
                        DrawGunnerBulletSelector(panelX, y, chooser);
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
                                    : chooser.AttacksAreAoe
                                        ? $"{chooser.SkillName} - {ElementLabel(chooser.SkillElement)} x{chooser.SkillPower:F1} a TODOS ({chooser.SkillTpCost} TP)"
                                        : $"{chooser.SkillName} - {ElementLabel(chooser.SkillElement)} x{chooser.SkillPower:F1} ({chooser.SkillTpCost} TP)";
                        if (UIButton.Draw(new Rect(panelX + 20, y, 340, 26), skillLabel))
                        {
                            _showingAbilities = false;
                            if (chooser.IsSelfStanceSkill)
                                combatManager.SubmitAction(new PartyAction { Actor = chooser, Type = ActionType.Skill });
                            else if (chooser.AttacksAreAoe)
                                combatManager.SubmitAction(new PartyAction { Actor = chooser, Type = ActionType.Skill });
                            else
                                _pendingType = ActionType.Skill;
                        }

                        if (chooser.CanProtectAll)
                        {
                            string protectLabel = $"Proteger a todos ({CombatEngine.ProtectAllTpCost} TP)";
                            if (UIButton.Draw(new Rect(panelX + 370, y, 220, 26), protectLabel))
                            {
                                combatManager.SubmitAction(new PartyAction { Actor = chooser, Type = ActionType.ProtectAll });
                                _showingAbilities = false;
                            }
                        }

                        if (UIButton.Draw(new Rect(panelX + 600, y, 100, 26), "Cerrar"))
                            _showingAbilities = false;
                    }
                    else if (_pendingType == null)
                    {
                        TrackActionMenuReveal(chooser);

                        if (DrawP5Button(new Rect(panelX + 20, y, 120, 30), "Atacar", 0))
                        {
                            if (chooser.AttacksAreAoe)
                                combatManager.SubmitAction(new PartyAction { Actor = chooser, Type = ActionType.Attack });
                            else
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
                    }
                    else if (_pendingType == ActionType.Skill && chooser.IsHealSkill)
                    {
                        GUI.Label(new Rect(panelX + 20, y, 300, 20), "Elegí a quién curar:");
                        y += 22;
                        float bx = panelX + 20;
                        foreach (var ally in combatManager.Party.Where(p => p.IsAlive))
                        {
                            if (UIButton.Draw(new Rect(bx, y, 150, 26), ally.Name))
                            {
                                var action = new PartyAction { Actor = chooser, Type = ActionType.Skill, TargetAllyIndex = combatManager.Party.IndexOf(ally) };
                                combatManager.SubmitAction(action);
                                _pendingType = null;
                            }
                            bx += 160;
                        }
                        if (UIButton.Draw(new Rect(panelX + 20, y + 34, 100, 24), "Cancelar")) _pendingType = null;
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
                            if (UIButton.Draw(new Rect(bx, y, 150, 26), $"{ally.Name} (buff)"))
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
                            if (UIButton.Draw(new Rect(bx, y, 150, 26), $"{enemy.Name} (debuff)"))
                            {
                                var action = new PartyAction { Actor = chooser, Type = ActionType.Skill, TargetIsAlly = false, TargetEnemyIndex = idx };
                                combatManager.SubmitAction(action);
                                _pendingType = null;
                            }
                            bx += 160;
                        }
                        if (UIButton.Draw(new Rect(panelX + 20, y + 34, 100, 24), "Cancelar")) _pendingType = null;
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
                            if (UIButton.Draw(new Rect(bx, y, 180, 26), enemy.Name))
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
                        if (UIButton.Draw(new Rect(panelX + 20, y + 4, 100, 24), "Cancelar")) _pendingType = null;
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

            Vector3 screenPos = cam.WorldToScreenPoint(view.TopAnchor);
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
                _popups.Add(new DamagePopup { Text = text, Color = color, StartTime = Time.time, X = x, Y = y });
            }
            lastHp[key] = currentHp;
        }

        private static GUIStyle _popupStyle;

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
                _popupStyle.normal.textColor = new Color(popup.Color.r, popup.Color.g, popup.Color.b, 1f - frac);
                // Centrado horizontal real (no solo el punto de anclaje a la izquierda), y sube
                // flotando desde la posicion de origen -- sobre el enemigo si vino de
                // EnemyPopupPosition, o junto a la fila del panel para la party.
                GUI.Label(new Rect(popup.X - w / 2f, popup.Y - frac * 24f - h / 2f, w, h), popup.Text, _popupStyle);
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
                if (UIButton.Draw(new Rect(bx, y, 130, 24), text, enabled: enabled))
                    gunner.LoadedBulletElement = element;
                GUI.color = old;
                bx += 136;
            }
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

        // Banner grande y llamativo ("hazlo estiloso"): se rompio el aguante de TODOS los
        // enemigos a la vez. Pulsa entre dorado y blanco y ofrece el boton (o Espacio) para
        // lanzar el golpe de equipo antes de que se acabe el tiempo.
        private void DrawAllOutAttackBanner(float panelX, float y, float panelW)
        {
            int mashCount = combatManager.AllOutAttackMashCount;
            // Cuanto mas se "machaca" el boton, mas intenso el pulso -- feedback inmediato de que
            // cada apretada suma (hasta el tope, donde se queda brillando a full).
            float mashFrac = Mathf.Clamp01(mashCount / (float)CombatEngine.AllOutMaxPresses);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (8f + mashFrac * 10f));
            Color glow = Color.Lerp(new Color(0.85f, 0.65f, 0.1f), new Color(1f, 0.95f, 0.55f), Mathf.Max(pulse, mashFrac));

            DrawRect(new Rect(panelX + 10, y, panelW - 20, 80), new Color(0.08f, 0.06f, 0.02f, 0.9f));
            DrawRect(new Rect(panelX + 10, y, panelW - 20, 4), glow);
            DrawRect(new Rect(panelX + 10, y + 76, panelW - 20, 4), glow);

            var oldColor = GUI.color;
            var bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            bigStyle.normal.textColor = glow;
            GUI.Label(new Rect(panelX + 10, y + 6, panelW - 20, 28), "¡TODOS LOS ENEMIGOS ATURDIDOS!", bigStyle);
            GUI.color = oldColor;

            string countLabel = mashCount > 0 ? $"¡Golpes acumulados: x{mashCount}! Mientras más machacás, más daño." : "¡MACHACÁ el botón (o Espacio) para el Ataque en Conjunto!";
            GUI.Label(new Rect(panelX + 20, y + 34, panelW - 260, 24), countLabel);

            string buttonLabel = mashCount > 0 ? $"¡SEGUÍ MACHACANDO! x{mashCount} (Espacio)" : "¡ATAQUE EN CONJUNTO! (Espacio)";
            if (UIButton.Draw(new Rect(panelX + panelW - 260, y + 30, 240, 32), buttonLabel, accentColor: glow))
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

            if (UIButton.Draw(new Rect(panelX + panelW - 180, panelY + panelH - 44, 160, 34), "Continuar"))
                combatManager.DismissVictorySummary();
        }

        private void TrackActionMenuReveal(CharacterStats chooser)
        {
            if (chooser == _actionMenuChooser) return;
            _actionMenuChooser = chooser;
            _actionMenuRevealStart = Time.time;
        }

        // Mismo boton compartido (UIButton) que el resto del juego, pero con una entrada extra:
        // desliza desde la izquierda con una desaceleracion marcada, en cascada segun "order"
        // (cada boton entra un poco despues que el anterior) -- el gesto de los menus de Persona
        // 5. No es clickeable hasta que termina de entrar, para que la animacion se note.
        private bool DrawP5Button(Rect target, string label, int order)
        {
            const float perStepDelay = 0.06f;
            const float duration = 0.22f;
            float t = Mathf.Clamp01((Time.time - _actionMenuRevealStart - order * perStepDelay) / duration);
            if (t <= 0f) return false;

            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float slide = (1f - eased) * (target.width + 40f);
            var r = new Rect(target.x - slide, target.y, target.width, target.height);

            bool clicked = UIButton.Draw(r, label.ToUpperInvariant());
            return t >= 1f && clicked;
        }

        private void DrawQteOverlay(float panelX, float y, float panelW)
        {
            var qte = QteManager;
            GUI.Box(new Rect(panelX + 10, y, panelW - 20, 80), "");
            GUI.Label(new Rect(panelX + 20, y + 4, panelW - 40, 20), "¡Repetí la secuencia a tiempo para un golpe extra!");

            // Barra de tiempo que se achica en tiempo real.
            float barX = panelX + 20, barY = y + 26, barW = panelW - 220, barH = 16;
            DrawRect(new Rect(barX, barY, barW, barH), new Color(0.2f, 0.2f, 0.22f));
            float frac = qte.TimeLimit > 0f ? Mathf.Clamp01(qte.TimeRemaining / qte.TimeLimit) : 0f;
            Color barColor = Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.3f, 0.9f, 0.3f), frac);
            DrawRect(new Rect(barX, barY, barW * frac, barH), barColor);

            // Iconos de la secuencia: gris = pendiente, amarillo = el que toca ahora, verde = ya hecho.
            float iconX = panelX + 20;
            float iconY = y + 48;
            for (int i = 0; i < qte.Sequence.Count; i++)
            {
                Color c = i < qte.ProgressIndex ? new Color(0.3f, 0.9f, 0.3f)
                    : i == qte.ProgressIndex ? new Color(1f, 0.9f, 0.2f)
                    : new Color(0.5f, 0.5f, 0.5f);
                DrawRect(new Rect(iconX, iconY, 26, 26), c);
                var old = GUI.color;
                GUI.color = Color.black;
                GUI.Label(new Rect(iconX, iconY + 3, 26, 20), ArrowGlyph(qte.Sequence[i]));
                GUI.color = old;
                iconX += 34;
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
