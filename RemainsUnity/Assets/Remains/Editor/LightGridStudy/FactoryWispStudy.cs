using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
using Debug=UnityEngine.Debug;

namespace Remains.Editor
{
    public static class FactoryWispStudy
    {
        private const int Width=1280,Height=672,Frames=FactoryWispPath.Frames,Fps=16;
        private static readonly Color[] Colors={new(1,.22f,.08f),new(.1f,1,.34f),new(.2f,.4f,1)};
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/light-polar-20261103"));
        [Serializable] private sealed class Room
        {
            public string id,title;
            public Rect worldRect;
            public string[] actors;
            public Vector2[] path;
            public int masks,unresolvedFill,partialSamples;
            public float mapError;
            public long transientGpuBytes;
            public double prepareMs,lightMedianMs,lightP90Ms,frameMedianMs,frameP90Ms,baselineFrameMedianMs,baselineFrameP90Ms,pairedFrameDeltaMedianMs;
            public double[] lightSamples,frameSamples,baselineFrameSamples;
        }
        [Serializable] private sealed class Report
        {
            public string startedUtc,finishedUtc,device,api,method,validation;
            public int seed=104,width=Width,height=Height,frames=Frames,fps=Fps,wisps=3,gc0,gc1,gc2;
            public float fill=.08f,staticGain=.2f,outerPitch=.05f;
            public Room[] rooms;
        }
        private sealed class Panel : IDisposable
        {
            internal readonly FactoryWispActors Actors;
            internal readonly FactoryWispPath Path;
            internal readonly GridFillReconstruction Fill;
            internal readonly FactoryWispComposite Composite;
            internal readonly RasterPolarWisp[] Wisps=new RasterPolarWisp[3];
            internal readonly SpriteRenderer[] Orbs=new SpriteRenderer[3];
            internal readonly Rect World;
            private readonly FactoryLevelView view;
            private readonly GridDirectHybrid hybrid;
            private readonly List<ProjectedLightShadow> masks=new();
            private readonly Texture2D glow;
            private readonly Sprite sprite;
            private readonly Material material;
            internal int MaskCount;
            internal Panel(Transform parent,FactoryLevelView view,GridDirectHybrid hybrid,RoomLayout layout,Rect room,float[] absorption,Texture blocked)
            {
                this.view=view;this.hybrid=hybrid;World=CameraRect(room);int layer=parent.gameObject.layer;
                try
                {
                    Actors=new FactoryWispActors(parent,layout,room,layer);Path=new FactoryWispPath(view.Light.Scene,absorption,room,Actors.DrawnBounds);
                    Fill=new GridFillReconstruction(Width,Height);
                    Fill.Build(hybrid.GridTexture.Texture,blocked,hybrid.Scene.WorldRect,view.Light.Scene.Texture,view.Light.Scene.WorldRect,World);
                    Composite=new FactoryWispComposite(World,Width,Height);
                    glow=new Texture2D(48,48,TextureFormat.RGBA32,false,true);var pixels=new Color[48*48];
                    for(int y=0;y<48;y++)for(int x=0;x<48;x++){float d=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(24,24))/24;pixels[y*48+x]=new Color(1,1,1,Mathf.Clamp01(Mathf.Exp(-d*d*12)-Mathf.Exp(-12)));}
                    glow.SetPixels(pixels);glow.Apply(false,false);sprite=Sprite.Create(glow,new Rect(0,0,48,48),new Vector2(.5f,.5f),80);
                    material=new Material(GameAssets.Load<Shader>("Presentation/ScreenGlow")??throw new InvalidOperationException("Нет шейдера свечения виспа."));
                    for(int i=0;i<3;i++)
                    {
                        Wisps[i]=new RasterPolarWisp(view.Light.Scene,World,Width,Height);
                        var go=new GameObject("Цветной висп "+i);go.transform.SetParent(parent,false);go.layer=layer;
                        Orbs[i]=go.AddComponent<SpriteRenderer>();Orbs[i].sprite=sprite;Orbs[i].sharedMaterial=material;Orbs[i].color=Colors[i];Orbs[i].sortingOrder=10000;
                    }
                }
                catch{Dispose();throw;}
            }
            internal void Draw(int frame,bool shadows=true)
            {
                Composite.Begin(hybrid.Direct.Sum,hybrid.Scene.WorldRect,Fill.Texture);
                MaskCount=0;
                for(int i=0;i<3;i++)
                {
                    var position=Path.Position(FactoryWispPath.Phase(frame,i));
                    Orbs[i].enabled=true;
                    Orbs[i].transform.position=new Vector3(position.x,position.y,-.1f);
                    Wisps[i].SetSource(position,Colors[i]);Wisps[i].BuildMap();Wisps[i].Render();masks.Clear();
                    if(shadows)Actors.AppendProjectedShadows("wisp:"+i,position,.06f,view.ShadowSettings,masks);
                    MaskCount+=masks.Count;Composite.Add("wisp:"+i,Wisps[i].Texture,masks);
                }
                Composite.Resolve();Composite.Publish();
            }
            internal void DrawWithoutWisps()
            {
                Composite.Begin(hybrid.Direct.Sum,hybrid.Scene.WorldRect,Fill.Texture);Composite.Resolve();Composite.Publish();
                foreach(var orb in Orbs)orb.enabled=false;
            }
            public void Dispose()
            {
                foreach(var w in Wisps)w?.Dispose();
                foreach(var r in Orbs)if(r!=null)Object.DestroyImmediate(r.gameObject);
                Actors?.Dispose();Fill?.Dispose();Composite?.Dispose();
                foreach(var o in new Object[]{sprite,glow,material})if(o!=null)Object.DestroyImmediate(o);
            }
        }
        public static void RefreshPageBatch()
        {
            try
            {
                if(!File.Exists(Path.Combine(Output,"factory-report.json")))throw new InvalidOperationException("Нет готового отчёта заводских виспов.");
                File.Copy(Path.Combine(Application.dataPath,"Remains/Editor/LightGridStudy/FactoryWispPreview.html.txt"),Path.Combine(Output,"factory.html"),true);
                Debug.Log("Завод/виспы: страница обновлена без повторной записи кадров и измерений.");EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void RunBatch()
        {
            GameObject visual=null,fixture=null,cameraRoot=null;GridDirectHybrid hybrid=null;Texture2D blocked=null,image=null,fence=null;RenderTexture target=null;
            try
            {
                Directory.CreateDirectory(Output);bool smoke=Environment.GetCommandLineArgs().Contains("-wisp-smoke");
                Debug.Log(RasterPolarWispChecks.Run());
                if(!Environment.GetCommandLineArgs().Contains("-wisp-factory-only"))
                {Debug.Log(GridFillReconstructionChecks.Run());PolarMotionStudy.RefreshFlashImages();}
                Anatomy.Load();WeaponCatalog.Load();var world=new WorldState{seed=104};
                FactoryPreparation.Create(world,AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath),0);
                FactoryContentSources.Prepare(world,"factory:0");var authoring=RoomAuthoring.Load("factory:0",world);
                DoorContentInstaller.Apply(world,RoomDoorContent.Read(authoring,"factory:0"));var layout=authoring.CreateLayout(false);
                visual=AuthoredRoomVisual.Create("factory:0",layout,world);var view=visual.GetComponent<FactoryLevelView>();var factory=visual.GetComponent<FactoryLevelRoot>();view.Synchronize(layout,world,1);
                view.Focus(new Vector2(layout.spawnX,layout.spawnY));
                hybrid=new GridDirectHybrid(view.Light.Scene,view.Light.Sources.All);hybrid.BuildAll();
                var fine=RasterPolarWisp.ReadFloatsForValidation(view.Light.Scene.Texture);
                blocked=new Texture2D(hybrid.Scene.Width,hybrid.Scene.Height,TextureFormat.RFloat,false,true);blocked.SetPixelData(hybrid.Absorption,0);blocked.Apply(false,false);
                fixture=new GameObject("Editor: NPC и виспы завода");cameraRoot=new GameObject("Editor: камера виспов");var camera=cameraRoot.AddComponent<Camera>();
                camera.enabled=false;camera.orthographic=true;camera.aspect=(float)Width/Height;camera.allowHDR=false;camera.allowMSAA=false;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
                image=new Texture2D(Width,Height,TextureFormat.RGBA32,false);fence=new Texture2D(1,1,TextureFormat.RGBA32,false);
                var report=new Report{startedUtc=DateTime.UtcNow.ToString("O"),device=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),
                    method="Только Editor: завод seed104, настоящие комнаты, предметы, пони, рейдер, крыса и два пистолета. Три цветных движущихся источника радиуса8м, угловой шаг и фильтр5см. Исходное поглощение10/м; глубокая полярная R32F-карта хранит префиксы пропускания, включая частичное поглощение. Маршруты заранее построены в связной воздушной области возле NPC по той же геометрии. Маски настоящего тела и оружия берутся из ActorCastShadowView, объединяются максимумом для одного источника и затеняют только его RGB на задней стене. Актёры и предметы читают переднее поле. Постоянный direct приглушён до20%, заполнение0,08 реконструировано один раз для неподвижной камеры. Виспы не добавляют отражений и не меняют сетку. Поза NPC фиксирована; ИИ, физика и сеть не симулируются. Браузер показывает записанные PNG Unity. Замеры:2прогрева,12измеряемых циклов кадра без виспов, света отдельно и полного кадра с виспами; порядок разворачивается через цикл. Время wall-clock включает отправку и ожидание GPU через чтение1пикселя. Полный кадр включает сцену,3полярных карты/поля,9проекций и композицию; это не FPS Player. PNG, проверки, подготовка и выделение ресурсов вне серии; нагрузка ПК не менялась. Парная разница full-minus-baseline и все выборки сохранены. Память transientGpuBytes — только полярные карты/поля, композит и восстановленное заполнение; не включает маски актёров, камеру, арт, меши и драйвер. Производственный свет остаётся загруженным для основы стенда; общей экономии памяти здесь не заявляем. Нет ограничения числа ламп или LOD."};
                var rooms=new List<Room>();var grid=factory.Prepared.Grid;float cell=(float)grid.Level.Cell;
                foreach(var (x,y,id,title) in new[]{(1,1,"hall","Завод: секция 1"),(1,9,"office","Завод: секция 2"),(0,0,"entry","Входная секция")})
                {
                    if(smoke && rooms.Count>0)break;
                    var placement=grid.Rooms.Single(r=>r.Cell.X==x && r.Cell.Y==y);
                    var room=new Rect(placement.Column*cell,(grid.Level.Height-placement.Row-placement.Template.Height)*cell,placement.Template.Width*cell,placement.Template.Height*cell);
                    var clock=Stopwatch.StartNew();view.Focus(room.center);
                    using var panel=new Panel(fixture.transform,view,hybrid,layout,room,fine,blocked);
                    camera.transform.position=new Vector3(panel.World.center.x,panel.World.center.y,-10);camera.orthographicSize=panel.World.height*.5f;view.SetViewArea(panel.World);
                    var result=new Room{id=id,title=title,worldRect=panel.World,actors=panel.Actors.ActorIds.ToArray(),path=panel.Path.Points.ToArray(),prepareMs=clock.Elapsed.TotalMilliseconds};
                    panel.Fill.ReadDiagnostics("factory:"+id);result.unresolvedFill=panel.Fill.UnresolvedCount;
                    panel.Draw(0);camera.Render();camera.Render();
                    var check=panel.Wisps[0].ValidateMapAgainstCpu(fine,7,.006f);result.partialSamples=check.PartialSamples;result.mapError=check.MaximumError;
                    // Fields/maps/composite/reconstruction payload only; excludes authored pose/projection masks,
                    // camera/depth buffers, artwork, meshes and driver overhead.
                    result.transientGpuBytes=panel.Wisps.Sum(w=>w.GpuBytes)+panel.Composite.GpuBytes+(long)Width*Height*16;result.masks=panel.MaskCount;
                    Capture(camera,target,image,Path.Combine(Output,"factory-"+id+"-00.png"));
                    panel.Draw(0,false);Capture(camera,target,image,Path.Combine(Output,"factory-"+id+"-no-shadow.png"));
                    int count=smoke?1:Frames;
                    for(int frame=0;frame<count;frame++){panel.Draw(frame);Capture(camera,target,image,Path.Combine(Output,$"factory-{id}-{frame:00}.png"));if(frame%12==11)Debug.Log($"Завод/виспы {id}: {frame+1}/{Frames}.");}
                    if(!smoke)Measure(panel,camera,target,fence,result,report);
                    rooms.Add(result);Debug.Log("Завод/виспы: комната готова "+JsonUtility.ToJson(result));
                }
                report.rooms=rooms.ToArray();report.finishedUtc=DateTime.UtcNow.ToString("O");
                report.validation="PASS: реконструкция заполнения проверена на постоянном поле и цветных сторонах преграды, включая закрытый угол; полярные префиксы на реальном поглощении сопоставлены CPU; настоящие маски NPC присутствуют. Движение и художественное качество оценивает пользователь. Открытые пиксели без поддержки заполнения явно учитываются и предупреждаются. Этот стенд не доказывает производительность полного Player с ИИ/сетью.";
                File.WriteAllText(Path.Combine(Output,smoke?"factory-smoke.json":"factory-report.json"),JsonUtility.ToJson(report,true));
                if(!smoke)File.Copy(Path.Combine(Application.dataPath,"Remains/Editor/LightGridStudy/FactoryWispPreview.html.txt"),Path.Combine(Output,"factory.html"),true);
                Debug.Log("Завод/виспы: PASS, smoke="+smoke);EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
            finally
            {
                hybrid?.Dispose();foreach(var o in new Object[]{blocked,image,fence,target,cameraRoot,fixture,visual})if(o!=null)Object.DestroyImmediate(o);
                FactoryContentSources.Clear();
            }
        }
        private static Rect CameraRect(Rect room)
        {
            float aspect=(float)Width/Height,h=Mathf.Max(room.height+.6f,(room.width+.6f)/aspect);
            return new Rect(room.center-new Vector2(h*aspect,h)*.5f,new Vector2(h*aspect,h));
        }
        private static void Capture(Camera camera,RenderTexture target,Texture2D image,string path)
        {
            var previous=RenderTexture.active;try{camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,Width,Height),0,0,false);File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{RenderTexture.active=previous;}
        }
        private static void Wait(RenderTexture target,Texture2D fence)
        {var previous=RenderTexture.active;try{RenderTexture.active=target;fence.ReadPixels(new Rect(0,0,1,1),0,0,false);}finally{RenderTexture.active=previous;}}
        private static void Measure(Panel panel,Camera camera,RenderTexture target,Texture2D fence,Room result,Report report)
        {
            var light=new List<double>();var full=new List<double>();var baseline=new List<double>();int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);var clock=new Stopwatch();
            for(int sample=-2;sample<12;sample++)foreach(int mode in (sample&1)==0?new[]{0,1,2}:new[]{2,1,0})
            {
                Wait(target,fence);clock.Restart();if(mode==0)panel.DrawWithoutWisps();else panel.Draw((sample+2)*3%Frames);
                if(mode!=1)camera.Render();Wait(mode!=1?target:panel.Composite.Front,fence);clock.Stop();
                if(sample>=0)(mode==0?baseline:mode==1?light:full).Add(clock.Elapsed.TotalMilliseconds);
            }
            report.gc0+=GC.CollectionCount(0)-g0;report.gc1+=GC.CollectionCount(1)-g1;report.gc2+=GC.CollectionCount(2)-g2;
            result.lightSamples=light.ToArray();result.frameSamples=full.ToArray();result.baselineFrameSamples=baseline.ToArray();
            result.lightMedianMs=Percentile(light,.5);result.lightP90Ms=Percentile(light,.9);result.frameMedianMs=Percentile(full,.5);result.frameP90Ms=Percentile(full,.9);
            result.baselineFrameMedianMs=Percentile(baseline,.5);result.baselineFrameP90Ms=Percentile(baseline,.9);
            result.pairedFrameDeltaMedianMs=Percentile(full.Select((v,i)=>v-baseline[i]).ToList(),.5);
        }
        private static double Percentile(List<double> values,double q)=>values.OrderBy(x=>x).ElementAt((int)Math.Ceiling(q*values.Count)-1);
    }
}
