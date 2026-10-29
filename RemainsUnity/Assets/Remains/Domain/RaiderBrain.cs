using System;
using UnityEngine;

namespace Remains.Domain
{
    // How a raider attacks (UnitRaider attackerType): 0 a melee weapon in its teeth or none, 1 a levitated melee weapon,
    // 2 a firearm, 3 thrown weapons.
    public enum RaiderAttacker { [InspectorName("Ближний бой (в зубах)")] Teeth=0, [InspectorName("Ближний бой (телекинез)")] Levitated=1,
        [InspectorName("Стрелок")] Ranged=2, [InspectorName("Метатель")] Thrower=3 }

    // A raider's behaviour data (AllData unit raider + raiderN, UnitRaider fields) in metres and seconds: px × 0.02,
    // px per frame × 0.6.
    [Serializable] public sealed class RaiderSettings
    {
        [InspectorName("Способ атаки")] public RaiderAttacker attacker=RaiderAttacker.Ranged;
        [Min(0),InspectorName("Шаг, м/с (move speed)")] public float walkSpeed;
        [Min(0),InspectorName("Прыжок, м/с (move jump)")] public float jumpSpeed;
        [Min(0),InspectorName("Ускорение, м/с за кадр (move accel)")] public float acceleration;
        [Min(0),InspectorName("Дальность стрельбы с места, м (un dist)")] public float holdDistance=20;
        [Min(0),InspectorName("Очередь, кадров (un och)")] public int burstFrames;
        [InspectorName("Оружие в зубах (comb krep)")] public bool weaponInTeeth=true;
        [InspectorName("Прогуливается (нет un stay)")] public bool stroll=true;
        [InspectorName("Лазает по лестницам")] public bool climbsLadders=true;
        [Min(0),InspectorName("Тупость: задержка реакции, кадров")] public int slowness=40;
        public float RunSpeed=>walkSpeed*2;
        public void Validate(string type)
        {
            if(!Enum.IsDefined(typeof(RaiderAttacker),attacker) || !float.IsFinite(walkSpeed) || walkSpeed<=0 || !float.IsFinite(jumpSpeed) || jumpSpeed<=0 ||
                !float.IsFinite(acceleration) || acceleration<=0 || !float.IsFinite(holdDistance) || holdDistance<0 || burstFrames<0 || slowness<0)
                throw new InvalidOperationException($"Некорректное поведение рейдера {type}: шаг {walkSpeed}, прыжок {jumpSpeed}, ускорение {acceleration}.");
        }
    }

    public enum RaiderState { Idle=0, Stroll=1, Search=2, Pursue=3, Hold=4, Notice=5 }

    // The owner's working memory of one raider's decisions (UnitRaider's AI fields). It lives with the owner and is not
    // saved or replicated: a new owner starts it from the shared perception (NpcPerceptionMemory, target).
    public sealed class RaiderMind
    {
        public RaiderState state;
        public int timer;              // aiTCh, frames
        public int alert;              // aiSpok
        public int direction=1;        // aiNapr
        public int wantedFacing=1;     // tstor
        public int facing=1;           // storona, turned after turnDelay
        public int turnDelay;          // t_turn
        public int vertical;           // aiVNapr: −1 the point is above, 1 below
        public int jumpDelay,ladderDelay,ladderTime,ladderDirection,wallTurn,burst,follow;
        public bool aware;             // aiVKurse: has seen a player
        public Vector2 point;          // celX/celY: the point the raider attends to (its centre for a unit)
        public Vector2 lastKnown;
        public bool hasLastKnown;
    }

    // What the raider senses this frame: the port's perception stands for Unit.findCel.
    public readonly struct RaiderSenses
    {
        public readonly bool sees;public readonly Vector2 target;public readonly bool known;public readonly Vector2 knownPoint;
        public RaiderSenses(bool sees,Vector2 target,bool known,Vector2 knownPoint)
        {this.sees=sees;this.target=target;this.known=known;this.knownPoint=knownPoint;}
    }

