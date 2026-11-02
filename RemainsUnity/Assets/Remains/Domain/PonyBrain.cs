using System;
using UnityEngine;

namespace Remains.Domain
{
    // A sapient pony is a player or a raider of the raiders' mind — not the robobrain or a zombie on the same brain (user
    // decision 2026-10-31: players and sapient ponies share their actions and this brain; later settlers and zebras).
    public static class SapientPonies
    {
        // By behaviour: every creature type carries (default) raider settings, so their presence says nothing.
        public static bool Is(ActorState actor)
        {
            if(actor.player) return true;
            var type=Remains.Content.ActorTypes.Get(actor.actorType);
            return type.behavior==NpcCreatureBehaviours.Raider && type.raider is {robot:false,zombie:false};
        }
    }

    // How much a sapient pony wants each intent (PonyBrain): its character as data. The original's numbers — reaction
    // (slowness), attack chances, step back, shooting distance — stay in RaiderSettings.
    [Serializable] public sealed class PonyProfile
    {
        [Range(0,2),InspectorName("Стрельба")] public float shoot=1;
        [Range(0,2),InspectorName("Ближний бой")] public float melee=1;
        [Range(0,2),InspectorName("Осторожность (отступление)")] public float caution=1;
        [Range(0,2),InspectorName("Гранаты")] public float grenade=1;
        [Range(0,60),InspectorName("Разброс броска без прицеливания, °")] public float throwError=24;
        [Range(0,1),InspectorName("Отступает при здоровье ниже, доля")] public float retreatHealth=.25f;
        [Range(0,1),InspectorName("Лечится аптечкой при здоровье ниже, доля")] public float healHealth=.5f;
        [Range(0,.5f),InspectorName("Надбавка начатому намерению")] public float commitment=.1f;
        public void Validate(string type)
        {
            foreach(float value in new[]{shoot,melee,caution,grenade,throwError,retreatHealth,healHealth,commitment})
                if(!float.IsFinite(value) || value<0) throw new InvalidOperationException($"Некорректный профиль разумного пони {type}.");
            if(shoot>2 || melee>2 || caution>2 || grenade>2 || throwError>60 || retreatHealth>1 || healHealth>1 || commitment>.5f) throw new InvalidOperationException($"Профиль разумного пони {type} вне пределов.");
        }
    }

    public enum PonyIntentKind { Calm, Notice, Search, Approach, Shoot, Melee, Retreat, Revive, Heal, Grenade }

    // The nearest downed ally the owner found for this pony, and whether the pony has a medkit to revive it with.
    public readonly struct PonyAlly
    {
        public readonly bool known,medkit;public readonly Vector2 position;
        public PonyAlly(bool known,Vector2 position,bool medkit) {this.known=known;this.position=position;this.medkit=medkit;}
        public static readonly PonyAlly None=new(false,default,false);
    }

    // The owner's memory of one sapient pony's choices beside its legs' RaiderMind; not saved or replicated.
    public sealed class PonyMind
    {
        public PonyIntentKind intent;
        public int held;                // frames the intent has been held
        public int clock;               // frames: the decision cadence and the legs' aiTCh
        public int reaction;            // frames before a just-seen target is acted on (tupizna)
        public int calm;public bool idle;           // the stroll/idle alternation while calm
        public int advance;public bool advancing;   // a shooter's 30% advance / 70% hold, re-rolled (UnitRaider states 3/4)
        public int stepBack;            // frames before the next step back
        public int cornered;            // frames a retreat has been blocked
        public float maxHealth;
        public float danger;            // 0–1: hits and shots near it, fading (the owner reports them: PonyBrain.Threat)
        public int aim;                 // frames spent aiming the grenade in hand
        public int grenadeRest;         // frames before another throw is considered
        public readonly float[] scores=new float[Enum.GetValues(typeof(PonyIntentKind)).Length];
    }

