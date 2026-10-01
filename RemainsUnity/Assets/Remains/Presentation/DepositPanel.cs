using System;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class DepositPanel
    {
        private readonly CoopSession session;
        private readonly Func<string,string> itemName;
        private readonly Func<int> quantity;
        private readonly Button[] rows=new Button[8];
        private readonly string[] ids=new string[8];
        private readonly Button previous,next;
        private readonly Text title;
        private InventorySource container;
        private int page;
        public RectTransform RowRect(int row)=>rows[row].GetComponent<RectTransform>();
        public DepositPanel(Transform parent,HudElements elements,CoopSession session,Func<string,string> itemName,Func<int> quantity)
        {
            this.session=session;this.itemName=itemName;this.quantity=quantity;
            title=elements.Label(parent,"Личный инвентарь · внести ↓",new Vector2(12,-12),new Vector2(335,40));
            for(int i=0;i<rows.Length;i++)
            {
                int index=i;
                rows[i]=elements.Button(parent,"",new Vector2(12,-55-i*35),new Vector2(335,32),()=>
                {if(container!=null && ids[index]!=null) session.Deposits.Request(container.id,ids[index],quantity());Refresh(container);});
                rows[i].GetComponentInChildren<Text>().fontSize=14;
            }
            previous=elements.Button(parent,"←",new Vector2(12,-350),new Vector2(65,32),()=>{page--;Refresh(container);});
            next=elements.Button(parent,"→",new Vector2(282,-350),new Vector2(65,32),()=>{page++;Refresh(container);});
        }
        public void Refresh(InventorySource destination)
        {
            container=destination;var actor=session.LocalActor;int count=actor?.inventory.Count??0;
            page=Mathf.Clamp(page,0,Math.Max(0,(count-1)/rows.Length));
            previous.interactable=page>0;next.interactable=(page+1)*rows.Length<count;
            title.text=session.Deposits.Pending?"Внесение: ожидаем подтверждения":"Личный инвентарь · внести ↓";
            for(int i=0;i<rows.Length;i++)
            {
                int index=page*rows.Length+i;var item=index<count?actor.inventory[index]:null;ids[i]=item?.id;
                int available=item==null?0:InventoryAvailability.Count(actor,item);
                rows[i].GetComponentInChildren<Text>().text=item==null?"Пусто":$"{itemName(item.definition)} × {item.count}"+(available<item.count?$" (доступно {available})":"");
                int amount=quantity()==0?available:quantity();
                rows[i].interactable=container!=null && item!=null && amount>0 && amount<=available &&
                    session.Rules.Deposit.AccessRejection(new LootAccessContext(actor,container))==null && session.Rules.Deposit.Rejection(new DepositContext(actor,container,item,amount))==null;
            }
        }
    }
}
