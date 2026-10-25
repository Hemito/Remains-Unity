using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Isolated Editor decision preview. No runtime registration or saved scene/asset edits.
    public static class LightHybridPreview
    {
        private const int ImageWidth=1280,ImageHeight=672;
        private static readonly string[] States={"all-on","lamp-off","door-closed","door-open","flash"};
        private static readonly string[] Labels={"Все лампы включены","Одна лампа выключена","Дверь закрыта","Дверь открыта","Дульная вспышка"};
        [Serializable] private sealed class SourceTime {public string id,kind;public Rect bounds;public float reach;public double milliseconds;}
        [Serializable] private sealed class Result
        {
            public string room,state,mode,path,lampId,doorId;public Rect roomBounds;public double referenceFenceMilliseconds,bounceFenceMilliseconds;
            public long fineGpuBytes,directCacheBytes,bounceBytes,composeBytes,ownedGpuBytes;
            public int coarseWidth,coarseHeight,coarseLevels,closedFineOpenTexels,thinBarrierCandidates;
            public float maxThinBarrierBackLight;public Vector2 thinBarrierWorld;
            public HybridBouncePrototype.IsolatedSeedLeak isolatedSeedLeak;
        }
        [Serializable] private sealed class Report
        {
            public int schema=1,seed=104,width,height,forcedOnLamps;
            public string timing="Command submission plus blocking 1-pixel ReadPixels fence after draining prior work. Not hardware GPU timestamps; allocation/source sampling excluded from direct timing.",
                model="Editor-only. Fine scene is the current painter. Lamp reach=2×authored lamp radius; windows 12m, screen glows 6m; smooth final10% cutoff, ≤8 emitting-area rays. Finite angular patch 2*atan((width*normalizedWeight)/(2*distance)), half-texel near clamp, far field width/distance; no per-density exposure/gain changes. Existing cascade/Bounce kernels transport a continuously supplied first-wall-reflection seed, 8 passes, no original emitter RGB in bounce scene. Partial coarse alpha capped .985; any fine opaque cell stays opaque. Screen shape/color uses documented mean-color approximation.",
                limits="No production integration/invalidation scheduler. Direct fields cached by source and intersecting closed-door geometry. Actor silhouettes are not subtracted; flash frame uses actual pistol/flash artwork but no projected actor-shadow comparison. Isotropic wall seeds can illuminate reverse faces; thin-barrier readback reports a proxy, not a proof that all indirect light is leakage. Conservative downsampling can close narrow openings. Real-pipeline camera.Render frames, same camera/exposure per room.";
            public Vector2 darkCornerWorld;
            public float darkCornerDirect,darkCornerBounce;
            public SourceTime[] directTimings;public Result[] frames;
        }
        private sealed class Sample {public string name;public FactoryRoomPlacement room;public Rect bounds;public string lamp;public DoorState door;}
        private sealed class Cached {public HybridDirectPrototype.Source source;public RenderTexture texture;}
        private static T Get<T>(object value,string key)=>(T)value.GetType().GetField(key,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);
        private static void Set(object value,string key,object data)=>value.GetType().GetField(key,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(value,data);

        public static void RunBatch()
        {
            GameObject visual=null,cameraRoot=null,gunRoot=null,flashRoot=null;
            HybridDirectPrototype direct=null;HybridScenePrototype fine=null;
            var cache=new Dictionary<string,Cached>(StringComparer.Ordinal);var timings=new List<SourceTime>();var frames=new List<Result>();
            RenderTexture baselineDirect=null;Texture2D fence=null;
            try
            {
                Anatomy.Load();WeaponCatalog.Load();WeaponRigs.Load();
                var world=new WorldState {seed=104};FactoryPreparation.Create(world,AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath),0);
                FactoryContentSources.Prepare(world,"factory:0");var authoring=RoomAuthoring.Load("factory:0",world);
                DoorContentInstaller.Apply(world,RoomDoorContent.Read(authoring,"factory:0"));
                var layout=authoring.CreateLayout(false);visual=AuthoredRoomVisual.Create("factory:0",layout,world);
                var view=visual.GetComponent<FactoryLevelView>();view.Synchronize(layout,world,1);var factory=visual.GetComponent<FactoryLevelRoot>();
                var grid=factory.Prepared.Grid;var doors=world.doors.Where(d=>d.level=="factory:0").ToArray();foreach(var door in doors) door.open=false;
                long revision=100;view.SyncDoors(doors,revision);
                var lamps=Get<Dictionary<string,(RoomSpriteAuthoring picture,int frame,Sprite sprite,RoomDefinition definition)>>(view,"lamps");
                currentTable=view.ShadowSettings;
                var report=new Report {forcedOnLamps=ForceOn(lamps,factory)};
                view.SyncLamps(Array.Empty<string>(),++revision);
                string output=Path.GetFullPath("../.reference/light-hybrid");Directory.CreateDirectory(output);
                bool flashOnly=Array.IndexOf(Environment.GetCommandLineArgs(),"-hybridFlashOnly")>=0;
                Report previous=flashOnly?JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(output,"report.json"))):null;
                fine=new HybridScenePrototype(view,factory);report.width=fine.Width;report.height=fine.Height;
                direct=new HybridDirectPrototype(fine.Scene,fine.WorldRect,fine.Width,fine.Height,fine.FineResolution);
                var baselineFields=Fields(fine,direct,cache,lamps,doors,timings);baselineDirect=direct.Accumulate(baselineFields);direct.Fence(baselineDirect);
                var samples=new List<Sample>();
                foreach(var p in new[]{(1,1,"hall"),(1,9,"office"),(2,11,"door")}) samples.Add(MakeSample(p.Item3,grid.Rooms.Single(r=>r.Cell.X==p.Item1 && r.Cell.Y==p.Item2),grid,lamps,doors,view.ShadowSettings));
                var used=new HashSet<FactoryRoomPlacement>(samples.Select(s=>s.room));
                var beam=grid.Rooms.Where(r=>!used.Contains(r)).OrderByDescending(r=>r.Template.Layers.TryGetValue(RoomLayers.Platform,out var rows)?rows.Sum(line=>line.Count(c=>c!=RoomFile.Empty)):0).First();
                samples.Add(MakeSample("beams",beam,grid,lamps,doors,view.ShadowSettings));used.Add(beam);
                using(var bounce=new HybridBouncePrototype(fine.Scene,baselineDirect,fine.WorldRect,fine.Width,fine.Height,4,view.ShadowSettings))
                {
                    var corner=FindDarkCorner(baselineDirect,bounce,grid,used,fine.WorldRect,report);
                    samples.Add(MakeSample("corner",corner,grid,lamps,doors,view.ShadowSettings));
                }
                fine.Dispose();fine=null;direct.Release(baselineDirect);baselineDirect=null;
                cameraRoot=new GameObject("Диагностика гибрида: камера");var camera=cameraRoot.AddComponent<Camera>();
                camera.enabled=false;camera.orthographic=true;camera.aspect=(float)ImageWidth/ImageHeight;camera.allowHDR=false;camera.allowMSAA=false;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.07f,.08f);
                fence=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
                var pistol=GameAssets.Load<WeaponAsset>("Content/weapons/pistol");
                var flashSprite=pistol.ShotAnimation.EmissionFrames.First(s=>s!=null);
                gunRoot=Object.Instantiate(pistol.Presentation).gameObject;var gun=gunRoot.GetComponent<WeaponView>();gun.SetFrame(gun.ResolveRestSprite());
                flashRoot=new GameObject("Диагностика гибрида: вспышка");var flashRenderer=flashRoot.AddComponent<SpriteRenderer>();
                flashRenderer.sprite=flashSprite;flashRenderer.sortingOrder=ActorVisualLayers.EffectOrder;SpriteArtworkMaterial.Prepare(flashSprite);SpriteArtworkMaterial.Apply(flashRenderer,flashSprite);SpriteArtworkMaterial.SetSelfIlluminated(flashRenderer);
                var profile=GameAssets.Load<MuzzleFlashCatalog>("Content/MuzzleFlashCatalog").Read();float flashGain=pistol.MuzzleFlashGain*profile[pistol.CreateDefinition().ammo];
                foreach(var sample in samples)
                {
                    camera.transform.position=new Vector3(sample.bounds.center.x,sample.bounds.center.y,-10);camera.orthographicSize=Mathf.Max(sample.bounds.height*.5f,sample.bounds.width/(2*camera.aspect));
                    var candidates=new List<Vector2>();
                    foreach(var floor in layout.platforms.Where(p=>p.slopeDirection==0 && p.width>2))
                    {
                        float left=Mathf.Max(floor.x+.5f,sample.bounds.xMin+1),right=Mathf.Min(floor.x+floor.width-1.5f,sample.bounds.xMax-2);
                        if(right<=left) continue;
                        foreach(float x in new[]{left,(left+right)*.5f,right})
                        {
                            var point=new Vector2(x,floor.y+floor.height+1.1f);if(!sample.bounds.Contains(point) || point.y>sample.bounds.yMax-1) continue;
                            var footprint=new Rect(point.x-.2f,point.y-.2f,1.5f,.5f);
                            if(!layout.platforms.Any(p=>new Rect(p.x,p.y,p.width,p.height).Overlaps(footprint))) candidates.Add(point);
                        }
                    }
                    if(candidates.Count==0) throw new InvalidOperationException("Нет свободной контрольной позиции ствола: "+sample.name);
                    var gunPoint=sample.name=="corner"?report.darkCornerWorld-new Vector2(.7f,0):candidates.OrderBy(p=>Vector2.Distance(p,sample.bounds.center)).First();
                    gun.transform.position=gunPoint;flashRoot.transform.position=gun.Muzzle.position;flashRoot.transform.localScale=Vector3.one;
                    foreach(string state in States)
                    {
                        if(flashOnly && state!="flash") continue;
                        foreach(var door in doors) door.open=false;if(state=="door-open") sample.door.open=true;
                        view.SyncDoors(doors,++revision);view.SyncLamps(state=="lamp-off"?new[]{sample.lamp}:Array.Empty<string>(),++revision);
                        bool flash=state=="flash";gunRoot.SetActive(flash);flashRoot.SetActive(flash);
                        var window=view.WindowFor(sample.bounds.center);var clock=Stopwatch.StartNew();view.RefreshLight(window);
                        var field=Get<RoomRadianceField>(view,"field");field.RenderTransient(flash?new[]{new RoomRadianceField.SpriteEmission(flashSprite,flashRoot.transform.localToWorldMatrix,flashGain)}:Array.Empty<RoomRadianceField.SpriteEmission>(),null,null);
                        Fence((RenderTexture)Shader.GetGlobalTexture("_RemainsRadiance"),fence);clock.Stop();
                        var prefix=sample.name+"-"+state;
                        Save(camera,Path.Combine(output,prefix+"-today.png"));
                        frames.Add(new Result {room=sample.name,roomBounds=sample.bounds,lampId=sample.lamp,doorId=sample.door.id,state=state,mode="today",path=prefix+"-today.png",referenceFenceMilliseconds=clock.Elapsed.TotalMilliseconds});
                        fine=new HybridScenePrototype(view,factory);direct.SetScene(fine.Scene);
                        var fields=Fields(fine,direct,cache,lamps,doors,timings);var sum=direct.Accumulate(fields);direct.Fence(sum);
                        HybridDirectPrototype.Source flashSource=null;RenderTexture flashField=null;
                        if(flash)
                        {
                            var emitter=new SteadyLightEmitter("preview:flash",flashSprite,flashRoot.transform.localToWorldMatrix,new Color(flashGain,flashGain*.7f,flashGain*.25f));
                            flashSource=Source(fine,emitter,12);flashField=direct.Build(flashSource,out _);
                        }
                        foreach(int divisor in new[]{2,4})
                        {
                            using(var bounce=new HybridBouncePrototype(fine.Scene,sum,fine.WorldRect,fine.Width,fine.Height,divisor,view.ShadowSettings))
                            {
                                var final=direct.Accumulate(fields,bounce.Result);if(flash) direct.Accumulate(flashSource,flashField,final);direct.Fence(final);
                                Shader.SetGlobalTexture("_RemainsRadiance",final);Shader.SetGlobalTexture("_RemainsBackgroundRadiance",final);
                                Shader.SetGlobalVector("_RemainsRadianceRect",new Vector4(fine.WorldRect.x,fine.WorldRect.y,fine.WorldRect.width,fine.WorldRect.height));
                                Shader.SetGlobalFloat("_RemainsRadianceEnabled",1);Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",0);
                                string path=prefix+"-hybrid"+divisor+".png";Save(camera,Path.Combine(output,path));
                                var result=new Result {room=sample.name,roomBounds=sample.bounds,lampId=sample.lamp,doorId=sample.door.id,state=state,mode="1/"+divisor,path=path,bounceFenceMilliseconds=bounce.FenceMilliseconds,
                                    fineGpuBytes=fine.SceneBytes,directCacheBytes=direct.Bytes-(long)fine.Width*fine.Height*16,bounceBytes=bounce.Bytes,composeBytes=(long)fine.Width*fine.Height*16,
                                    coarseWidth=bounce.Width,coarseHeight=bounce.Height,coarseLevels=bounce.CascadeLevels};
                                result.ownedGpuBytes=fine.SceneBytes+direct.Bytes+bounce.Bytes;MeasureGeometry(fine,bounce,sample.bounds,result);
                                frames.Add(result);direct.Release(final);
                                if(sample.name=="hall" && state=="all-on") result.isolatedSeedLeak=bounce.EvaluateIsolatedSeedLeak();
                            }
                        }
                        if(flashField!=null) direct.Release(flashField);direct.Release(sum);fine.Dispose();fine=null;
                        Debug.Log($"Гибрид: сохранено {sample.name}/{state}; кеш direct={cache.Count}, измерений источников={timings.Count}.");
                        if(previous!=null){report.darkCornerWorld=previous.darkCornerWorld;report.darkCornerDirect=previous.darkCornerDirect;report.darkCornerBounce=previous.darkCornerBounce;}
                        report.directTimings=previous?.directTimings??timings.ToArray();report.frames=previous==null?frames.ToArray():previous.frames.Where(f=>f.state!="flash").Concat(frames).ToArray();File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
                    }
                    ContactSheet(output,sample.name);
                }
                WriteIndex(output,samples,report);Debug.Log("Прототип гибридного света: кадры и измерения сохранены — "+output);EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally
            {
                fine?.Dispose();direct?.Dispose();
                foreach(var obj in new Object[]{visual,cameraRoot,gunRoot,flashRoot,fence}) if(obj!=null) Object.DestroyImmediate(obj);
                FactoryContentSources.Clear();RoomRadianceField.ClearSpareTextures();
            }
        }
        private static int ForceOn(Dictionary<string,(RoomSpriteAuthoring picture,int frame,Sprite sprite,RoomDefinition definition)> lamps,FactoryLevelRoot factory)
        {
            int count=0;foreach(string id in lamps.Keys.ToArray())
            {
                var p=lamps[id];if(p.definition.DecorFrameOn<=0) continue;
                string key=TileManifest.DecorFrameId(p.definition.Art,p.definition.DecorFrameOn);
                if(!factory.Catalog.Art.Manifest.Art.ContainsKey(key)) throw new InvalidOperationException("Нет включённого кадра лампы: "+id+" "+key);
                var sprite=factory.Catalog.Art.Tile(key).sprite;Set(p.picture,"frame",p.definition.DecorFrameOn);p.picture.GetComponent<SpriteRenderer>().sprite=sprite;
                lamps[id]=(p.picture,p.definition.DecorFrameOn,sprite,p.definition);count++;
            }
            return count;
        }
        private static HybridDirectPrototype.Source Source(HybridScenePrototype scene,SteadyLightEmitter emitter,float radius)
        {
            var a=emitter.LocalToWorld.MultiplyPoint3x4(emitter.Sprite.bounds.min);var b=emitter.LocalToWorld.MultiplyPoint3x4(emitter.Sprite.bounds.max);
            return new HybridDirectPrototype.Source {Id=emitter.Id,Radiance=emitter.Radiance,Samples=scene.Samples(emitter),ProjectedWidth=scene.ProjectedWidth(emitter),Radius=radius,
                Bounds=Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y))};
        }
        private static List<(HybridDirectPrototype.Source source,RenderTexture field)> Fields(HybridScenePrototype scene,HybridDirectPrototype direct,Dictionary<string,Cached> cache,
            Dictionary<string,(RoomSpriteAuthoring picture,int frame,Sprite sprite,RoomDefinition definition)> lamps,DoorState[] doors,List<SourceTime> timings)
        {
            var result=new List<(HybridDirectPrototype.Source,RenderTexture)>();
            foreach(var emitter in scene.Emitters)
            {
                bool lamp=lamps.TryGetValue(emitter.Id,out var entry);float radius=lamp?(float)(2*GetLampRadius(entry.definition)):emitter.Id.StartsWith("glow:")?6:12;
                double GetLampRadius(RoomDefinition definition)=>currentTable.Lamps[definition.Id].Radius;
                // currentTable is assigned by MakeSample/initial caller before Fields (see below).
                var source=Source(scene,emitter,radius);var reach=direct.SourceRect(source);
                string signature=emitter.Id+"|"+source.Radiance+"|"+source.Bounds+"|"+string.Join(";",doors.Where(d=>d.Blocks && reach.Overlaps(d.Bounds)).Select(d=>d.id));
                if(!cache.TryGetValue(signature,out var cached))
                {
                    var rt=direct.Build(source,out double ms);cached=new Cached {source=source,texture=rt};cache.Add(signature,cached);
                    timings.Add(new SourceTime {id=emitter.Id,kind=lamp?"lamp":emitter.Id.StartsWith("glow:")?"screen":"window",bounds=source.Bounds,reach=radius,milliseconds=ms});
                }
                result.Add((cached.source,cached.texture));
            }
            return result;
        }
        private static RoomLightingTable currentTable;
        private static Sample MakeSample(string name,FactoryRoomPlacement room,FactoryGrid grid,Dictionary<string,(RoomSpriteAuthoring picture,int frame,Sprite sprite,RoomDefinition definition)> lamps,DoorState[] doors,RoomLightingTable table)
        {
            currentTable=table;string prefix=$"factory:0:{room.Cell.X},{room.Cell.Y}:";
            string lamp=lamps.Keys.Where(id=>id.StartsWith(prefix,StringComparison.Ordinal) && table.LampFrameShare(lamps[id].definition,lamps[id].frame)>0).OrderBy(id=>id,StringComparer.Ordinal).First();
            var bounds=RoomBounds(room,grid);var door=doors.Where(d=>d.id.StartsWith(prefix,StringComparison.Ordinal)).OrderBy(d=>d.id,StringComparer.Ordinal).FirstOrDefault()
                ??doors.OrderBy(d=>Vector2.Distance(d.Bounds.center,bounds.center)).First();
            return new Sample {name=name,room=room,bounds=bounds,lamp=lamp,door=door};
        }
        private static Rect RoomBounds(FactoryRoomPlacement room,FactoryGrid grid)
        {float c=(float)grid.Level.Cell;return new Rect(room.Column*c,(grid.Level.Height-room.Row-room.Template.Height)*c,room.Template.Width*c,room.Template.Height*c);}
        private static FactoryRoomPlacement FindDarkCorner(RenderTexture direct,HybridBouncePrototype bounce,FactoryGrid grid,HashSet<FactoryRoomPlacement> used,Rect rect,Report report)
        {
            var fine=Read(direct);var indirect=Read((RenderTexture)bounce.Result);var coarse=Read((RenderTexture)bounce.CoarseScene);float best=0;FactoryRoomPlacement selected=null;
            for(int y=1;y<bounce.Height-1;y++)for(int x=1;x<bounce.Width-1;x++)
            {
                int fx=Math.Min(direct.width-1,x*4+2),fy=Math.Min(direct.height-1,y*4+2);float d=Rgb(fine[fy*direct.width+fx]);
                float b=Rgb(indirect[y*bounce.Width+x]);
                if(coarse[y*bounce.Width+x].a>=.99f || d>.002f || b<=best) continue;
                var world=new Vector2(rect.x+(x+.5f)*rect.width/bounce.Width,rect.y+(y+.5f)*rect.height/bounce.Height);
                int col=Mathf.Clamp(Mathf.FloorToInt(world.x/(float)grid.Level.Cell),0,grid.Level.Width-1),row=Mathf.Clamp(grid.Level.Height-1-Mathf.FloorToInt(world.y/(float)grid.Level.Cell),0,grid.Level.Height-1);
                var room=grid.Owner(col,row);if(used.Contains(room)) continue;best=b;selected=room;report.darkCornerWorld=world;report.darkCornerDirect=d;report.darkCornerBounce=b;
            }
            if(selected==null || best<.005f) throw new InvalidOperationException("Не найден реальный участок с direct<0.002 и bounce>0.005; нельзя заявлять отражённый тёмный угол.");return selected;
        }
        private static Color[] Read(RenderTexture target)
        {var previous=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);return image.GetPixels();}finally{RenderTexture.active=previous;Object.DestroyImmediate(image);}}
        private static void MeasureGeometry(HybridScenePrototype fine,HybridBouncePrototype bounce,Rect room,Result result)
        {
            var scene=Read((RenderTexture)fine.Scene);var coarse=Read((RenderTexture)bounce.CoarseScene);var light=Read((RenderTexture)bounce.Result);
            for(int y=1;y<bounce.Height-1;y++)for(int x=1;x<bounce.Width-1;x++)
            {
                var world=new Vector2(fine.WorldRect.x+(x+.5f)*fine.WorldRect.width/bounce.Width,fine.WorldRect.y+(y+.5f)*fine.WorldRect.height/bounce.Height);
                if(!room.Contains(world)) continue;int i=y*bounce.Width+x;if(coarse[i].a<.99f) continue;
                for(int dy=0;dy<bounce.Divisor;dy++)for(int dx=0;dx<bounce.Divisor;dx++) if(scene[(y*bounce.Divisor+dy)*fine.Width+x*bounce.Divisor+dx].a<.99f) result.closedFineOpenTexels++;
                bool horizontal=coarse[i-1].a<.99f && coarse[i+1].a<.99f,vertical=coarse[i-bounce.Width].a<.99f && coarse[i+bounce.Width].a<.99f;
                if(!horizontal && !vertical) continue;result.thinBarrierCandidates++;
                float b=horizontal?Mathf.Min(Rgb(light[i-1]),Rgb(light[i+1])):Mathf.Min(Rgb(light[i-bounce.Width]),Rgb(light[i+bounce.Width]));
                if(b>result.maxThinBarrierBackLight){result.maxThinBarrierBackLight=b;result.thinBarrierWorld=world;}
            }
        }
        private static float Rgb(Color color)=>Mathf.Max(color.r,Mathf.Max(color.g,color.b));
        private static void Save(Camera camera,string path)
        {
            var target=RenderTexture.GetTemporary(ImageWidth,ImageHeight,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
            var image=new Texture2D(ImageWidth,ImageHeight,TextureFormat.RGBA32,false);
            try{camera.targetTexture=target;camera.Render();camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,ImageWidth,ImageHeight),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(image);RenderTexture.ReleaseTemporary(target);}
        }
        private static void Fence(RenderTexture texture,Texture2D pixel)
        {var old=RenderTexture.active;try{RenderTexture.active=texture;pixel.ReadPixels(new Rect(0,0,1,1),0,0,false);}finally{RenderTexture.active=old;}}
        private static void ContactSheet(string output,string room)
        {
            var sheet=new Texture2D(ImageWidth*3,ImageHeight*States.Length,TextureFormat.RGBA32,false);
            try{for(int y=0;y<States.Length;y++)for(int x=0;x<3;x++){string mode=x==0?"today":"hybrid"+(x==1?2:4);var image=new Texture2D(2,2);try{image.LoadImage(File.ReadAllBytes(Path.Combine(output,room+"-"+States[y]+"-"+mode+".png")));sheet.SetPixels32(x*ImageWidth,(States.Length-1-y)*ImageHeight,ImageWidth,ImageHeight,image.GetPixels32());}finally{Object.DestroyImmediate(image);}}sheet.Apply();File.WriteAllBytes(Path.Combine(output,room+"-contact.png"),sheet.EncodeToPNG());}finally{Object.DestroyImmediate(sheet);}
        }
        private static void WriteIndex(string output,List<Sample> samples,Report report)
        {
            var html=new StringBuilder("<!doctype html><meta charset=utf-8><title>Сравнение света</title><style>body{background:#17191c;color:#ddd;font:16px sans-serif;margin:24px}table{width:100%;table-layout:fixed}img{width:100%}th{padding:12px}h2{margin-top:50px}</style><h1>Гибридный свет: выбор по изображению</h1><p>Одинаковая камера и экспозиция. Столбцы: текущий свет | отражения 1/2 | отражения 1/4. Прототип, без интеграции в игру.</p>");
            foreach(var sample in samples){html.Append($"<h2>{sample.name}: {sample.room.Cell.X},{sample.room.Cell.Y} — {sample.room.Template.Template}</h2><p>Лампа: {sample.lamp}; дверь: {sample.door.id}</p><table><tr><th>Сегодня</th><th>Гибрид 1/2</th><th>Гибрид 1/4</th></tr>");for(int s=0;s<States.Length;s++){html.Append("<tr><th colspan=3>"+Labels[s]+"</th></tr><tr>");foreach(string mode in new[]{"today","hybrid2","hybrid4"}) html.Append($"<td><a href='{sample.name}-{States[s]}-{mode}.png'><img src='{sample.name}-{States[s]}-{mode}.png'></a></td>");html.Append("</tr>");}html.Append("</table>");}
            html.Append("<p>Числа и ограничения: <a href='report.json'>report.json</a>; <a href='measurements.md'>таблицы и выводы</a>; <a href='geometry.md'>узкие проходы</a>. Время включает блокирующее ожидание GPU; это не hardware timestamps.</p>");File.WriteAllText(Path.Combine(output,"index.html"),html.ToString());
        }
    }
}
