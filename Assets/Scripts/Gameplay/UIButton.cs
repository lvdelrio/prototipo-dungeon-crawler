using UnityEngine;
using System.Collections.Generic;

namespace Gameplay
{
    // Estilo de boton UNICO para toda la UI del juego. Antes cada panel (menu de pausa, tienda,
    // dialogo, HUD de combate) dibujaba sus propios GUI.Button() con el skin gris por defecto de
    // Unity, y solo el menu de accion principal (Atacar/Habilidades/Guardia/Auto) tenia un estilo
    // propio "Persona 5" (capas apiladas: sombra + borde blanco + cuerpo negro + acento rojo
    // lateral) -- la mezcla se sentia como dos juegos distintos pegados con cinta. Ahora CUALQUIER
    // boton del juego pasa por este mismo dibujo, y ademas reacciona al mouse (crece un poco y el
    // acento se ilumina) para que se sienta "vivo" sin necesitar estado propio por boton (initable
    // stateless: usa Event.current.mousePosition, que IMGUI ya da gratis cada frame).
    public static class UIButton
    {
        private static readonly Color Black = new Color(0.07f, 0.07f, 0.08f);
        private static readonly Color Red = new Color(0.82f, 0.08f, 0.1f);
        private static readonly Color HoverRed = new Color(1f, 0.35f, 0.3f);
        private static Texture2D _whiteTex;
        private struct ClickSpark { public Vector2 Origin, Direction; public float Start; public Color Color; }
        private static readonly List<ClickSpark> _forestSparks = new List<ClickSpark>();

        // accentColor: para bananas especiales (p.ej. el aviso pulsante del Ataque en Conjunto)
        // que quieren su propio color de acento en vez del rojo estandar, sin perder la misma
        // silueta/capas que todo el resto de los botones del juego.
        public static bool Draw(Rect rect, string label, bool enabled = true, Color? accentColor = null, int fontSize = 0)
        {
            bool hover = enabled && Event.current != null && rect.Contains(Event.current.mousePosition);
            float grow = hover ? 3f : 0f;
            var r = new Rect(rect.x - grow / 2f, rect.y - grow / 2f, rect.width + grow, rect.height + grow);

            DrawRect(new Rect(r.x + 3, r.y + 4, r.width, r.height), new Color(0f, 0f, 0f, 0.35f));
            DrawRect(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), enabled ? Color.white : new Color(0.45f, 0.45f, 0.45f));
            DrawRect(r, Black);

            Color baseAccent = accentColor ?? Red;
            Color accent = hover ? Color.Lerp(baseAccent, Color.white, 0.35f) : baseAccent;
            if (!enabled) accent = new Color(0.3f, 0.3f, 0.3f);
            DrawRect(new Rect(r.x, r.y, 6, r.height), accent);

            bool clicked = enabled && GUI.Button(r, "", GUIStyle.none);

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = fontSize > 0 ? fontSize : (int)Mathf.Clamp(rect.height * 0.5f, 11f, 18f),
                wordWrap = false,
            };
            style.normal.textColor = enabled ? Color.white : new Color(0.65f, 0.65f, 0.65f);
            GUI.Label(r, label, style);

            return clicked;
        }

        public static bool DrawForest(Rect rect, string label, bool enabled = true, Color? accentColor = null, int fontSize = 0)
        {
            bool hover = enabled && Event.current != null && rect.Contains(Event.current.mousePosition);
            bool down = hover && Event.current.type == EventType.MouseDown && Event.current.button == 0;
            float lift = hover ? -2f : 0f;
            if (down) lift = 1f;
            var r = new Rect(rect.x, rect.y + lift, rect.width, rect.height);
            Color accent = accentColor ?? new Color(0.43f, 0.74f, 0.43f);
            DrawRect(new Rect(r.x + 2, r.y + 3, r.width, r.height), new Color(0.02f, 0.06f, 0.04f, 0.62f));
            DrawRect(new Rect(r.x, r.y, r.width, r.height), enabled ? new Color(0.72f, 0.79f, 0.58f) : new Color(0.36f, 0.4f, 0.34f));
            DrawRect(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), hover ? new Color(0.12f, 0.25f, 0.18f) : new Color(0.08f, 0.17f, 0.13f));
            DrawRect(new Rect(r.x + 5, r.y + 4, 3, r.height - 8), enabled ? accent : new Color(0.34f, 0.37f, 0.33f));
            bool clicked = enabled && GUI.Button(r, "", GUIStyle.none);
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = fontSize > 0 ? fontSize : (int)Mathf.Clamp(rect.height * 0.48f, 11f, 17f),
                wordWrap = false,
            };
            style.normal.textColor = enabled ? new Color(0.91f, 0.92f, 0.79f) : new Color(0.54f, 0.58f, 0.51f);
            GUI.Label(r, label, style);
            if (clicked) AddForestSparks(rect.center, accent);
            return clicked;
        }

        public static void DrawForestEffects()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            float now = Time.unscaledTime;
            for (int i = _forestSparks.Count - 1; i >= 0; i--)
            {
                var spark = _forestSparks[i];
                float t = (now - spark.Start) / 0.32f;
                if (t >= 1f) { _forestSparks.RemoveAt(i); continue; }
                float alpha = 1f - t;
                Vector2 p = spark.Origin + spark.Direction * (12f * t);
                DrawRect(new Rect(p.x - 1.5f, p.y - 1.5f, 3f, 3f), new Color(spark.Color.r, spark.Color.g, spark.Color.b, alpha));
            }
        }

        private static void AddForestSparks(Vector2 origin, Color color)
        {
            for (int i = 0; i < 7; i++)
            {
                float angle = (i / 7f) * Mathf.PI * 2f + Random.Range(-0.18f, 0.18f);
                _forestSparks.Add(new ClickSpark { Origin = origin, Direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), Start = Time.unscaledTime, Color = color });
            }
        }

        private static void DrawRect(Rect rect, Color color)
        {
            if (_whiteTex == null)
            {
                _whiteTex = new Texture2D(1, 1);
                _whiteTex.SetPixel(0, 0, Color.white);
                _whiteTex.Apply();
            }
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _whiteTex);
            GUI.color = old;
        }
    }
}
