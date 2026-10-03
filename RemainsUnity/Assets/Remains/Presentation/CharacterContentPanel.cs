using Mirror;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class CharacterContentPanel
    {
        private readonly Transform parent;
        private readonly HudElements ui;
        private readonly CoopSession session;
        private GameObject root;
        private int page;
        public RectTransform ConfirmRect {get;private set;}
        public RectTransform CancelRect {get;private set;}
        public CharacterContentPanel(Transform parent,HudElements ui,CoopSession session)
        {
            this.parent=parent;this.ui=ui;this.session=session;
            session.CharacterContent.Changed+=()=>{page=0;Render();};
        }
        private void Render()
        {
            if(root!=null) {root.SetActive(false);Object.Destroy(root);}
            ConfirmRect=CancelRect=null;
            var rows=session.CharacterContent.Differences;
            if(rows.Count==0) return;
            root=ui.Panel(parent,"Контент персонажа",new Vector2(90,-70),new Vector2(1100,570));
            (ConfirmRect,CancelRect)=ContentComparisonView.Draw(root.transform,rows,
                $"Персонаж «{session.CharacterId}»: версия игры или набор модов изменились.",page,
                next=>{page=next;Render();},accept=>
                {
                    if(accept) session.SendJoin(false,true);
                    else {session.CharacterContent.Reset();if(NetworkServer.active) session.StopHost();else session.StopClient();}
                });
        }
    }
}
