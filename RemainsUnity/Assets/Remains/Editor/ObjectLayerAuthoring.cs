using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ObjectLayerAuthoring
    {
        public static void UpdatePrefabsAndBuild()
        {
            int changed=0;
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Remains"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var prop=prefab.GetComponent<PropView>();var door=prefab.GetComponent<DoorView>();
                if(prop==null && door==null) continue;
                var contents=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var renderer=prop!=null?contents.GetComponent<PropView>().Artwork:contents.GetComponent<DoorView>().Artwork;
                    if(renderer==null) throw new InvalidOperationException("У префаба объекта нет изображения: "+path);
                    renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;
                    PrefabUtility.SaveAsPrefabAsset(contents,path);changed++;
                }
                finally {PrefabUtility.UnloadPrefabContents(contents);}
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Сохранены задние слои дверей/предметов: {changed} префабов, order={ActorVisualLayers.RestingObjectOrder}.");
            PrototypeBuild.Build();
        }
    }
}
