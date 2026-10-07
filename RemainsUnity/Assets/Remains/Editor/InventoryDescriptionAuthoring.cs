using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    // Explicit, idempotent prefab migration. Keep the existing Text object/file ID.
    public static class InventoryDescriptionAuthoring
    {
        public const string Path="Assets/Remains/Resources/Presentation/PersonalInventory.prefab";
        public static void Run()
        {
            var root=PrefabUtility.LoadPrefabContents(Path);
            try
            {
                var view=root.GetComponent<PersonalInventoryView>();
                if(view==null || view.Detail==null) throw new InvalidOperationException("Описание инвентаря: нет исходной личной страницы/текста.");
                if(view.Description==null) Configure(view);
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,Path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
        }
        private static void Configure(PersonalInventoryView view)
        {
            var original=view.Detail.rectTransform;
            if(original.parent!=view.transform || original.anchorMin!=new Vector2(0,1) || original.anchorMax!=new Vector2(0,1) ||
                original.pivot!=new Vector2(0,1) || original.anchoredPosition!=new Vector2(465,-112) || original.sizeDelta!=new Vector2(228,180))
                throw new InvalidOperationException($"Описание инвентаря: исходная геометрия изменена вручную; не перезаписываем. pos={original.anchoredPosition}, size={original.sizeDelta}.");
            if(view.transform.Find("Полное описание")!=null) throw new InvalidOperationException("Описание инвентаря: обнаружена область без явной ссылки; требуется исправить авторинг.");
            var region=new GameObject("Полное описание",typeof(RectTransform),typeof(ScrollRect),typeof(InventoryDescriptionView));
            region.transform.SetParent(view.transform,false);region.transform.SetSiblingIndex(original.GetSiblingIndex());
            var area=region.GetComponent<InventoryDescriptionView>();
            HudElements.Rect(area.Rect,original.anchoredPosition,original.sizeDelta);
            var viewport=new GameObject("Видимая область",typeof(RectTransform),typeof(Image),typeof(RectMask2D));
            viewport.transform.SetParent(region.transform,false);
            area.Viewport=(RectTransform)viewport.transform;area.Viewport.anchorMin=Vector2.zero;area.Viewport.anchorMax=Vector2.one;
            area.Viewport.offsetMin=Vector2.zero;area.Viewport.offsetMax=new Vector2(-18,0);
            viewport.GetComponent<Image>().color=Color.clear;viewport.GetComponent<Image>().raycastTarget=true;
            original.SetParent(viewport.transform,false);original.name="Полный текст предмета";
            HudElements.Rect(original,Vector2.zero,new Vector2(210,180));
            view.Detail.supportRichText=false;view.Detail.resizeTextForBestFit=false;
            view.Detail.horizontalOverflow=HorizontalWrapMode.Wrap;view.Detail.verticalOverflow=VerticalWrapMode.Overflow;view.Detail.raycastTarget=true;
            var track=new GameObject("Прокрутка описания",typeof(RectTransform),typeof(Image),typeof(Scrollbar));track.transform.SetParent(region.transform,false);
            var trackRect=(RectTransform)track.transform;trackRect.anchorMin=new Vector2(1,0);trackRect.anchorMax=Vector2.one;
            trackRect.pivot=new Vector2(1,.5f);trackRect.offsetMin=new Vector2(-14,0);trackRect.offsetMax=Vector2.zero;
            track.GetComponent<Image>().color=new Color(.08f,.2f,.12f,.85f);
            var handle=new GameObject("Ползунок",typeof(RectTransform),typeof(Image));handle.transform.SetParent(track.transform,false);
            var handleRect=(RectTransform)handle.transform;handleRect.anchorMin=Vector2.zero;handleRect.anchorMax=Vector2.one;
            handleRect.offsetMin=new Vector2(2,0);handleRect.offsetMax=new Vector2(-2,0);
            var image=handle.GetComponent<Image>();image.color=new Color(.4f,.85f,.5f,.9f);
            area.Scrollbar=track.GetComponent<Scrollbar>();area.Scrollbar.handleRect=handleRect;area.Scrollbar.targetGraphic=image;
            area.Scrollbar.direction=Scrollbar.Direction.BottomToTop;area.Scrollbar.navigation=new Navigation {mode=Navigation.Mode.None};
            area.Scroll=region.GetComponent<ScrollRect>();area.Scroll.content=original;area.Scroll.viewport=area.Viewport;
            area.Scroll.horizontal=false;area.Scroll.vertical=true;area.Scroll.movementType=ScrollRect.MovementType.Clamped;
            area.Scroll.inertia=false;area.Scroll.scrollSensitivity=22;area.Scroll.verticalScrollbar=area.Scrollbar;
            area.Scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;
            area.Content=view.Detail;view.Description=area;area.Validate();
        }
    }
}
