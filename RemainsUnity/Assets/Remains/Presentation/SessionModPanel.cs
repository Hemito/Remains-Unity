using System.Collections.Generic;
using Remains.Content;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class SessionModPanel
    {
        private readonly Transform parent;
        private readonly HudElements ui;
        private readonly CoopSession session;
        private GameObject root;
        private int page;
        public IReadOnlyList<ContentDifference> Differences {get;private set;}=new List<ContentDifference>();
        public RectTransform ConfirmRect {get;private set;}
        public RectTransform CancelRect {get;private set;}
        public SessionModPanel(Transform parent,HudElements ui,CoopSession session)
        {
            this.parent=parent;this.ui=ui;this.session=session;
            session.ModMismatch+=host=>
            {
                Differences=host==null?new List<ContentDifference>():ContentComparison.Compare(WeaponCatalog.ManifestCopy(),host);
                page=0;Render();
            };
        }
        private void Render()
        {
            if(root!=null) {root.SetActive(false);Object.Destroy(root);}
            ConfirmRect=CancelRect=null;
            if(Differences.Count==0) return;
            root=ui.Panel(parent,"Моды сессии",new Vector2(90,-70),new Vector2(1100,570));
            (ConfirmRect,CancelRect)=ContentComparisonView.Draw(root.transform,Differences,
                Remains.Content.GameLocalization.UiText("source.SessionModPanel.6736b949661ffde6", "Моды или игровые данные отличаются. Продолжение может изменить игровой результат.", "RemainsUnity/Assets/Remains/Presentation/SessionModPanel.cs"),page,
                next=>{page=next;Render();},accept=>
                {
                    Differences=new List<ContentDifference>();Render();
                    if(accept) session.SendJoin(true);else session.StopClient();
                },Remains.Content.GameLocalization.UiText("source.SessionModPanel.07a67ba979c9831e", "У вас", "RemainsUnity/Assets/Remains/Presentation/SessionModPanel.cs"),Remains.Content.GameLocalization.UiText("source.SessionModPanel.4187792a4d9f66b2", "У хоста", "RemainsUnity/Assets/Remains/Presentation/SessionModPanel.cs"),Remains.Content.GameLocalization.UiText("source.SessionModPanel.6dedfbafb0cbeb18", "Войти с этим набором", "RemainsUnity/Assets/Remains/Presentation/SessionModPanel.cs"));
        }
    }
}
