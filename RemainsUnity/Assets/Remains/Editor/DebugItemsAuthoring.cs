using System;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    // The hidden item menu's prefab: a centred window with search, category tabs, a scrolled list and a row template.
    //   batch -executeMethod Remains.Editor.DebugItemsAuthoring.RunBatch
    public static class DebugItemsAuthoring
    {
        private const string Path="Assets/Remains/Resources/Presentation/DebugItems.prefab";
        private const float Width=760,Height=640,RowHeight=40;

        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            var ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var root=new GameObject("Меню выдачи предметов",typeof(RectTransform));
            try
            {
                var rootRect=(RectTransform)root.transform;
                rootRect.anchorMin=Vector2.zero;rootRect.anchorMax=Vector2.one;rootRect.offsetMin=rootRect.offsetMax=Vector2.zero;
                var view=root.AddComponent<DebugItemsView>();
                var panel=ui.Panel(root.transform,"Панель",Vector2.zero,new Vector2(Width,Height));
                var panelRect=(RectTransform)panel.transform;
                panelRect.anchorMin=panelRect.anchorMax=panelRect.pivot=new Vector2(.5f,.5f);panelRect.anchoredPosition=Vector2.zero;
                view.Panel=panel;
                var title=ui.Label(panel.transform,"Выдача предметов",new Vector2(16,-12),new Vector2(400,28));title.fontSize=20;
                view.Close=ui.Button(panel.transform,"Закрыть",new Vector2(Width-136,-10),new Vector2(120,34),()=>{});
                view.Search=SearchField(ui,panel.transform);
                string[] titles=new string[DebugItems.Categories.Length+1];titles[0]="Все";
                Array.Copy(DebugItems.Categories,0,titles,1,DebugItems.Categories.Length);
                view.Tabs=new Button[titles.Length];float tab=(Width-32)/titles.Length;
                for(int i=0;i<titles.Length;i++)
                {
                    view.Tabs[i]=ui.Button(panel.transform,titles[i],new Vector2(16+i*tab,-96),new Vector2(tab-4,34),()=>{});
                    view.Tabs[i].GetComponentInChildren<Text>().fontSize=14;
                }
                view.Content=List(panel.transform,out var viewport);
                view.Row=Row(ui,view.Content);
                view.Status=ui.Label(panel.transform,"",new Vector2(16,-Height+34),new Vector2(Width-32,26));view.Status.fontSize=14;
                view.Validate();
                PrefabUtility.SaveAsPrefabAsset(root,Path);AssetDatabase.SaveAssets();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            Debug.Log("Меню выдачи: префаб "+Path+".");
        }
        private static InputField SearchField(HudElements ui,Transform parent)
        {
            var box=ui.Panel(parent,"Поиск",new Vector2(16,-48),new Vector2(Width-32,40));
            box.GetComponent<Image>().color=new Color(.09f,.16f,.13f,1);
            var placeholder=ui.Label(box.transform,"Поиск по названию или ID…",new Vector2(10,-8),new Vector2(Width-52,26));
            placeholder.color=new Color(.65f,1,.75f,.45f);
            var text=ui.Label(box.transform,"",new Vector2(10,-8),new Vector2(Width-52,26));text.supportRichText=false;
            var input=box.AddComponent<InputField>();input.textComponent=text;input.placeholder=placeholder;
            return input;
        }
        private static RectTransform List(Transform parent,out RectTransform viewport)
        {
            var region=new GameObject("Список",typeof(RectTransform),typeof(ScrollRect));region.transform.SetParent(parent,false);
            HudElements.Rect((RectTransform)region.transform,new Vector2(16,-138),new Vector2(Width-32,Height-180));
            var area=new GameObject("Видимая область",typeof(RectTransform),typeof(Image),typeof(RectMask2D));area.transform.SetParent(region.transform,false);
            viewport=(RectTransform)area.transform;viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;
            viewport.offsetMin=Vector2.zero;viewport.offsetMax=new Vector2(-18,0);
            area.GetComponent<Image>().color=Color.clear;
            var content=new GameObject("Содержимое",typeof(RectTransform),typeof(VerticalLayoutGroup),typeof(ContentSizeFitter));content.transform.SetParent(area.transform,false);
            var rect=(RectTransform)content.transform;rect.anchorMin=new Vector2(0,1);rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,1);rect.offsetMin=rect.offsetMax=Vector2.zero;
            var layout=content.GetComponent<VerticalLayoutGroup>();layout.spacing=2;layout.childControlHeight=true;layout.childControlWidth=true;
            layout.childForceExpandHeight=false;layout.childForceExpandWidth=true;
            content.GetComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var track=new GameObject("Прокрутка",typeof(RectTransform),typeof(Image),typeof(Scrollbar));track.transform.SetParent(region.transform,false);
            var trackRect=(RectTransform)track.transform;trackRect.anchorMin=new Vector2(1,0);trackRect.anchorMax=Vector2.one;
            trackRect.pivot=new Vector2(1,.5f);trackRect.offsetMin=new Vector2(-14,0);trackRect.offsetMax=Vector2.zero;
            track.GetComponent<Image>().color=new Color(.08f,.2f,.12f,.85f);
            var handle=new GameObject("Ползунок",typeof(RectTransform),typeof(Image));handle.transform.SetParent(track.transform,false);
            var handleRect=(RectTransform)handle.transform;handleRect.anchorMin=Vector2.zero;handleRect.anchorMax=Vector2.one;
            handleRect.offsetMin=new Vector2(2,0);handleRect.offsetMax=new Vector2(-2,0);
            var image=handle.GetComponent<Image>();image.color=new Color(.4f,.85f,.5f,.9f);
            var bar=track.GetComponent<Scrollbar>();bar.handleRect=handleRect;bar.targetGraphic=image;bar.direction=Scrollbar.Direction.BottomToTop;
            bar.navigation=new Navigation {mode=Navigation.Mode.None};
            var scroll=region.GetComponent<ScrollRect>();scroll.content=rect;scroll.viewport=viewport;scroll.horizontal=false;scroll.vertical=true;
            scroll.movementType=ScrollRect.MovementType.Clamped;scroll.inertia=false;scroll.scrollSensitivity=40;scroll.verticalScrollbar=bar;
            scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;
            return rect;
        }
        private static DebugItemRow Row(HudElements ui,RectTransform content)
        {
            var button=ui.Button(content,"Предмет",Vector2.zero,new Vector2(Width-50,RowHeight),()=>{});
            button.gameObject.name="Строка";
            button.gameObject.AddComponent<LayoutElement>().preferredHeight=RowHeight;
            var label=button.GetComponentInChildren<Text>();label.alignment=TextAnchor.MiddleLeft;label.fontSize=16;
            var labelRect=label.rectTransform;labelRect.anchorMin=Vector2.zero;labelRect.anchorMax=Vector2.one;labelRect.offsetMin=new Vector2(52,2);labelRect.offsetMax=new Vector2(-6,-2);
            var icon=new GameObject("Иконка",typeof(RectTransform),typeof(Image));icon.transform.SetParent(button.transform,false);
            var iconRect=(RectTransform)icon.transform;iconRect.anchorMin=iconRect.anchorMax=new Vector2(0,.5f);iconRect.pivot=new Vector2(0,.5f);
            iconRect.anchoredPosition=new Vector2(8,0);iconRect.sizeDelta=new Vector2(36,36);
            var image=icon.GetComponent<Image>();image.preserveAspect=true;image.raycastTarget=false;
            var row=button.gameObject.AddComponent<DebugItemRow>();row.Button=button;row.Icon=image;row.Name=label;
            return row;
        }
    }
}
