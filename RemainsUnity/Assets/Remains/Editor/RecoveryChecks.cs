using System;
using System.IO;
using Remains.Domain;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Editor
{
    public static class RecoveryChecks
    {
        public static void Run()
        {
            string directory=Path.Combine(Path.GetTempPath(),"Remains-recovery-"+Guid.NewGuid().ToString("N"));
            try
            {
                var store=new SaveStore(directory);
                var world=new WorldState {revision=17};
                world.actors.Add(new ActorState {id="player",health=73,x=4});
                string wire=JsonUtility.ToJson(world);
                // Unknown extension data must survive archival even before a loader knows it.
                wire=wire.Substring(0,wire.Length-1)+",\"unknownModPayload\":{\"value\":42}}";
                var snapshot=new RecoverySnapshot(wire);
                store.SaveRecovery(snapshot);
                var live=snapshot.CreateLiveWorld();
                live.actors[0].health=12;live.actors[0].x=99;
                Require(File.ReadAllText(Path.Combine(directory,"world.json"))==wire,"Предсказание не меняет сохранённый пакет и неизвестные поля");
                var gate=new RecoveryReceiptGate();gate.Register(snapshot);
                Require(gate.Matches(snapshot.Id,17),"Подтверждается сохранённый снимок");
                var predicted=new RecoverySnapshot(JsonUtility.ToJson(live));
                Require(!gate.Matches(predicted.Id,17) && !gate.Matches(snapshot.Id,18),"Та же ревизия с другим содержимым не подтверждается");
                store.SaveRecovery(predicted);
                Require(File.ReadAllText(Path.Combine(directory,"Worlds",world.worldId,"Snapshots",snapshot.Id+".json"))==wire,"Другое продолжение не стирает исходный снимок");
                var other=new RecoverySnapshot(JsonUtility.ToJson(new WorldState()));store.SaveRecovery(other);
                Require(File.Exists(Path.Combine(directory,"Worlds",world.worldId,"Snapshots",predicted.Id+".json")),"Другой мир сохраняет архив первого");
                gate.Clear();Require(!gate.Matches(snapshot.Id,17),"Новая сессия не принимает старые квитанции");
                Debug.Log("RecoveryChecks PASS: точные пакеты, изоляция предсказания, архив и подтверждения.");
            }
            finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
        }
        private static void Require(bool condition,string message)
        { if(!condition) throw new InvalidOperationException("RecoveryChecks: "+message); }
    }
}
