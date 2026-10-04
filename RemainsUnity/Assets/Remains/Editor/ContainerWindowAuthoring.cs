using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class ContainerWindowAuthoring
    {
        public static void LayoutSlots(ContainerWindowView view)
        {
            var names=new Text[9];var counts=new Text[9];
            var ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            for(int i=0;i<9;i++)
            {
                var slot=view.Contents.Slots[i];
                names[i]=view.Names?.Length==9?view.Names[i]:slot.GetComponentInChildren<Text>();
                counts[i]=view.Counts?.Length==9?view.Counts[i]:ui.Label(slot.transform,"",Vector2.zero,Vector2.one);
                names[i].name="Название предмета";counts[i].name="Количество в стопке";
                HudElements.Rect(names[i].rectTransform,new Vector2(4,-36),new Vector2(96,38));
                names[i].fontSize=12;names[i].resizeTextForBestFit=true;names[i].resizeTextMinSize=10;names[i].resizeTextMaxSize=12;
                names[i].alignment=TextAnchor.MiddleCenter;names[i].horizontalOverflow=HorizontalWrapMode.Wrap;
                HudElements.Rect(counts[i].rectTransform,new Vector2(40,-5),new Vector2(60,24));
                counts[i].fontSize=14;counts[i].alignment=TextAnchor.MiddleRight;counts[i].raycastTarget=false;
                HudElements.Rect(view.Contents.Icons[i].rectTransform,new Vector2(6,-4),new Vector2(30,28));
            }
            view.Names=names;view.Counts=counts;
        }
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
            try {var view=root.GetComponent<ContainerWindowView>();LayoutSlots(view);Style(view);view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);}
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
                LayoutSlots(view);Style(view);view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            AssetDatabase.SaveAssets();PrototypeBuild.Build();
        }
    }
}
