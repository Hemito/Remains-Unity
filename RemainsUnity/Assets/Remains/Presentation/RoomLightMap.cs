using System;
using System.Collections.Generic;
using System.Threading;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    public static class RoomLightMap
    {
        public static Texture2D Build(RoomLayout room,Rect bounds,RoomLightProfile profile)
        {
            ValidateBounds(room.id,bounds);
            var prepared=Prepare(room.id,profile,new MaskCache());
            var occlusion=RoomLightMaps.CaptureOcclusion(room.platforms);
            int width=Mathf.CeilToInt(bounds.width*8),height=Mathf.CeilToInt(bounds.height*8);
            var colors=new Color[width*height];
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
                colors[y*width+x]=Evaluate(occlusion,prepared,Position(bounds,width,height,x,y));
            var result=new Texture2D(width,height,TextureFormat.RGBA32,false,true) {name="Свет: "+room.id,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            result.SetPixels(colors);result.Apply(false,true);return result;
        }
        public static Color Sample(RoomLayout room,RoomLightProfile profile,Vector2 position)
            =>Evaluate(RoomLightMaps.CaptureOcclusion(room.platforms),Prepare(room.id,profile,new MaskCache()),position);

        internal static void ValidateBounds(string room,Rect bounds)
        {
            if(!float.IsFinite(bounds.x) || !float.IsFinite(bounds.y) || !float.IsFinite(bounds.width) || !float.IsFinite(bounds.height) ||
                bounds.width<=0 || bounds.height<=0 || bounds.width>256 || bounds.height>256)
                throw new InvalidOperationException($"Недопустимый размер карты света: room={room}, bounds={bounds}.");
        }
        internal static Vector2 Position(Rect bounds,int width,int height,int x,int y)
            =>new(bounds.x+(x+.5f)*bounds.width/width,bounds.y+(y+.5f)*bounds.height/height);

        // All resource access and colour-space selection happen during preparation.
        internal static PreparedProfile Prepare(string room,RoomLightProfile profile,MaskCache cache)
        {
            if(profile?.lights==null) throw new InvalidOperationException($"Нет источников света: room={room}.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            var lights=new PreparedLight[profile.lights.Length];
            for(int i=0;i<lights.Length;i++)
            {
                var light=profile.lights[i];
                if(light==null || string.IsNullOrEmpty(light.id) || !ids.Add(light.id) || !float.IsFinite(light.x) || !float.IsFinite(light.y) ||
                    !float.IsFinite(light.radius) || light.radius<=0 || !float.IsFinite(light.intensity) || light.intensity<0 ||
                    light.shadow && (light.intensity>1 || string.IsNullOrEmpty(light.mask) && light.maskTexture==null))
                    throw new InvalidOperationException($"Некорректный источник света: room={room}, light={light?.id}.");
                RoomLightMask mask=null;
                var footprint=new Rect(light.x-light.radius,light.y-light.radius,light.radius*2,light.radius*2);
                if(!string.IsNullOrEmpty(light.mask) || light.maskTexture!=null)
                {
                    var texture=cache.Texture(light);
                    footprint=new Rect(light.maskX,light.maskY,light.maskWidth,light.maskHeight);
                    mask=new RoomLightMask(cache.Pixels(texture),footprint);
                }
                lights[i]=new PreparedLight(new Vector2(light.x,light.y),light.radius,light.intensity,light.color,light.shadow,mask,footprint);
            }
            return new PreparedProfile(profile.ambient,lights,QualitySettings.activeColorSpace==ColorSpace.Linear);
        }
        // This evaluator is shared by full builds, public sampling and worker patches.
        internal static Color Evaluate(RoomLightMaps.Occlusion occlusion,PreparedProfile profile,Vector2 position,CancellationToken cancellation=default)
        {
            Color result=profile.Ambient;float shadowTransmission=1;
            foreach(var light in profile.Lights)
            {
                cancellation.ThrowIfCancellationRequested();
                if(light.Shadow)
                {
                    shadowTransmission*=1-Mathf.Clamp01(light.Mask.Sample(position).a*light.Intensity);
                    continue;
                }
                float distance=Vector2.Distance(position,light.Origin);
                if(light.Mask!=null)
                {
                    var contribution=light.Mask.Sample(position)*light.Intensity;
                    if(contribution.r==0 && contribution.g==0 && contribution.b==0) continue;
                    if(!Blocked(occlusion,light.Origin,position,cancellation)) result+=contribution;
                }
                else
                {
                    if(distance>=light.Radius) continue;
                    if(Blocked(occlusion,light.Origin,position,cancellation)) continue;
                    float falloff=1-distance/light.Radius;
                    result+=light.Color*(light.Intensity*falloff*falloff);
                }
            }
            // GammaToLinearSpace is a thread-safe FreeFunction in the selected Unity build.
            float attenuation=profile.Linear?Mathf.GammaToLinearSpace(shadowTransmission):shadowTransmission;
            result.r=Mathf.Clamp01(result.r)*attenuation;result.g=Mathf.Clamp01(result.g)*attenuation;result.b=Mathf.Clamp01(result.b)*attenuation;result.a=1;return result;
        }
        private static bool Blocked(RoomLightMaps.Occlusion occlusion,Vector2 start,Vector2 end,CancellationToken cancellation)
        {
            for(int i=0;i<occlusion.Count;i++)
            {cancellation.ThrowIfCancellationRequested();if(BlocksSegment(occlusion[i],start,end)) return true;}
            return false;
        }
        internal static bool BlocksSegment(Rect rect,Vector2 start,Vector2 end)
        {
            // Surface pixels receive light; only an obstacle strictly between points occludes.
            if(rect.Contains(start) || rect.Contains(end)) return false;
            float near=0,far=1;var delta=end-start;
            return Slab(start.x,delta.x,rect.xMin,rect.xMax,ref near,ref far) &&
                Slab(start.y,delta.y,rect.yMin,rect.yMax,ref near,ref far) && near<far && near<.999f && far>.001f;
        }
        private static bool Slab(float start,float delta,float min,float max,ref float near,ref float far)
        {
            if(Mathf.Abs(delta)<.00001f) return start>=min && start<=max;
            float a=(min-start)/delta,b=(max-start)/delta;
            near=Mathf.Max(near,Mathf.Min(a,b));far=Mathf.Min(far,Mathf.Max(a,b));return near<=far;
        }
        internal sealed class MaskCache
        {
            private readonly Dictionary<string,Texture2D> resources=new(StringComparer.Ordinal);
            private readonly Dictionary<Texture2D,RoomLightMask.Pixels> pixels=new();
            internal int Count=>pixels.Count;
            internal long PixelCount {get;private set;}
            internal Texture2D Texture(RoomLight light)
            {
                if(light.maskTexture!=null) return light.maskTexture;
                if(!resources.TryGetValue(light.mask,out var texture))
                {
                    texture=Resources.Load<Texture2D>(light.mask);
                    if(texture==null) throw new InvalidOperationException($"Нет маски света: light={light.id}, resource={light.mask}.");
                    resources.Add(light.mask,texture);
                }
                return texture;
            }
            internal RoomLightMask.Pixels Pixels(Texture2D texture)
            {
                if(!pixels.TryGetValue(texture,out var result))
                {
                    result=new RoomLightMask.Pixels(texture);pixels.Add(texture,result);
                    PixelCount+=(long)result.Width*result.Height;
                }
                return result;
            }
        }
        internal sealed class PreparedProfile
        {
            internal readonly Color Ambient;
            internal readonly PreparedLight[] Lights;
            internal readonly bool Linear;
            internal PreparedProfile(Color ambient,PreparedLight[] lights,bool linear) {Ambient=ambient;Lights=lights;Linear=linear;}
        }
        internal readonly struct PreparedLight
        {
            internal readonly Vector2 Origin;
            internal readonly float Radius,Intensity;
            internal readonly Color Color;
            internal readonly bool Shadow;
            internal readonly RoomLightMask Mask;
            internal readonly Rect Footprint;
            internal PreparedLight(Vector2 origin,float radius,float intensity,Color color,bool shadow,RoomLightMask mask,Rect footprint)
            {Origin=origin;Radius=radius;Intensity=intensity;Color=color;Shadow=shadow;Mask=mask;Footprint=footprint;}
        }
    }
}
