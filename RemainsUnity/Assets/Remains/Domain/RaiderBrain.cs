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
        [Min(0),InspectorName("Досягаемость взмаха по высоте, м (rapid × 8 px)")] public float swingHeight;
        [InspectorName("Оружие в зубах (comb krep)")] public bool weaponInTeeth=true;
        [InspectorName("Прогуливается (нет un stay)")] public bool stroll=true;
        [InspectorName("Лазает по лестницам")] public bool climbsLadders=true;
        [Min(0),InspectorName("Тупость: задержка реакции, кадров")] public int slowness=40;
        // UnitAIRobot (robobrain): the robots' variant of the same state machine — it notices in 20–50 frames, holds
        // (state 4, 10–20 frames) within aiDist once alert reaches maxSpok + 2, runs only while searching, never steps back
        // or touches (no attKorp), and its stroll does not stop at turns.
        [InspectorName("ИИ робота (UnitAIRobot)")] public bool robot;
        // UnitZombie (ZombieBrain): its own control over the same mind — runs at its move run (×0.6 while searching),
        // touches a seen target within its reach, may sleep buried, and a glowing one (super 5) flares up when angry.
        [InspectorName("ИИ зомби (UnitZombie)")] public bool zombie;
        [Min(0),InspectorName("Зомби: бег, м/с (move run)")] public float zombieRun;
        [Min(0),InspectorName("Зомби: досягаемость атаки по горизонтали, м (optDistAtt)")] public float attackReach=4;
        // UnitZombie.superSilaTip: 0 none or not carried over yet, 5 the glowing ghoul's flare (tZlo, tPrepSuper, tSuper).
        [Min(0),InspectorName("Зомби: суперсила (ss; 5 — вспышка свечения)")] public int zombieSuper;
        [Min(0),InspectorName("Зомби: злость до суперсилы, кадров (tZlo)")] public int angerFrames=120;
        [Min(0),InspectorName("Зомби: подготовка суперсилы, кадров (tPrepSuper)")] public int prepareFrames=30;
        [Min(0),InspectorName("Зомби: длительность суперсилы, кадров (tSuper)")] public int superFrames=75;
        // Unit.attKorp → udarUnit: every raider's touch, with its comb damage (+ half of a melee weapon's) and damage type.
        // Not used by a robot (Unity keeps a serialized class field non-null, so the flag, not null, says so).
        [InspectorName("Удар корпусом (attKorp; у робота не используется)")] public NaturalAttackDefinition bodyHit;
        // A sapient raider's character for PonyBrain (not used by the robobrain or a zombie).
        [InspectorName("Характер разумного пони (PonyBrain)")] public PonyProfile pony=new();
        public bool Touches=>!robot;
        public float RunSpeed=>zombie?zombieRun:walkSpeed*2;
        public void Validate(string type)
        {
            // A jump of 0 never jumps (robobrain: move jump 0).
            if(!Enum.IsDefined(typeof(RaiderAttacker),attacker) || !float.IsFinite(walkSpeed) || walkSpeed<=0 || !float.IsFinite(jumpSpeed) || jumpSpeed<0 ||
                !float.IsFinite(acceleration) || acceleration<=0 || !float.IsFinite(holdDistance) || holdDistance<0 || burstFrames<0 || slowness<0 ||
                !float.IsFinite(swingHeight) || swingHeight<0 || attacker==RaiderAttacker.Levitated && swingHeight<=0)
                throw new InvalidOperationException($"Некорректное поведение рейдера {type}: шаг {walkSpeed}, прыжок {jumpSpeed}, ускорение {acceleration}.");
            if(robot && attacker!=RaiderAttacker.Ranged) throw new InvalidOperationException($"Робот {type}: только стрелок (UnitAIRobot.attack).");
            if(zombie && (robot || attacker!=RaiderAttacker.Teeth || !float.IsFinite(zombieRun) || zombieRun<walkSpeed || !float.IsFinite(attackReach) || attackReach<=0 ||
                zombieSuper is not (0 or 5) || zombieSuper==5 && (angerFrames<=0 || prepareFrames<=0 || superFrames<=0)))
                throw new InvalidOperationException($"Зомби {type}: удар корпусом, бег не медленнее шага, досягаемость и суперсила 0 или 5 с её временем.");
            if(robot) return;
            if(bodyHit==null) throw new InvalidOperationException($"У рейдера {type} нет удара корпусом (attKorp).");
            bodyHit.Validate(type);
            if(zombie) return;
            if(pony==null) throw new InvalidOperationException($"У рейдера {type} нет характера разумного пони.");
            pony.Validate(type);
        }
    }

    // 0–5 are UnitRaider's aiState; a zombie's own states (UnitZombie 5, 6, 4, 7) follow after them.
    public enum RaiderState { Idle=0, Stroll=1, Search=2, Pursue=3, Hold=4, Notice=5, Buried=6, Emerging=7, Prepare=8, Super=9 }

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
        public int anger;              // aiZlo: a zombie's frames of pursuit towards its super power
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
        public bool charge;            // runs at 1.5 of its run (a melee raider attacking)
        public bool shoot;             // fires its firearm
        public bool swing;             // swings its levitated melee weapon (WClub)
        public float bodyHit;          // attKorp: 0 none, else its damage factor (1 or 0.5)
        public bool slowRun;           // a zombie searching runs at 0.6 of its run
        public bool flare;             // a glowing zombie's flare begins (superSila 5)
        public bool revive;            // a sapient pony stands over its downed ally reviving it (PonyBrain)
        public bool heal;              // a sapient pony uses a medkit on itself (PonyBrain)
        public bool grenade;public float grenadeError;   // a sapient pony throws a grenade with this error, degrees (GrenadeAim)
    }

    // UnitRaider.control / jump / checkJump / attack (1.04 dlc-ui), one call per original frame (30 per second). Kept close
    // to the source on purpose — a temporary AI to be rewritten (user decision 2026-10-28). The port's own movement
    // controller moves the body; perception is the port's. `attacker` is the attack way of this frame: a shooter whose
    // firearm is spent fights as a melee raider (the port's rule, user 2026-10-29; the source does it for a thrower).
    // Not ported yet: grenade and telekinesis fear (state 6), the dash (7/8), the thrown attack, swimming, flying
    // raiders, replicas, waking others (budilo), shots at a lost target, the aim error (mazil), stun (shok).
    // RaiderSettings.robot runs UnitAIRobot.control instead where the two differ (robobrain; its teleport isPort and
    // waking robot cells are not ported).
    public static class RaiderBrain
    {
        public const float FrameSeconds=1f/30;
        internal const int MaxAlert=30;                       // Unit.maxSpok
        internal const float Px=RaiderLegs.Px;

        public static RaiderIntent Tick(RaiderMind m,RaiderSettings s,ActorState actor,RoomLayout room,RaiderSenses senses,bool wallAhead,System.Random random,
            RaiderAttacker? attackerNow=null)
        {
            var attacker=attackerNow??s.attacker;
            var hull=Anatomy.Movement(actor);
            bool stay=ActorTerrainMotion.Supported(room,hull,actor.Position) && actor.vy<=0,climbing=actor.climbing;
            var intent=new RaiderIntent();
            bool Chance(double p)=>random.NextDouble()<p;
            int Range(int n)=>(int)Math.Floor(random.NextDouble()*n);

            RaiderLegs.Prelude(m,stay);

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
                    if(attacker==RaiderAttacker.Ranged && m.alert>=MaxAlert+(s.robot?2:8) && d.sqrMagnitude<s.holdDistance*s.holdDistance)
                        m.state=Chance(.3)?RaiderState.Pursue:RaiderState.Hold;
                    else m.state=RaiderState.Pursue;
                }
                m.timer=m.state<=RaiderState.Stroll?Range(50)+40:
                    (s.robot || attacker==RaiderAttacker.Levitated) && m.state==RaiderState.Hold?Range(10)+10:Range(100)+100;
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
                        m.timer=s.robot?Range(30)+20:Range(20)+(attacker>=RaiderAttacker.Ranged?s.slowness:s.slowness/4);
                    }
                    else m.alert=MaxAlert+10;
                }
                else if(heard) {m.point=senses.knownPoint;if(m.alert<MaxAlert-1) m.alert=MaxAlert-1;}
                else if(m.follow>0) {m.follow--;if(senses.known) m.point=senses.knownPoint;}
                else
                {
                    if(m.alert%5==1) m.point+=new Vector2(((float)random.NextDouble()*80-40)*Px,((float)random.NextDouble()*80-40)*Px);
                    if(m.alert>0) m.alert--;
                    if(!s.robot && !m.aware && m.alert<15 && m.alert>0) m.alert=0;
                }
                m.hasLastKnown=senses.known;if(senses.known) m.lastKnown=senses.knownPoint;
            }

            // Where the point is (celDX; celDY from the head while standing, y up here).
            float dx=m.point.x-actor.x;
            RaiderLegs.Locate(m,actor,hull,stay);
            intent.dropThrough=m.alert>0 && actor.y+hull.height-m.point.y>80*Px;
            if(!climbing) m.wantedFacing=m.direction;
            if(m.alert==0) m.point=new Vector2(actor.x+hull.width*m.facing*2,actor.y+hull.height);

            // A levitated melee raider stops to swing 50–100 px from a seen target (states 3 ↔ 4).
            if(attacker==RaiderAttacker.Levitated)
            {
                float near=Mathf.Abs(m.point.x-actor.x);bool inReach=senses.sees && Mathf.Abs(m.point.y-actor.y)<80*Px && near<100*Px && near>50*Px;
                if(m.state==RaiderState.Pursue && inReach) m.state=RaiderState.Hold;
                else if(m.state==RaiderState.Hold && !inReach) m.state=RaiderState.Pursue;
            }

            // durak: a raider always runs when alarmed; a robot runs only while searching (maxSpeed = runSpeed in state 2).
            bool run=m.state==RaiderState.Search || m.state==RaiderState.Pursue && !s.robot;
            intent.charge=attacker==RaiderAttacker.Teeth && m.state==RaiderState.Pursue;
            if(m.state==RaiderState.Pursue && attacker==RaiderAttacker.Ranged && s.weaponInTeeth)
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
                    if(RaiderLegs.Stroll(m,actor,room,hull,stay,wallAhead,random,ref intent,ref jump) && !s.robot && Chance(.1)) m.state=RaiderState.Idle;
                    break;
                case RaiderState.Search:
                case RaiderState.Pursue:
                    RaiderLegs.Toward(m,s,actor,room,hull,stay,climbing,wallAhead,m.timer,random,ref intent,ref jump);
                    break;
                case RaiderState.Hold:
                    m.direction=m.wantedFacing=m.point.x>actor.x?1:-1;
                    if(attacker==RaiderAttacker.Ranged && !s.robot && (m.point-actor.Position).sqrMagnitude<200*Px*200*Px)
                    {
                        if(Chance(.6)) {intent.pushX=s.RunSpeed*(dx>0?-1:1)*2;jump=.4f;}
                        m.state=RaiderState.Pursue;
                    }
                    break;
            }
            RaiderLegs.Jump(m,s,stay,climbing,jump,random,ref intent);
            // attack() while attacking (aiAttack, states 3 and 4) a seen target.
            if((m.state==RaiderState.Pursue || m.state==RaiderState.Hold) && senses.sees) RaiderLegs.Attack(m,s,attacker,actor,climbing,random,ref intent);
            return intent;
        }
    }
}
