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
        private readonly Button[] rows=new Button[8];
        private readonly string[] ids=new string[8];
        private readonly Button previous,next,deposit,use;
        private readonly Text title,detail;
        private InventorySource container;
        private string selected;
        private int page;
        public RectTransform RowRect(int row)=>rows[row].GetComponent<RectTransform>();
        public RectTransform DepositRect=>deposit.GetComponent<RectTransform>();
        public RectTransform UseRect=>use.GetComponent<RectTransform>();
        public PersonalInventoryPanel(Transform parent,HudElements ui,CoopSession session,Func<string,string> itemName,Func<int> quantity,Action<string> equip)
        {
            this.session=session;this.itemName=itemName;this.quantity=quantity;
            title=ui.Label(parent,"Личный инвентарь",new Vector2(20,-16),new Vector2(760,32));
            string[] weapons={"spear","crowbar","pistol","energy"};
            string[] names={"1 · Копьё","2 · Лом","3 · Пистолет","4 · Лазер"};
            for(int i=0;i<4;i++)
            {
                string weapon=weapons[i];
                ui.Button(parent,names[i],new Vector2(20+i*195,-56),new Vector2(185,38),()=>equip(weapon));
            }
            for(int i=0;i<rows.Length;i++)
            {
                int index=i;
                rows[i]=ui.Button(parent,"",new Vector2(20,-112-i*40),new Vector2(420,36),()=>{selected=ids[index];Refresh(container);});
                var label=rows[i].GetComponentInChildren<Text>();label.fontSize=16;label.alignment=TextAnchor.MiddleLeft;
            }
            previous=ui.Button(parent,"←",new Vector2(20,-449),new Vector2(65,34),()=>{page--;Refresh(container);});
            next=ui.Button(parent,"→",new Vector2(375,-449),new Vector2(65,34),()=>{page++;Refresh(container);});
            var hint=ui.Label(parent,"Выберите предмет, затем действие справа",new Vector2(20,-494),new Vector2(420,30));hint.fontSize=15;
            detail=ui.Label(parent,"",new Vector2(465,-112),new Vector2(325,220));detail.fontSize=16;
            use=ui.Button(parent,"Использовать аптечку",new Vector2(465,-338),new Vector2(325,42),()=>{session.RequestSelfHeal();Refresh(container);});
            deposit=ui.Button(parent,"Положить в контейнер →",new Vector2(465,-390),new Vector2(325,42),()=>
            {if(container!=null && selected!=null) session.Deposits.Request(container.id,selected,quantity());Refresh(container);});
            var armor=ui.Panel(parent,"Место экипировки",new Vector2(465,-448),new Vector2(325,70));
            var labelArmor=ui.Label(armor.transform,"Экипировка\nБроня — пока недоступна",new Vector2(12,-8),new Vector2(300,56));
            labelArmor.color=new Color(.38f,.55f,.45f);labelArmor.fontSize=16;
            session.Deposits.Reconciled+=()=>Refresh(container);
        }
        public void Refresh(InventorySource destination)
        {
            container=destination;var actor=session.LocalActor;int count=actor?.inventory.Count??0;
            if(actor==null) container=null;
            page=Mathf.Clamp(page,0,Math.Max(0,(count-1)/rows.Length));
            previous.interactable=page>0;next.interactable=(page+1)*rows.Length<count;
            title.text=$"Личный инвентарь · {page+1}/{Math.Max(1,(count+7)/8)}";
            var chosen=actor?.inventory.Find(item=>item.id==selected);
            if(chosen==null) selected=null;
            for(int i=0;i<rows.Length;i++)
            {
                int index=page*rows.Length+i;var item=index<count?actor.inventory[index]:null;ids[i]=item?.id;
                int available=item==null?0:InventoryAvailability.Count(actor,item);
                rows[i].GetComponentInChildren<Text>().text=item==null?"—":$"{(item.id==selected?"› ":"")}{itemName(item.definition)} × {item.count}"+(available<item.count?$" (доступно {available})":"");
                rows[i].interactable=item!=null;
            }
            int availableChosen=chosen==null?0:InventoryAvailability.Count(actor,chosen);
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
            if(session.Deposits.Pending) detail.text+="\nОжидаем подтверждения передачи…";
            else if(chosen!=null && rejection!=null) detail.text+="\n"+rejection;
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
