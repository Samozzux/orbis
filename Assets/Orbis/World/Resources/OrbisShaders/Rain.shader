// Pioggia: striscioline in un cubo attorno alla telecamera, che cadono e si ripetono (vertex shader).
// uv0.xy = posizione di partenza nel cubo (0..1), uv0.z = fase, uv0.w = 0/1 (estremo della striscia).
Shader "Orbis/Rain"
{
    Properties
    {
        _Intensity ("Intensità", Range(0, 1)) = 1
        _Box ("Cubo (m)", Float) = 40
        _Speed ("Velocità (m/s)", Float) = 11
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "Rain"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _Box;
                float _Speed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 uv0 : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half alpha : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 cam = _WorldSpaceCameraPos;
                float fall = frac(v.uv0.z - _Time.y * _Speed / _Box);
                float3 p = float3(v.uv0.x - 0.5, fall - 0.5, v.uv0.y - 0.5) * _Box;
                // il cubo si sposta a passi con la telecamera: le gocce restano ferme nel mondo
                float3 cell = floor(cam / _Box) * _Box;
                float3 w = cell + p + _Box * 0.5;
                w = cam + (frac((w - cam) / _Box + 0.5) - 0.5) * _Box;
                w.y -= v.uv0.w * 0.9;                // lunghezza della striscia
                w.xz += v.positionOS.xz * 0.012;     // spessore
                o.positionCS = TransformWorldToHClip(w);
                float active = step(v.uv0.z * 0.999, _Intensity);
                o.alpha = active * 0.22;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return half4(0.75, 0.8, 0.9, i.alpha); }
            ENDHLSL
        }
    }
}
