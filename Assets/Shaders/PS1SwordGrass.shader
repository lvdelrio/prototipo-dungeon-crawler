// Pasto chico estilo Zelda ("hojas de espada", ver Gameplay/GroundTileFactory.BuildSwordGrass):
// mismo vaiven de viento que Custom/PS1Grass (base plantada, la mitad de arriba se mece, fase por
// posicion de mundo para que no se muevan todas igual) pero en vez de un Quad rectangular liso,
// esto recorta la carta con clip() en el fragment shader para que se vea como una hoja angosta que
// termina en punta -- ancha en la base, afilada arriba, como una espadita saliendo del pasto -- en
// vez de agregar un mesh nuevo (este proyecto no tiene ninguno, todo Quad/Cube + shader).
Shader "Custom/PS1SwordGrass"
{
    Properties
    {
        _Color ("Color base", Color) = (0.24, 0.46, 0.18, 1)
        _TipColor ("Color de la punta", Color) = (0.4, 0.62, 0.22, 1)
        _WindStrength ("Fuerza del viento", Float) = 0.05
        _WindSpeed ("Velocidad", Float) = 2.6
        _WindScale ("Escala espacial (mundo)", Float) = 0.9
        _BaseWidth ("Ancho en la base (0-0.5)", Float) = 0.22
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Cull Off // carta finita, no un volumen cerrado -- tiene que verse desde los dos lados

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _TipColor;
            float _WindStrength;
            float _WindSpeed;
            float _WindScale;
            float _BaseWidth;
            float4 _GrassInteractor;
            float _WorldNightAmount;

            // _Color por instancia -- mismo patron que PS1Grass (ver FoliageManager.cs).
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed3 shade : COLOR0;
                UNITY_FOG_COORDS(1)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.uv = v.uv;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                // Peso 0 en la base (y local -0.5 = uv.y 0), 1 en la punta (y local +0.5 = uv.y 1)
                // -- identico criterio que PS1Grass. Fase por posicion de mundo, no por instancia.
                float weight = saturate(v.vertex.y + 0.5);
                float phase = (worldPos.x + worldPos.z) * _WindScale;
                float sway = sin(_Time.y * _WindSpeed + phase) * _WindStrength * weight;
                worldPos.x += sway;

                float2 playerDelta = worldPos.xz - _GrassInteractor.xz;
                float playerDistance = length(playerDelta);
                float bend = _GrassInteractor.w > 0.001 ? saturate(1.0 - playerDistance / _GrassInteractor.w) : 0.0;
                bend *= bend * weight;
                worldPos.xz += normalize(playerDelta + float2(0.0001, 0.0001)) * bend * 0.38;

                o.vertex = UnityWorldToClipPos(worldPos);

                float3 worldNormal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                float topLight = saturate(worldNormal.y * 0.5 + 0.5);
                o.shade = lerp(fixed3(0.6, 0.6, 0.6), fixed3(1, 1, 1), topLight);
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                // Silueta de hoja de espada: ancho maximo (_BaseWidth) en uv.y=0, se angosta
                // linealmente hasta 0 en uv.y=1 -- clip() descarta todo pixel fuera de ese
                // triangulo, dejando solo la forma afilada visible (el resto del Quad es
                // transparente de verdad, no solo pintado del color de fondo).
                float halfWidth = _BaseWidth * (1.0 - i.uv.y);
                clip(halfWidth - abs(i.uv.x - 0.5));

                fixed4 color = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                fixed3 tint = lerp(color.rgb, _TipColor.rgb, i.uv.y);
                float night = saturate(_WorldNightAmount);
                tint *= lerp(1.0, 0.5, night);
                tint = lerp(tint, tint * fixed3(0.48, 0.62, 0.95), night * 0.58);
                fixed4 col = fixed4(tint * i.shade, 1.0);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
