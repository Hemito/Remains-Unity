using System;
using System.IO;
using System.Threading;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    // Native Editor contracts. These do not prove player UnloadUnusedAssets reclamation:
    // the Editor and the test's own asset references may legitimately retain resources.
    public static class RoomCatalogLifetimeChecks
    {
        public static void Run()
        {
            using var fixture=new Fixture();
            CheckOwners(fixture);
            CheckFailedPreparation(fixture);
            CheckReentrancy(fixture);
            CheckDestroyedNative(fixture);
            CheckThreadGuard(fixture);
            CheckUncachedAuthoring(fixture);
            CheckSavedLayouts();
            Debug.Log("Каталог комнат: PASS — native SO, независимые владельцы, rollback/retry, потоки и свежие layout. Освобождение Resources.UnloadUnusedAssets требует отдельной проверки в player.");
        }

        private static void CheckOwners(Fixture fixture)
        {
            int loads=0;
            var lifetime=new RoomCatalogLifetime(()=>{loads++;return fixture.Catalog;},"checks/rooms/owners");
            Reject(()=>lifetime.Require(),"checks/rooms/owners","Каталог доступен без владельца");
            Reject(()=>lifetime.Acquire(" "),"checks/rooms/owners","Принят безымянный владелец");
            Require(loads==0,"Отказ без владельца неожиданно загрузил ресурс");
            var first=lifetime.Acquire("room/first");
            var second=lifetime.Acquire("room/second");
            try
            {
                Require(loads==1 && lifetime.ActiveLeases==2 && lifetime.Epoch==1 &&
                    ReferenceEquals(lifetime.Require(),fixture.Catalog),"Владельцы не разделяют исходный native каталог одной эпохи");
                first.Dispose();first.Dispose();
                Require(lifetime.ActiveLeases==1 && loads==1 && lifetime.Require().Resolve("checks:a")==fixture.First,
                    "Повторное освобождение первого владельца нарушило второго");
                try
                {
                    using var failingBody=lifetime.Acquire("room/throwing-body");
                    throw new FixtureBodyException();
                }
                catch(FixtureBodyException) {}
                Require(lifetime.ActiveLeases==1 && lifetime.Require()==fixture.Catalog,
                    "Исключение в using оставило лишнего владельца или сняло активного");
            }
            finally {first.Dispose();second.Dispose();}
            Reject(()=>lifetime.Require(),"checks/rooms/owners","Последнее освобождение сохранило доступ к каталогу");
            Require(fixture.Catalog!=null,"Освобождение уничтожило общий Unity asset");
            using var next=lifetime.Acquire("room/new-epoch");
            first.Dispose();second.Dispose();
            Require(loads==2 && lifetime.Epoch==2 && lifetime.ActiveLeases==1 && lifetime.Require()==fixture.Catalog,
                "Старый токен снял новую эпоху или финальное освобождение сохранило cache");
        }

        private static void CheckFailedPreparation(Fixture fixture)
        {
            RoomCatalogAsset candidate=null;
            int loads=0;
            var lifetime=new RoomCatalogLifetime(()=>{loads++;return candidate;},"checks/rooms/failure");
            Reject(()=>lifetime.Acquire("room/null"),"room/null","Отсутствующий native SO принят");
            Require(lifetime.ActiveLeases==0 && lifetime.Epoch==0 && loads==1,"Отказ загрузки опубликовал эпоху/владельца");
            candidate=ScriptableObject.CreateInstance<RoomCatalogAsset>();candidate.name="Проверка невалидного каталога";
            try
            {
                var invalid=new[]
                {
                    new[] {new RoomCatalogAsset.Entry {id="same",room=fixture.First},new RoomCatalogAsset.Entry {id="same",room=fixture.Second}},
                    new RoomCatalogAsset.Entry[] {null},
                    new[] {new RoomCatalogAsset.Entry {id="missing-room",room=null}},
                    new[] {new RoomCatalogAsset.Entry {id="",room=fixture.First}},
                    null
                };
                for(int i=0;i<invalid.Length;i++)
                {
                    // Configure assigns this detached fixture before its real Validate rejects it.
                    Reject(()=>candidate.Configure(invalid[i]),null,"Невалидный native fixture не отвергнут самим каталогом");
                    long before=lifetime.Epoch;
                    Reject(()=>lifetime.Acquire("room/invalid-"+i),"room/invalid-"+i,"Невалидный каталог опубликован");
                    Require(lifetime.ActiveLeases==0 && lifetime.Epoch==before,"Неуспешная подготовка изменила владельцев/эпоху");
                    Reject(()=>lifetime.Require(),"checks/rooms/failure","Отказ подготовки оставил доступный cache");
                    candidate.Configure(new[] {new RoomCatalogAsset.Entry {id="recovered",room=fixture.First}});
                    using(var retry=lifetime.Acquire("room/retry-"+i))
                        Require(lifetime.Require().Resolve("recovered")==fixture.First && lifetime.Epoch==before+1,
                            "Исправленный native SO не загрузился после validation rollback");
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(candidate);}
            var fault=new IOException("Явная ошибка загрузчика fixture");
            bool fail=true;
            var faulting=new RoomCatalogLifetime(()=>{if(fail) throw fault;return fixture.Catalog;},"checks/rooms/fault");
            var error=Reject(()=>faulting.Acquire("room/fault"),"room/fault","Ошибка загрузчика скрыта");
            Require(ReferenceEquals(error.InnerException,fault) && faulting.ActiveLeases==0 && faulting.Epoch==0,
                "Отказ потерял исходную ошибку или опубликовал частичный каталог");
            fail=false;
            using var recovered=faulting.Acquire("room/after-fault");
            Require(faulting.Require().Resolve("checks:a")==fixture.First,"Повтор после сбоя загрузчика не работает");
        }

        private static void CheckReentrancy(Fixture fixture)
        {
            RoomCatalogLifetime lifetime=null;
            bool recurse=true;
            lifetime=new RoomCatalogLifetime(()=>
            {
                if(recurse) using(lifetime.Acquire("room/recursive")) {}
                return fixture.Catalog;
            },"checks/rooms/reentrant");
            Reject(()=>lifetime.Acquire("room/outer"),"room/outer","Рекурсивная загрузка опубликовала каталог");
            Require(lifetime.ActiveLeases==0 && lifetime.Epoch==0,"Рекурсивный отказ оставил активную подготовку");
            recurse=false;
            using var retry=lifetime.Acquire("room/nonrecursive");
            Require(lifetime.Require()==fixture.Catalog,"После рекурсивного отказа сервис остался заблокирован");
        }

        private static void CheckDestroyedNative(Fixture fixture)
        {
            var candidate=UnityEngine.Object.Instantiate(fixture.Catalog);
            int loads=0;
            var lifetime=new RoomCatalogLifetime(()=>{loads++;return candidate;},"checks/rooms/native-dead");
            var first=lifetime.Acquire("room/native-owner");var second=lifetime.Acquire("room/native-observer");
            try
            {
                UnityEngine.Object.DestroyImmediate(candidate);
                Require(!ReferenceEquals(candidate,null) && candidate==null,"Fixture не сохранил wrapper уничтоженного native SO");
                Reject(()=>lifetime.Require(),"room/native-owner","Уничтоженный native SO доступен живым владельцам");
                Reject(()=>lifetime.Acquire("room/unexpected-reload"),"checks/rooms/native-dead","Живой lease молча перезагрузил уничтоженный SO");
                Require(loads==1 && lifetime.ActiveLeases==2 && lifetime.Epoch==1,"Native-dead запрос изменил существующую эпоху");
            }
            finally {first.Dispose();second.Dispose();}
            Reject(()=>lifetime.Acquire("room/dead-loader"),"room/dead-loader","Загрузчик вернул dead wrapper как valid SO");
            Require(lifetime.ActiveLeases==0 && lifetime.Epoch==1,"Dead loader опубликовал новую эпоху");
            candidate=UnityEngine.Object.Instantiate(fixture.Catalog);
            try
            {
                using var rebuilt=lifetime.Acquire("room/new-native");
                Require(lifetime.Epoch==2 && lifetime.Require().Resolve("checks:b")==fixture.Second,
                    "Явная новая эпоха не принимает восстановленный native SO");
            }
            finally {UnityEngine.Object.DestroyImmediate(candidate);}
        }

        private static void CheckThreadGuard(Fixture fixture)
        {
            int loads=0;
            var lifetime=new RoomCatalogLifetime(()=>{loads++;return fixture.Catalog;},"checks/rooms/thread");
            using var lease=lifetime.Acquire("room/main");
            var errors=new Exception[5];
            var calls=new Action[] {()=>lifetime.Acquire("room/worker"),()=>lifetime.Require(),()=>{_=lifetime.ActiveLeases;},()=>{_=lifetime.Epoch;},()=>lease.Dispose()};
            var worker=new Thread(()=>
            {
                for(int i=0;i<calls.Length;i++)
                    try {calls[i]();} catch(Exception error) {errors[i]=error;}
            }) {IsBackground=true};
            worker.Start();Require(worker.Join(5000),"Проверочный поток каталога не завершился");
            foreach(var error in errors)
                Require(error is InvalidOperationException && error.Message.Contains("checks/rooms/thread"),
                    "Вызов/освобождение из worker не отвергнут с контекстом ресурса");
            Require(loads==1 && lifetime.ActiveLeases==1 && lifetime.Require()==fixture.Catalog,
                "Worker изменил активного владельца или вызвал native загрузчик");
        }

        private static void CheckUncachedAuthoring(Fixture fixture)
        {
            var catalog=UnityEngine.Object.Instantiate(fixture.Catalog);
            try
            {
                Require(catalog.Resolve("checks:a")==fixture.First,"Native clone потерял ссылки на авторинг");
                catalog.Configure(new[] {new RoomCatalogAsset.Entry {id="checks:a",room=fixture.Second}});
                Require(catalog.Resolve("checks:a")==fixture.Second && fixture.Catalog.Resolve("checks:a")==fixture.First,
                    "Editor lookup сохранил stale index или detached SO изменил исходник");
                Reject(()=>catalog.Configure(new[] {new RoomCatalogAsset.Entry {id="checks:a",room=null}}),null,"Invalid Editor fixture принят");
                Reject(()=>catalog.Resolve("checks:a"),"checks:a","Editor lookup скрыл текущую невалидную ссылку");
                catalog.Configure(new[] {new RoomCatalogAsset.Entry {id="checks:a",room=fixture.First}});
                Require(catalog.Resolve("checks:a")==fixture.First,"Editor исправление требует скрытого сброса cache");
            }
            finally {UnityEngine.Object.DestroyImmediate(catalog);}
        }

        private static void CheckSavedLayouts()
        {
            var source=RoomCatalogAsset.Load().Resolve(NewGameWorld.FactoryDefinitionId);
            var world=new WorldState();
            foreach(string level in new[] {"factory:0","factory:6","factory:-27"})
                world.rooms.Add(new WorldRoom {level=level,definition=NewGameWorld.FactoryDefinitionId});
            var first=RoomLayout.Load("factory:0",world);var second=RoomLayout.Load("factory:6",world);var third=RoomLayout.Load("factory:-27",world);
            string original=StateJson.ToJson(first);
            Require(first.platforms.Length>0 && first.ladders.Length>0 && first.artwork.Length>0,"В actual factory отсутствуют данные для проверки независимых layout");
            Require(!ReferenceEquals(first,second) && !ReferenceEquals(first.platforms,second.platforms) &&
                !ReferenceEquals(first.platforms[0],second.platforms[0]) && !ReferenceEquals(second.platforms[0],third.platforms[0]) &&
                !ReferenceEquals(first.ladders[0],second.ladders[0]) && !ReferenceEquals(first.artwork[0],second.artwork[0]),
                "Один mutable layout/его элементы разделяются независимыми factory instances");
            first.platforms[0].x+=1000;first.ladders[0].bottom+=1000;first.artwork[0].width+=1;
            var door=new DoorState {id="checks:door",definition="checks:door",level="factory:0",surfaceTexture="floor",x=2,y=1,width=.8f,height=1.6f};
            first.platforms=DoorGeometry.Compose(first.platforms,new[] {door},"factory:0");
            first.dynamicSurfaces=new DynamicSurfaceFrame();
            Require(first.platforms[first.platforms.Length-1].sourceDoorId==door.id && second.dynamicSurfaces==null && third.dynamicSurfaces==null &&
                StateJson.ToJson(second)==original && StateJson.ToJson(third)==original && StateJson.ToJson(source.CreateLayout())==original,
                "Derived door/платформа или изменение экземпляра затронуло другую локацию/authoring source");
            var physics=source.CreateLayout(false);var after=source.CreateLayout();
            Require(physics.artwork==null && StateJson.ToJson(after)==original && !ReferenceEquals(after.platforms,second.platforms),
                "Запрос без оформления испортил следующий full layout или вернул cache");
            var clone=UnityEngine.Object.Instantiate(source);
            try
            {
                RoomSurfaceAuthoring surface=null;
                foreach(var candidate in clone.GetComponentsInChildren<RoomSurfaceAuthoring>(true))
                    if(clone.Included(candidate)) {surface=candidate;break;}
                Require(surface!=null,"Actual native clone не содержит включённой авторской поверхности");
                var before=surface.Read(clone);surface.transform.localPosition+=Vector3.right*.125f;
                var changed=clone.CreateLayout(false);
                bool found=false;
                foreach(var value in changed.platforms)
                    if(Mathf.Abs(value.x-before.x-.125f)<.00001f && value.y==before.y && value.width==before.width && value.height==before.height) found=true;
                Require(found && StateJson.ToJson(source.CreateLayout())==original,
                    "Свежий Editor layout не видит actual изменённый Transform либо изменил saved prefab");
            }
            finally {UnityEngine.Object.DestroyImmediate(clone.gameObject);}
        }

        private static InvalidOperationException Reject(Action action,string context,string message)
        {
            try {action();} catch(InvalidOperationException error)
            {Require(context==null || error.Message.Contains(context),message+": нет контекста "+context);return error;}
            catch(ArgumentException error)
            {Require(context==null || error.Message.Contains(context),message+": нет контекста "+context);return new InvalidOperationException(error.Message,error);}
            throw new InvalidOperationException(message);
        }
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
        private sealed class FixtureBodyException : Exception {}
        private sealed class Fixture : IDisposable
        {
            public readonly RoomAuthoring First,Second;
            public readonly RoomCatalogAsset Catalog;
            public Fixture()
            {
                First=new GameObject("Проверка lifetime комнаты A").AddComponent<RoomAuthoring>();
                Second=new GameObject("Проверка lifetime комнаты B").AddComponent<RoomAuthoring>();
                Catalog=ScriptableObject.CreateInstance<RoomCatalogAsset>();Catalog.name="Проверочный native каталог комнат";
                Catalog.Configure(new[] {new RoomCatalogAsset.Entry {id="checks:a",room=First},new RoomCatalogAsset.Entry {id="checks:b",room=Second}});
            }
            public void Dispose()
            {UnityEngine.Object.DestroyImmediate(Catalog);UnityEngine.Object.DestroyImmediate(First.gameObject);UnityEngine.Object.DestroyImmediate(Second.gameObject);}
        }
    }
}
