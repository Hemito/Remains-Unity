using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class RoomAuthoringMigration
    {
        public static void Run()
        {
            const string folder="Assets/Remains/Resources/Levels";
            if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Remains/Resources","Levels");
            foreach(string key in new[]{"hub","factory"})
            {
                string source="Assets/Remains/Resources/Content/"+(key=="hub"?"prototype-hub":"prototype-room")+".json";
                string target=folder+"/"+key+".prefab";
                if(AssetDatabase.LoadMainAssetAtPath(target)!=null) throw new InvalidOperationException("Уровень уже перенесён: "+target);
                var json=File.ReadAllText(source);var room=JsonUtility.FromJson<RoomLayout>(json);
                var root=new GameObject(key=="hub"?"Хаб":"Завод");
                try
                {
                    var authoring=root.AddComponent<RoomAuthoring>();authoring.Configure(room);
                    int index=0;
                    foreach(var surface in room.platforms)
                    {var part=new GameObject("Поверхность "+index++);part.transform.SetParent(root.transform,false);part.AddComponent<RoomSurfaceAuthoring>().Configure(surface);}
                    index=0;
                    foreach(var ladder in room.ladders)
                    {var part=new GameObject("Лестница "+index++);part.transform.SetParent(root.transform,false);part.AddComponent<RoomLadderAuthoring>().Configure(ladder);}
                    var copy=authoring.CreateLayout();
                    for(int i=0;i<room.platforms.Length;i++)
                        if(StateJson.ToJson(room.platforms[i])!=StateJson.ToJson(copy.platforms[i])) throw new InvalidOperationException("Изменилась поверхность при переносе: "+i);
                    for(int i=0;i<room.ladders.Length;i++)
                        if(Mathf.Abs(room.ladders[i].top-copy.ladders[i].top)>.00001f || room.ladders[i].x!=copy.ladders[i].x || room.ladders[i].bottom!=copy.ladders[i].bottom)
                            throw new InvalidOperationException("Изменилась лестница при переносе: "+i);
                    PrefabUtility.SaveAsPrefabAsset(root,target);
                }
                finally {UnityEngine.Object.DestroyImmediate(root);}
                Directory.CreateDirectory("../.reference/migration");File.WriteAllText("../.reference/migration/"+key+"-before-authoring.json",json);
                if(!AssetDatabase.DeleteAsset(source)) throw new InvalidOperationException("Не удалось удалить прежний источник комнаты: "+source);
            }
            AssetDatabase.SaveAssets();ContractChecks.Run();Debug.Log("Геометрия двух уровней перенесена в редактируемые префабы.");
        }
    }
}
