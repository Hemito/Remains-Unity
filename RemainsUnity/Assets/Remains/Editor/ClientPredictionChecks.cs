using System;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Editor
{
    // Interacting client first (decisions.md, 2026-10-24): a prediction is dropped when the canonical world confirms it,
    // undone on a rejection or after its deadline, and its undo puts the canonical part back.
    public static class ClientPredictionChecks
    {
        public static void Run()
        {
            var canonical=new WorldState {worldId="prediction-check"};canonical.lamps.Add(new LampState {id="l1",level="hub"});
            var live=Clone(canonical);
            var store=new ClientPredictions();
            ClientPredictions.Entry Lamp(float deadline)=>new() {Key="lamps:l1",Kind=WorldChangeKind.Lamps,Deadline=deadline,
                Confirmed=world=>world.lamps.Find(l=>l.id=="l1").broken,
                Revert=(source,target)=>target.lamps[0]=Remains.Serialization.StateJson.Copy(source.lamps[0])};
            live.lamps[0].broken=true;store.Add(Lamp(10));
            Require(store.Settle(canonical,5)==null && store.Count==1,"Неподтверждённое предсказание отменено до срока.");
            canonical.lamps[0].broken=true;
            Require(store.Settle(canonical,6)==null && store.Count==0,"Подтверждённое предсказание не снято.");
            canonical.lamps[0].broken=false;live.lamps[0].broken=true;store.Add(Lamp(10));
            var undo=store.Settle(canonical,11);
            Require(undo!=null && undo.Count==1 && store.Count==0,"Просроченное предсказание не отменено.");
            undo[0].Revert(canonical,live);
            Require(!live.lamps[0].broken && !ReferenceEquals(live.lamps[0],canonical.lamps[0]),"Отмена не вернула копию состояния хоста.");
            live.lamps[0].broken=true;store.Add(Lamp(100));
            undo=store.Settle(canonical,null,new[]{"lamps:l1"});
            Require(undo!=null && undo.Count==1 && store.Count==0,"Отказ хоста не отменил предсказание.");
            store.Add(Lamp(100));store.Add(Lamp(200));
            Require(store.Count==1,"Новое предсказание той же части не заменило прежнее.");
            Debug.Log("Предсказания клиента: PASS — подтверждение снимает, отказ и срок отменяют, отмена возвращает копию состояния хоста, одна запись на часть.");
        }
        private static WorldState Clone(WorldState world)=>Remains.Serialization.StateJson.FromJson<WorldState>(Remains.Serialization.StateJson.ToJson(world));
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
