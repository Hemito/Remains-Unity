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
                var propPaths=new[]{"Assets/Remains/RoomDrafts/woodbox.asset","Assets/Remains/RoomDrafts/FactoryProps/bigbox.asset",
                    "Assets/Remains/RoomDrafts/mcrate4.asset","Assets/Remains/Resources/Content/props/box.asset",
                    "Assets/Remains/Resources/Content/props/mcrate1.asset","Assets/Remains/Resources/Content/props/mcrate2.asset",
                    "Assets/Remains/Resources/Content/props/mcrate3.asset"};
                root.AddComponent<FactoryPropAdapter>().Configure(propPaths.Select(path=>AssetDatabase.LoadAssetAtPath<PropAsset>(path)
                    ??throw new InvalidOperationException("Нет готового определения физического объекта: "+path)).ToArray());
                var view=root.AddComponent<FactoryLevelView>();
                root.AddComponent<FactoryLightFocus>().Configure(view);
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath,out bool saved);
                if(!saved) throw new IOException("Не записан корень завода: "+PrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            FactoryEntranceAuthoring.Install();
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
                var layout=root.Prepared.Grid.Layout;
                string stored=world.factories[0].layout;
                var read=Remains.RoomFormat.FactoryLayoutReader.Read(stored,"проверка точек ролей");
                if(read.Write()!=stored || root.RolePoints.Count!=layout.Cells.Count(cell=>cell.Role!=Remains.RoomFormat.FactoryCellRole.None) ||
                    root.RolePoints.Any(point=>point.Cell.RolePoint!=point.Marker.Source.Id || point.Position!=point.Marker.Position))
                    throw new InvalidOperationException("Сохранённые точки ролей не совпали с исходными маркерами.");
                var generated=Remains.RoomFormat.FactoryLayoutGenerator.Generate(catalog.Templates.Values.ToArray(),"plant",104,0,4,21);
                if(generated.Write()!=stored) throw new InvalidOperationException("Точки ролей не воспроизводятся тем же seed и версией генератора.");
                var role=read.Cells.First(cell=>cell.Role!=Remains.RoomFormat.FactoryCellRole.None);
                string savedPoint=role.RolePoint;role.RolePoint=null;
                bool pointRejected=false;
                try {Remains.RoomFormat.FactoryLayoutReader.Read(read.Write(),"отсутствующий маркер роли");} catch(FormatException) {pointRejected=true;}
                if(!pointRejected) throw new InvalidOperationException("Отсутствующий сохранённый маркер роли не отклонён.");
                role.RolePoint="missing-role-marker";
                var invalid=StateJson.Copy(world);invalid.factories[0].layout=read.Write();pointRejected=false;
                try {FactoryPreparation.Prepare(invalid,catalog,"factory:0");} catch(InvalidOperationException) {pointRejected=true;}
                if(!pointRejected) throw new InvalidOperationException("Несуществующий исходный маркер роли не отклонён.");
                role.RolePoint=savedPoint;read.GeneratorVersion=1;
                invalid.factories[0].layout=read.Write();string legacy=invalid.factories[0].layout;pointRejected=false;
                try {FactoryPreparation.Prepare(invalid,catalog,"factory:0");} catch(System.IO.InvalidDataException) {pointRejected=true;}
                if(!pointRejected || invalid.factories[0].layout!=legacy) throw new InvalidOperationException("Старая версия генератора не отклонена или была перезаписана.");
                Debug.Log($"Точки ролей завода: PASS; генератор {layout.GeneratorVersion}, точек {root.RolePoints.Count}, выходов {root.RolePoints.Count(point=>point.Cell.Role==Remains.RoomFormat.FactoryCellRole.Exit)}; выбор сохранён, отсутствующий/чужой маркер и старая версия отклонены без перегенерации.");
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
                var props=root.GetComponent<FactoryPropAdapter>();props.Install(root);
                var physical=RoomPhysicalContent.Read(root.Authoring,"factory:0");
                if(physical.props.Length!=props.Installed || props.Installed==0 || physical.containers.Length!=0 ||
                    physical.props.Any(prop=>!root.Objects.Any(obj=>obj.Id==prop.id && obj.Position==new Vector2(prop.x,prop.y))))
                    throw new InvalidOperationException("Адаптер физических объектов потерял ID, позицию или подставил контейнер.");
                var entityCatalog=EntityAssetCatalog.Load();
                var registered=entityCatalog.CreateProps().ToDictionary(prop=>prop.id,StringComparer.Ordinal);
                foreach(var prop in physical.props)
                    if(!registered.TryGetValue(prop.definition,out var definition) || definition.width!=prop.width || definition.height!=prop.height ||
                        definition.mass!=prop.mass || definition.movable!=prop.movable || definition.hasPhysicalBody!=prop.hasPhysicalBody ||
                        !entityCatalog.TryGetPropPrefab(definition.id,definition.resource,out var prefab) || prefab==null)
                        throw new InvalidOperationException("Игровой каталог не соответствует расстановке: "+prop.id+", type="+prop.definition);
                PhysicalContentInstaller.Apply(world,physical);
                var savedProp=world.props[0];savedProp.x+=.25f;savedProp.vx=2;
                if(PhysicalContentInstaller.Apply(world,physical) || savedProp.vx!=2 || savedProp.x!=physical.props[0].x+.25f)
                    throw new InvalidOperationException("Повтор расстановки сбросил сохранённое движение физического объекта.");
                Debug.Log($"Физические объекты завода: PASS; объектов {props.Installed}, исходные ID/позиции, повтор сохраняет движение.");
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
