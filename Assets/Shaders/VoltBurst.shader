// Shader de habilidad para el elemento Rayo: sectores angulares que "parpadean" prendidos y
// apagados a alta velocidad (como ramas de un rayo), con largos aleatorios distintos por sector --
// nada de anillo liso, se siente electrico e inestable. Solo en golpes de HABILIDAD.
Shader "Custom/VoltBurst"
{
    Properties
    {
        _Color ("Color", Color) = (1,0.92,0.2,1)
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

            float hash(float x) { return frac(sin(x * 78.233) * 39758.4); }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = (i.uv - 0.5) * 2.0;
                float dist = length(uv);
                float angle = atan2(uv.y, uv.x);

                const float sectors = 10.0;
                float sectorId = floor((angle + 3.14159265) / (3.14159265 * 2.0) * sectors);

                float flickerClock = floor(_Time.y * 24.0);
                float flicker = step(0.45, hash(sectorId * 3.17 + flickerClock));
                float boltLen = lerp(0.35, 1.15, hash(sectorId + 5.5));

                float shape = (1.0 - smoothstep(boltLen - 0.1, boltLen, dist)) * flicker;
                float centerCore = 1.0 - smoothstep(0.0, 0.18, dist); // nucleo siempre visible
                shape = saturate(shape + centerCore);

                float life = 1.0 - _Progress;
                float alpha = saturate(shape * life * 1.6);

                fixed3 col = lerp(_Color.rgb, fixed3(1, 1, 0.95), centerCore);
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}
