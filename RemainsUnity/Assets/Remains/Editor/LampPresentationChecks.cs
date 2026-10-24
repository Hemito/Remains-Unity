using System;
using System.Linq;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class LampPresentationChecks
    {
        public static void RunBatch()
        {
            GameObject visual=null;
            try
            {
                LampBreakEffectAuthoring.Generate();
                LampGameplayChecks.Run();
                var catalog=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath);
                var world=new WorldState{seed=104};
                FactoryPreparation.Create(world,catalog,0);
                FactoryContentSources.Prepare(world,"factory:0");
                var source=RoomAuthoring.Load("factory:0",world);
                DoorContentInstaller.Apply(world,RoomDoorContent.Read(source,"factory:0"));
                var room=source.CreateLayout(false);
                visual=AuthoredRoomVisual.Create("factory:0",room,world);
                var view=visual.GetComponent<FactoryLevelView>();
                world.lamps.AddRange(view.LampPlacements.Select(item=>new LampState{id=item.id,level=item.level,x=item.x,y=item.y,width=item.width,height=item.height}));
                Require(world.lamps.Count>0 && world.lamps.All(lamp=>lamp.width>0 && lamp.height>0),"Нет полных размещений ламп.");
                var focus=world.lamps[0].Bounds.center;
                view.Synchronize(room,world,1);view.Focus(focus);
                int original=view.LightFieldInstanceId;
                int readbacks=view.LightSilhouetteReadbacks;
                double lit=Emission(view);
                world.lampRevision++;
                view.Synchronize(room,world,1);
                Require(view.LightFieldInstanceId==original && view.AppliedLampRevision==world.lampRevision,"Пустое изменение ревизии пересоздало поле.");
                view.SyncLamps(view.LampIds.ToArray(),++world.lampRevision);
                var brokenArt=GameAssets.Load<BrokenLampArt>(BrokenLampArt.ResourcePath);
                foreach(var picture in view.GetComponentsInChildren<RoomSpriteAuthoring>(true))
                    if(brokenArt.TryGet(picture.Definition,out _))
                    {
                        var definition=catalog.Definitions.Require(picture.Definition,"проверка обычного выключения лампы");
                        int expectedFrame=definition.DecorFrameOff>0?definition.DecorFrameOff:picture.Frame;
                        var expected=catalog.Art.Tile(TileManifest.DecorFrameId(definition.Art,expectedFrame)).sprite;
                        Require(picture.GetComponent<SpriteRenderer>().sprite==expected,$"Лампа {picture.Definition}: обычное выключение не сохранило исходный off-кадр.");
                    }
                foreach(var lamp in world.lamps) {lamp.broken=true;lamp.revision=++world.lampRevision;}
                view.Synchronize(room,world,1);
                double dark=Emission(view);
                CheckBrokenArtwork(view);
                Require(view.LightFieldInstanceId==original && view.LightSilhouetteReadbacks==readbacks,"Разрушение лампы пересоздало поле или повторило чтение силуэтов.");
                Require(view.AppliedDoorRevision==1 && view.AppliedLampRevision==world.lampRevision && lit>dark,"Лампы не выключились при неизменной ревизии дверей.");
                int broken=view.LightFieldInstanceId;
                view.Synchronize(room,world,1);
                Require(view.LightFieldInstanceId==broken,"Повторный снимок ламп пересоздал поле.");
                view.Focus(focus+new Vector2(0,-60));view.Focus(focus);
                CheckBrokenArtwork(view);
                Require(Math.Abs(Emission(view)-dark)<.01,"Смена окна воскресила свет лампы.");
                UnityEngine.Object.DestroyImmediate(visual);visual=null;
                world=Remains.Serialization.StateJson.Copy(world);
                visual=AuthoredRoomVisual.Create("factory:0",room,world);view=visual.GetComponent<FactoryLevelView>();
                view.Synchronize(room,world,1);view.Focus(focus);
                CheckBrokenArtwork(view);
                Require(Math.Abs(Emission(view)-dark)<.01 && view.AppliedLampRevision==world.lampRevision,"Повторный вход/сериализация воскресили лампы.");
                Debug.Log($"LampPresentationChecks: PASS — {world.lamps.Count} ламп, эмиссия {lit:F3} → {dark:F3}; ревизия без дверей, повтор, смена окна и повторный вход.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally {if(visual!=null) UnityEngine.Object.DestroyImmediate(visual);FactoryContentSources.Clear();}
        }
        private static double Emission(FactoryLevelView view)
        {
            var field=(RoomRadianceField)typeof(FactoryLevelView).GetField("field",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
            var scene=(RenderTexture)typeof(RoomRadianceField).GetField("scene",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(field);
            var previous=RenderTexture.active;
            var pixels=new Texture2D(scene.width,scene.height,TextureFormat.RGBAHalf,false,true);
            try
            {
                RenderTexture.active=scene;pixels.ReadPixels(new Rect(0,0,scene.width,scene.height),0,0);
                return pixels.GetPixels().Sum(color=>(double)color.r+color.g+color.b);
            }
            finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
        private static void CheckBrokenArtwork(FactoryLevelView view)
        {
            var catalog=GameAssets.Load<BrokenLampArt>(BrokenLampArt.ResourcePath);
            Require(catalog!=null,"Нет каталога разбитых ламп.");
            int checkedPictures=0;
            foreach(var picture in view.GetComponentsInChildren<RoomSpriteAuthoring>(true))
                if(catalog.TryGet(picture.Definition,out var expected))
                {
                    Require(picture.GetComponent<SpriteRenderer>().sprite==expected,$"Лампа {picture.Definition}: разбитый спрайт не сохранился при синхронизации/повторном входе.");
                    checkedPictures++;
                }
            Require(checkedPictures>0,"В заводе не проверено ни одной разбитой картинки лампы.");
        }
    }
}
