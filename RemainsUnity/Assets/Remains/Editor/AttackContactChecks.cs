using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class AttackContactChecks
    {
        public static void Run()
        {
            var npc=new ActorState {id="handoff-target",owner="first",level="hub",generation=4,lifecycleSequence=2};
            var pending=new AttackTargetSnapshot(npc,true);
            npc.owner="second";npc.generation++;
            Require(pending.CanApply(npc),"Смена владельца NPC потеряла уже установленный контакт");
            npc.lifecycleSequence++;
            Require(!pending.CanApply(npc),"Контакт применим к новому воплощению NPC");
            npc.lifecycleSequence--;npc.level="factory:0";
            Require(!pending.CanApply(npc),"Контакт пересёк границу уровня");
            npc.level="hub";npc.player=true;
            Require(!pending.CanApply(npc),"Контакт NPC применим к игроку");
            pending=new AttackTargetSnapshot(npc,true);npc.generation++;
            Require(!pending.CanApply(npc),"Смена владельца игрока сохранила старое решение о контакте");
            Require(HitGeometry.CircleEntry(Vector2.zero,1,new Vector2(-2,0),new Vector2(2,0))==.25f,"Точка входа в круг");
            Require(HitGeometry.CircleEntry(Vector2.zero,1,new Vector2(-2,2),new Vector2(2,2))==null,"Промах мимо круга");
            Require(HitGeometry.CircleEntry(Vector2.zero,1,Vector2.zero,Vector2.zero)==0,"Начало внутри круга");
            var near=new ActorState {id="near",player=true,connected=true,x=2,level="hub"};
            var far=new ActorState {id="far",player=true,connected=true,x=5,level="hub"};
            var other=new ActorState {id="other",player=true,connected=true,x=1,level="factory:0"};
            var shot=new AttackEvent {attacker="source",sourcePlayer=true,weapon="energy",level="hub",originX=0,originY=.7f,endX=10,endY=.7f};
            var wall=new RoomSurface {x=3,y=0,width=.5f,height=2};
            var room=new RoomLayout {platforms=new[]{wall}};
            Require(Mathf.Abs(ShotObstacles.Limit(room,shot.Origin,shot.End)-.3f)<.00001f,"Пересечение стены");
            Require(ShotObstacles.Limit(room,new Vector2(0,3),new Vector2(10,3))==1,"Луч над стеной");
            Require(ShotObstacles.Limit(room,new Vector2(3.2f,1),new Vector2(10,1))==0,"Источник внутри стены");
            Require(ShotObstacles.Limit(room,new Vector2(4,1),new Vector2(4,2))==1,"Параллельный луч вне стены");
            Require(AttackContacts.Capture(new[]{far},shot,true,true,ShotObstacles.Limit(room,shot.Origin,shot.End)).Count==0,"Стена закрывает дальнюю цель");
            Require(AttackContacts.Capture(new[]{far,near},shot,true,true,ShotObstacles.Limit(room,shot.Origin,shot.End))[0].id=="near","Цель перед стеной доступна");
            wall.shotsPassThrough=true;
            Require(ShotObstacles.Limit(room,shot.Origin,shot.End)==1,"Проницаемость задаётся данными");
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
