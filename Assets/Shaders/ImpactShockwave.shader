// Shockwave por distorsion de pantalla (GrabPass, ver Gameplay/ImpactShockwaveEffect.cs y
// BattleStageController.shockwaveMaterial): la tecnica que le falta a los shaders de impacto mas
// viejos (ElementalBurst/ImpactBurst/SkillBurst son formas aditivas planas sobre un quad, nunca
// deforman lo que hay detras). Dos modos, elegidos por TIPO de ataque (BattleStageController.
// IsPhysicalElement), no por elemento especifico -- asi cualquier elemento nuevo encaja sin shader
// nuevo:
//   _Mode = 0 (magico: Fuego/Hielo/Rayo): anillo turbulento que se expande, nucleo caliente al inicio.
//   _Mode = 1 (fisico: Slash/Strike/Pierce): un corte recto y duro en una direccion aleatoria
//             (_SlashDir), bordes nitidos (no ruidosos) y un flash blanco instantaneo en el frame
//             de impacto.
// Se combina con Custom/ImpactSpark (silueta angular simple) para el golpe final -- ver
// HandleEnemySkillHit/HandleEnemyElementalHit en BattleStageController.
Shader "Custom/ImpactShockwave"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Progress ("Progress (0=inicio, 1=fin)", Range(0,1)) = 0
        _Mode ("Mode (0=radial Fuego, 1=tajo Golpe basico)", Range(0,1)) = 0
        _SlashDir ("Slash Direction (radianes)", Float) = 0
        _DistortStrength ("Distort Strength", Range(0, 0.3)) = 0.09
        _RingWidth ("Ring Width", Range(0.02, 0.6)) = 0.16
        _NoiseScale ("Noise Scale", Float) = 16
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100
        Cull Off
        ZWrite Off

        GrabPass { "_ShockwaveGrabTex" }

        Pass
        {
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _ShockwaveGrabTex;
            fixed4 _Color;
            float _Progress;
            float _Mode;
            float _SlashDir;
            float _DistortStrength;
            float _RingWidth;
            float _NoiseScale;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 grabUV : TEXCOORD1;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.grabUV = ComputeGrabScreenPos(o.vertex);
                return o;
            }

            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed3 SampleDistorted(float4 grabUV, float2 offset)
            {
                float4 grabR = grabUV + float4(offset * 1.5, 0, 0);
                float4 grabG = grabUV + float4(offset, 0, 0);
                float4 grabB = grabUV + float4(offset * 0.5, 0, 0);
                fixed3 c;
                c.r = tex2Dproj(_ShockwaveGrabTex, UNITY_PROJ_COORD(grabR)).r;
                c.g = tex2Dproj(_ShockwaveGrabTex, UNITY_PROJ_COORD(grabG)).g;
                c.b = tex2Dproj(_ShockwaveGrabTex, UNITY_PROJ_COORD(grabB)).b;
                return c;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 centered = i.uv - 0.5;
                float life = 1.0 - _Progress;

                if (_Mode > 0.5)
                {
                    // --- Modo tajo (golpe basico): corte recto y duro, bordes nitidos. ---
                    float s = sin(_SlashDir);
                    float c = cos(_SlashDir);
                    float2 rot = float2(centered.x * c - centered.y * s, centered.x * s + centered.y * c);

                    float bandWidth = lerp(0.12, 0.025, saturate(_Progress * 1.6));
                    float band = 1.0 - saturate(abs(rot.y) / bandWidth);
                    band = pow(saturate(band), 4.0);

                    float reach = lerp(0.05, 1.15, saturate(_Progress * 4.0));
                    float lengthMask = 1.0 - saturate((abs(rot.x) - reach) / 0.12);
                    float gash = saturate(band * lengthMask);

                    float2 pushDir = float2(-s, c) * sign(rot.y + 1e-5);
                    float2 offset = pushDir * gash * _DistortStrength * 1.4 * life;
                    fixed3 distorted = SampleDistorted(i.grabUV, offset);

                    float coreFlash = gash * saturate(1.0 - _Progress * 5.0);
                    fixed3 hot = fixed3(1, 1, 0.96);
                    fixed3 col = distorted + _Color.rgb * gash * life * 1.4 + hot * coreFlash * 1.5;

                    return fixed4(col, 1.0);
                }
                else
                {
                    // --- Modo radial (Fuego): anillo turbulento, nucleo caliente al inicio. ---
                    float dist = length(centered) * 2.0;
                    float angle = atan2(centered.y, centered.x);

                    float ringRadius = lerp(0.05, 1.05, _Progress);
                    float ringMask = 1.0 - saturate(abs(dist - ringRadius) / _RingWidth);
                    ringMask = pow(saturate(ringMask), 1.5);

                    float n1 = hash(float2(angle * _NoiseScale, ringRadius * 4.0));
                    float n2 = hash(float2(angle * _NoiseScale * 2.3 + 5.1, ringRadius * 7.0 + 1.7));
                    float wobble = lerp(0.55, 1.0, n1) * lerp(0.85, 1.15, n2);

                    float distortAmt = ringMask * wobble * _DistortStrength * life;
                    float2 dir = dist > 0.0001 ? centered / dist : float2(0, 0);
                    float2 offset = dir * distortAmt;
                    fixed3 distorted = SampleDistorted(i.grabUV, offset);

                    float core = saturate(1.0 - dist * 2.5) * saturate(1.0 - _Progress * 4.0);
                    fixed3 hot = fixed3(1, 0.85, 0.5);

                    float rim = ringMask * life;
                    fixed3 col = distorted + _Color.rgb * rim * 1.7 + hot * core * 1.6;

                    return fixed4(col, 1.0);
                }
            }
            ENDCG
        }
    }
    FallBack Off
}
