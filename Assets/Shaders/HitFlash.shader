// Efecto de pantalla completa usado como feedback de impacto en combate: mezcla la imagen
// renderizada con un color solido segun _Intensity (1 = color solido, 0 = imagen normal). Se
// aplica como post-proceso de camara (OnRenderImage) desde Gameplay/CombatFeedback.cs.
Shader "Hidden/HitFlash"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0,1)) = 0
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
            fixed4 _FlashColor;
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
                col.rgb = lerp(col.rgb, _FlashColor.rgb, saturate(_Intensity) * _FlashColor.a);
                return col;
            }
            ENDCG
        }
    }
}
