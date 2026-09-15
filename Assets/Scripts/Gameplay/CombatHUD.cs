using System.Linq;
using UnityEngine;
using Combat;

namespace Gameplay
{
    public class CombatHUD : MonoBehaviour
    {
        public CombatManager combatManager;
        public QteManager qteManager;

        [Header("QTE")]
        public float qteTimeLimit = 2.5f;

        private ActionType? _pendingType;
        private PartyAction _pendingSkillAction; // accion en espera de que termine el QTE
        private System.Random _rng = new System.Random();
        private static Texture2D _whiteTex;

        void OnGUI()
        {
            if (combatManager == null || !combatManager.IsActive)
            {
                _pendingType = null;
                _pendingSkillAction = null;
                return;
            }

            const int panelX = 10, panelY = 180, panelW = 940, panelH = 500;
            GUI.Box(new Rect(panelX, panelY, panelW, panelH), "");
            GUI.Label(new Rect(panelX + 10, panelY + 5, panelW - 20, 24), combatManager.IsBossFight ? "COMBATE DE JEFE" : "COMBATE");

            float y = panelY + 32;

            // --- Enemigos ---
            GUI.Label(new Rect(panelX + 10, y, 300, 20), "Enemigos:");
            y += 22;
            foreach (var enemy in combatManager.Enemies)
            {
                bool isTurn = combatManager.IsResolvingRound && !combatManager.CurrentTurnIsParty && combatManager.CurrentTurnActorName == enemy.Name;
                string status = enemy.IsAlive ? $"HP {enemy.HP}/{enemy.MaxHP}" : "derrotado";
                DrawTurnLine(panelX + 20, y, panelW - 40, $"{enemy.Name} - {status}", isTurn, isEnemyTurn: true);
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
                DrawTurnLine(panelX + 20, y, panelW - 40, $"{member.Name} ({member.Class}) - HP {member.HP}/{member.MaxHP}  TP {member.TP}/{member.MaxTP}  [{status}]", isTurn, isEnemyTurn: false);
                y += 20;
            }

            y += 12;

            if (qteManager != null && qteManager.IsActive)
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
                    GUI.Label(new Rect(panelX + 10, y, panelW - 20, 20), $"Turno de {chooser.Name}:");
                    y += 24;

                    if (_pendingType == null)
                    {
                        if (GUI.Button(new Rect(panelX + 20, y, 120, 26), "Atacar"))
                            _pendingType = ActionType.Attack;

                        string skillLabel = $"{chooser.SkillName} ({chooser.SkillTpCost} TP)";
                        if (GUI.Button(new Rect(panelX + 150, y, 200, 26), skillLabel))
                            _pendingType = ActionType.Skill;

                        if (GUI.Button(new Rect(panelX + 360, y, 120, 26), "Guardia"))
                        {
                            combatManager.SubmitAction(new PartyAction { Actor = chooser, Type = ActionType.Guard });
                            _pendingType = null;
                        }

                        if (chooser.CanProtectAll)
                        {
                            if (GUI.Button(new Rect(panelX + 490, y, 190, 26), "Proteger a todos"))
                            {
                                combatManager.SubmitAction(new PartyAction { Actor = chooser, Type = ActionType.ProtectAll });
                                _pendingType = null;
                            }
                        }

                        if (GUI.Button(new Rect(panelX + 690, y, 170, 26), "Auto (todos atacan)"))
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
                            if (GUI.Button(new Rect(bx, y, 150, 26), ally.Name))
                            {
                                var action = new PartyAction { Actor = chooser, Type = ActionType.Skill, TargetAllyIndex = combatManager.Party.IndexOf(ally) };
                                BeginSkillQte(chooser, action);
                                _pendingType = null;
                            }
                            bx += 160;
                        }
                        if (GUI.Button(new Rect(panelX + 20, y + 34, 100, 24), "Cancelar")) _pendingType = null;
                    }
                    else if (_pendingType.HasValue)
                    {
                        GUI.Label(new Rect(panelX + 20, y, 300, 20), "Elegí un objetivo:");
                        y += 22;
                        float bx = panelX + 20;
                        foreach (var enemy in combatManager.Enemies.Where(e => e.IsAlive))
                        {
                            int idx = combatManager.Enemies.IndexOf(enemy);
                            if (GUI.Button(new Rect(bx, y, 180, 26), enemy.Name))
                            {
                                var action = new PartyAction { Actor = chooser, Type = _pendingType.Value, TargetEnemyIndex = idx };
                                if (_pendingType.Value == ActionType.Skill)
                                    BeginSkillQte(chooser, action);
                                else
                                    combatManager.SubmitAction(action);
                                _pendingType = null;
                            }
                            bx += 190;
                        }
                        if (GUI.Button(new Rect(panelX + 20, y + 34, 100, 24), "Cancelar")) _pendingType = null;
                    }
                }
                else
                {
                    GUI.Label(new Rect(panelX + 10, y, panelW - 20, 20), "Resolviendo ronda...");
                }
            }

            // --- Log de combate (ultimas lineas) ---
            float logY = panelY + panelH - 130;
            GUI.Box(new Rect(panelX + 10, logY, panelW - 20, 110), "");
            var lastLines = combatManager.Log.Skip(Mathf.Max(0, combatManager.Log.Count - 6));
            float ly = logY + 6;
            foreach (var line in lastLines)
            {
                GUI.Label(new Rect(panelX + 18, ly, panelW - 36, 18), line);
                ly += 18;
            }
        }

        // Arranca el QTE para una habilidad: elige al azar una de las 2 secuencias fijas del
        // personaje y deja la accion "pendiente" hasta que el QteManager avise si salio bien o mal.
        private void BeginSkillQte(CharacterStats actor, PartyAction action)
        {
            if (qteManager == null)
            {
                combatManager.SubmitAction(action);
                return;
            }

            var sequence = _rng.Next(2) == 0 ? actor.SkillSequenceA : actor.SkillSequenceB;
            if (sequence == null || sequence.Length == 0)
            {
                combatManager.SubmitAction(action);
                return;
            }

            _pendingSkillAction = action;
            qteManager.Begin(sequence, qteTimeLimit, success =>
            {
                _pendingSkillAction.QteSuccess = success;
                combatManager.SubmitAction(_pendingSkillAction);
                _pendingSkillAction = null;
            });
        }

        private void DrawQteOverlay(float panelX, float y, float panelW)
        {
            GUI.Box(new Rect(panelX + 10, y, panelW - 20, 80), "");
            GUI.Label(new Rect(panelX + 20, y + 4, panelW - 40, 20), "¡Repetí la secuencia a tiempo para un golpe extra!");

            // Barra de tiempo que se achica en tiempo real.
            float barX = panelX + 20, barY = y + 26, barW = panelW - 220, barH = 16;
            DrawRect(new Rect(barX, barY, barW, barH), new Color(0.2f, 0.2f, 0.22f));
            float frac = qteManager.TimeLimit > 0f ? Mathf.Clamp01(qteManager.TimeRemaining / qteManager.TimeLimit) : 0f;
            Color barColor = Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.3f, 0.9f, 0.3f), frac);
            DrawRect(new Rect(barX, barY, barW * frac, barH), barColor);

            // Iconos de la secuencia: gris = pendiente, amarillo = el que toca ahora, verde = ya hecho.
            float iconX = panelX + 20;
            float iconY = y + 48;
            for (int i = 0; i < qteManager.Sequence.Count; i++)
            {
                Color c = i < qteManager.ProgressIndex ? new Color(0.3f, 0.9f, 0.3f)
                    : i == qteManager.ProgressIndex ? new Color(1f, 0.9f, 0.2f)
                    : new Color(0.5f, 0.5f, 0.5f);
                DrawRect(new Rect(iconX, iconY, 26, 26), c);
                var old = GUI.color;
                GUI.color = Color.black;
                GUI.Label(new Rect(iconX, iconY + 3, 26, 20), ArrowGlyph(qteManager.Sequence[i]));
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
