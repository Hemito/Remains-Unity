using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryLegacyDoorChecks
    {
        public static void Run()
        {
            CheckDeclarationFailures();
            FactoryLegacyDoorAuthoring.Check();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ConnectedFactoryAuthoring.Path);
            Require(prefab!=null,"Нет реального связного завода.");
            var room=prefab.GetComponent<RoomAuthoring>();
            foreach(string level in new[]{"factory:0","factory:6","factory:-27"})
            {
                var layouts=RoomDoorCapabilityContent.Read(room,level);
                Require(layouts.Length==3 && layouts.SelectMany(l=>l.doors).Count()==9,"Не точные9 зарегистрированных ID: "+level);
                CheckMigration(RoomDoorContent.Read(room,level),layouts);
                CheckPreservedIntegrity(RoomDoorContent.Read(room,level),layouts);
                CheckFailures(RoomDoorContent.Read(room,level),layouts);
                CheckFrames(layouts);
            }
            Debug.Log("Прежние двери завода: PASS —9 точных placements/3 типа в0/6/−27; prepare-before-mutation/source callback, null→HP без сброса open/revision/extensions/refs, missing/no resurrection/repeat, сохранённая разрушенная/изменённая integrity, native die/дедупликация/геометрия. Ordinary game/оба peer/original runtime проверяются отдельно.");
        }
        private static void CheckDeclarationFailures()
        {
            var source=FactoryLegacyDoorAuthoring.ReadManifest();
            void Failure(Action<FactoryLegacyDoorAuthoring.Manifest> alter)
            {var copy=StateJson.Copy(source);alter(copy);Reject(()=>FactoryLegacyDoorAuthoring.ValidateDeclaration(copy));}
            Failure(m=>m.schema=2);
            Failure(m=>m.records[0]=null);
            Failure(m=>m.records[1].stableId=m.records[0].stableId);
            Failure(m=>m.records[0].module=27);
            Failure(m=>m.types[0].hp=61);
            Failure(m=>m.types[1].destroySound="unknown-mod-audio");
            Failure(m=>m.door3.classification="true-vector");
            Failure(m=>m.door3.width=160);
            Failure(m=>m.door3.pivotPixels[1]=1);
            Failure(m=>m.door3.textureGuid="invalid-guid");
            Failure(m=>m.protectedFiles[m.protectedFiles.Keys.First()]="not-a-sha");
        }
        private static WorldState Legacy(DoorLayout initial,DoorCapabilityLayout[] layouts)
        {
            var world=new WorldState {worldId="legacy-door-check",revision=23};
            world.doors=initial.doors.Select(StateJson.Copy).ToList();
            var selected=layouts.SelectMany(l=>l.doors).Select(d=>d.id).ToHashSet(StringComparer.Ordinal);
            foreach(var door in world.doors.Where(d=>selected.Contains(d.id)))
            {
                door.integrity=null;door.interaction=null;door.open=(door.id.GetHashCode()&1)==0;door.revision=17;
                door.extensions=new Dictionary<string,JToken> {{"modDoor",StateJson.FromJson<JToken>("{\"exact\":1.234567890123456789,\"nested\":[\"данные мода\",7]}")}};
            }
            world.extensions=new Dictionary<string,JToken> {{"modWorld",StateJson.FromJson<JToken>("{\"untouched\":true}")}};
            return world;
        }
        private static void CheckMigration(DoorLayout initial,DoorCapabilityLayout[] layouts)
        {
            var world=Legacy(initial,layouts);string detachedSource=StateJson.ToJson(world);
            var list=world.doors;var references=world.doors.ToArray();var before=world.doors.Select(StateJson.Copy).ToArray();
            string firstMissing=layouts[0].doors[0].id;world.doors.Remove(world.doors.Single(d=>d.id==firstMissing));
            int count=world.doors.Count,callbacks=0;long revision=world.revision;
            var expected=layouts.SelectMany(l=>l.doors).ToDictionary(d=>d.id,StringComparer.Ordinal);
            Require(DoorCapabilityMigration.Prepare(world,layouts,()=>callbacks++),"Нет явного обновления.");
            Require(callbacks==1 && ReferenceEquals(world.doors,list) && world.doors.Count==count && world.revision==revision+1 && layouts.All(l=>world.contentPatches.Contains(l.patch)),"Callback/list/count/revision/marker нарушены.");
            Require(!world.doors.Any(d=>d.id==firstMissing),"Удалённая дверь создана заново.");
            foreach(var door in world.doors)
            {
                var old=before.Single(d=>d.id==door.id);
                Require(ReferenceEquals(door,references.Single(d=>d.id==door.id)),"Подменён existing object: "+door.id);
                if(expected.TryGetValue(door.id,out var desired))
                {
                    Require(door.integrity!=null && StateJson.ToJson(door.integrity)==StateJson.ToJson(desired.integrity),"Прочность не взята из canonical initial layout: "+door.id);
                    var normalized=StateJson.Copy(door);normalized.integrity=null;
                    Require(StateJson.ToJson(normalized)==StateJson.ToJson(old),"Изменены open/revision/interaction/geometry/unknown: "+door.id);
                }
                else Require(StateJson.ToJson(door)==StateJson.ToJson(old),"Чужая дверь изменена: "+door.id);
            }
            string after=StateJson.ToJson(world);
            Require(!DoorCapabilityMigration.Prepare(world,layouts,()=>throw new InvalidOperationException("Повторный callback")) && StateJson.ToJson(world)==after,"Повтор записал мир или вернул отсутствующую дверь.");
            var loaded=StateJson.FromJson<WorldState>(after);
            Require(StateJson.ToJson(loaded)==after && !DoorCapabilityMigration.Prepare(loaded,layouts,()=>throw new InvalidOperationException("Callback после reload")),"Roundtrip потерял unknown/marker или повторно обновил дверь.");
            Require(StateJson.ToJson(StateJson.FromJson<WorldState>(detachedSource))==detachedSource,"Исходный detached JSON изменён.");
        }
        private static void CheckPreservedIntegrity(DoorLayout initial,DoorCapabilityLayout[] layouts)
        {
            var world=Legacy(initial,layouts);var desired=layouts.SelectMany(l=>l.doors).ToArray();
            for(int i=0;i<desired.Length;i++)
            {
                var door=world.doors.Single(d=>d.id==desired[i].id);
                door.integrity=new DoorIntegrity {maxHealth=123+i,health=i==0?0:31+i};
                door.integrity.resolvedAttacks.Add("saved-attack-"+i);door.integrity.extensions=new Dictionary<string,JToken> {{"modIntegrity",new JValue("сохранённая прочность")}};
            }
            var states=world.doors.Select(d=>(door:d,json:StateJson.ToJson(d),integrity:d.integrity)).ToArray();
            Require(DoorCapabilityMigration.Prepare(world,layouts,()=>{}),"Не записаны markers уже имеющейся integrity.");
            foreach(var state in states) Require(StateJson.ToJson(state.door)==state.json && ReferenceEquals(state.door.integrity,state.integrity),"Сброшена saved integrity/damage/death/extensions: "+state.door.id);
            var absent=Legacy(initial,layouts);var ids=desired.Select(d=>d.id).ToHashSet(StringComparer.Ordinal);
            absent.doors.RemoveAll(d=>ids.Contains(d.id));int count=absent.doors.Count;
            Require(DoorCapabilityMigration.Prepare(absent,layouts,()=>{}) && absent.doors.Count==count,"Полностью отсутствующий набор воскрес.");
        }
        private static void CheckFailures(DoorLayout initial,DoorCapabilityLayout[] layouts)
        {
            void Failure(Action<WorldState,DoorCapabilityLayout[]> alter)
            {
                var world=Legacy(initial,layouts);var candidates=StateJson.Copy(layouts);alter(world,candidates);
                string before=StateJson.ToJson(world);int callbacks=0;
                Reject(()=>DoorCapabilityMigration.Prepare(world,candidates,()=>callbacks++));
                Require(callbacks==0 && StateJson.ToJson(world)==before,"Отказ изменил более раннюю дверь/мир или вызвал backup.");
            }
            Failure((w,l)=>w.doors.Single(d=>d.id==l.Last().doors.Last().id).width+=.1f);
            Failure((w,l)=>l.Last().doors.Last().definition="unknown-definition");
            Failure((w,l)=>l.Last().doors.Last().integrity.health=-1);
            Failure((w,l)=>l.Last().doors.Last().id=l[0].doors[0].id);
            Failure((w,l)=>l.Last().patch=l[0].patch);
            Failure((w,l)=>w.revision=long.MaxValue);
            Failure((w,l)=>w.props.Add(new PropState {id=l[0].doors[0].id}));
            var world=Legacy(initial,layouts);string before=StateJson.ToJson(world);
            Reject(()=>DoorCapabilityMigration.Prepare(world,layouts,()=>throw new InvalidOperationException("Не сохранён источник")));
            Require(StateJson.ToJson(world)==before,"Неудачный preserveSource успел изменить мир.");
            Reject(()=>DoorCapabilityMigration.Prepare(world,layouts,null));
            Require(StateJson.ToJson(world)==before,"Отсутствующий backup callback изменил мир.");
        }
        private static void CheckFrames(DoorCapabilityLayout[] layouts)
        {
            foreach(var source in layouts.SelectMany(l=>l.doors))
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remains/Resources/Presentation/Doors/"+source.definition+".prefab");
                var instance=UnityEngine.Object.Instantiate(prefab);
                try
                {
                    var view=instance.GetComponent<DoorView>();var actorIndependent=StateJson.Copy(source);var position=instance.transform.position;
                    view.Show(actorIndependent);var closed=view.Artwork.bounds;
                    actorIndependent.open=true;view.Show(actorIndependent);
                    Require(view.Artwork.sprite==view.Definition.Opened && instance.transform.position==position,"Open сместил исходную регистрацию: "+source.id);
                    actorIndependent.open=false;string attack="native-check:"+source.id;
                    Require(DoorDamageOperations.Apply(actorIndependent,attack,actorIndependent.integrity.maxHealth,out _)==DoorChange.Changed && actorIndependent.Destroyed,"Общий damage не разрушает upgraded тип: "+source.id);
                    long revision=actorIndependent.revision;view.Show(actorIndependent);
                    Require(view.Artwork.sprite==view.Definition.Destroyed && instance.transform.position==position &&
                        Vector3.Distance(view.Artwork.bounds.min,closed.min)<.0001f && Vector3.Distance(view.Artwork.bounds.max,closed.max)<.0001f,"Die изменил visible world canvas/anchor: "+source.id);
                    Require(DoorDamageOperations.Apply(actorIndependent,attack,1,out _)==DoorChange.Unchanged && actorIndependent.revision==revision,"Повтор damage изменил destroyed объект.");
                    Require(DoorGeometry.Compose(Array.Empty<RoomSurface>(),new[]{actorIndependent},source.level).Length==0 && !actorIndependent.CanInteract,"Die оставил blocking/E volume.");
                    var reloaded=StateJson.FromJson<DoorState>(StateJson.ToJson(actorIndependent));view.Show(reloaded);
                    Require(view.Artwork.sprite==view.Definition.Destroyed && reloaded.Destroyed,"Reload воскресил дверь/изображение.");
                }
                finally {UnityEngine.Object.DestroyImmediate(instance);}
            }
        }
        private static void Reject(Action action)
        {bool rejected=false;try {action();} catch(InvalidOperationException) {rejected=true;} catch(ArgumentException) {rejected=true;}Require(rejected,"Некорректная миграция принята.");}
        private static void Require(bool value,string message)=>FactoryLegacyDoorAuthoring.Require(value,message);
    }
}
