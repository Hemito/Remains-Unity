using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    // Load-time repair only. Never search room geometry in the per-frame controller.
    public static class SavedActorPlacement
    {
        public static bool Repair(ActorState actor,RoomLayout room,string context)
        {
            bool repaired=ActorPositionRecovery.Repair(actor,room,context);
            var hull=Anatomy.Movement(actor);
            if(ActorTerrainMotion.IsClear(room,hull,actor.Position)) return repaired;
            var origin=actor.Position;
            float half=hull.width*.5f;
            var xs=new SortedSet<float> {origin.x,room.left+half,room.right-half};
            foreach(var surface in room.platforms)
            {
                // Solid faces and full-footprint support endpoints delimit safe intervals.
                xs.Add(surface.x-half);xs.Add(surface.x+half);
                xs.Add(surface.x+surface.width-half);xs.Add(surface.x+surface.width+half);
            }
            Vector2 best=default;double distance=double.PositiveInfinity;
            foreach(float x in xs)
                foreach(var surface in room.platforms)
                {
                    float y=RoomSupport.Top(surface,x,hull.width);
                    var candidate=new Vector2(x,y);
                    if(!ActorTerrainMotion.IsClear(room,hull,candidate) || !ActorTerrainMotion.Supported(room,hull,candidate)) continue;
                    double dx=(double)x-origin.x,dy=(double)y-origin.y;
                    double next=dx*dx+dy*dy;
                    if(next<distance) {distance=next;best=candidate;}
                }
            if(double.IsPositiveInfinity(distance))
                throw new InvalidOperationException($"Нет свободной опоры для восстановления: actor={actor.id}, anatomy={actor.anatomy}, room={room.id}, position={origin}, hull={hull.width}x{hull.height}, context={context}. Загрузка остановлена без записи исправленного сейва.");
            actor.Position=best;actor.vx=actor.vy=0;actor.revision++;
            ActorPose.Select(actor,actor.down?"down":actor.crouched?"crouch":"idle");
            Debug.LogWarning($"Позиция из сейва пересекает твёрдую геометрию или границы: actor={actor.id}, anatomy={actor.anatomy}, level={actor.level}, room={room.id}, position={origin}, restored={best}, hull={hull.width}x{hull.height}, context={context}. Здоровье, сознание, ранения и предметы сохранены.");
            return true;
        }
    }
}
