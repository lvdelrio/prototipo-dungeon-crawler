// Piso del bosque (ver Gameplay/GroundTileFactory + DungeonLevelBuilder.BuildFloorTile): sin
// especular ni normal map -- todo el brillo es 0 a proposito, la superficie tiene que verse
// completamente mate/chata como en un juego de PS1, nunca un reflejo suave de "roughness moderno".
// _SnapScale cuantiza la posicion en espacio de mundo antes de proyectar: ese es el wobble clasico
// de vertices de PS1 (falta de precision subpixel), visible como pequeñas costuras/temblores entre
// celdas vecinas en vez de bordes perfectos. 0 = desactivado.
Shader "Custom/PS1Ground"
{
    Properties
    {
        _Color ("Color base", Color) = (0.16, 0.27, 0.14, 1)
        _SnapScale ("Vertex Snap (mundo, 0 = desactivado)", Float) = 0.05
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _SnapScale;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            // UNITY_FOG_COORDS/UNITY_TRANSFER_FOG/UNITY_APPLY_FOG (abajo): sin esto el piso
            // ignoraba por completo la niebla lineal de la escena (RenderSettings.fog, la que
            // oscurece la mazmorra a partir de ~2-3 celdas) -- techo/paredes/marcadores la reciben
            // gratis por venir del shader Standard, pero un CGPROGRAM a mano no la aplica solo.
            struct v2f { float4 vertex : SV_POSITION; fixed3 shade : COLOR0; UNITY_FOG_COORDS(1) };

            v2f vert (appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                if (_SnapScale > 0.0)
                    worldPos = floor(worldPos / _SnapScale + 0.5) * _SnapScale;
                o.vertex = UnityWorldToClipPos(worldPos);

                // Sombreado plano por normal (tapa clara, costados mas oscuros): reemplaza la
                // iluminacion suave de Standard sin agregar especular ni sombras reales -- barato y
                // suficiente para leer el volumen de los props chatos (hojas, rocas).
                float3 worldNormal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                float topLight = saturate(worldNormal.y * 0.5 + 0.5);
                o.shade = lerp(fixed3(0.55, 0.55, 0.55), fixed3(1, 1, 1), topLight);
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
