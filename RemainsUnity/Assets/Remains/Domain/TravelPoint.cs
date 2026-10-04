using System;
using UnityEngine;

namespace Remains.Domain
{
    public enum TravelPointKind { Map, Exit }
    [Serializable] public sealed class TravelPoint
    {
        public string id,level,destination,arrivalPoint,title;
        public TravelPointKind kind;
        public float x,y,width,height,range,arrivalX,arrivalY;
        public Rect Bounds=>new(x,y,width,height);
        public Vector2 Arrival=>new(arrivalX,arrivalY);
        public float Distance(ActorState actor)
        {
            var center=actor.Position+Vector2.up*(Anatomy.Movement(actor).height*.5f);
            return Vector2.Distance(center,new Vector2(Mathf.Clamp(center.x,x,x+width),Mathf.Clamp(center.y,y,y+height)));
        }
        public string Rejection(ActorState actor)
        {
            if(actor==null || !actor.connected) return "Персонаж не подключён к сессии.";
            if(actor.down) return "Персонаж без сознания и не может перейти самостоятельно.";
            if(actor.level!=level) return "Точка перехода находится в другой локации.";
            return Distance(actor)>range?"Подойдите к точке перехода ближе.":null;
        }
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(level) || string.IsNullOrWhiteSpace(destination) ||
                string.IsNullOrWhiteSpace(title) || !Enum.IsDefined(typeof(TravelPointKind),kind) ||
                !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(width) || !float.IsFinite(height) ||
                width<=0 || height<=0 || !float.IsFinite(range) || range<=0 || !float.IsFinite(arrivalX) || !float.IsFinite(arrivalY))
                throw new InvalidOperationException($"Некорректная точка перехода: {id}, level={level}, destination={destination}.");
        }
    }
}
