// Shader de habilidad para el elemento Golpe: un flash solido en el centro con "rajaduras" que se
// extienden en unos pocos sectores angulares (como el impacto de un golpe contundente que agrieta
// el aire) -- silueta angulosa y compacta, distinta del corte lineal o el rayo electrico. Solo en
// golpes de HABILIDAD.
Shader "Custom/StrikeBurst"
{
    Properties
    {
        _Color ("Color", Color) = (1,0.75,0.4,1)
        _Progress ("Progress", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Progress;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash(float x) { return frac(sin(x * 61.19) * 27453.9); }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = (i.uv - 0.5) * 2.0;
                float dist = length(uv);
                float angle = atan2(uv.y, uv.x);

                const float sectors = 6.0;
                float sectorId = floor((angle + 3.14159265) / (3.14159265 * 2.0) * sectors);
                float crackLen = lerp(0.35, 0.95, hash(sectorId));
                float crack = 1.0 - smoothstep(crackLen - 0.06, crackLen, dist);

                float centerFlash = 1.0 - smoothstep(0.0, 0.3, dist);
                float shape = saturate(crack * 0.75 + centerFlash);

                float life = 1.0 - _Progress;
                float alpha = saturate(shape * life * 1.5);

                fixed3 col = lerp(_Color.rgb, fixed3(1, 0.98, 0.9), centerFlash);
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}
