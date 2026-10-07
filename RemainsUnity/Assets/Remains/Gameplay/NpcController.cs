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
        private readonly SimulationGeometry geometry;
        private readonly NpcSenses senses;
        private readonly Remains.Content.MovementSettings movementSettings;
        private readonly Dictionary<(string level,string type),MovementController> movement=new();
        public NpcController(CoopSession session,SimulationGeometry geometry)
        {
            this.session=session;this.geometry=geometry;
            movementSettings=Remains.Content.MovementSettings.Load();
            senses=new NpcSenses(session,geometry);
            session.Changed+=()=>
            {
                var needed=session.World==null?Array.Empty<(string level,string type)>():session.World.actors
                    .Where(a=>!a.player && Remains.Content.ActorTypes.Get(a.actorType).combatAi && a.owner==session.LocalOwner && !a.down && LevelActivity.HasConnectedPlayer(session.World,a.level))
                    .Select(a=>(level:a.level,type:a.actorType)).Distinct().ToArray();
                var keys=needed.ToHashSet();
                foreach(var key in movement.Keys.Where(key=>!keys.Contains(key)).ToArray()) movement.Remove(key);
                foreach(var value in needed)
                {
                    var key=value;
                    if(movement.TryGetValue(key,out var controller) && ReferenceEquals(controller.Room,geometry.Get(value.level))) continue;
                    var settings=Remains.Serialization.StateJson.Copy(movementSettings);
                    var natural=Remains.Content.ActorTypes.Get(value.type).naturalAttack;
                    if(natural!=null) {settings.walkSpeed=natural.approachSpeed;settings.runSpeed=Math.Max(settings.runSpeed,settings.walkSpeed);}
                    movement[key]=new MovementController(geometry.Get(value.level),settings);
                }
            };
        }
        public void Step(float dt)
        {
            if(session.World==null) return;
            foreach(var actor in session.World.actors)
            {
                if(actor.player || actor.owner!=session.LocalOwner || actor.down) continue;
                if(ActorHoldRules.Active(actor)) continue;
                var type=Remains.Content.ActorTypes.Get(actor.actorType);
                if(!type.combatAi) continue;
                bool natural=type.naturalAttack!=null;
                if(!LevelActivity.HasConnectedPlayer(session.World,actor.level)) continue;
                if(session.Pause.IsPaused(actor.level)) continue;
                if(!movement.TryGetValue((actor.level,actor.actorType),out var controller))
                    throw new InvalidOperationException($"Не загружена геометрия симуляции NPC: actor={actor.id}, level={actor.level}.");
                senses.Step(actor,dt);
                var target=session.World.Find(actor.attackBusy?actor.attackTarget:actor.target);
                bool visible=target!=null && target.level==actor.level && RoomVisibility.Clear(geometry.Get(actor.level),
                    natural?NpcNaturalAttack.Origin(actor):WeaponPose.RangedAnchor(actor),target.Position+Vector2.up*(natural?type.perception.targetHeight:.55f));
                actor.attackCooldown=Mathf.Max(0,actor.attackCooldown-dt);
                if(actor.attackBusy)
                {
                    controller.Step(actor,Vector2.zero,false,dt);
                    if(!natural) NpcWeaponAim.Apply(actor,visible?target.Position+Vector2.up*.55f:null,controller.Room);
                    actor.attackRemaining-=dt;
                    if(actor.attackRemaining<=0)
                    {
                        var attack=NpcAttackRules.Complete(actor,target,geometry.Get(actor.level),session.Rules.NpcWeaponTiming,session.PrepareProjectile);
                        if(attack!=null) session.FirePrepared(attack);
                        session.PublishActor(actor);
                    }
                    continue;
                }
                Vector2? destination=visible?target.Position:
                    actor.perception.hasKnownPosition?actor.perception.KnownPosition:null;
                if(natural)
                {
                    controller.Step(actor,new Vector2(NpcNaturalAttack.Approach(actor,destination,type.naturalAttack.approachSpeed),0),false,dt);
                    if(destination.HasValue && Mathf.Abs(destination.Value.x-actor.x)>.01f) actor.facing=destination.Value.x>=actor.x?1:-1;
                    if(visible && target.connected && !target.down && NpcAttackRules.TryBegin(actor,target,session.Rules.NpcWeaponTiming))
                        session.PublishActor(actor);
                    continue;
                }
                if(destination.HasValue)
                {
                    var door=NpcDoorIntent.Select(actor,destination.Value,session.World.doors,geometry.Get(actor.level));
                    if(door!=null) session.Doors.RequestNpcOpen(actor,door.id);
                }
                if(!visible || target.level!=actor.level || target.down || !target.connected)
                {
                    var memory=actor.perception;
                    float search=memory.hasKnownPosition?memory.knownX-actor.x:0;
                    controller.Step(actor,new Vector2(Mathf.Abs(search)>.25f?Mathf.Sign(search)*.5f:0,0),false,dt);
                    if(Mathf.Abs(search)>.01f) actor.facing=search>=0?1:-1;
                    NpcWeaponAim.Apply(actor,memory.hasKnownPosition?memory.KnownPosition+Vector2.up*.55f:null,controller.Room);
                    continue;
                }
                float distance=target.x-actor.x;
                controller.Step(actor,new Vector2(Mathf.Abs(distance)>5?Mathf.Sign(distance)*0.5f:0,0),false,dt);
                actor.facing=distance>=0?1:-1;
                NpcWeaponAim.Apply(actor,target.Position+Vector2.up*.55f,controller.Room);
                if(visible && Mathf.Abs(distance)<7 && actor.attackCooldown<=0)
                {
                    if(NpcAttackRules.TryBegin(actor,target,session.Rules.NpcWeaponTiming)) session.PublishActor(actor);
                }
            }
        }
    }
}
