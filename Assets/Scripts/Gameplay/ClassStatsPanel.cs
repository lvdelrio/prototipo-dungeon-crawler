using UnityEngine;
using Combat;

namespace Gameplay
{
    // Panel de solo lectura con las stats BASE de una clase (recien creada via
    // PartyFactory.CreateCharacter, nivel de arranque -- no las stats YA subidas de un personaje
    // real de la party actual). Reusado como modal centrado tanto por MainMenuHUD (boton "Status"
    // en cada carta de la pantalla de creacion de party) como por PauseMenuHUD (boton "Status" por
    // fila en la pestaña Stats), asi las dos pantallas muestran exactamente la misma info con el
    // mismo aspecto en vez de reinventar la tabla dos veces.
    public static class ClassStatsPanel
    {
        private const float Width = 360f;
        private const float Height = 380f;

        // Dibuja el modal centrado en pantalla (fondo + tarjeta + boton Cerrar) y devuelve true si
        // el jugador pidio cerrarlo este frame (click en "Cerrar" o fuera de la tarjeta).
        public static bool DrawModal(CharacterClass cls)
        {
            float w = Screen.width, h = Screen.height;

            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = old;

            var boxRect = new Rect((w - Width) / 2f, (h - Height) / 2f, Width, Height);
            bool clickedOutside = Event.current != null
                && Event.current.type == EventType.MouseDown
                && !boxRect.Contains(Event.current.mousePosition);

            DrawCard(boxRect, cls);

            bool clickedClose = UIButton.Draw(new Rect(boxRect.x + boxRect.width - 110, boxRect.y + boxRect.height - 40, 96, 28), "Cerrar");
            return clickedClose || clickedOutside;
        }

        private static void DrawCard(Rect rect, CharacterClass cls)
        {
            var c = PartyFactory.CreateCharacter(cls);

            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(rect.x + 3, rect.y + 4, rect.width, rect.height), Texture2D.whiteTexture);
            GUI.color = new Color(0.05f, 0.05f, 0.08f, 0.98f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.78f, 0.2f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 4f), Texture2D.whiteTexture);
            GUI.color = old;

            var titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 22, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
            GUI.Label(new Rect(rect.x, rect.y + 12, rect.width, 30), c.Class.ToString(), titleStyle);

            float y = rect.y + 52;
            (string label, string value)[] rows =
            {
                ("HP", c.MaxHP.ToString()),
                ("TP", c.MaxTP.ToString()),
                ("Ataque", c.Attack.ToString()),
                ("Ataque mágico", c.MagicAttack.ToString()),
                ("Defensa", c.Defense.ToString()),
                ("Velocidad", c.Speed.ToString()),
                ("Evasión", $"{c.Evasion}%"),
                ("Suerte (crítico)", $"{c.Luck}%"),
                ("Fila", c.IsFrontRow ? "Frente" : "Fondo"),
                ("Elemento de ataque", ElementLabel(c.AttackElement)),
            };

            float labelW = rect.width * 0.58f;
            var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            var valueStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            valueStyle.normal.textColor = new Color(0.95f, 0.95f, 0.8f);
            foreach (var (label, value) in rows)
            {
                GUI.Label(new Rect(rect.x + 18, y, labelW - 18, 20), label, labelStyle);
                GUI.Label(new Rect(rect.x + labelW, y, rect.width - labelW - 18, 20), value, valueStyle);
                y += 21f;
            }

            y += 6;
            DrawRectLine(new Rect(rect.x + 18, y, rect.width - 36, 1), new Color(0.3f, 0.3f, 0.32f));
            y += 10;

            var skillNameStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13 };
            skillNameStyle.normal.textColor = new Color(0.7f, 0.85f, 1f);
            GUI.Label(new Rect(rect.x + 18, y, rect.width - 36, 20), $"Habilidad: {c.SkillName} (TP {c.SkillTpCost})", skillNameStyle);
            y += 22;

            var descStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };
            descStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            GUI.Label(new Rect(rect.x + 18, y, rect.width - 36, 60), SkillDescription(c), descStyle);
        }

        private static string SkillDescription(CharacterStats c)
        {
            if (c.IsHealSkill) return $"Cura {c.HealAmount} HP a un aliado (+ bonus por Ataque Mágico). También revive a un caído.";
            if (c.IsSelfStanceSkill) return "Postura propia (sin objetivo): sube el ataque y baja la defensa mientras esté activa.";
            if (c.IsVersatileBuffSkill) return "Versátil: sobre un aliado buffea su Ataque, sobre un enemigo debuffea su Defensa.";
            if (c.SkillIsAoe) return $"Golpea a TODOS los enemigos vivos a la vez ({ElementLabel(c.SkillElement)}, potencia x{c.SkillPower:F1}).";
            if (c.SkillHitsEnemyFrontRow) return $"Golpea a TODA la fila delantera de enemigos a la vez ({ElementLabel(c.SkillElement)}, potencia x{c.SkillPower:F1}).";
            return $"Golpe fuerte a un solo objetivo ({ElementLabel(c.SkillElement)}, potencia x{c.SkillPower:F1}).";
        }

        private static string ElementLabel(Element element)
        {
            switch (element)
            {
                case Element.Slash: return "Corte";
                case Element.Strike: return "Golpe";
                case Element.Pierce: return "Perforación";
                case Element.Fire: return "Fuego";
                case Element.Ice: return "Hielo";
                case Element.Volt: return "Rayo";
                default: return "Ninguno";
            }
        }

        private static void DrawRectLine(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
