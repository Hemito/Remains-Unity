using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    public static class AtlasWeaponSockets
    {
        public static bool Enabled(ActorState actor)=>ActorTypes.Get(actor.actorType).atlasWeaponSocket;
        public static void Prepare(ActorTypeDefinition type)
        {
            if(!type.atlasWeaponSocket) return;
            var data=AtlasAnimationData.Get(type.artwork);
            if(data.cellWidth<=0 || data.cellHeight<=0 || !float.IsFinite(data.pixelsPerUnit) || data.pixelsPerUnit<=0 ||
                !float.IsFinite(data.pivotX) || !float.IsFinite(data.pivotY) || data.pivotX<0 || data.pivotX>1 || data.pivotY<0 || data.pivotY>1)
                throw new InvalidOperationException($"Тип {type.id}: неверная геометрия точки оружия атласа {type.artwork}.");
            foreach(var clip in data.clips) ValidateClip(clip,$"type={type.id}, artwork={type.artwork}",required:true);
        }
        public static void ValidateClip(AtlasActorClip clip,string context,bool required=false)
        {
            var points=clip.weaponSockets;
            if((points==null || points.Length==0) && !required) return;
            if(points==null || points.Length!=clip.count)
                throw new InvalidOperationException($"Точки оружия: {context}, pose={clip.pose}, ожидается {clip.count}, получено {points?.Length ?? 0}.");
            for(int frame=0;frame<points.Length;frame++)
                if(points[frame]==null || !float.IsFinite(points[frame].x) || !float.IsFinite(points[frame].y))
                    throw new InvalidOperationException($"Точка оружия: {context}, pose={clip.pose}, frame={frame}, неверные координаты.");
        }
        public static Vector2 Anchor(ActorState actor)
        {
            var type=ActorTypes.Get(actor.actorType);
            if(!type.atlasWeaponSocket) throw new InvalidOperationException($"Тип {type.id} не использует точку оружия атласа: actor={actor.id}.");
            var data=AtlasAnimationData.RequirePrepared(type.artwork);
            var clip=data.Clip(ActorPose.Clip(actor));
            var point=clip.weaponSockets[clip.Sample(actor)];
            var offset=new Vector2((point.x-data.cellWidth*data.pivotX)/data.pixelsPerUnit,
                (data.cellHeight*(1-data.pivotY)-point.y)/data.pixelsPerUnit);
            if((actor.facing<0)!=data.facesLeft) offset.x=-offset.x;
            return actor.Position+offset;
        }
    }
}
