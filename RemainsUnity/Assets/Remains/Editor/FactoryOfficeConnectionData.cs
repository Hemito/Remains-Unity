using System;
using System.IO;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    [Serializable] internal sealed class FactoryOfficeConnectionData
    {
        public int schema;public string sourceId,instanceRoomId;public bool mirror;
        public float[] translation,wholeLevelBounds;
        public RoomSurface connector;public RoomSurface[] seamClosures;
        public static FactoryOfficeConnectionData Load()
        {
            var path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Tools/Research/FactoryOffice53/connection.json"));
            var data=JsonUtility.FromJson<FactoryOfficeConnectionData>(File.ReadAllText(path));
            if(data==null || data.schema!=1 || data.sourceId!="original:plant:53" || data.instanceRoomId!="base:factory-office-53" || data.mirror ||
                data.translation?.Length!=2 || data.translation[0]!=76.8f || data.translation[1]!=0 ||
                data.wholeLevelBounds?.Length!=4 || !data.wholeLevelBounds.SequenceEqual(new[]{0f,0,115.2f,20}) ||
                data.connector==null || data.seamClosures?.Length!=2)
                throw new InvalidOperationException("Неполные или несовместимые данные подключения офиса: "+path);
            foreach(var surface in new[]{data.connector}.Concat(data.seamClosures))
                if(surface==null || string.IsNullOrWhiteSpace(surface.texture) || string.IsNullOrWhiteSpace(surface.movementSound) ||
                    surface.oneWay || surface.shotsPassThrough || surface.slopeDirection!=0 || surface.width<=0 || surface.height<=0 ||
                    !float.IsFinite(surface.x+surface.y+surface.width+surface.height))
                    throw new InvalidOperationException("Рамка подключения офиса: отсутствует явная опора, графика или звук. "+path);
            return data;
        }
    }
}
