using System;
using System.Collections.Generic;
using System.Globalization;
using Remains.Domain;
using Remains.Networking;
using Remains.Content;
using Remains.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class PersonalInventoryPanel
    {
        private readonly CoopSession session;
        private readonly Func<ItemShortcutActions> itemActions;
        private readonly Func<string,string> itemName;
        private readonly Func<int> quantity;
        private readonly Button[] rows;
        private readonly Text[] rowLabels;
        private readonly int[] rowFontSizes;
        private readonly string[] rowText;
        private readonly Vector2[] rowLabelSizes;
        private readonly float[] rowPixelUnits;
        private readonly HashSet<string> shortenedRows=new(StringComparer.Ordinal);
        private const int MinimumRowFontSize=16;
        private readonly string[] weapons;
        private readonly Button[] weaponButtons;
        private readonly Image[] weaponIcons;
        private readonly Image[] icons;
        private readonly Image detailIcon;
        private readonly Image weaponPreview;
        private readonly Text weaponDetail;
        private readonly InventoryArtwork artwork;
        private readonly ItemTextCatalog itemText;
        private readonly InventoryPortrait portrait;
        private readonly string[] ids;
        private readonly Button previous,next,deposit,use;
        private readonly Scrollbar scrollbar;
        private readonly Text actionStatus;
        private readonly InventoryDescriptionView description;
        private InventorySource container;
        private string selected;
        private int firstVisibleRow;
        private InventoryCategory category;
        private readonly InventoryItemFilter filter;
        private readonly Button[] categories;
        private readonly List<ItemStack> visible=new();
        private readonly HashSet<string> legacyActors=new();
        public RectTransform RowRect(int row)=>rows[row].GetComponent<RectTransform>();
        public int Page=>firstVisibleRow/rows.Length;
        public int FirstVisibleRow=>firstVisibleRow;
        public InventoryCategory Category=>category;
        public string SelectedItemId=>selected;
        public RectTransform CategoryRect(InventoryCategory value)=>categories[(int)value].GetComponent<RectTransform>();
        public int PageSize=>rows.Length;
        public RectTransform NextRect=>next.GetComponent<RectTransform>();
        public RectTransform PreviousRect=>previous.GetComponent<RectTransform>();
        public RectTransform DepositRect=>deposit.GetComponent<RectTransform>();
        public RectTransform UseRect=>use.GetComponent<RectTransform>();
        public RectTransform WeaponRect(int index)=>weaponButtons[index].GetComponent<RectTransform>();
        public PersonalInventoryPanel(PersonalInventoryView view,CoopSession session,Func<string,string> itemName,Func<int> quantity,Action<string> equip,InventoryArtwork artwork,Action<string> equipItem=null,Action pageSound=null,ItemTextCatalog itemText=null,Func<ItemShortcutActions> itemActions=null)
        {
            this.itemActions=itemActions;
            this.itemText=itemText??ItemTextCatalog.Load();
            this.session=session;this.itemName=itemName;this.quantity=quantity;this.artwork=artwork;
            if(view==null) throw new InvalidOperationException("Не назначено содержимое личной страницы инвентаря.");
            view.Validate();
            filter=new InventoryItemFilter(WeaponCatalog.All);categories=view.Categories;
            for(int i=0;i<categories.Length;i++)
            {
                var choice=(InventoryCategory)i;
                categories[i].onClick.AddListener(()=>{category=choice;firstVisibleRow=0;Refresh(container);pageSound?.Invoke();});
            }
            rows=view.Rows;icons=view.Icons;weapons=view.WeaponIds;weaponButtons=view.Weapons;ids=new string[rows.Length];
            rowLabels=new Text[rows.Length];rowFontSizes=new int[rows.Length];rowText=new string[rows.Length];
            rowLabelSizes=new Vector2[rows.Length];rowPixelUnits=new float[rows.Length];
            for(int i=0;i<rows.Length;i++)
            {
                rowLabels[i]=rows[i].GetComponentInChildren<Text>(true);
                if(rowLabels[i]==null || rowLabels[i].font==null || rowLabels[i].fontSize<MinimumRowFontSize)
                    throw new InvalidOperationException($"Личный инвентарь: строка {rows[i].name} требует назначенный шрифт размером не меньше {MinimumRowFontSize}.");
                rowFontSizes[i]=rowLabels[i].fontSize;
                rowLabels[i].resizeTextForBestFit=false;
                rowLabels[i].supportRichText=false;
                rowLabels[i].horizontalOverflow=HorizontalWrapMode.Overflow;
                rowLabels[i].verticalOverflow=VerticalWrapMode.Truncate;
            }
            detailIcon=view.DetailIcon;previous=view.Previous;next=view.Next;deposit=view.Deposit;use=view.Use;
            scrollbar=view.Scrollbar;
            scrollbar.onValueChanged.AddListener(value=>
            {firstVisibleRow=Mathf.RoundToInt(value*Math.Max(0,visible.Count-rows.Length));Refresh(container);});
            weaponPreview=view.WeaponPreview;weaponDetail=view.WeaponDetail;
            description=view.Description;actionStatus=view.ActionStatus;
            portrait=new InventoryPortrait(view.Portrait,view.PortraitParts);
            view.PortraitParts=portrait.Parts;
            weaponIcons=new Image[weaponButtons.Length];
            for(int i=0;i<weaponButtons.Length;i++)
            {
                string weapon=i<weapons.Length?weapons[i]:null,slot=ItemShortcuts.Keys[i];
                weaponIcons[i]=weaponButtons[i].transform.Find("Иконка предмета")?.GetComponent<Image>() ?? throw new InvalidOperationException($"Нет иконки назначения {slot}.");
                weaponButtons[i].onClick.AddListener(()=>
                {
                    var actor=session.LocalActor;
                    if(actor?.weaponInventoryVersion==1)
                    {
                        if(ItemShortcuts.Assign(actor,slot,selected,out var reason)) session.PublishActor(actor);
                        else session.ShowNotice(reason);
                        Refresh(container);
                    }
                    else if(weapon!=null) {Debug.LogWarning($"Старый инвентарь не подготовлен: actor={actor?.id}, slot={slot}. Выбор определения доступен только для диагностики.",view);equip(weapon);}
                });
            }
            for(int i=0;i<rows.Length;i++) {int index=i;rows[i].onClick.AddListener(()=>{selected=ids[index];Refresh(container);});}
            previous.onClick.AddListener(()=>Scroll(-rows.Length));next.onClick.AddListener(()=>Scroll(rows.Length));
            view.ScrollRows+=Scroll;
            use.onClick.AddListener(()=>
            {
                if(selected!=null) Actions().ActivateItem(selected,toggleEquipped:true);
                Refresh(container);
            });
            deposit.onClick.AddListener(()=>{if(container!=null && selected!=null) session.Deposits.Request(container.id,selected,quantity());Refresh(container);});
            session.Deposits.Reconciled+=()=>Refresh(container);
        }
        private void Scroll(int rowsToMove) {firstVisibleRow+=rowsToMove;Refresh(container);}
        public void Refresh(InventorySource destination)
        {
            container=destination;var actor=session.LocalActor;
            if(actor!=null && actor.weaponInventoryVersion!=1 && legacyActors.Add(actor.id))
                Debug.LogWarning($"Личный инвентарь показывает диагностические определения: actor={actor.id}, inventoryVersion={actor.weaponInventoryVersion}. Для назначений любых предметов требуется миграция экземпляров.");
            visible.Clear();
            if(actor!=null) foreach(var item in actor.inventory) if(filter.Includes(item,category)) visible.Add(item);
            int count=visible.Count;
            if(!visible.Exists(item=>item.id==selected)) selected=null;
            for(int i=0;i<categories.Length;i++) OriginalButtonStyle.SetSelected(categories[i],i==(int)category);
            if(actor==null) container=null;
            for(int i=0;i<weaponButtons.Length;i++)
            {
                weaponButtons[i].gameObject.SetActive(true);
                bool instances=actor?.weaponInventoryVersion==1;
                var chosenItem=InventoryStacks.Find(actor,selected);
                string legacy=i<weapons.Length?weapons[i]:null;
                var assigned=ItemShortcuts.Get(actor,ItemShortcuts.Keys[i]);
                OriginalButtonStyle.SetSelected(weaponButtons[i],instances?chosenItem!=null && assigned?.id==selected:legacy!=null && actor?.weapon==legacy);
                weaponButtons[i].interactable=actor is {down:false} && (instances?chosenItem!=null:legacy!=null);
                weaponButtons[i].GetComponentInChildren<Text>().text=$"[{ItemShortcuts.Keys[i]}]";
                artwork.Set(weaponIcons[i],instances?assigned?.definition:legacy);
            }
            firstVisibleRow=Mathf.Clamp(firstVisibleRow,0,Math.Max(0,count-rows.Length));
            scrollbar.gameObject.SetActive(count>rows.Length);
            scrollbar.size=count==0?1:Mathf.Min(1,rows.Length/(float)count);
            scrollbar.SetValueWithoutNotify(count>rows.Length?firstVisibleRow/(float)(count-rows.Length):0);
            previous.interactable=firstVisibleRow>0;next.interactable=firstVisibleRow+rows.Length<count;
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
                SetRowLabel(i,item?.id==selected && item!=null?"› ":"",item==null?"":itemName(item.definition),item==null?"":quantityText+
                    (CosmeticEquipment.IsEquipped(actor,item.id)?" · надето":"")+(available<item.count?$" (доступно {available})":""),item?.id);
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
            detailIcon.gameObject.SetActive(!weaponSelected);
            weaponPreview.gameObject.SetActive(weaponSelected);weaponDetail.gameObject.SetActive(weaponSelected);
            portrait.Refresh(weaponSelected?null:actor);
            foreach(var control in new[]{use,deposit,previous,next}) control.gameObject.SetActive(true);
            actionStatus.gameObject.SetActive(true);
            if(weaponSelected)
            {
                artwork.Set(weaponPreview,chosen.definition);
                weaponDetail.text=itemName(chosen.definition)+"\n"+WeaponDescription(actor,chosen);
            }
            var availability=chosen==null?default:Actions().GetAvailability(chosen.id,toggleEquipped:true);
            use.interactable=availability.Enabled;
            use.GetComponentInChildren<Text>().text=availability.Caption??"Использовать";
            foreach(var button in new[]{use,deposit,previous,next})
                button.GetComponentInChildren<Text>().color=button.interactable?new Color(.65f,1,.75f):new Color(.3f,.4f,.34f);
            description.SetText(chosen==null?(count==0?"В этой категории пока нет предметов.":"Выберите предмет в списке."):$"{itemName(chosen.definition)}\nВ наличии: {chosen.count}\nДоступно: {availableChosen}\n\n"+(weaponSelected?WeaponDescription(actor,chosen):(Remains.Domain.MineRules.PlacedWeapon(chosen.definition)!=null?"Мина: выберите, чтобы держать телекинезом; ЛКМ или G — отпустить\n\n":"")+
                itemText.Description(chosen.definition,"PersonalInventoryPanel: actor="+actor.id+", item="+chosen.id)),actor?.id,chosen?.id,!weaponSelected);
            actionStatus.text=session.Deposits.Pending?"Ожидаем подтверждения передачи…":chosen==null?"":
                availability.Rejection??(container==null?"":rejection??"");
            deposit.GetComponentInChildren<Text>().text=quantity()==0?"Положить всю стопку →":"Положить 1 предмет →";
        }
        // Fit only changed row text/layout. Refresh itself never repeats font generation
        // for an unchanged row; quantity and equipped status remain visible if a mod name is shortened.
        private void SetRowLabel(int index,string prefix,string name,string suffix,string item)
        {
            if(name==null || item!=null && string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException($"Личный инвентарь: отсутствует название строки {rows[index].name}, actor={session.LocalActor?.id}, item={item}.");
            name=name.Replace('\r',' ').Replace('\n',' ').Replace('\t',' ');
            var label=rowLabels[index];var size=label.rectTransform.rect.size;float units=label.pixelsPerUnit;
            string full=prefix+name+suffix;
            if(rowText[index]==full && rowLabelSizes[index]==size && rowPixelUnits[index]==units) return;
            if(!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x<=0 || size.y<=0 || !float.IsFinite(units) || units<=0)
                throw new InvalidOperationException($"Личный инвентарь: неверные размеры строки {rows[index].name}, actor={session.LocalActor?.id}, item={item}, size={size}, pixelsPerUnit={units}.");
            int font=rowFontSizes[index];
            while(font>MinimumRowFontSize && !Fits(full,font)) font--;
            string display=full;
            if(!Fits(full,font))
            {
                if(!Fits(prefix+"…"+suffix,font))
                    throw new InvalidOperationException($"Личный инвентарь: количество и состояние не помещаются в строку {rows[index].name}, actor={session.LocalActor?.id}, item={item}, size={size}, font={font}.");
                var elements=StringInfo.ParseCombiningCharacters(name);int low=0,high=elements.Length;
                while(low<high)
                {
                    int middle=(low+high+1)/2;int length=middle==elements.Length?name.Length:elements[middle];
                    if(Fits(prefix+name.Substring(0,length)+"…"+suffix,font)) low=middle;else high=middle-1;
                }
                int keep=low==elements.Length?name.Length:elements[low];
                display=prefix+name.Substring(0,keep)+"…"+suffix;
                string key=session.LocalActor?.id+"/"+item+"/"+full+"/"+size;
                if(shortenedRows.Add(key)) Debug.LogWarning($"Длинное название сокращено только в строке инвентаря: actor={session.LocalActor?.id}, item={item}, row={rows[index].name}, size={size}, font={font}, name={name}. Полное название доступно в описании справа.");
            }
            label.fontSize=font;label.text=display;
            rowText[index]=full;rowLabelSizes[index]=size;rowPixelUnits[index]=label.pixelsPerUnit;
            bool Fits(string text,int fontSize)
            {
                float candidateUnits=label.canvas==null || label.font.dynamic?units:
                    label.font.fontSize>0?label.font.fontSize/(float)fontSize:1;
                var settings=label.GetGenerationSettings(Vector2.zero);
                settings.fontSize=label.font.dynamic?fontSize:0;settings.scaleFactor=candidateUnits;
                return label.cachedTextGeneratorForLayout.GetPreferredWidth(text,settings)/candidateUnits<=size.x-.5f &&
                    label.cachedTextGeneratorForLayout.GetPreferredHeight(text,settings)/candidateUnits<=size.y;
            }
        }
        private ItemShortcutActions Actions()=>itemActions?.Invoke()??throw new InvalidOperationException(
            $"Личный инвентарь: не подключён общий обработчик действий, actor={session.LocalActor?.id}, item={selected}. Настройте PrototypeHud.ConfigureItemActions.");
        private string WeaponDescription(ActorState actor,ItemStack item)
        {
            var definition=WeaponCatalog.Get(item.definition);
            string resource=item.definition=="grenade"?$"В стопке: {item.count} · ЛКМ — бросить\n":definition.magazineSize>0?$"В магазине: {item.weaponState.rounds}/{definition.magazineSize}\n":
                definition.meleeMotion is {Length:>0}?"Ближний бой\n":"";
            float damage=session.Rules.Damage.Calculate(item.definition,0);
            if(!float.IsFinite(damage) || damage<0)
                throw new InvalidOperationException($"Некорректный урон в описании оружия {item.definition}: {damage}.");
            return $"Урон без движения: {damage:0.##}\nИнтервал атак: {definition.cooldown:0.##} с\n"+
                resource+$"Выбор: {(!HeldItemSelection.HasSelection(actor) && actor.equippedWeaponItem==item.id?"в копытах":"в инвентаре")}";
        }

    }
}

