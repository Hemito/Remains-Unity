using System;
using UnityEngine;

namespace Remains.Domain
{
    // The legs and hands of a raider-minded body, UnitRaider.control's movement and attack() parts: facing after a delay,
    // strolling along a floor, running toward the attended point with jumps, drops and ladders, the jump itself, and the
    // attack chances of each attack way. Shared by RaiderBrain (the original's machine: the robobrain), ZombieBrain's
    // probes and PonyBrain (sapient ponies) — one implementation of the walk for every ground walker on this mind.
    public static class RaiderLegs
    {
        internal const float Px=.02f;
        private static readonly MovementHull Probe=new() {width=.02f,height=.02f};

        public static bool Standing(RoomLayout room,ActorState actor)=>ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position) && actor.vy<=0;

        // storona follows tstor after t_turn; the ladder and jump delays count down.
        public static void Prelude(RaiderMind m,bool stay)
        {
            if(m.facing!=m.wantedFacing && m.turnDelay<=0) {m.facing=m.wantedFacing;m.turnDelay=stay?5:30;}
            if(m.turnDelay>0) m.turnDelay--;
            if(m.ladderDelay>0) m.ladderDelay--;
            if(m.jumpDelay>0) m.jumpDelay--;
        }

        // celDY from the head while standing (y up here): −1 the point is above, 1 below.
        public static void Locate(RaiderMind m,ActorState actor,MovementHull hull,bool stay)
        {
            if(!stay) return;
            float below=actor.y+hull.height-m.point.y;
            m.vertical=below>40*Px?1:below<-40*Px?-1:0;
        }

        // aiState 1: walk on, at an edge sometimes jump over a gap, else turn; turn at a wall. True when it turned.
        public static bool Stroll(RaiderMind m,ActorState actor,RoomLayout room,MovementHull hull,bool stay,bool wallAhead,System.Random random,
            ref RaiderIntent intent,ref float jump)
        {
            intent.move=m.direction;
            int turn=0;
            if(stay && Edge(room,hull,actor,m.direction,.25f))
            {
                if(random.NextDouble()<.1 && Ground(room,actor,m.direction*80*Px)) jump=.5f;
                else turn=-m.direction;
            }
            if(stay && wallAhead) turn=-m.direction;
            if(!stay || turn==0) return false;
            m.direction=m.wantedFacing=turn;
            return true;
        }

        // aiState 2 and 3: toward the point — a jump up when it is above, a turn toward it every 15th frame, ladders,
        // a hop over an edge, and at a wall a jump or, after a while, a turn. `cadence` counts frames (aiTCh).
        public static void Toward(RaiderMind m,RaiderSettings s,ActorState actor,RoomLayout room,MovementHull hull,bool stay,bool climbing,bool wallAhead,
            int cadence,System.Random random,ref RaiderIntent intent,ref float jump)
        {
            bool Chance(double p)=>random.NextDouble()<p;
            int Range(int n)=>(int)Math.Floor(random.NextDouble()*n);
            float dx=m.point.x-actor.x;
            if(m.vertical<0 && m.jumpDelay<=0 && cadence%2==1 && CanJump(room,actor,m.facing)) jump=1;
            if(cadence%15==1 && Chance(.5))
            {
                if(dx>100*Px) m.direction=m.wantedFacing=1;
                if(dx<-100*Px) m.direction=m.wantedFacing=-1;
            }
            // Ladders (mostLaz): catch one toward the point, climb while it is that way, then let go.
            if(s.climbsLadders)
            {
                if(!climbing && m.vertical!=0 && m.ladderDelay<=0 && m.ladderTime<-30) {intent.climb=-m.vertical;m.ladderDelay=30;}
                if(climbing)
                {
                    if(m.ladderTime<0) m.ladderTime=0;
                    m.ladderTime++;
                    if(m.point.y>actor.y && m.ladderDirection<=0) {m.ladderDirection=-1;intent.climb=1;}
                    else if(m.vertical==1 && m.ladderDirection>=0) {m.ladderDirection=1;intent.climb=-1;}
                    else {jump=m.ladderTime>20 && Chance(.7)?.8f:.01f;}
                }
                else {if(m.ladderTime>0) m.ladderTime=0;m.ladderTime--;m.ladderDirection=0;}
            }
            if(!climbing) intent.move=m.direction;
            if(stay && Chance(.5) && m.vertical<=0 && Edge(room,hull,actor,m.direction,.5f)) jump=.5f;
            if(wallAhead && !climbing)
            {
                if(dx*m.direction<0) {m.direction=m.wantedFacing=-m.direction;m.wallTurn=Range(20)+5;}
                else
                {
                    m.wallTurn--;
                    if(Chance(.1) || actor.y+hull.height-m.point.y>100*Px || !CanJump(room,actor,m.facing)) m.wallTurn-=10;
                    else jump=1;
                    if(m.wallTurn<0 && stay) {m.direction=m.wantedFacing=-m.direction;m.wallTurn=Range(20)+5;}
                }
            }
            if(jump>0) m.wantedFacing=m.direction;
        }

