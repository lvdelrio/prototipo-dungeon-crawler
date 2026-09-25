// Pasto con vaiven de viento (ver Gameplay/GroundTileFactory.BuildGrassTufts): mismo lenguaje
// visual que Custom/PS1Ground (mate, sin especular, sombreado plano por normal, niebla de
// escena) pero con un desplazamiento de vertices en X que crece desde la base del pastito (peso
// 0, vertice local y=-0.5 de un Quad comun) hasta la punta (peso 1, y=+0.5) -- asi la base queda
// plantada en el piso y solo la mitad de arriba se mece, sin animar huesos ni depender de Shader
// Graph (no esta instalado en este proyecto, pipeline built-in).
Shader "Custom/PS1Grass"
{
    Properties
    {
        _Color ("Color base", Color) = (0.22, 0.42, 0.16, 1)
        _WindStrength ("Fuerza del viento", Float) = 0.06
        _WindSpeed ("Velocidad", Float) = 2.2
        _WindScale ("Escala espacial (mundo)", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Cull Off // quads finitos, no un volumen cerrado -- tiene que verse desde los dos lados

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _WindStrength;
            float _WindSpeed;
            float _WindScale;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 vertex : SV_POSITION; fixed3 shade : COLOR0; UNITY_FOG_COORDS(1) };

            v2f vert (appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                // Peso 0 en la base (y local -0.5), 1 en la punta (y local +0.5) -- la base se
                // queda plantada, la mitad de arriba se mece. Fase por posicion de mundo (no por
                // instancia) para que pastitos vecinos no se muevan todos exactamente igual.
                float weight = saturate(v.vertex.y + 0.5);
                float phase = (worldPos.x + worldPos.z) * _WindScale;
                float sway = sin(_Time.y * _WindSpeed + phase) * _WindStrength * weight;
                worldPos.x += sway;

                o.vertex = UnityWorldToClipPos(worldPos);

                float3 worldNormal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                float topLight = saturate(worldNormal.y * 0.5 + 0.5);
                o.shade = lerp(fixed3(0.6, 0.6, 0.6), fixed3(1, 1, 1), topLight);
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = fixed4(_Color.rgb * i.shade, 1.0);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
