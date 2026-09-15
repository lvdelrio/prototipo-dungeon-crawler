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
        private static Texture2D _whiteTex;
        private bool _wasActive;

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

        void OnGUI()
        {
            if (combatManager == null || !combatManager.IsActive)
            {
                _pendingType = null;
                _wasActive = false;
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

            const int panelX = 10, panelY = 180, panelW = 940, panelH = 500;
            GUI.Box(new Rect(panelX, panelY, panelW, panelH), "");
            GUI.Label(new Rect(panelX + 10, panelY + 5, panelW - 240, 24), combatManager.IsBossFight ? "COMBATE DE JEFE" : "COMBATE");

            if (GUI.Button(new Rect(panelX + panelW - 220, panelY + 4, 100, 24), "Rendirse"))
                combatManager.Surrender();
            if (GUI.Button(new Rect(panelX + panelW - 110, panelY + 4, 100, 24), "[TEST] Saltar"))
                combatManager.SkipFightForTesting();

            float y = panelY + 32;

            // --- Enemigos ---
            GUI.Label(new Rect(panelX + 10, y, 300, 20), "Enemigos:");
            y += 22;
            foreach (var enemy in combatManager.Enemies)
            {
                bool isTurn = combatManager.IsResolvingRound && !combatManager.CurrentTurnIsParty && combatManager.CurrentTurnActorName == enemy.Name;
                string status = enemy.IsAlive ? $"HP {enemy.HP}/{enemy.MaxHP}" : "derrotado";
                DrawTurnLine(panelX + 20, y, panelW - 40, $"{enemy.Name} - {status}", isTurn, isEnemyTurn: true);
                TrackHpChange(_lastEnemyHp, enemy, enemy.HP, panelX + panelW - 60, y);
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
                TrackHpChange(_lastPartyHp, member, member.HP, panelX + panelW - 60, y);
                y += 20;
            }

            y += 12;

            if (QteManager != null && QteManager.IsActive)
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
                                combatManager.SubmitAction(action);
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

            DrawDamagePopups();
        }

        // Compara el HP actual contra el ultimo visto para ese combatiente; si bajo o subio,
        // crea un numero flotante (rojo = dano, verde = curacion) en la posicion de su linea.
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

        private void DrawDamagePopups()
        {
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                float age = Time.time - _popups[i].StartTime;
                if (age >= PopupDuration) { _popups.RemoveAt(i); continue; }

                float frac = age / PopupDuration;
                var popup = _popups[i];
                var oldColor = GUI.color;
                GUI.color = new Color(popup.Color.r, popup.Color.g, popup.Color.b, 1f - frac);
                GUI.Label(new Rect(popup.X, popup.Y - frac * 24f, 60, 20), popup.Text);
                GUI.color = oldColor;
            }
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
