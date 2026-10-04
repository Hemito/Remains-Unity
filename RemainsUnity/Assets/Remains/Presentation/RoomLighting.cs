using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    [Serializable] public sealed class RoomLight
    {
        public string id,fixture,mask;
        public bool shadow;
        public float backgroundIntensity=-1;
        public float maskX,maskY,maskWidth,maskHeight;
        public float x,y,radius,intensity,width,height;
        public Color color=Color.white;
        [NonSerialized] public Texture2D maskTexture;
        public RoomLight ForBackground()
        {
            if(!float.IsFinite(backgroundIntensity) || backgroundIntensity<0 && backgroundIntensity!= -1)
                throw new InvalidOperationException($"Источник {id}: яркость фона должна быть неотрицательной или -1 (общая).");
            var copy=(RoomLight)MemberwiseClone();
            if(backgroundIntensity>=0) copy.intensity=backgroundIntensity;
            return copy;
        }
    }
    [Serializable] public sealed class RoomLightProfile
    {
        public Color ambient;
        public float backgroundAmbientScale=1;
        public RoomLight[] lights;
        public RoomLightProfile ForBackground()
        {
            if(!float.IsFinite(backgroundAmbientScale) || backgroundAmbientScale<0 || lights==null)
                throw new InvalidOperationException("Некорректный профиль фонового света: проверьте множитель и источники.");
            return new RoomLightProfile {ambient=ambient*backgroundAmbientScale,lights=Array.ConvertAll(lights,light=>light.ForBackground())};
        }
    }
    public sealed class RoomLighting : MonoBehaviour
    {
        private static RoomLighting active;
        private Texture2D map;
        private Texture2D backgroundMap;
        public RoomLightProfile Profile {get;private set;}
        public void Initialize(RoomLayout room)
        {
            string path=room.lightingProfile;
            if(string.IsNullOrEmpty(path)) throw new InvalidOperationException("Не задан профиль освещения: "+room.id);
            var asset=Resources.Load<TextAsset>(path);
            if(asset==null) throw new InvalidOperationException("Нет описания света: "+path);
            Profile=JsonUtility.FromJson<RoomLightProfile>(asset.text);
            if(Profile?.lights==null) throw new InvalidOperationException("Нет источников света: "+path);
            Initialize(room,Profile);
        }
        public void Initialize(RoomLayout room,RoomLightProfile profile)
        {
            Release();
            Profile=profile??throw new InvalidOperationException("Нет профиля света: "+room.id);
            float bottom=room.artwork==null?0:room.bottom,top=room.artwork==null?7:room.top;
            var bounds=new Rect(room.left,bottom,room.right-room.left,top-bottom);
            map=RoomLightMap.Build(room,bounds,Profile);
            backgroundMap=RoomLightMap.Build(room,bounds,Profile.ForBackground());
            active=this;Shader.SetGlobalTexture("_WorldLightMap",map);
            Shader.SetGlobalTexture("_BackgroundLightMap",backgroundMap);
            Shader.SetGlobalVector("_WorldLightBounds",new Vector4(bounds.x,bounds.y,1/bounds.width,1/bounds.height));
            Shader.SetGlobalFloat("_WorldLightEnabled",1);
        }
        private void OnDestroy()=>Release();
        public void Release()
        {
            if(active==this) {active=null;Shader.SetGlobalFloat("_WorldLightEnabled",0);}
            if(map!=null) {if(Application.isPlaying) Destroy(map);else DestroyImmediate(map);}
            map=null;
            if(backgroundMap!=null) {if(Application.isPlaying) Destroy(backgroundMap);else DestroyImmediate(backgroundMap);}
            backgroundMap=null;
        }
    }
}
