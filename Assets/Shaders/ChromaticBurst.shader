// Aberracion cromatica de pantalla completa en el instante del golpe (ver referencia: el
// "fantasma" rojo/azul que se ve en los impactos de anime de pelea tipo Sparking Zero) -- separa
// los canales R/G/B radialmente desde el centro de pantalla, mas fuerte cuanto mayor _Intensity.
// Post-proceso de camara (OnRenderImage) desde Gameplay/CombatFeedback.cs, mismo patron que
// Hidden/HitFlash y Hidden/ElementalEdgeGlow.
Shader "Hidden/ChromaticBurst"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Intensity", Range(0,1)) = 0
        _Amount ("Max offset (UV)", Float) = 0.02
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
            float _Intensity;
            float _Amount;

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
                float2 dir = i.uv - float2(0.5, 0.5);
                float2 offset = dir * _Amount * saturate(_Intensity);

                fixed4 col;
                col.r = tex2D(_MainTex, i.uv + offset).r;
                col.g = tex2D(_MainTex, i.uv).g;
                col.b = tex2D(_MainTex, i.uv - offset).b;
                col.a = tex2D(_MainTex, i.uv).a;
                return col;
            }
            ENDCG
        }
    }
}
