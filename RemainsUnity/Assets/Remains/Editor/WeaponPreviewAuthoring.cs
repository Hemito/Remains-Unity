using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class WeaponPreviewAuthoring
    {
        public static void Run()
        {
            const string path="Assets/Remains/Resources/Presentation/PersonalInventory.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<PersonalInventoryView>();
                if(view.WeaponPreview==null)
                {
                    view.WeaponPreview=Object.Instantiate(view.DetailIcon,view.DetailIcon.transform.parent,false);
                    view.WeaponPreview.name="Просмотр оружия";
                }
                if(view.WeaponDetail==null)
                {
                    view.WeaponDetail=Object.Instantiate(view.Detail,view.Detail.transform.parent,false);
                    view.WeaponDetail.name="Описание оружия";
                }
                Place(view.WeaponPreview.rectTransform,465,112,325,65);
                Place(view.WeaponDetail.rectTransform,465,182,325,110);
                view.WeaponPreview.preserveAspect=true;view.WeaponPreview.raycastTarget=false;
                view.WeaponDetail.raycastTarget=false;
                view.WeaponPreview.gameObject.SetActive(false);view.WeaponDetail.gameObject.SetActive(false);
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();WeaponInventoryUiChecks.Render();
        }
        private static void Place(RectTransform rect,float x,float y,float width,float height)
        {
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);
        }
    }
}
