using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Combat;

namespace Gameplay
{
    public class CombatHUD : MonoBehaviour
    {
        public CombatManager combatManager;

        private ActionType? _pendingType;

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
                string status = enemy.IsAlive ? $"HP {enemy.HP}/{enemy.MaxHP}" : "derrotado";
                GUI.Label(new Rect(panelX + 20, y, panelW - 40, 20), $"{enemy.Name} - {status}");
                y += 20;
            }

            y += 12;

            // --- Party ---
            GUI.Label(new Rect(panelX + 10, y, 300, 20), "Party:");
            y += 22;
            foreach (var member in combatManager.Party)
            {
                string status = !member.IsAlive ? "caído" : member.IsGuarding ? "en guardia" : "listo";
                GUI.Label(new Rect(panelX + 20, y, panelW - 40, 20),
                    $"{member.Name} ({member.Class}) - HP {member.HP}/{member.MaxHP}  TP {member.TP}/{member.MaxTP}  [{status}]");
                y += 20;
            }

            y += 12;

            // --- Turno actual / menu de accion ---
            var chooser = combatManager.GetChooser();
            if (chooser != null)
            {
                GUI.Label(new Rect(panelX + 10, y, panelW - 20, 20), $"Turno de {chooser.Name}:");
                y += 24;

                if (_pendingType == null)
                {
                    if (GUI.Button(new Rect(panelX + 20, y, 150, 26), "Atacar"))
                        _pendingType = ActionType.Attack;

                    string skillLabel = chooser.IsHealSkill
                        ? $"{chooser.SkillName} ({chooser.SkillTpCost} TP)"
                        : $"{chooser.SkillName} ({chooser.SkillTpCost} TP)";
                    if (GUI.Button(new Rect(panelX + 180, y, 220, 26), skillLabel))
                        _pendingType = ActionType.Skill;

                    if (GUI.Button(new Rect(panelX + 410, y, 150, 26), "Guardia"))
                    {
                        combatManager.SubmitAction(new PartyAction { Actor = chooser, Type = ActionType.Guard });
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
    }
}
