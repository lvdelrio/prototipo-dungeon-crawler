Shader "Custom/CastleLandmark"
{
    Properties
    {
        _Color ("Color", Color) = (0.25, 0.3, 0.38, 1)
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _WorldNightAmount;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 vertex : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float night = saturate(_WorldNightAmount);
                fixed3 moonlit = _Color.rgb * fixed3(0.6, 0.72, 1.0);
                return fixed4(lerp(_Color.rgb, moonlit, night * 0.45), _Color.a);
            }
            ENDCG
        }
    }
}
