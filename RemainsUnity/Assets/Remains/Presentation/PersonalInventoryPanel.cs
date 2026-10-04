using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Networking;
using Remains.Content;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class PersonalInventoryPanel
    {
        private readonly CoopSession session;
        private readonly Action<string> equipItem;
        private readonly Func<string,string> itemName;
        private readonly Func<int> quantity;
        private readonly Button[] rows;
        private readonly string[] weapons;
        private readonly Button[] weaponButtons;
        private readonly Image[] weaponIcons;
        private readonly string[] legacyLabels;
        private readonly Image[] icons;
        private readonly Image detailIcon;
        private readonly Image weaponPreview;
        private readonly Text weaponDetail;
        private readonly InventoryArtwork artwork;
        private readonly InventoryPortrait portrait;
        private readonly string[] ids;
        private readonly Button previous,next,deposit,use;
        private readonly Scrollbar scrollbar;
        private readonly Text title,detail,actionStatus;
        private InventorySource container;
        private string selected;
        private int firstVisibleRow;
        private InventoryCategory category;
        private readonly InventoryItemFilter filter;
        private readonly Button[] categories;
        private readonly List<ItemStack> visible=new();
        public RectTransform RowRect(int row)=>rows[row].GetComponent<RectTransform>();
        public int Page=>firstVisibleRow/rows.Length;
        public int FirstVisibleRow=>firstVisibleRow;
        public InventoryCategory Category=>category;
        public RectTransform CategoryRect(InventoryCategory value)=>categories[(int)value].GetComponent<RectTransform>();
        public int PageSize=>rows.Length;
        public RectTransform NextRect=>next.GetComponent<RectTransform>();
        public RectTransform PreviousRect=>previous.GetComponent<RectTransform>();
        public RectTransform DepositRect=>deposit.GetComponent<RectTransform>();
        public RectTransform UseRect=>use.GetComponent<RectTransform>();
        public RectTransform WeaponRect(int index)=>weaponButtons[index].GetComponent<RectTransform>();
        public PersonalInventoryPanel(PersonalInventoryView view,CoopSession session,Func<string,string> itemName,Func<int> quantity,Action<string> equip,InventoryArtwork artwork,Action<string> equipItem=null)
        {
            this.equipItem=equipItem;
            this.session=session;this.itemName=itemName;this.quantity=quantity;this.artwork=artwork;
            if(view==null) throw new InvalidOperationException("Не назначено содержимое личной страницы инвентаря.");
            view.Validate();
            filter=new InventoryItemFilter(WeaponCatalog.All);categories=view.Categories;
            for(int i=0;i<categories.Length;i++)
            {
                var choice=(InventoryCategory)i;
                categories[i].onClick.AddListener(()=>{category=choice;firstVisibleRow=0;Refresh(container);});
            }
            rows=view.Rows;icons=view.Icons;weapons=view.WeaponIds;weaponButtons=view.Weapons;ids=new string[rows.Length];
            detailIcon=view.DetailIcon;previous=view.Previous;next=view.Next;deposit=view.Deposit;use=view.Use;
            scrollbar=view.Scrollbar;
            scrollbar.onValueChanged.AddListener(value=>
            {firstVisibleRow=Mathf.RoundToInt(value*Math.Max(0,visible.Count-rows.Length));Refresh(container);});
            weaponPreview=view.WeaponPreview;weaponDetail=view.WeaponDetail;
            title=view.Title;detail=view.Detail;actionStatus=view.ActionStatus;
            portrait=new InventoryPortrait(view.Portrait,view.PortraitParts);
            weaponIcons=new Image[weaponButtons.Length];legacyLabels=new string[weaponButtons.Length];
            for(int i=0;i<weaponButtons.Length;i++)
            {
                string weapon=weapons[i],slot=(i+1).ToString();
                weaponIcons[i]=weaponButtons[i].transform.Find("Иконка предмета")?.GetComponent<Image>() ?? throw new InvalidOperationException($"Нет иконки назначения {slot}.");
                legacyLabels[i]=weaponButtons[i].GetComponentInChildren<Text>().text;
                weaponButtons[i].onClick.AddListener(()=>
                {
                    var actor=session.LocalActor;
                    if(actor?.weaponInventoryVersion==1)
                    {
                        if(WeaponInventory.Assign(actor,slot,selected,out var reason)) session.PublishActor(actor);
                        else session.ShowNotice(reason);
                        Refresh(container);
                    }
                    else equip(weapon);
                });
            }
            for(int i=0;i<rows.Length;i++) {int index=i;rows[i].onClick.AddListener(()=>{selected=ids[index];Refresh(container);});}
            previous.onClick.AddListener(()=>Scroll(-rows.Length));next.onClick.AddListener(()=>Scroll(rows.Length));
            view.ScrollRows+=Scroll;
            use.onClick.AddListener(()=>
            {
                var actor=session.LocalActor;var item=InventoryStacks.Find(actor,selected);
                if(WeaponInventory.IsEquippable(item))
                {
                    if(this.equipItem==null) throw new InvalidOperationException("Не подключён выбор экземпляра оружия в инвентаре.");
                    this.equipItem(actor.equippedWeaponItem==item.id?null:item.id);
                }
                else if(item?.definition=="heal") session.RequestSelfHeal(item.id);
                Refresh(container);
            });
            deposit.onClick.AddListener(()=>{if(container!=null && selected!=null) session.Deposits.Request(container.id,selected,quantity());Refresh(container);});
            session.Deposits.Reconciled+=()=>Refresh(container);
        }
        private void Scroll(int rowsToMove) {firstVisibleRow+=rowsToMove;Refresh(container);}
        public void Refresh(InventorySource destination)
        {
            container=destination;var actor=session.LocalActor;
            visible.Clear();
            if(actor!=null) foreach(var item in actor.inventory) if(filter.Includes(item,category)) visible.Add(item);
            int count=visible.Count;
            if(!visible.Exists(item=>item.id==selected)) selected=null;
            for(int i=0;i<categories.Length;i++) OriginalButtonStyle.SetSelected(categories[i],i==(int)category);
            if(actor==null) container=null;
            for(int i=0;i<weaponButtons.Length;i++)
            {
                bool instances=actor?.weaponInventoryVersion==1;
                var chosenWeapon=InventoryStacks.Find(actor,selected);
                if(!WeaponInventory.IsEquippable(chosenWeapon)) chosenWeapon=null;
                OriginalButtonStyle.SetSelected(weaponButtons[i],instances?chosenWeapon!=null && WeaponInventory.Shortcut(actor,(i+1).ToString())?.id==selected:actor?.weapon==weapons[i]);
                weaponButtons[i].interactable=actor is {down:false} && (!instances || chosenWeapon!=null);
                weaponButtons[i].GetComponentInChildren<Text>().text=instances?$"Клавиша {i+1}":legacyLabels[i];
                artwork.Set(weaponIcons[i],instances?WeaponInventory.Shortcut(actor,(i+1).ToString())?.definition:weapons[i]);
            }
            firstVisibleRow=Mathf.Clamp(firstVisibleRow,0,Math.Max(0,count-rows.Length));
            scrollbar.gameObject.SetActive(count>rows.Length);
            scrollbar.size=count==0?1:Mathf.Min(1,rows.Length/(float)count);
            scrollbar.SetValueWithoutNotify(count>rows.Length?firstVisibleRow/(float)(count-rows.Length):0);
            previous.interactable=firstVisibleRow>0;next.interactable=firstVisibleRow+rows.Length<count;
            title.text=count==0?"Инвентарь · пусто":$"Инвентарь · {firstVisibleRow+1}–{Math.Min(count,firstVisibleRow+rows.Length)}/{count}";
            var chosen=InventoryStacks.Find(actor,selected);
            if(chosen==null) selected=null;
            for(int i=0;i<rows.Length;i++)
            {
                int index=firstVisibleRow+i;var item=index<count?visible[index]:null;ids[i]=item?.id;
                rows[i].gameObject.SetActive(item!=null);
                int available=item==null?0:InventoryAvailability.Count(actor,item);
                artwork.Set(icons[i],item?.definition);
                string quantityText=item?.weaponState==null?$" × {item?.count}":WeaponCatalog.Get(item.definition).magazineSize>0?
                    $" · {item.weaponState.rounds}/{WeaponCatalog.Get(item.definition).magazineSize}":"";
                rows[i].GetComponentInChildren<Text>().text=item==null?"":$"{(item.id==selected?"› ":"")}{itemName(item.definition)}{quantityText}"+(available<item.count?$" (доступно {available})":"");
                rows[i].interactable=item!=null;
                OriginalButtonStyle.SetSelected(rows[i],item!=null && item.id==selected);
            }
            int availableChosen=chosen==null?0:InventoryAvailability.Count(actor,chosen);
            artwork.Set(detailIcon,chosen?.definition);
            int amount=quantity()==0?availableChosen:quantity();
            string rejection=container==null?"Рядом нет доступного контейнера.":
                session.Rules.Deposit.AccessRejection(new LootAccessContext(actor,container));
            if(rejection==null && chosen!=null && amount>0) rejection=session.Rules.Deposit.Rejection(new DepositContext(actor,container,chosen,amount));
            bool paused=actor!=null && session.Pause.IsPaused(actor.level);
            deposit.interactable=chosen!=null && availableChosen>=amount && amount>0 && rejection==null && !paused;
            bool weaponSelected=WeaponInventory.IsEquippable(chosen) && actor.weaponInventoryVersion==1;
            detail.gameObject.SetActive(!weaponSelected);detailIcon.gameObject.SetActive(!weaponSelected);
            weaponPreview.gameObject.SetActive(weaponSelected);weaponDetail.gameObject.SetActive(weaponSelected);
            portrait.Refresh(weaponSelected?null:actor);
            if(weaponSelected)
            {
                artwork.Set(weaponPreview,chosen.definition);
                weaponDetail.text=itemName(chosen.definition)+"\n"+WeaponDescription(actor,chosen);
            }
            use.interactable=availableChosen>0 && !paused && actor is {down:false} &&
                (weaponSelected || chosen?.definition=="heal" && HealingRules.CanApply(actor,actor,true));
            use.GetComponentInChildren<Text>().text=weaponSelected?(actor.equippedWeaponItem==chosen.id?"Убрать оружие":"Взять в копыта"):"Использовать";
            foreach(var button in new[]{use,deposit,previous,next})
                button.GetComponentInChildren<Text>().color=button.interactable?new Color(.65f,1,.75f):new Color(.3f,.4f,.34f);
            detail.text=chosen==null?(count==0?"В этой категории пока нет предметов.":"Выберите предмет в списке."):$"{itemName(chosen.definition)}\nВ наличии: {chosen.count}\nДоступно: {availableChosen}\n\n"+(weaponSelected?WeaponDescription(actor,chosen):Description(chosen.definition));
            string useRejection=chosen?.definition=="heal"?HealingRules.Rejection(actor,actor,true):null;
            actionStatus.text=session.Deposits.Pending?"Ожидаем подтверждения передачи…":chosen==null?"":
                paused?"Область на паузе. Действия временно недоступны.":
                useRejection??(container==null?"":rejection??"");
            deposit.GetComponentInChildren<Text>().text=quantity()==0?"Положить всю стопку →":"Положить 1 предмет →";
        }
        private string WeaponDescription(ActorState actor,ItemStack item)
        {
            var definition=WeaponCatalog.Get(item.definition);
            string resource=item.definition=="grenade"?$"В стопке: {item.count} · ЛКМ — бросить\n":definition.magazineSize>0?$"В магазине: {item.weaponState.rounds}/{definition.magazineSize}\n":
                definition.meleeMotion is {Length:>0}?"Ближний бой\n":"";
            float damage=session.Rules.Damage.Calculate(item.definition,0);
            if(!float.IsFinite(damage) || damage<0)
                throw new InvalidOperationException($"Некорректный урон в описании оружия {item.definition}: {damage}.");
            return $"Урон без движения: {damage:0.##}\nИнтервал атак: {definition.cooldown:0.##} с\n"+
                resource+$"Выбор: {(actor.equippedWeaponItem==item.id?"в копытах":"в инвентаре")}";
        }
        private static string Description(string id)=>id switch
        {
            "heal"=>"Восстанавливает здоровье. Можно использовать для поднятия союзника.",
            "grenade"=>"Ручная граната. Возьмите в копыта и бросайте левой кнопкой мыши. Быстрый бросок — G.",
            "p10"=>"Боеприпасы 10-мм пистолета. Перезарядка — R.",
            "batt"=>"Боеприпасы лазерного пистолета. Перезарядка — R.",
            "caps"=>"Крышки.",
            _=>"Описание этого предмета пока отсутствует."
        };
    }
}

