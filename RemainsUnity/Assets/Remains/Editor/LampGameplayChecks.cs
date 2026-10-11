using System;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class LampGameplayChecks
    {
        public static void Run()
        {
            var world=new WorldState{worldId="lamp-check"};
            var actor=new ActorState{id="player",owner="owner",level="factory:0",connected=true,player=true,generation=2,lifecycleSequence=4};world.actors.Add(actor);
            var lamp=new LampState{id="factory:0:0,0:lamp",level=actor.level,x=2,y=1,width=.4f,height=.6f};
            Assert(LampOperations.Install(world,actor.level,new[]{lamp}),"initial registration");
            lamp=world.lamps[0];
            var attack=new AttackEvent{id="swing",attacker=actor.id,sourceOwner=actor.owner,sourcePlayer=true,weapon="crowbar",level=actor.level,
                originX=1,originY=1.3f,endX=3,endY=1.3f,radius=.08f};
            var request=new LampBreakRequestData{world=world.worldId,lamp=lamp.id,decisionActor=actor.id,decisionGeneration=2,decisionLifecycle=4,
                sourceGeneration=2,sourceLifecycle=4,attack=attack};
            var ledger=new DoorDamageAttackLedger();ledger.Record(world.worldId,actor,attack);
            WeaponDefinition Weapon(string id)=>new(){id=id,damage=14,projectileSpeed=id=="pistol"?25:0};
            Assert(LampBreakRules.Validate(world,actor,request,ledger,out _,Weapon)==null,"accepted melee");
            request.decisionLifecycle=3;Assert(LampBreakRules.Validate(world,actor,request,ledger,out _,Weapon)!=null,"stale lifecycle rejected");request.decisionLifecycle=4;
            actor.owner="other";Assert(LampBreakRules.Validate(world,actor,request,ledger,out _,Weapon)!=null,"wrong owner rejected");actor.owner="owner";
            var missed=StateJson.Copy(attack);missed.id="miss";missed.originY=4;missed.endY=4;ledger.Record(world.worldId,actor,missed);request.attack=missed;
            Assert(LampBreakRules.Validate(world,actor,request,ledger,out _,Weapon)!=null,"accepted attack missing lamp rejected");request.attack=attack;
            request.sourceGeneration=1;Assert(LampBreakRules.Validate(world,actor,request,ledger,out _,Weapon)!=null,"stale epoch rejected");request.sourceGeneration=2;
            request.attack=StateJson.Copy(attack);request.attack.endY=5;
            Assert(LampBreakRules.Validate(world,actor,request,ledger,out _,Weapon)!=null,"unaccepted segment rejected");request.attack=attack;
            Assert(LampOperations.Break(world,lamp,attack.id),"break");long revision=world.lampRevision;
            Assert(!LampOperations.Break(world,lamp,attack.id)&&world.lampRevision==revision,"duplicate idempotent");
            var reload=world.Copy();Assert(reload.lamps[0].broken&&reload.lamps[0].breakingAttack==attack.id&&reload.lampRevision==revision,"save roundtrip");
            Assert(!LampOperations.Install(reload,actor.level,new[]{new LampState{id=lamp.id,level=lamp.level,x=lamp.x,y=lamp.y,width=lamp.width,height=lamp.height}})&&reload.lamps[0].broken,"install cannot revive");
            attack=StateJson.Copy(attack);attack.id="bullet";attack.weapon="pistol";attack.speed=25;attack.radius=0;
            world.projectiles.Add(new ProjectileState{launch=StateJson.Copy(attack),owner=actor.owner,generation=3,flightSpeed=25});
            request.attack=attack;request.projectileGeneration=3;
            Assert(LampBreakRules.Validate(world,actor,request,ledger,out _,Weapon)==null,"accepted projectile");
            request.projectileGeneration=2;Assert(LampBreakRules.Validate(world,actor,request,ledger,out _,Weapon)!=null,"stale projectile rejected");
            request.projectileGeneration=3;attack.endX=20;Assert(LampBreakRules.Validate(world,actor,request,ledger,out _,Weapon)!=null,"outside launch trajectory rejected");
            Debug.Log("LampGameplayChecks PASS: melee/projectile authority, stale epochs, accepted segments, persistence, duplicate and no resurrection.");
        }
        private static void Assert(bool value,string context){if(!value)throw new InvalidOperationException("Лампы: "+context);}
    }
}
