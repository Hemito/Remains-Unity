using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class ContainerWindowAuthoring
    {
        private static void Style(ContainerWindowView view)
        {
            view.GetComponent<Image>().color=new Color(.04f,.08f,.07f,1);
            var sprite=Resources.Load<InventoryWindowView>("Presentation/InventoryWindow").Quantity.GetComponent<Image>().sprite;
            if(sprite==null || !AssetDatabase.Contains(sprite)) throw new InvalidOperationException("Нет сохранённой графики кнопки контейнера.");
            view.Close.GetComponent<Image>().sprite=sprite;view.Quantity.GetComponent<Image>().sprite=sprite;
        }
        public static void FinalizeLayout()
        {
            const string path="Assets/Remains/Resources/Presentation/ContainerWindow.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try {var view=root.GetComponent<ContainerWindowView>();Style(view);view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);}
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();PrototypeBuild.Build();
        }
        public static void Run()
        {
            const string path="Assets/Remains/Resources/Presentation/ContainerWindow.prefab";
            if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)
                throw new InvalidOperationException("Окно контейнера уже существует; авторские изменения не заменены.");
            var ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var root=ui.Panel(null,"Контейнер",Vector2.zero,new Vector2(340,440));
            try
            {
                var rect=(RectTransform)root.transform;
                rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(440,0);
                var view=root.AddComponent<ContainerWindowView>();
                view.Contents=((GameObject)PrefabUtility.InstantiatePrefab(Resources.Load<SharedInventoryView>("Presentation/SharedInventory").gameObject,root.transform)).GetComponent<SharedInventoryView>();
                HudElements.Rect((RectTransform)view.Contents.transform,new Vector2(10,-10),new Vector2(320,340));
                HudElements.Rect(view.Contents.Title.rectTransform,Vector2.zero,new Vector2(320,40));view.Contents.Title.fontSize=16;
                for(int i=0;i<9;i++)
                    HudElements.Rect((RectTransform)view.Contents.Slots[i].transform,new Vector2(i%3*108,-48-i/3*84),new Vector2(104,80));
                HudElements.Rect((RectTransform)view.Contents.Previous.transform,new Vector2(0,-304),new Vector2(150,32));
                HudElements.Rect((RectTransform)view.Contents.Next.transform,new Vector2(170,-304),new Vector2(150,32));
                view.Quantity=ui.Button(root.transform,"Количество: вся стопка",new Vector2(10,-352),new Vector2(320,34),()=>{});
                view.Close=ui.Button(root.transform,"Закрыть контейнер · E",new Vector2(10,-394),new Vector2(320,34),()=>{});
                Style(view);view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            AssetDatabase.SaveAssets();PrototypeBuild.Build();
        }
    }
}
