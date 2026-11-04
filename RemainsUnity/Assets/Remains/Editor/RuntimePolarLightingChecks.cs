using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Exercises the DEFAULT gameplay composition, not the previous Editor renderer.
    public static class RuntimePolarLightingChecks
    {
        private static string Output=>Path.GetFullPath(Remains.Runtime.Seed104CpuBaselineProbe.Argument("-runtime-light-output",Path.Combine(Application.dataPath,"../../.reference/light-main-20261104")));
        [Serializable] private sealed class Result
        {
            public bool success,defaultBackend,oddScene,pendingFlashRemoved,localEqualsFull,cameraUsesCache,actorMasks;
            public string device,api,detail;
            public int sources,shapePoints,projectedMasks,shadowPixels,flashPixels;
            public double factoryPrepareMs;
            public long levelGpuBytes,fillCpuBytes;
        }
        public static void RunBatch()
        {
            try
            {
                Directory.CreateDirectory(Output);var result=new Result{device=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString()};
                Debug.Log(StationaryPolarChecks.Run());
                CheckSmall(result);CheckFactory(result);
                result.success=true;result.detail="PASS: основной backend, 8слоёв, нечётные размеры, удаление вспышки при ожидающем событии, локальные очереди против полной сборки, мир-кэш при камере, настоящие маски пони/рейдера/крысы и оружия. Player/network проверяются отдельно.";
                File.WriteAllText(Path.Combine(Output,"integration.json"),JsonUtility.ToJson(result,true));
                Debug.Log(result.detail);EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
            finally{FactoryContentSources.Clear();}
        }
        public static void BuildPlayerBatch()
        {
            try
            {
                Directory.CreateDirectory(Output);
                var scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray();
                Require(scenes.Length==1 && AssetDatabase.LoadAssetAtPath<SceneAsset>(scenes[0])!=null,"В сборке должна быть существующая стартовая сцена.");
                var build=BuildPipeline.BuildPlayer(scenes,"Builds/Windows/Remains.exe",BuildTarget.StandaloneWindows64,BuildOptions.Development|BuildOptions.DetailedBuildReport);
                Require(build.summary.result==BuildResult.Succeeded,"Сборка нового света: "+build.summary.result);
                File.WriteAllText(Path.Combine(Output,"player-build.json"),JsonUtility.ToJson(new BuildResultData{success=true,path=Path.GetFullPath("Builds/Windows/Remains.exe"),bytes=build.summary.totalSize},true));
                Debug.Log("Основная Windows-сборка с полярным светом: PASS, Builds/Windows/Remains.exe.");EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        [Serializable] private sealed class BuildResultData{public bool success;public string path;public ulong bytes;}
        private static void CheckSmall(Result result)
        {
            var walls=new float[101,81];var scene=new LevelLightScene(walls,Vector2.zero,10,Array.Empty<LevelLightScene.ObjectSprite>());
            var doors=new[]{new LevelLightScene.Door("check-door",new Rect(5,1,.2f,6),1,true)};scene.SetDoors(doors,null);
            var source=new LevelLightSource("check-lamp",LevelLightSourceKind.Lamp,new[]{new Vector4(3,4,1,0)},new Color(3,2,1),.4f,6,new Rect(2.9f,3.9f,.2f,.2f),new RectInt(0,0,101,81),true);
            var sources=new LevelLightSources(new[]{source},101,81);
            using var light=new LevelLight(scene,sources,.2f);light.BuildAll();
            Require(light.UsesGridFill && light.Direct is PolarDirectLight && light.Bounce==null,"Основной путь не выбрал полярный свет.");result.defaultBackend=true;
            Require(light.Fill.Texture.width==101 && light.Fill.Texture.height==81,"Нечётная fine-сцена потеряла последний ряд.");result.oddScene=true;
            var context=new LightShaderContext();context.Activate();using var view=new LevelLightView(light,context);
            var area=scene.WorldRect;var noFlashes=Array.Empty<SpriteEmission>();var noMasks=Array.Empty<ProjectedLightShadow>();var noIds=Array.Empty<string>();
            view.Present(area,noFlashes,noMasks,noIds);var baseline=ReadGlobal("_RemainsRadiance");
            var texture=new Texture2D(4,4,TextureFormat.RGBA32,false,true);texture.SetPixels(Enumerable.Repeat(new Color(1,.7f,.2f),16).ToArray());texture.Apply();
            var sprite=Sprite.Create(texture,new Rect(0,0,4,4),new Vector2(.5f,.5f),20);
            try
            {
                var flash=new SpriteEmission(sprite,Matrix4x4.TRS(new Vector3(3.5f,3,0),Quaternion.identity,Vector3.one),100);
                view.Present(area,new[]{flash},noMasks,new[]{"check-flash"});
                result.flashPixels=Different(baseline,ReadGlobal("_RemainsRadiance"));Require(result.flashPixels>5,"Вспышка не попала в основной композит.");
                light.SetDark(new[]{source.Id});Require(light.HasWork,"Лампа должна поставить локальное обновление в очередь.");
                view.Present(area,noFlashes,noMasks,noIds);
                Require(Different(baseline,ReadGlobal("_RemainsRadiance"))==0,"Ожидающая лампа оставила вспышку или смешала ревизии.");
                Require(Shader.GetGlobalFloat("_RemainsIndirectEnabled")==0,"Отражения остались включены в основной схеме.");result.pendingFlashRemoved=true;
                Finish(light);view.Present(area,noFlashes,noMasks,noIds);
                Require(Different(baseline,ReadGlobal("_RemainsRadiance"))>5,"Событие лампы не изменило опубликованный свет.");
                // A second event interrupts an incomplete local sum; only the final canonical state may publish.
                light.SetDark(noIds);doors[0]=new("check-door",doors[0].Bounds,1,false);light.SetDoors(doors);light.Step(1,1);
                doors[0]=new("check-door",doors[0].Bounds,1,true);light.SetDoors(doors);
                doors[0]=new("check-door",doors[0].Bounds,1,false);light.SetDoors(doors);Finish(light);
                var localDirect=Read(light.Direct.Sum);var localFill=Read(light.Fill.Texture);
                light.BuildAll();Require(Different(localDirect,Read(light.Direct.Sum))==0,"Прерванная локальная полярная сумма отличается от полной.");
                Require(Different(localFill,Read(light.Fill.Texture))==0,"Локальное мировое заполнение отличается от полного.");result.localEqualsFull=true;
            }
            finally{Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);context.Release();}
        }
        private static void CheckFactory(Result result)
        {
            Anatomy.Load();WeaponCatalog.Load();var world=new WorldState{seed=104};
            FactoryPreparation.Create(world,AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath),0);
            FactoryContentSources.Prepare(world,"factory:0");var authoring=RoomAuthoring.Load("factory:0",world);
            DoorContentInstaller.Apply(world,RoomDoorContent.Read(authoring,"factory:0"));var layout=authoring.CreateLayout(false);
            GameObject visual=null,cameraObject=null,fixture=null;
            try
            {
                visual=AuthoredRoomVisual.Create("factory:0",layout,world);var view=visual.GetComponent<FactoryLevelView>();var factory=visual.GetComponent<FactoryLevelRoot>();view.Synchronize(layout,world,1);
                var clock=Stopwatch.StartNew();view.Focus(new Vector2(layout.spawnX,layout.spawnY));result.factoryPrepareMs=clock.Elapsed.TotalMilliseconds;
                var light=view.Light;Require(light.UsesGridFill && light.Bounce==null,"FactoryLevelView построил старый свет.");
                result.sources=light.Sources.Count;result.shapePoints=light.Sources.All.Sum(s=>s.Samples.Length);
                var p=factory.Prepared.Grid.Rooms.Single(r=>r.Cell.X==1 && r.Cell.Y==1);float cell=(float)factory.Prepared.Grid.Level.Cell;
                var room=new Rect(p.Column*cell,(factory.Prepared.Grid.Level.Height-p.Row-p.Template.Height)*cell,p.Template.Width*cell,p.Template.Height*cell);
                var area=new Rect(room.center-new Vector2(20,10.5f),new Vector2(40,21));view.Focus(area.center);view.SetViewArea(area);
                fixture=new GameObject("Editor: актёры основного света");using var actors=new FactoryWispActors(fixture.transform,layout,room,0);
                var selected=new List<ActorShadowSource>();view.SelectActorShadowSources(actors.DrawnBounds.center,selected);
                Require(selected.Count>0,"В настоящей комнате не выбраны лампы для теней.");
                view.PrepareActorShadowFields(selected.Select(s=>s.Id).ToArray());Finish(light);Require(view.ActorShadowFieldsReady(selected),"Поля теней не подготовились.");
                var masks=new List<ProjectedLightShadow>();actors.AppendProjectedShadows(selected,view.ShadowSettings,masks);result.projectedMasks=masks.Count;
                cameraObject=new GameObject("Editor: основной полярный свет");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.aspect=1280f/672;camera.orthographicSize=10.5f;
                camera.transform.position=new(area.center.x,area.center.y,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                view.RenderMuzzleFlashes(Array.Empty<SpriteEmission>(),Array.Empty<ProjectedLightShadow>(),Array.Empty<string>());
                var front=ReadGlobal("_RemainsRadiance");var noShadow=ReadGlobal("_RemainsBackgroundRadiance");
                Capture("factory-static.png");
                view.RenderMuzzleFlashes(Array.Empty<SpriteEmission>(),masks,Array.Empty<string>());
                Require(Different(front,ReadGlobal("_RemainsRadiance"))==0,"Маска стены затемнила передний свет персонажей.");
                result.shadowPixels=Different(noShadow,ReadGlobal("_RemainsBackgroundRadiance"));Require(result.shadowPixels>5,"Маски персонажей не изменили заднее поле.");result.actorMasks=true;
                Capture("factory-actors-shadows.png");
                long fillRevision=light.Fill.Revision;int directBuilds=light.Direct.Builds;long fillCells=light.Fill.LastGridCells;
                for(int i=0;i<6;i++){var move=area;move.x+=i*5;view.SetViewArea(move);view.RenderMuzzleFlashes(Array.Empty<SpriteEmission>(),masks,Array.Empty<string>());}
                Require(light.Fill.Revision==fillRevision && light.Direct.Builds==directBuilds && light.Fill.LastGridCells==fillCells,"Камера пересчитала геометрию или свет уровня.");result.cameraUsesCache=true;
                result.levelGpuBytes=light.Scene.Bytes+light.Direct.SumBytes+light.Direct.CacheBytes+((PolarDirectLight)light.Direct).ScratchBytes+light.Fill.GpuBytes;result.fillCpuBytes=light.Fill.CpuBytes;
                void Capture(string name){Remains.Runtime.NetworkFrameCapture.Save(camera,Path.Combine(Output,name),width:1280,height:672);}
            }
            finally{foreach(var o in new[]{cameraObject,fixture,visual})if(o!=null)Object.DestroyImmediate(o);}
        }
        private static void Finish(LevelLight light)
        {int steps=0;while(light.HasWork){light.Step(2,2);if(++steps>10000)throw new InvalidOperationException("Очередь основного света не завершилась.");}}
        private static Color[] ReadGlobal(string name)=>Read(Shader.GetGlobalTexture(name));
        private static Color[] Read(Texture texture)
        {var request=UnityEngine.Rendering.AsyncGPUReadback.Request(texture,0,TextureFormat.RGBAFloat);request.WaitForCompletion();if(request.hasError)throw new InvalidOperationException("Ошибка GPU-проверки "+texture.name);return request.GetData<Color>().ToArray();}
        private static int Different(Color[] a,Color[] b)
        {Require(a.Length==b.Length,"Размеры GPU-полей отличаются.");int count=0;for(int i=0;i<a.Length;i++)if(!a[i].Equals(b[i]))count++;return count;}
        private static void Require(bool valid,string context){if(!valid)throw new InvalidOperationException("Основной полярный свет: "+context);}
    }
}
