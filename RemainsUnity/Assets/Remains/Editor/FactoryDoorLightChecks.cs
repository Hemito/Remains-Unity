using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug=UnityEngine.Debug;

namespace Remains.Editor
{
    public static class FactoryDoorLightChecks
    {
        public static void RunBatch()
        {
            GameObject visual=null;
            try
            {
                // The factory content (mines, loot) reads the weapon catalog, as in the game.
                Anatomy.Load();WeaponCatalog.Load();
                var catalog=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath);
                var world=new WorldState{seed=104};
                FactoryPreparation.Create(world,catalog,0);
                FactoryContentSources.Prepare(world,"factory:0");
                var source=RoomAuthoring.Load("factory:0",world);
                DoorContentInstaller.Apply(world,RoomDoorContent.Read(source,"factory:0"));
                var room=source.CreateLayout(false);
                visual=AuthoredRoomVisual.Create("factory:0",room,world);
                var view=visual.GetComponent<FactoryLevelView>();
                CheckFactoryConsumers(visual,world,room);
                view.Synchronize(room,world,1);
                view.Focus(new Vector2(room.spawnX,room.spawnY));
                var field=(RoomRadianceField)typeof(FactoryLevelView).GetField("field",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
                int instance=view.LightFieldInstanceId,readbacks=view.LightSilhouetteReadbacks;
                var states=world.doors.Where(door=>door.level==room.id).ToArray();
                var hatch=states.Where(door=>door.definition=="hatch1").OrderBy(door=>Vector2.Distance(door.Bounds.center,new Vector2(room.spawnX,room.spawnY))).First();
                bool initial=hatch.open;
                double closedAlpha=0,openAlpha=0;
                for(int i=0;i<4;i++)
                {
                    hatch.open=i%2==0;
                    var timer=Stopwatch.StartNew();
                    view.SyncDoors(states,2+i);
                    double alpha=ReadAlpha(field);
                    timer.Stop();
                    if(view.LightFieldInstanceId!=instance || view.LightSilhouetteReadbacks!=readbacks || !view.IsReady || view.AppliedDoorRevision!=2+i)
                        throw new InvalidOperationException("Обновление люка пересоздало поле, повторило чтение силуэта или потеряло готовность/ревизию.");
                    if(hatch.open) openAlpha=alpha;else closedAlpha=alpha;
                    Debug.Log($"Люк: open={hatch.open}, обновление света с GPU-синхронизацией {timer.Elapsed.TotalMilliseconds:F2} мс, поле {instance}, чтений силуэтов {readbacks}, alpha={alpha:F2}.");
                }
                hatch.open=initial;
                if(!(closedAlpha>openAlpha)) throw new InvalidOperationException("Открытый люк не убрал свою альфу препятствия из сцены света.");
                MuzzleFlashLightingChecks.Run();
                FurnitureLightChecks.Run();
                Debug.Log("FactoryDoorLightChecks: PASS — одно поле, никаких повторных readback силуэтов, закрытие/открытие меняет препятствия, вспышки и мебель.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally
            {
                if(visual!=null) UnityEngine.Object.DestroyImmediate(visual);
                FactoryContentSources.Clear();
            }
        }
        private static void CheckFactoryConsumers(GameObject visual,WorldState world,RoomLayout room)
        {
            var factory=visual.GetComponent<FactoryLevelRoot>();
            var sourceDoors=RoomDoorContent.Read(FactoryContentSources.Require(world,room.id),room.id).doors;
            var states=world.doors.Where(door=>door.level==room.id).ToArray();
            var geometry=DoorGeometry.Compose(room.platforms,states,room.id);
            foreach(var source in sourceDoors)
            {
                var door=states.SingleOrDefault(value=>value.id==source.id);
                if(door==null || door.Bounds!=source.Bounds)
                    throw new InvalidOperationException($"Дверь завода: источник и состояние расходятся: ID={source.id}, source={source.Bounds}, state={door?.Bounds}.");
                var surfaces=geometry.Where(surface=>surface.sourceDoorId==door.id).ToArray();
                if(surfaces.Length!=(door.Blocks?1:0) || door.Blocks &&
                    (surfaces[0].x!=door.x || surfaces[0].y!=door.y || surfaces[0].width!=door.width || surfaces[0].height!=door.height))
                    throw new InvalidOperationException($"Дверь завода: препятствие расходится с состоянием: ID={door.id}, bounds={door.Bounds}, open={door.open}, destroyed={door.Destroyed}, surfaces={surfaces.Length}.");
            }
            foreach(var picture in factory.Rooms.GetComponentsInChildren<RoomSpriteAuthoring>(true))
            {
                var renderer=picture.GetComponent<SpriteRenderer>();
                var owner=RenderSorting.FindOwner(renderer);
                if(owner==null || owner.gameObject!=visual || owner.sortingLayerID!=0 || owner.sortingOrder!=WorldVisualOrderPolicy.SceneryRoomOrder)
                    throw new InvalidOperationException($"Порядок завода: картинка {picture.name}, type={picture.Definition}, index={picture.Index}, order={renderer.sortingOrder}, owner={owner?.name} вне общей группы декораций.");
                if(picture.Kind!=RoomSpriteKind.Object || picture.Definition is not ("remains:obj:door1" or "remains:obj:door2" or "remains:obj:door3" or "remains:obj:hatch1")) continue;
                if(renderer.enabled) throw new InvalidOperationException($"Дверь завода: статическое изображение не скрыто: {picture.Definition}, index={picture.Index}, room={picture.transform.parent.parent.name}.");
            }
            Debug.Log($"Источники завода: PASS — {sourceDoors.Length} дверей, включая левый столбец и зеркала: одинаковые ID/границы состояния и препятствий; все картинки декора в общей группе, статические двери скрыты.");
        }
        private static double ReadAlpha(RoomRadianceField field)
        {
            var scene=(RenderTexture)typeof(RoomRadianceField).GetField("scene",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(field);
            var previous=RenderTexture.active;
            var copy=new Texture2D(scene.width,scene.height,TextureFormat.RGBAHalf,false,true);
            try
            {
                // Read output too: this fences the submitted cascade work, not just the scene copy.
                RenderTexture.active=(RenderTexture)Shader.GetGlobalTexture("_RemainsRadiance");
                copy.ReadPixels(new Rect(0,0,scene.width,scene.height),0,0);
                RenderTexture.active=scene;copy.ReadPixels(new Rect(0,0,scene.width,scene.height),0,0);
                double total=0;foreach(var pixel in copy.GetPixels()) total+=pixel.a;
                return total;
            }
            finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(copy);}
        }
    }
}
