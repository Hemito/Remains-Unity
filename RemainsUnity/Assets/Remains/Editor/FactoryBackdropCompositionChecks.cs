using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    public static class FactoryBackdropCompositionChecks
    {
        private const string WindowPixels="0aa571a41d62adc4bca83bb3d8a2af355028299255eb12fc3fdd68b676389248";
        [Serializable] public sealed class RectEvidence {public float x,y,width,height;public RectEvidence(Rect value){x=value.x;y=value.y;width=value.width;height=value.height;}}
        [Serializable] public sealed class LayerEvidence {public string record,operation;public int order;public RectEvidence bounds;}
        [Serializable] public sealed class RenderEvidence
        {public string module,evidence="native Unity GPU; original runtime unverified";public int width=1920,height=1080,holePixels,bloodPixels,outsideHolePixels,outsideBloodPixels;public RectEvidence camera;public LayerEvidence[] layers;}
        [Serializable] public sealed class MultiplyEvidence
        {public string colorSpace,evidence="actual BackdropMultiply material GPU byte check";public int maximumError;public Color32[] expected,actual;public bool transparentNeutral,destinationAlphaPreserved,foregroundUnaffected;}
        [Serializable] public sealed class StateDifference
        {public string path,before,after;public bool acceptedLocalPrecision;public double difference;public float tolerance;}
        [Serializable] public sealed class TransformEvidence
        {public string type,path;public bool matched;public Vector3 before,after,expected;public float maximumError;}
        [Serializable] public sealed class TranslationEvidence
        {
            public string module,contract="ReadRect returns module-local coordinates; world markers translate with the module";
            public Vector3 shift;public bool success;
            public RoomLayout sourceGeometry,cloneGeometry;public RoomContentBatch sourceContent,cloneContent;
            public StateDifference[] geometryDifferences,contentDifferences;
            public TransformEvidence[] worldTransforms;
        }
        private static readonly Regex LocalCoordinates=new(@"^(?:geometry\.platforms\[\d+\]\.(?:x|y|width|height)|geometry\.ladders\[\d+\]\.(?:x|bottom|top)|content\.physical\.(?:props|containers)\[\d+\]\.(?:x|y)|content\.doors\.doors\[\d+\]\.(?:x|y)|content\.npcs\.actors\[\d+\]\.(?:x|y)|content\.mechanisms\.movingPlatforms\[\d+\]\.(?:startX|startY|endX|endY)|content\.mechanisms\.mechanismButtons\[\d+\]\.(?:x|y)|content\.travelPoints\[\d+\]\.(?:x|y))$",RegexOptions.CultureInvariant);
        public static void Run()
        {
            CheckPixels();CheckTranslationComparison();FactoryBackdropComposition.Validate();
            var data=FactoryBackdropComposition.Read();
            foreach(string module in new[]{"FactoryOffice53","FactoryWarehouse40"})
            {
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryBackdropComposition.PrefabPath(module));var room=source.GetComponent<RoomAuthoring>();
                CheckWindowPreservation(room);var originalGeometry=room.CreateLayout(false);var batch=RoomContentBatch.Read(room,"factory:0");
                var copy=UnityEngine.Object.Instantiate(source);
                try
                {
                    var clone=copy.GetComponent<RoomAuthoring>();copy.transform.position=new Vector3(module=="FactoryOffice53"?76.8f:115.2f,0,0);
                    FactoryBackdropComposition.ValidateModule(clone,data,module);
                    CheckTranslation(module,room,clone,originalGeometry,batch);
                    var composition=copy.GetComponent<RoomBackdropCompositionAuthoring>();var hole=composition.Layers.Single(l=>l.operation=="erase");
                    hole.artwork.transform.localPosition+=Vector3.right*.1f;Reject(composition.Validate,"Перемещённое отверстие не отклонено");hole.artwork.transform.localPosition-=Vector3.right*.1f;
                    var layer=composition.Layers[0];var renderer=layer.artwork.GetComponent<MeshRenderer>();int order=renderer.sortingOrder;
                    renderer.sortingOrder=0;Reject(composition.Validate,"Неправильный слой объекта не отклонён");renderer.sortingOrder=order;composition.Validate();
                }
                finally {UnityEngine.Object.DestroyImmediate(copy);}
            }
            Debug.Log("Композиция шести декораций: PASS, exact208/98/6, старые7 window pixels unchanged, clip без rescale, source resources, stale placement/depth negatives, сохранённые geometry/ID/content.");
        }
        private static void CheckTranslation(string module,RoomAuthoring source,RoomAuthoring clone,RoomLayout geometry,RoomContentBatch content)
        {
            // ReadRect removes the module transform. Its output remains local even
            // when the module is placed at a connected-factory world offset.
            var cloneGeometry=clone.CreateLayout(false);var cloneContent=RoomContentBatch.Read(clone,"factory:0");
            var geometryDifferences=StateDifferences(StateJson.ToJson(geometry),StateJson.ToJson(cloneGeometry),"geometry");
            var contentDifferences=StateDifferences(StateJson.ToJson(content),StateJson.ToJson(cloneContent),"content");
            var shift=clone.transform.position-source.transform.position;
            var before=Markers(source);var after=Markers(clone);var transforms=new List<TransformEvidence>();
            foreach(string key in before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal))
            {
                bool hasBefore=before.TryGetValue(key,out var original),hasAfter=after.TryGetValue(key,out var copied);
                var from=hasBefore?original.transform.position:Vector3.zero;var to=hasAfter?copied.transform.position:Vector3.zero;
                var expected=from+shift;
                float error=Mathf.Max(Mathf.Abs(to.x-expected.x),Mathf.Abs(to.y-expected.y),Mathf.Abs(to.z-expected.z));
                transforms.Add(new TransformEvidence {path=key,type=(original??copied).GetType().FullName,matched=hasBefore&&hasAfter,
                    before=from,after=to,expected=expected,maximumError=error});
            }
            bool success=geometryDifferences.Concat(contentDifferences).All(d=>d.acceptedLocalPrecision) &&
                transforms.All(t=>t.matched && float.IsFinite(t.maximumError) && t.maximumError<=.00001f);
            var evidence=new TranslationEvidence {module=module,shift=shift,success=success,sourceGeometry=geometry,cloneGeometry=cloneGeometry,
                sourceContent=content,cloneContent=cloneContent,geometryDifferences=geometryDifferences,contentDifferences=contentDifferences,worldTransforms=transforms.ToArray()};
            string path=System.IO.Path.Combine(EvidenceFolder(),module+"-translation.json");File.WriteAllText(path,StateJson.ToJson(evidence,true));
            var failed=geometryDifferences.Concat(contentDifferences).FirstOrDefault(d=>!d.acceptedLocalPrecision);
            var failedTransform=transforms.FirstOrDefault(t=>!t.matched || !float.IsFinite(t.maximumError) || t.maximumError>.00001f);
            Require(success,"Вложенное смещение нарушило локальную геометрию/ID/contents или мировой перенос: module="+module+
                ", field="+failed?.path+", before="+failed?.before+", after="+failed?.after+", marker="+failedTransform?.path+", evidence="+path);
            Debug.Log("Композиция "+module+": локальные geometry/content сохранены; float-diff="+(geometryDifferences.Length+contentDifferences.Length)+
                ", world markers="+transforms.Count+", shift="+shift+"; typed evidence="+path);
        }
        private static Dictionary<string,Component> Markers(RoomAuthoring room)
        {
            var result=new Dictionary<string,Component>(StringComparer.Ordinal);
            foreach(var component in room.GetComponentsInChildren<Component>(true))
            {
                if(component is not (RoomSurfaceAuthoring or RoomLadderAuthoring or RoomPhysicalPlacement or RoomActorPlacement or
                    RoomDoorAuthoring or RoomTravelPointAuthoring or RoomMovingPlatformPlacement or RoomMechanismButtonPlacement))continue;
                string path="";
                for(var parent=component.transform;parent!=room.transform;parent=parent.parent)
                    path="/"+parent.GetSiblingIndex()+":"+parent.name+path;
                string key=component.GetType().FullName+path;
                Require(result.TryAdd(key,component),"Повтор маркера в проверяемой иерархии: "+key); // No ambiguous match by object name.
            }
            return result;
        }
        // Only declared ReadRect coordinates may differ at the existing 1e-5m
        // geometry precision. IDs, definitions, inventory, extensions and every
        // other numeric field retain exact serialized equality.
        public static StateDifference[] StateDifferences(string before,string after,string root)
        {
            if(root!="geometry" && root!="content")throw new InvalidOperationException("Сравнение композиции: неизвестный корень "+root);
            var result=new List<StateDifference>();Compare(JToken.Parse(before),JToken.Parse(after),root,result);return result.ToArray();
        }
        private static void Compare(JToken before,JToken after,string path,List<StateDifference> result)
        {
            if(JToken.DeepEquals(before,after))return;
            if(before is JObject from && after is JObject to)
            {
                foreach(string name in from.Properties().Select(p=>p.Name).Concat(to.Properties().Select(p=>p.Name)).Distinct(StringComparer.Ordinal))
                    Compare(from[name],to[name],path+"."+name,result);
                return;
            }
            if(before is JArray left && after is JArray right)
            {
                for(int index=0;index<Math.Max(left.Count,right.Count);index++)
                    Compare(index<left.Count?left[index]:null,index<right.Count?right[index]:null,path+"["+index+"]",result);
                return;
            }
            double difference=0;bool numeric=before!=null && after!=null &&
                before.Type is JTokenType.Integer or JTokenType.Float && after.Type is JTokenType.Integer or JTokenType.Float;
            // These authored coordinate fields are floats. Compare their actual values,
            // not the difference between shortened decimal spellings in diagnostic JSON.
            if(numeric)difference=LocalCoordinates.IsMatch(path)?Math.Abs((double)before.Value<float>()-after.Value<float>()):Math.Abs(before.Value<double>()-after.Value<double>());
            const float tolerance=.00001f;
            result.Add(new StateDifference {path=path,before=before?.ToString(Newtonsoft.Json.Formatting.None)??"(absent)",
                after=after?.ToString(Newtonsoft.Json.Formatting.None)??"(absent)",difference=difference,tolerance=LocalCoordinates.IsMatch(path)?tolerance:0,
                acceptedLocalPrecision=numeric && LocalCoordinates.IsMatch(path) && double.IsFinite(difference) && difference<=tolerance});
        }
        public static void CheckTranslationComparison()
        {
            const string geometry="{\"platforms\":[{\"x\":22.4,\"width\":0.8,\"texture\":\"concrete\"}],\"ladders\":[{\"x\":3,\"bottom\":0,\"top\":8}]}";
            Require(StateDifferences(geometry,geometry,"geometry").Length==0,"Неизменная геометрия отклонена");
            Require(StateDifferences(geometry,geometry.Replace("22.4","22.400005"),"geometry").All(d=>d.acceptedLocalPrecision),"Точность локальной матрицы отклонена");
            Require(StateDifferences(geometry,geometry.Replace("22.4","99.2"),"geometry").Any(d=>!d.acceptedLocalPrecision),"Мировое смещение ошибочно принято как локальная геометрия");
            Require(StateDifferences(geometry,geometry.Replace("22.4","22.40002"),"geometry").Any(d=>!d.acceptedLocalPrecision),"Изменение геометрии сверх точности принято");
            const string content="{\"physical\":{\"containers\":[{\"id\":\"factory:0:source\",\"x\":22.4,\"definition\":\"box\",\"items\":[{\"id\":\"item\",\"count\":3}],\"extensions\":{\"x\":22.4}}]}}";
            Require(StateDifferences(content,content.Replace("factory:0:source","factory:0:other"),"content").Any(d=>!d.acceptedLocalPrecision),"Изменение stable ID принято");
            Require(StateDifferences(content,content.Replace("\"count\":3","\"count\":2"),"content").Any(d=>!d.acceptedLocalPrecision),"Изменение содержимого принято");
            Require(StateDifferences(content,content.Replace("\"box\"","\"other\""),"content").Any(d=>!d.acceptedLocalPrecision),"Изменение определения принято");
            Require(StateDifferences(content,content.Replace("\"extensions\":{\"x\":22.4}","\"extensions\":{\"x\":22.400005}"),"content").Any(d=>!d.acceptedLocalPrecision),"Изменение unknown extension ошибочно принято как координата");
            Require(StateDifferences(content,content.Replace("\"x\":22.4,\"definition\"","\"x\":22.400005,\"definition\""),"content").All(d=>d.acceptedLocalPrecision),"Локальная координата контейнера отклонена");
        }
        public static void CheckPixels()
        {
            var gradient=new Texture2D(2,1,TextureFormat.RGBA32,false,true);
            var half=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
            try
            {
                gradient.SetPixels32(new[]{new Color32(0,0,0,255),new Color32(255,255,255,255)});gradient.Apply();
                half.SetPixels32(new[]{new Color32(128,128,128,255)});half.Apply();
                var area=new Rect(0,0,.06f,.02f);
                var clipped=new WindowMaskRegion {sourceRecord="pixel-test:clipped-source",coverage=gradient,bakedBounds=new Rect(-.02f,0,.08f,.02f)};
                var pixels=RoomArtworkVisibilityBake.Pixels(area,new[]{clipped},out int w,out int h);
                Require(w==3 && h==1 && pixels.Select(p=>p.r).SequenceEqual(new byte[]{191,64,0}),"Выходящий за комнату источник растянут вместо обрезки");
                var middle=new WindowMaskRegion {sourceRecord="pixel-test:half",coverage=half,bakedBounds=area};
                var once=RoomArtworkVisibilityBake.Pixels(area,new[]{middle},out _,out _);
                var twice=RoomArtworkVisibilityBake.Pixels(area,new[]{middle,middle},out _,out _);
                Require(once.All(p=>p.r==127) && twice.All(p=>p.r==63 && p.a==255),"Перекрывающиеся erase маски неверно объединяют оставшуюся видимость");
                var outside=new WindowMaskRegion {sourceRecord="pixel-test:outside",coverage=half,bakedBounds=new Rect(1,1,.1f,.1f)};
                Reject(()=>RoomArtworkVisibilityBake.Pixels(area,new[]{outside},out _,out _),"Полностью внешний источник молча принят");
                middle.coverage=null;Reject(()=>RoomArtworkVisibilityBake.Pixels(area,new[]{middle},out _,out _),"Отсутствующий источник молча подменён");
            }
            finally {UnityEngine.Object.DestroyImmediate(gradient);UnityEngine.Object.DestroyImmediate(half);}
        }
        private static void CheckWindowPreservation(RoomAuthoring room)
        {
            if(room.GetComponent<RoomImportAudit>().Report.SourceId!="original:plant:53")return;
            var mask=room.GetComponent<RoomWindowMaskAuthoring>();var composition=room.GetComponent<RoomBackdropCompositionAuthoring>();
            var hole=mask.Regions.Single(r=>composition.Layers.Any(l=>l.operation=="erase" && l.sourceRecord==r.sourceRecord));
            var windows=mask.Regions.Where(r=>r!=hole).ToArray();Require(windows.Length==7,"Потеряны семь исходных окон");
            var before=RoomArtworkVisibilityBake.Pixels(mask.Bounds,windows,out int width,out int height);
            Require(Hash(before)==WindowPixels,"Atlas без отверстия отличается от независимого saved-native baseline семи окон");
            var after=mask.Visibility.GetPixels32();int insideChanges=0,outsideChanges=0;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                if(before[y*width+x].Equals(after[y*width+x]))continue;
                var p=new Vector2(mask.Bounds.x+(x+.5f)/50,mask.Bounds.y+(y+.5f)/50);
                if(hole.bakedBounds.Contains(p))insideChanges++;else outsideChanges++;
            }
            Require(insideChanges>0 && outsideChanges==0,"Новое отверстие изменило пиксели вне своего исходного холста");
        }
        public static void RenderScreenshots()
        {
            Require(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null,"Кадры GPU требуют batchmode без -nographics");
            Run();RoomAuthoringScenes.ValidateSavedOverrides();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderMultiply();foreach(string module in new[]{"FactoryOffice53","FactoryWarehouse40"})RenderModule(module);
        }
        private static void RenderModule(string module)
        {
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FactoryBackdropComposition.PrefabPath(module)));
            var room=root.GetComponent<RoomAuthoring>();var mask=root.GetComponent<RoomWindowMaskAuthoring>();var composition=root.GetComponent<RoomBackdropCompositionAuthoring>();
            RoomLightingPreview.Refresh(room);
            var camera=new GameObject("Предпросмотр композиции · "+module).AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=11;
            camera.transform.position=new Vector3(19.2f,10,-10);camera.backgroundColor=new Color(.08f,.09f,.1f);camera.aspect=1920f/1080;
            var hole=composition.Layers.Single(l=>l.operation=="erase");var blood=composition.Layers.Where(l=>l.operation=="multiply").Select(l=>l.artwork.GetComponent<MeshRenderer>()).ToArray();
            var originals=mask.Layers.Select(l=>l.sharedMaterial).ToArray();var temporary=originals.Select(m=>new Material(m)).ToArray();
            var regions=mask.Regions.Where(r=>r.sourceRecord!=hole.sourceRecord).ToArray();
            var noHole=new Texture2D(mask.Visibility.width,mask.Visibility.height,TextureFormat.RGBA32,false,true);
            if(regions.Length==0)noHole.SetPixels32(Enumerable.Repeat(new Color32(255,255,255,255),noHole.width*noHole.height).ToArray());
            else noHole.SetPixels32(RoomArtworkVisibilityBake.Pixels(mask.Bounds,regions,out _,out _));
            noHole.Apply();noHole.filterMode=mask.Visibility.filterMode;noHole.wrapMode=TextureWrapMode.Clamp;
            float width=22*camera.aspect;var envelope=new Rect(19.2f-width/2,-1,width,22);string directory=EvidenceFolder();
            try
            {
                foreach(var renderer in blood)renderer.enabled=false;hole.artwork.GetComponent<MeshRenderer>().enabled=false;
                for(int i=0;i<temporary.Length;i++) {temporary[i].SetTexture("_ArtworkVisibility",noHole);mask.Layers[i].sharedMaterial=temporary[i];}
                var baseline=Capture(camera,System.IO.Path.Combine(directory,module+"-before.png"));
                for(int i=0;i<originals.Length;i++)mask.Layers[i].sharedMaterial=originals[i];hole.artwork.GetComponent<MeshRenderer>().enabled=true;
                var cutout=Capture(camera,System.IO.Path.Combine(directory,module+"-erasure.png"));
                foreach(var renderer in blood)renderer.enabled=true;
                var complete=Capture(camera,System.IO.Path.Combine(directory,module+"-complete.png"));
                int holeChanged=0,holeOutside=0,bloodChanged=0,bloodOutside=0;
                var holeArea=Expand(hole.bakedBounds,.06f);var bloodAreas=composition.Layers.Where(l=>l.operation=="multiply").Select(l=>Expand(l.bakedBounds,.06f)).ToArray();
                for(int y=0;y<1080;y++)for(int x=0;x<1920;x++)
                {
                    int index=y*1920+x;var p=new Vector2(envelope.x+(x+.5f)*envelope.width/1920,envelope.y+(y+.5f)*envelope.height/1080);
                    if(!baseline[index].Equals(cutout[index])) {holeChanged++;if(!holeArea.Contains(p))holeOutside++;}
                    if(!cutout[index].Equals(complete[index])) {bloodChanged++;if(!bloodAreas.Any(a=>a.Contains(p)))bloodOutside++;}
                }
                File.WriteAllText(System.IO.Path.Combine(directory,module+"-evidence.json"),StateJson.ToJson(new RenderEvidence {module=module,holePixels=holeChanged,bloodPixels=bloodChanged,
                    outsideHolePixels=holeOutside,outsideBloodPixels=bloodOutside,camera=new RectEvidence(envelope),layers=composition.Layers.Select(l=>new LayerEvidence {record=l.sourceRecord,operation=l.operation,
                        order=l.artwork.Read(room).order,bounds=new RectEvidence(l.bakedBounds)}).ToArray()},true));
                Require(holeChanged>100 && holeOutside==0 && bloodOutside==0 && (blood.Length==0?bloodChanged==0:bloodChanged>100),"GPU: изменение отсутствует/выходит за согласованные графические границы: "+module);
                Debug.Log("Композиция GPU "+module+": holePixels="+holeChanged+", bloodPixels="+bloodChanged+", outside0;1920×1080 PNG/evidence сохранены.");
            }
            finally
            {
                for(int i=0;i<originals.Length;i++)mask.Layers[i].sharedMaterial=originals[i];
                foreach(var material in temporary)UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(noHole);
                RoomLightingPreview.Clear();UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(root);
            }
        }
        private static void RenderMultiply()
        {
            var root=new GameObject("Проверка настоящего multiply shader");root.AddComponent<SortingGroup>();
            var camera=new GameObject("Камера multiply проверки").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1;
            camera.aspect=1920f/1080;camera.transform.position=new Vector3(1.5f,.5f,-10);camera.backgroundColor=Color.black;
            var destination=new Texture2D(1,1,TextureFormat.RGBA32,false);destination.SetPixels32(new[]{new Color32(153,204,230,255)});destination.Apply();destination.filterMode=FilterMode.Point;
            var texture=new Texture2D(3,1,TextureFormat.RGBA32,false);var raw=new[]{new Color32(102,51,204,0),new Color32(102,51,204,128),new Color32(102,51,204,255)};
            texture.SetPixels32(raw);texture.Apply();texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
            var normal=new Material(Shader.Find("Remains/WorldLit"));normal.mainTexture=destination;
            var multiply=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Remains/Resources/Presentation/BackdropMultiply.shader"));multiply.mainTexture=texture;
            var background=Quad(root.transform,normal,-40);var factor=Quad(root.transform,multiply,-20);
            float enabled=Shader.GetGlobalFloat("_WorldLightEnabled");Shader.SetGlobalFloat("_WorldLightEnabled",0);
            try
            {
                string directory=EvidenceFolder();factor.GetComponent<MeshRenderer>().enabled=false;var baseline=Capture(camera,System.IO.Path.Combine(directory,"multiply-baseline.png"));
                factor.GetComponent<MeshRenderer>().enabled=true;var complete=Capture(camera,System.IO.Path.Combine(directory,"multiply-result.png"));
                var expected=new Color32[3];var actual=new Color32[3];int error=0;
                for(int i=0;i<3;i++)
                {
                    var screen=camera.WorldToViewportPoint(new Vector3(i+.5f,.5f,0));int index=Mathf.FloorToInt(screen.y*1080)*1920+Mathf.FloorToInt(screen.x*1920);
                    Color src=raw[i],dst=new Color32(153,204,230,255);float alpha=src.a;
                    if(QualitySettings.activeColorSpace==ColorSpace.Linear) {src=src.linear;dst=dst.linear;}
                    var color=new Color(dst.r*Mathf.Lerp(1,src.r,alpha),dst.g*Mathf.Lerp(1,src.g,alpha),dst.b*Mathf.Lerp(1,src.b,alpha),1);
                    if(QualitySettings.activeColorSpace==ColorSpace.Linear)color=color.gamma;
                    expected[i]=color;actual[i]=complete[index];error=Mathf.Max(error,Error(expected[i],actual[i]));
                }
                // Later ordinary artwork is not multiplied even when it fully overlaps the blood quad.
                background.GetComponent<MeshRenderer>().sortingOrder=0;var foreground=Capture(camera,System.IO.Path.Combine(directory,"multiply-foreground.png"));
                bool frontEqual=baseline.SequenceEqual(foreground);bool neutral=actual[0].Equals(expected[0]);bool alphaPreserved=complete.Zip(baseline,(a,b)=>a.a==b.a).All(equal=>equal);
                File.WriteAllText(System.IO.Path.Combine(directory,"multiply-evidence.json"),StateJson.ToJson(new MultiplyEvidence {colorSpace=QualitySettings.activeColorSpace.ToString(),maximumError=error,
                    expected=expected,actual=actual,transparentNeutral=neutral,destinationAlphaPreserved=alphaPreserved,foregroundUnaffected=frontEqual},true));
                Require(error<=3 && neutral && alphaPreserved && frontEqual,"GPU multiply нарушает alpha→neutral/color-space/sorting контракт, maxError="+error);
            }
            finally
            {
                Shader.SetGlobalFloat("_WorldLightEnabled",enabled);foreach(var filter in root.GetComponentsInChildren<MeshFilter>())UnityEngine.Object.DestroyImmediate(filter.sharedMesh);
                UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(camera.gameObject);
                UnityEngine.Object.DestroyImmediate(normal);UnityEngine.Object.DestroyImmediate(multiply);UnityEngine.Object.DestroyImmediate(destination);UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        private static GameObject Quad(Transform parent,Material material,int order)
        {
            var obj=new GameObject("Рисунок GPU проверки");obj.transform.SetParent(parent,false);
            obj.AddComponent<MeshFilter>().sharedMesh=new Mesh {vertices=new[]{Vector3.zero,new Vector3(3,0,0),new Vector3(0,1,0),new Vector3(3,1,0)},
                uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one},colors=Enumerable.Repeat(Color.white,4).ToArray(),triangles=new[]{0,2,1,2,3,1}};
            var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.sortingOrder=order;return obj;
        }
        private static Color32[] Capture(Camera camera,string path)
        {
            var target=new RenderTexture(1920,1080,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1920,1080,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
            try
            {
                SortingGroup.UpdateAllSortingGroups();camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());return image.GetPixels32();
            }
            finally {RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);}
        }
        private static string EvidenceFolder()
        {string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.reference/tests/factory-backdrop-composition"));Directory.CreateDirectory(path);return path;}
        private static int Error(Color32 a,Color32 b)=>Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b),Mathf.Abs(a.a-b.a));
        private static Rect Expand(Rect r,float amount)=>new Rect(r.x-amount,r.y-amount,r.width+2*amount,r.height+2*amount);
        private static string Hash(Color32[] pixels)
        {
            var bytes=new byte[pixels.Length*4];for(int i=0;i<pixels.Length;i++) {bytes[i*4]=pixels[i].r;bytes[i*4+1]=pixels[i].g;bytes[i*4+2]=pixels[i].b;bytes[i*4+3]=pixels[i].a;}
            using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
        }
        private static void Reject(Action action,string message)
        {bool rejected=false;try {action();}catch(InvalidOperationException){rejected=true;}Require(rejected,message);}
        private static void Require(bool condition,string message)
        {if(!condition)throw new InvalidOperationException("Композиция фона: "+message);}
    }
}
