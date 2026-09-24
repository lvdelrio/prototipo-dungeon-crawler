// Techo del Bioma 2 (Cueva Intergalactica, ver Gameplay/DungeonLevelBuilder.BuildCeilingTile):
// campo de estrellas procedural, sin textura (mismo criterio que el resto de los shaders del
// proyecto -- ver el hash() de Dissolve/ElementalBurst). Las estrellas se generan a partir de la
// posicion en el MUNDO (no el UV de cada tile de techo), asi el patron es continuo entre celdas en
// vez de repetirse identico tile por tile. Dos capas de densidad distinta (chicas+tenues, grandes+
// brillantes) para que no se vea un ruido uniforme sino algo con profundidad, cada una titilando a
// su propio ritmo (_Time.y + fase por estrella). Unlit y SIN aplicar niebla a proposito -- igual
// que StairsBeacon (particulas), las estrellas tienen que seguir viendose lejos, no desvanecerse
// en la niebla como el resto de la geometria (ver Custom/StarlitFloor, que comparte exactamente el
// mismo campo de estrellas para el piso).
Shader "Custom/StarrySky"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.02, 0.02, 0.07, 1)
        _StarColor ("Star Color", Color) = (0.9, 0.93, 1, 1)
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

            // Una capa de estrellas: reja de celdas de tamano 1/density, una estrella jitteada
            // adentro de cada celda, con su propio brillo/fase de titileo saliendo del hash.
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
                float small = StarLayer(i.worldXZ, _Density * 2.3, 7.0) * 0.6;
                float big = StarLayer(i.worldXZ + 37.2, _Density * 0.6, 9.0);
                float stars = saturate(small + big);

                fixed3 col = lerp(_BaseColor.rgb, _StarColor.rgb, stars);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
