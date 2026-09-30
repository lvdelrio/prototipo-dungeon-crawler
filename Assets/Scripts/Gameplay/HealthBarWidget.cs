using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    public enum BarKind { Hp, Tp }

    // Barra de vida/TP con textura (recortada de "Health Bar Asset Pack 2" de Adwit Rahman, ver
    // Assets/Resources/Sprites/UI) en vez del texto plano "HP 32/40" de antes. Reusada tanto por
    // CombatHUD (filas compactas del panel, con el numero exacto encima -- las decisiones
    // tacticas siguen necesitando el numero real) como por EnemyHealthBarHUD (barra flotante
    // arriba de cada enemigo en la escena 3D, a proposito SIN numero).
    //
    // Cada barra tiene 3 capas, de atras para adelante:
    //  1. Fondo vacio (BarEmpty) a ancho completo -- el "marco" de la barra.
    //  2. Relleno OSCURO (mismo sprite de relleno, tinte mas oscuro) que muestra el HP/TP de
    //     HACE UN INSTANTE y se "alcanza" al relleno real en TrailDuration segundos -- el clasico
    //     efecto de casi cualquier RPG (Pokemon, Persona, etc): de un vistazo se ve CUANTO se
    //     acaba de perder, no solo cuanto queda.
    //  3. Relleno REAL (color vivo), recortado por fraccion via texture coords (nunca estirado en
    //     el sentido de deformar las puntas en flecha -- se recorta que porcion de la textura se
    //     ve, no se aplasta la textura entera). Pedido puntual "animala como liquido": si el
    //     shader Custom/LiquidBar esta disponible, el relleno real se dibuja con el (borde
    //     ondulando + brillo recorriendo la barra); si no, cae de nuevo al dibujo plano sin
    //     romper nada.
    public static class HealthBarWidget
    {
        private const float TrailDuration = 0.6f;

        private class State
        {
            public float LastSeenFraction = 1f;
            public float TrailFromFraction = 1f;
            public float TrailStartTime = -100f;
            public float CurrentTrailDisplay = 1f;
        }

        // Claves = la instancia real de CharacterStats/EnemyStats (referencia, no valor) -- los
        // personajes de la party viven toda la run, los enemigos se recrean por combate, asi que
        // el diccionario crece un poco cada pelea. Es solo estado visual chico (unos floats por
        // combatiente); el tope de abajo evita que crezca sin limite en una sesion muy larga.
        private static readonly Dictionary<object, State> _states = new Dictionary<object, State>();

        private static bool _assetsLoaded;
        private static Texture2D _fillHp, _fillTp, _emptyTex;
        private static Material _liquidMat;
        private static Texture2D _whiteTex;

        private static void EnsureAssets()
        {
            if (_assetsLoaded) return;
            _assetsLoaded = true;

            _fillHp = Resources.Load<Texture2D>("Sprites/UI/BarFillHealth");
            _fillTp = Resources.Load<Texture2D>("Sprites/UI/BarFillMana");
            _emptyTex = Resources.Load<Texture2D>("Sprites/UI/BarEmpty");

            // Igual patron que CombatFeedback.LoadEffectMaterial: Shader.Find funciona en el
            // Editor pero un build standalone puede haber "strippeado" el shader si ningun
            // Material real lo referencia -- si eso pasa, _liquidMat queda null y Draw cae solo al
            // dibujo plano de siempre (ver mas abajo), nunca rompe la barra.
            var shader = Shader.Find("Custom/LiquidBar");
            if (shader != null) _liquidMat = new Material(shader);

            _whiteTex = new Texture2D(1, 1);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();
        }

        // rect: tamano/posicion en pantalla de la barra completa (el fondo vacio ocupa esto
        // entero). key: la instancia del combatiente (para el estado del trail). showText:
        // superpone "actual/maximo" centrado -- usalo donde el numero exacto importa (CombatHUD),
        // no donde la barra es puramente ambiental (EnemyHealthBarHUD).
        public static void Draw(Rect rect, object key, int current, int max, BarKind kind, bool showText = false)
        {
            EnsureAssets();
            if (_states.Count > 500) _states.Clear(); // ver comentario de _states arriba

            float fraction = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;

            if (!_states.TryGetValue(key, out var st))
            {
                st = new State { LastSeenFraction = fraction, TrailFromFraction = fraction, CurrentTrailDisplay = fraction };
                _states[key] = st;
            }
            if (fraction < st.LastSeenFraction - 0.0005f)
            {
                // Baja de verdad: el trail arranca desde donde esta AHORA visualmente (no desde el
                // ultimo valor "real" visto) -- si un segundo golpe llega mientras el trail
                // anterior todavia estaba retrocediendo, sigue de largo en vez de saltar.
                st.TrailFromFraction = st.CurrentTrailDisplay;
                st.TrailStartTime = Time.time;
            }
            st.LastSeenFraction = fraction;

            float t = Mathf.Clamp01((Time.time - st.TrailStartTime) / TrailDuration);
            float eased = 1f - (1f - t) * (1f - t); // ease-out: rapido al arrancar, se frena al final
            st.CurrentTrailDisplay = Mathf.Lerp(st.TrailFromFraction, fraction, eased);
            if (st.CurrentTrailDisplay < fraction) st.CurrentTrailDisplay = fraction; // curacion: nunca por debajo del real

            Texture2D fillTex = kind == BarKind.Hp ? _fillHp : _fillTp;
            Color plainColor = kind == BarKind.Hp ? new Color(0.82f, 0.16f, 0.16f) : new Color(0.2f, 0.5f, 0.92f);

            // --- Capa 1: fondo vacio ---
            if (_emptyTex != null) GUI.DrawTexture(rect, _emptyTex);
            else DrawFlat(rect, new Color(0.1f, 0.1f, 0.12f));

            // --- Capa 2: trail oscuro (solo si de verdad hay diferencia visible) ---
            if (st.CurrentTrailDisplay > fraction + 0.0015f)
            {
                var trailRect = new Rect(rect.x, rect.y, rect.width * st.CurrentTrailDisplay, rect.height);
                if (fillTex != null)
                {
                    var old = GUI.color;
                    GUI.color = new Color(0.42f, 0.38f, 0.38f, 1f); // tinte oscuro, multiplicativo sobre el color real de la textura
                    GUI.DrawTextureWithTexCoords(trailRect, fillTex, new Rect(0f, 0f, st.CurrentTrailDisplay, 1f));
                    GUI.color = old;
                }
                else
                {
                    DrawFlat(trailRect, plainColor * 0.45f);
                }
            }

            // --- Capa 3: relleno real (liquido si se puede, plano si no) ---
            if (fraction > 0.001f)
            {
                var fillRect = new Rect(rect.x, rect.y, rect.width * fraction, rect.height);
                bool drewLiquid = false;
                if (fillTex != null && _liquidMat != null && Event.current != null && Event.current.type == EventType.Repaint)
                {
                    try
                    {
                        Graphics.DrawTexture(fillRect, fillTex, new Rect(0f, 0f, fraction, 1f), 0, 0, 0, 0, Color.white, _liquidMat);
                        drewLiquid = true;
                    }
                    catch
                    {
                        drewLiquid = false; // nunca dejar la barra rota por un problema del shader/material
                    }
                }
                if (!drewLiquid)
                {
                    if (fillTex != null) GUI.DrawTextureWithTexCoords(fillRect, fillTex, new Rect(0f, 0f, fraction, 1f));
                    else DrawFlat(fillRect, plainColor);
                }
            }

            if (showText)
            {
                var style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = Mathf.Clamp((int)(rect.height * 0.85f), 9, 13),
                };
                var oldColor = GUI.color;
                GUI.color = Color.white;
                // Sombra simple (1px) para que el numero se lea igual sobre relleno claro u oscuro.
                style.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
                GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), $"{current}/{max}", style);
                style.normal.textColor = Color.white;
                GUI.Label(rect, $"{current}/{max}", style);
                GUI.color = oldColor;
            }
        }

        private static void DrawFlat(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _whiteTex);
            GUI.color = old;
        }
    }
}
