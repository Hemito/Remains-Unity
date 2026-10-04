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
    public static class FactoryEntryDecoration
    {
        public static void Verify()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remains/RoomDrafts/FactoryEntry46.prefab"));
            try
            {
                var room=root.GetComponent<RoomAuthoring>();var layout=room.CreateLayout();
                if(layout.artwork.Length!=163 || layout.platforms.Length!=63 || layout.ladders.Length!=2)
                    throw new InvalidOperationException("Сохранённый префаб входа потерял содержимое.");
                var light=new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()};
                root.AddComponent<RoomLighting>().Initialize(layout,light);
                OriginalTrainingRoomAuthoring.Render(root,layout,"factory-entry-46-decor.png");
            }
            finally {root.GetComponent<RoomLighting>()?.Release();UnityEngine.Object.DestroyImmediate(root);}
            ContractChecks.Run();
            Debug.Log("FactoryEntry46: проверен сохранённый префаб и повторный рендер в обычной сцене.");
        }
        public static void Import()
        {
            const string path="Assets/Remains/RoomDrafts/FactoryEntry46.prefab";
            AssetDatabase.Refresh();
            foreach(string file in Directory.GetFiles("Assets/Remains/Resources/Original/Room/Factory46","decor-*.png"))
            {
                var texture=(TextureImporter)AssetImporter.GetAtPath(file);
                texture.maxTextureSize=8192;texture.npotScale=TextureImporterNPOTScale.None;
                texture.textureCompression=TextureImporterCompression.Uncompressed;texture.mipmapEnabled=false;
                texture.alphaIsTransparency=true;texture.wrapMode=TextureWrapMode.Clamp;texture.SaveAndReimport();
            }
            var layout=JsonUtility.FromJson<RoomLayout>(File.ReadAllText(Path.Combine(OriginalTrainingRoomSource.Research,"factory-46-decor.json")));
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                if(root.transform.Find("Оригинальное оформление")!=null) throw new InvalidOperationException("Оформление входа уже импортировано; авторские правки не заменены.");
                var light=new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()};
                var decor=RoomVisual.Build(layout,light);decor.name="Оригинальное оформление";
                int index=0;
                foreach(var art in decor.GetComponentsInChildren<RoomArtworkAuthoring>())
                    art.name=$"Декор {index}: {layout.artwork[index++].texture}";
                RoomArtworkAssets.Save(decor,"Assets/Remains/RoomDrafts","FactoryEntry46Decoration");
                decor.transform.SetParent(root.transform,false);
                var room=root.GetComponent<RoomAuthoring>();var actual=room.CreateLayout();
                if(actual.artwork.Length!=107+layout.artwork.Length || index!=layout.artwork.Length)
                    throw new InvalidOperationException("Оформление входа: потеряны исходные слои или новые объекты.");
                PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();
                Debug.LogWarning("FactoryEntry46: добавлены изображения декора, включая светильники. Световые маски, общий задник, сколы и игровые объекты ещё не восстановлены.",root);
                Debug.Log($"FactoryEntry46 decoration: PASS — {index} размещений, всего {actual.artwork.Length} слоёв.");
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            Verify();
        }
    }
}
