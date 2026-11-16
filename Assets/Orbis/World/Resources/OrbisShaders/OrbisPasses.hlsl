// Pass ombre e profondità comuni (vertici con posizione e normale, instancing facoltativo).
#ifndef ORBIS_PASSES_INCLUDED
#define ORBIS_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

float3 _LightDirection;
float3 _LightPosition;

struct OrbisDepthAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 uv0 : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct OrbisDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS : TEXCOORD0;
};

#ifdef ORBIS_VERTEX_OFFSET
// lo shader può spostare i vertici (vento delle piante): stessa funzione in tutti i pass
float3 OrbisDisplace(float3 positionWS, float4 uv0);
#else
float3 OrbisDisplace(float3 positionWS, float4 uv0) { return positionWS; }
#endif

OrbisDepthVaryings OrbisShadowVertex(OrbisDepthAttributes input)
{
    OrbisDepthVaryings o;
    UNITY_SETUP_INSTANCE_ID(input);
    float3 positionWS = OrbisDisplace(TransformObjectToWorld(input.positionOS.xyz), input.uv0);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    o.positionCS = ApplyShadowClamping(positionCS);
    o.normalWS = normalWS;
    return o;
}

OrbisDepthVaryings OrbisDepthVertex(OrbisDepthAttributes input)
{
    OrbisDepthVaryings o;
    UNITY_SETUP_INSTANCE_ID(input);
    float3 positionWS = OrbisDisplace(TransformObjectToWorld(input.positionOS.xyz), input.uv0);
    o.positionCS = TransformWorldToHClip(positionWS);
    o.normalWS = TransformObjectToWorldNormal(input.normalOS);
    return o;
}

half4 OrbisDepthFragment(OrbisDepthVaryings i) : SV_Target { return 0; }

half4 OrbisDepthNormalsFragment(OrbisDepthVaryings i) : SV_Target
{
    return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
}

#endif
