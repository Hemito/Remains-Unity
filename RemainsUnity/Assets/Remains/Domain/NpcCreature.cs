using System;
using System.Linq;
using UnityEngine;

namespace Remains.Domain
{
    // Behaviour families of original creatures (the class of the AllData obj): one implementation per family.
    public static class NpcCreatureBehaviours
    {
        public const string Crawler="crawler",Bat="bat",Bloat="bloat",Slime="slime";
        // UnitTurret: a fixed gun (no locomotion, no gravity) that turns its weapon to the target and fires at its rate.
        public const string Turret="turret";
        public static bool Fixed(string behavior)=>behavior==Turret;
        // Families of touch attackers (natural attack and own motion), as opposed to the turret's weapon.
        public static bool Touches(string behavior)=>behavior is Bat or Bloat or Slime;
        public static bool Flies(string behavior)=>behavior==Bat || behavior==Bloat;
        // Empty (Unity serializes a missing string as "") or crawler: the natural-attack crawler AI.
        public static bool IsFamily(string behavior)=>!string.IsNullOrEmpty(behavior) && behavior!=Crawler;
        public static bool Valid(string behavior)=>string.IsNullOrEmpty(behavior) || behavior is Crawler or Bat or Bloat or Slime or Turret;
    }

    // Original Unit move data in metres and seconds (speed/run px per frame × 30 / 50, accel px per frame² × 900 / 50).
    [Serializable] public sealed class CreatureMotionSettings
    {
        [Min(0),InspectorName("Шаг, м/с")] public float walkSpeed;
        [Min(0),InspectorName("Бег, м/с")] public float runSpeed;
        [Min(0),InspectorName("Ускорение, м/с²")] public float acceleration;
        // UnitBloat un@attr / un@attch: the dash at a target closer than this, with this chance per decision.
        [Min(0),InspectorName("Дальность броска, м")] public float dashRange;
        [Range(0,1),InspectorName("Шанс броска")] public float dashChance;
        public void Validate(string type)
        {
            if(!float.IsFinite(walkSpeed) || !float.IsFinite(runSpeed) || !float.IsFinite(acceleration) || walkSpeed<=0 || runSpeed<walkSpeed || acceleration<=0 ||
                !float.IsFinite(dashRange) || dashRange<0 || !float.IsFinite(dashChance) || dashChance<0 || dashChance>1)
                throw new InvalidOperationException($"Некорректное движение существа {type}: шаг {walkSpeed}, бег {runSpeed}, ускорение {acceleration}.");
        }
    }

    // UnitTurret's watch and turn settings (UnitTurret.putLoc), seconds and degrees per second, angles with y up for a
    // turret facing right: the watch angles it sweeps between (aRot), one per period; how fast the gun turns while
    // watching or searching (watchDrot) and while attacking (actDrot); how long it takes after noticing a target
    // (aiTCh 30–40 frames) and how long it searches (maxSpok × 10 frames); an armored turret keeps its side (noTurn).
    [Serializable] public sealed class TurretSettings
    {
        [InspectorName("Углы наблюдения, °")] public float[] patrolAngles=Array.Empty<float>();
        [Min(.01f),InspectorName("Смена угла наблюдения, с")] public float periodSeconds=4;
        [Min(1),InspectorName("Поворот при наблюдении, °/с")] public float watchTurnDegreesPerSecond;
        [Min(1),InspectorName("Поворот в атаке, °/с")] public float attackTurnDegreesPerSecond;
        [Min(0),InspectorName("Тревога до атаки: от, с")] public float alertMinSeconds=1;
        [Min(0),InspectorName("Тревога до атаки: до, с")] public float alertMaxSeconds=4f/3;
        [Min(0),InspectorName("Поиск потерянной цели, с")] public float searchSeconds=10;
        [InspectorName("Не поворачивается корпусом (бронированная)")] public bool fixedFacing;
        public void Validate(string type)
        {
            if(patrolAngles==null || patrolAngles.Length==0 || patrolAngles.Any(a=>!float.IsFinite(a)) || !float.IsFinite(periodSeconds) || periodSeconds<=0 ||
                !float.IsFinite(watchTurnDegreesPerSecond) || watchTurnDegreesPerSecond<=0 || !float.IsFinite(attackTurnDegreesPerSecond) || attackTurnDegreesPerSecond<=0 ||
                !float.IsFinite(alertMinSeconds) || alertMinSeconds<0 || !float.IsFinite(alertMaxSeconds) || alertMaxSeconds<alertMinSeconds ||
                !float.IsFinite(searchSeconds) || searchSeconds<0)
                throw new InvalidOperationException($"Некорректные настройки турели {type}.");
        }
    }

    // The family AI's own state (saved and handed over with the actor's owner): its mode, a mode timer, the flight
    // direction and whether it hangs upside down (a slime on a ceiling).
    [Serializable] public sealed class NpcCreatureState
    {
        public string mode;
        public float timer,directionX,directionY;
        public bool inverted;
    }

    // Free flight (original isFly units): the velocity turns towards the wanted one by the acceleration; walls and
    // ceilings stop the blocked axis (the families turn on it); one-way platforms are flown through.
    public static class FlyerMotion
    {
        public readonly struct Contact
        {
            public readonly bool BlockedX,BlockedY,Ceiling,Floor;
            public Contact(bool x,bool y,bool ceiling,bool floor) {BlockedX=x;BlockedY=y;Ceiling=ceiling;Floor=floor;}
        }
        public static Contact Step(ActorState actor,RoomLayout room,Vector2 wanted,float acceleration,float dt)
        {
            if(!float.IsFinite(wanted.x) || !float.IsFinite(wanted.y) || dt<=0) return default;
            var velocity=Vector2.MoveTowards(new Vector2(actor.vx,actor.vy),wanted,acceleration*dt);
            var hull=Anatomy.Movement(actor);var start=actor.Position;var delta=velocity*dt;
            var moved=ActorTerrainMotion.Move(room,hull,start,delta,true,bodyTops:false);
            bool blockedX=Mathf.Abs(moved.x-(start.x+delta.x))>.0001f,blockedY=Mathf.Abs(moved.y-(start.y+delta.y))>.0001f;
            if(blockedX) velocity.x=0;
            if(blockedY) velocity.y=0;
            actor.Position=moved;actor.vx=velocity.x;actor.vy=velocity.y;
            if(Mathf.Abs(velocity.x)>.05f) actor.facing=velocity.x>0?1:-1;
            return new Contact(blockedX,blockedY,blockedY && delta.y>0,blockedY && delta.y<0);
        }
    }
}
