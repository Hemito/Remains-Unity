using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class NpcController
    {
        private readonly CoopSession session;
        private readonly Dictionary<string, MovementController> movement = new();
        public NpcController(CoopSession session,RoomLayout hub,RoomLayout factory)
        {
            this.session=session;
            movement.Add("hub",new MovementController(hub));
            movement.Add("factory:0",new MovementController(factory));
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
