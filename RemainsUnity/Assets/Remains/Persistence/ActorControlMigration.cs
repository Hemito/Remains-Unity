using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Persistence
{
    public static class ActorControlMigration
    {
        // Repair is justified only for the built-in zero-health rule and an unmodified
        // legacy manifest. A mod can intentionally incapacitate a living character.
        public static bool Prepare(WorldState world,IKnockdownRule rule,Func<string> preserveSource)
        {
            var legacy=world.actors.Where(a=>a.player && a.controlStateVersion==0).ToArray();
            if(legacy.Length==0) return false;
            string backup=preserveSource();
            bool builtIn=rule is ZeroHealthKnockdownRule &&
                IsUnmodified(world.weaponContent,world.weaponContentHistory);
            foreach(var actor in legacy)
            {
                bool unmodified=builtIn && IsUnmodified(actor.weaponContent,actor.weaponContentHistory);
                bool repair=actor.down && actor.health>0 && unmodified;
                if(repair)
                {
                    actor.down=false;
                    if(actor.connected && actor.pose=="down") ActorPose.Select(actor,"idle");
                }
                actor.controlStateVersion=ActorSessionRules.ControlStateVersion;
                actor.revision++;
                Debug.LogWarning($"Разделены отключение и нокдаун: world={world.worldId}, actor={actor.id}, level={actor.level}, HP={actor.health}, восстановленоУправление={repair}, нокдаун={actor.down}, правило={rule.GetType().Name}, исходник={backup}."+
                    (actor.down && actor.health>0 && !unmodified?" Положительное HP не доказывает отсутствие нокдауна при изменённых правилах; состояние сохранено.":""));
            }
            world.revision++;
            return true;
        }
        private static bool IsUnmodified(ContentManifest manifest,List<ContentManifest> history)=>
            (manifest==null || manifest.packs!=null && manifest.packs.All(pack=>pack.id=="base")) &&
            (history==null || history.All(previous=>IsUnmodified(previous,null)));
    }
}
