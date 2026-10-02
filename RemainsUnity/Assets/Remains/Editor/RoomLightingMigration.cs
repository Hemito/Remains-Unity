using System;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class RoomLightingMigration
    {
        public static void BindHubFixtures()
        {
            const string path="Assets/Remains/Resources/Levels/hub.prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();
                foreach(var light in root.GetComponentsInChildren<RoomLightAuthoring>(true))
                {
                    var data=light.Read(room);if(string.IsNullOrEmpty(data.mask)) continue;
                    if(light.GetComponentInChildren<MeshRenderer>(true)!=null) continue;
                    string key=data.mask.Replace("Original/Room/Hub/light-mask-","Hub/decor-");
                    var matches=root.GetComponentsInChildren<RoomArtworkAuthoring>(true).Where(art=>
                    {var item=art.Read(room);return item.texture==key && Mathf.Abs(item.x+item.width/2-data.x)<.0001f && Mathf.Abs(item.y+item.height/2-data.y)<.0001f;}).ToArray();
                    if(matches.Length!=1) throw new InvalidOperationException($"Свет {data.id}: ожидалось одно исходное изображение {key}, найдено {matches.Length}.");
                    matches[0].transform.SetParent(light.transform,true);
                }
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            ContractChecks.Run();
        }
        public static void Run()
        {
            foreach(string key in new[]{"hub","factory"})
            {
                string path="Assets/Remains/Resources/Levels/"+key+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if(root.GetComponent<RoomLightingAuthoring>()!=null) throw new InvalidOperationException("Свет уже перенесён: "+key);
                    var room=root.GetComponent<RoomAuthoring>();var layout=room.CreateLayout();
                    var profile=JsonUtility.FromJson<RoomLightProfile>(Resources.Load<TextAsset>(layout.lightingProfile).text);
                    var lighting=root.AddComponent<RoomLightingAuthoring>();lighting.SetAmbient(profile.ambient);
                    var group=new GameObject("Источники света");group.transform.SetParent(root.transform,false);
                    foreach(var light in profile.lights)
                    {
                        var node=new GameObject("Свет: "+light.id);node.transform.SetParent(group.transform,false);
                        var mask=string.IsNullOrEmpty(light.mask)?null:Resources.Load<Texture2D>(light.mask);
                        if(!string.IsNullOrEmpty(light.mask) && mask==null) throw new InvalidOperationException("Нет маски: "+light.mask);
                        node.AddComponent<RoomLightAuthoring>().Configure(light,mask);
                        var fixture=root.GetComponentsInChildren<MeshRenderer>(true).SingleOrDefault(item=>item.name=="Светильник: "+light.id);
                        if(!string.IsNullOrEmpty(light.fixture) && fixture==null) throw new InvalidOperationException("Нет изображения светильника: "+light.id);
                        if(fixture!=null) fixture.transform.SetParent(node.transform,true);
                    }
                    var copy=lighting.CreateProfile(room);
                    if(copy.lights.Length!=profile.lights.Length) throw new InvalidOperationException("Потеряны источники света: "+key);
                    PrefabUtility.SaveAsPrefabAsset(root,path);Debug.Log($"Свет {key}: перенесено {copy.lights.Length} источников.");
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();ContractChecks.Run();
        }
    }
}
