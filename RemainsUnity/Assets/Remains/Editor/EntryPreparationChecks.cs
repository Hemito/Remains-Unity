using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class EntryPreparationChecks
    {
        public static void Run()
        {
            CheckDetachedView();CheckReadiness();CheckLatestBody();CheckGeometryRefresh();CheckTransfer();CheckClientAdmissionLifecycle();
            Debug.Log("Подготовка входа: PASS — detached view без публикации/подключения, одноразовый token/TTL, актуальный урон тела после подготовки, независимая готовность участников, строгие room/door контексты, ограниченные chunks и unknown данные. Native порядок света/Welcome проверяется отдельно.");
        }
        public static void CheckDetachedView()
        {
            var source=Fixture();string before=StateJson.ToJson(source);
            var context=Capture(source);
            Require(StateJson.ToJson(source)==before,"preview изменил исходный мир/sequence/состояние тела");
            Require(!ReferenceEquals(context.ViewWorld,source) && !ReferenceEquals(context.ViewWorld.Find("player:entry"),source.Find("player:entry")),"preview делит live references");
            Require(context.ViewWorld.actors.Count==1 && !context.ViewWorld.actors[0].connected &&
                context.ViewWorld.doors.Count==1 && context.ViewWorld.containers.Count==0 && context.ViewWorld.props.Count==0 &&
                context.ViewWorld.completedTransactions.Count==0,"view отправляет несвязанные игровые сущности/receipt");
            Require(context.RoomDefinitionsJson==StateJson.ToJson(source.rooms) &&
                context.ViewWorld.rooms[0].extensions["modPayload"]["precision"].Value<decimal>()==123456789.0123456789m &&
                context.ViewWorld.extensions["world-mod"].Value<string>()=="не терять","preview потерял неизвестные данные комнаты/мира");
            context.ViewWorld.Find("player:entry").health=999;
            context.ViewWorld.rooms[0].extensions["modPayload"]["precision"]=0;
            Require(StateJson.ToJson(source)==before,"consumer view изменил live source");
            Reject(()=>new EntryPreparationContext("wrong",source.Copy(),"player:entry","entry","factory:0"),"неверный token");
            Reject(()=>new EntryPreparationContext(Guid.NewGuid().ToString("N"),source.Copy(),"player:ally","ally","factory:0"),"подключённый preview actor");
            Reject(()=>new EntryPreparationContext(Guid.NewGuid().ToString("N"),source.Copy(),"player:entry","other","factory:0"),"другой character");
        }
        public static void CheckReadiness()
        {
            var context=Capture(Fixture());var ticket=new EntryPreparationTicket(context,10);
            ticket.RequireReady(context.Token,context.GeometryFingerprint,11);
            Reject(()=>ticket.RequireReady(Guid.NewGuid().ToString("N"),context.GeometryFingerprint,11),"старый/чужой token");
            Reject(()=>ticket.RequireReady(context.Token,new string('0',64),11),"подмена fingerprint");
            Reject(()=>ticket.RequireReady(context.Token,context.GeometryFingerprint,double.NaN),"неfinite ready clock");
            Require(!ticket.Consumed,"невалидный Ready потребил действующую reservation");
            ticket.Consume(context.Token,context.GeometryFingerprint,12);
            Reject(()=>ticket.Consume(context.Token,context.GeometryFingerprint,12),"повторный Ready");
            var expired=new EntryPreparationTicket(context,10);
            Reject(()=>expired.Consume(context.Token,context.GeometryFingerprint,100),"Ready в точный deadline");
            Require(!expired.Consumed && expired.Expired(100),"timeout подключил тело/потребил ticket");
            Reject(()=>new EntryPreparationTicket(context,double.PositiveInfinity),"неfinite reservation clock");
        }
        public static void CheckClientAdmissionLifecycle()
        {
            var source=Fixture();var context=Capture(source);var welcomed=source.Copy();
            ActorSessionRules.Connect(welcomed.Find(context.ActorId),"connection:lifecycle");
            string protectedSource=StateJson.ToJson(source),protectedWelcome=StateJson.ToJson(welcomed);
            foreach(string boundary in new[] {"ClientAdmitted","PreserveCharacterSource","QueueRecoveryReceipt","Changed"})
            {
                var state=new SessionEntryPreparation.ClientAdmissionState();state.Begin(context);
                state.MarkPrepared(context.Token);
                Require(ReferenceEquals(state.Take(welcomed,context.ActorId),context) && !state.Pending &&
                    state.ReadySent && ReferenceEquals(state.Current,context),"Take потерял cleanup context: "+boundary);
                bool cancelled=false;
                try {throw new IOException("Тестовый отказ на границе "+boundary);}
                catch(IOException) {cancelled=state.Reset();}
                Require(cancelled && state.Current==null && !state.Pending && !state.ReadySent && !state.Reset(),
                    "отказ Welcome не отменяет retained подготовку ровно один раз: "+boundary);
                Reject(state.CompleteAdmission,"Complete после failed Welcome: "+boundary);
            }
            var success=new SessionEntryPreparation.ClientAdmissionState();success.Begin(context);
            Reject(success.CompleteAdmission,"Complete до Ready");
            Require(success.MarkPrepared(context.Token) && !success.MarkPrepared(context.Token),"duplicate MarkPrepared изменил состояние");
            Reject(success.CompleteAdmission,"Complete до Take");
            var foreign=welcomed.Copy();foreign.worldId=Guid.NewGuid().ToString("N");
            Reject(()=>success.Take(foreign,context.ActorId),"Take другого мира");
            Require(success.Pending && success.ReadySent && success.Current==context,"неправильный Welcome уничтожил действующий контекст");
            success.Take(welcomed,context.ActorId);
            Reject(()=>success.Take(welcomed,context.ActorId),"второй Take");
            Reject(()=>success.Begin(context),"replacement во время незавершённого Welcome");
            success.CompleteAdmission();
            Require(success.Current==null && !success.Pending && !success.ReadySent && !success.Reset(),"успешный Welcome вызовет Cancelled при disconnect");
            Reject(success.CompleteAdmission,"повторный Complete");
            var replacement=new SessionEntryPreparation.ClientAdmissionState();replacement.Begin(context);replacement.MarkPrepared(context.Token);
            var next=Capture(source);replacement.BeginReceiving();replacement.Begin(next);
            Require(replacement.Pending && !replacement.ReadySent && replacement.Current==next,"replacement сохранил старый Ready");
            Reject(()=>replacement.MarkPrepared(context.Token),"старый token после replacement");
            Require(replacement.Reset(),"отмена replacement не очистит warm room");
            var partial=new SessionEntryPreparation.ClientAdmissionState();partial.BeginReceiving();
            Require(partial.Reset(),"отмена частичной передачи не очистит loading");
            Require(StateJson.ToJson(source)==protectedSource && StateJson.ToJson(welcomed)==protectedWelcome,
                "client lifecycle изменил тело/мир или последствия вместо своего состояния");
        }
        public static void CheckLatestBody()
        {
            var world=Fixture();var body=world.Find("player:entry");var profile=body.Copy();profile.health=100;
            var context=Capture(world);var ticket=new EntryPreparationTicket(context,0);
            var solo=world.Copy();solo.actors.RemoveAll(actor=>actor.id!=body.id);
            Require(!LevelActivity.HasConnectedPlayer(solo,body.level) && !PartyRules.ResolveDefeat(solo),"ожидающий игрок активировал frozen level/party defeat");
            var hit=new AttackEvent {id="entry:offline-hit",weapon="pistol",level=body.level};
            Require(new DamageReceiver(new FixedDamage(5)).Apply(body,hit,true) && body.health==1 && !body.down,"обычный offline damage не сохранился");
            Require(!body.connected && ActorSessionRules.IsPassive(body) && context.Matches(world,body.id,body.level) &&
                context.ViewWorld.Find(body.id).health==6,"HP/receipt заставляет retry или preview не отделён");
            ticket.Consume(context.Token,context.GeometryFingerprint,1);
            var actual=PlayerWorldEntry.Reconcile(world,body,profile,out string notice);
            int generation=actual.generation,lifecycle=actual.lifecycleSequence;
            var position=actual.Position;string inventory=StateJson.ToJson(actual.inventory);
            ActorSessionRules.Connect(actual,"connection:prepared");context.RequireWelcome(world,actual.id);
            Require(ReferenceEquals(actual,body) && actual.health==1 && actual.resolvedAttacks.Contains(hit.id) &&
                actual.generation==generation+1 && actual.lifecycleSequence==lifecycle+1 && actual.Position==position &&
                StateJson.ToJson(actual.inventory)==inventory && actual.extensions["body-mod"].Value<string>()=="последствия" && notice!=null,
                "Connect вернул preview/profile вместо актуального тела или потерял HP/inventory/unknown");
            var waiting=world.Find("player:ally");ActorSessionRules.Disconnect(waiting);waiting.down=true;waiting.health=0;
            Require(!PartyRules.ResolveDefeat(world) && waiting.level=="factory:0" && waiting.health==0,
                "ожидающая/отключённая часть партии получила recovery penalty");
            var terminal=world.Copy();terminal.Find(actual.id).down=true;terminal.Find(actual.id).health=0;
            Require(PartyRules.ResolveDefeat(terminal) && terminal.Find(actual.id).level=="hub" &&
                terminal.Find(actual.id).health==1 && terminal.Find(waiting.id).level=="factory:0" && terminal.Find(waiting.id).health==0,
                "готовый incapacitated игрок не восстановлен или pending body ошибочно включён в поражение");
        }
        public static void CheckGeometryRefresh()
        {
            var world=Fixture();var context=Capture(world);
            world.Find("player:entry").x+=.25f;world.Find("player:entry").lifecycleSequence++;
            world.revision++;world.checkpointSequence++;
            world.doors.Single(door=>door.level=="hub").open=true;
            Require(context.Matches(world,context.ActorId,context.Level),"движение/epoch/другой level door инвалидировали свет");
            world.doors.Single(door=>door.level==context.Level).open=true;
            Require(!context.Matches(world,context.ActorId,context.Level),"старый Ready принят после изменения blocking door");
            var fresh=Capture(world);Require(fresh.Token!=context.Token && fresh.GeometryFingerprint!=context.GeometryFingerprint,
                "новый door context сохранил старый token/fingerprint");
            ActorSessionRules.Connect(world.Find(context.ActorId),"connection:new");
            Reject(()=>context.RequireWelcome(world,context.ActorId),"Welcome принял stale door geometry");fresh.RequireWelcome(world,context.ActorId);
            var changed=world.Copy();changed.rooms[0].extensions["modPayload"]["precision"]=2;
            Require(!fresh.Matches(changed,context.ActorId,context.Level),"unknown room binding не входит в identity");
            changed=world.Copy();changed.rooms[0].definition="mod:replacement";
            Require(!fresh.Matches(changed,context.ActorId,context.Level),"другое room definition");
            changed=world.Copy();changed.worldId=Guid.NewGuid().ToString("N");
            Require(!fresh.Matches(changed,context.ActorId,context.Level) && !fresh.Matches(world,context.ActorId,"hub") &&
                !fresh.Matches(world,"other:actor",context.Level),"другой world/level/body");
            changed=world.Copy();changed.doors[0].width=float.NaN;
            Reject(()=>EntryPreparationContext.Fingerprint(changed,context.Level),"неfinite door");
            changed=world.Copy();changed.doors.Add(StateJson.Copy(changed.doors[0]));
            Reject(()=>EntryPreparationContext.Fingerprint(changed,context.Level),"повтор ID двери");
        }
        public static void CheckTransfer()
        {
            var source=Fixture();source.extensions["large-known-context"]=new string('x',SnapshotTransfer.ChunkSize*3);
            var context=Capture(source);var snapshot=RecoverySnapshot.FromWorld(context.ViewWorld);
            byte[] bytes=Encoding.UTF8.GetBytes(snapshot.Json);var transfer=new SnapshotTransfer(snapshot.Id);
            RecoverySnapshot received=null;int chunks=0;
            for(int offset=0;offset<bytes.Length;offset+=SnapshotTransfer.ChunkSize)
            {
                byte[] chunk=bytes.Skip(offset).Take(SnapshotTransfer.ChunkSize).ToArray();
                received=transfer.Append(bytes.Length,offset,chunk);chunks++;
                if(offset+chunk.Length<bytes.Length) Require(received==null,"частичный view стал доступным");
            }
            Require(chunks>3 && received.Json==snapshot.Json,"chunks потеряли точные immutable bytes");
            var decoded=new EntryPreparationContext(context.Token,received.CreateLiveWorld(),context.ActorId,context.CharacterId,context.Level);
            Require(decoded.GeometryFingerprint==context.GeometryFingerprint && decoded.RoomDefinitionsJson==context.RoomDefinitionsJson,
                "strict chunk transport изменил room/door identity");
            Reject(()=>new SnapshotTransfer(snapshot.Id).Append(SnapshotTransfer.MaximumBytes+1,0,new byte[1]),"oversized view");
            Reject(()=>new SnapshotTransfer(snapshot.Id).Append(bytes.Length,1,new byte[1]),"out-of-order view");
            var corrupt=new SnapshotTransfer(new string('0',64));
            Reject(()=>corrupt.Append(2,0,Encoding.UTF8.GetBytes("{}")),"corrupt world view");
        }
        private static EntryPreparationContext Capture(WorldState world)
        {
            var copy=RecoverySnapshot.FromWorld(world).CreateLiveWorld();
            return new EntryPreparationContext(Guid.NewGuid().ToString("N"),EntryPreparationContext.NarrowView(copy,"player:entry","factory:0"),
                "player:entry","entry","factory:0");
        }
        private static WorldState Fixture()
        {
            var world=new WorldState {revision=7,checkpointSequence=5};
            world.extensions=new Dictionary<string,JToken> {{"world-mod","не терять"}};
            world.rooms.Add(new WorldRoom {level="factory:0",definition="base:factory-prototype",extensions=new Dictionary<string,JToken> {
                {"modPayload",new JObject {{"precision",123456789.0123456789m}}}}});
            world.rooms.Add(new WorldRoom {level="hub",definition="base:hub"});
            world.actors.Add(new ActorState {id="player:entry",character="entry",player=true,health=6,level="factory:0",x=7,y=4,
                generation=3,lifecycleSequence=8,controlStateVersion=1,inventory=new() {new ItemStack {id="entry:item",definition="p10",count=2}},
                extensions=new Dictionary<string,JToken> {{"body-mod","последствия"}}});
            world.actors.Add(new ActorState {id="player:ally",character="ally",player=true,connected=true,health=100,level="factory:0"});
            world.doors.Add(new DoorState {id="door:entry",definition="test:door",surfaceTexture="steel",level="factory:0",x=10,y=4,width=.8f,height=1.6f});
            world.doors.Add(new DoorState {id="door:hub",definition="test:door",surfaceTexture="steel",level="hub",x=1,y=0,width=.8f,height=1.6f});
            world.containers.Add(new ContainerState {id="container:unrelated",level="hub",items=new() {new ItemStack {id="unrelated:item",definition="p10",count=4}}});
            world.completedTransactions.Add("receipt:unrelated");return world;
        }
        private sealed class FixedDamage : IDamageRule
        {
            private readonly float amount;
            public FixedDamage(float amount) {this.amount=amount;}
            public float Calculate(string weapon,float speed)=>amount;
        }
        private static void Reject(Action action,string reason)
        {
            bool rejected=false;try {action();} catch(Exception error) when(error is InvalidDataException || error is InvalidOperationException || error is ArgumentException) {rejected=true;}
            Require(rejected,"не отклонено: "+reason);
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Подготовка входа: "+reason);}
    }
}
