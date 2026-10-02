using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    [Serializable] public sealed class RoomLight
    {public string id,fixture,mask;public float maskX,maskY,maskWidth,maskHeight;public float x,y,radius,intensity,width,height;public Color color=Color.white;[NonSerialized] public Texture2D maskTexture;}
    [Serializable] public sealed class RoomLightProfile
    {public Color ambient;public RoomLight[] lights;}
    public sealed class RoomLighting : MonoBehaviour
    {
        private static RoomLighting active;
        private Texture2D map;
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
            active=this;Shader.SetGlobalTexture("_WorldLightMap",map);
            Shader.SetGlobalVector("_WorldLightBounds",new Vector4(bounds.x,bounds.y,1/bounds.width,1/bounds.height));
            Shader.SetGlobalFloat("_WorldLightEnabled",1);
        }
        private void OnDestroy()=>Release();
        public void Release()
        {
            if(active==this) {active=null;Shader.SetGlobalFloat("_WorldLightEnabled",0);}
            if(map!=null) {if(Application.isPlaying) Destroy(map);else DestroyImmediate(map);}
            map=null;
        }
    }
}
