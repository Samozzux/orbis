// Cielo a gradiente (stile Classico): zenit, orizzonte e terra impostati da SkyController,
// disco e alone del sole, stelle di notte. Usato come skybox.
Shader "Orbis/Sky"
{
    Properties
    {
        _Zenith ("Zenit", Color) = (0.25, 0.48, 0.85, 1)
        _Horizon ("Orizzonte", Color) = (0.72, 0.82, 0.92, 1)
        _Ground ("Sotto l'orizzonte", Color) = (0.6, 0.66, 0.72, 1)
        _SunColor ("Sole", Color) = (1, 0.95, 0.85, 1)
        _SunDir ("Direzione del sole", Vector) = (0, 1, 0, 0)
        _SunSize ("Dimensione del sole", Float) = 0.012
        _Glow ("Alone", Float) = 0.35
        _Stars ("Stelle", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith;
                half4 _Horizon;
                half4 _Ground;
                half4 _SunColor;
                float4 _SunDir;
                float _SunSize;
                float _Glow;
                float _Stars;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                half3 col = h >= 0
                    ? lerp(_Horizon.rgb, _Zenith.rgb, pow(saturate(h), 0.55))
                    : lerp(_Horizon.rgb, _Ground.rgb, saturate(-h * 6.0));
                float3 s = normalize(_SunDir.xyz);
                float cosA = dot(d, s);
                // alone attorno al sole e disco
                col += _SunColor.rgb * _Glow * pow(saturate(cosA), 12.0);
                col += _SunColor.rgb * 0.25 * _Glow * pow(saturate(cosA), 3.0) * saturate(1 - h * 2);
                float disk = smoothstep(1 - _SunSize, 1 - _SunSize * 0.7, cosA);
                col = lerp(col, _SunColor.rgb * 2.5, disk * step(-0.02, s.y));
                // stelle
                if (_Stars > 0.01 && h > 0)
                {
                    float3 cell = floor(d * 220.0);
                    float star = step(0.9975, Hash(cell)) * _Stars * saturate(h * 4);
                    col += star * 0.9;
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
