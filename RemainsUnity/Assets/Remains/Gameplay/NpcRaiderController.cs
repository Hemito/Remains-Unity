using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // What a raider's brain wants of its owner this frame: a weapon action (a shot or a swing) and a touch.
    public readonly struct RaiderActions
    {
        public readonly bool weapon;public readonly float bodyHit;
        public RaiderActions(bool weapon,float bodyHit) {this.weapon=weapon;this.bodyHit=bodyHit;}
    }

    // Raiders this peer owns: RaiderBrain decides at the original's 30 frames per second, a movement controller of the
    // raider's own (its drop and ladder gestures are per body) moves it each frame. The decision memory, the swing being
    // swept and the touch pause stay here, with the owner; an unseen raider's entry is dropped after a few seconds.
    public sealed class NpcRaiderController
    {
        private const int MaxFramesPerStep=3,ForgetFrames=300;
        // Weapon.actions for a non-player owner: a levitated weapon hangs 40 px ahead at the weapon's height.
        private const float HoldAhead=.8f;
        private sealed class Entry
        {
            public RaiderMind mind=new();public MovementController movement;public MovementSettings settings;
            public int generation;public float clock;public int seen;
            public RaiderIntent intent;
            public string swingAttack;public MeleeSweep swing;
            public float touchReady;
        }
        private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
        private readonly List<string> forgotten=new();
        private readonly MovementSettings baseSettings;
        private readonly System.Random random;
        private int frame;private float time;
        // random: a seeded source for checks; the game's decisions are not reproduced across peers (owner-local).
        public NpcRaiderController(MovementSettings baseSettings,System.Random random=null)
        {this.baseSettings=baseSettings;this.random=random??new System.Random();}

        // The raider's own controller (also for the frames of an attack already begun).
        public MovementController Movement(ActorState actor,ActorTypeDefinition type,RoomLayout room)=>Get(actor,type,room).movement;

        // One frame of a raider that is awake and not attacking.
        public RaiderActions Step(ActorState actor,ActorTypeDefinition type,ActorState target,bool visible,RoomLayout room,float dt)
        {
            var entry=Get(actor,type,room);var settings=type.raider;
            entry.clock+=dt;
            int frames=Mathf.Min(MaxFramesPerStep,Mathf.FloorToInt(entry.clock/RaiderBrain.FrameSeconds));
            entry.clock=frames==MaxFramesPerStep?0:entry.clock-frames*RaiderBrain.FrameSeconds;
            bool sees=visible && target!=null && target.connected && !target.down;
            var memory=actor.perception;
            var senses=new RaiderSenses(sees,sees?target.Position+Vector2.up*type.perception.targetHeight:default,
                memory.hasKnownPosition,memory.KnownPosition+Vector2.up*type.perception.targetHeight);
            // A shooter with nothing left to shoot or reload with fights with its body (the port's rule, user 2026-10-29).
            var attacker=settings.attacker==RaiderAttacker.Ranged && settings.Touches && NpcWeaponActions.Spent(actor)?RaiderAttacker.Teeth:settings.attacker;
            bool weapon=false;float bodyHit=0;
            for(int i=0;i<frames;i++)
            {
                var hull=Anatomy.Movement(actor);
                // A wall ahead (turnX): the body one probe further along its way, above the step height, does not fit.
                bool wall=!ActorTerrainMotion.IsClear(room,hull,actor.Position+new Vector2(entry.mind.direction*.05f,entry.settings.stepHeight+.02f));
                var intent=RaiderBrain.Tick(entry.mind,settings,actor,room,senses,wall,random,attacker);
                weapon|=intent.shoot || intent.swing;bodyHit=Mathf.Max(bodyHit,intent.bodyHit);
                // One-shot parts wait for the movement step; the steady ones are the latest decision.
                if(entry.intent.jump>0 && intent.jump<=0) {intent.jump=entry.intent.jump;intent.jumpPush=entry.intent.jumpPush;}
                if(entry.intent.pushX!=0 && intent.pushX==0) intent.pushX=entry.intent.pushX;
                entry.intent=intent;
            }
            var decided=entry.intent;
            if(decided.pushX!=0) actor.vx=decided.pushX;
            entry.settings.jumpSpeed=settings.jumpSpeed*Mathf.Max(decided.jump,.01f);
            entry.settings.runSpeed=settings.RunSpeed*(decided.charge?1.5f:1);
            bool launch=decided.jump>0;
            entry.movement.Step(actor,new Vector2(decided.move,decided.climb),launch,dt,sprint:decided.run,
                lookAtX:actor.climbing?(float?)null:actor.x+entry.mind.facing,dropThrough:decided.dropThrough);
            if(launch && decided.jumpPush!=0 && actor.vy>0) actor.vx+=decided.jumpPush;
            entry.intent.jump=0;entry.intent.jumpPush=0;entry.intent.pushX=0;
            if(Levitated(actor)) WeaponPose.HoldAt(actor,WeaponPose.Origin(actor)+Vector2.right*(entry.mind.facing*HoldAhead));
            else NpcWeaponAim.Apply(actor,sees?target.BodyPosition+Vector2.up*.55f:memory.hasKnownPosition?memory.KnownPosition+Vector2.up*.55f:null,room);
            return new RaiderActions(weapon,bodyHit);
        }

        // attKorp: a touch in reach, at most once per the victim's window between touches (neujazMax: 30 frames for a
        // player, 20 for a unit) — kept by the attacker's owner here, not on the victim.
        public AttackEvent Touch(ActorState actor,ActorTypeDefinition type,ActorState target,RoomLayout room,float factor)
        {
            var entry=Get(actor,type,room);
            if(time<entry.touchReady) return null;
            var attack=NpcNaturalAttack.Touch(actor,target,NpcNaturalAttack.Variation(factor,random));
            if(attack!=null) entry.touchReady=time+(target.player?1:2f/3);
            return attack;
        }

        // A swing begun by NpcAttackRules (its action lasts the weapon's rapid): sampled as a player's swing, every contact
        // segment fired. A swing handed over mid-way has no sweep here and only runs out.
        public void Swing(ActorState actor,ActorTypeDefinition type,RoomLayout room,float dt,Action<AttackEvent> fire)
        {
            var entry=Get(actor,type,room);var action=actor.npcWeaponAction;
            if(!actor.attackBusy || action?.kind!=NpcWeaponActions.Swing) {entry.swing=null;return;}
            var weapon=WeaponCatalog.Get(action.weapon);
            if(entry.swingAttack!=actor.attackId)
            {
                entry.swingAttack=actor.attackId;entry.swing=null;
                if(actor.attackRemaining>=weapon.cooldown-.0001f)
                    entry.swing=new MeleeSweep(weapon.id,WeaponPose.Origin(actor),WeaponPose.Direction(actor),actor.weaponReach,room);
            }
            if(entry.swing==null) return;
            var origin=WeaponPose.Origin(actor);string id=actor.attackId;
            entry.swing.Advance(origin,dt,segment=>fire(new AttackEvent {id=id,attacker=actor.id,weapon=weapon.id,level=actor.level,
                factoryEnemy=FactoryEnemyScaling.Capture(actor.factoryEnemy),originX=segment.start.x,originY=segment.start.y,endX=segment.end.x,endY=segment.end.y,
                speed=segment.speed,radius=MeleeSweep.ContactRadius,meleeAnchor=segment.Anchor()}),room);
            var tip=entry.swing.Tip;
            actor.weaponTipX=tip.x;actor.weaponTipY=tip.y;
            if(!weapon.meleeAroundHold)
            {
                actor.weaponReach=Vector2.Distance(tip,origin);
                actor.weaponAngle=Mathf.Atan2(tip.y-origin.y,tip.x-origin.x)*Mathf.Rad2Deg;
            }
            if(entry.swing.Finished) entry.swing=null;
        }

        // Once per NpcController step: forget raiders not stepped for a while (asleep, handed over, removed).
        public void EndFrame(float dt)
        {
            frame++;time+=dt;
            if(frame%60!=0) return;
            forgotten.Clear();
            foreach(var pair in entries) if(frame-pair.Value.seen>ForgetFrames) forgotten.Add(pair.Key);
            foreach(string id in forgotten) entries.Remove(id);
        }

        private static bool Levitated(ActorState actor)=>!string.IsNullOrEmpty(actor.weapon) && WeaponCatalog.IsMelee(actor.weapon) &&
            WeaponCatalog.Get(actor.weapon).meleeAroundHold && !AtlasWeaponSockets.Enabled(actor);

        private Entry Get(ActorState actor,ActorTypeDefinition type,RoomLayout room)
        {
            if(type.raider==null) throw new InvalidOperationException($"Тип {type.id} не рейдер: actor={actor.id}.");
            if(!entries.TryGetValue(actor.id,out var entry) || entry.generation!=actor.generation || !ReferenceEquals(entry.movement.Room,room))
            {
                var settings=Remains.Serialization.StateJson.Copy(baseSettings);
                settings.walkSpeed=type.raider.walkSpeed;settings.runSpeed=type.raider.RunSpeed;settings.jumpSpeed=type.raider.jumpSpeed;
                settings.groundAcceleration=type.raider.acceleration*30;   // px per frame² → m/s²
                entry=new Entry {movement=new MovementController(room,settings),settings=settings,generation=actor.generation};
                entry.mind.direction=entry.mind.wantedFacing=entry.mind.facing=actor.facing<0?-1:1;
                entries[actor.id]=entry;
            }
            entry.seen=frame;
            return entry;
        }
    }
}