    // The sapient ponies' brain, utility AI (user decision 2026-10-31): every DecisionFrames original frames each intent is
    // scored from the situation (sight, distance, attack way, health) times the profile, the best one wins, the current
    // one keeps a bonus and is held at least MinHoldFrames so that the pony does not flicker. Each intent drives the
    // shared legs (RaiderLegs) and the original's attack chances. A downed ally is revived and the pony's own wounds are
    // healed with a medkit, as players do (HealingRules). A grenade is aimed for as long as the pony feels safe — every
    // second halves its error, up to 6 s — and thrown at once when danger rises; a retreating pony throws without aiming
    // (user 2026-11-01). Not yet: picking up weapons, factions beyond the original's player-only targets, a debug overlay.
    public static class PonyBrain
    {
        public const int DecisionFrames=3,MinHoldFrames=15;
        private const int MaxAlert=RaiderBrain.MaxAlert;
        private const float Px=RaiderLegs.Px;
        // Aiming a grenade: each AimHalfFrames halve the error, at most MaxAimFrames; danger at Interrupt throws at once.
        public const int AimHalfFrames=30,MaxAimFrames=180,GrenadeRestFrames=240;
        public const float Interrupt=.5f,Hit=1,NearShot=.6f,MaxThrowDistance=12;
        private const float DangerFade=.25f/30;   // per frame: a hit is forgotten in 4 s
        // The owner reports what endangers the pony: a hit (Hit) or a shot or blast near it (NearShot).
        public static void Threat(PonyMind p,float amount) {if(amount>p.danger) p.danger=Mathf.Clamp01(amount);}
        // How endangered it feels: what it was told, or a seen enemy closing in — 1 inside a grenade's blast (6 m), nothing
        // beyond 10 m.
        public static float Danger(PonyMind p,bool sees,float distance)=>Mathf.Max(p.danger,sees?Mathf.Clamp01((10-distance)/4):0);
        // The throw's error after this much aiming (degrees; GrenadeAim adds as many percent of speed).
        public static float ThrowError(PonyProfile w,int aim)=>w.throwError*Mathf.Pow(.5f,aim/(float)AimHalfFrames);

