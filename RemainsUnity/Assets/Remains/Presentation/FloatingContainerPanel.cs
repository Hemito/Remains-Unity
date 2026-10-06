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
        private readonly string[] cellDefinitions=new string[9],cellItems=new string[9];
        private readonly bool[] cellInitialized=new bool[9];
        public int CellBindingCount {get;private set;}
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
        public void ConfigureLayout(RectTransform[] reserved,Func<Rect?> worldActorBounds=null)=>view.ConfigureLayout(reserved,worldActorBounds);
        public void ConfigureArtworkLayout(RectTransform[] reserved,Func<Rect?> actor,Func<Rect?> weapon,Func<Rect?> heldItem,Func<string> context)=>
            view.ConfigureArtworkLayout(reserved,actor,weapon,heldItem,context);
        public bool Open(string id)
        {
            var source=InventorySource.Resolve(session.World,id);
            if(source?.IsLooseItem==true) {session.ShowNotice("Свободная вещь подбирается по E целиком.");return false;}
            if(!ContainerAccess.Accessible(session.LocalActor,source,session.Rules.Loot,session.Rules.Deposit))
            {session.ShowNotice("Контейнер больше недоступен.");return false;}
            view.BeginSource(id);sourceId=id;row=0;Refresh();
            if(source.Body==null) session.ContainerInspection.Request(id);
            changed?.Invoke();return true;
        }
        public void Close()
        {
            if(!IsOpen) return;
            sourceId=null;Source=null;view.EndSource();view.gameObject.SetActive(false);changed?.Invoke();
        }
        public void Refresh()
        {
            if(!IsOpen) return;
            var actor=session.LocalActor;
            Source=InventorySource.Resolve(session.World,sourceId);
            if(session.Travel.Pending || Source?.IsLooseItem==true || !ContainerAccess.Accessible(actor,Source,session.Rules.Loot,session.Rules.Deposit))
            {Close();return;}
            int rows=RowCount;row=Mathf.Clamp(row,0,rows-3);
            if(view.Scrollbar.gameObject.activeSelf!=(rows>3)) view.Scrollbar.gameObject.SetActive(rows>3);
            float size=3f/rows;if(view.Scrollbar.size!=size) view.Scrollbar.size=size;
            view.Scrollbar.SetValueWithoutNotify(rows>3?row/(float)(rows-3):0);
            for(int i=0;i<9;i++)
            {
                int index=FirstItem+i;var item=index<Source.items.Count?Source.items[index]:null;
                if(!cellInitialized[i] || cellDefinitions[i]!=item?.definition)
                {artwork.Set(view.Icons[i],item?.definition);cellDefinitions[i]=item?.definition;CellBindingCount++;}
                if(!cellInitialized[i] || cellItems[i]!=item?.id)
                {view.Intents[i].Bind(item?.id);cellItems[i]=item?.id;}
                cellInitialized[i]=true;
                bool missing=item!=null && !view.Icons[i].enabled;
                cellImages[i].color=missing?Color.magenta:cellColors[i];
                if(missing && missingIcons.Add(item.definition))
                    Debug.LogWarning($"Контейнер {sourceId}: нет иконки, item={item.id}, definition={item.definition}. Ячейка выделена розовым как ошибка данных.",view);
                view.Slots[i].interactable=item!=null && !session.Pause.IsPaused(actor.level) &&
                    ContainerAccess.ClaimRejection(actor,Source,item,session.Rules.Loot)==null;
            }
            bool pending=false;string actorId=actor.id;
            foreach(var grant in session.World.pendingGrants) if(grant.containerId==sourceId && grant.recipient==actorId) {pending=true;break;}
            if(view.Pending.gameObject.activeSelf!=pending) view.Pending.gameObject.SetActive(pending);
            bool visible=!obscured && view.PlacementAvailable;if(view.gameObject.activeSelf!=visible) view.gameObject.SetActive(visible);
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
            Source=current;obscured=cover;
            if(cover) view.gameObject.SetActive(false);
            else
            {
                view.Follow(camera,current.Bounds);
            }
        }
    }
}
