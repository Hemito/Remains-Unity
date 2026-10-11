using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Runtime;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class RoomContentPublicationChecks
    {
        private const string Level="factory:0";
        private static int assertions;

        public static void Run()
        {
            CheckRules();
            WeaponCatalog.Load();
            var room=RoomCatalogAsset.Load().Resolve(LegacyFactoryFixture.DefinitionId);
            var batch=RoomContentBatch.Read(room,Level);
            CheckInstallation(Fixture(),batch);
            Debug.Log("Публикация содержимого: PASS — "+assertions+" проверок; один подготовленный набор, точный лут, прежние ссылки/состояния, атомарный отказ и повтор без изменений.");
        }

        // Pure checks use the production installers and serialization, without a scene.
        public static int CheckRules()
        {
            assertions=0;
            CheckInstallation(Fixture(),Batch());

            var world=Fixture();var batch=Batch();string before=StateJson.ToJson(world);
            // Mechanisms are last: preceding valid doors, seeded loot and NPCs must
            // stay detached when the final component discovers a conflicting ID.
            batch.doors.doors[1].id=Level+":new-door";
            batch.mechanisms.movingPlatforms[1].id=batch.doors.doors[1].id;
            batch.mechanisms.mechanismButtons[1].platformId=batch.doors.doors[1].id;
            Reject(()=>RoomContentInstaller.Apply(world,batch),"Поздний конфликт механизма не отклонён.");
            Require(before==StateJson.ToJson(world),"Поздний конфликт опубликовал часть содержимого.");

            CheckRejectedStage(s=>s.actors[0].health=17,"Изменение прежнего актёра.");
            CheckRejectedStage(s=>s.doors[0].open=false,"Изменение прежней двери.");
            CheckRejectedStage(s=>s.containers[0].items.Add(new ItemStack {id="unexpected",definition="caps",count=1}),"Наполнение прежнего контейнера.");
            CheckRejectedStage(s=>s.extensions["mod:world"]["progress"]=12,"Изменение неизвестного поля мира.");
            CheckRejectedStage(s=>s.actors[0].extensions["mod:actor"]["state"]="changed","Изменение неизвестного поля актёра.");
            CheckRejectedStage(s=>s.pendingGrants.Clear(),"Удаление вне append-списков.");
            CheckRejectedStage(s=>s.seed++,"Изменение прочего поля мира.");
            CheckRejectedStage(s=>s.actors.RemoveAt(0),"Удаление прежней записи.");
            CheckRejectedStage(s=>s.actors.Reverse(),"Перестановка прежних записей.");
            CheckRejectedStage(s=>s.props=new List<PropState>(s.props),"Замена подготовленного списка.");
            CheckRejectedStage(s=>s.revision++,"Ревизия не соответствует новым отметкам.");
            CheckRejectedStage(s=>s.contentPatches.RemoveAt(s.contentPatches.Count-1),"Утрата новой отметки версии.");

            world=Fixture();var plan=RoomContentPublication.Capture(world);before=StateJson.ToJson(world);
            Require(!ReferenceEquals(plan.Staged,world) && !ReferenceEquals(plan.Staged.actors[0],world.actors[0]) &&
                !ReferenceEquals(plan.Staged.actors[0].inventory,world.actors[0].inventory) &&
                !ReferenceEquals(plan.Staged.extensions,world.extensions),"Capture оставил общие изменяемые данные.");
            Reject(plan.Prepare,"Пустой план принят без новых версий.");
            Require(before==StateJson.ToJson(world),"Пустой план изменил живой мир.");
            Reject(plan.Prepare,"Повторно использован отказавший план.");

            CheckDestinationChange(w=>w.revision++,"Изменённая live ревизия.");
            CheckDestinationChange(w=>w.actors.Add(new ActorState {id="late"}),"Изменённое live число актёров.");
            CheckDestinationChange(w=>w.mechanismButtons=new List<MechanismButtonState>(w.mechanismButtons),"Замена live списка.");

            world=Fixture();plan=Prepared(world);var other=StateJson.Copy(world);before=StateJson.ToJson(world);
            Reject(()=>plan.Publish(other),"План опубликован в другом экземпляре с тем же WorldId.");
            Require(before==StateJson.ToJson(world) && before==StateJson.ToJson(other),"Неверный экземпляр частично изменён.");
            Reject(()=>plan.Publish(world),"Отказавший план использован повторно.");

            world=Fixture();plan=Prepared(world);plan.Publish(world);before=StateJson.ToJson(world);
            Reject(()=>plan.Publish(world),"Повторная публикация выдала содержимое дважды.");
            Reject(plan.Prepare,"Опубликованный план подготовлен повторно.");
            Reject(()=>{var _=plan.Staged;},"Опубликованный план повторно выдал staging.");
            Require(before==StateJson.ToJson(world),"Повторный план изменил мир.");

            world=Fixture();world.revision=long.MaxValue;before=StateJson.ToJson(world);
            Reject(()=>RoomContentInstaller.Apply(world,Batch()),"Переполнение ревизии принято.");
            Require(before==StateJson.ToJson(world),"Переполнение оставило частичную публикацию.");
            return assertions;
        }

        private static void CheckInstallation(WorldState world,RoomContentBatch batch)
        {
            var actor=world.actors[0];var actorInventory=actor.inventory;var reservations=actor.inventoryReservations;
            var actorExtension=actor.extensions;var door=world.doors[0];var prop=world.props[0];var container=world.containers[0];
            var platform=world.movingPlatforms[0];var button=world.mechanismButtons[0];
            object[] lists={world.actors,world.doors,world.props,world.containers,world.movingPlatforms,world.mechanismButtons,world.contentPatches};
            string batchBefore=StateJson.ToJson(batch);var expected=StateJson.Copy(world);
            InstallExpected(expected,batch);long beforeRevision=world.revision;
            Require(RoomContentInstaller.Apply(world,batch),"Подготовленный набор не установлен.");
            Require(StateJson.ToJson(world)==StateJson.ToJson(expected),"Подготовленная публикация отличается от штатных установщиков: лут/ID/ревизии/поля.");
            Require(world.revision==beforeRevision+4 && world.contentPatches.Count==5,"Потеряны отметки или приращения ревизии четырёх компонентов.");
            object[] after={world.actors,world.doors,world.props,world.containers,world.movingPlatforms,world.mechanismButtons,world.contentPatches};
            Require(lists.Zip(after,ReferenceEquals).All(same=>same),"Заменён прежний live список.");
            Require(ReferenceEquals(actor,world.actors[0]) && ReferenceEquals(door,world.doors[0]) && ReferenceEquals(prop,world.props[0]) &&
                ReferenceEquals(container,world.containers[0]) && ReferenceEquals(platform,world.movingPlatforms[0]) && ReferenceEquals(button,world.mechanismButtons[0]),
                "Заменены прежние объекты живого мира.");
            Require(ReferenceEquals(actor.inventory,actorInventory) && ReferenceEquals(actor.inventoryReservations,reservations) &&
                actor.crouchPressSeconds==.37f && actor.poseCorrectionRemaining==.12f && reservations["reserved"]==2,
                "Установка утратила временные поля или вложенные ссылки актёра.");
            Require(ReferenceEquals(actorExtension,actor.extensions) && actor.down && actor.health==0 && container.items.Count==0 && container.x==19 &&
                prop.held && prop.owner=="holder" && platform.segment!=null && platform.segment.sequence==7 && button.repairState==MechanismRepairStates.Ready,
                "Установка сбросила прежнюю смерть/пустой лут/груз/сегмент/ремонт/данные мода.");
            Require(batchBefore==StateJson.ToJson(batch),"Установка изменила авторский набор или таблицу лута.");
            string before=StateJson.ToJson(world);
            Require(!RoomContentInstaller.Apply(world,batch) && before==StateJson.ToJson(world),"Повтор установки наполнил мир или увеличил ревизию.");
        }

        private static RoomContentPublication Prepared(WorldState world)
        {
            var plan=RoomContentPublication.Capture(world);InstallExpected(plan.Staged,Batch());plan.Prepare();return plan;
        }
        private static void CheckRejectedStage(Action<WorldState> mutate,string reason)
        {
            var world=Fixture();string before=StateJson.ToJson(world);var plan=RoomContentPublication.Capture(world);
            InstallExpected(plan.Staged,Batch());mutate(plan.Staged);Reject(plan.Prepare,reason);
            Require(before==StateJson.ToJson(world),"Guard изменил живой мир: "+reason);
        }
        private static void CheckDestinationChange(Action<WorldState> mutate,string reason)
        {
            var world=Fixture();var plan=Prepared(world);mutate(world);string before=StateJson.ToJson(world);
            Reject(()=>plan.Publish(world),reason);Require(before==StateJson.ToJson(world),"Изменившийся destination получил часть набора: "+reason);
        }
        private static void InstallExpected(WorldState world,RoomContentBatch batch)
        {
            if(batch.doors!=null) DoorContentInstaller.Apply(world,batch.doors);
            if(batch.physical!=null) PhysicalContentInstaller.Apply(world,batch.physical);
            if(batch.npcs!=null) NpcContentInstaller.Apply(world,batch.npcs);
            if(batch.mechanisms!=null) MechanismContentInstaller.Apply(world,batch.mechanisms);
        }

        private static WorldState Fixture()
        {
            var world=new WorldState {worldId="content-publication-world",branchId="content-publication-branch",revision=11,
                mechanismVersion=1,extensions=new Dictionary<string,JToken>{{"mod:world",JObject.Parse("{\"progress\":9,\"decimal\":1.234567890123456789}")}}};
            var actor=Npc("old-npc");actor.down=true;actor.health=0;actor.owner="old-owner";actor.x=37;
            actor.crouchPressSeconds=.37f;actor.poseCorrectionRemaining=.12f;actor.inventoryReservations["reserved"]=2;
            actor.extensions=new Dictionary<string,JToken>{{"mod:actor",JObject.Parse("{\"state\":\"saved\"}")}};
            actor.inventory.Add(new ItemStack {id="retained-ammo",definition="p10",count=2});world.actors.Add(actor);
            world.actors.Add(new ActorState {id="player",player=true,climbing=true,x=8,y=10.4f});
            var door=Door("old-door");door.open=true;door.revision=3;world.doors.Add(door);
            var prop=Prop("old-prop");prop.owner="holder";prop.held=true;prop.x=23;world.props.Add(prop);
            var container=Container("old-container");container.x=19;world.containers.Add(container);
            var platform=Platform("old-platform");platform.revision=7;
            platform.segment=new MovingPlatformSegment {sequence=7,fromEndpoint=0,toEndpoint=1,startElapsedSeconds=10,durationSeconds=6};
            world.movingPlatforms.Add(platform);
            var button=Button("old-button",platform.id);button.repairState=MechanismRepairStates.Ready;button.revision=9;world.mechanismButtons.Add(button);
            world.contentPatches.Add("prior");
            world.pendingGrants.Add(new PendingGrant {transaction="retained-tx",recipient="player",containerId=container.id,
                item=new ItemStack {id="pending-item",definition="caps",count=1}});
            return world;
        }
        private static RoomContentBatch Batch()
        {
            var table=new ContainerLootTable {id="publication-loot",version="1",entries=new[]{
                new ContainerLootEntry {id="caps",definition="caps",minimum=1,maximum=7},
                new ContainerLootEntry {id="pin",definition="pin",minimum=2,maximum=3}}};
            var platform=Platform("new-platform");
            return new RoomContentBatch {
                doors=new DoorLayout {patch=Level+":doors",level=Level,doors=new[]{Door("old-door"),Door("new-door")}},
                physical=new PhysicalLayout {patch=Level+":physical",level=Level,props=new[]{Prop("old-prop"),Prop("new-prop")},
                    containers=new[]{Container("old-container"),Container("new-container")},
                    loot=new[]{new ContainerLootBinding {container="new-container",table=table},new ContainerLootBinding {container="old-container",table=table}}},
                npcs=new NpcLayout {patch=Level+":npcs",level=Level,actors=new[]{Npc("old-npc"),Npc("new-npc")},
                    loot=new[]{new NpcLootBinding {actor="new-npc",table=table}}},
                mechanisms=new MechanismLayout {patch=Level+":mechanisms",level=Level,
                    platformDefinitions=new[]{new MovingPlatformDefinition {id="test-platform",width=2.4f,height=.78f,
                        graphicOffsetX=-1.2f,graphicOffsetY=.1f,graphicWidth=2.4f,graphicHeight=.7f,durationSeconds=6,surfaceTexture="floor",movementSound="metal"}},
                    buttonDefinitions=new[]{new MechanismButtonDefinition {id="test-button",repairDurationSeconds=1,interactionRadius=2,graphicWidth=1,graphicHeight=1}},
                    movingPlatforms=new[]{Platform("old-platform"),platform},
                    mechanismButtons=new[]{Button("old-button",Level+":old-platform"),Button("new-button",platform.id)}}};
        }
        private static ActorState Npc(string id)=>new() {id=id,actorType="raider",anatomy="pony",level=Level,weapon=null};
        private static DoorState Door(string id)=>new() {id=id,definition="door1",level=Level,width=.8f,height=1.6f,surfaceTexture="floor"};
        private static PropState Prop(string id)=>new() {id=id,definition="case",level=Level,anchorHeight=0};
        private static ContainerState Container(string id)=>new() {id=id,definition="case",level=Level,anchorHeight=0};
        private static MovingPlatformState Platform(string id)=>new() {id=Level+":"+id,definition="test-platform",definitionVersion=1,level=Level,
            roomId="publication-room",groupId="d2",startX=49.2f,startY=9.62f,endX=66,endY=9.62f};
        private static MechanismButtonState Button(string id,string platform)=>new() {id=Level+":"+id,definition="test-button",level=Level,
            roomId="publication-room",groupId="d2",platformId=platform,repairState=MechanismRepairStates.NeedsRepair,x=46,y=11.22f};
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Публикация содержимого: "+reason);assertions++;}
        private static void Reject(Action action,string reason)
        {try {action();}catch(InvalidOperationException) {assertions++;return;}throw new InvalidOperationException("Публикация содержимого: "+reason);}
    }
}
