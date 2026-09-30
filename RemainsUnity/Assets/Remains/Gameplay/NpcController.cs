using System;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class NpcController
    {
        private readonly CoopSession session;
        private readonly MovementController movement;
        private float nextAttack;
        public NpcController(CoopSession session,RoomLayout room) { this.session=session; movement=new MovementController(room); }
        public void Step(float dt)
        {
            if(session.World==null) return;
            foreach(var actor in session.World.actors)
            {
                if(actor.player || actor.owner!=session.LocalOwner || actor.down) continue;
                var target=session.World.Find(actor.target);
                if(actor.attackBusy)
                {
                    actor.attackRemaining-=dt;
                    if(actor.attackRemaining<=0)
                    {
                        if(target!=null)
                        {
                            var origin=actor.Position+new Vector2(0,0.55f);
                            var end=target.Position+new Vector2(0,0.55f);
                            session.Fire(new AttackEvent { id=Guid.NewGuid().ToString("N"),attacker=actor.id,weapon="npc-shot",
                                originX=origin.x,originY=origin.y,endX=end.x,endY=end.y });
                        }
                        actor.attackBusy=false; actor.revision++; session.PublishActor(actor);
                        nextAttack=Time.time+2;
                    }
                    continue;
                }
                if(target==null || target.down || !target.connected) { movement.Step(actor,Vector2.zero,false,dt); continue; }
                float distance=target.x-actor.x;
                movement.Step(actor,new Vector2(Mathf.Abs(distance)>5?Mathf.Sign(distance)*0.5f:0,0),false,dt);
                actor.facing=distance>=0?1:-1;
                if(Mathf.Abs(distance)<7 && Time.time>nextAttack)
                {
                    actor.attackBusy=true; actor.attackRemaining=0.8f;
                    actor.revision++; session.PublishActor(actor);
                }
            }
        }
    }
}
