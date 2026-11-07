using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class LampContentChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
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
                CheckPassages(world,catalog,source);
                var room=RoomAuthoring.Load("factory:0",world).CreateLayout(false);
                visual=AuthoredRoomVisual.Create("factory:0",room,world);
                var view=visual.GetComponent<FactoryLevelView>();
                var pictures=view.LampPlacements;
                if(source.Count!=pictures.Count || source.Any(lamp=>!pictures.Any(p=>p.id==lamp.id && p.Bounds==lamp.Bounds)))
                    throw new InvalidOperationException("Авторитетные границы ламп отличаются от изображений.");
                const string removed="factory:0:0,10:uW0abVdIRuDi3Dhq";
                var root=visual.GetComponent<FactoryLevelRoot>();
                int roomIndex=root.Prepared.Grid.Rooms.FindIndex(placement=>placement.Cell.X==0 && placement.Cell.Y==10);
                int index=root.Prepared.Grid.Rooms[roomIndex].Template.Decor.FindIndex(item=>item.Id=="uW0abVdIRuDi3Dhq");
                var artwork=root.Rooms.GetChild(roomIndex).GetComponentsInChildren<RoomSpriteAuthoring>(true)
                    .Single(picture=>picture.Kind==Remains.RoomFormat.RoomSpriteKind.Decor && picture.Index==index);
                view.Synchronize(room,world,0);
                view.Focus(new Vector2(36.4f,210));
                if(artwork.GetComponent<SpriteRenderer>().enabled || view.Light.Sources.TryGet(removed,out _))
                    throw new InvalidOperationException("Исключённая лампа осталась в графике или источниках света.");
                var hub=LampContent.Read(world,"hub");
                if(hub.Count==0 || hub.Any(lamp=>lamp.level!="hub" || !lamp.id.StartsWith("hub-room-0-0:")))
                    throw new InvalidOperationException("ID ламп хаба не согласованы с игровым уровнем.");
                var prefab=GameAssets.Load<LampBreakEffectView>("Presentation/Effects/lamp-break");
                prefab.Validate();
                Debug.Log($"LampContentChecks: PASS — {source.Count} ламп завода совпадают с изображениями, {hub.Count} ламп хаба, авторский эффект готов.");
            }
            finally {if(visual!=null) UnityEngine.Object.DestroyImmediate(visual);FactoryContentSources.Clear();}
        }
        private static void CheckPassages(WorldState world,FactoryCatalogAsset catalog,System.Collections.Generic.List<LampState> source)
        {
            const string level="factory:0",excluded="factory:0:0,10:uW0abVdIRuDi3Dhq",retained="factory:0:1,3:MYgSXni4C2GLJMdw";
            if(source.Count!=433 || source.Any(lamp=>lamp.id==excluded) || !source.Any(lamp=>lamp.id==retained))
                throw new InvalidOperationException("На заводе 0 не исключены пять ламп в запретных зонах или удалена соседняя лампа.");
            var old=new LampState {id=excluded,level=level,x=35.9f,y=209.5f,width=1,height=1};
            world.lamps.Add(old);LampOperations.Break(world,old,"old-shot");
            var kept=world.lamps.Single(lamp=>lamp.id==retained);LampOperations.Break(world,kept,"keep-shot");
            var ticks=new WorldTickComposer();
            ticks.Next(world,part=>WorldChanges.IsMarked(world,part));
            long revision=world.lampRevision;
            if(!LampContent.Install(world,level) || LampContent.Install(world,level) || world.lampRevision!=revision+1 ||
                world.lamps.Contains(old) || !world.lamps.Contains(kept) || !kept.broken ||
                !ticks.Next(world,part=>WorldChanges.IsMarked(world,part)).lists.Single(list=>list.name=="lamps").removed.Contains("lamps:"+excluded))
                throw new InvalidOperationException("Старое сохранение или следующий тик оставил лампу в проходе.");
            FactoryPreparation.Create(world,catalog,-12);
            if(LampContent.Read(world,"factory:-12").Count!=425)
                throw new InvalidOperationException("На заводе −12 не исключены девять ламп в запретных зонах.");
        }
    }
}
