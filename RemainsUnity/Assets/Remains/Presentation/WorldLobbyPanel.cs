using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class WorldLobbyPanel
    {
        private readonly Transform parent;
        private readonly HudElements ui;
        private readonly CoopSession session;
        private GameObject root;
        private int page;
        private readonly Dictionary<string,RectTransform> choices=new();
        public RectTransform StartButtonRect {get;private set;}
        public RectTransform ContentConfirmRect {get;private set;}
        public RectTransform ContentCancelRect {get;private set;}
        private int contentPage;
        public RectTransform ChoiceRect(string key)=>choices[key];
        public WorldLobbyPanel(Transform parent,HudElements ui,CoopSession session,Action refresh)
        {
            this.parent=parent;this.ui=ui;this.session=session;
            session.Lobby.Changed+=()=>{Render();refresh();};
        }
        private void Render()
        {
            if(root!=null) {root.SetActive(false);UnityEngine.Object.Destroy(root);}
            choices.Clear();StartButtonRect=null;ContentConfirmRect=ContentCancelRect=null;
            var lobby=session.Lobby;
            if(!lobby.ClientWaiting) return;
            root=ui.Panel(parent,"Выбор мира участниками",new Vector2(90,-70),new Vector2(1100,570));
            if(NetworkServer.active && lobby.ContentDifferences.Count>0) {RenderContent();return;}
            string instruction=NetworkServer.active?"Выберите целую копию и начните игру.":"Хост выбирает целую копию. Дождитесь начала игры.";
            ui.Label(root.transform,$"Участников: {lobby.ClientPlayers}. {instruction}\nДругие ветви сохранятся; объединения прогресса нет.",new Vector2(20,-10),new Vector2(1050,60));
            var selected=lobby.Copies.Find(c=>c.key==lobby.ClientSelected);
            int pages=Math.Max(1,(lobby.Copies.Count+4)/5);page=Math.Clamp(page,0,pages-1);
            for(int index=0;index<5 && page*5+index<lobby.Copies.Count;index++)
            {
                var copy=lobby.Copies[page*5+index];
                var relation=selected==null?"Другой сохранённый мир":WorldHistory.Description(copy.summary.CompareTo(selected.summary));
                var button=ui.Button(root.transform,$"{copy.holder} · мир {copy.summary.worldId.Substring(0,8)} · ревизия {copy.summary.revision}\n{relation}",
                    new Vector2(20,-80-index*65),new Vector2(1060,60),()=>session.Lobby.Select(copy.key));
                button.interactable=NetworkServer.active && !lobby.ClientBusy;
                if(copy.key==lobby.ClientSelected) button.GetComponent<Image>().color=new Color(.2f,.28f,.1f);
                choices[copy.key]=button.GetComponent<RectTransform>();
            }
            ui.Button(root.transform,"Назад",new Vector2(20,-410),new Vector2(150,35),()=>{page--;Render();}).interactable=page>0;
            ui.Label(root.transform,$"{page+1}/{pages}",new Vector2(190,-410),new Vector2(100,35));
            ui.Button(root.transform,"Далее",new Vector2(300,-410),new Vector2(150,35),()=>{page++;Render();}).interactable=page+1<pages;
            ui.Button(root.transform,"Новый мир",new Vector2(480,-410),new Vector2(250,35),()=>session.Lobby.Select("")).interactable=NetworkServer.active && !lobby.ClientBusy;
            ui.Label(root.transform,lobby.ClientBusy?"Получение и проверка выбранного сохранения…":lobby.ClientError??"",new Vector2(20,-451),new Vector2(1060,50));
            var start=ui.Button(root.transform,lobby.ClientSelected==""?"Начать в новом мире":"Начать с выбранной копией",new Vector2(20,-510),new Vector2(690,45),()=>session.Lobby.StartSelected());
            start.interactable=NetworkServer.active && !lobby.ClientBusy && (lobby.ClientSelected=="" || selected!=null);
            StartButtonRect=start.GetComponent<RectTransform>();
            ui.Button(root.transform,"Отключиться",new Vector2(760,-510),new Vector2(320,45),()=>{if(NetworkServer.active) session.StopHost();else session.StopClient();});
        }
        private void RenderContent()
        {
            (ContentConfirmRect,ContentCancelRect)=ContentComparisonView.Draw(root.transform,ui,session.Lobby.ContentDifferences,
                "Контент сохранения отличается. Продолжение может изменить игровой результат.",contentPage,
                next=>{contentPage=next;Render();},accept=>session.Lobby.ConfirmContent(accept));
        }
    }
}
