// Cortina de agua cayendo, para la boveda de cascada (pedido puntual, ver
// Gameplay/DungeonLevelBuilder.BuildWaterfallDecor). Simple a proposito (nada de grab pass ni
// refraccion como Custom/VoidWater, que esta pensado para una superficie horizontal vista desde
// arriba): franjas verticales que se desplazan con el tiempo dan sensacion de caida, con los
// bordes laterales atenuados para que no se lea como un rectangulo solido pegado en la puerta.
// Sin collider en el GameObject que la usa: es decorativa, el jugador camina a traves.
Shader "Custom/Waterfall"
{
    Properties
    {
        _Color ("Color", Color) = (0.55, 0.82, 0.88, 0.55)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100
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

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float scroll = frac(i.uv.y * 5.0 - _Time.y * 1.6);
                float streaks = smoothstep(0.0, 0.5, scroll) * smoothstep(1.0, 0.5, scroll);
                float edgeFade = smoothstep(0.0, 0.14, i.uv.x) * smoothstep(1.0, 0.86, i.uv.x);

                fixed4 col = _Color;
                col.rgb += streaks * 0.22;
                col.a *= (0.55 + streaks * 0.45) * edgeFade;
                return col;
            }
            ENDCG
        }
    }
}
