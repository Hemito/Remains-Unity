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
        // changed: a state other peers must see (a zombie's buried, digging or flaring mode) — publish the actor.
        public readonly bool changed;
        public RaiderActions(bool weapon,float bodyHit,bool changed=false) {this.weapon=weapon;this.bodyHit=bodyHit;this.changed=changed;}
    }

    // Raiders this peer owns: PonyBrain (sapient raiders), RaiderBrain (the robobrain) or ZombieBrain decides at the
    // original's 30 frames per second, a movement controller of the raider's own (its drop and ladder gestures are per
    // body) moves it each frame. The decision memory, the swing being
    // swept and the touch pause stay here, with the owner; an unseen raider's entry is dropped after a few seconds.
    public sealed class NpcRaiderController
    {
        private const int MaxFramesPerStep=3,ForgetFrames=300;
        // Weapon.actions for a non-player owner: a levitated weapon hangs 40 px ahead at the weapon's height.
        private const float HoldAhead=.8f;
        private sealed class Entry
        {
            public RaiderMind mind=new();public MovementController movement;public MovementSettings settings;
            public PonyMind pony;   // a sapient raider's utility memory (PonyBrain)
            public int generation;public float clock;public int seen;
            public RaiderIntent intent;
            public string swingAttack;public MeleeSweep swing;
            public float touchReady;
        }
        private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
        private readonly List<string> forgotten=new();
        private readonly PonyStrikes strikes=new();
        private readonly MovementSettings baseSettings;
        private readonly System.Random random;
        private int frame;private float time;
        // random: a seeded source for checks; the game's decisions are not reproduced across peers (owner-local).
        public NpcRaiderController(MovementSettings baseSettings,System.Random random=null)
        {this.baseSettings=baseSettings;this.random=random??new System.Random();}

        // What a sapient raider this peer owns intends now (diagnostics and probes); null for others and unknown ones.
        public PonyIntentKind? Intent(string actorId)=>entries.TryGetValue(actorId,out var entry) && entry.pony!=null?entry.pony.intent:null;

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
            bool sapient=!settings.zombie && SapientPonies.Is(actor);
            if(sapient)
            {
                entry.pony??=new PonyMind();
                // Its full health: the factory's level profile, else the most it has had with this owner.
                entry.pony.maxHealth=actor.factoryEnemy?.maxHealth>0?actor.factoryEnemy.maxHealth:Mathf.Max(entry.pony.maxHealth,actor.health);
            }
            for(int i=0;i<frames;i++)
            {
                var hull=Anatomy.Movement(actor);
                // A wall ahead (turnX): the body one probe further along its way, above the step height, does not fit.
                bool wall=!ActorTerrainMotion.IsClear(room,hull,actor.Position+new Vector2(entry.mind.direction*.05f,entry.settings.stepHeight+.02f));
                var intent=settings.zombie?ZombieBrain.Tick(entry.mind,settings,actor,room,senses,wall,random):
                    sapient?PonyBrain.Tick(entry.pony,entry.mind,settings,actor,room,senses,wall,random,attacker):
                    RaiderBrain.Tick(entry.mind,settings,actor,room,senses,wall,random,attacker);
                weapon|=intent.shoot || intent.swing;bodyHit=Mathf.Max(bodyHit,intent.bodyHit);
                // One-shot parts wait for the movement step; the steady ones are the latest decision.
                if(entry.intent.jump>0 && intent.jump<=0) {intent.jump=entry.intent.jump;intent.jumpPush=entry.intent.jumpPush;}
                if(entry.intent.pushX!=0 && intent.pushX==0) intent.pushX=entry.intent.pushX;
                entry.intent=intent;
            }
            var decided=entry.intent;
            bool changed=false;
            if(settings.zombie)
            {
                string mode=ZombieBrain.Mode(entry.mind.state);
                if(actor.creature?.mode!=mode) {actor.creature??=new NpcCreatureState();actor.creature.mode=mode;actor.revision++;changed=true;}
                // Buried or digging out it does not move at all (fixed).
                if(entry.mind.state is RaiderState.Buried or RaiderState.Emerging)
                {
                    actor.vx=actor.vy=0;ActorPose.Select(actor,entry.mind.state==RaiderState.Emerging?ZombieBrain.EmergingMode:"idle");
                    return new RaiderActions(false,0,changed);
                }
            }
            if(decided.pushX!=0) actor.vx=decided.pushX;
            entry.settings.jumpSpeed=settings.jumpSpeed*Mathf.Max(decided.jump,.01f);
            entry.settings.runSpeed=settings.RunSpeed*(decided.charge?1.5f:decided.slowRun?.6f:1);
            bool launch=decided.jump>0;
            entry.movement.Step(actor,new Vector2(decided.move,decided.climb),launch,dt,sprint:decided.run,
                lookAtX:actor.climbing?(float?)null:actor.x+entry.mind.facing,dropThrough:decided.dropThrough);
            if(launch && decided.jumpPush!=0 && actor.vy>0) actor.vx+=decided.jumpPush;
            entry.intent.jump=0;entry.intent.jumpPush=0;entry.intent.pushX=0;
            if(Levitated(actor)) WeaponPose.HoldAt(actor,WeaponPose.Origin(actor)+Vector2.right*(entry.mind.facing*HoldAhead));
            else NpcWeaponAim.Apply(actor,sees?target.BodyPosition+Vector2.up*.55f:memory.hasKnownPosition?memory.KnownPosition+Vector2.up*.55f:null,room);
            // UnitZombie.animate: trot while searching on the move.
            if(settings.zombie && entry.mind.state==RaiderState.Search && actor.pose is "walk" or "run") ActorPose.Select(actor,"trot");
            return new RaiderActions(weapon,bodyHit,changed);
        }

        // attKorp: a touch in reach. The victim's window (neujaz) decides whether it lands (CombatController.ApplyVictim);
        // this pause of the same length only keeps one attacker from sending a touch every frame.
        public AttackEvent Touch(ActorState actor,ActorTypeDefinition type,ActorState target,RoomLayout room,float factor)
        {
            var entry=Get(actor,type,room);
            if(time<entry.touchReady) return null;
            var attack=NpcNaturalAttack.Touch(actor,target,NpcNaturalAttack.Variation(factor,random));
            if(attack!=null) entry.touchReady=time+(target.player?1:2f/3);
            return attack;
        }

        // A sapient raider's body hit is the players' strike (user 2026-10-31: shared actions): a punch at a target ahead,
        // a kick at one behind. The robobrain and zombies on this brain keep the touch (PonyStrikeRules.Allowed). The
        // action is the player's; how often the raider chooses it keeps attKorp's pace, once per the victim's window.
        public bool Strike(ActorState actor,ActorTypeDefinition type,ActorState target,RoomLayout room)
        {
            var entry=Get(actor,type,room);
            if(time<entry.touchReady) return false;
            bool punch=PonyStrikeRules.InReach(actor,target,false),kick=!punch && PonyStrikeRules.InReach(actor,target,true);
            if(!(punch || kick) || !strikes.Begin(actor,kick,room)) return false;
            entry.touchReady=time+(target.player?1:2f/3);
            return true;
        }
        public bool Striking(ActorState actor)=>strikes.Busy(actor);
        public void StepStrikes(WorldState world,string owner,float dt,Func<string,bool> paused,Action<AttackEvent> fire)=>strikes.Step(world,owner,dt,paused,fire);

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

        // The movement of a raider-brained type: its own walk, run, jump and acceleration (AllData move). A jump of 0 (the
        // robobrain) never launches — the brain decides no jump — so the controller keeps the player's positive value.
        public static MovementSettings Movement(MovementSettings player,ActorTypeDefinition type)
        {
            var settings=Remains.Serialization.StateJson.Copy(player);
            settings.walkSpeed=type.raider.walkSpeed;settings.runSpeed=type.raider.RunSpeed;
            if(type.raider.jumpSpeed>0) settings.jumpSpeed=type.raider.jumpSpeed;
            settings.groundAcceleration=type.raider.acceleration*30;   // px per frame² → m/s²
            return settings;
        }
        private Entry Get(ActorState actor,ActorTypeDefinition type,RoomLayout room)
        {
            if(type.raider==null) throw new InvalidOperationException($"Тип {type.id} не рейдер: actor={actor.id}.");
            if(!entries.TryGetValue(actor.id,out var entry) || entry.generation!=actor.generation || !ReferenceEquals(entry.movement.Room,room))
            {
                var settings=Movement(baseSettings,type);
                entry=new Entry {movement=new MovementController(room,settings),settings=settings,generation=actor.generation};
                entry.mind.direction=entry.mind.wantedFacing=entry.mind.facing=actor.facing<0?-1:1;
                // A new owner keeps a buried or digging zombie so; a flare in progress ends (pursuit).
                if(actor.creature?.mode==ZombieBrain.BuriedMode) entry.mind.state=RaiderState.Buried;
                else if(actor.creature?.mode==ZombieBrain.EmergingMode) {entry.mind.state=RaiderState.Emerging;entry.mind.timer=24;}
                entries[actor.id]=entry;
            }
            entry.seen=frame;
            return entry;
        }
    }
}
