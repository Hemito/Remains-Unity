using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEngine;

namespace Remains.Presentation
{
    public static class ContentComparisonView
    {
        public static (RectTransform confirm,RectTransform cancel) Draw(Transform root,HudElements ui,
            IReadOnlyList<ContentDifference> rows,string title,int page,Action<int> turnPage,Action<bool> decide)
        {
            int pages=Math.Max(1,(rows.Count+3)/4);page=Math.Clamp(page,0,pages-1);
            ui.Label(root,title+"\nИсходная копия сохранится; моды не устанавливаются и не переставляются автоматически.",new Vector2(20,-10),new Vector2(1060,60));
            ui.Label(root,"Пакет",new Vector2(20,-75),new Vector2(200,30));
            ui.Label(root,"В сохранении",new Vector2(230,-75),new Vector2(410,30));
            ui.Label(root,"Сейчас",new Vector2(650,-75),new Vector2(420,30));
            for(int i=0;i<4 && page*4+i<rows.Count;i++)
            {
                var row=rows[page*4+i];float y=-115-i*75;
                ui.Label(root,row.id,new Vector2(20,y),new Vector2(200,70));
                ui.Label(root,row.saved,new Vector2(230,y),new Vector2(410,70)).fontSize=14;
                ui.Label(root,row.current,new Vector2(650,y),new Vector2(420,70)).fontSize=14;
            }
            ui.Button(root,"Назад",new Vector2(20,-430),new Vector2(180,40),()=>turnPage(page-1)).interactable=page>0;
            ui.Label(root,$"{page+1}/{pages}",new Vector2(230,-430),new Vector2(150,35));
            ui.Button(root,"Далее",new Vector2(400,-430),new Vector2(180,40),()=>turnPage(page+1)).interactable=page+1<pages;
            var confirm=ui.Button(root,"Попробовать загрузить с этим набором",new Vector2(20,-510),new Vector2(690,45),()=>decide(true)).GetComponent<RectTransform>();
            var cancel=ui.Button(root,"Отмена",new Vector2(760,-510),new Vector2(320,45),()=>decide(false)).GetComponent<RectTransform>();
            return (confirm,cancel);
        }
    }
}
