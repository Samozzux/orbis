// Sfocatura per il "liquid glass": usata con Graphics.Blit su una copia a bassa risoluzione della scena.
// Pass 0: gaussiana a 9 campioni lungo _Direction. Pass 1: come 0 + "vibrancy" (più chiaro e saturo).
Shader "Hidden/Orbis/GlassBlur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Direction ("Direzione", Vector) = (1, 0, 0, 0)
        _Lighten ("Schiarimento", Range(0, 1)) = 0.1
        _Saturation ("Saturazione", Range(0, 3)) = 1.3
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float4 _Direction;
        float _Lighten;
        float _Saturation;

        half4 Blur(float2 uv)
        {
            float2 o = _Direction.xy * _MainTex_TexelSize.xy;
            half4 c = tex2D(_MainTex, uv) * 0.2270270270;
            c += tex2D(_MainTex, uv + o * 1.3846153846) * 0.3162162162;
            c += tex2D(_MainTex, uv - o * 1.3846153846) * 0.3162162162;
            c += tex2D(_MainTex, uv + o * 3.2307692308) * 0.0702702703;
            c += tex2D(_MainTex, uv - o * 3.2307692308) * 0.0702702703;
            return c;
        }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            half4 frag (v2f_img i) : SV_Target { return Blur(i.uv); }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            half4 frag (v2f_img i) : SV_Target
            {
                half4 c = Blur(i.uv);
                // si lavora in uno spazio "percettivo" approssimato, come il ripiego su texture (Wallpaper.Vibrancy)
                half3 g = pow(saturate(c.rgb), 1.0 / 2.2);
                half grey = dot(g, half3(0.299, 0.587, 0.114));
                g = saturate(grey + (g - grey) * _Saturation);
                g = lerp(g, 1.0, _Lighten);
                return half4(pow(g, 2.2), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
