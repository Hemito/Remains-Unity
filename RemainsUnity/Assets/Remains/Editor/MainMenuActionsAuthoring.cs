using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class MainMenuActionsAuthoring
    {
        public static void Run()
        {
            const string path="Assets/Remains/Resources/Presentation/MainMenu.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<MainMenuView>();view.Validate();
                var picker=view.Worlds.GetComponent<RectTransform>();var newGame=view.NewGame.GetComponent<RectTransform>();
                float width=view.Host.GetComponent<RectTransform>().sizeDelta.x;
                newGame.SetParent(picker.parent,false);
                newGame.anchorMin=picker.anchorMin;newGame.anchorMax=picker.anchorMax;newGame.pivot=picker.pivot;
                picker.sizeDelta=new Vector2(width*.63f,picker.sizeDelta.y);
                newGame.anchoredPosition=picker.anchoredPosition+Vector2.right*(width*.63f+10);
                newGame.sizeDelta=new Vector2(width*.37f-10,picker.sizeDelta.y);
                StretchGraphics(view.Worlds);StretchGraphics(view.NewGame);
                view.Worlds.GetComponentInChildren<Text>().text="Миры и сохранения";
                view.NewGame.GetComponentInChildren<Text>().text="Новая игра";
                view.Host.GetComponentInChildren<Text>().text="Продолжить выбранный мир";
                view.World.text="Выбран: Мир 7f65d82c\n«Миры и сохранения»: выбор, имя и удаление.";
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();MainMenuPrefabChecks.Run();MainMenuRender.Render();MainMenuRender.RenderLongName();
        }
        private static void StretchGraphics(Button button)
        {
            foreach(var graphic in button.GetComponentsInChildren<Graphic>(true))
            {
                if(graphic.transform==button.transform) continue;
                var rect=graphic.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
                rect.offsetMin=rect.offsetMax=Vector2.zero;
            }
        }
    }
}
