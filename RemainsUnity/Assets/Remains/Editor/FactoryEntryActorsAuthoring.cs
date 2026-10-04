using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryEntryActorsAuthoring
    {
        private const string Draft="Assets/Remains/RoomDrafts/FactoryEntry46.prefab";
        public static void InstallNpcVersion()
        {
            const string path="Assets/Remains/RoomDrafts/FactoryRaiderSpawn.asset";
            if(AssetDatabase.LoadAssetAtPath<NpcSpawnAsset>(path)!=null) throw new InvalidOperationException("Начальное состояние рейдера уже создано.");
            var spawn=ScriptableObject.CreateInstance<NpcSpawnAsset>();spawn.Configure("raider","raider",140,"pistol");
            AssetDatabase.CreateAsset(spawn,path);
            var root=PrefabUtility.LoadPrefabContents(Draft);
            try
            {
                root.GetComponentInChildren<RoomActorPlacement>().SetSpawn(spawn);
                if(root.GetComponent<RoomNpcContent>()!=null) throw new InvalidOperationException("Версия NPC уже задана.");
                root.AddComponent<RoomNpcContent>().Configure("base:factory-entry-46:npc-v1");
                PrefabUtility.SaveAsPrefabAsset(root,Draft);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            VerifyNpcContent();ContractChecks.Run();
        }
        public static void VerifyNpcContent()
        {
            ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            var room=AssetDatabase.LoadAssetAtPath<GameObject>(Draft).GetComponent<RoomAuthoring>();
            var world=new WorldState();
            foreach(string level in new[]{"factory:0","factory:6"})
            {
                var layout=RoomNpcContent.Read(room,level);
                if(layout.actors.Length!=1) throw new InvalidOperationException("Потерян рейдер расстановки.");
                var actor=layout.actors[0];_=ActorTypes.Get(actor.actorType);_=Anatomy.Movement(actor.anatomy);_=WeaponCatalog.Get(actor.weapon);
                NpcContentInstaller.Apply(world,layout);
            }
            if(world.actors.Count!=2 || world.actors.Select(a=>a.id).Distinct().Count()!=2 || world.actors.Any(a=>a.owner!=null || a.x!=32 || a.y!=5.6f))
                throw new InvalidOperationException("NPC смешал экземпляры фабрик, владельцев или исходные позиции.");
            Debug.Log("Рейдер завода: PASS — начальное состояние из SO, независимые фабрики, владелец не назначен авторингом.");
        }
        public static void InstallPhysicalVersion()
        {
            var root=PrefabUtility.LoadPrefabContents(Draft);
            try
            {
                if(root.GetComponent<RoomPhysicalContent>()!=null) throw new InvalidOperationException("Версия физических объектов уже задана.");
                root.AddComponent<RoomPhysicalContent>().Configure("base:factory-entry-46:physical-v1");
                var room=root.GetComponent<RoomAuthoring>();
                var main=RoomPhysicalContent.Read(room,"factory:0");
                var other=RoomPhysicalContent.Read(room,"factory:6");
                var world=new WorldState();PhysicalContentInstaller.Apply(world,main);PhysicalContentInstaller.Apply(world,other);
                if(world.props.Count!=8 || world.containers.Count!=0 || world.props.Select(p=>p.id).Distinct().Count()!=8 ||
                    main.props.Any(p=>p.anchorHeight!=0)) throw new InvalidOperationException("Расстановка смешала фабрики, контейнеры или привязки.");
                PrefabUtility.SaveAsPrefabAsset(root,Draft);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            ContractChecks.Run();
            Debug.Log("Вход завода: четыре физических ящика устанавливаются независимо в фабрики 0 и 6.");
        }
        public static void Import()
        {
            ActorTypes.Load();Anatomy.Load();
            var root=PrefabUtility.LoadPrefabContents(Draft);
            try
            {
                if(root.GetComponentsInChildren<RoomActorPlacement>(true).Length!=0 || root.GetComponentsInChildren<RoomPropPlacement>(true).Length!=0)
                    throw new InvalidOperationException("Расстановка уже существует; ручные правки не заменены.");
                var wood=AssetDatabase.LoadAssetAtPath<PropAsset>("Assets/Remains/RoomDrafts/woodbox.asset");
                var box=AssetDatabase.LoadAssetAtPath<PropAsset>("Assets/Remains/Resources/Content/props/box.asset");
                PlaceProp(root,wood,"K0YS99TPt4U3OObU",24,6);
                PlaceProp(root,wood,"fwdvbX970KjMgqxw",1,3);
                PlaceProp(root,wood,"Cyr5Hffbds9A86Xj",3,3);
                PlaceProp(root,box,"jqCqTB25CpNmxL3f",31,11);
                var part=new GameObject("Рейдер · Pca2ee6SiIQvzRpV");part.transform.SetParent(root.transform,false);
                part.transform.localPosition=new Vector3(32f,5.6f,0);
                part.AddComponent<RoomActorPlacement>().Configure("original:plant:46:Pca2ee6SiIQvzRpV","raider",-1,false,null);
                Validate(root,0);PrefabUtility.SaveAsPrefabAsset(root,Draft);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            VerifyInitial();
        }
        private static void PlaceProp(GameObject root,PropAsset asset,string code,int x,int y)
        {
            if(asset==null || asset.Prefab==null) throw new InvalidOperationException("Нет определения физического ящика: "+code);
            var data=asset.CreateDefinition();var part=new GameObject($"{data.name} · {data.mass} кг · {code}");
            part.transform.SetParent(root.transform,false);
            // Source records occupy two cells horizontally. Their anchor is the bottom centre.
            part.transform.localPosition=new Vector3((x+1)*.8f,(25-y-1)*.8f,0);
            part.AddComponent<RoomPropPlacement>().Configure("original:plant:46:"+code,asset);
            var preview=(GameObject)PrefabUtility.InstantiatePrefab(asset.Prefab.gameObject,part.transform);
            preview.name="Предпросмотр физического ящика";preview.transform.localPosition=new Vector3(0,data.height*.5f,0);
        }
        private static void Validate(GameObject root,int containers)
        {
            var room=root.GetComponent<RoomAuthoring>();var props=root.GetComponentsInChildren<RoomPropPlacement>(true);
            var actors=root.GetComponentsInChildren<RoomActorPlacement>(true);
            if(props.Length!=4 || actors.Length!=1 || root.GetComponentsInChildren<RoomContainerPlacement>(true).Length!=containers)
                throw new InvalidOperationException("Расстановка смешала физические ящики с контейнерами или потеряла объекты.");
            if(!props.Select(p=>p.Definition.CreateDefinition().mass).OrderBy(m=>m).SequenceEqual(new[]{15f,15f,15f,50f}) ||
                props.Select(p=>p.Read(room).id).Distinct().Count()!=4 || actors[0].Read(room).type!="raider")
                throw new InvalidOperationException("Не совпадают ID, массы или тип рейдера.");
        }
        public static void Verify()=>VerifyStage(8);
        private static void VerifyInitial()=>VerifyStage(0);
        private static void VerifyStage(int containers)
        {
            ActorTypes.Load();Anatomy.Load();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Draft));
            try
            {
                Validate(root,containers);var room=root.GetComponent<RoomAuthoring>();var layout=room.CreateLayout();
                var placement=root.GetComponentInChildren<RoomActorPlacement>().Read(room);
                var actor=new ActorState {id=placement.id,actorType=placement.type,anatomy="raider",x=placement.x,y=placement.y,facing=placement.facing,weapon=null};
                var preview=ActorVisualFactory.Create(actor);preview.transform.SetParent(root.transform,false);preview.Bind(actor);
                root.AddComponent<RoomLighting>().Initialize(layout,root.GetComponent<RoomLightingAuthoring>().CreateProfile(room));
                OriginalTrainingRoomAuthoring.Render(root,layout,"factory-entry-46-actors.png");
                Debug.Log($"Расстановка входа: PASS — рейдер, четыре физических ящика, контейнеров: {containers}; ID и типы сохранены. Остальные объекты ещё не импортированы.");
            }
            finally {root.GetComponent<RoomLighting>()?.Release();UnityEngine.Object.DestroyImmediate(root);}
            ContractChecks.Run();
        }
    }
}
