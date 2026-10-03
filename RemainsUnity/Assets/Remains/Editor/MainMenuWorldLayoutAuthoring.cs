using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class MainMenuWorldLayoutAuthoring
    {
        public static void Run()
        {
            const string path="Assets/Remains/Resources/Presentation/MainMenu.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<MainMenuView>();view.Validate();
                var description=view.World.rectTransform;
                float extra=Mathf.Max(0,90-description.sizeDelta.y);
                float previousBottom=description.anchoredPosition.y-description.sizeDelta.y;
                foreach(RectTransform child in description.parent)
                    if(child!=description && child.anchoredPosition.y<=previousBottom)
                        child.anchoredPosition-=Vector2.up*extra;
                description.sizeDelta+=Vector2.up*extra;
                view.Controls.GetComponent<RectTransform>().sizeDelta+=Vector2.up*extra;
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();MainMenuPrefabChecks.Run();MainMenuRender.RenderLongName();MainMenuRender.Render();
        }
    }
}
