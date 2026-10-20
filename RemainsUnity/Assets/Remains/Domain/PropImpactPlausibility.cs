using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    // Admission only: never clamps simulation. Limits derive from the level's free-fall height
    // and the largest displacement between a held body and its allowed TK cursor.
    public sealed class PropImpactPlausibility
    {
        public const float PoseWindow=.25f;
        private readonly float followSpeed;
        private readonly Func<string,RoomLayout> readRoom;
        private readonly Dictionary<string,(Rect bounds,float speed)> levels=new(StringComparer.Ordinal);
        public PropImpactPlausibility(float range,float releaseRange,Func<string,RoomLayout> readRoom)
        {
            if(!float.IsFinite(range) || range<=0 || !float.IsFinite(releaseRange) || releaseRange<range)
                throw new ArgumentException("Некорректные пределы телекинеза для проверки Box.");
            followSpeed=TelekinesisFollow.ResponseRate*(range+releaseRange);this.readRoom=readRoom??throw new ArgumentNullException(nameof(readRoom));
        }
        public void Prepare(string level,RoomLayout room)
        {
            if(room==null || !float.IsFinite(room.left) || !float.IsFinite(room.right) || !float.IsFinite(room.bottom) ||
                !float.IsFinite(room.top) || room.right<=room.left || room.top<=room.bottom)
                throw new InvalidOperationException("Нет корректных границ уровня для проверки Box: "+level);
            levels[level]=(Rect.MinMaxRect(room.left,room.bottom,room.right,room.top),
                followSpeed+Mathf.Sqrt(2*WorldPhysics.Gravity*(room.top-room.bottom)));
        }
        private (Rect bounds,float speed) Level(string level)
        {if(!levels.TryGetValue(level,out var limits)){Prepare(level,readRoom(level));limits=levels[level];}return limits;}
        public bool Sample(PropState prop,PropImpactSample sample,out string reason)
        {
            reason="Нет исходного тела/образца.";if(prop==null || sample==null)return false;
            var limits=Level(prop.level);var velocity=new Vector2(sample.vx,sample.vy);
            if(!float.IsFinite(velocity.sqrMagnitude) || velocity.magnitude>limits.speed)
            {reason=$"Скорость образца {velocity} превышает физический предел {limits.speed:F2} м/с.";return false;}
            var position=new Vector2(sample.x,sample.y);float hull=Mathf.Max(prop.width,prop.height);
            var bounds=limits.bounds;bounds.xMin-=hull;bounds.xMax+=hull;bounds.yMin-=hull;bounds.yMax+=hull;
            if(!float.IsFinite(position.sqrMagnitude) || !bounds.Contains(position))
            {reason="Позиция образца вне границ исходного уровня.";return false;}
            var canonical=new Vector2(prop.vx,prop.vy);
            if(!float.IsFinite(canonical.sqrMagnitude) || canonical.magnitude>limits.speed ||
                velocity.magnitude>canonical.magnitude+WorldPhysics.Gravity*PoseWindow+.5f)
            {reason="Рост скорости не соответствует последней принятой позе Box.";return false;}
            float travel=PoseWindow*Mathf.Max(velocity.magnitude,canonical.magnitude)+hull;
            if(Vector2.Distance(position,prop.Position)>travel)
            {reason="Позиция образца не соответствует последней принятой позе Box.";return false;}
            reason=null;return true;
        }
        public bool Target(ActorState actor,Vector2 reported,float knocked,out Vector2 canonical,out string reason)
        {
            canonical=actor?.Velocity??Vector2.zero;reason="Нет жертвы Box.";if(actor==null)return false;
            float limit=Level(actor.level).speed*2*(1+knocked);
            if(!float.IsFinite(reported.sqrMagnitude) || !float.IsFinite(canonical.sqrMagnitude) ||
                reported.magnitude>limit || canonical.magnitude>limit)
            {reason="Нечисловая или неправдоподобная скорость жертвы Box.";return false;}
            if(Vector2.Distance(reported,canonical)>WorldPhysics.Gravity*PoseWindow+1)
            {reason="Скорость жертвы не соответствует её последнему принятому состоянию.";return false;}
            reason=null;return true;
        }
    }
}
