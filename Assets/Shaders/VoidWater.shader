// Stylized transparent water for the built-in render pipeline. A shared GrabPass provides
// real screen-space refraction; subdivided surfaces are displaced for visible rolling waves.
Shader "Custom/VoidWater"
{
    Properties
    {
        _ColorDeep ("Deep water tint", Color) = (0.015, 0.075, 0.16, 1)
        _ColorShallow ("Shallow water tint", Color) = (0.10, 0.48, 0.57, 1)
        _ColorFoam ("Foam and ripple crests", Color) = (0.67, 0.91, 0.83, 1)
        _ColorRim ("Edge light", Color) = (0.25, 0.76, 0.78, 1)
        _RippleScale ("Wave scale", Float) = 0.9
        _RippleSpeed ("Wave speed", Float) = 0.45
        _FoamCutoff ("Foam amount", Range(0.2, 1.4)) = 0.76
        _FoamStrength ("Foam strength", Range(0, 1)) = 0.8
        _ColorSteps ("Color bands", Range(2, 6)) = 4
        _RimStrength ("Edge light strength", Range(0, 1)) = 0.35
        _WaterOpacity ("Shallow opacity", Range(0, 1)) = 0.24
        _DeepOpacity ("Deep opacity", Range(0, 1)) = 0.56
        _DepthRange ("Depth tint range", Float) = 0.7
        _RefractionStrength ("Refraction strength", Range(0, 0.12)) = 0.065

        [HideInInspector] _Ripple0 ("Ripple 0", Vector) = (0, 0, -100, 0)
        [HideInInspector] _Ripple1 ("Ripple 1", Vector) = (0, 0, -100, 0)
        [HideInInspector] _Ripple2 ("Ripple 2", Vector) = (0, 0, -100, 0)
        [HideInInspector] _Ripple3 ("Ripple 3", Vector) = (0, 0, -100, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" }
        LOD 200
        Cull Off
        ZWrite Off

        // Named GrabPass captures the opaque dungeon once and reuses it for every pool tile.
        GrabPass { "_WaterBackground" }

        Pass
        {
            Blend One Zero

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _WaterBackground;
            sampler2D_float _CameraDepthTexture;
            fixed4 _ColorDeep;
            fixed4 _ColorShallow;
            fixed4 _ColorFoam;
            fixed4 _ColorRim;
            float _RippleScale;
            float _RippleSpeed;
            float _FoamCutoff;
            float _FoamStrength;
            float _ColorSteps;
            float _RimStrength;
            float _WaterOpacity;
            float _DeepOpacity;
            float _DepthRange;
            float _RefractionStrength;
            float _WorldNightAmount;
            float4 _Ripple0;
            float4 _Ripple1;
            float4 _Ripple2;
            float4 _Ripple3;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 worldXZ : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
                float4 grabPos : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float eyeDepth : TEXCOORD4;
                UNITY_FOG_COORDS(5)
            };

            float ImpactWave(float2 worldXZ, float4 impact, out float2 slope)
            {
                slope = 0;
                float age = _Time.y - impact.z;
                if (impact.w <= 0 || age < 0 || age > 1.45) return 0;

                float2 delta = worldXZ - impact.xy;
                float distanceToImpact = length(delta);
                float front = distanceToImpact - age * 1.85;
                float envelope = exp(-abs(front) * 1.7) * saturate(1.0 - age / 1.45) * impact.w;
                float phase = front * 19.0;
                float height = sin(phase) * envelope * 0.035;
                float derivative = (cos(phase) * 19.0 - sin(phase) * 1.7 * sign(front)) * envelope * 0.035;
                slope = derivative * delta / max(distanceToImpact, 0.02);
                return height;
            }

            float ImpactCrest(float2 worldXZ, float4 impact)
            {
                float age = _Time.y - impact.z;
                if (impact.w <= 0 || age < 0 || age > 1.45) return 0;
                float distanceToImpact = distance(worldXZ, impact.xy);
                float front = distanceToImpact - age * 1.85;
                float envelope = exp(-abs(front) * 1.7) * saturate(1.0 - age / 1.45) * impact.w;
                return saturate(sin(front * 19.0) * 0.5 + 0.5) * envelope;
            }

            float BaseWaveHeight(float2 worldXZ, out float2 slope, out float a, out float b, out float c)
            {
                float2 p = worldXZ * _RippleScale;
                float t = _Time.y * _RippleSpeed;
                a = dot(p, float2(0.86, 0.50)) * 2.2 - t;
                b = dot(p, float2(-0.42, 0.91)) * 3.0 + t * 0.72;
                c = dot(p, float2(0.34, -0.94)) * 4.1 - t * 0.48;

                float height = sin(a) * 0.035 + sin(b) * 0.020 + sin(c) * 0.012;
                slope.x = _RippleScale * (cos(a) * 0.035 * 2.2 * 0.86
                    + cos(b) * 0.020 * 3.0 * -0.42 + cos(c) * 0.012 * 4.1 * 0.34);
                slope.y = _RippleScale * (cos(a) * 0.035 * 2.2 * 0.50
                    + cos(b) * 0.020 * 3.0 * 0.91 + cos(c) * 0.012 * 4.1 * -0.94);

                float2 rippleSlope;
                height += ImpactWave(worldXZ, _Ripple0, rippleSlope); slope += rippleSlope;
                height += ImpactWave(worldXZ, _Ripple1, rippleSlope); slope += rippleSlope;
                height += ImpactWave(worldXZ, _Ripple2, rippleSlope); slope += rippleSlope;
                height += ImpactWave(worldXZ, _Ripple3, rippleSlope); slope += rippleSlope;
                return height;
            }

            v2f vert (appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float2 slope;
                float a, b, c;
                worldPos.y += BaseWaveHeight(worldPos.xz, slope, a, b, c);

                o.worldXZ = worldPos.xz;
                o.viewDir = _WorldSpaceCameraPos - worldPos;
                o.vertex = UnityWorldToClipPos(worldPos);
                o.grabPos = ComputeGrabScreenPos(o.vertex);
                o.screenPos = ComputeScreenPos(o.vertex);
                o.eyeDepth = -UnityWorldToViewPos(worldPos).z;
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 slope;
                float a, b, c;
                float displacement = BaseWaveHeight(i.worldXZ, slope, a, b, c);

                float2 p = i.worldXZ * _RippleScale;
                float heightBands = sin(a) * 0.52 + sin(b) * 0.31 + sin(c) * 0.17 + displacement * 3.0;
                float depthBands = saturate(0.5 + heightBands * 0.38);
                float bandCount = max(2.0, _ColorSteps);
                depthBands = floor(depthBands * (bandCount - 1.0) + 0.5) / (bandCount - 1.0);
                fixed3 waterTint = lerp(_ColorDeep.rgb, _ColorShallow.rgb, depthBands);

                // Warped moving contours create stylized surface foam.
                float2 flow = p * 1.35;
                float t = _Time.y * _RippleSpeed;
                flow += 0.16 * float2(sin(flow.y * 2.0 - t), sin(flow.x * 1.7 + t * 0.8));
                float foamField = sin(flow.x * 3.7 + sin(flow.y * 2.3 - t) * 0.8)
                                + sin(flow.y * 4.2 - flow.x * 0.65 + t * 0.65) * 0.48;
                float foam = smoothstep(_FoamCutoff, _FoamCutoff + 0.2, foamField) * _FoamStrength;
                waterTint = lerp(waterTint, _ColorFoam.rgb, foam);

                float3 viewDir = normalize(i.viewDir);
                float3 normalWS = normalize(float3(-slope.x, 1.0, -slope.y));
                float3 normalVS = mul((float3x3)UNITY_MATRIX_V, normalWS);
                float fresnel = 1.0 - saturate(abs(dot(viewDir, normalWS)));
                float rim = smoothstep(0.18, 0.82, fresnel) * _RimStrength;
                waterTint = lerp(waterTint, _ColorRim.rgb, rim);

                // Use the opaque scene depth to tint deeper water more strongly and keep
                // shallow pools transparent enough to see their rocky bottom.
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float sceneRawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screenUV);
                float sceneEyeDepth = LinearEyeDepth(sceneRawDepth);
                float waterDepth = max(0.0, sceneEyeDepth - i.eyeDepth);
                float depthFade = saturate(waterDepth / max(_DepthRange, 0.001));
                float opacity = lerp(_WaterOpacity, _DeepOpacity, depthFade);
                waterTint = lerp(waterTint, _ColorDeep.rgb, depthFade * 0.3);

                float impactCrest = max(max(ImpactCrest(i.worldXZ, _Ripple0), ImpactCrest(i.worldXZ, _Ripple1)),
                    max(ImpactCrest(i.worldXZ, _Ripple2), ImpactCrest(i.worldXZ, _Ripple3)));
                waterTint = lerp(waterTint, _ColorFoam.rgb, impactCrest * 0.75);
                opacity = saturate(opacity + impactCrest * 0.25);

                // Distort the actual color of geometry under the surface using the wave normal.
                float2 grabUV = i.grabPos.xy / i.grabPos.w;
                float2 refractedUV = grabUV + normalVS.xy * _RefractionStrength * saturate(waterDepth * 2.0);
                refractedUV = clamp(refractedUV, float2(0.001, 0.001), float2(0.999, 0.999));
                fixed3 refractedScene = tex2D(_WaterBackground, refractedUV).rgb;

                float3 halfDir = normalize(normalize(float3(-0.45, 0.8, -0.35)) + viewDir);
                float glint = step(0.76, pow(saturate(dot(normalWS, halfDir)), 20.0)) * 0.3;
                waterTint = lerp(waterTint, _ColorFoam.rgb, glint);
                float night = saturate(_WorldNightAmount);
                waterTint *= lerp(1.0, 0.62, night);
                waterTint = lerp(waterTint, waterTint * fixed3(0.5, 0.67, 1.0), night * 0.65);

                fixed4 finalColor = fixed4(lerp(refractedScene, waterTint, opacity), 1.0);
                UNITY_APPLY_FOG(i.fogCoord, finalColor);
                return finalColor;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
