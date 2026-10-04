using System;
using System.Collections.Generic;
using Remains.Domain;

namespace Remains.Content
{
    public static class NpcContentInstaller
    {
        public static bool NeedsInstall(WorldState world,NpcLayout layout)=>layout!=null && !world.contentPatches.Contains(layout.patch);
        public static bool Apply(WorldState world,NpcLayout layout)
        {
            if(world==null || layout==null || string.IsNullOrWhiteSpace(layout.patch) || string.IsNullOrWhiteSpace(layout.level) || layout.actors==null)
                throw new InvalidOperationException("Неполная расстановка NPC.");
            if(!NeedsInstall(world,layout)) return false;
            var ids=new HashSet<string>(StringComparer.Ordinal);var additions=new List<ActorState>();
            foreach(var actor in layout.actors)
            {
                if(actor==null || string.IsNullOrWhiteSpace(actor.id) || !ids.Add(actor.id) || actor.level!=layout.level || actor.player || actor.connected ||
                    actor.owner!=null || actor.target!=null || actor.down || actor.attackBusy || string.IsNullOrWhiteSpace(actor.actorType) ||
                    string.IsNullOrWhiteSpace(actor.anatomy) || !float.IsFinite(actor.x) || !float.IsFinite(actor.y) ||
                    !float.IsFinite(actor.health) || actor.health<=0 || Math.Abs(actor.facing)!=1)
                    throw new InvalidOperationException("Некорректный NPC расстановки: "+actor?.id);
                if(world.props.Exists(p=>p.id==actor.id) || world.containers.Exists(c=>c.id==actor.id) || world.doors.Exists(d=>d.id==actor.id))
                    throw new InvalidOperationException("ID NPC занят другим объектом: "+actor.id);
                var existing=world.actors.FindAll(a=>a.id==actor.id);
                if(existing.Count>1 || existing.Count==1 && (existing[0].player || existing[0].actorType!=actor.actorType || existing[0].level!=actor.level))
                    throw new InvalidOperationException("Конфликт сохранённого NPC: "+actor.id);
                if(existing.Count==0) additions.Add(actor.Copy());
            }
            if(world.revision==long.MaxValue) throw new InvalidOperationException("Исчерпан счётчик мира при установке NPC.");
            world.actors.AddRange(additions);world.contentPatches.Add(layout.patch);world.revision++;
            return true;
        }
    }
}
