using System;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class PersonalInventoryPanel
    {
        private readonly CoopSession session;
        private readonly Func<string,string> itemName;
        private readonly Func<int> quantity;
        private readonly Button[] rows;
        private readonly string[] weapons;
        private readonly Button[] weaponButtons;
        private readonly Image[] icons;
        private readonly Image detailIcon;
        private readonly InventoryArtwork artwork;
        private readonly InventoryPortrait portrait;
        private readonly string[] ids;
        private readonly Button previous,next,deposit,use;
        private readonly Text title,detail,actionStatus;
        private InventorySource container;
        private string selected;
        private int page;
        public RectTransform RowRect(int row)=>rows[row].GetComponent<RectTransform>();
        public RectTransform DepositRect=>deposit.GetComponent<RectTransform>();
        public RectTransform UseRect=>use.GetComponent<RectTransform>();
        public RectTransform WeaponRect(int index)=>weaponButtons[index].GetComponent<RectTransform>();
        public PersonalInventoryPanel(Transform parent,CoopSession session,Func<string,string> itemName,Func<int> quantity,Action<string> equip,InventoryArtwork artwork)
        {
            this.session=session;this.itemName=itemName;this.quantity=quantity;this.artwork=artwork;
            var prefab=Resources.Load<PersonalInventoryView>("Presentation/PersonalInventory");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/PersonalInventory.");
            prefab.Validate();var view=UnityEngine.Object.Instantiate(prefab,parent,false);
            rows=view.Rows;icons=view.Icons;weapons=view.WeaponIds;weaponButtons=view.Weapons;ids=new string[rows.Length];
            detailIcon=view.DetailIcon;previous=view.Previous;next=view.Next;deposit=view.Deposit;use=view.Use;
            title=view.Title;detail=view.Detail;actionStatus=view.ActionStatus;
            portrait=new InventoryPortrait(view.Portrait,view.PortraitParts);
            for(int i=0;i<weaponButtons.Length;i++) {string weapon=weapons[i];weaponButtons[i].onClick.AddListener(()=>equip(weapon));}
            for(int i=0;i<rows.Length;i++) {int index=i;rows[i].onClick.AddListener(()=>{selected=ids[index];Refresh(container);});}
            previous.onClick.AddListener(()=>{page--;Refresh(container);});next.onClick.AddListener(()=>{page++;Refresh(container);});
            use.onClick.AddListener(()=>{session.RequestSelfHeal();Refresh(container);});
            deposit.onClick.AddListener(()=>{if(container!=null && selected!=null) session.Deposits.Request(container.id,selected,quantity());Refresh(container);});
            session.Deposits.Reconciled+=()=>Refresh(container);
        }
        public void Refresh(InventorySource destination)
        {
            container=destination;var actor=session.LocalActor;portrait.Refresh(actor);int count=actor?.inventory.Count??0;
            if(actor==null) container=null;
            for(int i=0;i<weaponButtons.Length;i++)
            {
                OriginalButtonStyle.SetSelected(weaponButtons[i],actor?.weapon==weapons[i]);
                weaponButtons[i].interactable=actor is {down:false};
            }
            page=Mathf.Clamp(page,0,Math.Max(0,(count-1)/rows.Length));
            previous.interactable=page>0;next.interactable=(page+1)*rows.Length<count;
            title.text=$"Личный инвентарь · {page+1}/{Math.Max(1,(count+rows.Length-1)/rows.Length)}";
            var chosen=actor?.inventory.Find(item=>item.id==selected);
            if(chosen==null) selected=null;
            for(int i=0;i<rows.Length;i++)
            {
                int index=page*rows.Length+i;var item=index<count?actor.inventory[index]:null;ids[i]=item?.id;
                int available=item==null?0:InventoryAvailability.Count(actor,item);
                artwork.Set(icons[i],item?.definition);
                rows[i].GetComponentInChildren<Text>().text=item==null?"—":$"{(item.id==selected?"› ":"")}{itemName(item.definition)} × {item.count}"+(available<item.count?$" (доступно {available})":"");
                rows[i].interactable=item!=null;
            }
            int availableChosen=chosen==null?0:InventoryAvailability.Count(actor,chosen);
            artwork.Set(detailIcon,chosen?.definition);
            int amount=quantity()==0?availableChosen:quantity();
            string rejection=container==null?"Рядом нет доступного контейнера.":
                session.Rules.Deposit.AccessRejection(new LootAccessContext(actor,container));
            if(rejection==null && chosen!=null && amount>0) rejection=session.Rules.Deposit.Rejection(new DepositContext(actor,container,chosen,amount));
            bool paused=actor!=null && session.Pause.IsPaused(actor.level);
            deposit.interactable=chosen!=null && availableChosen>=amount && amount>0 && rejection==null && !paused;
            use.interactable=chosen?.definition=="heal" && availableChosen>0 && !paused && HealingRules.CanApply(actor,actor,true);
            foreach(var button in new[]{use,deposit,previous,next})
                button.GetComponentInChildren<Text>().color=button.interactable?new Color(.65f,1,.75f):new Color(.3f,.4f,.34f);
            detail.text=chosen==null?"Выберите предмет в списке.":$"{itemName(chosen.definition)}\nВ наличии: {chosen.count}\nДоступно: {availableChosen}\n\n"+Description(chosen.definition);
            actionStatus.text=session.Deposits.Pending?"Ожидаем подтверждения передачи…":chosen!=null?rejection??"":"";
            deposit.GetComponentInChildren<Text>().text=quantity()==0?"Положить всю стопку →":"Положить 1 предмет →";
        }
        private static string Description(string id)=>id switch
        {
            "heal"=>"Восстанавливает здоровье. Можно использовать для поднятия союзника.",
            "grenade"=>"Ручная граната. Бросок — G.",
            "p10"=>"Боеприпасы 10-мм пистолета. Перезарядка — R.",
            "batt"=>"Боеприпасы лазерного пистолета. Перезарядка — R.",
            "caps"=>"Крышки.",
            _=>"Описание этого предмета пока отсутствует."
        };
    }
}

