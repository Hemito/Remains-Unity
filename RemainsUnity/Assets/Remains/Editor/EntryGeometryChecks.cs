using System;
using System.Linq;
using Remains.Content;
using Remains.Runtime;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class EntryGeometryChecks
    {
        public static void Run()
        {
            var source=LegacyFactoryFixture.Create();
            var before=StateJson.ToJson(source);
            using var geometry=new SimulationGeometry(level=>RoomLayout.Load(level,source));
            var room=geometry.PrepareEntry(source,"hub");
            Require(!geometry.LoadedLevels.Any(),"подготовленная локация стала активной до admission");
            Require(StateJson.ToJson(source)==before,"подготовка изменила исходный мир");
            var same=geometry.PrepareEntry(StateJson.Copy(source),"hub");
            Require(ReferenceEquals(room,same),"повторный token одной локации сбросил геометрию");
            Reject(()=>geometry.MarkEntryPrepared(RoomLayout.Load("hub",source)),"чужая ссылка готовности");
            var live=StateJson.Copy(source);
            live.actors.Add(new ActorState {id="player:entry-geometry",character="entry-geometry",player=true,connected=true,level="hub"});
            Reject(()=>geometry.Reconcile(live),"Welcome до завершения подготовки");
            geometry.MarkEntryPrepared(room);geometry.Reconcile(live);
            Require(ReferenceEquals(room,geometry.Get("hub")),"Welcome не принял тот же RoomLayout");
            geometry.Reconcile(StateJson.Copy(live));
            Require(ReferenceEquals(room,geometry.Get("hub")),"следующий snapshot сбросил принятую геометрию");
            var prepared=geometry.PrepareEntry(source,"hub");geometry.MarkEntryPrepared(prepared);
            var foreign=StateJson.Copy(live);foreign.worldId=Guid.NewGuid().ToString("N");
            Reject(()=>geometry.Reconcile(foreign),"другой мир принял прежнюю готовность");
            geometry.ClearEntryPreparation();geometry.Reconcile(live);
            Require(!ReferenceEquals(prepared,geometry.Get("hub")),"отмена оставила staged geometry для следующего входа");
            // A factory created by another player must not rebuild the level this one stands in: light and movement hold that object.
            var standing=geometry.Get("hub");var grown=StateJson.Copy(live);
            grown.rooms.Add(new WorldRoom {level="factory:7",definition="factory:plant:7"});
            grown.factories.Add(new WorldFactory {level="factory:7",definition="factory:plant:7",contentFingerprint="check",layout="{}"});
            geometry.Reconcile(grown);
            Require(ReferenceEquals(standing,geometry.Get("hub")),"новый завод пересобрал геометрию локации, где стоит игрок");
            var redefined=StateJson.Copy(grown);redefined.rooms.RemoveAll(item=>item.level=="hub");redefined.rooms.Add(new WorldRoom {level="hub",definition="hub:other"});
            var reloaded=new System.Collections.Generic.List<string>();
            using(var own=new SimulationGeometry(level=>{reloaded.Add(level);return RoomLayout.Load(level,live);}))
            {
                own.Reconcile(grown);var first=own.Get("hub");
                own.Reconcile(redefined);
                Require(!ReferenceEquals(first,own.Get("hub")) && reloaded.Count==2,"изменившаяся локация не пересобрана");
            }
            Debug.Log("Подготовка геометрии входа: PASS — отдельный staged cache, неизменный source, reuse, однократный перенос той же ссылки, преждевременный/чужой Welcome отклонён, отмена очищена.");
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Подготовка геометрии входа: "+reason);}
        private static void Reject(Action action,string reason)
        {
            bool rejected=false;try {action();} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"не отклонено: "+reason);
        }
    }
}
