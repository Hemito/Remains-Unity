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
            try {Run();EditorApplication.Exit(0);}
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

            // Host snapshot A, client replica and live world from the full A.
            var a=RecoverySnapshot.FromWorld(host);
            var replica=WorldReplica.FromSnapshot(a);
            var client=a.CreateLiveWorld();

            // B: one container moved (marked), one lamp broken (marked).
            var moved=host.containers[0];moved.x+=1;WorldChanges.Mark(host,moved);
            if(host.lamps.Count>0) {host.lamps[0].broken=!host.lamps[0].broken;WorldChanges.Mark(host,host.lamps[0]);}
            host.revision++;
            var b=RecoverySnapshot.FromWorld(host);
            var delta=WorldDelta.Build(host.worldId,a.Id,a.Composition,b.Id,b.Composition);
            int changed=WorldDelta.ChangedParts(delta);
            Require(changed==(host.lamps.Count>0?2:1),$"Изменение A→B: частей {changed}.");
            string encoded=StateJson.ToJson(delta);
            var timer=System.Diagnostics.Stopwatch.StartNew();
            var applied=replica.Apply(StateJson.FromJson<WorldDeltaPayload>(encoded),client);
            double applyMs=timer.Elapsed.TotalMilliseconds;
            Require(applied.Id==b.Id && applied.Json==b.Json,"Клиент собрал другой текст B.");
            var nextClient=applied.CreateLiveWorld();
            Require(ReferenceEquals(nextClient.containers[1],client.containers[1]),"Неизменный контейнер не переиспользован.");
            Require(!ReferenceEquals(nextClient.containers[0],client.containers[0]) && Mathf.Approximately(nextClient.containers[0].x,moved.x),
                "Изменённый контейнер не разобран заново.");

            // C: a container added and another removed (order changes).
            var added=StateJson.Copy(host.containers[2]);added.id="check:delta-added";host.containers.Add(added);
            host.containers.RemoveAt(1);host.revision++;
            var c=RecoverySnapshot.FromWorld(host);
            var orderDelta=WorldDelta.Build(host.worldId,b.Id,b.Composition,c.Id,c.Composition);
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
                $"({100.0*encoded.Length/b.Json.Length:F1}%), применение {applyMs:F1} мс; смена состава, отказ чужой базе.");
        }

        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
