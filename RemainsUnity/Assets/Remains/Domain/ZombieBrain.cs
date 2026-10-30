using System;
using UnityEngine;

namespace Remains.Domain
{
    // UnitZombie.control (1.04 dlc-ui), one call per original frame (30 per second), over the raiders' mind, senses and
    // intent (NpcRaiderController moves the body). Kept close to the source — a temporary AI, as the raider's.
    //   buried (aiState 5, zakop): hidden and fixed; a target it sees or hears wakes it — it digs out for 24 frames (6,
    //           vykop) and pursues;
    //   0/1     stands or strolls (turning at ledges, a half jump to ground 80 px ahead one time in ten);
    //   2/3     searches (×0.6 of its run) or pursues (its run): turns to a point more than 100 px aside eight times in
    //           ten every 15 frames, drops through platforms to a point 80 px below, jumps to one above (checkJump), a
    //           half jump off a ledge, at a wall jumps or turns;
    //   attack  a seen target within optDistAtt across and 80 px up or down is touched (attKorp);
    //   super   a glowing ghoul (ss 5) angered for tZlo frames of pursuit prepares (4, tPrepSuper frames) and flares
    //           (7, tSuper frames: superSila 5), then pursues again.
    // Stage 1 (user 2026-10-30): the other super powers (jump 1, telekinesis 2, venom 3, ripper 4, soldier 6...), rising
    // again (isRes), breaking walls (vDestroy), the flare healing other zombies and the buried senses (vision 0.75,
    // ear 0.2) are not carried over yet.
    public static class ZombieBrain
    {
        // The zombie states other peers must know (NpcCreatureState.mode): hidden in the ground, digging out, flaring.
        public const string BuriedMode="buried",EmergingMode="emerging",FlareMode="flare";
        public static string Mode(RaiderState state)=>state switch
            {RaiderState.Buried=>BuriedMode,RaiderState.Emerging=>EmergingMode,RaiderState.Super=>FlareMode,_=>null};
        private const int MaxAlert=RaiderBrain.MaxAlert;
        private const float Px=RaiderBrain.Px;

