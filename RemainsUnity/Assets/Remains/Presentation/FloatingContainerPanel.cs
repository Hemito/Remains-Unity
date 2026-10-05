using System;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    // Local window state. Ownership and pending grants remain in the shared protocol.
    public sealed class FloatingContainerPanel
    {
        private readonly ContainerWindowView view;
        private readonly CoopSession session;
        private readonly InventoryArtwork artwork;
        private readonly Camera camera;
        private readonly Action changed;
        private readonly UnityEngine.UI.Image[] cellImages=new UnityEngine.UI.Image[9];
        private readonly Color[] cellColors=new Color[9];
        private string sourceId;
        private int row;
        private bool obscured;
        private readonly System.Collections.Generic.HashSet<string> missingIcons=new();
        public bool IsOpen=>sourceId!=null;
        public InventorySource Source {get;private set;}
        public int FirstItem=>row*3;
        public int RowCount=>Math.Max(3,((Source?.items.Count??0)+2)/3);
        public RectTransform SlotRect(int index)=>view.Slots[index].GetComponent<RectTransform>();
        public RectTransform WindowRect=>view.Rect;
        public bool PointerBlocked(Vector2 position)=>view.ContainsPointer(position);
        public FloatingContainerPanel(ContainerWindowView view,CoopSession session,InventoryArtwork artwork,Camera camera,Action changed)
        {
            this.view=view;this.session=session;this.artwork=artwork;this.camera=camera;this.changed=changed;
            view.Validate();view.gameObject.SetActive(false);
            for(int i=0;i<9;i++)
            {
                view.Intents[i].Initialize(view.Slots[i],Claim);
                cellImages[i]=view.Slots[i].GetComponent<UnityEngine.UI.Image>();cellColors[i]=cellImages[i].color;
            }
            view.ScrollRows+=delta=>{row+=delta;Refresh();};
            view.Scrollbar.onValueChanged.AddListener(value=>{row=Mathf.RoundToInt(value*(RowCount-3));Refresh();});
        }
        public bool Open(string id)
        {
            var source=InventorySource.Resolve(session.World,id);
            if(source?.IsLooseItem==true) {session.ShowNotice("Свободная вещь подбирается по E целиком.");return false;}
            if(!ContainerAccess.Accessible(session.LocalActor,source,session.Rules.Loot,session.Rules.Deposit))
            {session.ShowNotice("Контейнер больше недоступен.");return false;}
            sourceId=id;row=0;Refresh();changed?.Invoke();return true;
        }
        public void Close()
        {
            if(!IsOpen) return;
            sourceId=null;Source=null;view.gameObject.SetActive(false);changed?.Invoke();
        }
        public void Refresh()
        {
            if(!IsOpen) return;
            Source=InventorySource.Resolve(session.World,sourceId);
            if(session.Travel.Pending || Source?.IsLooseItem==true || !ContainerAccess.Accessible(session.LocalActor,Source,session.Rules.Loot,session.Rules.Deposit))
            {Close();return;}
            row=Mathf.Clamp(row,0,RowCount-3);
            view.Scrollbar.gameObject.SetActive(RowCount>3);view.Scrollbar.size=3f/RowCount;
            view.Scrollbar.SetValueWithoutNotify(RowCount>3?row/(float)(RowCount-3):0);
            for(int i=0;i<9;i++)
            {
                int index=FirstItem+i;var item=index<Source.items.Count?Source.items[index]:null;
                artwork.Set(view.Icons[i],item?.definition);
                view.Intents[i].Bind(item?.id);
                bool missing=item!=null && !view.Icons[i].enabled;
                cellImages[i].color=missing?Color.magenta:cellColors[i];
                if(missing && missingIcons.Add(item.definition))
                    Debug.LogWarning($"Контейнер {sourceId}: нет иконки, item={item.id}, definition={item.definition}. Ячейка выделена розовым как ошибка данных.",view);
                view.Slots[i].interactable=item!=null && !session.Pause.IsPaused(session.LocalActor.level) &&
                    ContainerAccess.ClaimRejection(session.LocalActor,Source,item,session.Rules.Loot)==null;
            }
            view.Pending.gameObject.SetActive(session.World.pendingGrants.Exists(g=>g.containerId==sourceId && g.recipient==session.LocalActor.id));
            view.gameObject.SetActive(!obscured);
        }
        private void Claim(string item)
        {
            if(!IsOpen || item==null) return;
            session.ShowNotice("Подбор подтверждается…");session.RequestClaim(sourceId,item,0);
        }
        public void Step(bool cover)
        {
            if(!IsOpen) return;
            var current=InventorySource.Resolve(session.World,sourceId);
            if(session.Travel.Pending || current?.IsLooseItem==true || !ContainerAccess.Accessible(session.LocalActor,current,session.Rules.Loot,session.Rules.Deposit))
            {Close();return;}
            Source=current;obscured=cover;view.gameObject.SetActive(!cover);
            if(!cover)
            {
                float height=current.Body==null?session.World.containers.Find(c=>c.id==sourceId).height:Anatomy.Movement(current.Body).height;
                view.Follow(camera,current.Position+Vector2.up*height);
            }
        }
    }
}