    // The decision of one frame for the movement controller and the attack.
    public struct RaiderIntent
    {
        public int move;               // −1, 0, 1
        public bool run;
        public float jump;             // 0 none, else the share of the full jump (UnitRaider.jump param)
        public float pushX;            // a set horizontal speed (the step back of state 4), 0 none
        public float jumpPush;         // added to the horizontal speed with the jump
        public int climb;              // ladder: 1 up, −1 down
        public bool dropThrough;       // throu: through a one-way platform
        public bool shoot;
    }

    // UnitRaider.control / jump / checkJump / attack (1.04 dlc-ui), one call per original frame (30 per second), for the
    // ranged raider (attackerType 2) first. Kept close to the source on purpose — a temporary AI to be rewritten
    // (user decision 2026-10-28). The port's own movement controller moves the body; perception is the port's.
    // Not ported yet: grenade and telekinesis fear (state 6), the dash (7/8), the melee and thrown attacks, swimming,
    // flying raiders, replicas, waking others (budilo), shots at a lost target, the aim error (mazil).
    public static class RaiderBrain
    {
        public const float FrameSeconds=1f/30;
        private const int MaxAlert=30;                       // Unit.maxSpok
        private const float Px=.02f;
        private static readonly MovementHull Probe=new() {width=.02f,height=.02f};

