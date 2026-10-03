using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    public static class RoomLightMap
    {
        public static Texture2D Build(RoomLayout room,Rect bounds,RoomLightProfile profile)
        {
            if(!float.IsFinite(bounds.width) || !float.IsFinite(bounds.height) || bounds.width<=0 || bounds.height<=0 ||
                bounds.width>256 || bounds.height>256)
                throw new InvalidOperationException($"Недопустимый размер карты света: room={room.id}, bounds={bounds}.");
            foreach(var light in profile.lights)
                if(light==null || string.IsNullOrEmpty(light.id) || !float.IsFinite(light.x) || !float.IsFinite(light.y) ||
                    !float.IsFinite(light.radius) || light.radius<=0 || !float.IsFinite(light.intensity) || light.intensity<0 ||
                    light.shadow && (light.intensity>1 || string.IsNullOrEmpty(light.mask) && light.maskTexture==null))
                    throw new InvalidOperationException($"Некорректный источник света: room={room.id}, light={light?.id}.");
            int width=Mathf.CeilToInt(bounds.width*8),height=Mathf.CeilToInt(bounds.height*8);
            var colors=new Color[width*height];
            var masks=LoadMasks(profile);
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
                colors[y*width+x]=Sample(room,profile,new Vector2(bounds.x+(x+.5f)*bounds.width/width,bounds.y+(y+.5f)*bounds.height/height),masks);
            var result=new Texture2D(width,height,TextureFormat.RGBA32,false,true) {name="Свет: "+room.id,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            result.SetPixels(colors);result.Apply(false,true);return result;
        }
        public static Color Sample(RoomLayout room,RoomLightProfile profile,Vector2 position)
            =>Sample(room,profile,position,LoadMasks(profile));
        private static Dictionary<string,RoomLightMask> LoadMasks(RoomLightProfile profile)
        {
            var masks=new Dictionary<string,RoomLightMask>(StringComparer.Ordinal);
            foreach(var light in profile.lights)
            {
                if(string.IsNullOrEmpty(light.mask) && light.maskTexture==null) continue;
                var texture=light.maskTexture!=null?light.maskTexture:Resources.Load<Texture2D>(light.mask);
                if(texture==null) throw new InvalidOperationException($"Нет маски света: light={light.id}, resource={light.mask}.");
                masks.Add(light.id,new RoomLightMask(texture,new Rect(light.maskX,light.maskY,light.maskWidth,light.maskHeight)));
            }
            return masks;
        }
        private static Color Sample(RoomLayout room,RoomLightProfile profile,Vector2 position,Dictionary<string,RoomLightMask> masks)
        {
            Color result=profile.ambient;float shadowTransmission=1;
            foreach(var light in profile.lights)
            {
                if(light.shadow)
                {
                    if(!masks.TryGetValue(light.id,out var shadow)) throw new InvalidOperationException($"Нет маски затемнения: light={light.id}.");
                    shadowTransmission*=1-Mathf.Clamp01(shadow.Sample(position).a*light.intensity);
                    continue;
                }
                var origin=new Vector2(light.x,light.y);float distance=Vector2.Distance(position,origin);
                if(Blocked(room,origin,position)) continue;
                if(masks.TryGetValue(light.id,out var mask)) result+=mask.Sample(position)*light.intensity;
                else
                {
                    if(distance>=light.radius) continue;
                    float falloff=1-distance/light.radius;
                    result+=light.color*(light.intensity*falloff*falloff);
                }
            }
            // Authored black-overlay opacity is display-space attenuation, while the map stores linear light in a linear project.
            float attenuation=QualitySettings.activeColorSpace==ColorSpace.Linear?Mathf.GammaToLinearSpace(shadowTransmission):shadowTransmission;
            result.r=Mathf.Clamp01(result.r)*attenuation;result.g=Mathf.Clamp01(result.g)*attenuation;result.b=Mathf.Clamp01(result.b)*attenuation;result.a=1;return result;
        }
        private static bool Blocked(RoomLayout room,Vector2 start,Vector2 end)
        {
            foreach(var wall in room.platforms)
            {
                if(wall.oneWay) continue;
                var rect=new Rect(wall.x,wall.y,wall.width,wall.height);
                // Surface pixels receive light; only an obstacle strictly between points occludes.
                if(rect.Contains(start) || rect.Contains(end)) continue;
                float near=0,far=1;var delta=end-start;
                if(Slab(start.x,delta.x,rect.xMin,rect.xMax,ref near,ref far) &&
                    Slab(start.y,delta.y,rect.yMin,rect.yMax,ref near,ref far) && near<far && near<.999f && far>.001f) return true;
            }
            return false;
        }
        private static bool Slab(float start,float delta,float min,float max,ref float near,ref float far)
        {
            if(Mathf.Abs(delta)<.00001f) return start>=min && start<=max;
            float a=(min-start)/delta,b=(max-start)/delta;
            near=Mathf.Max(near,Mathf.Min(a,b));far=Mathf.Min(far,Mathf.Max(a,b));return near<=far;
        }
    }
}
