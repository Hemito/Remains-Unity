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
                Validate(root);PrefabUtility.SaveAsPrefabAsset(root,Draft);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            Verify();
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
        private static void Validate(GameObject root)
        {
            var room=root.GetComponent<RoomAuthoring>();var props=root.GetComponentsInChildren<RoomPropPlacement>(true);
            var actors=root.GetComponentsInChildren<RoomActorPlacement>(true);
            if(props.Length!=4 || actors.Length!=1 || root.GetComponentsInChildren<RoomContainerPlacement>(true).Length!=0)
                throw new InvalidOperationException("Расстановка смешала физические ящики с контейнерами или потеряла объекты.");
            if(!props.Select(p=>p.Definition.CreateDefinition().mass).OrderBy(m=>m).SequenceEqual(new[]{15f,15f,15f,50f}) ||
                props.Select(p=>p.Read(room).id).Distinct().Count()!=4 || actors[0].Read(room).type!="raider")
                throw new InvalidOperationException("Не совпадают ID, массы или тип рейдера.");
        }
        public static void Verify()
        {
            ActorTypes.Load();Anatomy.Load();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Draft));
            try
            {
                Validate(root);var room=root.GetComponent<RoomAuthoring>();var layout=room.CreateLayout();
                var placement=root.GetComponentInChildren<RoomActorPlacement>().Read(room);
                var actor=new ActorState {id=placement.id,actorType=placement.type,anatomy="raider",x=placement.x,y=placement.y,facing=placement.facing,weapon=null};
                var preview=ActorVisualFactory.Create(actor);preview.transform.SetParent(root.transform,false);preview.Bind(actor);
                root.AddComponent<RoomLighting>().Initialize(layout,root.GetComponent<RoomLightingAuthoring>().CreateProfile(room));
                OriginalTrainingRoomAuthoring.Render(root,layout,"factory-entry-46-actors.png");
                Debug.Log("Расстановка входа: PASS — рейдер и четыре физических ящика, 15/15/15/50 кг, ID и типы сохранены. Остальные объекты ещё не импортированы.");
            }
            finally {root.GetComponent<RoomLighting>()?.Release();UnityEngine.Object.DestroyImmediate(root);}
            ContractChecks.Run();
        }
    }
}
