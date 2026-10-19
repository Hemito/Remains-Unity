using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    // Consumes the actual local selection and shared inventory/ammunition queries.
    // Resources are prepared by the owner once; Refresh never loads artwork.
    public sealed class SelectedItemHudPresentation
    {
        private readonly SelectedItemHudView view;
        private readonly InventoryArtwork artwork;
        private readonly ItemTextCatalog text;
        private readonly HashSet<string> missing=new(StringComparer.Ordinal);
        private string shownDefinition,shownAmmo;
        private WeaponDefinition weapon;
        private bool itemPrepared,ammoPrepared;
        private static readonly Color UnavailableColor=new(.5f,.5f,.5f,1);

        public SelectedItemHudPresentation(SelectedItemHudView view,InventoryArtwork artwork,ItemTextCatalog text)
        {
            this.view=view??throw new ArgumentNullException(nameof(view));
            this.artwork=artwork??throw new ArgumentNullException(nameof(artwork));
            this.text=text??throw new ArgumentNullException(nameof(text));
            view.Validate();
        }

        public void Refresh(ActorState actor,bool visible,float? reloadRemaining=null)
        {
            if(actor==null)
            {
                SetItem(null,false);SetAmmo(null);SetText(view.Counter,"");SetText(view.ActionState,"");
                SetText(view.ItemName,"");return;
            }
            if(!visible) return;
            if(actor.weaponInventoryVersion!=0 && actor.weaponInventoryVersion!=1)
            {
                if(missing.Add("version:"+actor.id+":"+actor.weaponInventoryVersion))
                    Debug.LogWarning($"HUD выбранной вещи: неподдерживаемая версия инвентаря, actor={actor.id}, version={actor.weaponInventoryVersion}. Выбранная вещь не подставлена из старых полей.");
                SetItem(null,false);SetAmmo(null);SetText(view.ItemName,"Неподдерживаемый инвентарь");
                SetText(view.Counter,"");SetText(view.ActionState,"Ошибка версии инвентаря — см. журнал.");return;
            }

            bool ordinaryHeld=HeldItemSelection.HasSelection(actor);
            var item=ordinaryHeld?HeldItemSelection.Selected(actor):WeaponInventory.Selected(actor);
            bool legacy=actor.weaponInventoryVersion!=1;
            string id=ordinaryHeld?item?.definition:legacy?actor.weapon:item?.definition;
            int available=item==null?0:InventoryAvailability.Count(actor,item);
            SetItem(id,id!=null && !actor.down && (legacy || available>0));
            string counter="",action="";
            if(ordinaryHeld)
            {
                SetAmmo(null);
                if(item==null) SetText(view.ItemName,"Предмет закончился");
                counter=item==null?"":$"В копытах: {available}";
                action=item==null?"Предмет закончился · выберите другой":available==0?
                    "Вещь зарезервирована для передачи…":"ЛКМ — бросить 1 · Shift+ЛКМ — стопку";
            }
            else if(string.IsNullOrEmpty(id))
            {
                SetAmmo(null);
                SetText(view.ItemName,"Оружие не выбрано");
                if((!string.IsNullOrEmpty(actor.weapon) || !string.IsNullOrEmpty(actor.equippedWeaponItem)) &&
                    missing.Add("selection:"+actor.id+"\n"+actor.equippedWeaponItem+"\n"+actor.weapon))
                    Debug.LogWarning($"HUD выбранной вещи: нет выбранного экземпляра, actor={actor.id}, weapon={actor.weapon}, item={actor.equippedWeaponItem}, inventoryVersion={actor.weaponInventoryVersion}. Определение не подставлено вместо отсутствующей вещи.");
            }
            else
            {
                // Definition metadata is resolved only when the selected ID changes.
                weapon??=WeaponCatalog.Get(id);
                SetAmmo(string.IsNullOrEmpty(weapon.ammo)?null:weapon.ammo);
                if(id=="grenade")
                {
                    counter=$"В стопке: {available}";action="ЛКМ — бросить";
                }
                else if(!weapon.UsesMagazine)
                {
                    action=weapon.speedDamage>0?"ЛКМ — удар · урон зависит от скорости":"ЛКМ — атака";
                }
                else
                {
                    var magazine=WeaponAmmunition.Magazine(actor,id);
                    if(magazine==null)
                    {
                        if(missing.Add("magazine:"+actor.id+":"+id))
                            Debug.LogWarning($"HUD выбранной вещи: нет магазина, actor={actor.id}, item={item?.id}, definition={id}, inventoryVersion={actor.weaponInventoryVersion}.");
                        action="Ошибка данных магазина — см. журнал.";
                    }
                    else
                    {
                        int reserve=WeaponAmmunition.Reserve(actor,weapon);
                        counter=$"{magazine.rounds} / {weapon.magazineSize}    Запас: {reserve}";
                        if(weapon.rechargeSeconds>0)
                        {
                            counter=$"{magazine.rounds} / {weapon.magazineSize}";
                            action=magazine.rounds<weapon.magazineSize?"Автоматическая подзарядка":"Заряд полный";
                        }
                        else
                        action=reloadRemaining.HasValue?$"Перезарядка · {Mathf.Max(0,reloadRemaining.Value):0.0} с":
                            magazine.rounds<weapon.ammoPerShot?(reserve>0?"Магазин пуст · R":"Нет боеприпасов"):
                            magazine.rounds<weapon.magazineSize && reserve>0?"R — перезарядить":"";
                    }
                }
                if(!legacy && available==0) action="Вещь зарезервирована для передачи…";
                else if(legacy) action="Старый инвентарь · требуется миграция";
            }
            if(actor.down) action="Без сознания · нужна помощь";
            var ammoColor=actor.down || !legacy && item!=null && available==0?UnavailableColor:Color.white;
            if(view.AmmoIcon.color!=ammoColor) view.AmmoIcon.color=ammoColor;
            SetText(view.Counter,counter);SetText(view.ActionState,action);
        }

        private void SetItem(string id,bool available)
        {
            if(!itemPrepared || shownDefinition!=id)
            {
                itemPrepared=true;shownDefinition=id;weapon=null;
                artwork.Set(view.Icon,id);
                SetText(view.ItemName,id==null?"":text.Name(id,"HUD выбранной вещи"));
            }
            var color=available?Color.white:UnavailableColor;
            if(view.Icon.color!=color) view.Icon.color=color;
        }
        private void SetAmmo(string id)
        {
            if(ammoPrepared && shownAmmo==id) return;
            ammoPrepared=true;shownAmmo=id;artwork.Set(view.AmmoIcon,id);
        }
        private static void SetText(Text target,string value)
        {if(target.text!=value) target.text=value;}
    }
}