        public static RaiderIntent Tick(PonyMind p,RaiderMind m,RaiderSettings s,ActorState actor,RoomLayout room,RaiderSenses senses,bool wallAhead,
            System.Random random,RaiderAttacker attacker,PonyAlly ally=default)
        {
            var hull=Anatomy.Movement(actor);
            bool stay=RaiderLegs.Standing(room,actor),climbing=actor.climbing;
            var intent=new RaiderIntent();
            bool Chance(double chance)=>random.NextDouble()<chance;
            int Range(int n)=>(int)Math.Floor(random.NextDouble()*n);
            p.clock++;
            p.danger=Mathf.Max(0,p.danger-DangerFade);
            if(p.grenadeRest>0) p.grenadeRest--;
            RaiderLegs.Prelude(m,stay);
            Perceive(p,m,s,senses,attacker,random);
            if(p.clock%DecisionFrames==0) Decide(p,m,s,actor,senses,attacker,ally);
            p.held++;
            if(p.stepBack>0) p.stepBack--;

            RaiderLegs.Locate(m,actor,hull,stay);
            intent.dropThrough=m.alert>0 && actor.y+hull.height-m.point.y>80*Px;
            if(!climbing) m.wantedFacing=m.direction;
            if(p.intent==PonyIntentKind.Revive && ally.known) m.point=ally.position;
            else if(m.alert==0 && p.intent!=PonyIntentKind.Notice) m.point=new Vector2(actor.x+hull.width*m.facing*2,actor.y+hull.height);
            float dx=m.point.x-actor.x,jump=0;
            bool sees=senses.sees && p.reaction<=0;
            void Face() {m.direction=m.wantedFacing=dx>0?1:-1;}
            void Toward()
            {
                intent.run=true;
                RaiderLegs.Toward(m,s,actor,room,hull,stay,climbing,wallAhead,p.clock,random,ref intent,ref jump);
                // A weapon in the teeth looks at the target: no running backward (UnitRaider state 3).
                if(attacker==RaiderAttacker.Ranged && s.weaponInTeeth && sees) {m.wantedFacing=dx>0?1:-1;if(m.facing*m.direction<0) intent.run=false;}
            }

            switch(p.intent)
            {
                case PonyIntentKind.Notice:
                    Face();break;   // stands while it takes in what it saw
                case PonyIntentKind.Calm:
                    if(--p.calm<=0) {p.idle=!s.stroll || Range(2)==0;p.calm=Range(50)+40;}
                    if(!p.idle && RaiderLegs.Stroll(m,actor,room,hull,stay,wallAhead,random,ref intent,ref jump) && Chance(.1)) p.idle=true;
                    break;
                case PonyIntentKind.Search:
                case PonyIntentKind.Approach:
                    Toward();
                    intent.charge=attacker==RaiderAttacker.Teeth && p.intent==PonyIntentKind.Approach;
                    break;
                case PonyIntentKind.Shoot:
                    if(--p.advance<=0) {p.advancing=Chance(.3);p.advance=Range(100)+100;}
                    if(p.advancing) Toward(); else Face();
                    // State 4's step back from a close target: pushed away with a small jump.
                    if(!s.robot && p.stepBack<=0 && Mathf.Abs(dx)<200*Px && Mathf.Abs(m.point.y-actor.y)<200*Px)
                    {
                        p.stepBack=Range(30)+30;
                        if(Chance(.6)) {intent.pushX=s.RunSpeed*(dx>0?-1:1)*2;jump=.4f;}
                    }
                    break;
                case PonyIntentKind.Melee:
                {
                    // A levitated weapon swings from 50–100 px; teeth and hooves close in at a charge.
                    float near=Mathf.Abs(dx);
                    if(attacker==RaiderAttacker.Levitated && near<100*Px && near>50*Px && Mathf.Abs(m.point.y-actor.y)<80*Px) Face();
                    else {Toward();intent.charge=attacker==RaiderAttacker.Teeth;}
                    break;
                }
                case PonyIntentKind.Revive:
                    // Within the healing distance it stands over the ally and works on it (the owner counts RevivalSeconds).
                    if(stay && Mathf.Abs(dx)<1.2f && Mathf.Abs(m.point.y-actor.y)<1) {Face();intent.revive=true;}
                    else Toward();
                    break;
                case PonyIntentKind.Grenade:
                {
                    // Stands and aims while it feels safe; the more danger, the sooner it lets go.
                    Face();
                    float danger=Danger(p,sees,senses.sees?(senses.target-actor.Position).magnitude:float.MaxValue);
                    int goal=Mathf.RoundToInt(MaxAimFrames*Mathf.Clamp01(1-danger/Interrupt));
                    if(++p.aim>=goal) Throw(p,s.pony,ref intent);
                    break;
                }
                case PonyIntentKind.Heal:
                    // Stands and uses a medkit on itself (the owner asks the host, as a player's use does).
                    if(m.alert>0) Face();
                    intent.heal=stay && !climbing;
                    break;
                case PonyIntentKind.Retreat:
                {
                    // Away from the threat at a run; blocked by a wall or an edge it is cornered and stops.
                    m.direction=m.wantedFacing=dx>0?-1:1;intent.run=true;
                    bool edge=stay && RaiderLegs.Edge(room,hull,actor,m.direction,.25f);
                    if(edge && RaiderLegs.Ground(room,actor,m.direction*80*Px) && m.jumpDelay<=0) {intent.move=m.direction;jump=.5f;}
                    else if(stay && (wallAhead || edge)) {intent.move=0;p.cornered++;}
                    else {intent.move=m.direction;if(p.cornered>0) p.cornered--;}
                    // On the run it throws without aiming now and then.
                    if(sees && p.grenadeRest<=0 && Chance(.05) && InThrowBand(actor,senses.target-actor.Position))
                    {p.aim=0;Throw(p,s.pony,ref intent);}
                    break;
                }
            }
            RaiderLegs.Jump(m,s,stay,climbing,jump,random,ref intent);
            if(sees && p.intent is PonyIntentKind.Approach or PonyIntentKind.Shoot or PonyIntentKind.Melee)
                RaiderLegs.Attack(m,s,attacker,actor,climbing,random,ref intent);
            return intent;
        }

