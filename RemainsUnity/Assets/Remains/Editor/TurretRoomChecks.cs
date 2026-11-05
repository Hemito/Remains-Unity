using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    // A real ceiling turret in a real room (PT-20261103-21): it finds a player on either side, its shots reach the body
    // and take health; a heard noise turns it, a hit wakes it at once facing the shooter, an armored one ignores both.
    public static class TurretRoomChecks
    {
        private const float Dt=1f/30,Ceiling=5.4f;
        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();
            var room=Room();
            foreach(int side in new[]{-1,1})
            {
                var (shots,contacts,lost)=Fight("turret0-1",room,side);
                Require(shots>0,$"сторона {side}: нет выстрелов");
                Require(contacts==shots,$"сторона {side}: в тело попало {contacts} из {shots}");
                Require(lost>0,$"сторона {side}: попадание не отнимает здоровье");
            }
            var type=ActorTypes.Get("turret0-1");
            // Looking right, a noise on the left: it searches there and faces it.
            var turret=Create("turret0-1");
            NpcTurret.Step(turret,type,null,false,null,room,Dt,new System.Random(1));
            turret.weaponAngle=-30;turret.facing=1;
            NpcTurret.Step(turret,type,null,false,new Vector2(-6,0),room,Dt,new System.Random(1));
            NpcTurret.Step(turret,type,null,false,new Vector2(-6,0),room,Dt,new System.Random(1));
            Require(turret.creature.mode==NpcTurret.Search && turret.facing==-1,$"шум слева: {turret.creature.mode}, сторона {turret.facing}");
            // A hit from behind: awake at once, facing the shooter; the look ignores which way it faced.
            turret=Create("turret0-1");NpcTurret.Step(turret,type,null,false,null,room,Dt,new System.Random(1));turret.facing=1;
            var shooter=Player(-6);
            Require(NpcTurret.Alarm(turret,type,shooter.Position) && turret.creature.mode==NpcTurret.Attack && turret.facing==-1,"удар не разбудил турель");
            turret.facing=1;
            Require(new NpcPerception(type.perception).Alarm(turret,turret.perception,shooter,(a,b)=>true) && turret.target==shooter.id,"удар сзади не даёт цель");
            turret.target=null;
            Require(!new NpcPerception(type.perception).Alarm(turret,turret.perception,shooter,(a,b)=>false) && turret.target==null && turret.perception.hasKnownPosition,
                "стрелок за стеной: цели нет, но место известно");
            var armored=ActorTypes.Get("turret3-1");
            Require(!NpcTurret.Alarm(Create("turret3-1"),armored,shooter.Position),"бронированная не отвечает на удар");
            Debug.Log("Турель в комнате: PASS — игрок с обеих сторон, выстрелы попадают и ранят, шум и удар разворачивают, бронированная не отвечает.");
        }
        // The order of NpcController.Step, without a session.
        private static (int shots,int contacts,float lost) Fight(string actorType,RoomLayout room,int side)
        {
            var actor=Create(actorType);var type=ActorTypes.Get(actorType);var player=Player(side*4);
            var perception=new NpcPerception(type.perception);var subjects=new List<ActorState> {player};
            var timing=new GuardWeaponTiming();var random=new System.Random(5);
            float sense=0,lost=0;int shots=0,contacts=0;
            for(int frame=0;frame<30*14;frame++)
            {
                sense+=Dt;if(sense>=.1f) {perception.Step(actor,actor.perception,subjects,(a,b)=>RoomVisibility.Clear(room,a,b),sense);sense=0;}
                var target=actor.target==player.id || actor.attackTarget==player.id?player:null;
                bool visible=target!=null && RoomVisibility.Clear(room,WeaponPose.RangedAnchor(actor),target.BodyPosition+Vector2.up*.55f);
                actor.attackCooldown=Mathf.Max(0,actor.attackCooldown-Dt);
                if(actor.attackBusy)
                {
                    NpcTurret.Follow(actor,type,target,visible,room,Dt);actor.attackRemaining-=Dt;
                    if(actor.attackRemaining>0) continue;
                    var shot=NpcAttackRules.Complete(actor,target,room,timing);
                    if(shot==null) continue;
                    shots++;shot.speed=WeaponCatalog.Get(shot.weapon).projectileSpeed;
                    var body=StateJson.Copy(player);
                    if(Anatomy.Contact(body,shot)==null) continue;
                    contacts++;
                    if(new DamageReceiver(new PrototypeDamageRule()).Apply(body,shot,true)) lost=player.health-body.health;
                    continue;
                }
                bool fire=NpcTurret.Step(actor,type,target,visible,actor.perception.hasKnownPosition?actor.perception.KnownPosition:(Vector2?)null,room,Dt,random);
                if(fire) NpcAttackRules.TryBegin(actor,target,timing);
            }
            return (shots,contacts,lost);
        }
        private static ActorState Create(string actorType)
        {
            var entry=CreatureCatalog.Load().Creatures.First(c=>c.actorType==actorType);
            var actor=entry.spawn.Create(new TrainingPlacement {id="turret-room",type=actorType,facing=1},"check");
            actor.x=0;actor.y=Ceiling;actor.level="check";actor.owner="o";
            var gun=entry.spawn.ReadInventory().Generate("check-world",actor.id).Single();gun.id="gun";
            actor.inventory.Clear();actor.inventory.Add(gun);actor.weaponInventoryVersion=1;actor.equippedWeaponItem="gun";
            return actor;
        }
        private static ActorState Player(float x)=>new() {id="target",player=true,connected=true,owner="p",level="check",x=x,y=0,health=100,facing=1};
        private static RoomLayout Room()
        {
            var room=new RoomLayout {id="turret-check",left=-20,right=20,bottom=-1,top=7,ladders=Array.Empty<RoomLadder>(),platforms=new[]{
                new RoomSurface {x=-20,y=-1,width=40,height=1},new RoomSurface {x=-20,y=6,width=40,height=1}}};
            RoomSurfaces.Prepare(room);return room;
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Турель в комнате: "+message);}
    }
}
