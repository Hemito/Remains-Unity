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
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Isolated authoring study. Does not install a runtime light implementation or alter lighting content.
    public static class LightGridStudy
    {
        private const int Divisor=2,ImageWidth=1280,ImageHeight=672;
        private static readonly string Output=Path.GetFullPath("../.reference/light-grid-20261103");
        [Serializable] private sealed class Measurement
        {
            public string device,api,limitations;
            public int seed=104,width,height,gridWidth,gridHeight,sources,enabled,seedCells,unseeded;
            public double prepareMs,directMs,bounceMs,readbackMs,gridPrepareMs,gridSolveMs,gridUploadMs;
            public double currentLampOffMs,gridLampOffMs,currentDoorOpenMs,gridDoorOpenMs;
            public long currentSceneBytes,currentDirectBytes,currentCacheBytes,currentBounceBytes,gridCpuBytes,gridTextureBytes;
            public float minSourceRadiance,maxSourceRadiance,maxSeedRadiance;
            public string lamp,door;
        }

        public static void RunBatch()
        {
            GameObject visual=null,cameraRoot=null;
            LevelLight benchmark=null;
            Texture2D gridTexture=null;
            var fence=new Texture2D(1,1,TextureFormat.RGBAHalf,false,true);
            try
            {
                Directory.CreateDirectory(Output);
                Debug.Log(GridLightField.RunSelfChecks());
                Anatomy.Load();WeaponCatalog.Load();
                var world=new WorldState{seed=104};
                FactoryPreparation.Create(world,AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath),0);
                FactoryContentSources.Prepare(world,"factory:0");
                var authoring=RoomAuthoring.Load("factory:0",world);
                DoorContentInstaller.Apply(world,RoomDoorContent.Read(authoring,"factory:0"));
                var room=authoring.CreateLayout(false);
                visual=AuthoredRoomVisual.Create("factory:0",room,world);
                var view=visual.GetComponent<FactoryLevelView>();var factory=visual.GetComponent<FactoryLevelRoot>();
                view.Synchronize(room,world,1);
                var report=new Measurement{device=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),
                    limitations="Editor batch; CPU submission + GPU completion wall time, not GPU timestamps. Static light only. Grid: full rebuild on events; max RGB, 12 m shared falloff; source reach replaced; seed strength derived from production irradiance at 1 m. No actor shadows/AO. Conservative absorption downsample. Current bytes: GPU resources only; grid bytes: array payloads and GPU texture, excludes seeds/CLR headers/driver overhead. Common artwork/scene resources excluded."};
                var clock=Stopwatch.StartNew();benchmark=view.CreateLevelLight();report.prepareMs=clock.Elapsed.TotalMilliseconds;
                clock.Restart();benchmark.Direct.BuildAll();Fence(benchmark.Direct.Sum,fence);report.directMs=clock.Elapsed.TotalMilliseconds;
                clock.Restart();benchmark.Bounce.BuildAll();Fence(benchmark.Bounce.Indirect,fence);report.bounceMs=clock.Elapsed.TotalMilliseconds;
                benchmark.Direct.WaitForMeasurements();
                report.currentSceneBytes=benchmark.Scene.Bytes;report.currentDirectBytes=benchmark.Direct.SumBytes;
                report.currentCacheBytes=benchmark.Direct.CacheBytes;report.currentBounceBytes=benchmark.Bounce.Bytes;
                report.width=benchmark.Scene.Width;report.height=benchmark.Scene.Height;
                report.sources=benchmark.Sources.Count;report.enabled=benchmark.Sources.All.Count(s=>s.Enabled);
                report.minSourceRadiance=benchmark.Sources.All.Min(s=>s.Radiance.maxColorComponent);
                report.maxSourceRadiance=benchmark.Sources.All.Max(s=>s.Radiance.maxColorComponent);
                clock.Restart();var absorption=ReadAbsorption(benchmark.Scene);report.readbackMs=clock.Elapsed.TotalMilliseconds;
                clock.Restart();var field=new GridLightField(report.width/Divisor,report.height/Divisor,benchmark.Scene.Texel*Divisor,absorption);
                var seedsBySource=new Dictionary<string,GridLightSeed[]>(StringComparer.Ordinal);
                var seeds=Seeds(benchmark.Scene,benchmark.Sources.All,absorption,report,seedsBySource);
                report.gridPrepareMs=clock.Elapsed.TotalMilliseconds;
                report.gridWidth=report.width/Divisor;report.gridHeight=report.height/Divisor;
                clock.Restart();field.Solve(seeds);report.gridSolveMs=clock.Elapsed.TotalMilliseconds;
                clock.Restart();gridTexture=Upload(field,absorption);report.gridUploadMs=clock.Elapsed.TotalMilliseconds;
                report.gridCpuBytes=field.AllocatedBytes+absorption.LongLength*sizeof(float);
                report.gridTextureBytes=(long)report.gridWidth*report.gridHeight*8;
                benchmark.Dispose();benchmark=null;

                // A separate view owns the production compositor. The benchmark never shares ownership of its scene.
                view.Focus(new Vector2(room.spawnX,room.spawnY));
                cameraRoot=new GameObject("Камера сравнения клеточного света");var camera=cameraRoot.AddComponent<Camera>();
                camera.enabled=false;camera.orthographic=true;camera.aspect=(float)ImageWidth/ImageHeight;camera.allowHDR=false;camera.allowMSAA=false;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.07f,.08f);
                var grid=factory.Prepared.Grid;float cell=(float)grid.Level.Cell;
                var doors=world.doors.Where(d=>d.level=="factory:0").ToArray();long revision=1;
                foreach(var (x,y,name) in new[]{(1,1,"hall"),(1,9,"office"),(2,11,"door"),(0,0,"entry")})
                {
                    var placement=grid.Rooms.Single(r=>r.Cell.X==x && r.Cell.Y==y);
                    var bounds=new Rect(placement.Column*cell,(grid.Level.Height-placement.Row-placement.Template.Height)*cell,placement.Template.Width*cell,placement.Template.Height*cell);
                    camera.transform.position=new Vector3(bounds.center.x,bounds.center.y,-10);
                    camera.orthographicSize=Mathf.Max(bounds.height*.5f,bounds.width/(2*camera.aspect));
                    float h=2*camera.orthographicSize;
                    view.SetViewArea(new Rect(bounds.center.x-h*camera.aspect*.5f,bounds.center.y-h*.5f,h*camera.aspect,h));view.Focus(bounds.center);
                    SavePair(view,camera,gridTexture,name);
                    if(name!="hall") continue;
                    report.lamp=view.Light.Sources.All.Where(s=>s.Kind==LevelLightSourceKind.Lamp && s.Enabled && s.Id.StartsWith("factory:0:1,1:",StringComparison.Ordinal))
                        .Select(s=>s.Id).OrderBy(s=>s,StringComparer.Ordinal).First();
                    clock.Restart();view.SyncLamps(new[]{report.lamp},++world.lampRevision);Fence(view.Light.Bounce.Indirect,fence);report.currentLampOffMs=clock.Elapsed.TotalMilliseconds;
                    // Geometry and source shapes did not move: disabling a lamp must not repeat seed placement.
                    clock.Restart();var offSeeds=seedsBySource.Where(p=>p.Key!=report.lamp).SelectMany(p=>p.Value).ToArray();
                    field.Solve(offSeeds);Replace(ref gridTexture,Upload(field,absorption));report.gridLampOffMs=clock.Elapsed.TotalMilliseconds;
                    SavePair(view,camera,gridTexture,"hall-lamp-off");
                    view.SyncLamps(Array.Empty<string>(),++world.lampRevision);
                    field.Solve(seeds);Replace(ref gridTexture,Upload(field,absorption));
                    var door=doors.Where(d=>d.id.StartsWith("factory:0:1,1:",StringComparison.Ordinal) && d.Blocks).OrderBy(d=>d.id,StringComparer.Ordinal).FirstOrDefault();
                    if(door==null) throw new InvalidOperationException("В зале seed104 не найдена закрытая дверь для сравнения.");
                    report.door=door.id;
                    clock.Restart();door.open=true;view.SyncDoors(doors,++revision);Fence(view.Light.Bounce.Indirect,fence);report.currentDoorOpenMs=clock.Elapsed.TotalMilliseconds;
                    clock.Restart();var openAbsorption=ReadAbsorption(view.Light.Scene);
                    var openField=new GridLightField(report.gridWidth,report.gridHeight,view.Light.Scene.Texel*Divisor,openAbsorption);
                    openField.Solve(Seeds(view.Light.Scene,view.Light.Sources.All,openAbsorption,null));Replace(ref gridTexture,Upload(openField,openAbsorption));report.gridDoorOpenMs=clock.Elapsed.TotalMilliseconds;
                    SavePair(view,camera,gridTexture,"hall-door-open");
                    door.open=false;view.SyncDoors(doors,++revision);field.Solve(seeds);Replace(ref gridTexture,Upload(field,absorption));
                }
                ControlledCases();
                File.WriteAllText(Path.Combine(Output,"report.json"),JsonUtility.ToJson(report,true));
                WritePage(report);
                Debug.Log("Клеточный свет: PASS; сравнение и измерения: "+Output+"\n"+JsonUtility.ToJson(report,true));
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally
            {
                benchmark?.Dispose();
                foreach(var obj in new Object[]{gridTexture,visual,cameraRoot,fence}) if(obj!=null) Object.DestroyImmediate(obj);
                FactoryContentSources.Clear();
            }
        }

        private static float[] ReadAbsorption(LevelLightScene scene)
        {
            if(scene.Width%Divisor!=0 || scene.Height%Divisor!=0) throw new InvalidOperationException("Размер сцены не делится на шаг экспериментальной сетки.");
            var previous=RenderTexture.active;var image=new Texture2D(scene.Width,scene.Height,TextureFormat.RFloat,false,true);
            try
            {
                RenderTexture.active=(RenderTexture)scene.Texture;image.ReadPixels(new Rect(0,0,scene.Width,scene.Height),0,0,false);
                var raw=image.GetRawTextureData<float>();int w=scene.Width/Divisor,h=scene.Height/Divisor;var result=new float[w*h];
                for(int y=0;y<h;y++) for(int x=0;x<w;x++)
                {
                    float maximum=0;
                    for(int dy=0;dy<Divisor;dy++) for(int dx=0;dx<Divisor;dx++) maximum=Mathf.Max(maximum,raw[(y*Divisor+dy)*scene.Width+x*Divisor+dx]);
                    // Conservative: a thin wall cannot disappear by averaging; partial alpha is per travelled cell.
                    result[y*w+x]=maximum>=GridLightField.OpaqueThreshold?1:Mathf.Min((float)LightRules.MaxObjectTexelAbsorption,1-Mathf.Pow(1-Mathf.Clamp01(maximum),Divisor));
                }
                return result;
            }
            finally {RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }

        private static List<GridLightSeed> Seeds(LevelLightScene scene,IReadOnlyList<LevelLightSource> sources,float[] absorption,Measurement report,Dictionary<string,GridLightSeed[]> bySource=null)
        {
            int w=scene.Width/Divisor,h=scene.Height/Divisor;float size=scene.Texel*Divisor;var seeds=new List<GridLightSeed>();
            int missing=0;float maximum=0;
            foreach(var source in sources)
            {
                if(!source.Enabled) continue;
                // Seed strength derived from irradiance at 1 m, using the same angular-width formula.
                // This does not fit the radial curve: grid light then decays linearly from the seed.
                // Area emitter samples seed a shape; their weights must not divide the max-grid brightness by 8.
                Color color=source.Radiance*(2*Mathf.Atan(source.Width*.5f));maximum=Mathf.Max(maximum,color.maxColorComponent);
                int before=seeds.Count;
                foreach(var sample in source.Samples)
                {
                    int cx=Mathf.FloorToInt((sample.x-scene.WorldRect.x)/size),cy=Mathf.FloorToInt((sample.y-scene.WorldRect.y)/size);
                    // Fixtures can begin inside a wall. Expose only free cells within the emitting form + one coarse cell.
                    int radius=Mathf.CeilToInt(Mathf.Max(source.Bounds.width,source.Bounds.height)/size)+1;
                    int best=-1;float distance=float.PositiveInfinity;
                    for(int y=Math.Max(0,cy-radius);y<=Math.Min(h-1,cy+radius);y++) for(int x=Math.Max(0,cx-radius);x<=Math.Min(w-1,cx+radius);x++)
                    {
                        if(absorption[y*w+x]>=.99f) continue;
                        var p=scene.WorldRect.position+new Vector2((x+.5f)*size,(y+.5f)*size);
                        if(p.x<source.Bounds.xMin-size || p.x>source.Bounds.xMax+size || p.y<source.Bounds.yMin-size || p.y>source.Bounds.yMax+size) continue;
                        float d=(p-new Vector2(sample.x,sample.y)).sqrMagnitude;
                        if(d<distance) {distance=d;best=y*w+x;}
                    }
                    if(best>=0) seeds.Add(new GridLightSeed(best,color));
                }
                if(seeds.Count==before) {missing++;Debug.LogWarning($"Эксперимент сетки: источник {source.Id}, форма {source.Bounds}, не имеет свободной клетки излучения; пропущен.");}
                bySource?.Add(source.Id,seeds.GetRange(before,seeds.Count-before).ToArray());
            }
            if(report!=null) {report.seedCells=seeds.Count;report.unseeded=missing;report.maxSeedRadiance=maximum;}
            return seeds;
        }

        private static Texture2D Upload(GridLightField field,float[] absorption)
        {
            var pixels=field.Pixels;
            var texture=new Texture2D(field.Width,field.Height,TextureFormat.RGBAHalf,false,true){name="Эксперимент: ограниченный RGB свет",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            texture.SetPixels(pixels);texture.Apply(false,true);
            // Complete texture upload before reporting time, just as the production path waits for GPU completion.
            var target=RenderTexture.GetTemporary(1,1,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            var pixel=new Texture2D(1,1,TextureFormat.RGBAHalf,false,true);
            try {Graphics.Blit(texture,target);Fence(target,pixel);}
            finally {Object.DestroyImmediate(pixel);RenderTexture.ReleaseTemporary(target);}
            return texture;
        }
        private static void Replace(ref Texture2D old,Texture2D next) {if(old!=null) Object.DestroyImmediate(old);old=next;}
        private static void Fence(Texture texture,Texture2D pixel)
        {
            var previous=RenderTexture.active;
            try {RenderTexture.active=(RenderTexture)texture;pixel.ReadPixels(new Rect(0,0,1,1),0,0,false);}
            finally {RenderTexture.active=previous;}
        }
        private static void SavePair(FactoryLevelView view,Camera camera,Texture2D grid,string name)
        {
            view.RenderMuzzleFlashes(Array.Empty<SpriteEmission>(),Array.Empty<ProjectedLightShadow>(),Array.Empty<string>());
            Save(camera,Path.Combine(Output,name+"-current.png"));
            var front=Shader.GetGlobalTexture("_RemainsRadiance");var back=Shader.GetGlobalTexture("_RemainsBackgroundRadiance");
            var oldRect=Shader.GetGlobalVector("_RemainsRadianceRect");float texel=Shader.GetGlobalFloat("_RemainsRadianceTexel");
            float enabled=Shader.GetGlobalFloat("_RemainsRadianceEnabled"),background=Shader.GetGlobalFloat("_RemainsBackgroundRadianceEnabled"),indirect=Shader.GetGlobalFloat("_RemainsIndirectEnabled");
            try
            {
                var rect=view.Light.Scene.WorldRect;
                Shader.SetGlobalTexture("_RemainsRadiance",grid);Shader.SetGlobalTexture("_RemainsBackgroundRadiance",grid);
                Shader.SetGlobalVector("_RemainsRadianceRect",new Vector4(rect.x,rect.y,rect.width,rect.height));
                Shader.SetGlobalFloat("_RemainsRadianceTexel",view.Light.Scene.Texel*Divisor);
                Shader.SetGlobalFloat("_RemainsRadianceEnabled",1);Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",1);Shader.SetGlobalFloat("_RemainsIndirectEnabled",0);
                Save(camera,Path.Combine(Output,name+"-grid.png"));
            }
            finally
            {
                // Present may skip an unchanged signature, so calling it again is insufficient to restore globals.
                Shader.SetGlobalTexture("_RemainsRadiance",front);Shader.SetGlobalTexture("_RemainsBackgroundRadiance",back);
                Shader.SetGlobalVector("_RemainsRadianceRect",oldRect);Shader.SetGlobalFloat("_RemainsRadianceTexel",texel);
                Shader.SetGlobalFloat("_RemainsRadianceEnabled",enabled);Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",background);Shader.SetGlobalFloat("_RemainsIndirectEnabled",indirect);
            }
        }
        private static void Save(Camera camera,string path)
        {
            var target=RenderTexture.GetTemporary(ImageWidth,ImageHeight,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
            var image=new Texture2D(ImageWidth,ImageHeight,TextureFormat.RGBA32,false);
            try
            {
                camera.targetTexture=target;camera.Render();camera.Render();RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,ImageWidth,ImageHeight),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally {camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(image);RenderTexture.ReleaseTemporary(target);}
        }
        private static void WritePage(Measurement report)
        {
            string html="<!doctype html><html lang='ru'><meta charset='utf-8'><title>Сравнение света</title><style>body{background:#20252b;color:#eee;font:17px system-ui;margin:24px}section{margin:28px 0}.pair{display:grid;grid-template-columns:1fr 1fr;gap:12px}img{width:100%}pre{white-space:pre-wrap}h3{margin:8px 0}</style><h1>Общая сетка света — эксперимент</h1><p>Слева текущий игровой гибрид, справа ограниченное RGB-поле. Одинаковые камера, материалы и окружающий свет. Игра не изменена.</p><p>Сетка 20 см, 15 уровней с 16 промежуточными значениями. Максимум каждого цветового канала, плавная выборка; затухание максимума за 12 м. Индивидуальные радиусы заменены общим. Это вариант по мотивам клеточного света, не точная копия Minecraft. Нет теней персонажей и AO.</p>";
            foreach(string name in new[]{"hall","hall-lamp-off","hall-door-open","office","door","entry"})
                html+=$"<section><h2>{name}</h2><div class='pair'><div><h3>Текущий</h3><img src='{name}-current.png'></div><div><h3>Общая сетка</h3><img src='{name}-grid.png'></div></div></section>";
            html+="<h2>Контрольные сцены</h2><p>Здесь показано само поле света на одинаковой условной белой поверхности, без окружающего света. Слева тот же игровой DirectLight + BounceLight, справа сетка. Все изображения используют одну линейную шкалу 0…1 с переводом в sRGB; превышение единицы обрезается одинаково. Цветные точки — источники, тёмные клетки — непроницаемые стены.</p>";
            foreach(string name in new[]{"corner","sealed","colored","one","many","weak-after-off"})
                html+=$"<section><h3>{name}</h3><div class='pair'><img src='test-{name}-current.png'><img src='test-{name}-grid.png'></div></section>";
            html+="<h2>Измерение полного завода seed104</h2><p>Один прогон в Editor. Текущий свет: отправка команд плюс ожидание GPU; это не GPU timestamps. События сетки пока полностью пересчитывают завод. Это не оценка FPS Player.</p><pre>"+JsonUtility.ToJson(report,true)+"</pre></html>";
            File.WriteAllText(Path.Combine(Output,"index.html"),html);
        }

        private static void ControlledCases()
        {
            const int w=100,h=60;const float size=.2f;
            foreach(string name in new[]{"corner","sealed","colored","one","many","weak-after-off"})
            {
                var alpha=new float[w*h];var walls=new float[w,h];var positions=new List<(int x,int y,Color color)>();
                for(int y=0;y<h;y++) for(int x=0;x<w;x++)
                {
                    bool solid=x==0 || y==0 || x==w-1 || y==h-1 ||
                        (name=="corner" && x==50 && y<43) || (name=="sealed" && x==50);
                    alpha[y*w+x]=walls[x,y]=solid?1:0;
                }
                if(name=="colored") {positions.Add((25,30,Color.red));positions.Add((50,30,Color.green));positions.Add((75,30,Color.blue));}
                else if(name=="many") for(int y=20;y<=40;y+=5) for(int x=35;x<=65;x+=5) positions.Add((x,y,Color.white));
                else if(name=="weak-after-off") positions.Add((58,30,new Color(.15f,.2f,.4f)));
                else positions.Add((name=="corner" || name=="sealed"?35:50,30,Color.white));
                var sources=new List<LevelLightSource>();var seeds=new List<GridLightSeed>();
                for(int i=0;i<positions.Count;i++)
                {
                    var (x,y,color)=positions[i];var p=new Vector2((x+.5f)*size,(y+.5f)*size);
                    sources.Add(new LevelLightSource("study:"+i,LevelLightSourceKind.Lamp,new[]{new Vector4(p.x,p.y,1,0)},color,1,12,
                        new Rect(p-Vector2.one*.1f,Vector2.one*.2f),new RectInt(0,0,w,h),true));
                    seeds.Add(new GridLightSeed(y*w+x,color*(2*Mathf.Atan(.5f))));
                }
                using var scene=new LevelLightScene(walls,Vector2.zero,5,Array.Empty<LevelLightScene.ObjectSprite>());
                using var direct=new DirectLight(scene,new LevelLightSources(sources,w,h));
                direct.BuildAll();using var bounce=new BounceLight(scene,direct,.5f);bounce.BuildAll();
                var current=ReadColors(direct.Sum);var indirect=ReadColors(bounce.Indirect);
                for(int y=0;y<h;y++) for(int x=0;x<w;x++) current[y*w+x]+=indirect[(y/2)*(w/2)+x/2];
                var field=new GridLightField(w,h,size,alpha);field.Solve(seeds);
                WriteField(current,w,h,alpha,positions,Path.Combine(Output,"test-"+name+"-current.png"));
                WriteField(field.Pixels,w,h,alpha,positions,Path.Combine(Output,"test-"+name+"-grid.png"));
                if(name=="sealed" && field.Pixels[30*w+70].maxColorComponent!=0) throw new InvalidOperationException("Контрольная стена пропустила клеточный свет.");
            }
        }
        private static Color[] ReadColors(Texture source)
        {
            var previous=RenderTexture.active;var image=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
            try {RenderTexture.active=(RenderTexture)source;image.ReadPixels(new Rect(0,0,source.width,source.height),0,0,false);return image.GetPixels();}
            finally {RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
        private static void WriteField(Color[] field,int w,int h,float[] alpha,List<(int x,int y,Color color)> positions,string path)
        {
            var pixels=new Color[field.Length];
            for(int i=0;i<field.Length;i++) pixels[i]=alpha[i]>=.99f?new Color(.06f,.07f,.08f,1):new Color(Mathf.Clamp01(field[i].r),Mathf.Clamp01(field[i].g),Mathf.Clamp01(field[i].b),1).gamma;
            foreach(var (x,y,color) in positions) pixels[y*w+x]=color;
            var image=new Texture2D(w,h,TextureFormat.RGBA32,false);
            try {image.SetPixels(pixels);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally {Object.DestroyImmediate(image);}
        }
    }
}
