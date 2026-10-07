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
    // Additive artwork installer for six reviewed records. Saved modules remain runtime truth.
    public static class FactoryBackdropComposition
    {
        public const string ResearchFolder="FactoryBackdropComposition";
        public const string OfficeAssets="Assets/Remains/RoomDrafts/FactoryOffice53BackdropComposition";
        public const string WarehouseAssets="Assets/Remains/RoomDrafts/FactoryWarehouse40BackdropComposition";
        [Serializable] internal sealed class Manifest
        {
            public int schema;public string sourceXmlSha256;
            public Mask[] masks;public Placement[] placements;
            public Dictionary<string,Dictionary<string,string>> historicalInputs;
            public Dictionary<string,int> currentArtworkCounts;
        }
        [Serializable] internal sealed class Mask {public int frame;public string file,sha256;public int[] pixels;}
        [Serializable] private sealed class OriginalManifest {public OriginalPlacement[] decorPlacements;}
        [Serializable] private sealed class OriginalPlacement {public string recordId;public int artworkIndex;}
        [Serializable] private sealed class OriginalDecor {public RoomArtwork[] artwork;}
        [Serializable] internal sealed class Placement
        {
            public string module,recordId,operation,diffuseFile,diffuseSha256;
            public int? maskFrame;
            public RoomArtwork artwork,sourceArtwork;
        }
        internal static Manifest Read()
        {
            var data=StateJson.FromJson<Manifest>(File.ReadAllText(Research("manifest.json")));
            if(data?.schema!=1 || data.placements?.Length!=6 || data.masks?.Length!=2 || data.placements.Select(p=>p.recordId).Distinct().Count()!=6 ||
                data.historicalInputs==null || data.currentArtworkCounts==null || data.currentArtworkCounts["FactoryOffice53"]!=208 || data.currentArtworkCounts["FactoryWarehouse40"]!=98)
                throw new InvalidOperationException("Композиция завода: неполный version1 manifest шести записей.");
            var ids=new[]{135,136,137,138,139}.Select(n=>"original:plant:53:record:"+n).Append("original:plant:40:record:82").OrderBy(x=>x,StringComparer.Ordinal);
            if(!ids.SequenceEqual(data.placements.Select(p=>p.recordId).OrderBy(x=>x,StringComparer.Ordinal)))
                throw new InvalidOperationException("Композиция завода: изменены шесть явно согласованных source ID.");
            foreach(var module in data.historicalInputs)
                foreach(var file in module.Value)
                    RequireHash(System.IO.Path.Combine(Repository(),"Tools/Research",module.Key,file.Key),file.Value);
            foreach(var mask in data.masks)
            {
                if(mask.frame is not (1 or 6) || mask.pixels?.Length!=2 || mask.pixels[0]!=1789 || mask.pixels[1]!=1341)
                    throw new InvalidOperationException("Композиция завода: изменён исходный mask canvas/frame.");
                RequireHash(System.IO.Path.Combine(Repository(),mask.file),mask.sha256);
            }
            foreach(var p in data.placements)
            {
                if(p.artwork==null || p.sourceArtwork==null || p.artwork.repeat || p.artwork.shade!=1 ||
                    p.operation is not ("erase" or "multiply") || p.artwork.order!=(p.operation=="erase"?-30:-20) ||
                    p.operation=="erase" && p.maskFrame is not (1 or 6) || p.operation=="multiply" && p.maskFrame!=null)
                    throw new InvalidOperationException("Композиция завода: неверный явный operation/frame/depth, record="+p.recordId);
                if(!SameRect(p.artwork,p.sourceArtwork) || p.artwork.texture!=p.sourceArtwork.texture)
                    throw new InvalidOperationException("Композиция завода: изменена source registration, record="+p.recordId);
                string originalFolder=System.IO.Path.Combine(Repository(),"Tools/Research",p.module);
                var original=StateJson.FromJson<OriginalManifest>(File.ReadAllText(System.IO.Path.Combine(originalFolder,"manifest.json")));
                var decor=StateJson.FromJson<OriginalDecor>(File.ReadAllText(System.IO.Path.Combine(originalFolder,"decor.json")));
                int index=original.decorPlacements.Single(r=>r.recordId==p.recordId).artworkIndex;
                if(StateJson.ToJson(decor.artwork[index])!=StateJson.ToJson(p.sourceArtwork))
                    throw new InvalidOperationException("Композиция завода: новый manifest расходится с неизменённой исторической записью, record="+p.recordId);
                RequireHash(System.IO.Path.Combine(Repository(),p.diffuseFile),p.diffuseSha256);
            }
            return data;
        }
        [MenuItem("Remains/Завод/Установить композицию шести декораций")]
        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();var data=Read();ValidateResources(data);
            // Both modules preflight before the first saved mutation.
            foreach(string module in new[]{"FactoryOffice53","FactoryWarehouse40"})
            {
                var source=Require<GameObject>(PrefabPath(module));Preflight(source.GetComponent<RoomAuthoring>(),data,module);
            }
            InstallModule(data,"FactoryOffice53");InstallModule(data,"FactoryWarehouse40");
            AssetDatabase.SaveAssets();Validate();
            Debug.Log("Композиция завода: сохранены Office135/136/137/138/139 и Warehouse82;208/98 рисунков, геометрия/объекты/свет/старые GUID сохранены. Original runtime не сравнивался.");
        }
        public static void Validate()
        {
            var data=Read();ValidateResources(data);
            foreach(string module in new[]{"FactoryOffice53","FactoryWarehouse40"})
                ValidateModule(Require<GameObject>(PrefabPath(module)).GetComponent<RoomAuthoring>(),data,module);
        }
        public static void RenderScreenshots()=>FactoryBackdropCompositionChecks.RenderScreenshots();
        public static void InstallAndBuild()
        {Install();FactoryBackdropCompositionChecks.Run();RenderScreenshots();PrototypeBuild.Build();}
        private static void Preflight(RoomAuthoring room,Manifest data,string module)
        {
            if(room==null || !room.HasAuthoredArtwork || room.GetComponent<RoomImportAudit>()?.Report.SourceId!=(module=="FactoryOffice53"?"original:plant:53":"original:plant:40"))
                throw new InvalidOperationException("Композиция завода: нет текущего authored source module="+module);
            if(room.GetComponent<RoomBackdropCompositionAuthoring>()!=null) {ValidateModule(room,data,module);return;}
            if(room.CreateLayout().artwork.Length!=data.currentArtworkCounts[module]-(module=="FactoryOffice53"?5:1))
                throw new InvalidOperationException("Композиция завода: изменено прежнее точное число рисунков, module="+module);
            foreach(var p in data.placements.Where(p=>p.module==module))
                if(room.GetComponentsInChildren<RoomArtworkAuthoring>(true).Any(a=>a.Read(room).texture==p.artwork.texture))
                    throw new InvalidOperationException("Композиция завода: обнаружен ручной/частичный рисунок; не заменён, record="+p.recordId);
            var mask=room.GetComponent<RoomWindowMaskAuthoring>();
            if(module=="FactoryOffice53")
            {
                if(mask==null || mask.Regions.Length!=7 || mask.Layers.Length!=38)
                    throw new InvalidOperationException("Композиция офиса: нужны прежние7 окон/38 bindings без частичной установки.");
                mask.Validate();RoomArtworkVisibilityBake.VerifyPixels(mask);
            }
            else if(mask!=null) throw new InvalidOperationException("Композиция склада: существующая ручная маска не заменена.");
            var backdrop=room.GetComponentsInChildren<RoomArtworkAuthoring>(true).Single(a=>a.Read(room).texture=="Factory46/backwall");
            if(backdrop.Read(room).order!=-40 || mask!=null && mask.Layers.Contains(backdrop.GetComponent<MeshRenderer>()))
                throw new InvalidOperationException("Композиция завода: общий задник изменён/вырезается, module="+module);
            string folder=Assets(module);
            if(AssetDatabase.IsValidFolder(folder)) throw new InvalidOperationException("Композиция завода: каталог уже содержит неустановленные assets; автоматическая замена запрещена, path="+folder);
        }
        private static void InstallModule(Manifest data,string module)
        {
            var root=PrefabUtility.LoadPrefabContents(PrefabPath(module));
            try
            {
                var room=root.GetComponent<RoomAuthoring>();Preflight(room,data,module);
                if(room.GetComponent<RoomBackdropCompositionAuthoring>()!=null)return;
                string geometry=StateJson.ToJson(room.CreateLayout(false)),content=StateJson.ToJson(RoomContentBatch.Read(room,"factory:0"));
                string lights=StateJson.ToJson(room.GetComponent<RoomLightingAuthoring>().CreateProfile(room));
                var existing=root.GetComponentsInChildren<RoomArtworkAuthoring>(true);
                var previous=existing.Select(a=>new ArtworkGuard(a,room)).ToArray();
                string prefabGuid=AssetDatabase.AssetPathToGUID(PrefabPath(module));
                string folder=Assets(module);AssetDatabase.CreateFolder("Assets/Remains/RoomDrafts",System.IO.Path.GetFileName(folder));
                var group=new GameObject("Композиция исходного фона");group.transform.SetParent(root.transform,false);
                var layers=new List<BackdropCompositionLayer>();
                foreach(var p in data.placements.Where(p=>p.module==module)) layers.Add(CreateLayer(group.transform,room,p,folder));
                var hole=layers.Single(l=>l.operation=="erase");
                var coverage=Require<Texture2D>(data.masks.Single(m=>m.frame==hole.maskFrame).file.Substring("RemainsUnity/".Length));
                var region=new WindowMaskRegion {sourceRecord=hole.sourceRecord,artwork=hole.artwork,coverage=coverage,bakedBounds=hole.bakedBounds};
                var mask=root.GetComponent<RoomWindowMaskAuthoring>();string atlasFolder;
                if(mask!=null)
                {
                    mask.Configure(mask.Visibility,mask.Bounds,mask.Regions.Append(region).ToArray(),mask.Layers.Append(hole.artwork.GetComponent<MeshRenderer>()).ToArray());
                    atlasFolder=FactoryOfficeWindowMasks.Folder;
                }
                else
                {
                    var targets=existing.Where(a=>a.Read(room).order==-29).Select(a=>a.GetComponent<MeshRenderer>()).Append(hole.artwork.GetComponent<MeshRenderer>()).ToArray();
                    if(targets.Length<=1) throw new InvalidOperationException("Композиция склада: отсутствует задняя геометрия−29; общий задник не используется как подмена.");
                    mask=root.AddComponent<RoomWindowMaskAuthoring>();mask.Configure(null,new Rect(0,0,38.4f,20),new[]{region},targets);
                    atlasFolder=folder;
                }
                RoomArtworkVisibilityBake.Bake(mask,atlasFolder);mask.Validate();
                root.AddComponent<RoomBackdropCompositionAuthoring>().Configure(module=="FactoryOffice53"?"original:plant:53":"original:plant:40",layers.ToArray());
                if(StateJson.ToJson(room.CreateLayout(false))!=geometry || StateJson.ToJson(RoomContentBatch.Read(room,"factory:0"))!=content ||
                    StateJson.ToJson(room.GetComponent<RoomLightingAuthoring>().CreateProfile(room))!=lights || AssetDatabase.AssetPathToGUID(PrefabPath(module))!=prefabGuid)
                    throw new InvalidOperationException("Композиция завода изменила geometry/content/light/GUID; prefab не сохранён, module="+module);
                foreach(var item in previous)item.Verify(room);
                ValidateModule(room,data,module);PrefabUtility.SaveAsPrefabAsset(root,PrefabPath(module));AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        private static BackdropCompositionLayer CreateLayer(Transform parent,RoomAuthoring room,Placement placement,string folder)
        {
            var art=placement.artwork;var obj=new GameObject("Исходная декорация · "+placement.recordId);obj.transform.SetParent(parent,false);
            obj.transform.localPosition=new Vector3(art.x,art.y,0);
            var mesh=new Mesh {name=placement.recordId,vertices=new[]{Vector3.zero,new Vector3(art.width,0,0),new Vector3(0,art.height,0),new Vector3(art.width,art.height,0)},
                uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one},triangles=new[]{0,2,1,2,3,1},colors=Enumerable.Repeat(Color.white,4).ToArray()};
            var texture=Require<Texture2D>(placement.diffuseFile.Substring("RemainsUnity/".Length));
            var shader=Require<Shader>("Assets/Remains/Resources/Presentation/"+(placement.operation=="multiply"?"BackdropMultiply":"WorldLit")+".shader");
            var material=new Material(shader) {name=placement.recordId};material.mainTexture=texture;material.SetColor("_Color",Color.white);
            material.SetFloat("_BackgroundLighting",1);material.SetFloat("_Emission",0);
            string key=System.IO.Path.GetFileName(placement.recordId.Replace(':','-'));
            AssetDatabase.CreateAsset(mesh,folder+"/"+key+".asset");AssetDatabase.CreateAsset(material,folder+"/"+key+".mat");
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.sortingOrder=art.order;
            var authoring=obj.AddComponent<RoomArtworkAuthoring>();authoring.Configure(art);
            return new BackdropCompositionLayer {sourceRecord=placement.recordId,operation=placement.operation,artwork=authoring,diffuse=texture,
                maskFrame=placement.maskFrame??0,bakedBounds=new Rect(art.x,art.y,art.width,art.height)};
        }
        internal static void ValidateModule(RoomAuthoring room,Manifest data,string module)
        {
            var current=room.GetComponent<RoomBackdropCompositionAuthoring>()??throw new InvalidOperationException("Композиция ещё не установлена, module="+module);
            current.Validate();var expected=data.placements.Where(p=>p.module==module).ToArray();
            if(current.SourceId!=(module=="FactoryOffice53"?"original:plant:53":"original:plant:40") || current.Layers.Length!=expected.Length || room.CreateLayout().artwork.Length!=data.currentArtworkCounts[module])
                throw new InvalidOperationException("Композиция: изменён точный набор208/98/6, module="+module);
            foreach(var p in expected)
            {
                var layer=current.Layers.Single(l=>l.sourceRecord==p.recordId);var a=layer.artwork.Read(room);
                if(!SameRect(a,p.artwork) || a.texture!=p.artwork.texture || a.order!=p.artwork.order || layer.operation!=p.operation || layer.maskFrame!=(p.maskFrame??0) ||
                    layer.diffuse!=Require<Texture2D>(p.diffuseFile.Substring("RemainsUnity/".Length)))
                    throw new InvalidOperationException("Композиция: изменены source artwork/registration, record="+p.recordId);
            }
            var mask=room.GetComponent<RoomWindowMaskAuthoring>();RoomArtworkVisibilityBake.VerifyPixels(mask);
            if(mask.Regions.Length!=(module=="FactoryOffice53"?8:1)) throw new InvalidOperationException("Композиция: потеряны существующие окна/одна hole region, module="+module);
            var backdrop=room.GetComponentsInChildren<RoomArtworkAuthoring>(true).Single(a=>a.Read(room).texture=="Factory46/backwall");
            if(mask.Layers.Contains(backdrop.GetComponent<MeshRenderer>()) || mask.Layers.Any(l=>l.GetComponent<RoomArtworkAuthoring>().Read(room).order is not (-29 or -30 or -20)))
                throw new InvalidOperationException("Композиция: вырезается общий задник/передний слой, module="+module);
            if(mask.Layers.Length!=(module=="FactoryOffice53"?39:27))throw new InvalidOperationException("Композиция: нужен точный набор39/27 bindings, module="+module);
        }
        internal static void ValidateResources(Manifest data)
        {
            foreach(var mask in data.masks)
            {
                string path=mask.file.Substring("RemainsUnity/".Length);var texture=Require<Texture2D>(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                if(texture.width!=mask.pixels[0] || texture.height!=mask.pixels[1] || !texture.isReadable || importer.sRGBTexture || importer.alphaIsTransparency || importer.mipmapEnabled ||
                    importer.textureCompression!=TextureImporterCompression.Uncompressed || importer.npotScale!=TextureImporterNPOTScale.None || importer.wrapModeU!=TextureWrapMode.Clamp || importer.wrapModeV!=TextureWrapMode.Clamp)
                    throw new InvalidOperationException("Композиция: изменены mask native import settings, path="+path);
            }
            foreach(var p in data.placements)
            {
                string path=p.diffuseFile.Substring("RemainsUnity/".Length);var texture=Require<Texture2D>(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                if(texture.width!=(p.operation=="multiply"?796:1789) || texture.height!=(p.operation=="multiply"?480:1341) || !importer.sRGBTexture || importer.mipmapEnabled ||
                    importer.npotScale!=TextureImporterNPOTScale.None || importer.textureCompression!=TextureImporterCompression.Uncompressed || importer.wrapModeU!=TextureWrapMode.Clamp || importer.wrapModeV!=TextureWrapMode.Clamp)
                    throw new InvalidOperationException("Композиция: изменены diffuse canvas/sRGB/sampler, record="+p.recordId);
            }
        }
        private sealed class ArtworkGuard
        {
            private readonly RoomArtworkAuthoring artwork;private readonly string value,meshGuid,materialGuid;
            public ArtworkGuard(RoomArtworkAuthoring source,RoomAuthoring room)
            {artwork=source;value=StateJson.ToJson(source.Read(room));meshGuid=Guid(source.GetComponent<MeshFilter>().sharedMesh);materialGuid=Guid(source.GetComponent<MeshRenderer>().sharedMaterial);}
            public void Verify(RoomAuthoring room)
            {
                if(artwork==null || StateJson.ToJson(artwork.Read(room))!=value || Guid(artwork.GetComponent<MeshFilter>().sharedMesh)!=meshGuid || Guid(artwork.GetComponent<MeshRenderer>().sharedMaterial)!=materialGuid)
                    throw new InvalidOperationException("Композиция изменила существующий рисунок/его GUID; prefab не сохранён, layer="+artwork?.name);
            }
            private static string Guid(UnityEngine.Object asset)=>AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
        }
        internal static bool SameRect(RoomArtwork a,RoomArtwork b)=>Mathf.Abs(a.x-b.x)+Mathf.Abs(a.y-b.y)+Mathf.Abs(a.width-b.width)+Mathf.Abs(a.height-b.height)<.0001f;
        internal static string PrefabPath(string module)=>module=="FactoryOffice53"?FactoryOfficeDraft.Path:module=="FactoryWarehouse40"?FactoryWarehouseDraft.Path:throw new InvalidOperationException("Композиция: неизвестный module="+module);
        internal static string Assets(string module)=>module=="FactoryOffice53"?OfficeAssets:WarehouseAssets;
        private static string Repository()=>System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../.."));
        private static string Research(string file)=>System.IO.Path.Combine(Repository(),"Tools/Research",ResearchFolder,file);
        private static void RequireHash(string path,string expected)
        {
            using var hash=SHA256.Create();string actual=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
            if(string.IsNullOrWhiteSpace(expected) || actual!=expected)throw new InvalidOperationException("Композиция: source/resource hash изменён; файл не заменён, path="+path);
        }
        private static T Require<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException("Композиция: отсутствует asset="+path);
    }
}
