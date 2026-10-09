#ifndef REMAINS_LIGHT_2D_INCLUDED
#define REMAINS_LIGHT_2D_INCLUDED
// Shared 2D lighting of Remains world shaders (WorldTerrain, WorldSprite), one implementation:
// ambient = URP blend style 0 (global light); lamps = the radiance field (RoomRadianceField) when
// enabled, otherwise URP blend style 1 "Lamps" (multiply). Include after SHAPE_LIGHT(0..3).
#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightVariables.hlsl"

TEXTURE2D(_RemainsRadiance); SAMPLER(sampler_RemainsRadiance);
float4 _RemainsRadianceRect;   // origin xy, size zw (m)
float _RemainsRadianceTexel;
float _RemainsRadianceEnabled;

half2 RemainsLightingUV(float2 world)
{
    float4 clip=TransformWorldToHClip(float3(world,0));
    return half2(ComputeScreenPos(clip/clip.w).xy);
}

bool RemainsLampsAvailable()
{
    #if USE_SHAPE_LIGHT_TYPE_1
    return true;
    #else
    return _RemainsRadianceEnabled>0.5;
    #endif
}

half4 RemainsSampleLamp(float2 world)
{
    if(_RemainsRadianceEnabled>0.5)
        return SAMPLE_TEXTURE2D_LOD(_RemainsRadiance,sampler_RemainsRadiance,(world-_RemainsRadianceRect.xy)/_RemainsRadianceRect.zw,0);
    #if USE_SHAPE_LIGHT_TYPE_1
    return SAMPLE_TEXTURE2D(_ShapeLightTexture1,sampler_ShapeLightTexture1,RemainsLightingUV(world));
    #else
    return 0;
    #endif
}

// Same sum as URP CombinedShapeLightShared (no mask channels): ambient + the given lamp light.
half4 RemainsCombineLight(half3 albedo,half alpha,half2 lightingUV,half4 lamp)
{
    half4 modulate=0, additive=0;
    #if USE_SHAPE_LIGHT_TYPE_0
    half4 l0=SAMPLE_TEXTURE2D(_ShapeLightTexture0,sampler_ShapeLightTexture0,lightingUV);
    modulate+=l0*_ShapeLightBlendFactors0.x; additive+=l0*_ShapeLightBlendFactors0.y;
    #endif
    if(_RemainsRadianceEnabled>0.5) modulate+=lamp;   // the field multiplies like the "Lamps" style
    #if USE_SHAPE_LIGHT_TYPE_1
    else { modulate+=lamp*_ShapeLightBlendFactors1.x; additive+=lamp*_ShapeLightBlendFactors1.y; }
    #endif
    #if !USE_SHAPE_LIGHT_TYPE_0 && !USE_SHAPE_LIGHT_TYPE_1
    if(_RemainsRadianceEnabled<0.5) modulate=1;
    #endif
    half4 color=_HDREmulationScale*(half4(albedo,1)*modulate+additive);
    return half4(max(0,color.rgb),alpha);
}
#endif
