using System;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable] public sealed class RoomSurface { public float x,y,width,height; public string texture; }
    [Serializable] public sealed class RoomLadder { public float x,bottom,top; }
    [Serializable] public sealed class RoomLayout
    {
        public string id;
        public float left,right;
        public RoomSurface[] platforms;
        public RoomLadder[] ladders;
        public static RoomLayout Load()
        {
            var asset=Resources.Load<TextAsset>("Content/prototype-room");
            if(asset==null) throw new InvalidOperationException("Не найдено описание комнаты Content/prototype-room.");
            var result=JsonUtility.FromJson<RoomLayout>(asset.text);
            if(result.platforms==null || result.ladders==null || result.left>=result.right)
                throw new InvalidOperationException($"Некорректная геометрия комнаты: {result.id}.");
            return result;
        }
    }
}
