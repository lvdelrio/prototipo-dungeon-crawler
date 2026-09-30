// Procedural material for the exposed sides of sunken forest pools. Mottled earthy stone with
// softened noisy bands, shallow cracks, a damp waterline, and vertex-colored variation per rim.
Shader "Custom/PitRockEdge"
{
    Properties
    {
        _RockDark ("Shadow earth", Color) = (0.09, 0.085, 0.07, 1)
        _RockMid ("Mossy stone", Color) = (0.27, 0.23, 0.17, 1)
        _RockLight ("Warm stone", Color) = (0.43, 0.36, 0.25, 1)
        _NoiseScale ("Rock mottling", Float) = 8
        _CrackScale ("Fine rocky grain", Float) = 24
        _CrackStrength ("Grain contrast", Range(0, 1)) = 0.28
        _DampStrength ("Waterline dampness", Range(0, 1)) = 0.42
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 150
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _RockDark;
            fixed4 _RockMid;
            fixed4 _RockLight;
            float _NoiseScale;
            float _CrackScale;
            float _CrackStrength;
            float _DampStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 normal : TEXCOORD2;
                fixed seed : TEXCOORD3;
                UNITY_FOG_COORDS(4)
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
                o.uv = v.uv;
                o.worldPos = worldPos;
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.seed = v.color.r;
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv * float2(_NoiseScale, _NoiseScale * 0.65) + i.seed * 37.17;
                float broad = valueNoise(p) * 0.58 + valueNoise(p * 2.13 + 7.1) * 0.29 + valueNoise(p * 4.7 + 13.8) * 0.13;
                float grain = valueNoise(i.uv * float2(_CrackScale, _CrackScale * 0.72) + i.seed * 83.4);
                float crack = 1.0 - smoothstep(0.025, 0.13, abs(grain - 0.5));

                fixed3 rock = lerp(_RockDark.rgb, _RockMid.rgb, smoothstep(0.12, 0.72, broad));
                rock = lerp(rock, _RockLight.rgb, smoothstep(0.68, 0.96, broad) * 0.48);
                rock *= 1.0 - crack * _CrackStrength * 0.55;

                // La línea de agua humedece y oscurece el borde bajo sin convertirlo en una franja plana.
                float waterline = 1.0 - smoothstep(0.04, 0.2, abs(i.uv.y - 0.5));
                rock = lerp(rock, rock * fixed3(0.56, 0.68, 0.72), waterline * _DampStrength);
                float light = lerp(0.62, 1.0, saturate(i.normal.y * 0.5 + 0.5));
                fixed4 col = fixed4(rock * light, 1);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
