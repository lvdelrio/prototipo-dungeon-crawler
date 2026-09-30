// Procedural canopy for the forest ceiling. Openings show a warm daytime sky or a deep night sky;
// broad leaf masses and dappled light use world coordinates so adjacent ceiling tiles stay seamless.
Shader "Custom/ForestCanopy"
{
    Properties
    {
        _LeafScale ("Canopy scale", Float) = 0.62
        _OpeningAmount ("Sky openings", Range(0.1, 0.8)) = 0.38
        _DappleStrength ("Dappled sunlight", Range(0, 1)) = 0.42
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            float _LeafScale;
            float _OpeningAmount;
            float _DappleStrength;
            float _WorldNightAmount;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 worldXZ : TEXCOORD0;
                float3 normal : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(cell);
                float b = hash21(cell + float2(1, 0));
                float c = hash21(cell + float2(0, 1));
                float d = hash21(cell + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.vertex = UnityWorldToClipPos(worldPos);
                o.worldXZ = worldPos.xz;
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.worldXZ * _LeafScale;
                float broad = valueNoise(p) * 0.72 + valueNoise(p * 2.3 + 17.4) * 0.28;
                float leafCoverage = smoothstep(_OpeningAmount - 0.08, _OpeningAmount + 0.2, broad);
                float leafVariation = valueNoise(p * 3.8 + 41.7);
                float night = saturate(_WorldNightAmount);

                fixed3 daySky = fixed3(0.58, 0.67, 0.43);
                fixed3 nightSky = fixed3(0.018, 0.035, 0.105);
                fixed3 sky = lerp(daySky, nightSky, night);
                fixed3 dayLeaf = lerp(fixed3(0.045, 0.12, 0.035), fixed3(0.18, 0.29, 0.08), leafVariation);
                fixed3 nightLeaf = lerp(fixed3(0.008, 0.023, 0.027), fixed3(0.025, 0.075, 0.065), leafVariation);
                fixed3 leaves = lerp(dayLeaf, nightLeaf, night);

                float sunlight = smoothstep(0.52, 0.88, broad) * _DappleStrength * (1.0 - night);
                leaves += fixed3(0.23, 0.19, 0.075) * sunlight;

                float stars = step(0.994, hash21(floor(i.worldXZ * 2.1))) * (1.0 - leafCoverage) * night;
                fixed3 color = lerp(sky, leaves, leafCoverage);
                color += fixed3(0.32, 0.42, 0.72) * stars;
                float faceLight = lerp(0.78, 1.0, saturate(abs(i.normal.y)));
                fixed4 outputColor = fixed4(color * faceLight, 1.0);
                UNITY_APPLY_FOG(i.fogCoord, outputColor);
                return outputColor;
            }
            ENDCG
        }
    }
    FallBack Off
}
