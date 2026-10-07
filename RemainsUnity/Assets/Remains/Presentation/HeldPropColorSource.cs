using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    public readonly struct HeldPropColor
    {
        public readonly string WorldId,PropId,Owner,Level,HolderId;
        public readonly int Generation,Magic;
        public Color Color=>PonyAppearance.FromRgb(Magic);
        internal HeldPropColor(string world,PropState prop,ActorState holder,int magic)
        {WorldId=world;PropId=prop.id;Owner=prop.owner;Level=prop.level;Generation=prop.generation;HolderId=holder.id;Magic=magic;}
        public bool Matches(string world,PropState prop)=>prop!=null && prop.held && (world==null || WorldId==world) &&
            PropId==prop.id && Owner==prop.owner && Level==prop.level && Generation==prop.generation;
    }

    // One event-built source for local and remote props/containers. Simulation
    // ownership alone is not holding: only a held prop's active player resolves.
    public sealed class HeldPropColorSource
    {
        private sealed class Holder {public ActorState actor;public int magic;public string rejection;}
        private readonly Dictionary<string,Holder> holders=new(StringComparer.Ordinal);
        public string WorldId {get;private set;}
        public void Rebuild(WorldState world)
        {
            holders.Clear();WorldId=world?.worldId;
            if(world==null) return;
            if(string.IsNullOrWhiteSpace(WorldId)) throw new InvalidOperationException("Цвет удержания требует устойчивый ID мира.");
            foreach(var actor in world.actors)
            {
                if(!actor.player || !actor.connected || actor.down || actor.health<=0 || string.IsNullOrWhiteSpace(actor.owner)) continue;
                if(holders.TryGetValue(actor.owner,out var previous))
                {previous.rejection="Неоднозначный держатель: один owner назначен нескольким активным игрокам.";continue;}
                var holder=new Holder {actor=actor};holders.Add(actor.owner,holder);
                try {holder.magic=PonyAppearance.Snapshot(actor).Magic;}
                catch(Exception error) {holder.rejection="Некорректная внешность держателя "+actor.id+": "+error.Message;}
            }
        }
        public bool TryResolve(PropState prop,out HeldPropColor color,out string reason)
        {
            color=default;reason=null;
            if(prop==null) throw new ArgumentNullException(nameof(prop));
            if(!prop.held) return false;
            if(WorldId==null) {reason="Нет подтверждённого мира.";return false;}
            if(string.IsNullOrWhiteSpace(prop.id) || string.IsNullOrWhiteSpace(prop.owner) || !holders.TryGetValue(prop.owner,out var holder))
            {reason="Нет активного подключённого игрока для owner удерживаемого предмета.";return false;}
            if(holder.rejection!=null) {reason=holder.rejection;return false;}
            if(holder.actor.level!=prop.level) {reason="Держатель находится в другом уровне: "+holder.actor.id+"/"+holder.actor.level;return false;}
            color=new HeldPropColor(WorldId,prop,holder.actor,holder.magic);return true;
        }
    }
}
