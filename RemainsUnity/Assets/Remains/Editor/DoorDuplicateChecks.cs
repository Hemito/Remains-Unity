using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    // Doors duplicated in one cell of the source data (plant/ofisy2): the factory layout keeps one, and a saved world
    // that installed both loses the second by the same ordinal rule.
    public static class DoorDuplicateChecks
    {
        public static void Run()
        {
            var saved=new WorldState {worldId=Guid.NewGuid().ToString("N")};
            DoorState Door(string id,bool open)=>new() {id=id,definition="door1",level="factory:0",x=15.2f,y=360.8f,width=.8f,height=1.6f,open=open};
            saved.doors.Add(Door("factory:0:0,2:fgGoMNwistCm7gGF",true));saved.doors.Add(Door("factory:0:0,2:J9aBslt95fKr30Mf",false));
            var other=Door("factory:0:0,3:other",false);other.x=20;saved.doors.Add(other);
            int backups=0;var removed=DoorDuplicates.Remove(saved,()=>backups++);
            Require(removed.SequenceEqual(new[] {"factory:0:0,2:fgGoMNwistCm7gGF"}) && backups==1 && saved.doors.Count==2,$"сохранённый мир: убрана вторая по ID дверь, копия мира один раз; удалены [{string.Join(", ",removed)}], копий {backups}, осталось {saved.doors.Count}");
            Require(DoorDuplicates.Remove(saved,()=>backups++).Count==0 && backups==1,"повторная миграция ничего не меняет");

            var world=new WorldState {worldId=Guid.NewGuid().ToString("N"),seed=104};
            FactoryPreparation.Create(world,Resources.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey),0);
            try
            {
                FactoryContentSources.Prepare(world,"factory:0");
                var doors=RoomContentBatch.Read(FactoryContentSources.Require(world,"factory:0"),"factory:0").doors.doors;
                int duplicates=doors.Count(a=>doors.Any(b=>b!=a && DoorDuplicates.SamePlace(a,b)));
                Require(duplicates==0,"расстановка завода seed104 содержит двойные двери: "+duplicates);
                Debug.Log($"PASS двойные двери: завод seed104 дверей {doors.Length} без дублей; миграция сохранения удаляет дубль по правилу ID.");
            }
            finally {FactoryContentSources.Clear();}
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Двойные двери: "+message);}
    }
}
