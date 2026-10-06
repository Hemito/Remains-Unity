using System;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;
namespace Remains.Editor
{
    public static class CombatHudPrefabChecks
    {
        public static void Run()
        {
            var root=new GameObject("Проверка HUD",typeof(RectTransform));
            try
            {
                string selected=null;int calls=0;
                var hud=new CombatHud(root.transform,id=>{selected=id;calls++;},null,slot=>{selected=slot;calls++;});
                var view=root.GetComponentInChildren<CombatHudView>();
                var normal=view.Health.sprite;
                var actor=new ActorState {id="hud-check",character="Проверка",weapon="pistol",health=19,mana=36};WeaponAmmunition.Initialize(actor,true);
                hud.Refresh(actor,true);
                Require(view.Grenades.text=="G · Гранаты: 0","Пустой запас гранат не показан");
                actor.inventory.Add(SharedInventory.Create("grenade",3));hud.Refresh(actor,true);
                Require(view.Grenades.text=="G · Гранаты: 3","HUD не прочитал запас гранат из инвентаря");
                InventoryJournal.Consume(actor,"grenade",1);hud.Refresh(actor,true);
                Require(view.Grenades.text=="G · Гранаты: 2","Подтверждённый расход не обновил HUD");
                actor.inventoryReservations[actor.inventory.Find(item=>item.definition=="grenade").id]=1;hud.Refresh(actor,true);
                Require(view.Grenades.text=="G · Гранаты: 1","Зарезервированная граната ошибочно показана доступной");
                actor.inventoryReservations.Clear();
                Require(Mathf.Approximately(view.Health.fillAmount,.19f) && Mathf.Approximately(view.Mana.fillAmount,.36f),"Неверное заполнение шкал");
                Require(Mathf.Approximately(view.Health.rectTransform.rect.width/((RectTransform)view.Health.transform.parent).rect.width,.19f) &&
                    Mathf.Approximately(view.Mana.rectTransform.rect.width/((RectTransform)view.Mana.transform.parent).rect.width,.36f),
                    "Фактическая длина шкал не соответствует здоровью/мане");
                Require(view.Health.sprite!=normal,"Не включилось предупреждение здоровья");
                for(int i=0;i<view.WeaponIds.Length;i++)
                {view.Weapons[i].onClick.Invoke();Require(selected==view.WeaponIds[i] && calls==i+1,"Кнопка выбирает неверное оружие или дублирует действие");}
                actor.health=20;hud.Refresh(actor,true);Require(view.Health.sprite==normal,"Лечение не вернуло обычную шкалу");
                actor.health=.4f;hud.Refresh(actor,true);
                Require(view.HealthText.text=="1/100" && Mathf.Approximately(view.Health.fillAmount,.004f) &&
                    actor.health==.4f && !actor.down,"Положительное дробное здоровье показано нулём либо округлено в симуляции");
                actor.health=19.1f;hud.Refresh(actor,true);
                Require(view.HealthText.text=="20/100" && view.Health.sprite!=normal,
                    "Округлённая подпись отключила предупреждение при фактическом здоровье ниже 20%");
                actor.health=0;hud.Refresh(actor,true);
                Require(view.HealthText.text=="0/100" && view.Health.fillAmount==0,"Нулевое здоровье показано положительным");
                var frame=view.Health.transform.parent.Find("Рамка здоровья") as RectTransform;
                Require(frame!=null && frame.rect.width>=((RectTransform)view.Health.transform.parent).rect.width && view.Health.rectTransform.rect.width==0,
                    "Рамка максимального здоровья исчезла или сжалась вместе с пустой заливкой");
                actor.down=true;hud.Refresh(actor,true);
                foreach(var button in view.Weapons) Require(!button.interactable,"Оружие доступно без сознания");
                actor.down=false;actor.weaponInventoryVersion=1;actor.weapon=null;
                var ammo=InventoryStacks.Add(actor,SharedInventory.Create("p10",20));
                foreach(var key in ItemShortcuts.Keys) Require(ItemShortcuts.Assign(actor,key,ammo.id,out _),"Нельзя назначить предмет на "+key);
                hud.Refresh(actor,true);int before=calls;
                for(int i=0;i<view.Weapons.Length;i++)
                {
                    Require(view.Weapons[i].interactable,"Нет доступного предмета на ["+ItemShortcuts.Keys[i]+"]");
                    view.Weapons[i].onClick.Invoke();Require(selected==ItemShortcuts.Keys[i] && calls==before+i+1,"HUD подменяет или дублирует универсальное действие");
                }
                hud.Refresh(actor,false);Require(!view.Vitals.activeSelf && !view.Loadout.activeSelf,"HUD не скрылся");
                Require(Resources.Load<CombatHudView>("Presentation/CombatHud").Health.fillAmount==1,"Обновление изменило исходный префаб");
                Debug.Log("Префаб HUD: PASS — показатели, предупреждение/лечение, кнопки, бессознательное состояние, видимость.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        public static void RenderAndCheck() {WeaponCatalog.Load();Run();CombatHudRender.Render();HealthWarningRender.Render();}
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
