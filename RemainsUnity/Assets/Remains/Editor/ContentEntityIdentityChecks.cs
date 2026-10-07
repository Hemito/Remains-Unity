using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class ContentEntityIdentityChecks
    {
        private const string Level="factory:0",Occupied=Level+":occupied";
        private const string ActorPrefix=Level+":prefix-actor",PhysicalPrefix=Level+":prefix-physical",DoorPrefix=Level+":prefix-door",MechanismPrefix=Level+":prefix-mechanism";
        private static int assertions;
        public static void Run()
        {
            int count=CheckRules();
            Debug.Log("Пространство ID содержимого: PASS — "+count+" проверок; шесть типов, атомарные отказы, прежние состояния/добавочные версии и неизвестные поля комнат.");
        }
        public static int CheckRules()
        {
            assertions=0;CheckRoomExtensions();CheckCrossKind();CheckMalformed();CheckExpansion();CheckBatch();return assertions;
        }
        private static void CheckRoomExtensions()
        {
            const string precise="1.2345678901234567890123456789";
            var world=StateJson.FromJson<WorldState>("{\"schema\":1,\"worldId\":\"11111111111111111111111111111111\",\"rooms\":[{\"level\":\"factory:-27\",\"definition\":\"mod:room\",\"modPayload\":{\"version\":99,\"precise\":"+precise+",\"nested\":{\"values\":[null,true,17,\"stable:token\"]}}}]}");
            var room=world.rooms[0];
            Require(room.extensions?.ContainsKey("modPayload")==true,"Утеряны неизвестные вложенные данные WorldRoom при чтении.");
            var payload=room.extensions["modPayload"];
            Require((decimal)payload["precise"]==decimal.Parse(precise,System.Globalization.CultureInfo.InvariantCulture),"Изменилась точность неизвестного числа комнаты.");
            var copy=StateJson.Copy(world);
            Require(WorldRooms.Definition(copy,"factory:-27")=="mod:room" && JToken.DeepEquals(payload,copy.rooms[0].extensions["modPayload"]),
                "Копия потеряла выбор signed factory или неизвестный payload.");
            Require(!ReferenceEquals(room,copy.rooms[0]) && !ReferenceEquals(payload,copy.rooms[0].extensions["modPayload"]),"Копия оставила общие изменяемые данные комнаты.");
            copy.rooms[0].definition="mod:expanded-room";copy.rooms[0].extensions["modPayload"]["version"]=100;
            Require(room.definition=="mod:room" && (int)payload["version"]==99,"Изменение копии затронуло исходную комнату.");
            var snapshot=RecoverySnapshot.FromWorld(world);var restored=snapshot.CreateLiveWorld();
            Require(JToken.DeepEquals(payload,restored.rooms[0].extensions["modPayload"]),"RecoverySnapshot потерял неизвестные поля комнаты.");
            restored.rooms[0].extensions["modPayload"]["nested"]["values"][3]="changed";
            Require((string)snapshot.CreateLiveWorld().rooms[0].extensions["modPayload"]["nested"]["values"][3]=="stable:token", "Новый snapshot consumer получил изменённый вложенный payload.");
            RejectData(()=>StateJson.FromJson<WorldRoom>("{\"Level\":\"factory:0\",\"definition\":\"mod:room\"}"),"регистр поля");
            RejectData(()=>StateJson.FromJson<WorldRoom>("{\"level\":\"factory:0\",\"level\":\"factory:6\",\"definition\":\"mod:room\"}"),"повтор поля");
            RejectData(()=>StateJson.FromJson<WorldRoom>("{\"level\":\"factory:0\",\"definition\":\"mod:room\",\"modPayload\":{\"unknown\":1e400}}"),"недопустимое неизвестное число");
            var invalid=StateJson.Copy(world);invalid.rooms.Add(StateJson.Copy(invalid.rooms[0]));
            Reject(()=>WorldRooms.Definition(invalid,"factory:-27"),"Повторная локация принята.");
            invalid=StateJson.Copy(world);invalid.rooms[0].definition=" ";Reject(()=>WorldRooms.Definition(invalid,"factory:-27"),"Пустое определение принято.");
            var conflict=new WorldRoom {level=Level,definition="mod:room",extensions=new Dictionary<string,JToken>{{"definition","conflict"}}};
            try {StateJson.ToJson(conflict);}
            catch(Exception error) when(IsRoomExtensionConflict(error)) {assertions++;return;}
            throw new InvalidOperationException("WorldRoom: конфликт extension с известным полем не отклонён.");
        }
        private static bool IsRoomExtensionConflict(Exception error)
        {
            // Unity/Mono invokes OnSerializing through reflection. Accept only the
            // intended rejection, whether direct or wrapped by reflection/Newtonsoft.
            while(error is System.Reflection.TargetInvocationException || error is JsonSerializationException)
                error=error.InnerException;
            return error is InvalidDataException && error.Message=="Дополнительное поле definition конфликтует с полем состояния WorldRoom.";
        }
        private static void CheckCrossKind()
        {
            var kinds=Enum.GetValues(typeof(ContentEntityKind)).Cast<ContentEntityKind>().ToArray();
            foreach(var saved in kinds) foreach(var incoming in kinds)
            {
                var world=World();Add(world,saved,Occupied);
                if(saved!=incoming) RejectUnchanged(world,()=>Install(world,incoming,Occupied),"Коллизия "+saved+" → "+incoming);
                else
                {
                    var retained=Find(world,saved,Occupied);string retainedJson=StateJson.ToJson(retained);
                    Install(world,incoming,Occupied);
                    Require(ReferenceEquals(retained,Find(world,saved,Occupied)) && StateJson.ToJson(retained)==retainedJson,
                        "Принятие существующего типа заменило объект/состояние: "+saved);
                    string before=StateJson.ToJson(world);Install(world,incoming,Occupied);
                    Require(StateJson.ToJson(world)==before,"Отмеченная версия применилась повторно: "+saved);
                }
            }
            var caseSensitive=World();caseSensitive.actors.Add(Npc(Level+":Case"));
            ContentEntityIds.Preflight(caseSensitive,new[]{new ContentEntityIdentity(ContentEntityKind.Prop,Level+":case")},"проверка регистра ID");
            Require(caseSensitive.props.Count==0,"Preflight изменил мир или свёл разные case-sensitive IDs.");
        }
        private static void CheckMalformed()
        {
            foreach(var kind in Enum.GetValues(typeof(ContentEntityKind)).Cast<ContentEntityKind>())
            {
                var world=World();Add(world,kind," ");RejectUnchanged(world,()=>Install(world,ContentEntityKind.Door,Occupied),"Пустой saved ID "+kind);
                world=World();Add(world,kind,Occupied);Add(world,kind,Occupied);
                RejectUnchanged(world,()=>Install(world,ContentEntityKind.Prop,Level+":fresh"),"Повтор saved ID "+kind);
                world=World();SetNull(world,kind,false);RejectUnchanged(world,()=>Install(world,ContentEntityKind.Door,Occupied),"Null saved record "+kind);
                world=World();SetNull(world,kind,true);RejectUnchanged(world,()=>Install(world,ContentEntityKind.Door,Occupied),"Null saved list "+kind);
                world=World();RejectUnchanged(world,()=>Install(world,kind,PrefixFor(kind)),"Повтор authored ID "+kind);
            }
            var malformed=World();Add(malformed,ContentEntityKind.Actor,Occupied);Add(malformed,ContentEntityKind.Prop,Occupied);
            RejectUnchanged(malformed,()=>Install(malformed,ContentEntityKind.Door,Level+":fresh"),"Повтор между saved типами вне нового набора.");
            Reject(()=>ContentEntityIds.ValidateCandidates(new[]{new ContentEntityIdentity((ContentEntityKind)999,Occupied)},"unknown category"),"Неизвестная категория принята.");
            Reject(()=>ContentEntityIds.ValidateCandidates(new[]{new ContentEntityIdentity(ContentEntityKind.Actor,Occupied),new ContentEntityIdentity(ContentEntityKind.Door,Occupied)},"cross authored"),"Повтор между authored типами принят.");
            Reject(()=>ContentEntityIds.Preflight(null,Array.Empty<ContentEntityIdentity>(),"нет мира"),"Null мир принят.");
            Reject(()=>ContentEntityIds.Preflight(World(),null,"нет списка"),"Null набор принят.");
        }
        private static void CheckExpansion()
        {
            var world=World();var old=Container(Level+":old-container");var added=Container(Level+":new-container");
            var first=new PhysicalLayout {patch=Level+":physical-v1",level=Level,containers=new[]{old}};
            PhysicalContentInstaller.Apply(world,first);world.containers[0].items.Clear();world.containers[0].x=73;
            world.containers[0].extensions=new Dictionary<string,JToken>{{"mod:retained",JObject.Parse("{\"state\":[3,\"empty\"]}")}};
            var saved=world.containers[0];string before=StateJson.ToJson(saved);
            var next=new PhysicalLayout {patch=Level+":physical-v2",level=Level,containers=new[]{old,added},
                expansion=new PhysicalExpansion {previousPatch=first.patch,additions=new[]{added.id}},
                loot=new[]{new ContainerLootBinding {container=old.id,table=Loot()},new ContainerLootBinding {container=added.id,table=Loot()}}};
            PhysicalContentInstaller.Apply(world,next);
            Require(ReferenceEquals(saved,world.containers[0]) && StateJson.ToJson(saved)==before && saved.items.Count==0 && saved.x==73,
                "Expansion изменил перемещённый/пустой старый контейнер или mod fields.");
            var generated=Loot().Generate(world.worldId,added.id);
            Require(StateJson.ToJson(world.containers.Single(c=>c.id==added.id).items)==StateJson.ToJson(generated),"Изменились stable item IDs/seeded loot при preflight.");
            world.containers.Clear();string empty=StateJson.ToJson(world);
            Require(!PhysicalContentInstaller.Apply(world,next) && StateJson.ToJson(world)==empty,"Отмеченная expansion воскресила удалённые объекты.");
            var deleted=World();PhysicalContentInstaller.Apply(deleted,first);deleted.containers.Clear();PhysicalContentInstaller.Apply(deleted,next);
            Require(deleted.containers.Count==1 && deleted.containers[0].id==added.id,"Предыдущая версия вернула удалённый старый объект.");
            var conflict=World();PhysicalContentInstaller.Apply(conflict,first);Add(conflict,ContentEntityKind.MechanismButton,added.id);
            RejectUnchanged(conflict,()=>PhysicalContentInstaller.Apply(conflict,next),"Expansion ID занят сохранённой кнопкой.");
        }
        private static void CheckBatch()
        {
            var world=World();Add(world,ContentEntityKind.MechanismButton,Occupied);
            var batch=new RoomContentBatch {doors=Doors(Level+":batch-door"),physical=Physical(ContentEntityKind.Container,Level+":batch-container"),
                npcs=Npcs(Occupied)};
            RejectUnchanged(world,()=>RoomContentInstaller.Apply(world,batch),"Поздний NPC collision опубликовал ранние door/physical компоненты.");
            world=World();var room=new WorldRoom {level=Level,definition="mod:room",extensions=new Dictionary<string,JToken>{{"modPayload",JObject.Parse("{\"chapter\":7,\"future\":{\"enabled\":true}}")}}};
            world.rooms.Add(room);var payload=room.extensions["modPayload"];string before=StateJson.ToJson(world.rooms);
            batch.npcs=Npcs(Level+":batch-npc");RoomContentInstaller.Apply(world,batch);
            Require(ReferenceEquals(room,world.rooms[0]) && ReferenceEquals(payload,room.extensions["modPayload"]) && StateJson.ToJson(world.rooms)==before,
                "Detached room publication потеряла ссылку/неизвестное состояние WorldRoom.");
            Require(world.doors.Count==2 && world.props.Count==1 && world.containers.Count==1 && world.actors.Count==2 && world.contentPatches.Count==3,
                "Полная публикация не установила ожидаемый набор.");
        }
        private static WorldState World()=>new() {worldId="11111111111111111111111111111111",branchId="22222222222222222222222222222222",mechanismVersion=1};
        private static ActorState Npc(string id)=>new() {id=id,actorType="raider",anatomy="pony",level=Level,weapon=null};
        private static PropState Prop(string id)=>new() {id=id,definition="test:prop",level=Level,anchorHeight=0};
        private static ContainerState Container(string id)=>new() {id=id,definition="test:container",level=Level,anchorHeight=0};
        private static DoorState Door(string id)=>new() {id=id,definition="test:door",level=Level,width=.8f,height=1.6f,surfaceTexture="test:surface"};
        private static MovingPlatformState Platform(string id)=>new() {id=id,definition="test:platform",definitionVersion=1,level=Level,roomId="test:room",groupId="group",
            startX=0,startY=1,endX=4,endY=1};
        private static MechanismButtonState Button(string id,string platform)=>new() {id=id,definition="test:button",level=Level,roomId="test:room",groupId="group",platformId=platform,repairState=MechanismRepairStates.Ready};
        private static ContainerLootTable Loot()=>new() {id="test:loot",version="1",entries=new[]{new ContainerLootEntry {id="caps",definition="caps",minimum=3,maximum=3}}};
        private static PhysicalLayout Physical(ContentEntityKind kind,string id)=>new() {patch=Level+":physical-test",level=Level,
            props=kind==ContentEntityKind.Prop?new[]{Prop(PhysicalPrefix),Prop(id)}:new[]{Prop(PhysicalPrefix)},containers=kind==ContentEntityKind.Container?new[]{Container(id)}:Array.Empty<ContainerState>()};
        private static DoorLayout Doors(string id)=>new() {patch=Level+":doors-test",level=Level,doors=new[]{Door(DoorPrefix),Door(id)}};
        private static NpcLayout Npcs(string id)=>new() {patch=Level+":npc-test",level=Level,actors=new[]{Npc(ActorPrefix),Npc(id)}};
        private static string PrefixFor(ContentEntityKind kind)=>kind switch
        {ContentEntityKind.Actor=>ActorPrefix,ContentEntityKind.Prop or ContentEntityKind.Container=>PhysicalPrefix,ContentEntityKind.Door=>DoorPrefix,_=>MechanismPrefix};
        private static MechanismLayout Mechanisms(ContentEntityKind kind,string id)
        {
            var platform=Platform(MechanismPrefix);return new MechanismLayout {patch=Level+":mechanisms-test",level=Level,
                movingPlatforms=kind==ContentEntityKind.MovingPlatform?new[]{platform,Platform(id)}:new[]{platform},
                mechanismButtons=kind==ContentEntityKind.MechanismButton?new[]{Button(MechanismPrefix+":button",platform.id),Button(id,platform.id)}:Array.Empty<MechanismButtonState>(),
                platformDefinitions=new[]{new MovingPlatformDefinition {id="test:platform",width=1,height=.3f,graphicWidth=1,graphicHeight=.3f,durationSeconds=6,surfaceTexture="test:surface",movementSound="test:movement"}},
                buttonDefinitions=new[]{new MechanismButtonDefinition {id="test:button",repairDurationSeconds=1,interactionRadius=2,graphicWidth=1,graphicHeight=1}}};
        }
        private static void Install(WorldState world,ContentEntityKind kind,string id)
        {
            switch(kind)
            {
                case ContentEntityKind.Actor:NpcContentInstaller.Apply(world,Npcs(id));break;
                case ContentEntityKind.Prop:case ContentEntityKind.Container:PhysicalContentInstaller.Apply(world,Physical(kind,id));break;
                case ContentEntityKind.Door:DoorContentInstaller.Apply(world,Doors(id));break;
                default:MechanismContentInstaller.Apply(world,Mechanisms(kind,id));break;
            }
        }
        private static void Add(WorldState world,ContentEntityKind kind,string id)
        {
            switch(kind)
            {
                case ContentEntityKind.Actor:var npc=Npc(id);npc.health=13;npc.owner="remote";world.actors.Add(npc);break;
                case ContentEntityKind.Prop:var prop=Prop(id);prop.held=true;prop.owner="remote";world.props.Add(prop);break;
                case ContentEntityKind.Container:var container=Container(id);container.x=33;world.containers.Add(container);break;
                case ContentEntityKind.Door:var door=Door(id);door.open=true;door.revision=7;world.doors.Add(door);break;
                case ContentEntityKind.MovingPlatform:var platform=Platform(id);platform.removed=true;platform.revision=7;world.movingPlatforms.Add(platform);break;
                case ContentEntityKind.MechanismButton:
                    string link=MechanismPrefix;if(!world.movingPlatforms.Any(p=>p.id==link)) world.movingPlatforms.Add(Platform(link));
                    var button=Button(id,link);button.removed=true;button.revision=8;world.mechanismButtons.Add(button);break;
            }
        }
        private static object Find(WorldState world,ContentEntityKind kind,string id)=>kind switch
        {ContentEntityKind.Actor=>world.actors.Single(a=>a.id==id),ContentEntityKind.Prop=>world.props.Single(p=>p.id==id),
            ContentEntityKind.Container=>world.containers.Single(c=>c.id==id),ContentEntityKind.Door=>world.doors.Single(d=>d.id==id),
            ContentEntityKind.MovingPlatform=>world.movingPlatforms.Single(p=>p.id==id),_=>world.mechanismButtons.Single(b=>b.id==id)};
        private static void SetNull(WorldState world,ContentEntityKind kind,bool list)
        {
            void Change<T>(ref List<T> entries) where T:class {if(list) entries=null;else entries.Add(null);}
            switch(kind)
            {case ContentEntityKind.Actor:Change(ref world.actors);break;case ContentEntityKind.Prop:Change(ref world.props);break;
                case ContentEntityKind.Container:Change(ref world.containers);break;case ContentEntityKind.Door:Change(ref world.doors);break;
                case ContentEntityKind.MovingPlatform:Change(ref world.movingPlatforms);break;case ContentEntityKind.MechanismButton:Change(ref world.mechanismButtons);break;}
        }
        private static void RejectUnchanged(WorldState world,Action action,string reason)
        {
            string before=StateJson.ToJson(world);object[] references={world.actors,world.props,world.containers,world.doors,world.movingPlatforms,world.mechanismButtons,world.contentPatches};
            Reject(action,reason);Require(StateJson.ToJson(world)==before,"Отказ изменил state: "+reason);
            object[] after={world.actors,world.props,world.containers,world.doors,world.movingPlatforms,world.mechanismButtons,world.contentPatches};
            Require(references.Zip(after,ReferenceEquals).All(v=>v),"Отказ заменил прежние list refs: "+reason);
        }
        private static void Reject(Action action,string reason)
        {try {action();} catch(InvalidOperationException){assertions++;return;} throw new InvalidOperationException("ID содержимого: "+reason);}
        private static void RejectData(Action action,string reason)
        {try {action();} catch(InvalidDataException){assertions++;return;} throw new InvalidOperationException("WorldRoom: некорректные данные приняты: "+reason);}
        private static void Require(bool value,string message) {assertions++;if(!value) throw new InvalidOperationException(message);}
    }
}
