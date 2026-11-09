using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    // Broad-phase actors use the existing event-updated index. Surface contacts use room buckets.
    // Fixed targets are prepared on an explicit throw/world-change, never looked up as assets in Step.
    public sealed class LightBallContacts
    {
        private readonly CoopSession session;
        private readonly List<ActorState> actors=new();
        private const float CellSize=4;
        private readonly Dictionary<string,Dictionary<(int X,int Y),List<Rect>>> fixedTargets=new(StringComparer.Ordinal);
        private readonly List<string> levels=new();
        public LightBallContacts(CoopSession session)
        {this.session=session;session.Changed+=Changed;}
        public void Prepare(string level)
        {
            var targets=new Dictionary<(int X,int Y),List<Rect>>();fixedTargets[level]=targets;
            if(session.World==null)return;
            void Add(Rect bounds)
            {
                for(int x=Mathf.FloorToInt(bounds.xMin/CellSize);x<=Mathf.FloorToInt(bounds.xMax/CellSize);x++)
                for(int y=Mathf.FloorToInt(bounds.yMin/CellSize);y<=Mathf.FloorToInt(bounds.yMax/CellSize);y++)
                {if(!targets.TryGetValue((x,y),out var cell))targets[(x,y)]=cell=new List<Rect>();cell.Add(bounds);}
            }
            foreach(var lamp in session.World.lamps)if(lamp.level==level && !lamp.broken)Add(lamp.Bounds);
            foreach(var hazard in session.World.hazards)if(hazard.level==level && hazard.health>0)
            {var size=HazardCatalog.Get(hazard.definition);Add(new Rect(hazard.x-size.width*.5f,hazard.y,size.width,size.height));}
        }
        private void Changed()
        {
            if(session.World==null){fixedTargets.Clear();return;}
            if(session.LastChange.Any(WorldChangeKind.Identity))
            {levels.Clear();levels.AddRange(fixedTargets.Keys);foreach(string level in levels)Prepare(level);return;}
            if(!session.LastChange.Any(WorldChangeKind.Lamps|WorldChangeKind.Hazards))return;
            levels.Clear();levels.AddRange(fixedTargets.Keys);foreach(string level in levels)Prepare(level);
        }
        public bool Hit(GrenadeState ball,RoomLayout room,Vector2 from,Vector2 to)
        {
            var source=session.LocalActor;
            if(source==null || source.level!=ball.level || ball.holder!=null)return false;
            var attack=new AttackEvent{id=ball.id+":contact",attacker=source.id,sourceOwner=session.LocalOwner,
                sourcePlayer=source.player,level=ball.level,weapon=ball.weapon,originX=from.x,originY=from.y,endX=to.x,endY=to.y,
                radius=.105f,friendlyFireDecision=session.World.friendlyFireDecision};
            float nearest=2;bool hit=false;
            var terrain=ShotObstacles.FirstContact(room,from,to,out var surface,attack.radius);
            float limit=terrain??1;
            if(terrain.HasValue && !string.IsNullOrEmpty(surface.sourceDoorId))
            {
                var door=session.World.doors.Find(d=>d.id==surface.sourceDoorId);
                if(door?.integrity!=null && door.Blocks){nearest=terrain.Value;hit=true;}
            }
            session.PropImpacts.Targets.Query(ball.level,SurfaceContact.QueryBounds(from,to,attack.radius),actors);
            foreach(var target in actors)
            {
                if(target.id==source.id || target.health<=0 || !ActorPresence.IsPresent(target) || ActorPresence.Hidden(target))continue;
                if(Anatomy.Contact(target,attack,out float at)!=null && at<=limit+.0001f && at<nearest){nearest=at;hit=true;}
            }
            if(fixedTargets.TryGetValue(ball.level,out var targets))
            {
                var query=SurfaceContact.QueryBounds(from,to,attack.radius);
                for(int x=Mathf.FloorToInt(query.xMin/CellSize);x<=Mathf.FloorToInt(query.xMax/CellSize);x++)
                for(int y=Mathf.FloorToInt(query.yMin/CellSize);y<=Mathf.FloorToInt(query.yMax/CellSize);y++)
                if(targets.TryGetValue((x,y),out var cell))foreach(var bounds in cell)
                {
                    var at=SurfaceContact.Entry(bounds,from,to,attack.radius);
                    if(at.HasValue && at.Value<=limit+.0001f && at.Value<nearest){nearest=at.Value;hit=true;}
                }
            }
            if(!hit)return false;
            // The deciding peer publishes one ordinary contact, followed by removal of this flight.
            // Attribute a caught/reassigned ball to its current controlling player, as a thrown physical body.
            var end=Vector2.Lerp(from,to,Mathf.Min(1,nearest+.0001f));attack.endX=end.x;attack.endY=end.y;
            ball.Position=end;session.FirePrepared(attack);return true;
        }
    }
}
