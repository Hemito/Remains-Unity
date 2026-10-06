using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public sealed class CombatHud
    {
        private readonly GameObject vitals,loadout;
        private readonly Text name,healthText,manaText,ammunition,grenades;
        private readonly Image grenadeIcon;
        private readonly Image health,mana;
        private readonly HealthBarWarning healthWarning;
        private readonly Button[] weapons;
        private readonly string[] ids;
        private readonly HashSet<string> missing=new();
        private ActorState displayedActor;
        private readonly InventoryArtwork artwork=new();
        private readonly Image[] shortcutIcons;
        // Explicit presentation bounds; consumers must not reserve the stretched
        // CombatHud root or search the scene for its visible children every frame.
        public IReadOnlyList<RectTransform> LayoutReservations {get;}
        public CombatHud(Transform parent,Action<string> equip,Action<string> equipItem=null,Action<string> activateShortcut=null)
        {
            var prefab=Resources.Load<CombatHudView>("Presentation/CombatHud");
            if(prefab==null) throw new InvalidOperationException("Нет префаба HUD Presentation/CombatHud.");
            prefab.Validate();var view=UnityEngine.Object.Instantiate(prefab,parent,false);
            vitals=view.Vitals;loadout=view.Loadout;name=view.CharacterName;healthText=view.HealthText;manaText=view.ManaText;
            ammunition=view.Ammunition;health=view.Health;mana=view.Mana;weapons=view.Weapons;ids=view.WeaponIds;
            grenades=view.Grenades;grenadeIcon=view.GrenadeIcon;
            LayoutReservations=Array.AsReadOnly(new[] {(RectTransform)vitals.transform,(RectTransform)loadout.transform,
                name.rectTransform,healthText.rectTransform,manaText.rectTransform});
            healthWarning=new HealthBarWarning(health.sprite,view.HealthWarningFrames);
            shortcutIcons=new Image[weapons.Length];
            for(int i=0;i<weapons.Length;i++)
            {
                string id=i<ids.Length?ids[i]:null,slot=ItemShortcuts.Keys[i];
                shortcutIcons[i]=weapons[i].transform.Find("Иконка предмета")?.GetComponent<Image>() ??
                    throw new InvalidOperationException($"Нет иконки быстрого слота {slot}.");
                weapons[i].onClick.AddListener(()=>
                {
                    if(displayedActor?.weaponInventoryVersion!=1) {if(id!=null) equip(id);return;}
                    var item=ItemShortcuts.Get(displayedActor,slot);
                    if(item==null) return;
                    if(activateShortcut!=null) {activateShortcut(slot);return;}
                    if(!WeaponInventory.IsEquippable(item))
                    {Debug.LogWarning($"HUD: не подключено действие предмета, slot={slot}, item={item.id}, definition={item.definition}.");return;}
                    if(equipItem==null) throw new InvalidOperationException("Не подключён быстрый выбор экземпляра оружия.");
                    equipItem(item.id);
                });
            }
        }
        public void Refresh(ActorState actor,bool visible,float? reloadRemaining=null)
        {
            displayedActor=actor;
            vitals.SetActive(visible);loadout.SetActive(visible);
            healthWarning.Refresh(health,actor==null?0:Mathf.Clamp01(actor.health/100),visible,Time.unscaledTime);
            if(actor==null) return;
            if(actor.weaponInventoryVersion!=1 && missing.Add("legacy:"+actor.id))
                Debug.LogWarning($"HUD показывает диагностические определения старого инвентаря: actor={actor.id}, inventoryVersion={actor.weaponInventoryVersion}. Для игры требуется миграция экземпляров.");
            int grenadeCount=InventoryJournal.Count(actor,"grenade");
            grenades.text=$"G · Гранаты: {grenadeCount}";
            var grenadeColor=grenadeCount>0 && !actor.down?new Color(.65f,1,.75f):new Color(.4f,.5f,.44f);
            grenades.color=grenadeColor;grenadeIcon.color=grenadeCount>0 && !actor.down?Color.white:new Color(.5f,.5f,.5f);
            name.text=actor.down?"Без сознания · нужна помощь":actor.character;
            health.fillAmount=Mathf.Clamp01(actor.health/100);mana.fillAmount=Mathf.Clamp01(actor.mana/100);
            healthText.text=$"Здоровье  {Mathf.CeilToInt(actor.health)} / 100";manaText.text=$"Мана  {actor.mana:0} / 100";
            bool ordinaryHeld=HeldItemSelection.HasSelection(actor);
            var heldItem=HeldItemSelection.Selected(actor);
            string activeItem=ordinaryHeld?heldItem?.id:WeaponInventory.Selected(actor)?.id;
            for(int i=0;i<weapons.Length;i++)
            {
                var item=actor.weaponInventoryVersion==1?ItemShortcuts.Get(actor,ItemShortcuts.Keys[i]):null;
                bool instances=actor.weaponInventoryVersion==1;
                string legacy=i<ids.Length?ids[i]:null;
                OriginalButtonStyle.SetSelected(weapons[i],instances?item!=null && item.id==activeItem:legacy!=null && legacy==actor.weapon);
                weapons[i].interactable=!actor.down && (instances?item!=null && InventoryAvailability.Count(actor,item)>0:legacy!=null);
                artwork.Set(shortcutIcons[i],instances?item?.definition:legacy);
            }
            if(ordinaryHeld)
            {
                int available=heldItem==null?0:InventoryAvailability.Count(actor,heldItem);
                ammunition.text=heldItem==null?"Предмет закончился · выберите другой":available==0?"Вещь зарезервирована для передачи…":
                    $"В копытах: {available}\nЛКМ — бросить 1 · Shift+ЛКМ — стопку";
                return;
            }
            if(string.IsNullOrEmpty(actor.weapon)) {ammunition.text="Оружие не выбрано";return;}
            var definition=WeaponCatalog.Get(actor.weapon);
            if(actor.weapon=="grenade")
            {
                var stack=WeaponInventory.Selected(actor);
                ammunition.text=$"В стопке: {(stack==null?0:InventoryAvailability.Count(actor,stack))} · ЛКМ — бросить";return;
            }
            if(string.IsNullOrEmpty(definition.ammo)) {ammunition.text=actor.weapon=="spear"?"Копьё · урон зависит от скорости":"Лом · ближний бой";return;}
            var magazine=WeaponAmmunition.Magazine(actor,actor.weapon);
            if(magazine==null)
            {
                if(missing.Add(actor.id+":"+actor.weapon)) Debug.LogWarning($"Нет магазина: actor={actor.id}, weapon={actor.weapon}.");
                ammunition.text="Ошибка данных магазина — см. журнал.";return;
            }
            int reserve=InventoryJournal.Count(actor,definition.ammo);
            string action=reloadRemaining.HasValue?$"Перезарядка · {Mathf.Max(0,reloadRemaining.Value):0.0} с":
                magazine.rounds<definition.ammoPerShot?(reserve>0?"Магазин пуст · R":"Нет боеприпасов"):
                magazine.rounds<definition.magazineSize && reserve>0?"R — перезарядить":"";
            ammunition.text=$"{magazine.rounds} / {definition.magazineSize}    Запас: {reserve}"+(action.Length>0?$"\n{action}":"");
        }
    }
}

