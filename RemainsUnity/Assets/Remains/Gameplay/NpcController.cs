using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class NpcController
    {
        private readonly CoopSession session;
        private readonly Dictionary<string, MovementController> movement = new();
        public NpcController(CoopSession session,SimulationGeometry geometry)
        {
            this.session=session;
            session.Changed+=()=>
            {
                var needed=session.World==null?new HashSet<string>():new HashSet<string>(session.World.actors
                    .Where(a=>!a.player && a.owner==session.LocalOwner && !a.down && LevelActivity.HasConnectedPlayer(session.World,a.level)).Select(a=>a.level));
                foreach(string level in movement.Keys.Where(level=>!needed.Contains(level)).ToArray()) movement.Remove(level);
                foreach(string level in needed) if(!movement.ContainsKey(level)) movement.Add(level,new MovementController(geometry.Get(level)));
            };
        }
        public void Step(float dt)
        {
            if(session.World==null) return;
            foreach(var actor in session.World.actors)
            {
                if(actor.player || actor.owner!=session.LocalOwner || actor.down) continue;
                if(!LevelActivity.HasConnectedPlayer(session.World,actor.level)) continue;
                if(!movement.TryGetValue(actor.level,out var controller))
                    throw new InvalidOperationException($"Не загружена геометрия симуляции NPC: actor={actor.id}, level={actor.level}.");
                var target=session.World.Find(actor.attackBusy?actor.attackTarget:actor.target);
                actor.attackCooldown=Mathf.Max(0,actor.attackCooldown-dt);
                if(actor.attackBusy)
                {
                    actor.attackRemaining-=dt;
                    if(actor.attackRemaining<=0)
                    {
                        var attack=NpcAttackRules.Complete(actor,target);
                        if(attack!=null) session.Fire(attack);
                        session.PublishActor(actor);
                    }
                    continue;
                }
                if(target==null || target.level!=actor.level || target.down || !target.connected) { controller.Step(actor,Vector2.zero,false,dt); continue; }
                float distance=target.x-actor.x;
                controller.Step(actor,new Vector2(Mathf.Abs(distance)>5?Mathf.Sign(distance)*0.5f:0,0),false,dt);
                actor.facing=distance>=0?1:-1;
                if(Mathf.Abs(distance)<7 && actor.attackCooldown<=0)
                {
                    NpcAttackRules.Begin(actor,target);session.PublishActor(actor);
                }
            }
        }
    }
}