        public static RaiderIntent Tick(RaiderMind m,RaiderSettings s,ActorState actor,RoomLayout room,RaiderSenses senses,bool wallAhead,System.Random random)
        {
            var hull=Anatomy.Movement(actor);
            bool stay=ActorTerrainMotion.Supported(room,hull,actor.Position) && actor.vy<=0,climbing=actor.climbing;
            var intent=new RaiderIntent();
            bool Chance(double p)=>random.NextDouble()<p;
            int Range(int n)=>(int)Math.Floor(random.NextDouble()*n);

            // Facing follows the wanted side after a delay (storona / tstor / t_turn).
            if(m.facing!=m.wantedFacing && m.turnDelay<=0) {m.facing=m.wantedFacing;m.turnDelay=stay?5:30;}
            if(m.turnDelay>0) m.turnDelay--;
            if(m.ladderDelay>0) m.ladderDelay--;
            if(m.jumpDelay>0) m.jumpDelay--;

            // The state when its timer runs out.
            if(m.timer>0) m.timer--;
            else if(m.state==RaiderState.Notice)
            {
                if(senses.sees) {m.alert=MaxAlert+10;m.state=RaiderState.Pursue;}
                else m.state=RaiderState.Stroll;
            }
            else
            {
                if(m.alert==0) m.state=s.stroll?(RaiderState)Range(2):RaiderState.Idle;
                if(m.alert>0) m.state=RaiderState.Search;
                if(m.alert>=MaxAlert)
                {
                    var d=m.point-actor.Position;
                    if(s.attacker==RaiderAttacker.Ranged && m.alert>=MaxAlert+8 && d.sqrMagnitude<s.holdDistance*s.holdDistance)
                        m.state=Chance(.3)?RaiderState.Pursue:RaiderState.Hold;
                    else m.state=RaiderState.Pursue;
                }
                m.timer=m.state<=RaiderState.Stroll?Range(50)+40:
                    s.attacker==RaiderAttacker.Levitated && m.state==RaiderState.Hold?Range(10)+10:Range(100)+100;
            }

            // findCel every 10th frame: a seen target, a heard point, the last seen one, or a fading search.
            if(m.timer%10==1)
            {
                bool heard=!senses.sees && senses.known && (!m.hasLastKnown || (senses.knownPoint-m.lastKnown).sqrMagnitude>.01f);
                if(senses.sees)
                {
                    m.point=senses.target;m.follow=6;m.aware=true;
                    if(m.state<=RaiderState.Stroll)
                    {
                        m.state=RaiderState.Notice;
                        m.timer=Range(20)+(s.attacker>=RaiderAttacker.Ranged?s.slowness:s.slowness/4);
                    }
                    else m.alert=MaxAlert+10;
                }
                else if(heard) {m.point=senses.knownPoint;if(m.alert<MaxAlert-1) m.alert=MaxAlert-1;}
                else if(m.follow>0) {m.follow--;if(senses.known) m.point=senses.knownPoint;}
                else
                {
                    if(m.alert%5==1) m.point+=new Vector2(((float)random.NextDouble()*80-40)*Px,((float)random.NextDouble()*80-40)*Px);
                    if(m.alert>0) m.alert--;
                    if(!m.aware && m.alert<15 && m.alert>0) m.alert=0;
                }
                m.hasLastKnown=senses.known;if(senses.known) m.lastKnown=senses.knownPoint;
            }

            // Where the point is (celDX; celDY from the head while standing, y up here).
            float dx=m.point.x-actor.x;
            if(stay)
            {
                float below=actor.y+hull.height-m.point.y;
                m.vertical=below>40*Px?1:below<-40*Px?-1:0;
            }
            intent.dropThrough=m.alert>0 && actor.y+hull.height-m.point.y>80*Px;
            if(!climbing) m.wantedFacing=m.direction;
            if(m.alert==0) m.point=new Vector2(actor.x+hull.width*m.facing*2,actor.y+hull.height);

            bool run=(m.state==RaiderState.Search || m.state==RaiderState.Pursue);   // durak: always runs when alarmed
            if(m.state==RaiderState.Pursue && s.attacker==RaiderAttacker.Ranged && s.weaponInTeeth)
            {
                m.wantedFacing=dx>0?1:-1;
                if(m.facing*m.direction<0) run=false;
            }
            intent.run=run;
            float jump=0;

            switch(m.state)
            {
                case RaiderState.Idle:
                case RaiderState.Notice:
                    break;   // stands (walk 0)
                case RaiderState.Stroll:
                {
                    intent.move=m.direction;
                    int turn=0;
                    if(stay && Edge(room,hull,actor,m.direction,.25f))
                    {
                        if(Chance(.1) && Ground(room,actor,m.direction*80*Px)) jump=.5f;
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
                    if(m.vertical<0 && m.jumpDelay<=0 && m.timer%2==1 && CanJump(room,actor,m.facing)) jump=1;
                    if(m.timer%15==1 && Chance(.5))
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
                    break;
                }
                case RaiderState.Hold:
                    m.direction=m.wantedFacing=m.point.x>actor.x?1:-1;
                    if(s.attacker==RaiderAttacker.Ranged && (m.point-actor.Position).sqrMagnitude<200*Px*200*Px)
                    {
                        if(Chance(.6)) {intent.pushX=s.RunSpeed*(dx>0?-1:1)*2;jump=.4f;}
                        m.state=RaiderState.Pursue;
                    }
                    break;
            }
            // jump(): only from support or a ladder; from support it pushes on along the facing. The source slows a
            // jump to the left instead (dx *= 0.8 for aiNapr −1) — an asymmetry taken as a defect, not ported.
            if(jump>0)
            {
                m.jumpDelay=Range(50)+30;
                if(stay || climbing) {intent.jump=jump;if(stay) intent.jumpPush=m.facing*s.acceleration*2;}
            }

            // attack(): a ranged raider shoots while attacking and not on a ladder.
            bool attacking=m.state==RaiderState.Pursue || m.state==RaiderState.Hold;
            if(attacking && s.attacker==RaiderAttacker.Ranged && !climbing && senses.sees)
            {
                if(s.burstFrames==0) intent.shoot=Chance(.1);
                else
                {
                    if(m.burst<=0) m.burst=Mathf.RoundToInt(((float)random.NextDouble()*.4f+.8f)*s.burstFrames);
                    intent.shoot=m.burst>s.burstFrames*.25f;
                    m.burst--;
                }
            }
            return intent;
        }

        // shX: the share of the body beyond the edge on that side is above `share`.
        private static bool Edge(RoomLayout room,MovementHull hull,ActorState actor,int side,float share)=>
            !ActorTerrainMotion.Supported(room,Probe,new Vector2(actor.x+side*hull.width*(.5f-share),actor.y));
        // getAbsTile(X + d, Y + 10).phis: ground just below the feet at that distance.
        private static bool Ground(RoomLayout room,ActorState actor,float offset)=>
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
