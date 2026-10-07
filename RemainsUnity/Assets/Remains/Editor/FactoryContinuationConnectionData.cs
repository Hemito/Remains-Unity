using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    [Serializable] internal sealed class FactoryContinuationPortal
    {
        public string id,compatibility;
        public int warehouseRightSlot,continuationLeftSlot;
        public int[] sourceValues,rows,warehouseColumns,continuationColumns;
        public float floor,height;
        public float[] worldOpenBounds;
    }
    [Serializable] internal sealed class FactoryContinuationConnectionData
    {
        public int schema;public string sourceId,instanceRoomId;public bool mirror;
        public float[] translation,wholeLevelBounds;
        public string[] priorRoomIds;
        public FactoryContinuationPortal[] portals;
        public RoomSurface upperBridge;public RoomSurface[] seamClosures;
        public static FactoryContinuationConnectionData Load()
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Tools/Research/FactoryContinuation27R60/connection.json"));
            var data=JsonConvert.DeserializeObject<FactoryContinuationConnectionData>(File.ReadAllText(path));
            Require(data!=null && data.schema==1 && data.sourceId=="original:plant:27" && data.instanceRoomId=="base:factory-continuation-27" && !data.mirror &&
                data.translation!=null && data.translation.SequenceEqual(new[]{153.6f,0}) &&
                data.wholeLevelBounds!=null && data.wholeLevelBounds.SequenceEqual(new[]{0f,0,192,20}),"Изменена явная регистрация комнаты27: "+path);
            Require(data.priorRoomIds!=null && data.priorRoomIds.SequenceEqual(new[]{"base:factory-entry-46","base:factory-workshop-18","base:factory-office-53","base:factory-warehouse-40"}),"Изменён список прежних модулей.");
            Require(data.portals?.Length==2,"Требуются два исходных прохода склада40 в комнату27.");
            for(int i=0;i<2;i++)
            {
                var p=data.portals[i];int first=i==0?13:17;float floor=i==0?7.2f:4;
                Require(p!=null && p.id=="base:factory-seam:40-27:slot"+(i+3) && p.warehouseRightSlot==i+3 && p.continuationLeftSlot==i+14 &&
                    p.sourceValues!=null && p.sourceValues.SequenceEqual(new[]{3,3}) && p.compatibility=="MIN(values)>=2; not sum" &&
                    p.rows!=null && p.rows.SequenceEqual(new[]{first,first+1,first+2}) &&
                    p.warehouseColumns!=null && p.warehouseColumns.SequenceEqual(new[]{46,47}) &&
                    p.continuationColumns!=null && p.continuationColumns.SequenceEqual(new[]{0,1}) &&
                    p.floor==floor && p.height==2.4f && p.worldOpenBounds!=null && p.worldOpenBounds.SequenceEqual(new[]{152f,floor,3.2f,2.4f}),
                    "Изменены исходные slots/air rows/высота прохода "+i);
            }
            var expected=new[]{Surface(152.8f,6.4f,.8f),Surface(153.6f,.8f,3.2f),Surface(153.6f,6.4f,.8f),Surface(153.6f,9.6f,9.6f)};
            Require(data.upperBridge!=null && data.seamClosures?.Length==3,"Нет явной опоры и трёх полос вне проходов.");
            var actual=new[]{data.upperBridge}.Concat(data.seamClosures).ToArray();
            for(int i=0;i<expected.Length;i++) Require(FactoryLadderLandingChecks.SameSurface(actual[i],expected[i]),"Изменена опора/рамка стыка: "+i);
            return data;
        }
        private static RoomSurface Surface(float x,float y,float height)=>new RoomSurface {x=x,y=y,width=.8f,height=height,
            texture="Factory27R60/26_tConcrete",movementSound="normal"};
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Стык склада40 и комнаты27: "+reason);}
    }
}
