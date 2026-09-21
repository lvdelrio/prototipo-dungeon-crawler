// Fondo de la escena de batalla: cielo en degrade + 3 capas de siluetas quebradas (lejos/media/
// cerca, cada una mas baja y mas oscura) con una franja de niebla en el horizonte -- la estetica
// de fondos pintados en capas de Hollow Knight, generada por completo en el shader (sin textura).
// Se aplica a un quad grande, fijo, detras de los "parantes" de la escena de batalla; al no tener
// las macros de niebla de Unity, la niebla de distancia de la escena no lo afecta (se comporta
// como un skybox).
Shader "Custom/HollowBackdrop"
{
    Properties
    {
        _SkyTop ("Sky Top", Color) = (0.05,0.05,0.15,1)
        _SkyBottom ("Sky Bottom", Color) = (0.25,0.15,0.4,1)
        _LayerFar ("Layer Far", Color) = (0.16,0.12,0.26,1)
        _LayerMid ("Layer Mid", Color) = (0.09,0.07,0.16,1)
        _LayerNear ("Layer Near", Color) = (0.04,0.03,0.08,1)
        _MistColor ("Mist Color", Color) = (0.55,0.6,0.75,0.35)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Opaque" }
        Cull Off
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _SkyTop, _SkyBottom, _LayerFar, _LayerMid, _LayerNear, _MistColor;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash(float x) { return frac(sin(x * 127.1) * 43758.5453); }

            // Silueta de "skyline" quebrado: interpola entre alturas al azar por segmento a lo
            // ancho, como una cadena de picos/ruinas distantes en vez de una linea lisa.
            float ridge(float x, float freq, float seed)
            {
                float xi = floor(x * freq + seed);
                float xf = frac(x * freq + seed);
                float a = hash(xi);
                float b = hash(xi + 1.0);
                return lerp(a, b, smoothstep(0.0, 1.0, xf));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                fixed3 col = lerp(_SkyBottom.rgb, _SkyTop.rgb, uv.y);

                float driftFar = _Time.y * 0.004;
                float driftMid = _Time.y * 0.008;
                float driftNear = _Time.y * 0.013;

                float far = ridge(uv.x + driftFar, 3.0, 10.0) * 0.3 + 0.18;
                float mid = ridge(uv.x + driftMid, 5.0, 40.0) * 0.24 + 0.09;
                float near = ridge(uv.x + driftNear, 8.0, 90.0) * 0.18 + 0.02;

                if (uv.y < far) col = _LayerFar.rgb;
                if (uv.y < mid) col = _LayerMid.rgb;
                if (uv.y < near) col = _LayerNear.rgb;

                float mistBand = smoothstep(far + 0.1, far - 0.06, uv.y) * smoothstep(-0.05, far + 0.12, uv.y);
                col = lerp(col, _MistColor.rgb, mistBand * _MistColor.a);

                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
