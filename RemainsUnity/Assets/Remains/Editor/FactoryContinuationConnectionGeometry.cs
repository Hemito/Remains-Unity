using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Authoring preflight uses the same complete contour as the saved seamless level.
    public static class FactoryContinuationConnectionGeometry
    {
        public static RoomLayout Preview(RoomAuthoring previous,RoomAuthoring module)
        {
            var result=previous.CreateLayout(false);
            if(Mathf.Abs(result.right-FactoryContinuationConnectionAuthoring.Offset)>.001f)
                throw new InvalidOperationException("Предпросмотр комнаты27: прежний завод имеет другую правую границу.");
            var contour=previous.transform.Find(FactoryBoundaryAuthoring.GroupName);
            if(contour==null) throw new InvalidOperationException("Предпросмотр комнаты27: нет прежнего внешнего контура.");
            var local=module.CreateLayout(false);
            var data=FactoryContinuationConnectionData.Load();
            result.right=FactoryContinuationConnectionAuthoring.Right;
            result.platforms=previous.GetComponentsInChildren<RoomSurfaceAuthoring>(true)
                .Where(p=>previous.Included(p) && !p.transform.IsChildOf(contour)).Select(p=>p.Read(previous))
                .Concat(local.platforms.Select(p=>Translate(p,FactoryContinuationConnectionAuthoring.Offset)))
                .Concat(new[]{StateJson.Copy(data.upperBridge)}).Concat(data.seamClosures.Select(StateJson.Copy)).ToArray();
            result.ladders=result.ladders.Concat(local.ladders.Select(p=>
            {var copy=StateJson.Copy(p);copy.x+=FactoryContinuationConnectionAuthoring.Offset;return copy;})).ToArray();
            result.platforms=result.platforms.Concat(RoomBoundaryGaps.Find(result,FactoryBoundaryAuthoring.Thickness,
                FactoryBoundaryAuthoring.Texture,"normal")).ToArray();
            return result;
        }

        public static RoomLayout InModule(RoomAuthoring module)
        {
            RoomLayout complete=null;
            for(var parent=module.transform.parent;parent!=null;parent=parent.parent)
            {
                var room=parent.GetComponent<RoomAuthoring>();
                if(room!=null && Mathf.Abs(room.CreateLayout(false).right-FactoryContinuationConnectionAuthoring.Right)<.001f)
                {complete=room.CreateLayout(false);break;}
            }
            if(complete==null)
            {
                var root=AssetDatabase.LoadAssetAtPath<GameObject>(ConnectedFactoryAuthoring.Path);
                if(root==null) throw new InvalidOperationException("Комната27: нет сохранённого общего завода для проверки опор.");
                var room=root.GetComponent<RoomAuthoring>();var saved=room.CreateLayout(false);
                complete=Mathf.Abs(saved.right-FactoryContinuationConnectionAuthoring.Right)<.001f?saved:Preview(room,module);
            }
            float offset=FactoryContinuationConnectionAuthoring.Offset;
            complete.left-=offset;complete.right-=offset;
            complete.platforms=complete.platforms.Select(p=>Translate(p,-offset)).ToArray();
            foreach(var ladder in complete.ladders) ladder.x-=offset;
            return complete;
        }

        public static void CheckBuilt(RoomLayout expected,RoomAuthoring connected)
        {
            var actual=connected.CreateLayout(false);var remaining=actual.platforms.ToList();
            foreach(var surface in expected.platforms)
            {
                int index=remaining.FindIndex(p=>FactoryLadderLandingChecks.SameSurface(p,surface));
                if(index<0) throw new InvalidOperationException($"Комната27: сохранённая геометрия отличается от предпросмотра опор ({surface.x}, {surface.y}, {surface.width}, {surface.height}).");
                remaining.RemoveAt(index);
            }
            if(remaining.Count!=0 || Mathf.Abs(actual.right-expected.right)>.001f)
                throw new InvalidOperationException("Комната27: при подключении появились непроверенные поверхности/границы.");
        }

        private static RoomSurface Translate(RoomSurface source,float offset)
        {var result=StateJson.Copy(source);result.x+=offset;return result;}
    }
}