        public static RaiderIntent Tick(RaiderMind m,RaiderSettings s,ActorState actor,RoomLayout room,RaiderSenses senses,bool wallAhead,System.Random random)
        {
            var hull=Anatomy.Movement(actor);
            bool stay=ActorTerrainMotion.Supported(room,hull,actor.Position) && actor.vy<=0;
            var intent=new RaiderIntent();
            bool Chance(double p)=>random.NextDouble()<p;
            int Range(int n)=>(int)Math.Floor(random.NextDouble()*n);
            if(m.jumpDelay>0) m.jumpDelay--;

            // Buried: only its timer and senses; digging out, it stands.
            if(m.state==RaiderState.Buried)
            {
                if(m.timer>0) m.timer--;
                else m.timer=30;
                if(senses.sees || senses.known) {m.state=RaiderState.Emerging;m.timer=24;}
                return intent;
            }
            if(m.state==RaiderState.Emerging)
            {
                if(m.timer>0) m.timer--;
                else {m.state=RaiderState.Pursue;m.alert=MaxAlert+10;m.timer=30;if(senses.known) m.point=senses.knownPoint;}
                return intent;
            }

            if(m.timer>0) m.timer--;
            else if(m.state==RaiderState.Prepare) {intent.flare=true;m.state=RaiderState.Super;m.timer=s.superFrames;}
            else if(m.state==RaiderState.Super) {m.state=RaiderState.Pursue;m.timer=Range(50)+50;}
            else
            {
                if(m.alert==0) {m.state=(RaiderState)Range(2);m.anger=0;m.facing=m.wantedFacing=m.direction;}
                if(m.alert>0) m.state=RaiderState.Search;
                if(m.alert>=MaxAlert) m.state=RaiderState.Pursue;
                m.timer=m.state<=RaiderState.Stroll?Range(50)+40:Range(100)+100;
            }

            // findCel every 10th frame (not while flaring): a seen target, a heard point, else a fading search.
            if(m.timer%10==1 && m.state!=RaiderState.Super)
            {
                bool heard=!senses.sees && senses.known && (!m.hasLastKnown || (senses.knownPoint-m.lastKnown).sqrMagnitude>.01f);
                if(senses.sees) {m.point=senses.target;m.alert=MaxAlert+10;}
                else if(heard) {m.point=senses.knownPoint;m.alert=MaxAlert-1;}
                else
                {
                    m.point+=new Vector2(((float)random.NextDouble()*80-40)*Px,0);
                    if(m.alert>0) m.alert--;
                }
                m.hasLastKnown=senses.known;if(senses.known) m.lastKnown=senses.knownPoint;
            }
            float dx=m.point.x-actor.x;
            if(stay)
            {
                float below=actor.y+hull.height-m.point.y;
                m.vertical=below>40*Px?1:below<-40*Px?-1:0;
            }
            if(m.alert==0) m.point=new Vector2(actor.x+hull.width*m.facing*2,actor.y+hull.height);
            intent.run=m.state is RaiderState.Search or RaiderState.Pursue;
            intent.slowRun=m.state==RaiderState.Search;

            // The super power: tZlo frames of pursuit (or a seen target) make it prepare.
            if(m.state==RaiderState.Pursue && m.anger<s.angerFrames+10) m.anger++;
            if(s.zombieSuper==5 && m.anger>s.angerFrames && (senses.sees || m.state==RaiderState.Pursue) && m.state is RaiderState.Search or RaiderState.Pursue)
            {m.anger=0;m.state=RaiderState.Prepare;m.timer=s.prepareFrames;}

            float jump=0;
            switch(m.state)
            {
                case RaiderState.Prepare:
                    if(dx>40*Px) m.direction=m.wantedFacing=1;
                    if(dx<-40*Px) m.direction=m.wantedFacing=-1;
                    break;
                case RaiderState.Stroll:
                {
                    intent.move=m.direction;
                    int turn=0;
                    if(stay && RaiderBrain.Edge(room,hull,actor,m.direction,.25f))
                    {
                        if(Chance(.1) && RaiderBrain.Ground(room,actor,m.direction*80*Px)) jump=.5f;
                        else turn=-m.direction;
                    }
                    if(stay && wallAhead) turn=-m.direction;
                    if(stay && turn!=0)
                    {
                        if(Chance(.1)) m.state=RaiderState.Idle;
                        m.direction=m.wantedFacing=turn;
                    }
                    break;
                }
                case RaiderState.Search:
                case RaiderState.Pursue:
                {
                    if(m.vertical<0 && m.jumpDelay<=0 && m.timer%2==1 && RaiderBrain.CanJump(room,actor,m.direction)) jump=1;
                    if(m.timer%15==1 && Chance(.8))
                    {
                        if(dx>100*Px) m.direction=m.wantedFacing=1;
                        if(dx<-100*Px) m.direction=m.wantedFacing=-1;
                    }
                    intent.dropThrough=actor.y+hull.height-m.point.y>80*Px;
                    intent.move=m.direction;
                    if(stay && RaiderBrain.Edge(room,hull,actor,m.direction,.5f) && m.vertical<=0 && Chance(.5)) jump=.5f;
                    if(wallAhead)
                    {
                        m.wallTurn--;
                        if(Chance(.03)) m.wallTurn-=10;
                        else if(Chance(.5) && RaiderBrain.CanJump(room,actor,m.direction)) jump=1;
                        else m.wallTurn-=10;
                        if(m.wallTurn<0 && stay) {m.direction=m.wantedFacing=-m.direction;m.wallTurn=Range(20)+5;}
                    }
                    break;
                }
            }
            // jump(): only from support; aiJump 30–80 frames before the next.
            if(s.jumpSpeed<=0) jump=0;
            if(jump>0 && stay) {intent.jump=jump;m.jumpDelay=30+Range(50);}
            m.facing=m.wantedFacing;

            // attKorp at a seen target in reach; a touch (or one time in five) calms the anger.
            if(senses.sees)
            {
                var d=senses.target-actor.Position;
                if(Mathf.Abs(d.x)<s.attackReach && Mathf.Abs(d.y)<80*Px)
                {
                    intent.bodyHit=1;
                    if(m.anger>80 && Chance(.2)) m.anger-=25;
                }
            }
            return intent;
        }
    }
}
