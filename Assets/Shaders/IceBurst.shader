// Shader de habilidad para el elemento Hielo: una silueta de cristal facetado (radio quebrado por
// sector angular) en vez de un anillo liso, con un nucleo blanco brillante -- se siente afilado y
// solido, distinto a la llama turbulenta del Fuego. Solo en golpes de HABILIDAD.
Shader "Custom/IceBurst"
{
    Properties
    {
        _Color ("Color", Color) = (0.4,0.85,1,1)
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

            float hash(float x) { return frac(sin(x * 91.345) * 47453.7); }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = (i.uv - 0.5) * 2.0;
                float dist = length(uv);
                float angle = atan2(uv.y, uv.x);

                const float sectors = 9.0;
                float sectorId = floor((angle + 3.14159265) / (3.14159265 * 2.0) * sectors);
                float facet = 0.55 + 0.45 * hash(sectorId);
                float grow = lerp(0.2, 1.05, saturate(_Progress * 1.3));
                float edge = facet * grow;

                float shape = 1.0 - smoothstep(edge - 0.06, edge, dist);
                float life = 1.0 - _Progress;
                float alpha = saturate(shape * life * 1.5);

                fixed3 core = fixed3(0.95, 1, 1);
                float facetLine = smoothstep(edge - 0.03, edge, dist); // faceta mas clara en el borde
                fixed3 col = lerp(core, _Color.rgb, 0.55 + facetLine * 0.45);

                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}
