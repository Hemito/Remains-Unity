using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class InventoryScrollAuthoring
    {
        public static void Run()
        {
            const string path="Assets/Remains/Resources/Presentation/PersonalInventory.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<PersonalInventoryView>();
                if(view.Scrollbar==null)
                {
                    var ui=new HudElements(view.Detail.font);
                    var first=view.Rows[0].GetComponent<RectTransform>();
                    var last=view.Rows[view.Rows.Length-1].GetComponent<RectTransform>();
                    float height=first.anchoredPosition.y-last.anchoredPosition.y+last.sizeDelta.y;
                    var track=ui.Panel(root.transform,"Прокрутка списка",new Vector2(444,first.anchoredPosition.y),new Vector2(14,height));
                    track.GetComponent<Image>().color=new Color(.08f,.2f,.12f,.85f);
                    var handle=new GameObject("Ползунок",typeof(RectTransform),typeof(Image));handle.transform.SetParent(track.transform,false);
                    var rect=handle.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
                    rect.offsetMin=new Vector2(2,0);rect.offsetMax=new Vector2(-2,0);
                    var image=handle.GetComponent<Image>();image.color=new Color(.4f,.85f,.5f,.9f);
                    var scrollbar=track.AddComponent<Scrollbar>();scrollbar.handleRect=rect;scrollbar.targetGraphic=image;
                    scrollbar.direction=Scrollbar.Direction.TopToBottom;scrollbar.navigation=new Navigation {mode=Navigation.Mode.None};
                    view.Scrollbar=scrollbar;
                }
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();WeaponInventoryUiChecks.BuildAndRender();
        }
    }
}
