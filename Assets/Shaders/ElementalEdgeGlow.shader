// Brillo de borde de pantalla por elemento: en vez de tapar toda la imagen (como HitFlash), tiñe
// solo el BORDE/las esquinas segun el elemento del golpe (fuego = naranja, hielo = celeste, etc.),
// dejando el centro limpio -- el jugador siempre "ve" el elemento sin importar donde este mirando
// la accion, ademas del efecto en el mundo (particulas/shader en el enemigo). Un anillo interior
// mas brillante y pulsante (via _Time) le suma algo de vida en vez de quedar estatico.
// Se aplica como post-proceso de camara (OnRenderImage) desde Gameplay/CombatFeedback.cs.
Shader "Hidden/ElementalEdgeGlow"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _GlowColor ("Glow Color", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0,2)) = 0
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _GlowColor;
            float _Intensity;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

                float2 centered = (i.uv - 0.5) * 2.0; // -1..1 en ambos ejes
                float dist = length(centered);
                float edge = smoothstep(0.25, 1.15, dist); // 0 en el centro, 1 en las esquinas

                // Pulso sutil (no cambia si hay o no brillo, solo la intensidad visible del anillo)
                // para que el borde se sienta vivo en vez de una mascara estatica.
                float pulse = 0.85 + 0.15 * sin(_Time.y * 14.0);

                float glow = edge * saturate(_Intensity) * pulse;
                col.rgb += _GlowColor.rgb * glow * 0.9;
                col.rgb = lerp(col.rgb, _GlowColor.rgb, glow * 0.35);
                return col;
            }
            ENDCG
        }
    }
}
