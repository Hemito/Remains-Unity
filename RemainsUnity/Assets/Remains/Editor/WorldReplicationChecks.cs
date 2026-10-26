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
            host.containers.Add(new ContainerState {id="replication:container"});
            var a=RecoverySnapshot.FromWorld(host);
            host.actors[0].x=1;host.revision++;
            var b=RecoverySnapshot.FromWorld(host);

            // A failed ordinary publication asks for a resync and leaves no partial transfer; the next full world is accepted.
            var receiver=new WorldReplication();
            Require(Full(receiver,a,_=>throw new InvalidOperationException("Проверка отказа согласования инвентаря."))?.resync==true,
                "Ошибка публикации не запрашивает resync.");
            Require(Full(receiver,b)?.snapshot.Id==b.Id,"После ошибки публикации полный снимок не принят.");

            // An unexpected publisher bug propagates loudly; a failed admission (Welcome) stops the session.
            bool failed=false;
            try {Full(receiver,a,_=>throw new ArgumentException("Проверка неожиданной ошибки публикации."));}
            catch(ArgumentException) {failed=true;}
            Require(failed && Full(receiver,b)?.snapshot.Id==b.Id,"Неожиданная ошибка публикации не распространилась или сломала приём.");
            bool admissionFailed=false;
            try {Full(receiver,a,_=>throw new InvalidOperationException("Проверка отказа входа."),"server-owner");}
            catch(InvalidOperationException error) when(error.Message=="Проверка отказа входа.") {admissionFailed=true;}
            Require(admissionFailed,"Отказ Welcome не прервал вход.");

            // Every framing error clears the incomplete transfer; a fresh full can follow.
            byte[] bytes=PayloadCodec.Pack(a.Utf8);int half=Math.Min(128,bytes.Length-1);
            var first=new byte[half];Buffer.BlockCopy(bytes,0,first,0,half);
            foreach(int kind in new[]{0,1,2,3,4})
            {
                receiver.Forget();
                Require(receiver.Receive(new WorldPartMessage {snapshot=a.Id,total=bytes.Length,offset=0,data=first})==null,"Неполная часть опубликована.");
                var wrong=new WorldPartMessage {snapshot=a.Id,total=bytes.Length,offset=half+1,data=new byte[]{1}};
                if(kind==1) wrong.snapshot="other";
                if(kind==2) {wrong.offset=half-1;wrong.data=new byte[]{1,2};}
                if(kind==3) {wrong.offset=0;wrong.total=bytes.Length+1;}
                if(kind==4) {wrong.offset=0;wrong.data=(byte[])first.Clone();wrong.data[0]^=1;}
                Require(receiver.Receive(wrong)?.resync==true,"Повреждённая часть не запросила resync: "+kind);
                Require(Full(receiver,b)?.snapshot.Id==b.Id,"Ошибка framing заблокировала новый полный снимок: "+kind);
            }
            receiver.Forget();
            var invalid=new WorldPartMessage {snapshot=a.Id,total=2,offset=0,data=new byte[]{0xff,0xff}};
            Require(receiver.Receive(invalid)?.resync==true,"Неверные данные не сбросили приём.");
            Require(Full(receiver,a)?.snapshot.Id==a.Id,"После неверных данных полный снимок не принят.");
            Debug.Log("Приём снимков: PASS — отказ публикации запрашивает resync, неожиданная ошибка и отказ входа видимы, framing/данные восстанавливаются полным снимком.");
        }
        private static ReceivedWorld Full(WorldReplication receiver,RecoverySnapshot snapshot,Action<ReceivedWorld> publish=null,string owner=null)
        {
            byte[] bytes=PayloadCodec.Pack(snapshot.Utf8);ReceivedWorld result=null;   // as WorldReplication.SendParts sends it
            for(int offset=0;offset<bytes.Length;offset+=SnapshotTransfer.ChunkSize)
            {
                var data=new byte[Math.Min(SnapshotTransfer.ChunkSize,bytes.Length-offset)];Buffer.BlockCopy(bytes,offset,data,0,data.Length);
                var message=new WorldPartMessage {snapshot=snapshot.Id,total=bytes.Length,offset=offset,data=data,owner=offset==0?owner:null,actor=offset==0 && owner!=null?"server-actor":null};
                result=publish==null?receiver.Receive(message):receiver.ReceiveAndPublish(message,publish);
            }
            return result;
        }
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
