using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Persistence;
using Remains.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class WorldLobbyPanel
    {
        private readonly Transform parent;
        private readonly CoopSession session;
        private GameObject root;
        private int page;
        private readonly Dictionary<string,RectTransform> choices=new();
        public RectTransform StartButtonRect {get;private set;}
        public RectTransform ContentConfirmRect {get;private set;}
        public RectTransform ContentCancelRect {get;private set;}
        private int contentPage;
        public RectTransform ChoiceRect(string key)=>choices[key];
        public WorldLobbyPanel(Transform parent,CoopSession session,Action refresh)
        {
            this.parent=parent;this.session=session;
            session.Lobby.Changed+=()=>{Render();refresh();};
        }
        private void Render()
        {
            if(root!=null) {root.SetActive(false);UnityEngine.Object.Destroy(root);}
            choices.Clear();StartButtonRect=null;ContentConfirmRect=ContentCancelRect=null;
            var lobby=session.Lobby;
            if(!lobby.ClientWaiting) return;
            var view=WorldLobbyView.Create(parent);root=view.gameObject;
            if(NetworkServer.active && lobby.ContentDifferences.Count>0) {view.Contents.SetActive(false);RenderContent();return;}
            string instruction=NetworkServer.active?"Выберите целую копию и начните игру.":"Хост выбирает целую копию. Дождитесь начала игры.";
            view.Instruction.text=$"Участников: {lobby.ClientPlayers}. {instruction}\nДругие ветви сохранятся; объединения прогресса нет.";
            var selected=lobby.Copies.Find(c=>c.key==lobby.ClientSelected);
            int capacity=view.Choices.Length;
            int pages=Math.Max(1,(lobby.Copies.Count+capacity-1)/capacity);page=Math.Clamp(page,0,pages-1);
            for(int index=0;index<capacity;index++)
            {
                bool visible=page*capacity+index<lobby.Copies.Count;view.Choices[index].gameObject.SetActive(visible);
                if(!visible) continue;
                var copy=lobby.Copies[page*capacity+index];
                var relation=selected==null?"Другой сохранённый мир":WorldHistory.Description(copy.summary.CompareTo(selected.summary));
                var button=view.Choices[index];button.onClick.AddListener(()=>session.Lobby.Select(copy.key));
                view.ShowChoice(index,new WorldLibrary(session.Saves).Name(copy.summary.worldId),copy.holder,copy.summary.revision,relation,
                    copy.key==lobby.ClientSelected,NetworkServer.active && !lobby.ClientBusy);
                choices[copy.key]=button.GetComponent<RectTransform>();
            }
            view.Previous.onClick.AddListener(()=>{page--;Render();});view.Previous.interactable=page>0;
            view.Page.text=$"{page+1}/{pages}";
            view.Next.onClick.AddListener(()=>{page++;Render();});view.Next.interactable=page+1<pages;
            view.NewWorld.onClick.AddListener(()=>session.Lobby.Select(""));view.NewWorld.interactable=NetworkServer.active && !lobby.ClientBusy;
            OriginalButtonStyle.SetSelected(view.NewWorld,lobby.ClientSelected=="");
            view.Status.text=lobby.ClientBusy?"Получение и проверка выбранного сохранения…":lobby.ClientError??"";
            var start=view.StartGame;start.GetComponentInChildren<Text>().text=lobby.ClientSelected==""?"Начать в новом мире":"Начать с выбранной копией";
            start.onClick.AddListener(()=>session.Lobby.StartSelected());
            start.interactable=NetworkServer.active && !lobby.ClientBusy && (lobby.ClientSelected=="" || selected!=null);
            StartButtonRect=start.GetComponent<RectTransform>();
            view.Disconnect.onClick.AddListener(()=>{if(NetworkServer.active) session.StopHost();else session.StopClient();});
        }
        private void RenderContent()
        {
            var selected=session.Lobby.Copies.Find(copy=>copy.key==session.Lobby.ClientSelected);
            string name=selected==null?"Выбранный мир":new WorldLibrary(session.Saves).Name(selected.summary.worldId);
            (ContentConfirmRect,ContentCancelRect)=ContentComparisonView.Draw(root.transform,session.Lobby.ContentDifferences,
                $"{name}: версия игры или набор модов изменились.",contentPage,
                next=>{contentPage=next;Render();},accept=>session.Lobby.ConfirmContent(accept));
        }
    }
}
