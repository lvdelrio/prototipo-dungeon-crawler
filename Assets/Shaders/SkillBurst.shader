// Shader UNICO y compartido para el impacto de CUALQUIER habilidad: un doble anillo (uno un poco
// mas grande y desfasado que el otro) con un borde ligeramente ruidoso, mas elaborado que el
// anillo simple de los ataques basicos (Custom/ElementalBurst) pero SIN una forma distinta por
// elemento -- la diferencia entre habilidades es solo el COLOR (_Color, ver Gameplay/
// ElementVisuals.cs), nunca la silueta. Antes cada elemento tenia su propio shader con una forma
// completamente distinta (llama/cristal/rayo/corte/grieta/puas); en la practica, como cada
// personaje de esta party usa un elemento distinto, eso se sentia como "cada PERSONAJE tiene su
// propio efecto" en vez de "cada HABILIDAD tiene su color" -- este shader unico corrige eso.
Shader "Custom/SkillBurst"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Progress ("Progress (0=inicio, 1=fin)", Range(0,1)) = 0
        _NoiseScale ("Noise Scale", Float) = 14
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
            float _NoiseScale;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Ring(float dist, float angle, float radius, float width, float noiseOffset)
            {
                float ring = 1.0 - saturate(abs(dist - radius) / width);
                float n = hash(float2(angle * _NoiseScale + noiseOffset, radius * 4.0));
                return ring * lerp(0.65, 1.0, n);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 centered = i.uv - 0.5;
                float dist = length(centered) * 2.0;
                float angle = atan2(centered.y, centered.x);

                // Doble anillo: el segundo un poco mas chico y con un leve retraso de fase, para
                // que se sienta con mas cuerpo que un anillo simple sin ser una forma distinta.
                float radiusA = lerp(0.15, 1.0, _Progress);
                float widthA = lerp(0.45, 0.09, _Progress);
                float ringA = Ring(dist, angle, radiusA, widthA, 0.0);

                float progressB = saturate(_Progress * 1.15 - 0.05);
                float radiusB = lerp(0.1, 0.78, progressB);
                float widthB = lerp(0.3, 0.07, progressB);
                float ringB = Ring(dist, angle, radiusB, widthB, 8.3) * 0.6;

                float ring = saturate(ringA + ringB);
                float fade = 1.0 - _Progress;
                float alpha = saturate(ring) * fade;

                fixed3 col = _Color.rgb * (1.0 + ring * 0.35);
                return fixed4(col, alpha * _Color.a);
            }
            ENDCG
        }
    }
    FallBack Off
}
