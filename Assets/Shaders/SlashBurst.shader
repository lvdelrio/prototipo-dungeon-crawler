// Shader de habilidad para el elemento Corte: 2 franjas diagonales finas cruzando el punto de
// impacto, como el tajo de una espada -- nada de anillo, es una silueta lineal y rapida. Solo en
// golpes de HABILIDAD.
Shader "Custom/SlashBurst"
{
    Properties
    {
        _Color ("Color", Color) = (0.85,0.9,1,1)
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

            // Distancia (con signo) de un punto a una recta que pasa por el origen con angulo
            // dado, desplazada "offset" unidades en la direccion normal.
            float lineDist(float2 uv, float angleDeg, float offset)
            {
                float rad = radians(angleDeg);
                float2 dir = float2(cos(rad), sin(rad));
                float2 normal = float2(-dir.y, dir.x);
                return dot(uv, normal) - offset;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = (i.uv - 0.5) * 2.0;
                float dist = length(uv);

                // El tajo "abre" con el progreso: arranca angosto y se estira un poco.
                float spread = lerp(0.0, 1.1, saturate(_Progress * 2.0));

                float s1 = 1.0 - smoothstep(0.05, 0.12, abs(lineDist(uv, 40.0, spread * 0.15)));
                float s2 = 1.0 - smoothstep(0.03, 0.09, abs(lineDist(uv, 40.0, -spread * 0.35)));
                float within = 1.0 - smoothstep(0.85, 1.05, dist);

                float shape = saturate((s1 + s2 * 0.7) * within);
                float life = 1.0 - saturate(_Progress * 1.3);
                float alpha = saturate(shape * life * 1.7);

                fixed3 col = lerp(_Color.rgb, fixed3(1, 1, 1), s1 * 0.6);
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}
