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
        // revive: the downed ally this raider has finished reviving — the owner asks the host (CoopSession.RequestNpcRevive).
        public readonly ActorState revive;
        // heal: the raider uses a medkit on itself now — the owner asks the host (CoopSession.RequestNpcSelfHeal).
        public readonly bool heal;
        // grenade: the throw the raider lets go now (GrenadeAim) — the owner asks the host (CoopSession.RequestNpcThrow).
        public readonly GrenadeThrow grenade;
        public RaiderActions(bool weapon,float bodyHit,bool changed=false,ActorState revive=null,bool heal=false,GrenadeThrow grenade=default)
        {this.weapon=weapon;this.bodyHit=bodyHit;this.changed=changed;this.revive=revive;this.heal=heal;this.grenade=grenade;}
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
        // A grenade leaves 0.3 m above the body point; a shot within NearShotDistance metres frightens a sapient raider.
        private const float ThrowLift=.3f,NearShotDistance=1.5f;
        private sealed class Entry
        {
            public RaiderMind mind=new();public MovementController movement;public MovementSettings settings;
            public PonyMind pony;   // a sapient raider's utility memory (PonyBrain)
            public ActorState actor;public int damageSeen=-1;   // the raider itself and the last damage it has felt
            public readonly RevivalProgress revival=new();public float revivalPending,healPending;
            public int generation;public float clock;public int seen;
            public int firingStanceFrames;
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

        // How endangered a sapient raider this peer owns feels (checks); 0 for others.
        public float Danger(string actorId)=>entries.TryGetValue(actorId,out var entry) && entry.pony!=null?entry.pony.danger:0;
        // What a sapient raider this peer owns intends now (diagnostics and probes); null for others and unknown ones.
        public PonyIntentKind? Intent(string actorId)=>entries.TryGetValue(actorId,out var entry) && entry.pony!=null?entry.pony.intent:null;

        // The raider's own controller (also for the frames of an attack already begun).
        public MovementController Movement(ActorState actor,ActorTypeDefinition type,RoomLayout room)=>Get(actor,type,room).movement;

        // One frame of a raider that is awake and not attacking. `ally`: the nearest downed sapient ally the caller found.
        public RaiderActions Step(ActorState actor,ActorTypeDefinition type,ActorState target,bool visible,RoomLayout room,float dt,ActorState ally=null,IReadOnlyList<ActorState> actors=null)
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
            var friend=sapient && ally!=null?new PonyAlly(true,ally.Position,InventoryJournal.Count(actor,"heal")>0):PonyAlly.None;
            if(sapient)
            {
                entry.pony??=new PonyMind();
                // A hit since the last frame frightens it (danger: how long it dares to aim a grenade).
                if(entry.damageSeen>=0 && actor.damageSequence!=entry.damageSeen) PonyBrain.Threat(entry.pony,PonyBrain.Hit);
                entry.damageSeen=actor.damageSequence;
                // Its full health: the factory's level profile, else the most it has had with this owner.
                entry.pony.maxHealth=actor.factoryEnemy?.maxHealth>0?actor.factoryEnemy.maxHealth:Mathf.Max(entry.pony.maxHealth,actor.health);
                entry.pony.blocked=sees && actors!=null && frames>0 && NpcLineOfFire.Blocked(actors,actor,target,
                    attacker==RaiderAttacker.Ranged?WeaponPose.RangedAnchor(actor):actor.BodyPosition+Vector2.up*.55f,
                    target.BodyPosition+Vector2.up*.55f,attacker==RaiderAttacker.Ranged);
            }
            for(int i=0;i<frames;i++)
            {
                var hull=Anatomy.Movement(actor);
                // A wall ahead (turnX): the body one probe further along its way, above the step height, does not fit.
                bool wall=!ActorTerrainMotion.IsClear(room,hull,actor.Position+new Vector2(entry.mind.direction*.05f,entry.settings.stepHeight+.02f));
                var intent=settings.zombie?ZombieBrain.Tick(entry.mind,settings,actor,room,senses,wall,random):
                    sapient?PonyBrain.Tick(entry.pony,entry.mind,settings,actor,room,senses,wall,random,attacker,friend):
                    RaiderBrain.Tick(entry.mind,settings,actor,room,senses,wall,random,attacker);
                weapon|=intent.shoot || intent.swing;bodyHit=Mathf.Max(bodyHit,intent.bodyHit);
                // One-shot parts wait for the movement step; the steady ones are the latest decision.
                if(entry.intent.jump>0 && intent.jump<=0) {intent.jump=entry.intent.jump;intent.jumpPush=entry.intent.jumpPush;}
                if(entry.intent.pushX!=0 && intent.pushX==0) intent.pushX=entry.intent.pushX;
                entry.intent=intent;
            }
            var decided=entry.intent;
            bool changed=sapient && UpdateFiringStance(entry,actor,target,sees,attacker,room,actors,frames);
            if(ActorPose.IsTacticallyProne(actor))
            {
                actor.vx=0;
                entry.movement.Step(actor,Vector2.zero,false,dt);
                NpcWeaponAim.Apply(actor,sees?target.BodyPosition+Vector2.up*.55f:null,room);
                entry.intent=default;
                return new RaiderActions(weapon,0,changed);
            }
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
            return new RaiderActions(weapon,bodyHit,changed,Revive(entry,actor,ally,decided.revive,dt),Heal(entry,actor,decided.heal),
                decided.grenade && sees && GrenadeItems.Next(actor) is { } grenade?
                    GrenadeAim.Plan(room,actor.BodyPosition+Vector2.up*ThrowLift,actor.BodyPosition,target.BodyPosition,decided.grenadeError,random,grenade.definition):default);
        }

        private bool UpdateFiringStance(Entry entry,ActorState actor,ActorState target,bool sees,RaiderAttacker attacker,
            RoomLayout room,IReadOnlyList<ActorState> actors,int frames)
        {
            bool prone=ActorPose.IsTacticallyProne(actor);
            bool canYield=sees && actors!=null && attacker==RaiderAttacker.Ranged && !actor.climbing &&
                !actor.crouched && entry.pony.intent==PonyIntentKind.Shoot && !Striking(actor);
            if(canYield)
            {
                entry.firingStanceFrames-=frames;
                if(frames==0 || entry.firingStanceFrames>0) return false;
                entry.firingStanceFrames=PonyBrain.DecisionFrames;
                if((prone || Mathf.Abs(actor.vy)<.01f && RaiderLegs.Standing(room,actor) && Anatomy.CanLieProne(actor,room)) &&
                    NpcLineOfFire.ShouldLieProne(actors,actor,target,room))
                {
                    if(prone) return false;
                    ActorPose.Select(actor,ActorPose.TacticalProne);actor.revision++;return true;
                }
            }
            if(!prone || !ActorTerrainMotion.IsClear(room,Anatomy.Movement(actor.anatomy),actor.Position)) return false;
            ActorPose.Select(actor,"idle");actor.revision++;return true;
        }

        // A shot or a blast near a sapient raider this peer owns frightens it: a shot's line passing within NearShot
        // metres, a blast within its reach and as much again. Its own attacks do not count.
        public void Heard(AttackEvent attack)
        {
            bool blast=WeaponCatalog.IsExplosive(attack.weapon);
            float reach=blast?WeaponCatalog.Get(attack.weapon).range+NearShotDistance:NearShotDistance;
            foreach(var entry in entries.Values)
            {
                var actor=entry.actor;
                if(entry.pony==null || actor==null || actor.id==attack.attacker || actor.level!=attack.level) continue;
                var body=actor.BodyPosition;
                float distance=blast?Vector2.Distance(body,attack.End):SegmentDistance(body,attack.Origin,attack.End);
                if(distance<reach) PonyBrain.Threat(entry.pony,PonyBrain.NearShot);
            }
        }
        private static float SegmentDistance(Vector2 point,Vector2 a,Vector2 b)
        {
            var ab=b-a;float t=ab.sqrMagnitude>0?Mathf.Clamp01(Vector2.Dot(point-a,ab)/ab.sqrMagnitude):0;
            return Vector2.Distance(point,a+ab*t);
        }

        // A medkit used at once, as a player's; one request until the host answers or 5 s pass.
        private bool Heal(Entry entry,ActorState actor,bool wanted)
        {
            if(!wanted || entry.healPending>time || !HealingRules.CanApply(actor,actor,true)) return false;
            entry.healPending=time+5;return true;
        }

        // RevivalSeconds over the ally, as a player's hold of E; then one request until the host answers or 5 s pass.
        private ActorState Revive(Entry entry,ActorState actor,ActorState ally,bool working,float dt)
        {
            if(entry.revivalPending>time) return null;
            if(!entry.revival.Step(actor,ally,working && ally!=null,dt)) return null;
            entry.revival.Reset();entry.revivalPending=time+5;
            return ally;
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
            entry.actor=actor;
            entry.seen=frame;
            return entry;
        }
    }
}
