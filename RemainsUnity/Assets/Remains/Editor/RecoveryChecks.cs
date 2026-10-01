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
                var payload=System.Text.Encoding.UTF8.GetBytes(wire);
                var receiver=new SnapshotTransfer(snapshot.Id);
                var half=new byte[payload.Length/2];Array.Copy(payload,half,half.Length);
                Require(receiver.Append(payload.Length,0,half)==null,"Часть передачи не публикуется как сохранение");
                Require(receiver.Append(payload.Length,0,half)==null,"Повтор части не меняет позицию сборки");
                var rest=new byte[payload.Length-half.Length];Array.Copy(payload,half.Length,rest,0,rest.Length);
                Require(receiver.Append(payload.Length,half.Length,rest).Id==snapshot.Id,"Полная передача восстанавливает исходный хеш");
                bool rejected=false;
                try {new SnapshotTransfer(new string('0',64)).Append(payload.Length,0,payload);}
                catch(InvalidDataException) {rejected=true;}
                Require(rejected,"Подмена хеша не публикуется");
                rejected=false;
                try {new SnapshotTransfer(snapshot.Id).Append(SnapshotTransfer.MaximumBytes+1,0,half);}
                catch(InvalidDataException) {rejected=true;}
                Require(rejected,"Невозможный размер отклонён до выделения памяти");
                var root=new WorldState();WorldHistory.BeginContinuation(root);
                var first=store.SaveWorld(root);var old=root.Copy();
                root.revision++;var later=store.SaveWorld(root);
                Require(WorldHistory.Compare(root,later.Id,old,first.Id)==WorldRelation.Newer,"Порядок внутри ветви определяется снимками");
                var left=root.Copy();WorldHistory.BeginContinuation(left);left.revision=1000;
                var leftSnapshot=store.SaveWorld(left);
                var right=root.Copy();WorldHistory.BeginContinuation(right);right.revision=2;
                var rightSnapshot=store.SaveWorld(right);
                Require(WorldHistory.Compare(left,leftSnapshot.Id,right,rightSnapshot.Id)==WorldRelation.Divergent,"Большая ревизия не делает независимую ветвь новее");
                Require(WorldHistory.Compare(left,leftSnapshot.Id,root,later.Id)==WorldRelation.Newer,"Точка ответвления доказывает продолжение");
                var fromOld=old.Copy();WorldHistory.BeginContinuation(fromOld);var fork=store.SaveWorld(fromOld);
                Require(WorldHistory.Compare(fromOld,fork.Id,root,later.Id)==WorldRelation.Divergent,"Ответвление от старого снимка не включает последующий прогресс исходной ветви");
                var options=store.Continuations();
                Require(options.Exists(x=>x.Id==leftSnapshot.Id) && options.Exists(x=>x.Id==rightSnapshot.Id) && options.Exists(x=>x.Id==fork.Id),"Все независимые продолжения доступны для выбора");
                Require(!options.Exists(x=>x.Id==first.Id) && !options.Exists(x=>x.Id==later.Id),"Доказанные предки скрыты из списка продолжений");
                store.SelectContinuation(rightSnapshot);
                Require(store.SelectedSnapshot().Id==rightSnapshot.Id && store.LoadWorld(false).branchId==right.branchId,"Выбор загружает целую указанную ветвь");
                Require(store.Continuations().Exists(x=>x.Id==leftSnapshot.Id),"Выбор не удаляет альтернативную ветвь");
                Debug.Log("RecoveryChecks PASS: точные пакеты, изоляция предсказания, архив и подтверждения.");
            }
            finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
        }
        private static void Require(bool condition,string message)
        { if(!condition) throw new InvalidOperationException("RecoveryChecks: "+message); }
    }
}
