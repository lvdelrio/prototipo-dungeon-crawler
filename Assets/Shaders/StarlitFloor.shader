// Piso del Bioma 2 (Cueva Intergalactica, ver Gameplay/DungeonLevelBuilder.BuildFloorTile): roca
// oscura con el MISMO campo de estrellas que Custom/StarrySky (identico hash/StarLayer, mismo
// mapeo por posicion de MUNDO) pero mucho mas tenue/chico -- para que se lea como el reflejo
// apagado de las estrellas del techo en vez de un piso identico al techo. Pedido puntual: que las
// luces del cielo "de alguna manera se vean desde el suelo en el que caminamos", sin duplicar un
// techo entero mirando para abajo.
Shader "Custom/StarlitFloor"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.05, 0.05, 0.08, 1)
        _StarColor ("Star Color", Color) = (0.55, 0.7, 1, 1)
        _Density ("Star Density", Float) = 0.9
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
            #include "UnityCG.cginc"

            fixed4 _BaseColor;
            fixed4 _StarColor;
            float _Density;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float2 worldXZ : TEXCOORD0; float4 vertex : SV_POSITION; };

            v2f vert (appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldXZ = worldPos.xz;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            // Misma funcion que Custom/StarrySky.StarLayer -- tiene que quedar igual para que la
            // reja de estrellas del piso coincida en escala/posicion con la del techo.
            float StarLayer(float2 worldXZ, float density, float sharpness)
            {
                float2 scaled = worldXZ * density;
                float2 cell = floor(scaled);
                float2 local = frac(scaled) - 0.5;
                float2 rnd = hash2(cell);
                float2 starPos = (rnd - 0.5) * 0.75;
                float d = length(local - starPos);
                float star = pow(saturate(1.0 - d * sharpness), 10.0);
                float twinkle = 0.5 + 0.5 * sin(_Time.y * (1.2 + rnd.y * 2.5) + rnd.x * 6.2831);
                float brightness = lerp(0.4, 1.0, rnd.y);
                return star * lerp(0.5, 1.0, twinkle) * brightness;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Solo la capa "grande" del techo, y bien atenuada: un reflejo apagado, no una
                // copia -- el piso sigue leyendose como piso, con apenas un brillo salteado.
                float stars = StarLayer(i.worldXZ + 37.2, _Density * 0.6, 9.0) * 0.35;

                fixed3 col = lerp(_BaseColor.rgb, _StarColor.rgb, saturate(stars));
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
