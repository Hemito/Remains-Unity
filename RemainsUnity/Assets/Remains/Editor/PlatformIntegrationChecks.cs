using System;
using System.Linq;
using Remains.Content;
using Remains.Runtime;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class PlatformIntegrationChecks
    {
        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();
            var world=LegacyFactoryFixture.Create();MechanismPersistence.Prepare(world,()=>{});
            RoomContentInstaller.Apply(world,RoomContentBatch.Read(RoomAuthoring.Load("factory:0",world),"factory:0"));
            LevelTimeMigration.Prepare(world,new[]{"factory:0"});
            var platform=world.movingPlatforms.Single(p=>p.level=="factory:0");
            var button=world.mechanismButtons.Single(b=>b.platformId==platform.id && b.x<platform.startX);
            var catalog=MechanismCatalogAsset.Load();var type=catalog.Platform(platform.definition).CreateDefinition();
            var buttonType=catalog.Button(button.definition).CreateDefinition();
            var actor=new ActorState {id="platform-save-rider",player=true,connected=true,owner="local",level=platform.level,
                x=button.x,y=button.y,health=100};world.actors.Add(actor);
            var progress=new ButtonRepairProgress();
            progress.Begin("mechanism:integration-repair",actor,button,buttonType.repairDurationSeconds,buttonType.interactionRadius);
            var repaired=progress.Step(actor,button,true,false,buttonType.repairDurationSeconds);
            var operations=new MechanismOperations(world);
            Require(operations.ApplyRepair(actor,button,repaired.Candidate,buttonType.interactionRadius,repaired.Candidate.AttemptId).Changed,"repair apply");
            Require(platform.segment==null && platform.endpoint==0 && !button.pressed,"repair must not activate");
            Require(operations.TryActivate(actor,button,platform,type,0,button.revision,buttonType.interactionRadius,"mechanism:integration-start").Changed,"separate activation");
            world.levelTimes.Single(clock=>clock.level==platform.level).elapsedSeconds=type.durationSeconds*.5;
            var sample=new MovingPlatformMotion(type).Evaluate(platform,type.durationSeconds*.5);
            actor.Position=new Vector2(sample.SupportBounds.center.x,sample.SupportBounds.yMax);actor.vx=actor.vy=0;
            string saved=StateJson.ToJson(world);var restored=StateJson.FromJson<WorldState>(saved);
            var room=RoomLayout.Load(actor.level,restored);
            PlatformSimulation.RestoreSupport(restored,room,actor.level,type.durationSeconds*.5);
            var rider=restored.Find(actor.id);var hull=Anatomy.Movement(rider);
            Require(ActorTerrainMotion.IsClear(room,hull,rider.Position) && ActorTerrainMotion.Supported(room,hull,rider.Position),"restored live platform support");
            Require(!SavedActorPlacement.Repair(rider,room,"проверка сохранённой платформы") && StateJson.ToJson(restored)==saved,"load keeps rider/segment/clock exactly");
            Require(!StateJson.ToJson(room).Contains("dynamicSurfaces",StringComparison.Ordinal),"transient support excluded from save/hash");
            var initialFrame=room.dynamicSurfaces;
            PlatformSimulation.RestoreSupport(restored,room,actor.level,type.durationSeconds*.5);
            Require(!ReferenceEquals(initialFrame,room.dynamicSurfaces) && room.dynamicSurfaces.IsPublished &&
                StateJson.ToJson(room.dynamicSurfaces.Pose(0).Current)==StateJson.ToJson(initialFrame.Pose(0).Current),
                "repeated detached preparation restores a complete fresh support frame");
            var withoutPlatform=StateJson.Copy(restored);withoutPlatform.movingPlatforms.Clear();
            PlatformSimulation.RestoreSupport(withoutPlatform,room,actor.level,type.durationSeconds*.5);
            Require(room.dynamicSurfaces.Count==0 && room.dynamicSurfaces.IsPublished,"removed platform leaves no stale detached support");
            PlatformSimulation.RestoreSupport(restored,room,actor.level,type.durationSeconds*.5);
            PlatformSimulationChecks.CheckDepartures(type,platform,room,hull);
            Debug.Log("Интеграция платформы: PASS — реальный контент, раздельные ремонт/запуск и восстановление опоры перед загрузкой пассажира; первый departure/reverse уже стоящих actor/cargo на реальной сохранённой платформе/геометрии при 30/60/144.");
        }
        private static void Require(bool valid,string context) {if(!valid) throw new InvalidOperationException("PlatformIntegration: "+context);}
    }
}
