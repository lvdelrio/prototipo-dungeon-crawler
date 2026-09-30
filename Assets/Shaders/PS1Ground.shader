// Piso del bosque (ver Gameplay/GroundTileFactory + DungeonLevelBuilder.BuildFloorTile): sin
// especular ni normal map -- todo el brillo es 0 a proposito, la superficie tiene que verse
// completamente mate/chata como en un juego de PS1, nunca un reflejo suave de "roughness moderno".
// _SnapScale cuantiza la posicion en espacio de mundo antes de proyectar: ese es el wobble clasico
// de vertices de PS1 (falta de precision subpixel), visible como pequeñas costuras/temblores entre
// celdas vecinas en vez de bordes perfectos. 0 = desactivado.
//
// _MainTex (pedido puntual, "poner material de grass pine forest" -- ver GroundTileFactory.
// GrassGroundMaterial): OPCIONAL, blanco 1x1 por defecto (_MainTex ("...", 2D) = "white" {}), asi
// que tex2D(...) = (1,1,1,1) y col = _Color * 1 = _Color de siempre para cualquier tile que NO le
// asigne una textura real -- cero cambio visual en piso/dirt/path existentes. Cuando si hay
// textura, se muestrea por POSICION DE MUNDO (worldXZ * _TexScale), no por UV del mesh -- asi se
// repite continua a lo largo de todo el piso sin importar donde empiece/termine cada celda
// individual, mismo criterio que Custom/VoidWater/StarlitFloor para evitar costuras entre tiles.
Shader "Custom/PS1Ground"
{
    Properties
    {
        _Color ("Color base", Color) = (0.16, 0.27, 0.14, 1)
        _SnapScale ("Vertex Snap (mundo, 0 = desactivado)", Float) = 0.05
        _MainTex ("Textura opcional (world-space)", 2D) = "white" {}
        _TexScale ("Repeticiones por unidad de mundo", Float) = 0.15
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
            sampler2D _MainTex;
            float _TexScale;
            float _WorldNightAmount;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 worldXZ : TEXCOORD0;
                fixed3 shade : COLOR0;
                UNITY_FOG_COORDS(1)
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                if (_SnapScale > 0.0)
                    worldPos = floor(worldPos / _SnapScale + 0.5) * _SnapScale;
                o.vertex = UnityWorldToClipPos(worldPos);
                o.worldXZ = worldPos.xz;

                float3 worldNormal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                float topLight = saturate(worldNormal.y * 0.5 + 0.5);
                o.shade = lerp(fixed3(0.55, 0.55, 0.55), fixed3(1, 1, 1), topLight);
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.worldXZ * _TexScale);
                fixed4 col = fixed4(_Color.rgb * tex.rgb * i.shade, 1.0);
                float night = saturate(_WorldNightAmount);
                col.rgb *= lerp(1.0, 0.48, night);
                col.rgb = lerp(col.rgb, col.rgb * fixed3(0.48, 0.58, 0.9), night * 0.64);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
