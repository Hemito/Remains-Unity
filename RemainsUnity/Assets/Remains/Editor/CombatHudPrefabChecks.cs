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
                var hud=new CombatHud(root.transform,id=>{selected=id;calls++;});
                var view=root.GetComponentInChildren<CombatHudView>();
                var normal=view.Health.sprite;
                var actor=new ActorState {id="hud-check",character="Проверка",weapon="pistol",health=19,mana=36};WeaponAmmunition.Initialize(actor,true);
                hud.Refresh(actor,true);
                Require(Mathf.Approximately(view.Health.fillAmount,.19f) && Mathf.Approximately(view.Mana.fillAmount,.36f),"Неверное заполнение шкал");
                Require(view.Health.sprite!=normal,"Не включилось предупреждение здоровья");
                for(int i=0;i<view.Weapons.Length;i++)
                {view.Weapons[i].onClick.Invoke();Require(selected==view.WeaponIds[i] && calls==i+1,"Кнопка выбирает неверное оружие или дублирует действие");}
                actor.health=20;hud.Refresh(actor,true);Require(view.Health.sprite==normal,"Лечение не вернуло обычную шкалу");
                actor.down=true;hud.Refresh(actor,true);
                foreach(var button in view.Weapons) Require(!button.interactable,"Оружие доступно без сознания");
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
