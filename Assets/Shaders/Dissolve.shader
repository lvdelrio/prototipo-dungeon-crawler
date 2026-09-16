// Shader "animacion falsa" para los enemigos de la escena de batalla: como el prototipo no
// tiene clips de animacion reales, esto simula tanto la reaccion al golpe (pulso corto) como la
// muerte (disolucion completa) via Gameplay/EnemyView.cs, que anima _DissolveAmount de 0 a 1.
// El ruido para el recorte es procedural (hash de la posicion en mundo), sin textura externa.
Shader "Custom/Dissolve"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _DissolveAmount ("Dissolve Amount", Range(0,1)) = 0
        _EdgeColor ("Edge Color", Color) = (1,0.55,0.1,1)
        _EdgeWidth ("Edge Width", Range(0.001,0.3)) = 0.08
        _NoiseScale ("Noise Scale", Float) = 6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert addshadow
        #pragma target 3.0

        fixed4 _Color;
        float _DissolveAmount;
        fixed4 _EdgeColor;
        float _EdgeWidth;
        float _NoiseScale;

        struct Input
        {
            float3 worldPos;
        };

        float hash(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            float n = hash(floor(IN.worldPos * _NoiseScale));
            clip(n - _DissolveAmount);

            float edge = smoothstep(0.0, _EdgeWidth, n - _DissolveAmount);
            o.Albedo = lerp(_EdgeColor.rgb, _Color.rgb, edge);
            o.Emission = _EdgeColor.rgb * (1.0 - edge) * _EdgeColor.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
