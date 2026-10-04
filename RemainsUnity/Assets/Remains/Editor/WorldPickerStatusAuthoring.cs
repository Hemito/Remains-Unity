using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class WorldPickerStatusAuthoring
    {
        public static void UpdateAndBuild()
        {
            const string path="Assets/Remains/Resources/Presentation/WorldPicker.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<WorldPickerView>();
                if(view.Warning==null)
                {
                    var warning=Object.Instantiate(view.Empty,view.Empty.transform.parent,false);
                    warning.name="Предупреждения списка миров";warning.fontSize=14;warning.alignment=TextAnchor.MiddleLeft;
                    warning.color=new Color(1,.78f,.3f);warning.raycastTarget=false;
                    var rect=warning.rectTransform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
                    rect.anchoredPosition=new Vector2(20,-46);rect.sizeDelta=new Vector2(900,22);
                    warning.text="Список собран с предупреждениями. Подробности — в журнале игры.";
                    warning.gameObject.SetActive(false);view.Warning=warning;
                }
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            WorldCatalogChecks.BuildAndBenchmark();
        }
    }
}
