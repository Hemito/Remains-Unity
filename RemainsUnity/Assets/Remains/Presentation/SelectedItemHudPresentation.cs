using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
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
        private KeyboardBindings keys=KeyboardBindings.Defaults;
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
        public void SetBindings(KeyboardBindings value)=>keys=value;

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
                SetItem(null,false);SetAmmo(null);SetText(view.ItemName,Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.3d6ebae018a50653", "Неподдерживаемый инвентарь", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs"));
                SetText(view.Counter,"");SetText(view.ActionState,Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.0254cee2ea38afc1", "Ошибка версии инвентаря — см. журнал.", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs"));return;
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
                if(item==null) SetText(view.ItemName,Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.9549a63436da6057", "Предмет закончился", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs"));
                counter=item==null?"":Remains.Content.GameLocalization.Format("source.SelectedItemHudPresentation.a20549b830fa25c5", $"В копытах: {available}", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs");
                action=item==null?Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.35fd5c3f31d38807", "Предмет закончился · выберите другой", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs"):available==0?
                    Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.dfd48f5155be7826", "Вещь зарезервирована для передачи…", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs"):Remains.Content.GameLocalization.Format("source.SelectedItemHudPresentation.49ac388aa9864da0", $"ЛКМ — бросить 1 · {keys.Label(PlayerBinding.Sprint)}+ЛКМ — стопку", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs");
            }
            else if(string.IsNullOrEmpty(id))
            {
                SetAmmo(null);
                SetText(view.ItemName,Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.334dc6d9b121425d", "Оружие не выбрано", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs"));
                if((!string.IsNullOrEmpty(actor.weapon) || !string.IsNullOrEmpty(actor.equippedWeaponItem)) &&
                    missing.Add("selection:"+actor.id+"\n"+actor.equippedWeaponItem+"\n"+actor.weapon))
                    Debug.LogWarning($"HUD выбранной вещи: нет выбранного экземпляра, actor={actor.id}, weapon={actor.weapon}, item={actor.equippedWeaponItem}, inventoryVersion={actor.weaponInventoryVersion}. Определение не подставлено вместо отсутствующей вещи.");
            }
            else
            {
                // Definition metadata is resolved only when the selected ID changes.
                weapon??=WeaponCatalog.Get(id);
                SetAmmo(string.IsNullOrEmpty(weapon.ammo)?null:weapon.ammo);
                if(weapon.IsGrenade)
                {
                    counter=Remains.Content.GameLocalization.Format("source.SelectedItemHudPresentation.8628511c3fcc729c", $"В стопке: {available}", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs");action=Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.f29de0f1803f07df", "ЛКМ — бросить", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs");
                }
                else if(!weapon.UsesMagazine)
                {
                    action=weapon.speedDamage>0?Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.748baa09f8582eaa", "ЛКМ — удар · урон зависит от скорости", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs"):Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.a4b1bdcf0015b4f1", "ЛКМ — атака", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs");
                }
                else
                {
                    var magazine=WeaponAmmunition.Magazine(actor,id);
                    if(magazine==null)
                    {
                        if(missing.Add("magazine:"+actor.id+":"+id))
                            Debug.LogWarning($"HUD выбранной вещи: нет магазина, actor={actor.id}, item={item?.id}, definition={id}, inventoryVersion={actor.weaponInventoryVersion}.");
                        action=Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.3204e2669c5d8d06", "Ошибка данных магазина — см. журнал.", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs");
                    }
                    else
                    {
                        int reserve=WeaponAmmunition.Reserve(actor,weapon);
                        counter=Remains.Content.GameLocalization.Format("source.SelectedItemHudPresentation.eb8513f9b67b595b", $"{magazine.rounds} / {weapon.magazineSize}    Запас: {reserve}", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs");
                        if(weapon.rechargeSeconds>0)
                        {
                            counter=$"{magazine.rounds} / {weapon.magazineSize}";
                            action=magazine.rounds<weapon.magazineSize?Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.a27cd6ea79ad23f0", "Автоматическая подзарядка", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs"):Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.6acc86eafa6da92a", "Заряд полный", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs");
                        }
                        else
                        action=reloadRemaining.HasValue?Remains.Content.GameLocalization.Format("source.SelectedItemHudPresentation.981591b5dd6d3f6f", $"Перезарядка · {Mathf.Max(0,reloadRemaining.Value):0.0} с", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs"):
                            magazine.rounds<weapon.ammoPerShot?(reserve>0?Remains.Content.GameLocalization.Format("source.SelectedItemHudPresentation.8d90734a05ea7e72", $"Магазин пуст · {keys.Label(PlayerBinding.Reload)}", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs"):Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.34d9dddfbfc6d33f", "Нет боеприпасов", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs")):
                            magazine.rounds<weapon.magazineSize && reserve>0?Remains.Content.GameLocalization.Format("source.SelectedItemHudPresentation.5423b10df68783af", $"{keys.Label(PlayerBinding.Reload)} — перезарядить", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs"):"";
                    }
                }
                if(!legacy && available==0) action=Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.dfd48f5155be7826", "Вещь зарезервирована для передачи…", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs");
                else if(legacy) action=Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.f2cf5461945197a9", "Старый инвентарь · требуется миграция", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs");
            }
            if(actor.down) action=Remains.Content.GameLocalization.UiText("source.SelectedItemHudPresentation.50b52b0dc904b3c0", "Без сознания · нужна помощь", "RemainsUnity/Assets/Remains/Presentation/SelectedItemHudPresentation.cs");
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
