using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class AttackContactChecks
    {
        public static void Run()
        {
            Require(HitGeometry.CircleEntry(Vector2.zero,1,new Vector2(-2,0),new Vector2(2,0))==.25f,"Точка входа в круг");
            Require(HitGeometry.CircleEntry(Vector2.zero,1,new Vector2(-2,2),new Vector2(2,2))==null,"Промах мимо круга");
            Require(HitGeometry.CircleEntry(Vector2.zero,1,Vector2.zero,Vector2.zero)==0,"Начало внутри круга");
            var near=new ActorState {id="near",player=true,connected=true,x=2,level="hub"};
            var far=new ActorState {id="far",player=true,connected=true,x=5,level="hub"};
            var other=new ActorState {id="other",player=true,connected=true,x=1,level="factory:0"};
            var shot=new AttackEvent {attacker="source",sourcePlayer=true,weapon="energy",level="hub",originX=0,originY=.7f,endX=10,endY=.7f};
            foreach(var actors in new[]{new[]{far,other,near},new[]{near,far,other}})
            {
                var hits=AttackContacts.Capture(actors,shot,true,true);
                Require(hits.Count==1 && hits[0].id=="near","Одна ближайшая цель независимо от порядка и чужого уровня");
                Require(AttackContacts.Capture(actors,shot,true,false).Count==2,"Настраиваемое сквозное попадание");
                Require(AttackContacts.Capture(actors,shot,false,true).Count==0,"Отключённый дружественный огонь");
            }
        }
        private static void Require(bool value,string message)
        {if(!value) throw new InvalidOperationException("Контакт выстрела: "+message);}
    }
}
