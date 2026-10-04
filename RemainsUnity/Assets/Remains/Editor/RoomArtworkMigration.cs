using System;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class RoomArtworkMigration
    {
        public static void ClearLegacyCopies()
        {
            foreach(string key in new[]{"hub","factory"})
            {
                string path="Assets/Remains/Resources/Levels/"+key+".prefab";
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var room=root.GetComponent<RoomAuthoring>();
                    if(!room.HasAuthoredArtwork) throw new InvalidOperationException("Нет авторского оформления: "+key);
                    var serialized=new SerializedObject(room);
                    int count=serialized.FindProperty("settings").FindPropertyRelative("artwork").arraySize;
                    string before=Remains.Serialization.StateJson.ToJson(room.CreateLayout());
                    room.UseAuthoredArtwork();
                    if(before!=Remains.Serialization.StateJson.ToJson(room.CreateLayout()))
                        throw new InvalidOperationException("Удаление старой копии изменило комнату: "+key);
                    if(count>0) PrefabUtility.SaveAsPrefabAsset(root,path);
                    Debug.Log($"Авторская комната {key}: удалено устаревших записей {count}, актуальные данные совпадают.");
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();PrototypeBuild.Build();
        }
        public static void AlignFactoryLadder()
        {
            const string path="Assets/Remains/Resources/Levels/factory.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var authoring=root.GetComponent<RoomAuthoring>();
                var ladders=root.GetComponentsInChildren<RoomLadderAuthoring>();
                if(ladders.Length!=1) throw new InvalidOperationException("Ожидалась одна лестница завода.");
                var ladder=ladders[0];var data=ladder.Read(authoring);
                if(data.artworkOffset!=0) throw new InvalidOperationException("Смещение лестницы уже задано; повторная миграция отменена.");
                data.artworkOffset=.5f;ladder.Configure(data);
                foreach(Transform child in ladder.transform) child.localPosition+=Vector3.right*.5f;
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();ContractChecks.Run();
        }
        public static void FinalizeAssets()
        {
            foreach(string key in new[]{"hub","factory"})
            {
                string path="Assets/Remains/Resources/Levels/"+key+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
                var duplicates=new System.Collections.Generic.HashSet<string>();
                try
                {
                    var room=root.GetComponent<RoomAuthoring>().CreateLayout();var visual=root.transform.Find("Оформление");
                    foreach(var ladder in root.GetComponentsInChildren<RoomLadderAuthoring>(true))
                        if(ladder.GetComponentInChildren<MeshRenderer>(true)!=null) throw new InvalidOperationException("Графика лестниц уже привязана; повторная миграция отменена: "+key);
                    var shared=new System.Collections.Generic.Dictionary<(Texture,Shader,float),Material>();
                    var textures=new System.Collections.Generic.HashSet<string>();
                    foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var material=renderer.sharedMaterial;var signature=(material.mainTexture,material.shader,material.GetFloat("_Emission"));
                        if(shared.TryGetValue(signature,out var existing))
                        {renderer.sharedMaterial=existing;string duplicate=AssetDatabase.GetAssetPath(material);if(duplicate!=AssetDatabase.GetAssetPath(existing)) duplicates.Add(duplicate);}
                        else shared.Add(signature,material);
                        textures.Add(AssetDatabase.GetAssetPath(material.mainTexture));
                    }
                    foreach(string texture in textures)
                    {
                        var importer=(TextureImporter)AssetImporter.GetAtPath(texture);
                        if(importer.wrapMode!=TextureWrapMode.Repeat) {importer.wrapMode=TextureWrapMode.Repeat;importer.SaveAndReimport();}
                    }
                    var ladders=root.GetComponentsInChildren<RoomLadderAuthoring>(true);
                    int count=0;foreach(var ladder in room.ladders) {if(!string.IsNullOrEmpty(ladder.artwork)) count++;else {count+=2;for(float y=ladder.bottom;y<ladder.top;y+=.3f) count++;}}
                    // Generated ladders are the last group, in the same order as the room definitions.
                    var parts=new System.Collections.Generic.List<Transform>();
                    for(int i=visual.childCount-count;i<visual.childCount;i++) parts.Add(visual.GetChild(i));
                    int cursor=0;
                    for(int i=0;i<ladders.Length;i++)
                    {
                        int pieces=1;var ladder=room.ladders[i];
                        if(string.IsNullOrEmpty(ladder.artwork)) {pieces=2;for(float y=ladder.bottom;y<ladder.top;y+=.3f) pieces++;}
                        for(int j=0;j<pieces;j++) parts[cursor++].SetParent(ladders[i].transform,true);
                    }
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
                foreach(string duplicate in duplicates)
                {
                    if(!duplicate.StartsWith("Assets/Remains/Resources/Levels/"+key+"-artwork/",StringComparison.Ordinal) || !duplicate.EndsWith(".mat",StringComparison.Ordinal))
                        throw new InvalidOperationException("Нельзя удалить материал вне каталога миграции: "+duplicate);
                    AssetDatabase.DeleteAsset(duplicate);
                }
            }
            AssetDatabase.SaveAssets();ContractChecks.Run();
        }
        public static void Run()
        {
            const string resources="Assets/Remains/Resources/Levels";
            foreach(string key in new[]{"hub","factory"})
            {
                string path=resources+"/"+key+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
                GameObject generated=null;
                try
                {
                    var authoring=root.GetComponent<RoomAuthoring>();
                    if(authoring.HasAuthoredArtwork) throw new InvalidOperationException("Оформление уже перенесено: "+key);
                    var layout=authoring.CreateLayout();generated=RoomVisual.Build(layout);generated.name="Оформление";
                    RoomArtworkAssets.Save(generated,resources,key+"-artwork");
                    generated.transform.SetParent(root.transform,false);
                    authoring.UseAuthoredArtwork();PrefabUtility.SaveAsPrefabAsset(root,path);generated=null;
                    Debug.Log($"Оформление {key}: сохранены слои в префабе.");
                }
                finally {if(generated!=null) UnityEngine.Object.DestroyImmediate(generated);PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();ContractChecks.Run();
        }
    }
}
