using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Independent editor bake of a visibility atlas; source graphics are content, not rendering code.
    public static class FactoryOfficeWindowMasks
    {
        public const string Folder="Assets/Remains/RoomDrafts/FactoryOffice53WindowMasks";
        private const string MaskPath=Folder+"/visibility.png";
        private const int PixelsPerUnit=50;
        public static void InstallAndBuild()
        {
            Install();FactoryOfficeWindowMaskChecks.Run();FactoryOfficeWindowMaskChecks.RenderScreenshots();PrototypeBuild.Build();
        }
        public static void RebuildAndBuild()
        {
            Rebuild();FactoryOfficeWindowMaskChecks.Run();FactoryOfficeWindowMaskChecks.RenderScreenshots();PrototypeBuild.Build();
        }
        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();ValidateResources();
            var root=PrefabUtility.LoadPrefabContents(FactoryOfficeDraft.Path);
            try
            {
                var current=root.GetComponent<RoomWindowMaskAuthoring>();
                if(current!=null) {VerifyPixels(current);current.Validate();return;}
                var room=root.GetComponent<RoomAuthoring>();var layoutBefore=StateJson.ToJson(room.CreateLayout());
                var manifest=JObject.Parse(File.ReadAllText(Research("manifest.json")));
                var decor=JObject.Parse(File.ReadAllText(Research("decor.json")));
                var artworks=root.GetComponentsInChildren<RoomArtworkAuthoring>();
                var regions=manifest["decorPlacements"].Where(p=>p["sourceAttributes"]["id"].Value<string>() is "bwindow" or "swindow").Select(p=>
                {
                    var a=decor["artwork"][p["artworkIndex"].Value<int>()];string key=a["texture"].Value<string>();
                    var item=artworks.Single(item=>{var r=item.Read(room);return r.texture==key && Mathf.Abs(r.x-a["x"].Value<float>())<.0001f && Mathf.Abs(r.y-a["y"].Value<float>())<.0001f;});
                    string kind=p["sourceAttributes"]["id"].Value<string>();
                    return new WindowMaskRegion {sourceRecord=p["recordId"].Value<string>(),artwork=item,
                        coverage=Require<Texture2D>("Assets/Remains/Resources/Original/Room/Factory53/window-mask-"+kind+"-1.png")};
                }).ToArray();
                if(regions.Length!=7 || regions.Select(r=>r.sourceRecord).Distinct().Count()!=7)
                    throw new InvalidOperationException("Офис: нужны семь отдельных source окон, маска не установлена.");
                var windows=regions.Select(r=>r.artwork).ToArray();
                var targets=artworks.Where(a=>a.Read(room).order==-29 || windows.Contains(a)).Select(a=>a.GetComponent<MeshRenderer>()).ToArray();
                if(targets.Length!=38) throw new InvalidOperationException("Офис: нужны31 слой задней геометрии и7 рисунков окон; layout изменён, ничего не заменено.");
                if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Remains/RoomDrafts","FactoryOffice53WindowMasks");
                current=root.AddComponent<RoomWindowMaskAuthoring>();current.Configure(null,new Rect(0,0,38.4f,20),regions,targets);
                Bake(current);current.Validate();
                if(StateJson.ToJson(room.CreateLayout())!=layoutBefore) throw new InvalidOperationException("Маска окон изменила авторскую геометрию/расстановку; prefab не сохранён.");
                PrefabUtility.SaveAsPrefabAsset(root,FactoryOfficeDraft.Path);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            Debug.Log("Офис: сохранена маска1920×1000 из семи исходных alpha-масок,38 mesh/material bindings. Фон Unity остаётся согласованным общим задником, сравнение с оригинальным runtime ещё не выполнено.");
        }
        [MenuItem("Remains/Завод/Пересобрать маски окон офиса")]
        public static void Rebuild()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();ValidateResources();
            var root=PrefabUtility.LoadPrefabContents(FactoryOfficeDraft.Path);
            try
            {
                var mask=root.GetComponent<RoomWindowMaskAuthoring>()??throw new InvalidOperationException("Офис: маски ещё не установлены.");
                Bake(mask);mask.Validate();PrefabUtility.SaveAsPrefabAsset(root,FactoryOfficeDraft.Path);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        private static void Bake(RoomWindowMaskAuthoring component)
        {
            var room=component.GetComponent<RoomAuthoring>();
            foreach(var region in component.Regions)
            {
                var a=region.artwork.Read(room);region.bakedBounds=new Rect(a.x,a.y,a.width,a.height);
            }
            var pixels=Pixels(component,out int width,out int height);
            var generated=new Texture2D(width,height,TextureFormat.RGBA32,false,true);
            try {generated.SetPixels32(pixels);generated.Apply();File.WriteAllBytes(MaskPath,generated.EncodeToPNG());}
            finally {UnityEngine.Object.DestroyImmediate(generated);}
            AssetDatabase.ImportAsset(MaskPath,ImportAssetOptions.ForceUpdate);
            var importer=(TextureImporter)AssetImporter.GetAtPath(MaskPath);importer.textureType=TextureImporterType.Default;
            importer.isReadable=true;importer.sRGBTexture=false;importer.mipmapEnabled=false;importer.alphaIsTransparency=false;
            importer.npotScale=TextureImporterNPOTScale.None;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.maxTextureSize=4096;importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
            var texture=Require<Texture2D>(MaskPath);var shader=Require<Shader>("Assets/Remains/Resources/Presentation/WorldLit.shader");
            var bounds=component.Bounds;
            for(int index=0;index<component.Layers.Length;index++)
            {
                var renderer=component.Layers[index];var filter=renderer.GetComponent<MeshFilter>();
                var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);var vertices=mesh.vertices;var uv=new Vector2[vertices.Length];
                var matrix=room.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                for(int i=0;i<vertices.Length;i++) {var p=matrix.MultiplyPoint3x4(vertices[i]);uv[i]=new Vector2((p.x-bounds.x)/bounds.width,(p.y-bounds.y)/bounds.height);}
                mesh.uv2=uv;var material=new Material(renderer.sharedMaterial);material.shader=shader;
                material.SetTexture("_ArtworkVisibility",texture);material.SetFloat("_ArtworkVisibilityEnabled",1);
                filter.sharedMesh=Save(mesh,Folder+"/layer-"+index+".asset");renderer.sharedMaterial=Save(material,Folder+"/layer-"+index+".mat");
            }
            component.Configure(texture,bounds,component.Regions,component.Layers);EditorUtility.SetDirty(component);
        }
        internal static Color32[] Pixels(RoomWindowMaskAuthoring component,out int width,out int height)
        {
            var area=component.Bounds;width=Mathf.RoundToInt(area.width*PixelsPerUnit);height=Mathf.RoundToInt(area.height*PixelsPerUnit);
            if(width<=0 || height<=0 || width>4096 || height>4096) throw new InvalidOperationException("Маска окон: неверные или слишком большие границы.");
            var pixels=Enumerable.Repeat(new Color32(255,255,255,255),checked(width*height)).ToArray();
            foreach(var region in component.Regions)
            {
                if(region.coverage==null || !region.coverage.isReadable) throw new InvalidOperationException("Маска окна отсутствует/нечитаема, record="+region.sourceRecord);
                var r=region.bakedBounds;
                if(r.width<=0 || r.height<=0 || r.xMin<area.xMin || r.yMin<area.yMin || r.xMax>area.xMax || r.yMax>area.yMax)
                    throw new InvalidOperationException("Окно выходит за границы atlas, record="+region.sourceRecord);
                int left=Mathf.FloorToInt((r.xMin-area.xMin)*PixelsPerUnit),right=Mathf.CeilToInt((r.xMax-area.xMin)*PixelsPerUnit);
                int bottom=Mathf.FloorToInt((r.yMin-area.yMin)*PixelsPerUnit),top=Mathf.CeilToInt((r.yMax-area.yMin)*PixelsPerUnit);
                var source=region.coverage.GetPixels32();int sourceWidth=region.coverage.width,sourceHeight=region.coverage.height;
                for(int y=bottom;y<top;y++) for(int x=left;x<right;x++)
                {
                    float u=(area.x+(x+.5f)/PixelsPerUnit-r.x)/r.width,v=(area.y+(y+.5f)/PixelsPerUnit-r.y)/r.height;
                    if(u<0 || u>1 || v<0 || v>1) continue;
                    // Explicit pixel-centre registration. Texture2D.GetPixelBilinear's CPU
                    // coordinates do not match GPU normalized texel centres at native scale.
                    float coverage=SampleCoverage(source,sourceWidth,sourceHeight,u,v);
                    byte value=(byte)Mathf.RoundToInt(pixels[y*width+x].r*(1-coverage));pixels[y*width+x]=new Color32(value,value,value,255);
                }
            }
            return pixels;
        }
        private static float SampleCoverage(Color32[] pixels,int width,int height,float u,float v)
        {
            float x=Mathf.Clamp(u*width-.5f,0,width-1),y=Mathf.Clamp(v*height-.5f,0,height-1);
            int x0=Mathf.FloorToInt(x),y0=Mathf.FloorToInt(y),x1=Mathf.Min(x0+1,width-1),y1=Mathf.Min(y0+1,height-1);
            float lower=Mathf.Lerp(pixels[y0*width+x0].r,pixels[y0*width+x1].r,x-x0);
            float upper=Mathf.Lerp(pixels[y1*width+x0].r,pixels[y1*width+x1].r,x-x0);
            return Mathf.Lerp(lower,upper,y-y0)/255;
        }
        internal static void VerifyPixels(RoomWindowMaskAuthoring component)
        {
            var expected=Pixels(component,out int width,out int height);var actual=component.Visibility;
            if(actual==null || !actual.isReadable || actual.width!=width || actual.height!=height || !actual.GetPixels32().SequenceEqual(expected))
                throw new InvalidOperationException("Офис: сохранённый visibility atlas не соответствует маскам/окнам; выполните явную пересборку.");
        }
        internal static void ValidateResources()
        {
            var source=JObject.Parse(File.ReadAllText(Research("window-masks.json")));
            if(source["schema"].Value<int>()!=1 || source["masks"].Count()!=2) throw new InvalidOperationException("Офис: неверный resource manifest масок окон.");
            foreach(var item in source["masks"])
            {
                string path=item["file"].Value<string>().Substring("RemainsUnity/".Length);var texture=Require<Texture2D>(path);
                using var hash=System.Security.Cryptography.SHA256.Create();
                string expected=item["sha256"].Value<string>(),actual=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                if(expected!=actual || texture.width!=item["pixels"][0].Value<int>() || texture.height!=item["pixels"][1].Value<int>() ||
                    !texture.isReadable || importer.sRGBTexture || importer.alphaIsTransparency || importer.mipmapEnabled || importer.textureCompression!=TextureImporterCompression.Uncompressed)
                    throw new InvalidOperationException("Маска окна: изменены source PNG или importer, path="+path);
            }
        }
        private static T Save<T>(T value,string path) where T:UnityEngine.Object
        {
            var old=AssetDatabase.LoadAssetAtPath<T>(path);
            if(old==null) {AssetDatabase.CreateAsset(value,path);return value;}
            EditorUtility.CopySerialized(value,old);EditorUtility.SetDirty(old);UnityEngine.Object.DestroyImmediate(value);return old;
        }
        private static string Research(string file)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Tools/Research/FactoryOffice53",file));
        private static T Require<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException("Маска окон: отсутствует ассет "+path);
    }
}
