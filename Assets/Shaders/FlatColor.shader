// Color plano solido, unlit, con niebla -- usado como backdrop detras de las capas de arte de
// Custom/ForestLayerCutout (ver DungeonLevelBuilder.BuildWall/hasForestArt) para no depender del
// shader built-in "Unlit/Color" de Unity (cuyo GUID de recurso interno no se puede referenciar a
// mano con confianza sin abrir el editor a confirmarlo).
Shader "Custom/FlatColor"
{
    Properties
    {
        _Color ("Color", Color) = (0.03, 0.05, 0.06, 1)
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
            float _WorldNightAmount;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 vertex : SV_POSITION; UNITY_FOG_COORDS(0) };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = _Color;
                col.rgb *= lerp(1.0, 0.42, saturate(_WorldNightAmount));
                col.rgb = lerp(col.rgb, col.rgb * fixed3(0.3, 0.43, 0.78), saturate(_WorldNightAmount) * 0.72);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
