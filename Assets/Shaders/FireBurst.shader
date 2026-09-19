// Shader de habilidad para el elemento Fuego: una llama turbulenta que sube (ruido desplazado
// hacia -Y en el tiempo), nucleo blanco-amarillo que se funde a naranja/rojo en el borde. Solo se
// usa en golpes de HABILIDAD (ver BattleStageController.HandleEnemySkillHit); los ataques basicos
// siguen usando el anillo simple de Custom/ElementalBurst.
Shader "Custom/FireBurst"
{
    Properties
    {
        _Color ("Color", Color) = (1,0.4,0.1,1)
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

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = hash(i);
                float b = hash(i + float2(1, 0));
                float c = hash(i + float2(0, 1));
                float d = hash(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = (i.uv - 0.5) * 2.0;
                float dist = length(uv);

                float t = _Time.y * 3.0;
                float n = noise(uv * 4.0 + float2(0, -t)) * 0.5
                        + noise(uv * 8.0 + float2(1.7, -t * 1.7)) * 0.25;

                float flameShape = 1.0 - saturate(dist + n * 0.7 - 0.15);
                float life = 1.0 - _Progress;
                float alpha = saturate(flameShape * life * 1.6);

                fixed3 core = fixed3(1, 0.95, 0.7);
                fixed3 col = lerp(_Color.rgb, core, saturate(flameShape - 0.35) * 2.2);

                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}
