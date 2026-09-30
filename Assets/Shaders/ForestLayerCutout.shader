// Capa de parallax con arte real (ver Gameplay/ForestParallaxWallFactory.cs): un quad transparente
// pegado enfrente de la pared de bosque, con una de las ilustraciones importadas de Assets/Sprites/
// Forest/ (paquete de Asset Store reincorporado por pedido puntual: "aplicar esta vez si parallax"
// -- las veces anteriores el "parallax" era una simulacion via shader/ruido procedural porque no
// habia arte real todavia).
//
// El parallax es real esta vez en el sentido de que hay VARIAS CAPAS FISICAS separadas (2 quads a
// distinta profundidad, ver el factory) en vez de una sola superficie -- pero el CORRIMIENTO de
// cada capa sigue siendo el mismo truco seguro que ya se valido en Custom/ForestWall: un offset de
// UV (no de vertices, no de posicion real del quad) basado en hacia donde mira la camara, lineal y
// acotado a [-_ParallaxStrength, _ParallaxStrength] (nunca division por viewTS.z, que se dispara a
// infinito mirando de canto y fue justo el tipo de bug que causo el parpadeo original documentado
// junto a DungeonLevelBuilder.BuildWall). Con WrapMode=Clamp (ver ForestLayerTextureImporter) el
// offset nunca causa un salto de textura visible en los bordes, solo estira el ultimo pixel.
//
// Borde oscuro (pedido puntual: "donde termina uno empieza el otro con una ligera franja negra de
// separacion" -- antes el quad se achicaba un poco (0.96/0.94) para no asomar por la esquina con la
// pared perpendicular vecina, y eso dejaba un hueco de ANCHO INCONSISTENTE segun el angulo/pared.
// Ahora el quad va a tamano COMPLETO (cellSize x wallHeight, sin achicar) y el borde se dibuja dentro
// del shader por UV -- un fundido a negro parejo cerca de CUALQUIER borde, mismo ancho relativo
// siempre, y como cada pared vecina dibuja su PROPIO borde, las dos juntas arman la franja de
// separacion pareja que se pidio, sin depender de que las geometrias calcen exacto.
//
// Vaiven de viento (pedido puntual: "shader ligero de movimiento a los arboles"): un offset de UV
// chico, animado con _Time y con la fase corrida segun uvBorder.x -- asi no es todo el arbol
// deslizando parejo (se veria como la imagen entera temblando en bloque), sino un vaiven mas
// organico donde no todas las partes se mueven exactamente igual. Sigue siendo puramente UV, nunca
// geometria: cero riesgo de reabrir el bug de parpadeo por geometria superpuesta.
//
// Corrimiento a verde (pedido puntual: el azul de la ilustracion original no combina con el verde
// del piso/ambientacion del bosque): _GreenShift mezcla el color muestreado con una version teñida
// de verde que preserva su luminancia (no es solo oscurecer, mantiene claros/oscuros) -- 0 = colores
// originales del arte, 1 = completamente teñido.
//
// Variacion entre paredes (pedido puntual: "que no se vean tan repetitivo" -- todas las paredes
// mostraban exactamente la misma franja de la imagen). El color de vertice (COLOR0) lleva un hash
// 0-1 distinto por pared, escrito UNA vez al crear el quad (ver ForestParallaxWallFactory -- clona
// el mesh compartido de Unity y le pinta el color, no es un MaterialPropertyBlock ni un material
// unico por pared, asi que el static batching sigue agrupando todo igual). Ese hash corre la UV de
// muestreo un tramo fijo por pared -- cada una arranca en una porcion distinta de la ilustracion,
// combinado con Clamp (ver ForestLayerTextureImporter) nunca se ve un salto/costura por el
// corrimiento, solo estira el ultimo pixel en el peor caso.
Shader "Custom/ForestLayerCutout"
{
    Properties
    {
        _MainTex ("Capa (PNG con alpha)", 2D) = "white" {}
        _Tint ("Tinte / oscurecido", Color) = (1, 1, 1, 1)
        _ParallaxStrength ("Fuerza del parallax de esta capa", Float) = 0.2
        _BorderWidth ("Ancho del borde oscuro (fraccion 0-0.2)", Range(0, 0.2)) = 0
        _SwayAmount ("Fuerza del vaiven de viento", Range(0, 0.05)) = 0.006
        _SwaySpeed ("Velocidad del vaiven", Float) = 0.6
        _GreenShift ("Corrimiento hacia verde (0-1)", Range(0, 1)) = 0.55
        _VariationRange ("Rango de variacion de UV entre paredes (0-1)", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Tint;
            float _ParallaxStrength;
            float _BorderWidth;
            float _SwayAmount;
            float _SwaySpeed;
            float _GreenShift;
            float _VariationRange;
            float _WorldNightAmount;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR0; // hash 0-1 por pared en .r, ver ForestParallaxWallFactory
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uvBorder : TEXCOORD1; // UV cruda 0-1 SIN parallax/tiling, solo para medir distancia al borde geometrico real
                float3 worldPos : TEXCOORD2;
                float3 worldNormal : TEXCOORD3;
                fixed hash : TEXCOORD4;
                UNITY_FOG_COORDS(5)
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uvBorder = v.uv;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.hash = v.color.r;
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            // VFACE: Cull Off dibuja las dos caras del quad (asi se ve la capa este del lado que
            // sea que camine el jugador, sin duplicar geometria) -- pero eso significa que la
            // MISMA normal de vertice se usa para las dos caras. Sin corregirla, el parallax se
            // corre para el lado equivocado cuando se mira desde "atras" del quad. facing<0 = cara
            // trasera, se invierte la normal para ese caso.
            fixed4 frag (v2f i, float facing : VFACE) : SV_Target
            {
                float3 N = i.worldNormal * (facing < 0 ? -1.0 : 1.0);
                float3 T = normalize(float3(N.z, 0.0, -N.x) + 1e-5);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
                float viewTS_x = dot(viewDir, T);

                // Vaiven: fase corrida por uvBorder.x (columna dentro del quad) para que no sea
                // todo el arbol moviendose parejo -- un vaiven "de viento" real tiene partes que se
                // adelantan y otras que se atrasan.
                float sway = sin(_Time.y * _SwaySpeed + i.uvBorder.x * 9.0) * _SwayAmount;

                // Variacion por pared: corrimiento FIJO (no animado, viene del hash de vertice) --
                // centrado en 0 para que el rango completo _VariationRange se reparta mitad para
                // cada lado en vez de arrancar siempre corrido hacia el mismo lado.
                float wallVariation = (i.hash - 0.5) * _VariationRange;

                float2 uv = i.uv + float2(viewTS_x * _ParallaxStrength + sway + wallVariation, 0.0);
                fixed4 col = tex2D(_MainTex, uv) * _Tint;

                // Corrimiento a verde: tiñe preservando luminancia (no es solo oscurecer un canal).
                float luma = dot(col.rgb, float3(0.299, 0.587, 0.114));
                fixed3 greenish = luma * fixed3(0.42, 0.62, 0.32);
                col.rgb = lerp(col.rgb, greenish, _GreenShift);
                float night = saturate(_WorldNightAmount);
                col.rgb *= lerp(1.0, 0.38, night);
                col.rgb = lerp(col.rgb, col.rgb * fixed3(0.42, 0.55, 0.88), night * 0.78);

                // Borde oscuro parejo (ver header): distancia UV cruda al borde mas cercano de ESTE
                // quad, fundido a negro/transparente en _BorderWidth -- geometria completa, borde
                // 100% en el shader, mismo ancho relativo en cualquier pared sin importar su tamano.
                float2 distToEdge = min(i.uvBorder, 1.0 - i.uvBorder);
                float edge = min(distToEdge.x, distToEdge.y);
                float borderFade = smoothstep(0.0, max(_BorderWidth, 0.0001), edge);
                col.a *= borderFade;

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
