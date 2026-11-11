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
    public sealed class WorldLobbyPanel : IDisposable
    {
        private readonly Transform parent;
        private readonly CoopSession session;
        private readonly Action refresh;
        private bool disposed;
        private GameObject root;
        private int page;
        private readonly Dictionary<string,RectTransform> choices=new();
        public RectTransform StartButtonRect {get;private set;}
        public RectTransform ContentConfirmRect {get;private set;}
        public RectTransform ContentCancelRect {get;private set;}
        private int contentPage;
        public bool CopyPickerVisible {get;private set;}
        public RectTransform ChoiceRect(string key)=>choices[key];
        public bool HasChoice(string key)=>choices.ContainsKey(key);
        public WorldLobbyPanel(Transform parent,CoopSession session,Action refresh)
        {
            this.parent=parent;this.session=session;this.refresh=refresh;
            session.Lobby.Changed+=OnChanged;
        }
        private void OnChanged() {if(disposed) return;Render();refresh();}
        public void Dispose()
        {
            if(disposed) return;disposed=true;
            session.Lobby.Changed-=OnChanged;
        }
        private void Render()
        {
            if(root!=null) {root.SetActive(false);UnityEngine.Object.Destroy(root);}
            choices.Clear();StartButtonRect=null;ContentConfirmRect=ContentCancelRect=null;CopyPickerVisible=false;
            var lobby=session.Lobby;
            if(!lobby.ClientWaiting) return;
            bool content=lobby.ClientContentPending && NetworkServer.active && lobby.ContentDifferences.Count>0;
            var view=WorldLobbyView.Create(parent,lobby.ClientChoosingCopies && !content);root=view.gameObject;
            if(content) {RenderContent();return;}
            if(!lobby.ClientChoosingCopies)
            {
                view.ShowWaiting(lobby.ClientContentPending?Remains.Content.GameLocalization.UiText("source.WorldLobbyPanel.570a95f92d9814e2", "Хост сравнивает содержимое выбранного мира. Дождитесь его решения.", "RemainsUnity/Assets/Remains/Presentation/WorldLobbyPanel.cs"):
                    Remains.Content.GameLocalization.UiText("source.WorldLobbyPanel.f59fe2b172b20679", "Подготовка выбранного мира. Другие копии не заменяют выбранное продолжение.", "RemainsUnity/Assets/Remains/Presentation/WorldLobbyPanel.cs"),lobby.ClientError);
                view.Disconnect.onClick.AddListener(()=>{if(NetworkServer.active) session.StopHost();else session.StopClient();});
                return;
            }
            CopyPickerVisible=true;
            string instruction=NetworkServer.active?Remains.Content.GameLocalization.UiText("source.WorldLobbyPanel.6ceb2be8dc4705a5", "Выберите целую копию и начните игру.", "RemainsUnity/Assets/Remains/Presentation/WorldLobbyPanel.cs"):Remains.Content.GameLocalization.UiText("source.WorldLobbyPanel.af8c2a40648c3b9b", "Хост выбирает целую копию. Дождитесь начала игры.", "RemainsUnity/Assets/Remains/Presentation/WorldLobbyPanel.cs");
            view.Instruction.text=Remains.Content.GameLocalization.Format("source.WorldLobbyPanel.1f455b6899b0d4a2", $"Участников: {lobby.ClientPlayers}. {instruction}\nДругие ветви сохранятся; объединения прогресса нет.", "RemainsUnity/Assets/Remains/Presentation/WorldLobbyPanel.cs");
            var selected=lobby.Copies.Find(c=>c.key==lobby.ClientSelected);
            var branches=lobby.Copies.Select(copy=>copy.summary.branchId).ToArray();
            var snapshots=lobby.Copies.Select(copy=>copy.summary.snapshot).ToArray();
            int capacity=view.Choices.Length;
            int pages=Math.Max(1,(lobby.Copies.Count+capacity-1)/capacity);page=Math.Clamp(page,0,pages-1);
            for(int index=0;index<capacity;index++)
            {
                bool visible=page*capacity+index<lobby.Copies.Count;view.Choices[index].gameObject.SetActive(visible);
                if(!visible) continue;
                var copy=lobby.Copies[page*capacity+index];
                var relation=selected==null?Remains.Content.GameLocalization.UiText("source.WorldLobbyPanel.e7a450515357d011", "Выберите копию для сравнения", "RemainsUnity/Assets/Remains/Presentation/WorldLobbyPanel.cs"):
                    WorldLobbyView.RelationText(copy.summary.CompareTo(selected.summary),copy.key==lobby.ClientSelected);
                var button=view.Choices[index];button.onClick.AddListener(()=>session.Lobby.Select(copy.key));
                view.ShowChoice(index,new WorldLibrary(session.Saves).Name(copy.summary.worldId),copy.holder,copy.summary.revision,
                    WorldLobbyView.IdentityLabel(copy.summary.branchId,branches),WorldLobbyView.IdentityLabel(copy.summary.snapshot,snapshots),relation,
                    copy.key==lobby.ClientSelected,NetworkServer.active && !lobby.ClientBusy);
                choices[copy.key]=button.GetComponent<RectTransform>();
            }
            view.Previous.onClick.AddListener(()=>{page--;Render();});view.Previous.interactable=page>0;
            view.Page.text=$"{page+1}/{pages}";
            view.Next.onClick.AddListener(()=>{page++;Render();});view.Next.interactable=page+1<pages;
            view.NewWorld.onClick.AddListener(()=>session.Lobby.Select(""));view.NewWorld.interactable=NetworkServer.active && !lobby.ClientBusy;
            OriginalButtonStyle.SetSelected(view.NewWorld,lobby.ClientSelected=="");
            view.Status.text=lobby.ClientBusy?Remains.Content.GameLocalization.UiText("source.WorldLobbyPanel.65488965b341548d", "Получение и проверка выбранного сохранения…", "RemainsUnity/Assets/Remains/Presentation/WorldLobbyPanel.cs"):lobby.ClientError??"";
            var start=view.StartGame;start.GetComponentInChildren<Text>().text=lobby.ClientSelected==""?Remains.Content.GameLocalization.UiText("source.WorldLobbyPanel.7e33e928010bd887", "Начать в новом мире", "RemainsUnity/Assets/Remains/Presentation/WorldLobbyPanel.cs"):Remains.Content.GameLocalization.UiText("source.WorldLobbyPanel.bd8f79f748bce132", "Начать с выбранной копией", "RemainsUnity/Assets/Remains/Presentation/WorldLobbyPanel.cs");
            start.onClick.AddListener(()=>session.Lobby.StartSelected());
            start.interactable=NetworkServer.active && !lobby.ClientBusy && (lobby.ClientSelected=="" || selected!=null);
            StartButtonRect=start.GetComponent<RectTransform>();
            view.Disconnect.onClick.AddListener(()=>{if(NetworkServer.active) session.StopHost();else session.StopClient();});
        }
        private void RenderContent()
        {
            var selected=session.Lobby.Copies.Find(copy=>copy.key==session.Lobby.ClientSelected);
            string id=session.Lobby.Startup.ChoosesCopies?selected?.summary.worldId:session.Lobby.Startup.Selected?.WorldId;
            string name=id==null?Remains.Content.GameLocalization.UiText("source.WorldLobbyPanel.748d7d19c40fceff", "Выбранный мир", "RemainsUnity/Assets/Remains/Presentation/WorldLobbyPanel.cs"):new WorldLibrary(session.Saves).Name(id);
            (ContentConfirmRect,ContentCancelRect)=ContentComparisonView.Draw(root.transform,session.Lobby.ContentDifferences,
                Remains.Content.GameLocalization.Format("source.WorldLobbyPanel.5b330688ff357304", $"{name}: версия игры или набор модов изменились.", "RemainsUnity/Assets/Remains/Presentation/WorldLobbyPanel.cs"),contentPage,
                next=>{contentPage=next;Render();},accept=>session.Lobby.ConfirmContent(accept));
        }
    }
}
