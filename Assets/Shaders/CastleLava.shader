Shader "Custom/CastleLava"
{
    Properties
    {
        _ColorDeep ("Molten crust", Color) = (0.12, 0.018, 0.004, 1)
        _ColorHot ("Hot channels", Color) = (1, 0.23, 0.012, 1)
        _ColorCore ("Lava core", Color) = (1, 0.72, 0.12, 1)
        _Refraction ("Heat refraction", Range(0, 0.08)) = 0.028
        _FlowSpeed ("Flow speed", Float) = 0.42
        [HideInInspector] _Ripple0 ("Ripple 0", Vector) = (0, 0, -100, 0)
        [HideInInspector] _Ripple1 ("Ripple 1", Vector) = (0, 0, -100, 0)
        [HideInInspector] _Ripple2 ("Ripple 2", Vector) = (0, 0, -100, 0)
        [HideInInspector] _Ripple3 ("Ripple 3", Vector) = (0, 0, -100, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        GrabPass { "_CastleLavaBackground" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _CastleLavaBackground;
            sampler2D_float _CameraDepthTexture;
            fixed4 _ColorDeep, _ColorHot, _ColorCore;
            float _Refraction, _FlowSpeed;
            float4 _Ripple0, _Ripple1, _Ripple2, _Ripple3;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 worldXZ : TEXCOORD0;
                float4 grabPos : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float eyeDepth : TEXCOORD3;
            };

            float Impact(float2 p, float4 ripple)
            {
                float age = _Time.y - ripple.z;
                if (ripple.w <= 0 || age < 0 || age > 1.45) return 0;
                float front = distance(p, ripple.xy) - age * 1.75;
                return sin(front * 19) * exp(-abs(front) * 1.8) * saturate(1 - age / 1.45) * ripple.w;
            }

            float Height(float2 p)
            {
                float t = _Time.y * _FlowSpeed;
                float h = sin(dot(p, float2(0.9, 0.42)) * 3.4 - t) * 0.025;
                h += sin(dot(p, float2(-0.38, 0.92)) * 5.1 + t * 0.76) * 0.014;
                h += (Impact(p, _Ripple0) + Impact(p, _Ripple1) + Impact(p, _Ripple2) + Impact(p, _Ripple3)) * 0.012;
                return h;
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                world.y += Height(world.xz);
                o.worldXZ = world.xz;
                o.vertex = UnityWorldToClipPos(world);
                o.grabPos = ComputeGrabScreenPos(o.vertex);
                o.screenPos = ComputeScreenPos(o.vertex);
                o.eyeDepth = -UnityWorldToViewPos(world).z;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.worldXZ;
                float t = _Time.y * _FlowSpeed;
                float2 warp = float2(sin(p.y * 5.0 + t), cos(p.x * 4.4 - t * 0.83));
                float channels = sin(p.x * 6.1 + sin(p.y * 3.2 + t) * 1.4 - t * 1.6);
                channels += sin(p.y * 7.0 - p.x * 2.5 + t * 1.25) * 0.45;
                float crust = smoothstep(-0.2, 0.9, channels);
                float hot = smoothstep(0.55, 1.22, channels);
                float2 uv = i.grabPos.xy / i.grabPos.w;
                float refraction = _Refraction * (0.45 + crust * 0.55);
                fixed3 scene = tex2D(_CastleLavaBackground, uv + warp * refraction).rgb;
                fixed3 molten = lerp(_ColorDeep.rgb, _ColorHot.rgb, crust);
                molten = lerp(molten, _ColorCore.rgb, hot * 0.72);

                // Depth tint keeps thin edges translucent enough to read the stone basin below.
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float sceneDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screenUV));
                float thickness = saturate((sceneDepth - i.eyeDepth) * 1.2);
                float fresnel = saturate(1.0 - abs(dot(normalize(_WorldSpaceCameraPos - float3(p.x, 0, p.y)), float3(0,1,0))));
                float impactRings = max(max(abs(Impact(p, _Ripple0)), abs(Impact(p, _Ripple1))),
                    max(abs(Impact(p, _Ripple2)), abs(Impact(p, _Ripple3))));
                fixed3 color = lerp(scene, molten, 0.82 + thickness * 0.15);
                color += _ColorCore.rgb * (hot * (0.2 + crust * 0.3) + impactRings * 0.45);
                color = lerp(color, color * 1.15 + _ColorHot.rgb * 0.12, fresnel * 0.3);
                return fixed4(color, 0.9);
            }
            ENDCG
        }
    }
}
