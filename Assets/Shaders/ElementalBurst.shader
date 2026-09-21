// Efecto de impacto elemental (sin sprite, todo shader): un anillo de energia que se expande y
// se desvanece, con un ruido procedural en el borde (mismo tipo de hash que usa Dissolve.shader)
// para que no se vea un circulo perfecto sino algo mas organico/electrico segun el elemento.
// Se renderiza sobre un quad orientado a camara (ver Gameplay/ElementalBurstEffect.cs), que anima
// _Progress de 0 a 1 y despues se destruye solo.
Shader "Custom/ElementalBurst"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Progress ("Progress (0=inicio, 1=fin)", Range(0,1)) = 0
        _NoiseScale ("Noise Scale", Float) = 18
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

            fixed4 frag (v2f i) : SV_Target
            {
                float2 centered = i.uv - 0.5;
                float dist = length(centered) * 2.0;

                // Anillo que crece con el tiempo (radio y ancho de banda cambian con _Progress).
                float ringRadius = lerp(0.15, 1.0, _Progress);
                float ringWidth = lerp(0.5, 0.08, _Progress);
                float ring = 1.0 - saturate(abs(dist - ringRadius) / ringWidth);

                // Ruido angular para que el borde no sea un circulo perfecto.
                float angle = atan2(centered.y, centered.x);
                float n = hash(float2(angle * _NoiseScale, ringRadius * 4.0));
                ring *= lerp(0.6, 1.0, n);

                float fade = 1.0 - _Progress;
                float alpha = saturate(ring) * fade;

                fixed3 col = _Color.rgb * (1.0 + ring * 0.4);
                return fixed4(col, alpha * _Color.a);
            }
            ENDCG
        }
    }
    FallBack Off
}
