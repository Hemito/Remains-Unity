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
        private static void Bake(RoomWindowMaskAuthoring component)=>RoomArtworkVisibilityBake.Bake(component,Folder);
        internal static Color32[] Pixels(RoomWindowMaskAuthoring component,out int width,out int height)=>RoomArtworkVisibilityBake.Pixels(component,out width,out height);
        internal static void VerifyPixels(RoomWindowMaskAuthoring component)=>RoomArtworkVisibilityBake.VerifyPixels(component);
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
        private static string Research(string file)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Tools/Research/FactoryOffice53",file));
        private static T Require<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException("Маска окон: отсутствует ассет "+path);
    }
}
