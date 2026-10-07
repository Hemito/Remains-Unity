using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    // Explicit saved-UI migration; normal startup never edits assets or rebuilds labels.
    public static class WorldLibraryIdentityAuthoring
    {
        public static void RunAndRender() {Run();WorldLibraryIdentityChecks.Render();}
        public static void Run()
        {
            EditPicker();EditMenu();AssetDatabase.SaveAssets();
            WorldLibraryIdentityChecks.Run();
        }
        private static void EditPicker()
        {
            const string path="Assets/Remains/Resources/Presentation/WorldPicker.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<WorldPickerView>();view.Validate();
                if(view.Choices.Length!=5) throw new InvalidOperationException($"Подписи мира {path}: ожидаются пять сохранённых строк; изменение ручного макета отменено.");
                for(int i=0;i<view.Choices.Length;i++)
                    foreach(var button in new[]{view.Choices[i],view.Manage[i]})
                    {
                        var rect=button.GetComponent<RectTransform>();
                        if(!Mathf.Approximately(rect.anchoredPosition.y,-70-i*96) && !Mathf.Approximately(rect.anchoredPosition.y,-70-i*104))
                            throw new InvalidOperationException($"Подписи мира {path}: ручное положение строки {i}, объект={button.name}; проверьте макет перед миграцией.");
                        rect.anchoredPosition=new Vector2(rect.anchoredPosition.x,-70-i*104);
                        rect.sizeDelta=new Vector2(rect.sizeDelta.x,100);
                        var label=button.GetComponentInChildren<Text>();
                        label.rectTransform.sizeDelta=new Vector2(label.rectTransform.sizeDelta.x,94);
                        if(button==view.Choices[i]) Configure(label,16);
                    }
                var panel=view.Choices[0].transform.parent.GetComponent<RectTransform>();
                panel.sizeDelta=new Vector2(panel.sizeDelta.x,665);
                foreach(var rect in new[]{view.Previous.GetComponent<RectTransform>(),view.Next.GetComponent<RectTransform>(),view.Page.rectTransform,view.Close.GetComponent<RectTransform>()})
                    rect.anchoredPosition=new Vector2(rect.anchoredPosition.x,-610);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        private static void EditMenu()
        {
            const string path="Assets/Remains/Resources/Presentation/MainMenu.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<MainMenuView>();view.Validate();
                var rect=view.World.rectTransform;float extra=Mathf.Max(0,120-rect.sizeDelta.y);
                float previousBottom=rect.anchoredPosition.y-rect.sizeDelta.y;
                var panel=view.Controls.GetComponent<RectTransform>();
                if(extra>0)
                {
                    foreach(RectTransform child in rect.parent)
                        if(child!=rect && child.anchoredPosition.y<=previousBottom) child.anchoredPosition-=Vector2.up*extra;
                    rect.sizeDelta+=Vector2.up*extra;panel.sizeDelta+=Vector2.up*extra;
                    // Keep the existing panel's bottom margin instead of pushing actions off screen.
                    panel.anchoredPosition+=Vector2.up*extra;
                }
                if(panel.anchoredPosition.y>-15 || panel.sizeDelta.y-panel.anchoredPosition.y>705)
                    throw new InvalidOperationException($"Подписи мира {path}: ручной макет не оставляет полей для снимка, panel={panel.anchoredPosition}/{panel.sizeDelta}; сохранение отменено.");
                Configure(view.World,14);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        private static void Configure(Text label,int fontSize)
        {
            label.fontSize=fontSize;label.supportRichText=false;label.resizeTextForBestFit=false;
            label.horizontalOverflow=HorizontalWrapMode.Wrap;label.verticalOverflow=VerticalWrapMode.Truncate;
        }
    }
}
