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
        private readonly Dictionary<string, float> nextAttack = new();
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
                if(!movement.TryGetValue(actor.level,out var controller))
                    throw new InvalidOperationException($"Не загружена геометрия симуляции NPC: actor={actor.id}, level={actor.level}.");
                var target=session.World.Find(actor.target);
                if(actor.attackBusy)
                {
                    actor.attackRemaining-=dt;
                    if(actor.attackRemaining<=0)
                    {
                        if(target!=null && target.level==actor.level)
                        {
                            var origin=actor.Position+new Vector2(0,0.55f);
                            var end=target.Position+new Vector2(0,0.55f);
                            session.Fire(new AttackEvent { id=Guid.NewGuid().ToString("N"),attacker=actor.id,weapon="npc-shot",
                                originX=origin.x,originY=origin.y,endX=end.x,endY=end.y });
                        }
                        actor.attackBusy=false; actor.revision++; session.PublishActor(actor);
                        nextAttack[actor.id]=Time.time+2;
                    }
                    continue;
                }
                if(target==null || target.level!=actor.level || target.down || !target.connected) { controller.Step(actor,Vector2.zero,false,dt); continue; }
                float distance=target.x-actor.x;
                controller.Step(actor,new Vector2(Mathf.Abs(distance)>5?Mathf.Sign(distance)*0.5f:0,0),false,dt);
                actor.facing=distance>=0?1:-1;
                if(Mathf.Abs(distance)<7 && (!nextAttack.TryGetValue(actor.id,out float readyAt) || Time.time>readyAt))
                {
                    actor.attackBusy=true; actor.attackRemaining=0.8f;
                    actor.revision++; session.PublishActor(actor);
                }
            }
        }
    }
}
