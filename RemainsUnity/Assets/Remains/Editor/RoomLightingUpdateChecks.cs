using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Native Editor checks: real saved masks and source geometry, plus isolated numerical counterexamples.
    // No scene/prefab/resource is saved, and no original gameplay implementation is used.
    public static class RoomLightingUpdateChecks
    {
        [Serializable] private sealed class TransitionEvidence
        {
            public string transition;
            public int foregroundChanged,backgroundChanged,candidatePixels,sampledPixels;
            public long publishedVersion;
        }
        [Serializable] private sealed class Evidence
        {
            public int schema=1;
            public string sourcePrefab,sourceHatch,sourcePrefabSha256;
            public int width,height,sourceCount,uniqueDecodedTextures;
            public long decodedPixels;
            public TransitionEvidence[] transitions;
            public string limitation="Проверены данные Unity и численные карты света. Оригинальный runtime, пиксельное совпадение и GPU lifecycle этой проверкой не подтверждаются.";
        }
        public static void Run()
        {
            CheckActualOffice();
            CheckSimulationGeometry();
            CheckResourceMaskOutsideOrigin();
            CheckSaturatedSourcesAndBackground();
            CheckCoalescedAndCancelled();
            CheckOneWayEndpointsAndCapture();
            Debug.Log("Обновление света: PASS — сохранённый люк/маски, exact RGBA32 partial=full, насыщение, FG/BG, последний опубликованный baseline, отмена/устаревшие результаты, открытые балки/концы луча и события геометрии; исходные ресурсы не менялись.");
        }
        private static void CheckActualOffice()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryOfficeDraft.Path);
            Require(prefab!=null,"нет сохранённого офиса: "+FactoryOfficeDraft.Path);
            string original=FileDigest(FactoryOfficeDraft.Path);
            var authoring=prefab.GetComponent<RoomAuthoring>();
            Require(authoring!=null && prefab.GetComponent<RoomLightingAuthoring>()!=null,"офис не содержит authored геометрию/профиль света");
            var geometry=authoring.CreateLayout(false);
            var profile=prefab.GetComponent<RoomLightingAuthoring>().CreateProfile(authoring);
            string profileBefore=StateJson.ToJson(profile),geometryBefore=StateJson.ToJson(geometry);
            var masks=ResourceDigests(profile);
            var doors=RoomDoorContent.Read(authoring,"factory:0").doors;
            var hatch=doors.Single(door=>door.definition=="office-hatch1");
            Require(hatch.id.EndsWith(":original:plant:53:cLSBzRkMe7e7pefR",StringComparison.Ordinal) &&
                Mathf.Abs(hatch.x-12.8f)<.00001f && Mathf.Abs(hatch.y-14.4f)<.00001f && hatch.width==1.6f && hatch.height==.8f &&
                hatch.Blocks && !hatch.open && !hatch.Destroyed,"люк fixture не соответствует сохранённому источнику: "+hatch.id+" "+hatch.Bounds);
            var bounds=new Rect(geometry.left,geometry.bottom,geometry.right-geometry.left,geometry.top-geometry.bottom);
            RoomLayout State() => WithSurfaces(geometry,DoorGeometry.Compose(geometry.platforms,doors,"factory:0"));
            var maps=new RoomLightMaps(State(),bounds,profile);
            CheckFresh(maps,State(),profile,"начальная закрытая геометрия");
            var evidence=new List<TransitionEvidence>();
            hatch.open=true;
            var opened=State();var opening=Update(maps,opened,profile,"люк открыт");evidence.Add(opening);
            Require(opening.foregroundChanged+opening.backgroundChanged>=1,"открытый настоящий люк не изменил ни одной RGBA32 ячейки света");
            hatch.open=false;
            evidence.Add(Update(maps,State(),profile,"люк закрыт снова"));
            hatch.integrity.health=0;
            var destroyed=State();evidence.Add(Update(maps,destroyed,profile,"люк разрушен"));
            Require(destroyed.platforms.All(surface=>surface.sourceDoorId!=hatch.id),"разрушенный люк остался окклюдером");
            var same=maps.CreatePatch(RoomLightMaps.CaptureOcclusion(destroyed.platforms));
            Require(same.IsEmpty && same.Indices.Count==0,"неизменная геометрия повторно требует GPU/CPU patch");
            Require(FileDigest(FactoryOfficeDraft.Path)==original && StateJson.ToJson(profile)==profileBefore &&
                StateJson.ToJson(geometry)==geometryBefore && masks.All(pair=>FileDigest(pair.Key)==pair.Value),
                "пересчёт изменил исходный prefab, профиль, геометрию или PNG маски");
            var report=new Evidence {sourcePrefab=FactoryOfficeDraft.Path,sourceHatch=hatch.id,sourcePrefabSha256=original,
                width=maps.Width,height=maps.Height,sourceCount=maps.SourceCount,uniqueDecodedTextures=maps.UniqueDecodedTextures,
                decodedPixels=maps.DecodedPixels,transitions=evidence.ToArray()};
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/room-lighting-update-r43"));
            Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"report.json"),StateJson.ToJson(report,true));
            Debug.Log($"Свет люка {hatch.id}: open changedFG={opening.foregroundChanged}, changedBG={opening.backgroundChanged}, candidates={opening.candidatePixels}, sampled={opening.sampledPixels}; grid={maps.Width}x{maps.Height}. Не задано искусственное ожидание ровно15 ячеек.");
        }
        private static void CheckSimulationGeometry()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryOfficeDraft.Path);
            var authoring=prefab.GetComponent<RoomAuthoring>();
            var source=authoring.CreateLayout(false);string sourceJson=StateJson.ToJson(source);
            var doors=RoomDoorContent.Read(authoring,"factory:0").doors;
            var hatch=doors.Single(door=>door.definition=="office-hatch1");
            var actor=new ActorState {id="player:light-check",character="light-check",player=true,connected=true,
                owner="light-check",level="factory:0",x=9,y=.8f,health=100,pose="idle"};
            var world=new WorldState {actors=new List<ActorState> {actor},doors=doors.ToList()};
            int loads=0,events=0;long reportedRevision=-1;RoomLayout reportedRoom=null;
            var geometry=new SimulationGeometry(level=>
            {
                Require(level=="factory:0","fixture запросила неизвестный уровень: "+level);
                loads++;return StateJson.FromJson<RoomLayout>(sourceJson);
            });
            geometry.OcclusionChanged+=(level,room,revision)=>
            {
                Require(level=="factory:0","событие света другого уровня: "+level);
                events++;reportedRoom=room;reportedRevision=revision;
            };
            geometry.Reconcile(world);
            var bound=geometry.Get("factory:0");long version=geometry.OcclusionRevision("factory:0");
            Require(events==1 && loads==1 && ReferenceEquals(reportedRoom,bound) && reportedRevision==version,
                "первоначальная composed геометрия не сообщила одну действительную ревизию");
            Require(bound.platforms.Any(surface=>surface.sourceDoorId==hatch.id),"закрытый люк отсутствует в SimulationGeometry");
            actor.health--;actor.x++;actor.pose="walk";actor.owner="other-owner";actor.revision++;world.revision++;
            hatch.integrity.health--;
            geometry.Reconcile(world);
            Require(events==1 && loads==1 && geometry.OcclusionRevision("factory:0")==version && ReferenceEquals(geometry.Get("factory:0"),bound),
                "HP/поза/владение актёра или положительный HP люка запустили пересчёт препятствий");
            hatch.open=true;geometry.Reconcile(world);
            Require(events==2 && reportedRevision==version+1 && ReferenceEquals(reportedRoom,bound) &&
                bound.platforms.All(surface=>surface.sourceDoorId!=hatch.id),"открытие люка не обновило composed obstacles один раз");
            geometry.Reconcile(world);Require(events==2,"повтор того же состояния повторно уведомил свет");
            hatch.open=false;geometry.Reconcile(world);
            Require(events==3 && geometry.OcclusionRevision("factory:0")==version+2,"закрытие люка не изменило ревизию");
            hatch.surfaceTexture="light-check-material-only";geometry.Reconcile(world);
            Require(events==3,"смена материала без изменения solid bounds пересчитала окклюзию");
            hatch.integrity.health=0;geometry.Reconcile(world);
            Require(events==4 && geometry.OcclusionRevision("factory:0")==version+3 &&
                bound.platforms.All(surface=>surface.sourceDoorId!=hatch.id),"разрушение люка не удалило один solid volume");
            geometry.Reconcile(world);
            Require(events==4 && loads==1 && StateJson.ToJson(source)==sourceJson,"повтор разрушения изменил источник или повторно загрузил геометрию");
        }
        private static void CheckResourceMaskOutsideOrigin()
        {
            const string path="Assets/Remains/Resources/Original/Room/Hub/light-mask-light3-1.png";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Require(texture!=null && texture.isReadable,"нет читаемой исходной маски light3: "+path);
            string digest=FileDigest(path);
            var pixels=texture.GetPixels();Require(pixels.Any(pixel=>pixel.a>0 && pixel.maxColorComponent>.1f),"маска light3 пуста");
            var bounds=new Rect(-2,-2,12,4);
            var room=Fixture("resource-mask-outside-origin",Array.Empty<RoomSurface>(),bounds);
            var profile=new RoomLightProfile {ambient=new Color(.03f,.04f,.05f),backgroundAmbientScale=.3f,lights=new[]{
                new RoomLight {id="source-mask-outside-radius",x=0,y=0,radius=.5f,intensity=1.1f,backgroundIntensity=.3f,
                    maskTexture=texture,maskX=4,maskY=-2,maskWidth=4,maskHeight=4},
                new RoomLight {id="same-texture-second-placement",x=-1,y=0,radius=.5f,intensity=.2f,backgroundIntensity=.1f,
                    maskTexture=texture,maskX=5,maskY=-2,maskWidth=4,maskHeight=4},
                new RoomLight {id="same-texture-shadow",shadow=true,x=0,y=0,radius=.5f,intensity=.1f,
                    maskTexture=texture,maskX=4,maskY=-2,maskWidth=4,maskHeight=4}}};
            string input=StateJson.ToJson(profile);
            var maps=new RoomLightMaps(room,bounds,profile);
            Require(maps.SourceCount==3 && maps.UniqueDecodedTextures==1 && maps.DecodedPixels==(long)texture.width*texture.height,
                "общая PNG декодирована повторно по источникам или FG/BG");
            var blocked=WithSurfaces(room,new[]{Surface(2,-2,.25f,4)});
            var change=Update(maps,blocked,profile,"стена между source origin и далёкой маской");
            Require(change.foregroundChanged+change.backgroundChanged>=1,"radius-only dirty culling потерял исходную маску вне origin/radius");
            Update(maps,room,profile,"стена перед маской убрана");
            Require(maps.UniqueDecodedTextures==1 && maps.DecodedPixels==(long)texture.width*texture.height &&
                FileDigest(path)==digest && StateJson.ToJson(profile)==input,"patch повторно декодировал или изменил источник/маску");
        }
        private static void CheckSaturatedSourcesAndBackground()
        {
            var bounds=new Rect(-3,-2,6,4);var clear=Fixture("saturation",Array.Empty<RoomSurface>(),bounds);
            var profile=new RoomLightProfile {ambient=new Color(.15f,.18f,.2f),backgroundAmbientScale=.2f,lights=new[]{
                new RoomLight {id="lower-overlap",x=-2,y=-1,radius=20,intensity=4,backgroundIntensity=.15f,color=Color.white},
                new RoomLight {id="upper-overlap",x=-2,y=1,radius=20,intensity=4,backgroundIntensity=.08f,color=new Color(1,.8f,.9f)}}};
            var maps=new RoomLightMaps(clear,bounds,profile);
            int point=NearestIndex(maps,new Vector2(2,0));
            Require(((Color32)maps.Foreground[point]).r==255 && ((Color32)maps.Background[point]).r<255,
                "fixture не создаёт насыщенный FG и отдельный ненасыщенный BG");
            var lower=Surface(0,-1,.2f,.7f);var upper=Surface(0,.3f,.2f,.7f);
            Update(maps,WithSurfaces(clear,new[]{lower}),profile,"один источник перекрыт, другой всё ещё насыщает FG");
            Require(((Color32)maps.Foreground[point]).r==255,"инкрементальный пересчёт вычел вклад из уже clamped цвета");
            Update(maps,WithSurfaces(clear,new[]{lower,upper}),profile,"оба перекрывающихся источника закрыты");
            Require(((Color32)maps.Foreground[point]).r<255,"после обеих стен сохранён старый насыщенный FG");
            Update(maps,clear,profile,"оба источника снова доступны");
            Require(maps.Foreground.Where((color,index)=>!((Color32)color).Equals((Color32)maps.Background[index])).Any(),
                "FG/BG перепутаны или сведены к общему профилю");
            var reference=profile.ForBackground();
            Require(profile.lights[0].intensity==4 && reference.lights[0].intensity==.15f,"профиль фона изменил исходный источник");
        }
        private static void CheckCoalescedAndCancelled()
        {
            var bounds=new Rect(-3,-2,6,4);var a=Fixture("coalesced",Array.Empty<RoomSurface>(),bounds);
            var profile=PointProfile();var maps=new RoomLightMaps(a,bounds,profile);
            var baseline=Quantize(maps.Foreground);long version=maps.PublishedVersion;
            var first=new Color[maps.Width*maps.Height];var second=new Color[first.Length];
            maps.CopyPixels(first,second);
            Require(Equal(baseline,first) && Equal(Quantize(maps.Background),second),"CopyPixels не возвращает пару опубликованных буферов");
            first[0]=Color.magenta;
            Require(Equal(baseline,maps.Foreground),"CopyPixels выдал alias вместо снимка для GPU upload");
            bool rejected=false;
            try {maps.CopyPixels(first,first);} catch(ArgumentException) {rejected=true;}
            Require(rejected,"CopyPixels допустил FG/BG в один и тот же массив");
            var b=WithSurfaces(a,new[]{Surface(-.5f,-2,.2f,4)});
            var c=WithSurfaces(a,new[]{Surface(-.5f,-2,.2f,4),Surface(1,-2,.2f,4)});
            var patchB=maps.CreatePatch(RoomLightMaps.CaptureOcclusion(b.platforms));
            var patchC=maps.CreatePatch(RoomLightMaps.CaptureOcclusion(c.platforms));
            Require(patchB.BaseVersion==version && patchC.BaseVersion==version && maps.PublishedVersion==version &&
                Equal(baseline,maps.Foreground),"подготовка B/C изменила last published baseline до Commit");
            Require(maps.Commit(patchC),"последний patch C отклонён");CheckFresh(maps,c,profile,"coalesced A→C, не B→C");
            var publishedFG=Quantize(maps.Foreground);var publishedBG=Quantize(maps.Background);version=maps.PublishedVersion;
            Require(!maps.Commit(patchB) && !maps.Commit(patchC) && maps.PublishedVersion==version &&
                Equal(publishedFG,maps.Foreground) && Equal(publishedBG,maps.Background),"stale/duplicate patch изменил уже опубликованный C");
            var foreign=new RoomLightMaps(a,bounds,profile).CreatePatch(RoomLightMaps.CaptureOcclusion(b.platforms));
            Require(!maps.Commit(foreign) && Equal(publishedFG,maps.Foreground),"чужой cache patch опубликован без owner binding");
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();bool cancelled=false;
            try {maps.CreatePatch(RoomLightMaps.CaptureOcclusion(a.platforms),cancellation.Token);} catch(OperationCanceledException) {cancelled=true;}
            Require(cancelled && maps.PublishedVersion==version && Equal(publishedFG,maps.Foreground) && Equal(publishedBG,maps.Background),
                "отмена не прервала вычисление или частично изменила публикацию");
            var unchanged=maps.CreatePatch(RoomLightMaps.CaptureOcclusion(c.platforms));
            Require(unchanged.IsEmpty && unchanged.Indices.Count==0 && maps.Commit(unchanged) &&
                maps.PublishedVersion==version+1 && !maps.Commit(unchanged) && Equal(publishedFG,maps.Foreground) && Equal(publishedBG,maps.Background),
                "пустой patch не подтвердил новую геометрию ровно один раз или изменил опубликованные пиксели");
            var source=Surface(0,-2,.2f,4);var captured=RoomLightMaps.CaptureOcclusion(new[]{source});
            source.x=99;source.oneWay=true;
            Require(captured.Count==1 && captured[0].x==0,"immutable occlusion сохраняет изменяемый alias RoomSurface");
        }
        private static void CheckOneWayEndpointsAndCapture()
        {
            var bounds=new Rect(-2,-1,4,2);var clear=Fixture("light-endpoints",Array.Empty<RoomSurface>(),bounds);
            var profile=PointProfile();profile.lights[0].x=-1;profile.lights[0].y=0;
            var maps=new RoomLightMaps(clear,bounds,profile);var before=Quantize(maps.Foreground);
            var oneWay=Surface(0,-1,.2f,2);oneWay.oneWay=true;
            Require(RoomLightMaps.CaptureOcclusion(new[]{oneWay}).Count==0,"oneWay включён в сплошные окклюдеры");
            Update(maps,WithSurfaces(clear,new[]{oneWay}),profile,"открытая балка не перекрывает свет");
            Require(Equal(before,maps.Foreground),"oneWay изменила свет");
            var origin=Surface(-1.1f,-.1f,.2f,.2f);var endpoint=Surface(.9f,-.1f,.2f,.2f);
            var enclosed=WithSurfaces(clear,new[]{origin,endpoint});
            Update(maps,enclosed,profile,"поверхности на концах луча");
            var sample=new Vector2(1,0);var reference=(Color32)RoomLightMap.Sample(clear,profile,sample);
            Require(((Color32)RoomLightMap.Sample(enclosed,profile,sample)).Equals(reference),"концы луча стали внутренней преградой");
            Update(maps,WithSurfaces(clear,new[]{Surface(0,-1,.2f,2)}),profile,"настоящая стена между концами");
            Require(!Equal(before,maps.Foreground),"внутренняя сплошная стена не перекрыла свет");
        }
        private static TransitionEvidence Update(RoomLightMaps maps,RoomLayout target,RoomLightProfile profile,string context)
        {
            var fg=Quantize(maps.Foreground);var bg=Quantize(maps.Background);
            var patch=maps.CreatePatch(RoomLightMaps.CaptureOcclusion(target.platforms));
            Require(maps.Commit(patch),"не принят текущий patch: "+context);
            CheckFresh(maps,target,profile,context);
            // Check real changed cells, not merely unchanged ambient corners. Bounded to avoid
            // decoding the source masks through public Sample once per grid pixel.
            var changed=new List<int>();
            for(int i=0;i<fg.Length && changed.Count<3;i++)
                if(!fg[i].Equals((Color32)maps.Foreground[i]) || !bg[i].Equals((Color32)maps.Background[i])) changed.Add(i);
            if(changed.Count>0) CheckSamplePoints(maps,target,profile,changed);
            return new TransitionEvidence {transition=context,foregroundChanged=Changed(fg,maps.Foreground),backgroundChanged=Changed(bg,maps.Background),
                candidatePixels=patch.CandidatePixels,sampledPixels=patch.DirtyPixels,publishedVersion=maps.PublishedVersion};
        }
        private static void CheckFresh(RoomLightMaps incremental,RoomLayout target,RoomLightProfile profile,string context)
        {
            var fresh=new RoomLightMaps(target,incremental.Bounds,profile);
            Require(fresh.Width==incremental.Width && fresh.Height==incremental.Height &&
                Equal(Quantize(fresh.Foreground),incremental.Foreground) && Equal(Quantize(fresh.Background),incremental.Background),
                "partial != fresh full (RGBA32): "+context);
        }
        private static void CheckSamplePoints(RoomLightMaps maps,RoomLayout room,RoomLightProfile profile,IEnumerable<int> selected)
        {
            // Public Sample shares the evaluator; this checks preparation/coordinate/quantization
            // parity, not independent proof of a previous implementation or original runtime.
            var background=profile.ForBackground();
            foreach(int index in selected.Distinct())
            {
                var point=CellPoint(maps,index);
                Require(((Color32)RoomLightMap.Sample(room,profile,point)).Equals((Color32)maps.Foreground[index]) &&
                    ((Color32)RoomLightMap.Sample(room,background,point)).Equals((Color32)maps.Background[index]),
                    "cache не сохранил baseline Sample/quantization: cell="+index+", point="+point);
            }
        }
        private static RoomLightProfile PointProfile()=>new() {ambient=new Color(.12f,.13f,.14f),backgroundAmbientScale=.2f,
            lights=new[]{new RoomLight {id="point-fixture",x=-2,y=0,radius=7,intensity=.9f,backgroundIntensity=.3f,color=Color.white}}};
        private static RoomSurface Surface(float x,float y,float width,float height)=>new() {x=x,y=y,width=width,height=height,texture="fixture",movementSound="normal"};
        private static RoomLayout Fixture(string id,RoomSurface[] surfaces,Rect bounds)=>new() {id=id,platforms=surfaces,ladders=Array.Empty<RoomLadder>(),
            left=bounds.xMin,right=bounds.xMax,bottom=bounds.yMin,top=bounds.yMax};
        private static RoomLayout WithSurfaces(RoomLayout source,RoomSurface[] surfaces)=>new() {id=source.id,platforms=surfaces,ladders=source.ladders,
            left=source.left,right=source.right,bottom=source.bottom,top=source.top};
        private static Vector2 CellPoint(RoomLightMaps maps,int index)=>new(maps.Bounds.x+(index%maps.Width+.5f)*maps.Bounds.width/maps.Width,
            maps.Bounds.y+(index/maps.Width+.5f)*maps.Bounds.height/maps.Height);
        private static int NearestIndex(RoomLightMaps maps,Vector2 point)=>Mathf.Clamp(Mathf.FloorToInt((point.y-maps.Bounds.y)/maps.Bounds.height*maps.Height),0,maps.Height-1)*maps.Width+
            Mathf.Clamp(Mathf.FloorToInt((point.x-maps.Bounds.x)/maps.Bounds.width*maps.Width),0,maps.Width-1);
        private static Color32[] Quantize(IReadOnlyList<Color> pixels) {var result=new Color32[pixels.Count];for(int i=0;i<result.Length;i++) result[i]=pixels[i];return result;}
        private static bool Equal(Color32[] expected,IReadOnlyList<Color> actual)=>expected.Length==actual.Count && expected.Where((pixel,index)=>!pixel.Equals((Color32)actual[index])).Any()==false;
        private static int Changed(Color32[] before,IReadOnlyList<Color> after)=>before.Where((pixel,index)=>!pixel.Equals((Color32)after[index])).Count();
        private static Dictionary<string,string> ResourceDigests(RoomLightProfile profile)
        {
            var result=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var light in profile.lights)
            {
                var texture=light.maskTexture??(string.IsNullOrEmpty(light.mask)?null:Resources.Load<Texture2D>(light.mask));
                if(texture==null) continue;
                string path=AssetDatabase.GetAssetPath(texture);Require(!string.IsNullOrEmpty(path),"actual source mask не является сохранённым asset: "+light.id);
                result[path]=FileDigest(path);
            }
            return result;
        }
        private static string FileDigest(string assetPath)
        {
            string full=Path.GetFullPath(Path.Combine(Application.dataPath,"..",assetPath));
            using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(full))).Replace("-","").ToLowerInvariant();
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException("Обновление света: "+reason);}
    }
}
