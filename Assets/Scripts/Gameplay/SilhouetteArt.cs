using UnityEngine;

namespace Gameplay
{
    // Arte de silueta 100% procedural (rects + un circulo generado en runtime), al estilo de los
    // menus de Persona 3/5: figuras planas de un solo color, sin detalle interno, que solo se leen
    // por su silueta general. El proyecto todavia no tiene assets de arte importados (ver
    // README -- todo son primitivas/particulas generadas por codigo), asi que esto le da presencia
    // visual a paneles que si no serian una lista de texto plana, con el mismo espiritu que el
    // resto del juego.
    public static class SilhouetteArt
    {
        private static Texture2D _circleTex;
        private static Texture2D _whiteTex;

        private static Texture2D CircleTex()
        {
            if (_circleTex != null) return _circleTex;
            const int size = 64;
            _circleTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r, dy = y + 0.5f - r;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    // 1px de antialiasing suave en el borde, para que no se vea un circulo pixelado.
                    float alpha = Mathf.Clamp01(r - dist);
                    _circleTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            _circleTex.Apply();
            return _circleTex;
        }

        private static Texture2D WhiteTex()
        {
            if (_whiteTex != null) return _whiteTex;
            _whiteTex = new Texture2D(1, 1);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();
            return _whiteTex;
        }

        // Un rectangulo (no aspecto libre) se ve como una elipse; con w==h, un circulo.
        public static void DrawEllipse(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, CircleTex());
            GUI.color = old;
        }

        public static void DrawRect(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, WhiteTex());
            GUI.color = old;
        }

        // Silueta de mercader: sombrero de ala ancha + cabeza + poncho (trapecio armado con
        // rebanadas horizontales, ya que IMGUI no tiene triangulos nativos) + un brazo levantado
        // ofreciendo un item -- para que la pestana de compras se sienta como una escena real
        // ("alguien vendiendo"), no una lista de precios sola.
        public static void DrawMerchant(Rect area, Color color)
        {
            float cx = area.x + area.width * 0.5f;
            float top = area.y;
            float headSize = area.width * 0.34f;

            // ala + copa del sombrero
            DrawEllipse(new Rect(cx - area.width * 0.5f, top + headSize * 0.22f, area.width, headSize * 0.3f), color);
            DrawRect(new Rect(cx - headSize * 0.28f, top, headSize * 0.56f, headSize * 0.32f), color);

            // cabeza
            DrawEllipse(new Rect(cx - headSize / 2f, top + headSize * 0.34f, headSize, headSize), color);

            // poncho (trapecio aproximado con rebanadas que se ensanchan hacia abajo)
            float bodyTop = top + headSize * 1.2f;
            float bodyH = area.y + area.height - bodyTop;
            const int slices = 12;
            for (int i = 0; i < slices; i++)
            {
                float t = i / (float)(slices - 1);
                float w = Mathf.Lerp(headSize * 0.75f, area.width * 0.95f, t);
                DrawRect(new Rect(cx - w / 2f, bodyTop + bodyH * t, w, bodyH / slices + 1f), color);
            }

            // brazo levantado con un item (cuadrado) en la mano, del lado derecho
            float armW = area.width * 0.09f;
            DrawRect(new Rect(cx + area.width * 0.26f, bodyTop - headSize * 0.15f, armW, bodyH * 0.5f), color);
            DrawRect(new Rect(cx + area.width * 0.22f, bodyTop - headSize * 0.45f, armW * 1.8f, armW * 1.8f), color);
        }

        // Silueta de guerrero con la espada en alto -- para la pestana de subir de nivel, que se
        // sienta como progreso/poder ganado en vez de una tabla de numeros sola.
        public static void DrawWarrior(Rect area, Color color)
        {
            float cx = area.x + area.width * 0.5f;
            float top = area.y;
            float headSize = area.width * 0.3f;

            DrawEllipse(new Rect(cx - headSize / 2f, top, headSize, headSize), color);

            float bodyTop = top + headSize * 0.85f;
            float bodyH = area.y + area.height - bodyTop;
            const int slices = 12;
            for (int i = 0; i < slices; i++)
            {
                float t = i / (float)(slices - 1);
                float w = Mathf.Lerp(headSize * 0.95f, headSize * 1.7f, t);
                DrawRect(new Rect(cx - w / 2f, bodyTop + bodyH * t * 0.8f, w, bodyH / slices + 1f), color);
            }

            // espada en alto, del lado derecho de la cabeza: hoja vertical + guarda horizontal
            float swordX = cx + headSize * 0.6f;
            DrawRect(new Rect(swordX, top - headSize * 0.55f, headSize * 0.13f, headSize * 1.7f), color);
            DrawRect(new Rect(swordX - headSize * 0.22f, top + headSize * 0.85f, headSize * 0.57f, headSize * 0.13f), color);
        }
    }
}
