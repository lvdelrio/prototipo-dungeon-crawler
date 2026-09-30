Shader "Custom/CastleLavafall"
{
    Properties { _Color ("Lavafall", Color) = (1, 0.28, 0.025, 0.9) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 p = i.uv;
                p.x += sin(p.y * 15.0 - t * 2.4) * 0.025 + sin(p.y * 7.0 + t) * 0.018;
                float stream = sin(p.x * 34.0 + sin(p.y * 8.0 - t * 2.0) * 2.1 + t * 1.8);
                float hotVein = smoothstep(0.55, 0.88, stream);
                float crust = smoothstep(-0.65, -0.05, stream + sin(p.y * 21.0 + t * 1.35) * 0.35);
                float edge = smoothstep(0.0, 0.13, i.uv.x) * smoothstep(1.0, 0.87, i.uv.x);
                float falling = 0.7 + 0.3 * sin(i.uv.y * 22.0 - t * 6.0);
                fixed3 color = lerp(float3(0.32, 0.035, 0.004), _Color.rgb, crust);
                color = lerp(color, float3(1.0, 0.72, 0.12), hotVein * 0.88);
                return fixed4(color * falling * 1.15, _Color.a * edge * (0.72 + hotVein * 0.28));
            }
            ENDCG
        }
    }
}
