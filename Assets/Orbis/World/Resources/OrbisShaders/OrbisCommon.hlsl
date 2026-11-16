// Funzioni comuni degli shader del mondo (stile "Classico", docs/08 §3).
#ifndef ORBIS_COMMON_INCLUDED
#define ORBIS_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// impostati da DayNightCycle: 0 = giorno, 1 = notte piena
float _OrbisNight;
// 0..1 bagnato (pioggia)
float _OrbisWet;

float OrbisHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float OrbisNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float a = OrbisHash21(i);
    float b = OrbisHash21(i + float2(1, 0));
    float c = OrbisHash21(i + float2(0, 1));
    float d = OrbisHash21(i + float2(1, 1));
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

half3 OrbisSRGB(half3 c) { return SRGBToLinear(c); }

// luce principale "morbida" + ambiente dalle armoniche sferiche, con ombre in cascata
half3 OrbisLighting(float3 positionWS, half3 normalWS, half3 albedo, half ao, half specular)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light light = GetMainLight(shadowCoord);
    half ndl = saturate((dot(normalWS, light.direction) + 0.15h) / 1.15h);
    half shadow = light.shadowAttenuation * light.distanceAttenuation;
    half3 direct = albedo * light.color * ndl * shadow;
    half3 ambient = albedo * SampleSH(normalWS) * ao;
    half3 color = direct + ambient;
    if (specular > 0)
    {
        float3 viewDir = normalize(GetWorldSpaceViewDir(positionWS));
        half3 h = normalize(light.direction + viewDir);
        half spec = pow(saturate(dot(normalWS, h)), 64.0h) * specular * shadow;
        color += light.color * spec;
    }
    return color;
}

#endif
