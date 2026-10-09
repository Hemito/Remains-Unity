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

// background: 0 world map, 1 background map; emission: 0 lit, 1 unlit.
fixed3 RemainsWorldLight(float2 lightUV,float background,float emission)
{
    fixed3 sampled=lerp(tex2D(_WorldLightMap,lightUV).rgb,tex2D(_BackgroundLightMap,lightUV).rgb,background);
    fixed3 light=lerp(fixed3(1,1,1),sampled,_WorldLightEnabled);
    return lerp(light,fixed3(1,1,1),emission);
}
#endif
