using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Remains.Domain;
using Remains.Networking;
using Remains.Persistence;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class WorldReplicationChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            var host=new WorldState();
            host.actors.Add(new ActorState {id="replication:a",health=100});
            host.actors.Add(new ActorState {id="replication:b",health=100});
            host.containers.Add(new ContainerState {id="replication:container"});
            host.props.Add(new PropState {id="replication:prop"});
            var a=RecoverySnapshot.FromWorld(host);var client=a.CreateLiveWorld();
            host.actors[0].x=1;host.revision++;
            var b=RecoverySnapshot.FromWorld(host);var ab=Delta(a,b);
            host.containers[0].x=2;host.props[0].x=3;host.revision++;
            var c=RecoverySnapshot.FromWorld(host);var bc=Delta(b,c);

            var receiver=new WorldReplication();Full(receiver,a);
            var rejected=receiver.ReceiveAndPublish(ab,()=>client,_=>throw new InvalidOperationException("Проверка отказа согласования инвентаря."));
            Require(rejected?.resync==true,"Ошибка публикации не запрашивает resync.");
            Require(client.actors[0].x==0,"Неопубликованный снимок изменил старый live graph.");
            Require(receiver.Receive(bc,()=>client)?.resync==true,"После ошибки публикации осталась продвинутая база.");
            Full(receiver,b);client=b.CreateLiveWorld();
            var accepted=receiver.ReceiveAndPublish(bc,()=>client,received=>client=received.live);
            Require(accepted!=null && !accepted.resync && accepted.snapshot.Id==c.Id,"Полный resync не восстановил следующий приём.");
            Require(!accepted.changes.Any(WorldChangeKind.Actors),"Body-only дельта объявляет замену актёров.");
            Require(ReferenceEquals(accepted.findBody(client,"containers:replication:container"),client.containers[0]),"Изменённый контейнер не найден по канонической позиции.");
            client.props.Insert(0,new PropState {id="local:inserted"});
            Require(accepted.findBody(client,"props:replication:prop")?.id=="replication:prop","Локальная перестановка сломала поиск физического тела.");

            // An unexpected publisher bug also clears the base before it propagates loudly.
            Full(receiver,a);client=a.CreateLiveWorld();bool failed=false;
            try {receiver.ReceiveAndPublish(ab,()=>client,_=>throw new ArgumentException("Проверка неожиданной ошибки публикации."));}
            catch(ArgumentException) {failed=true;}
            Require(failed && receiver.Receive(bc,()=>client)?.resync==true,"Неожиданная ошибка оставила допустимую базу.");
            bool admissionFailed=false;
            try {Full(receiver,a,_=>throw new InvalidOperationException("Проверка отказа входа."),"server-owner");}
            catch(InvalidOperationException error) when(error.Message=="Проверка отказа входа.") {admissionFailed=true;}
            Require(admissionFailed && receiver.Receive(ab,()=>client)?.resync==true,"Отказ Welcome не прервал вход или оставил базу.");

            // Every framing error clears both the incomplete transfer and the replica; a fresh full can follow.
            byte[] bytes=a.Utf8;int half=Math.Min(128,bytes.Length-1);
            var first=new byte[half];Buffer.BlockCopy(bytes,0,first,0,half);
            foreach(int kind in new[]{0,1,2,3,4})
            {
                receiver.Forget();Full(receiver,a);
                Require(receiver.Receive(new WorldPartMessage {snapshot=a.Id,total=bytes.Length,offset=0,data=first},()=>client)==null,"Неполная часть опубликована.");
                var wrong=new WorldPartMessage {snapshot=a.Id,total=bytes.Length,offset=half+1,data=new byte[]{1}};
                if(kind==1) wrong.snapshot="other";
                if(kind==2) {wrong.offset=half-1;wrong.data=new byte[]{1,2};}
                if(kind==3) {wrong.offset=0;wrong.total=bytes.Length+1;}
                if(kind==4) {wrong.offset=0;wrong.data=(byte[])first.Clone();wrong.data[0]^=1;}
                Require(receiver.Receive(wrong,()=>client)?.resync==true,"Повреждённая часть не запросила resync: "+kind);
                Require(receiver.Receive(ab,()=>client)?.resync==true,"Ошибка framing сохранила старую базу: "+kind);
                Full(receiver,a);
                Require(receiver.Receive(ab,()=>a.CreateLiveWorld())?.snapshot.Id==b.Id,"Ошибка framing заблокировала новый полный снимок: "+kind);
            }
            receiver.Forget();
            var invalid=new WorldPartMessage {snapshot=a.Id,total=2,offset=0,data=new byte[]{0xff,0xff}};
            Require(receiver.Receive(invalid,()=>client)?.resync==true,"Неверный UTF-8 не сбросил приём.");
            Full(receiver,a);
            Debug.Log("Приём снимков: PASS — отказ публикации/неожиданная ошибка сбрасывают базу, framing/UTF-8 восстанавливаются полным снимком, body-only delta и адресный поиск тел.");
        }
        private static WorldPartMessage Delta(RecoverySnapshot before,RecoverySnapshot after)
        {
            var delta=WorldDelta.Build(after.WorldId,before.ReplicationId,before.Composition,after.ReplicationId,after.Composition);
            byte[] bytes=Encoding.UTF8.GetBytes(StateJson.ToJson(delta));
            return new WorldPartMessage {snapshot=after.ReplicationId,baseSnapshot=before.ReplicationId,total=bytes.Length,data=bytes};
        }
        private static void Full(WorldReplication receiver,RecoverySnapshot snapshot,Action<ReceivedWorld> publish=null,string owner=null)
        {
            byte[] bytes=PayloadCodec.Pack(snapshot.Utf8);ReceivedWorld result=null;   // as WorldReplication.SendParts sends it
            for(int offset=0;offset<bytes.Length;offset+=SnapshotTransfer.ChunkSize)
            {
                var data=new byte[Math.Min(SnapshotTransfer.ChunkSize,bytes.Length-offset)];Buffer.BlockCopy(bytes,offset,data,0,data.Length);
                var message=new WorldPartMessage {snapshot=snapshot.Id,total=bytes.Length,offset=offset,data=data,owner=offset==0?owner:null,actor=offset==0 && owner!=null?"server-actor":null};
                result=publish==null?receiver.Receive(message,()=>null):receiver.ReceiveAndPublish(message,()=>null,publish);
            }
            Require(result!=null && !result.resync && result.snapshot.Id==snapshot.Id,"Полный снимок не принят.");
        }
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
