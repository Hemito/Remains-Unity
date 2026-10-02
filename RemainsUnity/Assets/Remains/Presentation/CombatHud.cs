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
        private readonly Text name,healthText,manaText,ammunition;
        private readonly Image health,mana;
        private readonly HealthBarWarning healthWarning;
        private readonly Button[] weapons;
        private readonly string[] ids;
        private readonly HashSet<string> missing=new();
        public CombatHud(Transform parent,Action<string> equip)
        {
            var prefab=Resources.Load<CombatHudView>("Presentation/CombatHud");
            if(prefab==null) throw new InvalidOperationException("Нет префаба HUD Presentation/CombatHud.");
            prefab.Validate();var view=UnityEngine.Object.Instantiate(prefab,parent,false);
            vitals=view.Vitals;loadout=view.Loadout;name=view.CharacterName;healthText=view.HealthText;manaText=view.ManaText;
            ammunition=view.Ammunition;health=view.Health;mana=view.Mana;weapons=view.Weapons;ids=view.WeaponIds;
            healthWarning=new HealthBarWarning(health.sprite,view.HealthWarningFrames);
            for(int i=0;i<weapons.Length;i++) {string id=ids[i];weapons[i].onClick.AddListener(()=>equip(id));}
        }
        public void Refresh(ActorState actor,bool visible,float? reloadRemaining=null)
        {
            vitals.SetActive(visible);loadout.SetActive(visible);
            healthWarning.Refresh(health,actor==null?0:Mathf.Clamp01(actor.health/100),visible,Time.unscaledTime);
            if(actor==null) return;
            name.text=actor.down?"Без сознания · нужна помощь":actor.character;
            health.fillAmount=Mathf.Clamp01(actor.health/100);mana.fillAmount=Mathf.Clamp01(actor.mana/100);
            healthText.text=$"Здоровье  {actor.health:0} / 100";manaText.text=$"Мана  {actor.mana:0} / 100";
            for(int i=0;i<weapons.Length;i++)
            {
                OriginalButtonStyle.SetSelected(weapons[i],ids[i]==actor.weapon);
                weapons[i].interactable=!actor.down;
            }
            var definition=WeaponCatalog.Get(actor.weapon);
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

