// Pared de bosque de canas/tallos finos con luz calida entrando desde arriba (pedido puntual, ver
// referencia visual pasada por el usuario: Labyrinth of Refrain -- tallos parejos y delgados, NO
// troncos con forma de arbol/pelota, fondo dorado/ambar difuminado detras, borde calido de luz de
// un lado de cada tallo, rayos de luz diagonales suaves). Sin geometria de mas ni desplazamiento
// de vertices en ningun momento (nada de vertex.y += sway como Custom/PS1Grass): la version
// original de este shader (Custom/ForestWallParallax) se saco del proyecto por un parpadeo que
// tardo varias rondas en diagnosticarse y al final resulto ser geometria de pared/piso/techo
// superpuesta (ver el comentario junto a DungeonLevelBuilder.BuildWall), no el shader -- asi que
// ESTE shader sigue sin tocar un solo vertice: las 3 capas de profundidad de tallos (lejos/medio/
// cerca) son pura ilusion de textura, corridas horizontalmente en el fragment shader segun hacia
// donde mira la camara.
//
// 2 cosas separadas para que el corrimiento de parallax NUNCA se sienta como jitter:
// 1) El corrimiento depende de la DIRECCION DE VISTA (viewTS_x mas abajo), nunca de _Time -- si la
//    camara esta quieta, el patron de tallos esta 100% quieto, pixel por pixel.
// 2) Es un offset LINEAL (viewTS_x * profundidad), no la formula clasica de parallax mapping con
//    division por viewTS.z -- esa division se dispara a infinito mirando casi de canto a la pared,
//    justo el tipo de bug que causaria temblor violento en las esquinas de los corredores.
//    viewTS_x ya esta acotado a [-1,1] (producto punto de dos vectores unitarios), asi que el
//    offset lineal nunca se dispara.
// La silueta de cada tallo (StalkColumn mas abajo) tambien es 100% estatica en el tiempo -- ni su
// ancho ni su posicion dependen de _Time. Los rayos de luz diagonales (mas abajo en frag) tambien
// son estaticos por el mismo motivo. Lo unico animado es un brillo muy sutil sobre un tallo ya
// decidido.
Shader "Custom/ForestWall"
{
    Properties
    {
        _BackdropColorTop ("Fondo: luz filtrada de la copa", Color) = (0.42, 0.55, 0.34, 1)
        _BackdropColorBottom ("Fondo: mas oscuro/verdoso cerca del piso", Color) = (0.09, 0.09, 0.05, 1)
        _StalkColorDark ("Tallo: lado en sombra", Color) = (0.07, 0.16, 0.07, 1)
        _StalkColorWarm ("Tallo: borde iluminado", Color) = (0.48, 0.58, 0.31, 1)
        _GlowHeight ("Altura del resplandor (mundo, cerca del techo)", Float) = 2.8
        _StalkFrequency ("Frecuencia de tallos (capa cercana, mundo)", Float) = 0.85
        _StalkWidth ("Multiplicador de ancho de tallo", Range(0.3, 1.5)) = 0.7
        _ParallaxStrength ("Fuerza del parallax (mundo)", Float) = 1.8
        _RimStrength ("Fuerza de la luz de borde calida", Range(0,1)) = 0.85
        _RayStrength ("Fuerza de los rayos de luz diagonales", Range(0,1)) = 0.4
        _SwayStrength ("Fuerza del brillo sutil por tallo", Range(0,0.5)) = 0.12
        _SwaySpeed ("Velocidad de ese brillo", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _BackdropColorTop;
            fixed4 _BackdropColorBottom;
            fixed4 _StalkColorDark;
            fixed4 _StalkColorWarm;
            float _GlowHeight;
            float _StalkFrequency;
            float _StalkWidth;
            float _ParallaxStrength;
            float _RimStrength;
            float _RayStrength;
            float _SwayStrength;
            float _SwaySpeed;
            float _WorldNightAmount;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                fixed3 shade : COLOR0;
                UNITY_FOG_COORDS(2)
            };

            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldNormal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));

                // Sombreado plano por normal, mas suave que PS1Ground/PS1Grass a proposito (0.78 en
                // vez de 0.55): la referencia es calida y luminosa, no el clima oscuro de una cueva
                // -- este vertex shader nunca toca v.vertex, solo lo proyecta tal cual.
                float topLight = saturate(o.worldNormal.y * 0.5 + 0.5);
                o.shade = lerp(fixed3(0.78, 0.76, 0.7), fixed3(1, 1, 0.98), topLight);
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            // Un tallo/cana FINA Y PAREJA (no un tronco con copa) a lo largo de `u` (ya con el
            // corrimiento de parallax de su capa aplicado), todo el alto de la pared. edgeSoftness
            // mas grande = borde mas difuso (asi se simula el desenfoque de una capa lejana sin un
            // pase de blur de verdad). Luz de borde CALIDA siempre del mismo lado (izquierda, `du`
            // negativo) -- todos los tallos comparten el mismo lado iluminado, como en la
            // referencia, en vez de cada uno iluminado al azar. Silueta 100% estatica en el tiempo;
            // solo el brillo final respira un poco (shimmer).
            fixed3 StalkColumn(float u, float worldY, float freq, float seed, float edgeSoftness, float brightnessMul, out float coverage)
            {
                float spacing = 1.0 / max(freq, 0.001);
                float cellIndex = floor(u / spacing);
                float2 rnd = hash2(float2(cellIndex, seed));
                float cellCenterU = (cellIndex + 0.5) * spacing;
                float du = u - cellCenterU;

                float halfWidth = lerp(spacing * 0.14, spacing * 0.24, rnd.x) * _StalkWidth;
                float edge = spacing * edgeSoftness;
                coverage = 1.0 - smoothstep(halfWidth - edge, halfWidth + edge, abs(du));

                float rim = saturate(-du / max(halfWidth, 0.0001));
                rim = smoothstep(0.15, 1.0, rim) * _RimStrength;

                float tone = lerp(0.72, 1.0, rnd.y);
                fixed3 col = lerp(_StalkColorDark.rgb, _StalkColorWarm.rgb, rim) * tone;

                float shimmerPhase = rnd.y * 6.2831;
                float shimmer = sin(_Time.y * _SwaySpeed + shimmerPhase) * 0.5 + 0.5;
                col *= lerp(1.0 - _SwayStrength * 0.5, 1.0 + _SwayStrength * 0.5, shimmer);

                return col * brightnessMul;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Marco tangente de esta cara (paredes son cubos axis-aligned: la normal solo puede
                // ser +-X o +-Z en las 4 caras visibles) -- T queda horizontal a lo largo de la
                // pared. dot(worldPos, T) da una coordenada continua que sigue calzando entre dos
                // segmentos de pared vecinos de la MISMA orientacion (sin costura).
                float3 N = i.worldNormal;
                float3 T = normalize(float3(N.z, 0.0, -N.x) + 1e-5);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
                float viewTS_x = dot(viewDir, T);

                float uBase = dot(i.worldPos, T);
                float worldY = i.worldPos.y;

                // Fondo: gradiente vertical calido (resplandor arriba, cerca de _GlowHeight, mas
                // oscuro/verdoso abajo) -- la sensacion de "luz de sol filtrando desde el techo del
                // bosque" que tiene la referencia, sin necesitar ninguna luz real de escena (este
                // shader es unlit a proposito, ver header).
                float glow = saturate(worldY / max(_GlowHeight, 0.01));
                fixed3 col = lerp(_BackdropColorBottom.rgb, _BackdropColorTop.rgb, glow);

                // Rayos de luz diagonales, suaves y ESTATICOS (ver nota de jitter en el header):
                // bandas anchas en diagonal que se atenuan hacia el piso, como luz de sol filtrando
                // entre las canas. pow(...,4) angosta las bandas para que no se vean como rayas
                // parejas de arcoiris sino como haces sueltos.
                float rayCoord = i.worldPos.x * 0.35 + worldY;
                float rays = pow(saturate(sin(rayCoord * 2.2) * 0.5 + 0.5), 4.0) * _RayStrength * glow;
                col += _BackdropColorTop.rgb * rays;

                // 3 capas de profundidad (lejos/medio/cerca), MUY separadas entre si a proposito: la
                // lejana usa un borde bien difuso (edgeSoftness alto) y se funde bastante con el
                // fondo -- simula desenfoque de distancia sin un pase de blur real. La cercana tiene
                // borde nitido y su propio corrimiento de parallax es el mas fuerte de las 3.
                float far    = uBase - viewTS_x * _ParallaxStrength * 0.15;
                float mid    = uBase - viewTS_x * _ParallaxStrength * 0.5;
                float near_  = uBase - viewTS_x * _ParallaxStrength * 1.15;

                float covFar;
                fixed3 colFar = StalkColumn(far, worldY, _StalkFrequency * 2.6, 11.0, 0.9, 0.55, covFar);
                colFar = lerp(colFar, col, 0.45);
                col = lerp(col, colFar, covFar);

                float covMid;
                fixed3 colMid = StalkColumn(mid, worldY, _StalkFrequency * 1.6, 47.0, 0.35, 0.78, covMid);
                col = lerp(col, colMid, covMid);

                float covNear;
                fixed3 colNear = StalkColumn(near_, worldY, _StalkFrequency * 1.0, 91.0, 0.08, 1.0, covNear);
                col = lerp(col, colNear, covNear);

                float night = saturate(_WorldNightAmount);
                col *= lerp(1.0, 0.34, night);
                col = lerp(col, col * fixed3(0.38, 0.52, 0.9), night * 0.8);
                fixed4 finalColor = fixed4(col * i.shade, 1.0);
                UNITY_APPLY_FOG(i.fogCoord, finalColor);
                return finalColor;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
