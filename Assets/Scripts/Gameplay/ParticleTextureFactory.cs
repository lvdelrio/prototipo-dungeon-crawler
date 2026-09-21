using UnityEngine;

namespace Gameplay
{
    // Textura y material compartidos por TODOS los sistemas de particulas del juego (ambiente de
    // mazmorra + rafagas elementales de combate): un punto suave generado en runtime (sin depender
    // de ningun asset externo), con un shader de particulas elegido probando varios nombres
    // conocidos del Built-in Render Pipeline por si alguno no esta disponible en este proyecto.
    public static class ParticleTextureFactory
    {
        private static Texture2D _softDot;
        private static Material _sharedMaterial;

        public static Texture2D SoftDot
        {
            get
            {
                if (_softDot == null) _softDot = BuildSoftDot(32);
                return _softDot;
            }
        }

        public static Material SharedMaterial
        {
            get
            {
                if (_sharedMaterial == null)
                {
                    var shader = FindParticleShader();
                    _sharedMaterial = new Material(shader);
                    if (_sharedMaterial.HasProperty("_MainTex")) _sharedMaterial.SetTexture("_MainTex", SoftDot);
                    if (_sharedMaterial.HasProperty("_BaseMap")) _sharedMaterial.SetTexture("_BaseMap", SoftDot);
                }
                return _sharedMaterial;
            }
        }

        private static Shader FindParticleShader()
        {
            string[] candidates =
            {
                "Particles/Alpha Blended",
                "Legacy Shaders/Particles/Alpha Blended",
                "Mobile/Particles/Alpha Blended",
                "Particles/Standard Unlit",
                "Sprites/Default",
            };
            foreach (var name in candidates)
            {
                var shader = Shader.Find(name);
                if (shader != null) return shader;
            }
            return Shader.Find("Sprites/Default"); // ultimo recurso: siempre existe en cualquier proyecto
        }

        private static Texture2D BuildSoftDot(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var center = new Vector2(size / 2f, size / 2f);
            float maxDist = size / 2f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / maxDist;
                    float alpha = Mathf.Clamp01(1f - dist);
                    alpha *= alpha; // caida mas suave hacia el borde
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
    }
}
