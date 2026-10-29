using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // Raiders this peer owns: RaiderBrain decides at the original's 30 frames per second, a movement controller of the
    // raider's own (its drop and ladder gestures are per body) moves it each frame. The decision memory stays here, with
    // the owner; an unseen raider's entry is dropped after a few seconds.
    public sealed class NpcRaiderController
    {
        private const int MaxFramesPerStep=3,ForgetFrames=300;
        private sealed class Entry
        {
            public RaiderMind mind=new();public MovementController movement;public MovementSettings settings;
            public int generation;public float clock;public int seen;
            public RaiderIntent intent;
        }
        private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
        private readonly List<string> forgotten=new();
        private readonly MovementSettings baseSettings;
        private readonly System.Random random=new();
        private int frame;
        public NpcRaiderController(MovementSettings baseSettings)=>this.baseSettings=baseSettings;

        // The raider's own controller (also for the frames of an attack already begun).
        public MovementController Movement(ActorState actor,ActorTypeDefinition type,RoomLayout room)=>Get(actor,type,room).movement;

        // One frame of a raider that is awake and not attacking. True when its brain wants a shot now.
        public bool Step(ActorState actor,ActorTypeDefinition type,ActorState target,bool visible,RoomLayout room,float dt)
        {
            var entry=Get(actor,type,room);var settings=type.raider;
            entry.clock+=dt;
            int frames=Mathf.Min(MaxFramesPerStep,Mathf.FloorToInt(entry.clock/RaiderBrain.FrameSeconds));
            entry.clock=frames==MaxFramesPerStep?0:entry.clock-frames*RaiderBrain.FrameSeconds;
            bool sees=visible && target!=null && target.connected && !target.down;
            var memory=actor.perception;
            var senses=new RaiderSenses(sees,sees?target.Position+Vector2.up*type.perception.targetHeight:default,
                memory.hasKnownPosition,memory.KnownPosition+Vector2.up*type.perception.targetHeight);
            bool shoot=false;
            for(int i=0;i<frames;i++)
            {
                var hull=Anatomy.Movement(actor);
                // A wall ahead (turnX): the body one probe further along its way, above the step height, does not fit.
                bool wall=!ActorTerrainMotion.IsClear(room,hull,actor.Position+new Vector2(entry.mind.direction*.05f,entry.settings.stepHeight+.02f));
                var intent=RaiderBrain.Tick(entry.mind,settings,actor,room,senses,wall,random);
                shoot|=intent.shoot;
                // One-shot parts wait for the movement step; the steady ones are the latest decision.
                if(entry.intent.jump>0 && intent.jump<=0) {intent.jump=entry.intent.jump;intent.jumpPush=entry.intent.jumpPush;}
                if(entry.intent.pushX!=0 && intent.pushX==0) intent.pushX=entry.intent.pushX;
                entry.intent=intent;
            }
            var decided=entry.intent;
            if(decided.pushX!=0) actor.vx=decided.pushX;
            entry.settings.jumpSpeed=settings.jumpSpeed*Mathf.Max(decided.jump,.01f);
            bool launch=decided.jump>0;
            entry.movement.Step(actor,new Vector2(decided.move,decided.climb),launch,dt,sprint:decided.run,
                lookAtX:actor.climbing?(float?)null:actor.x+entry.mind.facing,dropThrough:decided.dropThrough);
            if(launch && decided.jumpPush!=0 && actor.vy>0) actor.vx+=decided.jumpPush;
            entry.intent.jump=0;entry.intent.jumpPush=0;entry.intent.pushX=0;
            NpcWeaponAim.Apply(actor,sees?target.BodyPosition+Vector2.up*.55f:memory.hasKnownPosition?memory.KnownPosition+Vector2.up*.55f:null,room);
            return shoot;
        }

        // Once per NpcController step: forget raiders not stepped for a while (asleep, handed over, removed).
        public void EndFrame()
        {
            frame++;
            if(frame%60!=0) return;
            forgotten.Clear();
            foreach(var pair in entries) if(frame-pair.Value.seen>ForgetFrames) forgotten.Add(pair.Key);
            foreach(string id in forgotten) entries.Remove(id);
        }

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
