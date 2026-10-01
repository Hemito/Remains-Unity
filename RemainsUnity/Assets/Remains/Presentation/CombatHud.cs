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
        private readonly Button[] weapons=new Button[4];
        private readonly string[] ids={"spear","crowbar","pistol","energy"};
        private readonly HashSet<string> missing=new();
        public CombatHud(Transform parent,HudElements ui,InventoryArtwork artwork,Action<string> equip)
        {
            vitals=ui.Panel(parent,"Состояние персонажа",new Vector2(18,-16),new Vector2(310,115));
            vitals.GetComponent<Image>().raycastTarget=false;
            name=ui.Label(vitals.transform,"",new Vector2(12,-8),new Vector2(286,25));
            health=Bar(ui,vitals.transform,"health",new Vector2(12,-40),new Vector2(286,25));
            mana=Bar(ui,vitals.transform,"mana",new Vector2(12,-78),new Vector2(286,20));
            healthText=ui.Label(vitals.transform,"",new Vector2(16,-42),new Vector2(275,24));
            manaText=ui.Label(vitals.transform,"",new Vector2(16,-78),new Vector2(275,24));
            healthText.color=manaText.color=Color.white;healthText.fontSize=manaText.fontSize=16;
            loadout=ui.Panel(parent,"Быстрое оружие",new Vector2(18,-600),new Vector2(450,104));
            var anchor=loadout.GetComponent<RectTransform>();anchor.anchorMin=anchor.anchorMax=new Vector2(0,0);anchor.anchoredPosition=new Vector2(18,120);
            for(int i=0;i<4;i++)
            {
                string id=ids[i];weapons[i]=ui.Button(loadout.transform,(i+1).ToString(),new Vector2(8+i*110,-8),new Vector2(104,58),()=>equip(id));
                var label=weapons[i].GetComponentInChildren<Text>();label.alignment=TextAnchor.UpperLeft;label.fontSize=14;
                artwork.Create(weapons[i].transform,new Vector2(22,-10),new Vector2(75,37),id);
            }
            ammunition=ui.Label(loadout.transform,"",new Vector2(12,-73),new Vector2(426,24));ammunition.fontSize=16;
        }
        private static Image Bar(HudElements ui,Transform parent,string id,Vector2 position,Vector2 size)
        {
            var back=ui.Panel(parent,"Шкала "+id,position,size);back.GetComponent<Image>().color=new Color(.03f,.04f,.03f,1);
            var obj=ui.Panel(back.transform,"Заполнение",Vector2.zero,size);var image=obj.GetComponent<Image>();image.raycastTarget=false;
            var texture=Resources.Load<Texture2D>("Original/Hud/"+id);
            if(texture==null) throw new InvalidOperationException("Нет оригинальной шкалы: "+id);
            image.sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));image.color=Color.white;
            image.type=Image.Type.Filled;image.fillMethod=Image.FillMethod.Horizontal;return image;
        }
        public void Refresh(ActorState actor,bool visible)
        {
            vitals.SetActive(visible);loadout.SetActive(visible);if(actor==null) return;
            name.text=actor.down?"Без сознания · нужна помощь":actor.character;
            health.fillAmount=Mathf.Clamp01(actor.health/100);mana.fillAmount=Mathf.Clamp01(actor.mana/100);
            healthText.text=$"Здоровье  {actor.health:0} / 100";manaText.text=$"Мана  {actor.mana:0} / 100";
            for(int i=0;i<weapons.Length;i++)
            {
                var colors=weapons[i].colors;colors.normalColor=ids[i]==actor.weapon?new Color(.45f,.8f,.55f,1):new Color(.20f,.25f,.23f,.85f);weapons[i].colors=colors;
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
            ammunition.text=$"{magazine.rounds} / {definition.magazineSize}    Запас: {InventoryJournal.Count(actor,definition.ammo)}    R — перезарядить";
        }
    }
}
