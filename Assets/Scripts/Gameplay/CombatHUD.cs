using System.Linq;
using UnityEngine;
using Combat;

namespace Gameplay
{
    public class CombatHUD : MonoBehaviour
    {
        public CombatManager combatManager;

        private ActionType? _pendingType;
        private static Texture2D _whiteTex;

        void OnGUI()
        {
            if (combatManager == null || !combatManager.IsActive)
            {
                _pendingType = null;
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
                string status = !member.IsAlive ? "caído" : member.IsGuarding ? "en guardia" : "listo";
                DrawTurnLine(panelX + 20, y, panelW - 40, $"{member.Name} ({member.Class}) - HP {member.HP}/{member.MaxHP}  TP {member.TP}/{member.MaxTP}  [{status}]", isTurn, isEnemyTurn: false);
                y += 20;
            }

            y += 12;

            if (combatManager.IsResolvingRound)
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
                        if (GUI.Button(new Rect(panelX + 20, y, 130, 26), "Atacar"))
                            _pendingType = ActionType.Attack;

                        string skillLabel = $"{chooser.SkillName} ({chooser.SkillTpCost} TP)";
                        if (GUI.Button(new Rect(panelX + 160, y, 210, 26), skillLabel))
                            _pendingType = ActionType.Skill;

                        if (GUI.Button(new Rect(panelX + 380, y, 130, 26), "Guardia"))
                        {
                            combatManager.SubmitAction(new PartyAction { Actor = chooser, Type = ActionType.Guard });
                            _pendingType = null;
                        }

                        if (GUI.Button(new Rect(panelX + 520, y, 150, 26), "Auto (todos atacan)"))
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
                                combatManager.SubmitAction(new PartyAction { Actor = chooser, Type = ActionType.Skill, TargetAllyIndex = combatManager.Party.IndexOf(ally) });
                                _pendingType = null;
                            }
                            bx += 160;
                        }
                        if (GUI.Button(new Rect(panelX + 20, y + 34, 100, 24), "Cancelar")) _pendingType = null;
                    }
                    else
                    {
                        GUI.Label(new Rect(panelX + 20, y, 300, 20), "Elegí un objetivo:");
                        y += 22;
                        float bx = panelX + 20;
                        foreach (var enemy in combatManager.Enemies.Where(e => e.IsAlive))
                        {
                            int idx = combatManager.Enemies.IndexOf(enemy);
                            if (GUI.Button(new Rect(bx, y, 180, 26), enemy.Name))
                            {
                                combatManager.SubmitAction(new PartyAction { Actor = chooser, Type = _pendingType.Value, TargetEnemyIndex = idx });
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
