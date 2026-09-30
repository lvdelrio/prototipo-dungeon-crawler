// Silueta de "spark" (ver Gameplay/ImpactSparkEffect.cs y BattleStageController.sparkMaterial)
// basada en referencia pixel-art de hit-effects: cunas triangulares de borde RECTO (ancho que baja
// linealmente hasta 0 en la punta), asi se ve anguloso/filoso en vez de redondeado como una version
// anterior descartada (lobulos cos^n). Dos siluetas, elegidas por TIPO de ataque
// (BattleStageController.IsPhysicalElement), para que un golpe fisico nunca se confunda con uno
// magico:
//   _Mode = 1 (fisico: Slash/Strike/Pierce): cruz de 4 puntas identicas a 45/135/225/315, simetrica
//             y limpia -- un "corte" geometrico.
//   _Mode = 0 (magico: Fuego/Hielo/Rayo): 6 puntas asimetricas (largo/ancho/angulo irregulares), un
//             estallido mas organico y caotico.
// Se dispara JUNTO a Custom/ImpactShockwave (el shockwave hace de "cuerpo" del golpe, esto es el
// acento anguloso encima) en TODO golpe, basico o de habilidad.
Shader "Custom/ImpactSpark"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Progress ("Progress (0=inicio, 1=fin)", Range(0,1)) = 0
        _Mode ("Mode (0=Magico, 1=Slash)", Range(0,1)) = 0
        _Rotation ("Rotation (radianes)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100
        Cull Off
        ZWrite Off
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Progress;
            float _Mode;
            float _Rotation;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float2 Rotate(float2 p, float ang)
            {
                float s = sin(ang);
                float c = cos(ang);
                return float2(p.x * c - p.y * s, p.x * s + p.y * c);
            }

            // Cuna triangular de borde recto: 1.0 pegado al eje de la punta y en el nacimiento,
            // se afina LINEALMENTE hasta 0 justo en la punta -- esto es lo que da el borde recto y
            // anguloso (nada de curvas), a diferencia de un lobulo cos^n que siempre redondea.
            float TriangleSpike(float2 p, float angleRad, float spikeLen, float halfWidth, float edgeSoft)
            {
                float2 local = Rotate(p, -angleRad);
                if (local.x < 0.0 || local.x > spikeLen) return 0.0;
                float t = local.x / spikeLen;
                float width = halfWidth * (1.0 - t);
                return 1.0 - smoothstep(width - edgeSoft, width, abs(local.y));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 centered = (i.uv - 0.5) * 2.0;
                float2 p = Rotate(centered, _Rotation);

                // "Pop" casi instantaneo y despues se desvanece, igual que el resto de los
                // efectos de impacto -- el golpe se nota de una, no aparece gradual.
                float grow = smoothstep(0.0, 0.12, _Progress);
                float fade = 1.0 - smoothstep(0.1, 1.0, _Progress);

                float mask = 0.0;
                if (_Mode > 0.5)
                {
                    // Cruz de 4 puntas identicas: golpe fisico, silueta limpia y simetrica.
                    const float len = 1.05;
                    const float hw = 0.09;
                    const float soft = 0.02;
                    mask = max(mask, TriangleSpike(p, 0.7854, len * grow, hw, soft));   // 45
                    mask = max(mask, TriangleSpike(p, 2.3562, len * grow, hw, soft));   // 135
                    mask = max(mask, TriangleSpike(p, 3.9270, len * grow, hw, soft));   // 225
                    mask = max(mask, TriangleSpike(p, 5.4978, len * grow, hw, soft));   // 315
                }
                else
                {
                    // Estallido de 6 puntas asimetricas: mas caotico/organico, golpe magico.
                    mask = max(mask, TriangleSpike(p, 1.35, 1.15 * grow, 0.16, 0.02));
                    mask = max(mask, TriangleSpike(p, 0.15, 0.65 * grow, 0.07, 0.015));
                    mask = max(mask, TriangleSpike(p, 2.05, 0.55 * grow, 0.06, 0.015));
                    mask = max(mask, TriangleSpike(p, 3.35, 0.80 * grow, 0.11, 0.02));
                    mask = max(mask, TriangleSpike(p, 4.55, 0.40 * grow, 0.05, 0.015));
                    mask = max(mask, TriangleSpike(p, 5.55, 0.60 * grow, 0.08, 0.018));
                }

                // Nucleo chico: cuadrado rotado 45 (rombo), no un circulo, para no reintroducir
                // redondez en el centro.
                float coreSize = 0.05 * grow;
                float core = 1.0 - smoothstep(coreSize * 0.7, coreSize, abs(p.x) + abs(p.y));

                float alpha = saturate(mask + core) * fade;
                fixed3 hot = fixed3(1.0, 0.97, 0.9);
                fixed3 col = lerp(_Color.rgb, hot, saturate(mask * 0.7 + core));

                return fixed4(col, alpha * _Color.a);
            }
            ENDCG
        }
    }
    FallBack Off
}
