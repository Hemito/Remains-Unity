using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class MainMenuStartupAuthoring
    {
        public static void Run()
        {
            const string path="Assets/Remains/Resources/Presentation/MainMenu.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<MainMenuView>();
                if(view==null || view.Controls==null || view.Host==null)
                    throw new InvalidOperationException($"Меню {path}: нет исходной кнопки запуска или панели подключения.");
                if(view.CompareCopies==null)
                {
                    // One-time authored insertion, never a runtime UI fallback.
                    foreach(RectTransform child in view.Controls.transform)
                        if(child.anchoredPosition.y<=-346) child.anchoredPosition-=Vector2.up*50;
                    view.Controls.GetComponent<RectTransform>().sizeDelta+=Vector2.up*50;
                    view.CompareCopies=UnityEngine.Object.Instantiate(view.Host,view.Host.transform.parent,false);
                    view.CompareCopies.name="Сравнение копий мира";
                    view.CompareCopies.onClick.RemoveAllListeners();
                }
                var rect=view.CompareCopies.GetComponent<RectTransform>();
                var source=view.Host.GetComponent<RectTransform>();
                rect.anchorMin=source.anchorMin;rect.anchorMax=source.anchorMax;rect.pivot=source.pivot;
                rect.anchoredPosition=source.anchoredPosition+Vector2.down*62;
                rect.sizeDelta=new Vector2(source.sizeDelta.x,44);
                var label=view.CompareCopies.GetComponentInChildren<Text>(true);
                label.text="Сравнить копии для совместной игры";label.fontSize=18;
                label.resizeTextForBestFit=false;
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();MainMenuPrefabChecks.Run();MainMenuRender.Render();MainMenuRender.RenderLongName();
        }
        public static void RunAndBuild() {Run();InteractionOutlineChecks.RenderMapAndPony();PrototypeBuild.Build();}
    }
}
