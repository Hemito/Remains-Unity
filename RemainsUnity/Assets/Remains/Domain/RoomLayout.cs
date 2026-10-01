using System;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable] public sealed class RoomSurface { public float x,y,width,height; public string texture; public bool oneWay,shotsPassThrough; }
    [Serializable] public sealed class RoomLadder { public float x,bottom,top,movementOffset; }
    [Serializable] public sealed class RoomArtwork { public float x,y,width,height,shade=1; public string texture; public int order; public bool repeat; }
    [Serializable] public sealed class RoomLayout
    {
        public string id;
        public float left,right;
        public float bottom,top=7,spawnX,spawnY;
        public RoomSurface[] platforms;
        public RoomLadder[] ladders;
        public RoomArtwork[] artwork;
        public static RoomLayout Load(string level="factory:0")
        {
            string key=level switch {"factory:0"=>"prototype-room","hub"=>"prototype-hub",_=>throw new InvalidOperationException($"Неизвестная локация прототипа: {level}.")};
            var asset=Resources.Load<TextAsset>("Content/"+key);
            if(asset==null) throw new InvalidOperationException($"Не найдено описание комнаты Content/{key}.");
            var result=JsonUtility.FromJson<RoomLayout>(asset.text);
            if(result.platforms==null || result.ladders==null || result.left>=result.right)
                throw new InvalidOperationException($"Некорректная геометрия комнаты: {result.id}.");
            return result;
        }
    }
}
