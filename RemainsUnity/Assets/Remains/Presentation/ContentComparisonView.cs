using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public static class ContentComparisonView
    {
        public static (RectTransform confirm,RectTransform cancel) Draw(Transform root,
            IReadOnlyList<ContentDifference> rows,string title,int page,Action<int> turnPage,Action<bool> decide,string leftTitle=null,string rightTitle=null,string confirmTitle=null)
        {
            leftTitle??=GameLocalization.UiText("ui.content.saved", "В сохранении", "ContentComparisonView");
            rightTitle??=GameLocalization.UiText("ui.content.current", "Сейчас", "ContentComparisonView");
            confirmTitle??=GameLocalization.UiText("ui.content.tryLoad", "Попробовать загрузить с этим набором", "ContentComparisonView");
            var prefab=GameAssets.Load<ContentComparisonLayout>("Presentation/ContentComparison");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/ContentComparison.");
            prefab.Validate();var view=UnityEngine.Object.Instantiate(prefab,root,false);
            int capacity=view.Ids.Length;int pages=Math.Max(1,(rows.Count+capacity-1)/capacity);page=Math.Clamp(page,0,pages-1);
            view.Title.text=title+Remains.Content.GameLocalization.UiText("source.ContentComparisonView.43a27e501cc5d161", "\nИсходная копия сохранится; моды не устанавливаются и не переставляются автоматически.", "RemainsUnity/Assets/Remains/Presentation/ContentComparisonView.cs");
            view.LeftTitle.text=leftTitle;view.RightTitle.text=rightTitle;
            for(int i=0;i<capacity;i++)
            {
                int index=page*capacity+i;bool visible=index<rows.Count;
                view.Ids[i].gameObject.SetActive(visible);view.Left[i].gameObject.SetActive(visible);view.Right[i].gameObject.SetActive(visible);
                if(!visible) continue;
                var row=rows[index];view.Ids[i].text=row.id;view.Left[i].text=row.saved;view.Right[i].text=row.current;
            }
            view.Previous.onClick.AddListener(()=>turnPage(page-1));view.Previous.interactable=page>0;
            view.Page.text=$"{page+1}/{pages}";
            view.Next.onClick.AddListener(()=>turnPage(page+1));view.Next.interactable=page+1<pages;
            view.Confirm.GetComponentInChildren<Text>().text=confirmTitle;
            view.Confirm.onClick.AddListener(()=>decide(true));view.Cancel.onClick.AddListener(()=>decide(false));
            return (view.Confirm.GetComponent<RectTransform>(),view.Cancel.GetComponent<RectTransform>());
        }
    }
}
