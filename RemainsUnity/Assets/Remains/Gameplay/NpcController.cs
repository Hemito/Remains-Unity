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
        public Func<string,Vector2,float?> LevelBrightness {set=>senses.LevelBrightness=value;}
        private readonly Remains.Content.MovementSettings movementSettings;
        private readonly Dictionary<(string level,string type),MovementController> movement=new();
        private readonly NpcCreatureController creatures=new();
        private readonly NpcRaiderController raiders;
        private readonly Dictionary<string,List<ActorState>> downed=new(StringComparer.Ordinal);
        // A raider revives a downed ally of its kind within this distance on its level.
        private const float ReviveSearch=10;
        private readonly WeaponRechargeController recharge;
        // The owner's damage receiver: a spider mine destroys itself through it (NpcCreatureController.Detonates).
        private readonly CombatController combat;
        public NpcController(CoopSession session,SimulationGeometry geometry,CombatController combat=null)
        {
            this.session=session;this.geometry=geometry;this.combat=combat;
            recharge=new WeaponRechargeController(session);
            movementSettings=Remains.Content.MovementSettings.Load();
            raiders=new NpcRaiderController(movementSettings);
            senses=new NpcSenses(session,geometry);
            if(combat!=null) combat.NpcDamaged+=Alarm;
            session.AttackReceived+=raiders.Heard;
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
                    movement[key]=new MovementController(geometry.Get(value.level),Movement(movementSettings,Remains.Content.ActorTypes.Get(value.type)));
                }
            };
        }
        // The ground movement of an NPC type: the player's settings with a natural attacker's approach speed, or the walk
        // and jump of a walking family's own motion (spider mine, roller: AllData move). Unity serializes the motion of
        // every type, so an unauthored one is all zeros and keeps the player's values.
        public static Remains.Content.MovementSettings Movement(Remains.Content.MovementSettings player,Remains.Content.ActorTypeDefinition definition)
        {
            var settings=Remains.Serialization.StateJson.Copy(player);
            bool walks=!NpcCreatureBehaviours.Flies(definition.behavior);
            if(definition.naturalAttack!=null) {settings.walkSpeed=definition.naturalAttack.approachSpeed;settings.runSpeed=Math.Max(settings.runSpeed,settings.walkSpeed);}
            else if(walks && definition.motion?.walkSpeed>0) {settings.walkSpeed=definition.motion.walkSpeed;settings.runSpeed=Math.Max(settings.runSpeed,settings.walkSpeed);}
            if(walks && definition.motion?.jumpSpeed>0) settings.jumpSpeed=definition.motion.jumpSpeed;
            return settings;
        }
        private readonly System.Random turretRandom=new();
        private readonly ConnectedPlayers connected=new();
        // Unit.damage calls alarma: a turret shot from behind must not go on sweeping.
        private void Alarm(ActorState actor,AttackEvent attack)
        {
            var type=Remains.Content.ActorTypes.Get(actor.actorType);
            var attacker=session.World.Find(attack.attacker);
            if(actor.down || !NpcCreatureBehaviours.Fixed(type.behavior) || attacker==null || !attacker.player) return;
            if(NpcTurret.Alarm(actor,type,attacker.Position)) {senses.Alarm(actor,attacker);actor.revision++;}
        }
        public void Step(float dt)
        {
            if(session.World==null) return;
            connected.Refresh(session.World);
            // Downed sapient NPCs by level, once a frame: the raiders' allies to revive (a short list, not all against all).
            foreach(var list in downed.Values) list.Clear();
            foreach(var body in session.World.actors)
                if(body.down && !body.player && connected.OnLevel(body.level) && SapientPonies.Is(body))
                {if(!downed.TryGetValue(body.level,out var list)) downed[body.level]=list=new List<ActorState>();list.Add(body);}
            foreach(var actor in session.World.actors)
            {
                if(actor.player || actor.owner!=session.LocalOwner || actor.down) continue;
                // The cheap activity filters first: most NPCs of a factory sleep far from every player.
                if(!connected.OnLevel(actor.level) || !NpcActivity.Awake(connected.Players,actor)) continue;
                var type=Remains.Content.ActorTypes.Get(actor.actorType);
                if(ActorHoldRules.Active(actor) && !NpcCreatureBehaviours.Fixed(type.behavior)) continue;
                if(!type.combatAi) continue;
                bool natural=type.naturalAttack!=null;
                if(session.Pause.IsPaused(actor.level)) continue;
                if(!movement.TryGetValue((actor.level,actor.actorType),out var controller))
                    throw new InvalidOperationException($"Не загружена геометрия симуляции NPC: actor={actor.id}, level={actor.level}.");
                senses.Step(actor,dt);
                var target=session.World.Find(actor.attackBusy?actor.attackTarget:actor.target);
                bool visible=target!=null && target.level==actor.level && RoomVisibility.Clear(geometry.Get(actor.level),
                    natural?NpcNaturalAttack.Origin(actor):WeaponPose.RangedAnchor(actor),target.BodyPosition+Vector2.up*(natural?type.perception.targetHeight:.55f));
                if(!natural) recharge.Step(actor,dt,actor.attackBusy?dt:actor.attackCooldown);
                actor.attackCooldown=Mathf.Max(0,actor.attackCooldown-dt);
                bool family=NpcCreatureBehaviours.IsFamily(type.behavior);
                bool raider=NpcCreatureBehaviours.UsesRaiderBrain(type.behavior);
                // A raider moves with a controller of its own (RaiderBrain); the others share their type's.
                if(raider) controller=raiders.Movement(actor,type,controller.Room);
                if(actor.attackBusy)
                {
                    if(family && NpcCreatureBehaviours.Flies(type.behavior)) creatures.Coast(actor,type,controller.Room,dt);
                    else if(!NpcCreatureBehaviours.Fixed(type.behavior)) controller.Step(actor,Vector2.zero,false,dt,lookAtX:!natural && visible?target.x:(float?)null);
                    if(NpcCreatureBehaviours.Fixed(type.behavior)) NpcTurret.Follow(actor,type,target,visible,controller.Room,dt);
                    // A raider's swing is swept by its owner over the action; the weapon is not re-aimed meanwhile.
                    else if(raider && actor.npcWeaponAction?.kind==NpcWeaponActions.Swing) raiders.Swing(actor,type,controller.Room,dt,session.Fire);
                    else if(!natural) NpcWeaponAim.Apply(actor,visible?target.BodyPosition+Vector2.up*.55f:null,controller.Room);
                    actor.attackRemaining-=dt;
                    if(actor.attackRemaining<=0)
                    {
                        // The shot as its volley, as a player's (pellet weapons fire all their pellets).
                        System.Collections.Generic.List<AttackEvent> volley=null;
                        var attack=NpcAttackRules.Complete(actor,target,geometry.Get(actor.level),session.Rules.NpcWeaponTiming,shot=>(volley=session.PrepareVolley(shot))!=null);
                        if(attack!=null)
                        {
                            if(NpcNaturalAttack.IsNatural(attack)) session.FirePrepared(attack);
                            else session.FireVolley(volley);
                        }
                        session.PublishActor(actor);
                    }
                    continue;
                }
                Vector2? destination=visible?target.Position:
                    actor.perception.hasKnownPosition?actor.perception.KnownPosition:null;
                if(NpcCreatureBehaviours.Fixed(type.behavior))
                {
                    // A turret neither walks nor falls: it watches, notices, attacks and searches (NpcTurret).
                    bool fire=NpcTurret.Step(actor,type,target,visible,actor.perception.hasKnownPosition?actor.perception.KnownPosition:(Vector2?)null,controller.Room,dt,turretRandom);
                    if(fire && target.connected && !target.down && NpcAttackRules.TryBegin(actor,target,session.Rules.NpcWeaponTiming))
                        session.PublishActor(actor);
                    continue;
                }
                if(family)
                {
                    bool wants=creatures.Step(actor,type,target,destination,controller.Room,controller,dt);
                    if(type.behavior==NpcCreatureBehaviours.SpiderMine)
                    {
                        if(NpcCreatureController.Detonates(actor,target))
                        {
                            if(combat==null) throw new InvalidOperationException($"Самоподрыв мины-паука без получателя урона: actor={actor.id}.");
                            if(combat.SelfDestruct(actor,"self-destruct:"+actor.id+":"+actor.lifecycleSequence)) session.PublishActor(actor);
                        }
                        continue;
                    }
                    // A flying gun (spritebot) turns its gun to the target it sees, else holds it ahead.
                    if(type.hasWeapon) NpcWeaponAim.Apply(actor,visible?target.BodyPosition+Vector2.up*.55f:(Vector2?)null,controller.Room);
                    if(wants && visible && target.connected && !target.down && NpcAttackRules.TryBegin(actor,target,session.Rules.NpcWeaponTiming))
                        session.PublishActor(actor);
                    continue;
                }
                if(natural)
                {
                    controller.Step(actor,new Vector2(NpcNaturalAttack.Approach(actor,destination,type.naturalAttack.approachSpeed),0),false,dt,
                        lookAtX:destination.HasValue && Mathf.Abs(destination.Value.x-actor.x)>.01f?destination.Value.x:(float?)null);
                    if(visible && target.connected && !target.down && NpcAttackRules.TryBegin(actor,target,session.Rules.NpcWeaponTiming))
                        session.PublishActor(actor);
                    continue;
                }
                if(destination.HasValue)
                {
                    var door=NpcDoorIntent.Select(actor,destination.Value,session.World.doors,geometry.Get(actor.level));
                    if(door!=null) session.Doors.RequestNpcOpen(actor,door.id);
                }
                if(raider)
                {
                    var wants=raiders.Step(actor,type,target,visible && target.level==actor.level,controller.Room,dt,NearestDowned(actor),session.World.actors);
                    if(wants.changed) session.PublishActor(actor);
                    if(wants.revive!=null) session.RequestNpcRevive(actor,wants.revive);
                    if(wants.heal) session.RequestNpcSelfHeal(actor);
                    if(wants.grenade.valid) session.RequestNpcThrow(actor,wants.grenade);
                    bool ready=visible && target.level==actor.level && target.connected && !target.down;
                    if(raiders.Striking(actor)) continue;
                    if(wants.weapon && ready && actor.attackCooldown<=0 && NpcAttackRules.TryBegin(actor,target,session.Rules.NpcWeaponTiming))
                        session.PublishActor(actor);
                    else if(wants.bodyHit>0 && ready && PonyStrikeRules.Allowed(actor)) raiders.Strike(actor,type,target,controller.Room);
                    else if(wants.bodyHit>0 && ready && raiders.Touch(actor,type,target,controller.Room,wants.bodyHit) is {} touch)
                        session.FirePrepared(touch);
                    continue;
                }
                if(!visible || target.level!=actor.level || target.down || !target.connected)
                {
                    var memory=actor.perception;
                    float search=memory.hasKnownPosition?memory.knownX-actor.x:0;
                    controller.Step(actor,new Vector2(Mathf.Abs(search)>.25f?Mathf.Sign(search)*.5f:0,0),false,dt,
                        lookAtX:memory.hasKnownPosition && Mathf.Abs(search)>.01f?memory.knownX:(float?)null);
                    NpcWeaponAim.Apply(actor,memory.hasKnownPosition?memory.KnownPosition+Vector2.up*.55f:null,controller.Room);
                    continue;
                }
                float distance=target.x-actor.x;
                controller.Step(actor,new Vector2(Mathf.Abs(distance)>5?Mathf.Sign(distance)*0.5f:0,0),false,dt,lookAtX:target.x);
                NpcWeaponAim.Apply(actor,target.BodyPosition+Vector2.up*.55f,controller.Room);
                if(visible && Mathf.Abs(distance)<7 && actor.attackCooldown<=0)
                {
                    if(NpcAttackRules.TryBegin(actor,target,session.Rules.NpcWeaponTiming)) session.PublishActor(actor);
                }
            }
            raiders.StepStrikes(session.World,session.LocalOwner,dt,session.Pause.IsPaused,session.Fire);
            raiders.EndFrame(dt);
        }

        private ActorState NearestDowned(ActorState actor)
        {
            if(!downed.TryGetValue(actor.level,out var list)) return null;
            ActorState best=null;float nearest=ReviveSearch*ReviveSearch;
            foreach(var body in list)
            {
                float d=(body.Position-actor.Position).sqrMagnitude;
                if(body.id!=actor.id && d<nearest) {nearest=d;best=body;}
            }
            return best;
        }
    }
}
