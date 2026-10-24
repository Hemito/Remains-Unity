using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class LampContentChecks
    {
        public static void RunBatch()
        {
            GameObject visual=null;
            try
            {
                Anatomy.Load();WeaponCatalog.Load();
                var world=new WorldState{seed=104};
                var catalog=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath);
                FactoryPreparation.Create(world,catalog,0);
                var source=LampContent.Read(world,"factory:0");
                LampOperations.Install(world,"factory:0",source);
                var room=RoomAuthoring.Load("factory:0",world).CreateLayout(false);
                visual=AuthoredRoomVisual.Create("factory:0",room,world);
                var pictures=visual.GetComponent<FactoryLevelView>().LampPlacements;
                if(source.Count!=pictures.Count || source.Any(lamp=>!pictures.Any(p=>p.id==lamp.id && p.Bounds==lamp.Bounds)))
                    throw new InvalidOperationException("Авторитетные границы ламп отличаются от изображений.");
                var hub=LampContent.Read(world,"hub");
                if(hub.Count==0 || hub.Any(lamp=>lamp.level!="hub" || !lamp.id.StartsWith("hub-room-0-0:")))
                    throw new InvalidOperationException("ID ламп хаба не согласованы с игровым уровнем.");
                var prefab=GameAssets.Load<LampBreakEffectView>("Presentation/Effects/lamp-break");
                prefab.Validate();
                Debug.Log($"LampContentChecks: PASS — {source.Count} ламп завода совпадают с изображениями, {hub.Count} ламп хаба, авторский эффект готов.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally {if(visual!=null) UnityEngine.Object.DestroyImmediate(visual);FactoryContentSources.Clear();}
        }
    }
}
