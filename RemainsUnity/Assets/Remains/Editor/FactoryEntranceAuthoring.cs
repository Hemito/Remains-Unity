using System;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryEntranceAuthoring
    {
        public static void RunBatch()
        {
            int code=0;
            try {Install();Check();}
            catch(Exception error) {Debug.LogError("Подготовка входа завода прервана: "+error);code=1;}
            EditorApplication.Exit(code);
        }
        public static void Install()
        {
            const string path="Assets/Remains/Resources/Content/FactoryLevelRoot.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                if(root.GetComponent<FactoryEntrance>()!=null) return;
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Remains/Resources/Presentation/Travel/exit.asset")
                    ??throw new InvalidOperationException("Нет подготовленного изображения выходной двери.");
                var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Remains/Resources/Presentation/Travel/exit.mat")
                    ??throw new InvalidOperationException("Нет материала выходной двери.");
                var node=new GameObject("Вход завода · На выход");
                node.transform.SetParent(root.transform,false);
                var image=node.AddComponent<SpriteRenderer>();
                image.sprite=sprite;image.sharedMaterial=material;image.sortingOrder=ActorVisualLayers.WallFixtureOrder;
                var point=node.AddComponent<RoomTravelPointAuthoring>();
                point.Configure("factory:0:entrance:hub-exit",TravelPointKind.Exit,"На выход","hub",
                    "original:hub:worldMap",sprite.bounds.size,Vector2.zero);
                root.AddComponent<FactoryEntrance>().Configure(point);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        public static void Check()
        {
            Anatomy.Load();
            var world=new WorldState{worldId="factory-entrance-check",seed=104};
            var catalog=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath);
            FactoryPreparation.Create(world,catalog,0);
            string before=StateJson.ToJson(world);
            FactoryContentSources.Prepare(world,"factory:0");
            try
            {
                var source=FactoryContentSources.Require(world,"factory:0");
                var points=RoomTravelPointAuthoring.ReadAll(source,"factory:0");
                if(points.Length!=1) throw new InvalidOperationException("У входа завода должна быть одна выходная дверь.");
                var point=points[0];
                var room=source.CreateLayout();
                var actor=new ActorState{connected=true,level="factory:0",x=room.spawnX,y=room.spawnY};
                if(point.id!="factory:0:entrance:hub-exit" || point.destination!="hub" || point.title!="На выход" ||
                    point.Rejection(actor)!=null || FactoryArrival.Rejection(room,Anatomy.Movement(actor),point.Arrival)!=null)
                    throw new InvalidOperationException("Неверный выход или недоступная точка входа завода.");
                actor.x+=20;
                if(point.Rejection(actor)==null) throw new InvalidOperationException("Выход разрешён издалека.");
                var hub=RoomAuthoring.Load("hub",world);
                var target=Array.Find(RoomTravelPointAuthoring.ReadAll(hub,"hub"),p=>p.id==point.arrivalPoint);
                if(target==null || FactoryArrival.Rejection(hub.CreateLayout(),Anatomy.Movement(actor),target.Arrival)!=null)
                    throw new InvalidOperationException("Выход завода не имеет безопасного прибытия в хаб.");
                FactoryContentSources.Prepare(world,"factory:0");
                if(StateJson.ToJson(world)!=before || RoomTravelPointAuthoring.ReadAll(source,"factory:0").Length!=1)
                    throw new InvalidOperationException("Установка выхода меняет мир или дублирует дверь.");
                Debug.Log("Вход завода: PASS; одна видимая дверь, доступна у входа, запрещена издалека, прибытие в хаб свободно, повтор без дублирования/изменения мира.");
            }
            finally {FactoryContentSources.Clear();}
        }
    }
}
