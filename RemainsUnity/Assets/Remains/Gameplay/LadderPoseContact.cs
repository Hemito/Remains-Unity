using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // Release the hold before a descending visible leg crosses a solid landing.
    // The body's position and momentum still belong to ordinary terrain motion.
    public static class LadderPoseContact
    {
        private static readonly HashSet<string> missing=new();
        public static bool ShouldRelease(ActorState actor,RoomLayout room,Vector2 nextBodyPosition)
        {
            if(!Anatomy.TrySupportContacts(actor,out var contacts))
            {
                string key=actor.id+":"+actor.anatomy;
                if(missing.Add(key)) Debug.LogWarning($"Нет контактов ног для спуска: actor={actor.id}, anatomy={actor.anatomy}, pose={ActorPose.Clip(actor)}, room={room.id}. Проверка видимой ноги пропущена; сохраняется прежний контакт габарита тела.");
                return false;
            }
            foreach(var local in contacts)
            {
                var point=nextBodyPosition+new Vector2(local.x*actor.facing,local.y);
                foreach(var surface in room.platforms)
                {
                    // A held body deliberately passes the one-way beams.
                    if(surface.oneWay || !RoomSupport.ContainsX(surface,point.x)) continue;
                    float top=RoomSupport.Top(surface,point.x);
                    if(actor.y>=top-RoomSupport.ContactTolerance && point.y<=top+.0001f) return true;
                }
            }
            return false;
        }
    }
}
