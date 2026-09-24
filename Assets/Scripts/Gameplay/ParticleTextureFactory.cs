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
        private static Texture2D _leaf;
        private static Material _leafMaterial;
        private static Texture2D _beam;
        private static Material _beamMaterial;

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

        // Silueta de hoja (vesica piscis: interseccion de 2 circulos, apuntada en ambos extremos)
        // en blanco con alpha, MISMO criterio que SoftDot -- el color de verdad lo pone quien la usa
        // (AmbientParticles.main.startColor con tonos otoño), no la textura, para poder variar el
        // tono por particula sin generar una textura por color.
        public static Texture2D Leaf
        {
            get
            {
                if (_leaf == null) _leaf = BuildLeaf(32);
                return _leaf;
            }
        }

        public static Material LeafMaterial
        {
            get
            {
                if (_leafMaterial == null)
                {
                    var shader = FindParticleShader();
                    _leafMaterial = new Material(shader);
                    if (_leafMaterial.HasProperty("_MainTex")) _leafMaterial.SetTexture("_MainTex", Leaf);
                    if (_leafMaterial.HasProperty("_BaseMap")) _leafMaterial.SetTexture("_BaseMap", Leaf);
                }
                return _leafMaterial;
            }
        }

        // Franja vertical suave (nucleo brillante al centro, se apaga hacia los costados y un poco
        // arriba/abajo) para los rayos de luz de bosque (ver AmbientParticles.ConfigureLightShaftLayer):
        // se estira mucho en Y via startSize3D, asi que la textura en si solo necesita variar en X.
        public static Texture2D Beam
        {
            get
            {
                if (_beam == null) _beam = BuildBeam(32);
                return _beam;
            }
        }

        public static Material BeamMaterial
        {
            get
            {
                if (_beamMaterial == null)
                {
                    var shader = FindParticleShader();
                    _beamMaterial = new Material(shader);
                    if (_beamMaterial.HasProperty("_MainTex")) _beamMaterial.SetTexture("_MainTex", Beam);
                    if (_beamMaterial.HasProperty("_BaseMap")) _beamMaterial.SetTexture("_BaseMap", Beam);
                }
                return _beamMaterial;
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

        // Vesica piscis: la interseccion de 2 circulos desplazados horizontalmente forma una
        // silueta apuntada en ambos extremos (como una hoja u ojo), con el borde suavizado segun
        // que tan lejos esta del circulo mas ajustado -- mismo "AA a mano" que BuildSoftDot.
        private static Texture2D BuildLeaf(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float radius = size * 0.62f;
            float offset = size * 0.32f;
            var centerA = new Vector2(size / 2f - offset, size / 2f);
            var centerB = new Vector2(size / 2f + offset, size / 2f);
            float feather = size * 0.06f;

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float da = radius - Vector2.Distance(p, centerA);
                    float db = radius - Vector2.Distance(p, centerB);
                    float inside = Mathf.Min(da, db); // negativo = afuera de al menos 1 circulo
                    float alpha = Mathf.Clamp01(inside / feather);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        private static Texture2D BuildBeam(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size;
                float vertFade = Mathf.Clamp01(Mathf.Min(ny, 1f - ny) / 0.18f); // se apaga cerca de las puntas
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f - size / 2f) / (size / 2f); // -1..1
                    float horiz = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(nx)), 1.6f); // nucleo angosto
                    pixels[y * size + x] = new Color(1f, 1f, 1f, horiz * vertFade);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
    }
}
