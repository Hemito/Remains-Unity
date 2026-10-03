using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class WorldPickerLayoutAuthoring
    {
        public static void Run()
        {
            const string path="Assets/Remains/Resources/Presentation/WorldPicker.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<WorldPickerView>();view.Validate();
                var panel=view.Choices[0].transform.parent.GetComponent<RectTransform>();
                panel.sizeDelta=new Vector2(panel.sizeDelta.x,625);
                panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,.5f);panel.anchoredPosition=Vector2.zero;
                var background=panel.GetComponent<Image>();var color=background.color;color.a=1;background.color=color;
                for(int i=0;i<view.Choices.Length;i++)
                {
                    foreach(var button in new[]{view.Choices[i],view.Manage[i]})
                    {
                        var rect=button.GetComponent<RectTransform>();
                        rect.anchoredPosition=new Vector2(rect.anchoredPosition.x,-70-i*96);
                        rect.sizeDelta=new Vector2(rect.sizeDelta.x,90);
                        var label=button.GetComponentInChildren<Text>();
                        label.rectTransform.anchoredPosition=new Vector2(label.rectTransform.anchoredPosition.x,-3);
                        label.rectTransform.sizeDelta=new Vector2(label.rectTransform.sizeDelta.x,84);
                        label.alignment=button==view.Choices[i]?TextAnchor.MiddleLeft:TextAnchor.MiddleCenter;
                    }
                }
                foreach(var rect in new[]{view.Previous.GetComponent<RectTransform>(),view.Next.GetComponent<RectTransform>(),view.Page.rectTransform,view.Close.GetComponent<RectTransform>()})
                    rect.anchoredPosition=new Vector2(rect.anchoredPosition.x,-565);
                PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            WorldPickerRender.Render();
        }
    }
}
