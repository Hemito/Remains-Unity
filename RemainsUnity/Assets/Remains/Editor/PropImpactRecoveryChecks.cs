using System;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class PropImpactRecoveryChecks
    {
        public static void RunBatch()
        {try {Run();UnityEditor.EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);UnityEditor.EditorApplication.Exit(1);}}
        public static void Run()
        {
            CheckClocksAndTravel();CheckAuthorityEpoch();
            Debug.Log("Box recovery: PASS — level-qualified cooldown, hub3000/floor0, same-level pause/save, live epoch vs recovered host generation.");
        }
        private static void CheckClocksAndTravel()
        {
            var world=new WorldState {worldId="box-clock",levelTimeVersion=1};
            world.levelTimes.Add(new LevelSimulationTime {level="hub",elapsedSeconds=3000});
            world.levelTimes.Add(new LevelSimulationTime {level="factory:0",elapsedSeconds=0});
            var clocks=new LevelTimeAuthority(world);
            var actor=new ActorState {id="traveller",level="hub",health=100,player=true,connected=true};world.actors.Add(actor);
            PropImpactCooldown.Record(actor,"hub",clocks.Read("hub")+1);
            Require(PropImpactCooldown.Active(actor,clocks.Read("hub")),"Защита на исходном уровне не активна.");
            clocks.Advance(30,_=>false);
            Require(PropImpactCooldown.Active(actor,clocks.Read("hub")),"Пауза израсходовала игровую защиту.");
            world=StateJson.Copy(world);actor=world.Find(actor.id);clocks=new LevelTimeAuthority(world);
            Require(PropImpactCooldown.Active(actor,clocks.Read("hub")),"Загрузка потеряла остаток защиты того же уровня.");
            TravelRules.Apply(actor,"factory:0",new RoomLayout {spawnX=2,spawnY=0});
            Require(!PropImpactCooldown.Active(actor,clocks.Read("factory:0")) && actor.boxImpactUntil==0 && actor.boxImpactLevel==null,
                "Срок hub3001 перенесён в часы factory0 и блокирует удары тысячи секунд.");
            PropImpactCooldown.Record(actor,"factory:0",.5);
            Require(actor.boxImpactUntil==.5 && actor.boxImpactLevel=="factory:0","Новый срок унаследовал абсолютное время другого уровня.");
            clocks.Advance(.6,level=>level=="factory:0");
            Require(!PropImpactCooldown.Active(actor,clocks.Read("factory:0")),"Локальные часы не завершили защиту.");
            var legacy=actor.Copy();legacy.boxImpactLevel=null;legacy.boxImpactUntil=3001;
            Require(!PropImpactCooldown.Active(legacy,0),"Старый неквалифицированный срок применён к неизвестному уровню.");
            var incoming=actor.Copy();PropImpactCooldown.Record(actor,actor.level,5);PropImpactCooldown.Preserve(actor,incoming);
            Require(incoming.boxImpactUntil==5 && incoming.boxImpactLevel==actor.level,"Снимок того же уровня потерял подтверждённую защиту.");
            incoming.level="hub";incoming.boxImpactLevel=null;incoming.boxImpactUntil=0;PropImpactCooldown.Preserve(actor,incoming);
            Require(incoming.boxImpactUntil==0,"Preserve перенёс deadline между уровнями.");
        }
        private static void CheckAuthorityEpoch()
        {
            var canonical=new PropState {id="box",owner="owner",level="factory:0",generation=3,sequence=5,x=1,vx=6};
            var contact=new PropImpactContact {id="accepted",target="target",source=new PropImpactSample {world="world",prop=canonical.id,owner=canonical.owner,
                level=canonical.level,generation=3},generationBefore=3,generationAfter=4,propVx=2,propVy=1};
            var local=StateJson.Copy(canonical);
            Require(PropImpactRules.AdoptImpulse(local,contact,"live-epoch"),"Live accepted contact не применён.");
            var stale=StateJson.Copy(canonical);PropReplication.PreserveNewerPose(local,stale,"owner","live-epoch");
            Require(stale.generation==4 && stale.vx==2,"Обычный старый snapshot откатил live accepted impulse.");
            var equal=StateJson.Copy(stale);PropReplication.PreserveNewerPose(stale,equal,"owner","live-epoch");
            Require(equal.impactAuthorityEpoch=="live-epoch","Обычный снимок равного epoch потерял runtime proof.");
            var recoveredLocal=StateJson.Copy(local);
            Require(recoveredLocal.impactAuthorityEpoch==null,"Runtime authority proof попал в сохранение.");
            var recoveredHost=StateJson.Copy(canonical);PropReplication.PreserveNewerPose(recoveredLocal,recoveredHost,"owner","new-authority-epoch");
            Require(recoveredHost.generation==3 && recoveredHost.vx==6,"Persisted contact journal повысил recovered host generation без live authority proof.");
            var newHost=StateJson.Copy(canonical);PropReplication.PreserveNewerPose(local,newHost,"owner","new-authority-epoch");
            Require(newHost.generation==3,"Новый host session принял ephemeral proof старого соединения.");
            Require(PropReplication.Accept(newHost,3,6,2,0,4,0) && newHost.vx==4,"Recovered host G больше не принимает законные pose G после localG+1.");
            var newer=StateJson.Copy(canonical);newer.generation=5;newer.vx=8;
            PropReplication.PreserveNewerPose(local,newer,"owner","live-epoch");
            Require(newer.generation==5 && newer.vx==8,"Live proof понизил более новое host authority generation.");
        }
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    }
}
