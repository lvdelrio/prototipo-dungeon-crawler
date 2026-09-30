Shader "Custom/BossLightning"
{
    Properties
    {
        _Color ("Lightning Color", Color) = (0.68, 0.82, 1, 1)
        _Progress ("Fade Progress", Range(0, 1)) = 0
        _Seed ("Flicker Seed", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Progress;
            float _Seed;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // U esta definido a mano de borde a borde en las cintas: nucleo blanco fino y un
                // halo azul que cae suavemente hacia los laterales.
                float edge = abs(i.uv.x * 2.0 - 1.0);
                float coverage = 1.0 - smoothstep(0.58, 1.0, edge);
                float core = 1.0 - smoothstep(0.035, 0.24, edge);
                float halo = pow(saturate(1.0 - edge), 2.0);

                float segment = floor(i.uv.y * 38.0 + _Seed);
                float noise = frac(sin(segment * 127.1 + _Seed * 17.7) * 43758.5453);
                float flicker = lerp(0.55, 1.0, step(0.12, noise));
                flicker *= 0.84 + 0.16 * sin(_Time.y * 48.0 + _Seed + i.uv.y * 31.0);

                float fade = 1.0 - smoothstep(0.08, 1.0, _Progress);
                float tipFade = smoothstep(0.0, 0.08, i.uv.y) * (1.0 - smoothstep(0.91, 1.0, i.uv.y));
                float alpha = coverage * fade * tipFade * flicker * _Color.a;

                float3 color = lerp(_Color.rgb, float3(1.0, 1.0, 1.0), core * 0.96);
                color *= 0.58 * halo + 1.7 * core;
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
