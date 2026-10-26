#ifndef REMAINS_WORLD_LIGHTING_INCLUDED
#define REMAINS_WORLD_LIGHTING_INCLUDED
// Shared world light-map lookup for world artwork shaders (WorldLit, WorldTerrain).
// One implementation: room lighting (RoomLighting/RoomLightMaps) feeds these globals.
sampler2D _WorldLightMap;
sampler2D _BackgroundLightMap;
float4 _WorldLightBounds;
float _WorldLightEnabled;

float2 RemainsWorldLightUV(float2 world)
{
    return (world-_WorldLightBounds.xy)*_WorldLightBounds.zw;
}

// Light of a generated level (FactoryLevelView / LevelLightView) — the same light the walls get (RemainsLight2D.hlsl):
// ambient + direct light over the camera area + the level's bounce light.
sampler2D _RemainsRadiance;
sampler2D _RemainsIndirect;
float4 _RemainsIndirectRect;
float _RemainsIndirectEnabled;
float4 _RemainsRadianceRect;   // origin xy, size zw (m)
float _RemainsRadianceEnabled;
float4 _RemainsAmbient;        // level ambient (global light colour × intensity), set with the field

float2 RemainsRadianceUV(float2 world)
{
    return (world-_RemainsRadianceRect.xy)/_RemainsRadianceRect.zw;
}

// background: 0 world map, 1 background map; emission: 0 lit, 1 unlit.
fixed3 RemainsWorldLight(float2 lightUV,float2 radianceUV,float background,float emission)
{
    fixed3 light;
    if(_RemainsRadianceEnabled>0.5)
    {
        float inside=all(radianceUV>=0) && all(radianceUV<=1)?1:0;
        light=_RemainsAmbient.rgb+inside*tex2Dlod(_RemainsRadiance,float4(radianceUV,0,0)).rgb;
        if(_RemainsIndirectEnabled>0.5)
        {
            float2 indirectUV=(radianceUV*_RemainsRadianceRect.zw+_RemainsRadianceRect.xy-_RemainsIndirectRect.xy)/_RemainsIndirectRect.zw;
            if(all(indirectUV>=0) && all(indirectUV<=1)) light+=tex2Dlod(_RemainsIndirect,float4(indirectUV,0,0)).rgb;
        }
    }
    else
    {
        fixed3 sampled=lerp(tex2D(_WorldLightMap,lightUV).rgb,tex2D(_BackgroundLightMap,lightUV).rgb,background);
        light=lerp(fixed3(1,1,1),sampled,_WorldLightEnabled);
    }
    return lerp(light,fixed3(1,1,1),emission);
}
#endif
