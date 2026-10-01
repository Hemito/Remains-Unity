using System;
using UnityEngine;

namespace Remains.Domain
{
    // Straight flight only. The caller supplies current contacts; no damage, networking or rendering.
    public sealed class LinearProjectilePath
    {
        public Vector2 Origin {get;}
        public Vector2 Direction {get;}
        public float Speed {get;}
        public float Range {get;}
        public float Traveled {get;private set;}
        public bool Finished {get;private set;}
        public Vector2 Position=>Origin+Direction*Traveled;
        public LinearProjectilePath(Vector2 origin,Vector2 direction,float speed,float range,float traveled=0,bool finished=false)
        {
            double magnitude=Math.Sqrt((double)direction.x*direction.x+(double)direction.y*direction.y);
            if(!Finite(origin) || !Finite(direction) || magnitude<=0 ||
                !float.IsFinite(speed) || speed<=0 || !float.IsFinite(range) || range<=0 ||
                !float.IsFinite(traveled) || traveled<0 || traveled>range)
                throw new ArgumentException("Некорректные параметры прямолинейного полёта.");
            Origin=origin;Direction=new Vector2((float)(direction.x/magnitude),(float)(direction.y/magnitude));Speed=speed;Range=range;Traveled=traveled;
            Finished=finished || traveled==range;
            if(!Finite(Origin+Direction*range)) throw new ArgumentException("Конец траектории выходит за допустимый диапазон координат.");
        }
        public ProjectileStep Advance(float dt,Func<Vector2,Vector2,float?> firstContact)
        {
            if(!float.IsFinite(dt) || dt<0) throw new ArgumentOutOfRangeException(nameof(dt),"Шаг полёта должен быть конечным и неотрицательным.");
            if(firstContact==null) throw new ArgumentNullException(nameof(firstContact));
            var start=Position;
            if(Finished || dt==0) return new ProjectileStep(start,start,false,false,0);
            float distance=(float)Math.Min((double)Speed*dt,Range-Traveled);
            var end=Origin+Direction*(Traveled+distance);
            float? fraction=firstContact(start,end);
            if(fraction.HasValue && (!float.IsFinite(fraction.Value) || fraction.Value<0 || fraction.Value>1))
                throw new InvalidOperationException($"Некорректная доля контакта снаряда: fraction={fraction}.");
            float passed=distance*(fraction??1);
            Traveled=Mathf.Min(Range,Traveled+passed);
            Finished=fraction.HasValue || Traveled>=Range;
            return new ProjectileStep(start,Position,true,fraction.HasValue,passed/Speed);
        }
        private static bool Finite(Vector2 value)=>float.IsFinite(value.x) && float.IsFinite(value.y);
    }
    public readonly struct ProjectileStep
    {
        public readonly Vector2 start,end;
        public readonly bool advanced,contact;
        public readonly float duration;
        public ProjectileStep(Vector2 start,Vector2 end,bool advanced,bool contact,float duration)
        {this.start=start;this.end=end;this.advanced=advanced;this.contact=contact;this.duration=duration;}
    }
}
