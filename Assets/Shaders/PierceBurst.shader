// Shader de habilidad para el elemento Perforacion: muchas puas finas y afiladas disparando hacia
// afuera del punto de impacto (silueta de "erizo"), rapidas y angostas -- distinto de todos los
// demas, que son mas anchos/redondeados. Solo en golpes de HABILIDAD.
Shader "Custom/PierceBurst"
{
    Properties
    {
        _Color ("Color", Color) = (0.8,1,0.7,1)
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

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = (i.uv - 0.5) * 2.0;
                float dist = length(uv);
                float angle = atan2(uv.y, uv.x);

                float spikes = pow(abs(cos(angle * 9.0)), 10.0);
                float reach = lerp(0.15, 1.1, saturate(_Progress * 1.6));
                float shape = spikes * (1.0 - smoothstep(reach * 0.7, reach, dist));

                float centerCore = 1.0 - smoothstep(0.0, 0.12, dist);
                shape = saturate(shape + centerCore);

                float life = 1.0 - _Progress;
                float alpha = saturate(shape * life * 1.7);

                fixed3 col = lerp(_Color.rgb, fixed3(1, 1, 0.95), centerCore);
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}
