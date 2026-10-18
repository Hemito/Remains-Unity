using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
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
            string input=null;var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++) if(args[i]=="-recoveryPerfWorld") input=args[i+1];
            if(input==null) throw new ArgumentException("Нужен -recoveryPerfWorld <мир.json>.");
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
            var delta=WorldDelta.Build(host.worldId,a.Id,a.Composition,b.Id,b.Composition);
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
            var delta2=WorldDelta.Build(host.worldId,b.Id,b.Composition,b2.Id,b2.Composition);
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
                    var actorDelta=WorldDelta.Build(host.worldId,baseSnapshot.Id,baseSnapshot.Composition,next.Id,next.Composition);
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
            var orderDelta=WorldDelta.Build(host.worldId,lastBase.Id,lastBase.Composition,c.Id,c.Composition);
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

            Debug.Log($"Изменения мира: PASS — A→B частей {changed}, {encoded.Length} символов вместо {b.Json.Length} " +
                $"({100.0*encoded.Length/b.Json.Length:F1}%), сборка у хоста {composeMs:F1} мс, применение {applyMs:F1} мс, следующее {apply2Ms:F1} мс, сдвиг персонажа {actorBytes} символов; смена состава, отказ чужой базе.");
        }

        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
