// Vista dei lotti: celle colorate (colore dai vertici) con il bordo della cella disegnato dallo shader.
Shader "Orbis/Overlay"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+5" }
        Pass
        {
            Name "Overlay"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -2, -4
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float4 uv0 : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv0.xy;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 d = min(i.uv, 1 - i.uv);
                float2 w = max(fwidth(i.uv), 1e-4);
                float edge = 1 - saturate(min(d.x / w.x, d.y / w.y) - 0.5);
                half4 c = i.color;
                c.rgb = lerp(c.rgb, c.rgb * 0.35, edge);
                c.a = lerp(c.a, min(1, c.a * 2.2), edge);
                return c;
            }
            ENDHLSL
        }
    }
}
