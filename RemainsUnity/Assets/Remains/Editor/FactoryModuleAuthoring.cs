using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryModuleAuthoring
    {
        private const string GraphicsName="Сохранённое оформление модуля";
        public static GameObject Create(FactoryModuleAuthoringInput input)
        {
            Validate(input);ImportResources(input.resources);
            var root=new GameObject(input.name);
            try
            {
                var room=RoomDraftGeometry.Create(root,input.geometry);
                root.AddComponent<RoomPhysicalContent>().Configure(input.physicalPatch);
                root.AddComponent<RoomDoorContent>().Configure(input.doorPatch);
                var layout=StateJson.Copy(input.geometry);layout.artwork=Artwork(input).ToArray();
                var graphics=RoomVisual.Build(layout,new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()});
                graphics.name=GraphicsName;graphics.transform.SetParent(root.transform,false);
                var surfaces=root.GetComponentsInChildren<RoomSurfaceAuthoring>();
                var ladders=root.GetComponentsInChildren<RoomLadderAuthoring>();
                var art=graphics.GetComponentsInChildren<RoomArtworkAuthoring>();
                foreach(var surface in surfaces) surface.transform.SetParent(graphics.transform,true);
                foreach(var ladder in ladders) ladder.transform.SetParent(graphics.transform,true);
                foreach(var binding in input.sourceBindings)
                    foreach(int index in binding.artworkIndices) art[index].transform.SetParent(surfaces[binding.surfaceIndex].transform,true);
                var rungs=graphics.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="Пристенная лестница").ToArray();
                if(rungs.Length!=ladders.Length) throw Error(input,"не найден единственный исходный rung для каждой лестницы");
                for(int i=0;i<ladders.Length;i++) rungs[i].transform.SetParent(ladders[i].transform,true);
                room.UseAuthoredArtwork();
                foreach(var binding in input.physical) PlacePhysical(root,input.records.Single(r=>r.recordId==binding.recordId),binding,input.sourceIndex);
                foreach(var binding in input.doors)
                {
                    var record=input.records.Single(r=>r.recordId==binding.recordId);
                    var instance=(GameObject)PrefabUtility.InstantiatePrefab(binding.prefab,root.transform);
                    instance.name="Дверь · "+record.code;instance.transform.localPosition=binding.anchor;
                    instance.AddComponent<RoomDoorAuthoring>().Configure(input.sourceId+":"+record.code,false,binding.movementSound);
                }
                ValidateCreated(room,input);return root;
            }
            catch {UnityEngine.Object.DestroyImmediate(root);throw;}
        }
        public static void Save(GameObject root,FactoryModuleAuthoringInput input)
        {
            Validate(input);var room=root.GetComponent<RoomAuthoring>();ValidateCreated(room,input);
            if(root.GetComponent<RoomLightingAuthoring>()==null) throw Error(input,"до сохранения требуется явное освещение");
            var graphics=root.transform.Find(GraphicsName)?.gameObject;
            if(graphics==null || graphics.GetComponent<RoomMeshLifetime>()==null) throw Error(input,"нет несохранённого оформления для первого импорта");
            RoomArtworkAssets.Save(graphics,System.IO.Path.GetDirectoryName(input.artworkFolder).Replace('\\','/'),System.IO.Path.GetFileName(input.artworkFolder));
            if(input.installLadderLandings)
            {
                if(input.landingRecords==null) FactoryLadderLandingAuthoring.InstallOn(root,input.sourceIndex);
                else FactoryLadderLandingAuthoring.InstallOn(root,input.sourceIndex,input.landingRecords);
            }
            var report=ScriptableObject.CreateInstance<RoomImportReportAsset>();report.Configure(input.sourceId,input.sourceHash,StateJson.Copy(input.records));
            AssetDatabase.CreateAsset(report,input.reportPath);root.AddComponent<RoomImportAudit>().Configure(report);
            var saved=PrefabUtility.SaveAsPrefabAsset(root,input.prefabPath);
            if(saved==null) throw Error(input,"Unity не сохранил префаб");
            AssetDatabase.SaveAssets();report.WarnIncomplete(saved);
        }
        public static void Validate(FactoryModuleAuthoringInput input)
        {
            if(input==null || input.geometry==null || input.decor==null || input.additionalArtwork==null || input.resources==null || input.sourceBindings==null || input.records==null || input.physical==null || input.doors==null)
                throw new InvalidOperationException("Импорт модуля: неполный явный вход.");
            if(input.sourceIndex<0 || input.sourceId!="original:plant:"+input.sourceIndex || !Hex(input.sourceHash,64) || string.IsNullOrWhiteSpace(input.name) || string.IsNullOrWhiteSpace(input.physicalPatch) || string.IsNullOrWhiteSpace(input.doorPatch))
                throw Error(input,"неполная идентичность источника/версии расстановки");
            foreach(string path in new[]{input.prefabPath,input.artworkFolder,input.reportPath})
                if(string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/Remains/RoomDrafts/",StringComparison.Ordinal) || path.Contains("..") || AssetDatabase.LoadMainAssetAtPath(path)!=null || AssetDatabase.IsValidFolder(path))
                    throw Error(input,"путь существует или не относится к новому модулю: "+path);
            if(input.landingRecords!=null)
            {
                if(!input.installLadderLandings) throw Error(input,"явные посадки заданы при выключенной установке");
                new FactoryLadderLandingSource {schema=1,records=input.landingRecords}.Validate();
                if(input.landingRecords.Any(r=>r.sourceRoom!=input.sourceIndex)) throw Error(input,"посадки принадлежат другому источнику");
            }
            ValidateLayout(input.geometry,input);ValidateLayout(input.decor,input);
            if(input.decor.platforms.Length!=0 || input.decor.ladders.Length!=0 || input.geometry.left!=input.decor.left || input.geometry.right!=input.decor.right || input.geometry.bottom!=input.decor.bottom || input.geometry.top!=input.decor.top)
                throw Error(input,"декор меняет физическую геометрию или границы");
            var keys=new HashSet<string>();
            foreach(var resource in input.resources)
            {
                ValidateResource(resource,input.sourceId);
                if(!keys.Add(resource.textureKey)) throw Error(input,"повтор ресурса: "+resource.textureKey);
            }
            foreach(var art in Artwork(input))
            {
                if(art==null || !Rect(art.x,art.y,art.width,art.height) || art.slopeDirection!=0) throw Error(input,"нет полного отдельного прямоугольника дополнительного рисунка");
                if(!keys.Contains(art.texture)) throw Error(input,"нет декларации рисунка: "+art.texture);
                var resource=input.resources.Single(r=>r.textureKey==art.texture);var b=resource.sourceBoundsPixelsYDown;
                if(!art.repeat && (!Same(art.width,(b[2]-b[0])/resource.sourcePixelsPerMetre) || !Same(art.height,(b[3]-b[1])/resource.sourcePixelsPerMetre)))
                    throw Error(input,"полный рисунок масштабирован без отдельного доказанного правила: "+art.texture);
                if(art.repeat && (resource.sampler.u!="Repeat" || !art.stretchY && resource.sampler.v!="Repeat"))
                    throw Error(input,"повтор рисунка не соответствует явным осям sampler: "+art.texture);
            }
            foreach(var ladder in input.geometry.ladders)
            {
                if(string.IsNullOrWhiteSpace(ladder.artwork) || !keys.Contains(ladder.artwork)) throw Error(input,"лестница не имеет явного исходного рисунка: "+ladder.artwork);
                var resource=input.resources.Single(r=>r.textureKey==ladder.artwork);
                // RoomVisual's existing rung contract uses a one-metre canvas and 100 raster pixels/metre.
                if(!Same(resource.pixelWidth/(resource.exportZoom*resource.sourcePixelsPerMetre),1) || !Same(resource.exportZoom*resource.sourcePixelsPerMetre,100))
                    throw Error(input,"ресурс лестницы не соответствует существующему явному rung контракту: "+ladder.artwork);
            }
            var used=new HashSet<int>();var boundSurfaces=new HashSet<int>();
            foreach(var binding in input.sourceBindings)
            {
                if(binding==null || binding.surfaceIndex<0 || binding.surfaceIndex>=input.geometry.platforms.Length || !boundSurfaces.Add(binding.surfaceIndex) || binding.artworkIndices==null || binding.artworkIndices.Length==0)
                    throw Error(input,"неверная связь поверхность/рисунок");
                foreach(int i in binding.artworkIndices)
                    if(i<0 || i>=input.geometry.artwork.Length || !used.Add(i)) throw Error(input,"повтор/неизвестный индекс привязанного рисунка: "+i);
            }
            if(boundSurfaces.Count!=input.geometry.platforms.Length) throw Error(input,"не каждая общая поверхность имеет единственную связь с рисунком");
            if(input.records.Any(r=>r==null || string.IsNullOrWhiteSpace(r.recordId) || string.IsNullOrWhiteSpace(r.code) || string.IsNullOrWhiteSpace(r.kind) || string.IsNullOrWhiteSpace(r.sourceData) || string.IsNullOrWhiteSpace(r.reason)) ||
                input.records.Select(r=>r.recordId).Distinct().Count()!=input.records.Length || input.records.Select(r=>r.code).Distinct().Count()!=input.records.Length)
                throw Error(input,"записи источника не имеют единственного ID/XML/явного решения");
            var bound=new HashSet<string>();
            foreach(var physical in input.physical)
            {
                var record=RequireRecord(input,physical?.recordId,bound);var definition=physical.definition?.CreateDefinition();
                if(definition==null || physical.definition.Prefab==null || definition.id!=record.kind || !Finite(physical.anchor)) throw Error(input,"нет согласованного определения/anchor: "+record.recordId);
                if(StateJson.ToJson(PropCatalog.Get(definition.id))!=StateJson.ToJson(definition)) throw Error(input,"ассет отличается от загруженного canonical типа: "+record.recordId);
                PropCatalog.Create("authoring:"+record.recordId,definition.id,"factory:0",physical.anchor);
                if(physical.container && physical.loot==null && !physical.explicitlyEmpty || !physical.container && physical.loot!=null || physical.loot!=null && physical.explicitlyEmpty)
                    throw Error(input,"не задано явное содержимое контейнера: "+record.recordId);
                physical.loot?.Read();
            }
            foreach(var door in input.doors)
            {
                var record=RequireRecord(input,door?.recordId,bound);var view=door.prefab==null?null:door.prefab.GetComponent<DoorView>();
                if(view?.Definition==null || !Finite(door.anchor) || string.IsNullOrWhiteSpace(door.movementSound)) throw Error(input,"нет явного префаба/anchor двери: "+record.recordId);
                view.Definition.Validate();
            }
            foreach(var record in input.records)
                if(record.installed && record.kind!="player" && !bound.Contains(record.recordId)) throw Error(input,"installed без реальной привязки: "+record.recordId);
        }
        public static void ValidateResource(FactoryModuleResource r,string context)
        {
            if(r==null || string.IsNullOrWhiteSpace(r.textureKey) || r.textureKey.Contains("..") || string.IsNullOrWhiteSpace(r.file) || !Hex(r.sha256,64) || r.pixelWidth<=0 || r.pixelHeight<=0 || r.sampler==null ||
                !ValidSampler(r.sampler.u) || !ValidSampler(r.sampler.v) || !float.IsFinite(r.exportZoom) || r.exportZoom<=0 || !float.IsFinite(r.sourcePixelsPerMetre) || r.sourcePixelsPerMetre<=0 ||
                r.sourceBoundsPixelsYDown==null || r.sourceBoundsPixelsYDown.Length!=4 || r.sourceBoundsPixelsYDown.Any(v=>!float.IsFinite(v)))
                throw new InvalidOperationException("Ресурс модуля "+context+": неполная декларация, key="+r?.textureKey);
            var b=r.sourceBoundsPixelsYDown;
            if(b[2]<=b[0] || b[3]<=b[1] || Mathf.Abs((b[2]-b[0])*r.exportZoom-r.pixelWidth)>.01f || Mathf.Abs((b[3]-b[1])*r.exportZoom-r.pixelHeight)>.01f)
                throw new InvalidOperationException("Ресурс модуля "+context+": pixel canvas не соответствует доказанным bounds/zoom, key="+r.textureKey);
            string source=ResolveSource(r.file);
            if(!File.Exists(source) || Hash(source)!=r.sha256) throw new InvalidOperationException("Ресурс модуля "+context+": файл отсутствует/изменился, key="+r.textureKey+", file="+source);
            string target=ResourcePath(r,source);
            if(File.Exists(target) && Hash(target)!=r.sha256) throw new InvalidOperationException("Ресурс модуля "+context+": назначение отличается от авторского файла, key="+r.textureKey+", file="+target);
        }
        private static void ImportResources(FactoryModuleResource[] resources)
        {
            foreach(var r in resources)
            {
                string source=ResolveSource(r.file),path=ResourcePath(r,source);
                bool create=!File.Exists(path);
                if(!create && Hash(path)!=r.sha256) throw new InvalidOperationException("Ресурс модуля отличается от авторского файла; key="+r.textureKey+", file="+path);
                if(create) {Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));File.Copy(source,path,false);AssetDatabase.ImportAsset(path);}
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                if(importer==null) throw new InvalidOperationException("Нет импортёра ресурса модуля: "+path);
                var u=r.sampler.u=="Repeat"?TextureWrapMode.Repeat:TextureWrapMode.Clamp;var v=r.sampler.v=="Repeat"?TextureWrapMode.Repeat:TextureWrapMode.Clamp;
                if(create)
                {
                    importer.maxTextureSize=Mathf.Max(128,Mathf.NextPowerOfTwo(Mathf.Max(r.pixelWidth,r.pixelHeight)));importer.npotScale=TextureImporterNPOTScale.None;
                    importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;importer.alphaIsTransparency=false;
                    importer.wrapModeU=u;importer.wrapModeV=v;importer.SaveAndReimport();
                }
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(texture==null || texture.width!=r.pixelWidth || texture.height!=r.pixelHeight || importer.textureCompression!=TextureImporterCompression.Uncompressed || importer.npotScale!=TextureImporterNPOTScale.None || importer.mipmapEnabled || importer.wrapModeU!=u || importer.wrapModeV!=v || create && importer.alphaIsTransparency ||
                    (UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(texture.graphicsFormat) && !TextureSizingPolicy.IsApprovedCompression(path)))
                    throw new InvalidOperationException("Импорт ресурса модуля теряет размер/формат/sampler; key="+r.textureKey+", file="+path);
            }
        }
        private static void PlacePhysical(GameObject root,RoomSourceRecord record,FactoryModulePhysicalBinding binding,int index)
        {
            var definition=binding.definition.CreateDefinition();var marker=new GameObject(definition.name+" · "+record.code);
            marker.transform.SetParent(root.transform,false);marker.transform.localPosition=binding.anchor;
            RoomPhysicalPlacement placement;
            if(binding.container) {var container=marker.AddComponent<RoomContainerPlacement>();container.SetLoot(binding.loot);placement=container;}
            else placement=marker.AddComponent<RoomPropPlacement>();
            placement.Configure("original:plant:"+index+":"+record.code,binding.definition);
            var preview=(GameObject)PrefabUtility.InstantiatePrefab(binding.definition.Prefab.gameObject,marker.transform);
            preview.transform.localPosition=binding.definition.Prefab.VisualAnchor==PropVisualAnchor.PlacementAnchor?Vector3.zero:new Vector3(0,definition.height*.5f,0);
        }
        private static void ValidateCreated(RoomAuthoring room,FactoryModuleAuthoringInput input)
        {
            if(room==null) throw Error(input,"нет авторской комнаты");var actual=room.CreateLayout();
            if(actual.platforms.Length!=input.geometry.platforms.Length || actual.ladders.Length!=input.geometry.ladders.Length || actual.artwork.Length!=Artwork(input).Count())
                throw Error(input,"потеряны поверхности/лестницы/рисунки");
            var surfaces=actual.platforms.ToList();
            foreach(var s in input.geometry.platforms)
            {
                int i=surfaces.FindIndex(a=>Same(a.x,s.x)&&Same(a.y,s.y)&&Same(a.width,s.width)&&Same(a.height,s.height)&&a.texture==s.texture&&a.oneWay==s.oneWay&&a.shotsPassThrough==s.shotsPassThrough&&a.slopeDirection==s.slopeDirection&&a.movementSound==s.movementSound);
                if(i<0) throw Error(input,"изменена поверхность при привязке рисунка");surfaces.RemoveAt(i);
            }
            var artwork=actual.artwork.ToList();
            foreach(var s in Artwork(input))
            {
                int i=artwork.FindIndex(a=>Same(a.x,s.x)&&Same(a.y,s.y)&&Same(a.width,s.width)&&Same(a.height,s.height)&&a.texture==s.texture&&a.order==s.order&&a.repeat==s.repeat&&a.slopeDirection==s.slopeDirection);
                if(i<0) throw Error(input,"потеряна исходная регистрация рисунка: "+s.texture);artwork.RemoveAt(i);
            }
        }
        private static void ValidateLayout(RoomLayout layout,FactoryModuleAuthoringInput input)
        {
            if(layout.platforms==null || layout.ladders==null || layout.artwork==null || !float.IsFinite(layout.left+layout.right+layout.bottom+layout.top+layout.spawnX+layout.spawnY) || layout.left>=layout.right || layout.bottom>=layout.top)
                throw Error(input,"неполная геометрия/границы");
            foreach(var s in layout.platforms)
                if(s==null || !Rect(s.x,s.y,s.width,s.height) || string.IsNullOrWhiteSpace(s.texture) || string.IsNullOrWhiteSpace(s.movementSound) || Math.Abs(s.slopeDirection)>1 || s.slopeDirection!=0&&(!s.oneWay||!s.shotsPassThrough)) throw Error(input,"неподдерживаемая поверхность");
            foreach(var a in layout.artwork)
                if(a==null || !Rect(a.x,a.y,a.width,a.height) || a.slopeDirection!=0) throw Error(input,"рисунок должен иметь полный самостоятельный прямоугольник, без имитации ступени тонкой балкой");
            foreach(var l in layout.ladders)
                if(l==null || !float.IsFinite(l.x+l.bottom+l.top+l.artworkOffset+l.movementOffset) || l.bottom>=l.top || l.facing!=LadderFacing.Left&&l.facing!=LadderFacing.Right) throw Error(input,"неверная лестница");
        }
        private static RoomSourceRecord RequireRecord(FactoryModuleAuthoringInput input,string id,HashSet<string> bound)
        {var r=input.records.SingleOrDefault(r=>r.recordId==id);if(r==null || !r.installed || !bound.Add(id)) throw Error(input,"неоднозначная привязка исходной записи: "+id);return r;}
        private static bool Rect(float x,float y,float w,float h)=>float.IsFinite(x)&&float.IsFinite(y)&&float.IsFinite(w)&&float.IsFinite(h)&&w>0&&h>0;
        private static bool Finite(Vector2 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y);
        private static bool Same(float a,float b)=>Mathf.Abs(a-b)<.0001f;
        private static bool Hex(string s,int length)=>s?.Length==length&&s.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');
        private static bool ValidSampler(string s)=>s=="Repeat"||s=="Clamp";
        private static IEnumerable<RoomArtwork> Artwork(FactoryModuleAuthoringInput input)=>input.geometry.artwork.Concat(input.decor.artwork).Concat(input.additionalArtwork);
        private static string ResourcePath(FactoryModuleResource r,string source)=>"Assets/Remains/Resources/Original/Room/"+r.textureKey+System.IO.Path.GetExtension(source);
        private static string ResolveSource(string path)=>System.IO.Path.IsPathRooted(path)?path:System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../..",path));
        private static string Hash(string path) {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        private static InvalidOperationException Error(FactoryModuleAuthoringInput input,string reason)=>new("Импорт модуля "+input.sourceId+": "+reason+".");
    }
}
