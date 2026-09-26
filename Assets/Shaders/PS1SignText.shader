// Texto de los carteles de bienvenida (ver Gameplay/DungeonLevelBuilder.BuildSignText): el
// material de fuente por defecto que Unity le pone a un TextMesh es semitransparente con
// ZWrite Off (cola Transparent) -- eso lo deja "flotando" visible por encima de cualquier pared
// en vez de taparse detras de ella como cualquier geometria opaca. Este shader lee la MISMA
// textura de fuente (alfa = forma de la letra) pero en la cola AlphaTest, que SI escribe
// profundidad -- el texto vuelve a ocluirse normal contra paredes/objetos solidos. El color viene
// del vertex color que TextMesh ya graba solo (TextMesh.color), no de una property aparte.
Shader "Custom/PS1SignText"
{
    Properties
    {
        _MainTex ("Textura de fuente", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; UNITY_FOG_COORDS(1) };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 texcol = tex2D(_MainTex, i.uv);
                clip(texcol.a - 0.5);
                fixed4 col = i.color;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
