// Aura sutil de piso alrededor de una escalera (ver Gameplay/DungeonLevelBuilder.
// BuildStairsFloorAura): un resplandor grande, tenue y con pulso lento sobre el piso -- para que
// se note que hay una escalera cerca desde unas ~3 celdas de distancia por un pasillo recto, antes
// de estar encima. Es geometria real (un quad chato sobre el piso), asi que las paredes la ocluyen
// como a cualquier otra cosa -- no se filtra a traves de ellas, solo por linea de vista real, igual
// que pasaria con luz de verdad. Mismo color que el marcador/baliza de esa escalera (celeste subir,
// naranja bajar, ver StairsBeacon) para que las 3 senales (marcador, columna de particulas, aura de
// piso) siempre se lean como la misma escalera.
Shader "Custom/StairsAura"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 1, 1, 1)
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

                // Pulso lento y suave: "sutil" pidio el usuario, no un flash -- apenas respira.
                float pulse = 0.8 + 0.2 * sin(_Time.y * 1.1);
                float falloff = pow(saturate(1.0 - dist), 2.5);

                // Techo de alpha bajo (0.3): sigue siendo piso, no una luz solida encima.
                float alpha = falloff * pulse * 0.3;
                return fixed4(_Color.rgb, alpha * _Color.a);
            }
            ENDCG
        }
    }
    FallBack Off
}
