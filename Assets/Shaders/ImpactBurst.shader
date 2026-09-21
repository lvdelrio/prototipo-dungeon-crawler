// "Hit spark" estilo juego de pelea (Tekken 8 y similares): un flash blanco muy breve en el
// centro (el "frame de impacto") mas puntas radiales que se disparan hacia afuera y se afinan
// rapido, como una estrella de golpe seca -- no una explosion lenta. Complementa (no reemplaza) al
// anillo mas suave y duradero de Custom/ElementalBurst: ese anillo comunica el ELEMENTO del golpe,
// este comunica el PESO/impacto en si, y por eso se dispara en CUALQUIER golpe (basico o de
// habilidad) ademas del anillo -- ver Gameplay/ImpactBurstEffect.cs.
Shader "Custom/ImpactBurst"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Progress ("Progress (0=inicio, 1=fin)", Range(0,1)) = 0
        _SpikeCount ("Spike Count", Float) = 10
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
            float _SpikeCount;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 centered = i.uv - 0.5;
                float dist = length(centered) * 2.0;
                float angle = atan2(centered.y, centered.x);

                // Puntas radiales tipo "estrella de impacto": patron angular que forma picos finos
                // y se estira hacia afuera con el tiempo (golpe seco, no una explosion lenta).
                float spikes = abs(frac(angle / 6.28318 * _SpikeCount) - 0.5) * 2.0; // 0 en el pico, 1 entre picos
                float spikeShape = pow(saturate(1.0 - spikes), 6.0);
                float reach = lerp(0.15, 1.1, saturate(_Progress * 2.2));
                float spikeMask = spikeShape * saturate(1.0 - dist / reach);

                // Nucleo blanco muy breve en el centro: el "flash" del frame de impacto, se apaga
                // casi de inmediato (a diferencia del anillo elemental, que dura toda la animacion).
                float coreLife = saturate(1.0 - _Progress * 4.0);
                float core = saturate(1.0 - dist * 3.0) * coreLife;

                float life = 1.0 - _Progress;
                float alpha = saturate((spikeMask * 0.9 + core * 0.75) * life);

                fixed3 hot = fixed3(1, 1, 0.95);
                fixed3 col = lerp(_Color.rgb, hot, saturate(core + spikeMask * 0.3));

                return fixed4(col, alpha * _Color.a);
            }
            ENDCG
        }
    }
    FallBack Off
}
