using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryLevelAuthoring
    {
        public const string PrefabPath="Assets/Remains/Resources/Content/FactoryLevelRoot.prefab";
        [MenuItem("Remains/Комнаты/Подготовить корень игрового завода")]
        public static void Generate()
        {
            var stage=UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if(stage!=null && stage.assetPath==PrefabPath)
                throw new InvalidOperationException("Закройте редактирование префаба корня завода перед его подготовкой.");
            var catalog=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath)
                ??throw new InvalidOperationException("Сначала подготовьте каталог игрового завода.");
            var root=new GameObject("Завод");
            try
            {
                var room=root.AddComponent<RoomAuthoring>();
                var container=new GameObject("Комнаты").transform;container.SetParent(root.transform,false);
                root.AddComponent<FactoryLevelRoot>().Configure(catalog,room,container);
                DoorView Door(string id)=>AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Remains/Resources/Presentation/Doors/{id}.prefab")?.GetComponent<DoorView>()
                    ??throw new InvalidOperationException("Нет готового префаба двери: "+id);
                root.AddComponent<FactoryDoorAdapter>().Configure(Door("door1"),Door("door2"),Door("door3"));
                var view=root.AddComponent<FactoryLevelView>();
                root.AddComponent<FactoryLightFocus>().Configure(view);
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath,out bool saved);
                if(!saved) throw new IOException("Не записан корень завода: "+PrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        public static void RunBatch()
        {
            int code=0;
            try {Generate();Check();}
            catch(Exception error) {Debug.LogError("Подготовка корня завода прервана: "+error);code=1;}
            EditorApplication.Exit(code);
        }
        public static void RunPresentationBatch()
        {
            int code=0;
            GameObject visual=null,cameraObject=null;
            RoomPresentationGate gate=null;
            try
            {
                RoomLoadingViewChecks.Run();
                var catalog=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath);
                var world=new WorldState{seed=104};
                FactoryPreparation.Create(world,catalog,0);
                FactoryContentSources.Prepare(world,"factory:0");
                var source=RoomAuthoring.Load("factory:0",world);
                DoorContentInstaller.Apply(world,RoomDoorContent.Read(source,"factory:0"));
                var room=source.CreateLayout(false);
                visual=AuthoredRoomVisual.Create("factory:0",room,world);
                var view=visual.GetComponent<FactoryLevelView>();
                cameraObject=new GameObject("Проверка камеры завода",typeof(Camera));
                var camera=cameraObject.GetComponent<Camera>();camera.cullingMask=31;
                gate=new RoomPresentationGate(camera);gate.SetContext(true,room);gate.Bind(view);
                if(view==null || gate.IsReady || camera.cullingMask!=0)
                    throw new InvalidOperationException("Камера завода открылась до подготовки окна света.");
                view.Synchronize(room,world,1);
                view.Focus(new Vector2(room.spawnX,room.spawnY));
                if(!gate.IsReady || camera.cullingMask!=31 || view.AppliedOcclusionRevision!=1 || view.LightWindowRooms.y!=0)
                    throw new InvalidOperationException("Готовый свет верхнего входа не принят камерой завода.");
                gate.SetEntryPending(true);
                if(gate.IsReady || camera.cullingMask!=0)
                    throw new InvalidOperationException("Вход в завод показан до завершения допуска.");
                gate.SetEntryPending(false);
                if(!gate.IsReady) throw new InvalidOperationException("Допуск не принял подготовленный свет завода.");
                view.Release();
                if(gate.IsReady || camera.cullingMask!=0)
                    throw new InvalidOperationException("Освобождение света оставило игровую камеру открытой.");
                Debug.Log("Игровой показ завода: PASS; авторский корень, окно света верхнего входа, ревизия дверей, общий допуск камеры и освобождение ресурсов.");
            }
            catch(Exception error) {Debug.LogError("Проверка игрового показа завода прервана: "+error);code=1;}
            finally
            {
                gate?.Dispose();
                if(visual!=null) UnityEngine.Object.DestroyImmediate(visual);
                if(cameraObject!=null) UnityEngine.Object.DestroyImmediate(cameraObject);
                FactoryContentSources.Clear();
            }
            EditorApplication.Exit(code);
        }
        public static void Check()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath);
            var world=new WorldState{seed=104};
            FactoryPreparation.Create(world,catalog,0);
            var root=FactoryLevelRoot.Create(world,"factory:0");
            try
            {
                var geometry=root.Authoring.CreateLayout(false);
                var anatomy=StateJson.FromJson<AnatomyCatalog>(Resources.Load<TextAsset>("Content/anatomy").text).anatomies.Single(value=>value.id=="pony");
                var arrival=FactoryArrival.Require(root.Prepared,root.Objects);
                string arrivalError=FactoryArrival.Rejection(geometry,anatomy.movement,new Vector2(geometry.spawnX,geometry.spawnY));
                if(arrivalError!=null || new Vector2(geometry.spawnX,geometry.spawnY)!=arrival.Position)
                    throw new InvalidOperationException(arrivalError??"Прибытие не совпало с исходным маркером игрока.");
                Debug.Log($"Прибытие завода: PASS; ID {arrival.Id}, точка {arrival.Position}, свободная опора общей сетки.");
                if(geometry.platforms.Length!=root.Prepared.Physics.platforms.Length || root.Objects.Count==0 ||
                    root.Objects.Select(o=>o.Id).Distinct(StringComparer.Ordinal).Count()!=root.Objects.Count)
                    throw new InvalidOperationException("Геометрия или ID объектов корня не совпали с подготовленными данными.");
                root.InstantiateRooms();
                if(root.Rooms.childCount!=84) throw new InvalidOperationException("Число комнат корня не совпало с раскладкой.");
                var adapter=root.GetComponent<FactoryDoorAdapter>();adapter.Install(root);
                var doors=RoomDoorContent.Read(root.Authoring,"factory:0");
                if(doors.doors.Length!=adapter.Installed || doors.doors.Length==0 ||
                    doors.doors.Any(door=>!root.Objects.Any(obj=>obj.Id==door.id && obj.Source.Params.TryGetValue("lock",out var value) && value=="0")))
                    throw new InvalidOperationException("Адаптер потерял ID дверей или установил неподдержанный замок.");
                DoorContentInstaller.Apply(world,doors);
                var savedDoor=world.doors[0];savedDoor.open=true;savedDoor.integrity.health=0;savedDoor.revision=2;
                if(DoorContentInstaller.Apply(world,doors) || !savedDoor.open || !savedDoor.Destroyed || savedDoor.revision!=2)
                    throw new InvalidOperationException("Повторная установка сбросила сохранённую дверь.");
                Debug.Log($"Адаптер завода: PASS; явно незапертых дверей {adapter.Installed}; повторная установка сохраняет состояние.");
                bool rejected=false;
                try {RoomAuthoring.Load("factory:0",world);} catch(InvalidOperationException) {rejected=true;}
                if(!rejected) throw new InvalidOperationException("Чтение неподготовленного завода не отклонено.");
                FactoryContentSources.Prepare(world,"factory:0");
                var cached=RoomAuthoring.Load("factory:0",world);
                FactoryContentSources.Prepare(StateJson.Copy(world),"factory:0");
                if(cached!=RoomAuthoring.Load("factory:0",world)) throw new InvalidOperationException("Повторная подготовка пересоздала источник завода.");
                if(RoomDoorContent.Read(cached,"factory:0").doors.Length!=adapter.Installed)
                    throw new InvalidOperationException("Подготовленный источник потерял двери.");
                var identity=FactoryContentIdentity.Capture(world);
                var copy=StateJson.Copy(world);
                if(!identity.Matches(copy)) throw new InvalidOperationException("Идентичность заводов изменилась после копирования мира.");
                copy.seed++;
                if(identity.Matches(copy)) throw new InvalidOperationException("Изменение сида мира не обнаружено.");
                copy.seed=world.seed;
                copy.factories[0].layout+=" ";
                if(identity.Matches(copy)) throw new InvalidOperationException("Изменение сохранённой раскладки не обнаружено.");
                world.actors.Add(new ActorState{id="factory-check",player=true,connected=true,level="factory:0"});
                int loads=0;
                using var simulation=new SimulationGeometry(level=>{loads++;return root.Authoring.CreateLayout(false);});
                simulation.Reconcile(world);
                world.factories[0].layout+=" ";
                simulation.Reconcile(world);
                if(loads!=2) throw new InvalidOperationException("Кэш физики не обновился при смене сохранённой раскладки.");
                rejected=false;
                try {RoomAuthoring.Load("factory:0",world);} catch(InvalidOperationException) {rejected=true;}
                if(!rejected) throw new InvalidOperationException("Чтение изменённой раскладки использовало прежний источник.");
                Debug.Log("Источники игрового завода: PASS; чтение только после подготовки, повтор использует один источник, смена раскладки отклоняет устаревший кэш.");
                Debug.Log($"Корень игрового завода: PASS; комнат {root.Rooms.childCount}, объектов {root.Objects.Count}, геометрия из общей сетки; кэш обнаруживает смену раскладки.");
            }
            finally {FactoryContentSources.Clear();UnityEngine.Object.DestroyImmediate(root.gameObject);}
        }
    }
}
