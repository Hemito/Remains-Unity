using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryEntryDraft
    {
        public static void Import()
        {
            const string folder="Assets/Remains/RoomDrafts";
            const string path=folder+"/FactoryEntry46.prefab";
            if(AssetDatabase.LoadMainAssetAtPath(path)!=null) throw new InvalidOperationException("Черновик входа уже существует; авторские правки не заменены.");
            AssetDatabase.Refresh();
            foreach(string file in Directory.GetFiles("Assets/Remains/Resources/Original/Room/Factory46"))
            {
                if(AssetImporter.GetAtPath(file) is not TextureImporter texture) continue;
                texture.textureCompression=TextureImporterCompression.Uncompressed;texture.mipmapEnabled=false;
                texture.wrapMode=TextureWrapMode.Repeat;texture.SaveAndReimport();
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var layout=JsonUtility.FromJson<RoomLayout>(File.ReadAllText(Path.Combine(OriginalTrainingRoomSource.Research,"factory-46-geometry-final.json")));
            var root=new GameObject("Завод — вход2, только геометрия (не игровой уровень)");
            var light=new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()};
            try
            {
                var authoring=RoomDraftGeometry.Create(root,layout);
                var visual=RoomVisual.Build(layout,light);visual.name="Основные текстуры — без сколов и декора";
                RoomArtworkAssets.Save(visual,folder,"FactoryEntry46Artwork");visual.transform.SetParent(root.transform,false);
                authoring.UseAuthoredArtwork();
                var actual=authoring.CreateLayout();
                if(actual.platforms.Length!=63 || actual.ladders.Length!=2 || actual.artwork.Length!=107)
                    throw new InvalidOperationException("При импорте входа потеряны поверхности, лестницы или слои.");
                PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();
                root.AddComponent<RoomLighting>().Initialize(layout,light);
                OriginalTrainingRoomAuthoring.Render(root,layout,"factory-entry-46-geometry.png");
                Debug.LogWarning("FactoryEntry46: черновик геометрии. Не импортированы декор, маски разрушений, наследуемый задник, двери, противники и контейнеры. В игровой каталог не включён.",root);
                ContractChecks.Run();
                Debug.Log("FactoryEntry46: PASS — 63 поверхности, 2 лестницы, 107 слоёв, сохранённый префаб и рендер черновика.");
            }
            finally {root.GetComponent<RoomLighting>()?.Release();UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