        // jump(): only from support or a ladder; from support it pushes on along the facing. The source slows a jump to
        // the left instead (dx *= 0.8 for aiNapr −1) — an asymmetry taken as a defect, not ported. A unit without a jump
        // (jumpdy 0) never leaves the ground.
        public static void Jump(RaiderMind m,RaiderSettings s,bool stay,bool climbing,float jump,System.Random random,ref RaiderIntent intent)
        {
            if(s.jumpSpeed<=0 || jump<=0) return;
            m.jumpDelay=(int)Math.Floor(random.NextDouble()*50)+30;
            if(stay || climbing) {intent.jump=jump;if(stay) intent.jumpPush=m.facing*s.acceleration*2;}
        }

        // attack() at a seen target (celUnit), by attack way. The touch lands only in reach (attKorp: the bodies overlap),
        // its factor 1 when the raider moves faster than 8 px per frame.
        public static void Attack(RaiderMind m,RaiderSettings s,RaiderAttacker attacker,ActorState actor,bool climbing,System.Random random,ref RaiderIntent intent)
        {
            bool Chance(double p)=>random.NextDouble()<p;
            var d=m.point-actor.Position;float full=Mathf.Abs(actor.vx)>8*Px*30?1:.5f;
            bool close=d.sqrMagnitude<100*Px*100*Px;
            switch(attacker)
            {
                case RaiderAttacker.Teeth: intent.bodyHit=full;break;
                case RaiderAttacker.Levitated:
                    intent.swing=Mathf.Abs(d.x)<120*Px && Mathf.Abs(d.y)<s.swingHeight && Chance(.3);
                    if(Chance(.1)) intent.bodyHit=.5f;
                    break;
                case RaiderAttacker.Ranged when !climbing:
                    if(s.burstFrames==0) intent.shoot=Chance(.1);
                    else
                    {
                        if(m.burst<=0) m.burst=Mathf.RoundToInt(((float)random.NextDouble()*.4f+.8f)*s.burstFrames);
                        intent.shoot=m.burst>s.burstFrames*.25f;
                        m.burst--;
                    }
                    if(close && s.Touches && Chance(.1)) intent.bodyHit=.5f;
                    break;
                case RaiderAttacker.Thrower:
                    if(close && Chance(.1)) intent.bodyHit=full;
                    break;
            }
        }

        // shX: the share of the body beyond the edge on that side is above `share`.
        public static bool Edge(RoomLayout room,MovementHull hull,ActorState actor,int side,float share)=>
            !ActorTerrainMotion.Supported(room,Probe,new Vector2(actor.x+side*hull.width*(.5f-share),actor.y));
        // getAbsTile(X + d, Y + 10).phis: ground just below the feet at that distance.
        public static bool Ground(RoomLayout room,ActorState actor,float offset)=>
            !ActorTerrainMotion.IsClear(room,Probe,new Vector2(actor.x+offset,actor.y-10*Px));
        // checkJump: free cells 85 and 125 px above the feet, here and one cell ahead.
        public static bool CanJump(RoomLayout room,ActorState actor,int facing)
        {
            foreach(float x in new[]{actor.x,actor.x+40*Px*facing})
            foreach(float up in new[]{85*Px,125*Px})
                if(!ActorTerrainMotion.IsClear(room,Probe,new Vector2(x,actor.y+up))) return false;
            return true;
        }
    }
}
