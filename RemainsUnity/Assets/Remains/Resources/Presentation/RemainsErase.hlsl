#ifndef REMAINS_ERASE_INCLUDED
#define REMAINS_ERASE_INCLUDED
// Background erase (original decor back_<id>_e, e.g. windows): RoomDecorView renders the room's
// erase clips into one world-space mask; layers drawn before the erase (backdrop, back walls, decor
// flagged "eraseFirst") lose those pixels, so the far background shows through.
TEXTURE2D(_RemainsErase); SAMPLER(sampler_RemainsErase);
float4 _RemainsEraseRect;   // origin xy, size zw (m)
float _RemainsEraseEnabled;

half RemainsEraseKeep(float2 world,float affected)
{
    if(affected<0.5 || _RemainsEraseEnabled<0.5) return 1;
    float2 uv=(world-_RemainsEraseRect.xy)/_RemainsEraseRect.zw;
    if(any(uv<0) || any(uv>1)) return 1;
    return 1-SAMPLE_TEXTURE2D_LOD(_RemainsErase,sampler_RemainsErase,uv,0).r;
}
#endif