        // findCel every 10th frame, as the original's: a seen target (first noticed after the reaction delay), a heard
        // point, the last seen one followed for a while, or a fading search.
        private static void Perceive(PonyMind p,RaiderMind m,RaiderSettings s,RaiderSenses senses,RaiderAttacker attacker,System.Random random)
        {
            if(p.reaction>0 && --p.reaction==0)
            {
                // The notice ends: an alarm if the target is still in sight, back to calm if not (state 5).
                if(senses.sees) {m.alert=MaxAlert+10;m.point=senses.target;} else m.alert=0;
            }
            if(p.clock%10!=1) return;
            bool heard=!senses.sees && senses.known && (!m.hasLastKnown || (senses.knownPoint-m.lastKnown).sqrMagnitude>.01f);
            if(senses.sees)
            {
                m.point=senses.target;m.follow=6;m.aware=true;
                if(m.alert==0 && p.reaction<=0)
                {
                    p.reaction=(int)Math.Floor(random.NextDouble()*20)+(attacker>=RaiderAttacker.Ranged?s.slowness:s.slowness/4);
                    if(p.reaction==0) m.alert=MaxAlert+10;
                }
                else if(p.reaction<=0) m.alert=MaxAlert+10;
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

        // The utility of every intent now; the best wins over the current one only past its bonus and its minimum hold.
        private static void Decide(PonyMind p,RaiderMind m,RaiderSettings s,ActorState actor,RaiderSenses senses,RaiderAttacker attacker,PonyAlly ally)
        {
            var w=s.pony;var score=p.scores;Array.Clear(score,0,score.Length);
            if(p.reaction>0) {Choose(p,PonyIntentKind.Notice);return;}
            bool sees=senses.sees,alerted=m.alert>0,ranged=attacker==RaiderAttacker.Ranged;
            // The threat's distance: the seen target itself (while reviving, the attended point is the ally).
            float distance=((sees?senses.target:m.point)-actor.Position).magnitude;
            float health=p.maxHealth>0?Mathf.Clamp01(actor.health/p.maxHealth):1;
            if(p.cornered>30) p.cornered=Math.Min(p.cornered+1,120);   // cornered: fights for a while before trying again
            if(p.cornered>=120) p.cornered=0;
            score[(int)PonyIntentKind.Calm]=alerted?0:.2f;
            score[(int)PonyIntentKind.Search]=!sees && alerted?.3f+.3f*Mathf.Min(1,m.alert/(float)MaxAlert):0;
            score[(int)PonyIntentKind.Approach]=sees?.45f:0;
            score[(int)PonyIntentKind.Shoot]=sees && ranged && distance<=s.holdDistance?(.6f+(distance>200*Px?.15f:0))*w.shoot:0;
            score[(int)PonyIntentKind.Melee]=sees?(ranged?(distance<100*Px?.55f:0):.7f)*w.melee:0;
            // A downed ally with a medkit at hand: before searching or strolling, and even under fire unless the enemy is close.
            score[(int)PonyIntentKind.Revive]=ally.known && ally.medkit?(!sees?.65f:distance>200*Px?.5f:0):0;
            // Its own wounds below the profile's share, a medkit and a known full health (HealingRules): out of sight first —
            // after a retreat — and before reviving an ally; in sight only with the enemy far.
            score[(int)PonyIntentKind.Heal]=health<w.healHealth && HealingRules.CanApply(actor,actor,true)?(!sees?.7f:distance>200*Px?.5f:0):0;
            // A grenade in hand at a seen target beyond its own blast: before shooting, once in GrenadeRestFrames; a pony hurt
            // enough to retreat does not stand to aim (it throws on the run).
            score[(int)PonyIntentKind.Grenade]=sees && p.grenadeRest<=0 && health>=w.retreatHealth &&
                InThrowBand(actor,senses.target-actor.Position)?.8f*w.grenade:0;
            score[(int)PonyIntentKind.Retreat]=sees && health<w.retreatHealth && p.cornered<=30?(.75f+.25f*(1-health/Mathf.Max(w.retreatHealth,.0001f)))*w.caution:0;

            int best=0;
            for(int i=1;i<score.Length;i++) if(score[i]>score[best]) best=i;
            float current=score[(int)p.intent];
            if(best!=(int)p.intent && (current<=0 || p.held>=MinHoldFrames && score[best]>current+w.commitment)) Choose(p,(PonyIntentKind)best);
        }
        private static void Choose(PonyMind p,PonyIntentKind next)
        {
            if(p.intent==next) return;
            p.intent=next;p.held=0;p.advance=0;p.aim=0;
        }
        private static void Throw(PonyMind p,PonyProfile w,ref RaiderIntent intent)
        {
            intent.grenade=true;intent.grenadeError=ThrowError(w,p.aim);
            p.aim=0;p.grenadeRest=GrenadeRestFrames;
        }
        // Beyond its own blast and within a throw (GrenadeAim decides the throw itself; this only says it is worth trying).
        private static bool InThrowBand(ActorState actor,Vector2 offset)
        {
            // A grenade in hand (GrenadeItems.Next decides which) and the target beyond its blast, within a throw.
            var grenade=GrenadeItems.Next(actor);if(grenade==null) return false;
            float distance=offset.magnitude;
            return distance>GrenadeAim.Reach(grenade.definition) && distance<=MaxThrowDistance;
        }
    }
}
