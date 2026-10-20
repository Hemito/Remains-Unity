using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Networking;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Delta replication (host-migration.md, stage 2) on a real factory world: a replica built from a full snapshot
    // applies host deltas to exactly the host's next text (same snapshot ID), strictly parses only changed parts and
    // reuses unchanged live objects; list order changes (added/removed container) apply; a delta from another base
    // is rejected.
    //   batch -executeMethod Remains.Editor.WorldDeltaChecks.RunBatch -recoveryPerfWorld <world.json>
    public static class WorldDeltaChecks
    {
        public static void RunBatch()
        {
            try {ValidatedStateCopyChecks.Run();Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            CheckDeltaTransport();
            string input=null;var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++) if(args[i]=="-recoveryPerfWorld") input=args[i+1];
            if(input==null) {Debug.Log("Изменения мира: транспорт проверен; fixture-проверка требует -recoveryPerfWorld <мир.json>.");return;}
            Anatomy.Load();WeaponCatalog.Load();
            var host=StateJson.FromJson<WorldState>(File.ReadAllText(input));
            Require(host.containers.Count>2,"В мире мало контейнеров.");

            WorldChanges.Track(host);
            // Host snapshot A, client replica and live world from the full A.
            var a=RecoverySnapshot.FromWorld(host);
            var replica=WorldReplica.FromSnapshot(a);
            var client=a.CreateLiveWorld();

            // B: one container moved (marked), one lamp broken (marked).
            var moved=host.containers[0];moved.x+=1;WorldChanges.Mark(host,moved);
            if(host.lamps.Count>0) {host.lamps[0].broken=!host.lamps[0].broken;WorldChanges.Mark(host,host.lamps[0]);}
            int expected=1+(host.lamps.Count>0?1:0);
            if(host.doors.Count>0) {host.doors[0].open=!host.doors[0].open;host.doors[0].revision++;WorldChanges.Mark(host,host.doors[0]);expected++;}
            if(host.actors.Count>0) {host.actors[0].x+=.5f;WorldChanges.Mark(host,host.actors[0]);expected++;}
            host.revision++;
            var composeTimer=System.Diagnostics.Stopwatch.StartNew();
            var b=RecoverySnapshot.FromWorld(host);double composeMs=composeTimer.Elapsed.TotalMilliseconds;
            var delta=BuildDelta(a,b);
            int changed=WorldDelta.ChangedParts(delta);
            Require(changed==expected,$"Изменение A→B: частей {changed}, ожидалось {expected}.");
            string encoded=StateJson.ToJson(delta);
            var timer=System.Diagnostics.Stopwatch.StartNew();
            var applied=replica.Apply(StateJson.FromJson<WorldDeltaPayload>(encoded),client);
            double applyMs=timer.Elapsed.TotalMilliseconds;
            Require(applied.Id==b.Id && applied.Json==b.Json,"Клиент собрал другой текст B.");
            var nextClient=applied.CreateLiveWorld();
            Require(ReferenceEquals(nextClient.containers[1],client.containers[1]),"Неизменный контейнер не переиспользован.");
            Require(!ReferenceEquals(nextClient.containers[0],client.containers[0]) && Mathf.Approximately(nextClient.containers[0].x,moved.x),
                "Изменённый контейнер не разобран заново.");

            // B2: the next change on the applied world (the replica's positional reuse of its own objects).
            if(nextClient.actors.Count>0)
            {
                nextClient.actors[0].x+=123;
                nextClient.actors[0].inventory.Clear();
                nextClient.actors[0].perception.remainingSeconds=123;
            }
            var second=host.containers[3];second.x+=1;WorldChanges.Mark(host,second);host.revision++;
            var b2=RecoverySnapshot.FromWorld(host);
            var delta2=BuildDelta(b,b2);
            timer.Restart();
            RecoverySnapshot actorStep=null;int actorBytes=0;
            var applied2=replica.Apply(StateJson.FromJson<WorldDeltaPayload>(StateJson.ToJson(delta2)),nextClient);
            double apply2Ms=timer.Elapsed.TotalMilliseconds;
            Require(applied2.Id==b2.Id && applied2.Json==b2.Json,"Клиент собрал другой текст B2.");
            var client2=applied2.CreateLiveWorld();
            if(client2.actors.Count>0)
            {
                Require(StateJson.ToJson(client2.actors[0])==StateJson.ToJson(host.actors[0]),"Локальные изменения повредили канонический кеш актёров.");
                Require(!ReferenceEquals(client2.actors[0],nextClient.actors[0]) &&
                    !ReferenceEquals(client2.actors[0].inventory,nextClient.actors[0].inventory),"Живые актёры разделяют изменяемые данные между снимками.");
            }
            Require(ReferenceEquals(client2.containers[4],nextClient.containers[4]) && ReferenceEquals(client2.props.Count>0?client2.props[0]:null,nextClient.props.Count>0?nextClient.props[0]:null),
                "Повторное изменение: неизменные объекты не переиспользованы по позиции.");
            Require(!ReferenceEquals(client2.containers[3],nextClient.containers[3]) && Mathf.Approximately(client2.containers[3].x,second.x),
                "Повторное изменение: изменённый контейнер не разобран заново.");
            nextClient=client2;

            // B3/B4: an actor known to the base travels as its template plus only the changed cold texts.
            if(host.actors.Count>0)
            {
                var mover=host.actors[0];int coldIndex=-1;
                for(int round=0;round<2;round++)
                {
                    if(round==0) mover.x+=.25f;
                    else {mover.inventory.Add(new ItemStack {id="check:delta-item",definition="caps",count=1});coldIndex=2;}
                    WorldChanges.Mark(host,mover);
                    host.revision++;
                    var next=RecoverySnapshot.FromWorld(host);
                    var baseSnapshot=round==0?b2:actorStep;
                    var actorDelta=BuildDelta(baseSnapshot,next);
                    var list=actorDelta.lists.Find(entry=>entry.name=="actors");
                    int at=Array.IndexOf(list.keys,"actors:"+mover.id);
                    Require(at>=0 && list.templates!=null && list.templates[at]!=null && list.texts[at]==null,"Персонаж передан целым текстом, а не шаблоном.");
                    for(int part=0;part<list.cold[at].Length;part++)
                        Require((list.cold[at][part]!=null)==(part==coldIndex),$"Холодная часть {ActorText.Cold[part]}: передана={list.cold[at][part]!=null}, ожидалось {part==coldIndex}.");
                    var appliedActor=replica.Apply(StateJson.FromJson<WorldDeltaPayload>(StateJson.ToJson(actorDelta)),nextClient);
                    Require(appliedActor.Id==next.Id && appliedActor.Json==next.Json,"Клиент собрал другой текст после изменения персонажа.");
                    nextClient=appliedActor.CreateLiveWorld();actorStep=next;applied2=appliedActor;
                    if(round==0) actorBytes=StateJson.ToJson(actorDelta).Length;
                }
                mover.inventory.RemoveAll(item=>item.id=="check:delta-item");
                WorldChanges.Mark(host,mover);
            }
            var lastBase=host.actors.Count>0?actorStep:b2;

            // C: a container added and another removed (order changes).
            var added=StateJson.Copy(host.containers[2]);added.id="check:delta-added";host.containers.Add(added);
            host.containers.RemoveAt(1);host.revision++;
            var c=RecoverySnapshot.FromWorld(host);
            var orderDelta=BuildDelta(lastBase,c);
            Require(orderDelta.lists.Any(list=>list.name=="containers" && list.order!=null),"Смена состава контейнеров без порядка.");
            var appliedC=replica.Apply(StateJson.FromJson<WorldDeltaPayload>(StateJson.ToJson(orderDelta)),nextClient);
            Require(appliedC.Id==c.Id && appliedC.Json==c.Json,"Клиент собрал другой текст C после смены состава.");
            var clientC=appliedC.CreateLiveWorld();
            Require(clientC.containers.Count==host.containers.Count && clientC.containers.Any(x=>x.id==added.id),"Состав контейнеров клиента не совпал.");

            // A delta from another base is rejected (the client then asks for a full snapshot).
            bool rejected=false;
            try {replica.Apply(StateJson.FromJson<WorldDeltaPayload>(encoded),clientC);}
            catch(InvalidDataException) {rejected=true;}
            Require(rejected,"Изменение от чужой базы применено.");
            RunLiveChecks(host);

            Debug.Log($"Изменения мира: PASS — A→B частей {changed}, {encoded.Length} символов вместо {b.Json.Length} " +
                $"({100.0*encoded.Length/b.Json.Length:F1}%), сборка у хоста {composeMs:F1} мс, применение {applyMs:F1} мс, следующее {apply2Ms:F1} мс, сдвиг персонажа {actorBytes} символов; смена состава, отказ чужой базе.");
        }

        private static void CheckDeltaTransport()
        {
            typeof(CoopSession).Assembly.GetType("Mirror.GeneratedNetworkCode").GetMethod("InitReadWriters").Invoke(null,null);
            var payload=new byte[100*1024];
            for(int i=0;i<payload.Length;i++) payload[i]=(byte)(i*31+i/251);
            string baseId=new string('a',64),id=new string('b',64);
            foreach(int budget in new[]{256*1024,1024})
            {
                var parts=new List<WorldPartMessage>();
                WorldReplication.SendDeltaParts(baseId,id,payload,budget,parts.Add);
                Require(budget==1024?parts.Count>1:parts.Count==1,"Число сообщений не соответствует бюджету дельты.");
                var receiver=new SnapshotTransfer(id,SnapshotTransfer.MaximumChunkBytes);byte[] assembled=null;
                foreach(var part in parts)
                {
                    using var writer=NetworkWriterPool.Get();writer.Write(part);
                    Require(writer.Position<=budget,"Сериализованное Mirror-сообщение превышает бюджет: "+writer.Position+"/"+budget);
                    Require(part.snapshot==id && part.baseSnapshot==baseId && part.total==payload.Length,"Потеряна идентичность транспорта дельты.");
                    assembled=receiver.AppendRaw(part.total,part.offset,part.data);
                }
                Require(assembled!=null && assembled.SequenceEqual(payload) && receiver.ReceivedBytes==payload.Length,"Сборка транспорта изменила байты дельты.");
                if(budget==1024)
                {
                    var first=parts[0];var next=parts[1];var last=parts[parts.Count-1];
                    var ordered=new SnapshotTransfer(id,SnapshotTransfer.MaximumChunkBytes);
                    Reject(()=>ordered.AppendRaw(next.total,next.offset,next.data),"Пропуск первой части принят.");
                    Require(ordered.ReceivedBytes==0,"Неверный порядок продвинул позицию приёмника.");
                    ordered.AppendRaw(first.total,first.offset,first.data);
                    Require(ordered.AppendRaw(first.total,first.offset,first.data)==null && ordered.ReceivedBytes==first.data.Length,"Идентичный повтор изменил позицию.");
                    var corrupt=(byte[])first.data.Clone();corrupt[0]^=1;
                    Reject(()=>ordered.AppendRaw(first.total,first.offset,corrupt),"Повреждённый повтор принят.");
                    Reject(()=>ordered.AppendRaw(next.total+1,next.offset,next.data),"Подмена полного размера принята.");
                    Reject(()=>ordered.AppendRaw(first.total,first.data.Length-1,next.data),"Перекрытие принято.");
                    Reject(()=>ordered.AppendRaw(SnapshotTransfer.MaximumBytes+1,0,first.data),"Превышение размера снимка принято.");
                    Reject(()=>ordered.AppendRaw(SnapshotTransfer.MaximumChunkBytes+1,0,new byte[SnapshotTransfer.MaximumChunkBytes+1]),"Превышение размера части принято.");
                    Reject(()=>ordered.AppendRaw(last.total,last.offset,last.data),"Пропуск промежуточных частей принят.");
                    Require(ordered.ReceivedBytes==first.data.Length,"Отказ повреждённым частям изменил позицию.");
                    byte[] completed=null;
                    foreach(var part in parts.Skip(1)) completed=ordered.AppendRaw(part.total,part.offset,part.data);
                    Require(completed!=null && completed.SequenceEqual(payload),"Отказ повреждённым частям испортил последующую сборку.");
                }
                else Reject(()=>new SnapshotTransfer(id).AppendRaw(parts[0].total,0,parts[0].data),"Приёмник полного снимка принял часть больше 8192 байт.");
            }
            Reject(()=>WorldReplication.SendDeltaParts(baseId,id,payload,1,_=>throw new InvalidOperationException("Невместимый заголовок был отправлен.")),
                "Бюджет меньше заголовка принят.");
            Debug.Log("Транспорт дельты: PASS — 100 КиБ одним сообщением, бюджет Mirror, части 1024, точные байты, порядок, повторы, повреждения и пределы.");
        }
        private static void Reject(Action action,string message)
        {
            bool rejected=false;try {action();} catch(InvalidDataException) {rejected=true;}
            Require(rejected,message);
        }

        private static WorldDeltaPayload BuildDelta(RecoverySnapshot before,RecoverySnapshot after)
            =>WorldDelta.Build(after.WorldId,before.ReplicationId,before.Composition,after.ReplicationId,after.Composition);
        private static WorldDeltaPayload Wire(WorldDeltaPayload delta)=>StateJson.FromJson<WorldDeltaPayload>(StateJson.ToJson(delta));

        // The production opt-in preserves runtime projections but keeps canonical/recovery graphs independent.
        private static void RunLiveChecks(WorldState fixture)
        {
            var host=fixture.Copy();Require(host.actors.Count>1,"Для проверки live-реплики нужны два актёра.");
            WorldChanges.Track(host);
            var a=RecoverySnapshot.FromWorld(host);var replica=WorldReplica.FromSnapshot(a);var current=a.CreateLiveWorld();
            var mover=host.actors[0];var stable=current.actors[1];
            stable.x+=123;stable.perception.remainingSeconds=123;
            stable.inventory.Add(new ItemStack {id="check:local-only",definition="caps",count=1});
            mover.x+=.25f;WorldChanges.Mark(host,mover);host.revision++;
            var b=RecoverySnapshot.FromWorld(host);var delta=BuildDelta(a,b);
            string before=StateJson.ToJson(current),canonicalBefore=StateJson.ToJson(replica.CopyCanonicalActor(mover.id));
            string baseId=replica.SnapshotId;
            var corrupt=Wire(delta);corrupt.snapshot=new string('0',64);
            bool rejected=false;
            try {replica.ApplyLive(corrupt,current,out _,out _);} catch(InvalidDataException) {rejected=true;}
            Require(rejected && replica.SnapshotId==baseId && StateJson.ToJson(current)==before &&
                StateJson.ToJson(replica.CopyCanonicalActor(mover.id))==canonicalBefore,"Повреждённая live-дельта изменила базу, canonical или живой мир.");
            var snapshot=replica.ApplyLive(Wire(delta),current,out var live,out var reused);
            Require(reused.Contains(stable.id) && ReferenceEquals(live.actors[1],stable) && stable.perception.remainingSeconds==123 &&
                stable.inventory.Any(item=>item.id=="check:local-only"),"Неизменённая live-проекция потеряла ссылку или локальное состояние.");
            Require(!reused.Contains(mover.id) && !ReferenceEquals(live.actors[0],current.actors[0]) && live.actors[0].x==mover.x,
                "Изменённый live-актёр не скопирован из canonical.");
            Require(snapshot.Json==b.Json && StateJson.ToJson(snapshot.CreateLiveWorld())==b.Json,"Live-проекция попала в восстановительный снимок.");
            live.actors[0].inventory.Clear();live.actors[0].inventoryAliases["check:alias"]="check:target";
            live.actors[0].perception.remainingSeconds=999;
            Require(StateJson.ToJson(replica.CopyCanonicalActor(mover.id))==StateJson.ToJson(mover),"Live-изменение загрязнило canonical, включая неизменённые cold-поля.");
            var detached=replica.CopyCanonicalActor(mover.id);detached.inventory.Clear();detached.x=999;
            Require(StateJson.ToJson(replica.CopyCanonicalActor(mover.id))==StateJson.ToJson(mover),"CopyCanonicalActor выдал внутренний граф.");
            mover.inventory.Add(new ItemStack {id="check:changed-cold",definition="caps",count=2});
            WorldChanges.Mark(host,mover);host.revision++;var c=RecoverySnapshot.FromWorld(host);
            var snapshotC=replica.ApplyLive(Wire(BuildDelta(b,c)),live,out var liveC,out var reusedC);
            Require(reusedC.Contains(stable.id) && ReferenceEquals(liveC.actors[1],stable) &&
                StateJson.ToJson(liveC.actors[0].inventory)==StateJson.ToJson(mover.inventory),"Разреженная cold-дельта повредила inventory или unchanged-ссылку.");
            liveC.actors[0].inventory.Clear();
            Require(StateJson.ToJson(snapshotC.CreateLiveWorld())==c.Json && StateJson.ToJson(snapshot.CreateLiveWorld())==b.Json,
                "Последующая live-мутация загрязнила текущий или предыдущий detached recovery.");
            // Same ID is insufficient after a lifetime change; missing actors must also be reconstructed.
            stable.generation++;liveC.actors.RemoveAt(0);host.revision++;var d=RecoverySnapshot.FromWorld(host);
            replica.ApplyLive(Wire(BuildDelta(c,d)),liveC,out var liveD,out var reusedD);
            Require(!reusedD.Contains(stable.id) && !ReferenceEquals(liveD.actors[1],stable) &&
                StateJson.ToJson(liveD.actors[0])==StateJson.ToJson(host.actors[0]),"Несовместимый lifetime или отсутствующий live-актёр переиспользован.");
            Debug.Log("Live-реплика: PASS — разреженные ссылки, hot/cold изоляция, detached recovery, rollback и lifetime.");
        }

        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
