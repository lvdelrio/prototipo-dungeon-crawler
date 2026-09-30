// Relleno "liquido" de las barras de vida/TP (ver Gameplay/HealthBarWidget.cs, pedido puntual:
// "animala como liquido"). Un solo truco, en UV, nada de geometria nueva -- se dibuja sobre un
// quad chato via Graphics.DrawTexture dentro de OnGUI: el borde superior de la textura (uv.y
// cerca de 1) ondula con el tiempo -- cualquier pixel que quede por ENCIMA de esa superficie
// ondulada se recorta (clip), dejando ver el fondo real de la barra por debajo, como si el nivel
// del liquido temblara. Pedido puntual: sin brillo (el shimmer que recorria la barra se saco).
Shader "Custom/LiquidBar"
{
    Properties
    {
        _MainTex ("Textura de relleno", 2D) = "white" {}
        _Color ("Tinte", Color) = (1,1,1,1)
        _WaveAmplitude ("Amplitud de la ola (fraccion 0-1 de la altura)", Range(0,0.4)) = 0.14
        _WaveFrequency ("Frecuencia", Float) = 16
        _WaveSpeed ("Velocidad de la ola", Float) = 3.4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float _WaveAmplitude;
            float _WaveFrequency;
            float _WaveSpeed;

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
                // Superficie liquida ondulando alrededor de uv.y = 1 - amplitud -- un pixel por
                // encima de esa linea en este instante se descarta (transparente), revelando lo
                // que haya debajo (el fondo vacio de la barra, ya dibujado antes que esto).
                float wave = sin(i.uv.x * _WaveFrequency + _Time.y * _WaveSpeed) * _WaveAmplitude;
                float surface = (1.0 - _WaveAmplitude) + wave;
                clip(surface - i.uv.y);

                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
