using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    // BedRest: E at a bed within reach lays the pony on its top (belly: the fall's last frame; back: the authored clip),
    // W or Space gets it up, a knocked-down body leaves the bed; the pointer selects a bed only in reach.
    public static class BedRestChecks
    {
        public static void Run()
        {
            Remains.Content.ActorTypes.Load();Anatomy.Load();
            var bed=new RoomRest {id="bed",x=2,y=0,width=3.2f,height=.56f};
            var actor=new ActorState {id="p",player=true,connected=true,level="l",x=1.2f,y=0,health=100,actorType="pony",anatomy="pony",pose="walk"};
            Require(BedRest.Rejection(actor,bed)==null,"рядом с кроватью можно лечь");
            var far=new ActorState {id="f",player=true,connected=true,level="l",x=-1,y=0,health=100};
            Require(BedRest.Rejection(far,bed)!=null,"издалека лечь нельзя");

            BedRest.LieDown(actor,bed,BedRest.Belly);
            Require(BedRest.Active(actor) && Mathf.Approximately(actor.x,3.6f) && Mathf.Approximately(actor.y,.56f) && actor.vx==0 && actor.vy==0,
                $"лежит на середине верха кровати: ({actor.x}; {actor.y})");
            Require(ActorPose.Clip(actor)==BedRest.BellyClip && ActorPose.HoldsLastFrame(actor),"на животе — последний кадр падения");
            Require(BedRest.Rejection(actor,bed)!=null,"лежащий не ложится второй раз");
            actor.vx=3;
            Require(BedRest.Step(actor,false) && actor.vx==0,"лежит неподвижно");
            Require(!BedRest.Step(actor,true) && !BedRest.Active(actor) && ActorPose.Clip(actor)=="idle" && !ActorPose.HoldsLastFrame(actor),"W или пробел — встаёт");

            BedRest.LieDown(actor,bed,BedRest.Back);
            Require(ActorPose.Clip(actor)==BedRest.BackClip,"на спине — своя поза");
            actor.down=true;
            Require(!BedRest.Step(actor,false) && !BedRest.Active(actor),"сбитый с ног покидает кровать");
            actor.down=false;actor.x=1.2f;actor.y=0;

            var world=new WorldState();world.actors.Add(actor);
            var rules=GameplayRuleSet.CreateDefault();
            var target=InteractionSelection.Select(world,actor,rules,new Vector2(3,.3f),null,null,new[]{bed});
            Require(target.kind==InteractionKind.Bed && target.id=="bed","указатель на кровати в досягаемости выбирает её");
            Require(InteractionSelection.Select(world,far,rules,new Vector2(3,.3f),null,null,new[]{bed}).kind!=InteractionKind.Bed,"вне досягаемости кровать не выбирается");
            Debug.Log("Кровать: PASS — лечь рядом, на животе и на спине, неподвижно, встать по W/пробелу, сбитый с ног встаёт, выбор указателем.");
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Кровать: "+message);}
    }
}
